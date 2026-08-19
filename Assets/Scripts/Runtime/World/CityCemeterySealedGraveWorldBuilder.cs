using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The finished grave: the earth heaped back over the coffin and
    /// one of the cemetery's own monuments standing at the head of it.
    /// The hole itself is gone by the time this is built — the slab
    /// was made whole again — so nothing here lines anything, it only
    /// stands on ground.
    ///
    /// The stone is not a new silhouette: it comes out of
    /// <see cref="CityCemeteryPlanner.CreateGraveParts"/> and batches
    /// through the same path as the standing rows, so the hero's own
    /// grave is materially one of them. What it does not take from
    /// there is the slab: a stone slab over the plot is what a family
    /// lays years later, and this grave is an hour old, so the fresh
    /// mound stands in its place.
    /// </summary>
    public static class CityCemeterySealedGraveWorldBuilder
    {
        public const string RootName = "Sealed Grave";
        public const string MoundName = "Grave Mound";
        /// <summary>
        /// The monument as an object in the world: the mover whose
        /// pivot every pose of it turns about. Its batched parts hang
        /// under it with a name of their own, so a lookup for "the
        /// monument" finds the thing that can be moved rather than the
        /// geometry inside it.
        /// </summary>
        public const string StoneName = "Grave Monument";
        public const string StonePartsName = "Grave Monument Parts";
        public const string LyingStoneName = "Grave Monument Lying";

        /// <summary>
        /// The mound covers the body and stops short of the head, so
        /// the monument stands on flat ground the way every other one
        /// in this yard does.
        /// </summary>
        public const float MoundLengthMeters = 1.62f;
        public const float MoundWidthMeters = 0.98f;
        public const float MoundFootShiftMeters = 0.30f;

        /// <summary>
        /// Ordinal reserved for a grave the hero dug himself. Well
        /// clear of the planner's own count, so a part id never reads
        /// as one of the standing rows.
        /// </summary>
        public const int GraveOrdinal = 999;

        /// <summary>
        /// Course by course, each smaller than the last, turned off
        /// the grave's own axis and shoved off the one below it. Both
        /// of those matter: a stack of centred courses is a step
        /// pyramid, and a gravedigger with a spade does not build step
        /// pyramids. Scales are given along the grave and across it
        /// separately, so the heap stays a ridge over the body rather
        /// than narrowing to a point.
        /// </summary>
        private static readonly float[] CourseHeights =
            { 0.15f, 0.10f, 0.06f };
        private static readonly Vector2[] CourseScales =
        {
            new Vector2(1.00f, 1.00f),
            new Vector2(0.86f, 0.70f),
            new Vector2(0.58f, 0.44f)
        };
        private static readonly Vector2[] CourseOffsets =
        {
            new Vector2(0f, 0f),
            new Vector2(0.06f, -0.07f),
            new Vector2(-0.05f, 0.09f)
        };
        private static readonly float[] CourseYawDegrees =
            { 0f, -8f, 11f };

        public static GameObject Build(
            Transform parent,
            CemeteryGravediggingPlan plan)
        {
            GameObject root = BuildMound(parent, plan);
            if (root != null)
            {
                BuildStandingStone(root.transform, plan);
            }

            return root;
        }

        /// <summary>
        /// The monument on its foot, seated by the same measurement
        /// that seats it while it is being heaved up. That is the whole
        /// point of routing it through here: a stone placed by its
        /// authored parts and a stone placed by its own bounds do not
        /// land in the same spot, and the difference shows as a jump —
        /// or a float — at the instant the act commits.
        /// </summary>
        public static GameObject BuildStandingStone(
            Transform parent,
            CemeteryGravediggingPlan plan)
        {
            GameObject mover = BuildLyingStone(parent, plan);
            if (mover != null)
            {
                mover.name = StoneName;
                ApplyLyingPose(mover.transform, plan, 0f);
            }

            return mover;
        }

        /// <summary>
        /// The closed grave without its stone: what stands on the plot
        /// between the last spadeful and the moment the monument is
        /// set. Those are two rungs of the work, so they are two
        /// builds.
        /// </summary>
        public static GameObject BuildMound(
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

            var courses =
                new List<RuntimeOrientedBox>(CourseHeights.Length);
            AppendMound(courses, plan);
            GameObject mound =
                RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                    MoundName,
                    root,
                    courses,
                    CityCemeteryPitWorldBuilder.FreshEarth,
                    false,
                    CityCemeterySurfaceAppearance.GetRecipe(
                        CityCemeterySurfaceKind.Soil).MetersPerTile,
                    RuntimeWorldUvMode.BoxProjected);
            CityCemeterySurfaceAppearance.ApplyCombined(
                mound.GetComponent<Renderer>(),
                CityCemeterySurfaceKind.Soil,
                CityCemeteryPitWorldBuilder.FreshEarth);
            return root.gameObject;
        }

        /// <summary>
        /// The monument alone, batched exactly as the standing rows
        /// are. It is built under its own root so the setting of it
        /// can be carried, tilted and tamped before it is left there.
        /// </summary>
        public static GameObject BuildStone(
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

            Transform root = new GameObject(StonePartsName).transform;
            root.SetParent(parent, false);
            CityCemeteryWorldBuilder.BuildPartBatches(
                root,
                CreateMonumentParts(plan),
                "Grave Stone");
            return root.gameObject;
        }

        /// <summary>
        /// The monument on its back, waiting to be stood up.
        ///
        /// It is the same stone, built the same way, under a mover
        /// whose pivot is the anchor the parts were authored around —
        /// so tipping it is one rotation and standing it up later is
        /// the same rotation run backwards. Where it comes to rest
        /// vertically is measured rather than assumed: the four
        /// silhouettes are different heights and a cross is mostly
        /// air, so only its own bounds know where its back is.
        /// </summary>
        public static GameObject BuildLyingStone(
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

            var mover = new GameObject(LyingStoneName);
            mover.transform.SetParent(parent, false);
            GameObject stone = BuildStone(mover.transform, plan);
            if (stone == null)
            {
                return mover;
            }

            stone.transform.localPosition = -GetStoneAnchor(plan);
            ApplyLyingPose(mover.transform, plan, 1f);
            return mover;
        }

        /// <summary>
        /// Puts a stone mover somewhere between flat on the grass and
        /// standing on its foot. <paramref name="lying01"/> is
        /// <c>1</c> for down and <c>0</c> for up, and the ground
        /// clearance is re-measured on the way so it neither floats
        /// nor sinks through the turf as it comes round.
        /// </summary>
        public static void ApplyLyingPose(
            Transform mover,
            CemeteryGravediggingPlan plan,
            float lying01)
        {
            if (mover == null)
            {
                throw new ArgumentNullException(nameof(mover));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            float amount = Mathf.Clamp01(lying01);
            Vector3 anchor = GetStoneAnchor(plan);
            Vector3 seat = Vector3.Lerp(
                anchor,
                plan.StoneRestGround,
                amount);
            Quaternion upright = Quaternion.identity;
            Quaternion flat = Quaternion.Euler(
                                  0f,
                                  plan.StoneRestYawDegrees,
                                  0f) *
                              Quaternion.Euler(-90f, 0f, 0f);
            mover.SetPositionAndRotation(
                seat,
                Quaternion.Slerp(upright, flat, amount));
            mover.position += new Vector3(
                0f,
                plan.GroundTopY - MeasureLowestPoint(mover),
                0f);
        }

        /// <summary>
        /// The point the monument's parts are authored around, which
        /// is what every pose of it turns about.
        /// </summary>
        public static Vector3 GetStoneAnchor(
            CemeteryGravediggingPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            return new Vector3(
                plan.Ground.x,
                plan.GroundTopY,
                plan.Ground.z);
        }

        private static float MeasureLowestPoint(Transform mover)
        {
            Renderer[] renderers =
                mover.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return mover.position.y;
            }

            float lowest = float.MaxValue;
            for (int index = 0; index < renderers.Length; index++)
            {
                // The plaque rides the stone; it is not part of what
                // decides where the stone's foot is.
                if (renderers[index]
                    .GetComponentInParent<CemeteryPlaqueSurface>() !=
                    null)
                {
                    continue;
                }

                lowest = Mathf.Min(
                    lowest,
                    renderers[index].bounds.min.y);
            }

            return lowest == float.MaxValue
                ? mover.position.y
                : lowest;
        }

        /// <summary>
        /// The monument alone, without the grave slab the planner
        /// would put under it.
        /// </summary>
        public static IReadOnlyList<CityCemeteryPartDescriptor>
            CreateMonumentParts(CemeteryGravediggingPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            IReadOnlyList<CityCemeteryPartDescriptor> planned =
                CityCemeteryPlanner.CreateGraveParts(
                    GraveOrdinal,
                    new Vector3(
                        plan.Ground.x,
                        plan.GroundTopY,
                        plan.Ground.z),
                    plan.Heading,
                    plan.Monument,
                    plan.MonumentStyle,
                    // Fresh work stands straight, and nobody has put a
                    // fence or a wreath on it yet.
                    0f,
                    false,
                    false);
            var parts =
                new List<CityCemeteryPartDescriptor>(planned.Count);
            for (int index = 0; index < planned.Count; index++)
            {
                if (planned[index].Kind !=
                    CityCemeteryPartKind.GraveSlab)
                {
                    parts.Add(planned[index]);
                }
            }

            return parts;
        }

        private static void AppendMound(
            ICollection<RuntimeOrientedBox> courses,
            CemeteryGravediggingPlan plan)
        {
            // Everything below is stated in the grave's own frame —
            // x across it, z along it toward the head — and turned
            // into the world by the plan's heading.
            Vector3 seat = plan.Heading * new Vector3(
                0f,
                0f,
                -MoundFootShiftMeters);
            var center = new Vector3(
                plan.Ground.x + seat.x,
                plan.GroundTopY,
                plan.Ground.z + seat.z);
            float bottom = 0f;
            for (int course = 0;
                 course < CourseHeights.Length;
                 course++)
            {
                Vector2 scale = CourseScales[course];
                float height = CourseHeights[course];
                Vector2 offset = CourseOffsets[course];
                Vector3 local = plan.Heading * new Vector3(
                    offset.x,
                    0f,
                    offset.y);
                courses.Add(new RuntimeOrientedBox(
                    new Vector3(
                        center.x + local.x,
                        center.y + bottom + height * 0.5f,
                        center.z + local.z),
                    plan.Heading * Quaternion.Euler(
                        0f,
                        CourseYawDegrees[course],
                        0f),
                    new Vector3(
                        MoundWidthMeters * scale.y,
                        height,
                        MoundLengthMeters * scale.x)));
                bottom += height;
            }
        }
    }
}
