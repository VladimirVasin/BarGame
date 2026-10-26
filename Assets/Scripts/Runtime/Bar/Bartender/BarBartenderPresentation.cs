using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    /// <summary>One service sampler and two-hand contact pass over the default NPC rig.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(320)]
    public sealed partial class BarBartenderPresentation : MonoBehaviour
    {
        public const float ReachBlendSeconds = 0.28f;
        public const float BeerPlacementTorsoLeanDegrees = 38f;
        public const float BeerPlacementTorsoBlendSeconds = 0.18f;
        public const float MaximumCounterHipLeanDegrees = 20f;
        public const int OrdinaryHandCount = 2;
        public const int OrdinaryVesselHandIndex = 0;
        public const int OrdinaryBottleHandIndex = 1;
        public const float CounterHandSupportClearance = 0.002f;
        internal const float MaximumClavicleReachDegrees = 15f;
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
        private float counterSupportPalmRadius;
        private float freeRightSupportWeight;
        private Vector3 freeRightSupportTarget;
        private Vector3 lastWipeHandTarget;
        private bool menuHandActive;
        private bool counterMotionPose;
        private bool hasGraph;
        private bool hasActionPlayable;
        private bool isInitialized;
        private Vector3 authoredPelvisLocalPosition;
        private float lowReachLowering;
        private float lowReachLoweringGoal;

        public float LowReachLowering => lowReachLowering;

        /// <summary>A small knee bend for stationary low contacts; both authored feet stay planted.</summary>
        public void SetLowReachStance(float loweringMetres)
        {
            if (!float.IsFinite(loweringMetres)) return;
            lowReachLoweringGoal = Mathf.Clamp(loweringMetres, 0f, .12f);
        }

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
        public float FreeRightHandSupportWeight => freeRightSupportWeight;
        public Vector3 FreeRightHandSupportTarget => freeRightSupportTarget;
        public Vector3 LastWipeHandTarget => lastWipeHandTarget;
        public float WipeHandTargetError => isInitialized
            ? Vector3.Distance(registry.VesselGripAnchor.position, lastWipeHandTarget) : float.PositiveInfinity;
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

        public void RequestTowelService(bool serviceRequested) => TowelPresentation?.RequestService(
            serviceRequested || (registry != null && !registry.HasSafeWipePatch));
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
                CacheWipeStrokeBounds();
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
            if (chainIndex == OrdinaryVesselHandIndex) menuHandActive = false;
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

        public void SetMenuHandPose(Transform menuGrip, bool carried)
        {
            if (!isInitialized || menuGrip == null) return;
            SelectGripKind(OrdinaryVesselHandIndex, NpcHandGripKind.Cylindrical);
            AnatomicalHandFrame frame = anatomicalHands[OrdinaryVesselHandIndex];
            Vector3 target = menuGrip.position;
            Quaternion rotation = frame.ResolveHandRotation(menuGrip.up, -menuGrip.forward);
            if (carried)
            {
                float reach = LimbTwoBoneIk.ChainLength(registry.LeftUpperArm, registry.LeftForearm, registry.LeftHand);
                target = registry.LeftUpperArm.position + transform.forward * (reach * .50f) -
                    transform.up * (reach * .48f) + transform.right *
                    (Vector3.Distance(registry.LeftUpperArm.position, registry.RightUpperArm.position) * .35f);
                if (registry.TryGetCounterSurfaceHeight(out float top))
                    target.y = Mathf.Max(target.y, top + .10f);
                rotation = frame.ResolveHandRotation(transform.up, transform.forward);
            }
            SetChainTarget(OrdinaryVesselHandIndex, target, 1f);
            chainHandRotations[OrdinaryVesselHandIndex] = rotation;
            chainHasGripFrame[OrdinaryVesselHandIndex] = true;
            SetChainElbowDirection(OrdinaryVesselHandIndex, -transform.right * .35f - transform.up * .15f);
            SetCounterReachLean(target, carried ? 0f : 1f);
            menuHandActive = true;
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
            Bounds palmBounds = default;
            bool hasPalmBounds = false;
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
                        if (!hasPalmBounds) { palmBounds = new Bounds(point, Vector3.zero); hasPalmBounds = true; }
                        else palmBounds.Encapsulate(point);
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
            counterSupportPalmRadius = palmBounds.extents.magnitude;
        }

        private void CacheWipeStrokeBounds()
        {
            if (!hasGraph || registry.HasWipeStrokeBounds ||
                !registry.TryGetClip(BarBartenderClipKind.Wipe, out AnimationClip clip, out _)) return;
            Bounds localStroke = default;
            var shoulderFromClavicle = new Vector3[33];
            var wristFromClavicle = new Vector3[33];
            bool found = false;
            // One bounded initialization pass over the real rig/cloth. The
            // conservative envelope lets menu avoidance move a complete
            // stroke without changing its direction midway through a wipe.
            for (int sample = 0; sample <= 32; sample++)
            {
                RestoreAuthoredPose();
                defaultPlayable.SetTime(clip.length * sample / 32d);
                mixer.SetInputWeight(0, 1f);
                mixer.SetInputWeight(1, 0f);
                graph.Evaluate(0f);
                CaptureAuthoredPose();
                shoulderFromClavicle[sample] = transform.InverseTransformVector(
                    registry.LeftUpperArm.position - registry.LeftClavicle.position);
                ApplyWipeSurfaceContact();
                wristFromClavicle[sample] = transform.InverseTransformVector(
                    registry.LeftHand.position - registry.LeftClavicle.position);
                Bounds towel = registry.TowelWipeWorldBounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 signs = new Vector3((corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                    Vector3 point = transform.InverseTransformPoint(towel.center + Vector3.Scale(towel.extents, signs));
                    if (!found) { localStroke = new Bounds(point, Vector3.zero); found = true; }
                    else localStroke.Encapsulate(point);
                }
            }
            localStroke.Expand(.02f);
            registry.CacheWipeStrokeBounds(localStroke, shoulderFromClavicle, wristFromClavicle);
            RestoreAuthoredPose();
            EvaluateOrdinaryAnimation();
            CaptureAuthoredPose();
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
            counterMotionPose = true;
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
            counterMotionPose = true;
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
            if (!registry.HasSafeWipePatch) RequestTowelService(true);
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
            lowReachLowering = Mathf.MoveTowards(lowReachLowering, lowReachLoweringGoal,
                Mathf.Max(0f, deltaTime) * .4f);
            ApplyLowReachStance();
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
            }
            ApplyWipeSurfaceContact();
            ApplyFreeRightHandSupport(deltaTime);
            for (int index = 0; index < ChainCount; index++)
            {
                if (chainWeights[index] > 0.0001f)
                {
                    SolveOrdinaryReach(index);
                }
            }
            ApplyCounterHandSupport();
            handPose?.SetGrip(true, chainWeights[OrdinaryVesselHandIndex] * (menuHandActive ? .25f : 1f),
                chainGripKinds[OrdinaryVesselHandIndex]);
            handPose?.SetGrip(false, chainWeights[OrdinaryBottleHandIndex] * (1f - counterSupportWeight),
                chainGripKinds[OrdinaryBottleHandIndex]);
            TowelPresentation.Advance(deltaTime, new Pose(registry.LeftHand.position, registry.LeftHand.rotation));
        }

        private void ApplyCounterHandSupport()
        {
            if (counterSupportWeight <= 0.0001f) return;
            if (registry.HasCounterSurface)
            {
                if (!registry.TryResolveCounterSupportTarget(counterSupportSurfaceTarget,
                    counterSupportPalmRadius, out Vector3 surface))
                { counterSupportWeight = 0f; return; }
                counterSupportSurfaceTarget = surface;
            }
            ApplyRightPalmSupport(CounterHandSupportTarget, counterSupportWeight);
        }

        private void ApplyFreeRightHandSupport(float deltaTime)
        {
            bool free = currentClipKind != BarBartenderClipKind.Walk &&
                currentClipKind != BarBartenderClipKind.Pour &&
                !counterMotionPose &&
                !registry.RightHandCarriesBottle &&
                chainGoalWeights[OrdinaryBottleHandIndex] <= .0001f &&
                counterSupportGoalWeight <= .0001f;
            float strokeAmount = .5f;
            if (currentClipKind == BarBartenderClipKind.Wipe && !IsTowelTransitioning &&
                registry.HasWipeStrokeBounds)
            {
                Bounds stroke = registry.WipeStrokeWorldBounds;
                Bounds cloth = registry.TowelWipeWorldBounds;
                Vector3 axis = transform.right;
                Vector3 absoluteAxis = new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
                float halfTravel = Mathf.Max(.001f,
                    Vector3.Dot(stroke.extents - cloth.extents, absoluteAxis));
                strokeAmount = Mathf.Clamp01(.5f + Vector3.Dot(cloth.center - stroke.center, axis) / (halfTravel * 2f));
            }
            // The free hand braces each end of the cloth's stroke and lifts
            // slightly while repositioning with it. This is the bar's work
            // action; the shared cafe's fixed bottle-carry arm stays intact.
            Vector3 desired = registry.RightUpperArm.position + transform.right * Mathf.Lerp(-.045f, .045f, strokeAmount);
            bool hasSurface = registry.TryResolveCounterSupportTarget(desired,
                counterSupportPalmRadius, out Vector3 surface);
            float lift = currentClipKind == BarBartenderClipKind.Wipe && !IsTowelTransitioning
                ? .025f * 4f * strokeAmount * (1f - strokeAmount) : 0f;
            Quaternion rotation = counterSupportHandFrame.ResolveHandRotation(-transform.up, transform.forward);
            float noticeRelease = 0f;
            if (currentClipKind == BarBartenderClipKind.Notice)
            {
                float phase = Mathf.Clamp01(currentClipTimeSeconds /
                    Mathf.Max(.0001f, ResolveClipLength(BarBartenderClipKind.Notice)));
                noticeRelease = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, .24f, phase)) *
                    (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.68f, 1f, phase)));
                // Release the brace to attend the guest, then return the
                // open hand to wood; the left hand remains free for the menu.
                rotation = Quaternion.Slerp(rotation, counterSupportHandFrame.ResolveHandRotation(
                    -transform.up - transform.forward, transform.forward), noticeRelease);
            }
            Vector3 target = surface + transform.up *
                (CounterHandSupportClearance + lift + noticeRelease * .10f) - transform.forward * (noticeRelease * .035f);
            Vector3 wrist = target - rotation * counterSupportContactInHand;
            float reach = LimbTwoBoneIk.ChainLength(registry.RightUpperArm, registry.RightForearm, registry.RightHand);
            free &= hasSurface && Vector3.Distance(registry.RightUpperArm.position, wrist) <= reach * 1.03f;
            freeRightSupportWeight = Mathf.MoveTowards(freeRightSupportWeight, free ? 1f : 0f,
                Mathf.Max(0f, deltaTime) / ReachBlendSeconds);
            // A visible carried bottle owns the hand even during a carry
            // phase whose reach weight is zero. Assigned contacts win below.
            if (!hasSurface) { freeRightSupportWeight = 0f; return; }
            if (registry.RightHandCarriesBottle || freeRightSupportWeight <= .0001f) return;
            freeRightSupportTarget = target;
            float weight = freeRightSupportWeight * (1f - Mathf.Max(
                chainWeights[OrdinaryBottleHandIndex], counterSupportWeight));
            ApplyRightPalmSupport(target, weight, rotation);
        }

        private void ApplyRightPalmSupport(Vector3 target, float weight)
            => ApplyRightPalmSupport(target, weight,
                counterSupportHandFrame.ResolveHandRotation(-transform.up, transform.forward));

        private void ApplyRightPalmSupport(Vector3 target, float weight, Quaternion rotation)
        {
            if (weight <= .0001f) return;
            Vector3 wrist = target - rotation * counterSupportContactInHand;
            // Preserve the already blended ordinary right arm at entry and
            // release. Its clavicle assistance is blended from that same pose.
            Quaternion beforeClavicle = registry.RightClavicle.localRotation;
            ApplyClavicleReach(OrdinaryBottleHandIndex, wrist, 1f);
            registry.RightClavicle.localRotation = Quaternion.Slerp(beforeClavicle,
                registry.RightClavicle.localRotation, weight);
            Vector3 hint = registry.RightUpperArm.position + transform.right * 0.35f - transform.up * 0.25f;
            LimbTwoBoneIk.Solve(registry.RightUpperArm, registry.RightForearm, registry.RightHand,
                wrist, rotation, hint, weight, float.PositiveInfinity, true);
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
            if (registry.Pelvis != null) registry.Pelvis.localPosition = authoredPelvisLocalPosition;
        }

        private void CaptureAuthoredPose()
        {
            authoredPelvisLocalPosition = registry.Pelvis.localPosition;
            for (int index = 0; index < proceduralBones.Length; index++)
                authoredRotations[index] = proceduralBones[index].localRotation;
            for (int index = 0; index < chainClavicles.Length; index++)
                authoredClavicleRotations[index] = chainClavicles[index].localRotation;
        }

        private void ApplyLowReachStance()
        {
            if (lowReachLowering <= .0001f) return;
            Pose left = new Pose(registry.LeftFoot.position, registry.LeftFoot.rotation);
            Pose right = new Pose(registry.RightFoot.position, registry.RightFoot.rotation);
            registry.Pelvis.position -= transform.up * lowReachLowering;
            SolvePlantedLeg(registry.LeftThigh, registry.LeftShin, registry.LeftFoot, left);
            SolvePlantedLeg(registry.RightThigh, registry.RightShin, registry.RightFoot, right);
        }

        private void ApplyWipeSurfaceContact()
        {
            if (currentClipKind != BarBartenderClipKind.Wipe ||
                chainWeights[OrdinaryVesselHandIndex] > 0.0001f)
                return;
            // Keep the towel in its authored hand mount and move that hand,
            // rather than separating the cloth from the default character.
            Quaternion handRotation = registry.LeftHand.rotation;
            if (!registry.TryResolveWipeHandTarget(out Vector3 target, true))
            { lastWipeHandTarget = registry.VesselGripAnchor.position; return; }
            lastWipeHandTarget = target;
            for (int iteration = 0; iteration < 3; iteration++)
            {
                // Keep the original translated target through polishing;
                // never accept a shortened solve as the next X/Z goal.
                SolveOrdinaryReach(OrdinaryVesselHandIndex, target, 1f);
                registry.LeftHand.rotation = handRotation;
                if (Vector3.Distance(registry.VesselGripAnchor.position, target) < .0005f) break;
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
            Quaternion pelvisBase = registry.Pelvis.rotation;
            Quaternion spineBase = registry.Spine.rotation;
            Quaternion hipBend = Quaternion.AngleAxis(
                Mathf.Min(angle, MaximumCounterHipLeanDegrees), axis.normalized);
            registry.Pelvis.rotation = hipBend * pelvisBase;
            registry.Spine.rotation = Quaternion.AngleAxis(angle, axis.normalized) * spineBase;
            SolvePlantedLeg(registry.LeftThigh, registry.LeftShin, registry.LeftFoot, leftContact);
            SolvePlantedLeg(registry.RightThigh, registry.RightShin, registry.RightFoot, rightContact);
        }

        private void SolvePlantedLeg(Transform thigh, Transform shin, Transform foot, Pose contact)
        {
            // A knee's previous world point can fall behind its hip-to-foot
            // line when the pelvis lowers or hinges. Build the pole from the
            // moved hip and the actor's anatomical forward instead; a nearly
            // straight or asymmetric sampled leg must not choose its side.
            Vector3 legAxis = contact.position - thigh.position;
            Vector3 kneeDirection = Vector3.ProjectOnPlane(transform.forward, legAxis);
            if (kneeDirection.sqrMagnitude < .000001f)
                kneeDirection = Vector3.ProjectOnPlane(transform.up, legAxis);
            Vector3 hint = thigh.position + kneeDirection.normalized;
            LimbTwoBoneIk.Solve(thigh, shin, foot, contact.position, contact.rotation, hint);
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
            counterMotionPose = false;
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
                CacheWipeStrokeBounds();
            }
        }

        private void OnDisable()
        {
            orderAcknowledgement?.Clear();
            counterSupportGoalWeight = counterSupportWeight = 0f;
            freeRightSupportWeight = 0f;
            RestoreAuthoredPose();
            TowelPresentation?.HoldDisabledPose();
            DestroyOrdinaryGraph();
            handPose?.ResetGrip();
        }

        private void OnDestroy()
        {
            counterSupportGoalWeight = counterSupportWeight = 0f;
            freeRightSupportWeight = 0f;
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
