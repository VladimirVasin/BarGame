using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Materializes the close-range part of one planned mountain ridge.
    /// The geometry is deliberately faceted: every triangle owns its
    /// vertices and therefore its normal, which keeps the mountain readable
    /// after the low-resolution PS1 composite.
    /// </summary>
    internal static class CityMountainBoundaryMeshFactory
    {
        private const float MaximumChunkLength = 48f;
        private const float FootInset = 0.35f;
        private const float ShoulderDepthRatio = 0.20f;
        private const float CrestDepthRatio = 0.52f;
        private const float ShoulderHeightRatio = 0.34f;
        private const float BackHeightRatio = 0f;

        internal static GameObject CreateRidge(
            Transform parent,
            CityMountainRidgeDescriptor ridge)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (ridge == null)
            {
                throw new ArgumentNullException(nameof(ridge));
            }

            if (ridge.Stations.Count < 2)
            {
                throw new ArgumentException(
                    "A mountain ridge needs at least two stations.",
                    nameof(ridge));
            }

            var root = new GameObject(ridge.StableId);
            root.transform.SetParent(parent, false);

            float[] distances = CreateStationDistances(ridge.Stations);
            int first = 0;
            int chunkIndex = 0;
            while (first < ridge.Stations.Count - 1)
            {
                int last = first + 1;
                while (last + 1 < ridge.Stations.Count &&
                       distances[last + 1] - distances[first] <=
                       MaximumChunkLength)
                {
                    last++;
                }

                CreateChunk(
                    root.transform,
                    ridge,
                    first,
                    last,
                    distances,
                    chunkIndex);
                first = last;
                chunkIndex++;
            }

            return root;
        }

        internal static GameObject CreatePortalFrame(
            Transform parent,
            CityMountainTunnelDescriptor tunnel)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            Vector3 axis = Flatten(tunnel.Axis);
            Vector3 right = Vector3.Cross(Vector3.up, axis).normalized;
            float innerRadius = tunnel.OpeningWidth * 0.5f;
            float springY = tunnel.OpeningHeight - innerRadius;
            const float ringThickness = 1.15f;
            const float frameDepth = 1.25f;
            const int arcSegments = 8;

            var inner = new List<Vector2>(arcSegments + 3)
            {
                new Vector2(-innerRadius, 0f),
                new Vector2(-innerRadius, springY)
            };
            var outer = new List<Vector2>(arcSegments + 3)
            {
                new Vector2(-innerRadius - ringThickness, 0f),
                new Vector2(-innerRadius - ringThickness, springY)
            };
            for (int index = 1; index <= arcSegments; index++)
            {
                float angle = Mathf.Lerp(
                    Mathf.PI,
                    0f,
                    index / (float)arcSegments);
                inner.Add(new Vector2(
                    Mathf.Cos(angle) * innerRadius,
                    springY + Mathf.Sin(angle) * innerRadius));
                outer.Add(new Vector2(
                    Mathf.Cos(angle) *
                    (innerRadius + ringThickness),
                    springY + Mathf.Sin(angle) *
                    (innerRadius + ringThickness)));
            }

            inner.Add(new Vector2(innerRadius, 0f));
            outer.Add(new Vector2(innerRadius + ringThickness, 0f));

            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            Vector3 frontOffset = -axis * (frameDepth * 0.5f);
            Vector3 backOffset = axis * (frameDepth * 0.5f);
            Vector3 desiredFront = -axis;
            Vector3 desiredBack = axis;
            for (int index = 0; index < inner.Count - 1; index++)
            {
                Vector3 innerA = ToWorld(
                    tunnel.PortalGroundCenter,
                    right,
                    inner[index]);
                Vector3 innerB = ToWorld(
                    tunnel.PortalGroundCenter,
                    right,
                    inner[index + 1]);
                Vector3 outerA = ToWorld(
                    tunnel.PortalGroundCenter,
                    right,
                    outer[index]);
                Vector3 outerB = ToWorld(
                    tunnel.PortalGroundCenter,
                    right,
                    outer[index + 1]);

                AddQuad(
                    vertices,
                    uvs,
                    triangles,
                    innerA + frontOffset,
                    innerB + frontOffset,
                    outerA + frontOffset,
                    outerB + frontOffset,
                    desiredFront);
                AddQuad(
                    vertices,
                    uvs,
                    triangles,
                    innerA + backOffset,
                    outerA + backOffset,
                    innerB + backOffset,
                    outerB + backOffset,
                    desiredBack);
                AddQuad(
                    vertices,
                    uvs,
                    triangles,
                    outerA + frontOffset,
                    outerA + backOffset,
                    outerB + frontOffset,
                    outerB + backOffset,
                    (outerA - tunnel.PortalGroundCenter).normalized);
                AddQuad(
                    vertices,
                    uvs,
                    triangles,
                    innerA + frontOffset,
                    innerB + frontOffset,
                    innerA + backOffset,
                    innerB + backOffset,
                    (tunnel.PortalGroundCenter - innerA).normalized);
            }

            return CreateMeshObject(
                "Mountain Tunnel Portal",
                parent,
                vertices,
                uvs,
                triangles,
                CityMountainBoundaryWorldBuilder.MidRock,
                true,
                false);
        }

        private static void CreateChunk(
            Transform parent,
            CityMountainRidgeDescriptor ridge,
            int first,
            int last,
            IReadOnlyList<float> distances,
            int chunkIndex)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            for (int stationIndex = first;
                 stationIndex < last;
                 stationIndex++)
            {
                CityMountainRidgeStation current =
                    ridge.Stations[stationIndex];
                CityMountainRidgeStation next =
                    ridge.Stations[stationIndex + 1];
                Vector3[] currentCross = CreateCrossSection(current);
                Vector3[] nextCross = CreateCrossSection(next);
                Vector3 inward = -Flatten(
                    current.OutwardNormal + next.OutwardNormal);
                for (int band = 0; band < currentCross.Length - 1; band++)
                {
                    Vector3 faceDirection =
                        band < currentCross.Length - 2
                            ? inward
                            : -inward;
                    Vector3 desired =
                        (faceDirection * 0.72f +
                         Vector3.up * 0.28f).normalized;
                    AddQuad(
                        vertices,
                        uvs,
                        triangles,
                        currentCross[band],
                        currentCross[band + 1],
                        nextCross[band],
                        nextCross[band + 1],
                        desired,
                        distances[stationIndex],
                        distances[stationIndex + 1]);
                }
            }

            AddEndCap(
                vertices,
                uvs,
                triangles,
                CreateCrossSection(ridge.Stations[first]),
                ridge.Stations[first].WorldXZ -
                ridge.Stations[first + 1].WorldXZ);
            AddEndCap(
                vertices,
                uvs,
                triangles,
                CreateCrossSection(ridge.Stations[last]),
                ridge.Stations[last].WorldXZ -
                ridge.Stations[last - 1].WorldXZ);

            Color tint = CreateChunkTint(
                ridge.Stations[first].Seed,
                chunkIndex);
            CreateMeshObject(
                $"{ridge.StableId} Chunk {chunkIndex + 1}",
                parent,
                vertices,
                uvs,
                triangles,
                tint,
                false,
                true);
            CreateToeCollider(
                parent,
                ridge,
                first,
                last,
                chunkIndex);
        }

        private static void CreateToeCollider(
            Transform parent,
            CityMountainRidgeDescriptor ridge,
            int first,
            int last,
            int chunkIndex)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (int index = first; index < last; index++)
            {
                CityMountainRidgeStation current = ridge.Stations[index];
                CityMountainRidgeStation next = ridge.Stations[index + 1];
                Vector3[] currentCross = CreateCrossSection(current);
                Vector3[] nextCross = CreateCrossSection(next);
                Vector3 inward = -Flatten(
                    current.OutwardNormal + next.OutwardNormal);
                AddQuad(
                    vertices,
                    uvs,
                    triangles,
                    currentCross[0],
                    currentCross[1],
                    nextCross[0],
                    nextCross[1],
                    (inward * 0.72f +
                     Vector3.up * 0.28f).normalized);
            }

            var mesh = new Mesh
            {
                name = $"{ridge.StableId} Toe Collider Mesh",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = vertices.Count > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();

            var result = new GameObject(
                $"{ridge.StableId} Toe Collider {chunkIndex + 1}");
            result.transform.SetParent(parent, false);
            result.AddComponent<MeshCollider>().sharedMesh = mesh;
            result.AddComponent<RuntimeGeneratedMeshOwner>()
                .Initialize(mesh);
            mesh.UploadMeshData(false);
        }

        private static GameObject CreateMeshObject(
            string name,
            Transform parent,
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            Color tint,
            bool collider,
            bool usePhysicalRidgeMaterial)
        {
            var mesh = new Mesh
            {
                name = $"{name} Mesh",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = vertices.Count > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var result = new GameObject(name);
            result.transform.SetParent(parent, false);
            result.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = result.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            if (usePhysicalRidgeMaterial)
            {
                CityMountainSurfaceAppearance.ApplyPhysicalRidge(
                    renderer,
                    tint);
            }
            else
            {
                CityMountainSurfaceAppearance.ApplyCombined(renderer, tint);
            }
            if (collider)
            {
                result.AddComponent<MeshCollider>().sharedMesh = mesh;
            }

            result.AddComponent<RuntimeGeneratedMeshOwner>()
                .Initialize(mesh);
            mesh.UploadMeshData(!collider);
            return result;
        }

        private static Vector3[] CreateCrossSection(
            CityMountainRidgeStation station)
        {
            Vector3 outward = Flatten(station.OutwardNormal);
            Vector3 anchor = new Vector3(
                station.WorldXZ.x,
                station.BaseY,
                station.WorldXZ.y);
            float height = Mathf.Max(1f, station.PeakY - station.BaseY);
            return new[]
            {
                anchor + outward * FootInset + Vector3.down * 0.12f,
                anchor + outward *
                (station.Depth * ShoulderDepthRatio) +
                Vector3.up * (height * ShoulderHeightRatio),
                anchor + outward *
                (station.Depth * CrestDepthRatio) +
                Vector3.up * height,
                anchor + outward * station.Depth +
                Vector3.up * (height * BackHeightRatio)
            };
        }

        private static void AddEndCap(
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            IReadOnlyList<Vector3> cross,
            Vector2 direction)
        {
            Vector3 desired = new Vector3(
                direction.x,
                0f,
                direction.y).normalized;
            AddTriangle(
                vertices,
                uvs,
                triangles,
                cross[0],
                cross[1],
                cross[2],
                desired);
            AddTriangle(
                vertices,
                uvs,
                triangles,
                cross[0],
                cross[2],
                cross[3],
                desired);
        }

        private static void AddQuad(
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 d,
            Vector3 desiredNormal,
            float? uA = null,
            float? uB = null)
        {
            AddTriangle(
                vertices,
                uvs,
                triangles,
                a,
                b,
                c,
                desiredNormal,
                uA,
                uA,
                uB);
            AddTriangle(
                vertices,
                uvs,
                triangles,
                c,
                b,
                d,
                desiredNormal,
                uB,
                uA,
                uB);
        }

        private static void AddTriangle(
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 desiredNormal,
            float? alongA = null,
            float? alongB = null,
            float? alongC = null)
        {
            if (Vector3.Dot(
                    Vector3.Cross(b - a, c - a),
                    desiredNormal) < 0f)
            {
                (b, c) = (c, b);
                (alongB, alongC) = (alongC, alongB);
            }

            int start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            float pitch = CityMountainSurfaceAppearance.MetersPerTile;
            uvs.Add(CreateUv(a, alongA ?? a.x + a.z, pitch));
            uvs.Add(CreateUv(b, alongB ?? b.x + b.z, pitch));
            uvs.Add(CreateUv(c, alongC ?? c.x + c.z, pitch));
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
        }

        private static Vector2 CreateUv(
            Vector3 point,
            float along,
            float pitch)
        {
            return new Vector2(along / pitch, point.y / pitch);
        }

        private static float[] CreateStationDistances(
            IReadOnlyList<CityMountainRidgeStation> stations)
        {
            var result = new float[stations.Count];
            for (int index = 1; index < stations.Count; index++)
            {
                result[index] = result[index - 1] + Vector2.Distance(
                    stations[index - 1].WorldXZ,
                    stations[index].WorldXZ);
            }

            return result;
        }

        private static Color CreateChunkTint(int seed, int chunkIndex)
        {
            unchecked
            {
                uint hash = (uint)(seed * 397 ^ chunkIndex * 7919);
                hash ^= hash >> 16;
                float amount = (hash & 255u) / 255f;
                return amount < 0.5f
                    ? Color.Lerp(
                        CityMountainBoundaryWorldBuilder.ForeRock,
                        CityMountainBoundaryWorldBuilder.MidRock,
                        amount * 2f)
                    : Color.Lerp(
                        CityMountainBoundaryWorldBuilder.MidRock,
                        CityMountainBoundaryWorldBuilder.HighRock,
                        (amount - 0.5f) * 2f);
            }
        }

        private static Vector3 ToWorld(
            Vector3 origin,
            Vector3 right,
            Vector2 point)
        {
            return origin + right * point.x + Vector3.up * point.y;
        }

        private static Vector3 Flatten(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                throw new ArgumentException(
                    "A mountain direction must have an XZ component.",
                    nameof(direction));
            }

            return direction.normalized;
        }
    }
}
