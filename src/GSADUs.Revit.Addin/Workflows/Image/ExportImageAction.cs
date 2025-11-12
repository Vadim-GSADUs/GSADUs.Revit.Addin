using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GSADUs.Revit.Addin.Workflows.Image
{
    internal sealed class ExportImageAction : IExportAction
    {
        public string Id => "export-image"; // renamed from export-png (legacy id removed)
        public int Order => 600;
        public bool RequiresExternalClone => false;
        public bool IsEnabled(AppSettings app, BatchExportSettings request)
        {
            try { return request.ActionIds?.Any(a => string.Equals(a, Id, StringComparison.OrdinalIgnoreCase)) == true; } catch { return false; }
        }

        // Post-export normalization: Revit appends " - ViewType - ViewName" when exporting a single view via SetOfViews.
        // For SingleView scope we want the filename to match UI preview (baseNoExt+ext) without the suffix.
        private static void TryNormalizeExportedFile(string directory, string baseNoExt, string ext)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(baseNoExt) || string.IsNullOrWhiteSpace(ext)) return;
                if (!System.IO.Directory.Exists(directory)) return;
                var desired = System.IO.Path.Combine(directory, baseNoExt + ext);
                if (System.IO.File.Exists(desired)) return; // already normalized or name collision
                string prefix = baseNoExt + " - ";
                var candidates = System.IO.Directory.GetFiles(directory, baseNoExt + " -*" + ext, System.IO.SearchOption.TopDirectoryOnly)
                    .Where(f => System.IO.Path.GetFileName(f).StartsWith(prefix, StringComparison.Ordinal))
                    .ToList();
                if (candidates.Count != 1) return; // only normalize when exactly one candidate
                var src = candidates[0];
                try { System.IO.File.Move(src, desired); } catch { }
            }
            catch { }
        }

        // Minimal sanitization + token expansion (print-set branch only).
        private static string SanitizeFileComponent(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var s = raw.Trim();
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            s = s.Replace('/', '_').Replace('\\', '_');
            return s;
        }
        private static string ExpandTokens(string pattern, string setName)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return setName;
            var now = DateTime.Now;
            return pattern
                .Replace("{SetName}", SanitizeFileComponent(setName))
                .Replace("{Date}", now.ToString("yyyyMMdd"))
                .Replace("{Time}", now.ToString("HHmmss"));
        }
        private static string MapImageTypeToExt(ImageFileType type)
        {
            // Match UI preview extensions
            return type switch
            {
                ImageFileType.BMP => ".bmp",
                ImageFileType.TIFF => ".tiff",
                ImageFileType.PNG => ".png",
                _ => ".png"
            };
        }

        /// <summary>
        /// Builds sanitized base filename (no directory), aligned with rules:
        /// - Expands {SetName},{Date},{Time}
        /// - Replaces {ViewName} only if token is present
        /// - For multi-view (PrintSet) exports, if pattern lacks {ViewName} we auto-append sanitized view name
        /// - SingleView scope never auto-appends (only explicit token replacement)
        /// - Returns (baseNoExt, ext, fileNameWithExt)
        /// </summary>
        private (string baseNoExt, string ext, string fileName) BuildImageFileName(
            string pattern,
            string setName,
            ImageFileType type,
            string? viewNameOrNull,
            bool autoAppendViewNameIfMissing)
        {
            var expanded = ExpandTokens(pattern ?? "{SetName}", setName);
            var core = SanitizeFileComponent(System.IO.Path.GetFileNameWithoutExtension(expanded));

            // Replace token explicitly
            if (core.Contains("{ViewName}", StringComparison.Ordinal))
            {
                var vn = string.IsNullOrWhiteSpace(viewNameOrNull) ? string.Empty : SanitizeFileComponent(viewNameOrNull);
                core = core.Replace("{ViewName}", vn);
            }
            else if (autoAppendViewNameIfMissing && !string.IsNullOrWhiteSpace(viewNameOrNull))
            {
                // Auto append with space separator for clarity
                var vn = SanitizeFileComponent(viewNameOrNull);
                if (!string.IsNullOrWhiteSpace(vn))
                {
                    if (core.Length > 0) core = core + " " + vn; else core = vn;
                }
            }

            var baseNoExt = string.IsNullOrWhiteSpace(core) ? "export" : core;
            var ext = MapImageTypeToExt(type);
            var fileName = baseNoExt + ext;
            return (baseNoExt, ext, fileName);
        }

        // If list is non-empty only categories in it are considered for auto-crop.
        private static List<BoundingBoxXYZ> CollectVisiblePlanElementBoxes(ViewPlan plan, Document doc, IEnumerable<ElementId> setIds)
        {
            var boxes = new List<BoundingBoxXYZ>();
            if (plan == null || doc == null) return boxes;
            List<int>? whitelist = null; try { whitelist = AppSettingsStore.Load()?.ImageWhitelistCategoryIds; } catch { }
            if (whitelist != null && whitelist.Count == 0) whitelist = null; // empty -> include all

            HashSet<ElementId>? visible = null;
            try { visible = new HashSet<ElementId>(new FilteredElementCollector(doc, plan.Id).ToElementIds()); } catch { }

            foreach (var id in setIds)
            {
                if (visible != null && !visible.Contains(id)) continue;
                Element? el = null; try { el = doc.GetElement(id); } catch { }
                if (el == null) continue;

                bool allowed = true;
                try
                {
                    if (whitelist != null)
                    {
                        long raw = 0; try { raw = el.Category?.Id?.Value ?? 0; } catch { raw = 0; }
                        int catId = raw >= int.MinValue && raw <= int.MaxValue ? (int)raw : 0;
                        if (!whitelist.Contains(catId)) allowed = false;
                    }
                }
                catch { }
                if (!allowed) continue;

                BoundingBoxXYZ? bb = null; try { bb = el.get_BoundingBox(plan); } catch { }
                if (bb == null || bb.Min == null || bb.Max == null) continue;
                if (bb.Max.X - bb.Min.X < 1e-9 || bb.Max.Y - bb.Min.Y < 1e-9) continue;
                boxes.Add(new BoundingBoxXYZ { Min = bb.Min, Max = bb.Max, Enabled = bb.Enabled });
            }
            return boxes;
        }

        // Collect bounding boxes for 3D views (respecting visibility in that view)
        private static List<BoundingBoxXYZ> CollectVisible3DElementBoxes(View3D view3d, Document doc, IEnumerable<ElementId> setIds)
        {
            var boxes = new List<BoundingBoxXYZ>();
            if (view3d == null || doc == null) return boxes;
            List<int>? whitelist = null; try { whitelist = AppSettingsStore.Load()?.ImageWhitelistCategoryIds; } catch { }
            if (whitelist != null && whitelist.Count == 0) whitelist = null;

            HashSet<ElementId>? visible = null;
            try { visible = new HashSet<ElementId>(new FilteredElementCollector(doc, view3d.Id).ToElementIds()); } catch { }

            foreach (var id in setIds)
            {
                if (visible != null && !visible.Contains(id)) continue;
                Element? el = null; try { el = doc.GetElement(id); } catch { }
                if (el == null) continue;

                bool allowed = true;
                try
                {
                    if (whitelist != null)
                    {
                        long raw = 0; try { raw = el.Category?.Id?.Value ?? 0; } catch { raw = 0; }
                        int catId = raw >= int.MinValue && raw <= int.MaxValue ? (int)raw : 0;
                        if (!whitelist.Contains(catId)) allowed = false;
                    }
                }
                catch { }
                if (!allowed) continue;

                BoundingBoxXYZ? bb = null; try { bb = el.get_BoundingBox(view3d); } catch { }
                if (bb == null) { try { bb = el.get_BoundingBox(null); } catch { bb = null; } }
                if (bb == null || bb.Min == null || bb.Max == null) continue;
                if (Math.Abs(bb.Max.X - bb.Min.X) < 1e-9 || Math.Abs(bb.Max.Y - bb.Min.Y) < 1e-9) continue;
                boxes.Add(new BoundingBoxXYZ { Min = bb.Min, Max = bb.Max, Enabled = bb.Enabled });
            }
            return boxes;
        }

        private const double MinExtentFeet = 1.0 / 12.0; // 1 inch safeguard shared with plan auto-crop

        private static void EnsureMinExtent(ref double min, ref double max)
        {
            if (max <= min)
            {
                var mid = 0.5 * (min + max);
                var half = MinExtentFeet * 0.5;
                min = mid - half;
                max = mid + half;
                return;
            }

            if ((max - min) < MinExtentFeet)
            {
                var mid = 0.5 * (min + max);
                var half = MinExtentFeet * 0.5;
                min = mid - half;
                max = mid + half;
            }
        }

        private static BoundingBoxXYZ CloneWithOffset(BoundingBoxXYZ box, double offset)
        {
            if (box == null) return box;

            double minX = box.Min.X;
            double minY = box.Min.Y;
            double minZ = box.Min.Z;
            double maxX = box.Max.X;
            double maxY = box.Max.Y;
            double maxZ = box.Max.Z;

            if (Math.Abs(offset) > 0)
            {
                minX -= offset; minY -= offset; minZ -= offset;
                maxX += offset; maxY += offset; maxZ += offset;
            }

            EnsureMinExtent(ref minX, ref maxX);
            EnsureMinExtent(ref minY, ref maxY);
            EnsureMinExtent(ref minZ, ref maxZ);

            return new BoundingBoxXYZ
            {
                Min = new XYZ(minX, minY, minZ),
                Max = new XYZ(maxX, maxY, maxZ),
                Enabled = box.Enabled
            };
        }

        // Adjust a perspective camera by moving the eye along its forward vector so that
        // the entire 3D bounding box fits within the given horizontal FOV (and same vertical tolerance),
        // with an additional buffer percentage. Returns false if fitting fails.
        private static bool TryAdjustPerspectiveCamera(View3D v3, BoundingBoxXYZ targetBox, double heuristicFovDeg, double bufferPct)
        {
            try
            {
                if (v3 == null || targetBox == null) return false;
                if (!v3.IsPerspective) return false;
                if (heuristicFovDeg <= 0 || heuristicFovDeg >= 180) return false;

                var ori = v3.GetOrientation();
                if (ori == null) return false;

                var eye = ori.EyePosition;
                var forwardVec = ori.ForwardDirection;
                var upVec = ori.UpDirection ?? XYZ.BasisZ;
                if (forwardVec == null) return false;
                if (forwardVec.GetLength() < 1e-9) return false;
                if (upVec.GetLength() < 1e-9) upVec = XYZ.BasisZ;

                var forward = forwardVec.Normalize();
                // Build right from forward x up and re-orthonormalize basis
                var right = forward.CrossProduct(upVec);
                if (right.GetLength() < 1e-9)
                {
                    // Try to rebuild with world Z as hint
                    right = forward.CrossProduct(XYZ.BasisZ);
                }
                if (right.GetLength() < 1e-9) return false;
                right = right.Normalize();
                var up = right.CrossProduct(forward);
                if (up.GetLength() < 1e-9) up = upVec; // fallback to original up
                if (up.GetLength() < 1e-9) up = XYZ.BasisZ;
                up = up.Normalize();
                // Recompute right to ensure orthonormality
                right = forward.CrossProduct(up); if (right.GetLength() < 1e-9) return false; right = right.Normalize();

                // Center of target box in model XY; keep eye height (Z) constant
                var center = new XYZ(0.5 * (targetBox.Min.X + targetBox.Max.X), 0.5 * (targetBox.Min.Y + targetBox.Max.Y), eye.Z);

                // Build all 8 corners of the 3D bounding box
                var zMin = targetBox.Min.Z; var zMax = targetBox.Max.Z;
                var corners = new List<XYZ>
                {
                    new XYZ(targetBox.Min.X, targetBox.Min.Y, zMin),
                    new XYZ(targetBox.Min.X, targetBox.Max.Y, zMin),
                    new XYZ(targetBox.Max.X, targetBox.Min.Y, zMin),
                    new XYZ(targetBox.Max.X, targetBox.Max.Y, zMin),
                    new XYZ(targetBox.Min.X, targetBox.Min.Y, zMax),
                    new XYZ(targetBox.Min.X, targetBox.Max.Y, zMax),
                    new XYZ(targetBox.Max.X, targetBox.Min.Y, zMax),
                    new XYZ(targetBox.Max.X, targetBox.Max.Y, zMax)
                };

                // Tolerances from FOV and buffer
                double fovRad = heuristicFovDeg * Math.PI / 180.0;
                double halfFov = Math.Max(1e-6, fovRad * 0.5);
                double tanHalfH = Math.Tan(halfFov);
                if (double.IsNaN(tanHalfH) || double.IsInfinity(tanHalfH) || tanHalfH <= 0) return false;
                // Use same FOV for vertical tolerance (can be extended later)
                double tanHalfV = tanHalfH;
                double bufferFactor = 1.0 + (bufferPct / 100.0);
                if (bufferFactor <= 0) bufferFactor = 1.0;
                double limitH = tanHalfH * bufferFactor;
                double limitV = tanHalfV * bufferFactor;

                // Analytic minimal distance along forward that makes all corners fit.
                // For a corner with vector p from center, requirement is:
                // |p·right| <= limitH * (D + p·forward) and |p·up| <= limitV * (D + p·forward)
                // which gives D >= |p·right|/limitH - p·forward and D >= |p·up|/limitV - p·forward.
                double requiredD = 0.01; // feet
                for (int i = 0; i < corners.Count; i++)
                {
                    var p = corners[i] - center;
                    double pf = p.DotProduct(forward);
                    double pr = Math.Abs(p.DotProduct(right));
                    double pu = Math.Abs(p.DotProduct(up));
                    if (limitH > 1e-9)
                    {
                        double needH = (pr / limitH) - pf;
                        if (!double.IsNaN(needH) && !double.IsInfinity(needH)) requiredD = Math.Max(requiredD, needH);
                    }
                    if (limitV > 1e-9)
                    {
                        double needV = (pu / limitV) - pf;
                        if (!double.IsNaN(needV) && !double.IsInfinity(needV)) requiredD = Math.Max(requiredD, needV);
                    }
                }

                // Compare with current distance and ensure a small visible change if equal
                double currentD = (center - eye).DotProduct(forward);
                if (double.IsNaN(currentD) || double.IsInfinity(currentD)) currentD = 1.0;
                requiredD = Math.Max(requiredD, 0.01);
                if (Math.Abs(requiredD - currentD) < 1e-3)
                {
                    // Nudge out slightly so the export visibly changes
                    requiredD = currentD * 1.05;
                }

                var newEye = new XYZ(center.X - forward.X * requiredD, center.Y - forward.Y * requiredD, center.Z - forward.Z * requiredD);
                var newForward = center - newEye; if (newForward.GetLength() < 1e-9) return false;
                var newOri = new ViewOrientation3D(newEye, up, newForward);
                v3.SetOrientation(newOri);
                return true;
            }
            catch { return false; }
        }

        private const string ClampCode = "IMAGE_EXPORT_CLAMP";
        private const string AutoVisibleCode = "AUTO_SET_VISIBLE";
        private const string StaticCropCode = "STATIC_CROP";
        private const string AbortInvalidCode = "IMAGE_EXPORT_ABORT_INVALID";
