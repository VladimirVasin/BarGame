using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Scene-local palette, copied from existing compressed texture mips.
    /// Source sheets stay authoritative; no CPU readback or per-edge material.</summary>
    internal sealed class CityGroundJointPalette : IDisposable
    {
        internal const int Size = 256, Layers = 32;
        private const int Mips = 9;
        private readonly Dictionary<Texture2D, int> indices = new Dictionary<Texture2D, int>();
        internal Texture2DArray Color { get; private set; }
        internal Texture2DArray Response { get; private set; }

        internal static bool Supported => SystemInfo.graphicsShaderLevel >= 45 &&
            SystemInfo.supports2DArrayTextures && SystemInfo.SupportsTextureFormat(TextureFormat.BC7) &&
            (SystemInfo.copyTextureSupport & (CopyTextureSupport.Basic | CopyTextureSupport.DifferentTypes)) ==
            (CopyTextureSupport.Basic | CopyTextureSupport.DifferentTypes);

        internal int Add(Texture2D color, Texture2D response)
        {
            if (indices.TryGetValue(color, out int existing)) return existing;
            if (!CanCopy(color) || !CanCopy(response) || indices.Count == Layers) return -1;
            Color ??= Allocate(false, "City ground joints: pigment");
            Response ??= Allocate(true, "City ground joints: response");
            int layer = indices.Count;
            Copy(color, Color, layer);
            Copy(response, Response, layer);
            indices.Add(color, layer);
            return layer;
        }

        private static bool CanCopy(Texture2D texture) => texture != null &&
            texture.format == TextureFormat.BC7 && texture.width == texture.height &&
            texture.width >= Size && Mathf.IsPowerOfTwo(texture.width) &&
            texture.activeMipmapLimit <= Offset(texture) &&
            texture.mipmapCount >= Offset(texture) + Mips;

        private static int Offset(Texture2D texture) => Mathf.RoundToInt(Mathf.Log(texture.width / Size, 2f));

        private static Texture2DArray Allocate(bool linear, string name)
        {
            var texture = new Texture2DArray(Size, Size, Layers, TextureFormat.BC7, true, linear)
            {
                name = name, hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4
            };
            // Retain only the GPU copy. Apply must precede the GPU upload.
            texture.Apply(false, true);
            return texture;
        }

        private static void Copy(Texture2D source, Texture2DArray target, int layer)
        {
            int first = Offset(source);
            for (int mip = 0; mip < Mips; mip++)
                Graphics.CopyTexture(source, 0, first + mip, target, layer, mip);
        }

        public void Dispose()
        {
            Release(Color); Release(Response);
            Color = Response = null;
            indices.Clear();
        }

        internal static void Release(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }

    /// <summary>Immutable spatial bins of shared, height-aware coating contacts.
    /// Both sides of a joint sample the same field. A field belongs to one
    /// static composition or one locally rebuilt ground region.</summary>
    internal sealed class CityGroundJointField : IDisposable
    {
        private const int MaximumVectors = 1 << 20;
        private const int MaximumCellCandidates = 512;
        private static readonly int GridId = Shader.PropertyToID("_CityJointGrid");
        private static readonly int DataId = Shader.PropertyToID("_CityJointData");
        private static readonly int ColorId = Shader.PropertyToID("_CityJointColor");
        private static readonly int ResponseId = Shader.PropertyToID("_CityJointResponse");
        private static readonly int CellPitchId = Shader.PropertyToID("_CityJointCellsPerMetre");
        private GraphicsBuffer data;
        private Vector4 grid;
        private float cellsPerMetre;

        internal readonly struct Recipe : IEquatable<Recipe>
        {
            internal Recipe(Vector4 u, Vector4 v, Vector4 dry, Vector4 wet, Vector4 physics)
            { U = u; V = v; Dry = dry; Wet = wet; Physics = physics; }
            internal readonly Vector4 U, V, Dry, Wet, Physics;
            public bool Equals(Recipe other) => U.Equals(other.U) && V.Equals(other.V) &&
                Dry.Equals(other.Dry) && Wet.Equals(other.Wet) && Physics.Equals(other.Physics);
            public override bool Equals(object obj) => obj is Recipe other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(U, V, Dry, Wet, Physics);
        }

        internal readonly struct Edge
        {
            internal Edge(Vector3 a, Vector3 b, float width, Recipe first, Recipe second, Vector2 slopeA, Vector2 slopeB)
            { A = a; B = b; Width = width; First = first; Second = second;
                Slopes = new Vector4(slopeA.x, slopeA.y, slopeB.x, slopeB.y); }
            internal readonly Vector3 A, B;
            internal readonly float Width;
            internal readonly Recipe First, Second;
            internal readonly Vector4 Slopes;
        }

        internal int MaximumCandidates { get; private set; }
        internal int Bytes => data == null ? 0 : data.count * data.stride;

        internal bool Build(IReadOnlyList<Edge> edges) => edges.Count > 0 &&
            (TryBuild(edges, 2f) || TryBuild(edges, 1f));

        private bool TryBuild(IReadOnlyList<Edge> edges, float cellSize)
        {
            MaximumCandidates = 0;
            var bins = new Dictionary<Vector2Int, List<int>>();
            var entries = new List<Vector4>();
            var recipes = new Dictionary<Recipe, int>();
            Vector2Int min = new Vector2Int(int.MaxValue, int.MaxValue);
            Vector2Int max = new Vector2Int(int.MinValue, int.MinValue);
            int references = 0;
            for (int i = 0; i < edges.Count; i++)
            {
                Edge e = edges[i];
                int a = AddRecipe(e.First), b = AddRecipe(e.Second);
                int record = entries.Count;
                entries.Add(new Vector4(e.A.x, e.A.y, e.A.z, e.Width));
                entries.Add(new Vector4(e.B.x, e.B.y, e.B.z, a));
                entries.Add(new Vector4(b, Vector3.Distance(e.A, e.B), 0f, 0f));
                entries.Add(e.Slopes);
                int x0 = Cell(Mathf.Min(e.A.x, e.B.x) - e.Width), x1 = Cell(Mathf.Max(e.A.x, e.B.x) + e.Width);
                int z0 = Cell(Mathf.Min(e.A.z, e.B.z) - e.Width), z1 = Cell(Mathf.Max(e.A.z, e.B.z) + e.Width);
                Vector2 start = new Vector2(e.A.x, e.A.z);
                Vector2 span = new Vector2(e.B.x - e.A.x, e.B.z - e.A.z);
                float inverseLength = 1f / span.sqrMagnitude;
                float reach = e.Width + .708f * cellSize;
                for (int x = x0; x <= x1; x++)
                    for (int z = z0; z <= z1; z++)
                    {
                        // Reject distant cells in a diagonal edge's bounding
                        // rectangle. A cell's half diagonal is a safe margin.
                        Vector2 delta = new Vector2((x + .5f) * cellSize, (z + .5f) * cellSize) - start;
                        float t = Mathf.Clamp01(Vector2.Dot(delta, span) * inverseLength);
                        if ((delta - span * t).sqrMagnitude > reach * reach) continue;
                        var cell = new Vector2Int(x, z);
                        if (!bins.TryGetValue(cell, out List<int> bin)) bins.Add(cell, bin = new List<int>());
                        bin.Add(record);
                        if (++references + entries.Count > MaximumVectors || bin.Count > MaximumCellCandidates)
                            return false;
                        min = Vector2Int.Min(min, cell);
                        max = Vector2Int.Max(max, cell);
                        MaximumCandidates = Mathf.Max(MaximumCandidates, bin.Count);
                    }
            }

            int columns = max.x - min.x + 1, rows = max.y - min.y + 1;
            long headerCount = (long)columns * rows;
            int packedReferences = 0;
            foreach (List<int> bin in bins.Values) packedReferences += (bin.Count + 3) / 4;
            if (headerCount + packedReferences + entries.Count > MaximumVectors) return false;
            var upload = new Vector4[(int)headerCount + packedReferences + entries.Count];
            int cursor = (int)headerCount;
            int payload = cursor + packedReferences;
            entries.CopyTo(upload, payload);
            for (int z = min.y; z <= max.y; z++)
                for (int x = min.x; x <= max.x; x++)
                {
                    if (!bins.TryGetValue(new Vector2Int(x, z), out List<int> bin)) continue;
                    upload[(z - min.y) * columns + x - min.x] = new Vector4(cursor, bin.Count, payload, 0f);
                    for (int i = 0; i < bin.Count; i++)
                        upload[cursor + i / 4][i & 3] = bin[i];
                    cursor += (bin.Count + 3) / 4;
                }
            grid = new Vector4(min.x, min.y, columns, rows);
            cellsPerMetre = 1f / cellSize;
            data = new GraphicsBuffer(GraphicsBuffer.Target.Structured, upload.Length, 4 * sizeof(float));
            try { data.SetData(upload); }
            catch { Dispose(); throw; }
            return true;

            int Cell(float coordinate) => Mathf.FloorToInt(coordinate / cellSize);

            int AddRecipe(Recipe recipe)
            {
                if (recipes.TryGetValue(recipe, out int address)) return address;
                recipes.Add(recipe, address = entries.Count);
                entries.Add(recipe.U); entries.Add(recipe.V);
                entries.Add(recipe.Dry); entries.Add(recipe.Wet); entries.Add(recipe.Physics);
                return address;
            }
        }

        internal void Bind(MaterialPropertyBlock properties, CityGroundJointPalette palette)
        {
            properties.SetBuffer(DataId, data);
            properties.SetVector(GridId, grid);
            properties.SetTexture(ColorId, palette.Color);
            properties.SetTexture(ResponseId, palette.Response);
            properties.SetFloat(CellPitchId, cellsPerMetre);
        }

        public void Dispose()
        {
            data?.Dispose();
            data = null;
        }
    }
}
