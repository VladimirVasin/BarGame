using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Passive imported meshes share three bounded river-surface batches.</summary>
    public static class CityRiverDressingWorldBuilder
    {
        public const string RootName = "River Construction Details";
        public const int MaximumBatchCount = 3;

        public static GameObject Build(Transform parent, CityLayout layout)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            CityRiverDressingPlan plan = CityRiverDressingPlan.Create(layout);
            if (plan.Placements.Count == 0) return null;
            CityMiscAssetProvider provider = CityMiscAssetProvider.LoadOrThrow();
            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);
            var batches = new Dictionary<CityRiverSurfaceKind, List<RuntimeMeshPlacement>>(MaximumBatchCount);
            foreach (CityRiverDressingPlacement placement in plan.Placements)
            {
                int partCount = CityMiscAssetProvider.GetPartCount(placement.Kind);
                for (int index = 0; index < partCount; index++)
                {
                    CityMiscMeshPart part = provider.GetPartOrThrow(placement.Kind, 0, index);
                    CityRiverSurfaceKind surface = ResolveSurface(placement.Kind, part.Surface);
                    if (!batches.TryGetValue(surface, out List<RuntimeMeshPlacement> meshes))
                        batches.Add(surface, meshes = new List<RuntimeMeshPlacement>(256));
                    meshes.Add(new RuntimeMeshPlacement(part.Mesh, placement.Position,
                        placement.Rotation, placement.Scale));
                }
                if (placement.Kind == CityMiscKind.RiverMooringBollard)
                    AddBollardCollision(root, placement);
            }
            foreach (KeyValuePair<CityRiverSurfaceKind, List<RuntimeMeshPlacement>> batch in batches)
            {
                Color color = batch.Key == CityRiverSurfaceKind.Iron
                    ? new Color(0.10f, 0.125f, 0.12f)
                    : batch.Key == CityRiverSurfaceKind.Paving
                        ? new Color(0.38f, 0.395f, 0.37f)
                        : new Color(0.39f, 0.405f, 0.375f);
                GameObject geometry = RuntimePrimitiveFactory.CreateCombinedMeshes(
                    "River Details " + batch.Key + (batch.Key == CityRiverSurfaceKind.Iron ? "_PaintedMetal" : ""),
                    root, batch.Value, color,
                    worldUvTileSize: CityRiverSurfaceAppearance.GetRecipe(batch.Key).MetersPerTile,
                    uvMode: RuntimeWorldUvMode.BoxProjected);
                CityRiverSurfaceAppearance.ApplyCombined(geometry.GetComponent<Renderer>(), batch.Key, color);
            }
            return root.gameObject;
        }

        internal static CityRiverSurfaceKind ResolveSurface(CityMiscKind kind, CityMiscSurfaceKind surface)
            => surface == CityMiscSurfaceKind.PaintedMetal ? CityRiverSurfaceKind.Iron
                : kind == CityMiscKind.RiverStairNosing ? CityRiverSurfaceKind.Paving
                : CityRiverSurfaceKind.Quay;

        private static void AddBollardCollision(Transform parent, CityRiverDressingPlacement placement)
        {
            Transform root = new GameObject(placement.Id).transform;
            root.SetParent(parent, false);
            root.localPosition = placement.Position;
            root.localRotation = placement.Rotation;
            BoxCollider box = root.gameObject.AddComponent<BoxCollider>();
            box.center = Vector3.up * (CityRiverDressingPlan.BollardHeightMeters * 0.5f);
            box.size = new Vector3(CityRiverDressingPlan.BollardWidthMeters,
                CityRiverDressingPlan.BollardHeightMeters, CityRiverDressingPlan.BollardWidthMeters);
        }
    }
}
