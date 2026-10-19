using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Four finite rigged machines. Scene-owned time, cached pivots and causal local audio.</summary>
    public sealed partial class WarehouseArenaMechanisms : MonoBehaviour
    {
        public const float FanShutdownDuration = 5f;
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private enum Motion { Fan, Lift, Drum, Pusher }
        private struct Machine
        {
            public Motion Motion;
            public Transform Pivot;
            public Vector3 Origin;
            public Quaternion Rotation;
            public float Phase;
            public AudioSource Voice;
            public int Cycle;
        }

        private Machine[] machines = Array.Empty<Machine>();
        private CombatTestRoot owner;
        private WarehouseArenaPlan plan;
        private WarehouseArenaAmbience ambience;
        private AudioClip contact;
        private AudioClip fanGrind;
        private Renderer fanLens;
        private MaterialPropertyBlock fanProperties;
        private Color fanEmission;
        private float fanIntensity;
        private float lastFanFactor = -1f;
        private float lastFanLightFactor = -1f;
        private float lastFanProgress = -1f;
        private double fanShutdownSeconds;
        private bool initialized;
        private bool subscribed;
        private bool fanGrindPending;
        private bool audioPaused;
        public double ElapsedSeconds { get; private set; }
        public double MechanicalSeconds { get; private set; }
        public Transform FanRotor { get; private set; }
        public Light FanLight { get; private set; }
        public bool FanShutdownStarted { get; private set; }
        public float FanShutdownProgress => FanShutdownStarted ? (float)(fanShutdownSeconds / FanShutdownDuration) : 0f;
        public float FanSpeedFactor => 1f - FanShutdownProgress;
        public AudioSource FanGrindSource { get; private set; }
        public int FanGrindPlayCount { get; private set; }
        public int MachineCount => machines.Length;

        public void Initialize(WarehouseArenaPlan plan, CombatTestRoot warehouse)
        {
            if (initialized) throw new InvalidOperationException("Warehouse mechanisms already initialized.");
            this.plan = plan;
            owner = warehouse;
            // Rebuilding a cleared warehouse restores the final state silently.
            FanShutdownStarted = WarehouseFishLamp.AllSourcesCollected(plan);
            fanShutdownSeconds = FanShutdownStarted ? FanShutdownDuration : 0d;
            machines = new Machine[plan.Mechanisms.Count];
            float[] samples = StairwellSoundscapeSynthesis.GenerateCueSamples(StairwellSoundscapeCueKind.MetalStress);
            contact = AudioClip.Create("Warehouse mechanism strain", samples.Length, 1, StairwellSoundscapeSynthesis.SampleRate, false);
            contact.SetData(samples, 0);
            for (int i = 0; i < machines.Length; i++)
            {
                WarehouseArenaMechanism specification = plan.Mechanisms[i];
                Quaternion rotation = Quaternion.Euler(0f, specification.Yaw, 0f);
                Transform pivot;
                Motion motion;
                switch (specification.Kind)
                {
                    case "Fan":
                        GameObject housing = WarehouseArenaAssetProvider.Create("FanHousing", transform,
                            specification.Position + Vector3.up * .42f, rotation);
                        FanLight = WarehouseArenaLighting.CreateFanOverhead(
                            WarehouseArenaAssetProvider.FindAnchor(housing, "FanLight"), transform);
                        fanIntensity = FanLight.intensity;
                        foreach (Renderer candidate in housing.GetComponentsInChildren<Renderer>(true))
                            if (candidate.name.EndsWith("__Lamp", StringComparison.Ordinal))
                            {
                                fanLens = candidate;
                                break;
                            }
                        if (fanLens == null) throw new InvalidOperationException("Warehouse fan lacks its authored lamp lens.");
                        fanEmission = fanLens.sharedMaterial.GetColor(EmissionColorId);
                        fanProperties = new MaterialPropertyBlock();
                        fanLens.GetPropertyBlock(fanProperties);
                        pivot = WarehouseArenaAssetProvider.Create("FanRotor", transform, specification.Position, rotation).transform;
                        FanRotor = pivot;
                        InitializeDoorAmbushFan(housing);
                        ambience = gameObject.AddComponent<WarehouseArenaAmbience>();
                        ambience.Initialize(pivot, owner, FanSpeedFactor);
                        var grindVoice = new GameObject("Warehouse Fan Grinding");
                        grindVoice.transform.SetParent(transform, false);
                        grindVoice.transform.position = pivot.position;
                        FanGrindSource = grindVoice.AddComponent<AudioSource>();
                        FanGrindSource.playOnAwake = false;
                        CityWorkAudio.Configure(FanGrindSource, true, 3800f, 32f, false);
                        FanGrindSource.minDistance = 3f;
                        FanGrindSource.volume = .70f;
                        FanGrindSource.priority = 80;
                        motion = Motion.Fan;
                        break;
                    case "Lift":
                        WarehouseArenaAssetProvider.Create("LiftFrame", transform, specification.Position, rotation);
                        pivot = WarehouseArenaAssetProvider.Create("LiftPlatform", transform, specification.Position, rotation).transform;
                        motion = Motion.Lift;
                        break;
                    case "Drum":
                        GameObject drum = WarehouseArenaAssetProvider.Create("ChainDrum", transform, specification.Position, rotation);
                        pivot = WarehouseArenaAssetProvider.FindAnchor(drum, "DrumRotor");
                        motion = Motion.Drum;
                        break;
                    case "Pusher":
                        WarehouseArenaAssetProvider.Create("PusherFrame", transform, specification.Position, rotation);
                        pivot = WarehouseArenaAssetProvider.Create("PusherHead", transform, specification.Position, rotation).transform;
                        motion = Motion.Pusher;
                        break;
                    default: throw new InvalidOperationException("Unknown warehouse machine " + specification.Kind);
                }
                machines[i] = new Machine { Motion = motion, Pivot = pivot, Origin = pivot.localPosition,
                    Rotation = pivot.localRotation, Phase = specification.Phase, Cycle = -1 };
                if (motion != Motion.Fan)
                {
                    var voice = new GameObject("Warehouse " + specification.Kind + " Strain");
                    voice.transform.SetParent(transform, false);
                    voice.transform.localPosition = specification.Position + Vector3.up;
                    AudioSource source = voice.AddComponent<AudioSource>();
                    source.playOnAwake = false;
                    CityWorkAudio.Configure(source, true, 2100f, 13f, false);
                    source.clip = contact;
                    source.volume = .07f;
                    source.pitch = motion == Motion.Lift ? .64f : motion == Motion.Drum ? .82f : 1.04f;
                    machines[i].Voice = source;
                }
            }
            initialized = true;
            Subscribe();
            ApplyFanPower();
            ApplyPoses(false);
        }

        private void Subscribe()
        {
            if (subscribed || !isActiveAndEnabled) return;
            GameSessionState.WorldItemsChanged += RefreshFanCompletion;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            GameSessionState.WorldItemsChanged -= RefreshFanCompletion;
            subscribed = false;
        }

        private void RefreshFanCompletion()
        {
            if (!initialized) return;
            bool complete = WarehouseFishLamp.AllSourcesCollected(plan);
            if (complete == FanShutdownStarted) return;
            FanShutdownStarted = complete;
            fanShutdownSeconds = 0d;
            if (!complete)
            {
                ResetDoorAmbushOverdrive();
                fanGrindPending = false;
                if (FanGrindSource != null) FanGrindSource.Stop();
                ApplyFanPower();
                return;
            }
            if (FanGrindSource == null) return;
            if (fanGrind == null)
            {
                float[] samples = CreateFanGrindSamples();
                fanGrind = AudioClip.Create("Warehouse fan metal grinding", samples.Length, 1,
                    StairwellSoundscapeSynthesis.SampleRate, false);
                fanGrind.SetData(samples, 0);
                FanGrindSource.clip = fanGrind;
            }
            // A successful Take is committed inside the refrigerator modal.
            // Defer native playback until its pause owner has released input.
            fanGrindPending = true;
            SynchronizeAudio(Running);
        }

        private bool Running => owner != null && owner.isActiveAndEnabled && !GameTimeScaleRuntime.IsPaused;

        private void Update() => Advance(Time.deltaTime);

        private void SynchronizeAudio(bool running)
        {
            if (audioPaused == running)
            {
                foreach (Machine machine in machines)
                    if (machine.Voice != null)
                    {
                        if (running) machine.Voice.UnPause();
                        else machine.Voice.Pause();
                    }
                if (FanGrindSource != null)
                {
                    if (running) FanGrindSource.UnPause();
                    else FanGrindSource.Pause();
                }
                audioPaused = !running;
            }
            if (running && fanGrindPending && FanGrindSource != null)
            {
                fanGrindPending = false;
                FanGrindSource.Play();
                FanGrindPlayCount++;
            }
        }

        public void Advance(float seconds)
        {
            if (!initialized) return;
            bool running = Running && (!DoorAmbushOverdriveActive || DoorAmbushOverdriveCanRun);
            SynchronizeAudio(running);
            SynchronizeDoorAmbushAudio(running);
            if (!running || seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            ElapsedSeconds += seconds;
            if (FanShutdownStarted)
            {
                double previous = fanShutdownSeconds;
                fanShutdownSeconds = Math.Min(FanShutdownDuration, previous + seconds);
                double step = fanShutdownSeconds - previous;
                // All drives share the integral of the linear motor ramp. Their
                // authored phase stays continuous at the trigger and is stable
                // across frame sizes and long background catch-up advances.
                MechanicalSeconds += step * (1d - (previous + fanShutdownSeconds) / (2d * FanShutdownDuration));
                ApplyFanPower();
                if (fanShutdownSeconds >= FanShutdownDuration && FanGrindSource != null && !DoorAmbushOverdriveActive)
                {
                    fanGrindPending = false;
                    FanGrindSource.Stop();
                }
            }
            else MechanicalSeconds += seconds;
            AdvanceDoorAmbushOverdrive(seconds);
            // A background jump restores phase without replaying missed contacts.
            ApplyPoses(seconds <= .25f);
        }

        internal void FinishShutdownForDebug()
        {
            if (!initialized || !WarehouseFishLamp.AllSourcesCollected(plan))
                throw new InvalidOperationException("The warehouse fan needs all committed fish sources.");
            ResetDoorAmbushOverdrive();
            FanShutdownStarted = true;
            double previous = fanShutdownSeconds;
            double remaining = FanShutdownDuration - previous;
            MechanicalSeconds += remaining * (1d - (previous + FanShutdownDuration) / (2d * FanShutdownDuration));
            ElapsedSeconds += remaining;
            fanShutdownSeconds = FanShutdownDuration;
            fanGrindPending = false;
            if (FanGrindSource != null && !DoorAmbushOverdriveActive) FanGrindSource.Stop();
            ApplyFanPower();
            ApplyPoses(false);
        }

        private void ApplyPoses(bool sound)
        {
            for (int i = 0; i < machines.Length; i++)
            {
                ref Machine machine = ref machines[i];
                double time = MechanicalSeconds + machine.Phase;
                float phase;
                int cycle;
                switch (machine.Motion)
                {
                    case Motion.Fan:
                        if (DoorAmbushOverdriveActive) continue;
                        machine.Pivot.localRotation = machine.Rotation * Quaternion.Euler(0f, (float)(time * 9d % 360d), 0f);
                        continue;
                    case Motion.Drum:
                        machine.Pivot.localRotation = machine.Rotation * Quaternion.Euler((float)(time * -17d % 360d), 0f, 0f);
                        cycle = (int)(time / 13d);
                        break;
                    case Motion.Lift:
                        phase = (float)(time % 24d) / 24f;
                        machine.Pivot.localPosition = machine.Origin + Vector3.up * Mathf.Lerp(.4f, 6.6f, .5f - .5f * Mathf.Cos(phase * Mathf.PI * 2f));
                        cycle = (int)(time / 12d);
                        break;
                    default:
                        phase = (float)(time % 8d) / 8f;
                        machine.Pivot.localPosition = machine.Origin + machine.Rotation * Vector3.forward *
                            Mathf.Lerp(.2f, .65f, .5f - .5f * Mathf.Cos(phase * Mathf.PI * 2f));
                        cycle = (int)(time / 4d);
                        break;
                }
                if (cycle != machine.Cycle && machine.Cycle >= 0 && sound && FanSpeedFactor > 0f && owner.Hero != null &&
                    (owner.Hero.transform.position - machine.Voice.transform.position).sqrMagnitude < 169f)
                    machine.Voice.Play();
                machine.Cycle = cycle;
            }
        }

        private void ApplyFanPower()
        {
            float progress = FanShutdownProgress;
            if (lastFanProgress != progress)
            {
                lastFanProgress = progress;
                // Lamp failure can make power zero before the motor reaches its terminal pose.
                // Audio admission follows the actual rundown clock independently of that power gate.
                owner?.Music?.ApplyFanShutdown(progress);
                owner?.Ambience?.ApplyFanShutdown(DoorAmbushOverdriveActive ? 0f : progress);
                owner?.ApplyWarehouseFanAftermath(progress);
            }
            float factor = GameSessionState.WarehouseFishLamp.Phase == WarehouseFishLampPhase.Burst ? 0f : FanSpeedFactor;
            float lightFactor = DoorAmbushOverdriveActive
                ? Mathf.Clamp01(doorAmbushOverdriveSpeed / DoorAmbushOverdriveMaximumSpeed) : factor;
            if (lastFanLightFactor != lightFactor)
            {
                lastFanLightFactor = lightFactor;
                if (FanLight != null)
                {
                    FanLight.intensity = fanIntensity * lightFactor;
                    FanLight.enabled = lightFactor > 0f;
                }
                if (fanLens != null)
                {
                    // Retain the blackout owner's other per-renderer values.
                    fanLens.GetPropertyBlock(fanProperties);
                    fanProperties.SetColor(EmissionColorId, fanEmission * lightFactor);
                    fanLens.SetPropertyBlock(fanProperties);
                }
            }
            if (lastFanFactor == factor) return;
            lastFanFactor = factor;
            if (ambience != null) ambience.SetPower(factor);
            foreach (Machine machine in machines)
                if (machine.Voice != null)
                {
                    machine.Voice.volume = .07f * factor;
                    if (factor <= 0f) machine.Voice.Stop();
                }
        }

        private static float[] CreateFanGrindSamples()
        {
            int sampleRate = StairwellSoundscapeSynthesis.SampleRate;
            var samples = new float[Mathf.RoundToInt(FanShutdownDuration * sampleRate)];
            uint noiseState = 0x64A8F23Du;
            double scrapePhase = 0d, metalPhase = 0d;
            float filteredNoise = 0f;
            for (int index = 0; index < samples.Length; index++)
            {
                float time = (float)index / sampleRate;
                float progress = time / FanShutdownDuration;
                float speed = 1f - progress;
                noiseState = unchecked(noiseState * 1664525u + 1013904223u);
                float noise = (noiseState >> 8) * (2f / 16777215f) - 1f;
                filteredNoise += .16f * (noise - filteredNoise);
                scrapePhase += (5d + 20d * speed) / sampleRate;
                metalPhase += (135d + 96d * speed) / sampleRate;
                float chatter = .35f + .65f * Mathf.Pow(.5f + .5f * (float)Math.Sin(scrapePhase * Math.PI * 2d), 3f);
                float scrape = (noise - filteredNoise) * .70f + filteredNoise * .28f;
                float metal = (float)(Math.Sin(metalPhase * Math.PI * 2d) * .16d +
                    Math.Sin(metalPhase * Math.PI * 2d * 2.713d) * .09d +
                    Math.Sin(metalPhase * Math.PI * 2d * 4.37d) * .055d);
                float attack = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / .10f));
                float release = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((FanShutdownDuration - time) / .50f));
                samples[index] = Mathf.Clamp((scrape * chatter + metal) * attack * release * .85f, -.95f, .95f);
            }
            return samples;
        }

        private void OnEnable()
        {
            if (!initialized) return;
            Subscribe();
            // If completion happened while disabled, restore state without a
            // second shutdown performance or grinding replay.
            bool complete = WarehouseFishLamp.AllSourcesCollected(plan);
            if (complete != FanShutdownStarted)
            {
                if (!complete) ResetDoorAmbushOverdrive();
                FanShutdownStarted = complete;
                fanShutdownSeconds = complete ? FanShutdownDuration : 0d;
                ApplyFanPower();
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
            foreach (Machine machine in machines) if (machine.Voice != null) machine.Voice.Stop();
            fanGrindPending = false;
            if (FanGrindSource != null) FanGrindSource.Stop();
            SynchronizeDoorAmbushAudio(false);
            audioPaused = false;
        }

        private void OnDestroy()
        {
            Unsubscribe();
            DisposeDoorAmbushOverdrive();
            if (FanGrindSource != null) { FanGrindSource.Stop(); FanGrindSource.clip = null; }
            if (contact != null) Destroy(contact);
            if (fanGrind != null) Destroy(fanGrind);
        }
    }
}
