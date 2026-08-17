using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    public static class CityOpenAreaWorldBuilder
    {
        public const string RootName = "Open Area Landmarks";
        private const float SpatialChunkSize = 48f;

        private static readonly Color LakeStone =
            new Color(0.24f, 0.28f, 0.25f);
        private static readonly Color Reeds =
            new Color(0.25f, 0.34f, 0.16f);
        private static readonly Color WeatheredWood =
            new Color(0.27f, 0.20f, 0.13f);
        private static readonly Color TreeTrunk =
            new Color(0.13f, 0.10f, 0.07f);
        private static readonly Color YardTimber =
            new Color(0.24f, 0.19f, 0.14f);
        private static readonly Color YardPipe =
            new Color(0.19f, 0.20f, 0.19f);
        private static readonly Color YardSpotlightMetal =
            new Color(0.055f, 0.060f, 0.065f);
        // The single saturated note in the yard, on one dropped toy.
        private static readonly Color YardPaint =
            new Color(0.46f, 0.23f, 0.16f);

        public static GameObject Build(
            Transform parent,
            CityOpenAreaDecorationPlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);
            var batches = new Dictionary<BatchKey, List<Bounds>>();
            for (int index = 0; index < plan.Descriptors.Count; index++)
            {
                CityOpenAreaDecorationDescriptor descriptor =
                    plan.Descriptors[index];
                var key = new BatchKey(
                    Mathf.FloorToInt(
                        descriptor.Bounds.center.x / SpatialChunkSize),
                    Mathf.FloorToInt(
                        descriptor.Bounds.center.z / SpatialChunkSize),
                    descriptor.Style);
                if (!batches.TryGetValue(key, out List<Bounds> boxes))
                {
                    boxes = new List<Bounds>();
                    batches.Add(key, boxes);
                }

                boxes.Add(descriptor.Bounds);
            }

            var keys = new List<BatchKey>(batches.Keys);
            keys.Sort(BatchKey.Compare);
            for (int index = 0; index < keys.Count; index++)
            {
                BatchKey key = keys[index];
                RuntimePrimitiveFactory.CreateCombinedBoxes(
                    $"Open Area Chunk {key.X} {key.Z} {key.Style}",
                    root,
                    batches[key],
                    ResolveColor(key.Style),
                    CityOpenAreaDecorationRules.BlocksMovement(key.Style));
            }

            if (plan.YardSpotlight.HasValue)
            {
                BuildHomeYardSpotlight(
                    root,
                    plan.YardSpotlight.Value);
            }

            return root.gameObject;
        }

        private static void BuildHomeYardSpotlight(
            Transform parent,
            HomeYardSpotlightDescriptor descriptor)
        {
            Vector3 facadeNormal = Vector3.ProjectOnPlane(
                descriptor.FacadeNormal,
                Vector3.up);
            if (facadeNormal.sqrMagnitude < 0.0001f)
            {
                facadeNormal = Vector3.ProjectOnPlane(
                    descriptor.TargetPosition - descriptor.MountPosition,
                    Vector3.up);
            }

            if (facadeNormal.sqrMagnitude < 0.0001f)
            {
                facadeNormal = Vector3.forward;
            }

            facadeNormal.Normalize();
            Vector3 aimDirection =
                descriptor.TargetPosition - descriptor.MountPosition;
            if (aimDirection.sqrMagnitude < 0.0001f)
            {
                aimDirection = facadeNormal + Vector3.down;
            }

            aimDirection.Normalize();

            Transform assembly = new GameObject(
                "Home Yard Spotlight").transform;
            assembly.SetParent(parent, false);
            assembly.SetPositionAndRotation(
                descriptor.MountPosition,
                Quaternion.LookRotation(facadeNormal, Vector3.up));

            GameObject wallPlate = RuntimePrimitiveFactory.CreateBox(
                "Spotlight Wall Plate",
                assembly,
                new Vector3(0f, 0f, -0.14f),
                new Vector3(0.62f, 0.42f, 0.08f),
                YardSpotlightMetal,
                false);
            DisableFixtureShadows(wallPlate);

            GameObject bracket = RuntimePrimitiveFactory.CreateBox(
                "Spotlight Wall Bracket",
                assembly,
                new Vector3(0f, -0.03f, -0.015f),
                new Vector3(0.11f, 0.11f, 0.25f),
                YardSpotlightMetal,
                false);
            DisableFixtureShadows(bracket);

            Transform head = new GameObject(
                "Spotlight Head").transform;
            head.SetParent(assembly, false);
            head.localPosition = Vector3.zero;
            Vector3 rotationUp = Mathf.Abs(Vector3.Dot(
                aimDirection,
                Vector3.up)) > 0.98f
                    ? facadeNormal
                    : Vector3.up;
            head.rotation = Quaternion.LookRotation(
                aimDirection,
                rotationUp);

            GameObject housing = RuntimePrimitiveFactory.CreateBox(
                "Spotlight Housing",
                head,
                new Vector3(0f, 0f, -0.20f),
                new Vector3(0.50f, 0.32f, 0.42f),
                YardSpotlightMetal,
                false);
            DisableFixtureShadows(housing);

            Color lensColor = MultiplyRgb(descriptor.Color, 4.8f, 1f);
            GameObject lens = RuntimePrimitiveFactory.CreateBox(
                "Spotlight Lens",
                head,
                new Vector3(0f, 0f, 0.012f),
                new Vector3(0.39f, 0.21f, 0.035f),
                lensColor,
                CityNightResources.EmissiveMaterial,
                false);
            DisableFixtureShadows(lens);

            GameObject emitter = new GameObject(
                "Home Yard Spotlight Light");
            emitter.transform.SetParent(head, false);
            Light light = emitter.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = descriptor.Color;
            light.intensity = descriptor.Intensity;
            light.range = descriptor.Range;
            light.spotAngle = descriptor.SpotAngle;
            light.innerSpotAngle = descriptor.InnerSpotAngle;
            light.shadows = LightShadows.Hard;
            light.shadowStrength = 0.95f;
            light.shadowBias = 0.05f;
            light.shadowNormalBias = 0.25f;
            light.shadowNearPlane = 0.20f;
            UniversalAdditionalLightData additionalLightData =
                light.GetUniversalAdditionalLightData();
            // URP initializes new additional-light data at High. Its public
            // setter deliberately rejects edit-time changes, so enforce the
            // same tier only in the actual runtime composition path.
            if (Application.isPlaying)
            {
                additionalLightData.additionalLightsShadowResolutionTier =
                    UniversalAdditionalLightData
                        .AdditionalLightsShadowResolutionTierHigh;
            }
            light.renderMode = LightRenderMode.ForcePixel;
            light.bounceIntensity = 0.10f;
            light.cullingMask = ~0;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            light.enabled = true;

            GameObject haloObject = new GameObject(
                "Spotlight Source Halo");
            haloObject.transform.SetParent(emitter.transform, false);
            CityLightHalo halo =
                haloObject.AddComponent<CityLightHalo>();
            halo.Initialize(
                CityNightResources.AtmosphereMaterial,
                0.65f,
                1.80f,
                MultiplyRgb(descriptor.Color, 4.2f, 0.18f),
                MultiplyRgb(descriptor.Color, 2.1f, 0.05f));
        }

        private static Color MultiplyRgb(
            Color color,
            float multiplier,
            float alpha)
        {
            return new Color(
                color.r * multiplier,
                color.g * multiplier,
                color.b * multiplier,
                alpha);
        }

        private static void DisableFixtureShadows(GameObject fixture)
        {
            Renderer renderer = fixture.GetComponent<Renderer>();
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static Color ResolveColor(
            CityOpenAreaDecorationStyle style)
        {
            switch (style)
            {
                case CityOpenAreaDecorationStyle.LakeStone:
                    return LakeStone;
                case CityOpenAreaDecorationStyle.Reeds:
                    return Reeds;
                case CityOpenAreaDecorationStyle.WeatheredWood:
                    return WeatheredWood;
                case CityOpenAreaDecorationStyle.TreeTrunk:
                    return TreeTrunk;
                case CityOpenAreaDecorationStyle.YardTimber:
                    return YardTimber;
                case CityOpenAreaDecorationStyle.YardPipe:
                    return YardPipe;
                case CityOpenAreaDecorationStyle.YardPaint:
                    return YardPaint;
                default:
                    throw new ArgumentOutOfRangeException(nameof(style));
            }
        }

        private readonly struct BatchKey : IEquatable<BatchKey>
        {
            public BatchKey(
                int x,
                int z,
                CityOpenAreaDecorationStyle style)
            {
                X = x;
                Z = z;
                Style = style;
            }

            public int X { get; }
            public int Z { get; }
            public CityOpenAreaDecorationStyle Style { get; }

            public bool Equals(BatchKey other)
            {
                return X == other.X &&
                       Z == other.Z &&
                       Style == other.Style;
            }

            public override bool Equals(object obj)
            {
                return obj is BatchKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = X;
                    hash = (hash * 397) ^ Z;
                    return (hash * 397) ^ (int)Style;
                }
            }

            public static int Compare(BatchKey left, BatchKey right)
            {
                int x = left.X.CompareTo(right.X);
                if (x != 0)
                {
                    return x;
                }

                int z = left.Z.CompareTo(right.Z);
                return z != 0
                    ? z
                    : left.Style.CompareTo(right.Style);
            }
        }
    }
}
