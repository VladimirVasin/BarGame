using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;
using UnityEngine.TestTools.Utils;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest]
        public IEnumerator LiveCity_OrdinaryStreetsContinuePastFormerFenceCaps()
        {
            yield return LoadProductionCity();
            CityLayout layout = city.Layout;
            RoadEdge[] streets = layout.RoadEdges.Where(edge => layout.GetPathKind(edge) == CityPathKind.Street).ToArray();
            var deadEnds = streets.SelectMany(edge => new[] { edge.A, edge.B }).GroupBy(node => node)
                .Where(incident => incident.Count() == 1).Select(incident => incident.Key).ToArray();
            Assert.That(deadEnds, Is.Empty, "Driving into these street nodes requires a U-turn: " + string.Join(", ", deadEnds));
            Assert.That(city.World.FencePlan.Segments.Any(segment => segment.Purpose == RoadFenceSegmentPurpose.DeadEnd),
                Is.False, "A physical fence still spans the end of a city carriageway.");

            var terrace = CityTerraceDistrictPlan.ForLayout(layout);
            Assert.That(terrace.IsEnabled && terrace.HasShortcut && terrace.HasCourtyardLink &&
                CityCoastalAscentPlan.IsEnabled(layout), Is.True, "The coast, stair court and both pedestrian passages must remain connected.");
            Assert.That(city.BusPlans.Select(plan => plan.Number), Is.EquivalentTo(new[] { 1, 2, 3 }));
            foreach (CityBusPlan route in city.BusPlans)
            {
                Assert.That(route.IsEmpty, Is.False);
                var order = route.OrderedLinkIndices;
                Assert.That(order, Is.Not.Empty);
                for (int i = 0; i < order.Count; i++)
                    Assert.That(route.Links[order[i]].ToNodeIndex, Is.EqualTo(route.Links[order[(i + 1) % order.Count]].FromNodeIndex),
                        $"Route {route.Number} no longer closes through {route.Links[order[i]].Id}");
            }

            bool capture = System.Environment.GetEnvironmentVariable("BAR_PROMENADE_CAPTURE_ROAD_CONNECTIONS") == "1" &&
                SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            if (capture)
            {
                double untilNoon = (2160d - GameSessionState.GameTimeOfDayMinutes) % 1440d;
                GameSessionState.AdvanceGameTime((float)(untilNoon / GameTimeState.GameMinutesPerRealSecond));
                yield return null;
            }
            Physics.SyncTransforms();
            foreach (Vector3 mouth in new[] { terrace.EntryStartWorld, terrace.EntryEndWorld,
                terrace.LowerPassStartWorld, terrace.LowerPassEndWorld, terrace.CourtyardLinkRouteWorld[0] })
                AssertNoRoadRail(mouth);
            var roadIssues = new List<string>();
            // Surveyed before and after the reconnect. Exercise only the new carriageways.
            RoadEdge[] connections = {
                East(1, 2), East(1, 4), East(1, 8), East(1, 10), East(3, 2), East(3, 10),
                North(3, 4), North(3, 7), East(5, 2), North(5, 9), North(8, 2), East(8, 9),
                North(8, 10), North(10, 1), East(10, 7), North(10, 10), East(12, 1), East(12, 7), East(12, 10)
            };
            foreach (RoadEdge edge in connections)
            {
                Assert.That(streets, Does.Contain(edge));
                InspectNewCarriageway(edge);
            }
            Assert.That(roadIssues, Is.Empty, string.Join("\n", roadIssues));
            // The previous map ended these two western streets and the eastern quay branch at full-width rails.
            foreach (var site in new[] {
                (node: new Vector2Int(1, 2), approach: new Vector2Int(1, 1)),
                (node: new Vector2Int(2, 4), approach: new Vector2Int(2, 5)),
                (node: new Vector2Int(8, 2), approach: new Vector2Int(7, 2)) })
            {
                RoadEdge approach = new RoadEdge(site.node, site.approach);
                Assert.That(streets, Does.Contain(approach));
                if (capture)
                {
                    Vector3 junction = layout.GetNodeWorldPosition(site.node);
                    Vector3 towardApproach = (layout.GetNodeWorldPosition(site.approach) - junction).normalized;
                    Vector3 eye = junction + towardApproach * 10f + Vector3.up * 4.2f;
                    city.Camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(junction - eye));
                    AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "CityRoadConnections",
                        $"junction-{site.node.x}-{site.node.y}", 1024, 576);
                }
            }
#if UNITY_EDITOR
            if (System.Environment.GetEnvironmentVariable("BAR_PROMENADE_EXPORT_CITY_MAP") == "1")
                System.Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                    .GetMethod("ExportCurrent", System.Type.EmptyTypes).Invoke(null, null);
