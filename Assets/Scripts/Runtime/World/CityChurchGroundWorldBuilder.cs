using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Builds the church precinct's authoritative solid ground separately
    /// from generic Yard terrain. This keeps the typed ChurchGround surface
    /// available to planning, traversal and map presentation without letting
    /// the residual east-yard dresser claim it.
    /// </summary>
    public static class CityChurchGroundWorldBuilder
    {
        public const string ObjectName = "Church Ground";
        public const float TerrainBottomDrop = 0.32f;
        public const float MinimumSlabHeight = 0.32f;

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

            var boxes = new List<Bounds>(8);
            float terrainBottom =
                layout.ElevationPlan.MinimumElevation - TerrainBottomDrop;
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                if (surface.Kind != CitySurfaceKind.ChurchGround)
                {
                    continue;
                }

                float topY = surface.PhysicalTopY;
                float height = Mathf.Max(
                    MinimumSlabHeight,
                    topY - terrainBottom);
                List<Rect> patches = CityTerrainSurfaceWorldBuilder
                    .CreateSurfacePatches(layout, surface);
                for (int patchIndex = 0;
                     patchIndex < patches.Count;
                     patchIndex++)
                {
                    Rect patch = patches[patchIndex];
                    boxes.Add(new Bounds(
                        new Vector3(
                            patch.center.x,
                            topY - height * 0.5f,
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

            GameObject ground = RuntimePrimitiveFactory.CreateCombinedBoxes(
                ObjectName,
                parent,
                boxes,
                CityExteriorAppearance.ChurchGround,
                true,
                CityExteriorAppearance.GroundTextureTileSize);
            CityExteriorAppearance.ApplyGroundSurface(
                ground.GetComponent<Renderer>(),
                CityExteriorAppearance.ChurchGround);
            return ground;
        }
    }
}
