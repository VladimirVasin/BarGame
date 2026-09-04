using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    /// <summary>
    /// Presentation shared by the active ordinary bartender and the retained
    /// six-armed legacy asset. The ordinary path plays authored service and
    /// locomotion clips on the common NpcHumanV2 Avatar, then applies a light
    /// two-hand contact pass toward the independently driven bar service props.
    /// The legacy
    /// path keeps its original four procedural chains so that prefab remains
    /// inspectable without ever being selected by the runtime provider.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(320)]
    public sealed class BarBartenderPresentation : MonoBehaviour
    {
        // Broad enough to read across the dark hall at PS1
        // resolution: the first pass at 4.5 degrees was invisible.
        public const float IdleSwayDegrees = 10f;
        public const float ElbowSwayDegrees = 7f;
        public const float HeadSwayDegrees = 9f;
        public const float ReachBlendSeconds = 0.28f;
        public const int OrdinaryHandCount = 2;
        public const int OrdinaryVesselHandIndex = 0;
        public const int OrdinaryBottleHandIndex = 1;
        private const int SolveIterations = 3;

        // Root-local rest points retained for the inspectable legacy
        // six-arm path. The active ordinary bartender stays ground-level and
        // uses the shared authored human clips. World-space aiming is
        // deliberate — imported FBX bone axes are not trustworthy for local
        // Euler folds.
        private static readonly Vector3 LeftHandRestLocal =
            new Vector3(0.27f, 1.16f, 0.42f);
        private static readonly Vector3 RightHandRestLocal =
            new Vector3(-0.27f, 1.16f, 0.42f);

        private readonly float[] chainPhases =
        {
            0.00f, 1.73f, 3.31f, 4.87f
        };
        private readonly float[] chainSpeeds =
        {
            0.53f, 0.41f, 0.61f, 0.47f
        };

        private BarBartenderAssetRegistry registry;
        private Transform[] chainShoulders;
        private Transform[] chainElbows;
        private Transform[] chainWrists;
        private Transform[] chainGrips;
        private Quaternion[] shoulderRestRotations;
        private Quaternion[] elbowRestRotations;
        private Quaternion[] wristRestRotations;
        private Vector3[] chainTargets;
        private float[] chainWeights;
        private float[] chainGoalWeights;
        private Quaternion leftUpperRest;
        private Quaternion leftForearmRest;
        private Quaternion rightUpperRest;
        private Quaternion rightForearmRest;
        private Quaternion headRest;
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private AnimationClipPlayable defaultPlayable;
        private AnimationClipPlayable actionPlayable;
        private AnimationClip actionClip;
        private BarBartenderClipKind currentClipKind =
            BarBartenderClipKind.Wipe;
        private float currentClipTimeSeconds;
        private float ordinaryIdleElapsedSeconds;
        private bool hasGraph;
        private bool hasActionPlayable;
        private bool usesOrdinaryRig;
        private float elapsed;
        private bool isInitialized;

        public bool IsInitialized => isInitialized;
        public BarBartenderAssetRegistry Registry => registry;
        public bool UsesOrdinaryRig => usesOrdinaryRig;
        public BarBartenderClipKind CurrentClipKind => currentClipKind;
        public float CurrentClipTimeSeconds => currentClipTimeSeconds;
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
            if (registry.Chest == null ||
                registry.Head == null ||
                registry.LeftUpperArm == null ||
                registry.RightUpperArm == null)
            {
                throw new InvalidOperationException(
                    "The bartender registry lacks its " +
                    "canonical bones.");
            }

            usesOrdinaryRig = registry.UsesAuthoredServiceClips;
            if (usesOrdinaryRig)
            {
                InitializeOrdinaryRig();
            }
            else
            {
                InitializeLegacyRig();
            }
            chainTargets = new Vector3[ChainCount];
            chainWeights = new float[ChainCount];
            chainGoalWeights = new float[ChainCount];
            isInitialized = true;
            if (usesOrdinaryRig && Application.isPlaying)
            {
                CreateOrdinaryGraph();
            }
            Advance(0f);
        }

        private void InitializeOrdinaryRig()
        {
            if (registry.ExtraArmChains.Count != 0 ||
                registry.Animator == null ||
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
            chainElbows = new[]
            {
                registry.LeftForearm,
                registry.RightForearm
            };
            chainWrists = new[]
            {
                registry.LeftHand,
                registry.RightHand
            };
            chainGrips = new[]
            {
                registry.VesselGripAnchor,
                registry.BottleGripAnchor
            };
            registry.Animator.enabled = true;
            registry.SetServiceTowelVisible(true);
        }

        private void InitializeLegacyRig()
        {
            if (registry.ExtraArmChains.Count !=
                BarBartenderAssetRegistry.ExtraArmChainCount)
            {
                throw new InvalidOperationException(
                    "The legacy bartender registry lacks its four " +
                    "extra-arm chains.");
            }

            // The imported Animator carries the shared Avatar but no
            // controller; nothing may fight the retained procedural pose.
            if (registry.Animator != null)
            {
                registry.Animator.enabled = false;
            }

            ReparentChains();
            CaptureRestPose();
        }

        /// <summary>
        /// Points one service hand at a world position. On the active model
        /// indices 0/1 are the ordinary left-vessel/right-bottle hands; on
        /// the retained legacy model they are its original extra-arm chains.
        /// </summary>
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

            chainTargets[chainIndex] = worldPosition;
            chainGoalWeights[chainIndex] = Mathf.Clamp01(weight);
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
            if (!isInitialized || !usesOrdinaryRig)
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

            bool leftHandServing =
                leftHandCarriesMenu ||
                frame.Phase ==
                    BarDrinkServicePhase.VesselPlacement ||
                frame.Phase == BarDrinkServicePhase.Pouring ||
                frame.Phase == BarDrinkServicePhase.BottleReturn ||
                frame.Phase == BarDrinkServicePhase.BeerGlassPickup ||
                frame.Phase == BarDrinkServicePhase.BeerPouring ||
                frame.Phase == BarDrinkServicePhase.BeerCarryToGuest ||
                frame.Phase == BarDrinkServicePhase.BeerGlassPlacement;
            registry.SetServiceTowelVisible(!leftHandServing);
        }

        public void ResetServicePose()
        {
            if (!isInitialized || !usesOrdinaryRig)
            {
                return;
            }

            SetOrdinaryClip(
                BarBartenderClipKind.Wipe,
                ordinaryIdleElapsedSeconds);
            registry.SetServiceTowelVisible(true);
        }

        public void ApplyCounterTravelPose(
            float elapsedSeconds,
            bool leftHandCarriesMenu = false)
        {
            if (!isInitialized || !usesOrdinaryRig)
            {
                return;
            }

            SetOrdinaryClip(
                BarBartenderClipKind.Walk,
                Mathf.Max(0f, elapsedSeconds));
            registry.SetServiceTowelVisible(!leftHandCarriesMenu);
        }

        public void ApplyCounterTurnPose(
            float elapsedSeconds,
            bool leftHandCarriesMenu = false)
        {
            if (!isInitialized || !usesOrdinaryRig)
            {
                return;
            }

            SetOrdinaryClip(
                BarBartenderClipKind.ServiceStep,
                Mathf.Max(0f, elapsedSeconds));
            registry.SetServiceTowelVisible(!leftHandCarriesMenu);
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

            elapsed += Mathf.Max(0f, deltaTime);
            if (usesOrdinaryRig)
            {
                AdvanceOrdinary(deltaTime);
                return;
            }

            ApplyCanonicalRest();
            ApplyHeadSway();
            for (int index = 0; index < ChainCount; index++)
            {
                float goal = chainGoalWeights[index];
                chainWeights[index] = Mathf.MoveTowards(
                    chainWeights[index],
                    goal,
                    deltaTime <= 0f
                        ? 0f
                        : deltaTime / ReachBlendSeconds);
                ApplyChainIdle(index);
                if (chainWeights[index] > 0.0001f)
                {
                    SolveChainReach(index);
                }
            }
        }

        private void LateUpdate()
        {
            Advance(Time.deltaTime);
        }

        private void AdvanceOrdinary(float deltaTime)
        {
            ordinaryIdleElapsedSeconds += Mathf.Max(0f, deltaTime);
            if (currentClipKind == BarBartenderClipKind.Wipe)
            {
                currentClipTimeSeconds = ordinaryIdleElapsedSeconds;
            }

            if (Application.isPlaying && !hasGraph)
            {
                CreateOrdinaryGraph();
            }

            EvaluateOrdinaryAnimation();
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
        }

        private void SetOrdinaryClip(
            BarBartenderClipKind kind,
            float elapsedSeconds)
        {
            currentClipKind = kind;
            currentClipTimeSeconds = Mathf.Max(0f, elapsedSeconds);
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
        {
            Transform shoulder = chainShoulders[index];
            Transform elbow = chainElbows[index];
            Transform grip = chainGrips[index];
            Quaternion shoulderBase = shoulder.rotation;
            Quaternion elbowBase = elbow.rotation;
            for (int iteration = 0;
                 iteration < SolveIterations;
                 iteration++)
            {
                RotateTowards(elbow, grip, chainTargets[index]);
                RotateTowards(shoulder, grip, chainTargets[index]);
            }

            shoulder.rotation = Quaternion.Slerp(
                shoulderBase,
                shoulder.rotation,
                chainWeights[index]);
            elbow.rotation = Quaternion.Slerp(
                elbowBase,
                elbow.rotation,
                chainWeights[index]);
        }

        /// <summary>
        /// The wheelchair/cashier mechanism: segments bind to the
        /// static root in the FBX so Unity imports plain renderers;
        /// at runtime each segment re-parents under its pivot and the
        /// pivots chain shoulder → elbow → wrist → grip beneath the
        /// chest bone, so the whole fan rides the torso.
        /// </summary>
        private void ReparentChains()
        {
            int count = registry.ExtraArmChains.Count;
            chainShoulders = new Transform[count];
            chainElbows = new Transform[count];
            chainWrists = new Transform[count];
            chainGrips = new Transform[count];
            Dictionary<string, Transform> segments =
                IndexSegments();
            for (int index = 0; index < count; index++)
            {
                BarBartenderArmChain chain =
                    registry.ExtraArmChains[index];
                chainShoulders[index] = chain.ShoulderPivot;
                chainElbows[index] = chain.ElbowPivot;
                chainWrists[index] = chain.WristPivot;
                chainGrips[index] = chain.GripPivot;

                chain.ShoulderPivot.SetParent(registry.Chest, true);
                chain.ElbowPivot.SetParent(chain.ShoulderPivot, true);
                chain.WristPivot.SetParent(chain.ElbowPivot, true);
                chain.GripPivot.SetParent(chain.WristPivot, true);

                AttachSegment(
                    segments,
                    $"ARM{chain.ChainId.Substring(3, 1)}_Upper." +
                    chain.ChainId.Substring(5, 1),
                    chain.ShoulderPivot);
                AttachSegment(
                    segments,
                    $"ARM{chain.ChainId.Substring(3, 1)}_Fore." +
                    chain.ChainId.Substring(5, 1),
                    chain.ElbowPivot);
                AttachSegment(
                    segments,
                    $"ARM{chain.ChainId.Substring(3, 1)}_Hand." +
                    chain.ChainId.Substring(5, 1),
                    chain.WristPivot);
            }

            AttachSegment(
                segments,
                "ARM2_BrassBand.R",
                registry.ExtraArmChains[1].ElbowPivot);
        }

        private Dictionary<string, Transform> IndexSegments()
        {
            var segments = new Dictionary<string, Transform>(
                StringComparer.Ordinal);
            IReadOnlyList<BarBartenderRendererBinding> bindings =
                registry.RendererBindings;
            for (int index = 0; index < bindings.Count; index++)
            {
                BarBartenderRendererBinding binding = bindings[index];
                if (binding?.Renderer != null)
                {
                    segments[binding.RendererName] =
                        binding.Renderer.transform;
                }
            }

            return segments;
        }

        private static void AttachSegment(
            IReadOnlyDictionary<string, Transform> segments,
            string segmentName,
            Transform pivot)
        {
            if (!segments.TryGetValue(
                    segmentName,
                    out Transform segment))
            {
                throw new InvalidOperationException(
                    $"The bartender model lost segment " +
                    $"'{segmentName}'.");
            }

            segment.SetParent(pivot, true);
        }

        private void CaptureRestPose()
        {
            int count = registry.ExtraArmChains.Count;
            shoulderRestRotations = new Quaternion[count];
            elbowRestRotations = new Quaternion[count];
            wristRestRotations = new Quaternion[count];
            for (int index = 0; index < count; index++)
            {
                shoulderRestRotations[index] =
                    chainShoulders[index].localRotation;
                elbowRestRotations[index] =
                    chainElbows[index].localRotation;
                wristRestRotations[index] =
                    chainWrists[index].localRotation;
            }

            leftUpperRest = registry.LeftUpperArm.localRotation;
            leftForearmRest = registry.LeftForearm.localRotation;
            rightUpperRest = registry.RightUpperArm.localRotation;
            rightForearmRest = registry.RightForearm.localRotation;
            headRest = registry.Head.localRotation;
        }

        /// <summary>
        /// Folds the canonical pair from the imported A-pose to a
        /// braced counter rest with the slowest sway of the three
        /// pairs — solved in world space against root-local rest
        /// points, the axis-safe way to pose imported FBX bones.
        /// </summary>
        private void ApplyCanonicalRest()
        {
            registry.LeftUpperArm.localRotation = leftUpperRest;
            registry.LeftForearm.localRotation = leftForearmRest;
            registry.RightUpperArm.localRotation = rightUpperRest;
            registry.RightForearm.localRotation = rightForearmRest;

            float sway = Mathf.Sin(elapsed * 0.37f) * 0.035f;
            Vector3 leftTarget = transform.TransformPoint(
                LeftHandRestLocal + new Vector3(0f, sway, 0f));
            Vector3 rightTarget = transform.TransformPoint(
                RightHandRestLocal - new Vector3(0f, sway, 0f));
            FoldCanonicalArm(
                registry.LeftUpperArm,
                registry.LeftForearm,
                registry.LeftHand,
                leftTarget);
            FoldCanonicalArm(
                registry.RightUpperArm,
                registry.RightForearm,
                registry.RightHand,
                rightTarget);
        }

        private static void FoldCanonicalArm(
            Transform upperArm,
            Transform forearm,
            Transform hand,
            Vector3 target)
        {
            for (int iteration = 0;
                 iteration < SolveIterations;
                 iteration++)
            {
                RotateTowards(forearm, hand, target);
                RotateTowards(upperArm, hand, target);
            }
        }

        private void ApplyHeadSway()
        {
            float yaw = Mathf.Sin(elapsed * 0.21f) * HeadSwayDegrees;
            float pitch = Mathf.Sin(elapsed * 0.13f + 1.1f) * 1.8f;
            registry.Head.localRotation =
                headRest * Quaternion.Euler(pitch, yaw, 0f);
        }

        /// <summary>
        /// Each free chain does quiet desynchronized business: its
        /// own phase, its own speed, small amplitudes — polishing,
        /// drumming, bracing, without literal props.
        /// </summary>
        private void ApplyChainIdle(int index)
        {
            float phase = elapsed * chainSpeeds[index] +
                chainPhases[index];
            float shoulderSwing =
                Mathf.Sin(phase) * IdleSwayDegrees;
            float shoulderLift =
                Mathf.Sin(phase * 0.7f + 0.6f) * 2.6f;
            float elbowSwing =
                Mathf.Sin(phase * 1.3f + 2.1f) * ElbowSwayDegrees;
            chainShoulders[index].localRotation =
                shoulderRestRotations[index] *
                Quaternion.Euler(
                    shoulderLift,
                    shoulderSwing,
                    shoulderSwing * 0.4f);
            chainElbows[index].localRotation =
                elbowRestRotations[index] *
                Quaternion.Euler(
                    elbowSwing,
                    0f,
                    elbowSwing * 0.5f);
            chainWrists[index].localRotation =
                wristRestRotations[index];
        }

        /// <summary>
        /// CCD toward the chain target: shoulder and elbow pivots
        /// steer the grip pivot, then slerp back by the eased weight —
        /// the shared procedural-arm idiom.
        /// </summary>
        private void SolveChainReach(int index)
        {
            float weight = chainWeights[index];
            Vector3 target = chainTargets[index];
            Transform shoulder = chainShoulders[index];
            Transform elbow = chainElbows[index];
            Transform grip = chainGrips[index];

            Quaternion shoulderBase = shoulder.rotation;
            Quaternion elbowBase = elbow.rotation;
            for (int iteration = 0;
                 iteration < SolveIterations;
                 iteration++)
            {
                RotateTowards(elbow, grip, target);
                RotateTowards(shoulder, grip, target);
            }

            shoulder.rotation = Quaternion.Slerp(
                shoulderBase,
                shoulder.rotation,
                weight);
            elbow.rotation = Quaternion.Slerp(
                elbowBase,
                elbow.rotation,
                weight);
        }

        private void OnEnable()
        {
            if (Application.isPlaying &&
                isInitialized &&
                usesOrdinaryRig &&
                !hasGraph)
            {
                CreateOrdinaryGraph();
            }
        }

        private void OnDisable()
        {
            DestroyOrdinaryGraph();
            registry?.SetServiceTowelVisible(true);
        }

        private void OnDestroy()
        {
            DestroyOrdinaryGraph();
        }

        private void CreateOrdinaryGraph()
        {
            if (hasGraph || !usesOrdinaryRig)
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

        private static void RotateTowards(
            Transform joint,
            Transform end,
            Vector3 target)
        {
            Vector3 toEnd = end.position - joint.position;
            Vector3 toTarget = target - joint.position;
            if (toEnd.sqrMagnitude < 0.000001f ||
                toTarget.sqrMagnitude < 0.000001f)
            {
                return;
            }

            joint.rotation = Quaternion.FromToRotation(
                    toEnd,
                    toTarget) *
                joint.rotation;
        }
    }
}
