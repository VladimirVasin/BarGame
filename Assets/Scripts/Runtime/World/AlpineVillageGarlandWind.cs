using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Bounded presentation rules for one village garland span. The wire is
    /// fixed at both anchors; only its free middle leans and flutters. This is
    /// intentionally much smaller than cloth motion: an electrical cord may
    /// show the gale, but it may not whip through roofs or detach from posts.
    /// </summary>
    public static class AlpineVillageGarlandWindRules
    {
        public const float MinimumMidpointPush = 0.16f;
        public const float MaximumMidpointPush = 0.30f;
        public const float MinimumFlutterLift = 0.020f;
        public const float MaximumFlutterLift = 0.055f;
        public const float MaximumDisplacement = 0.33f;

        private const float GustFrequency = 2.1f;
        private const float FlutterFrequency = 5.1f;

        public static Vector3 EvaluateOffset(
            in WindSample wind,
            float spanPosition01,
            float elapsedSeconds,
            float phase)
        {
            float amount = Mathf.Clamp01(spanPosition01);
            float envelope = Mathf.Sin(Mathf.PI * amount);
            envelope *= envelope;
            if (envelope <= 0.00001f)
            {
                return Vector3.zero;
            }

            float gale = Mathf.InverseLerp(
                AlpineVillageWeatherRules.WindFloor,
                AlpineVillageWeatherRules.WindCeiling,
                wind.Strength01);
            float midpointPush = Mathf.Lerp(
                MinimumMidpointPush,
                MaximumMidpointPush,
                gale);
            float gust =
                0.82f +
                Mathf.Sin(elapsedSeconds * GustFrequency + phase) * 0.16f +
                Mathf.Sin(
                    elapsedSeconds * FlutterFrequency + phase * 1.71f) *
                0.07f;
            gust = Mathf.Clamp(gust, 0.55f, 1.05f);

            float lift = Mathf.Lerp(
                MinimumFlutterLift,
                MaximumFlutterLift,
                gale);
            float flutter = Mathf.Sin(
                elapsedSeconds * 4.6f +
                phase * 1.37f +
                amount * 1.2f);

            Vector3 offset =
                wind.HorizontalDirection * (midpointPush * gust) +
                Vector3.up * (lift * flutter);
            return Vector3.ClampMagnitude(
                offset * envelope,
                MaximumDisplacement);
        }
    }

    /// <summary>
    /// Deforms the two already-batched meshes of one garland span after the
    /// weather controller has applied the village shaper. The render meshes
    /// are unique runtime meshes and carry no colliders, so this changes only
    /// the visible cord, bulbs and the real lamp that belongs to their middle.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(90)]
    public sealed class AlpineVillageGarlandWind : MonoBehaviour
    {
        private DeformableMesh wire;
        private DeformableMesh bulbs;
        private Transform semanticAnchor;
        private Transform lamp;
        private Vector3 semanticAnchorBaseLocalPosition;
        private Vector3 lampBaseLocalPosition;
        private CityWeatherController weather;
        private float phase;

        public bool IsConfigured { get; private set; }
        public bool IsWeatherBound => weather != null;
        public Vector3 CurrentMidpointOffset { get; private set; }

        public void Configure(
            MeshFilter wireFilter,
            MeshFilter bulbFilter,
            Transform semantic,
            Transform realLamp,
            Vector3 leftWorld,
            Vector3 rightWorld,
            float motionPhase)
        {
            if (IsConfigured)
            {
                throw new InvalidOperationException(
                    "The Alpine Village garland wind is already configured.");
            }

            wire = new DeformableMesh(
                wireFilter,
                leftWorld,
                rightWorld);
            bulbs = new DeformableMesh(
                bulbFilter,
                leftWorld,
                rightWorld);
            semanticAnchor = semantic != null
                ? semantic
                : throw new ArgumentNullException(nameof(semantic));
            lamp = realLamp;
            semanticAnchorBaseLocalPosition = semanticAnchor.localPosition;
            if (lamp != null)
            {
                lampBaseLocalPosition = lamp.localPosition;
            }

            phase = motionPhase;
            IsConfigured = true;
        }

        public void BindWeather(CityWeatherController weatherController)
        {
            if (!IsConfigured)
            {
                throw new InvalidOperationException(
                    "Configure the garland geometry before binding weather.");
            }

            weather = weatherController != null
                ? weatherController
                : throw new ArgumentNullException(nameof(weatherController));
            ApplyWind(Time.unscaledTime);
        }

        private void LateUpdate()
        {
            if (!IsConfigured || weather == null)
            {
                return;
            }

            ApplyWind(Time.unscaledTime);
        }

        private void ApplyWind(float elapsedSeconds)
        {
            WindSample wind = weather.CurrentWind;
            wire.Apply(wind, elapsedSeconds, phase);
            bulbs.Apply(wind, elapsedSeconds, phase);
            CurrentMidpointOffset =
                AlpineVillageGarlandWindRules.EvaluateOffset(
                    wind,
                    0.5f,
                    elapsedSeconds,
                    phase);
            ApplyWorldOffset(
                semanticAnchor,
                semanticAnchorBaseLocalPosition,
                CurrentMidpointOffset);
            if (lamp != null)
            {
                ApplyWorldOffset(
                    lamp,
                    lampBaseLocalPosition,
                    CurrentMidpointOffset);
            }
        }

        private void OnDisable()
        {
            wire?.Restore();
            bulbs?.Restore();
            if (semanticAnchor != null)
            {
                semanticAnchor.localPosition =
                    semanticAnchorBaseLocalPosition;
            }

            if (lamp != null)
            {
                lamp.localPosition = lampBaseLocalPosition;
            }

            CurrentMidpointOffset = Vector3.zero;
        }

        private static void ApplyWorldOffset(
            Transform target,
            Vector3 baseLocalPosition,
            Vector3 worldOffset)
        {
            Transform parent = target.parent;
            target.localPosition = parent != null
                ? baseLocalPosition + parent.InverseTransformVector(worldOffset)
                : baseLocalPosition + worldOffset;
        }

        private sealed class DeformableMesh
        {
            private readonly Transform owner;
            private readonly Mesh mesh;
            private readonly Vector3[] originalVertices;
            private readonly Vector3[] workingVertices;
            private readonly float[] spanPositions;

            public DeformableMesh(
                MeshFilter filter,
                Vector3 leftWorld,
                Vector3 rightWorld)
            {
                if (filter == null)
                {
                    throw new ArgumentNullException(nameof(filter));
                }

                owner = filter.transform;
                // The factory already gives every span a unique generated
                // mesh and its RuntimeGeneratedMeshOwner owns that exact
                // object. Keep using sharedMesh: MeshFilter.mesh would make
                // an unowned clone on the first access and leak it each time
                // the village scene is loaded.
                mesh = filter.sharedMesh;
                if (mesh == null)
                {
                    throw new InvalidOperationException(
                        $"Garland mesh '{filter.name}' is missing.");
                }

                mesh.MarkDynamic();
                originalVertices = mesh.vertices;
                workingVertices = new Vector3[originalVertices.Length];
                spanPositions = new float[originalVertices.Length];
                Vector3 left = owner.InverseTransformPoint(leftWorld);
                Vector3 right = owner.InverseTransformPoint(rightWorld);
                Vector3 chord = right - left;
                float chordLengthSquared = chord.sqrMagnitude;
                if (chordLengthSquared <= 0.0001f)
                {
                    throw new ArgumentException(
                        "Garland anchors must define a non-zero span.");
                }

                for (int index = 0;
                     index < originalVertices.Length;
                     index++)
                {
                    spanPositions[index] = Mathf.Clamp01(
                        Vector3.Dot(
                            originalVertices[index] - left,
                            chord) /
                        chordLengthSquared);
                }
            }

            public void Apply(
                in WindSample wind,
                float elapsedSeconds,
                float phase)
            {
                for (int index = 0;
                     index < originalVertices.Length;
                     index++)
                {
                    Vector3 worldOffset =
                        AlpineVillageGarlandWindRules.EvaluateOffset(
                            wind,
                            spanPositions[index],
                            elapsedSeconds,
                            phase);
                    workingVertices[index] =
                        originalVertices[index] +
                        owner.InverseTransformVector(worldOffset);
                }

                mesh.vertices = workingVertices;
                mesh.RecalculateBounds();
            }

            public void Restore()
            {
                if (mesh == null)
                {
                    return;
                }

                mesh.vertices = originalVertices;
                mesh.RecalculateBounds();
            }
        }
    }
}
