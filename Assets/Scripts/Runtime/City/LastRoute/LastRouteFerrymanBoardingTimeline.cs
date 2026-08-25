using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The clock of one man getting off a car and into it: the drop, the
    /// walk round the nose, and the door-open-sit-shut. Pure and
    /// EditMode-testable, the mourner's idiom.
    ///
    /// Two of the three lengths are the authored clips' own and are handed
    /// in rather than typed here, because a clip that is re-timed in Blender
    /// must not need this file re-timed with it. The walk's length is the
    /// only one this side owns, and it is a distance divided by a pace.
    /// </summary>
    public sealed class LastRouteFerrymanBoardingTimeline
    {
        /// <summary>
        /// Inside the DROP, when his boots reach the ground. Authored: the
        /// landing key of `FerrymanDismount` sits at 0.62 of the clip, and
        /// the car is kicked on its springs at exactly that fraction. Move
        /// one and the body rocks before or after he has actually landed,
        /// which is the single most obvious way this beat can read as fake.
        /// </summary>
        public const float LandingPhase = 0.62f;

        /// <summary>
        /// Inside the DROP, when his hips actually leave the metal. The
        /// root does not stir before this: the first fifth of the clip is
        /// the shove, and a man whose whole body slides forward while he is
        /// still braced on a bonnet reads as a man on a conveyor.
        /// </summary>
        public const float DropReleasePhase = 0.20f;

        /// <summary>
        /// Inside the BOARD, when his weight arrives on the seat and the
        /// springs take it. The authored key that puts his pelvis down.
        /// </summary>
        public const float SeatCuePhase = 0.62f;

        /// <summary>
        /// Inside the BOARD, the door's own four moments. The leaf is shut
        /// until his hand is on the handle, open before he moves an inch
        /// towards the seat, and does not start closing until he is in it.
        ///
        /// These are a CONTRACT with the key grid of `FerrymanBoard` in
        /// `tools/build-city-pedestrian-3d-model.py`, in the same way the
        /// coin's release and catch are a contract with `FerrymanWait`. Get
        /// them out of step and he mimes a handle that is already open, or
        /// walks through his own door.
        /// </summary>
        public const float DoorPullPhase = 0.16f;
        public const float DoorOpenPhase = 0.34f;
        public const float DoorShutStartPhase = 0.84f;
        public const float DoorShutPhase = 0.98f;

        /// <summary>
        /// When the ROOT travels from the standing point into the seat.
        /// It starts only once the leaf is fully open and finishes before
        /// the leaf begins to close, so the man is never in the same place
        /// as the door.
        /// </summary>
        public const float TravelStartPhase = 0.36f;
        public const float TravelEndPhase = 0.78f;

        private float phaseElapsed;
        private float phaseDuration;
        private bool landingCueConsumed;
        private bool seatCueConsumed;

        public LastRouteFerrymanBoardingTimeline(
            float dismountSeconds,
            float walkSeconds,
            float boardSeconds)
        {
            DismountSeconds = Require(dismountSeconds, nameof(dismountSeconds));
            WalkSeconds = Require(walkSeconds, nameof(walkSeconds));
            BoardSeconds = Require(boardSeconds, nameof(boardSeconds));
            phaseDuration = DismountSeconds;
        }

        public float DismountSeconds { get; }
        public float WalkSeconds { get; }
        public float BoardSeconds { get; }

        public LastRouteFerrymanPhase Phase { get; private set; } =
            LastRouteFerrymanPhase.Dismounting;

        public float PhaseElapsed => phaseElapsed;
        public float PhaseDuration => phaseDuration;
        public bool IsDone => Phase == LastRouteFerrymanPhase.Driving;

        /// <summary>How far through the current phase, in `[0, 1]`. One at
        /// the end, so a terminal sample lands on the authored last frame
        /// rather than a hair before it.</summary>
        public float PhaseProgress => phaseDuration > 0f
            ? Mathf.Clamp01(phaseElapsed / phaseDuration)
            : 1f;

        /// <summary>
        /// How far open the driver's door should be right now. Zero in
        /// every phase but the board, because the leaf belongs to the beat
        /// that pulls it rather than to a second free-running timer - the
        /// same rule the coin lives by.
        /// </summary>
        public float DriverDoorOpenness
        {
            get
            {
                if (Phase != LastRouteFerrymanPhase.Boarding)
                {
                    return 0f;
                }

                return EvaluateDoorOpenness(PhaseProgress);
            }
        }

        /// <summary>How far the root has moved from the standing point into
        /// the seat, in `[0, 1]`, eased.</summary>
        public float SeatTravel => Phase == LastRouteFerrymanPhase.Boarding
            ? EvaluateSeatTravel(PhaseProgress)
            : 0f;

        /// <summary>How far the root has carried off the bonnet towards the
        /// landing point, in `[0, 1]`.</summary>
        public float DropTravel => Phase == LastRouteFerrymanPhase.Dismounting
            ? EvaluateDropTravel(PhaseProgress)
            : 1f;

        /// <summary>How far the root has FALLEN, in `[0, 1]`. Separate from
        /// the horizontal on purpose - see
        /// <see cref="EvaluateDropFall"/>.</summary>
        public float DropFall => Phase == LastRouteFerrymanPhase.Dismounting
            ? EvaluateDropFall(PhaseProgress)
            : 1f;

        /// <summary>
        /// Pure: the horizontal carry off the bonnet. Held at zero through
        /// the shove and complete at the landing key, because everything
        /// after that key is the absorb and the stand, both in place.
        /// </summary>
        public static float EvaluateDropTravel(float dismountProgress)
        {
            float progress = Mathf.Clamp01(dismountProgress);
            if (progress <= DropReleasePhase)
            {
                return 0f;
            }

            if (progress >= LandingPhase)
            {
                return 1f;
            }

            return Mathf.SmoothStep(
                0f,
                1f,
                (progress - DropReleasePhase) /
                (LandingPhase - DropReleasePhase));
        }

        /// <summary>
        /// Pure: the fall. Squared rather than smoothed, and that is the
        /// point - a smoothstep decelerates into the ground, which is what
        /// a lift does. A man who steps off a car accelerates all the way
        /// down and stops dead, and the stopping dead is the frame the
        /// springs are kicked on.
        /// </summary>
        public static float EvaluateDropFall(float dismountProgress)
        {
            float progress = Mathf.Clamp01(dismountProgress);
            if (progress <= DropReleasePhase)
            {
                return 0f;
            }

            if (progress >= LandingPhase)
            {
                return 1f;
            }

            float fall =
                (progress - DropReleasePhase) /
                (LandingPhase - DropReleasePhase);
            return fall * fall;
        }

        /// <summary>Pure: the leaf's angle fraction at a point in the board
        /// clip.</summary>
        public static float EvaluateDoorOpenness(float boardProgress)
        {
            float progress = Mathf.Clamp01(boardProgress);
            if (progress <= DoorPullPhase)
            {
                return 0f;
            }

            if (progress < DoorOpenPhase)
            {
                return Mathf.SmoothStep(
                    0f,
                    1f,
                    (progress - DoorPullPhase) /
                    (DoorOpenPhase - DoorPullPhase));
            }

            if (progress <= DoorShutStartPhase)
            {
                return 1f;
            }

            if (progress >= DoorShutPhase)
            {
                return 0f;
            }

            return 1f - Mathf.SmoothStep(
                0f,
                1f,
                (progress - DoorShutStartPhase) /
                (DoorShutPhase - DoorShutStartPhase));
        }

        /// <summary>Pure: how far into the seat the root has carried.
        /// </summary>
        public static float EvaluateSeatTravel(float boardProgress)
        {
            float progress = Mathf.Clamp01(boardProgress);
            if (progress <= TravelStartPhase)
            {
                return 0f;
            }

            if (progress >= TravelEndPhase)
            {
                return 1f;
            }

            return Mathf.SmoothStep(
                0f,
                1f,
                (progress - TravelStartPhase) /
                (TravelEndPhase - TravelStartPhase));
        }

        public void Advance(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            phaseElapsed += Mathf.Max(0f, deltaTime);
            // The remainder of a step that crosses a boundary belongs to
            // the next phase, so a hitchy frame cannot stretch the beat.
            while (!IsDone && phaseElapsed >= phaseDuration)
            {
                float carried = phaseElapsed - phaseDuration;
                switch (Phase)
                {
                    case LastRouteFerrymanPhase.Dismounting:
                        SetPhase(
                            LastRouteFerrymanPhase.WalkingToDoor,
                            WalkSeconds);
                        break;
                    case LastRouteFerrymanPhase.WalkingToDoor:
                        SetPhase(
                            LastRouteFerrymanPhase.Boarding,
                            BoardSeconds);
                        break;
                    default:
                        SetPhase(LastRouteFerrymanPhase.Driving, 0f);
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
        /// One-shot: true exactly once, at the frame his boots reach the
        /// ground. The car's springs are kicked from this and nothing else,
        /// so the rock cannot drift away from the landing.
        /// </summary>
        public bool ConsumeLandingCue()
        {
            if (landingCueConsumed)
            {
                return false;
            }

            bool landed =
                Phase != LastRouteFerrymanPhase.Dismounting ||
                PhaseProgress >= LandingPhase;
            if (!landed)
            {
                return false;
            }

            landingCueConsumed = true;
            return true;
        }

        /// <summary>
        /// One-shot: true exactly once, at the frame his weight reaches the
        /// driver's seat. The springs take it from here and nowhere else.
        /// </summary>
        public bool ConsumeSeatCue()
        {
            if (seatCueConsumed)
            {
                return false;
            }

            bool seated =
                Phase == LastRouteFerrymanPhase.Driving ||
                (Phase == LastRouteFerrymanPhase.Boarding &&
                 PhaseProgress >= SeatCuePhase);
            if (!seated)
            {
                return false;
            }

            seatCueConsumed = true;
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
