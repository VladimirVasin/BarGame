using System;
using UnityEngine;

namespace BarPromenade
{
    public enum HomeTeethBrushingPhase
    {
        Idle = 0,
        CameraToMirror = 1,
        Brushing = 2,
        Rinse = 3,
        CameraReturn = 4,
        Completed = 5
    }

    /// <summary>
    /// The mirror close-up: camera pushes to the mirror plane and
    /// looks back at the hero's face, the procedural right arm works
    /// the brush at the mouth, then a rinse beat and the camera walks
    /// home. Pure and EditMode-testable.
    /// </summary>
    public sealed class HomeTeethBrushingTimeline
    {
        public const float CameraToMirrorSeconds = 2.8f;
        public const float CameraHoldSeconds = 0.35f;
        public const float ArmRaiseStartSeconds = 2.0f;
        public const float ArmRaiseSeconds = 0.8f;
        public const float ArmLowerSeconds = 0.7f;
        public const float MinimumBrushSeconds = 4.0f;
        public const float AutomaticBrushSeconds = 8.0f;
        public const float FoamStartSeconds = 2.0f;
        public const float ScrubCueIntervalSeconds = 0.55f;
        public const float RinseSeconds = 2.5f;
        public const float RinseDipUpSeconds = 0.8f;
        public const float RinseDipDownStartSeconds = 1.7f;
        public const float CameraReturnSeconds = 2.2f;
        public const float FirstPourCueSeconds = 0.3f;
        public const float SecondPourCueSeconds = 1.8f;

        private float phaseElapsed;
        private float nextScrubCue;
        private int pourCuesConsumed;
        private float returnStartBlend = 1f;
        private float returnStartArm;

        public HomeTeethBrushingPhase Phase { get; private set; } =
            HomeTeethBrushingPhase.Idle;
        public float PhaseElapsed => phaseElapsed;
        public float BrushedSeconds { get; private set; }
        public bool ReachedMinimumBrush =>
            BrushedSeconds >= MinimumBrushSeconds;

        public float CameraBlend
        {
            get
            {
                switch (Phase)
                {
                    case HomeTeethBrushingPhase.CameraToMirror:
                        return SmoothStep01(
                            Mathf.InverseLerp(
                                CameraHoldSeconds,
                                CameraToMirrorSeconds,
                                phaseElapsed));
                    case HomeTeethBrushingPhase.Brushing:
                    case HomeTeethBrushingPhase.Rinse:
                        return 1f;
                    case HomeTeethBrushingPhase.CameraReturn:
                        // Scaled by where the interrupted camera
                        // actually was, so an abort mid-push never
                        // snaps to the mirror before walking home.
                        return returnStartBlend * (1f - SmoothStep01(
                            phaseElapsed / CameraReturnSeconds));
                    default:
                        return 0f;
                }
            }
        }

        /// <summary>0..1 weight of the procedural brushing arm.</summary>
        public float ArmWeight
        {
            get
            {
                switch (Phase)
                {
                    case HomeTeethBrushingPhase.CameraToMirror:
                        return Mathf.Clamp01(
                            (phaseElapsed - ArmRaiseStartSeconds) /
                            ArmRaiseSeconds);
                    case HomeTeethBrushingPhase.Brushing:
                        return 1f;
                    case HomeTeethBrushingPhase.Rinse:
                        return 1f - Mathf.Clamp01(
                            phaseElapsed / ArmLowerSeconds);
                    case HomeTeethBrushingPhase.CameraReturn:
                        return returnStartArm * (1f - Mathf.Clamp01(
                            phaseElapsed / ArmLowerSeconds));
                    default:
                        return 0f;
                }
            }
        }

        public bool FoamVisible =>
            (Phase == HomeTeethBrushingPhase.Brushing &&
             phaseElapsed >= FoamStartSeconds) ||
            (Phase == HomeTeethBrushingPhase.Rinse &&
             BrushedSeconds >= FoamStartSeconds &&
             phaseElapsed < RinseDipUpSeconds);

