using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One quiet shared-rig worker, two drums and a finite basket/cloth transaction.
    /// Absolute calendar phase survives distance and scene visits; presentation is bounded.</summary>
    [DefaultExecutionOrder(210)]
    public sealed class CityLaundryController : MonoBehaviour
    {
        public const float CycleSeconds = 48f;
        public CityLaundryPlan Plan { get; private set; }
        public Transform ModelRoot { get; private set; }
        public VillageResidentPresentation Worker { get; private set; }
        public Transform Basket { get; private set; }
        public Transform Cloth { get; private set; }
        public bool AutoAdvance { get; set; } = true;
        public bool ForcePresentation { get; set; }
        public Transform PresentationObserver { get; set; }
        public double ElapsedSeconds { get; private set; }
        public bool HandsMatch { get; private set; }
        public float HandContactError { get; private set; }
        public bool IsPresentationActive { get; private set; } = true;
        public IReadOnlyList<Collider> RainShelters { get; private set; } = Array.Empty<Collider>();
        private Transform spine, leftHandle, rightHandle, leftFold, rightFold, drumA, drumB, basketBody;
        private Vector3 work, waypoint, canopy, restBasket, handoff;
        private Renderer[] dynamicRenderers;
        private AudioSource hum;
        private AudioClip humClip;
        private bool initialized, audioPaused;

        public void Initialize(CityLaundryPlan plan, Transform model)
        {
            if (initialized) throw new InvalidOperationException("Laundry is already initialized.");
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            ModelRoot = model ?? throw new ArgumentNullException(nameof(model));
            Transform Part(string name) => CityLaundryAssetProvider.FindPart(model, name);
            Vector3 Local(string name) => plan.InverseWorld(Part(name).position);
            work = Local("ANCHOR_Worker"); waypoint = Local("ANCHOR_CarryWaypointA");
            canopy = Local("ANCHOR_CanopyWorker"); restBasket = Local("ANCHOR_BasketRest");
            handoff = Local("ANCHOR_CanopyHandoff");
            Basket = Part("MOVE_CarryBasket"); Cloth = Part("MOVE_ClothA");
            leftHandle = Part("ANCHOR_BasketLeft"); rightHandle = Part("ANCHOR_BasketRight");
            leftFold = Part("MOVE_ClothLeft"); rightFold = Part("MOVE_ClothRight");
            drumA = Part("MOVE_DrumA"); drumB = Part("MOVE_DrumB");
            Worker = DefaultNpcFactory.CreateForCharacter(transform, DefaultNpcPopulation.CityLaundryWorker);
            Worker.name = "Laundry Worker";
            Worker.transform.SetPositionAndRotation(plan.World(work), plan.Rotation * Quaternion.Euler(0f, 180f, 0f));
            CityPortCrew.AlignWorkerModelWithPlacement(Worker);
            spine = CityPedestrianHandProps.FindSocket(Worker.ModelRoot, "spine");
            var body = Worker.gameObject.AddComponent<CapsuleCollider>();
            body.center = Vector3.up * .9f; body.height = 1.72f; body.radius = .21f;
            basketBody = new GameObject("Laundry Carried Basket Body").transform;
            basketBody.SetParent(transform, false);
            var basketCollider = basketBody.gameObject.AddComponent<BoxCollider>();
            basketCollider.center = Vector3.up * .01f;
            basketCollider.size = new Vector3(.74f, .38f, .64f);
            var movingVisuals = new List<Renderer>(Worker.GetComponentsInChildren<Renderer>(true));
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                if (renderer.transform.IsChildOf(Basket) || renderer.transform.IsChildOf(Cloth) ||
                    renderer.transform.IsChildOf(drumA) || renderer.transform.IsChildOf(drumB)) movingVisuals.Add(renderer);
            dynamicRenderers = movingVisuals.ToArray();
            RainShelters = new[] { Part("COL_Canopy").GetComponent<Collider>() };
            // Real fixtures lease the existing nearest-first pool. Their
            // bounded shadow maps stop room light at the authored shell.
            void LightAnchor(string purpose, string anchor, Vector3 aim)
            {
                Transform light = new GameObject(CityNightAtmosphere.IndustrialTaskLightPrefix + purpose).transform;
                light.SetParent(transform, false); light.position = Part(anchor).position;
                light.rotation = plan.Rotation * Quaternion.LookRotation(aim);
            }
            LightAnchor("LaundryWork", "ANCHOR_WorkLight", new Vector3(0f, -1f, 0f));
            LightAnchor("LaundryMachines", "ANCHOR_MachinesLight", Vector3.down);
            LightAnchor("LaundryCourt", "ANCHOR_CourtLight", Vector3.down);
            LightAnchor("LaundryEntrance", "ANCHOR_EntranceLight", Vector3.down);
            Transform audio = new GameObject("Laundry Machine Hum").transform;
            audio.SetParent(transform, false); audio.position = Part("ANCHOR_MachineAudio").position;
            hum = audio.gameObject.AddComponent<AudioSource>();
            hum.playOnAwake = false; hum.loop = true; hum.volume = .075f;
            CityWorkAudio.Configure(hum, true, 1250f, 13f);
            float[] samples = StairwellSoundscapeSynthesis.GenerateVentilationLoopSamples();
            humClip = AudioClip.Create("Laundry machine ventilation", samples.Length, 1,
                StairwellSoundscapeSynthesis.SampleRate, false);
            humClip.SetData(samples, 0); hum.clip = humClip;
            initialized = true;
            ApplyAt(CalendarSeconds());
        }

        private static double CalendarSeconds() =>
            (GameSessionState.GameDayIndex * 1440d + GameSessionState.GameTimeOfDayMinutes) /
            GameTimeState.GameMinutesPerRealSecond;

        private void LateUpdate()
        {
            if (!initialized) return;
            bool paused = GameTimeScaleRuntime.IsPaused || !GameSessionState.IsGameTimeRunning;
            if (!paused && AutoAdvance) ElapsedSeconds = CalendarSeconds();
            bool present = ForcePresentation || PresentationObserver == null ||
                (PresentationObserver.position - Plan.Position).sqrMagnitude <=
                (IsPresentationActive ? 72f * 72f : 64f * 64f);
            bool changed = present != IsPresentationActive;
            IsPresentationActive = present;
            if (!paused || changed) Sample(ElapsedSeconds, present);
            if (changed) foreach (Renderer renderer in dynamicRenderers) renderer.enabled = present;
            bool audible = present && (PresentationObserver == null || ForcePresentation ||
                (PresentationObserver.position - hum.transform.position).sqrMagnitude < 18f * 18f);
            if (paused)
            {
                if (!audioPaused && hum.isPlaying) hum.Pause();
                audioPaused = true;
            }
            else if (audible)
            {
                if (audioPaused) hum.UnPause();
                if (!hum.isPlaying)
                {
                    hum.timeSamples = (int)((ElapsedSeconds * humClip.frequency) % humClip.samples);
                    hum.Play();
                }
                audioPaused = false;
            }
            else { hum.Stop(); audioPaused = false; }
        }

        public void ApplyAt(double seconds)
        {
            if (!initialized || double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            ElapsedSeconds = seconds;
            Sample(seconds, true);
        }

        private void Sample(double seconds, bool present)
        {
            float phase = (float)(seconds % CycleSeconds);
            Vector3 actor = work;
            float yaw = 180f;
            float speed = 0f;
            if (phase >= 12f && phase < 20f)
            {
                yaw = Mathf.LerpAngle(180f, 90f, Smooth((phase - 12f) / 2f));
                actor = Route(Smooth((phase - 14f) / 6f)); speed = RouteSpeed((phase - 14f) / 6f);
            }
            else if (phase >= 20f && phase < 32f) { actor = canopy; yaw = 90f; }
            else if (phase >= 32f && phase < 40f)
            {
                yaw = Mathf.LerpAngle(90f, 270f, Smooth((phase - 32f) / 2f));
                actor = Route(1f - Smooth((phase - 34f) / 6f)); speed = RouteSpeed((phase - 34f) / 6f);
            }
            else if (phase >= 40f && phase < 44f)
                yaw = Mathf.LerpAngle(270f, 180f, Smooth((phase - 40f) / 2f));
            Quaternion facing = Plan.Rotation * Quaternion.Euler(0f, yaw, 0f);
            Worker.transform.SetPositionAndRotation(Plan.World(actor), facing);
            Vector3 held = Worker.transform.position + facing * new Vector3(0f, 1.25f, .58f);
            Quaternion heldRotation = facing * Quaternion.Euler(0f, 180f, 0f);
            Vector3 basket = Plan.World(restBasket);
            Quaternion basketRotation = Plan.Rotation;
            bool carrying = phase >= 8f && phase < 24f || phase >= 28f && phase < 44f;
            if (phase >= 8f && phase < 12f)
            {
                float lift = Smooth((phase - 10f) / 2f);
                basket = Vector3.Lerp(basket, held, lift); basketRotation = heldRotation;
            }
            else if (phase >= 12f && phase < 20f || phase >= 32f && phase < 42f)
            { basket = held; basketRotation = heldRotation; }
            else if (phase >= 20f && phase < 24f)
            {
                basket = Vector3.Lerp(held, Plan.World(handoff), Smooth((phase - 20f) / 4f));
                basketRotation = heldRotation;
            }
            else if (phase >= 24f && phase < 28f)
            { basket = Plan.World(handoff); basketRotation = Plan.Rotation * Quaternion.Euler(0f, 270f, 0f); }
            else if (phase >= 28f && phase < 32f)
            {
                basket = Vector3.Lerp(Plan.World(handoff), held, Smooth((phase - 30f) / 2f));
                basketRotation = heldRotation;
            }
            else if (phase >= 42f && phase < 44f)
            {
                basket = Vector3.Lerp(held, basket, Smooth((phase - 42f) / 2f));
                basketRotation = heldRotation;
            }
            Basket.SetPositionAndRotation(basket, basketRotation);
            basketBody.SetPositionAndRotation(basket, basketRotation);
            Cloth.SetPositionAndRotation(basket + basketRotation * new Vector3(0f, .19f, 0f), basketRotation);
            if (!present) return;
            float fold = phase < 8f ? Mathf.Sin(phase * Mathf.PI / 8f) : 0f;
            float leftAngle = -135f * fold, rightAngle = 110f * fold;
            leftFold.localRotation = Quaternion.AngleAxis(leftAngle, Vector3.forward);
            rightFold.localRotation = Quaternion.AngleAxis(rightAngle, Vector3.forward);
            drumA.localRotation = Quaternion.AngleAxis((float)(seconds * 92d % 360d), Vector3.forward);
            drumB.localRotation = Quaternion.AngleAxis((float)(-seconds * 75d % 360d), Vector3.forward);
            // Keep the authored carrying posture through release and regrip;
            // the hands ease independently while the basket rests on the cart.
            // Switching the torso to Idle at the contact would break reach.
            bool carryPose = phase >= 8f && phase < 44f;
            Worker.ApplyLocomotion(speed, carryPose, (float)(seconds % 3600d), Cloth.position);
            float lean = carrying ? Mathf.Lerp(38f, 12f, Mathf.InverseLerp(handoff.y, 1.25f, Plan.InverseWorld(basket).y)) : 12f;
            float contactWeight = 1f;
            if (phase >= 24f && phase < 28f)
            {
                contactWeight = 1f - Smooth((phase - 24f) / 2f);
                lean = Mathf.Lerp(12f, 38f, contactWeight);
            }
            else if (phase >= 28f && phase < 30f)
            {
                contactWeight = Smooth((phase - 28f) / 2f);
                lean = Mathf.Lerp(12f, 38f, contactWeight);
            }
            Vector3 right = leftHandle.position, left = rightHandle.position;
            if (!carrying && (phase < 8f || phase >= 44f))
            {
                right = Cloth.position + Cloth.rotation * (new Vector3(-.1f, 0f, 0f) +
                    Quaternion.AngleAxis(leftAngle, Vector3.forward) * new Vector3(-.18f, .02f, 0f));
                left = Cloth.position + Cloth.rotation * (new Vector3(.1f, 0f, 0f) +
                    Quaternion.AngleAxis(rightAngle, Vector3.forward) * new Vector3(.18f, .02f, 0f));
            }
            bool touching = carrying || phase < 8f || phase >= 44f || phase < 26f;
            Quaternion upright = spine.rotation;
            HandsMatch = true; HandContactError = 0f;
            if (!touching || contactWeight < .001f)
            { spine.rotation = Quaternion.AngleAxis(lean, Worker.transform.right) * upright; return; }
            // Fit this fixed body to measured work surfaces. A bounded spine
            // lean keeps planted feet and both grips; it never stretches arms
            // or moves the basket to conceal an unreachable contact.
            for (int attempt = 0; attempt < 12; attempt++)
            {
                float angle = Mathf.Min(56f, lean + attempt * 4f);
                spine.rotation = Quaternion.AngleAxis(angle, Worker.transform.right) * upright;
                HandsMatch = Worker.ApplyHandContacts(right, left, contactWeight);
                HandContactError = contactWeight > .999f ? Mathf.Max(Vector3.Distance(Worker.RightGrip.position, right),
                    Vector3.Distance(Worker.LeftGrip.position, left)) : 0f;
                if (HandsMatch || angle >= 56f || contactWeight < .999f) break;
            }
        }

        private Vector3 Route(float progress)
        {
            float first = Vector3.Distance(work, waypoint), second = Vector3.Distance(waypoint, canopy);
            float distance = Mathf.Clamp01(progress) * (first + second);
            return distance <= first ? Vector3.Lerp(work, waypoint, distance / first) :
                Vector3.Lerp(waypoint, canopy, (distance - first) / second);
        }
        private float RouteSpeed(float progress)
        {
            float t = Mathf.Clamp01(progress);
            return (Vector3.Distance(work, waypoint) + Vector3.Distance(waypoint, canopy)) * t * (1f - t);
        }
        private static float Smooth(float value) { float t = Mathf.Clamp01(value); return t * t * (3f - 2f * t); }
        private void OnEnable()
        {
            if (initialized) Sample(ElapsedSeconds, IsPresentationActive);
        }
        private void OnDisable()
        {
            if (hum != null) hum.Stop();
            if (Worker != null) Worker.ReleaseAnimation();
            audioPaused = false;
        }
        private void OnDestroy()
        {
            if (hum != null) hum.Stop();
            if (humClip != null) Destroy(humClip);
            if (Worker != null) NpcFootstepSources.Unregister(Worker.transform);
        }
    }
}
