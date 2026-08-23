using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One frame of the cat's articulation, expressed as deltas over
    /// the prefab's rest pose. Angles are world-axis rotations the
    /// actor converts into each pivot's parent space: yaw about world
    /// up, pitch and ear tilts about the cat's own right (positive
    /// pitch dips the muzzle), roll and tail swings about the cat's
    /// own forward.
    /// </summary>
    public readonly struct StairwellCatPose
    {
        public StairwellCatPose(
            float chestScale,
            float headLiftMeters,
            float headYawDegrees,
            float headPitchDegrees,
            float headRollDegrees,
            float earLeftTiltDegrees,
            float earRightTiltDegrees,
            float tailSwing01Degrees,
            float tailSwing02Degrees,
            float tailSwing03Degrees)
        {
            ChestScale = chestScale;
            HeadLiftMeters = headLiftMeters;
            HeadYawDegrees = headYawDegrees;
            HeadPitchDegrees = headPitchDegrees;
            HeadRollDegrees = headRollDegrees;
            EarLeftTiltDegrees = earLeftTiltDegrees;
            EarRightTiltDegrees = earRightTiltDegrees;
            TailSwing01Degrees = tailSwing01Degrees;
            TailSwing02Degrees = tailSwing02Degrees;
            TailSwing03Degrees = tailSwing03Degrees;
        }

        public float ChestScale { get; }
        public float HeadLiftMeters { get; }
        public float HeadYawDegrees { get; }
        public float HeadPitchDegrees { get; }
        public float HeadRollDegrees { get; }
        public float EarLeftTiltDegrees { get; }
        public float EarRightTiltDegrees { get; }
        public float TailSwing01Degrees { get; }
        public float TailSwing02Degrees { get; }
        public float TailSwing03Degrees { get; }
    }

    /// <summary>
    /// Pure static mapping of the untouched idle/feeding timelines
    /// onto 3D pivot deltas. The frame indices and their timing come
    /// from StairwellCatIdleModel and StairwellCatFeedingTimeline
    /// exactly as they did for the atlas rows; only the presentation
    /// of a frame changed.
    /// </summary>
    public static class StairwellCatPoseRules
    {
        /// <summary>Chest scale per breathe frame 0..3; the idle
        /// model's ping-pong walks these up and back down.</summary>
        public static readonly float[] BreatheChestScales =
        {
            1.000f,
            1.010f,
            1.022f,
            1.030f
        };

        public const float BreatheHeadLiftPerFrameMeters = 0.0015f;
        public const float RestChestScale = 1.015f;

        public const float TailFlickBaseDegrees = 14f;
        public const float TailFlickMidDegrees = 22f;
        public const float TailFlickTipDegrees = 30f;
        public const float TailFlickReturnFactor = -0.7f;

        public const float EarTwitchBackDegrees = -18f;
        public const float EarTwitchReleaseDegrees = -4f;

        public const float GroomPitchPerFrameDegrees = 12f;
        public const float GroomMaxPitchDegrees = 35f;
        public const float GroomRollDegrees = 8f;

        public const float FeedingPitchDegrees = 38f;
        public const float FeedingChewDegrees = 4f;
        public const float FeedingEarsBackDegrees = -8f;
        public const int FeedingDipFrames = 3;
        public const int FeedingLiftFrame = 14;

        /// <summary>The over-shoulder cap: from the MiddleFlight shot
        /// the camera sits ~137 degrees behind the cat's muzzle, so
        /// the grin turn must comfortably exceed the 65 degree
        /// tracking clamp - the owlish excess is the point.</summary>
        public const float MaxGrinYawDegrees = 150f;
        public const float GrinPitchDegrees = -6f;

        public static StairwellCatPose IdlePose(
            StairwellCatIdleKind kind,
            int frame,
            float headYawDegrees)
        {
            float chestScale = RestChestScale;
            float headLift = 0f;
            float headPitch = 0f;
            float headRoll = 0f;
            float headYaw = headYawDegrees;
            float earLeft = 0f;
            float earRight = 0f;
            float tail01 = 0f;
            float tail02 = 0f;
            float tail03 = 0f;

            switch (kind)
            {
                case StairwellCatIdleKind.Breathe:
                {
                    int breatheFrame = Mathf.Clamp(
                        frame - StairwellCatIdleModel.BreatheFirstFrame,
                        0,
                        BreatheChestScales.Length - 1);
                    chestScale = BreatheChestScales[breatheFrame];
                    headLift =
                        breatheFrame * BreatheHeadLiftPerFrameMeters;
                    break;
                }

                case StairwellCatIdleKind.TailFlick:
                {
                    bool outStroke =
                        frame == StairwellCatIdleModel.TailFirstFrame;
                    float factor = outStroke
                        ? 1f
                        : TailFlickReturnFactor;
                    tail01 = TailFlickBaseDegrees * factor;
                    tail02 = TailFlickMidDegrees * factor;
                    tail03 = TailFlickTipDegrees * factor;
                    break;
                }

                case StairwellCatIdleKind.EarTwitch:
                {
                    earLeft =
                        frame == StairwellCatIdleModel.EarFirstFrame
                            ? EarTwitchBackDegrees
                            : EarTwitchReleaseDegrees;
                    break;
                }

                case StairwellCatIdleKind.Groom:
                {
                    int groomFrame = Mathf.Clamp(
                        frame,
                        0,
                        StairwellCatIdleModel.GroomFrameCount - 1);
                    headPitch = Mathf.Min(
                        GroomMaxPitchDegrees,
                        groomFrame * GroomPitchPerFrameDegrees);
                    headRoll = (groomFrame & 1) == 0
                        ? GroomRollDegrees
                        : -GroomRollDegrees;
                    // A grooming cat does not track the player.
                    headYaw = 0f;
                    break;
                }
            }

            return new StairwellCatPose(
                chestScale,
                headLift,
                headYaw,
                headPitch,
                headRoll,
                earLeft,
                earRight,
                tail01,
                tail02,
                tail03);
        }

        public static StairwellCatPose FeedingPose(int frameIndex)
        {
            int frame = Mathf.Clamp(
                frameIndex,
                0,
                StairwellCatFeedingTimeline.FrameCount - 1);
            float pitch;
            if (frame < FeedingDipFrames)
            {
                pitch = FeedingPitchDegrees *
                    (frame + 1) / (FeedingDipFrames + 1f);
            }
            else if (frame >= FeedingLiftFrame)
            {
                int liftStep = frame - FeedingLiftFrame;
                pitch = FeedingPitchDegrees *
                    (1f - (liftStep + 1) * 0.45f);
            }
            else
            {
                pitch = FeedingPitchDegrees +
                    ((frame & 1) == 0
                        ? -FeedingChewDegrees * 0.5f
                        : FeedingChewDegrees);
            }

            return new StairwellCatPose(
                1.01f,
                0f,
                0f,
                pitch,
                0f,
                FeedingEarsBackDegrees,
                FeedingEarsBackDegrees,
                0f,
                0f,
                0f);
        }

        /// <summary>
        /// Blends the head from wherever the base pose put it toward
        /// the over-shoulder camera-facing turn as the grin draws
        /// itself in. Everything below the neck keeps the base pose:
        /// the body stays a calm sitting cat while the head does the
        /// impossible thing.
        /// </summary>
        public static StairwellCatPose ComposeGrin(
            in StairwellCatPose basePose,
            float grinWeight,
            float cameraYawDegrees)
        {
            float weight = Mathf.Clamp01(grinWeight);
            if (weight <= 0f)
            {
                return basePose;
            }

            float targetYaw = Mathf.Clamp(
                cameraYawDegrees,
                -MaxGrinYawDegrees,
                MaxGrinYawDegrees);
            return new StairwellCatPose(
                basePose.ChestScale,
                basePose.HeadLiftMeters,
                Mathf.LerpAngle(
                    basePose.HeadYawDegrees,
                    targetYaw,
                    weight),
                Mathf.Lerp(
                    basePose.HeadPitchDegrees,
                    GrinPitchDegrees,
                    weight),
                Mathf.Lerp(basePose.HeadRollDegrees, 0f, weight),
                basePose.EarLeftTiltDegrees,
                basePose.EarRightTiltDegrees,
                basePose.TailSwing01Degrees,
                basePose.TailSwing02Degrees,
                basePose.TailSwing03Degrees);
        }
    }
}
