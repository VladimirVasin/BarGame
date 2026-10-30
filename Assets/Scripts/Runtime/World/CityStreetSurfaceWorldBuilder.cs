using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>One street source, with typed material regions. Only imported
    /// Blender topology is deformed; every top and skirt uses the support plan.</summary>
    internal static class CityStreetSurfaceWorldBuilder
    {
        private const string ResourcePath = "City/StreetProfile/CityStreetProfile3D";
        private static Topology patchTopology, stripTopology;

        internal static GameObject Build(string name, Transform parent, CityLayout layout,
            CityStreetSurfacePlan street, HomeExteriorContextPlan home = null)
        {
            EnsureTopology();
            CityStreetSupportPlan plan = CityStreetSupportPlan.FromStreetPlan(layout, street);
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var road = new List<Vector4>();
            var colors = new List<Color>();
            var triangles = new[] { new List<int>(), new List<int>(), new List<int>() };
            var frames = new List<(RoadEdge edge, CityRoadPath path)>();
            foreach (RoadEdge edge in layout.RoadEdges)
                if (layout.GetPathKind(edge) == CityPathKind.Street) frames.Add((edge, layout.RoadGeometry.Get(edge)));
            foreach (CityStreetSupportPatch patch in plan.Patches)
            {
                if (home != null && !InHomeRange(patch.Bounds, home)) continue;
                int slot = (int)patch.Kind;
                float tile = patch.Kind == CityStreetSupportKind.Asphalt ? CityExteriorAppearance.RoadTextureTileSize :
                    patch.Kind == CityStreetSupportKind.Sidewalk ? CityExteriorAppearance.SidewalkTextureTileSize :
                    CityParkSurfaceAppearance.GetRecipe(CityParkSurfaceKind.Path).MetersPerTile;
                CityRoadPath frame = null;
                bool junction = false;
                if (patch.Kind == CityStreetSupportKind.Asphalt)
                {
                    float nearest = float.PositiveInfinity;
                    if (patch.Edge.HasValue) frame = layout.RoadGeometry.Get(patch.Edge.Value);
                    foreach (var candidate in frames)
                    {
                        if (patch.Edge.HasValue) break;
                        float distance = candidate.path.Project(patch.Bounds.center).DistanceSquared;
                        if (distance >= nearest) continue;
                        nearest = distance; frame = candidate.path;
                    }
                    junction = patch.FlatNode.HasValue || (!patch.Edge.HasValue &&
                        Mathf.Abs(patch.Bounds.width - layout.RoadWidth) < .02f &&
                        Mathf.Abs(patch.Bounds.height - layout.RoadWidth) < .02f);
                }
                IReadOnlyList<Vector3> contour = patch.TopVertices;
                float signedArea = 0f;
                for (int i = 0; i < contour.Count; i++)
                {
                    Vector3 a = contour[i], b = contour[(i + 1) % contour.Count];
                    signedArea += a.x * b.z - b.x * a.z;
                }
                if (patch.IsSupport)
                for (int i = 1; i < contour.Count - 1; i++)
                    AppendFace(contour[0], contour[i], contour[i + 1]);
                for (int i = 0; i < contour.Count; i++)
                {
                    Vector3 a = contour[i], b = contour[(i + 1) % contour.Count];
                    if (signedArea < 0f) { Vector3 swap = a; a = b; b = swap; }
                    AppendStrip(a, b, patch.Thickness);
                }

                void AppendFace(Vector3 a, Vector3 b, Vector3 c)
                {
                    // Imported SurfacePatch is CCW in XZ, hence normal +Y.
                    if (Vector3.Cross(c - a, b - a).y < 0f) { Vector3 swap = b; b = c; c = swap; }
                    Vector3 normal = Vector3.Cross(c - a, b - a).normalized;
                    if (normal.y < .00001f) return;
                    var polygon = new List<Vector3>(3) { a, b, c };
                    if (home != null) polygon = ClipHome(polygon, home);
                    for (int fan = 1; fan < polygon.Count - 1; fan++)
                    {
                        Vector3 first = polygon[0], second = polygon[fan], third = polygon[fan + 1];
                        int offset = vertices.Count;
                        foreach (Vector3 coefficient in patchTopology.Vertices)
                            AddVertex(first + (second - first) * coefficient.x + (third - first) * coefficient.z, normal);
                        foreach (int index in patchTopology.Indices) triangles[slot].Add(offset + index);
                    }
                }

                void AppendStrip(Vector3 a, Vector3 b, float depth)
                {
                    if (depth <= .00001f || (b - a).sqrMagnitude < .0000001f) return;
                    if (home != null)
                    {
                        float ax = PlayerHomeBalconyGeometry.ToHomeLocal(home.PlayerHome, a).x;
                        float bx = PlayerHomeBalconyGeometry.ToHomeLocal(home.PlayerHome, b).x;
                        if (ax < HomeExteriorViewBuilder.ContextMinimumX && bx < HomeExteriorViewBuilder.ContextMinimumX) return;
                        if ((ax < HomeExteriorViewBuilder.ContextMinimumX) != (bx < HomeExteriorViewBuilder.ContextMinimumX))
                        {
                            Vector3 intersection = Vector3.Lerp(a, b, (HomeExteriorViewBuilder.ContextMinimumX - ax) / (bx - ax));
                            if (ax < HomeExteriorViewBuilder.ContextMinimumX) a = intersection; else b = intersection;
                        }
                    }
                    Vector3 normal = Vector3.Cross(b - a, Vector3.down).normalized;
                    int offset = vertices.Count;
                    foreach (Vector3 coefficient in stripTopology.Vertices)
                        AddVertex(Vector3.LerpUnclamped(a, b, coefficient.x) + Vector3.up * (coefficient.y * depth), normal);
                    foreach (int index in stripTopology.Indices) triangles[slot].Add(offset + index);
                }

                void AddVertex(Vector3 point, Vector3 normal)
                {
                    Vector3 local = home == null ? point : PlayerHomeBalconyGeometry.ToHomeLocal(home.PlayerHome, point);
                    vertices.Add(local);
                    normals.Add(home == null ? normal : PlayerHomeBalconyGeometry.ToHomeLocalDirection(home.PlayerHome, normal));
                    Vector2 texturePoint = Mathf.Abs(normal.y) > .5f ? new Vector2(point.x, point.z) :
                        Mathf.Abs(normal.x) > .5f ? new Vector2(point.z, point.y) : new Vector2(point.x, point.y);
                    uvs.Add(texturePoint / tile);
                    if (frame != null)
                    {
                        CityRoadProjection projection = frame.Project(new Vector2(point.x, point.z));
                        road.Add(new Vector4(projection.SignedLateral, projection.DistanceAlong,
                            junction ? 0f : street.CarriagewayWidth * .5f,
                            GroundSurfaceCoordinates.Seed(new Vector3(frame.Vertices[0].x, 0f, frame.Vertices[0].y))));
                    }
                    else road.Add(Vector4.zero);
                    colors.Add(Color.white);
                }
            }
            if (vertices.Count == 0) return null;
            var mesh = new Mesh { name = name + " Mesh", indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uvs);
            mesh.SetUVs(GroundSurfaceCoordinates.Channel, road); mesh.SetColors(colors);
            mesh.subMeshCount = 3;
            for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i, false);
            mesh.RecalculateBounds();
            var result = new GameObject(name); result.transform.SetParent(parent, false);
            result.AddComponent<MeshFilter>().sharedMesh = mesh;
            result.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
            MeshRenderer renderer = result.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { RuntimePrimitiveFactory.DefaultMaterial, RuntimePrimitiveFactory.DefaultMaterial, RuntimePrimitiveFactory.DefaultMaterial };
            CityExteriorAppearance.ApplyRoadSurface(renderer, 0);
            GroundSurfaceCoordinates.Enable(renderer, 0);
            CityExteriorAppearance.ApplySidewalkSurface(renderer, 1);
            CityParkSurfaceAppearance.ApplyCombined(renderer, CityParkSurfaceKind.Path, CityExteriorAppearance.ParkPath, 2);
            CityGroundSurfaceSystem.Register(renderer, new[] {
                new CityGroundSurfaceRegion(FootstepGroundKind.Concrete, 20),
                new CityGroundSurfaceRegion(FootstepGroundKind.Stone, 30),
                new CityGroundSurfaceRegion(FootstepGroundKind.Soil, 20) });
            // The compositor creates the sole physical surface. Avoid cooking
            // temporary colliders which it would immediately replace.
            return result;
        }

        private static bool InHomeRange(Rect bounds, HomeExteriorContextPlan home)
        {
            Vector3 anchor = home.PlayerHome.DoorPosition;
            float dx = Mathf.Max(0f, Mathf.Max(bounds.xMin - anchor.x, anchor.x - bounds.xMax));
            float dz = Mathf.Max(0f, Mathf.Max(bounds.yMin - anchor.z, anchor.z - bounds.yMax));
            return dx * dx + dz * dz <= HomeExteriorContextPlanner.ViewRadius * HomeExteriorContextPlanner.ViewRadius;
        }

        internal static List<Vector3> ClipHome(IReadOnlyList<Vector3> polygon, HomeExteriorContextPlan home)
        {
            var result = new List<Vector3>();
            Vector3 previous = polygon[polygon.Count - 1];
            float previousDistance = PlayerHomeBalconyGeometry.ToHomeLocal(home.PlayerHome, previous).x - HomeExteriorViewBuilder.ContextMinimumX;
            foreach (Vector3 current in polygon)
            {
                float distance = PlayerHomeBalconyGeometry.ToHomeLocal(home.PlayerHome, current).x - HomeExteriorViewBuilder.ContextMinimumX;
                if ((distance >= 0f) != (previousDistance >= 0f))
                    result.Add(Vector3.Lerp(previous, current, previousDistance / (previousDistance - distance)));
                if (distance >= 0f) result.Add(current);
                previous = current; previousDistance = distance;
            }
            return result;
        }

        private static void EnsureTopology()
        {
            if (patchTopology != null) return;
            GameObject model = Resources.Load<GameObject>(ResourcePath);
            if (model == null) throw new InvalidOperationException("Missing Blender street profile: " + ResourcePath);
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.name == "SurfacePatch") patchTopology = new Topology(filter, new Vector3(1f, 0f, 1f));
                else if (filter.name == "BoundaryStrip") stripTopology = new Topology(filter, new Vector3(1f, 1f, 0f));
            }
            if (patchTopology == null || stripTopology == null)
                throw new InvalidOperationException("Street profile needs SurfacePatch and BoundaryStrip meshes.");
        }

        private sealed class Topology
        {
            internal readonly Vector3[] Vertices;
            internal readonly int[] Indices;
            internal Topology(MeshFilter filter, Vector3 expectedSize)
            {
                Vertices = filter.sharedMesh.vertices; Indices = filter.sharedMesh.triangles;
                Bounds bounds = new Bounds(filter.transform.TransformPoint(Vertices[0]), Vector3.zero);
                for (int i = 0; i < Vertices.Length; i++)
                { Vertices[i] = filter.transform.TransformPoint(Vertices[i]); bounds.Encapsulate(Vertices[i]); }
                if ((bounds.size - expectedSize).sqrMagnitude > .000001f)
                    throw new InvalidOperationException("Imported street topology is not in metres: " + filter.name + " " + bounds);
                for (int i = 0; i < Indices.Length; i += 3)
                {
                    Vector3 normal = Vector3.Cross(Vertices[Indices[i + 1]] - Vertices[Indices[i]], Vertices[Indices[i + 2]] - Vertices[Indices[i]]);
                    if ((filter.name == "SurfacePatch" && normal.y <= 0f) || (filter.name == "BoundaryStrip" && normal.z >= 0f))
                        throw new InvalidOperationException("Inverted imported street topology: " + filter.name);
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { patchTopology = null; stripTopology = null; }
    }
}
