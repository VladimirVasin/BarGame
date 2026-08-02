using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class CityNightAtmosphere : MonoBehaviour
    {
        public const int MaximumRealtimeLights = 12;

        private const float ReassignmentInterval = 0.35f;
        private const float ReassignmentDistance = 1.25f;

        private static readonly Color StreetLightColor =
            new Color(1f, 0.72f, 0.42f);
        private static readonly Color BarLightColor =
            new Color(1f, 0.76f, 0.48f);
        private static readonly Color StreetHaloInner =
            new Color(4.0f, 2.0f, 0.55f, 0.18f);
        private static readonly Color StreetHaloOuter =
            new Color(2.2f, 1.25f, 0.42f, 0.045f);
        private static readonly Color BarHaloInner =
            new Color(4.2f, 2.3f, 0.78f, 0.20f);
        private static readonly Color BarHaloOuter =
            new Color(2.4f, 1.45f, 0.58f, 0.055f);

        private Transform player;
        private Transform[] lampAnchors = Array.Empty<Transform>();
        private Light[] streetLightPool = Array.Empty<Light>();
        private Light[] barLights = Array.Empty<Light>();
        private int[] selectedAnchorIndices = Array.Empty<int>();
        private float[] selectedAnchorDistances = Array.Empty<float>();
        private float nextReassignmentTime;
        private Vector3 lastAssignmentPosition;

        public IReadOnlyList<Transform> LampAnchors => lampAnchors;
        public IReadOnlyList<Light> StreetLightPool => streetLightPool;
        public IReadOnlyList<Light> BarLights => barLights;
        public int RealtimeLightCount =>
            streetLightPool.Length + barLights.Length;

        public void Initialize(
            Transform playerTransform,
            IReadOnlyList<Transform> streetLampAnchors,
            IReadOnlyList<Vector3> barLightPositions)
        {
            player = playerTransform != null
                ? playerTransform
                : throw new ArgumentNullException(nameof(playerTransform));
            if (streetLampAnchors == null)
            {
                throw new ArgumentNullException(nameof(streetLampAnchors));
            }

            if (barLightPositions == null)
            {
                throw new ArgumentNullException(nameof(barLightPositions));
            }

            lampAnchors = CopyAnchors(streetLampAnchors);
            int barLightCount = Mathf.Min(
                barLightPositions.Count,
                MaximumRealtimeLights);
            barLights = new Light[barLightCount];
            for (int index = 0; index < barLightCount; index++)
            {
                barLights[index] = CreateLight(
                    $"Bar Entrance Light {index + 1}",
                    barLightPositions[index],
                    BarLightColor,
                    LightType.Point,
                    8f,
                    7.5f,
                    1.05f,
                    2.85f,
                    BarHaloInner,
                    BarHaloOuter);
            }

            int streetLightCount = Mathf.Min(
                lampAnchors.Length,
                MaximumRealtimeLights - barLightCount);
            streetLightPool = new Light[streetLightCount];
            selectedAnchorIndices = new int[streetLightCount];
            selectedAnchorDistances = new float[streetLightCount];
            for (int index = 0; index < streetLightCount; index++)
            {
                streetLightPool[index] = CreateLight(
                    $"Pooled Street Light {index + 1}",
                    transform.position,
                    StreetLightColor,
                    LightType.Spot,
                    12f,
                    10.5f,
                    0.80f,
                    2.20f,
                    StreetHaloInner,
                    StreetHaloOuter);
            }

            RefreshStreetLights(true);
        }

        private void Update()
        {
            RefreshStreetLights(false);
        }

        public void RefreshImmediate()
        {
            RefreshStreetLights(true);
        }

        private void RefreshStreetLights(bool force)
        {
            if (player == null || streetLightPool.Length == 0)
            {
                return;
            }

            float now = Time.unscaledTime;
            Vector3 playerPosition = player.position;
            float movementSquared =
                (playerPosition - lastAssignmentPosition).sqrMagnitude;
            if (!force &&
                now < nextReassignmentTime &&
                movementSquared <
                ReassignmentDistance * ReassignmentDistance)
            {
                return;
            }

            for (int index = 0; index < selectedAnchorIndices.Length; index++)
            {
                selectedAnchorIndices[index] = -1;
                selectedAnchorDistances[index] = float.PositiveInfinity;
            }

            for (int anchorIndex = 0;
                 anchorIndex < lampAnchors.Length;
                 anchorIndex++)
            {
                Transform anchor = lampAnchors[anchorIndex];
                float distance = anchor != null
                    ? (anchor.position - playerPosition).sqrMagnitude
                    : float.PositiveInfinity;
                InsertNearest(anchorIndex, distance);
            }

            for (int index = 0; index < streetLightPool.Length; index++)
            {
                int anchorIndex = selectedAnchorIndices[index];
                Light light = streetLightPool[index];
                bool hasAnchor =
                    anchorIndex >= 0 &&
                    anchorIndex < lampAnchors.Length &&
                    lampAnchors[anchorIndex] != null;
                light.enabled = hasAnchor;
                if (hasAnchor)
                {
                    Transform anchor = lampAnchors[anchorIndex];
                    light.transform.SetPositionAndRotation(
                        anchor.position,
                        CreateStreetLightRotation(anchor));
                }
            }

            lastAssignmentPosition = playerPosition;
            nextReassignmentTime = now + ReassignmentInterval;
        }

        private void InsertNearest(int anchorIndex, float distance)
        {
            for (int slot = 0; slot < selectedAnchorDistances.Length; slot++)
            {
                if (distance >= selectedAnchorDistances[slot])
                {
                    continue;
                }

                for (int move = selectedAnchorDistances.Length - 1;
                     move > slot;
                     move--)
                {
                    selectedAnchorDistances[move] =
                        selectedAnchorDistances[move - 1];
                    selectedAnchorIndices[move] =
                        selectedAnchorIndices[move - 1];
                }

                selectedAnchorDistances[slot] = distance;
                selectedAnchorIndices[slot] = anchorIndex;
                return;
            }
        }

        private Light CreateLight(
            string lightName,
            Vector3 position,
            Color color,
            LightType lightType,
            float intensity,
            float range,
            float innerHaloSize,
            float outerHaloSize,
            Color innerHaloColor,
            Color outerHaloColor)
        {
            GameObject lightObject = new GameObject(lightName);
            lightObject.transform.SetParent(transform, true);
            lightObject.transform.position = position;
            Light light = lightObject.AddComponent<Light>();
            light.type = lightType;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.bounceIntensity = 0.15f;

            if (lightType == LightType.Spot)
            {
                light.spotAngle = 105f;
                light.innerSpotAngle = 55f;
            }

            GameObject haloObject = new GameObject("Fog Light Halo");
            haloObject.transform.SetParent(lightObject.transform, false);
            CityLightHalo halo =
                haloObject.AddComponent<CityLightHalo>();
            halo.Initialize(
                CityNightResources.AtmosphereMaterial,
                innerHaloSize,
                outerHaloSize,
                innerHaloColor,
                outerHaloColor);
            return light;
        }

        private static Quaternion CreateStreetLightRotation(Transform anchor)
        {
            Vector3 roadDirection = Vector3.ProjectOnPlane(
                anchor.forward,
                Vector3.up).normalized;
            if (roadDirection.sqrMagnitude < 0.5f)
            {
                roadDirection = Vector3.forward;
            }

            Vector3 lightDirection =
                (Vector3.down * 0.92f + roadDirection * 0.38f).normalized;
            return Quaternion.LookRotation(lightDirection, Vector3.up);
        }

        private static Transform[] CopyAnchors(
            IReadOnlyList<Transform> anchors)
        {
            var result = new Transform[anchors.Count];
            for (int index = 0; index < anchors.Count; index++)
            {
                result[index] = anchors[index] != null
                    ? anchors[index]
                    : throw new ArgumentException(
                        "Street lamp anchors cannot contain null.",
                        nameof(anchors));
            }

            return result;
        }
    }
}
