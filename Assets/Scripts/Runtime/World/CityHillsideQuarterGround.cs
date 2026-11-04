using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal static class CityHillsideQuarterGround
    {
        // Same terrain skin as the adjacent switchbacks. All stair openings and
        // existing bank break lines are vertices in the visual and physical mesh.
        public static void Build(Transform parent, CityLayout layout)
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
            for (int j = 0; j < zAxis.Count; j++)
            {
                var section = CityHillsideQuarterRavine.At(zAxis[j]);
                for (int i = 0; i < columns; i++)
                {
                    int a = j * columns + i;
                    Vector2 point = new Vector2(CityHillsideQuarterRavine.MeshX(xAxis[i], section), zAxis[j]);
                    float height = plan.SampleSoil(point);
                    vertices[a] = vertices[a + gridSize] = new Vector3(point.x, height, point.y);
                    uv[a] = point * .35f;
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
                    bool opening = false;
                    foreach (Rect cut in court.WalkableBounds)
                        if (cut.Contains(centre)) { opening = true; break; }
                    if (opening) continue;
                    bool isRock = CityHillsideQuarterRavine.IsOpenRockColumn(templateX, midZ);
                    var triangles = isRock ? rock : faces;
                    if (isRock) a += gridSize;
                    int b = a + columns;
                    triangles.Add(a); triangles.Add(b); triangles.Add(a + 1);
                    triangles.Add(a + 1); triangles.Add(b); triangles.Add(b + 1);
                }
            }
            var mesh = new Mesh { name = "Upper quarter saddle and dry rock cut", vertices = vertices, uv = uv, subMeshCount = 2 };
            mesh.SetTriangles(faces, 0);
            mesh.SetTriangles(rock, 1);
            mesh.RecalculateNormals();
            var ground = new GameObject("Shared residential hillside");
            ground.transform.SetParent(parent, false);
            ground.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = ground.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { RuntimePrimitiveFactory.DefaultMaterial, RuntimePrimitiveFactory.DefaultMaterial };
            CityExteriorAppearance.ApplyGroundSurface(renderer, new Color(.49f, .50f, .43f), 0);
            CityMountainSurfaceAppearance.ApplyCombined(renderer, new Color(.36f, .375f, .355f), 1);
            ground.AddComponent<MeshCollider>().sharedMesh = mesh;
            ground.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
            var kinds = new FootstepGroundKind[(faces.Count + rock.Count) / 3];
            for (int i = 0; i < kinds.Length; i++)
                kinds[i] = i < faces.Count / 3 ? FootstepGroundKind.Soil : FootstepGroundKind.Stone;
            FootstepGround.Stamp(ground, FootstepGroundKind.Soil).SetTriangleKinds(kinds);
            CityHillsideQuarterBridgeWorld.Build(parent, plan);
        }
    }
}
