using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Bounded physical voices. Seeking reconstructs state without replaying cargo impacts.</summary>
    [DefaultExecutionOrder(210)]
    public sealed class CityPortSound : MonoBehaviour
    {
        private CityPortController port;
        private Transform engineAnchor;
        private readonly AudioSource[] loops = new AudioSource[4];
        private readonly AudioClip[] clips = new AudioClip[4];
        private readonly bool[] started = new bool[4];
        private readonly float[] gain = new float[4];
        private AudioSource contact;
        private double previousSeconds;
        private bool hasPrevious, paused;
        public AudioSource EngineSource => loops[0];
        public AudioSource FirstCraneSource => loops[1];
        public AudioSource SecondCraneSource => loops[2];
        public AudioSource TrolleySource => loops[3];
        public AudioSource ContactSource => contact;
        public int ContactsPlayed { get; private set; }
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
            sound.clips[0] = CityOffshoreBoatSynthesis.CreateEngineClip(seed ^ 0x504F5254, 0);
            sound.clips[1] = CitySourceSoundSynthesis.CreateRuntimeClip(CitySourceSoundId.IndustrialWeighbridgeMechanismLoop, 0);
            sound.clips[2] = CitySourceSoundSynthesis.CreateRuntimeClip(CitySourceSoundId.IndustrialWeighbridgeMechanismLoop, 2);
            sound.clips[3] = CitySourceSoundSynthesis.CreateRuntimeClip(CitySourceSoundId.DryingYardCarpetStrike, 1);
            string[] names = { "Trawler Diesel", "West Crane Drive", "East Crane Drive", "Cargo Trolley Wheels" };
            for (int i = 0; i < sound.loops.Length; i++)
            {
                sound.loops[i] = sound.CreateVoice(names[i], true, GameAudioGroup.AmbienceBeds,
                    i == 0 ? 1700f : i == 3 ? 1300f : 2100f);
                int clipIndex = i == 0 ? 0 : i == 3 ? 2 : 1;
                sound.loops[i].clip = sound.clips[clipIndex];
                sound.loops[i].pitch = i == 3 ? .62f : i == 2 ? .92f : 1f;
            }
            sound.contact = sound.CreateVoice("Cargo Landing", false, GameAudioGroup.AmbienceDetails, 1900f);
            sound.contact.clip = sound.clips[3];
            sound.contact.pitch = .72f;
            sound.contact.volume = .12f;
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
            if (timeRunning && continuous)
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
                now.Stage == CityPortCycleStage.Approach || now.Stage == CityPortCycleStage.Depart ? .16f : .045f : 0f;
            SetLoop(0, engine, delta);
            bool craneMoves = now.CargoStage == CityPortCargoStage.LowerHook ||
                now.CargoStage == CityPortCargoStage.Hoist || now.CargoStage == CityPortCargoStage.Slew ||
                now.CargoStage == CityPortCargoStage.LowerLoad || now.CargoStage == CityPortCargoStage.Return ||
                (now.CargoStage == CityPortCargoStage.Unhook && now.SecondsInCargo >= 26d);
            float movement = Mathf.Sin(Mathf.Clamp01(now.CargoStageProgress) * Mathf.PI);
            for (int i = 0; i < 2; i++)
                SetLoop(i + 1, now.ActiveCraneIndex == i && craneMoves ? .085f * movement : 0f, delta);
            bool cartMoves = now.CargoStage == CityPortCargoStage.Trolley || now.CargoStage == CityPortCargoStage.Return;
            SetLoop(3, cartMoves ? .075f * movement : 0f, delta);
        }

        private void SyncAnchors()
        {
            loops[0].transform.position = engineAnchor.position;
            for (int i = 0; i < 2; i++) loops[i + 1].transform.position = port.CraneBases[i].position + Vector3.up * 2f;
            loops[3].transform.position = port.Trolley.position + Vector3.up * .2f;
        }

        private void SetLoop(int index, float target, float delta)
        {
            gain[index] = Mathf.MoveTowards(gain[index], target, delta * .22f);
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
        }

        private AudioSource CreateVoice(string name, bool loop, GameAudioGroup group, float cutoff)
        {
            var host = new GameObject(name);
            host.transform.SetParent(transform, false);
            var source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.volume = 0f;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.minDistance = 1.5f;
            source.maxDistance = 28f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.priority = 183;
            source.reverbZoneMix = .2f;
            GameAudioMixer.Route(source, group);
            var filter = host.AddComponent<AudioLowPassFilter>();
            filter.cutoffFrequency = cutoff;
            filter.lowpassResonanceQ = 1f;
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
            hasPrevious = false;
            paused = false;
        }

        private void OnDestroy()
        {
            OnDisable();
            foreach (AudioSource source in loops) if (source != null) DestroyOwned(source.gameObject);
            if (contact != null) DestroyOwned(contact.gameObject);
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
