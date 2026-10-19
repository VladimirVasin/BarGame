using UnityEngine;

namespace BarPromenade
{
    public enum CombatRoomSequencePhase { Idle, FocusEntry, PointHero, PointChest, EnterRoom, SeatedHero, ReturnRoom, Return, HeadPain, Laugh }

    public sealed partial class CombatTestRoot
    {
        internal const float RoomShotSeconds = 2f, RoomLaughSeconds = 5f;
        internal const float RoomFocusSeconds = .9f, RoomChestSeconds = .6f, RoomTransferSeconds = 1f, RoomWithdrawSeconds = .6f;
        private readonly ContextualCameraDirector roomCamera = new ContextualCameraDirector();
        private readonly BarMinigameModalLock roomInput = new BarMinigameModalLock();
        private CombatRoomAttackTableau roomTableau;
        private CombatActor roomAttacker;
        private Transform roomHead;
        private float roomPhaseElapsed;
        private int roomAttackSequence;
        private Pose roomChestCamera, roomApproachCamera;
        private Vector3 roomChestFocus, roomPointFocus, roomPathFirst, roomPathSecond;
        private bool roomReleasePending;

        public CombatRoomSequencePhase RoomAttackPhase { get; private set; }
        public float RoomAttackPhaseElapsed => roomPhaseElapsed;
        public bool RoomAttackCinematicActive => RoomAttackPhase != CombatRoomSequencePhase.Idle && RoomAttackPhase != CombatRoomSequencePhase.Laugh;
        internal bool RoomAttackTableauVisible => roomTableau != null && roomTableau.Visible;
        internal Player3DAssetRegistry RoomAttackHeroImage => roomTableau?.Hero;

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
                acquired = roomCamera.BeginAuthored(CameraFollow, attacker.transform, Hero.transform, shot, 43f, focus,
                    entrySeconds: RoomFocusSeconds, exitSeconds: .8f, smoothTransitions: true);
                if (acquired) roomPointFocus = focus;
            }
            if (!acquired || !roomInput.TryCaptureAndDisable(Player.Interactor, CameraFollow, null,
                BarMinigameModalLockOptions.FullscreenWithPause))
            { CancelRoomAttackSequence(); return false; }
            roomAttackSequence++;
            ReleaseFreePistolAim();
            Hero.SuspendPistolInput(); Hero.CancelPendingKick("room_attack");
            CancelHeldHeroCharge(); ResetChargeInput();
            ResetAllOpponentMovement();
            pendingSeconds = 0d; hitStopSubsteps = 0;
            AmmoSpawns?.SetInteractionEnabled(false);
            SetDuelFrozen(true);
            roomTableau.SetVisible(true);
            attacker.SampleRoomAttackPose(CombatRoomAttackPose.PointHero, 0f);
            BodyEffects?.SynchronizeActor(attacker, attacker.LastImpact);
            SetRoomPhase(CombatRoomSequencePhase.FocusEntry);
            return true;
        }

        private Pose ResolveRoomPointShot(int variant, out Vector3 focus)
        {
            Vector3 finger = roomAttacker.RoomHandTipWorld(false);
            Vector3 head = roomHead.position + Vector3.up * .04f;
            focus = Vector3.Lerp(head, finger, .45f);
            float separation = Vector3.Distance(head, finger);
            float distance = Mathf.Max(1.05f, (separation + .28f) / (2f * Mathf.Tan(43f * .5f * Mathf.Deg2Rad)));
            float side = variant == 1 ? -1f : variant == 2 ? 1.5f : 1f;
            Vector3 direction = (roomAttacker.transform.forward * .65f + roomAttacker.transform.right * side).normalized;
            Vector3 position = focus + direction * distance;
            return new Pose(position, Quaternion.LookRotation(focus - position, Vector3.up));
        }

        private void SetRoomPhase(CombatRoomSequencePhase phase)
        { RoomAttackPhase = phase; roomPhaseElapsed = 0f; }

        /// <summary>Runs outside the gameplay gate held by this sequence. Actual pause still wins.</summary>
        private void AdvanceRoomAttackSequence(float seconds)
        {
            if (RoomAttackPhase == CombatRoomSequencePhase.Idle) return;
            if (roomAttacker == null || !roomAttacker.gameObject.activeInHierarchy ||
                !roomAttacker.RoomAttackPoseActive || Hero.State.IsDefeated ||
                SceneTransitionService.IsTransitioning)
            { CancelRoomAttackSequence(); return; }
            if (PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused || !pistolApplicationFocused) return;
            roomPhaseElapsed += seconds;
            switch (RoomAttackPhase)
            {
                case CombatRoomSequencePhase.FocusEntry:
                    roomAttacker.SampleRoomAttackPose(CombatRoomAttackPose.PointHero,
                        .15f * Mathf.Clamp01(roomPhaseElapsed / RoomFocusSeconds));
                    roomTableau.SetLightAmount(RoomEase(roomPhaseElapsed / RoomFocusSeconds));
                    roomCamera.Tick(seconds);
                    if (roomCamera.IsSettled) SetRoomPhase(CombatRoomSequencePhase.PointHero);
                    break;
                case CombatRoomSequencePhase.PointHero:
                    roomAttacker.SampleRoomAttackPose(CombatRoomAttackPose.PointHero, .15f + .85f * Mathf.Clamp01(roomPhaseElapsed / RoomShotSeconds));
                    roomCamera.Tick(seconds);
                    if (roomPhaseElapsed >= RoomShotSeconds)
                    {
                        roomChestCamera = roomCamera.CurrentShotPose;
                        roomChestFocus = roomTableau.Focus;
                        Pose entry = roomTableau.EntryPose;
                        Vector3 inward = roomTableau.Inward;
                        roomApproachCamera = new Pose(entry.position - inward * (.25f * roomAttacker.PresentationScale), entry.rotation);
                        roomPathFirst = entry.position - inward * (.10f * roomAttacker.PresentationScale);
                        roomPathSecond = roomTableau.CameraPose.position - inward * (.05f * roomAttacker.PresentationScale);
                        SetRoomPhase(CombatRoomSequencePhase.PointChest);
                    }
                    break;
                case CombatRoomSequencePhase.PointChest:
                    roomAttacker.SampleRoomAttackPose(CombatRoomAttackPose.PointChest, roomPhaseElapsed / RoomChestSeconds);
                    float chest = RoomEase(roomPhaseElapsed / RoomChestSeconds);
                    roomCamera.SetAuthoredShot(new Pose(Vector3.Lerp(roomChestCamera.position, roomApproachCamera.position, chest),
                        Quaternion.Slerp(roomChestCamera.rotation, roomApproachCamera.rotation, chest)), Mathf.Lerp(43f, 50f, chest),
                        Vector3.Lerp(roomPointFocus, roomChestFocus, chest), true,
                        Mathf.Lerp(roomCamera.OriginalNearClipPlane, RoomApproachNear, chest),
                        Mathf.Lerp(1f, Mathf.Sqrt(roomTableau.OpticalScale), chest));
                    if (roomPhaseElapsed >= RoomChestSeconds)
                    {
                        SetRoomPhase(CombatRoomSequencePhase.EnterRoom);
                    }
                    break;
                case CombatRoomSequencePhase.EnterRoom:
                    ApplyRoomTravel(RoomEase(roomPhaseElapsed / RoomTransferSeconds));
                    if (roomPhaseElapsed >= RoomTransferSeconds)
                    {
                        roomTableau.Sample(0f);
                        SetRoomPhase(CombatRoomSequencePhase.SeatedHero);
                    }
                    break;
                case CombatRoomSequencePhase.SeatedHero:
                    roomTableau.Sample(roomPhaseElapsed);
                    roomCamera.Tick(seconds);
                    if (roomPhaseElapsed >= RoomShotSeconds)
                    {
                        SetRoomPhase(CombatRoomSequencePhase.ReturnRoom);
                    }
                    break;
                case CombatRoomSequencePhase.ReturnRoom:
                    ApplyRoomTravel(1f - RoomEase(roomPhaseElapsed / RoomWithdrawSeconds));
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
                    if (!Hero.ReceiveRoomAttack(roomAttacker, roomAttackSequence) || Hero.State.IsDefeated || !Hero.TryBeginRoomHeadPain())
                    { CancelRoomAttackSequence(); break; }
                    SetRoomPhase(CombatRoomSequencePhase.HeadPain);
                    break;
                case CombatRoomSequencePhase.HeadPain:
                    if (!Hero.SampleRoomHeadPain(roomPhaseElapsed / RoomShotSeconds))
                    { CancelRoomAttackSequence(); break; }
                    if (roomPhaseElapsed >= RoomShotSeconds) roomReleasePending = true;
                    break;
                case CombatRoomSequencePhase.Laugh:
                    if (!roomAttacker.SampleRoomAttackPose(CombatRoomAttackPose.Laugh, roomPhaseElapsed / RoomLaughSeconds) ||
                        roomPhaseElapsed >= RoomLaughSeconds) CancelRoomAttackSequence();
                    break;
            }
        }

        private float RoomApproachNear => Mathf.Min(roomCamera.OriginalNearClipPlane, .02f);

        private void ApplyRoomTravel(float progress)
        {
            Pose seated = roomTableau.CameraPose;
            float reverse = 1f - progress;
            Vector3 position = reverse * reverse * reverse * roomApproachCamera.position +
                3f * reverse * reverse * progress * roomPathFirst +
                3f * reverse * progress * progress * roomPathSecond + progress * progress * progress * seated.position;
            roomCamera.SetAuthoredShot(new Pose(position,
                Quaternion.Slerp(roomApproachCamera.rotation, seated.rotation, progress)), Mathf.Lerp(50f, 56f, progress),
                roomChestFocus, true, Mathf.Lerp(RoomApproachNear, Mathf.Min(RoomApproachNear, .0025f), progress),
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
            // The visible pain action has already spent the hit's recovery.
            // Do not append a second hidden stagger after controls return.
            Hero.State.Advance(RoomShotSeconds, false);
            roomCamera.RestoreImmediate(); roomInput.Restore();
            SetDuelFrozen(false); ResetChargeInput();
            requireAttackRelease = requirePistolAimRelease = requireMouseAttackRelease = requireMouseAimRelease = true;
            pendingSeconds = 0d;
            SetRoomPhase(CombatRoomSequencePhase.Laugh);
            if (!roomAttacker.BeginRoomLaughRecovery()) CancelRoomAttackSequence();
            else BodyEffects?.SynchronizeActor(roomAttacker, roomAttacker.LastImpact);
        }

        private bool IsRoomAttackerRecovering(CombatActor actor) => roomAttacker == actor && RoomAttackPhase == CombatRoomSequencePhase.Laugh;

        private void CancelRoomAttackSequence()
        {
            bool owned = roomInput.IsLocked;
            roomReleasePending = false;
            roomCamera.RestoreImmediate(); roomInput.Restore();
            roomTableau?.SetVisible(false);
            roomAttacker?.EndRoomAttackPose(); Hero?.EndRoomHeadPain();
            roomAttacker = null; roomHead = null;
            SetRoomPhase(CombatRoomSequencePhase.Idle);
            if (owned && IsInitialized)
            {
                SetDuelFrozen(false); pendingSeconds = 0d; ResetChargeInput();
                requireAttackRelease = requirePistolAimRelease = requireMouseAttackRelease = requireMouseAimRelease = true;
            }
        }
    }
}
