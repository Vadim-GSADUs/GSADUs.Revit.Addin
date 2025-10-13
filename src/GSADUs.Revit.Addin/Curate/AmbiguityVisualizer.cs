using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GSADUs.Revit.Addin
{
    internal static class AmbiguityVisualizer
    {
        /// <summary>
        /// Draw model-line rectangles for sets whose inflated bounding boxes (IBBs) intersect.
        /// - Uses active plan view; if not a plan, falls back to first non-template plan view.
        /// - Draws at the plan level elevation.
        /// </summary>
        /// <param name="doc">Revit document</param>
        /// <param name="activeView">Current active view</param>
        /// <param name="setIbbs">Map: set name ? inflated BoundingBoxXYZ</param>
        /// <param name="candidateSetNames">Sets to consider (already flagged ambiguous or all)</param>
        public static void DrawAmbiguousIbbRectangles(
            Document doc,
            View activeView,
            IDictionary<string, BoundingBoxXYZ> setIbbs,
            ISet<string> candidateSetNames)
        {
            if (doc == null || setIbbs == null || candidateSetNames == null) return;
            if (setIbbs.Count == 0 || candidateSetNames.Count == 0) return;

            // Determine which candidates actually intersect at IBB level
            var names = setIbbs.Keys.ToList();
            var drawNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < names.Count; i++)
            {
                var ni = names[i];
                if (!candidateSetNames.Contains(ni)) continue;
                var bi = setIbbs[ni];
                if (bi == null) continue;

                for (int j = i + 1; j < names.Count; j++)
                {
                    var nj = names[j];
                    if (!candidateSetNames.Contains(nj)) continue;
                    var bj = setIbbs[nj];
                    if (bj == null) continue;

                    if (Intersects(bi, bj))
                    {
                        drawNames.Add(ni);
                        drawNames.Add(nj);
                    }
                }
            }

            if (drawNames.Count == 0) return;

            // Choose plan view
            ViewPlan planView = activeView as ViewPlan
                ?? new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewPlan))
                        .Cast<ViewPlan>()
                        .FirstOrDefault(vp => !vp.IsTemplate);

            if (planView == null) return;

            double z = 0.0;
            try { z = planView.GenLevel?.Elevation ?? 0.0; } catch { /* ignore */ }

            var plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, z));

            using (var tx = new Transaction(doc, "Ambiguous Rectangles (Preview)"))
            {
                tx.Start();
                var sp = SketchPlane.Create(doc, plane);

                foreach (var name in drawNames)
                {
                    if (!setIbbs.TryGetValue(name, out var bb) || bb == null) continue;

                    var min = bb.Min; var max = bb.Max;
                    var p1 = new XYZ(min.X, min.Y, z);
                    var p2 = new XYZ(max.X, min.Y, z);
                    var p3 = new XYZ(max.X, max.Y, z);
                    var p4 = new XYZ(min.X, max.Y, z);

                    CreateModelLine(doc, sp, p1, p2);
                    CreateModelLine(doc, sp, p2, p3);
                    CreateModelLine(doc, sp, p3, p4);
                    CreateModelLine(doc, sp, p4, p1);
                }

                tx.Commit();
            }
        }

        private static void CreateModelLine(Document doc, SketchPlane sp, XYZ a, XYZ b)
        {
            var line = Line.CreateBound(a, b);
            var mc = doc.Create.NewModelCurve(line, sp) as ModelCurve;
            if (mc != null)
            {
                try
                {
                    var p = mc.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    if (p != null && !p.IsReadOnly)
                    {
                        p.Set("Ambiguity IBB");
                    }
                }
                catch { }
            }
        }

        private static bool Intersects(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            if (a == null || b == null) return false;
            return !(a.Max.X < b.Min.X || b.Max.X < a.Min.X ||
                     a.Max.Y < b.Min.Y || b.Max.Y < a.Min.Y ||
                     a.Max.Z < b.Min.Z || b.Max.Z < a.Min.Z);
        }
    }
}