#if DEBUG
        private const string DebugClampMiss = "IMAGE_EXPORT_CLAMP_MISSED";
#endif
        private readonly HashSet<string> _clampLoggedThisRun = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _autoLoggedThisRun = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _staticLoggedThisRun = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _abortLoggedThisRun = new(StringComparer.OrdinalIgnoreCase);

        private void LogOnce(Document doc, string code, string viewName, string detail)
        { if (doc == null) return; var key = code + ":" + viewName + ":" + detail; if (_autoLoggedThisRun.Contains(key)) return; _autoLoggedThisRun.Add(key); try { doc.Application?.WriteJournalComment($"{code}: view='{viewName}' {detail}", false); } catch { } }
        private void LogAbort(Document doc, string viewName, string reason)
        { if (doc == null) return; var key = viewName + ":" + reason; if (_abortLoggedThisRun.Contains(key)) return; _abortLoggedThisRun.Add(key); try { doc.Application?.WriteJournalComment($"{AbortInvalidCode}: view='{viewName}' reason='{reason}'", false); } catch { } }
        private void LogStaticOnce(Document doc, string viewName)
        { if (doc == null) return; if (_staticLoggedThisRun.Contains(viewName)) return; _staticLoggedThisRun.Add(viewName); try { doc.Application?.WriteJournalComment($"{StaticCropCode}: view='{viewName}'", false); } catch { } }
        private void LogClamp(Document doc, string viewName, int requested, int used)
        { if (doc == null) return; var key = viewName + ":" + requested; if (_clampLoggedThisRun.Contains(key)) return; _clampLoggedThisRun.Add(key); try { doc.Application?.WriteJournalComment($"{ClampCode}: view='{viewName}' requested='{requested}' used='{used}'", false); } catch { } }

        // Pixel size helpers
        private static int ComputePixelSize(ViewPlan v, int dpi, out int raw)
        {
            raw = 0; BoundingBoxXYZ? box = null; try { box = v.CropBox; } catch { }
            if (box == null) return 0;
            double wFt = Math.Abs(box.Max.X - box.Min.X); double hFt = Math.Abs(box.Max.Y - box.Min.Y);
            if (wFt <= 0 || hFt <= 0 || v.Scale <= 0) return 0;
            double wIn = wFt * 12.0 / v.Scale; double hIn = hFt * 12.0 / v.Scale;
            int px = (int)Math.Ceiling(Math.Max(wIn, hIn) * dpi);
            raw = px;
            return Math.Clamp(px, 64, 16384);
        }
        private static bool TryComputePixelSize(ViewPlan plan, int dpi, out int pixelSize, out string reason, out bool clamped, out int raw)
        { pixelSize = 0; reason = string.Empty; clamped = false; raw = 0; try { int px = ComputePixelSize(plan, dpi, out raw); if (px <= 0) { reason = "invalid_extents"; return false; } pixelSize = px; clamped = raw > 16384; return true; } catch (Exception ex) { reason = ex.GetType().Name.ToLowerInvariant(); return false; } }

        private static bool TryGet3DExtents(View3D v, out double wFt, out double hFt, out string reason)
        { wFt = 0; hFt = 0; reason = string.Empty; try { BoundingBoxXYZ? box = null; try { box = v.GetSectionBox(); } catch { } if (box == null || box.Min == null || box.Max == null) { try { box = v.get_BoundingBox(null); } catch { } } if (box == null) { reason = "no_box"; return false; } wFt = Math.Abs(box.Max.X - box.Min.X); hFt = Math.Abs(box.Max.Y - box.Min.Y); if (wFt <= 0 || hFt <= 0) { reason = "invalid_dimensions"; return false; } return true; } catch (Exception ex) { reason = ex.GetType().Name.ToLowerInvariant(); return false; } }
        private static bool TryComputePixelSize3D(View3D v, int dpi, out int pixelSize, out string reason, out bool clamped, out int raw)
        { pixelSize = 0; reason = string.Empty; clamped = false; raw = 0; if (!TryGet3DExtents(v, out var wFt, out var hFt, out reason)) return false; int scale = 0; try { scale = v.Scale; } catch { } if (scale <= 0) { reason = "invalid_scale"; return false; } double wIn = wFt * 12.0 / scale; double hIn = hFt * 12.0 / scale; if (wIn <= 0 || hIn <= 0) { reason = "invalid_inches"; return false; } int px = (int)Math.Ceiling(Math.Max(wIn, hIn) * dpi); raw = px; if (px <= 0) { reason = "zero_px"; return false; } pixelSize = Math.Clamp(px, 64, 16384); clamped = raw > 16384; return true; }

        public void Execute(UIApplication uiapp, Document sourceDoc, Document? outDoc, string setName, IList<string> preserveUids, bool isDryRun)
        {
            AppSettings app; try { app = AppSettingsStore.Load(); } catch { return; }
            if (app.Workflows == null || app.SelectedWorkflowIds == null) return;

            // One-time in-memory migration: replace legacy action id entries with the new id (no dual support kept)
            bool migrated = false;
            foreach (var wf in app.Workflows)
            {
                try
                {
                    if (wf.Output != OutputType.Image || wf.ActionIds == null) continue;
                    bool hasLegacy = wf.ActionIds.Any(a => string.Equals(a, "export-png", StringComparison.OrdinalIgnoreCase));
                    if (!hasLegacy) continue;
                    // Replace each legacy occurrence with the new id
                    for (int i = 0; i < wf.ActionIds.Count; i++)
                        if (string.Equals(wf.ActionIds[i], "export-png", StringComparison.OrdinalIgnoreCase)) wf.ActionIds[i] = Id;
                    // Deduplicate (leave only a single new id)
                    wf.ActionIds = wf.ActionIds.Where(a => string.Equals(a, Id, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    migrated = true;
                }
                catch { }
            }
            if (migrated) { try { AppSettingsStore.Save(app); } catch { } }

            var selected = new HashSet<string>(app.SelectedWorkflowIds, StringComparer.OrdinalIgnoreCase);
            var imageWfs = app.Workflows.Where(w => w.Output == OutputType.Image && selected.Contains(w.Id) && (w.ActionIds?.Any(a => string.Equals(a, Id, StringComparison.OrdinalIgnoreCase)) ?? false)).ToList();
            if (imageWfs.Count == 0) return;
            string outputDir = app.DefaultOutputDir; if (string.IsNullOrWhiteSpace(outputDir)) outputDir = AppSettingsStore.FallbackOutputDir; try { System.IO.Directory.CreateDirectory(outputDir); } catch { return; }
            foreach (var wf in imageWfs)
            {
                if (wf.Parameters == null) continue;
                string GetStr(string key)
                { try { if (wf.Parameters.TryGetValue(key, out var je)) { if (je.ValueKind == System.Text.Json.JsonValueKind.String) return je.GetString() ?? string.Empty; if (je.ValueKind == System.Text.Json.JsonValueKind.Number) return je.ToString(); } } catch { } return string.Empty; }
                bool GetBool(string key, bool def)
                { try { if (wf.Parameters.TryGetValue(key, out var je)) { if (je.ValueKind == System.Text.Json.JsonValueKind.True) return true; if (je.ValueKind == System.Text.Json.JsonValueKind.False) return false; if (je.ValueKind == System.Text.Json.JsonValueKind.String && bool.TryParse(je.GetString(), out var b)) return b; } } catch { } return def; }

                var fmtStr = GetStr(ImageWorkflowKeys.imageFormat); if (string.IsNullOrWhiteSpace(fmtStr)) fmtStr = "PNG";
                var resStr = GetStr(ImageWorkflowKeys.resolutionPreset); if (string.IsNullOrWhiteSpace(resStr)) resStr = "Medium"; // Accept new preset tokens

                ImageFileType type = fmtStr.ToUpperInvariant() switch
                {
                    "PNG" => ImageFileType.PNG,
                    "BMP" => ImageFileType.BMP,
                    "TIFF" or "TIF" => ImageFileType.TIFF,
                    _ => ImageFileType.PNG
                };

                var token = resStr.Trim().ToUpperInvariant();
                int targetDpi; ImageResolution res;
                switch (token)
                {
                    case "LOW":
                    case "DPI_72": targetDpi = 72; res = ImageResolution.DPI_72; break;
                    case "HIGH":
                    case "DPI_300": targetDpi = 300; res = ImageResolution.DPI_300; break;
                    case "ULTRA":
                    case "DPI_600": targetDpi = 600; res = ImageResolution.DPI_600; break;
                    case "DPI_150":
                    case "MEDIUM":
                    default: targetDpi = 150; res = ImageResolution.DPI_150; break;
                }
                ;

                int effectiveDpi = targetDpi;

                List<ElementId>? viewIds = null;

                // Scope selection: "PrintSet" | "SingleView"
                var scopeVal = GetStr(ImageWorkflowKeys.exportScope);
                bool singleViewScope = string.Equals(scopeVal, "SingleView", StringComparison.OrdinalIgnoreCase);
                bool printSetScope = !singleViewScope;

                if (singleViewScope)
                {
                    // Build view list from saved single view id
                    var idRaw = GetStr(ImageWorkflowKeys.singleViewId);
                    if (int.TryParse(idRaw, out var ival))
                    {
                        var vid = new ElementId(ival);
                        try
                        {
                            if (sourceDoc.GetElement(vid) is View v
                                && !v.IsTemplate
                                && v.ViewType != ViewType.DrawingSheet)
                            {
                                viewIds = new List<ElementId> { vid };
                            }
                        }
                        catch { }
                    }
                    if (viewIds == null || viewIds.Count == 0) continue; // nothing to export
                }
                else
                {
                    string printSetName = GetStr(ImageWorkflowKeys.imagePrintSetName);
                    if (!string.IsNullOrWhiteSpace(printSetName))
                    {
                        try
                        {
                            var set = new FilteredElementCollector(sourceDoc)
                                .OfClass(typeof(ViewSheetSet))
                                .Cast<ViewSheetSet>()
                                .FirstOrDefault(s => string.Equals(s.Name, printSetName, StringComparison.OrdinalIgnoreCase));
                            if (set != null)
                            {
                                var ids = new List<ElementId>();
                                try { foreach (View v in set.Views) ids.Add(v.Id); } catch { }
                                if (ids.Count > 0) viewIds = ids;
                            }
                        }
                        catch { }
                    }
                    if (viewIds == null || viewIds.Count == 0) continue; // nothing to export
                }

                var sourceViews = viewIds.Select(id => { View? v = null; try { v = sourceDoc.GetElement(id) as View; } catch { } return v; }).Where(v => v != null).ToList()!;
                if (sourceViews.Count == 0) continue;

                var cropModeParam = GetStr(ImageWorkflowKeys.cropMode);
                bool isAutoCrop = string.Equals(cropModeParam, "Auto", StringComparison.OrdinalIgnoreCase);
                double offset = 0; double.TryParse(GetStr(ImageWorkflowKeys.cropOffset), out offset);
                string patternParam = GetStr(ImageWorkflowKeys.fileNamePattern); if (string.IsNullOrWhiteSpace(patternParam)) patternParam = "{SetName}";

                // Selection filter ids (used for auto-crop bounding calculations)
                List<ElementId> selectionIds = new List<ElementId>();
                try
                {
                    SelectionFilterElement? sfe = null; try { sfe = new FilteredElementCollector(sourceDoc).OfClass(typeof(SelectionFilterElement)).Cast<SelectionFilterElement>().FirstOrDefault(f => string.Equals(f.Name, setName, StringComparison.OrdinalIgnoreCase)); } catch { }
                    if (sfe != null) selectionIds = sfe.GetElementIds()?.ToList() ?? new List<ElementId>();
                }
                catch { }

                // Record originals for 3D perspective/section boxes
                var threeDOriginals = new List<(View3D v, BoundingBoxXYZ? sectionBox, ViewOrientation3D? ori)>();
                var planOriginals = new List<(ViewPlan plan, BoundingBoxXYZ box, bool? active, bool? visible)>();
                var normalizationStates = new List<(ViewPlan plan, bool? active, bool? visible, bool? annot, BoundingBoxXYZ? box)>();

                // Pre-normalize plan view crop settings for Auto mode
                if (isAutoCrop)
                {
                    foreach (var vp in sourceViews.OfType<ViewPlan>())
                    {
                        bool? a = null, vis = null, annot = null; BoundingBoxXYZ? box = null;
                        try { a = vp.CropBoxActive; } catch { }
                        try { vis = vp.CropBoxVisible; } catch { }
                        try { box = vp.CropBox; } catch { }
                        try { var p = vp.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE); if (p != null) annot = p.AsInteger() == 1; } catch { }
                        normalizationStates.Add((vp, a, vis, annot, box));
                    }
                    using (var t = new Transaction(sourceDoc, "Normalize Crop Settings"))
                    {
                        t.Start();
                        try
                        {
                            foreach (var v in sourceViews.OfType<ViewPlan>())
                            {
                                try { v.CropBoxActive = false; } catch { }
                                try { v.CropBoxVisible = true; } catch { }
                                try { v.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE)?.Set(1); } catch { }
                            }
                        }
                        finally { try { t.Commit(); } catch { } }
                    }
                }

                var docForExport = outDoc ?? sourceDoc;
                bool autoAppendViewNameIfMissing = printSetScope; // per requirements

                foreach (var view in sourceViews)
                {
                    bool isPlan = view is ViewPlan;
                    bool is3d = view is View3D;

                    // AUTO CROPPING
                    if (isAutoCrop && (isPlan || is3d))
                    {
                        if (isPlan)
                        {
                            var vp = (ViewPlan)view;
                            var visibleInView = new FilteredElementCollector(sourceDoc, vp.Id).WhereElementIsNotElementType().ToElementIds();
                            var visibleSetIds = selectionIds.Where(visibleInView.Contains).ToList();
                            if (visibleSetIds.Count > 0)
                            {
                                var boxes = CollectVisiblePlanElementBoxes(vp, sourceDoc, visibleSetIds);
                                if (boxes.Count > 0)
                                {
                                    BoundingBoxXYZ existing; try { existing = vp.CropBox; } catch { existing = null; }
                                    if (existing != null)
                                    {
                                        var originalCopy = new BoundingBoxXYZ { Min = existing.Min, Max = existing.Max, Enabled = existing.Enabled };
                                        bool? origActive = null; bool? origVisible = null;
                                        try { origActive = vp.CropBoxActive; } catch { }
                                        try { origVisible = vp.CropBoxVisible; } catch { }

                                        double minx = double.PositiveInfinity, miny = double.PositiveInfinity, maxx = double.NegativeInfinity, maxy = double.NegativeInfinity;
                                        foreach (var bb in boxes)
                                        {
                                            minx = Math.Min(minx, bb.Min.X); miny = Math.Min(miny, bb.Min.Y);
                                            maxx = Math.Max(maxx, bb.Max.X); maxy = Math.Max(maxy, bb.Max.Y);
                                        }
                                        if (Math.Abs(offset) > 0)
                                        { minx -= offset; miny -= offset; maxx += offset; maxy += offset; }
                                        const double minSizeFt = 1.0 / 12.0;
                                        if (maxx - minx < minSizeFt) { double d = (minSizeFt - (maxx - minx)) * 0.5; minx -= d; maxx += d; }
                                        if (maxy - miny < minSizeFt) { double d = (minSizeFt - (maxy - miny)) * 0.5; miny -= d; maxy += d; }

                                        using (var t = new Transaction(sourceDoc, "Auto Crop (Image)"))
                                        {
                                            t.Start();
                                            try
                                            {
                                                vp.CropBoxActive = true;
                                                var zMin = existing.Min.Z; var zMax = existing.Max.Z;
                                                vp.CropBox = new BoundingBoxXYZ { Min = new XYZ(minx, miny, zMin), Max = new XYZ(maxx, maxy, zMax), Enabled = true };
                                                try { vp.CropBoxVisible = false; } catch { }
                                                t.Commit();
                                                LogOnce(sourceDoc, AutoVisibleCode, vp.Name, "crop=auto");
                                                planOriginals.Add((vp, originalCopy, origActive, origVisible));
                                            }
                                            catch { try { t.RollBack(); } catch { } }
                                        }
                                    }
                                }
                            }
                        }
                        else if (is3d)
                        {
                            var v3 = (View3D)view;
                            // collect set-based boxes for 3D
                            var visibleInView = new FilteredElementCollector(sourceDoc, v3.Id).WhereElementIsNotElementType().ToElementIds();
                            var visibleSetIds = selectionIds.Where(visibleInView.Contains).ToList();
                            if (visibleSetIds.Count > 0)
                            {
                                var boxes = CollectVisible3DElementBoxes(v3, sourceDoc, visibleSetIds);
                                if (boxes.Count > 0)
                                {
                                    // merged box
                                    double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
                                    double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
                                    foreach (var bb in boxes)
                                    {
                                        minX = Math.Min(minX, bb.Min.X); minY = Math.Min(minY, bb.Min.Y); minZ = Math.Min(minZ, bb.Min.Z);
                                        maxX = Math.Max(maxX, bb.Max.X); maxY = Math.Max(maxY, bb.Max.Y); maxZ = Math.Max(maxZ, bb.Max.Z);
                                    }
                                    var merged = new BoundingBoxXYZ { Min = new XYZ(minX, minY, minZ), Max = new XYZ(maxX, maxY, maxZ) };

                                    BoundingBoxXYZ? origSection = null; try { origSection = v3.GetSectionBox(); } catch { }
                                    ViewOrientation3D? origOri = null; try { origOri = v3.GetOrientation(); } catch { }
                                    threeDOriginals.Add((v3, origSection, origOri));

                                    using (var t = new Transaction(sourceDoc, "Auto Crop 3D"))
                                    {
                                        t.Start();
                                        try
                                        {
                                            if (!v3.IsPerspective)
                                            {
                                                var sb = CloneWithOffset(merged, offset); if (sb != null) { sb.Enabled = true; try { v3.SetSectionBox(sb); } catch { } }
                                            }
                                            else
                                            {
                                                double hf = 50.0; double.TryParse(GetStr(ImageWorkflowKeys.heuristicFovDeg), out hf); hf = Math.Clamp(hf <= 0 ? 50.0 : hf, 1.0, 90.0);
                                                double hb = 5.0; double.TryParse(GetStr(ImageWorkflowKeys.heuristicFovBufferPct), out hb); hb = Math.Clamp(hb, -50.0, 100.0);
                                                var adjustedBox = CloneWithOffset(merged, offset) ?? merged;
                                                if (!TryAdjustPerspectiveCamera(v3, adjustedBox, hf, hb))
                                                    LogAbort(sourceDoc, v3.Name ?? "<3D>", "forward_fit_failed");
                                            }
                                            t.Commit();
                                        }
                                        catch { try { t.RollBack(); } catch { } }
                                    }
                                }
                            }
                        }
                    } // end auto-crop

                    // Export this view
                    var opts = new ImageExportOptions
                    {
                        ExportRange = ExportRange.SetOfViews,
                        HLRandWFViewsFileType = type,
                        ShadowViewsFileType = type,
                        ZoomType = ZoomFitType.Zoom,
                        ImageResolution = res
                    };
                    try { opts.SetViewsAndSheets(new List<ElementId> { view.Id }); } catch { continue; }

                    // SingleView scope: do NOT auto append view name; PrintSet: auto append if missing
                    var (baseNoExt, ext, fileName) = BuildImageFileName(
                        patternParam,
                        setName,
                        type,
                        view.Name,
                        autoAppendViewNameIfMissing);

                    if (!app.DefaultOverwrite)
                        fileName = EnsureUnique(outputDir, fileName);

                    var basePathNoExt = System.IO.Path.Combine(outputDir, System.IO.Path.GetFileNameWithoutExtension(fileName));
                    opts.FilePath = basePathNoExt;

                    if (app.DefaultOverwrite)
                    {
                        try { var full = basePathNoExt + ext; if (System.IO.File.Exists(full)) System.IO.File.Delete(full); } catch { }
                    }

                    LogOnce(sourceDoc, AutoVisibleCode, setName, $"namePattern='{patternParam}', example='{fileName}'");
                    try { docForExport.ExportImage(opts); } catch { }

                    // Normalize only if SingleView scope (exactly one exported view per set)
                    if (singleViewScope && sourceViews.Count == 1)
                        TryNormalizeExportedFile(outputDir, baseNoExt, ext);
                }

                // Restore originals for 3D views
                foreach (var orig in threeDOriginals)
                {
                    try
                    {
                        using (var t = new Transaction(sourceDoc, "Restore 3D Crop"))
                        {
                            t.Start();
                            try
                            {
                                if (orig.sectionBox != null) try { orig.v.SetSectionBox(orig.sectionBox); } catch { }
                                if (orig.ori != null) try { orig.v.SetOrientation(orig.ori); } catch { }
                                t.Commit();
                            }
                            catch { try { t.RollBack(); } catch { } }
                        }
                    }
                    catch { }
                }

                // Restore plan view crops (auto mode only)
                if (isAutoCrop)
                {
                    foreach (var o in planOriginals)
                    {
                        try
                        {
                            using (var t = new Transaction(sourceDoc, "Restore Crop (Image)"))
                            {
                                t.Start();
                                try
                                {
                                    var restore = o.plan.CropBox; // copy current box
                                    restore.Min = new XYZ(o.box.Min.X, o.box.Min.Y, restore.Min.Z);
                                    restore.Max = new XYZ(o.box.Max.X, o.box.Max.Y, restore.Max.Z);
                                    restore.Enabled = o.box.Enabled;
                                    o.plan.CropBox = restore;
                                    try { if (o.active.HasValue) o.plan.CropBoxActive = o.active.Value; } catch { }
                                    try { if (o.visible.HasValue) o.plan.CropBoxVisible = o.visible.Value; } catch { }
                                    t.Commit();
                                }
                                catch { try { t.RollBack(); } catch { } }
                            }
                        }
                        catch { }
                    }

                    // Restore normalized crop settings fully
                    using (var t = new Transaction(sourceDoc, "Restore Normalized Crop Settings"))
                    {
                        t.Start();
                        try
                        {
                            foreach (var s in normalizationStates)
                            {
                                try
                                {
                                    if (s.active.HasValue) s.plan.CropBoxActive = s.active.Value;
                                    if (s.visible.HasValue) s.plan.CropBoxVisible = s.visible.Value;
                                    if (s.annot.HasValue) s.plan.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE)?.Set(s.annot.Value ? 1 : 0);
                                    if (s.box != null) s.plan.CropBox = s.box;
                                }
                                catch { }
                            }
                        }
                        finally { try { t.Commit(); } catch { } }
                    }
                }

                try { sourceDoc.Regenerate(); } catch { }
            }
        }

        private static string San(string name) { foreach (var c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_'); return name; }
        private static string EnsureUnique(string dir, string fileName)
        { try { var baseName = System.IO.Path.GetFileNameWithoutExtension(fileName); var ext = System.IO.Path.GetExtension(fileName); var candidate = fileName; int i = 2; while (System.IO.File.Exists(System.IO.Path.Combine(dir, candidate))) { candidate = $"{baseName} ({i}){ext}"; i++; if (i > 10000) break; } return candidate; } catch { return fileName; } }
    }
}
