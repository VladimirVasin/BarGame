using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The bathroom mirror's plane and the arithmetic of a geometric mirror:
    /// a parent placed at twice the plane's depth with its Z scale flipped
    /// maps every child's room-local pose to its reflection, the way old
    /// games mirrored a room by building it twice. Pure and EditMode-testable.
    /// </summary>
    internal static class HomeBathroomMirrorPlane
    {
        /// <summary>The centre plane of "Home Bathroom Cracked Mirror" (room-local = world).</summary>
        public const float PlaneZ = 3.866f;

        public const string PlateName = "Home Bathroom Cracked Mirror";
        public const string CrackName = "Home Bathroom Mirror Crack";
        public const string GlassName = "Home Bathroom Mirror Glass";
        public const string BackWallName = "Home Back Wall";
        public const string BackTileName = "Home Bathroom Back Tile";

        /// <summary>The plate's footprint on the wall: the hole cut through the tile and the wall.</summary>
        public static readonly Rect OpeningXY = new Rect(1.745f, 1.28f, 0.66f, 0.88f);

        /// <summary>
        /// The thick wall is cut this much wider than the plate, behind a
        /// thin skin cut exactly: a 24 cm wall would otherwise read as a
        /// tunnel at every oblique angle.
        /// </summary>
        public const float CavityMargin = 0.30f;

        /// <summary>The skin's depth in front of the thick wall.</summary>
        public const float SkinThickness = 0.02f;

        public static Vector3 SpaceLocalPosition => new Vector3(0f, 0f, 2f * PlaneZ);
        public static Vector3 SpaceLocalScale => new Vector3(1f, 1f, -1f);

        /// <summary>A room-local point's reflection across the mirror plane.</summary>
        public static Vector3 Reflect(Vector3 roomPoint)
        {
            return new Vector3(roomPoint.x, roomPoint.y, 2f * PlaneZ - roomPoint.z);
        }

        /// <summary>Positive in front of the mirror (inside the bathroom), negative behind it.</summary>
        public static float DepthInFront(Vector3 roomPoint)
        {
            return PlaneZ - roomPoint.z;
        }
    }

    internal enum HomeMirrorOpeningGroup
    {
        Wall = 0,
        WallSkin = 1,
        Tile = 2
    }

    /// <summary>One box of the opening: an axis-aligned room-local piece.</summary>
    internal readonly struct HomeMirrorOpeningPiece
    {
        public HomeMirrorOpeningPiece(
            string name,
            HomeMirrorOpeningGroup group,
            Rect faceXY,
            float minZ,
            float maxZ)
        {
            Name = name;
            Group = group;
            FaceXY = faceXY;
            MinZ = minZ;
            MaxZ = maxZ;
        }

        public string Name { get; }
        public HomeMirrorOpeningGroup Group { get; }

        /// <summary>The piece's rectangle on the wall plane, room-local x and y.</summary>
        public Rect FaceXY { get; }
        public float MinZ { get; }
        public float MaxZ { get; }

        public Vector3 Center => new Vector3(
            FaceXY.center.x,
            FaceXY.center.y,
            (MinZ + MaxZ) * 0.5f);

        public Vector3 Size => new Vector3(FaceXY.width, FaceXY.height, MaxZ - MinZ);
    }

    /// <summary>
    /// Cuts the mirror's opening out of the wall and the tile as a set of
    /// axis-aligned boxes, and writes texture transforms that stay
    /// continuous across the cut — the surface pipeline's own tiling is
    /// per-box with a hashed phase, so pieces of one wall would otherwise
    /// meet at visible seams.
    /// </summary>
    internal static class HomeMirrorOpeningLayout
    {
        private const float MinimumPieceSize = 0.001f;

        public static IReadOnlyList<HomeMirrorOpeningPiece> CreatePieces(
            Bounds wallBounds,
            Bounds tileBounds,
            Rect opening,
            float cavityMargin,
            float skinThickness)
        {
            var pieces = new List<HomeMirrorOpeningPiece>(11);
            Rect wallFace = Rect.MinMaxRect(wallBounds.min.x, wallBounds.min.y, wallBounds.max.x, wallBounds.max.y);
            Rect cavity = Intersect(Grow(opening, cavityMargin), wallFace);
            float skinMaxZ = Mathf.Min(wallBounds.max.z, wallBounds.min.z + skinThickness);
            AddRing(pieces, HomeBathroomMirrorPlane.BackWallName + " Mirror", HomeMirrorOpeningGroup.Wall,
                wallFace, cavity, wallBounds.min.z, wallBounds.max.z);
            AddRing(pieces, HomeBathroomMirrorPlane.BackWallName + " Mirror Skin", HomeMirrorOpeningGroup.WallSkin,
                cavity, opening, wallBounds.min.z, skinMaxZ);
            Rect tileFace = Rect.MinMaxRect(tileBounds.min.x, tileBounds.min.y, tileBounds.max.x, tileBounds.max.y);
            AddRing(pieces, HomeBathroomMirrorPlane.BackTileName + " Mirror", HomeMirrorOpeningGroup.Tile,
                tileFace, Intersect(opening, tileFace), tileBounds.min.z, tileBounds.max.z);
            return pieces;
        }

        /// <summary>Whether a piece's face overlaps the rectangle by more than a hair.</summary>
        public static bool Overlaps(Rect a, Rect b)
        {
            return a.xMin < b.xMax - MinimumPieceSize &&
                   b.xMin < a.xMax - MinimumPieceSize &&
                   a.yMin < b.yMax - MinimumPieceSize &&
                   b.yMin < a.yMax - MinimumPieceSize;
        }

        /// <summary>
        /// A base-map transform whose texel coordinate is a function of the
        /// room-local position alone: scale = face size over the pitch,
        /// offset = the face's edge over the pitch (with the sign the cube
        /// face's own UV direction needs), minus a phase so a grout line
        /// can be pinned where the authored tile put it. Two pieces built
        /// this way tile seamlessly wherever they meet.
        /// </summary>
        public static Vector4 ContinuousBaseMapTransform(
            Rect faceXY,
            float metersPerTile,
            Vector2 phaseOrigin,
            bool uGrowsWithX,
            bool vGrowsWithY)
        {
            float pitch = Mathf.Max(0.0001f, metersPerTile);
            float scaleU = faceXY.width / pitch;
            float scaleV = faceXY.height / pitch;
            float offsetU = uGrowsWithX
                ? (faceXY.xMin - phaseOrigin.x) / pitch
                : -(faceXY.xMax - phaseOrigin.x) / pitch;
            float offsetV = vGrowsWithY
                ? (faceXY.yMin - phaseOrigin.y) / pitch
                : -(faceXY.yMax - phaseOrigin.y) / pitch;
            return new Vector4(scaleU, scaleV, offsetU, offsetV);
        }

        /// <summary>
        /// The texel coordinate the transform produces at a room-local
        /// point of the face — the quantity that must agree across a seam.
        /// </summary>
        public static Vector2 EvaluateTexel(
            Rect faceXY,
            Vector4 transform,
            Vector2 roomPoint,
            bool uGrowsWithX,
            bool vGrowsWithY)
        {
            float u = uGrowsWithX
                ? (roomPoint.x - faceXY.xMin) / faceXY.width
                : (faceXY.xMax - roomPoint.x) / faceXY.width;
            float v = vGrowsWithY
                ? (roomPoint.y - faceXY.yMin) / faceXY.height
                : (faceXY.yMax - roomPoint.y) / faceXY.height;
            return new Vector2(u * transform.x + transform.z, v * transform.y + transform.w);
        }

        /// <summary>
        /// Reads which way the built-in cube's UV runs on the face with the
        /// given outward normal, so the transform never assumes it.
        /// </summary>
        public static bool TryGetFaceUvDirections(
            Mesh cube,
            Vector3 faceNormal,
            out bool uGrowsWithHorizontal,
            out bool vGrowsWithVertical)
        {
            uGrowsWithHorizontal = true;
            vGrowsWithVertical = true;
            if (cube == null)
            {
                return false;
            }

            Vector3[] vertices = cube.vertices;
            Vector3[] normals = cube.normals;
            Vector2[] uv = cube.uv;
            if (vertices == null || normals == null || uv == null ||
                normals.Length != vertices.Length || uv.Length != vertices.Length)
            {
                return false;
            }

            Vector3 normal = faceNormal.normalized;
            // The face's own axes: "horizontal" is whichever world axis the
            // normal leaves free first, "vertical" the other one.
            Vector3 horizontal = Mathf.Abs(normal.x) < 0.5f ? Vector3.right : Vector3.forward;
            Vector3 vertical = Mathf.Abs(normal.y) < 0.5f ? Vector3.up : Vector3.forward;
            float horizontalCorrelation = 0f;
            float verticalCorrelation = 0f;
            Vector3 centre = Vector3.zero;
            Vector2 uvCentre = Vector2.zero;
            int count = 0;
            for (int index = 0; index < vertices.Length; index++)
            {
                if (Vector3.Dot(normals[index], normal) < 0.9f)
                {
                    continue;
                }

                centre += vertices[index];
                uvCentre += uv[index];
                count++;
            }

            if (count < 3)
            {
                return false;
            }

            centre /= count;
            uvCentre /= count;
            for (int index = 0; index < vertices.Length; index++)
            {
                if (Vector3.Dot(normals[index], normal) < 0.9f)
                {
                    continue;
                }

                Vector3 offset = vertices[index] - centre;
                Vector2 uvOffset = uv[index] - uvCentre;
                horizontalCorrelation += Vector3.Dot(offset, horizontal) * uvOffset.x;
                verticalCorrelation += Vector3.Dot(offset, vertical) * uvOffset.y;
            }

            uGrowsWithHorizontal = horizontalCorrelation >= 0f;
            vGrowsWithVertical = verticalCorrelation >= 0f;
            return true;
        }

        private static void AddRing(
            List<HomeMirrorOpeningPiece> pieces,
            string prefix,
            HomeMirrorOpeningGroup group,
            Rect outer,
            Rect hole,
            float minZ,
            float maxZ)
        {
            if (maxZ - minZ < MinimumPieceSize)
            {
                return;
            }

            Rect clippedHole = Intersect(hole, outer);
            if (clippedHole.width < MinimumPieceSize || clippedHole.height < MinimumPieceSize)
            {
                Add(pieces, prefix, group, outer, minZ, maxZ);
                return;
            }

            Add(pieces, prefix + " Left", group,
                Rect.MinMaxRect(outer.xMin, outer.yMin, clippedHole.xMin, outer.yMax), minZ, maxZ);
            Add(pieces, prefix + " Right", group,
                Rect.MinMaxRect(clippedHole.xMax, outer.yMin, outer.xMax, outer.yMax), minZ, maxZ);
            Add(pieces, prefix + " Below", group,
                Rect.MinMaxRect(clippedHole.xMin, outer.yMin, clippedHole.xMax, clippedHole.yMin), minZ, maxZ);
            Add(pieces, prefix + " Above", group,
                Rect.MinMaxRect(clippedHole.xMin, clippedHole.yMax, clippedHole.xMax, outer.yMax), minZ, maxZ);
        }

        private static void Add(
            List<HomeMirrorOpeningPiece> pieces,
            string name,
            HomeMirrorOpeningGroup group,
            Rect face,
            float minZ,
            float maxZ)
        {
            if (face.width < MinimumPieceSize || face.height < MinimumPieceSize)
            {
                return;
            }

            pieces.Add(new HomeMirrorOpeningPiece(name, group, face, minZ, maxZ));
        }

        private static Rect Grow(Rect rect, float margin)
        {
            return Rect.MinMaxRect(
                rect.xMin - margin, rect.yMin - margin, rect.xMax + margin, rect.yMax + margin);
        }

        private static Rect Intersect(Rect a, Rect b)
        {
            float xMin = Mathf.Max(a.xMin, b.xMin);
            float yMin = Mathf.Max(a.yMin, b.yMin);
            float xMax = Mathf.Min(a.xMax, b.xMax);
            float yMax = Mathf.Min(a.yMax, b.yMax);
            if (xMax <= xMin || yMax <= yMin)
            {
                return new Rect(xMin, yMin, 0f, 0f);
            }

            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }
    }
}
