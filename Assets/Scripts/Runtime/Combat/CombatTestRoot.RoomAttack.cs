using UnityEngine;

namespace BarPromenade
{
    public enum CombatRoomSequencePhase { Idle, FocusEntry, PointHero, PointChest, EnterRoom, SeatedHero, ReturnRoom, Return, HeadPain, Laugh }

    public sealed partial class CombatTestRoot
    {
        internal const float RoomShotSeconds = 2f, RoomPointSeconds = 1f, RoomLaughSeconds = 5f;
        internal const float RoomFocusSeconds = .9f, RoomChestSeconds = .6f, RoomTransferSeconds = 1f, RoomWithdrawSeconds = .6f;
        internal const float RoomPointStartFov = 76f, RoomPointEndFov = 88f;
        private const float RoomPointStartRoll = 12f, RoomPointEndRoll = 22f;
        // The shortened take reaches its held gesture at source frame 18/60.
        private const float RoomPointRaisedProgress = .3f;
        private const float RoomHeadGrabEnterSeconds = .32f, RoomHeadGrabHeldProgress = .16f;
        private const float RoomApproachFov = 45f, RoomApproachRoll = 6f;
        private const float RoomSeatedStartFov = 48f, RoomSeatedEndFov = 50f;
        private const float RoomSeatedStartRoll = 4f, RoomSeatedEndRoll = -4f;
        private static readonly float RoomSeatedFramingScale = Mathf.Tan(56f * .5f * Mathf.Deg2Rad) /
            Mathf.Tan(RoomSeatedStartFov * .5f * Mathf.Deg2Rad);
        private readonly ContextualCameraDirector roomCamera = new ContextualCameraDirector();
        private readonly BarMinigameModalLock roomInput = new BarMinigameModalLock();
        private CombatRoomAttackTableau roomTableau;
        private CombatActor roomAttacker;
        private Transform roomHead;
        private float roomPhaseElapsed, roomLaughElapsed;
        private int roomAttackSequence;
        private Pose roomChestCamera, roomApproachCamera, roomPointStartCamera, roomPointEndCamera;
        private float roomChestStartFov, roomChestStartNear;
        private Vector3 roomChestFocus, roomPointFocus, roomPathFirst, roomPathSecond;
        private bool roomReleasePending;

        public CombatRoomSequencePhase RoomAttackPhase { get; private set; }
        public float RoomAttackPhaseElapsed => roomPhaseElapsed;
        internal float RoomLaughElapsed => roomLaughElapsed;
        public bool RoomAttackCinematicActive => RoomAttackPhase != CombatRoomSequencePhase.Idle && RoomAttackPhase != CombatRoomSequencePhase.Laugh;
        internal bool RoomAttackTableauVisible => roomTableau != null && roomTableau.Visible;
        internal Player3DAssetRegistry RoomAttackHeroImage => roomTableau?.Hero;
        internal Light RoomAttackShadowLight => roomTableau?.ShadowLight;
        internal AudioSource RoomAttackMemorySound => roomTableau?.MemorySound;

        private void PrepareRoomAttackSequence()
        {
            bool needed = false;
            foreach (CombatActor actor in preparedOpponents)
                if (actor.IsRoom) { actor.PrepareRoomAttackPresentation(); needed = true; }
            if (!needed) return;
            Hero.PrepareRoomHeadPainPresentation();
            roomTableau = new CombatRoomAttackTableau(transform, ((Player3DCharacterPresentation)Player.Visual).Registry);
            foreach (CombatActor actor in preparedOpponents)
                if (actor.IsRoom) roomTableau.PrepareActor(actor);
        }

