using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The coin the Ferryman keeps throwing while he waits.
    ///
    /// It is the only bright thing on him and the only thing about him that
    /// moves more than a breath, so it carries the whole read of the
    /// character from across the island: a man who has been amusing himself
    /// for a very long time.
    ///
    /// The design decision worth stating is that this object has NO STATE.
    /// It never reparents, it is always a child of the runtime root at
    /// scale one, and its world pose is written every frame as a pure
    /// function of the wait loop's own normalized time. There is therefore
    /// no state machine, no frame in which its owner is ambiguous, and
    /// nothing that can drift out of step with the hand - a lagging wisp of
    /// smoke reads as atmosphere, but a lagging coin reads as a bug. It
    /// also means it needs none of the 100x inverse scale that bone-socket
    /// props do, because it is never socketed to a bone.
    ///
    /// Execution order 300 puts it after the presentation has evaluated its
    /// graph for the frame, so the palm it reads has already moved.
    /// </summary>
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public sealed class LastRouteFerrymanCoin : MonoBehaviour
    {
        /// <summary>
        /// Half-turns per throw. ODD on purpose, and that is the whole
        /// difference between flipping a coin and spinning a prop: an odd
        /// count lands it on the other face. Three of them also happen to
        /// be a whole number of turns, so the catch has no seam.
        /// </summary>
        public const int FlipsPerToss = 3;

        /// <summary>
        /// How high above the palm the arc peaks. Tuned to carry it up
        /// past his own face rather than to be physically modest: the
        /// throw is the character, and the top of the arc is the frame
        /// where a player who has just walked up notices it.
        /// </summary>
        public const float ApexMeters = 0.50f;

        /// <summary>
        /// And how far it drifts sideways from release to catch. Small but
        /// not zero: a real toss is never perfectly vertical, and a
        /// perfectly vertical one reads as a machine.
        /// </summary>
        public const float DriftMeters = 0.070f;

        /// <summary>
        /// Deliberately a large coin - a five-kopeck piece rather than a
        /// realistic one.
        ///
        /// The first pass drew it at 32 mm, which is what a coin actually
        /// measures and which is also under one pixel of the 640x360
        /// composite this game renders at any distance a player looks at
        /// him from. Being right about the diameter and invisible is the
        /// wrong trade for the one prop that carries the whole read of
        /// the character, so it is drawn at the size it needs to be seen
        /// at instead.
        /// </summary>
        public const float DiameterMeters = 0.054f;
        public const float ThicknessMeters = 0.009f;

        /// <summary>Old brass, kept bright - and brighter still since the
        /// coat around it was lifted. See the class docstring for why it
        /// is the lightest thing on the island.</summary>
        public static readonly Color CoinColor =
            new Color(0.96f, 0.86f, 0.52f, 1f);

        private LastRouteFerrymanPresentation presentation;
        private Transform palmAnchor;
        private Transform facingReference;
        private Transform coinTransform;

        public bool IsInitialized { get; private set; }

        /// <summary>True while the coin is off the palm, for tests and for
        /// anything that wants to know without recomputing the arc.
        /// </summary>
        public bool IsAirborne { get; private set; }

        /// <summary>
        /// Pure: height above the palm as a fraction of the apex, over the
        /// flight phase. Zero at both ends, one in the middle - the coin
        /// leaves the hand and arrives back in it, by construction rather
        /// than by tuning.
        /// </summary>
        public static float ArcHeightAt(float flightPhase)
        {
            float phase = Mathf.Clamp01(flightPhase);
            return 4f * phase * (1f - phase);
        }

        /// <summary>
        /// Pure: sideways drift as a signed fraction of
        /// <see cref="DriftMeters"/>, from -0.5 at release to +0.5 at the
        /// catch.
        /// </summary>
        public static float ArcDriftAt(float flightPhase)
        {
            return Mathf.Clamp01(flightPhase) - 0.5f;
        }

        /// <summary>Pure: how far it has turned, in degrees.</summary>
        public static float SpinDegreesAt(float flightPhase)
        {
            return Mathf.Clamp01(flightPhase) * 360f * FlipsPerToss;
        }

        public void Initialize(
            LastRouteFerrymanPresentation ferrymanPresentation,
            Transform coinPalmAnchor,
            Transform ferrymanFacing)
        {
            if (ferrymanPresentation == null)
            {
                throw new ArgumentNullException(
                    nameof(ferrymanPresentation));
            }

            if (coinPalmAnchor == null)
            {
                throw new ArgumentNullException(nameof(coinPalmAnchor));
            }

            if (ferrymanFacing == null)
            {
                throw new ArgumentNullException(nameof(ferrymanFacing));
            }

            presentation = ferrymanPresentation;
            palmAnchor = coinPalmAnchor;
            facingReference = ferrymanFacing;

            // A child of this root at scale one, and it stays that way for
            // its whole life. See the class docstring.
            GameObject coin = RuntimePrimitiveFactory.CreateCylinder(
                "Ferryman Coin",
                transform,
                Vector3.zero,
                new Vector3(
                    DiameterMeters,
                    ThicknessMeters * 0.5f,
                    DiameterMeters),
                CoinColor,
                collider: false);
            coinTransform = coin.transform;
            IsInitialized = true;
            WritePose();
        }

        /// <summary>
        /// Where the coin should be, given the palm and the loop position.
        /// Separated from the frame so a test can ask the same question
        /// without a scene running.
        /// </summary>
        public Vector3 ResolveWorldPosition(float normalizedTime)
        {
            Vector3 palm = palmAnchor.position;
            if (!LastRouteFerrymanPresentation.IsCoinAirborneAt(
                    normalizedTime))
            {
                return palm;
            }

            float flight =
                LastRouteFerrymanPresentation.TossFlightPhaseAt(
                    normalizedTime);
            return palm +
                   Vector3.up * (ArcHeightAt(flight) * ApexMeters) +
                   facingReference.right *
                       (ArcDriftAt(flight) * DriftMeters);
        }

        private void LateUpdate()
        {
            if (!IsInitialized)
            {
                return;
            }

            WritePose();
        }

        private void WritePose()
        {
            // Only during the wait. Once he is boarding or driving the coin
            // has been pocketed - the alternative is a coin hanging in the
            // air beside a man who has walked away from it.
            if (!presentation.IsWaiting)
            {
                if (coinTransform.gameObject.activeSelf)
                {
                    coinTransform.gameObject.SetActive(false);
                }

                IsAirborne = false;
                return;
            }

            if (!coinTransform.gameObject.activeSelf)
            {
                coinTransform.gameObject.SetActive(true);
            }

            float normalizedTime = presentation.NormalizedTime;
            IsAirborne = LastRouteFerrymanPresentation.IsCoinAirborneAt(
                normalizedTime);
            float flight = IsAirborne
                ? LastRouteFerrymanPresentation.TossFlightPhaseAt(
                    normalizedTime)
                : 0f;

            // Tumbling end over end about his own lateral axis, so the face
            // of the coin keeps turning towards and away from the camera
            // rather than spinning like a top.
            coinTransform.SetPositionAndRotation(
                ResolveWorldPosition(normalizedTime),
                Quaternion.AngleAxis(
                    SpinDegreesAt(flight),
                    facingReference.right) *
                Quaternion.LookRotation(
                    facingReference.right,
                    Vector3.up));
        }
    }
}
