using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityCanneryController
    {
        private readonly AudioSource[] voices = new AudioSource[3];
        private readonly AudioClip[] soundClips = new AudioClip[3];

        private void CreateSounds(int seed)
        {
            soundClips[0] = CityOffshoreBoatSynthesis.CreateEngineClip(seed ^ 0x43414E, 1);
            soundClips[1] = CitySourceSoundSynthesis.CreateRuntimeClip(CitySourceSoundId.IndustrialWeighbridgeMechanismLoop, 1);
            soundClips[2] = CitySourceSoundSynthesis.CreateRuntimeClip(CitySourceSoundId.IndustrialWeighbridgeMechanismLoop, 2);
            string[] names = { "Delivery truck engine", "Seamer motor", "Retort circulation" };
            for (int i = 0; i < voices.Length; i++)
            {
                var host = new GameObject(names[i]);
                host.transform.SetParent(transform, false);
                AudioSource source = host.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = true;
                source.clip = soundClips[i];
                source.volume = 0;
                source.spatialBlend = 1;
                source.dopplerLevel = 0;
                source.minDistance = 1.5f;
                source.maxDistance = 22;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.pitch = i == 0 ? .74f : i == 1 ? 1.08f : .45f;
                source.priority = 184;
                GameAudioMixer.Route(source, GameAudioGroup.AmbienceBeds);
                host.AddComponent<AudioLowPassFilter>().cutoffFrequency = i == 2 ? 650 : 1500;
                voices[i] = source;
            }
        }

        private void LateUpdate() { if (IsInitialized) ApplySounds(); }

        private void ApplySounds()
        {
            bool running = AutoAdvance && isActiveAndEnabled && GameSessionState.IsGameTimeRunning && !GameTimeScaleRuntime.IsPaused;
            for (int i = 0; i < voices.Length; i++)
            {
                AudioSource source = voices[i];
                if (source == null) continue;
                source.transform.position = i == 0 ? Truck.TransformPoint(new Vector3(0, 1.3f, 4.6f)) :
                    i == 1 ? seamer.position : retortDoor.position;
                bool active = running && (i == 0 ? Snapshot.IsDriving && !IsBlocked :
                    i == 1 ? Snapshot.Stage == CityFishSupplyStage.Seal : Snapshot.Stage == CityFishSupplyStage.Heat);
                source.volume = active ? (i == 0 ? .09f : .055f) : 0;
                if (active && !source.isPlaying) source.Play();
                else if (!active && source.isPlaying) source.Stop();
            }
        }

        private void OnDisable()
        {
            foreach (AudioSource source in voices) if (source != null) source.Stop();
        }

        private void DestroySounds()
        {
            foreach (AudioSource source in voices) if (source != null) source.Stop();
            foreach (AudioClip clip in soundClips)
            {
                if (clip == null) continue;
                if (Application.isPlaying) Destroy(clip); else DestroyImmediate(clip);
            }
        }
    }
}