        private bool TryBeginRoomAttack(CombatActor attacker)
        {
            if (RoomAttackPhase != CombatRoomSequencePhase.Idle || roomTableau == null ||
                Hero.State.IsDefeated || Hero.IsKnockedDown || Hero.IsRagdollActive ||
                !Hero.BodyDamage.CanStand || !Hero.BodyDamage.CanUseLeftHand ||
                !Hero.IsAvailable || BarMinigameModalLock.IsAnyLocked || SceneTransitionService.IsTransitioning ||
                !Hero.PrepareRoomHeadPainPresentation() || !attacker.TryBeginRoomAttackPose()) return false;
            roomAttacker = attacker;
            roomHead = CityPedestrianHandProps.FindSocket(attacker.DamageRigRoot, "head");
            if (roomHead == null) { CancelRoomAttackSequence(); return false; }
            // Frame the authored held gesture; its entry raises the arm while
            // the shared camera moves from the ordinary view.
            attacker.SampleRoomAttackPose(CombatRoomAttackPose.PointHero, 1f);
            roomTableau.BindTo(attacker);
            bool acquired = false;
            for (int variant = 0; variant < 3 && !acquired; variant++)
            {
                Pose shot = ResolveRoomPointShot(variant, out Vector3 focus);
                acquired = roomCamera.BeginAuthored(CameraFollow, attacker.transform, Hero.transform, shot, RoomPointStartFov, focus,
                    entrySeconds: RoomFocusSeconds, exitSeconds: .8f, smoothTransitions: true,
                    nearClipPlane: Mathf.Min(CameraFollow.Camera.nearClipPlane, .025f));
                if (acquired) roomPointFocus = focus;
            }
            if (!acquired || !roomInput.TryCaptureAndDisable(Player.Interactor, CameraFollow, null,
                BarMinigameModalLockOptions.FullscreenWithPause))
            { CancelRoomAttackSequence(); return false; }
            roomAttackSequence++;
            ReleaseFreePistolAim();
            Hero.SuspendPistolInput(); Hero.CancelPendingKick("room_attack");
            CancelHeldHeroCharge(); ResetChargeInput();
            Hero.State.CancelAction();
            if (!Hero.TryBeginRoomHeadPain()) { CancelRoomAttackSequence(); return false; }
            ResetAllOpponentMovement();
            hitStopSubsteps = 0;
            AmmoSpawns?.SetInteractionEnabled(false);
            SetDuelFrozen(false);
            SetRoomOpponentSuppression(true);
            roomTableau.SetVisible(true);
            attacker.SampleRoomAttackPose(CombatRoomAttackPose.PointHero, 0f);
            BodyEffects?.SynchronizeActor(attacker, attacker.LastImpact);
            SetRoomPhase(CombatRoomSequencePhase.FocusEntry);
            return true;
        }

        private Pose ResolveRoomPointShot(int variant, out Vector3 focus)
        {
            Vector3 finger = roomAttacker.RoomPointingTipWorld;
            Vector3 head = roomHead.position + Vector3.up * .04f;
            focus = head;
            Vector3 axis = roomAttacker.RoomPointingDirectionWorld;
            float scale = roomAttacker.PresentationScale;
            float distance = (.76f + .22f * variant) * scale;
            Vector3 position = finger + axis * distance;
            roomPointStartCamera = RollRoomCamera(position, head, RoomPointStartRoll);
            // A real push compensates the widening lens at the head. The much
            // nearer index grows in the foreground while the head keeps its size.
            float lensRatio = Mathf.Tan(RoomPointStartFov * .5f * Mathf.Deg2Rad) /
                Mathf.Tan(RoomPointEndFov * .5f * Mathf.Deg2Rad);
            // Solve the compensating distance on the index ray itself: the
            // finger keeps pointing into the lens throughout the whole push.
            Vector3 fingerFromHead = finger - head;
            float projection = Vector3.Dot(fingerFromHead, axis);
            float perpendicularSquared = Mathf.Max(0f, fingerFromHead.sqrMagnitude - projection * projection);
            float targetSquared = (position - head).sqrMagnitude * lensRatio * lensRatio;
            float endDistance = Mathf.Clamp(Mathf.Sqrt(Mathf.Max(0f, targetSquared - perpendicularSquared)) - projection,
                .28f * scale, distance);
            Vector3 destination = finger + axis * endDistance;
            roomPointEndCamera = RollRoomCamera(destination, head, RoomPointEndRoll);
            return roomPointStartCamera;
        }

        private static Pose RollRoomCamera(Vector3 position, Vector3 focus, float roll) => new Pose(position,
            Quaternion.LookRotation(focus - position, Vector3.up) * Quaternion.AngleAxis(roll, Vector3.forward));

        private static Pose WithRoomRoll(Pose pose, float roll) => new Pose(pose.position,
            pose.rotation * Quaternion.AngleAxis(roll, Vector3.forward));

        private void SampleRoomPointCamera(float progress)
        {
            Vector3 position = Vector3.Lerp(roomPointStartCamera.position, roomPointEndCamera.position, progress);
            roomCamera.SetAuthoredShot(RollRoomCamera(position, roomPointFocus,
                Mathf.Lerp(RoomPointStartRoll, RoomPointEndRoll, progress)),
                Mathf.Lerp(RoomPointStartFov, RoomPointEndFov, progress), roomPointFocus, true);
        }

