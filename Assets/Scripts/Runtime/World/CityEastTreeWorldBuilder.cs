using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Existing Blender trees retain their rigid shared metre meshes and collide only at their trunks.</summary>
    public static class CityEastTreeWorldBuilder
    {
        public const string RootName = "Eastern Fence Trees";

        internal static Transform Build(Transform parent, CityEastExitPlan exit, CityEastLitterWorldBuilder.SampleGround sample)
        {
            CityEastTreePlan plan = CityEastTreePlan.Create(exit);
            CityMiscAssetProvider provider = CityMiscAssetProvider.LoadOrThrow();
            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);
            foreach (CityEastTreePart tree in plan.Parts)
            {
                Vector2 point = new Vector2(tree.Position.x, tree.Position.z);
                if (!sample(point, out float height))
                    throw new InvalidOperationException("Eastern tree left its ground support: " + tree.Id);
                // Embed the small root collar to the lowest supporting edge.
                // A rigid trunk stays upright; its roots never hover over a slope.
                for (int edge = 0; edge < 8; edge++)
                {
                    float angle = edge * Mathf.PI * .25f;
                    Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (.34f * tree.Scale);
                    if (sample(point + offset, out float support)) height = Mathf.Min(height, support);
                }
                Transform placement = new GameObject(tree.Id).transform;
                placement.SetParent(root, false);
                placement.SetPositionAndRotation(new Vector3(point.x, height - .015f, point.y), tree.Rotation);
                placement.localScale = Vector3.one * tree.Scale;
                Bounds bounds = default;
                int count = CityMiscAssetProvider.GetPartCount(CityMiscKind.ParkTree);
                for (int i = 0; i < count; i++)
                {
                    CityMiscMeshPart part = provider.GetPartOrThrow(CityMiscKind.ParkTree, tree.Variant, i);
                    if (i == 0) bounds = part.Mesh.bounds;
                    else bounds.Encapsulate(part.Mesh.bounds);
                    Transform meshRoot = new GameObject(part.Component).transform;
                    meshRoot.SetParent(placement, false);
                    meshRoot.gameObject.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
                    MeshRenderer renderer = meshRoot.gameObject.AddComponent<MeshRenderer>();
                    bool bark = part.Role == CityMiscMeshRole.Bark;
                    if (!bark && part.Role != CityMiscMeshRole.Foliage)
                        throw new InvalidOperationException("Unexpected eastern tree material role: " + part.Role);
                    CityParkSurfaceAppearance.ApplyCombined(renderer,
                        bark ? CityParkSurfaceKind.Bark : CityParkSurfaceKind.Foliage,
                        bark ? new Color(.20f, .15f, .10f) : new Color(.18f, .25f, .14f));
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                }
                if (Mathf.Abs(bounds.size.y * tree.Scale - tree.Height) > .005f || Mathf.Abs(bounds.min.y) > .005f)
                    throw new InvalidOperationException("Imported eastern tree metre bounds changed: " + tree.Id);
                BoxCollider trunk = placement.gameObject.AddComponent<BoxCollider>();
                trunk.center = new Vector3(0f, 1.15f, 0f);
                trunk.size = new Vector3(.68f, 2.3f, .68f);
            }
            GameLog.Debug("city", "east_trees_built", GameLog.Field("trees", plan.Parts.Count));
            return root;
        }
    }
}
