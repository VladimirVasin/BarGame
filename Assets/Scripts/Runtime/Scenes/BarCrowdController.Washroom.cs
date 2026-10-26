using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class BarCrowdController
    {
        private const float WashroomBlockedTimeout = 12f;
        private const float HandReachSeconds = .45f;
        private enum WashroomPhase
        {
            ReleasingDrink, Standing, Approaching, OpeningForEntry, Entering, ClosingForVisit,
            Occupied, ReturningToDoor, WaitingToExit,
            OpeningForExit, LeavingWashroom, ClosingAfterVisit, Returning, Sitting
        }

        private sealed class WashroomVisit
        {
            public Transit Transition;
            public WashroomPhase Phase;
            public float Seconds, BlockedSeconds, DoorStartFraction;
            public int Corner;
            public bool Aborted, DrinkingWasEnabled, DoorContactReady;
            public NpcWashroomContactPose Contacts;
            public GameObject Owner => Transition.Patron.Registry != null ? Transition.Patron.Registry.gameObject : null;
            public BarPatron Patron => Transition.Patron;
            public readonly Vector3[] Route = new Vector3[9];
            public int RouteCount;
        }

        private BarWashroom washroom;
        private BarAisleTraffic washroomTraffic;
        private BarWashroomSchedule washroomSchedule;
        private WashroomVisit washroomVisit;
        private int[] observedSips;
        private bool[] residentSlots;

        public bool AutoScheduleWashroom { get; set; } = true;
        public bool HasWashroomVisitor => washroomVisit != null;
        public BarPatron WashroomPatron => washroomVisit?.Patron;
        public string WashroomPatronId => washroomVisit?.Patron.Anchor.Id ?? string.Empty;
        public string WashroomPhaseName => washroomVisit != null ? washroomVisit.Phase.ToString() : string.Empty;
        public float WashroomPhaseSeconds => washroomVisit?.Seconds ?? 0f;
        public float WashroomElapsedActiveSeconds => (float)(washroomSchedule?.ActiveSeconds ?? 0d);
        public Vector3 WashroomTarget { get; private set; }
        public string WashroomBlockedBy { get; private set; } = string.Empty;
        public float WashroomHandContactError { get; private set; }
        public int CompletedWashroomVisits { get; private set; }
        public string LastWashroomAbortReason { get; private set; } = string.Empty;

        public void ConfigureWashroom(BarWashroom composedWashroom, BarAisleTraffic traffic)
        {
            if (washroomVisit != null) throw new InvalidOperationException("Cannot replace an occupied washroom.");
            washroom = composedWashroom != null ? composedWashroom : throw new ArgumentNullException(nameof(composedWashroom));
            washroomTraffic = traffic != null ? traffic : throw new ArgumentNullException(nameof(traffic));
        }

        private void InitializeWashroomSchedule()
        {
            washroomSchedule = new BarWashroomSchedule(seed, slots.Length);
            observedSips = new int[slots.Length];
            residentSlots = new bool[slots.Length];
            UpdateWashroomResidents();
        }

        private void UpdateWashroomResidents()
        {
            for (int slot = 0; slot < slots.Length; slot++)
            {
                BarPatron patron = slots[slot];
                bool resident = patron != null && patron.Registry != null && patrons.Contains(patron);
                if (resident != residentSlots[slot])
                {
                    residentSlots[slot] = resident;
                    washroomSchedule.SetResident(slot, resident);
                    observedSips[slot] = patron?.Drinking?.Timeline.CompletedDrinks ?? 0;
                }
                if (!resident || patron.Drinking == null) continue;
                int completed = patron.Drinking.Timeline.CompletedDrinks;
                if (completed > observedSips[slot]) washroomSchedule.RecordSip(slot);
                observedSips[slot] = completed;
            }
        }

        private void AdvanceWashroomSchedule(float seconds)
        {
            if (washroomSchedule == null) return;
            if (washroomVisit != null && washroomVisit.Owner == null)
                RemoveDestroyedWashroomGuest();
            UpdateWashroomResidents();
            washroomSchedule.Advance(seconds, BarBusinessHours.IsOpen(GameSessionState.GameMinuteOfDay));
            if (!AutoScheduleWashroom || washroom == null || transit != null || washroomVisit != null) return;
            int candidate = washroomSchedule.ChooseCandidate();
            if (candidate >= 0) TryBeginWashroomVisit(visit.GetAnchor(candidate).Id);
        }

        public bool IsWashroomEligible(string anchorId)
        {
            if (washroomSchedule == null || !BarBusinessHours.IsOpen(GameSessionState.GameMinuteOfDay)) return false;
            for (int slot = 0; slot < slots.Length; slot++)
                if (slots[slot] != null && slots[slot].Anchor.Id == anchorId)
                    return washroomSchedule.CanVisit(slot) && IsSafeForWashroom(slots[slot]);
            return false;
        }

        private bool IsSafeForWashroom(BarPatron patron) => patron != null && patron.Registry != null &&
            !patron.IsTemporarilyAway && patrons.Contains(patron) &&
            (patron.Drinking == null || patron.Drinking.Timeline.Phase == BarPatronDrinkPhase.Rest) &&
            (canDepart == null || canDepart(patron));

        /// <summary>Requests the normal eligible physical visit; it never bypasses timers or hero ownership.</summary>
        public bool TryBeginWashroomVisit(string anchorId)
        {
            if (!IsInitialized || !isActiveAndEnabled || washroom == null || washroomTraffic == null ||
                washroomVisit != null || transit != null || GameTimeScaleRuntime.IsPaused ||
                !GameSessionState.IsGameTimeRunning || !BarBusinessHours.IsOpen(GameSessionState.GameMinuteOfDay)) return false;
            for (int slot = 0; slot < slots.Length; slot++)
            {
                BarPatron patron = slots[slot];
                if (patron == null || patron.Anchor.Id != anchorId || abandoned[slot] != null ||
                    !washroomSchedule.CanVisit(slot) || !IsSafeForWashroom(patron)) continue;
                Vector3? dockOverride = patron.Anchor.Role == BarNpcRole.StandingPatron
                    ? StandingWashroomDock(patron) : null;
                GameObject owner = patron.Registry.gameObject;
                if (!washroom.Access.TryReserveNpc(owner, patron.Registry.transform)) return false;
                if (!washroomTraffic.TryAcquire(owner)) { washroom.Access.ReleaseNpc(owner); return false; }
                if (!washroomSchedule.BeginVisit(slot))
                {
                    washroomTraffic.Release(owner); washroom.Access.ReleaseNpc(owner); return false;
                }
                bool drinkingEnabled = patron.Drinking != null && patron.Drinking.enabled;
                Transit transition = PrepareTransit(patron, slot, false, false, dockOverride);
                washroomVisit = new WashroomVisit
                {
                    Transition = transition, Phase = WashroomPhase.ReleasingDrink,
                    DrinkingWasEnabled = drinkingEnabled,
                    Contacts = new NpcWashroomContactPose(patron.Registry)
                };
                patron.IsTemporarilyAway = true;
                LastWashroomAbortReason = string.Empty;
                BuildWashroomRoute(washroomVisit);
                PresenceChanged?.Invoke();
                return true;
            }
            return false;
        }

        private void BuildWashroomRoute(WashroomVisit current)
        {
            Vector3 dock = current.Transition.Dock;
            current.Route[current.RouteCount++] = visit.Root.TransformPoint(dock);
            if (current.Patron.Anchor.Role == BarNpcRole.CounterPatron)
                current.Route[current.RouteCount++] = visit.Root.TransformPoint(new Vector3(dock.x, 0f, BarCounterSeatPlanner.ApproachZ));
            // The open aisle is east of both booth shells, then crosses north
            // of the rear bench before approaching the actual washroom leaf.
            float aisleEntryZ = current.Patron.Anchor.Role == BarNpcRole.CounterPatron
                ? BarCounterSeatPlanner.ApproachZ : dock.z;
            current.Route[current.RouteCount++] = visit.Root.TransformPoint(new Vector3(-2.75f, 0f, aisleEntryZ));
            current.Route[current.RouteCount++] = visit.Root.TransformPoint(new Vector3(-2.75f, 0f, 2.95f));
            current.Route[current.RouteCount++] = visit.Root.TransformPoint(new Vector3(-4.20f, 0f, 2.95f));
            current.Route[current.RouteCount++] = Grounded(washroom.Door.GetNpcDock(false, true));
        }

        private Vector3 StandingWashroomDock(BarPatron patron)
        {
            Transform wall = room.Find(patron.Anchor.Position.x < 0f ? "Front Wall Collision 1" : "Front Wall Collision 2");
            Transform table = room.Find(patron.Drinking.SurfaceRenderer.name + " Collision");
            BoxCollider wallBody = wall != null ? wall.GetComponent<BoxCollider>() : null;
            BoxCollider tableBody = table != null ? table.GetComponent<BoxCollider>() : null;
            if (wallBody == null || tableBody == null)
                throw new InvalidOperationException("A standing WC departure requires its actual front wall and table collision.");
            Bounds wallBounds = CollisionBoundsInVisit(wallBody);
            Bounds tableBounds = CollisionBoundsInVisit(tableBody);
            float gap = tableBounds.min.z - wallBounds.max.z;
            if (gap <= 2f * (BodyRadius + .025f))
                throw new InvalidOperationException("The standing guest has no capsule-clear departure corridor.");
            return new Vector3(patron.Anchor.Position.x, 0f, (wallBounds.max.z + tableBounds.min.z) * .5f);
        }

        private Bounds CollisionBoundsInVisit(BoxCollider collider)
        {
            Bounds local = new Bounds();
            Vector3 half = collider.size * .5f;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = collider.center + new Vector3((corner & 1) == 0 ? -half.x : half.x,
                    (corner & 2) == 0 ? -half.y : half.y, (corner & 4) == 0 ? -half.z : half.z);
                point = visit.Root.InverseTransformPoint(collider.transform.TransformPoint(point));
                if (corner == 0) local = new Bounds(point, Vector3.zero); else local.Encapsulate(point);
            }
            return local;
        }

        private void AdvanceWashroomVisit(float dt)
        {
            WashroomVisit current = washroomVisit;
            if (current.Owner == null) { RemoveDestroyedWashroomGuest(); return; }
            current.Contacts.RestoreBasePose();
            current.Seconds += dt;
            current.Transition.Seconds = current.Seconds;
            switch (current.Phase)
            {
                case WashroomPhase.ReleasingDrink:
                    AdvanceWashroomPresentation(0f, false, true);
                    if (current.Seconds >= ReleaseHandSeconds && current.Transition.ReleaseEndpointShown)
                        SetWashroomPhase(WashroomPhase.Standing);
                    break;
                case WashroomPhase.Standing:
                    AdvanceWashroomPresentation(0f, false, true);
                    if (current.Seconds >= SeatTransitionSeconds)
                    {
                        current.Patron.Registry.transform.position = current.Route[0];
                        current.Transition.Pose.Apply(0f);
                        current.Corner = 1;
                        SetWashroomPhase(WashroomPhase.Approaching);
                    }
                    break;
                case WashroomPhase.Approaching:
                    if (washroom.Access.IsHeroBlocking) { AbortWashroomApproach(); break; }
                    if (MoveWashroomToward(current.Route[current.Corner], dt))
                    {
                        current.Corner++;
                        if (current.Corner == current.RouteCount) SetWashroomPhase(WashroomPhase.OpeningForEntry);
                    }
                    else if (current.BlockedSeconds >= WashroomBlockedTimeout) AbortWashroomApproach();
                    break;
                case WashroomPhase.OpeningForEntry:
                    if (washroom.Access.IsHeroBlocking) { AbortWashroomApproach(); break; }
                    if (OperateWashroomDoor(false, true, dt)) { current.Corner = 0; SetWashroomPhase(WashroomPhase.Entering); }
                    else if (current.Seconds >= WashroomBlockedTimeout) AbortWashroomApproach();
                    break;
                case WashroomPhase.Entering:
                    if (washroom.Access.IsHeroBlocking) { YieldWashroomToHero(); break; }
                    if (MoveWashroomToward(current.Corner == 0 ? DoorCrossingPoint(true) :
                        Grounded(washroom.Door.GetNpcDock(true, false)), dt))
                    {
                        if (++current.Corner == 2) SetWashroomPhase(WashroomPhase.ClosingForVisit);
                    }
                    break;
                case WashroomPhase.ClosingForVisit:
                    if (washroom.Access.IsHeroBlocking) { YieldWashroomToHero(); break; }
                    if (OperateWashroomDoor(true, false, dt) && washroom.Access.TryLockNpc(current.Owner))
                    {
                        washroomTraffic.Release(current.Owner);
                        SetWashroomPhase(WashroomPhase.Occupied);
                    }
                    break;
                case WashroomPhase.Occupied:
                    // The closed room needs only occupancy time. The same
                    // actor remains safely inside without a hidden action scene.
                    IdleWashroom(dt);
                    if (current.Seconds >= washroomSchedule.VisitDurationSeconds)
                        SetWashroomPhase(WashroomPhase.ReturningToDoor);
                    break;
                case WashroomPhase.ReturningToDoor:
                    if (MoveWashroomToward(Grounded(washroom.Door.GetNpcDock(true, true)), dt))
                        SetWashroomPhase(WashroomPhase.WaitingToExit);
                    break;
                case WashroomPhase.WaitingToExit:
                    IdleWashroom(dt);
                    washroomTraffic.RequestReturn(current.Owner);
                    if (washroomTraffic.TryAcquire(current.Owner, true))
                    {
                        washroom.Access.BeginNpcExit(current.Owner);
                        SetWashroomPhase(WashroomPhase.OpeningForExit);
                    }
                    break;
                case WashroomPhase.OpeningForExit:
                    if (OperateWashroomDoor(true, true, dt)) { current.Corner = 0; SetWashroomPhase(WashroomPhase.LeavingWashroom); }
                    break;
                case WashroomPhase.LeavingWashroom:
                    // Reverse the same capsule-clear opening: align inside
                    // the jamb before crossing, then approach the outer leaf.
                    if (MoveWashroomToward(current.Corner == 0 ? DoorCrossingPoint(true) :
                        current.Corner == 1 ? DoorCrossingPoint(false) :
                        Grounded(washroom.Door.GetNpcDock(false, false)), dt))
                    {
                        if (++current.Corner == 3) SetWashroomPhase(WashroomPhase.ClosingAfterVisit);
                    }
                    break;
                case WashroomPhase.ClosingAfterVisit:
                    if (current.Aborted || washroom.Access.IsHeroBlocking || OperateWashroomDoor(false, false, dt))
                    {
                        washroom.Access.ReleaseNpc(current.Owner);
                        current.Corner = current.RouteCount - 1;
                        SetWashroomPhase(WashroomPhase.Returning);
                    }
                    break;
                case WashroomPhase.Returning:
                    if (MoveWashroomToward(current.Route[current.Corner], dt))
                    {
                        if (current.Corner == 0 && !TurnWashroomToward(
                            visit.Root.rotation * Quaternion.Euler(0f, current.Patron.Anchor.YawDegrees, 0f), dt)) break;
                        if (--current.Corner < 0) SetWashroomPhase(WashroomPhase.Sitting);
                    }
                    break;
                case WashroomPhase.Sitting:
                    AdvanceWashroomPresentation(0f, false, true);
                    if (current.Seconds >= SeatTransitionSeconds) CompleteWashroomVisit();
                    break;
            }
        }

        private Vector3 Grounded(Vector3 world)
        {
            world.y = visit.Root.position.y;
            return world;
        }

        private Vector3 DoorCrossingPoint(bool entering) =>
            Grounded(washroom.transform.TransformPoint(new Vector3(-4.20f, 0f, entering ? 4.55f : 3.25f)));

        private void AbortWashroomApproach()
        {
            WashroomVisit current = washroomVisit;
            RecordWashroomAbort("Approach aborted");
            current.Aborted = true;
            washroom.Access.ReleaseNpc(current.Owner);
            current.Corner = Mathf.Clamp(current.Corner - 1, 0, current.RouteCount - 1);
            SetWashroomPhase(WashroomPhase.Returning);
        }

        private void YieldWashroomToHero()
        {
            WashroomVisit current = washroomVisit;
            RecordWashroomAbort("Hero entered first");
            current.Aborted = true;
            washroom.Access.BeginNpcExit(current.Owner);
            if (washroom.transform.InverseTransformPoint(current.Patron.Registry.transform.position).z < 4.15f)
            {
                current.Corner = 1;
                SetWashroomPhase(WashroomPhase.LeavingWashroom);
            }
            else SetWashroomPhase(WashroomPhase.ReturningToDoor);
        }

        private void RecordWashroomAbort(string reason)
        {
            WashroomVisit current = washroomVisit;
            LastWashroomAbortReason = $"{reason}: phase={current.Phase}, seconds={current.Seconds:F2}, " +
                $"localBody={visit.Root.InverseTransformPoint(current.Patron.Registry.transform.position)}, " +
                $"target={WashroomTarget}, blocker={WashroomBlockedBy}, palmError={WashroomHandContactError:F4}";
        }

        private void SetWashroomPhase(WashroomPhase phase)
        {
            WashroomVisit current = washroomVisit;
            current.Phase = phase;
            current.Seconds = current.BlockedSeconds = 0f;
            current.Transition.Seconds = 0f;
            current.DoorStartFraction = washroom.Door.OpenFraction;
            current.DoorContactReady = false;
            WashroomBlockedBy = string.Empty;
        }

        private void IdleWashroom(float dt) => AdvanceWashroomPresentation(dt, false);

        private void AdvanceWashroomPresentation(float dt, bool moving, bool immediately = false)
        {
            washroomVisit.Patron.Presentation.Advance(dt, moving, immediately);
            washroomVisit.Contacts.CaptureBasePose();
        }

        private bool MoveWashroomToward(Vector3 target, float dt)
        {
            WashroomVisit current = washroomVisit;
            Transform actor = current.Patron.Registry.transform;
            WashroomTarget = target;
            WashroomBlockedBy = string.Empty;
            Vector3 offset = target - actor.position; offset.y = 0f;
            float distance = offset.magnitude;
            if (distance <= .015f) { IdleWashroom(dt); current.BlockedSeconds = 0f; return true; }
            Vector3 direction = offset / distance;
            actor.rotation = Quaternion.RotateTowards(actor.rotation, Quaternion.LookRotation(direction, visit.Root.up), TurnSpeed * dt);
            float facing = Mathf.Clamp01(1f - Vector3.Angle(actor.forward, direction) / 70f);
            float travel = Mathf.Min(distance, WalkingSpeed * dt * facing);
            // Door ownership moves authored collision with AutoSyncTransforms off.
            Physics.SyncTransforms();
            bool blocked = IsPathBlocked(actor.position, direction, travel, actor);
            if (blocked)
            {
                WashroomBlockedBy = TransitBlockedBy;
                current.BlockedSeconds += dt;
            }
            else { actor.position += direction * travel; current.BlockedSeconds = 0f; }
            AdvanceWashroomPresentation(dt, !blocked && travel > .0001f);
            return false;
        }

        private bool TurnWashroomToward(Quaternion facing, float dt)
        {
            Transform actor = washroomVisit.Patron.Registry.transform;
            actor.rotation = Quaternion.RotateTowards(actor.rotation, facing, TurnSpeed * dt);
            return Quaternion.Angle(actor.rotation, facing) < .2f;
        }

        private bool OperateWashroomDoor(bool inside, bool open, float dt)
        {
            WashroomVisit current = washroomVisit;
            if (Mathf.Abs(current.DoorStartFraction - (open ? 1f : 0f)) <= .001f) return true;
            Vector3 contact = washroom.Door.GetNpcContact(inside, open);
            Transform actor = current.Patron.Registry.transform;
            Vector3 toward = contact - actor.position; toward.y = 0f;
            if (toward.sqrMagnitude > .00001f) TurnWashroomToward(
                Quaternion.AngleAxis(-45f, visit.Root.up) * Quaternion.LookRotation(toward, visit.Root.up), dt);
            IdleWashroom(dt);
            if (current.Seconds < HandReachSeconds || !current.DoorContactReady) return false;
            float next = Mathf.MoveTowards(washroom.Door.OpenFraction, open ? 1f : 0f, dt / InteriorHingedDoorInteraction.SwingSeconds);
            if (!washroom.Door.TrySetNpcOpenFraction(current.Owner, next))
            {
                WashroomBlockedBy = "Washroom door sweep";
                return false;
            }
            return (open ? washroom.Door.IsOpen : washroom.Door.IsClosed) &&
                current.Seconds >= HandReachSeconds + InteriorHingedDoorInteraction.SwingSeconds + .3f;
        }

        private void CompleteWashroomVisit()
        {
            WashroomVisit current = washroomVisit;
            BarPatron patron = current.Patron;
            Transform body = patron.Registry.transform;
            body.localPosition = patron.Anchor.Position;
            body.localRotation = Quaternion.Euler(0f, patron.Anchor.YawDegrees, 0f);
            current.Transition.Pose.Apply(1f);
            if (current.Transition.Seat != null) patron.Presentation.TrySeat(current.Transition.Seat, current.Transition.Ride);
            patron.Presentation.Advance(0f, false, true);
            if (current.Transition.Vessel != null) current.Transition.Vessel.SetParent(body, true);
            patron.Registry.Animator.cullingMode = current.Transition.PreviousCulling;
            patron.IsTemporarilyAway = false;
            if (patron.Drinking != null)
            {
                patron.Drinking.enabled = current.DrinkingWasEnabled;
                patron.Drinking.SampleFinalPose();
            }
            washroomTraffic.Release(current.Owner);
            washroom.Access.ReleaseNpc(current.Owner);
            if (current.Aborted) washroomSchedule.Defer(current.Transition.Slot);
            else { washroomSchedule.CompleteVisit(current.Transition.Slot); CompletedWashroomVisits++; }
            washroomVisit = null;
            WashroomTarget = Vector3.zero;
            WashroomBlockedBy = string.Empty;
            WashroomHandContactError = 0f;
            PresenceChanged?.Invoke();
        }

        private void DisposeWashroomVisit()
        {
            WashroomVisit current = washroomVisit;
            if (current == null) return;
            if (current.Owner != null)
            {
                washroomTraffic?.Release(current.Owner);
                washroom?.Access.ReleaseNpc(current.Owner);
                current.Patron.IsTemporarilyAway = false;
                Destroy(current.Owner);
            }
            if (current.Transition.Vessel != null) Destroy(current.Transition.Vessel.gameObject);
            washroomVisit = null;
        }

        private void RemoveDestroyedWashroomGuest()
        {
            WashroomVisit current = washroomVisit;
            washroomSchedule.Defer(current.Transition.Slot);
            slots[current.Transition.Slot] = null;
            patrons.Remove(current.Patron);
            if (current.Transition.Seat != null) Destroy(current.Transition.Seat.gameObject);
            DisposeWashroomVisit();
            PresenceChanged?.Invoke();
        }
    }
}
