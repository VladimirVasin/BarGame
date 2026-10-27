using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Four shared surface batches and small invisible collision proxies.</summary>
    public static class CityParkFurnitureWorldBuilder
    {
        public const string RootName = "Park Furniture";

        public static GameObject Build(Transform parent, CityLayout layout,
            CityDecorationPlan decorations)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            CityParkFurniturePlan plan = CityParkFurniturePlan.Create(layout, decorations);
            if (plan.Placements.Count == 0) return null;
            CityMiscAssetProvider provider = CityMiscAssetProvider.LoadOrThrow();
            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var batches = new Dictionary<int, List<RuntimeMeshPlacement>>(4);
            foreach (CityParkFurniturePlacement placement in plan.Placements)
            {
                int count = CityMiscAssetProvider.GetPartCount(placement.Kind);
                for (int index = 0; index < count; index++)
                {
                    CityMiscMeshPart part = provider.GetPartOrThrow(placement.Kind, 0, index);
                    int key = (int)part.Surface * 16 + (int)part.Role;
                    if (!batches.TryGetValue(key, out List<RuntimeMeshPlacement> meshes))
                    {
                        meshes = new List<RuntimeMeshPlacement>(3);
                        batches.Add(key, meshes);
                    }
                    meshes.Add(new RuntimeMeshPlacement(part.Mesh,
                        placement.Position, placement.Rotation));
                }
                BuildCollision(root, placement);
            }
            foreach (KeyValuePair<int, List<RuntimeMeshPlacement>> batch in batches)
            {
                CityMiscSurfaceKind surface = (CityMiscSurfaceKind)(batch.Key / 16);
                CityMiscMeshRole role = (CityMiscMeshRole)(batch.Key % 16);
                CityParkSurfaceKind parkSurface = surface == CityMiscSurfaceKind.Timber
                    ? CityParkSurfaceKind.Timber : CityParkSurfaceKind.PaintedMetal;
                Color color = ResolveColor(role, parkSurface);
                GameObject geometry = RuntimePrimitiveFactory.CreateCombinedMeshes(
                    $"Park Furniture {role} {surface}", root, batch.Value, color,
                    worldUvTileSize: CityParkSurfaceAppearance.GetRecipe(parkSurface).MetersPerTile,
                    uvMode: CityParkSurfaceAppearance.GetUvMode(parkSurface));
                CityParkSurfaceAppearance.ApplyCombined(geometry.GetComponent<Renderer>(),
                    parkSurface, color);
            }
            return root.gameObject;
        }

        private static Color ResolveColor(CityMiscMeshRole role, CityParkSurfaceKind surface)
        {
            if (surface == CityParkSurfaceKind.Timber)
                return role == CityMiscMeshRole.Residential
                    ? new Color(0.275f, 0.255f, 0.205f, 1f)
                    : new Color(0.19f, 0.205f, 0.17f, 1f);
            return role == CityMiscMeshRole.Residential
                ? new Color(0.20f, 0.25f, 0.21f, 1f)
                : new Color(0.13f, 0.155f, 0.145f, 1f);
        }

        private static void BuildCollision(Transform parent, CityParkFurniturePlacement placement)
        {
            Transform root = new GameObject(placement.Id).transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(placement.Position, placement.Rotation);
            if (placement.Kind == CityMiscKind.ParkBin)
            {
                AddBox(root, new Vector3(0f, 0.4725f, 0f),
                    new Vector3(0.78f, 0.945f, 0.535f));
                return;
            }
            // The clear space below the board stays physically empty.
            AddBox(root, new Vector3(-0.81f, 0.60f, 0.04f),
                new Vector3(0.12f, 1.20f, 0.14f));
            AddBox(root, new Vector3(0.81f, 0.60f, 0.04f),
                new Vector3(0.12f, 1.20f, 0.14f));
            AddBox(root, new Vector3(0f, 1.66f, 0f),
                new Vector3(1.72f, 1.22f, 0.13f));
        }

        private static void AddBox(Transform root, Vector3 center, Vector3 size)
        {
            BoxCollider box = root.gameObject.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
        }
    }
}
