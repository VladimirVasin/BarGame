using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        internal const float DoorAmbushMotionRate = .5f;
        private const float DoorAmbushHaulSourceZ = 2f;
        private readonly Transform[] doorCableGuides = new Transform[6];
        private readonly Transform[] doorRoofGuides = new Transform[2];
        private readonly Transform[] doorLegCable = new Transform[4];
        private readonly Vector3[] doorLegCableScales = new Vector3[4];
        private readonly bool[] doorOriginalSupportVisibility = new bool[6 * CraneCableSegments];
        private readonly bool[] doorOriginalOwnSupportVisibility = new bool[6 * CraneCableSegments];
        private CombatActor doorSuspensionSource, doorAmbushVictim;
        private AnimationClip doorAmbushClip, doorAmbushHoldClip, doorAmbushRetreatClip;
        private Transform doorAmbushLeg;
        private Pose doorOriginalPose, doorCapturePose, doorReleasePose;
        private Vector3 doorCapturePoint, doorAmbushPullOrigin;
        private Vector3 doorReleasedHookEye;
        private Quaternion doorReleasedHookRotation;
        private CombatCranePhase doorOriginalPhase;
        private CombatCraneHookPhase doorOriginalHookPhase;
        private bool doorOriginalActive, doorOriginalEnabled, doorOriginalHost, doorOriginalRails;
        private bool doorOriginalHead, doorOriginalBridge, doorOriginalHook, doorOriginalCuff, doorOriginalAttackCable;
        private bool doorOriginalBody, doorOriginalPatrol, doorOriginalRemnantEnabled;
        private bool doorPresentationVisible;
        private Renderer[] doorBodyRenderers;
        private bool[] doorOriginalBodyVisibility;
        private Vector3[] doorSavedPositions, doorSavedScales, doorPreviousPositions, doorPreviousScales;
        private Quaternion[] doorSavedRotations, doorPreviousRotations;
        private float doorClipSeconds, doorCaptureSeconds, doorReleaseSeconds, doorOriginalCraneClock;
        private bool doorLegContactCommitted;
        private float doorRetreatStallSeconds;
        private float doorHoistRootCap, doorHoistAnchorHeight, doorTopHoldElapsed, doorGuideRelease;
        private float doorHoistStallSeconds;
        private float doorHoldAnimationSeconds;
        private int doorRetreatTerminalFrame = -1;

        internal bool DoorAmbushPrepared { get; private set; }
        internal bool DoorAmbushCaptured => DoorAmbushPrepared && craneVictim != null;
        internal bool DoorAmbushReleased { get; private set; }
        internal bool DoorAmbushBlocked { get; private set; }
        internal bool DoorAmbushVisible => DoorAmbushPrepared && doorPresentationVisible;
        internal float DoorAmbushCaptureElapsed => doorCaptureSeconds;
        internal bool DoorAmbushHoistComplete { get; private set; }
        internal bool DoorAmbushHangingGameplay { get; private set; }
        internal float DoorAmbushTopHoldElapsed => doorTopHoldElapsed;
        internal float DoorAmbushHoistTargetHeight => DoorRoofCentre.y - .12f;
        internal Vector3 DoorAmbushVictimTarget => craneVictim != null ? craneVictim.RestraintAnchor : doorAmbushPullOrigin;
        internal float DoorAmbushHoistProgress => craneVictim != null ? Mathf.InverseLerp(doorCapturePoint.y,
            DoorAmbushHoistTargetHeight - .45f, craneVictim.RestraintAnchor.y) : 0f;
        internal bool DoorAmbushLegCapture => DoorAmbushPrepared && doorLegContactCommitted;
        internal bool DoorAmbushVictimInverted => craneVictim != null &&
            craneVictim.Ragdoll.PhysicsController.TryGetCombatBone(Player3DAnatomicalPart.Head, out Transform head) &&
            craneVictim.Ragdoll.PhysicsController.TryGetCombatBone(Player3DAnatomicalPart.Pelvis, out Transform pelvis) &&
            head.position.y < pelvis.position.y - .15f && pelvis.position.y < craneVictim.RestraintAnchor.y - .35f;
        // The first authored fragment ends at hanging gameplay. Escape is a
        // future action and receives neither a prompt nor buffered presses.
        internal bool DoorAmbushEscapeEnabled => false;
        internal bool DoorAmbushRetreatComplete => DoorAmbushReleased && doorRetreatTerminalFrame >= 0 &&
            Time.frameCount > doorRetreatTerminalFrame;
        internal bool DoorAmbushSettled => DoorAmbushRetreatComplete && (doorAmbushVictim == null ||
            doorAmbushVictim.State.IsDefeated || !doorAmbushVictim.IsRestrained &&
            !doorAmbushVictim.IsKnockedDown && !doorAmbushVictim.IsRagdollActive);

        internal bool ConfigureDoorAmbushCableGuides(Transform[] guides)
        {
            if (DoorAmbushPrepared || guides == null || guides.Length != doorCableGuides.Length + doorRoofGuides.Length) return false;
            for (int i = 0; i < guides.Length; i++) if (guides[i] == null) return false;
            for (int i = 0; i < doorCableGuides.Length; i++) doorCableGuides[i] = guides[i];
            for (int i = 0; i < doorRoofGuides.Length; i++) doorRoofGuides[i] = guides[doorCableGuides.Length + i];
            return true;
        }

        /// <summary>Loans one retained body and the existing fixed machinery; it never joins the combat roster.</summary>
        internal bool PrepareDoorAmbush(Pose rootPose, CombatActor existingSuspension = null)
        {
            if (DoorAmbushPrepared || !IsCrane || craneSuspensionHost == null ||
                !FiniteRestraintVector(rootPose.position) || !FiniteDoorRotation(rootPose.rotation)) return false;
            for (int i = 0; i < doorCableGuides.Length; i++) if (doorCableGuides[i] == null) return false;
            for (int i = 0; i < doorRoofGuides.Length; i++) if (doorRoofGuides[i] == null) return false;
            InitializeNpcAnimation();
            EnsureDoorAmbushBuffers();
            doorOriginalPose = new Pose(transform.position, transform.rotation);
            doorOriginalPhase = CranePhase; doorOriginalHookPhase = CraneHookPhase;
            doorOriginalCraneClock = craneClock;
            doorOriginalActive = gameObject.activeSelf; doorOriginalEnabled = enabled;
            doorOriginalHost = craneSuspensionHost.activeSelf; doorOriginalRails = craneRails.activeSelf;
            doorOriginalHead = craneSuspensionHead.activeSelf; doorOriginalBridge = craneBridge.activeSelf;
            doorOriginalHook = craneHookFrame.activeSelf; doorOriginalCuff = craneCuffFrame.activeSelf;
            doorOriginalAttackCable = craneAttackCable.gameObject.activeSelf;
            doorOriginalBody = Body.enabled; doorOriginalPatrol = CranePatrolEnabled;
            for (int i = 0; i < doorBodyRenderers.Length; i++)
                doorOriginalBodyVisibility[i] = doorBodyRenderers[i].enabled;
            SaveDoorBones(doorSavedPositions, doorSavedRotations, doorSavedScales);
            doorSuspensionSource = existingSuspension != null && existingSuspension.IsCrane ? existingSuspension : this;
            doorOriginalRemnantEnabled = doorSuspensionSource.craneRemnant.enabled;
            for (int i = 0; i < 6; i++)
                for (int segment = 0; segment < CraneCableSegments; segment++)
                {
                    Transform piece = doorSuspensionSource.craneCablePieces[i, segment];
                    doorOriginalSupportVisibility[i * CraneCableSegments + segment] = piece.gameObject.activeSelf;
                    doorOriginalOwnSupportVisibility[i * CraneCableSegments + segment] =
                        craneCablePieces[i, segment].gameObject.activeSelf;
                }
            DoorAmbushPrepared = true;
            DoorAmbushReleased = DoorAmbushBlocked = false;
            doorCaptureSeconds = doorReleaseSeconds = doorClipSeconds = 0f;
            doorLegContactCommitted = false;
            doorRetreatStallSeconds = 0f; doorRetreatTerminalFrame = -1;
            doorTopHoldElapsed = doorGuideRelease = doorHoistStallSeconds = 0f;
            doorHoldAnimationSeconds = 0f;
            DoorAmbushHoistComplete = false;
            DoorAmbushHangingGameplay = false;
            doorAmbushVictim = null; doorAmbushLeg = null;
            doorAmbushClip = doorAmbushHoldClip = doorAmbushRetreatClip = null;
            CranePatrolEnabled = false;
            State.CancelAction();
            collectSweep = sweepValid = collectShove = collectKick = craneContactValid = false;
            StopCraneVoices();
            CancelPoseBlend();
            enabled = true;
            gameObject.SetActive(true);
            Body.enabled = false;
            craneSuspensionHost.SetActive(true);
            bool ownMachine = doorSuspensionSource == this;
            craneRails.SetActive(ownMachine); craneBridge.SetActive(ownMachine); craneSuspensionHead.SetActive(ownMachine);
            craneHookFrame.SetActive(true); craneCuffFrame.SetActive(true); craneAttackCable.gameObject.SetActive(false);
            craneHookCollider.enabled = craneCuffCollider.enabled = false;
            craneHookBody.isKinematic = craneCuffBody.isKinematic = true;
            craneHookBody.useGravity = craneCuffBody.useGravity = false;
            CraneHookPhase = CombatCraneHookPhase.Stowed;
            SetCraneRopesVisible(true);
            transform.SetPositionAndRotation(rootPose.position, rootPose.rotation);
            SetDoorAmbushVisible(false);
            return true;
        }

        internal bool CommitDoorAmbushHangingGameplay()
        {
            if (!DoorAmbushCaptured || !DoorAmbushHoistComplete) return false;
            DoorAmbushHangingGameplay = true;
            return true;
        }

        /// <summary>Reveal the staged body without disabling its ownership or the borrowed original machinery.</summary>
        internal void SetDoorAmbushVisible(bool visible)
        {
            if (!DoorAmbushPrepared) return;
            doorPresentationVisible = visible;
            for (int i = 0; i < doorBodyRenderers.Length; i++)
                if (doorBodyRenderers[i] != null)
                    doorBodyRenderers[i].enabled = visible && doorOriginalBodyVisibility[i];
            craneHookFrame.SetActive(visible); craneCuffFrame.SetActive(visible);
            craneAttackCable.gameObject.SetActive(false);
            SetCraneRopesVisible(visible);
            if (!visible) SetDoorLegCableVisible(false);
            if (doorSuspensionSource != null && doorSuspensionSource != this)
            {
                // During the initial door shot the old free cables retain their
                // exact achieved pose and update. Their loan begins on reveal.
                doorSuspensionSource.craneRemnant.enabled = !visible && doorOriginalRemnantEnabled;
                for (int i = 0; i < 6; i++)
                    for (int segment = 0; segment < CraneCableSegments; segment++)
                        doorSuspensionSource.craneCablePieces[i, segment].gameObject.SetActive(
                            !visible && doorOriginalSupportVisibility[i * CraneCableSegments + segment]);
            }
            else if (doorSuspensionSource != null)
                doorSuspensionSource.craneRemnant.enabled = !visible && doorOriginalRemnantEnabled;
            if (visible) PresentDoorAmbushCables();
        }

        internal bool SampleDoorAmbush(AnimationClip clip, float clipSeconds, Pose rootPose,
            float hookContact01, CombatActor victim)
        {
            if (!DoorAmbushPrepared || DoorAmbushCaptured || clip == null || !float.IsFinite(clipSeconds) ||
                !float.IsFinite(hookContact01) || !FiniteRestraintVector(rootPose.position) ||
                !FiniteDoorRotation(rootPose.rotation)) return false;
            doorAmbushClip = clip;
            doorClipSeconds = Mathf.Clamp(clipSeconds, 0f, clip.length);
            doorAmbushVictim = victim;
            if (victim != null && victim.Ragdoll != null)
                victim.Ragdoll.PhysicsController.TryGetCombatBone(Player3DAnatomicalPart.RightShin, out doorAmbushLeg);
            bool clear = SampleDoorAmbushPose(rootPose, clip, doorClipSeconds, sweep: false);
            PresentDoorAmbushCables();
            return clear;
        }

        /// <summary>One authored shin puncture, followed by the same physical leg attachment through inversion.</summary>
        internal bool BeginScriptedLegCapture(CombatActor victim, AnimationClip heroHoldClip,
            AnimationClip craneHoldClip, AnimationClip craneRetreatClip, bool applyDamage = true)
        {
            if (!DoorAmbushPrepared || DoorAmbushCaptured || DoorAmbushReleased || victim == null ||
                heroHoldClip == null || craneHoldClip == null || craneRetreatClip == null || heroHoldClip.length <= 0f ||
                craneHoldClip.length <= 0f || craneRetreatClip.length <= 0f || craneRetreatClip.isLooping || victim.Ragdoll == null ||
                !victim.Ragdoll.PhysicsController.TryGetCombatBone(Player3DAnatomicalPart.RightShin, out Transform shin) ||
                !victim.Ragdoll.PhysicsController.TryGetCombatBone(Player3DAnatomicalPart.RightFoot, out Transform foot)) return false;
            doorAmbushHoldClip = craneHoldClip;
            doorAmbushRetreatClip = craneRetreatClip;
            doorAmbushLeg = shin;
            doorAmbushVictim = victim;
            Vector3 axis = shin.position - foot.position;
            if (axis.sqrMagnitude < .0001f) return false;
            doorCapturePoint = doorAmbushPullOrigin = foot.position + axis.normalized * .14f;
            Vector3 localPoint = shin.InverseTransformPoint(doorCapturePoint);
            if (!victim.TryBeginCraneRestraint(this, Player3DAnatomicalPart.RightShin,
                localPoint, doorCapturePoint, heroHoldClip)) return false;
            craneVictim = victim;
            doorCapturePose = new Pose(transform.position, transform.rotation);
            doorCaptureSeconds = doorReleaseSeconds = 0f;
            CranePhase = CombatCranePhase.Capturing;
            SampleDoorAmbushPose(doorCapturePose, craneHoldClip, 0f, sweep: false);
            // Capture the actually rendered terminal hook before changing its
            // owner: the same frame/offset follows the real shin in PhysX.
            craneVictimHookRotation = Quaternion.Inverse(shin.rotation) * craneHookRotation;
            craneVictimPenetration = Quaternion.Inverse(shin.rotation) * (CraneHookPosition - doorCapturePoint);
            doorReleasedHookRotation = craneHookRotation;
            doorLegContactCommitted = true;
            CraneHookPhase = CombatCraneHookPhase.Embedded;
            if (applyDamage)
            {
                Vector3 direction = Vector3.down;
                int sequence = State.AttackSequence;
                MeleeHitResult result = victim.Receive(this, false, sequence, doorCapturePoint, -direction, direction,
                    State.AttackDamage, State.AttackBlockCost, 0f,
                    new MeleeHitLocation(MeleeBodyRegion.RightLeg, MeleeHitSide.Rear),
                    Player3DAnatomicalPart.RightShin, localPoint, weaponSpeed: 3.2f, physicalBody: true,
                    bodyRegion: BodyDamageRegion.RightShin);
                State.RecordAttackOutcome(result, sequence);
                PlayCraneContactSound(result);
                if (result is not (MeleeHitResult.Hit or MeleeHitResult.GuardBroken) || victim.State.IsDefeated)
                { ReleaseDoorAmbushCapture(); return false; }
            }
            // The sharp lift starts immediately at the terminal jab, while the
            // ankle remains below the real gallery ceiling during inward haul.
            if (!victim.BeginRestraintPhysics(doorAmbushPullOrigin))
            { ReleaseDoorAmbushCapture(); return false; }
            doorHoistRootCap = DoorSuspendedRootCap();
            doorHoistAnchorHeight = doorCapturePoint.y;
            PresentDoorAmbushCables();
            return true;
        }

        /// <summary>The adapter calls this once; the normal population tick is suspended for this episode.</summary>
        internal void AdvanceDoorAmbush(float seconds)
        {
            if (!DoorAmbushPrepared || !float.IsFinite(seconds) || seconds <= 0f ||
                presentationFrozen || PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused) return;
            // Bound authored motion and restraint handoffs even after a frame hitch.
            float remaining = seconds;
            float maximumStep = Mathf.Max(1f / 60f, seconds / 32f);
            while (remaining > .000001f)
            {
                float step = Mathf.Min(remaining, maximumStep);
                AdvanceDoorAmbushStep(step);
                remaining -= step;
            }
            doorAmbushVictim?.Present();
            PresentDoorAmbush();
        }

        private void AdvanceDoorAmbushStep(float seconds)
        {
            if (DoorAmbushCaptured)
            {
                doorCaptureSeconds += seconds;
                doorHoldAnimationSeconds = Mathf.Repeat(doorHoldAnimationSeconds + seconds * DoorAmbushMotionRate,
                    doorAmbushHoldClip.length);
                craneClock = doorCaptureSeconds;
                CombatActor victim = craneVictim;
                if (victim.State.IsDefeated || !victim.isActiveAndEnabled || !victim.IsRestrained)
                    ReleaseDoorAmbushCapture();
                else
                {
                    // The exit has a real gallery ceiling. Haul beneath its edge
                    // first; only the actual suspended ankle reaching the well
                    // permits the second lift through the open vertical shaft.
                    Vector3 wanted = doorCapturePose.position;
                    wanted.z = Mathf.Lerp(doorCapturePose.position.z, DoorAmbushHaulSourceZ,
                        Mathf.SmoothStep(0f, 1f, doorCaptureSeconds * DoorAmbushMotionRate / 2.15f));
                    bool insideWell = Mathf.Abs(victim.RestraintAnchor.z) < 5.2f &&
                        Mathf.Abs(victim.RestraintAnchor.x) < 5.2f && DoorSourceInsideWell();
                    if (insideWell)
                        doorGuideRelease = Mathf.MoveTowards(doorGuideRelease, 1f, seconds * DoorAmbushMotionRate);
                    bool releasedGuides = insideWell && doorGuideRelease >= 1f &&
                        Mathf.Abs(transform.position.z - DoorAmbushHaulSourceZ) < .03f;
                    wanted.y = releasedGuides ? Mathf.MoveTowards(transform.position.y,
                        doorHoistRootCap, seconds * 2.2f * DoorAmbushMotionRate) : transform.position.y;
                    var pose = new Pose(wanted, doorCapturePose.rotation);
                    float previousHeight = doorHoistAnchorHeight;
                    bool accepted = SampleDoorAmbushPose(pose, doorAmbushHoldClip,
                        doorHoldAnimationSeconds, sweep: true);
                    if (accepted)
                    {
                        Vector3 floorPull = doorCapturePoint + Vector3.ProjectOnPlane(transform.position - doorCapturePose.position, Vector3.up);
                        if (releasedGuides)
                            doorHoistAnchorHeight = Mathf.MoveTowards(doorHoistAnchorHeight,
                                DoorAmbushHoistTargetHeight, seconds * 2.2f * DoorAmbushMotionRate);
                        else
                            doorHoistAnchorHeight = Mathf.Lerp(doorCapturePoint.y, 2.35f,
                                Mathf.SmoothStep(0f, 1f, doorCaptureSeconds / .30f));
                        float hoist = Mathf.InverseLerp(doorCapturePoint.y, DoorAmbushHoistTargetHeight, doorHoistAnchorHeight);
                        doorAmbushPullOrigin = Vector3.Lerp(floorPull, DoorRoofCentre, hoist);
                        doorAmbushPullOrigin.y = doorHoistAnchorHeight;
                    }
                    bool top = releasedGuides && accepted &&
                        Mathf.Abs(transform.position.y - doorHoistRootCap) < .035f &&
                        doorHoistAnchorHeight >= DoorAmbushHoistTargetHeight - .015f &&
                        victim.RestraintAnchor.y >= DoorAmbushHoistTargetHeight - .60f && DoorAmbushVictimInverted;
                    if (top) DoorAmbushHoistComplete = true;
                    if (DoorAmbushHoistComplete) doorTopHoldElapsed += seconds;
                    else if (releasedGuides && (!accepted || doorHoistAnchorHeight <= previousHeight + .00001f))
                        doorHoistStallSeconds += seconds;
                    else doorHoistStallSeconds = 0f;
                    if (!DoorAmbushHoistComplete && (doorHoistStallSeconds >= 8f || doorCaptureSeconds >= 45f))
                        throw new System.InvalidOperationException("The crane's ceiling hoist could not reach its physical endpoint.");
                }
            }
            if (DoorAmbushReleased)
            {
                doorReleaseSeconds += seconds;
                Vector3 endpoint = new Vector3(doorReleasePose.position.x,
                    Mathf.Min(doorReleasePose.position.y + 1.4f, doorHoistRootCap), 0f);
                Vector3 retreat = transform.position;
                retreat.z = Mathf.MoveTowards(retreat.z, endpoint.z, seconds * 1.8f * DoorAmbushMotionRate);
                // An early escape leaves the source beneath the gallery too.
                // Reach the open well before spending any vertical retreat.
                if (transform.position.z <= 4.75f)
                    retreat.y = Mathf.MoveTowards(retreat.y, endpoint.y, seconds * .65f * DoorAmbushMotionRate);
                float before = Vector3.Distance(transform.position, endpoint);
                bool accepted = SampleDoorAmbushPose(new Pose(retreat, doorReleasePose.rotation), doorAmbushRetreatClip,
                    Mathf.Min(doorReleaseSeconds * DoorAmbushMotionRate, doorAmbushRetreatClip.length), sweep: true);
                GetCraneStowedHookPose(out Vector3 stowedEye, out Quaternion stowedRotation);
                float hookReturn = Mathf.SmoothStep(0f, 1f, doorReleaseSeconds * DoorAmbushMotionRate / .65f);
                craneHookEye = Vector3.Lerp(doorReleasedHookEye, stowedEye, hookReturn);
                craneHookRotation = Quaternion.Slerp(doorReleasedHookRotation, stowedRotation, hookReturn);
                if (hookReturn >= 1f) CraneHookPhase = CombatCraneHookPhase.Stowed;
                PresentCraneHook();
                float after = Vector3.Distance(transform.position, endpoint);
                bool arrived = accepted && after <= .02f;
                if (arrived && doorReleaseSeconds * DoorAmbushMotionRate + .000001f >= doorAmbushRetreatClip.length)
                {
                    if (doorRetreatTerminalFrame < 0) doorRetreatTerminalFrame = Time.frameCount;
                }
                else if (!accepted || before - after <= .00005f) doorRetreatStallSeconds += seconds;
                else doorRetreatStallSeconds = 0f;
                if (doorRetreatTerminalFrame < 0 && (doorRetreatStallSeconds >= 2.5f || doorReleaseSeconds >= 20f))
                    throw new System.InvalidOperationException("The crane's collision-aware retreat could not reach its endpoint.");
            }
            if (DoorAmbushCaptured || DoorAmbushReleased) doorAmbushVictim?.AdvanceSimulation(seconds);
        }

        private void ReleaseDoorAmbushCapture()
        {
            if (!DoorAmbushPrepared || craneVictim == null) return;
            CombatActor victim = craneVictim;
            craneVictim = null;
            DoorAmbushReleased = true;
            doorReleaseSeconds = 0f;
            doorRetreatStallSeconds = 0f;
            doorRetreatTerminalFrame = -1;
            doorReleasePose = new Pose(transform.position, transform.rotation);
            doorReleasedHookEye = craneHookEye;
            doorReleasedHookRotation = craneHookRotation;
            CraneHookPhase = CombatCraneHookPhase.Holding;
            CranePhase = CombatCranePhase.ReturningHome;
            craneClock = 0f;
            victim.EndCraneRestraint(this);
            StopCraneMotors();
        }

        private bool IsDoorAmbushClip(AnimationClip clip) => DoorAmbushPrepared &&
            (clip == doorAmbushClip || clip == doorAmbushHoldClip || clip == doorAmbushRetreatClip);

        private bool PresentDoorAmbush()
        {
            if (!DoorAmbushPrepared) return false;
            if (!doorPresentationVisible) return true;
            if (presentationFrozen || PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused) return true;
            PresentCraneHook();
            craneAttackCable.gameObject.SetActive(false);
            PresentDoorAmbushCables();
            return true;
        }

        private bool SampleDoorAmbushPose(Pose rootPose, AnimationClip clip, float time, bool sweep)
        {
            Pose previousRoot = new Pose(transform.position, transform.rotation);
            SaveDoorBones(doorPreviousPositions, doorPreviousRotations, doorPreviousScales);
            for (int i = 0; i < craneClearanceBones.Length; i++) craneCandidateBones[i] = craneClearanceBones[i].position;
            transform.SetPositionAndRotation(rootPose.position, rootPose.rotation);
            SampleNpcClip(clip, time);
            RefreshCraneAttachmentSockets();
            PresentCraneHook();
            DoorAmbushBlocked = !DoorAmbushPoseClear(sweep);
            if (DoorAmbushBlocked)
            {
                transform.SetPositionAndRotation(previousRoot.position, previousRoot.rotation);
                RestoreDoorBones(doorPreviousPositions, doorPreviousRotations, doorPreviousScales);
                RefreshCraneAttachmentSockets();
                PresentCraneHook();
                return false;
            }
            visibleClip = clip.name;
            handPose?.SetGrip(false, CombatCraneAssetProvider.RightHookGrip);
            handPose?.SetGrip(true, CombatCraneAssetProvider.LeftHookGrip);
            // Manual sampling also runs while the cutscene owns the world's
            // pause. Its rigid attachments must use these same fresh bones.
            return true;
        }

        private bool DoorAmbushPoseClear(bool sweep)
        {
            // Real imported eyelets turn the six feeds at the gallery edge.
            // The ordinary suspension resolver assumes a single straight run
            // and a carriage inside the well, so this local route checks each
            // of its two actual spans without moving the original hardware.
            for (int i = 0; i < doorCableGuides.Length; i++)
            {
                for (int segment = 0; segment < CraneCableSegments; segment++)
                    if (CraneWorldOverlap(DoorSupportPoint(i, segment / (float)CraneCableSegments),
                        DoorSupportPoint(i, (segment + 1f) / CraneCableSegments), .014f))
                    { CraneBlockedAnatomy = "support cable"; return false; }
            }
            if (doorAmbushLeg != null)
                for (int segment = 0; segment < 4; segment++)
                    if (CraneWorldOverlap(DoorLegCablePoint(segment), DoorLegCablePoint(segment + 1), .014f))
                    { CraneBlockedAnatomy = "leg hook cable"; return false; }
            if (!DoorCraneMachineClear()) return false;
            Bounds broad = new Bounds(transform.position + Vector3.up, Vector3.one * .1f);
            for (int i = 0; i < craneClearanceBones.Length; i++)
            {
                broad.Encapsulate(new Bounds(craneClearanceBones[i].position,
                    Vector3.one * (CraneLimbRadius(i) * 2f)));
                if (sweep) broad.Encapsulate(craneCandidateBones[i]);
            }
            broad.Expand(.05f);
            if (!CraneBoxOverlaps(broad)) return true;
            for (int i = 0; i < craneClearanceBones.Length; i++)
            {
                Vector3 point = craneClearanceBones[i].position;
                int parent = CraneLimbParents[i];
                Vector3 a = parent < 0 ? point : craneClearanceBones[parent].position;
                if (CraneWorldOverlap(a, point, CraneLimbRadius(i)) ||
                    sweep && CraneWorldSweep(craneCandidateBones[i], point, CraneLimbRadius(i)))
                { CraneBlockedAnatomy = CraneAnatomyBoneNames[i]; return false; }
            }
            CraneBlockedAnatomy = null;
            return true;
        }

        private Vector3 DoorSupportPoint(int index, float fraction)
        {
            Vector3 feed = doorSuspensionSource.craneFeeds[index].position, end = craneAttachments[index].position;
            Vector3 guide = Vector3.Lerp(doorCableGuides[index].position, (feed + end) * .5f, doorGuideRelease);
            return fraction <= .5f
                ? Vector3.Lerp(feed, guide, fraction * 2f)
                : Vector3.Lerp(guide, end, fraction * 2f - 1f);
        }

        private Vector3 DoorRoofCentre => doorRoofGuides[0] != null && doorRoofGuides[1] != null
            ? (doorRoofGuides[0].position + doorRoofGuides[1].position) * .5f : Vector3.zero;

        private Vector3 DoorLegCablePoint(int point)
        {
            Vector3 hand = CraneHookCableExit;
            Vector3 roof = doorRoofGuides[1].position;
            Vector3 eye = CraneHookEyePosition;
            if (point == 0) return hand;
            if (point == 2) return roof;
            if (point == 4) return eye;
            int lowIndex = point == 1 ? 5 : 4;
            Vector3 low = doorCableGuides[lowIndex].position + Vector3.forward * .03f;
            Vector3 straight = point == 1 ? (hand + roof) * .5f : (roof + eye) * .5f;
            return Vector3.Lerp(low, straight, doorGuideRelease);
        }

        private bool DoorSourceInsideWell()
        {
            for (int i = 0; i < craneClearanceBones.Length; i++)
            {
                Vector3 point = craneClearanceBones[i].position;
                float radius = CraneLimbRadius(i);
                if (Mathf.Abs(point.x) + radius >= 5.6f || Mathf.Abs(point.z) + radius >= 5.6f) return false;
            }
            return true;
        }

        private float DoorSuspendedRootCap()
        {
            float cap = WarehouseArenaPlan.CeilingHeight - .12f;
            for (int i = 0; i < craneClearanceBones.Length; i++)
                cap = Mathf.Min(cap, WarehouseArenaPlan.CeilingHeight - .12f -
                    (craneClearanceBones[i].position.y - transform.position.y + CraneLimbRadius(i)));
            for (int i = 0; i < craneAttachments.Length; i++)
                cap = Mathf.Min(cap, doorSuspensionSource.craneFeeds[i].position.y - .08f -
                    (craneAttachments[i].position.y - transform.position.y));
            return Mathf.Max(doorCapturePose.position.y, cap);
        }

        private bool DoorCraneMachineClear()
        {
            CombatActor machine = doorSuspensionSource;
            if (!machine.craneMachineBoundsReady) machine.MeasureCraneMachineBounds();
            Bounds head = machine.craneHeadBounds;
            head.center += machine.CraneAnchorPosition;
            Bounds bridge = machine.craneBridgeBounds;
            bridge.center += new Vector3(0f, machine.CraneAnchorPosition.y, machine.craneCommand.z);
            for (int i = 0; i < craneClearanceBones.Length; i++)
            {
                int parent = CraneLimbParents[i];
                Vector3 from = parent < 0 ? craneClearanceBones[i].position : craneClearanceBones[parent].position;
                Vector3 delta = craneClearanceBones[i].position - from;
                if (CombatHurtboxes.IntersectsSegment(head, from, delta, CraneLimbRadius(i)) ||
                    CombatHurtboxes.IntersectsSegment(bridge, from, delta, CraneLimbRadius(i)))
                { CraneBlockedAnatomy = "fixed gantry"; return false; }
            }
            return true;
        }

        private void PresentDoorAmbushCables()
        {
            if (!doorPresentationVisible)
            { SetDoorLegCableVisible(false); return; }
            for (int i = 0; i < 6; i++)
                for (int segment = 0; segment < CraneCableSegments; segment++)
                    PositionDoorCable(craneCablePieces[i, segment], craneCableScales[i, segment],
                        DoorSupportPoint(i, segment / (float)CraneCableSegments),
                        DoorSupportPoint(i, (segment + 1f) / CraneCableSegments));
            bool visible = doorAmbushLeg != null;
            SetDoorLegCableVisible(visible);
            if (!visible) return;
            PresentCraneCuff();
            for (int segment = 0; segment < 4; segment++)
                PositionDoorCable(doorLegCable[segment], doorLegCableScales[segment],
                    DoorLegCablePoint(segment), DoorLegCablePoint(segment + 1));
        }

        private static void PositionDoorCable(Transform piece, Vector3 scale, Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            piece.SetPositionAndRotation(from, delta.sqrMagnitude > .00000001f
                ? Quaternion.FromToRotation(Vector3.up, delta) : Quaternion.identity);
            scale.y *= delta.magnitude;
            piece.localScale = scale;
        }

        private void SetDoorLegCableVisible(bool visible)
        {
            for (int i = 0; i < doorLegCable.Length; i++)
                if (doorLegCable[i] != null) doorLegCable[i].gameObject.SetActive(visible);
        }

        private void EnsureDoorAmbushBuffers()
        {
            if (doorBodyRenderers == null)
            {
                doorBodyRenderers = DamageRigRoot.GetComponentsInChildren<Renderer>(true);
                doorOriginalBodyVisibility = new bool[doorBodyRenderers.Length];
            }
            if (doorSavedPositions == null)
            {
                int count = npcAnimationBaseBones.Length;
                doorSavedPositions = new Vector3[count]; doorSavedScales = new Vector3[count];
                doorSavedRotations = new Quaternion[count];
                doorPreviousPositions = new Vector3[count]; doorPreviousScales = new Vector3[count];
                doorPreviousRotations = new Quaternion[count];
            }
            for (int i = 0; i < doorLegCable.Length; i++)
                if (doorLegCable[i] == null)
                {
                    Transform cable = CombatCraneAssetProvider.CreateCableSegment(craneSuspensionHost.transform).transform;
                    cable.name = "Crane leg hook cable";
                    doorLegCable[i] = cable; doorLegCableScales[i] = cable.localScale;
                    cable.gameObject.SetActive(false);
                }
        }

        private void SaveDoorBones(Vector3[] positions, Quaternion[] rotations, Vector3[] scales)
        {
            for (int i = 0; i < npcAnimationBaseBones.Length; i++)
            {
                npcAnimationBaseBones[i].GetLocalPositionAndRotation(out positions[i], out rotations[i]);
                scales[i] = npcAnimationBaseBones[i].localScale;
            }
        }

        private void RestoreDoorBones(Vector3[] positions, Quaternion[] rotations, Vector3[] scales)
        {
            for (int i = 0; i < npcAnimationBaseBones.Length; i++)
            {
                npcAnimationBaseBones[i].SetLocalPositionAndRotation(positions[i], rotations[i]);
                npcAnimationBaseBones[i].localScale = scales[i];
            }
        }

        internal void EndDoorAmbush()
        {
            if (!DoorAmbushPrepared) return;
            ReleaseDoorAmbushCapture();
            DoorAmbushPrepared = false;
            DoorAmbushHangingGameplay = false;
            SetDoorLegCableVisible(false);
            for (int i = 0; i < doorBodyRenderers.Length; i++)
                if (doorBodyRenderers[i] != null) doorBodyRenderers[i].enabled = doorOriginalBodyVisibility[i];
            StopCraneVoices();
            transform.SetPositionAndRotation(doorOriginalPose.position, doorOriginalPose.rotation);
            RestoreDoorBones(doorSavedPositions, doorSavedRotations, doorSavedScales);
            CranePhase = doorOriginalPhase; CraneHookPhase = doorOriginalHookPhase;
            craneClock = doorOriginalCraneClock;
            CranePatrolEnabled = doorOriginalPatrol;
            Body.enabled = doorOriginalBody;
            craneRails.SetActive(doorOriginalRails); craneBridge.SetActive(doorOriginalBridge);
            craneSuspensionHead.SetActive(doorOriginalHead); craneHookFrame.SetActive(doorOriginalHook);
            craneCuffFrame.SetActive(doorOriginalCuff); craneAttackCable.gameObject.SetActive(doorOriginalAttackCable);
            for (int i = 0; i < 6; i++)
                for (int segment = 0; segment < CraneCableSegments; segment++)
                    craneCablePieces[i, segment].gameObject.SetActive(
                        doorOriginalOwnSupportVisibility[i * CraneCableSegments + segment]);
            if (doorSuspensionSource != null)
            {
                for (int i = 0; i < 6; i++)
                    for (int segment = 0; segment < CraneCableSegments; segment++)
                        doorSuspensionSource.craneCablePieces[i, segment].gameObject.SetActive(
                            doorOriginalSupportVisibility[i * CraneCableSegments + segment]);
                doorSuspensionSource.craneRemnant.enabled = doorOriginalRemnantEnabled;
            }
            craneSuspensionHost.SetActive(doorOriginalHost);
            doorAmbushVictim = null; doorAmbushLeg = null; doorSuspensionSource = null;
            doorAmbushClip = doorAmbushHoldClip = doorAmbushRetreatClip = null;
            visibleClip = null;
            if (gameObject.activeSelf != doorOriginalActive) gameObject.SetActive(doorOriginalActive);
            enabled = doorOriginalEnabled;
        }

        /// <summary>Only the debug preview boundary restores its captured ready-state hero.</summary>
        internal void RestoreDoorAmbushPreview(Vector3 position, Quaternion rotation, float health,
            float stamina, int fallCount, float fallDamage)
        {
            if (!IsHero || !FiniteRestraintVector(position) || !FiniteDoorRotation(rotation)) return;
            CancelRestraint();
            ResetKnockdown();
            Ragdoll?.Cancel();
            ReleasePresentation();
            ResetDefeat();
            State.RestoreVitals(health, stamina);
            guardHeld = false;
            reaction = null;
            reactionClock = 0f;
            collectSweep = sweepValid = collectShove = collectKick = false;
            ImpactMotion?.Forget();
            hero.ResetClipSpatialOffset();
            motor?.ReleaseMovementConstraint(this);
            motor?.SetOwnedMovementFrozen(this, false);
            motor?.Teleport(position);
            transform.rotation = rotation;
            RestraintFallDamageCount = fallCount;
            LastRestraintFallDamage = fallDamage;
            restraintSeconds = 0f;
            restraintPresses = 0;
            restraintDragVelocity = Vector3.zero;
        }

        private static bool FiniteDoorRotation(Quaternion value) => float.IsFinite(value.x) &&
            float.IsFinite(value.y) && float.IsFinite(value.z) && float.IsFinite(value.w) &&
            value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w > .0001f;
    }
}
