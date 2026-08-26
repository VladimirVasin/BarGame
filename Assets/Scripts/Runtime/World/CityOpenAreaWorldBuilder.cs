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
            var importedBatches = new Dictionary<
                BatchKey,
                List<RuntimeMeshPlacement>>();
            var importedIds = new HashSet<string>(StringComparer.Ordinal);
            var collisionBounds = new List<Bounds>(plan.Descriptors.Count);
            CityMiscAssetProvider miscProvider =
                CityMiscAssetProvider.Load();

            TryAppendYardAssembly(
                plan.Descriptors,
                CityOpenAreaDecorationKind.YardDeadTree,
                CityMiscKind.YardDeadTree,
                miscProvider,
                importedBatches,
                importedIds);
            TryAppendYardAssembly(
                plan.Descriptors,
                CityOpenAreaDecorationKind.YardBench,
                CityMiscKind.YardBench,
                miscProvider,
                importedBatches,
                importedIds);
            TryAppendYardAssembly(
                plan.Descriptors,
                CityOpenAreaDecorationKind.YardCarpetFrame,
                CityMiscKind.YardCarpetFrame,
                miscProvider,
                importedBatches,
                importedIds);
            TryAppendYardAssembly(
                plan.Descriptors,
                CityOpenAreaDecorationKind.YardSandpit,
                CityMiscKind.YardSandpit,
                miscProvider,
                importedBatches,
                importedIds);
            TryAppendYardAssembly(
                plan.Descriptors,
                CityOpenAreaDecorationKind.YardChildToy,
                CityMiscKind.YardChildToy,
                miscProvider,
                importedBatches,
                importedIds);
            TryAppendYardAssembly(
                plan.Descriptors,
                CityOpenAreaDecorationKind.YardDeadLamp,
                CityMiscKind.YardDeadLamp,
                miscProvider,
                importedBatches,
                importedIds);
            TryAppendYardAssembly(
                plan.Descriptors,
                CityOpenAreaDecorationKind.YardBin,
                CityMiscKind.YardBin,
                miscProvider,
                importedBatches,
                importedIds);
            TryAppendYardAssembly(
                plan.Descriptors,
                CityOpenAreaDecorationKind.YardBottle,
                CityMiscKind.YardBottle,
                miscProvider,
                importedBatches,
                importedIds);

            for (int index = 0; index < plan.Descriptors.Count; index++)
            {
                CityOpenAreaDecorationDescriptor descriptor =
                    plan.Descriptors[index];
                if (descriptor.BlocksMovement)
                {
                    collisionBounds.Add(descriptor.Bounds);
                }

                if (importedIds.Contains(descriptor.StableId))
                {
                    continue;
                }

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
                    false);
            }

            keys = new List<BatchKey>(importedBatches.Keys);
            keys.Sort(BatchKey.Compare);
            for (int index = 0; index < keys.Count; index++)
            {
                BatchKey key = keys[index];
                RuntimePrimitiveFactory.CreateCombinedMeshes(
                    $"Imported Open Area Chunk {key.X} {key.Z} " +
                    $"{key.Style}",
                    root,
                    importedBatches[key],
                    ResolveColor(key.Style));
            }

            if (collisionBounds.Count > 0)
            {
                Transform collisionRoot = new GameObject(
                    "Open Area Collision Proxies").transform;
                collisionRoot.SetParent(root, false);
                CityStaticCollisionBuilder.AddBoxColliders(
                    collisionRoot,
                    collisionBounds);
            }

            if (plan.YardSpotlight.HasValue)
            {
                BuildHomeYardSpotlight(
                    root,
                    plan.YardSpotlight.Value,
                    miscProvider);
            }

            return root.gameObject;
        }

        private static void TryAppendYardAssembly(
            IReadOnlyList<CityOpenAreaDecorationDescriptor> descriptors,
            CityOpenAreaDecorationKind decorationKind,
            CityMiscKind miscKind,
            CityMiscAssetProvider provider,
            IDictionary<BatchKey, List<RuntimeMeshPlacement>> batches,
            ISet<string> importedIds)
        {
            if (!TryResolveYardAssemblyOrigin(
                    descriptors,
                    decorationKind,
                    out Vector3 origin) ||
                !TryGetImportedParts(
                    provider,
                    miscKind,
                    out List<CityMiscMeshPart> parts))
            {
                return;
            }

            for (int index = 0; index < parts.Count; index++)
            {
                CityMiscMeshPart part = parts[index];
                CityOpenAreaDecorationStyle style = ResolveYardStyle(
                    decorationKind,
                    part.Role);
                var key = new BatchKey(
                    Mathf.FloorToInt(origin.x / SpatialChunkSize),
                    Mathf.FloorToInt(origin.z / SpatialChunkSize),
                    style);
                if (!batches.TryGetValue(
                        key,
                        out List<RuntimeMeshPlacement> placements))
                {
                    placements = new List<RuntimeMeshPlacement>();
                    batches.Add(key, placements);
                }

                placements.Add(new RuntimeMeshPlacement(
                    part.Mesh,
                    origin,
                    Quaternion.identity));
            }

            for (int index = 0; index < descriptors.Count; index++)
            {
                CityOpenAreaDecorationDescriptor descriptor =
                    descriptors[index];
                if (descriptor.Kind == decorationKind)
                {
                    importedIds.Add(descriptor.StableId);
                }
            }
        }

        private static bool TryResolveYardAssemblyOrigin(
            IReadOnlyList<CityOpenAreaDecorationDescriptor> descriptors,
            CityOpenAreaDecorationKind kind,
            out Vector3 origin)
        {
            string suffix;
            Vector3 offset;
            switch (kind)
            {
                case CityOpenAreaDecorationKind.YardDeadTree:
                    suffix = "-tree-trunk";
                    offset = Vector3.zero;
                    break;
                case CityOpenAreaDecorationKind.YardBench:
                    suffix = "-bench-seat";
                    offset = new Vector3(0f, -0.47f, 0f);
                    break;
                case CityOpenAreaDecorationKind.YardCarpetFrame:
                    suffix = "-carpet-frame-header";
                    offset = new Vector3(0f, -1.62f, 0f);
                    break;
                case CityOpenAreaDecorationKind.YardSandpit:
                    suffix = "-sandpit-edge-a";
                    offset = Vector3.zero;
                    break;
                case CityOpenAreaDecorationKind.YardChildToy:
                    suffix = "-sandpit-toy";
                    offset = Vector3.zero;
                    break;
                case CityOpenAreaDecorationKind.YardDeadLamp:
                    suffix = "-dead-lamp-post";
                    offset = Vector3.zero;
                    break;
                case CityOpenAreaDecorationKind.YardBin:
                    suffix = "-bin-body";
                    offset = Vector3.zero;
                    break;
                case CityOpenAreaDecorationKind.YardBottle:
                    suffix = "-bench-bottle";
                    offset = Vector3.zero;
                    break;
                default:
                    origin = default;
                    return false;
            }

            for (int index = 0; index < descriptors.Count; index++)
            {
                CityOpenAreaDecorationDescriptor descriptor =
                    descriptors[index];
                if (descriptor.Kind != kind ||
                    !descriptor.StableId.EndsWith(
                        suffix,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                Bounds bounds = descriptor.Bounds;
                origin = new Vector3(
                    bounds.center.x,
                    bounds.min.y,
                    bounds.center.z) + offset;
                // The bench and frame offsets were stated from their
                // authored centres; every other anchor is its ground base.
                if (kind == CityOpenAreaDecorationKind.YardBench ||
                    kind == CityOpenAreaDecorationKind.YardCarpetFrame)
                {
                    origin.y = bounds.center.y + offset.y;
                }

                return true;
            }

            origin = default;
            return false;
        }

        private static CityOpenAreaDecorationStyle ResolveYardStyle(
            CityOpenAreaDecorationKind kind,
            CityMiscMeshRole role)
        {
            switch (kind)
            {
                case CityOpenAreaDecorationKind.YardDeadTree:
                    return CityOpenAreaDecorationStyle.TreeTrunk;
                case CityOpenAreaDecorationKind.YardBench:
                    return role == CityMiscMeshRole.Timber
                        ? CityOpenAreaDecorationStyle.YardTimber
                        : CityOpenAreaDecorationStyle.YardPipe;
                case CityOpenAreaDecorationKind.YardCarpetFrame:
                case CityOpenAreaDecorationKind.YardDeadLamp:
                case CityOpenAreaDecorationKind.YardBin:
                    return CityOpenAreaDecorationStyle.YardPipe;
                case CityOpenAreaDecorationKind.YardSandpit:
                case CityOpenAreaDecorationKind.YardBottle:
                    return CityOpenAreaDecorationStyle.YardTimber;
                case CityOpenAreaDecorationKind.YardChildToy:
                    return CityOpenAreaDecorationStyle.YardPaint;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        "Unsupported imported yard assembly.");
            }
        }

        private static bool TryGetImportedParts(
            CityMiscAssetProvider provider,
            CityMiscKind kind,
            out List<CityMiscMeshPart> parts)
        {
            parts = null;
            if (provider == null ||
                !CityMiscAssetProvider.Supports(kind))
            {
                return false;
            }

            try
            {
                int partCount = CityMiscAssetProvider.GetPartCount(kind);
                if (partCount < 1)
                {
                    return false;
                }

                var result = new List<CityMiscMeshPart>(partCount);
                for (int index = 0; index < partCount; index++)
                {
                    CityMiscMeshPart part = provider.GetPartOrThrow(
                        kind,
                        0,
                        index);
                    if (part.Mesh == null)
                    {
                        return false;
                    }

                    result.Add(part);
                }

                parts = result;
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        private static void BuildHomeYardSpotlight(
            Transform parent,
            HomeYardSpotlightDescriptor descriptor,
            CityMiscAssetProvider miscProvider)
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

            if (!TryBuildImportedFixture(
                    assembly,
                    miscProvider,
                    CityMiscKind.YardSpotlightWallMount,
                    "Imported Spotlight Wall Mount"))
            {
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
            }

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

            if (!TryBuildImportedFixture(
                    head,
                    miscProvider,
                    CityMiscKind.YardSpotlightHeadShell,
                    "Imported Spotlight Head Shell"))
            {
                GameObject housing = RuntimePrimitiveFactory.CreateBox(
                    "Spotlight Housing",
                    head,
                    new Vector3(0f, 0f, -0.20f),
                    new Vector3(0.50f, 0.32f, 0.42f),
                    YardSpotlightMetal,
                    false);
                DisableFixtureShadows(housing);
            }

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

        private static bool TryBuildImportedFixture(
            Transform parent,
            CityMiscAssetProvider provider,
            CityMiscKind kind,
            string name)
        {
            if (!TryGetImportedParts(
                    provider,
                    kind,
                    out List<CityMiscMeshPart> parts))
            {
                return false;
            }

            for (int index = 0; index < parts.Count; index++)
            {
                CityMiscMeshPart part = parts[index];
                GameObject fixture =
                    RuntimePrimitiveFactory.CreateCombinedMeshes(
                        $"{name} {part.Role}",
                        parent,
                        new[]
                        {
                            new RuntimeMeshPlacement(
                                part.Mesh,
                                Vector3.zero,
                                Quaternion.identity)
                        },
                        YardSpotlightMetal);
                DisableFixtureShadows(fixture);
            }

            return true;
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
