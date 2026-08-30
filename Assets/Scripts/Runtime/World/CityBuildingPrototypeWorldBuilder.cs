using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Runtime bridge from pure generated lots to the four passive Blender
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
            GameObject source = Provider.GetPrefabOrThrow(lot.District);
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

            registry.ValidateOrThrow();
            return registry;
        }

        private static CityBuildingAssetRegistry GetSourceRegistry(
            BuildingLot lot)
        {
            CityBuildingAssetRegistry registry = Provider
                .GetPrefabOrThrow(lot.District)
                .GetComponent<CityBuildingAssetRegistry>();
            if (registry == null)
            {
                throw new InvalidOperationException(
                    $"City building prefab for {lot.District} lost its " +
                    "registry.");
            }

            return registry;
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
                    ResolveSurfaceTint(surface, facade));
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
            Bounds visibleBounds = CityBuildingPrototypePlacement
                .TransformBounds(registry.LocalBounds, pose);
            float top = visibleBounds.min.y + FoundationOverlap;
            float bottom = visibleBounds.min.y - foundationDepth;
            Color facade = CityExteriorAppearance
                .CreateNightFacadeColor(lot);
            GameObject foundation = RuntimePrimitiveFactory.CreateBox(
                name,
                parent,
                new Vector3(
                    visibleBounds.center.x,
                    (bottom + top) * 0.5f,
                    visibleBounds.center.z),
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
            CityBuildingSurfaceAppearance.Apply(
                foundation.GetComponent<Renderer>(),
                lot.District,
                CityBuildingSurfaceKind.Plinth,
                facade);
        }

        internal static void BuildLogicalCollision(
            Transform parent,
            BuildingLot lot,
            float foundationDepth)
        {
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
            collider.size = new Vector3(
                lot.Size.x,
                lot.Height + foundationDepth,
                lot.Size.y);
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
        }
    }
}
