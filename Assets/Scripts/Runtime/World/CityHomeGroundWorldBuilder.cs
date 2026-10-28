using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Bounded collider-free copy of the City's actual terrain support.</summary>
    internal static class CityHomeGroundWorldBuilder
    {
        internal static void Build(Transform parent, HomeExteriorContextPlan home)
        {
            float radius = HomeExteriorContextPlanner.ViewRadius;
            Vector3 anchor = home.PlayerHome.DoorPosition;
            Rect retained = Rect.MinMaxRect(anchor.x - radius, anchor.z - radius, anchor.x + radius, anchor.z + radius);
            var groups = new HashSet<(CitySurfaceKind kind, string area)>();
            foreach (CitySurfaceDescriptor surface in home.Layout.Surfaces)
            {
                if (surface.Kind != CitySurfaceKind.BuildableGround && surface.Kind != CitySurfaceKind.ParkGround &&
                    surface.Kind != CitySurfaceKind.OpenGround) continue;
                if (!retained.Overlaps(surface.WorldBounds) || !groups.Add((surface.Kind, surface.AreaId))) continue;
                bool lawn = surface.Kind == CitySurfaceKind.ParkGround;
                float tile = lawn ? CityParkSurfaceAppearance.GetRecipe(CityParkSurfaceKind.Lawn).MetersPerTile : CityExteriorAppearance.GroundTextureTileSize;
                CityTerrainMeshSource source = CityTerrainSurfaceWorldBuilder.CreateMeshSource(home.Layout, surface.Kind,
                    tile, null, CityTerrainSurfaceAreaFilter.IncludeOnly(new[] { surface.AreaId }), false,
                    CityBeachSandPlan.MeshPitch, retained);
                if (source.IsEmpty) continue;
                var vertices = new List<Vector3>(); var normals = new List<Vector3>();
                var uv = new List<Vector2>(); var triangles = new List<int>();
                for (int index = 0; index < source.Triangles.Count; index += 3)
                {
                    var polygon = new[] { source.Vertices[source.Triangles[index]],
                        source.Vertices[source.Triangles[index + 1]], source.Vertices[source.Triangles[index + 2]] };
                    List<Vector3> clipped = CityStreetSurfaceWorldBuilder.ClipHome(polygon, home);
                    for (int fan = 1; fan < clipped.Count - 1; fan++)
                    {
                        Vector3 a = clipped[0], b = clipped[fan], c = clipped[fan + 1];
                        Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                        if (normal.sqrMagnitude < .5f) continue;
                        foreach (Vector3 point in new[] { a, b, c })
                        {
                            triangles.Add(vertices.Count);
                            vertices.Add(PlayerHomeBalconyGeometry.ToHomeLocal(home.PlayerHome, point));
                            normals.Add(PlayerHomeBalconyGeometry.ToHomeLocalDirection(home.PlayerHome, normal));
                            uv.Add(new Vector2(point.x / tile, point.z / tile));
                        }
                    }
                }
                if (triangles.Count == 0) continue;
                var mesh = new Mesh { name = "Home Actual Terrain " + surface.AreaId,
                    indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
                var result = new GameObject(mesh.name); result.transform.SetParent(parent, false);
                result.AddComponent<MeshFilter>().sharedMesh = mesh;
                result.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
                MeshRenderer renderer = result.AddComponent<MeshRenderer>(); renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
                if (lawn) CityParkSurfaceAppearance.ApplyCombined(renderer, CityParkSurfaceKind.Lawn, new Color(.16f, .30f, .18f));
                else
                {
                    Color tint = Color.white;
                    foreach (CityDistrictDescriptor district in home.Layout.Districts)
                        if (district.AreaId == surface.AreaId)
                        { tint = CityExteriorAppearance.ResolveDistrictGroundTint(CityDistrictPresentationPlanner.GetProfile(district.Kind).Wear); break; }
                    CityExteriorAppearance.ApplyGroundSurface(renderer, tint);
                }
                FootstepGround.Stamp(result, lawn ? FootstepGroundKind.Grass : FootstepGroundKind.Soil);
                CityGroundSurfaceSystem.Register(result, 0);
            }
        }
    }
}
