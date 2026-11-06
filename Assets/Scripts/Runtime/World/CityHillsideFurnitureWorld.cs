using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Imported furniture, shared materials and existing collision/interaction contracts.</summary>
    internal static class CityHillsideFurnitureWorld
    {
        internal static void Build(Transform parent, CityLayout layout, Rect? retained = null)
        {
            var plan = CityHillsideFurniturePlan.ForLayout(layout);
            if (plan.Placements.Count == 0) return;
            var root = new GameObject("Ridge Street Furniture").transform;
            root.SetParent(parent, false);
            var benches = new Dictionary<Vector2Int, List<RuntimeMeshPlacement>>();
            var utilities = new List<CityDecorationDescriptor>();
            var provider = CityMiscAssetProvider.LoadOrThrow();
            foreach (var placed in plan.Placements)
            {
                if (retained.HasValue && !retained.Value.Overlaps(placed.Bounds)) continue;
                if (placed.Kind != CityMiscKind.ParkBench)
                {
                    utilities.Add(new CityDecorationDescriptor(placed.Id,
                        placed.Kind == CityMiscKind.RoadsidePhoneBooth ? CityDecorationKind.RoadsidePhoneBooth
                            : CityDecorationKind.RoadsideDumpsterAndUtility,
                        CityDecorationAnchorKind.Roadside, CityDistrictKind.Residential, CityDecorationDescriptor.NoLot,
                        placed.Position, placed.Forward, 0, CityDecorationPalette.StreetUtility,
                        CityDecorationVisibilityTier.MidRange, retained.HasValue ? CityDecorationCollisionTier.None
                            : CityDecorationCollisionTier.Blocking, fixedSupportPlane: true));
                    continue;
                }
                var key = new Vector2Int(Mathf.FloorToInt(placed.Position.x / 48f), Mathf.FloorToInt(placed.Position.z / 48f));
                if (!benches.TryGetValue(key, out var parts)) { parts = new(); benches.Add(key, parts); }
                for (int i = 0; i < CityMiscAssetProvider.GetPartCount(placed.Kind); i++)
                {
                    var part = provider.GetPartOrThrow(placed.Kind, placed.Variant, i);
                    parts.Add(new RuntimeMeshPlacement(part.Mesh, placed.Position, placed.Rotation));
                }
                if (!retained.HasValue)
                {
                    var solid = new GameObject(placed.Id).transform;
                    solid.SetParent(root, false);
                    solid.SetPositionAndRotation(placed.Position, placed.Rotation);
                    var collider = solid.gameObject.AddComponent<BoxCollider>();
                    collider.center = new Vector3(0f, .48f, 0f);
                    collider.size = new Vector3(2.3f, .96f, .72f);
                }
            }
            foreach (var chunk in benches)
            {
                var color = new Color(.285f, .27f, .235f);
                var geometry = RuntimePrimitiveFactory.CreateCombinedMeshes($"Ridge benches {chunk.Key.x},{chunk.Key.y}",
                    root, chunk.Value, color,
                    worldUvTileSize: CityParkSurfaceAppearance.GetRecipe(CityParkSurfaceKind.Timber).MetersPerTile,
                    uvMode: CityParkSurfaceAppearance.GetUvMode(CityParkSurfaceKind.Timber));
                CityParkSurfaceAppearance.ApplyCombined(geometry.GetComponent<Renderer>(), CityParkSurfaceKind.Timber, color);
            }
            if (utilities.Count != 0) CityDecorationWorldBuilder.BuildDescriptors(root, layout, utilities);
        }
    }
}
