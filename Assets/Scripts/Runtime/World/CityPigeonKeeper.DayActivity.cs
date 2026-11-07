using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityPigeonKeeper
    {
        private sealed class DayLeg
        {
            public CityPigeonKeeperDayStop Stop;
            public float[] Distances;
            public float Start, WalkSeconds, WorkSeconds, End;
        }

        public CityPigeonKeeperDayTask? CurrentDayTask { get; private set; }
        public bool IsWorkingInYard => dayActive && CurrentDayTask.HasValue &&
            CurrentDayTask != CityPigeonKeeperDayTask.DeckRail && CurrentDayTask != CityPigeonKeeperDayTask.ReturnToDeck;
        public float DayCycleSeconds { get; private set; }
        public float NominalDayCycleSeconds { get; private set; }
        public float CurrentDayWorkSeconds { get; private set; }
        public float DayPhase => dayElapsed;
        public double DayActivitySeconds { get; private set; }
        public float DayTaskTime { get; private set; } = -1f;
        public Transform DayBroomFoot => broomFoot;
        public Transform DayGrainContact => grainLidHandle;
        public Transform DayViceContact => viceGrip;

        private DayLeg[] dayLegs;
        private Transform viceHandle, viceGrip, grainLid, grainLidHandle, dayWorkbench;
        private Pose viceRest, lidRest;
        private Vector3[] broomApproach;
        private float[] broomApproachDistances;
        private float broomApproachSeconds, broomTransferSeconds;
        private float dayElapsed, dayStepDelta;
        private double lastDayPose = double.NegativeInfinity;
        private double scheduledUpperCoveredUntil = double.NegativeInfinity;
        private float previousDayScoopPhase = -1f;
        private bool dayActive, dayConfigured, samplingDay;
        private int groundDayStopCount;

        public void ConfigureDayProps(Transform viceHandle, Transform grainLid, Transform bath, Transform workbench)
        {
            if (CommutePlan == null || !viceHandle || !grainLid || !bath || !workbench)
                throw new ArgumentException("The keeper's work circuit requires its measured yard and physical props.");
            this.viceHandle = viceHandle;
            this.grainLid = grainLid;
            dayWorkbench = workbench;
            viceGrip = Require(viceHandle, "ViceGrip");
            grainLidHandle = Require(grainLid, "GrainLidHandle");
            viceRest = new Pose(viceHandle.position, viceHandle.rotation);
            lidRest = new Pose(grainLid.position, grainLid.rotation);
            float across = Vector3.Dot(broomRest.position - work, right);
            float depth = Vector3.Dot(broomRest.position - work, forward);
            Vector3 pickup = work + right * (across + .20f) + forward * (depth - .44f);
            broomApproach = new[] { CommutePlan.WorkToGate[0], pickup };
            broomApproachDistances = Distances(broomApproach);
            broomApproachSeconds = broomApproachDistances[1] / CommuteSpeed + .25f;
            broomTransferSeconds = broomApproachSeconds * 2f + 3.5f;
            dayLegs = new DayLeg[CommutePlan.DayStops.Count];
            groundDayStopCount = 0;
            for (int index = 0; index < dayLegs.Length; index++)
            {
                var stop = CommutePlan.DayStops[index];
                var distances = Distances(stop.Route);
                float walk = distances[distances.Length - 1] / CommuteSpeed + .25f;
                dayLegs[index] = new DayLeg { Stop = stop, Distances = distances,
                    WalkSeconds = walk };
                if (IsGroundDayTask(stop.Task)) groundDayStopCount++;
            }
            RebuildDayDurations(0f);
            NominalDayCycleSeconds = DayCycleSeconds;
            dayConfigured = true;
            ApplyAt(Calendar());
        }

        private static bool IsGroundDayTask(CityPigeonKeeperDayTask task) =>
            task != CityPigeonKeeperDayTask.DeckRail && task != CityPigeonKeeperDayTask.ReturnToDeck;

        private void RebuildDayDurations(float groundPadding)
        {
            float share = groundPadding / Mathf.Max(1, groundDayStopCount);
            float elapsed = broomTransferSeconds;
            foreach (DayLeg leg in dayLegs)
            {
                leg.WorkSeconds = leg.Stop.WorkSeconds + (IsGroundDayTask(leg.Stop.Task) ? share : 0f);
                leg.Start = elapsed;
                float drop = leg.Stop.Task == CityPigeonKeeperDayTask.ReturnToDeck ? broomTransferSeconds : 0f;
                leg.End = elapsed + leg.WalkSeconds + drop + leg.WorkSeconds;
                elapsed = leg.End;
            }
            DayCycleSeconds = elapsed;
        }

        private void RebuildNextDayCycle(double startSeconds)
        {
            float padding = 0f;
            if (GameSessionState.IsGameTimeRunning)
            {
                double day = startSeconds % GameTimeState.RealSecondsPerGameDay;
                double evening = 1020d / GameTimeState.GameMinutesPerRealSecond;
                double remaining = (day < evening ? evening : EveningDepartureSeconds) - day;
                // The last complete circuit fills its remaining window with
                // ordinary ground chores. Its route and deck visit stay short.
                if (remaining >= NominalDayCycleSeconds && remaining < NominalDayCycleSeconds * 2d)
                    padding = (float)(remaining - NominalDayCycleSeconds);
            }
            RebuildDayDurations(padding);
        }

        private void ResetDayActivity(double seconds)
        {
            CancelGrief();
            CurrentDayTask = null;
            DayTaskTime = -1f;
            CurrentDayWorkSeconds = 0f;
            dayActive = false;
            dayElapsed = dayStepDelta = 0f;
            scheduledUpperCoveredUntil = double.NegativeInfinity;
            previousDayScoopPhase = -1f;
            if (!dayConfigured) return;
            RebuildDayDurations(0f);
            RestoreDayProps();
            double day = seconds % GameTimeState.RealSecondsPerGameDay;
            double morning = 360d / GameTimeState.GameMinutesPerRealSecond + CycleSeconds;
            double evening = 1020d / GameTimeState.GameMinutesPerRealSecond;
            double start = day < evening ? morning : evening + CycleSeconds;
            if (day < start || day >= EveningDepartureSeconds || WorkPhase(seconds) < WorkRestPhase) return;
            double end = day < evening ? evening : EveningDepartureSeconds;
            double offset = day - start;
            double finalStart = start + Mathf.Max(0, (int)((end - start) / NominalDayCycleSeconds) - 1) * NominalDayCycleSeconds;
            float phase;
            if (GameSessionState.IsGameTimeRunning && day >= finalStart)
            {
                RebuildDayDurations((float)(end - finalStart - NominalDayCycleSeconds));
                phase = (float)(day - finalStart);
            }
            else phase = (float)(offset % NominalDayCycleSeconds);
            dayElapsed = phase;
            DayActivitySeconds = phase;
            dayActive = true;
            ResetGriefSchedule();
        }

        private bool AdvanceDayClock(double seconds, float delta)
        {
            dayStepDelta = delta;
            if (!dayConfigured || delta <= 0f) return false;
            // A circuit already performing the same complete deck chore at
            // the calendar visit owns that visit until it has finished. The
            // calendar never jumps a walking worker back onto the landing.
            if (dayActive && CurrentDayTask == CityPigeonKeeperDayTask.ReturnToDeck && DayTaskTime >= 0f &&
                WorkPhase(seconds) < WorkRestPhase)
            {
                double day = seconds % GameTimeState.RealSecondsPerGameDay;
                double visit = day < 1020d / GameTimeState.GameMinutesPerRealSecond ? 360d : 1020d;
                scheduledUpperCoveredUntil = seconds - day + visit / GameTimeState.GameMinutesPerRealSecond + CycleSeconds;
            }
            if (!dayActive)
            {
                if (!CanStartDayCycle(seconds)) return false;
                RebuildNextDayCycle(seconds - delta);
                dayActive = true;
                dayElapsed = 0f;
            }
            dayElapsed += delta;
            DayActivitySeconds += delta;
            if (AdvanceGriefClock(delta)) return true;
            if (dayElapsed < DayCycleSeconds) return true;
            if (CanStartDayCycle(seconds))
            {
                float remainder = dayElapsed - DayCycleSeconds;
                RebuildNextDayCycle(seconds - remainder);
                dayElapsed = remainder;
            }
            else
            {
                dayElapsed = 0f;
                dayActive = false;
                CurrentDayTask = null;
                CurrentDayWorkSeconds = 0f;
                RestoreDayProps();
                RestoreTools();
            }
            return true;
        }

        private bool CanStartDayCycle(double seconds)
        {
            double day = seconds % GameTimeState.RealSecondsPerGameDay;
            double evening = 1020d / GameTimeState.GameMinutesPerRealSecond;
            if (day < 360d / GameTimeState.GameMinutesPerRealSecond || day >= EveningDepartureSeconds ||
                EffectiveWorkPhase(seconds) < WorkRestPhase) return false;
            if (!GameSessionState.IsGameTimeRunning) return true;
            return (day < evening ? evening : EveningDepartureSeconds) - day >= NominalDayCycleSeconds;
        }

        private bool DayPoseDue(double interval) => dayConfigured && DayActivitySeconds - lastDayPose >= interval;
        private void HoldDayActivity() => dayElapsed = Mathf.Max(0f, dayElapsed - dayStepDelta);
        private float PresentationSeconds(double calendar) => (float)((samplingDay ? DayActivitySeconds : calendar) % 1200d);
        private float EffectiveWorkPhase(double seconds) => seconds < scheduledUpperCoveredUntil ? WorkRestPhase : WorkPhase(seconds);

        private bool DayActivityHoldsDeparture(double seconds)
        {
            if (!dayActive || forceRoutine) return false;
            double day = seconds % GameTimeState.RealSecondsPerGameDay;
            return day >= EveningDepartureSeconds || day < 360d / GameTimeState.GameMinutesPerRealSecond;
        }

        private bool SampleDayActivity(double seconds, bool pose)
        {
            if (!dayConfigured || !dayActive) { CurrentDayTask = null; CurrentDayWorkSeconds = 0f; return false; }
            IsAtHome = IsCommuting = IsUsingGate = false;
            if (pose) lastDayPose = DayActivitySeconds;
            samplingDay = true;
            try
            {
                if (dayElapsed < broomTransferSeconds)
                {
                    CurrentDayTask = null;
                    CurrentDayWorkSeconds = 0f;
                    DayTaskTime = -1f;
                    SampleBroomTransfer(dayElapsed, true, seconds, pose);
                    return true;
                }
                foreach (DayLeg leg in dayLegs)
                {
                    if (dayElapsed >= leg.End) continue;
                    CurrentDayTask = leg.Stop.Task;
                    CurrentDayWorkSeconds = leg.WorkSeconds;
                    float elapsed = dayElapsed - leg.Start;
                    DayTaskTime = elapsed - leg.WalkSeconds;
                    if (elapsed < leg.WalkSeconds)
                    {
                        Walk(leg.Stop.Route, leg.Distances, elapsed, false, seconds, pose);
                        CarryDayBroom();
                        if (pose) DayHands(null, 0f, 1f, 0f);
                    }
                    else if (leg.Stop.Task == CityPigeonKeeperDayTask.ReturnToDeck)
                    {
                        float workTime = elapsed - leg.WalkSeconds;
                        DayTaskTime = workTime - broomTransferSeconds;
                        if (workTime < broomTransferSeconds) SampleBroomTransfer(workTime, false, seconds, pose);
                        else
                        {
                            float workPhase = WorkPosePhase(workTime - broomTransferSeconds);
                            if (pose && PresentationObserver && (PresentationObserver.position - feeder.position).sqrMagnitude < 256f)
                                PlayScoopTap(previousDayScoopPhase, workPhase);
                            previousDayScoopPhase = workPhase;
                            SampleWork(seconds, workPhase, pose);
                        }
                    }
                    else SampleDayTask(leg.Stop, elapsed - leg.WalkSeconds, leg.WorkSeconds, seconds, pose);
                    return true;
                }
                return false;
            }
            finally { samplingDay = false; }
        }

        private void SampleBroomTransfer(float elapsed, bool pickup, double seconds, bool pose)
        {
            if (elapsed < broomApproachSeconds)
            {
                Walk(broomApproach, broomApproachDistances, elapsed, false, seconds, pose);
                if (pickup) broom.SetPositionAndRotation(broomRest.position, broomRest.rotation);
                else { CarryDayBroom(); if (pose) DayHands(null, 0f, 1f, 0f); }
                return;
            }
            if (elapsed >= broomApproachSeconds + 3.5f)
            {
                Walk(broomApproach, broomApproachDistances, elapsed - broomApproachSeconds - 3.5f, true, seconds, pose);
                if (pickup) { CarryDayBroom(); if (pose) DayHands(null, 0f, 1f, 0f); }
                else broom.SetPositionAndRotation(broomRest.position, broomRest.rotation);
                return;
            }
            float t = elapsed - broomApproachSeconds;
            Worker.transform.SetPositionAndRotation(broomApproach[1], facing);
            if (pose) Worker.ApplyLocomotion(0f, false, (float)(DayActivitySeconds % 1200d), broomRest.position);
            Pose carry = DayBroomCarryPose();
            float fraction = Ease(t, 1f, 2.5f);
            if (!pickup) fraction = 1f - fraction;
            broom.SetPositionAndRotation(Vector3.Lerp(broomRest.position, carry.position, fraction),
                Quaternion.Slerp(broomRest.rotation, carry.rotation, fraction));
            float contact = pickup ? Ease(t, 0f, .8f) : 1f - Ease(t, 2.7f, 3.5f);
            if (pose) DayHands(null, 0f, contact, 16f * contact * (1f - fraction));
        }

        private void SampleDayTask(CityPigeonKeeperDayStop stop, float elapsed, float workSeconds, double seconds, bool pose)
        {
            Worker.transform.SetPositionAndRotation(stop.Stand, stop.Facing);
            IsFeeding = false;
            if (pose) Worker.ApplyLocomotion(0f, false, (float)(DayActivitySeconds % 1200d), stop.RightContact);
            CarryDayBroom();
            float contact = Window(elapsed, 0f, 2f, workSeconds - 2f, workSeconds);
            Pose? rightHand = null;
            float leftWeight = 1f, lean = 16f * contact;
            switch (stop.Task)
            {
                case CityPigeonKeeperDayTask.GrainStore:
                    float lift = Window(elapsed, 3f, 5f, workSeconds - 6f, workSeconds - 3f);
                    grainLid.SetPositionAndRotation(lidRest.position + Vector3.up * (.16f * lift),
                        Quaternion.AngleAxis(10f * lift, stop.Facing * Vector3.right) * lidRest.rotation);
                    rightHand = fingers.GetSocketPose(false, grainLidHandle.position, stop.Facing * Vector3.right, Vector3.down);
                    break;
                case CityPigeonKeeperDayTask.Workbench:
                    float working = Window(elapsed, 2f, 4f, workSeconds - 4f, workSeconds - 2f);
                    viceHandle.rotation = Quaternion.AngleAxis(22f * Mathf.Sin(elapsed * 1.6f) * working,
                        dayWorkbench.forward) * viceRest.rotation;
                    rightHand = fingers.GetSocketPose(false, viceGrip.position, viceHandle.right, -(stop.Facing * Vector3.forward));
                    break;
                case CityPigeonKeeperDayTask.Bath:
                case CityPigeonKeeperDayTask.Sweep:
                    float brush = Window(elapsed, 3f, 5f, workSeconds - 5f, workSeconds - 3f);
                    Vector3 target = Vector3.Lerp(stop.RightContact, stop.LeftContact, .5f + .5f * Mathf.Sin(elapsed * 1.8f));
                    Quaternion brushRotation = Quaternion.AngleAxis(12f, Worker.transform.right) *
                        Worker.transform.rotation * Quaternion.Inverse(facing) * broomRest.rotation;
                    Pose carry = DayBroomCarryPose();
                    broom.SetPositionAndRotation(carry.position, Quaternion.Slerp(carry.rotation, brushRotation, brush));
                    broom.position += (target - broomFoot.position) * brush;
                    rightHand = fingers.GetSocketPose(false, broom.position, Vector3.up, Worker.transform.forward);
                    contact = brush;
                    lean = 38f * brush;
                    break;
                default:
                    rightHand = fingers.GetSocketPose(false, stop.RightContact, Vector3.up, Worker.transform.forward);
                    break;
            }
            if (pose) DayHands(rightHand, contact, leftWeight, lean,
                stop.Task == CityPigeonKeeperDayTask.Bath || stop.Task == CityPigeonKeeperDayTask.Sweep ? contact : 0f);
        }

        private Pose DayBroomCarryPose() => new Pose(Worker.transform.TransformPoint(new Vector3(-.24f, 1.10f, .12f)),
            Worker.transform.rotation * Quaternion.Inverse(facing) * broomRest.rotation);

        private void CarryDayBroom()
        {
            Pose carry = DayBroomCarryPose();
            broom.SetPositionAndRotation(carry.position, carry.rotation);
        }

        private void DayHands(Pose? rightHand, float rightWeight, float leftWeight, float lean, float lowerGrip = 0f)
        {
            Vector3 leftPoint = Vector3.Lerp(broom.position, broomLower.position, lowerGrip);
            Pose leftHand = fingers.GetSocketPose(true, leftPoint, Vector3.up, -Worker.transform.right);
            Quaternion upright = spine.rotation;
            bool leftReached = true, rightReached = true;
            for (float angle = lean; ; angle = Mathf.Min(60f, angle + 4f))
            {
                spine.rotation = Quaternion.AngleAxis(angle, Worker.transform.right) * upright;
                leftReached = Worker.ApplyHandContacts(null, leftHand.position, null, leftHand.rotation, leftWeight);
                rightReached = !rightHand.HasValue || rightWeight <= 0f ||
                    Worker.ApplyHandContacts(rightHand.Value.position, null, rightHand.Value.rotation, null, rightWeight);
                if (leftReached && rightReached || angle >= 60f || rightWeight < .999f) break;
            }
            fingers.SetGrip(true, leftWeight * .85f);
            fingers.SetGrip(false, rightWeight * .85f);
            HandContactError = Mathf.Max(leftWeight >= .999f ? Vector3.Distance(Worker.LeftGrip.position, leftHand.position) : 0f,
                rightHand.HasValue && rightWeight >= .999f ? Vector3.Distance(Worker.RightGrip.position, rightHand.Value.position) : 0f);
        }

        private void RestoreDayProps()
        {
            if (viceHandle) viceHandle.SetPositionAndRotation(viceRest.position, viceRest.rotation);
            if (grainLid) grainLid.SetPositionAndRotation(lidRest.position, lidRest.rotation);
        }
    }
}
