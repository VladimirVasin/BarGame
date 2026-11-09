using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    // This is the mesh's surveyed grid, including its bent ravine columns.
    // Rendering, collision and standing heights use these same triangles.
    internal sealed class CityHillsideQuarterGroundGrid
    {
        internal enum Surface : byte { Cut, Earth, Rock, Scree }
        private static readonly ConditionalWeakTable<CityLayout, CityHillsideQuarterGroundGrid> Cached = new();
        private readonly Vector3[] vertices;
        private readonly float[] z;
        private readonly Surface[] surfaces;
        internal int Columns { get; }
        internal int Rows => z.Length;

        internal static CityHillsideQuarterGroundGrid ForLayout(CityLayout layout) =>
            Cached.GetValue(layout, value => new CityHillsideQuarterGroundGrid(value));

        private CityHillsideQuarterGroundGrid(CityLayout layout)
        {
            var plan = CityHillsideQuarterPlan.ForLayout(layout);
            if (plan.Links.Count == 0)
            {
                vertices = Array.Empty<Vector3>(); z = Array.Empty<float>(); surfaces = Array.Empty<Surface>();
                return;
            }
            Rect bounds = plan.TerrainBounds;
            var xs = new SortedSet<float> { bounds.xMin, bounds.xMax, bounds.xMin + 9f, bounds.xMax - 9f };
            var zs = new SortedSet<float> { bounds.yMin, bounds.yMax, plan.Links[0].WestGateWorld.z - 20f };
            for (float x = bounds.xMin; x <= bounds.xMax; x += 3f) xs.Add(x);
            for (float at = bounds.yMin; at <= bounds.yMax; at += 3f) zs.Add(at);
            var bank = CityHillsideStreetPlan.ForLayout(layout).Routes[0];
            for (int i = 0; i < CityHillsideStreetRoute.LaneCount; i++)
            {
                float at = bank.GetLaneZ(i);
                if (at - 5f > bounds.yMin && at - 5f < bounds.yMax) zs.Add(at - 5f);
                if (at + 5f > bounds.yMin && at + 5f < bounds.yMax) zs.Add(at + 5f);
            }
            foreach (var link in plan.Links) { zs.Add(link.Bounds.yMin); zs.Add(link.Bounds.yMax); }
            var court = CityHillsideAccessPlan.ForLayout(layout).SharedCourt;
            foreach (Rect cut in court.WalkableBounds)
            {
                xs.Add(cut.xMin); xs.Add(cut.xMax); zs.Add(cut.yMin); zs.Add(cut.yMax);
            }
            xs.Add(CityHillsideQuarterRavine.MeshWest); xs.Add(CityHillsideQuarterRavine.MeshEast);
            foreach (var p in CityHillsideQuarterRavine.Profile)
                xs.Add(Mathf.Lerp(CityHillsideQuarterRavine.TemplateWest, CityHillsideQuarterRavine.TemplateEast, p.x));
            foreach (var section in CityHillsideQuarterRavine.Sections) zs.Add(section.x);
            var pads = CityHillsideFurniturePlan.GroundPads(layout);
            var ramps = CityHillsideFurniturePlan.GroundRamps(layout);
            var seams = CityHillsideGroundSeamPlan.ForLayout(layout);
            var talus = CityMountainTalusPlan.ForLayout(layout);
            CityHillsideGroundPlan.AddPadBreaks(bounds, pads, xs, zs);
            CityHillsideGroundPlan.AddRampBreaks(bounds, ramps, xs, zs);
            seams.AddBreaks(bounds, xs, zs);
            talus.AddBreaks(bounds, xs, zs);
            var xAxis = new List<float>(xs);
            z = new float[zs.Count]; zs.CopyTo(z);
            Columns = xAxis.Count;
            vertices = new Vector3[Columns * Rows];
            surfaces = new Surface[(Columns - 1) * (Rows - 1)];
            for (int row = 0; row < Rows; row++)
            {
                var section = CityHillsideQuarterRavine.At(z[row]);
                for (int col = 0; col < Columns; col++)
                {
                    Vector2 point = new(CityHillsideQuarterRavine.MeshX(xAxis[col], section), z[row]);
                    float ground = CityHillsideGroundPlan.ApplyRamps(point,
                        CityHillsideGroundPlan.ApplyPads(point, seams.Apply(point, plan.SampleSoil(point)), pads), ramps);
                    vertices[row * Columns + col] = new Vector3(point.x, talus.ApplyHeight(point, ground), point.y);
                    if (col + 1 == Columns || row + 1 == Rows) continue;
                    float midZ = (z[row] + z[row + 1]) * .5f;
                    float templateX = (xAxis[col] + xAxis[col + 1]) * .5f;
                    var centre = new Vector2(CityHillsideQuarterRavine.MeshX(templateX, section), midZ);
                    Surface kind = Surface.Earth;
                    foreach (Rect cut in court.WalkableBounds)
                        if (cut.Contains(centre)) { kind = Surface.Cut; break; }
                    if (kind != Surface.Cut && CityHillsideQuarterRavine.IsOpenRockColumn(templateX, midZ))
                    {
                        float across = Mathf.InverseLerp(CityHillsideQuarterRavine.TemplateWest,
                            CityHillsideQuarterRavine.TemplateEast, templateX);
                        kind = section.w > 4f && across > .37f && across < .61f + .035f * Mathf.Sin(midZ * .21f)
                            ? Surface.Scree : Surface.Rock;
                    }
                    surfaces[row * (Columns - 1) + col] = kind;
                }
            }
        }

        internal Vector3 Vertex(int col, int row) => vertices[row * Columns + col];
        internal Surface CellSurface(int col, int row) => surfaces[row * (Columns - 1) + col];

        internal bool TryHeight(Vector2 point, out float top)
        {
            top = 0f;
            if (Rows < 2 || point.y < z[0] || point.y > z[Rows - 1]) return false;
            int found = Array.BinarySearch(z, point.y);
            int row = Mathf.Clamp(found >= 0 ? found : ~found - 1, 0, Rows - 2);
            float along = Mathf.InverseLerp(z[row], z[row + 1], point.y);
            // Rows have the same ordered columns; their X values bend linearly.
            int lo = 0, hi = Columns - 1;
            if (point.x < X(0) || point.x > X(hi)) return false;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (point.x < X(mid)) hi = mid; else lo = mid;
            }
            if (SampleCell(lo, row, point, out top)) return true;
            // At a cut edge, the uncut neighbour still owns the shared edge.
            bool onLeft = lo > 0 && Mathf.Abs(point.x - X(lo)) < .0001f;
            bool onBottom = row > 0 && Mathf.Abs(point.y - z[row]) < .0001f;
            if (onLeft && SampleCell(lo - 1, row, point, out top)) return true;
            if (onBottom && SampleCell(lo, row - 1, point, out top)) return true;
            if (onLeft && onBottom && SampleCell(lo - 1, row - 1, point, out top)) return true;
            return false;

            float X(int col) => Mathf.Lerp(Vertex(col, row).x, Vertex(col, row + 1).x, along);
        }

        private bool SampleCell(int col, int row, Vector2 point, out float top)
        {
            top = 0f;
            if (CellSurface(col, row) == Surface.Cut) return false;
            Vector3 a = Vertex(col, row), b = Vertex(col, row + 1), c = Vertex(col + 1, row), d = Vertex(col + 1, row + 1);
            return TriangleHeight(a, b, c, point, out top) || TriangleHeight(c, b, d, point, out top);
        }

        private static bool TriangleHeight(Vector3 a, Vector3 b, Vector3 c, Vector2 p, out float top)
        {
            Vector2 ab = new(b.x - a.x, b.z - a.z), ac = new(c.x - a.x, c.z - a.z), ap = new(p.x - a.x, p.y - a.z);
            float determinant = CityRoadPolygon.Cross(ab, ac);
            if (Mathf.Abs(determinant) < .000001f) { top = 0f; return false; }
            float u = CityRoadPolygon.Cross(ap, ac) / determinant;
            float v = CityRoadPolygon.Cross(ab, ap) / determinant;
            top = a.y + u * (b.y - a.y) + v * (c.y - a.y);
            return u >= -.0001f && v >= -.0001f && u + v <= 1.0001f;
        }
    }
}
