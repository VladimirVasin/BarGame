using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The pavement side of the ambient passenger feature: where a walker
    /// stands while it waits for Route 01, and which designs are allowed to
    /// board at all.
    /// </summary>
    public sealed class CityBusStopWaitPlannerTests
    {
        /// <summary>
        /// Cabin floor top and ceiling bottom in the production bus model, and
        /// the cushion between them. A seated design that rises past the
        /// remaining headroom would pass through the roof.
        /// </summary>
        private const float CabinFloorToCeiling = 2.05f;
        private const float CushionAboveFloor = 0.41f;

        [Test]
        public void ProductionRoute_PutsWaitSlotsOnThePavementBesideEveryStop()
        {
            CreateContext(
                out CityBusPlan busPlan,
                out CityPedestrianPlan pedestrianPlan,
                out RoadWalkableArea walkableArea);
            CityBusStopWaitPlan waitPlan = CityBusStopWaitPlanner.Create(
                busPlan,
                pedestrianPlan,
                walkableArea);

            Assert.That(
                waitPlan.Count,
                Is.EqualTo(busPlan.Stops.Count),
                "Every Route 01 stop must keep reachable pavement beside " +
                "its pole — a silently skipped stop is one nobody can be " +
                "served at.");

            for (int index = 0; index < waitPlan.WaitPoints.Count; index++)
            {
                CityBusStopWaitPoint point = waitPlan.WaitPoints[index];
                CityBusStopDescriptor stop = busPlan.Stops[point.StopIndex];
                Assert.That(point.WaitSlots.Count, Is.GreaterThan(0));

                Vector3 along = Vector3.ProjectOnPlane(
                    stop.Forward,
                    Vector3.up).normalized;
                Vector3 toRoad = Vector3.ProjectOnPlane(
                    stop.RoadsideForward,
                    Vector3.up).normalized;
                for (int slot = 0; slot < point.WaitSlots.Count; slot++)
                {
                    Vector3 position = point.WaitSlots[slot];
                    Assert.That(
                        position.y,
                        Is.EqualTo(stop.ShelterPosition.y)
                            .Within(0.0001f),
                        "A waiter stands on the raised sidewalk surface.");
                    Assert.That(
                        walkableArea.Contains(
                            position,
                            CityPedestrianPlanner.AgentRadius),
                        Is.True,
                        "Every wait slot must hold a full walker capsule.");

                    Vector3 fromPole = position - stop.ShelterPosition;
                    fromPole.y = 0f;
                    Assert.That(
                        Vector3.Dot(fromPole, toRoad),
                        Is.EqualTo(
                            CityBusStopWaitPlanner.PoleToSidewalkCenter)
                            .Within(0.0001f),
                        "The pole stands off the walkable strip and carries " +
                        "a collider, so the slot sits road-ward of it on the " +
                        "sidewalk centreline.");
                }

                // A 1 m pavement minus a 0.35 m agent cannot fit two walkers
                // abreast, so the slots queue along the lane instead.
                for (int first = 0; first < point.WaitSlots.Count; first++)
                {
                    for (int second = first + 1;
                         second < point.WaitSlots.Count;
                         second++)
                    {
                        Vector3 separation =
                            point.WaitSlots[second] - point.WaitSlots[first];
                        separation.y = 0f;
                        Assert.That(
                            Mathf.Abs(Vector3.Dot(separation, toRoad)),
                            Is.LessThan(0.0001f),
                            "Wait slots never separate across the lane.");
                        Assert.That(
                            Mathf.Abs(Vector3.Dot(separation, along)),
                            Is.GreaterThan(
                                CityPedestrianPlanner.AgentRadius * 2f),
                            "Two waiters need more than one capsule of room " +
                            "along the lane.");
                    }
                }

                Assert.That(
                    point.NodeDistances.Count,
                    Is.EqualTo(pedestrianPlan.Nodes.Count));
                Assert.That(
                    point.NodeDistances[point.PedestrianNodeIndex],
                    Is.EqualTo(0f).Within(0.0001f),
                    "The stop node is its own routing source.");
                Assert.That(
                    point.IsReachableFrom(point.PedestrianNodeIndex),
                    Is.True);

                Vector3 nodeOffset =
                    pedestrianPlan.Nodes[point.PedestrianNodeIndex].Position -
                    stop.ShelterPosition;
                nodeOffset.y = 0f;
                Assert.That(
                    nodeOffset.magnitude,
                    Is.LessThanOrEqualTo(
                        CityBusStopWaitPlanner.MaximumNodeDistance),
                    "A stop with no nearby graph node is skipped, not faked.");
            }
        }

        [Test]
        public void WaitPlan_IsDeterministicForTheSameLayoutAndSeed()
        {
            CreateContext(
                out CityBusPlan firstBus,
                out CityPedestrianPlan firstPedestrians,
                out RoadWalkableArea firstArea);
            CreateContext(
                out CityBusPlan secondBus,
                out CityPedestrianPlan secondPedestrians,
                out RoadWalkableArea secondArea);
            CityBusStopWaitPlan first = CityBusStopWaitPlanner.Create(
                firstBus,
                firstPedestrians,
                firstArea);
            CityBusStopWaitPlan second = CityBusStopWaitPlanner.Create(
                secondBus,
                secondPedestrians,
                secondArea);

            Assert.That(second.Count, Is.EqualTo(first.Count));
            for (int index = 0; index < first.WaitPoints.Count; index++)
            {
                CityBusStopWaitPoint left = first.WaitPoints[index];
                CityBusStopWaitPoint right = second.WaitPoints[index];
                Assert.That(right.StopId, Is.EqualTo(left.StopId));
                Assert.That(
                    right.PedestrianNodeIndex,
                    Is.EqualTo(left.PedestrianNodeIndex));
                Assert.That(
                    right.WaitSlots.Count,
                    Is.EqualTo(left.WaitSlots.Count));
                for (int slot = 0; slot < left.WaitSlots.Count; slot++)
                {
                    Assert.That(
                        right.WaitSlots[slot],
                        Is.EqualTo(left.WaitSlots[slot]));
                }
            }
        }

        /// <summary>
        /// Regression: ambient boarding was silently skipped because the
        /// controller validated the passenger door dock against the
        /// pedestrian lane graph. That dock is pushed outward past the curb
        /// line, and the dock candidate ladder varies along the bus rather
        /// than across it, so every candidate failed and no walker ever
        /// boarded. The transfer area must be the hero's road-inclusive one.
        /// </summary>
        [Test]
        public void PassengerDoorDock_NeedsTheRoadInclusiveArea()
        {
            CreateContext(
                out CityBusPlan busPlan,
                out CityPedestrianPlan pedestrianPlan,
                out RoadWalkableArea pedestrianArea);
            CityBusStopWaitPlan waitPlan = CityBusStopWaitPlanner.Create(
                busPlan,
                pedestrianPlan,
                pedestrianArea);
            Assert.That(waitPlan.Count, Is.GreaterThan(0));

            CityBusDesignVehicle vehicle = CityBusDesignVehicle.Default;
            float doorDepth = (vehicle.BodyWidth * 0.5f) +
                              CityPedestrianPlanner.AgentRadius +
                              CityBusRidePlan.DoorBodyClearance;

            bool anyDockOffThePavement = false;
            for (int index = 0; index < waitPlan.WaitPoints.Count; index++)
            {
                CityBusStopWaitPoint point = waitPlan.WaitPoints[index];
                CityBusStopDescriptor stop = busPlan.Stops[point.StopIndex];
                Vector3 toRoad = Vector3.ProjectOnPlane(
                    stop.RoadsideForward,
                    Vector3.up).normalized;

                // The halted body sits on the lane centreline; the dock is
                // that centre pushed outward by half the body plus the
                // waiting capsule and its clearance.
                Vector3 dock = stop.Position + (toRoad * doorDepth);
                dock.y = stop.ShelterPosition.y;
                if (!pedestrianArea.Contains(
                        dock,
                        CityPedestrianPlanner.AgentRadius))
                {
                    anyDockOffThePavement = true;
                }

                // Whatever the dock does, the wait slot itself always holds a
                // full walker capsule on the pavement, which is why an
                // alighting walker is sent there instead of onto the dock.
                for (int slot = 0; slot < point.WaitSlots.Count; slot++)
                {
                    Assert.That(
                        pedestrianArea.Contains(
                            point.WaitSlots[slot],
                            CityPedestrianPlanner.AgentRadius),
                        Is.True);
                }
            }

            Assert.That(
                anyDockOffThePavement,
                Is.True,
                "The passenger door dock overhangs the curb, so validating " +
                "it against sidewalk-only navigation rejects every " +
                "candidate and no ambient passenger can ever board.");
        }

        [Test]
        public void SeatedRide_IsDeclaredPerDesignAndFitsTheCabin()
        {
            IReadOnlyList<CityPedestrianArchetype> archetypes =
                CityPedestrianResources.Archetypes;
            int riders = 0;
            for (int index = 0; index < archetypes.Count; index++)
            {
                CityPedestrianArchetype archetype = archetypes[index];
                if (!archetype.CanRideBus)
                {
                    continue;
                }

                riders++;
                CityPedestrianSeatedRide ride = archetype.SeatedRide;
                Assert.That(
                    CushionAboveFloor + ride.SeatLift + ride.SeatedHeadroom,
                    Is.LessThan(CabinFloorToCeiling),
                    $"{archetype.DesignId} would pass through the cabin " +
                    "ceiling when seated.");
                Assert.That(
                    ride.SeatBackOffset,
                    Is.GreaterThan(0f),
                    "A seated design sits back into the cushion.");
            }

            Assert.That(
                riders,
                Is.EqualTo(archetypes.Count - 1),
                "Exactly one design declares no seated ride.");
            Assert.That(
                CityPedestrianResources.TryGetArchetype(
                    CityPedestrianResources.HelmetLampDesignId,
                    out CityPedestrianArchetype hopper),
                Is.True);
            Assert.That(
                hopper.CanRideBus,
                Is.False,
                "The one design that hops and wears a working light stays " +
                "on the pavement by declaration.");
        }

        [Test]
        public void AmbientSeats_LeaveTheHeroSeatFreeAndStayInsideTheCabin()
        {
            IReadOnlyList<int> ambientSeats = CityBusActor.NpcSeatIndices;
            var seen = new HashSet<int>();
            for (int index = 0; index < ambientSeats.Count; index++)
            {
                int seat = ambientSeats[index];
                Assert.That(
                    seat,
                    Is.Not.EqualTo(CityBusActor.PlayerSeatIndex),
                    "Seat 07 stays reserved for the hero.");
                Assert.That(
                    seen.Add(seat),
                    Is.True,
                    "Two ambient passengers may not be sent to one seat.");
            }

            Assert.That(
                CityBusActor.MaximumNpcOccupants,
                Is.EqualTo(CityBusActor.CabinCapacity - 1),
                "Ambient passengers always leave one place for the hero, so " +
                "the cabin never carries more than three.");
        }

        /// <summary>
        /// A bus that has been running its loop before the hero saw it should
        /// not always arrive empty, but a spawned cabin must still leave him
        /// a place: the preload draws from `0` to the ambient capacity, never
        /// past it.
        /// </summary>
        [Test]
        public void CabinPreload_NeverFillsThePlaceReservedForTheHero()
        {
            Assert.That(
                CityBusNpcPassengerController.MaximumPreloadedPassengers,
                Is.EqualTo(CityBusActor.MaximumNpcOccupants),
                "The preload may seat ambient passengers only.");
            Assert.That(
                CityBusNpcPassengerController.MaximumPreloadedPassengers,
                Is.LessThan(CityBusActor.CabinCapacity),
                "A spawned bus always leaves seat 07 free for the hero.");

            // The draw is `hash % (max + 1)`, so it spans 0..max inclusive and
            // an empty bus stays a real outcome.
            var drawn = new HashSet<int>();
            for (uint sample = 0; sample < 512; sample++)
            {
                drawn.Add((int)(sample %
                    (uint)(CityBusNpcPassengerController
                        .MaximumPreloadedPassengers + 1)));
            }

            Assert.That(drawn, Does.Contain(0));
            Assert.That(
                drawn,
                Does.Contain(
                    CityBusNpcPassengerController
                        .MaximumPreloadedPassengers));
            Assert.That(
                drawn.Count,
                Is.EqualTo(
                    CityBusNpcPassengerController
                        .MaximumPreloadedPassengers + 1));
        }

        /// <summary>
        /// Regression: ambient passengers neither boarded nor got off because
        /// the transfer plan enforced "seat opposite the driver" for everyone.
        /// That rule exists for the hero alone — his authored ride clip and
        /// window camera are built around seat `07`’s lateral side — while the
        /// ambient seat order deliberately spans the whole cabin, six of whose
        /// seats are on the driver’s side. Enforcing it there rejected every
        /// plan, including the exit one.
        /// </summary>
        [Test]
        public void AmbientSeatOrder_SpansBothSidesOfTheCabin()
        {
            var manifest = JsonUtility.FromJson<BusModelManifest>(
                System.IO.File.ReadAllText(BusModelManifestPath));
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.pivots, Is.Not.Null);

            float driverX = 0f;
            var seatXByName = new SortedDictionary<string, float>(
                System.StringComparer.Ordinal);
            for (int index = 0; index < manifest.pivots.Length; index++)
            {
                BusModelPivot pivot = manifest.pivots[index];
                if (pivot == null || pivot.local_position == null ||
                    pivot.local_position.Length < 1)
                {
                    continue;
                }

                if (pivot.role == "driver_seat_anchor")
                {
                    driverX = pivot.local_position[0];
                }
                else if (pivot.role == "passenger_seat_anchor")
                {
                    seatXByName[pivot.name] = pivot.local_position[0];
                }
            }

            Assert.That(driverX, Is.Not.EqualTo(0f));
            var seatX = new List<float>(seatXByName.Values);
            Assert.That(seatX.Count, Is.GreaterThanOrEqualTo(12));

            Assert.That(
                seatX[CityBusActor.PlayerSeatIndex] * driverX,
                Is.LessThan(0f),
                "Seat 07 must stay opposite the driver: the hero's ride clip " +
                "and window camera depend on that side.");

            int ambientOnDriverSide = 0;
            IReadOnlyList<int> ambient = CityBusActor.NpcSeatIndices;
            for (int index = 0; index < ambient.Count; index++)
            {
                if (seatX[ambient[index]] * driverX > 0f)
                {
                    ambientOnDriverSide++;
                }
            }

            Assert.That(
                ambientOnDriverSide,
                Is.GreaterThan(0),
                "Ambient passengers use the driver-side row, so their " +
                "transfer plans must not require the opposite side.");
        }

        /// <summary>
        /// Regression: every ambient passenger aborted at the doorway because
        /// the transfer had a flat `2.5 s` timeout. The walk is pavement to
        /// door to seat, and the four riding designs cover it at
        /// `0.72`-`1.30 m/s`, so the real transfer takes several seconds and
        /// no boarding could ever complete. The budget is now derived from
        /// the measured path and the walker's own pace.
        /// </summary>
        [Test]
        public void TransferBudget_CoversTheRealWalkForEveryRidingDesign()
        {
            var manifest = JsonUtility.FromJson<BusModelManifest>(
                System.IO.File.ReadAllText(BusModelManifestPath));
            Vector2 front = Vector2.zero;
            Vector2 rear = Vector2.zero;
            var seats = new SortedDictionary<string, Vector2>(
                System.StringComparer.Ordinal);
            for (int index = 0; index < manifest.pivots.Length; index++)
            {
                BusModelPivot pivot = manifest.pivots[index];
                if (pivot == null || pivot.local_position == null ||
                    pivot.local_position.Length < 2)
                {
                    continue;
                }

                var lateral = new Vector2(
                    pivot.local_position[0],
                    pivot.local_position[1]);
                if (pivot.role == "front_door_entry")
                {
                    front = lateral;
                }
                else if (pivot.role == "rear_door_entry")
                {
                    rear = lateral;
                }
                else if (pivot.role == "passenger_seat_anchor")
                {
                    seats[pivot.name] = lateral;
                }
            }

            var seatList = new List<Vector2>(seats.Values);
            float worstAisle = 0f;
            IReadOnlyList<int> ambient = CityBusActor.NpcSeatIndices;
            for (int index = 0; index < ambient.Count; index++)
            {
                Vector2 seat = seatList[ambient[index]];
                worstAisle = Mathf.Max(
                    worstAisle,
                    Mathf.Min(
                        Vector2.Distance(front, seat),
                        Vector2.Distance(rear, seat)));
            }

            Assert.That(
                worstAisle,
                Is.GreaterThan(1f),
                "Every ambient seat is a real walk from either doorway.");

            // Pavement leg: the wait slot stands one door clearance plus a
            // capsule off the body, and the doors are along the same kerb.
            const float PavementLeg = 3f;
            float worstPath = PavementLeg + worstAisle;
            for (int index = 0;
                 index < CityPedestrianResources.Archetypes.Count;
                 index++)
            {
                CityPedestrianArchetype archetype =
                    CityPedestrianResources.Archetypes[index];
                if (!archetype.CanRideBus)
                {
                    continue;
                }

                float slowest = archetype.MinimumMovementSpeed;
                float required = worstPath / slowest;
                float budget = Mathf.Clamp(
                    (required *
                     CityBusNpcPassengerController.TransferBudgetSlack) +
                    CityBusNpcPassengerController.TransferBudgetPadding,
                    CityBusNpcPassengerController.MinimumTransferBudget,
                    CityBusNpcPassengerController.MaximumTransferBudget);
                Assert.That(
                    budget,
                    Is.GreaterThan(required),
                    $"{archetype.DesignId} walks {worstPath:F2} m at " +
                    $"{slowest:F2} m/s, which needs {required:F1} s; a " +
                    "budget below that aborts every transfer it ever tries.");
            }

            Assert.That(
                CityBusNpcPassengerController.MaximumTransferBudget,
                Is.LessThanOrEqualTo(CityBusActor.DwellDuration),
                "A transfer that outlasts a whole dwell is stuck, not slow.");
        }

        /// <summary>
        /// Regression: the bus pulled up to a stop, halted and never opened
        /// its doors while a walker stood waiting. The walker was the reason.
        /// A `1 m` sidewalk minus a `0.35 m` capsule and two `0.15 m`
        /// navigation margins leaves exactly one lateral position for a wait
        /// slot, and that position sits `0.08 m` outside the bus obstacle
        /// corridor — daylight the walker's own `0.15 m` shoulder-shift
        /// closes. The bus then yields short of its own stop, the doors stay
        /// shut, and the waiter waits forever for a bus that can never serve
        /// it. The slot cannot move, so the passenger is exempt from the
        /// obstacle test instead.
        /// </summary>
        [Test]
        public void WaitSlot_SitsInsideTheBusObstacleCorridor()
        {
            CreateContext(
                out CityBusPlan busPlan,
                out CityPedestrianPlan pedestrianPlan,
                out RoadWalkableArea walkableArea);
            CityBusStopWaitPlan waitPlan = CityBusStopWaitPlanner.Create(
                busPlan,
                pedestrianPlan,
                walkableArea);
            Assert.That(waitPlan.Count, Is.GreaterThan(0));

            CityBusDesignVehicle vehicle = CityBusDesignVehicle.Default;
            float corridor = CityPedestrianPlanner.AgentRadius +
                             CityBusActor.ObstacleStopPadding;

            bool anySlotInsideCorridor = false;
            for (int index = 0; index < waitPlan.WaitPoints.Count; index++)
            {
                CityBusStopWaitPoint point = waitPlan.WaitPoints[index];
                CityBusStopDescriptor stop = busPlan.Stops[point.StopIndex];
                Vector3 toRoad = Vector3.ProjectOnPlane(
                    stop.RoadsideForward,
                    Vector3.up).normalized;
                for (int slot = 0; slot < point.WaitSlots.Count; slot++)
                {
                    Vector3 offset = point.WaitSlots[slot] - stop.Position;
                    offset.y = 0f;
                    // Lateral distance from the halted body flank.
                    float gap = Mathf.Abs(Vector3.Dot(offset, toRoad)) -
                                (vehicle.BodyWidth * 0.5f);
                    if (gap - CityPedestrianActor.MaximumLateralOffset <
                        corridor)
                    {
                        anySlotInsideCorridor = true;
                    }
                }
            }

            Assert.That(
                anySlotInsideCorridor,
                Is.True,
                "A waiter reaches inside the corridor once it leans, so " +
                "route-bound walkers must be exempt from the bus obstacle " +
                "test or the bus can never reach its own stop.");
        }

        private static void CreateContext(
            out CityBusPlan busPlan,
            out CityPedestrianPlan pedestrianPlan,
            out RoadWalkableArea walkableArea)
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Resolve(
                    GameSessionState.DefaultCityBlueprintId),
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            RoadFencePlan fences = RoadFencePlanner.CreatePlan(layout);
            CityNightFixturePlan night =
                CityNightFixturePlanner.CreatePlan(layout);
            CityDecorationPlan decorations =
                CityDecorationPlanner.CreatePlan(layout, fences, night);
            busPlan = CityBusPlanner.Create(layout, decorations);
            pedestrianPlan = CityPedestrianPlanner.Create(
                layout,
                GameSessionState.DefaultCitySeed,
                CityStreetSurfacePlanner.Create(layout));
            walkableArea =
                CityPedestrianPlanner.CreateWalkableArea(pedestrianPlan);
        }

        private const string BusModelManifestPath =
            "Assets/Vehicles/Models/CityBus3D.json";

        [System.Serializable]
        private sealed class BusModelManifest
        {
            public BusModelPivot[] pivots;
        }

        [System.Serializable]
        private sealed class BusModelPivot
        {
            public string name;
            public string role;
            public float[] local_position;
        }
    }
}
