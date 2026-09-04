using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Where the hero is in getting up.</summary>
    public enum PlayerRiseStage
    {
        /// <summary>The ragdoll is still moving; wait for it to come to rest.</summary>
        Settling = 0,

        /// <summary>At rest, and not yet stirring: a drunk lies there a while.</summary>
        Stunned,

        /// <summary>The frozen body gathers itself: head lifts, hands find the floor.</summary>
        Stirring,

        /// <summary>Pushing up onto all fours, with a slump or two on the way.</summary>
        PushingUp,

        /// <summary>On all fours and told to go somewhere: crawling, hands alternating, as long as the key is held.</summary>
        Crawling,

        /// <summary>One boot forward, a hand to the knee.</summary>
        Kneeling,

        /// <summary>Up, with a wobble at the top.</summary>
        Standing,
        Done
    }

    /// <summary>What the world tells the rise model this frame.</summary>
    public readonly struct PlayerRiseInput
    {
        public PlayerRiseInput(
            float intoxication,
            bool grounded,
            float maximumBodySpeed)
        {
            Intoxication = Mathf.Clamp01(intoxication);
            Grounded = grounded;
            MaximumBodySpeed = Mathf.Max(0f, maximumBodySpeed);
        }

        public float Intoxication { get; }
        public bool Grounded { get; }

        /// <summary>The fastest ragdoll body, m/s; zero once frozen.</summary>
        public float MaximumBodySpeed { get; }
    }

    /// <summary>The boot that steps forward under him while he kneels.</summary>
    public readonly struct PlayerRiseStepPose
    {
        public PlayerRiseStepPose(
            bool active,
            FootSide side,
            Vector2 targetLocal,
            float lift,
            float weight)
        {
            Active = active;
            Side = side;
            TargetLocal = targetLocal;
            Lift = Mathf.Max(0f, lift);
            Weight = Mathf.Clamp01(weight);
        }

        public static PlayerRiseStepPose None => default;

        public bool Active { get; }
        public FootSide Side { get; }

        /// <summary>Where the sole goes, hero frame (x right, y forward).</summary>
        public Vector2 TargetLocal { get; }
        public float Lift { get; }
        public float Weight { get; }
    }

    /// <summary>
    /// One limb of a crawl: planted (holding its spot on the floor while
    /// the body moves over it) or swinging ahead, and how far through
    /// the swing it is.
    /// </summary>
    public readonly struct PlayerCrawlLimb
    {
        public PlayerCrawlLimb(bool swinging, float progress)
        {
            Swinging = swinging;
            Progress = Mathf.Clamp01(progress);
        }

        public static PlayerCrawlLimb Planted => default;

        public bool Swinging { get; }
        public float Progress { get; }
    }

    /// <summary>What the presentation draws this frame of the rise.</summary>
    public readonly struct PlayerRiseOutput
    {
        public PlayerRiseOutput(
            PlayerRiseStage stage,
            float stageProgress,
            float progress,
            float clipTime,
            float blendProgress,
            float pelvisOffsetMetres,
            float leftHandWeight,
            Vector2 leftHandOffsetLocal,
            float rightHandWeight,
            Vector2 rightHandOffsetLocal,
            bool handOnKnee,
            FootSide kneeSide,
            PlayerRiseStepPose step,
            float headLiftDegrees,
            Vector2 wobbleLeanDegrees,
            float legsWeight,
            bool slumpActive,
            float leftHandLift = 0f,
            float rightHandLift = 0f,
            Vector2 crawlVelocityLocal = default,
            float crawlYawDegreesPerSecond = 0f,
            PlayerCrawlLimb leftHandCrawl = default,
            PlayerCrawlLimb rightHandCrawl = default,
            PlayerCrawlLimb leftKneeCrawl = default,
            PlayerCrawlLimb rightKneeCrawl = default)
        {
            Stage = stage;
            LeftHandLift = Mathf.Max(0f, leftHandLift);
            RightHandLift = Mathf.Max(0f, rightHandLift);
            CrawlVelocityLocal = crawlVelocityLocal;
            CrawlYawDegreesPerSecond = crawlYawDegreesPerSecond;
            LeftHandCrawl = leftHandCrawl;
            RightHandCrawl = rightHandCrawl;
            LeftKneeCrawl = leftKneeCrawl;
            RightKneeCrawl = rightKneeCrawl;
            StageProgress = Mathf.Clamp01(stageProgress);
            Progress = Mathf.Clamp01(progress);
            ClipTime = Mathf.Clamp01(clipTime);
            BlendProgress = Mathf.Clamp01(blendProgress);
            PelvisOffsetMetres = Mathf.Min(0f, pelvisOffsetMetres);
            LeftHandWeight = Mathf.Clamp01(leftHandWeight);
            LeftHandOffsetLocal = leftHandOffsetLocal;
            RightHandWeight = Mathf.Clamp01(rightHandWeight);
            RightHandOffsetLocal = rightHandOffsetLocal;
            HandOnKnee = handOnKnee;
            KneeSide = kneeSide;
            Step = step;
            HeadLiftDegrees = headLiftDegrees;
            WobbleLeanDegrees = wobbleLeanDegrees;
            LegsWeight = Mathf.Clamp01(legsWeight);
            SlumpActive = slumpActive;
        }

        public static PlayerRiseOutput Lying =>
            new PlayerRiseOutput(
                PlayerRiseStage.Settling,
                0f,
                0f,
                0f,
                0f,
                0f,
                0f,
                Vector2.zero,
                0f,
                Vector2.zero,
                false,
                FootSide.Right,
                PlayerRiseStepPose.None,
                0f,
                Vector2.zero,
                0f,
                false);

        public PlayerRiseStage Stage { get; }

        /// <summary>How far through the current stage (<c>0..1</c>).</summary>
        public float StageProgress { get; }

        /// <summary>How far through the whole rise (<c>0..1</c>); the fall amount is one minus this.</summary>
        public float Progress { get; }

        /// <summary>Where to scrub the authored Rise clip (<c>0..1</c>).</summary>
        public float ClipTime { get; }

        /// <summary>Stirring only: how far the frozen lying body has blended into the clip.</summary>
        public float BlendProgress { get; }

        /// <summary>A dip of the pelvis below the clip, metres (a slump); never positive.</summary>
        public float PelvisOffsetMetres { get; }

        /// <summary>How hard each hand is on the floor, and where it rests relative to its shoulder, hero frame.</summary>
        public float LeftHandWeight { get; }
        public Vector2 LeftHandOffsetLocal { get; }
        public float RightHandWeight { get; }
        public Vector2 RightHandOffsetLocal { get; }

        /// <summary>The hand on the knee side is on the knee, not the floor.</summary>
        public bool HandOnKnee { get; }
        public FootSide KneeSide { get; }
        public PlayerRiseStepPose Step { get; }

        /// <summary>How far the face lifts off the floor, degrees.</summary>
        public float HeadLiftDegrees { get; }

        /// <summary>The wobble at the top: pelvis roll (x) and pitch (y), degrees.</summary>
        public Vector2 WobbleLeanDegrees { get; }

        /// <summary>How much of the standing leg solve applies (<c>0..1</c>).</summary>
        public float LegsWeight { get; }

        /// <summary>A slump is in progress: the clip runs backward for a moment.</summary>
        public bool SlumpActive { get; }

        /// <summary>How far each hand is off the floor, metres: the swinging hand of a crawl.</summary>
        public float LeftHandLift { get; }
        public float RightHandLift { get; }

        /// <summary>The crawl's velocity in the hero's frame (x right, y forward), m/s; zero unless crawling.</summary>
        public Vector2 CrawlVelocityLocal { get; }

        /// <summary>How fast the crawl turns him toward the key, degrees per second; zero unless crawling.</summary>
        public float CrawlYawDegreesPerSecond { get; }

        /// <summary>
        /// The crawl's four contacts, diagonal like a real crawl: the left
        /// hand and the right knee swing through the first half of the
        /// cycle while the other two hold, then the other pair.
        /// </summary>
        public PlayerCrawlLimb LeftHandCrawl { get; }
        public PlayerCrawlLimb RightHandCrawl { get; }
        public PlayerCrawlLimb LeftKneeCrawl { get; }
        public PlayerCrawlLimb RightKneeCrawl { get; }
    }

    /// <summary>The pure numbers of the rise: the clip's authored keys and every duration.</summary>
    public static class PlayerRiseRules
    {
        /// <summary>The authored Rise clip's keys, normalized time.</summary>
        public const float DownKey = 0f;
        public const float BraceKey = 0.10f;
        public const float ProneTuckKey = 0.24f;
        public const float AllFoursKey = 0.38f;
        public const float AllFoursShiftKey = 0.48f;
        public const float FootLiftKey = 0.56f;
        public const float HalfKneelKey = 0.64f;
        public const float CrouchLegLiftKey = 0.72f;
        public const float LowCrouchKey = 0.80f;
        public const float NearUprightKey = 0.92f;
        public const float RelaxedKey = 1f;

        /// <summary>The ragdoll is at rest below this speed for this long; never sooner, never later.</summary>
        public const float SettleRestSpeed = 0.15f;
        public const float SettleRestSeconds = 0.25f;
        public const float SettleMinimumSeconds = 0.6f;
        public const float SettleMaximumSeconds = 2.5f;

        /// <summary>The slump: the clip runs back this far, the pelvis dips this much, in these three parts.</summary>
        public const float SlumpRetreatSeconds = 0.15f;
        public const float SlumpHoldSeconds = 0.20f;
        public const float SlumpResumeSeconds = 0.10f;
        public const float SlumpSeconds =
            SlumpRetreatSeconds + SlumpHoldSeconds + SlumpResumeSeconds;
        public const float SlumpRetreatClip = 0.06f;
        public const float SlumpDipMetres = 0.06f;
        public const int MaximumSlumps = 2;

        public const float HeadLiftPeakDegrees = 12f;
        public const float LeadStepForwardMetres = 0.30f;
        public const float LeadStepSideMetres = 0.12f;
        public const float LeadStepLift = 0.08f;
        public const float WobbleDegreesAtMaximum = 4f;
        public const float WobbleHertz = 1.5f;
        public const float HandbackVelocityScale = 0.5f;
        public const float HandsReleaseSeconds = 0.3f;

        /// <summary>
        /// The crawl: a key past the dead zone once he is on all fours
        /// keeps him there, hands alternating; released for this long he
        /// goes on with the kneel. A kneel already this far along is not
        /// abandoned for a crawl.
        /// </summary>
        public const float CrawlDeadZone = 0.2f;
        public const float CrawlReleaseSeconds = 0.15f;
        public const float KneelAbortProgress = 0.3f;
        public const float CrawlYawDegreesPerSecond = 60f;
        public const float CrawlTurnFullDegrees = 45f;

        /// <summary>
        /// Where a crawling limb plants relative to its shoulder's or
        /// hip's ground point (hero frame: ahead, and out to its side),
        /// how high it swings, and how far above the floor a resting
        /// knee sits. A hand plants a long reach ahead and the body
        /// crawls over it; a knee only a little.
        /// </summary>
        public const float CrawlHandReachMetres = 0.22f;
        public const float CrawlHandSideMetres = 0.06f;
        public const float CrawlHandLiftMetres = 0.10f;
        public const float CrawlKneeReachMetres = 0.10f;
        public const float CrawlKneeSideMetres = 0.10f;
        public const float CrawlKneeLiftMetres = 0.06f;
        public const float CrawlKneeClearanceMetres = 0.05f;

        /// <summary>
        /// The hips come down for the planted knee to rest on the floor
        /// and for the planted hands to reach it — never more than this
        /// (the crawl's first plant is wherever the clip left the knee,
        /// which can be far behind the hip and would fold him flat), and
        /// never faster than this, so the crawl's start is a settle.
        /// </summary>
        public const float CrawlMaximumHipDropMetres = 0.25f;
        public const float CrawlHipDropRateMetresPerSecond = 0.6f;
        public const float CrawlBobMetres = 0.02f;
        public const float CrawlHeadLiftDegrees = 4f;

        /// <summary>A twitch on the floor shortens the stun, never below this.</summary>
        public const float StunFloorSeconds = 0.3f;

        /// <summary>Hand-swings per second, slower the drunker.</summary>
        public static float CrawlHertz(float intoxication)
        {
            return Mathf.Lerp(1.2f, 0.9f, Mathf.Clamp01(intoxication));
        }

        /// <summary>Crawl speed at full key, m/s, slower the drunker.</summary>
        public static float CrawlSpeed(float intoxication)
        {
            return Mathf.Lerp(0.5f, 0.35f, Mathf.Clamp01(intoxication));
        }

        /// <summary>Stage shares of the whole rise's progress.</summary>
        public const float StirringShare = 0.2f;
        public const float PushingUpShare = 0.3f;
        public const float KneelingShare = 0.25f;
        public const float StandingShare = 0.25f;

        public static float StunSeconds(float intoxication, float unit)
        {
            return Mathf.Lerp(0.5f, 2f, Mathf.Clamp01(intoxication)) *
                   (0.7f + 0.6f * Mathf.Clamp01(unit));
        }

        public static float StirringSeconds(float unit)
        {
            return Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(unit));
        }

        public static float PushingUpSeconds(float unit)
        {
            return Mathf.Lerp(0.8f, 1.2f, Mathf.Clamp01(unit));
        }

        public static float KneelingSeconds(float unit)
        {
            return Mathf.Lerp(0.6f, 0.9f, Mathf.Clamp01(unit));
        }

        public static float StandingSeconds(float unit)
        {
            return Mathf.Lerp(0.8f, 1.2f, Mathf.Clamp01(unit));
        }

        /// <summary>How many times the push-up fails first: none sober-ish, up to two blind drunk.</summary>
        public static int SlumpCount(float intoxication, float unit)
        {
            float raw = Mathf.Clamp01(unit) * (0.4f + 2.2f * Mathf.Clamp01(intoxication));
            return Mathf.Min(MaximumSlumps, Mathf.FloorToInt(raw));
        }

        public static float WobbleDegrees(float intoxication)
        {
            return WobbleDegreesAtMaximum * Mathf.Clamp01(intoxication);
        }

        /// <summary>The dip of a slump over its three parts (<c>0..1</c> of the full dip).</summary>
        public static float SlumpShape(float slumpElapsed)
        {
            if (slumpElapsed <= 0f)
            {
                return 0f;
            }

            if (slumpElapsed < SlumpRetreatSeconds)
            {
                return Mathf.SmoothStep(0f, 1f, slumpElapsed / SlumpRetreatSeconds);
            }

            if (slumpElapsed < SlumpRetreatSeconds + SlumpHoldSeconds)
            {
                return 1f;
            }

            float resume = (slumpElapsed - SlumpRetreatSeconds - SlumpHoldSeconds) /
                           SlumpResumeSeconds;
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(resume));
        }
    }

    /// <summary>
    /// Getting up, staged and seeded: the ragdoll lies until it is still,
    /// then a drunk lies a while longer; the frozen body stirs — the head
    /// lifts, the hands find the floor — and blends into the authored
    /// Rise's brace; he pushes up onto all fours, slumping back once or
    /// twice on the way when he is far gone; one boot comes forward and
    /// a hand goes to the knee; he stands, and wobbles at the top before
    /// the balance model has him again.
    ///
    /// The authored clip supplies the trunk; this model supplies the
    /// TIME — how the clip is scrubbed, where it pauses and runs back —
    /// and the limbs' targets and weights. Pure, every draw taken at
    /// construction, so a seed replays the same rise.
    /// </summary>
    public sealed class PlayerRiseModel
    {
        private readonly float intoxication;
        private readonly float stirringSeconds;
        private readonly float pushingUpSeconds;
        private readonly float kneelingSeconds;
        private readonly float standingSeconds;
        private readonly float[] slumpAt = new float[PlayerRiseRules.MaximumSlumps];
        private readonly float wobblePhase;

        private PlayerRiseStage stage = PlayerRiseStage.Settling;
        private float elapsed;
        private float stageElapsed;
        private float restTimer;
        private float pushProgress;
        private int slumpsPlanned;
        private int slumpsTaken;
        private bool inSlump;
        private float slumpElapsed;
        private FootSide leadFoot = FootSide.Right;
        private PlayerRiseOutput output = PlayerRiseOutput.Lying;
        private Vector2 handbackVelocity;
        private float stunSeconds;
        private Vector2 downedInput;
        private float crawlPhase;
        private float crawlReleaseTimer;

        public PlayerRiseModel(int seed, float intoxication)
        {
            this.intoxication = Mathf.Clamp01(intoxication);
            var random = new System.Random(seed);
            stunSeconds = PlayerRiseRules.StunSeconds(this.intoxication, Unit(random));
            stirringSeconds = PlayerRiseRules.StirringSeconds(Unit(random));
            pushingUpSeconds = PlayerRiseRules.PushingUpSeconds(Unit(random));
            slumpsPlanned = PlayerRiseRules.SlumpCount(this.intoxication, Unit(random));
            float first = Mathf.Lerp(0.45f, 0.75f, Unit(random));
            float second = Mathf.Lerp(0.45f, 0.75f, Unit(random));
            slumpAt[0] = Mathf.Min(first, second);
            slumpAt[1] = Mathf.Max(first, second);
            if (slumpAt[1] - slumpAt[0] < 0.08f)
            {
                slumpAt[1] = Mathf.Min(0.8f, slumpAt[0] + 0.08f);
            }

            kneelingSeconds = PlayerRiseRules.KneelingSeconds(Unit(random));
            standingSeconds = PlayerRiseRules.StandingSeconds(Unit(random));
            wobblePhase = Unit(random) * Mathf.PI * 2f;
        }

        public PlayerRiseStage Stage => stage;
        public PlayerRiseOutput Output => output;
        public float Elapsed => elapsed;
        public float StageElapsed => stageElapsed;
        public int SlumpsPlanned => slumpsPlanned;
        public int SlumpsTaken => slumpsTaken;
        public FootSide LeadFoot => leadFoot;
        public float Intoxication => intoxication;

        /// <summary>Seconds this rise lies stunned once the ragdoll is still.</summary>
        public float StunSeconds => stunSeconds;

        /// <summary>
        /// What the wobble hands the balance model when the rise is done,
        /// hero frame, m/s: the last swing's velocity, halved.
        /// </summary>
        public Vector2 HandbackVelocity => handbackVelocity;

        /// <summary>
        /// Which side he is lying on: the boot that leads the kneel and
        /// the hand that goes to the knee. Taken while he is still lying
        /// or stirring — the status controller decides it on the frame
        /// the model first reports Stirring — never once he pushes up.
        /// </summary>
        public void SetLyingSide(FootSide side)
        {
            if (stage <= PlayerRiseStage.Stirring)
            {
                leadFoot = side;
            }
        }

        /// <summary>Capture-sheet seam: plan this many slumps whatever the seed drew.</summary>
        internal void DebugPlanSlumps(int count)
        {
            if (stage <= PlayerRiseStage.Stirring)
            {
                slumpsPlanned = Mathf.Clamp(count, 0, PlayerRiseRules.MaximumSlumps);
            }
        }

        /// <summary>
        /// The player's key this frame, hero frame (x right, y forward),
        /// no longer than one. Past the dead zone it holds him crawling
        /// once he is on all fours, or takes him back to all fours from
        /// the start of the kneel.
        /// </summary>
        public void SetDownedInput(Vector2 bodyLocal)
        {
            downedInput = bodyLocal.sqrMagnitude > 1f ? bodyLocal.normalized : bodyLocal;
        }

        public bool HasDownedInput =>
            downedInput.magnitude > PlayerRiseRules.CrawlDeadZone;

        /// <summary>The crawl's hand-swing phase, radians; the left hand swings forward over the first half turn.</summary>
        public float CrawlPhase => crawlPhase;

        /// <summary>
        /// Shortens (or lengthens) the stun still to come — a twitch on
        /// the floor is the will to get up — never below the floor, and
        /// never once he has started to stir.
        /// </summary>
        public void NudgeStun(float seconds)
        {
            if (stage <= PlayerRiseStage.Stunned)
            {
                stunSeconds = Mathf.Max(
                    PlayerRiseRules.StunFloorSeconds,
                    stunSeconds + seconds);
            }
        }

        public void Advance(float deltaTime, in PlayerRiseInput input)
        {
            if (deltaTime <= 0f || float.IsNaN(deltaTime))
            {
                return;
            }

            deltaTime = Mathf.Min(deltaTime, 0.25f);
            elapsed += deltaTime;
            stageElapsed += deltaTime;
            switch (stage)
            {
                case PlayerRiseStage.Settling:
                    restTimer = input.MaximumBodySpeed < PlayerRiseRules.SettleRestSpeed
                        ? restTimer + deltaTime
                        : 0f;
                    if ((restTimer >= PlayerRiseRules.SettleRestSeconds &&
                         stageElapsed >= PlayerRiseRules.SettleMinimumSeconds) ||
                        stageElapsed >= PlayerRiseRules.SettleMaximumSeconds)
                    {
                        Enter(PlayerRiseStage.Stunned);
                    }

                    break;

                case PlayerRiseStage.Stunned:
                    if (stageElapsed >= stunSeconds)
                    {
                        Enter(PlayerRiseStage.Stirring);
                    }

                    break;

                case PlayerRiseStage.Stirring:
                    if (stageElapsed >= stirringSeconds)
                    {
                        Enter(PlayerRiseStage.PushingUp);
                    }

                    break;

                case PlayerRiseStage.PushingUp:
                    AdvancePushingUp(deltaTime);
                    break;

                case PlayerRiseStage.Crawling:
                    AdvanceCrawling(deltaTime);
                    break;

                case PlayerRiseStage.Kneeling:
                    if (HasDownedInput &&
                        stageElapsed / kneelingSeconds < PlayerRiseRules.KneelAbortProgress)
                    {
                        // Only just kneeling and told to go: back to all
                        // fours and crawl.
                        Enter(PlayerRiseStage.Crawling);
                        break;
                    }

                    if (stageElapsed >= kneelingSeconds)
                    {
                        Enter(PlayerRiseStage.Standing);
                    }

                    break;

                case PlayerRiseStage.Standing:
                    if (stageElapsed >= standingSeconds)
                    {
                        handbackVelocity = WobbleVelocity(standingSeconds);
                        Enter(PlayerRiseStage.Done);
                    }

                    break;
            }

            output = BuildOutput();
        }

        private void Enter(PlayerRiseStage next)
        {
            stage = next;
            stageElapsed = 0f;
        }

        /// <summary>
        /// The push-up's own clock: it runs at the nominal rate, and at
        /// each planned slump point it stops for the slump's three parts
        /// — the clip backs off, holds, and resumes — before going on.
        /// </summary>
        private void AdvancePushingUp(float deltaTime)
        {
            if (inSlump)
            {
                slumpElapsed += deltaTime;
                if (slumpElapsed >= PlayerRiseRules.SlumpSeconds)
                {
                    inSlump = false;
                    slumpElapsed = 0f;
                    slumpsTaken++;
                }

                return;
            }

            pushProgress += deltaTime / pushingUpSeconds;
            if (slumpsTaken < slumpsPlanned &&
                pushProgress >= slumpAt[slumpsTaken])
            {
                pushProgress = slumpAt[slumpsTaken];
                inSlump = true;
                slumpElapsed = 0f;
                return;
            }

            if (pushProgress >= 1f)
            {
                pushProgress = 1f;
                Enter(HasDownedInput ? PlayerRiseStage.Crawling : PlayerRiseStage.Kneeling);
            }
        }

        /// <summary>
        /// The crawl's own clock: the hands swing while the key is held
        /// and freeze where they are when it is not; a key gone for the
        /// release time hands him on to the kneel.
        /// </summary>
        private void AdvanceCrawling(float deltaTime)
        {
            if (HasDownedInput)
            {
                crawlReleaseTimer = 0f;
                crawlPhase += deltaTime * Mathf.PI * 2f * PlayerRiseRules.CrawlHertz(intoxication);
                return;
            }

            crawlReleaseTimer += deltaTime;
            if (crawlReleaseTimer >= PlayerRiseRules.CrawlReleaseSeconds)
            {
                crawlReleaseTimer = 0f;
                Enter(PlayerRiseStage.Kneeling);
            }
        }

        /// <summary>
        /// Where the key points relative to his facing decides the crawl:
        /// he turns toward it at up to the turn rate, and moves forward
        /// only in so far as he faces it — a key behind him turns him
        /// round before it moves him.
        /// </summary>
        private void CrawlMotion(out Vector2 velocityLocal, out float yawDegreesPerSecond)
        {
            velocityLocal = Vector2.zero;
            yawDegreesPerSecond = 0f;
            if (!HasDownedInput)
            {
                return;
            }

            float angle = Mathf.Atan2(downedInput.x, downedInput.y) * Mathf.Rad2Deg;
            yawDegreesPerSecond =
                Mathf.Clamp(angle / PlayerRiseRules.CrawlTurnFullDegrees, -1f, 1f) *
                PlayerRiseRules.CrawlYawDegreesPerSecond;
            float facing = Mathf.Clamp01(Mathf.Cos(angle * Mathf.Deg2Rad));
            // A crawl moves in pulls, not at a constant glide.
            float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(crawlPhase));
            velocityLocal = new Vector2(
                0f,
                PlayerRiseRules.CrawlSpeed(intoxication) *
                Mathf.Clamp01(downedInput.magnitude) *
                facing *
                pulse);
        }

        private Vector2 WobbleVelocity(float seconds)
        {
            float amplitude = PlayerRiseRules.WobbleDegrees(intoxication) * Mathf.Deg2Rad;
            float rate = Mathf.PI * 2f * PlayerRiseRules.WobbleHertz;
            float angularVelocity = amplitude * rate * Mathf.Cos(rate * seconds + wobblePhase);
            float speed = angularVelocity *
                          PlayerBalanceSettings.DefaultComHeight *
                          PlayerRiseRules.HandbackVelocityScale;
            return new Vector2(speed, speed * 0.5f);
        }

        private PlayerRiseOutput BuildOutput()
        {
            float leadSign = leadFoot == FootSide.Right ? 1f : -1f;
            Vector2 leftHand = new Vector2(-0.05f, 0.1f);
            Vector2 rightHand = new Vector2(0.05f, 0.1f);
            switch (stage)
            {
                case PlayerRiseStage.Settling:
                case PlayerRiseStage.Stunned:
                    return PlayerRiseOutput.Lying;

                case PlayerRiseStage.Stirring:
                {
                    float p = Mathf.Clamp01(stageElapsed / stirringSeconds);
                    float hands = Mathf.Clamp01(p / 0.5f);
                    float head = p < 0.5f
                        ? PlayerRiseRules.HeadLiftPeakDegrees * (p / 0.5f)
                        : PlayerRiseRules.HeadLiftPeakDegrees * (1f - 0.5f * ((p - 0.5f) / 0.5f));
                    return new PlayerRiseOutput(
                        stage,
                        p,
                        PlayerRiseRules.StirringShare * p,
                        Mathf.Lerp(PlayerRiseRules.DownKey, PlayerRiseRules.BraceKey, p),
                        Mathf.SmoothStep(0f, 1f, p),
                        0f,
                        hands,
                        leftHand,
                        hands,
                        rightHand,
                        false,
                        leadFoot,
                        PlayerRiseStepPose.None,
                        head,
                        Vector2.zero,
                        0f,
                        false);
                }

                case PlayerRiseStage.PushingUp:
                {
                    float shape = inSlump ? PlayerRiseRules.SlumpShape(slumpElapsed) : 0f;
                    float clip = Mathf.Lerp(
                                     PlayerRiseRules.BraceKey,
                                     PlayerRiseRules.AllFoursKey,
                                     pushProgress) -
                                 PlayerRiseRules.SlumpRetreatClip * shape;
                    return new PlayerRiseOutput(
                        stage,
                        pushProgress,
                        PlayerRiseRules.StirringShare +
                        PlayerRiseRules.PushingUpShare * pushProgress,
                        clip,
                        1f,
                        -PlayerRiseRules.SlumpDipMetres * shape,
                        1f,
                        leftHand,
                        1f,
                        rightHand,
                        false,
                        leadFoot,
                        PlayerRiseStepPose.None,
                        PlayerRiseRules.HeadLiftPeakDegrees * 0.5f * (1f - pushProgress),
                        Vector2.zero,
                        0f,
                        inSlump);
                }

                case PlayerRiseStage.Crawling:
                {
                    // A diagonal crawl: the left hand and the right knee
                    // swing forward over the first half turn while the
                    // other two hold the floor, then the right hand and the
                    // left knee. The clip rocks between its two all-fours
                    // keys and the hips dip with each pull; the
                    // presentation plants each limb in the WORLD and
                    // carries the body over it.
                    float turn = Mathf.Repeat(crawlPhase, Mathf.PI * 2f);
                    bool firstHalf = turn < Mathf.PI;
                    float progress = firstHalf ? turn / Mathf.PI : (turn - Mathf.PI) / Mathf.PI;
                    float leftSwing = firstHalf ? Mathf.Sin(progress * Mathf.PI) : 0f;
                    float rightSwing = firstHalf ? 0f : Mathf.Sin(progress * Mathf.PI);
                    var swinging = new PlayerCrawlLimb(true, progress);
                    float rock = 0.5f + 0.5f * Mathf.Sin(crawlPhase * 2f);
                    CrawlMotion(out Vector2 velocity, out float yaw);
                    return new PlayerRiseOutput(
                        stage,
                        Mathf.Clamp01(stageElapsed),
                        PlayerRiseRules.StirringShare + PlayerRiseRules.PushingUpShare,
                        Mathf.Lerp(
                            PlayerRiseRules.AllFoursKey,
                            PlayerRiseRules.AllFoursShiftKey,
                            rock),
                        1f,
                        -PlayerRiseRules.CrawlBobMetres * Mathf.Abs(Mathf.Sin(crawlPhase * 2f)),
                        1f,
                        new Vector2(
                            leftHand.x,
                            leftHand.y + PlayerRiseRules.CrawlHandReachMetres * leftSwing),
                        1f,
                        new Vector2(
                            rightHand.x,
                            rightHand.y + PlayerRiseRules.CrawlHandReachMetres * rightSwing),
                        false,
                        leadFoot,
                        PlayerRiseStepPose.None,
                        PlayerRiseRules.CrawlHeadLiftDegrees,
                        Vector2.zero,
                        0f,
                        false,
                        PlayerRiseRules.CrawlHandLiftMetres * leftSwing,
                        PlayerRiseRules.CrawlHandLiftMetres * rightSwing,
                        velocity,
                        yaw,
                        firstHalf ? swinging : PlayerCrawlLimb.Planted,
                        firstHalf ? PlayerCrawlLimb.Planted : swinging,
                        firstHalf ? PlayerCrawlLimb.Planted : swinging,
                        firstHalf ? swinging : PlayerCrawlLimb.Planted);
                }

                case PlayerRiseStage.Kneeling:
                {
                    float p = Mathf.Clamp01(stageElapsed / kneelingSeconds);
                    float stepPhase = Mathf.Clamp01((p - 0.55f) / 0.45f);
                    bool handOnKnee = p >= 0.6f;
                    var step = new PlayerRiseStepPose(
                        stepPhase > 0f,
                        leadFoot,
                        new Vector2(
                            leadSign * PlayerRiseRules.LeadStepSideMetres,
                            PlayerRiseRules.LeadStepForwardMetres),
                        PlayerRiseRules.LeadStepLift * Mathf.Sin(stepPhase * Mathf.PI),
                        stepPhase);
                    return new PlayerRiseOutput(
                        stage,
                        p,
                        PlayerRiseRules.StirringShare +
                        PlayerRiseRules.PushingUpShare +
                        PlayerRiseRules.KneelingShare * p,
                        Mathf.Lerp(PlayerRiseRules.AllFoursKey, PlayerRiseRules.HalfKneelKey, p),
                        1f,
                        0f,
                        1f,
                        leftHand,
                        1f,
                        rightHand,
                        handOnKnee,
                        leadFoot,
                        step,
                        0f,
                        Vector2.zero,
                        0f,
                        false);
                }

                case PlayerRiseStage.Standing:
                {
                    float p = Mathf.Clamp01(stageElapsed / standingSeconds);
                    float hands = 1f - Mathf.Clamp01(
                        stageElapsed / PlayerRiseRules.HandsReleaseSeconds);
                    float wobbleGain = Mathf.Clamp01((p - 0.7f) / 0.3f);
                    float rate = Mathf.PI * 2f * PlayerRiseRules.WobbleHertz;
                    float wobble = PlayerRiseRules.WobbleDegrees(intoxication) *
                                   wobbleGain *
                                   Mathf.Sin(rate * stageElapsed + wobblePhase);
                    return new PlayerRiseOutput(
                        stage,
                        p,
                        PlayerRiseRules.StirringShare +
                        PlayerRiseRules.PushingUpShare +
                        PlayerRiseRules.KneelingShare +
                        PlayerRiseRules.StandingShare * p,
                        Mathf.Lerp(PlayerRiseRules.HalfKneelKey, PlayerRiseRules.RelaxedKey, p),
                        1f,
                        0f,
                        hands,
                        leftHand,
                        hands,
                        rightHand,
                        hands > 0f,
                        leadFoot,
                        PlayerRiseStepPose.None,
                        0f,
                        new Vector2(wobble, wobble * 0.5f),
                        Mathf.Clamp01(p / 0.4f),
                        false);
                }

                default:
                    return new PlayerRiseOutput(
                        PlayerRiseStage.Done,
                        1f,
                        1f,
                        PlayerRiseRules.RelaxedKey,
                        1f,
                        0f,
                        0f,
                        leftHand,
                        0f,
                        rightHand,
                        false,
                        leadFoot,
                        PlayerRiseStepPose.None,
                        0f,
                        Vector2.zero,
                        1f,
                        false);
            }
        }

        private static float Unit(System.Random random)
        {
            return (float)random.NextDouble();
        }
    }
}
