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
        /// Builds sanitized base filename (no directory), aligned with UI preview:
        /// - Expands {SetName},{Date},{Time} via ExpandTokens
        /// - Replaces {ViewName} only if the token is present
        /// - Applies prefix and suffix with no implicit separators
        /// - Returns (baseNoExt, ext, finalFileNameWithExt)
        /// </summary>
        private (string baseNoExt, string ext, string fileName) BuildImageFileName(
            string pattern,
            string setName,
            ImageFileType type,
            string? viewNameOrNull)
        {
            // 1) Expand core tokens like UI
            var expanded = ExpandTokens(pattern ?? "{SetName}", setName);
            var core = SanitizeFileComponent(System.IO.Path.GetFileNameWithoutExtension(expanded));

            // 2) Replace {ViewName} only if present; never auto-append
            if (core.Contains("{ViewName}", StringComparison.Ordinal))
            {
                var vn = string.IsNullOrWhiteSpace(viewNameOrNull) ? string.Empty : SanitizeFileComponent(viewNameOrNull);
                core = core.Replace("{ViewName}", vn);
            }

            // 3) Fallback when empty after sanitation
            var baseNoExt = string.IsNullOrWhiteSpace(core) ? "export" : core;

            // 4) Extension consistent with UI
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

        // Adjust a perspective camera's eye XY distance to fit the bounding box with given horizontal FOV and buffer percent.
        // Preserves original Eye.Z and Target.Z (target elevation).
        private static bool TryAdjustPerspectiveCamera(View3D v3, BoundingBoxXYZ targetBox, double heuristicFovDeg, double bufferPct)
        {
            try
            {
                if (v3 == null || targetBox == null) return false;
                // Validate FOV
                if (heuristicFovDeg <= 0 || heuristicFovDeg >= 180) return false;

                var ori = v3.GetOrientation();
                if (ori == null) return false;

                var eye = ori.EyePosition;
                var forwardVec = ori.ForwardDirection;
                if (forwardVec == null) return false;
                double forwardLen = forwardVec.GetLength();
                if (forwardLen < 1e-9) return false;

                var target = eye + forwardVec;
                double eyeZ = eye.Z;
                double targetZ = target.Z;

                var forwardNorm = forwardVec.Normalize();
                var upVec = ori.UpDirection ?? XYZ.BasisZ;
                if (upVec.GetLength() < 1e-9) upVec = XYZ.BasisZ;
                else upVec = upVec.Normalize();

                var rightVec = forwardNorm.CrossProduct(upVec);
                if (rightVec.GetLength() < 1e-9)
                {
                    rightVec = forwardNorm.CrossProduct(XYZ.BasisZ);
                }
                if (rightVec.GetLength() < 1e-9) return false;
                rightVec = rightVec.Normalize();

                var desiredTarget = new XYZ(center.X, center.Y, targetZ);

                var horizontalForward = new XYZ(forwardNorm.X, forwardNorm.Y, 0.0);
                if (horizontalForward.GetLength() < 1e-9)
                {
                    horizontalForward = new XYZ(desiredTarget.X - eye.X, desiredTarget.Y - eye.Y, 0.0);
                }
                if (horizontalForward.GetLength() < 1e-9)
                {
                    horizontalForward = rightHint.CrossProduct(XYZ.BasisZ);
                }
                if (horizontalForward.GetLength() < 1e-9)
                {
                    horizontalForward = new XYZ(1, 0, 0);
                }
                horizontalForward = horizontalForward.Normalize();

                var projectionCorners = corners;
                if (projectionCorners == null || projectionCorners.Count == 0)
                {
                    var rel = c - target;
                    double p = rel.DotProduct(rightVec);
                    minProj = Math.Min(minProj, p);
                    maxProj = Math.Max(maxProj, p);
                }
                double span = maxProj - minProj;
                if (double.IsNaN(span) || double.IsInfinity(span) || span <= 0)
                    span = Math.Abs(targetBox.Max.X - targetBox.Min.X);
                if (span <= 1e-9) span = MinExtentFeet;
                double halfWidth = span * 0.5;

                double fovRad = heuristicFovDeg * Math.PI / 180.0;
                double halfFov = fovRad * 0.5;
                if (halfFov <= 1e-6) return false;
                double tanHalf = Math.Tan(halfFov);
                if (double.IsNaN(tanHalf) || double.IsInfinity(tanHalf) || tanHalf <= 0) return false;

                double initialHorizontal = Math.Sqrt(Math.Pow(eye.X - target.X, 2) + Math.Pow(eye.Y - target.Y, 2));
                if (double.IsNaN(initialHorizontal) || initialHorizontal < 0.01) initialHorizontal = 1.0;

                double minDist = 0.01;
                double maxDist = Math.Max(initialHorizontal * 8.0, minDist * 2.0);
                if (maxDist < 10.0) maxDist = 10.0;

                bool TryCompute(double horizontalDist, out double maxRatio, out ViewOrientation3D? orientation)
                {
                    orientation = null;
                    maxRatio = double.PositiveInfinity;
                    if (horizontalDist < 0.005) horizontalDist = 0.005;

                    var candidateEye = new XYZ(
                        desiredTarget.X - horizontalForward.X * horizontalDist,
                        desiredTarget.Y - horizontalForward.Y * horizontalDist,
                        eyeZ);

                    var forwardCandidate = desiredTarget - candidateEye;
                    if (forwardCandidate.GetLength() < 1e-9) return false;
                    var forwardUnit = forwardCandidate.Normalize();

                    var right = forwardUnit.CrossProduct(upHint);
                    if (right.GetLength() < 1e-9)
                    {
                        right = forwardUnit.CrossProduct(XYZ.BasisZ);
                    }
                    if (right.GetLength() < 1e-9) return false;
                    right = right.Normalize();

                    var up = right.CrossProduct(forwardUnit);
                    if (up.GetLength() < 1e-9)
                    {
                        up = upHint;
                    }
                    if (up.GetLength() < 1e-9) up = XYZ.BasisZ;
                    up = up.Normalize();

                    double localMax = 0;
                    foreach (var corner in projectionCorners)
                    {
                        var vec = corner - candidateEye;
                        double depth = vec.DotProduct(forwardUnit);
                        double horiz = vec.DotProduct(right);
                        if (depth <= 1e-6)
                        {
                            maxRatio = double.PositiveInfinity;
                            return false;
                        }
                        double ratio = Math.Abs(horiz) / depth;
                        if (double.IsNaN(ratio) || double.IsInfinity(ratio))
                        {
                            maxRatio = double.PositiveInfinity;
                            return false;
                        }
                        if (ratio > localMax) localMax = ratio;
                    }

                var targetXY = new XYZ(target.X, target.Y, 0);
                var centerToEyeXY = new XYZ(eye.X - target.X, eye.Y - target.Y, 0);
                XYZ forwardXY;
                double dirLen = centerToEyeXY.GetLength();
                if (dirLen < 1e-9)
                {
                    forwardXY = new XYZ(-forwardNorm.X, -forwardNorm.Y, 0);
                    var fl = forwardXY.GetLength();
                    if (fl < 1e-9) forwardXY = new XYZ(1, 0, 0); else forwardXY = forwardXY.Normalize();
                }
                else
                {
                    forwardXY = centerToEyeXY.Normalize();
                }

                // New eye XY = targetXY + forwardXY * desiredDist (forwardXY points from target to eye)
                var newEyeXY = targetXY + forwardXY * desiredDist;
                var newEye = new XYZ(newEyeXY.X, newEyeXY.Y, eyeZ);

                // Rebuild forward vector from new eye to center (preserve Zs)
                var desiredForward = new XYZ((target.X - newEye.X), (target.Y - newEye.Y), (targetZ - newEye.Z));
                if (desiredForward.GetLength() < 1e-9) return false;
                var upForOrientation = ori.UpDirection ?? XYZ.BasisZ;
                if (upForOrientation.GetLength() < 1e-9) upForOrientation = XYZ.BasisZ;
                var newOri = new ViewOrientation3D(newEye, upForOrientation, desiredForward);
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

                List<ElementId>? printSetViewIds = null;

                // Scope selection: "PrintSet" | "SingleView"
                var scopeVal = GetStr(ImageWorkflowKeys.exportScope);
                bool singleViewScope = string.Equals(scopeVal, "SingleView", StringComparison.OrdinalIgnoreCase);

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
                                // Allow any non-template non-sheet, including 3D/camera
                                printSetViewIds = new List<ElementId> { vid };
                            }
                        }
                        catch { }
                    }
                    // Mirror empty print set behavior: nothing to export for this workflow
                    if (printSetViewIds == null || printSetViewIds.Count == 0)
                        continue; // skip this workflow (same as empty print set)
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
                                if (ids.Count > 0) printSetViewIds = ids;
                            }
                        }
                        catch { }
                    }
                    if (printSetViewIds == null || printSetViewIds.Count == 0) continue; // nothing to export
                }

                // Determine cropping mode intent once
                var cropModeParam = GetStr(ImageWorkflowKeys.cropMode);
                double offset = 0; double.TryParse(GetStr(ImageWorkflowKeys.cropOffset), out offset);

                // Classify views
                var sourceViews = new List<View>();
                foreach (var id in printSetViewIds)
                {
                    View? v = null; try { v = sourceDoc.GetElement(id) as View; } catch { }
                    if (v != null) sourceViews.Add(v);
                }
                if (sourceViews.Count == 0) continue;

                bool any3d = sourceViews.Any(v => v is View3D);
                bool anySheet = sourceViews.Any(v => v is ViewSheet);
                bool allPlans = !any3d && !anySheet && sourceViews.All(v => v is ViewPlan);
                bool autoCropEligible = allPlans && string.Equals(cropModeParam, "Auto", StringComparison.OrdinalIgnoreCase);
                bool autoCrop3dEligible = any3d && string.Equals(cropModeParam, "Auto", StringComparison.OrdinalIgnoreCase);

                // If eligible for 3D auto-crop (contains 3D views and cropMode Auto), handle per-view 3D logic
                if (autoCrop3dEligible)
                {
                    string patternSet = GetStr(ImageWorkflowKeys.fileNamePattern);
                    if (string.IsNullOrWhiteSpace(patternSet)) patternSet = "{SetName}";

                    // Selection filter (once) for determining which elements define extents
                    List<ElementId> selectionIds3d = new List<ElementId>();
                    try
                    {
                        SelectionFilterElement? sfe = null; try { sfe = new FilteredElementCollector(sourceDoc).OfClass(typeof(SelectionFilterElement)).Cast<SelectionFilterElement>().FirstOrDefault(f => string.Equals(f.Name, setName, StringComparison.OrdinalIgnoreCase)); } catch { }
                        if (sfe != null) selectionIds3d = sfe.GetElementIds()?.ToList() ?? new List<ElementId>();
                    }
                    catch { }

                    var docForExport3 = outDoc ?? sourceDoc;

                    // Record originals for restore
                    var originals3d = new List<(View3D v, BoundingBoxXYZ? sectionBox, ViewOrientation3D? ori)>();

                    foreach (var v in sourceViews.OfType<View3D>())
                    {
                        try
                        {
                            // Compute visible set ids in this view
                            var visibleInView = new FilteredElementCollector(sourceDoc, v.Id).WhereElementIsNotElementType().ToElementIds();
                            var visibleSetIds = selectionIds3d.Where(visibleInView.Contains).ToList();
                            if (visibleSetIds.Count == 0) continue;

                            var boxes = CollectVisible3DElementBoxes(v, sourceDoc, visibleSetIds);
                            if (boxes.Count == 0) continue;

                            // Merge boxes into a single BB in model coords
                            double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
                            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
                            foreach (var bb in boxes)
                            {
                                minX = Math.Min(minX, bb.Min.X); minY = Math.Min(minY, bb.Min.Y); minZ = Math.Min(minZ, bb.Min.Z);
                                maxX = Math.Max(maxX, bb.Max.X); maxY = Math.Max(maxY, bb.Max.Y); maxZ = Math.Max(maxZ, bb.Max.Z);
                            }
                            var merged = new BoundingBoxXYZ { Min = new XYZ(minX, minY, minZ), Max = new XYZ(maxX, maxY, maxZ) };

                            // Save originals
                            BoundingBoxXYZ? origSection = null; try { origSection = v.GetSectionBox(); } catch { }
                            ViewOrientation3D? origOri = null; try { origOri = v.GetOrientation(); } catch { }
                            originals3d.Add((v, origSection, origOri));

                            using (var t = new Transaction(sourceDoc, "Auto Crop 3D"))
                            {
                                t.Start();
                                try
                                {
                                    if (!v.IsPerspective)
                                    {
                                        // Orthographic: set section box to merged extents with offset applied
                                        var sb = CloneWithOffset(merged, offset);
                                        if (sb != null)
                                        {
                                            sb.Enabled = true;
                                            try { v.SetSectionBox(sb); } catch { }
                                        }
                                    }
                                    else
                                    {
                                        // Perspective: attempt camera distance adjust according to heuristic
                                        double hf = 50.0; double.TryParse(GetStr(ImageWorkflowKeys.heuristicFovDeg), out hf);
                                        if (hf <= 0) hf = 50.0;
                                        hf = Math.Clamp(hf, 1.0, 90.0);
                                        double hb = 5.0; double.TryParse(GetStr(ImageWorkflowKeys.heuristicFovBufferPct), out hb);
                                        hb = Math.Clamp(hb, -50.0, 100.0);

                                        var adjustedBox = CloneWithOffset(merged, offset);
                                        TryAdjustPerspectiveCamera(v, adjustedBox ?? merged, hf, hb);
                                    }
                                    t.Commit();
                                }
                                catch { try { t.RollBack(); } catch { } }
                            }
                        }
                        catch { }
                    }

                    try { sourceDoc.Regenerate(); } catch { }

                    // Export each 3D view individually
                    foreach (var v in sourceViews)
                    {
                        if (v is View3D v3)
                        {
                            var opts = new ImageExportOptions
                            {
                                ExportRange = ExportRange.SetOfViews,
                                HLRandWFViewsFileType = type,
                                ShadowViewsFileType = type,
                                ZoomType = ZoomFitType.Zoom,
                                ImageResolution = res
                            };
                            try { opts.SetViewsAndSheets(new List<ElementId> { v3.Id }); } catch { continue; }

                            var (baseNoExt, ext, fileName) = BuildImageFileName(
                                patternSet,
                                setName,
                                type,
                                v3.Name);

                            if (!app.DefaultOverwrite)
                                fileName = EnsureUnique(outputDir, fileName);

                            var basePathNoExt = System.IO.Path.Combine(outputDir, System.IO.Path.GetFileNameWithoutExtension(fileName));
                            opts.FilePath = basePathNoExt;

                            if (app.DefaultOverwrite)
                            {
                                try { var full = basePathNoExt + ext; if (System.IO.File.Exists(full)) System.IO.File.Delete(full); } catch { }
                            }

                            LogOnce(sourceDoc, AutoVisibleCode, setName, $"namePattern='{patternSet}', example='{fileName}'");
                            try { (outDoc ?? sourceDoc).ExportImage(opts); } catch { }
                            if (singleViewScope && sourceViews.Count == 1) TryNormalizeExportedFile(outputDir, baseNoExt, ext);
                        }
                    }

                    // Restore originals
                    foreach (var orig in originals3d)
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

                    try { sourceDoc.Regenerate(); } catch { }

                    continue;
                }

                // If NOT auto-crop eligible, keep original bulk export behavior for the set (now via unified builder)
                if (!autoCropEligible)
                {
                    string patternSet = GetStr(ImageWorkflowKeys.fileNamePattern);
                    if (string.IsNullOrWhiteSpace(patternSet)) patternSet = "{SetName}";

                    var opts = new ImageExportOptions
                    {
                        ExportRange = ExportRange.SetOfViews,
                        ImageResolution = res,
                        ZoomType = ZoomFitType.Zoom,
                        HLRandWFViewsFileType = type,
                        ShadowViewsFileType = type
                    };
                    try { opts.Zoom = 100; } catch { }
                    try { opts.SetViewsAndSheets(printSetViewIds); } catch { continue; }
                    var viewCountForExport = printSetViewIds?.Count ?? 0; // added: count views for rename gating

                    // If exporting a single view (e.g., SingleView scope), expand {ViewName} using that view's name for preview parity
                    string? singleViewName = null;
                    if (singleViewScope && viewCountForExport == 1)
                    {
                        try { singleViewName = (sourceViews.FirstOrDefault()?.Name) ?? null; } catch { }
                    }

                    var (baseNoExt, ext, fileName) = BuildImageFileName(
                        patternSet,
                        setName,
                        type,
                        singleViewName);

                    if (!app.DefaultOverwrite)
                        fileName = EnsureUnique(outputDir, fileName);

                    var basePathNoExt = System.IO.Path.Combine(outputDir, System.IO.Path.GetFileNameWithoutExtension(fileName));
                    opts.FilePath = basePathNoExt;

                    // Existing preview style log (kept)
                    LogOnce(sourceDoc, AutoVisibleCode, setName, $"namePattern='{patternSet}', example='{fileName}'");

                    try { (outDoc ?? sourceDoc).ExportImage(opts); } catch { }

                    if (viewCountForExport == 1) // only single output -> safe to normalize
                    {
                        TryNormalizeExportedFile(outputDir, baseNoExt, ext);
                    }

                    // lightweight diagnostics
                    LogOnce(sourceDoc, AutoVisibleCode, setName, $"image-export non-auto views={viewCountForExport} fmt={type} base='{baseNoExt}'");

                    continue;
                }

                // AUTO CROP BRANCH (all views are plan views)
                var docForExport = outDoc ?? sourceDoc;
                string pattern = GetStr(ImageWorkflowKeys.fileNamePattern); if (string.IsNullOrWhiteSpace(pattern)) pattern = "{SetName}";
                var normalizationStates = new List<(ViewPlan plan, bool? active, bool? visible, bool? annot, BoundingBoxXYZ? box)>();

                // Removed unused locals: prefix, suffix, appendVN, nowDate, nowTime

                // Selection filter (once) for determining which elements define extents
                List<ElementId> selectionIds = new List<ElementId>();
                try
                {
                    SelectionFilterElement? sfe = null; try { sfe = new FilteredElementCollector(sourceDoc).OfClass(typeof(SelectionFilterElement)).Cast<SelectionFilterElement>().FirstOrDefault(f => string.Equals(f.Name, setName, StringComparison.OrdinalIgnoreCase)); } catch { }
                    if (sfe != null) selectionIds = sfe.GetElementIds()?.ToList() ?? new List<ElementId>();
                }
                catch { }

                foreach (var v in sourceViews.Cast<ViewPlan>())
                {
                    bool? a = null, vis = null, an = null; BoundingBoxXYZ? b = null;
                    try { a = v.CropBoxActive; } catch { }
                    try { vis = v.CropBoxVisible; } catch { }
                    try { b = v.CropBox; } catch { }
                    try
                    {
                        var p = v.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE);
                        if (p != null) an = p.AsInteger() == 1;
                    }
                    catch { }
                    normalizationStates.Add((v, a, vis, an, b));
                }

                using (var t = new Transaction(sourceDoc, "Normalize Crop Settings"))
                {
                    t.Start();
                    try
                    {
                        foreach (var v in sourceViews.Cast<ViewPlan>())
                        {
                            try { v.CropBoxActive = false; } catch { }
                            try { v.CropBoxVisible = true; } catch { }
                            try { v.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE)?.Set(1); } catch { }
                            try
                            {
                                BoundingBoxXYZ? stagingBox = null;
                                try { /*stagingBox = BatchRunCoordinator.ComputeSetCropBox(sourceDoc, preserveUids, v);*/ } catch { }
                                if (stagingBox != null) v.CropBox = stagingBox;
                            }
                            catch { }
                        }
                    }
                    finally { try { t.Commit(); } catch { } }
                }

                var originals = new List<(ViewPlan plan, BoundingBoxXYZ box, bool? active, bool? visible)>();

                foreach (var view in sourceViews.Cast<ViewPlan>())
                {
                    var visibleInView = new FilteredElementCollector(sourceDoc, view.Id).WhereElementIsNotElementType().ToElementIds();
                    var visibleSetIds = selectionIds.Where(visibleInView.Contains).ToList();
                    if (visibleSetIds.Count == 0) continue; // nothing to bound
                    var boxes = CollectVisiblePlanElementBoxes(view, sourceDoc, visibleSetIds);
                    if (boxes.Count == 0) continue;

                    BoundingBoxXYZ existing; try { existing = view.CropBox; } catch { continue; }
                    var originalCopy = new BoundingBoxXYZ { Min = existing.Min, Max = existing.Max, Enabled = existing.Enabled };
                    bool? origActive = null; bool? origVisible = null;
                    try { origActive = view.CropBoxActive; } catch { }
                    try { origVisible = view.CropBoxVisible; } catch { }

                    double minx = double.PositiveInfinity, miny = double.PositiveInfinity, maxx = double.NegativeInfinity, maxy = double.NegativeInfinity;
                    foreach (var bb in boxes)
                    {
                        minx = Math.Min(minx, bb.Min.X); miny = Math.Min(miny, bb.Min.Y);
                        maxx = Math.Max(maxx, bb.Max.X); maxy = Math.Max(maxy, bb.Max.Y);
                    }

                    if (Math.Abs(offset) > 0)
                    {
                        minx -= offset; miny -= offset; maxx += offset; maxy += offset;
                        if (maxx <= minx || maxy <= miny)
                        {
                            double cx = (minx + maxx) * 0.5; double cy = (miny + maxy) * 0.5;
                            const double fallback = 1.0 / 12.0; // 1 inch square
                            minx = cx - fallback * 0.5; maxx = cx + fallback * 0.5;
                            miny = cy - fallback * 0.5; maxy = cy + fallback * 0.5;
                        }
                    }

                    const double minSizeFt = 1.0 / 12.0; // 1 inch
                    if (maxx - minx < minSizeFt) { double d = (minSizeFt - (maxx - minx)) * 0.5; minx -= d; maxx += d; }
                    if (maxy - miny < minSizeFt) { double d = (minSizeFt - (maxy - miny)) * 0.5; miny -= d; maxy += d; }

                    using (var t = new Transaction(sourceDoc, "Auto Crop (Image)"))
                    {
                        t.Start();
                        try
                        {
                            view.CropBoxActive = true;
                            var zMin = existing.Min.Z; var zMax = existing.Max.Z;
                            view.CropBox = new BoundingBoxXYZ { Min = new XYZ(minx, miny, zMin), Max = new XYZ(maxx, maxy, zMax), Enabled = true };
                            try { view.CropBoxVisible = false; } catch { }
                            t.Commit();
                            LogOnce(sourceDoc, AutoVisibleCode, view.Name, "crop=auto");
                            originals.Add((view, originalCopy, origActive, origVisible));
                        }
                        catch { try { t.RollBack(); } catch { } }
                    }
                }
                try { sourceDoc.Regenerate(); } catch { }

                // Export each plan view individually (pixel size per current crop)
                foreach (var view in sourceViews.Cast<ViewPlan>())
                {
                    int pixelSize = 0;
                    try
                    {
                        bool origActive = false; try { origActive = view.CropBoxActive; } catch { }
                        bool tempActivated = false;
                        if (!origActive)
                        {
                            using (var t = new Transaction(sourceDoc, "Activate Crop For Export"))
                            {
                                t.Start();
                                try { view.CropBoxActive = true; t.Commit(); tempActivated = true; } catch { try { t.RollBack(); } catch { } }
                            }
                        }
                        string failReason = string.Empty; if (!TryComputePixelSize(view, effectiveDpi, out var px, out failReason, out var _, out var _raw)) continue; pixelSize = px;
                        if (tempActivated && !origActive)
                        {
                            using (var t = new Transaction(sourceDoc, "Restore Crop Active"))
                            { t.Start(); try { view.CropBoxActive = false; t.Commit(); } catch { try { t.RollBack(); } catch { } } }
                        }
                    }
                    catch { continue; }

                    var opts = new ImageExportOptions
                    {
                        ExportRange = ExportRange.SetOfViews,
                        HLRandWFViewsFileType = type,
                        ShadowViewsFileType = type,
                        ZoomType = ZoomFitType.Zoom,
                        ImageResolution = res
                    };
                    try { opts.SetViewsAndSheets(new List<ElementId> { view.Id }); } catch { continue; }

                    var (baseNoExt, ext, fileName) = BuildImageFileName(
                        pattern,
                        setName,
                        type,
                        view.Name);

                    if (!app.DefaultOverwrite)
                        fileName = EnsureUnique(outputDir, fileName);

                    var basePathNoExt = System.IO.Path.Combine(outputDir, System.IO.Path.GetFileNameWithoutExtension(fileName));
                    opts.FilePath = basePathNoExt;

                    if (app.DefaultOverwrite)
                    {
                        try { var full = basePathNoExt + ext; if (System.IO.File.Exists(full)) System.IO.File.Delete(full); } catch { }
                    }

                    LogOnce(sourceDoc, AutoVisibleCode, setName, $"namePattern='{pattern}', example='{fileName}'");
                    try { docForExport.ExportImage(opts); } catch { }
                    if (singleViewScope && sourceViews.Count == 1)
                    {
                        TryNormalizeExportedFile(outputDir, baseNoExt, ext);
                    }
                }

                // Restore original crop boxes
                foreach (var o in originals)
                {
                    try
                    {
                        using (var t = new Transaction(sourceDoc, "Restore Crop (Image)"))
                        {
                            t.Start();
                            try
                            {
                                var restore = o.plan.CropBox;
                                restore.Min = new XYZ(o.box.Min.X, o.box.Min.Y, restore.Min.Z);
                                restore.Max = new XYZ(o.box.Max.X, o.box.Max.Y, restore.Max.Z);
                                restore.Enabled = o.box.Enabled;
                                o.plan.CropBox = restore; // reassignment (previous bug fix)
                                try { if (o.active.HasValue) o.plan.CropBoxActive = o.active.Value; } catch { }
                                try { if (o.visible.HasValue) o.plan.CropBoxVisible = o.visible.Value; } catch { }
                                t.Commit();
                            }
                            catch { try { t.RollBack(); } catch { } }
                        }
                    }
                    catch { }
                }
                try { sourceDoc.Regenerate(); } catch { }

                // New: restore normalization states fully (annotation, visibility, crop box) after original crop restoration
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
                                if (s.annot.HasValue)
                                    s.plan.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE)?.Set(s.annot.Value ? 1 : 0); // corrected constant
                                if (s.box != null) s.plan.CropBox = s.box;
                            }
                            catch { }
                        }
                    }
                    finally { try { t.Commit(); } catch { } }
                }
            }
        }

        private static string San(string name) { foreach (var c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_'); return name; }
        private static string EnsureUnique(string dir, string fileName)
        { try { var baseName = System.IO.Path.GetFileNameWithoutExtension(fileName); var ext = System.IO.Path.GetExtension(fileName); var candidate = fileName; int i = 2; while (System.IO.File.Exists(System.IO.Path.Combine(dir, candidate))) { candidate = $"{baseName} ({i}){ext}"; i++; if (i > 10000) break; } return candidate; } catch { return fileName; } }
    }
}
