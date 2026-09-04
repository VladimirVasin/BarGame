using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Everything the late procedural layer is asked to do this frame.
    /// Angles are degrees, lengths metres, plants <c>0..1</c>.
    /// </summary>
    internal readonly struct Player3DProceduralLayerInput
    {
        public Player3DProceduralLayerInput(
            bool enabled,
            float pelvisRollDegrees,
            float chestRollDegrees,
            float pelvisPitchDegrees,
            float leftArmOutwardDegrees,
            float leftArmForwardDegrees,
            float rightArmOutwardDegrees,
            float rightArmForwardDegrees,
            float crouchMetres,
            float plantLeft,
            float plantRight,
            float runBlend,
            bool hasRunClip,
            bool forwardGait,
            PlayerWallReachPose wallReach = default,
            float chestPitchDegrees = 0f,
            PlayerArmReachPose leftReach = default,
            PlayerArmReachPose rightReach = default,
            Vector2 leftFootOffsetLocal = default,
            Vector2 rightFootOffsetLocal = default,
            float leftFootYawDegrees = 0f,
            float rightFootYawDegrees = 0f,
            float leftFootLift = 0f,
            float rightFootLift = 0f)
        {
            Enabled = enabled;
            LeftFootOffsetLocal = leftFootOffsetLocal;
            RightFootOffsetLocal = rightFootOffsetLocal;
            LeftFootYawDegrees = leftFootYawDegrees;
            RightFootYawDegrees = rightFootYawDegrees;
            LeftFootLift = Mathf.Max(0f, leftFootLift);
            RightFootLift = Mathf.Max(0f, rightFootLift);
            PelvisRollDegrees = pelvisRollDegrees;
            ChestRollDegrees = chestRollDegrees;
            PelvisPitchDegrees = pelvisPitchDegrees;
            ChestPitchDegrees = chestPitchDegrees;
            LeftReach = leftReach;
            RightReach = rightReach;
            LeftArmOutwardDegrees = leftArmOutwardDegrees;
            LeftArmForwardDegrees = leftArmForwardDegrees;
            RightArmOutwardDegrees = rightArmOutwardDegrees;
            RightArmForwardDegrees = rightArmForwardDegrees;
            CrouchMetres = Mathf.Max(0f, crouchMetres);
            PlantLeft = Mathf.Clamp01(plantLeft);
            PlantRight = Mathf.Clamp01(plantRight);
            RunBlend = Mathf.Clamp01(runBlend);
            HasRunClip = hasRunClip;
            ForwardGait = forwardGait;
            WallReach = wallReach;
        }

        /// <summary>A hand reaching for a wall, if any.</summary>
        public PlayerWallReachPose WallReach { get; }

        /// <summary>
        /// Each hand reaching for something else — the ground he is
        /// falling toward, a knee — if it is. Where a wall and one of
        /// these want the same arm, the heavier reach wins.
        /// </summary>
        public PlayerArmReachPose LeftReach { get; }
        public PlayerArmReachPose RightReach { get; }

        public static Player3DProceduralLayerInput Disabled => default;

        public bool Enabled { get; }
        public float PelvisRollDegrees { get; }
        public float ChestRollDegrees { get; }

        /// <summary>Forward pitch of the pelvis, degrees.</summary>
        public float PelvisPitchDegrees { get; }

        /// <summary>Forward pitch of the chest on top of the pelvis, degrees.</summary>
        public float ChestPitchDegrees { get; }

        /// <summary>
        /// Each arm's swing in the ACTOR's frame, degrees: out to the
        /// side (abduction, positive = away from the body's midline) and
        /// raised to the front. Both are zero for a hanging arm.
        /// </summary>
        public float LeftArmOutwardDegrees { get; }
        public float LeftArmForwardDegrees { get; }
        public float RightArmOutwardDegrees { get; }
        public float RightArmForwardDegrees { get; }
        public float CrouchMetres { get; }
        public float PlantLeft { get; }
        public float PlantRight { get; }
        public float RunBlend { get; }
        public bool HasRunClip { get; }
        public bool ForwardGait { get; }

        /// <summary>
        /// The drunk walk's disorder of each boot, applied only in a
        /// forward gait to a boot no step or lock owns: where it lands
        /// relative to the clip (hero frame, x right, y forward), how far
        /// its toes turn out (degrees about up), and how much higher it
        /// swings.
        /// </summary>
        public Vector2 LeftFootOffsetLocal { get; }
        public Vector2 RightFootOffsetLocal { get; }
        public float LeftFootYawDegrees { get; }
        public float RightFootYawDegrees { get; }
        public float LeftFootLift { get; }
        public float RightFootLift { get; }

        /// <summary>Whether the drunk walk asks anything of this boot.</summary>
        public bool HasGaitDisorder(int legIndex)
        {
            return legIndex == 0
                ? LeftFootOffsetLocal.sqrMagnitude > 0.00000001f ||
                  LeftFootLift > 0.00001f ||
                  Mathf.Abs(LeftFootYawDegrees) > 0.001f
                : RightFootOffsetLocal.sqrMagnitude > 0.00000001f ||
                  RightFootLift > 0.00001f ||
                  Mathf.Abs(RightFootYawDegrees) > 0.001f;
        }
    }

    /// <summary>
    /// What the late layer is asked to do on top of the authored Rise
    /// clip: the hands to the floor (or one to the knee), the lead boot
    /// stepping forward, a dip and a wobble of the pelvis, the head
    /// lifting, and — as he stands — the ordinary leg solve fading in.
    /// </summary>
    internal readonly struct Player3DRiseLayerInput
    {
        public Player3DRiseLayerInput(
            bool enabled,
            PlayerArmReachPose leftHand,
            PlayerArmReachPose rightHand,
            bool stepActive,
            FootSide stepSide,
            Vector3 stepWorldPosition,
            float stepLift,
            float stepWeight,
            float pelvisOffsetMetres,
            float pelvisRollDegrees,
            float pelvisPitchDegrees,
            float headLiftDegrees,
            float legsWeight,
            bool leftKneeActive = false,
            Vector3 leftKneeWorldPosition = default,
            float leftKneeWeight = 0f,
            bool rightKneeActive = false,
            Vector3 rightKneeWorldPosition = default,
            float rightKneeWeight = 0f)
        {
            Enabled = enabled;
            LeftHand = leftHand;
            RightHand = rightHand;
            LeftKneeActive = leftKneeActive;
            LeftKneeWorldPosition = leftKneeWorldPosition;
            LeftKneeWeight = Mathf.Clamp01(leftKneeWeight);
            RightKneeActive = rightKneeActive;
            RightKneeWorldPosition = rightKneeWorldPosition;
            RightKneeWeight = Mathf.Clamp01(rightKneeWeight);
            StepActive = stepActive;
            StepSide = stepSide;
            StepWorldPosition = stepWorldPosition;
            StepLift = Mathf.Max(0f, stepLift);
            StepWeight = Mathf.Clamp01(stepWeight);
            PelvisOffsetMetres = pelvisOffsetMetres;
            PelvisRollDegrees = pelvisRollDegrees;
            PelvisPitchDegrees = pelvisPitchDegrees;
            HeadLiftDegrees = headLiftDegrees;
            LegsWeight = Mathf.Clamp01(legsWeight);
        }

        public static Player3DRiseLayerInput Disabled => default;

        public bool Enabled { get; }
        public PlayerArmReachPose LeftHand { get; }
        public PlayerArmReachPose RightHand { get; }
        public bool StepActive { get; }
        public FootSide StepSide { get; }

        /// <summary>Where the stepping sole goes, world space, on the ground.</summary>
        public Vector3 StepWorldPosition { get; }
        public float StepLift { get; }
        public float StepWeight { get; }
        public float PelvisOffsetMetres { get; }
        public float PelvisRollDegrees { get; }
        public float PelvisPitchDegrees { get; }
        public float HeadLiftDegrees { get; }
        public float LegsWeight { get; }

        /// <summary>
        /// A crawl's knees: each thigh is aimed so the knee goes to its
        /// world point (on the floor, or arcing to its next spot), and
        /// the shin is laid flat behind it.
        /// </summary>
        public bool LeftKneeActive { get; }
        public Vector3 LeftKneeWorldPosition { get; }
        public float LeftKneeWeight { get; }
        public bool RightKneeActive { get; }
        public Vector3 RightKneeWorldPosition { get; }
        public float RightKneeWeight { get; }
    }

    /// <summary>
    /// The single late writer of the hero's bones after the clip.
    ///
    /// Every frame it puts back exactly what it wrote last time, captures
    /// the clip pose the graph just produced, and then, in this order:
    /// leans the pelvis, chest and arms (the status pose), probes the
    /// ground under each boot, lowers or lifts the pelvis so the lower
    /// boot can reach its surface, and solves each leg so its sole lands
    /// where the probe said the ground is — a tread, a kerb, a ramp, or
    /// the actor's own ground plane when nothing collidable is there.
    ///
    /// It replaces the presentation's symmetric knee bend and its shared
    /// "lowest sole to actor ground" pin: with both feet held by IK, a
    /// lowered pelvis bends the knees anatomically, and asymmetrically
    /// when the feet stand on different heights.
    /// </summary>
    internal sealed class Player3DProceduralLocomotionLayer : IDisposable
    {
        /// <summary>How long the legs take to fade in after a clip ends.</summary>
        public const float BlendInSeconds = 0.2f;

        /// <summary>The knee hint sits this far ahead of the shin joint.</summary>
        public const float KneeHintForward = 0.45f;
        public const float KneeHintUp = 0.05f;

        /// <summary>
        /// How far behind the elbow its bend hint sits, along the upper
        /// arm's calibrated back. Relative to the elbow, not the shoulder:
        /// an elbow already roughly back keeps its azimuth, one that is
        /// clearly forward is swung across.
        /// </summary>
        public const float ElbowHintBackMetres = 0.35f;

        /// <summary>The elbow's back is mostly behind and a little below the hanging arm.</summary>
        public const float ElbowBackShare = 0.85f;

        /// <summary>A ramp tilts the sole at most this much.</summary>
        public const float MaximumSoleTiltDegrees = 18f;

        /// <summary>
        /// The head lift of the rise, shared down the neck. In-game check:
        /// a positive local-X turn on the imported neck and head bones
        /// pitches the face UP (the attention head's finding), so the lift
        /// is the positive turn.
        /// </summary>
        public const float HeadLiftSign = 1f;
        public const float HeadLiftNeckShare = 0.4f;
        public const float HeadLiftHeadShare = 0.6f;

        private static readonly Player3DProceduralLayerInput StandingRiseInput =
            new Player3DProceduralLayerInput(
                true,
                0f,
                0f,
                0f,
                0f,
                0f,
                0f,
                0f,
                0f,
                1f,
                1f,
                0f,
                false,
                false);

        private readonly Leg[] legs = { new Leg(), new Leg() };
        private readonly Arm[] arms = { new Arm(), new Arm() };
        private readonly BoneLocalPose[] basePoses = new BoneLocalPose[16];
        private readonly FootGroundSample[] samples =
            { FootGroundSample.None, FootGroundSample.None };
        private readonly float[] plants = { 1f, 1f };

        private Player3DAssetRegistry registry;
        private Transform actorRoot;
        private Transform pelvis;
        private Transform chest;
        private Transform leftUpperArm;
        private Transform rightUpperArm;
        private Transform neck;
        private Transform head;
        private Player3DFootGroundProbe probe;
        private bool baseCaptured;
        private float ikBlend;
        private float groundedFootHeightOffset;
        private bool groundedOffsetCaptured;
        private float soleClearance;
        private bool soleClearanceCaptured;
        private float lastPelvisDrop;
        private float smoothedPlaneDelta;
        private bool hasSmoothedPlaneDelta;

        /// <summary>
        /// Bound to an actor root; the registry is optional (pedestrians
        /// bind bones found by name).
        /// </summary>
        public bool IsBound => actorRoot != null;
        public float IkBlend => ikBlend;
        public float LastPelvisDrop => lastPelvisDrop;

        /// <summary>
        /// The neutral lowest sole above the actor root — the legacy
        /// grounding contract, still the target when no surface is found.
        /// </summary>
        public bool HasGroundedFootHeightOffset => groundedOffsetCaptured;

        public float GroundedFootHeightOffset => groundedFootHeightOffset;

        /// <summary>
        /// How far the neutral sole floats above a probed floor (the
        /// controller's skin width in practice), or zero until a floor has
        /// been seen.
        /// </summary>
        public float SoleClearance => soleClearance;

        public bool HasSoleClearance => soleClearanceCaptured;

        public FootGroundSample GetSample(FootSide side)
        {
            return samples[(int)side];
        }

        public float GetPlant(FootSide side)
        {
            return plants[(int)side];
        }

        public bool IsFootLocked(FootSide side)
        {
            return legs[(int)side].Locked;
        }

        /// <summary>
        /// Where the layer last left this ankle after its solve — the
        /// position a lock must start from so a boot that was already
        /// solved onto its ground does not jump to the raw clip pose.
        /// </summary>
        public bool TryGetLastAnklePosition(FootSide side, out Vector3 position)
        {
            Leg leg = legs[(int)side];
            position = leg.LastAnklePosition;
            return leg.HasLastAnklePosition;
        }

        public void Bind(
            Player3DAssetRegistry assetRegistry,
            Transform actorFacingTransform,
            Transform pelvisBone,
            Transform chestBone,
            Transform leftUpperArmBone,
            Transform rightUpperArmBone,
            Transform leftThigh,
            Transform leftShin,
            Transform leftFoot,
            Transform rightThigh,
            Transform rightShin,
            Transform rightFoot,
            Player3DFootGroundProbe groundProbe)
        {
            Restore();
            probe?.Dispose();
            registry = assetRegistry;
            actorRoot = actorFacingTransform;
            pelvis = pelvisBone;
            chest = chestBone;
            leftUpperArm = leftUpperArmBone;
            rightUpperArm = rightUpperArmBone;
            legs[0].Bind(leftThigh, leftShin, leftFoot);
            legs[1].Bind(rightThigh, rightShin, rightFoot);
            probe = groundProbe;
            ikBlend = 0f;
            groundedOffsetCaptured = false;
            soleClearanceCaptured = false;
            hasSmoothedPlaneDelta = false;
            lastPelvisDrop = 0f;
            samples[0] = FootGroundSample.None;
            samples[1] = FootGroundSample.None;
        }

        /// <summary>
        /// Measures the neutral rig: leg lengths, each boot's forward and
        /// sole-up in its own bone space, the lowest sole above the actor
        /// root, and — when a floor is under the boots — how far that sole
        /// floats above it. Call with the clip evaluated at its neutral
        /// pose and no procedural writes applied.
        /// </summary>
        public void Calibrate()
        {
            if (!IsBound)
            {
                return;
            }

            Vector3 actorForward = PlanarForward();
            for (int index = 0; index < legs.Length; index++)
            {
                legs[index].Calibrate(actorForward);
                // A rebind or a teleport leaves a smoothed target that
                // belongs to another room; the next frame probes afresh.
                legs[index].HasSmoothedTarget = false;
            }

            hasSmoothedPlaneDelta = false;

            Vector3 actorRight = Vector3.Cross(Vector3.up, actorForward);
            arms[0].Calibrate(actorRight, actorForward);
            arms[1].Calibrate(-actorRight, actorForward);

            groundedOffsetCaptured = false;
            soleClearanceCaptured = false;
            if (!TryGetLowestSole(out float lowestSole))
            {
                return;
            }

            groundedFootHeightOffset = lowestSole - actorRoot.position.y;
            groundedOffsetCaptured = true;
            CaptureSoleClearance(lowestSole);
        }

        /// <summary>
        /// Puts every bone the layer wrote back to the clip pose. Safe to
        /// call when nothing was written.
        /// </summary>
        public void Restore()
        {
            if (!baseCaptured)
            {
                return;
            }

            for (int index = 0; index < basePoses.Length; index++)
            {
                basePoses[index].Restore();
            }

            baseCaptured = false;
        }

        /// <summary>Forgets the blend-in so the legs fade back in gently.</summary>
        public void ResetBlend()
        {
            ikBlend = 0f;
        }

        /// <summary>
        /// Keeps what the layer wrote: the next <see cref="Restore"/> puts
        /// nothing back. For the moment the ragdoll takes the bones as
        /// they are — the brace pose, not the clip under it.
        /// </summary>
        public void ForgetBase()
        {
            baseCaptured = false;
        }

        /// <summary>
        /// Holds a boot's sole at a world position while it is planted
        /// (a balance step's stance foot). Released automatically when the
        /// target leaves the leg's reach.
        /// </summary>
        public void LockFoot(FootSide side, Vector3 worldSolePosition)
        {
            legs[(int)side].Lock(worldSolePosition);
        }

        public void ReleaseFoot(FootSide side)
        {
            legs[(int)side].Release();
        }

        /// <summary>
        /// A recovery step in flight: this boot's ankle goes to the world
        /// XZ given, lifted by <paramref name="liftMetres"/> above where the
        /// ground would put it, at full solve weight whatever the clip's
        /// plant says — the balance model owns this foot until the step
        /// lands.
        /// </summary>
        public void SetStepTarget(
            FootSide side,
            Vector3 worldPosition,
            float liftMetres)
        {
            stepActive = true;
            stepSide = side;
            stepWorldPosition = worldPosition;
            stepLift = Mathf.Max(0f, liftMetres);
        }

        public void ClearStepTarget()
        {
            stepActive = false;
            stepLift = 0f;
        }

        public bool HasStepTarget => stepActive;

        private bool stepActive;
        private FootSide stepSide;
        private Vector3 stepWorldPosition;
        private float stepLift;

        /// <summary>
        /// The arm chains a wall hand can reach with. Optional: a rig
        /// without registered forearms simply never reaches.
        /// </summary>
        public void BindArms(
            Transform leftForearm,
            Transform leftHand,
            Transform rightForearm,
            Transform rightHand)
        {
            arms[0].Bind(leftUpperArm, leftForearm, leftHand);
            arms[1].Bind(rightUpperArm, rightForearm, rightHand);
        }

        /// <summary>The neck and head the rise lifts. Optional.</summary>
        public void BindHead(Transform neckBone, Transform headBone)
        {
            neck = neckBone;
            head = headBone;
        }

        /// <summary>
        /// The reaching hands: the wall hand, and each hand's own target
        /// (the ground in a topple). Where two reaches want one arm the
        /// heavier one is solved. Runs after the legs so the shoulder it
        /// reaches from is where the lean put it.
        /// </summary>
        private void ApplyArmReaches(in Player3DProceduralLayerInput input)
        {
            PlayerArmReachPose wall = input.WallReach.ToArmReach();
            PlayerArmReachPose left = input.LeftReach;
            PlayerArmReachPose right = input.RightReach;
            if (wall.Active)
            {
                if (wall.RightHand)
                {
                    right = Heavier(wall, right);
                }
                else
                {
                    left = Heavier(wall, left);
                }
            }

            ApplyArmReach(left, false);
            ApplyArmReach(right, true);
        }

        private static PlayerArmReachPose Heavier(
            in PlayerArmReachPose first,
            in PlayerArmReachPose second)
        {
            if (!second.Active)
            {
                return first;
            }

            if (!first.Active)
            {
                return second;
            }

            return second.Weight > first.Weight ? second : first;
        }

        /// <summary>
        /// One hand to its target: the palm goes to the point at the
        /// pose's weight, elbow hinted below and behind the shoulder by
        /// the pose's own amounts, palm turned to the surface.
        /// </summary>
        private void ApplyArmReach(in PlayerArmReachPose reach, bool rightArm)
        {
            if (!reach.Active || reach.Weight <= 0.0001f)
            {
                return;
            }

            Arm arm = arms[rightArm ? 1 : 0];
            if (!arm.IsComplete)
            {
                return;
            }

            Vector3 shoulder = arm.Upper.position;
            Vector3 target = PlayerWallContactRules.ClampToReach(
                shoulder,
                reach.WorldPosition,
                arm.Length);
            Vector3 normal = reach.WorldNormal.sqrMagnitude > 0.0001f
                ? reach.WorldNormal.normalized
                : Vector3.forward;
            Vector3 palmNow = arm.PalmDirection();
            Quaternion handRotation = arm.Hand.rotation;
            if (palmNow.sqrMagnitude > 0.0001f)
            {
                handRotation = Quaternion.FromToRotation(palmNow, -normal) *
                               handRotation;
            }

            // The elbow points the way the upper arm's back will face
            // once the arm is swung onto its target by the least
            // rotation — the same rule as the knee: no twist asked of
            // the shoulder, and the actor's planar back (meaningless to
            // an arm on a lying body) never consulted.
            Vector3 elbowBack = Quaternion.FromToRotation(
                                    arm.Hand.position - shoulder,
                                    target - shoulder) *
                                arm.ElbowBack();
            Vector3 hint = arm.Forearm.position +
                           elbowBack * ElbowHintBackMetres;
            LimbTwoBoneIk.Solve(
                arm.Upper,
                arm.Forearm,
                arm.Hand,
                target,
                handRotation,
                hint,
                reach.Weight * ikBlend,
                float.PositiveInfinity,
                true);
            AlignHingeRoll(arm.Upper, arm.Forearm, arm.Hand, arm.ElbowBack(), reach.Weight * ikBlend);
        }

        /// <summary>The calibrated knee-forward direction of one leg, for probes.</summary>
        internal Vector3 DebugKneeForward(FootSide side)
        {
            return legs[(int)side].KneeForward();
        }

        /// <summary>The calibrated elbow-back direction of one arm, for probes.</summary>
        internal Vector3 DebugElbowBack(bool rightArm)
        {
            return arms[rightArm ? 1 : 0].ElbowBack();
        }

        private sealed class Arm
        {
            private Vector3 palmLocal = Vector3.forward;
            private Vector3 elbowBackLocal = Vector3.back;

            public Transform Upper { get; private set; }
            public Transform Forearm { get; private set; }
            public Transform Hand { get; private set; }
            public float Length { get; private set; }
            public bool IsComplete =>
                Upper != null && Forearm != null && Hand != null;

            public void Bind(Transform upper, Transform forearm, Transform hand)
            {
                Upper = upper;
                Forearm = forearm;
                Hand = hand;
                Length = 0f;
            }

            /// <summary>
            /// In the neutral pose the arms hang with the palms toward the
            /// body, so the palm direction is captured as the actor's
            /// inward side in the hand's own space; and the elbow's
            /// anatomical back — mostly behind, a little below — in the
            /// upper arm's, so it turns with the arm wherever it points.
            /// </summary>
            public void Calibrate(Vector3 inwardWorld, Vector3 actorForward)
            {
                if (!IsComplete)
                {
                    return;
                }

                Length = LimbTwoBoneIk.ChainLength(Upper, Forearm, Hand);
                palmLocal = Quaternion.Inverse(Hand.rotation) * inwardWorld;
                Vector3 back = -actorForward * ElbowBackShare +
                               Vector3.down * (1f - ElbowBackShare);
                elbowBackLocal = Quaternion.Inverse(Upper.rotation) *
                                 back.normalized;
            }

            public Vector3 PalmDirection()
            {
                return Hand != null
                    ? Hand.rotation * palmLocal
                    : Vector3.forward;
            }

            /// <summary>Where the elbow should point, in the upper arm's current frame.</summary>
            public Vector3 ElbowBack()
            {
                return Upper != null
                    ? Upper.rotation * elbowBackLocal
                    : Vector3.back;
            }
        }

        public void Apply(
            in Player3DProceduralLayerInput input,
            float deltaTime)
        {
            Restore();
            if (!input.Enabled || !IsBound)
            {
                ikBlend = 0f;
                lastPelvisDrop = 0f;
                return;
            }

            float clampedDelta = Mathf.Max(0f, deltaTime);
            CaptureBase();
            ikBlend = Mathf.MoveTowards(
                ikBlend,
                1f,
                clampedDelta / BlendInSeconds);
            plants[0] = input.PlantLeft;
            plants[1] = input.PlantRight;

            ApplyBodyPose(input);
            ApplyLegs(input, clampedDelta);
            ApplyArmReaches(input);
        }

        /// <summary>
        /// The rise's late pass, on top of the authored Rise clip: the
        /// pelvis dips (a slump) and wobbles (the top), the head lifts,
        /// the lead boot is solved to its step target while he kneels,
        /// the ordinary leg solve fades in as he stands, and the hands go
        /// to the floor or the knee. No blend-in of its own — the clip
        /// owns the base and the weights come from the rise model.
        /// </summary>
        public void ApplyRise(in Player3DRiseLayerInput input, float deltaTime)
        {
            Restore();
            if (!input.Enabled || !IsBound)
            {
                ikBlend = 0f;
                lastPelvisDrop = 0f;
                return;
            }

            CaptureBase();
            lastPelvisDrop = 0f;
            if (pelvis != null)
            {
                if (Mathf.Abs(input.PelvisOffsetMetres) > 0.00001f)
                {
                    pelvis.position += Vector3.up * input.PelvisOffsetMetres;
                    lastPelvisDrop = input.PelvisOffsetMetres;
                }

                RotateBone(pelvis, Vector3.forward, input.PelvisRollDegrees);
                RotateBone(pelvis, Vector3.right, -input.PelvisPitchDegrees);
            }

            if (Mathf.Abs(input.HeadLiftDegrees) > 0.00001f)
            {
                RotateBone(
                    neck,
                    Vector3.right,
                    HeadLiftSign * HeadLiftNeckShare * input.HeadLiftDegrees);
                RotateBone(
                    head,
                    Vector3.right,
                    HeadLiftSign * HeadLiftHeadShare * input.HeadLiftDegrees);
            }

            if (input.LegsWeight > 0.0001f)
            {
                // Standing: the boots find the floor under them as the
                // clip brings him up, at the model's weight.
                ikBlend = input.LegsWeight;
                plants[0] = 1f;
                plants[1] = 1f;
                ApplyLegs(
                    StandingRiseInput,
                    Mathf.Max(0f, deltaTime),
                    allowReachDip: false);
            }
            else if (input.StepActive && input.StepWeight > 0.0001f)
            {
                SolveRiseStep(input);
            }

            if (input.LeftKneeActive)
            {
                PlaceKnee(legs[0], input.LeftKneeWorldPosition, input.LeftKneeWeight);
            }

            if (input.RightKneeActive)
            {
                PlaceKnee(legs[1], input.RightKneeWorldPosition, input.RightKneeWeight);
            }

            ikBlend = 1f;
            ApplyArmReach(input.LeftHand, false);
            ApplyArmReach(input.RightHand, true);
        }

        /// <summary>
        /// A crawling knee: the thigh is turned from the hip so the knee
        /// points at its spot (the thigh's length decides where it lands;
        /// the presentation brings the hips down so that is on the
        /// floor), and the shin is laid flat behind it along the floor,
        /// trailing.
        /// </summary>
        private void PlaceKnee(Leg leg, Vector3 kneeWorld, float weight)
        {
            if (!leg.IsComplete || weight <= 0.0001f)
            {
                return;
            }

            // A thigh has one length: aimed at a spot nearer the hip than
            // that, the knee would overshoot it — into the floor. The
            // spot's HEIGHT is what matters (the floor, or the arc over
            // it), so the spot is pushed out along its own planar
            // direction until the thigh reaches it at that height; a
            // spot the hips are too high for is taken straight below.
            Vector3 hip = leg.Thigh.position;
            float thighLength = Vector3.Distance(hip, leg.Shin.position);
            Vector3 planar = kneeWorld - hip;
            planar.y = 0f;
            float vertical = hip.y - kneeWorld.y;
            if (vertical >= thighLength * 0.98f)
            {
                kneeWorld = new Vector3(hip.x, hip.y - thighLength, hip.z);
            }
            else
            {
                float needed = Mathf.Sqrt(Mathf.Max(0f, thighLength * thighLength - vertical * vertical));
                Vector3 direction = planar.sqrMagnitude > 0.000001f
                    ? planar.normalized
                    : PlanarForwardStatic(leg);
                kneeWorld = new Vector3(
                    hip.x + direction.x * needed,
                    kneeWorld.y,
                    hip.z + direction.z * needed);
            }

            AimBone(leg.Thigh, leg.Shin.position, kneeWorld, weight);
            float shinLength = Vector3.Distance(leg.Shin.position, leg.Foot.position);
            Vector3 back = -PlanarForward();
            Vector3 footTarget = leg.Shin.position + back * shinLength;
            AimBone(leg.Shin, leg.Foot.position, footTarget, weight);
        }

        /// <summary>The way the leg's kneecap points, flattened: where a knee with no planar spot goes.</summary>
        private static Vector3 PlanarForwardStatic(Leg leg)
        {
            Vector3 forward = leg.KneeForward();
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }

        /// <summary>Turns a bone from its pivot by the least rotation that carries <paramref name="endNow"/> onto the ray to <paramref name="target"/>, blended.</summary>
        private static void AimBone(Transform joint, Vector3 endNow, Vector3 target, float weight)
        {
            Vector3 from = endNow - joint.position;
            Vector3 to = target - joint.position;
            if (from.sqrMagnitude < 0.000001f || to.sqrMagnitude < 0.000001f)
            {
                return;
            }

            Quaternion delta = Quaternion.FromToRotation(from, to);
            joint.rotation = Quaternion.Slerp(Quaternion.identity, delta, Mathf.Clamp01(weight)) *
                             joint.rotation;
        }

        /// <summary>The lead boot to its step target, arcing by the lift.</summary>
        private void SolveRiseStep(in Player3DRiseLayerInput input)
        {
            Leg leg = legs[(int)input.StepSide];
            if (!leg.IsComplete)
            {
                return;
            }

            Vector3 target = input.StepWorldPosition + Vector3.up * input.StepLift;
            Vector3 hip = leg.Thigh.position;
            float reach = Mathf.Max(
                leg.Length * PlayerFootPlacementRules.DefaultReachFraction,
                Vector3.Distance(hip, leg.Foot.position));
            Vector3 clamped = ClampToReach(hip, target, reach);
            Vector3 hint = KneeHint(leg, hip, clamped);
            LimbTwoBoneIk.Solve(
                leg.Thigh,
                leg.Shin,
                leg.Foot,
                clamped,
                leg.Foot.rotation,
                hint,
                input.StepWeight,
                float.PositiveInfinity,
                false);
            AlignHingeRoll(leg.Thigh, leg.Shin, leg.Foot, leg.KneeForward(), input.StepWeight);
        }

        /// <summary>
        /// A knee is a hinge: the shin folds in the thigh's own sagittal
        /// plane, the kneecap facing the way the shin bends away from
        /// straight. The two-bone solve aims the thigh and the shin
        /// independently, so a foot pulled off to the side can leave the
        /// shin swung ROUND the thigh — a knee turned ninety degrees,
        /// which the ragdoll's hinge then snaps back. This rolls the
        /// upper bone about its own length until its bend reference
        /// (the kneecap, the elbow's back) faces the lower bone's fold,
        /// holding the lower bone's world rotation so the tip stays put:
        /// the twist becomes rotation in the hip or the shoulder, where
        /// a body has it. Nothing to do while the joint is nearly
        /// straight.
        /// </summary>
        private static void AlignHingeRoll(
            Transform upper,
            Transform lower,
            Transform tip,
            Vector3 bendReference,
            float weight)
        {
            if (upper == null || lower == null || tip == null || weight <= 0.0001f)
            {
                return;
            }

            Vector3 axis = lower.position - upper.position;
            if (axis.sqrMagnitude < 0.000001f)
            {
                return;
            }

            axis.Normalize();
            // The lower bone folds AWAY from the reference: the shin goes
            // back behind the kneecap, the forearm forward of the elbow.
            Vector3 fold = -Vector3.ProjectOnPlane(tip.position - lower.position, axis);
            Vector3 facing = Vector3.ProjectOnPlane(bendReference, axis);
            if (fold.magnitude < HingeAlignMinimumFoldMetres ||
                facing.sqrMagnitude < 0.000001f)
            {
                return;
            }

            float roll = Vector3.SignedAngle(facing, fold, axis) * Mathf.Clamp01(weight);
            if (Mathf.Abs(roll) < 0.01f)
            {
                return;
            }

            Quaternion lowerWorld = lower.rotation;
            upper.rotation = Quaternion.AngleAxis(roll, axis) * upper.rotation;
            lower.rotation = lowerWorld;
        }

        /// <summary>Below this fold off straight a hinge has no plane to align to.</summary>
        public const float HingeAlignMinimumFoldMetres = 0.02f;

        /// <summary>
        /// Where the knee should point: the way the kneecap will face
        /// once the thigh has been swung to aim the leg at its target by
        /// the least rotation — not the way it faces now, and not the
        /// actor's forward. A hint fixed in the world makes the solver
        /// TWIST the femur to reach it; a leg swung far to the side was
        /// being screwed half a turn in the hip, the mesh with it, and
        /// read as a backward knee. Aiming the hint along the kneecap
        /// asks for no twist at all, and a knee authored backward is
        /// still swung across by the solver's side guard.
        /// </summary>
        private static Vector3 KneeHint(Leg leg, Vector3 hip, Vector3 target)
        {
            Vector3 kneecap = Quaternion.FromToRotation(
                                  leg.Foot.position - hip,
                                  target - hip) *
                              leg.KneeForward();
            return leg.Shin.position +
                   kneecap * KneeHintForward +
                   Vector3.up * KneeHintUp;
        }

        public void Dispose()
        {
            Restore();
            probe?.Dispose();
            probe = null;
            registry = null;
            actorRoot = null;
        }

        private void ApplyBodyPose(in Player3DProceduralLayerInput input)
        {
            // Keep the authored horizontal pelvis anchor: rotations only.
            // Procedural translation here would move the whole scale-100
            // imported rig by a hundred times the intended metres.
            // Roll about the spine chain's local forward is a lean to the
            // RIGHT for a positive angle, as the contract says. Its local
            // right, though, points to the hero's LEFT on the imported
            // rig (measured: +10° about it moved the head 11 cm BACK), so
            // the forward pitch is the negative turn.
            RotateBone(pelvis, Vector3.forward, input.PelvisRollDegrees);
            RotateBone(pelvis, Vector3.right, -input.PelvisPitchDegrees);
            RotateBone(chest, Vector3.forward, input.ChestRollDegrees);
            // The chest shares the spine chain's axes, so its forward
            // pitch is the same negative turn about local right as the
            // pelvis's (pinned by a probe in PlayerBalancePlayModeTests).
            RotateBone(chest, Vector3.right, -input.ChestPitchDegrees);

            // The arms swing in the actor's frame, never the bone's. An
            // imported upper-arm bone's local axes sit at whatever roll
            // Blender gave them — measured on the V2 rig, no local axis
            // is the abduction axis, and a turn about local forward sent
            // both hands backward and into the ribs. Abduction is a turn
            // about the actor's planar forward through the shoulder; a
            // raise to the front is a turn about the actor's right. Both
            // fade in with the layer's own blend so the arms do not snap
            // out on the first frame after a clip hands the body back.
            Vector3 forward = PlanarForward();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            SwingArm(
                leftUpperArm,
                forward,
                right,
                -1f,
                input.LeftArmOutwardDegrees * ikBlend,
                input.LeftArmForwardDegrees * ikBlend);
            SwingArm(
                rightUpperArm,
                forward,
                right,
                1f,
                input.RightArmOutwardDegrees * ikBlend,
                input.RightArmForwardDegrees * ikBlend);
        }

        /// <summary>
        /// Turns an upper arm about world axes through its own pivot.
        /// A hanging arm turned by a positive angle about the actor's
        /// forward moves toward the actor's right (left-handed
        /// <see cref="Quaternion.AngleAxis"/>), so the left arm's outward
        /// is the negative turn; a positive turn about the actor's right
        /// swings a hanging arm backward, so the raise is the negative
        /// turn for both. The raise goes on FIRST, while the arm still
        /// hangs, and the abduction turns the raised arm about the very
        /// axis the raise moved it along — so the hands keep their
        /// forward reach whatever the spread, instead of the raise
        /// degenerating into a roll of the arm once it is out wide.
        /// </summary>
        private static void SwingArm(
            Transform upper,
            Vector3 actorForward,
            Vector3 actorRight,
            float outwardSign,
            float outwardDegrees,
            float forwardDegrees)
        {
            if (upper == null)
            {
                return;
            }

            if (Mathf.Abs(forwardDegrees) > 0.00001f)
            {
                upper.rotation =
                    Quaternion.AngleAxis(-forwardDegrees, actorRight) *
                    upper.rotation;
            }

            if (Mathf.Abs(outwardDegrees) > 0.00001f)
            {
                upper.rotation =
                    Quaternion.AngleAxis(
                        outwardDegrees * outwardSign,
                        actorForward) *
                    upper.rotation;
            }
        }

        /// <param name="allowReachDip">
        /// Whether a boot that cannot reach its target may bring the hips
        /// down to it. False through a rise: the Rise clip and the rise
        /// model own the pelvis there, and a half-kneeling boot is out of
        /// reach by design.
        /// </param>
        private void ApplyLegs(
            in Player3DProceduralLayerInput input,
            float deltaTime,
            bool allowReachDip = true)
        {
            lastPelvisDrop = 0f;
            if (pelvis == null)
            {
                return;
            }

            if (!groundedOffsetCaptured)
            {
                Calibrate();
                if (!groundedOffsetCaptured)
                {
                    return;
                }
            }

            float rootY = actorRoot.position.y;
            float fallbackSole = rootY + groundedFootHeightOffset;
            Vector3 actorForward = PlanarForward();

            // The clip's own lowest sole is the reference every lift is
            // measured from: the planted boot has none, the swinging boot
            // keeps however much the animator gave it.
            bool anySole = false;
            float referenceSole = float.PositiveInfinity;
            for (int index = 0; index < legs.Length; index++)
            {
                Leg leg = legs[index];
                leg.Prepared = false;
                if (!leg.IsComplete)
                {
                    continue;
                }

                leg.SoleY = TryGetSole((FootSide)index, out float soleY)
                    ? soleY
                    : leg.Foot.position.y;
                referenceSole = Mathf.Min(referenceSole, leg.SoleY);
                anySole = true;
            }

            if (!anySole)
            {
                return;
            }

            if (!soleClearanceCaptured)
            {
                CaptureSoleClearance(referenceSole);
            }

            for (int index = 0; index < legs.Length; index++)
            {
                Leg leg = legs[index];
                if (!leg.IsComplete)
                {
                    samples[index] = FootGroundSample.None;
                    continue;
                }

                float plant = plants[index];
                Vector3 anklePosition = leg.Foot.position;
                FootGroundSample sample = probe != null
                    ? probe.Probe(
                        anklePosition,
                        leg.FootForward(),
                        rootY)
                    : FootGroundSample.None;
                samples[index] = sample;

                float lift = PlayerFootPlacementRules.ClipLift(
                    leg.SoleY,
                    referenceSole);
                float baseSole = sample.HasSurface
                    ? PlayerFootPlacementRules.TargetSoleHeight(
                        PlayerFootPlacementRules.SupportHeight(
                            sample.Kind,
                            sample.HeelY,
                            sample.ToeY,
                            plant),
                        soleClearance,
                        0f)
                    : fallbackSole;
                if (sample.HasSurface && leg.HasSmoothedTarget)
                {
                    // Probe flicker at a tread nosing must not kick a
                    // planted boot; a swinging boot follows freely. Only
                    // the SURFACE is smoothed — the clip's own lift rides
                    // on top unfiltered, or the swing would lag the clip.
                    //
                    // And it is smoothed relative to the ACTOR ROOT, never
                    // in the world. The capsule carries the body down a
                    // stair flight at more than a metre a second; a world
                    // height chasing at the planted rate reads that descent
                    // as ground sinking under a planted boot and falls half
                    // a metre behind in one flight. Both deltas then went
                    // positive, the pelvis pinned at its lift cap, and the
                    // hero came down the stairwell with his boots in the
                    // air and his knees folded double. Held above the root,
                    // his own descent passes through untouched and only a
                    // real change under the foot — a nosing, a kerb — is
                    // rate-limited.
                    baseSole = Mathf.MoveTowards(
                        rootY + leg.SmoothedSoleAboveRoot,
                        baseSole,
                        PlayerFootPlacementRules.MaximumTargetStep(
                            plant,
                            deltaTime));
                }

                float targetSole = baseSole + lift;
                leg.SmoothedSoleAboveRoot = baseSole - rootY;
                leg.HasSmoothedTarget = sample.HasSurface;
                leg.TargetBoneY = targetSole + (anklePosition.y - leg.SoleY);
                leg.Delta = targetSole - leg.SoleY;
                leg.ClipFootRotation = leg.Foot.rotation;
                leg.Prepared = true;
            }

            // The pelvis follows the ground the CAPSULE stands on, not
            // whichever boot found the lower tread. On a floor the two are
            // the same number to the last decimal — both boots probe the
            // surface under him — but on a stair flight the boots straddle
            // two or three risers while the controller walks one hidden
            // ramp, and following the lower boot dives the hips a quarter
            // of a metre ahead of the footfall every step.
            float pelvisDrop;
            if (probe != null &&
                probe.TryProbeActorGround(
                    actorRoot.position,
                    out float actorGroundY,
                    out _))
            {
                float planeDelta = PlayerFootPlacementRules.PelvisPlaneDelta(
                    actorGroundY,
                    soleClearance,
                    referenceSole);
                // Rate-limited like a boot's own surface, and for the same
                // reason: the ray reads the ground under the capsule's
                // CENTRE, which crosses a kerb's edge a few frames after
                // the controller has already stepped the body up onto it.
                // Raw, that pair of frames would drop the hips a whole kerb
                // and snap them back. The term is measured against the clip
                // plane, which rides the root, so a ramp under him is
                // already a constant here and nothing filters the descent.
                if (hasSmoothedPlaneDelta)
                {
                    planeDelta = Mathf.MoveTowards(
                        smoothedPlaneDelta,
                        planeDelta,
                        PlayerFootPlacementRules.MaximumTargetStep(
                            1f,
                            deltaTime));
                }

                smoothedPlaneDelta = planeDelta;
                hasSmoothedPlaneDelta = true;
                pelvisDrop = PlayerFootPlacementRules.PelvisDrop(
                    planeDelta,
                    planeDelta,
                    input.RunBlend,
                    input.HasRunClip);
            }
            else
            {
                // Nothing walkable under him — a pedestrian bound without a
                // probe, a body over a gap — so the boots are all there is.
                float leftDelta = legs[0].Prepared ? legs[0].Delta : legs[1].Delta;
                float rightDelta = legs[1].Prepared ? legs[1].Delta : legs[0].Delta;
                hasSmoothedPlaneDelta = false;
                pelvisDrop = PlayerFootPlacementRules.PelvisDrop(
                    leftDelta,
                    rightDelta,
                    input.RunBlend,
                    input.HasRunClip);
            }

            // The reach is measured from the hip the GROUND has moved, not
            // from the crouched one: a drunk's squat and his reach for a
            // boot he has thrown wide are two separate demands on the hips
            // and they add, exactly as they did before the ground term
            // existed. Measuring after the crouch would quietly make the
            // deeper of the two the whole answer.
            float reachHipDrop = pelvisDrop;
            pelvisDrop -= input.CrouchMetres;
            // A boot out of its leg's reach from where the hips have just
            // been put brings the hips down to it — a wide drunk stance is
            // a squat, a tread below the flight's ramp is a reach — never
            // the sole up off the ground.
            if (allowReachDip)
            {
                pelvisDrop -= ReachShortfall(input, actorForward, reachHipDrop);
            }
            lastPelvisDrop = pelvisDrop;
            if (Mathf.Abs(pelvisDrop) > 0.00001f)
            {
                pelvis.position += Vector3.up * pelvisDrop;
            }

            for (int index = 0; index < legs.Length; index++)
            {
                Leg leg = legs[index];
                if (!leg.Prepared)
                {
                    continue;
                }

                bool stepping = stepActive && stepSide == (FootSide)index;
                // The drunk walk's boot is placed by the layer through
                // its whole cycle, swing and stance alike; the sober
                // boot keeps the clip's swing untouched as before.
                bool disordered = input.ForwardGait &&
                                  !stepping &&
                                  !leg.Locked &&
                                  input.HasGaitDisorder(index);
                float weight = stepping
                    ? ikBlend
                    : PlayerFootPlacementRules.IkWeight(
                        ikBlend,
                        input.RunBlend,
                        plants[index]);
                if (disordered)
                {
                    weight = Mathf.Max(weight, ikBlend);
                }

                if (weight <= 0.0001f)
                {
                    continue;
                }

                Vector3 footPosition = leg.Foot.position;
                Vector3 target = new Vector3(
                    footPosition.x,
                    leg.TargetBoneY,
                    footPosition.z);
                float gaitYaw = 0f;
                if (stepping)
                {
                    // The balance model owns this boot: it goes where the
                    // recovery step says, arcing over the ground the probe
                    // found there.
                    target.x = stepWorldPosition.x;
                    target.z = stepWorldPosition.z;
                    target.y += stepLift;
                }
                else if (leg.Locked)
                {
                    target.x = leg.LockPosition.x;
                    target.z = leg.LockPosition.z;
                }
                else if (disordered)
                {
                    Vector2 offsetLocal = index == 0
                        ? input.LeftFootOffsetLocal
                        : input.RightFootOffsetLocal;
                    Vector3 offsetWorld =
                        Vector3.Cross(Vector3.up, actorForward) * offsetLocal.x +
                        actorForward * offsetLocal.y;
                    target.x += offsetWorld.x;
                    target.z += offsetWorld.z;
                    target.y += index == 0 ? input.LeftFootLift : input.RightFootLift;
                    gaitYaw = index == 0 ? input.LeftFootYawDegrees : input.RightFootYawDegrees;
                }

                if (!stepping &&
                    !leg.Locked &&
                    !disordered &&
                    Mathf.Abs(target.y - footPosition.y) < 0.001f)
                {
                    // The clip already has this boot where the ground is:
                    // leave the authored leg alone rather than re-solving
                    // it into a fractionally different knee.
                    leg.LastAnklePosition = footPosition;
                    leg.HasLastAnklePosition = true;
                    continue;
                }

                Vector3 hip = leg.Thigh.position;
                // Never pull a target closer than the clip's own extension:
                // the authored idle stands almost straight, and a clamp to
                // 98 % of the chain would float every stance boot.
                float reach = Mathf.Max(
                    leg.Length * PlayerFootPlacementRules.DefaultReachFraction,
                    Vector3.Distance(hip, footPosition));
                Vector3 clamped = ClampToReach(hip, target, reach);
                if (leg.Locked && clamped != target)
                {
                    // The stance foot has been carried out of reach; let it
                    // go rather than drag the hip after it.
                    leg.Release();
                    target = new Vector3(
                        footPosition.x,
                        leg.TargetBoneY,
                        footPosition.z);
                    clamped = ClampToReach(hip, target, reach);
                }

                Quaternion footRotation = leg.ClipFootRotation;
                if (Mathf.Abs(gaitYaw) > 0.001f)
                {
                    // The toes turn out about up, before any ramp tilt.
                    footRotation = Quaternion.AngleAxis(gaitYaw, Vector3.up) * footRotation;
                }

                FootGroundSample sample = samples[index];
                if (sample.HasSurface &&
                    sample.Kind == FootSurfaceKind.Ramp)
                {
                    Vector3 soleUp = leg.SoleUp();
                    Quaternion tilt = Quaternion.FromToRotation(
                        soleUp,
                        sample.Normal);
                    tilt.ToAngleAxis(out float tiltAngle, out Vector3 tiltAxis);
                    if (tiltAngle > 180f)
                    {
                        tiltAngle -= 360f;
                    }

                    tilt = Quaternion.AngleAxis(
                        Mathf.Clamp(
                            tiltAngle,
                            -MaximumSoleTiltDegrees,
                            MaximumSoleTiltDegrees),
                        tiltAxis);
                    footRotation = Quaternion.Slerp(
                        footRotation,
                        tilt * footRotation,
                        plants[index]);
                }

                Vector3 hint = KneeHint(leg, hip, clamped);
                LimbTwoBoneIk.Solve(
                    leg.Thigh,
                    leg.Shin,
                    leg.Foot,
                    clamped,
                    footRotation,
                    hint,
                    weight,
                    float.PositiveInfinity,
                    true);
                AlignHingeRoll(leg.Thigh, leg.Shin, leg.Foot, leg.KneeForward(), weight);
                leg.LastAnklePosition = leg.Foot.position;
                leg.HasLastAnklePosition = true;
            }
        }

        /// <summary>
        /// How far the hips must come down for a boot to reach its target:
        /// the largest shortfall of any leg whose target lies beyond its
        /// reach from the hip the pelvis drop has ALREADY moved (a hip
        /// lifted onto a kerb has to answer for the foot still on the road
        /// below it).
        ///
        /// A boot the drunk walk has thrown wide or long counts whatever
        /// its phase — that stance is a squat and was tuned as one. A sober
        /// boot counts by its plant instead, so the leg carrying the weight
        /// brings the hips to its tread while the one still swinging down a
        /// flight reaches as far as its leg goes and no further. Following
        /// a swinging boot is how a body ends up diving a whole riser ahead
        /// of its own footfall.
        /// </summary>
        private float ReachShortfall(
            in Player3DProceduralLayerInput input,
            Vector3 actorForward,
            float pelvisDrop)
        {
            float shortfall = 0f;
            Vector3 right = Vector3.Cross(Vector3.up, actorForward);
            float lowestPlant = Mathf.Min(plants[0], plants[1]);
            float highestPlant = Mathf.Max(plants[0], plants[1]);
            for (int index = 0; index < legs.Length; index++)
            {
                Leg leg = legs[index];
                bool stepping = stepActive && stepSide == (FootSide)index;
                if (!leg.Prepared || stepping || leg.Locked)
                {
                    continue;
                }

                bool disordered = input.ForwardGait &&
                                  input.HasGaitDisorder(index);
                float weight = disordered
                    ? 1f
                    : PlayerFootPlacementRules.StanceWeight(
                        plants[index],
                        lowestPlant,
                        highestPlant);
                if (weight <= 0.0001f)
                {
                    continue;
                }

                Vector3 offsetWorld = Vector3.zero;
                if (disordered)
                {
                    Vector2 offsetLocal = index == 0
                        ? input.LeftFootOffsetLocal
                        : input.RightFootOffsetLocal;
                    offsetWorld = right * offsetLocal.x +
                                  actorForward * offsetLocal.y;
                }

                Vector3 hip = leg.Thigh.position;
                Vector3 ankle = leg.Foot.position;
                Vector3 planar = new Vector3(
                    ankle.x + offsetWorld.x - hip.x,
                    0f,
                    ankle.z + offsetWorld.z - hip.z);
                float reach = Mathf.Max(
                    leg.Length * PlayerFootPlacementRules.DefaultReachFraction,
                    Vector3.Distance(hip, ankle));
                shortfall = Mathf.Max(
                    shortfall,
                    weight * PlayerFootPlacementRules.ReachShortfall(
                        planar.magnitude,
                        hip.y + pelvisDrop - leg.TargetBoneY,
                        reach));
            }

            return Mathf.Max(0f, shortfall);
        }

        private void CaptureBase()
        {
            basePoses[0] = new BoneLocalPose(pelvis);
            basePoses[1] = new BoneLocalPose(chest);
            basePoses[2] = new BoneLocalPose(leftUpperArm);
            basePoses[3] = new BoneLocalPose(rightUpperArm);
            basePoses[4] = new BoneLocalPose(legs[0].Thigh);
            basePoses[5] = new BoneLocalPose(legs[0].Shin);
            basePoses[6] = new BoneLocalPose(legs[0].Foot);
            basePoses[7] = new BoneLocalPose(legs[1].Thigh);
            basePoses[8] = new BoneLocalPose(legs[1].Shin);
            basePoses[9] = new BoneLocalPose(legs[1].Foot);
            basePoses[10] = new BoneLocalPose(arms[0].Forearm);
            basePoses[11] = new BoneLocalPose(arms[0].Hand);
            basePoses[12] = new BoneLocalPose(arms[1].Forearm);
            basePoses[13] = new BoneLocalPose(arms[1].Hand);
            basePoses[14] = new BoneLocalPose(neck);
            basePoses[15] = new BoneLocalPose(head);
            baseCaptured = true;
        }

        private void CaptureSoleClearance(float lowestSole)
        {
            if (probe == null)
            {
                return;
            }

            float rootY = actorRoot.position.y;
            float highestSurface = float.NegativeInfinity;
            for (int index = 0; index < legs.Length; index++)
            {
                Leg leg = legs[index];
                if (!leg.IsComplete)
                {
                    continue;
                }

                FootGroundSample sample = probe.Probe(
                    leg.Foot.position,
                    leg.FootForward(),
                    rootY);
                if (sample.HasSurface)
                {
                    highestSurface = Mathf.Max(highestSurface, sample.HeelY);
                }
            }

            if (float.IsNegativeInfinity(highestSurface))
            {
                return;
            }

            soleClearance = Mathf.Max(0f, lowestSole - highestSurface);
            soleClearanceCaptured = true;
        }

        private bool TryGetSole(FootSide side, out float soleY)
        {
            if (probe != null && probe.TryGetSoleHeight(side, out soleY))
            {
                return true;
            }

            soleY = 0f;
            return false;
        }

        private bool TryGetLowestSole(out float lowestSole)
        {
            if (probe != null && probe.TryGetLowestSoleHeight(out lowestSole))
            {
                return true;
            }

            lowestSole = float.PositiveInfinity;
            for (int index = 0; index < legs.Length; index++)
            {
                Transform foot = legs[index].Foot;
                if (foot != null)
                {
                    lowestSole = Mathf.Min(lowestSole, foot.position.y);
                }
            }

            return !float.IsPositiveInfinity(lowestSole);
        }

        private Vector3 PlanarForward()
        {
            Vector3 forward = actorRoot != null
                ? actorRoot.forward
                : Vector3.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f
                ? forward.normalized
                : Vector3.forward;
        }

        private static void RotateBone(
            Transform bone,
            Vector3 localAxis,
            float degrees)
        {
            if (bone != null && Mathf.Abs(degrees) > 0.00001f)
            {
                bone.localRotation *= Quaternion.AngleAxis(
                    degrees,
                    localAxis);
            }
        }

        private static Vector3 ClampToReach(
            Vector3 hip,
            Vector3 target,
            float reach)
        {
            Vector3 offset = target - hip;
            if (reach <= 0f || offset.sqrMagnitude <= reach * reach)
            {
                return target;
            }

            return hip + offset.normalized * reach;
        }

        private sealed class Leg
        {
            private Vector3 footForwardLocal = Vector3.forward;
            private Vector3 soleUpLocal = Vector3.up;
            private Vector3 kneeForwardLocal = Vector3.forward;

            public Transform Thigh { get; private set; }
            public Transform Shin { get; private set; }
            public Transform Foot { get; private set; }
            public float Length { get; private set; }
            public bool IsComplete =>
                Thigh != null && Shin != null && Foot != null;

            public bool Locked { get; private set; }
            public Vector3 LockPosition { get; private set; }
            public Vector3 LastAnklePosition;
            public bool HasLastAnklePosition;

            public float SoleY;
            public float TargetBoneY;
            public float Delta;

            /// <summary>
            /// The smoothed surface target, held above the ACTOR ROOT so
            /// the body's own descent is never mistaken for ground moving
            /// under the boot.
            /// </summary>
            public float SmoothedSoleAboveRoot;
            public bool HasSmoothedTarget;
            public bool Prepared;
            public Quaternion ClipFootRotation;

            public void Bind(Transform thigh, Transform shin, Transform foot)
            {
                Thigh = thigh;
                Shin = shin;
                Foot = foot;
                Length = 0f;
                Locked = false;
                HasSmoothedTarget = false;
                Prepared = false;
            }

            public void Calibrate(Vector3 actorForward)
            {
                if (!IsComplete)
                {
                    return;
                }

                Length = LimbTwoBoneIk.ChainLength(Thigh, Shin, Foot);
                Quaternion inverse = Quaternion.Inverse(Foot.rotation);
                footForwardLocal = inverse * actorForward;
                soleUpLocal = inverse * Vector3.up;
                kneeForwardLocal = Quaternion.Inverse(Thigh.rotation) *
                                   actorForward;
            }

            /// <summary>
            /// The way the knee bends, in the thigh's current frame: the
            /// actor's forward while he stands, up when the thigh is
            /// horizontal, wherever the thigh carries it when he lies.
            /// </summary>
            public Vector3 KneeForward()
            {
                return Thigh != null
                    ? Thigh.rotation * kneeForwardLocal
                    : Vector3.forward;
            }

            public Vector3 FootForward()
            {
                return Foot != null
                    ? Foot.rotation * footForwardLocal
                    : Vector3.forward;
            }

            public Vector3 SoleUp()
            {
                return Foot != null
                    ? Foot.rotation * soleUpLocal
                    : Vector3.up;
            }

            public void Lock(Vector3 worldPosition)
            {
                Locked = true;
                LockPosition = worldPosition;
            }

            public void Release()
            {
                Locked = false;
            }
        }

        private readonly struct BoneLocalPose
        {
            private readonly Transform bone;
            private readonly Vector3 localPosition;
            private readonly Quaternion localRotation;

            public BoneLocalPose(Transform boneToCapture)
            {
                bone = boneToCapture;
                localPosition = bone != null
                    ? bone.localPosition
                    : Vector3.zero;
                localRotation = bone != null
                    ? bone.localRotation
                    : Quaternion.identity;
            }

            public void Restore()
            {
                if (bone == null)
                {
                    return;
                }

                bone.localPosition = localPosition;
                bone.localRotation = localRotation;
            }
        }
    }
}
