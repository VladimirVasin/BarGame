using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// What the drink does to the walk this frame: where each boot lands
    /// relative to where the clip puts it (hero frame, x right, y
    /// forward), how far it turns out and how high it swings, how the
    /// pelvis rolls over a wide stance, and how much faster or slower the
    /// current half-step runs. All exactly nothing sober.
    /// </summary>
    public readonly struct PlayerDrunkGaitPose
    {
        private readonly float cadenceOffset;

        public PlayerDrunkGaitPose(
            float cadenceMultiplier,
            Vector2 leftFootOffsetLocal,
            Vector2 rightFootOffsetLocal,
            float leftFootYawDegrees,
            float rightFootYawDegrees,
            float leftFootLift,
            float rightFootLift,
            float pelvisRollDegrees)
        {
            cadenceOffset = cadenceMultiplier - 1f;
            LeftFootOffsetLocal = leftFootOffsetLocal;
            RightFootOffsetLocal = rightFootOffsetLocal;
            LeftFootYawDegrees = leftFootYawDegrees;
            RightFootYawDegrees = rightFootYawDegrees;
            LeftFootLift = Mathf.Max(0f, leftFootLift);
            RightFootLift = Mathf.Max(0f, rightFootLift);
            PelvisRollDegrees = pelvisRollDegrees;
        }

        /// <summary>The sober walk: the default of the struct, so a pose never asked for is this.</summary>
        public static PlayerDrunkGaitPose None => default;

        /// <summary>The walk clip's cadence is multiplied by this (one sober).</summary>
        public float CadenceMultiplier => 1f + cadenceOffset;

        public Vector2 LeftFootOffsetLocal { get; }
        public Vector2 RightFootOffsetLocal { get; }

        /// <summary>Each boot's turn about up, degrees, positive clockwise from above: out for the right, in for the left.</summary>
        public float LeftFootYawDegrees { get; }
        public float RightFootYawDegrees { get; }

        /// <summary>Extra height of a swinging boot, metres; zero while it stands.</summary>
        public float LeftFootLift { get; }
        public float RightFootLift { get; }

        /// <summary>The pelvis rolling over a wide stance, degrees, positive right.</summary>
        public float PelvisRollDegrees { get; }

        public bool IsNone =>
            cadenceOffset == 0f &&
            LeftFootOffsetLocal == Vector2.zero &&
            RightFootOffsetLocal == Vector2.zero &&
            LeftFootYawDegrees == 0f &&
            RightFootYawDegrees == 0f &&
            LeftFootLift == 0f &&
            RightFootLift == 0f &&
            PelvisRollDegrees == 0f;
    }

    /// <summary>The numbers of the drunk walk, all scaled by the intoxication <c>t</c>.</summary>
    public static class PlayerDrunkGaitRules
    {
        /// <summary>Each half-step runs <c>1 ± this · t²</c> as fast as the clip.</summary>
        public const float TimingJitter = 0.25f;

        /// <summary>The boots land outward of the clip by <c>base + gain·t</c>, give or take <c>jitter·t</c>.</summary>
        public const float LateralBaseMetres = 0.03f;
        public const float LateralGainMetres = 0.14f;
        public const float LateralJitterMetres = 0.08f;

        /// <summary>One step in this many (times <c>t</c>) crosses the midline instead.</summary>
        public const float CrossoverChance = 0.15f;
        public const float CrossoverFraction = 0.5f;
        public const float LateralClampMetres = 0.17f;

        /// <summary>The stride lands short or long by up to this (times <c>t</c>).</summary>
        public const float StrideJitterMetres = 0.15f;

        /// <summary>A swinging boot comes up to this much higher (times <c>t</c>).</summary>
        public const float LiftGainMetres = 0.05f;

        /// <summary>The toes turn out up to this (times <c>t</c>).</summary>
        public const float ToeOutDegrees = 12f;

        /// <summary>The pelvis rolls up to this (times <c>t</c>) over the widest stance.</summary>
        public const float PelvisRollDegrees = 4f;

        /// <summary>
        /// The Walk clip contacts the left heel at cycle zero and the right
        /// at one half; each boot stands for the half cycle centred on its
        /// contact and swings for the other. The left swings from a
        /// quarter to three quarters, the right from three quarters round
        /// to a quarter.
        /// </summary>
        public const float LeftSwingStart = 0.25f;
        public const float RightSwingStart = 0.75f;
        public const float SwingLength = 0.5f;

        /// <summary>How far through its swing a boot is at this cycle, one when it stands.</summary>
        public static float SwingProgress(float cycle, float swingStart)
        {
            float since = Mathf.Repeat(cycle - swingStart, 1f);
            return since < SwingLength ? since / SwingLength : 1f;
        }

        /// <summary>Whether the cycle passed <paramref name="mark"/> going from <paramref name="previous"/> to <paramref name="current"/>, wrapping.</summary>
        public static bool Crossed(float previous, float current, float mark)
        {
            float before = Mathf.Repeat(mark - previous, 1f);
            float travelled = Mathf.Repeat(current - previous, 1f);
            return travelled > 0f && before < travelled && before <= 0.5f;
        }
    }

    /// <summary>
    /// The uneven walk of a drunk, pure and seeded: at the start of every
    /// swing the boot draws where it will land — wider than the clip,
    /// now and then across the midline, short or long — how high it
    /// comes up and how far the toes turn out, and the half-step draws
    /// how fast it runs; the landing eases in over the swing and HOLDS
    /// through the stance, so a planted boot never slides. The clip
    /// still supplies the walk; this supplies the disorder. Sober it
    /// draws nothing and returns exactly nothing.
    /// </summary>
    public sealed class PlayerDrunkGaitModel
    {
        private readonly System.Random random;
        private Foot left;
        private Foot right;
        private float cadenceMultiplier = 1f;
        private float lastCycle;
        private bool hasCycle;
        private PlayerDrunkGaitPose pose = PlayerDrunkGaitPose.None;

        public PlayerDrunkGaitModel(int seed)
        {
            random = new System.Random(seed);
        }

        public PlayerDrunkGaitPose Pose => pose;

        /// <summary>How many boot landings have been drawn (each consumes the seed's next draws).</summary>
        public int LandingsDrawn { get; private set; }

        /// <summary>The landings last drawn for each boot (probe seams).</summary>
        public Vector2 DebugLeftTarget => left.Target;
        public Vector2 DebugRightTarget => right.Target;

        /// <summary>Forget the walk in progress (a fall, a clip, a teleport); the seed's sequence goes on from where it is.</summary>
        public void Reset()
        {
            left = default;
            right = default;
            cadenceMultiplier = 1f;
            hasCycle = false;
            pose = PlayerDrunkGaitPose.None;
        }

        /// <summary>
        /// Advances the model on the walk's own cycle (<c>0..1</c>, the
        /// Walk clip's normalized time); time is only for the record.
        /// </summary>
        public PlayerDrunkGaitPose Advance(
            float deltaTime,
            float intoxication,
            float walkCycle,
            bool forwardGait,
            float runBlend,
            float locomotionBlend)
        {
            float t = Mathf.Clamp01(intoxication);
            float gate = Mathf.Clamp01(locomotionBlend) * (1f - Mathf.Clamp01(runBlend));
            if (t <= 0f || !forwardGait || gate <= 0f)
            {
                // Nothing to draw, nothing drawn: the seed's sequence is
                // untouched, so a sober stretch changes no later step.
                Reset();
                return pose;
            }

            float cycle = Mathf.Repeat(walkCycle, 1f);
            if (!hasCycle)
            {
                hasCycle = true;
                lastCycle = cycle;
            }

            if (PlayerDrunkGaitRules.Crossed(lastCycle, cycle, PlayerDrunkGaitRules.LeftSwingStart))
            {
                BeginSwing(ref left, -1f, t);
            }

            if (PlayerDrunkGaitRules.Crossed(lastCycle, cycle, PlayerDrunkGaitRules.RightSwingStart))
            {
                BeginSwing(ref right, 1f, t);
            }

            lastCycle = cycle;
            float leftProgress = PlayerDrunkGaitRules.SwingProgress(cycle, PlayerDrunkGaitRules.LeftSwingStart);
            float rightProgress = PlayerDrunkGaitRules.SwingProgress(cycle, PlayerDrunkGaitRules.RightSwingStart);
            left.Ease(leftProgress);
            right.Ease(rightProgress);

            float width = (Mathf.Abs(left.Offset.x) + Mathf.Abs(right.Offset.x)) * 0.5f;
            float roll = PlayerDrunkGaitRules.PelvisRollDegrees * t *
                         Mathf.Clamp01(width / PlayerDrunkGaitRules.LateralClampMetres) *
                         Mathf.Cos(cycle * Mathf.PI * 2f);
            pose = new PlayerDrunkGaitPose(
                1f + (cadenceMultiplier - 1f) * gate,
                left.Offset * gate,
                right.Offset * gate,
                left.Yaw * gate,
                right.Yaw * gate,
                left.Lift * gate,
                right.Lift * gate,
                roll * gate);
            return pose;
        }

        private void BeginSwing(ref Foot foot, float outwardSign, float t)
        {
            float lateral = PlayerDrunkGaitRules.LateralBaseMetres +
                            PlayerDrunkGaitRules.LateralGainMetres * t +
                            Signed(Unit()) * PlayerDrunkGaitRules.LateralJitterMetres * t;
            if (Unit() < PlayerDrunkGaitRules.CrossoverChance * t)
            {
                lateral = -lateral * PlayerDrunkGaitRules.CrossoverFraction;
            }

            lateral = Mathf.Clamp(
                lateral,
                -PlayerDrunkGaitRules.LateralClampMetres,
                PlayerDrunkGaitRules.LateralClampMetres);
            float stride = Signed(Unit()) * PlayerDrunkGaitRules.StrideJitterMetres * t;
            float lift = Unit() * PlayerDrunkGaitRules.LiftGainMetres * t;
            float toeOut = PlayerDrunkGaitRules.ToeOutDegrees * t * (0.5f + 0.5f * Unit());
            cadenceMultiplier = 1f + Signed(Unit()) * PlayerDrunkGaitRules.TimingJitter * t * t;
            foot.Begin(
                new Vector2(outwardSign * lateral, stride),
                outwardSign * toeOut,
                lift);
            LandingsDrawn++;
        }

        private float Unit()
        {
            return (float)random.NextDouble();
        }

        private static float Signed(float unit)
        {
            return unit * 2f - 1f;
        }

        /// <summary>One boot: where it stood, where it is going, and where it is between the two.</summary>
        private struct Foot
        {
            private Vector2 previousOffset;
            private Vector2 targetOffset;
            private float previousYaw;
            private float targetYaw;
            private float liftAmplitude;

            public Vector2 Offset { get; private set; }
            public float Yaw { get; private set; }
            public float Lift { get; private set; }
            public Vector2 Target => targetOffset;

            public void Begin(Vector2 offset, float yaw, float lift)
            {
                previousOffset = Offset;
                previousYaw = Yaw;
                targetOffset = offset;
                targetYaw = yaw;
                liftAmplitude = lift;
            }

            public void Ease(float progress)
            {
                float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
                Offset = Vector2.Lerp(previousOffset, targetOffset, eased);
                Yaw = Mathf.Lerp(previousYaw, targetYaw, eased);
                Lift = progress < 1f
                    ? liftAmplitude * Mathf.Sin(progress * Mathf.PI)
                    : 0f;
            }
        }
    }
}
