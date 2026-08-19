using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The four ropes a coffin is let down on, and nothing else.
    ///
    /// There is deliberately no timber across the mouth. Bearers are
    /// what a box is set down on beside a grave, not what it is lowered
    /// through — they would be in the way of the very thing going past
    /// them — and the blocks it waited on are already back at the foot
    /// of the plot. What is over the hole is two pairs of slings
    /// running down from the hands of men standing at the edge, so the
    /// ropes leave frame at the ground line and the mouth stays clear.
    ///
    /// The coffin's own pose is computed here too, from the same two
    /// payouts, so the box and the ropes holding it can never disagree.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CemeteryGraveSlings : MonoBehaviour
    {
        public const string RuntimeRootName = "Grave Slings";
        public const string SlingName = "Grave Sling";

        public const int SlingCount = 4;

        /// <summary>How far along the grave each pair of slings runs,
        /// either side of the middle.</summary>
        public const float SlingOffsetMeters = 0.62f;

        /// <summary>How far apart the two slings of one pair run,
        /// across the grave.</summary>
        public const float SlingSpreadMeters = 0.30f;

        public const float SlingThicknessMeters = 0.022f;

        /// <summary>
        /// How far above the grass the rope is still drawn before it
        /// is taken to be in somebody's hands. Short: the men holding
        /// it are outside the shot, and a rope that climbs any higher
        /// ends in mid-air.
        /// </summary>
        public const float SlingRiseMeters = 0.16f;

        /// <summary>Where the box starts — sitting in the mouth of the
        /// grave, taking its weight on the ropes.</summary>
        public const float RestHeightAboveGround = 0.02f;

        /// <summary>Hemp, pale enough to read against turned earth at
        /// the bottom of a hole.</summary>
        internal static readonly Color Rope =
            new Color(0.52f, 0.46f, 0.33f);

        private readonly Transform[] slings =
            new Transform[SlingCount];

        private CemeteryGravediggingPlan plan;

        public static CemeteryGraveSlings Create(
            Transform parent,
            CemeteryGravediggingPlan gravediggingPlan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (gravediggingPlan == null)
            {
                throw new ArgumentNullException(
                    nameof(gravediggingPlan));
            }

            if (!gravediggingPlan.IsPresent)
            {
                return null;
            }

            var root = new GameObject(RuntimeRootName);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(
                new Vector3(
                    gravediggingPlan.PitMouth.center.x,
                    gravediggingPlan.GroundTopY,
                    gravediggingPlan.PitMouth.center.y),
                gravediggingPlan.Heading);

            var rig = root.AddComponent<CemeteryGraveSlings>();
            rig.plan = gravediggingPlan;
            rig.BuildSlings();
            rig.SetCoffinDrop(0f, 0f);
            return rig;
        }

        /// <summary>
        /// Where the coffin's own root belongs for a given pair of
        /// payouts, in metres below the height it started at. Its model
        /// is built standing on its own floor, so this is the bottom of
        /// the box.
        /// </summary>
        public Vector3 GetCoffinPosition(
            float headDrop,
            float footDrop)
        {
            float head = GetEndY(headDrop);
            float foot = GetEndY(footDrop);
            return new Vector3(
                plan.PitMouth.center.x,
                (head + foot) * 0.5f,
                plan.PitMouth.center.y);
        }

        /// <summary>
        /// How the box hangs. The grave's local <c>+Z</c> is the head,
        /// and a positive pitch about local <c>X</c> takes <c>+Z</c>
        /// down, so a head end lower than the foot end is a positive
        /// angle.
        /// </summary>
        public Quaternion GetCoffinRotation(
            float headDrop,
            float footDrop)
        {
            float rise = GetEndY(footDrop) - GetEndY(headDrop);
            float pitch = Mathf.Atan2(
                              rise,
                              CityCemeteryCoffinWorldBuilder
                                  .LengthMeters) *
                          Mathf.Rad2Deg;
            return plan.Heading *
                   Quaternion.Euler(pitch, 0f, 0f);
        }

        /// <summary>
        /// Pays the slings out to match the two ends. Both arguments
        /// are normalized: <c>0</c> is up in the mouth of the grave,
        /// <c>1</c> is down on the floor of it.
        /// </summary>
        public void SetCoffinDrop(float headDrop, float footDrop)
        {
            for (int index = 0; index < SlingCount; index++)
            {
                Transform sling = slings[index];
                if (sling == null)
                {
                    continue;
                }

                // Slings 0 and 1 carry the head end, 2 and 3 the foot.
                bool head = index < 2;
                float lidY = GetEndY(head ? headDrop : footDrop) +
                             CityCemeteryCoffinWorldBuilder
                                 .HeightMeters;
                float top = plan.GroundTopY + SlingRiseMeters;
                float length = Mathf.Max(
                    SlingThicknessMeters,
                    top - lidY);
                float lateral = (index % 2) == 0
                    ? -SlingSpreadMeters
                    : SlingSpreadMeters;
                float along = head
                    ? SlingOffsetMeters
                    : -SlingOffsetMeters;
                sling.localPosition = new Vector3(
                    lateral,
                    SlingRiseMeters - (length * 0.5f),
                    along);
                sling.localScale = new Vector3(
                    SlingThicknessMeters,
                    length,
                    SlingThicknessMeters);
            }
        }

        /// <summary>Takes the ropes away: the box is down and they
        /// have been pulled out from under it.</summary>
        public void Dismantle()
        {
            if (Application.isPlaying)
            {
                Destroy(gameObject);
            }
            else
            {
                DestroyImmediate(gameObject);
            }
        }

        private float GetEndY(float drop)
        {
            float top = plan.GroundTopY + RestHeightAboveGround;
            return Mathf.Lerp(
                top,
                plan.PitFloorY,
                Mathf.Clamp01(drop));
        }

        private void BuildSlings()
        {
            for (int index = 0; index < SlingCount; index++)
            {
                GameObject sling = RuntimePrimitiveFactory.CreateBox(
                    $"{SlingName} {index}",
                    transform,
                    Vector3.zero,
                    Vector3.one,
                    Rope,
                    false);
                slings[index] = sling.transform;
            }
        }
    }
}
