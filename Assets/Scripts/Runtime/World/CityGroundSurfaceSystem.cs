using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Triangle = BarPromenade.GroundSurfacePartitioner.Triangle;
using Vertex = BarPromenade.GroundSurfacePartitioner.Vertex;

namespace BarPromenade
{
    /// <summary>One owner for the City's ground coatings. Builders supply their
    /// measured surfaces; priority partitioning removes buried/overlapping skins,
    /// and render/collision sectors preserve their exact planes and UVs.</summary>
    [DisallowMultipleComponent]
    internal sealed class CityGroundSurfaceSystem : MonoBehaviour
    {
        internal const float SectorSize = 48f;
        private const float SurfaceBucketSize = 2f;
        private const float SupportBucketSize = 1f;
        private const float EdgeBucketSize = .5f;
        private readonly List<Source> sources = new List<Source>();
        private readonly List<Style> styles = new List<Style>();
        private readonly List<Collider> replacedColliders = new List<Collider>();
        private readonly List<DynamicRegion> dynamicRegions = new List<DynamicRegion>();
        private readonly Dictionary<Vector2Int, List<int>> supports = new Dictionary<Vector2Int, List<int>>();
        private readonly List<Triangle> supportFaces = new List<Triangle>();
        private readonly List<Rect> supportBounds = new List<Rect>();
        private readonly List<SupportPlane> supportPlanes = new List<SupportPlane>();
        private readonly List<GroundSurfacePartitioner.Footprint> supportContours = new List<GroundSurfacePartitioner.Footprint>();
        private readonly Dictionary<(int, FootstepGroundKind), int> projectedStyles = new Dictionary<(int, FootstepGroundKind), int>();
        private bool buildCollision;
        private bool sealedSurface;
        internal int SourceCount => sources.Count;
        internal int SurfaceTriangleCount { get; private set; }
        internal int SectorCount { get; private set; }
        internal double BuildMilliseconds { get; private set; }

        internal static CityGroundSurfaceSystem Begin(Transform parent, bool collision = true)
        {
            var system = parent.gameObject.AddComponent<CityGroundSurfaceSystem>();
            system.buildCollision = collision;
            return system;
        }

        internal static void Register(GameObject source, int priority = 10, bool paint = false, bool dynamic = false)
        {
            if (source == null) return;
            foreach (Renderer renderer in source.GetComponents<Renderer>()) Register(renderer, priority, paint, dynamic);
        }

        internal static void Register(Renderer renderer, int priority = 10, bool paint = false, bool dynamic = false)
        {
            if (renderer == null) return;
            CityGroundSurfaceSystem system = renderer.GetComponentInParent<CityGroundSurfaceSystem>(true);
            if (system == null) return;
            for (int i = 0; i < system.sources.Count; i++)
                if (system.sources[i].Renderer == renderer)
                { system.sources[i].Priority = priority; system.sources[i].Paint = paint; return; }
            var source = new Source(renderer, priority, paint, dynamic);
            system.Capture(source);
            if (system.sealedSurface)
            {
                if (!dynamic) throw new InvalidOperationException("Static ground registered after City surface completion: " + renderer.name);
                system.RebuildDynamic(source);
            }
            else system.sources.Add(source);
        }

        internal static void ReplaceCollider(Collider collider)
        {
            if (collider == null) return;
            CityGroundSurfaceSystem system = collider.GetComponentInParent<CityGroundSurfaceSystem>(true);
            if (system != null && !system.replacedColliders.Contains(collider)) system.replacedColliders.Add(collider);
        }

