using System;
using System.Collections.Generic;
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

        /// <summary>The word both supermarket signs spell.</summary>
        public const string SignWord = "ПРОДУКТЫ";

        private const float BladeCenterY = 4.05f;
        private const float BladeHeight = 3.05f;
        private const float BladeRowStep = 0.36f;

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

            // The sign spells the word: blocky segment glyphs bright
            // enough to read down the street, the recognisable Soviet
            // grocery lettering rather than anonymous glowing blocks.
            IReadOnlyList<SignSegmentRect> lettering =
                CitySignLettering.Layout(
                    SignWord,
                    0.62f,
                    0.42f,
                    0.90f);
            for (int index = 0; index < lettering.Count; index++)
            {
                SignSegmentRect segment = lettering[index];
                CreateGlowBox(
                    "Supermarket Sign Letter",
                    parent,
                    doorPosition +
                    direction * 0.275f +
                    tangent * segment.Center.x +
                    Vector3.up * (3.48f + segment.Center.y),
                    CreateFacadeSize(
                        frontageIsX,
                        0.045f,
                        segment.Size.y,
                        segment.Size.x),
                    LetterColor,
                    emissiveMaterial,
                    clipToHomeExterior);
            }

            BuildBladeSign(
                parent,
                doorPosition,
                direction,
                tangent,
                frontageIsX,
                emissiveMaterial,
                clipToHomeExterior);
        }

        /// <summary>
        /// The vertical corner box: one glyph per row reading down,
        /// lettered on both street-facing sides — the classic Soviet
        /// blade sign that marks the grocery from far along the block.
        /// </summary>
        private static void BuildBladeSign(
            Transform parent,
            Vector3 doorPosition,
            Vector3 direction,
            Vector3 tangent,
            bool frontageIsX,
            Material emissiveMaterial,
            bool clipToHomeExterior)
        {
            Vector3 bladeCenter =
                doorPosition +
                direction * 0.62f +
                tangent *
                (SupermarketEntranceGeometry.StorefrontWidth * 0.5f +
                 0.42f) +
                Vector3.up * BladeCenterY;

            CreateBox(
                "Supermarket Blade Sign Housing",
                parent,
                bladeCenter,
                frontageIsX
                    ? new Vector3(1.02f, BladeHeight, 0.18f)
                    : new Vector3(0.18f, BladeHeight, 1.02f),
                SignHousingColor,
                null,
                clipToHomeExterior);
            CreateBox(
                "Supermarket Blade Sign Bracket",
                parent,
                bladeCenter -
                direction * 0.44f +
                Vector3.up * (BladeHeight * 0.5f - 0.14f),
                frontageIsX
                    ? new Vector3(0.42f, 0.10f, 0.10f)
                    : new Vector3(0.10f, 0.10f, 0.42f),
                SignHousingColor,
                null,
                clipToHomeExterior);

            float top = (SignWord.Length - 1) * 0.5f * BladeRowStep;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 faceOffset = tangent * (side * 0.115f);
                for (int row = 0; row < SignWord.Length; row++)
                {
                    IReadOnlyList<SignSegmentRect> glyph =
                        CitySignLettering.Layout(
                            SignWord[row].ToString(),
                            0.46f,
                            0.30f,
                            1f);
                    float rowY = top - row * BladeRowStep;
                    for (int index = 0; index < glyph.Count; index++)
                    {
                        SignSegmentRect segment = glyph[index];
                        // Each face flips the glyphs along the read
                        // axis so both approaches read forward — the
                        // asymmetric Р and К must never mirror.
                        CreateGlowBox(
                            "Supermarket Blade Sign Letter",
                            parent,
                            bladeCenter +
                            faceOffset +
                            direction * (segment.Center.x * side) +
                            Vector3.up * (rowY + segment.Center.y),
                            frontageIsX
                                ? new Vector3(
                                    segment.Size.x,
                                    segment.Size.y,
                                    0.04f)
                                : new Vector3(
                                    0.04f,
                                    segment.Size.y,
                                    segment.Size.x),
                            LetterColor,
                            emissiveMaterial,
                            clipToHomeExterior);
                    }
                }
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
