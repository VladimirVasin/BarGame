using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// The end-to-end proof that was missing while four separate defects each
    /// broke ambient boarding on their own: the pedestrian navigation area
    /// instead of the road-inclusive one, the hero-only "seat opposite the
    /// driver" invariant, an unreachable transfer timeout, and a waiter that
    /// stopped its own bus short of the stop. Every one of them left the
    /// planners, the occupancy rules and the asset contracts passing, because
    /// nothing walked a passenger from the pavement into a seat and back out.
    /// </summary>
    public sealed partial class CityBusNpcPassengerPlayModeTests
    {
        private const float Step = 0.05f;

        // Budgets are game seconds, never frame counts. Time.deltaTime in a
        // batch run has been observed anywhere from 0.006 s to the 6.7 s
        // ceiling depending on how fast the frames come, so a frame budget is
        // meaningless; the frame cap is only a runaway guard. One lap of the
        // test loop is roughly 37 s: 100 m at 6 m/s plus two 10 s dwells.
        private const float PreloadSeconds = 200f;
        private const float WaiterSeconds = 150f;
        private const float BoardSeconds = 90f;
        private const float AlightSeconds = 200f;
        private const int FrameCap = 40000;

        [Test]
        public void PassengerFeet_FollowDoorwayAndAisleInBothDirections()
        {
            const float terraceHeight = 12f;
            float pavementY = terraceHeight + CityStreetSurfacePlanner.RoadTop;
            var root = new GameObject("Passenger doorway foot contact");
            var mesh = new Mesh();
            try
            {
                var road = new AlwaysWalkableArea();
                CreateGround(root.transform, terraceHeight);
                var registry = CityBusResources.Instantiate(root.transform);
                var view = registry.GetComponent<CityBusPresentation>() ??
                    registry.gameObject.AddComponent<CityBusPresentation>();
                view.Initialize(registry);
                var bus = new GameObject("Bus at raised stop").AddComponent<CityBusActor>();
                bus.gameObject.layer = CityBusCollision.LayerIndex;
                bus.transform.SetParent(root.transform, false);
                bus.Initialize(registry.LocalBounds, registry.Dimensions);
                var route = CreateTwoStopRoute(terraceHeight);
                bus.PrepareSpawn(route, route.SpawnAnchors[0], 71);
                bus.BindPresentation(view);
                AdvanceActorUntil(bus, () => bus.DoorsFullyOpen);
                Physics.SyncTransforms();

                int seat = CityBusActor.NpcSeatIndices[2];
                CityBusRidePlan DoorPlan(CityBusPassengerDoor door)
                {
                    Assert.That(CityBusRidePlan.TryCreate(bus, road, Vector3.up * .835f, .35f,
                        door, null, seat, 0f, false, out var plan), Is.True);
                    return plan;
                }
                var entry = DoorPlan(CityBusPassengerDoor.Front);
                var exit = DoorPlan(CityBusPassengerDoor.Rear);
                var walker = new GameObject("Resident at stop").AddComponent<CityPedestrianActor>();
                walker.gameObject.layer = CityPedestrianCollision.LayerIndex;
                walker.transform.SetParent(root.transform, false);
                Vector3 dock = entry.EntryPose.RootPosition;
                var graph = CreatePedestrianPlan(dock, bus.TravelDirection);
                walker.Initialize(road, graph.AgentRadius);
                walker.PrepareSpawn(graph, new CityPedestrianSpawnAnchor("boarding", dock, 0, 1),
                    0, .82f, 1f, 0f, 0, 1u);
                var model = CityPedestrianDefaultNpcBody.Create(root.transform,
                    DefaultNpcPopulation.PedestrianId(52), CityPedestrianResources.LoadStreetClipDonor());
                var body = model.gameObject.AddComponent<CityPedestrianPresentation>();
                body.Initialize(model);
                model.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                walker.BindPresentation(body);
                var soles = new[] { "Sole.L", "Sole.R" }.Select(suffix => model.Renderers
                    .OfType<SkinnedMeshRenderer>().Single(s => s.enabled && s.gameObject.activeInHierarchy &&
                        s.name.EndsWith(suffix, System.StringComparison.Ordinal))).ToArray();
                Camera camera = null;
                if (System.Environment.GetEnvironmentVariable("BAR_PROMENADE_CAPTURE_BUS_TRANSFER") == "1")
                {
                    camera = new GameObject("Doorway review").AddComponent<Camera>();
                    camera.transform.SetParent(root.transform, false);
                    camera.enabled = false;
                    camera.nearClipPlane = .04f;
                    camera.fieldOfView = 40f;
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(.18f, .21f, .23f);
                    var light = camera.gameObject.AddComponent<Light>();
                    light.type = LightType.Directional;
                    light.intensity = 1.8f;
                }
                Assert.That(walker.BeginStopApproach(0, dock, bus.TravelDirection, new[] { 0f, 6f, 12f }), Is.True);
                walker.Advance(Step);
                CheckTransfer(entry, true, dock);
                Assert.That(CityBusNpcPassengerController.BeginPassengerRide(walker, bus, seat, false), Is.True);
                walker.Advance(Step);
                CheckTransfer(exit, false, exit.ExitPose.RootPosition);
                walker.ResumeRoaming(0);
                for (int i = 0; i < 4; i++)
                {
                    walker.Advance(Step);
                    CheckFeet(pavementY, "walking after alighting");
                }

                void CheckTransfer(CityBusRidePlan plan, bool boarding, Vector3 pavement)
                {
                    Assert.That(CityBusNpcPassengerController.BeginDoorTransfer(walker, bus, plan, boarding,
                        pavement, out _), Is.True);
                    bool crossedAisle = false;
                    bool captured = false;
                    for (int i = 0; i < 360 && !walker.TransferCompleted; i++)
                    {
                        walker.Advance(Step);
                        float side = Mathf.Abs(bus.transform.InverseTransformPoint(walker.Position).x);
                        // The model's cabin deck is 2.08 m wide. Once both feet have
                        // cleared the lip, their support is the deck above the road.
                        bool onDeck = side < .85f;
                        crossedAisle |= onDeck;
                        float floor = onDeck ? registry.StandingFloorAnchors[0].position.y : walker.Position.y;
                        if (camera != null && !captured && side > 1f && side < 1.15f)
                        {
                            captured = true;
                            Vector3 focus = walker.Position + Vector3.up * .8f;
                            Vector3 outward = Vector3.ProjectOnPlane(pavement - plan.DoorAnchor.position,
                                Vector3.up).normalized;
                            camera.transform.position = focus + outward * 3.8f + bus.TravelDirection * .8f + Vector3.up * .4f;
                            camera.transform.LookAt(focus);
                            AreaCaptureFixture.CaptureCurrentCamera(camera, "BusDoorTransfer", plan.PassengerDoor.ToString(), 960, 720);
                        }
                        CheckFeet(floor, $"{plan.PassengerDoor} {walker.MotionState} sample {i} side {side:F2}");
                    }
                    Assert.That(walker.TransferCompleted && crossedAisle, Is.True);
                    if (!boarding) CheckFeet(pavementY, "landed on pavement");
                }

                void CheckFeet(float floor, string phase)
                {
                    Assert.That(Mathf.Max(model.LeftFoot.position.y, model.RightFoot.position.y),
                        Is.LessThan(model.PelvisAnchor.position.y - .2f), phase + ": ankle above walking pelvis");
                    float planted = float.PositiveInfinity;
                    foreach (var sole in soles)
                    {
                        float gap = CalibratedBakedWorldVertices(sole, mesh).Min(p => p.y) - floor;
                        Assert.That(gap, Is.InRange(-.035f, .32f), $"{phase}: {sole.name} above support");
                        planted = Mathf.Min(planted, gap);
                    }
                    Assert.That(planted, Is.LessThan(.09f), $"{phase}: the supporting shoe floats");
                }
            }
            finally { Object.DestroyImmediate(mesh); Object.DestroyImmediate(root); }
        }

        [Test]
        public void Residents_UseOppositeDoorsDuringTheSameStop()
        {
            var root = new GameObject("Two-door resident service");
            CityBusNpcPassengerController passengers = null;
            CityPedestrianDirector pedestrians = null;
            CityBusDirector director = null;
            try
            {
                var road = new AlwaysWalkableArea();
                CreateGround(root.transform);
                var route = CreateTwoStopRoute();
                var registry = CityBusResources.Instantiate(root.transform);
                var view = registry.GetComponent<CityBusPresentation>() ??
                    registry.gameObject.AddComponent<CityBusPresentation>();
                view.Initialize(registry);
                var bus = new GameObject("Service bus").AddComponent<CityBusActor>();
                bus.transform.SetParent(root.transform, false);
                bus.gameObject.layer = CityBusCollision.LayerIndex;
                bus.Initialize(registry.LocalBounds, registry.Dimensions);
                var player = new GameObject("Observer").transform;
                player.SetParent(root.transform, false);
                director = root.AddComponent<CityBusDirector>();
                director.Initialize(route, bus, view, player, null, root.transform, () => 0f);
                bus.PrepareSpawn(route, route.SpawnAnchors[0], 71);
                bus.BindPresentation(view);
                AdvanceActorUntil(bus, () => bus.DoorsFullyOpen);
                Physics.SyncTransforms();

                Vector3 outward = Vector3.ProjectOnPlane(registry.FrontDoorEntryAnchor.position -
                    bus.Position, bus.TravelDirection).normalized;
                outward.y = 0f;
                outward.Normalize();
                Vector3 Dock(Transform door)
                {
                    Vector3 point = door.position + outward * 1.8f;
                    point.y = CityStreetSurfacePlanner.RoadTop;
                    return point;
                }
                Vector3 front = Dock(registry.FrontDoorEntryAnchor), rear = Dock(registry.RearDoorEntryAnchor);
                player.position = front + outward * 5f;
                var pavement = CreatePedestrianPlan(front, bus.TravelDirection, serviceCrowd: true);
                pedestrians = CityPedestrianFactory.Create(root.transform, pavement, player, road,
                    CityPedestrianPopulationProfile.City, () => false);
                Assert.That(pedestrians.Actors.Count, Is.EqualTo(3), "Two waiting walkers plus the alighting resident need street slots.");
                var world = pedestrians.Residents;
                var waits = new CityBusStopWaitPlan(new[] {
                    new CityBusStopWaitPoint(0, route.Stops[0].Id, 0, -outward,
                        new[] { front, front + outward * 1.2f, rear }, world.DistancesTo(0)),
                    new CityBusStopWaitPoint(1, route.Stops[1].Id, 2, -outward,
                        new[] { pavement.Nodes[2].Position }, world.DistancesTo(2))
                });
                var journeys = new CityResidentJourneyDirector(world, new[] { route }, new[] { waits });
                journeys.Reset(600d);
                var setup = journeys.Capture();
                for (int id = 3; id < setup.Residents.Length; id++)
                    setup.Residents[id].Phase = CityResidentJourneyPhase.AtDestination;
                for (int id = 0; id < 3; id++)
                {
                    ref var r = ref setup.Residents[id];
                    r.Phase = CityResidentJourneyPhase.Waiting;
                    r.Leg = id == 0 ? new CityResidentBusLeg(0, 1, 0) : new CityResidentBusLeg(0, 0, 1);
                    r.HasTransfer = false;
                    r.Goal = id == 0 ? 0 : 2;
                    r.ArrivesAt = 595d + id;
                }
                journeys.Restore(setup);
                Assert.That(journeys.TryBoard(0, 0, 1, CityBusActor.NpcSlotForPlace(8)), Is.True);
                pedestrians.ConfigureResidentJourneys(journeys);
                passengers = CityBusNpcPassengerController.Create(director, pedestrians, waits, road, null, player, 71);
                passengers.ConfigureResidentJourneys(journeys, 0);
                passengers.ShareWaitingPlaces(new[] { passengers });
                passengers.Advance(Step); // The rear rider starts for the open doors.
                passengers.WarmResidentWaiters();
                Assert.That(passengers.WaiterCount, Is.EqualTo(2),
                    $"Active street walkers {pedestrians.ActiveCount}, next waiting resident {journeys.NextWaiting(0, 0, true)}.");

                var crossed = new CityBusPassengerDoor?[3];
                int simultaneousBoarder = -1;
                var stranger = new object();
                for (int tick = 0; tick < 600; tick++)
                {
                    bus.Advance(Step, CityBusObstacleState.Clear, 0f);
                    foreach (var walker in pedestrians.Actors)
                        if (walker.IsSpawned && walker.IsRouteBound) walker.Advance(Step);
                    passengers.Advance(Step);
                    int active = 0, boarder = -1;
                    bool exiting = false;
                    foreach (var walker in pedestrians.Actors)
                    {
                        if (!walker.IsSpawned) continue;
                        var state = walker.MotionState;
                        if (state != CityPedestrianMotionState.Boarding && state != CityPedestrianMotionState.Alighting) continue;
                        int id = pedestrians.ResidentIndex(walker);
                        Assert.That(id, Is.InRange(0, 2));
                        active++;
                        if (state == CityPedestrianMotionState.Alighting) exiting = true;
                        else boarder = id;
                        float side = Mathf.Abs(bus.transform.InverseTransformPoint(walker.Position).x);
                        if (side < 1f || side > 1.30f) continue;
                        float frontAlong = Mathf.Abs(Vector3.Dot(walker.Position - registry.FrontDoorEntryAnchor.position, bus.TravelDirection));
                        float rearAlong = Mathf.Abs(Vector3.Dot(walker.Position - registry.RearDoorEntryAnchor.position, bus.TravelDirection));
                        Assert.That(Mathf.Min(frontAlong, rearAlong), Is.LessThan(.85f), "Passenger crossed the side outside either doorway.");
                        var door = frontAlong < rearAlong ? CityBusPassengerDoor.Front : CityBusPassengerDoor.Rear;
                        crossed[id] = door;
                        Assert.That(bus.CanUsePassengerDoor(stranger, door), Is.False, "A second passenger could claim an occupied doorway.");
                    }
                    Assert.That(active, Is.LessThanOrEqualTo(2));
                    if (exiting && boarder >= 0 && simultaneousBoarder < 0) simultaneousBoarder = boarder;
                    if (active > 0)
                    {
                        Assert.That(bus.HasServiceHold, Is.True);
                        Assert.That(bus.CurrentStopIndex, Is.EqualTo(0));
                        Assert.That(bus.DoorsFullyOpen, Is.True);
                    }
                    if (journeys.GetState(1).Phase == CityResidentJourneyPhase.Riding &&
                        journeys.GetState(2).Phase == CityResidentJourneyPhase.Riding && !bus.HasServiceHold) break;
                }
                Assert.That(simultaneousBoarder, Is.EqualTo(1), "The first queued resident boards while the earlier rider is still leaving.");
                Assert.That(crossed[0], Is.EqualTo(CityBusPassengerDoor.Rear));
                Assert.That(crossed[1], Is.EqualTo(CityBusPassengerDoor.Front));
                Assert.That(crossed[2].HasValue, Is.True, "The waiting resident eventually crossed a doorway.");
                Assert.That(journeys.GetState(0).Phase, Is.Not.EqualTo(CityResidentJourneyPhase.Riding));
                Assert.That(passengers.GetCabinBody(1), Is.Not.Null);
                Assert.That(passengers.GetCabinBody(2), Is.Not.Null);
                Assert.That(bus.NpcOccupantCount, Is.EqualTo(2));
                Assert.That(journeys.GetState(1).PlaceIndex, Is.Not.EqualTo(journeys.GetState(2).PlaceIndex));
                Assert.That(bus.HasServiceHold, Is.False);
                AdvanceActorUntil(bus, () => bus.CurrentStopIndex != 0);
                Assert.That(bus.CurrentStopIndex, Is.Not.EqualTo(0), "Bus can finish its dwell after the last crossing.");
            }
            finally
            {
                passengers?.Shutdown();
                pedestrians?.Shutdown();
                director?.Shutdown();
                Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator FullCabin_StandingRidersGripTheMovingBusAndLeaveThroughBothDoors()
        {
            var root = new GameObject("Full production midibus");
            var people = new List<CityPedestrianActor>();
            var seatedFailures = new List<string>();
            var places = CityBusActor.NpcPlaceIndices;
            var road = new AlwaysWalkableArea();
            try
            {
                var registry = CityBusResources.Instantiate(root.transform);
                var view = registry.GetComponent<CityBusPresentation>() ?? registry.gameObject.AddComponent<CityBusPresentation>();
                view.Initialize(registry);
                var bus = new GameObject("Fleet service").AddComponent<CityBusActor>();
                bus.transform.SetParent(root.transform, false);
                bus.Initialize(registry.LocalBounds, registry.Dimensions);
                var route = CreateTwoStopRoute();
                bus.PrepareSpawn(route, route.SpawnAnchors[0], 71);
                bus.BindPresentation(view);
                var driver = CityBusDriverResources.Instantiate(root.transform);
                view.AttachDriver(driver);
                AdvanceActorUntil(bus, () => bus.DoorsFullyOpen);
                var donor = CityPedestrianResources.LoadStreetClipDonor();
                var byHeight = Enumerable.Range(0, DefaultNpcPopulation.PedestrianCount)
                    .Select(DefaultNpcPopulation.PedestrianId).OrderBy(id => DefaultNpcPopulation.GetAssignment(id).HeightCm).ToArray();
                var standees = new[] { byHeight[0], byHeight[byHeight.Length / 2], byHeight[byHeight.Length - 1] };
                // Include the two short proportions that used to drive the
                // knees downward into the cushion, then cover the height range.
                var shortRiders = new[] { DefaultNpcPopulation.PedestrianId(52), DefaultNpcPopulation.PedestrianId(58) };
                var remainder = byHeight.Except(standees).Except(shortRiders).ToArray();
                int otherSeats = CityBusActor.SeatCount - 1 - shortRiders.Length;
                var identities = shortRiders.Concat(Enumerable.Range(0, otherSeats).Select(
                    i => remainder[i * (remainder.Length - 1) / (otherSeats - 1)])).Concat(standees).ToArray();
                for (int slot = 0; slot < places.Count; slot++)
                {
                    int place = places[slot];
                    var door = slot % 2 == 0 ? CityBusPassengerDoor.Front : CityBusPassengerDoor.Rear;
                    Assert.That(CityBusRidePlan.TryCreate(bus, road, Vector3.up * .835f, .35f, door,
                        null, place, 0f, false, out var boarding), Is.True, "place " + place);
                    var walker = new GameObject("Resident " + slot).AddComponent<CityPedestrianActor>();
                    walker.transform.SetParent(root.transform, false);
                    people.Add(walker);
                    Vector3 dock = boarding.EntryPose.RootPosition;
                    var graph = CreatePedestrianPlan(dock, bus.TravelDirection);
                    walker.Initialize(road, graph.AgentRadius);
                    walker.PrepareSpawn(graph, new CityPedestrianSpawnAnchor("boarding", dock, 0, 1),
                        0, .82f, 1f, 0f, 0, (uint)(slot + 1));
                    string id = identities[slot];
                    var model = CityPedestrianDefaultNpcBody.Create(root.transform, id, donor);
                    var body = model.gameObject.AddComponent<CityPedestrianPresentation>();
                    body.Initialize(model);
                    model.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    walker.BindPresentation(body);
                    Assert.That(bus.TryAttachNpcPassengerAtPlace(walker, place), Is.True);
                    if (CityBusActor.IsStandingPlace(place))
                    {
                        Assert.That(walker.BeginStopApproach(0, dock, bus.TravelDirection, new[] {0f, 6f, 12f}), Is.True);
                        walker.Advance(Step);
                        Assert.That(walker.MotionState, Is.EqualTo(CityPedestrianMotionState.WaitingAtStop));
                        Assert.That(bus.TryAcquireServiceHold(walker), Is.True);
                        Vector3 floor = registry.StandingFloorAnchors[place - CityBusActor.SeatCount].position;
                        CityBusNpcPassengerController.GetAislePath(bus, boarding.DoorAnchor.position, floor, out var aisleDoor, out var aislePlace);
                        Assert.That(walker.BeginTransfer(true, boarding.DoorAnchor.position, floor, bus.Rotation, aisleDoor, aislePlace), Is.True);
                        FinishTransfer(walker);
                        Assert.That(bus.ReleaseServiceHold(walker), Is.True);
                        Assert.That(CityBusNpcPassengerController.BeginPassengerRide(walker, bus, place, false), Is.True, id);
                    }
                    else
                    {
                        walker.transform.SetPositionAndRotation(bus.transform.TransformPoint(boarding.RideRootLocalPosition), bus.Rotation);
                        Assert.That(CityBusNpcPassengerController.BeginPassengerRide(walker, bus, place, true), Is.True, id);
                    }
                }
                var hero = new object();
                Assert.That(bus.TryAttachPassenger(hero), Is.True);
                Assert.That(bus.OccupantCount, Is.EqualTo(CityBusActor.CabinCapacity));
                Assert.That(bus.TryAttachNpcPassenger(new object(), out _), Is.False);
                if (System.Environment.GetEnvironmentVariable("BAR_PROMENADE_CAPTURE_FULL_BUS") == "1")
                    CaptureFullCabin(root, bus, registry);
                foreach (var door in new[] { CityBusPassengerDoor.Front, CityBusPassengerDoor.Rear })
                {
                    Assert.That(CityBusRidePlan.TryCreate(bus, road, Vector3.up * .835f, .32f, door, out var heroPlan), Is.True);
                    CityBusRidePlayModeTests.AssertHeroPathClearsCabin(heroPlan, registry, .32f);
                }
                for (int step = 0; step < 4; step++)
                {
                    bus.transform.SetPositionAndRotation(new Vector3(12 + step, 3, 9), Quaternion.Euler(step * 2, 80 + step * 9, step));
                    for (int slot = 0; slot < places.Count; slot++)
                    {
                        int place = places[slot];
                        Assert.That(CityBusNpcPassengerController.TryGetCabinRootPose(bus, place, out var local, out var rotation), Is.True);
                        var walker = people[slot];
                        walker.SetRidePose(bus.transform.TransformPoint(local), bus.Rotation * rotation);
                        walker.Presentation.Advance(.11f, false);
                        if (CityBusActor.IsStandingPlace(place))
                            CheckStandingContacts(walker.Presentation, registry, view.CabinUp, place - CityBusActor.SeatCount);
                        else
                        {
                            try { CheckSeatedResident(walker.Presentation, registry.PassengerSeatAnchors[place],
                                registry.FrontDoorEntryAnchor.position, view.CabinUp, bus.TravelDirection); }
                            catch (AssertionException failure) { seatedFailures.Add($"place {place} sample {step}: " + failure.Message); }
                        }
                    }
                }
                // Return to the physical stop before opening the other doors.
                AdvanceActorUntil(bus, () => bus.ServiceOrdinal > 1 && bus.DoorsFullyOpen);
                for (int slot = places.Count - 3; slot < places.Count; slot++)
                {
                    int place = places[slot];
                    var walker = people[slot];
                    var door = slot % 2 == 0 ? CityBusPassengerDoor.Rear : CityBusPassengerDoor.Front;
                    Assert.That(CityBusRidePlan.TryCreate(bus, road, Vector3.up * .835f, .35f, door, null,
                        place, 0f, false, out var exit), Is.True);
                    walker.SetRidePose(bus.transform.TransformPoint(exit.RideRootLocalPosition), bus.Rotation);
                    CityBusNpcPassengerController.GetAislePath(bus, exit.DoorAnchor.position, walker.Position, out var aisleDoor, out var aislePlace);
                    Assert.That(walker.BeginTransfer(false, aislePlace, exit.ExitPose.RootPosition, exit.ExitPose.RootRotation,
                        aisleDoor, exit.DoorAnchor.position), Is.True);
                    FinishTransfer(walker);
                    Assert.That(walker.Presentation.IsStandingInBus, Is.False);
                    Assert.That(walker.Presentation.GetComponentInChildren<NpcHandPose>().RightGripWeight, Is.Zero);
                    Assert.That(Vector3.Distance(walker.Position, exit.ExitPose.RootPosition), Is.LessThan(.01f));
                    Assert.That(bus.ReleasePassenger(walker), Is.True);
                    walker.ResumeRoaming(0);
                }
                Assert.That(bus.OccupantCount, Is.EqualTo(CityBusActor.SeatCount));
                Assert.That(seatedFailures, Is.Empty, string.Join("\n", seatedFailures));
                yield return null;
            }
            finally { Object.DestroyImmediate(root); }

            void FinishTransfer(CityPedestrianActor walker)
            {
                for (int i = 0; i < 360 && !walker.TransferCompleted; i++) walker.Advance(Step);
                Assert.That(walker.TransferCompleted, Is.True, "Complete the physical doorway and aisle path");
            }
        }

        private static void CheckSeatedResident(CityPedestrianPresentation body, Transform seat,
            Vector3 floorPoint, Vector3 up, Vector3 forward)
        {
            forward = Vector3.ProjectOnPlane(forward, up).normalized;
            Vector3 right = Vector3.Cross(up, forward);
            var pelvis = body.Registry.PelvisAnchor;
            string id = body.Registry.GetComponent<CityPedestrianDefaultNpcBody>().CharacterId;
            var fingers = body.GetComponentInChildren<NpcHandPose>();
            var mesh = new Mesh();
            try
            {
                Vector3[] cloth = null;
                SkinnedMeshRenderer wornTrousers = null;
                int[] clothTriangles = null;
                float[] thighWeight = null;
                float gap = float.PositiveInfinity, buttockGap = float.PositiveInfinity;
                float pelvisDepth = Vector3.Dot(pelvis.position - seat.position, forward);
                foreach (var render in SeatedSupportRenderers(body.Registry.Renderers))
                {
                    if (!(render is SkinnedMeshRenderer skin)) continue;
                    var support = SeatedSupportWeights(skin);
                    Vector3[] vertices = CalibratedBakedWorldVertices(skin, mesh);
                    int[] triangles = mesh.triangles;
                    if (support != null) { cloth = vertices; clothTriangles = triangles; thighWeight = support; wornTrousers = skin; }

                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        var polygon = new List<Vector3>(3);
                        var weights = new float[3];
                        for (int v = 0; v < 3; v++)
                        {
                            int index = triangles[i + v];
                            Vector3 world = vertices[index] - seat.position;
                            polygon.Add(new Vector3(Vector3.Dot(world, right), Vector3.Dot(world, up), Vector3.Dot(world, forward)));
                            weights[v] = support == null ? 1f : support[index];
                        }
                        polygon = ClipSeatedSupportWeights(polygon, weights);
                        polygon = ClipSeatPolygon(polygon, 0, -.21f, true);
                        polygon = ClipSeatPolygon(polygon, 0, .21f, false);
                        polygon = ClipSeatPolygon(polygon, 2, -.18f, true);
                        polygon = ClipSeatPolygon(polygon, 2, .18f, false);
                        foreach (Vector3 point in polygon) gap = Mathf.Min(gap, point.y);
                        polygon = ClipSeatPolygon(polygon, 2, pelvisDepth + .065f, false);
                        foreach (Vector3 point in polygon) buttockGap = Mathf.Min(buttockGap, point.y);
                    }
                }
                if (gap < -.013f || gap > .018f)
                {
                    var actual = new CitySeatedCushionContact(body.Registry);
                    actual.Bind();
                    Assert.Fail($"{id} {seat.name}: actual hip/thigh clearance {gap:F5} m; target -.013..+.018 m, " +
                        $"weighted residual {actual.MeasureCorrection(seat.position, up, forward, .21f, .18f):F5} m.");
                }
                Assert.That(buttockGap, Is.InRange(-.013f, .027f), $"{id} worn buttocks at the rearward support patch");
                for (int side = 0; side < 2; side++)
                {
                    bool left = side == 0;
                    Transform ankle = left ? body.Registry.LeftFoot : body.Registry.RightFoot;
                    Transform knee = ankle.parent, hip = knee.parent;
                    var wrist = CityPedestrianHandProps.FindSocket(body.Registry.ModelRoot, left ? "hand.L" : "hand.R");
                    Vector3 palm = wrist.position + wrist.rotation * fingers.GetAnatomicalFrame(left).PalmCentreInHand;
                    Assert.That(Vector3.Dot(knee.position - pelvis.position, forward), Is.GreaterThan(.20f),
                        $"{id} knee in front of cushion; thigh/shin {Vector3.Distance(hip.position, knee.position):F3}/" +
                        $"{Vector3.Distance(knee.position, ankle.position):F3}, hip/knee/ankle above floor " +
                        $"{Vector3.Dot(hip.position - floorPoint, up):F3}/{Vector3.Dot(knee.position - floorPoint, up):F3}/" +
                        $"{Vector3.Dot(ankle.position - floorPoint, up):F3}");
                    Assert.That(Vector3.Dot(wrist.position - pelvis.position, forward), Is.GreaterThan(.055f), id + " wrist in front of backrest");
                    Assert.That(Vector3.Dot(wrist.parent.position - seat.position, forward), Is.GreaterThan(-.17f), id + " elbow clear of backrest");
                    var frame = fingers.GetAnatomicalFrame(left);
                    Assert.That(Vector3.Dot(wrist.rotation * frame.PalmNormalInHand, -up), Is.GreaterThan(.55f), id + " inward palm faces cloth");
                    string suffix = left ? "Palm.L" : "Palm.R";
                    var skin = body.Registry.Renderers.OfType<SkinnedMeshRenderer>().First(r => r.enabled &&
                        r.gameObject.activeInHierarchy && r.name.EndsWith(suffix, System.StringComparison.Ordinal));
                    float palmGap = float.PositiveInfinity;
                    foreach (Vector3 handVertex in CalibratedBakedWorldVertices(skin, mesh))
                    {
                        // Intersect a downward ray through the real hand mesh
                        // against the upper surface of the deformed trousers.
                        Vector3 origin = handVertex + up * .35f;
                        Ray ray = new Ray(origin, -up);
                        for (int face = 0; face < clothTriangles.Length; face += 3)
                        {
                            int ia = clothTriangles[face], ib = clothTriangles[face + 1], ic = clothTriangles[face + 2];
                            if (thighWeight[ia] < .5f && thighWeight[ib] < .5f && thighWeight[ic] < .5f) continue;
                            Vector3 a = cloth[ia], b = cloth[ib], c = cloth[ic];
                            Plane plane = new Plane(a, b, c);
                            if (!plane.Raycast(ray, out float travel)) continue;
                            Vector3 hit = ray.GetPoint(travel);
                            if (Vector3.Dot(Vector3.Cross(b - a, hit - a), plane.normal) < -.000001f ||
                                Vector3.Dot(Vector3.Cross(c - b, hit - b), plane.normal) < -.000001f ||
                                Vector3.Dot(Vector3.Cross(a - c, hit - c), plane.normal) < -.000001f) continue;
                            palmGap = Mathf.Min(palmGap, travel - .35f);
                        }
                    }
                    Vector3 thighPlan = Vector3.ProjectOnPlane(knee.position - hip.position, up);
                    // The separate mesh test measures vertical hand contact.
                    // In plan view the palm has to lie over the thigh between
                    // hip and knee, even where that thigh slopes steeply down.
                    float along = Vector3.Dot(Vector3.ProjectOnPlane(palm - hip.position, up), thighPlan) / thighPlan.sqrMagnitude;
                    Assert.That(along, Is.InRange(.20f, .95f), id + " palm rests along its own thigh");
                    int thigh = System.Array.IndexOf(wornTrousers.bones, hip);
                    BoneWeight[] skinWeights = wornTrousers.sharedMesh.boneWeights;
                    float hitHeight = float.NegativeInfinity, contactWeight = 0f;
                    for (int j = 0; j < clothTriangles.Length; j += 3)
                    {
                        int a = clothTriangles[j], b = clothTriangles[j + 1], c = clothTriangles[j + 2];
                        Vector3 ab = cloth[b] - cloth[a], ac = cloth[c] - cloth[a];
                        float det = Vector3.Dot(Vector3.Cross(ab, ac), up);
                        if (Mathf.Abs(det) < .000001f) continue;
                        Vector3 ap = palm - cloth[a];
                        float u = Vector3.Dot(Vector3.Cross(ap, ac), up) / det;
                        float v = Vector3.Dot(Vector3.Cross(ab, ap), up) / det;
                        if (u < 0f || v < 0f || u + v > 1f) continue;
                        float h = Vector3.Dot(cloth[a] + ab * u + ac * v, up);
                        if (h <= hitHeight) continue;
                        hitHeight = h;
                        contactWeight = (1f - u - v) * Weight(a) + u * Weight(b) + v * Weight(c);
                    }
                    Assert.That(contactWeight, Is.GreaterThan(.45f), $"{id} palm over the corresponding thigh mesh ({suffix}, at {along:F2})");
                    Assert.That(palmGap, Is.InRange(-.014f, .018f), id + " worn " + suffix + " to actual trouser surface");
                    CheckSoleGap(body, mesh, left ? "Sole.L" : "Sole.R", floorPoint, up);
                    float Weight(int vertex)
                    {
                        var w = skinWeights[vertex];
                        return (w.boneIndex0 == thigh ? w.weight0 : 0f) + (w.boneIndex1 == thigh ? w.weight1 : 0f) +
                            (w.boneIndex2 == thigh ? w.weight2 : 0f) + (w.boneIndex3 == thigh ? w.weight3 : 0f);
                    }
                }
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        private static void CheckSoleGap(CityPedestrianPresentation body, Mesh mesh, string suffix,
            Vector3 floor, Vector3 up)
        {
            float gap = float.PositiveInfinity;
            foreach (var skin in body.Registry.Renderers.OfType<SkinnedMeshRenderer>())
            {
                if (!skin.enabled || !skin.gameObject.activeInHierarchy ||
                    !skin.name.EndsWith(suffix, System.StringComparison.Ordinal)) continue;
                foreach (Vector3 vertex in CalibratedBakedWorldVertices(skin, mesh))
                    gap = Mathf.Min(gap, Vector3.Dot(vertex - floor, up));
            }
            Assert.That(gap, Is.InRange(-.018f, .024f), body.Registry.DesignId + " " + suffix + " floor gap");
        }

        // Compare the native deformation to the full set of weighted world bind
        // matrices, including body and joint morphs. This checks the scale
        // convention on the actual resident's renderer rather than assuming a
        // uniform scale of one. The resulting external measurement uses BakeMesh.
        private static readonly HashSet<int> ReportedBakeModes = new HashSet<int>();

        private static Vector3[] CalibratedBakedWorldVertices(SkinnedMeshRenderer skin, Mesh baked)
        {
            Vector3[] reference = CityPedestrianShoeContacts.SkinWorldVertices(skin);
            var candidates = new Vector3[2][];
            var errors = new float[4];
            for (int flag = 0; flag <= 1; flag++)
            {
                skin.BakeMesh(baked, flag != 0);
                Vector3[] vertices = candidates[flag] = baked.vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    var rigid = skin.transform.position + skin.transform.rotation * vertices[i];
                    var full = skin.transform.TransformPoint(vertices[i]);
                    errors[flag * 2] = Mathf.Max(errors[flag * 2], (rigid - reference[i]).magnitude);
                    errors[flag * 2 + 1] = Mathf.Max(errors[flag * 2 + 1], (full - reference[i]).magnitude);
                }
            }
            int best = System.Array.IndexOf(errors, errors.Min());
            if (ReportedBakeModes.Add(best))
                Debug.Log($"Passenger mesh {skin.name} scale {skin.transform.lossyScale:F3}, BakeMesh world mode {best}; " +
                    $"maximum vertex error (false-rigid/false-full/true-rigid/true-full) {string.Join("/", errors.Select(e => e.ToString("F5")))} m");
            Assert.That(errors[best], Is.LessThan(.003f), skin.name + " native baked mesh against world-space skin on the live bones");
            Vector3[] selected = candidates[best / 2];
            for (int i = 0; i < selected.Length; i++)
                selected[i] = (best & 1) != 0 ? skin.transform.TransformPoint(selected[i]) :
                    skin.transform.position + skin.transform.rotation * selected[i];
            return selected;
        }

        private static void CheckStandingContacts(CityPedestrianPresentation body, CityBusAssetRegistry bus, Vector3 up, int bay)
        {
            var hands = body.Registry.GetComponentInChildren<NpcHandPose>();
            Assert.That(hands.RightGripWeight, Is.EqualTo(1f));
            Assert.That(Vector3.Distance(hands.CylinderCentre(false), bus.StandingGripAnchors[bay].position),
                Is.LessThan(.026f), body.Registry.DesignId + " palm at rail in bay " + bay);
            var sole = new Mesh();
            try
            {
                foreach (string suffix in new[] {"Sole.L", "Sole.R"})
                    CheckSoleGap(body, sole, suffix, bus.StandingFloorAnchors[bay].position, up);
            }
            finally { Object.DestroyImmediate(sole); }
        }

        private static void CaptureFullCabin(GameObject root, CityBusActor bus, CityBusAssetRegistry registry)
        {
            var camera = new GameObject("Full cabin section").AddComponent<Camera>();
            camera.transform.SetParent(root.transform, false);
            camera.enabled = false;
            camera.nearClipPlane = .025f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.18f, .21f, .23f);
            var lamp = new GameObject("Section light").AddComponent<Light>();
            lamp.transform.SetParent(root.transform, false);
            lamp.type = LightType.Directional; lamp.intensity = 2.0f;
            lamp.transform.rotation = bus.Rotation * Quaternion.Euler(38, 45, 0);
            Vector3 focus = bus.Position + bus.transform.up * 1.35f;
            camera.transform.position = focus + bus.Rotation * new Vector3(-9f, 2.7f, -1.4f);
            camera.transform.LookAt(focus, bus.transform.up);
            camera.orthographic = true; camera.orthographicSize = 2.7f;
            camera.aspect = 16f / 9f;
            SetSeatedCaptureLayer(root);
            camera.cullingMask = 1 << 31;
            var keptParts = new HashSet<string> { "INT_Handrails", "INT_PassengerSeats", "INT_RearBench", "INT_SeatFrames", "INT_DriverSeat", "INT_SteeringWheel" };
            var visible = new HashSet<Renderer>(registry.RendererBindings.Where(part => keptParts.Contains(part.SourceName))
                .Select(part => part.Renderer));
            visible.UnionWith(bus.Presentation.DriverPresentation.Registry.Renderers);
            foreach (var rider in registry.GetComponentsInChildren<CityPedestrianPresentation>(true))
                visible.UnionWith(rider.Registry.Renderers);
            // Runtime glass overlays and number panels are outside the imported registry.
            // Exclude those too, including from the inside and rear-bench cameras.
            var hidden = registry.GetComponentsInChildren<Renderer>(true)
                .Where(part => part.enabled && !visible.Contains(part)).ToArray();
            var target = new RenderTexture(1440, 810, 24);
            var pixels = new Texture2D(1440, 810, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                foreach (var part in hidden) part.enabled = false;
                string output = Path.GetFullPath("Captures/BusFullCabin");
                Directory.CreateDirectory(output);
                camera.targetTexture = target;
                Save("standing-section");
                camera.transform.position = focus + bus.Rotation * new Vector3(8.5f, 2.3f, 3.9f);
                camera.transform.LookAt(focus, bus.transform.up);
                Save("opposite-section");

                Vector3 floor = registry.StandingFloorAnchors[0].position;
                Vector3 grip = registry.StandingGripAnchors[0].position;
                Vector3 eye = Vector3.Lerp(floor, grip, .62f);
                camera.orthographicSize = 1.05f;
                camera.transform.position = eye + bus.Rotation * new Vector3(.86f, .19f, 1.16f);
                camera.transform.LookAt(eye, bus.transform.up);
                Save("right-hand-standing");

                camera.orthographic = false;
                camera.fieldOfView = 65f;
                camera.transform.position = registry.PassengerSeatAnchors[CityBusActor.PlayerSeatIndex].position +
                    bus.transform.up * .75f + bus.transform.right * .05f;
                camera.transform.LookAt(registry.StandingGripAnchors[1].position - bus.transform.up * .25f, bus.transform.up);
                Save("from-seat-07");

                // Eye inside the aisle, aimed past both pairs of shoes at the bench.
                // Level the view to the cabin so the rig contacts read even on a grade.
                Vector3 bench = (registry.PassengerSeatAnchors[5].position + registry.PassengerSeatAnchors[9].position) * .5f;
                Vector3 up = bus.Presentation.CabinUp;
                Vector3 forward = Vector3.ProjectOnPlane(bus.TravelDirection, up).normalized;
                Vector3 right = Vector3.Cross(up, forward);
                camera.transform.position = bench + forward * 1.48f + up * .52f + right * .05f;
                camera.transform.LookAt(bench + up * .25f, up);
                camera.fieldOfView = 64f;
                var benchFill = new GameObject("Rear cabin review fill").AddComponent<Light>();
                benchFill.transform.SetParent(root.transform, false);
                benchFill.transform.position = camera.transform.position + up * .23f;
                benchFill.transform.LookAt(bench + up * .15f, up);
                benchFill.type = LightType.Spot;
                benchFill.spotAngle = 96f;
                benchFill.innerSpotAngle = 65f;
                benchFill.range = 3.2f;
                benchFill.intensity = 2.2f;
                benchFill.shadows = LightShadows.None;
                benchFill.cullingMask = camera.cullingMask;
                Save("rear-bench-from-aisle");
                benchFill.enabled = false;

                void Save(string name)
                {
                    camera.Render(); RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 1440, 810), 0, 0); pixels.Apply();
                    File.WriteAllBytes(Path.Combine(output, name + ".png"), pixels.EncodeToPNG());
                }
            }
            finally
            {
                foreach (var part in hidden) if (part != null) part.enabled = true;
                camera.targetTexture = null; RenderTexture.active = previous;
                Object.DestroyImmediate(pixels); Object.DestroyImmediate(target);
            }
        }

        [UnityTest]
        public IEnumerator SeatedPassengers_StayOnActualCushionsAcrossAnimationAndBusMotion()
        {
            // No city, boarding loop or directors: exercise the production
            // presentation and the actual passenger update order in isolation.
            var root = new GameObject("Bus seated mesh regression");
            var report = new SeatedMeshReport();
            var failures = new List<string>();
            string directory = Path.GetFullPath("Captures/BusNpcSeating");
            Directory.CreateDirectory(directory);
            Color ambient = RenderSettings.ambientLight;
            UnityEngine.Rendering.AmbientMode ambientMode = RenderSettings.ambientMode;
            bool fog = RenderSettings.fog;
            try
            {
                RenderSettings.fog = false;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.70f, .70f, .70f);
                Camera camera = new GameObject("Seated mesh camera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform, false);
                camera.enabled = false;
                camera.nearClipPlane = .025f;
                camera.farClipPlane = 40f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.16f, .18f, .20f);
                camera.cullingMask = 1 << 31;
                Light light = new GameObject("Seated mesh light").AddComponent<Light>();
                light.transform.SetParent(root.transform, false);
                light.type = LightType.Directional;
                light.intensity = 1.5f;
                light.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

                CityBusAssetRegistry bus = CityBusResources.Instantiate(root.transform);
                Assert.That(bus, Is.Not.Null);
                CityBusPresentation busPresentation = bus.GetComponent<CityBusPresentation>() ??
                    bus.gameObject.AddComponent<CityBusPresentation>();
                busPresentation.Initialize(bus);
                CityBusActor busActor = new GameObject("Seated mesh bus actor").AddComponent<CityBusActor>();
                busActor.transform.SetParent(root.transform, false);
                busActor.Initialize(bus.LocalBounds, bus.Dimensions);
                CityBusPlan route = CreateTwoStopRoute();
                busActor.PrepareSpawn(route, route.SpawnAnchors[0], 0x53454154u);
                busActor.BindPresentation(busPresentation);
                busActor.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Quaternion suspensionInBus = Quaternion.Inverse(busActor.transform.rotation) *
                    busPresentation.SuspensionVisual.rotation;
                CityBusDriverAssetRegistry driver = CityBusDriverResources.Instantiate(root.transform);
                Assert.That(driver, Is.Not.Null);
                busPresentation.AttachDriver(driver);
                SetSeatedCaptureLayer(root);
                Renderer passengerCushions = bus.RendererBindings.Single(
                    binding => binding.SourceName == "INT_PassengerSeats").Renderer;
                Renderer driverCushion = bus.RendererBindings.Single(
                    binding => binding.SourceName == "INT_DriverSeat").Renderer;
                Assert.That(CityBusRidePlan.TryCreateSeatedPose(busActor,
                    CityBusActor.NpcSeatIndices[0], out Vector3 ridePosition,
                    out Quaternion rideRotation, out Transform seat), Is.True);
                var passengerFrame = new SeatedCushionFrame(seat, busActor.transform.rotation);
                var driverFrame = new SeatedCushionFrame(bus.DriverSeatAnchor, busActor.transform.rotation);

                foreach (CityPedestrianArchetype archetype in CityPedestrianResources.AllArchetypes)
                {
                    if (!archetype.CanRideBus) continue;
                    busActor.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    busPresentation.SuspensionVisual.rotation = busActor.transform.rotation * suspensionInBus;
                    var actorObject = new GameObject(archetype.DesignId + " seated mesh actor");
                    actorObject.layer = CityPedestrianCollision.LayerIndex;
                    actorObject.transform.SetParent(root.transform, false);
                    CityPedestrianActor actor = actorObject.AddComponent<CityPedestrianActor>();
                    CityPedestrianPlan plan = CreatePedestrianPlan(Vector3.zero, Vector3.forward);
                    actor.Initialize(new AlwaysWalkableArea(), plan.AgentRadius);
                    CityPedestrianAssetRegistry npc;
                    bool defaultBody = DefaultNpcCatalog.ModelIds.Contains(archetype.DesignId);
                    if (defaultBody)
                    {
                        string identity = Enumerable.Range(0, DefaultNpcPopulation.PedestrianCount)
                            .Select(DefaultNpcPopulation.PedestrianId).First(id =>
                                DefaultNpcPopulation.GetAssignment(id).ModelId == archetype.DesignId);
                        npc = CityPedestrianDefaultNpcBody.Create(root.transform, identity,
                            CityPedestrianResources.LoadStreetClipDonor());
                    }
                    else
                        Assert.That(CityPedestrianResources.TryInstantiate(
                            Resources.Load<GameObject>(archetype.PrefabResourcePath), root.transform,
                            out npc), Is.True, archetype.DesignId);
                    CityPedestrianPresentation presentation = npc.GetComponent<CityPedestrianPresentation>() ??
                        npc.gameObject.AddComponent<CityPedestrianPresentation>();
                    presentation.Initialize(npc);
                    npc.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    actor.PrepareSpawn(plan, plan.SpawnAnchors[0], 1, 1f, 1f, 0f, 0, 1u);
                    actor.BindPresentation(presentation);
                    Quaternion standingModelRotation = npc.ModelRoot.localRotation;
                    actor.transform.SetPositionAndRotation(
                        busActor.transform.TransformPoint(ridePosition), busActor.transform.rotation * rideRotation);
                    SetSeatedCaptureLayer(actorObject);
                    NpcWardrobe wardrobe = defaultBody ? npc.GetComponentInChildren<NpcWardrobe>() : null;
                    string[] trouserVariants = defaultBody ? new[] { "everyday", "work", "warm" } : new[] { "" };
                    foreach (string trouserVariant in trouserVariants)
                    {
                        // All three authored cuts must fit the same seat; this
                        // local swap does not alter the population assignment.
                        if (defaultBody) wardrobe.SetSlot("trousers", "trousers." + trouserVariant);
                        string phasePrefix = defaultBody ? trouserVariant + "/" : "";
                        string captureName = archetype.DesignId + (defaultBody ? "-" + trouserVariant : "");
                        busActor.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                        busPresentation.SuspensionVisual.rotation = busActor.transform.rotation * suspensionInBus;
                        actor.transform.SetPositionAndRotation(busActor.transform.TransformPoint(ridePosition),
                            busActor.transform.rotation * rideRotation);
                        Renderer[] occupant = npc.GetComponentsInChildren<Renderer>(true);
                        Renderer[] support = SeatedSupportRenderers(occupant);
                        Assert.That(support.Length, Is.EqualTo(defaultBody ? 1 : 3),
                            archetype.DesignId + " visible pelvis and upper thighs");
                        Assert.That(Vector3.Distance(busPresentation.CabinUp, passengerFrame.Up), Is.LessThan(.0001f));
                        Assert.That(actor.BeginSeatedRide(seat, archetype.SeatedRide, busPresentation.CabinUp,
                            bus.FrontDoorEntryAnchor.position, busActor.TravelDirection), Is.True);

                        // Must already be seated before either the next tick or a
                        // frame yield. A standing graph can have a correct pelvis
                        // anchor while its visible thighs still cut the cushion.
                        MeasurePassengerSeat(archetype, npc, actor, passengerFrame, passengerCushions,
                            support, phasePrefix + "initial", report, failures);
                        yield return null;
                        // This isolated actor has no director to sample its
                        // manual graph each frame, unlike a production rider.
                        // Reapply after Animator's first-frame initialization.
                        actor.Advance(0f);
                        MeasurePassengerSeat(archetype, npc, actor, passengerFrame, passengerCushions,
                            support, phasePrefix + "rendered", report, failures);
                        CaptureSeatedMesh(camera, bus, passengerFrame, npc.PelvisAnchor,
                            occupant, passengerCushions, directory, captureName + "-level");

                        const int samples = 16;
                        for (int phase = 1; phase <= samples; phase++)
                        {
                            actor.Advance(npc.SitClip.length / samples);
                            // Production order: pedestrians tick, bus advances,
                            // passenger controller updates the independent root.
                            // Include sprung body tilt as well as road/root tilt.
                            float angle = phase * Mathf.PI * 2f / samples;
                            busActor.transform.SetPositionAndRotation(
                                new Vector3(phase * .31f, .12f * Mathf.Sin(angle), -phase * .17f),
                                Quaternion.Euler(2f * Mathf.Sin(angle), phase * 3f, -2f * Mathf.Cos(angle)));
                            busPresentation.SuspensionVisual.rotation = busActor.transform.rotation * Quaternion.Euler(
                                CityBusPresentation.MaximumSuspensionPitch * Mathf.Cos(angle), 0f,
                                CityBusPresentation.MaximumSuspensionRoll * Mathf.Sin(angle)) * suspensionInBus;
                            actor.SetRidePose(busActor.transform.TransformPoint(ridePosition),
                                busActor.transform.rotation * rideRotation);
                            Assert.That(Vector3.Distance(busPresentation.CabinUp, passengerFrame.Up), Is.LessThan(.0001f), "Production cabin normal follows the drawn cushion.");
                            MeasurePassengerSeat(archetype, npc, actor, passengerFrame, passengerCushions,
                                support, phasePrefix + "sit-" + phase, report, failures);
                        }

                        yield return null; // Refresh GPU skinning for the tilted view.
                        CaptureSeatedMesh(camera, bus, passengerFrame, npc.PelvisAnchor,
                            occupant, passengerCushions, directory, captureName + "-tilted");
                        actor.ResumeRoaming(1);
                        actor.Advance(0f);
                        if (presentation.IsSeated ||
                            Quaternion.Angle(npc.ModelRoot.localRotation, standingModelRotation) > .01f)
                            failures.Add(archetype.DesignId + ": standing retained the seat's model tilt.");
                        File.WriteAllText(Path.Combine(directory, "report.json"), JsonUtility.ToJson(report, true));
                    }
                    Object.DestroyImmediate(actorObject);
                }

                Renderer[] driverSupport = SeatedSupportRenderers(driver.Renderers);
                Assert.That(driverSupport.Length, Is.EqualTo(3));
                for (int sample = 0; sample < 2; sample++)
                {
                    busActor.transform.SetPositionAndRotation(new Vector3(8f, 1f, -5f),
                        Quaternion.Euler(sample * 3f, 38f, sample * -3f));
                    busPresentation.SuspensionVisual.rotation = busActor.transform.rotation *
                        Quaternion.Euler(sample * .8f, 0f, sample) * suspensionInBus;
                    busPresentation.DriverPresentation.ApplyPose(sample, sample, 0f);
                    MeasureSeatedSurface("bus-driver", sample == 0 ? "wheel" : "button-tilted",
                        driverFrame, driverCushion, driverSupport, report, failures);
                    yield return null;
                    CaptureSeatedMesh(camera, bus, driverFrame, driver.Pelvis,
                        driver.Renderers, driverCushion, directory,
                        sample == 0 ? "driver-wheel" : "driver-button-tilted");
                }
            }
            finally
            {
                File.WriteAllText(Path.Combine(directory, "report.json"), JsonUtility.ToJson(report, true));
                RenderSettings.ambientLight = ambient;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.fog = fog;
                Object.DestroyImmediate(root);
            }

            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        private static void MeasurePassengerSeat(CityPedestrianArchetype archetype,
            CityPedestrianAssetRegistry npc, CityPedestrianActor actor, SeatedCushionFrame frame,
            Renderer cushion, Renderer[] support, string phase, SeatedMeshReport report,
            List<string> failures)
        {
            var defaultBody = npc.GetComponent<CityPedestrianDefaultNpcBody>();
            float error;
            if (defaultBody != null)
            {
                // Stature sets the fore/aft fit. Keep the pelvis on that
                // exact live contact as both the rig and cushion move.
                Vector3 position = frame.Coordinates(npc.PelvisAnchor.position);
                error = new Vector2(position.x, position.z - actor.Presentation.SeatedPelvisForward).magnitude;
            }
            else
            {
                Vector3 expected = frame.Seat.position + frame.Up * archetype.SeatedRide.SeatLift -
                    Vector3.ProjectOnPlane(actor.transform.forward, frame.Up).normalized * archetype.SeatedRide.SeatBackOffset;
                error = Vector3.Distance(npc.PelvisAnchor.position, expected);
            }
            if (error > .001f)
                failures.Add($"{archetype.DesignId}/{phase}: pelvis missed live seat by {error:F4} m.");
            MeasureSeatedSurface(archetype.DesignId, phase, frame, cushion, support, report, failures);
        }

        private static Renderer[] SeatedSupportRenderers(IReadOnlyList<Renderer> renderers)
        {
            return renderers.Where(renderer => renderer != null && renderer.enabled &&
                (renderer.name == "GEO_Pelvis" || renderer.name == "CLO_Seat" ||
                 renderer.name == "GEO_Thigh.L" || renderer.name == "GEO_Thigh.R" ||
                 renderer.name == "CLO_Thigh.L" || renderer.name == "CLO_Thigh.R" ||
                 renderer.name.StartsWith("CLO_Trousers_", System.StringComparison.Ordinal) &&
                 renderer.name.EndsWith("Upper", System.StringComparison.Ordinal))).ToArray();
        }

        private static void SetSeatedCaptureLayer(GameObject root)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.gameObject.layer = 31;
                if (renderer is SkinnedMeshRenderer skin)
                {
                    skin.updateWhenOffscreen = true;
                    // The fixture samples a manual graph before Camera.Render;
                    // its first image must not reuse the previous bone matrices.
                    skin.forceMatrixRecalculationPerRender = true;
                }
            }
        }

        private static void MeasureSeatedSurface(string design, string phase, SeatedCushionFrame frame,
            Renderer cushion, Renderer[] support, SeatedMeshReport report, List<string> failures)
        {
            var baked = new Mesh();
            try
            {
                Mesh seatMesh = SeatedMesh(cushion, baked);
                ReadSeatedMesh(seatMesh, out Vector3[] cushionVertices, out _);
                Vector3[] seatVertices = cushionVertices.Select(vertex =>
                    frame.Coordinates(cushion.transform.TransformPoint(vertex))).ToArray();
                // Find the actual top-face corners near this seat, excluding
                // the combined mesh's backrest and all neighboring cushions.
                Vector3[] top = seatVertices.Where(vertex => Mathf.Abs(vertex.y) < .01f &&
                    Mathf.Abs(vertex.x) < .36f && Mathf.Abs(vertex.z) < .36f).ToArray();
                Assert.That(top.Length, Is.GreaterThanOrEqualTo(4),
                    $"{design} actual cushion top; anchor={frame.Seat.position:F4}, normal={frame.Up:F4}, " +
                    $"meshScale={cushion.transform.lossyScale:F4}, bounds={cushion.bounds}; nearest seat-local vertices=" +
                    string.Join(", ", seatVertices.OrderBy(vertex => vertex.sqrMagnitude)
                        .Take(6).Select(vertex => vertex.ToString("F4"))));
                float surface = top.Average(vertex => vertex.y);
                Rect footprint = Rect.MinMaxRect(top.Min(vertex => vertex.x), top.Min(vertex => vertex.z),
                    top.Max(vertex => vertex.x), top.Max(vertex => vertex.z));
                Assert.That(footprint.width, Is.InRange(.48f, .59f));
                Assert.That(footprint.height, Is.InRange(.44f, .54f));
                float minimum = float.PositiveInfinity;
                string contactRenderer = null;
                Vector3 contactPoint = Vector3.zero;
                int clippedTriangles = 0;
                foreach (Renderer renderer in support)
                {
                    Mesh mesh = SeatedMesh(renderer, baked);
                    ReadSeatedMesh(mesh, out Vector3[] meshVertices, out int[] triangles);
                    Vector3[] vertices = meshVertices.Select(vertex =>
                        frame.Coordinates(renderer.transform.TransformPoint(vertex))).ToArray();
                    float[] supportWeights = SeatedSupportWeights(renderer);
                    for (int index = 0; index < triangles.Length; index += 3)
                    {
                        var polygon = new List<Vector3>
                        {
                            vertices[triangles[index]], vertices[triangles[index + 1]], vertices[triangles[index + 2]]
                        };
                        if (supportWeights != null)
                            polygon = ClipSeatedSupportWeights(polygon, new[]
                            {
                                supportWeights[triangles[index]], supportWeights[triangles[index + 1]],
                                supportWeights[triangles[index + 2]]
                            });
                        // Clip the surface, not just its vertices: a large
                        // triangle may cross a cushion with all corners outside.
                        // The weight clip above separates the thigh from the
                        // shin inside joined trousers; footprint alone cannot
                        // distinguish a calf below the seat from a seated hip.
                        polygon = ClipSeatPolygon(polygon, 0, footprint.xMin + .002f, true);
                        polygon = ClipSeatPolygon(polygon, 0, footprint.xMax - .002f, false);
                        polygon = ClipSeatPolygon(polygon, 2, footprint.yMin + .002f, true);
                        polygon = ClipSeatPolygon(polygon, 2, footprint.yMax - .002f, false);
                        if (polygon.Count < 3) continue;
                        clippedTriangles++;
                        foreach (Vector3 point in polygon)
                        {
                            if (point.y - surface >= minimum) continue;
                            minimum = point.y - surface;
                            contactPoint = point;
                            contactRenderer = renderer.name;
                        }
                    }
                }

                report.samples.Add(new SeatedMeshSample
                {
                    design = design, phase = phase, minimum_clearance_m = minimum,
                    support_triangles = clippedTriangles, cushion_width_m = footprint.width,
                    cushion_depth_m = footprint.height, seat = frame.Seat.position, seat_up = frame.Up,
                    contact_renderer = contactRenderer, contact_in_seat = contactPoint
                });
                if (clippedTriangles == 0 || float.IsInfinity(minimum))
                    failures.Add(design + "/" + phase + ": no hip/thigh surface overlaps the cushion.");
                else if (minimum < -.012f || minimum > .03f)
                    failures.Add($"{design}/{phase}: visible support clearance {minimum:F4} m at " +
                        $"{contactRenderer} {contactPoint:F4}; expected -0.012 to 0.030 m.");
            }
            finally { Object.DestroyImmediate(baked); }
        }

        private static float[] SeatedSupportWeights(Renderer renderer)
        {
            if (!renderer.name.StartsWith("CLO_Trousers_", System.StringComparison.Ordinal)) return null;
            var skin = (SkinnedMeshRenderer)renderer;
            // Modular Upper includes the upper calves as well as the hips and
            // thighs. Only the predominantly pelvis/thigh surface supports a
            // sitter. Retain the upper half of the blended knee transition.
            bool[] supportingBones = skin.bones.Select(bone => bone.name == "pelvis" ||
                bone.name == "thigh.L" || bone.name == "thigh.R").ToArray();
            Assert.That(supportingBones.Count(value => value), Is.EqualTo(3), renderer.name);
            BoneWeight[] weights = skin.sharedMesh.boneWeights;
            Assert.That(weights.Length, Is.EqualTo(skin.sharedMesh.vertexCount));
            return weights.Select(weight =>
                (supportingBones[weight.boneIndex0] ? weight.weight0 : 0f) +
                (supportingBones[weight.boneIndex1] ? weight.weight1 : 0f) +
                (supportingBones[weight.boneIndex2] ? weight.weight2 : 0f) +
                (supportingBones[weight.boneIndex3] ? weight.weight3 : 0f)).ToArray();
        }

        private static List<Vector3> ClipSeatedSupportWeights(List<Vector3> triangle, float[] weights)
        {
            const float minimumSupport = .5f;
            var clipped = new List<Vector3>();
            int previous = triangle.Count - 1;
            for (int current = 0; current < triangle.Count; current++)
            {
                bool inside = weights[current] >= minimumSupport;
                if (inside != (weights[previous] >= minimumSupport))
                {
                    float t = (minimumSupport - weights[previous]) / (weights[current] - weights[previous]);
                    clipped.Add(Vector3.LerpUnclamped(triangle[previous], triangle[current], t));
                }
                if (inside) clipped.Add(triangle[current]);
                previous = current;
            }
            return clipped;
        }

        private static Mesh SeatedMesh(Renderer renderer, Mesh baked)
        {
            if (renderer is SkinnedMeshRenderer skin)
            {
                skin.BakeMesh(baked, true); // Retain the imported FBX unit factor.
                return baked;
            }
            return renderer.GetComponent<MeshFilter>().sharedMesh;
        }

        private static void ReadSeatedMesh(Mesh mesh, out Vector3[] vertices, out int[] triangles)
        {
#if UNITY_EDITOR
            // Passive production bus meshes intentionally discard their CPU
            // copy. Editor readback inspects the imported geometry without
            // changing Read/Write or allocating a persistent production copy.
            using (Mesh.MeshDataArray data = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(mesh))
            using (var positions = new Unity.Collections.NativeArray<Vector3>(
                       data[0].vertexCount, Unity.Collections.Allocator.Temp))
            {
                data[0].GetVertices(positions);
                vertices = positions.ToArray();
                var combined = new List<int>();
                for (int submesh = 0; submesh < data[0].subMeshCount; submesh++)
                {
                    UnityEngine.Rendering.SubMeshDescriptor descriptor = data[0].GetSubMesh(submesh);
                    Assert.That(descriptor.topology, Is.EqualTo(MeshTopology.Triangles), mesh.name);
                    using (var indices = new Unity.Collections.NativeArray<int>(
                               descriptor.indexCount, Unity.Collections.Allocator.Temp))
                    {
                        data[0].GetIndices(indices, submesh);
                        combined.AddRange(indices.ToArray());
                    }
                }
                triangles = combined.ToArray();
            }
#else
            vertices = mesh.vertices;
            triangles = mesh.triangles;
#endif
        }

        private readonly struct SeatedCushionFrame
        {
            public readonly Transform Seat;
            private readonly Quaternion orientationInAnchor;
            private Quaternion Orientation => Seat.rotation * orientationInAnchor;
            public Vector3 Up => Orientation * Vector3.up;
            public Vector3 Right => Orientation * Vector3.right;
            public Vector3 Forward => Orientation * Vector3.forward;

            public SeatedCushionFrame(Transform seat, Quaternion busOrientation)
            {
                Seat = seat;
                // The imported FBX anchor's +Y is longitudinal. Calibrate the
                // neutral vehicle basis once, then carry it with the live seat
                // through road slope and suspension. Do not assume anchor.up.
                orientationInAnchor = Quaternion.Inverse(seat.rotation) * busOrientation;
            }

            public Vector3 Coordinates(Vector3 point)
            {
                return Quaternion.Inverse(Orientation) * (point - Seat.position);
            }
        }

        internal static List<Vector3> ClipSeatPolygon(List<Vector3> polygon, int axis, float edge, bool above)
        {
            var clipped = new List<Vector3>();
            if (polygon.Count == 0) return clipped;
            Vector3 previous = polygon[polygon.Count - 1];
            bool previousInside = above ? previous[axis] >= edge : previous[axis] <= edge;
            foreach (Vector3 current in polygon)
            {
                bool inside = above ? current[axis] >= edge : current[axis] <= edge;
                if (inside != previousInside)
                {
                    float t = (edge - previous[axis]) / (current[axis] - previous[axis]);
                    clipped.Add(Vector3.LerpUnclamped(previous, current, t));
                }
                if (inside) clipped.Add(current);
                previous = current;
                previousInside = inside;
            }
            return clipped;
        }

        private static void CaptureSeatedMesh(Camera camera, CityBusAssetRegistry bus, SeatedCushionFrame frame,
            Transform pelvis, IReadOnlyList<Renderer> occupant, Renderer cushion, string directory, string name)
        {
            // A temporary cutaway preserves the drawn occupant and real seat.
            // The bus driver, rails, dashboard and shell otherwise obscure the
            // contact from an interior camera. Restore every visibility flag.
            var visible = new HashSet<Renderer>(occupant) { cushion };
            if (frame.Seat == bus.DriverSeatAnchor)
                visible.Add(bus.RendererBindings.Single(part => part.SourceName == "INT_SteeringWheel").Renderer);
            Renderer[] hidden = camera.transform.root.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.enabled && !visible.Contains(renderer)).ToArray();
            Vector3 right = frame.Right;
            Vector3 forward = frame.Forward;
            Transform seat = frame.Seat;
            float side = Mathf.Sign(Vector3.Dot(seat.position - bus.transform.position, right));
            Vector3 contact = Vector3.Lerp(seat.position, pelvis.position, .5f);
            // Look in from the occupant's window side, so the opposite row's
            // combined seat mesh stays behind the subject instead of in front.
            camera.transform.position = contact + right * side * 2.2f +
                forward * .75f + frame.Up * .55f;
            camera.transform.LookAt(contact + frame.Up * .10f, frame.Up);
            camera.orthographic = true;
            camera.orthographicSize = 1.0f;
            camera.aspect = 4f / 3f;
            var target = new RenderTexture(1280, 960, 24);
            var pixels = new Texture2D(1280, 960, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                foreach (Renderer renderer in hidden) renderer.enabled = false;
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0f, 0f, 1280f, 960f), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), pixels.EncodeToPNG());
            }
            finally
            {
                foreach (Renderer renderer in hidden)
                    if (renderer != null) renderer.enabled = true;
                camera.targetTexture = null;
                RenderTexture.active = previous;
                Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(target);
            }
        }

        [System.Serializable]
        private sealed class SeatedMeshReport
        {
            public List<SeatedMeshSample> samples = new List<SeatedMeshSample>();
        }

        [System.Serializable]
        private sealed class SeatedMeshSample
        {
            public string design, phase, contact_renderer;
            public float minimum_clearance_m, cushion_width_m, cushion_depth_m;
            public int support_triangles;
            public Vector3 seat, seat_up, contact_in_seat;
        }

        [UnityTest]
        public IEnumerator AmbientPassenger_BoardsRidesAndAlightsAtALaterStop()
        {

            float previousTimeScale = Time.timeScale;
            float previousCaptureDeltaTime = Time.captureDeltaTime;
            GameObject root = null;
            CityBusDirector director = null;
            CityPedestrianDirector pedestrians = null;
            CityBusNpcPassengerController passengers = null;
            try
            {
                // One fixed step for every subsystem. Batch-mode frames come
                // at whatever rate the machine allows — Time.deltaTime was
                // seen at 0.006 s on one run and pinned to the 6.7 s ceiling
                // on another — and the bus, the walkers and the transfer
                // budget must all be measured against the same clock or a
                // hold expires under a passenger who is still walking.
                Time.timeScale = 1f;
                Time.captureDeltaTime = Step;
                root = new GameObject("City Bus NPC Passenger Root");
                var walkableArea = new AlwaysWalkableArea();
                CreateGround(root.transform);

                GameObject cameraObject = new GameObject("NPC Ride Camera");
                cameraObject.transform.SetParent(root.transform, false);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                GameObject uiObject = new GameObject("NPC Ride UI");
                uiObject.transform.SetParent(root.transform, false);
                InteractionPromptView prompt =
                    uiObject.AddComponent<InteractionPromptView>();

                // The hero stands far away: ambient waiters may only be
                // activated outright where the stop is already fog-hidden, and
                // an aboard hero would change the recycle and obstacle rules.
                PlayerRuntime player = PlayerFactory.Create(
                    root.transform,
                    new Vector3(0f, PlayerFactory.GroundedRootOffset, 300f),
                    camera,
                    walkableArea,
                    prompt);

                CityBusPlan route = CreateTwoStopRoute();
                Transform pool =
                    new GameObject("NPC Ride Bus Pool").transform;
                pool.SetParent(root.transform, false);
                CityBusAssetRegistry registry =
                    CityBusResources.Instantiate(pool);
                Assert.That(
                    registry,
                    Is.Not.Null,
                    "The production bus prefab must be available.");
                CityBusPresentation presentation =
                    registry.GetComponent<CityBusPresentation>();
                if (presentation == null)
                {
                    presentation = registry.gameObject
                        .AddComponent<CityBusPresentation>();
                }

                presentation.Initialize(registry);
                presentation.gameObject.SetActive(false);

                GameObject actorObject = new GameObject("NPC Ride Bus Actor");
                actorObject.layer = CityBusCollision.LayerIndex;
                actorObject.transform.SetParent(root.transform, false);
                CityBusActor actor =
                    actorObject.AddComponent<CityBusActor>();
                actor.Initialize(registry.LocalBounds, registry.Dimensions);

                director = root.AddComponent<CityBusDirector>();
                director.Initialize(
                    route,
                    actor,
                    presentation,
                    player.GameObject.transform,
                    null,
                    pool,
                    () => 0f);
                director.enabled = false;

                actor.PrepareSpawn(route, route.SpawnAnchors[0], 0x42555350u);
                actor.BindPresentation(presentation);
                AdvanceActorUntil(
                    actor,
                    () => actor.ServiceOrdinal == 1 && actor.DoorsFullyOpen);
                Assert.That(
                    actor.DoorsFullyOpen,
                    Is.True,
                    "The bus must reach its first stop with open doors.");

                // Take the wait slot from the real door dock rather than
                // guessing a side, then build a pedestrian graph beside it.
                Assert.That(
                    CityBusRidePlan.TryCreate(
                        actor,
                        walkableArea,
                        new Vector3(
                            0f,
                            CityBusNpcPassengerController
                                .PassengerPelvisHeight,
                            0f),
                        CityPedestrianPlanner.AgentRadius,
                        CityBusPassengerDoor.Front,
                        null,
                        CityBusActor.NpcSeatIndices[0],
                        0f,
                        false,
                        out CityBusRidePlan probe),
                    Is.True,
                    "An ambient seat must produce a transfer plan; requiring " +
                    "the hero's opposite-driver side here rejects most of " +
                    "the cabin.");

                Vector3 waitSlot = probe.EntryPose.RootPosition;
                Vector3 along = actor.transform.forward;
                CityPedestrianPlan pedestrianPlan =
                    CreatePedestrianPlan(waitSlot, along);
                pedestrians = CityPedestrianFactory.Create(
                    root.transform,
                    pedestrianPlan,
                    player.GameObject.transform,
                    walkableArea,
                    CityPedestrianPopulationProfile.City,
                    () => false);

                var waitPlan = new CityBusStopWaitPlan(new[]
                {
                    new CityBusStopWaitPoint(
                        0,
                        route.Stops[0].Id,
                        0,
                        -along,
                        new[] { waitSlot },
                        new[] { 0f, 4f, 8f })
                });
                passengers = CityBusNpcPassengerController.Create(
                    director,
                    pedestrians,
                    waitPlan,
                    walkableArea,
                    null,
                    player.GameObject.transform,
                    0x42555350,
                    () => false);

                // Every subsystem runs on the same clock: the pedestrian and
                // passenger directors advance themselves from LateUpdate, and
                // only the bus actor needs driving because its own director is
                // switched off. Disabling either director would call its
                // OnDisable and shut it down for good.
                Assert.That(passengers.IsInitialized, Is.True);

                // The bus was already running when the controller appeared, so
                // it seats its spawn preload first. Let that clear, otherwise
                // a full cabin legitimately refuses the waiter under test.
                // One frame first, so the controller's own LateUpdate can
                // seat its spawn preload before the wait for it to clear.
                yield return null;
                float elapsed = 0f;
                for (int frame = 0;
                     frame < FrameCap &&
                     elapsed < PreloadSeconds &&
                     (actor.NpcOccupantCount != 0 ||
                      passengers.PassengerCount != 0);
                     frame++)
                {
                    actor.Advance(
                        Time.deltaTime,
                        CityBusObstacleState.Clear,
                        0f);
                    elapsed += Time.deltaTime;
                    yield return null;
                }
                Assert.That(
                    actor.NpcOccupantCount,
                    Is.Zero,
                    "Preloaded passengers must leave at their own stop. " +
                    Describe(actor, passengers));

                elapsed = 0f;
                for (int frame = 0;
                     frame < FrameCap &&
                     elapsed < WaiterSeconds &&
                     FindWaitingWalker(pedestrians) == null;
                     frame++)
                {
                    actor.Advance(
                        Time.deltaTime,
                        CityBusObstacleState.Clear,
                        0f);
                    elapsed += Time.deltaTime;
                    yield return null;
                }
                CityPedestrianActor walker = FindWaitingWalker(pedestrians);
                Assert.That(
                    walker,
                    Is.Not.Null,
                    "A stop beyond the fog band must receive a waiter. " +
                    Describe(actor, passengers));

                CityPedestrianArchetype archetype =
                    CityPedestrianDirector.GetActorArchetype(walker);
                Assert.That(archetype, Is.Not.Null);
                Assert.That(
                    archetype.CanRideBus,
                    Is.True,
                    "Only a design that declares a seated ride may wait.");

                elapsed = 0f;
                for (int frame = 0;
                     frame < FrameCap &&
                     elapsed < BoardSeconds &&
                     walker.MotionState !=
                         CityPedestrianMotionState.Riding;
                     frame++)
                {
                    actor.Advance(
                        Time.deltaTime,
                        CityBusObstacleState.Clear,
                        0f);
                    elapsed += Time.deltaTime;
                    yield return null;
                }
                Assert.That(
                    walker.MotionState,
                    Is.EqualTo(CityPedestrianMotionState.Riding),
                    "The waiter never reached a seat. Every ambient boarding " +
                    "defect so far has ended exactly here while the planners " +
                    "and asset contracts stayed green. " +
                    Describe(actor, passengers) +
                    $" walker={walker.MotionState}");
                Assert.That(actor.NpcOccupantCount, Is.EqualTo(1));
                Assert.That(actor.HasPlayerPassenger, Is.False);
                Assert.That(
                    actor.IsSeatOccupied(CityBusActor.PlayerSeatIndex),
                    Is.False,
                    "Seat 07 stays reserved for the hero.");
                Assert.That(
                    CountOccupiedAmbientSeats(actor),
                    Is.EqualTo(1),
                    "The passenger holds exactly one ambient seat.");
                Assert.That(
                    walker.Presentation.IsSeated,
                    Is.True,
                    "A riding walker plays its authored seated loop.");
                Assert.That(
                    IsInsideCabin(actor, registry, walker.Position),
                    Is.True,
                    "A seated passenger must be inside the body it rides in.");

                int boardedOrdinal = actor.ServiceOrdinal;
                elapsed = 0f;
                for (int frame = 0;
                     frame < FrameCap &&
                     elapsed < AlightSeconds &&
                     (walker.IsAttachedToVehicle ||
                      actor.NpcOccupantCount != 0);
                     frame++)
                {
                    actor.Advance(
                        Time.deltaTime,
                        CityBusObstacleState.Clear,
                        0f);
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                Assert.That(
                    walker.IsAttachedToVehicle,
                    Is.False,
                    "The passenger never got off again. " +
                    Describe(actor, passengers) +
                    $" walker={walker.MotionState}");
                Assert.That(
                    actor.ServiceOrdinal,
                    Is.GreaterThan(boardedOrdinal),
                    "A passenger may only leave at a later stop.");
                Assert.That(
                    walker.MotionState,
                    Is.EqualTo(CityPedestrianMotionState.Walking),
                    "An alighted walker rejoins ordinary roaming.");
                Assert.That(
                    walker.Presentation == null ||
                    !walker.Presentation.IsSeated,
                    Is.True);
                Assert.That(
                    IsInsideCabin(actor, registry, walker.Position),
                    Is.False,
                    "An alighted walker stands outside the bus.");
                Assert.That(
                    actor.HasServiceHold,
                    Is.False,
                    "A finished transfer hands its dwell hold back; a leaked " +
                    "one seals the doors at every later stop.");
            }
            finally
            {
                if (passengers != null)
                {
                    passengers.Shutdown();
                }

                if (pedestrians != null)
                {
                    pedestrians.Shutdown();
                }

                if (director != null)
                {
                    director.Shutdown();
                }

                if (root != null)
                {
                    Object.Destroy(root);
                }

                Time.captureDeltaTime = previousCaptureDeltaTime;
                Time.timeScale = previousTimeScale;
            }
        }

        private static string Describe(
            CityBusActor actor,
            CityBusNpcPassengerController passengers)
        {
            return $"[bus {actor.MotionState} stop={actor.CurrentStopIndex} " +
                   $"ordinal={actor.ServiceOrdinal} " +
                   $"doorsOpen={actor.DoorsFullyOpen} " +
                   $"hold={actor.HasServiceHold} " +
                   $"npc={actor.NpcOccupantCount} | tracked=" +
                   $"{passengers.TrackedCount} waiting={passengers.WaiterCount}" +
                   $" aboard={passengers.PassengerCount} dt={Time.deltaTime:F3}]";
        }

        private static CityPedestrianActor FindWaitingWalker(
            CityPedestrianDirector pedestrians)
        {
            IReadOnlyList<CityPedestrianActor> actors = pedestrians.Actors;
            for (int index = 0; index < actors.Count; index++)
            {
                CityPedestrianActor candidate = actors[index];
                if (candidate.IsSpawned &&
                    (candidate.MotionState ==
                         CityPedestrianMotionState.WaitingAtStop ||
                     candidate.MotionState ==
                         CityPedestrianMotionState.ApproachingStop))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// The controller owns its passenger records privately, so occupancy
        /// is read back through the cabin itself rather than through a handle
        /// a test has no business holding.
        /// </summary>
        private static int CountOccupiedAmbientSeats(CityBusActor actor)
        {
            int count = 0;
            IReadOnlyList<int> seats = CityBusActor.NpcSeatIndices;
            for (int index = 0; index < seats.Count; index++)
            {
                if (actor.IsSeatOccupied(seats[index]))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsInsideCabin(
            CityBusActor actor,
            CityBusAssetRegistry registry,
            Vector3 position)
        {
            Vector3 local = actor.transform.InverseTransformPoint(position);
            Bounds bounds = registry.LocalBounds;
            return Mathf.Abs(local.x) <= bounds.extents.x &&
                   Mathf.Abs(local.z) <= bounds.extents.z;
        }

        private static void AdvanceActorUntil(
            CityBusActor actor,
            System.Func<bool> predicate)
        {
            for (int guard = 0; guard < 4000 && !predicate(); guard++)
            {
                actor.Advance(Step, CityBusObstacleState.Clear, 0f);
            }
        }

        private static CityPedestrianPlan CreatePedestrianPlan(
            Vector3 slot,
            Vector3 along,
            bool serviceCrowd = false)
        {
            Vector3 planar = new Vector3(along.x, 0f, along.z).normalized;
            var nodes = new List<CityPedestrianNode>
            {
                new CityPedestrianNode("npc-stop-node", slot, false),
                new CityPedestrianNode(
                    "npc-lane-node",
                    slot + (planar * 6f),
                    false),
                new CityPedestrianNode(
                    "npc-far-node",
                    slot + (planar * 12f),
                    false)
            };
            var links = new List<CityPedestrianLink>
            {
                new CityPedestrianLink(
                    "npc-link-a",
                    0,
                    1,
                    CityPedestrianLinkKind.Sidewalk),
                new CityPedestrianLink(
                    "npc-link-b",
                    1,
                    2,
                    CityPedestrianLinkKind.Sidewalk)
            };
            var anchors = new List<CityPedestrianSpawnAnchor>
            {
                new CityPedestrianSpawnAnchor(
                    "npc-anchor",
                    slot + (planar * 6f),
                    0,
                    1)
            };
            if (serviceCrowd)
            {
                anchors.Add(new CityPedestrianSpawnAnchor("npc-near-anchor", slot + planar * 3f, 0, 1));
                anchors.Add(new CityPedestrianSpawnAnchor("npc-far-anchor", slot + planar * 9f, 1, 2));
            }
            var rectangles = new List<Rect>
            {
                Rect.MinMaxRect(
                    slot.x - 60f,
                    slot.z - 60f,
                    slot.x + 60f,
                    slot.z + 60f)
            };
            return new CityPedestrianPlan(
                91,
                37,
                0x50454431u,
                CityPedestrianPlanner.AgentRadius,
                nodes,
                links,
                anchors,
                rectangles);
        }

        private static CityBusPlan CreateTwoStopRoute(float elevation = 0f)
        {
            Vector3 start = new Vector3(
                0f,
                elevation + CityStreetSurfacePlanner.RoadTop,
                0f);
            Vector3 east = start + (Vector3.right * 30f);
            Vector3 southEast = east + (Vector3.back * 20f);
            Vector3 southWest = southEast + (Vector3.left * 30f);
            var edge = new RoadEdge(
                new Vector2Int(0, 0),
                new Vector2Int(0, 1));
            var samples = new List<CityBusPathSample>
            {
                new CityBusPathSample(start, Vector3.right, 0f),
                new CityBusPathSample(east, Vector3.back, 30f),
                new CityBusPathSample(southEast, Vector3.left, 50f),
                new CityBusPathSample(southWest, Vector3.forward, 80f),
                new CityBusPathSample(start, Vector3.right, 100f)
            };
            var clearance = new CityBusClearanceResult(
                true,
                CityBusClearanceFailureKind.None,
                -1,
                default,
                CityBusDesignVehicle.Default.ClearanceMargin);
            var nodes = new List<CityBusRouteNode>
            {
                new CityBusRouteNode(
                    "npc-ride-node",
                    start,
                    Vector3.right,
                    edge,
                    edge.A,
                    edge.B,
                    new[] { 0 })
            };
            var links = new List<CityBusRouteLink>
            {
                new CityBusRouteLink(
                    "npc-ride-link",
                    0,
                    0,
                    CityBusRouteLinkKind.Straight,
                    edge.B,
                    samples,
                    float.PositiveInfinity,
                    clearance)
            };
            var anchors = new List<CityBusSpawnAnchor>
            {
                new CityBusSpawnAnchor(
                    "npc-ride-anchor",
                    0,
                    0f,
                    start,
                    Vector3.right,
                    edge)
            };
            var stops = new List<CityBusStopDescriptor>
            {
                CreateStop("npc-ride-stop-a", start, 8f, edge),
                CreateStop("npc-ride-stop-b", start, 48f, edge)
            };
            return new CityBusPlan(
                91,
                37,
                0x42555332u,
                1.5f,
                CityBusDesignVehicle.Default,
                nodes,
                links,
                anchors,
                stops,
                new List<CityBusClearanceFailure>(),
                1,
                1);
        }

        private static CityBusStopDescriptor CreateStop(
            string id,
            Vector3 start,
            float distance,
            RoadEdge edge)
        {
            Vector3 position = distance <= 30f
                ? start + (Vector3.right * distance)
                : start + (Vector3.right * 30f) +
                  (Vector3.back * (distance - 30f));
            Vector3 forward = distance <= 30f
                ? Vector3.right
                : Vector3.back;
            return new CityBusStopDescriptor(
                id,
                $"{id}-shelter",
                position + (Vector3.forward * 3f),
                0,
                distance,
                position,
                forward,
                edge);
        }

        private static void CreateGround(Transform parent, float elevation = 0f)
        {
            GameObject ground = GameObject.CreatePrimitive(
                PrimitiveType.Cube);
            ground.name = "NPC Ride Test Ground";
            ground.transform.SetParent(parent, false);
            // Its TOP must be the height this scene actually lives at, and
            // that is the ROAD, not the sidewalk. Everything synthetic here
            // - the route, the bus, and therefore every door dock derived
            // from it - is built on CityStreetSurfacePlanner.RoadTop, while
            // this slab used to be raised to SidewalkTop. Six centimetres of
            // difference buried every dock, and the pedestrian director's
            // spawn-clearance capsule correctly refused to materialise a
            // waiter inside terrain: its lowest point sat 12 mm under the
            // slab. That single line is why this test "failed on any code"
            // for as long as it existed. In the real city the road and the
            // pavement are separate meshes at their own heights, so nothing
            // there was ever wrong.
            ground.transform.position = new Vector3(
                10f,
                elevation + CityStreetSurfacePlanner.RoadTop * 0.5f,
                -10f);
            ground.transform.localScale = new Vector3(
                160f,
                CityStreetSurfacePlanner.RoadTop,
                160f);
        }

        private sealed class AlwaysWalkableArea : IWalkableArea
        {
            public bool Contains(Vector3 position, float radius = 0f)
            {
                return true;
            }

            public Vector3 Constrain(
                Vector3 currentPosition,
                Vector3 desiredPosition,
                float radius = 0f)
            {
                return desiredPosition;
            }

            public Vector3 ClosestPoint(
                Vector3 position,
                float radius = 0f)
            {
                return position;
            }
        }
    }
}
