using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Builds the four district points of interest as open public places.
    /// The layout owns their position and access contract; this builder owns
    /// only the physical surface and the free-standing visual recipes.
    /// </summary>
    public static class CityDistrictPointOfInterestWorldBuilder
    {
        public const string CityRootName =
            "District Points Of Interest";
        public const string HomeExteriorRootName =
            "Home Exterior District Points Of Interest";
        public const string PublicGroundName = "Public Ground";

        private const float ReferencePublicWidth = 15f;
        private const float PublicGroundHeight = 0.12f;
        private const float MinimumPublicGroundFoundationDepth = 0.14f;

        // The two sittable benches, shared between the visual recipes
        // below and <see cref="TryDescribeBenchSeat"/> so the seat the
        // hero docks against is always the seat that was drawn.
        private const float DryingBenchX = -3.25f;
        private const float DryingBenchSeatCenterY = 0.53f;
        private const float DryingBenchZ = 4.45f;
        private const float DryingBenchWidth = 2.40f;
        private const float DryingBenchSeatThickness = 0.18f;
        private const float DryingBenchDepth = 0.58f;
        private const float IslandBenchX = 2.85f;
        private const float IslandBenchSeatCenterY = 0.66f;
        private const float IslandBenchZ = 2.55f;
        private const float IslandBenchWidth = 2.50f;
        private const float IslandBenchSeatThickness = 0.22f;
        private const float IslandBenchDepth = 0.72f;
        private const float IslandBenchYaw = 22f;

        private static readonly Color OldTownPaving =
            new Color(0.255f, 0.235f, 0.190f);
        private static readonly Color OldStone =
            new Color(0.285f, 0.245f, 0.185f);
        private static readonly Color OldMetal =
            new Color(0.095f, 0.125f, 0.120f);
        private static readonly Color OldRepairMetal =
            new Color(0.315f, 0.355f, 0.325f);
        private static readonly Color OldWater =
            new Color(0.055f, 0.130f, 0.145f);
        private static readonly Color AmberGlow =
            new Color(1.10f, 0.54f, 0.18f);

        private static readonly Color ResidentialPaving =
            new Color(0.235f, 0.275f, 0.270f);
        private static readonly Color ResidentialFrame =
            new Color(0.145f, 0.190f, 0.185f);
        private static readonly Color ResidentialCloth =
            new Color(0.405f, 0.245f, 0.185f);
        private static readonly Color ResidentialClothCold =
            new Color(0.225f, 0.350f, 0.375f);
        private static readonly Color ResidentialPatch =
            new Color(0.600f, 0.500f, 0.285f);

        private static readonly Color IndustrialPaving =
            new Color(0.200f, 0.220f, 0.210f);
        private static readonly Color IndustrialSteel =
            new Color(0.175f, 0.205f, 0.205f);
        private static readonly Color IndustrialDark =
            new Color(0.070f, 0.090f, 0.095f);
        private static readonly Color IndustrialRust =
            new Color(0.390f, 0.205f, 0.095f);
        private static readonly Color IndustrialMarking =
            new Color(0.585f, 0.505f, 0.210f);
        private static readonly Color IndustrialGlow =
            new Color(0.380f, 0.700f, 0.690f);

        private static readonly Color NightlifePaving =
            new Color(0.170f, 0.175f, 0.215f);
        private static readonly Color NightlifeIsland =
            new Color(0.245f, 0.235f, 0.285f);
        private static readonly Color NightlifeFrame =
            new Color(0.085f, 0.090f, 0.125f);
        private static readonly Color NightlifeSeat =
            new Color(0.225f, 0.145f, 0.245f);
        private static readonly Color NightlifeRoutePaper =
            new Color(0.385f, 0.335f, 0.255f);
        private static readonly Color NightlifeRouteInk =
            new Color(0.105f, 0.125f, 0.155f);
        private static readonly Color NightlifePosterRed =
            new Color(0.355f, 0.135f, 0.165f);
        private static readonly Color NightlifePosterBlue =
            new Color(0.145f, 0.245f, 0.295f);
        private static readonly Color NightlifeWaste =
            new Color(0.115f, 0.135f, 0.145f);

        public static GameObject Build(
            Transform parent,
            CityLayout layout)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            Transform root = new GameObject(CityRootName).transform;
            root.SetParent(parent, false);
            for (int index = 0;
                 index < layout.DistrictPointsOfInterest.Count;
                 index++)
            {
                BuildCitySite(
                    root,
                    layout,
                    layout.DistrictPointsOfInterest[index]);
            }

            return root.gameObject;
        }

        public static GameObject BuildHomeExterior(
            Transform parent,
            HomeExteriorContextPlan context)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            Transform root = new GameObject(
                HomeExteriorRootName).transform;
            root.SetParent(parent, false);
            for (int index = 0;
                 index < context.NearbyDistrictPointsOfInterest.Count;
                 index++)
            {
                BuildHomeExteriorSite(
                    root,
                    context,
                    context.NearbyDistrictPointsOfInterest[index]);
            }

            return root.gameObject;
        }

        public static string GetSiteName(string id)
        {
            return $"District Point Of Interest {id}";
        }

        public static string GetRecipeName(
            CityDistrictPointOfInterestKind kind)
        {
            switch (kind)
            {
                case CityDistrictPointOfInterestKind
                    .OldTownWaterworksCourt:
                    return "Old Town Waterworks Court";
                case CityDistrictPointOfInterestKind
                    .ResidentialDryingYard:
                    return "Residential Drying Yard";
                case CityDistrictPointOfInterestKind
                    .IndustrialWeighbridge:
                    return "Industrial Weighbridge";
                case CityDistrictPointOfInterestKind
                    .NightlifeLastRouteIsland:
                    return "Nightlife Last Route Island";
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        /// <summary>
        /// Describes the sittable bench seat one point of interest
        /// carries, in world space, mirroring the recipe transform the
        /// city build applies. Only the drying yard and the last route
        /// island keep a bench; every other kind reports none.
        /// </summary>
        public static bool TryDescribeBenchSeat(
            CityDistrictPointOfInterestDescriptor descriptor,
            out CityBenchSeat seat)
        {
            Vector3 localSeatCenter;
            Vector3 localSeatSize;
            float localYaw;
            string id;
            switch (descriptor.Kind)
            {
                case CityDistrictPointOfInterestKind
                    .ResidentialDryingYard:
                    localSeatCenter = new Vector3(
                        DryingBenchX,
                        DryingBenchSeatCenterY,
                        DryingBenchZ);
                    localSeatSize = new Vector3(
                        DryingBenchWidth,
                        DryingBenchSeatThickness,
                        DryingBenchDepth);
                    localYaw = 0f;
                    id = "drying-yard-shared-bench";
                    break;
                case CityDistrictPointOfInterestKind
                    .NightlifeLastRouteIsland:
                    localSeatCenter = new Vector3(
                        IslandBenchX,
                        IslandBenchSeatCenterY,
                        IslandBenchZ);
                    localSeatSize = new Vector3(
                        IslandBenchWidth,
                        IslandBenchSeatThickness,
                        IslandBenchDepth);
                    localYaw = IslandBenchYaw;
                    id = "last-route-island-empty-bench";
                    break;
                default:
                    seat = default;
                    return false;
            }

            Quaternion recipeRotation = Quaternion.LookRotation(
                ResolveForward(descriptor),
                Vector3.up);
            float horizontalScale =
                ResolveHorizontalScale(descriptor.PublicBounds);
            Vector3 worldSeatCenter = descriptor.Center +
                recipeRotation * new Vector3(
                    localSeatCenter.x * horizontalScale,
                    localSeatCenter.y,
                    localSeatCenter.z * horizontalScale);

            // Both authored benches face their recipe's local -Z: the
            // shared bench looks at the drying frames, the empty bench
            // looks back across the island.
            Vector3 faceDirection = recipeRotation *
                (Quaternion.Euler(0f, localYaw, 0f) *
                 Vector3.back);
            seat = new CityBenchSeat(
                id,
                new Vector3(
                    worldSeatCenter.x,
                    worldSeatCenter.y +
                    localSeatSize.y * 0.5f,
                    worldSeatCenter.z),
                localSeatSize.x * horizontalScale,
                localSeatSize.z * horizontalScale,
                descriptor.Center.y + PublicGroundHeight * 0.5f,
                faceDirection);
            return true;
        }

        private static void BuildCitySite(
            Transform parent,
            CityLayout layout,
            CityDistrictPointOfInterestDescriptor descriptor)
        {
            Transform site = CreateSiteRoot(parent, descriptor);
            Rect publicBounds = descriptor.PublicBounds;
            CreatePublicGround(
                site,
                new Vector3(
                    publicBounds.center.x,
                    descriptor.Center.y,
                    publicBounds.center.y),
                new Vector2(
                    publicBounds.width,
                    publicBounds.height),
                ResolvePavingColor(descriptor.Kind),
                true,
                false,
                ResolvePublicGroundFoundationDepth(
                    layout,
                    descriptor));

            Vector3 forward = ResolveForward(descriptor);
            Transform recipe = CreateRecipeRoot(
                site,
                descriptor,
                descriptor.Center,
                forward,
                ResolveHorizontalScale(publicBounds));
            BuildRecipe(recipe, descriptor.Kind, true, false);
        }

        private static void BuildHomeExteriorSite(
            Transform parent,
            HomeExteriorContextPlan context,
            CityDistrictPointOfInterestDescriptor descriptor)
        {
            Rect localBounds =
                PlayerHomeBalconyGeometry.ToHomeLocalRect(
                    context.PlayerHome,
                    descriptor.PublicBounds);
            if (localBounds.xMin <
                HomeExteriorViewBuilder.ExteriorMinimumX)
            {
                return;
            }

            Transform site = CreateSiteRoot(parent, descriptor);
            Vector3 localCenter =
                PlayerHomeBalconyGeometry.ToHomeLocal(
                    context.PlayerHome,
                    descriptor.Center);
            CreatePublicGround(
                site,
                new Vector3(
                    localBounds.center.x,
                    localCenter.y,
                    localBounds.center.y),
                new Vector2(
                    localBounds.width,
                    localBounds.height),
                ResolvePavingColor(descriptor.Kind),
                false,
                true,
                0f);

            Vector3 localForward =
                PlayerHomeBalconyGeometry.ToHomeLocalDirection(
                    context.PlayerHome,
                    ResolveForward(descriptor));
            Transform recipe = CreateRecipeRoot(
                site,
                descriptor,
                localCenter,
                localForward,
                ResolveHorizontalScale(localBounds));
            BuildRecipe(recipe, descriptor.Kind, false, true);
        }

        private static Transform CreateSiteRoot(
            Transform parent,
            CityDistrictPointOfInterestDescriptor descriptor)
        {
            Transform site = new GameObject(
                GetSiteName(descriptor.Id)).transform;
            site.SetParent(parent, false);
            return site;
        }

        private static Transform CreateRecipeRoot(
            Transform parent,
            CityDistrictPointOfInterestDescriptor descriptor,
            Vector3 center,
            Vector3 forward,
            float horizontalScale)
        {
            Transform recipe = new GameObject(
                GetRecipeName(descriptor.Kind)).transform;
            recipe.SetParent(parent, false);
            recipe.localPosition = center;
            recipe.localRotation = Quaternion.LookRotation(
                forward,
                Vector3.up);
            recipe.localScale = new Vector3(
                horizontalScale,
                1f,
                horizontalScale);
            return recipe;
        }

        private static void CreatePublicGround(
            Transform parent,
            Vector3 center,
            Vector2 size,
            Color color,
            bool collider,
            bool homeExterior,
            float foundationDepth)
        {
            GameObject ground = RuntimePrimitiveFactory.CreateBox(
                PublicGroundName,
                parent,
                center - Vector3.up * (foundationDepth * 0.5f),
                new Vector3(
                    size.x,
                    PublicGroundHeight + foundationDepth,
                    size.y),
                color,
                RuntimePrimitiveFactory.DefaultMaterial,
                collider);
            ConfigureRenderer(ground, homeExterior);
        }

        private static float ResolvePublicGroundFoundationDepth(
            CityLayout layout,
            CityDistrictPointOfInterestDescriptor descriptor)
        {
            CitySurfaceDescriptor surface = default;
            bool found = false;
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                if (layout.Surfaces[index].Cell != descriptor.Cell)
                {
                    continue;
                }

                surface = layout.Surfaces[index];
                found = true;
                break;
            }

            if (!found || !CityTerrainSurfacePlan.UsesContinuousTop(surface))
            {
                return MinimumPublicGroundFoundationDepth;
            }

            Rect bounds = descriptor.PublicBounds;
            Vector2[] samples =
            {
                new Vector2(bounds.xMin, bounds.yMin),
                new Vector2(bounds.xMax, bounds.yMin),
                new Vector2(bounds.xMin, bounds.yMax),
                new Vector2(bounds.xMax, bounds.yMax)
            };
            float lowestTop = float.PositiveInfinity;
            for (int index = 0; index < samples.Length; index++)
            {
                lowestTop = Mathf.Min(
                    lowestTop,
                    CityTerrainSurfacePlan.SampleTop(
                        layout,
                        surface,
                        samples[index]));
            }

            float authoredTop = descriptor.Center.y +
                                PublicGroundHeight * 0.5f;
            return Mathf.Max(
                MinimumPublicGroundFoundationDepth,
                authoredTop - lowestTop);
        }

        private static void BuildRecipe(
            Transform parent,
            CityDistrictPointOfInterestKind kind,
            bool colliders,
            bool homeExterior)
        {
            switch (kind)
            {
                case CityDistrictPointOfInterestKind
                    .OldTownWaterworksCourt:
                    BuildWaterworks(
                        parent,
                        colliders,
                        homeExterior);
                    return;
                case CityDistrictPointOfInterestKind
                    .ResidentialDryingYard:
                    BuildDryingYard(
                        parent,
                        colliders,
                        homeExterior);
                    return;
                case CityDistrictPointOfInterestKind
                    .IndustrialWeighbridge:
                    BuildWeighbridge(
                        parent,
                        colliders,
                        homeExterior);
                    return;
                case CityDistrictPointOfInterestKind
                    .NightlifeLastRouteIsland:
                    BuildLastRouteIsland(
                        parent,
                        colliders,
                        homeExterior);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static void BuildWaterworks(
            Transform parent,
            bool colliders,
            bool homeExterior)
        {
            AddBox(parent, "Basin Floor", -0.80f, 0.15f, 0.40f,
                4.15f, 0.30f, 1.55f, OldStone, false, homeExterior);
            AddBox(parent, "Basin North Rim", -0.80f, 0.43f, 1.10f,
                4.35f, 0.56f, 0.24f, OldStone, false, homeExterior);
            AddBox(parent, "Basin South Rim", -0.80f, 0.43f, -0.30f,
                4.35f, 0.56f, 0.24f, OldStone, false, homeExterior);
            AddBox(parent, "Basin West Rim", -2.86f, 0.43f, 0.40f,
                0.24f, 0.56f, 1.18f, OldStone, false, homeExterior);
            AddBox(parent, "Basin East Rim", 1.26f, 0.43f, 0.40f,
                0.24f, 0.56f, 1.18f, OldStone, false, homeExterior);
            AddBox(parent, "Dark Water", -1.02f, 0.32f, 0.40f,
                3.45f, 0.045f, 1.04f, OldWater, false, homeExterior);

            AddCylinder(parent, "Standpipe Pedestal", 0.55f, 0.45f, 0.40f,
                1.08f, 0.45f, 1.08f, OldStone, false, homeExterior);
            AddCylinder(parent, "Cast Iron Standpipe", 0.55f, 1.98f, 0.40f,
                0.58f, 1.52f, 0.58f, OldMetal, false, homeExterior);
            AddCylinder(parent, "Standpipe Cap", 0.55f, 3.55f, 0.40f,
                1.02f, 0.12f, 1.02f, OldMetal, false, homeExterior);
            AddBox(parent, "Water Spout", 0.55f, 2.82f, 0.98f,
                0.30f, 0.28f, 1.28f, OldMetal, false, homeExterior);
            AddBox(parent, "Water Spout Mouth", 0.55f, 2.62f, 1.58f,
                0.42f, 0.58f, 0.30f, OldMetal, false, homeExterior);
            AddBox(parent, "Repair Riser", 0.98f, 1.82f, 0.40f,
                0.20f, 2.20f, 0.20f, OldRepairMetal, false, homeExterior);
            AddBox(parent, "Repair Bridge", 0.77f, 2.73f, 0.40f,
                0.62f, 0.18f, 0.22f, OldRepairMetal, false, homeExterior);
            AddBox(parent, "Lower Pipe Clamp", 0.55f, 1.32f, 0.40f,
                0.78f, 0.15f, 0.78f, OldRepairMetal, false, homeExterior);
            AddBox(parent, "Upper Pipe Clamp", 0.55f, 2.42f, 0.40f,
                0.76f, 0.15f, 0.76f, OldRepairMetal, false, homeExterior);
            AddBox(parent, "Valve Crossbar", -0.02f, 2.05f, 0.40f,
                0.95f, 0.14f, 0.14f, OldRepairMetal, false, homeExterior);
            AddBox(parent, "Valve Handle", -0.47f, 2.05f, 0.40f,
                0.12f, 0.72f, 0.12f, OldRepairMetal, false, homeExterior);
            AddBox(parent, "Working Lamp", 0.55f, 3.88f, 0.40f,
                0.34f, 0.20f, 0.34f, AmberGlow, true, homeExterior);

            AddBox(parent, "Drain Channel A", -1.80f, 0.075f, -1.08f,
                0.16f, 0.025f, 2.00f, OldMetal, false, homeExterior);
            AddBox(parent, "Drain Channel B", -0.95f, 0.075f, -1.28f,
                0.16f, 0.025f, 1.55f, OldMetal, false, homeExterior);
            AddBox(parent, "Drain Channel C", -0.10f, 0.075f, -1.02f,
                0.16f, 0.025f, 1.95f, OldMetal, false, homeExterior);

            if (colliders)
            {
                AddObstacleCollider(
                    parent,
                    "Waterworks Basin Collider",
                    new Vector3(-0.80f, 0.55f, 0.40f),
                    new Vector3(4.40f, 1.10f, 1.85f));
            }
        }

        private static void BuildDryingYard(
            Transform parent,
            bool colliders,
            bool homeExterior)
        {
            float[] rows = { -3f, 0f, 3f };
            for (int row = 0; row < rows.Length; row++)
            {
                float z = rows[row];
                string rowName = $"Drying Frame {row + 1}";
                AddBox(parent, rowName + " West Post", -4.55f, 1.35f, z,
                    0.20f, 2.70f, 0.20f, ResidentialFrame, false, homeExterior);
                AddBox(parent, rowName + " East Post", 4.55f, 1.35f, z,
                    0.20f, 2.70f, 0.20f, ResidentialFrame, false, homeExterior);
                AddBox(parent, rowName + " Header", 0f, 2.66f, z,
                    9.30f, 0.18f, 0.20f, ResidentialFrame, false, homeExterior);
                AddBox(parent, rowName + " Front Line", 0f, 2.34f, z - 0.16f,
                    9.05f, 0.045f, 0.045f, ResidentialFrame, false, homeExterior);
                AddBox(parent, rowName + " Back Line", 0f, 2.20f, z + 0.16f,
                    9.05f, 0.045f, 0.045f, ResidentialFrame, false, homeExterior);

                if (colliders)
                {
                    AddObstacleCollider(
                        parent,
                        rowName + " West Post Collider",
                        new Vector3(-4.55f, 1.35f, z),
                        new Vector3(0.28f, 2.70f, 0.28f));
                    AddObstacleCollider(
                        parent,
                        rowName + " East Post Collider",
                        new Vector3(4.55f, 1.35f, z),
                        new Vector3(0.28f, 2.70f, 0.28f));
                }
            }

            AddBox(parent, "Large Faded Blanket", 1.15f, 1.55f, 0f,
                3.20f, 1.45f, 0.075f, ResidentialCloth, false, homeExterior);
            AddBox(parent, "Blanket Repair Patch", 1.72f, 1.66f, -0.045f,
                0.72f, 0.52f, 0.035f, ResidentialPatch, false, homeExterior);
            AddBox(parent, "Cold Sheet", -2.75f, 1.78f, -3f,
                1.70f, 0.94f, 0.065f, ResidentialClothCold, false, homeExterior);
            AddBox(parent, "Small Towel", 2.75f, 1.94f, 3f,
                0.90f, 0.58f, 0.065f, ResidentialPatch, false, homeExterior);
            AddBox(parent, "Shared Bench Seat",
                DryingBenchX, DryingBenchSeatCenterY, DryingBenchZ,
                DryingBenchWidth, DryingBenchSeatThickness,
                DryingBenchDepth, ResidentialCloth, false, homeExterior);
            AddBox(parent, "Shared Bench Leg A", -4.02f, 0.28f, 4.45f,
                0.18f, 0.50f, 0.42f, ResidentialFrame, false, homeExterior);
            AddBox(parent, "Shared Bench Leg B", -2.48f, 0.28f, 4.45f,
                0.18f, 0.50f, 0.42f, ResidentialFrame, false, homeExterior);

            if (colliders)
            {
                AddObstacleCollider(
                    parent,
                    "Faded Blanket Collider",
                    new Vector3(1.15f, 1.55f, 0f),
                    new Vector3(3.20f, 1.45f, 0.12f));
                AddObstacleCollider(
                    parent,
                    "Shared Bench Collider",
                    new Vector3(DryingBenchX, 0.38f, DryingBenchZ),
                    new Vector3(DryingBenchWidth, 0.76f, 0.62f));
            }
        }

        private static void BuildWeighbridge(
            Transform parent,
            bool colliders,
            bool homeExterior)
        {
            AddBox(parent, "Weighbridge Deck", 0f, 0.16f, 0f,
                3.60f, 0.22f, 11.60f, IndustrialSteel, false, homeExterior);
            AddBox(parent, "Deck Dark Channel West", -1.48f, 0.285f, 0f,
                0.20f, 0.035f, 10.80f, IndustrialDark, false, homeExterior);
            AddBox(parent, "Deck Dark Channel East", 1.48f, 0.285f, 0f,
                0.20f, 0.035f, 10.80f, IndustrialDark, false, homeExterior);
            AddBox(parent, "Axle Marking North", 0f, 0.305f, 3.62f,
                3.05f, 0.025f, 0.20f, IndustrialMarking, false, homeExterior);
            AddBox(parent, "Axle Marking South", 0f, 0.305f, -3.62f,
                3.05f, 0.025f, 0.20f, IndustrialMarking, false, homeExterior);
            AddBox(parent, "Deck Repair Plate", 0.62f, 0.31f, -1.25f,
                1.05f, 0.035f, 1.45f, IndustrialRust, false, homeExterior);

            AddBox(parent, "Scale Mechanism Base", 3.25f, 0.34f, 0.20f,
                1.20f, 0.56f, 1.28f, IndustrialDark, false, homeExterior);
            AddBox(parent, "Scale Indicator Mast", 3.25f, 2.52f, 0.20f,
                0.30f, 4.35f, 0.34f, IndustrialSteel, false, homeExterior);
            AddBox(parent, "Scale Indicator Head", 3.25f, 4.63f, 0.20f,
                2.25f, 0.82f, 0.62f, IndustrialDark, false, homeExterior);
            AddBox(parent, "Scale Indicator Face", 3.25f, 4.66f, 0.525f,
                1.78f, 0.52f, 0.035f, IndustrialGlow, true, homeExterior,
                alwaysLit: true);
            AddBox(parent, "Scale Needle", 3.25f, 4.66f, 0.55f,
                0.10f, 0.42f, 0.035f, IndustrialDark, false, homeExterior, 28f);
            AddBox(parent, "Mechanical Linkage", 2.65f, 1.10f, 0.20f,
                1.08f, 0.20f, 0.22f, IndustrialRust, false, homeExterior);
            AddBox(parent, "Cold Service Lamp", 3.25f, 5.34f, 0.20f,
                1.15f, 0.16f, 0.38f, IndustrialGlow, true, homeExterior);

            for (int side = -1; side <= 1; side += 2)
            {
                AddBox(parent, $"Load Cell {side} North", side * 1.62f, 0.19f, 4.45f,
                    0.42f, 0.28f, 0.72f, IndustrialRust, false, homeExterior);
                AddBox(parent, $"Load Cell {side} South", side * 1.62f, 0.19f, -4.45f,
                    0.42f, 0.28f, 0.72f, IndustrialRust, false, homeExterior);
            }

            AddBox(parent, "Wheel Chock A", -0.92f, 0.42f, -5.10f,
                0.62f, 0.26f, 0.42f, IndustrialMarking, false, homeExterior, 14f);
            AddBox(parent, "Wheel Chock B", 0.92f, 0.42f, -5.10f,
                0.62f, 0.26f, 0.42f, IndustrialMarking, false, homeExterior, -14f);

            if (colliders)
            {
                AddObstacleCollider(
                    parent,
                    "Walkable Weighbridge Collider",
                    new Vector3(0f, 0.16f, 0f),
                    new Vector3(3.60f, 0.22f, 11.60f));
                AddObstacleCollider(
                    parent,
                    "Scale Mechanism Collider",
                    new Vector3(3.25f, 0.50f, 0.20f),
                    new Vector3(1.20f, 1.00f, 1.28f));
            }
        }

        private static void BuildLastRouteIsland(
            Transform parent,
            bool colliders,
            bool homeExterior)
        {
            AddCylinder(parent, "Last Route Island", 0f, 0.12f, 0f,
                10.80f, 0.09f, 10.80f, NightlifeIsland,
                colliders, homeExterior);
            AddCylinder(parent, "Inner Route Ring", 0f, 0.225f, 0f,
                7.20f, 0.025f, 7.20f, NightlifeFrame, false, homeExterior);
            AddCylinder(parent, "Empty Island Centre", 0f, 0.255f, 0f,
                4.20f, 0.02f, 4.20f, NightlifePaving, false, homeExterior);

            float[] segmentAngles = { 48f, 102f, 168f, 226f, 292f };
            for (int index = 0; index < segmentAngles.Length; index++)
            {
                float angle = segmentAngles[index];
                float radians = angle * Mathf.Deg2Rad;
                float x = Mathf.Sin(radians) * 4.70f;
                float z = Mathf.Cos(radians) * 4.70f;
                string name = $"Broken Canopy Segment {index + 1}";
                AddBox(parent, name + " Post", x, 1.70f, z,
                    0.30f, 3.40f, 0.30f, NightlifeFrame, false, homeExterior);
                AddBox(parent, name + " Beam", x, 3.36f, z,
                    3.25f, 0.26f, 0.42f, NightlifeFrame, false, homeExterior, angle);
                AddBox(parent, name + " Roof", x, 3.58f, z,
                    3.45f, 0.18f, 1.25f, NightlifeFrame, false, homeExterior, angle);
                if (index == 1 || index == 4)
                {
                    float plateOffset = 0.18f;
                    Color plateColor = index == 1
                        ? NightlifePosterBlue
                        : NightlifePosterRed;
                    AddBox(
                        parent,
                        name + " Weathered Route Plate",
                        x + Mathf.Sin(radians) * plateOffset,
                        2.42f,
                        z + Mathf.Cos(radians) * plateOffset,
                        0.62f,
                        0.44f,
                        0.07f,
                        plateColor,
                        false,
                        homeExterior,
                        angle);
                }

                if (colliders)
                {
                    AddObstacleCollider(
                        parent,
                        name + " Post Collider",
                        new Vector3(x, 1.70f, z),
                        new Vector3(0.36f, 3.40f, 0.36f));
                }
            }

            AddBox(parent, "Last Route Mast Base", -2.75f, 0.52f, -1.25f,
                1.12f, 0.78f, 1.12f, NightlifeFrame, false, homeExterior);
            AddBox(parent, "Last Route Mast", -2.75f, 3.35f, -1.25f,
                0.34f, 5.70f, 0.34f, NightlifeFrame, false, homeExterior);
            AddBox(parent, "Broken Route Totem", -2.75f, 5.55f, -1.25f,
                1.58f, 1.55f, 0.42f, NightlifeFrame, false, homeExterior);
            AddBox(parent, "Totem Route Map Backing", -2.75f, 5.55f, -1.02f,
                1.28f, 1.20f, 0.04f, NightlifeRoutePaper, false, homeExterior);
            AddBox(parent, "Totem Torn Poster A", -2.95f, 5.66f, -0.99f,
                0.64f, 0.70f, 0.025f, NightlifePosterBlue, false, homeExterior,
                -4f);
            AddBox(parent, "Totem Torn Poster B", -2.52f, 5.33f, -0.98f,
                0.50f, 0.43f, 0.025f, NightlifePosterRed, false, homeExterior,
                6f);
            AddBox(parent, "Totem Route Number Plate", -2.71f, 5.97f, -0.97f,
                0.42f, 0.20f, 0.025f, NightlifeRouteInk, false, homeExterior);
            AddBox(parent, "Departure Board", 2.45f, 2.10f, -2.55f,
                2.65f, 1.10f, 0.28f, NightlifeFrame, false, homeExterior, -12f);
            AddBox(parent, "Departure Board Support West", 1.61f, 0.885f, -2.73f,
                0.20f, 1.33f, 0.24f, NightlifeFrame, false, homeExterior, -12f);
            AddBox(parent, "Departure Board Support East", 3.29f, 0.885f, -2.37f,
                0.20f, 1.33f, 0.24f, NightlifeFrame, false, homeExterior, -12f);
            AddBox(parent, "Departure Board Foot West", 1.61f, 0.27f, -2.73f,
                0.48f, 0.12f, 0.46f, NightlifeFrame, false, homeExterior, -12f);
            AddBox(parent, "Departure Board Foot East", 3.29f, 0.27f, -2.37f,
                0.48f, 0.12f, 0.46f, NightlifeFrame, false, homeExterior, -12f);
            AddBox(parent, "Departure Board Glass", 2.45f, 2.10f, -2.39f,
                2.30f, 0.78f, 0.035f, NightlifeRouteInk, false, homeExterior,
                -12f);
            AddBox(parent, "Departure Schedule Row A", 2.45f, 2.30f, -2.365f,
                1.78f, 0.07f, 0.025f, NightlifeRoutePaper, false, homeExterior,
                -12f);
            AddBox(parent, "Departure Schedule Row B", 2.45f, 2.10f, -2.365f,
                1.42f, 0.07f, 0.025f, NightlifePosterBlue, false, homeExterior,
                -12f);
            AddBox(parent, "Departure Schedule Row C", 2.45f, 1.90f, -2.365f,
                1.92f, 0.07f, 0.025f, NightlifeRoutePaper, false, homeExterior,
                -12f);
            AddBox(parent, "Empty Bench",
                IslandBenchX, IslandBenchSeatCenterY, IslandBenchZ,
                IslandBenchWidth, IslandBenchSeatThickness,
                IslandBenchDepth, NightlifeSeat, false, homeExterior,
                IslandBenchYaw);
            AddBox(parent, "Empty Bench Base",
                IslandBenchX, 0.33f, IslandBenchZ,
                0.38f, 0.66f, 0.48f, NightlifeFrame, false, homeExterior,
                IslandBenchYaw);
            AddBox(parent, "Island Waste Bin", 4.15f, 0.71f, 2.20f,
                0.72f, 1.00f, 0.72f, NightlifeWaste, false, homeExterior, 8f);
            AddBox(parent, "Island Waste Bin Rim", 4.15f, 1.23f, 2.20f,
                0.82f, 0.08f, 0.82f, NightlifeFrame, false, homeExterior, 8f);
            AddBox(parent, "Island Waste Bin Opening", 4.15f, 1.275f, 2.20f,
                0.54f, 0.018f, 0.50f, NightlifeRouteInk, false, homeExterior,
                8f);
            AddCylinder(parent, "Discarded Bottle Standing", 2.08f, 0.31f,
                3.82f, 0.13f, 0.09f, 0.13f, NightlifeRoutePaper, false,
                homeExterior);
            AddBox(parent, "Discarded Bottle Fallen", 1.72f, 0.255f, 3.68f,
                0.34f, 0.07f, 0.12f, NightlifePosterBlue, false, homeExterior,
                28f);
            AddBox(parent, "Lost Scarf", -0.35f, 0.292f, 1.05f,
                1.10f, 0.025f, 0.34f, NightlifePosterRed, false, homeExterior,
                -18f);
            AddBox(parent, "Discarded Timetable", -1.20f, 0.228f, -3.65f,
                0.72f, 0.025f, 0.50f, NightlifeRoutePaper, false,
                homeExterior, 12f);

            if (colliders)
            {
                AddObstacleCollider(
                    parent,
                    "Last Route Mast Collider",
                    new Vector3(-2.75f, 0.72f, -1.25f),
                    new Vector3(1.12f, 1.42f, 1.12f));
                AddObstacleCollider(
                    parent,
                    "Departure Board Collider",
                    new Vector3(2.45f, 1.35f, -2.55f),
                    new Vector3(2.65f, 2.70f, 0.38f),
                    -12f);
                AddObstacleCollider(
                    parent,
                    "Empty Bench Collider",
                    new Vector3(IslandBenchX, 0.44f, IslandBenchZ),
                    new Vector3(
                        IslandBenchWidth,
                        0.88f,
                        IslandBenchDepth),
                    IslandBenchYaw);
                AddObstacleCollider(
                    parent,
                    "Island Waste Bin Collider",
                    new Vector3(4.15f, 0.71f, 2.20f),
                    new Vector3(0.78f, 1.00f, 0.78f),
                    8f);
            }
        }

        private static void AddBox(
            Transform parent,
            string name,
            float x,
            float y,
            float z,
            float width,
            float height,
            float depth,
            Color color,
            bool emissive,
            bool homeExterior,
            float yaw = 0f,
            bool alwaysLit = false)
        {
            Material material = emissive
                ? CityNightResources.EmissiveMaterial
                : RuntimePrimitiveFactory.DefaultMaterial;
            GameObject part = RuntimePrimitiveFactory.CreateBox(
                name,
                parent,
                new Vector3(x, y, z),
                new Vector3(width, height, depth),
                color,
                material,
                false);
            part.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            ConfigureRenderer(part, homeExterior);

            // Site lamps die by day with every other electric glow;
            // only a working instrument face may stay always lit.
            if (emissive && !alwaysLit)
            {
                CityNightGlowRegistry.Register(
                    part.GetComponent<Renderer>(),
                    color);
            }
        }

        private static void AddCylinder(
            Transform parent,
            string name,
            float x,
            float y,
            float z,
            float width,
            float halfHeight,
            float depth,
            Color color,
            bool collider,
            bool homeExterior)
        {
            Material material =
                RuntimePrimitiveFactory.DefaultMaterial;
            GameObject part = RuntimePrimitiveFactory.CreateCylinder(
                name,
                parent,
                new Vector3(x, y, z),
                new Vector3(width, halfHeight, depth),
                color,
                material,
                false);
            if (collider && !homeExterior)
            {
                MeshCollider surface =
                    part.AddComponent<MeshCollider>();
                surface.sharedMesh =
                    part.GetComponent<MeshFilter>().sharedMesh;
            }
            ConfigureRenderer(part, homeExterior);
        }

        private static void AddObstacleCollider(
            Transform parent,
            string name,
            Vector3 center,
            Vector3 size,
            float yaw = 0f)
        {
            GameObject obstacle = new GameObject(name);
            obstacle.transform.SetParent(parent, false);
            obstacle.transform.localPosition = center;
            obstacle.transform.localRotation =
                Quaternion.Euler(0f, yaw, 0f);
            BoxCollider collider = obstacle.AddComponent<BoxCollider>();
            collider.size = size;
        }

        private static void ConfigureRenderer(
            GameObject gameObject,
            bool homeExterior)
        {
            Renderer renderer = gameObject.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            if (homeExterior)
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        private static Vector3 ResolveForward(
            CityDistrictPointOfInterestDescriptor descriptor)
        {
            Vector2Int streetSide = descriptor.Accesses.Count > 0
                ? descriptor.Accesses[0].StreetSideDirection
                : Vector2Int.down;
            var forward = new Vector3(
                streetSide.x,
                0f,
                streetSide.y);
            if (!IsFinite(forward.x) ||
                !IsFinite(forward.z) ||
                forward.sqrMagnitude < 0.25f)
            {
                return Vector3.back;
            }

            return Mathf.Abs(forward.x) > Mathf.Abs(forward.z)
                ? new Vector3(Mathf.Sign(forward.x), 0f, 0f)
                : new Vector3(0f, 0f, Mathf.Sign(forward.z));
        }

        private static float ResolveHorizontalScale(Rect bounds)
        {
            float minimum = Mathf.Min(bounds.width, bounds.height);
            return Mathf.Clamp(
                minimum / ReferencePublicWidth,
                0.72f,
                1.08f);
        }

        private static Color ResolvePavingColor(
            CityDistrictPointOfInterestKind kind)
        {
            switch (kind)
            {
                case CityDistrictPointOfInterestKind
                    .OldTownWaterworksCourt:
                    return OldTownPaving;
                case CityDistrictPointOfInterestKind
                    .ResidentialDryingYard:
                    return ResidentialPaving;
                case CityDistrictPointOfInterestKind
                    .IndustrialWeighbridge:
                    return IndustrialPaving;
                case CityDistrictPointOfInterestKind
                    .NightlifeLastRouteIsland:
                    return NightlifePaving;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
