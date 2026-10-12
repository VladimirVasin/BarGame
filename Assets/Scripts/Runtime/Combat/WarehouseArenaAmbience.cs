using UnityEngine;

namespace BarPromenade
{
    /// <summary>One physical ventilation owner, sharing the existing interior synthesis and world mixer.</summary>
    public sealed class WarehouseArenaAmbience : MonoBehaviour
    {
        private AudioSource source;
        private AudioClip clip;
        private bool paused;

        public void Initialize(Transform equipment)
        {
            var voice = new GameObject("Warehouse Ventilation"); voice.transform.SetParent(transform,false);
            voice.transform.position = equipment.TransformPoint(new Vector3(-4.8f,.68f,.42f));
            source = voice.AddComponent<AudioSource>(); source.loop = true; source.playOnAwake = false;
            CityWorkAudio.Configure(source,true,1700f,24f); source.volume = .085f;
            float[] samples = StairwellSoundscapeSynthesis.GenerateVentilationLoopSamples();
            clip = AudioClip.Create("Warehouse ventilation",samples.Length,1,StairwellSoundscapeSynthesis.SampleRate,false);
            clip.SetData(samples,0); source.clip = clip; source.Play();
        }

        private void Update()
        {
            if (source == null) return;
            if (GameTimeScaleRuntime.IsPaused && !paused) { source.Pause(); paused = true; }
            else if (!GameTimeScaleRuntime.IsPaused && paused) { source.UnPause(); paused = false; }
        }

        private void OnDisable() { if (source != null) source.Stop(); paused = false; }
        private void OnDestroy() { if (clip != null) Destroy(clip); }
    }
}
