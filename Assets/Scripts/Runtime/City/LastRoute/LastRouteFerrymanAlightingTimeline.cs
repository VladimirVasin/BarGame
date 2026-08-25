using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The clock of the same man getting back OUT of the car and onto its
    /// bonnet: the door and the climb out, the walk back round the nose, and
    /// the sit up onto the metal.
    ///
    /// It is <see cref="LastRouteFerrymanBoardingTimeline"/> run backwards, and
    /// it is written as that rather than as three new beats because the beats
    /// are the same three. Every shape it needs is one of that timeline's own
    /// pure functions evaluated at `1 - progress`, which is also why no new
    /// animation was authored for any of this: each one-shot in the library is
    /// deliberately authored to END on the base pose of the clip the runtime
    /// crosses into, so played backwards it BEGINS there - which is exactly
    /// what a reverse beat needs at its seam.
    ///
    /// Read the door curve backwards and it is still shut, open, shut: the
    /// leaf comes open, he unfolds himself out through it, and it goes to
    /// behind him. That symmetry is not luck - it is what
    /// <see cref="LastRouteFerrymanBoardingTimeline.EvaluateDoorOpenness"/>
    /// describes either way round.
    /// </summary>
    public sealed class LastRouteFerrymanAlightingTimeline
    {
        private float phaseElapsed;
        private float phaseDuration;
        private bool unseatCueConsumed;
        private bool mountCueConsumed;

        public LastRouteFerrymanAlightingTimeline(
            float boardSeconds,
            float walkSeconds,
            float dismountSeconds)
        {
            BoardSeconds = Require(boardSeconds, nameof(boardSeconds));
            WalkSeconds = Require(walkSeconds, nameof(walkSeconds));
            DismountSeconds = Require(dismountSeconds, nameof(dismountSeconds));
            phaseDuration = BoardSeconds;
        }

        public float BoardSeconds { get; }
        public float WalkSeconds { get; }
        public float DismountSeconds { get; }

        public LastRouteFerrymanPhase Phase { get; private set; } =
            LastRouteFerrymanPhase.Alighting;

        public float PhaseElapsed => phaseElapsed;
        public float PhaseDuration => phaseDuration;

        /// <summary>He is back on the bonnet and the beat is over.</summary>
        public bool IsDone => Phase == LastRouteFerrymanPhase.Waiting;

        public float PhaseProgress => phaseDuration > 0f
            ? Mathf.Clamp01(phaseElapsed / phaseDuration)
            : 1f;

        /// <summary>
        /// Where in its own clip the reversed one-shot should stand: one at
        /// the start of the phase, zero at the end. The presentation parks
        /// these clips at speed zero and writes this fraction of their length
        /// every frame, rather than trusting a negative playable speed.
        /// </summary>
        public float ReversedClipPhase => 1f - PhaseProgress;

        /// <summary>How far open the driver's leaf stands. Zero outside the
        /// climb out, for the boarding timeline's own reason: the leaf belongs
        /// to the hand pulling it.</summary>
        public float DriverDoorOpenness =>
            Phase == LastRouteFerrymanPhase.Alighting
                ? LastRouteFerrymanBoardingTimeline.EvaluateDoorOpenness(
                    ReversedClipPhase)
                : 0f;

        /// <summary>
        /// How far the root still is INTO the seat, in `[0, 1]` - one while he
        /// is still sitting in it, zero once he is standing at the door. The
        /// board's own travel curve read backwards.
        /// </summary>
        public float SeatTravel =>
            Phase == LastRouteFerrymanPhase.Alighting
                ? LastRouteFerrymanBoardingTimeline.EvaluateSeatTravel(
                    ReversedClipPhase)
                : 0f;

        /// <summary>How far the root has carried back onto the bonnet, in
        /// `[0, 1]`. Zero until the sit up begins.</summary>
        public float MountTravel =>
            Phase == LastRouteFerrymanPhase.Mounting
                ? 1f - LastRouteFerrymanBoardingTimeline.EvaluateDropTravel(
                    ReversedClipPhase)
                : (Phase == LastRouteFerrymanPhase.Waiting ? 1f : 0f);

        /// <summary>
        /// How far the root has RISEN back onto the metal, in `[0, 1]`.
        /// Separate from the horizontal exactly as the drop's fall is, and for
        /// the mirrored reason: he does not float up, he pushes.
        /// </summary>
        public float MountRise =>
            Phase == LastRouteFerrymanPhase.Mounting
                ? 1f - LastRouteFerrymanBoardingTimeline.EvaluateDropFall(
                    ReversedClipPhase)
                : (Phase == LastRouteFerrymanPhase.Waiting ? 1f : 0f);

        public void Advance(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            phaseElapsed += Mathf.Max(0f, deltaTime);
            while (!IsDone && phaseElapsed >= phaseDuration)
            {
                float carried = phaseElapsed - phaseDuration;
                switch (Phase)
                {
                    case LastRouteFerrymanPhase.Alighting:
                        SetPhase(
                            LastRouteFerrymanPhase.WalkingToBonnet,
                            WalkSeconds);
                        break;
                    case LastRouteFerrymanPhase.WalkingToBonnet:
                        SetPhase(
                            LastRouteFerrymanPhase.Mounting,
                            DismountSeconds);
                        break;
                    default:
                        SetPhase(LastRouteFerrymanPhase.Waiting, 0f);
                        break;
                }

                phaseElapsed = carried;
                if (IsDone)
                {
                    phaseElapsed = 0f;
                    break;
                }
            }
        }

        /// <summary>
        /// One-shot: true exactly once, at the frame his weight leaves the
        /// driver's seat and the car comes up on that side. The mirror of the
        /// board's seat cue, at the mirror of its phase.
        /// </summary>
        public bool ConsumeUnseatCue()
        {
            if (unseatCueConsumed)
            {
                return false;
            }

            bool left =
                Phase != LastRouteFerrymanPhase.Alighting ||
                ReversedClipPhase <=
                    LastRouteFerrymanBoardingTimeline.SeatCuePhase;
            if (!left)
            {
                return false;
            }

            unseatCueConsumed = true;
            return true;
        }

        /// <summary>
        /// One-shot: true exactly once, at the frame he settles back onto the
        /// bonnet and the nose goes down again.
        /// </summary>
        public bool ConsumeMountCue()
        {
            if (mountCueConsumed)
            {
                return false;
            }

            bool settled =
                Phase == LastRouteFerrymanPhase.Waiting ||
                (Phase == LastRouteFerrymanPhase.Mounting &&
                 ReversedClipPhase <=
                     LastRouteFerrymanBoardingTimeline.LandingPhase);
            if (!settled)
            {
                return false;
            }

            mountCueConsumed = true;
            return true;
        }

        private void SetPhase(LastRouteFerrymanPhase phase, float duration)
        {
            Phase = phase;
            phaseDuration = duration;
            phaseElapsed = 0f;
        }

        private static float Require(float seconds, string parameterName)
        {
            if (float.IsNaN(seconds) ||
                float.IsInfinity(seconds) ||
                seconds < 0f)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }

            return seconds;
        }
    }
}
