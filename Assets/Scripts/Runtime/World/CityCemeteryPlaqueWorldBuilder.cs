using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The little board on the front of a stone the hero set himself.
    ///
    /// It carries three things — a name, a span of years and one line
    /// the hero wrote — but it carries none of them as geometry. The
    /// city's own signage lettering is a handful of authored glyphs
    /// covering exactly the words its signs spell, and a plaque has to
    /// hold whatever a player types. So the board is a board: a plate,
    /// a bevel and four studs, and the words live on the panel the
    /// player reads it with. That is the honest split rather than a
    /// half-built alphabet that fails on the first unusual letter.
    /// </summary>
    public static class CityCemeteryPlaqueWorldBuilder
    {
        public const string RootName = "Grave Plaque";

        public const float WidthMeters = 0.36f;
        public const float HeightMeters = 0.24f;
        public const float ThicknessMeters = 0.018f;
        public const float BevelMeters = 0.022f;

        /// <summary>How far up the face of the stone the board is
        /// fixed. Chest height on a standing monument: where a hand
        /// would put it and where an eye finds it.</summary>
        public const float SeatHeightMeters = 0.72f;

        /// <summary>
        /// Dull brass gone green at the edges — the plate a yard like
        /// this would actually screw to a stone.
        /// </summary>
        internal static readonly Color Plate =
            new Color(0.44f, 0.40f, 0.24f);
        internal static readonly Color Bezel =
            new Color(0.29f, 0.31f, 0.24f);

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
                GetSeat(plan),
                plan.Heading);

            // The bezel first and the plate proud of it, so the board
            // reads as fixed to the stone rather than painted on.
            RuntimePrimitiveFactory.CreateBox(
                "Plaque Bezel",
                root.transform,
                new Vector3(0f, 0f, -ThicknessMeters * 0.5f),
                new Vector3(
                    WidthMeters + BevelMeters,
                    HeightMeters + BevelMeters,
                    ThicknessMeters),
                Bezel,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Plaque Plate",
                root.transform,
                new Vector3(0f, 0f, -ThicknessMeters * 1.2f),
                new Vector3(
                    WidthMeters,
                    HeightMeters,
                    ThicknessMeters * 0.6f),
                Plate,
                false);
            return root;
        }

        /// <summary>
        /// Where the board sits: on the face of the stone, turned to
        /// look down the grave the way anybody reading it would stand.
        /// </summary>
        public static Vector3 GetSeat(CemeteryGravediggingPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            Vector3 face = plan.Heading * new Vector3(
                0f,
                0f,
                -CemeteryPlaqueReadInteraction.FaceOffsetMeters);
            return new Vector3(
                plan.Ground.x + face.x,
                plan.GroundTopY + SeatHeightMeters,
                plan.Ground.z + face.z);
        }
    }
}
