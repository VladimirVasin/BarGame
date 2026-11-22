using System;
using UnityEngine;

namespace BarPromenade
{
    [DefaultExecutionOrder(365)]
    [DisallowMultipleComponent]
    internal sealed class PlayerWatchController : MonoBehaviour
    {
        internal const float TransitionSeconds = .84f;
        private const float EyeFieldOfView = 55f;
        private const float CuffTravel = .067f;

        private PlayerRuntime player;
        private Player3DCharacterPresentation visual;
        private PlayerAnimatedInteractionController interaction;
        private PlayerScarfController scarf;
        private PlayerCrowbarCombatController combat;
        private Camera view;
        private PlayerCameraFollow follow;
        private NpcHandPose fingers;
        private Transform head, leftEye, rightEye;
        private Transform leftUpper, leftForearm, leftHand;
        private Transform rightUpper, rightForearm, rightHand;
        private PlayerWatchPresentation watch;
        private AnatomicalHandFrame leftFrame, pinchFrame;
        private IDisposable cameraLoan;
        private Player3DHeadVisibility headVisibility;
        private Pose startView;
        private float startLens, startNear;
        private Vector3 shoulderWaypoint;
        private Vector2 look;
        private int entryFrame;
        private bool opening, terminalFrame;
        private float progress;
        private Action restore;
        private readonly RotationSnapshot[] bones = new RotationSnapshot[7];
        private bool posed;

        internal bool IsOpen => cameraLoan != null && progress >= 1f;
        internal float Progress => progress;
        internal Transform Dial => watch?.Dial;
        internal int DisplayedMinute => watch?.DisplayedMinute ?? -1;
        internal float CuffAmount => watch?.CuffAmount ?? 0f;
        internal float PinchError { get; private set; }

        internal static void Install(PlayerRuntime player, Camera camera, PlayerAnimatedInteractionController interaction)
        {
            if (camera == null || !(player.Visual is Player3DCharacterPresentation visual)) return;
            var owner = player.GameObject.AddComponent<PlayerWatchController>();
            owner.player = player;
            owner.visual = visual;
            owner.view = camera;
            owner.interaction = interaction;
            owner.scarf = player.GameObject.GetComponent<PlayerScarfController>();
            owner.combat = player.GameObject.GetComponent<PlayerCrowbarCombatController>();
            owner.restore = owner.Restore;
        }

        private void Update()
        {
            UndoPose();
            if (cameraLoan != null)
            {
                if (view == null || follow == null || !follow.isActiveAndEnabled ||
                    SceneTransitionService.IsTransitioning || player.Ragdoll.IsActive ||
                    (!GameTimeScaleRuntime.IsPaused && !player.Motor.IsGrounded) ||
                    Player3DBathingAppearance.IsActive)
                {
                    interaction.CancelActiveInteraction();
                    return;
                }
                visual.SampleInteractionNeutralPose();
                if (GameCursor.ApplicationFocused &&
                    GameInput.WasPressed(GameInputAction.CheckWatch, GameInputContext.Contextual))
                    opening = !opening;
                return;
            }

            if (watch != null)
                watch.SetWorn(!Player3DBathingAppearance.IsActive &&
                              !player.PresentationVisibility.RenderersHidden);
            if (GameCursor.ApplicationFocused &&
                GameInput.WasPressed(GameInputAction.CheckWatch, GameInputContext.Gameplay))
                TryBegin();
        }

        private void TryBegin()
        {
            if (view == null || !view.TryGetComponent(out follow) || follow.PresentationSuspended ||
                !follow.OrbitInputEnabled || follow.FreeAimActive || follow.TargetLockActive ||
                player.Ragdoll.IsActive || !player.Motor.IsGrounded || !player.Motor.InputEnabled ||
                player.Motor.ExternalPushActive || interaction.IsActive || visual.InteractionHandoffLocked ||
                player.PresentationVisibility.RenderersHidden || Player3DBathingAppearance.IsActive ||
                GameSessionState.IsRidingAVehicle || !Player3DHeadVisibility.IsHeadDrawn(visual.Registry) ||
                PlayerVodkaBottleController.IsHandOccupied(gameObject) || (scarf != null && scarf.IsGestureActive) ||
                (combat != null && (combat.HasPendingAction || combat.IsPreparingInteraction ||
                    (combat.Actor != null && (combat.Actor.HasWeaponInHand || combat.Actor.IsSwitchingWeapon))))) return;

            if (watch == null && !PrepareRig()) return;
            if (!watch.CanPullCuff ||
                !fingers.TryCaptureGrip(this, false, NpcHandGripKind.Cigarette, exclusive: true)) return;
            if (!fingers.TryCaptureGrip(this, true, NpcHandGripKind.Cylindrical, exclusive: true))
            {
                fingers.ReleaseGrip(this, false);
                return;
            }
            if (!interaction.TryBeginStationary(follow, restore))
            {
                ReleaseHands();
                return;
            }

            startView = new Pose(view.transform.position, view.transform.rotation);
            startLens = view.fieldOfView;
            startNear = view.nearClipPlane;
            cameraLoan = follow.SuspendPresentation();
            Vector3 eyes = EyePosition();
            Vector3 offset = startView.position - eyes;
            float side = Vector3.Dot(offset, transform.right) >= 0f ? 1f : -1f;
            Vector3 aroundHead = eyes + side * .31f * transform.right + .07f * transform.up;
            shoulderWaypoint = follow.ConstrainContextualCamera(eyes, aroundHead);
            opening = true;
            entryFrame = Time.frameCount;
            watch.RefreshTime();
        }

