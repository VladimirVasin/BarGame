using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class CompactCityAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            System.Type.GetType("BarPromenade.Editor.CityBuildingAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", System.Type.EmptyTypes).Invoke(null, null);
            System.Type.GetType("BarPromenade.Editor.CityCoastalInfrastructureAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", System.Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("City block composition, lowered authored models and actual courtyard traversal.")]
        [PrebuildSetup(typeof(CompactCityAssetsSetup))]
        public IEnumerator CityFabric()
        {
            Debug.Log("CITY FABRIC: starting session and world construction.");
            GameSessionState.BeginNewGame();
            Assert.That(GameSessionState.TryStartGameTimeFromWake(), Is.True);
            GameSessionState.AdvanceGameTime((float)(360d / GameTimeState.GameMinutesPerRealSecond));
            CityLayout planned = CityLayoutCache.GetOrGenerate(
                CityBlueprintCatalog.Resolve(GameSessionState.CityBlueprintId),
                CityGenerationSettings.Default, GameSessionState.CitySeed);
            CityCanneryTruckRoute cargo = CityLayoutCache.GetOrCreateCanneryRoute(planned,
                CityCanneryPlan.Create(planned), CityPortAccessPlan.ForLayout(planned));
            Assert.That(cargo, Is.Not.Null);
            foreach (RoadEdge edge in cargo.StreetEdges)
                Assert.That(planned.RoadGeometry.Get(edge).IsStraight, Is.True);
            // Fail a pure-plan contract before loading the scene; the scene
            // consumes this same memoised plan rather than recomputing it.
            CityWorldPlans.GetOrCreate(planned);
            CityArchShelterPlacement retainedArch = CityArchShelterPlanner.Create(planned).Placement;
            Assert.That(retainedArch.CommonFacadeFootprint.width, Is.EqualTo(14.472f).Within(.005f));
            Assert.That(retainedArch.CommonFacadeFootprint.height, Is.EqualTo(11.602f).Within(.005f));
            Assert.That(retainedArch.TerraceRise, Is.EqualTo(1.562f).Within(.005f));
            Debug.Log("CITY FABRIC: cargo routing valid, constructing physical world.");
            CityGameRoot city = null;
            var issues = new List<string>();
            yield return Capture(SceneIds.City, () =>
            {
                city = Object.FindAnyObjectByType<CityGameRoot>();
                return city != null && city.IsInitialized && city.World != null && city.BusPlan != null
                    ? city : null;
            }, () => CityFabricShots(city, issues));
            Assert.That(issues, Is.Empty, string.Join("\n", issues));
            yield return Capture(SceneIds.HomeInterior, Root<HomeInteriorRoot>(), new[]
            {
                Shot.At("compact-city-from-home-balcony", new Vector3(6.2f, 1.72f, -1.45f),
                    new Vector3(19f, -.5f, -1.45f), 70f)
            });
        }

        private static Shot[] CityFabricShots(CityGameRoot city, List<string> issues)
        {
            Debug.Log("CITY FABRIC: world ready, measuring routes and rendered composition.");
            CityLayout layout = city.Layout;
            layout.ValidateOrThrow();
            CityBuildingAssetProvider provider = CityBuildingAssetProvider.LoadOrThrow();
            Assert.That(provider.Entries.Count, Is.EqualTo(20));
            Assert.That(layout.SpatialPlan.IsCompactCoastal, Is.True);
            Assert.That(layout.BuildingLots.Where(lot => lot.HasBuilding).Select(lot => lot.District).Distinct(),
                Is.EquivalentTo(new[] { CityDistrictKind.Residential, CityDistrictKind.Industrial }));
            // Historical declared dry-land area, before the compact rebuild.
            // Decorative sea and the river channel are excluded in both maps.
            // Whole cells include their streets; surface bounds trim those away.
            float landArea = layout.Blueprint.Cells.Where(cell => !cell.IsWater)
                .Sum(cell =>
                {
                    Rect area = layout.SpatialPlan.GetCellBounds(cell.Cell);
                    return area.width * area.height;
                });
            Assert.That(landArea / 193828f, Is.InRange(.69f, .72f));
            Assert.That(layout.DistrictPointsOfInterest.Select(point => point.Kind).Distinct().Count(), Is.EqualTo(4));
            Assert.That(layout.PrimaryLandmarkCells.Count, Is.EqualTo(4));
            Assert.That(CityFairPlanner.Create(layout).IsEnabled, Is.True);
            Assert.That(CityArchShelterPlanner.Create(layout).IsEnabled, Is.True);
            Assert.That(LastRouteCarPlan.Create(layout).IsPresent, Is.True);
            Assert.That(city.World.PortPlan?.Access, Is.Not.Null);
            Assert.That(city.World.ChurchPlan, Is.Not.Null);
            Assert.That(city.BusPlan.Stops.Select(stop => stop.Id).Distinct().Count(), Is.EqualTo(city.BusPlan.Stops.Count));
            CityCoastalInfrastructurePlan infrastructure = CityCoastalInfrastructurePlan.ForLayout(layout);
            Assert.That(infrastructure.Facilities.Count, Is.EqualTo(3));
            Assert.That(infrastructure.CableSpans.Count, Is.GreaterThan(10));
            foreach (CityElevationStairDescriptor stair in layout.ElevationPlan.SignatureStairs)
                Assert.That(layout.RoadGeometry.Get(stair.Edge).IsStraight, Is.True,
                    $"Signature stair {stair.Id} must preserve its straight street {stair.Edge}.");
            Assert.That(layout.CourtyardBlocks.Count, Is.GreaterThan(5));
            Assert.That(layout.CourtyardBlocks.Select(block => block.Primary.District).Distinct().Count(),
                Is.EqualTo(2), "Both compact districts must contain actual courtyard composition.");
            Assert.That(layout.CourtyardBlocks.Count(block => block.RearBuilding != null),
                Is.GreaterThan(3), "The widened plots must contain actual additional authored wings.");
            Assert.That(layout.BuildingLots.Count(lot => lot.IsBar), Is.EqualTo(1));
            Assert.That(layout.PlayerHome.Height, Is.EqualTo(8.8f).Within(.01f));
            Assert.That(layout.Supermarket.Height, Is.EqualTo(6.4f).Within(.01f));
            var ordinary = layout.BuildingLots.Where(lot => lot.IsOrdinaryBuilding).ToArray();
            var residential = ordinary.Where(lot => lot.District == CityDistrictKind.Residential).ToArray();
            Assert.That(residential.Length, Is.GreaterThan(30), "The hero's home is part of a populated residential fabric.");
            Assert.That(residential.All(lot => lot.Height >= 9f && lot.Height <= 16.5f), Is.True);
            Assert.That(ordinary.Where(lot => lot.District == CityDistrictKind.Industrial).Max(lot => lot.Height),
                Is.LessThan(residential.Min(lot => lot.Height)));
            Assert.That(CityLayoutCache.GetOrCreateCanneryRoute(layout, CityCanneryPlan.Create(layout),
                CityPortAccessPlan.ForLayout(layout)), Is.Not.Null);

            CityStreetSurfacePlan streets = CityStreetSurfacePlanner.Create(layout);
            RoadWalkableArea walkers = CityPedestrianPlanner.CreateWalkableArea(city.PedestrianPlan);
            Physics.SyncTransforms();
            foreach (CityCourtyardBlock block in layout.CourtyardBlocks)
                VerifyReplanningWalkingPath($"City fabric court {block.Cell}", block.Route,
                    city, streets, walkers, issues);
            foreach (CityCourtyardConnection connection in layout.CourtyardConnections)
                VerifyReplanningWalkingPath($"City fabric shared court {connection.FirstCell}->{connection.SecondCell}",
                    connection.Path, city, streets, walkers, issues);
            Debug.Log($"CITY FABRIC: physical route issues={issues.Count}. " +
                string.Join("\n", issues.Take(30)));

            var shots = new List<Shot>();
            Vector3 parkPlaza = layout.Park.Regions.Last().PlazaPosition;
            shots.Add(Shot.At("compact-park-and-houses", parkPlaza + new Vector3(7f, 2.2f, -4f),
                parkPlaza + new Vector3(-8f, 1.5f, 8f), 78f));
            shots.Add(Shot.At("compact-coastal-silhouette", city.World.PortPlan.World(new Vector3(10f, 8f, 6f)),
                city.World.PortPlan.World(new Vector3(0f, 4f, -35f)), 76f));
            CityFairPlan fair = CityFairPlanner.Create(layout);
            Vector3 fairCenter = new Vector3(fair.Bounds.center.x, (fair.SouthY + fair.NorthY) * .5f,
                fair.BoundaryZ);
            shots.Add(Shot.At("compact-retained-fair", new Vector3(fair.Bounds.xMax + 6f,
                fairCenter.y + 2f, fairCenter.z), fairCenter + Vector3.up, 80f));
            CityArchShelterPlan arch = CityArchShelterPlanner.Create(layout);
            shots.Add(Shot.At("compact-retained-arch", arch.Placement.StructurePosition +
                new Vector3(0f, 2f, -12f), arch.Placement.StructurePosition + Vector3.up * 2f, 76f));
            Transform archRoot = city.World.Root.transform.Find(CityArchShelterWorldBuilder.RootName);
            Assert.That(archRoot, Is.Not.Null);
            foreach (string part in new[] { "PlatformSupport_Masonry", "PlatformSlab_Street" })
            {
                Bounds rendered = archRoot.GetComponentsInChildren<Renderer>(true)
                    .Single(renderer => renderer.name == part).bounds;
                Rect expected = arch.Platform.Footprint;
                Assert.That(rendered.min.x, Is.EqualTo(expected.xMin).Within(.02f), part);
                Assert.That(rendered.max.x, Is.EqualTo(expected.xMax).Within(.02f), part);
                Assert.That(rendered.min.z, Is.EqualTo(expected.yMin).Within(.02f), part);
                Assert.That(rendered.max.z, Is.EqualTo(expected.yMax).Within(.02f), part);
            }
            foreach (CityDistrictKind district in new[] { CityDistrictKind.Residential, CityDistrictKind.Industrial })
            {
                BuildingLot lot = ordinary.Where(candidate => candidate.District == district && candidate.HasRoadFrontage)
                    .OrderByDescending(candidate => candidate.Height).First();
                Vector3 eye = lot.SidewalkArrivalPosition + lot.FacadeForward *
                    (layout.RoadWidth - CityStreetSurfacePlanner.SidewalkWidth);
                if (TryReplanningSurfaceTop(layout, streets, new Vector2(eye.x, eye.z), out float top)) eye.y = top;
                shots.Add(Shot.At($"fabric-{district}-street", eye + Vector3.up * EyeHeight,
                    lot.DoorPosition + Vector3.up * (lot.Height * .3f), 86f));
            }
            foreach (CityCourtyardBlock block in layout.CourtyardBlocks
                         .GroupBy(candidate => candidate.Kind).Select(group => group.First()))
            {
                Vector3 right = block.Primary.FacadeRotation * Vector3.right;
                Vector3 eye = block.CourtCenter + Vector3.up * EyeHeight;
                Vector3 target = block.CourtCenter + right * 12f + Vector3.up * 1.8f;
                if (block.Kind == CityCourtyardBlockKind.Passage)
                {
                    eye = block.PassageCenter + Vector3.up * EyeHeight;
                    target = eye - block.Primary.FacadeForward * 15f;
                }
                shots.Add(Shot.At($"fabric-{block.Kind}-{block.Cell.x}-{block.Cell.y}", eye,
                    target, 76f));
            }
            foreach (RoadEdge edge in layout.RoadGeometry.ReplannedEdges.Take(3))
            {
                CityRoadPath path = layout.RoadGeometry.Get(edge);
                CityRoadSample entry = path.SampleDistance(2f);
                Vector3 eye = ReplanningStreetEye(layout, new Vector3(entry.Position.x, 0f, entry.Position.y));
                CityRoadSample target = path.SampleDistance(path.Length * .8f);
                shots.Add(Shot.At($"fabric-street-{edge.A.x}-{edge.A.y}-{edge.B.x}-{edge.B.y}", eye,
                    new Vector3(target.Position.x, eye.y, target.Position.y), 62f));
            }
            foreach (CityCourtyardBlock block in layout.CourtyardBlocks
                         .Where(candidate => candidate.RearBuilding?.BuildingVariant == CityCourtyardBlockPlanner.WingVariant)
                         .GroupBy(candidate => candidate.Primary.District).Select(group => group.First()))
            {
                BuildingLot wing = block.RearBuilding;
                shots.Add(Shot.At($"fabric-{wing.District}-wing-{block.Cell.x}-{block.Cell.y}",
                    block.CourtCenter + Vector3.up * EyeHeight,
                    wing.Center + Vector3.up * (wing.Height * .25f), 86f));
            }
            foreach (BuildingLot lot in layout.BuildingLots.Where(candidate =>
                         candidate.IsBar || candidate.IsPlayerHome || candidate.IsSupermarket))
            {
                Vector3 forward = lot.FacadeForward;
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                Vector3 eye = ReplanningStreetEye(layout,
                    lot.SidewalkArrivalPosition + right * 9f + forward * 1.5f);
                shots.Add(Shot.At($"compact-entrance-{lot.Cell.x}-{lot.Cell.y}", eye,
                    lot.DoorPosition + Vector3.up, 70f));
                VerifyCompactForecourt(city, lot, issues);
            }
            foreach (var facility in infrastructure.Facilities)
            {
                // The authored plan owns the actual ground and camera anchor.
                Vector3 center = facility.Position;
                shots.Add(Shot.At("compact-infrastructure-" + facility.Id,
                    center + new Vector3(10f, 2.2f, 10f), center + Vector3.up * 2f, 65f));
            }
            BuildingLot bar = layout.BuildingLots.First(lot => lot.IsBar);
            int nightFrames = 0;
            shots.Add(Shot.At("compact-night-home-bar", bar.SidewalkArrivalPosition +
                bar.FacadeForward * 2f + Vector3.up * EyeHeight,
                bar.DoorPosition + Vector3.up * 2f, 70f, readyWhen: () =>
                {
                    if (nightFrames++ == 0)
                        GameSessionState.AdvanceGameTime((float)(420d / GameTimeState.GameMinutesPerRealSecond));
                    return nightFrames > SettleFrames;
                }));
            Debug.Log($"CITY FABRIC: curves={layout.RoadGeometry.ReplannedEdges.Count}, " +
                $"courts={layout.CourtyardBlocks.Count}, shared={layout.CourtyardConnections.Count}, " +
                $"wings={layout.CourtyardBlocks.Count(block => block.RearBuilding != null)}, " +
                $"passages={ordinary.Count(lot => lot.BuildingVariant == 3)}, physical={layout.BuildingMasses.Count}.");
            return shots.ToArray();
        }

        private static void VerifyCompactForecourt(CityGameRoot city, BuildingLot lot, List<string> issues)
        {
            Vector3 direction = lot.FacadeForward;
            Vector3 start = lot.DoorPosition + direction * .8f;
            Vector3 end = lot.SidewalkArrivalPosition;
            int samples = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(start, end) / .2f));
            float previousTop = float.NaN;
            for (int index = 0; index <= samples; index++)
            {
                Vector3 position = Vector3.Lerp(start, end, index / (float)samples);
                if (!city.World.WalkableArea.Contains(position, .32f))
                    issues.Add($"Entrance {lot.Cell} leaves the continuous pedestrian mask at {position}.");
                if (!TryCompactStaticFloor(position, out RaycastHit hit) || hit.normal.y < .7f)
                    issues.Add($"Entrance {lot.Cell} has no supported floor at {position}.");
                else
                {
                    if (!float.IsNaN(previousTop) && Mathf.Abs(hit.point.y - previousTop) >
                        CityRoadGroundBoundaryPlanner.MaximumSafeStep + .01f)
                        issues.Add($"Entrance {lot.Cell} has an unsafe physical floor step at {position}.");
                    previousTop = hit.point.y;
                }
            }
        }

        private static bool TryCompactStaticFloor(Vector3 position, out RaycastHit floor)
        {
            floor = default;
            bool found = false;
            foreach (RaycastHit hit in Physics.RaycastAll(position + Vector3.up * 2f,
                         Vector3.down, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                // People and vehicles can occupy a valid doorway while the
                // persistent floor below them must still remain continuous.
                if (hit.collider.attachedRigidbody != null || hit.collider is CapsuleCollider ||
                    hit.collider is CharacterController) continue;
                if (found && hit.distance >= floor.distance) continue;
                floor = hit;
                found = true;
            }
            return found;
        }
    }
}
