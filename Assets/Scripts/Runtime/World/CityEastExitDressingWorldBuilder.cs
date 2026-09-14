using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>Places imported topology and fits it to the existing ground; creates no visible primitives.</summary>
    public static class CityEastExitDressingWorldBuilder
    {
        public const string ResourcePath = "City/EastExit/CityEastExitDressing3D";
        public const string RootName = "Eastern Checkpoint Surroundings";

        internal static void AddTemplates(IDictionary<string, Transform> templates)
        {
            GameObject asset = Resources.Load<GameObject>(ResourcePath);
            if (asset == null) throw new InvalidOperationException("Missing authored checkpoint surroundings kit: " + ResourcePath);
            foreach (Transform item in asset.GetComponentsInChildren<Transform>(true))
                if (!templates.ContainsKey(item.name)) templates.Add(item.name, item);
        }

        internal static Transform Build(Transform parent, CityEastExitPlan exit, CityEastExitDressingPlan plan,
            IDictionary<string, Transform> templates)
        {
            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);
            var support = new SurfaceSupport(parent);
            var groups = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (CityEastExitDressingPart part in plan.Parts)
            {
                if (!groups.TryGetValue(part.GroupId, out Transform group))
                {
                    group = new GameObject(part.GroupId).transform;
                    group.SetParent(root, false); groups.Add(part.GroupId, group);
                }
                Transform placed = CityEastExitWorldBuilder.Place(templates, group, part.Assembly, part.Id,
                    part.Position, part.Rotation, part.Scale);
                FitAuthoredMeshes(placed, exit, part, support);
                if (part.GroupId == "Post Foot Traces")
                    foreach (Renderer renderer in placed.GetComponentsInChildren<Renderer>(true))
                        CityFringeYardSurfaceAppearance.ApplyCombined(renderer, CityFringeYardSurfaceKind.ForefieldGround,
                            new Color(.285f, .30f, .245f));
                if (part.Assembly == "RoadRepair")
                    foreach (Renderer renderer in placed.GetComponentsInChildren<Renderer>(true))
                    {
                        var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                        block.SetColor("_BaseColor", new Color(.23f, .24f, .21f));
                        block.SetColor("_Color", new Color(.23f, .24f, .21f)); renderer.SetPropertyBlock(block);
                    }
                if (part.Assembly == "GroundRidge")
                {
                    foreach (MeshFilter filter in placed.GetComponentsInChildren<MeshFilter>(true))
                        filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                    FootstepGround.Stamp(placed.gameObject, FootstepGroundKind.Soil);
                }
                else
                    foreach (CityEastExitDressingSolid solid in plan.Solids)
                        if (solid.PartId == part.Id)
                        {
                            BoxCollider collider = placed.gameObject.AddComponent<BoxCollider>();
                            collider.center = placed.InverseTransformPoint(solid.Position);
                            collider.size = new Vector3(solid.Size.x / part.Scale.x, solid.Size.y / part.Scale.y, solid.Size.z / part.Scale.z);
                        }
                if (part.Assembly == "Shelter")
                    CityEastExitWorldBuilder.AddBox(placed, new Vector3(0, 2.57f, 0), new Vector3(3.6f, .16f, 3.2f));
            }
            return root;
        }

        private static void FitAuthoredMeshes(Transform placement, CityEastExitPlan exit, CityEastExitDressingPart part,
            SurfaceSupport support)
        {
            foreach (MeshFilter filter in placement.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh source = filter.sharedMesh;
                if (source == null || !source.isReadable)
                    throw new InvalidOperationException("Checkpoint ground fitting requires readable authored mesh: " + filter.name);
                Mesh mesh = Object.Instantiate(source);
                mesh.name = part.Id + " Ground Fitted " + source.name;
                Vector3[] vertices = mesh.vertices;
                var worldVertices = new Vector3[vertices.Length];
                bool post = part.Assembly == "Shelter" && filter.name.IndexOf("_Post", StringComparison.Ordinal) >= 0;
                bool foot = part.Assembly == "Shelter" && filter.name.IndexOf("_Foot", StringComparison.Ordinal) >= 0;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 world = filter.transform.TransformPoint(vertices[i]);
                    var point = new Vector2(world.x, world.z);
                    float authoredHeight = world.y - part.Position.y;
                    // The visible ground is triangulated, while the plan is a
                    // curved height field. Millimetre-thin sheets must fit the
                    // rendered triangles, including the lower road shoulders.
                    if (!support.TrySample(point, out float ground))
                        ground = exit.RoadBounds.Contains(point) ? exit.SampleRoadTop(point.x, point.y) : exit.SampleGroundTop(point);
                    if (part.Fit == CityEastExitDressingFit.Ground || part.Fit == CityEastExitDressingFit.Road)
                        world.y = ground + authoredHeight;
                    else
                    {
                        float weight = foot ? 1f : Mathf.Clamp01(1f - authoredHeight / (post ? 2.48f : .30f));
                        world.y += (ground - part.Position.y) * weight;
                    }
                    worldVertices[i] = world;
                }
                bool sheet = part.Assembly == "GravelPatch" || part.Assembly == "RoadRepair" ||
                    part.Assembly == "DryDrain" && !filter.name.EndsWith("_Gravel", StringComparison.Ordinal);
                if (sheet)
                {
                    // Different triangulations can cross between vertices.
                    // Measure all overlap polygon corners: their affine height
                    // difference reaches its maximum at one of those corners.
                    // A local tile moves only by the clearance it actually needs;
                    // this also separates the worn traces where branches meet.
                    float lift = support.RequiredLift(worldVertices, mesh.triangles, .002f);
                    if (lift > 0f)
                        for (int i = 0; i < worldVertices.Length; i++) worldVertices[i].y += lift;
                    support.Add(worldVertices, mesh.triangles);
                }
                for (int i = 0; i < vertices.Length; i++)
                    vertices[i] = filter.transform.InverseTransformPoint(worldVertices[i]);
                mesh.vertices = vertices;
                mesh.RecalculateBounds(); mesh.RecalculateNormals();
                filter.sharedMesh = mesh;
                filter.gameObject.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
            }
        }

        /// <summary>Build-local XZ index of the actual supporting mesh faces.</summary>
        private sealed class SurfaceSupport
        {
            private const float CellSize = 4f;
            private readonly List<Face> faces = new List<Face>();
            private readonly Dictionary<Vector2Int, List<int>> cells = new Dictionary<Vector2Int, List<int>>();

            internal SurfaceSupport(Transform exitRoot)
            {
                bool foundGround = false;
                foreach (MeshFilter filter in exitRoot.parent.GetComponentsInChildren<MeshFilter>(true))
                {
                    bool ground = filter.name == CityFringeYardGroundWorldBuilder.GenericGroundObjectName;
                    bool road = filter.transform.IsChildOf(exitRoot) && filter.name.StartsWith("EEX_Road_", StringComparison.Ordinal);
                    if (!ground && !road) continue;
                    foundGround |= ground;
                    Mesh mesh = filter.sharedMesh;
                    Vector3[] vertices = mesh.vertices;
                    for (int i = 0; i < vertices.Length; i++) vertices[i] = filter.transform.TransformPoint(vertices[i]);
                    Add(vertices, mesh.triangles);
                }
                if (!foundGround) throw new InvalidOperationException("Checkpoint dressing requires the built Yard Ground mesh.");
            }

            internal void Add(Vector3[] vertices, int[] triangles)
            {
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    var face = new Face(vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]]);
                    if (Mathf.Abs(face.Area) < .000001f) continue;
                    int index = faces.Count; faces.Add(face);
                    Vector2Int minimum = Cell(face.Bounds.min), maximum = Cell(face.Bounds.max);
                    for (int x = minimum.x; x <= maximum.x; x++)
                    for (int z = minimum.y; z <= maximum.y; z++)
                    {
                        var cell = new Vector2Int(x, z);
                        if (!cells.TryGetValue(cell, out List<int> items)) cells.Add(cell, items = new List<int>());
                        items.Add(index);
                    }
                }
            }

            internal bool TrySample(Vector2 point, out float height)
            {
                height = float.NegativeInfinity;
                if (cells.TryGetValue(Cell(point), out List<int> items))
                    foreach (int index in items)
                        if (faces[index].Contains(point)) height = Mathf.Max(height, faces[index].Height(point));
                return !float.IsNegativeInfinity(height);
            }

            internal float RequiredLift(Vector3[] vertices, int[] triangles, float clearance)
            {
                float lift = 0f;
                var visited = new HashSet<int>();
                var polygon = new List<Vector2>(6);
                var clipped = new List<Vector2>(6);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    var face = new Face(vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]]);
                    if (Mathf.Abs(face.Area) < .000001f) continue;
                    visited.Clear();
                    Vector2Int minimum = Cell(face.Bounds.min), maximum = Cell(face.Bounds.max);
                    for (int x = minimum.x; x <= maximum.x; x++)
                    for (int z = minimum.y; z <= maximum.y; z++)
                    {
                        if (!cells.TryGetValue(new Vector2Int(x, z), out List<int> items)) continue;
                        foreach (int index in items)
                        {
                            if (!visited.Add(index)) continue;
                            Face other = faces[index];
                            if (!face.Bounds.Overlaps(other.Bounds)) continue;
                            polygon.Clear(); polygon.Add(face.A); polygon.Add(face.B); polygon.Add(face.C);
                            Clip(other.A, other.B, other.Area);
                            Clip(other.B, other.C, other.Area);
                            Clip(other.C, other.A, other.Area);
                            foreach (Vector2 point in polygon)
                                lift = Mathf.Max(lift, other.Height(point) + clearance - face.Height(point));
                        }
                    }
                }
                return lift;

                void Clip(Vector2 a, Vector2 b, float orientation)
                {
                    if (polygon.Count == 0) return;
                    clipped.Clear();
                    Vector2 previous = polygon[polygon.Count - 1];
                    float previousSide = Cross(b - a, previous - a) * Mathf.Sign(orientation);
                    foreach (Vector2 current in polygon)
                    {
                        float side = Cross(b - a, current - a) * Mathf.Sign(orientation);
                        if ((side >= 0f) != (previousSide >= 0f))
                            clipped.Add(Vector2.Lerp(previous, current, previousSide / (previousSide - side)));
                        if (side >= 0f) clipped.Add(current);
                        previous = current; previousSide = side;
                    }
                    List<Vector2> swap = polygon; polygon = clipped; clipped = swap;
                }
            }

            private static Vector2Int Cell(Vector2 point) =>
                new Vector2Int(Mathf.FloorToInt(point.x / CellSize), Mathf.FloorToInt(point.y / CellSize));
            private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

            private readonly struct Face
            {
                internal Face(Vector3 a, Vector3 b, Vector3 c)
                {
                    A = new Vector2(a.x, a.z); B = new Vector2(b.x, b.z); C = new Vector2(c.x, c.z);
                    heights = new Vector3(a.y, b.y, c.y);
                    Area = Cross(B - A, C - A);
                    Bounds = Rect.MinMaxRect(Mathf.Min(a.x, b.x, c.x), Mathf.Min(a.z, b.z, c.z),
                        Mathf.Max(a.x, b.x, c.x), Mathf.Max(a.z, b.z, c.z));
                }
                internal Vector2 A { get; }
                internal Vector2 B { get; }
                internal Vector2 C { get; }
                internal float Area { get; }
                internal Rect Bounds { get; }
                private readonly Vector3 heights;
                internal bool Contains(Vector2 p) => Cross(B - A, p - A) / Area >= -.00001f &&
                    Cross(C - B, p - B) / Area >= -.00001f && Cross(A - C, p - C) / Area >= -.00001f;
                internal float Height(Vector2 p)
                {
                    float b = Cross(p - A, C - A) / Area, c = Cross(B - A, p - A) / Area;
                    return heights.x + b * (heights.y - heights.x) + c * (heights.z - heights.x);
                }
            }
        }
    }
}
