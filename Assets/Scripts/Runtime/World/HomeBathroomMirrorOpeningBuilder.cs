using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// The mirror as a real hole: the authored back wall and back tile keep
    /// their objects, meshes and the wall's full-size collider (physics and
    /// the authored-geometry contracts are untouched) but stop drawing, and
    /// a ring of runtime boxes draws the same wallpaper and tile around the
    /// plate's footprint with continuous texture coordinates. The original
    /// plate stays as the plug that closes the hole whenever the mirrored
    /// room behind it is not being shown; a faint transparent pane sits in
    /// the opening so the reflection reads through dirty glass.
    /// </summary>
    internal static class HomeBathroomMirrorOpeningBuilder
    {
        public const string RootName = "Home Bathroom Mirror Opening";

        /// <summary>Just in front of the tile's face, behind the crack sliver.</summary>
        public const float GlassCenterZ = 3.850f;
        public const float GlassThickness = 0.004f;

        public static HomeBathroomMirrorOpening Build(Transform room)
        {
            if (room == null)
            {
                throw new ArgumentNullException(nameof(room));
            }

            Transform wall = Require(room, HomeBathroomMirrorPlane.BackWallName);
            Transform tile = Require(room, HomeBathroomMirrorPlane.BackTileName);
            Transform plate = Require(room, HomeBathroomMirrorPlane.PlateName);
            MeshRenderer wallRenderer = wall.GetComponent<MeshRenderer>();
            MeshRenderer tileRenderer = tile.GetComponent<MeshRenderer>();
            Renderer plateRenderer = plate.GetComponent<Renderer>();
            if (wallRenderer == null || tileRenderer == null || plateRenderer == null)
            {
                throw new InvalidOperationException(
                    "The bathroom mirror opening needs the back wall, the back tile and the plate to be drawn.");
            }

            HomeInteriorModelLibrary library = HomeInteriorModelLibrary.Load();
            HomeAuthoredPart wallPart = library.Binding(HomeBathroomMirrorPlane.BackWallName, "Box");
            HomeAuthoredPart tilePart = library.Binding(HomeBathroomMirrorPlane.BackTileName, "Box");
            HomeSurfaceKind wallKind = ParseKind(wallPart.sheet, HomeSurfaceKind.Wallpaper);
            HomeSurfaceKind tileKind = ParseKind(tilePart.sheet, HomeSurfaceKind.BathroomTile);

            Bounds wallBounds = RoomLocalBounds(room, wallRenderer);
            Bounds tileBounds = RoomLocalBounds(room, tileRenderer);
            RequireOpeningMatchesPlate(RoomLocalBounds(room, plateRenderer));
            IReadOnlyList<HomeMirrorOpeningPiece> pieces = HomeMirrorOpeningLayout.CreatePieces(
                wallBounds,
                tileBounds,
                HomeBathroomMirrorPlane.OpeningXY,
                HomeBathroomMirrorPlane.CavityMargin,
                HomeBathroomMirrorPlane.SkinThickness);

            var root = new GameObject(RootName);
            root.transform.SetParent(room, false);
            HomeApartmentDressing dressing = room.GetComponentInParent<HomeApartmentDressing>();
            Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            HomeMirrorOpeningLayout.TryGetFaceUvDirections(
                cube, Vector3.back, out bool uGrowsWithX, out bool vGrowsWithY);
            // Each authored box carries metre UVs centred on its own middle, so
            // the replacement pieces take their phase from the part they stand
            // in for; anchoring them at the room origin would slide the
            // wallpaper half a wall sideways and 1.7 m down.
            Vector2 tilePhase = new Vector2(tileBounds.center.x, tileBounds.center.y);
            Vector2 wallPhase = new Vector2(wallBounds.center.x, wallBounds.center.y);
            var pieceRenderers = new List<Renderer>(pieces.Count);
            for (int index = 0; index < pieces.Count; index++)
            {
                HomeMirrorOpeningPiece piece = pieces[index];
                bool isTile = piece.Group == HomeMirrorOpeningGroup.Tile;
                HomeSurfaceKind kind = isTile ? tileKind : wallKind;
                HomeAuthoredPart part = isTile ? tilePart : wallPart;
                MeshRenderer source = isTile ? tileRenderer : wallRenderer;
                GameObject box = RuntimePrimitiveFactory.CreateBox(
                    piece.Name,
                    root.transform,
                    piece.Center,
                    piece.Size,
                    Color.white,
                    false);
                MeshRenderer renderer = box.GetComponent<MeshRenderer>();
                HomeSurfaceAppearance.Apply(renderer, kind, SurfaceProjection.BoxXY, part.Tint);
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                properties.SetVector(
                    "_BaseMap_ST",
                    HomeMirrorOpeningLayout.ContinuousBaseMapTransform(
                        piece.FaceXY,
                        HomeSurfaceAppearance.GetRecipe(kind).MetersPerTile,
                        isTile ? tilePhase : wallPhase,
                        uGrowsWithX,
                        vGrowsWithY));
                renderer.SetPropertyBlock(properties);
                // The wall's sun shadow always falls outside the flat; not
                // casting keeps it off the mirrored room behind the plane.
                renderer.shadowCastingMode = isTile
                    ? source.shadowCastingMode
                    : ShadowCastingMode.Off;
                renderer.receiveShadows = source.receiveShadows;
                dressing?.RegisterSurface(renderer, kind);
                pieceRenderers.Add(renderer);
            }

            wallRenderer.enabled = false;
            tileRenderer.enabled = false;

            Rect opening = HomeBathroomMirrorPlane.OpeningXY;
            GameObject glass = RuntimePrimitiveFactory.CreateBox(
                HomeBathroomMirrorPlane.GlassName,
                root.transform,
                new Vector3(opening.center.x, opening.center.y, GlassCenterZ),
                new Vector3(opening.width, opening.height, GlassThickness),
                HomeBathroomMirrorResources.GlassColor,
                HomeBathroomMirrorResources.GlassMaterial,
                false);
            Renderer glassRenderer = glass.GetComponent<Renderer>();
            glassRenderer.shadowCastingMode = ShadowCastingMode.Off;
            glassRenderer.receiveShadows = false;

            HomeBathroomMirrorOpening component = root.AddComponent<HomeBathroomMirrorOpening>();
            component.Initialize(plateRenderer, glassRenderer, wallRenderer, tileRenderer, pieceRenderers);
            return component;
        }

        private static Transform Require(Transform room, string name)
        {
            Transform found = room.Find(name);
            if (found == null)
            {
                throw new InvalidOperationException(
                    $"The bathroom mirror opening needs '{name}' to be built first.");
            }

            return found;
        }

        /// <summary>
        /// The hole's rectangle is a constant, and the plate that plugs it is
        /// built somewhere else entirely. If the sink ever moves, this is where
        /// it is caught, rather than in a frame where the wall has a slot in it.
        /// </summary>
        private static void RequireOpeningMatchesPlate(Bounds plateBounds)
        {
            Rect opening = HomeBathroomMirrorPlane.OpeningXY;
            const float tolerance = 0.004f;
            bool matches =
                Mathf.Abs(plateBounds.min.x - opening.xMin) <= tolerance &&
                Mathf.Abs(plateBounds.max.x - opening.xMax) <= tolerance &&
                Mathf.Abs(plateBounds.min.y - opening.yMin) <= tolerance &&
                Mathf.Abs(plateBounds.max.y - opening.yMax) <= tolerance &&
                Mathf.Abs(plateBounds.center.z - HomeBathroomMirrorPlane.PlaneZ) <= tolerance;
            if (!matches)
            {
                throw new InvalidOperationException(
                    $"The bathroom mirror's opening {opening} on z {HomeBathroomMirrorPlane.PlaneZ} no longer " +
                    $"matches the plate it must plug (x {plateBounds.min.x:F3}..{plateBounds.max.x:F3}, " +
                    $"y {plateBounds.min.y:F3}..{plateBounds.max.y:F3}, z centre {plateBounds.center.z:F3}).");
            }
        }

        private static HomeSurfaceKind ParseKind(string sheet, HomeSurfaceKind fallback)
        {
            return Enum.TryParse(sheet, out HomeSurfaceKind kind) ? kind : fallback;
        }

        /// <summary>An axis-aligned renderer's box in the room's frame.</summary>
        private static Bounds RoomLocalBounds(Transform room, Renderer renderer)
        {
            Bounds world = renderer.bounds;
            Vector3 min = room.InverseTransformPoint(world.min);
            Vector3 max = room.InverseTransformPoint(world.max);
            var bounds = new Bounds();
            bounds.SetMinMax(Vector3.Min(min, max), Vector3.Max(min, max));
            return bounds;
        }
    }

    /// <summary>
    /// The opening's switch: while the mirrored room is shown the plug plate
    /// is off and the hole is open; otherwise the plate closes it, so no
    /// other shot ever looks through the wall into the void.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HomeBathroomMirrorOpening : MonoBehaviour
    {
        private readonly List<Renderer> pieces = new List<Renderer>();

        public Renderer Plate { get; private set; }
        public Renderer Glass { get; private set; }
        public Renderer HiddenWall { get; private set; }
        public Renderer HiddenTile { get; private set; }
        public IReadOnlyList<Renderer> Pieces => pieces;
        public bool IsInitialized => Plate != null;
        public bool IsMirrorActive { get; private set; }

        internal void Initialize(
            Renderer plate,
            Renderer glass,
            Renderer hiddenWall,
            Renderer hiddenTile,
            IReadOnlyList<Renderer> pieceRenderers)
        {
            Plate = plate;
            Glass = glass;
            HiddenWall = hiddenWall;
            HiddenTile = hiddenTile;
            pieces.Clear();
            pieces.AddRange(pieceRenderers);
            SetMirrorActive(false);
        }

        /// <summary>
        /// The two are exactly complementary: with the mirrored room shown the
        /// hole is open behind a dirty pane, and without it the plate closes
        /// the hole and the pane goes with it, so no shot ever sees a sheet of
        /// glass hanging over a solid plate.
        /// </summary>
        public void SetMirrorActive(bool active)
        {
            IsMirrorActive = active;
            if (Plate != null)
            {
                Plate.enabled = !active;
            }

            if (Glass != null)
            {
                Glass.enabled = active;
            }
        }
    }
}