        internal void FinalizeSurface()
        {
            if (sealedSurface) return;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            foreach (Source source in sources) Capture(source);
            double captureMilliseconds = timer.Elapsed.TotalMilliseconds;
            sources.Sort((a, b) => a.Priority != b.Priority ? b.Priority.CompareTo(a.Priority) : a.Order.CompareTo(b.Order));
            supports.Clear();
            supportFaces.Clear(); supportBounds.Clear(); supportPlanes.Clear(); supportContours.Clear();
            foreach (Source source in sources)
                if (!source.Paint) foreach (Triangle triangle in source.Support) IndexSupport(triangle);
            // Painting is projected onto the actual support triangles, including
            // their grade breaks. Merely moving a paint quad's corners would
            // introduce a second plane across a road bend.
            foreach (Source source in sources)
                if (source.Paint) source.Top = ProjectPaint(source.Top);
            double paintMilliseconds = timer.Elapsed.TotalMilliseconds;

            foreach (Source source in sources)
                if (source.Dynamic && source.Renderer.GetComponent<CitySandTreading>() == null)
                    dynamicRegions.Add(new DynamicRegion(source));

            var input = new List<Triangle>();
            var sides = new List<Triangle>();
            foreach (Source source in sources)
            {
                if (source.Dynamic) continue;
                foreach (Triangle triangle in source.Top)
                {
                    Rect bounds = Bounds(triangle);
                    bool meetsDynamic = false;
                    foreach (DynamicRegion region in dynamicRegions)
                        if (bounds.Overlaps(region.Bounds)) { meetsDynamic = true; break; }
                    if (!meetsDynamic) { input.Add(triangle); continue; }
                    var pieces = new List<Triangle> { triangle };
                    foreach (DynamicRegion region in dynamicRegions)
                    {
                        foreach (Triangle piece in pieces) region.Coatings.AddRange(GroundSurfacePartitioner.ClipToRect(piece, region.Bounds));
                        var outside = new List<Triangle>();
                        foreach (Triangle piece in pieces) outside.AddRange(GroundSurfacePartitioner.SubtractRect(piece, region.Bounds));
                        pieces = outside;
                    }
                    input.AddRange(pieces);
                }
                if (!source.Paint) sides.AddRange(source.Sides);
            }
            double routingMilliseconds = timer.Elapsed.TotalMilliseconds;
            List<Triangle> surface = GroundSurfacePartitioner.Partition(input, out GroundSurfacePartitioner.Statistics statistics, SurfaceBucketSize);
            double partitionMilliseconds = timer.Elapsed.TotalMilliseconds;
            surface = GroundSurfacePartitioner.Conform(surface, bucketSize: EdgeBucketSize);
            double conformMilliseconds = timer.Elapsed.TotalMilliseconds;
            SurfaceTriangleCount = surface.Count;
            surface.AddRange(sides);
            Transform sectors = new GameObject("City Ground").transform;
            sectors.SetParent(transform, false);
            SectorCount = BuildSectors(sectors, surface, buildCollision);
            foreach (DynamicRegion region in dynamicRegions) BuildDynamic(region);
            foreach (Source source in sources)
            {
                if (source.Dynamic) continue;
                source.Renderer.enabled = false;
                CityWetSurfaceRegistry.Unregister(source.Renderer);
                foreach (Collider collider in source.Renderer.GetComponents<Collider>()) collider.enabled = false;
            }
            foreach (Collider collider in replacedColliders) if (collider != null) collider.enabled = false;
            supports.Clear();
            supportFaces.Clear(); supportBounds.Clear(); supportPlanes.Clear(); supportContours.Clear();
            foreach (Source source in sources)
            {
                source.Support = null;
                if (!source.Dynamic || source.Renderer.GetComponent<CitySandTreading>() != null)
                { source.Top = null; source.Sides = null; }
            }
            sealedSurface = true;
            BuildMilliseconds = timer.Elapsed.TotalMilliseconds;
            GameLog.Debug("city", "ground_surface_complete", GameLog.Field("sources", sources.Count),
                GameLog.Field("sectors", SectorCount), GameLog.Field("triangles", SurfaceTriangleCount),
                GameLog.Field("candidates", statistics.CandidatePairs), GameLog.Field("bucket_entries", statistics.BucketEntriesVisited),
                GameLog.Field("duration_ms", BuildMilliseconds),
                GameLog.Field("capture_ms", captureMilliseconds), GameLog.Field("paint_ms", paintMilliseconds - captureMilliseconds),
                GameLog.Field("routing_ms", routingMilliseconds - paintMilliseconds),
                GameLog.Field("partition_ms", partitionMilliseconds - routingMilliseconds),
                GameLog.Field("conform_ms", conformMilliseconds - partitionMilliseconds),
                GameLog.Field("publish_ms", BuildMilliseconds - conformMilliseconds));
        }

