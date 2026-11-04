using UnityEngine;

namespace BarPromenade
{
    // Contacts are measured from the resident's equipped soles, including
    // stature and the FBX unit scale. A fixed wardrobe is baked once per body.
    internal sealed class CityPedestrianShoeContacts
    {
        private readonly CityPedestrianAssetRegistry registry;
        private readonly Quaternion[] shoeRotation = new Quaternion[2];
        private readonly SkinnedMeshRenderer[] measured = new SkinnedMeshRenderer[2];
        private readonly float[] ankleHeight = new float[2];

        internal CityPedestrianShoeContacts(CityPedestrianAssetRegistry registry, Quaternion facing)
        {
            this.registry = registry;
            shoeRotation[0] = Quaternion.Inverse(facing) * registry.LeftFoot.rotation;
            shoeRotation[1] = Quaternion.Inverse(facing) * registry.RightFoot.rotation;
        }

        internal Quaternion Rotation(int side) => shoeRotation[side];
        internal float Height(int side) => ankleHeight[side];

        internal bool Measure(bool force = false)
        {
            for (int side = 0; side < 2; side++)
            {
                string suffix = side == 0 ? "Sole.L" : "Sole.R";
                SkinnedMeshRenderer worn = null;
                foreach (var renderer in registry.Renderers)
                    if (renderer is SkinnedMeshRenderer skin && skin.enabled && skin.gameObject.activeInHierarchy &&
                        skin.name.EndsWith(suffix, System.StringComparison.Ordinal)) { worn = skin; break; }
                if (worn == null) return false;
                if (!force && worn == measured[side]) continue;
                Transform foot = side == 0 ? registry.LeftFoot : registry.RightFoot;
                Vector3 normal = foot.rotation * (Quaternion.Inverse(shoeRotation[side]) * Vector3.up);
                float lowest = float.PositiveInfinity;
                foreach (Vector3 vertex in SkinWorldVertices(worn))
                    lowest = Mathf.Min(lowest, Vector3.Dot(vertex - foot.position, normal));
                if (float.IsPositiveInfinity(lowest)) return false;
                measured[side] = worn;
                ankleHeight[side] = .002f - lowest;
            }
            return true;
        }

        // Used only for shoe/wardrobe changes. Skin directly into world metres:
        // an imported renderer has both an FBX unit factor and a resident scale.
        internal static Vector3[] SkinWorldVertices(SkinnedMeshRenderer skin)
        {
            Mesh mesh = skin.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Vector3[] deltas = null;
            for (int shape = 0; shape < mesh.blendShapeCount; shape++)
            {
                float weight = skin.GetBlendShapeWeight(shape);
                if (Mathf.Abs(weight) < .00001f) continue;
                int last = mesh.GetBlendShapeFrameCount(shape) - 1;
                deltas ??= new Vector3[vertices.Length];
                mesh.GetBlendShapeFrameVertices(shape, last, deltas, null, null);
                float ratio = weight / mesh.GetBlendShapeFrameWeight(shape, last);
                for (int i = 0; i < vertices.Length; i++) vertices[i] += deltas[i] * ratio;
            }
            var bones = skin.bones;
            var matrices = mesh.bindposes;
            for (int i = 0; i < bones.Length; i++) matrices[i] = bones[i].localToWorldMatrix * matrices[i];
            var weights = mesh.boneWeights;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = vertices[i];
                var w = weights[i];
                vertices[i] = matrices[w.boneIndex0].MultiplyPoint3x4(p) * w.weight0 +
                    matrices[w.boneIndex1].MultiplyPoint3x4(p) * w.weight1 +
                    matrices[w.boneIndex2].MultiplyPoint3x4(p) * w.weight2 +
                    matrices[w.boneIndex3].MultiplyPoint3x4(p) * w.weight3;
            }
            return vertices;
        }
    }
}
