using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal static partial class CityMountainBoundaryMeshFactory
    {
        private const float RockfaceTileLength = 7.5f;
        private const float RockfaceTileHeight = 5.5f;
        private const float RockfaceToeClearance = .12f;
        internal const int MaximumRockfaceChunkVertices = 60000;

        // Imported template data is read once, then reused by every spatial chunk.
        private sealed class RockfaceTemplate
        {
            internal readonly Vector3[] Vertices;
            internal readonly int[] Triangles;

            internal RockfaceTemplate(Mesh mesh)
            {
                Vertices = mesh.vertices;
                Triangles = mesh.triangles;
            }
        }

        private static readonly RockfaceTemplate[] RockfaceTemplates =
            new RockfaceTemplate[CityRockfaceAssetProvider.VariantCount];

        private static void AppendRockface(
            List<Vector3> vertices, List<Vector2> uvs, List<int> triangles,
            CityMountainRidgeStation current, CityMountainRidgeStation next,
            Vector3[] currentCross, Vector3[] nextCross, int band,
            float alongStart, float alongEnd)
        {
            Vector3 lowerA = currentCross[band], upperA = currentCross[band + 1];
            Vector3 lowerB = nextCross[band], upperB = nextCross[band + 1];
            int columns = Mathf.Clamp(Mathf.CeilToInt((alongEnd - alongStart) / RockfaceTileLength), 1, 8);
            float height = Mathf.Max(upperA.y - lowerA.y, upperB.y - lowerB.y);
            int rows = Mathf.Clamp(Mathf.CeilToInt(height / RockfaceTileHeight), 1, 8);
            uint seed = unchecked((uint)(current.Seed ^ band * 7919));
            float[] xs = RockfaceBreaks(columns, seed);
            float[] ys = RockfaceBreaks(rows, seed ^ 0x69ab32u);
            Vector3 inward = -Flatten(current.OutwardNormal + next.OutwardNormal);
            bool reverse = Vector3.Dot(Vector3.Cross(lowerB - lowerA, upperA - lowerA), inward) < 0f;

            for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
            {
                uint hash = RockfaceHash(seed ^ (uint)(row * 1301 + column * 2971));
                int variant = (int)(hash % CityRockfaceAssetProvider.VariantCount);
                RockfaceTemplate template = RockfaceTemplates[variant] ??
                    (RockfaceTemplates[variant] = new RockfaceTemplate(CityRockfaceAssetProvider.GetMesh(variant)));
                bool mirror = (hash & 16u) != 0;
                float strength = .72f + ((hash >> 5) & 255u) / 255f * .25f;
                float amplitude = 1.1f + ((hash >> 13) & 255u) / 255f * 1.6f;
                int firstVertex = vertices.Count;
                foreach (Vector3 source in template.Vertices)
                {
                    float u = Mathf.Lerp(xs[column], xs[column + 1], mirror ? 1f - source.x : source.x);
                    float v = Mathf.Lerp(ys[row], ys[row + 1], source.y);
                    Vector3 position = Vector3.Lerp(
                        Vector3.Lerp(lowerA, upperA, v), Vector3.Lerp(lowerB, upperB, v), u);
                    Vector3 outward = Flatten(Vector3.Lerp(current.OutwardNormal, next.OutwardNormal, u));
                    Vector3 toe = Vector3.Lerp(current.Toe, next.Toe, u);
                    float available = Mathf.Max(0f, Vector3.Dot(position - toe, outward) - RockfaceToeClearance);
                    // The fractured mass stays on the mountain side of the real
                    // toe, so routes, entry collars and map arrivals stay exact.
                    position -= outward * (source.z * Mathf.Min(amplitude, available) * strength);
                    vertices.Add(position);
                    uvs.Add(CreateUv(position, Mathf.Lerp(alongStart, alongEnd, u), CityMountainSurfaceAppearance.MetersPerTile));
                }
                for (int index = 0; index < template.Triangles.Length; index += 3)
                {
                    int a = template.Triangles[index], b = template.Triangles[index + 1], c = template.Triangles[index + 2];
                    triangles.Add(firstVertex + a);
                    triangles.Add(firstVertex + (reverse ^ mirror ? c : b));
                    triangles.Add(firstVertex + (reverse ^ mirror ? b : c));
                }
            }
            if (vertices.Count > MaximumRockfaceChunkVertices)
                throw new InvalidOperationException("Mountain rockface exceeds its spatial mesh budget.");
        }

        private static float[] RockfaceBreaks(int count, uint seed)
        {
            var values = new float[count + 1];
            for (int index = 1; index <= count; index++)
                values[index] = values[index - 1] + .8f + (RockfaceHash(seed ^ (uint)index) & 255u) / 255f * .4f;
            float total = values[count];
            for (int index = 1; index <= count; index++) values[index] /= total;
            return values;
        }

        private static uint RockfaceHash(uint value)
        {
            unchecked
            {
                value ^= value >> 16;
                value *= 0x7feb352du;
                value ^= value >> 15;
                value *= 0x846ca68bu;
                return value ^ (value >> 16);
            }
        }
    }
}