        private void Capture(Source source)
        {
            MeshFilter filter = source.Renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) throw new InvalidOperationException("Ground needs a MeshFilter: " + source.Renderer.name);
            Mesh mesh = filter.sharedMesh;
            if (!mesh.isReadable)
            {
                if (source.Top != null) return;
                throw new InvalidOperationException("Ground source uploaded before registration: " + source.Renderer.name);
            }
            Vector3[] positions = mesh.vertices, normals = mesh.normals;
            Vector2[] uv = mesh.uv;
            Color[] colors = mesh.colors;
            var road = new List<Vector4>(); mesh.GetUVs(GroundSurfaceCoordinates.Channel, road);
            Matrix4x4 matrix = transform.worldToLocalMatrix * source.Renderer.transform.localToWorldMatrix;
            Matrix4x4 normalMatrix = matrix.inverse.transpose;
            bool identity = matrix.Equals(Matrix4x4.identity);
            var vertices = new Vertex[positions.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 normal = normals.Length == positions.Length ? normals[i] : Vector3.up;
                vertices[i] = new Vertex(identity ? positions[i] : matrix.MultiplyPoint3x4(positions[i]),
                    (identity ? normal : normalMatrix.MultiplyVector(normal)).normalized,
                    uv.Length == positions.Length ? uv[i] : Vector2.zero,
                    road.Count == positions.Length ? road[i] : Vector4.zero,
                    colors.Length == positions.Length ? colors[i] : Color.white);
            }
            int triangleCapacity = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) triangleCapacity += (int)mesh.GetIndexCount(i) / 3;
            if (source.Top == null) source.Top = new List<Triangle>(triangleCapacity); else source.Top.Clear();
            if (source.Sides == null) source.Sides = new List<Triangle>(); else source.Sides.Clear();
            Material[] materials = source.Renderer.sharedMaterials;
            FootstepGround marker = source.Renderer.GetComponentInParent<FootstepGround>(true);
            FootstepGroundKind kind = marker != null ? marker.Kind : source.Paint ? FootstepGroundKind.None : FootstepGroundKind.Soil;
            for (int slot = 0; slot < mesh.subMeshCount; slot++)
            {
                var block = new MaterialPropertyBlock();
                source.Renderer.GetPropertyBlock(block, slot);
                if (block.isEmpty) source.Renderer.GetPropertyBlock(block);
                int style = FindStyle(new Style(source.Renderer, slot, materials[Mathf.Min(slot, materials.Length - 1)], block, kind));
                int[] indices = mesh.GetTriangles(slot);
                for (int index = 0; index < indices.Length; index += 3)
                {
                    Triangle triangle = new Triangle(vertices[indices[index]], vertices[indices[index + 1]], vertices[indices[index + 2]], style);
                    Vector3 cross = Vector3.Cross(triangle.B.Position - triangle.A.Position, triangle.C.Position - triangle.A.Position);
                    if (cross.y > .0000001f) source.Top.Add(triangle);
                    else if (Mathf.Abs(cross.y) < cross.magnitude * .05f && !source.Paint) source.Sides.Add(triangle);
                }
            }
            // Combined imported paving can contain nested terraces. Highest
            // faces own their footprint before lower faces of the same source.
            SortTopFaces();
            source.Support = source.Paint ? null : source.Top;
            MeshCollider collision = source.Renderer.GetComponent<MeshCollider>();
            if (!source.Paint && collision != null && collision.sharedMesh != null && collision.sharedMesh != mesh && collision.sharedMesh.isReadable)
            {
                source.Support = new List<Triangle>(source.Top);
                // Church grass is cut away visually beneath paving; its full
                // collision mesh still supplies the paving's exact substrate.
                Mesh substrate = collision.sharedMesh;
                Vector3[] supportVertices = substrate.vertices;
                int[] supportIndices = substrate.triangles;
                for (int i = 0; i < supportIndices.Length; i += 3)
                {
                    Vector3 a = supportVertices[supportIndices[i]];
                    Vector3 b = supportVertices[supportIndices[i + 1]];
                    Vector3 c = supportVertices[supportIndices[i + 2]];
                    if (!identity)
                    {
                        a = matrix.MultiplyPoint3x4(a); b = matrix.MultiplyPoint3x4(b); c = matrix.MultiplyPoint3x4(c);
                    }
                    Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                    if (normal.y <= .01f) continue;
                    source.Support.Add(new Triangle(new Vertex(a, normal, Vector2.zero, Vector4.zero, Color.white),
                        new Vertex(b, normal, Vector2.zero, Vector4.zero, Color.white),
                        new Vertex(c, normal, Vector2.zero, Vector4.zero, Color.white), source.Top.Count > 0 ? source.Top[0].Surface : 0));
                }
            }

