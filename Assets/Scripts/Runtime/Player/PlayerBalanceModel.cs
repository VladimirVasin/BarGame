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
            Vector2 capturePoint)
        {
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
    }

    /// <summary>
    /// The drunk hero as a linear inverted pendulum: a centre of mass a
    /// little under a metre up, a centre of pressure the ankles move late
    /// and imprecisely, a seeded disturbance that grows with the drink,
    /// and a capture point that tells the model when a step is needed,
    /// where it must land, and when nothing can save him.
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

        /// <summary>The COM lean the pelvis roll is clamped to.</summary>
        public const float MaximumLeanRollDegrees = 16f;
        public const float MaximumLeanPitchDegrees = 10f;

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

        /// <summary>Test and debug seam: the next advance latches a fall.</summary>
        public void ForceLoseBalance(float direction)
        {
            lostBalance = true;
            fallDirection = direction < 0f ? -1f : 1f;
            instability = 1f;
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
            output = PlayerBalanceOutput.Still;
        }

        public void Advance(float deltaTime, in PlayerBalanceInput input)
        {
            if (deltaTime <= 0f || float.IsNaN(deltaTime))
            {
                return;
            }

            PlayerBalanceSettings settings =
                PlayerBalanceSettings.FromIntoxication(input.Intoxication);
            lastIntoxication = settings.Intoxication;
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

            if (!input.Grounded || lostBalance)
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
                return;
            }

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
                (PlayerBalanceSettings.Gravity * settings.SlopeBias);
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
            if (stepActive)
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

                drift = stepDrift;
                rootShift += stepDrift * h;
                com -= stepDrift * h;
                if (progress >= 1f)
                {
                    FinishStep(settings);
                }
            }
            else
            {
                if (gatherTimer > 0f)
                {
                    gatherTimer -= h;
                    if (gatherTimer <= 0f)
                    {
                        PlanGatherStep(settings);
                    }
                }

                if (!stepActive &&
                    reactionTimer <= 0f &&
                    PlayerBalanceRules.NeedsStep(
                        capturePoint,
                        support,
                        settings.CaptureMargin))
                {
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

            // Lean and instability.
            float leanRaw = PlayerBalanceRules.LeanDegrees(
                com.magnitude,
                settings.ComHeight);
            float excursion = support.Excursion(capturePoint);
            instability = Mathf.Max(
                Mathf.Clamp01(
                    excursion /
                    (settings.MaximumStepReach *
                     PlayerBalanceRules.RecoverableReachFraction)),
                Mathf.Clamp01(leanRaw / settings.FallLeanDegrees));

            // Falls.
            if (input.FallAllowed)
            {
                bool blockedSide = excursion > 0f &&
                                   input.WallWithinReach &&
                                   !handHolding &&
                                   Vector2.Dot(
                                       capturePoint,
                                       -input.WallNormal) > 0f;
                blockedTimer = blockedSide ? blockedTimer + h : 0f;
                // While a step is in flight the recovery IS the step:
                // judge the capture point against the polygon the landing
                // will make, not against the lone stance foot, or every
                // step away from the stance foot reads as a lost cause the
                // moment it starts.
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
                    (leanRaw > settings.FallLeanDegrees ||
                     cannotStep ||
                     blockedTimer > PlayerBalanceRules.BlockedFallSeconds))
                {
                    lostBalance = true;
                    fallDirection = PlayerBalanceRules.FallDirection(capturePoint);
                    instability = 1f;
                }
            }
            else
            {
                BalanceSupportPolygon recoverable =
                    PlayerBalanceRules.RecoverablePolygon(support, settings);
                if (!recoverable.Contains(capturePoint))
                {
                    Vector2 pinned = recoverable.Clamp(capturePoint);
                    comVelocity = (pinned - com) * omega;
                }

                instability = Mathf.Min(instability, 0.85f);
                drift.y = 0f;
                blockedTimer = 0f;
            }

            weave = settings.HeadingWeaveDegrees *
                    Mathf.Sin(
                        elapsed * settings.HeadingWeaveFrequency * Mathf.PI * 2f +
                        swayPhases[0]);
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

            stepActive = false;
            stepDrift = Vector2.zero;
            RecentreFeet();
            // A landed step catches the fall: the centre of pressure is
            // under the new boot at once, and the impact takes half the
            // momentum that carried him there. Without this the ankles
            // only start chasing the capture point a reaction later and
            // the COM sails on past the boot that was meant to stop it.
            cop = stepSide == FootSide.Left ? leftFoot : rightFoot;
            comVelocity *= LandingVelocityRetention;
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
            PlayerBalanceSettings settings =
                PlayerBalanceSettings.FromIntoxication(lastIntoxication);
            float leanRoll = Mathf.Clamp(
                PlayerBalanceRules.LeanDegrees(com.x, PlayerBalanceSettings.DefaultComHeight),
                -MaximumLeanRollDegrees,
                MaximumLeanRollDegrees);
            float leanPitch = Mathf.Clamp(
                PlayerBalanceRules.LeanDegrees(com.y, PlayerBalanceSettings.DefaultComHeight),
                -MaximumLeanPitchDegrees,
                MaximumLeanPitchDegrees);
            BalanceStepCommand step = stepActive
                ? new BalanceStepCommand(
                    true,
                    stepSide,
                    stepElapsed / stepDuration,
                    stepFrom - rootShift,
                    stepTo - rootShift,
                    stepLift)
                : BalanceStepCommand.None;
            return new PlayerBalanceOutput(
                drift,
                leanRoll,
                leanPitch,
                instability,
                instability,
                step,
                wallSupport,
                lostBalance,
                fallDirection,
                lastIntoxication * 0.03f + instability * 0.04f,
                weave,
                leftFoot - rootShift,
                rightFoot - rootShift,
                PlayerBalanceRules.CapturePoint(
                    com,
                    comVelocity,
                    settings.Omega));
        }

        private float lastIntoxication;
    }
}
