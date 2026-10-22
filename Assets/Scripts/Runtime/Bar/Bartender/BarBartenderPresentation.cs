using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    /// <summary>One service sampler and two-hand contact pass over the default NPC rig.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(320)]
    public sealed class BarBartenderPresentation : MonoBehaviour
    {
        public const float ReachBlendSeconds = 0.28f;
        public const float BeerPlacementTorsoLeanDegrees = 38f;
        public const float BeerPlacementTorsoBlendSeconds = 0.18f;
        public const float MaximumCounterHipLeanDegrees = 20f;
        public const int OrdinaryHandCount = 2;
        public const int OrdinaryVesselHandIndex = 0;
        public const int OrdinaryBottleHandIndex = 1;
        public const float CounterHandSupportClearance = 0.002f;
        private const float MaximumClavicleReachDegrees = 15f;
        private const float ClavicleReachThresholdMeters = 0.002f;
        private BarBartenderAssetRegistry registry;
        private NpcHandPose handPose;
        private Transform[] chainShoulders;
        private Transform[] chainClavicles;
        private Quaternion[] authoredClavicleRotations;
        private Transform[] chainElbows;
        private Transform[] chainGrips;
        private Transform[] chainHands;
        private SeatedArmHandAttachment[] handAttachments;
        private AnatomicalHandFrame[] anatomicalHands;
        private NpcHandGripKind[] chainGripKinds;
        private Quaternion[] chainHandRotations;
        private bool[] chainHasGripFrame;
        private Vector3[] chainElbowDirections;
        private bool[] chainHasElbowDirection;
        private Vector3[] chainTargets;
        private float[] chainWeights;
        private float[] chainGoalWeights;
        private Transform[] proceduralBones;
        private Quaternion[] authoredRotations;
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private AnimationClipPlayable defaultPlayable;
        private AnimationClipPlayable actionPlayable;
        private AnimationClip actionClip;
        private BarBartenderClipKind currentClipKind =
            BarBartenderClipKind.Wipe;
        private float currentClipTimeSeconds;
        private BarBartenderClipKind requestedClipKind = BarBartenderClipKind.Wipe;
        private float requestedClipTimeSeconds;
        private float ordinaryIdleElapsedSeconds;
        private Vector3 ordinaryTorsoLeanTarget;
        private float ordinaryTorsoLeanWeight;
        private float ordinaryTorsoLeanGoalWeight;
        private AnatomicalHandFrame counterSupportHandFrame;
        private Vector3 counterSupportContactInHand;
        private Vector3 counterSupportSurfaceTarget;
        private float counterSupportWeight;
        private float counterSupportGoalWeight;
        private bool hasGraph;
        private bool hasActionPlayable;
        private bool isInitialized;

        public bool IsInitialized => isInitialized;
        public BarBartenderAssetRegistry Registry => registry;
        public bool UsesOrdinaryRig => isInitialized;
        public BarBartenderClipKind CurrentClipKind => currentClipKind;
        public float CurrentClipTimeSeconds => currentClipTimeSeconds;
        public BarBartenderTowelPresentation TowelPresentation { get; private set; }
        public bool IsTowelServiceReady => TowelPresentation != null && TowelPresentation.ServiceReady;
        public bool IsTowelWipeReady => TowelPresentation != null && TowelPresentation.WipeReady;
        public bool IsTowelTransitioning => TowelPresentation != null && TowelPresentation.IsTransitioning;
        public float CounterHandSupportWeight => counterSupportWeight;
        /// <summary>Desired physical palm contact, including the two millimetre surface clearance.</summary>
        public Vector3 CounterHandSupportTarget => counterSupportSurfaceTarget + transform.up * CounterHandSupportClearance;
        public Vector3 CounterHandSupportPalmContact => isInitialized
            ? registry.RightHand.position + registry.RightHand.rotation * counterSupportContactInHand : Vector3.zero;
        public float CounterHandSupportContactError => isInitialized
            ? Vector3.Distance(CounterHandSupportPalmContact, CounterHandSupportTarget) : float.PositiveInfinity;
        public Vector3 CounterHandSupportPalmNormal => isInitialized
            ? registry.RightHand.rotation * counterSupportHandFrame.PalmNormalInHand : Vector3.zero;
        public Vector3 CounterHandSupportFingerDirection => isInitialized
            ? registry.RightHand.rotation * counterSupportHandFrame.FingerDirectionInHand : Vector3.zero;

        public void RequestTowelService(bool serviceRequested) => TowelPresentation?.RequestService(serviceRequested);
        public int ChainCount =>
            chainShoulders != null ? chainShoulders.Length : 0;

        public void Initialize(
            BarBartenderAssetRegistry assetRegistry)
        {
            if (isInitialized)
            {
                throw new InvalidOperationException(
                    "The bartender presentation is already " +
                    "initialized.");
            }

            registry = assetRegistry != null
                ? assetRegistry
                : throw new ArgumentNullException(
                    nameof(assetRegistry));
            handPose = registry.Actor.GetComponent<NpcHandPose>();
            if (registry.Chest == null ||
                registry.Head == null ||
                registry.LeftUpperArm == null ||
                registry.RightUpperArm == null)
            {
                throw new InvalidOperationException(
                    "The bartender registry lacks its " +
                    "canonical bones.");
            }

            InitializeOrdinaryRig();
            CacheCounterHandSupportContact();
            chainTargets = new Vector3[ChainCount];
            chainWeights = new float[ChainCount];
            chainGoalWeights = new float[ChainCount];
            chainHandRotations = new Quaternion[ChainCount];
            chainHasGripFrame = new bool[ChainCount];
            chainElbowDirections = new Vector3[ChainCount];
            chainHasElbowDirection = new bool[ChainCount];
            chainGripKinds = new NpcHandGripKind[ChainCount];
            proceduralBones = new[]
            {
                registry.Pelvis, registry.Spine, registry.Chest, registry.Neck, registry.Head,
                registry.LeftClavicle, registry.RightClavicle,
                registry.LeftUpperArm, registry.LeftForearm, registry.LeftHand,
                registry.RightUpperArm, registry.RightForearm, registry.RightHand,
                registry.LeftThigh, registry.LeftShin, registry.LeftFoot,
                registry.RightThigh, registry.RightShin, registry.RightFoot
            };
            authoredRotations = new Quaternion[proceduralBones.Length];
            CaptureAuthoredPose();
            isInitialized = true;
            TowelPresentation = new BarBartenderTowelPresentation(this, registry);
            if (Application.isPlaying)
            {
                CreateOrdinaryGraph();
            }
            Advance(0f);
        }

        private void InitializeOrdinaryRig()
        {
            if (registry.Animator == null ||
                registry.Animator.avatar == null ||
                registry.LeftVesselSocket == null ||
                registry.RightBottleSocket == null ||
                registry.VesselGripAnchor == null ||
                registry.BottleGripAnchor == null)
            {
                throw new InvalidOperationException(
                    "The ordinary bartender registry lacks its authored " +
                    "clips, Avatar or two-hand service sockets.");
            }

            chainShoulders = new[]
            {
                registry.LeftUpperArm,
                registry.RightUpperArm
            };
            chainClavicles = new[] { registry.LeftClavicle, registry.RightClavicle };
            authoredClavicleRotations = new Quaternion[OrdinaryHandCount];
            chainElbows = new[]
            {
                registry.LeftForearm,
                registry.RightForearm
            };
            chainGrips = new[]
            {
                registry.VesselGripAnchor,
                registry.BottleGripAnchor
            };
            chainHands = new[] { registry.LeftHand, registry.RightHand };
            handAttachments = new[]
            {
                new SeatedArmHandAttachment(registry.LeftHand, registry.VesselGripAnchor),
                new SeatedArmHandAttachment(registry.RightHand, registry.BottleGripAnchor)
            };
            anatomicalHands = new[]
            {
                registry.HandPose.GetAnatomicalFrame(true),
                registry.HandPose.GetAnatomicalFrame(false)
            };
            registry.Animator.enabled = true;
            registry.SetServiceTowelVisible(true);
        }

        /// <summary>Indices zero and one address the left vessel and right bottle hands.</summary>
        public void SetChainTarget(
            int chainIndex,
            Vector3 worldPosition,
            float weight)
        {
            if (!isInitialized ||
                chainIndex < 0 ||
                chainIndex >= ChainCount)
            {
                return;
            }

            float goalWeight = Mathf.Clamp01(weight);
            chainHasGripFrame[chainIndex] = false;
            chainHasElbowDirection[chainIndex] = false;
            // A released chain still blends out for ReachBlendSeconds. Keep
            // its last meaningful target during that fade instead of aiming
            // the partly weighted arm at the world origin.
            if (goalWeight > 0.0001f ||
                chainWeights[chainIndex] <= 0.0001f ||
                worldPosition != Vector3.zero)
            {
                chainTargets[chainIndex] = worldPosition;
            }

            chainGoalWeights[chainIndex] = goalWeight;
        }

        public void SetChainGripFrame(int chainIndex, Vector3 cylinderAxis, Vector3 palmTowardObject,
            NpcHandGripKind kind = NpcHandGripKind.Cylindrical)
        {
            if (!isInitialized || chainIndex < 0 || chainIndex >= ChainCount)
                return;
            SelectGripKind(chainIndex, kind);
            chainHandRotations[chainIndex] = anatomicalHands[chainIndex]
                .ResolveCylinderPose(Vector3.zero, cylinderAxis, palmTowardObject).rotation;
            chainHasGripFrame[chainIndex] = true;
        }

        public void SetChainElbowDirection(int chainIndex, Vector3 worldDirection)
        {
            if (!isInitialized || chainIndex < 0 || chainIndex >= ChainCount ||
                worldDirection.sqrMagnitude < .000001f) return;
            chainElbowDirections[chainIndex] = worldDirection;
            chainHasElbowDirection[chainIndex] = true;
        }

        public bool ResolveAnatomicalGripContactAfterWristRoll(
            int chainIndex, Vector3 cylinderCentre, Vector3 cylinderAxis, Vector3 palmTowardObject,
            NpcHandGripKind kind = NpcHandGripKind.Cylindrical)
        {
            if (!isInitialized || chainIndex < 0 || chainIndex >= ChainCount)
                return false;
            SetChainGripFrame(chainIndex, cylinderAxis, palmTowardObject, kind);
            if (chainWeights[chainIndex] >= 0.95f)
                SolveOrdinaryReach(chainIndex, cylinderCentre, 1f);
            else
                // Carry walks keep their authored elbow/shoulder motion.
                // Roll the held hand anatomically, then the prop owner seats
                // its real grasp centre back onto that same moving hand.
                chainHands[chainIndex].rotation = chainHandRotations[chainIndex];
            handPose.SetGrip(chainIndex == OrdinaryVesselHandIndex, 1f, kind);
            return true;
        }

        private void SelectGripKind(int index, NpcHandGripKind kind)
        {
            if (chainGripKinds[index] == kind) return;
            chainGripKinds[index] = kind;
            bool isLeft = index == OrdinaryVesselHandIndex;
            anatomicalHands[index] = handPose.GetAnatomicalFrame(isLeft, kind);
            registry.ConfigureGripKind(isLeft, kind);
            handAttachments[index] = new SeatedArmHandAttachment(chainHands[index], chainGrips[index]);
        }

        /// <summary>
        /// Bends the ordinary service rig from the hips toward a reachable
        /// counter contact. The hand solver then runs from that leaned pose,
        /// so a placed mug remains in the palm instead of finishing the route
        /// as an independently moving prop.
        /// </summary>
        public void SetCounterReachLean(
            Vector3 worldTarget,
            float weight)
        {
            ordinaryTorsoLeanGoalWeight = Mathf.Clamp01(weight);
            if (ordinaryTorsoLeanGoalWeight > 0.0001f)
            {
                ordinaryTorsoLeanTarget = worldTarget;
            }
        }

        /// <summary>Brace the free right palm on a measured counter point; zero releases with the ordinary reach blend.</summary>
        public void SetCounterHandSupport(Vector3 palmTarget, float weight)
        {
            if (!isInitialized || !float.IsFinite(weight)) return;
            float goal = Mathf.Clamp01(weight);
            if (goal > 0.0001f)
            {
                if (!float.IsFinite(palmTarget.x) || !float.IsFinite(palmTarget.y) || !float.IsFinite(palmTarget.z)) return;
                counterSupportSurfaceTarget = palmTarget;
            }
            counterSupportGoalWeight = goal;
        }

        private void CacheCounterHandSupportContact()
        {
            counterSupportHandFrame = anatomicalHands[OrdinaryBottleHandIndex];
            Transform hand = registry.RightHand;
            Quaternion inverse = Quaternion.Inverse(hand.rotation);
            Vector3 normal = counterSupportHandFrame.PalmNormalInHand;
            Vector3 contact = Vector3.zero;
            float depth = float.NegativeInfinity;
            int count = 0;
            foreach (NpcHandPose.HandBinding binding in handPose.Hands)
            {
                if (binding.IsLeft) continue;
                foreach (SkinnedMeshRenderer renderer in binding.Renderers)
                {
                    if (renderer == null || !(renderer.name.StartsWith("GEO_Hand.", StringComparison.Ordinal) ||
                        renderer.name.Contains("HandPalm."))) continue;
                    Mesh mesh = renderer.sharedMesh;
                    int bone = Array.IndexOf(renderer.bones, hand);
                    if (mesh == null || bone < 0) continue;
                    Matrix4x4 bindPose = mesh.bindposes[bone];
                    // The same immutable neutral, rotation-only metre space
                    // used by NpcHandPose. No live mesh bake or frame search.
                    foreach (Vector3 vertex in mesh.vertices)
                    {
                        Vector3 point = inverse * hand.TransformVector(bindPose.MultiplyPoint3x4(vertex));
                        float projection = Vector3.Dot(point, normal);
                        if (projection > depth + 0.000001f)
                        { depth = projection; contact = point; count = 1; }
                        else if (Mathf.Abs(projection - depth) <= 0.000001f)
                        { contact += point; count++; }
                    }
                }
            }
            if (count == 0)
                throw new InvalidOperationException("A counter hand support needs the default NPC's neutral palm geometry.");
            // Averaging the supporting face's corners retains an actual
            // contact on that face instead of aiming the wrist/socket at wood.
            counterSupportContactInHand = contact / count;
        }

        public float GetChainWeight(int chainIndex)
        {
            return isInitialized &&
                   chainIndex >= 0 &&
                   chainIndex < ChainCount
                ? chainWeights[chainIndex]
                : 0f;
        }

        public Transform GetChainGrip(int chainIndex)
        {
            return isInitialized &&
                   chainIndex >= 0 &&
                   chainIndex < ChainCount
                ? chainGrips[chainIndex]
                : null;
        }

        /// <summary>
        /// Makes the authored service animation a pure reader of the bar's
        /// existing deterministic service clock. It never advances the shop
        /// or moves a bottle/vessel itself.
        /// </summary>
        public void ApplyServiceFrame(
            BarDrinkServiceFrame frame,
            bool leftHandCarriesMenu = false)
        {
            if (!isInitialized)
            {
                return;
            }

            switch (frame.Phase)
            {
                case BarDrinkServicePhase.CameraApproach:
                    SetOrdinaryClip(
                        BarBartenderClipKind.Notice,
                        frame.PhaseProgress *
                        ResolveClipLength(BarBartenderClipKind.Notice));
                    break;
                case BarDrinkServicePhase.BottlePickup:
                case BarDrinkServicePhase.BottleWalkToShelf:
                case BarDrinkServicePhase.BottleCarryToPour:
                case BarDrinkServicePhase.BottleCarryToGuest:
                case BarDrinkServicePhase.BottleVesselPlacement:
                case BarDrinkServicePhase.BottleWalkToShelfReturn:
                case BarDrinkServicePhase.BeerWalkToTap:
                case BarDrinkServicePhase.BeerCarryToGuest:
                case BarDrinkServicePhase.VesselPlacement:
                case BarDrinkServicePhase.BottleReturn:
                case BarDrinkServicePhase.BeerGlassPickup:
                case BarDrinkServicePhase.BeerGlassPlacement:
                    SetOrdinaryClip(
                        BarBartenderClipKind.ServiceStep,
                        frame.PhaseElapsedSeconds);
                    break;
                case BarDrinkServicePhase.Pouring:
                case BarDrinkServicePhase.BeerPouring:
                    SetOrdinaryClip(
                        BarBartenderClipKind.Pour,
                        frame.PhaseProgress *
                        ResolveClipLength(BarBartenderClipKind.Pour));
                    break;
                default:
                    SetOrdinaryClip(
                        BarBartenderClipKind.Wipe,
                        ordinaryIdleElapsedSeconds);
                    break;
            }

            if (leftHandCarriesMenu) RequestTowelService(true);
        }

        public void ResetServicePose()
        {
            if (!isInitialized)
            {
                return;
            }

            SetOrdinaryClip(
                BarBartenderClipKind.Wipe,
                ordinaryIdleElapsedSeconds);
            RequestTowelService(false);
        }

        public void ApplyCounterTravelPose(
            float elapsedSeconds,
            bool leftHandCarriesMenu = false,
            bool walkingBackwards = false)
        {
            if (!isInitialized)
            {
                return;
            }

            float time = Mathf.Max(0f, elapsedSeconds);
            if (walkingBackwards)
            {
                float length = ResolveClipLength(BarBartenderClipKind.Walk);
                if (length > 0.0001f) time = length - Mathf.Repeat(time, length);
            }
            SetOrdinaryClip(BarBartenderClipKind.Walk, time);
        }

        public void ApplyCounterTurnPose(
            float elapsedSeconds,
            bool leftHandCarriesMenu = false)
        {
            if (!isInitialized)
            {
                return;
            }

            SetOrdinaryClip(
                BarBartenderClipKind.ServiceStep,
                Mathf.Max(0f, elapsedSeconds));
        }

        /// <summary>
        /// Public so EditMode tests can drive the pose without a
        /// player loop.
        /// </summary>
        public void Advance(float deltaTime)
        {
            if (!isInitialized)
            {
                return;
            }

            AdvanceOrdinary(deltaTime);
        }

        private void LateUpdate()
        {
            Advance(Time.deltaTime);
        }

        private void AdvanceOrdinary(float deltaTime)
        {
            if (!IsTowelTransitioning)
                ordinaryIdleElapsedSeconds += Mathf.Max(0f, deltaTime);
            ResolveTowelSafeClip();
            if (currentClipKind == BarBartenderClipKind.Wipe)
            {
                currentClipTimeSeconds = ordinaryIdleElapsedSeconds;
            }

            if (Application.isPlaying && !hasGraph)
            {
                CreateOrdinaryGraph();
            }

            // Imported clips may omit constant channels. Remove the previous
            // procedural pass before sampling, so an unkeyed spine or wrist
            // never treats last frame's correction as its authored baseline.
            RestoreAuthoredPose();
            EvaluateOrdinaryAnimation();
            CaptureAuthoredPose();
            ordinaryTorsoLeanWeight = Mathf.MoveTowards(
                ordinaryTorsoLeanWeight,
                ordinaryTorsoLeanGoalWeight,
                Mathf.Max(0f, deltaTime) /
                BeerPlacementTorsoBlendSeconds);
            ApplyOrdinaryTorsoLean();
            counterSupportWeight = Mathf.MoveTowards(counterSupportWeight, counterSupportGoalWeight,
                Mathf.Max(0f, deltaTime) / ReachBlendSeconds);
            for (int index = 0; index < ChainCount; index++)
            {
                chainWeights[index] = Mathf.MoveTowards(
                    chainWeights[index],
                    chainGoalWeights[index],
                    deltaTime <= 0f
                        ? 0f
                        : deltaTime / ReachBlendSeconds);
                if (chainWeights[index] > 0.0001f)
                {
                    SolveOrdinaryReach(index);
                }
            }
            ApplyWipeSurfaceContact();
            ApplyCounterHandSupport();
            handPose?.SetGrip(true, chainWeights[OrdinaryVesselHandIndex], chainGripKinds[OrdinaryVesselHandIndex]);
            handPose?.SetGrip(false, chainWeights[OrdinaryBottleHandIndex] * (1f - counterSupportWeight),
                chainGripKinds[OrdinaryBottleHandIndex]);
            TowelPresentation.Advance(deltaTime, new Pose(registry.LeftHand.position, registry.LeftHand.rotation));
        }

        private void ApplyCounterHandSupport()
        {
            if (counterSupportWeight <= 0.0001f) return;
            Quaternion rotation = counterSupportHandFrame.ResolveHandRotation(-transform.up, transform.forward);
            Vector3 wrist = CounterHandSupportTarget - rotation * counterSupportContactInHand;
            // Preserve the already blended ordinary right arm at entry and
            // release. Its clavicle assistance is blended from that same pose.
            Quaternion beforeClavicle = registry.RightClavicle.localRotation;
            ApplyClavicleReach(OrdinaryBottleHandIndex, wrist, 1f);
            registry.RightClavicle.localRotation = Quaternion.Slerp(beforeClavicle,
                registry.RightClavicle.localRotation, counterSupportWeight);
            Vector3 hint = registry.RightUpperArm.position + transform.right * 0.35f - transform.up * 0.25f;
            LimbTwoBoneIk.Solve(registry.RightUpperArm, registry.RightForearm, registry.RightHand,
                wrist, rotation, hint, counterSupportWeight, float.PositiveInfinity, true);
        }

        internal void SolveTowelHand(Pose wrist)
        {
            ApplyClavicleReach(OrdinaryVesselHandIndex, wrist.position, 1f);
            Vector3 hint = registry.LeftUpperArm.position - transform.right * .35f - transform.up * .15f;
            LimbTwoBoneIk.Solve(registry.LeftUpperArm, registry.LeftForearm, registry.LeftHand,
                wrist.position, wrist.rotation, hint, 1f, float.PositiveInfinity, true);
        }

        private void RestoreAuthoredPose()
        {
            if (proceduralBones == null)
                return;
            for (int index = 0; index < proceduralBones.Length; index++)
                proceduralBones[index].localRotation = authoredRotations[index];
        }

        private void CaptureAuthoredPose()
        {
            for (int index = 0; index < proceduralBones.Length; index++)
                authoredRotations[index] = proceduralBones[index].localRotation;
            for (int index = 0; index < chainClavicles.Length; index++)
                authoredClavicleRotations[index] = chainClavicles[index].localRotation;
        }

        private void ApplyWipeSurfaceContact()
        {
            if (currentClipKind != BarBartenderClipKind.Wipe ||
                chainWeights[OrdinaryVesselHandIndex] > 0.0001f)
                return;
            // Keep the towel in its authored hand mount and move that hand,
            // rather than separating the cloth from the default character.
            Quaternion handRotation = registry.LeftHand.rotation;
            for (int iteration = 0; iteration < 3; iteration++)
            {
                if (!registry.TryResolveWipeHandTarget(out Vector3 target))
                    return;
                SolveOrdinaryReach(OrdinaryVesselHandIndex, target, 1f);
                registry.LeftHand.rotation = handRotation;
            }
        }

        private void ApplyOrdinaryTorsoLean()
        {
            if ((ordinaryTorsoLeanGoalWeight <= 0.0001f &&
                 ordinaryTorsoLeanWeight <= 0.0001f) ||
                registry.Spine == null || registry.Chest == null)
            {
                return;
            }

            Vector3 torsoDirection =
                registry.Chest.position - registry.Spine.position;
            Vector3 targetDirection = Vector3.ProjectOnPlane(
                ordinaryTorsoLeanTarget - registry.Spine.position,
                transform.up);
            if (torsoDirection.sqrMagnitude < 0.000001f ||
                targetDirection.sqrMagnitude < 0.000001f)
            {
                return;
            }

            torsoDirection.Normalize();
            targetDirection.Normalize();
            Vector3 axis = Vector3.Cross(
                torsoDirection,
                targetDirection);
            if (axis.sqrMagnitude < 0.000001f)
            {
                return;
            }

            // The shared cafe pose already leans forward. This is a total
            // torso limit, not another full bend added to that authored pose.
            float budget = Mathf.Max(0f, BeerPlacementTorsoLeanDegrees -
                Vector3.Angle(torsoDirection, transform.up));
            Vector3 spineOffset = registry.Spine.position - registry.Pelvis.position;
            Vector3 shoulderOffset = registry.LeftUpperArm.position - registry.Spine.position;
            Quaternion handRotation = chainHasGripFrame[OrdinaryVesselHandIndex]
                ? chainHandRotations[OrdinaryVesselHandIndex] : registry.LeftHand.rotation;
            Vector3 wristTarget = ordinaryTorsoLeanTarget - handRotation *
                handAttachments[OrdinaryVesselHandIndex].SocketPositionInHand;
            float reach = LimbTwoBoneIk.ChainLength(registry.LeftUpperArm,
                registry.LeftForearm, registry.LeftHand) * 0.98f;
            if (Vector3.Distance(registry.LeftUpperArm.position, wristTarget) <= reach)
                return;
            float low = 0f;
            float high = budget;
            for (int iteration = 0; iteration < 6; iteration++)
            {
                float middle = (low + high) * 0.5f;
                Vector3 shoulder = PredictCounterLeanShoulder(middle, axis.normalized,
                    registry.Pelvis.position, spineOffset, shoulderOffset);
                if (Vector3.Distance(shoulder, wristTarget) <= reach)
                    high = middle;
                else
                    low = middle;
            }
            // The moving vessel already supplies a continuous entry path:
            // its first target is at the carried hand, requiring zero lean.
            // Fading the required contact angle again detaches the fingers
            // midway through placement. Only the release is time-blended.
            float contactWeight = ordinaryTorsoLeanGoalWeight > 0.0001f
                ? 1f
                : Mathf.SmoothStep(0f, 1f, ordinaryTorsoLeanWeight);
            float angle = high * contactWeight;
            // A waist-only bend wastes the pelvis-to-spine lever and makes a
            // low shot glass unreachable even with the torso at its limit.
            // Move that pivot with a bounded hip hinge, then spend the rest
            // of the same total angle at the spine. The feet keep the clip's
            // original contacts; neither the actor nor the served dock moves.
            Pose leftContact = new Pose(registry.LeftFoot.position, registry.LeftFoot.rotation);
            Pose rightContact = new Pose(registry.RightFoot.position, registry.RightFoot.rotation);
            Vector3 leftKnee = registry.LeftShin.position;
            Vector3 rightKnee = registry.RightShin.position;
            Quaternion pelvisBase = registry.Pelvis.rotation;
            Quaternion spineBase = registry.Spine.rotation;
            Quaternion hipBend = Quaternion.AngleAxis(
                Mathf.Min(angle, MaximumCounterHipLeanDegrees), axis.normalized);
            registry.Pelvis.rotation = hipBend * pelvisBase;
            registry.Spine.rotation = Quaternion.AngleAxis(angle, axis.normalized) * spineBase;
            LimbTwoBoneIk.Solve(registry.LeftThigh, registry.LeftShin, registry.LeftFoot,
                leftContact.position, leftContact.rotation, leftKnee);
            LimbTwoBoneIk.Solve(registry.RightThigh, registry.RightShin, registry.RightFoot,
                rightContact.position, rightContact.rotation, rightKnee);
        }

        private static Vector3 PredictCounterLeanShoulder(float angle, Vector3 axis,
            Vector3 pelvis, Vector3 spineOffset, Vector3 shoulderOffset)
        {
            return pelvis + Quaternion.AngleAxis(Mathf.Min(angle, MaximumCounterHipLeanDegrees), axis) *
                spineOffset + Quaternion.AngleAxis(angle, axis) * shoulderOffset;
        }

        private void SetOrdinaryClip(
            BarBartenderClipKind kind,
            float elapsedSeconds)
        {
            requestedClipKind = kind;
            requestedClipTimeSeconds = Mathf.Max(0f, elapsedSeconds);
            if (kind != BarBartenderClipKind.Wipe) RequestTowelService(true);
            ResolveTowelSafeClip();
        }

        private void ResolveTowelSafeClip()
        {
            currentClipKind = IsTowelServiceReady
                ? requestedClipKind == BarBartenderClipKind.Wipe
                    ? BarBartenderClipKind.Notice : requestedClipKind
                : BarBartenderClipKind.Wipe;
            // A stored towel cannot wipe. Wait in the shared attentive pose
            // until the idle owner requests the physical draw at home.
            currentClipTimeSeconds = IsTowelServiceReady && requestedClipKind == BarBartenderClipKind.Wipe
                ? ResolveClipLength(BarBartenderClipKind.Notice) : requestedClipTimeSeconds;
        }

        private float ResolveClipLength(BarBartenderClipKind kind)
        {
            return registry.TryGetClip(kind, out AnimationClip clip, out _)
                ? clip.length
                : 0f;
        }

        private void EvaluateOrdinaryAnimation()
        {
            if (!hasGraph ||
                !registry.TryGetClip(
                    BarBartenderClipKind.Wipe,
                    out AnimationClip idleClip,
                    out _))
            {
                return;
            }

            defaultPlayable.SetTime(Mathf.Repeat(
                ordinaryIdleElapsedSeconds,
                Mathf.Max(0.0001f, idleClip.length)));
            if (currentClipKind == BarBartenderClipKind.Wipe)
            {
                mixer.SetInputWeight(0, 1f);
                mixer.SetInputWeight(1, 0f);
                graph.Evaluate(0f);
                return;
            }

            if (!registry.TryGetClip(
                    currentClipKind,
                    out AnimationClip clip,
                    out bool loop))
            {
                return;
            }

            EnsureOrdinaryActionPlayable(clip);
            float time = loop
                ? Mathf.Repeat(
                    currentClipTimeSeconds,
                    Mathf.Max(0.0001f, clip.length))
                : Mathf.Min(currentClipTimeSeconds, clip.length);
            actionPlayable.SetTime(time);
            mixer.SetInputWeight(0, 0f);
            mixer.SetInputWeight(1, 1f);
            graph.Evaluate(0f);
        }

        private void SolveOrdinaryReach(int index)
            => SolveOrdinaryReach(index, chainTargets[index], chainWeights[index]);

        private void SolveOrdinaryReach(int index, Vector3 target, float weight)
        {
            Transform shoulder = chainShoulders[index];
            Transform elbow = chainElbows[index];
            Transform hand = chainHands[index];
            Quaternion rotation = chainHasGripFrame[index]
                ? chainHandRotations[index]
                : hand.rotation;
            Vector3 wrist = target - rotation * handAttachments[index].SocketPositionInHand;
            ApplyClavicleReach(index, wrist, weight);
            Vector3 hint = shoulder.position +
                transform.right * (index == OrdinaryVesselHandIndex ? -0.35f : 0.35f) -
                transform.up * 0.25f;
            if (chainHasElbowDirection[index]) hint = shoulder.position + chainElbowDirections[index];
            LimbTwoBoneIk.Solve(shoulder, elbow, hand, wrist, rotation, hint,
                weight, float.PositiveInfinity, true);
        }

        private void ApplyClavicleReach(int index, Vector3 wrist, float weight)
        {
            Transform clavicle = chainClavicles[index];
            // A right-wrist roll can request a second solve in the same frame.
            // Always guide from this frame's sampled shoulder, never compound
            // the first solve's protraction on an unkeyed animation channel.
            clavicle.localRotation = authoredClavicleRotations[index];
            Transform shoulder = chainShoulders[index];
            float reach = LimbTwoBoneIk.ChainLength(
                shoulder, chainElbows[index], chainHands[index]) * 0.98f;
            if (Vector3.Distance(shoulder.position, wrist) <=
                reach + ClavicleReachThresholdMeters)
            {
                return;
            }

            Vector3 offset = shoulder.position - clavicle.position;
            Vector3 towardWrist = wrist - clavicle.position;
            Vector3 axis = Vector3.Cross(offset, towardWrist);
            if (axis.sqrMagnitude < 0.000001f)
                return;
            axis.Normalize();
            float low = 0f;
            float high = Mathf.Min(MaximumClavicleReachDegrees,
                Vector3.Angle(offset, towardWrist));
            for (int iteration = 0; iteration < 6; iteration++)
            {
                float middle = (low + high) * 0.5f;
                Vector3 guidedShoulder = clavicle.position +
                    Quaternion.AngleAxis(middle, axis) * offset;
                if (Vector3.Distance(guidedShoulder, wrist) <= reach)
                    high = middle;
                else
                    low = middle;
            }

            clavicle.rotation = Quaternion.AngleAxis(
                high * Mathf.Clamp01(weight), axis) * clavicle.rotation;
        }

        private void OnEnable()
        {
            if (Application.isPlaying &&
                isInitialized &&
                !hasGraph)
            {
                CreateOrdinaryGraph();
            }
        }

        private void OnDisable()
        {
            counterSupportGoalWeight = counterSupportWeight = 0f;
            RestoreAuthoredPose();
            TowelPresentation?.HoldDisabledPose();
            DestroyOrdinaryGraph();
            handPose?.ResetGrip();
        }

        private void OnDestroy()
        {
            counterSupportGoalWeight = counterSupportWeight = 0f;
            DestroyOrdinaryGraph();
        }

        private void CreateOrdinaryGraph()
        {
            if (hasGraph)
            {
                return;
            }

            if (!registry.TryGetClip(
                    BarBartenderClipKind.Wipe,
                    out AnimationClip idleClip,
                    out bool idleLoops) ||
                !idleLoops)
            {
                throw new InvalidOperationException(
                    "The ordinary bartender needs a looping Wipe clip.");
            }

            graph = PlayableGraph.Create("Bar Bartender Service");
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                defaultPlayable = AnimationClipPlayable.Create(
                    graph,
                    idleClip);
                defaultPlayable.SetApplyFootIK(false);
                defaultPlayable.SetApplyPlayableIK(false);
                defaultPlayable.SetSpeed(0d);
                mixer = AnimationMixerPlayable.Create(graph, 2);
                graph.Connect(defaultPlayable, 0, mixer, 0);
                mixer.SetInputWeight(0, 1f);
                mixer.SetInputWeight(1, 0f);
                AnimationPlayableOutput.Create(
                        graph,
                        "Bar Bartender Pose",
                        registry.Animator)
                    .SetSourcePlayable(mixer);
                graph.Play();
                graph.Evaluate(0f);
                hasGraph = true;
            }
            catch
            {
                if (graph.IsValid())
                {
                    graph.Destroy();
                }

                hasGraph = false;
                hasActionPlayable = false;
                throw;
            }
        }

        private void EnsureOrdinaryActionPlayable(AnimationClip clip)
        {
            if (hasActionPlayable && actionClip == clip)
            {
                return;
            }

            if (hasActionPlayable)
            {
                graph.Disconnect(mixer, 1);
                actionPlayable.Destroy();
                hasActionPlayable = false;
                actionClip = null;
            }

            actionPlayable = AnimationClipPlayable.Create(graph, clip);
            actionPlayable.SetApplyFootIK(false);
            actionPlayable.SetApplyPlayableIK(false);
            actionPlayable.SetSpeed(0d);
            graph.Connect(actionPlayable, 0, mixer, 1);
            actionClip = clip;
            hasActionPlayable = true;
        }

        private void DestroyOrdinaryGraph()
        {
            if (graph.IsValid())
            {
                graph.Destroy();
            }

            hasGraph = false;
            hasActionPlayable = false;
            actionClip = null;
        }

    }
}
