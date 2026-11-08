using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityGlassReturnWorker
    {
        private const float WalkSpeed = .82f, DoorSeconds = 3.5f;
        public CityGlassReturnWorkerPlan CommutePlan { get; private set; }
        public string HomeStableId => CommutePlan?.HomeStableId;
        public bool IsWorking { get; private set; }
        public bool IsCommuting { get; private set; }
        public bool IsAtHome { get; private set; }
        public bool IsUsingStaffDoor { get; private set; }
        public float StaffDoorOpenness { get; private set; }
        public float CommuteSeconds { get; private set; }
        private Transform staffDoor, doorPush;
        private Transform doorHand;
        private AnatomicalHandFrame doorPalm;
        private BoxCollider doorLeaf;
        private Quaternion doorClosed, leafRotation;
        private Vector3 leafCentre, leafHalfSize;
        private float[] switchDistances, staffDistances, crossDistances, homeDistances;
        private float switchWalkSeconds, staffWalkSeconds, crossWalkSeconds, homeWalkSeconds;
        private double commuteDelay, lastMotionSample = double.NaN, nextHomeVisibility;
        private bool insideHidden;
        private Collider[] doorBodies = new Collider[16];
        private static double StartSeconds => ShiftStartMinute / GameTimeState.GameMinutesPerRealSecond;
        private static double EndSeconds => ShiftEndMinute / GameTimeState.GameMinutesPerRealSecond;

        public void ConfigureCommute(CityGlassReturnWorkerPlan plan)
        {
            if (plan?.House == null || plan.WorkToDoor.Count < 2 || plan.DoorCrossing.Count < 2 || plan.DoorToHome.Count < 2)
                throw new ArgumentException("The glass worker requires its assigned house and physical staff-door route.");
            CommutePlan = plan;
            staffDoor = CityGlassReturnAssetProvider.FindPart(transform, "MOVE_ServiceDoor");
            doorPush = CityGlassReturnAssetProvider.FindPart(staffDoor, "ANCHOR_ServiceDoorPush");
            doorHand = CityPedestrianHandProps.FindSocket(Worker.ModelRoot, "hand.R");
            doorPalm = hands.GetAnatomicalFrame(false);
            doorLeaf = staffDoor.GetComponentInChildren<BoxCollider>();
            if (!doorLeaf) throw new InvalidOperationException("The glass worker's staff door has no solid moving leaf.");
            doorClosed = staffDoor.rotation;
            leafCentre = staffDoor.InverseTransformPoint(doorLeaf.transform.TransformPoint(doorLeaf.center));
            leafRotation = Quaternion.Inverse(staffDoor.rotation) * doorLeaf.transform.rotation;
            leafHalfSize = Vector3.Scale(doorLeaf.size, doorLeaf.transform.lossyScale) * .5f;
            switchDistances = Distances(plan.WorkToSwitch);
            staffDistances = Distances(plan.SwitchToDoor);
            crossDistances = Distances(plan.DoorCrossing);
            homeDistances = Distances(plan.DoorToHome);
            switchWalkSeconds = Duration(switchDistances);
            staffWalkSeconds = Duration(staffDistances);
            crossWalkSeconds = Duration(crossDistances);
            homeWalkSeconds = Duration(homeDistances);
            CommuteSeconds = switchWalkSeconds + SwitchActionSeconds + staffWalkSeconds +
                crossWalkSeconds + homeWalkSeconds + DoorSeconds * 2f;
            InitializeOperating();
            ApplyAt(Clock());
        }

        private void ResetCommute()
        {
            commuteDelay = 0d; lastMotionSample = double.NaN;
            insideHidden = false; nextHomeVisibility = 0d;
            SeekOperating();
        }

        private void Sample(double seconds, double delta, bool pose)
        {
            try
            {
                double day = seconds % GameTimeState.RealSecondsPerGameDay;
                double motionDelta = double.IsNaN(lastMotionSample) ? 0d : Math.Max(0d, seconds - lastMotionSample);
                if (motionDelta > .5d) motionDelta = delta;
                lastMotionSample = seconds;
                IsUsingStaffDoor = false;
                IsUsingSwitch = false;
                SwitchContactError = SwitchContactWeight = 0f;
                if (CommutePlan == null)
                {
                    IsWorking = day >= StartSeconds && day < EndSeconds;
                    SampleOperatingRest(IsWorking);
                    IsCommuting = false; IsAtHome = !IsWorking;
                    insideHidden = !IsWorking; UpdateVisibility();
                    if (IsWorking && pose) PoseAt(seconds);
                    return;
                }
                bool returning = day >= StartSeconds - CommuteSeconds && day < StartSeconds + commuteDelay;
                bool departing = day >= EndSeconds && day < EndSeconds + CommuteSeconds + commuteDelay;
                IsWorking = !returning && day >= StartSeconds && day < EndSeconds;
                IsCommuting = returning || departing;
                IsAtHome = !IsWorking && !IsCommuting;
                if (IsWorking)
                {
                    SampleOperatingRest(true);
                    commuteDelay = 0d;
                    insideHidden = false; UpdateVisibility();
                    Worker.transform.SetPositionAndRotation(workRest.position, workRest.rotation);
                    TryMoveDoor(0f);
                    if (pose) PoseAt(seconds);
                    return;
                }
                if (clink.isPlaying) clink.Stop();
                Bottle.SetPositionAndRotation(bottleRest.position, bottleRest.rotation);
                hands.SetGrip(true, 0f); hands.SetGrip(false, 0f);
                HandContactError = 0f;
                if (IsAtHome)
                {
                    SampleOperatingRest(false);
                    commuteDelay = 0d;
                    Worker.transform.SetPositionAndRotation(CommutePlan.InsideHome,
                        Quaternion.LookRotation(-CommutePlan.House.Lot.FacadeForward));
                    TryMoveDoor(0f);
                    if (!insideHidden && seconds >= nextHomeVisibility)
                    {
                        insideHidden = CanRetireInside();
                        nextHomeVisibility = seconds + .25d;
                    }
                    UpdateVisibility();
                    if (pose && !insideHidden)
                        Worker.ApplyLocomotion(0f, false, (float)(seconds % 1200d), CommutePlan.HomeDoor + Vector3.up);
                    return;
                }
                // Morning visibility resumes at exactly the same recessed position.
                // A retired actor cannot pop back into a directly watched doorway.
                if (returning && insideHidden && !forceSample && !CanRetireInside())
                {
                    commuteDelay += motionDelta;
                    return;
                }
                insideHidden = false; UpdateVisibility();
                float elapsed = (float)Math.Max(0d, day - (returning ? StartSeconds - CommuteSeconds : EndSeconds) - commuteDelay);
                SampleTrip(seconds, elapsed, returning, motionDelta, pose);
            }
            finally
            {
                if (pose && !dormant && !insideHidden) CapturePose();
                else if (insideHidden) heldPoseValid = false;
                operatingSeek = false;
            }
        }

        private void SampleTrip(double seconds, float elapsed, bool returning, double delta, bool pose)
        {
            SampleOperatingTimeline(seconds, elapsed, returning, delta);
            if (!returning)
            {
                if (elapsed < switchWalkSeconds)
                {
                    SetDoor(0f);
                    Walk(CommutePlan.WorkToSwitch, switchDistances, elapsed, false, seconds, delta, pose);
                    return;
                }
                if (elapsed < switchWalkSeconds + SwitchActionSeconds)
                {
                    UseSwitch(seconds, elapsed - switchWalkSeconds, false, delta, pose);
                    return;
                }
                elapsed -= switchWalkSeconds + SwitchActionSeconds;
            }
            float approach = returning ? homeWalkSeconds : staffWalkSeconds;
            float crossingEnd = approach + DoorSeconds + crossWalkSeconds;
            if (elapsed < approach)
            {
                SetDoor(0f);
                Walk(returning ? CommutePlan.DoorToHome : CommutePlan.SwitchToDoor,
                    returning ? homeDistances : staffDistances, elapsed, returning, seconds, delta, pose);
            }
            else if (elapsed < approach + DoorSeconds)
                UseDoor(seconds, elapsed - approach, true, returning, delta, pose);
            else if (elapsed < crossingEnd)
            {
                SetDoor(1f);
                Walk(CommutePlan.DoorCrossing, crossDistances, elapsed - approach - DoorSeconds,
                    returning, seconds, delta, pose);
            }
            else if (elapsed < crossingEnd + DoorSeconds)
                UseDoor(seconds, elapsed - crossingEnd, false, !returning, delta, pose);
            else
            {
                SetDoor(0f);
                float finish = elapsed - crossingEnd - DoorSeconds;
                if (!returning) Walk(CommutePlan.DoorToHome, homeDistances, finish, false, seconds, delta, pose);
                else if (finish < staffWalkSeconds)
                    Walk(CommutePlan.SwitchToDoor, staffDistances, finish, true, seconds, delta, pose);
                else if (finish < staffWalkSeconds + SwitchActionSeconds)
                    UseSwitch(seconds, finish - staffWalkSeconds, true, delta, pose);
                else Walk(CommutePlan.WorkToSwitch, switchDistances, finish - staffWalkSeconds - SwitchActionSeconds,
                    true, seconds, delta, pose);
            }
        }

        private void UseDoor(double seconds, float elapsed, bool opening, bool outside, double delta, bool pose)
        {
            IsUsingStaffDoor = true;
            float fraction = Ease(elapsed, .8f, 2.6f);
            if (!TryMoveDoor(opening ? fraction : 1f - fraction) && !forceSample) commuteDelay += delta;
            Vector3 stand = outside ? CommutePlan.DoorCrossing[CommutePlan.DoorCrossing.Count - 1] : CommutePlan.DoorCrossing[0];
            Vector3 direction = Vector3.ProjectOnPlane(doorPush.position - stand, Vector3.up);
            Worker.transform.SetPositionAndRotation(stand, Quaternion.LookRotation(direction));
            if (!pose) return;
            Worker.ApplyDoor(opening ? VillageResidentAction.DoorOpen : VillageResidentAction.DoorClose, elapsed, doorPush.position);
            float weight = Ease(elapsed, 0f, .7f) * (1f - Ease(elapsed, 2.8f, DoorSeconds));
            // Push the solid panel with an open palm, rather than curling the
            // fingers around an imaginary handle close to its hinge.
            Vector3 contact = doorPush.position - (outside ? 0f : .036f) * staffDoor.right;
            Quaternion palmRotation = doorPalm.ResolveHandRotation((outside ? -1f : 1f) * staffDoor.right, Vector3.up);
            Vector3 socketInHand = Quaternion.Inverse(doorHand.rotation) * (Worker.RightGrip.position - doorHand.position);
            Vector3 socket = contact + palmRotation * (socketInHand - doorPalm.PalmCentreInHand);
            Quaternion upright = spine.rotation;
            for (float angle = 0f; ; angle += 4f)
            {
                spine.rotation = Quaternion.AngleAxis(angle * weight, Worker.transform.right) * upright;
                if (Worker.ApplyHandContacts(socket, null, palmRotation, null, weight) || angle >= 36f || weight < .999f) break;
            }
            hands.SetGrip(false, 0f);
            Vector3 palm = doorHand.position + doorHand.rotation * doorPalm.PalmCentreInHand;
            HandContactError = weight > .999f ? Vector3.Distance(palm, contact) : 0f;
        }

        private void Walk(IReadOnlyList<Vector3> route, float[] distances, float elapsed, bool reverse,
            double seconds, double delta, bool pose)
        {
            const float ramp = .25f;
            float length = distances[distances.Length - 1], duration = Duration(distances);
            float t = Mathf.Clamp(elapsed, 0f, duration);
            float along = t < ramp ? WalkSpeed * t * t / (2f * ramp) :
                t > duration - ramp ? length - WalkSpeed * (duration - t) * (duration - t) / (2f * ramp) :
                WalkSpeed * (t - ramp * .5f);
            if (reverse) along = length - along;
            int segment = 0;
            while (segment + 2 < distances.Length && distances[segment + 1] < along) segment++;
            Vector3 position = Vector3.Lerp(route[segment], route[segment + 1],
                Mathf.InverseLerp(distances[segment], distances[segment + 1], along));
            Vector3 direction = Vector3.ProjectOnPlane(route[segment + 1] - route[segment], Vector3.up).normalized;
            if (reverse) direction = -direction;
            if (!forceSample && HeroBlocks(position))
            {
                commuteDelay += delta;
                if (pose) Worker.ApplyLocomotion(0f, false, (float)(seconds % 1200d), observer.position + Vector3.up);
                return;
            }
            Worker.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
            if (pose) Worker.ApplyLocomotion(WalkSpeed * Mathf.Clamp01(Mathf.Min(t, duration - t) / ramp), false,
                (float)(seconds % 1200d), position + direction * 2f + Vector3.up);
        }

        private bool HeroBlocks(Vector3 next)
        {
            if (!heroBody || !heroBody.enabled) return false;
            Bounds hero = heroBody.bounds;
            if (next.y >= hero.max.y || next.y + workerBody.height <= hero.min.y) return false;
            Vector3 current = Worker.transform.position;
            Vector2 from = new(current.x - hero.center.x, current.z - hero.center.z);
            Vector2 step = new(next.x - current.x, next.z - current.z);
            if (step.sqrMagnitude < .000001f || Vector2.Dot(from, step) >= 0f) return false;
            float fraction = Mathf.Clamp01(-Vector2.Dot(from, step) / step.sqrMagnitude);
            float radius = workerBody.radius + Mathf.Max(hero.extents.x, hero.extents.z) + .04f;
            return (from + step * fraction).sqrMagnitude < radius * radius;
        }

        private void SetDoor(float fraction)
        {
            StaffDoorOpenness = fraction;
            if (staffDoor) staffDoor.rotation = Quaternion.AngleAxis(-90f * fraction, Vector3.up) * doorClosed;
        }

        private bool TryMoveDoor(float fraction)
        {
            if (!forceSample)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(fraction - StaffDoorOpenness) * 12f));
                for (int step = 1; step <= steps; step++)
                {
                    float at = Mathf.Lerp(StaffDoorOpenness, fraction, step / (float)steps);
                    Quaternion rotation = Quaternion.AngleAxis(-90f * at, Vector3.up) * doorClosed;
                    Vector3 centre = staffDoor.position + rotation * leafCentre;
                    int count;
                    while ((count = Physics.OverlapBoxNonAlloc(centre, leafHalfSize, doorBodies, rotation * leafRotation,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) == doorBodies.Length)
                        Array.Resize(ref doorBodies, doorBodies.Length * 2);
                    for (int index = 0; index < count; index++)
                    {
                        Collider body = doorBodies[index];
                        if ((body is CharacterController || body is CapsuleCollider) && !body.transform.IsChildOf(Worker.transform))
                            return false;
                    }
                }
            }
            SetDoor(fraction);
            return true;
        }

        private bool CanRetireInside()
        {
            if (!viewCamera) viewCamera = Camera.main;
            if (!viewCamera) return false;
            return Hidden(Worker.Head.position) && Hidden(Worker.transform.position + Vector3.up * .85f) &&
                Hidden(Worker.transform.position + Vector3.up * .10f);
            bool Hidden(Vector3 point)
            {
                Vector3 view = viewCamera.WorldToViewportPoint(point);
                if (view.z < 0f || view.x < -.1f || view.x > 1.1f || view.y < -.1f || view.y > 1.1f) return true;
                Vector3 ray = point - viewCamera.transform.position;
                if (!Physics.Raycast(viewCamera.transform.position, ray.normalized, out RaycastHit hit,
                    ray.magnitude - .08f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
                return !hit.transform.IsChildOf(Worker.transform) &&
                    CommutePlan.House.FoundationBounds.Contains(new Vector2(hit.point.x, hit.point.z));
            }
        }

        private void UpdateVisibility()
        {
            if (workerRenderers == null) return;
            for (int i = 0; i < workerRenderers.Length; i++) workerRenderers[i].enabled = !insideHidden && !dormant && rendererVisible[i];
            workerBody.enabled = !insideHidden;
            if (insideHidden) { Worker.ReleaseAnimation(); heldPoseValid = false; }
        }

        private static float Duration(float[] distances) => distances[distances.Length - 1] / WalkSpeed + .25f;
        private static float[] Distances(IReadOnlyList<Vector3> route)
        {
            var values = new float[route.Count];
            for (int i = 1; i < route.Count; i++) values[i] = values[i - 1] + Vector3.Distance(route[i - 1], route[i]);
            return values;
        }
    }
}