            void SortTopFaces()
            {
                List<Triangle> top = source.Top;
                if (top.Count < 2) return;
                float previous = HeightKey(top[0]);
                bool sorted = true;
                for (int i = 1; i < top.Count; i++)
                {
                    float current = HeightKey(top[i]);
                    if (current.CompareTo(previous) > 0) { sorted = false; break; }
                    previous = current;
                }
                if (sorted) return;

                // Move compact indices during sorting, rather than repeatedly
                // copying three complete vertex records for every comparison.
                var heights = new float[top.Count];
                var order = new int[top.Count];
                for (int i = 0; i < top.Count; i++) { heights[i] = HeightKey(top[i]); order[i] = i; }
                Array.Sort(order, (first, last) => heights[last].CompareTo(heights[first]));
                for (int first = 0; first < order.Length; first++)
                {
                    if (order[first] == first) continue;
                    Triangle saved = top[first];
                    int destination = first;
                    while (order[destination] != first)
                    {
                        int next = order[destination];
                        top[destination] = top[next];
                        order[destination] = destination;
                        destination = next;
                    }
                    top[destination] = saved;
                    order[destination] = destination;
                }
            }

            float HeightKey(Triangle triangle) => triangle.A.Position.y + triangle.B.Position.y + triangle.C.Position.y;
        }

        private void IndexSupport(Triangle triangle)
        {
            Rect bounds = Bounds(triangle);
            int index = supportFaces.Count;
            supportFaces.Add(triangle); supportBounds.Add(bounds);
            supportPlanes.Add(new SupportPlane(triangle));
            supportContours.Add(null);
            for (int x = Mathf.FloorToInt(bounds.xMin / SupportBucketSize); x <= Mathf.FloorToInt(bounds.xMax / SupportBucketSize); x++)
            for (int z = Mathf.FloorToInt(bounds.yMin / SupportBucketSize); z <= Mathf.FloorToInt(bounds.yMax / SupportBucketSize); z++)
            {
                var key = new Vector2Int(x, z);
                if (!supports.TryGetValue(key, out List<int> bucket)) supports.Add(key, bucket = new List<int>());
                bucket.Add(index);
            }
        }

        private List<Triangle> ProjectPaint(List<Triangle> paint)
        {
            var result = new List<Triangle>();
            var candidates = new HashSet<int>();
            var projected = new List<Triangle>();
            foreach (Triangle triangle in paint)
            {
                Rect bounds = Bounds(triangle);
                candidates.Clear(); projected.Clear();
                for (int x = Mathf.FloorToInt(bounds.xMin / SupportBucketSize); x <= Mathf.FloorToInt(bounds.xMax / SupportBucketSize); x++)
                for (int z = Mathf.FloorToInt(bounds.yMin / SupportBucketSize); z <= Mathf.FloorToInt(bounds.yMax / SupportBucketSize); z++)
                {
                    if (!supports.TryGetValue(new Vector2Int(x, z), out List<int> bucket)) continue;
                    foreach (int supportIndex in bucket)
                    {
                        if (!bounds.Overlaps(supportBounds[supportIndex]) || !candidates.Add(supportIndex)) continue;
                        Triangle support = supportFaces[supportIndex];
                        SupportPlane plane = supportPlanes[supportIndex];
                        GroundSurfacePartitioner.Footprint contour = supportContours[supportIndex];
                        if (contour == null) supportContours[supportIndex] = contour = GroundSurfacePartitioner.Prepare(support);
                        foreach (Triangle part in GroundSurfacePartitioner.Intersect(triangle, contour))
                        {
                            Vector3 centre = (part.A.Position + part.B.Position + part.C.Position) / 3f;
                            float y = plane.Height(centre);
                            if (Mathf.Abs(y - centre.y) > .45f) continue;
                            FootstepGroundKind paintKind = styles[part.Surface].Kind;
                            int style = ProjectedStyle(part.Surface, paintKind == FootstepGroundKind.None ? styles[support.Surface].Kind : paintKind);
                            projected.Add(new Triangle(plane.Project(part.A), plane.Project(part.B), plane.Project(part.C), style));
                        }
                    }
                }
                // Highest support wins over buried ground, even if both had a
                // footprint before partitioning. Stable source ordering breaks ties.
                projected.Sort(CompareHeight);
                result.AddRange(GroundSurfacePartitioner.Partition(projected, SurfaceBucketSize));
            }
            return result;
        }

        private static int CompareHeight(Triangle a, Triangle b) =>
            (b.A.Position.y + b.B.Position.y + b.C.Position.y).CompareTo(a.A.Position.y + a.B.Position.y + a.C.Position.y);

        private int ProjectedStyle(int paint, FootstepGroundKind kind)
        {
            var key = (paint, kind);
            if (!projectedStyles.TryGetValue(key, out int index))
            {
                index = FindStyle(new Style(styles[paint], kind));
                projectedStyles.Add(key, index);
            }
            return index;
        }

        private int FindStyle(Style candidate)
        {
            for (int i = 0; i < styles.Count; i++) if (styles[i].Equivalent(candidate)) return i;
            styles.Add(candidate);
            return styles.Count - 1;
        }

        private readonly struct SupportPlane
        {
            internal SupportPlane(Triangle triangle)
            { Origin = triangle.A.Position; RawNormal = Vector3.Cross(triangle.B.Position - Origin, triangle.C.Position - Origin); Normal = RawNormal.normalized; }
            private readonly Vector3 Origin, RawNormal, Normal;
            internal float Height(Vector3 position) => Origin.y - (RawNormal.x * (position.x - Origin.x) +
                RawNormal.z * (position.z - Origin.z)) / RawNormal.y;
            internal Vertex Project(Vertex vertex)
            {
                Vector3 position = vertex.Position; position.y = Height(position);
                return new Vertex(position, Normal, vertex.Uv0, vertex.RoadCoordinates, vertex.Color);
            }
        }

        private int BuildSectors(Transform parent, IReadOnlyList<Triangle> triangles, bool collision)
        {
            var groups = new Dictionary<Vector2Int, List<Triangle>>();
            foreach (Triangle triangle in triangles)
            {
                Vector3 centre = (triangle.A.Position + triangle.B.Position + triangle.C.Position) / 3f;
                var key = new Vector2Int(Mathf.FloorToInt(centre.x / SectorSize), Mathf.FloorToInt(centre.z / SectorSize));
                if (!groups.TryGetValue(key, out List<Triangle> group)) groups.Add(key, group = new List<Triangle>());
                group.Add(triangle);
            }
            foreach (KeyValuePair<Vector2Int, List<Triangle>> entry in groups)
                BuildMesh("Ground sector " + entry.Key, parent, entry.Value, collision);
            return groups.Count;
        }

        private GameObject BuildMesh(string name, Transform parent, IReadOnlyList<Triangle> triangles, bool collision)
        {
            var positions = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>();
            var road = new List<Vector4>(); var colors = new List<Color>();
            var slots = new List<int>(); var indices = new List<List<int>>();
            foreach (Triangle triangle in triangles)
            {
                int slot = slots.IndexOf(triangle.Surface);
                if (slot < 0) { slot = slots.Count; slots.Add(triangle.Surface); indices.Add(new List<int>()); }
                int first = positions.Count;
                Append(triangle.A); Append(triangle.B); Append(triangle.C);
                indices[slot].Add(first); indices[slot].Add(first + 1); indices[slot].Add(first + 2);
            }
            var mesh = new Mesh { name = name, indexFormat = positions.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(positions); mesh.SetNormals(normals); mesh.SetUVs(0, uv);
            mesh.SetUVs(GroundSurfaceCoordinates.Channel, road); mesh.SetColors(colors);
            mesh.subMeshCount = slots.Count;
            var kinds = new List<FootstepGroundKind>();
            var materials = new Material[slots.Count];
            for (int i = 0; i < slots.Count; i++)
            {
                mesh.SetTriangles(indices[i], i, false); materials[i] = styles[slots[i]].Material;
                for (int j = 0; j < indices[i].Count; j += 3) kinds.Add(styles[slots[i]].Kind);
            }
            mesh.RecalculateBounds();
            var host = new GameObject(name); host.transform.SetParent(parent, false);
            // Source coordinates are local to this system, not to a dynamic parent.
            host.transform.SetPositionAndRotation(transform.position, transform.rotation);
            host.transform.localScale = Vector3.one;
            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            host.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
            MeshRenderer renderer = host.AddComponent<MeshRenderer>(); renderer.sharedMaterials = materials;
            for (int i = 0; i < slots.Count; i++)
            {
                Style style = styles[slots[i]];
                renderer.SetPropertyBlock(style.Properties, i);
                if (style.HasWeather) CityWetSurfaceRegistry.Register(renderer, style.WetKind, style.DryTint, i);
            }
            if (collision)
            {
                host.AddComponent<MeshCollider>().sharedMesh = mesh;
                FootstepGround.Stamp(host, FootstepGroundKind.Soil, kinds);
            }
            return host;

            void Append(Vertex vertex)
            { positions.Add(vertex.Position); normals.Add(vertex.Normal); uv.Add(vertex.Uv0); road.Add(vertex.RoadCoordinates); colors.Add(vertex.Color); }
        }

        private void BuildDynamic(DynamicRegion region)
        {
            var input = new List<Triangle>(region.Coatings);
            // A grave cuts through every coating, including a path that happens
            // to cross its mouth. Only this sector is rebuilt after a dig.
            CityCemeteryGroundExcavation excavation = region.Source.Renderer.GetComponentInParent<CityCemeteryGroundExcavation>(true);
            if (excavation != null)
                foreach (Rect cut in excavation.Cuts)
                {
                    var remaining = new List<Triangle>();
                    foreach (Triangle triangle in input) remaining.AddRange(GroundSurfacePartitioner.SubtractRect(triangle, cut));
                    input = remaining;
                }
            input.AddRange(region.Source.Top);
            List<Triangle> surface = GroundSurfacePartitioner.Conform(
                GroundSurfacePartitioner.Partition(input, SurfaceBucketSize), bucketSize: EdgeBucketSize);
            surface.AddRange(region.Source.Sides);
            if (region.Built != null) { region.Built.SetActive(false); DestroyGenerated(region.Built); }
            region.Built = BuildMesh("Dynamic ground sector - " + region.Name, region.Source.Renderer.transform, surface, buildCollision);
            region.Source.Renderer.enabled = false;
            CityWetSurfaceRegistry.Unregister(region.Source.Renderer);
            foreach (Collider collider in region.Source.Renderer.GetComponents<Collider>()) collider.enabled = false;
        }

        private void RebuildDynamic(Source source)
        {
            foreach (DynamicRegion region in dynamicRegions)
                if (region.Name == source.Renderer.name)
                { region.Source = source; BuildDynamic(region); return; }
        }

        private static void DestroyGenerated(UnityEngine.Object target)
        { if (Application.isPlaying) Destroy(target); else DestroyImmediate(target); }
        private static Rect Bounds(Triangle triangle) => Rect.MinMaxRect(
            Mathf.Min(triangle.A.Position.x, Mathf.Min(triangle.B.Position.x, triangle.C.Position.x)),
            Mathf.Min(triangle.A.Position.z, Mathf.Min(triangle.B.Position.z, triangle.C.Position.z)),
            Mathf.Max(triangle.A.Position.x, Mathf.Max(triangle.B.Position.x, triangle.C.Position.x)),
            Mathf.Max(triangle.A.Position.z, Mathf.Max(triangle.B.Position.z, triangle.C.Position.z)));

        private sealed class Source
        {
            private static int nextOrder;
            internal Source(Renderer renderer, int priority, bool paint, bool dynamic)
            { Renderer = renderer; Priority = priority; Paint = paint; Dynamic = dynamic; Order = nextOrder++; }
            internal readonly Renderer Renderer; internal readonly bool Dynamic; internal readonly int Order;
            internal int Priority; internal bool Paint;
            internal List<Triangle> Top, Sides, Support;
        }
        private sealed class Style
        {
            private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Shader, PropertyLayout>
                PropertyLayouts = new System.Runtime.CompilerServices.ConditionalWeakTable<Shader, PropertyLayout>();
            private readonly PropertyLayout propertyLayout;
            private readonly PropertyValue[] propertyValues;

            internal Style(Renderer source, int slot, Material material, MaterialPropertyBlock properties, FootstepGroundKind kind)
            {
                Material = material; Properties = properties; Kind = kind;
                HasWeather = CityWetSurfaceRegistry.TryReadRegistration(source, slot, out CityWetSurfaceKind wetKind, out Color dryTint) ||
                    CityWetSurfaceRegistry.TryReadRegistration(source, -1, out wetKind, out dryTint);
                WetKind = wetKind; DryTint = dryTint;
                propertyLayout = PropertyLayouts.GetValue(material.shader, ReadPropertyLayout);
                propertyValues = new PropertyValue[propertyLayout.Ids.Length];
                for (int p = 0; p < propertyValues.Length; p++)
                    propertyValues[p] = new PropertyValue(properties, propertyLayout.Ids[p], propertyLayout.Types[p]);
            }
            internal Style(Style source, FootstepGroundKind kind)
            {
                Material = source.Material; Properties = source.Properties; Kind = kind;
                HasWeather = source.HasWeather; WetKind = source.WetKind; DryTint = source.DryTint;
                propertyLayout = source.propertyLayout; propertyValues = source.propertyValues;
            }
            internal readonly Material Material;
            internal readonly MaterialPropertyBlock Properties; internal readonly FootstepGroundKind Kind;
            internal readonly bool HasWeather; internal readonly CityWetSurfaceKind WetKind; internal readonly Color DryTint;

            internal bool Equivalent(Style other)
            {
                if (Material != other.Material || Kind != other.Kind || HasWeather != other.HasWeather ||
                    (HasWeather && (WetKind != other.WetKind || DryTint != other.DryTint))) return false;
                if (!ReferenceEquals(propertyLayout, other.propertyLayout)) return false;
                // Capture native shader/MPB state once. Comparisons happen for
                // every possible style match and must only read these snapshots.
                for (int p = 0; p < propertyValues.Length; p++)
                {
                    PropertyValue value = propertyValues[p], otherValue = other.propertyValues[p];
                    if (value.Present != otherValue.Present) return false;
                    if (!value.Present) continue;
                    switch (propertyLayout.Types[p])
                    {
                        case ShaderPropertyType.Texture:
                            if (value.Texture != otherValue.Texture) return false; break;
                        case ShaderPropertyType.Color:
                        case ShaderPropertyType.Vector:
                            if (value.Vector != otherValue.Vector) return false; break;
                        default:
                            if (value.Scalar != otherValue.Scalar) return false; break;
                    }
                }
                return true;
            }

            private static PropertyLayout ReadPropertyLayout(Shader shader)
            {
                int count = shader.GetPropertyCount();
                var layout = new PropertyLayout(count);
                for (int p = 0; p < count; p++)
                {
                    layout.Ids[p] = shader.GetPropertyNameId(p);
                    layout.Types[p] = shader.GetPropertyType(p);
                }
                return layout;
            }

            private sealed class PropertyLayout
            {
                internal PropertyLayout(int count)
                { Ids = new int[count]; Types = new ShaderPropertyType[count]; }
                internal readonly int[] Ids;
                internal readonly ShaderPropertyType[] Types;
            }

            private readonly struct PropertyValue
            {
                internal PropertyValue(MaterialPropertyBlock properties, int id, ShaderPropertyType type)
                {
                    Present = properties.HasProperty(id);
                    Texture = null; Vector = default; Scalar = 0f;
                    if (!Present) return;
                    switch (type)
                    {
                        case ShaderPropertyType.Texture: Texture = properties.GetTexture(id); break;
                        case ShaderPropertyType.Color:
                        case ShaderPropertyType.Vector: Vector = properties.GetVector(id); break;
                        default: Scalar = properties.GetFloat(id); break;
                    }
                }
                internal readonly bool Present;
                internal readonly Texture Texture;
                internal readonly Vector4 Vector;
                internal readonly float Scalar;
            }
        }
        private sealed class DynamicRegion
        {
            internal DynamicRegion(Source source)
            {
                Source = source; Name = source.Renderer.name;
                Bounds = CityGroundSurfaceSystem.Bounds(source.Top[0]);
                foreach (Triangle triangle in source.Top)
                {
                    Rect bounds = CityGroundSurfaceSystem.Bounds(triangle);
                    Bounds = Rect.MinMaxRect(Mathf.Min(Bounds.xMin, bounds.xMin), Mathf.Min(Bounds.yMin, bounds.yMin),
                        Mathf.Max(Bounds.xMax, bounds.xMax), Mathf.Max(Bounds.yMax, bounds.yMax));
                }
            }
            internal Source Source; internal readonly string Name; internal Rect Bounds;
            internal readonly List<Triangle> Coatings = new List<Triangle>(); internal GameObject Built;
        }
    }
}
