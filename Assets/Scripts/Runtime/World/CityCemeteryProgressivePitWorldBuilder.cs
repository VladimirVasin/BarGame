using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The hole halfway through being made, and halfway through being
    /// unmade again.
    ///
    /// The trick is the same one that makes a finished grave possible,
    /// run in reverse. The ground slab gives up the whole rectangle at
    /// the first cut of the spade, so the void is there from the
    /// start; what stands in it is earth this builder puts back — one
    /// block per segment of the lattice, reaching from the floor up to
    /// however far down that segment has been taken. Dig a course and
    /// its block gets shorter. Fill a course and it gets taller. The
    /// slab itself is never touched again until the grave closes.
    ///
    /// That is why a spadeful costs one small combined mesh instead of
    /// a rebuild of the cemetery's whole ground.
    /// </summary>
    public static class CityCemeteryProgressivePitWorldBuilder
    {
        public const string RootName = "Grave Being Dug";
        public const string EarthName = "Grave Working Earth";

        /// <summary>
        /// How deep one course is. Three of them share the pit, so the
        /// last course of every segment lands exactly on the floor and
        /// no sliver of earth is left standing on it.
        /// </summary>
        public static float CourseDepthMeters =>
            CemeteryGravediggingPlan.PitDepthMeters /
            CemeteryGraveLatticeModel.CoursesPerSegment;

        public static GameObject Build(
            Transform parent,
            CemeteryGravediggingPlan plan,
            CemeteryGraveLatticeModel lattice)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (lattice == null)
            {
                throw new ArgumentNullException(nameof(lattice));
            }

            if (!plan.IsPresent)
            {
                return null;
            }

            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);

            var boxes = new List<Bounds>(16);
            CityCemeteryPitWorldBuilder.AppendCollar(boxes, plan);
            CityCemeteryPitWorldBuilder.AppendFloor(boxes, plan);
            AppendSegments(boxes, plan, lattice);
            CityCemeteryPitWorldBuilder.AppendSpoil(
                boxes,
                plan,
                GetHeapFullness(lattice));

            GameObject earth =
                RuntimePrimitiveFactory.CreateCombinedBoxes(
                    EarthName,
                    root,
                    boxes,
                    CityCemeteryPitWorldBuilder.FreshEarth,
                    true,
                    CityCemeterySurfaceAppearance.GetRecipe(
                        CityCemeterySurfaceKind.Soil).MetersPerTile,
                    RuntimeWorldUvMode.BoxProjected);
            CityCemeterySurfaceAppearance.ApplyCombined(
                earth.GetComponent<Renderer>(),
                CityCemeterySurfaceKind.Soil,
                CityCemeteryPitWorldBuilder.FreshEarth);

            CityCemeteryPitWorldBuilder.AppendMouthGuard(root, plan);
            return root.gameObject;
        }

        /// <summary>
        /// The patch of mouth one segment of the lattice owns. The
        /// lattice counts along the grave and across it; the mouth is
        /// axis-aligned, so which world axis that is depends on the
        /// plan's snapped heading and nothing else.
        /// </summary>
        public static Rect GetSegmentRect(
            CemeteryGravediggingPlan plan,
            int segment)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            RequireSegment(segment);
            Rect mouth = plan.PitMouth;
            int along =
                segment / CemeteryGraveLatticeModel.SegmentsAcross;
            int across =
                segment % CemeteryGraveLatticeModel.SegmentsAcross;
            if (plan.RunsAlongX)
            {
                float alongSize = mouth.width /
                                  CemeteryGraveLatticeModel
                                      .SegmentsAlong;
                float acrossSize = mouth.height /
                                   CemeteryGraveLatticeModel
                                       .SegmentsAcross;
                return Rect.MinMaxRect(
                    mouth.xMin + (along * alongSize),
                    mouth.yMin + (across * acrossSize),
                    mouth.xMin + ((along + 1) * alongSize),
                    mouth.yMin + ((across + 1) * acrossSize));
            }

            float alongDepth = mouth.height /
                               CemeteryGraveLatticeModel
                                   .SegmentsAlong;
            float acrossWidth = mouth.width /
                                CemeteryGraveLatticeModel
                                    .SegmentsAcross;
            return Rect.MinMaxRect(
                mouth.xMin + (across * acrossWidth),
                mouth.yMin + (along * alongDepth),
                mouth.xMin + ((across + 1) * acrossWidth),
                mouth.yMin + ((along + 1) * alongDepth));
        }

        /// <summary>
        /// Where the blade meets this segment: the middle of its patch
        /// at the height the working face currently stands. This is
        /// what the spade is aimed at and what the camera looks down
        /// on.
        /// </summary>
        public static Vector3 GetSegmentFace(
            CemeteryGravediggingPlan plan,
            CemeteryGraveLatticeModel lattice,
            int segment)
        {
            if (lattice == null)
            {
                throw new ArgumentNullException(nameof(lattice));
            }

            Rect patch = GetSegmentRect(plan, segment);
            return new Vector3(
                patch.center.x,
                GetSegmentFaceY(plan, lattice, segment),
                patch.center.y);
        }

        /// <summary>
        /// Height of the earth standing in one segment. Digging takes
        /// it down from the grass; filling brings it up from the
        /// floor.
        /// </summary>
        public static float GetSegmentFaceY(
            CemeteryGravediggingPlan plan,
            CemeteryGraveLatticeModel lattice,
            int segment)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (lattice == null)
            {
                throw new ArgumentNullException(nameof(lattice));
            }

            RequireSegment(segment);
            float worked = lattice.GetCoursesDone(segment) *
                           CourseDepthMeters;
            return lattice.Mode == CemeteryGraveLatticeMode.Filling
                ? plan.PitFloorY + worked
                : plan.GroundTopY - worked;
        }

        /// <summary>
        /// How much of the heap is standing beside the hole. What
        /// comes out of the ground goes onto it, and what goes back
        /// into the ground comes off it, so the two are one number
        /// read from opposite ends.
        /// </summary>
        public static float GetHeapFullness(
            CemeteryGraveLatticeModel lattice)
        {
            if (lattice == null)
            {
                throw new ArgumentNullException(nameof(lattice));
            }

            return lattice.Mode == CemeteryGraveLatticeMode.Filling
                ? 1f - lattice.Progress01
                : lattice.Progress01;
        }

        /// <summary>
        /// The earth still standing in each segment. Both acts state
        /// it the same way — a block from the floor up to the working
        /// face — because digging lowers that face from the grass and
        /// filling raises it from the floor. A segment with nothing
        /// left to do contributes a box of no height and is dropped.
        /// </summary>
        private static void AppendSegments(
            ICollection<Bounds> boxes,
            CemeteryGravediggingPlan plan,
            CemeteryGraveLatticeModel lattice)
        {
            for (int segment = 0;
                 segment < CemeteryGraveLatticeModel.SegmentCount;
                 segment++)
            {
                Rect patch = GetSegmentRect(plan, segment);
                CityCemeteryPitWorldBuilder.AddBox(
                    boxes,
                    patch.xMin,
                    patch.xMax,
                    patch.yMin,
                    patch.yMax,
                    plan.PitFloorY,
                    GetSegmentFaceY(plan, lattice, segment));
            }
        }

        private static void RequireSegment(int segment)
        {
            if (segment < 0 ||
                segment >= CemeteryGraveLatticeModel.SegmentCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(segment));
            }
        }
    }
}
