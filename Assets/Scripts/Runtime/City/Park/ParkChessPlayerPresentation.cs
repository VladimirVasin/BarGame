using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    /// <summary>
    /// Drives the park chess player: one authored ChessBrood loop —
    /// three slow breaths and a deeper settle — through a small manual
    /// PlayableGraph, exactly like the watchman at his window and the
    /// fisherman on his boards. The authored trudge stays in the shared
    /// library for a later pass.
    ///
    /// Over that loop sits one authored beat, ChessJeer: the head comes
    /// out of the hands, turns across the set and the left arm goes up.
    /// It is a second input on a mixer rather than a graph swap, so the
    /// brooding never stops underneath — he goes back into his palms on
    /// the same breath he left them.
    ///
    /// The one thing this presentation does that the other stationary
    /// drivers do not is seat him. He is the first staged design whose
    /// idle is seated on world furniture, and neither of the existing
    /// grounding rules fits: pinning his lowest sole would drag him
    /// down until he stood on the lawn, and the bus cabin's rule aligns
    /// a pelvis bone to a cushion anchor that a park bench does not
    /// have. So the model is lifted until the underside of his hips
    /// rests on the plank that was actually drawn, and his boots fall
    /// wherever the lawn under the bench happens to be. That matters
    /// here specifically: the chess set beds its feet into a slope, and
    /// the seat plank is the one part of it held at a constant height.
    ///
    /// The presentation stays passive. Nothing here reads input, and
    /// nothing here decides when to shout — the quarrel controller owns
    /// the turn and calls <see cref="BeginTaunt"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ParkChessPlayerPresentation : MonoBehaviour
    {
        /// <summary>A hitch longer than this advances the loop by a
        /// bounded step instead of teleporting mid-breath.</summary>
        public const float MaximumStepSeconds = 0.1f;

        /// <summary>
        /// How far the pelvis bone rides above the underside of the
        /// seated hips. Measured by the art build rather than guessed —
        /// it is printed as `pelvis lift` by the `perch_seat_height_m`
        /// validator for ChessBrood, against the real deformed meshes —
        /// and it is what turns "his pelvis is somewhere near the seat"
        /// into "the seat of his coat is on the timber".
        /// </summary>
        public const float PerchPelvisLiftMeters = 0.0651f;

        /// <summary>
        /// The shout takes the pose fast, because a taunt that eases in
        /// is not a taunt. It leaves slower: the arm has already fallen
        /// inside the clip, so the tail only has to hide the couple of
        /// degrees of chest the breathing loop has moved on by.
        /// </summary>
        public const float TauntBlendInSeconds = 0.10f;
        public const float TauntBlendOutSeconds = 0.18f;

        private CityPedestrianAssetRegistry registry;
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private AnimationClipPlayable broodPlayable;
        private AnimationClipPlayable tauntPlayable;
        private Vector3 modelBaseLocalPosition;
        private float seatPelvisY;
        private float playbackSpeed = 1f;
        private float tauntClipLength;
        private float tauntElapsed;
        private bool hasTauntClip;
        private bool tauntHeld;
        private bool hasGraph;

        public bool IsInitialized { get; private set; }
        public AnimationClip ActiveClip { get; private set; }

        /// <summary>The authored shout, or <c>null</c> on a prefab built
        /// before the quarrel existed. Without it he simply broods.</summary>
        public AnimationClip TauntClip { get; private set; }

        /// <summary>
        /// The height the pelvis bone is held at, which is the drawn
        /// plank plus the measured lift. Where he sits across the plank
        /// is the factory's business, not this component's.
        /// </summary>
        public float SeatPelvisY => seatPelvisY;

        /// <summary>True from <see cref="BeginTaunt"/> until the shout
        /// has fully blended back into the brooding loop.</summary>
        public bool IsTaunting => hasTauntClip &&
            tauntElapsed < tauntClipLength + TauntBlendOutSeconds;

        /// <summary>
        /// Normalized time of the shout clip, or <c>-1</c> when he is
        /// not shouting. Anything that has to land on a moment of the
        /// beat reads this rather than starting a second clock — the
        /// fisherman's pipe smoke rule.
        /// </summary>
        public float TauntPhase => IsTaunting && tauntClipLength > 0f
            ? Mathf.Clamp01(tauntElapsed / tauntClipLength)
            : -1f;

        /// <summary>Where a line of his belongs on screen: the live head
        /// bone, so the bubble rides up with the throw.</summary>
        public Transform SpeechAnchor =>
            registry != null ? registry.HeadAnchor : null;

        public void Initialize(
            CityPedestrianAssetRegistry assetRegistry,
            ParkChessPlayerStance stance)
        {
            if (assetRegistry == null)
            {
                throw new ArgumentNullException(nameof(assetRegistry));
            }

            if (assetRegistry.Animator == null)
            {
                throw new InvalidOperationException(
                    "The chess player prefab has no Animator.");
            }

            if (assetRegistry.ModelRoot == null ||
                assetRegistry.PelvisAnchor == null)
            {
                throw new InvalidOperationException(
                    "The chess player prefab needs a model root and a " +
                    "pelvis anchor to be seated on its plank.");
            }

            // Idle slot carries the brooding loop — the same mapping the
            // art build declares.
            AnimationClip clip = assetRegistry.IdleClip;
            if (clip == null)
            {
                throw new InvalidOperationException(
                    "The chess player prefab is missing its brooding loop.");
            }

            registry = assetRegistry;
            ActiveClip = clip;
            TauntClip = assetRegistry.ActionClip;
            playbackSpeed = Mathf.Max(0.05f, stance.PlaybackSpeed);
            modelBaseLocalPosition = registry.ModelRoot.localPosition;
            registry.ApplyPaletteVariant(stance.PaletteVariant);

            seatPelvisY = stance.SeatTopCenter.y + PerchPelvisLiftMeters;

            hasTauntClip = TauntClip != null && TauntClip.length > 0f;
            tauntClipLength = hasTauntClip ? TauntClip.length : 0f;
            // Past the tail, so nothing is taunting on the first frame.
            tauntElapsed = tauntClipLength + TauntBlendOutSeconds;
            tauntHeld = true;

            graph = PlayableGraph.Create("Park Chess Player");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            broodPlayable = AnimationClipPlayable.Create(graph, clip);
            broodPlayable.SetApplyFootIK(false);
            broodPlayable.SetApplyPlayableIK(false);
            broodPlayable.SetTime(Mathf.Repeat(
                stance.PhaseOffsetSeconds,
                clip.length));

            mixer = AnimationMixerPlayable.Create(
                graph,
                hasTauntClip ? 2 : 1);
            graph.Connect(broodPlayable, 0, mixer, 0);
            mixer.SetInputWeight(0, 1f);
            if (hasTauntClip)
            {
                tauntPlayable = AnimationClipPlayable.Create(
                    graph,
                    TauntClip);
                tauntPlayable.SetApplyFootIK(false);
                tauntPlayable.SetApplyPlayableIK(false);
                tauntPlayable.SetTime(tauntClipLength);
                tauntPlayable.SetSpeed(0d);
                graph.Connect(tauntPlayable, 0, mixer, 1);
                mixer.SetInputWeight(1, 0f);
            }

            AnimationPlayableOutput
                .Create(graph, "Park Chess Player Pose",
                    registry.Animator)
                .SetSourcePlayable(mixer);
            graph.Play();
            Evaluate(0f);
            hasGraph = true;
            IsInitialized = true;
        }

        /// <summary>
        /// Throws the shout from its first frame. A call arriving while
        /// he is already mid-shout is ignored rather than queued: the
        /// turn is five seconds and the beat is two, so a second call
        /// inside one can only be a bug upstream.
        /// </summary>
        public bool BeginTaunt()
        {
            if (!hasGraph || !hasTauntClip || IsTaunting)
            {
                return false;
            }

            tauntElapsed = 0f;
            tauntHeld = false;
            tauntPlayable.SetTime(0d);
            tauntPlayable.SetSpeed(1d);
            return true;
        }

        /// <summary>
        /// Drops the shout immediately and returns to the loop — used
        /// when the hero walks out of earshot mid-line.
        /// </summary>
        public void CancelTaunt()
        {
            if (!hasGraph || !hasTauntClip)
            {
                return;
            }

            tauntElapsed = tauntClipLength + TauntBlendOutSeconds;
            HoldTauntAtEnd();
            mixer.SetInputWeight(1, 0f);
            mixer.SetInputWeight(0, 1f);
        }

        /// <summary>
        /// The blend of the shout over the brooding loop, as a pure
        /// function of how long ago it started. Zero at both ends, one
        /// across the whole authored beat: the clip is the performance,
        /// the blend only hides the seam.
        /// </summary>
        public static float ResolveTauntWeight(
            float elapsedSeconds,
            float clipLength)
        {
            if (clipLength <= 0f ||
                elapsedSeconds <= 0f ||
                float.IsNaN(elapsedSeconds) ||
                elapsedSeconds >= clipLength + TauntBlendOutSeconds)
            {
                return 0f;
            }

            float rise = Mathf.Clamp01(
                elapsedSeconds / TauntBlendInSeconds);
            float fall = Mathf.Clamp01(
                (clipLength + TauntBlendOutSeconds - elapsedSeconds) /
                TauntBlendOutSeconds);
            return Mathf.Min(rise, fall);
        }

        private void LateUpdate()
        {
            if (!hasGraph)
            {
                return;
            }

            Evaluate(Mathf.Min(Time.deltaTime, MaximumStepSeconds) *
                playbackSpeed);
        }

        private void Evaluate(float deltaTime)
        {
            if (!graph.IsValid())
            {
                return;
            }

            // The seat correction is applied to the model root, so the
            // root has to go back to its authored basis before the clip
            // is sampled or every frame would add to the last one.
            if (registry != null && registry.ModelRoot != null)
            {
                registry.ModelRoot.localPosition = modelBaseLocalPosition;
            }

            if (hasTauntClip)
            {
                tauntElapsed += deltaTime;
                // The clip is looping in the shared library, so once the
                // beat is spent it has to be pinned or it would wind up
                // again underneath the fading blend.
                if (!tauntHeld && tauntElapsed >= tauntClipLength)
                {
                    HoldTauntAtEnd();
                }

                float weight = ResolveTauntWeight(
                    tauntElapsed,
                    tauntClipLength);
                mixer.SetInputWeight(1, weight);
                mixer.SetInputWeight(0, 1f - weight);
            }

            graph.Evaluate(deltaTime);
            AlignHipsToPlank();
        }

        private void HoldTauntAtEnd()
        {
            tauntHeld = true;
            tauntPlayable.SetSpeed(0d);
            tauntPlayable.SetTime(tauntClipLength);
        }

        private void AlignHipsToPlank()
        {
            if (registry == null ||
                registry.ModelRoot == null ||
                registry.PelvisAnchor == null)
            {
                return;
            }

            // Only the vertical is corrected. The factory already put him
            // across the plank, and letting the clip's own forward lean
            // slide him over the timber would take his elbows off the
            // board rim they were fitted to.
            Vector3 moved = registry.ModelRoot.position;
            moved.y += seatPelvisY - registry.PelvisAnchor.position.y;
            registry.ModelRoot.position = moved;
        }

        private void OnDestroy()
        {
            if (hasGraph && graph.IsValid())
            {
                graph.Destroy();
            }

            hasGraph = false;
        }
    }
}
