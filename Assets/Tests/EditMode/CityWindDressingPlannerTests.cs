using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityWindDressingPlannerTests
    {
        private static CityLayout CreateDefaultLayout()
        {
            return CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
        }

        private static CityWindDressingPlan CreatePlan(
            CityLayout layout)
        {
            CityMountainBoundaryPlan mountainPlan =
                CityMountainBoundaryPlanner.Create(layout);
            return CityWindDressingPlanner.Create(
                layout,
                CityDecorationPlanner.CreatePlan(
                    layout,
                    RoadFencePlanner.CreatePlan(layout),
                    CityNightFixturePlanner.CreatePlan(layout)),
                CitySeacoastPlanner.Create(layout),
                CityCemeteryPlanner.Create(layout),
                CityFringeYardPlanner.Create(layout, mountainPlan));
        }

        [Test]
        public void DefaultCity_PlansDeterministicWindDressing()
        {
            CityLayout layout = CreateDefaultLayout();
            CityWindDressingPlan first = CreatePlan(layout);
            CityWindDressingPlan second = CreatePlan(layout);

            Assert.That(
                first.ClothCount,
                Is.GreaterThanOrEqualTo(40),
                "The default city must hang enough cloth to be met " +
                "on an ordinary walk.");
            Assert.That(
                first.ClothCount,
                Is.LessThanOrEqualTo(
                    CityWindDressingPlan.MaximumClothCount));
            Assert.That(
                second.ClothCount,
                Is.EqualTo(first.ClothCount));
            for (int index = 0; index < first.ClothCount; index++)
            {
                Assert.That(
                    second.Cloths[index],
                    Is.EqualTo(first.Cloths[index]));
            }

            Assert.That(
                second.Supports.Count,
                Is.EqualTo(first.Supports.Count));
            for (int index = 0;
                 index < first.Supports.Count;
                 index++)
            {
                Assert.That(
                    second.Supports[index],
                    Is.EqualTo(first.Supports[index]));
            }
        }

        [Test]
        public void DefaultCity_GroundsStreetMiscAndKeepsCourtyardLinesClear()
        {
            CityLayout layout = CreateDefaultLayout();
            RoadFencePlan fencePlan = RoadFencePlanner.CreatePlan(layout);
            CityNightFixturePlan nightPlan =
                CityNightFixturePlanner.CreatePlan(layout);
            CityDecorationPlan decorationPlan =
                CityDecorationPlanner.CreatePlan(
                    layout,
                    fencePlan,
                    nightPlan);
            CityMountainBoundaryPlan mountainPlan =
                CityMountainBoundaryPlanner.Create(layout);
            CityWindDressingPlan windPlan =
                CityWindDressingPlanner.Create(
                    layout,
                    decorationPlan,
                    CitySeacoastPlanner.Create(layout),
                    CityCemeteryPlanner.Create(layout),
                    CityFringeYardPlanner.Create(
                        layout,
                        mountainPlan));

            int groundedDescriptorCount = 0;
            var decorationProxies = new List<Bounds>();
            var proxyBuffer = new List<Bounds>(
                CityStaticCollisionBuilder.MaximumDecorationProxyCount);
            for (int index = 0;
                 index < decorationPlan.Descriptors.Count;
                 index++)
            {
                CityDecorationDescriptor descriptor =
                    decorationPlan.Descriptors[index];
                if (descriptor.AnchorKind ==
                        CityDecorationAnchorKind.BuildingFrontage ||
                    descriptor.AnchorKind ==
                        CityDecorationAnchorKind.Roadside)
                {
                    Assert.That(
                        CityTerrainSurfacePlan.TrySampleGroundTop(
                            layout,
                            new Vector2(
                                descriptor.Position.x,
                                descriptor.Position.z),
                            out float groundTop,
                            out _),
                        Is.True,
                        $"Street misc '{descriptor.StableId}' has no " +
                        "sampled ground.");
                    Assert.That(
                        descriptor.Position.y,
                        Is.EqualTo(groundTop).Within(0.001f),
                        $"Street misc '{descriptor.StableId}' floats " +
                        "above or sinks below its sampled ground.");
                    groundedDescriptorCount++;
                }

                if (descriptor.CollisionTier ==
                    CityDecorationCollisionTier.None)
                {
                    continue;
                }

                proxyBuffer.Clear();
                CityStaticCollisionBuilder.AddDecorationProxyBounds(
                    layout,
                    descriptor,
                    proxyBuffer);
                decorationProxies.AddRange(proxyBuffer);
            }

            Assert.That(
                groundedDescriptorCount,
                Is.GreaterThan(0),
                "The default city planned no grounded street misc.");
            Assert.That(
                decorationProxies,
                Is.Not.Empty,
                "The default city planned no physical decoration proxies.");

            int linePoleCount = 0;
            int lineSupportCount = 0;
            for (int index = 0;
                 index < windPlan.Supports.Count;
                 index++)
            {
                CityWindDressingSupportDescriptor support =
                    windPlan.Supports[index];
                if (support.Zone != CityWindDressingZone.Residential)
                {
                    continue;
                }

                lineSupportCount++;
                Bounds supportBounds = AxisAlignedBounds(support.Box);
                for (int proxyIndex = 0;
                     proxyIndex < decorationProxies.Count;
                     proxyIndex++)
                {
                    Assert.That(
                        OverlapsStrict(
                            supportBounds,
                            decorationProxies[proxyIndex]),
                        Is.False,
                        $"Courtyard-line support " +
                        $"'{support.StableId}' intersects physical " +
                        $"decoration proxy {proxyIndex}.");
                }

                if (support.Kind !=
                    CityWindDressingSupportKind.LinePole)
                {
                    continue;
                }

                Assert.That(
                    CityTerrainSurfacePlan.TrySampleGroundTop(
                        layout,
                        new Vector2(
                            support.Box.Center.x,
                            support.Box.Center.z),
                        out float poleGroundTop,
                        out _),
                    Is.True,
                    $"Line pole '{support.StableId}' has no sampled " +
                    "ground.");
                Assert.That(
                    supportBounds.min.y,
                    Is.EqualTo(poleGroundTop).Within(0.001f),
                    $"Line pole '{support.StableId}' is not planted " +
                    "on sampled ground.");
                linePoleCount++;
            }

            Assert.That(
                lineSupportCount,
                Is.GreaterThan(0),
                "The default city planned no courtyard-line supports.");
            Assert.That(
                linePoleCount,
                Is.GreaterThan(0),
                "The default city planned no courtyard-line poles.");
        }

        [Test]
        public void DefaultCity_RespectsZoneBudgetsAndBodyRegistry()
        {
            CityWindDressingPlan plan =
                CreatePlan(CreateDefaultLayout());

            foreach (CityWindDressingZone zone in
                     (CityWindDressingZone[])System.Enum.GetValues(
                         typeof(CityWindDressingZone)))
            {
                Assert.That(
                    plan.GetClothCount(zone),
                    Is.LessThanOrEqualTo(
                        CityWindDressingRules.MaximumClothCount(zone)),
                    $"Zone '{zone}' exceeds its cloth budget.");
            }

            for (int index = 0; index < plan.ClothCount; index++)
            {
                CityWindDressingClothDescriptor cloth =
                    plan.Cloths[index];
                // Only body-height courtyard wash parts around the
                // hero; everything else hangs out of reach.
                if (cloth.RegisterBody)
                {
                    Assert.That(
                        cloth.Kind,
                        Is.EqualTo(
                            CityWindDressingKind.CourtyardLaundry));
                }
            }
        }

        [Test]
        public void DefaultCity_HangsStreetLevelClothInEveryUrbanDistrict()
        {
            CityLayout layout = CreateDefaultLayout();
            CityWindDressingPlan plan = CreatePlan(layout);

            var streetLevelCounts = new Dictionary<
                CityWindDressingZone, int>
            {
                { CityWindDressingZone.OldTown, 0 },
                { CityWindDressingZone.Residential, 0 },
                { CityWindDressingZone.Industrial, 0 },
                { CityWindDressingZone.Nightlife, 0 }
            };
            for (int index = 0; index < plan.ClothCount; index++)
            {
                CityWindDressingClothDescriptor cloth =
                    plan.Cloths[index];
                if (!streetLevelCounts.ContainsKey(cloth.Zone))
                {
                    continue;
                }

                if (!CityTerrainSurfacePlan.TrySampleGroundTop(
                        layout,
                        new Vector2(
                            cloth.Position.x,
                            cloth.Position.z),
                        out float ground,
                        out _))
                {
                    continue;
                }

                // Above eight metres a piece no longer reads from the
                // pavement — the regression this test exists for was
                // tarps planned 45 m up a landmark tower.
                if (cloth.Position.y - ground <= 8f)
                {
                    streetLevelCounts[cloth.Zone]++;
                }
            }

            Assert.That(
                streetLevelCounts[CityWindDressingZone.OldTown],
                Is.GreaterThanOrEqualTo(8));
            Assert.That(
                streetLevelCounts[CityWindDressingZone.Residential],
                Is.GreaterThanOrEqualTo(10));
            Assert.That(
                streetLevelCounts[CityWindDressingZone.Industrial],
                Is.GreaterThanOrEqualTo(6));
            Assert.That(
                streetLevelCounts[CityWindDressingZone.Nightlife],
                Is.GreaterThanOrEqualTo(6));
        }

        [Test]
        public void DefaultCity_KeepsCourtyardWashOffTheDryingYard()
        {
            CityLayout layout = CreateDefaultLayout();
            CityWindDressingPlan plan = CreatePlan(layout);

            Rect dryingYard = default;
            bool hasDryingYard = false;
            for (int index = 0;
                 index < layout.DistrictPointsOfInterest.Count;
                 index++)
            {
                CityDistrictPointOfInterestDescriptor point =
                    layout.DistrictPointsOfInterest[index];
                if (point.Kind ==
                    CityDistrictPointOfInterestKind
                        .ResidentialDryingYard)
                {
                    dryingYard = point.PublicBounds;
                    hasDryingYard = true;
                }
            }

            Assert.That(hasDryingYard, Is.True);
            for (int index = 0; index < plan.ClothCount; index++)
            {
                CityWindDressingClothDescriptor cloth =
                    plan.Cloths[index];
                if (cloth.Kind !=
                    CityWindDressingKind.CourtyardLaundry)
                {
                    continue;
                }

                Rect keepOut = new Rect(
                    dryingYard.x -
                    CityWindDressingPlanner.DryingYardClearance,
                    dryingYard.y -
                    CityWindDressingPlanner.DryingYardClearance,
                    dryingYard.width +
                    (CityWindDressingPlanner.DryingYardClearance * 2f),
                    dryingYard.height +
                    (CityWindDressingPlanner.DryingYardClearance * 2f));
                Assert.That(
                    keepOut.Contains(new Vector2(
                        cloth.Position.x,
                        cloth.Position.z)),
                    Is.False,
                    "Courtyard wash crowds the authored drying yard.");
            }
        }

        [Test]
        public void DefaultCity_HangsNothingInTheBarSideYard()
        {
            CityLayout layout = CreateDefaultLayout();
            CityWindDressingPlan plan = CreatePlan(layout);
            CityOpenAreaDecorationPlan openArea =
                CityOpenAreaDecorationPlanner.Create(layout);

            if (!openArea.HomeYardSite.HasValue)
            {
                Assert.Ignore(
                    "This seed carries no bar-side yard to protect.");
            }

            Rect yard = openArea.HomeYardSite.Value.GroundBounds;
            for (int index = 0; index < plan.ClothCount; index++)
            {
                Assert.That(
                    yard.Contains(new Vector2(
                        plan.Cloths[index].Position.x,
                        plan.Cloths[index].Position.z)),
                    Is.False,
                    "The bar-side yard is authored by subtraction " +
                    "and hangs no cloth.");
            }
        }

        [Test]
        public void DefaultCity_EveryClothHangsInsideItsZone()
        {
            CityLayout layout = CreateDefaultLayout();
            CityWindDressingPlan plan = CreatePlan(layout);
            CityMountainBoundaryPlan mountainPlan =
                CityMountainBoundaryPlanner.Create(layout);
            CityFringeYardPlan fringePlan =
                CityFringeYardPlanner.Create(layout, mountainPlan);
            CityCemeteryPlan cemeteryPlan =
                CityCemeteryPlanner.Create(layout);
            CitySeacoastPlan seacoastPlan =
                CitySeacoastPlanner.Create(layout);

            // Anchors sit inside their zones; small hang offsets may
            // lean past a district edge by less than half a cell.
            const float Tolerance = 4f;
            for (int index = 0; index < plan.ClothCount; index++)
            {
                CityWindDressingClothDescriptor cloth =
                    plan.Cloths[index];
                var point = new Vector2(
                    cloth.Position.x,
                    cloth.Position.z);
                Assert.That(
                    ResolveZoneRects(
                            layout,
                            fringePlan,
                            cemeteryPlan,
                            seacoastPlan,
                            cloth.Zone)
                        .Exists(rect =>
                            Expand(rect, Tolerance).Contains(point)),
                    Is.True,
                    $"Cloth '{cloth.StableId}' hangs outside its " +
                    $"'{cloth.Zone}' zone.");
            }
        }

        private static List<Rect> ResolveZoneRects(
            CityLayout layout,
            CityFringeYardPlan fringePlan,
            CityCemeteryPlan cemeteryPlan,
            CitySeacoastPlan seacoastPlan,
            CityWindDressingZone zone)
        {
            var rects = new List<Rect>();
            if (zone == CityWindDressingZone.FringeYards)
            {
                for (int index = 0;
                     index < fringePlan.Yards.Count;
                     index++)
                {
                    rects.Add(fringePlan.Yards[index].AreaBounds);
                }

                return rects;
            }

            // The cemetery and the seacoast are open-area features
            // whose dressed ground lives on their own plans, not on a
            // district descriptor.
            if (zone == CityWindDressingZone.Cemetery)
            {
                if (cemeteryPlan != null)
                {
                    rects.Add(cemeteryPlan.Grounds);
                }

                return rects;
            }

            if (zone == CityWindDressingZone.Seacoast)
            {
                if (seacoastPlan != null)
                {
                    rects.Add(seacoastPlan.Grounds);
                }

                return rects;
            }

            CityDistrictKind kind;
            switch (zone)
            {
                case CityWindDressingZone.OldTown:
                    kind = CityDistrictKind.OldTown;
                    break;
                case CityWindDressingZone.Residential:
                    kind = CityDistrictKind.Residential;
                    break;
                case CityWindDressingZone.Industrial:
                    kind = CityDistrictKind.Industrial;
                    break;
                case CityWindDressingZone.Nightlife:
                    kind = CityDistrictKind.Nightlife;
                    break;
                default:
                    kind = CityDistrictKind.CentralPark;
                    break;
            }

            if (layout.TryGetDistrict(
                    kind,
                    out CityDistrictDescriptor district))
            {
                rects.Add(Rect.MinMaxRect(
                    district.WorldBounds.min.x,
                    district.WorldBounds.min.z,
                    district.WorldBounds.max.x,
                    district.WorldBounds.max.z));
            }

            return rects;
        }

        private static Rect Expand(Rect source, float amount)
        {
            return new Rect(
                source.x - amount,
                source.y - amount,
                source.width + (amount * 2f),
                source.height + (amount * 2f));
        }

        private static Bounds AxisAlignedBounds(RuntimeOrientedBox box)
        {
            Vector3 axisX = box.Rotation *
                new Vector3(box.Size.x * 0.5f, 0f, 0f);
            Vector3 axisY = box.Rotation *
                new Vector3(0f, box.Size.y * 0.5f, 0f);
            Vector3 axisZ = box.Rotation *
                new Vector3(0f, 0f, box.Size.z * 0.5f);
            var extents = new Vector3(
                Mathf.Abs(axisX.x) +
                Mathf.Abs(axisY.x) +
                Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) +
                Mathf.Abs(axisY.y) +
                Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) +
                Mathf.Abs(axisY.z) +
                Mathf.Abs(axisZ.z));
            return new Bounds(box.Center, extents * 2f);
        }

        private static bool OverlapsStrict(Bounds left, Bounds right)
        {
            const float Epsilon = 0.001f;
            return left.min.x < right.max.x - Epsilon &&
                   left.max.x > right.min.x + Epsilon &&
                   left.min.y < right.max.y - Epsilon &&
                   left.max.y > right.min.y + Epsilon &&
                   left.min.z < right.max.z - Epsilon &&
                   left.max.z > right.min.z + Epsilon;
        }
    }
}
