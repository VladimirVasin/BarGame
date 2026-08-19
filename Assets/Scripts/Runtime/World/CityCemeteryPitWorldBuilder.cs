using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Materialises a finished grave: the earth collar lining the hole
    /// the excavation cut out of the ground slab, the floor it stops
    /// at, and the spoil heaped along one side. The hole itself is not
    /// built here — it is the rectangle
    /// <see cref="CityCemeteryGroundExcavation"/> removed from the
    /// slab, and this dressing only lines what is already missing.
    ///
    /// The collar refills exactly the ring the excavation over-cut, so
    /// the walking surface stays continuous right up to the edge and
    /// the wall faces carry box-projected soil UVs instead of the
    /// slab's planar ones, which would smear down a vertical face.
    /// </summary>
    public static class CityCemeteryPitWorldBuilder
    {
        public const string RootName = "Dug Grave";
        public const string GuardName = "Grave Mouth Guard";

        /// <summary>How far the floor slab reaches below the floor the
        /// hero sees, so the bottom of the hole is never a paper
        /// sheet seen edge-on.</summary>
        public const float FloorThickness = 0.22f;

        /// <summary>
        /// The heap, course by course from the ground up. Each one is
        /// smaller than the last and nudged off the one below it: a
        /// stack of centred courses reads as a ziggurat, and a
        /// gravedigger does not build ziggurats.
        /// </summary>
        private static readonly float[] SpoilCourseHeights =
            { 0.17f, 0.12f, 0.09f, 0.06f };
        private static readonly float[] SpoilCourseScales =
            { 1.00f, 0.78f, 0.55f, 0.30f };
        private static readonly Vector2[] SpoilCourseOffsets =
        {
            new Vector2(0f, 0f),
            new Vector2(0.11f, -0.06f),
            new Vector2(-0.09f, 0.07f),
            new Vector2(0.06f, 0.03f)
        };

        /// <summary>
        /// Fresh-turned earth: the same soil sheet the cemetery ground
        /// carries, tinted down to the wet colour of ground that saw
        /// daylight an hour ago.
        /// </summary>
        internal static readonly Color FreshEarth =
            new Color(0.115f, 0.092f, 0.062f);

        /// <summary>
        /// The hero has neither a jump nor a climb, and a stepOffset
        /// of 0.28 m will not lift him out of a grave. Until digging
        /// grows a way back out, an invisible cap over the mouth keeps
        /// him from walking into a hole he could not leave. Delete it
        /// the day he can climb.
        /// </summary>
        public const float MouthGuardThickness = 0.10f;

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

            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);

            var boxes = new List<Bounds>(9);
            AppendCollar(boxes, plan);
            AppendFloor(boxes, plan);
            AppendSpoil(boxes, plan, 1f);

            GameObject earth =
                RuntimePrimitiveFactory.CreateCombinedBoxes(
                    "Grave Earth",
                    root,
                    boxes,
                    FreshEarth,
                    true,
                    CityCemeterySurfaceAppearance.GetRecipe(
                        CityCemeterySurfaceKind.Soil).MetersPerTile,
                    RuntimeWorldUvMode.BoxProjected);
            CityCemeterySurfaceAppearance.ApplyCombined(
                earth.GetComponent<Renderer>(),
                CityCemeterySurfaceKind.Soil,
                FreshEarth);

            AppendMouthGuard(root, plan);
            return root.gameObject;
        }

        /// <summary>The rectangle the ground slab gives up: the mouth
        /// plus the collar that lines it.</summary>
        public static Rect GetExcavationRect(
            CemeteryGravediggingPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            return Expand(
                plan.PitMouth,
                CemeteryGravediggingPlan.PitWallThicknessMeters);
        }

        internal static void AppendCollar(
            ICollection<Bounds> boxes,
            CemeteryGravediggingPlan plan)
        {
            Rect mouth = plan.PitMouth;
            Rect outer = GetExcavationRect(plan);
            float floor = plan.PitFloorY;
            float top = plan.GroundTopY;

            // Two long walls spanning the whole outer rectangle, two
            // short ones between them: the ring closes at the corners
            // without any pair of boxes sharing a volume.
            AddBox(boxes, outer.xMin, outer.xMax,
                mouth.yMax, outer.yMax, floor, top);
            AddBox(boxes, outer.xMin, outer.xMax,
                outer.yMin, mouth.yMin, floor, top);
            AddBox(boxes, outer.xMin, mouth.xMin,
                mouth.yMin, mouth.yMax, floor, top);
            AddBox(boxes, mouth.xMax, outer.xMax,
                mouth.yMin, mouth.yMax, floor, top);
        }

        internal static void AppendFloor(
            ICollection<Bounds> boxes,
            CemeteryGravediggingPlan plan)
        {
            Rect outer = GetExcavationRect(plan);
            AddBox(
                boxes,
                outer.xMin,
                outer.xMax,
                outer.yMin,
                outer.yMax,
                plan.PitFloorY - FloorThickness,
                plan.PitFloorY);
        }

        /// <summary>
        /// The heap at any point between bare grass and everything the
        /// hole held. <paramref name="fullness01"/> walks the courses
        /// on from the bottom, growing the one it is part-way through
        /// rather than popping a whole course into existence — a heap
        /// that jumps a step every spadeful reads as a staircase being
        /// built, not as earth being thrown.
        /// </summary>
        internal static void AppendSpoil(
            ICollection<Bounds> boxes,
            CemeteryGravediggingPlan plan,
            float fullness01)
        {
            Rect footprint = plan.SpoilFootprint;
            int count = SpoilCourseHeights.Length;
            float filled = Mathf.Clamp01(fullness01) * count;
            float top = plan.GroundTopY;
            for (int course = 0; course < count; course++)
            {
                float share = Mathf.Clamp01(filled - course);
                if (share <= 0f)
                {
                    break;
                }

                float height = SpoilCourseHeights[course] * share;
                AddCourse(
                    boxes,
                    footprint,
                    SpoilCourseScales[course],
                    SpoilCourseOffsets[course],
                    top,
                    height);
                top += height;
            }
        }

        private static void AddCourse(
            ICollection<Bounds> boxes,
            Rect footprint,
            float scale,
            Vector2 offset,
            float bottomY,
            float height)
        {
            Vector2 center = footprint.center + offset;
            float halfWidth = footprint.width * 0.5f * scale;
            float halfHeight = footprint.height * 0.5f * scale;
            AddBox(
                boxes,
                center.x - halfWidth,
                center.x + halfWidth,
                center.y - halfHeight,
                center.y + halfHeight,
                bottomY,
                bottomY + height);
        }

        /// <summary>
        /// See <see cref="MouthGuardThickness"/>: a collider with no
        /// renderer, flush with the ground over the open mouth.
        /// </summary>
        internal static void AppendMouthGuard(
            Transform root,
            CemeteryGravediggingPlan plan)
        {
            Rect mouth = plan.PitMouth;
            var guard = new GameObject(GuardName);
            guard.transform.SetParent(root, false);
            guard.transform.position = new Vector3(
                mouth.center.x,
                plan.GroundTopY - MouthGuardThickness * 0.5f,
                mouth.center.y);
            BoxCollider collider = guard.AddComponent<BoxCollider>();
            collider.size = new Vector3(
                mouth.width,
                MouthGuardThickness,
                mouth.height);
        }

        internal static void AddBox(
            ICollection<Bounds> boxes,
            float xMin,
            float xMax,
            float zMin,
            float zMax,
            float yMin,
            float yMax)
        {
            float sizeX = xMax - xMin;
            float sizeY = yMax - yMin;
            float sizeZ = zMax - zMin;
            if (sizeX <= 0f || sizeY <= 0f || sizeZ <= 0f)
            {
                return;
            }

            boxes.Add(new Bounds(
                new Vector3(
                    (xMin + xMax) * 0.5f,
                    (yMin + yMax) * 0.5f,
                    (zMin + zMax) * 0.5f),
                new Vector3(sizeX, sizeY, sizeZ)));
        }

        private static Rect Expand(Rect source, float amount)
        {
            return new Rect(
                source.x - amount,
                source.y - amount,
                source.width + amount * 2f,
                source.height + amount * 2f);
        }
    }
}
