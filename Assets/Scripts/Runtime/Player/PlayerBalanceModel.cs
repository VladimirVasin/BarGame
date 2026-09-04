using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// What the world tells the balance model this frame. Everything is
    /// in the hero's own frame: <c>x</c> right, <c>y</c> forward, metres
    /// and seconds.
    /// </summary>
    public readonly struct PlayerBalanceInput
    {
        public PlayerBalanceInput(
            float intoxication,
            Vector2 planarVelocity,
            float turnInput,
            float runBlend,
            bool grounded,
            Vector2 slopeDownhill,
            bool sideContact,
            Vector2 contactNormal,
            float plantLeft,
            float plantRight,
            float kerbRiseAhead,
            bool fallAllowed,
            bool wallWithinReach,
            Vector2 wallNormal,
            bool handHolding)
        {
            Intoxication = Mathf.Clamp01(intoxication);
            PlanarVelocity = planarVelocity;
            TurnInput = Mathf.Clamp(turnInput, -1f, 1f);
            RunBlend = Mathf.Clamp01(runBlend);
            Grounded = grounded;
            SlopeDownhill = slopeDownhill;
            SideContact = sideContact;
            ContactNormal = contactNormal;
            PlantLeft = Mathf.Clamp01(plantLeft);
            PlantRight = Mathf.Clamp01(plantRight);
            KerbRiseAhead = Mathf.Max(0f, kerbRiseAhead);
            FallAllowed = fallAllowed;
            WallWithinReach = wallWithinReach;
            WallNormal = wallNormal;
            HandHolding = handHolding;
        }

        /// <summary>Standing still on flat ground, nothing nearby.</summary>
        public static PlayerBalanceInput Quiet(
            float intoxication,
            bool fallAllowed = true)
        {
            return new PlayerBalanceInput(
                intoxication,
                Vector2.zero,
                0f,
                0f,
                true,
                Vector2.zero,
                false,
                Vector2.zero,
                1f,
                1f,
                0f,
                fallAllowed,
                false,
                Vector2.zero,
                false);
        }

        public PlayerBalanceInput WithTurnInput(float turnInput)
        {
            return new PlayerBalanceInput(
                Intoxication,
                PlanarVelocity,
                turnInput,
                RunBlend,
                Grounded,
                SlopeDownhill,
                SideContact,
                ContactNormal,
                PlantLeft,
                PlantRight,
                KerbRiseAhead,
                FallAllowed,
                WallWithinReach,
                WallNormal,
                HandHolding);
        }

        public float Intoxication { get; }
        public Vector2 PlanarVelocity { get; }

        /// <summary>The A/D axis: positive steers right.</summary>
        public float TurnInput { get; }
        public float RunBlend { get; }
        public bool Grounded { get; }

        /// <summary>Horizontal downhill direction scaled by the slope's tangent.</summary>
        public Vector2 SlopeDownhill { get; }

        /// <summary>The capsule touched something beside it this frame.</summary>
        public bool SideContact { get; }

        /// <summary>Planar normal of that contact, pointing away from the wall.</summary>
        public Vector2 ContactNormal { get; }
        public float PlantLeft { get; }
        public float PlantRight { get; }

        /// <summary>Rise of the ground ahead of the swinging boot, metres.</summary>
        public float KerbRiseAhead { get; }

        /// <summary>False on stairs and slopes the Rise clip cannot recover on.</summary>
        public bool FallAllowed { get; }

        /// <summary>A wall the hand could reach on the side he is tipping to.</summary>
        public bool WallWithinReach { get; }

        /// <summary>Planar normal of that wall, pointing away from it.</summary>
        public Vector2 WallNormal { get; }

        /// <summary>The hand is on the wall and taking weight.</summary>
        public bool HandHolding { get; }
    }

    /// <summary>A recovery step in flight, in the hero's frame.</summary>
    public readonly struct BalanceStepCommand
    {
        public BalanceStepCommand(
            bool active,
            FootSide side,
            float progress,
            Vector2 from,
            Vector2 to,
            float lift)
        {
            Active = active;
            Side = side;
            Progress = Mathf.Clamp01(progress);
            From = from;
            To = to;
            Lift = lift;
        }

        public static BalanceStepCommand None => default;

        public bool Active { get; }
        public FootSide Side { get; }
        public float Progress { get; }
        public Vector2 From { get; }
        public Vector2 To { get; }
        public float Lift { get; }
    }

    /// <summary>
    /// Where the model is in the fight for balance. <c>Steady</c> and
    /// <c>Recovering</c> are the ordinary stagger (the latter with the
    /// brace arms still coming down after a save); <c>Toppling</c> is the
    /// half second to a second and a half in which the ankles have given
    /// up and only a lunge or the torso can still catch him; <c>Fallen</c>
    /// is the latch the ragdoll takes over from.
    /// </summary>
    public enum BalancePhase
    {
        Steady = 0,
        Recovering,
        Toppling,
        Fallen
    }

    /// <summary>Why a topple ended in a fall.</summary>
    public enum BalanceFallCause
    {
        None = 0,

        /// <summary>The body leaned past the point of no return.</summary>
        PointOfNoReturn,

        /// <summary>The topple went on longer than any save takes.</summary>
        ToppleTimeout,

        /// <summary>Pinned against a wall the hand never caught.</summary>
        Blocked,

        /// <summary>The capsule hit something mid-topple.</summary>
        Stopped,

        /// <summary>Both lunges landed and he was still going.</summary>
        LungesSpent,

        /// <summary>After a lunge the capture point was beyond even a lunge.</summary>
        BeyondLunge,

        /// <summary>A test or debug seam latched it.</summary>
        Forced
    }

    /// <summary>What the presentation, motor and camera read from the model.</summary>
    public readonly struct PlayerBalanceOutput
    {
        public PlayerBalanceOutput(
            Vector2 driftVelocity,
            float leanRollDegrees,
            float leanPitchDegrees,
            float instability,
            float armReaction,
            BalanceStepCommand step,
            bool wallSupport,
            bool lostBalance,
            float fallDirection,
            float crouchMetres,
            float headingWeaveDegrees,
            Vector2 leftFoot,
            Vector2 rightFoot,
            Vector2 capturePoint,
            Vector2 torsoReactionDegrees = default,
            BalancePhase phase = BalancePhase.Steady,
            float braceWeight = 0f,
            Vector2 fallAxis = default,
            Vector2 fallVelocity = default,
            float fallLeanDegrees = 0f,
            float fallAngularVelocity = 0f,
            Vector2 supportCentre = default,
            Vector2 centreOfPressure = default)
        {
            CentreOfPressure = centreOfPressure;
            DriftVelocity = driftVelocity;
            LeanRollDegrees = leanRollDegrees;
            LeanPitchDegrees = leanPitchDegrees;
            Instability = Mathf.Clamp01(instability);
            ArmReaction = Mathf.Clamp01(armReaction);
            Step = step;
            WallSupport = wallSupport;
            LostBalance = lostBalance;
            FallDirection = fallDirection;
            CrouchMetres = Mathf.Max(0f, crouchMetres);
            HeadingWeaveDegrees = headingWeaveDegrees;
            LeftFoot = leftFoot;
            RightFoot = rightFoot;
            CapturePoint = capturePoint;
            TorsoReactionDegrees = torsoReactionDegrees;
            Phase = phase;
            BraceWeight = Mathf.Clamp01(braceWeight);
            FallAxis = fallAxis;
            FallVelocity = fallVelocity;
            FallLeanDegrees = fallLeanDegrees;
            FallAngularVelocity = fallAngularVelocity;
            SupportCentre = supportCentre;
        }

        public static PlayerBalanceOutput Still =>
            new PlayerBalanceOutput(
                Vector2.zero,
                0f,
                0f,
                0f,
                0f,
                BalanceStepCommand.None,
                false,
                false,
                1f,
                0f,
                0f,
                PlayerBalanceModel.DefaultLeftFoot,
                PlayerBalanceModel.DefaultRightFoot,
                Vector2.zero);

        /// <summary>Root velocity to add to the motor, hero frame, m/s.</summary>
        public Vector2 DriftVelocity { get; }

        /// <summary>Pelvis roll toward the lean side, degrees (positive = right).</summary>
        public float LeanRollDegrees { get; }

        /// <summary>Pelvis pitch, degrees (positive = forward).</summary>
        public float LeanPitchDegrees { get; }
        public float Instability { get; }
        public float ArmReaction { get; }
        public BalanceStepCommand Step { get; }
        public bool WallSupport { get; }
        public bool LostBalance { get; }
        public float FallDirection { get; }
        public float CrouchMetres { get; }

        /// <summary>Heading bias for the walking line, degrees.</summary>
        public float HeadingWeaveDegrees { get; }
        public Vector2 LeftFoot { get; }
        public Vector2 RightFoot { get; }
        public Vector2 CapturePoint { get; }

        /// <summary>
        /// The torso's hip-strategy whip, degrees off the legs: <c>x</c> a
        /// roll (positive right), <c>y</c> a pitch (positive forward) —
        /// in the sense of the fall it is fighting.
        /// </summary>
        public Vector2 TorsoReactionDegrees { get; }
        public BalancePhase Phase { get; }

        /// <summary>How far the hands have gone out toward the ground (<c>0..1</c>).</summary>
        public float BraceWeight { get; }

        /// <summary>Planar unit direction of the fall, hero frame; valid once <see cref="Phase"/> is past Steady.</summary>
        public Vector2 FallAxis { get; }

        /// <summary>COM velocity at the moment of the fall, hero frame, m/s.</summary>
        public Vector2 FallVelocity { get; }

        /// <summary>Body lean at the moment of the fall, degrees.</summary>
        public float FallLeanDegrees { get; }

        /// <summary>Angular speed about the support edge at the fall, rad/s.</summary>
        public float FallAngularVelocity { get; }

        /// <summary>Midpoint of the two boots, hero frame.</summary>
        public Vector2 SupportCentre { get; }

        /// <summary>Where the weight is on the ground, hero frame: the pivot of a topple.</summary>
        public Vector2 CentreOfPressure { get; }
    }

    /// <summary>
    /// The drunk hero as a linear inverted pendulum: a centre of mass a
    /// little under a metre up, a centre of pressure the ankles move late
    /// and imprecisely, a seeded disturbance that grows with the drink,
    /// and a capture point that tells the model when a step is needed,
    /// where it must land, and when nothing can save him.
    ///
    /// Three strategies fight the disturbance, in the order a body uses
    /// them: the ankles (the centre of pressure chasing the capture
    /// point inside the boots), the hips (a torso-and-arms flywheel spun
    /// in the sense of the fall, worth a bounded extra centre of
    /// pressure), and the feet (a recovery step past the capture point).
    /// When the capture point escapes even a step, the model does not
    /// give up: it enters a topple, in which the root follows the falling
    /// centre of mass, the torso whips at its full budget, and one or two
    /// lunges — longer, slower, and aimed with a drunk's error — try to
    /// get a boot under him. Only past the point of no return, or when
    /// the lunges are spent, does it latch a fall, and then it hands the
    /// ragdoll the velocity the body had.
    ///
    /// Pure C#, fixed step, seeded: two models with the same seed and the
    /// same inputs produce the same steps and the same fall. Sober is
    /// exactly inert — no noise, no coupling to the steering input, no
    /// slope bias — so the model changes nothing until a drink lands.
    /// </summary>
    public sealed class PlayerBalanceModel
    {
        public const float FixedStep = PlayerBalanceRules.FixedStep;
        public static readonly Vector2 DefaultLeftFoot = new Vector2(-0.10f, 0f);
        public static readonly Vector2 DefaultRightFoot = new Vector2(0.10f, 0f);

        /// <summary>COM offset below which no root creep is emitted.</summary>
        public const float CreepDeadZone = 0.04f;


        /// <summary>How long a step waits for the clip to lift the stance foot.</summary>
        public const float StanceFootWaitSeconds = 0.12f;
        public const float StanceFootWaitUrgency = 1.6f;

        /// <summary>Nominal stance width after a gather step.</summary>
        public const float GatherStanceWidth = 0.20f;

        /// <summary>The COM velocity that survives a step's landing.</summary>
        public const float LandingVelocityRetention = 0.5f;

        /// <summary>
        /// How much filtered white noise rides on the seeded sways. The
        /// sways are the stagger everyone sees; the noise is what makes
        /// two episodes differ, and too much of it is a shove out of
        /// nowhere that no step could ever have caught.
        /// </summary>
        public const float WhiteNoiseGain = 1.5f;

        private readonly System.Random random;
        private readonly float[] swayPhases = new float[4];
        private readonly float[] swayFrequencies = new float[4];

        private float accumulator;
        private Vector2 com;
        private Vector2 comVelocity;
        private Vector2 cop;
        private Vector2 noiseState1;
        private Vector2 noiseState2;
        private Vector2 leftFoot = DefaultLeftFoot;
        private Vector2 rightFoot = DefaultRightFoot;
        private Vector2 rootShift;
        private bool stepActive;
        private FootSide stepSide;
        private Vector2 stepFrom;
        private Vector2 stepTo;
        private float stepDuration;
        private float stepElapsed;
        private float stepLift;
        private Vector2 stepDrift;
        private bool stepIsLunge;
        private float graceSeconds;
        private float reactionTimer;
        private float stanceWaitTimer;
        private float gatherTimer;
        private float tripTimer;
        private float tripAcceleration;
        private float blockedTimer;
        private float elapsed;
        private float instability;
        private bool wallSupport;
        private bool lostBalance;
        private float fallDirection = 1f;
        private bool previousSideContact;
        private Vector2 pendingImpulse;
        private int stepsTaken;
        private int stumbles;
        private Vector2 flywheelAngle;
        private Vector2 flywheelVelocity;
        private Vector2 flywheelCommand;
        private bool flywheelSpent;
        private Vector2 leanReference;
        private BalancePhase phase = BalancePhase.Steady;
        private float toppleElapsed;
        private int lungesTaken;
        private float recoveringTimer;
        private float braceWeight;
        private Vector2 leanVector;
        private float leanDegrees;
        private Vector2 supportCentre;
        private Vector2 fallAxis = Vector2.right;
        private Vector2 fallVelocity;
        private float fallLeanDegrees;
        private float fallAngularVelocity;
        private BalanceFallCause fallCause;
        private int topples;
        private PlayerBalanceSettings lastSettings =
            PlayerBalanceSettings.FromIntoxication(0f);
        private PlayerBalanceOutput output = PlayerBalanceOutput.Still;

        public PlayerBalanceModel(int seed)
        {
            Seed = seed;
            random = new System.Random(seed);
            for (int index = 0; index < swayPhases.Length; index++)
            {
                swayPhases[index] = (float)(random.NextDouble() * Mathf.PI * 2f);
                swayFrequencies[index] = Mathf.Lerp(
                    0.8f,
                    1.25f,
                    (float)random.NextDouble());
            }
        }

        public int Seed { get; }
        public PlayerBalanceOutput Output => output;
        public Vector2 ComOffset => com;
        public Vector2 ComVelocity => comVelocity;
        public Vector2 CentreOfPressure => cop;
        public float Instability => instability;
        public bool LostBalance => lostBalance;
        public float FallDirection => fallDirection;
        public bool StepActive => stepActive;
        public FootSide StepSide => stepSide;
        public float GraceSeconds => graceSeconds;
        public int StepsTaken => stepsTaken;
        public int Stumbles => stumbles;
        public Vector2 LeftFoot => leftFoot - rootShift;
        public Vector2 RightFoot => rightFoot - rootShift;

        /// <summary>The torso flywheel, radians off the legs (x roll, y pitch).</summary>
        public Vector2 FlywheelAngle => flywheelAngle;
        public Vector2 FlywheelVelocity => flywheelVelocity;
        public BalancePhase Phase => phase;

        /// <summary>Seconds spent in the current topple.</summary>
        public float ToppleElapsed => toppleElapsed;

        /// <summary>Lunges taken in the current topple.</summary>
        public int LungesTaken => lungesTaken;

        /// <summary>The step in flight is a lunge.</summary>
        public bool StepIsLunge => stepActive && stepIsLunge;

        /// <summary>Topples entered since the last reset, recovered or not.</summary>
        public int Topples => topples;
        public float BraceWeight => braceWeight;
        public Vector2 FallAxis => fallAxis;
        public Vector2 FallVelocity => fallVelocity;
        public float FallLeanDegrees => fallLeanDegrees;
        public float FallAngularVelocity => fallAngularVelocity;
        public BalanceFallCause FallCause => fallCause;
        public Vector2 SupportCentre => supportCentre;

        /// <summary>Body lean from what is holding him up, degrees.</summary>
        public float LeanDegrees => leanDegrees;

        /// <summary>
        /// The point the lean is measured from, hero frame: the boots'
        /// midpoint in a stance, the boot under the pressure once they
        /// are split.
        /// </summary>
        public Vector2 LeanReference => leanReference;

        /// <summary>Balance cannot be lost for this long (after a fall, a modal).</summary>
        public void ArmGrace(float seconds)
        {
            graceSeconds = Mathf.Max(graceSeconds, seconds);
        }

        /// <summary>A shove: adds velocity to the centre of mass, hero frame.</summary>
        public void InjectPerturbation(Vector2 velocity)
        {
            pendingImpulse += velocity;
        }

        /// <summary>
        /// Test and debug seam: latches a fall at once, with the inertia
        /// a modest sideways topple would have carried so the ragdoll
        /// still starts moving.
        /// </summary>
        public void ForceLoseBalance(float direction)
        {
            float sign = direction < 0f ? -1f : 1f;
            phase = BalancePhase.Fallen;
            lostBalance = true;
            fallDirection = sign;
            fallAxis = new Vector2(sign, 0f);
            fallVelocity = new Vector2(
                sign * PlayerBalanceRules.ForcedFallVelocity,
                0f);
            fallLeanDegrees = PlayerBalanceRules.ForcedFallLeanDegrees;
            fallAngularVelocity = PlayerBalanceRules.FallAngularVelocity(
                PlayerBalanceRules.ForcedFallVelocity,
                fallLeanDegrees,
                lastSettings.ComHeight);
            leanVector = fallAxis * (
                Mathf.Tan(fallLeanDegrees * Mathf.Deg2Rad) *
                lastSettings.ComHeight);
            leanDegrees = fallLeanDegrees;
            fallCause = BalanceFallCause.Forced;
            braceWeight = 1f;
            instability = 1f;
            stepActive = false;
            stepDrift = Vector2.zero;
            output = BuildOutput(Vector2.zero, 0f);
        }

        /// <summary>Back to standing still, feet under him, nothing owed.</summary>
        public void Reset()
        {
            accumulator = 0f;
            com = Vector2.zero;
            comVelocity = Vector2.zero;
            cop = Vector2.zero;
            noiseState1 = Vector2.zero;
            noiseState2 = Vector2.zero;
            leftFoot = DefaultLeftFoot;
            rightFoot = DefaultRightFoot;
            rootShift = Vector2.zero;
            stepActive = false;
            stepElapsed = 0f;
            stepDrift = Vector2.zero;
            stepIsLunge = false;
            reactionTimer = 0f;
            stanceWaitTimer = 0f;
            gatherTimer = 0f;
            tripTimer = 0f;
            tripAcceleration = 0f;
            blockedTimer = 0f;
            instability = 0f;
            wallSupport = false;
            lostBalance = false;
            fallDirection = 1f;
            previousSideContact = false;
            pendingImpulse = Vector2.zero;
            flywheelAngle = Vector2.zero;
            flywheelVelocity = Vector2.zero;
            flywheelCommand = Vector2.zero;
            flywheelSpent = false;
            phase = BalancePhase.Steady;
            toppleElapsed = 0f;
            lungesTaken = 0;
            recoveringTimer = 0f;
            braceWeight = 0f;
            leanVector = Vector2.zero;
            leanReference = Vector2.zero;
            leanDegrees = 0f;
            supportCentre = Vector2.zero;
            fallAxis = Vector2.right;
            fallVelocity = Vector2.zero;
            fallLeanDegrees = 0f;
            fallAngularVelocity = 0f;
            fallCause = BalanceFallCause.None;
            topples = 0;
            output = PlayerBalanceOutput.Still;
        }

        public void Advance(float deltaTime, in PlayerBalanceInput input)
        {
            Advance(
                deltaTime,
                input,
                PlayerBalanceSettings.FromIntoxication(input.Intoxication));
        }

        /// <summary>
        /// The same advance with an explicit tuning row — the seam the
        /// tests and the offline simulation use to vary one knob.
        /// </summary>
        public void Advance(
            float deltaTime,
            in PlayerBalanceInput input,
            in PlayerBalanceSettings settings)
        {
            if (deltaTime <= 0f || float.IsNaN(deltaTime))
            {
                return;
            }

            lastSettings = settings;
            accumulator += Mathf.Min(deltaTime, 0.25f);
            Vector2 drift = output.DriftVelocity;
            float weave = output.HeadingWeaveDegrees;
            while (accumulator >= FixedStep)
            {
                accumulator -= FixedStep;
                Step(FixedStep, input, settings, out drift, out weave);
            }

            output = BuildOutput(drift, weave);
        }

        private void Step(
            float h,
            in PlayerBalanceInput input,
            in PlayerBalanceSettings settings,
            out Vector2 drift,
            out float weave)
        {
            elapsed += h;
            graceSeconds = Mathf.Max(0f, graceSeconds - h);
            reactionTimer = Mathf.Max(0f, reactionTimer - h);
            bool drunk = settings.Intoxication > 0f;
            float omega = settings.Omega;
            drift = Vector2.zero;
            weave = 0f;

            if (!input.Grounded || phase == BalancePhase.Fallen)
            {
                comVelocity *= Mathf.Max(0f, 1f - 4f * h);
                com *= Mathf.Max(0f, 1f - 4f * h);
                if (!stepActive)
                {
                    RecentreFeet();
                }

                return;
            }

            if (!drunk)
            {
                // Sober: bit-exact rest. Nothing is integrated, so a
                // reapply or a long idle cannot creep.
                com = Vector2.zero;
                comVelocity = Vector2.zero;
                cop = Vector2.zero;
                noiseState1 = Vector2.zero;
                noiseState2 = Vector2.zero;
                instability = 0f;
                wallSupport = false;
                stepActive = false;
                RecentreFeet();
                pendingImpulse = Vector2.zero;
                previousSideContact = input.SideContact;
                flywheelAngle = Vector2.zero;
                flywheelVelocity = Vector2.zero;
                flywheelCommand = Vector2.zero;
                flywheelSpent = false;
                phase = BalancePhase.Steady;
                toppleElapsed = 0f;
                lungesTaken = 0;
                recoveringTimer = 0f;
                braceWeight = 0f;
                leanVector = Vector2.zero;
                leanReference = Vector2.zero;
                leanDegrees = 0f;
                supportCentre = Vector2.zero;
                return;
            }

            bool toppling = phase == BalancePhase.Toppling;

            // Disturbance: two slow seeded sways per axis plus a little
            // filtered white noise, all scaled by the level and by running.
            float amplitude = settings.NoiseAmplitude *
                              Mathf.Lerp(
                                  1f,
                                  settings.RunNoiseMultiplier,
                                  input.RunBlend);
            Vector2 white = new Vector2(
                (float)(random.NextDouble() * 2.0 - 1.0),
                (float)(random.NextDouble() * 2.0 - 1.0));
            float alpha = Mathf.Clamp01(
                Mathf.PI * 2f * settings.NoiseFrequency * h);
            noiseState1 += (white - noiseState1) * alpha;
            noiseState2 += (noiseState1 - noiseState2) * alpha;
            Vector2 disturbance = new Vector2(
                Mathf.Sin(elapsed * swayFrequencies[0] * settings.NoiseFrequency * Mathf.PI * 2f + swayPhases[0]) * 0.7f +
                Mathf.Sin(elapsed * swayFrequencies[1] * settings.NoiseFrequency * Mathf.PI * 1.3f + swayPhases[1]) * 0.5f,
                Mathf.Sin(elapsed * swayFrequencies[2] * settings.NoiseFrequency * Mathf.PI * 1.7f + swayPhases[2]) * 0.55f +
                Mathf.Sin(elapsed * swayFrequencies[3] * settings.NoiseFrequency * Mathf.PI * 2.4f + swayPhases[3]) * 0.35f);
            disturbance = (disturbance + noiseState2 * WhiteNoiseGain) * amplitude;

            // Support: both feet, or the stance foot alone mid-step, plus
            // the wall the hand is holding.
            Vector2 left = leftFoot - rootShift;
            Vector2 right = rightFoot - rootShift;
            BalanceSupportPolygon support = stepActive
                ? BalanceSupportPolygon.FromFoot(
                    stepSide == FootSide.Left ? right : left,
                    settings)
                : BalanceSupportPolygon.FromFeet(left, right, settings);
            bool handHolding = wallSupport && input.HandHolding;
            if (handHolding && input.WallNormal.sqrMagnitude > 0.0001f)
            {
                support = support.ExtendedToward(
                    -input.WallNormal,
                    PlayerBalanceRules.WallSupportReach);
            }

            // Ankle strategy: the centre of pressure chases the capture
            // point, late, and the A/D input shifts it sideways — pressing
            // toward the side he tips to is what brings him back.
            Vector2 capturePoint = PlayerBalanceRules.CapturePoint(
                com,
                comVelocity,
                omega);
            Vector2 desiredCop = support.Clamp(capturePoint);
            cop += (desiredCop - cop) *
                   Mathf.Min(1f, h / settings.ReactionDelay);
            Vector2 effectiveCop = cop +
                                   new Vector2(
                                       input.TurnInput * settings.InputCopShift,
                                       0f);

            // Hip strategy: the torso and arms are a flywheel. Spun in the
            // sense of the fall they push the hips back under him, worth
            // an extra centre of pressure of I·α/(m·g), until the angle
            // stop; then a slow spring unwinds them. The command lags by
            // the reaction delay — except in a topple, where there is no
            // time left to be slow about it.
            Vector2 flywheelTarget = PlayerBalanceRules.FlywheelCommand(
                capturePoint,
                support,
                settings.CaptureMargin,
                settings.FlywheelAcceleration);
            // Slow to start, quick to let go: the onset lags by the
            // reaction delay, the release is immediate. A torso at its
            // stop is spent — holding it there buys nothing — so it
            // unwinds even while the point is still out, and re-arms
            // once it has come most of the way back.
            if (flywheelSpent &&
                flywheelAngle.magnitude <
                PlayerBalanceRules.FlywheelMaximumRadians *
                PlayerBalanceRules.FlywheelRearmFraction)
            {
                flywheelSpent = false;
            }

            if (flywheelTarget.sqrMagnitude <= 0f || flywheelSpent)
            {
                flywheelCommand = Vector2.zero;
            }
            else if (toppling)
            {
                flywheelCommand = flywheelTarget;
            }
            else
            {
                flywheelCommand += (flywheelTarget - flywheelCommand) *
                                   Mathf.Min(1f, h / settings.FlywheelReactionDelay);
            }

            Vector2 flywheelAcceleration = flywheelCommand.sqrMagnitude > 0f
                ? flywheelCommand
                : PlayerBalanceRules.FlywheelReturn(flywheelAngle, flywheelVelocity);
            flywheelVelocity += flywheelAcceleration * h;
            flywheelAngle += flywheelVelocity * h;
            if (PlayerBalanceRules.ClampFlywheel(
                    ref flywheelAngle,
                    ref flywheelVelocity,
                    ref flywheelAcceleration))
            {
                flywheelSpent = true;
            }

            // Contacts: a wall met this step absorbs or bounces the COM.
            if (input.SideContact && !previousSideContact &&
                input.ContactNormal.sqrMagnitude > 0.0001f)
            {
                Vector2 normal = input.ContactNormal.normalized;
                float into = Vector2.Dot(comVelocity, normal);
                if (into < 0f)
                {
                    if (handHolding)
                    {
                        comVelocity -= normal * into * PlayerBalanceRules.WallAbsorb;
                    }
                    else
                    {
                        comVelocity -= normal * into * (1f + PlayerBalanceRules.WallBounce);
                        comVelocity += normal * PlayerBalanceRules.WallPushOff;
                    }
                }
            }

            previousSideContact = input.SideContact;

            // Kerb trip: a swinging boot catching a rise throws the COM
            // forward for a moment.
            if (tripTimer <= 0f && input.KerbRiseAhead > 0f)
            {
                float swingPlant = Mathf.Min(input.PlantLeft, input.PlantRight);
                float trip = PlayerBalanceRules.TripImpulse(
                    input.KerbRiseAhead,
                    settings.Intoxication);
                if (trip > 0f && swingPlant < 0.4f)
                {
                    tripAcceleration = trip;
                    tripTimer = PlayerBalanceRules.TripDuration;
                    stumbles++;
                }
            }

            Vector2 acceleration =
                (com - effectiveCop) * (omega * omega) +
                disturbance +
                input.SlopeDownhill *
                (PlayerBalanceSettings.Gravity * settings.SlopeBias) -
                flywheelAcceleration *
                (PlayerBalanceRules.FlywheelCopGain * omega * omega);
            if (tripTimer > 0f)
            {
                tripTimer -= h;
                acceleration.y += tripAcceleration;
            }

            comVelocity += acceleration * h;
            comVelocity += pendingImpulse;
            pendingImpulse = Vector2.zero;
            com += comVelocity * h;
            comVelocity *= Mathf.Max(0f, 1f - settings.CopDamping * h);

            capturePoint = PlayerBalanceRules.CapturePoint(
                com,
                comVelocity,
                omega);

            // Steps.
            if (toppling)
            {
                AdvanceTopplingSteps(h, input, settings, capturePoint, support, out drift);
            }
            else
            {
                AdvanceOrdinarySteps(h, input, settings, capturePoint, support, out drift);
            }

            // A landing this step moved the root, the boots and half the
            // momentum: the capture point and the polygon the phase is
            // judged against are the ones AFTER it, or a boot that has
            // just caught him reads as a lost cause for a step.
            capturePoint = PlayerBalanceRules.CapturePoint(
                com,
                comVelocity,
                omega);
            if (!stepActive)
            {
                support = BalanceSupportPolygon.FromFeet(
                    leftFoot - rootShift,
                    rightFoot - rootShift,
                    settings);
                if (handHolding && input.WallNormal.sqrMagnitude > 0.0001f)
                {
                    support = support.ExtendedToward(
                        -input.WallNormal,
                        PlayerBalanceRules.WallSupportReach);
                }
            }

            // Walls.
            wallSupport = input.WallWithinReach &&
                          (instability > PlayerBalanceRules.WallCatchInstability ||
                           input.SideContact);
            if ((input.SideContact || handHolding) &&
                input.ContactNormal.sqrMagnitude > 0.0001f)
            {
                float into = Vector2.Dot(drift, input.ContactNormal.normalized);
                if (into < 0f)
                {
                    drift -= input.ContactNormal.normalized * into;
                }
            }

            // Lean and instability. The lean is the centre of mass over
            // what is holding him up: the midpoint of the boots in a
            // stance, and — as the boots split into a lunge — the boot
            // the pressure is on, because a man in a wide split stands
            // over the foot that has his weight, not over the empty
            // ground between his feet. In a topple the root travels with
            // the centre of mass while the boots stay, so this is the
            // strut angle of the stance leg.
            left = leftFoot - rootShift;
            right = rightFoot - rootShift;
            supportCentre = (left + right) * 0.5f;
            float split = Mathf.Clamp01(
                (Vector2.Distance(left, right) - PlayerBalanceRules.NominalStanceMetres) /
                PlayerBalanceRules.SplitStanceMetres);
            leanReference = Vector2.Lerp(supportCentre, cop, split);
            leanVector = com - leanReference;
            leanDegrees = PlayerBalanceRules.LeanDegrees(
                leanVector.magnitude,
                settings.ComHeight);
            float excursion = support.Excursion(capturePoint);
            instability = Mathf.Max(
                Mathf.Clamp01(
                    excursion /
                    (settings.MaximumStepReach *
                     PlayerBalanceRules.RecoverableReachFraction)),
                Mathf.Clamp01(leanDegrees / settings.FallLeanDegrees));

            UpdatePhase(h, input, settings, capturePoint, support, excursion, left, right, ref drift);

            weave = settings.HeadingWeaveDegrees *
                    Mathf.Sin(
                        elapsed * settings.HeadingWeaveFrequency * Mathf.PI * 2f +
                        swayPhases[0]);
        }

        /// <summary>
        /// The ordinary stagger's feet: a step in flight carries the root
        /// at half its speed, a landed pair gathers, a capture point past
        /// the boots plans a recovery step, and the root creeps under the
        /// centre of mass between steps.
        /// </summary>
        private void AdvanceOrdinarySteps(
            float h,
            in PlayerBalanceInput input,
            in PlayerBalanceSettings settings,
            Vector2 capturePoint,
            in BalanceSupportPolygon support,
            out Vector2 drift)
        {
            drift = Vector2.zero;
            if (stepActive)
            {
                drift = stepDrift;
                rootShift += stepDrift * h;
                com -= stepDrift * h;
                AdvanceStepInFlight(h, settings);
                return;
            }

            if (gatherTimer > 0f)
            {
                gatherTimer -= h;
                if (gatherTimer <= 0f)
                {
                    PlanGatherStep(settings);
                }
            }

            if (!stepActive &&
                PlayerBalanceRules.NeedsStep(
                    capturePoint,
                    support,
                    settings.CaptureMargin))
            {
                // The step is judged against where the capture point will
                // be when the boot lands. If an ordinary step cannot get
                // there, this is no stagger any more: the topple begins
                // here, with a lunge thrown at once — no reaction delay
                // and no waiting for the clip to free the boot, because
                // the point runs away as e^(ω·t).
                Vector2 predicted = PlayerBalanceRules.PredictedCapturePoint(
                    capturePoint,
                    support,
                    settings.Omega,
                    settings.StepDuration * PlayerBalanceRules.LungePredictionFraction);
                bool emergency = graceSeconds <= 0f &&
                                 input.FallAllowed &&
                                 !PlayerBalanceRules.CanRecoverByStep(
                                     predicted,
                                     support,
                                     settings);
                if (emergency)
                {
                    EnterTopple(capturePoint);
                    PlanLunge(input, settings, capturePoint, support);
                    return;
                }

                if (reactionTimer > 0f)
                {
                    return;
                }

                FootSide swingPreference = input.PlantLeft <= input.PlantRight
                    ? FootSide.Left
                    : FootSide.Right;
                FootSide side = PlayerBalanceRules.StepSide(
                    capturePoint,
                    support,
                    swingPreference);
                float plantOfSide = side == FootSide.Left
                    ? input.PlantLeft
                    : input.PlantRight;
                float plantOfOther = side == FootSide.Left
                    ? input.PlantRight
                    : input.PlantLeft;
                float urgency = support.Excursion(capturePoint) /
                                Mathf.Max(0.01f, settings.MaximumStepReach * 0.5f);
                bool clipHoldsThatFoot =
                    plantOfSide > 0.6f && plantOfOther < 0.6f;
                if (clipHoldsThatFoot &&
                    urgency < StanceFootWaitUrgency &&
                    stanceWaitTimer < StanceFootWaitSeconds)
                {
                    stanceWaitTimer += h;
                }
                else
                {
                    PlanStep(side, capturePoint, urgency, settings);
                }
            }
            else
            {
                stanceWaitTimer = 0f;
            }

            if (!stepActive && com.magnitude > CreepDeadZone)
            {
                drift = Vector2.ClampMagnitude(
                    com * settings.DriftGain,
                    settings.CreepLimit);
            }
        }

        /// <summary>
        /// The topple's feet: the root travels with the falling centre of
        /// mass (the boots stay where they were planted and the legs
        /// stretch after him), and while the lunge budget lasts a boot is
        /// thrown at where the capture point will be — at once, with no
        /// reaction delay to wait out, because the point runs away as
        /// e^(ω·t) and every hundredth of a second is a centimetre of
        /// reach lost. A step already in the air is redirected into the
        /// lunge rather than waited for.
        /// </summary>
        private void AdvanceTopplingSteps(
            float h,
            in PlayerBalanceInput input,
            in PlayerBalanceSettings settings,
            Vector2 capturePoint,
            in BalanceSupportPolygon support,
            out Vector2 drift)
        {
            drift = comVelocity;
            rootShift += drift * h;
            com -= drift * h;
            if (stepActive)
            {
                if (!stepIsLunge &&
                    lungesTaken < PlayerBalanceRules.MaximumLunges &&
                    support.Excursion(capturePoint) > 0f)
                {
                    RedirectStepIntoLunge(input, settings, capturePoint, support);
                }

                AdvanceStepInFlight(h, settings);
                return;
            }

            if (lungesTaken < PlayerBalanceRules.MaximumLunges &&
                support.Excursion(capturePoint) > 0f)
            {
                PlanLunge(input, settings, capturePoint, support);
            }
        }

        private void AdvanceStepInFlight(
            float h,
            in PlayerBalanceSettings settings)
        {
            stepElapsed += h;
            float progress = Mathf.Clamp01(stepElapsed / stepDuration);
            Vector2 footNow = Vector2.Lerp(
                stepFrom,
                stepTo,
                Mathf.SmoothStep(0f, 1f, progress));
            if (stepSide == FootSide.Left)
            {
                leftFoot = footNow;
            }
            else
            {
                rightFoot = footNow;
            }

            if (progress >= 1f)
            {
                FinishStep(settings);
            }
        }

        /// <summary>
        /// Steady or Recovering into Toppling when the ankles and an
        /// ordinary step are beaten; Toppling into Recovering when a
        /// lunge or the torso has put the capture point back between the
        /// boots with the body not too far over; Toppling into Fallen at
        /// the point of no return, when the lunges are spent, when the
        /// topple has gone on too long, or when a wall stops the body.
        /// Where falls are not allowed the capture point is pinned instead
        /// and the whole fight is called off.
        /// </summary>
        private void UpdatePhase(
            float h,
            in PlayerBalanceInput input,
            in PlayerBalanceSettings settings,
            Vector2 capturePoint,
            in BalanceSupportPolygon support,
            float excursion,
            Vector2 left,
            Vector2 right,
            ref Vector2 drift)
        {
            bool handHolding = wallSupport && input.HandHolding;
            if (!input.FallAllowed)
            {
                BalanceSupportPolygon recoverable =
                    PlayerBalanceRules.RecoverablePolygon(support, settings);
                if (!recoverable.Contains(capturePoint))
                {
                    Vector2 pinned = recoverable.Clamp(capturePoint);
                    comVelocity = (pinned - com) * PlayerBalanceRules.Omega(settings.ComHeight);
                }

                instability = Mathf.Min(instability, 0.85f);
                drift.y = 0f;
                blockedTimer = 0f;
                if (phase == BalancePhase.Toppling)
                {
                    // The ground under him changed its mind mid-topple
                    // (a stair, a slope): the fight is called off and he
                    // is only staggering again.
                    phase = BalancePhase.Steady;
                    lungesTaken = 0;
                    toppleElapsed = 0f;
                }

                recoveringTimer = 0f;
                braceWeight = 0f;
                return;
            }

            bool blockedSide = excursion > 0f &&
                               input.WallWithinReach &&
                               !handHolding &&
                               Vector2.Dot(
                                   capturePoint,
                                   -input.WallNormal) > 0f;
            blockedTimer = blockedSide ? blockedTimer + h : 0f;

            if (phase == BalancePhase.Toppling)
            {
                toppleElapsed += h;
                instability = 1f;
                braceWeight = Mathf.Max(
                    braceWeight,
                    PlayerBalanceRules.BraceWeight(leanDegrees));

                bool beyondLunge = !stepActive &&
                                   lungesTaken > 0 &&
                                   !PlayerBalanceRules.CanRecoverByStep(
                                       capturePoint,
                                       support,
                                       settings.WithStepReach(
                                           PlayerBalanceRules.LungeReachMultiplier));
                bool lungesSpent = !stepActive &&
                                   lungesTaken >= PlayerBalanceRules.MaximumLunges &&
                                   excursion > settings.CaptureMargin;
                bool stopped = input.SideContact && !handHolding;
                BalanceFallCause cause = BalanceFallCause.None;
                if (leanDegrees > PlayerBalanceRules.PointOfNoReturnDegrees)
                {
                    cause = BalanceFallCause.PointOfNoReturn;
                }
                else if (toppleElapsed > PlayerBalanceRules.MaximumToppleSeconds)
                {
                    cause = BalanceFallCause.ToppleTimeout;
                }
                else if (blockedTimer > PlayerBalanceRules.BlockedFallSeconds)
                {
                    cause = BalanceFallCause.Blocked;
                }
                else if (stopped)
                {
                    cause = BalanceFallCause.Stopped;
                }
                else if (lungesSpent)
                {
                    cause = BalanceFallCause.LungesSpent;
                }
                else if (beyondLunge)
                {
                    cause = BalanceFallCause.BeyondLunge;
                }

                if (cause != BalanceFallCause.None)
                {
                    Fall(cause);
                    return;
                }

                if (toppleElapsed >= PlayerBalanceRules.MinimumToppleSeconds &&
                    !stepActive &&
                    excursion <= 0f &&
                    leanDegrees < PlayerBalanceRules.RecoverLeanDegrees)
                {
                    phase = BalancePhase.Recovering;
                    recoveringTimer = PlayerBalanceRules.RecoveringSeconds;
                }

                return;
            }

            if (phase == BalancePhase.Recovering)
            {
                recoveringTimer -= h;
                braceWeight = Mathf.MoveTowards(
                    braceWeight,
                    0f,
                    h / PlayerBalanceRules.BraceReleaseSeconds);
                if (recoveringTimer <= 0f)
                {
                    phase = BalancePhase.Steady;
                    recoveringTimer = 0f;
                }
            }
            else
            {
                braceWeight = 0f;
            }

            // While a step is in flight the recovery IS the step: judge
            // the capture point against the polygon the landing will make,
            // not against the lone stance foot, or every step away from
            // the stance foot reads as a lost cause the moment it starts.
            BalanceSupportPolygon recoverySupport = stepActive
                ? BalanceSupportPolygon.FromFeet(
                    stepSide == FootSide.Left ? right : left,
                    stepTo - rootShift,
                    settings)
                : support;
            bool cannotStep = !PlayerBalanceRules.CanRecoverByStep(
                capturePoint,
                recoverySupport,
                settings);
            if (graceSeconds <= 0f &&
                (leanDegrees > settings.FallLeanDegrees ||
                 cannotStep ||
                 blockedTimer > PlayerBalanceRules.BlockedFallSeconds))
            {
                EnterTopple(capturePoint);
            }
        }

        private void EnterTopple(Vector2 capturePoint)
        {
            if (phase == BalancePhase.Toppling)
            {
                return;
            }

            phase = BalancePhase.Toppling;
            toppleElapsed = 0f;
            lungesTaken = 0;
            recoveringTimer = 0f;
            topples++;
            instability = 1f;
            Vector2 axis = capturePoint - leanReference;
            fallAxis = axis.sqrMagnitude > 0.000001f
                ? axis.normalized
                : new Vector2(PlayerBalanceRules.FallDirection(capturePoint), 0f);
        }

        /// <summary>The latch, and everything the ragdoll needs to carry on the motion.</summary>
        private void Fall(BalanceFallCause cause)
        {
            phase = BalancePhase.Fallen;
            lostBalance = true;
            fallCause = cause;
            Vector2 axis = leanVector.sqrMagnitude > 0.000001f
                ? leanVector.normalized
                : fallAxis;
            fallAxis = axis.sqrMagnitude > 0.000001f
                ? axis
                : Vector2.right;
            fallDirection = PlayerBalanceRules.FallDirection(fallAxis);
            // A body past the point of no return does not move toward
            // upright: whatever the last landing left of a velocity back
            // over the boots is not handed on.
            Vector2 velocity = comVelocity;
            float toward = Vector2.Dot(velocity, fallAxis);
            if (toward < 0f)
            {
                velocity -= fallAxis * toward;
            }

            fallVelocity = velocity;
            fallLeanDegrees = leanDegrees;
            fallAngularVelocity = PlayerBalanceRules.FallAngularVelocity(
                velocity.magnitude,
                leanDegrees,
                lastSettings.ComHeight);
            braceWeight = 1f;
            instability = 1f;
        }

        private void PlanStep(
            FootSide side,
            Vector2 capturePoint,
            float urgency,
            in PlayerBalanceSettings settings)
        {
            Vector2 left = leftFoot - rootShift;
            Vector2 right = rightFoot - rootShift;
            RecentreFeet();
            Vector2 from = side == FootSide.Left ? left : right;
            Vector2 other = side == FootSide.Left ? right : left;
            Vector2 to = PlayerBalanceRules.StepTarget(
                capturePoint,
                other,
                side,
                settings);
            BeginStep(side, from, to, settings.StepDuration,
                PlayerBalanceRules.StepLiftBase +
                PlayerBalanceRules.StepLiftPerUrgency *
                Mathf.Clamp(urgency, 0f, 2f));
        }

        /// <summary>
        /// A lunge: the topple's step. Longer than a stagger's, faster,
        /// higher, thrown past where the capture point will be when the
        /// boot lands, pulled by the A/D input, and aimed with an error
        /// the drink puts on it — drawn here, inside the fixed step, so
        /// the miss is the same at every frame rate.
        /// </summary>
        private void PlanLunge(
            in PlayerBalanceInput input,
            in PlayerBalanceSettings settings,
            Vector2 capturePoint,
            in BalanceSupportPolygon support)
        {
            FootSide swingPreference = input.PlantLeft <= input.PlantRight
                ? FootSide.Left
                : FootSide.Right;
            FootSide side = PlayerBalanceRules.StepSide(
                capturePoint,
                support,
                swingPreference);
            Vector2 left = leftFoot - rootShift;
            Vector2 right = rightFoot - rootShift;
            RecentreFeet();
            Vector2 from = side == FootSide.Left ? left : right;
            Vector2 other = side == FootSide.Left ? right : left;
            float duration = settings.StepDuration *
                             PlayerBalanceRules.LungeDurationMultiplier;
            Vector2 to = LungeTarget(
                input,
                settings,
                capturePoint,
                support,
                other,
                side,
                duration);
            BeginStep(side, from, to, duration, LungeLift());
            stepIsLunge = true;
            lungesTaken++;
        }

        /// <summary>
        /// The step already in the air becomes the lunge: same boot,
        /// carrying on from where it is now, to the lunge's target in the
        /// time a lunge would still have from here.
        /// </summary>
        private void RedirectStepIntoLunge(
            in PlayerBalanceInput input,
            in PlayerBalanceSettings settings,
            Vector2 capturePoint,
            in BalanceSupportPolygon support)
        {
            float progress = Mathf.Clamp01(stepElapsed / stepDuration);
            Vector2 left = leftFoot - rootShift;
            Vector2 right = rightFoot - rootShift;
            RecentreFeet();
            Vector2 from = stepSide == FootSide.Left ? left : right;
            Vector2 other = stepSide == FootSide.Left ? right : left;
            float remaining = Mathf.Max(
                0.05f,
                settings.StepDuration *
                PlayerBalanceRules.LungeDurationMultiplier *
                (1f - progress));
            Vector2 to = LungeTarget(
                input,
                settings,
                capturePoint,
                support,
                other,
                stepSide,
                remaining);
            stepFrom = from;
            stepTo = to;
            stepElapsed = 0f;
            stepDuration = remaining;
            stepLift = Mathf.Max(stepLift, LungeLift());
            stepDrift = (to - from) * 0.5f / stepDuration;
            stepIsLunge = true;
            lungesTaken++;
        }

        private Vector2 LungeTarget(
            in PlayerBalanceInput input,
            in PlayerBalanceSettings settings,
            Vector2 capturePoint,
            in BalanceSupportPolygon support,
            Vector2 otherFoot,
            FootSide side,
            float flightSeconds)
        {
            float error = PlayerBalanceRules.LungeAimErrorMetres * settings.Intoxication;
            Vector2 aimError = new Vector2(
                (float)(random.NextDouble() * 2.0 - 1.0) * error,
                (float)(random.NextDouble() * 2.0 - 1.0) * error);
            PlayerBalanceSettings lungeSettings = settings.WithStepReach(
                PlayerBalanceRules.LungeReachMultiplier);
            Vector2 predicted = PlayerBalanceRules.PredictedCapturePoint(
                capturePoint,
                support,
                settings.Omega,
                flightSeconds * PlayerBalanceRules.LungePredictionFraction);
            return PlayerBalanceRules.LungeTarget(
                predicted,
                otherFoot,
                side,
                input.TurnInput,
                aimError,
                lungeSettings);
        }

        private static float LungeLift()
        {
            return (PlayerBalanceRules.StepLiftBase +
                    PlayerBalanceRules.StepLiftPerUrgency * 2f) *
                   PlayerBalanceRules.LungeLiftMultiplier;
        }

        private void PlanGatherStep(in PlayerBalanceSettings settings)
        {
            RecentreFeet();
            float separation = Mathf.Abs(leftFoot.x - rightFoot.x);
            if (separation <= PlayerBalanceRules.GatherThreshold)
            {
                return;
            }

            // The trailing foot closes to the nominal stance beside the
            // one that stepped, so the next lurch starts from a stance.
            bool leftTrails = Mathf.Abs(leftFoot.x) > Mathf.Abs(rightFoot.x);
            FootSide side = leftTrails ? FootSide.Left : FootSide.Right;
            Vector2 anchor = leftTrails ? rightFoot : leftFoot;
            Vector2 from = leftTrails ? leftFoot : rightFoot;
            Vector2 to = new Vector2(
                anchor.x + (leftTrails ? -GatherStanceWidth : GatherStanceWidth),
                anchor.y);
            BeginStep(
                side,
                from,
                to,
                settings.StepDuration * 0.8f,
                PlayerBalanceRules.StepLiftBase);
        }

        private void BeginStep(
            FootSide side,
            Vector2 from,
            Vector2 to,
            float duration,
            float lift)
        {
            stepActive = true;
            stepSide = side;
            stepFrom = from;
            stepTo = to;
            stepDuration = Mathf.Max(0.05f, duration);
            stepElapsed = 0f;
            stepLift = lift;
            stepDrift = (to - from) * 0.5f / stepDuration;
            stepIsLunge = false;
            stanceWaitTimer = 0f;
            gatherTimer = 0f;
            stepsTaken++;
        }

        private void FinishStep(in PlayerBalanceSettings settings)
        {
            if (stepSide == FootSide.Left)
            {
                leftFoot = stepTo;
            }
            else
            {
                rightFoot = stepTo;
            }

            // A landed step catches the fall: the centre of pressure is
            // under the new boot at once, and the impact takes half the
            // momentum that carried him there. Without this the ankles
            // only start chasing the capture point a reaction later and
            // the COM sails on past the boot that was meant to stop it.
            // A drunk's lunge lands soft — the knee gives — and keeps
            // more of the momentum than a stagger's step.
            float retention = stepIsLunge
                ? Mathf.Lerp(
                    LandingVelocityRetention,
                    PlayerBalanceRules.LungeLandingRetentionAtMaximum,
                    settings.Intoxication)
                : LandingVelocityRetention;
            stepActive = false;
            stepDrift = Vector2.zero;
            stepIsLunge = false;
            RecentreFeet();
            cop = stepSide == FootSide.Left ? leftFoot : rightFoot;
            comVelocity *= retention;
            reactionTimer = settings.ReactionDelay;
            if (Mathf.Abs(leftFoot.x - rightFoot.x) >
                PlayerBalanceRules.GatherThreshold)
            {
                gatherTimer = PlayerBalanceRules.GatherDelay;
            }
        }

        private void RecentreFeet()
        {
            if (rootShift.sqrMagnitude <= 0f)
            {
                return;
            }

            leftFoot -= rootShift;
            rightFoot -= rootShift;
            stepFrom -= rootShift;
            stepTo -= rootShift;
            rootShift = Vector2.zero;
        }

        private PlayerBalanceOutput BuildOutput(Vector2 drift, float weave)
        {
            PlayerBalanceSettings settings = lastSettings;
            bool freeLean = phase >= BalancePhase.Toppling;
            float leanRoll = PlayerBalanceRules.LeanDegrees(
                leanVector.x,
                settings.ComHeight);
            float leanPitch = PlayerBalanceRules.LeanDegrees(
                leanVector.y,
                settings.ComHeight);
            if (!freeLean)
            {
                // With his feet still under him the lean is held to the
                // angle a topple begins at, so the hand-over to the free
                // lean of a topple is continuous.
                leanRoll = Mathf.Clamp(
                    leanRoll,
                    -settings.FallLeanDegrees,
                    settings.FallLeanDegrees);
                leanPitch = Mathf.Clamp(
                    leanPitch,
                    -settings.FallLeanDegrees,
                    settings.FallLeanDegrees);
            }

            BalanceStepCommand step = stepActive
                ? new BalanceStepCommand(
                    true,
                    stepSide,
                    stepElapsed / stepDuration,
                    stepFrom - rootShift,
                    stepTo - rootShift,
                    stepLift)
                : BalanceStepCommand.None;
            float armReaction = Mathf.Max(
                instability,
                Mathf.Clamp01(
                    flywheelVelocity.magnitude /
                    PlayerBalanceRules.FlywheelArmReferenceVelocity));
            // The pelvis rides the pendulum's arc: as the body tips, its
            // hip drops by h·(1 − cos θ). Continuous from upright, so the
            // topple's dip is the same dip a sway already shows a little
            // of.
            float crouch = settings.Intoxication * 0.03f +
                           instability * 0.04f +
                           PlayerBalanceRules.PendulumDrop(
                               leanDegrees,
                               settings.ComHeight);
            return new PlayerBalanceOutput(
                drift,
                leanRoll,
                leanPitch,
                instability,
                armReaction,
                step,
                wallSupport,
                lostBalance,
                fallDirection,
                crouch,
                weave,
                leftFoot - rootShift,
                rightFoot - rootShift,
                PlayerBalanceRules.CapturePoint(
                    com,
                    comVelocity,
                    settings.Omega),
                flywheelAngle * Mathf.Rad2Deg,
                phase,
                braceWeight,
                fallAxis,
                fallVelocity,
                fallLeanDegrees,
                fallAngularVelocity,
                supportCentre,
                cop);
        }
    }
}