        private bool PrepareRig()
        {
            var registry = visual.Registry;
            if (!registry.TryGetComponent(out fingers)) return false;
            leftUpper = Bone(Player3DAnatomicalPart.LeftUpperArm);
            leftForearm = Bone(Player3DAnatomicalPart.LeftForearm);
            leftHand = Bone(Player3DAnatomicalPart.LeftHand);
            rightUpper = Bone(Player3DAnatomicalPart.RightUpperArm);
            rightForearm = Bone(Player3DAnatomicalPart.RightForearm);
            rightHand = Bone(Player3DAnatomicalPart.RightHand);
            head = registry.Anchors.Head;
            if (head == null) return false;
            leftEye = head.Find("face.eye.L");
            rightEye = head.Find("face.eye.R");
            if (leftUpper == null || leftForearm == null || leftHand == null || rightUpper == null ||
                rightForearm == null || rightHand == null) return false;
            leftFrame = fingers.GetAnatomicalFrame(true);
            pinchFrame = fingers.GetAnatomicalFrame(false, NpcHandGripKind.Cigarette);
            watch = PlayerWatchPresentation.Create(visual, leftForearm, leftHand,
                -(leftHand.rotation * leftFrame.PalmNormalInHand), CuffTravel);
            return watch != null;
        }

        private Transform Bone(Player3DAnatomicalPart part) =>
            visual.Registry.TryGetPart(part, out var binding) ? binding.Bone : null;

        private Vector3 EyePosition() => leftEye != null && rightEye != null
            ? (leftEye.position + rightEye.position) * .5f + transform.forward * .026f
            : head.position + transform.up * .12f + transform.forward * .12f;

        private void LateUpdate()
        {
            if (cameraLoan == null) return;
            if (terminalFrame && !opening)
            {
                interaction.CancelActiveInteraction();
                return;
            }
            if (Time.frameCount <= entryFrame) return;

            if (!PauseMenuController.IsAnyPaused && !GameTimeScaleRuntime.IsPaused)
            {
                float target = opening ? 1f : 0f;
                progress = Mathf.MoveTowards(progress, target, Time.unscaledDeltaTime / TransitionSeconds);
                Vector2 delta = DebugFreeCamera.BlocksWorldInput ? Vector2.zero : follow.SampleMouseLookDegrees();
                look.x = Mathf.Clamp(look.x + delta.x, -65f, 65f);
                look.y = Mathf.Clamp(look.y + delta.y, -52f, 40f);
            }
            terminalFrame = !opening && progress <= 0f;
            Vector3 eyes = EyePosition();
            if (progress > 0f) ApplyGesture(eyes);
            watch.RefreshTime();
            if (DebugFreeCamera.IsAnyActive) ShowHead();
            else ApplyCamera(eyes);
        }

        private void ApplyGesture(Vector3 eyes)
        {
            Quaternion facing = transform.rotation;
            Vector3 up = transform.up, right = transform.right, forward = transform.forward;
            CapturePose();
            float lift = Ease(0f, .73f);
            Vector3 target = eyes + facing * new Vector3(.073f, -.25f, .245f);
            Vector3 elbow = leftUpper.position + facing * new Vector3(-.23f, -.15f, .27f);
            LimbTwoBoneIk.Solve(leftUpper, leftForearm, leftHand, target, leftHand.rotation,
                elbow, lift, LimbTwoBoneIk.DefaultReachFraction, false);

            Vector3 axis = (leftHand.position - leftForearm.position).normalized;
            Vector3 toEye = Vector3.ProjectOnPlane(eyes - watch.Dial.position, axis).normalized;
            float wristRoll = Vector3.SignedAngle(watch.Dial.forward, toEye, axis);
            leftForearm.rotation = Quaternion.AngleAxis(wristRoll * lift, axis) * leftForearm.rotation;
            Quaternion handTarget = leftFrame.ResolveHandRotation(-toEye,
                (axis + right * .24f - up * .16f).normalized);
            leftHand.rotation = Quaternion.Slerp(leftHand.rotation, handTarget, lift);
            fingers.SetOwnedGrip(this, true, lift * .60f);

            float pull = Ease(.38f, .73f);
            watch.PullCuff(pull);
            float reach = Ease(.08f, .38f) * (1f - Ease(.73f, 1f));
            float pinch = Ease(.26f, .38f) * (1f - Ease(.73f, .85f));
            fingers.SetOwnedGrip(this, false, pinch);
            PinchError = 0f;
            if (reach > 0f)
            {
                Vector3 outward = (watch.Dial.forward * .65f - watch.Dial.up * .76f).normalized;
                Vector3 lip = leftHand.position - axis * (CuffTravel * pull) + outward * (.035f + .006f * pull);
                Vector3 tangent = Vector3.Cross(axis, outward);
                Pose rightWrist = pinchFrame.ResolveCylinderPose(lip, tangent, -outward);
                Vector3 hint = rightUpper.position + facing * new Vector3(.25f, -.24f, .16f);
                LimbTwoBoneIk.Solve(rightUpper, rightForearm, rightHand, rightWrist.position, rightWrist.rotation,
                    hint, reach, LimbTwoBoneIk.DefaultReachFraction, true);
                PinchError = Vector3.Distance(lip, rightHand.position + rightHand.rotation * pinchFrame.CylinderCentreInHand);
            }

            Vector3 gaze = watch.Dial.position - eyes;
            Quaternion glance = Quaternion.AngleAxis(look.x, up) *
                Quaternion.AngleAxis(look.y, right) * Quaternion.LookRotation(gaze, up);
            head.rotation = Quaternion.Slerp(head.rotation, glance * Quaternion.Inverse(facing) * head.rotation, lift);
        }

