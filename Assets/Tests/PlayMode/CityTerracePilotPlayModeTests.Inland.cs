using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        private static readonly Vector2Int InlandWest = new Vector2Int(12, 8);
        private static readonly Vector2Int InlandEast = new Vector2Int(13, 8);

        private void AssertInlandStreetSection()
        {
            CityLayout layout = city.Layout;
            CityStreetSupportPlan support = CityStreetSupportPlan.ForLayout(layout);
            foreach (BuildingLot lot in layout.BuildingLots.Where(lot => lot.IsPlayerHome || lot.IsBar))
            {
                RoadEdge frontage = RoadEdge.ForCellFrontage(lot.Cell, lot.FrontageDirection);
                CityRoadPath path = layout.RoadGeometry.Get(frontage);
                Vector2 point = new Vector2(lot.ReturnPosition.x, lot.ReturnPosition.z);
                float expected = layout.ElevationPlan.SampleRoadDatum(frontage, path.Project(point).DistanceAlong / path.Length);
                Assert.That(lot.ReturnPosition.y, Is.EqualTo(expected).Within(.001f), "Entrance return must follow the actual road");
            }
            Assert.That(layout.RoadGeometry.GetNodePosition(InlandWest), Is.EqualTo(new Vector2(155.85f, 52.25f)));
            Assert.That(layout.RoadGeometry.GetNodePosition(InlandEast), Is.EqualTo(new Vector2(182.5f, 52.25f)));
            foreach (Vector2Int node in new[] { InlandWest, InlandEast })
            {
                Assert.That(layout.RoadGeometry.TryGetJunction(node, out CityRoadJunction junction), Is.True);
                Assert.That(junction.SidewalkPaths.Count, Is.GreaterThan(0));
            }
            foreach (RoadEdge edge in layout.RoadEdges)
            {
                if (!edge.Contains(InlandWest) && !edge.Contains(InlandEast)) continue;
                AssertStreetProfileContacts(layout, support, edge);
            }
            Vector2 west = layout.RoadGeometry.GetNodePosition(InlandWest);
            Vector2 east = layout.RoadGeometry.GetNodePosition(InlandEast);
            bool NearInlandJunction(Vector2 center) => (center - west).sqrMagnitude <= 144f || (center - east).sqrMagnitude <= 144f;
            CityBusPlan bus = CityBusPlanner.Create(layout);
            Assert.That(bus.IsEmpty, Is.False, "The changed junctions must retain Route 01");
            int sweptContacts = 0;
            for (int index = 0; index < bus.OrderedLinkIndices.Count; index++)
            {
                CityBusRouteLink current = bus.Links[bus.OrderedLinkIndices[index]];
                CityBusRouteLink next = bus.Links[bus.OrderedLinkIndices[
                    (index + 1) % bus.OrderedLinkIndices.Count]];
                Assert.That(Vector3.Distance(current.Samples[current.Samples.Count - 1].Position,
                    next.Samples[0].Position), Is.LessThanOrEqualTo(.001f), current.Id + " has a detached lane join");
            }
            foreach (CityBusRouteLink link in bus.Links)
            {
                if (!link.Samples.Any(sample => sample.Position.x > 145f && sample.Position.z > 40f && sample.Position.z < 65f)) continue;
                Assert.That(link.Clearance.IsClear, Is.True, link.Id + " swept body fails at the new junction");
                foreach (CityBusPathSample sample in link.Samples)
                {
                    if (!link.Id.StartsWith("bus:road:")) continue;
                    Assert.That(support.TrySampleTop(new Vector2(sample.Position.x, sample.Position.z),
                        FootstepGroundKind.Concrete, out float top), Is.True, link.Id);
                    Assert.That(sample.Position.y, Is.EqualTo(top).Within(.01f), link.Id + " missed the physical road profile");
                }
                sweptContacts += AssertBusBodyContacts(support, bus.Vehicle, link, NearInlandJunction);
            }
            Assert.That(sweptContacts, Is.GreaterThan(0),
                "Route 01 must actually traverse the changed section for this vehicle proof");
            CityTerraceTransportPlan draft = CityTerraceTransportPlan.Create(layout, CityTerraceMasterPlan.Create(layout));
            Assert.That(draft.HasProtectedConflicts || draft.HasBuildingConflicts, Is.False,
                "The live section must preserve the surveyed full-corridor clearance");
        }

        private static void AssertStreetProfileContacts(CityLayout layout, CityStreetSupportPlan support, RoadEdge edge)
        {
            CityRoadPath path = layout.RoadGeometry.Get(edge);
            float before = layout.ElevationPlan.SampleRoadDatum(edge, 0f);
            int samples = Mathf.CeilToInt(path.Length / .25f);
            for (int index = 0; index <= samples; index++)
            {
                float distance = path.Length * index / samples;
                CityRoadSample station = path.SampleDistance(distance);
                float datum = layout.ElevationPlan.SampleRoadDatum(edge, distance / path.Length);
                if (index > 0)
                    Assert.That(Mathf.Abs(datum - before) / (path.Length / samples) * 100f,
                        Is.LessThanOrEqualTo(6.01f), edge + " exceeds the street grade");
                before = datum;
                Assert.That(support.TrySampleTop(station.Position, FootstepGroundKind.Concrete, out float top),
                    Is.True, edge + " has an unsupported road seam at " + distance);
                Assert.That(top, Is.EqualTo(datum + CityStreetSurfacePlanner.RoadTop).Within(.025f), edge + " road height at " + station.Position);
                if (index % 8 != 0) continue;
                Assert.That(Physics.Raycast(new Vector3(station.Position.x, top + .4f, station.Position.y),
                    Vector3.down, out RaycastHit hit, .7f, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore), Is.True, edge + " road skin has no physical support at " + station.Position);
                Assert.That(hit.point.y, Is.EqualTo(top).Within(.04f), edge + " old terrain/road overlaps the replacement skin at " + station.Position);
            }
        }

        private static int AssertBusBodyContacts(CityStreetSupportPlan support, CityBusDesignVehicle vehicle,
            CityBusRouteLink link, System.Predicate<Vector2> inSection)
        {
            int contacts = 0;
            int bodySteps = Mathf.CeilToInt(vehicle.InflatedLength / .5f);
            for (int index = 0; index < link.Samples.Count; index += 10)
            {
                CityBusPathSample sample = link.Samples[index];
                Vector2 center = new Vector2(sample.Position.x, sample.Position.z);
                if (!inSection(center)) continue;
                Vector2 forward = new Vector2(sample.Forward.x, sample.Forward.z).normalized;
                Vector2 right = new Vector2(forward.y, -forward.x);
                for (int body = 0; body <= bodySteps; body++)
                for (int side = -1; side <= 1; side++)
                {
                    Vector2 point = center + forward * Mathf.Lerp(-vehicle.InflatedLength * .5f,
                        vehicle.InflatedLength * .5f, body / (float)bodySteps) + right * (vehicle.InflatedWidth * .5f * side);
                    bool road = support.TrySampleTop(point, FootstepGroundKind.Concrete, out float roadTop);
                    bool paving = support.TrySampleTop(point, FootstepGroundKind.Stone, out float pavingTop);
                    Assert.That(road || paving, Is.True, link.Id + " unsupported body at " + point);
                    if (road && paving)
                        Assert.That(pavingTop - roadTop, Is.LessThanOrEqualTo(.01f), link.Id + " body reaches a raised kerb");
                    float top = road && paving ? Mathf.Max(roadTop, pavingTop) : road ? roadTop : pavingTop;
                    Assert.That(Physics.Raycast(new Vector3(point.x, top + .3f, point.y), Vector3.down,
                        out RaycastHit hit, .6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore),
                        Is.True, link.Id + " body has no physical paving at " + point);
                    Assert.That(hit.point.y, Is.EqualTo(top).Within(.04f), link.Id + " body support collider differs");
                    contacts++;
                }
            }
            return contacts;
        }

        private IEnumerator WalkInlandStreetSection(CityTerraceDistrictPlan district, Vector3 eastStreet)
        {
            CityLayout layout = city.Layout;
            var lowerApproach = new[] { eastStreet,
                InlandGround(new Vector2(eastStreet.x, district.LowerHouseStreetWorld.z)), district.LowerHouseStreetWorld };
            yield return WalkCityRoute(lowerApproach, false, "lower-house-street", district);
            yield return WalkCityRoute(district.LowerHouseRouteWorld, false, "lower-house-down", district);
            yield return WalkCityRoute(district.LowerHouseRouteWorld, true, "lower-house-up", district);
            yield return WalkCityRoute(lowerApproach, true, "lower-house-street-return", district);
            var route = new List<Vector3> { eastStreet };
            route.Add(InlandGround(new Vector2(130f, 48f)));
            foreach (RoadEdge edge in new[] {
                new RoadEdge(new Vector2Int(11, 8), InlandWest), new RoadEdge(InlandWest, InlandEast) })
            {
                CityRoadPath path = layout.RoadGeometry.Get(edge);
                for (float distance = 4f; distance < path.Length - 4f; distance += 2f)
                {
                    CityRoadSample sample = path.SampleDistance(distance);
                    route.Add(InlandGround(sample.Position + sample.Right * 4f));
                }
            }
            Vector2 corner = layout.RoadGeometry.GetNodePosition(InlandEast) - Vector2.one * 4f;
            route.Add(InlandGround(corner));
            BuildingLot bar = layout.BuildingLots.Single(lot => lot.IsBar);
            Vector3 barArrival = bar.SidewalkArrivalPosition;
            route.Add(InlandGround(new Vector2(barArrival.x, barArrival.z)));
            Assert.That(city.World.TryGetBar(bar.BarId, out BarEntrance entrance), Is.True);
            PlayerDoorActionTarget barDoor = entrance.GetComponent<PlayerDoorActionTarget>();
            route.Add(barDoor.Plan.EntryRootPosition - Vector3.up * PlayerFactory.GroundedRootOffset);
            yield return WalkCityRoute(route, false, "inland-junctions-to-bar", district);
            CaptureCity("live-07-inland-bar-approach", route[route.Count - 2] + Vector3.up * 1.65f,
                bar.DoorPosition + Vector3.up * 1.5f);
            BuildingLot home = layout.PlayerHome;
            PlayerDoorActionTarget homeDoor = city.World.PlayerHome.GetComponent<PlayerDoorActionTarget>();
            Vector3 homeArrival = home.SidewalkArrivalPosition;
            var homeRoute = new[] {
                route[route.Count - 1], route[route.Count - 2], InlandGround(new Vector2(178f, -4f)),
                InlandGround(new Vector2(homeArrival.x, homeArrival.z)),
                homeDoor.Plan.EntryRootPosition - Vector3.up * PlayerFactory.GroundedRootOffset };
            float length = 0f;
            for (int i = 1; i < homeRoute.Length; i++) length += Vector3.Distance(homeRoute[i - 1], homeRoute[i]);
            Assert.That(length, Is.InRange(53f, 64f), "Home–Bar must retain its short door-to-door route");
            yield return WalkCityRoute(homeRoute, false, "inland-bar-home", district);
            yield return WalkCityRoute(homeRoute, true, "inland-home-bar", district);
            yield return WalkCityRoute(route, true, "inland-bar-junctions", district);
            CaptureCity("live-08-level-court-exit", district.LowerPassRampEndWorld + Vector3.up * 1.65f,
                eastStreet + new Vector3(5f, 1f, 0f));
        }

        private IEnumerator CapturePlayerView(string name, Vector3 target, System.Action<Camera> check = null)
        {
            PlayerCameraFollow follow = city.Camera.GetComponent<PlayerCameraFollow>();
            float previousPitch = follow.TargetOrbitPitch;
            bool cinematic = follow.CinematicMotionEnabled;
            Vector3 direction = target - (city.Player.Motor.transform.position + Vector3.up * 1.6f);
            float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            float turn = Mathf.DeltaAngle(city.Camera.transform.eulerAngles.y, yaw);
            float pitch = -Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;
            try
            {
                follow.SetCinematicMotionEnabled(false);
                follow.RotateYaw(turn);
                follow.RotatePitch(pitch - previousPitch);
                follow.Snap();
                yield return null;
                AssertCameraClearance(follow);
                Assert.That(city.Camera.fieldOfView, Is.EqualTo(follow.FollowFieldOfView).Within(.1f));
                AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "CityTerracePilot", name, 1280, 720);
                check?.Invoke(city.Camera);
            }
            finally
            {
                follow.RotateYaw(-turn);
                follow.RotatePitch(previousPitch - follow.TargetOrbitPitch);
                follow.SetCinematicMotionEnabled(cinematic);
                follow.Snap();
            }
        }

        private Vector3 InlandGround(Vector2 point)
        {
            Assert.That(CityStreetSupportPlan.ForLayout(city.Layout).TrySampleTop(point, out float height, out _),
                Is.True, "Missing public pavement at " + point);
            return new Vector3(point.x, height, point.y);
        }
    }
}
