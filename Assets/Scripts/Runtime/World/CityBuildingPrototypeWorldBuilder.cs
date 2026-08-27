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

        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");
        private static readonly int SmoothnessId =
            Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId =
            Shader.PropertyToID("_Metallic");

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
            ApplyFlat(
                GetRenderer(registry, CityBuildingMeshRole.Shell),
                facade,
                0.08f,
                0f);
            ApplyFlat(
                GetRenderer(registry, CityBuildingMeshRole.Trim),
                Color.Lerp(facade, Color.white, 0.22f),
                0.12f,
                0.02f);
            ApplyFlat(
                GetRenderer(registry, CityBuildingMeshRole.Roof),
                CityExteriorAppearance.Darken(facade, 0.065f),
                CityFacadeAppearance.RoofSmoothness,
                0f);
            ApplyFlat(
                GetRenderer(registry, CityBuildingMeshRole.Metal),
                CityExteriorAppearance.Darken(facade, 0.20f),
                0.28f,
                0.52f);
            ApplyFlat(
                GetRenderer(registry, CityBuildingMeshRole.WindowFrame),
                new Color(0.055f, 0.065f, 0.067f),
                0.18f,
                0.16f);
            CityBuildingWindowSlotAppearance.Apply(
                GetRenderer(registry, CityBuildingMeshRole.WindowGlass),
                registry,
                lot,
                citySeed);
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

        private static void ApplyFlat(
            Renderer renderer,
            Color color,
            float smoothness,
            float metallic)
        {
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetTexture(BaseMapId, Texture2D.whiteTexture);
            properties.SetColor(BaseColorId, color);
            properties.SetColor(ColorId, color);
            properties.SetFloat(SmoothnessId, smoothness);
            properties.SetFloat(MetallicId, metallic);
            renderer.SetPropertyBlock(properties);
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
                    visibleBounds.size.x,
                    top - bottom,
                    visibleBounds.size.z),
                facade,
                RuntimePrimitiveFactory.DefaultMaterial,
                false);
            ApplyFlat(
                foundation.GetComponent<Renderer>(),
                facade,
                0.08f,
                0f);
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