        private void SetRoomPhase(CombatRoomSequencePhase phase)
        { RoomAttackPhase = phase; roomPhaseElapsed = 0f; }

        private void SetRoomOpponentSuppression(bool suppressed)
        {
            foreach (CombatActor actor in preparedOpponents)
                actor.SetRoomAttackSuppressed(suppressed && actor != roomAttacker);
        }

        /// <summary>The presentation owns input, while the ordinary simulation keeps running.</summary>
        private void AdvanceRoomAttackSequence(float seconds)
        {
            if (RoomAttackPhase == CombatRoomSequencePhase.Idle) return;
            if (roomAttacker == null || !roomAttacker.gameObject.activeInHierarchy ||
                !roomAttacker.RoomAttackPoseActive || Hero.State.IsDefeated ||
                RoomAttackCinematicActive && !Hero.RoomHeadPainActive ||
                SceneTransitionService.IsTransitioning)
            { CancelRoomAttackSequence(); return; }
            if (PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused || !pistolApplicationFocused) return;
            roomPhaseElapsed += seconds;
            if (RoomAttackCinematicActive && RoomAttackPhase != CombatRoomSequencePhase.HeadPain)
            {
                float progress = RoomAttackPhase == CombatRoomSequencePhase.FocusEntry
                    ? RoomHeadGrabHeldProgress * Mathf.Clamp01(roomPhaseElapsed / RoomHeadGrabEnterSeconds)
                    : RoomHeadGrabHeldProgress;
                if (!Hero.SampleRoomHeadPain(progress)) { CancelRoomAttackSequence(); return; }
            }
            if (RoomAttackPhase == CombatRoomSequencePhase.HeadPain || RoomAttackPhase == CombatRoomSequencePhase.Laugh)
                roomLaughElapsed += seconds;
            switch (RoomAttackPhase)
            {
                case CombatRoomSequencePhase.FocusEntry:
                    roomAttacker.SampleRoomAttackPose(CombatRoomAttackPose.PointHero,
                        RoomPointRaisedProgress * Mathf.Clamp01(roomPhaseElapsed / RoomFocusSeconds));
                    roomTableau.SetLightAmount(RoomEase(roomPhaseElapsed / RoomFocusSeconds));
                    roomCamera.Tick(seconds);
                    if (roomCamera.IsSettled) SetRoomPhase(CombatRoomSequencePhase.PointHero);
                    break;
                case CombatRoomSequencePhase.PointHero:
                    roomAttacker.SampleRoomAttackPose(CombatRoomAttackPose.PointHero, RoomPointRaisedProgress +
                        (1f - RoomPointRaisedProgress) * Mathf.Clamp01(roomPhaseElapsed / RoomPointSeconds));
                    SampleRoomPointCamera(RoomEase(roomPhaseElapsed / RoomPointSeconds));
                    if (roomPhaseElapsed >= RoomPointSeconds)
                    {
                        roomChestCamera = roomCamera.CurrentShotPose;
                        roomChestStartFov = roomCamera.CurrentShotFieldOfView;
                        roomChestStartNear = CameraFollow.Camera.nearClipPlane;
                        roomChestFocus = roomTableau.Focus;
                        Pose entry = roomTableau.EntryPose;
                        Vector3 inward = roomTableau.Inward;
                        roomApproachCamera = new Pose(entry.position - inward * (.25f * roomAttacker.PresentationScale), entry.rotation);
                        roomPathFirst = entry.position - inward * (.10f * roomAttacker.PresentationScale);
                        roomPathSecond = RoomSeatedCamera.position - inward * (.05f * roomAttacker.PresentationScale);
                        SetRoomPhase(CombatRoomSequencePhase.PointChest);
                    }
                    break;
                case CombatRoomSequencePhase.PointChest:
                    roomAttacker.SampleRoomAttackPose(CombatRoomAttackPose.PointChest, roomPhaseElapsed / RoomChestSeconds);
                    float chest = RoomEase(roomPhaseElapsed / RoomChestSeconds);
                    roomCamera.SetAuthoredShot(new Pose(Vector3.Lerp(roomChestCamera.position, roomApproachCamera.position, chest),
                        Quaternion.Slerp(roomChestCamera.rotation, WithRoomRoll(roomApproachCamera, RoomApproachRoll).rotation, chest)),
                        Mathf.Lerp(roomChestStartFov, RoomApproachFov, chest),
                        Vector3.Lerp(roomPointFocus, roomChestFocus, chest), true,
                        Mathf.Lerp(roomChestStartNear, RoomApproachNear, chest),
                        Mathf.Lerp(1f, Mathf.Sqrt(roomTableau.OpticalScale), chest));
                    if (roomPhaseElapsed >= RoomChestSeconds)
                    {
                        SetRoomPhase(CombatRoomSequencePhase.EnterRoom);
                    }
                    break;
                case CombatRoomSequencePhase.EnterRoom:
                    roomTableau.SampleShadow(Mathf.Min(roomPhaseElapsed, RoomTransferSeconds));
                    roomTableau.SampleSound(Mathf.Min(roomPhaseElapsed, RoomTransferSeconds));
                    ApplyRoomTravel(RoomEase(roomPhaseElapsed / RoomTransferSeconds));
                    if (roomPhaseElapsed >= RoomTransferSeconds)
                    {
                        roomTableau.Sample(0f);
                        SetRoomPhase(CombatRoomSequencePhase.SeatedHero);
                    }
                    break;
                case CombatRoomSequencePhase.SeatedHero:
                    roomTableau.Sample(roomPhaseElapsed);
                    roomTableau.SampleShadow(RoomTransferSeconds + Mathf.Min(roomPhaseElapsed, RoomShotSeconds));
                    roomTableau.SampleSound(RoomTransferSeconds + Mathf.Min(roomPhaseElapsed, RoomShotSeconds));
                    float seatedProgress = RoomEase(roomPhaseElapsed / RoomShotSeconds);
                    float facePush = RoomEase((roomPhaseElapsed / RoomShotSeconds - .3f) / .7f);
                    Pose wide = RoomSeatedCamera, face = roomTableau.FaceCameraPose;
                    var seatedShot = new Pose(Vector3.Lerp(wide.position, face.position, facePush),
                        Quaternion.Slerp(wide.rotation, face.rotation, facePush));
                    roomCamera.SetAuthoredShot(WithRoomRoll(seatedShot,
                        Mathf.Lerp(RoomSeatedStartRoll, RoomSeatedEndRoll, seatedProgress)),
                        Mathf.Lerp(RoomSeatedStartFov, RoomSeatedEndFov, seatedProgress),
                        Vector3.Lerp(roomChestFocus, roomTableau.FaceFocus, facePush), true,
                        RoomSeatedNear, roomTableau.OpticalScale);
                    if (roomPhaseElapsed >= RoomShotSeconds)
                    {
                        SetRoomPhase(CombatRoomSequencePhase.ReturnRoom);
                    }
                    break;
                case CombatRoomSequencePhase.ReturnRoom:
                    roomTableau.SampleShadow(RoomTransferSeconds + RoomShotSeconds + Mathf.Min(roomPhaseElapsed, RoomWithdrawSeconds));
                    roomTableau.SampleSound(RoomTransferSeconds + RoomShotSeconds + Mathf.Min(roomPhaseElapsed, RoomWithdrawSeconds));
                    ApplyRoomTravel(1f - RoomEase(roomPhaseElapsed / RoomWithdrawSeconds), withdrawing: true);
                    if (roomPhaseElapsed >= RoomWithdrawSeconds)
                    {
                        roomCamera.BeginExit();
                        SetRoomPhase(CombatRoomSequencePhase.Return);
                    }
                    break;
                case CombatRoomSequencePhase.Return:
                    roomCamera.Tick(seconds);
                    if (!roomCamera.IsFinished) break;
                    roomTableau.SetVisible(false);
                    if (!Hero.ReceiveRoomAttack(roomAttacker, roomAttackSequence) || Hero.State.IsDefeated || !Hero.RoomHeadPainActive)
                    { CancelRoomAttackSequence(); break; }
                    SetRoomPhase(CombatRoomSequencePhase.HeadPain);
                    roomLaughElapsed = 0f;
                    if (!roomAttacker.BeginRoomLaughRecovery()) CancelRoomAttackSequence();
                    else BodyEffects?.SynchronizeActor(roomAttacker, roomAttacker.LastImpact);
                    break;
                case CombatRoomSequencePhase.HeadPain:
                    if (!Hero.SampleRoomHeadPain(Mathf.Lerp(RoomHeadGrabHeldProgress, 1f, roomPhaseElapsed / RoomShotSeconds)) ||
                        !roomAttacker.SampleRoomAttackPose(CombatRoomAttackPose.Laugh, roomLaughElapsed / RoomLaughSeconds))
                    { CancelRoomAttackSequence(); break; }
                    if (roomPhaseElapsed >= RoomShotSeconds) roomReleasePending = true;
                    break;
                case CombatRoomSequencePhase.Laugh:
                    if (!roomAttacker.SampleRoomAttackPose(CombatRoomAttackPose.Laugh, roomLaughElapsed / RoomLaughSeconds) ||
                        roomLaughElapsed >= RoomLaughSeconds) CancelRoomAttackSequence();
                    break;
            }
        }

