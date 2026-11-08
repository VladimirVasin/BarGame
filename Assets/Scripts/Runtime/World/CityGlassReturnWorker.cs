using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One real bottle passes between two hands. The deposit and the later inspection
    /// are opposite trips between the same two supports; neither stock nor props respawn.</summary>
    [DefaultExecutionOrder(215), DisallowMultipleComponent]
    public sealed class CityGlassReturnWorker : MonoBehaviour
    {
        private const float Cycle = 13f, ReturnAt = 7.2f, SetDown = 2.8f;
        public VillageResidentPresentation Worker { get; private set; }
        public Transform Bottle { get; private set; }
        public float HandContactError { get; private set; }
        public bool AutoAdvance { get; set; } = true;
        private Transform rest, slot, spine, observer;
        private NpcHandPose hands;
        private AudioSource clink;
        private double last = double.NaN, nextFarSample, checkObserver;
        private bool dormant;

        internal void Initialize(Transform pavilion)
        {
            Transform Part(string key) => CityGlassReturnAssetProvider.FindPart(pavilion, key);
            Transform feet = Part("ANCHOR_Worker");
            rest = Part("ANCHOR_BottleRest"); slot = Part("ANCHOR_CrateSlot");
            Bottle = Part("MOVE_Bottle");
            Worker = DefaultNpcFactory.CreateForCharacter(pavilion, DefaultNpcPopulation.CityGlassReturnWorker);
            Worker.name = "Glass Return Sorter";
            Worker.transform.SetPositionAndRotation(feet.position, pavilion.rotation);
            CityPortCrew.AlignWorkerModelWithPlacement(Worker);
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
            if (!Worker || !AutoAdvance) return;
            if (GameTimeScaleRuntime.IsPaused)
            {
                if (clink.isPlaying) clink.Stop();
                if (!dormant && !double.IsNaN(last)) PoseAt(last);
                return;
            }
            double now = Clock();
            if (!observer && Time.unscaledTimeAsDouble >= checkObserver)
            {
                if (Camera.main is Camera camera) observer = camera.transform;
                checkObserver = Time.unscaledTimeAsDouble + 1d;
            }
            float distanceSq = observer ? (observer.position - transform.position).sqrMagnitude : 0f;
            if (distanceSq > (dormant ? 60f * 60f : 70f * 70f))
            {
                if (!dormant) { dormant = true; Worker.gameObject.SetActive(false); clink.Stop(); }
                last = double.NaN; return;
            }
            if (dormant) { dormant = false; Worker.gameObject.SetActive(true); nextFarSample = 0; }
            if (distanceSq < 15f * 15f && now > last && now - last < .5 &&
                Math.Floor((now - SetDown) / Cycle) > Math.Floor((last - SetDown) / Cycle))
            {
                if (!clink.clip) clink.clip = RetroAudioService.EnsureInstalled().GetClip(RetroSfxId.Clink);
                clink.Play();
            }
            if (distanceSq <= 15f * 15f || double.IsNaN(last) || now < last || now >= nextFarSample)
            { PoseAt(now); nextFarSample = now + 1d / 10d; }
            last = now;
        }

        public void ApplyAt(double seconds)
        {
            if (seconds < 0d || double.IsNaN(seconds) || double.IsInfinity(seconds))
                throw new ArgumentOutOfRangeException(nameof(seconds));
            if (dormant) { dormant = false; Worker.gameObject.SetActive(true); }
            last = seconds; PoseAt(seconds);
        }

        private void PoseAt(double seconds)
        {
            float phase = (float)(seconds % Cycle);
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

        private void OnDisable() { if (clink) clink.Stop(); last = double.NaN; }
    }
}
