using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using GSADUs.Revit.Addin.Workflows.Rvt;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GSADUs.Revit.Addin
{
    public static class AuditAndCurate
    {
        // Build and cache a DeletePlan for re-use during batch processing.
        // Only physical model elements and view-specific annotations, excluding blacklisted categories.
        public static DeletePlan BuildAndCacheDeletePlan(Document doc)
        {
            var plan = ExportCleanup.BuildDeletePlan(doc, new CleanupOptions());
            DeletePlanCache.Store(doc, plan);
            return plan;
        }

        public sealed class AuditOptions
        {
            public double BbInflationOffset { get; set; } = 1.0;
            public bool DrawDebugRectangles { get; set; } = false; // reserved
        }

        public sealed class SetChange
        {
            public string SetName { get; set; } = string.Empty;
            public int BeforeCount { get; set; }
            public int AfterCount { get; set; }
            public int AddedCount { get; set; }
            public int RemovedCount { get; set; }
            public bool WasAmbiguous { get; set; }
        }

        public sealed class AuditSummary
        {
            public IReadOnlyList<string> ValidSets { get; set; } = Array.Empty<string>();
            public IReadOnlyList<string> IgnoredSets { get; set; } = Array.Empty<string>();
            public IReadOnlyList<string> AmbiguousSets { get; set; } = Array.Empty<string>();
            public IReadOnlyList<SetChange> Changes { get; set; } = Array.Empty<SetChange>();
            public bool AnyChanges => Changes.Any(c => c.AddedCount > 0 || c.RemovedCount > 0);
        }

        // Global compute retained for back-compat. (Legacy Run wrapper removed.)
        public static CuratePlan Compute(Document doc, View activeView, AuditOptions? options = null)
            => Compute(doc, activeView, (IEnumerable<string>?)null, options);

        // Restricted/global overload.
        public static CuratePlan Compute(Document doc, View activeView, IEnumerable<string>? restrictIdsOrNames, AuditOptions? options = null)
        {
            options ??= new AuditOptions();

            HashSet<string>? restrictTokens = null;
            if (restrictIdsOrNames != null)
            {
                restrictTokens = new HashSet<string>(restrictIdsOrNames.Where(t => !string.IsNullOrWhiteSpace(t)), StringComparer.OrdinalIgnoreCase);
                if (restrictTokens.Count == 0)
                    return new CuratePlan { ValidSets = Array.Empty<string>(), IgnoredSets = Array.Empty<string>(), AmbiguousSets = Array.Empty<string>(), Deltas = Array.Empty<SetDelta>() };
            }

            var settings = AppSettingsStore.Load();
            var proxyDistance = settings.SelectionProxyDistance;
            if (proxyDistance < 0) proxyDistance = 0;
            options.BbInflationOffset = proxyDistance;

            var catsViews = AuditComputeCache.GetOrBuild(doc, settings);
            var seedCatIds = catsViews.Seed;          // HashSet<ElementId>
            var blacklistCatIds = catsViews.Blacklist; // HashSet<ElementId>

            // (A) Enforce blacklist against Seed & Proxy category collections (always-on)
            if (blacklistCatIds.Count > 0)
            {
                foreach (var bid in blacklistCatIds.ToList())
                {
                    try { seedCatIds.Remove(bid); } catch { }
                    try { if (catsViews.Proxy.Contains(bid)) catsViews.Proxy.Remove(bid); } catch { }
                }
            }

            // Re-seed with defaults only if all seeds were removed (and defaults not blacklisted)
            if (seedCatIds.Count == 0)
            {
                try { var c = Category.GetCategory(doc, BuiltInCategory.OST_Walls); if (c != null && !blacklistCatIds.Contains(c.Id)) seedCatIds.Add(c.Id); } catch { }
                try { var c = Category.GetCategory(doc, BuiltInCategory.OST_Floors); if (c != null && !blacklistCatIds.Contains(c.Id)) seedCatIds.Add(c.Id); } catch { }
                try { var c = Category.GetCategory(doc, BuiltInCategory.OST_Roofs); if (c != null && !blacklistCatIds.Contains(c.Id)) seedCatIds.Add(c.Id); } catch { }
            }

            var selectionSets = new FilteredElementCollector(doc)
                .OfClass(typeof(SelectionFilterElement))
                .Cast<SelectionFilterElement>()
                .ToList();

            var validSets = new List<SelectionFilterElement>();
            var ignoredSets = new List<string>();
            var setElements = new Dictionary<string, List<Element>>();
            var setRawIds = new Dictionary<string, HashSet<ElementId>>();

            foreach (var sset in selectionSets)
            {
                if (restrictTokens != null &&
                    !restrictTokens.Contains(sset.UniqueId ?? string.Empty) &&
                    !restrictTokens.Contains(sset.Name ?? string.Empty))
                {
                    continue;
                }

                try
                {
                    var rawIds = sset.GetElementIds();
                    var allElems = new List<Element>();
                    if (rawIds != null && rawIds.Count > 0)
                    {
                        foreach (var id in rawIds)
                        {
                            var e = doc.GetElement(id);
                            if (e == null) continue;
                            var cat = e.Category;
                            if (cat != null)
                            {
                                try { if (blacklistCatIds.Contains(cat.Id)) continue; } catch { }
                            }
                            allElems.Add(e);
                        }
                    }

                    bool hasSeed = allElems.Any(e => e.Category != null && seedCatIds.Contains(e.Category.Id));
                    if (hasSeed)
                    {
                        validSets.Add(sset);
                        setElements[sset.Name] = allElems;
                        setRawIds[sset.Name] = new HashSet<ElementId>(rawIds ?? new List<ElementId>());
                    }
                    else if (restrictTokens == null || restrictTokens.Contains(sset.UniqueId ?? string.Empty) || restrictTokens.Contains(sset.Name ?? string.Empty))
                    {
                        ignoredSets.Add(sset.Name);
                    }
                }
                catch
                {
                    ignoredSets.Add(sset.Name);
                }
            }

            if (validSets.Count == 0)
            {
                return new CuratePlan { ValidSets = Array.Empty<string>(), IgnoredSets = ignoredSets, AmbiguousSets = Array.Empty<string>(), Deltas = Array.Empty<SetDelta>() };
            }

            var setBbs = new Dictionary<string, BoundingBoxXYZ>();
            var setIbbs = new Dictionary<string, BoundingBoxXYZ>();

            foreach (var sset in validSets)
            {
                var sname = sset.Name;
                var elems = setElements[sname]
                    .Where(e => e.Category != null && !blacklistCatIds.Contains(e.Category.Id))
                    .Where(e => e.Category != null && seedCatIds.Contains(e.Category.Id))
                    .ToList();

                if (elems.Count == 0)
                    continue;

                bool any = false;
                double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
                double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;

                foreach (var e in elems)
                {
                    var bb = GetBoundingBox(e, activeView);
                    if (bb == null) continue;
                    any = true;
                    minX = Math.Min(minX, bb.Min.X);
                    minY = Math.Min(minY, bb.Min.Y);
                    minZ = Math.Min(minZ, bb.Min.Z);
                    maxX = Math.Max(maxX, bb.Max.X);
                    maxY = Math.Max(maxY, bb.Max.Y);
                    maxZ = Math.Max(maxZ, bb.Max.Z);
                }

                if (!any) continue;

                var bbU = new BoundingBoxXYZ { Min = new XYZ(minX, minY, minZ), Max = new XYZ(maxX, maxY, maxZ) };
                var ibb = Inflate(bbU, options.BbInflationOffset);
                setBbs[sname] = bbU;
                setIbbs[sname] = ibb;
            }

            var ambiguousSets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var elementSets = new Dictionary<int, List<string>>();
            var ambiguousElements = new HashSet<int>();
            bool multiSetScope = validSets.Count > 1;

            if (multiSetScope)
            {
                foreach (var sset in validSets)
                {
                    foreach (var e in setElements.GetValueOrDefault(sset.Name, new List<Element>()))
                    {
                        var key = ToKey(e.Id);
                        if (key == null) continue;
                        if (!elementSets.TryGetValue(key.Value, out var lst)) { lst = new List<string>(); elementSets[key.Value] = lst; }
                        lst.Add(sset.Name);
                    }
                }
                foreach (var kv in elementSets) if (kv.Value.Count > 1) ambiguousElements.Add(kv.Key);

                var ibbPairs = setIbbs.ToList();
                for (int i = 0; i < ibbPairs.Count; i++)
                    for (int j = i + 1; j < ibbPairs.Count; j++)
                        if (Intersects(ibbPairs[i].Value, ibbPairs[j].Value)) { ambiguousSets.Add(ibbPairs[i].Key); ambiguousSets.Add(ibbPairs[j].Key); }

                foreach (var sset in validSets)
                {
                    var sname = sset.Name;
                    var ids = new HashSet<int>(setElements.GetValueOrDefault(sname, new List<Element>())
                        .Select(e => ToKey(e.Id))
                        .Where(k => k.HasValue)
                        .Select(k => k!.Value));
                    if (ids.Any(id => ambiguousElements.Contains(id))) ambiguousSets.Add(sname);
                }
            }

            var updatesBySet = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
            var removalsBySet = new Dictionary<string, HashSet<int>>();
            var allMemberIds = multiSetScope ? new HashSet<int>(elementSets.Keys) : new HashSet<int>();

            foreach (var sset in validSets)
            {
                var sname = sset.Name;
                if (!setIbbs.TryGetValue(sname, out var ibb)) continue;
                var outline = new Outline(ibb.Min, ibb.Max);
                var bbFilter = new BoundingBoxIntersectsFilter(outline);
                var seenLocal = new HashSet<int>();

                var fecDoc = new FilteredElementCollector(doc).WherePasses(bbFilter).WhereElementIsNotElementType();
                foreach (var e in fecDoc) TryQueueUpdate(e);

                if (catsViews.Proxy.Count > 0)
                {
                    foreach (var vid in catsViews.ViewIds ?? Enumerable.Empty<ElementId>())
                    {
                        try
                        {
                            var fecViewHits = new FilteredElementCollector(doc, vid)
                                .WherePasses(bbFilter)
                                .WhereElementIsNotElementType();

                            foreach (var e in fecViewHits)
                            {
                                var cat = e.Category; if (cat == null) continue;
                                if (!catsViews.Proxy.Contains(cat.Id)) continue;
                                TryQueueUpdate(e);
                            }
                        }
                        catch { }
                    }
                }

                void TryQueueUpdate(Element e)
                {
                    if (e == null) return;
                    var idKey = ToKey(e.Id); if (idKey == null) return;
                    if (multiSetScope && allMemberIds.Contains(idKey.Value)) return;
                    if (seenLocal.Contains(idKey.Value)) return;
                    var cat = e.Category; if (cat == null) return;
                    if (catsViews.Blacklist.Contains(cat.Id)) return; // still guarded
                    if (cat.CategoryType != CategoryType.Model && cat.CategoryType != CategoryType.Annotation) return;
                    if (catsViews.Proxy.Count > 0 && !catsViews.Proxy.Contains(cat.Id)) return;
                    if (!updatesBySet.TryGetValue(sname, out var hs)) { hs = new HashSet<int>(); updatesBySet[sname] = hs; }
                    hs.Add(idKey.Value); seenLocal.Add(idKey.Value);
                }
            }

            // (B) Enforce blacklist removals in all existing sets (elements already present)
            foreach (var sset in validSets)
            {
                var sname = sset.Name;
                if (!setRawIds.TryGetValue(sname, out var rawIds)) continue;
                foreach (var rid in rawIds)
                {
                    var el = doc.GetElement(rid); if (el == null) continue;
                    var cat = el.Category; if (cat == null) continue;
                    if (!blacklistCatIds.Contains(cat.Id)) continue;
                    var key = ToKey(rid); if (!key.HasValue) continue;
                    if (!removalsBySet.TryGetValue(sname, out var hs)) { hs = new HashSet<int>(); removalsBySet[sname] = hs; }
                    hs.Add(key.Value);
                }
            }

            var deltas = new List<SetDelta>();
            foreach (var sset in validSets)
            {
                var sname = sset.Name;
                var original = new HashSet<int>(setRawIds.GetValueOrDefault(sname, new HashSet<ElementId>())
                    .Select(id => ToKey(id))
                    .Where(k => k.HasValue)
                    .Select(k => k!.Value));

                var unresolved = new HashSet<int>(original.Where(i => doc.GetElement(new ElementId(i)) == null));

                var current = new HashSet<int>(original);
                current.ExceptWith(unresolved);
                if (removalsBySet.TryGetValue(sname, out var rms)) current.ExceptWith(rms); // blacklist removals
                if (updatesBySet.TryGetValue(sname, out var ups)) current.UnionWith(ups);

                var added = new HashSet<int>(current); added.ExceptWith(original);
                var removed = new HashSet<int>(original); removed.ExceptWith(current);

                var detailsLines = new List<string>();
                (string Cat, string Fam, string Typ) GetInfo(Element el)
                {
                    string cat = el?.Category?.Name ?? "(No Category)";
                    string fam = (el as FamilyInstance)?.Symbol?.Family?.Name
                                 ?? (el as ElementType)?.FamilyName
                                 ?? (el is ElementType et ? et.Name : (el is FamilyInstance fi ? fi.Symbol?.Name : el?.Name))
                                 ?? el?.Name ?? "";
                    string typ = (el as FamilyInstance)?.Symbol?.Name
                                 ?? (el as ElementType)?.Name
                                 ?? el?.Name ?? "";
                    return (cat, fam, typ);
                }

                var addedEls = new List<Element>();
                foreach (var idInt in added) { try { var el = doc.GetElement(new ElementId(idInt)); if (el != null) addedEls.Add(el); } catch { } }
                var removedEls = new List<(Element? el, int id)>();
                foreach (var idInt in removed) { try { var el = doc.GetElement(new ElementId(idInt)); removedEls.Add((el, idInt)); } catch { removedEls.Add((null, idInt)); } }

                var addGroups = addedEls.GroupBy(el => GetInfo(el)).Select(g => $"+{g.Count()} {g.Key.Cat} - {g.Key.Fam} - {g.Key.Typ}");
                var remGroupsKnown = removedEls.Where(t => t.el != null).GroupBy(t => GetInfo(t.el!)).Select(g => $"-{g.Count()} {g.Key.Cat} - {g.Key.Fam} - {g.Key.Typ}");
                int unknownRem = removedEls.Count(t => t.el == null);
                detailsLines.AddRange(addGroups);
                detailsLines.AddRange(remGroupsKnown);
                if (unknownRem > 0) detailsLines.Add($"-{unknownRem} (Deleted/Unknown)");

                deltas.Add(new SetDelta
                {
                    SetName = sname,
                    BeforeIds = original,
                    ToAdd = added,
                    ToRemove = removed,
                    AfterIds = current,
                    WasAmbiguous = ambiguousSets.Contains(sname),
                    Details = string.Join("\n", detailsLines),
                    FilterUniqueId = sset.UniqueId
                });
            }

            // Ambiguity report dialog (unchanged logic except enforced blacklist already applied)
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("AMBIGUITY REPORT");
                sb.AppendLine("----------------");

                var names = setIbbs.Keys.ToList();
                int overlapCount = 0;
                for (int i = 0; i < names.Count; i++)
                {
                    var ni = names[i];
                    if (!ambiguousSets.Contains(ni)) continue;
                    var bi = setIbbs[ni];
                    if (bi == null) continue;

                    for (int j = i + 1; j < names.Count; j++)
                    {
                        var nj = names[j];
                        if (!ambiguousSets.Contains(nj)) continue;
                        var bj = setIbbs[nj];
                        if (bj == null) continue;

                        if (!(bi.Max.X < bj.Min.X || bj.Max.X < bi.Min.X ||
                              bi.Max.Y < bj.Min.Y || bj.Max.Y < bi.Min.Y ||
                              bi.Max.Z < bj.Min.Z || bj.Max.Z < bi.Min.Z))
                        {
                            overlapCount++;
                            sb.AppendLine($"[Overlaps] {ni} ↔ {nj}");
                        }
                    }
                }
                sb.AppendLine($"IBB Overlap Pairs: {overlapCount}");

                var elemToSets = new Dictionary<ElementId, List<string>>();
                foreach (var kv in setElements)
                {
                    var setName = kv.Key;
                    foreach (var el in kv.Value)
                    {
                        if (el == null) continue;
                        var eid = el.Id;
                        if (!elemToSets.TryGetValue(eid, out var list)) { list = new List<string>(); elemToSets[eid] = list; }
                        list.Add(setName);
                    }
                }

                int sharedLogged = 0;
                const int sharedCap = 50;
                foreach (var kv in elemToSets)
                {
                    if (sharedLogged >= sharedCap) break;
                    var sets = kv.Value;
                    if (sets.Count <= 1) continue;

                    Element? el = null; try { el = doc.GetElement(kv.Key); } catch { }
                    var cat = el?.Category?.Name ?? "<NoCategory>";
                    var idVal = ToKey(kv.Key) ?? 0;
                    sb.AppendLine($"[SharedElement] {idVal} ({cat}) in: {string.Join(", ", sets)}");
                    sharedLogged++;
                }
                sb.AppendLine($"Shared Elements Logged: {sharedLogged}");

                TaskDialog.Show("Ambiguity Details", sb.ToString());
            }
            catch { }

            return new CuratePlan
            {
                ValidSets = validSets.Select(v => v.Name).OrderBy(n => n).ToList(),
                IgnoredSets = ignoredSets.OrderBy(n => n).ToList(),
                AmbiguousSets = ambiguousSets.OrderBy(n => n).ToList(),
                Deltas = deltas
            };
        }

        public static AuditSummary Apply(Document doc, CuratePlan plan)
        {
            var changes = new List<SetChange>();
            bool anyChange = false;

            var allFilters = new FilteredElementCollector(doc)
                .OfClass(typeof(SelectionFilterElement))
                .Cast<SelectionFilterElement>()
                .ToList();
            var byUid = allFilters.Where(f => !string.IsNullOrWhiteSpace(f.UniqueId))
                                  .ToDictionary(f => f.UniqueId, f => f, StringComparer.OrdinalIgnoreCase);
            var byName = allFilters.Where(f => !string.IsNullOrWhiteSpace(f.Name))
                                   .GroupBy(f => f.Name!, StringComparer.OrdinalIgnoreCase)
                                   .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            using (var tx = new Transaction(doc, "Audit: Curate Selection Sets"))
            {
                tx.Start();
                try
                {
                    foreach (var delta in plan.Deltas)
                    {
                        SelectionFilterElement? sset = null;
                        if (!string.IsNullOrWhiteSpace(delta.FilterUniqueId) && byUid.TryGetValue(delta.FilterUniqueId, out var byIdMatch))
                        {
                            sset = byIdMatch;
                        }
                        else if (!string.IsNullOrWhiteSpace(delta.SetName) && byName.TryGetValue(delta.SetName, out var byNameMatch))
                        {
                            sset = byNameMatch;
                        }

                        if (sset == null)
                        {
                            try { PerfLogger.Write("Curate.Apply.Skip", $"Missing set Name={delta.SetName};Uid={delta.FilterUniqueId}", System.TimeSpan.Zero); } catch { }
                            continue;
                        }

                        var original = delta.BeforeIds;
                        var current = delta.AfterIds;
                        if (!original.SetEquals(current))
                        {
                            anyChange = true;
                            var newList = new List<ElementId>(current.Select(i => new ElementId(i)));
                            try { sset.SetElementIds(newList); }
                            catch { }
                        }

                        changes.Add(new SetChange
                        {
                            SetName = delta.SetName,
                            BeforeCount = original.Count,
                            AfterCount = current.Count,
                            AddedCount = delta.ToAdd.Count,
                            RemovedCount = delta.ToRemove.Count,
                            WasAmbiguous = delta.WasAmbiguous
                        });
                    }

                    if (anyChange) tx.Commit(); else tx.RollBack();
                }
                catch
                {
                    try { tx.RollBack(); } catch { }
                    throw;
                }
            }

            try { if (anyChange) doc.Regenerate(); } catch { }

            return new AuditSummary
            {
                ValidSets = plan.ValidSets,
                IgnoredSets = plan.IgnoredSets,
                AmbiguousSets = plan.AmbiguousSets,
                Changes = changes
            };
        }

        public static void ReconcileWithModel(Document doc, CuratePlan plan, bool annotateMismatches = true)
        {
            if (doc == null || plan == null || plan.Deltas == null) return;
            try
            {
                var allFilters = new FilteredElementCollector(doc)
                    .OfClass(typeof(SelectionFilterElement))
                    .Cast<SelectionFilterElement>()
                    .ToList();
                var byUid = allFilters.Where(f => !string.IsNullOrWhiteSpace(f.UniqueId))
                                       .ToDictionary(f => f.UniqueId, f => f, StringComparer.OrdinalIgnoreCase);
                var byName = allFilters.Where(f => !string.IsNullOrWhiteSpace(f.Name))
                                        .GroupBy(f => f.Name!, StringComparer.OrdinalIgnoreCase)
                                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                foreach (var d in plan.Deltas)
                {
                    if (d == null) continue;
                    SelectionFilterElement? sset = null;

                    if (!string.IsNullOrWhiteSpace(d.FilterUniqueId) && byUid.TryGetValue(d.FilterUniqueId, out var uidMatch))
                    {
                        sset = uidMatch;
                    }
                    else if (!string.IsNullOrWhiteSpace(d.SetName) && byName.TryGetValue(d.SetName, out var nameMatch))
                    {
                        sset = nameMatch;
                        try { if (string.IsNullOrWhiteSpace(d.FilterUniqueId)) d.FilterUniqueId = sset.UniqueId; } catch { }
                    }

                    if (sset == null) continue;

                    HashSet<int> actual = new HashSet<int>();
                    try
                    {
                        foreach (var eid in sset.GetElementIds() ?? new List<ElementId>())
                        {
                            var k = ToKey(eid);
                            if (k.HasValue) actual.Add(k.Value);
                        }
                    }
                    catch { }

                    var expected = d.AfterIds ?? new HashSet<int>();
                    bool mismatch = !actual.SetEquals(expected);
                    if (mismatch && annotateMismatches)
                    {
                        var missing = new HashSet<int>(expected); missing.ExceptWith(actual);
                        var unexpected = new HashSet<int>(actual); unexpected.ExceptWith(expected);
                        var line = $"[ModelMismatch] Expected={expected.Count} Actual={actual.Count} Missing={missing.Count} Unexpected={unexpected.Count}";
                        if (!string.IsNullOrWhiteSpace(d.Details)) d.Details += "\n" + line; else d.Details = line;
                    }

                    d.BeforeIds = new HashSet<int>(actual);
                    d.AfterIds = new HashSet<int>(actual);
                    d.ToAdd.Clear();
                    d.ToRemove.Clear();
                }
            }
            catch { }
        }

        static BoundingBoxXYZ? GetBoundingBox(Element e, View v)
        {
            try
            {
                var bb = e.get_BoundingBox(v);
                if (bb != null) return bb;
            }
            catch { }
            try
            {
                return e.get_BoundingBox(null);
            }
            catch { return null; }
        }

        static BoundingBoxXYZ Inflate(BoundingBoxXYZ bb, double d)
        {
            return new BoundingBoxXYZ
            {
                Min = new XYZ(bb.Min.X - d, bb.Min.Y - d, bb.Min.Z - d),
                Max = new XYZ(bb.Max.X + d, bb.Max.Y + d, bb.Max.Z + d)
            };
        }

        static bool Intersects(BoundingBoxXYZ? a, BoundingBoxXYZ? b)
        {
            if (a == null || b == null) return false;
            return !(a.Max.X < b.Min.X || b.Max.X < a.Min.X ||
                     a.Max.Y < b.Min.Y || b.Max.Y < a.Min.Y ||
                     a.Max.Z < b.Min.Z || b.Max.Z < a.Min.Z);
        }

        static int? ToKey(ElementId id)
        {
            if (id == null) return null;
            try
            {
                var prop = id.GetType().GetProperty("IntegerValue");
                if (prop != null)
                {
                    var val = prop.GetValue(id);
                    if (val is int i) return i;
                    if (val is long l) return checked((int)l);
                }
            }
            catch { }
            try
            {
                var s = id.ToString();
                if (int.TryParse(s, out var i)) return i;
            }
            catch { }
            return null;
        }
    }
}
