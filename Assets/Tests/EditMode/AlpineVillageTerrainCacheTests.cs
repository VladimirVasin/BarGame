using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class AlpineVillageTerrainCacheTests
    {
        private AlpineVillagePlan plan;

        [OneTimeSetUp]
        public void CreatePlan()
        {
            plan = CityLayoutCache.GetOrCreateAlpineVillage(GameSessionState.DefaultCitySeed);
        }

        [Test]
        [Category("AlpineVillage")]
        public void GridAndMeshHeights_MatchUncachedGroundAtVerticesTrianglesAndBounds()
        {
            AlpineVillageTerrainGrid grid = AlpineVillageTerrainGrid.Get(plan);
            var cells = new HashSet<Vector2Int>();
            for (int row = 0; row <= 6; row++)
            for (int column = 0; column <= 6; column++)
                cells.Add(new Vector2Int((grid.Columns - 1) * column / 6,
                    (grid.Rows - 1) * row / 6));
            foreach (Vector3 landmark in new[]
            {
                plan.Lane.Sample(plan.Lane.Length * .5f).Position,
                plan.Brook.BowlCenter,
                plan.Brook.Samples[plan.Brook.Samples.Count / 2].Position,
                plan.MothersHouse.GroundCenter,
                plan.Expansion.LodgeCenter,
                plan.Expansion.CliffEdge
            })
                cells.Add(new Vector2Int(grid.FindColumn(landmark.x), grid.FindRow(landmark.z)));

            // The two off-diagonal fractions exercise both terrain triangles;
            // exact corners and the shared diagonal also retain their arithmetic.
            Vector2[] fractions =
            {
                Vector2.zero, new Vector2(.22f, .31f), new Vector2(.5f, .5f),
                new Vector2(.78f, .69f), Vector2.one
            };
            foreach (Vector2Int cell in cells)
            {
                for (int dz = 0; dz <= 1; dz++)
                for (int dx = 0; dx <= 1; dx++)
                {
                    int column = cell.x + dx, row = cell.y + dz;
                    var point = new Vector2(grid.XCoordinates[column], grid.ZCoordinates[row]);
                    float expected = AlpineVillageTerrainSampler.SampleHeight(plan, point);
                    AssertExact(grid.SampleHeight(column, row), expected, "grid " + point);
                    AssertExact(grid.SampleHeight(column, row), expected, "cached grid " + point);
                }
                foreach (Vector2 fraction in fractions)
                {
                    var point = new Vector2(
                        Mathf.Lerp(grid.XCoordinates[cell.x], grid.XCoordinates[cell.x + 1], fraction.x),
                        Mathf.Lerp(grid.ZCoordinates[cell.y], grid.ZCoordinates[cell.y + 1], fraction.y));
                    AssertMeshHeight(plan, grid, point);
                }
            }

            Rect bounds = plan.TerrainMeshBounds;
            foreach (float x in new[] { bounds.xMin - 11f, bounds.xMin, bounds.center.x,
                         bounds.xMax, bounds.xMax + 11f })
            foreach (float z in new[] { bounds.yMin - 11f, bounds.yMin, bounds.center.y,
                         bounds.yMax, bounds.yMax + 11f })
                AssertMeshHeight(plan, grid, new Vector2(x, z));
        }

        [Test]
        [Category("AlpineVillage")]
        public void GridCache_SeparatesPlansAndRebuildsAfterBrookAttachment()
        {
            AlpineVillageTerrainGrid original = AlpineVillageTerrainGrid.Get(plan);
            AlpineVillagePlan assembling = WithoutBrook(plan);
            AlpineVillageTerrainGrid before = AlpineVillageTerrainGrid.Get(assembling);
            Assert.That(before, Is.Not.SameAs(original), "Equal seeds do not make plans interchangeable.");
            Assert.That(AlpineVillageTerrainGrid.Get(assembling), Is.SameAs(before));

            // Warm vertices near the future channel before the only mutable
            // height input arrives. Coarse coordinates remain in the fine grid.
            var heightsBefore = new Dictionary<Vector2, float>();
            int stride = Mathf.Max(1, plan.Brook.Samples.Count / 12);
            for (int index = 0; index < plan.Brook.Samples.Count; index += stride)
            {
                Vector3 sample = plan.Brook.Samples[index].Position;
                int column = before.FindColumn(sample.x), row = before.FindRow(sample.z);
                for (int dz = 0; dz <= 1; dz++)
                for (int dx = 0; dx <= 1; dx++)
                {
                    var point = new Vector2(before.XCoordinates[column + dx], before.ZCoordinates[row + dz]);
                    float height = before.SampleHeight(column + dx, row + dz);
                    AssertExact(height, AlpineVillageTerrainSampler.SampleHeight(assembling, point),
                        "unattached brook " + point);
                    heightsBefore[point] = height;
                }
            }

            assembling.AttachBrook(plan.Brook);
            AlpineVillageTerrainGrid after = AlpineVillageTerrainGrid.Get(assembling);
            Assert.That(after, Is.Not.SameAs(before), "Brook attachment invalidates heights as well as axes.");
            bool channelChangedHeight = false;
            foreach (KeyValuePair<Vector2, float> sample in heightsBefore)
            {
                int column = Array.IndexOf(after.XCoordinates, sample.Key.x);
                int row = Array.IndexOf(after.ZCoordinates, sample.Key.y);
                Assert.That(column, Is.GreaterThanOrEqualTo(0), "The refined grid discarded an original X.");
                Assert.That(row, Is.GreaterThanOrEqualTo(0), "The refined grid discarded an original Z.");
                float expected = AlpineVillageTerrainSampler.SampleHeight(assembling, sample.Key);
                channelChangedHeight |= Mathf.Abs(expected - sample.Value) > .0001f;
                AssertExact(after.SampleHeight(column, row), expected, "attached brook " + sample.Key);
                AssertMeshHeight(assembling, after, sample.Key);
            }
            Assert.That(channelChangedHeight, Is.True, "The warmed vertices must include terrain cut by the brook.");

            AlpineVillageTerrainGrid restored = AlpineVillageTerrainGrid.Get(plan);
            Assert.That(restored, Is.Not.SameAs(after));
            AssertMeshHeight(plan, restored, new Vector2(plan.Brook.BowlCenter.x, plan.Brook.BowlCenter.z));
        }

        private static AlpineVillagePlan WithoutBrook(AlpineVillagePlan source)
        {
            return new AlpineVillagePlan(source.Seed, source.SlopeOrigin, source.Uphill, source.Grade,
                source.Lane, source.Station, source.MothersHouse, source.MothersHouseReturnPosition,
                new List<AlpineVillagePlotDescriptor>(source.Plots),
                new List<AlpineVillageRidgeDescriptor>(source.Ridges), source.CoreTerrainBounds,
                source.TerrainMeshBounds, source.WorldBounds, source.SpawnPosition, source.SpawnForward);
        }

        private static void AssertMeshHeight(AlpineVillagePlan source, AlpineVillageTerrainGrid grid, Vector2 point)
        {
            // Reference the analytic corners directly: going through the memo
            // here would let stale or aliased cache values validate themselves.
            int column = grid.FindColumn(point.x), row = grid.FindRow(point.y);
            float nearX = grid.XCoordinates[column], farX = grid.XCoordinates[column + 1];
            float nearZ = grid.ZCoordinates[row], farZ = grid.ZCoordinates[row + 1];
            float u = Mathf.InverseLerp(nearX, farX, point.x);
            float v = Mathf.InverseLerp(nearZ, farZ, point.y);
            float nearRight = AlpineVillageTerrainSampler.SampleHeight(source, new Vector2(farX, nearZ));
            float farLeft = AlpineVillageTerrainSampler.SampleHeight(source, new Vector2(nearX, farZ));
            float expected;
            if (u + v <= 1f)
            {
                float nearLeft = AlpineVillageTerrainSampler.SampleHeight(source, new Vector2(nearX, nearZ));
                expected = nearLeft + u * (nearRight - nearLeft) + v * (farLeft - nearLeft);
            }
            else
            {
                float farRight = AlpineVillageTerrainSampler.SampleHeight(source, new Vector2(farX, farZ));
                expected = farRight + (1f - v) * (nearRight - farRight) + (1f - u) * (farLeft - farRight);
            }
            AssertExact(AlpineVillageTerrainSampler.SampleMeshHeight(source, point), expected, "mesh " + point);
            AssertExact(AlpineVillageTerrainSampler.SampleMeshHeight(source, point), expected, "cached mesh " + point);
        }

        private static void AssertExact(float actual, float expected, string context)
        {
            Assert.That(BitConverter.SingleToInt32Bits(actual),
                Is.EqualTo(BitConverter.SingleToInt32Bits(expected)), context);
        }
    }
}
