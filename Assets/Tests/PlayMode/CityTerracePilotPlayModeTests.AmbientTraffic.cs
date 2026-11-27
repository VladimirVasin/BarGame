using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest]
        public IEnumerator LiveCity_AmbientPassByReusesDriverAndClearsRoad()
        {
            Assert.That(GameSessionState.TryStartGameTimeAt(14 * 60), Is.True);
            yield return LoadProductionCity();
            var traffic = city.AmbientTraffic;
            traffic.AutoAdvance = false;
            Time.timeScale = 0f;
            city.Player.Motor.SetInputEnabled(false);
            city.Camera.GetComponent<PlayerCameraFollow>().enabled = false;
            Vector3 hero = city.Player.GameObject.transform.position;
            Assert.That(traffic.Count, Is.EqualTo(AmbientTrafficRules.CirculatingCapacity));

            AdvanceSeconds(CityAmbientTraffic.MinimumPassByDelay - .25f);
            Assert.That(traffic.Count, Is.EqualTo(AmbientTrafficRules.CirculatingCapacity), "Respect the first interval.");
            var car = AwaitPassBy();
            var carObject = car.Driver.gameObject;
            Assert.That(Vector3.Distance(hero, car.Position), Is.InRange(CityAmbientCarPlan.PassByMinimumEntry,
                CityAmbientCarPlan.PassByMaximumEntry));
            Assert.That(car.Plan.CanWaitOutsideJunction(car.Driver.Distance), Is.True);
            Assert.That(car.Person.IsDriving, Is.True);
            Assert.That(car.Person.GetComponent<DefaultNpcAppearance>().AppearanceKey,
                Is.EqualTo(DefaultNpcPopulation.AmbientDriverId(AmbientTrafficRules.CirculatingCapacity)));
            Assert.That(carObject.GetComponent<BoxCollider>().enabled, Is.True);
            for (int i = 0; i < traffic.Count - 1; i++)
                Assert.That(car.Hull.Overlaps(traffic.GetCar(i).Hull), Is.False);
            city.Pedestrians.VehicleTraffic.Refresh();
            Assert.That(city.Pedestrians.VehicleTraffic.PhysicallyBlocks(car.Hull.Centre, .35f), Is.True);

            float distance = car.Driver.Distance;
            traffic.AutoAdvance = true;
            yield return null;
            yield return null;
            Assert.That(car.Driver.Distance, Is.EqualTo(distance), "Pause holds a live pass-by.");
            traffic.AutoAdvance = false;

            float closest = float.PositiveInfinity;
            bool captured = false;
            for (float elapsed = 0f; elapsed < 90f && carObject.activeSelf; elapsed += .25f)
            {
                AdvanceSeconds(.25f);
                closest = Mathf.Min(closest, Vector3.Distance(hero, car.Position));
                if (captured || closest > CityAmbientCarPlan.PassByNearRadius ||
                    SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) continue;
                captured = true;
                Transform camera = city.Camera.transform;
                var originalView = new Pose(camera.position, camera.rotation);
                car.Plan.Path.Sample(car.Driver.Distance + 7f, out var lane, out var forward);
                Vector3 eye = lane + Vector3.up * 2f - Vector3.Cross(Vector3.up, forward).normalized * 1.2f;
                camera.SetPositionAndRotation(eye, Quaternion.LookRotation(car.Position + Vector3.up * .8f - eye));
                Time.timeScale = 1f;
                for (int frame = 0; frame < 4; frame++)
                {
                    traffic.Advance(.05f);
                    yield return null;
                }
                AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "AmbientPassBy", "pass", 1280, 720);
                Time.timeScale = 0f;
                camera.SetPositionAndRotation(originalView.position, originalView.rotation);
                yield return null;
            }
            Assert.That(closest, Is.LessThan(CityAmbientCarPlan.PassByNearRadius + 1f), "Drive near the hero before leaving.");
            Assert.That(carObject.activeSelf, Is.False, "The sedan must leave the traffic simulation outside its radius.");
            Assert.That(Vector3.Distance(hero, car.Position), Is.GreaterThan(CityAmbientCarPlan.PassByReleaseRadius));
            Assert.That(traffic.Count, Is.EqualTo(AmbientTrafficRules.CirculatingCapacity));
            Assert.That(car.Beams.All(beam => !beam.enabled), Is.True);
            Assert.That(car.Audio.OwnedSources.All(source => !source.isPlaying), Is.True);

            AdvanceSeconds(CityAmbientTraffic.MinimumPassByDelay - .25f);
            Assert.That(traffic.Count, Is.EqualTo(AmbientTrafficRules.CirculatingCapacity), "Respect the interval after release.");
            Assert.That(AwaitPassBy(), Is.SameAs(car), "Reuse the same sedan and permanently assigned driver.");

            void AdvanceSeconds(float seconds)
            {
                for (float elapsed = 0f; elapsed < seconds; elapsed += .25f)
                {
                    traffic.Advance(.25f);
                    Physics.SyncTransforms();
                }
            }

            CityAmbientTraffic.Car AwaitPassBy()
            {
                for (float elapsed = 0f; elapsed < CityAmbientTraffic.MaximumPassByDelay &&
                    traffic.Count < AmbientTrafficRules.Capacity; elapsed += .25f) AdvanceSeconds(.25f);
                Assert.That(traffic.Count, Is.EqualTo(AmbientTrafficRules.Capacity), "Expected a clear approach near " + hero);
                return traffic.GetCar(traffic.Count - 1);
            }
        }

        [UnityTest]
        [Category("CityTrafficPassage")]
        public IEnumerator LiveCity_AmbientTrafficClearsEastSlopeJunctionQueue()
        {
            Assert.That(GameSessionState.TryStartGameTimeAt(14 * 60), Is.True);
            yield return LoadProductionCity();
            var traffic = city.AmbientTraffic;
            Assert.That(traffic.Count, Is.EqualTo(AmbientTrafficRules.CirculatingCapacity));
            traffic.AutoAdvance = false;
            // Freeze unrelated schedules; advance the real four traffic actors
            // explicitly, including physics occupancy after every safety step.
            Time.timeScale = 0f;
            city.Player.Motor.SetInputEnabled(false);
            city.Player.Motor.Teleport(new Vector3(72f, 6.3f, -145f));
            var southCar = traffic.GetCar(1);
            var northCar = traffic.GetCar(2);
            var southBus = city.Bus.GetVehicle(3);
            var northBus = city.Bus.GetVehicle(4);
            // Recorded 2026-11-15: route 02 waits behind car 1, route 03
            // behind car 2, at the foot of the eastern hillside. Rewind the
            // queues to their approaches: once both noses enter the narrow
            // lane shifts, neither can safely drive forward through the other.
            float southApproach = PlaceCar(southCar, new Vector3(78.10502f, 6.83802f, -133.7672f));
            float northApproach = PlaceCar(northCar, new Vector3(78.051216f, 6.978259f, -147.432114f));
            PlaceBus(southBus, "bus:junction:9:1:right-wide:8:1:9:0:route-02:84", 16.66601f, southApproach);
            PlaceBus(northBus, "bus-route:default-coastal:route-03:link:4", 21.26222f, northApproach);
            Physics.SyncTransforms();
            float southTravel = 0f, northTravel = 0f, southBusTravel = 0f, northBusTravel = 0f;
            foreach (var bus in new[] { southBus, northBus })
            {
                var obstacle = CityBusObstacleState.Clear;
                traffic.AccumulateObstacle(bus.Actor, 18f, ref obstacle);
                Assert.That(obstacle.MustStop, Is.True, "Reproduce the actual queued bus: " + bus.Plan.RouteId);
                Assert.That(obstacle.ForwardClearance, Is.LessThan(4f));
            }
            Debug.Log("East slope traffic reproduction: south=" + southCar.Position.ToString("F3") +
                "; north=" + northCar.Position.ToString("F3"));
            var buses = new[] { southBus, northBus };
            for (int tick = 0; tick < 1200; tick++)
            {
                float southBefore = southCar.Driver.Distance, northBefore = northCar.Driver.Distance;
                float southBusBefore = southBus.DistanceAlongLoop, northBusBefore = northBus.DistanceAlongLoop;
                traffic.Advance(.05f);
                Physics.SyncTransforms();
                southBus.Advance(.05f);
                northBus.Advance(.05f);
                Physics.SyncTransforms();
                southTravel += Mathf.Repeat(southCar.Driver.Distance - southBefore, southCar.Plan.Length);
                northTravel += Mathf.Repeat(northCar.Driver.Distance - northBefore, northCar.Plan.Length);
                southBusTravel += Mathf.Repeat(southBus.DistanceAlongLoop - southBusBefore, southBus.Plan.LoopLength);
                northBusTravel += Mathf.Repeat(northBus.DistanceAlongLoop - northBusBefore, northBus.Plan.LoopLength);
                Assert.That(southCar.Hull.Overlaps(northCar.Hull), Is.False, "Sedans must pass without contact.");
                foreach (var bus in buses)
                {
                    var hull = new CityTrafficHull(bus.Actor.Position, bus.Actor.Rotation, bus.Actor.LocalVisualBounds);
                    Assert.That(southCar.Hull.Overlaps(hull), Is.False, "South sedan/bus contact: " + bus.Plan.RouteId + "; tick=" + tick);
                    Assert.That(northCar.Hull.Overlaps(hull), Is.False, "North sedan/bus contact: " + bus.Plan.RouteId + "; tick=" + tick);
                }
                if (tick % 100 == 0)
                {
                    Debug.Log("East slope queue progress: south=" + southTravel.ToString("F2") +
                        "; north=" + northTravel.ToString("F2") +
                        "; holds=" + southCar.Driver.Model.HoldDistance + "/" + northCar.Driver.Model.HoldDistance +
                        "; physical=" + southCar.PhysicalBlocker?.name + "/" + northCar.PhysicalBlocker?.name +
                        "; cars=" + southCar.Position.ToString("F2") + "/" + northCar.Position.ToString("F2") +
                        "; buses=" + southBus.Actor.Position.ToString("F2") + "/" + northBus.Actor.Position.ToString("F2"));
                    yield return null;
                }
            }
            Assert.That(southTravel, Is.GreaterThan(southApproach + 12f), "South queue leader must clear the junction.");
            Assert.That(northTravel, Is.GreaterThan(northApproach + 12f), "North queue leader must clear the junction.");
            Assert.That(southBusTravel, Is.GreaterThan(southApproach + 12f), "Route 02 must resume.");
            Assert.That(northBusTravel, Is.GreaterThan(northApproach + 12f), "Route 03 must resume.");

            float PlaceCar(CityAmbientTraffic.Car car, Vector3 recordedRearPoint)
            {
                float nearest = car.Plan.Path.FindNearestDistance(recordedRearPoint);
                car.Plan.Path.Sample(nearest, out _, out var direction);
                Vector3 rear = car.Bounds.center - Vector3.forward * car.Bounds.extents.z;
                Vector3 rootPosition = recordedRearPoint - Quaternion.LookRotation(direction) * rear;
                float at = car.Plan.Length + Mathf.Repeat(car.Plan.Path.FindNearestDistance(rootPosition), car.Plan.Length);
                for (float rewind = 30f; rewind <= 120f; rewind += 1f)
                {
                    if (!car.Plan.CanWaitOutsideJunction(at - rewind)) continue;
                    car.Driver.Begin(car.Plan.Path, car.Driver.Model.Profile, 0f, at - rewind);
                    Debug.Log("Queue safe approach " + car.Plan.RouteId + ": " + rewind + " m before recorded jam.");
                    return rewind;
                }
                Assert.Fail("No safe approach before the recorded junction: " + car.Plan.RouteId);
                return 0f;
            }

        }

        [UnityTest]
        [Category("CityTrafficPassage")]
        public IEnumerator LiveCity_Route03ClearsSedanAndRoute02AtWorksJunction()
        {
            Assert.That(GameSessionState.TryStartGameTimeAt(14 * 60), Is.True);
            yield return LoadProductionCity();
            var traffic = city.AmbientTraffic;
            var sedan = traffic.GetCar(2);
            var turning = city.Bus.GetVehicle(2);
            var through = city.Bus.GetVehicle(5);
            Time.timeScale = 0f;
            traffic.AutoAdvance = false;
            city.Player.Motor.SetInputEnabled(false);
            city.Player.Motor.Teleport(new Vector3(29.1f, 4.1f, -124.9f));

            // Restore the occupied junction as recorded, including the leading sedan.
            var recordedCentre = new Vector3(22.443903f, 4.8f, -128.553329f);
            float nearest = sedan.Plan.Path.FindNearestDistance(recordedCentre);
            sedan.Plan.Path.Sample(nearest, out _, out var heading);
            var axle = recordedCentre - Quaternion.LookRotation(heading) * sedan.Bounds.center;
            float entry = sedan.Plan.Length + Mathf.Repeat(sedan.Plan.Path.FindNearestDistance(axle), sedan.Plan.Length);
            sedan.Driver.Begin(sedan.Plan.Path, sedan.Profile, 0f, entry);
            PlaceBus(turning, "bus:road:7:2:7:1:route-02:80", 14.08599f);
            PlaceBus(through, "bus-route:default-coastal:route-03:link:9", .3086215f);
            Physics.SyncTransforms();
            float turnStart = turning.DistanceAlongLoop, throughStart = through.DistanceAlongLoop;
            // Someone is crossing immediately beyond the seam. Both queues
            // must wait safely, then make progress once that lane is clear.
            sedan.Plan.Path.Sample(entry + 11f, out var crossing, out var crossingHeading);
            var hero = city.Player.Motor;
            hero.Teleport(crossing + Vector3.up * PlayerFactory.GroundedRootOffset);
            Physics.SyncTransforms();
            for (int tick = 0; tick < 1300; tick++)
            {
                traffic.Advance(.05f);
                Physics.SyncTransforms();
                through.Advance(.05f);
                turning.Advance(.05f);
                Physics.SyncTransforms();
                var a = new CityTrafficHull(turning.Actor.Position, turning.Actor.Rotation, turning.Actor.LocalVisualBounds);
                var b = new CityTrafficHull(through.Actor.Position, through.Actor.Rotation, through.Actor.LocalVisualBounds);
                Assert.That(a.Overlaps(b) || a.Overlaps(sedan.Hull) || b.Overlaps(sedan.Hull), Is.False,
                    "Vehicle bodies touched at step " + tick);
                Assert.That(sedan.Hull.ContainsCapsule(hero.transform.position, .35f) ||
                    a.ContainsCapsule(hero.transform.position, .35f) || b.ContainsCapsule(hero.transform.position, .35f),
                    Is.False, "The pedestrian must stay clear of all three bodies.");
                if (tick == 199)
                {
                    Assert.That(sedan.Driver.Distance - entry, Is.GreaterThan(1f), "Cross the first surface seam.");
                    Assert.That(sedan.Driver.Speed, Is.LessThan(.05f), "Stop for the crossing pedestrian.");
                    hero.Teleport(crossing + Vector3.Cross(Vector3.up, crossingHeading) * 4f +
                        Vector3.up * PlayerFactory.GroundedRootOffset);
                    Physics.SyncTransforms();
                }
                if (tick % 100 == 0) yield return null;
                if (sedan.Driver.Distance - entry > 22f && turning.DistanceAlongLoop - turnStart > 22f &&
                    through.DistanceAlongLoop - throughStart > 22f) yield break;
            }
            Assert.Fail("Queue did not clear. Sedan " + sedan.Position + " physical blocker " + sedan.PhysicalBlocker?.name +
                ", route 02 " + turning.Actor.Position + ", route 03 " + through.Actor.Position);
        }

        private static void PlaceBus(CityBusDirector bus, string linkId, float along, float approach = 0f)
        {
            int index = Enumerable.Range(0, bus.Plan.Links.Count).Single(i => bus.Plan.Links[i].Id == linkId);
            along -= approach;
            while (along < 0f)
            {
                var order = bus.Plan.OrderedLinkIndices;
                int slot = Enumerable.Range(0, order.Count).Single(i => order[i] == index);
                index = order[(slot + order.Count - 1) % order.Count];
                along += bus.Plan.Links[index].Length;
            }
            // Manual safety steps run while physics time is paused. Inspect
            // the current pose rather than a previous interpolated frame.
            bus.Actor.RigidBody.interpolation = RigidbodyInterpolation.None;
            bus.Actor.ApplyDetachedPhase(new CityBusTimetable.Sample(index, along, 0f, -1, 0f, -1), 0);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator LiveCity_AmbientTrafficSharesRoadsAndKeepsEveryBusPlatformAccessible()
        {
            Assert.That(GameSessionState.TryStartGameTimeAt(14 * 60), Is.True);
            yield return LoadProductionCity();
            var traffic = city.AmbientTraffic;
            Assert.That(traffic, Is.Not.Null);
            Assert.That(traffic.Count, Is.EqualTo(AmbientTrafficRules.CirculatingCapacity));
            traffic.AutoAdvance = false;
            for (int i = 0; i < traffic.Count; i++)
            {
                var actor = traffic.GetCar(i);
                Assert.That(actor.Driver.GetComponent<BoxCollider>().enabled, Is.True);
                Assert.That(actor.Driver.GetComponent<Rigidbody>().isKinematic, Is.True);
                Assert.That(actor.Driver.GetComponentInChildren<LastRouteCarSeatInteraction>(), Is.Null);
                Assert.That(actor.Person.IsDriving, Is.True);
                foreach (var part in actor.Registry.Bindings)
                    if (part.Role == "headlight")
                    {
                        Assert.That(part.Renderer.sharedMaterial.shader.isSupported, Is.True, "The lens shader must render.");
                        var block = new MaterialPropertyBlock();
                        part.Renderer.GetPropertyBlock(block);
                        Assert.That(block.GetColor("_BaseColor"), Is.EqualTo(LastRouteCarHeadlights.LensColor));
                    }
                actor.Plan.Path.Sample(actor.Plan.Length, out var a, out var f);
                actor.Plan.Path.Sample(actor.Plan.Length * 2f, out var b, out var g);
                Assert.That(Vector3.Distance(a, b), Is.LessThan(.08f), actor.Plan.RouteId + " circuit seam");
                Assert.That(Vector3.Dot(f, g), Is.GreaterThan(.99f));
            }

            var car = traffic.GetCar(0);
            float startDistance = -1f;
            Vector3 direction = Vector3.forward;
            // City streets are split into short road/junction links. Find a straight
            // corridor across those seams rather than requiring one very long link.
            for (float d = car.Plan.Length + 12f; d < car.Plan.Length * 2f - 25f; d += 4f)
            {
                car.Plan.Path.Sample(d, out var p, out var f);
                car.Plan.Path.Sample(d + 18f, out var q, out var g);
                if (Vector3.Dot(f, g) < .998f || Vector3.Distance(p, q) < 17.95f) continue;
                if (car.Plan.KeepJunctionExitClear(d, d + 11f) < d + 5f) continue;
                bool clear = true;
                for (int i = 1; i < traffic.Count; i++)
                    if (Vector3.Distance(p, traffic.GetCar(i).Position) < 40f) clear = false;
                for (int i = 0; i < city.Bus.VehicleCount; i++)
                    if (Vector3.Distance(p, city.Bus.GetVehicle(i).Actor.Position) < 40f) clear = false;
                if (clear) { startDistance = d; direction = f; break; }
            }
            Assert.That(startDistance, Is.Positive, "A clear public straight for the crossing reproduction.");
            car.Driver.Begin(car.Plan.Path, car.Driver.Model.Profile, 0f, startDistance);
            car.Plan.Path.Sample(startDistance + 18f, out var crossing, out _);
            city.Player.Motor.SetInputEnabled(false);
            city.Player.Motor.Teleport(crossing + Vector3.up * PlayerFactory.GroundedRootOffset);
            Physics.SyncTransforms();
            float initial = car.Driver.Distance;
            float brakeVolume = 0f;
            for (int tick = 0; tick < 240; tick++)
            {
                traffic.Advance(.05f);
                brakeVolume = Mathf.Max(brakeVolume, car.Feedback.BrakeVolume);
                if (tick % 4 == 0) yield return null;
            }
            Assert.That(brakeVolume, Is.Positive, "Obstruction braking drives the road friction sound.");
            Assert.That(car.Feedback.BrakeVolume, Is.Zero, "A stationary sedan no longer sounds as if its tyres are moving.");
            Assert.That(car.Driver.Distance - initial, Is.GreaterThan(4f), "Sedan never left its entry: " + car.Position +
                "; physical=" + car.PhysicalBlocker?.name + "; hold=" + car.Driver.Model.HoldDistance + "; at=" + initial);
            Assert.That(car.Driver.Speed, Is.LessThan(.05f), "The sedan must stop before the hero.");
            Assert.That(car.Hull.ContainsCapsule(city.Player.GameObject.transform.position, .35f), Is.False);
            int firstCues = car.Feedback.HornCount;
            Assert.That(firstCues, Is.Positive, "A continuously blocked sedan must signal after braking.");
            bool pressed = false;
            float waiting = car.Driver.Distance;
            for (int tick = 0; tick < 420; tick++)
            {
                traffic.Advance(.05f);
                if (!pressed && car.Feedback.HornReach >= .99f)
                {
                    yield return null;
                    var rig = car.Person.Rig;
                    var hand = rig.HandPose.GetAnatomicalFrame(false);
                    var palm = rig.RightHand.position + rig.RightHand.rotation * hand.PalmCentreInHand;
                    var hub = car.Registry.SteeringWheelPivot.position + car.Driver.SteeringWheelDriverSide * .016f;
                    Assert.That(Vector3.Distance(palm, hub), Is.LessThan(.025f), "The horn cue has a palm contact on the pad.");
                    Assert.That(car.Person.LeftGripDistance, Is.LessThan(.025f), "The other hand keeps its grip.");
                    if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                    {
                        var shot = city.Camera.transform;
                        var previous = new Pose(shot.position, shot.rotation);
                        Vector3 view = car.Driver.transform.forward * 2.4f + Vector3.up * .8f;
                        shot.SetPositionAndRotation(hub + view, Quaternion.LookRotation(-view));
                        AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "AmbientTraffic", "horn-contact", 1280, 720);
                        shot.SetPositionAndRotation(previous.position, previous.rotation);
                    }
                    pressed = true;
                }
                if (tick % 8 == 0) yield return null;
            }
            Assert.That(pressed, Is.True, "The hand must finish reaching the horn during a repeated cue.");
            Assert.That(car.Feedback.HornCount, Is.GreaterThan(firstCues));
            Assert.That(car.Driver.Distance, Is.EqualTo(waiting).Within(.06f), "No collision bypass after prolonged waiting.");
            Debug.Log("Ambient traffic: real hero held the sedan beyond the ride timeout.");

            var cameraFollow = city.Camera.GetComponent<PlayerCameraFollow>();
            cameraFollow.enabled = false;
            yield return TrafficFrame("day", 14 * 60);
            yield return TrafficFrame("night", 23 * 60);
            AdvanceRidgeClock(14 * 60);
            cameraFollow.enabled = true;

            city.Player.Motor.Teleport(crossing + Vector3.Cross(Vector3.up, direction).normalized * 4f +
                Vector3.up * PlayerFactory.GroundedRootOffset);
            Physics.SyncTransforms();
            for (int tick = 0; tick < 100; tick++)
            {
                traffic.Advance(.05f);
                if (tick % 4 == 0) yield return null;
            }
            Assert.That(car.Driver.Distance, Is.GreaterThan(waiting + 3f), "The sedan must resume when the person clears its lane.");
            Assert.That(car.Feedback.HornReach, Is.Zero, "The hand returns to the wheel once the lane clears.");

            float paused = car.Driver.Distance;
            Time.timeScale = 0f;
            traffic.AutoAdvance = true;
            for (int frame = 0; frame < 4; frame++) yield return null;
            Assert.That(car.Driver.Distance, Is.EqualTo(paused));
            Time.timeScale = 1f;

            // Verify the bus uses the new obstacle contributor, then restore the car.
            traffic.AutoAdvance = false;
            var bus = city.Bus.GetVehicle(0).Actor;
            bus.SampleAhead(10f, out var busAhead, out _);
            float saved = car.Driver.Distance;
            car.Driver.Begin(car.Plan.Path, car.Driver.Model.Profile, 0f,
                car.Plan.Length + Mathf.Repeat(car.Plan.Path.FindNearestDistance(busAhead), car.Plan.Length));
            var obstacle = CityBusObstacleState.Clear;
            traffic.AccumulateObstacle(bus, 35f, ref obstacle);
            Assert.That(obstacle.MustStop, Is.True, "The bus must see a sedan in its lane.");
            Assert.That(traffic.BlocksTruckSweep(new System.Collections.Generic.List<Vector3> { car.Hull.Centre }, 1.25f), Is.True);
            car.Driver.Begin(car.Plan.Path, car.Driver.Model.Profile, 0f, saved);
            Physics.SyncTransforms();
            city.Pedestrians.VehicleTraffic.Refresh();
            Assert.That(city.Pedestrians.VehicleTraffic.Blocks(car.Position, .35f), Is.True);
            float dormant = car.Driver.Distance;
            Assert.That(city.EnterDormant(), Is.True);
            for (int frame = 0; frame < 3; frame++) yield return null;
            Assert.That(car.Driver.Distance, Is.EqualTo(dormant));
            city.ResumeFromDormant();
            traffic.AutoAdvance = true;
            city.Player.Motor.SetInputEnabled(true);
            Debug.Log("Ambient traffic: bus/truck/walker occupancy, pause and dormancy checked; auditing every platform.");
            yield return VerifyEveryBusStopCanBeBoardedFromThePublicPavement();

            IEnumerator TrafficFrame(string name, int minute)
            {
                AdvanceRidgeClock(minute);
                Vector3 target = car.Position + Vector3.up * .9f;
                Vector3 view = -car.Driver.transform.right * 2f + car.Driver.transform.forward * 6f + Vector3.up * 1.4f;
                city.Camera.transform.SetPositionAndRotation(target + view, Quaternion.LookRotation(-view));
                for (int frame = 0; frame < 20; frame++) { traffic.Advance(.05f); yield return null; }
                if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                {
                    AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "AmbientTraffic", name, 1280, 720);
                    if (name == "night") VerifyRoadLight();
                }
            }

            void VerifyRoadLight()
            {
                var lensPose = new Pose(city.Camera.transform.position, city.Camera.transform.rotation);
                Vector3 eye = car.Position - car.Driver.transform.forward * 2f +
                    car.Driver.transform.right * 6f + Vector3.up * 5f;
                car.Plan.Path.Sample(car.Driver.Distance + car.Bounds.max.z + 4f, out var target, out _);
                city.Camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                Texture2D lit = null, dark = null;
                try
                {
                    foreach (var beam in car.Beams) Assert.That(beam.enabled, Is.True);
                    AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "AmbientTraffic", "night-road-lit", 1280, 720);
                    foreach (var beam in car.Beams) beam.enabled = false;
                    AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "AmbientTraffic", "night-road-unlit", 1280, 720);
                    lit = Read("night-road-lit"); dark = Read("night-road-unlit");
                    float brightest = 0f;
                    for (float ahead = 2f; ahead <= 7f; ahead += 1f)
                    {
                        car.Plan.Path.Sample(car.Driver.Distance + car.Bounds.max.z + ahead, out var road, out _);
                        Vector3 uv = city.Camera.WorldToViewportPoint(road + Vector3.up * .015f);
                        Assert.That(uv.z, Is.Positive);
                        int x = Mathf.Clamp(Mathf.RoundToInt(uv.x * lit.width), 4, lit.width - 5);
                        int y = Mathf.Clamp(Mathf.RoundToInt(uv.y * lit.height), 4, lit.height - 5);
                        float difference = 0f;
                        for (int dx = -4; dx <= 4; dx++)
                        for (int dy = -4; dy <= 4; dy++)
                            difference += lit.GetPixel(x + dx, y + dy).grayscale - dark.GetPixel(x + dx, y + dy).grayscale;
                        brightest = Mathf.Max(brightest, difference / 81f);
                    }
                    Assert.That(brightest, Is.GreaterThan(.04f), "The actual road must receive visible light, beyond the glowing lenses.");
                    Debug.Log("Ambient traffic: dipped headlights illuminate the physical road; A/B frames checked.");
                }
                finally
                {
                    foreach (var beam in car.Beams) beam.enabled = true;
                    city.Camera.transform.SetPositionAndRotation(lensPose.position, lensPose.rotation);
                    if (lit != null) Object.DestroyImmediate(lit);
                    if (dark != null) Object.DestroyImmediate(dark);
                }

                Texture2D Read(string shot)
                {
                    var texture = new Texture2D(2, 2);
                    texture.LoadImage(System.IO.File.ReadAllBytes(System.IO.Path.Combine(
                        System.IO.Directory.GetCurrentDirectory(), "Captures", "AmbientTraffic", shot + ".png")));
                    return texture;
                }
            }
        }
    }
}
