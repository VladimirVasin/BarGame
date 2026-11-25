using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class CityBalconySmokerPresentation : MonoBehaviour
    {
        public const string SmokeLoopClipName = "SmokeLoop";
        public const float MaximumStepSeconds = .1f;
        public const string MouthSocketName = "SOCKET_Mouth";
        public const string CigaretteSocketName = CityPedestrianHandProps.CigaretteRightSocketName;
        private const float OpenSeconds = VillageResidentPresentation.DoorDurationSeconds;
        private const float ApproachSeconds = 1.65f, CrossingSeconds = 2.2f, InsideTurnSeconds = 1.55f;
        private const float ClosingSeconds = 1.15f;
        private const float SettleSeconds = .68f;
        private enum Visit { ComingOut, AtRail, GoingIn, Indoors }
        private Visit stage;
        private float elapsed, smokingSeconds;
        private double burstTimeSeconds;
        private CityPedestrianPresentation body;
        private VillageResidentPresentation resident;
        private bool samplingDoor;
        internal bool OperatingDoor => samplingDoor;
        internal float DoorFraction => door.Fraction;
        internal Vector3 DoorHandle => door.Handle;
        private CityRailRestPose rest;
        private CityBalconyDoor door;
        private Vector3 threshold;
        private GameObject poseDriverRoot;
        private Transform driverMouth, driverFilter;
        private AnimationClip activeClip;
        private PlayerAnimatedInteractionDefinition animationDefinition;
        private PlayableGraph graph;
        private AnimationClipPlayable playable;

        public bool IsInitialized { get; private set; }
        public CityBalconySmokerDescriptor Descriptor { get; private set; }
        public CityPedestrianAssetRegistry Registry => body.Registry;
        public AnimationClip ActiveClip => activeClip;
        public PlayerAnimatedInteractionDefinition AnimationDefinition => animationDefinition;
        public HomeBalconySmokingExhaleEffect ExhaleEffect => rest?.Exhale;
        public CityPedestrianHandPropRegistry HeldCigarette => rest?.Cigarette;
        public IReadOnlyList<Renderer> CigaretteRenderers => HeldCigarette != null
            ? HeldCigarette.Renderers : Array.Empty<Renderer>();
        public double LoopTimeSeconds { get; private set; }
        public int CurrentLoopFrame { get; private set; }
        public float CurrentClipProgress01 { get; private set; }
        public int SmokeBurstCount => ExhaleEffect != null ? ExhaleEffect.ManualBurstCount : 0;
        internal bool AtRail => stage == Visit.AtRail;
        internal bool Finished => stage == Visit.Indoors;

        internal void Initialize(CityPedestrianPresentation actor, CityBalconySmokerDescriptor descriptor,
            AnimationClip smokeLoop, Avatar heroAvatar, PlayerAnimatedInteractionDefinition definition, bool poseIsParentLocal)
        {
            body = actor;
            Descriptor = descriptor;
            if (actor.Registry.DesignId != descriptor.ArchetypeDesignId ||
                actor.GetComponent<CityPedestrianDefaultNpcBody>().CharacterId != descriptor.CharacterId)
                throw new InvalidOperationException("Balcony resident lacks its assigned default appearance.");
            var facing = Quaternion.LookRotation(descriptor.Facing);
            if (poseIsParentLocal) transform.SetLocalPositionAndRotation(descriptor.Position, facing);
            else transform.SetPositionAndRotation(descriptor.Position, facing);
            resident = body.GetComponent<CityPedestrianDefaultNpcBody>().Character;
            body.UseAuthoredFloor = true;
            body.Registry.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            // The visit owns these skins through the rail-to-walk and handle-to-walk handoffs.
            foreach (Renderer part in body.Registry.Renderers)
                if (part is SkinnedMeshRenderer skin) skin.forceMatrixRecalculationPerRender = true;
            threshold = new Vector3(descriptor.DoorOffset.x, 0f, descriptor.DoorOffset.y);
            door = new CityBalconyDoor(transform, threshold, descriptor);
            activeClip = smokeLoop;
            animationDefinition = definition;
            BuildGraph(CreatePoseDriver(heroAvatar));
            rest = new CityRailRestPose(body.Registry);
            rest.PrepareSmoking();
            HeldCigarette.ApplyPaletteVariant(descriptor.PaletteVariant);
            HeldCigarette.gameObject.SetActive(false);
            burstTimeSeconds = CalculateFrameStartSeconds(definition, HomeBalconySmokingExhaleEffect.ExhaleStartLoopFrame);
            smokingSeconds = (float)definition.LoopDurationSeconds * (descriptor.AnimationPhase01 < .5f ? 2 : 3);
            IsInitialized = true;
            StartVisit();
        }

        private void StartVisit()
        {
            stage = Visit.ComingOut;
            elapsed = 0f;
            body.transform.SetLocalPositionAndRotation(HandleDock(true, 0f), Quaternion.identity);
            body.gameObject.SetActive(true);
            NpcFootstepSources.Unregister(body.transform);
            body.Advance(0f, false, true);
            door.SetOpen(0f);
        }

        public void Advance(float deltaTime)
        {
            if (!IsInitialized || Finished || !(deltaTime > 0f) || !isActiveAndEnabled) return;
            float step = Mathf.Min(deltaTime, MaximumStepSeconds);
            elapsed += step;
            if (AtRail)
            {
                SampleSmoke(step);
                if (elapsed >= smokingSeconds + SettleSeconds)
                {
                    rest.Release();
                    stage = Visit.GoingIn;
                    elapsed = 0f;
                    body.Advance(0f, false, true);
                }
                return;
            }

            if (stage == Visit.ComingOut) ComeOutside(step);
            else GoInside(step);
        }

        private Vector3 OutsideDock => threshold + new Vector3(.40f, 0f, .50f);
        private Vector3 InsideDock => threshold + new Vector3(.07f, 0f, -.72f);

        private void ComeOutside(float step)
        {
            if (elapsed <= OpenSeconds)
            {
                OperateDoor(elapsed, false, step);
                return;
            }
            EndDoorAction();
            float walk = elapsed - OpenSeconds;
            Vector3 at = Vector3.Lerp(HandleDock(true, 1f), OutsideDock, Mathf.Clamp01(walk / .9f));
            at = Vector3.Lerp(at, Vector3.zero, Mathf.InverseLerp(.9f, 1.65f, walk));
            Move(at, Vector3.forward, step);
            door.SetOpen(1f - Ease(walk, 1.05f, 2.2f));
            if (walk < 2.35f) return;
            stage = Visit.AtRail;
            elapsed = 0f;
            BeginLean();
        }

        private void GoInside(float step)
        {
            if (elapsed < ApproachSeconds)
            {
                // Straighten completely at the rail, then turn toward the closed door.
                Vector3 dock = HandleDock(false, 0f);
                Move(Vector3.Lerp(Vector3.zero, dock, Ease(elapsed, .36f, 1.36f)), threshold - dock, step);
                return;
            }
            float opening = elapsed - ApproachSeconds;
            if (opening <= OpenSeconds)
            {
                OperateDoor(opening, true, step);
                return;
            }
            EndDoorAction();
            float walk = opening - OpenSeconds;
            if (walk < CrossingSeconds + InsideTurnSeconds)
            {
                door.SetOpen(1f);
                Vector3 at = Vector3.Lerp(HandleDock(false, 1f), InsideDock, Mathf.Clamp01(walk / CrossingSeconds));
                at = Vector3.Lerp(at, InsideDock + Vector3.right * 1.04f,
                    Mathf.InverseLerp(CrossingSeconds, CrossingSeconds + InsideTurnSeconds, walk));
                Move(at, Vector3.back, step);
                return;
            }
            // The jamb hides the whole resident inside the room before the leaf closes.
            if (body.gameObject.activeSelf) body.gameObject.SetActive(false);
            float close = (walk - CrossingSeconds - InsideTurnSeconds) / ClosingSeconds;
            door.SetOpen(1f - Mathf.SmoothStep(0f, 1f, close));
            if (close >= 1f) stage = Visit.Indoors;
        }

        private void OperateDoor(float seconds, bool fromDeck, float step)
        {
            float fraction = Ease(seconds, VillageResidentPresentation.DoorGripStartSeconds,
                VillageResidentPresentation.DoorGripEndSeconds);
            door.SetOpen(fraction);
            Vector3 dock = HandleDock(!fromDeck, fraction);
            Vector3 hand = transform.InverseTransformPoint(door.Handle);
            float travel = Vector3.Distance(dock, body.transform.localPosition);
            Move(dock, new Vector3(hand.x - dock.x, 0f, hand.z - dock.z), step, faceTarget: true);
            // The same authored clip and moving right-hand contact as the ordinary
            // residents at the kiosk and glass-return workshop.
            body.SampleWalkingDoor(resident, seconds, fromDeck ? -travel : travel, step);
            resident.ApplySampledDoorContact(seconds, door.Handle);
            samplingDoor = true;
        }

        private Vector3 HandleDock(bool inside, float fraction)
        {
            // Follow the latch from beside the swept leaf, with room behind the feet.
            Vector3 hinge = threshold + new Vector3(-.365f, 0f, .06f);
            if (inside)
            {
                Vector3 latch = Quaternion.Euler(0f, -95f * fraction, 0f) * new Vector3(.665f, 0f, .034f);
                return hinge + latch + new Vector3(Mathf.Lerp(-.25f, .39f, fraction), 0f, -.51f);
            }
            float angle = Mathf.Lerp(23f, 63f, fraction) * Mathf.Deg2Rad;
            return hinge + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * .96f;
        }

        private void EndDoorAction()
        {
            if (!samplingDoor) return;
            samplingDoor = false;
            body.ClearAuthoredAction();
            body.Advance(0f, false, true);
        }

        private void Move(Vector3 at, Vector3 facing, float step, bool faceTarget = false)
        {
            Vector3 displacement = at - body.transform.localPosition;
            bool walking = displacement.sqrMagnitude > .000001f;
            Vector3 heading = walking && !faceTarget ? displacement : facing;
            if (heading.sqrMagnitude > .000001f)
                body.transform.localRotation = Quaternion.RotateTowards(body.transform.localRotation,
                    Quaternion.LookRotation(heading), 240f * step);
            body.transform.localPosition = at;
            if (!faceTarget) body.AdvanceTravelled(step, walking, transform.TransformVector(displacement));
        }

        private static float Ease(float seconds, float start, float end) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, seconds));

        private CityRailRestSpot Rail => CityRailRestSpot.AtLevelRail(transform.position, transform.forward,
            transform.position + transform.forward * Descriptor.RailReach + Vector3.up * CityBalconySmokerDescriptor.RailHeight);

        private void BeginLean()
        {
            body.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            body.Advance(0f, false, true);
            rest.Bind(Rail, true, Descriptor.AnimationPhase01);
            SampleSmoke(0f);
        }

        private void SampleSmoke(float step)
        {
            double duration = animationDefinition.LoopDurationSeconds;
            float sampled = Mathf.Min(elapsed, smokingSeconds);
            LoopTimeSeconds = sampled >= smokingSeconds ? duration : sampled % duration;
            CurrentLoopFrame = ResolveLoopFrame(animationDefinition, LoopTimeSeconds);
            CurrentClipProgress01 = ResolveClipProgress(animationDefinition, LoopTimeSeconds);
            playable.SetTime(activeClip.length * CurrentClipProgress01);
            graph.Evaluate(0f);
            // The Hero clip supplies the actual draw and authored holds. Match its
            // reach on the resident's own proportions, with the filter touching the lips.
            float distance = Vector3.Distance(driverMouth.position, driverFilter.position);
            float lift = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.43f, .085f, distance));
            float weight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / SettleSeconds)) *
                (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - smokingSeconds) / SettleSeconds)));
            rest.ApplyBalcony(Rail, elapsed, lift, weight);
            if (step > 0f && elapsed <= smokingSeconds &&
                Math.Floor((elapsed - burstTimeSeconds) / duration) > Math.Floor((elapsed - step - burstTimeSeconds) / duration))
                ExhaleEffect.EmitManualBurst();
        }

        private void LateUpdate() => Advance(Time.deltaTime);

        private void OnEnable()
        {
            if (!IsInitialized) return;
            if (Finished) StartVisit();
            else if (AtRail) BeginLean();
        }

        private void OnDisable()
        {
            rest?.Release();
            if (door != null) door.SetOpen(0f);
            if (ExhaleEffect != null) ExhaleEffect.StopAndClear();
        }

        public void Shutdown()
        {
            if (!IsInitialized && !graph.IsValid()) return;
            IsInitialized = false;
            if (door != null) door.SetOpen(0f);
            rest?.Release();
            if (ExhaleEffect != null) ExhaleEffect.StopAndClear();
            if (graph.IsValid()) graph.Destroy();
            if (poseDriverRoot != null) CityPedestrianResources.DestroyObject(poseDriverRoot);
            if (body != null) body.Shutdown();
        }

        private void OnDestroy() => Shutdown();

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

        private Animator CreatePoseDriver(Avatar heroAvatar)
        {
            var poseDriverRegistry = Player3DResources.Instantiate(transform);
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
            driverMouth = CityPedestrianHandProps.FindSocket(poseDriverRegistry.ModelRoot, MouthSocketName);
            driverFilter = CityPedestrianHandProps.FindSocket(poseDriverRegistry.ModelRoot, CigaretteSocketName);
            return driverAnimator;
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

        internal static float ResolveClipProgress(
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

    }
}
