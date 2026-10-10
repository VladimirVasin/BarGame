using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BarPromenade.Editor
{
    public static partial class Player3DV2AssetSetup
    {
        private static void ValidateJointSurfaceManifest(Player3DV2Manifest manifest)
        {
            CharacterJointSurfaceManifest joints = manifest.joint_surfaces;
            if (joints == null || joints.contract != "character_joint_surfaces_v1" ||
                joints.source_space != "blender_z_up_minus_y_forward" || joints.max_influences != 4 ||
                joints.surfaces == null || joints.surfaces.Length == 0 || joints.seams == null || joints.seams.Length == 0)
                throw new InvalidOperationException("Hero requires declared continuous joint surfaces and four-influence skinning.");
            var parts = manifest.parts.ToDictionary(part => part.name, StringComparer.Ordinal);
            var surfaces = new HashSet<string>(StringComparer.Ordinal);
            foreach (CharacterJointSkinSurface surface in joints.surfaces)
                if (surface == null || !surfaces.Add(surface.name) || !parts.ContainsKey(surface.name) ||
                    surface.bones == null || surface.bones.Length == 0 ||
                    surface.bones.Any(string.IsNullOrWhiteSpace) || surface.bones.Distinct().Count() != surface.bones.Length)
                    throw new InvalidOperationException("Joint surfaces must retain named parts and unique allowed bone influences.");
            var seams = new HashSet<string>(StringComparer.Ordinal);
            foreach (CharacterJointSeam seam in joints.seams)
            {
                if (seam == null || string.IsNullOrWhiteSpace(seam.id) || !seams.Add(seam.id) ||
                    string.IsNullOrWhiteSpace(seam.bone) || seam.renderers == null || seam.renderers.Length != 2 ||
                    (seam.renderers[0] == seam.renderers[1] && seam.topology != "continuous") ||
                    seam.renderers.Any(name => !surfaces.Contains(name)) ||
                    seam.points_blender == null || seam.points_blender.Length < 6 ||
                    seam.points_blender.Any(point => !float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z)))
                    throw new InvalidOperationException("Every joint seam needs two independent named surfaces and its measured boundary ring.");
                if (!string.IsNullOrEmpty(seam.corrective_shape) &&
                    (seam.corrective_angle_degrees <= 90f || seam.corrective_angle_degrees >= 180f))
                    throw new InvalidOperationException("Joint corrections must declare their deep-flex angle.");
            }
        }

        private static void ValidateJointSurfaceSkinning(Renderer renderer, CharacterJointSkinSurface surface)
        {
            if (!(renderer is SkinnedMeshRenderer skin) || skin.sharedMesh == null ||
                skin.sharedMesh.boneWeights.Length != skin.sharedMesh.vertexCount || skin.sharedMesh.bindposes.Length != skin.bones.Length)
                throw new InvalidOperationException("Joint surface lost its imported skin: " + surface.name);
            var allowed = new HashSet<string>(surface.bones, StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (BoneWeight weight in skin.sharedMesh.boneWeights)
            {
                float sum = 0f;
                void Include(int index, float amount)
                {
                    if (!float.IsFinite(amount) || amount < 0f)
                        throw new InvalidOperationException("Joint surface has an invalid skin weight: " + surface.name);
                    sum += amount;
                    if (amount == 0f) return;
                    if ((uint)index >= skin.bones.Length || skin.bones[index] == null || !allowed.Contains(skin.bones[index].name))
                        throw new InvalidOperationException("Joint surface uses an undeclared bone influence: " + surface.name);
                    used.Add(skin.bones[index].name);
                }
                Include(weight.boneIndex0, weight.weight0); Include(weight.boneIndex1, weight.weight1);
                Include(weight.boneIndex2, weight.weight2); Include(weight.boneIndex3, weight.weight3);
                if (Mathf.Abs(sum - 1f) > .0001f)
                    throw new InvalidOperationException("Joint surface weights must remain normalized: " + surface.name);
            }
            if (!used.SetEquals(allowed))
                throw new InvalidOperationException("Joint surface lost one of its declared skin influences: " + surface.name);
            skin.quality = SkinQuality.Bone4;
        }

        private static void ValidateImportedJointSeams(Transform root, CharacterJointSurfaceManifest joints,
            IReadOnlyDictionary<string, Renderer> renderers)
        {
            const float positionTolerance = .00005f;
            foreach (CharacterJointSeam seam in joints.seams)
            {
                var first = (SkinnedMeshRenderer)renderers[seam.renderers[0]];
                var second = (SkinnedMeshRenderer)renderers[seam.renderers[1]];
                Vector3[] firstVertices = first.sharedMesh.vertices, secondVertices = second.sharedMesh.vertices;
                Vector3[] firstNormals = first.sharedMesh.normals, secondNormals = second.sharedMesh.normals;
                BoneWeight[] firstWeights = first.sharedMesh.boneWeights, secondWeights = second.sharedMesh.boneWeights;
                foreach (Vector3 sourcePoint in seam.points_blender)
                {
                    Vector3 expected = root.TransformPoint(new Vector3(-sourcePoint.x, sourcePoint.z, -sourcePoint.y));
                    List<int> firstMatches = MatchJointPoint(first, firstVertices, expected, positionTolerance);
                    List<int> secondMatches = MatchJointPoint(second, secondVertices, expected, positionTolerance);
                    if (firstMatches.Count == 0 || secondMatches.Count == 0)
                        throw new InvalidOperationException("Imported joint seam has a missing boundary vertex: " + seam.id);
                    float bestNormalDot = -1f;
                    foreach (int a in firstMatches)
                        foreach (int b in secondMatches)
                        {
                            ValidateSeamWeights(first, firstWeights[a], second, secondWeights[b], seam.id);
                            Vector3 normalA = first.transform.localToWorldMatrix.inverse.transpose.MultiplyVector(firstNormals[a]).normalized;
                            Vector3 normalB = second.transform.localToWorldMatrix.inverse.transpose.MultiplyVector(secondNormals[b]).normalized;
                            bestNormalDot = Mathf.Max(bestNormalDot, Vector3.Dot(normalA, normalB));
                        }
                    if (bestNormalDot < .9995f)
                        throw new InvalidOperationException("Imported joint seam lost its shared surface normals: " + seam.id);
                }
                if (!string.IsNullOrEmpty(seam.corrective_shape) &&
                    (CharacterJointDeformation.FindShape(first.sharedMesh, seam.corrective_shape) < 0 ||
                     CharacterJointDeformation.FindShape(second.sharedMesh, seam.corrective_shape) < 0))
                    throw new InvalidOperationException("Joint seam lost its authored deep-flex correction: " + seam.id);
            }
        }

        private static List<int> MatchJointPoint(SkinnedMeshRenderer renderer, Vector3[] vertices, Vector3 expected, float tolerance)
        {
            var matches = new List<int>();
            for (int index = 0; index < vertices.Length; index++)
                if ((renderer.transform.TransformPoint(vertices[index]) - expected).sqrMagnitude <= tolerance * tolerance) matches.Add(index);
            return matches;
        }

        private static void ValidateSeamWeights(SkinnedMeshRenderer first, BoneWeight a, SkinnedMeshRenderer second, BoneWeight b, string seam)
        {
            Dictionary<string, float> Weights(SkinnedMeshRenderer skin, BoneWeight weight)
            {
                var result = new Dictionary<string, float>(StringComparer.Ordinal);
                void Add(int index, float amount) { if (amount > 0f) result[skin.bones[index].name] = amount; }
                Add(weight.boneIndex0, weight.weight0); Add(weight.boneIndex1, weight.weight1);
                Add(weight.boneIndex2, weight.weight2); Add(weight.boneIndex3, weight.weight3);
                return result;
            }
            Dictionary<string, float> left = Weights(first, a), right = Weights(second, b);
            if (left.Count != right.Count || left.Any(pair => !right.TryGetValue(pair.Key, out float amount) || Mathf.Abs(pair.Value - amount) > .0001f))
                throw new InvalidOperationException("Joint boundary vertices must share their skin field: " + seam);
        }

        [Serializable] private sealed class CharacterJointSurfaceManifest
        {
            public string contract, source_space;
            public int max_influences;
            public CharacterJointSkinSurface[] surfaces;
            public CharacterJointSeam[] seams;
        }
        [Serializable] private sealed class CharacterJointSkinSurface { public string name; public string[] bones; }
        [Serializable] private sealed class CharacterJointSeam
        {
            public string id, bone, corrective_shape, topology;
            public string[] renderers;
            public Vector3[] points_blender;
            public float corrective_angle_degrees;
        }
    }
}
