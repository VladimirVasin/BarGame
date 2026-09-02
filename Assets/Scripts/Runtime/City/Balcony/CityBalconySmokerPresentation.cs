using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    /// <summary>
    /// A static, passive roaming-pedestrian presentation sampling the
    /// production Hero V2 SmokeLoop through a manual graph. Its clock mirrors
    /// the player's authored frame holds instead of letting the four-second
    /// clip run free.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityBalconySmokerPresentation : MonoBehaviour
    {
        public const string SmokeLoopClipName = "SmokeLoop";
        public const float MaximumStepSeconds = 0.1f;
        public const string MouthSocketName = "SOCKET_Mouth";
        private const int ExpectedPoseBoneCount = 31;
        private const float RestPositionTolerance = 0.0001f;
        private const float RestAngleTolerance = 0.01f;
        private static readonly string[] PoseBoneNames =
        {
            "root",
            "pelvis",
            "spine",
            "chest",
            "neck",
            "head",
            "face.eye.L",
            "face.eye.R",
            "face.brow.L",
            "face.brow.R",
            "face.mouth",
            MouthSocketName,
            "clavicle.L",
            "upper_arm.L",
            "forearm.L",
            "hand.L",
            "SOCKET_Grip.L",
            "SOCKET_Vessel.L",
            "clavicle.R",
            "upper_arm.R",
            "forearm.R",
            CityBalconySmokerAccessory.RightHandBoneName,
            "SOCKET_Grip.R",
            CityBalconySmokerAccessory.CigaretteSocketName,
            "SOCKET_Bottle.R",
            "thigh.L",
            "shin.L",
            "foot.L",
            "thigh.R",
            "shin.R",
            "foot.R"
        };

        private PlayableGraph graph;
        private AnimationClipPlayable playable;
        private CityPedestrianAssetRegistry registry;
        private AnimationClip activeClip;
        private PlayerAnimatedInteractionDefinition animationDefinition;
        private HomeBalconySmokingExhaleEffect exhaleEffect;
        private FootGroundingProbe footGroundingProbe;
        private Player3DAssetRegistry poseDriverRegistry;
        private GameObject poseDriverRoot;
        private PoseBoneBinding[] poseBoneBindings =
            Array.Empty<PoseBoneBinding>();
        private Transform mouthSocket;
        private Vector3 modelBaseLocalPosition;
        private double loopTimeSeconds;
        private double burstTimeSeconds;
        private IReadOnlyList<Renderer> cigaretteRenderers =
            Array.Empty<Renderer>();

        public bool IsInitialized { get; private set; }
        public CityBalconySmokerDescriptor Descriptor { get; private set; }
        public CityPedestrianAssetRegistry Registry => registry;
        public AnimationClip ActiveClip => activeClip;
        public PlayerAnimatedInteractionDefinition AnimationDefinition =>
            animationDefinition;
        public HomeBalconySmokingExhaleEffect ExhaleEffect => exhaleEffect;
        public IReadOnlyList<Renderer> CigaretteRenderers =>
            cigaretteRenderers;
        public double LoopTimeSeconds => loopTimeSeconds;
        public int CurrentLoopFrame { get; private set; }
        public float CurrentClipProgress01 { get; private set; }
        public int SmokeBurstCount => exhaleEffect != null
            ? exhaleEffect.ManualBurstCount
            : 0;

        internal void Initialize(
            CityPedestrianAssetRegistry assetRegistry,
            CityBalconySmokerDescriptor descriptor,
            AnimationClip smokeLoop,
            Avatar heroAvatar,
            PlayerAnimatedInteractionDefinition definition,
            bool poseIsParentLocal)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The balcony smoker presentation is already " +
                    "initialized.");
            }

            registry = assetRegistry != null
                ? assetRegistry
                : throw new ArgumentNullException(nameof(assetRegistry));
            activeClip = smokeLoop != null
                ? smokeLoop
                : throw new ArgumentNullException(nameof(smokeLoop));
            animationDefinition = definition ??
                throw new ArgumentNullException(nameof(definition));
            if (registry.Animator == null || registry.ModelRoot == null)
            {
                throw new InvalidOperationException(
                    "A balcony smoker requires an Animator and model root.");
            }

            if (!string.Equals(
                    registry.DesignId,
                    descriptor.ArchetypeDesignId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The instantiated roaming archetype does not match the " +
                    "balcony-smoker descriptor.");
            }

            if (heroAvatar == null ||
                registry.Animator.avatar != heroAvatar)
            {
                throw new InvalidOperationException(
                    "The balcony smoker must share Hero V2's Generic Avatar " +
                    "before it can sample the literal SmokeLoop clip.");
            }

            if (!string.Equals(
                    animationDefinition.LoopClipName,
                    SmokeLoopClipName,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The balcony smoker definition must target SmokeLoop.",
                    nameof(definition));
            }

            Descriptor = descriptor;
            Quaternion facingRotation = Quaternion.LookRotation(
                descriptor.Facing,
                Vector3.up);
            if (poseIsParentLocal)
            {
                transform.localPosition = descriptor.Position;
                transform.localRotation = facingRotation;
            }
            else
            {
                transform.SetPositionAndRotation(
                    descriptor.Position,
                    facingRotation);
            }

            transform.localScale = Vector3.one;
            registry.ApplyPaletteVariant(descriptor.PaletteVariant);
            cigaretteRenderers = CityBalconySmokerAccessory.Attach(
                registry,
                descriptor.PaletteVariant);
            mouthSocket = FindDescendant(
                registry.ModelRoot,
                MouthSocketName);
            if (mouthSocket == null)
            {
                throw new InvalidOperationException(
                    $"The balcony smoker prefab lost '{MouthSocketName}'.");
            }

            Animator animator = registry.Animator;
            animator.applyRootMotion = false;
            animator.runtimeAnimatorController = null;
            // A Generic clip imported with Player3DV2 binds to that FBX's
            // hierarchy, not to a separately imported pedestrian even when
            // both Animators reference the same Avatar asset. Keep the
            // resident Animator from restoring its authored A-pose; a hidden
            // Hero V2 rig samples the literal clip and the canonical-bone
            // deltas are transferred below.
            animator.enabled = false;
            modelBaseLocalPosition = registry.ModelRoot.localPosition;
            footGroundingProbe = FootGroundingProbe.Create(registry);
            Animator poseDriverAnimator = CreatePoseDriver(heroAvatar);
            BuildGraph(poseDriverAnimator);

            loopTimeSeconds =
                descriptor.AnimationPhase01 *
                animationDefinition.LoopDurationSeconds;
            burstTimeSeconds = CalculateFrameStartSeconds(
                animationDefinition,
                HomeBalconySmokingExhaleEffect.ExhaleStartLoopFrame);
            EvaluatePose();

            exhaleEffect = gameObject.AddComponent<
                HomeBalconySmokingExhaleEffect>();
            exhaleEffect.Initialize(mouthSocket, animationDefinition);
            if (!exhaleEffect.EnableManualBurstMode())
            {
                throw new InvalidOperationException(
                    "The balcony smoker could not enter manual smoke-burst " +
                    "mode.");
            }

            IsInitialized = true;
        }

        public void Advance(float deltaTime)
        {
            if (!IsInitialized)
            {
                return;
            }

            float step = SanitizeDeltaTime(deltaTime);
            double previous = loopTimeSeconds;
            double unwrapped = previous + step;
            double duration = animationDefinition.LoopDurationSeconds;
            bool wrapped = unwrapped >= duration;
            bool crossedBurst =
                previous < burstTimeSeconds &&
                unwrapped >= burstTimeSeconds;
            if (wrapped)
            {
                unwrapped %= duration;
                crossedBurst |= unwrapped >= burstTimeSeconds;
            }

            loopTimeSeconds = unwrapped;
            EvaluatePose();
            if (crossedBurst)
            {
                exhaleEffect.EmitManualBurst();
            }
        }

        public void Shutdown()
        {
            if (!IsInitialized && !graph.IsValid())
            {
                return;
            }

            if (exhaleEffect != null)
            {
                exhaleEffect.StopAndClear();
            }

            RestoreModelBasePosition();
            if (graph.IsValid())
            {
                graph.Destroy();
            }

            if (poseDriverRoot != null)
            {
                CityPedestrianResources.DestroyObject(poseDriverRoot);
            }

            IsInitialized = false;
            registry = null;
            activeClip = null;
            animationDefinition = null;
            exhaleEffect = null;
            footGroundingProbe = null;
            poseDriverRegistry = null;
            poseDriverRoot = null;
            poseBoneBindings = Array.Empty<PoseBoneBinding>();
            mouthSocket = null;
            cigaretteRenderers = Array.Empty<Renderer>();
            loopTimeSeconds = 0d;
            burstTimeSeconds = 0d;
            CurrentLoopFrame = 0;
            CurrentClipProgress01 = 0f;
        }

        public static PlayerAnimatedInteractionDefinition
            CreateAnimationDefinition()
        {
            var holds = new float[HomeBalconySmokingPlan.LoopFrameCount];
            holds[HomeBalconySmokingPlan.RestHoldLoopFrame] =
                HomeBalconySmokingPlan.RestHoldSeconds;
            holds[HomeBalconySmokingPlan.InhaleHoldLoopFrame] =
                HomeBalconySmokingPlan.InhaleHoldSeconds;
            holds[HomeBalconySmokingPlan.BreathHoldLoopFrame] =
                HomeBalconySmokingPlan.BreathHoldSeconds;
            holds[HomeBalconySmokingPlan.ExhaleHoldLoopFrame] =
                HomeBalconySmokingPlan.ExhaleHoldSeconds;
            return new PlayerAnimatedInteractionDefinition(
                "SmokeEnter",
                SmokeLoopClipName,
                "SmokeExit",
                enterFrameCount: HomeBalconySmokingPlan.EnterFrameCount,
                enterFramesPerSecond:
                    HomeBalconySmokingPlan.EnterFramesPerSecond,
                loopFrameCount: HomeBalconySmokingPlan.LoopFrameCount,
                loopFramesPerSecond:
                    HomeBalconySmokingPlan.LoopFramesPerSecond,
                exitFrameCount: HomeBalconySmokingPlan.ExitFrameCount,
                exitFramesPerSecond:
                    HomeBalconySmokingPlan.ExitFramesPerSecond,
                loopFrameExtraHoldSeconds: holds);
        }

        private void LateUpdate()
        {
            Advance(Time.deltaTime);
        }

        private void OnEnable()
        {
            if (IsInitialized)
            {
                EvaluatePose();
            }
        }

        private void OnDisable()
        {
            if (exhaleEffect != null)
            {
                exhaleEffect.StopAndClear();
            }
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private void BuildGraph(Animator animator)
        {
            graph = PlayableGraph.Create(
                $"City Balcony Smoker {Descriptor.StableId}");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            playable = AnimationClipPlayable.Create(graph, activeClip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            playable.SetSpeed(0d);
            AnimationPlayableOutput output =
                AnimationPlayableOutput.Create(
                    graph,
                    "City Balcony Smoker Pose",
                    animator);
            output.SetSourcePlayable(playable);
            graph.Play();
        }

        private void EvaluatePose()
        {
            if (!graph.IsValid())
            {
                return;
            }

            RestoreModelBasePosition();
            CurrentLoopFrame = ResolveLoopFrame(
                animationDefinition,
                loopTimeSeconds);
            CurrentClipProgress01 = ResolveClipProgress(
                animationDefinition,
                loopTimeSeconds);
            playable.SetTime(activeClip.length * CurrentClipProgress01);
            graph.Evaluate(0f);
            TransferPoseFromDriver();
            GroundFeetToPresentationRoot();
        }

        private Animator CreatePoseDriver(Avatar heroAvatar)
        {
            poseDriverRegistry = Player3DResources.Instantiate(
                transform,
                Player3DVariant.ProductionV2);
            poseDriverRoot = poseDriverRegistry.gameObject;
            poseDriverRoot.name = "SmokeLoop Pose Driver";
            poseDriverRoot.transform.localPosition = Vector3.zero;
            poseDriverRoot.transform.localRotation = Quaternion.identity;
            poseDriverRoot.transform.localScale = Vector3.one;

            for (int index = 0;
                 index < poseDriverRegistry.Renderers.Count;
                 index++)
            {
                Renderer renderer = poseDriverRegistry.Renderers[index];
                if (renderer != null)
                {
                    renderer.enabled = false;
                }
            }

            Animator driverAnimator = poseDriverRegistry.Animator;
            if (driverAnimator == null ||
                driverAnimator.avatar != heroAvatar ||
                poseDriverRegistry.ModelRoot == null)
            {
                throw new InvalidOperationException(
                    "The hidden balcony-smoker pose driver lost the " +
                    "production Hero V2 rig.");
            }

            driverAnimator.applyRootMotion = false;
            driverAnimator.runtimeAnimatorController = null;
            driverAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            poseBoneBindings = BuildPoseBoneBindings(
                poseDriverRegistry.ModelRoot,
                registry.ModelRoot);
            return driverAnimator;
        }

        private static PoseBoneBinding[] BuildPoseBoneBindings(
            Transform sourceModelRoot,
            Transform targetModelRoot)
        {
            if (PoseBoneNames.Length != ExpectedPoseBoneCount)
            {
                throw new InvalidOperationException(
                    "The balcony-smoker canonical bone catalog drifted.");
            }

            var bindings = new PoseBoneBinding[ExpectedPoseBoneCount];
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < PoseBoneNames.Length; index++)
            {
                string boneName = PoseBoneNames[index];
                if (!names.Add(boneName))
                {
                    throw new InvalidOperationException(
                        $"Balcony-smoker pose catalog repeats bone " +
                        $"'{boneName}'.");
                }

                Transform source = FindUniqueDescendant(
                    sourceModelRoot,
                    boneName,
                    "source");
                Transform target = FindUniqueDescendant(
                    targetModelRoot,
                    boneName,
                    "target");
                string sourceParent = source.parent != null
                    ? source.parent.name
                    : string.Empty;
                string targetParent = target.parent != null
                    ? target.parent.name
                    : string.Empty;
                if (!string.Equals(
                        sourceParent,
                        targetParent,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Balcony-smoker bone '{boneName}' changed parent " +
                        $"from '{sourceParent}' to '{targetParent}'.");
                }

                bindings[index] = new PoseBoneBinding(source, target);
            }

            return bindings;
        }

        private static Transform FindUniqueDescendant(
            Transform rootTransform,
            string name,
            string rigRole)
        {
            Transform found = null;
            FindUniqueDescendant(
                rootTransform,
                name,
                rigRole,
                ref found);
            if (found == null)
            {
                throw new InvalidOperationException(
                    $"Balcony-smoker {rigRole} rig is missing canonical " +
                    $"bone '{name}'.");
            }

            return found;
        }

        private static void FindUniqueDescendant(
            Transform current,
            string name,
            string rigRole,
            ref Transform found)
        {
            if (string.Equals(current.name, name, StringComparison.Ordinal))
            {
                if (found != null)
                {
                    throw new InvalidOperationException(
                        $"Balcony-smoker {rigRole} rig repeats canonical " +
                        $"bone '{name}'.");
                }

                found = current;
            }

            for (int index = 0; index < current.childCount; index++)
            {
                FindUniqueDescendant(
                    current.GetChild(index),
                    name,
                    rigRole,
                    ref found);
            }
        }

        private void TransferPoseFromDriver()
        {
            for (int index = 0;
                 index < poseBoneBindings.Length;
                 index++)
            {
                poseBoneBindings[index].Apply();
            }
        }

        private void GroundFeetToPresentationRoot()
        {
            if (registry == null || registry.ModelRoot == null)
            {
                return;
            }

            if (!TryGetGroundingHeight(out float lowestFoot))
            {
                return;
            }

            registry.ModelRoot.position += Vector3.up *
                (transform.position.y - lowestFoot);
        }

        private bool TryGetGroundingHeight(out float height)
        {
            if (footGroundingProbe != null &&
                footGroundingProbe.TryGetLowestHeight(out height))
            {
                return true;
            }

            height = float.PositiveInfinity;
            IncludeHeight(registry.LeftFootAnchor, ref height);
            IncludeHeight(registry.RightFootAnchor, ref height);
            return !float.IsPositiveInfinity(height);
        }

        private void RestoreModelBasePosition()
        {
            if (registry != null && registry.ModelRoot != null)
            {
                registry.ModelRoot.localPosition = modelBaseLocalPosition;
            }
        }

        private static Transform FindDescendant(
            Transform root,
            string name)
        {
            if (root == null)
            {
                return null;
            }

            if (string.Equals(root.name, name, StringComparison.Ordinal))
            {
                return root;
            }

            for (int index = 0; index < root.childCount; index++)
            {
                Transform result = FindDescendant(root.GetChild(index), name);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static int ResolveLoopFrame(
            PlayerAnimatedInteractionDefinition definition,
            double elapsedSeconds)
        {
            double cursor = 0d;
            for (int frame = 0;
                 frame < definition.LoopFrameCount;
                 frame++)
            {
                cursor += definition.GetLoopFrameDurationSeconds(frame);
                if (elapsedSeconds < cursor)
                {
                    return frame;
                }
            }

            return definition.LoopFrameCount - 1;
        }

        private static float ResolveClipProgress(
            PlayerAnimatedInteractionDefinition definition,
            double elapsedSeconds)
        {
            double cursor = 0d;
            double baseFrameDuration =
                1d / definition.LoopFramesPerSecond;
            for (int frame = 0;
                 frame < definition.LoopFrameCount;
                 frame++)
            {
                double baseFrameEnd = cursor + baseFrameDuration;
                if (elapsedSeconds < baseFrameEnd)
                {
                    double local =
                        (elapsedSeconds - cursor) / baseFrameDuration;
                    return Mathf.Clamp01(
                        (float)((frame + local) /
                                definition.LoopFrameCount));
                }

                double heldFrameEnd = baseFrameEnd +
                    definition.GetLoopFrameExtraHoldSeconds(frame);
                if (elapsedSeconds < heldFrameEnd)
                {
                    return (frame + 1f) / definition.LoopFrameCount;
                }

                cursor = heldFrameEnd;
            }

            return 1f;
        }

        private static double CalculateFrameStartSeconds(
            PlayerAnimatedInteractionDefinition definition,
            int localFrame)
        {
            double seconds = 0d;
            for (int frame = 0; frame < localFrame; frame++)
            {
                seconds += definition.GetLoopFrameDurationSeconds(frame);
            }

            return seconds;
        }

        private static float SanitizeDeltaTime(float deltaTime)
        {
            return IsFinite(deltaTime)
                ? Mathf.Clamp(deltaTime, 0f, MaximumStepSeconds)
                : 0f;
        }

        private static void IncludeHeight(
            Transform anchor,
            ref float lowestHeight)
        {
            if (anchor != null && IsFinite(anchor.position.y))
            {
                lowestHeight = Mathf.Min(lowestHeight, anchor.position.y);
            }
        }

        private sealed class FootGroundingProbe
        {
            private readonly FootContactPoint[] contacts;

            private FootGroundingProbe(FootContactPoint[] contactPoints)
            {
                contacts = contactPoints;
            }

            public static FootGroundingProbe Create(
                CityPedestrianAssetRegistry assetRegistry)
            {
                var points = new List<FootContactPoint>();
                IReadOnlyList<CityPedestrianRendererBinding> bindings =
                    assetRegistry.RendererBindings;
                for (int index = 0; index < bindings.Count; index++)
                {
                    CityPedestrianRendererBinding binding = bindings[index];
                    if (binding == null || binding.Renderer == null)
                    {
                        continue;
                    }

                    Transform foot = ResolveFoot(assetRegistry, binding);
                    if (foot != null)
                    {
                        AddBoundsContacts(
                            points,
                            foot,
                            binding.Renderer.bounds);
                    }
                }

                return points.Count > 0
                    ? new FootGroundingProbe(points.ToArray())
                    : null;
            }

            public bool TryGetLowestHeight(out float height)
            {
                height = float.PositiveInfinity;
                for (int index = 0; index < contacts.Length; index++)
                {
                    if (contacts[index].TryGetWorldPosition(
                            out Vector3 position))
                    {
                        height = Mathf.Min(height, position.y);
                    }
                }

                return !float.IsPositiveInfinity(height);
            }

            private static Transform ResolveFoot(
                CityPedestrianAssetRegistry assetRegistry,
                CityPedestrianRendererBinding binding)
            {
                string rendererName = binding.RendererName ?? string.Empty;
                if (rendererName.IndexOf(
                        "LeftBootSole",
                        StringComparison.Ordinal) >= 0 ||
                    rendererName.IndexOf(
                        "ShoeSole.L",
                        StringComparison.Ordinal) >= 0)
                {
                    return assetRegistry.LeftFootAnchor;
                }

                if (rendererName.IndexOf(
                        "RightBootSole",
                        StringComparison.Ordinal) >= 0 ||
                    rendererName.IndexOf(
                        "ShoeSole.R",
                        StringComparison.Ordinal) >= 0)
                {
                    return assetRegistry.RightFootAnchor;
                }

                return null;
            }

            private static void AddBoundsContacts(
                ICollection<FootContactPoint> points,
                Transform bone,
                Bounds bounds)
            {
                AddBoundsFace(points, bone, bounds, bounds.min.y);
                AddBoundsFace(points, bone, bounds, bounds.max.y);
            }

            private static void AddBoundsFace(
                ICollection<FootContactPoint> points,
                Transform bone,
                Bounds bounds,
                float y)
            {
                points.Add(new FootContactPoint(
                    bone,
                    new Vector3(bounds.min.x, y, bounds.min.z)));
                points.Add(new FootContactPoint(
                    bone,
                    new Vector3(bounds.min.x, y, bounds.max.z)));
                points.Add(new FootContactPoint(
                    bone,
                    new Vector3(bounds.max.x, y, bounds.min.z)));
                points.Add(new FootContactPoint(
                    bone,
                    new Vector3(bounds.max.x, y, bounds.max.z)));
            }
        }

        private readonly struct FootContactPoint
        {
            private readonly Transform bone;
            private readonly Vector3 boneLocalPosition;

            public FootContactPoint(
                Transform footBone,
                Vector3 worldPosition)
            {
                bone = footBone;
                boneLocalPosition = bone != null
                    ? bone.InverseTransformPoint(worldPosition)
                    : Vector3.zero;
            }

            public bool TryGetWorldPosition(out Vector3 position)
            {
                if (bone == null)
                {
                    position = default;
                    return false;
                }

                position = bone.TransformPoint(boneLocalPosition);
                return IsFinite(position);
            }
        }

        private readonly struct PoseBoneBinding
        {
            private readonly Transform source;
            private readonly Transform target;
            private readonly Vector3 sourceRestPosition;
            private readonly Vector3 targetRestPosition;
            private readonly Quaternion sourceRestRotation;
            private readonly Quaternion targetRestRotation;
            private readonly Vector3 sourceRestScale;
            private readonly Vector3 targetRestScale;

            public PoseBoneBinding(Transform sourceBone, Transform targetBone)
            {
                source = sourceBone;
                target = targetBone;
                sourceRestPosition = sourceBone.localPosition;
                targetRestPosition = targetBone.localPosition;
                sourceRestRotation = sourceBone.localRotation;
                targetRestRotation = targetBone.localRotation;
                sourceRestScale = sourceBone.localScale;
                targetRestScale = targetBone.localScale;
                if (Vector3.Distance(
                        sourceRestPosition,
                        targetRestPosition) > RestPositionTolerance ||
                    Quaternion.Angle(
                        sourceRestRotation,
                        targetRestRotation) > RestAngleTolerance ||
                    Vector3.Distance(
                        sourceRestScale,
                        targetRestScale) > RestPositionTolerance ||
                    Mathf.Abs(sourceRestScale.x) <= 0.000001f ||
                    Mathf.Abs(sourceRestScale.y) <= 0.000001f ||
                    Mathf.Abs(sourceRestScale.z) <= 0.000001f)
                {
                    throw new InvalidOperationException(
                        $"Balcony-smoker bone '{sourceBone.name}' no " +
                        "longer shares the Hero V2 rest transform.");
                }
            }

            public void Apply()
            {
                if (source == null || target == null)
                {
                    return;
                }

                Quaternion delta =
                    Quaternion.Inverse(sourceRestRotation) *
                    source.localRotation;
                target.localPosition = targetRestPosition +
                    (source.localPosition - sourceRestPosition);
                target.localRotation = targetRestRotation * delta;
                Vector3 sourceScaleRatio = new Vector3(
                    source.localScale.x / sourceRestScale.x,
                    source.localScale.y / sourceRestScale.y,
                    source.localScale.z / sourceRestScale.z);
                target.localScale = Vector3.Scale(
                    targetRestScale,
                    sourceScaleRatio);
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
