using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("City block composition, lowered authored models and actual courtyard traversal.")]
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
        }

        private static Shot[] CityFabricShots(CityGameRoot city, List<string> issues)
        {
            Debug.Log("CITY FABRIC: world ready, measuring routes and rendered composition.");
            CityLayout layout = city.Layout;
            layout.ValidateOrThrow();
            CityBuildingAssetProvider provider = CityBuildingAssetProvider.LoadOrThrow();
            Assert.That(provider.Entries.Count, Is.EqualTo(20));
            Assert.That(layout.RoadGeometry.ReplannedEdges.Count, Is.GreaterThan(3));
            foreach (CityElevationStairDescriptor stair in layout.ElevationPlan.SignatureStairs)
                Assert.That(layout.RoadGeometry.Get(stair.Edge).IsStraight, Is.True,
                    $"Signature stair {stair.Id} must preserve its straight street {stair.Edge}.");
            Assert.That(layout.CourtyardBlocks.Count, Is.GreaterThan(5));
            Assert.That(layout.CourtyardBlocks.Select(block => block.Primary.District).Distinct().Count(),
                Is.GreaterThanOrEqualTo(3), "Courtyard composition must extend beyond the OldTown pilot.");
            Assert.That(layout.CourtyardBlocks.Count(block => block.RearBuilding != null),
                Is.GreaterThan(3), "The widened plots must contain actual additional authored wings.");
            Assert.That(layout.BuildingLots.Count(lot => lot.IsBar), Is.EqualTo(1));
            Assert.That(layout.PlayerHome.Height, Is.EqualTo(8.8f).Within(.01f));
            Assert.That(layout.Supermarket.Height, Is.EqualTo(6.4f).Within(.01f));
            var ordinary = layout.BuildingLots.Where(lot => lot.IsOrdinaryBuilding).ToArray();
            Assert.That(ordinary.Where(lot => lot.District == CityDistrictKind.Industrial).Max(lot => lot.Height),
                Is.LessThan(ordinary.Where(lot => lot.District == CityDistrictKind.Residential).Min(lot => lot.Height)));
            Assert.That(ordinary.Where(lot => lot.District == CityDistrictKind.OldTown).Max(lot => lot.Height),
                Is.LessThan(ordinary.Where(lot => lot.District == CityDistrictKind.Nightlife).Min(lot => lot.Height)));
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
            foreach (CityDistrictKind district in CityLayoutGenerator.UrbanDistricts)
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
            Debug.Log($"CITY FABRIC: curves={layout.RoadGeometry.ReplannedEdges.Count}, " +
                $"courts={layout.CourtyardBlocks.Count}, shared={layout.CourtyardConnections.Count}, " +
                $"wings={layout.CourtyardBlocks.Count(block => block.RearBuilding != null)}, " +
                $"passages={ordinary.Count(lot => lot.BuildingVariant == 3)}, physical={layout.BuildingMasses.Count}.");
            return shots.ToArray();
        }
    }
}
