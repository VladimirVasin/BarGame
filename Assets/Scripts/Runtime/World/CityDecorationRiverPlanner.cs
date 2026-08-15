using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static partial class CityDecorationPlanner
    {
        private static bool TryFindParkPosition(
            CityLayout layout,
            RoadFencePlan fencePlan,
            CityNightFixturePlan nightPlan,
            ICollection<Vector3> occupiedGroundPositions,
            int xSign,
            int zSign,
            float radius,
            out Vector3 position)
        {
            CityParkRegionPlan region = ResolveParkRegion(layout, xSign);
            Rect bounds = region.WalkableBounds;
            float margin = radius + 0.75f;
            float[] fractions = { 0.30f, 0.38f, 0.22f, 0.44f, 0.16f };
            for (int xIndex = 0; xIndex < fractions.Length; xIndex++)
            {
                for (int zIndex = 0; zIndex < fractions.Length; zIndex++)
                {
                    Vector3 candidate = region.Center + new Vector3(
                        xSign * bounds.width * fractions[xIndex],
                        0f,
                        zSign * bounds.height * fractions[zIndex]);
                    if (CityTerrainSurfacePlan.TrySampleGroundTop(
                            layout,
                            new Vector2(candidate.x, candidate.z),
                            out float groundTop,
                            out _))
                    {
                        candidate.y = groundTop;
                    }

                    if (candidate.x < bounds.xMin + margin ||
                        candidate.x > bounds.xMax - margin ||
                        candidate.z < bounds.yMin + margin ||
                        candidate.z > bounds.yMax - margin ||
                        !IsParkCandidateClear(
                            layout,
                            region,
                            candidate,
                            radius,
                            fencePlan,
                            nightPlan,
                            occupiedGroundPositions))
                    {
                        continue;
                    }

                    position = candidate;
                    return true;
                }
            }

            const int xSteps = 9;
            const int zSteps = 15;
            float xMinimum = bounds.xMin + margin;
            float xMaximum = bounds.xMax - margin;
            float zMinimum = bounds.yMin + margin;
            float zMaximum = bounds.yMax - margin;
            for (int xIndex = 0; xIndex < xSteps; xIndex++)
            {
                float xAmount = (xIndex + 0.5f) / xSteps;
                if (xSign > 0)
                {
                    xAmount = 1f - xAmount;
                }

                for (int zIndex = 0; zIndex < zSteps; zIndex++)
                {
                    float zAmount = (zIndex + 0.5f) / zSteps;
                    if (zSign > 0)
                    {
                        zAmount = 1f - zAmount;
                    }

                    Vector3 candidate = new Vector3(
                        Mathf.Lerp(xMinimum, xMaximum, xAmount),
                        0f,
                        Mathf.Lerp(zMinimum, zMaximum, zAmount));
                    if (CityTerrainSurfacePlan.TrySampleGroundTop(
                            layout,
                            new Vector2(candidate.x, candidate.z),
                            out float groundTop,
                            out _))
                    {
                        candidate.y = groundTop;
                    }

                    if (IsParkCandidateClear(
                            layout,
                            region,
                            candidate,
                            radius,
                            fencePlan,
                            nightPlan,
                            occupiedGroundPositions))
                    {
                        position = candidate;
                        return true;
                    }
                }
            }

            position = default;
            return false;
        }

        private static bool IsParkCandidateClear(
            CityLayout layout,
            CityParkRegionPlan region,
            Vector3 position,
            float radius,
            RoadFencePlan fencePlan,
            CityNightFixturePlan nightPlan,
            ICollection<Vector3> occupiedGroundPositions)
        {
            float pathClearance = Mathf.Min(
                5.4f + radius,
                Mathf.Min(
                    region.WalkableBounds.width,
                    region.WalkableBounds.height) * 0.18f);
            Vector3 centerOffset = position - region.Center;
            if (Mathf.Abs(centerOffset.x) < pathClearance ||
                Mathf.Abs(centerOffset.z) < pathClearance ||
                CityDecorationValidator.IsProtectedGroundAnchor(
                    position,
                    radius,
                    fencePlan,
                    nightPlan) ||
                !IsSeparated(position, occupiedGroundPositions, radius + 3f))
            {
                return false;
            }

            for (int index = 0;
                 index < layout.Park.TreePositions.Count;
                 index++)
            {
                if (PlanarSquaredDistance(
                        position,
                        layout.Park.TreePositions[index]) <
                    (radius + 1.8f) * (radius + 1.8f))
                {
                    return false;
                }
            }

            for (int index = 0;
                 index < layout.Park.BenchPositions.Count;
                 index++)
            {
                if (PlanarSquaredDistance(
                        position,
                        layout.Park.BenchPositions[index]) <
                    (radius + 1.25f) * (radius + 1.25f))
                {
                    return false;
                }
            }

            return true;
        }

        private static CityParkRegionPlan ResolveParkRegion(
            CityLayout layout,
            int xSign)
        {
            if (layout.Park.Regions.Count == 1)
            {
                return layout.Park.Regions[0];
            }

            CityParkRegionPlan selected = layout.Park.Regions[0];
            for (int index = 1;
                 index < layout.Park.Regions.Count;
                 index++)
            {
                CityParkRegionPlan candidate = layout.Park.Regions[index];
                if ((xSign < 0 && candidate.Center.x < selected.Center.x) ||
                    (xSign >= 0 && candidate.Center.x > selected.Center.x))
                {
                    selected = candidate;
                }
            }

            return selected;
        }
    }
}
