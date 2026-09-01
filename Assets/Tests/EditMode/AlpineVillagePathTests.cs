using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class AlpineVillagePathTests
    {
        [Test]
        [Category("AlpineVillage")]
        public void VisiblePaths_CarryEveryPermittedBranch()
        {
            AlpineVillagePlan plan = AlpineVillagePlanner.Create(
                GameSessionState.DefaultCitySeed);
            IReadOnlyList<AlpineVillagePathDescriptor> paths =
                AlpineVillagePathPlanner.Create(plan);
            var walkable = new AlpineVillageWalkableArea(plan);
            float radius = CityGroundTraversalPlanner.MaximumAgentRadius;

            Assert.That(paths, Is.Not.Empty);
            for (int index = 0; index < paths.Count; index++)
            {
                AlpineVillagePathDescriptor path = paths[index];
                Assert.That(path.StableId, Is.Not.Empty);
                Assert.That(path.LengthXZ, Is.GreaterThan(0.25f));

                // A full-sized hero walking the route stays on the ground the
                // route paints. The mask no longer depends on this - the
                // whole bowl is walkable - but a track whose corridor is
                // wider than its own compacted ribbon plus the bare-soil
                // skirt is a route that does not look like one under his
                // feet: a household path's corridor (1.10 m) minus the hero
                // (0.35 m) puts his centre 0.75 m out, inside the
                // 0.62 + 0.15 m of bare soil and outside the 0.62 m ribbon.
                Assert.That(
                    path.WalkableHalfWidth - radius,
                    Is.LessThanOrEqualTo(
                        path.SurfaceHalfWidth +
                        AlpineVillagePathPlanner.BareSkirtHalfWidth),
                    path.StableId);

                int samples = Mathf.Max(2, Mathf.CeilToInt(path.LengthXZ));
                for (int step = 0; step <= samples; step++)
                {
                    Vector3 point = Vector3.Lerp(
                        path.Start,
                        path.End,
                        step / (float)samples);
                    Assert.That(
                        walkable.Contains(point, radius),
                        Is.True,
                        $"'{path.StableId}' leaves the walkable ground at " +
                        $"{step}/{samples}.");
                }
            }

            for (int plotIndex = 0;
                 plotIndex < plan.Plots.Count;
                 plotIndex++)
            {
                AlpineVillagePlotDescriptor plot = plan.Plots[plotIndex];
                bool reachesThreshold = false;
                for (int pathIndex = 0;
                     pathIndex < paths.Count;
                     pathIndex++)
                {
                    if (Vector3.Distance(
                            paths[pathIndex].End,
                            plot.DoorDockPosition) <= 0.01f)
                    {
                        reachesThreshold = true;
                        break;
                    }
                }

                Assert.That(
                    reachesThreshold,
                    Is.True,
                    $"'{plot.StableId}' has no visible path to its dock.");
            }
        }

        [Test]
        [Category("AlpineVillage")]
        public void PathEnvelopes_ClearRotatedPlotsAcrossRegressionSeeds()
        {
            foreach (int seed in new[]
                     {
                         GameSessionState.DefaultCitySeed,
                         -99992,
                         -99895,
                         -96746,
                         -87107,
                         -58640,
                         -29563,
                         3677,
                         57657,
                         89380
                     })
            {
                AlpineVillagePlan plan = AlpineVillagePlanner.Create(seed);
                IReadOnlyList<AlpineVillagePathDescriptor> paths =
                    AlpineVillagePathPlanner.Create(plan);
                for (int pathIndex = 0;
                     pathIndex < paths.Count;
                     pathIndex++)
                {
                    AlpineVillagePathDescriptor path = paths[pathIndex];
                    float envelope = Mathf.Max(
                        path.SurfaceHalfWidth,
                        path.WalkableHalfWidth);
                    for (int plotIndex = 0;
                         plotIndex < plan.Plots.Count;
                         plotIndex++)
                    {
                        AlpineVillagePlotDescriptor plot =
                            plan.Plots[plotIndex];
                        Assert.That(
                            AlpineVillagePathValidator
                                .MeasureFootprintClearance(path, plot),
                            Is.GreaterThanOrEqualTo(envelope - 0.001f),
                            $"seed {seed}: '{path.StableId}' enters " +
                            $"'{plot.StableId}'");
                    }
                }
            }
        }

        [Test]
        [Category("AlpineVillage")]
        public void Houses_FormAuthoredClustersWithoutParallelNeighbours()
        {
            AlpineVillagePlan plan = AlpineVillagePlanner.Create(
                GameSessionState.DefaultCitySeed);
            var houses = new List<AlpineVillagePlotDescriptor>();
            for (int index = 0; index < plan.Plots.Count; index++)
            {
                if (plan.Plots[index].Kind == AlpineVillagePlotKind.House)
                {
                    houses.Add(plan.Plots[index]);
                }
            }

            houses.Sort((left, right) =>
                left.LaneDistance.CompareTo(right.LaneDistance));
            Assert.That(houses, Has.Count.EqualTo(AlpineVillagePlanner.HouseCount));

            bool hasSameSideCluster = false;
            bool hasDeliberatePause = false;
            for (int index = 0; index < houses.Count - 1; index++)
            {
                AlpineVillagePlotDescriptor first = houses[index];
                AlpineVillagePlotDescriptor second = houses[index + 1];
                hasSameSideCluster |= first.Side == second.Side;
                hasDeliberatePause |=
                    second.LaneDistance - first.LaneDistance >= 8f;

                Assert.That(
                    Vector3.Angle(first.Facing, second.Facing),
                    Is.GreaterThan(4f),
                    $"'{first.StableId}' and '{second.StableId}' are parallel.");
            }

            Assert.That(
                hasSameSideCluster,
                Is.True,
                "The frontage still alternates left/right mechanically.");
            Assert.That(
                hasDeliberatePause,
                Is.True,
                "The street has no compositional breathing space.");
        }
    }
}
