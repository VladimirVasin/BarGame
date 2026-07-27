using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public sealed class BeerPongTableLayout
    {
        public const int CupCount = 6;

        private static readonly BeerPongTableLayout defaultLayout =
            CreateDefault();

        private readonly BeerPongCupDefinition[] cups;
        private readonly ReadOnlyCollection<BeerPongCupDefinition> cupsView;

        public BeerPongTableLayout(
            float tableHalfWidth,
            float tableNearZ,
            float tableFarZ,
            float tableSurfaceY,
            float ballRadius,
            Vector3 throwOrigin,
            IList<BeerPongCupDefinition> cupDefinitions)
        {
            if (!BeerPongMath.IsFinitePositive(tableHalfWidth))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tableHalfWidth));
            }

            if (!BeerPongMath.IsFinite(tableNearZ) ||
                !BeerPongMath.IsFinite(tableFarZ) ||
                tableFarZ <= tableNearZ)
            {
                throw new ArgumentException(
                    "Table depth bounds must be finite and ordered.");
            }

            if (!BeerPongMath.IsFinite(tableSurfaceY))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tableSurfaceY));
            }

            if (!BeerPongMath.IsFinitePositive(ballRadius))
            {
                throw new ArgumentOutOfRangeException(nameof(ballRadius));
            }

            if (!BeerPongMath.IsFinite(throwOrigin))
            {
                throw new ArgumentException(
                    "Throw origin must be finite.",
                    nameof(throwOrigin));
            }

            if (cupDefinitions == null)
            {
                throw new ArgumentNullException(nameof(cupDefinitions));
            }

            if (cupDefinitions.Count != CupCount)
            {
                throw new ArgumentException(
                    $"A beer-pong layout must contain exactly {CupCount} cups.",
                    nameof(cupDefinitions));
            }

            cups = new BeerPongCupDefinition[CupCount];
            var occupiedIndices = new bool[CupCount];
            for (int i = 0; i < cupDefinitions.Count; i++)
            {
                BeerPongCupDefinition cup = cupDefinitions[i];
                if (cup.Index >= CupCount || occupiedIndices[cup.Index])
                {
                    throw new ArgumentException(
                        "Cup indices must be unique and contiguous from zero.",
                        nameof(cupDefinitions));
                }

                if (cup.MouthCenter.x - cup.MouthRadius < -tableHalfWidth ||
                    cup.MouthCenter.x + cup.MouthRadius > tableHalfWidth ||
                    cup.MouthCenter.z - cup.MouthRadius < tableNearZ ||
                    cup.MouthCenter.z + cup.MouthRadius > tableFarZ ||
                    cup.BaseCenter.y < tableSurfaceY - 0.001f)
                {
                    throw new ArgumentException(
                        "Every cup must stand completely on the table.",
                        nameof(cupDefinitions));
                }

                occupiedIndices[cup.Index] = true;
                cups[cup.Index] = cup;
            }

            TableHalfWidth = tableHalfWidth;
            TableNearZ = tableNearZ;
            TableFarZ = tableFarZ;
            TableSurfaceY = tableSurfaceY;
            BallRadius = ballRadius;
            ThrowOrigin = throwOrigin;
            cupsView = Array.AsReadOnly(cups);
        }

        public static BeerPongTableLayout Default => defaultLayout;
        public float TableHalfWidth { get; }
        public float TableNearZ { get; }
        public float TableFarZ { get; }
        public float TableSurfaceY { get; }
        public float BallRadius { get; }
        public Vector3 ThrowOrigin { get; }
        public IReadOnlyList<BeerPongCupDefinition> Cups => cupsView;
        public int AllCupsMask => (1 << CupCount) - 1;

        public BeerPongCupDefinition GetCup(int index)
        {
            if (index < 0 || index >= cups.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return cups[index];
        }

        public bool IsCupActive(int cupMask, int cupIndex)
        {
            if (cupIndex < 0 || cupIndex >= CupCount)
            {
                return false;
            }

            return (cupMask & (1 << cupIndex)) != 0;
        }

        public bool IsPointOverTable(Vector3 position)
        {
            return
                position.x >= -TableHalfWidth &&
                position.x <= TableHalfWidth &&
                position.z >= TableNearZ &&
                position.z <= TableFarZ;
        }

        private static BeerPongTableLayout CreateDefault()
        {
            const float tableSurface = 0f;
            const float cupHeight = 0.38f;
            const float mouthRadius = 0.17f;
            float mouthY = tableSurface + cupHeight;

            var definitions = new[]
            {
                new BeerPongCupDefinition(
                    0,
                    new Vector3(0f, mouthY, 3.55f),
                    mouthRadius,
                    cupHeight),
                new BeerPongCupDefinition(
                    1,
                    new Vector3(-0.19f, mouthY, 3.91f),
                    mouthRadius,
                    cupHeight),
                new BeerPongCupDefinition(
                    2,
                    new Vector3(0.19f, mouthY, 3.91f),
                    mouthRadius,
                    cupHeight),
                new BeerPongCupDefinition(
                    3,
                    new Vector3(-0.38f, mouthY, 4.27f),
                    mouthRadius,
                    cupHeight),
                new BeerPongCupDefinition(
                    4,
                    new Vector3(0f, mouthY, 4.27f),
                    mouthRadius,
                    cupHeight),
                new BeerPongCupDefinition(
                    5,
                    new Vector3(0.38f, mouthY, 4.27f),
                    mouthRadius,
                    cupHeight)
            };

            return new BeerPongTableLayout(
                1.1f,
                0f,
                4.8f,
                tableSurface,
                0.075f,
                new Vector3(0f, 0.86f, -0.32f),
                definitions);
        }
    }

    public static class BeerPongAim
    {
        public const float MinimumYawDegrees = -35f;
        public const float MaximumYawDegrees = 35f;
        public const float MinimumPitchDegrees = 15f;
        public const float MaximumPitchDegrees = 65f;
        public const float MinimumLaunchSpeed = 5.25f;
        public const float MaximumLaunchSpeed = 9.25f;

        public static Vector3 ToVelocity(
            float yawDegrees,
            float pitchDegrees,
            float power)
        {
            if (!BeerPongMath.IsFinite(yawDegrees))
            {
                throw new ArgumentOutOfRangeException(nameof(yawDegrees));
            }

            if (!BeerPongMath.IsFinite(pitchDegrees))
            {
                throw new ArgumentOutOfRangeException(nameof(pitchDegrees));
            }

            if (!BeerPongMath.IsFinite(power))
            {
                throw new ArgumentOutOfRangeException(nameof(power));
            }

            float clampedYaw = Mathf.Clamp(
                yawDegrees,
                MinimumYawDegrees,
                MaximumYawDegrees);
            float clampedPitch = Mathf.Clamp(
                pitchDegrees,
                MinimumPitchDegrees,
                MaximumPitchDegrees);
            float clampedPower = Mathf.Clamp01(power);
            float speed = Mathf.Lerp(
                MinimumLaunchSpeed,
                MaximumLaunchSpeed,
                clampedPower);

            float yawRadians = clampedYaw * Mathf.Deg2Rad;
            float pitchRadians = clampedPitch * Mathf.Deg2Rad;
            float horizontal = Mathf.Cos(pitchRadians);
            return new Vector3(
                Mathf.Sin(yawRadians) * horizontal,
                Mathf.Sin(pitchRadians),
                Mathf.Cos(yawRadians) * horizontal) * speed;
        }
    }
}
