using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One assigned resident walks between his home and a full shift at the workbench.</summary>
    [DefaultExecutionOrder(215), DisallowMultipleComponent]
    public sealed partial class CityCobblerWorker : MonoBehaviour
    {
        public const int ShiftStartMinute = 660, ShiftEndMinute = 1230;
        private const float WalkingSpeed = .86f, SitSeconds = 1.75f;
        public bool AutoAdvance { get; set; } = true;
        public bool IsWorking { get; private set; }
        public bool IsAtHome { get; private set; }
        public bool IsCommuting { get; private set; }
        public bool DoorShouldBeOpen { get; private set; }
        public string HomeStableId => CityCobblerWorkerPlan.ResidenceId;
        public float CommuteSeconds => CommutePlan.Commute.Length / WalkingSpeed + SitSeconds + LampSwitchSeconds;
        public CityCobblerWorkerPlan CommutePlan { get; private set; }
        public VillageResidentPresentation Worker { get; private set; }
        public float HandContactError { get; private set; }
        public float SoleContactError { get; private set; }
        public float WorkSeatWeight { get; private set; }
        public Transform PresentationObserver
        {
            get => viewer;
            set { viewer = value; hero = value ? value.GetComponentInParent<CharacterController>() : null; }
        }
        private CityCobblerSitePlan site;
        private Transform viewer;
        private Camera observer;
        private CharacterController hero;
        private CapsuleCollider body;
        private Renderer[] skins;
        private bool[] enabledSkins;
        private bool hidden, retiredAtHome;
        private double previousClock = double.NaN, motionClock, nextPose, nextCamera;
        private Vector3 basePosition;

        public void Configure(CityCobblerSitePlan plan, Transform shell)
        {
            if (Worker || plan == null || !shell) throw new InvalidOperationException("The cobbler requires one surveyed workshop.");
            site = plan;
            CommutePlan = new CityCobblerWorkerPlan(plan, shell);
            Worker = DefaultNpcFactory.CreateForCharacter(transform, DefaultNpcPopulation.CityCobbler);
            Worker.name = "Ridge cobbler";
            Worker.transform.SetPositionAndRotation(CommutePlan.HomeDoor, plan.Rotation);
            CityPortCrew.AlignWorkerModelWithPlacement(Worker);
            body = Worker.gameObject.AddComponent<CapsuleCollider>();
            body.radius = .2f; body.height = 1.7f; body.center = Vector3.up * .85f;
            skins = Worker.GetComponentsInChildren<Renderer>(true);
            enabledSkins = new bool[skins.Length];
            for (int i = 0; i < skins.Length; i++) enabledSkins[i] = skins[i].enabled;
            ConfigureRig(shell);
            ConfigureLampSwitch(shell);
            ApplyAt(Clock());
        }

        private static double Clock() => (GameSessionState.GameDayIndex * 1440d + GameSessionState.GameTimeOfDayMinutes)
            / GameTimeState.GameMinutesPerRealSecond;
        private static double AtMinute(int minute) => minute / GameTimeState.GameMinutesPerRealSecond;

        public void ApplyAt(double seconds)
        {
            if (!double.IsFinite(seconds) || seconds < 0d) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (!Worker) return;
            previousClock = motionClock = nextPose = seconds;
            Sample(seconds, false, true);
        }

        private void LateUpdate()
        {
            if (!Worker) return;
            if (!observer && Time.unscaledTimeAsDouble >= nextCamera)
            {
                observer = Camera.main;
                if (!viewer && observer) PresentationObserver = observer.transform;
                nextCamera = Time.unscaledTimeAsDouble + 1d;
            }
            if (AutoAdvance && !GameTimeScaleRuntime.IsPaused && !PauseMenuController.IsAnyPaused)
            {
                double now = Clock(), delta = now - previousClock;
                if (double.IsNaN(previousClock) || delta < 0d || delta > .75d) ApplyAt(now);
                else if (now >= nextPose)
                {
                    double target = Math.Min(now, motionClock + delta * 1.2d);
                    if (Sample(target, true, false)) motionClock = target;
                    float distance = viewer ? (viewer.position - basePosition).sqrMagnitude : 0f;
                    nextPose = now + (hidden ? .25d : distance < 18f * 18f ? 1d / 30d : 1d / 10d);
                    previousClock = now;
                }
            }
            RestorePose();
        }

        private bool Sample(double seconds, bool yieldToPlayer, bool seek)
        {
            double day = seconds % GameTimeState.RealSecondsPerGameDay;
            double opening = AtMinute(ShiftStartMinute), closing = AtMinute(ShiftEndMinute);
            bool morning = day >= opening - CommuteSeconds && day < opening;
            bool evening = day >= closing && day < closing + CommuteSeconds;
            bool home = !morning && !evening && (day < opening || day >= closing);
            bool doorOpen = !home && !morning && !evening;
            Vector3 stand = CommutePlan.HomeToWork[CommutePlan.HomeToWork.Count - 1];
            Vector3 position = stand, forward = site.Rotation * Vector3.forward;
            float seat = 0f, walking = 0f, grip = 0f;
            if (home)
            {
                position = CommutePlan.InsideHome;
                forward = -CommutePlan.House.Lot.FacadeForward;
            }
            else if (morning || evening)
            {
                var path = CommutePlan.Commute;
                float progress = morning ? (float)(day - opening + CommuteSeconds) : CommuteSeconds - (float)(day - closing);
                float along = Mathf.Clamp(progress * WalkingSpeed, 0f, path.Length);
                doorOpen = along >= CommutePlan.DoorApproachDistance;
                if (along < path.Length)
                {
                    position = path.Sample(along, out forward);
                    if (evening) forward = -forward;
                    walking = WalkingSpeed;
                }
                else
                {
                    float t = (progress - path.Length / WalkingSpeed) / SitSeconds;
                    forward = SeatTurn(morning, t);
                    seat = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.28f, 1f, t));
                }
            }
            else
            {
                seat = 1f;
                grip = Mathf.SmoothStep(0f, 1f, (float)Math.Min(day - opening, closing - day) / .9f);
            }
            if (yieldToPlayer && walking > 0f && HeroInPath(position))
                return false;
            if (yieldToPlayer && morning && retiredAtHome && observer && !CanRetire(CommutePlan.InsideHome))
                return false;

            IsCommuting = morning || evening; IsAtHome = home;
            IsWorking = !home && !IsCommuting;
            DoorShouldBeOpen = doorOpen;
            SampleLampSwitch(day, opening, closing);
            WorkSeatWeight = seat;
            basePosition = position;
            float sqrDistance = viewer ? (viewer.position - position).sqrMagnitude : 0f;
            bool culled = !seek && sqrDistance > (hidden ? 65f * 65f : 75f * 75f);
            retiredAtHome = home && (seek || retiredAtHome || CanRetire(position));
            SetHidden(culled || retiredAtHome);
            body.enabled = !retiredAtHome;
            // Keep the wrapper on its floor station. Move the skeleton onto the seat;
            // a seated pelvis must never drag the collision capsule down through the floor.
            Worker.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
            body.height = Mathf.Lerp(1.7f, 1.18f, seat);
            body.center = Vector3.up * (body.height * .5f);
            SampleTool(seconds, grip);
            if (!hidden)
            {
                if (walking > 0f) Worker.ApplyLocomotion(walking, false, (float)(seconds % 1200d));
                else Worker.Apply(VillageResidentAction.Idle, (float)(seconds % 1200d));
                ApplyRig(seat, grip);
                if (IsUsingLampSwitch) ApplyLampSwitchPose();
                CapturePose();
            }
            else HandContactError = SoleContactError = 0f;
            return true;
        }

        private Vector3 SeatTurn(bool arriving, float towardSeat)
        {
            Vector3 aisle = site.Rotation * (arriving ? Vector3.left : Vector3.right);
            return Vector3.Slerp(aisle, site.Rotation * Vector3.forward,
                Mathf.SmoothStep(0f, 1f, towardSeat / .32f));
        }

        private bool HeroInPath(Vector3 next)
        {
            if (!hero || !hero.enabled) return false;
            Bounds bounds = hero.bounds;
            if (next.y > bounds.max.y || next.y + 1.7f < bounds.min.y) return false;
            Vector3 radius = Vector3.ProjectOnPlane(basePosition - bounds.center, Vector3.up);
            Vector3 step = Vector3.ProjectOnPlane(next - basePosition, Vector3.up);
            if (Vector3.Dot(radius, step) >= 0f) return false;
            Vector3 nearest = radius + step * Mathf.Clamp01(-Vector3.Dot(radius, step) / Mathf.Max(.000001f, step.sqrMagnitude));
            float clearance = body.radius + Mathf.Max(bounds.extents.x, bounds.extents.z) + .025f;
            return nearest.sqrMagnitude < clearance * clearance;
        }

        private bool CanRetire(Vector3 feet)
        {
            if (!observer) return false;
            for (int i = 0; i < 3; i++)
            {
                Vector3 p = feet + Vector3.up * (.12f + .72f * i);
                Vector3 view = observer.WorldToViewportPoint(p);
                if (view.z <= 0f || view.x < -.1f || view.x > 1.1f || view.y < -.1f || view.y > 1.1f) continue;
                Vector3 ray = p - observer.transform.position;
                if (!Physics.Raycast(observer.transform.position, ray.normalized, out var hit, ray.magnitude - .1f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ||
                    hit.transform.IsChildOf(Worker.transform) ||
                    !CommutePlan.House.FoundationBounds.Contains(new Vector2(hit.point.x, hit.point.z))) return false;
            }
            return true;
        }

        private void SetHidden(bool value)
        {
            if (hidden == value) return;
            hidden = value;
            for (int i = 0; i < skins.Length; i++) skins[i].enabled = !value && enabledSkins[i];
            if (value) { held = false; Worker.ReleaseAnimation(); }
        }

        private void OnDisable()
        {
            held = false;
            IsUsingLampSwitch = false;
            LampSwitchWeight = LampSwitchContactError = 0f;
            SetLampButton(0f);
            if (Worker) Worker.ReleaseAnimation();
            if (awl) RestTool();
            previousClock = double.NaN;
        }
        private void OnEnable() { if (Worker) ApplyAt(Clock()); }
        private void OnDestroy() { if (Worker) NpcFootstepSources.Unregister(Worker.transform); }
    }
}
