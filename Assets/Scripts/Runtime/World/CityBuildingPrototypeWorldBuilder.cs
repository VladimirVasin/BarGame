using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Runtime bridge from pure generated lots to the passive Blender
    /// prototype wrappers. The imported wrapper owns visible architecture;
    /// the generated lot remains authoritative for collision and navigation.
    /// </summary>
    internal static class CityBuildingPrototypeWorldBuilder
    {
        public const string PrototypeObjectName =
            "Blender Building Prototype";
        public const string LogicalCollisionObjectName = "Building Mass";
        public const string FoundationObjectName = "Prototype Foundation";

        private const float FoundationOverlap = 0.04f;
        internal const float FoundationHorizontalInset = 0.08f;

        private static CityBuildingAssetProvider provider;

        // Every instance is a copy of one catalog source prefab, so the
        // registry contract is proved on the source once per prefab, not on
        // every lot: the passive-hierarchy walk alone is nine
        // GetComponentsInChildren sweeps, ~1300 per City load when run per lot.
        private static readonly HashSet<GameObject> validatedSources =
            new HashSet<GameObject>();

        public static CityBuildingAssetRegistry BuildCity(
            Transform parent,
            BuildingLot lot,
            int citySeed,
            float logicalFoundationDepth)
        {
            ValidateBuildArguments(parent, lot, logicalFoundationDepth);
            CityBuildingAssetRegistry registry = InstantiatePrototype(
                parent,
                lot);
            CityBuildingPrototypePose pose =
                CityBuildingPrototypePlacement.ResolveCityPose(
                    lot,
                    registry);
            ApplyPose(registry.transform, pose);
            ApplyAppearance(registry, lot, citySeed);
            BuildFoundation(
                parent,
                registry,
                pose,
                lot,
                logicalFoundationDepth,
                FoundationObjectName);
            BuildLogicalCollision(
                parent,
                lot,
                logicalFoundationDepth);
            return registry;
        }

        public static CityBuildingExteriorFit ClassifyHomeExterior(
            HomeExteriorContextPlan context,
            BuildingLot lot)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            ValidateOrdinaryLot(lot);
            CityBuildingAssetRegistry registry = GetSourceRegistry(lot);
            CityBuildingPrototypePose cityPose =
                CityBuildingPrototypePlacement.ResolveCityPose(
                    lot,
                    registry);
            Bounds homeBounds = CityBuildingPrototypePlacement
                .ResolveHomeBounds(
                    context.PlayerHome,
                    cityPose,
                    registry.LocalBounds);
            return CityBuildingPrototypePlacement.ClassifyHomeBounds(
                homeBounds);
        }

        public static CityBuildingAssetRegistry BuildHomeExterior(
            Transform parent,
            HomeExteriorContextPlan context,
            BuildingLot lot,
            float foundationDepth)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            ValidateBuildArguments(parent, lot, foundationDepth);
            CityBuildingAssetRegistry registry = InstantiatePrototype(
                parent,
                lot);
            CityBuildingPrototypePose cityPose =
                CityBuildingPrototypePlacement.ResolveCityPose(
                    lot,
                    registry);
            CityBuildingPrototypePose homePose =
                CityBuildingPrototypePlacement.ResolveHomePose(
                    context.PlayerHome,
                    cityPose);
            ApplyPose(registry.transform, homePose);
            ApplyAppearance(registry, lot, context.Layout.Seed);
            BuildFoundation(
                parent,
                registry,
                homePose,
                lot,
                foundationDepth,
                "Exterior " + FoundationObjectName);
            return registry;
        }

        private static CityBuildingAssetRegistry InstantiatePrototype(
            Transform parent,
            BuildingLot lot)
        {
            GameObject source = Provider.GetPrefabOrThrow(lot.District, lot.BuildingVariant);
            ValidateSourceOnce(source, lot.District);
            GameObject instance = UnityEngine.Object.Instantiate(
                source,
                parent,
                false);
            instance.name = PrototypeObjectName;
            CityBuildingAssetRegistry registry =
                instance.GetComponent<CityBuildingAssetRegistry>();
            if (registry == null)
            {
                throw new InvalidOperationException(
                    $"City building prefab for {lot.District} lost its " +
                    "registry.");
            }

            return registry;
        }

        private static CityBuildingAssetRegistry GetSourceRegistry(
            BuildingLot lot)
        {
            return GetSourceRegistry(
                Provider.GetPrefabOrThrow(lot.District, lot.BuildingVariant),
                lot.District);
        }

        private static CityBuildingAssetRegistry GetSourceRegistry(
            GameObject source,
            CityDistrictKind district)
        {
            CityBuildingAssetRegistry registry =
                source.GetComponent<CityBuildingAssetRegistry>();
            if (registry == null)
            {
                throw new InvalidOperationException(
                    $"City building prefab for {district} lost its " +
                    "registry.");
            }

            return registry;
        }

        private static void ValidateSourceOnce(
            GameObject source,
            CityDistrictKind district)
        {
            if (validatedSources.Contains(source))
            {
                return;
            }

            // A failed source stays out of the set so a later build reports
            // the same fault instead of trusting a prefab that never passed.
            GetSourceRegistry(source, district).ValidateOrThrow();
            validatedSources.Add(source);
        }

        private static CityBuildingAssetProvider Provider
        {
            get
            {
                if (provider == null)
                {
                    provider = CityBuildingAssetProvider.LoadOrThrow();
                }

                return provider;
            }
        }

        private static void ApplyPose(
            Transform target,
            CityBuildingPrototypePose pose)
        {
            target.localPosition = pose.Position;
            target.localRotation = pose.Rotation;
            target.localScale = Vector3.one;
        }

        private static void ApplyAppearance(
            CityBuildingAssetRegistry registry,
            BuildingLot lot,
            int citySeed)
        {
            Color facade = CityExteriorAppearance
                .CreateNightFacadeColor(lot);
            for (int index = 0; index < registry.Parts.Count; index++)
            {
                CityBuildingPartBinding binding = registry.Parts[index];
                if (binding.Role == CityBuildingMeshRole.WindowGlass)
                {
                    continue;
                }
                if (binding.Role == CityBuildingMeshRole.TaskLightLens)
                {
                    // A lamp's visible lens belongs to the shared electric
                    // glow owner, while its housing keeps the physical steel
                    // response. One material and property block retain the
                    // always-burning daylight floor without per-lot materials.
                    Renderer lens = binding.Renderer;
                    lens.sharedMaterial = CityNightResources.EmissiveMaterial;
                    lens.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    lens.receiveShadows = false;
                    lens.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    lens.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                    Color litColor = CityNightAtmosphere.GetIndustrialTaskLightColor(lot.IndustrialPurpose) * 3f;
                    litColor.a = 1f;
                    CityNightGlowRegistry.Register(lens, litColor);
                    continue;
                }

                if (!CityBuildingSurfaceAppearance.TryResolveSurface(
                        lot.District,
                        binding.SurfaceKind,
                        out CityBuildingSurfaceKind surface))
                {
                    throw new InvalidOperationException(
                        $"City building '{registry.StableId}' has " +
                        $"unsupported surface '{binding.SurfaceKind}'.");
                }

                CityBuildingSurfaceAppearance.Apply(
                    binding.Renderer,
                    lot.District,
                    surface,
                    ResolveSurfaceTint(surface, facade),
                    lot.IndustrialPurpose);
            }

            CityBuildingWindowSlotAppearance.Apply(
                GetRenderer(registry, CityBuildingMeshRole.WindowGlass),
                registry,
                lot,
                citySeed);
        }

        private static Color ResolveSurfaceTint(
            CityBuildingSurfaceKind surface,
            Color facade)
        {
            switch (surface)
            {
                case CityBuildingSurfaceKind.FacadePrimary:
                case CityBuildingSurfaceKind.Plinth:
                    return facade;
                case CityBuildingSurfaceKind.FacadeSecondary:
                    return Color.Lerp(facade, Color.white, 0.22f);
                case CityBuildingSurfaceKind.Roof:
                    return CityExteriorAppearance.Darken(facade, 0.065f);
                case CityBuildingSurfaceKind.Metal:
                    return CityExteriorAppearance.Darken(facade, 0.20f);
                case CityBuildingSurfaceKind.WindowFrame:
                    return new Color(0.055f, 0.065f, 0.067f);
                case CityBuildingSurfaceKind.CargoTimber:
                case CityBuildingSurfaceKind.EquipmentConcrete:
                case CityBuildingSurfaceKind.EquipmentSteel:
                    return Color.white;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(surface),
                        surface,
                        null);
            }
        }

        private static Renderer GetRenderer(
            CityBuildingAssetRegistry registry,
            CityBuildingMeshRole role)
        {
            if (!registry.TryGetRenderer(role, out Renderer renderer))
            {
                throw new InvalidOperationException(
                    $"City building '{registry.StableId}' lost its {role} " +
                    "renderer.");
            }

            return renderer;
        }

        private static void BuildFoundation(
            Transform parent,
            CityBuildingAssetRegistry registry,
            CityBuildingPrototypePose pose,
            BuildingLot lot,
            float foundationDepth,
            string name)
        {
            if (lot.BuildingVariant > 0 || lot.District == CityDistrictKind.Residential)
            {
                for (int index = 0; index < registry.ColliderBounds.Count; index++)
                {
                    Bounds solid = registry.ColliderBounds[index];
                    if (solid.min.y >= .1f) continue;
                    BuildFoundationBox(parent, lot, solid, pose, foundationDepth,
                        name + " " + index);
                }
                return;
            }
            BuildFoundationBox(parent, lot, registry.LocalBounds, pose, foundationDepth, name);
        }

        private static void BuildFoundationBox(Transform parent, BuildingLot lot,
            Bounds visibleBounds, CityBuildingPrototypePose pose, float foundationDepth, string name)
        {
            float top = visibleBounds.min.y + FoundationOverlap;
            float bottom = visibleBounds.min.y - foundationDepth;
            Color facade = CityExteriorAppearance
                .CreateNightFacadeColor(lot);
            GameObject foundation = RuntimePrimitiveFactory.CreateBox(
                name,
                parent,
                pose.TransformPoint(new Vector3(
                    visibleBounds.center.x,
                    (bottom + top) * 0.5f,
                    visibleBounds.center.z)),
                new Vector3(
                    Mathf.Max(
                        0.1f,
                        visibleBounds.size.x -
                        (FoundationHorizontalInset * 2f)),
                    top - bottom,
                    Mathf.Max(
                        0.1f,
                        visibleBounds.size.z -
                        (FoundationHorizontalInset * 2f))),
                facade,
                RuntimePrimitiveFactory.DefaultMaterial,
                false);
            foundation.transform.localRotation = pose.Rotation;
            CityBuildingSurfaceAppearance.Apply(
                foundation.GetComponent<Renderer>(),
                lot.District,
                CityBuildingSurfaceKind.Plinth,
                facade,
                lot.IndustrialPurpose);
        }

        internal static void BuildLogicalCollision(
            Transform parent,
            BuildingLot lot,
            float foundationDepth)
        {
            if (lot.BuildingVariant > 0 || lot.District == CityDistrictKind.Residential)
            {
                CityBuildingPrototypePose pose =
                    CityBuildingPrototypePlacement.ResolveExpectedCityPose(lot);
                IReadOnlyList<Bounds> solids = CityBuildingAssetProvider
                    .GetExpectedCollisionBounds(lot.District, lot.BuildingVariant);
                for (int index = 0; index < solids.Count; index++)
                {
                    float extension = solids[index].min.y >= .1f
                        ? 0f : foundationDepth;
                    var part = new GameObject(LogicalCollisionObjectName + " " + index);
                    part.transform.SetParent(parent, false);
                    part.transform.localPosition = pose.TransformPoint(
                        solids[index].center - Vector3.up * extension * 0.5f);
                    part.transform.localRotation = pose.Rotation;
                    BoxCollider body = part.AddComponent<BoxCollider>();
                    body.size = solids[index].size + Vector3.up * extension;
                }
                if (lot.District == CityDistrictKind.Residential)
                {
                    Vector3 threshold = CityBuildingAssetProvider.GetExpectedEntranceThreshold(lot.BuildingVariant);
                    const float floorThickness = .12f;
                    var floor = new GameObject("Residential Vestibule Floor");
                    floor.transform.SetParent(parent, false);
                    floor.transform.localPosition = pose.TransformPoint(threshold -
                        Vector3.forward * CityBuildingAssetProvider.EntranceDepth * .5f - Vector3.up * floorThickness * .5f);
                    floor.transform.localRotation = pose.Rotation;
                    floor.AddComponent<BoxCollider>().size = new Vector3(CityBuildingAssetProvider.EntranceClearWidth,
                        floorThickness, CityBuildingAssetProvider.EntranceDepth);
                    FootstepGround.Stamp(floor, FootstepGroundKind.Stone);
                }
                return;
            }
            var collisionObject = new GameObject(
                LogicalCollisionObjectName);
            Transform collision = collisionObject.transform;
            collision.SetParent(parent, false);
            collision.localPosition = lot.Center +
                Vector3.up *
                (lot.Height * 0.5f +
                 CityFacadeGrid.MassBaseElevation -
                 foundationDepth * 0.5f);
            BoxCollider collider = collisionObject.AddComponent<BoxCollider>();
            if (lot.HasFacadeRotation)
            {
                collision.localRotation = lot.FacadeRotation;
                collider.size = new Vector3(
                    lot.FrontageDirection.x != 0 ? lot.Size.y : lot.Size.x,
                    lot.Height + foundationDepth,
                    lot.FrontageDirection.x != 0 ? lot.Size.x : lot.Size.y);
            }
            else collider.size = new Vector3(lot.Size.x,
                lot.Height + foundationDepth, lot.Size.y);
        }

        private static void ValidateBuildArguments(
            Transform parent,
            BuildingLot lot,
            float foundationDepth)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            ValidateOrdinaryLot(lot);
            if (foundationDepth <= 0f ||
                float.IsNaN(foundationDepth) ||
                float.IsInfinity(foundationDepth))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(foundationDepth));
            }
        }

        private static void ValidateOrdinaryLot(BuildingLot lot)
        {
            if (lot == null || !lot.IsOrdinaryBuilding)
            {
                throw new ArgumentException(
                    "Blender district prototypes only replace ordinary " +
                    "City buildings.",
                    nameof(lot));
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCachedResources()
        {
            provider = null;
            validatedSources.Clear();
        }
    }
}
