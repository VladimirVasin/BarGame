using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Where the watchman stands and which way he looks:
    /// derived entirely from the cemetery plan's own lodge and gate
    /// parts, so the drawn booth and the NPC can never disagree. He
    /// keeps his post on the doorstep, under the porch bulb the
    /// planner hangs over the same doorway.</summary>
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
    /// old watchman on his own doorstep, eyes on the main alley every
    /// visitor walks up from the gate. Absent when
    /// the blueprint has no cemetery or the cemetery plan carries no
    /// lodge (a gate too close to a lateral edge skips the booth).
    /// </summary>
    public sealed class CemeteryWatchmanPlan
    {
        /// <summary>How far past the doorstep, along the door's own
        /// normal, the watchman holds his post: clear of the leaf
        /// swung ajar in front of him, still inside the porch bulb's
        /// pool of light.</summary>
        public const float DoorStandOffMeters = 0.75f;

        /// <summary>And how far he steps off the doorway's own line
        /// toward the main alley: enough to stand in front of the open
        /// leaf rather than behind it, and to put the bulb beside the
        /// door over his shoulder instead of at his back.</summary>
        public const float AlleyStepMeters = 0.30f;

        private const string LodgeBaseId = "cemetery-lodge-base";
        private const string LodgeStepId = "cemetery-lodge-step";
        private const string LodgeRearWallId = "cemetery-lodge-wall-rear";
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
                    LodgeStepId,
                    out CityCemeteryPartDescriptor step) ||
                !TryFindPart(
                    cemeteryPlan,
                    LodgeRearWallId,
                    out CityCemeteryPartDescriptor rearWall))
            {
                return AbsentPlan;
            }

            // Out of the booth through its doorway: the rear wall is a
            // slab, so the axis it is thin along is the door's normal,
            // signed away from the booth's middle. Standing inside
            // would bury him in the batched wall collider and starve
            // his attention magnet.
            Vector3 outOfBooth = step.Center - lodgeBase.Center;
            outOfBooth.y = 0f;
            if (outOfBooth.sqrMagnitude < 0.0001f)
            {
                return AbsentPlan;
            }

            Vector3 facingOut = rearWall.Size.x < rearWall.Size.z
                ? Vector3.right
                : Vector3.forward;
            if (Vector3.Dot(facingOut, outOfBooth) < 0f)
            {
                facingOut = -facingOut;
            }

            // Which way the main alley lies, along the wall: the gate
            // arch stands on it, so the arch direction with the door's
            // own normal projected out of it is the alley heading. A
            // plan without the arch leaves him looking straight out of
            // his door rather than into a wall.
            Vector3 alleyward = Vector3.zero;
            if (TryFindPart(
                    cemeteryPlan,
                    GateArchId,
                    out CityCemeteryPartDescriptor arch))
            {
                Vector3 toArch = arch.Center - lodgeBase.Center;
                toArch.y = 0f;
                alleyward = toArch -
                            facingOut * Vector3.Dot(toArch, facingOut);
            }

            if (alleyward.sqrMagnitude < 0.0001f)
            {
                alleyward = Vector3.zero;
            }
            else
            {
                alleyward = alleyward.normalized;
            }

            // Measured off the step, not the wall: the doorstep is the
            // one lodge part that already carries the doorway's own
            // lateral line. Then one step along the wall toward the
            // alley, so he stands in front of the leaf swung ajar
            // instead of behind it.
            Vector3 position = new Vector3(
                step.Center.x,
                cemeteryPlan.GroundTopY,
                step.Center.z) +
                facingOut * DoorStandOffMeters +
                alleyward * AlleyStepMeters;

            // He watches the alley, the road everyone who comes in
            // walks up — not the gate arch behind his own booth.
            Vector3 facing = alleyward.sqrMagnitude > 0.5f
                ? alleyward
                : facingOut;

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
