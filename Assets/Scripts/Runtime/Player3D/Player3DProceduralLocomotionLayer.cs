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
            float leftArmSpreadDegrees,
            float rightArmSpreadDegrees,
            float crouchMetres,
            float plantLeft,
            float plantRight,
            float runBlend,
            bool hasRunClip,
            bool forwardGait,
            PlayerWallReachPose wallReach = default)
        {
            Enabled = enabled;
            PelvisRollDegrees = pelvisRollDegrees;
            ChestRollDegrees = chestRollDegrees;
            PelvisPitchDegrees = pelvisPitchDegrees;
            LeftArmSpreadDegrees = leftArmSpreadDegrees;
            RightArmSpreadDegrees = rightArmSpreadDegrees;
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

        public static Player3DProceduralLayerInput Disabled => default;

        public bool Enabled { get; }
        public float PelvisRollDegrees { get; }
        public float ChestRollDegrees { get; }

        /// <summary>Forward pitch of the pelvis, degrees.</summary>
        public float PelvisPitchDegrees { get; }
        public float LeftArmSpreadDegrees { get; }
        public float RightArmSpreadDegrees { get; }
        public float CrouchMetres { get; }
        public float PlantLeft { get; }
        public float PlantRight { get; }
        public float RunBlend { get; }
        public bool HasRunClip { get; }
        public bool ForwardGait { get; }
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

        /// <summary>A ramp tilts the sole at most this much.</summary>
        public const float MaximumSoleTiltDegrees = 18f;

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
        private Player3DFootGroundProbe probe;
        private bool baseCaptured;
        private float ikBlend;
        private float groundedFootHeightOffset;
        private bool groundedOffsetCaptured;
        private float soleClearance;
        private bool soleClearanceCaptured;
        private float lastPelvisDrop;

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
            }

            Vector3 actorRight = Vector3.Cross(Vector3.up, actorForward);
            arms[0].Calibrate(actorRight);
            arms[1].Calibrate(-actorRight);

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

        /// <summary>
        /// The wall hand: the palm goes to the wall point at the pose's
        /// weight, elbow hanging down and back, palm turned to the wall.
        /// Runs after the legs so the shoulder it reaches from is where
        /// the lean put it.
        /// </summary>
        private void ApplyWallHand(in Player3DProceduralLayerInput input)
        {
            PlayerWallReachPose reach = input.WallReach;
            if (!reach.Active || reach.Weight <= 0.0001f)
            {
                return;
            }

            Arm arm = arms[reach.RightHand ? 1 : 0];
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

            Vector3 back = -PlanarForward();
            Vector3 hint = shoulder +
                           Vector3.down * 0.3f +
                           back * 0.2f;
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
        }

        private sealed class Arm
        {
            private Vector3 palmLocal = Vector3.forward;

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
            /// inward side in the hand's own space.
            /// </summary>
            public void Calibrate(Vector3 inwardWorld)
            {
                if (!IsComplete)
                {
                    return;
                }

                Length = LimbTwoBoneIk.ChainLength(Upper, Forearm, Hand);
                palmLocal = Quaternion.Inverse(Hand.rotation) * inwardWorld;
            }

            public Vector3 PalmDirection()
            {
                return Hand != null
                    ? Hand.rotation * palmLocal
                    : Vector3.forward;
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
            ApplyWallHand(input);
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
            RotateBone(pelvis, Vector3.forward, input.PelvisRollDegrees);
            RotateBone(pelvis, Vector3.right, input.PelvisPitchDegrees);
            RotateBone(chest, Vector3.forward, input.ChestRollDegrees);
            RotateBone(
                leftUpperArm,
                Vector3.forward,
                input.LeftArmSpreadDegrees);
            RotateBone(
                rightUpperArm,
                Vector3.forward,
                input.RightArmSpreadDegrees);
        }

        private void ApplyLegs(
            in Player3DProceduralLayerInput input,
            float deltaTime)
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
                    baseSole = Mathf.MoveTowards(
                        leg.SmoothedTargetSole,
                        baseSole,
                        PlayerFootPlacementRules.MaximumTargetStep(
                            plant,
                            deltaTime));
                }

                float targetSole = baseSole + lift;
                leg.SmoothedTargetSole = baseSole;
                leg.HasSmoothedTarget = sample.HasSurface;
                leg.TargetBoneY = targetSole + (anklePosition.y - leg.SoleY);
                leg.Delta = targetSole - leg.SoleY;
                leg.ClipFootRotation = leg.Foot.rotation;
                leg.Prepared = true;
            }

            float leftDelta = legs[0].Prepared ? legs[0].Delta : legs[1].Delta;
            float rightDelta = legs[1].Prepared ? legs[1].Delta : legs[0].Delta;
            float pelvisDrop = PlayerFootPlacementRules.PelvisDrop(
                leftDelta,
                rightDelta,
                input.RunBlend,
                input.HasRunClip);
            pelvisDrop -= input.CrouchMetres;
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
                float weight = stepping
                    ? ikBlend
                    : PlayerFootPlacementRules.IkWeight(
                        ikBlend,
                        input.RunBlend,
                        plants[index]);
                if (weight <= 0.0001f)
                {
                    continue;
                }

                Vector3 footPosition = leg.Foot.position;
                Vector3 target = new Vector3(
                    footPosition.x,
                    leg.TargetBoneY,
                    footPosition.z);
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

                if (!stepping &&
                    !leg.Locked &&
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

                Vector3 hint = leg.Shin.position +
                               actorForward * KneeHintForward +
                               Vector3.up * KneeHintUp;
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
                leg.LastAnklePosition = leg.Foot.position;
                leg.HasLastAnklePosition = true;
            }
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
            basePoses[14] = default;
            basePoses[15] = default;
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
            public float SmoothedTargetSole;
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
