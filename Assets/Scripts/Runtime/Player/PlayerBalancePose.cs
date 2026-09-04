using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One recovery step for the presentation to draw: which boot, how far
    /// along, and where its sole is going in the hero's frame
    /// (<c>x</c> right, <c>y</c> forward, metres).
    /// </summary>
    public readonly struct PlayerBalanceStepPose
    {
        public PlayerBalanceStepPose(
            bool active,
            FootSide side,
            float progress,
            Vector2 fromLocal,
            Vector2 toLocal,
            float lift)
        {
            Active = active;
            Side = side;
            Progress = Mathf.Clamp01(progress);
            FromLocal = fromLocal;
            ToLocal = toLocal;
            Lift = Mathf.Max(0f, lift);
        }

        public static PlayerBalanceStepPose None => default;

        public bool Active { get; }
        public FootSide Side { get; }
        public float Progress { get; }
        public Vector2 FromLocal { get; }
        public Vector2 ToLocal { get; }
        public float Lift { get; }
    }

    /// <summary>
    /// A hand reaching for a wall: where the palm goes and how hard it is
    /// holding on.
    /// </summary>
    public readonly struct PlayerWallReachPose
    {
        public PlayerWallReachPose(
            bool active,
            bool rightHand,
            Vector3 worldPosition,
            Vector3 worldNormal,
            float weight)
        {
            Active = active;
            RightHand = rightHand;
            WorldPosition = worldPosition;
            WorldNormal = worldNormal;
            Weight = Mathf.Clamp01(weight);
        }

        public static PlayerWallReachPose None =>
            new PlayerWallReachPose(
                false,
                true,
                Vector3.zero,
                Vector3.forward,
                0f);

        public bool Active { get; }
        public bool RightHand { get; }

        /// <summary>Where the palm goes, world space.</summary>
        public Vector3 WorldPosition { get; }

        /// <summary>The wall's normal, pointing away from it toward the hero.</summary>
        public Vector3 WorldNormal { get; }
        public float Weight { get; }

        /// <summary>The same reach as the general arm target the layer solves.</summary>
        public PlayerArmReachPose ToArmReach()
        {
            return new PlayerArmReachPose(
                Active,
                RightHand,
                WorldPosition,
                WorldNormal,
                Weight);
        }
    }

    /// <summary>
    /// What the balance model wants the body to show this frame. Carried
    /// from the balance controller to the presentation every frame; the
    /// presentation owns how it lands on the bones.
    /// </summary>
    public readonly struct PlayerBalancePose
    {
        public PlayerBalancePose(
            float weight,
            float leanRollDegrees,
            float leanPitchDegrees,
            float instability,
            float armReaction,
            float crouchMetres,
            PlayerBalanceStepPose step,
            PlayerWallReachPose wallReach,
            Vector2 leftFootLocal,
            Vector2 rightFootLocal,
            float torsoRollDegrees = 0f,
            float torsoPitchDegrees = 0f,
            BalancePhase phase = BalancePhase.Steady,
            float braceWeight = 0f,
            Vector2 fallAxisLocal = default,
            PlayerArmReachPose leftBrace = default,
            PlayerArmReachPose rightBrace = default,
            PlayerDrunkGaitPose gait = default)
        {
            Phase = phase;
            Gait = gait;
            BraceWeight = Mathf.Clamp01(braceWeight);
            FallAxisLocal = fallAxisLocal;
            LeftBrace = leftBrace;
            RightBrace = rightBrace;
            Weight = Mathf.Clamp01(weight);
            LeanRollDegrees = leanRollDegrees;
            LeanPitchDegrees = leanPitchDegrees;
            Instability = Mathf.Clamp01(instability);
            ArmReaction = Mathf.Clamp01(armReaction);
            CrouchMetres = Mathf.Max(0f, crouchMetres);
            Step = step;
            WallReach = wallReach;
            LeftFootLocal = leftFootLocal;
            RightFootLocal = rightFootLocal;
            TorsoRollDegrees = torsoRollDegrees;
            TorsoPitchDegrees = torsoPitchDegrees;
        }

        /// <summary>Nothing to show: sober, or the model is frozen.</summary>
        public static PlayerBalancePose Neutral =>
            new PlayerBalancePose(
                0f,
                0f,
                0f,
                0f,
                0f,
                0f,
                PlayerBalanceStepPose.None,
                PlayerWallReachPose.None,
                PlayerBalanceModel.DefaultLeftFoot,
                PlayerBalanceModel.DefaultRightFoot);

        /// <summary>How much of the whole pose applies (<c>0..1</c>).</summary>
        public float Weight { get; }

        /// <summary>Pelvis roll toward the lean, degrees, positive right.</summary>
        public float LeanRollDegrees { get; }

        /// <summary>Pelvis pitch, degrees, positive forward.</summary>
        public float LeanPitchDegrees { get; }
        public float Instability { get; }
        public float ArmReaction { get; }
        public float CrouchMetres { get; }
        public PlayerBalanceStepPose Step { get; }
        public PlayerWallReachPose WallReach { get; }

        /// <summary>Where the model believes each sole is, hero frame.</summary>
        public Vector2 LeftFootLocal { get; }
        public Vector2 RightFootLocal { get; }

        /// <summary>
        /// The torso's hip-strategy whip, degrees off the legs, in the
        /// sense of the fall it fights: roll positive right, pitch
        /// positive forward.
        /// </summary>
        public float TorsoRollDegrees { get; }
        public float TorsoPitchDegrees { get; }
        public BalancePhase Phase { get; }

        /// <summary>How far the hands have gone out for the ground (<c>0..1</c>).</summary>
        public float BraceWeight { get; }

        /// <summary>Planar direction of the topple, hero frame (x right, y forward).</summary>
        public Vector2 FallAxisLocal { get; }

        /// <summary>Where each bracing hand goes, if it is going anywhere.</summary>
        public PlayerArmReachPose LeftBrace { get; }
        public PlayerArmReachPose RightBrace { get; }

        /// <summary>The drink's disorder of the walk: boot placements, lifts, toe-out, cadence.</summary>
        public PlayerDrunkGaitPose Gait { get; }
    }

    /// <summary>
    /// A hand reaching for something — a wall, the ground: where the palm
    /// goes, which way it faces, and how hard the arm is committed to it.
    /// </summary>
    public readonly struct PlayerArmReachPose
    {
        public PlayerArmReachPose(
            bool active,
            bool rightHand,
            Vector3 worldPosition,
            Vector3 worldNormal,
            float weight,
            float elbowDropMetres = 0.3f,
            float elbowBackMetres = 0.2f)
        {
            Active = active;
            RightHand = rightHand;
            WorldPosition = worldPosition;
            WorldNormal = worldNormal;
            Weight = Mathf.Clamp01(weight);
            ElbowDropMetres = elbowDropMetres;
            ElbowBackMetres = elbowBackMetres;
        }

        public static PlayerArmReachPose None => default;

        public bool Active { get; }
        public bool RightHand { get; }

        /// <summary>Where the palm goes, world space.</summary>
        public Vector3 WorldPosition { get; }

        /// <summary>The surface normal at the palm, pointing toward the hero.</summary>
        public Vector3 WorldNormal { get; }
        public float Weight { get; }

        /// <summary>Where the elbow is hinted: this far below and behind the shoulder.</summary>
        public float ElbowDropMetres { get; }
        public float ElbowBackMetres { get; }
    }

    /// <summary>A presentation that can draw the balance model's pose.</summary>
    public interface IPlayerBalancePresentation
    {
        void SetBalance(in PlayerBalancePose pose);
    }

    /// <summary>
    /// What the rise model wants the body to show this frame on top of
    /// the authored Rise clip: the hands on the floor (or one on the
    /// knee), the boot stepping forward, a dip and a wobble of the pelvis,
    /// the head lifting, and how much of the standing leg solve applies.
    /// Hand offsets are hero-frame metres from each shoulder's ground
    /// point; the presentation finds the floor under them.
    /// </summary>
    public readonly struct PlayerRisePose
    {
        public PlayerRisePose(
            bool active,
            PlayerRiseStage stage,
            float leftHandWeight,
            Vector2 leftHandOffsetLocal,
            float rightHandWeight,
            Vector2 rightHandOffsetLocal,
            bool handOnKnee,
            FootSide kneeSide,
            PlayerRiseStepPose step,
            float pelvisOffsetMetres,
            float pelvisRollDegrees,
            float pelvisPitchDegrees,
            float headLiftDegrees,
            float legsWeight,
            float leftHandLift = 0f,
            float rightHandLift = 0f,
            float stageProgress = 0f,
            bool slumpActive = false,
            PlayerCrawlLimb leftHandCrawl = default,
            PlayerCrawlLimb rightHandCrawl = default,
            PlayerCrawlLimb leftKneeCrawl = default,
            PlayerCrawlLimb rightKneeCrawl = default)
        {
            Active = active;
            Stage = stage;
            LeftHandLift = Mathf.Max(0f, leftHandLift);
            RightHandLift = Mathf.Max(0f, rightHandLift);
            StageProgress = Mathf.Clamp01(stageProgress);
            SlumpActive = slumpActive;
            LeftHandCrawl = leftHandCrawl;
            RightHandCrawl = rightHandCrawl;
            LeftKneeCrawl = leftKneeCrawl;
            RightKneeCrawl = rightKneeCrawl;
            LeftHandWeight = Mathf.Clamp01(leftHandWeight);
            LeftHandOffsetLocal = leftHandOffsetLocal;
            RightHandWeight = Mathf.Clamp01(rightHandWeight);
            RightHandOffsetLocal = rightHandOffsetLocal;
            HandOnKnee = handOnKnee;
            KneeSide = kneeSide;
            Step = step;
            PelvisOffsetMetres = pelvisOffsetMetres;
            PelvisRollDegrees = pelvisRollDegrees;
            PelvisPitchDegrees = pelvisPitchDegrees;
            HeadLiftDegrees = headLiftDegrees;
            LegsWeight = Mathf.Clamp01(legsWeight);
        }

        public static PlayerRisePose None => default;

        /// <summary>The rise model's output as the presentation wants it.</summary>
        public static PlayerRisePose FromOutput(in PlayerRiseOutput output)
        {
            bool active = output.Stage >= PlayerRiseStage.Stirring &&
                          output.Stage < PlayerRiseStage.Done;
            return new PlayerRisePose(
                active,
                output.Stage,
                output.LeftHandWeight,
                output.LeftHandOffsetLocal,
                output.RightHandWeight,
                output.RightHandOffsetLocal,
                output.HandOnKnee,
                output.KneeSide,
                output.Step,
                output.PelvisOffsetMetres,
                output.WobbleLeanDegrees.x,
                output.WobbleLeanDegrees.y,
                output.HeadLiftDegrees,
                output.LegsWeight,
                output.LeftHandLift,
                output.RightHandLift,
                output.StageProgress,
                output.SlumpActive,
                output.LeftHandCrawl,
                output.RightHandCrawl,
                output.LeftKneeCrawl,
                output.RightKneeCrawl);
        }

        /// <summary>How far each hand is held off the floor, metres (a crawl's swinging hand).</summary>
        public float LeftHandLift { get; }
        public float RightHandLift { get; }

        /// <summary>How far through the current stage (<c>0..1</c>), and whether a slump is on.</summary>
        public float StageProgress { get; }
        public bool SlumpActive { get; }

        /// <summary>The crawl's four contacts (planted or swinging, and how far through the swing).</summary>
        public PlayerCrawlLimb LeftHandCrawl { get; }
        public PlayerCrawlLimb RightHandCrawl { get; }
        public PlayerCrawlLimb LeftKneeCrawl { get; }
        public PlayerCrawlLimb RightKneeCrawl { get; }

        public bool Active { get; }
        public PlayerRiseStage Stage { get; }
        public float LeftHandWeight { get; }
        public Vector2 LeftHandOffsetLocal { get; }
        public float RightHandWeight { get; }
        public Vector2 RightHandOffsetLocal { get; }
        public bool HandOnKnee { get; }
        public FootSide KneeSide { get; }
        public PlayerRiseStepPose Step { get; }

        /// <summary>A dip of the pelvis below the clip, metres (never positive).</summary>
        public float PelvisOffsetMetres { get; }
        public float PelvisRollDegrees { get; }
        public float PelvisPitchDegrees { get; }
        public float HeadLiftDegrees { get; }
        public float LegsWeight { get; }
    }

    /// <summary>A presentation that can draw the rise model's pose.</summary>
    public interface IPlayerRisePresentation
    {
        void SetRise(in PlayerRisePose pose);
    }
}
