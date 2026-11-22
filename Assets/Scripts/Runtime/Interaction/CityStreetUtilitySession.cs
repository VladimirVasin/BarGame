using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One active municipal interaction per hero. Walking always moves the real controller.</summary>
    [DefaultExecutionOrder(365)]
    public sealed class CityStreetUtilitySession : MonoBehaviour
    {
        public enum Step { Idle, Approach, Opening, Entering, Inspect, Call, Leaving, Closing, Return }
        public Step Phase { get; private set; }
        private CityGameRoot city;
        private CityStreetUtilityInteraction source;
        private CityStreetUtilityModel model;
        private PlayerAnimatedInteractionController controller;
        private Player3DCharacterPresentation visual;
        private PlayerCameraFollow follow;
        private Camera lens;
        private IDisposable cameraLoan;
        private Player3DHeadVisibility hiddenHead;
        private NpcHandPose fingers;
        private Transform leftEye, rightEye, spine;
        private Arm left, right;
        private Vector3 entry, inside, exit;
        private Pose initialCamera;
        private Quaternion facing;
        private float initialLens, initialNear, elapsed, yaw, pitch;
        private int firstFrame;
        private bool feedback, ownsHands;
        private Func<bool> use;

        private readonly struct Arm
        {
            private readonly Transform upper, lower, hand;
            private readonly AnatomicalHandFrame frame;
            internal Arm(Player3DAssetRegistry reg, NpcHandPose fingers, bool left)
            {
                upper = Bone(reg, left ? Player3DAnatomicalPart.LeftUpperArm : Player3DAnatomicalPart.RightUpperArm);
                lower = Bone(reg, left ? Player3DAnatomicalPart.LeftForearm : Player3DAnatomicalPart.RightForearm);
                hand = Bone(reg, left ? Player3DAnatomicalPart.LeftHand : Player3DAnatomicalPart.RightHand);
                frame = fingers.GetAnatomicalFrame(left);
            }
            internal void Reach(Vector3 target, Vector3 axis, Vector3 front, Vector3 side, float amount)
            {
                if (amount <= 0f) return;
                Pose wrist = frame.ResolveCylinderPose(target, axis, front);
                LimbTwoBoneIk.Solve(upper,lower,hand,wrist.position,wrist.rotation,
                    upper.position + side*.24f + front*.16f - Vector3.up*.30f,
                    amount,LimbTwoBoneIk.DefaultReachFraction,true);
            }
        }

        private static Transform Bone(Player3DAssetRegistry reg, Player3DAnatomicalPart part) =>
            reg.TryGetPart(part,out var bone) ? bone.Bone : throw new InvalidOperationException("Missing hero joint " + part);

        internal void Begin(CityGameRoot owner, CityStreetUtilityInteraction utility)
        {
            if (Phase != Step.Idle || !utility.CanInteract(owner.Player.Interactor)) return;
            controller = GetComponent<PlayerAnimatedInteractionController>();
            visual = owner.Player.Visual as Player3DCharacterPresentation;
            lens = owner.Camera;
            if (!visual || !controller || !lens.TryGetComponent(out follow) || follow.PresentationSuspended) return;
            var registry = visual.Registry;
            if (!registry.TryGetComponent(out fingers)) return;
            left = new Arm(registry, fingers, true);
            right = new Arm(registry, fingers, false);
            spine = Bone(registry, Player3DAnatomicalPart.Torso);
            leftEye = registry.Anchors.Head.Find("face.eye.L");
            rightEye = registry.Anchors.Head.Find("face.eye.R");
            if (!leftEye || !rightEye || !controller.TryBeginGroundedSequence(follow, Restore)) return;
            city = owner;
            source = utility;
            model = source.Model;
            try
            {
                if (!fingers.TryCaptureGrip(this, false, NpcHandGripKind.Cylindrical, exclusive:true) ||
                    !fingers.TryCaptureGrip(this, true, NpcHandGripKind.Cylindrical, exclusive:true))
                {
                    ReleaseHands();
                    controller.CancelActiveInteraction();
                    return;
                }
                ownsHands = true;
                entry = source.Dock.StandPosition + Vector3.up * PlayerFactory.GroundedRootOffset;
                inside = model.transform.TransformPoint(new Vector3(0,PlayerFactory.GroundedRootOffset,CityStreetUtilityModel.Inside));
                exit = entry;
                facing = Quaternion.LookRotation(source.Dock.Facing);
                yaw = pitch = 0f;
                use ??= Use;
                enabled = true;
                Change(Step.Approach);
            }
            catch { controller.CancelActiveInteraction(); throw; }
        }

        private void Change(Step next)
        {
            Phase = next;
            elapsed = 0f;
            firstFrame = Time.frameCount;
            bool walking = next == Step.Approach || next == Step.Entering || next == Step.Leaving;
            visual.SetInteractionHandoffLocked(!walking);
            if (!walking) visual.SampleInteractionNeutralPose();
            city.Player.Interactor.PromptView?.SetPrompt(null);
        }

        private void Update()
        {
            if (Phase == Step.Idle) return;
            if (!source || !source.isActiveAndEnabled || city.IsDormant || SceneTransitionService.IsTransitioning)
            { controller.CancelActiveInteraction(); return; }
            if (GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused || Time.frameCount <= firstFrame) return;
            elapsed += Time.deltaTime;
            switch (Phase)
            {
                case Step.Approach:
                    if (Walk(entry))
                    {
                        initialCamera = new Pose(lens.transform.position,lens.transform.rotation);
                        initialLens = lens.fieldOfView;
                        initialNear = lens.nearClipPlane;
                        cameraLoan = follow.SuspendPresentation();
                        GameCursor.Capture(this, CursorLockMode.Locked, false, 8);
                        Change(Step.Opening);
                    }
                    break;
                case Step.Opening:
                    if (elapsed >= 1.16f)
                    {
                        model.SetOpen(1f);
                        Change(model.Phone ? Step.Entering : Step.Inspect);
                    }
                    break;
                case Step.Entering:
                    if (Walk(inside)) Change(Step.Inspect);
                    break;
                case Step.Inspect:
                    if (DebugFreeCamera.BlocksWorldInput) break;
                    Vector2 delta = follow.SampleMouseLookDegrees();
                    Look(delta);
                    city.Player.Interactor.PromptView?.SetPrompt(InspectKey, use);
                    if (GameInput.WasPressed(GameInputAction.Interact, GameInputContext.Contextual)) Use();
                    break;
                case Step.Call:
                    if (!feedback && elapsed >= 1.15f)
                    {
                        feedback = true;
                        city.Player.Interactor.ShowFeedback(CityStreetUtilityInteraction.PhoneBoothPlaceholderKey,1.32f);
                    }
                    if (elapsed >= 2.64f) Change(Step.Inspect);
                    break;
                case Step.Leaving:
                    if (Walk(exit, true)) Change(Step.Closing);
                    break;
                case Step.Closing:
                    if (elapsed >= .94f) { model.SetOpen(0f); Change(Step.Return); }
                    break;
                case Step.Return:
                    if (elapsed >= .57f && Time.frameCount > firstFrame+1) controller.CancelActiveInteraction();
                    break;
            }
        }

        private bool Walk(Vector3 target, bool back = false)
        {
            var motor = city.Player.Motor;
            if (motor.MoveTowardsSupportedInteractionPose(target, facing, Time.deltaTime, back)) return true;
            if (motor.InteractionPoseMoveStalled || elapsed > 5f) controller.CancelActiveInteraction();
            return false;
        }

        // At the rear panel the phone is ahead. Turning toward the public opening offers exit.
        private string InspectKey => !model.Phone ? "interaction.close_dumpster" :
            Mathf.Abs(yaw) > 75f ? "interaction.leave_phone_booth" : "interaction.use_phone_booth";

        internal void Look(Vector2 delta)
        {
            yaw = Mathf.Clamp(yaw + delta.x, model.Phone ? -178f : -48f, model.Phone ? 178f : 48f);
            pitch = Mathf.Clamp(pitch + delta.y, -32f, 36f);
        }

        internal bool Use()
        {
            if (Phase != Step.Inspect || Time.frameCount <= firstFrame || GameTimeScaleRuntime.IsPaused) return false;
            if (model.Phone && Mathf.Abs(yaw) <= 75f)
            {
                feedback = false;
                Change(Step.Call);
            }
            else Change(model.Phone ? Step.Leaving : Step.Closing);
            return true;
        }

        private void LateUpdate()
        {
            if (cameraLoan == null || !model) return;
            bool walking = Phase == Step.Entering || Phase == Step.Leaving;
            float opening = Phase == Step.Opening ? Ease(.43f,.91f) :
                Phase == Step.Closing ? 1f-Ease(.18f,.73f) : model.OpenAmount;
            model.SetOpen(opening);
            float weight = Phase == Step.Opening ? Ease(0f,.44f) : Phase == Step.Return ? 1f-Ease(0f,.52f) : 1f;
            if (!walking)
            {
                visual.SampleInteractionNeutralPose();
                float lean = model.Phone ? 0f : 29f * opening;
                spine.rotation = Quaternion.AngleAxis(lean, transform.right) * spine.rotation;
            }
            Vector3 eyes = (leftEye.position + rightEye.position)*.5f + transform.forward*.035f;
            if (!GameTimeScaleRuntime.IsPaused && (Phase == Step.Closing || Phase == Step.Leaving || Phase == Step.Call))
            {
                float speed = Time.deltaTime * 230f;
                yaw = Mathf.MoveTowards(yaw,0f,speed);
                pitch = Mathf.MoveTowards(pitch,0f,speed);
            }
            if (Phase == Step.Opening || Phase == Step.Closing)
            {
                float reach = Phase == Step.Opening ? Ease(.18f,.42f)*(1f-Ease(.76f,1.1f)) :
                    Ease(0f,.2f)*(1f-Ease(.73f,.93f));
                // The phone handle is to the approaching caller's left.
                bool leftSide = model.Phone;
                (leftSide ? left : right).Reach(model.Handle, leftSide ? Vector3.up : transform.right,
                    transform.forward, transform.right * (leftSide ? -1f : 1f), reach);
                fingers.SetOwnedGrip(this,leftSide,reach*.83f);
            }
            else if (Phase == Step.Call)
            {
                float lift = Ease(.26f,.72f)*(1f-Ease(2.02f,2.40f));
                model.SetReceiver(eyes-transform.right*.115f-Vector3.up*.075f, lift);
                float grip = Ease(0f,.25f)*(1f-Ease(2.40f,2.62f));
                left.Reach(model.Receiver,Vector3.up,transform.forward,-transform.right,grip);
                fingers.SetOwnedGrip(this,true,grip*.86f);
                float press = Ease(.58f,.82f)*(1f-Ease(1.10f,1.34f));
                right.Reach(model.Key-transform.forward*(.025f*Mathf.Abs(Mathf.Sin(elapsed*19f))),
                    Vector3.up,transform.forward,transform.right,press);
            }
            if (DebugFreeCamera.IsAnyActive) { ShowHead(); return; }
            Pose wide = Phase == Step.Return ? follow.ResolveFollowPose(exit) : initialCamera;
            Vector3 start = wide.position;
            Vector3 shoulder = eyes + transform.right * (Vector3.Dot(start-eyes,transform.right) < 0f ? -.38f : .38f);
            Vector3 position = Vector3.Lerp(Vector3.Lerp(start,shoulder,weight),Vector3.Lerp(shoulder,eyes,weight),weight);
            Vector3 interest = model.Phone ? model.Key : model.transform.TransformPoint(new Vector3(0,.63f,.02f));
            Vector3 sight = interest - eyes;
            float angle = Mathf.Atan2(-sight.y, new Vector2(sight.x,sight.z).magnitude)*Mathf.Rad2Deg;
            if (!model.Phone) angle = Mathf.Lerp(29f,angle,opening);
            Quaternion look = facing * Quaternion.Euler(0,yaw,0) * Quaternion.Euler(angle+pitch,0f,0f);
            lens.transform.SetPositionAndRotation(position,Quaternion.Slerp(wide.rotation,look,weight));
            lens.fieldOfView = Mathf.Lerp(initialLens,model.Phone ? 70f : 67f,weight);
            lens.nearClipPlane = Mathf.Lerp(initialNear,.032f,weight);
            if (Vector3.Distance(position,eyes) < .28f) hiddenHead ??= Player3DHeadVisibility.Hide(visual.Registry);
            else ShowHead();
        }

        private float Ease(float start,float end) => Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(start,end,elapsed));
        private void ShowHead() { hiddenHead?.Restore(); hiddenHead = null; }
        private void ReleaseHands() { fingers.ReleaseGrip(this,true); fingers.ReleaseGrip(this,false); ownsHands = false; }

        internal void Release(CityStreetUtilityInteraction utility)
        {
            if (source == utility && controller && controller.IsActive) controller.CancelActiveInteraction();
        }

        private void Restore()
        {
            if (model)
            {
                if (model.Phone) model.SetReceiver(model.Receiver,0f);
                model.CloseWhenClear(transform);
            }
            if (ownsHands) ReleaseHands();
            if (visual) visual.SampleInteractionNeutralPose();
            ShowHead();
            if (cameraLoan != null)
            {
                lens.nearClipPlane = initialNear;
                if (!DebugFreeCamera.IsAnyActive)
                {
                    var pose = follow.ResolveFollowPose(transform.position);
                    lens.transform.SetPositionAndRotation(pose.position,pose.rotation);
                    lens.fieldOfView = initialLens;
                }
                cameraLoan.Dispose();
                cameraLoan = null;
            }
            GameCursor.Release(this);
            if (city) city.Player.Interactor.PromptView?.SetPrompt(null);
            Phase = Step.Idle;
            source = null;
            model = null;
            enabled = false;
        }

        private void OnDisable() { if (Phase != Step.Idle && controller) controller.CancelActiveInteraction(); }
    }
}
