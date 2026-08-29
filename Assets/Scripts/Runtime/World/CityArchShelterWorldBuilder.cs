using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public sealed class CityArchShelterWorldResult
    {
        internal CityArchShelterWorldResult(
            GameObject root,
            Transform structureRoot,
            IList<Transform> propRoots,
            IList<Transform> residentRoots,
            Collider platformCollider,
            IList<Collider> rainShelterColliders)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            StructureRoot = structureRoot;
            PropRoots = new ReadOnlyCollection<Transform>(
                new List<Transform>(propRoots));
            ResidentRoots = new ReadOnlyCollection<Transform>(
                new List<Transform>(residentRoots));
            PlatformCollider = platformCollider;
            UpperLandingCollider = platformCollider;
            RainShelterColliders = new ReadOnlyCollection<Collider>(
                new List<Collider>(rainShelterColliders));
        }

        public GameObject Root { get; }
        public Transform StructureRoot { get; }
        public IReadOnlyList<Transform> PropRoots { get; }
        public IReadOnlyList<Transform> ResidentRoots { get; }
        public Collider PlatformCollider { get; }
        // The logical upper landing is contained by the one physical
        // platform collider, avoiding overlapping coplanar colliders.
        public Collider UpperLandingCollider { get; }
        public IReadOnlyList<Collider> RainShelterColliders { get; }
    }

    public static class CityArchShelterWorldBuilder
    {
        public const string RootName = "City Arch Shelter";
        public const string StructureRootName =
            "city-arch-shelter-10-05-11-05-structure";
        public const string CollisionRootName =
            "City Arch Shelter Collision";
        public const string RainRootName =
            "City Arch Shelter Rain Volumes";
        public const int IgnoreRaycastLayer = 2;

        private static readonly Color MasonryColor =
            new Color(0.235f, 0.220f, 0.205f, 1f);
        private static readonly Color IndustrialColor =
            new Color(0.205f, 0.155f, 0.115f, 1f);
        private static readonly Color StreetColor =
            new Color(0.105f, 0.095f, 0.085f, 1f);
        private static readonly Color TimberColor =
            new Color(0.210f, 0.135f, 0.075f, 1f);
        private static readonly Color ResidentialColor =
            new Color(0.245f, 0.205f, 0.175f, 1f);
        private static readonly Color SkinColor =
            new Color(0.330f, 0.235f, 0.190f, 1f);
        private static readonly Color FlameColor =
            new Color(4.2f, 1.30f, 0.27f, 1f);
        private static readonly Color FlameOuterColor =
            new Color(2.8f, 0.50f, 0.055f, 1f);
        private static readonly Color FlameLeftColor =
            new Color(3.5f, 0.82f, 0.10f, 1f);
        private static readonly Color FlameRightColor =
            new Color(2.45f, 0.37f, 0.032f, 1f);
        private static readonly Color EmberColor =
            new Color(1.55f, 0.14f, 0.018f, 1f);
        private static readonly Color GroundSpillColor =
            new Color(2.8f, 0.28f, 0.045f, 0.28f);
        private static readonly int EdgePowerId =
            Shader.PropertyToID("_EdgePower");
        private static readonly int NoiseStrengthId =
            Shader.PropertyToID("_NoiseStrength");
        private static readonly int SoftParticleDistanceId =
            Shader.PropertyToID("_SoftParticleDistance");

        public static CityArchShelterWorldResult Build(
            Transform parent,
            CityLayout layout,
            CityArchShelterPlan plan)
        {
            return Build(
                parent,
                layout,
                plan,
                plan != null && plan.IsEnabled
                    ? CityMiscAssetProvider.LoadOrThrow()
                    : null);
        }

        public static CityArchShelterWorldResult Build(
            Transform parent,
            CityLayout layout,
            CityArchShelterPlan plan,
            CityMiscAssetProvider provider)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            CityArchShelterValidator.ValidateOrThrow(layout, plan);
            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);
            if (!plan.IsEnabled)
            {
                return new CityArchShelterWorldResult(
                    root.gameObject,
                    null,
                    Array.Empty<Transform>(),
                    Array.Empty<Transform>(),
                    null,
                    Array.Empty<Collider>());
            }

            if (provider == null)
            {
                throw new InvalidOperationException(
                    "An enabled City arch shelter requires the imported " +
                    "City misc asset provider.");
            }

            provider.ValidateOrThrow();
            Transform structure = CreateAssemblyRoot(
                root,
                StructureRootName,
                plan.Placement.StructurePosition,
                plan.Placement.StructureRotation);
            BuildImportedAssembly(
                structure,
                provider,
                CityMiscKind.NightlifeArchBridgeShell,
                0);

            var propRoots = new List<Transform>(plan.Props.Count);
            for (int index = 0; index < plan.Props.Count; index++)
            {
                CityArchShelterPropDescriptor descriptor =
                    plan.Props[index];
                Transform propRoot = CreateAssemblyRoot(
                    root,
                    descriptor.StableId,
                    descriptor.Position,
                    descriptor.Rotation);
                BuildImportedAssembly(
                    propRoot,
                    provider,
                    ResolveMiscKind(descriptor.Kind),
                    descriptor.Variant);
                propRoots.Add(propRoot);
            }

            var residentRoots = new List<Transform>(
                plan.NpcAnchors.Count);
            for (int index = 0; index < plan.NpcAnchors.Count; index++)
            {
                CityArchShelterNpcAnchorDescriptor anchor =
                    plan.NpcAnchors[index];
                Quaternion rotation = Quaternion.LookRotation(
                    anchor.Facing,
                    Vector3.up);
                Transform residentRoot = CreateAssemblyRoot(
                    root,
                    anchor.StableId,
                    anchor.Position,
                    rotation);
                BuildImportedAssembly(
                    residentRoot,
                    provider,
                    ResolveMiscKind(anchor.Stage),
                    0);
                residentRoots.Add(residentRoot);
            }

            Transform collisionRoot = BuildObstacleColliders(
                root,
                plan.Obstacles);
            BuildStepColliders(collisionRoot, plan.Steps);
            BoxCollider platformCollider = BuildPlatformCollider(
                collisionRoot,
                plan.Platform);
            List<Collider> rainShelters = BuildRainShelterColliders(
                root,
                plan.RainOccluders);
            return new CityArchShelterWorldResult(
                root.gameObject,
                structure,
                propRoots,
                residentRoots,
                platformCollider,
                rainShelters);
        }

        private static Transform CreateAssemblyRoot(
            Transform parent,
            string name,
            Vector3 position,
            Quaternion rotation)
        {
            Transform result = new GameObject(name).transform;
            result.SetParent(parent, false);
            result.SetPositionAndRotation(position, rotation);
            result.localScale = Vector3.one;
            return result;
        }

        private static void BuildImportedAssembly(
            Transform parent,
            CityMiscAssetProvider provider,
            CityMiscKind kind,
            int variant)
        {
            int variantCount = CityMiscAssetProvider.GetVariantCount(kind);
            if (variant < 0 || variant >= variantCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(variant),
                    variant,
                    $"{kind} has {variantCount} authored variants.");
            }

            int partCount = CityMiscAssetProvider.GetPartCount(kind);
            for (int index = 0; index < partCount; index++)
            {
                CityMiscMeshPart part = provider.GetPartOrThrow(
                    kind,
                    variant,
                    index);
                GameObject partObject =
                    RuntimePrimitiveFactory.CreateCombinedMeshes(
                        part.Component,
                        parent,
                        new[]
                        {
                            new RuntimeMeshPlacement(
                                part.Mesh,
                                Vector3.zero,
                                Quaternion.identity)
                        },
                        ResolveColor(part));
                if (IsFlame(part.Component))
                {
                    Renderer renderer = partObject.GetComponent<Renderer>();
                    renderer.sharedMaterial = CityNightResources
                        .EmissiveMaterial;
                    RuntimePrimitiveFactory.SetColor(
                        renderer,
                        ResolveColor(part));
                    renderer.shadowCastingMode =
                        UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
                else if (IsGroundSpill(part.Component))
                {
                    Renderer renderer = partObject.GetComponent<Renderer>();
                    renderer.sharedMaterial = CityNightResources
                        .AtmosphereMaterial;
                    RuntimePrimitiveFactory.SetColor(
                        renderer,
                        ResolveColor(part));
                    renderer.shadowCastingMode =
                        UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    var properties = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(properties);
                    properties.SetFloat(EdgePowerId, 1.3f);
                    properties.SetFloat(NoiseStrengthId, 0.05f);
                    properties.SetFloat(SoftParticleDistanceId, 0.2f);
                    renderer.SetPropertyBlock(properties);
                }
            }
        }

        private static Transform BuildObstacleColliders(
            Transform parent,
            IReadOnlyList<CityArchShelterObstacleDescriptor> obstacles)
        {
            Transform root = new GameObject(CollisionRootName).transform;
            root.SetParent(parent, false);
            for (int index = 0; index < obstacles.Count; index++)
            {
                CityArchShelterObstacleDescriptor descriptor =
                    obstacles[index];
                GameObject proxy = new GameObject(descriptor.StableId);
                proxy.transform.SetParent(root, false);
                proxy.transform.position = descriptor.Bounds.center;
                BoxCollider collider = proxy.AddComponent<BoxCollider>();
                collider.center = Vector3.zero;
                collider.size = descriptor.Bounds.size;
            }

            return root;
        }

        private static void BuildStepColliders(
            Transform parent,
            CityArchShelterStepDescriptor steps)
        {
            float treadDepth = steps.TreadDepth;
            bool ascendsEast = steps.AscentDirection.x > 0f;
            for (int index = 0; index < steps.StepCount; index++)
            {
                float xMin = ascendsEast
                    ? steps.Footprint.xMin + treadDepth * index
                    : steps.Footprint.xMax - treadDepth * (index + 1);
                float xMax = xMin + treadDepth;
                float top = steps.LowerSurfaceY +
                            steps.StepRise * (index + 1);
                float height = top - steps.LowerSurfaceY;
                GameObject tread = new GameObject(
                    $"{steps.StableId}-tread-{index + 1:D2}");
                tread.transform.SetParent(parent, false);
                tread.transform.position = new Vector3(
                    (xMin + xMax) * 0.5f,
                    steps.LowerSurfaceY + height * 0.5f,
                    steps.Footprint.center.y);
                BoxCollider collider = tread.AddComponent<BoxCollider>();
                collider.center = Vector3.zero;
                collider.size = new Vector3(
                    xMax - xMin,
                    height,
                    steps.Footprint.height);
            }
        }

        private static BoxCollider BuildPlatformCollider(
            Transform parent,
            CityArchShelterPlatformDescriptor platform)
        {
            Bounds support = platform.SupportBounds;
            GameObject platformObject = new GameObject(platform.StableId);
            platformObject.transform.SetParent(parent, false);
            platformObject.transform.position = support.center;
            BoxCollider collider =
                platformObject.AddComponent<BoxCollider>();
            collider.center = Vector3.zero;
            collider.size = support.size;
            return collider;
        }

        private static List<Collider> BuildRainShelterColliders(
            Transform parent,
            IReadOnlyList<CityArchShelterRainOccluderDescriptor> descriptors)
        {
            Transform root = new GameObject(RainRootName).transform;
            root.SetParent(parent, false);
            root.gameObject.layer = IgnoreRaycastLayer;
            var result = new List<Collider>(descriptors.Count);
            for (int index = 0; index < descriptors.Count; index++)
            {
                CityArchShelterRainOccluderDescriptor descriptor =
                    descriptors[index];
                GameObject volume = new GameObject(descriptor.StableId);
                volume.layer = IgnoreRaycastLayer;
                volume.transform.SetParent(root, false);
                volume.transform.position = descriptor.Bounds.center;
                BoxCollider collider = volume.AddComponent<BoxCollider>();
                collider.center = Vector3.zero;
                collider.size = descriptor.Bounds.size;
                collider.isTrigger = true;
                result.Add(collider);
            }

            return result;
        }

        private static CityMiscKind ResolveMiscKind(
            CityArchShelterPropKind kind)
        {
            switch (kind)
            {
                case CityArchShelterPropKind.BurnBarrel:
                    return CityMiscKind.NightlifeBurnBarrel;
                case CityArchShelterPropKind.Fire:
                    return CityMiscKind.NightlifeShelterFire;
                case CityArchShelterPropKind.Bedding:
                    return CityMiscKind.NightlifeShelterBedding;
                case CityArchShelterPropKind.Clutter:
                    return CityMiscKind.NightlifeShelterClutter;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        "Unsupported arch-shelter prop kind.");
            }
        }

        private static CityMiscKind ResolveMiscKind(
            CityArchShelterNpcStageKind stage)
        {
            switch (stage)
            {
                case CityArchShelterNpcStageKind.StandingWarmer:
                    return CityMiscKind.NightlifeShelterStandingPerson;
                case CityArchShelterNpcStageKind.SeatedWarmer:
                    return CityMiscKind.NightlifeShelterSeatedPerson;
                case CityArchShelterNpcStageKind.Sleeper:
                    return CityMiscKind.NightlifeShelterSleepingPerson;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(stage),
                        stage,
                        "Unsupported arch-shelter resident stage.");
            }
        }

        private static bool IsFlame(string component)
        {
            return component != null &&
                   (component.StartsWith(
                        "Flame",
                        StringComparison.Ordinal) ||
                    string.Equals(
                        component,
                        "EmberBed_Neon",
                        StringComparison.Ordinal));
        }

        private static bool IsGroundSpill(string component)
        {
            return string.Equals(
                component,
                "GroundSpill_BacklitSign",
                StringComparison.Ordinal);
        }

        private static Color ResolveColor(CityMiscMeshPart part)
        {
            if (string.Equals(
                    part.Component,
                    "FlameCore_Neon",
                    StringComparison.Ordinal))
            {
                return FlameColor;
            }

            if (string.Equals(
                    part.Component,
                    "FlameOuter_Neon",
                    StringComparison.Ordinal))
            {
                return FlameOuterColor;
            }

            if (string.Equals(
                    part.Component,
                    "FlameLeftTongue_Neon",
                    StringComparison.Ordinal))
            {
                return FlameLeftColor;
            }

            if (string.Equals(
                    part.Component,
                    "FlameRightTongue_Neon",
                    StringComparison.Ordinal))
            {
                return FlameRightColor;
            }

            if (string.Equals(
                    part.Component,
                    "EmberBed_Neon",
                    StringComparison.Ordinal))
            {
                return EmberColor;
            }

            if (string.Equals(
                    part.Component,
                    "GroundSpill_BacklitSign",
                    StringComparison.Ordinal))
            {
                return GroundSpillColor;
            }

            if (string.Equals(
                    part.Component,
                    "Skin_Masonry",
                    StringComparison.Ordinal))
            {
                return SkinColor;
            }

            switch (part.Role)
            {
                case CityMiscMeshRole.Masonry:
                    return MasonryColor;
                case CityMiscMeshRole.Industrial:
                    return IndustrialColor;
                case CityMiscMeshRole.Street:
                    return StreetColor;
                case CityMiscMeshRole.Timber:
                    return TimberColor;
                case CityMiscMeshRole.Residential:
                    return ResidentialColor;
                case CityMiscMeshRole.Neon:
                    return FlameColor;
                case CityMiscMeshRole.BacklitSign:
                    return GroundSpillColor;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(part),
                        part.Role,
                        "Unsupported arch-shelter mesh role.");
            }
        }
    }
}
