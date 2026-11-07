using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The keeper works from the loft's landing. The same absolute clock
    /// drives his hands, the real utensils, and the flock's invitation to feed.</summary>
    [DefaultExecutionOrder(205)]
    [DisallowMultipleComponent]
    public sealed class CityPigeonKeeper : MonoBehaviour
    {
        public const float CycleSeconds = 192f;
        public Transform PresentationObserver { get; set; }
        public bool AutoAdvance { get; set; } = true;
        public VillageResidentPresentation Worker { get; private set; }
        public bool IsFeeding { get; private set; }
        public float HandContactError { get; private set; }
        public const float CommuteSpeed = 1.1f;
        public const float GateActionSeconds = 3.5f;
        public bool IsAtHome { get; private set; }
        public bool IsCommuting { get; private set; }
        public bool IsUsingGate { get; private set; }
        public float GateActionTime { get; private set; }
        public CityPigeonKeeperPlan CommutePlan { get; private set; }
        public double MorningDepartureSeconds => 360d / GameTimeState.GameMinutesPerRealSecond - CommuteSeconds;
        public double EveningDepartureSeconds => 1260d / GameTimeState.GameMinutesPerRealSecond;
        public double CommuteSeconds => workDuration + GateActionSeconds + homeDuration;
        public float GateApproachSeconds => workDuration;
        public float HomeWalkSeconds => homeDuration;

        private Transform feeder, water, scoop, broom, waterRight, waterLeft, broomLower, broomFoot;
        private Transform spine, grain, waterTap;
        private Renderer waterStream;
        private Vector3 work, right, forward, grainFullScale;
        private int grainHeightAxis;
        private Quaternion facing, tapRest;
        private Pose waterRest, scoopRest, broomRest;
        private NpcHandPose fingers;
        private AudioSource tapSound;
        private Renderer[] renderers;
        private bool[] visible;
        private bool present = true;
        private double lastPose = double.NegativeInfinity;
        private double lastClock = double.NaN;
        private CityPigeonCourtyardGate gate;
        private CapsuleCollider bodyCollider;
        private Camera viewCamera;
        private float[] workDistances, homeDistances;
        private float workDuration, homeDuration;
        private double commuteDelay, sampleDelta, nextHomeVisibility;
        private bool insideHidden, forceRoutine, gateActionActive, gateActionOpening, commuteToolsRestored;
        private float gateActionStart;
        private Vector3[] exitOpenApproach, exitOpenReturn;
        private float[] exitOpenApproachDistances, exitOpenReturnDistances;
        private float exitOpenApproachDuration, exitOpenReturnDuration, exitOpenElapsed, exitOpenStart;
        private bool exitOpening;
        private Transform walkObserver;
        private CharacterController observedBody;

        public void ConfigureCommute(CityPigeonKeeperPlan plan, CityPigeonCourtyardGate courtyardGate)
        {
            CommutePlan = plan ?? throw new ArgumentNullException(nameof(plan));
            gate = courtyardGate ?? throw new ArgumentNullException(nameof(courtyardGate));
            workDistances = Distances(plan.WorkToGate);
            homeDistances = Distances(plan.GateToHome);
            workDuration = workDistances[workDistances.Length - 1] / CommuteSpeed + .25f;
            homeDuration = homeDistances[homeDistances.Length - 1] / CommuteSpeed + .25f;
            Quaternion closedGate = gate.Pivot.rotation * Quaternion.Euler(0f, 90f * gate.Openness, 0f);
            Vector3 inside = gate.Pivot.position + closedGate * new Vector3(-.35f, 0f, .40f);
            inside.y = plan.WorkToGate[plan.WorkToGate.Count - 3].y;
            Vector3 behindTip = gate.Pivot.position + closedGate * new Vector3(-.35f, 0f, 1.85f);
            Vector3 beyondTip = gate.Pivot.position + closedGate * new Vector3(.90f, 0f, 1.85f);
            behindTip.y = beyondTip.y = inside.y;
            Vector3 arrival = plan.WorkToGate[plan.WorkToGate.Count - 3];
            exitOpenApproach = new[] { arrival, inside };
            exitOpenReturn = new[] { inside, behindTip, beyondTip, arrival };
            exitOpenApproachDistances = Distances(exitOpenApproach);
            exitOpenReturnDistances = Distances(exitOpenReturn);
            exitOpenApproachDuration = exitOpenApproachDistances[1] / CommuteSpeed + .25f;
            exitOpenReturnDuration = exitOpenReturnDistances[3] / CommuteSpeed + .25f;
            ApplyAt(Calendar());
        }

        public void Initialize(Transform site, Transform feeder, Transform water, Transform scoop, Transform workAnchor)
        {
            if (Worker != null) throw new InvalidOperationException("The pigeon keeper is already placed.");
            this.feeder = feeder;
            this.water = water;
            this.scoop = scoop;
            work = workAnchor.position;
            facing = Quaternion.LookRotation(Vector3.ProjectOnPlane(site.forward, Vector3.up), Vector3.up);
            right = facing * Vector3.right;
            forward = facing * Vector3.forward;
            broom = FindMovable(site, "Broom") ?? throw new InvalidOperationException("Pigeon loft is missing its broom.");
            waterRight = Require(water, "WaterGripRight");
            waterLeft = Require(water, "WaterGripLeft");
            broomLower = Require(broom, "BroomLowerGrip");
            broomFoot = Require(broom, "BroomFoot");
            waterRest = new Pose(water.position, water.rotation);
            scoopRest = new Pose(scoop.position, scoop.rotation);
            broomRest = new Pose(broom.position, broom.rotation);
            grain = FindMovable(feeder, "FeederGrain");
            if (grain != null)
            {
                grainFullScale = grain.localScale;
                Vector3 vertical = grain.InverseTransformDirection(Vector3.up);
                grainHeightAxis = Mathf.Abs(vertical.x) > Mathf.Abs(vertical.y) ? 0 : 1;
                if (Mathf.Abs(vertical.z) > Mathf.Abs(vertical[grainHeightAxis])) grainHeightAxis = 2;
            }
            waterTap = FindMovable(site, "WaterTap");
            if (waterTap != null) tapRest = waterTap.rotation;
            Transform stream = FindMovable(site, "WaterStream");
            if (stream != null) waterStream = stream.GetComponent<Renderer>();
            if (PresentationObserver == null && Camera.main is Camera camera)
                PresentationObserver = camera.transform;

            Worker = DefaultNpcFactory.CreateForCharacter(transform, DefaultNpcPopulation.CityPigeonKeeper);
            Worker.name = "Pigeon Keeper";
            Worker.transform.SetPositionAndRotation(work, facing);
            CityPortCrew.AlignWorkerModelWithPlacement(Worker);
            spine = Require(Worker.ModelRoot, "spine");
            fingers = Worker.GetComponent<NpcHandPose>() ??
                throw new InvalidOperationException("The pigeon keeper must use the ordinary hand rig.");
            bodyCollider = Worker.gameObject.AddComponent<CapsuleCollider>();
            bodyCollider.center = Vector3.up * .87f;
            bodyCollider.height = 1.7f;
            bodyCollider.radius = .20f;
            viewCamera = Camera.main;
            renderers = Worker.GetComponentsInChildren<Renderer>(true);
            visible = new bool[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) visible[i] = renderers[i].enabled;
            tapSound = feeder.gameObject.AddComponent<AudioSource>();
            tapSound.playOnAwake = false;
            tapSound.volume = .18f;
            CityWorkAudio.Configure(tapSound, false, 4500f, 11f, false);
            ApplyAt(Calendar());
        }

        private static Transform Require(Transform root, string name) =>
            CityPedestrianHandProps.FindSocket(root, name) ??
            throw new InvalidOperationException("Pigeon loft is missing " + name);

        private static Transform FindMovable(Transform root, string name) =>
            CityPedestrianHandProps.FindSocket(root, "MOVE_" + name) ??
            CityPedestrianHandProps.FindSocket(root, name);

        private static double Calendar() => (GameSessionState.GameDayIndex * 1440d +
            GameSessionState.GameTimeOfDayMinutes) / GameTimeState.GameMinutesPerRealSecond;

        private static float WorkPhase(double seconds)
        {
            double day = seconds % GameTimeState.RealSecondsPerGameDay;
            double morning = day - 360d / GameTimeState.GameMinutesPerRealSecond;
            double evening = day - 1020d / GameTimeState.GameMinutesPerRealSecond;
            // Each real visit has a continuous resting entry and exit. Between
            // the morning and evening chores the keeper watches the birds.
            if (morning >= 0d && morning < CycleSeconds) return (float)morning;
            if (evening >= 0d && evening < CycleSeconds) return (float)evening;
            return CycleSeconds;
        }

        public void ApplyAt(double seconds)
        {
            if (Worker == null || double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            lastClock = seconds;
            commuteDelay = sampleDelta = 0d;
            forceRoutine = true;
            insideHidden = false;
            exitOpening = false;
            nextHomeVisibility = seconds;
            Sample(seconds, WorkPhase(seconds), true);
            forceRoutine = false;
        }

        private void LateUpdate()
        {
            if (Worker == null) return;
            AdvanceAt(AutoAdvance ? Calendar() : lastClock);
        }

        /// <summary>Advance the ordinary calendar path, retaining blocked-gate waits.</summary>
        public void AdvanceAt(double clock)
        {
            if (Worker == null || double.IsNaN(clock) || double.IsInfinity(clock) || clock < 0d)
                throw new ArgumentOutOfRangeException(nameof(clock));
            if (walkObserver != PresentationObserver)
            {
                walkObserver = PresentationObserver;
                observedBody = walkObserver ? walkObserver.GetComponent<CharacterController>() : null;
            }
            if (GameTimeScaleRuntime.IsPaused)
            {
                if (tapSound.isPlaying) tapSound.Stop();
                return;
            }
            float distance = PresentationObserver == null ? 0 :
                (PresentationObserver.position - Worker.transform.position).sqrMagnitude;
            bool show = !insideHidden && distance < (present ? 115f * 115f : 105f * 105f);
            bool changed = show != present;
            if (changed)
            {
                present = show;
                for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = show && visible[i];
                if (!show) Worker.ReleaseAnimation();
            }
            if (clock == lastClock && !changed) return;
            float phase = WorkPhase(clock);
            bool close = distance < 16f * 16f;
            if (close && !changed && clock > lastClock && clock - lastClock < .5d)
            {
                float previous = WorkPhase(lastClock);
                if ((previous < 8.30f && phase >= 8.30f || previous < 9.18f && phase >= 9.18f) &&
                    phase < 10f && Application.isPlaying)
                {
                    if (tapSound.clip == null)
                        tapSound.clip = RetroAudioService.EnsureInstalled().GetClip(RetroSfxId.Clink);
                    tapSound.Play();
                }
            }
            sampleDelta = double.IsNaN(lastClock) ? 0d : Math.Max(0d, clock - lastClock);
            if (clock < lastClock || sampleDelta > .5d) { commuteDelay = 0d; exitOpening = false; }
            lastClock = clock;
            double interval = close ? 1d / 30d : 1d / 12d;
            Sample(clock, phase, show && (changed || clock < lastPose || clock - lastPose >= interval));
        }

        private static float Ease(float x, float a, float b) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

        private static float Window(float phase, float start, float arrive, float leave, float finish) =>
            Ease(phase, start, arrive) - Ease(phase, leave, finish);

        private static float Velocity(float phase, float start, float end)
        {
            float t = Mathf.InverseLerp(start, end, phase);
            return 6f * t * (1f - t) / (end - start);
        }

        private void Sample(double seconds, float phase, bool pose)
        {
            if (CommutePlan != null && SampleCommute(seconds, pose)) return;
            IsAtHome = IsCommuting = false;
            IsUsingGate = gateActionActive = false;
            insideHidden = false;
            commuteToolsRestored = false;
            if (bodyCollider != null) bodyCollider.enabled = true;
            if (forceRoutine && gate != null) gate.TrySetOpenness(1f);
            SampleWork(seconds, phase, pose);
        }

        private bool SampleCommute(double seconds, bool pose)
        {
            if (exitOpening)
            {
                commuteDelay += sampleDelta;
                exitOpenElapsed += (float)sampleDelta;
                SampleExitOpening(seconds, pose);
                return true;
            }
            bool wasAtHome = IsAtHome;
            double day = (seconds - commuteDelay) % GameTimeState.RealSecondsPerGameDay;
            double morning = MorningDepartureSeconds;
            double leaving = EveningDepartureSeconds;
            bool returning = day >= morning && day < morning + CommuteSeconds;
            bool leavingNow = day >= leaving && day < leaving + CommuteSeconds;
            IsAtHome = day < morning || day >= leaving + CommuteSeconds;
            IsCommuting = returning || leavingNow;
            IsUsingGate = false;
            if (!IsAtHome && !IsCommuting) { commuteDelay = 0d; return false; }
            if (!commuteToolsRestored) { RestoreTools(); commuteToolsRestored = true; }
            IsFeeding = false;

            if (IsAtHome)
            {
                gateActionActive = false;
                if (forceRoutine) gate.TrySetOpenness(0f);
                if (!wasAtHome || forceRoutine)
                {
                    Vector3 inward = -CommutePlan.House.Lot.FacadeForward;
                    Worker.transform.SetPositionAndRotation(CommutePlan.InsideHome, Quaternion.LookRotation(inward));
                }
                if (pose && !insideHidden)
                {
                    lastPose = seconds;
                    Worker.ApplyLocomotion(0f, false, (float)(seconds % 1200d));
                }
                bool wasHidden = insideHidden;
                if (!insideHidden && seconds >= nextHomeVisibility)
                {
                    nextHomeVisibility = seconds + .25d;
                    insideHidden = CanRetireInside();
                }
                if (wasHidden != insideHidden || forceRoutine) UpdateHomeVisibility();
                commuteDelay = 0d;
                return true;
            }

            double elapsed = day - (returning ? morning : leaving);
            // A retired model only returns while its actual vestibule is hidden.
            // If someone watches that opening, it waits at the same indoor feet.
            if (returning && insideHidden && !forceRoutine && !CanRetireInside())
            {
                commuteDelay += sampleDelta;
                Worker.transform.position = CommutePlan.InsideHome;
                UpdateHomeVisibility();
                return true;
            }
            insideHidden = false;
            bodyCollider.enabled = true;
            float beforeGate = returning ? homeDuration : workDuration;
            if (elapsed < beforeGate)
            {
                int arrivalIndex = CommutePlan.WorkToGate.Count - 3;
                float arrivalAt = workDistances[arrivalIndex] / CommuteSpeed + .125f;
                if (!returning && !forceRoutine && gate.Openness < .999f && elapsed >= arrivalAt)
                {
                    commuteDelay += elapsed - arrivalAt;
                    exitOpening = true;
                    exitOpenElapsed = 0f;
                    exitOpenStart = gate.Openness;
                    SampleExitOpening(seconds, pose);
                    return true;
                }
                gateActionActive = false;
                if (forceRoutine) gate.TrySetOpenness(returning ? 0f : 1f);
                Walk(returning ? CommutePlan.GateToHome : CommutePlan.WorkToGate,
                    returning ? homeDistances : workDistances, (float)elapsed, returning, seconds, pose);
            }
            else if (elapsed < beforeGate + GateActionSeconds)
            {
                GateActionTime = (float)(elapsed - beforeGate);
                IsUsingGate = true;
                if (!gateActionActive || gateActionOpening != returning || forceRoutine)
                {
                    gateActionActive = true;
                    gateActionOpening = returning;
                    gateActionStart = forceRoutine ? (returning ? 0f : 1f) : gate.Openness;
                }
                float fraction = Mathf.Lerp(gateActionStart, returning ? 1f : 0f,
                    Ease(GateActionTime, .8f, 2.6f));
                if (!gate.TrySetOpenness(fraction) && !forceRoutine) commuteDelay += sampleDelta;
                PoseAtGate(seconds, GateActionTime, returning, pose, CommutePlan.GateOutsideStand);
            }
            else
            {
                gateActionActive = false;
                if (forceRoutine) gate.TrySetOpenness(returning ? 1f : 0f);
                Walk(returning ? CommutePlan.WorkToGate : CommutePlan.GateToHome,
                    returning ? workDistances : homeDistances,
                    (float)(elapsed - beforeGate - GateActionSeconds), returning, seconds, pose);
            }
            return true;
        }

        private void SampleExitOpening(double seconds, bool pose)
        {
            IsAtHome = false;
            IsCommuting = true;
            IsUsingGate = false;
            if (exitOpenElapsed < exitOpenApproachDuration)
                Walk(exitOpenApproach, exitOpenApproachDistances, exitOpenElapsed, false, seconds, pose);
            else if (exitOpenElapsed < exitOpenApproachDuration + GateActionSeconds)
            {
                IsUsingGate = true;
                GateActionTime = exitOpenElapsed - exitOpenApproachDuration;
                float goal = Mathf.Lerp(exitOpenStart, 1f, Ease(GateActionTime, .8f, 2.6f));
                if (!gate.TrySetOpenness(goal)) exitOpenElapsed -= (float)sampleDelta;
                PoseAtGate(seconds, GateActionTime, true, pose, exitOpenApproach[1]);
            }
            else
            {
                float elapsed = exitOpenElapsed - exitOpenApproachDuration - GateActionSeconds;
                Walk(exitOpenReturn, exitOpenReturnDistances, elapsed, false, seconds, pose);
                if (elapsed >= exitOpenReturnDuration) exitOpening = false;
            }
        }

        private void PoseAtGate(double seconds, float elapsed, bool opening, bool pose, Vector3 stand)
        {
            Vector3 grip = gate.GripPosition;
            Vector3 aim = Vector3.ProjectOnPlane(grip - stand, Vector3.up);
            Worker.transform.SetPositionAndRotation(stand, Quaternion.LookRotation(aim));
            if (!pose) return;
            lastPose = seconds;
            Worker.ApplyDoor(opening ? VillageResidentAction.DoorOpen : VillageResidentAction.DoorClose, elapsed, grip);
            float weight = Window(elapsed, 0f, .7f, 2.8f, GateActionSeconds);
            Pose hand = fingers.GetSocketPose(false, grip, Vector3.up, -Worker.transform.forward);
            Quaternion upright = spine.rotation;
            for (float lean = 0f; ; lean += 4f)
            {
                spine.rotation = Quaternion.AngleAxis(lean * weight, Worker.transform.right) * upright;
                if (Worker.ApplyHandContacts(hand.position, null, hand.rotation, null, weight) || lean >= 36f || weight < .999f) break;
            }
            fingers.SetGrip(false, weight * .85f);
            HandContactError = weight >= .999f ? Vector3.Distance(Worker.RightGrip.position, hand.position) : 0f;
        }

        private void Walk(IReadOnlyList<Vector3> route, float[] distances, float elapsed,
            bool reverse, double seconds, bool pose)
        {
            const float ramp = .25f;
            float length = distances[distances.Length - 1];
            float duration = length / CommuteSpeed + ramp;
            float t = Mathf.Clamp(elapsed, 0f, duration);
            float along = t < ramp ? CommuteSpeed * t * t / (2f * ramp) :
                t > duration - ramp ? length - CommuteSpeed * (duration - t) * (duration - t) / (2f * ramp) :
                CommuteSpeed * (t - ramp * .5f);
            if (reverse) along = length - along;
            int segment = 0;
            while (segment + 2 < distances.Length && distances[segment + 1] < along) segment++;
            float fraction = Mathf.InverseLerp(distances[segment], distances[segment + 1], along);
            Vector3 position = Vector3.Lerp(route[segment], route[segment + 1], fraction);
            Vector3 direction = Vector3.ProjectOnPlane(route[segment + 1] - route[segment], Vector3.up).normalized;
            if (reverse) direction = -direction;
            if (!forceRoutine && HeroBlocksStep(position))
            {
                if (exitOpening) exitOpenElapsed -= (float)sampleDelta;
                else commuteDelay += sampleDelta;
                if (pose)
                {
                    lastPose = seconds;
                    Worker.ApplyLocomotion(0f, false, (float)(seconds % 1200d), PresentationObserver.position + Vector3.up * 1.2f);
                }
                return;
            }
            Worker.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
            if (!pose) return;
            lastPose = seconds;
            float speed = CommuteSpeed * Mathf.Clamp01(Mathf.Min(t, duration - t) / ramp);
            Worker.ApplyLocomotion(speed, false, (float)(seconds % 1200d), position + direction * 2f + Vector3.up * 1.2f);
            HandContactError = 0f;
        }

        private bool HeroBlocksStep(Vector3 next)
        {
            if (!observedBody || !observedBody.enabled) return false;
            Bounds hero = observedBody.bounds;
            if (next.y >= hero.max.y || next.y + bodyCollider.height <= hero.min.y) return false;
            Vector3 current = Worker.transform.position;
            Vector2 from = new(current.x - hero.center.x, current.z - hero.center.z);
            Vector2 step = new(next.x - current.x, next.z - current.z);
            if (step.sqrMagnitude < .000001f || Vector2.Dot(from, step) >= 0f) return false;
            float fraction = Mathf.Clamp01(-Vector2.Dot(from, step) / step.sqrMagnitude);
            float radius = bodyCollider.radius + Mathf.Max(hero.extents.x, hero.extents.z) + .04f;
            return (from + step * fraction).sqrMagnitude < radius * radius;
        }

        private static float[] Distances(IReadOnlyList<Vector3> route)
        {
            if (route == null || route.Count < 2) throw new ArgumentException("A keeper commute needs continuous endpoints.");
            var result = new float[route.Count];
            for (int i = 1; i < route.Count; i++) result[i] = result[i - 1] + Vector3.Distance(route[i - 1], route[i]);
            return result;
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

        private void UpdateHomeVisibility()
        {
            bodyCollider.enabled = !insideHidden;
            for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = !insideHidden && present && visible[i];
            if (insideHidden) Worker.ReleaseAnimation();
        }

        private void RestoreTools()
        {
            if (tapSound != null && tapSound.isPlaying) tapSound.Stop();
            if (scoop != null) scoop.SetPositionAndRotation(scoopRest.position, scoopRest.rotation);
            if (water != null) water.SetPositionAndRotation(waterRest.position, waterRest.rotation);
            if (broom != null) broom.SetPositionAndRotation(broomRest.position, broomRest.rotation);
            if (waterTap != null) waterTap.rotation = tapRest;
            if (waterStream != null) waterStream.enabled = false;
            if (grain != null) grain.localScale = grainFullScale;
        }

        private void SampleWork(double seconds, float phase, bool pose)
        {
            float feedVisit = Window(phase, 0, 3, 17, 20);
            float waterVisit = Window(phase, 55, 59, 84, 88);
            float sweepVisit = Window(phase, 99, 104, 130, 135);
            float waterX = Vector3.Dot(waterRest.position - work, right);
            float sweepX = Vector3.Dot(broomRest.position - work, right);
            float sideSpeed = waterX * (Velocity(phase, 55, 59) - Velocity(phase, 84, 88)) +
                sweepX * (Velocity(phase, 99, 104) - Velocity(phase, 130, 135));
            float forwardSpeed = .25f * (Velocity(phase, 0, 3) - Velocity(phase, 17, 20) +
                Velocity(phase, 55, 59) - Velocity(phase, 84, 88) +
                Velocity(phase, 99, 104) - Velocity(phase, 130, 135));
            float standOff = -.25f * (1f - feedVisit - waterVisit - sweepVisit);
            Vector3 body = work + forward * standOff + right * (waterX * waterVisit + sweepX * sweepVisit);
            Quaternion heading = Quaternion.AngleAxis(Mathf.Clamp(sideSpeed * 260f, -88f, 88f), Vector3.up) * facing;
            Worker.transform.SetPositionAndRotation(body, heading);
            IsFeeding = phase >= 20 && phase < 53;

            float scoopContact = Window(phase, 3, 5, 15, 17);
            float toFeeder = Window(phase, 5, 7.7f, 13, 15);
            float tilt = Window(phase, 10, 11.7f, 12.3f, 13);
            float tap = Window(phase, 8.04f, 8.30f, 8.31f, 8.60f) +
                Window(phase, 8.92f, 9.18f, 9.19f, 9.53f);
            Vector3 aboveRim = feeder.position - right * .11f - forward * .18f + Vector3.up * .22f;
            scoop.SetPositionAndRotation(Vector3.Lerp(scoopRest.position, aboveRim, toFeeder) +
                Vector3.up * (.10f * Mathf.Sin(Mathf.PI * toFeeder) - .055f * tap),
                Quaternion.AngleAxis(64f * tilt + 14f * tap, right) * scoopRest.rotation);
            if (grain != null)
            {
                float fill = Mathf.Lerp(.14f, 1f, Window(phase, 10, 12, 22, 52));
                Vector3 scale = grainFullScale;
                scale[grainHeightAxis] *= fill;
                grain.localScale = scale;
            }

            float waterContact = Window(phase, 60, 62, 73, 75);
            float withdrawal = Window(phase, 62, 63.4f, 71.6f, 73);
            float lift = Window(phase, 63.4f, 65, 70, 71.6f);
            float tip = Window(phase, 65, 66.7f, 68.2f, 70);
            // First draw the rim past the fixed spout, then lift the bowl.
            water.SetPositionAndRotation(waterRest.position + Vector3.up * (.26f * lift) - forward * (.21f * withdrawal),
                Quaternion.AngleAxis(-38f * tip, forward) * waterRest.rotation);
            float valveContact = Window(phase, 75, 76.5f, 82, 83.5f);
            if (waterTap != null) waterTap.rotation = Quaternion.AngleAxis(
                58f * Window(phase, 76.5f, 77.5f, 81, 82), forward) * tapRest;
            if (waterStream != null) waterStream.enabled = phase > 76.9f && phase < 81.7f;

            float sweepContact = Window(phase, 104, 106, 127, 129);
            float sweep = Window(phase, 106, 108, 125, 127);
            float stroke = Mathf.Sin((phase - 108f) * 2.2f) * sweep;
            Quaternion broomRotation = Quaternion.AngleAxis(9f * stroke - 12f * sweep, forward) *
                Quaternion.AngleAxis(-17f * sweep, right) * broomRest.rotation;
            // Keep the broom bristles on the landing throughout each stroke.
            broom.SetPositionAndRotation(broomRest.position + right * (.33f * stroke) - forward * (.12f * sweep),
                broomRotation);
            broom.position += Vector3.up * (work.y - broomFoot.position.y) * sweep;

            if (!pose) return;
            lastPose = seconds;
            Transform lookingAt = waterVisit > .4f ? water : sweepVisit > .4f ? broomFoot : feeder;
            Vector3 gaze = lookingAt.position;
            if (phase >= 20 && phase < 53) gaze += right * (.55f * Mathf.Sin((phase - 20) * .27f));
            Worker.ApplyLocomotion(Mathf.Sqrt(sideSpeed * sideSpeed + forwardSpeed * forwardSpeed),
                false, (float)(seconds % 1200d), gaze);
            Pose? leftHand = null, rightHand = null;
            float contact;
            HandContactError = 0f;
            if (scoopContact > 0)
            {
                contact = scoopContact;
                Quaternion roll = scoop.rotation * Quaternion.Inverse(scoopRest.rotation);
                leftHand = fingers.GetSocketPose(true, scoop.position, roll * forward, roll * Vector3.down);
            }
            else if (waterContact > 0)
            {
                contact = waterContact;
                Quaternion roll = water.rotation * Quaternion.Inverse(waterRest.rotation);
                rightHand = fingers.GetSocketPose(false, waterRight.position, roll * forward, roll * -right);
                leftHand = fingers.GetSocketPose(true, waterLeft.position, roll * forward, roll * right);
            }
            else if (valveContact > 0 && waterTap != null)
            {
                contact = valveContact;
                Quaternion turn = waterTap.rotation * Quaternion.Inverse(tapRest);
                rightHand = fingers.GetSocketPose(false, waterTap.position, turn * Vector3.up, forward);
            }
            else if (sweepContact > 0)
            {
                contact = sweepContact;
                Vector3 shaft = (broom.position - broomLower.position).normalized;
                rightHand = fingers.GetSocketPose(false, broom.position, shaft, forward - right * .4f);
                leftHand = fingers.GetSocketPose(true, broomLower.position, shaft, forward + right * .4f);
            }
            else return;
            Vector3 lowGrip = (leftHand ?? rightHand.Value).position;
            float lean = Mathf.Lerp(40f, 11f, Mathf.InverseLerp(.64f, 1.17f, lowGrip.y - body.y));
            Quaternion upright = spine.rotation;
            // Accommodate the worker's assigned body and boots without
            // stretching his arms or changing the position of a planted foot.
            for (float angle = lean; ; angle = Mathf.Min(60f, angle + 4f))
            {
                spine.rotation = Quaternion.AngleAxis(angle * contact, Worker.transform.right) * upright;
                bool reached = Worker.ApplyHandContacts(rightHand?.position, leftHand?.position,
                    rightHand?.rotation, leftHand?.rotation, contact);
                if (reached || angle >= 60f || contact < .999f) break;
            }
            if (contact >= .999f)
                HandContactError = Mathf.Max(
                    leftHand.HasValue ? Vector3.Distance(Worker.LeftGrip.position, leftHand.Value.position) : 0f,
                    rightHand.HasValue ? Vector3.Distance(Worker.RightGrip.position, rightHand.Value.position) : 0f);
            if (leftHand.HasValue) fingers.SetGrip(true, contact * .85f);
            if (rightHand.HasValue) fingers.SetGrip(false, contact * .85f);
        }

        private void OnEnable()
        {
            if (Worker != null) ApplyAt(Calendar());
        }

        private void OnDisable()
        {
            RestoreTools();
            if (Worker != null) Worker.ReleaseAnimation();
            IsFeeding = false;
        }

        private void OnDestroy()
        {
            if (Worker != null) NpcFootstepSources.Unregister(Worker.transform);
        }
    }
}
