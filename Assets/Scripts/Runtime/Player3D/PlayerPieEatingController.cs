using System;
using UnityEngine;

namespace BarPromenade
{
    [DefaultExecutionOrder(365)]
    [DisallowMultipleComponent]
    internal sealed class PlayerPieEatingController : MonoBehaviour
    {
        private static readonly float[] BiteTimes = { .82f, 1.72f, 2.62f };
        private const float PlaybackRate = .9f;
        private const float BurpTime = 3.86f;
        private const float EndTime = 4.38f;

        private PlayerRuntime player;
        private Player3DCharacterPresentation visual;
        private PlayerAnimatedInteractionController interaction;
        private PlayerScarfController scarf;
        private PlayerCrowbarCombatController combat;
        private PlayerCameraFollow follow;
        private Camera view;
        private NpcHandPose fingers;
        private Transform mouth;
        private Transform leftEye;
        private Transform rightEye;
        private Transform rightUpper;
        private Transform rightLower;
        private Transform rightHand;
        private Transform leftUpper;
        private Transform leftLower;
        private Transform leftHand;
        private AnatomicalHandFrame foodHand;
        private AnatomicalHandFrame wipingHand;
        private Quaternion wristInPie;
        private readonly Quaternion[] baseRotations = new Quaternion[7];
        private bool poseApplied;
        private readonly GameObject[] pieces = new GameObject[3];
        private readonly Vector3[] bitePoints = new Vector3[3];
        private Vector3 grip;
        private Transform prop;
        private PlayerScarfController.MouthAccess mouthAccess;
        private Action cleanup;
        private IDisposable cameraLoan;
        private Pose originalView;
        private float originalLens;
        private bool seated;
        private bool playing;
        private bool gripping;
        private bool burped;
        private float elapsed;
        private int bite;
        private int settledFrame;
        private int terminalFrame;
        private int session;

        internal int BitesPresented => bite;
        internal bool BurpPresented => burped;
        internal float ContactError { get; private set; }

        internal static void Install(PlayerRuntime player, Camera camera, PlayerAnimatedInteractionController interaction)
        {
            if (camera == null || player.Visual is not Player3DCharacterPresentation visual) return;

            var owner = player.GameObject.AddComponent<PlayerPieEatingController>();
            owner.player = player;
            owner.visual = visual;
            owner.interaction = interaction;
            owner.view = camera;
            owner.scarf = player.GameObject.GetComponent<PlayerScarfController>();
            owner.combat = player.GameObject.GetComponent<PlayerCrowbarCombatController>();
            owner.cleanup = owner.Restore;
        }

        private bool CanStand => !interaction.IsActive && player.Motor.IsGrounded && !player.Motor.ExternalPushActive;
        private bool IsIncapacitated => (player.Ragdoll != null && player.Ragdoll.IsActive) ||
            (combat != null && combat.Actor != null && (combat.Actor.IsKnockedDown ||
                combat.Actor.IsRagdollActive || combat.Actor.IsRestrained || combat.Actor.State.IsDefeated));

        internal bool CanPrepare => isActiveAndEnabled && view != null && !playing &&
            (CanStand || interaction.CanUseStationarySeat) && !IsIncapacitated &&
            !GameSessionState.IsRidingAVehicle && !SceneTransitionService.IsTransitioning &&
            !Player3DBathingAppearance.IsActive && Player3DHeadVisibility.IsHeadDrawn(visual.Registry) &&
            !PlayerVodkaBottleController.IsHandOccupied(gameObject) &&
            (scarf == null || !scarf.IsGestureActive) &&
            (combat == null || (!combat.HasPendingAction && !combat.IsPreparingInteraction &&
                (combat.Actor == null || (!combat.Actor.HasWeaponInHand && !combat.Actor.IsSwitchingWeapon))));

