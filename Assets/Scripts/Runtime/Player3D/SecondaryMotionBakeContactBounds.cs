using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Conservative authored boot envelopes; queries transform cached boxes, never skin vertices.</summary>
    internal sealed class SecondaryMotionBakeContactBounds
    {
        private readonly SkinnedMeshRenderer source;
        private readonly Transform foot;
        private readonly Mesh mesh;
        private readonly Transform[] bones;
        private readonly Bounds[] local;
        private readonly bool[] used;
        private readonly bool[] toeShapes;
        private readonly int vertexCount;
        private readonly bool valid;

        internal SecondaryMotionBakeContactBounds(SkinnedMeshRenderer source, Transform shin, Transform foot)
        {
            this.source = source; this.foot = foot;
            if (source == null || shin == null || foot == null || shin == foot) return;
            mesh = source.sharedMesh;
            if (mesh == null || !mesh.isReadable || mesh.vertexCount == 0) return;
            bones = source.bones; local = new Bounds[bones.Length]; used = new bool[bones.Length];
            toeShapes = new bool[mesh.blendShapeCount]; vertexCount = mesh.vertexCount;
            try { valid = Build(shin); }
            catch (UnityException) { valid = false; }
            catch (ArgumentException) { valid = false; }
        }

        private bool Build(Transform shin)
        {
            Vector3[] vertices = mesh.vertices;
            Matrix4x4[] bindposes = mesh.bindposes;
            if (bindposes.Length != bones.Length) return false;
            var toeMin = new Vector3[vertexCount]; var toeMax = new Vector3[vertexCount];
            var ankleMin = new Vector3[vertexCount]; var ankleMax = new Vector3[vertexCount];
            var delta = new Vector3[vertexCount];
            for (int shape = 0; shape < toeShapes.Length; shape++)
            {
                string name = mesh.GetBlendShapeName(shape);
                bool toe = Named(name, "BootToeRoll15") || Named(name, "BootToeRoll30") || Named(name, "BootToeRoll45");
                if ((!toe && !Named(name, "BootAnkleDorsiflex") && !Named(name, "BootAnklePlantarflex")) ||
                    mesh.GetBlendShapeFrameCount(shape) != 1 || mesh.GetBlendShapeFrameWeight(shape, 0) != 100f) return false;
                toeShapes[shape] = toe;
                mesh.GetBlendShapeFrameVertices(shape, 0, delta, null, null);
                Vector3[] minimum = toe ? toeMin : ankleMin, maximum = toe ? toeMax : ankleMax;
                for (int vertex = 0; vertex < vertexCount; vertex++)
                {
                    if (!Finite(delta[vertex])) return false;
                    minimum[vertex] = Vector3.Min(minimum[vertex], delta[vertex]);
                    maximum[vertex] = Vector3.Max(maximum[vertex], delta[vertex]);
                }
            }
            // Each group is a convex blend of its endpoints and the rest shape.
            // Toe and ankle may coexist; their independent interval bounds add.
            using var counts = mesh.GetBonesPerVertex();
            using var weights = mesh.GetAllBoneWeights();
            if (counts.Length != vertexCount) return false;
            int offset = 0;
            for (int vertex = 0; vertex < vertexCount; vertex++)
            {
                if (!Finite(vertices[vertex]) || counts[vertex] == 0 || offset + counts[vertex] > weights.Length) return false;
                float sum = 0f;
                Vector3 minimum = vertices[vertex] + toeMin[vertex] + ankleMin[vertex];
                Vector3 maximum = vertices[vertex] + toeMax[vertex] + ankleMax[vertex];
                for (int influence = 0; influence < counts[vertex]; influence++)
                {
                    BoneWeight1 weight = weights[offset++]; int bone = weight.boneIndex;
                    if (!float.IsFinite(weight.weight) || weight.weight < 0f || bone < 0 || bone >= bones.Length) return false;
                    sum += weight.weight;
                    if (weight.weight == 0f) continue;
                    if (bones[bone] != foot && bones[bone] != shin) return false;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        Vector3 point = bindposes[bone].MultiplyPoint3x4(Corner(minimum, maximum, corner));
                        if (!Finite(point)) return false;
                        if (!used[bone]) { local[bone] = new Bounds(point, Vector3.zero); used[bone] = true; }
                        else local[bone].Encapsulate(point);
                    }
                }
                if (Mathf.Abs(sum - 1f) > .000001f) return false;
            }
            return offset == weights.Length;
        }

        internal bool TryBounds(out Bounds bounds)
        {
            bounds = default;
            if (!valid || source == null || foot == null || mesh == null || source.sharedMesh != mesh || !mesh.isReadable ||
                mesh.vertexCount != vertexCount || mesh.blendShapeCount != toeShapes.Length) return false;
            Transform[] currentBones = source.bones;
            if (currentBones.Length != bones.Length) return false;
            float toes = 0f, ankles = 0f;
            for (int shape = 0; shape < toeShapes.Length; shape++)
            {
                float weight = source.GetBlendShapeWeight(shape);
                if (!float.IsFinite(weight) || weight < 0f || weight > 100f) return false;
                if (toeShapes[shape]) toes += weight; else ankles += weight;
            }
            if (toes > 100.0001f || ankles > 100.0001f) return false;
            Matrix4x4 toFoot = foot.worldToLocalMatrix, toWorld = foot.localToWorldMatrix;
            if (!Finite(toFoot) || !Finite(toWorld) || Mathf.Abs(toWorld.determinant) < 1e-20f) return false;
            Bounds inFoot = default; bool first = true;
            for (int bone = 0; bone < bones.Length; bone++)
            {
                if (!used[bone]) continue;
                if (bones[bone] == null || currentBones[bone] != bones[bone]) return false;
                Matrix4x4 matrix = toFoot * bones[bone].localToWorldMatrix;
                if (!Finite(matrix)) return false;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = matrix.MultiplyPoint3x4(Corner(local[bone].min, local[bone].max, corner));
                    if (!Finite(point)) return false;
                    if (first) { inFoot = new Bounds(point, Vector3.zero); first = false; }
                    else inFoot.Encapsulate(point);
                }
            }
            if (first) return false;
            // The six foot-axis planes bound the actual contact hull, even
            // where its broad-phase fitted ellipsoid extends beyond that hull.
            Vector3 padding = new Vector3(new Vector3(toFoot.m00, toFoot.m01, toFoot.m02).magnitude,
                new Vector3(toFoot.m10, toFoot.m11, toFoot.m12).magnitude,
                new Vector3(toFoot.m20, toFoot.m21, toFoot.m22).magnitude) * .0031f;
            inFoot.extents += padding;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = toWorld.MultiplyPoint3x4(Corner(inFoot.min, inFoot.max, corner));
                if (!Finite(point)) return false;
                if (corner == 0) bounds = new Bounds(point, Vector3.zero); else bounds.Encapsulate(point);
            }
            return true;
        }

        private static bool Named(string name, string stem) => name == stem + ".L" || name == stem + ".R" ||
            name.EndsWith("." + stem + ".L", StringComparison.Ordinal) || name.EndsWith("." + stem + ".R", StringComparison.Ordinal);
        private static Vector3 Corner(Vector3 min, Vector3 max, int i) => new Vector3((i & 1) == 0 ? min.x : max.x,
            (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
        private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
        private static bool Finite(Matrix4x4 m)
        { for (int i = 0; i < 16; i++) if (!float.IsFinite(m[i])) return false; return true; }
    }
}
