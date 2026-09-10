using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityCanneryController
    {
        private readonly AudioSource[] voices = new AudioSource[4];
        private readonly AudioClip[] soundClips = new AudioClip[4];
        private Transform truckEngineAnchor, reverseAlarmAnchor;
        private AudioSource arrivalHorn;
        private AudioClip arrivalHornClip;
        private bool hasHornSample, hornPaused;
        private double previousHornSeconds;
        private long lastHornBatch = -1;
        public AudioSource TruckEngineSource => voices[0];
        public AudioSource SeamerSource => voices[1];
        public AudioSource RetortSource => voices[2];
        public AudioSource ReverseAlarmSource => voices[3];
        public AudioSource ArrivalHornSource => arrivalHorn;
        public int ArrivalHornPairsPlayed { get; private set; }

        private void CreateSounds(int seed)
        {
            truckEngineAnchor = Require(Truck, "ANCHOR_TruckEngine");
            reverseAlarmAnchor = Require(Truck, "ANCHOR_ReverseAlarm");
            soundClips[0] = CityOffshoreBoatSynthesis.CreateEngineClip(seed ^ 0x43414E, 1);
            soundClips[1] = CitySourceSoundSynthesis.CreateRuntimeClip(CitySourceSoundId.IndustrialWeighbridgeMechanismLoop, 1);
            soundClips[2] = CitySourceSoundSynthesis.CreateRuntimeClip(CitySourceSoundId.IndustrialWeighbridgeMechanismLoop, 2);
            soundClips[3] = CreateReverseAlarm();
            string[] names = { "Delivery truck engine", "Seamer motor", "Retort circulation", "Truck reversing alarm" };
            for (int i = 0; i < voices.Length; i++)
            {
                var host = new GameObject(names[i]);
                host.transform.SetParent(transform, false);
                AudioSource source = host.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = true;
                source.clip = soundClips[i];
                source.volume = 0;
                source.pitch = i == 0 ? .74f : i == 1 ? 1.08f : i == 2 ? .45f : 1f;
                CityWorkAudio.Configure(source, i == 1 || i == 2, i == 2 ? 2800f : i == 1 ? 6500f : 4000f,
                    i == 0 ? 32f : 24f);
                voices[i] = source;
            }
            var hornHost = new GameObject("Delivery truck arrival horn");
            hornHost.transform.SetParent(transform, false);
            arrivalHorn = hornHost.AddComponent<AudioSource>();
            arrivalHorn.playOnAwake = false;
            arrivalHorn.loop = false;
            arrivalHorn.clip = arrivalHornClip = CreateArrivalHorn();
            arrivalHorn.volume = .30f;
            CityWorkAudio.Configure(arrivalHorn, false, 3400f, 64f);
            arrivalHorn.minDistance = 6f;
        }

        private void LateUpdate() { if (IsInitialized) ApplySounds(); }

        private void ApplySounds()
        {
            AdvanceSounds(AutoAdvance && isActiveAndEnabled && GameSessionState.IsGameTimeRunning && !GameTimeScaleRuntime.IsPaused);
        }

        /// <summary>Samples the current production/transport state without advancing the session clock.</summary>
        public void AdvanceSounds(bool timeRunning)
        {
            for (int i = 0; i < voices.Length; i++)
            {
                AudioSource source = voices[i];
                if (source == null) continue;
                source.transform.position = i == 0 ? truckEngineAnchor.position :
                    i == 3 ? reverseAlarmAnchor.position :
                    i == 1 ? seamer.position : Plan.World(new Vector3(-5, 1.35f, 1.7f));
                bool near = ForcePresentation || hero == null || (hero.position - source.transform.position).sqrMagnitude <
                    Mathf.Pow(source.maxDistance + (source.isPlaying ? 8f : 4f), 2);
                bool visible = i == 0 || i == 3 ? TruckPresentationActive : FactoryPresentationActive;
                bool active = timeRunning && visible && near && (i == 0 ? Snapshot.IsDriving && !IsBlocked :
                    i == 3 ? IsReversing && !IsBlocked :
                    i == 1 ? Production.Stage == CityCanneryProductionStage.Seal : Production.Stage == CityCanneryProductionStage.Heat);
                source.volume = active ? (i == 0 ? .24f : i == 1 ? .32f : i == 2 ? .26f : .26f) : 0;
                if (active && !source.isPlaying) source.Play();
                else if (!active && source.isPlaying) source.Stop();
            }
            AdvanceArrivalHorn(timeRunning);
        }

        private void AdvanceArrivalHorn(bool timeRunning)
        {
            if (arrivalHorn == null) return;
            arrivalHorn.transform.position = truckEngineAnchor.position;
            double elapsed = hasHornSample ? WorkingSeconds - previousHornSeconds : 0d;
            bool continuous = hasHornSample && elapsed > 0d && elapsed <= .35d;
            bool audible = TruckPresentationActive && (ForcePresentation || hero == null ||
                (hero.position - arrivalHorn.transform.position).sqrMagnitude < arrivalHorn.maxDistance * arrivalHorn.maxDistance);
            if (!audible || elapsed < 0d || elapsed > .35d)
            {
                arrivalHorn.Stop();
                hornPaused = false;
            }
            if (!timeRunning && arrivalHorn.isPlaying)
            {
                arrivalHorn.Pause();
                hornPaused = true;
            }
            else if (timeRunning && audible && hornPaused)
            {
                arrivalHorn.UnPause();
                hornPaused = false;
            }
            // Only a nearby forward crossing sounds the pair. Reconstructing
            // City, revealing distant meshes or seeking never replays it.
            if (timeRunning && audible && continuous && !IsBlocked &&
                Snapshot.Stage == CityFishSupplyStage.PortArrive && Snapshot.Batch > lastHornBatch)
            {
                CityFishSupplySnapshot before = Cycle.Sample(previousHornSeconds);
                double trigger = System.Math.Max(0d, Snapshot.Duration - 1.2d);
                if (before.Batch == Snapshot.Batch && before.Stage == Snapshot.Stage &&
                    before.Seconds < trigger && Snapshot.Seconds >= trigger)
                {
                    arrivalHorn.Play();
                    lastHornBatch = Snapshot.Batch;
                    ArrivalHornPairsPlayed++;
                }
            }
            previousHornSeconds = WorkingSeconds;
            hasHornSample = true;
        }

        private static AudioClip CreateArrivalHorn()
        {
            const int sampleRate = 24000;
            var samples = new float[(int)(sampleRate * .82f)];
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)sampleRate;
                float local = t < .42f ? t : t - .42f;
                float duration = t < .42f ? .24f : .30f;
                float envelope = Mathf.Clamp01(local / .018f) * Mathf.Clamp01((duration - local) / .045f);
                // A restrained dual-tone road horn, with a real silent gap.
                samples[i] = envelope * (.28f * Mathf.Sin(2f * Mathf.PI * 310f * t) +
                    .22f * Mathf.Sin(2f * Mathf.PI * 390f * t) +
                    .06f * Mathf.Sin(2f * Mathf.PI * 620f * t));
            }
            AudioClip clip = AudioClip.Create("Truck arrival double horn", samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip CreateReverseAlarm()
        {
            const int sampleRate = 24000;
            // A short industrial beeper followed by silence. The loop belongs
            // to reverse motion, never to a UI event or a replayed handoff.
            var samples = new float[sampleRate];
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)sampleRate;
                float envelope = Mathf.Clamp01(t / .012f) * Mathf.Clamp01((.38f - t) / .018f);
                samples[i] = envelope * (.7f * Mathf.Sin(2f * Mathf.PI * 1050f * t) +
                    .12f * Mathf.Sin(2f * Mathf.PI * 2100f * t));
            }
            AudioClip clip = AudioClip.Create("Truck reverse beeper", samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void OnDisable()
        {
            foreach (AudioSource source in voices) if (source != null) source.Stop();
            arrivalHorn?.Stop();
            hornPaused = hasHornSample = false;
            if (portConversation != null) portConversation.SetDriverState(false, false, false, false, false, false);
        }

        private void DestroySounds()
        {
            foreach (AudioSource source in voices) if (source != null) source.Stop();
            arrivalHorn?.Stop();
            if (arrivalHornClip != null)
            {
                if (Application.isPlaying) Destroy(arrivalHornClip); else DestroyImmediate(arrivalHornClip);
            }
            foreach (AudioClip clip in soundClips)
            {
                if (clip == null) continue;
                if (Application.isPlaying) Destroy(clip); else DestroyImmediate(clip);
            }
        }
    }
}
