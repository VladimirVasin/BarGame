using UnityEngine;

namespace BarPromenade
{
    /// <summary>One physical ventilation owner, sharing the existing interior synthesis and world mixer.</summary>
    public sealed class WarehouseArenaAmbience : MonoBehaviour
    {
        private AudioSource source;
        private AudioClip clip;
        private bool paused;
        private float doorAmbushOverdrive = -1f;
        private bool doorAmbushOverdriveSuspended;
        private CombatTestRoot owner;
        public AudioSource Source => source;
        public float PowerFactor { get; private set; } = 1f;

        public void Initialize(Transform equipment, CombatTestRoot warehouse, float powerFactor = 1f)
        {
            owner = warehouse;
            var voice = new GameObject("Warehouse Ventilation"); voice.transform.SetParent(transform,false);
            voice.transform.position = equipment.position;
            source = voice.AddComponent<AudioSource>(); source.loop = true; source.playOnAwake = false;
            CityWorkAudio.Configure(source,true,1400f,32f,false); source.volume = .13f; source.pitch = .7f;
            float[] samples = StairwellSoundscapeSynthesis.GenerateVentilationLoopSamples();
            clip = AudioClip.Create("Warehouse ventilation",samples.Length,1,StairwellSoundscapeSynthesis.SampleRate,false);
            clip.SetData(samples,0); source.clip = clip;
            SetPower(powerFactor);
            if (PowerFactor > 0f) source.Play();
        }

        public void SetPower(float factor)
        {
            PowerFactor = Mathf.Clamp01(factor);
            if (source == null) return;
            if (doorAmbushOverdrive >= 0f) { ApplyDoorAmbushOverdrive(); return; }
            source.volume = .13f * PowerFactor;
            source.pitch = .7f * Mathf.Lerp(.45f, 1f, PowerFactor);
            if (PowerFactor <= 0f) { source.Stop(); paused = false; }
        }

        internal void SetDoorAmbushOverdrive(float speed)
        {
            doorAmbushOverdrive = speed < 0f ? -1f : Mathf.Clamp01(speed);
            if (doorAmbushOverdrive < 0f) doorAmbushOverdriveSuspended = false;
            if (source == null) return;
            if (doorAmbushOverdrive < 0f) SetPower(PowerFactor);
            else ApplyDoorAmbushOverdrive();
        }

        internal void SetDoorAmbushOverdriveSuspended(bool value)
        {
            doorAmbushOverdriveSuspended = value;
            if (value && doorAmbushOverdrive >= 0f && source != null && !paused)
            { source.Pause(); paused = true; }
        }

        private void ApplyDoorAmbushOverdrive()
        {
            source.volume = Mathf.Lerp(.13f, .30f, doorAmbushOverdrive);
            source.pitch = Mathf.Lerp(.7f, 2.4f, doorAmbushOverdrive);
        }

        private void Update()
        {
            if (source == null || (PowerFactor <= 0f && doorAmbushOverdrive < 0f)) return;
            bool suspended = GameTimeScaleRuntime.IsPaused || owner == null || !owner.isActiveAndEnabled ||
                (doorAmbushOverdrive >= 0f && (doorAmbushOverdriveSuspended ||
                    !GameInput.MovementFocused || SceneTransitionService.IsTransitioning));
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