        /// <summary>0..1 camera look-dip toward the basin on rinse.</summary>
        public float RinseDip
        {
            get
            {
                if (Phase != HomeTeethBrushingPhase.Rinse)
                {
                    return 0f;
                }

                if (phaseElapsed < RinseDipUpSeconds)
                {
                    return SmoothStep01(
                        phaseElapsed / RinseDipUpSeconds);
                }

                if (phaseElapsed < RinseDipDownStartSeconds)
                {
                    return 1f;
                }

                return 1f - SmoothStep01(
                    (phaseElapsed - RinseDipDownStartSeconds) /
                    (RinseSeconds - RinseDipDownStartSeconds));
            }
        }

        public bool IsCompleted =>
            Phase == HomeTeethBrushingPhase.Completed;

        public void Begin()
        {
            Phase = HomeTeethBrushingPhase.CameraToMirror;
            phaseElapsed = 0f;
            nextScrubCue = ScrubCueIntervalSeconds;
            pourCuesConsumed = 0;
            returnStartBlend = 1f;
            returnStartArm = 0f;
            BrushedSeconds = 0f;
        }

        public void Advance(float deltaTime)
        {
            if (float.IsNaN(deltaTime) ||
                float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deltaTime));
            }

            if (Phase == HomeTeethBrushingPhase.Idle ||
                Phase == HomeTeethBrushingPhase.Completed)
            {
                return;
            }