        private void ApplyCamera(Vector3 eyes)
        {
            float weight = Ease(0f, 1f);
            Vector3 control = Vector3.Lerp(startView.position, shoulderWaypoint, .70f);
            Vector3 midA = Vector3.Lerp(startView.position, control, weight);
            Vector3 midB = Vector3.Lerp(control, shoulderWaypoint, weight);
            Vector3 midC = Vector3.Lerp(shoulderWaypoint, eyes, weight);
            Vector3 blended = Vector3.Lerp(Vector3.Lerp(midA, midB, weight), Vector3.Lerp(midB, midC, weight), weight);
            // Fixed rooms may deliberately have their camera beyond a cutaway wall.
            // Keep their established shot at the endpoint and sweep the near-body leg.
            if (!follow.FixedPoseActive && weight > 0f)
                blended = follow.ConstrainContextualCamera(eyes, blended);
            else if (weight >= .7f)
                blended = Vector3.Lerp(blended, follow.ConstrainContextualCamera(eyes, blended),
                    Mathf.SmoothStep(0f, 1f, (weight - .7f) / .3f));

            Vector3 frameCentre = watch.Dial.position + transform.up * .105f + transform.right * .050f;
            Quaternion eyeView = Quaternion.AngleAxis(look.x, transform.up) *
                Quaternion.LookRotation(frameCentre - eyes, transform.up) * Quaternion.Euler(look.y, 0f, 0f);
            view.transform.SetPositionAndRotation(blended, Quaternion.Slerp(startView.rotation, eyeView, weight));
            view.fieldOfView = Mathf.Lerp(startLens, EyeFieldOfView, weight);
            view.nearClipPlane = Mathf.Lerp(startNear, Mathf.Min(startNear, .045f), weight);
            if (Vector3.Distance(blended, eyes) < .27f)
                headVisibility ??= Player3DHeadVisibility.Hide(visual.Registry);
            else
                ShowHead();
        }

        private float Ease(float start, float end) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, progress));

        private void CapturePose()
        {
            bones[0] = new RotationSnapshot(leftUpper);
            bones[1] = new RotationSnapshot(leftForearm);
            bones[2] = new RotationSnapshot(leftHand);
            bones[3] = new RotationSnapshot(rightUpper);
            bones[4] = new RotationSnapshot(rightForearm);
            bones[5] = new RotationSnapshot(rightHand);
            bones[6] = new RotationSnapshot(head);
            posed = true;
        }

        private void UndoPose()
        {
            if (!posed) return;
            posed = false;
            if (player.Ragdoll.IsActive) return;
            foreach (var bone in bones) bone.Restore();
        }

        private void ShowHead()
        {
            headVisibility?.Restore();
            headVisibility = null;
        }

        private void Restore()
        {
            UndoPose();
            ShowHead();
            watch?.PullCuff(0f);
            ReleaseHands();
            if (view != null)
            {
                view.nearClipPlane = startNear;
                if (!DebugFreeCamera.IsAnyActive)
                {
                    view.transform.SetPositionAndRotation(startView.position, startView.rotation);
                    view.fieldOfView = startLens;
                }
            }
            cameraLoan?.Dispose();
            cameraLoan = null;
            opening = terminalFrame = false;
            progress = 0f;
            look = Vector2.zero;
        }

        private void OnDisable()
        {
            if (cameraLoan != null && interaction != null) interaction.CancelActiveInteraction();
            watch?.SetWorn(false);
        }

        private void OnDestroy() => watch?.Dispose();

        private void ReleaseHands()
        {
            fingers?.ReleaseGrip(this, true);
            fingers?.ReleaseGrip(this, false);
        }

        private readonly struct RotationSnapshot
        {
            private readonly Transform bone;
            private readonly Quaternion rotation;
            public RotationSnapshot(Transform bone)
            {
                this.bone = bone;
                rotation = bone.localRotation;
            }
            public void Restore()
            {
                if (bone != null) bone.localRotation = rotation;
            }
        }
    }
}
