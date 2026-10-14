using UnityEngine;

namespace BarPromenade
{
    /// <summary>One physical ventilation owner, sharing the existing interior synthesis and world mixer.</summary>
    public sealed class WarehouseArenaAmbience : MonoBehaviour
    {
        private AudioSource source;
        private AudioClip clip;
        private bool paused;
        private CombatTestRoot owner;

        public void Initialize(Transform equipment, CombatTestRoot warehouse)
        {
            owner = warehouse;
            var voice = new GameObject("Warehouse Ventilation"); voice.transform.SetParent(transform,false);
            voice.transform.position = equipment.position;
            source = voice.AddComponent<AudioSource>(); source.loop = true; source.playOnAwake = false;
            CityWorkAudio.Configure(source,true,1400f,32f,false); source.volume = .13f; source.pitch = .7f;
            float[] samples = StairwellSoundscapeSynthesis.GenerateVentilationLoopSamples();
            clip = AudioClip.Create("Warehouse ventilation",samples.Length,1,StairwellSoundscapeSynthesis.SampleRate,false);
            clip.SetData(samples,0); source.clip = clip; source.Play();
        }

        private void Update()
        {
            if (source == null) return;
            bool suspended = GameTimeScaleRuntime.IsPaused || owner == null || !owner.isActiveAndEnabled;
            if (suspended && !paused) { source.Pause(); paused = true; }
            else if (!suspended && paused) { source.UnPause(); paused = false; }
            // Native audio stops when the whole scene carrier is disabled.
            // Re-enabling that carrier must restore the retained ventilation loop.
            if (!suspended && !source.isPlaying) source.Play();
        }

        private void OnDisable() { if (source != null) source.Stop(); paused = false; }
        private void OnDestroy() { if (clip != null) Destroy(clip); }
    }
}
