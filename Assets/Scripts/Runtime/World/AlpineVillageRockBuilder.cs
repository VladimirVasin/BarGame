using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    internal static class AlpineVillageRockBuilder
    {
        public const string RootName = "Village Bedded Rock";
        // These are surface albedos before the shared ridge shader lights
        // and hazes them. Pre-darkening ledge snow as though it were already
        // in shadow loses its contrast again to light, fog and the PS1 grade.
        private static readonly Color StoneTint = new Color(0.36f, 0.385f, 0.385f, 1f);
        private static readonly Color SnowTint = new Color(0.84f, 0.86f, 0.85f, 1f);

        public static void Build(Transform parent, AlpineVillagePlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            VillageRockAssetProvider kit = VillageRockAssetProvider.LoadOrThrow();
            IReadOnlyList<AlpineVillageRockPlacement> placements = AlpineVillageRockPlanner.Create(plan);
            var root = new GameObject(RootName);
            root.transform.SetParent(parent, false);
            for (int index = 0; index < placements.Count; index++)
            {
                AlpineVillageRockPlacement placement = placements[index];
                var mass = new GameObject($"Bedded Rock {index:00} Variant {placement.Variant}");
                mass.transform.SetParent(root.transform, false);
                mass.transform.SetPositionAndRotation(placement.Position, placement.Rotation);
                AddPart(mass.transform, kit, placement, VillageRockMeshRole.Stone);
                AddPart(mass.transform, kit, placement, VillageRockMeshRole.Snow);
            }

        }


        private static void AddPart(Transform parent, VillageRockAssetProvider kit,
            AlpineVillageRockPlacement placement, VillageRockMeshRole role)
        {
            VillageRockMeshEntry entry = kit.GetPartOrThrow(placement.Variant, role);
            var host = new GameObject(role.ToString());
            host.transform.SetParent(parent, false);
            host.transform.localScale = entry.ImportedScale;
            host.AddComponent<MeshFilter>().sharedMesh = entry.Mesh;
            MeshRenderer renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = AlpineVillageRidgeAppearance.RidgeMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            bool snow = role == VillageRockMeshRole.Snow;
            MountainRoadSurfaceKind surface = snow ? MountainRoadSurfaceKind.WindSnow :
                MountainRoadSurfaceKind.LayeredStone;
            Color tint = snow ? SnowTint : StoneTint;
            tint.r += placement.TintVariation;
            tint.g += placement.TintVariation;
            tint.b += placement.TintVariation;
            float uv = 1f / MountainRoadSurfaceAppearance.GetRecipe(surface).MetersPerTile;
            AlpineVillageRidgeAppearance.Apply(renderer, 0, surface, tint,
                new Vector4(uv, uv, 0f, 0f), snow ? 1.6f : 1f);
        }
    }
}