        private float RoomApproachNear => Mathf.Min(roomCamera.OriginalNearClipPlane, .02f);
        private float RoomSeatedNear => Mathf.Min(RoomApproachNear, .0025f);

        private Pose RoomSeatedCamera
        {
            get
            {
                Pose pose = roomTableau.CameraPose;
                // Keep the existing seated silhouette inside the tighter lens.
                pose.position = roomTableau.Focus + (pose.position - roomTableau.Focus) * RoomSeatedFramingScale;
                return pose;
            }
        }

        private void ApplyRoomTravel(float progress, bool withdrawing = false)
        {
            Pose seated = withdrawing ? roomTableau.FaceCameraPose : RoomSeatedCamera;
            Vector3 second = withdrawing ? seated.position - roomTableau.Inward * (.05f * roomAttacker.PresentationScale) : roomPathSecond;
            float reverse = 1f - progress;
            Vector3 position = reverse * reverse * reverse * roomApproachCamera.position +
                3f * reverse * reverse * progress * roomPathFirst +
                3f * reverse * progress * progress * second + progress * progress * progress * seated.position;
            roomCamera.SetAuthoredShot(new Pose(position,
                Quaternion.Slerp(roomApproachCamera.rotation, seated.rotation, progress) * Quaternion.AngleAxis(
                    Mathf.Lerp(RoomApproachRoll, withdrawing ? RoomSeatedEndRoll : RoomSeatedStartRoll, progress), Vector3.forward)),
                Mathf.Lerp(RoomApproachFov, withdrawing ? RoomSeatedEndFov : RoomSeatedStartFov, progress),
                withdrawing ? Vector3.Lerp(roomChestFocus, roomTableau.FaceFocus, progress) : roomChestFocus,
                true, Mathf.Lerp(RoomApproachNear, RoomSeatedNear, progress),
                Mathf.Lerp(Mathf.Sqrt(roomTableau.OpticalScale), roomTableau.OpticalScale, progress));
        }