        internal bool TryPrepare(PlayerCameraFollow cameraFollow)
        {
            if (!CanPrepare || prop != null || !GameSessionState.EvaluateInventoryItemUse(InventoryItemId.PotatoPirozhok).Succeeded)
                return false;

            var registry = visual.Registry;
            mouth = registry.Anchors.Mouth;
            Transform head = registry.Anchors.Head;
            if (head != null)
            {
                foreach (Transform bone in head.GetComponentsInChildren<Transform>(true))
                {
                    if (bone.name == "face.eye.L") leftEye = bone;
                    if (bone.name == "face.eye.R") rightEye = bone;
                }
            }
            rightUpper = Bone(Player3DAnatomicalPart.RightUpperArm);
            rightLower = Bone(Player3DAnatomicalPart.RightForearm);
            rightHand = Bone(Player3DAnatomicalPart.RightHand);
            leftUpper = Bone(Player3DAnatomicalPart.LeftUpperArm);
            leftLower = Bone(Player3DAnatomicalPart.LeftForearm);
            leftHand = Bone(Player3DAnatomicalPart.LeftHand);
            if (!registry.TryGetComponent(out fingers) || mouth == null || leftEye == null || rightEye == null ||
                rightUpper == null || rightLower == null || rightHand == null ||
                leftUpper == null || leftLower == null || leftHand == null)
                return false;

            if (Resources.Load<GameObject>(InventoryItemModelFactory.PotatoPieResourcePath) == null) return false;
            prop = InventoryItemModelFactory.BuildWorldModel(InventoryItemId.PotatoPirozhok, transform, Vector3.one);
            prop.name = "Eating Potato Pirozhok";
            prop.gameObject.SetActive(false);
            Transform imported = prop.GetChild(0);
            Transform model = imported.Find("PotatoPie_MetricRoot") ?? imported;
            Transform gripMarker = model.Find("PieGrip");
            if (gripMarker == null)
            {
                Restore();
                return false;
            }

            grip = prop.InverseTransformPoint(gripMarker.position);
            for (int i = 0; i < pieces.Length; i++)
            {
                Transform piece = model.Find($"PiePiece{i+1}");
                Transform contact = model.Find($"Bite{i+1}");
                if (piece == null || contact == null)
                {
                    Restore();
                    return false;
                }

                pieces[i] = piece.gameObject;
                bitePoints[i] = prop.InverseTransformPoint(contact.position);
            }

            follow = cameraFollow;
            return true;
        }

        internal bool BeginPrepared()
        {
            if (prop == null) return false;
            seated = interaction.CanUseStationarySeat;
            if (!(seated ? interaction.TryBeginStationarySeat(follow, cleanup) :
                    interaction.TryBeginStationary(follow, cleanup)))
            {
                Restore();
                return false;
            }

            playing = true;
            session = GameSessionState.SessionGeneration;
            elapsed = 0f;
            bite = 0;
            ContactError = 0f;
            burped = false;
            terminalFrame = -1;
            settledFrame = Time.frameCount;
            mouthAccess = PlayerScarfController.RequireMouthAccess(player, this);
            originalView = new Pose(view.transform.position, view.transform.rotation);
            originalLens = view.fieldOfView;
            cameraLoan = follow != null ? follow.SuspendPresentation() : null;
            return true;
        }

        private Transform Bone(Player3DAnatomicalPart part) =>
            visual.Registry.TryGetPart(part, out var binding) ? binding.Bone : null;

        private void Update()
        {
            if (!playing) return;
            UndoPose();
            if (terminalFrame >= 0 && Time.frameCount > terminalFrame ||
                SceneTransitionService.IsTransitioning || session != GameSessionState.SessionGeneration ||
                view == null || IsIncapacitated || !seated && !GameTimeScaleRuntime.IsPaused && !player.Motor.IsGrounded ||
                seated && interaction.Phase != PlayerAnimatedInteractionPhase.Looping)
            {
                interaction.EndStationaryAction(cleanup);
                return;
            }

            if (!seated) visual.SampleInteractionNeutralPose();
        }

        private void LateUpdate()
        {
            if (!playing || Time.frameCount <= settledFrame || !mouthAccess.IsReady) return;
            if (!gripping)
            {
                if (GameTimeScaleRuntime.IsPaused) return;
                gripping = fingers.TryCaptureGrip(this, false, NpcHandGripKind.Cylindrical, exclusive: true);
                if (!gripping || !fingers.TryCaptureGrip(this, true, NpcHandGripKind.Cylindrical, exclusive: true))
                {
                    interaction.EndStationaryAction(cleanup);
                    return;
                }

                foodHand = fingers.GetAnatomicalFrame(false);
                wipingHand = fingers.GetAnatomicalFrame(true);
                // Cup the wrapped heel from below. The fingers follow the
                // forearm across the paper, with the thumb on the long side.
                wristInPie = foodHand.ResolveHandRotation(Vector3.up, new Vector3(.12f, .55f, -1f));
                settledFrame = Time.frameCount;
                return;
            }

            // Every contact is a sampling boundary even when a slow frame
            // would otherwise cross multiple beats.
            float stop = bite < BiteTimes.Length ? BiteTimes[bite] : burped ? EndTime : BurpTime;
            bool advances = !GameTimeScaleRuntime.IsPaused && !PauseMenuController.IsAnyPaused;
            if (advances) elapsed = Mathf.Min(stop, elapsed + Time.deltaTime * PlaybackRate);
            ReapplyPresentation();
            ApplyCamera();

            if (!advances || elapsed < stop) return;
            if (bite < BiteTimes.Length)
            {
                if (bite == 0 && !GameSessionState.TryConsumeInventoryItem(InventoryItemId.PotatoPirozhok).Succeeded)
                {
                    interaction.EndStationaryAction(cleanup);
                    return;
                }

                ContactError = Vector3.Distance(prop.TransformPoint(bitePoints[bite]), LipPosition());
                pieces[bite++].SetActive(false);
                RetroAudio.PlayAt(RetroSfxId.PieBite, mouth.position);
            }
            else if (!burped)
            {
                burped = true;
                RetroAudio.PlayAt(RetroSfxId.PieBurp, mouth.position);
            }
            else
            {
                terminalFrame = Time.frameCount;
            }
        }

