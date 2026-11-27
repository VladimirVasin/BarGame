using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One active municipal interaction per hero. Walking always moves the real controller.</summary>
    [DefaultExecutionOrder(365)]
    public sealed class CityStreetUtilitySession : MonoBehaviour
    {
        public enum Step { Idle, Approach, Opening, Entering, Inspect, Call, HangingUp, Leaving, Closing, Return, Searching, Found }
        public Step Phase { get; private set; }
        private CityGameRoot city;
        private CityStreetUtilityInteraction source;
        private CityStreetUtilityModel model;
        private PlayerAnimatedInteractionController controller;
        private Player3DCharacterPresentation visual;
        private PlayerCameraFollow follow;
        private Camera lens;
        private IDisposable cameraLoan, dumpsterVisibility;
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
        private bool exitRequested, exitKeyHeld, receiverOffHook;
        private float receiverLift, receiverGrip, pointingWeight, hangLift, hangGrip, hangPoint;
        private Vector3 indexPad, pointTarget;
        private int activeKey = -1, nextClick;
        private const float FirstPress = .88f, PressInterval = .34f;
        internal Vector3 IndexTip => right.Tip(indexPad);
        internal float WristBend => right.WristBend;
        internal int DepressedKey => activeKey;
        private Func<bool> use, leave;
        internal const float DumpsterSearchSeconds = 3f;
        private WorldItemFoundScreen findScreen;
        private Transform findModel, findGrip;
        private InventoryItemId findItem;
        private int searchDay;
        private bool revisiting, searchEndSampled, presentingFind;
        private float cardboardDirection, cardboardAtClose;

        private float SearchDuration => revisiting ? .80f : DumpsterSearchSeconds;
        private float SearchProgress => Mathf.Clamp01(elapsed / SearchDuration);

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
            internal Vector3 ResolveHeldPoint(Pose wrist, Vector3 target) =>
                hand.position + hand.rotation * (Quaternion.Inverse(wrist.rotation) * (target - wrist.position));

            internal Pose GripPose(Vector3 target, Vector3 axis, Vector3 front) =>
                frame.ResolveCylinderPose(target, axis, front);

            internal Vector3 Tip(Vector3 offset) => hand.position + hand.rotation * offset;
            internal float WristBend => Vector3.Angle(hand.position-lower.position,hand.rotation*frame.FingerDirectionInHand);

            internal void Point(Vector3 target, Vector3 index, Transform actor, float weight)
            {
                if (weight <= 0f) return;
                // Treat the forearm and extended finger as one link. Removing the index's
                // small sideways offset before solving keeps the wrist aligned as keys change.
                float axial = Vector3.Dot(index,frame.FingerDirectionInHand);
                Vector3 lateral = index - frame.FingerDirectionInHand*axial;
                float humerus = Vector3.Distance(upper.position,lower.position);
                float reach = Vector3.Distance(lower.position,hand.position)+axial;
                Vector3 distal = actor.forward;
                Vector3 elbow = lower.position;
                Vector3 pole = actor.right*.16f - Vector3.up*.42f;
                for (int i=0; i<3; i++)
                {
                    Quaternion aim = frame.ResolveHandRotation(Vector3.down,distal);
                    Vector3 endpoint = target - aim*lateral;
                    Vector3 axis = (endpoint-upper.position).normalized;
                    float distance = Mathf.Max(.001f,Vector3.Distance(endpoint,upper.position));
                    float along = Mathf.Clamp((humerus*humerus-reach*reach+distance*distance)/(2f*distance),-humerus,humerus);
                    float bend = Mathf.Sqrt(Mathf.Max(0f,humerus*humerus-along*along));
                    elbow = upper.position + axis*along + Vector3.ProjectOnPlane(pole,axis).normalized*bend;
                    distal = (endpoint-elbow).normalized;
                }
                Quaternion rotation = frame.ResolveHandRotation(Vector3.down, distal);
                Vector3 wrist = target - rotation*index;
                LimbTwoBoneIk.Solve(upper,lower,hand,wrist,rotation,elbow,weight,.998f,true);
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
                var rightGrip = model.Phone ? NpcHandGripKind.Pointing : NpcHandGripKind.Cylindrical;
                if (!fingers.TryCaptureGrip(this, false, rightGrip, exclusive:true) ||
                    !fingers.TryCaptureGrip(this, true, NpcHandGripKind.Cylindrical, exclusive:true))
                {
                    ReleaseHands();
                    controller.CancelActiveInteraction();
                    return;
                }
                ownsHands = true;
                if (model.Phone) indexPad = fingers.ExtendedIndexPad(false);
                exitRequested = receiverOffHook = false;
                exitKeyHeld = GameInput.IsHeld(GameInputAction.LeavePhoneBooth,GameInputContext.Contextual);
                receiverLift = receiverGrip = pointingWeight = 0f;
                activeKey = -1;
                entry = source.Dock.StandPosition + Vector3.up * PlayerFactory.GroundedRootOffset;
                inside = model.transform.TransformPoint(new Vector3(0,PlayerFactory.GroundedRootOffset,CityStreetUtilityModel.Inside));
                exit = entry;
                facing = Quaternion.LookRotation(source.Dock.Facing);
                yaw = pitch = 0f;
                cardboardAtClose = 0f;
                if (!model.Phone)
                {
                    searchDay = GameSessionState.GameDayIndex;
                    findItem = GameSessionState.DumpsterLoot(source.Dock.Id, searchDay);
                    revisiting = GameSessionState.GetDumpsterSearchState(source.Dock.Id, searchDay) ==
                        GameSessionState.DumpsterSearchState.Found;
                    cardboardDirection = (source.Dock.Id[0] + (int)findItem + searchDay) % 2 == 0 ? 1f : -1f;
                    searchEndSampled = false;
                }
                use ??= Use;
                leave ??= RequestBoothExit;
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
            if (next == Step.Call) city.Player.Interactor.PromptView?.SetPrompt("interaction.hang_up_phone",leave);
        }

        private void Update()
        {
            if (Phase == Step.Idle) return;
            if (!source || !source.isActiveAndEnabled || city.IsDormant || SceneTransitionService.IsTransitioning)
            { controller.CancelActiveInteraction(); return; }
            if (GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused || Time.frameCount <= firstFrame) return;
            bool held = GameInput.IsHeld(GameInputAction.LeavePhoneBooth,GameInputContext.Contextual);
            bool leavePressed = GameInput.WasPressed(GameInputAction.LeavePhoneBooth,GameInputContext.Contextual) ||
                held && !exitKeyHeld;
            exitKeyHeld = held;
            if (leavePressed &&
                RequestBoothExit() && (Phase == Step.Idle || firstFrame == Time.frameCount)) return;
            if (!model.Phone && Phase != Step.Found &&
                GameInput.WasPressed(GameInputAction.Cancel, GameInputContext.Contextual))
            {
                if (Phase == Step.Approach) controller.CancelActiveInteraction();
                else if (Phase == Step.Opening) exitRequested = true;
                else if (Phase == Step.Searching) CloseDumpster();
                return;
            }
            float previous = elapsed;
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
                    if (previous < .43f && elapsed >= .43f)
                        RetroAudio.PlayAt(model.Phone ? RetroSfxId.BoothUnlatch : RetroSfxId.BinLidLift,model.Handle);
                    if (elapsed >= 1.16f)
                    {
                        model.SetOpen(1f);
                        Change(exitRequested ? Step.Closing : model.Phone ? Step.Entering : Step.Searching);
                    }
                    break;
                case Step.Searching:
                    try { AdvanceDumpsterSearch(previous / SearchDuration); }
                    catch
                    {
                        controller.CancelActiveInteraction();
                        throw;
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
                    if (!feedback && elapsed >= 1.85f)
                    {
                        feedback = true;
                        city.Player.Interactor.ShowFeedback(CityStreetUtilityInteraction.PhoneBoothPlaceholderKey,1.32f);
                    }
                    if (elapsed >= 2.88f)
                    {
                        ResetDial();
                        Change(Step.Inspect);
                    }
                    break;
                case Step.HangingUp:
                    if (elapsed >= .38f)
                    {
                        ResetDial();
                        Change(Step.Leaving);
                    }
                    break;
                case Step.Leaving:
                    if (Walk(exit, true)) Change(Step.Closing);
                    break;
                case Step.Closing:
                    if (previous < .70f && elapsed >= .70f)
                        RetroAudio.PlayAt(model.Phone ? RetroSfxId.BoothLatch : RetroSfxId.BinLidShut,model.Handle);
                    if (elapsed >= .94f) { model.SetOpen(0f); Change(Step.Return); }
                    break;
                case Step.Return:
                    if (elapsed >= .57f && Time.frameCount > firstFrame+1) controller.CancelActiveInteraction();
                    break;
            }
        }

        private void AdvanceDumpsterSearch(float before)
        {
            float progress = SearchProgress;
            if (!revisiting && (Crossed(.17f) || Crossed(.39f) || Crossed(.61f)))
                RetroAudio.PlayAt(RetroSfxId.BinPaperRustle, model.CardboardGrip);
            if (Crossed(.72f) && findItem != InventoryItemId.None)
            {
                findModel = InventoryItemModelFactory.BuildWorldModel(findItem, model.transform, Vector3.one);
                var vessel = findModel.GetComponent<VodkaBottleView>();
                bool pawnshopFind = PawnshopFindCatalog.TryGet(findItem, out var offer);
                findGrip = vessel ? vessel.Grip : pawnshopFind ? PawnshopFindCatalog.FindAnchor(findModel, "Grip") : null;
                RetroAudio.PlayAt(pawnshopFind ? offer.Sound : findItem switch
                {
                    InventoryItemId.EmptyWineBottle or InventoryItemId.WineBottleLastSip => RetroSfxId.BinFoundGlass,
                    InventoryItemId.EmptyBeerCan or InventoryItemId.BeerCanLastSip => RetroSfxId.BinFoundMetal,
                    _ => RetroSfxId.BinPaperRustle
                }, model.CardboardGrip);
            }
            if (Crossed(.89f) && DrinkContainerCatalog.IsLastSip(findItem))
                RetroAudio.PlayAt(RetroSfxId.BinLastSipSlosh, findModel.position);
            if (progress < 1f) return;
            if (!searchEndSampled)
            {
                // Leave a rendered hold before the inspection presenter takes the model.
                searchEndSampled = true;
                return;
            }
            if (findItem == InventoryItemId.None)
            {
                GameSessionState.RevealDumpsterLoot(source.Dock.Id, searchDay);
                city.Player.Interactor.ShowFeedback(CityStreetUtilityInteraction.DumpsterEmptyKey, 2f);
                CloseDumpster();
                return;
            }
            PresentDumpsterFind();

            bool Crossed(float phase) => before < phase && progress >= phase;
        }

        private void PresentDumpsterFind()
        {
            GameSessionState.RevealDumpsterLoot(source.Dock.Id, searchDay);
            Change(Step.Found);
            var interactor = city.Player.Interactor;
            findScreen = WorldItemFoundScreen.For(interactor);
            var modal = controller.StationaryModalFor(Restore);
            if (findScreen && findScreen.TryPresentWithin(modal, interactor, findItem, findModel,
                TakeDumpsterFind, FinishDumpsterFind))
            {
                presentingFind = true;
                GameCursor.Capture(this, CursorLockMode.None, true, 8);
            }
            else CloseDumpster();
        }

        private bool TakeDumpsterFind() =>
            GameSessionState.TryTakeDumpsterLoot(source.Dock.Id, searchDay, findItem);

        private void FinishDumpsterFind(bool taken)
        {
            if (!presentingFind) return;
            presentingFind = false;
            findScreen = null;
            if (taken) DestroyDumpsterFind();
            CloseDumpster();
        }

        private void CloseDumpster()
        {
            cardboardAtClose = Phase == Step.Searching ? CardboardLift(SearchProgress) :
                Phase == Step.Found ? CardboardLift(1f) : 0f;
            GameCursor.Capture(this, CursorLockMode.Locked, false, 8);
            Change(Step.Closing);
        }

        private void DestroyDumpsterFind()
        {
            if (findModel) Destroy(findModel.gameObject);
            findModel = findGrip = null;
        }

        private bool Walk(Vector3 target, bool back = false)
        {
            var motor = city.Player.Motor;
            if (motor.MoveTowardsSupportedInteractionPose(target, facing, Time.deltaTime, back)) return true;
            if (motor.InteractionPoseMoveStalled || elapsed > 5f) controller.CancelActiveInteraction();
            return false;
        }

        // At the rear panel the phone is ahead. Turning toward the public opening offers exit.
        private string InspectKey => Mathf.Abs(yaw) > 75f ? "interaction.leave_phone_booth" : "interaction.use_phone_booth";

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
                nextClick = 0;
                Change(Step.Call);
            }
            else Change(Step.Leaving);
            return true;
        }

        internal bool RequestBoothExit()
        {
            if (!model || !model.Phone || Phase == Step.Idle) return false;
            exitRequested = true;
            switch (Phase)
            {
                case Step.Approach: controller.CancelActiveInteraction(); break;
                case Step.Inspect:
                case Step.Entering: Change(Step.Leaving); break;
                case Step.Call:
                    hangLift = receiverLift;
                    hangGrip = receiverGrip;
                    hangPoint = pointingWeight;
                    ReleaseKey();
                    Change(Step.HangingUp);
                    break;
            }
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
                float lean = model.Phone ? (Phase == Step.Call ? 11f*Ease(.36f,.76f)*(1f-Ease(1.82f,2.08f)) :
                    Phase == Step.HangingUp ? 11f*hangPoint*(1f-Ease(0f,.30f)) : 0f) : 29f * opening;
                spine.rotation = Quaternion.AngleAxis(lean, transform.right) * spine.rotation;
            }
            Vector3 eyes = (leftEye.position + rightEye.position)*.5f + transform.forward*.035f;
            if (!GameTimeScaleRuntime.IsPaused && (Phase == Step.Closing || Phase == Step.Leaving || Phase == Step.Call))
            {
                float speed = Time.deltaTime * 230f;
                yaw = Mathf.MoveTowards(yaw,0f,speed);
                pitch = Mathf.MoveTowards(pitch,0f,speed);
            }
            if (!model.Phone && (Phase == Step.Searching || Phase == Step.Found))
                PoseDumpsterSearch(Phase == Step.Found ? 1f : SearchProgress);
            if (!model.Phone && Phase == Step.Closing)
            {
                model.SetCardboard(cardboardAtClose * (1f - Ease(0f, .32f)), cardboardDirection);
                if (findModel && elapsed < .27f)
                    PoseDumpsterFind(1f - Ease(0f, .27f), rotate: 0f);
                else DestroyDumpsterFind();
            }
            if (Phase == Step.Opening || Phase == Step.Closing)
            {
                float reach = Phase == Step.Opening ? Ease(.18f,.42f)*(1f-Ease(.76f,1.1f)) :
                    (model.Phone ? Ease(0f,.2f) : Ease(.26f,.38f))*(1f-Ease(.73f,.93f));
                // The phone handle is to the approaching caller's left.
                bool leftSide = model.Phone;
                (leftSide ? left : right).Reach(model.Handle, leftSide ? Vector3.up : transform.right,
                    transform.forward, transform.right * (leftSide ? -1f : 1f), reach);
                fingers.SetOwnedGrip(this,leftSide,reach*.83f);
            }
            else if (Phase == Step.Call || Phase == Step.HangingUp)
            {
                bool hanging = Phase == Step.HangingUp;
                receiverLift = hanging ? hangLift*(1f-Ease(0f,.28f)) : Ease(.26f,.72f)*(1f-Ease(2.23f,2.64f));
                receiverGrip = hanging ? hangGrip*(1f-Ease(.27f,.37f)) : Ease(0f,.25f)*(1f-Ease(2.64f,2.86f));
                model.SetReceiver(eyes-transform.right*.115f-Vector3.up*.075f,receiverLift);
                if (!receiverOffHook && !hanging && elapsed < 1f && receiverLift > .04f)
                {
                    receiverOffHook = true;
                    RetroAudio.PlayAt(RetroSfxId.HandsetLift,model.Receiver);
                }
                if (receiverOffHook && receiverLift < .025f) SeatReceiver();
                left.Reach(model.Receiver,Vector3.up,transform.forward,-transform.right,receiverGrip);
                fingers.SetOwnedGrip(this,true,receiverGrip*.86f);
                pointingWeight = hanging ? hangPoint*(1f-Ease(0f,.28f)) : Ease(.45f,.76f)*(1f-Ease(1.75f,2.05f));
                if (!hanging) SampleDial();
                fingers.SetOwnedGrip(this,false,pointingWeight);
                right.Point(pointTarget,indexPad,transform,pointingWeight);
            }
            if (DebugFreeCamera.IsAnyActive)
            {
                ShowHead();
                RestoreDumpsterVisibility();
                return;
            }
            if (!model.Phone) dumpsterVisibility ??= city.Player.PresentationVisibility.AcquireHidden(this);
            Pose wide = Phase == Step.Return ? follow.ResolveFollowPose(exit) : initialCamera;
            Vector3 start = wide.position;
            Vector3 closeCamera = eyes;
            if (!model.Phone)
            {
                Vector3 outsideRim = eyes - transform.forward * .23f + Vector3.up * .10f;
                Vector3 insideRim = model.transform.TransformPoint(new Vector3(0f, 1.48f, .34f));
                // Advance after the lid clears the lens path and retreat before it closes.
                float advance = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.64f, .97f, opening));
                closeCamera = Vector3.Lerp(outsideRim, insideRim, advance);
            }
            Vector3 shoulder = closeCamera + transform.right * (Vector3.Dot(start-eyes,transform.right) < 0f ? -.38f : .38f);
            Vector3 position = Vector3.Lerp(Vector3.Lerp(start,shoulder,weight),Vector3.Lerp(shoulder,closeCamera,weight),weight);
            Quaternion look;
            if (model.Phone)
            {
                Vector3 sight = model.Key - eyes;
                float angle = Mathf.Atan2(-sight.y, new Vector2(sight.x,sight.z).magnitude)*Mathf.Rad2Deg;
                look = facing * Quaternion.Euler(0,yaw,0) * Quaternion.Euler(angle+pitch,0f,0f);
            }
            else
            {
                Vector3 interest = model.transform.TransformPoint(new Vector3(0f, .83f, .15f));
                look = Quaternion.LookRotation(interest - closeCamera, Vector3.up);
            }
            lens.transform.SetPositionAndRotation(position,Quaternion.Slerp(wide.rotation,look,weight));
            lens.fieldOfView = Mathf.Lerp(initialLens,model.Phone ? 70f : 67f,weight);
            lens.nearClipPlane = Mathf.Lerp(initialNear,.032f,weight);
            if (model.Phone && Vector3.Distance(position,eyes) < .28f) hiddenHead ??= Player3DHeadVisibility.Hide(visual.Registry);
            else ShowHead();
        }

        private static float SearchEase(float start, float end, float progress) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, progress));

        private float CardboardLift(float progress) =>
            SearchEase(.14f, .39f, progress) * (1f - SearchEase(findModel ? .80f : .73f, .94f, progress));

        private void PoseDumpsterSearch(float progress)
        {
            model.SetCardboard(CardboardLift(progress), cardboardDirection);
            Vector3 front = transform.forward;
            Vector3 side = transform.right;
            left.Reach(model.transform.TransformPoint(new Vector3(.24f, 1.065f, .455f)),
                side, front, -side, SearchEase(0f, .17f, progress));
            fingers.SetOwnedGrip(this, true, .57f * SearchEase(0f, .17f, progress));
            if (findModel)
            {
                if (Phase != Step.Found)
                    PoseDumpsterFind(SearchEase(.72f, .97f, progress), SearchEase(.85f, .99f, progress));
                else PoseDumpsterFind(1f, 1f, moveModel: false);
                return;
            }

            Vector3 elbowRoom = side * (cardboardDirection * .115f * SearchEase(.40f, .64f, progress));
            Vector3 target = model.CardboardGrip + elbowRoom - Vector3.up * (.055f * SearchEase(.44f, .62f, progress));
            float reach = SearchEase(.04f, .16f, progress) * (1f - SearchEase(.80f, .97f, progress));
            right.Reach(target, side, front, side, reach);
            fingers.SetOwnedGrip(this, false, .63f * reach);
        }

        private void PoseDumpsterFind(float lift, float rotate, bool moveModel = true)
        {
            Vector3 front = transform.forward;
            Vector3 side = transform.right;
            Vector3 target = model.transform.TransformPoint(Vector3.Lerp(new Vector3(-.16f, .86f, .29f),
                new Vector3(-.08f, 1.12f, .34f), lift));
            Quaternion rotation = Quaternion.AngleAxis(17f * rotate, front) * facing;
            if (PawnshopFindCatalog.TryGet(findItem, out _))
            {
                float reveal = findItem == InventoryItemId.SilverRing ? -80f + 360f * (1f - rotate) : -58f * rotate;
                rotation *= Quaternion.Euler(reveal, 0f, 0f);
            }
            right.Reach(target, rotation * Vector3.up, front, side, 1f);
            fingers.SetOwnedGrip(this, false, .82f);
            if (!moveModel) return;
            Pose wrist = right.GripPose(target, rotation * Vector3.up, front);
            Vector3 localGrip = findGrip ? findModel.InverseTransformPoint(findGrip.position) : Vector3.up * .035f;
            Vector3 contact = right.ResolveHeldPoint(wrist, target);
            findModel.SetPositionAndRotation(contact - rotation * Vector3.Scale(localGrip, findModel.lossyScale), rotation);
        }

        private float Ease(float start,float end) => Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(start,end,elapsed));

        private void SampleDial()
        {
            int key = Mathf.Clamp(Mathf.FloorToInt((elapsed-FirstPress+.11f)/PressInterval),0,2);
            float contact = FirstPress+key*PressInterval;
            float stroke = Ease(contact-.09f,contact)*(1f-Ease(contact+.08f,contact+.17f));
            float travel = Mathf.InverseLerp(.85f,1f,stroke);
            if (activeKey != key) ReleaseKey();
            activeKey = key;
            model.SetKeyTravel(key,travel);
            Vector3 face = model.KeyFace(key);
            float move = key < 2 ? Ease(contact+.17f,contact+PressInterval-.11f) : 0f;
            pointTarget = Vector3.Lerp(face,model.DialKey(Mathf.Min(key+1,2)),move) +
                model.transform.forward * (.028f * (1f-Mathf.Clamp01(stroke/.85f)));
            if (travel >= .65f && key >= nextClick)
            {
                nextClick = key+1;
                RetroAudio.PlayAt(RetroSfxId.PayphoneKey,face);
            }
        }

        private void SeatReceiver()
        {
            receiverOffHook = false;
            RetroAudio.PlayAt(RetroSfxId.HandsetSeat,model.Receiver);
        }
        private void ReleaseKey()
        {
            if (activeKey >= 0) model.SetKeyTravel(activeKey,0f);
            activeKey = -1;
        }
        private void ResetDial()
        {
            ReleaseKey();
            model.SetReceiver(model.Receiver,0f);
            if (receiverOffHook) SeatReceiver();
            fingers.SetOwnedGrip(this,false,0f);
            fingers.SetOwnedGrip(this,true,0f);
            receiverLift = receiverGrip = pointingWeight = 0f;
        }
        private void ShowHead() { hiddenHead?.Restore(); hiddenHead = null; }

        private void RestoreDumpsterVisibility()
        {
            dumpsterVisibility?.Dispose();
            dumpsterVisibility = null;
        }

        private void ReleaseHands() { fingers.ReleaseGrip(this,true); fingers.ReleaseGrip(this,false); ownsHands = false; }

        internal void Release(CityStreetUtilityInteraction utility)
        {
            if (source == utility && controller && controller.IsActive) controller.CancelActiveInteraction();
        }

        private void Restore()
        {
            if (model)
            {
                if (model.Phone) { ReleaseKey(); model.SetReceiver(model.Receiver,0f); }
                else model.SetCardboard(0f, cardboardDirection);
                model.CloseWhenClear(transform);
            }
            presentingFind = false;
            if (findScreen && Phase == Step.Found) findScreen.Abandon();
            findScreen = null;
            DestroyDumpsterFind();
            if (ownsHands) ReleaseHands();
            if (visual) visual.SampleInteractionNeutralPose();
            ShowHead();
            RestoreDumpsterVisibility();
            if (cameraLoan != null)
            {
                if (lens) lens.nearClipPlane = initialNear;
                if (lens && follow && !DebugFreeCamera.IsAnyActive)
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
