using System;
using UnityEngine;

namespace BarPromenade
{
    public enum PlayerViewDirection
    {
        Front = 0,
        FrontRight = 1,
        Right = 2,
        BackRight = 3,
        Back = 4,
        BackLeft = 5,
        Left = 6,
        FrontLeft = 7
    }

    /// <summary>
    /// Selects the nearest of eight view directions while retaining the
    /// current direction across a configurable angular hysteresis band.
    /// </summary>
    public sealed class PlayerViewDirectionSelector
    {
        public const float SectorAngleDegrees = 45f;
        public const float HalfSectorAngleDegrees = 22.5f;
        public const float DefaultHysteresisDegrees = 5f;

        private const int DirectionCount = 8;

        private readonly float hysteresisDegrees;
        private PlayerViewDirection currentDirection;

        public PlayerViewDirectionSelector(
            float hysteresisDegrees = DefaultHysteresisDegrees,
            PlayerViewDirection initialDirection =
                PlayerViewDirection.Front)
        {
            if (!IsFinite(hysteresisDegrees) ||
                hysteresisDegrees < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(hysteresisDegrees),
                    "Hysteresis must be finite and non-negative.");
            }

            this.hysteresisDegrees = hysteresisDegrees;
            Reset(initialDirection);
        }

        public float HysteresisDegrees => hysteresisDegrees;
        public PlayerViewDirection CurrentDirection => currentDirection;

        public PlayerViewDirection Select(float angleDegrees)
        {
            ValidateAngle(angleDegrees);

            float currentCenter =
                (int)currentDirection * SectorAngleDegrees;
            float distanceFromCenter = Mathf.Abs(
                Mathf.DeltaAngle(currentCenter, angleDegrees));
            float holdHalfAngle =
                HalfSectorAngleDegrees + hysteresisDegrees;

            if (distanceFromCenter > holdHalfAngle)
            {
                currentDirection = GetNearestDirection(angleDegrees);
            }

            return currentDirection;
        }

        public void Reset(PlayerViewDirection direction)
        {
            ValidateDirection(direction);
            currentDirection = direction;
        }

        public static PlayerViewDirection GetNearestDirection(
            float angleDegrees)
        {
            ValidateAngle(angleDegrees);

            float normalizedAngle = Mathf.Repeat(angleDegrees, 360f);
            int directionIndex = Mathf.FloorToInt(
                (normalizedAngle + HalfSectorAngleDegrees) /
                SectorAngleDegrees) % DirectionCount;
            return (PlayerViewDirection)directionIndex;
        }

        private static void ValidateAngle(float angleDegrees)
        {
            if (!IsFinite(angleDegrees))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(angleDegrees),
                    "Angle must be finite.");
            }
        }

        private static void ValidateDirection(
            PlayerViewDirection direction)
        {
            int directionIndex = (int)direction;
            if (directionIndex < 0 ||
                directionIndex >= DirectionCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(direction),
                    direction,
                    "Direction must be one of the eight defined values.");
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
