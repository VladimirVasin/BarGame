using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Chart data for the village tab.
    ///
    /// Shares the mountain chart container: the main lane retains the travel
    /// endpoints, secondary segments show the forest and old roads, and the
    /// original inhabited bowl remains distinct from the full display extent.
    /// </summary>
    public static class CityMapAlpineVillageOverlayBuilder
    {
        /// <summary>
        /// How often the lane is sampled for the chart. Coarser than the
        /// plan's own metre, because a map line does not need every bend and
        /// eighty points of it would draw as a smear.
        /// </summary>
        public const float LaneChartSpacing = 3.5f;

        public static CityMapMountainRoadOverlay Create(
            AlpineVillagePlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            var lane = new List<Vector3>(
                Mathf.CeilToInt(plan.Lane.Length / LaneChartSpacing) + 2);

            // The station first, so the polyline starts where the cabin puts
            // the player down - the tab's travel target reads point zero.
            lane.Add(plan.Station.PadArea.Center);
            for (float distance = 0f;
                 distance < plan.Lane.Length;
                 distance += LaneChartSpacing)
            {
                lane.Add(plan.Lane.Sample(distance).Position);
            }

            lane.Add(plan.Lane.End);
            CityMapMountainRoadOverlay chart = CityMapMountainRoadOverlayBuilder.Create(
                lane, plan.TerrainBounds);
            var branches = new List<CityMapRouteSegment>();
            foreach (AlpineVillagePathDescriptor path in plan.Expansion.Paths)
            {
                branches.Add(new CityMapRouteSegment(path.Start, path.End));
            }

            return new CityMapMountainRoadOverlay(lane, new List<Vector3>(),
                new List<CityMapMountainHatchSegment>(),
                new List<MountainRoadTerminalLandmark>(), false, Vector3.zero,
                plan.CoreTerrainBounds, chart.DisplayWorldXZBounds, branches);
        }
    }
}