        private Vector3 LipPosition()
        {
            Vector3 faceForward = mouth.up;
            Vector3 faceUp = Vector3.ProjectOnPlane((leftEye.position + rightEye.position) * .5f -
                mouth.position, faceForward).normalized;
            // Compensate the registered socket's offset from the painted lips.
            return mouth.position - faceForward * .0395f - faceUp * .0096f;
        }

        internal void ReapplyPresentation()
        {
            if (!playing || !gripping || Time.frameCount <= settledFrame) return;
            UndoPose();
            Transform head = visual.Registry.Anchors.Head;
            baseRotations[0] = rightUpper.localRotation;
            baseRotations[1] = rightLower.localRotation;
            baseRotations[2] = rightHand.localRotation;
            baseRotations[3] = leftUpper.localRotation;
            baseRotations[4] = leftLower.localRotation;
            baseRotations[5] = leftHand.localRotation;
            baseRotations[6] = head.localRotation;
            poseApplied = true;

            float raise = 0f;
            float jaw = 0f;
            int contact = 0;
            for (int i = 0; i < BiteTimes.Length; i++)
            {
                float sinceBite = elapsed - BiteTimes[i];
                float lift = Ease(-.32f, -.045f, sinceBite) * (1f - Ease(.02f, .23f, sinceBite));
                if (lift > 0f)
                {
                    raise = lift;
                    contact = i;
                }
                jaw += 58f * Ease(-.20f, -.065f, sinceBite) * (1f - Ease(-.038f, .015f, sinceBite));
                float chew = Ease(.045f, .115f, sinceBite) * (1f - Ease(.38f, .57f, sinceBite));
                jaw += chew * (15f + 11f * Mathf.Sin(sinceBite * 2f * Mathf.PI * 4.7f));
            }

            float burp = Ease(BurpTime-.06f, BurpTime+.08f, elapsed) *
                (1f - Ease(BurpTime+.11f, BurpTime+.31f, elapsed));
            head.rotation = Quaternion.AngleAxis(5f * raise - 7f * burp, transform.right) * head.rotation;
            visual.SetActionJaw(jaw + 42f * burp);
            Vector3 lip = LipPosition();
            float hold = Ease(.06f, .40f, elapsed) * (1f - Ease(2.92f, 3.36f, elapsed));
            ApplyFoodHand(lip, hold, raise, contact);
            ApplyWipe(lip);
        }

        private void ApplyFoodHand(Vector3 lip, float hold, float raise, int contact)
        {
            Vector3 forward = transform.forward;
            Vector3 right = transform.right;
            Vector3 up = transform.up;
            Vector3 biteAxis = -forward - .65f * right + .12f * up;
            Vector3 carryAxis = -right - .27f * forward + .08f * up;
            Quaternion atMouth = PieRotation(biteAxis, up);
            Quaternion carry = PieRotation(carryAxis, up);
            Quaternion rotation = Quaternion.Slerp(carry, atMouth, raise);
            Vector3 atRest = lip + transform.rotation * new Vector3(.20f, -.34f, .26f);
            Vector3 biteFromGrip = bitePoints[contact] - grip;
            Vector3 gripTarget = Vector3.Lerp(atRest, lip - atMouth * biteFromGrip, raise);
            Vector3 hint = rightUpper.position + transform.rotation * new Vector3(.16f, -.30f, -.08f);
            SolveFoodArm(gripTarget, rotation, hint, hold);
            if (raise > 0f)
            {
                Vector3 forearm = rightHand.position - rightLower.position;
                Vector3 fingersForward = rightHand.rotation * foodHand.FingerDirectionInHand;
                float bend = Vector3.Angle(fingersForward, forearm);
                float correction = Mathf.Clamp(bend - 32f, 0f, 26f) * raise;
                if (correction > 0f)
                {
                    // Roll the held pie around the bite point so the shorter
                    // remainder can reach the lips with a straighter wrist.
                    Vector3 contactPoint = gripTarget + rotation * biteFromGrip;
                    rotation = Quaternion.RotateTowards(Quaternion.identity,
                        Quaternion.FromToRotation(fingersForward, forearm), correction) * rotation;
                    gripTarget = contactPoint - rotation * biteFromGrip;
                    rightUpper.localRotation = baseRotations[0];
                    rightLower.localRotation = baseRotations[1];
                    rightHand.localRotation = baseRotations[2];
                    SolveFoodArm(gripTarget, rotation, hint, hold);
                }
            }
            fingers.SetOwnedGrip(this, false, .70f * hold);

            bool show = elapsed >= .28f && elapsed <= 3.26f;
            if (prop.gameObject.activeSelf != show) prop.gameObject.SetActive(show);
            if (!show) return;
            Quaternion attached = rightHand.rotation * Quaternion.Inverse(wristInPie);
            Vector3 attachedGrip = rightHand.position + rightHand.rotation * foodHand.CylinderCentreInHand;
            prop.SetPositionAndRotation(attachedGrip - attached * grip, attached);
        }

