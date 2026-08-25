using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    public static class MountainRoadSurfaceMeshFactory
    {
        public const float SurfaceThickness = 0.18f;
        public const float MetersPerTile = 3.5f;
        public const float TerminalApronSurfaceOffset = 0.025f;
        public const float TerminalApronEntryOverlap = 0.45f;

        private const int TerminalApronArcSegments = 28;

        private readonly struct Row
        {
            internal Row(Vector3 center, Vector3 right, float width, float distance)
            {
                Center = center;
                Right = right.normalized;
                Width = width;
                Distance = distance;
            }

            internal Vector3 Center { get; }
            internal Vector3 Right { get; }
            internal float Width { get; }
            internal float Distance { get; }
            internal Vector3 Left => Center - Right * (Width * 0.5f);
            internal Vector3 RightEdge => Center + Right * (Width * 0.5f);
        }

        private readonly struct RibbonConnection
        {
            internal RibbonConnection(
                int leftTop,
                int rightTop,
                int leftBottom,
                int rightBottom)
            {
                LeftTop = leftTop;
                RightTop = rightTop;
                LeftBottom = leftBottom;
                RightBottom = rightBottom;
            }

            internal int LeftTop { get; }
            internal int RightTop { get; }
            internal int LeftBottom { get; }
            internal int RightBottom { get; }
        }

        public static Mesh Create(MountainRoadPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            MountainRoadValidator.ValidateOrThrow(plan);
            List<Row> rows = CreateRows(plan);
            var vertices = new List<Vector3>(rows.Count * 6 + 32);
            var uvs = new List<Vector2>(rows.Count * 6 + 32);
            var triangles = new List<int>(rows.Count * 18 + 96);
            RibbonConnection connection = AppendRibbon(
                rows,
                vertices,
                uvs,
                triangles);
            AppendPlateau(
                plan.Plateau,
                connection,
                vertices,
                uvs,
                triangles);
            return CreateMesh("Mountain Road Surface", vertices, uvs, triangles);
        }

        public static Mesh CreateTerminalApron(
            MountainRoadVehicleApronPlan apron)
        {
            if (apron == null)
            {
                throw new ArgumentNullException(nameof(apron));
            }

            float halfWidth = apron.EntryWidth * 0.5f;
            float chordForward = -Mathf.Sqrt(
                apron.TurningRadius * apron.TurningRadius -
                halfWidth * halfWidth);
            float startAngle = Mathf.Atan2(chordForward, -halfWidth);
            float endAngle = Mathf.Atan2(chordForward, halfWidth) -
                             Mathf.PI * 2f;
            Vector3 surfaceCenter = apron.Center +
                                    Vector3.up * TerminalApronSurfaceOffset;
            var outline = new List<Vector3>(TerminalApronArcSegments + 3)
            {
                apron.EntryCenter -
                apron.Forward * TerminalApronEntryOverlap -
                apron.Right * halfWidth +
                Vector3.up * TerminalApronSurfaceOffset
            };
            for (int index = 0;
                 index <= TerminalApronArcSegments;
                 index++)
            {
                float angle = Mathf.Lerp(
                    startAngle,
                    endAngle,
                    index / (float)TerminalApronArcSegments);
                outline.Add(surfaceCenter +
                    apron.Right * (Mathf.Cos(angle) * apron.TurningRadius) +
                    apron.Forward * (Mathf.Sin(angle) * apron.TurningRadius));
            }

            outline.Add(
                apron.EntryCenter -
                apron.Forward * TerminalApronEntryOverlap +
                apron.Right * halfWidth +
                Vector3.up * TerminalApronSurfaceOffset);

            var vertices = new List<Vector3>(outline.Count + 1)
            {
                surfaceCenter
            };
            vertices.AddRange(outline);
            var uvs = new List<Vector2>(vertices.Count);
            for (int index = 0; index < vertices.Count; index++)
            {
                Vector3 local = vertices[index] - surfaceCenter;
                uvs.Add(new Vector2(
                    Vector3.Dot(local, apron.Right),
                    Vector3.Dot(local, apron.Forward)) / MetersPerTile);
            }

            var triangles = new List<int>(outline.Count * 3);
            for (int index = 0; index < outline.Count; index++)
            {
                triangles.Add(0);
                triangles.Add(index + 1);
                triangles.Add((index + 1) % outline.Count + 1);
            }

            return CreateMesh(
                "Mountain Road Terminal Apron",
                vertices,
                uvs,
                triangles);
        }

        private static List<Row> CreateRows(MountainRoadPlan plan)
        {
            MountainRoadTunnelDescriptor tunnel = plan.Tunnel;
            Vector3 right = Vector3.Cross(
                Vector3.up,
                tunnel.OutwardAxis).normalized;
            var rows = new List<Row>(plan.Route.Samples.Count + 4)
            {
                new Row(
                    tunnel.PortalGroundCenter -
                    tunnel.OutwardAxis * tunnel.VisualDepth,
                    right,
                    tunnel.OpeningWidth,
                    -tunnel.VisualDepth),
                new Row(
                    tunnel.SpawnPosition,
                    right,
                    tunnel.OpeningWidth,
                    -MountainRoadPlanner.SpawnDepth),
                new Row(
                    tunnel.PortalGroundCenter - tunnel.OutwardAxis * 2.2f,
                    right,
                    6.4f,
                    -2.2f)
            };
            MountainRoadRouteSample routeStart = plan.Route.Samples[0];
            rows.Add(new Row(
                routeStart.Position,
                routeStart.Right,
                routeStart.Width,
                routeStart.Distance));
            for (int index = 1; index < plan.Route.Samples.Count; index++)
            {
                MountainRoadRouteSample sample = plan.Route.Samples[index];
                if (sample.Distance >= plan.Plateau.EntryDistance)
                {
                    break;
                }

                rows.Add(new Row(
                    sample.Position,
                    sample.Right,
                    sample.Width,
                    sample.Distance));
            }

            MountainRoadRouteSample entry = plan.Route.Sample(
                plan.Plateau.EntryDistance);
            rows.Add(new Row(
                entry.Position,
                entry.Right,
                entry.Width,
                entry.Distance));
            return rows;
        }

        private static RibbonConnection AppendRibbon(
            IReadOnlyList<Row> rows,
            ICollection<Vector3> vertices,
            ICollection<Vector2> uvs,
            IList<int> triangles)
        {
            var leftTop = new int[rows.Count];
            var rightTop = new int[rows.Count];
            var leftBottom = new int[rows.Count];
            var rightBottom = new int[rows.Count];
            for (int index = 0; index < rows.Count; index++)
            {
                Row row = rows[index];
                float v = row.Distance / MetersPerTile;
                leftTop[index] = AddVertex(
                    row.Left,
                    new Vector2(-row.Width * 0.5f / MetersPerTile, v),
                    vertices,
                    uvs);
                rightTop[index] = AddVertex(
                    row.RightEdge,
                    new Vector2(row.Width * 0.5f / MetersPerTile, v),
                    vertices,
                    uvs);
                leftBottom[index] = AddVertex(
                    row.Left - Vector3.up * SurfaceThickness,
                    new Vector2(0f, v),
                    vertices,
                    uvs);
                rightBottom[index] = AddVertex(
                    row.RightEdge - Vector3.up * SurfaceThickness,
                    new Vector2(1f, v),
                    vertices,
                    uvs);
            }

            for (int index = 1; index < rows.Count; index++)
            {
                AddQuad(
                    leftTop[index - 1],
                    leftTop[index],
                    rightTop[index - 1],
                    rightTop[index],
                    triangles);
                AddQuad(
                    leftBottom[index - 1],
                    leftTop[index - 1],
                    leftBottom[index],
                    leftTop[index],
                    triangles);
                AddQuad(
                    rightTop[index - 1],
                    rightBottom[index - 1],
                    rightTop[index],
                    rightBottom[index],
                    triangles);
            }

            int last = rows.Count - 1;
            return new RibbonConnection(
                leftTop[last],
                rightTop[last],
                leftBottom[last],
                rightBottom[last]);
        }

        private static void AppendPlateau(
            MountainRoadPlateauDescriptor plateau,
            RibbonConnection connection,
            ICollection<Vector3> vertices,
            ICollection<Vector2> uvs,
            IList<int> triangles)
        {
            int centerTop = AddVertex(
                plateau.Center,
                Vector2.zero,
                vertices,
                uvs);
            int centerBottom = AddVertex(
                plateau.Center - Vector3.up * SurfaceThickness,
                Vector2.zero,
                vertices,
                uvs);
            int count = plateau.VerticesXZ.Count;
            var rimTop = new int[count];
            var rimBottom = new int[count];
            for (int index = 0; index < count; index++)
            {
                if (index == 0)
                {
                    rimTop[index] = connection.LeftTop;
                    rimBottom[index] = connection.LeftBottom;
                    continue;
                }

                if (index == count - 1)
                {
                    rimTop[index] = connection.RightTop;
                    rimBottom[index] = connection.RightBottom;
                    continue;
                }

                Vector2 xz = plateau.VerticesXZ[index];
                Vector3 world = new Vector3(xz.x, plateau.Center.y, xz.y);
                Vector3 local = world - plateau.Center;
                Vector2 uv = new Vector2(
                    Vector3.Dot(local, plateau.Right),
                    Vector3.Dot(local, plateau.Forward)) / MetersPerTile;
                rimTop[index] = AddVertex(world, uv, vertices, uvs);
                rimBottom[index] = AddVertex(
                    world - Vector3.up * SurfaceThickness,
                    uv,
                    vertices,
                    uvs);
            }

            for (int index = 0; index < count; index++)
            {
                int next = (index + 1) % count;
                triangles.Add(centerTop);
                triangles.Add(rimTop[index]);
                triangles.Add(rimTop[next]);
                triangles.Add(centerBottom);
                triangles.Add(rimBottom[next]);
                triangles.Add(rimBottom[index]);
                if (next != 0)
                {
                    AddQuad(
                        rimTop[index],
                        rimBottom[index],
                        rimTop[next],
                        rimBottom[next],
                        triangles);
                }
            }
        }

        private static int AddVertex(
            Vector3 position,
            Vector2 uv,
            ICollection<Vector3> vertices,
            ICollection<Vector2> uvs)
        {
            int index = vertices.Count;
            vertices.Add(position);
            uvs.Add(uv);
            return index;
        }

        private static void AddQuad(
            int nearLeft,
            int farLeft,
            int nearRight,
            int farRight,
            IList<int> triangles)
        {
            triangles.Add(nearLeft);
            triangles.Add(farLeft);
            triangles.Add(nearRight);
            triangles.Add(nearRight);
            triangles.Add(farLeft);
            triangles.Add(farRight);
        }

        private static Mesh CreateMesh(
            string name,
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles)
        {
            var mesh = new Mesh
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave
            };
            if (vertices.Count > ushort.MaxValue)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
