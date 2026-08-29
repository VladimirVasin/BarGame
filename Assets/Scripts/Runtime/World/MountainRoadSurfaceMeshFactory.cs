using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    public static class MountainRoadSurfaceMeshFactory
    {
        public const float SurfaceThickness = 0.18f;
        public const float TerminalApronSurfaceOffset = 0.025f;
        public const float TerminalApronEntryOverlap = 0.45f;

        /// <summary>
        /// The pitch every road UV is baked at, read from the packaged
        /// asphalt recipe so the mesh and the sheet can never drift apart.
        /// </summary>
        public static float MetersPerTile =>
            MountainRoadSurfaceAppearance.GetRecipe(
                MountainRoadSurfaceKind.Asphalt).MetersPerTile;

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
                plan.Route.Sample(plan.Plateau.EntryDistance),
                connection,
                vertices,
                uvs,
                triangles);
            return CreateMesh("Mountain Road Surface", vertices, uvs, triangles);
        }

        /// <summary>
        /// The visible pocket the vehicle turns in. Its UVs are anchored to
        /// the road entry rather than to the pocket's own centre, so the
        /// asphalt runs continuously across the seam the apron shares with
        /// the road and the plateau instead of restarting there.
        /// </summary>
        public static Mesh CreateTerminalApron(
            MountainRoadVehicleApronPlan apron,
            float entryDistance)
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
            float tile = MetersPerTile;
            var uvs = new List<Vector2>(vertices.Count);
            for (int index = 0; index < vertices.Count; index++)
            {
                Vector3 local = vertices[index] - apron.EntryCenter;
                uvs.Add(new Vector2(
                    Vector3.Dot(local, apron.Right),
                    entryDistance +
                    Vector3.Dot(local, apron.Forward)) / tile);
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
            float tile = MetersPerTile;
            var leftTop = new int[rows.Count];
            var rightTop = new int[rows.Count];
            var leftBottom = new int[rows.Count];
            var rightBottom = new int[rows.Count];
            for (int index = 0; index < rows.Count; index++)
            {
                Row row = rows[index];
                float v = row.Distance / tile;
                float halfWidth = row.Width * 0.5f / tile;

                // Across the carriageway and along the distance travelled.
                leftTop[index] = AddVertex(
                    row.Left,
                    new Vector2(-halfWidth, v),
                    vertices,
                    uvs);
                rightTop[index] = AddVertex(
                    row.RightEdge,
                    new Vector2(halfWidth, v),
                    vertices,
                    uvs);

                // The kerb continues that same unwrap over the edge: its U
                // runs on past the carriageway by the slab's own thickness,
                // so 0.18 m of face carries 0.18 m of sheet. These vertices
                // are shared with the plateau rim and with the collider, so
                // they are re-mapped rather than duplicated; giving them a
                // fixed 0..1 U is what used to squeeze three metres of
                // asphalt into two centimetres of border.
                float kerb = halfWidth + SurfaceThickness / tile;
                leftBottom[index] = AddVertex(
                    row.Left - Vector3.up * SurfaceThickness,
                    new Vector2(-kerb, v),
                    vertices,
                    uvs);
                rightBottom[index] = AddVertex(
                    row.RightEdge - Vector3.up * SurfaceThickness,
                    new Vector2(kerb, v),
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
                // Kerbs face away from the carriageway. They used to be
                // wound inward (+Right on the left, -Right on the right),
                // so the opaque mountain shader culled both vertical road
                // edges and the ribbon visually dissolved into the soil.
                AddQuad(
                    leftTop[index - 1],
                    leftBottom[index - 1],
                    leftTop[index],
                    leftBottom[index],
                    triangles);
                AddQuad(
                    rightBottom[index - 1],
                    rightTop[index - 1],
                    rightBottom[index],
                    rightTop[index],
                    triangles);
            }

            int last = rows.Count - 1;
            return new RibbonConnection(
                leftTop[last],
                rightTop[last],
                leftBottom[last],
                rightBottom[last]);
        }

        /// <summary>
        /// The plateau's UVs live in the road's own frame, measured from the
        /// entry sample and biased by the distance already travelled. That
        /// is what makes the sheet cross the shared entry seam unbroken:
        /// the two connection vertices the ribbon already wrote land on
        /// exactly the value this projection would give them. A plateau
        /// unwrapped around its own centre restarts the texture at the seam
        /// instead, which is the join the old mapping showed.
        /// </summary>
        private static void AppendPlateau(
            MountainRoadPlateauDescriptor plateau,
            MountainRoadRouteSample entry,
            RibbonConnection connection,
            ICollection<Vector3> vertices,
            ICollection<Vector2> uvs,
            IList<int> triangles)
        {
            float tile = MetersPerTile;
            float skirt = SurfaceThickness / tile;
            Vector2 centerUv = ProjectRoadFrameUv(
                plateau.Center,
                plateau,
                entry,
                tile);
            int centerTop = AddVertex(
                plateau.Center,
                centerUv,
                vertices,
                uvs);
            int centerBottom = AddVertex(
                plateau.Center - Vector3.up * SurfaceThickness,
                centerUv,
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
                Vector2 uv = ProjectRoadFrameUv(world, plateau, entry, tile);
                rimTop[index] = AddVertex(world, uv, vertices, uvs);

                // The rim's own kerb unwraps outward from the centre by the
                // slab thickness, the same trick the road's border uses, so
                // the skirt samples a real 0.18 m band rather than smearing
                // one line of the sheet down its whole face.
                Vector2 outward = uv - centerUv;
                rimBottom[index] = AddVertex(
                    world - Vector3.up * SurfaceThickness,
                    uv + (outward.sqrMagnitude > 1e-8f
                        ? outward.normalized
                        : Vector2.down) * skirt,
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

        /// <summary>
        /// One world point in the road's unwrap: across the carriageway on
        /// U, along the distance already travelled on V.
        /// </summary>
        private static Vector2 ProjectRoadFrameUv(
            Vector3 world,
            MountainRoadPlateauDescriptor plateau,
            MountainRoadRouteSample entry,
            float tile)
        {
            Vector3 local = world - entry.Position;
            return new Vector2(
                Vector3.Dot(local, plateau.Right),
                entry.Distance +
                Vector3.Dot(local, plateau.Forward)) / tile;
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
