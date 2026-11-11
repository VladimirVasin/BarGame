using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    public static partial class RuntimePrimitiveFactory
    {
        private const float ProfileRadius = .125f;
        private static JointProfile structuralJoint;

        internal static void ProfileStructuralBox(GameObject box, float jointRadius, float? tileSize = null)
        {
            Transform pose = box.transform;
            Vector3 size = pose.localScale;
            if (!IsPositiveFinite(size) || !IsPositiveFinite(jointRadius) ||
                (tileSize.HasValue && !IsPositiveFinite(tileSize.Value)))
                throw new ArgumentException("Construction boxes require positive dimensions, radius and UV pitch.");

            JointProfile profile = structuralJoint ??= LoadJointProfile();
            GetJointFit(size, jointRadius, out Vector3 inner, out float radius);
            var vertices = new Vector3[profile.Vertices.Length];
            var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length];
            Vector3 inverseScale = new Vector3(1f / size.x, 1f / size.y, 1f / size.z);
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 point = FitJointPoint(profile.Vertices[i], inner, radius, out Vector3 normal);
                vertices[i] = Vector3.Scale(point, inverseScale);
                normals[i] = Vector3.Scale(normal, size).normalized;
                uv[i] = tileSize.HasValue
                    ? ProjectBoxUv(pose.localPosition + pose.localRotation * point, pose.localRotation * profile.Charts[i]) /
                      tileSize.Value
                    : ProjectBoxUv(vertices[i], profile.Charts[i]) + Vector2.one * .5f;
            }
            SetJointMesh(box, box.GetComponent<MeshFilter>(),
                CreateJointMesh(box.name, vertices, normals, uv, profile.Indices));
        }

        internal static GameObject CreateStructuralBoxes(string name, Transform parent, IReadOnlyList<Bounds> boxes,
            Color color, bool collider, float tileSize, RuntimeWorldUvMode uvMode, float jointRadius)
        {
            var placements = new RuntimeOrientedBox[boxes.Count];
            for (int i = 0; i < boxes.Count; i++)
                placements[i] = new RuntimeOrientedBox(boxes[i].center, Quaternion.identity, boxes[i].size);
            return CreateStructuralBoxes(name, parent, placements, color, collider, tileSize, uvMode, jointRadius);
        }

        internal static GameObject CreateStructuralBoxes(string name, Transform parent,
            IReadOnlyList<RuntimeOrientedBox> boxes, Color color, bool collider, float tileSize,
            RuntimeWorldUvMode uvMode, float jointRadius, Material material = null)
        {
            if (boxes.Count == 0 || !IsPositiveFinite(tileSize) || !IsPositiveFinite(jointRadius))
                throw new ArgumentException("Construction joints require at least one box and positive tile and edge sizes.");

            JointProfile profile = structuralJoint ??= LoadJointProfile();
            int vertexCount = checked(boxes.Count * profile.Vertices.Length);
            var vertices = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var uv = new Vector2[vertexCount];
            var indices = new int[checked(boxes.Count * profile.Indices.Length)];
            var supports = collider ? new Matrix4x4[boxes.Count] : null;
            for (int boxIndex = 0; boxIndex < boxes.Count; boxIndex++)
            {
                RuntimeOrientedBox box = boxes[boxIndex];
                if (!IsPositiveFinite(box.Size) || !IsFinite(box.Center) || !IsFinite(box.Rotation))
                    throw new ArgumentException("Construction boxes require finite transforms and positive dimensions.");
                GetJointFit(box.Size, jointRadius, out Vector3 inner, out float radius);
                int offset = boxIndex * profile.Vertices.Length;
                for (int i = 0; i < profile.Vertices.Length; i++)
                {
                    Vector3 point = FitJointPoint(profile.Vertices[i], inner, radius, out Vector3 normal);
                    Vector3 vertex = box.Center + box.Rotation * point;
                    vertices[offset + i] = vertex;
                    normals[offset + i] = box.Rotation * normal.normalized;
                    uv[offset + i] = uvMode == RuntimeWorldUvMode.BoxProjected
                        ? ProjectBoxUv(vertex, box.Rotation * profile.Charts[i]) / tileSize
                        : new Vector2(vertex.x, vertex.z) / tileSize;
                }

                int first = boxIndex * profile.Indices.Length;
                for (int i = 0; i < profile.Indices.Length; i++)
                    indices[first + i] = offset + profile.Indices[i];
                if (collider)
                    supports[boxIndex] = Matrix4x4.TRS(box.Center, box.Rotation, box.Size);
            }

            GameObject result;
            MeshFilter filter;
            if (collider)
            {
                result = CreateCombinedBoxTransforms(name, parent, supports, color, material, true, null);
                filter = result.GetComponent<MeshFilter>();
            }
            else
            {
                result = CreateMeshHost(name, parent, color, material, true, out filter);
            }
            SetJointMesh(result, filter, CreateJointMesh(name, vertices, normals, uv, indices));
            return result;
        }

        private static void GetJointFit(Vector3 size, float jointRadius, out Vector3 inner, out float radius)
        {
            Vector3 half = size * .5f;
            radius = Mathf.Min(jointRadius, .45f * Mathf.Min(half.x, Mathf.Min(half.y, half.z)));
            inner = half - Vector3.one * radius;
        }

        private static Vector3 FitJointPoint(Vector3 source, Vector3 inner, float radius, out Vector3 normal)
        {
            normal = Vector3.zero;
            Vector3 point = Vector3.zero;
            for (int axis = 0; axis < 3; axis++)
            {
                float radial = Mathf.Clamp01((Mathf.Abs(source[axis]) - (.5f - ProfileRadius)) / ProfileRadius);
                float sign = Mathf.Sign(source[axis]);
                normal[axis] = sign * radial;
                point[axis] = sign * (inner[axis] + radius * radial);
            }
            return point;
        }

        private static Mesh CreateJointMesh(string name, Vector3[] vertices, Vector3[] normals, Vector2[] uv, int[] indices)
        {
            var mesh = new Mesh
            {
                name = name + " Profiled Mesh",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = vertices.Length > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16,
                vertices = vertices,
                normals = normals,
                uv = uv,
                triangles = indices
            };
            mesh.RecalculateTangents();
            return mesh;
        }

        private static void SetJointMesh(GameObject box, MeshFilter filter, Mesh mesh)
        {
            filter.sharedMesh = mesh;
            box.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
            mesh.UploadMeshData(Application.isPlaying);
        }

        private static JointProfile LoadJointProfile()
        {
            const string path = "Environment/ConstructionJointProfile";
            GameObject model = Resources.Load<GameObject>(path);
            MeshFilter filter = model == null ? null : model.GetComponentInChildren<MeshFilter>(true);
            if (filter == null || !filter.sharedMesh.isReadable)
                throw new InvalidOperationException("Missing readable construction profile " + path);
            Mesh mesh = filter.sharedMesh;
            var vertices = mesh.vertices;
            var uv = mesh.uv;
            if (vertices.Length == 0 || vertices.Length != uv.Length)
                throw new InvalidOperationException("Construction profile needs a projection chart.");

            var charts = new Vector3[vertices.Length];
            var bounds = new Bounds(filter.transform.TransformPoint(vertices[0]), Vector3.zero);
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = filter.transform.TransformPoint(vertices[i]);
                bounds.Encapsulate(vertices[i]);
                int axis = Mathf.RoundToInt(uv[i].x * 2f);
                charts[i][axis] = 1f;
            }
            if ((bounds.size - Vector3.one).sqrMagnitude > .000001f || bounds.center.sqrMagnitude > .000001f)
                throw new InvalidOperationException("Construction profile needs a centred one metre envelope.");
            return new JointProfile(vertices, charts, mesh.triangles);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStructuralJoint() => structuralJoint = null;

        private sealed class JointProfile
        {
            internal readonly Vector3[] Vertices;
            internal readonly Vector3[] Charts;
            internal readonly int[] Indices;

            internal JointProfile(Vector3[] vertices, Vector3[] charts, int[] indices)
            {
                Vertices = vertices;
                Charts = charts;
                Indices = indices;
            }
        }
    }
}
