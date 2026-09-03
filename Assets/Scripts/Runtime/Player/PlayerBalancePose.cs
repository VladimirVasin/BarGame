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
            Vector2 rightFootLocal)
        {
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
    }

    /// <summary>A presentation that can draw the balance model's pose.</summary>
    public interface IPlayerBalancePresentation
    {
        void SetBalance(in PlayerBalancePose pose);
    }
}
