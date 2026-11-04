using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Binds resident journeys to physical queues, doors and seats. Isolated
    /// single-bus fixtures can use a local population without the City calendar.
    /// The cabin holds three, counting the hero.
    /// </summary>
    [DefaultExecutionOrder(345)]
    [DisallowMultipleComponent]
    public sealed partial class CityBusNpcPassengerController : MonoBehaviour
    {
        // Ambient walkers share the NpcHumanV2/Hero V2 adult rest pelvis.
        // The transfer target therefore aligns the same 0.835 m landmark to
        // the cushion anchor before the per-design compressed-seat lift is
        // applied by CityPedestrianPresentation.
        internal const float PassengerPelvisHeight = 0.835f;

        /// <summary>
        /// A walker already this close to a stop along the pavement can be
        /// asked to use it, which is how the hero sees somebody walk up and
        /// wait rather than only ever finding people already standing there.
        /// </summary>
        public const float RecruitGraphDistance = 55f;

        /// <summary>
        /// Below this range a stop is close enough for the hero to notice an
        /// activation, so a waiter is only ever spawned outright beyond it.
        /// It matches the fixed-fog band the population director already
        /// proved hides an appearing walker.
        /// </summary>
        public const float HiddenSpawnDistance = 76f;

        public const float MinimumWaiterDelay = 5f;
        public const float MaximumWaiterDelay = 18f;
        public const float NightWaiterDelayScale = 3f;

        // The walk includes the outside dock, sill, aisle and final turn.
        // An alternate door can add the distance between doors along the kerb.
        public const float TransferBudgetSlack = 1.6f;
        public const float TransferBudgetPadding = 1f;
        public const float MinimumTransferBudget = 3f;
        public const float MaximumTransferBudget = CityBusActor.MaximumServiceHoldDuration - TransferBudgetPadding;

        /// <summary>
        /// Simultaneous physical queue placements across the network. Logical
        /// demand and occupied cabin places have separate budgets.
        /// </summary>
        public const int MaximumWaiters = 3;

        /// <summary>
        /// A bus that has been running its loop since before the hero saw it
        /// should not always arrive empty, so it spawns with a seeded
        /// `0`-`2` ambient passengers already seated. It activates `76-86 m`
        /// away behind fixed fog, so nobody watches them appear, and they
        /// leave through the ordinary alighting path at a seeded later stop.
        /// </summary>
        public const int MaximumPreloadedPassengers = 2;

        private const uint PreloadSalt = 0x50524C44u;
        private const uint StopSalt = 0x53544F50u;
        private const uint SpawnSalt = 0x53504157u;
        private const uint RideSalt = 0x52494445u;
        private const uint FallbackSeed = 0xA341316Cu;

        private readonly List<Waiter> waiters = new List<Waiter>(MaximumWaiters);
        private readonly object seatReservation = new object();

        private CityBusDirector busDirector;
        private IReadOnlyList<CityBusNpcPassengerController> lines;

        // All lines draw from the same bounded population. A shared platform
        // owns one queue in physical space even when two routes call there.
        internal void ShareWaitingPlaces(IReadOnlyList<CityBusNpcPassengerController> lineControllers)
        {
            lines = lineControllers;
        }

        private int NetworkWaiters()
        {
            if (lines == null) return WaiterCount;
            int count = 0;
            for (int i = 0; i < lines.Count; i++) count += lines[i].WaiterCount;
            return count;
        }

        private CityPedestrianDirector pedestrians;
        private CityBusStopWaitPlan waitPlan;
        private IWalkableArea transferArea;
        private CityStreetSurfacePlan streetSurfacePlan;
        private Transform player;
        private Func<bool> nightModeProvider;
        private Action passengerCleanup;
        private uint randomState = FallbackSeed;
        private float spawnCooldown;
        private bool busWasSpawned;

        public bool IsInitialized { get; private set; }
        public int TrackedCount => waiters.Count;

        public int WaiterCount
        {
            get
            {
                int count = 0;
                for (int index = 0; index < waiters.Count; index++)
                {
                    if (!waiters[index].IsAboard)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int PassengerCount
        {
            get
            {
                int count = 0;
                if (journeys != null)
                {
                    for (int slot = 0; slot < CityBusActor.MaximumNpcOccupants; slot++)
                        if (journeys.RiderAt(vehicleIndex, slot) >= 0) count++;
                    return count;
                }
                for (int index = 0; index < waiters.Count; index++)
                {
                    if (waiters[index].IsAboard)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public static CityBusNpcPassengerController Create(
            CityBusDirector directorComponent,
            CityPedestrianDirector pedestrianDirector,
            CityBusStopWaitPlan stopWaitPlan,
            IWalkableArea doorTransferArea,
            CityStreetSurfacePlan surfacePlan,
            Transform playerTransform,
            int seed,
            Func<bool> nightMode = null)
        {
            if (directorComponent == null)
            {
                throw new ArgumentNullException(nameof(directorComponent));
            }

            CityBusNpcPassengerController controller =
                directorComponent.gameObject
                    .AddComponent<CityBusNpcPassengerController>();
            controller.Initialize(
                directorComponent,
                pedestrianDirector,
                stopWaitPlan,
                doorTransferArea,
                surfacePlan,
                playerTransform,
                seed,
                nightMode);
            return controller;
        }

        /// <summary>
        /// <paramref name="doorTransferArea"/> must be the same road-inclusive
        /// area the hero's ride controller uses, not the pedestrian lane
        /// graph. The outward door dock lands `0.12 m` past the curb line, so
        /// it never validates against sidewalk-only navigation and every dock
        /// candidate would be rejected — the dock offsets vary along the bus,
        /// not across it.
        /// </summary>
        public void Initialize(
            CityBusDirector directorComponent,
            CityPedestrianDirector pedestrianDirector,
            CityBusStopWaitPlan stopWaitPlan,
            IWalkableArea doorTransferArea,
            CityStreetSurfacePlan surfacePlan,
            Transform playerTransform,
            int seed,
            Func<bool> nightMode = null)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The city bus passenger controller is already " +
                    "initialized.");
            }

            busDirector = directorComponent ??
                throw new ArgumentNullException(nameof(directorComponent));
            pedestrians = pedestrianDirector ??
                throw new ArgumentNullException(nameof(pedestrianDirector));
            waitPlan = stopWaitPlan ??
                throw new ArgumentNullException(nameof(stopWaitPlan));
            transferArea = doorTransferArea ??
                throw new ArgumentNullException(nameof(doorTransferArea));
            player = playerTransform ??
                throw new ArgumentNullException(nameof(playerTransform));
            streetSurfacePlan = surfacePlan;
            nightModeProvider = nightMode ?? DefaultNightMode;
            randomState = unchecked((uint)seed);
            if (randomState == 0u)
            {
                randomState = FallbackSeed;
            }

            passengerCleanup = ReleaseEveryPassenger;
            busDirector.RegisterPassengerCleanup(passengerCleanup);
            spawnCooldown = GetNextSpawnDelay();
            IsInitialized = true;
        }

        public void Shutdown()
        {
            if (!IsInitialized)
            {
                return;
            }

            ReleaseEveryWaiter();
            if (journeys != null && busDirector != null && busDirector.Actor != null)
                busDirector.Actor.ServiceStarted -= OnResidentServiceStarted;
            if (busDirector != null && passengerCleanup != null)
            {
                busDirector.UnregisterPassengerCleanup(passengerCleanup);
            }

            passengerCleanup = null;
            busWasSpawned = false;
            IsInitialized = false;
        }

        public void Advance(float deltaTime)
        {
            if (!IsInitialized)
            {
                return;
            }

            float safeDeltaTime = IsFinite(deltaTime)
                ? Mathf.Max(0f, deltaTime)
                : 0f;
            CityBusActor actor = busDirector.Actor;
            bool busAvailable = actor != null && actor.IsSpawned;
            if (journeys != null)
                RefreshResidentService(actor, safeDeltaTime);
            else if (busAvailable && !busWasSpawned)
            {
                PreloadPassengers(actor);
            }

            busWasSpawned = busAvailable;
            for (int index = waiters.Count - 1; index >= 0; index--)
            {
                if (!AdvanceWaiter(waiters[index], actor, busAvailable,
                        safeDeltaTime))
                {
                    waiters.RemoveAt(index);
                }
            }

            if (journeys != null) return;
            spawnCooldown = Mathf.Max(0f, spawnCooldown - safeDeltaTime);
            if (spawnCooldown <= 0f)
            {
                TryAddWaiter();
                spawnCooldown = GetNextSpawnDelay();
            }
        }

        private void LateUpdate()
        {
            Advance(Time.deltaTime);
        }

        /// <summary>
        /// Disabling releases the street, not the controller: a City dormant
        /// behind a bar door comes back with the same bus, and a controller
        /// that shut itself down here stayed dead for the rest of the run,
        /// its cleanup unregistered from a director it never rejoined. The
        /// registration is kept; destruction alone takes it away.
        /// </summary>
        private void OnDisable()
        {
            if (!IsInitialized)
            {
                return;
            }

            if (journeys != null) ReleaseForTimeJump();
            else ReleaseEveryWaiter();
            busWasSpawned = false;
        }

        private void OnEnable()
        {
            if (!IsInitialized)
            {
                return;
            }

            busWasSpawned = false;
            spawnCooldown = GetNextSpawnDelay();
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private bool AdvanceWaiter(
            Waiter waiter,
            CityBusActor actor,
            bool busAvailable,
            float deltaTime)
        {
            CityPedestrianActor walker = waiter.Actor;
            if (walker == null || !walker.IsSpawned ||
                (waiter.Resident >= 0 && pedestrians.ResidentIndex(walker) != waiter.Resident))
            {
                return ReleaseWaiterOwnership(waiter, actor, "walker_lost");
            }

            switch (walker.MotionState)
            {
                case CityPedestrianMotionState.ApproachingStop:
                case CityPedestrianMotionState.WaitingAtStop:
                    if (journeys != null) return AdvanceResidentQueue(waiter);
                    if (!busAvailable)
                    {
                        return true;
                    }

                    return TryBeginBoarding(waiter, actor);
                case CityPedestrianMotionState.Boarding:
                    return AdvanceBoarding(waiter, actor, deltaTime);
                case CityPedestrianMotionState.Riding:
                    return AdvanceRide(waiter, actor);
                case CityPedestrianMotionState.Alighting:
                    return AdvanceAlighting(waiter, actor, deltaTime);
                default:
                    // Ordinary roaming resumed, or the walker was recycled.
                    return ReleaseWaiterOwnership(
                        waiter,
                        actor,
                        "walker_left_route");
            }
        }

        /// <summary>
        /// Drops a tracked record and always hands back whatever it held on
        /// the bus. A service hold that outlives its owner freezes the dwell
        /// timer, and the door timeline is sampled from that timer: the next
        /// stop is then served with the doors shut forever, which is why no
        /// record may ever be forgotten while still owning one.
        /// </summary>
        private bool ReleaseWaiterOwnership(
            Waiter waiter,
            CityBusActor actor,
            string reason)
        {
            if (actor != null)
            {
                bool heldService = actor.ReleaseServiceHold(waiter);
                bool heldSeat = actor.ReleasePassenger(waiter);
                if (heldService || heldSeat)
                {
                    GameLog.Warning(
                        "bus_npc",
                        "ownership_reclaimed",
                        GameLog.Field("stop", waiter.StopId),
                        GameLog.Field("reason", reason),
                        GameLog.Field("held_service", heldService),
                        GameLog.Field("held_seat", heldSeat));
                }
            }

            return false;
        }

        private bool TryBeginBoarding(Waiter waiter, CityBusActor actor)
        {
            if (waiter.Walker.MotionState !=
                    CityPedestrianMotionState.WaitingAtStop ||
                !actor.DoorsFullyOpen ||
                actor.CurrentStopIndex != waiter.StopIndex)
            {
                return true;
            }

            if (actor.NpcOccupantCount >= CityBusActor.MaximumNpcOccupants ||
                actor.OccupantCount >= CityBusActor.CabinCapacity)
            {
                ReportBoardingBlocked(waiter, "cabin_full");
                return true;
            }

            if (!TryReserveBoarding(waiter, actor, out var plan))
            {
                ReportBoardingBlocked(waiter, "no_free_door_or_place");
                return true;
            }
            int seatIndex = plan.SeatIndex;

            if (!BeginDoorTransfer(waiter.Walker, actor, plan, true, waiter.Walker.Position, out float path))
            {
                actor.ReleaseServiceHold(waiter);
                actor.ReleasePassenger(waiter);
                ReportBoardingBlocked(waiter, "transfer_refused");
                return true;
            }

            waiter.BlockedReason = null;
            waiter.Plan = plan;
            waiter.RideRootLocalPosition = plan.RideRootLocalPosition;
            waiter.RideRootLocalRotation = plan.RideRootLocalRotation;
            waiter.SeatIndex = seatIndex;
            waiter.TransferElapsed = 0f;
            waiter.TransferBudget = ResolveTransferBudget(
                path,
                waiter.Walker.MovementSpeed);
            GameLog.Info(
                "bus_npc",
                "board_started",
                GameLog.Field("stop", waiter.StopId),
                GameLog.Field("seat", seatIndex),
                GameLog.Field("design", waiter.Walker.DesignId),
                GameLog.Field("path_m", path),
                GameLog.Field("budget_s", waiter.TransferBudget));
            return true;
        }

        private bool TryReserveBoarding(Waiter waiter, CityBusActor actor, out CityBusRidePlan plan)
        {
            plan = null;
            // A body walking to its seat already owns it even though the resident
            // manifest commits only when they sit. Try the next compatible place.
            var places = CityBusActor.NpcPlaceIndices;
            bool freeSeat = false;
            for (int slot = 0; slot < places.Count; slot++)
            {
                int place = places[slot];
                if (CityBusActor.IsStandingPlace(place) && freeSeat) break;
                if (actor.IsPlaceOccupied(place) || (journeys != null && journeys.RiderAt(vehicleIndex, slot) >= 0))
                    continue;
                freeSeat |= !CityBusActor.IsStandingPlace(place);
                var preferred = SelectBestDoor(waiter.Walker.Position, actor, place);
                if (!TryOpenPlan(waiter, actor, place, preferred, out plan) &&
                    !TryOpenPlan(waiter, actor, place, OtherDoor(preferred), out plan)) continue;
                if (!actor.TryAttachNpcPassengerAtPlace(waiter, place)) continue;
                if (actor.TryAcquireDoorHold(waiter, plan.PassengerDoor, place)) return true;
                actor.ReleasePassenger(waiter);
            }
            plan = null;
            return false;
        }

        private bool TryOpenPlan(Waiter waiter, CityBusActor actor, int place, CityBusPassengerDoor door,
            out CityBusRidePlan plan)
        {
            plan = null;
            return actor.CanUsePassengerDoor(waiter, door, place) && TryBuildPlan(waiter, actor, place, door, out plan);
        }

        private static CityBusPassengerDoor OtherDoor(CityBusPassengerDoor door) => door == CityBusPassengerDoor.Front
            ? CityBusPassengerDoor.Rear : CityBusPassengerDoor.Front;

        private bool AdvanceBoarding(
            Waiter waiter,
            CityBusActor actor,
            float deltaTime)
        {
            waiter.TransferElapsed += deltaTime;
            if (waiter.Walker.TransferCompleted)
            {
                if (!BeginPassengerRide(waiter.Walker, actor, waiter.SeatIndex, false))
                {
                    return AbortTransfer(waiter, actor);
                }

                if (journeys != null && !journeys.TryBoard(waiter.Resident, vehicleIndex, waiter.StopIndex,
                    CityBusActor.NpcSlotForPlace(waiter.SeatIndex)))
                    return AbortTransfer(waiter, actor);
                waiter.BoardedServiceOrdinal = actor.ServiceOrdinal;
                if (journeys == null) waiter.RideStopCount = DrawRideStopCount();
                actor.ReleaseServiceHold(waiter);
                GameLog.Info(
                    "bus_npc",
                    "board_completed",
                    GameLog.Field("stop", waiter.StopId),
                    GameLog.Field("seat", waiter.SeatIndex),
                    GameLog.Field("ride_stops", waiter.RideStopCount));
                return journeys == null || !MoveBoardedWalkerIntoCabin(waiter, actor);
            }

            if (waiter.TransferElapsed >= waiter.TransferBudget)
            {
                return AbortTransfer(waiter, actor);
            }

            return true;
        }

        private bool AdvanceRide(Waiter waiter, CityBusActor actor)
        {
            Transform actorRoot = actor.transform;
            waiter.Walker.SetRidePose(
                actorRoot.TransformPoint(waiter.RideRootLocalPosition),
                actorRoot.rotation * waiter.RideRootLocalRotation);
            int served = actor.ServiceOrdinal - waiter.BoardedServiceOrdinal;
            bool destinationReached = journeys != null ?
                journeys.GetState(waiter.Resident).ExitStop == actor.CurrentStopIndex : served >= waiter.RideStopCount;
            if (!destinationReached ||
                !actor.DoorsFullyOpen ||
                actor.CurrentStop == null)
            {
                return true;
            }

            var preferred = waiter.Plan?.PassengerDoor ?? SelectBestDoor(waiter.Walker.Position, actor, waiter.SeatIndex);
            if (!TryOpenPlan(waiter, actor, waiter.SeatIndex, preferred, out var exitPlan) &&
                !TryOpenPlan(waiter, actor, waiter.SeatIndex, OtherDoor(preferred), out exitPlan)) return true;
            if (!actor.TryAcquireDoorHold(waiter, exitPlan.PassengerDoor, waiter.SeatIndex)) return true;

            // The hero may step down onto the carriageway dock; a walker
            // belongs on the pavement, and the stop's own wait slot is
            // already proven walkable for a full walker capsule.
            Vector3 exitRoot = exitPlan.ExitPose.RootPosition;
            if (waitPlan.TryGetWaitPoint(
                    actor.CurrentStopIndex,
                    out CityBusStopWaitPoint exitWaitPoint) &&
                exitWaitPoint.WaitSlots.Count > 0)
            {
                if (!TryChooseExit(exitWaitPoint, exitPlan.ExitPose.RootPosition, waiter.Walker, out exitRoot))
                {
                    actor.ReleaseServiceHold(waiter);
                    return true;
                }
            }

            if (!BeginDoorTransfer(waiter.Walker, actor, exitPlan, false, exitRoot, out float path))
            {
                actor.ReleaseServiceHold(waiter);
                return true;
            }

            waiter.ExitLanding = exitRoot;
            waiter.Plan = exitPlan;
            waiter.StopIndex = actor.CurrentStopIndex;
            waiter.StopId = actor.CurrentStop.Id;
            waiter.TransferElapsed = 0f;
            waiter.TransferBudget = ResolveTransferBudget(path, waiter.Walker.MovementSpeed);
            return true;
        }

        private bool AdvanceAlighting(
            Waiter waiter,
            CityBusActor actor,
            float deltaTime)
        {
            waiter.TransferElapsed += deltaTime;
            if (!waiter.Walker.TransferCompleted &&
                waiter.TransferElapsed < waiter.TransferBudget)
            {
                return true;
            }

            bool completed = waiter.Walker.TransferCompleted;
            if (journeys != null) journeys.TryAlight(waiter.Resident, vehicleIndex, waiter.StopIndex);
            actor.ReleaseServiceHold(waiter);
            actor.ReleasePassenger(waiter);
            int rejoinNode = waitPlan.TryGetWaitPoint(
                waiter.StopIndex,
                out CityBusStopWaitPoint point)
                ? point.PedestrianNodeIndex
                : -1;
            // The landing remains reserved until this transfer releases it.
            if (!completed) waiter.Walker.transform.position = waiter.ExitLanding;

            waiter.Walker.ResumeRoaming(rejoinNode);
            GameLog.Info(
                "bus_npc",
                "alighted",
                GameLog.Field("stop", waiter.StopId),
                GameLog.Field("design", waiter.Walker.DesignId));
            return false;
        }

        private bool AbortTransfer(Waiter waiter, CityBusActor actor)
        {
            actor.ReleaseServiceHold(waiter);
            actor.ReleasePassenger(waiter);
            int rejoinNode = waitPlan.TryGetWaitPoint(
                waiter.StopIndex,
                out CityBusStopWaitPoint point)
                ? point.PedestrianNodeIndex
                : -1;
            if (point != null && point.WaitSlots.Count > 0)
            {
                waiter.Walker.transform.position = point.WaitSlots[0];
            }

            waiter.Walker.ResumeRoaming(rejoinNode);
            GameLog.Info(
                "bus_npc",
                "transfer_aborted",
                GameLog.Field("stop", waiter.StopId));
            return false;
        }

        /// <summary>
        /// Seats a seeded `0`-`2` ambient passengers the moment the bus
        /// activates. There is no doorway crossing to play: they were already
        /// riding before the hero could see the vehicle at all.
        /// </summary>
        private void PreloadPassengers(CityBusActor actor)
        {
            int requested = (int)(
                CityPedestrianStableHash.Combine(
                    NextRandomUInt(),
                    PreloadSalt) %
                (uint)(MaximumPreloadedPassengers + 1));
            int seated = 0;
            for (int index = 0; index < requested; index++)
            {
                if (TryPreloadOnePassenger(actor))
                {
                    seated++;
                }
            }

            if (requested > 0)
            {
                GameLog.Info(
                    "bus_npc",
                    "cabin_preloaded",
                    GameLog.Field("requested", requested),
                    GameLog.Field("seated", seated));
            }
        }

        private bool TryPreloadOnePassenger(CityBusActor actor)
        {
            if (actor.NpcOccupantCount >= CityBusActor.MaximumNpcOccupants ||
                actor.OccupantCount >= CityBusActor.CabinCapacity ||
                waitPlan.Count == 0)
            {
                return false;
            }

            var placeholder = seatReservation;
            if (!actor.TryAttachNpcPassenger(placeholder, out int seatIndex))
            {
                return false;
            }

            if (!CityBusRidePlan.TryCreateSeatedPose(
                    actor,
                    seatIndex,
                    out Vector3 localPosition,
                    out Quaternion localRotation,
                    out Transform seat))
            {
                actor.ReleasePassenger(placeholder);
                return false;
            }

            Transform actorRoot = actor.transform;
            // Any retained graph node serves as the rejoin point until the
            // passenger actually alights, which rebinds it to the stop it
            // steps out at.
            int rejoinNode = waitPlan.WaitPoints[0].PedestrianNodeIndex;
            CityPedestrianActor walker = pedestrians.TrySpawnSeatedPassenger(
                actorRoot.TransformPoint(localPosition),
                actorRoot.rotation * localRotation,
                rejoinNode,
                CityPedestrianStableHash.Combine(NextRandomUInt(), PreloadSalt));
            if (walker == null)
            {
                actor.ReleasePassenger(placeholder);
                return false;
            }

            if (!BeginPassengerRide(walker, actor, seatIndex, true))
            {
                pedestrians.ReleaseRouteBoundActor(walker);
                actor.ReleasePassenger(placeholder);
                return false;
            }

            var waiter = new Waiter(
                walker,
                actor.CurrentStopIndex,
                "spawn",
                actorRoot.TransformPoint(localPosition))
            {
                SeatIndex = seatIndex,
                RideRootLocalPosition = localPosition,
                RideRootLocalRotation = localRotation,
                BoardedServiceOrdinal = actor.ServiceOrdinal,
                RideStopCount = DrawRideStopCount()
            };
            // The placeholder only reserved the seat while the walker was
            // being activated; the cabin tracks the waiter from here.
            actor.ReleasePassenger(placeholder);
            if (!actor.TryAttachNpcPassengerAtPlace(waiter, seatIndex))
            {
                pedestrians.ReleaseRouteBoundActor(walker);
                actor.ReleasePassenger(waiter);
                return false;
            }

            waiters.Add(waiter);
            return true;
        }

        private void TryAddWaiter()
        {
            if ((lines != null && WaiterCount >= Mathf.CeilToInt(MaximumWaiters / (float)lines.Count)) ||
                NetworkWaiters() >= MaximumWaiters ||
                waitPlan.Count == 0)
            {
                return;
            }

            int offset = (int)(NextRandomUInt() % (uint)waitPlan.Count);
            for (int step = 0; step < waitPlan.Count; step++)
            {
                CityBusStopWaitPoint point =
                    waitPlan.WaitPoints[(offset + step) % waitPlan.Count];
                if (!TryReserveSlot(point, out Vector3 slot))
                {
                    continue;
                }

                if (TryRecruitWalker(point, slot) ||
                    TrySpawnHiddenWaiter(point, slot))
                {
                    return;
                }
            }
        }

        private bool TryReserveSlot(
            CityBusStopWaitPoint point,
            out Vector3 slot)
        {
            for (int index = 0; index < point.WaitSlots.Count; index++)
            {
                Vector3 candidate = point.WaitSlots[index];
                if (!IsSlotTakenOnAnyLine(candidate))
                {
                    slot = candidate;
                    return true;
                }
            }

            slot = default;
            return false;
        }

        private bool IsSlotTakenOnAnyLine(Vector3 slot)
        {
            if (lines == null) return IsSlotTaken(slot);
            for (int i = 0; i < lines.Count; i++)
                if (lines[i].IsSlotTaken(slot)) return true;
            return false;
        }

        private bool IsSlotTaken(Vector3 slot)
        {
            for (int index = 0; index < waiters.Count; index++)
            {
                Waiter waiter = waiters[index];
                bool leaving = waiter.Walker != null && waiter.Walker.MotionState == CityPedestrianMotionState.Alighting;
                if (waiter.IsAboard && !leaving) continue;
                Vector3 reserved = leaving ? waiter.ExitLanding : waiter.Slot;
                Vector3 offset = reserved - slot;
                offset.y = 0f;
                if (offset.sqrMagnitude < .70f * .70f && Mathf.Abs(reserved.y - slot.y) < 1f)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The organic half of the hybrid: an ordinary roaming walker already
        /// near the stop decides to use it, so the hero can watch the whole
        /// approach.
        /// </summary>
        private bool TryRecruitWalker(
            CityBusStopWaitPoint point,
            Vector3 slot)
        {
            IReadOnlyList<CityPedestrianActor> actors = pedestrians.Actors;
            for (int index = 0; index < actors.Count; index++)
            {
                CityPedestrianActor walker = actors[index];
                if (walker == null ||
                    !walker.IsSpawned ||
                    walker.MotionState !=
                        CityPedestrianMotionState.Walking ||
                    IsTracked(walker))
                {
                    continue;
                }

                CityPedestrianArchetype archetype =
                    CityPedestrianDirector.GetActorArchetype(walker);
                if (archetype == null ||
                    !archetype.CanRideBus ||
                    walker.Presentation == null ||
                    walker.Presentation.Registry == null ||
                    walker.Presentation.Registry.SitClip == null)
                {
                    continue;
                }

                if (!point.IsReachableFrom(walker.TargetNodeIndex) ||
                    point.NodeDistances[walker.TargetNodeIndex] >
                        RecruitGraphDistance)
                {
                    continue;
                }

                if (!walker.BeginStopApproach(
                        point.PedestrianNodeIndex,
                        slot,
                        point.Facing,
                        point.NodeDistances))
                {
                    continue;
                }

                waiters.Add(new Waiter(walker, point.StopIndex, point.StopId,
                    slot));
                GameLog.Info(
                    "bus_npc",
                    "waiter_recruited",
                    GameLog.Field("stop", point.StopId),
                    GameLog.Field("design", walker.DesignId));
                return true;
            }

            return false;
        }

        /// <summary>
        /// The cheap half: where the stop is already hidden, a waiter is
        /// activated straight onto its slot.
        /// </summary>
        private bool TrySpawnHiddenWaiter(
            CityBusStopWaitPoint point,
            Vector3 slot)
        {
            Vector3 offset = slot - player.position;
            offset.y = 0f;
            if (offset.magnitude < HiddenSpawnDistance)
            {
                return false;
            }

            CityPedestrianActor walker = pedestrians.TrySpawnStopWaiter(
                slot,
                point.Facing,
                point.PedestrianNodeIndex,
                point.NodeDistances,
                CityPedestrianStableHash.Combine(NextRandomUInt(), SpawnSalt));
            if (walker == null)
            {
                return false;
            }

            waiters.Add(new Waiter(walker, point.StopIndex, point.StopId,
                slot));
            GameLog.Info(
                "bus_npc",
                "waiter_spawned",
                GameLog.Field("stop", point.StopId),
                GameLog.Field("design", walker.DesignId));
            return true;
        }

        private bool TryChooseExit(CityBusStopWaitPoint point, Vector3 reference, CityPedestrianActor passenger,
            out Vector3 destination)
        {
            destination = default;
            float nearest = float.PositiveInfinity;
            for (int index = 0; index < point.WaitSlots.Count; index++)
            {
                Vector3 candidate = point.WaitSlots[index];
                float distance = PlanarSquaredDistance(candidate, reference);
                if (distance < nearest && !IsSlotTakenOnAnyLine(candidate) && Clear(candidate))
                {
                    nearest = distance;
                    destination = candidate;
                }
            }
            if (!float.IsPositiveInfinity(nearest)) return true;
            // The two waiting places may both be occupied. The passenger can
            // step back onto the surveyed through sidewalk and continue there.
            destination = pedestrians.Plan.Nodes[point.PedestrianNodeIndex].Position;
            return !IsSlotTakenOnAnyLine(destination) && Clear(destination);

            bool Clear(Vector3 p)
            {
                float passengerRadius = passenger != null ? passenger.AgentRadius : pedestrians.Plan.AgentRadius;
                float radius = passengerRadius + .35f;
                if (Mathf.Abs(player.position.y - p.y) < 1.8f && PlanarSquaredDistance(p, player.position) < radius * radius)
                    return false;
                var actors = pedestrians.Actors;
                for (int i = 0; i < actors.Count; i++)
                {
                    var walker = actors[i];
                    if (!walker.IsSpawned || walker == passenger || walker.MotionState == CityPedestrianMotionState.Riding ||
                        Mathf.Abs(walker.Position.y - p.y) >= 1.8f) continue;
                    float gap = passengerRadius + walker.AgentRadius;
                    if (PlanarSquaredDistance(p, walker.Position) < gap * gap) return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Logs once per changed reason. A waiter stands beside an open door
        /// for whole seconds, so an unconditional log would bury the stream.
        /// </summary>
        private static void ReportBoardingBlocked(Waiter waiter, string reason)
        {
            if (string.Equals(waiter.BlockedReason, reason, StringComparison.Ordinal))
            {
                return;
            }

            waiter.BlockedReason = reason;
            GameLog.Warning(
                "bus_npc",
                "board_blocked",
                GameLog.Field("stop", waiter.StopId),
                GameLog.Field("reason", reason),
                GameLog.Field("design", waiter.Walker.DesignId));
        }

        private bool IsTracked(CityPedestrianActor walker)
        {
            for (int index = 0; index < waiters.Count; index++)
            {
                if (waiters[index].Actor == walker)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryBuildPlan(
            Waiter waiter,
            CityBusActor actor,
            int seatIndex,
            CityBusPassengerDoor door,
            out CityBusRidePlan plan)
        {
            return CityBusRidePlan.TryCreate(
                actor,
                transferArea,
                new Vector3(0f, PassengerPelvisHeight, 0f),
                waiter.Walker.AgentRadius,
                door,
                streetSurfacePlan,
                seatIndex,
                // A walker's root already sits on the pavement surface, so it
                // needs no grounded-root offset the way the hero's rig does.
                0f,
                // The opposite-driver rule protects the hero's authored ride
                // clip and window camera. An ambient passenger uses the whole
                // cabin, half of which is on the driver's side.
                false,
                out plan);
        }

        /// <summary>
        /// Picks the door by the whole journey, pavement plus aisle, not by
        /// which one the walker happens to stand nearer. The doors sit
        /// `4.39 m` apart along the same kerb, so choosing by the outside leg
        /// alone can send a passenger the length of the cabin: the in-aisle
        /// walk runs to `6.60 m` that way against `2.56 m` when the seat is
        /// taken into account.
        /// </summary>
        private static CityBusPassengerDoor SelectBestDoor(
            Vector3 position,
            CityBusActor actor,
            int seatIndex)
        {
            CityBusAssetRegistry registry = actor.Presentation != null
                ? actor.Presentation.Registry
                : null;
            if (registry == null ||
                registry.FrontDoorEntryAnchor == null ||
                registry.RearDoorEntryAnchor == null)
            {
                return CityBusPassengerDoor.Front;
            }

            Transform seat = ResolveSeatAnchor(actor, seatIndex);
            Vector3 front = registry.FrontDoorEntryAnchor.position;
            Vector3 rear = registry.RearDoorEntryAnchor.position;
            if (seat == null)
            {
                return PlanarSquaredDistance(position, front) <=
                       PlanarSquaredDistance(position, rear)
                    ? CityBusPassengerDoor.Front
                    : CityBusPassengerDoor.Rear;
            }

            float frontCost = ResolveTransferPath(position, front, seat.position);
            float rearCost = ResolveTransferPath(position, rear, seat.position);
            return frontCost <= rearCost
                ? CityBusPassengerDoor.Front
                : CityBusPassengerDoor.Rear;
        }

        private static float ResolveTransferPath(
            Vector3 from,
            Vector3 door,
            Vector3 destination)
        {
            return Vector3.Distance(from, door) +
                   Vector3.Distance(door, destination);
        }

        // Both doors open onto the clear centre aisle. Make the right-angle
        // turn inside the doorway, then leave the aisle abreast of this place.
        internal static void GetAislePath(CityBusActor actor, Vector3 door, Vector3 place,
            out Vector3 aisleDoor, out Vector3 aislePlace)
        {
            Vector3 side = Vector3.Cross(actor.Presentation.CabinUp, actor.TravelDirection).normalized;
            Vector3 origin = actor.transform.position;
            aisleDoor = door - side * Vector3.Dot(door - origin, side);
            aislePlace = place - side * Vector3.Dot(place - origin, side);
        }

        internal static bool BeginDoorTransfer(CityPedestrianActor walker, CityBusActor actor, CityBusRidePlan plan,
            bool boarding, Vector3 pavement, out float path)
        {
            var registry = actor.Presentation.Registry;
            Plane floor = actor.Presentation.PassengerFloor;
            // The entry marker is inset into the aisle. The two stationary hinge
            // pivots mark the outer edge where the passenger must already be up.
            bool front = plan.PassengerDoor == CityBusPassengerDoor.Front;
            Transform first = front ? registry.FrontDoorForwardLeaf : registry.RearDoorForwardLeaf;
            Transform last = front ? registry.FrontDoorRearwardLeaf : registry.RearDoorRearwardLeaf;
            Vector3 sill = floor.ClosestPointOnPlane((first.position + last.position) * .5f);
            Vector3 place = floor.ClosestPointOnPlane(plan.SeatAnchor.position);
            Vector3 dock = boarding ? plan.EntryPose.RootPosition : plan.ExitPose.RootPosition;
            GetAislePath(actor, sill, place, out var aisleDoor, out var aislePlace);
            path = Vector3.Distance(pavement, dock) + Vector3.Distance(dock, sill) +
                Vector3.Distance(sill, aisleDoor) + Vector3.Distance(aisleDoor, aislePlace) +
                Vector3.Distance(aislePlace, place);
            return boarding
                ? walker.BeginTransfer(true, dock, place, actor.Rotation * plan.RideRootLocalRotation, sill, aisleDoor, aislePlace)
                : walker.BeginTransfer(false, aislePlace, pavement, plan.ExitPose.RootRotation, aisleDoor, sill, dock);
        }

        internal static bool TryGetCabinRootPose(CityBusActor actor, int place, out Vector3 local, out Quaternion rotation) =>
            CityBusRidePlan.TryCreateSeatedPose(actor, place, out local, out rotation, out _);

        internal static bool TryBindCabinPose(CityPedestrianPresentation body, CityBusActor actor, int place)
        {
            CityBusAssetRegistry registry = actor.Presentation.Registry;
            Transform anchor = CityBusRidePlan.PlaceAnchor(registry, place);
            if (CityBusActor.IsStandingPlace(place))
                return body.TryStandInBus(anchor, registry.StandingGripAnchors[place - CityBusActor.SeatCount], actor.Presentation.CabinUp);
            return CityPedestrianResources.TryGetArchetype(body.Registry.DesignId, out var archetype) &&
                body.TrySeat(anchor, archetype.SeatedRide, actor.Presentation.CabinUp, registry.StandingFloorAnchors[0].position,
                    actor.TravelDirection);
        }

        internal static bool BeginPassengerRide(CityPedestrianActor walker, CityBusActor actor, int place, bool preloaded)
        {
            CityBusAssetRegistry registry = actor.Presentation.Registry;
            Transform anchor = CityBusRidePlan.PlaceAnchor(registry, place);
            Vector3 up = actor.Presentation.CabinUp;
            if (CityBusActor.IsStandingPlace(place))
                return walker.BeginStandingRide(anchor, registry.StandingGripAnchors[place - CityBusActor.SeatCount], up, preloaded);
            var archetype = CityPedestrianDirector.GetActorArchetype(walker);
            Vector3 floor = registry.StandingFloorAnchors[0].position, facing = actor.TravelDirection;
            return archetype != null && (preloaded ? walker.BeginSeatedRide(anchor, archetype.SeatedRide, up, floor, facing) :
                walker.BeginRide(anchor, archetype.SeatedRide, up, floor, facing));
        }

        /// <summary>
        /// How long this walker may hold the doors, derived from the walk it
        /// actually has to make at its own authored pace. Padding covers the
        /// turn at the doorway and the arrival radius.
        /// </summary>
        private static float ResolveTransferBudget(float path, float speed)
        {
            if (!IsFinite(path) || !IsFinite(speed) || speed <= 0.01f)
            {
                return MinimumTransferBudget;
            }

            float expected = (path / speed * TransferBudgetSlack) +
                             TransferBudgetPadding;
            return Mathf.Clamp(
                expected,
                MinimumTransferBudget,
                MaximumTransferBudget);
        }

        private static Transform ResolveSeatAnchor(
            CityBusActor actor,
            int seatIndex)
        {
            CityBusAssetRegistry registry = actor.Presentation != null
                ? actor.Presentation.Registry
                : null;
            return CityBusRidePlan.PlaceAnchor(registry, seatIndex);
        }

        /// <summary>
        /// Detaches every ambient passenger so the bus may be pooled. A rider
        /// is 92 m away behind fog when this runs, so it returns to the
        /// pedestrian pool rather than being dropped on a road it is nowhere
        /// near.
        /// </summary>
        private void ReleaseEveryPassenger()
        {
            CityBusActor actor = busDirector != null
                ? busDirector.Actor
                : null;
            ReleaseCabinViews(actor);
            for (int index = waiters.Count - 1; index >= 0; index--)
            {
                Waiter waiter = waiters[index];
                // The bus is the authority on who is aboard, not the walker.
                // On teardown the pedestrian director may already have pooled
                // its actors and reset them to Dormant; keying off the walker
                // would then leave the occupant attached and the actor would
                // refuse to pool, which is the documented post-condition of
                // `CityBusActor.ReleasePresentation`.
                bool wasAboard = false;
                if (actor != null)
                {
                    actor.ReleaseServiceHold(waiter);
                    wasAboard = actor.ReleasePassenger(waiter);
                }

                if (!wasAboard && !waiter.IsAboard)
                {
                    continue;
                }

                if (pedestrians != null && (waiter.Resident < 0 ||
                    pedestrians.ResidentIndex(waiter.Actor) == waiter.Resident))
                {
                    pedestrians.ReleaseRouteBoundActor(waiter.Actor);
                }

                waiters.RemoveAt(index);
            }
        }

        private void ReleaseEveryWaiter()
        {
            ReleaseEveryPassenger();
            for (int index = waiters.Count - 1; index >= 0; index--)
            {
                waiters[index].Walker.CancelStopApproach();
            }

            waiters.Clear();
        }

        private int DrawRideStopCount()
        {
            int stopCount = busDirector.Plan != null
                ? busDirector.Plan.Stops.Count
                : 1;
            int span = Mathf.Max(1, stopCount - 1);
            return 1 + (int)(
                CityPedestrianStableHash.Combine(NextRandomUInt(), RideSalt) %
                (uint)span);
        }

        private float GetNextSpawnDelay()
        {
            float unit = CityPedestrianStableHash.ToUnitFloat(
                CityPedestrianStableHash.Combine(NextRandomUInt(), StopSalt));
            float delay = Mathf.Lerp(
                MinimumWaiterDelay,
                MaximumWaiterDelay,
                unit);
            return nightModeProvider()
                ? delay * NightWaiterDelayScale
                : delay;
        }

        private uint NextRandomUInt()
        {
            randomState ^= randomState << 13;
            randomState ^= randomState >> 17;
            randomState ^= randomState << 5;
            return randomState;
        }

        private static bool DefaultNightMode()
        {
            return GameTimeDayNightRules.IsNight(
                GameSessionState.GameTimeOfDayMinutes);
        }

        private static float PlanarSquaredDistance(Vector3 a, Vector3 b)
        {
            float deltaX = a.x - b.x;
            float deltaZ = a.z - b.z;
            return (deltaX * deltaX) + (deltaZ * deltaZ);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private sealed class Waiter
        {
            public Waiter(
                CityPedestrianActor actor,
                int stopIndex,
                string stopId,
                Vector3 slot)
            {
                Actor = actor;
                StopIndex = stopIndex;
                StopId = stopId;
                Slot = slot;
            }

            public CityPedestrianActor Actor { get; }
            public int Resident { get; set; } = -1;
            public CityPedestrianActor Walker => Actor;
            public int StopIndex { get; set; }
            public string StopId { get; set; }
            public Vector3 Slot { get; }
            public CityBusRidePlan Plan { get; set; }
            public int SeatIndex { get; set; } = -1;
            public Vector3 ExitLanding { get; set; }
            public Vector3 RideRootLocalPosition { get; set; }
            public Quaternion RideRootLocalRotation { get; set; } =
                Quaternion.identity;
            public int BoardedServiceOrdinal { get; set; }
            public int RideStopCount { get; set; } = 1;
            public float TransferElapsed { get; set; }
            public float TransferBudget { get; set; } = MinimumTransferBudget;
            public string BlockedReason { get; set; }

            public bool IsAboard => Actor != null &&
                                    Actor.IsAttachedToVehicle;
        }
    }
}