        private static float RoomEase(float progress)
        {
            float t = Mathf.Clamp01(progress);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        private void FinishRoomAttackPresentationFrame()
        {
            if (!roomReleasePending || PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused || !pistolApplicationFocused) return;
            roomReleasePending = false;
            Hero.EndRoomHeadPain();
            roomCamera.RestoreImmediate(); roomInput.Restore();
            SetRoomOpponentSuppression(false);
            SetDuelFrozen(false); ResetChargeInput();
            requireAttackRelease = requirePistolAimRelease = requireMouseAttackRelease = requireMouseAimRelease = true;
            SetRoomPhase(CombatRoomSequencePhase.Laugh);
            // Preserve the laugh already spent during head pain, including a
            // hitch that reaches its end before the terminal pain frame renders.
            if (roomLaughElapsed >= RoomLaughSeconds) CancelRoomAttackSequence();
        }

        private bool IsRoomAttackerRecovering(CombatActor actor) => actor != null &&
            (actor.RoomAttackCooldownActive || roomAttacker == actor && RoomAttackPhase == CombatRoomSequencePhase.Laugh);

        private bool HasRoomAttackCooldown
        {
            get
            {
                foreach (CombatActor actor in preparedOpponents)
                    if (actor != null && actor.RoomAttackCooldownActive) return true;
                return false;
            }
        }

        private void ClearRoomAttackCooldowns()
        {
            foreach (CombatActor actor in preparedOpponents)
                if (actor != null) actor.ClearRoomAttackCooldown();
        }

        private void CancelRoomAttackSequence()
        {
            bool owned = roomInput.IsLocked;
            roomReleasePending = false;
            roomCamera.RestoreImmediate(); roomInput.Restore();
            roomTableau?.SetVisible(false);
            roomAttacker?.EndRoomAttackPose(); Hero?.EndRoomHeadPain();
            SetRoomOpponentSuppression(false);
            roomAttacker = null; roomHead = null;
            roomLaughElapsed = 0f;
            SetRoomPhase(CombatRoomSequencePhase.Idle);
            if (owned && IsInitialized)
            {
                SetDuelFrozen(false); ResetChargeInput();
                requireAttackRelease = requirePistolAimRelease = requireMouseAttackRelease = requireMouseAimRelease = true;
            }
        }
    }
}
