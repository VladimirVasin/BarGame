using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The summit's one opening. Everything here is about the same trade:
    /// the cut has to take away enough ground for a view and none of the
    /// ground anything else stands on.
    /// </summary>
    public sealed class MountainRoadBrinkTests
    {
        private const float EyeHeight = 1.62f;

        [Test]
        [Category("MountainRoad")]
        public void BrinkFall_OpensTheViewWithoutTouchingTheDrivableSurface()
        {
            MountainRoadPlan plan = MountainRoadPlanner.Create(
                GameSessionState.DefaultCitySeed);
            MountainRoadPlateauDescriptor plateau = plan.Plateau;
            MountainRoadBrinkDescriptor brink = plateau.Brink;
            Assert.That(brink, Is.Not.Null);

            // 1. The pad is untouched, to the bit. The car drives on this.
            float padHeight = plateau.Center.y -
                              MountainRoadTerrainSampler.RoadBedClearance;
            Vector3[] interior =
            {
                plateau.Center,
                plan.Terminal.VehicleApron.Center,
                plan.Terminal.VehicleApron.EntryCenter,
                plan.Terminal.Cafe.Center,
                plan.Terminal.Cableway.StationArea.Center
            };
            for (int index = 0; index < interior.Length; index++)
            {
                Assert.That(
                    MountainRoadTerrainSampler.SampleHeight(
                        plan.Route,
                        plateau,
                        new Vector2(interior[index].x, interior[index].z)),
                    Is.EqualTo(padHeight),
                    "The cut reached inside the plateau polygon.");
            }

            MountainRoadRouteSample entry = plan.Route.Sample(
                plateau.EntryDistance);
            Assert.That(
                entry.Position.y,
                Is.EqualTo(plateau.Center.y).Within(0.001f),
                "The road/plateau seam moved.");

            // 2. There is a real drop, and it happens in a few metres.
            MountainRoadViewCorridor corridor = brink.Corridor;
            float atRim = SampleAlongAxis(plan, corridor, 0.5f);
            float atBlendEnd = SampleAlongAxis(
                plan,
                corridor,
                corridor.InnerRadius + brink.EdgeBlendDistance + 0.5f);
            Assert.That(
                atRim - atBlendEnd,
                Is.GreaterThan(20f),
                "The brink is a slope, not a drop.");
            Assert.That(
                atRim,
                Is.GreaterThan(plateau.Center.y - 3f),
                "The ground gives way before the parapet can explain it.");

            // 3. Swept across the composed wedge, nothing rises into the
            //    sightline. -12 degrees is a window; anything shallower is
            //    a hillside you happen to be standing on top of.
            Vector3 eye = brink.RimCenter + Vector3.up * EyeHeight;
            float worstElevation = float.NegativeInfinity;
            float worstBearing = 0f;
            for (float offset = -8f; offset <= 8f; offset += 1f)
            {
                Vector3 direction = Rotate(
                    corridor.Axis,
                    offset);
                for (float distance = 6f; distance <= 120f; distance += 2f)
                {
                    Vector3 point = eye + direction * distance;
                    float height = MountainRoadTerrainSampler.SampleHeight(
                        plan.Route,
                        plateau,
                        new Vector2(point.x, point.z));
                    float elevation = Mathf.Atan2(
                        height - eye.y,
                        distance) * Mathf.Rad2Deg;
                    if (elevation > worstElevation)
                    {
                        worstElevation = elevation;
                        worstBearing = offset;
                    }
                }
            }

            Assert.That(
                worstElevation,
                Is.LessThan(-12f),
                $"Ground rises to {worstElevation:0.0} degrees at " +
                $"{worstBearing:0.0} degrees off the corridor axis, so the " +
                "view opens onto a slope rather than onto air.");

            // 4. Nothing that has to stay grounded stands in the cut.
            //    The cableway rule is a HORIZONTAL distance on purpose:
            //    dropping the ground under a support only makes its own
            //    clearance test greener while it ends up on stilts.
            AssertClear(
                corridor,
                RoutePoints(plan),
                MountainRoadPlanner.BrinkRouteClearance,
                "route");
            AssertClear(
                corridor,
                CablewayGround(plan),
                MountainRoadPlanner.BrinkCablewayClearance,
                "cableway ground");
            AssertClear(
                corridor,
                RidgeFootprints(plan),
                MountainRoadPlanner.BrinkRidgeClearance,
                "ridge footprint");

            // 5. And the ridges the cut deliberately does NOT move are
            //    still enough to carry the amphitheatre.
            int mid = 0;
            int snowy = 0;
            for (int index = 0; index < plan.Ridges.Count; index++)
            {
                if (plan.Ridges[index].Layer ==
                    MountainRoadRidgeLayer.Mid)
                {
                    mid++;
                }
                else
                {
                    snowy++;
                }
            }

            Assert.That(mid, Is.GreaterThanOrEqualTo(6));
            Assert.That(snowy, Is.GreaterThanOrEqualTo(10));
        }

        [Test]
        [Category("MountainRoad")]
        public void BrinkCorridor_RejectsAnythingPlantedInIt()
        {
            MountainRoadPlan plan = MountainRoadPlanner.Create(
                GameSessionState.DefaultCitySeed);
            MountainRoadViewCorridor corridor = plan.Plateau.Brink.Corridor;

            Vector3 inside = corridor.Apex +
                             corridor.Axis * (corridor.InnerRadius + 30f);
            Assert.That(
                corridor.DepthInside(new Vector2(inside.x, inside.z)),
                Is.GreaterThan(0f));
            Assert.That(
                corridor.Weight(
                    new Vector2(inside.x, inside.z),
                    plan.Plateau.Brink.EdgeBlendDistance),
                Is.EqualTo(1f).Within(0.001f));

            Vector3 behind = corridor.Apex -
                             corridor.Axis * 20f;
            Assert.That(
                corridor.Weight(
                    new Vector2(behind.x, behind.z),
                    plan.Plateau.Brink.EdgeBlendDistance),
                Is.EqualTo(0f),
                "The cut reaches backwards into the plateau.");

            Vector3 beyond = corridor.Apex +
                             corridor.Axis * (corridor.OuterRadius + 5f);
            Assert.That(
                corridor.Weight(
                    new Vector2(beyond.x, beyond.z),
                    plan.Plateau.Brink.EdgeBlendDistance),
                Is.EqualTo(0f));
        }

        private static float SampleAlongAxis(
            MountainRoadPlan plan,
            MountainRoadViewCorridor corridor,
            float distance)
        {
            Vector3 point = corridor.Apex + corridor.Axis * distance;
            return MountainRoadTerrainSampler.SampleHeight(
                plan.Route,
                plan.Plateau,
                new Vector2(point.x, point.z));
        }

        private static void AssertClear(
            MountainRoadViewCorridor corridor,
            IReadOnlyList<Vector2> points,
            float margin,
            string label)
        {
            float worst = float.NegativeInfinity;
            for (int index = 0; index < points.Count; index++)
            {
                worst = Mathf.Max(
                    worst,
                    corridor.DepthInside(points[index]));
            }

            Assert.That(
                worst,
                Is.LessThanOrEqualTo(-margin),
                $"The nearest {label} clears the cut by {(-worst):0.00} m " +
                $"against the {margin:0.00} m it needs.");
        }

        private static Vector3 Rotate(Vector3 axis, float degrees)
        {
            return Quaternion.AngleAxis(degrees, Vector3.up) * axis;
        }

        private static List<Vector2> RoutePoints(MountainRoadPlan plan)
        {
            var points = new List<Vector2>(plan.Route.Samples.Count);
            for (int index = 0; index < plan.Route.Samples.Count; index++)
            {
                Vector3 position = plan.Route.Samples[index].Position;
                points.Add(new Vector2(position.x, position.z));
            }

            return points;
        }

        private static List<Vector2> CablewayGround(MountainRoadPlan plan)
        {
            IReadOnlyList<MountainCablewayNodeDescriptor> nodes =
                plan.Terminal.Cableway.Nodes;
            var points = new List<Vector2>(nodes.Count);
            for (int index = 0; index < nodes.Count; index++)
            {
                Vector3 ground = nodes[index].GroundPosition;
                points.Add(new Vector2(ground.x, ground.z));
            }

            return points;
        }

        private static List<Vector2> RidgeFootprints(MountainRoadPlan plan)
        {
            var points = new List<Vector2>(plan.Ridges.Count * 4);
            for (int index = 0; index < plan.Ridges.Count; index++)
            {
                MountainRoadRidgeDescriptor ridge = plan.Ridges[index];
                float radians = ridge.YawDegrees * Mathf.Deg2Rad;
                var right = new Vector2(
                    Mathf.Cos(radians),
                    -Mathf.Sin(radians));
                var forward = new Vector2(
                    Mathf.Sin(radians),
                    Mathf.Cos(radians));
                var center = new Vector2(ridge.Center.x, ridge.Center.z);
                for (int cornerX = -1; cornerX <= 1; cornerX += 2)
                {
                    for (int cornerZ = -1; cornerZ <= 1; cornerZ += 2)
                    {
                        points.Add(
                            center +
                            right * (cornerX * ridge.Size.x * 0.5f) +
                            forward * (cornerZ * ridge.Size.z * 0.5f));
                    }
                }
            }

            return points;
        }
    }
}
