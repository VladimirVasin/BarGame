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
        [Explicit("Replanned district streets through the production camera, light and fog.")]
        public IEnumerator CityReplanning()
        {
            GameSessionState.BeginNewGame();
            Assert.That(GameSessionState.TryStartGameTimeFromWake(), Is.True);
            GameSessionState.AdvanceGameTime((float)(360d / GameTimeState.GameMinutesPerRealSecond));
            CityGameRoot city = null;
            yield return Capture(SceneIds.City,
                () =>
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    return city != null && city.Layout != null && city.World != null &&
                        city.BusPlan != null ? city : null;
                }, () => CityReplanningShots(city));
        }

        private static Shot[] CityReplanningShots(CityGameRoot city)
        {
            CityLayout layout = city.Layout;
            Assert.That(layout.SpatialPlan.IsUniform, Is.False);
            LogReplanningRouteLengths(city);
            var shots = new List<Shot>();
            BuildingLot courtyardLot = layout.BuildingLots
                .Where(candidate => candidate.IsOrdinaryBuilding &&
                    candidate.District == CityDistrictKind.Residential && candidate.BuildingVariant == 2)
                .OrderBy(candidate => (candidate.Cell - new Vector2Int(10, 10)).sqrMagnitude).First();
            CityBuildingPrototypePose courtyardPose =
                CityBuildingPrototypePlacement.ResolveExpectedCityPose(courtyardLot);
            Vector3 courtyardProbe = courtyardPose.TransformPoint(new Vector3(3f, 8f, -3.5f));
            Assert.That(Physics.Raycast(courtyardProbe, Vector3.down, out RaycastHit courtyardGround,
                16f, ~0, QueryTriggerInteraction.Ignore), Is.True);
            Assert.That(courtyardGround.point.y, Is.LessThan(courtyardLot.Center.y + 1f),
                "The recessed courtyard must reach the ground rather than a filled building proxy.");
            Vector3 courtyardEye = courtyardGround.point + Vector3.up * EyeHeight;
            Assert.That(Physics.CheckSphere(courtyardEye, .2f, ~0, QueryTriggerInteraction.Ignore),
                Is.False, "The standing camera must be clear of the courtyard's physical colliders.");
            shots.Add(Shot.At("replanning-00-open-courtyard", courtyardEye,
                courtyardPose.TransformPoint(new Vector3(-3f, 3.5f, 3f)), 82f));
            CityDistrictKind[] districts =
            {
                CityDistrictKind.OldTown, CityDistrictKind.Residential,
                CityDistrictKind.Industrial, CityDistrictKind.Nightlife
            };
            Vector2Int[] stations =
            {
                new Vector2Int(2, 9), new Vector2Int(10, 10),
                new Vector2Int(2, 2), new Vector2Int(9, 2)
            };
            for (int index = 0; index < districts.Length; index++)
            {
                CityDistrictKind district = districts[index];
                Vector2Int station = stations[index];
                BuildingLot lot = layout.BuildingLots
                    .Where(candidate => candidate.IsOrdinaryBuilding && candidate.HasRoadFrontage &&
                        candidate.District == district)
                    .OrderByDescending(candidate => candidate.BuildingVariant > 0)
                    .ThenBy(candidate => (candidate.Cell - station).sqrMagnitude)
                    .ThenBy(candidate => candidate.Cell.y).ThenBy(candidate => candidate.Cell.x).First();
                Assert.That(layout.TryGetFrontageEdge(lot, out RoadEdge edge), Is.True);
                Vector3 start = layout.GetNodeWorldPosition(edge.A);
                Vector3 end = layout.GetNodeWorldPosition(edge.B);
                Vector3 tangent = end - start;
                tangent.y = 0f;
                tangent.Normalize();
                Vector3 towardBuilding = new Vector3(-lot.FrontageDirection.x, 0f, -lot.FrontageDirection.y);
                string prefix = "replanning-" + district.ToString().ToLowerInvariant();

                Vector3 streetEye = ReplanningStreetEye(layout, Vector3.Lerp(start, end, .25f));
                shots.Add(Shot.At(prefix + "-01-street",
                    streetEye, streetEye + tangent * 22f + towardBuilding * 6f, 66f));
                Vector3 frontEye = ReplanningStreetEye(layout,
                    lot.ReturnPosition + tangent * Mathf.Min(4f, layout.GetRoadLength(edge) * .12f));
                shots.Add(Shot.At(prefix + "-02-frontage",
                    frontEye, lot.DoorPosition + Vector3.up * 4.2f, 72f));
            }
            return shots.ToArray();
        }

        private static void LogReplanningRouteLengths(CityGameRoot city)
        {
            Assert.That(city.World.FringeYardPlan.HasTunnelForecourt, Is.True);
            Assert.That(city.World.MountainBoundaryPlan.HasTunnel, Is.True);
            LastRouteCarPlan car = LastRouteCarPlan.Create(city.Layout);
            Assert.That(car.IsPresent, Is.True);
            CityTunnelTravelPlan tunnel = CityTunnelTravelPlanner.Create(
                city.World.MountainBoundaryPlan.Tunnel);
            CityTunnelForecourtDescriptor forecourt = city.World.FringeYardPlan.TunnelForecourt;
            LastRouteCarDrivePath departure = LastRouteCityDrivePlanner.CreateDeparture(
                car, city.Layout, forecourt, tunnel.FloorSurfaceY);
            LastRouteCarDrivePath arrival = LastRouteCityDrivePlanner.CreateReturn(
                car, city.Layout, forecourt, tunnel.FloorSurfaceY);
            float blackout = LastRouteCityDrivePlanner.TunnelBlackoutDepth;
            Debug.Log($"City replanning routes: departureTotal={departure.Length:F3} m; " +
                $"cityToPortal={departure.Length - blackout:F3} m; tunnel={blackout:F3} m; " +
                $"returnTotal={arrival.Length:F3} m; busLoop={city.BusPlan.LoopLength:F3} m; " +
                $"busStops={city.BusPlan.Stops.Count}.");
        }

        private static Vector3 ReplanningStreetEye(CityLayout layout, Vector3 position)
        {
            if (layout.ElevationPlan.TrySampleSurface(new Vector2(position.x, position.z),
                CitySurfaceRole.RoadTop, out float top, out _)) position.y = top;
            position.y += EyeHeight;
            return position;
        }
    }
}
