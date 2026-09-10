using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Bounded physical voices. Seeking reconstructs state without replaying cargo impacts.</summary>
    [DefaultExecutionOrder(210)]
    public sealed class CityPortSound : MonoBehaviour
    {
        public const double ArrivalHornAtSeconds = CityPortCycle.ApproachDurationSeconds - 4d;
        public const double DepartureHornAtSeconds = CityPortCycle.CycleDurationSeconds -
            CityPortCycle.IdleDurationSeconds - CityPortCycle.DepartDurationSeconds;
        public const float HornTailSeconds = 4.6f;
        private CityPortController port;
        private Transform engineAnchor, hornAnchor;
        private readonly Transform[] driveAnchors = new Transform[2];
        private readonly AudioSource[] loops = new AudioSource[4];
        private readonly AudioClip[] clips = new AudioClip[4];
        private readonly bool[] started = new bool[4];
        private readonly float[] gain = new float[4];
        private AudioSource contact, horn;
        private AudioClip hornClip;
        private AudioEchoFilter hornEcho;
        private AudioReverbFilter hornReverb;
        private bool hornSignalActive;
        private long lastArrivalHornCycle = -1, lastDepartureHornCycle = -1;
        private double previousSeconds;
        private bool hasPrevious, paused;
        public AudioSource EngineSource => loops[0];
        public AudioSource FirstCraneSource => loops[1];
        public AudioSource SecondCraneSource => loops[2];
        public AudioSource TrolleySource => loops[3];
        public AudioSource ContactSource => contact;
        public AudioSource HornSource => horn;
        public int ContactsPlayed { get; private set; }
        public int HornsPlayed { get; private set; }
        public bool IsInitialized { get; private set; }
        public bool IsPaused => paused;

        public static CityPortSound Build(Transform parent, CityPortController controller, int seed)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            var host = new GameObject("Port Mechanical Sounds");
            host.transform.SetParent(parent, false);
            var sound = host.AddComponent<CityPortSound>();
            sound.port = controller;
            sound.engineAnchor = CityPortAssetProvider.FindPart(controller.Vessel.gameObject, "ANCHOR_Engine");
            sound.hornAnchor = CityPortAssetProvider.FindPart(controller.Vessel.gameObject, "ANCHOR_Horn");
            for (int i = 0; i < sound.driveAnchors.Length; i++)
                sound.driveAnchors[i] = CityPortAssetProvider.FindPart(controller.CraneBases[i].gameObject, "ANCHOR_HoistFeed");
            sound.clips[0] = CityOffshoreBoatSynthesis.CreateEngineClip(seed ^ 0x504F5254, 0);
            sound.clips[1] = CitySourceSoundSynthesis.CreateRuntimeClip(CitySourceSoundId.IndustrialWeighbridgeMechanismLoop, 0);
            sound.clips[2] = CitySourceSoundSynthesis.CreateRuntimeClip(CitySourceSoundId.IndustrialWeighbridgeMechanismLoop, 2);
            sound.clips[3] = CitySourceSoundSynthesis.CreateRuntimeClip(CitySourceSoundId.DryingYardCarpetStrike, 1);
            string[] names = { "Trawler Diesel", "West Crane Drive", "East Crane Drive", "Cargo Trolley Wheels" };
            for (int i = 0; i < sound.loops.Length; i++)
            {
                sound.loops[i] = sound.CreateVoice(names[i], true,
                    i == 0 ? 4000f : i == 3 ? 4200f : 6500f);
                int clipIndex = i == 0 ? 0 : i == 3 ? 2 : 1;
                sound.loops[i].clip = sound.clips[clipIndex];
                sound.loops[i].pitch = i == 3 ? .62f : i == 2 ? .92f : 1f;
            }
            sound.contact = sound.CreateVoice("Cargo Landing", false, 6500f);
            sound.contact.clip = sound.clips[3];
            sound.contact.pitch = .72f;
            sound.contact.volume = .36f;
            sound.horn = sound.CreateVoice("Trawler Arrival and Departure Horn", false, 1250f);
            float[] dryHorn = CityOffshoreBoatSynthesis.GenerateHornSamples(seed ^ 0x504F5254, 0);
            var hornSamples = new float[dryHorn.Length + Mathf.RoundToInt(CityOffshoreBoatSynthesis.SampleRate * HornTailSeconds)];
            Array.Copy(dryHorn, hornSamples, dryHorn.Length);
            sound.hornClip = AudioClip.Create("Trawler low horn with reflection tail", hornSamples.Length, 1,
                CityOffshoreBoatSynthesis.SampleRate, false);
            sound.hornClip.SetData(hornSamples, 0);
            sound.horn.clip = sound.hornClip;
            sound.horn.volume = .32f;
            sound.horn.minDistance = 6f;
            sound.horn.maxDistance = 64f;
            // The roof horn alone has long harbour reflections. The appended
            // zero-input region lets the real DSP ring out on this same local
            // voice, under its distance, pause and lifetime owner.
            sound.hornEcho = sound.horn.gameObject.AddComponent<AudioEchoFilter>();
            sound.hornEcho.delay = 620f;
            sound.hornEcho.decayRatio = .42f;
            sound.hornEcho.wetMix = .30f;
            sound.hornEcho.dryMix = 1f;
            sound.hornReverb = sound.horn.GetComponent<AudioReverbFilter>();
            sound.hornReverb.room = -1400f;
            sound.hornReverb.roomHF = -2800f;
            sound.hornReverb.decayTime = 3.8f;
            sound.hornReverb.decayHFRatio = .42f;
            sound.hornReverb.reflectionsLevel = -1200f;
            sound.hornReverb.reflectionsDelay = .12f;
            sound.hornReverb.reverbLevel = -1200f;
            sound.hornReverb.reverbDelay = .08f;
            sound.hornReverb.diffusion = 82f;
            sound.hornReverb.density = 65f;
            sound.RememberHornWindows(controller.ElapsedSeconds);
            sound.previousSeconds = controller.ElapsedSeconds;
            sound.hasPrevious = true;
            sound.IsInitialized = true;
            sound.SyncAnchors();
            return sound;
        }

        private void LateUpdate()
        {
            if (port == null) { OnDisable(); return; }
            Advance(port.ElapsedSeconds, Time.deltaTime, (port.AutoAdvance || port.IsSupplyDriven) &&
                port.isActiveAndEnabled && GameSessionState.IsGameTimeRunning && !GameTimeScaleRuntime.IsPaused);
        }

        public void Advance(double seconds, float deltaTime, bool timeRunning)
        {
            if (!IsInitialized || port == null) return;
            if (!port.ShorePresentationActive && !port.VesselPresentationActive)
            { OnDisable(); return; }
            timeRunning &= port.isActiveAndEnabled;
            CityPortCycleSnapshot now = CityPortCycle.Sample(seconds);
            SyncAnchors();
            SetPaused(!timeRunning);
            float delta = float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) ? 0f : Mathf.Clamp(deltaTime, 0f, .5f);
            double elapsed = hasPrevious ? seconds - previousSeconds : 0d;
            // Day changes, captures, reloads and clock seeks never catch up
            // impacts; only a nearby forward crossing in ordinary playback does.
            bool continuous = hasPrevious && elapsed > 0d && elapsed <= .35d &&
                elapsed <= Math.Max(.08d, delta * 2.5d + .02d);
            if (elapsed < 0d || elapsed > .35d) contact.Stop();
            AdvanceHorn(seconds, now, timeRunning, continuous, elapsed);
            if (timeRunning && continuous && port.ShorePresentationActive && Audible(contact, port.Trolley.position))
            {
                CityPortCycleSnapshot before = CityPortCycle.Sample(previousSeconds);
                if (before.CycleIndex == now.CycleIndex && before.CargoIndex == now.CargoIndex &&
                    before.Stage == CityPortCycleStage.Unload && now.Stage == CityPortCycleStage.Unload &&
                    before.SecondsInCargo < CityPortCycle.LandedAtSeconds &&
                    now.SecondsInCargo >= CityPortCycle.LandedAtSeconds)
                {
                    contact.transform.position = port.Cargo[now.CargoIndex].position;
                    contact.Play();
                    ContactsPlayed++;
                }
            }
            previousSeconds = seconds;
            hasPrevious = true;
            if (!timeRunning) return;

            float engine = now.VesselPresent ?
                now.Stage == CityPortCycleStage.Approach || now.Stage == CityPortCycleStage.Depart ? .30f : .12f : 0f;
            SetLoop(0, engine, delta);
            bool craneMoves = now.CargoStage == CityPortCargoStage.LowerHook ||
                now.CargoStage == CityPortCargoStage.Hoist || now.CargoStage == CityPortCargoStage.Slew ||
                now.CargoStage == CityPortCargoStage.LowerLoad || now.CargoStage == CityPortCargoStage.Return ||
                (now.CargoStage == CityPortCargoStage.Unhook && now.SecondsInCargo >= 26d);
            float movement = Mathf.Sin(Mathf.Clamp01(now.CargoStageProgress) * Mathf.PI);
            for (int i = 0; i < 2; i++)
                SetLoop(i + 1, now.ActiveCraneIndex == i && craneMoves ? .24f * movement : 0f, delta);
            bool cartMoves = now.CargoStage == CityPortCargoStage.Trolley || now.CargoStage == CityPortCargoStage.Return;
            SetLoop(3, cartMoves ? .16f * movement : 0f, delta);
        }

        private void SyncAnchors()
        {
            loops[0].transform.position = engineAnchor.position;
            horn.transform.position = hornAnchor.position;
            for (int i = 0; i < 2; i++) loops[i + 1].transform.position = driveAnchors[i].position;
            loops[3].transform.position = port.Trolley.position + Vector3.up * .2f;
        }

        private void AdvanceHorn(double seconds, CityPortCycleSnapshot now, bool running, bool continuous, double elapsed)
        {
            bool audible = port.VesselPresentationActive && now.VesselPresent && Audible(horn, hornAnchor.position);
            if (!audible || elapsed < 0d || elapsed > .35d || elapsed > 0d && hornSignalActive && !paused && !horn.isPlaying) StopHorn();
            if (running && continuous && audible)
            {
                double start = now.CycleIndex * CityPortCycle.CycleDurationSeconds;
                bool arrival = now.CycleIndex > lastArrivalHornCycle &&
                    previousSeconds < start + ArrivalHornAtSeconds && seconds >= start + ArrivalHornAtSeconds;
                bool departure = now.CycleIndex > lastDepartureHornCycle &&
                    previousSeconds < start + DepartureHornAtSeconds && seconds >= start + DepartureHornAtSeconds;
                if (arrival || departure)
                {
                    // One restrained low ship blast at each manoeuvre, from
                    // the actual roof horn; never an ambient repeating alarm.
                    hornEcho.enabled = hornReverb.enabled = true;
                    horn.Play();
                    hornSignalActive = true;
                    HornsPlayed++;
                }
            }
            RememberHornWindows(seconds);
        }

        private void StopHorn()
        {
            if (horn != null) horn.Stop();
            if (hornEcho != null) hornEcho.enabled = false;
            if (hornReverb != null) hornReverb.enabled = false;
            hornSignalActive = false;
        }

        private void RememberHornWindows(double seconds)
        {
            CityPortCycleSnapshot sample = CityPortCycle.Sample(seconds);
            double local = seconds - sample.CycleIndex * CityPortCycle.CycleDurationSeconds;
            // Crossed windows are consumed even while inaudible or seeking.
            // Rewinding, returning to the port or rebuilding cannot replay one.
            lastArrivalHornCycle = Math.Max(lastArrivalHornCycle,
                sample.CycleIndex - (local >= ArrivalHornAtSeconds ? 0 : 1));
            lastDepartureHornCycle = Math.Max(lastDepartureHornCycle,
                sample.CycleIndex - (local >= DepartureHornAtSeconds ? 0 : 1));
        }

        private void SetLoop(int index, float target, float delta)
        {
            bool visible = index == 0 ? port.VesselPresentationActive : port.ShorePresentationActive;
            if (!visible || !Audible(loops[index], loops[index].transform.position))
            {
                loops[index].Stop(); loops[index].volume = 0f;
                gain[index] = 0f; started[index] = false;
                return;
            }
            gain[index] = Mathf.MoveTowards(gain[index], target, delta * .6f);
            loops[index].volume = gain[index];
            if (gain[index] > .0001f && !started[index])
            {
                loops[index].Play();
                started[index] = true;
            }
            else if (gain[index] <= .0001f && started[index])
            {
                loops[index].Stop();
                started[index] = false;
            }
        }

        private bool Audible(AudioSource source, Vector3 position)
        {
            if (port.ForcePresentation || port.PresentationObserver == null) return true;
            float radius = source.maxDistance + (source.isPlaying ? 8f : 4f);
            return (port.PresentationObserver.position - position).sqrMagnitude < radius * radius;
        }

        private void SetPaused(bool value)
        {
            if (paused == value) return;
            paused = value;
            for (int i = 0; i < loops.Length; i++)
            {
                if (!started[i]) continue;
                if (value) loops[i].Pause(); else loops[i].UnPause();
            }
            if (value) contact.Pause(); else contact.UnPause();
            if (value)
            {
                horn.Pause();
                hornEcho.enabled = hornReverb.enabled = false;
            }
            else if (hornSignalActive)
            {
                hornEcho.enabled = hornReverb.enabled = true;
                horn.UnPause();
            }
        }

        private AudioSource CreateVoice(string name, bool loop, float cutoff)
        {
            var host = new GameObject(name);
            host.transform.SetParent(transform, false);
            var source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.volume = 0f;
            CityWorkAudio.Configure(source, false, cutoff, 32f);
            return source;
        }

        private void OnDisable()
        {
            for (int i = 0; i < loops.Length; i++)
            {
                if (loops[i] != null) { loops[i].Stop(); loops[i].volume = 0f; }
                started[i] = false;
                gain[i] = 0f;
            }
            if (contact != null) contact.Stop();
            StopHorn();
            hasPrevious = false;
            paused = false;
        }

        private void OnDestroy()
        {
            OnDisable();
            foreach (AudioSource source in loops) if (source != null) DestroyOwned(source.gameObject);
            if (contact != null) DestroyOwned(contact.gameObject);
            if (horn != null) DestroyOwned(horn.gameObject);
            DestroyOwned(hornClip);
            foreach (AudioClip clip in clips) DestroyOwned(clip);
            IsInitialized = false;
        }

        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
