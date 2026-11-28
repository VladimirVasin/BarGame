using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A detour owned by an existing street encounter. Its street route stays parked
    /// at the departure point while the module owns the seat and the authored contacts.</summary>
    internal struct CityPlaygroundVisit
    {
        private const float VisitRadius = 16f;
        private const float BodyRadius = .23f;
        private const float RideDuration = 10f;
        private const float ApproachTimeout = 7f;
        private const float ApproachTurnRate = 260f;
        private const float CourtProbeSpacing = .17f;
        private const float PartnerWaitSeconds = 12f;

        internal enum Phase
        {
            None, Approach, Dock, Mount, Ride, Brake, Dismount, Leave, Return,
            CourtJoin, CourtDock, Bars, FarTurn, BeamDock, Beam, NearTurn, CourtLeave,
            SlideApproach, Slide, SlideExitSide, SlideReturnSide,
            SeesawJoin, SeesawApproach, SeesawWing, SeesawDock, SeesawWait, SeesawRide,
            SeesawWingOut, SeesawApproachOut, SeesawJoinOut,
            BalanceJoin, BalanceBranch, BalanceDock, BalancePlay, BalanceBranchOut, BalanceJoinOut
        }
        internal Phase CurrentPhase { get; private set; }
        internal CityPlaygroundSwing Seat { get; private set; }
        private CityCourtyardPlayground court;
        private CityCourtyardPlayground.Crossing courtCrossing;
        private CityPlaygroundSeesaw seesaw;
        private CityPlaygroundBalanceStation balance;
        private int seesawEnd;
        private Vector3 streetPoint, sidePoint, stagingPoint, dockPoint, courtJoin;
        private float elapsed, blocked, rideAmount;
        private bool sideReached, descentStarted;

        internal bool TryStart(CityPlaygroundSwing[] seats, int encounter, Vector3 feet,
            CityCourtyardPlayground courtyard = null, CityCourtyardPlayground slide = null,
            CityCourtyardPlayground bridge = null, CityPlaygroundSeesaw pair = null,
            IReadOnlyList<CityPlaygroundBalanceStation> balances = null)
        {
            if ((pair != null && pair.FreeEnd == 1 && TryStartSeesaw(pair, encounter, feet)) ||
                ((encounter & 1) == 0 && TryStartBalance(balances, encounter, feet)) ||
                TryStartSeesaw(pair, encounter, feet) ||
                TryStartCourt(courtyard, encounter, feet) || TryStartCourt(slide, encounter, feet) ||
                TryStartCourt(bridge, encounter, feet)) return true;
            foreach (CityPlaygroundSwing seat in seats)
            {
                if (!seat.isActiveAndEnabled || seat.Rider != 0 ||
                    (feet - seat.Staging).sqrMagnitude > VisitRadius * VisitRadius ||
                    !TryGround(seat.Staging, out Vector3 stage) ||
                    !TryGround(seat.Dock, out Vector3 dock) || Mathf.Abs(dock.y - seat.Dock.y) > .075f ||
                    !ClearSegment(stage, dock))
                    continue;

                Vector3 side = stage;
                if (!ClearOutsideArc(seat, feet, stage))
                {
                    Vector3 right = seat.RestRotation * Vector3.right;
                    Vector3 frameCenter = seat.transform.parent.position;
                    float sign = Mathf.Sign(Vector3.Dot(feet - frameCenter, right));
                    Vector3 outer = frameCenter + right * (sign * 2.8f) + seat.RestForward * CityPlaygroundSwing.StagingOffset;
                    if (!TryGround(outer, out side) ||
                        !ClearOutsideArc(seat, feet, side) ||
                        !ClearOutsideArc(seat, side, stage))
                        continue;
                }

                if (!seat.TryReserve(encounter)) continue;
                Seat = seat;
                streetPoint = feet;
                sidePoint = side;
                stagingPoint = stage;
                dockPoint = dock;
                SetPhase(Phase.Approach);
                return true;
            }
            return TryStartBalance(balances, encounter, feet);
        }

        private bool TryStartBalance(IReadOnlyList<CityPlaygroundBalanceStation> choices, int encounter, Vector3 feet)
        {
            if (choices == null) return false;
            for (int index = 0; index < choices.Count; index++)
            {
                CityPlaygroundBalanceStation station = choices[index];
                if (!station.Available || (feet - station.Plan.RouteJoin).sqrMagnitude >= 25f ||
                    !TryGround(station.Plan.RouteJoin, out Vector3 join) ||
                    !TryGround(station.Plan.Branch, out Vector3 branch) ||
                    !TryGround(station.Dock.position, out Vector3 dock) ||
                    !ClearSegment(feet, join, spacing: CourtProbeSpacing) ||
                    !ClearSegment(join, branch, spacing: CourtProbeSpacing) ||
                    !ClearSegment(branch, dock, spacing: CourtProbeSpacing)) continue;
                station.Reserve(encounter);
                balance = station;
                streetPoint = feet;
                courtJoin = join;
                sidePoint = branch;
                dockPoint = dock;
                SetPhase(Phase.BalanceJoin);
                return true;
            }
            return false;
        }

        private bool TryStartCourt(CityCourtyardPlayground site, int encounter, Vector3 feet)
        {
            if (site == null || !site.CanReserve || (feet - site.Site.RouteJoin).sqrMagnitude >= 25f ||
                !TryGround(site.Site.RouteJoin, out Vector3 join) ||
                !ClearSegment(feet, join, spacing: CourtProbeSpacing))
                return false;
            CityCourtyardPlayground.Crossing crossing = site.Next;
            Vector3 entry = crossing.Entry.position;
            Vector3 approach = site.Site.HasSlide ? site.SideEntry : entry;
            if (!ClearSegment(join, approach, spacing: CourtProbeSpacing) ||
                site.Site.HasSlide && !ClearSegment(approach, entry, spacing: CourtProbeSpacing) ||
                !site.TryReserve(encounter))
                return false;
            court = site;
            courtCrossing = crossing;
            courtJoin = join;
            streetPoint = feet;
            SetPhase(Phase.CourtJoin);
            return true;
        }

        private bool TryStartSeesaw(CityPlaygroundSeesaw pair, int encounter, Vector3 feet)
        {
            if (pair == null || (feet - pair.Site.RouteJoin).sqrMagnitude >= 25f) return false;
            int end = pair.FreeEnd;
            if (end < 0 ||
                !TryGround(pair.Site.RouteJoin, out Vector3 join) ||
                !TryGround(pair.Dock(end), out Vector3 dock) ||
                !ClearSegment(feet, join, spacing: CourtProbeSpacing) ||
                !ClearSegment(join, pair.Approach, spacing: CourtProbeSpacing) ||
                !ClearSegment(pair.Approach, pair.Wing(end), spacing: CourtProbeSpacing) ||
                !ClearSegment(pair.Wing(end), dock, spacing: CourtProbeSpacing)) return false;

            pair.Reserve(end, encounter);
            seesaw = pair;
            seesawEnd = end;
            streetPoint = feet;
            courtJoin = join;
            stagingPoint = pair.Approach;
            sidePoint = pair.Wing(end);
            dockPoint = dock;
            SetPhase(Phase.SeesawJoin);
            return true;
        }

        internal bool Advance(CityPedestrianPresentation body, int encounter, float speed, float delta, bool daylight = true)
        {
            if (CurrentPhase >= Phase.BalanceJoin)
                return AdvanceBalance(body, encounter, speed, delta, daylight);
            if (CurrentPhase >= Phase.SeesawJoin)
                return AdvanceSeesaw(body, encounter, speed, delta, daylight);
            if (CurrentPhase >= Phase.CourtJoin)
                return AdvanceCourt(body, encounter, speed, delta);
            if (CurrentPhase < Phase.Return && (Seat == null || !Seat.isActiveAndEnabled || Seat.Rider != encounter))
            {
                body.ClearAuthoredAction();
                SetPhase(Phase.Return);
            }

            elapsed += delta;
            switch (CurrentPhase)
            {
                case Phase.Approach:
                    if (!sideReached) sideReached = WalkTo(body, sidePoint, speed, delta);
                    else if (WalkTo(body, stagingPoint, speed, delta)) SetPhase(Phase.Dock);
                    if (blocked >= ApproachTimeout) SetPhase(Phase.Return);
                    break;
                case Phase.Dock:
                    if (!Seat.ReadyToMount && (body.transform.position - stagingPoint).sqrMagnitude < .01f)
                    {
                        body.Advance(delta, false);
                        break;
                    }
                    if (!WalkTo(body, dockPoint, speed * .8f, delta))
                    {
                        if (blocked >= ApproachTimeout) SetPhase(Phase.Leave);
                        break;
                    }
                    if (!TurnToDock(body, delta)) break;
                    SetPhase(Phase.Mount);
                    Sample(body, CityFairChildAction.SwingEnter, 0f);
                    break;
                case Phase.Mount:
                    float mountTime = CityFairChildActions.Duration(CityFairChildAction.SwingEnter);
                    Sample(body, CityFairChildAction.SwingEnter, elapsed / mountTime);
                    if (elapsed >= mountTime) SetPhase(Phase.Ride);
                    break;
                case Phase.Ride:
                    rideAmount = Mathf.MoveTowards(rideAmount, 1f, delta * .43f);
                    float cycle = elapsed * Mathf.PI * .65f;
                    Seat.DriveRider(encounter, Mathf.Sin(cycle) * 17f * rideAmount);
                    Sample(body, CityFairChildAction.SwingPump, PhaseOfLegs());
                    if (elapsed >= RideDuration) SetPhase(Phase.Brake);
                    break;
                case Phase.Brake:
                    Seat.DriveRider(encounter, 0f);
                    rideAmount = Mathf.MoveTowards(rideAmount, 0f, delta * .75f);
                    Sample(body, CityFairChildAction.SwingPump, PhaseOfLegs());
                    if (rideAmount <= 0f && Seat.ReadyToMount && ClearPoint(dockPoint))
                        SetPhase(Phase.Dismount);
                    break;
                case Phase.Dismount:
                    float dismountTime = CityFairChildActions.Duration(CityFairChildAction.SwingExit);
                    Sample(body, CityFairChildAction.SwingExit, elapsed / dismountTime);
                    if (elapsed >= dismountTime)
                    {
                        body.transform.SetPositionAndRotation(dockPoint, Seat.RestRotation);
                        body.ClearAuthoredAction();
                        SetPhase(Phase.Leave);
                    }
                    break;
                case Phase.Leave:
                    if (WalkTo(body, stagingPoint, speed, delta)) SetPhase(Phase.Return);
                    break;
                case Phase.Return:
                    if (Seat != null) Seat.ReleaseRider(encounter);
                    if (sideReached) sideReached = !WalkTo(body, sidePoint, speed, delta);
                    else if (WalkTo(body, streetPoint, speed, delta))
                    {
                        Cancel(body, encounter);
                        return false;
                    }
                    break;
            }
            return true;

        }

        private float PhaseOfLegs() =>
            Mathf.InverseLerp(-.6f, .6f, -Seat.AngularSpeed) * rideAmount;

        internal void Cancel(CityPedestrianPresentation body, int encounter)
        {
            if (Seat != null) Seat.ReleaseRider(encounter);
            if (court != null) court.Release(encounter);
            if (seesaw != null) seesaw.Release(seesawEnd, encounter);
            if (balance != null) balance.Release(encounter);
            if (CurrentPhase >= Phase.Mount && CurrentPhase <= Phase.Dismount ||
                CurrentPhase == Phase.Bars || CurrentPhase == Phase.Beam || CurrentPhase == Phase.Slide ||
                CurrentPhase == Phase.SeesawRide || CurrentPhase == Phase.BalancePlay)
                body.ClearAuthoredAction();
            this = default;
        }

        private bool AdvanceBalance(CityPedestrianPresentation body, int encounter, float speed, float delta, bool daylight)
        {
            if (balance == null || !balance.isActiveAndEnabled || balance.Visitor != encounter)
            {
                Cancel(body, encounter);
                return false;
            }
            elapsed += delta;
            switch (CurrentPhase)
            {
                case Phase.BalanceJoin:
                    if (!daylight || blocked >= ApproachTimeout) EndBalance(encounter);
                    else if (WalkTo(body, courtJoin, speed, delta)) SetPhase(Phase.BalanceBranch);
                    break;
                case Phase.BalanceBranch:
                    if (!daylight || blocked >= ApproachTimeout) SetPhase(Phase.BalanceJoinOut);
                    else if (WalkTo(body, sidePoint, speed, delta)) SetPhase(Phase.BalanceDock);
                    break;
                case Phase.BalanceDock:
                    if (!daylight || blocked >= ApproachTimeout) SetPhase(Phase.BalanceBranchOut);
                    else if (WalkTo(body, dockPoint, speed, delta) && TurnTo(body, balance.transform.rotation, delta))
                        SetPhase(Phase.BalancePlay);
                    break;
                case Phase.BalancePlay:
                    const float hold = 1.8f;
                    float mount = CityFairChildActions.Duration(CityFairChildAction.BalanceStep);
                    float end = 2f * mount + hold;
                    if (elapsed >= mount + hold && !descentStarted)
                    {
                        descentStarted = ClearPoint(dockPoint);
                        if (!descentStarted) elapsed = mount + hold;
                    }
                    float phase = Mathf.Clamp01(Mathf.Min(elapsed, end - elapsed) / mount);
                    Vector3 basis = Vector3.Lerp(dockPoint, balance.Dock.position, phase);
                    SamplePose(body, CityFairChildAction.BalanceStep, phase, basis, balance.transform.rotation, true);
                    if (elapsed >= end)
                    {
                        body.ClearAuthoredAction();
                        SetPhase(Phase.BalanceBranchOut);
                    }
                    break;
                case Phase.BalanceBranchOut:
                    if (WalkTo(body, sidePoint, speed, delta)) SetPhase(Phase.BalanceJoinOut);
                    break;
                case Phase.BalanceJoinOut:
                    if (WalkTo(body, courtJoin, speed, delta)) EndBalance(encounter);
                    break;
            }
            return true;
        }

        private void EndBalance(int encounter)
        {
            balance.Release(encounter);
            sideReached = false;
            SetPhase(Phase.Return);
        }

        private bool AdvanceSeesaw(CityPedestrianPresentation body, int encounter, float speed, float delta, bool daylight)
        {
            if (seesaw == null || !seesaw.Owns(seesawEnd, encounter))
            {
                Cancel(body, encounter);
                return false;
            }
            elapsed += delta;
            switch (CurrentPhase)
            {
                case Phase.SeesawJoin:
                    if (!daylight || blocked >= ApproachTimeout) EndSeesawVisit(encounter);
                    else if (WalkTo(body, courtJoin, speed, delta)) SetPhase(Phase.SeesawApproach);
                    break;
                case Phase.SeesawApproach:
                    if (!daylight || blocked >= ApproachTimeout) SetPhase(Phase.SeesawJoinOut);
                    else if (WalkToGround(body, stagingPoint, speed, delta)) SetPhase(Phase.SeesawWing);
                    break;
                case Phase.SeesawWing:
                    if (!daylight || blocked >= ApproachTimeout) SetPhase(Phase.SeesawApproachOut);
                    else if (WalkToGround(body, sidePoint, speed, delta)) SetPhase(Phase.SeesawDock);
                    break;
                case Phase.SeesawDock:
                    if (!daylight || blocked >= ApproachTimeout) SetPhase(Phase.SeesawWingOut);
                    else if (WalkTo(body, dockPoint, speed, delta) && TurnTo(body, seesaw.Facing(seesawEnd), delta))
                    {
                        seesaw.MarkReady(seesawEnd, true);
                        SetPhase(Phase.SeesawWait);
                    }
                    break;
                case Phase.SeesawWait:
                    if (seesaw.Playing)
                    {
                        SetPhase(Phase.SeesawRide);
                        SampleSeesaw(body);
                    }
                    else if (!daylight || elapsed >= PartnerWaitSeconds)
                    {
                        seesaw.MarkReady(seesawEnd, false);
                        SetPhase(Phase.SeesawWingOut);
                    }
                    else body.Advance(delta, false);
                    break;
                case Phase.SeesawRide:
                    SampleSeesaw(body);
                    if (seesaw.Seconds >= CityPlaygroundSeesaw.Finish)
                    {
                        body.ClearAuthoredAction();
                        SetPhase(Phase.SeesawWingOut);
                    }
                    break;
                case Phase.SeesawWingOut:
                    if (WalkToGround(body, sidePoint, speed, delta)) SetPhase(Phase.SeesawApproachOut);
                    break;
                case Phase.SeesawApproachOut:
                    if (WalkToGround(body, stagingPoint, speed, delta)) SetPhase(Phase.SeesawJoinOut);
                    break;
                case Phase.SeesawJoinOut:
                    if (WalkTo(body, courtJoin, speed, delta)) EndSeesawVisit(encounter);
                    break;
            }
            return true;
        }

        private void EndSeesawVisit(int encounter)
        {
            seesaw.Release(seesawEnd, encounter);
            sideReached = false;
            SetPhase(Phase.Return);
        }

        private void SampleSeesaw(CityPedestrianPresentation body)
        {
            float time = seesaw.Seconds;
            float phase = time <= CityPlaygroundSeesaw.DismountStart
                ? Mathf.Clamp01(time / CityPlaygroundSeesaw.MountSeconds)
                : Mathf.Clamp01((CityPlaygroundSeesaw.Finish - time) / CityPlaygroundSeesaw.MountSeconds);
            Vector3 basis = seesaw.MovingDock(seesawEnd);
            basis.y += (dockPoint.y - seesaw.Dock(seesawEnd).y) * (1f - Mathf.SmoothStep(0f, 1f, phase));
            SamplePose(body, CityFairChildAction.SeesawMount, phase, basis, seesaw.Facing(seesawEnd, true), true);
        }

        private bool AdvanceCourt(CityPedestrianPresentation body, int encounter, float speed, float delta)
        {
            if (court == null || !court.isActiveAndEnabled || court.Visitor != encounter)
            {
                Cancel(body, encounter);
                return false;
            }

            elapsed += delta;
            switch (CurrentPhase)
            {
                case Phase.CourtJoin:
                    if (WalkTo(body, courtJoin, speed, delta))
                        SetPhase(court.Site.HasSlide ? Phase.SlideApproach : Phase.CourtDock);
                    else if (blocked >= ApproachTimeout) BeginCourtReturn(encounter);
                    break;
                case Phase.SlideApproach:
                    if (WalkToGround(body, court.SideEntry, speed, delta)) SetPhase(Phase.CourtDock);
                    else if (blocked >= ApproachTimeout) SetPhase(Phase.CourtLeave);
                    break;
                case Phase.CourtDock:
                    if (ApproachCrossing(body, courtCrossing, speed, delta))
                        SetPhase(court.Site.HasSlide ? Phase.Slide : Phase.Bars);
                    else if (blocked >= ApproachTimeout)
                        SetPhase(court.Site.HasSlide ? Phase.SlideReturnSide : Phase.NearTurn);
                    break;
                case Phase.Bars:
                    if (Cross(body, court.Bars)) SetPhase(Phase.FarTurn);
                    break;
                case Phase.FarTurn:
                    if (WalkToGround(body, court.FarTurn, speed, delta)) SetPhase(Phase.BeamDock);
                    break;
                case Phase.BeamDock:
                    if (ApproachCrossing(body, court.Beam, speed, delta)) SetPhase(Phase.Beam);
                    break;
                case Phase.Beam:
                    if (Cross(body, court.Beam)) SetPhase(Phase.NearTurn);
                    break;
                case Phase.Slide:
                    // Wait seated at the top for a free runout. Once moving,
                    // keep the descent continuous through the standing pose.
                    float startOfDescent = courtCrossing.DescentTime;
                    if (!descentStarted && elapsed >= startOfDescent)
                    {
                        descentStarted = ClearPoint(courtCrossing.Exit.position);
                        if (!descentStarted) elapsed = startOfDescent;
                    }
                    if (Cross(body, courtCrossing))
                    {
                        court.CompleteCrossing(encounter);
                        SetPhase(Phase.SlideExitSide);
                    }
                    break;
                case Phase.SlideExitSide:
                    if (WalkToGround(body, court.SideExit, speed, delta)) SetPhase(Phase.SlideReturnSide);
                    break;
                case Phase.SlideReturnSide:
                    if (WalkToGround(body, court.SideEntry, speed, delta)) SetPhase(Phase.CourtLeave);
                    break;
                case Phase.NearTurn:
                    if (WalkToGround(body, court.NearTurn, speed, delta)) SetPhase(Phase.CourtLeave);
                    break;
                case Phase.CourtLeave:
                    if (WalkToGround(body, courtJoin, speed, delta))
                        BeginCourtReturn(encounter);
                    break;
            }
            return true;
        }

        private void BeginCourtReturn(int encounter)
        {
            court.Release(encounter);
            sideReached = false;
            SetPhase(Phase.Return);
        }

        private bool ApproachCrossing(CityPedestrianPresentation body, CityCourtyardPlayground.Crossing crossing,
            float speed, float delta)
        {
            if (!WalkToGround(body, crossing.Entry.position, speed, delta) || !TurnTo(body, crossing.Facing, delta))
                return false;
            SampleCrossing(body, crossing, 0f);
            return true;
        }

        private bool Cross(CityPedestrianPresentation body, CityCourtyardPlayground.Crossing crossing)
        {
            float duration = CityFairChildActions.Duration(crossing.Action);
            SampleCrossing(body, crossing, elapsed / duration);
            if (elapsed < duration) return false;
            body.ClearAuthoredAction();
            return true;
        }

        private static void SampleCrossing(CityPedestrianPresentation body,
            CityCourtyardPlayground.Crossing crossing, float normalized)
        {
            SamplePose(body, crossing.Action, normalized, crossing.Entry.position, crossing.Facing, true);
        }

        private bool WalkToGround(CityPedestrianPresentation body, Vector3 target, float speed, float delta)
        {
            if (TryGround(target, out target)) return WalkTo(body, target, speed, delta);
            blocked += delta;
            body.Advance(delta, false);
            return false;
        }

        private void Sample(CityPedestrianPresentation body, CityFairChildAction action, float normalized)
        {
            // The hinge is authoritative in FixedUpdate. Its interpolated render
            // pose supplies the reference frame for the pelvis and both grips.
            SamplePose(body, action, normalized, Seat.RiderRoot, Seat.transform.rotation, false);
        }

        internal static void SamplePose(CityPedestrianPresentation body, CityFairChildAction action,
            float normalized, Vector3 basis, Quaternion facing, bool travelling)
        {
            AnimationClip clip = CityFairChildActions.GetClip(action);
            normalized = Mathf.Clamp01(normalized);
            // Place the sampled pelvis explicitly as the fair and bus seats do.
            // The Generic graph may extract displacement from the pelvis track.
            CityFairChildFrame pose = CityFairChildActions.Sample(action, normalized * clip.length);
            Vector3 root = travelling ? basis + facing * new Vector3(pose.Pelvis.x, 0f, pose.Pelvis.z) : basis;
            body.transform.SetPositionAndRotation(root, facing);
            body.ApplyAuthoredAction(clip, normalized, 1f, preserveAuthoredContacts: true);
            Vector3 pelvis = basis + facing * pose.Pelvis;
            body.Registry.ModelRoot.position += pelvis - body.Registry.PelvisAnchor.position;
        }

        private bool TurnToDock(CityPedestrianPresentation body, float delta) =>
            TurnTo(body, Seat.RestRotation, delta);

        private static bool TurnTo(CityPedestrianPresentation body, Quaternion rotation, float delta)
        {
            Transform actor = body.transform;
            actor.rotation = Quaternion.RotateTowards(actor.rotation, rotation, ApproachTurnRate * delta);
            body.Advance(delta, false);
            return Quaternion.Angle(actor.rotation, rotation) < .1f;
        }

        private bool WalkTo(CityPedestrianPresentation body, Vector3 target, float speed, float delta)
        {
            Transform actor = body.transform;
            Vector3 feet = actor.position;
            Vector3 flat = Vector3.ProjectOnPlane(target - feet, Vector3.up);
            if (flat.sqrMagnitude < .0001f)
            {
                // This is the exact saved route point on return.
                actor.position = target;
                return true;
            }
            Quaternion facing = Quaternion.LookRotation(flat);
            actor.rotation = Quaternion.RotateTowards(actor.rotation, facing, ApproachTurnRate * delta);
            Vector3 next = Vector3.MoveTowards(feet, target, speed * delta);
            if (Quaternion.Angle(actor.rotation, facing) > 30f)
            {
                body.Advance(delta, false);
                return false;
            }
            Vector3 probe = Vector3.MoveTowards(feet, target, .19f);
            bool supported = TryGround(next, out next);
            bool ahead = TryGround(probe, out probe);
            if (!supported && ahead && Mathf.Abs(probe.y - feet.y) < .08f)
            {
                // The flush coating is partitioned into adjacent triangles. A
                // probe on a seam can miss its face while both sides support
                // the same step. Interpolate only between those supports.
                float distance = Vector2.Distance(new Vector2(probe.x, probe.z), new Vector2(feet.x, feet.z));
                if (distance > .025f)
                {
                    float advance = Vector2.Distance(new Vector2(next.x, next.z), new Vector2(feet.x, feet.z));
                    next.y = Mathf.Lerp(feet.y, probe.y, advance / distance);
                    supported = true;
                }
            }
            if (!supported || !ClearPoint(next) || ahead && !ClearPoint(probe))
            {
                blocked += delta;
                body.Advance(delta, false);
                return false;
            }
            blocked = 0f;
            actor.position = next;
            body.AdvanceTravelled(delta, true, next - feet);
            return (next - target).sqrMagnitude < .0001f;
        }

        private void SetPhase(Phase phase)
        {
            CurrentPhase = phase;
            elapsed = blocked = 0f;
        }

        private static bool TryGround(Vector3 point, out Vector3 ground)
        {
            ground = point;
            if (!Physics.Raycast(point + Vector3.up * .26f, Vector3.down, out RaycastHit hit, .52f,
                    CityPedestrianCollision.NonPedestrianMask, QueryTriggerInteraction.Ignore) ||
                hit.normal.y < .82f) return false;
            ground.y = hit.point.y;
            return true;
        }

        private static bool ClearSegment(Vector3 from, Vector3 to, Transform frame = null, float spacing = .4f)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / spacing));
            for (int step = 0; step <= steps; step++)
            {
                if (!TryGround(Vector3.Lerp(from, to, (float)step / steps), out Vector3 feet) ||
                    !ClearPoint(feet)) return false;
                if (frame != null)
                {
                    Vector3 local = frame.InverseTransformPoint(feet);
                    if (Mathf.Abs(local.x) < 1.62f && Mathf.Abs(local.z) < CityPlaygroundGeometry.SeatReach + .35f)
                        return false;
                }
            }
            return true;
        }

        private static bool ClearOutsideArc(CityPlaygroundSwing seat, Vector3 from, Vector3 to)
            => ClearSegment(from, to, seat.transform.parent);

        internal static bool ClearPoint(Vector3 feet) => !Physics.CheckCapsule(
            feet + Vector3.up * .32f, feet + Vector3.up * 1.07f, BodyRadius,
            CityPedestrianCollision.NonPedestrianMask, QueryTriggerInteraction.Ignore);
    }
}