            float safeDelta = Mathf.Max(0f, deltaTime);
            phaseElapsed += safeDelta;
            switch (Phase)
            {
                case HomeTeethBrushingPhase.CameraToMirror:
                    if (phaseElapsed >= CameraToMirrorSeconds)
                    {
                        SetPhase(HomeTeethBrushingPhase.Brushing);
                        nextScrubCue = ScrubCueIntervalSeconds;
                    }

                    break;
                case HomeTeethBrushingPhase.Brushing:
                    BrushedSeconds += safeDelta;
                    if (phaseElapsed >= AutomaticBrushSeconds)
                    {
                        SetPhase(HomeTeethBrushingPhase.Rinse);
                    }

                    break;
                case HomeTeethBrushingPhase.Rinse:
                    if (phaseElapsed >= RinseSeconds)
                    {
                        SetPhase(HomeTeethBrushingPhase.CameraReturn);
                    }

                    break;
                case HomeTeethBrushingPhase.CameraReturn:
                    if (phaseElapsed >= CameraReturnSeconds)
                    {
                        SetPhase(HomeTeethBrushingPhase.Completed);
                    }

                    break;
            }
        }

        /// <summary>
        /// Interrupts the ritual from any pre-wind-down phase: an
        /// abort during the camera push walks straight home, a stop
        /// during brushing still passes through the rinse beat. The
        /// minimum brush time gates only the stress reward, never the
        /// exit itself.
        /// </summary>
        public bool RequestFinish()
        {
            switch (Phase)
            {
                case HomeTeethBrushingPhase.CameraToMirror:
                    returnStartBlend = CameraBlend;
                    returnStartArm = ArmWeight;
                    SetPhase(HomeTeethBrushingPhase.CameraReturn);
                    return true;
                case HomeTeethBrushingPhase.Brushing:
                    SetPhase(HomeTeethBrushingPhase.Rinse);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>One-shot: a scrub cue is due during brushing.</summary>
        public bool ConsumeScrubCue()
        {
            if (Phase != HomeTeethBrushingPhase.Brushing ||
                phaseElapsed < nextScrubCue)
            {
                return false;
            }

            nextScrubCue += ScrubCueIntervalSeconds;
            return true;
        }

        /// <summary>One-shot: the tap/spit beats of the rinse.</summary>
        public bool ConsumePourCue()
        {
            if (Phase != HomeTeethBrushingPhase.Rinse)
            {
                return false;
            }

            if (pourCuesConsumed == 0 &&
                phaseElapsed >= FirstPourCueSeconds)
            {
                pourCuesConsumed = 1;
                return true;
            }

            if (pourCuesConsumed == 1 &&
                phaseElapsed >= SecondPourCueSeconds)
            {
                pourCuesConsumed = 2;
                return true;
            }

            return false;
        }

        public void Reset()
        {
            Phase = HomeTeethBrushingPhase.Idle;
            phaseElapsed = 0f;
            nextScrubCue = ScrubCueIntervalSeconds;
            pourCuesConsumed = 0;
            returnStartBlend = 1f;
            returnStartArm = 0f;
            BrushedSeconds = 0f;
        }

        private void SetPhase(HomeTeethBrushingPhase phase)
        {
            if (phase == HomeTeethBrushingPhase.CameraReturn &&
                Phase == HomeTeethBrushingPhase.Rinse)
            {
                returnStartBlend = 1f;
                returnStartArm = 0f;
            }

            Phase = phase;
            phaseElapsed = 0f;
        }

        private static float SmoothStep01(float amount)
        {
            float clamped = Mathf.Clamp01(amount);
            return clamped * clamped * (3f - 2f * clamped);
        }
    }

    /// <summary>
    /// Procedural additive brushing arm atop Idle: every LateUpdate it
    /// captures the animated right-arm pose, then CCD-steers the hand
    /// so the gripped toothbrush oscillates at the Mouth anchor, with
    /// a small counter-yaw of the head. Restores captured rotations
    /// when its weight is zero — the bus-driver/cashier idiom; the
    /// recorded standard exception lives in ai/architecture-notes.md.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(300)]
    public sealed class HomeTeethBrushingArmPose : MonoBehaviour
    {
        public const float OscillationHertz = 5.5f;
        public const float LateralAmplitudeMeters = 0.020f;
        public const float VerticalAmplitudeMeters = 0.008f;
        public const float ForwardOffsetMeters = 0.015f;
        public const float HeadCounterYawDegrees = 2.0f;
        private const int SolveIterations = 3;

        private Player3DAssetRegistry registry;
        private Transform upperArm;
        private Transform forearm;
        private Transform hand;
        private Transform head;
        private float elapsed;
        private bool isInitialized;

        public float Weight { get; set; }

        /// <summary>
        /// The CCD end effector — the toothbrush bristles, so the
        /// brush head works the mouth instead of the gripping fist.
        /// Falls back to the RightGrip socket when unset.
        /// </summary>
        public Transform Effector { get; set; }

        public void Initialize(
            Player3DAssetRegistry playerRegistry)
        {
            registry = playerRegistry != null
                ? playerRegistry
                : throw new ArgumentNullException(
                    nameof(playerRegistry));
            upperArm = ResolveBone(
                Player3DAnatomicalPart.RightUpperArm);
            forearm = ResolveBone(
                Player3DAnatomicalPart.RightForearm);
            hand = ResolveBone(Player3DAnatomicalPart.RightHand);
            head = ResolveBone(Player3DAnatomicalPart.Head);
            if (upperArm == null ||
                forearm == null ||
                hand == null ||
                head == null ||
                registry.Anchors.Mouth == null ||
                registry.Anchors.RightGrip == null)
            {
                throw new InvalidOperationException(
                    "The brushing arm requires the right-arm bones, " +
                    "head bone, Mouth and RightGrip anchors.");
            }

            isInitialized = true;
        }

        private Transform ResolveBone(Player3DAnatomicalPart part)
        {
            return registry.TryGetPart(part, out var binding) &&
                   binding != null
                ? binding.Bone
                : null;
        }

        private void LateUpdate()
        {
            if (!isInitialized || Weight <= 0.0001f)
            {
                return;
            }

            elapsed += Time.deltaTime;
            float wave = Mathf.Sin(
                elapsed * OscillationHertz * 2f * Mathf.PI);
            float vertical = Mathf.Cos(
                elapsed * OscillationHertz * 2f * Mathf.PI);
            Transform mouth = registry.Anchors.Mouth;
            Vector3 target = mouth.position +
                head.right * (wave * LateralAmplitudeMeters) +
                Vector3.up * (vertical * VerticalAmplitudeMeters) +
                (mouth.position - head.position).normalized *
                ForwardOffsetMeters;

            // Blend by solving fully, then slerping the bones back
            // toward their animated pose by (1 - weight).
            Quaternion upperBase = upperArm.rotation;
            Quaternion forearmBase = forearm.rotation;
            Quaternion headBase = head.rotation;
            Transform end = Effector != null
                ? Effector
                : registry.Anchors.RightGrip;
            for (int iteration = 0;
                 iteration < SolveIterations;
                 iteration++)
            {
                RotateTowards(forearm, end, target);
                RotateTowards(upperArm, end, target);
            }

            float clamped = Mathf.Clamp01(Weight);
            upperArm.rotation = Quaternion.Slerp(
                upperBase,
                upperArm.rotation,
                clamped);
            forearm.rotation = Quaternion.Slerp(
                forearmBase,
                forearm.rotation,
                clamped);
            head.rotation = Quaternion.Slerp(
                headBase,
                Quaternion.AngleAxis(
                    -wave * HeadCounterYawDegrees,
                    Vector3.up) *
                headBase,
                clamped);
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

    /// <summary>
    /// The mirror scene: the hero docks at the sink, the camera takes
    /// the mirror's place looking back at his face, the procedural arm
    /// works the toothbrush, foam appears, a rinse beat closes it —
    /// and once per game day the ritual relieves a little stress.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(260)]
    public sealed class HomeTeethBrushingInteraction :
        HomeBathroomSceneInteraction
    {
        public const string BrushPromptKey = "interaction.brush_teeth";
        public const string StopPromptKeyName =
            "interaction.stop_brushing";
        public const int StressRelief = 5;
        public const float BrushVisibleWeight = 0.15f;

        private static readonly Vector3 CameraPosition =
            new Vector3(2.075f, 1.66f, 3.79f);
        private static readonly Vector3 CameraLookAt =
            new Vector3(2.075f, 1.55f, 2.98f);
        private static readonly Vector3 RinseLookAt =
            new Vector3(2.075f, 1.30f, 3.30f);

        private readonly HomeTeethBrushingTimeline timeline =
            new HomeTeethBrushingTimeline();

        private HomeTeethBrushingArmPose armPose;
        private GameObject toothbrush;
        private Transform brushTip;
        private GameObject foam;
        private bool committed;

        public HomeTeethBrushingTimeline Timeline => timeline;
        public HomeTeethBrushingArmPose ArmPose => armPose;
        public GameObject Toothbrush => toothbrush;
        public override string PromptKey =>
            OwnsScene ? string.Empty : BrushPromptKey;

        protected override string StopPromptKey => StopPromptKeyName;
        protected override Vector3 CameraLocalPosition =>
            CameraPosition;
        protected override Vector3 CameraLocalLookAt =>
            Vector3.Lerp(
                CameraLookAt,
                RinseLookAt,
                timeline.RinseDip);
        protected override float CameraFieldOfView => 36f;
        protected override float CameraBlend => timeline.CameraBlend;
        protected override float CameraDriftWeight => 0.8f;
        protected override bool SceneCompleted =>
            timeline.IsCompleted;
        protected override bool StopPromptVisible =>
            timeline.Phase == HomeTeethBrushingPhase.Brushing;

        public void Initialize(HomeInteriorRoot homeRoot)
        {
            if (homeRoot == null)
            {
                throw new ArgumentNullException(nameof(homeRoot));
            }

            // The dock keeps the capsule (radius + skin width) clear
            // of the sink basin collider at z 3.25 — the guided walk
            // demands an exact planar arrival.
            InitializeScene(
                homeRoot,
                new Vector3(2.075f, 0f, 2.78f),
                Quaternion.LookRotation(
                    Vector3.forward,
                    Vector3.up),
                new Vector3(2.075f, 0f, 2.50f),
                Quaternion.LookRotation(Vector3.back, Vector3.up),
                new Vector3(2.075f, 0f, 2.78f));
        }

        protected override void OnSceneBegin()
        {
            if (!(Home.Player.Visual is
                    Player3DCharacterPresentation visual))
            {
                CancelScene();
                return;
            }

            if (armPose == null)
            {
                armPose = gameObject
                    .AddComponent<HomeTeethBrushingArmPose>();
                armPose.Initialize(visual.Registry);
            }

            EnsureProps(visual.Registry);
            armPose.Effector = brushTip;
            committed = false;
            timeline.Begin();
        }

        protected override void OnSceneAdvance(float deltaTime)
        {
            timeline.Advance(deltaTime);
            if (armPose != null)
            {
                armPose.Weight = timeline.ArmWeight;
            }

            if (toothbrush != null)
            {
                toothbrush.SetActive(
                    timeline.ArmWeight > BrushVisibleWeight);
            }

            if (foam != null)
            {
                foam.SetActive(timeline.FoamVisible);
            }

            if (timeline.ConsumeScrubCue())
            {
                Home.Audio?.TryPlay(
                    RetroSfxId.TeethBrushScrub,
                    Home.transform.TransformPoint(
                        new Vector3(2.075f, 1.5f, 3.0f)));
            }

            if (timeline.ConsumePourCue())
            {
                Home.Audio?.TryPlay(
                    RetroSfxId.Pour,
                    Home.transform.TransformPoint(
                        new Vector3(2.075f, 0.9f, 3.42f)));
            }
        }

        protected override bool OnRequestStop()
        {
            return timeline.RequestFinish();
        }

        protected override void OnSceneCommit()
        {
            // An aborted ritual (interrupted before the minimum
            // brush time) ends gracefully but earns nothing and does
            // not consume the daily gate.
            if (committed || !timeline.ReachedMinimumBrush)
            {
                return;
            }

            committed = true;
            GameSessionState.TryCommitTeethBrushingRelief(
                StressRelief);
        }

        protected override void OnSceneRestore()
        {
            timeline.Reset();
            if (armPose != null)
            {
                armPose.Weight = 0f;
            }

            if (toothbrush != null)
            {
                toothbrush.SetActive(false);
            }

            if (foam != null)
            {
                foam.SetActive(false);
            }
        }

        /// <summary>
        /// The toothbrush rides the RightGrip socket and the foam the
        /// Mouth anchor — the cigarette pattern, including the inverse
        /// of the FBX bone scale.
        /// </summary>
        private void EnsureProps(Player3DAssetRegistry registry)
        {
            if (toothbrush == null)
            {
                Transform grip = registry.Anchors.RightGrip;
                toothbrush = new GameObject("Player Toothbrush");
                toothbrush.transform.SetParent(grip, false);
                toothbrush.transform.localScale =
                    InverseScale(grip.lossyScale);
                RuntimePrimitiveFactory.CreateCylinder(
                    "Handle",
                    toothbrush.transform,
                    new Vector3(0f, 0.055f, 0f),
                    new Vector3(0.012f, 0.11f, 0.012f),
                    new Color(0.55f, 0.15f, 0.12f),
                    false);
                RuntimePrimitiveFactory.CreateBox(
                    "Bristles",
                    toothbrush.transform,
                    new Vector3(0f, 0.115f, 0.008f),
                    new Vector3(0.014f, 0.025f, 0.010f),
                    new Color(0.75f, 0.75f, 0.68f),
                    false);
                var tip = new GameObject("Brush Tip");
                tip.transform.SetParent(toothbrush.transform, false);
                tip.transform.localPosition =
                    new Vector3(0f, 0.115f, 0.008f);
                brushTip = tip.transform;
                toothbrush.SetActive(false);
            }

            if (foam == null)
            {
                Transform mouth = registry.Anchors.Mouth;
                foam = new GameObject("Player Brushing Foam");
                foam.transform.SetParent(mouth, false);
                foam.transform.localScale =
                    InverseScale(mouth.lossyScale);
                Vector3[] blobs =
                {
                    new Vector3(-0.012f, -0.004f, 0.012f),
                    new Vector3(0.011f, -0.006f, 0.010f),
                    new Vector3(0f, 0.006f, 0.014f)
                };
                for (int index = 0; index < blobs.Length; index++)
                {
                    RuntimePrimitiveFactory.CreateBox(
                        $"Foam {index + 1}",
                        foam.transform,
                        blobs[index],
                        new Vector3(0.014f, 0.011f, 0.011f),
                        new Color(0.80f, 0.80f, 0.75f),
                        false);
                }

                foam.SetActive(false);
            }
        }

        private static Vector3 InverseScale(Vector3 scale)
        {
            return new Vector3(
                Mathf.Approximately(scale.x, 0f) ? 1f : 1f / scale.x,
                Mathf.Approximately(scale.y, 0f) ? 1f : 1f / scale.y,
                Mathf.Approximately(scale.z, 0f) ? 1f : 1f / scale.z);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (toothbrush != null)
            {
                Destroy(toothbrush);
            }

            if (foam != null)
            {
                Destroy(foam);
            }
        }
    }
}
