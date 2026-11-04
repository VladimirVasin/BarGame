using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    // The two triangles, missing stair cells and porch breaks are shared by
    // placement, rendering and physics. Sampling never constructs a Unity mesh.
    internal sealed class CityHillsideGroundGrid
    {
        internal enum Surface : byte { Cut, Earth, Concrete }

        private static readonly ConditionalWeakTable<CityLayout, CityHillsideGroundGrid[]> Cached = new();
        private readonly float[] x, z, heights;
        private readonly Surface[] surfaces;
        internal CityHillsideStreetRoute Road { get; }
        internal CityLayout Layout { get; }
        internal int Columns => x.Length;
        internal int Rows => z.Length;

        internal static IReadOnlyList<CityHillsideGroundGrid> ForLayout(CityLayout layout) =>
            Cached.GetValue(layout, CreateBanks);

        private static CityHillsideGroundGrid[] CreateBanks(CityLayout layout)
        {
            var roads = CityHillsideStreetPlan.ForLayout(layout).Routes;
            var grids = new CityHillsideGroundGrid[roads.Count];
            var buildings = CityHillsideNeighborhoodPlan.ForLayout(layout).Buildings;
            for (int i = 0; i < roads.Count; i++)
            {
                var houses = new List<CityHillsideBuilding>();
                foreach (var house in buildings)
                    if (house.Bank == roads[i].Bank) houses.Add(house);
                grids[i] = new CityHillsideGroundGrid(layout, roads[i], houses);
            }
            return grids;
        }

        private CityHillsideGroundGrid(CityLayout layout, CityHillsideStreetRoute road, List<CityHillsideBuilding> houses)
        {
            Layout = layout;
            Road = road;
            Rect bounds = CityHillsideNeighborhoodPlan.TerrainBounds(road);
            var xs = new SortedSet<float>();
            var zs = new SortedSet<float>();
            for (float at = bounds.xMin; at < bounds.xMax; at += 3f) xs.Add(at);
            for (float at = bounds.yMin; at < bounds.yMax; at += 3f) zs.Add(at);
            AddBreaks(bounds);
            foreach (var house in houses)
            {
                AddBreaks(house.FoundationBounds);
                AddBreaks(house.CourtBounds);
                AddBreaks(house.ApproachBounds);
            }
            var cuts = new List<Rect>();
            var access = CityHillsideAccessPlan.ForLayout(layout);
            foreach (var stair in access.Shortcuts)
                if (stair.Bank == road.Bank) { cuts.Add(stair.Bounds); AddBreaks(stair.Bounds); }
            foreach (var stair in access.Transfers)
                if (stair.Bank == road.Bank)
                {
                    // The lower apron spans the seam onto the existing city ground.
                    Rect cut = stair.Bounds;
                    cut.yMax = Mathf.Min(cut.yMax, bounds.yMax);
                    cuts.Add(cut);
                    AddBreaks(cut);
                }
            for (int i = 0; i < CityHillsideStreetRoute.LaneCount; i++)
            {
                zs.Add(road.GetLaneZ(i) - CityHillsideStreetRoute.OuterHalfWidth);
                zs.Add(road.GetLaneZ(i) + CityHillsideStreetRoute.OuterHalfWidth);
            }
            x = new float[xs.Count]; xs.CopyTo(x);
            z = new float[zs.Count]; zs.CopyTo(z);
            heights = new float[Columns * Rows];
            surfaces = new Surface[(Columns - 1) * (Rows - 1)];
            for (int row = 0; row < Rows; row++)
            for (int col = 0; col < Columns; col++)
            {
                var p = new Vector2(x[col], z[row]);
                heights[row * Columns + col] = CityHillsideNeighborhoodPlan.SampleBuiltGround(layout, road, p, houses);
                if (col + 1 == Columns || row + 1 == Rows) continue;
                var center = new Vector2((p.x + x[col + 1]) * .5f, (p.y + z[row + 1]) * .5f);
                surfaces[row * (Columns - 1) + col] = Classify(center);
            }

            Surface Classify(Vector2 p)
            {
                foreach (Rect cut in cuts) if (cut.Contains(p)) return Surface.Cut;
                foreach (var house in houses) if (house.TrySampleApproach(p, out _)) return Surface.Concrete;
                return Surface.Earth;
            }
            void AddBreaks(Rect area)
            {
                xs.Add(area.xMin); xs.Add(area.xMax); zs.Add(area.yMin); zs.Add(area.yMax);
            }
        }

        internal Vector3 Vertex(int col, int row) =>
            new Vector3(x[col], heights[row * Columns + col], z[row]);

        internal Surface CellSurface(int col, int row) => surfaces[row * (Columns - 1) + col];

        internal void AppendCellTriangles(List<int> faces, int col, int row)
        {
            int a = row * Columns + col, b = a + Columns;
            faces.Add(a); faces.Add(b); faces.Add(a + 1);
            faces.Add(a + 1); faces.Add(b); faces.Add(b + 1);
        }

        internal bool TryHeight(Vector2 point, out float top)
        {
            top = 0f;
            if (point.x < x[0] || point.x > x[Columns - 1] || point.y < z[0] || point.y > z[Rows - 1]) return false;
            int col = Cell(x, point.x), row = Cell(z, point.y);
            if (CellSurface(col, row) == Surface.Cut)
            {
                // At a cut edge the adjacent uncut cell still owns its shared side.
                bool onLeft = col > 0 && point.x == x[col];
                bool onBottom = row > 0 && point.y == z[row];
                if (onLeft && CellSurface(col - 1, row) != Surface.Cut) col--;
                else if (onBottom && CellSurface(col, row - 1) != Surface.Cut) row--;
                else if (onLeft && onBottom && CellSurface(col - 1, row - 1) != Surface.Cut) { col--; row--; }
                else return false;
            }
            top = Height(col, row, Mathf.InverseLerp(x[col], x[col + 1], point.x),
                Mathf.InverseLerp(z[row], z[row + 1], point.y));
            return true;
        }

        // A clipped piecewise-linear surface peaks at a rectangle corner or at an
        // endpoint of the cell diagonal inside the rectangle. This catches narrow
        // porch and switchback ridges falling between the regular clearance probes.
        internal bool ReachesAbove(Rect rect, float limit)
        {
            if (rect.xMin > x[Columns - 1] || rect.xMax < x[0] || rect.yMin > z[Rows - 1] || rect.yMax < z[0])
                return false;
            int firstCol = Cell(x, rect.xMin), lastCol = Cell(x, rect.xMax);
            int firstRow = Cell(z, rect.yMin), lastRow = Cell(z, rect.yMax);
            for (int row = firstRow; row <= lastRow; row++)
            for (int col = firstCol; col <= lastCol; col++)
            {
                if (CellSurface(col, row) == Surface.Cut) continue;
                float u0 = Mathf.InverseLerp(x[col], x[col + 1], rect.xMin);
                float u1 = Mathf.InverseLerp(x[col], x[col + 1], rect.xMax);
                float v0 = Mathf.InverseLerp(z[row], z[row + 1], rect.yMin);
                float v1 = Mathf.InverseLerp(z[row], z[row + 1], rect.yMax);
                if (Height(col, row, u0, v0) > limit || Height(col, row, u1, v0) > limit ||
                    Height(col, row, u0, v1) > limit || Height(col, row, u1, v1) > limit) return true;
                float lo = Mathf.Max(v0, 1f - u1), hi = Mathf.Min(v1, 1f - u0);
                if (lo <= hi && (Height(col, row, 1f - lo, lo) > limit ||
                                 Height(col, row, 1f - hi, hi) > limit)) return true;
            }
            return false;
        }

        private float Height(int col, int row, float u, float v)
        {
            int a = row * Columns + col, b = a + Columns;
            float left = heights[a], right = heights[a + 1], far = heights[b], opposite = heights[b + 1];
            return u + v <= 1f ? left + (right - left) * u + (far - left) * v :
                opposite + (far - opposite) * (1f - u) + (right - opposite) * (1f - v);
        }

        private static int Cell(float[] axis, float value)
        {
            int found = Array.BinarySearch(axis, value);
            return Mathf.Clamp(found >= 0 ? found : ~found - 1, 0, axis.Length - 2);
        }
    }
}
