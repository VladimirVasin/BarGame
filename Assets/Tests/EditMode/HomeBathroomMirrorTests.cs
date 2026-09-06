using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The geometric bathroom mirror, off the rig: the plane reflects, a
    /// flipped-scale parent reflects for it, the opening tiles the wall and
    /// the tile around the hole without touching it, the texture transforms
    /// agree across every seam, the built room keeps its physics and the
    /// plate plugs the hole, and the twin's head follows the body.
    /// </summary>
    public sealed class HomeBathroomMirrorTests
    {
        private static readonly Bounds WallBounds = new Bounds(new Vector3(0f, 1.70f, 4.00f), new Vector3(10f, 3.4f, 0.24f));
        private static readonly Bounds TileBounds = new Bounds(new Vector3(3.10f, 0.88f, 3.868f), new Vector3(2.98f, 1.70f, 0.022f));

        [Test]
        public void ThePlaneReflectsAcrossItselfAndTheFlippedParentAgrees()
        {
            Vector3 dock = new Vector3(2.075f, 0f, 2.78f);
            Vector3 reflected = HomeBathroomMirrorPlane.Reflect(dock);
            AssertVector(reflected, new Vector3(2.075f, 0f, 4.952f));
            Assert.That(HomeBathroomMirrorPlane.DepthInFront(dock), Is.EqualTo(-HomeBathroomMirrorPlane.DepthInFront(reflected)).Within(1e-5f));
            AssertVector(HomeBathroomMirrorPlane.Reflect(reflected), dock);

            var space = new GameObject("Mirror Space");
            var child = new GameObject("child");
            try
            {
                space.transform.localPosition = HomeBathroomMirrorPlane.SpaceLocalPosition;
                space.transform.localScale = HomeBathroomMirrorPlane.SpaceLocalScale;
                child.transform.SetParent(space.transform, false);
                child.transform.localPosition = dock;
                AssertVector(child.transform.position, reflected);
                Assert.That(child.transform.lossyScale.z, Is.EqualTo(-1f).Within(1e-5f), "The parent flips Z, nothing else.");
                Assert.That(child.transform.lossyScale.x, Is.EqualTo(1f).Within(1e-5f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(space);
            }
        }

        [Test]
        public void TheOpeningTilesTheWallAndTheTileAroundTheHole()
        {
            Rect opening = HomeBathroomMirrorPlane.OpeningXY;
            IReadOnlyList<HomeMirrorOpeningPiece> pieces = HomeMirrorOpeningLayout.CreatePieces(
                WallBounds, TileBounds, opening, HomeBathroomMirrorPlane.CavityMargin, HomeBathroomMirrorPlane.SkinThickness);
            Assert.That(pieces.Count, Is.EqualTo(11));

            float wallArea = 0f, skinArea = 0f, tileArea = 0f;
            int walls = 0, skins = 0, tiles = 0;
            foreach (HomeMirrorOpeningPiece piece in pieces)
            {
                Assert.That(HomeMirrorOpeningLayout.Overlaps(piece.FaceXY, opening), Is.False, piece.Name + " covers the hole.");
                Assert.That(piece.Size.x, Is.GreaterThan(0.001f), piece.Name);
                Assert.That(piece.Size.y, Is.GreaterThan(0.001f), piece.Name);
                Assert.That(piece.Size.z, Is.GreaterThan(0.001f), piece.Name);
                float area = piece.FaceXY.width * piece.FaceXY.height;
                switch (piece.Group)
                {
                    case HomeMirrorOpeningGroup.Wall:
                        walls++;
                        wallArea += area;
                        Assert.That(piece.MinZ, Is.EqualTo(3.88f).Within(1e-4f), piece.Name);
                        Assert.That(piece.MaxZ, Is.EqualTo(4.12f).Within(1e-4f), piece.Name);
                        break;
                    case HomeMirrorOpeningGroup.WallSkin:
                        skins++;
                        skinArea += area;
                        Assert.That(piece.MinZ, Is.EqualTo(3.88f).Within(1e-4f), piece.Name);
                        Assert.That(piece.MaxZ, Is.EqualTo(3.88f + HomeBathroomMirrorPlane.SkinThickness).Within(1e-4f), piece.Name);
                        break;
                    default:
                        tiles++;
                        tileArea += area;
                        Assert.That(piece.MinZ, Is.EqualTo(3.857f).Within(1e-4f), piece.Name);
                        Assert.That(piece.MaxZ, Is.EqualTo(3.879f).Within(1e-4f), piece.Name);
                        break;
                }
            }

            Assert.That(walls, Is.EqualTo(4));
            Assert.That(skins, Is.EqualTo(4));
            Assert.That(tiles, Is.EqualTo(3), "The hole reaches above the tile band, so there is no tile piece above it.");
            float cavityWidth = opening.width + 2f * HomeBathroomMirrorPlane.CavityMargin;
            float cavityHeight = opening.height + 2f * HomeBathroomMirrorPlane.CavityMargin;
            Assert.That(wallArea, Is.EqualTo(10f * 3.4f - cavityWidth * cavityHeight).Within(1e-3f), "The thick wall minus the cavity.");
            Assert.That(skinArea, Is.EqualTo(cavityWidth * cavityHeight - opening.width * opening.height).Within(1e-3f), "The skin minus the exact hole.");
            float tileHoleHeight = TileBounds.max.y - opening.yMin;
            Assert.That(tileArea, Is.EqualTo(2.98f * 1.70f - opening.width * tileHoleHeight).Within(1e-3f), "The tile minus the part of the hole inside it.");
        }

        [Test]
        public void ContinuousTransformsAgreeAcrossEverySeam()
        {
            const float pitch = 1.2f;
            Vector2 phase = new Vector2(3.10f, 0.88f);
            Rect left = Rect.MinMaxRect(1.445f, 0.98f, 1.745f, 2.46f);
            Rect below = Rect.MinMaxRect(1.745f, 0.98f, 2.405f, 1.28f);
            Rect above = Rect.MinMaxRect(1.745f, 2.16f, 2.405f, 2.46f);
            Rect right = Rect.MinMaxRect(2.405f, 0.98f, 2.705f, 2.46f);
            foreach (bool uGrows in new[] { true, false })
            {
                foreach (bool vGrows in new[] { true, false })
                {
                    Vector4 leftTransform = HomeMirrorOpeningLayout.ContinuousBaseMapTransform(left, pitch, phase, uGrows, vGrows);
                    Vector4 belowTransform = HomeMirrorOpeningLayout.ContinuousBaseMapTransform(below, pitch, phase, uGrows, vGrows);
                    Vector4 aboveTransform = HomeMirrorOpeningLayout.ContinuousBaseMapTransform(above, pitch, phase, uGrows, vGrows);
                    Vector4 rightTransform = HomeMirrorOpeningLayout.ContinuousBaseMapTransform(right, pitch, phase, uGrows, vGrows);
                    Assert.That(leftTransform.x, Is.GreaterThan(0f));
                    Assert.That(leftTransform.y, Is.GreaterThan(0f));

                    Vector2 seam = new Vector2(1.745f, 1.10f);
                    AssertVector2(
                        HomeMirrorOpeningLayout.EvaluateTexel(left, leftTransform, seam, uGrows, vGrows),
                        HomeMirrorOpeningLayout.EvaluateTexel(below, belowTransform, seam, uGrows, vGrows),
                        $"vertical seam u{uGrows} v{vGrows}");
                    Vector2 farSeam = new Vector2(2.405f, 2.30f);
                    AssertVector2(
                        HomeMirrorOpeningLayout.EvaluateTexel(above, aboveTransform, farSeam, uGrows, vGrows),
                        HomeMirrorOpeningLayout.EvaluateTexel(right, rightTransform, farSeam, uGrows, vGrows),
                        $"far vertical seam u{uGrows} v{vGrows}");
                    // A point on the pitch grid from the phase origin lands on a whole texel.
                    Vector2 onGrid = new Vector2(3.10f - 1.2f, 0.88f + 1.2f);
                    Rect cell = Rect.MinMaxRect(1.5f, 1.9f, 2.3f, 2.4f);
                    Vector4 cellTransform = HomeMirrorOpeningLayout.ContinuousBaseMapTransform(cell, pitch, phase, uGrows, vGrows);
                    Vector2 texel = HomeMirrorOpeningLayout.EvaluateTexel(cell, cellTransform, onGrid, uGrows, vGrows);
                    Assert.That(Mathf.Abs(texel.x - Mathf.Round(texel.x)), Is.LessThan(1e-4f), $"grid u{uGrows} v{vGrows}");
                    Assert.That(Mathf.Abs(texel.y - Mathf.Round(texel.y)), Is.LessThan(1e-4f), $"grid u{uGrows} v{vGrows}");

                    // The claim that makes the seams agree, stated without
                    // reference to any rectangle: the texel a piece produces at
                    // a room-local point is that point measured from the phase
                    // origin in tiles. Two pieces agree because both obey this.
                    foreach (Vector2 probe in new[]
                             {
                                 new Vector2(1.55f, 2.05f), new Vector2(2.25f, 1.92f), new Vector2(2.30f, 2.39f),
                             })
                    {
                        Vector2 actual = HomeMirrorOpeningLayout.EvaluateTexel(cell, cellTransform, probe, uGrows, vGrows);
                        float expectedU = (uGrows ? 1f : -1f) * (probe.x - phase.x) / pitch;
                        float expectedV = (vGrows ? 1f : -1f) * (probe.y - phase.y) / pitch;
                        Assert.That(actual.x, Is.EqualTo(expectedU).Within(1e-4f), $"u at {probe} for u{uGrows}");
                        Assert.That(actual.y, Is.EqualTo(expectedV).Within(1e-4f), $"v at {probe} for v{vGrows}");
                    }
                }
            }
        }

        [Test]
        public void TheCubeFaceUvDirectionsAreReadFromTheMesh()
        {
            Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            Assert.That(cube, Is.Not.Null);
            Assert.That(
                HomeMirrorOpeningLayout.TryGetFaceUvDirections(cube, Vector3.back, out bool uBack, out bool vBack),
                Is.True,
                "The built-in cube's -Z face must be readable.");
            // Read the same fact straight off the mesh a different way — the
            // widest-apart pair of vertices on that face — so the answer is
            // checked, not merely the fact that one was produced.
            AssertFaceUvDirectionsMatchTheMesh(cube, Vector3.back, uBack, vBack);
            Assert.That(
                HomeMirrorOpeningLayout.TryGetFaceUvDirections(cube, Vector3.right, out bool uRight, out bool vRight),
                Is.True,
                "The side faces the patches use must be readable too.");
            AssertFaceUvDirectionsMatchTheMesh(cube, Vector3.right, uRight, vRight);
            Assert.That(HomeMirrorOpeningLayout.TryGetFaceUvDirections(null, Vector3.back, out _, out _), Is.False);
        }

        [Test]
        public void TheBuiltRoomCutsTheOpeningAndKeepsThePhysics()
        {
            var parent = new GameObject("Home Mirror Opening Test");
            try
            {
                HomeInteriorLayoutPlan plan = HomeInteriorLayoutPlanner.Generate();
                HomeBalconyLayoutPlan balcony = HomeBalconyLayoutPlanner.Generate(plan);
                Transform room = HomeInteriorWorldBuilder.Build(parent.transform, plan, balcony);

                Transform wall = room.Find(HomeBathroomMirrorPlane.BackWallName);
                Assert.That(wall, Is.Not.Null);
                Assert.That(wall.GetComponent<MeshRenderer>().enabled, Is.False, "The authored wall stops drawing.");
                Assert.That(wall.GetComponent<MeshFilter>().sharedMesh, Is.Not.Null, "...but keeps its mesh.");
                BoxCollider collider = wall.GetComponent<BoxCollider>();
                Assert.That(collider, Is.Not.Null, "...and its collider.");
                AssertVector(Vector3.Scale(collider.size, wall.lossyScale), new Vector3(10f, 3.4f, 0.24f));
                Transform tile = room.Find(HomeBathroomMirrorPlane.BackTileName);
                Assert.That(tile.GetComponent<MeshRenderer>().enabled, Is.False);

                HomeBathroomMirrorOpening opening = room.GetComponentInChildren<HomeBathroomMirrorOpening>(true);
                Assert.That(opening, Is.Not.Null);
                Assert.That(opening.IsInitialized, Is.True);
                Assert.That(opening.Pieces.Count, Is.EqualTo(11));
                var block = new MaterialPropertyBlock();
                Rect hole = HomeBathroomMirrorPlane.OpeningXY;
                Texture wallTexture = HomeSurfaceAppearance.GetTexture(HomeSurfaceKind.Wallpaper);
                Texture tileTexture = HomeSurfaceAppearance.GetTexture(HomeSurfaceKind.BathroomTile);
                var faces = new Dictionary<string, Rect>();
                var transforms = new Dictionary<string, Vector4>();
                foreach (Renderer piece in opening.Pieces)
                {
                    Assert.That(piece.enabled, Is.True, piece.name);
                    Assert.That(ReferenceEquals(piece.sharedMaterial, RuntimePrimitiveFactory.DefaultMaterial), Is.True, piece.name + " shares the home material.");
                    piece.GetPropertyBlock(block);
                    bool isTile = piece.name.StartsWith(HomeBathroomMirrorPlane.BackTileName, StringComparison.Ordinal);
                    Assert.That(
                        block.GetTexture("_BaseMap"),
                        Is.EqualTo(isTile ? tileTexture : wallTexture),
                        piece.name + " must wear the surface the authored part it replaces wears.");
                    Vector4 st = block.GetVector("_BaseMap_ST");
                    Assert.That(st.x, Is.GreaterThan(0f), piece.name);
                    Assert.That(st.y, Is.GreaterThan(0f), piece.name);
                    Bounds bounds = piece.bounds;
                    Rect face = Rect.MinMaxRect(bounds.min.x, bounds.min.y, bounds.max.x, bounds.max.y);
                    Assert.That(HomeMirrorOpeningLayout.Overlaps(face, hole), Is.False, piece.name + " must not cover the hole.");
                    faces[piece.name] = face;
                    transforms[piece.name] = st;
                }

                // The seam proof on the real build: the surface pipeline's own
                // per-box hashed phase would put a jump here, so this fails the
                // moment the explicit transform stops being written.
                Assert.That(
                    HomeMirrorOpeningLayout.TryGetFaceUvDirections(
                        Resources.GetBuiltinResource<Mesh>("Cube.fbx"), Vector3.back, out bool uGrows, out bool vGrows),
                    Is.True);
                AssertSeamAgrees(faces, transforms, uGrows, vGrows,
                    HomeBathroomMirrorPlane.BackWallName + " Mirror Left",
                    HomeBathroomMirrorPlane.BackWallName + " Mirror Below",
                    new Vector2(hole.xMin - HomeBathroomMirrorPlane.CavityMargin, hole.yMin));
                AssertSeamAgrees(faces, transforms, uGrows, vGrows,
                    HomeBathroomMirrorPlane.BackWallName + " Mirror Above",
                    HomeBathroomMirrorPlane.BackWallName + " Mirror Right",
                    new Vector2(hole.xMax + HomeBathroomMirrorPlane.CavityMargin, hole.yMax));
                AssertSeamAgrees(faces, transforms, uGrows, vGrows,
                    HomeBathroomMirrorPlane.BackWallName + " Mirror Skin Left",
                    HomeBathroomMirrorPlane.BackWallName + " Mirror Skin Below",
                    new Vector2(hole.xMin, hole.yMin));
                AssertSeamAgrees(faces, transforms, uGrows, vGrows,
                    HomeBathroomMirrorPlane.BackTileName + " Mirror Left",
                    HomeBathroomMirrorPlane.BackTileName + " Mirror Below",
                    new Vector2(hole.xMin, hole.yMin - 0.10f));

                Assert.That(opening.Glass, Is.Not.Null);
                Assert.That(opening.Glass.name, Does.Contain("Glass"));
                Assert.That(ReferenceEquals(opening.Glass.sharedMaterial, RuntimePrimitiveFactory.DefaultMaterial), Is.False);
                Assert.That(opening.Glass.sharedMaterial.renderQueue, Is.GreaterThanOrEqualTo((int)RenderQueue.Transparent));
                Assert.That(opening.Glass.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));

                Assert.That(opening.Plate, Is.Not.Null);
                Assert.That(opening.Plate.enabled, Is.True, "The plate plugs the hole until the mirrored room shows.");
                Assert.That(opening.IsMirrorActive, Is.False);
                opening.SetMirrorActive(true);
                Assert.That(opening.Plate.enabled, Is.False);
                Assert.That(opening.IsMirrorActive, Is.True);
                opening.SetMirrorActive(false);
                Assert.That(opening.Plate.enabled, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        [TestCase(true, true, true, true)]
        [TestCase(true, false, true, true)]
        [TestCase(true, true, false, false)]
        [TestCase(false, true, false, true)]
        [TestCase(false, false, true, false)]
        public void TheTwinsHeadFollowsTheBodyNotTheSource(bool isHead, bool sourceEnabled, bool anyBody, bool expected)
        {
            Assert.That(HomeBathroomMirrorWorld.ResolveTwinRendererEnabled(isHead, sourceEnabled, anyBody), Is.EqualTo(expected));
        }

        [Test]
        public void RoomLocalBoundsComeFromTheMeshesNotTheRendererState()
        {
            var room = new GameObject("Room");
            try
            {
                GameObject box = RuntimePrimitiveFactory.CreateBox("Box", room.transform, new Vector3(2f, 1f, 3f), new Vector3(1f, 2f, 0.5f), Color.white, false);
                box.GetComponent<MeshRenderer>().enabled = false;
                box.SetActive(false);
                Assert.That(HomeBathroomMirrorWorld.TryGetRoomLocalBounds(box.transform, room.transform, out Bounds bounds), Is.True);
                AssertVector(bounds.min, new Vector3(1.5f, 0f, 2.75f));
                AssertVector(bounds.max, new Vector3(2.5f, 2f, 3.25f));
                var empty = new GameObject("Empty");
                empty.transform.SetParent(room.transform, false);
                Assert.That(HomeBathroomMirrorWorld.TryGetRoomLocalBounds(empty.transform, room.transform, out _), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(room);
            }
        }

        /// <summary>
        /// The face's own vertices, read independently: whichever pair is
        /// furthest apart along the face's horizontal axis settles whether u
        /// grows with it, and likewise for v.
        /// </summary>
        private static void AssertFaceUvDirectionsMatchTheMesh(
            Mesh cube,
            Vector3 faceNormal,
            bool reportedU,
            bool reportedV)
        {
            Vector3[] vertices = cube.vertices;
            Vector3[] normals = cube.normals;
            Vector2[] uv = cube.uv;
            Vector3 normal = faceNormal.normalized;
            Vector3 horizontal = Mathf.Abs(normal.x) < 0.5f ? Vector3.right : Vector3.forward;
            Vector3 vertical = Mathf.Abs(normal.y) < 0.5f ? Vector3.up : Vector3.forward;
            int loH = -1, hiH = -1, loV = -1, hiV = -1;
            for (int index = 0; index < vertices.Length; index++)
            {
                if (Vector3.Dot(normals[index], normal) < 0.9f)
                {
                    continue;
                }

                float h = Vector3.Dot(vertices[index], horizontal);
                float v = Vector3.Dot(vertices[index], vertical);
                if (loH < 0 || h < Vector3.Dot(vertices[loH], horizontal)) loH = index;
                if (hiH < 0 || h > Vector3.Dot(vertices[hiH], horizontal)) hiH = index;
                if (loV < 0 || v < Vector3.Dot(vertices[loV], vertical)) loV = index;
                if (hiV < 0 || v > Vector3.Dot(vertices[hiV], vertical)) hiV = index;
            }

            Assert.That(loH, Is.GreaterThanOrEqualTo(0), "No vertices on the " + faceNormal + " face.");
            Assert.That(uv[hiH].x - uv[loH].x, Is.Not.EqualTo(0f), "The face's u is flat along its horizontal axis.");
            Assert.That(uv[hiV].y - uv[loV].y, Is.Not.EqualTo(0f), "The face's v is flat along its vertical axis.");
            Assert.That(uv[hiH].x > uv[loH].x, Is.EqualTo(reportedU), "u direction on the " + faceNormal + " face.");
            Assert.That(uv[hiV].y > uv[loV].y, Is.EqualTo(reportedV), "v direction on the " + faceNormal + " face.");
        }

        /// <summary>Two built pieces must name the same texel where they touch.</summary>
        private static void AssertSeamAgrees(
            Dictionary<string, Rect> faces,
            Dictionary<string, Vector4> transforms,
            bool uGrows,
            bool vGrows,
            string left,
            string right,
            Vector2 seam)
        {
            Assert.That(faces.ContainsKey(left), Is.True, "No piece named '" + left + "'.");
            Assert.That(faces.ContainsKey(right), Is.True, "No piece named '" + right + "'.");
            Vector2 a = HomeMirrorOpeningLayout.EvaluateTexel(faces[left], transforms[left], seam, uGrows, vGrows);
            Vector2 b = HomeMirrorOpeningLayout.EvaluateTexel(faces[right], transforms[right], seam, uGrows, vGrows);
            AssertVector2(a, b, $"'{left}' and '{right}' break the surface at {seam}.");
        }

        private static void AssertVector(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(1e-4f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(1e-4f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(1e-4f));
        }

        private static void AssertVector2(Vector2 actual, Vector2 expected, string message)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(1e-4f), message);
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(1e-4f), message);
        }
    }
}
