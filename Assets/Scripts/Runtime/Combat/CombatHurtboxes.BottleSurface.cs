using UnityEngine;

namespace BarPromenade
{
    internal sealed partial class CombatHurtboxes
    {
        private CombatZombieBottleDestruction bottle;
        private HeadSurface bottleFlesh;
        private HeadTriangle[] bottleTriangles;
        private int[] bottleCellOffsets;
        private Bounds[] bottleCellBounds;
        private bool[] bottleCellReady;
        private Matrix4x4 bottleMatrix;
        private Bounds bottleBounds;
        private uint bottleVersion;

        internal void SetBottleSurfaces(CombatZombieBottleDestruction owner, SkinnedMeshRenderer flesh)
        {
            bottle = owner;
            bottleFlesh = new HeadSurface(flesh, captureBlendShapes: true);
            bottleCellOffsets = new int[owner.CellCount];
            bottleCellBounds = new Bounds[owner.CellCount]; bottleCellReady = new bool[owner.CellCount];
            int triangles = 0;
            for (int i = 0; i < owner.CellCount; i++)
            { bottleCellOffsets[i] = triangles; triangles += owner.CellTriangles[i].Length / 3; }
            bottleTriangles = new HeadTriangle[triangles];
            InvalidateBottleGeometry(); CaptureBottlePose();
        }

        internal void InvalidateBottleGeometry()
        {
            if (bottleCellReady != null) System.Array.Clear(bottleCellReady, 0, bottleCellReady.Length);
            projectileBoundsCaptured = false;
        }

        private void CaptureBottlePose()
        {
            if (bottle == null) return;
            Matrix4x4 matrix = bottle.GlassTransform.localToWorldMatrix;
            if (!matrix.Equals(bottleMatrix) || bottleVersion != bottle.GeometryVersion)
                InvalidateBottleGeometry();
            bottleMatrix = matrix; bottleVersion = bottle.GeometryVersion;
            bottleBounds = bottle.WorldBounds;
            for (int i = 0; i < bottle.CellCount; i++)
                bottleCellBounds[i] = CombatZombieBottleDestruction.TransformBounds(bottle.CellBounds[i], matrix);
            bottleFlesh.CapturePose();
        }

        private bool BottleOwnsTorso(Player3DAnatomicalPart part) => bottle != null &&
            part is Player3DAnatomicalPart.Torso or Player3DAnatomicalPart.LowerTorso or Player3DAnatomicalPart.Pelvis or Player3DAnatomicalPart.Neck;

        private bool SweepBottleSurfaces(Vector3 from, Vector3 to, float radius, Vector3 direction, ref Hit hit, float first)
        {
            if (bottle == null) return false;
            bool found = false;
            Hit selected = hit;
            var query = new SegmentQuery(from, to - from, radius);
            if (bottleFlesh.Active && query.Intersects(bottleFlesh.Bounds, first))
            {
                bottleFlesh.PrepareQuery(query, first);
                for (int i = 0; i < bottleFlesh.QueryTriangleCount; i++)
                    Test(bottleFlesh.Triangles[i], false);
            }
            if (query.Intersects(bottleBounds, first))
            {
                Vector3[] vertices = bottle.GlassVertices;
                for (int cell = 0; cell < bottle.CellCount; cell++)
                {
                    if (bottle.BrokenCells[cell] || !query.Intersects(bottleCellBounds[cell], first)) continue;
                    int[] indices = bottle.CellTriangles[cell];
                    int offset = bottleCellOffsets[cell];
                    if (!bottleCellReady[cell])
                    {
                        for (int i = 0; i < indices.Length; i += 3)
                            bottleTriangles[offset + i / 3] = new HeadTriangle(
                                bottleMatrix.MultiplyPoint3x4(vertices[indices[i]]),
                                bottleMatrix.MultiplyPoint3x4(vertices[indices[i + 1]]),
                                bottleMatrix.MultiplyPoint3x4(vertices[indices[i + 2]]));
                        bottleCellReady[cell] = true;
                    }
                    for (int i = 0; i < indices.Length / 3; i++) Test(bottleTriangles[offset + i], true);
                }
            }
            hit = selected;
            return found;

            void Test(HeadTriangle triangle, bool glass)
            {
                Vector3 delta = to - from;
                if (!query.Intersects(triangle.Bounds, first) ||
                    !triangle.FirstContact(from, delta, radius, out float fraction) || fraction >= first) return;
                Vector3 centre = from + delta * fraction, point = triangle.Closest(centre);
                Vector3 normal = centre - point;
                if (normal.sqrMagnitude < .00000001f)
                {
                    normal = Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A).normalized;
                    if (Vector3.Dot(normal, direction) > 0f) normal = -normal;
                }
                else normal.Normalize();
                Vector3 offset = actorFrame.InverseTransformVector(point - actorFrame.position);
                var location = MeleeHitLocation.FromLocalSurface(MeleeBodyRegion.Torso, offset.x, offset.y, offset.z);
                selected = new Hit(point, normal, direction, fraction, location, Player3DAnatomicalPart.Torso,
                    actorFrame.InverseTransformPoint(point), actorFrame.InverseTransformDirection(direction),
                    glass ? BodyDamageRegion.Chest : BodyDamageRegion.Abdomen, 0);
                first = fraction; found = true;
            }
        }

        internal static Vector3 ClosestBottleTriangle(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
            => new HeadTriangle(a, b, c).Closest(point);
    }
}
