using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The coffin that goes down into a dug grave: the six-sided
    /// домовина this cemetery would actually bury — narrow at the
    /// feet, broadest at the shoulders, drawn back in at the head —
    /// with a lid that overhangs it and a cross laid on the lid.
    ///
    /// It is a model of its own rather than a box in the pit's earth
    /// batch, because the taper is the whole silhouette and a box
    /// cannot state it. Oriented boards do: each flank is two boards
    /// turned to their own segment of the outline. Nothing here
    /// carries a collider — it sits 1.2 m below a mouth the hero is
    /// already capped out of.
    /// </summary>
    public static class CityCemeteryCoffinWorldBuilder
    {
        public const string RootName = "Grave Coffin";
        public const string BodyName = "Coffin Boards";
        public const string CrossName = "Coffin Cross";

        public const float LengthMeters = 1.95f;
        public const float BodyHeightMeters = 0.37f;
        public const float LidThicknessMeters = 0.05f;

        /// <summary>Half-widths of the outline at the three points
        /// that define it, from the feet to the head.</summary>
        public const float FootHalfWidthMeters = 0.20f;
        public const float ShoulderHalfWidthMeters = 0.31f;
        public const float HeadHalfWidthMeters = 0.23f;

        /// <summary>Where the shoulders sit along the length: a third
        /// of the way down from the head, which is where a man is
        /// widest and where the outline turns.</summary>
        public const float ShoulderOffsetMeters = 0.36f;

        private const float BoardThickness = 0.04f;
        private const float LidWingWidth = 0.13f;

        /// <summary>Dark stained timber, a shade under the bench
        /// planks: new work, but nobody varnishes a coffin.</summary>
        internal static readonly Color Boards =
            new Color(0.20f, 0.15f, 0.10f);

        /// <summary>The one pale thing at the bottom of the hole.
        /// </summary>
        internal static readonly Color Cross =
            new Color(0.55f, 0.53f, 0.46f);

        /// <summary>Total height of the closed coffin, lid included.
        /// </summary>
        public static float HeightMeters =>
            BodyHeightMeters + LidThicknessMeters;

        public static GameObject Build(
            Transform parent,
            CemeteryGravediggingPlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (!plan.IsPresent)
            {
                return null;
            }

            var root = new GameObject(RootName);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(
                new Vector3(
                    plan.PitMouth.center.x,
                    plan.PitFloorY,
                    plan.PitMouth.center.y),
                plan.Heading);

            var boards = new List<RuntimeOrientedBox>(12);
            AppendFloor(boards);
            AppendFlanks(boards);
            AppendEndBoards(boards);
            AppendLid(boards);
            RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                BodyName,
                root.transform,
                boards,
                Boards);

            var cross = new List<RuntimeOrientedBox>(4);
            AppendCross(cross);
            RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                CrossName,
                root.transform,
                cross,
                Cross);
            return root;
        }

        /// <summary>
        /// The bottom the boards are nailed to. It is never seen — the
        /// lid is on — so it is one rectangle rather than a second
        /// tapered outline.
        /// </summary>
        private static void AppendFloor(
            ICollection<RuntimeOrientedBox> boards)
        {
            boards.Add(new RuntimeOrientedBox(
                new Vector3(0f, 0.025f, 0f),
                Quaternion.identity,
                new Vector3(
                    ShoulderHalfWidthMeters * 2f - BoardThickness,
                    0.05f,
                    LengthMeters - BoardThickness * 2f)));
        }

        /// <summary>
        /// Four boards: for each flank, the long run from the feet out
        /// to the shoulders and the short one drawing back in to the
        /// head, each turned to lie along its own segment.
        /// </summary>
        private static void AppendFlanks(
            ICollection<RuntimeOrientedBox> boards)
        {
            float half = LengthMeters * 0.5f;
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                AppendSegment(
                    boards,
                    new Vector2(sign * FootHalfWidthMeters, -half),
                    new Vector2(
                        sign * ShoulderHalfWidthMeters,
                        ShoulderOffsetMeters),
                    BodyHeightMeters,
                    BoardThickness,
                    BodyHeightMeters * 0.5f);
                AppendSegment(
                    boards,
                    new Vector2(
                        sign * ShoulderHalfWidthMeters,
                        ShoulderOffsetMeters),
                    new Vector2(sign * HeadHalfWidthMeters, half),
                    BodyHeightMeters,
                    BoardThickness,
                    BodyHeightMeters * 0.5f);
            }
        }

        private static void AppendEndBoards(
            ICollection<RuntimeOrientedBox> boards)
        {
            float half = LengthMeters * 0.5f;
            boards.Add(new RuntimeOrientedBox(
                new Vector3(
                    0f,
                    BodyHeightMeters * 0.5f,
                    -half + BoardThickness * 0.5f),
                Quaternion.identity,
                new Vector3(
                    FootHalfWidthMeters * 2f + BoardThickness,
                    BodyHeightMeters,
                    BoardThickness)));
            boards.Add(new RuntimeOrientedBox(
                new Vector3(
                    0f,
                    BodyHeightMeters * 0.5f,
                    half - BoardThickness * 0.5f),
                Quaternion.identity,
                new Vector3(
                    HeadHalfWidthMeters * 2f + BoardThickness,
                    BodyHeightMeters,
                    BoardThickness)));
        }

        /// <summary>
        /// A plank down the middle wide enough to reach the narrowest
        /// point of the outline, and a wing along each of the four
        /// flank segments to carry the taper out past the boards. The
        /// overhang is deliberate: a lid flush with its own sides
        /// reads as a crate.
        /// </summary>
        private static void AppendLid(
            ICollection<RuntimeOrientedBox> boards)
        {
            float lidY = BodyHeightMeters + LidThicknessMeters * 0.5f;
            boards.Add(new RuntimeOrientedBox(
                new Vector3(0f, lidY, 0f),
                Quaternion.identity,
                new Vector3(
                    FootHalfWidthMeters * 2f,
                    LidThicknessMeters,
                    LengthMeters)));
            float half = LengthMeters * 0.5f;
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                AppendSegment(
                    boards,
                    new Vector2(sign * FootHalfWidthMeters, -half),
                    new Vector2(
                        sign * ShoulderHalfWidthMeters,
                        ShoulderOffsetMeters),
                    LidThicknessMeters,
                    LidWingWidth,
                    lidY);
                AppendSegment(
                    boards,
                    new Vector2(
                        sign * ShoulderHalfWidthMeters,
                        ShoulderOffsetMeters),
                    new Vector2(sign * HeadHalfWidthMeters, half),
                    LidThicknessMeters,
                    LidWingWidth,
                    lidY);
            }
        }

        /// <summary>
        /// The Orthodox cross laid on the lid: upright, the short bar
        /// over the title, the main bar, and the footrest slanted in
        /// the plane of the lid because on a closed coffin that plane
        /// is the only one the cross has.
        /// </summary>
        private static void AppendCross(
            ICollection<RuntimeOrientedBox> boxes)
        {
            const float thickness = 0.024f;
            float baseY = BodyHeightMeters + LidThicknessMeters +
                          thickness * 0.5f;
            boxes.Add(new RuntimeOrientedBox(
                new Vector3(0f, baseY, 0.30f),
                Quaternion.identity,
                new Vector3(0.055f, thickness, 0.86f)));
            boxes.Add(new RuntimeOrientedBox(
                new Vector3(0f, baseY, 0.66f),
                Quaternion.identity,
                new Vector3(0.19f, thickness, 0.050f)));
            boxes.Add(new RuntimeOrientedBox(
                new Vector3(0f, baseY, 0.47f),
                Quaternion.identity,
                new Vector3(0.34f, thickness, 0.055f)));
            boxes.Add(new RuntimeOrientedBox(
                new Vector3(0f, baseY, 0.02f),
                Quaternion.Euler(0f, 19f, 0f),
                new Vector3(0.24f, thickness, 0.050f)));
        }

        /// <summary>
        /// One board spanning two points of the outline: it is turned
        /// so its length follows the segment, which is what makes the
        /// flanks a taper instead of a staircase of boxes.
        /// </summary>
        private static void AppendSegment(
            ICollection<RuntimeOrientedBox> boards,
            Vector2 from,
            Vector2 to,
            float height,
            float width,
            float centerY)
        {
            Vector2 span = to - from;
            float length = span.magnitude;
            if (length <= 0f)
            {
                return;
            }

            Vector2 middle = (from + to) * 0.5f;
            boards.Add(new RuntimeOrientedBox(
                new Vector3(middle.x, centerY, middle.y),
                Quaternion.LookRotation(
                    new Vector3(span.x, 0f, span.y),
                    Vector3.up),
                new Vector3(width, height, length)));
        }
    }
}
