using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Closes the original seated legs on a floor without moving the seat's pelvis or root.</summary>
    internal sealed class SeatedFootContactSolver
    {
        private const float Clearance = .003f;
        private readonly Leg[] legs = new Leg[2];
        private readonly PlayerWardrobe wardrobe;
        private Vector3 lastFloor, lastNormal;

        private sealed class Surface
        {
            internal SkinnedMeshRenderer Renderer;
            internal Vector3[] Points;
            internal Vector3[][] ShapeDeltas;
            internal float[] Weights;
        }

        private sealed class Leg
        {
            internal Transform Thigh, Shin, Foot;
            internal Vector3 UpLocal;
            internal Surface[] Surfaces;
            internal Vector3 BeforeHip, BeforeKnee, BeforeFoot, Target;
            internal float Minimum, FinalMinimum;
        }

        internal SeatedFootContactSolver(Player3DAssetRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            wardrobe = registry.GetComponent<PlayerWardrobe>();
            PlayerBootDeformation boots = registry.GetComponent<PlayerBootDeformation>();
            for (int side = 0; side < legs.Length; side++)
            {
                bool left = side == 0;
                var leg = legs[side] = new Leg
                {
                    Thigh = Bone(left ? Player3DAnatomicalPart.LeftThigh : Player3DAnatomicalPart.RightThigh),
                    Shin = Bone(left ? Player3DAnatomicalPart.LeftShin : Player3DAnatomicalPart.RightShin),
                    Foot = Bone(left ? Player3DAnatomicalPart.LeftFoot : Player3DAnatomicalPart.RightFoot)
                };
                leg.UpLocal = Quaternion.Inverse(leg.Foot.rotation) * Vector3.up;
                if (boots != null)
                    foreach (PlayerBootDeformation.FootBinding binding in boots.Bindings)
                        if ((int)binding.Side == side) leg.UpLocal = binding.FootUpLocal;
                var surfaces = new List<Surface>(2);
                string suffix = left ? ".L" : ".R";
                foreach (Player3DMeshBinding binding in registry.MeshBindings)
                    if ((binding.MeshName == "CLO_BootSole" + suffix || binding.MeshName == "GEO_Foot" + suffix) &&
                        binding.Renderer is SkinnedMeshRenderer renderer && renderer.sharedMesh != null)
                        surfaces.Add(Capture(renderer, leg.Foot));
                if (surfaces.Count == 0) throw new InvalidOperationException("Seated contact requires the original foot surfaces.");
                leg.Surfaces = surfaces.ToArray();
            }

            Transform Bone(Player3DAnatomicalPart part) => registry.TryGetPart(part, out var binding)
                ? binding.Bone : throw new InvalidOperationException("Seated contact lost " + part);
        }

        private static Surface Capture(SkinnedMeshRenderer renderer, Transform foot)
        {
            Mesh mesh = renderer.sharedMesh;
            int count = mesh.blendShapeCount;
            var result = new Surface { Renderer = renderer, ShapeDeltas = new Vector3[count][], Weights = new float[count] };
            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            Matrix4x4[] bindposes = mesh.bindposes;
            Transform[] bones = renderer.bones;
            if (weights.Length != vertices.Length || bones.Length != bindposes.Length)
                throw new InvalidOperationException("Seated contact requires the original skin binding: " + renderer.name);
            Matrix4x4 toFootMetres = Matrix4x4.TRS(foot.position, foot.rotation, Vector3.one).inverse;
            var skin = new Matrix4x4[bones.Length];
            for (int bone = 0; bone < skin.Length; bone++)
                skin[bone] = toFootMetres * bones[bone].localToWorldMatrix * bindposes[bone];
            // Read the authored skin directly. BakeMesh on this imported 100x
            // hierarchy already includes its unit scale, so a subsequent
            // renderer.TransformPoint would apply that scale a second time.
            result.Points = new Vector3[vertices.Length];
            for (int vertex = 0; vertex < vertices.Length; vertex++)
                result.Points[vertex] = Skin(vertices[vertex], weights[vertex], false);
            for (int shape = 0; shape < count; shape++)
            {
                if (mesh.GetBlendShapeFrameCount(shape) != 1 || Mathf.Abs(mesh.GetBlendShapeFrameWeight(shape, 0) - 100f) > .001f)
                    throw new InvalidOperationException("Seated sole contact requires one authored morph endpoint.");
                var delta = new Vector3[vertices.Length];
                mesh.GetBlendShapeFrameVertices(shape, 0, delta, null, null);
                for (int vertex = 0; vertex < delta.Length; vertex++) delta[vertex] = Skin(delta[vertex], weights[vertex], true);
                result.ShapeDeltas[shape] = delta;
            }
            return result;

            Vector3 Skin(Vector3 point, BoneWeight weight, bool direction)
            {
                return Point(weight.boneIndex0) * weight.weight0 + Point(weight.boneIndex1) * weight.weight1 +
                    Point(weight.boneIndex2) * weight.weight2 + Point(weight.boneIndex3) * weight.weight3;
                Vector3 Point(int index) => direction ? skin[index].MultiplyVector(point) : skin[index].MultiplyPoint3x4(point);
            }
        }

        internal void Apply(Vector3 floorPoint, Vector3 floorNormal, float weight = 1f)
        {
            weight = Mathf.Clamp01(weight);
            if (weight <= 0f || floorNormal.sqrMagnitude < .5f) return;
            floorNormal.Normalize();
            lastFloor = floorPoint; lastNormal = floorNormal;
            foreach (Leg leg in legs)
            {
                leg.BeforeHip = leg.Thigh.position; leg.BeforeKnee = leg.Shin.position; leg.BeforeFoot = leg.Foot.position;
                foreach (Surface surface in leg.Surfaces)
                    for (int shape = 0; shape < surface.Weights.Length; shape++)
                        surface.Weights[shape] = surface.Renderer.GetBlendShapeWeight(shape) * .01f;
                Quaternion rotation = Quaternion.FromToRotation(leg.Foot.TransformDirection(leg.UpLocal), floorNormal) * leg.Foot.rotation;
                float distance = Lowest(leg, rotation, floorPoint, floorNormal);
                if (float.IsPositiveInfinity(distance)) continue;
                Vector3 target = leg.Foot.position + floorNormal * (Clearance - distance);
                leg.Minimum = distance; leg.Target = target;
                LimbTwoBoneIk.Solve(leg.Thigh, leg.Shin, leg.Foot, target, rotation,
                    leg.Shin.position, weight, float.PositiveInfinity, true);
                // A full seated contact closes any bounded IK residual. Entry
                // and exit retain their one authored blend, not repeated weight.
                if (weight < .999f) continue;
                float residual = Clearance - Lowest(leg, leg.Foot.rotation, floorPoint, floorNormal);
                if (residual > .0001f)
                    LimbTwoBoneIk.Solve(leg.Thigh, leg.Shin, leg.Foot, leg.Foot.position + floorNormal * residual,
                        leg.Foot.rotation, leg.Shin.position, 1f, float.PositiveInfinity, true);
                leg.FinalMinimum = Lowest(leg, leg.Foot.rotation, floorPoint, floorNormal);
            }
        }

        internal string Describe()
        {
            string result = $"floor={lastFloor:F5}, normal={lastNormal:F5}";
            for (int i = 0; i < legs.Length; i++)
            {
                Leg leg = legs[i];
                result += $"; leg{i}: before={leg.BeforeHip:F5}/{leg.BeforeKnee:F5}/{leg.BeforeFoot:F5}, " +
                    $"target={leg.Target:F5}, min={leg.Minimum:F5}, finalMin={leg.FinalMinimum:F5}, upLocal={leg.UpLocal:F5}";
                foreach (Surface surface in leg.Surfaces)
                {
                    Bounds points = new Bounds(surface.Points[0], Vector3.zero);
                    foreach (Vector3 point in surface.Points) points.Encapsulate(point);
                    result += $"; {surface.Renderer.name}: worn={wardrobe == null || wardrobe.IsRendererWorn(surface.Renderer)}, " +
                        $"footLocalBounds={points}";
                }
            }
            return result;
        }

        private float Lowest(Leg leg, Quaternion rotation, Vector3 plane, Vector3 normal)
        {
            float minimum = float.PositiveInfinity;
            foreach (Surface surface in leg.Surfaces)
            {
                if (surface.Renderer == null || wardrobe != null && !wardrobe.IsRendererWorn(surface.Renderer)) continue;
                for (int vertex = 0; vertex < surface.Points.Length; vertex++)
                {
                    Vector3 point = surface.Points[vertex];
                    for (int shape = 0; shape < surface.Weights.Length; shape++)
                        point += surface.ShapeDeltas[shape][vertex] * surface.Weights[shape];
                    minimum = Mathf.Min(minimum, Vector3.Dot(leg.Foot.position + rotation * point - plane, normal));
                }
            }
            return minimum;
        }
    }
}