#endif

            RoadEdge East(int x, int z) => new RoadEdge(new Vector2Int(x, z), new Vector2Int(x + 1, z));
            RoadEdge North(int x, int z) => new RoadEdge(new Vector2Int(x, z), new Vector2Int(x, z + 1));

            void InspectNewCarriageway(RoadEdge edge)
            {
                CityRoadPath path = layout.RoadGeometry.Get(edge);
                CityBusDesignVehicle vehicle = CityBusDesignVehicle.Default;
                float lane = (layout.RoadWidth - 2f * CityStreetSurfacePlanner.SidewalkWidth) * .25f;
                float halfBody = vehicle.BodyWidth * .5f;
                float wheelOffset = halfBody - .25f;
                float[] tracks = { -lane - wheelOffset, -lane + wheelOffset, 0f, lane - wheelOffset, lane + wheelOffset };
                int intervals = Mathf.CeilToInt(path.Length / .65f);
                var reported = new HashSet<string>();
                for (int i = 0; i <= intervals; i++)
                {
                    float amount = (float)i / intervals;
                    CityRoadSample sample = path.SampleDistance(path.Length * amount);
                    float top = layout.ElevationPlan.SampleRoadDatum(edge, amount) + CityStreetSurfacePlanner.RoadTop;
                    Vector3 middle = new Vector3(sample.Position.x, top, sample.Position.y);
                    Vector3 right = new Vector3(sample.Right.x, 0f, sample.Right.y);
                    foreach (float track in tracks)
                    {
                        Vector3 contact = middle + right * track;
                        RaycastHit[] hits = Physics.RaycastAll(contact + Vector3.up * .4f, Vector3.down, .8f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                        bool paved = hits.Any(hit => Mathf.Abs(hit.point.y - top) < .07f &&
                            hit.collider.TryGetComponent(out FootstepGround ground) &&
                            ground.ResolveKind(hit.triangleIndex) == FootstepGroundKind.Concrete);
                        if (!paved && reported.Add("pavement " + track))
                            roadIssues.Add($"{edge} missing asphalt on track {track:F2} at {contact:F2}; hits " +
                                string.Join(", ", hits.Select(hit => hit.collider.name + "@" + hit.point.y.ToString("F2"))));
                    }

                    // Adjacent boxes overlap along the path. Together the two lane envelopes include
                    // the centre line, up to roof height, while clearing the sloping road deck.
                    const float clearance = .23f;
                    float halfHeight = (vehicle.BodyHeight - clearance) * .5f;
                    Quaternion heading = Quaternion.LookRotation(new Vector3(sample.Tangent.x, 0f, sample.Tangent.y));
                    var area = new Vector3(lane + halfBody, halfHeight, .38f);
                    foreach (Collider hit in Physics.OverlapBox(middle + Vector3.up * (clearance + halfHeight),
                        area, heading, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    {
                        if (hit.transform.IsChildOf(city.Player.GameObject.transform) ||
                            hit.GetComponentInParent<CityBusActor>() != null || hit.GetComponentInParent<CityPedestrianActor>() != null ||
                            city.LastRouteCar != null && hit.transform.IsChildOf(city.LastRouteCar.transform) ||
                            city.Cannery != null && city.Cannery.Truck != null && hit.transform.IsChildOf(city.Cannery.Truck)) continue;
                        string obstacle = hit.name;
                        for (Transform parent = hit.transform.parent; parent != null && parent != city.transform; parent = parent.parent)
                            obstacle = parent.name + "/" + obstacle;
                        if (reported.Add(obstacle))
                            roadIssues.Add($"{edge} carriageway blocked at {middle:F2} by {obstacle}");
                    }
                }
            }

            void AssertNoRoadRail(Vector3 floor)
            {
                Collider[] obstructions = Physics.OverlapSphere(floor + Vector3.up * .78f, .48f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                Assert.That(obstructions.Where(hit => hit.name == "Safety Rails"), Is.Empty,
                    $"Fence blocks the through route at {floor}");
            }
        }

        [UnityTest]
        public IEnumerator LiveCity_DormantMorningAdvancesEveryServiceAndPreservesResidentOwnership()
        {
            yield return LoadProductionCity();
            GameSessionState.TryStartGameTimeFromWake();
            int[] ordinals = city.BusVehicles.Select(bus => bus.Actor.ServiceOrdinal).ToArray();
            Assert.That(city.EnterDormant(), Is.True);
            var services = city.BusVehicles;
            Assert.That(services.All(bus => !bus.Actor.IsSpawned), Is.True);
            GameSessionState.AdvanceGameTime((float)(90d / GameTimeState.GameMinutesPerRealSecond));
            city.ResumeFromDormant();
            var riders = new HashSet<int>();
            for (int vehicle = 0; vehicle < services.Count; vehicle++)
            {
                Assert.That(services[vehicle].Actor.ServiceOrdinal, Is.GreaterThan(ordinals[vehicle]),
                    "Service must advance while the City hierarchy is disabled.");
                for (int slot = 0; slot < CityBusActor.MaximumNpcOccupants; slot++)
                {
                    int id = city.ResidentJourneys.RiderAt(vehicle, slot);
                    if (id < 0) continue;
                    Assert.That(riders.Add(id), Is.True);
                    Assert.That(city.ResidentJourneys.GetState(id).Vehicle, Is.EqualTo(vehicle));
                }
            }
            for (int id = 0; id < CityResidentLifeRules.ResidentCount; id++)
                Assert.That(city.ResidentJourneys.GetState(id).Phase == CityResidentJourneyPhase.Riding,
                    Is.EqualTo(riders.Contains(id)), "Resident " + id);

            // Look beside a real commuter's moving vehicle after hidden service.
            // Arrival times can vary; poll the morning, never inject a rider into the test.
            int rider = -1;
            for (int step = 0; step < 48 && rider < 0; step++)
            {
                for (int id = 0; id < CityResidentLifeRules.ResidentCount; id++)
                {
                    var state = city.ResidentJourneys.GetState(id);
                    if (state.Activity != CityResidentActivity.Work || state.Phase != CityResidentJourneyPhase.Riding)
                        continue;
                    var bus = services[state.Vehicle];
                    int vehicle = state.Vehicle;
                    int slot = CityBusActor.NpcSlotForPlace(state.PlaceIndex);
                    if (bus.Actor.CurrentStopIndex == state.ExitStop || bus.EstimatedSecondsToStop(state.ExitStop) < 6d ||
                        bus.CanneryBlocksSpawn(bus.Actor.Position, bus.Actor.Rotation)) continue;
                    // Follow the first passenger due at that exit so another person's
                    // doorway animation cannot consume the test's bounded wait.
                    bool precedes = Enumerable.Range(0, slot).Select(i => city.ResidentJourneys.RiderAt(vehicle, i))
                        .Any(other => other >= 0 && city.ResidentJourneys.GetState(other).ExitStop == state.ExitStop);
                    if (precedes) continue;
                    rider = id;
                    break;
                }
                if (rider < 0)
                {
                    GameSessionState.AdvanceGameTime((float)(3d / GameTimeState.GameMinutesPerRealSecond));
                    yield return null;
                }
            }
            Assert.That(rider, Is.GreaterThanOrEqualTo(0), "Expected an assigned worker on a morning service.");
            var commute = city.ResidentJourneys.GetState(rider);
            var service = services[commute.Vehicle];
            var passengers = city.VehiclePassengers[commute.Vehicle];
            int cabinSlot = CityBusActor.NpcSlotForPlace(commute.PlaceIndex);
            city.Player.Motor.Teleport(service.Actor.Position + service.Actor.transform.right * 15f);
            for (int frame = 0; frame < 25 && passengers.GetCabinBody(rider) == null; frame++)
                yield return null;
            Assert.That(service.Actor.IsSpawned, Is.True);
            AssertCabinView();
            var checkpoint = CityResidentTransitSession.CaptureCheckpoint();
            Assert.That(checkpoint.Residents.Seats[commute.Vehicle, cabinSlot], Is.EqualTo(rider));

            // Let the trip continue offscreen, then stand on the public pavement
            // before the same vehicle opens its doors at the assigned destination.
            double remaining = service.EstimatedSecondsToStop(commute.ExitStop);
            Assert.That(city.EnterDormant(), Is.True);
            Assert.That(passengers.GetCabinBody(rider), Is.Null);
            if (remaining > 4d) GameSessionState.AdvanceGameTime((float)(remaining - 4d));
            city.ResumeFromDormant();
            var exit = service.Plan.Stops[commute.ExitStop];
            city.Player.Motor.Teleport(CityBusStopWaitPlanner.StandingPosition(service.Plan, exit, city.Layout, 8f));
            float previousStep = Time.captureDeltaTime;
            try
            {
                Time.captureDeltaTime = .1f;
                CityPedestrianPresentation beforeExit = null;
                CityPedestrianActor exiting = null;
                for (int frame = 0; frame < 300; frame++)
                {
                    yield return null;
                    if (beforeExit == null && passengers.GetCabinBody(rider) != null)
                    {
                        AssertCabinView();
                        beforeExit = passengers.GetCabinBody(rider);
                    }
                    if (exiting == null)
                        exiting = city.Pedestrians.Actors.SingleOrDefault(w => w.IsSpawned &&
                            city.Pedestrians.ResidentIndex(w) == rider);
                    if (exiting != null && city.ResidentJourneys.GetState(rider).Vehicle < 0) break;
                }
                Assert.That(beforeExit, Is.Not.Null, "The manifest must restore the cabin view before the stop.");
                Assert.That(exiting, Is.Not.Null, "The same resident must take a physical pedestrian slot at the open door.");
                Assert.That(exiting.Presentation, Is.SameAs(beforeExit), "Keep the actual body through the doorway handoff.");
                Assert.That(passengers.GetCabinBody(rider), Is.Null);
                Assert.That(city.Pedestrians.Actors.Count(w => w.IsSpawned && city.Pedestrians.ResidentIndex(w) == rider),
                    Is.EqualTo(1));
                Assert.That(city.ResidentJourneys.GetState(rider).Vehicle, Is.EqualTo(-1));
                Assert.That(city.ResidentJourneys.RiderAt(commute.Vehicle, cabinSlot), Is.Not.EqualTo(rider));
                Assert.That(checkpoint.Residents.Seats[commute.Vehicle, cabinSlot], Is.EqualTo(rider),
                    "A later alighting must not change the checkpoint taken aboard.");
            }
            finally { Time.captureDeltaTime = previousStep; }

            void AssertCabinView()
            {
                var body = passengers.GetCabinBody(rider);
                Assert.That(body, Is.Not.Null);
                Assert.That(body.transform.IsChildOf(service.Actor.Presentation.SuspensionVisual), Is.True);
                Assert.That(body.IsStandingInBus, Is.EqualTo(CityBusActor.IsStandingPlace(commute.PlaceIndex)));
                if (!body.IsStandingInBus)
                    Assert.That(body.SeatAnchor, Is.SameAs(service.Actor.Presentation.Registry.PassengerSeatAnchors[commute.PlaceIndex]));
                Assert.That(city.ResidentJourneys.GetState(rider).PlaceIndex, Is.EqualTo(commute.PlaceIndex));
                Assert.That(city.ResidentJourneys.GetState(rider).ExitStop, Is.EqualTo(commute.ExitStop));
                Assert.That(city.Pedestrians.Actors.Any(w => w.IsSpawned && city.Pedestrians.ResidentIndex(w) == rider),
                    Is.False, "A visible cabin body cannot simultaneously be a walker on the street.");
            }
        }

        [UnityTest]
        public IEnumerator LiveCity_EveryBusStopCanBeBoardedFromThePublicPavement()
        {
            yield return LoadProductionCity();
            var metadata = JsonUtility.FromJson<CityMapArtResources.Metadata>(
                Resources.Load<TextAsset>(CityMapArtResources.ResourcePath).text);
            Debug.Log($"Bus boarding map: illustrated {CityMapArtResources.TryGet(city.Map, out _, out _)}, " +
                $"current {CityMapArtResources.CreateFingerprint(city.Map)}, artwork {metadata.fingerprint}");
            bool capture = System.Environment.GetEnvironmentVariable("BAR_PROMENADE_CAPTURE_BUS_BOARDING") == "1" &&
                SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            var player = city.Player;
            var motor = player.Motor;
            var hero = player.GameObject.transform;
            var capsule = hero.GetComponent<CharacterController>();
            var visual = (Player3DCharacterPresentation)player.Visual;
            var areas = city.World.WalkableArea;
            var surfaces = CityStreetSurfacePlanner.Create(city.Layout);
            var failures = new List<string>();
            var input = new InputTestFixture();
            input.Setup();
            var keyboard = InputSystem.AddDevice<Keyboard>();
            keyboard.MakeCurrent();
            var cursor = new object();
            var prompt = city.GetComponentInChildren<InteractionPromptView>(true);
            var doors = new[] { CityBusPassengerDoor.Front, CityBusPassengerDoor.Rear };
            bool previousFocus = GameInput.MovementFocused;
            int interval = OnDemandRendering.renderFrameInterval;
            int boarded = 0;
            CityPedestrianActor placedWaiter = null;
            try
            {
                GameInput.HandleApplicationFocus(true);
                GameCursor.Capture(cursor, CursorLockMode.Locked, false, 0);
                Time.captureDeltaTime = .10f;
                OnDemandRendering.renderFrameInterval = 24;
                // Own the fleet clock only. Keep actual world colliders, outdoor population,
                // player motion, interaction discovery and the production boarding animation.
                foreach (var vehicle in city.BusVehicles) ResetService(vehicle);
                Assert.That(city.BusVehicles.Count, Is.EqualTo(city.BusPlans.Count * CityBusDirector.VehiclesPerLine));
                for (int lineIndex = 0; lineIndex < city.BusPlans.Count; lineIndex++)
                {
                    foreach (var stop in city.BusPlans[lineIndex].Stops)
                    foreach (var door in doors)
                    {
                        int vehicleSlot = (stop.SequenceIndex + (door == CityBusPassengerDoor.Front ? 0 : 1)) % CityBusDirector.VehiclesPerLine;
                        int vehicle = lineIndex * CityBusDirector.VehiclesPerLine + vehicleSlot;
                        var line = city.BusVehicles[vehicle];
                        var ride = city.BusRides[vehicle];
                        var actor = line.Actor;
                        string stage = $"{line.Plan.Number:00}-{stop.SequenceIndex + 1:00}/{door} vehicle {vehicleSlot + 1}";
                        if (placedWaiter != null) city.Pedestrians.ReleaseRouteBoundActor(placedWaiter);
                        placedWaiter = null;
                        var queue = city.LineWaitPlans[lineIndex].WaitPoints.Single(point => point.StopId == stop.Id);
                        Assert.That(surfaces.TryGetStopCrossing(stop.Id, out var zebra), Is.True, stage + " zebra");
                        Assert.That(zebra.Stripes.Count, Is.GreaterThanOrEqualTo(4), stage + " painted stripes");
                        // Independent cases approach from alternate kerbs. The
                        // ordinary motor must walk the painted surface at its actual grade.
                        var path = zebra.WalkPath;
                        bool reverse = door == CityBusPassengerDoor.Rear;
                        motor.Teleport(path[reverse ? path.Count - 1 : 0] + Vector3.up * (PlayerFactory.GroundedRootOffset + .035f));
                        Physics.SyncTransforms();
                        for (int p = 1; p < path.Count; p++)
                        {
                            var point = path[reverse ? path.Count - 1 - p : p];
                            yield return Walk(point + Vector3.up * PlayerFactory.GroundedRootOffset, stage + " zebra");
                            Assert.That(motor.transform.position.y, Is.EqualTo(point.y + PlayerFactory.GroundedRootOffset).Within(.22f),
                                stage + " crossing elevation at " + point);
                        }
                        if (door == CityBusPassengerDoor.Front)
                        {
                            Debug.Log($"Bus boarding audit {stage}, seated so far {boarded}, failures {failures.Count}.");
                            foreach (Vector3 slot in queue.WaitSlots)
                            {
                                // Sample the public footway at the same longitudinal location,
                                // including the hillside's curved, graded pavement.
                                Vector3 forward = Vector3.ProjectOnPlane(stop.Forward, Vector3.up).normalized;
                                Vector3 center = CityBusStopWaitPlanner.StandingPosition(line.Plan, stop, city.Layout);
                                float alongLane = Vector3.Dot(slot - center, forward);
                                Vector3 passage = CityBusStopWaitPlanner.StandingPosition(line.Plan, stop, city.Layout, alongLane);
                                float separation = Vector3.ProjectOnPlane(slot - passage, Vector3.up).magnitude;
                                if (separation < capsule.radius + city.PedestrianPlan.AgentRadius)
                                    Fail($"{stage} occupied wait slot {slot:F2} blocks public pavement {passage:F2}, clearance {separation:F2}");
                            }
                        }
                        ResetService(line);
                        var bench = CityBusStopWorldBuilder.DescribeShelterBenchSeat(stop);
                        Vector3 atShelter = bench.SeatTopCenter + bench.FaceDirection * .86f;
                        atShelter.y = stop.ShelterPosition.y;
                        Vector3 curbAtShelter = CityBusStopWaitPlanner.StandingPosition(line.Plan, stop, city.Layout,
                            CityBusStopWorldBuilder.ShelterOffsetAlongLane);
                        Vector3 wait = CityBusStopWaitPlanner.StandingPosition(line.Plan, stop, city.Layout, .8f);
                        // Simulate arriving from each end of the public pavement in turn. The
                        // only player warp is the independent case setup, outside the whole roof.
                        Vector3 start = CityBusStopWaitPlanner.StandingPosition(line.Plan, stop, city.Layout,
                            door == CityBusPassengerDoor.Front ? 6.5f : -6.5f);
                        motor.Teleport(start + Vector3.up * (PlayerFactory.GroundedRootOffset + .035f));
                        Physics.SyncTransforms();
                        // Occupy a real waiting place with a solid resident, even when this
                        // particular stop has no naturally scheduled demand at the test hour.
                        bool hasWaiter = city.Pedestrians.Actors.Any(walker => walker.IsSpawned &&
                            walker.MotionState == CityPedestrianMotionState.WaitingAtStop &&
                            queue.WaitSlots.Any(slot => Vector3.Distance(walker.Position, slot) < .4f));
                        if (!hasWaiter)
                        {
                            int id = Enumerable.Range(0, CityResidentLifeRules.ResidentCount).First(candidate =>
                                !city.ResidentJourneys.IsStreetPresented(candidate) &&
                                city.ResidentJourneys.GetState(candidate).Phase == CityResidentJourneyPhase.AtDestination);
                            foreach (var slot in queue.WaitSlots)
                            {
                                placedWaiter = city.Pedestrians.TrySpawnStopWaiter(slot, queue.Facing,
                                    queue.PedestrianNodeIndex, queue.NodeDistances, 7u, id);
                                if (placedWaiter != null) break;
                            }
                            Assert.That(placedWaiter, Is.Not.Null, stage + " cannot host a real waiting pedestrian.");
                        }
                        yield return null;
                        int before = failures.Count;
                        yield return Walk(curbAtShelter, stage + " public pavement");
                        if (failures.Count == before) yield return Walk(atShelter, stage + " inside shelter");
                        if (failures.Count == before) yield return Walk(curbAtShelter, stage + " shelter exit");
                        if (failures.Count == before) yield return Walk(wait, stage + " waiting place");
                        if (failures.Count != before) continue;

                        float along = Mathf.Max(0f, stop.DistanceAlongLink - 8f);
                        CityBusActor.EvaluateLink(line.Plan.Links[stop.LinkIndex], along, out var spawn, out var heading);
                        actor.PrepareSpawn(line.Plan, new CityBusSpawnAnchor("approaching-platform",
                            stop.LinkIndex, along, spawn, heading, stop.RoadEdge), 7u);
                        int ticks = 0;
                        while (ticks++ < 220 && !(actor.CurrentStop == stop && actor.DoorsFullyOpen))
                        {
                            line.Advance(.1f);
                            if (ticks % 5 == 0) yield return null;
                        }
                        if (!actor.IsSpawned || !actor.DoorsFullyOpen || actor.CurrentStop != stop)
                        {
                            Fail($"{stage} bus failed to serve waiting hero: {actor.MotionState}, {actor.Position}, stop {actor.CurrentStop?.Id}");
                            continue;
                        }
                        Vector3 pelvis = hero.InverseTransformPoint(visual.Registry.Anchors.Pelvis.position);
                        if (!CityBusRidePlan.TryCreate(actor, areas, pelvis, capsule.radius, door, surfaces, out var dock))
                        {
                            Fail(stage + " open bus has no grounded dock");
                            continue;
                        }
                        yield return Walk(dock.EntryPose.RootPosition, stage + " open doorway", line);
                        if (failures.Count != before) continue;
                        yield return null;
                        yield return null;
                        if (!ReferenceEquals(player.Interactor.ActiveInteractable, ride) || !ride.CanInteract(player.Interactor))
                        {
                            Fail($"{stage} no boarding prompt at {hero.position}, active {player.Interactor.ActiveInteractable}, open {actor.DoorsFullyOpen}");
                            continue;
                        }
                        if (capture && stop.SequenceIndex == 0 && line.Plan.Number >= 2 && door == CityBusPassengerDoor.Front)
                        {
                            Transform lens = city.Camera.transform;
                            var pose = new Pose(lens.position, lens.rotation);
                            Vector3 target = Vector3.Lerp(atShelter, hero.position, .65f) + Vector3.up;
                            Vector3 eye = hero.position + stop.Forward * 6.5f - stop.RoadsideForward * .8f + Vector3.up * 2.3f;
                            lens.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                            try { AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "BusBoarding", $"{line.Plan.Number:00}-01", 960, 540); }
                            finally { lens.SetPositionAndRotation(pose.position, pose.rotation); }
                        }
                        input.Press(keyboard.eKey, queueEventOnly: true);
                        yield return null;
                        string press = $"phase on E {ride.State}, key down {keyboard.eKey.isPressed}, " +
                            $"key frame {keyboard.eKey.wasPressedThisFrame}, world E {GameInput.WasPressed(GameInputAction.Interact, GameInputContext.Contextual)}, " +
                            $"clickable {prompt?.IsClickable}, feedback {prompt?.IsFeedbackVisible}, focus {GameInput.MovementFocused}";
                        input.Release(keyboard.eKey, queueEventOnly: true);
                        for (int frame = 0; frame < 65 && ride.State != CityBusRideState.Riding; frame++) yield return null;
                        if (ride.State != CityBusRideState.Riding || !actor.HasPlayerPassenger || ride.ActivePlan.PassengerDoor != door)
                            Fail($"{stage} E did not seat the hero through the selected door: {ride.State}; {press}");
                        else boarded++;
                        ResetService(line);
                    }
                }
                Assert.That(failures, Is.Empty, string.Join("\n", failures));
                Assert.That(boarded, Is.EqualTo(city.BusPlans.Sum(plan => plan.Stops.Count) * doors.Length));
                Assert.That(city.BusPlans.Sum(plan => plan.Stops.Count), Is.EqualTo(20));
            }
            finally
            {
                if (placedWaiter != null) city.Pedestrians.ReleaseRouteBoundActor(placedWaiter);
                motor.CancelInteractionPoseMove();
                foreach (var ride in city.BusRides) ride.ForceReleaseForBusLifecycle();
                input.TearDown();
                GameCursor.Release(cursor);
                GameInput.HandleApplicationFocus(previousFocus);
                OnDemandRendering.renderFrameInterval = interval;
            }

            void ResetService(CityBusDirector line)
            {
                // The production director asks all riders (including active NPCs) to
                // release their seats before returning the shared model to its pool.
                line.enabled = true;
                line.enabled = false;
                line.Actor.ReleasePresentation(line.transform);
            }

            void Fail(string detail)
            {
                Debug.Log("Bus boarding audit: " + detail);
                failures.Add(detail);
            }

            IEnumerator Walk(Vector3 target, string stage, CityBusDirector arrivingLine = null)
            {
                for (int step = 0; step < 120; step++)
                {
                    if (motor.MoveTowardsApproachWaypoint(target, .14f, .08f))
                    {
                        motor.CancelInteractionPoseMove();
                        yield break;
                    }
                    arrivingLine?.Advance(.08f);
                    if (step % 3 == 2) yield return null;
                }
                motor.CancelInteractionPoseMove();
                Vector3 position = hero.position;
                Vector3 direction = Vector3.ProjectOnPlane(target - position, Vector3.up).normalized;
                var obstacles = Physics.CapsuleCastAll(position + Vector3.up * .36f, position + Vector3.up * 1.35f,
                    capsule.radius, direction, 1.5f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
                Fail($"{stage} blocked at {position:F2} toward {target:F2}: " +
                    $"inside walkable region {areas.Contains(target, capsule.radius)}; " +
                    string.Join(", ", obstacles.Where(hit => !hit.collider.transform.IsChildOf(hero)).Select(hit =>
                    {
                        var walker = hit.collider.GetComponentInParent<CityPedestrianActor>();
                        return walker == null ? $"{hit.collider.transform.parent?.name}/{hit.collider.name} hit {hit.distance:F2}m" :
                            $"{walker.name} {walker.MotionState} at {walker.Position:F2} yielding {walker.IsYielding}";
                    }).Distinct()));
            }
        }

        [UnityTest]
        public IEnumerator LiveCity_AllBusPlatformsHaveClearSheltersAndDoorApproaches()
        {
            yield return LoadProductionCity();
            Physics.SyncTransforms();
            var obstacles = new HashSet<string>();
            var sharedSurfaces = new Dictionary<string, Material>();
            var shelterParts = new[]
            {
                ("Imported Route 01 Shelter Shell Residential", "BusShelterPaintedMetal"),
                ("Imported Route 01 Shelter Shell Fixture", "CityPoiPaintedMetal"),
                ("Imported Route 01 Shelter Shell Timber", "CityPoiTimber"),
                ("Imported Route 01 Pole Shell Fixture", "CityPoiPaintedMetal"),
                ("Imported Route 01 Pole Shell Street", "ArchEnamel"),
                ("Imported Route 01 Pole Shell Residential", "ArchEnamel")
            };
            int index = 0;
            bool capture = System.Environment.GetEnvironmentVariable("BAR_PROMENADE_CAPTURE_BUS_PLATFORMS") == "1";
            if (capture)
            {
                double untilNoon = (2160d - GameSessionState.GameTimeOfDayMinutes) % 1440d;
                GameSessionState.AdvanceGameTime((float)(untilNoon / GameTimeState.GameMinutesPerRealSecond));
            }
            foreach (var platform in CityBusNetworkFurniture.Platforms(city.BusPlans))
            {
                var stop = platform.stop;
                Transform structure = city.BusStops.transform.GetChild(index++);
                string code = $"{platform.line.Number:00}-{stop.SequenceIndex + 1:00}";
                foreach (var (part, surface) in shelterParts)
                {
                    var mesh = structure.Find(part);
                    Assert.That(mesh, Is.Not.Null, code + " " + part);
                    var material = mesh.GetComponent<MeshRenderer>().sharedMaterial;
                    Assert.That(material, Is.Not.Null, part);
                    Assert.That(material.shader.name, Is.EqualTo("Bar Promenade/City Surface"), part);
                    Assert.That(material.GetTexture("_BaseMap")?.name, Is.EqualTo(surface + "Albedo"), part);
                    Assert.That(material.GetTexture("_SceneMaterialResponse")?.name, Is.EqualTo(surface + "Response"), part);
                    Assert.That(material.GetFloat("_SceneResponseStrength"), Is.EqualTo(1f), part);
                    if (sharedSurfaces.TryGetValue(part, out var first))
                        Assert.That(material, Is.SameAs(first), code + " " + part);
                    else sharedSurfaces.Add(part, material);
                }
                string expectedPlate = code;
                if (stop.TransferNumber > 0)
                {
                    var otherLine = city.BusPlans.Single(line => line.Number == stop.TransferNumber);
                    var otherStop = otherLine.Stops.Single(other => other.TargetId == stop.TargetId);
                    string otherCode = $"{otherLine.Number:00}-{otherStop.SequenceIndex + 1:00}";
                    expectedPlate += (platform.shared ? "\n" : "\n> ") + otherCode;
                }
                var faces = structure.GetComponentsInChildren<TMPro.TextMeshPro>();
                Assert.That(faces.Length, Is.EqualTo(2), code);
                Assert.That(Vector3.Dot(faces[0].transform.forward, faces[1].transform.forward), Is.LessThan(-.99f), code);
                foreach (var face in faces)
                {
                    face.ForceMeshUpdate();
                    Assert.That(face.text, Is.EqualTo(expectedPlate), stop.Id);
                    Assert.That(face.textBounds.size.x, Is.InRange(.01f, face.rectTransform.rect.width + .01f), code);
                    Assert.That(face.textBounds.size.y, Is.InRange(.01f, face.rectTransform.rect.height + .01f), code);
                }
                int mapIndex = city.Map.BusOverlay.Stops.ToList().FindIndex(marker => marker.StableId == stop.Id);
                Assert.That(mapIndex, Is.GreaterThanOrEqualTo(0), code);
                Assert.That(city.Map.GetBusStopLabel(mapIndex), Does.StartWith(code), stop.Id);
                Vector3 f = Vector3.ProjectOnPlane(stop.Forward, Vector3.up).normalized;
                Vector3 road = stop.RoadsideForward.normalized;
                // Ignore the platform's own colliders, but include every neighbouring
                // platform, signal, fence, tree and placed decoration in the live scene.
                Check("shelter", stop.ShelterPosition + f * -2.25f, new Vector3(.78f, 1.18f, 2.85f));
                Vector3 wait = CityBusStopWaitPlanner.StandingPosition(platform.line, stop, city.Layout);
                Check("boarding strip", wait, new Vector3(.45f, .83f, 4.2f));
                // A recessed shelter also needs a clear walk from its front to the kerb.
                Vector3 front = stop.ShelterPosition + road * .85f;
                float recess = Vector3.Dot(wait - front, road);
                if (recess > .5f)
                    Check("walk from shelter to pavement", (front + wait) * .5f + f * .9f,
                        new Vector3(recess * .5f, .83f, .46f));
                Vector3 target = stop.ShelterPosition - f * 1.6f + Vector3.up * 1.15f;
                Vector3 eye = target + road * 8.2f + f * 2f + Vector3.up * 2.2f;
                city.Player.Motor.Teleport(eye + road * 4f);
                yield return null;
                var lighting = city.Night.Atmosphere;
                lighting.RefreshImmediate();
                Assert.That(lighting.RealtimeLightCount, Is.LessThanOrEqualTo(CityNightAtmosphere.MaximumRealtimeLights));
                var spots = lighting.StreetLightPool.Where(light => light.enabled && light.type == LightType.Spot && light.range < 1f &&
                    (light.transform.position - faces[0].transform.position).sqrMagnitude < .9f).ToArray();
                Assert.That(spots.Length, Is.EqualTo(2), code);
                foreach (var face in faces)
                    Assert.That(spots.Any(light => light.intensity > 0f && light.shadows == LightShadows.None &&
                        light.range < 1f && Vector3.Dot(light.transform.forward,
                            (face.transform.position - light.transform.position).normalized) > .999f), Is.True, code);
                if (capture && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
                {
                    var previous = new Pose(city.Camera.transform.position, city.Camera.transform.rotation);
                    city.Camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                    AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "BusPlatforms",
                        $"{platform.line.Number:00}-{stop.SequenceIndex + 1:00}", 800, 450);
                    if (platform.ordinal == 2)
                    {
                        var bench = CityBusStopWorldBuilder.DescribeShelterBenchSeat(stop);
                        Vector3 detail = bench.SeatTopCenter + structure.right * .55f + Vector3.up * .32f;
                        Vector3 detailEye = detail + road * 2.05f + structure.right * .85f + Vector3.up * .48f;
                        city.Camera.transform.SetPositionAndRotation(detailEye, Quaternion.LookRotation(detail - detailEye));
                        AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "BusPlatforms", code + "-surfaces", 1280, 720);
                    }
                    if (platform.line.Number == 3 && platform.ordinal <= 2)
                    {
                        Vector3 plaque = faces[0].transform.position;
                        Vector3 portraitEye = plaque + road * 2.2f + f * .35f;
                        city.Camera.transform.SetPositionAndRotation(portraitEye, Quaternion.LookRotation(plaque - portraitEye));
                        AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "BusPlatforms", code + "-plate", 800, 450);
                        if (platform.ordinal == 1)
                        {
                            foreach (var spot in spots) spot.enabled = false;
                            try { AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "BusPlatforms", code + "-unlit", 800, 450); }
                            finally { foreach (var spot in spots) spot.enabled = true; }
                        }
                    }
                    city.Camera.transform.SetPositionAndRotation(previous.position, previous.rotation);
                }

                void Check(string area, Vector3 basePoint, Vector3 half)
                {
                    var colliders = Physics.OverlapBox(basePoint + Vector3.up * (half.y + .32f), half,
                        Quaternion.LookRotation(f), Physics.AllLayers, QueryTriggerInteraction.Ignore);
                    foreach (Collider hit in colliders)
                    {
                        if (hit.transform.IsChildOf(structure) ||
                            hit.transform.IsChildOf(city.Player.GameObject.transform) ||
                            hit.GetComponentInParent<CityPedestrianActor>() != null ||
                            hit.GetComponentInParent<CityBusActor>() != null) continue;
                        string path = hit.name;
                        for (Transform par = hit.transform.parent; par != null && par != city.transform; par = par.parent)
                            path = par.name + "/" + path;
                        obstacles.Add($"{stop.Id} {area} at {basePoint}: {path}");
                    }
                }
            }
            Assert.That(index, Is.EqualTo(city.BusStops.transform.childCount));
            Assert.That(obstacles, Is.Empty, string.Join("\n", obstacles));
            if (capture && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                Assert.That(city.Map.Open(), Is.True);
                try
                {
                    yield return null;
                    // Native IMGUI screenshots need a visible Game view. The live
                    // marker/title checks above also run in batch mode.
                    if (!Application.isBatchMode)
                        yield return CityMapLazyAreaTabsPlayModeTests.CaptureMapUi("bus-stop-codes");
                }
                finally { city.Map.Close(); }
            }
        }

        // Both coast-to-summit round trips use the normal 2.6 m/s motor and
        // include the city approaches, six hairpins per side and two stair loops.
        [UnityTest, Timeout(900000)]
        public IEnumerator LiveCity_CitywideSlopesKeepSupportedRoutesAndArrivals()
        {
            yield return LoadProductionCity();
            CityLayout layout = city.Layout;
            CityTerraceDistrictPlan district = CityTerraceDistrictPlan.ForLayout(layout);
            var hills = CityHillsideStreetPlan.ForLayout(layout);
            Assert.That(hills.Routes.Select(route => route.Bank), Is.EquivalentTo(new[] {
                CityTerraceTransportBank.East, CityTerraceTransportBank.West }));
            var failures = new List<string>();
            foreach (CityHillsideStreetRoute route in hills.Routes)
            {
                Check(() => AssertHillsideRoadSupport(route));
                Check(() => AssertMappedSummit(route));
                Check(() => AssertHouseApproaches(layout, route));
            }
            Check(() => AssertShortcutMapAndDestinations(district));
            Check(() => AssertEasternThroughStreet(layout));
            Assert.That(failures, Is.Empty, string.Join("\n", failures));

            void Check(System.Action validate)
            {
                try { validate(); }
                catch (AssertionException failure) { failures.Add(failure.Message); }
            }

            isolatedHeroCapsule = city.Player.Motor.GetComponent<CharacterController>();
            city.Player.Motor.SetInputEnabled(false);
            city.Player.Interactor.SetInputEnabled(false);
            city.Camera.GetComponent<PlayerCameraFollow>().SetOrbitInputEnabled(false);
            // Simulate every motor/physics/animation step; render the long round
            // trips less frequently. The native captures render the real camera.
            int previousInterval = OnDemandRendering.renderFrameInterval;
            try
            {
                OnDemandRendering.renderFrameInterval = 12;
                Time.captureDeltaTime = .10f;
                foreach (CityHillsideStreetRoute route in hills.Routes.OrderByDescending(road => road.Bank))
                {
                    IReadOnlyList<Vector3> walk = route.SidewalkPath;
                    bool east = route.Bank == CityTerraceTransportBank.East;
                    Vector3 coast = layout.GetNodeWorldPosition(route.CoastApproachNodes[0]);
                    var graph = city.PedestrianPlan;
                    int start = NearestSidewalkNode(graph, coast, 8f);
                    int mouth = NearestSidewalkNode(graph, walk[0], 2.5f);
                    int summit = NearestSidewalkNode(graph, route.SummitArrivalWorld, 2.5f);
                    List<Vector3> approach;
                    if (east)
                    {
                        // NPC connectivity is a graph property; the player's
                        // published approach is the measured coastal street chain.
                        int north = Enumerable.Range(0, graph.Nodes.Count).Single(i =>
                            graph.Nodes[i].Id == "lane:13:10:13:11:side:-1:b");
                        int south = Enumerable.Range(0, graph.Nodes.Count).Single(i =>
                            graph.Nodes[i].Id == "lane:13:7:13:8:side:-1:b");
                        StreetWalk(graph, start, north);
                        StreetWalk(graph, north, south);
                        StreetWalk(graph, south, mouth);
                        approach = SurveyedEasternPavement(route);
                    }
                    else
                    {
                        approach = StreetWalk(graph, start, mouth);
                        approach.Add(walk[0]);
                    }
                    // The summit must be reachable by the live NPC graph as well.
                    Assert.That(StreetWalk(graph, mouth, summit).Count, Is.GreaterThan(2));
                    Assert.That(Vector3.Distance(walk[walk.Count - 1], route.SummitArrivalWorld), Is.LessThan(.05f));
                    float gain = route.SummitArrivalWorld.y - approach[0].y;
                    // The published section is in centimetres; both ends are actual sidewalk tops.
                    Assert.That(Mathf.RoundToInt(gain * 100f), Is.InRange(3500, 4500), route.Bank.ToString());

                    city.Player.Motor.Teleport(approach[0] + Vector3.up * PlayerFactory.GroundedRootOffset);
                    IsolateAmbientCollisionPairs();
                    for (int i = 0; i < 4; i++) yield return null;
                    string bank = east ? "east" : "west";
                    yield return WalkCityRoute(approach, false, bank + "-shore-to-hill", district);
                    yield return WalkPastLowerFacades(route, district, bank);
                    Assert.That(city.Player.Motor.transform.position.y,
                        Is.EqualTo(route.SummitArrivalWorld.y + PlayerFactory.GroundedRootOffset).Within(.20f));
                    yield return CapturePlayerView("citywide-" + bank + "-summit", walk[walk.Count - 2] + Vector3.up);
                    yield return WalkHillsideDown(route, district);
                    yield return WalkCityRoute(approach, true, bank + "-hill-to-shore", district);
                    Assert.That(Vector3.Distance(city.Player.Motor.transform.position,
                        approach[0] + Vector3.up * PlayerFactory.GroundedRootOffset), Is.LessThan(.20f));
                    Debug.Log($"Citywide {bank}: shore {approach[0].y:F2}, summit {route.SummitArrivalWorld.y:F2}, " +
                        $"rise {route.SummitArrivalWorld.y - approach[0].y:F2} m; one-way walk {WalkLength(approach) + WalkLength(walk):F1} m.");
                }
            }
            finally { OnDemandRendering.renderFrameInterval = previousInterval; }
#if UNITY_EDITOR
            System.Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                .GetMethod("ExportCurrent", System.Type.EmptyTypes).Invoke(null, null);
#endif
        }

        // Exercise the new district directly instead of replaying the unchanged coast.
        [UnityTest, Timeout(360000)]
        public IEnumerator LiveCity_UpperQuarterJoinsBothSlopesThroughTheLitCourt()
        {
            GameSessionState.TryStartGameTimeAt(12 * 60);
            yield return LoadProductionCity();
            var layout = city.Layout;
#if UNITY_EDITOR
            if (System.Environment.GetEnvironmentVariable("BARGAME_EXPORT_UPPER_MAP") == "1")
                System.Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                    .GetMethod("ExportCurrent", System.Type.EmptyTypes).Invoke(null, null);
#endif
            var area = CityTerraceDistrictPlan.ForLayout(layout);
            var quarter = CityHillsideQuarterPlan.ForLayout(layout);
            var access = CityHillsideAccessPlan.ForLayout(layout);
            var housing = CityHillsideNeighborhoodPlan.ForLayout(layout);
            var graph = city.PedestrianPlan;
            var ground = new CityMapCityTeleportGround(layout);
            var missingTreeViews = new List<string>();
            Assert.That(CityPawnshopPlan.TryCreate(layout, out var pawnshop), Is.True);
            Vector3 forecourtCentre = pawnshop.DoorWorld + pawnshop.DoorOutward * 2.1f;
            Vector3 acrossApproach = Vector3.Cross(Vector3.up, pawnshop.DoorOutward);
            Vector3 approachEdge = forecourtCentre + acrossApproach * (CityPawnshopPlan.ApproachWidth * .5f);
            var hillside = CityHillsideGroundPlan.ForLayout(layout);
            Assert.That(hillside.TrySampleTop(new Vector2(forecourtCentre.x, forecourtCentre.z), out float approachY), Is.True);
            forecourtCentre.y = approachEdge.y = approachY;
            Physics.SyncTransforms();
            Assert.That(Physics.Raycast(forecourtCentre + Vector3.up * .3f, Vector3.down, out var forecourt, .6f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True);
            Assert.That(forecourt.point.y, Is.EqualTo(approachY).Within(.02f));
            AssertGroundCoating(forecourt, GroundSurfaceKind.Paving, FootstepGroundKind.Stone);
            Assert.That(forecourt.collider.GetComponent<Renderer>().sharedMaterials.Any(
                material => material.IsKeywordEnabled("_CITY_GROUND_SEAMS")), Is.True,
                "The live forecourt must receive the same field as its neighbouring soil.");
            Assert.That(quarter.Links.Select(l => l.Id), Is.EquivalentTo(new[] { "court", "summit" }));
            Assert.That(housing.AllBuildings.All(h => h.Lot.District == CityDistrictKind.Residential), Is.True);
            Assert.That(housing.SharedCourtBuildings.Count, Is.GreaterThan(0));
            foreach (var road in CityHillsideStreetPlan.ForLayout(layout).Routes)
            {
                Assert.That(access.Shortcuts.Where(s => s.Bank == road.Bank).Select(s => s.LowerLane),
                    Is.EquivalentTo(Enumerable.Range(0, CityHillsideStreetRoute.LaneCount - 1)));
                AssertMappedSummit(road);
            }
            foreach (var stair in access.AllStairs)
            {
                for (int i = 1; i < stair.RouteWorld.Count; i++)
                {
                    var a = stair.RouteWorld[i - 1]; var b = stair.RouteWorld[i];
                    for (int j = 0, n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b))); j <= n; j++)
                    {
                        var foot = Vector3.Lerp(a, b, j / (float)n);
                        Assert.That(city.World.WalkableArea.Contains(foot, .3f), Is.True, "Stair walk mask " + foot);
                        AssertHillsideCollider(new Vector2(foot.x, foot.z), foot.y);
                    }
                }
            }
            foreach (var link in quarter.Links)
            {
                for (int i = 1; i < link.Centreline.Count; i++)
                {
                    var a = link.Centreline[i - 1]; var b = link.Centreline[i];
                    for (int j = 0, n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b))); j <= n; j++)
                    {
                        var p = Vector3.Lerp(a, b, j / (float)n);
                        // A ray on the exact diagonal of a cooked road patch can
                        // miss both triangles. Probe both sides of the centreline.
                        AssertHillsideCollider(new Vector2(p.x, p.z - .002f), p.y);
                        AssertHillsideCollider(new Vector2(p.x, p.z + .002f), p.y);
                    }
                }
                if (link.Id == "court")
                {
                    var streets = CityStreetSupportPlan.ForLayout(layout);
                    foreach (var joint in new[] { link.WestGateWorld + Vector3.right * 4f, link.EastGateWorld + Vector3.left * 4f })
                    {
                        Assert.That(streets.TrySampleTop(new Vector2(joint.x, joint.z), out _, out var surface), Is.True);
                        Assert.That(surface, Is.EqualTo(FootstepGroundKind.Concrete), "A kerb still closes the T-junction.");
                    }
                }
                Assert.That(city.Night.Plan.StreetLamps.Any(l => link.Bounds.Contains(new Vector2(l.Position.x,l.Position.z))),
                    Is.True, "No municipal lighting on " + link.Id);
            }
            var courtMarker = city.Map.ActiveMapPoints.Single(p => p.StableId == "city:poi:upper-shared-court");
            Assert.That(ground.TryClampArrival(courtMarker.WorldPosition, out var safe), Is.True);
            Assert.That(Vector3.Distance(courtMarker.WorldPosition, safe), Is.LessThan(.06f));
            Assert.That(quarter.Bridges.Select(b => b.Id), Is.EquivalentTo(quarter.Links.Select(l => l.Id)));
            foreach (var bridge in quarter.Bridges)
            {
                Vector3 deck = bridge.DeckCenterWorld;
                AssertHillsideCollider(new Vector2(deck.x, deck.z), deck.y);
                Assert.That(Physics.Raycast(deck - Vector3.up * 3f, Vector3.down, out var rock,
                    50f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True, bridge.Id);
                Assert.That(deck.y - rock.point.y, Is.GreaterThan(8f), "There must be open space under " + bridge.Id);
                Assert.That(rock.point.y, Is.EqualTo(bridge.BottomWorld.y).Within(.25f));
                AssertGroundCoating(rock, GroundSurfaceKind.Gravel, FootstepGroundKind.Stone);
                // Step off the deck footprint toward the middle of the cut.
                var beside = new Vector2(deck.x, deck.z + (bridge.Id == "court" ? -1f : 1f) *
                    (bridge.DeckBounds.height * .5f + 1.5f));
                Assert.That(ground.TryResolveStandingPosition(beside, out _), Is.False, "Map arrival over open rock.");
                var marker = city.Map.ActiveMapPoints.Single(p => p.StableId == "city:poi:upper-" + bridge.Id + "-bridge");
                Assert.That(ground.TryClampArrival(marker.WorldPosition, out safe), Is.True);
                Assert.That(Vector3.Distance(marker.WorldPosition, safe), Is.LessThan(.06f));
            }

            city.Player.Motor.SetInputEnabled(false);
            city.Player.Interactor.SetInputEnabled(false);
            city.Camera.GetComponent<PlayerCameraFollow>().SetOrbitInputEnabled(false);
            isolatedHeroCapsule = city.Player.Motor.GetComponent<CharacterController>();
            int interval = OnDemandRendering.renderFrameInterval;
            try
            {
                Time.captureDeltaTime = .1f;
                OnDemandRendering.renderFrameInterval = 24;
                yield return Stand(forecourtCentre + pawnshop.DoorOutward, approachEdge);
                yield return CaptureSurfaceWeather("upper-quarter-pawnshop-joint-dry", approachEdge, 0f,
                    camera => CapturePavingBaseline(camera, "dry"));
                yield return CaptureSurfaceWeather("upper-quarter-pawnshop-joint-rain", approachEdge, 1f,
                    camera => CapturePavingBaseline(camera, "rain"));
                var benchTerrace = CityPawnshopBenchTerracePlan.ForLayout(layout);
                yield return Stand(benchTerrace.RouteWorld[0], benchTerrace.UpperLanding.Center);
                yield return CapturePlayerView("upper-quarter-bench-landing", benchTerrace.UpperLanding.Center);
                foreach (var road in CityHillsideStreetPlan.ForLayout(layout).Routes)
                {
                    bool westBank = road.Bank == CityTerraceTransportBank.West;
                    var home = housing.Buildings.Where(h => h.Bank == road.Bank &&
                            h.Lot.BuildingVariant == (westBank ? 0 : 2))
                        .OrderBy(h => Mathf.Abs(road.SummitArrivalWorld.y - h.DoorWorld.y - 4f)).First();
                    Vector3 vantage = home.StreetContactWorld;
                    float x = Mathf.MoveTowards(vantage.x, (road.LaneMinX + road.LaneMaxX) * .5f, 7.5f);
                    float z = vantage.z + Mathf.Sign(vantage.z - home.DoorWorld.z) * 6f;
                    if (road.TrySampleTop(new Vector2(x,z),out float y,out _)) vantage = new Vector3(x,y,z);
                    yield return Stand(vantage, home.DoorWorld);
                    yield return CapturePlayerView("upper-quarter-" + road.Bank, home.DoorWorld + Vector3.up * 4.3f);
                }
                var shared = access.SharedCourt.RouteWorld;
                var court = quarter.Links.Single(l => l.Id == "court");
                var summit = quarter.Links.Single(l => l.Id == "summit");
                var west = summit.NorthSidewalk[0];
                var east = summit.NorthSidewalk[summit.NorthSidewalk.Count-1];
                var summitHome = layout.SummitHome;
                var bus = city.BusPlans.Single(line => line.Number == 3);
                var stop = bus.Stops.Single(s => s.SequenceIndex == 3);
                Vector3 wait = CityBusStopWaitPlanner.StandingPosition(bus, stop, layout);
                Vector3 door = summitHome.DoorWorld + Vector3.up * 1.3f;
                Debug.Log($"Ridge approach 03-04 {stop.ShelterPosition:F2}, waiting {wait:F2}, " +
                    $"home {summitHome.ThresholdWorld:F2}, pole to threshold {Vector3.Distance(stop.ShelterPosition, summitHome.ThresholdWorld):F2}m.");
                yield return Stand(wait, door);
                yield return CapturePlayerView("upper-quarter-stop-to-home", door, camera => CheckTreeView(camera, "03-04"));
                var homeWall = city.World.Root.transform.Find("Residential terrace edges").Cast<Transform>()
                    .Where(t => t.name == "RidgeRetainingEnd")
                    .OrderBy(t => Vector3.Distance(t.position, summitHome.DoorWorld)).First();
                float along = Mathf.Clamp(homeWall.position.x - 4f, summit.Bounds.xMin + 2f, summit.Bounds.xMax - 2f);
                var acrossStreet = new Vector3(along, summit.PavementTopAt(along), summit.WestGateWorld.z + 4f);
                Vector3 frontage = Vector3.Lerp(homeWall.position, door, .35f) + Vector3.up * .65f;
                yield return Stand(acrossStreet, frontage);
                yield return CapturePlayerView("upper-quarter-home-frontage", frontage);
                var upperBridge = quarter.Bridges.Single(b => b.Id == "summit");
                Vector3 parapet = upperBridge.WestBearingWorld + Vector3.up;
                yield return Stand(summitHome.StreetContactWorld, parapet);
                yield return CapturePlayerView("upper-quarter-home-to-abutment", parapet);
                yield return Stand(summitHome.StreetContactWorld, door);
                yield return CaptureSurfaceWeather("upper-quarter-home-entry-dry", door, 0f);
                yield return CaptureSurfaceWeather("upper-quarter-home-entry-rain", door, 1f);
                var dryingCourt = housing.SharedCourtBuildings.Single(h => h.Lot.Cell == new Vector2Int(41, -21));
                Vector3 eastEdge = city.World.Root.transform.Find("Residential terrace edges").Cast<Transform>()
                    .Where(t => t.name == "RidgeCourtEdge").OrderBy(t =>
                        Vector3.Distance(t.position, dryingCourt.Lot.Center)).First().position;
                float courtX = eastEdge.x - 4f;
                Vector3 eastFront = Vector3.Lerp(eastEdge, dryingCourt.DoorWorld + Vector3.up * 2f, .36f);
                yield return Stand(new Vector3(courtX, court.PavementTopAt(courtX), court.WestGateWorld.z - 4f), eastFront);
                yield return CapturePlayerView("upper-quarter-east-court-edge", eastFront, camera => CheckTreeView(camera, "east court"));
                foreach (var bridge in quarter.Bridges)
                {
                    var link = bridge.Link;
                    bool high = bridge.Id == "summit";
                    float x = high ? bridge.EastBearingWorld.x - 5.8f : bridge.WestBearingWorld.x + 5.8f;
                    float side = high ? 1f : -1f;
                    var sight = new Vector3(x + side * 3.1f, link.PavementTopAt(x) - 4.3f,
                        bridge.DeckCenterWorld.z + side * 10f);
                    yield return Stand(new Vector3(x, link.PavementTopAt(x), bridge.DeckCenterWorld.z + side * 4f), sight);
                    yield return CapturePlayerView("upper-quarter-bridge-" + bridge.Id, sight);
                    if (!high)
                    {
                        float approach = bridge.EastBearingWorld.x + 2.5f;
                        var truss = new Vector3(approach - 10f,link.RoadTopAt(approach) + 1.5f,bridge.DeckCenterWorld.z - 4f);
                        yield return Stand(new Vector3(approach,link.PavementTopAt(approach),bridge.DeckCenterWorld.z + 4f),truss);
                        yield return CapturePlayerView("upper-quarter-bridge-approach", truss);
                    }
                }
                // The two dry connections belong to the same live pedestrian graph.
                var loop = new[] { west, east, court.SouthSidewalk[court.SouthSidewalk.Count-1], court.SouthSidewalk[0], west };
                for (int i=1; i<loop.Length; i++)
                    Assert.That(StreetWalk(graph, NearestSidewalkNode(graph, loop[i-1], 3f),
                        NearestSidewalkNode(graph, loop[i], 3f)).Count, Is.GreaterThan(1));
                yield return Stand(shared[0], shared[1]);
                yield return CapturePlayerView("upper-quarter-passage-day", shared[1] + Vector3.up * 1.6f);
                yield return WalkCityRoute(shared, false, "upper-quarter-through-house", area);
                yield return Stand(court.SouthSidewalk[0]);
                yield return WalkCityRoute(court.SouthSidewalk, false, "upper-quarter-court-west-to-east", area);
                // Show the actual lighting clock at night on the same route.
                GameSessionState.AdvanceGameTime((float)((22 * 60 - GameSessionState.GameTimeOfDayMinutes + 1440) % 1440 /
                    GameTimeState.GameMinutesPerRealSecond));
                city.DayNight.ApplyCurrentTime(true);
                yield return Stand(shared[shared.Count-1]);
                yield return WalkCityRoute(shared, true, "upper-quarter-down-at-night", area);
                yield return CapturePlayerView("upper-quarter-passage-night", shared[1] + Vector3.up * 1.6f);
                yield return Stand(west);
                yield return WalkCityRoute(summit.NorthSidewalk, false, "upper-quarter-west-to-east", area);
                var crestBridge = quarter.Bridges.Single(b => b.Id == "summit");
                var centre = crestBridge.DeckCenterWorld;
                yield return Stand(new Vector3(centre.x,summit.PavementTopAt(centre.x),centre.z + 3.85f),
                    centre + new Vector3(4f,0f,12f));
                yield return CapturePlayerView("upper-quarter-bridge-night", centre + new Vector3(4f,-5f,12f));
            }
            finally { OnDemandRendering.renderFrameInterval = interval; }
            AssertPlacedRidgeDetails(layout, quarter, housing);
            Assert.That(missingTreeViews, Is.Empty, "A mid-distance yard tree should be framed from each public approach.");
            // The west side of Home crosses the seam from the saddle onto the bank grid.
            Rect foundation = layout.SummitHome.FoundationBounds;
            Vector3 plinth = new Vector3(foundation.xMin - .7f, layout.SummitHome.ThresholdWorld.y + 8f, foundation.center.y);
            var bankGround = city.World.Root.transform.Find("Stepped Residences/Terraced ground and courts")
                .GetComponent<MeshCollider>();
            Assert.That(bankGround.Raycast(new Ray(plinth, Vector3.down), out var band, 25f), Is.True, plinth.ToString());
            AssertGroundCoating(band, GroundSurfaceKind.Asphalt, FootstepGroundKind.Concrete);
            var upper = quarter.Links.Single(l => l.Id == "summit");
            float vergeX = Mathf.Lerp(upper.WestGateWorld.x + 2f, foundation.xMin - CityBuildingPerimeterPlan.Width, .5f);
            AssertVerge(3.3f, GroundSurfaceKind.Lawn, FootstepGroundKind.Grass);
            AssertVerge(.24f, GroundSurfaceKind.Gravel, FootstepGroundKind.Stone);

            void AssertVerge(float depth, GroundSurfaceKind coating, FootstepGroundKind step)
            {
                var origin = new Vector3(vergeX, plinth.y, upper.Bounds.yMin - depth);
                Assert.That(bankGround.Raycast(new Ray(origin, Vector3.down), out var surface, 25f), Is.True, origin.ToString());
                AssertGroundCoating(surface, coating, step);
            }

            void CheckTreeView(Camera camera, string view)
            {
                var trees = city.World.Root.transform.Find("Residential terrace edges").Cast<Transform>()
                    .Where(t => t.name == "RidgeYardTree");
                bool framed = trees.Any(tree =>
                {
                    Bounds crown = tree.GetComponentsInChildren<MeshRenderer>().OrderByDescending(r => r.bounds.max.y).First().bounds;
                    Vector3 screen = camera.WorldToViewportPoint(crown.center);
                    return screen.z > 2f && screen.z < 60f && screen.x > .01f && screen.x < .99f &&
                        screen.y > 0f && screen.y < 1f;
                });
                if (!framed) missingTreeViews.Add(view);
            }

            void AssertGroundCoating(RaycastHit hit, GroundSurfaceKind coating, FootstepGroundKind step)
            {
                var mesh = ((MeshCollider)hit.collider).sharedMesh;
                int index = hit.triangleIndex * 3;
                int slot = Enumerable.Range(0, mesh.subMeshCount).Single(i =>
                    index >= mesh.GetIndexStart(i) && index < mesh.GetIndexStart(i) + mesh.GetIndexCount(i));
                var properties = new MaterialPropertyBlock();
                hit.collider.GetComponent<Renderer>().GetPropertyBlock(properties, slot);
                Assert.That(properties.HasFloat("_GroundKind"), Is.True, hit.point.ToString());
                Assert.That(properties.GetFloat("_GroundKind"), Is.EqualTo((float)coating), hit.point.ToString());
                Assert.That(hit.collider.GetComponent<FootstepGround>().ResolveKind(hit.triangleIndex), Is.EqualTo(step));
            }

            void CapturePavingBaseline(Camera camera, string weather)
            {
                int preview = Shader.PropertyToID("_CityJointPreviewOff");
                float saved = Shader.GetGlobalFloat(preview);
                Shader.SetGlobalFloat(preview, 1f);
                try
                {
                    AreaCaptureFixture.CaptureCurrentCamera(camera, "CityTerracePilot",
                        "upper-quarter-pawnshop-baseline-" + weather, 1280, 720);
                }
                finally { Shader.SetGlobalFloat(preview, saved); }
            }

            IEnumerator Stand(Vector3 floor, Vector3? facing = null)
            {
                city.Player.Motor.Teleport(floor + Vector3.up * PlayerFactory.GroundedRootOffset);
                if (facing.HasValue)
                    city.Player.Motor.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(facing.Value - floor, Vector3.up));
                for (int i = 0; i < 4; i++) yield return null;
            }
        }

        private void AssertPlacedRidgeDetails(CityLayout layout, CityHillsideQuarterPlan quarter,
            CityHillsideNeighborhoodPlan housing)
        {
            Transform root = city.World.Root.transform.Find("Residential terrace edges");
            var placed = root.Cast<Transform>().ToList();
            var sites = CityHillsideSiteDetails.ForLayout(layout);
            Assert.That(placed.Count, Is.EqualTo(sites.Count));
            foreach (var site in sites)
                Assert.That(placed.Any(t => t.name == "Ridge" + site.Part &&
                    Vector3.Distance(t.position, site.Origin) < .01f && Quaternion.Angle(t.rotation, site.Rotation) < .1f),
                    Is.True, "Missing placed " + site.Part + " at " + site.Origin);

            var home = layout.SummitHome;
            var store = placed.Single(t => t.name == "RidgeYardShelter");
            Assert.That(store.GetComponent<BoxCollider>().enabled, Is.True);
            foreach (var tree in placed.Where(t => t.name == "RidgeYardTree"))
            {
                float top = tree.GetComponentsInChildren<MeshRenderer>().Max(r => r.bounds.max.y);
                Assert.That(top - tree.position.y, Is.InRange(3.5f, 7f), "Yard tree height in imported metres.");
                Assert.That(tree.GetComponent<BoxCollider>().enabled, Is.True);
            }
            var stop = city.BusPlans.Single(line => line.Number == 3).Stops.Single(s => s.SequenceIndex == 3);
            var verge = placed.Single(t => t.name == "RidgeGardenVerge" &&
                t.position.x > stop.ShelterPosition.x && t.position.x < home.ThresholdWorld.x);
            var crown = verge.GetComponentsInChildren<MeshRenderer>().Single(r => r.name.Contains("__Foliage"));
            Assert.That(crown.bounds.max.y - verge.position.y, Is.InRange(1.25f, 1.8f), "Visible shrub in world metres.");
            Rect vergeBounds = sites.Single(s => s.Origin == verge.position).Bounds;
            Rect boarding = CityBusPlatformClearance.PassengerAccess(stop.Position, stop.Forward,
                stop.ShelterPosition, stop.RoadsideForward);
            Assert.That(vergeBounds.Overlaps(boarding), Is.False);
            Assert.That(vergeBounds.Overlaps(CityBusStopWorldBuilder.DescribeFurnitureFootprint(stop)), Is.False);
            Transform retaining = Nearest("RidgeRetainingEnd", home.DoorWorld, 15f);
            Nearest("RidgePlinthRepair", home.ThresholdWorld, 5f);
            var court = housing.SharedCourtBuildings.Single(h => h.Lot.Cell == new Vector2Int(41, -21));
            Nearest("RidgeCourtEdge", court.Lot.Center, 16f);
            var eastEdges = placed.Where(t => t.name == "RidgeCourtEdge").ToList();
            Assert.That(housing.Buildings.Any(h => h.Bank == CityTerraceTransportBank.East &&
                eastEdges.Any(edge => Vector3.Distance(edge.position, h.Lot.Center) < 16f)), Is.True,
                "The eastern slope needs its own domestic frontage beyond the shared court.");
            var meshes = retaining.GetComponentsInChildren<MeshRenderer>();
            Bounds wall = meshes.Single(r => r.name.Contains("__ConcreteWall")).bounds;
            Bounds bank = meshes.Single(r => r.name.Contains("__Soil")).bounds;
            Assert.That(wall.size.x, Is.InRange(5.8f, 6.1f), "West retaining wall in imported metres.");
            Assert.That(wall.size.y, Is.InRange(1.5f, 1.9f));
            Assert.That(bank.size.z, Is.GreaterThan(2f), "Filled bank must meet the back of the wall.");
            Assert.That(bank.max.y, Is.GreaterThan(wall.min.y + 1f));
            var stairs = CityHillsideAccessPlan.ForLayout(layout).SharedCourt;
            var flight = stairs.Flights[0];
            Nearest("RidgeRetainingEnd", flight.Start, 5f);
            var passage = city.World.Root.transform.Find("Hillside stairs " + stairs.Id + "/" + stairs.Floors[0].Id);
            foreach (var strip in new[] { "Street threshold", "Covered passage paving", "Courtyard threshold" })
            {
                var tile = passage.Find(strip + " 0");
                Assert.That(tile, Is.Not.Null, strip);
                var surfaces = tile.GetComponentsInChildren<MeshRenderer>();
                var properties = new MaterialPropertyBlock();
                bool foundConcrete = false;
                foreach (var surface in surfaces)
                {
                    surface.GetPropertyBlock(properties, 0);
                    if (properties.isEmpty) surface.GetPropertyBlock(properties);
                    if (!properties.GetTexture("_GroundResponse")) continue;
                    Assert.That(properties.GetFloat("_GroundCityClimate"), Is.EqualTo(
                        strip == "Covered passage paving" ? 0f : 1f), strip);
                    foundConcrete = true;
                }
                Assert.That(foundConcrete, Is.True, strip + " requires a textured ground receiver.");
            }
            var terrain = city.World.Root.transform.Find("Shared residential hillside").GetComponent<MeshCollider>();
            foreach (var bridge in quarter.Bridges)
            {
                Transform ledge = Nearest("RidgeBearingTalus", bridge.Id == "summit" ?
                    bridge.WestBearingWorld : bridge.EastBearingWorld, 18f);
                Assert.That(terrain.Raycast(new Ray(ledge.position + Vector3.up * 4f, Vector3.down), out var contact, 8f), Is.True);
                Assert.That(Mathf.Abs(contact.point.y - ledge.position.y), Is.LessThan(.4f), bridge.Id + " rock contact");
                var shelf = ledge.GetComponentInChildren<MeshRenderer>().bounds;
                Assert.That(shelf.min.y, Is.LessThan(contact.point.y));
                Assert.That(shelf.max.y, Is.GreaterThan(contact.point.y + .1f));
            }

            Transform Nearest(string part, Vector3 target, float radius)
            {
                Transform match = placed.Where(t => t.name == part).OrderBy(t => Vector3.Distance(t.position, target)).First();
                Assert.That(Vector3.Distance(match.position, target), Is.LessThan(radius), part + " near " + target);
                return match;
            }
        }

        private List<Vector3> SurveyedEasternPavement(CityHillsideStreetRoute hill)
        {
            IReadOnlyList<Vector2Int> nodes = hill.CoastApproachNodes;
            var line = new List<Vector2>();
            Vector2 previousEnd = default, previousDirection = default;
            for (int i = 1; i < nodes.Count; i++)
            {
                RoadEdge edge = new RoadEdge(nodes[i - 1], nodes[i]);
                // Canonical east-going roads have the south pavement to their
                // right; north-going roads have the east pavement to their right.
                CityRoadPath lane = city.Layout.RoadGeometry.Get(edge).Offset(3.5f);
                bool forward = edge.A == nodes[i - 1];
                CityRoadSample first = lane.SampleDistance(forward ? 0f : lane.Length);
                CityRoadSample last = lane.SampleDistance(forward ? lane.Length : 0f);
                Vector2 direction = first.Tangent * (forward ? 1f : -1f);
                if (i == 1) line.Add(first.Position);
                else
                {
                    Vector2 joint = first.Position;
                    float cross = CityRoadPolygon.Cross(previousDirection, direction);
                    if (Mathf.Abs(cross) > .01f)
                        joint = previousEnd + previousDirection *
                            (CityRoadPolygon.Cross(first.Position - previousEnd, direction) / cross);
                    line.Add(joint);
                }
                // Keep the actual curve within each block. Leave the four-metre
                // junction mouths to the mitred pavement intersection above.
                for (float d = 4f; d < lane.Length - 3.99f; d += 2f)
                    line.Add(lane.SampleDistance(forward ? d : lane.Length - d).Position);
                previousEnd = last.Position;
                previousDirection = last.Tangent * (forward ? 1f : -1f);
            }
            Vector3 mouth = hill.SidewalkPath[0];
            line.Add(new Vector2(mouth.x, previousEnd.y));
            line.Add(new Vector2(mouth.x, mouth.z));
            return ClearPavementCourse(line);
        }

        private List<Vector3> ClearPavementCourse(List<Vector2> line)
        {
            // Local public paving only. At a post, a player can step sideways
            // within this two-metre band. Test each continuous capsule movement
            // against the same street support, navigation mask and static bodies.
            var centers = new List<Vector2> { line[0] };
            for (int i = 1; i < line.Count; i++)
            {
                int steps = Mathf.CeilToInt(Vector2.Distance(centers[centers.Count - 1], line[i]) / .9f);
                Vector2 start = centers[centers.Count - 1];
                for (int j = 1; j <= steps; j++) centers.Add(Vector2.Lerp(start, line[i], j / (float)steps));
            }
            float[] offsets = { 0f, -.6f, .6f, -1.2f, 1.2f, -1.8f, 1.8f };
            var prev = new int[centers.Count, offsets.Length];
            var positions = new Vector3[centers.Count, offsets.Length];
            var costs = new float[centers.Count, offsets.Length];
            float radius = isolatedHeroCapsule.radius;
            var support = CityStreetSupportPlan.ForLayout(city.Layout);
            for (int i = 0; i < centers.Count; i++)
            {
                Vector2 tangent = (centers[Mathf.Min(centers.Count - 1, i + 1)] - centers[Mathf.Max(0, i - 1)]).normalized;
                Vector2 right = new Vector2(tangent.y, -tangent.x);
                bool any = false;
                for (int j = 0; j < offsets.Length; j++)
                {
                    costs[i, j] = float.PositiveInfinity;
                    if ((i == 0 || i == centers.Count - 1) && j != 0) continue;
                    Vector2 xz = centers[i] + right * offsets[j];
                    if (!support.TrySampleTop(xz, out float y, out _)) continue;
                    Vector3 p = new Vector3(xz.x, y, xz.y);
                    positions[i, j] = p;
                    if (!city.World.WalkableArea.Contains(p + Vector3.up * PlayerFactory.GroundedRootOffset, radius)) continue;
                    if (i == 0) { costs[i, j] = 0f; any = true; continue; }
                    for (int k = 0; k < offsets.Length; k++)
                    {
                        float cost = costs[i - 1, k] + Mathf.Abs(offsets[j] - offsets[k]) + Mathf.Abs(offsets[j]) * .15f;
                        if (cost >= costs[i, j] || Mathf.Abs(offsets[j] - offsets[k]) > .61f) continue;
                        Vector3 from = positions[i - 1, k];
                        Vector3 step = p - from;
                        float lift = radius + .18f;
                        if (Physics.CapsuleCast(from + Vector3.up * lift,
                            from + Vector3.up * (isolatedHeroCapsule.height - radius), radius,
                            step.normalized, out RaycastHit hit, step.magnitude, Physics.DefaultRaycastLayers,
                            QueryTriggerInteraction.Ignore) && !hit.transform.IsChildOf(city.Player.Motor.transform) &&
                            hit.collider.GetComponentInParent<CityPedestrianActor>() == null &&
                            hit.collider.GetComponentInParent<CityBusActor>() == null) continue;
                        costs[i, j] = cost;
                        prev[i, j] = k;
                        any = true;
                    }
                }
                Assert.That(any, Is.True, "No continuous clear pavement near surveyed east station " + i + " " + centers[i]);
            }
            var route = new List<Vector3>();
            for (int i = centers.Count - 1, k = 0; i >= 0; i--)
            {
                route.Add(positions[i, k]);
                k = prev[i, k];
            }
            route.Reverse();
            // Keep only direction and grade changes for the guided walk.
            for (int i = route.Count - 2; i > 0; i--)
                if (Vector3.Angle(route[i + 1] - route[i], route[i] - route[i - 1]) < .08f) route.RemoveAt(i);
            return route;
        }

        private IEnumerator WalkPastLowerFacades(CityHillsideStreetRoute route, CityTerraceDistrictPlan district, string bank)
        {
            var walk = route.SidewalkPath;
            int turn = -1;
            float nearest = float.PositiveInfinity;
            Vector3 target = default;
            foreach (CityHillsideBuilding house in CityHillsideNeighborhoodPlan.ForLayout(city.Layout).Buildings)
            {
                if (house.Bank != route.Bank) continue;
                for (int i = 1; i < walk.Count - 1; i++)
                {
                    Vector3 viewer = walk[i];
                    if (viewer.y < 20f || viewer.y <= house.DoorWorld.y || viewer.z >= house.StreetContactWorld.z ||
                        house.StreetContactWorld.z - viewer.z > 28f) continue;
                    Vector2 station = route.Path.Vertices[i];
                    if (station.x > route.LaneMinX + .01f && station.x < route.LaneMaxX - .01f) continue;
                    // Sight the nearest exposed wall of a lower building from a
                    // hairpin. Its actual footprint, height and street contact
                    // follow the same parcel shifts as the visible model.
                    Rect body = house.FoundationBounds;
                    Vector3 face = new Vector3(Mathf.Clamp(viewer.x, body.xMin, body.xMax),
                        house.DoorWorld.y + Mathf.Min(3f, house.Lot.Height * .5f),
                        Mathf.Clamp(viewer.z, body.yMin, body.yMax));
                    float distance = Vector3.Distance(viewer, face);
                    if (distance >= nearest) continue;
                    nearest = distance;
                    target = face;
                    turn = i;
                }
            }
            Assert.That(turn, Is.GreaterThan(0), bank + " has no upper turn overlooking the preceding street");
            yield return WalkCityRoute(walk.Take(turn + 1).ToArray(), false, bank + "-hill-up", district);
            yield return CapturePlayerView("citywide-" + bank + "-lower-facade", target);
            yield return WalkCityRoute(walk.Skip(turn).ToArray(), false, bank + "-hill-to-summit", district);
        }

        private void AssertHouseApproaches(CityLayout layout, CityHillsideStreetRoute route)
        {
            var buildings = CityHillsideNeighborhoodPlan.ForLayout(layout).Buildings.Where(h => h.Bank == route.Bank).ToList();
            Assert.That(buildings.Any(h => h.DoorWorld.y >= 35f), Is.True, route.Bank + " needs inhabited upper streets");
            foreach (CityHillsideBuilding house in buildings)
            {
                int steps = Mathf.CeilToInt(Vector3.Distance(house.StreetContactWorld, house.DoorWorld) / .3f);
                for (int i = 0; i <= steps; i++)
                {
                    Vector3 p = Vector3.Lerp(house.StreetContactWorld, house.DoorWorld, i / (float)steps);
                    AssertHillsideCollider(new Vector2(p.x, p.z), p.y);
                    Assert.That(city.World.WalkableArea.Contains(p + Vector3.up * PlayerFactory.GroundedRootOffset, .30f),
                        Is.True, route.Bank + " doorstep is disconnected from its street at " + p);
                }
            }
        }

        private IEnumerator WalkHillsideDown(CityHillsideStreetRoute road, CityTerraceDistrictPlan district)
        {
            CityHillsideShortcut stair = CityHillsideAccessPlan.ForLayout(city.Layout).Shortcuts.Single(s => s.Bank == road.Bank && s.LowerLane == CityHillsideStreetRoute.LaneCount - 2);
            var path = road.SidewalkPath;
            var stairs = stair.RouteWorld;
            Vector3 high = stairs[stairs.Count - 1];
            // Split the walked pavement at the stair's transverse station. The
            // opposite pavement on the western street is reached by crossing its
            // actual asphalt. Return via the same mouth to cover the full road too.
            int segment = -1;
            Vector3 dock = default;
            float nearest = float.PositiveInfinity;
            for (int i = 1; i < path.Count; i++)
            {
                Vector3 a = path[i - 1], b = path[i];
                if (Mathf.Abs(b.x - a.x) < 1f || high.x < Mathf.Min(a.x, b.x) || high.x > Mathf.Max(a.x, b.x)) continue;
                Vector3 candidate = Vector3.Lerp(a, b, (high.x - a.x) / (b.x - a.x));
                float distance = Vector3.Distance(candidate, high);
                if (distance >= nearest) continue;
                segment = i;
                nearest = distance;
                dock = candidate;
            }
            Assert.That(segment, Is.GreaterThan(0));
            Assert.That(nearest, Is.LessThanOrEqualTo(2f * CityHillsideStreetRoute.OuterHalfWidth), road.Bank.ToString());
            Assert.That(high.y - stairs[0].y, Is.EqualTo(2f * CityTerracePilotPlan.FlightRise).Within(.04f));
            var fromSummit = path.Skip(segment).Reverse().Concat(new[] { dock, high }).ToArray();
            yield return WalkCityRoute(fromSummit, false, road.Bank + "-down-to-stair", district);
            yield return CapturePlayerView("citywide-" + road.Bank + "-stair", stairs[0] + Vector3.up);
            yield return WalkCityRoute(stairs, true, road.Bank + "-shortcut-down", district);
            yield return WalkCityRoute(stairs, false, road.Bank + "-shortcut-up", district);
            var toMouth = new[] { high, dock }.Concat(path.Take(segment).Reverse()).ToArray();
            yield return WalkCityRoute(toMouth, false, road.Bank + "-hill-down", district);
        }

        private void AssertEasternThroughStreet(CityLayout layout)
        {
            var support = CityStreetSupportPlan.ForLayout(layout);
            var edges = new HashSet<RoadEdge>();
            for (int z = 11; z > 8; z--)
            {
                var from = new Vector2Int(13, z);
                var to = from + Vector2Int.down;
                var edge = new RoadEdge(from, to);
                edges.Add(edge);
                Assert.That(layout.HasRoad(edge), Is.True);
                Assert.That(layout.ElevationPlan.GetNodeElevation(to), Is.GreaterThan(layout.ElevationPlan.GetNodeElevation(from)));
                AssertStreetProfileContacts(layout, support, edge);
            }
            var bus = city.BusPlan;
            int contacts = 0;
            foreach (CityBusRouteLink link in bus.Links)
            {
                if (!edges.Contains(bus.Nodes[link.FromNodeIndex].RoadEdge) &&
                    !edges.Contains(bus.Nodes[link.ToNodeIndex].RoadEdge)) continue;
                Assert.That(link.Clearance.IsClear, Is.True, link.Id);
                contacts += AssertBusBodyContacts(support, bus.Vehicle, link, point => true);
            }
            Assert.That(contacts, Is.GreaterThan(0), "Route 01 must still serve the eastern through street");
        }

        private void AssertHillsideRoadSupport(CityHillsideStreetRoute route)
        {
            var vehicle = city.BusPlan.Vehicle;
            var blocker = new Collider[1];
            float start = route.Profile.SampleDistance(0f).Height;
            Assert.That(route.Profile.SampleDistance(route.Path.Length).Height, Is.EqualTo(40f).Within(.10f));
            float previous = start;
            for (int i = 0, count = Mathf.CeilToInt(route.Path.Length); i <= count; i++)
            {
                float along = route.Path.Length * i / count;
                CityRoadSample sample = route.Path.SampleDistance(along);
                float expected = route.Profile.SampleDistance(along).Height;
                if (i > 0)
                {
                    float grade = (expected - previous) / (route.Path.Length / count) * 100f;
                    Assert.That(grade, Is.InRange(-.01f, 6.01f), route.Bank + " at " + along);
                }
                previous = expected;
                Assert.That(route.TrySampleTop(sample.Position, out float top, out _), Is.True);
                Assert.That(top, Is.EqualTo(expected).Within(.035f));
                AssertHillsideCollider(sample.Position, top);
                // Front and rear overhangs, both swept flanks and both axles.
                // The standard 3 m traffic lanes keep a real bus off the kerbs
                // in either direction at the entry curve and along every hairpin.
                if (along < vehicle.InflatedLength * .5f || route.Path.Length - along < vehicle.InflatedLength * .5f) continue;
                const float laneCentreOffset = 1.5f;
                for (int lane = -1; lane <= 1; lane += 2)
                {
                    // A mast, bench or projecting wall can be above supported asphalt.
                    // Exclude the underfloor margin at transitions from grade to flat.
                    Vector2 center = sample.Position + sample.Right * (lane * laneCentreOffset);
                    float pitch = route.Profile.SampleDistance(along + vehicle.Wheelbase * .5f).Height -
                        route.Profile.SampleDistance(along - vehicle.Wheelbase * .5f).Height;
                    Quaternion rotation = Quaternion.LookRotation(new Vector3(sample.Tangent.x * vehicle.Wheelbase,
                        pitch, sample.Tangent.y * vehicle.Wheelbase));
                    float halfHeight = (vehicle.BodyHeight - .3f) * .5f;
                    Vector3 boxCenter = new Vector3(center.x, expected, center.y) + rotation * Vector3.up * (.3f + halfHeight);
                    int overlaps = Physics.OverlapBoxNonAlloc(boxCenter,
                        new Vector3(vehicle.InflatedWidth * .5f, halfHeight, vehicle.InflatedLength * .5f),
                        blocker, rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    Assert.That(overlaps, Is.Zero, route.Bank + " bus swept volume at " + center +
                        (overlaps > 0 ? " meets " + blocker[0].name : ""));
                    for (int alongBody = -2; alongBody <= 2; alongBody++)
                    for (int side = -1; side <= 1; side++)
                    {
                        float longitudinal = Mathf.Abs(alongBody) == 2
                            ? Mathf.Sign(alongBody) * vehicle.InflatedLength * .5f : alongBody * vehicle.Wheelbase * .5f;
                        Vector2 contact = sample.Position + sample.Tangent * longitudinal +
                            sample.Right * (laneCentreOffset * lane + vehicle.InflatedWidth * .5f * side);
                        Assert.That(route.TrySampleTop(contact, out float ground, out CityStreetSupportKind kind), Is.True,
                            route.Bank + " bus envelope crosses a verge at " + contact);
                        Assert.That(kind, Is.EqualTo(CityStreetSupportKind.Asphalt), route.Bank + " bus reaches a kerb at " + contact);
                        AssertHillsideCollider(contact, ground);
                    }
                }
            }
        }

        private static void AssertHillsideCollider(Vector2 point, float top)
        {
            Assert.That(Physics.Raycast(new Vector3(point.x, top + .32f, point.y), Vector3.down, out RaycastHit hit,
                .65f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True, "Missing road at " + point);
            Assert.That(hit.point.y, Is.EqualTo(top).Within(.05f), "Physical hillside paving at " + point);
        }

        private void AssertMappedSummit(CityHillsideStreetRoute route)
        {
            var ground = new CityMapCityTeleportGround(city.Layout);
            string id = route.Bank == CityTerraceTransportBank.East ? "city:poi:residential-summit" : "city:poi:industrial-summit";
            CityMapPointDescriptor marker = city.Map.ActiveMapPoints.Single(point => point.StableId == id);
            Vector3 rootPosition = route.SummitArrivalWorld + Vector3.up * PlayerFactory.GroundedRootOffset;
            Assert.That(Vector3.Distance(marker.WorldPosition, rootPosition), Is.LessThan(.01f), marker.StableId);
            Assert.That(ground.TryClampArrival(marker.WorldPosition, out Vector3 safe), Is.True, marker.StableId);
            Assert.That(Vector3.Distance(rootPosition, safe), Is.LessThan(.06f), marker.StableId + " must retain the upper floor");
            AssertHillsideCollider(new Vector2(safe.x, safe.z), route.SummitArrivalWorld.y);
        }

        private static int NearestSidewalkNode(CityPedestrianPlan graph, Vector3 near, float maximumDistance)
        {
            int closest = Enumerable.Range(0, graph.Nodes.Count)
                .OrderBy(i => (graph.Nodes[i].Position - near).sqrMagnitude).First();
            Assert.That(Vector3.Distance(graph.Nodes[closest].Position, near), Is.LessThan(maximumDistance),
                "No NPC access near " + near);
            return closest;
        }

        private static List<Vector3> StreetWalk(CityPedestrianPlan graph, int start, int end)
        {
            var previous = Enumerable.Repeat(-1, graph.Nodes.Count).ToArray();
            var pending = new Queue<int>();
            previous[start] = -2;
            pending.Enqueue(start);
            while (pending.Count > 0 && previous[end] == -1)
            {
                int node = pending.Dequeue();
                foreach (int id in graph.GetLinkIndices(node))
                {
                    CityPedestrianLink link = graph.Links[id];
                    int next = link.Other(node);
                    if (previous[next] != -1 || link.Kind == CityPedestrianLinkKind.Courtyard) continue;
                    previous[next] = id;
                    pending.Enqueue(next);
                }
            }
            Assert.That(previous[end], Is.Not.EqualTo(-1), graph.Nodes[end].Id + " is disconnected from " + graph.Nodes[start].Id);
            var links = new Stack<CityPedestrianLink>();
            for (int node = end; node != start;)
            {
                CityPedestrianLink link = graph.Links[previous[node]];
                links.Push(link);
                node = link.Other(node);
            }
            var walk = new List<Vector3> { graph.Nodes[start].Position };
            int current = start;
            foreach (CityPedestrianLink link in links)
            {
                int next = link.Other(current);
                if (link.Path != null && link.PathHeightSampler != null)
                {
                    var points = link.Path.Vertices;
                    for (int n = 1; n < points.Count - 1; n++)
                    {
                        Vector2 point = points[current == link.FirstNodeIndex ? n : points.Count - 1 - n];
                        float top = link.PathHeightSampler(point);
                        walk.Add(new Vector3(point.x, top, point.y));
                    }
                }
                walk.Add(graph.Nodes[next].Position);
                current = next;
            }
            return walk;
        }
    }
}
