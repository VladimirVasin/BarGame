using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    // Continue the surveyed terrain skin into its stair opening. The concrete
    // floor and steel feet keep their own geometry and finishes.
    internal sealed class CityHillsideStairCut
    {
        internal const string ObjectName = "Hillside stair soil cuts";
        private const float LipBevel = .03f;
        private const float MinimumDepth = .08f;
        private readonly List<(Vector3 First, Vector3 Last, float Bottom, Vector3? BackFirst, Vector3? BackLast)> spans = new();
        private readonly Dictionary<Vector3, Vector3> lipOffsets = new();
        internal void AddCell(int col, int row, int columns, int rows,
            Func<int, int, Vector3> vertex, Func<int, int, bool> isCut, CityHillsideShortcut stair)
        {
            float bottom = stair.FoundationBottom;
            // Follow only cut-to-earth boundaries. The open street mouths
            // and the seams between adjoining flights and landings stay clear.
            if (col > 0 && !isCut(col - 1, row))
                Add(vertex(col, row), vertex(col, row + 1), bottom);
            if (col + 2 < columns && !isCut(col + 1, row))
                Add(vertex(col + 1, row + 1), vertex(col + 1, row), bottom);
            if (row > 0 && !isCut(col, row - 1) && !AtStreet(vertex(col, row).z))
                Add(vertex(col + 1, row), vertex(col, row), bottom);
            if (row + 2 < rows && !isCut(col, row + 1) && !AtStreet(vertex(col, row + 1).z))
                Add(vertex(col, row + 1), vertex(col + 1, row + 1), bottom);

            bool AtStreet(float z) => Mathf.Abs(z - stair.RouteWorld[0].z) < .001f ||
                Mathf.Abs(z - stair.RouteWorld[stair.RouteWorld.Count - 1].z) < .001f;
        }

        internal void Add(Vector3 first, Vector3 last, float bottom, Vector3? backFirst = null, Vector3? backLast = null)
        {
            Vector3 inward = Vector3.Cross(last - first, Vector3.down);
            if (inward.sqrMagnitude < 1e-8f) return;
            inward.Normalize();
            spans.Add((first, last, bottom, backFirst, backLast));
            lipOffsets.TryGetValue(first, out Vector3 start);
            lipOffsets[first] = start + inward;
            lipOffsets.TryGetValue(last, out Vector3 end);
            lipOffsets[last] = end + inward;
        }

        internal void Build(Transform parent, bool passive, Color? tint = null)
        {
            if (spans.Count == 0) return;
            var vertices = new List<Vector3>(spans.Count * 6);
            var normals = new List<Vector3>(vertices.Capacity);
            var uv = new List<Vector2>(vertices.Capacity);
            var indices = new List<int>(spans.Count * 12);
            foreach (var (first, last, bottom, backFirst, backLast) in spans)
            {
                Vector3 inward = Vector3.Cross(last - first, Vector3.down).normalized;
                Vector3 along = Vector3.Cross(inward, Vector3.down);
                int start = vertices.Count;
                bool hasBacking = backFirst.HasValue && backLast.HasValue;
                int rows = hasBacking ? 4 : 3;
                AddEnd(first, backFirst);
                AddEnd(last, backLast);
                for (int row = 0; row + 1 < rows; row++)
                {
                    int a = start + row, b = a + rows;
                    indices.Add(a); indices.Add(b); indices.Add(a + 1);
                    indices.Add(a + 1); indices.Add(b); indices.Add(b + 1);
                }

                void AddEnd(Vector3 top, Vector3? back)
                {
                    if (hasBacking) AddVertex(back.Value, Vector3.up);
                    // Both faces of a corner share this bevel endpoint.
                    Vector3 lip = top + lipOffsets[top].normalized * LipBevel - Vector3.up * LipBevel;
                    Vector3 foot = new(lip.x, Mathf.Min(bottom, top.y - MinimumDepth), lip.z);
                    AddVertex(top, Vector3.up);
                    AddVertex(lip, inward);
                    AddVertex(foot, inward);
                }

                void AddVertex(Vector3 point, Vector3 normal)
                {
                    vertices.Add(point);
                    normals.Add(normal);
                    Vector2 texture = hasBacking && normal == Vector3.up
                        ? new Vector2(point.x, point.z) : new Vector2(Vector3.Dot(point, along), point.y);
                    uv.Add(texture / CityExteriorAppearance.GroundTextureTileSize);
                }
            }

            var mesh = new Mesh { name = ObjectName };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateTangents();
            var bank = new GameObject(ObjectName);
            bank.transform.SetParent(parent, false);
            bank.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = bank.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            CityExteriorAppearance.ApplyGroundSurface(renderer, tint ?? CityHillsidePerimeterSurface.ResidentialSoil);
            bank.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
            if (!passive) bank.AddComponent<MeshCollider>().sharedMesh = mesh;
        }
    }
}
