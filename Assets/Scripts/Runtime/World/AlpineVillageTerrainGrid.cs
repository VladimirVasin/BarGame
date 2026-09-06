using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Shared rectilinear ground axes. Original two-metre vertices stay exact;
    /// only intervals near the brook receive extra coordinates. Full rows and
    /// columns meet at every edge, so refinement introduces no T-junctions.
    /// </summary>
    internal sealed class AlpineVillageTerrainGrid
    {
        private const float BrookMargin = 2f;
        private static AlpineVillagePlan cachedPlan;
        private static AlpineVillageBrookPlan cachedBrook;
        private static AlpineVillageTerrainGrid cachedGrid;

        private AlpineVillageTerrainGrid(AlpineVillagePlan plan)
        {
            Rect bounds = plan.TerrainMeshBounds;
            AlpineVillageBrookPlan brook = plan.Brook;
            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float minZ = float.PositiveInfinity;
            float maxZ = float.NegativeInfinity;
            if (brook != null && brook.Samples.Count > 0)
            {
                // Include the catch and its short spill as well as the channel.
                float catchRadius = brook.CatchOuterSize.magnitude * 0.5f + BrookMargin;
                minX = brook.BowlCenter.x - catchRadius;
                maxX = brook.BowlCenter.x + catchRadius;
                minZ = brook.BowlCenter.z - catchRadius;
                maxZ = brook.BowlCenter.z + catchRadius;
                foreach (AlpineVillageBrookSample sample in brook.Samples)
                {
                    float reach = sample.HalfWidth + BrookMargin;
                    minX = Mathf.Min(minX, sample.Position.x - reach);
                    maxX = Mathf.Max(maxX, sample.Position.x + reach);
                    minZ = Mathf.Min(minZ, sample.Position.z - reach);
                    maxZ = Mathf.Max(maxZ, sample.Position.z + reach);
                }
            }

            XCoordinates = BuildAxis(bounds.xMin, bounds.width, minX, maxX);
            ZCoordinates = BuildAxis(bounds.yMin, bounds.height, minZ, maxZ);
        }

        internal float[] XCoordinates { get; }
        internal float[] ZCoordinates { get; }
        internal int Columns => XCoordinates.Length - 1;
        internal int Rows => ZCoordinates.Length - 1;

        internal static AlpineVillageTerrainGrid Get(AlpineVillagePlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            // Keep one small axis cache, not heights. Plans can receive their
            // brook after initial planning, so both references own validity.
            if (cachedGrid == null || !ReferenceEquals(cachedPlan, plan) ||
                !ReferenceEquals(cachedBrook, plan.Brook))
            {
                cachedGrid = new AlpineVillageTerrainGrid(plan);
                cachedPlan = plan;
                cachedBrook = plan.Brook;
            }
            return cachedGrid;
        }

        internal int FindColumn(float x) => FindInterval(XCoordinates, x);
        internal int FindRow(float z) => FindInterval(ZCoordinates, z);

        private static float[] BuildAxis(
            float minimum, float length, float fineMinimum, float fineMaximum)
        {
            int cells = Mathf.Max(1, Mathf.CeilToInt(
                length / AlpineVillageTerrainSampler.TerrainCell));
            var axis = new List<float>(cells + 1) { minimum };
            for (int cell = 0; cell < cells; cell++)
            {
                // This is exactly the original BuildTerrain vertex arithmetic.
                float start = minimum + length * (cell / (float)cells);
                float end = minimum + length * ((cell + 1) / (float)cells);
                int subdivisions = end > fineMinimum && start < fineMaximum
                    ? Mathf.Max(1, Mathf.CeilToInt(
                        (end - start) / AlpineVillageTerrainSampler.BrookTerrainCell))
                    : 1;
                for (int step = 1; step <= subdivisions; step++)
                {
                    axis.Add(step == subdivisions ? end :
                        start + (end - start) * (step / (float)subdivisions));
                }
            }
            return axis.ToArray();
        }

        private static int FindInterval(float[] axis, float position)
        {
            int low = 0;
            int high = axis.Length - 1;
            while (high - low > 1)
            {
                int middle = (low + high) / 2;
                if (axis[middle] <= position) low = middle;
                else high = middle;
            }
            return low;
        }
    }
}
