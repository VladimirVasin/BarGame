using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Builds the collider-free storefront shared by City and the bounded
    /// exterior reconstructed outside the player's Home.
    /// </summary>
    public static class CitySupermarketFacadeWorldBuilder
    {
        private static readonly Color FrameColor =
            new Color(0.46f, 0.49f, 0.38f);
        private static readonly Color DoorColor =
            new Color(0.10f, 0.13f, 0.11f);
        private static readonly Color CanopyColor =
            new Color(0.30f, 0.34f, 0.25f);
        private static readonly Color SignHousingColor =
            new Color(0.20f, 0.18f, 0.14f);
        private static readonly Color SignColor =
            new Color(1.40f, 0.24f, 0.12f);
        private static readonly Color LetterColor =
            new Color(1.20f, 1.05f, 0.64f);

        public static void BuildCity(
            Transform parent,
            BuildingLot lot)
        {
            Validate(parent, lot);
            Build(
                parent,
                lot.DoorPosition,
                ResolveDirection(lot),
                CityNightResources.EmissiveMaterial,
                false);
        }

        public static void BuildHomeExterior(
            Transform parent,
            HomeExteriorContextPlan context,
            BuildingLot lot)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            Validate(parent, lot);
            Build(
                parent,
                PlayerHomeBalconyGeometry.ToHomeLocal(
                    context.PlayerHome,
                    lot.DoorPosition),
                PlayerHomeBalconyGeometry.ToHomeLocalDirection(
                    context.PlayerHome,
                    ResolveDirection(lot)),
                CityNightResources.EmissiveMaterial,
                true);
        }

        private static void Build(
            Transform parent,
            Vector3 doorPosition,
            Vector3 direction,
            Material emissiveMaterial,
            bool clipToHomeExterior)
        {
            direction.y = 0f;
            direction.Normalize();
            Vector3 tangent = new Vector3(
                -direction.z,
                0f,
                direction.x);
            bool frontageIsX = Mathf.Abs(direction.x) > 0.5f;

            const float doorWidth = 1.90f;
            const float panelGap = 0.22f;
            float glassWidth =
                (SupermarketEntranceGeometry.StorefrontWidth -
                 doorWidth -
                 panelGap * 4f) *
                0.5f;
            float glassOffset =
                doorWidth * 0.5f +
                panelGap +
                glassWidth * 0.5f;

            CreateBox(
                "Supermarket Door",
                parent,
                doorPosition +
                direction * 0.055f +
                Vector3.up * 1.20f,
                CreateFacadeSize(
                    frontageIsX,
                    0.12f,
                    2.40f,
                    doorWidth),
                DoorColor,
                null,
                clipToHomeExterior);

            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 panelCenter =
                    doorPosition +
                    direction * 0.060f +
                    tangent * (side * glassOffset) +
                    Vector3.up * 1.18f;

                // The storefront panels are real glazing on the
                // supermarket window family: framed plain glass that
                // glows green at night and dies to dark glass by day
                // with every other lit window.
                CreateStorefrontGlass(
                    parent,
                    panelCenter,
                    CreateFacadeSize(
                        frontageIsX,
                        0.10f,
                        2.20f,
                        glassWidth),
                    clipToHomeExterior);

                float outerFrameOffset =
                    SupermarketEntranceGeometry.StorefrontWidth *
                    0.5f;
                CreateBox(
                    "Supermarket Storefront Post",
                    parent,
                    doorPosition +
                    direction * 0.105f +
                    tangent * (side * outerFrameOffset) +
                    Vector3.up * 1.22f,
                    CreateFacadeSize(
                        frontageIsX,
                        0.18f,
                        2.55f,
                        0.18f),
                    FrameColor,
                    null,
                    clipToHomeExterior);

                CreateBox(
                    "Supermarket Door Post",
                    parent,
                    doorPosition +
                    direction * 0.105f +
                    tangent *
                    (side * (doorWidth * 0.5f + panelGap * 0.5f)) +
                    Vector3.up * 1.22f,
                    CreateFacadeSize(
                        frontageIsX,
                        0.18f,
                        2.55f,
                        0.16f),
                    FrameColor,
                    null,
                    clipToHomeExterior);
            }

            CreateBox(
                "Supermarket Storefront Header",
                parent,
                doorPosition +
                direction * 0.105f +
                Vector3.up * 2.52f,
                CreateFacadeSize(
                    frontageIsX,
                    0.18f,
                    0.22f,
                    SupermarketEntranceGeometry.StorefrontWidth + 0.20f),
                FrameColor,
                null,
                clipToHomeExterior);

            CreateBox(
                "Supermarket Entrance Canopy",
                parent,
                doorPosition +
                direction * 0.58f +
                Vector3.up * 2.72f,
                CreateFacadeSize(
                    frontageIsX,
                    1.05f,
                    0.16f,
                    SupermarketEntranceGeometry.CanopyWidth),
                CanopyColor,
                null,
                clipToHomeExterior);

            CreateBox(
                "Supermarket Sign Housing",
                parent,
                doorPosition +
                direction * 0.10f +
                Vector3.up * 3.48f,
                CreateFacadeSize(
                    frontageIsX,
                    0.20f,
                    0.90f,
                    SupermarketEntranceGeometry.StorefrontWidth + 0.70f),
                SignHousingColor,
                null,
                clipToHomeExterior);
            CreateGlowBox(
                "Supermarket Sign",
                parent,
                doorPosition +
                direction * 0.22f +
                Vector3.up * 3.48f,
                CreateFacadeSize(
                    frontageIsX,
                    0.08f,
                    0.62f,
                    SupermarketEntranceGeometry.StorefrontWidth + 0.28f),
                SignColor,
                emissiveMaterial,
                clipToHomeExterior);

            for (int letter = -2; letter <= 2; letter++)
            {
                CreateGlowBox(
                    "Supermarket Sign Letter",
                    parent,
                    doorPosition +
                    direction * 0.275f +
                    tangent * (letter * 0.92f) +
                    Vector3.up * 3.48f,
                    CreateFacadeSize(
                        frontageIsX,
                        0.045f,
                        0.28f,
                        0.58f),
                    LetterColor,
                    emissiveMaterial,
                    clipToHomeExterior);
            }
        }

        private static void CreateStorefrontGlass(
            Transform parent,
            Vector3 position,
            Vector3 size,
            bool clipToHomeExterior)
        {
            Bounds bounds = new Bounds(position, size);
            if (clipToHomeExterior &&
                !HomeExteriorViewBuilder.TryClipToExteriorHalfSpace(
                    bounds,
                    out bounds))
            {
                return;
            }

            GameObject glass = RuntimePrimitiveFactory.CreateMaterialBox(
                "Supermarket Storefront Glass",
                parent,
                bounds.center,
                bounds.size,
                CityWindowAppearance.ResolveLitMaterial(
                    CityWindowFamily.Supermarket),
                false);
            CityWindowAppearance.ApplyPlainPane(
                glass.GetComponent<Renderer>());
        }

        private static void CreateGlowBox(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 size,
            Color color,
            Material emissiveMaterial,
            bool clipToHomeExterior)
        {
            Bounds bounds = new Bounds(position, size);
            if (clipToHomeExterior &&
                !HomeExteriorViewBuilder.TryClipToExteriorHalfSpace(
                    bounds,
                    out bounds))
            {
                return;
            }

            GameObject box = RuntimePrimitiveFactory.CreateBox(
                name,
                parent,
                bounds.center,
                bounds.size,
                color,
                emissiveMaterial,
                false);
            CityNightGlowRegistry.Register(
                box.GetComponent<Renderer>(),
                color);
        }

        private static Vector3 CreateFacadeSize(
            bool frontageIsX,
            float depth,
            float height,
            float width)
        {
            return frontageIsX
                ? new Vector3(depth, height, width)
                : new Vector3(width, height, depth);
        }

        private static void CreateBox(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 size,
            Color color,
            Material material,
            bool clipToHomeExterior)
        {
            Bounds bounds = new Bounds(position, size);
            if (clipToHomeExterior &&
                !HomeExteriorViewBuilder.TryClipToExteriorHalfSpace(
                    bounds,
                    out bounds))
            {
                return;
            }

            if (material == null)
            {
                RuntimePrimitiveFactory.CreateBox(
                    name,
                    parent,
                    bounds.center,
                    bounds.size,
                    color,
                    false);
                return;
            }

            RuntimePrimitiveFactory.CreateBox(
                name,
                parent,
                bounds.center,
                bounds.size,
                color,
                material,
                false);
        }

        private static Vector3 ResolveDirection(BuildingLot lot)
        {
            return new Vector3(
                lot.FrontageDirection.x,
                0f,
                lot.FrontageDirection.y);
        }

        private static void Validate(
            Transform parent,
            BuildingLot lot)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (lot == null)
            {
                throw new ArgumentNullException(nameof(lot));
            }

            if (!lot.IsSupermarket ||
                !lot.HasRoadFrontage)
            {
                throw new ArgumentException(
                    "A supermarket facade requires a supermarket lot " +
                    "with street frontage.",
                    nameof(lot));
            }
        }
    }
}
