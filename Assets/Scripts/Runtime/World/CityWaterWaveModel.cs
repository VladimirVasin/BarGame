using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One water body's wave recipe, as the shader's uniforms describe
    /// it: the numbers <see cref="CityWaterWaveModel"/> needs to stand
    /// on the CPU exactly where the GPU stands the surface.
    /// </summary>
    public readonly struct CityWaterWaveProfile
    {
        public CityWaterWaveProfile(
            float waveHeight,
            float waveLength,
            Vector2 flowDirection,
            float flowSpeed,
            float shoreFadeSouthZ,
            float shoreFadeRampLength,
            float shoreFadeFloor,
            bool shoreFadeEnabled)
        {
            WaveHeight = waveHeight;
            WaveLength = waveLength;
            FlowDirection = flowDirection;
            FlowSpeed = flowSpeed;
            ShoreFadeSouthZ = shoreFadeSouthZ;
            ShoreFadeRampLength = shoreFadeRampLength;
            ShoreFadeFloor = shoreFadeFloor;
            ShoreFadeEnabled = shoreFadeEnabled;
        }

        public float WaveHeight { get; }
        public float WaveLength { get; }
        public Vector2 FlowDirection { get; }
        public float FlowSpeed { get; }
        public float ShoreFadeSouthZ { get; }
        public float ShoreFadeRampLength { get; }
        public float ShoreFadeFloor { get; }
        public bool ShoreFadeEnabled { get; }
    }

    /// <summary>
    /// The CPU mirror of `CityRiverWater.shader`'s displacement - the
    /// same three sine trains, the same still-water breathing, the same
    /// shore envelope, constant for constant.
    ///
    /// It exists for the same reason the bed has
    /// `HomeBedSurfaceDepressionModel`: the visible surface is written
    /// by something no test can sample, so the physics live twice - once
    /// where they draw and once where they can be asserted, held equal
    /// by a contract test that reads the shader source for these very
    /// constants. It is also what lets anything FLOAT: the fisherman's
    /// bobber asks this model where the water it draws actually is.
    ///
    /// Time is scene time (`Time.timeSinceLevelLoad`), which is what
    /// the shader's `_Time.y` counts; the flow speed scales it inside,
    /// as the vertex stage does.
    /// </summary>
    public static class CityWaterWaveModel
    {
        // The train ratios. The shader carries these same literals in
        // WaveField; CityWaterWaveModelTests reads the shader text and
        // fails if either side drifts.
        public const float SecondTrainAmplitudeRatio = 0.42f;
        public const float ThirdTrainAmplitudeRatio = 0.31f;
        public const float SecondTrainWaveLengthRatio = 0.53f;
        public const float ThirdTrainWaveLengthRatio = 1.71f;
        public const float FirstTrainRate = 1.00f;
        public const float SecondTrainRate = 1.63f;
        public const float ThirdTrainRate = 0.41f;

        /// <summary>
        /// The highest possible crest, in units of the profile's wave
        /// height: all three trains aligned at full breath.
        /// </summary>
        public const float CrestFactor =
            1f + SecondTrainAmplitudeRatio + ThirdTrainAmplitudeRatio;

        // Still-water breathing: three rates, none a multiple of
        // another, so the trains never pulse together.
        private const float FirstBreathRate = 0.90f;
        private const float SecondBreathRate = 0.71f;
        private const float SecondBreathPhase = 1.7f;
        private const float ThirdBreathRate = 1.37f;
        private const float ThirdBreathPhase = 3.1f;

        /// <summary>
        /// The surface's height offset from its rest plane at a world
        /// XZ and a scene time - exactly what the vertex stage adds.
        /// </summary>
        public static float SampleHeight(
            in CityWaterWaveProfile profile,
            float x,
            float z,
            float sceneTimeSeconds)
        {
            Evaluate(
                profile, x, z, sceneTimeSeconds,
                out float height, out _);
            return height;
        }

        /// <summary>
        /// The analytic d(height)/d(x, z) - what the shader builds its
        /// normal from. Exposed so a test can hold it to the finite
        /// difference of <see cref="SampleHeight"/>, shore envelope,
        /// product rule and all.
        /// </summary>
        public static Vector2 SampleSlope(
            in CityWaterWaveProfile profile,
            float x,
            float z,
            float sceneTimeSeconds)
        {
            Evaluate(
                profile, x, z, sceneTimeSeconds,
                out _, out Vector2 slope);
            return slope;
        }

        /// <summary>
        /// The shore envelope alone: 1 everywhere when the profile does
        /// not fade, the floor south of the ramp, full height north of
        /// it. What the shelf-clearance pins sample.
        /// </summary>
        public static float SampleShoreEnvelope(
            in CityWaterWaveProfile profile,
            float z)
        {
            return ShoreEnvelope(profile, z, out _);
        }

        private static void Evaluate(
            in CityWaterWaveProfile profile,
            float x,
            float z,
            float sceneTimeSeconds,
            out float height,
            out Vector2 slope)
        {
            float time = sceneTimeSeconds * profile.FlowSpeed;

            Vector2 axis = profile.FlowDirection;
            float magnitude = axis.magnitude;
            bool flowing = magnitude > 1e-4f;
            Vector2 downstream = flowing
                ? axis / magnitude
                : new Vector2(0f, 1f);
            Vector2 across =
                new Vector2(-downstream.y, downstream.x);

            float k0 = TwoPi / Mathf.Max(0.05f, profile.WaveLength);
            float k1 = TwoPi / Mathf.Max(
                0.05f,
                profile.WaveLength * SecondTrainWaveLengthRatio);
            float k2 = TwoPi / Mathf.Max(
                0.05f,
                profile.WaveLength * ThirdTrainWaveLengthRatio);

            Vector2 secondAxis = flowing
                ? downstream
                : (downstream + across).normalized;

            var position = new Vector2(x, z);
            float p0 = Vector2.Dot(position, downstream) * k0 -
                       (flowing ? time * FirstTrainRate : 0f);
            float p1 = Vector2.Dot(position, secondAxis) * k1 -
                       (flowing ? time * SecondTrainRate : 0f);
            float p2 = Vector2.Dot(position, across) * k2 +
                       (flowing ? time * ThirdTrainRate : 0f);

            float b0 = flowing
                ? 1f
                : 0.55f + 0.45f * Mathf.Sin(time * FirstBreathRate);
            float b1 = flowing
                ? 1f
                : 0.55f + 0.45f * Mathf.Sin(
                    time * SecondBreathRate + SecondBreathPhase);
            float b2 = flowing
                ? 1f
                : 0.55f + 0.45f * Mathf.Sin(
                    time * ThirdBreathRate + ThirdBreathPhase);

            float a0 = profile.WaveHeight * b0;
            float a1 = profile.WaveHeight *
                       SecondTrainAmplitudeRatio * b1;
            float a2 = profile.WaveHeight *
                       ThirdTrainAmplitudeRatio * b2;

            Vector2 rawSlope =
                downstream * (a0 * k0 * Mathf.Cos(p0)) +
                secondAxis * (a1 * k1 * Mathf.Cos(p1)) +
                across * (a2 * k2 * Mathf.Cos(p2));
            float raw = a0 * Mathf.Sin(p0) +
                        a1 * Mathf.Sin(p1) +
                        a2 * Mathf.Sin(p2);

            float envelope = ShoreEnvelope(
                profile, z, out float envelopeSlopeZ);
            height = envelope * raw;
            slope = envelope * rawSlope +
                    new Vector2(0f, envelopeSlopeZ * raw);
        }

        private static float ShoreEnvelope(
            in CityWaterWaveProfile profile,
            float z,
            out float slopeZ)
        {
            if (!profile.ShoreFadeEnabled)
            {
                slopeZ = 0f;
                return 1f;
            }

            float inverseLength =
                1f / Mathf.Max(0.01f, profile.ShoreFadeRampLength);
            float t = Mathf.Clamp01(
                (z - profile.ShoreFadeSouthZ) * inverseLength);
            float eased = t * t * (3f - 2f * t);
            slopeZ = (1f - profile.ShoreFadeFloor) *
                     6f * t * (1f - t) * inverseLength;
            return profile.ShoreFadeFloor +
                   (1f - profile.ShoreFadeFloor) * eased;
        }

        private const float TwoPi = 6.2831853f;
    }
}
