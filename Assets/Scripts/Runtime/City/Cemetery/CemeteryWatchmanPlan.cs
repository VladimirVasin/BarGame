using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Where the watchman stands and which way he looks:
    /// derived entirely from the cemetery plan's own lodge and gate
    /// parts, so the drawn booth and the NPC can never disagree.</summary>
    public readonly struct CemeteryWatchmanStance
    {
        public CemeteryWatchmanStance(
            Vector3 position,
            Vector3 facing,
            int paletteVariant,
            float playbackSpeed,
            float phaseOffsetSeconds)
        {
            Position = position;
            Facing = facing;
            PaletteVariant = paletteVariant;
            PlaybackSpeed = playbackSpeed;
            PhaseOffsetSeconds = phaseOffsetSeconds;
        }

        public Vector3 Position { get; }
        public Vector3 Facing { get; }
        public int PaletteVariant { get; }
        public float PlaybackSpeed { get; }
        public float PhaseOffsetSeconds { get; }
    }

    /// <summary>
    /// The authored population of the cemetery gate lodge: one snide
    /// old watchman at his window post, eyes on the gate. Absent when
    /// the blueprint has no cemetery or the cemetery plan carries no
    /// lodge (a gate too close to a lateral edge skips the booth).
    /// </summary>
    public sealed class CemeteryWatchmanPlan
    {
        /// <summary>How far off the lodge base's edge, on the window
        /// side, the watchman holds his post.</summary>
        public const float WindowStandOffMeters = 0.55f;

        private const string LodgeBaseId = "cemetery-lodge-base";
        private const string LodgeWindowBoardId =
            "cemetery-lodge-window-board";
        private const string GateArchId = "cemetery-gate-arch";

        private static readonly CemeteryWatchmanPlan AbsentPlan =
            new CemeteryWatchmanPlan(default, false);

        private CemeteryWatchmanPlan(
            CemeteryWatchmanStance stance,
            bool isPresent)
        {
            Stance = stance;
            IsPresent = isPresent;
        }

        public CemeteryWatchmanStance Stance { get; }
        public bool IsPresent { get; }

        public static CemeteryWatchmanPlan Create(
            CityCemeteryPlan cemeteryPlan)
        {
            if (cemeteryPlan == null)
            {
                return AbsentPlan;
            }

            if (!TryFindPart(
                    cemeteryPlan,
                    LodgeBaseId,
                    out CityCemeteryPartDescriptor lodgeBase) ||
                !TryFindPart(
                    cemeteryPlan,
                    LodgeWindowBoardId,
                    out CityCemeteryPartDescriptor windowBoard))
            {
                return AbsentPlan;
            }

            // Out of the booth toward its watch window: standing
            // inside would bury him in the batched wall collider and
            // starve his attention magnet.
            Vector3 facingOut =
                windowBoard.Center - lodgeBase.Center;
            facingOut.y = 0f;
            if (facingOut.sqrMagnitude < 0.0001f)
            {
                return AbsentPlan;
            }

            facingOut = facingOut.normalized;
            float halfExtentAlongFacing = Mathf.Abs(Vector3.Dot(
                new Vector3(
                    lodgeBase.Size.x * 0.5f,
                    0f,
                    lodgeBase.Size.z * 0.5f),
                new Vector3(
                    Mathf.Abs(facingOut.x),
                    0f,
                    Mathf.Abs(facingOut.z))));
            Vector3 position = lodgeBase.Center +
                facingOut * (halfExtentAlongFacing +
                             WindowStandOffMeters);
            position.y = cemeteryPlan.GroundTopY;

            // He literally watches the gate; a plan without the arch
            // still leaves him facing away from his own wall.
            Vector3 facing = facingOut;
            if (TryFindPart(
                    cemeteryPlan,
                    GateArchId,
                    out CityCemeteryPartDescriptor arch))
            {
                Vector3 toArch = arch.Center - position;
                toArch.y = 0f;
                if (toArch.sqrMagnitude > 0.0001f)
                {
                    facing = toArch.normalized;
                }
            }

            return new CemeteryWatchmanPlan(
                new CemeteryWatchmanStance(
                    position,
                    facing,
                    1,
                    1f,
                    0f),
                true);
        }

        private static bool TryFindPart(
            CityCemeteryPlan plan,
            string stableId,
            out CityCemeteryPartDescriptor part)
        {
            for (int index = 0; index < plan.Parts.Count; index++)
            {
                if (string.Equals(
                        plan.Parts[index].StableId,
                        stableId,
                        StringComparison.Ordinal))
                {
                    part = plan.Parts[index];
                    return true;
                }
            }

            part = default;
            return false;
        }
    }
}
