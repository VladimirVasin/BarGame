using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The outline round the segment the spade is aimed at.
    ///
    /// The lattice is on the panel, but the hole is what the hero is
    /// looking at, and six identical patches of earth are six
    /// identical patches of earth. Without a frame down there the map
    /// is a puzzle to be solved against the world rather than a
    /// picture of it — which is exactly what filling in became once
    /// its timing bar went away and choosing the square was the whole
    /// act.
    ///
    /// One frame is built and then moved: every segment of a lattice
    /// is the same size, so there is nothing to rebuild when the
    /// choice changes.
    /// </summary>
    public static class CityCemeterySegmentFrameWorldBuilder
    {
        public const string RootName = "Grave Segment Frame";

        /// <summary>Thin enough to read as a chalk line rather than a
        /// kerb.</summary>
        public const float RailWidthMeters = 0.035f;
        public const float RailHeightMeters = 0.012f;

        /// <summary>How far the frame floats over the working face, so
        /// it never fights the earth for the same pixels.</summary>
        public const float HoverMeters = 0.018f;

        /// <summary>
        /// The same chalk-white the plot is marked out in when the job
        /// is first taken: one colour for "this is the bit you are
        /// working on", wherever it appears.
        /// </summary>
        internal static readonly Color FrameColor =
            CemeteryGraveDigSiteInteraction.MarkColor;

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

            Rect patch =
                CityCemeteryProgressivePitWorldBuilder
                    .GetSegmentRect(plan, 0);
            var root = new GameObject(RootName);
            root.transform.SetParent(parent, false);

            // Two rails along and two across, each pulled in by its own
            // width so the corners meet instead of overlapping.
            AddRail(
                root.transform,
                new Vector3(0f, 0f, (patch.height - RailWidthMeters) * 0.5f),
                new Vector3(patch.width, RailHeightMeters, RailWidthMeters));
            AddRail(
                root.transform,
                new Vector3(0f, 0f, -(patch.height - RailWidthMeters) * 0.5f),
                new Vector3(patch.width, RailHeightMeters, RailWidthMeters));
            AddRail(
                root.transform,
                new Vector3((patch.width - RailWidthMeters) * 0.5f, 0f, 0f),
                new Vector3(
                    RailWidthMeters,
                    RailHeightMeters,
                    patch.height - (RailWidthMeters * 2f)));
            AddRail(
                root.transform,
                new Vector3(-(patch.width - RailWidthMeters) * 0.5f, 0f, 0f),
                new Vector3(
                    RailWidthMeters,
                    RailHeightMeters,
                    patch.height - (RailWidthMeters * 2f)));
            return root;
        }

        /// <summary>
        /// Lays the frame over one segment at the height its earth
        /// currently stands. Both acts use it: digging drops it as the
        /// face goes down, filling raises it as the face comes up.
        /// </summary>
        public static void Place(
            Transform frame,
            CemeteryGravediggingPlan plan,
            CemeteryGraveLatticeModel lattice,
            int segment)
        {
            if (frame == null)
            {
                return;
            }

            if (segment < 0)
            {
                frame.gameObject.SetActive(false);
                return;
            }

            frame.gameObject.SetActive(true);
            Vector3 face =
                CityCemeteryProgressivePitWorldBuilder.GetSegmentFace(
                    plan,
                    lattice,
                    segment);
            frame.position = new Vector3(
                face.x,
                face.y + HoverMeters,
                face.z);
        }

        private static void AddRail(
            Transform parent,
            Vector3 localPosition,
            Vector3 size)
        {
            GameObject rail = RuntimePrimitiveFactory.CreateBox(
                "Segment Rail",
                parent,
                localPosition,
                size,
                FrameColor,
                CityNightResources.EmissiveMaterial,
                false);
            Renderer created = rail.GetComponent<Renderer>();
            created.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            created.receiveShadows = false;
        }
    }
}
