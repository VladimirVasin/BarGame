using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum VillageNeighbourTask
    {
        Home, Household, Walk, Wait, ReserveDoor, OpenDoor, CloseDoor,
        ReleaseDoor, ArriveHome, PickBasket, PutBasket, OpenGate, CloseGate,
        PickShovel, Shovel, PutShovel, ReserveRoomRoute, Workroom, PickBucket, FillBucket, PutBucket, FinishWaterVisit
    }

    internal sealed class VillageNeighbourStep
    {
        internal VillageNeighbourTask Kind;
        internal Vector3[] Points;
        internal Vector3 Facing;
        internal bool Indoors, InsideOperation, WaitForApproachClear;
        internal float Duration;
        internal string ClearingId;
        internal int ClearingStage;
    }

    public sealed class VillageNeighbourState
    {
        public VillageResidentPresentation Actor { get; internal set; }
        public VillageResidentRole Role => Actor.Role;
        public bool IsOutside { get; internal set; }
        public bool IsHome => !IsOutside && Steps.Count == 0;
        public bool IsBlocked { get; internal set; }
        public bool IsYielding { get; internal set; }
        public bool IsReactingToGust => GustTime >= 0f;
        public bool IsCarryingShovel => ShovelHeld;
        public bool IsCarryingBasket => BasketHeld;
        public bool IsCarryingBucket => BucketHeld;
        public bool IsWalkingOutside => Steps.Count > 0 && Steps.Peek().Kind == VillageNeighbourTask.Walk && !Steps.Peek().Indoors;
        public int CompletedOutings { get; internal set; }
        public VillageNeighbourTask Task => Steps.Count > 0 ? Steps.Peek().Kind :
            IsOutside ? VillageNeighbourTask.Household : VillageNeighbourTask.Home;
        internal readonly Queue<VillageNeighbourStep> Steps = new Queue<VillageNeighbourStep>();
        internal VillageResidentDoor Door;
        internal CapsuleCollider Body;
        internal AlpineVillagePlotDescriptor Home;
        internal int Slot, PointIndex;
        internal float Time, WalkTime, DoorWalkTime, DoorStartFraction, Speed, Cooldown, GustTime = -1f, GustCooldown;
        internal bool BasketHeld, ShovelHeld, BucketHeld, ContactDone, WasBlocked;
        internal Vector3? YieldPoint;
        internal VillageNeighbourState YieldTo;
    }

    public sealed partial class AlpineVillageLifeController
    {
        private readonly List<VillageNeighbourState> neighbours = new List<VillageNeighbourState>(6);
        private readonly List<Collider> neighbourSolids = new List<Collider>();
        private VillageNeighbourhoodPlan neighbourhood;
        private IReadOnlyDictionary<string, VillageResidentDoor> residentDoors;
        private AlpineVillageSnowTreading snowTreading;
        private Transform gate, gateFrame;
        private float gateFraction;
        private Vector3 shovelRestPosition;
        private Quaternion shovelRestRotation;
        private bool lastStrongGust;
        public IReadOnlyList<VillageNeighbourState> Neighbours => neighbours;
        public VillageNeighbourhoodPlan Neighbourhood => neighbourhood;
        public Transform Shovel { get; private set; }
        public Transform ClosedBasket { get; private set; }
        public int TotalDoorPassages { get; private set; }
        public int GustReactions { get; private set; }
        public int YieldCount { get; private set; }
        public int SnowWorkCycles { get; private set; }
        public int OutdoorCount
        {
            get { int count = 0; foreach (var n in neighbours) if (n.IsOutside) count++; return count; }
        }

        private void InitializeNeighbours(VillageResidentLibrary library,
            IReadOnlyDictionary<string, VillageResidentDoor> doors, AlpineVillageSnowTreading snow)
        {
            residentDoors = doors ?? throw new InvalidOperationException("Village household doors are missing.");
            snowTreading = snow;
            neighbourhood = new VillageNeighbourhoodPlan(Plan.Village);
            BuildNeighbourProps();
            for (int i = 0; i < 6; i++)
            {
                VillageResidentPresentation actor = i == 0 ? StationWorker : i == 1 ? Woman : library.Create((VillageResidentRole)i, transform);
                string homeId = i < 2 ? AlpineVillageLifePlan.WoodHouseId : i < 4 ? "village-house-08" : "village-house-11";
                var n = new VillageNeighbourState
                {
                    Actor = actor, Door = residentDoors[homeId], Home = neighbourhood.FindHouse(homeId),
                    Slot = i % 2, IsOutside = i < 2,
                    Cooldown = i == 4 ? 7f : i == 5 ? 15f : i == 2 ? 35f : 50f
                };
                neighbours.Add(n);
                if (i < 2) continue;
                InstallResident(actor, i, new[] { "", "", "village-repair-neighbor", "village-sewing-woman", "village-snow-neighbor", "village-basket-visitor" }[i]);
                // They remain real, active people behind the turn of a solid vestibule.
                actor.transform.SetPositionAndRotation(n.Door.HiddenDocks[n.Slot], Quaternion.LookRotation(n.Home.Facing));
                actor.Apply(VillageResidentAction.Idle, i * 0.31f);
            }
            foreach (var n in neighbours) n.Body = n.Actor.GetComponent<CapsuleCollider>();
            InitializeErrands();
            neighbourSolids.AddRange(solids);
            foreach (var door in residentDoors.Values)
                neighbourSolids.AddRange(door.HouseRoot.GetComponentsInChildren<MeshCollider>());
        }

        private void BuildNeighbourProps()
        {
            var quiet = neighbourhood.QuietHouse;
            var work = neighbourhood.WorkHouse;
            Quaternion q = Quaternion.LookRotation(quiet.Facing);
            gateFrame = Place(VillageLifePropKind.GatePosts, neighbourhood.GatePosition, q, true);
            gate = VillageLifePropLibrary.Create(VillageLifePropKind.GateLeaf, gateFrame).transform;
            gate.localPosition = VillageLifePropLibrary.GetAnchor(VillageLifePropKind.GatePosts, "Hinge");
            foreach (var mesh in gate.GetComponentsInChildren<MeshFilter>())
            {
                var collider = mesh.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh.sharedMesh;
                solids.Add(collider);
            }
            Transform rack = Place(VillageLifePropKind.ShovelRack, neighbourhood.ShovelRack, q, true);
            shovelRestPosition = rack.TransformPoint(VillageLifePropLibrary.GetAnchor(VillageLifePropKind.ShovelRack, "ShovelRest"));
            shovelRestRotation = Quaternion.LookRotation(-quiet.Facing);
            Shovel = Place(VillageLifePropKind.Shovel, shovelRestPosition, shovelRestRotation, false);
            Place(VillageLifePropKind.BasketStand, neighbourhood.BasketHome, q, true);
            Place(VillageLifePropKind.BasketStand, neighbourhood.BasketVisit, Quaternion.LookRotation(work.Facing), true);
            ClosedBasket = Place(VillageLifePropKind.ClosedBasket,
                neighbourhood.BasketHome + Vector3.up * AlpineVillageLifePlan.StandHeight,
                Quaternion.LookRotation(-quiet.Facing), false);
            foreach (var p in new[] { quiet, work })
                Place(VillageLifePropKind.PorchMat, neighbourhood.Yard(p, 0f, .45f), Quaternion.LookRotation(p.Facing), false);
        }

        private void AdvanceNeighbours(float dt, double minutes, float gust, Vector3 wind)
        {
            bool day = minutes >= 5 * 60 && minutes < 18.5 * 60;
            bool strong = gust >= 0.82f;
            bool rising = strong && !lastStrongGust;
            lastStrongGust = strong;
            foreach (var n in neighbours)
            {
                int i = (int)n.Role;
                n.Cooldown = Mathf.Max(0f, n.Cooldown - dt);
                n.GustCooldown = Mathf.Max(0f, n.GustCooldown - dt);
                if (PauseForGatePreparation(n)) continue;
                if (n.IsHome)
                {
                    if (day && workroom != null && workroom.WantsIndoorWork(n)) BeginIndoorWork(n);
                    else if (day && n.Cooldown <= 0f && OutdoorCount < 4 &&
                        !(n.Role == VillageResidentRole.SnowNeighbor && playerShovelReserved)) BeginOuting(n);
                    continue;
                }
                if (rising && n.IsOutside && n.GustCooldown <= 0f && CanWeatherPause(n))
                {
                    n.GustTime = 0f;
                    n.GustCooldown = 13f + i;
                    GustReactions++;
                }
                if (n.IsReactingToGust)
                {
                    SampleStoppedResident(n);
                    n.Actor.ApplyGust(n.GustTime, n.BasketHeld || n.ShovelHeld || n.BucketHeld || (i == 1 && (basketAttached || heldLog >= 0)));
                    if (i == 1) { FollowBasket(); FollowLoadingLog(); }
                    if (n.BasketHeld) FollowClosedBasket(n);
                    if (n.BucketHeld) FollowBucket(n);
                    n.GustTime += dt;
                    if (n.GustTime >= 2.2f) n.GustTime = -1f;
                    continue;
                }
                if (!day && n.Steps.Count == 0 && LegacyCanGoHome(n)) BeginLegacyReturn(n);
                if (n.Steps.Count > 0)
                {
                    if (n.Task == VillageNeighbourTask.Workroom)
                    {
                        if (workroom.AdvanceResident(n, dt, day)) FinishStep(n);
                    }
                    else AdvanceNeighbourTask(n, dt);
                }
                else if (i == 0) AdvanceStation(dt);
                else if (i == 1) AdvanceWoman(dt);
                if ((n.IsOutside || n.Task == VillageNeighbourTask.Workroom) && !n.IsReactingToGust) TryAutomaticGreeting(n.Role, n.Actor);
            }
        }

        private bool CanWeatherPause(VillageNeighbourState n)
        {
            if (n.Task == VillageNeighbourTask.Walk) return !n.Steps.Peek().Indoors;
            if (n.Task == VillageNeighbourTask.Wait) return true;
            if (n.Steps.Count > 0) return false;
            if (n.Role == VillageResidentRole.StationWorker) return CanTalk(n.Role);
            return n.Role == VillageResidentRole.WoodWoman &&
                WomanStage != VillageWomanStage.PickingUp && WomanStage != VillageWomanStage.PuttingDown && WomanStage != VillageWomanStage.Working &&
                WomanStage != VillageWomanStage.LoadingLog && WomanStage != VillageWomanStage.PlacingLog;
        }

        private void SampleStoppedResident(VillageNeighbourState n)
        {
            if (n.Role == VillageResidentRole.WoodWoman && n.Steps.Count == 0)
            {
                Woman.Apply(basketAttached || heldLog >= 0 ? VillageResidentAction.Carry : VillageResidentAction.Idle, walkTime);
                FollowLoadingLog();
                return;
            }
            n.Actor.Apply(n.ShovelHeld ? VillageResidentAction.ShovelHold : n.BasketHeld || n.BucketHeld ? VillageResidentAction.Carry : VillageResidentAction.Idle, n.WalkTime);
            if (n.ShovelHeld) HoldShovel(n);
            if (n.BucketHeld) FollowBucket(n);
        }

        private bool LegacyCanGoHome(VillageNeighbourState n) => (int)n.Role < 2 &&
            (n.Role == VillageResidentRole.StationWorker
                ? !stationReserved && !stationReturning && Mathf.Repeat(stationTime, StationWorker.ClipLength(VillageResidentAction.StationWork) + 7f) < 7f
                : route == null && (WomanStage == VillageWomanStage.Resting || WomanStage == VillageWomanStage.Waiting));

        private void BeginLegacyReturn(VillageNeighbourState n)
        {
            if (n.Role == VillageResidentRole.StationWorker)
            {
                Walk(n, new[] { n.Actor.transform.position, Plan.StationWork + Plan.StationForward * 1.1f, Plan.Village.Lane.Start }, Plan.Village.Lane.Sample(0).Forward);
                WalkStreet(n, 0f, n.Home.LaneDistance);
                Walk(n, new[] { Plan.Village.Lane.Sample(n.Home.LaneDistance).Position, n.Door.ExteriorDock }, -n.Home.Facing);
            }
            else Walk(n, new[] { n.Actor.transform.position, Plan.Yard(0f, 3.5f), n.Door.ExteriorDock }, -n.Home.Facing);
            EnterHome(n);
        }

        private void BeginOuting(VillageNeighbourState n)
        {
            n.IsOutside = true; // Reserve an outside slot for the whole visible exit.
            LeaveHome(n);
            if (TryBuildErrand(n)) { EnterHome(n); return; }
            switch (n.Role)
            {
                case VillageResidentRole.StationWorker:
                    Walk(n, new[] { n.Door.ExteriorDock, Plan.Village.Lane.Sample(n.Home.LaneDistance).Position }, -Plan.Village.Lane.Sample(n.Home.LaneDistance).Forward);
                    WalkStreet(n, n.Home.LaneDistance, 0f);
                    Walk(n, new[] { Plan.Village.Lane.Start, Plan.StationWork + Plan.StationForward * 1.1f, Plan.StationWork }, -Plan.StationForward);
                    break;
                case VillageResidentRole.WoodWoman:
                    bool basketWork = SelectNextBasket();
                    Walk(n, new[] { n.Door.ExteriorDock, Plan.Yard(0f, 3.5f), basketWork ? Plan.Yard(1.45f, 3.5f) : Plan.Rest,
                        basketWork ? Plan.Dock(Plan.Pickups[workBasket]) : Plan.Rest }, basketWork ? -Plan.Forward : Plan.Forward);
                    break;
                case VillageResidentRole.SnowNeighbor:
                    BuildSnowOuting(n); EnterHome(n); break;
                case VillageResidentRole.BasketVisitor:
                    BuildBasketVisit(n); EnterHome(n); break;
                default:
                    var destination = n.Role == VillageResidentRole.RepairNeighbor ? neighbourhood.QuietHouse : Plan.House;
                    Walk(n, neighbourhood.BetweenHouses(n.Home, destination), -destination.Facing);
                    Walk(n, new[] { destination.DoorDockPosition, neighbourhood.Yard(destination, .8f, 1.9f) }, destination.Facing);
                    Add(n, VillageNeighbourTask.Wait, n.Role == VillageResidentRole.RepairNeighbor ? 8f : 10f);
                    Walk(n, new[] { neighbourhood.Yard(destination, .8f, 1.9f), destination.DoorDockPosition }, destination.Facing);
                    Walk(n, neighbourhood.BetweenHouses(destination, n.Home), -n.Home.Facing);
                    EnterHome(n); break;
            }
        }

        private void WalkStreet(VillageNeighbourState n, float from, float to)
        {
            var points = new List<Vector3>();
            float sign = Mathf.Sign(to - from);
            if (sign == 0f) return;
            points.Add(Plan.Village.Lane.Sample(from).Position);
            for (float d = from + sign * 1.5f; sign * (to - d) > 0f; d += sign * 1.5f)
                points.Add(Plan.Village.Lane.Sample(d).Position);
            points.Add(Plan.Village.Lane.Sample(to).Position);
            Walk(n, points.ToArray(), Plan.Village.Lane.Sample(to).Forward * sign);
        }

        private void LeaveHome(VillageNeighbourState n)
        {
            Add(n, VillageNeighbourTask.ReserveDoor);
            var routeIn = n.Door.GetInteriorRoute(n.Slot);
            var points = new List<Vector3>();
            for (int i = routeIn.Length - 1; i >= 2; i--) points.Add(routeIn[i]);
            points.Add(n.Door.GetOperatingDock(0f, true));
            Walk(n, points.ToArray(), n.Door.GetOperatingFacing(true), true);
            AddDoor(n, true, true);
            Walk(n, new[] { n.Door.GetOperatingDock(1f, false) }, n.Door.GetOperatingFacing(false), true);
            AddDoor(n, false, false);
            Walk(n, new[] { n.Door.ExteriorDock }, n.Home.Facing, true);
            Add(n, VillageNeighbourTask.ReleaseDoor);
        }

        private void EnterHome(VillageNeighbourState n)
        {
            // A returning housemate waits beside the path, leaving room for
            // someone who still owns the door to complete the visible exit.
            VillageNeighbourStep approach = null;
            foreach (var queued in n.Steps)
                if (queued.Kind == VillageNeighbourTask.Walk) approach = queued;
            if (approach != null && approach.Points.Length > 0 &&
                Vector3.Distance(approach.Points[approach.Points.Length - 1], n.Door.ExteriorDock) < .05f)
                approach.Points[approach.Points.Length - 1] = neighbourhood.Yard(n.Home, n.Slot == 0 ? .9f : -.9f, 2.8f);
            n.Steps.Enqueue(new VillageNeighbourStep { Kind = VillageNeighbourTask.ReserveDoor, WaitForApproachClear = true });
            Walk(n, new[] { n.Door.GetOperatingDock(0f, false) }, n.Door.GetOperatingFacing(false), true);
            AddDoor(n, true, false);
            Walk(n, new[] { n.Door.GetOperatingDock(1f, true) }, n.Door.GetOperatingFacing(true), true);
            AddDoor(n, false, true);
            var routeIn = n.Door.GetInteriorRoute(n.Slot);
            var points = new List<Vector3> { n.Door.InteriorDock };
            for (int i = 3; i < routeIn.Length; i++) points.Add(routeIn[i]);
            Walk(n, points.ToArray(), -n.Home.Facing, true);
            Add(n, VillageNeighbourTask.ArriveHome);
        }

        private void BuildSnowOuting(VillageNeighbourState n)
        {
            var p = neighbourhood.QuietHouse;
            if (SnowClearing.IsComplete(Errands.PorchPatch.StableId))
            {
                Walk(n, new[] { n.Door.ExteriorDock, neighbourhood.Yard(p, .8f, 1.9f) }, p.Facing);
                Add(n, VillageNeighbourTask.Wait, 10f);
                Walk(n, new[] { neighbourhood.Yard(p, .8f, 1.9f), n.Door.ExteriorDock }, -p.Facing);
                return;
            }
            Walk(n, new[] { n.Door.ExteriorDock, neighbourhood.Yard(p, 0f, 2.3f), neighbourhood.Yard(p, 2.1f, 2.3f), neighbourhood.ShovelDock }, -p.Facing);
            Add(n, VillageNeighbourTask.PickShovel, 3f);
            Walk(n, new[] { neighbourhood.ShovelDock, Errands.PorchPatch.Dock }, p.Facing);
            QueueClearing(n, Errands.PorchPatch);
            Walk(n, new[] { Errands.PorchPatch.Dock, neighbourhood.ShovelDock }, -p.Facing);
            Add(n, VillageNeighbourTask.PutShovel, 3f);
            Walk(n, new[] { neighbourhood.ShovelDock, neighbourhood.Yard(p, 2.1f, 2.3f), neighbourhood.Yard(p, 0f, 2.3f), n.Door.ExteriorDock }, p.Facing);
            Add(n, VillageNeighbourTask.Wait, 7f);
        }

        private void BuildBasketVisit(VillageNeighbourState n)
        {
            var home = neighbourhood.QuietHouse;
            var visit = neighbourhood.WorkHouse;
            Vector3 insideGate = neighbourhood.Yard(home, -1.5f, 2.45f);
            Vector3 outsideGate = neighbourhood.Yard(home, -1.5f, 4.1f);
            Walk(n, new[] { n.Door.ExteriorDock, neighbourhood.Yard(home, 0f, 2.2f), insideGate, GateOperatingDock(false) }, home.Facing);
            Add(n, VillageNeighbourTask.OpenGate, 3.5f);
            Walk(n, new[] { insideGate, neighbourhood.Yard(home, -1.5f, 2.2f), neighbourhood.BasketDock(home) }, -home.Facing);
            Add(n, VillageNeighbourTask.PickBasket, 3f);
            Walk(n, new[] { neighbourhood.BasketDock(home), insideGate, outsideGate, neighbourhood.Yard(home, 0f, 4.1f), home.DoorDockPosition }, home.Facing);
            Walk(n, neighbourhood.BetweenHouses(home, visit), -visit.Facing);
            Walk(n, new[] { visit.DoorDockPosition, neighbourhood.Yard(visit, 0f, 2.2f), neighbourhood.Yard(visit, -1.5f, 2.2f), neighbourhood.BasketDock(visit) }, -visit.Facing);
            Add(n, VillageNeighbourTask.PutBasket, 3f);
            Add(n, VillageNeighbourTask.Wait, 8f);
            Add(n, VillageNeighbourTask.PickBasket, 3f);
            Walk(n, new[] { neighbourhood.BasketDock(visit), neighbourhood.Yard(visit, -1.5f, 2.2f), neighbourhood.Yard(visit, 0f, 2.2f), visit.DoorDockPosition }, visit.Facing);
            Walk(n, neighbourhood.BetweenHouses(visit, home), -home.Facing);
            Walk(n, new[] { home.DoorDockPosition, neighbourhood.Yard(home, 0f, 4.1f), outsideGate, insideGate, neighbourhood.BasketDock(home) }, -home.Facing);
            Add(n, VillageNeighbourTask.PutBasket, 3f);
            Walk(n, new[] { neighbourhood.BasketDock(home), insideGate, GateOperatingDock(true) }, home.Facing);
            Add(n, VillageNeighbourTask.CloseGate, 3.5f);
            Walk(n, new[] { insideGate, neighbourhood.Yard(home, 0f, 2.2f), n.Door.ExteriorDock }, -home.Facing);
        }

        private Vector3 GateOperatingDock(bool open)
        {
            Quaternion q = gateFrame.rotation * Quaternion.Euler(0f, open ? 92f : 0f, 0f);
            Vector3 handle = gateFrame.position + q * VillageLifePropLibrary.GetAnchor(VillageLifePropKind.GateLeaf, "Handle");
            return neighbourhood.Ground(handle - neighbourhood.QuietHouse.Facing * .5f - Vector3.up * .82f);
        }
        private static void Add(VillageNeighbourState n, VillageNeighbourTask task, float seconds = 0f) =>
            n.Steps.Enqueue(new VillageNeighbourStep { Kind = task, Duration = seconds });
        private static void Walk(VillageNeighbourState n, Vector3[] points, Vector3 facing, bool indoors = false) =>
            n.Steps.Enqueue(new VillageNeighbourStep { Kind = VillageNeighbourTask.Walk, Points = points, Facing = facing, Indoors = indoors });
        private static void AddDoor(VillageNeighbourState n, bool open, bool inside) =>
            n.Steps.Enqueue(new VillageNeighbourStep { Kind = open ? VillageNeighbourTask.OpenDoor : VillageNeighbourTask.CloseDoor,
                Duration = 3.5f, InsideOperation = inside });

        private void FinishStep(VillageNeighbourState n)
        {
            n.Steps.Dequeue(); n.Time = 0f; n.PointIndex = 0; n.ContactDone = false; n.Speed = 0f;
            n.IsBlocked = false; n.IsYielding = false; n.YieldPoint = null; n.YieldTo = null;
        }

        private void AdvanceNeighbourTask(VillageNeighbourState n, float dt)
        {
            var step = n.Steps.Peek();
            int i = (int)n.Role;
            if (step.Kind == VillageNeighbourTask.Wait && speechRemaining[i] > 0f)
            {
                n.Actor.Apply(n.ShovelHeld ? VillageResidentAction.ShovelHold : n.BasketHeld ? VillageResidentAction.Carry : VillageResidentAction.Idle,
                    0f, GreetingLook(i, n.Actor));
                if (n.ShovelHeld) HoldShovel(n);
                if (n.BasketHeld) FollowClosedBasket(n);
                return;
            }
            n.Time += dt;
            if (AdvanceErrandTask(n, step, dt)) return;
            switch (step.Kind)
            {
                case VillageNeighbourTask.ReserveRoomRoute:
                    n.Actor.Apply(VillageResidentAction.Idle, n.Time);
                    if (RoomRouteIsClear(n)) FinishStep(n);
                    break;
                case VillageNeighbourTask.ReserveDoor:
                    n.Actor.Apply(VillageResidentAction.Idle, n.Time);
                    if ((!step.WaitForApproachClear || DoorApproachIsClear(n)) && n.Door.TryReserve(n.Actor.transform)) FinishStep(n);
                    break;
                case VillageNeighbourTask.ReleaseDoor:
                    n.Door.Release(n.Actor.transform); TotalDoorPassages++; FinishStep(n); break;
                case VillageNeighbourTask.ArriveHome:
                    n.Door.Release(n.Actor.transform); TotalDoorPassages++;
                    n.IsOutside = false; n.CompletedOutings++; n.Cooldown = 35f + i * 7f;
                    FinishStep(n); break;
                case VillageNeighbourTask.Walk:
                    AdvanceNeighbourWalk(n, step, dt); break;
                case VillageNeighbourTask.OpenDoor:
                case VillageNeighbourTask.CloseDoor:
                    AdvanceNeighbourDoor(n, step, dt); break;
                case VillageNeighbourTask.OpenGate:
                case VillageNeighbourTask.CloseGate:
                    AdvanceNeighbourGate(n, step, dt); break;
                case VillageNeighbourTask.PickBasket:
                case VillageNeighbourTask.PutBasket:
                    bool picking = step.Kind == VillageNeighbourTask.PickBasket;
                    n.Actor.Apply(picking ? VillageResidentAction.Reach : VillageResidentAction.Place, n.Time);
                    if (picking && n.Time >= 1.5f) n.BasketHeld = true;
                    if (n.BasketHeld) FollowClosedBasket(n);
                    if (!picking && n.Time >= 1.5f && !n.ContactDone)
                    { n.BasketHeld = false; n.ContactDone = true; sound.PlayWood(ClosedBasket.position); }
                    if (n.Time >= step.Duration) FinishStep(n);
                    break;
                case VillageNeighbourTask.PickShovel:
                case VillageNeighbourTask.Shovel:
                case VillageNeighbourTask.PutShovel:
                    AdvanceShovel(n, step); break;
                default:
                    SampleStoppedResident(n);
                    if (n.BasketHeld) FollowClosedBasket(n);
                    if (n.Time >= step.Duration) FinishStep(n);
                    break;
            }
        }

        private void AdvanceNeighbourWalk(VillageNeighbourState n, VillageNeighbourStep step, float dt)
        {
            if (n.YieldPoint.HasValue && Vector3.Distance(n.Actor.transform.position, n.YieldPoint.Value) < .03f)
            {
                // Stay off the centre line until the other person has actually
                // passed. Immediately aiming at the old waypoint closes the gap again.
                Vector3 separation = n.YieldTo == null ? Vector3.one * 10f :
                    n.YieldTo.Actor.transform.position - n.Actor.transform.position;
                separation.y = 0f;
                if (separation.sqrMagnitude < 1.15f * 1.15f &&
                    (separation.sqrMagnitude >= .95f * .95f || !WidenYield(n)))
                {
                    n.Speed = Mathf.MoveTowards(n.Speed, 0f, dt * 2.2f);
                    n.IsBlocked = true; n.IsYielding = true;
                    SampleStoppedResident(n);
                    if (n.BasketHeld) FollowClosedBasket(n);
                    return;
                }
                if (separation.sqrMagnitude >= 1.15f * 1.15f)
                { n.YieldPoint = null; n.YieldTo = null; n.IsYielding = false; }
            }
            Vector3 target = n.YieldPoint ?? step.Points[n.PointIndex];
            Vector3 delta = target - n.Actor.transform.position;
            delta.y = 0f;
            float distance = delta.magnitude;
            bool last = !n.YieldPoint.HasValue && n.PointIndex == step.Points.Length - 1;
            Vector3 direction = distance > .008f ? delta / distance : n.Actor.transform.forward;
            Quaternion facing = Quaternion.LookRotation(last && distance < .025f ? step.Facing : direction);
            n.Actor.transform.rotation = Quaternion.RotateTowards(n.Actor.transform.rotation, facing, 135f * dt);
            float angle = Quaternion.Angle(n.Actor.transform.rotation, facing);
            Vector3 proposed = n.Actor.transform.position + direction * Mathf.Min(distance, dt * .82f);
            bool blocked = ResidentBlocks(n, proposed) || (!step.Indoors && !walkable.Contains(proposed, .26f));
            n.IsBlocked = blocked;
            float targetSpeed = !blocked && angle < 23f ? Mathf.Min(n.BasketHeld ? .68f : .82f, distance * 3f) : 0f;
            n.Speed = Mathf.MoveTowards(n.Speed, targetSpeed, dt * 2.2f);
            float amount = blocked ? 0f : Mathf.Min(distance, dt * n.Speed);
            if (amount > 0f)
            {
                Vector3 before = n.Actor.transform.position;
                var next = n.Actor.transform.position + direction * amount;
                next.y = Mathf.MoveTowards(next.y, step.Indoors ? target.y : neighbourhood.Ground(next).y,
                    dt * (step.Indoors ? .45f : .8f));
                n.Actor.transform.position = next;
                ObserveGatePassage(n, before, next);
            }
            n.WalkTime += dt * (n.Speed / .82f);
            if (n.ShovelHeld) n.Actor.ApplyShovelLocomotion(n.WalkTime, n.Speed);
            else n.Actor.ApplyLocomotion(n.WalkTime, n.Speed, n.BasketHeld || n.BucketHeld);
            if (n.BasketHeld) FollowClosedBasket(n);
            if (n.ShovelHeld) HoldShovel(n);
            if (n.BucketHeld) FollowBucket(n);
            if (distance < .025f && (!last || angle < 1.5f))
            {
                if (n.YieldPoint.HasValue) return;
                if (last) FinishStep(n);
                else n.PointIndex++;
            }
        }

        private bool ResidentBlocks(VillageNeighbourState n, Vector3 next)
        {
            Vector3 current = n.Actor.transform.position;
            bool blocked = HeroBlocks(current, next);
            bool peerAhead = false;
            foreach (var other in neighbours)
            {
                if (other == n || Mathf.Abs(other.Actor.transform.position.y - current.y) > 1.5f) continue;
                var peer = other.Actor.transform.position;
                float separation = Vector2.Distance(new Vector2(next.x, next.z), new Vector2(peer.x, peer.z));
                float previous = Vector2.Distance(new Vector2(current.x, current.z), new Vector2(peer.x, peer.z));
                if (separation < .73f && separation < previous + .0001f)
                {
                    blocked = true; peerAhead = true;
                    if (n.YieldPoint.HasValue && n.YieldTo == other) WidenYield(n);
                    if ((int)n.Role > (int)other.Role && n.Task == VillageNeighbourTask.Walk &&
                        !n.Steps.Peek().Indoors && !n.YieldPoint.HasValue)
                    {
                        Vector3 travel = next - current;
                        travel.y = 0f;
                        Vector3 side = Vector3.Cross(Vector3.up, travel.normalized);
                        for (int sign = 1; sign >= -1; sign -= 2)
                        {
                            Vector3 yield = neighbourhood.Ground(current + side * (.95f * sign));
                            if (!CanStepAside(n, current, yield)) continue;
                            n.YieldPoint = yield; n.YieldTo = other;
                            break;
                        }
                        if (!n.YieldPoint.HasValue)
                        {
                            // A wall or a person can block both sideways steps.
                            // Use the same checked retreat as an occupied yield
                            // point, even when no first sidestep was possible.
                            n.YieldTo = other;
                            if (!WidenYield(n)) n.YieldTo = null;
                        }
                    }
                }
            }
            n.IsYielding = peerAhead || n.YieldPoint.HasValue;
            if (blocked && !n.WasBlocked) YieldCount++;
            n.WasBlocked = blocked;
            return blocked;
        }

        private bool WidenYield(VillageNeighbourState n)
        {
            if (n.YieldTo == null) return false;
            Vector3 current = n.Actor.transform.position;
            Vector3 away = current - n.YieldTo.Actor.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < .001f) return false;
            // At a junction the other route may bend towards the initial
            // sidestep. Give that person more room instead of waiting nose to nose.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                float angle = attempt == 0 ? 0f : attempt == 1 ? 45f : -45f;
                Vector3 target = neighbourhood.Ground(current + Quaternion.Euler(0f, angle, 0f) * away.normalized * .85f);
                if (!CanStepAside(n, current, target)) continue;
                n.YieldPoint = target;
                return true;
            }
            return false;
        }

        private bool CanStepAside(VillageNeighbourState n, Vector3 from, Vector3 to)
        {
            for (int sample = 1; sample <= 7; sample++)
            {
                Vector3 point = neighbourhood.Ground(Vector3.Lerp(from, to, sample / 7f));
                if (!walkable.Contains(point, .27f) || HeroBlocks(from, point)) return false;
                foreach (var other in neighbours)
                {
                    if (other == n) continue;
                    Vector3 gap = point - other.Actor.transform.position;
                    if (Mathf.Abs(gap.y) < 1.5f && gap.x * gap.x + gap.z * gap.z < .73f * .73f) return false;
                }
                foreach (var solid in neighbourSolids)
                {
                    if (solid == null || !solid.enabled) continue;
                    if (Physics.ComputePenetration(n.Body, point, n.Actor.transform.rotation,
                        solid, solid.transform.position, solid.transform.rotation, out _, out float depth) && depth > .015f)
                        return false;
                }
            }
            return true;
        }

        private bool DoorApproachIsClear(VillageNeighbourState n)
        {
            Vector3 across = Vector3.Cross(Vector3.up, n.Home.Facing);
            foreach (var other in neighbours)
            {
                if (other == n) continue;
                Vector3 delta = other.Actor.transform.position - n.Home.DoorGroundPosition;
                float forward = Vector3.Dot(delta, n.Home.Facing);
                if (Mathf.Abs(delta.y) < 1.5f && Mathf.Abs(Vector3.Dot(delta, across)) < .73f &&
                    forward > 0f && forward < 3.6f) return false;
            }
            return true;
        }

        private void AdvanceNeighbourDoor(VillageNeighbourState n, VillageNeighbourStep step, float dt)
        {
            bool open = step.Kind == VillageNeighbourTask.OpenDoor;
            bool guestInsideWorkroom = workroom != null && n.Home.StableId == VillageWorkroomPlan.HouseId &&
                hero != null && workroom.Plan.ContainsInterior(hero.position);
            if (!n.ContactDone)
            {
                n.ContactDone = true; n.DoorStartFraction = n.Door.OpenFraction;
                // A visitor can leave this real room's door open. Residents
                // keep it open for the guest and continue their ordinary route.
                if ((open && n.Door.IsOpen) || (!open && (n.Door.IsClosed || guestInsideWorkroom)))
                { FinishStep(n); return; }
            }
            float progress = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 2.5f, n.Time));
            n.Door.SetOpenFraction(n.Actor.transform, Mathf.Lerp(n.DoorStartFraction, open ? 1f : 0f, progress));
            Vector3 dock = n.Door.GetOperatingDock(n.Door.OpenFraction, step.InsideOperation);
            Vector3 before = n.Actor.transform.position;
            n.Actor.transform.position = Vector3.MoveTowards(before, dock, dt * .9f);
            n.Actor.transform.rotation = Quaternion.RotateTowards(n.Actor.transform.rotation,
                Quaternion.LookRotation(n.Door.GetOperatingFacing(step.InsideOperation)), dt * 135f);
            float speed = Vector3.Distance(before, n.Actor.transform.position) / dt;
            n.DoorWalkTime += Vector3.Dot(n.Actor.transform.position - before, n.Actor.transform.forward) / .82f;
            n.Actor.ApplyDoor(open ? VillageResidentAction.DoorOpen : VillageResidentAction.DoorClose,
                n.Time, n.Door.Handle.position, speed, n.DoorWalkTime);
            if (!open && n.Time >= 2.5f && !n.Door.IsClosed && !guestInsideWorkroom) n.Time = 2.49f;
            if (n.Time >= step.Duration) FinishStep(n);
        }

        private void AdvanceNeighbourGate(VillageNeighbourState n, VillageNeighbourStep step, float dt)
        {
            bool open = step.Kind == VillageNeighbourTask.OpenGate;
            if (gateHelpReserved)
            {
                if (open && gateFraction > .95f) { FinishStep(n); return; }
                n.Time -= dt; n.IsBlocked = true; n.IsYielding = true;
                SampleStoppedResident(n);
                return;
            }
            float progress = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 2.5f, n.Time));
            float target = open ? progress : 1f - progress;
            if (hero != null && Vector3.Distance(hero.position, gateFrame.position + gateFrame.right * .54f) < .9f)
            { n.Time -= dt; target = gateFraction; n.IsBlocked = true; }
            else n.IsBlocked = false;
            if (!n.IsBlocked && n.Time - dt < 1f && n.Time >= 1f)
                sound.PlayHinge(gate.TransformPoint(VillageLifePropLibrary.GetAnchor(VillageLifePropKind.GateLeaf, "Handle")));
            gateFraction = target;
            gate.localRotation = Quaternion.Euler(0f, 92f * gateFraction, 0f);
            Vector3 handle = gate.TransformPoint(VillageLifePropLibrary.GetAnchor(VillageLifePropKind.GateLeaf, "Handle"));
            Vector3 dock = neighbourhood.Ground(handle - neighbourhood.QuietHouse.Facing * .5f - Vector3.up * .82f);
            Vector3 before = n.Actor.transform.position;
            n.Actor.transform.position = Vector3.MoveTowards(before, dock, dt * .9f);
            float speed = Vector3.Distance(before, n.Actor.transform.position) / dt;
            n.DoorWalkTime += Vector3.Dot(n.Actor.transform.position - before, n.Actor.transform.forward) / .82f;
            n.Actor.ApplyDoor(open ? VillageResidentAction.DoorOpen : VillageResidentAction.DoorClose,
                n.Time, handle, speed, n.DoorWalkTime);
            if (n.Time >= step.Duration) { sound.PlayWood(handle); FinishStep(n); }
        }

        private void FollowClosedBasket(VillageNeighbourState n)
        {
            Vector3 left = n.Actor.LeftGrip.position, right = n.Actor.RightGrip.position;
            Quaternion rotation = Quaternion.LookRotation(Vector3.Cross((right - left).normalized, Vector3.up));
            ClosedBasket.SetPositionAndRotation((right + left) * .5f - Vector3.up * .43f, rotation);
            n.Actor.ApplyHandContacts(ClosedBasket.TransformPoint(new Vector3(.29f, .43f, 0f)),
                ClosedBasket.TransformPoint(new Vector3(-.29f, .43f, 0f)));
        }

        private void HoldShovel(VillageNeighbourState n)
        {
            Pose pose = VillageResidentPresentation.SampleShovelPose(VillageResidentAction.ShovelHold, 0f);
            // Walking straightens the knees: bring the long tool up with actual
            // speed so its lower grip stays comfortably within the left arm.
            pose.position += Vector3.up * Mathf.Min(.14f, n.Speed * .17f);
            SetShovelPose(n, pose);
        }

        private void SetShovelPose(VillageNeighbourState n, Pose local)
        {
            Shovel.SetPositionAndRotation(n.Actor.transform.position + n.Actor.transform.rotation * local.position,
                n.Actor.transform.rotation * local.rotation);
            n.Actor.ApplyHandContacts(Shovel.TransformPoint(VillageLifePropLibrary.GetAnchor(VillageLifePropKind.Shovel, "RightGrip")),
                Shovel.TransformPoint(VillageLifePropLibrary.GetAnchor(VillageLifePropKind.Shovel, "LeftGrip")));
        }

        private void AdvanceShovel(VillageNeighbourState n, VillageNeighbourStep step)
        {
            var action = step.Kind == VillageNeighbourTask.PickShovel ? VillageResidentAction.ShovelPickUp :
                step.Kind == VillageNeighbourTask.PutShovel ? VillageResidentAction.ShovelPutBack : VillageResidentAction.ShovelWork;
            n.Actor.Apply(action, n.Time);
            float contact = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(n.Time / 1.5f));
            if (action == VillageResidentAction.ShovelPickUp && n.Time < 1.5f)
                n.Actor.ApplyHandContacts(Shovel.TransformPoint(VillageLifePropLibrary.GetAnchor(VillageLifePropKind.Shovel, "RightGrip")),
                    Shovel.TransformPoint(VillageLifePropLibrary.GetAnchor(VillageLifePropKind.Shovel, "LeftGrip")), contact);
            else if (action == VillageResidentAction.ShovelPutBack && n.Time >= 1.5f)
            {
                Shovel.SetPositionAndRotation(shovelRestPosition, shovelRestRotation);
                n.ShovelHeld = false;
                n.Actor.ApplyHandContacts(Shovel.TransformPoint(VillageLifePropLibrary.GetAnchor(VillageLifePropKind.Shovel, "RightGrip")),
                    Shovel.TransformPoint(VillageLifePropLibrary.GetAnchor(VillageLifePropKind.Shovel, "LeftGrip")),
                    Mathf.SmoothStep(1f, 0f, (n.Time - 1.5f) / 1.5f));
            }
            else
            {
                n.ShovelHeld = true;
                Pose pose = VillageResidentPresentation.SampleShovelPose(action, n.Time);
                if (action != VillageResidentAction.ShovelWork)
                {
                    float restWeight = action == VillageResidentAction.ShovelPickUp
                        ? 1f - Mathf.SmoothStep(0f, 1f, (n.Time - 1.5f) / 1.5f)
                        : Mathf.SmoothStep(0f, 1f, n.Time / 1.5f);
                    Vector3 authoredRest = n.Actor.transform.position + n.Actor.transform.rotation * new Vector3(0f, 0f, .48f);
                    pose.position += Quaternion.Inverse(n.Actor.transform.rotation) * (shovelRestPosition - authoredRest) * restWeight;
                }
                SetShovelPose(n, pose);
            }
            if (step.Kind == VillageNeighbourTask.Shovel && n.Time >= .8f && !n.ContactDone)
            {
                n.ContactDone = true; SnowWorkCycles++;
                snowTreading?.Press(Shovel.position);
                if (!string.IsNullOrEmpty(step.ClearingId)) SnowClearing.Advance(step.ClearingId, step.ClearingStage);
                sound.PlayScrape(Shovel.position);
            }
            if (n.Time >= step.Duration) FinishStep(n);
        }
    }
}
