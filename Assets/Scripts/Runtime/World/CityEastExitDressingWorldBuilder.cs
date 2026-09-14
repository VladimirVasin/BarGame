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
        public const string EmbeddedGroundMeshSuffix = " Embedded Post Gravel";

        public static bool IsEmbeddedGroundPart(CityEastExitDressingPart part) =>
            part.Assembly == "CanopyApron" || part.GroupId == "Post Foot Traces" ||
            part.Assembly == "FenceToe" || part.GroupId == "Fence Wear";

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
            EmbedPostGround(parent, exit, plan, templates);
            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);
            var support = new SurfaceSupport(parent);
            var groups = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (CityEastExitDressingPart part in plan.Parts)
            {
                if (IsEmbeddedGroundPart(part)) continue;
                if (!groups.TryGetValue(part.GroupId, out Transform group))
                {
                    group = new GameObject(part.GroupId).transform;
                    group.SetParent(root, false); groups.Add(part.GroupId, group);
                }
                Transform placed = CityEastExitWorldBuilder.Place(templates, group, part.Assembly, part.Id,
                    part.Position, part.Rotation, part.Scale);
                FitAuthoredMeshes(placed, exit, part, support);
                if (part.Assembly == "RoadRepair")
                    foreach (Renderer renderer in placed.GetComponentsInChildren<Renderer>(true))
                    {
                        var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                        block.SetColor("_BaseColor", new Color(.23f, .24f, .21f));
                        block.SetColor("_Color", new Color(.23f, .24f, .21f)); renderer.SetPropertyBlock(block);
                    }
                if (part.Assembly == "GroundRidge" || part.Assembly == "EarthBank" || part.Assembly == "DrainCrossing" ||
                    part.Assembly == "DrainInspection")
                {
                    foreach (MeshFilter filter in placed.GetComponentsInChildren<MeshFilter>(true))
                        filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                    FootstepGround.Stamp(placed.gameObject, part.Assembly == "DrainCrossing" || part.Assembly == "DrainInspection"
                        ? FootstepGroundKind.Concrete : FootstepGroundKind.Soil);
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
                // The banks and grass roots belong to the yard's earth.
                // Their former generic soil role carried a different tile
                // and tint, producing separate dark islands in the field.
                if (part.Assembly == "EarthBank" || part.Assembly == "GroundRidge" || part.Assembly == "DryGrass")
                    foreach (Renderer renderer in placed.GetComponentsInChildren<Renderer>(true))
                        if (renderer.name.EndsWith("_Ground", StringComparison.Ordinal))
                        {
                            CityFringeYardSurfaceAppearance.ApplyCombined(renderer,
                                CityFringeYardSurfaceKind.ForefieldGround, CityExteriorAppearance.YardGround);
                            float pitch = CityFringeYardSurfaceAppearance.GetRecipe(CityFringeYardSurfaceKind.ForefieldGround).MetersPerTile;
                            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                            block.SetVector("_BaseMap_ST", new Vector4(1f, 1f, 0f, -exit.YardBounds.yMin / pitch));
                            renderer.SetPropertyBlock(block);
                        }
            }
            return root;
        }

        private static void EmbedPostGround(Transform exitRoot, CityEastExitPlan exit,
            CityEastExitDressingPlan plan, IDictionary<string, Transform> templates)
        {
            var masks = new List<GroundMask>();
            foreach (CityEastExitDressingPart part in plan.Parts)
            {
                if (!IsEmbeddedGroundPart(part)) continue;
                if (!templates.TryGetValue(part.Assembly, out Transform template))
                    throw new InvalidOperationException("Missing post-ground outline " + part.Assembly);
                var faces = new List<Vector2>();
                foreach (MeshFilter filter in template.GetComponentsInChildren<MeshFilter>(true))
                {
                    Vector3[] vertices = filter.sharedMesh.vertices;
                    int[] triangles = filter.sharedMesh.triangles;
                    foreach (int index in triangles)
                    {
                        // Match Place's complete imported basis and unit scale.
                        Vector3 authored = filter.transform.TransformPoint(vertices[index]) - template.position;
                        Vector3 point = part.Position + part.Rotation * Vector3.Scale(part.Scale, authored);
                        faces.Add(new Vector2(point.x, point.z));
                    }
                }
                masks.Add(new GroundMask(part.Footprint, faces));
            }
            HomeSurfaceRecipe recipe = CityFringeYardSurfaceAppearance.GetRecipe(CityFringeYardSurfaceKind.ForefieldGround);
            foreach (MeshFilter filter in exitRoot.parent.GetComponentsInChildren<MeshFilter>(true))
            {
                bool yard = filter.name == CityFringeYardGroundWorldBuilder.GenericGroundObjectName;
                bool road = filter.transform.IsChildOf(exitRoot) && filter.name.StartsWith("EEX_Road_", StringComparison.Ordinal);
                if (!yard && !road) continue;
                Mesh source = filter.sharedMesh;
                Vector3[] positions = source.vertices, sourceNormals = source.normals;
                Vector2[] sourceUvs = source.uv;
                var vertices = new List<Vector3>(positions);
                var normals = new List<Vector3>(sourceNormals);
                var uv = new List<Vector2>(sourceUvs);
                int originalSlots = source.subMeshCount;
                var ordinary = new List<int>[originalSlots];
                var embedded = new List<int>();
                for (int slot = 0; slot < originalSlots; slot++)
                {
                    int[] triangles = source.GetTriangles(slot);
                    ordinary[slot] = new List<int>(triangles.Length);
                    for (int index = 0; index < triangles.Length; index += 3)
                    {
                        int a = triangles[index], b = triangles[index + 1], c = triangles[index + 2];
                        Vector3 worldA = filter.transform.TransformPoint(positions[a]);
                        Vector3 worldB = filter.transform.TransformPoint(positions[b]);
                        Vector3 worldC = filter.transform.TransformPoint(positions[c]);
                        Vector3 center = (worldA + worldB + worldC) / 3f;
                        bool covered = false;
                        // Imported road bevels share smoothed vertex normals
                        // with their top. Only the actual upward face carries
                        // standing ground; a side/skirt must never be painted.
                        if (Vector3.Cross(worldB - worldA, worldC - worldA).normalized.y > .7f)
                        {
                            covered = yard && exit.Swale.IsBed(new Vector2(center.x, center.z));
                            foreach (GroundMask mask in masks)
                                if (mask.Contains(new Vector2(center.x, center.z))) { covered = true; break; }
                        }
                        if (!covered)
                        {
                            ordinary[slot].Add(a); ordinary[slot].Add(b); ordinary[slot].Add(c);
                            continue;
                        }
                        // Reuse whole terrain triangles: the existing half-
                        // metre detail follows the worn outline without any
                        // added T-junction, raised sheet or hidden collision.
                        for (int corner = 0; corner < 3; corner++)
                        {
                            int original = triangles[index + corner];
                            Vector3 world = filter.transform.TransformPoint(positions[original]);
                            embedded.Add(vertices.Count);
                            vertices.Add(positions[original]); normals.Add(sourceNormals[original]);
                            uv.Add(new Vector2(world.x / recipe.MetersPerTile, world.z / recipe.MetersPerTile));
                        }
                    }
                }
                if (embedded.Count == 0) continue;
                Mesh mesh = Object.Instantiate(source);
                mesh.name = source.name + EmbeddedGroundMeshSuffix;
                if (vertices.Count > ushort.MaxValue) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv);
                mesh.subMeshCount = originalSlots + 1;
                for (int slot = 0; slot < originalSlots; slot++) mesh.SetTriangles(ordinary[slot], slot, false);
                mesh.SetTriangles(embedded, originalSlots, false);
                mesh.bounds = source.bounds;
                filter.sharedMesh = mesh;
                filter.gameObject.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);

                Renderer renderer = filter.GetComponent<Renderer>();
                Material[] previous = renderer.sharedMaterials;
                var materials = new Material[originalSlots + 1];
                var blocks = new MaterialPropertyBlock[originalSlots];
                var global = new MaterialPropertyBlock(); renderer.GetPropertyBlock(global);
                for (int slot = 0; slot < originalSlots; slot++)
                {
                    materials[slot] = previous[Mathf.Min(slot, previous.Length - 1)];
                    blocks[slot] = new MaterialPropertyBlock(); renderer.GetPropertyBlock(blocks[slot], slot);
                    if (blocks[slot].isEmpty) blocks[slot] = global;
                }
                materials[originalSlots] = RuntimePrimitiveFactory.DefaultMaterial;
                renderer.sharedMaterials = materials;
                renderer.SetPropertyBlock(null);
                for (int slot = 0; slot < originalSlots; slot++) renderer.SetPropertyBlock(blocks[slot], slot);
                var gravel = new MaterialPropertyBlock();
                gravel.SetTexture("_BaseMap", CityFringeYardSurfaceAppearance.GetTexture(CityFringeYardSurfaceKind.ForefieldGround));
                Color tint = CityFringeYardSurfaceAppearance.CreateDisplayTint(new Color(.285f, .30f, .245f),
                    CityFringeYardSurfaceKind.ForefieldGround);
                gravel.SetColor("_BaseColor", tint); gravel.SetColor("_Color", tint);
                gravel.SetVector("_BaseMap_ST", new Vector4(1f, 1f, 0f, -exit.YardBounds.yMin / recipe.MetersPerTile));
                gravel.SetFloat("_Smoothness", recipe.Smoothness); gravel.SetFloat("_Metallic", recipe.Metallic);
                renderer.SetPropertyBlock(gravel, originalSlots);
            }
        }

        private sealed class GroundMask
        {
            private readonly Rect bounds;
            private readonly IReadOnlyList<Vector2> faces;
            internal GroundMask(Rect bounds, IReadOnlyList<Vector2> faces) { this.bounds = bounds; this.faces = faces; }
            internal bool Contains(Vector2 point)
            {
                if (!bounds.Contains(point)) return false;
                for (int index = 0; index < faces.Count; index += 3)
                {
                    Vector2 a = faces[index], b = faces[index + 1], c = faces[index + 2];
                    float area = Cross(b - a, c - a);
                    if (Mathf.Abs(area) < .000001f) continue;
                    if (Cross(b - a, point - a) / area >= -.00001f &&
                        Cross(c - b, point - b) / area >= -.00001f &&
                        Cross(a - c, point - c) / area >= -.00001f) return true;
                }
                return false;
            }
            private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        }

        private static void FitAuthoredMeshes(Transform placement, CityEastExitPlan exit, CityEastExitDressingPart part,
            SurfaceSupport support)
        {
            bool shrub = part.Assembly == "Shrub" || part.Assembly == "LowShrub" || part.Assembly == "BranchShrub";
            float rootLift = shrub && support.TrySample(new Vector2(part.Position.x, part.Position.z), out float rootGround)
                ? rootGround - part.Position.y : 0f;
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
                        world.y += rootLift + (ground - part.Position.y - rootLift) * weight;
                    }
                    worldVertices[i] = world;
                }
                bool sheet = part.Assembly == "GravelPatch" || part.Assembly == "CanopyApron" ||
                    part.Assembly == "FenceToe" || part.Assembly == "RoadRepair" ||
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
                else if (part.Assembly == "EarthBank" || part.Assembly == "GroundRidge")
                    support.Add(worldVertices, mesh.triangles);
                for (int i = 0; i < vertices.Length; i++)
                    vertices[i] = filter.transform.InverseTransformPoint(worldVertices[i]);
                mesh.vertices = vertices;
                if ((part.Assembly == "EarthBank" || part.Assembly == "GroundRidge" || part.Assembly == "DryGrass") &&
                    filter.name.EndsWith("_Ground", StringComparison.Ordinal))
                {
                    float pitch = CityFringeYardSurfaceAppearance.GetRecipe(CityFringeYardSurfaceKind.ForefieldGround).MetersPerTile;
                    var uv = new Vector2[worldVertices.Length];
                    for (int i = 0; i < uv.Length; i++)
                        uv[i] = new Vector2(worldVertices[i].x / pitch, worldVertices[i].z / pitch);
                    mesh.uv = uv;
                }
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
