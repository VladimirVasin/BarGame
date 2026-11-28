using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Seeded planting stations shared by the city floor and Ridge earth cells.</summary>
    internal static class CitySoilTrees
    {
        private const float Spacing = 13.5f;
        internal const string RootName = "Open-ground trees";
        private static readonly ConditionalWeakTable<CityLayout, Survey> Surveys = new();

        private readonly struct Plant
        {
            internal readonly Vector3 Root;
            internal readonly CityTreeForm Form;
            internal readonly CityLeafHabitat Habitat;
            internal Plant(Vector3 root, CityTreeForm form, CityLeafHabitat habitat)
            { Root = root; Form = form; Habitat = habitat; }
        }

        private sealed class Survey
        {
            internal readonly List<Plant> Plants = new();
            internal readonly Dictionary<Vector2Int, Plant> Cells = new();
        }

        private static Vector2Int Cell(Vector2 p) =>
            new(Mathf.FloorToInt(p.x / Spacing), Mathf.FloorToInt(p.y / Spacing));

        private static Vector2 Station(int seed, int x, int z, uint salt)
        {
            uint h = CityGrassPlan.Hash(seed, x, z, salt);
            return new Vector2(x + .18f + .64f * CityGrassPlan.Unit(h), z + .18f + .64f * CityGrassPlan.Unit(h >> 16)) * Spacing;
        }

        internal static IEnumerable<Vector2> Candidates(Rect area, int seed, uint salt = 2309)
        {
            Vector2Int lo = Cell(area.min), hi = Cell(area.max);
            for (int z = lo.y; z <= hi.y; z++)
            for (int x = lo.x; x <= hi.x; x++)
            {
                Vector2 p = Station(seed, x, z, salt);
                if (area.Contains(p)) yield return p;
            }
        }

        internal static void BuildLower(Transform parent, CityLayout city, CityGrassSurface surface,
            List<CityLeafSite> sites, RoadWalkableArea walkable)
        {
            bool replay = surface.Home != null;
            if (!Surveys.TryGetValue(city, out var survey))
            {
                survey = SurveySoil(city, surface, sites);
                if (!replay) Surveys.Add(city, survey);
            }
            var trees = new GameObject(RootName).transform;
            trees.SetParent(parent, false);
            var trunks = new List<Vector2[]>();
            foreach (Plant plant in survey.Plants)
            {
                if (replay && (plant.Root - surface.Home.PlayerHome.DoorPosition).sqrMagnitude >
                    HomeExteriorContextPlanner.ViewRadius * HomeExteriorContextPlanner.ViewRadius) continue;
                CityTreeForm form = plant.Form;
                var frame = new GameObject("Soil tree").transform;
                frame.SetParent(trees, false);
                frame.SetPositionAndRotation(surface.FromCity(plant.Root), surface.FromCityRotation * form.Rotation);
                CityHillsideSiteDetails.BuildPlant(frame, CityMiscKind.ParkTree, form.Variant, !replay, form);
                sites.Add(new CityLeafSite(plant.Root, form.LeafHeight, plant.Habitat, city.Seed));
                if (!replay) trunks.Add(CityHillsideSiteDetails.TrunkFootprint(plant.Root, form.Rotation, form.Wood.x));
            }
            walkable?.ExcludePolygons(trunks);
        }

        private static Survey SurveySoil(CityLayout city, CityGrassSurface surface, List<CityLeafSite> existing)
        {
            var result = new Survey();
            var district = new CityDistrictStreamingPlan(city);
            var spans = CityCoastalInfrastructurePlan.ForLayout(city).CableSpans;
            var east = CityWorldPlans.GetOrCreate(city).EastExit;
            // Alternate stations find the broken-up strips of exposed dirt between the pavement
            // and buildings. One accepted tree per cell, with space across neighbouring cells.
            for (uint pass = 0; pass < 8; pass++)
            foreach (Vector2 p in Candidates(surface.Bounds, city.Seed, 2309 + pass))
            {
                var cell = Cell(p);
                if (result.Cells.ContainsKey(cell)) continue;
                if (east != null && east.IsEnabled && (east.YardBounds.Contains(p) || east.NorthYardBounds.Contains(p))) continue;
                if (!surface.TrySample(p, out var root, out var normal, out var habitat) ||
                    normal.y < .88f || !district.IsLower(root) || surface.LeafWater(surface.FromCity(root)) ||
                    habitat is CityGrassHabitat.Beach or CityGrassHabitat.Cemetery or CityGrassHabitat.Garden or CityGrassHabitat.PlaygroundEdge) continue;
                uint h = CityGrassPlan.Hash(city.Seed, cell.x, cell.y, 1629);
                if (habitat == CityGrassHabitat.Industrial && (h & 1) == 0) continue;
                var form = new CityTreeForm(city.Seed, root, (int)(h % 4));
                float clearance = form.Radius + CityWindField.MaximumTreeTravel + .35f;
                bool blocked = false;
                foreach (var source in existing)
                {
                    if (source.CrownHeight <= 0f) continue;
                    float gap = clearance + Mathf.Clamp(source.CrownHeight * .35f, 1.3f, 3.5f);
                    if ((source.Position - root).sqrMagnitude < gap * gap) { blocked = true; break; }
                }
                if (blocked) continue;
                for (int z = cell.y - 1; z <= cell.y + 1 && !blocked; z++)
                for (int x = cell.x - 1; x <= cell.x + 1; x++)
                    if (result.Cells.TryGetValue(new Vector2Int(x, z), out var neighbour) &&
                        (neighbour.Root - root).sqrMagnitude < Mathf.Pow(clearance + neighbour.Form.Radius, 2f))
                    { blocked = true; break; }
                if (blocked) continue;
                float rootY = root.y;
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * Mathf.PI * .25f;
                    var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    if (!surface.TrySample(p + direction * clearance, out var edge, out _, out _) ||
                        Mathf.Abs(edge.y - root.y) > clearance * .48f ||
                        !surface.TrySample(p + direction * form.TrunkRadius, out var foot, out _, out _))
                    { blocked = true; break; }
                    rootY = Mathf.Min(rootY, foot.y);
                }
                if (blocked) continue;
                foreach (var span in spans)
                    if (CityRidgeVegetationPlan.TouchesCable(span, p, rootY + form.Height, clearance))
                    { blocked = true; break; }
                if (blocked) continue;
                root.y = rootY;
                var plant = new Plant(root, form, habitat == CityGrassHabitat.Park ? CityLeafHabitat.Park : CityLeafHabitat.OpenGround);
                result.Plants.Add(plant);
                result.Cells.Add(cell, plant);
            }
            return result;
        }

        internal static bool BlocksArrival(CityLayout city, Vector2 p, float radius)
        {
            var cell = Cell(p);
            // Cold travel resolves its destination again after City composition in ResolveAreaSpawn.
            if (!Surveys.TryGetValue(city, out var survey) || !survey.Cells.TryGetValue(cell, out var plant)) return false;
            Vector3 offset = Quaternion.Inverse(plant.Form.Rotation) * new Vector3(p.x - plant.Root.x, 0, p.y - plant.Root.z);
            float half = plant.Form.TrunkRadius + radius;
            return Mathf.Abs(offset.x) < half && Mathf.Abs(offset.z) < half;
        }
    }
}
