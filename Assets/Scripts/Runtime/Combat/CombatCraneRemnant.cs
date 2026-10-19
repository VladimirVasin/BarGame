using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Six existing free cables, with light asynchronous wind and a quiet creaking suspension.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatCraneRemnant : MonoBehaviour
    {
        internal const float SettleSeconds = 3f;
        private const float WindCycleSeconds = 40f, AudibleDistance = 16f;
        private readonly Vector3[] starts = new Vector3[6], restEnds = new Vector3[6], ends = new Vector3[6], spread = new Vector3[6];
        private readonly Renderer[] visibilityRenderers = new Renderer[24];
        private Transform[,] pieces;
        private Vector3[,] scales;
        private CombatTestRoot owner;
        private Transform observer;
        private Vector3 centre;
        private AudioClip creak;
        private float elapsed, windClock, presentationRemaining, creakRemaining, observerDistanceSquared;
        private int creakSequence, segmentCount;
        private bool audioPaused;

        internal bool IsVisible { get; private set; }
        internal bool IsAnimating => IsVisible && enabled;
        internal AudioSource Source { get; private set; }
        internal Vector3 GetCableEnd(int index) => ends[index];

        internal void Initialize(Transform[,] cablePieces, Vector3[,] cableScales, CombatTestRoot warehouse)
        {
            pieces = cablePieces;
            scales = cableScales;
            segmentCount = pieces.GetLength(1);
            owner = warehouse;
            observer = owner != null && owner.CameraFollow != null ? owner.CameraFollow.Camera.transform : null;
            for (int index = 0; index < starts.Length; index++)
                for (int segment = 0; segment < segmentCount; segment++)
                    visibilityRenderers[index * segmentCount + segment] = pieces[index, segment].GetComponentInChildren<Renderer>();
            enabled = false;
        }

        internal void Show(Transform[] feeds, Transform[] attachments, bool animate)
        {
            if (IsVisible) return;
            centre = Vector3.zero;
            for (int index = 0; index < starts.Length; index++)
            {
                starts[index] = feeds[index].position;
                centre += starts[index];
            }
            centre /= starts.Length;
            for (int index = 0; index < starts.Length; index++)
            {
                Vector3 outward = Vector3.ProjectOnPlane(starts[index] - centre, Vector3.up);
                if (outward.sqrMagnitude < .0001f)
                    outward = Quaternion.AngleAxis(index * 60f, Vector3.up) * Vector3.right;
                spread[index] = outward.normalized;
                // Retain the achieved payout with small unequal losses at the free
                // ends; using vertical clearance keeps them above the former feet.
                float length = Mathf.Max(.45f, starts[index].y - attachments[index].position.y) * (.92f + index % 3 * .025f);
                restEnds[index] = starts[index] + Vector3.down * length + spread[index] * (.09f + index * .012f);
            }
            IsVisible = true;
            elapsed = animate ? 0f : SettleSeconds;
            windClock = 0f;
            presentationRemaining = 0f;
            creakRemaining = 2.3f;
            creakSequence = 0;
            EnsureAudio();
            Source.transform.position = centre;
            enabled = true;
            Sample();
        }

        internal void Clear()
        {
            IsVisible = false;
            elapsed = windClock = presentationRemaining = 0f;
            StopAudio();
            enabled = false;
        }

        internal Vector3 GetPoint(int index, float fraction)
            => Vector3.Lerp(starts[index], ends[index], fraction) +
               spread[index] * (Mathf.Sin(fraction * Mathf.PI) * .035f);

        internal void Advance(float seconds)
        {
            if (!float.IsFinite(seconds) || seconds < 0f) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (!IsVisible || !enabled) return;
            if (owner != null && !owner.isActiveAndEnabled)
            { StopAudio(); return; }
            if (GameTimeScaleRuntime.IsPaused || !GameInput.CanRead(GameInputContext.Gameplay) || !GameInput.MovementFocused)
            { SetAudioPaused(true); return; }
            SetAudioPaused(false);
            elapsed = Mathf.Min(SettleSeconds, elapsed + seconds);
            // Culling changes only presentation cadence. The wind and cue clock
            // keep advancing, so a returning view never resumes an old pose.
            windClock = Mathf.Repeat(windClock + seconds, WindCycleSeconds);
            presentationRemaining -= seconds;
            if (presentationRemaining <= 0f)
            {
                observerDistanceSquared = observer != null ? (observer.position - centre).sqrMagnitude : 0f;
                bool visible = false;
                for (int index = 0; index < visibilityRenderers.Length; index++)
                    if (visibilityRenderers[index] != null && visibilityRenderers[index].isVisible) { visible = true; break; }
                presentationRemaining = observerDistanceSquared > 36f * 36f ? .5f :
                    observerDistanceSquared > 18f * 18f || !visible ? .2f : .05f;
                if (observerDistanceSquared <= 36f * 36f || visible) Sample();
                if (observerDistanceSquared > AudibleDistance * AudibleDistance) StopAudio();
            }
            creakRemaining -= seconds;
            if (creakRemaining <= 0f)
            {
                creakSequence = (creakSequence + 1) % 4;
                creakRemaining = 5.7f + creakSequence * 1.13f;
                if (observerDistanceSquared <= AudibleDistance * AudibleDistance && !Source.isPlaying)
                {
                    Source.pitch = .88f + creakSequence * .035f;
                    Source.Play();
                }
            }
        }

        private void Update() => Advance(Time.deltaTime);

        private void EnsureAudio()
        {
            if (Source != null) return;
            var voice = new GameObject("Crane cable creak");
            voice.transform.SetParent(transform, false);
            Source = voice.AddComponent<AudioSource>();
            Source.playOnAwake = false;
            Source.loop = false;
            Source.volume = .13f;
            CityWorkAudio.Configure(Source, true, 3600f, AudibleDistance, false);
            // A dry pivot/chain creak, rather than the active crane's motor.
            creak = CitySourceSoundSynthesis.CreateRuntimeClip(CitySourceSoundId.ParkSwingCreak, 1);
            Source.clip = creak;
        }

        private void SetAudioPaused(bool paused)
        {
            if (Source == null || audioPaused == paused) return;
            if (paused) Source.Pause(); else Source.UnPause();
            audioPaused = paused;
        }

        private void StopAudio()
        {
            if (Source != null) Source.Stop();
            audioPaused = false;
        }

        private void OnDisable() => StopAudio();
        private void OnDestroy()
        {
            StopAudio();
            if (Source != null) Source.clip = null;
            if (creak != null) Destroy(creak);
        }

        private void Sample()
        {
            float decay = 1f - elapsed / SettleSeconds;
            decay *= decay;
            float phase = windClock * (Mathf.PI * 2f / WindCycleSeconds);
            for (int index = 0; index < starts.Length; index++)
            {
                Vector3 tangent = Vector3.Cross(Vector3.up, spread[index]);
                ends[index] = restEnds[index] +
                    tangent * (Mathf.Sin(phase * (3 + index % 3) + index * .7f) * .022f +
                        Mathf.Sin(elapsed * (5.6f + index * .19f) + index * .7f) * .06f * decay) +
                    spread[index] * (Mathf.Sin(phase * (2 + index % 2) + index * .43f) * .014f);
                for (int segment = 0; segment < segmentCount; segment++)
                {
                    Vector3 from = GetPoint(index, segment / (float)segmentCount);
                    Vector3 to = GetPoint(index, (segment + 1f) / segmentCount);
                    Transform piece = pieces[index, segment];
                    piece.SetPositionAndRotation(from, Quaternion.FromToRotation(Vector3.up, to - from));
                    Vector3 scale = scales[index, segment];
                    scale.y *= Vector3.Distance(from, to);
                    piece.localScale = scale;
                    if (!piece.gameObject.activeSelf) piece.gameObject.SetActive(true);
                }
            }
        }
    }
}
