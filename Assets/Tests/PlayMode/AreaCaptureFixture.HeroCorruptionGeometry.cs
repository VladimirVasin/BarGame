using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [Serializable]
        private sealed class HeroCorruptionFaceGeometryReport
        {
            public int pairedRays, occludedRays, worstFaceTriangle;
            public float minimumFaceToSkullMetres;
            public Vector3 cameraPosition, worstFacePoint;
            public HeroCorruptionNormalReport[] normals;
        }

        [Serializable]
        private sealed class HeroCorruptionNormalReport
        {
            public string mesh;
            public int missingNormals, oppositeGeometryNormals;
            public float minimumNormalLength, minimumDotToGeometry, minimumDotToOriginal;
        }

        private sealed class HeroCorruptionSkinSnapshot
        {
            public Vector3[] Vertices, Normals, OriginalNormals;
            public int[] Triangles;
        }

        private static void AssertHeroCorruptionRenderedFace(Camera camera, Player3DAssetRegistry registry)
        {
            var head = (SkinnedMeshRenderer)registry.MeshBindings.Single(value => value.MeshName == "GEO_Head").Renderer;
            var face = (SkinnedMeshRenderer)registry.MeshBindings.Single(value => value.MeshName == "GEO_FaceSurface").Renderer;
            HeroCorruptionSkinSnapshot skull = SkinHeroCorruptionDiagnostic(head);
            HeroCorruptionSkinSnapshot patch = SkinHeroCorruptionDiagnostic(face);
            Vector3 headAnchor = registry.Anchors.Head.position;
            float maximumAnchorDistance = skull.Vertices.Concat(patch.Vertices)
                .Max(value => Vector3.Distance(value, headAnchor));
            Assert.That(maximumAnchorDistance, Is.LessThan(.5f),
                "GPU source-skin diagnostic scale is invalid: head-anchor distance=" + maximumAnchorDistance +
                "; anchor=" + headAnchor);
            var report = new HeroCorruptionFaceGeometryReport
            {
                cameraPosition = camera.transform.position,
                minimumFaceToSkullMetres = float.MaxValue,
                normals = new[] {
                    MeasureHeroCorruptionNormals(skull, "GEO_Head"),
                    MeasureHeroCorruptionNormals(patch, "GEO_FaceSurface") }
            };
            for (int triangle = 0; triangle < patch.Triangles.Length; triangle += 3)
            {
                Vector3 sample = (patch.Vertices[patch.Triangles[triangle]] + patch.Vertices[patch.Triangles[triangle + 1]] +
                    patch.Vertices[patch.Triangles[triangle + 2]]) / 3f;
                Vector3 delta = sample - report.cameraPosition;
                float distance = delta.magnitude;
                if (distance < .001f) continue;
                Vector3 direction = delta / distance;
                float faceHit = NearestHeroCorruptionTriangle(report.cameraPosition, direction, patch.Vertices, patch.Triangles);
                float skullHit = NearestHeroCorruptionTriangle(report.cameraPosition, direction, skull.Vertices, skull.Triangles);
                if (float.IsPositiveInfinity(faceHit) || float.IsPositiveInfinity(skullHit)) continue;
                report.pairedRays++;
                float clearance = skullHit - faceHit;
                if (clearance < report.minimumFaceToSkullMetres)
                {
                    report.minimumFaceToSkullMetres = clearance;
                    report.worstFaceTriangle = triangle / 3;
                    report.worstFacePoint = report.cameraPosition + direction * faceHit;
                }
                if (clearance < -.0005f) report.occludedRays++;
            }
            if (report.pairedRays == 0) report.minimumFaceToSkullMetres = -1f;
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "HeroAppearance");
            Directory.CreateDirectory(folder);
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Path.Combine(folder, "corruption-rendered-face-geometry.json"), json);
            Debug.Log("HERO_CORRUPTION_RENDERED_FACE_GEOMETRY: " + json);
            Assert.That(report.pairedRays, Is.GreaterThan(0), "The rendered expression surface must expose paired skull rays.");
            Assert.That(report.occludedRays, Is.Zero,
                "The posed Unity skull lies before its expression surface; inspect the rendered geometry report.");
            foreach (HeroCorruptionNormalReport normal in report.normals)
                Assert.That(normal.missingNormals, Is.Zero, normal.mesh + " has invalid skinned shading normals.");
        }

        private static HeroCorruptionSkinSnapshot SkinHeroCorruptionDiagnostic(SkinnedMeshRenderer renderer)
        {
            // Mirror the shader/source-contact skin formula. Native BakeMesh has
            // an additional imported FBX root factor on this rig, so applying a
            // renderer matrix to its output is not a reliable world measurement.
            Mesh mesh = renderer.sharedMesh;
            Vector3[] vertices = mesh.vertices, normals = mesh.normals;
            Vector3[] originalNormals = (Vector3[])normals.Clone();
            var deltaVertices = new Vector3[vertices.Length];
            var deltaNormals = new Vector3[vertices.Length];
            for (int shape = 0; shape < mesh.blendShapeCount; shape++)
            {
                float weight = renderer.GetBlendShapeWeight(shape);
                if (Mathf.Abs(weight) < .00001f) continue;
                int frame = mesh.GetBlendShapeFrameCount(shape) - 1;
                mesh.GetBlendShapeFrameVertices(shape, frame, deltaVertices, deltaNormals, null);
                float amount = weight / Mathf.Max(.00001f, mesh.GetBlendShapeFrameWeight(shape, frame));
                for (int vertex = 0; vertex < vertices.Length; vertex++)
                {
                    vertices[vertex] += deltaVertices[vertex] * amount;
                    normals[vertex] += deltaNormals[vertex] * amount;
                }
            }
            Transform[] bones = renderer.bones;
            Matrix4x4[] bindposes = mesh.bindposes;
            var matrices = new Matrix4x4[bones.Length];
            var normalMatrices = new Matrix4x4[bones.Length];
            for (int bone = 0; bone < bones.Length; bone++)
            {
                matrices[bone] = bones[bone].localToWorldMatrix * bindposes[bone];
                normalMatrices[bone] = matrices[bone].inverse.transpose;
            }
            BoneWeight[] weights = mesh.boneWeights;
            Assert.That(weights.Length, Is.EqualTo(vertices.Length));
            for (int vertex = 0; vertex < vertices.Length; vertex++)
            {
                Vector3 position = Vector3.zero, normal = Vector3.zero, originalNormal = Vector3.zero;
                BoneWeight weight = weights[vertex];
                void Add(int bone, float amount)
                {
                    if (amount <= 0f) return;
                    position += matrices[bone].MultiplyPoint3x4(vertices[vertex]) * amount;
                    normal += normalMatrices[bone].MultiplyVector(normals[vertex]) * amount;
                    originalNormal += normalMatrices[bone].MultiplyVector(originalNormals[vertex]) * amount;
                }
                Add(weight.boneIndex0, weight.weight0); Add(weight.boneIndex1, weight.weight1);
                Add(weight.boneIndex2, weight.weight2); Add(weight.boneIndex3, weight.weight3);
                vertices[vertex] = position;
                normals[vertex] = normal.normalized;
                originalNormals[vertex] = originalNormal.normalized;
            }
            return new HeroCorruptionSkinSnapshot
            { Vertices = vertices, Normals = normals, OriginalNormals = originalNormals, Triangles = mesh.triangles };
        }

        private static HeroCorruptionNormalReport MeasureHeroCorruptionNormals(HeroCorruptionSkinSnapshot posed, string label)
        {
            Vector3[] vertices = posed.Vertices, normals = posed.Normals;
            int[] triangles = posed.Triangles;
            var geometry = new Vector3[vertices.Length];
            for (int index = 0; index < triangles.Length; index += 3)
            {
                int a = triangles[index], b = triangles[index + 1], c = triangles[index + 2];
                Vector3 area = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                geometry[a] += area; geometry[b] += area; geometry[c] += area;
            }
            var report = new HeroCorruptionNormalReport
            {
                mesh = label, minimumNormalLength = float.MaxValue,
                minimumDotToGeometry = 1f, minimumDotToOriginal = 1f
            };
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 normal = index < normals.Length ? normals[index] : Vector3.zero;
                float length = normal.magnitude;
                if (float.IsNaN(length) || float.IsInfinity(length) || length < .01f)
                { report.missingNormals++; continue; }
                report.minimumNormalLength = Mathf.Min(report.minimumNormalLength, length);
                if (geometry[index].sqrMagnitude > 1e-20f)
                {
                    float dot = Vector3.Dot(normal.normalized, geometry[index].normalized);
                    report.minimumDotToGeometry = Mathf.Min(report.minimumDotToGeometry, dot);
                    if (dot < -.1f) report.oppositeGeometryNormals++;
                }
                if (index < posed.OriginalNormals.Length && posed.OriginalNormals[index].sqrMagnitude > 1e-20f)
                    report.minimumDotToOriginal = Mathf.Min(report.minimumDotToOriginal,
                        Vector3.Dot(normal.normalized, posed.OriginalNormals[index].normalized));
            }
            if (report.minimumNormalLength == float.MaxValue) report.minimumNormalLength = 0f;
            return report;
        }

        private static float NearestHeroCorruptionTriangle(Vector3 origin, Vector3 direction,
            Vector3[] vertices, int[] triangles)
        {
            float nearest = float.PositiveInfinity;
            for (int index = 0; index < triangles.Length; index += 3)
            {
                Vector3 a = vertices[triangles[index]], edge1 = vertices[triangles[index + 1]] - a,
                    edge2 = vertices[triangles[index + 2]] - a;
                Vector3 cross = Vector3.Cross(direction, edge2);
                float determinant = Vector3.Dot(edge1, cross);
                if (Mathf.Abs(determinant) < 1e-12f) continue;
                float inverse = 1f / determinant;
                Vector3 offset = origin - a;
                float u = Vector3.Dot(offset, cross) * inverse;
                if (u < -.00001f || u > 1.00001f) continue;
                Vector3 q = Vector3.Cross(offset, edge1);
                float v = Vector3.Dot(direction, q) * inverse;
                if (v < -.00001f || u + v > 1.00001f) continue;
                float distance = Vector3.Dot(edge2, q) * inverse;
                if (distance > .00001f) nearest = Mathf.Min(nearest, distance);
            }
            return nearest;
        }
    }
}
