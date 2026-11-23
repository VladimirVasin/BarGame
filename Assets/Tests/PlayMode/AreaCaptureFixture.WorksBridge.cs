using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        private static CityRiverBridgeDescriptor WorksBridge(CityGameRoot city) =>
            city.Layout.River.Bridges.Single(b => b.Definition.Style == CityBridgeStyle.Works);

        private static void AssertWorksContacts(CityGameRoot city)
        {
            CityRiverBridgeDescriptor bridge = WorksBridge(city);
            Transform root = city.World.RiverRoot.transform.Find($"River Bridges/{bridge.Definition.Id} Road Bridge");
            Renderer steel = root.Find("Bridge Underside").GetComponent<Renderer>();
            Assert.That(steel.sharedMaterial.GetTexture("_BumpMap").name, Is.EqualTo("WorksBridgeNormal"));
            Assert.That(steel.bounds.size.z, Is.EqualTo(bridge.SpanBounds.height * 7.94f / 8f).Within(.008f));
            Assert.That(steel.bounds.max.y, Is.EqualTo(bridge.AverageY + CityStreetSurfacePlanner.RoadTop - .08f).Within(.006f));
            foreach (string end in new[] { "West", "East" })
            {
                Renderer pier = root.Find("Bridge Pier " + end).GetComponent<Renderer>();
                Assert.That(pier.bounds.max.y, Is.EqualTo(steel.bounds.min.y).Within(.006f), end + " bearing seat");
            }
        }

        private static IEnumerator WalkWorksBridgeAndRiverLandings(CityGameRoot city)
        {
            CityRiverBridgeDescriptor bridge = WorksBridge(city);
            Rect span = bridge.SpanBounds;
            PlayerMotor motor = city.Player.Motor;
            float top = bridge.AverageY + CityStreetSurfacePlanner.RoadTop;
            var west = new Vector3(span.xMin - .6f, top, span.yMin + .63f);
            var east = new Vector3(span.xMax + .6f, top, west.z);
            motor.Teleport(west + Vector3.up * PlayerFactory.GroundedRootOffset);
            Physics.SyncTransforms();
            yield return WalkTo(east, "bridge deck");
            foreach (CityRiverLandingDescriptor landing in city.Layout.River.Landings)
            {
                float d = Mathf.Sign(landing.DescentDirection.z);
                float approachZ = landing.StairBounds.center.y - d * (landing.StairBounds.height / 2f + .55f);
                CityRiverPromenadeDescriptor bank = city.Layout.River.Promenades.Single(b => b.WestBank == landing.WestBank);
                var approach = new Vector3(landing.StairBounds.center.x, RiverPromenadeY(bank, approachZ), approachZ);
                var bottom = new Vector3(landing.PlatformBounds.center.x, landing.LowerY, landing.PlatformBounds.center.y);
                motor.Teleport(approach + Vector3.up * PlayerFactory.GroundedRootOffset);
                Physics.SyncTransforms();
                yield return WalkTo(bottom, landing.Id + " down");
                yield return WalkTo(approach, landing.Id + " up");
            }

            IEnumerator WalkTo(Vector3 floor, string label)
            {
                Vector3 target = floor + Vector3.up * PlayerFactory.GroundedRootOffset;
                bool arrived = false;
                for (int frame = 0; frame < 300 && !arrived; frame++)
                {
                    arrived = motor.MoveTowardsApproachWaypoint(target, .05f, .05f);
                    Assert.That(motor.InteractionPoseMoveStalled, Is.False, $"{label}: blocked at {motor.transform.position} toward {target}");
                    yield return null;
                }
                motor.CancelInteractionPoseMove();
                Assert.That(arrived, Is.True, label);
                Assert.That(motor.transform.position.y, Is.EqualTo(target.y).Within(.14f), label + " floor");
            }
        }

        private static IEnumerator RideWorksBridge(CityGameRoot city)
        {
            CityRiverBridgeDescriptor bridge = WorksBridge(city);
            CityBusDirector bus = city.BusVehicles.First(b => b.Plan.Number == 3);
            int vehicle = city.BusVehicles.ToList().IndexOf(bus);
            CityBusRideController ride = city.BusRides[vehicle];
            CityBusPlan plan = bus.Plan;
            CityBusActor actor = bus.Actor;
            Rect span = bridge.SpanBounds;
            Transform bridgeRoot = city.World.RiverRoot.transform.Find($"River Bridges/{bridge.Definition.Id} Road Bridge");
            Bounds[] guards = new[] { "Outer Parapet", "Landing Parapet" }
                .Select(name => bridgeRoot.Find(name).GetComponent<Renderer>().bounds).OrderBy(b => b.center.z).ToArray();
            // Pick the last public stop before the bridge in the actual direction of travel.
            float closest = float.MaxValue, crossing = 0f, alongLoop = 0f;
            foreach (int index in plan.OrderedLinkIndices)
            {
                CityBusRouteLink link = plan.Links[index];
                foreach (CityBusPathSample sample in link.Samples)
                {
                    float distance = (new Vector2(sample.Position.x, sample.Position.z) - span.center).sqrMagnitude;
                    if (distance >= closest) continue;
                    closest = distance;
                    crossing = alongLoop + sample.Distance;
                }
                alongLoop += link.Length;
            }
            CityBusStopDescriptor stop = plan.Stops.OrderBy(s =>
                Mathf.Repeat(crossing - s.DistanceAlongLoop, plan.LoopLength)).First();
            var hero = city.Player;
            hero.Motor.SetInputEnabled(true);
            hero.Motor.Teleport(CityBusStopWaitPlanner.StandingPosition(plan, stop, city.Layout)
                + Vector3.up * PlayerFactory.GroundedRootOffset);
            Physics.SyncTransforms();
            foreach (CityBusDirector service in city.BusVehicles)
            {
                service.enabled = false;
                service.Actor.ReleasePresentation(service.transform);
            }
            float along = Mathf.Max(0f, stop.DistanceAlongLink - 8f);
            CityBusActor.EvaluateLink(plan.Links[stop.LinkIndex], along, out Vector3 spawn, out Vector3 heading);
            actor.PrepareSpawn(plan, new CityBusSpawnAnchor("works acceptance", stop.LinkIndex, along,
                spawn, heading, stop.RoadEdge), plan.StableSeed);
            for (int frame = 0; frame < 300 && !(actor.CurrentStop == stop && actor.DoorsFullyOpen); frame++)
            {
                bus.Advance(.08f);
                yield return null;
            }
            Assert.That(actor.CurrentStop, Is.EqualTo(stop), "03 at " + stop.Id);
            Assert.That(actor.DoorsFullyOpen, Is.True);
            var capsule = hero.GameObject.GetComponent<CharacterController>();
            var rig = (Player3DCharacterPresentation)hero.Visual;
            Vector3 hips = hero.GameObject.transform.InverseTransformPoint(rig.Registry.Anchors.Pelvis.position);
            Assert.That(CityBusRidePlan.TryCreate(actor, city.World.WalkableArea, hips, capsule.radius,
                CityBusPassengerDoor.Front, CityStreetSurfacePlanner.Create(city.Layout), out var dock), Is.True);
            hero.Motor.Teleport(dock.EntryPose.RootPosition);
            hero.GameObject.transform.rotation = dock.EntryPose.RootRotation;
            Physics.SyncTransforms();
            Assert.That(ride.BeginBoarding(), Is.True);
            for (int frame = 0; frame < 360 && ride.State != CityBusRideState.Riding; frame++) yield return null;
            Assert.That(ride.State, Is.EqualTo(CityBusRideState.Riding));
            Transform seat = ride.ActivePlan.SeatAnchor;
            bool entered = false, captured = false, crossed = false;
            float direction = 0f, clearance = float.MaxValue;
            for (int tick = 0; tick < 6500 && !crossed; tick++)
            {
                bus.Advance(.10f);
                ride.RefreshPassengerPose();
                Assert.That(actor.HasPlayerPassenger && ride.ActivePlan.SeatAnchor == seat, Is.True);
                Vector3 pos = actor.Position;
                if (span.Contains(new Vector2(pos.x, pos.z)))
                {
                    if (!entered) direction = Mathf.Sign(actor.TravelDirection.x);
                    entered = true;
                    Bounds hull = actor.WorldBodyBounds;
                    clearance = Mathf.Min(clearance, Mathf.Min(hull.min.z - guards[0].max.z, guards[1].min.z - hull.max.z));
                    if (!captured && Mathf.Abs(pos.x-span.center.x) < .7f)
                    {
                        ride.ActivePlan.EvaluateRideCamera(0f, -5f, out Vector3 eye, out Quaternion rotation);
                        ParkFrame(city.Camera, "day-river-works-passenger", eye, eye + rotation*Vector3.forward*8f, 68f);
                        captured = true;
                    }
                }
                if (entered) crossed = direction > 0 ? pos.x > span.xMax + 4f : pos.x < span.xMin - 4f;
                if (tick % 5 == 0) yield return null;
            }
            Assert.That(crossed && captured, Is.True, $"03 from {stop.Id} at {actor.Position}, state {actor.MotionState}");
            Assert.That(clearance, Is.GreaterThan(.05f), "Bus hull clears both parapets");
            TestContext.WriteLine($"Works bridge: line 03 from {stop.Id}, minimum parapet clearance {clearance:F2} m.");
            ride.ForceReleaseForBusLifecycle();
        }
    }
}
