using System;
using System.Collections.Generic;
using BarPromenade.Rendering;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityLightingPresentation
    {
        private const int MaximumExteriorGroundFaces = 131072, MaximumExteriorStructure = 2048;
        private const int MaximumExteriorGroundReferences = 524288;
        private const float GroundCellSize = 4f;
        private readonly List<ExteriorGroundFace> exteriorGround = new List<ExteriorGroundFace>();
        private readonly Dictionary<Vector2Int, List<int>> exteriorGroundCells = new Dictionary<Vector2Int, List<int>>();
        private readonly List<Bounds> exteriorStructure = new List<Bounds>();
        private Vector4 exteriorBoundary;
        private bool exteriorSupportValid;

        private readonly struct ExteriorGroundFace
        {
            internal readonly Vector3 A, B, C, Normal;
            internal readonly Color Albedo;
            internal ExteriorGroundFace(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Color albedo)
            { A = a; B = b; C = c; Normal = normal; Albedo = albedo; }
        }

        public int ExteriorGroundFaceCount => exteriorGround.Count;
        public int ExteriorStructureCount => exteriorStructure.Count;
        public bool HasExteriorSupport => Root == null && exteriorSupportValid;

        private void CacheExteriorSupport()
        {
            Vector3 normal = sourceRoot.right;
            Vector3 boundaryPoint = sourceRoot.TransformPoint(new Vector3(HomeExteriorViewBuilder.ExteriorMinimumX, 0f, 0f));
            exteriorBoundary = new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(normal, boundaryPoint));
            exteriorSupportValid = true;
            // These are the same authored metre-space solids the City uses for
            // collision. Home retains the registry but deliberately adds no colliders.
            foreach (CityBuildingAssetRegistry registry in sourceRoot.GetComponentsInChildren<CityBuildingAssetRegistry>(true))
                foreach (Bounds local in registry.ColliderBounds)
                    if (!AddExteriorStructure(TransformExteriorBounds(registry.transform, local))) return;
            foreach (Renderer renderer in sourceRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || renderer.GetComponentInParent<CityBuildingAssetRegistry>(true) != null ||
                    renderer.sharedMaterial == null || renderer.sharedMaterial.renderQueue >= 2500) continue;
                string name = renderer.name;
                if (!(name.Contains("Wall") || name.Contains("Facade") || name.Contains("Foundation") ||
                    name.Contains("Building Mass") || name.Contains("Roof") || name.Contains("Shell"))) continue;
                Bounds bounds = renderer.bounds;
                if (bounds.size.y < .45f || bounds.size.x < .04f || bounds.size.z < .04f) continue;
                if (!AddExteriorStructure(bounds)) return;
            }
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var indices = new List<int>();
            int references = 0;
            foreach (MeshFilter filter in sourceRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.name.StartsWith("Ground sector ", StringComparison.Ordinal) ||
                    !filter.TryGetComponent(out Renderer renderer) || !renderer.enabled) continue;
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable) { exteriorSupportValid = false; return; }
                vertices.Clear(); normals.Clear(); mesh.GetVertices(vertices); mesh.GetNormals(normals);
                Material[] materials = renderer.sharedMaterials;
                for (int slot = 0; slot < mesh.subMeshCount; slot++)
                {
                    Material material = slot < materials.Length ? materials[slot] : null;
                    properties.Clear(); renderer.GetPropertyBlock(properties, slot);
                    if (properties.isEmpty) renderer.GetPropertyBlock(properties);
                    Color tint = properties.HasColor(BaseColorId) ? properties.GetColor(BaseColorId) :
                        material != null && material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId) : new Color(.23f, .245f, .225f);
                    indices.Clear(); mesh.GetTriangles(indices, slot);
                    for (int i = 0; i + 2 < indices.Count; i += 3)
                    {
                        int ia = indices[i], ib = indices[i + 1], ic = indices[i + 2];
                        Vector3 a = filter.transform.TransformPoint(vertices[ia]);
                        Vector3 b = filter.transform.TransformPoint(vertices[ib]);
                        Vector3 c = filter.transform.TransformPoint(vertices[ic]);
                        Vector3 faceNormal = Vector3.Cross(b - a, c - a).normalized;
                        Vector3 authoredNormal = normals.Count == vertices.Count ?
                            filter.transform.TransformDirection(normals[ia] + normals[ib] + normals[ic]).normalized : faceNormal;
                        if (authoredNormal.y < .85f || faceNormal.sqrMagnitude < .5f) continue;
                        if (Vector3.Dot(faceNormal, authoredNormal) < 0f) { Vector3 swap = b; b = c; c = swap; faceNormal = -faceNormal; }
                        if (exteriorGround.Count >= MaximumExteriorGroundFaces) { exteriorSupportValid = false; return; }
                        int index = exteriorGround.Count;
                        exteriorGround.Add(new ExteriorGroundFace(a, b, c, faceNormal, tint.linear));
                        Vector2Int min = GroundCell(Vector3.Min(a, Vector3.Min(b, c)));
                        Vector2Int max = GroundCell(Vector3.Max(a, Vector3.Max(b, c)));
                        for (int z = min.y; z <= max.y; z++) for (int x = min.x; x <= max.x; x++)
                        {
                            if (++references > MaximumExteriorGroundReferences) { exteriorSupportValid = false; return; }
                            var cell = new Vector2Int(x, z);
                            if (!exteriorGroundCells.TryGetValue(cell, out List<int> entries))
                                exteriorGroundCells.Add(cell, entries = new List<int>());
                            entries.Add(index);
                        }
                    }
                }
            }
            exteriorSupportValid = exteriorGround.Count > 0;
        }

        private bool AddExteriorStructure(Bounds bounds)
        {
            if (exteriorStructure.Count >= MaximumExteriorStructure) { exteriorSupportValid = false; return false; }
            exteriorStructure.Add(bounds); return true;
        }

        private static Bounds TransformExteriorBounds(Transform owner, Bounds local)
        {
            var result = new Bounds(owner.TransformPoint(local.center), Vector3.zero);
            for (int corner = 0; corner < 8; corner++)
                result.Encapsulate(owner.TransformPoint(local.center + Vector3.Scale(local.extents,
                    new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f))));
            return result;
        }

        private static Vector2Int GroundCell(Vector3 point) =>
            new Vector2Int(Mathf.FloorToInt(point.x / GroundCellSize), Mathf.FloorToInt(point.z / GroundCellSize));

        private void ConfigureExteriorSupport(Practical entry)
        {
            entry.Planes[0] = exteriorBoundary;
            if (!exteriorSupportValid) { entry.SupportValid = false; return; }
            int count = 0;
            foreach (Bounds bounds in exteriorStructure)
            {
                if (!bounds.Intersects(entry.Bounds) || bounds.Contains(entry.SupportPosition)) continue;
                if (count == entry.Blockers.Length) { entry.SupportValid = false; return; }
                entry.Blockers[count++] = bounds;
            }
            entry.SupportValid = true;
        }

        private bool TryExteriorGroundRay(Vector3 origin, Vector3 direction, float reach,
            out Vector3 point, out ExteriorGroundFace ground)
        {
            point = default; ground = default;
            if (!exteriorSupportValid || direction.y >= -.001f) return false;
            Vector3 end = origin + direction * reach;
            Vector2Int min = GroundCell(Vector3.Min(origin, end)), max = GroundCell(Vector3.Max(origin, end));
            float closest = reach + .0001f; bool found = false;
            // A patch ray is at most fifteen metres. Its finite rectangle touches
            // only nearby preindexed cells; no mesh reads or hierarchy scans occur here.
            for (int z = min.y; z <= max.y; z++) for (int x = min.x; x <= max.x; x++)
            {
                if (!exteriorGroundCells.TryGetValue(new Vector2Int(x, z), out List<int> entries)) continue;
                foreach (int index in entries)
                {
                    ExteriorGroundFace face = exteriorGround[index];
                    float denominator = Vector3.Dot(face.Normal, direction);
                    if (denominator >= -.00001f) continue;
                    float distance = Vector3.Dot(face.Normal, face.A - origin) / denominator;
                    if (distance < 0f || distance >= closest) continue;
                    Vector3 candidate = origin + direction * distance;
                    if (Vector3.Dot(Vector3.Cross(face.B - face.A, candidate - face.A), face.Normal) < -.0001f ||
                        Vector3.Dot(Vector3.Cross(face.C - face.B, candidate - face.B), face.Normal) < -.0001f ||
                        Vector3.Dot(Vector3.Cross(face.A - face.C, candidate - face.C), face.Normal) < -.0001f) continue;
                    closest = distance; point = candidate; ground = face; found = true;
                }
            }
            return found;
        }

        private bool ExteriorSourceReaches(Light light, Vector3 point)
        {
            if (!IsLit(light) || !SourceConeContains(light, point) ||
                Vector3.Dot(new Vector3(exteriorBoundary.x, exteriorBoundary.y, exteriorBoundary.z), point) + exteriorBoundary.w < 0f) return false;
            Vector3 delta = point - light.transform.position; float distance = delta.magnitude;
            if (distance < .12f) return false;
            var ray = new Ray(light.transform.position, delta / distance);
            foreach (Bounds bounds in exteriorStructure)
                if (!bounds.Contains(ray.origin) && bounds.IntersectRay(ray, out float enter) && enter < distance - .05f) return false;
            return true;
        }

        private void TryAddExteriorPatch(Practical entry)
        {
            Light light = entry.Light; Vector3 source = light.transform.position;
            Vector3 direction = light.type == LightType.Spot && light.transform.forward.y < -.12f ? light.transform.forward : Vector3.down;
            if (!TryExteriorGroundRay(source + direction * .1f, direction, Mathf.Min(light.range, 15f), out Vector3 hit,
                    out ExteriorGroundFace ground)) return;
            Vector3 center = hit + ground.Normal * .035f;
            if (!ExteriorSourceReaches(light, center)) return;
            float radius = 1.1f;
            for (int pass = 0; pass < 3; pass++)
            {
                bool continuous = true;
                for (int side = 0; side < 4; side++)
                {
                    Vector3 offset = new Vector3((side & 1) == 0 ? -radius : radius, 0f, (side & 2) == 0 ? -radius : radius);
                    if (!TryExteriorGroundRay(center + offset + Vector3.up * .3f, Vector3.down, .6f,
                            out Vector3 corner, out _) || Mathf.Abs(corner.y - hit.y) > .15f) continuous = false;
                }
                if (continuous) break;
                radius *= .6f; if (pass == 2) return;
            }
            float reach = radius * 2.1f;
            Bounds receivers = new Bounds(center + Vector3.up * .85f, new Vector3(reach * 2f, 1.8f, reach * 2f));
            Vector3 min = receivers.min, max = receivers.max;
            for (int side = 0; side < 4; side++)
            {
                Vector3 along = side == 0 ? Vector3.right : side == 1 ? Vector3.left : side == 2 ? Vector3.forward : Vector3.back;
                var ray = new Ray(center + Vector3.up * .2f, along); float nearest = reach;
                foreach (Bounds solid in exteriorStructure)
                    if (!solid.Contains(ray.origin) && solid.IntersectRay(ray, out float distance) && distance < nearest) nearest = distance;
                Vector3 wall = ray.GetPoint(nearest);
                if (side == 0) max.x = Mathf.Min(max.x, wall.x + .01f);
                else if (side == 1) min.x = Mathf.Max(min.x, wall.x - .01f);
                else if (side == 2) max.z = Mathf.Min(max.z, wall.z + .01f);
                else min.z = Mathf.Max(min.z, wall.z - .01f);
            }
            // Home reconstruction is axis aligned in its scene. Clamp the
            // finite bounce receiver on the same exterior side as its air.
            Vector3 planeNormal = new Vector3(exteriorBoundary.x, exteriorBoundary.y, exteriorBoundary.z);
            if (planeNormal.x > .999f) min.x = Mathf.Max(min.x, -exteriorBoundary.w);
            else if (planeNormal.x < -.999f) max.x = Mathf.Min(max.x, exteriorBoundary.w);
            else if (planeNormal.z > .999f) min.z = Mathf.Max(min.z, -exteriorBoundary.w);
            else if (planeNormal.z < -.999f) max.z = Mathf.Min(max.z, exteriorBoundary.w);
            else return; // An unsupported rotated context fails closed.
            if (max.x <= min.x || max.z <= min.z) return;
            receivers.SetMinMax(min, max);
            int index = BounceRegionCount++;
            patches[index] = new Patch { Light = light, Center = center, Normal = ground.Normal, Bounds = receivers,
                Radius = radius, SourcePosition = source, Albedo = ground.Albedo, Visibility = 1f };
            centers[index] = new Vector4(center.x, center.y, center.z, reach);
            minima[index] = min; maxima[index] = max; normals[index] = ground.Normal;
        }
    }
}
