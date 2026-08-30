using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One frame of a raven's articulation, expressed as deltas over
    /// the prefab's rest pose — the stairwell cat's grammar on a bird.
    /// Every channel is anatomical rather than world-space: the actor
    /// converts each into its pivot's parent space, which is what
    /// keeps FBX axis conversion out of the pose maths. Directions,
    /// stated once so no consumer has to guess: positive body dip
    /// sinks the body toward the feet, positive body lean rolls it
    /// about the bird's own forward axis, positive body pitch raises
    /// the breast (a landing flare), positive head yaw turns the head
    /// toward the bird's left, positive head pitch dips the beak (a
    /// preen), wing fold runs from 0 folded on the flank out to
    /// <see cref="CemeteryRavenPoseRules.WingFoldMaximumDegrees"/>
    /// deployed, positive wing flap lifts that wing's tip, and
    /// positive tail pitch lifts the tail.
    /// </summary>
    public readonly struct CemeteryRavenPose
    {
        public CemeteryRavenPose(
            float bodyDipMeters,
            float bodyLeanDegrees,
            float bodyPitchDegrees,
            float headYawDegrees,
            float headPitchDegrees,
            float wingFoldLeftDegrees,
            float wingFoldRightDegrees,
            float wingFlapLeftDegrees,
            float wingFlapRightDegrees,
            float tailPitchDegrees)
        {
            BodyDipMeters = bodyDipMeters;
            BodyLeanDegrees = bodyLeanDegrees;
            BodyPitchDegrees = bodyPitchDegrees;
            HeadYawDegrees = headYawDegrees;
            HeadPitchDegrees = headPitchDegrees;
            WingFoldLeftDegrees = wingFoldLeftDegrees;
            WingFoldRightDegrees = wingFoldRightDegrees;
            WingFlapLeftDegrees = wingFlapLeftDegrees;
            WingFlapRightDegrees = wingFlapRightDegrees;
            TailPitchDegrees = tailPitchDegrees;
        }

        public float BodyDipMeters { get; }
        public float BodyLeanDegrees { get; }
        public float BodyPitchDegrees { get; }
        public float HeadYawDegrees { get; }
        public float HeadPitchDegrees { get; }
        public float WingFoldLeftDegrees { get; }
        public float WingFoldRightDegrees { get; }
        public float WingFlapLeftDegrees { get; }
        public float WingFlapRightDegrees { get; }
        public float TailPitchDegrees { get; }
    }

    /// <summary>
    /// Pure static mapping of the raven timelines onto pivot deltas,
    /// the shape of <see cref="StairwellCatPoseRules"/>: the models
    /// decide WHEN something happens, this file decides WHAT that
    /// moment looks like, and the actor is left with nothing but
    /// transform writes. Cloned rather than shared with the cat so
    /// bird tuning never edits the cat.
    /// </summary>
    public static class CemeteryRavenPoseRules
    {
        /// <summary>
        /// The wing's whole travel from folded on the flank to flight,
        /// mirroring the generator manifest's wing_fold_max_degrees.
        /// The two must agree, or the runtime would swing a wing past
        /// the arc the geometry was modelled to sweep.
        /// </summary>
        public const float WingFoldMaximumDegrees = 70f;

        /// <summary>Flap swing at full deployment. A folded wing does
        /// not beat, so the amplitude scales with the fold — that one
        /// rule makes deploy, flight and refold a single motion.
        /// </summary>
        public const float FlightFlapAmplitudeDegrees = 40f;

        /// <summary>Breathing is millimetres: visible on a bird this
        /// small only as a faint rise, which is exactly the point.
        /// </summary>
        public const float BreatheDipAmplitudeMeters = 0.0025f;

        public const float WeightShiftLeanDegrees = 6f;

        /// <summary>The head counter-turns a little against the lean,
        /// the way a standing bird keeps its eyes level while its
        /// feet re-settle.</summary>
        public const float WeightShiftCounterHeadYawDegrees = 4f;

        public const float WingRuffleFoldDegrees = 25f;
        public const float WingRuffleFlapDegrees = 18f;
        public const float WingRuffleFlapCount = 2f;

        public const float PreenHeadPitchDegrees = 55f;

        /// <summary>
        /// How far the head turns toward the preened wing. This
        /// REPLACES the tracked yaw for the whole preen rather than
        /// adding to it: a preening raven does not follow the hero —
        /// the cat's own grooming rule, kept on purpose.
        /// </summary>
        public const float PreenHeadYawDegrees = 30f;

        public const float PreenWingLiftDegrees = 12f;

        /// <summary>Fraction of the preen spent easing in and out, so
        /// the head neither snaps down into the coverts nor snaps
        /// back up to attention.</summary>
        public const float PreenRampFraction = 0.15f;

        /// <summary>The tail trails the body pitch instead of owning
        /// a timeline of its own: one balancing animal, not two
        /// animated parts.</summary>
        public const float TailFollowFactor = 0.4f;

        /// <summary>
        /// The perched pose for one evaluated idle moment. The
        /// continuous breath rides under every kind — lungs do not
        /// wait for wings — and only the preen is allowed to discard
        /// the tracked head yaw.
        /// </summary>
        public static CemeteryRavenPose IdlePose(
            CemeteryRavenIdleKind kind,
            float eventProgress01,
            float breathe01,
            float eventSign,
            bool preenOnLeftWing,
            float headYawDegrees)
        {
            float progress = Mathf.Clamp01(eventProgress01);
            float bodyDip =
                (0.5f - Mathf.Clamp01(breathe01)) *
                2f *
                BreatheDipAmplitudeMeters;
            float bodyLean = 0f;
            float headYaw = headYawDegrees;
            float headPitch = 0f;
            float wingFoldLeft = 0f;
            float wingFoldRight = 0f;
            float wingFlapLeft = 0f;
            float wingFlapRight = 0f;

            switch (kind)
            {
                case CemeteryRavenIdleKind.WeightShift:
                {
                    // Out and back in one half sine: the weight goes
                    // over one leg and settles home again.
                    float shape = Mathf.Sin(Mathf.PI * progress);
                    bodyLean =
                        eventSign * WeightShiftLeanDegrees * shape;
                    headYaw -=
                        eventSign *
                        WeightShiftCounterHeadYawDegrees *
                        shape;
                    break;
                }

                case CemeteryRavenIdleKind.WingRuffle:
                {
                    float envelope = Mathf.Sin(Mathf.PI * progress);
                    float fold = WingRuffleFoldDegrees * envelope;
                    float flap =
                        WingRuffleFlapDegrees *
                        Mathf.Sin(
                            Mathf.PI * 2f *
                            WingRuffleFlapCount *
                            progress) *
                        envelope;
                    wingFoldLeft = fold;
                    wingFoldRight = fold;
                    wingFlapLeft = flap;
                    wingFlapRight = flap;
                    break;
                }

                case CemeteryRavenIdleKind.Preen:
                {
                    float ramp = Mathf.Clamp01(
                        Mathf.Min(progress, 1f - progress) /
                        PreenRampFraction);
                    float side = preenOnLeftWing ? 1f : -1f;
                    headPitch = PreenHeadPitchDegrees * ramp;
                    // Not the tracked yaw: the beak is in the coverts
                    // and the hero can walk where he likes.
                    headYaw = side * PreenHeadYawDegrees * ramp;
                    float lift = PreenWingLiftDegrees * ramp;
                    if (preenOnLeftWing)
                    {
                        wingFlapLeft = lift;
                    }
                    else
                    {
                        wingFlapRight = lift;
                    }

                    break;
                }
            }

            return new CemeteryRavenPose(
                bodyDip,
                bodyLean,
                0f,
                headYaw,
                headPitch,
                wingFoldLeft,
                wingFoldRight,
                wingFlapLeft,
                wingFlapRight,
                0f);
        }

        /// <summary>
        /// The airborne pose for one evaluated flight moment. The
        /// flight model owns position and timing; this map only turns
        /// its four numbers into wings, body and tail. The head stays
        /// neutral in the air — a flying bird looks where it goes.
        /// </summary>
        public static CemeteryRavenPose FlightPose(
            float wingFold01,
            float flapPhaseRadians,
            float bodyPitchDegrees,
            float bodyDipMeters)
        {
            float fold01 = Mathf.Clamp01(wingFold01);
            float fold = fold01 * WingFoldMaximumDegrees;
            float flap =
                Mathf.Sin(flapPhaseRadians) *
                FlightFlapAmplitudeDegrees *
                fold01;
            return new CemeteryRavenPose(
                bodyDipMeters,
                0f,
                bodyPitchDegrees,
                0f,
                0f,
                fold,
                fold,
                flap,
                flap,
                bodyPitchDegrees * TailFollowFactor);
        }
    }
}