        private void SolveFoodArm(Vector3 gripTarget, Quaternion pieRotation, Vector3 hint, float weight)
        {
            Quaternion wristRotation = pieRotation * wristInPie;
            Vector3 wristTarget = gripTarget - wristRotation * foodHand.CylinderCentreInHand;
            LimbTwoBoneIk.Solve(rightUpper, rightLower, rightHand, wristTarget, wristRotation, hint,
                weight, LimbTwoBoneIk.DefaultReachFraction, true);
        }

        private static Quaternion PieRotation(Vector3 length, Vector3 up)
        {
            Vector3 side = Vector3.Cross(length, up);
            return Quaternion.LookRotation(side, Vector3.Cross(side, length));
        }

        private void ApplyWipe(Vector3 lip)
        {
            float reach = Ease(3.08f, 3.32f, elapsed) * (1f - Ease(3.56f, 3.79f, elapsed));
            if (reach <= 0f) return;
            float sweep = Ease(3.32f, 3.56f, elapsed);
            Vector3 palm = lip + transform.rotation * new Vector3(Mathf.Lerp(.065f, -.072f, sweep), -.012f, .033f);
            Quaternion rotation = wipingHand.ResolveHandRotation(transform.forward, transform.right + .16f * transform.up);
            Vector3 hint = leftUpper.position + transform.rotation * new Vector3(-.35f, -.27f, .18f);
            LimbTwoBoneIk.Solve(leftUpper, leftLower, leftHand, wipingHand.ResolveHandPosition(palm, rotation),
                rotation, hint, reach, LimbTwoBoneIk.DefaultReachFraction, true);
            fingers.SetOwnedGrip(this, true, .23f * reach);
        }

        private void ApplyCamera()
        {
            if (cameraLoan == null || view == null || DebugFreeCamera.IsAnyActive) return;
            float blend = Ease(.06f, .69f, elapsed) * (1f - Ease(3.93f, EndTime, elapsed));
            Vector3 centre = mouth.position - transform.up * .15f;
            Vector3 startOffset = originalView.position - centre;
            Vector3 targetOffset = transform.rotation * new Vector3(1.06f, .22f, 1.18f);
            Vector3 position = centre + Vector3.Slerp(startOffset, targetOffset, blend);
            if (blend > 0f)
                position = Vector3.Lerp(position, follow.ConstrainContextualCamera(centre, position), blend);
            Quaternion look = Quaternion.LookRotation(centre - position, transform.up) *
                Quaternion.Euler(0f, 0f, -2f * blend);
            view.transform.SetPositionAndRotation(position, Quaternion.Slerp(originalView.rotation, look, blend));
            view.fieldOfView = Mathf.Lerp(originalLens, 39f, blend);
        }

        private static float Ease(float start, float end, float time) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, time));

        private void UndoPose()
        {
            if (!poseApplied) return;
            poseApplied = false;
            rightUpper.localRotation = baseRotations[0];
            rightLower.localRotation = baseRotations[1];
            rightHand.localRotation = baseRotations[2];
            leftUpper.localRotation = baseRotations[3];
            leftLower.localRotation = baseRotations[4];
            leftHand.localRotation = baseRotations[5];
            visual.Registry.Anchors.Head.localRotation = baseRotations[6];
            visual.SetActionJaw(0f);
        }

        private void Restore()
        {
            UndoPose();
            if (gripping)
            {
                fingers.ReleaseGrip(this, false);
                fingers.ReleaseGrip(this, true);
            }

            gripping = playing = false;
            mouthAccess?.Dispose();
            mouthAccess = null;
            if (cameraLoan != null)
            {
                if (view != null && !DebugFreeCamera.IsAnyActive)
                {
                    view.transform.SetPositionAndRotation(originalView.position, originalView.rotation);
                    view.fieldOfView = originalLens;
                }
                cameraLoan.Dispose();
                cameraLoan = null;
            }
            if (prop != null)
            {
                prop.gameObject.SetActive(false);
                Destroy(prop.gameObject);
                prop = null;
            }
        }

        private void OnDisable()
        {
            if (playing && interaction != null) interaction.EndStationaryAction(cleanup);
            else Restore();
        }
    }
}
