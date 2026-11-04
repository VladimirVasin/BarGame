using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal static class CityHillsideQuarterGround
    {
        // Same terrain skin as the adjacent switchbacks. All stair openings and
        // existing bank break lines are vertices in the visual and physical mesh.
        public static void Build(Transform parent, CityLayout layout, Rect? retained = null)
        {
            var plan = CityHillsideQuarterPlan.ForLayout(layout);
            if (plan.Links.Count == 0) return;
            Rect bounds = plan.TerrainBounds;
            var xs = new SortedSet<float> { bounds.xMin, bounds.xMax, bounds.xMin + 9f, bounds.xMax - 9f };
            var zs = new SortedSet<float> { bounds.yMin, bounds.yMax, plan.Links[0].WestGateWorld.z - 20f };
            for (float x = bounds.xMin; x <= bounds.xMax; x += 3f) xs.Add(x);
            for (float z = bounds.yMin; z <= bounds.yMax; z += 3f) zs.Add(z);
            var bank = CityHillsideStreetPlan.ForLayout(layout).Routes[0];
            for (int i = 0; i < CityHillsideStreetRoute.LaneCount; i++)
            {
                float z = bank.GetLaneZ(i);
                if (z - 5f > bounds.yMin && z - 5f < bounds.yMax) zs.Add(z - 5f);
                if (z + 5f > bounds.yMin && z + 5f < bounds.yMax) zs.Add(z + 5f);
            }
            foreach (var link in plan.Links) { zs.Add(link.Bounds.yMin); zs.Add(link.Bounds.yMax); }
            var court = CityHillsideAccessPlan.ForLayout(layout).SharedCourt;
            foreach (Rect cut in court.WalkableBounds)
            {
                xs.Add(cut.xMin); xs.Add(cut.xMax);
                zs.Add(cut.yMin); zs.Add(cut.yMax);
            }
            // Fixed template columns bend with the cut. The outermost columns and
            // the court stay on the original mesh, including both bank seams.
            xs.Add(CityHillsideQuarterRavine.MeshWest);
            xs.Add(CityHillsideQuarterRavine.MeshEast);
            foreach (var p in CityHillsideQuarterRavine.Profile)
                xs.Add(Mathf.Lerp(CityHillsideQuarterRavine.TemplateWest, CityHillsideQuarterRavine.TemplateEast, p.x));
            foreach (var section in CityHillsideQuarterRavine.Sections) zs.Add(section.x);
            var xAxis = new List<float>(xs);
            var zAxis = new List<float>(zs);
            int columns = xAxis.Count;
            int gridSize = columns * zAxis.Count;
            var vertices = new Vector3[gridSize * 2];
            var uv = new Vector2[vertices.Length];
            var faces = new List<int>();
            var rock = new List<int>();
            var scree = new List<int>();
            for (int j = 0; j < zAxis.Count; j++)
            {
                var section = CityHillsideQuarterRavine.At(zAxis[j]);
                for (int i = 0; i < columns; i++)
                {
                    int a = j * columns + i;
                    Vector2 point = new Vector2(CityHillsideQuarterRavine.MeshX(xAxis[i], section), zAxis[j]);
                    float height = plan.SampleSoil(point);
                    vertices[a] = vertices[a + gridSize] = new Vector3(point.x, height, point.y);
                    uv[a] = point / CityExteriorAppearance.GroundTextureTileSize;
                    // Distance along the gully and across its section, in metres.
                    // Walls retain texture scale where their X span nearly vanishes.
                    float u = Mathf.InverseLerp(section.y, section.z, point.x);
                    float depth = CityHillsideQuarterRavine.Drop(point);
                    uv[a + gridSize] = new Vector2(point.y,
                        point.x + (u < .5f ? depth : 2f * section.w - depth)) / CityMountainSurfaceAppearance.MetersPerTile;
                    if (i + 1 == columns || j + 1 == zAxis.Count) continue;
                    float midZ = (point.y + zAxis[j + 1]) * .5f;
                    float templateX = (xAxis[i] + xAxis[i + 1]) * .5f;
                    var centre = new Vector2(CityHillsideQuarterRavine.MeshX(templateX, section), midZ);
                    if (retained.HasValue && !retained.Value.Overlaps(Rect.MinMaxRect(
                        point.x, point.y, CityHillsideQuarterRavine.MeshX(xAxis[i + 1], section), zAxis[j + 1]))) continue;
                    bool opening = false;
                    foreach (Rect cut in court.WalkableBounds)
                        if (cut.Contains(centre)) { opening = true; break; }
                    if (opening) continue;
                    bool isRock = CityHillsideQuarterRavine.IsOpenRockColumn(templateX, midZ);
                    // Loose chips settle on the relatively flat bottom of the cut.
                    // Its benches and the exposed steep walls keep the layered rock.
                    float across = Mathf.InverseLerp(CityHillsideQuarterRavine.TemplateWest,
                        CityHillsideQuarterRavine.TemplateEast, templateX);
                    bool looseFloor = isRock && section.w > 4f && across > .37f &&
                        across < .61f + .035f * Mathf.Sin(midZ * .21f);
                    var triangles = looseFloor ? scree : isRock ? rock : faces;
                    if (isRock) a += gridSize;
                    int b = a + columns;
                    triangles.Add(a); triangles.Add(b); triangles.Add(a + 1);
                    triangles.Add(a + 1); triangles.Add(b); triangles.Add(b + 1);
                }
            }
            var mesh = CityHillsidePerimeterSurface.Build(layout, "Upper quarter saddle and dry rock cut",
                vertices, uv, faces, rock, scree);
            var ground = new GameObject("Shared residential hillside");
            ground.transform.SetParent(parent, false);
            ground.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = ground.AddComponent<MeshRenderer>();
            var material = RuntimePrimitiveFactory.DefaultMaterial;
            renderer.sharedMaterials = new[] { material, material, material, material, material, material };
            CityExteriorAppearance.ApplyGroundSurface(renderer, CityHillsidePerimeterSurface.ResidentialSoil, 0);
            CityMountainSurfaceAppearance.ApplyCombined(renderer, new Color(.36f, .375f, .355f), 1);
            CityCemeterySurfaceAppearance.ApplyCombined(renderer, CityCemeterySurfaceKind.Gravel,
                new Color(.31f, .305f, .28f), 2);
            CityHillsidePerimeterSurface.SetTilePitch(renderer, 2, CityMountainSurfaceAppearance.MetersPerTile,
                CityCemeterySurfaceAppearance.GetRecipe(CityCemeterySurfaceKind.Gravel).MetersPerTile);
            CityExteriorAppearance.ApplyRoadSurface(renderer, 3);
            CityHillsidePerimeterSurface.ApplyVerges(renderer, 4, 5);
            if (!retained.HasValue) ground.AddComponent<MeshCollider>().sharedMesh = mesh;
            ground.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
            if (!retained.HasValue) CityHillsidePerimeterSurface.Stamp(ground, mesh, FootstepGroundKind.Soil,
                FootstepGroundKind.Stone, FootstepGroundKind.Stone, FootstepGroundKind.Concrete,
                FootstepGroundKind.Grass, FootstepGroundKind.Stone);
            CityHillsideQuarterBridgeWorld.Build(parent, plan, retained);
        }
    }
}
