using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The cemetery's ground slab. Unlike the continuous city terrain
    /// this precinct is a solid block of boxes reaching from the
    /// terrain floor up to the soil top, which is what makes a grave
    /// diggable at all: subtract the hole's rectangle from the patch
    /// list and the slab comes back with a real rectangular void
    /// through it, walls and all, instead of a decal painted on a
    /// skin.
    ///
    /// Built once at world build with no excavations, and rebuilt by
    /// <see cref="CityCemeteryGroundExcavation"/> every time somebody
    /// digs.
    /// </summary>
    internal static class CityCemeteryGroundWorldBuilder
    {
        public const string ObjectName = "Cemetery Ground";

        /// <summary>How far the slab reaches below the lowest ground
        /// in the city, mirrored from the surface pass that used to
        /// own this geometry.</summary>
        public const float TerrainBottomDrop = 0.32f;
        public const float MinimumSlabHeight = 0.32f;

        /// <summary>
        /// Null when the blueprint carries no cemetery cells at all,
        /// or when the excavations happen to swallow every one of
        /// them — the same silent bail the surface pass always had.
        /// </summary>
        public static GameObject Build(
            Transform parent,
            CityLayout layout,
            IReadOnlyList<Rect> excavations)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            var boxes = new List<Bounds>();
            float terrainBottom =
                layout.ElevationPlan.MinimumElevation - TerrainBottomDrop;
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                if (surface.Kind != CitySurfaceKind.CemeteryGround ||
                    CityTerrainSurfacePlan.UsesContinuousTop(surface))
                {
                    continue;
                }

                float topY = surface.PhysicalTopY;
                float height = Mathf.Max(
                    MinimumSlabHeight,
                    topY - terrainBottom);
                float centerY = topY - height * 0.5f;
                List<Rect> patches = CityTerrainSurfaceWorldBuilder
                    .CreateSurfacePatches(layout, surface, excavations);
                for (int patchIndex = 0;
                     patchIndex < patches.Count;
                     patchIndex++)
                {
                    Rect patch = patches[patchIndex];
                    boxes.Add(new Bounds(
                        new Vector3(
                            patch.center.x,
                            centerY,
                            patch.center.y),
                        new Vector3(
                            patch.width,
                            height,
                            patch.height)));
                }
            }

            if (boxes.Count == 0)
            {
                return null;
            }

            // The cemetery slab gets the leaf-litter soil sheet the
            // way roads get asphalt: planar world UVs at the soil
            // pitch, tinted with the same colour it always had.
            GameObject ground =
                RuntimePrimitiveFactory.CreateCombinedBoxes(
                    ObjectName,
                    parent,
                    boxes,
                    CityExteriorAppearance.CemeteryGround,
                    true,
                    CityCemeterySurfaceAppearance.GetRecipe(
                        CityCemeterySurfaceKind.Soil).MetersPerTile,
                    // The terrace is a solid slab, so wherever it meets
                    // lower ground its side is a real wall the player
                    // sees. Planar XZ smears the soil sheet down that
                    // wall in vertical streaks; box projection gives
                    // each face its own plane and leaves the top, which
                    // is horizontal, projected exactly as before.
                    RuntimeWorldUvMode.BoxProjected);
            CityCemeterySurfaceAppearance.ApplyCombined(
                ground.GetComponent<Renderer>(),
                CityCemeterySurfaceKind.Soil,
                CityExteriorAppearance.CemeteryGround);
            return ground;
        }
    }
}
