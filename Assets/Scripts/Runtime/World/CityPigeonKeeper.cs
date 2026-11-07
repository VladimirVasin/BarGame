using System;
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
            var body = Worker.gameObject.AddComponent<CapsuleCollider>();
            body.center = Vector3.up * .87f;
            body.height = 1.7f;
            body.radius = .20f;
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
            Sample(seconds, WorkPhase(seconds), true);
        }

        private void LateUpdate()
        {
            if (Worker == null) return;
            double clock = AutoAdvance ? Calendar() : lastClock;
            if (GameTimeScaleRuntime.IsPaused && tapSound.isPlaying) tapSound.Stop();
            float distance = PresentationObserver == null ? 0 :
                (PresentationObserver.position - work).sqrMagnitude;
            bool show = distance < (present ? 115f * 115f : 105f * 105f);
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
            if (tapSound != null) tapSound.Stop();
            if (scoop != null) scoop.SetPositionAndRotation(scoopRest.position, scoopRest.rotation);
            if (water != null) water.SetPositionAndRotation(waterRest.position, waterRest.rotation);
            if (broom != null) broom.SetPositionAndRotation(broomRest.position, broomRest.rotation);
            if (waterTap != null) waterTap.rotation = tapRest;
            if (waterStream != null) waterStream.enabled = false;
            if (grain != null) grain.localScale = grainFullScale;
            if (Worker != null) Worker.ReleaseAnimation();
            IsFeeding = false;
        }

        private void OnDestroy()
        {
            if (Worker != null) NpcFootstepSources.Unregister(Worker.transform);
        }
    }
}
