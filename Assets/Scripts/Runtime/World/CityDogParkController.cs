using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One bounded group: calendar activity, physical movement, shared animal sounds and ordinary permanent owners.</summary>
    [DefaultExecutionOrder(215)]
    public sealed class CityDogParkController : MonoBehaviour
    {
        private sealed class Dog
        {
            public Transform Host;
            public CityDogPose Pose;
            public AudioSource Voice, Feet;
            public float Radius, Distance, Speed;
            public int LastStep;
        }
        public CityDogParkPlan Plan { get; private set; }
        public IReadOnlyList<Transform> Dogs { get; private set; }
        public IReadOnlyList<Transform> Owners { get; private set; }
        public bool AutoAdvance { get; set; } = true;
        public bool ForcePresentation { get; set; }
        public Transform PresentationObserver { get; set; }
        public double ElapsedSeconds { get; private set; }
        public bool IsPresentationActive { get; private set; } = true;
        public float MaximumPawContactError { get; private set; }
        private readonly Dog[] dogs = new Dog[3];
        private readonly VillageResidentPresentation[] owners = new VillageResidentPresentation[2];
        private readonly AudioClip[] barks = new AudioClip[3];
        private readonly Transform[] thighs = new Transform[2], shins = new Transform[2], feet = new Transform[2];
        private readonly Quaternion[] footRotations = new Quaternion[2];
        private Transform pelvis;
        private Vector3 standingAnchor, seatedAnchor;
        private Renderer[] dynamicRenderers;
        private AudioClip pawClip;
        private bool initialized, audioPaused;
        private double lastPoseSeconds = double.NegativeInfinity, lastBarkSlot;

        public void Initialize(CityDogParkPlan plan, Transform model)
        {
            if (initialized) throw new InvalidOperationException("Dog park is already initialized.");
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            if (model == null) throw new ArgumentNullException(nameof(model));
            standingAnchor = plan.InverseWorld(CityDogParkAssetProvider.FindPart(model, "ANCHOR_OwnerStanding").position);
            seatedAnchor = plan.InverseWorld(CityDogParkAssetProvider.FindPart(model, "ANCHOR_OwnerSeated").position);
            var dogTransforms = new Transform[3]; var ownerTransforms = new Transform[2];
            pawClip = CreatePawClip();
            for (int i = 0; i < 3; i++)
            {
                Transform host = new GameObject(i == 0 ? "Small Dog" : i == 1 ? "Shaggy Dog" : "Large Dog").transform;
                host.SetParent(transform, false);
                GameObject animal = CityDogParkAssetProvider.CreateDog(host, i);
                float radius = i == 0 ? .22f : i == 1 ? .30f : .37f;
                var collider = host.gameObject.AddComponent<CapsuleCollider>();
                collider.direction = 2; collider.center = new Vector3(0, radius + .04f, 0);
                collider.radius = radius; collider.height = radius * 3.2f;
                var dog = new Dog { Host = host, Radius = radius, Pose = new CityDogPose(host, animal.transform) };
                dog.Voice = Source(host, "Dog voice", .17f, 23f);
                dog.Feet = Source(host, "Soft paws on earth", .045f, 9f);
                dog.Feet.clip = pawClip;
                float[] samples = AlpineVillageSoundSynthesis.GenerateSamples(AlpineVillageSoundKind.DogBehindFence, i);
                barks[i] = AudioClip.Create("Residential dog bark " + i, samples.Length, 1, AlpineVillageSoundSynthesis.SampleRate, false);
                barks[i].SetData(samples, 0); dog.Voice.clip = barks[i];
                dog.Voice.pitch = i == 0 ? 1.38f : i == 1 ? 1.05f : .77f;
                dogs[i] = dog; dogTransforms[i] = host;
            }
            for (int i = 0; i < 2; i++)
            {
                owners[i] = DefaultNpcFactory.CreateForCharacter(transform, i == 0 ?
                    DefaultNpcPopulation.CityDogParkStandingOwner : DefaultNpcPopulation.CityDogParkSeatedOwner);
                owners[i].name = i == 0 ? "Standing Dog Owner" : "Seated Dog Owner";
                CityPortCrew.AlignWorkerModelWithPlacement(owners[i]);
                var body = owners[i].gameObject.AddComponent<CapsuleCollider>();
                body.radius = .23f; body.height = i == 0 ? 1.7f : 1.1f;
                body.center = Vector3.up * (i == 0 ? .88f : 1.18f);
                ownerTransforms[i] = owners[i].transform;
            }
            Transform Joint(string name) => CityPedestrianHandProps.FindSocket(owners[1].ModelRoot, name)
                ?? throw new InvalidOperationException("Dog owner is missing shared rig joint " + name);
            pelvis = Joint("pelvis");
            for (int i = 0; i < 2; i++)
            {
                string side = i == 0 ? ".L" : ".R";
                thighs[i] = Joint("thigh" + side); shins[i] = Joint("shin" + side); feet[i] = Joint("foot" + side);
                footRotations[i] = Quaternion.Inverse(owners[1].transform.rotation) * feet[i].rotation;
            }
            Dogs = Array.AsReadOnly(dogTransforms); Owners = Array.AsReadOnly(ownerTransforms);
            var renderers = new List<Renderer>();
            foreach (Dog dog in dogs) renderers.AddRange(dog.Host.GetComponentsInChildren<Renderer>(true));
            foreach (VillageResidentPresentation owner in owners) renderers.AddRange(owner.GetComponentsInChildren<Renderer>(true));
            dynamicRenderers = renderers.ToArray();
            initialized = true;
            ApplyAt(CalendarSeconds());
        }

        private static AudioSource Source(Transform parent, string name, float volume, float reach)
        {
            var host = new GameObject(name); host.transform.SetParent(parent, false);
            host.transform.localPosition = Vector3.up * .35f;
            var source = host.AddComponent<AudioSource>(); source.playOnAwake = false;
            source.spatialBlend = 1; source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = .9f; source.maxDistance = reach; source.dopplerLevel = 0;
            source.volume = volume; source.outputAudioMixerGroup = GameAudioMixer.AmbienceDetailsGroup;
            return source;
        }

        private static double CalendarSeconds() => (GameSessionState.GameDayIndex * 1440d +
            GameSessionState.GameTimeOfDayMinutes) / GameTimeState.GameMinutesPerRealSecond;

        public void ApplyAt(double seconds)
        {
            if (!initialized) throw new InvalidOperationException("Dog park is not initialized.");
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            ElapsedSeconds = seconds;
            for (int i = 0; i < dogs.Length; i++)
            {
                CityDogActivity.Sample sample = CityDogActivity.Evaluate(i, seconds);
                dogs[i].Host.SetPositionAndRotation(Plan.World(new Vector3(sample.X, 0f, sample.Z)),
                    Plan.Rotation * Quaternion.Euler(0f, sample.HeadingDegrees, 0f));
                dogs[i].Distance = sample.Distance; dogs[i].Speed = sample.Speed;
            }
            lastBarkSlot = Math.Floor(seconds / 17d); lastPoseSeconds = double.NegativeInfinity;
            Pose(seconds);
        }

        private void LateUpdate()
        {
            if (!initialized) return;
            bool paused = GameTimeScaleRuntime.IsPaused || !GameSessionState.IsGameTimeRunning;
            double previous = ElapsedSeconds;
            if (AutoAdvance && !paused) ElapsedSeconds = CalendarSeconds();
            float delta = (float)Math.Max(0, ElapsedSeconds - previous);
            float distance = PresentationObserver == null ? 0 : (PresentationObserver.position - Plan.Position).sqrMagnitude;
            bool present = ForcePresentation || distance <= (IsPresentationActive ? 72f * 72f : 64f * 64f);
            bool changed = present != IsPresentationActive;
            IsPresentationActive = present;
            if (!paused && delta > 0)
                for (int i = 0; i < dogs.Length; i++) Move(i, delta);
            if (changed)
            {
                foreach (Renderer renderer in dynamicRenderers) renderer.enabled = present;
                if (!present) foreach (VillageResidentPresentation owner in owners) owner.ReleaseAnimation();
            }
            if (present && (!paused || changed) && (ForcePresentation || changed || ElapsedSeconds - lastPoseSeconds >= 1d / 30d))
                Pose(ElapsedSeconds);
            UpdateAudio(paused, present && (ForcePresentation || distance < 26f * 26f));
        }

        private void Move(int index, float delta)
        {
            Dog dog = dogs[index]; CityDogActivity.Sample sample = CityDogActivity.Evaluate(index, ElapsedSeconds);
            Vector3 target = Plan.World(new Vector3(sample.X, 0, sample.Z));
            Vector3 old = dog.Host.position;
            Vector3 next = Vector3.MoveTowards(old, target, Mathf.Min(delta, .25f) * (index == 2 ? 1.4f : 2.8f));
            Vector3 step = next - old;
            // A circumscribed disc includes both capsule ends at every yaw,
            // so the turn after the sweep cannot swing a muzzle into a body.
            bool clear = Plan.AllowsDogAt(Plan.InverseWorld(next), dog.Radius * 1.6f + .05f) && ClearOfBodies(index, next);
            if (clear && step.sqrMagnitude > .0000001f)
            {
                // The cast stops at the nearest physical body; our starting
                // capsule overlaps its own collider, which casts ignore.
                Vector3 middle = old + Vector3.up * (dog.Radius + .06f);
                float half = dog.Radius * .6f;
                if (Physics.CapsuleCast(middle - dog.Host.forward * half, middle + dog.Host.forward * half,
                    dog.Radius, step.normalized, out RaycastHit hit, step.magnitude + .025f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) clear = false;
            }
            if (clear)
            {
                dog.Host.position = next;
                float moved = step.magnitude;
                dog.Speed = delta > 0 ? moved / delta : 0;
                dog.Distance += moved;
                Quaternion facing = moved > .001f ? Quaternion.LookRotation(step, Vector3.up) :
                    Plan.Rotation * Quaternion.Euler(0, sample.HeadingDegrees, 0);
                dog.Host.rotation = Quaternion.RotateTowards(dog.Host.rotation, facing, delta * 200f);
            }
            else dog.Speed = 0;
        }

        private bool ClearOfBodies(int index, Vector3 point)
        {
            Dog dog = dogs[index];
            for (int i = 0; i < dogs.Length; i++)
                if (i != index && PlanarSquared(point, dogs[i].Host.position) <
                    Mathf.Pow((dog.Radius + dogs[i].Radius) * 1.6f + .06f, 2)) return false;
            if (PresentationObserver != null && Mathf.Abs(point.y - PresentationObserver.position.y) < 1.8f &&
                PlanarSquared(point, PresentationObserver.position) < Mathf.Pow(dog.Radius * 1.6f + .42f, 2)) return false;
            for (int i = 0; i < owners.Length; i++)
                if (PlanarSquared(point, owners[i].transform.position) < Mathf.Pow(dog.Radius * 1.6f + .3f, 2)) return false;
            return true;
        }
        private static float PlanarSquared(Vector3 a, Vector3 b) { float x = a.x - b.x, z = a.z - b.z; return x * x + z * z; }

        private void Pose(double seconds)
        {
            MaximumPawContactError = 0;
            for (int i = 0; i < dogs.Length; i++)
            {
                Dog dog = dogs[i]; dog.Pose.Apply(seconds, dog.Distance, dog.Speed, CityDogActivity.Evaluate(i, seconds));
                MaximumPawContactError = Mathf.Max(MaximumPawContactError, dog.Pose.PawError);
            }
            Vector3 look = dogs[(int)(seconds / 9d) % 3].Host.position + Vector3.up * .45f;
            owners[0].transform.SetPositionAndRotation(Plan.World(standingAnchor),
                Plan.Rotation * Quaternion.Euler(0, -135, 0));
            owners[0].ApplyIdleVariation((float)(seconds % 3600d), true, .25f, look);
            VillageResidentPresentation seated = owners[1];
            seated.transform.SetPositionAndRotation(Plan.World(seatedAnchor), Plan.Rotation);
            seated.ApplyIdleVariation((float)(seconds % 3600d) + 3.5f, true, .1f, look);
            Vector3 seat = Plan.World(seatedAnchor + Vector3.up * .56f);
            seated.transform.position += seat - pelvis.position;
            for (int i = 0; i < 2; i++)
                LimbTwoBoneIk.Solve(thighs[i], shins[i], feet[i],
                    Plan.World(seatedAnchor + new Vector3(i == 0 ? -.16f : .16f, .1f, .48f)),
                    Plan.Rotation * footRotations[i], seat + Plan.Forward, 1f, .999f, true);
            seated.ApplyHandContacts(Plan.World(seatedAnchor + new Vector3(.22f, .72f, .32f)),
                Plan.World(seatedAnchor + new Vector3(-.22f, .72f, .32f)));
            lastPoseSeconds = seconds;
        }

        private void UpdateAudio(bool paused, bool audible)
        {
            if (paused)
            {
                if (!audioPaused) foreach (Dog dog in dogs) { dog.Voice.Pause(); dog.Feet.Pause(); }
                audioPaused = true; return;
            }
            if (audioPaused) foreach (Dog dog in dogs) { dog.Voice.UnPause(); dog.Feet.UnPause(); }
            audioPaused = false;
            double slot = Math.Floor(ElapsedSeconds / 17d);
            if (!audible)
            {
                foreach (Dog dog in dogs) { dog.Voice.Stop(); dog.Feet.Stop(); }
                lastBarkSlot = slot; return;
            }
            if (slot > lastBarkSlot)
            {
                int index = (int)(slot % 3);
                dogs[index].Voice.Play(); lastBarkSlot = slot;
            }
            foreach (Dog dog in dogs)
            {
                int step = (int)(dog.Distance / (dog.Radius * .65f));
                if (step != dog.LastStep && dog.Speed > .15f && !dog.Feet.isPlaying) dog.Feet.Play();
                dog.LastStep = step;
            }
        }

        private static AudioClip CreatePawClip()
        {
            const int rate = 22050; var samples = new float[rate / 12]; uint state = 0xD06F00Du;
            for (int i = 0; i < samples.Length; i++)
            {
                state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                float t = i / (float)rate;
                samples[i] = ((state & 65535) / 32767.5f - 1) * Mathf.Exp(-t * 65) * .18f * Mathf.Min(1, t * 1500);
            }
            AudioClip clip = AudioClip.Create("Soft paws on earth", samples.Length, 1, rate, false); clip.SetData(samples, 0); return clip;
        }
        private void OnDisable()
        {
            if (!initialized) return;
            foreach (Dog dog in dogs) { dog.Voice.Stop(); dog.Feet.Stop(); }
            foreach (VillageResidentPresentation owner in owners) owner.ReleaseAnimation();
            audioPaused = false;
        }
        private void OnDestroy()
        {
            foreach (VillageResidentPresentation owner in owners) if (owner != null) NpcFootstepSources.Unregister(owner.transform);
            foreach (AudioClip clip in barks) if (clip != null) Destroy(clip);
            if (pawClip != null) Destroy(pawClip);
        }
    }
}
