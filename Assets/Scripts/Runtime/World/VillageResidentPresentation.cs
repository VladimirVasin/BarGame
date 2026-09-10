using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    /// <summary>Absolute-time animation only; placement, tasks and prop ownership stay outside.</summary>
    [DisallowMultipleComponent]
    public sealed partial class VillageResidentPresentation : MonoBehaviour
    {
        public const float PickupContactSeconds = 1.5f;
        public const float PlaceContactSeconds = 1.5f;
        public const float ShovelContactSeconds = 1.5f;
        public const float DoorGripStartSeconds = 1f;
        public const float DoorGripEndSeconds = 2.5f;
        public const float DoorDurationSeconds = 3.5f;
        public const float GustDurationSeconds = 2.2f;
        public const float FreeRunStartSpeed = 1.8f;
        public const float FreeRunFullSpeed = 2.6f;
        public static Vector3 CarryBasketLocalPosition => new Vector3(0f, .50f, .44f);
        public static Vector3 RestBasketLocalPosition => new Vector3(0f, .38f, .44f);
        public static Quaternion CarryBasketLocalRotation => Quaternion.identity;

        [SerializeField] private VillageResidentRole role;
        [SerializeField] private Animator animator;
        [SerializeField] private Transform modelRoot;
        [SerializeField] private Transform rightGrip;
        [SerializeField] private Transform leftGrip;
        [SerializeField] private Transform head;
        [SerializeField] private AnimationClip[] clips = Array.Empty<AnimationClip>();
        [SerializeField] private Renderer[] renderers = Array.Empty<Renderer>();
        [SerializeField] private Color[] colors = Array.Empty<Color>();
        [SerializeField] private Texture2D atlas;
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private AnimationClipPlayable[] playables;
        private AnimationClip freeRunClip;
        private AnimationClipPlayable freeRunPlayable;
        private Animator freeRunSourceAnimator;
        private GameObject freeRunSkeleton;
        private FreeRunBone[] freeRunBones;
        private readonly Dictionary<Transform, Vector3> freeRunRestPositions = new Dictionary<Transform, Vector3>();
        private readonly struct FreeRunBone
        {
            public readonly Transform Source, Target;
            public readonly Vector3 SourceRestPosition, TargetRestPosition;
            public FreeRunBone(Transform source, Transform target, Vector3 targetRestPosition)
            {
                Source = source; Target = target;
                SourceRestPosition = source.localPosition;
                TargetRestPosition = targetRestPosition;
            }
        }
        private AnimationLayerMixerPlayable weatherLayers;
        private AnimationLayerMixerPlayable contactLayers;
        private AnimationClipPlayable doorWalkPlayable;
        private AnimationClipPlayable gustPlayable;
        private AvatarMask gustMask;
        private AvatarMask heldGustMask;
        private AvatarMask doorWalkMask;
        private Transform spine;
        private Transform neck;
        private bool gustUsesHeldMask;
        private Transform rightUpperArm, rightForearm, rightHand;
        private Transform leftUpperArm, leftForearm, leftHand;
        public VillageResidentRole Role => role;
        public Transform ModelRoot => modelRoot;
        public Transform RightGrip => rightGrip;
        public Transform LeftGrip => leftGrip;
        public Transform Head => head;
        public Animator Animator => animator;
        public VillageResidentAction CurrentAction { get; private set; }
        public float CurrentActionSeconds { get; private set; }
        public AnimationClip FreeRunClip => freeRunClip;
        public float CurrentRunWeight { get; private set; }
        public float CurrentLocomotionSpeed { get; private set; }
        public float CurrentLocomotionCycle { get; private set; }
        public bool IsInitialized => graph.IsValid();
        public float ClipLength(VillageResidentAction action) => clips[(int)action].length;

        public void Configure(VillageResidentRole configuredRole, Animator configuredAnimator,
            Transform configuredModelRoot, Transform configuredRightGrip, Transform configuredLeftGrip,
            Transform configuredHead, AnimationClip[] configuredClips, Renderer[] configuredRenderers,
            Color[] configuredColors, Texture2D configuredAtlas)
        {
            role = configuredRole; animator = configuredAnimator; modelRoot = configuredModelRoot;
            rightGrip = configuredRightGrip; leftGrip = configuredLeftGrip; head = configuredHead;
            clips = configuredClips; renderers = configuredRenderers; colors = configuredColors; atlas = configuredAtlas;
            ApplyAppearance();
        }

        public void Initialize()
        {
            if (graph.IsValid()) return;
            if (animator == null || rightGrip == null || leftGrip == null || head == null ||
                clips.Length != Enum.GetValues(typeof(VillageResidentAction)).Length)
                throw new InvalidOperationException("Village resident has no complete authored rig/action bindings.");
            ApplyAppearance();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create("VillageResident." + role);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, clips.Length);
            playables = new AnimationClipPlayable[clips.Length];
            for (int i = 0; i < clips.Length; ++i)
            {
                if (clips[i] == null) throw new InvalidOperationException("Missing village action " + i);
                playables[i] = AnimationClipPlayable.Create(graph, clips[i]);
                playables[i].SetApplyFootIK(false); playables[i].SetApplyPlayableIK(false);
                playables[i].SetSpeed(0);
                graph.Connect(playables[i], 0, mixer, i);
            }
            if (freeRunClip != null) CreateFreeRunPlayable();
            Transform Find(string name) => CityPedestrianHandProps.FindSocket(modelRoot, name)
                ?? throw new InvalidOperationException("Missing ordinary village joint " + name);
            spine = Find("spine");
            neck = Find("neck");
            rightUpperArm = Find("upper_arm.R"); rightForearm = Find("forearm.R"); rightHand = Find("hand.R");
            leftUpperArm = Find("upper_arm.L"); leftForearm = Find("forearm.L"); leftHand = Find("hand.L");
            weatherLayers = AnimationLayerMixerPlayable.Create(graph, 2);
            contactLayers = AnimationLayerMixerPlayable.Create(graph, 2);
            graph.Connect(mixer, 0, contactLayers, 0);
            contactLayers.SetInputWeight(0, 1f);
            doorWalkPlayable = AnimationClipPlayable.Create(graph, clips[(int)VillageResidentAction.Walk]);
            doorWalkPlayable.SetSpeed(0); doorWalkPlayable.SetApplyFootIK(false);
            graph.Connect(doorWalkPlayable, 0, contactLayers, 1);
            doorWalkMask = CreateGustMask(true);
            contactLayers.SetLayerMaskFromAvatarMask(1, doorWalkMask);
            graph.Connect(contactLayers, 0, weatherLayers, 0);
            weatherLayers.SetInputWeight(0, 1f);
            gustPlayable = AnimationClipPlayable.Create(graph, clips[(int)VillageResidentAction.Gust]);
            gustPlayable.SetSpeed(0); gustPlayable.SetApplyFootIK(false);
            graph.Connect(gustPlayable, 0, weatherLayers, 1);
            gustMask = CreateGustMask();
            heldGustMask = CreateGustMask(heldOnly: true);
            gustUsesHeldMask = false;
            weatherLayers.SetLayerMaskFromAvatarMask(1, gustMask);
            AnimationPlayableOutput.Create(graph, "Body", animator).SetSourcePlayable(weatherLayers);
            graph.Play();
            Apply(VillageResidentAction.Idle, 0);
        }

        public void Apply(VillageResidentAction action, float elapsedSeconds, Vector3? lookAt = null)
        {
            Initialize();
            weatherLayers.SetInputWeight(1, 0);
            contactLayers.SetInputWeight(1, 0);
            ResetFreeLocomotion();
            CurrentAction = action; CurrentActionSeconds = Mathf.Max(0, elapsedSeconds);
            for (int i = 0; i < clips.Length; ++i) mixer.SetInputWeight(i, i == (int)action ? 1f : 0f);
            SampleTime((int)action, elapsedSeconds);
            graph.Evaluate(0);
            ApplyLook(lookAt);
        }

        /// <summary>Blend planted rest with the authored gait as actual speed approaches zero.</summary>
        public void ApplyLocomotion(float speed, bool carry, float elapsedSeconds, Vector3? lookAt = null)
        {
            Initialize();
            weatherLayers.SetInputWeight(1, 0);
            contactLayers.SetInputWeight(1, 0);
            ResetFreeLocomotion();
            CurrentLocomotionSpeed = Mathf.Abs(speed);
            int idle = (int)(carry ? VillageResidentAction.Carry : VillageResidentAction.Idle);
            int walk = (int)(carry ? VillageResidentAction.CarryWalk : VillageResidentAction.Walk);
            float weight = Mathf.Clamp01(Mathf.Abs(speed) / .75f);
            for (int i = 0; i < clips.Length; ++i) mixer.SetInputWeight(i, i == idle ? 1-weight : i == walk ? weight : 0);
            SampleTime(idle, elapsedSeconds); SampleTime(walk, elapsedSeconds);
            CurrentAction = (VillageResidentAction)(weight > .01f ? walk : idle);
            CurrentActionSeconds = Mathf.Max(0, elapsedSeconds);
            graph.Evaluate(0);
            ApplyLook(lookAt);
        }

        public void ApplyLocomotion(float elapsedSeconds, float speedMetersPerSecond, bool carrying)
            => ApplyLocomotion(speedMetersPerSecond, carrying, elapsedSeconds, null);

        /// <summary>Opt a compatible Generic rig into the packaged hero's bone-only run.</summary>
        public void ConfigureFreeRun(AnimationClip clip, Animator sourceAnimator)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            if (sourceAnimator == null) throw new ArgumentNullException(nameof(sourceAnimator));
            if (freeRunClip != null && freeRunClip != clip)
                throw new InvalidOperationException("A resident already has a different shared run binding.");
            freeRunClip = clip;
            freeRunSourceAnimator = sourceAnimator;
            Initialize();
            if (!freeRunPlayable.IsValid()) CreateFreeRunPlayable();
        }

        private void CreateFreeRunPlayable()
        {
            // Hero curves bind ROOT_PlayerV2/RIG_Player; resident curves bind
            // ROOT_Player/RIG_Player. A shared Generic Avatar does not remap
            // those paths. Sample the original clip on its original bone-only
            // hierarchy, then blend matching joint poses onto the resident.
            // No hero model, renderers, colliders or gameplay are instantiated.
            freeRunSkeleton = new GameObject("Shared Run Skeleton");
            freeRunSkeleton.transform.SetParent(transform, false);
            var sampler = freeRunSkeleton.AddComponent<Animator>();
            sampler.avatar = freeRunSourceAnimator.avatar;
            sampler.applyRootMotion = false;
            sampler.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Transform rootBone = CityPedestrianHandProps.FindSocket(freeRunSourceAnimator.transform, "root")
                ?? throw new InvalidOperationException("The shared run source has no root bone.");
            Transform CopyParent(Transform source)
            {
                if (source == freeRunSourceAnimator.transform) return freeRunSkeleton.transform;
                return CopyTransform(source, CopyParent(source.parent));
            }
            var bindings = new List<FreeRunBone>();
            void CopyBones(Transform source, Transform parent)
            {
                Transform copy = CopyTransform(source, parent);
                Transform target = CityPedestrianHandProps.FindSocket(modelRoot, source.name);
                if (target != null && !source.name.StartsWith("SOCKET_", StringComparison.Ordinal))
                {
                    // Keep the initial rest translation across visibility graph
                    // rebuilds; the last visible frame may be mid-flight.
                    if (!freeRunRestPositions.TryGetValue(target, out Vector3 rest))
                        freeRunRestPositions.Add(target, rest = target.localPosition);
                    bindings.Add(new FreeRunBone(copy, target, rest));
                }
                for (int i = 0; i < source.childCount; ++i) CopyBones(source.GetChild(i), copy);
            }
            CopyBones(rootBone, CopyParent(rootBone.parent));
            freeRunBones = bindings.ToArray();
            freeRunPlayable = AnimationClipPlayable.Create(graph, freeRunClip);
            freeRunPlayable.SetApplyFootIK(false); freeRunPlayable.SetApplyPlayableIK(false);
            freeRunPlayable.SetSpeed(0);
            AnimationPlayableOutput.Create(graph, "Shared Run", sampler).SetSourcePlayable(freeRunPlayable);
        }

        private static Transform CopyTransform(Transform source, Transform parent)
        {
            Transform copy = new GameObject(source.name).transform;
            copy.SetParent(parent, false);
            copy.localPosition = source.localPosition;
            copy.localRotation = source.localRotation;
            copy.localScale = source.localScale;
            return copy;
        }

        /// <summary>Free hands only. Task/carry locomotion deliberately never selects Run.</summary>
        public void ApplyFreeLocomotion(float speed, float gaitCycles, Vector3? lookAt = null)
        {
            Initialize();
            if (!freeRunPlayable.IsValid())
                throw new InvalidOperationException("Free running needs the shared authored run clip.");
            weatherLayers.SetInputWeight(1, 0);
            contactLayers.SetInputWeight(1, 0);
            CurrentLocomotionSpeed = Mathf.Abs(speed);
            CurrentLocomotionCycle = Mathf.Max(0f, gaitCycles);
            float moving = Mathf.Clamp01(CurrentLocomotionSpeed / .75f);
            CurrentRunWeight = FreeRunWeight(speed) * moving;
            int idle = (int)VillageResidentAction.Idle, walk = (int)VillageResidentAction.Walk;
            for (int i = 0; i < clips.Length; ++i)
                mixer.SetInputWeight(i, i == idle ? 1f - moving : i == walk ? moving : 0f);
            // Both authored gaits start with left contact and share one
            // normalized phase, so the blend cannot swap the planted leg.
            SampleTime(idle, gaitCycles * clips[walk].length);
            SampleTime(walk, gaitCycles * clips[walk].length);
            freeRunPlayable.SetTime(Mathf.Repeat(CurrentLocomotionCycle, 1f) * freeRunClip.length);
            CurrentAction = moving > .01f ? VillageResidentAction.Walk : VillageResidentAction.Idle;
            CurrentActionSeconds = CurrentLocomotionCycle * clips[walk].length;
            graph.Evaluate(0);
            for (int i = 0; i < freeRunBones.Length; ++i)
            {
                FreeRunBone bone = freeRunBones[i];
                // Shared NpcHumanV2/Hero V2 joints have identical rest axes.
                // Preserve the NPC's length/scale and transfer only the clip's
                // authored displacement (principally the pelvis flight lift).
                bone.Target.localRotation = Quaternion.Slerp(bone.Target.localRotation,
                    bone.Source.localRotation, CurrentRunWeight);
                bone.Target.localPosition = Vector3.Lerp(bone.Target.localPosition,
                    bone.TargetRestPosition + bone.Source.localPosition - bone.SourceRestPosition, CurrentRunWeight);
            }
            ApplyLook(lookAt);
        }

        public float FreeLocomotionCyclesPerSecond(float speed)
        {
            if (freeRunClip == null)
                throw new InvalidOperationException("Free running needs the shared authored run clip.");
            speed = Mathf.Abs(speed);
            float walkRate = Mathf.Min(speed / .95f, 1.5f) / clips[(int)VillageResidentAction.Walk].length;
            float runRate = Mathf.Clamp01(speed / Player3DCharacterPresentation.FullRunSpeed) / freeRunClip.length;
            return Mathf.Lerp(walkRate, runRate, FreeRunWeight(speed));
        }

        private static float FreeRunWeight(float speed) =>
            Smooth(Mathf.InverseLerp(FreeRunStartSpeed, FreeRunFullSpeed, Mathf.Abs(speed)));

        private void ResetFreeLocomotion()
        {
            CurrentRunWeight = 0f;
            CurrentLocomotionSpeed = 0f;
            CurrentLocomotionCycle = 0f;
        }

        /// <summary>The tool's low grip needs its authored torso lean even while the legs walk.</summary>
        public void ApplyShovelLocomotion(float elapsedSeconds, float speedMetersPerSecond)
        {
            Apply(VillageResidentAction.ShovelHold, elapsedSeconds);
            contactLayers.SetInputWeight(1, Mathf.Clamp01(Mathf.Abs(speedMetersPerSecond) / .75f));
            doorWalkPlayable.SetTime(Mathf.Repeat(Mathf.Max(0, elapsedSeconds), clips[(int)VillageResidentAction.Walk].length));
            graph.Evaluate(0);
            // The owner now places the separate shovel and solves both hands
            // against its actual grips. No prop position is inferred here.
        }

        public void ApplyDoor(VillageResidentAction action, float elapsedSeconds, Vector3 worldHandle)
        {
            if (action != VillageResidentAction.DoorOpen && action != VillageResidentAction.DoorClose)
                throw new ArgumentException("A door requires its opening or closing action.", nameof(action));
            Apply(action, elapsedSeconds);
            float weight = elapsedSeconds < 1f ? Smooth(elapsedSeconds) :
                elapsedSeconds > 2.5f ? Smooth(3.5f - elapsedSeconds) : 1f;
            ApplyHandContacts(worldHandle, null, weight);
        }

        public void ApplyDoor(VillageResidentAction action, float elapsedSeconds, Vector3 worldHandle,
            float walkSpeed, float walkTime)
        {
            ApplyDoor(action, elapsedSeconds, worldHandle);
            contactLayers.SetInputWeight(1, Mathf.Clamp01(Mathf.Abs(walkSpeed) / .75f));
            doorWalkPlayable.SetTime(Mathf.Repeat(walkTime, clips[(int)VillageResidentAction.Walk].length));
            graph.Evaluate(0);
            float weight = elapsedSeconds < 1f ? Smooth(elapsedSeconds) :
                elapsedSeconds > 2.5f ? Smooth(3.5f - elapsedSeconds) : 1f;
            ApplyHandContacts(worldHandle, null, weight);
        }

        /// <summary>Apply after the ordinary frame; legs, ownership and both held grips survive the gust.</summary>
        public void ApplyGust(float elapsedSeconds, bool carrying)
        {
            Initialize();
            Vector3 right = rightGrip.position, left = leftGrip.position;
            Quaternion rightRotation = rightHand.rotation, leftRotation = leftHand.rotation;
            if (gustUsesHeldMask != carrying)
            {
                weatherLayers.SetLayerMaskFromAvatarMask(1, carrying ? heldGustMask : gustMask);
                gustUsesHeldMask = carrying;
            }
            float phase = Mathf.Clamp01(elapsedSeconds / GustDurationSeconds);
            float weight = Mathf.Sin(Mathf.PI * phase);
            weatherLayers.SetInputWeight(1, weight * weight);
            gustPlayable.SetTime(phase * clips[(int)VillageResidentAction.Gust].length);
            graph.Evaluate(0);
            if (carrying)
            {
                SolveArm(false, right, 1f, rightRotation);
                SolveArm(true, left, 1f, leftRotation);
            }
        }

        public bool ApplyHandContacts(Vector3? right, Vector3? left, float weight = 1f)
        {
            Initialize();
            bool matches = true;
            if (right.HasValue) matches &= SolveArm(false, right.Value, weight, rightHand.rotation);
            if (left.HasValue) matches &= SolveArm(true, left.Value, weight, leftHand.rotation);
            return matches;
        }

        private bool SolveArm(bool isLeft, Vector3 target, float weight, Quaternion handRotation)
        {
            Transform upper = isLeft ? leftUpperArm : rightUpperArm;
            Transform forearm = isLeft ? leftForearm : rightForearm;
            Transform hand = isLeft ? leftHand : rightHand;
            Transform grip = isLeft ? leftGrip : rightGrip;
            target = Vector3.Lerp(grip.position, target, Mathf.Clamp01(weight));
            Vector3 offset = Quaternion.Inverse(hand.rotation) * (grip.position - hand.position);
            Vector3 wrist = target - handRotation * offset;
            Vector3 shoulder = upper.position;
            float upperLength = Vector3.Distance(shoulder, forearm.position);
            float lowerLength = Vector3.Distance(forearm.position, hand.position);
            Vector3 delta = wrist - shoulder;
            if (delta.sqrMagnitude < .000001f) return false;
            float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(upperLength - lowerLength) + .001f,
                upperLength + lowerLength - .001f);
            Vector3 axis = delta.normalized;
            wrist = shoulder + axis * distance;
            Vector3 pole = transform.right * (isLeft ? -1f : 1f) - transform.forward * .15f;
            Vector3 bend = Vector3.ProjectOnPlane(pole, axis).normalized;
            if (bend.sqrMagnitude < .1f) bend = Vector3.ProjectOnPlane(transform.up, axis).normalized;
            float along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2f * distance);
            Vector3 elbow = shoulder + axis * along + bend * Mathf.Sqrt(Mathf.Max(0, upperLength * upperLength - along * along));
            upper.rotation = Quaternion.FromToRotation(forearm.position - shoulder, elbow - shoulder) * upper.rotation;
            forearm.rotation = Quaternion.FromToRotation(hand.position - forearm.position, wrist - forearm.position) * forearm.rotation;
            hand.rotation = handRotation;
            return Vector3.Distance(grip.position, target) <= .025f;
        }

        private AvatarMask CreateGustMask(bool lowerOnly = false, bool heldOnly = false)
        {
            Transform root = animator.transform;
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            var mask = new AvatarMask { name = lowerOnly ? "Village Door Steps" :
                heldOnly ? "Village Held Gust Head and Neck" : "Village Gust Upper Body", transformCount = transforms.Length };
            for (int index = 0; index < transforms.Length; ++index)
            {
                Transform bone = transforms[index];
                string path = bone == root ? string.Empty : bone.name;
                for (Transform parent = bone.parent; bone != root && parent != null && parent != root; parent = parent.parent)
                    path = parent.name + "/" + path;
                mask.SetTransformPath(index, path);
                bool lower = bone.name == "pelvis" || bone.name.StartsWith("thigh.", StringComparison.Ordinal) ||
                    bone.name.StartsWith("shin.", StringComparison.Ordinal) || bone.name.StartsWith("foot.", StringComparison.Ordinal);
                // A carried tool may require a substantial authored torso lean.
                // Replacing that lean with the empty-hand gust straightens the
                // back and can put its low grip outside the arm's reach. Tuck
                // the head and move its neck-bound cloth while retaining the
                // complete chest/shoulder/arm chain in the held action.
                mask.SetTransformActive(index, lowerOnly ? lower : bone.IsChildOf(heldOnly ? neck : spine));
            }
            return mask;
        }

        private static float Smooth(float value) { value = Mathf.Clamp01(value); return value * value * (3f - 2f * value); }

        /// <summary>Local pose of the separate shovel; origin is the bottom of its blade.</summary>
        public static Pose SampleShovelPose(VillageResidentAction action, float elapsedSeconds)
        {
            var rest = new Pose(new Vector3(0, 0, .48f), Quaternion.identity);
            var hold = new Pose(new Vector3(0, .14f, .40f), Quaternion.Euler(-12, 0, 0));
            if (action == VillageResidentAction.ShovelPickUp)
                return BlendPose(rest, hold, Smooth((elapsedSeconds - 1.5f) / 1.5f));
            if (action == VillageResidentAction.ShovelPutBack)
                return BlendPose(rest, hold, Smooth((1.5f - elapsedSeconds) / 1.5f));
            if (action != VillageResidentAction.ShovelWork) return hold;
            float t = Mathf.Repeat(Mathf.Max(0, elapsedSeconds), 4f);
            var lower = new Pose(new Vector3(0, .02f, .48f), Quaternion.identity);
            var push = new Pose(new Vector3(0, .02f, .57f), Quaternion.Euler(4, 0, 0));
            var lift = new Pose(new Vector3(-.12f, .23f, .38f), Quaternion.Euler(-12, 0, -18));
            if (t < .7f) return BlendPose(hold, lower, Smooth(t / .7f));
            if (t < 1.65f) return BlendPose(lower, push, Smooth((t - .7f) / .95f));
            if (t < 2.45f) return BlendPose(push, lift, Smooth((t - 1.65f) / .8f));
            if (t < 3.2f) return BlendPose(lift, hold, Smooth((t - 2.45f) / .75f));
            return hold;
        }

        private static Pose BlendPose(Pose first, Pose last, float weight) => new Pose(
            Vector3.Lerp(first.position, last.position, weight), Quaternion.Slerp(first.rotation, last.rotation, weight));

        private void SampleTime(int index, float seconds)
        {
            bool oneShot = index == (int)VillageResidentAction.Reach || index == (int)VillageResidentAction.Place ||
                index == (int)VillageResidentAction.DoorOpen || index == (int)VillageResidentAction.DoorClose ||
                index == (int)VillageResidentAction.ShovelPickUp || index == (int)VillageResidentAction.ShovelPutBack ||
                index == (int)VillageResidentAction.Gust ||
                (index >= (int)VillageResidentAction.RepairTakeTool &&
                 index != (int)VillageResidentAction.RepairWork && index != (int)VillageResidentAction.SewingWork);
            float time = oneShot ? Mathf.Clamp(seconds, 0, clips[index].length) : Mathf.Repeat(Mathf.Max(0, seconds), clips[index].length);
            playables[index].SetTime(time);
        }

        private void ApplyLook(Vector3? target)
        {
            if (!target.HasValue) return;
            Vector3 direction = transform.InverseTransformDirection(target.Value - head.position);
            if (direction.sqrMagnitude < .001f) return;
            float yaw = Mathf.Clamp(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, -35, 35);
            head.rotation = Quaternion.AngleAxis(yaw, transform.up) * head.rotation;
        }

        private void ApplyAppearance()
        {
            var block = new MaterialPropertyBlock();
            for (int i = 0; i < renderers.Length; ++i)
            {
                if (renderers[i] == null) continue;
                renderers[i].GetPropertyBlock(block);
                block.SetColor("_BaseColor", colors[i]); block.SetColor("_Color", colors[i]);
                block.SetTexture("_BaseMap", atlas); block.SetTexture("_MainTex", atlas);
                renderers[i].SetPropertyBlock(block); block.Clear();
            }
        }
        private void OnEnable() => ApplyAppearance();
        private void ReleaseGraph()
        {
            if (graph.IsValid()) graph.Destroy();
            if (freeRunSkeleton != null)
            {
                freeRunSkeleton.SetActive(false);
                if (Application.isPlaying) Destroy(freeRunSkeleton); else DestroyImmediate(freeRunSkeleton);
                freeRunSkeleton = null;
                freeRunBones = null;
            }
            if (gustMask != null) { if (Application.isPlaying) Destroy(gustMask); else DestroyImmediate(gustMask); gustMask = null; }
            if (heldGustMask != null) { if (Application.isPlaying) Destroy(heldGustMask); else DestroyImmediate(heldGustMask); heldGustMask = null; }
            if (doorWalkMask != null) { if (Application.isPlaying) Destroy(doorWalkMask); else DestroyImmediate(doorWalkMask); doorWalkMask = null; }
        }
        private void OnDisable() => ReleaseGraph();
        private void OnDestroy() => ReleaseGraph();
    }
}
