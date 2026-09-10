using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityCanneryController
    {
        private readonly AudioSource[] voices = new AudioSource[3];
        private readonly AudioClip[] soundClips = new AudioClip[3];
        public AudioSource TruckEngineSource => voices[0];
        public AudioSource SeamerSource => voices[1];
        public AudioSource RetortSource => voices[2];

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
                source.pitch = i == 0 ? .74f : i == 1 ? 1.08f : .45f;
                CityWorkAudio.Configure(source, i != 0, i == 2 ? 2800f : i == 1 ? 6500f : 4000f,
                    i == 0 ? 32f : 24f);
                voices[i] = source;
            }
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
                source.transform.position = i == 0 ? Truck.TransformPoint(new Vector3(0, 1.3f, 4.6f)) :
                    i == 1 ? seamer.position : Plan.World(new Vector3(-5, 1.35f, 1.7f));
                bool near = ForcePresentation || hero == null || (hero.position - source.transform.position).sqrMagnitude <
                    Mathf.Pow(source.maxDistance + (source.isPlaying ? 8f : 4f), 2);
                bool visible = i == 0 ? TruckPresentationActive : FactoryPresentationActive;
                bool active = timeRunning && visible && near && (i == 0 ? Snapshot.IsDriving && !IsBlocked :
                    i == 1 ? Production.Stage == CityCanneryProductionStage.Seal : Production.Stage == CityCanneryProductionStage.Heat);
                source.volume = active ? (i == 0 ? .24f : i == 1 ? .32f : .26f) : 0;
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
