using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityCanneryController
    {
        private readonly AudioSource[] processVoices = new AudioSource[4];
        private readonly AudioClip[] processClips = new AudioClip[5];
        private readonly double[] lastProcessContact = { -1d, -1d, -1d, -1d };
        private readonly bool[] processVoicePaused = new bool[4];
        private AudioSource coldStoreFan;
        private Transform coldStoreSoundAnchor;
        private double previousProcessSeconds;
        private bool hasProcessSample, coldStorePaused;
        public int ProcessContactsPlayed { get; private set; }
        public AudioSource ColdStoreSoundSource => coldStoreFan;
        public AudioSource CanContactSource => processVoices[0];
        public AudioSource RetortContactSource => processVoices[1];
        public AudioSource ReceivingContactSource => processVoices[2];
        public AudioSource PressureReleaseSource => processVoices[3];

        private void CreateProcessSounds(int seed)
        {
            string[] names = { "Cannery tin contact", "Cannery retort latch", "Cannery crate landing", "Cannery pressure release" };
            for (int i = 0; i < processVoices.Length; i++)
            {
                processClips[i] = CreateProcessContactClip(i, seed);
                processVoices[i] = CreateFactoryVoice(names[i], false, i == 3 ? 3100f : 5500f, 15f);
                processVoices[i].clip = processClips[i];
            }
            coldStoreSoundAnchor = Require(equipment, "ANCHOR_ColdStoreSound");
            coldStoreFan = CreateFactoryVoice("Cannery cold-store fan", true, 1500f, 12f);
            float[] fan = StairwellSoundscapeSynthesis.GenerateVentilationLoopSamples();
            processClips[4] = AudioClip.Create("Cannery refrigeration", fan.Length, 1,
                StairwellSoundscapeSynthesis.SampleRate, false);
            processClips[4].SetData(fan, 0);
            coldStoreFan.clip = processClips[4];
            coldStoreFan.volume = .09f;
        }

        private AudioSource CreateFactoryVoice(string label, bool loop, float cutoff, float radius)
        {
            var host = new GameObject(label);
            host.transform.SetParent(factory, false);
            AudioSource source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            CityWorkAudio.Configure(source, true, cutoff, radius);
            return source;
        }

        private bool ProcessAudible(AudioSource source, Vector3 position) => FactoryPresentationActive &&
            (ForcePresentation || hero == null || (hero.position - position).sqrMagnitude < source.maxDistance * source.maxDistance);

        private void AdvanceProcessSounds(bool running)
        {
            if (coldStoreFan == null) return;
            coldStoreFan.transform.position = coldStoreSoundAnchor.position;
            if (!ProcessAudible(coldStoreFan, coldStoreSoundAnchor.position))
            { coldStoreFan.Stop(); coldStorePaused = false; }
            else if (!running)
            {
                if (coldStoreFan.isPlaying) { coldStoreFan.Pause(); coldStorePaused = true; }
            }
            else if (coldStorePaused) { coldStoreFan.UnPause(); coldStorePaused = false; }
            else if (!coldStoreFan.isPlaying) coldStoreFan.Play();

            double elapsed = hasProcessSample ? WorkingSeconds - previousProcessSeconds : 0d;
            bool jumped = hasProcessSample && (elapsed < 0d || elapsed > .35d);
            bool continuous = hasProcessSample && elapsed > 0d && elapsed <= .35d && running && !IsBlocked;
            for (int i = 0; i < processVoices.Length; i++)
            {
                AudioSource source = processVoices[i];
                if (jumped || !ProcessAudible(source, source.transform.position))
                { source.Stop(); processVoicePaused[i] = false; }
                else if (!running && source.isPlaying)
                { source.Pause(); processVoicePaused[i] = true; }
                else if (running && processVoicePaused[i])
                { source.UnPause(); processVoicePaused[i] = false; }
            }

            // Cross actual custody/contact instants. Sampled restoration consumes
            // old cues silently; no queue of missed operations survives a seek.
            for (int i = 0; i < CityFishSupplyCycle.HandlingUnits; i++)
                ProcessCue(2, Cycle.TransferUnitStart(CityFishSupplyStage.UnloadFish, i, Snapshot.Batch) +
                    CityFishSupplyCycle.TransferUnitDuration * .75d,
                    RawStore(CityFishSupplyCycle.HandlingUnits - 1 - i), .23f, continuous);
            for (int i = 0; i < CityFishSupplyCycle.HandlingUnits; i++)
            {
                ProcessCue(2, Cycle.InspectionPhaseStart(CityCanneryInspectionStage.SetDown, i, Snapshot.Batch) +
                    Cycle.InspectionPhaseDuration(CityCanneryInspectionStage.SetDown, i) * .70d,
                    ShippingScaleLoadPosition, .18f, continuous);
                ProcessCue(2, Cycle.InspectionPhaseStart(CityCanneryInspectionStage.PutAway, i, Snapshot.Batch) +
                    Cycle.InspectionPhaseDuration(CityCanneryInspectionStage.PutAway, i) * .70d,
                    ReadyStore(i) + Vector3.up * ShippingPalletHeight, .15f, continuous);
            }
            if (Production.IsActive)
            {
                double start = Cycle.ProductionStageStart(Production.Stage, Production.LotIndex, Snapshot.Batch);
                if (Production.Stage == CityCanneryProductionStage.Fill || Production.Stage == CityCanneryProductionStage.Seal ||
                    Production.Stage == CityCanneryProductionStage.Pack)
                {
                    bool pack = Production.Stage == CityCanneryProductionStage.Pack;
                    double lead = pack ? 8d : 2d, tail = pack ? 10d : 4d;
                    double phase = pack ? .90d : Production.Stage == CityCanneryProductionStage.Seal ? .58d : .35d;
                    for (int i = 0; i < canUnits.Length; i++)
                        ProcessCue(0, start + (lead + (Production.Duration - tail) * (i + phase) / canUnits.Length) /
                            CityFishSupplyCycle.ProductionSpeed, canUnits[i].position, pack ? .13f : .10f, continuous);
                }
                if (Production.Stage == CityCanneryProductionStage.LoadRetort || Production.Stage == CityCanneryProductionStage.Cool)
                {
                    ProcessCue(1, start + 1.5d / CityFishSupplyCycle.ProductionSpeed, retortDoor.position, .22f, continuous);
                    ProcessCue(1, start + (Production.Duration - .15d) / CityFishSupplyCycle.ProductionSpeed,
                        retortDoor.position, .25f, continuous);
                }
                if (Production.Stage == CityCanneryProductionStage.Cool)
                    ProcessCue(3, start + .08d / CityFishSupplyCycle.ProductionSpeed, Anchor("SteamOutlet"), .17f, continuous);
            }
            previousProcessSeconds = WorkingSeconds;
            hasProcessSample = true;
        }

        private void ProcessCue(int voice, double at, Vector3 point, float gain, bool continuous)
        {
            if (at > WorkingSeconds || at <= lastProcessContact[voice]) return;
            lastProcessContact[voice] = at;
            AudioSource source = processVoices[voice];
            if (!continuous || previousProcessSeconds >= at || !ProcessAudible(source, point)) return;
            source.transform.position = point;
            source.volume = gain;
            source.Play();
            processVoicePaused[voice] = false;
            ProcessContactsPlayed++;
        }

        private static AudioClip CreateProcessContactClip(int kind, int seed)
        {
            const int rate = 24000;
            float duration = kind == 3 ? 1.9f : kind == 0 ? .18f : .28f;
            var samples = new float[Mathf.CeilToInt(rate * duration)];
            var random = new System.Random(seed ^ (0x434150 + kind));
            float filtered = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)rate;
                float noise = (float)(random.NextDouble() * 2d - 1d);
                filtered += (noise - filtered) * (kind == 2 ? .13f : .4f);
                float fade = Mathf.Clamp01(t / .003f) * Mathf.Clamp01((duration - t) / .04f);
                float signal;
                if (kind == 3)
                    signal = filtered * .7f * Mathf.Clamp01(t / .08f) * Mathf.Pow(1f - t / duration, 1.6f);
                else
                {
                    float frequency = kind == 0 ? 1460f : kind == 1 ? 380f : 125f;
                    float ring = Mathf.Sin(t * frequency * 2f * Mathf.PI) +
                        .38f * Mathf.Sin(t * frequency * 3.71f * Mathf.PI);
                    signal = .32f * ring * Mathf.Exp(-t * (kind == 0 ? 30f : 23f)) +
                        .5f * filtered * Mathf.Exp(-t * (kind == 2 ? 17f : 58f));
                }
                samples[i] = Mathf.Clamp(signal * fade, -.85f, .85f);
            }
            AudioClip clip = AudioClip.Create("Cannery physical contact " + kind, samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void StopProcessSounds()
        {
            if (coldStoreFan != null) coldStoreFan.Stop();
            coldStorePaused = hasProcessSample = false;
            for (int i = 0; i < processVoices.Length; i++)
            { if (processVoices[i] != null) processVoices[i].Stop(); processVoicePaused[i] = false; }
        }

        private void DestroyProcessSounds()
        {
            StopProcessSounds();
            foreach (AudioClip clip in processClips)
                if (clip != null) { if (Application.isPlaying) Destroy(clip); else DestroyImmediate(clip); }
        }
    }
}
