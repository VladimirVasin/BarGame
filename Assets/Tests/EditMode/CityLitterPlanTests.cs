using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>The city-wide litter contract: where it may lie, how far apart, and never the same thing twice nearby.</summary>
    public sealed class CityLitterPlanTests
    {
        private CityLayout layout;
        private CityNightFixturePlan night;
        private RoadFencePlan fence;
        private CityDecorationPlan decoration;
        private CitySeacoastPlan seacoast;
        private CityArchShelterPlan archShelter;
        private CityStreetSurfacePlan streets;
        private CityLitterPlan plan;

        [OneTimeSetUp]
        public void PlanDefaultCity()
        {
            layout = CityLayoutGenerator.Generate(CityBlueprintCatalog.Default, CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            night = CityNightFixturePlanner.CreatePlan(layout);
            fence = RoadFencePlanner.CreatePlan(layout);
            decoration = CityDecorationPlanner.CreatePlan(layout, fence, night);
            seacoast = CitySeacoastPlanner.Create(layout);
            archShelter = CityArchShelterPlanner.Create(layout);
            streets = CityStreetSurfacePlanner.Create(layout);
            plan = CreatePlan();
        }

        private CityLitterPlan CreatePlan() =>
            CityLitterPlanner.Create(layout, night, fence, decoration, seacoast, archShelter, CityLitterCatalog.Load());

        [Test]
        public void DefaultCity_IsBitIdentical()
        {
            CityLitterPlan again = CreatePlan();
            Assert.That(again.Parts.Count, Is.EqualTo(plan.Parts.Count));
            for (int index = 0; index < plan.Parts.Count; index++)
            {
                CityLitterPart expected = plan.Parts[index], actual = again.Parts[index];
                Assert.That(actual.Id, Is.EqualTo(expected.Id));
                Assert.That(actual.Item.Name, Is.EqualTo(expected.Item.Name), expected.Id);
                Assert.That(actual.Zone, Is.EqualTo(expected.Zone), expected.Id);
                Assert.That(actual.Surface, Is.EqualTo(expected.Surface), expected.Id);
                Assert.That(actual.Position, Is.EqualTo(expected.Position), expected.Id);
                Assert.That(actual.Rotation, Is.EqualTo(expected.Rotation), expected.Id);
                Assert.That(actual.Scale, Is.EqualTo(expected.Scale), expected.Id);
                Assert.That(actual.Footprint, Is.EqualTo(expected.Footprint), expected.Id);
            }
        }

        [Test]
        public void DefaultCity_PlansOnAPoolThreadAsOnTheMainThread()
        {
            // The prime runs this planner off the main thread with the catalog
            // already warmed; nothing in it may touch the engine.
            CityLitterPlan pooled = System.Threading.Tasks.Task.Run(CreatePlan).GetAwaiter().GetResult();
            Assert.That(pooled.Parts.Count, Is.EqualTo(plan.Parts.Count));
            for (int index = 0; index < plan.Parts.Count; index++)
            {
                Assert.That(pooled.Parts[index].Id, Is.EqualTo(plan.Parts[index].Id));
                Assert.That(pooled.Parts[index].Position, Is.EqualTo(plan.Parts[index].Position), plan.Parts[index].Id);
            }
        }

        [Test]
        public void DefaultCity_NeverRepeatsAVariantWithinItsRadius()
        {
            var bicyclesByDistrict = new Dictionary<CityDistrictKind, int>();
            int bicycles = 0;
            for (int first = 0; first < plan.Parts.Count; first++)
            {
                CityLitterPart a = plan.Parts[first];
                if (a.Item.Category == CityLitterPlan.BicycleCategory)
                {
                    bicycles++;
                    bicyclesByDistrict.TryGetValue(a.District, out int count);
                    bicyclesByDistrict[a.District] = count + 1;
                }
                float radius = CityLitterPlan.SameVariantRadius(a.Item);
                for (int second = first + 1; second < plan.Parts.Count; second++)
                {
                    CityLitterPart b = plan.Parts[second];
                    if (b.Item.Name != a.Item.Name) continue;
                    float distance = Vector2.Distance(new Vector2(a.Position.x, a.Position.z), new Vector2(b.Position.x, b.Position.z));
                    Assert.That(distance, Is.GreaterThanOrEqualTo(radius - .001f),
                        "The same authored thing must never lie twice within sight: " + a.Id + " / " + b.Id);
                }
            }
            Assert.That(bicycles, Is.LessThanOrEqualTo(CityLitterPlan.MaximumBicycleCount));
            foreach (KeyValuePair<CityDistrictKind, int> entry in bicyclesByDistrict)
                Assert.That(entry.Value, Is.LessThanOrEqualTo(CityLitterPlan.MaximumBicyclesPerDistrict), entry.Key.ToString());
        }

        [Test]
        public void DefaultCity_KeepsFootprintsOffRoadsCrossingsBuildingsWaterAndClosedGrounds()
        {
            IReadOnlyList<Rect> roads = layout.CreateRoadRects();
            var buildings = layout.BuildingLots.Where(lot => lot.HasBuilding).Select(lot => Rect.MinMaxRect(
                lot.Center.x - lot.Size.x * .5f, lot.Center.z - lot.Size.y * .5f,
                lot.Center.x + lot.Size.x * .5f, lot.Center.z + lot.Size.y * .5f)).ToArray();
            var closed = layout.Surfaces.Where(surface =>
                surface.Kind == CitySurfaceKind.CemeteryGround || surface.Kind == CitySurfaceKind.ChurchGround ||
                surface.IsWater || surface.Feature == CityAreaFeatureKind.Yard ||
                surface.Feature == CityAreaFeatureKind.Cemetery || surface.Feature == CityAreaFeatureKind.Church)
                .Select(surface => surface.WorldBounds).ToArray();
            var scenes = new List<Rect>();
            foreach (CityDistrictPointOfInterestDescriptor point in layout.DistrictPointsOfInterest) scenes.Add(point.PublicBounds);
            HomeYardSitePlan? homeYard = HomeYardSitePlanner.Create(layout);
            if (homeYard.HasValue) scenes.Add(homeYard.Value.GroundBounds);
            if (archShelter.IsEnabled)
            {
                scenes.Add(archShelter.Placement.PassageFootprint);
                scenes.Add(archShelter.Placement.ShelteredFootprint);
                scenes.Add(archShelter.Placement.TableauFootprint);
            }
            if (seacoast?.Port != null) scenes.Add(seacoast.Port.LandBounds);
            foreach (CityLitterPart part in plan.Parts)
            {
                Rect footprint = part.Footprint;
                if (part.Zone != CityLitterZone.Sidewalk)
                    Assert.That(roads.Any(road => road.Overlaps(footprint)), Is.False, "Never on the carriageway: " + part.Id);
                Assert.That(streets.CrosswalkWalkableRectangles.Any(crossing => crossing.Overlaps(footprint)), Is.False,
                    "Never on a crossing: " + part.Id);
                Assert.That(buildings.Any(building => building.Overlaps(footprint)), Is.False, "Never inside a wall: " + part.Id);
                Assert.That(closed.Any(ground => ground.Overlaps(footprint)), Is.False,
                    "The cemetery, the church, water and every fringe yard stay as they are: " + part.Id);
                Assert.That(scenes.Any(scene => scene.Overlaps(footprint)), Is.False, "Authored scenes stay untouched: " + part.Id);
                Assert.That(layout.IsWater(part.Position), Is.False, part.Id);
            }
        }

        [Test]
        public void DefaultCity_KeepsSolidsOffWalkLanes()
        {
            var landing = new CityMapCityTeleportGround(layout);
            var lanes = new List<Rect>(streets.SidewalkWalkableRectangles);
            lanes.AddRange(streets.CrosswalkWalkableRectangles);
            lanes.AddRange(layout.OpenAreaAccesses.Select(access => access.ApproachBounds));
            foreach (CityDistrictPointOfInterestDescriptor point in layout.DistrictPointsOfInterest)
                lanes.AddRange(point.Accesses.Select(access => access.ApproachBounds));
            foreach (CityLitterPart part in plan.Parts)
            {
                if (!part.Item.Solid) continue;
                Assert.That(part.Zone, Is.Not.EqualTo(CityLitterZone.Sidewalk), "A solid never lies on a pavement: " + part.Id);
                Rect around = CityLitterGeometry.Expand(part.Footprint, .8f);
                Assert.That(lanes.Any(lane => lane.Overlaps(around)), Is.False,
                    "A crate or a wrecked bicycle leaves the walk lane beside it free: " + part.Id);
                float radius = Mathf.Sqrt(part.Footprint.width * part.Footprint.width + part.Footprint.height * part.Footprint.height) * .5f;
                Assert.That(CityDecorationValidator.IsProtectedGroundAnchor(part.Position, radius + .8f, fence, night), Is.False,
                    "Doors, gates, lamps and signals keep their clearance: " + part.Id);
                Assert.That(landing.TryResolveStandingPosition(new Vector2(part.Position.x, part.Position.z), out _), Is.False,
                    "Map arrivals must exclude the same large objects: " + part.Id);
            }
        }

        [Test]
        public void DefaultCity_HoldsDensityBounds()
        {
            TestContext.WriteLine("parts=" + plan.Parts.Count + " solids=" + plan.SolidCount + " triangles=" + plan.TriangleCount +
                " sidewalk=" + plan.GetCount(CityLitterZone.Sidewalk) + " lot=" + plan.GetCount(CityLitterZone.LotGround) +
                " park=" + plan.GetCount(CityLitterZone.Park) + " beach=" + plan.GetCount(CityLitterZone.Beach) +
                " old=" + plan.GetCount(CityDistrictKind.OldTown) + " residential=" + plan.GetCount(CityDistrictKind.Residential) +
                " industrial=" + plan.GetCount(CityDistrictKind.Industrial) + " nightlife=" + plan.GetCount(CityDistrictKind.Nightlife) +
                " variants=" + plan.Parts.Select(part => part.Item.Name).Distinct().Count());
            Assert.That(plan.Parts.Count, Is.InRange(380, CityLitterPlan.MaximumPartCount),
                "Sparse across the whole city: clearly thinner than the eastern strip, still met on an ordinary walk.");
            Assert.That(plan.GetCount(CityLitterZone.Sidewalk), Is.InRange(150, 400));
            Assert.That(plan.GetCount(CityLitterZone.LotGround), Is.GreaterThan(60));
            Assert.That(plan.GetCount(CityLitterZone.Park), Is.InRange(2, 6), "The park gets a handful, never a scatter.");
            Assert.That(plan.GetCount(CityLitterZone.Beach), Is.InRange(12, 60));
            foreach (CityDistrictKind district in new[] { CityDistrictKind.OldTown, CityDistrictKind.Residential,
                         CityDistrictKind.Industrial, CityDistrictKind.Nightlife })
                Assert.That(plan.GetCount(district), Is.GreaterThan(0), district.ToString());
            Assert.That(plan.GetCount(CityDistrictKind.Nightlife), Is.GreaterThan(plan.GetCount(CityDistrictKind.OldTown)),
                "The night district drops more than the old town.");
            Assert.That(plan.SolidCount, Is.InRange(1, CityLitterPlan.MaximumSolidCount));
            Assert.That(plan.Parts.Select(part => part.Item.Name).Distinct().Count(), Is.GreaterThanOrEqualTo(30),
                "Nearly every authored variant should be met somewhere in the city.");
            foreach (CityLitterPart part in plan.Parts)
            {
                if (part.District == CityDistrictKind.Industrial)
                    Assert.That(part.Item.Category, Is.Not.EqualTo("bag").And.Not.EqualTo("paper").And.Not.EqualTo("glass"),
                        "Industrial ground carries what serves the process, not household waste: " + part.Id);
                if (part.Zone == CityLitterZone.Park)
                    Assert.That(part.Item.Solid, Is.False, part.Id);
            }
        }

        [Test]
        public void DefaultCity_KeepsClearanceAndGroundContact()
        {
            float minimumGap = float.PositiveInfinity;
            string nearest = null;
            for (int first = 0; first < plan.Parts.Count; first++)
            for (int second = first + 1; second < plan.Parts.Count; second++)
            {
                float gap = CityLitterGeometry.Gap(plan.Parts[first].Footprint, plan.Parts[second].Footprint);
                if (gap >= minimumGap) continue;
                minimumGap = gap;
                nearest = plan.Parts[first].Id + " / " + plan.Parts[second].Id;
            }
            Assert.That(minimumGap, Is.GreaterThanOrEqualTo(CityLitterPlan.MinimumItemClearance - .001f),
                "Litter is met one thing at a time, never as a little pile: " + nearest);
            foreach (CityLitterPart part in plan.Parts)
            {
                var point = new Vector2(part.Position.x, part.Position.z);
                if (part.Zone == CityLitterZone.Sidewalk)
                {
                    RuntimeOrientedBox box = streets.SidewalkGeometry[part.Surface];
                    Assert.That(box.TrySampleTop(part.Position, out float top), Is.True, part.Id);
                    Assert.That(part.Position.y, Is.EqualTo(top).Within(.001f), part.Id);
                    Assert.That(box.Size.y, Is.EqualTo(CityStreetSurfacePlanner.SidewalkTop - CityStreetSurfacePlanner.RoadTop).Within(.001f),
                        "A pavement strip, never the port's buried kerb skirt: " + part.Id);
                    continue;
                }
                CitySurfaceDescriptor surface = layout.Surfaces[part.Surface];
                Assert.That(CityTerrainSurfacePlan.TrySampleGroundTop(layout, point, out float ground, out CitySurfaceDescriptor found), Is.True, part.Id);
                Assert.That(found, Is.EqualTo(surface), part.Id);
                Assert.That(part.Position.y, Is.EqualTo(ground).Within(.001f), part.Id);
                switch (part.Zone)
                {
                    case CityLitterZone.LotGround:
                        Assert.That(surface.Kind, Is.EqualTo(CitySurfaceKind.BuildableGround), part.Id);
                        break;
                    case CityLitterZone.Park:
                        Assert.That(surface.Kind, Is.EqualTo(CitySurfaceKind.ParkGround), part.Id);
                        break;
                    case CityLitterZone.Beach:
                        Assert.That(surface.Kind, Is.EqualTo(CitySurfaceKind.Beach), part.Id);
                        Assert.That(CityBeachSandPlan.SampleLooseDepth(layout.ElevationPlan, surface, point), Is.EqualTo(0f),
                            "Beach litter lies on the firm strand the collider also draws: " + part.Id);
                        break;
                }
            }
        }

        [Test]
        public void CityWorldPlans_MemoisesLitterWithItsDecoration()
        {
            CityWorldPlans plans = CityWorldPlans.GetOrCreate(layout);
            CityLitterPlan first = plans.GetLitter(night);
            Assert.That(plans.GetLitter(night), Is.SameAs(first));
            Assert.That(first.Parts.Count, Is.EqualTo(plan.Parts.Count));
            for (int index = 0; index < plan.Parts.Count; index++)
                Assert.That(first.Parts[index].Id, Is.EqualTo(plan.Parts[index].Id));
        }
    }
}
