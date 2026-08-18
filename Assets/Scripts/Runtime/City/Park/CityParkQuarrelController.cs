using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The argument at the park chess set. Two old men who have been
    /// coming here long enough that both boards have gone empty, and who
    /// hate each other on the only grounds that were ever available: one
    /// plays chess and despises draughts, the other plays draughts and
    /// despises chess. Every ten seconds, in strict turn, one of them
    /// pulls his head out of his hands, throws it across the set and his
    /// left arm up with it, and says something. The line hangs for four
    /// seconds and then the park is quiet again until the answer.
    ///
    /// It runs only while the hero is near enough to hear it. That is
    /// not a performance saving — two mixers cost nothing — it is that a
    /// quarrel is a thing that happens at somebody. Held out of earshot
    /// it would just be two men shouting at an empty park, and the first
    /// line the hero ever caught would be a fragment.
    ///
    /// The bubble does not open when the shout is triggered. It opens
    /// when the authored clip reaches the frame the arm is fully out, so
    /// the words land on the gesture rather than a fifth of a second
    /// before it. That is the fisherman's rule — a contextual effect
    /// reads its host clip's phase, never its own timer.
    ///
    /// Owned by the City root and polled, like the weighbridge needle
    /// and the cemetery mourner; the two staged prefabs stay passive.
    /// </summary>
    [DefaultExecutionOrder(320)]
    [DisallowMultipleComponent]
    public sealed class CityParkQuarrelController : MonoBehaviour
    {
        public const string RuntimeObjectName = "Park Quarrel Controller";

        /// <summary>
        /// How close the hero has to be, measured from the middle of the
        /// set, before the two of them start. Well inside the City's
        /// fixed `48 m` far clip, so the shout is always thrown by
        /// somebody the player can actually see.
        /// </summary>
        public const float AudibleRadiusMeters = 22f;

        /// <summary>
        /// And how far out he has to walk before it stops. The gap is
        /// hysteresis: a hero standing on the line would otherwise reset
        /// the turn every other frame and never hear a whole line.
        /// </summary>
        public const float SilenceRadiusMeters = 25f;

        /// <summary>
        /// Inside this the lines are drawn solid. It is a little wider
        /// than the set itself, so a hero who has walked up to watch the
        /// two of them reads them at full strength from either side of
        /// the tables rather than having to stand on one man's feet.
        /// </summary>
        public const float SolidRadiusMeters = 8f;

        /// <summary>
        /// And how faint they are at the far edge of earshot. Low enough
        /// that from across the park the quarrel reads as something
        /// going on over there rather than as something being said to
        /// the player, and high enough to still be legible if he cares
        /// to squint at it — which is the whole invitation to walk over.
        /// </summary>
        public const float FaintOpacity = 0.35f;

        /// <summary>
        /// The frame of the authored shout at which the arm is fully out
        /// and the head has finished turning. Must match the key grid in
        /// `tools/build-city-pedestrian-3d-model.py`; re-timing the clip
        /// re-times this with it.
        /// </summary>
        public const float ShoutPhase = 0.22f;

        private ParkChessPlayerPresentation chessPlayer;
        private ParkCheckersPlayerPresentation checkersPlayer;
        private Transform player;
        private NpcSpeechBubbleView bubbles;
        private ParkQuarrelTimeline timeline;
        private Vector3 setCenter;

        private uint chessTauntState;
        private uint checkersTauntState;
        private int lastChessLineIndex = -1;
        private int lastCheckersLineIndex = -1;

        private ParkQuarrelSpeaker pendingSpeaker;
        private string pendingLineKey = string.Empty;
        private bool pendingRidesClip;
        private bool hasPendingLine;
        private bool isEngaged;

        /// <summary>True while the hero is close enough that the two of
        /// them are actually going at it.</summary>
        public bool IsEngaged => isEngaged;

        /// <summary>The last line served, for tests and logs.</summary>
        public string LastLineKey { get; private set; } = string.Empty;

        public ParkQuarrelSpeaker LastSpeaker { get; private set; }

        public ParkQuarrelTimeline Timeline => timeline;

        /// <summary>
        /// Raises the quarrel, or returns <c>null</c> when the layout
        /// gave the park no chess set — in which case one or both of the
        /// men were never built and there is nobody to argue with.
        /// </summary>
        public static CityParkQuarrelController Create(
            Transform parent,
            ParkChessPlayerPresentation chess,
            ParkCheckersPlayerPresentation checkers,
            Transform playerTransform,
            NpcSpeechBubbleView bubbleView,
            int citySeed)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (chess == null ||
                checkers == null ||
                playerTransform == null ||
                bubbleView == null)
            {
                return null;
            }

            var host = new GameObject(RuntimeObjectName);
            host.transform.SetParent(parent, false);
            var controller =
                host.AddComponent<CityParkQuarrelController>();
            controller.chessPlayer = chess;
            controller.checkersPlayer = checkers;
            controller.player = playerTransform;
            controller.bubbles = bubbleView;
            controller.timeline = new ParkQuarrelTimeline(citySeed);
            controller.chessTauntState = ParkQuarrelTaunts.CreateState(
                citySeed,
                ParkQuarrelSpeaker.Chess);
            controller.checkersTauntState =
                ParkQuarrelTaunts.CreateState(
                    citySeed,
                    ParkQuarrelSpeaker.Checkers);
            // Both men are bolted to their planks for the life of the
            // City, so the middle of the set is measured once.
            controller.setCenter = Vector3.Lerp(
                chess.transform.position,
                checkers.transform.position,
                0.5f);
            return controller;
        }

        /// <summary>
        /// Which radius applies right now. Entering takes the close one,
        /// leaving the wide one.
        /// </summary>
        public static bool IsWithinEarshot(
            float distanceMeters,
            bool wasEngaged)
        {
            float radius = wasEngaged
                ? SilenceRadiusMeters
                : AudibleRadiusMeters;
            return distanceMeters <= radius;
        }

        /// <summary>
        /// How solid the bubbles are drawn from this far off: faint out
        /// at the edge of earshot, solid once he has walked up, and
        /// smoothed across the gap so the words firm up as he closes
        /// rather than snapping on at one particular step.
        /// </summary>
        public static float ResolveBubbleOpacity(float distanceMeters)
        {
            if (float.IsNaN(distanceMeters))
            {
                return FaintOpacity;
            }

            float approach = Mathf.InverseLerp(
                AudibleRadiusMeters,
                SolidRadiusMeters,
                distanceMeters);
            approach = approach * approach * (3f - 2f * approach);
            return Mathf.Lerp(FaintOpacity, 1f, approach);
        }

        private void Update()
        {
            if (chessPlayer == null ||
                checkersPlayer == null ||
                player == null)
            {
                Disengage();
                return;
            }

            float distance = Vector3.Distance(
                player.position,
                setCenter);
            if (!IsWithinEarshot(distance, isEngaged))
            {
                Disengage();
                return;
            }

            if (!isEngaged)
            {
                isEngaged = true;
                timeline.Reset();
            }

            // Every frame, not once per line: he is walking while they
            // are shouting, and a line that fixed its opacity when it
            // opened would stay faint all the way up to the tables.
            if (bubbles != null)
            {
                bubbles.SetOpacity(ResolveBubbleOpacity(distance));
            }

            timeline.Advance(Time.deltaTime);
            if (timeline.ConsumeTauntCue(out ParkQuarrelSpeaker speaker))
            {
                ThrowTaunt(speaker);
            }
        }

        /// <summary>
        /// Runs after both presentations have evaluated their graphs
        /// this frame, which is the whole reason this component declares
        /// an execution order: the phase it reads has to be this frame's.
        /// </summary>
        private void LateUpdate()
        {
            if (!hasPendingLine || bubbles == null)
            {
                return;
            }

            // Held back until the arm is out — unless the beat is
            // already over, which can only mean the presentation was
            // torn down mid-shout. A line that waits for a frame that
            // will never come is a line the player never reads.
            if (pendingRidesClip &&
                IsTauntingOf(pendingSpeaker) &&
                PhaseOf(pendingSpeaker) < ShoutPhase)
            {
                return;
            }

            Transform anchor = AnchorOf(pendingSpeaker);
            if (anchor == null)
            {
                return;
            }

            // The neighbour's line has taken itself down long before
            // now — four seconds up against ten between shouts — but a
            // frame lost somewhere must never leave two panels over one
            // set, so his slot is cleared anyway.
            bubbles.Dismiss(
                OwnerOf(ParkQuarrelTimeline.Opposite(pendingSpeaker)));
            bubbles.Show(
                OwnerOf(pendingSpeaker),
                anchor,
                LocalizationService.Get(pendingLineKey));
            hasPendingLine = false;
        }

        private void OnDisable()
        {
            Disengage();
        }

        private void ThrowTaunt(ParkQuarrelSpeaker speaker)
        {
            string[] keys = ParkQuarrelTaunts.LineKeysFor(speaker);
            int index;
            if (speaker == ParkQuarrelSpeaker.Chess)
            {
                index = ParkQuarrelTaunts.NextIndex(
                    speaker,
                    ref chessTauntState,
                    lastChessLineIndex);
                lastChessLineIndex = index;
                // A prefab built before the shout was authored has no
                // action clip. He still says his piece; he just says it
                // without taking his head out of his hands.
                pendingRidesClip = chessPlayer.BeginTaunt();
            }
            else
            {
                index = ParkQuarrelTaunts.NextIndex(
                    speaker,
                    ref checkersTauntState,
                    lastCheckersLineIndex);
                lastCheckersLineIndex = index;
                pendingRidesClip = checkersPlayer.BeginTaunt();
            }

            pendingSpeaker = speaker;
            pendingLineKey = keys[index];
            hasPendingLine = true;
            LastSpeaker = speaker;
            LastLineKey = pendingLineKey;
        }

        private void Disengage()
        {
            if (!isEngaged)
            {
                return;
            }

            isEngaged = false;
            hasPendingLine = false;
            timeline?.Reset();
            if (bubbles != null)
            {
                bubbles.DismissAll();
                bubbles.SetOpacity(NpcSpeechBubbleView.SolidOpacity);
            }

            if (chessPlayer != null)
            {
                chessPlayer.CancelTaunt();
            }

            if (checkersPlayer != null)
            {
                checkersPlayer.CancelTaunt();
            }
        }

        private float PhaseOf(ParkQuarrelSpeaker speaker)
        {
            if (speaker == ParkQuarrelSpeaker.Chess)
            {
                return chessPlayer != null
                    ? chessPlayer.TauntPhase
                    : -1f;
            }

            return checkersPlayer != null
                ? checkersPlayer.TauntPhase
                : -1f;
        }

        private bool IsTauntingOf(ParkQuarrelSpeaker speaker)
        {
            if (speaker == ParkQuarrelSpeaker.Chess)
            {
                return chessPlayer != null && chessPlayer.IsTaunting;
            }

            return checkersPlayer != null && checkersPlayer.IsTaunting;
        }

        private Transform AnchorOf(ParkQuarrelSpeaker speaker)
        {
            if (speaker == ParkQuarrelSpeaker.Chess)
            {
                return chessPlayer != null
                    ? chessPlayer.SpeechAnchor
                    : null;
            }

            return checkersPlayer != null
                ? checkersPlayer.SpeechAnchor
                : null;
        }

        private UnityEngine.Object OwnerOf(ParkQuarrelSpeaker speaker)
        {
            return speaker == ParkQuarrelSpeaker.Chess
                ? (UnityEngine.Object)chessPlayer
                : checkersPlayer;
        }
    }
}
