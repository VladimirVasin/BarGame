using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>One street-facing fabrication hall with three glazed roof bays and a visible work station.</summary>
    internal static class CityMetalWorkshop
    {
        internal const string ModelPath = "City/MetalWorkshop/MetalWorkshop";
        internal static readonly Vector3 WindowLocal = new(-7.75f, 1.98f, .50f);
        private static readonly ConditionalWeakTable<CityLayout, Site> Sites = new();
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        private sealed class Site
        {
            internal readonly BuildingLot Lot;

            internal Site(CityLayout layout)
            {
                float best = float.NegativeInfinity;
                foreach (BuildingLot candidate in layout.BuildingLots)
                {
                    if (candidate.IndustrialPurpose != CityIndustrialPurpose.MetalWorkshop || !candidate.HasRoadFrontage)
                        continue;
                    // Prefer a public street on the window side, then the candidate
                    // closest to the river. Both City and the balcony select by the same survey.
                    Vector2Int front = candidate.FrontageDirection;
                    Vector2Int left = new(-front.y, front.x);
                    var side = RoadEdge.ForCellFrontage(candidate.Cell, left);
                    bool street = layout.HasRoad(side) && layout.GetPathKind(side) == CityPathKind.Street;
                    float score = (street ? 1000f : 0f) - Mathf.Abs(candidate.Center.x);
                    if (score <= best) continue;
                    best = score;
                    Lot = candidate;
                }
            }
        }

        internal static BuildingLot Select(CityLayout layout) =>
            layout.SpatialPlan.IsCompactCoastal ? Sites.GetValue(layout, value => new Site(value)).Lot : null;

        internal static void Build(CityLayout layout, BuildingLot lot, CityBuildingAssetRegistry registry)
        {
            if (lot.IndustrialPurpose != CityIndustrialPurpose.MetalWorkshop || lot != Select(layout)) return;
            var asset = Resources.Load<GameObject>(ModelPath);
            var glass = Resources.Load<Material>("Pawnshop/Materials/PawnshopGlass");
            var wire = Resources.Load<Texture2D>("City/MetalWorkshop/WiredGlass");
            if (!asset || !glass || !wire) throw new InvalidOperationException("Missing glazed fabrication hall assets.");
            var model = UnityEngine.Object.Instantiate(asset, registry.transform, false);
            model.name = "Glazed fabrication hall";
            Renderer originalWall = null, originalRoof = null;
            foreach (var part in registry.Parts)
            {
                if (part.Role == CityBuildingMeshRole.FacadePrimary) originalWall = part.Renderer;
                if (part.Role == CityBuildingMeshRole.Roof) originalRoof = part.Renderer;
            }
            if (!originalWall || !originalRoof) throw new InvalidOperationException("Metal workshop shell has no wall or roof.");
            var block = new MaterialPropertyBlock();
            foreach (var part in model.GetComponentsInChildren<MeshRenderer>())
            {
                switch (part.name)
                {
                    case "Wall": Replace(originalWall, part); break;
                    case "Roof": Replace(originalRoof, part); break;
                    case "ClearGlass":
                    case "WireGlass":
                    case "RepairGlass":
                        block.Clear();
                        bool wired = part.name != "ClearGlass";
                        float opacity = part.name == "RepairGlass" ? .54f : .84f;
                        if (wired) block.SetTexture(BaseMap, wire);
                        block.SetColor(BaseColor, wired ? new Color(.64f, .69f, .66f, opacity) : new Color(.66f, .72f, .69f, .18f));
                        part.sharedMaterial = glass;
                        part.SetPropertyBlock(block);
                        part.shadowCastingMode = ShadowCastingMode.Off;
                        break;
                    case "BackWall": Surface(part, CityBuildingSurfaceKind.EquipmentConcrete, new Color(.77f,.77f,.70f)); break;
                    case "Floor": Surface(part, CityBuildingSurfaceKind.EquipmentConcrete, new Color(.46f,.44f,.40f)); break;
                    case "Timber": Surface(part, CityBuildingSurfaceKind.CargoTimber, new Color(.71f,.62f,.48f)); break;
                    case "Hoist": Surface(part, CityBuildingSurfaceKind.EquipmentSteel, new Color(.70f,.52f,.27f)); break;
                    case "Stock": Surface(part, CityBuildingSurfaceKind.EquipmentSteel, new Color(.70f,.69f,.65f)); break;
                    case "Chain": Surface(part, CityBuildingSurfaceKind.EquipmentSteel, new Color(.45f,.43f,.40f)); break;
                    case "Steel":
                    case "Frames": Surface(part, CityBuildingSurfaceKind.EquipmentSteel, new Color(.41f,.45f,.42f)); break;
                    default: throw new InvalidOperationException("Unknown metal workshop part " + part.name);
                }
            }

            void Replace(Renderer original, Renderer replacement)
            {
                // Keep the catalog registry and its measured bounds valid while this
                // instance draws the cut shell using the exact original surface chart.
                original.GetPropertyBlock(block);
                replacement.sharedMaterial = original.sharedMaterial;
                replacement.SetPropertyBlock(block);
                original.forceRenderingOff = true;
            }
        }

        private static void Surface(Renderer part, CityBuildingSurfaceKind kind, Color tint) =>
            CityBuildingSurfaceAppearance.Apply(part, CityDistrictKind.Industrial, kind, tint, CityIndustrialPurpose.MetalWorkshop);
    }
}
