using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// One finite passive model set for the road's portal and existing structural
    /// slots. No update loop, new material, Light or collider. Original physical
    /// mesh ownership is retained when a visible batch receives authored profiles.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MountainRoadStructureDetails : MonoBehaviour
    {
        public const string ResourcePath = "MountainRoad/MountainRoadStructureDetails3D";
        public const string DesignId = "mountain_road_structure_details_v1";
        public const string PortalName = "Authored Tunnel Portal Stone Joints";
        public const int ExpectedMeshCount = 5, MaximumTemplateVertices = 16000, MaximumBatchParts = 256;
        private readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>(StringComparer.Ordinal);
        public int ReplacedBatchCount { get; private set; }
        public int ReplacedPartCount { get; private set; }
        public int SharedMeshCount => meshes.Count;

        private static MountainRoadStructureDetails Scope(Transform parent)
        {
            var scope = parent.GetComponentInParent<MountainRoadStructureDetails>();
            if (scope == null) scope = parent.gameObject.AddComponent<MountainRoadStructureDetails>();
            scope.Load();
            return scope;
        }

        private void Load()
        {
            if (meshes.Count != 0) return;
            GameObject source = Resources.Load<GameObject>(ResourcePath);
            if (source == null) throw new InvalidOperationException("Missing generated road structure details.");
            int vertices = 0;
            try
            {
                foreach (MeshFilter filter in source.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!filter.name.StartsWith("GEO_RoadStructure_", StringComparison.Ordinal))
                        throw new InvalidOperationException("Unexpected road structure detail " + filter.name);
                    Mesh mesh = MountainCablewayCraftedGeometry.BakeImportedMesh(filter);
                    meshes.Add(filter.name.Substring("GEO_RoadStructure_".Length), mesh);
                    vertices += mesh.vertexCount;
                }
                if (meshes.Count != ExpectedMeshCount || vertices > MaximumTemplateVertices)
                    throw new InvalidOperationException("Road structure detail resource budget changed.");
                foreach (var pair in meshes)
                {
                    if (pair.Key == "Portal") continue;
                    if (Vector3.Distance(pair.Value.bounds.center, Vector3.zero) > .002f ||
                        Vector3.Distance(pair.Value.bounds.size, Vector3.one) > .002f)
                        throw new InvalidOperationException("Road structure detail changed its metre slot " + pair.Key);
                }
            }
            catch { Release(); throw; }
        }

        public static void CreatePortal(Transform parent, MountainRoadTunnelDescriptor tunnel)
        {
            if (Mathf.Abs(tunnel.OpeningWidth - 8f) > .001f || Mathf.Abs(tunnel.OpeningHeight - 5.5f) > .001f)
                throw new InvalidOperationException("Authored portal details require the existing 8 by 5.5 metre opening.");
            var scope = Scope(parent);
            var placements = new[] { new RuntimeMeshPlacement(scope.meshes["Portal"],
                tunnel.PortalGroundCenter, Quaternion.LookRotation(tunnel.OutwardAxis, Vector3.up)) };
            Color tint = new Color(.115f, .13f, .12f, 1f);
            float tile = MountainRoadSurfaceAppearance.GetRecipe(MountainRoadSurfaceKind.LayeredStone).MetersPerTile;
            GameObject portal = RuntimePrimitiveFactory.CreateCombinedMeshes(PortalName, parent, placements,
                tint, false, tile, RuntimeWorldUvMode.BoxProjected);
            MountainRoadSurfaceAppearance.ApplyCombined(portal.GetComponent<Renderer>(), MountainRoadSurfaceKind.LayeredStone, tint);
        }

        public static void ReplaceGirders(GameObject batch, IReadOnlyList<RuntimeOrientedBox> boxes, float tile)
        {
            var scope = Scope(batch.transform);
            var roles = new string[boxes.Count];
            for (int i = 0; i < roles.Length; i++) roles[i] = boxes[i].Size.x > boxes[i].Size.z ? "GirderX" : "GirderZ";
            scope.ReplaceBatch(batch, boxes, roles, tile);
        }

        public static void ReplaceCoping(GameObject batch, IReadOnlyList<RuntimeOrientedBox> boxes,
            IReadOnlyList<bool> coping, float tile)
        {
            bool any = false;
            for (int i = 0; i < coping.Count; i++) any |= coping[i];
            if (!any) return;
            var scope = Scope(batch.transform);
            var roles = new string[boxes.Count];
            for (int i = 0; i < roles.Length; i++) roles[i] = coping[i] ? "Coping" : "Plain";
            scope.ReplaceBatch(batch, boxes, roles, tile);
        }

        private void ReplaceBatch(GameObject batch, IReadOnlyList<RuntimeOrientedBox> boxes,
            IReadOnlyList<string> roles, float tile)
        {
            if (boxes.Count == 0 || boxes.Count > MaximumBatchParts || roles.Count != boxes.Count)
                throw new InvalidOperationException("Road structural batch exceeds its bounded part budget.");
            var combine = new CombineInstance[boxes.Count];
            int vertices = 0;
            for (int i = 0; i < boxes.Count; i++)
            {
                Mesh source = meshes[roles[i]];
                vertices += source.vertexCount;
                combine[i] = new CombineInstance { mesh = source,
                    transform = Matrix4x4.TRS(boxes[i].Center, boxes[i].Rotation, boxes[i].Size) };
            }
            var result = new Mesh { name = batch.name + " Authored Profiles", hideFlags = HideFlags.DontSave,
                indexFormat = vertices > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            result.CombineMeshes(combine, true, true, false);
            Vector3[] positions = result.vertices, normals = result.normals;
            var uv = new Vector2[positions.Length];
            for (int i = 0; i < positions.Length; i++) uv[i] = RuntimePrimitiveFactory.ProjectBoxUv(positions[i], normals[i]) / tile;
            result.uv = uv;
            result.RecalculateBounds();
            MeshFilter filter = batch.GetComponent<MeshFilter>();
            Mesh previous = filter.sharedMesh;
            filter.sharedMesh = result;
            // A physical batch's old box mesh remains the exact collision
            // surface, with its original owner. Only its renderer changes.
            bool retainedCollision = batch.TryGetComponent(out MeshCollider collider) && collider.sharedMesh == previous;
            if (retainedCollision) batch.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(result);
            else
            {
                batch.GetComponent<RuntimeGeneratedMeshOwner>().Initialize(result);
                if (Application.isPlaying) Destroy(previous); else DestroyImmediate(previous);
            }
            result.UploadMeshData(true);
            ReplacedBatchCount++;
            ReplacedPartCount += boxes.Count;
        }

        private void OnDestroy() => Release();
        private void Release()
        {
            foreach (Mesh mesh in meshes.Values)
                if (mesh != null)
                {
                    if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
                }
            meshes.Clear();
        }
    }
}
