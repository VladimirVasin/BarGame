using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One burning candle, or one cluster of them on a single fixture:
    /// the flame geometry and the light it casts, driven together so
    /// the pool on the floor moves with the thing making it.
    ///
    /// The flames were static meshes and static lights, which is what a
    /// church interior most obviously is not. This is a simulation only
    /// in the sense that matters here - it is deterministic layered
    /// waves rather than physics - but it is unrepeating to the eye,
    /// every fixture is on its own phase, and the light follows the
    /// flame instead of being animated independently of it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChurchCandleFlame : MonoBehaviour
    {
        /// <summary>
        /// How far the cast light swings. Deliberately far smaller than
        /// the flame's own movement: a candle's tip dances, the pool it
        /// throws only breathes. Overdo this and the room strobes.
        /// </summary>
        public const float LightAmplitude = 0.14f;

        /// <summary>The flame's own stretch, which may be theatrical.</summary>
        public const float StretchAmplitude = 0.28f;
        public const float NarrowAmplitude = 0.16f;
        public const float LeanMetres = 0.013f;
        public const float RiseMetres = 0.018f;

        /// <summary>
        /// The guttering: a rare, deeper dip, as if something moved the
        /// air. Zero almost always, and never so deep that a fixture
        /// goes dark and pops back.
        /// </summary>
        public const float GutterDepth = 0.22f;
        public const float GutterSharpness = 10f;
        public const float MinimumFlicker = 0.35f;

        public static readonly Color EmberColor =
            new Color(1f, 0.42f, 0.14f);

        private Light candleLight;
        private Transform[] flames = Array.Empty<Transform>();
        private Vector3[] restScales = Array.Empty<Vector3>();
        private Vector3[] restPositions = Array.Empty<Vector3>();
        private Quaternion[] restRotations = Array.Empty<Quaternion>();
        private float phase;
        private float gutterPhase;
        private float speed = 1f;

        /// <summary>
        /// What the fixture would be at, before it flickers. The day
        /// and night schedule writes THIS, never the light itself, or
        /// the two would fight for the same field every frame.
        /// </summary>
        public float BaseIntensity { get; set; }

        public Color BaseColor { get; set; } = Color.white;

        /// <summary>The last multiplier applied, for tests.</summary>
        public float Flicker { get; private set; } = 1f;

        public Light Light => candleLight;
        public int FlameCount => flames.Length;

        public void Configure(
            Light light,
            IReadOnlyList<Transform> flameTransforms,
            uint seed)
        {
            candleLight = light != null
                ? light
                : throw new ArgumentNullException(nameof(light));
            BaseIntensity = light.intensity;
            BaseColor = light.color;

            int count = flameTransforms?.Count ?? 0;
            flames = new Transform[count];
            restScales = new Vector3[count];
            restPositions = new Vector3[count];
            restRotations = new Quaternion[count];
            for (int index = 0; index < count; index++)
            {
                Transform flame = flameTransforms[index];
                flames[index] = flame;
                if (flame == null)
                {
                    continue;
                }

                restScales[index] = flame.localScale;
                restPositions[index] = flame.localPosition;
                restRotations[index] = flame.localRotation;
            }

            // A fixture's phase has to come from something stable, or
            // every candle in the church agrees on when to gutter and
            // the whole room pulses as one.
            uint hash = seed * 2654435761u;
            hash ^= hash >> 15;
            phase = (hash % 10000u) * 0.001f * Mathf.PI * 2f;
            gutterPhase = ((hash >> 8) % 10000u) * 0.001f * Mathf.PI * 2f;
            speed = 2.1f + ((hash >> 16) % 100u) * 0.006f;
        }

        /// <summary>
        /// Layered waves whose periods share no common multiple, so the
        /// pattern does not visibly repeat. Cheap enough to run per
        /// fixture per frame without thinking about it.
        /// </summary>
        public static float Wave(float t)
        {
            return (0.55f * Mathf.Sin(t)) +
                   (0.30f * Mathf.Sin((t * 2.37f) + 1.7f)) +
                   (0.15f * Mathf.Sin((t * 5.11f) + 4.2f));
        }

        private void LateUpdate()
        {
            if (candleLight == null)
            {
                return;
            }

            // Scaled time on purpose: a paused game freezes the flames
            // with everything else.
            float t = (Time.time * speed) + phase;
            float gutter = GutterDepth * Mathf.Pow(
                Mathf.Max(
                    0f,
                    Mathf.Sin((Time.time * 0.31f) + gutterPhase)),
                GutterSharpness);
            float wave = Wave(t);
            Flicker = Mathf.Max(
                MinimumFlicker,
                1f + (LightAmplitude * wave) - gutter);
            candleLight.intensity = BaseIntensity * Flicker;

            // A dipping flame reddens; a strong one does not go bluer
            // than the wax it started from.
            candleLight.color = Color.Lerp(
                BaseColor,
                EmberColor,
                Mathf.Clamp01(gutter * (1f / GutterDepth) * 0.75f));

            for (int index = 0; index < flames.Length; index++)
            {
                Transform flame = flames[index];
                if (flame == null)
                {
                    continue;
                }

                float own = t + (index * 1.37f);
                float stretch = Wave(own);
                float sway = Wave((own * 0.63f) + 2.4f);
                Vector3 rest = restScales[index];
                flame.localScale = new Vector3(
                    rest.x * (1f - (NarrowAmplitude * stretch)),
                    rest.y * (1f + (StretchAmplitude * stretch)),
                    rest.z * (1f - (NarrowAmplitude * stretch)));
                flame.localPosition = restPositions[index] +
                    new Vector3(
                        sway * LeanMetres,
                        Mathf.Abs(stretch) * RiseMetres,
                        Wave((own * 0.47f) + 5.1f) * LeanMetres);
                flame.localRotation = restRotations[index] *
                    Quaternion.Euler(
                        sway * 7f,
                        0f,
                        Wave((own * 0.51f) + 0.9f) * 7f);
            }
        }
    }
}
