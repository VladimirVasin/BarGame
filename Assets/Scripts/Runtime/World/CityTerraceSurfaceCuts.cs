using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Finite cuts in existing imported street topology; the terrace kit owns the replacement.</summary>
    internal static class CityTerraceSurfaceCuts
    {
        internal static bool Reserves(CityLayout layout, Vector3 position, float radius = 0f)
        {
            CityTerraceDistrictPlan plan = CityTerraceDistrictPlan.ForLayout(layout);
            Rect bounds = plan.ReservedBounds;
            return plan.IsEnabled && (Inside(bounds) || Inside(plan.LowerPassBounds));

            bool Inside(Rect area) => position.x >= area.xMin - radius && position.x <= area.xMax + radius &&
                position.z >= area.yMin - radius && position.z <= area.yMax + radius;
        }

        internal static bool Overlaps(CityTerraceDistrictPlan plan, Rect bounds)
        {
            if (!plan.IsEnabled || !plan.ReservedBounds.Overlaps(bounds, true) &&
                !plan.LowerPassBounds.Overlaps(bounds, true)) return false;
            foreach (Rect cut in plan.GroundCuts) if (cut.Overlaps(bounds, true)) return true;
            return false;
        }

        internal static List<Vector3[]> Outside(IReadOnlyList<Vector3> polygon, CityTerraceDistrictPlan plan)
        {
            var flat = new Vector2[polygon.Count];
            for (int i = 0; i < flat.Length; i++) flat[i] = new Vector2(polygon[i].x, polygon[i].z);
            var pieces = new List<Vector2[]> { CityRoadPolygon.CounterClockwise(flat) };
            foreach (Rect cut in plan.GroundCuts)
            {
                var next = new List<Vector2[]>();
                foreach (Vector2[] piece in pieces)
                    if (CityRoadPolygon.Bounds(piece).Overlaps(cut))
                        next.AddRange(CityRoadPolygon.Subtract(piece, CityRoadPolygon.Rectangle(cut)));
                    else next.Add(piece);
                pieces = next;
            }
            Vector3 first = polygon[0];
            Vector3 normal = Vector3.Cross(polygon[1] - first, polygon[2] - first);
            var result = new List<Vector3[]>(pieces.Count);
            foreach (Vector2[] piece in pieces)
            {
                var face = new Vector3[piece.Length];
                for (int i = 0; i < face.Length; i++)
                {
                    Vector2 p = piece[i];
                    face[i] = new Vector3(p.x, first.y -
                        (normal.x * (p.x - first.x) + normal.z * (p.y - first.z)) / normal.y, p.y);
                }
                result.Add(face);
            }
            return result;
        }

        internal static List<Vector2> OutsideIntervals(Vector3 a, Vector3 b, CityTerraceDistrictPlan plan)
        {
            var intervals = new List<Vector2> { new Vector2(0f, 1f) };
            foreach (Rect cut in plan.GroundCuts)
            {
                float first = 0f, last = 1f;
                if (!Clip(a.x, b.x - a.x, cut.xMin, cut.xMax, ref first, ref last) ||
                    !Clip(a.z, b.z - a.z, cut.yMin, cut.yMax, ref first, ref last)) continue;
                var next = new List<Vector2>();
                foreach (Vector2 interval in intervals)
                {
                    if (last <= interval.x || first >= interval.y) { next.Add(interval); continue; }
                    if (first > interval.x + .00001f) next.Add(new Vector2(interval.x, first));
                    if (last < interval.y - .00001f) next.Add(new Vector2(last, interval.y));
                }
                intervals = next;
            }
            return intervals;
        }

        private static bool Clip(float start, float delta, float minimum, float maximum, ref float first, ref float last)
        {
            if (Mathf.Abs(delta) < .000001f) return start >= minimum && start <= maximum;
            float a = (minimum - start) / delta, b = (maximum - start) / delta;
            first = Mathf.Max(first, Mathf.Min(a, b)); last = Mathf.Min(last, Mathf.Max(a, b));
            return first <= last;
        }
    }
}
