using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Three bounded, causal world voices for the lock, falling chain and moving rusty drive.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(320)]
    public sealed class WarehouseGateAudio : MonoBehaviour
    {
        private const int SampleRate = 24000;
        private const float TurningVolume = .40f, FadeSeconds = .12f, ContactCooldown = .09f;
        private static AudioClip keyTurnClip, unlockClip, releaseClip, turningClip;
        private static readonly AudioClip[] impactClips = new AudioClip[3];
        private static int leases;
        private Func<bool> suspended;
        private bool initialized, leased, turning, manuallySuspended, paused, contactsEnabled, contactPending;
        private Vector3 contactPoint;
        private float contactImpulse;
        private double clock, lastContact = double.NegativeInfinity;
        private int impactVariant;

        internal AudioSource CreakSource { get; private set; }
        internal AudioSource ActionSource { get; private set; }
        internal AudioSource ContactSource { get; private set; }
        public int KeyTurnCount { get; private set; }
        public int UnlockClickCount { get; private set; }
        public int ChainReleaseCount { get; private set; }
        public int ImpactCount { get; private set; }
        public int CreakStartCount { get; private set; }
        public bool IsPaused => paused;

        public void Initialize(Transform mechanism, Transform contactParent, Func<bool> suspended = null)
        {
            if (initialized) return;
            if (mechanism == null || contactParent == null) throw new ArgumentNullException();
            this.suspended = suspended;
            try
            {
                AcquireClips();
                leased = true;
                CreakSource = CreateVoice(mechanism, "Warehouse valve strain", 3500f, TurningVolume);
                CreakSource.loop = true;
                CreakSource.clip = turningClip;
                CreakSource.volume = 0f;
                ActionSource = CreateVoice(contactParent, "Warehouse lock and chain release", 5200f, .65f);
                // Contacts stay at their last world point, independent of the falling lock's parent.
                ContactSource = CreateVoice(transform, "Warehouse chain contacts", 4700f, .5f);
                initialized = true;
            }
            catch
            {
                ClearVoices();
                if (leased) { leased = false; ReleaseClips(); }
                throw;
            }
        }

        private static AudioSource CreateVoice(Transform parent, string name, float cutoff, float volume)
        {
            var host = new GameObject(name);
            host.transform.SetParent(parent, false);
            AudioSource source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.volume = volume;
            CityWorkAudio.Configure(source, true, cutoff, 13f, false);
            source.priority = 80;
            source.minDistance = .65f;
            return source;
        }

        public void PlayKeyTurn()
        {
            if (!CanEmit) return;
            PlayAction(keyTurnClip, .48f);
            KeyTurnCount++;
        }

        public void PlayUnlock()
        {
            if (!CanEmit) return;
            PlayAction(unlockClip, .68f);
            UnlockClickCount++;
        }

        public void PlayChainRelease()
        {
            contactsEnabled = initialized;
            if (!CanEmit) return;
            PlayAction(releaseClip, .62f);
            ChainReleaseCount++;
        }

        private void PlayAction(AudioClip clip, float volume)
        {
            ActionSource.Stop();
            ActionSource.clip = clip;
            ActionSource.volume = volume;
            ActionSource.Play();
        }

        /// <summary>Call with actual drive movement, including false on release or a terminal stop.</summary>
        public void SetTurning(bool moving)
        {
            turning = initialized && moving;
            if (turning && CanEmit && !CreakSource.isPlaying)
            {
                CreakSource.Play();
                CreakStartCount++;
            }
        }

        public void ReportChainContact(Vector3 point, float impulse)
        {
            if (!contactsEnabled || !CanEmit || !float.IsFinite(impulse) || impulse <= .025f ||
                !float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z)) return;
            impulse = Mathf.Min(impulse, 4f);
            if (!contactPending || impulse > contactImpulse)
            {
                contactPoint = point;
                contactImpulse = impulse;
                contactPending = true;
            }
        }

        public void Advance(float seconds)
        {
            if (!float.IsFinite(seconds) || seconds < 0f) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (!initialized) return;
            if (suspended?.Invoke() ?? false) { Stop(); return; }
            SetPaused(manuallySuspended || RuntimePaused);
            if (paused) return;
            clock += seconds;
            float target = turning ? TurningVolume : 0f;
            CreakSource.volume = Mathf.MoveTowards(CreakSource.volume, target, TurningVolume * seconds / FadeSeconds);
            if (turning && !CreakSource.isPlaying)
            {
                CreakSource.Play();
                CreakStartCount++;
            }
            else if (!turning && CreakSource.volume <= 0f) CreakSource.Stop();
            if (contactPending && clock - lastContact >= ContactCooldown)
            {
                lastContact = clock;
                ContactSource.Stop();
                ContactSource.transform.position = contactPoint;
                ContactSource.clip = impactClips[impactVariant];
                ContactSource.volume = Mathf.Lerp(.16f, .68f, Mathf.Clamp01(contactImpulse / .8f));
                ContactSource.pitch = .94f + impactVariant * .055f;
                ContactSource.Play();
                impactVariant = (impactVariant + 1) % impactClips.Length;
                ImpactCount++;
                contactPending = false;
            }
        }

        public void Suspend(bool value)
        {
            manuallySuspended = value;
            if (initialized) SetPaused(value || RuntimePaused);
        }

        private static bool RuntimePaused => GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused ||
            SceneTransitionService.IsTransitioning || !GameInput.MovementFocused || !GameInput.CanRead(GameInputContext.Menu);
        private bool CanEmit => initialized && isActiveAndEnabled && !paused && !manuallySuspended &&
            !RuntimePaused && !(suspended?.Invoke() ?? false);

        private void SetPaused(bool value)
        {
            if (paused == value) return;
            paused = value;
            if (value)
            {
                contactPending = false;
                CreakSource.Pause(); ActionSource.Pause(); ContactSource.Pause();
            }
            else { CreakSource.UnPause(); ActionSource.UnPause(); ContactSource.UnPause(); }
        }

        /// <summary>Leaving inspection can stop lock handling while the world chain finishes landing.</summary>
        public void StopActions() { if (ActionSource != null) ActionSource.Stop(); }

        public void Stop()
        {
            turning = manuallySuspended = paused = contactPending = contactsEnabled = false;
            if (CreakSource != null) { CreakSource.Stop(); CreakSource.volume = 0f; }
            if (ActionSource != null) ActionSource.Stop();
            if (ContactSource != null) ContactSource.Stop();
        }

        public void Reset()
        {
            Stop();
            clock = 0d; lastContact = double.NegativeInfinity; impactVariant = 0;
            KeyTurnCount = UnlockClickCount = ChainReleaseCount = ImpactCount = CreakStartCount = 0;
        }

        private void Update() => Advance(Time.unscaledDeltaTime);
        private void OnDisable() => Stop();
        private void OnDestroy()
        {
            Stop(); ClearVoices();
            if (leased) { leased = false; ReleaseClips(); }
        }

        private void ClearVoices()
        {
            DestroyVoice(CreakSource); DestroyVoice(ActionSource); DestroyVoice(ContactSource);
            CreakSource = ActionSource = ContactSource = null;
        }

        private static void DestroyVoice(AudioSource source)
        {
            if (source == null) return;
            source.Stop(); source.clip = null;
            Destroy(source.gameObject);
        }

        private static void AcquireClips()
        {
            if (leases == 0)
            {
                try
                {
                    keyTurnClip = MakeClip("Warehouse key in rusty lock", .22f, 0);
                    unlockClip = MakeClip("Warehouse shackle release", .30f, 1);
                    releaseClip = MakeClip("Warehouse chain coming loose", .64f, 2);
                    turningClip = MakeClip("Warehouse rusty valve strain", 4f, 4);
                    for (int index = 0; index < impactClips.Length; index++)
                        impactClips[index] = MakeClip("Warehouse chain contact " + (index + 1), .24f + index * .035f, 3, index);
                }
                catch { DestroyClips(); throw; }
            }
            leases++;
        }

        private static void ReleaseClips()
        {
            if (leases > 0 && --leases == 0) DestroyClips();
        }

        private static void DestroyClips()
        {
            if (keyTurnClip != null) Destroy(keyTurnClip);
            if (unlockClip != null) Destroy(unlockClip);
            if (releaseClip != null) Destroy(releaseClip);
            if (turningClip != null) Destroy(turningClip);
            keyTurnClip = unlockClip = releaseClip = turningClip = null;
            for (int index = 0; index < impactClips.Length; index++)
            { if (impactClips[index] != null) Destroy(impactClips[index]); impactClips[index] = null; }
        }

        private static AudioClip MakeClip(string name, float duration, int kind, int variant = 0)
        {
            var samples = new float[Mathf.CeilToInt(duration * SampleRate)];
            uint random = 0x47617465u + (uint)(kind * 997 + variant * 541);
            float lowNoise = 0f;
            for (int index = 0; index < samples.Length; index++)
            {
                float time = index / (float)SampleRate;
                random = unchecked(random * 1664525u + 1013904223u);
                float noise = (random >> 8) * (2f / 16777215f) - 1f;
                lowNoise += (noise - lowNoise) * .065f;
                float sample;
                if (kind == 4)
                {
                    // Integer oscillator cycles and periodic modulation retain both
                    // buffer endpoints. There is no repeated one-shot seam or voice.
                    double phase = index / (double)(samples.Length - 1) * Math.PI * 2d;
                    double slip = .68d + .18d * Math.Sin(phase * 5d) + .12d * Math.Sin(phase * 11d + .7d);
                    double groan = Math.Sin(phase * 910d + .9d * Math.Sin(phase * 2d) + .45d * Math.Sin(phase * 5d));
                    double shell = Math.Sin(phase * 1663d + .4d * Math.Sin(phase * 11d));
                    double weight = Math.Sin(phase * 237d + .5d * Math.Sin(phase * 3d));
                    double rub = Math.Sin(phase * 3491d + .6d * Math.Sin(phase * 97d)) +
                        .45d * Math.Sin(phase * 5237d + .3d * Math.Sin(phase * 41d));
                    sample = (float)((groan * .31d + shell * .13d + weight * .16d + rub * .055d) * slip);
                }
                else
                {
                    float attack = Mathf.Clamp01(time / .0025f);
                    if (kind == 0)
                    {
                        float rub = noise * (.24f + .16f * Mathf.Abs(Mathf.Sin(time * 173f))) * Mathf.Exp(-time * 17f);
                        sample = rub + MetalRing(time, 1390f) * .22f * Mathf.Exp(-time * 35f);
                    }
                    else if (kind == 1)
                    {
                        sample = (noise * .36f + MetalRing(time, 730f) * .35f) * Mathf.Exp(-time * 24f);
                        float second = Mathf.Max(0f, time - .075f);
                        if (time >= .075f) sample += MetalRing(second, 1170f) * .23f * Mathf.Exp(-second * 34f);
                    }
                    else if (kind == 2)
                    {
                        sample = (noise - lowNoise) * .13f * Mathf.Exp(-time * 5f);
                        for (int hit = 0; hit < 4; hit++)
                        {
                            float since = time - (hit * .087f + hit * hit * .012f);
                            if (since >= 0f) sample += (MetalRing(since, 430f + hit * 113f) * .29f + noise * .15f) *
                                Mathf.Exp(-since * (22f + hit * 4f)) * (1f - hit * .16f);
                        }
                    }
                    else sample = (MetalRing(time, 390f + variant * 91f) * .50f + noise * .24f) * Mathf.Exp(-time * (19f + variant * 3f));
                    sample *= attack * Mathf.Clamp01((duration - time) / .012f);
                }
                samples[index] = Mathf.Clamp(sample, -.88f, .88f);
            }
            if (kind == 4) samples[samples.Length - 1] = samples[0];
            AudioClip clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
            clip.hideFlags = HideFlags.DontSave;
            clip.SetData(samples, 0);
            return clip;
        }

        private static float MetalRing(float time, float frequency)
            => Mathf.Sin(time * Mathf.PI * 2f * frequency) +
               .36f * Mathf.Sin(time * Mathf.PI * 2f * frequency * 2.63f) +
               .17f * Mathf.Sin(time * Mathf.PI * 2f * frequency * 4.59f);
    }

    /// <summary>Reports real contacts to one shared voice; it never owns an audio source.</summary>
    public sealed class WarehouseGateChainContact : MonoBehaviour
    {
        private WarehouseGateAudio sounds;
        public void Initialize(WarehouseGateAudio audio) => sounds = audio;
        private void OnCollisionEnter(Collision collision)
        {
            if (sounds == null || collision.contactCount == 0) return;
            sounds.ReportChainContact(collision.GetContact(0).point, collision.impulse.magnitude);
        }
    }
}
