using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    /// <summary>
    /// Which of a design's two clip pairs a body plays.
    ///
    /// Every promoted resident carries TWO: the working loop it was authored
    /// for - the watchman's `WatchmanWatch`, the weigher's `WeigherCheck`,
    /// the babushka's `BabushkaSmoke` - and a shared citizen gait so that an
    /// anonymous copy of it on the promenade reads as a passer-by rather than
    /// as that person doing their job in the middle of the street.
    ///
    /// Until 2026-09-02 the graph only ever built the roaming pair, so a
    /// figure POSED at a courtyard dock played a one-and-a-half-second
    /// pavement breath for ever. That is what the user was looking at when he
    /// asked for «осмысленную и активную idle анимацию»: the meaningful
    /// animation already existed, one field away, and nothing selected it.
    /// </summary>
    public enum CityPedestrianClipSource
    {
        /// <summary>The shared citizen gait: anyone on the street.</summary>
        Roaming = 0,

        /// <summary>The design's own working loop: a body standing where it
        /// belongs, doing what it is for.</summary>
        Placed = 1
    }

    /// <summary>
    /// Lightweight, manually advanced Idle/Walk/Sit presentation for a pooled
    /// city pedestrian. Route motion remains entirely code-owned.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityPedestrianPresentation : MonoBehaviour
    {
        public const float LocomotionBlendDuration = 0.15f;

        private PlayableGraph graph;
        private AnimationMixerPlayable locomotionMixer;
        private AnimationClipPlayable idlePlayable;
        private AnimationClipPlayable walkPlayable;
        private AnimationClipPlayable sitPlayable;
        private AnimationClipPlayable authoredActionPlayable;
        private AnimationClip authoredActionClip;
        private bool hasSitPlayable;
        private bool hasAuthoredActionPlayable;
        private int authoredActionInputIndex = -1;
        private float authoredActionWeight;
        private Transform seatAnchor;
        private CityPedestrianSeatedRide seatedRide;
        private CityPedestrianAssetRegistry registry;
        private CityPedestrianClipSource clipSource =
            CityPedestrianClipSource.Roaming;
        private AnimationClip activeIdleClip;
        private AnimationClip activeWalkClip;
        private Vector3 modelBaseLocalPosition;
        private float animationSpeed = 0.91f;
        private float targetWalkWeight;
        private float groundedFootHeightOffset;
        private bool groundedFootHeightOffsetCaptured;
        private float archetypeGroundTrim;
        private bool archetypeGroundTrimResolved;
        // The hero's leg layer, legs only: per-foot probes and a two-bone
        // solve so a walker's boots rest on the kerb or tread under each
        // of them instead of the whole model being pinned to its lowest
        // sole. Airborne designs and seated riders keep the old paths.
        private readonly Player3DProceduralLocomotionLayer legLayer =
            new Player3DProceduralLocomotionLayer();
        private bool legLayerBound;
        // The Silent Hill head, mirrored: the same additive neck-and-head
        // turn the hero makes toward a passer-by, made by the passer-by
        // toward him. The layer is shared with every staged NPC that
        // looks back at the hero (the cemetery watchman first).
        private readonly NpcAttentionHeadLayer attention =
            new NpcAttentionHeadLayer();

        public bool IsInitialized { get; private set; }
        public bool IsMoving { get; private set; }
        public bool IsSeated => seatAnchor != null;
        public float WalkWeight { get; private set; }
        public float AnimationSpeed => animationSpeed;
        public CityPedestrianAssetRegistry Registry => registry;
        public AnimationClip AuthoredActionClip => authoredActionClip;
        public float AuthoredActionWeight => authoredActionWeight;

        /// <summary>The seat this walker is aligned to, or <c>null</c> on
        /// the pavement.</summary>
        public Transform SeatAnchor => seatAnchor;

        /// <summary>Where the head is asked to look, or <c>null</c> for
        /// nothing; the last value handed to <see cref="SetAttentionFocus"/>.</summary>
        public Vector3? AttentionFocus => attention.Focus;

        /// <summary>How far the glance is blended in, 0..1.</summary>
        public float AttentionWeight => attention.Weight;

        /// <summary>Whether this rig has a head bone the glance can turn.</summary>
        public bool HasAttentionHead => attention.HasHead;

        /// <summary>The sanitised delta the graph was last evaluated with -
        /// already accelerated for a distant walker.</summary>
        public float LastAdvanceDeltaTime { get; private set; }

        /// <summary>
        /// Raised right after the graph has written the bones for one
        /// <see cref="Advance(float, bool, bool)"/>, with the same delta.
        /// Secondary motion that must stay in step with the body whatever
        /// state it is in - the kettle's boil - rides this rather than a
        /// clock of its own. <see cref="ConfigureCycle"/> evaluates the
        /// graph directly and does not raise it.
        /// </summary>
        public event Action<float> Advanced;

        public void Initialize(
            CityPedestrianAssetRegistry assetRegistry)
        {
            Initialize(assetRegistry, CityPedestrianClipSource.Roaming);
        }

        /// <summary>
        /// The same, choosing which pair of clips the body lives on. Every
        /// caller that does not say gets <see cref="CityPedestrianClipSource.
        /// Roaming"/>, which is what the thirteen pooled walkers, the bar
        /// patrons, the bus passengers and the balcony smokers all want.
        /// </summary>
        public void Initialize(
            CityPedestrianAssetRegistry assetRegistry,
            CityPedestrianClipSource source)
        {
            clipSource = source;
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The city pedestrian presentation is already initialized.");
            }

            registry = assetRegistry != null
                ? assetRegistry
                : throw new ArgumentNullException(nameof(assetRegistry));
            if (registry.Animator == null ||
                registry.IdleClip == null ||
                registry.WalkClip == null)
            {
                throw new InvalidOperationException(
                    "The city pedestrian registry requires an Animator and " +
                    "archetype Idle/Walk clips.");
            }

            Animator animator = registry.Animator;
            animator.applyRootMotion = false;
            animator.runtimeAnimatorController = null;
            animator.cullingMode =
                AnimatorCullingMode.CullUpdateTransforms;
            modelBaseLocalPosition = registry.ModelRoot != null
                ? registry.ModelRoot.localPosition
                : Vector3.zero;

            BuildGraph(animator);
            IsInitialized = true;
            ConfigureCycle(animationSpeed, 0f);
            BindLegLayer();
            BindAttentionBones();
            SetMoving(false);
        }

        /// <summary>
        /// Where this walker's head should look, in world space, or
        /// <c>null</c> for nothing. Set before each <see cref="Advance(float,
        /// bool, bool)"/> by whoever decides what the walker notices - the
        /// actor, for the hero - and applied after the graph has written the
        /// bones. The presentation owns all smoothing and limits, exactly as
        /// the hero's does.
        /// </summary>
        public void SetAttentionFocus(Vector3? focus)
        {
            attention.SetFocus(focus);
        }

        /// <summary>
        /// Drops the glance at once: focus, blend and the bones it moved.
        /// A body going back to the pool calls this explicitly - it cannot
        /// wait for <c>OnDisable</c>, which edit mode never raises - so no
        /// spawn inherits the last hero its predecessor was looking at.
        /// </summary>
        public void ClearAttention()
        {
            attention.Restore();
            attention.Clear();
        }

        public void ConfigureCycle(
            float walkAnimationSpeed,
            float phase01)
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException(
                    "Initialize the city pedestrian presentation first.");
            }

            animationSpeed = Mathf.Clamp(
                IsFinite(walkAnimationSpeed)
                    ? walkAnimationSpeed
                    : GetMinimumAnimationSpeed(),
                GetMinimumAnimationSpeed(),
                GetMaximumAnimationSpeed());
            float phase = IsFinite(phase01)
                ? Mathf.Repeat(phase01, 1f)
                : 0f;
            idlePlayable.SetSpeed(1d);
            walkPlayable.SetSpeed(animationSpeed);
            idlePlayable.SetTime(phase * activeIdleClip.length);
            walkPlayable.SetTime(phase * activeWalkClip.length);
            EvaluateGraph(0f);
        }

        public void SetMoving(bool moving)
        {
            SetMoving(moving, true);
        }

        public void SetMoving(bool moving, bool immediately)
        {
            if (!IsInitialized || !locomotionMixer.IsValid())
            {
                return;
            }

            IsMoving = moving;
            targetWalkWeight = moving ? 1f : 0f;
            if (immediately)
            {
                WalkWeight = targetWalkWeight;
            }

            ApplyMixerWeights();
        }

        /// <summary>
        /// Binds this walker to a live passenger seat. The seat anchor belongs
        /// to the sprung bus body, so it keeps moving under the design while
        /// the design keeps its own authored seated posture.
        /// </summary>
        public bool TrySeat(Transform anchor, CityPedestrianSeatedRide ride)
        {
            if (!IsInitialized ||
                anchor == null ||
                ride == null ||
                !hasSitPlayable ||
                registry == null ||
                registry.PelvisAnchor == null)
            {
                return false;
            }

            seatAnchor = anchor;
            seatedRide = ride;
            IsMoving = false;
            targetWalkWeight = 0f;
            WalkWeight = 0f;
            ApplyMixerWeights();
            return true;
        }

        public void ClearSeat()
        {
            if (seatAnchor == null && seatedRide == null)
            {
                return;
            }

            seatAnchor = null;
            seatedRide = null;
            if (IsInitialized && locomotionMixer.IsValid())
            {
                ApplyMixerWeights();
            }
        }

        /// <summary>
        /// Samples one full-body authored action on the presentation's own
        /// graph. Scene-local tableaux use this instead of creating a second
        /// PlayableGraph for the same Animator. The ordinary idle/sit pose
        /// remains input zero and the caller owns the blend envelope.
        /// </summary>
        public bool ApplyAuthoredAction(
            AnimationClip clip,
            float normalizedTime,
            float weight)
        {
            if (!IsInitialized ||
                clip == null ||
                !IsFinite(normalizedTime) ||
                !IsFinite(weight) ||
                !locomotionMixer.IsValid())
            {
                return false;
            }

            EnsureAuthoredActionPlayable(clip);
            authoredActionPlayable.SetTime(
                Mathf.Clamp01(normalizedTime) * clip.length);
            authoredActionPlayable.SetSpeed(0d);
            authoredActionWeight = Mathf.Clamp01(weight);
            ApplyMixerWeights();
            EvaluateGraph(0f);
            return true;
        }

        public void ClearAuthoredAction()
        {
            if (!IsInitialized || !locomotionMixer.IsValid())
            {
                return;
            }

            authoredActionWeight = 0f;
            ApplyMixerWeights();
            EvaluateGraph(0f);
        }

        private void ApplyMixerWeights()
        {
            float baseWeight = 1f - authoredActionWeight;
            if (IsSeated)
            {
                locomotionMixer.SetInputWeight(0, 0f);
                locomotionMixer.SetInputWeight(1, 0f);
                if (hasSitPlayable)
                {
                    locomotionMixer.SetInputWeight(2, baseWeight);
                }
            }
            else
            {
                locomotionMixer.SetInputWeight(
                    0,
                    (1f - WalkWeight) * baseWeight);
                locomotionMixer.SetInputWeight(
                    1,
                    WalkWeight * baseWeight);
                if (hasSitPlayable)
                {
                    locomotionMixer.SetInputWeight(2, 0f);
                }
            }

            if (authoredActionInputIndex >= 0)
            {
                locomotionMixer.SetInputWeight(
                    authoredActionInputIndex,
                    hasAuthoredActionPlayable
                        ? authoredActionWeight
                        : 0f);
            }
        }

        public void Advance(float deltaTime, bool moving)
        {
            Advance(deltaTime, moving, false);
        }

        public void Advance(
            float deltaTime,
            bool moving,
            bool immediately)
        {
            if (!IsInitialized)
            {
                return;
            }

            float safeDeltaTime = SanitizeDeltaTime(deltaTime);
            SetMoving(IsSeated ? false : moving, immediately);
            if (!immediately &&
                Mathf.Abs(WalkWeight - targetWalkWeight) > 0.0001f)
            {
                WalkWeight = Mathf.MoveTowards(
                    WalkWeight,
                    targetWalkWeight,
                    safeDeltaTime / LocomotionBlendDuration);
                ApplyMixerWeights();
            }

            EvaluateGraph(safeDeltaTime);
            attention.Apply(safeDeltaTime);
            LastAdvanceDeltaTime = safeDeltaTime;
            Advanced?.Invoke(safeDeltaTime);
        }

        public void Shutdown()
        {
            if (!IsInitialized && !graph.IsValid())
            {
                return;
            }

            RestoreModelBasePosition();
            if (graph.IsValid())
            {
                graph.Destroy();
            }

            IsInitialized = false;
            IsMoving = false;
            seatAnchor = null;
            seatedRide = null;
            hasSitPlayable = false;
            hasAuthoredActionPlayable = false;
            authoredActionClip = null;
            authoredActionInputIndex = -1;
            authoredActionWeight = 0f;
            WalkWeight = 0f;
            targetWalkWeight = 0f;
            archetypeGroundTrim = 0f;
            archetypeGroundTrimResolved = false;
            legLayer.Dispose();
            legLayerBound = false;
            attention.Unbind();
            registry = null;
            Advanced = null;
        }

        private void BuildGraph(Animator animator)
        {
            graph = PlayableGraph.Create(
                $"City Pedestrian {GetEntityId()}");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            hasSitPlayable = registry.SitClip != null;
            authoredActionInputIndex = hasSitPlayable ? 3 : 2;
            locomotionMixer = AnimationMixerPlayable.Create(
                graph,
                authoredActionInputIndex + 1);
            // Which pair this body lives on. ROAMING is the ambient one
            // where a design declares it and the ordinary slots otherwise: a
            // promoted resident walks the street on the shared citizen gait
            // while its PLACED copy goes on beating a carpet or holding a
            // gate.
            //
            // Cached, and not only for speed: `ConfigureCycle` seeds the
            // phase off a clip length, and it used to read `registry.IdleClip`
            // while the playable had been built from `RoamingIdleClip`. For
            // the babushka that pushed a `phase x 4.0 s` seek into a `2.0 s`
            // clip - it wrapped instead of erroring, quietly aliasing the
            // phase spread the director asks for so that bodies meant to be
            // out of step were not.
            activeIdleClip = clipSource == CityPedestrianClipSource.Placed
                ? registry.IdleClip
                : registry.RoamingIdleClip;
            activeWalkClip = clipSource == CityPedestrianClipSource.Placed
                ? registry.WalkClip
                : registry.RoamingWalkClip;
            idlePlayable = AnimationClipPlayable.Create(
                graph,
                activeIdleClip);
            walkPlayable = AnimationClipPlayable.Create(
                graph,
                activeWalkClip);
            idlePlayable.SetApplyFootIK(false);
            idlePlayable.SetApplyPlayableIK(false);
            walkPlayable.SetApplyFootIK(false);
            walkPlayable.SetApplyPlayableIK(false);
            graph.Connect(idlePlayable, 0, locomotionMixer, 0);
            graph.Connect(walkPlayable, 0, locomotionMixer, 1);
            locomotionMixer.SetInputWeight(0, 1f);
            locomotionMixer.SetInputWeight(1, 0f);
            if (hasSitPlayable)
            {
                sitPlayable = AnimationClipPlayable.Create(
                    graph,
                    registry.SitClip);
                sitPlayable.SetApplyFootIK(false);
                sitPlayable.SetApplyPlayableIK(false);
                graph.Connect(sitPlayable, 0, locomotionMixer, 2);
                locomotionMixer.SetInputWeight(2, 0f);
            }
            locomotionMixer.SetInputWeight(authoredActionInputIndex, 0f);

            AnimationPlayableOutput output =
                AnimationPlayableOutput.Create(
                    graph,
                    "City Pedestrian Animator",
                    animator);
            output.SetSourcePlayable(locomotionMixer);
            graph.Play();
        }

        private void EnsureAuthoredActionPlayable(AnimationClip clip)
        {
            if (hasAuthoredActionPlayable && authoredActionClip == clip)
            {
                return;
            }

            if (hasAuthoredActionPlayable)
            {
                graph.Disconnect(
                    locomotionMixer,
                    authoredActionInputIndex);
                authoredActionPlayable.Destroy();
                hasAuthoredActionPlayable = false;
                authoredActionClip = null;
            }

            authoredActionPlayable = AnimationClipPlayable.Create(
                graph,
                clip);
            authoredActionPlayable.SetApplyFootIK(false);
            authoredActionPlayable.SetApplyPlayableIK(false);
            authoredActionPlayable.SetSpeed(0d);
            graph.Connect(
                authoredActionPlayable,
                0,
                locomotionMixer,
                authoredActionInputIndex);
            authoredActionClip = clip;
            hasAuthoredActionPlayable = true;
        }

        private void EvaluateGraph(float deltaTime)
        {
            if (!graph.IsValid())
            {
                return;
            }

            RestoreModelBasePosition();
            legLayer.Restore();
            attention.Restore();
            graph.Evaluate(deltaTime);
            if (IsSeated)
            {
                AlignPelvisToSeat();
                return;
            }

            if (legLayerBound && !registry.PreservesAirborneMotion)
            {
                ApplyLegLayer(deltaTime);
                return;
            }

            GroundFeetToPresentationRoot();
        }

        /// <summary>
        /// A seated design must not be sole-pinned: its boots leave the ground
        /// plane the pin measures against, so the pin would drag the whole
        /// model down until the feet touched the cabin floor. Every walker
        /// shares the hero's rest pelvis, so aligning that one bone to the
        /// cushion seats all four riding proportions with the same rule.
        /// </summary>
        private void AlignPelvisToSeat()
        {
            if (registry == null ||
                registry.ModelRoot == null ||
                registry.PelvisAnchor == null ||
                seatAnchor == null ||
                seatedRide == null)
            {
                return;
            }

            Vector3 target = seatAnchor.position +
                (transform.up * seatedRide.SeatLift) -
                (transform.forward * seatedRide.SeatBackOffset);
            registry.ModelRoot.position +=
                target - registry.PelvisAnchor.position;
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

            // An airborne design authors its own vertical arc, proved grounded
            // at its landing frames by the deterministic generator, so pinning
            // its lowest sole every frame would erase the hop. It is lowered
            // by one declared constant instead. The lift being cancelled comes
            // from retargeting a squat skeleton through the hero's shared
            // Generic Avatar rather than from the clip, so no measurement of
            // the clip can predict it and the archetype declares it directly.
            if (registry.PreservesAirborneMotion)
            {
                float trim = GetArchetypeGroundTrim();
                if (trim != 0f)
                {
                    registry.ModelRoot.position -= Vector3.up * trim;
                }

                return;
            }

            if (!groundedFootHeightOffsetCaptured)
            {
                groundedFootHeightOffset =
                    lowestFoot - transform.position.y;
                groundedFootHeightOffsetCaptured = true;
            }

            float targetFootHeight = transform.position.y +
                                     groundedFootHeightOffset;
            registry.ModelRoot.position += Vector3.up *
                (targetFootHeight - lowestFoot);
        }

        private float GetArchetypeGroundTrim()
        {
            if (!archetypeGroundTrimResolved)
            {
                archetypeGroundTrim =
                    registry != null &&
                    CityPedestrianResources.TryGetArchetype(
                        registry.DesignId,
                        out CityPedestrianArchetype archetype)
                        ? archetype.GroundTrim
                        : 0f;
                archetypeGroundTrimResolved = true;
            }

            return archetypeGroundTrim;
        }

        private void RestoreModelBasePosition()
        {
            if (registry != null && registry.ModelRoot != null)
            {
                registry.ModelRoot.localPosition = modelBaseLocalPosition;
            }
        }

        private void OnDisable()
        {
            IsMoving = false;
            WalkWeight = 0f;
            targetWalkWeight = 0f;
            authoredActionWeight = 0f;
            if (locomotionMixer.IsValid())
            {
                ApplyMixerWeights();
            }

            legLayer.Restore();
            ClearAttention();
            RestoreModelBasePosition();
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private bool TryGetGroundingHeight(out float height)
        {
            height = float.PositiveInfinity;
            IncludeHeight(registry.LeftFootAnchor, ref height);
            IncludeHeight(registry.RightFootAnchor, ref height);
            return !float.IsPositiveInfinity(height);
        }

        /// <summary>
        /// Adopts the hero's leg layer for this walker: pelvis, thigh, shin
        /// and foot bones found by the names every humanoid rig in the
        /// project shares, boot soles from the renderer bindings. A rig
        /// missing any of them keeps the legacy whole-model pin.
        /// </summary>
        private void BindLegLayer()
        {
            legLayerBound = false;
            if (registry == null ||
                registry.Animator == null ||
                registry.PelvisAnchor == null ||
                registry.PreservesAirborneMotion)
            {
                return;
            }

            Transform bones = registry.Animator.transform;
            Transform leftThigh = FindBone(bones, "thigh.L");
            Transform leftShin = FindBone(bones, "shin.L");
            Transform leftFoot = registry.LeftFootAnchor != null
                ? registry.LeftFootAnchor
                : FindBone(bones, "foot.L");
            Transform rightThigh = FindBone(bones, "thigh.R");
            Transform rightShin = FindBone(bones, "shin.R");
            Transform rightFoot = registry.RightFootAnchor != null
                ? registry.RightFootAnchor
                : FindBone(bones, "foot.R");
            if (leftThigh == null || leftShin == null || leftFoot == null ||
                rightThigh == null || rightShin == null || rightFoot == null)
            {
                return;
            }

            CollectSoleRenderers(
                out List<SkinnedMeshRenderer> leftSoles,
                out List<SkinnedMeshRenderer> rightSoles);
            legLayer.Bind(
                null,
                transform,
                registry.PelvisAnchor,
                null,
                null,
                null,
                leftThigh,
                leftShin,
                leftFoot,
                rightThigh,
                rightShin,
                rightFoot,
                Player3DFootGroundProbe.Create(
                    leftSoles,
                    rightSoles,
                    transform));
            legLayer.Calibrate();
            legLayerBound = legLayer.HasGroundedFootHeightOffset;
            if (!legLayerBound)
            {
                legLayer.Dispose();
            }
        }

        /// <summary>
        /// The head anchor every pedestrian registry carries and the neck
        /// above the chest, by the name every humanoid rig in the project
        /// shares. A rig without a head keeps its head still; one without a
        /// neck turns the head alone. The glance is measured against this
        /// presentation's own facing, which the actor keeps aligned with
        /// its root.
        /// </summary>
        private void BindAttentionBones()
        {
            attention.Bind(
                transform,
                registry != null ? registry.HeadAnchor : null,
                registry != null ? registry.Animator : null);
        }

        private void ApplyLegLayer(float deltaTime)
        {
            // Walkers do not share the hero's left-first contact order for
            // certain, so both boots take the same plant: fully down when
            // standing, the walk's own cosine when moving. The layer keeps
            // each boot's authored lift relative to the other, so the swing
            // still clears the ground.
            float plant = 1f;
            if (WalkWeight > 0.0001f &&
                walkPlayable.IsValid() &&
                activeWalkClip != null &&
                activeWalkClip.length > 0.0001f)
            {
                float cycle = (float)(walkPlayable.GetTime() /
                                      activeWalkClip.length);
                PlayerFootPlacementRules.FootPlantAmounts(
                    cycle,
                    false,
                    0.68f,
                    out float left,
                    out float right);
                plant = Mathf.Lerp(
                    1f,
                    PlayerFootPlacementRules.CombinedPlant(left, right),
                    WalkWeight);
            }

            legLayer.Apply(
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
                    plant,
                    plant,
                    0f,
                    false,
                    WalkWeight > 0.5f),
                deltaTime);
        }

        private void CollectSoleRenderers(
            out List<SkinnedMeshRenderer> leftSoles,
            out List<SkinnedMeshRenderer> rightSoles)
        {
            leftSoles = new List<SkinnedMeshRenderer>();
            rightSoles = new List<SkinnedMeshRenderer>();
            IReadOnlyList<CityPedestrianRendererBinding> bindings =
                registry.RendererBindings;
            for (int index = 0; index < bindings.Count; index++)
            {
                CityPedestrianRendererBinding binding = bindings[index];
                if (binding == null ||
                    !(binding.Renderer is SkinnedMeshRenderer skinned))
                {
                    continue;
                }

                string rendererName = binding.RendererName ?? string.Empty;
                if (rendererName.IndexOf("LeftBootSole", StringComparison.Ordinal) >= 0 ||
                    rendererName.IndexOf("ShoeSole.L", StringComparison.Ordinal) >= 0)
                {
                    leftSoles.Add(skinned);
                }
                else if (rendererName.IndexOf("RightBootSole", StringComparison.Ordinal) >= 0 ||
                         rendererName.IndexOf("ShoeSole.R", StringComparison.Ordinal) >= 0)
                {
                    rightSoles.Add(skinned);
                }
            }
        }

        private static Transform FindBone(Transform root, string boneName)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == boneName)
            {
                return root;
            }

            for (int index = 0; index < root.childCount; index++)
            {
                Transform found = FindBone(root.GetChild(index), boneName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static void IncludeHeight(
            Transform anchor,
            ref float lowestHeight)
        {
            if (anchor != null && IsFinite(anchor.position.y))
            {
                lowestHeight = Mathf.Min(
                    lowestHeight,
                    anchor.position.y);
            }
        }

        private static float SanitizeDeltaTime(float deltaTime)
        {
            return IsFinite(deltaTime)
                ? Mathf.Max(0f, deltaTime)
                : 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private float GetMinimumAnimationSpeed()
        {
            return registry != null &&
                   CityPedestrianResources.TryGetArchetype(
                       registry.DesignId,
                       out CityPedestrianArchetype archetype)
                ? archetype.MinimumAnimationSpeed
                : CityPedestrianPlanner.MinimumAnimationSpeed;
        }

        private float GetMaximumAnimationSpeed()
        {
            return registry != null &&
                   CityPedestrianResources.TryGetArchetype(
                       registry.DesignId,
                       out CityPedestrianArchetype archetype)
                ? archetype.MaximumAnimationSpeed
                : CityPedestrianPlanner.MaximumAnimationSpeed;
        }
    }
}
