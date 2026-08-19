using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The spade the grave is dug with: a pointed штыковая blade with
    /// a rolled tread across its shoulders, a socket, an ash shaft and
    /// a D-grip at the top of it.
    ///
    /// Two materials and therefore two combined meshes, because a
    /// spade read at arm's length is exactly one thing — a bright
    /// worn edge on a dark handle — and one tinted mesh would throw
    /// that away.
    ///
    /// The origin sits at the point of the blade, on the plane the
    /// blade would enter. That is the end that does the work, so the
    /// tool can be aimed by putting its origin where the earth is and
    /// turning it, without a pivot offset anywhere in the animation.
    /// The shaft runs up local <c>+Y</c>, the blade's face looks along
    /// local <c>-Z</c>, and the whole handle leans back off the blade
    /// the few degrees a real one does.
    ///
    /// No collider. It spends its life either in the hero's working
    /// arc or stood in a heap of loose earth, and in both places a
    /// collider is something to catch on.
    /// </summary>
    public static class CityGravediggerShovelWorldBuilder
    {
        public const string RootName = "Gravedigger Spade";
        public const string SteelName = "Spade Steel";
        public const string HandleName = "Spade Handle";

        /// <summary>Point to shoulder.</summary>
        public const float BladeLengthMeters = 0.30f;
        public const float BladeWidthMeters = 0.20f;
        public const float BladeThicknessMeters = 0.014f;

        /// <summary>How much of the blade is the taper down to the
        /// point rather than parallel sides.</summary>
        public const float BladePointMeters = 0.09f;

        public const float SocketLengthMeters = 0.13f;
        public const float ShaftLengthMeters = 0.98f;
        public const float ShaftThicknessMeters = 0.034f;

        public const float GripHeightMeters = 0.15f;
        public const float GripWidthMeters = 0.115f;

        /// <summary>
        /// How far the handle leans back off the blade's plane. A
        /// spade whose shaft is straight in line with its blade is a
        /// shovel for coal, not for ground.
        /// </summary>
        public const float HandleLeanDegrees = 11f;

        /// <summary>Overall length, point of the blade to the top of
        /// the grip.</summary>
        public static float LengthMeters =>
            BladeLengthMeters +
            SocketLengthMeters +
            ShaftLengthMeters +
            GripHeightMeters;

        /// <summary>
        /// Worn steel: dark, but the one thing at the graveside that
        /// takes a highlight off the work lamp.
        /// </summary>
        internal static readonly Color Steel =
            new Color(0.34f, 0.35f, 0.37f);

        /// <summary>Ash, darkened by hands.</summary>
        internal static readonly Color Handle =
            new Color(0.31f, 0.23f, 0.14f);

        public static GameObject Build(
            Transform parent,
            string name)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            var root = new GameObject(
                string.IsNullOrEmpty(name) ? RootName : name);
            root.transform.SetParent(parent, false);

            var steel = new List<RuntimeOrientedBox>(8);
            AppendBlade(steel);
            AppendSocket(steel);
            RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                SteelName,
                root.transform,
                steel,
                Steel);

            var timber = new List<RuntimeOrientedBox>(5);
            AppendShaft(timber);
            AppendGrip(timber);
            RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                HandleName,
                root.transform,
                timber,
                Handle);
            return root;
        }

        /// <summary>
        /// How deep the blade is driven when the spade is left
        /// standing in the ground.
        /// </summary>
        public const float RestBiteMeters = 0.13f;

        /// <summary>How far off the vertical it leans there. A spade
        /// left standing perfectly upright looks planted; one at a
        /// lean looks put down.</summary>
        public const float RestLeanDegrees = 17f;

        /// <summary>
        /// Where the spade stands when nobody has it: driven into the
        /// ground beside the waiting coffin, past the foot of the
        /// grave. It is a pure function of the plan, so a spade left
        /// at a worksite is in the same place every time the city is
        /// rebuilt.
        /// </summary>
        public static Vector3 GetRestPosition(
            CemeteryGravediggingPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            return new Vector3(
                plan.SpadeRestGround.x,
                plan.GroundTopY - RestBiteMeters,
                plan.SpadeRestGround.z);
        }

        /// <summary>The lean of a spade stuck in the ground, as a
        /// rotation applied to the assembly's own frame.</summary>
        public static Quaternion GetRestRotation(
            CemeteryGravediggingPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            return Quaternion.Euler(
                       0f,
                       plan.SpadeRestYawDegrees,
                       0f) *
                   Quaternion.Euler(-RestLeanDegrees, 0f, 8f);
        }

        /// <summary>
        /// The blade: a parallel-sided pan for most of its length and
        /// a taper to the point at the bottom. The taper is two boards
        /// turned in to meet, which is what gives the outline its
        /// shoulder instead of a wedge.
        /// </summary>
        private static void AppendBlade(
            ICollection<RuntimeOrientedBox> boxes)
        {
            float pan = BladeLengthMeters - BladePointMeters;
            boxes.Add(new RuntimeOrientedBox(
                new Vector3(
                    0f,
                    BladePointMeters + (pan * 0.5f),
                    0f),
                Quaternion.identity,
                new Vector3(
                    BladeWidthMeters,
                    pan,
                    BladeThicknessMeters)));

            // The two sides of the point, each turned in far enough to
            // close on the origin.
            float taper = Mathf.Atan2(
                              BladeWidthMeters * 0.5f,
                              BladePointMeters) *
                          Mathf.Rad2Deg;
            float half = BladePointMeters / Mathf.Cos(
                Mathf.Deg2Rad * taper);
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                boxes.Add(new RuntimeOrientedBox(
                    new Vector3(
                        sign * BladeWidthMeters * 0.25f,
                        BladePointMeters * 0.5f,
                        0f),
                    Quaternion.Euler(0f, 0f, sign * taper * 0.5f),
                    new Vector3(
                        BladeWidthMeters * 0.62f,
                        half,
                        BladeThicknessMeters)));
            }

            // The tread across the shoulders: what a boot goes on.
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                boxes.Add(new RuntimeOrientedBox(
                    new Vector3(
                        sign * BladeWidthMeters * 0.30f,
                        BladeLengthMeters - 0.012f,
                        0.014f),
                    Quaternion.Euler(38f, 0f, 0f),
                    new Vector3(
                        BladeWidthMeters * 0.38f,
                        0.030f,
                        0.020f)));
            }
        }

        /// <summary>The socket the shaft is driven into, tapering off
        /// the back of the blade.</summary>
        private static void AppendSocket(
            ICollection<RuntimeOrientedBox> boxes)
        {
            Quaternion lean =
                Quaternion.Euler(HandleLeanDegrees, 0f, 0f);
            Vector3 seat = new Vector3(0f, BladeLengthMeters, 0f);
            boxes.Add(new RuntimeOrientedBox(
                seat + (lean * new Vector3(
                    0f,
                    SocketLengthMeters * 0.5f,
                    0f)),
                lean,
                new Vector3(
                    ShaftThicknessMeters * 1.7f,
                    SocketLengthMeters,
                    ShaftThicknessMeters * 1.7f)));
        }

        private static void AppendShaft(
            ICollection<RuntimeOrientedBox> boxes)
        {
            Quaternion lean =
                Quaternion.Euler(HandleLeanDegrees, 0f, 0f);
            Vector3 seat = new Vector3(
                0f,
                BladeLengthMeters,
                0f);
            boxes.Add(new RuntimeOrientedBox(
                seat + (lean * new Vector3(
                    0f,
                    SocketLengthMeters + (ShaftLengthMeters * 0.5f),
                    0f)),
                lean,
                new Vector3(
                    ShaftThicknessMeters,
                    ShaftLengthMeters,
                    ShaftThicknessMeters)));
        }

        /// <summary>
        /// The D at the top: two splayed cheeks off the end of the
        /// shaft and one crossbar between them.
        /// </summary>
        private static void AppendGrip(
            ICollection<RuntimeOrientedBox> boxes)
        {
            Quaternion lean =
                Quaternion.Euler(HandleLeanDegrees, 0f, 0f);
            Vector3 top = new Vector3(0f, BladeLengthMeters, 0f) +
                          (lean * new Vector3(
                              0f,
                              SocketLengthMeters + ShaftLengthMeters,
                              0f));
            float splay = Mathf.Atan2(
                              GripWidthMeters * 0.5f,
                              GripHeightMeters) *
                          Mathf.Rad2Deg;
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                boxes.Add(new RuntimeOrientedBox(
                    top + (lean * new Vector3(
                        sign * GripWidthMeters * 0.25f,
                        GripHeightMeters * 0.5f,
                        0f)),
                    lean * Quaternion.Euler(0f, 0f, sign * splay),
                    new Vector3(
                        ShaftThicknessMeters * 0.72f,
                        GripHeightMeters,
                        ShaftThicknessMeters * 0.72f)));
            }

            boxes.Add(new RuntimeOrientedBox(
                top + (lean * new Vector3(
                    0f,
                    GripHeightMeters,
                    0f)),
                lean,
                new Vector3(
                    GripWidthMeters,
                    ShaftThicknessMeters * 0.80f,
                    ShaftThicknessMeters * 0.80f)));
        }
    }
}
