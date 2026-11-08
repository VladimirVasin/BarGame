using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One real bottle passes between two hands. The deposit and the later inspection
    /// are opposite trips between the same two supports; neither stock nor props respawn.</summary>
    [DefaultExecutionOrder(215), DisallowMultipleComponent]
    public sealed partial class CityGlassReturnWorker : MonoBehaviour
    {
        public const float SortingCycleSeconds = 13f;
        public const int ShiftStartMinute = 480, ShiftEndMinute = 1380;
        private const float ReturnAt = 7.2f, SetDown = 2.8f;
        public VillageResidentPresentation Worker { get; private set; }
        public Transform Bottle { get; private set; }
        public float HandContactError { get; private set; }
        public bool AutoAdvance { get; set; } = true;
        public Transform PresentationObserver
        {
            get => observer;
            set { observer = value; heroBody = value ? value.GetComponent<CharacterController>() : null; }
        }
        private Transform rest, slot, spine, observer;
        private NpcHandPose hands;
        private AudioSource clink;
        private double last = double.NaN, nextFarSample, checkObserver;
        private bool dormant, forceSample, heldPoseValid;
        private Pose workRest, bottleRest;
        private Renderer[] workerRenderers;
        private bool[] rendererVisible;
        private Transform[] poseBones;
        private Vector3[] heldPositions;
        private Quaternion[] heldRotations;
        private float heldLeftGrip, heldRightGrip;
        private CharacterController heroBody;
        private CapsuleCollider workerBody;
        private Camera viewCamera;

        internal void Initialize(Transform pavilion)
        {
            Transform Part(string key) => CityGlassReturnAssetProvider.FindPart(pavilion, key);
            Transform feet = Part("ANCHOR_Worker");
            rest = Part("ANCHOR_BottleRest"); slot = Part("ANCHOR_CrateSlot");
            Bottle = Part("MOVE_Bottle");
            bottleRest = new Pose(Bottle.position, Bottle.rotation);
            Worker = DefaultNpcFactory.CreateForCharacter(pavilion, DefaultNpcPopulation.CityGlassReturnWorker);
            Worker.name = "Glass Return Sorter";
            Worker.transform.SetPositionAndRotation(feet.position, pavilion.rotation);
            CityPortCrew.AlignWorkerModelWithPlacement(Worker);
            workRest = new Pose(Worker.transform.position, Worker.transform.rotation);
            workerBody = Worker.gameObject.AddComponent<CapsuleCollider>();
            workerBody.center = Vector3.up * .87f; workerBody.height = 1.7f; workerBody.radius = .20f;
            workerRenderers = Worker.GetComponentsInChildren<Renderer>(true);
            rendererVisible = new bool[workerRenderers.Length];
            for (int i = 0; i < workerRenderers.Length; i++) rendererVisible[i] = workerRenderers[i].enabled;
            var bones = new HashSet<Transform>();
            foreach (var skin in Worker.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (var bone in skin.bones)
                    for (var joint = bone; joint && joint != Worker.transform && joint != Worker.ModelRoot; joint = joint.parent)
                        bones.Add(joint);
            poseBones = new Transform[bones.Count]; bones.CopyTo(poseBones);
            heldPositions = new Vector3[poseBones.Length]; heldRotations = new Quaternion[poseBones.Length];
            spine = CityPedestrianHandProps.FindSocket(Worker.ModelRoot, "spine");
            hands = Worker.GetComponent<NpcHandPose>();
            clink = Part("ANCHOR_Clink").gameObject.AddComponent<AudioSource>();
            clink.playOnAwake = false; clink.volume = .39f;
            CityWorkAudio.Configure(clink, false, 6500f, 15f, false);
            ApplyAt(Clock());
        }

        private static double Clock() => (GameSessionState.GameDayIndex * 1440d +
            GameSessionState.GameTimeOfDayMinutes) / GameTimeState.GameMinutesPerRealSecond;
        private static float Ease(float time, float a, float b) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, time));

        private void LateUpdate()
        {
            if (!Worker) return;
            if (AutoAdvance) AdvanceAt(Clock());
            ReapplyPose();
        }

        public void AdvanceAt(double seconds)
        {
            if (!Worker || seconds < 0d || double.IsNaN(seconds) || double.IsInfinity(seconds))
                throw new ArgumentOutOfRangeException(nameof(seconds));
            if (GameTimeScaleRuntime.IsPaused)
            {
                if (clink.isPlaying) clink.Stop();
                return;
            }
            if (!observer && Time.unscaledTimeAsDouble >= checkObserver)
            {
                if (Camera.main is Camera camera)
                {
                    viewCamera = camera; observer = camera.transform;
                    heroBody = camera.GetComponentInParent<CharacterController>();
                }
                checkObserver = Time.unscaledTimeAsDouble + 1d;
            }
            float distanceSq = observer ? (observer.position - Worker.transform.position).sqrMagnitude : 0f;
            bool hide = distanceSq > (dormant ? 60f * 60f : 70f * 70f);
            bool changed = hide != dormant;
            if (changed)
            {
                dormant = hide;
                UpdateVisibility();
                if (hide) { Worker.ReleaseAnimation(); heldPoseValid = false; clink.Stop(); }
            }
            double delta = double.IsNaN(last) ? 0d : Math.Max(0d, seconds - last);
            if (seconds < last || delta > .5d) ResetCommute();
            if (!dormant && IsWorking && distanceSq < 15f * 15f && seconds > last && delta < .5d &&
                Math.Floor((SortingElapsed(seconds) - SetDown) / SortingCycleSeconds) >
                Math.Floor((SortingElapsed(last) - SetDown) / SortingCycleSeconds))
            {
                if (!clink.clip) clink.clip = RetroAudioService.EnsureInstalled().GetClip(RetroSfxId.Clink);
                clink.Play();
            }
            // Placement and collision retain calendar authority even while the rig is culled.
            if (changed || double.IsNaN(last) || seconds < last || seconds >= nextFarSample)
            {
                Sample(seconds, Math.Min(delta, .5d), !dormant);
                nextFarSample = seconds + (distanceSq < 15f * 15f ? 1d / 30d : 1d / 10d);
            }
            last = seconds;
        }

        public void ApplyAt(double seconds)
        {
            if (seconds < 0d || double.IsNaN(seconds) || double.IsInfinity(seconds))
                throw new ArgumentOutOfRangeException(nameof(seconds));
            dormant = false; forceSample = true;
            ResetCommute();
            nextFarSample = seconds;
            UpdateVisibility();
            last = seconds;
            Sample(seconds, 0d, true);
            forceSample = false;
        }

        private static double SortingElapsed(double seconds)
        {
            double elapsed = seconds % GameTimeState.RealSecondsPerGameDay - ShiftStartMinute / GameTimeState.GameMinutesPerRealSecond;
            double duration = (ShiftEndMinute - ShiftStartMinute) / GameTimeState.GameMinutesPerRealSecond;
            // Finish the last complete hand-to-hand trip before closing. The same
            // bottle rests on its real support during the remaining few seconds.
            return Math.Clamp(elapsed, 0d, Math.Floor(duration / SortingCycleSeconds) * SortingCycleSeconds);
        }

        private void PoseAt(double seconds)
        {
            float phase = (float)(SortingElapsed(seconds) % SortingCycleSeconds);
            bool back = phase >= ReturnAt;
            float t = back ? phase - ReturnAt : phase;
            Vector3 a = back ? slot.position : rest.position, b = back ? rest.position : slot.position;
            Vector3 middle = (a + b) * .5f + Vector3.up * .23f - transform.forward * .11f;
            float lift = Ease(t, .48f, 1.32f), place = Ease(t, 1.75f, SetDown);
            Bottle.position = Vector3.Lerp(Vector3.Lerp(a, middle, lift), b, place);
            Worker.Apply(VillageResidentAction.Idle, (float)(seconds % 1200d), Bottle.position + Vector3.up * .19f);
            float lead = Ease(t, 0f, .48f) * (1f - Ease(t, 1.54f, 1.75f));
            float follow = Ease(t, 1.32f, 1.54f) * (1f - Ease(t, SetDown + .04f, 3.25f));
            float left = back ? follow : lead, right = back ? lead : follow;
            HandContactError = 0f;
            if (Mathf.Max(left, right) < .001f) return;
            spine.rotation = Quaternion.AngleAxis(24f * Mathf.Max(left, right), transform.right) * spine.rotation;
            Grip(true, left, .19f);
            Grip(false, right, .12f);
        }

        private void Grip(bool isLeft, float weight, float height)
        {
            if (weight <= .001f) return;
            Vector3 grip = Bottle.position + Vector3.up * height;
            if (isLeft) Worker.ApplyHandContacts(null, grip, weight);
            else Worker.ApplyHandContacts(grip, null, weight);
            // The ordinary resident rig has the cylindrical hand shape; BottleGrip
            // belongs to the equipment hero and is not present on this worker.
            hands.SetGrip(isLeft, weight * .88f);
            if (weight > .999f)
                HandContactError = Mathf.Max(HandContactError,
                    Vector3.Distance((isLeft ? Worker.LeftGrip : Worker.RightGrip).position, grip));
        }

        private void CapturePose()
        {
            for (int i = 0; i < poseBones.Length; i++)
            { heldPositions[i] = poseBones[i].localPosition; heldRotations[i] = poseBones[i].localRotation; }
            heldLeftGrip = hands.LeftGripWeight; heldRightGrip = hands.RightGripWeight;
            heldPoseValid = true;
        }

        private void ReapplyPose()
        {
            if (!heldPoseValid || dormant || insideHidden || !Worker.IsInitialized) return;
            for (int i = 0; i < poseBones.Length; i++)
            { poseBones[i].localPosition = heldPositions[i]; poseBones[i].localRotation = heldRotations[i]; }
            hands.SetGrip(true, heldLeftGrip); hands.SetGrip(false, heldRightGrip);
        }

        private void OnDisable()
        {
            if (clink) clink.Stop();
            if (Bottle) Bottle.SetPositionAndRotation(bottleRest.position, bottleRest.rotation);
            if (Worker) Worker.ReleaseAnimation();
            heldPoseValid = false;
            IsUsingSwitch = false;
            SwitchContactError = SwitchContactWeight = 0f;
            SetRocker(WorkLightOn ? 1f : 0f);
            last = double.NaN;
        }

        private void OnEnable() { if (Worker) ApplyAt(Clock()); }
        private void OnDestroy() { if (Worker) NpcFootstepSources.Unregister(Worker.transform); }
    }
}
