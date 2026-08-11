using UnityEngine;

namespace BarPromenade
{
    public enum CityBusDoorPhase
    {
        Closed = 0,
        Opening,
        Open,
        Closing
    }

    public readonly struct CityBusDriverDoorSample
    {
        internal CityBusDriverDoorSample(
            float doorOpenness,
            CityBusDoorPhase doorPhase,
            float phase01,
            float rightHandButtonBlend,
            float buttonPress01,
            float doorLook01)
        {
            DoorOpenness = doorOpenness;
            DoorPhase = doorPhase;
            Phase01 = phase01;
            RightHandButtonBlend = rightHandButtonBlend;
            ButtonPress01 = buttonPress01;
            DoorLook01 = doorLook01;
        }

        public float DoorOpenness { get; }
        public CityBusDoorPhase DoorPhase { get; }
        public float Phase01 { get; }
        public float RightHandButtonBlend { get; }
        public float ButtonPress01 { get; }
        public float DoorLook01 { get; }
    }

    public static class CityBusDriverDoorTimeline
    {
        public const float DefaultDwellDuration = 10f;
        public const float DefaultDoorTransitionDuration = 0.70f;
        public const float ApproachReachDistance = 0.80f;
        public const float ApproachFullSpeed = 2f;
        public const float ApproachMaximumSpeed = 3f;
        public const float ClosingButtonReachDuration = 0.45f;
        public const float HandReturnDuration = 0.45f;
        public const float ButtonReleaseDuration = 0.12f;
        public const float DoorLookTurnDuration = 0.25f;
        public const float DoorLookReturnDuration = 0.45f;

        public static CityBusDriverDoorSample SampleApproach(
            float distanceToStop,
            float speed)
        {
            float distance = SanitizeDistance(distanceToStop);
            float safeSpeed = SanitizeSpeed(speed);
            float distanceBlend = 1f - SmootherStep(
                distance / ApproachReachDistance);
            float speedBlend = 1f - SmootherStep(
                Mathf.InverseLerp(
                    ApproachFullSpeed,
                    ApproachMaximumSpeed,
                    safeSpeed));

            return new CityBusDriverDoorSample(
                0f,
                CityBusDoorPhase.Closed,
                0f,
                distanceBlend * speedBlend,
                0f,
                0f);
        }

        public static CityBusDriverDoorSample SampleDwell(
            float elapsed,
            float duration,
            float doorTransitionDuration)
        {
            float safeDuration = SanitizePositive(
                duration,
                DefaultDwellDuration);
            float transition = Mathf.Min(
                SanitizePositive(
                    doorTransitionDuration,
                    DefaultDoorTransitionDuration),
                safeDuration * 0.5f);
            float safeElapsed = SanitizeElapsed(
                elapsed,
                safeDuration);
            float closingStart = safeDuration - transition;

            if (safeElapsed >= safeDuration)
            {
                return CreateClosedSample();
            }

            if (safeElapsed < transition)
            {
                return SampleOpening(safeElapsed, transition);
            }

            if (safeElapsed >= closingStart)
            {
                return SampleClosing(
                    safeElapsed - closingStart,
                    transition);
            }

            return SampleOpenHold(
                safeElapsed,
                transition,
                closingStart);
        }

        private static CityBusDriverDoorSample SampleOpening(
            float phaseElapsed,
            float transitionDuration)
        {
            float phase01 = Mathf.Clamp01(
                phaseElapsed / transitionDuration);
            float handDuration = Mathf.Min(
                HandReturnDuration,
                transitionDuration);
            float pressDuration = Mathf.Min(
                ButtonReleaseDuration,
                transitionDuration);
            float lookDuration = Mathf.Min(
                DoorLookTurnDuration,
                transitionDuration);

            return new CityBusDriverDoorSample(
                phase01,
                CityBusDoorPhase.Opening,
                phase01,
                1f - SmootherStep(phaseElapsed / handDuration),
                1f - SmootherStep(phaseElapsed / pressDuration),
                SmootherStep(phaseElapsed / lookDuration));
        }

        private static CityBusDriverDoorSample SampleOpenHold(
            float elapsed,
            float transitionDuration,
            float closingStart)
        {
            float openDuration = closingStart - transitionDuration;
            float phase01 = openDuration > 0f
                ? Mathf.Clamp01(
                    (elapsed - transitionDuration) / openDuration)
                : 1f;
            float reachDuration = Mathf.Min(
                ClosingButtonReachDuration,
                openDuration);
            float reachStart = closingStart - reachDuration;
            float handBlend = reachDuration > 0f
                ? SmootherStep(
                    (elapsed - reachStart) / reachDuration)
                : 1f;
            return new CityBusDriverDoorSample(
                1f,
                CityBusDoorPhase.Open,
                phase01,
                handBlend,
                0f,
                1f);
        }

        private static CityBusDriverDoorSample SampleClosing(
            float phaseElapsed,
            float transitionDuration)
        {
            float phase01 = Mathf.Clamp01(
                phaseElapsed / transitionDuration);
            float handDuration = Mathf.Min(
                HandReturnDuration,
                transitionDuration);
            float pressDuration = Mathf.Min(
                ButtonReleaseDuration,
                transitionDuration);
            float lookDuration = Mathf.Min(
                DoorLookReturnDuration,
                transitionDuration);

            return new CityBusDriverDoorSample(
                1f - phase01,
                CityBusDoorPhase.Closing,
                phase01,
                1f - SmootherStep(phaseElapsed / handDuration),
                1f - SmootherStep(phaseElapsed / pressDuration),
                1f - SmootherStep(phaseElapsed / lookDuration));
        }

        private static CityBusDriverDoorSample CreateClosedSample()
        {
            return new CityBusDriverDoorSample(
                0f,
                CityBusDoorPhase.Closed,
                0f,
                0f,
                0f,
                0f);
        }

        private static float SanitizeDistance(float distance)
        {
            if (float.IsNaN(distance) ||
                float.IsPositiveInfinity(distance))
            {
                return float.PositiveInfinity;
            }

            return Mathf.Max(0f, distance);
        }

        private static float SanitizeSpeed(float speed)
        {
            if (float.IsNaN(speed) ||
                float.IsPositiveInfinity(speed))
            {
                return float.PositiveInfinity;
            }

            return Mathf.Max(0f, speed);
        }

        private static float SanitizeElapsed(
            float elapsed,
            float duration)
        {
            if (float.IsNaN(elapsed) || elapsed <= 0f)
            {
                return 0f;
            }

            if (float.IsPositiveInfinity(elapsed))
            {
                return duration;
            }

            return Mathf.Min(elapsed, duration);
        }

        private static float SanitizePositive(
            float value,
            float fallback)
        {
            return value > 0f &&
                   !float.IsInfinity(value)
                ? value
                : fallback;
        }

        private static float SmootherStep(float amount)
        {
            float clamped = Mathf.Clamp01(amount);
            return clamped *
                   clamped *
                   clamped *
                   (clamped * (clamped * 6f - 15f) + 10f);
        }
    }
}
