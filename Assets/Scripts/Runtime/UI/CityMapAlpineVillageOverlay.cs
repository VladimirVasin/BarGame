using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Chart data for the village tab.
    ///
    /// It reuses <see cref="CityMapMountainRoadOverlay"/> rather than defining
    /// a second type, because both tabs are the same drawing: one polyline and
    /// one patch of ground. Here the polyline is the lane and the patch is the
    /// walkable extent. Landmarks are deliberately left empty - the places up
    /// there reach the chart as map POINTS, which is what the inspector and
    /// the teleport already read.
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
            return CityMapMountainRoadOverlayBuilder.Create(
                lane,
                plan.TerrainBounds);
        }
    }
}
