using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class WarehouseArenaMechanisms
    {
        public const float DoorAmbushOverdriveMaximumSpeed = 720f;
        public const float DoorAmbushOverdriveRampSeconds = 5f;
        private const float DoorAmbushSparkPeriod = .13f;

        private struct FanVoiceState
        {
            public AudioClip Clip;
            public float Volume, Pitch;
            public int Sample;
            public bool Loop, Playing;
        }

        private struct FanOverdriveState
        {
            public bool Active;
            public float Speed, Angle, RampSeconds, SparkDelay, SoundDelay;
            public Quaternion Rotation, Origin;
            public FanVoiceState Voice, VentilationVoice;
        }

        private object doorAmbushOverdriveOwner;
        private bool doorAmbushOverdriveActive;
        private int doorAmbushAudioPauseState = -1;
        private float doorAmbushOverdriveSpeed, doorAmbushOverdriveAngle;
        private float doorAmbushOverdriveRampSeconds;
        private float doorAmbushSparkDelay, doorAmbushSparkSoundDelay;
        private Quaternion doorAmbushFanOrigin;
        private FanOverdriveState previousDoorAmbushFan, originalDoorAmbushFan;
        private CombatSparkEffects doorAmbushSparks;
        private Vector3 doorAmbushSparkOrigin;

        public bool DoorAmbushOverdriveActive => doorAmbushOverdriveActive;
        public float DoorAmbushOverdriveSpeed => doorAmbushOverdriveSpeed;
        public int DoorAmbushSparkEmissionCount => doorAmbushSparks != null ? doorAmbushSparks.EmissionCount : 0;
        public int DoorAmbushSparkCount => doorAmbushSparks != null ? doorAmbushSparks.ActiveParticleCount : 0;
        public Vector3 DoorAmbushSparkOrigin => doorAmbushSparkOrigin;

        private bool DoorAmbushOverdriveCanRun => isActiveAndEnabled && GameInput.MovementFocused &&
            !SceneTransitionService.IsTransitioning;

        private void InitializeDoorAmbushFan(GameObject housing)
        {
            // Use the imported motor's lower bearing seam, retaining the FBX
            // root's unit scale. This is a fault in the actual overhead drive.
            Renderer motor = null;
            foreach (Renderer candidate in housing.GetComponentsInChildren<Renderer>(true))
                if (candidate.name.StartsWith("Motor__", StringComparison.Ordinal))
                { motor = candidate; break; }
            if (motor == null) throw new InvalidOperationException("Warehouse fan lacks its authored motor.");
            Bounds bounds = motor.bounds;
            doorAmbushSparkOrigin = new Vector3(bounds.center.x + bounds.extents.x * .8f,
                bounds.min.y, bounds.center.z);
            doorAmbushSparks = gameObject.AddComponent<CombatSparkEffects>();
            doorAmbushSparks.Initialize(transform);
            InitializeDoorAmbushSparkLight();
        }

        /// <summary>Restart only the actual fan; completed fish/power/music and other drives stay committed.</summary>
        public bool BeginDoorAmbushOverdrive(object playbackOwner)
        {
            if (playbackOwner == null || !initialized || FanRotor == null || FanShutdownProgress < 1f) return false;
            if (doorAmbushOverdriveOwner != null)
                return ReferenceEquals(doorAmbushOverdriveOwner, playbackOwner);
            previousDoorAmbushFan = CaptureDoorAmbushFan();
            if (!doorAmbushOverdriveActive) originalDoorAmbushFan = previousDoorAmbushFan;
            doorAmbushOverdriveOwner = playbackOwner;
            doorAmbushAudioPauseState = -1;
            doorAmbushOverdriveActive = true;
            doorAmbushOverdriveSpeed = 0f;
            ApplyFanPower();
            doorAmbushOverdriveRampSeconds = 0f;
            doorAmbushOverdriveAngle = 0f;
            doorAmbushFanOrigin = FanRotor.localRotation;
            doorAmbushSparkDelay = DoorAmbushSparkPeriod;
            doorAmbushSparkSoundDelay = 0f;
            fanGrindPending = false;
            doorAmbushSparks.ResetRound();
            doorAmbushSparks.Emit(doorAmbushSparkOrigin, Vector3.down, Vector3.right);
            EmitDoorAmbushSparkLight();
            if (FanGrindSource != null)
            {
                // Reuse the existing spatial scrape voice and bank. The shared
                // ventilation voice supplies the accelerating motor underneath.
                FanGrindSource.Stop();
                FanGrindSource.clip = fanGrind != null ? fanGrind : contact;
                FanGrindSource.loop = true;
                FanGrindSource.volume = .38f;
                FanGrindSource.pitch = .55f;
            }
            ambience?.SetDoorAmbushOverdrive(0f);
            owner?.Ambience?.ApplyFanShutdown(0f);
            SynchronizeDoorAmbushAudio(Running && DoorAmbushOverdriveCanRun);
            return true;
        }

        /// <summary>Cancel restores the prior drive; a successful scene leaves this warehouse running.</summary>
        public void EndDoorAmbushOverdrive(object playbackOwner, bool keepRunning = false)
        {
            if (playbackOwner == null || !ReferenceEquals(doorAmbushOverdriveOwner, playbackOwner)) return;
            doorAmbushOverdriveOwner = null;
            if (keepRunning) return;
            RestoreDoorAmbushFan(previousDoorAmbushFan);
        }

        private FanOverdriveState CaptureDoorAmbushFan()
        {
            var state = new FanOverdriveState
            {
                Active = doorAmbushOverdriveActive, Speed = doorAmbushOverdriveSpeed,
                RampSeconds = doorAmbushOverdriveRampSeconds,
                Angle = doorAmbushOverdriveAngle, SparkDelay = doorAmbushSparkDelay,
                SoundDelay = doorAmbushSparkSoundDelay, Origin = doorAmbushFanOrigin,
                Rotation = FanRotor.localRotation
            };
            state.Voice = CaptureDoorAmbushVoice(FanGrindSource, doorAmbushOverdriveActive);
            state.VentilationVoice = CaptureDoorAmbushVoice(ambience != null ? ambience.Source : null,
                doorAmbushOverdriveActive || (ambience != null && ambience.PowerFactor > 0f));
            return state;
        }

        private static FanVoiceState CaptureDoorAmbushVoice(AudioSource source, bool retainedPlaying)
        {
            if (source == null) return default;
            return new FanVoiceState
            {
                Clip = source.clip, Volume = source.volume, Pitch = source.pitch,
                Sample = source.clip != null ? source.timeSamples : 0,
                Loop = source.loop, Playing = retainedPlaying || source.isPlaying
            };
        }

        private static void RestoreDoorAmbushVoice(AudioSource source, FanVoiceState state, bool running)
        {
            if (source == null) return;
            source.Stop();
            source.clip = state.Clip;
            source.loop = state.Loop;
            source.volume = state.Volume;
            source.pitch = state.Pitch;
            if (state.Clip != null && state.Clip.samples > 0)
                source.timeSamples = Mathf.Clamp(state.Sample, 0, state.Clip.samples - 1);
            if (state.Playing) source.Play();
            if (!running) source.Pause();
        }

        private void RestoreDoorAmbushFan(FanOverdriveState state)
        {
            doorAmbushOverdriveActive = state.Active;
            doorAmbushOverdriveSpeed = state.Speed;
            ApplyFanPower();
            doorAmbushOverdriveRampSeconds = state.RampSeconds;
            doorAmbushOverdriveAngle = state.Angle;
            doorAmbushSparkDelay = state.SparkDelay;
            doorAmbushSparkSoundDelay = state.SoundDelay;
            doorAmbushFanOrigin = state.Origin;
            if (FanRotor != null) FanRotor.localRotation = state.Rotation;
            doorAmbushSparks?.ResetRound();
            ClearDoorAmbushSparkLight();
            RetroAudioService.Instance?.ReleasePresentation(this);
            ambience?.SetDoorAmbushOverdrive(state.Active ? state.Speed / DoorAmbushOverdriveMaximumSpeed : -1f);
            owner?.Ambience?.ApplyFanShutdown(state.Active ? 0f : FanShutdownProgress);
            bool running = Running && (!state.Active || DoorAmbushOverdriveCanRun);
            RestoreDoorAmbushVoice(FanGrindSource, state.Voice, running);
            RestoreDoorAmbushVoice(ambience != null ? ambience.Source : null, state.VentilationVoice, running);
            if (state.Active) ambience?.SetDoorAmbushOverdriveSuspended(!running);
            doorAmbushAudioPauseState = state.Active ? (running ? 0 : 1) : -1;
            audioPaused = !running;
        }

        private void AdvanceDoorAmbushOverdrive(float seconds)
        {
            if (!doorAmbushOverdriveActive) return;
            // SmoothStep acceleration has zero slope at both ends. Integrate
            // its polynomial so frame size never changes the resulting angle.
            float previousRamp = doorAmbushOverdriveRampSeconds;
            doorAmbushOverdriveRampSeconds = Mathf.Min(DoorAmbushOverdriveRampSeconds, previousRamp + seconds);
            float ramp = doorAmbushOverdriveRampSeconds - previousRamp;
            float from = previousRamp / DoorAmbushOverdriveRampSeconds;
            float to = doorAmbushOverdriveRampSeconds / DoorAmbushOverdriveRampSeconds;
            float degrees = DoorAmbushOverdriveMaximumSpeed * (DoorAmbushOverdriveRampSeconds *
                (SmoothRampIntegral(to) - SmoothRampIntegral(from)) + seconds - ramp);
            doorAmbushOverdriveSpeed = DoorAmbushOverdriveMaximumSpeed * Mathf.SmoothStep(0f, 1f, to);
            ApplyFanPower();
            doorAmbushOverdriveAngle = Mathf.Repeat(doorAmbushOverdriveAngle + degrees, 360f);
            FanRotor.localRotation = doorAmbushFanOrigin * Quaternion.Euler(0f, doorAmbushOverdriveAngle, 0f);
            float speed = doorAmbushOverdriveSpeed / DoorAmbushOverdriveMaximumSpeed;
            ambience?.SetDoorAmbushOverdrive(speed);
            if (FanGrindSource != null) FanGrindSource.pitch = Mathf.Lerp(.55f, 2.4f, speed);
            doorAmbushSparks.Tick(seconds);
            AdvanceDoorAmbushSparkLight(seconds);
            doorAmbushSparkDelay -= seconds;
            doorAmbushSparkSoundDelay -= seconds;
            if (doorAmbushSparkDelay > 0f) return;
            // One bounded burst per live frame; never replay a backlog after a hitch.
            doorAmbushSparkDelay = DoorAmbushSparkPeriod;
            doorAmbushSparks.Emit(doorAmbushSparkOrigin, Vector3.down, Vector3.right);
            EmitDoorAmbushSparkLight();
            if (doorAmbushSparkSoundDelay > 0f) return;
            doorAmbushSparkSoundDelay = .39f;
            object previous = RetroAudio.SetPresentationCueOwner(this);
            try { RetroAudio.PlayAt(RetroSfxId.SpadeGlance, doorAmbushSparkOrigin, .22f); }
            finally { RetroAudio.SetPresentationCueOwner(previous); }
        }

        private static float SmoothRampIntegral(float value) => value * value * value * (1f - .5f * value);

        private void SynchronizeDoorAmbushAudio(bool running)
        {
            if (!doorAmbushOverdriveActive) return;
            SetDoorAmbushSparkLightPaused(!running);
            int pauseState = running ? 0 : 1;
            if (doorAmbushAudioPauseState != pauseState)
            {
                doorAmbushAudioPauseState = pauseState;
                ambience?.SetDoorAmbushOverdriveSuspended(!running);
                RetroAudioService.Instance?.SetPresentationPaused(this, !running);
                if (FanGrindSource != null)
                {
                    if (running) FanGrindSource.UnPause();
                    else FanGrindSource.Pause();
                }
            }
            if (FanGrindSource == null) return;
            if (running && !FanGrindSource.isPlaying) FanGrindSource.Play();
        }

        private void ResetDoorAmbushOverdrive()
        {
            if (!doorAmbushOverdriveActive) return;
            doorAmbushOverdriveOwner = null;
            RestoreDoorAmbushFan(originalDoorAmbushFan);
        }

        private void DisposeDoorAmbushOverdrive()
        {
            doorAmbushOverdriveOwner = null;
            doorAmbushOverdriveActive = false;
            ambience?.SetDoorAmbushOverdrive(-1f);
            RetroAudioService.Instance?.ReleasePresentation(this);
            doorAmbushSparks?.Clear();
            DisposeDoorAmbushSparkLight();
        }
    }
}
