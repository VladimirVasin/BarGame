using System;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct StairwellFlightPlan
    {
        public StairwellFlightPlan(
            string id,
            Vector2 start,
            Vector2 direction,
            float baseElevation,
            int stepCount,
            float stepRise,
            float stepDepth,
            float width)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "A stairwell flight requires an id.",
                    nameof(id));
            }

            Id = id;
            Start = start;
            Direction = direction.normalized;
            BaseElevation = baseElevation;
            StepCount = stepCount;
            StepRise = stepRise;
            StepDepth = stepDepth;
            Width = width;
        }

        public string Id { get; }
        public Vector2 Start { get; }
        public Vector2 Direction { get; }
        public float BaseElevation { get; }
        public int StepCount { get; }
        public float StepRise { get; }
        public float StepDepth { get; }
        public float Width { get; }
        public float TopElevation =>
            BaseElevation + (StepCount * StepRise);
        public float RunLength => StepCount * StepDepth;

        public Vector3 GetStepCenter(int index)
        {
            if (index < 0 || index >= StepCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            float height = (index + 1) * StepRise;
            Vector2 planarCenter =
                Start +
                Direction * ((index + 0.5f) * StepDepth);
            return new Vector3(
                planarCenter.x,
                BaseElevation + (height * 0.5f),
                planarCenter.y);
        }

        public Vector3 GetStepSize(int index)
        {
            if (index < 0 || index >= StepCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return new Vector3(
                Width,
                (index + 1) * StepRise,
                StepDepth + 0.015f);
        }
    }
}
