using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Shared infrastructure of the city audit probe: renderer and
    /// collider registries, the world-space triangle index, the
    /// 0.25 m standing grid and the geometry helpers every detector
    /// leans on. Nothing here decides what a defect is.
    /// </summary>
    public sealed partial class CityAuditProbeTests
    {
        internal enum SurfaceClass : byte
        {
            Other = 0,
            Ground = 1,
            Road = 2,
            Sidewalk = 3,
            Marking = 4,
            Puddle = 5,
            Water = 6,
            SeaBed = 7,
            Fence = 8,
            Building = 9,
            Mountain = 10,
            Detail = 11
        }

        internal static bool IsFlatClass(SurfaceClass c)
        {
            return c == SurfaceClass.Ground ||
                   c == SurfaceClass.Road ||
                   c == SurfaceClass.Sidewalk ||
                   c == SurfaceClass.Marking ||
                   c == SurfaceClass.Puddle ||
                   c == SurfaceClass.Water ||
                   c == SurfaceClass.SeaBed;
        }

        internal static bool IsDrawnGroundClass(SurfaceClass c)
        {
            return c == SurfaceClass.Ground ||
                   c == SurfaceClass.Road ||
                   c == SurfaceClass.Sidewalk;
        }

        internal static bool IsSeamClass(SurfaceClass c)
        {
            return c == SurfaceClass.Ground ||
                   c == SurfaceClass.Road ||
                   c == SurfaceClass.Sidewalk ||
                   c == SurfaceClass.Mountain ||
                   c == SurfaceClass.SeaBed;
        }

        internal static bool IsPropClass(SurfaceClass c)
        {
            return c == SurfaceClass.Other || c == SurfaceClass.Detail;
        }

        private sealed class ClassRule
        {
            public ClassRule(string pattern, SurfaceClass surfaceClass)
            {
                Pattern = new Regex(pattern, RegexOptions.CultureInvariant);
                Class = surfaceClass;
            }

            public Regex Pattern { get; }
            public SurfaceClass Class { get; }
        }

        // Leaf rules match the object's own name; subtree rules match any
        // ancestor below the host. Names verified by grep against the
        // world builders (CityWorldBuilder, CitySeacoastWorldBuilder,
        // CityPuddleWorldBuilder, RoadFenceWorldBuilder, ...).
        private static readonly ClassRule[] LeafRules =
        {
            new ClassRule(
                @"^(Active Land|Park Lawn|Yard Ground|Mountain Forefield Ground|Church Ground|Beach|Park Plaza|Cemetery Ground|Public Ground)($| |\()",
                SurfaceClass.Ground),
            new ClassRule(@"^(Street Surfaces|Park Paths)$", SurfaceClass.Road),
            new ClassRule(@"^Sidewalk Surfaces$", SurfaceClass.Sidewalk),
            new ClassRule(
                @"^(Road Center Markings|Pedestrian Crossings)$",
                SurfaceClass.Marking),
            new ClassRule(
                @"^(Gutter Puddle Water|City Puddle Water Sheet)$",
                SurfaceClass.Puddle),
            new ClassRule(
                @"^(Sea|Sea Water \d+|Sea Swash \d+|Mouth Spill|River Water -?\d+)$",
                SurfaceClass.Water),
            new ClassRule(@"^Sea Bed Slope$", SurfaceClass.SeaBed)
        };

        private static readonly ClassRule[] SubtreeRules =
        {
            new ClassRule(
                @"^(Road Edge Fences|Terrain Guard Rails|Quay Guard Rails)$",
                SurfaceClass.Fence),
            new ClassRule(@"^Physical Ridges$", SurfaceClass.Mountain),
            new ClassRule(@"^City Detail Chunk -?\d+ -?\d+$", SurfaceClass.Detail),
            new ClassRule(
                @"^(Building -?\d+--?\d+|Bar .+|Player Home|Supermarket|Prototype Foundation|Exterior Prototype Foundation|Blender Building Prototype|Building Mass|Church.*)$",
                SurfaceClass.Building)
        };

        private static readonly Regex KnownInvisibleColliderPattern = new Regex(
            @"(Continuous Invisible Ramp Collider|Foot Probe Surface|Building Mass|Church Collision|Church Courtyard Collision|Garden Fence Collision \d+|Imported Collision|Toe Collider \d+|Park Tree Colliders|Park Hedge Colliders|Park Bench Colliders|Tree Collider \d+|Home Mailbox Collider|Open Area Collision Proxies|City Arch Shelter Collision|City Arch Shelter Rain Volumes)",
            RegexOptions.CultureInvariant);

        private static readonly Regex ProxyColliderPattern = new Regex(
            @"(City Detail Chunk -?\d+ -?\d+|Open Area Collision Proxies|Park (Tree|Hedge|Bench) Colliders|Home Mailbox Collider|Church.*Collision|Building Mass)",
            RegexOptions.CultureInvariant);

        private static readonly Regex SwashPattern = new Regex(
            @"^Sea Swash \d+$",
            RegexOptions.CultureInvariant);

        internal static SurfaceClass Classify(Transform target, Transform host)
        {
            string name = target.name;
            for (int index = 0; index < LeafRules.Length; index++)
            {
                if (LeafRules[index].Pattern.IsMatch(name))
                {
                    return LeafRules[index].Class;
                }
            }

            for (Transform ancestor = target;
                 ancestor != null && ancestor != host;
                 ancestor = ancestor.parent)
            {
                string ancestorName = ancestor.name;
                for (int index = 0; index < SubtreeRules.Length; index++)
                {
                    if (SubtreeRules[index].Pattern.IsMatch(ancestorName))
                    {
                        return SubtreeRules[index].Class;
                    }
                }
            }

            return SurfaceClass.Other;
        }

        internal static string PathOf(Transform target, Transform host)
        {
            var segments = new List<string>();
            for (Transform current = target;
                 current != null && current != host;
                 current = current.parent)
            {
                segments.Add(current.name);
            }

            var builder = new StringBuilder();
            for (int index = segments.Count - 1; index >= 0; index--)
            {
                if (builder.Length > 0)
                {
                    builder.Append('/');
                }

                builder.Append(segments[index]);
            }

            return builder.ToString();
        }

        internal static bool IsUnder(Transform target, Transform host)
        {
            if (target == null || host == null)
            {
                return false;
            }

            return target == host || target.IsChildOf(host);
        }

        // ------------------------------------------------------------------
        // Mesh reading. Never gated on Mesh.isReadable: runtime-combined
        // meshes report false while vertices/triangles still answer.
        // ------------------------------------------------------------------

        internal sealed class MeshData
        {
            public Vector3[] Vertices;
            public int[][] Submeshes;
            public bool Readable;
            public int TriangleCount;
        }

        internal sealed class MeshCache
        {
            // Keyed by the Mesh reference: UnityEngine.Object hashes by
            // its instance id, so one shared mesh is read exactly once.
            private readonly Dictionary<Mesh, MeshData> map =
                new Dictionary<Mesh, MeshData>();

            public MeshData Get(Mesh mesh)
            {
                if (map.TryGetValue(mesh, out MeshData data))
                {
                    return data;
                }

                data = new MeshData();
                Vector3[] vertices = mesh.vertices;
                data.Readable = vertices != null &&
                                vertices.Length > 0 &&
                                vertices.Length == mesh.vertexCount;
                if (data.Readable)
                {
                    data.Vertices = vertices;
                    int submeshCount = mesh.subMeshCount;
                    data.Submeshes = new int[submeshCount][];
                    for (int index = 0; index < submeshCount; index++)
                    {
                        int[] triangles = mesh.GetTriangles(index);
                        data.Submeshes[index] = triangles ?? Array.Empty<int>();
                        data.TriangleCount += data.Submeshes[index].Length / 3;
                    }
                }
                else
                {
                    data.Vertices = Array.Empty<Vector3>();
                    data.Submeshes = Array.Empty<int[]>();
                }

                map[mesh] = data;
                return data;
            }
        }

        // ------------------------------------------------------------------
        // Renderer registry.
        // ------------------------------------------------------------------

        internal sealed class RendererRecord
        {
            public int Index;
            public MeshRenderer Renderer;
            public Transform Transform;
            public string Name;
            public string Path;
            public SurfaceClass Class;
            public Mesh Mesh;
            public MeshData Data;
            public Matrix4x4 LocalToWorld;
            public Bounds Bounds;
            public string[] ShaderNames;
            public int[] RenderQueues;
            public bool[] WritesDepth;
            public float[] Cull;
            public int TriStart;
            public int TriEnd;
            public int ComponentBase;
            public int ComponentCount;
            public bool IsSwash;
            public bool IsBeach;

            public int TriangleCount => TriEnd - TriStart;

            public bool AnyCullOff()
            {
                for (int index = 0; index < Cull.Length; index++)
                {
                    if (Cull[index] == 0f)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        internal sealed class RendererRegistry
        {
            public readonly List<RendererRecord> Records =
                new List<RendererRecord>();

            public readonly MeshCache Meshes = new MeshCache();

            public int SkippedDisabled;
            public int SkippedText;
            public int SkippedNoMesh;
            public int Unreadable;

            public static RendererRegistry Build(Transform host)
            {
                var registry = new RendererRegistry();
                MeshRenderer[] renderers =
                    host.GetComponentsInChildren<MeshRenderer>(true);
                for (int index = 0; index < renderers.Length; index++)
                {
                    MeshRenderer renderer = renderers[index];
                    if (renderer == null ||
                        !renderer.enabled ||
                        !renderer.gameObject.activeInHierarchy)
                    {
                        registry.SkippedDisabled++;
                        continue;
                    }

                    if (renderer.GetComponent<TMPro.TMP_Text>() != null)
                    {
                        registry.SkippedText++;
                        continue;
                    }

                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null)
                    {
                        registry.SkippedNoMesh++;
                        continue;
                    }

                    Mesh mesh = filter.sharedMesh;
                    MeshData data = registry.Meshes.Get(mesh);
                    if (!data.Readable)
                    {
                        registry.Unreadable++;
                    }

                    var record = new RendererRecord
                    {
                        Index = registry.Records.Count,
                        Renderer = renderer,
                        Transform = renderer.transform,
                        Name = renderer.name,
                        Path = PathOf(renderer.transform, host),
                        Class = Classify(renderer.transform, host),
                        Mesh = mesh,
                        Data = data,
                        LocalToWorld = renderer.transform.localToWorldMatrix,
                        Bounds = renderer.bounds
                    };
                    record.IsSwash = SwashPattern.IsMatch(record.Name);
                    record.IsBeach = record.Name == "Beach";

                    int submeshCount = Math.Max(1, mesh.subMeshCount);
                    Material[] materials = renderer.sharedMaterials;
                    record.ShaderNames = new string[submeshCount];
                    record.RenderQueues = new int[submeshCount];
                    record.WritesDepth = new bool[submeshCount];
                    record.Cull = new float[submeshCount];
                    for (int submesh = 0; submesh < submeshCount; submesh++)
                    {
                        Material material = null;
                        if (materials != null && materials.Length > 0)
                        {
                            material = submesh < materials.Length
                                ? materials[submesh]
                                : materials[materials.Length - 1];
                        }

                        record.ShaderNames[submesh] = string.Empty;
                        record.RenderQueues[submesh] = -1;
                        record.WritesDepth[submesh] = true;
                        record.Cull[submesh] = -1f;
                        if (material == null)
                        {
                            continue;
                        }

                        record.ShaderNames[submesh] = material.shader != null
                            ? material.shader.name
                            : string.Empty;
                        record.RenderQueues[submesh] = material.renderQueue;
                        if (material.HasProperty("_ZWrite") &&
                            material.GetFloat("_ZWrite") == 0f)
                        {
                            record.WritesDepth[submesh] = false;
                        }

                        if (material.HasProperty("_WaterZWrite") &&
                            material.GetFloat("_WaterZWrite") == 0f)
                        {
                            record.WritesDepth[submesh] = false;
                        }

                        if (material.HasProperty("_Cull"))
                        {
                            record.Cull[submesh] = material.GetFloat("_Cull");
                        }
                    }

                    registry.Records.Add(record);
                }

                return registry;
            }
        }

        // ------------------------------------------------------------------
        // Vertex keys, union-find and the CSR hash used by both spatial
        // indices.
        // ------------------------------------------------------------------

        internal readonly struct VKey : IEquatable<VKey>
        {
            public readonly int X;
            public readonly int Y;
            public readonly int Z;

            public VKey(int x, int y, int z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public static VKey Of(Vector3 position, float quantum)
            {
                float inverse = 1f / quantum;
                return new VKey(
                    (int)Math.Round(position.x * inverse),
                    (int)Math.Round(position.y * inverse),
                    (int)Math.Round(position.z * inverse));
            }

            public bool Equals(VKey other)
            {
                return X == other.X && Y == other.Y && Z == other.Z;
            }

            public override bool Equals(object obj)
            {
                return obj is VKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = X;
                    hash = (hash * 73856093) ^ (Y * 19349663);
                    return hash ^ (Z * 83492791);
                }
            }
        }

        internal sealed class UnionFind
        {
            private int[] parent;

            public UnionFind(int count)
            {
                parent = new int[count];
                for (int index = 0; index < count; index++)
                {
                    parent[index] = index;
                }
            }

            public int Count => parent.Length;

            public int Find(int index)
            {
                while (parent[index] != index)
                {
                    parent[index] = parent[parent[index]];
                    index = parent[index];
                }

                return index;
            }

            public void Union(int first, int second)
            {
                int rootFirst = Find(first);
                int rootSecond = Find(second);
                if (rootFirst == rootSecond)
                {
                    return;
                }

                if (rootFirst < rootSecond)
                {
                    parent[rootSecond] = rootFirst;
                }
                else
                {
                    parent[rootFirst] = rootSecond;
                }
            }
        }

        /// <summary>
        /// Two-pass compressed-sparse-row hash: Count every key, Finish,
        /// Fill every key again in the same order, then Lookup.
        /// </summary>
        internal sealed class CsrHash
        {
            private readonly Dictionary<long, int> slots =
                new Dictionary<long, int>();

            private readonly List<int> counts = new List<int>();
            private int[] offsets = Array.Empty<int>();
            private int[] cursor = Array.Empty<int>();
            private int[] entries = Array.Empty<int>();
            private long total;

            public int[] Entries => entries;
            public int SlotCount => counts.Count;
            public long EntryCount => total;

            public void Count(long key)
            {
                if (!slots.TryGetValue(key, out int slot))
                {
                    slot = counts.Count;
                    slots.Add(key, slot);
                    counts.Add(0);
                }

                counts[slot]++;
                total++;
            }

            public void Finish()
            {
                offsets = new int[counts.Count + 1];
                for (int index = 0; index < counts.Count; index++)
                {
                    offsets[index + 1] = offsets[index] + counts[index];
                }

                cursor = new int[counts.Count];
                Array.Copy(offsets, cursor, counts.Count);
                entries = new int[total];
            }

            public void Fill(long key, int value)
            {
                int slot = slots[key];
                entries[cursor[slot]++] = value;
            }

            public bool Lookup(long key, out int start, out int end)
            {
                if (slots.TryGetValue(key, out int slot))
                {
                    start = offsets[slot];
                    end = offsets[slot + 1];
                    return true;
                }

                start = 0;
                end = 0;
                return false;
            }
        }

        internal static long CellKey(int x, int y, int z)
        {
            const int offset = 1 << 20;
            return ((long)(x + offset) << 42) |
                   ((long)(y + offset) << 21) |
                   (long)(z + offset);
        }

        internal static int FloorCell(float value, float cellSize)
        {
            return (int)Math.Floor(value / cellSize);
        }

        // ------------------------------------------------------------------
        // World-space triangle index.
        // ------------------------------------------------------------------

        internal struct Tri
        {
            public Vector3 A;
            public Vector3 B;
            public Vector3 C;
            public Vector3 N;
            public float D;
            public int Renderer;
            public int Component;
            public short Submesh;
            public SurfaceClass Class;

            public Vector3 Centroid => (A + B + C) * (1f / 3f);
        }

        internal sealed class TriangleIndex
        {
            public const float CellSize = 1f;
            public const int BigCellLimit = 64;
            // Triangles spanning more than BigCellLimit fine cells (facades, water
            // sheets, the sea bed) live in a second, coarse hash instead of a flat
            // list: a flat list is scanned on EVERY query, and the first city run
            // spent an hour scanning 13 000 big triangles per raster cell.
            public const float CoarseCellSize = 16f;
            public const float ComponentQuantum = 0.0001f;

            public Tri[] Tris = Array.Empty<Tri>();
            public int Count;
            public int Degenerate;
            public int TotalComponents;
            public int BigCount;

            private readonly CsrHash hash = new CsrHash();
            private readonly CsrHash coarse = new CsrHash();
            private readonly List<int> big = new List<int>();
            private int[] stamp = Array.Empty<int>();
            private int stampValue;

            public static void TriBounds(in Tri t, out Vector3 min, out Vector3 max)
            {
                min = Vector3.Min(t.A, Vector3.Min(t.B, t.C));
                max = Vector3.Max(t.A, Vector3.Max(t.B, t.C));
            }

            public static TriangleIndex Build(
                RendererRegistry registry,
                Action<string> log)
            {
                var index = new TriangleIndex();
                long triangleTotal = 0;
                foreach (RendererRecord record in registry.Records)
                {
                    if (record.Data.Readable)
                    {
                        triangleTotal += record.Data.TriangleCount;
                    }
                }

                index.Tris = new Tri[Math.Max(1, (int)Math.Min(triangleTotal, int.MaxValue))];
                int cursor = 0;
                int componentBase = 0;
                foreach (RendererRecord record in registry.Records)
                {
                    record.TriStart = cursor;
                    record.ComponentBase = componentBase;
                    if (!record.Data.Readable)
                    {
                        record.TriEnd = cursor;
                        continue;
                    }

                    MeshData data = record.Data;
                    Matrix4x4 matrix = record.LocalToWorld;
                    int vertexCount = data.Vertices.Length;
                    var world = new Vector3[vertexCount];
                    var keyIds = new int[vertexCount];
                    var keyMap = new Dictionary<VKey, int>(vertexCount);
                    for (int vertex = 0; vertex < vertexCount; vertex++)
                    {
                        Vector3 position = matrix.MultiplyPoint3x4(data.Vertices[vertex]);
                        world[vertex] = position;
                        VKey key = VKey.Of(position, ComponentQuantum);
                        if (!keyMap.TryGetValue(key, out int keyId))
                        {
                            keyId = keyMap.Count;
                            keyMap.Add(key, keyId);
                        }

                        keyIds[vertex] = keyId;
                    }

                    var union = new UnionFind(keyMap.Count);
                    for (int submesh = 0; submesh < data.Submeshes.Length; submesh++)
                    {
                        int[] triangles = data.Submeshes[submesh];
                        for (int t = 0; t + 2 < triangles.Length; t += 3)
                        {
                            int ia = triangles[t];
                            int ib = triangles[t + 1];
                            int ic = triangles[t + 2];
                            if (ia >= vertexCount || ib >= vertexCount || ic >= vertexCount)
                            {
                                index.Degenerate++;
                                continue;
                            }

                            Vector3 a = world[ia];
                            Vector3 b = world[ib];
                            Vector3 c = world[ic];
                            Vector3 normal = Vector3.Cross(b - a, c - a);
                            if (normal.sqrMagnitude < 1e-14f)
                            {
                                index.Degenerate++;
                                continue;
                            }

                            normal.Normalize();
                            union.Union(keyIds[ia], keyIds[ib]);
                            union.Union(keyIds[ib], keyIds[ic]);
                            index.Tris[cursor] = new Tri
                            {
                                A = a,
                                B = b,
                                C = c,
                                N = normal,
                                D = Vector3.Dot(normal, a),
                                Renderer = record.Index,
                                Component = keyIds[ia],
                                Submesh = (short)submesh,
                                Class = record.Class
                            };
                            cursor++;
                        }
                    }

                    var dense = new Dictionary<int, int>();
                    for (int t = record.TriStart; t < cursor; t++)
                    {
                        int root = union.Find(index.Tris[t].Component);
                        if (!dense.TryGetValue(root, out int local))
                        {
                            local = dense.Count;
                            dense.Add(root, local);
                        }

                        index.Tris[t].Component = componentBase + local;
                    }

                    record.TriEnd = cursor;
                    record.ComponentCount = dense.Count;
                    componentBase += dense.Count;
                }

                index.Count = cursor;
                index.TotalComponents = componentBase;
                index.BuildHash(log);
                return index;
            }

            private void BuildHash(Action<string> log)
            {
                stamp = new int[Math.Max(1, Count)];
                var isBig = new bool[Math.Max(1, Count)];
                for (int t = 0; t < Count; t++)
                {
                    TriBounds(in Tris[t], out Vector3 min, out Vector3 max);
                    int x0 = FloorCell(min.x, CellSize), x1 = FloorCell(max.x, CellSize);
                    int y0 = FloorCell(min.y, CellSize), y1 = FloorCell(max.y, CellSize);
                    int z0 = FloorCell(min.z, CellSize), z1 = FloorCell(max.z, CellSize);
                    long cells = (long)(x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1);
                    if (cells > BigCellLimit)
                    {
                        isBig[t] = true;
                        big.Add(t);
                        int cx0 = FloorCell(min.x, CoarseCellSize), cx1 = FloorCell(max.x, CoarseCellSize);
                        int cy0 = FloorCell(min.y, CoarseCellSize), cy1 = FloorCell(max.y, CoarseCellSize);
                        int cz0 = FloorCell(min.z, CoarseCellSize), cz1 = FloorCell(max.z, CoarseCellSize);
                        for (int x = cx0; x <= cx1; x++)
                        for (int y = cy0; y <= cy1; y++)
                        for (int z = cz0; z <= cz1; z++)
                        {
                            coarse.Count(CellKey(x, y, z));
                        }

                        continue;
                    }

                    for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                    for (int z = z0; z <= z1; z++)
                    {
                        hash.Count(CellKey(x, y, z));
                    }
                }

                hash.Finish();
                coarse.Finish();
                for (int t = 0; t < Count; t++)
                {
                    TriBounds(in Tris[t], out Vector3 min, out Vector3 max);
                    if (isBig[t])
                    {
                        int cx0 = FloorCell(min.x, CoarseCellSize), cx1 = FloorCell(max.x, CoarseCellSize);
                        int cy0 = FloorCell(min.y, CoarseCellSize), cy1 = FloorCell(max.y, CoarseCellSize);
                        int cz0 = FloorCell(min.z, CoarseCellSize), cz1 = FloorCell(max.z, CoarseCellSize);
                        for (int x = cx0; x <= cx1; x++)
                        for (int y = cy0; y <= cy1; y++)
                        for (int z = cz0; z <= cz1; z++)
                        {
                            coarse.Fill(CellKey(x, y, z), t);
                        }

                        continue;
                    }

                    int x0 = FloorCell(min.x, CellSize), x1 = FloorCell(max.x, CellSize);
                    int y0 = FloorCell(min.y, CellSize), y1 = FloorCell(max.y, CellSize);
                    int z0 = FloorCell(min.z, CellSize), z1 = FloorCell(max.z, CellSize);
                    for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                    for (int z = z0; z <= z1; z++)
                    {
                        hash.Fill(CellKey(x, y, z), t);
                    }
                }

                BigCount = big.Count;
                log?.Invoke(
                    "triangle index: " + Count + " triangles, " +
                    hash.SlotCount + " cells, " + hash.EntryCount +
                    " entries, " + big.Count + " big, " + Degenerate +
                    " degenerate, " + TotalComponents + " components");
            }

            public void QueryBox(Vector3 min, Vector3 max, List<int> result)
            {
                result.Clear();
                stampValue++;
                if (stampValue == int.MaxValue)
                {
                    Array.Clear(stamp, 0, stamp.Length);
                    stampValue = 1;
                }

                int x0 = FloorCell(min.x, CellSize), x1 = FloorCell(max.x, CellSize);
                int y0 = FloorCell(min.y, CellSize), y1 = FloorCell(max.y, CellSize);
                int z0 = FloorCell(min.z, CellSize), z1 = FloorCell(max.z, CellSize);
                int[] entries = hash.Entries;
                for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                {
                    if (!hash.Lookup(CellKey(x, y, z), out int start, out int end))
                    {
                        continue;
                    }

                    for (int slot = start; slot < end; slot++)
                    {
                        int t = entries[slot];
                        if (stamp[t] == stampValue)
                        {
                            continue;
                        }

                        stamp[t] = stampValue;
                        if (Overlaps(t, min, max))
                        {
                            result.Add(t);
                        }
                    }
                }

                int[] coarseEntries = coarse.Entries;
                int cx0 = FloorCell(min.x, CoarseCellSize), cx1 = FloorCell(max.x, CoarseCellSize);
                int cy0 = FloorCell(min.y, CoarseCellSize), cy1 = FloorCell(max.y, CoarseCellSize);
                int cz0 = FloorCell(min.z, CoarseCellSize), cz1 = FloorCell(max.z, CoarseCellSize);
                for (int x = cx0; x <= cx1; x++)
                for (int y = cy0; y <= cy1; y++)
                for (int z = cz0; z <= cz1; z++)
                {
                    if (!coarse.Lookup(CellKey(x, y, z), out int start, out int end))
                    {
                        continue;
                    }

                    for (int slot = start; slot < end; slot++)
                    {
                        int t = coarseEntries[slot];
                        if (stamp[t] == stampValue)
                        {
                            continue;
                        }

                        stamp[t] = stampValue;
                        if (Overlaps(t, min, max))
                        {
                            result.Add(t);
                        }
                    }
                }
            }

            private bool Overlaps(int t, Vector3 min, Vector3 max)
            {
                TriBounds(in Tris[t], out Vector3 tmin, out Vector3 tmax);
                return tmin.x <= max.x && tmax.x >= min.x &&
                       tmin.y <= max.y && tmax.y >= min.y &&
                       tmin.z <= max.z && tmax.z >= min.z;
            }

            public void QueryPoint(Vector3 point, float pad, List<int> result)
            {
                var half = new Vector3(pad, pad, pad);
                QueryBox(point - half, point + half, result);
            }

            public void QueryPoint(Vector3 point, Vector3 pad, List<int> result)
            {
                QueryBox(point - pad, point + pad, result);
            }
        }

        // ------------------------------------------------------------------
        // Collider registry.
        // ------------------------------------------------------------------

        internal sealed class ColliderRecord
        {
            public int Index;
            public Collider Collider;
            public Transform Transform;
            public string Name;
            public string Path;
            public string Type;
            public bool IsTrigger;
            public int Layer;
            public Bounds Bounds;
            public bool KnownInvisible;
            public bool Visible;
            public bool HasOwnRenderer;
            public bool IsProxy;
            public SurfaceClass Class;
        }

        internal sealed class ColliderRegistry
        {
            public readonly List<ColliderRecord> Records =
                new List<ColliderRecord>();

            private readonly Dictionary<Collider, ColliderRecord> byCollider =
                new Dictionary<Collider, ColliderRecord>();

            public ColliderRecord Find(Collider collider)
            {
                if (collider == null)
                {
                    return null;
                }

                return byCollider.TryGetValue(collider, out ColliderRecord record)
                    ? record
                    : null;
            }

            public static ColliderRegistry Build(
                Transform host,
                TriangleIndex tris,
                List<int> scratch)
            {
                var registry = new ColliderRegistry();
                Collider[] colliders = host.GetComponentsInChildren<Collider>(true);
                for (int index = 0; index < colliders.Length; index++)
                {
                    Collider collider = colliders[index];
                    if (collider == null ||
                        !collider.enabled ||
                        !collider.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    var record = new ColliderRecord
                    {
                        Index = registry.Records.Count,
                        Collider = collider,
                        Transform = collider.transform,
                        Name = collider.name,
                        Path = PathOf(collider.transform, host),
                        Type = collider.GetType().Name,
                        IsTrigger = collider.isTrigger,
                        Layer = collider.gameObject.layer,
                        Bounds = collider.bounds,
                        Class = Classify(collider.transform, host)
                    };
                    record.KnownInvisible =
                        KnownInvisibleColliderPattern.IsMatch(record.Path);
                    record.IsProxy = ProxyColliderPattern.IsMatch(record.Path);
                    record.HasOwnRenderer = HasEnabledRenderer(collider.transform);
                    record.Visible = record.HasOwnRenderer ||
                                     DrawsInside(tris, record.Bounds, scratch);
                    registry.Records.Add(record);
                    registry.byCollider[collider] = record;
                }

                return registry;
            }

            private static bool HasEnabledRenderer(Transform target)
            {
                MeshRenderer[] renderers =
                    target.GetComponentsInChildren<MeshRenderer>(true);
                for (int index = 0; index < renderers.Length; index++)
                {
                    MeshRenderer renderer = renderers[index];
                    if (renderer.enabled &&
                        renderer.gameObject.activeInHierarchy &&
                        renderer.GetComponent<TMPro.TMP_Text>() == null)
                    {
                        MeshFilter filter = renderer.GetComponent<MeshFilter>();
                        if (filter != null && filter.sharedMesh != null)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            private static bool DrawsInside(
                TriangleIndex tris,
                Bounds bounds,
                List<int> scratch)
            {
                const float shrink = 0.02f;
                Vector3 min = bounds.min + new Vector3(shrink, shrink, shrink);
                Vector3 max = bounds.max - new Vector3(shrink, shrink, shrink);
                if (min.x > max.x || min.y > max.y || min.z > max.z)
                {
                    return false;
                }

                Vector3 center = (min + max) * 0.5f;
                Vector3 half = (max - min) * 0.5f;
                tris.QueryBox(min, max, scratch);
                for (int index = 0; index < scratch.Count; index++)
                {
                    ref Tri t = ref tris.Tris[scratch[index]];
                    if (IsFlatClass(t.Class))
                    {
                        continue;
                    }

                    if (Geo.TriBoxOverlap(center, half, t.A, t.B, t.C))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        // ------------------------------------------------------------------
        // Rectangle bucket index for point-in-rect lookups.
        // ------------------------------------------------------------------

        internal sealed class RectBucketIndex
        {
            private const float BucketSize = 4f;
            private readonly Dictionary<long, List<int>> buckets =
                new Dictionary<long, List<int>>();

            private readonly IReadOnlyList<Rect> rects;

            public RectBucketIndex(IReadOnlyList<Rect> source)
            {
                rects = source ?? Array.Empty<Rect>();
                for (int index = 0; index < rects.Count; index++)
                {
                    Rect rect = rects[index];
                    int x0 = FloorCell(rect.xMin, BucketSize);
                    int x1 = FloorCell(rect.xMax, BucketSize);
                    int z0 = FloorCell(rect.yMin, BucketSize);
                    int z1 = FloorCell(rect.yMax, BucketSize);
                    for (int x = x0; x <= x1; x++)
                    for (int z = z0; z <= z1; z++)
                    {
                        long key = CellKey(x, 0, z);
                        if (!buckets.TryGetValue(key, out List<int> list))
                        {
                            list = new List<int>();
                            buckets.Add(key, list);
                        }

                        list.Add(index);
                    }
                }
            }

            public int Count => rects.Count;

            public int Find(float x, float z)
            {
                long key = CellKey(FloorCell(x, BucketSize), 0, FloorCell(z, BucketSize));
                if (!buckets.TryGetValue(key, out List<int> list))
                {
                    return -1;
                }

                for (int index = 0; index < list.Count; index++)
                {
                    Rect rect = rects[list[index]];
                    if (x >= rect.xMin && x <= rect.xMax &&
                        z >= rect.yMin && z <= rect.yMax)
                    {
                        return list[index];
                    }
                }

                return -1;
            }
        }

        // ------------------------------------------------------------------
        // Standing grid.
        // ------------------------------------------------------------------

        internal sealed class StandingGrid
        {
            public const byte Inside = 1;
            public const byte Touching = 2;
            public const byte Water = 4;
            public const byte HasGround = 8;
            public const byte Occupied = 16;
            public const byte Ring = 32;
            public const byte RoadRect = 64;
            public const byte SidewalkRect = 128;

            public float XMin;
            public float ZMin;
            public float Step;
            public int NX;
            public int NZ;
            public byte[] Flags = Array.Empty<byte>();
            public float[] GroundY = Array.Empty<float>();
            public float[] ExpectedY = Array.Empty<float>();
            public int[] Occupant = Array.Empty<int>();
            public int[] GroundCollider = Array.Empty<int>();
            public int InsideCount;
            public int RingCount;
            public int RayCount;
            public int OverlapCount;
            public int ForeignHits;

            public int CellCount => NX * NZ;

            public int Index(int ix, int iz)
            {
                return iz * NX + ix;
            }

            public bool InGrid(int ix, int iz)
            {
                return ix >= 0 && iz >= 0 && ix < NX && iz < NZ;
            }

            public float CenterX(int ix)
            {
                return XMin + (ix + 0.5f) * Step;
            }

            public float CenterZ(int iz)
            {
                return ZMin + (iz + 0.5f) * Step;
            }

            public bool TryCell(float x, float z, out int ix, out int iz)
            {
                ix = (int)Math.Floor((x - XMin) / Step);
                iz = (int)Math.Floor((z - ZMin) / Step);
                return InGrid(ix, iz);
            }

            public bool Has(int index, byte flag)
            {
                return (Flags[index] & flag) != 0;
            }

            public bool IsInside(int ix, int iz)
            {
                return InGrid(ix, iz) && (Flags[Index(ix, iz)] & Inside) != 0;
            }

            public float CellArea => Step * Step;
        }

        // ------------------------------------------------------------------
        // Geometry helpers.
        // ------------------------------------------------------------------

        internal static class Geo
        {
            /// <summary>Separating-axis triangle/box overlap (13 axes).</summary>
            public static bool TriBoxOverlap(
                Vector3 center,
                Vector3 half,
                Vector3 a,
                Vector3 b,
                Vector3 c)
            {
                Vector3 v0 = a - center;
                Vector3 v1 = b - center;
                Vector3 v2 = c - center;
                Vector3 tmin = Vector3.Min(v0, Vector3.Min(v1, v2));
                Vector3 tmax = Vector3.Max(v0, Vector3.Max(v1, v2));
                if (tmin.x > half.x || tmax.x < -half.x ||
                    tmin.y > half.y || tmax.y < -half.y ||
                    tmin.z > half.z || tmax.z < -half.z)
                {
                    return false;
                }

                Vector3 e0 = v1 - v0;
                Vector3 e1 = v2 - v1;
                Vector3 e2 = v0 - v2;
                Vector3 normal = Vector3.Cross(e0, e1);
                float distance = Vector3.Dot(normal, v0);
                float radius = half.x * Math.Abs(normal.x) +
                               half.y * Math.Abs(normal.y) +
                               half.z * Math.Abs(normal.z);
                if (distance > radius || distance < -radius)
                {
                    return false;
                }

                return CrossAxes(e0, v0, v1, v2, half) &&
                       CrossAxes(e1, v0, v1, v2, half) &&
                       CrossAxes(e2, v0, v1, v2, half);
            }

            private static bool CrossAxes(
                Vector3 edge,
                Vector3 v0,
                Vector3 v1,
                Vector3 v2,
                Vector3 half)
            {
                return AxisTest(Vector3.Cross(Vector3.right, edge), v0, v1, v2, half) &&
                       AxisTest(Vector3.Cross(Vector3.up, edge), v0, v1, v2, half) &&
                       AxisTest(Vector3.Cross(Vector3.forward, edge), v0, v1, v2, half);
            }

            private static bool AxisTest(
                Vector3 axis,
                Vector3 v0,
                Vector3 v1,
                Vector3 v2,
                Vector3 half)
            {
                if (axis.sqrMagnitude < 1e-16f)
                {
                    return true;
                }

                float p0 = Vector3.Dot(axis, v0);
                float p1 = Vector3.Dot(axis, v1);
                float p2 = Vector3.Dot(axis, v2);
                float min = Math.Min(p0, Math.Min(p1, p2));
                float max = Math.Max(p0, Math.Max(p1, p2));
                float radius = half.x * Math.Abs(axis.x) +
                               half.y * Math.Abs(axis.y) +
                               half.z * Math.Abs(axis.z);
                return !(min > radius || max < -radius);
            }

            /// <summary>Squared distance from a point to a triangle (Ericson).</summary>
            public static float PointTriDistanceSq(
                Vector3 p,
                Vector3 a,
                Vector3 b,
                Vector3 c)
            {
                Vector3 ab = b - a;
                Vector3 ac = c - a;
                Vector3 ap = p - a;
                float d1 = Vector3.Dot(ab, ap);
                float d2 = Vector3.Dot(ac, ap);
                if (d1 <= 0f && d2 <= 0f)
                {
                    return ap.sqrMagnitude;
                }

                Vector3 bp = p - b;
                float d3 = Vector3.Dot(ab, bp);
                float d4 = Vector3.Dot(ac, bp);
                if (d3 >= 0f && d4 <= d3)
                {
                    return bp.sqrMagnitude;
                }

                float vc = d1 * d4 - d3 * d2;
                if (vc <= 0f && d1 >= 0f && d3 <= 0f)
                {
                    float v = d1 / (d1 - d3);
                    return (p - (a + ab * v)).sqrMagnitude;
                }

                Vector3 cp = p - c;
                float d5 = Vector3.Dot(ab, cp);
                float d6 = Vector3.Dot(ac, cp);
                if (d6 >= 0f && d5 <= d6)
                {
                    return cp.sqrMagnitude;
                }

                float vb = d5 * d2 - d1 * d6;
                if (vb <= 0f && d2 >= 0f && d6 <= 0f)
                {
                    float w = d2 / (d2 - d6);
                    return (p - (a + ac * w)).sqrMagnitude;
                }

                float va = d3 * d6 - d5 * d4;
                if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
                {
                    float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                    return (p - (b + (c - b) * w)).sqrMagnitude;
                }

                float denominator = 1f / (va + vb + vc);
                float v2 = vb * denominator;
                float w2 = vc * denominator;
                return (p - (a + ab * v2 + ac * w2)).sqrMagnitude;
            }

            /// <summary>
            /// Whether the XZ point lies inside the triangle's footprint;
            /// answers the plane height at that point when it does.
            /// </summary>
            public static bool ContainsXZ(
                in Vector3 a,
                in Vector3 b,
                in Vector3 c,
                float x,
                float z,
                out float y)
            {
                float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Math.Abs(denominator) < 1e-12f)
                {
                    y = 0f;
                    return false;
                }

                float l1 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / denominator;
                float l2 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / denominator;
                float l3 = 1f - l1 - l2;
                const float epsilon = -1e-4f;
                if (l1 < epsilon || l2 < epsilon || l3 < epsilon)
                {
                    y = 0f;
                    return false;
                }

                y = l1 * a.y + l2 * b.y + l3 * c.y;
                return true;
            }

            /// <summary>
            /// Area of the intersection of two triangles in the plane
            /// (Sutherland–Hodgman, subject clipped by clip) and its
            /// centroid. Zero when they do not overlap.
            /// </summary>
            public static float ClipArea(
                Vector2[] subject,
                Vector2[] clip,
                List<Vector2> input,
                List<Vector2> output,
                out Vector2 centroid)
            {
                centroid = Vector2.zero;
                float clipSign = Cross(clip[1] - clip[0], clip[2] - clip[0]);
                if (Math.Abs(clipSign) < 1e-12f)
                {
                    return 0f;
                }

                output.Clear();
                output.Add(subject[0]);
                output.Add(subject[1]);
                output.Add(subject[2]);
                for (int edge = 0; edge < 3; edge++)
                {
                    Vector2 e0 = clip[edge];
                    Vector2 e1 = clip[(edge + 1) % 3];
                    input.Clear();
                    input.AddRange(output);
                    output.Clear();
                    if (input.Count == 0)
                    {
                        return 0f;
                    }

                    Vector2 previous = input[input.Count - 1];
                    float previousSide = Cross(e1 - e0, previous - e0) * clipSign;
                    for (int index = 0; index < input.Count; index++)
                    {
                        Vector2 current = input[index];
                        float currentSide = Cross(e1 - e0, current - e0) * clipSign;
                        if (currentSide >= 0f)
                        {
                            if (previousSide < 0f)
                            {
                                output.Add(Intersect(previous, current, e0, e1));
                            }

                            output.Add(current);
                        }
                        else if (previousSide >= 0f)
                        {
                            output.Add(Intersect(previous, current, e0, e1));
                        }

                        previous = current;
                        previousSide = currentSide;
                    }
                }

                if (output.Count < 3)
                {
                    return 0f;
                }

                float area2 = 0f;
                float cx = 0f;
                float cz = 0f;
                for (int index = 0; index < output.Count; index++)
                {
                    Vector2 p = output[index];
                    Vector2 q = output[(index + 1) % output.Count];
                    float cross = p.x * q.y - q.x * p.y;
                    area2 += cross;
                    cx += (p.x + q.x) * cross;
                    cz += (p.y + q.y) * cross;
                }

                float area = Math.Abs(area2) * 0.5f;
                if (Math.Abs(area2) > 1e-12f)
                {
                    centroid = new Vector2(cx / (3f * area2), cz / (3f * area2));
                }
                else
                {
                    Vector2 sum = Vector2.zero;
                    for (int index = 0; index < output.Count; index++)
                    {
                        sum += output[index];
                    }

                    centroid = sum / output.Count;
                }

                return area;
            }

            private static float Cross(Vector2 a, Vector2 b)
            {
                return a.x * b.y - a.y * b.x;
            }

            private static Vector2 Intersect(Vector2 p, Vector2 q, Vector2 e0, Vector2 e1)
            {
                Vector2 d = q - p;
                Vector2 e = e1 - e0;
                float denominator = Cross(d, e);
                if (Math.Abs(denominator) < 1e-12f)
                {
                    return p;
                }

                float t = Cross(e0 - p, e) / denominator;
                return p + d * Mathf.Clamp01(t);
            }

            /// <summary>Möller–Trumbore segment/triangle intersection.</summary>
            public static bool SegmentHitsTriangle(
                Vector3 p,
                Vector3 q,
                Vector3 a,
                Vector3 b,
                Vector3 c)
            {
                Vector3 d = q - p;
                Vector3 e1 = b - a;
                Vector3 e2 = c - a;
                Vector3 pv = Vector3.Cross(d, e2);
                float determinant = Vector3.Dot(e1, pv);
                if (Math.Abs(determinant) < 1e-12f)
                {
                    return false;
                }

                float inverse = 1f / determinant;
                Vector3 tv = p - a;
                float u = Vector3.Dot(tv, pv) * inverse;
                if (u < 0f || u > 1f)
                {
                    return false;
                }

                Vector3 qv = Vector3.Cross(tv, e1);
                float v = Vector3.Dot(d, qv) * inverse;
                if (v < 0f || u + v > 1f)
                {
                    return false;
                }

                float t = Vector3.Dot(e2, qv) * inverse;
                return t >= 0f && t <= 1f;
            }

            public static bool TrianglesIntersect(
                Vector3 a0,
                Vector3 a1,
                Vector3 a2,
                Vector3 b0,
                Vector3 b1,
                Vector3 b2)
            {
                return SegmentHitsTriangle(a0, a1, b0, b1, b2) ||
                       SegmentHitsTriangle(a1, a2, b0, b1, b2) ||
                       SegmentHitsTriangle(a2, a0, b0, b1, b2) ||
                       SegmentHitsTriangle(b0, b1, a0, a1, a2) ||
                       SegmentHitsTriangle(b1, b2, a0, a1, a2) ||
                       SegmentHitsTriangle(b2, b0, a0, a1, a2);
            }

            public static bool PointInConvexXZ(Vector2 point, Vector2[] polygon)
            {
                bool positive = false;
                bool negative = false;
                for (int index = 0; index < polygon.Length; index++)
                {
                    Vector2 a = polygon[index];
                    Vector2 b = polygon[(index + 1) % polygon.Length];
                    float side = Cross(b - a, point - a);
                    if (side > 1e-6f)
                    {
                        positive = true;
                    }
                    else if (side < -1e-6f)
                    {
                        negative = true;
                    }

                    if (positive && negative)
                    {
                        return false;
                    }
                }

                return true;
            }

            public static Vector2[] OrientedBoxCornersXZ(RuntimeOrientedBox box)
            {
                var corners = new Vector2[4];
                Vector3 hx = box.Rotation * new Vector3(box.Size.x * 0.5f, 0f, 0f);
                Vector3 hz = box.Rotation * new Vector3(0f, 0f, box.Size.z * 0.5f);
                Vector3 c = box.Center;
                Vector3 p0 = c - hx - hz;
                Vector3 p1 = c + hx - hz;
                Vector3 p2 = c + hx + hz;
                Vector3 p3 = c - hx + hz;
                corners[0] = new Vector2(p0.x, p0.z);
                corners[1] = new Vector2(p1.x, p1.z);
                corners[2] = new Vector2(p2.x, p2.z);
                corners[3] = new Vector2(p3.x, p3.z);
                return corners;
            }

            public static bool RectContains(Rect rect, float x, float z)
            {
                return x >= rect.xMin && x <= rect.xMax &&
                       z >= rect.yMin && z <= rect.yMax;
            }

            public static float DistanceToRectPerimeter(Rect rect, float x, float z)
            {
                float dx = Math.Min(Math.Abs(x - rect.xMin), Math.Abs(x - rect.xMax));
                float dz = Math.Min(Math.Abs(z - rect.yMin), Math.Abs(z - rect.yMax));
                if (RectContains(rect, x, z))
                {
                    return Math.Min(dx, dz);
                }

                float ox = x < rect.xMin ? rect.xMin - x : x > rect.xMax ? x - rect.xMax : 0f;
                float oz = z < rect.yMin ? rect.yMin - z : z > rect.yMax ? z - rect.yMax : 0f;
                return (float)Math.Sqrt(ox * ox + oz * oz);
            }
        }

        /// <summary>Axis-aligned XZ accumulator for run and cluster boxes.</summary>
        internal struct BoxXZ
        {
            public float XMin;
            public float XMax;
            public float ZMin;
            public float ZMax;
            public bool Any;

            public void Add(float x, float z)
            {
                if (!Any)
                {
                    XMin = XMax = x;
                    ZMin = ZMax = z;
                    Any = true;
                    return;
                }

                if (x < XMin) XMin = x;
                if (x > XMax) XMax = x;
                if (z < ZMin) ZMin = z;
                if (z > ZMax) ZMax = z;
            }

            public float CenterX => (XMin + XMax) * 0.5f;
            public float CenterZ => (ZMin + ZMax) * 0.5f;
        }
    }
}
