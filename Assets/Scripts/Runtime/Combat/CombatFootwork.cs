using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Authored shuffles, measured travel and world-space sole contacts share the duel clock.
    /// Sampling a weapon or presenting a frame never advances a step.</summary>
    internal sealed class CombatFootwork
    {
        private const int Samples = 80;
        private readonly Transform frame, pelvis;
        private readonly Transform[] bones = new Transform[7];
        private readonly Vector3[] basePositions = new Vector3[7];
        private readonly Quaternion[] baseRotations = new Quaternion[7];
        private readonly PoseFrame[][] curves = new PoseFrame[4][];
        private readonly PoseFrame[,] stepLandingPoses = new PoseFrame[4, 2];
        private readonly Vector3[] restFeet = new Vector3[2], feet = new Vector3[2], correction = new Vector3[2];
        private readonly Quaternion[] restRotations = new Quaternion[2], rotations = new Quaternion[2], swingRotations = new Quaternion[2];
        private readonly float[] legLengths = new float[2];
        private readonly RaycastHit[] groundHits = new RaycastHit[16], sweepHits = new RaycastHit[16];
        private readonly Collider[] landingOverlaps = new Collider[16];
        private readonly bool[] supportConfirmed = { true, true };
        private readonly Vector3[] presentedFeet = new Vector3[2];
        private readonly Vector3[] authoredStepFeet = new Vector3[2];
        private readonly Quaternion[] authoredStepRotations = new Quaternion[2];
        private readonly bool[] authoredStepContacts = new bool[2];
        private readonly float[] stepGroundGaps = { -1f, -1f };
        private int presentedStepLeading = -1;
        private float presentedStepTravel;
        private readonly Vector3[] recoveryStepStarts = new Vector3[2], recoveryStepLandings = new Vector3[2];
        private readonly Quaternion[] recoveryStepRotations = new Quaternion[2];
        private Vector3 recoveryStepDirection, recoveryStepStartRoot;
        private float recoveryStepDistance, recoveryStepFirstLandingSeconds;
        private int recoveryStepSupport = -1;
        private bool recoveryStepOwned, recoveryStepFirstLanded, recoveryStepSecondLanded, recoveryStepObstructed;
        private readonly Vector3 readyPelvis;
        private Vector3 previousPosition, previousForward, gaitOffset, settleOffset, settleStart;
        private Quaternion settleRotation, catchRotation;
        private Vector3 travelDirection;
        private float cycle, settling, settleDuration, idleSeconds;
        private int direction, swing, attackSequence = -1;
        private bool initialized, moving, applied, yielded, settlingFoot, settlingAttack;
        private bool catching, catchAwaitingContact, catchDecisionPending, catchStabilityPending, hasPresentedContacts;
        private bool recoveryGaitReleased;
        private bool ordinaryStepTracked;
        private int movementLandingId, ordinaryStepSequence = -1, ordinaryStepHalf = -1;
        private Vector3 achievedGaitVelocity;
        private bool kickOwnsFoot, kickLandingPending;
        internal int SelectedSupportSide { get; private set; }
        private int KickFootSide => 1 - SelectedSupportSide;
        internal bool KickWorldBlocked { get; private set; }
        internal enum KickSupportFailure { None, Uninitialized, Yielded, Recovery, GroundMissing, FootGap, LandingObstructed }
        internal KickSupportFailure LastKickSupportFailure { get; private set; }
        internal float LastKickSupportGap { get; private set; } = -1f;
        internal Vector3 LastKickSupportAnkle { get; private set; }
        internal Vector3 LastKickSupportGround { get; private set; }
        internal bool CanWaitForKickSupport { get; private set; }
        private Vector3 catchTarget;
        private float impactPelvisFloor, catchLift, catchRetry;
        private int recoverySequence = -1, sequenceSteps;
        public CombatImpactMotion ImpactMotion { get; set; }
        internal CombatActor JournalActor { get; set; }
        public bool TransferringFoot => moving || settlingFoot || catching;
        internal int CatchStepCount { get; private set; }
        internal int CatchLandingCount { get; private set; }
        internal int BlockedCatchCount { get; private set; }
        internal bool CatchStepActive => catching;
        // Landing a boot does not yet decide whether another step or continued
        // balance recovery is needed. The final support decision owns the hand.
        internal bool RecoveryEpisodeActive => recoveryStepOwned || catching || catchDecisionPending || catchStabilityPending || kickLandingPending;
        internal bool RecoveryStepOwnsFeet => recoveryStepOwned;
        internal float CatchStepProgress => catching ? Mathf.Clamp01(settling / Mathf.Max(.001f, settleDuration)) : 0f;
        internal Vector3 LastCatchTarget { get; private set; }
        internal int LastCatchSide { get; private set; } = -1;
        internal bool JournalLeftSupport => supportConfirmed[0];
        internal bool JournalRightSupport => supportConfirmed[1];
        internal string SupportDiagnostics => $"confirmed={supportConfirmed[0]}/{supportConfirmed[1]}, " +
            $"presented={hasPresentedContacts}, gaps={Vector3.Distance(presentedFeet[0], feet[0]):F3}/" +
            $"{Vector3.Distance(presentedFeet[1], feet[1]):F3}, swing={swing}, awaiting={catchAwaitingContact}, " +
            $"feet={feet[0]:F3}/{feet[1]:F3}, step={presentedStepTravel:F3}/lead{presentedStepLeading}, " +
            $"groundGaps={stepGroundGaps[0]:F3}/{stepGroundGaps[1]:F3}, stepTargets={authoredStepContacts[0]}/{authoredStepContacts[1]}, " +
            $"recoveryStep={recoveryStepOwned}/support{recoveryStepSupport}/landed{recoveryStepFirstLanded}/{recoveryStepSecondLanded}/blocked{recoveryStepObstructed}";

        private struct PoseFrame
        {
            public Vector3 Pelvis, Left, Right;
            public Quaternion LeftRotation, RightRotation;
            public Vector3 Foot(int side) => side == 0 ? Left : Right;
            public Quaternion Rotation(int side) => side == 0 ? LeftRotation : RightRotation;
        }

        public CombatFootwork(Transform rig, GameObject animationRoot, Transform actorFrame, AnimationClip ready, bool npc)
        {
            frame = actorFrame;
            string[] names = { "pelvis", "thigh.L", "shin.L", "foot.L", "thigh.R", "shin.R", "foot.R" };
            Transform[] all = rig.GetComponentsInChildren<Transform>(true);
            foreach (Transform bone in all)
                for (int i = 0; i < names.Length; i++) if (bone.name == names[i]) bones[i] = bone;
            foreach (Transform bone in bones)
                if (bone == null) throw new InvalidOperationException("Combat footwork requires the original pelvis and both leg chains.");
            pelvis = bones[0];
            var positions = new Vector3[all.Length];
            var rotationsBefore = new Quaternion[all.Length];
            for (int i = 0; i < all.Length; i++) { positions[i] = all[i].localPosition; rotationsBefore[i] = all[i].localRotation; }
            try
            {
                ready.SampleAnimation(animationRoot, 0f);
                PoseFrame neutral = ReadPose();
                readyPelvis = neutral.Pelvis;
                for (int side = 0; side < 2; side++)
                {
                    restFeet[side] = neutral.Foot(side); restRotations[side] = neutral.Rotation(side);
                    int i = 1 + side * 3;
                    legLengths[side] = Vector3.Distance(bones[i].position, bones[i + 1].position) +
                        Vector3.Distance(bones[i + 1].position, bones[i + 2].position);
                }
                for (int d = 0; d < curves.Length; d++)
                {
                    AnimationClip clip = CombatAssetProvider.LoadClip(CombatAssetProvider.LocomotionClipNames[d], npc);
                    curves[d] = new PoseFrame[Samples + 1];
                    for (int sample = 0; sample <= Samples; sample++)
                    { clip.SampleAnimation(animationRoot, clip.length * sample / Samples); curves[d][sample] = ReadPose(); }
                    AnimationClip step = CombatAssetProvider.LoadClip(CombatAssetProvider.StepClipNames[d], npc);
                    for (int half = 0; half < 2; half++)
                    {
                        step.SampleAnimation(animationRoot, MeleeCombatSettings.Crowbar.StepTravelSeconds * (half + 1) * .5f);
                        stepLandingPoses[d, half] = ReadPose();
                    }
                }
            }
            finally
            {
                for (int i = 0; i < all.Length; i++) { all[i].localPosition = positions[i]; all[i].localRotation = rotationsBefore[i]; }
            }
            Reset();
        }

        private PoseFrame ReadPose() => new PoseFrame
        {
            Pelvis = frame.InverseTransformPoint(pelvis.position),
            Left = frame.InverseTransformPoint(bones[3].position), Right = frame.InverseTransformPoint(bones[6].position),
            LeftRotation = Quaternion.Inverse(frame.rotation) * bones[3].rotation,
            RightRotation = Quaternion.Inverse(frame.rotation) * bones[6].rotation
        };

        public void Reset()
        {
            if (catching) JournalCatch("catch_cancelled", "reset");
            ImpactMotion?.CancelRecoveryStep();
            Restore(); initialized = false; moving = settlingFoot = yielded = settlingAttack = catching = false;
            cycle = settling = 0f; gaitOffset = Vector3.zero; attackSequence = -1;
            idleSeconds = catchRetry = 0f; catchAwaitingContact = catchDecisionPending = catchStabilityPending = hasPresentedContacts = false;
            presentedStepLeading = -1; presentedStepTravel = 0f;
            authoredStepContacts[0] = authoredStepContacts[1] = false;
            stepGroundGaps[0] = stepGroundGaps[1] = -1f;
            recoveryStepOwned = recoveryStepFirstLanded = recoveryStepSecondLanded = recoveryStepObstructed = false;
            recoveryStepSupport = -1;
            recoveryGaitReleased = false;
            ordinaryStepTracked = false; ordinaryStepSequence = ordinaryStepHalf = -1;
            movementLandingId = 0; achievedGaitVelocity = Vector3.zero;
            ImpactMotion?.CancelMovementLanding();
            kickOwnsFoot = kickLandingPending = false; SelectedSupportSide = 0;
            KickWorldBlocked = false;
            LastKickSupportFailure = KickSupportFailure.None;
            LastKickSupportGap = -1f; CanWaitForKickSupport = false;
            LastKickSupportAnkle = LastKickSupportGround = Vector3.zero;
            recoverySequence = -1; sequenceSteps = CatchStepCount = CatchLandingCount = BlockedCatchCount = 0;
            LastCatchTarget = Vector3.zero; LastCatchSide = -1;
            previousPosition = frame.position; previousForward = frame.forward;
            PlantReady();
        }

        private void PlantReady()
        {
            hasPresentedContacts = false;
            for (int side = 0; side < 2; side++)
            { feet[side] = frame.TransformPoint(restFeet[side]); rotations[side] = frame.rotation * restRotations[side]; supportConfirmed[side] = true; }
            initialized = true;
        }

        internal bool TryBeginKickSupport()
        {
            LastKickSupportFailure = KickSupportFailure.None;
            LastKickSupportGap = -1f; CanWaitForKickSupport = false;
            LastKickSupportGround = Vector3.zero;
            if (!initialized) return RejectKickSupport(KickSupportFailure.Uninitialized);
            if (yielded) return RejectKickSupport(KickSupportFailure.Yielded);
            if (RecoveryEpisodeActive) return RejectKickSupport(KickSupportFailure.Recovery);
            int preferred = PreferredKickSupport();
            if (!MeasureKickSupport(preferred))
            {
                KickSupportFailure failure = LastKickSupportFailure;
                float gap = LastKickSupportGap;
                Vector3 ankle = LastKickSupportAnkle, ground = LastKickSupportGround;
                bool canWait = CanWaitForKickSupport;
                if (!MeasureKickSupport(1 - preferred))
                {
                    if (!CanWaitForKickSupport)
                    {
                        SelectedSupportSide = preferred; LastKickSupportFailure = failure;
                        LastKickSupportGap = gap; LastKickSupportAnkle = ankle; LastKickSupportGround = ground;
                        CanWaitForKickSupport = canWait;
                    }
                    return false;
                }
            }
            int support = SelectedSupportSide;
            Transform sole = bones[3 + support * 3];
            feet[support] = sole.position; rotations[support] = sole.rotation;
            supportConfirmed[support] = true; supportConfirmed[1 - support] = false;
            moving = settlingFoot = false; gaitOffset = Vector3.zero;
            kickOwnsFoot = true; kickLandingPending = false;
            KickWorldBlocked = false;
            return true;
        }

        private int PreferredKickSupport()
        {
            if (moving || settlingFoot) return 1 - swing;
            if (supportConfirmed[0] != supportConfirmed[1]) return supportConfirmed[0] ? 0 : 1;
            Vector3 across = Vector3.ProjectOnPlane(feet[1] - feet[0], Vector3.up);
            float rightLoad = ImpactMotion != null && across.sqrMagnitude > .001f ? Mathf.Clamp01(Vector3.Dot(
                ImpactMotion.CentreOfMass - feet[0], across) / across.sqrMagnitude) : .5f;
            // A near-even stance keeps the original right kick. Never alternate
            // arbitrarily or use the previous recovery step as current weight.
            return rightLoad > .6f ? 1 : 0;
        }

        private bool MeasureKickSupport(int side)
        {
            SelectedSupportSide = side;
            Transform ankle = bones[3 + side * 3];
            LastKickSupportAnkle = ankle.position; LastKickSupportGround = Vector3.zero;
            LastKickSupportGap = -1f; CanWaitForKickSupport = false;
            if (!TryCatchGround(ankle.position, side, out Vector3 grounded))
                return RejectKickSupport(KickSupportFailure.GroundMissing);
            LastKickSupportGround = grounded;
            LastKickSupportGap = Vector3.Distance(ankle.position, grounded);
            if (!LandingClear(grounded)) return RejectKickSupport(KickSupportFailure.LandingObstructed);
            bool transferring = moving || settlingFoot;
            if (LastKickSupportGap > .08f || transferring && swing == side)
            {
                // Only an ordinary walking transfer may settle for a
                // pending kick. Height changes and physical recovery never qualify.
                CanWaitForKickSupport = (moving || settlingFoot && !settlingAttack) && swing == side &&
                    ankle.position.y > grounded.y &&
                    Mathf.Abs(frame.TransformPoint(restFeet[side]).y - grounded.y) <= .08f;
                return RejectKickSupport(KickSupportFailure.FootGap);
            }
            LastKickSupportFailure = KickSupportFailure.None;
            return true;
        }

        private bool RejectKickSupport(KickSupportFailure failure)
        { LastKickSupportFailure = failure; return false; }

        internal void BeginKickSupportWait()
        {
            if (!CanWaitForKickSupport) return;
            // Use the ordinary constrained walking settle. The actual ankle and
            // ground gates are still rechecked before the kick can spend stamina.
            BeginSettle(SelectedSupportSide, .13f, false);
            moving = false;
        }

        internal void EndKickSupport()
        {
            if (!kickOwnsFoot) return;
            kickOwnsFoot = false;
            KickWorldBlocked = false;
            int side = KickFootSide;
            Vector3 ankle = bones[3 + side * 3].position;
            kickLandingPending = !TryCatchGround(ankle, side, out Vector3 actual) ||
                Vector3.Distance(ankle, actual) > .08f || !LandingClear(actual);
            Vector3 desired = frame.TransformPoint(restFeet[side]);
            if (TryCatchGround(desired, side, out Vector3 grounded))
                feet[side] = Vector3.Distance(desired, grounded) <= .08f ? desired : grounded;
            rotations[side] = frame.rotation * restRotations[side];
            supportConfirmed[side] = !kickLandingPending;
        }

        public void Advance(float seconds, MeleeCombatant state)
        {
            if (!float.IsFinite(seconds) || seconds <= 0f) return;
            AdvanceFootwork(seconds, state);
            // Only the duel clock reports support. Pose previews and repeated Apply
            // calls cannot land a boot or change the outcome of a recovery.
            ReportSupport();
            ReportMovementLanding(state);
        }

        internal void CompleteRecoveryDecision(Vector3 locomotionVelocity)
        {
            if (recoveryStepOwned) return;
            // A settled episode has returned its feet to ordinary locomotion.
            // Walking must not reopen that same impulse's recovery decision.
            if (recoveryGaitReleased) return;
            // The actor calls this after EvaluateSupport, including this tick's
            // actual landing. Exhausting the step budget alone is not recovery.
            // No extra clock: reset/yield or the physical response ending clears it.
            catchStabilityPending = initialized && !yielded && sequenceSteps > 0 &&
                !catching && !catchDecisionPending && ImpactMotion != null && ImpactMotion.IsActive &&
                (!hasPresentedContacts || !ImpactMotion.HasStableRecoverySupport(locomotionVelocity));
            if (sequenceSteps > 0 && !RecoveryEpisodeActive) recoveryGaitReleased = true;
        }

        // Preparation only measures the current visible soles and the requested
        // landing. It never cancels catch, grants support or spends stamina.
        internal bool PrepareRecoveryStep(Vector3 direction, float distance)
        {
            if (!hasPresentedContacts || direction.sqrMagnitude < .5f || distance <= 0f) return false;
            bool left = PresentedGrounded(0, out _), right = PresentedGrounded(1, out _);
            if (!left && !right) return false;
            int support = left && !right ? 0 : right && !left ? 1 :
                catching ? 1 - swing :
                (Vector3.ProjectOnPlane(pelvis.position - presentedFeet[0], Vector3.up).sqrMagnitude <=
                 Vector3.ProjectOnPlane(pelvis.position - presentedFeet[1], Vector3.up).sqrMagnitude ? 0 : 1);
            float travelSeconds = JournalActor.State.Settings.StepTravelSeconds;
            // A fast incoming torso needs its first new support earlier than a
            // balanced shuffle. Keep the same total travel/return clocks.
            float incomingSpeed = Mathf.Clamp01((ImpactMotion?.Velocity.magnitude ?? 0f) / 1.5f);
            recoveryStepFirstLandingSeconds = Mathf.Lerp(travelSeconds * .5f,
                Mathf.Min(.10f, travelSeconds * .5f), incomingSpeed);
            for (int side = 0; side < 2; side++)
            {
                Vector3 start = presentedFeet[side];
                Vector3 carry = ImpactMotion != null ? ImpactMotion.PredictRecoveryDisplacement(travelSeconds) : Vector3.zero;
                float footDistance = side == support ? distance : distance * Mathf.Lerp(.65f, .5f, incomingSpeed);
                if (!TryCatchGround(start + direction * footDistance + carry, side, out Vector3 target) || !LandingClear(target)) return false;
                Vector3 middle = Vector3.Lerp(start, target, .5f) + Vector3.up * .06f;
                if (!ClearFootTravel(start, middle) || !ClearFootTravel(middle, target)) return false;
                recoveryStepStarts[side] = start;
                recoveryStepLandings[side] = target;
                recoveryStepRotations[side] = bones[3 + side * 3].rotation;
            }
            recoveryStepSupport = support;
            recoveryStepDirection = direction.normalized;
            recoveryStepDistance = distance;
            recoveryStepStartRoot = frame.position;
            return true;
        }

        internal void BeginPreparedRecoveryStep(MeleeCombatSettings settings)
        {
            if (recoveryStepSupport < 0) return;
            if (catching) JournalCatch("catch_cancelled", "recovery_step_owns_feet");
            // The moving recovery foot is replaced by a directed opening shuffle,
            // while its actually grounded partner remains at the same world contact.
            catching = catchAwaitingContact = catchDecisionPending = catchStabilityPending = false;
            moving = settlingFoot = false; initialized = true; yielded = false;
            recoveryStepOwned = true;
            recoveryStepFirstLanded = recoveryStepSecondLanded = recoveryStepObstructed = false;
            recoveryGaitReleased = false; gaitOffset = Vector3.zero;
            for (int side = 0; side < 2; side++)
            { feet[side] = recoveryStepStarts[side]; rotations[side] = recoveryStepRotations[side]; }
            ImpactMotion?.BeginAuthoredRecoveryStep(1 - recoveryStepSupport,
                recoveryStepLandings[1 - recoveryStepSupport], recoveryStepFirstLandingSeconds,
                recoveryStepDirection * (recoveryStepDistance * Smooth(recoveryStepFirstLandingSeconds / settings.StepTravelSeconds)) +
                ImpactMotion.PredictRecoveryDisplacement(recoveryStepFirstLandingSeconds));
        }

        private bool PresentedGrounded(int side, out Vector3 ground)
        {
            ground = default;
            return hasPresentedContacts && TryCatchGround(presentedFeet[side], side, out ground) &&
                Vector3.Distance(presentedFeet[side], ground) <= .055f && LandingClear(ground);
        }

        // A step's travel ends before its authored visual return. Adopt only
        // the soles actually presented at that boundary, never a fresh stance.
        internal bool PrepareStepAttackHandoff()
        {
            if (JournalActor == null || JournalActor.State.Phase != MeleePhase.Step) return true;
            if (JournalActor.State.StepTravelProgress < 1f) return false;
            bool left = PresentedGrounded(0, out _), right = PresentedGrounded(1, out _);
            if (!left && !right) return false;
            if (recoveryStepOwned)
            {
                AdvanceAuthoredRecoveryStep(JournalActor.State);
                if (!recoveryStepSecondLanded) return true;
                ReleaseAuthoredRecoveryStep();
            }
            ordinaryStepTracked = false;
            initialized = true; yielded = false; moving = settlingFoot = false;
            gaitOffset = Vector3.zero;
            for (int side = 0; side < 2; side++)
            {
                feet[side] = presentedFeet[side];
                rotations[side] = bones[3 + side * 3].rotation;
                supportConfirmed[side] = side == 0 ? left : right;
            }
            return true;
        }

        private void AdvanceAuthoredRecoveryStep(MeleeCombatant state)
        {
            float travel = state.StepTravelProgress;
            float firstTravel = recoveryStepFirstLandingSeconds / state.Settings.StepTravelSeconds;
            int first = 1 - recoveryStepSupport;
            if (!recoveryStepObstructed && !recoveryStepFirstLanded && travel >= firstTravel &&
                TryLandAuthoredRecoveryFoot(first))
            {
                recoveryStepFirstLanded = true;
                float remaining = Mathf.Max(.001f, state.Settings.StepTravelSeconds - state.StepElapsed);
                Vector3 remainingCarry = ImpactMotion != null ? ImpactMotion.PredictRecoveryDisplacement(remaining) : Vector3.zero;
                Vector3 achievedCarry = frame.position - recoveryStepStartRoot -
                    recoveryStepDirection * (recoveryStepDistance * Smooth(travel));
                Vector3 target = recoveryStepStarts[recoveryStepSupport] +
                    recoveryStepDirection * recoveryStepDistance + achievedCarry + remainingCarry;
                if (TryCatchGround(target, recoveryStepSupport, out Vector3 ground) && LandingClear(ground) &&
                    ClearFootTravel(feet[recoveryStepSupport], Vector3.Lerp(feet[recoveryStepSupport], ground, .5f) + Vector3.up * .06f))
                {
                    recoveryStepLandings[recoveryStepSupport] = ground;
                    ImpactMotion?.BeginAuthoredRecoveryStep(recoveryStepSupport, ground, remaining,
                        recoveryStepDirection * (recoveryStepDistance * (1f - Smooth(travel))) + remainingCarry);
                }
                else { recoveryStepObstructed = true; ImpactMotion?.CancelRecoveryStep(); }
            }
            if (!recoveryStepObstructed && recoveryStepFirstLanded && !recoveryStepSecondLanded && travel >= 1f &&
                TryLandAuthoredRecoveryFoot(recoveryStepSupport)) recoveryStepSecondLanded = true;
            for (int side = 0; side < 2; side++)
            {
                float progress = side == first ? Mathf.Clamp01(travel / firstTravel) :
                    recoveryStepFirstLanded ? Mathf.Clamp01((travel - firstTravel) / (1f - firstTravel)) : 0f;
                Vector3 target = Vector3.Lerp(recoveryStepStarts[side], recoveryStepLandings[side], Smooth(progress));
                float lift = Mathf.Sin(progress * Mathf.PI);
                target += Vector3.up * (.06f * lift * lift);
                if (!ClearFootTravel(feet[side], target))
                {
                    recoveryStepObstructed = true;
                    ImpactMotion?.CancelRecoveryStep();
                    continue;
                }
                feet[side] = target;
            }
            if (!recoveryStepObstructed && !recoveryStepSecondLanded)
            {
                float landingTravel = recoveryStepFirstLanded ? 1f : firstTravel;
                float remaining = Mathf.Max(0f, state.Settings.StepTravelSeconds * landingTravel - state.StepElapsed);
                ImpactMotion?.UpdateAuthoredRecoveryProjection(recoveryStepDirection *
                    (recoveryStepDistance * Mathf.Max(0f, Smooth(landingTravel) - Smooth(travel))) +
                    ImpactMotion.PredictRecoveryDisplacement(remaining));
            }
        }

        private bool TryLandAuthoredRecoveryFoot(int side)
        {
            if (!PresentedGrounded(side, out Vector3 ground) ||
                Vector3.Distance(presentedFeet[side], recoveryStepLandings[side]) > .055f ||
                !ClearFootTravel(feet[side], ground)) return false;
            recoveryStepLandings[side] = ground;
            ImpactMotion?.LandRecoveryStep(side, ground);
            return true;
        }

        private void ReleaseAuthoredRecoveryStep()
        {
            recoveryStepOwned = false;
            ImpactMotion?.CancelRecoveryStep();
            // Return from the soles that were really drawn. PlantReady would
            // manufacture a new stance and erase the unfinished physical save.
            initialized = true; yielded = false;
            for (int side = 0; side < 2; side++)
            {
                if (hasPresentedContacts) feet[side] = presentedFeet[side];
                supportConfirmed[side] = PresentedGrounded(side, out _);
            }
        }

        private void ReportSupport()
        {
            if (JournalActor != null && JournalActor.State.Phase == MeleePhase.Step &&
                (recoveryStepOwned || ImpactMotion != null && ImpactMotion.IsActive))
            {
                ReportAuthoredStepSupport();
                return;
            }
            bool active = initialized && !yielded;
            if (active && hasPresentedContacts && ImpactMotion != null && ImpactMotion.IsActive &&
                !catching && !kickOwnsFoot && !kickLandingPending)
            {
                for (int side = 0; side < 2; side++)
                    supportConfirmed[side] = PresentedGrounded(side, out _) &&
                        Vector3.Distance(presentedFeet[side], feet[side]) <= .055f;
                ImpactMotion.SetFootSupport(presentedFeet[0], presentedFeet[1], supportConfirmed[0], supportConfirmed[1]);
                return;
            }
            bool transferring = moving || settlingFoot || catching;
            for (int side = 0; side < 2; side++)
                if (active && hasPresentedContacts && ImpactMotion != null && (ImpactMotion.IsActive || catching) &&
                    supportConfirmed[side] && (!transferring || swing != side) &&
                    Vector3.Distance(presentedFeet[side], feet[side]) > .055f)
                    supportConfirmed[side] = false;
            ImpactMotion?.SetFootSupport(feet[0], feet[1],
                active && supportConfirmed[0] && (!kickOwnsFoot || KickFootSide != 0) && (!transferring || swing != 0),
                active && supportConfirmed[1] && (!kickOwnsFoot || KickFootSide != 1) && (!transferring || swing != 1));
        }

        private void ReportAuthoredStepSupport()
        {
            // The authored opening/closing shuffle owns its bones instead of
            // the ordinary gait IK. That is not absence of physical support:
            // judge the loaded sole that was actually drawn on the real floor.
            for (int side = 0; side < 2; side++)
            {
                Vector3 ankle = presentedFeet[side];
                bool loaded = !recoveryStepOwned || recoveryStepSecondLanded ||
                    side == (recoveryStepFirstLanded ? 1 - recoveryStepSupport : recoveryStepSupport);
                Vector3 grounded = default;
                bool floor = hasPresentedContacts && loaded && TryCatchGround(ankle, side, out grounded);
                stepGroundGaps[side] = floor ? Vector3.Distance(ankle, grounded) : -1f;
                supportConfirmed[side] = floor && stepGroundGaps[side] <= .055f && LandingClear(grounded) &&
                    (!recoveryStepOwned || Vector3.Distance(ankle, feet[side]) <= .055f);
                if (hasPresentedContacts && !recoveryStepOwned) feet[side] = ankle;
            }
            ImpactMotion?.SetFootSupport(hasPresentedContacts ? presentedFeet[0] : feet[0],
                hasPresentedContacts ? presentedFeet[1] : feet[1], supportConfirmed[0], supportConfirmed[1]);
        }

        private void ReportMovementLanding(MeleeCombatant state)
        {
            if (ImpactMotion == null) return;
            if (!ImpactMotion.IsActive || recoveryStepOwned || catching || yielded || state.IsKnockedDown ||
                state.IsDefeated || state.IsKicking || kickOwnsFoot || kickLandingPending)
            { ImpactMotion.CancelMovementLanding(); return; }
            int side;
            Vector3 target, rootTravel;
            float remaining;
            if (state.Phase == MeleePhase.Step)
            {
                if (JournalActor == null || JournalActor.StepTravelBlocked || state.StepTravelProgress >= 1f)
                { ImpactMotion.CancelMovementLanding(); return; }
                int half = state.StepTravelProgress < .5f ? 0 : 1;
                if (ordinaryStepSequence != state.AttackSequence || ordinaryStepHalf != half)
                {
                    ordinaryStepSequence = state.AttackSequence; ordinaryStepHalf = half;
                    movementLandingId++;
                }
                int clip = Array.IndexOf(CombatAssetProvider.StepClipNames, JournalActor.ActiveClipName);
                if (clip < 0) { ImpactMotion.CancelMovementLanding(); return; }
                int leading = StepLeadingFoot(JournalActor.ActiveClipName);
                side = half == 0 ? leading : 1 - leading;
                float landingTravel = (half + 1) * .5f;
                remaining = state.Settings.StepTravelSeconds * landingTravel - state.StepElapsed;
                rootTravel = JournalActor.StepDirection * (state.Settings.StepDistance *
                    (Smooth(landingTravel) - Smooth(state.StepTravelProgress)));
                PoseFrame landing = stepLandingPoses[clip, half];
                target = frame.TransformPoint(landing.Foot(side)) + rootTravel;
            }
            else if (settlingFoot)
            {
                side = swing; remaining = settleDuration - settling;
                rootTravel = Vector3.zero;
                target = frame.TransformPoint(restFeet[side]);
            }
            else if (moving && idleSeconds < .075f && achievedGaitVelocity.sqrMagnitude > .0001f)
            {
                side = swing;
                float boundary = cycle < .5f ? .5f : 1f;
                float distance = (boundary - cycle) * CombatAssetProvider.LocomotionCycleDistance;
                remaining = distance / achievedGaitVelocity.magnitude;
                // Only the rest of this authored half-stride, never an open-ended
                // velocity promise. A stop/turn replaces it with a short settle.
                rootTravel = achievedGaitVelocity.normalized * distance;
                target = DesiredFoot(Sample(boundary), side) + rootTravel;
            }
            else { ImpactMotion.CancelMovementLanding(); return; }
            if (remaining <= 0f || !ClearRootTravel(rootTravel))
            { ImpactMotion.CancelMovementLanding(); return; }
            Vector3 carry = ImpactMotion.PredictRecoveryDisplacement(remaining);
            target += carry;
            Vector3 from = hasPresentedContacts ? presentedFeet[side] : feet[side];
            if (!TryCatchGround(target, side, out Vector3 ground) || Vector3.Distance(target, ground) > .055f ||
                !LandingClear(ground) || !ClearFootTravel(from, Vector3.Lerp(from, ground, .5f) + Vector3.up * .06f) ||
                !ClearFootTravel(Vector3.Lerp(from, ground, .5f) + Vector3.up * .06f, ground))
            { ImpactMotion.CancelMovementLanding(); return; }
            ImpactMotion.ReportMovementLanding(movementLandingId, side, ground, remaining, rootTravel + carry);
        }

        private bool ClearRootTravel(Vector3 travel)
        {
            if (JournalActor?.Body == null || travel.sqrMagnitude < .000001f) return true;
            CharacterController body = JournalActor.Body;
            Vector3 centre = frame.TransformPoint(body.center);
            float radius = Mathf.Max(.01f, body.radius - body.skinWidth);
            float half = Mathf.Max(0f, body.height * .5f - body.radius);
            JournalActor.JournalPhysicsQuery();
            int count = Physics.CapsuleCastNonAlloc(centre + Vector3.up * half, centre - Vector3.up * half,
                radius, travel.normalized, sweepHits, travel.magnitude, ~0, QueryTriggerInteraction.Ignore);
            if (count == sweepHits.Length) return false;
            for (int i = 0; i < count; i++)
                if (sweepHits[i].collider != null && !sweepHits[i].collider.transform.IsChildOf(frame) &&
                    sweepHits[i].normal.y < .65f) return false;
            return true;
        }

        // Called only after the actor's final constrained pose. Root movement in
        // the next simulation step moves the rig temporarily; those intermediate
        // bone positions are not evidence that a planted foot left the floor.
        internal void CapturePresentedContacts()
        {
            bool stepping = JournalActor != null && JournalActor.State.Phase == MeleePhase.Step;
            if ((!initialized || yielded) && !stepping) { hasPresentedContacts = false; return; }
            presentedFeet[0] = bones[3].position;
            presentedFeet[1] = bones[6].position;
            presentedStepLeading = -1;
            presentedStepTravel = stepping ? JournalActor.State.StepTravelProgress : 0f;
            if (stepping) presentedStepLeading = StepLeadingFoot(JournalActor.ActiveClipName);
            hasPresentedContacts = true;
        }

        private static int StepLeadingFoot(string clip) =>
            clip == "CombatStepForward" || clip == "CombatStepLeft" ? 0 :
            clip == "CombatStepBackward" || clip == "CombatStepRight" ? 1 : -1;

        private static bool StepFootLoaded(int side, int leading, float travel) =>
            leading < 0 || travel >= 1f || side == (travel < .5f ? 1 - leading : leading);

        private bool NeedsAuthoredStepContacts => JournalActor != null && JournalActor.State.Phase == MeleePhase.Step &&
            (recoveryStepOwned || ImpactMotion != null && ImpactMotion.IsActive);

        private void CaptureAuthoredStepContacts()
        {
            int leading = StepLeadingFoot(JournalActor.ActiveClipName);
            float travel = JournalActor.State.StepTravelProgress;
            for (int side = 0; side < 2; side++)
            {
                // This is the sampled original clip before the final pose blend,
                // not a previous catching foot or an assumed ground position.
                Vector3 ankle = bones[3 + side * 3].position;
                authoredStepContacts[side] = StepFootLoaded(side, leading, travel) &&
                    TryCatchGround(ankle, side, out Vector3 grounded) &&
                    Vector3.Distance(ankle, grounded) <= .055f && LandingClear(grounded);
                authoredStepFeet[side] = ankle;
                authoredStepRotations[side] = bones[3 + side * 3].rotation;
            }
        }

        private void AdvanceFootwork(float seconds, MeleeCombatant state)
        {
            Vector3 displacement = frame.position - previousPosition;
            float turn = Vector3.Angle(previousForward, frame.forward) * Mathf.Deg2Rad;
            previousPosition = frame.position; previousForward = frame.forward;
            displacement.y = 0f;
            if (ordinaryStepTracked && state.Phase != MeleePhase.Step)
            {
                // Keep the last rendered soles when the original clip returns
                // to locomotion. A fresh ready stance would invent a landing.
                ordinaryStepTracked = false; initialized = true; yielded = false;
                moving = settlingFoot = false; gaitOffset = Vector3.zero;
                for (int side = 0; side < 2; side++)
                {
                    if (hasPresentedContacts) feet[side] = presentedFeet[side];
                    rotations[side] = bones[3 + side * 3].rotation;
                    supportConfirmed[side] = PresentedGrounded(side, out _);
                }
            }
            if (recoveryStepOwned)
            {
                if (state.Phase == MeleePhase.Step) { AdvanceAuthoredRecoveryStep(state); return; }
                ReleaseAuthoredRecoveryStep();
            }
            if (kickOwnsFoot && !state.IsKicking) EndKickSupport();
            if (state.IsKicking && kickOwnsFoot)
            { moving = settlingFoot = false; gaitOffset = Vector3.zero; return; }
            if (kickLandingPending && TryCatchGround(bones[3 + KickFootSide * 3].position, KickFootSide, out Vector3 landing) &&
                Vector3.Distance(bones[3 + KickFootSide * 3].position, landing) <= .08f && LandingClear(landing))
            {
                kickLandingPending = false;
                feet[KickFootSide] = landing;
                rotations[KickFootSide] = bones[3 + KickFootSide * 3].rotation;
                supportConfirmed[KickFootSide] = true;
            }
            if (state.Phase == MeleePhase.Step && ImpactMotion != null && ImpactMotion.IsActive)
            {
                if (!ordinaryStepTracked)
                {
                    if (catching) JournalCatch("catch_cancelled", "action_owns_feet");
                    ImpactMotion.CancelRecoveryStep();
                    catching = catchAwaitingContact = catchDecisionPending = catchStabilityPending = false;
                    ordinaryStepTracked = true; initialized = true; yielded = false;
                    moving = settlingFoot = false; gaitOffset = Vector3.zero;
                }
                return;
            }
            bool yield = state.Phase == MeleePhase.Step || state.IsDefeated || state.IsKnockedDown;
            if (yield)
            {
                if (catching) JournalCatch("catch_cancelled", "action_owns_feet");
                ImpactMotion?.CancelRecoveryStep();
                yielded = true; initialized = false; moving = settlingFoot = catching = catchAwaitingContact = catchDecisionPending = catchStabilityPending = false;
                kickOwnsFoot = kickLandingPending = false;
                recoveryGaitReleased = false;
                gaitOffset = Vector3.zero; return;
            }
            if (!initialized || displacement.sqrMagnitude > 1f)
            {
                if (catching) JournalCatch("catch_cancelled", "root_discontinuity");
                ImpactMotion?.CancelRecoveryStep();
                PlantReady(); moving = settlingFoot = catching = catchAwaitingContact = catchDecisionPending = catchStabilityPending = false;
                recoveryGaitReleased = false;
                gaitOffset = Vector3.zero; cycle = 0f;
            }
            yielded = false;

            if (AdvanceCatchStep(seconds)) return;

            // Commit the current transfer inside the existing tell, including the short chain tell.
            // Its supporting foot stays at its actual contact; input never waits for a gait boundary.
            if (state.Phase == MeleePhase.Windup && attackSequence != state.AttackSequence)
            {
                attackSequence = state.AttackSequence;
                float remaining = Mathf.Max(.02f, state.AttackWindupSeconds - state.AttackElapsed);
                int side = moving || settlingFoot ? swing : FarthestFoot();
                if (moving || settlingFoot || Vector3.Distance(feet[side], frame.TransformPoint(restFeet[side])) > .025f)
                    BeginSettle(side, Mathf.Min(.13f, remaining * .45f), true);
                moving = false;
            }
            if (state.Phase == MeleePhase.Active)
            {
                if (settlingFoot) FinishSettle();
                gaitOffset = Vector3.zero; moving = false; return;
            }
            if (settlingFoot)
            {
                settling += seconds;
                float t = Mathf.Clamp01(settling / settleDuration);
                float blend = Smooth(t);
                Vector3 target = frame.TransformPoint(restFeet[swing]);
                float lift = Mathf.Sin(t * Mathf.PI);
                feet[swing] = Vector3.Lerp(settleStart, target, blend) + Vector3.up * (.025f * lift * lift);
                rotations[swing] = Quaternion.Slerp(settleRotation, frame.rotation * restRotations[swing], blend);
                gaitOffset = settleOffset * (1f - blend);
                if (t >= 1f)
                {
                    FinishSettle();
                    int other = 1 - swing;
                    float remaining = state.Phase == MeleePhase.Windup ? state.AttackWindupSeconds - state.AttackElapsed : .2f;
                    if (remaining > .025f && Vector3.Distance(feet[other], frame.TransformPoint(restFeet[other])) > .025f)
                        BeginSettle(other, Mathf.Min(.13f, remaining * .8f), settlingAttack);
                }
                return;
            }
            if (state.Phase == MeleePhase.Windup)
            {
                // Continued aiming can outgrow the initial foot plant. Reuse the
                // ordinary short transfer while the tell still has room to land it.
                float remaining = state.AttackWindupSeconds - state.AttackElapsed;
                int side = FarthestFoot();
                if (remaining >= .06f && Vector3.Distance(feet[side], frame.TransformPoint(restFeet[side])) > .035f)
                    BeginSettle(side, Mathf.Min(.13f, remaining * .65f), true);
                return;
            }
            bool allowed = state.Phase == MeleePhase.Ready || state.Phase == MeleePhase.Charging || state.Phase == MeleePhase.Recovery;
            float distance = displacement.magnitude;
            float travel = distance;
            // Turning on the spot lifts and replaces the more displaced boot;
            // it does not play a lateral metre-stride while the root stands still.
            if (allowed && distance < .00005f && turn > .00001f)
            {
                int side = moving ? swing : FarthestFoot();
                if (Vector3.Distance(feet[side], frame.TransformPoint(restFeet[side])) > .035f)
                { BeginSettle(side, .13f, false); moving = false; return; }
            }
            idleSeconds = travel < .00005f ? idleSeconds + seconds : 0f;
            if (!allowed || travel < .00005f)
            {
                // A rendered motor update can feed several duel substeps. An empty
                // substep is not a stop; consume each achieved displacement once.
                if (moving && (!allowed || idleSeconds >= .075f))
                { BeginSettle(swing, .13f, false); moving = false; }
                return;
            }
            Vector3 local = frame.InverseTransformDirection(displacement.normalized);
            local.y = 0f;
            if (!moving)
            {
                // On a diagonal the nearer side opens first. A forward-right
                // shuffle led by the left boot would cross the planted right leg.
                direction = Mathf.Abs(local.x) > Mathf.Abs(local.z) * .2f
                    ? (local.x < 0 ? 2 : 3) : (local.z >= 0 ? 0 : 1);
                travelDirection = local.normalized; cycle = 0f; moving = true;
                swing = direction == 0 || direction == 2 ? 0 : 1;
                BeginSwing();
            }
            // Direction changes are resolved through a short planted settle, not a new clip's first frame.
            else if (Vector3.Dot(local, travelDirection) < .9f)
            { BeginSettle(swing, .10f, false); moving = false; return; }

            float next = cycle + travel / CombatAssetProvider.LocomotionCycleDistance;
            if (next >= (cycle < .5f ? .5f : 1f))
            {
                float boundary = cycle < .5f ? .5f : 1f;
                EvaluateSwing(boundary);
                cycle = boundary == 1f ? 0f : .5f;
                swing = 1 - swing;
                BeginSwing();
                next = cycle + Mathf.Min(.45f, next - boundary);
            }
            cycle = next;
            EvaluateSwing(cycle);
            achievedGaitVelocity = displacement / seconds;
        }

        private bool AdvanceCatchStep(float seconds)
        {
            if (ImpactMotion == null) { catchDecisionPending = catchStabilityPending = false; return false; }
            if (recoverySequence != ImpactMotion.RecoverySequence)
            {
                recoverySequence = ImpactMotion.RecoverySequence;
                sequenceSteps = 0; catchRetry = 0f; catchDecisionPending = catchStabilityPending = false;
                recoveryGaitReleased = false;
            }
            // Keep the step budget for this impulse, but stop owning the gait
            // as soon as its final support decision has released the motor.
            if (recoveryGaitReleased) return false;
            catchRetry = Mathf.Max(0f, catchRetry - seconds);
            bool needsLanding = !supportConfirmed[0] || !supportConfirmed[1];
            if (!ImpactMotion.IsActive && !catching && !needsLanding)
            { catchDecisionPending = false; return false; }
            if (!catching)
            {
                int displaced = FarthestFoot();
                Vector3 capture = ImpactMotion.CaptureOffset;
                float error = Mathf.Max(Vector3.ProjectOnPlane(feet[0] - RecoveryTarget(0, capture), Vector3.up).magnitude,
                    Vector3.ProjectOnPlane(feet[1] - RecoveryTarget(1, capture), Vector3.up).magnitude);
                float urgency = ImpactMotion.RecoveryUrgency;
                bool stable = !needsLanding && urgency < .42f && error < .11f;
                if (stable || catchRetry > 0f ||
                    sequenceSteps >= ImpactMotion.MaximumRecoverySteps)
                {
                    if (stable || sequenceSteps >= ImpactMotion.MaximumRecoverySteps) catchDecisionPending = false;
                    return needsLanding || sequenceSteps > 0;
                }
                bool alreadyTransferring = moving || settlingFoot;
                if (!alreadyTransferring)
                {
                    Vector3 across = Vector3.ProjectOnPlane(feet[1] - feet[0], Vector3.up);
                    float lateral = Vector3.Dot(capture, across.normalized);
                    float rightLoad = across.sqrMagnitude > .001f ? Mathf.Clamp01(Vector3.Dot(
                        ImpactMotion.CentreOfMass - feet[0], across) / across.sqrMagnitude) : .5f;
                    // A boot already in the air goes first. Otherwise preserve the
                    // actually loaded boot; direction breaks only a near-even tie.
                    swing = !supportConfirmed[0] ? 0 : !supportConfirmed[1] ? 1 :
                        Mathf.Abs(rightLoad - .5f) > .1f ? (rightLoad > .5f ? 0 : 1) :
                        Mathf.Abs(lateral) > .06f ? (lateral > 0f ? 1 : 0) : displaced;
                }
                float skill = Mathf.Clamp01(ImpactMotion.RecoverySkill);
                float severity = Mathf.Clamp01(urgency * .65f);
                float duration = Mathf.Lerp(.30f, .19f, severity) + (1f - skill) * .055f;
                catchLift = Mathf.Lerp(.045f, .105f, severity) + (1f - skill) * .015f;
                int preferred = swing;
                bool found = TryCatchTarget(preferred, capture, skill, out Vector3 target, out float score);
                // Compare useful support, rather than repeatedly moving whichever
                // foot is easiest to lift. The loaded foot wins only for a clearly
                // better landing; an already airborne boot must land first.
                bool canChangeSide = !alreadyTransferring && supportConfirmed[preferred];
                if (!found && !canChangeSide) canChangeSide = ConfirmCurrentContact(preferred);
                if (canChangeSide && TryCatchTarget(1 - preferred, capture, skill, out Vector3 alternative, out float otherScore) &&
                    (!found || otherScore > score + .035f))
                {
                    swing = 1 - preferred; target = alternative; found = true;
                }
                if (!found)
                {
                    // A refused next step is a completed decision, not a hand
                    // reservation that can persist while retries are impossible.
                    catchDecisionPending = false;
                    if (needsLanding || ImpactMotion.RecoveryFootError(0, feet[0]) > .025f) RejectCatchPlan("no_valid_target");
                    return needsLanding || sequenceSteps > 0;
                }
                settleStart = feet[swing]; settleRotation = rotations[swing]; catchTarget = target;
                Vector3 plannedTravel = Vector3.ProjectOnPlane(target - settleStart, Vector3.up);
                float pivot = Mathf.Clamp(Vector3.SignedAngle(frame.forward, plannedTravel, Vector3.up), -18f, 18f);
                catchRotation = Quaternion.AngleAxis(pivot, Vector3.up) * frame.rotation * restRotations[swing];
                // A short lateral save is quicker than a full emergency lunge.
                float distance = Vector3.ProjectOnPlane(target - settleStart, Vector3.up).magnitude;
                settleDuration = Mathf.Clamp(duration + (distance - .2f) * .18f, .16f, .36f);
                settling = 0f; catching = true; catchAwaitingContact = catchDecisionPending = false;
                supportConfirmed[swing] = false;
                sequenceSteps++; CatchStepCount++;
                LastCatchTarget = target; LastCatchSide = swing;
                moving = settlingFoot = false; gaitOffset = Vector3.zero;
                ImpactMotion.BeginRecoveryStep(swing, target, settleDuration);
                JournalCatch("catch_planned", "support_recovery");
            }
            if (catchAwaitingContact)
            {
                // Last presentation has now actually placed the ankle. A leg
                // clipped by IK or a new obstruction buys no imaginary support.
                bool reached = hasPresentedContacts && Vector3.Distance(presentedFeet[swing], catchTarget) <= .055f;
                Vector3 grounded = default;
                string rejection = !reached ? "presented_foot_gap" :
                    !TryCatchGround(catchTarget, swing, out grounded) ? "ground_missing" :
                    !(Vector3.Distance(grounded, catchTarget) <= .025f) ? "ground_target_gap" :
                    !LandingClear(grounded) ? "landing_obstructed" :
                    !ClearFootTravel(feet[swing], grounded) ? "foot_path_obstructed" : null;
                if (rejection == null)
                {
                    feet[swing] = grounded;
                    supportConfirmed[swing] = true;
                    catching = catchAwaitingContact = false;
                    ReportSupport();
                    ImpactMotion.LandRecoveryStep(swing, grounded);
                    CatchLandingCount++;
                    JournalCatch("catch_landed", "presented_contact");
                    catchRetry = .025f;
                    catchDecisionPending = sequenceSteps < ImpactMotion.MaximumRecoverySteps;
                    RetroAudio.PlayAt(RetroSfxId.FootstepConcrete, grounded, .7f);
                }
                else
                {
                    // Continue from the foot that was really drawn, never teleport
                    // it to the failed destination before trying a nearer target.
                    if (hasPresentedContacts) feet[swing] = presentedFeet[swing];
                    catching = catchAwaitingContact = false;
                    catchDecisionPending = false;
                    ConfirmCurrentContact(swing);
                    ImpactMotion.CancelRecoveryStep();
                    RejectCatchPlan(rejection);
                }
                return true;
            }
            settling += seconds;
            float t = Mathf.Clamp01(settling / settleDuration), blend = Smooth(t);
            float lift = Mathf.Sin(t * Mathf.PI);
            feet[swing] = Vector3.Lerp(settleStart, catchTarget, blend) + Vector3.up * (catchLift * lift * lift);
            rotations[swing] = Quaternion.Slerp(settleRotation, catchRotation, blend);
            // ImpactMotion owns the buckle. A second sinusoidal pelvis drop here
            // fought the planted-leg correction and produced a deep repeated bob.
            gaitOffset = Vector3.zero;
            if (t >= 1f) catchAwaitingContact = true;
            return true;
        }

        private void RejectCatchPlan(string reason)
        {
            JournalCatch("catch_rejected", reason);
            BlockedCatchCount++; catchRetry = .075f;
            ImpactMotion.StepBlocked();
        }

        private void JournalCatch(string eventName, string reason)
        {
            if (JournalActor?.Journal == null) return;
            JournalActor.JournalEvent(eventName,
                f0: GameLog.Field("reason", reason), f1: GameLog.Field("recovery_sequence", recoverySequence),
                f2: GameLog.Field("side", swing), f3: GameLog.Field("target_x", catchTarget.x),
                f4: GameLog.Field("target_y", catchTarget.y), f5: GameLog.Field("target_z", catchTarget.z),
                f6: GameLog.Field("presented_gap", hasPresentedContacts ? Vector3.Distance(presentedFeet[swing], catchTarget) : -1f),
                f7: GameLog.Field("duration", settleDuration));
        }

        private bool ConfirmCurrentContact(int side)
        {
            if (!hasPresentedContacts || !TryCatchGround(feet[side], side, out Vector3 ground) ||
                Vector3.Distance(ground, feet[side]) > .025f ||
                Vector3.Distance(presentedFeet[side], ground) > .055f || !LandingClear(ground)) return false;
            supportConfirmed[side] = true;
            return true;
        }

        private Vector3 RecoveryTarget(int side, Vector3 capture)
        {
            Vector3 stance = frame.TransformVector(restFeet[side] - (restFeet[0] + restFeet[1]) * .5f);
            Vector3 target = ImpactMotion.CentreOfMass + capture + stance;
            target.y = feet[side].y;
            return target;
        }

        private bool TryCatchTarget(int side, Vector3 capture, float skill, out Vector3 target, out float bestScore)
        {
            target = feet[side]; bestScore = float.NegativeInfinity;
            Vector3 outward = Vector3.ProjectOnPlane(frame.right * (side == 0 ? -1f : 1f), Vector3.up).normalized;
            // Reproducible variation belongs to the whole attempt, never the
            // number of render/weapon samples. Lower skill adds a small aim error.
            int variant = (recoverySequence * 31 + sequenceSteps * 17 + side * 7) & 7;
            float aimError = (variant / 7f - .5f) * .07f * (1f - skill);
            Vector3 desired = RecoveryTarget(side, capture);
            Vector3 neutral = RecoveryTarget(side, Vector3.zero);
            float previousError = ImpactMotion.RecoveryFootError(0, feet[0]);
            float previousDistance = Vector3.ProjectOnPlane(feet[side] - desired, Vector3.up).magnitude;
            bool found = false;
            for (int candidate = 0; candidate < 4; candidate++)
            {
                Vector3 proposed = candidate switch
                {
                    0 => desired,
                    1 => Vector3.Lerp(feet[side], desired, .8f) + outward * .06f,
                    2 => desired + outward * .10f,
                    _ => neutral
                };
                proposed += outward * aimError;
                if (!ConstrainCatchTarget(side, proposed, out Vector3 grounded) ||
                    Vector3.ProjectOnPlane(grounded - feet[side], Vector3.up).sqrMagnitude < .0025f) continue;
                float error = ImpactMotion.RecoveryFootError(side, grounded);
                float closure = previousDistance - Vector3.ProjectOnPlane(grounded - desired, Vector3.up).magnitude;
                float gain = previousError - error;
                if (supportConfirmed[side] && (error > previousError + .015f || (gain < .01f && closure < .035f))) continue;
                float distance = Vector3.ProjectOnPlane(grounded - feet[side], Vector3.up).magnitude;
                float score = gain * 6f + closure * .5f - distance * .03f;
                if (score <= bestScore) continue;
                Vector3 middle = Vector3.Lerp(feet[side], grounded, .5f) + Vector3.up * catchLift;
                if (!LandingClear(grounded) || !ClearFootTravel(feet[side], middle) || !ClearFootTravel(middle, grounded)) continue;
                target = grounded; bestScore = score; found = true;
            }
            return found;
        }

        private bool ConstrainCatchTarget(int side, Vector3 desired, out Vector3 target)
        {
            target = desired;
            if (!TryCatchGround(target, side, out target)) return false;
            Vector3 outward = Vector3.ProjectOnPlane(frame.right * (side == 0 ? -1f : 1f), Vector3.up).normalized;
            float separation = Vector3.Dot(target - feet[1 - side], outward);
            if (separation < .12f) target += outward * (.12f - separation);
            Vector3 hip = bones[1 + side * 3].position;
            Vector3 horizontal = Vector3.ProjectOnPlane(target - hip, Vector3.up);
            float length = legLengths[side] * .98f;
            float height = Mathf.Max(0f, hip.y - target.y - .10f);
            float reach = Mathf.Sqrt(Mathf.Max(0f, length * length - height * height));
            horizontal = Vector3.ClampMagnitude(horizontal, Mathf.Min(.44f, reach));
            target.x = hip.x + horizontal.x; target.z = hip.z + horizontal.z;
            if (Vector3.Dot(target - feet[1 - side], outward) < .115f ||
                !TryCatchGround(target, side, out target)) return false;
            return true;
        }

        private bool ClearFootTravel(Vector3 from, Vector3 to)
        {
            Vector3 start = from + Vector3.up * .045f;
            Vector3 travel = to - from;
            float distance = travel.magnitude;
            if (distance < .001f) return true;
            JournalActor?.JournalPhysicsQuery();
            int count = Physics.SphereCastNonAlloc(start, .045f, travel / distance,
                sweepHits, distance, ~0, QueryTriggerInteraction.Ignore);
            if (count == sweepHits.Length)
            { JournalActor?.JournalQueryBufferFull(10, "foot_travel", sweepHits.Length); return false; }
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = sweepHits[i];
                if (hit.collider != null && !hit.collider.transform.IsChildOf(frame) && hit.normal.y < .65f) return false;
            }
            return true;
        }

        private bool LandingClear(Vector3 point)
        {
            JournalActor?.JournalPhysicsQuery();
            int count = Physics.OverlapSphereNonAlloc(point + Vector3.up * .025f, .04f,
                landingOverlaps, ~0, QueryTriggerInteraction.Ignore);
            if (count == landingOverlaps.Length)
            { JournalActor?.JournalQueryBufferFull(11, "foot_landing", landingOverlaps.Length); return false; }
            for (int i = 0; i < count; i++)
                if (landingOverlaps[i] != null && !landingOverlaps[i].transform.IsChildOf(frame)) return false;
            return true;
        }

        private bool TryCatchGround(Vector3 desired, int side, out Vector3 grounded)
        {
            grounded = desired;
            float nearest = float.PositiveInfinity;
            JournalActor?.JournalPhysicsQuery();
            int count = Physics.RaycastNonAlloc(desired + Vector3.up * .55f, Vector3.down,
                groundHits, 1.1f, ~0, QueryTriggerInteraction.Ignore);
            if (count == groundHits.Length)
            { JournalActor?.JournalQueryBufferFull(12, "foot_ground", groundHits.Length); return false; }
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = groundHits[i];
                if (hit.collider == null || hit.collider.GetComponentInParent<CombatActor>() != null ||
                    hit.normal.y < .65f || hit.distance >= nearest) continue;
                nearest = hit.distance;
                grounded = hit.point + Vector3.up * Mathf.Max(.025f, restFeet[side].y);
            }
            return nearest < float.PositiveInfinity;
        }

        private void BeginSwing()
        {
            movementLandingId++;
            PoseFrame pose = Sample(cycle);
            correction[swing] = feet[swing] - DesiredFoot(pose, swing);
            swingRotations[swing] = rotations[swing];
        }

        private void EvaluateSwing(float phase)
        {
            PoseFrame pose = Sample(phase);
            float half = phase <= .5f && cycle < .5f ? phase * 2f : (phase - .5f) * 2f;
            float blend = Smooth(Mathf.Clamp01(half));
            feet[swing] = DesiredFoot(pose, swing) + correction[swing] * (1f - blend);
            rotations[swing] = Quaternion.Slerp(swingRotations[swing], frame.rotation * pose.Rotation(swing), blend);
            gaitOffset = DirectionRotation() * (pose.Pelvis - readyPelvis);
        }

        private Quaternion DirectionRotation()
        {
            Vector3 axis = direction == 0 ? Vector3.forward : direction == 1 ? Vector3.back : direction == 2 ? Vector3.left : Vector3.right;
            return Quaternion.FromToRotation(axis, travelDirection);
        }

        private Vector3 DesiredFoot(PoseFrame pose, int side)
        {
            Vector3 excursion = pose.Foot(side) - restFeet[side];
            excursion = DirectionRotation() * excursion;
            return frame.TransformPoint(restFeet[side] + excursion);
        }

        private PoseFrame Sample(float phase)
        {
            float sample = Mathf.Clamp01(phase) * Samples;
            int index = Mathf.Min(Samples - 1, (int)sample);
            float t = sample - index;
            PoseFrame a = curves[direction][index], b = curves[direction][index + 1];
            return new PoseFrame { Pelvis = Vector3.Lerp(a.Pelvis, b.Pelvis, t),
                Left = Vector3.Lerp(a.Left, b.Left, t), Right = Vector3.Lerp(a.Right, b.Right, t),
                LeftRotation = Quaternion.Slerp(a.LeftRotation, b.LeftRotation, t), RightRotation = Quaternion.Slerp(a.RightRotation, b.RightRotation, t) };
        }

        private int FarthestFoot() => (feet[0] - frame.TransformPoint(restFeet[0])).sqrMagnitude >=
            (feet[1] - frame.TransformPoint(restFeet[1])).sqrMagnitude ? 0 : 1;

        private void BeginSettle(int side, float duration, bool attack)
        {
            movementLandingId++;
            swing = side; settling = 0f; settleDuration = Mathf.Max(.02f, duration);
            settleStart = feet[side]; settleRotation = rotations[side]; settleOffset = gaitOffset;
            settlingFoot = true; settlingAttack = attack;
        }

        private void FinishSettle()
        {
            feet[swing] = frame.TransformPoint(restFeet[swing]);
            rotations[swing] = frame.rotation * restRotations[swing];
            settlingFoot = false; gaitOffset = Vector3.zero;
        }

        public void Apply()
        {
            Restore();
            impactPelvisFloor = pelvis.position.y - .20f;
            if (NeedsAuthoredStepContacts)
            {
                if (!recoveryStepOwned) CaptureAuthoredStepContacts();
                else ImpactMotion?.Apply();
                return;
            }
            if (!yielded) ImpactMotion?.Apply();
            if (!initialized || yielded) return;
            for (int i = 0; i < bones.Length; i++) { basePositions[i] = bones[i].localPosition; baseRotations[i] = bones[i].localRotation; }
            applied = true;
            pelvis.position += frame.TransformVector(gaitOffset);
            ConstrainContacts(false);
        }

        // Transition source/target poses already include the weight shift. Re-close
        // their contacts afterwards without applying that shift a second time.
        public void ConstrainContacts() => ConstrainContacts(true);

        private void ConstrainContacts(bool preserveBend)
        {
            bool authoredStep = NeedsAuthoredStepContacts;
            if (authoredStep)
            {
                if (!recoveryStepOwned && !authoredStepContacts[0] && !authoredStepContacts[1]) return;
                if (!applied)
                {
                    for (int i = 0; i < bones.Length; i++)
                    { basePositions[i] = bones[i].localPosition; baseRotations[i] = bones[i].localRotation; }
                    applied = true;
                }
            }
            else if (!initialized || yielded) return;
            if (kickOwnsFoot || kickLandingPending) ConstrainKickWorld();
            // The animation owns the weight shift; only lower a hip if a planted leg would lock straight.
            float lower = 0f;
            for (int side = 0; side < 2; side++)
            {
                if (authoredStep ? !recoveryStepOwned && !authoredStepContacts[side] :
                    (kickOwnsFoot || kickLandingPending) && side == KickFootSide) continue;
                Vector3 delta = bones[1 + side * 3].position - (authoredStep && !recoveryStepOwned ? authoredStepFeet[side] : feet[side]);
                float horizontal = delta.x * delta.x + delta.z * delta.z;
                float length = legLengths[side] * .997f;
                lower = Mathf.Max(lower, delta.y - Mathf.Sqrt(Mathf.Max(.01f, length * length - horizontal)));
            }
            float correction = Mathf.Clamp(lower, 0f, .18f);
            if (ImpactMotion != null && ImpactMotion.IsActive)
                correction = Mathf.Min(correction, Mathf.Max(0f, pelvis.position.y - impactPelvisFloor));
            pelvis.position -= Vector3.up * correction;
            for (int side = 0; side < 2; side++)
            {
                if (authoredStep ? !recoveryStepOwned && !authoredStepContacts[side] :
                    (kickOwnsFoot || kickLandingPending) && side == KickFootSide) continue;
                int i = 1 + side * 3;
                // The sampled target uses the ordinary knee pole. After the
                // final pose blend, keep its inherited bend plane while closing
                // the same sole: replacing that plane would snap a step's knee.
                Vector3 hint = preserveBend && !authoredStep ? bones[i + 1].position :
                    bones[i].position + frame.forward * .7f + (side == 0 ? -frame.right : frame.right) * .08f;
                LimbTwoBoneIk.Solve(bones[i], bones[i + 1], bones[i + 2],
                    authoredStep && !recoveryStepOwned ? authoredStepFeet[side] : feet[side],
                    authoredStep && !recoveryStepOwned ? authoredStepRotations[side] : rotations[side],
                    hint,
                    1f, .999f, true);
            }
        }

        private void ConstrainKickWorld()
        {
            int chain = 1 + KickFootSide * 3;
            Vector3 target = bones[chain + 2].position, origin = bones[chain].position;
            Vector3 travel = target - origin;
            float length = travel.magnitude;
            if (length < .00001f) return;
            JournalActor?.JournalPhysicsQuery();
            int count = Physics.SphereCastNonAlloc(origin, .12f, travel / length, sweepHits, length, ~0,
                QueryTriggerInteraction.Ignore);
            float distance = length;
            if (count == sweepHits.Length) distance = 0f;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = sweepHits[i];
                if (hit.collider == null || hit.collider.GetComponentInParent<CombatActor>() != null || hit.normal.y > .65f) continue;
                distance = Mathf.Min(distance, Mathf.Max(0f, hit.distance - .04f));
            }
            if (distance >= length) return;
            KickWorldBlocked = true;
            LimbTwoBoneIk.Solve(bones[chain], bones[chain + 1], bones[chain + 2], origin + travel / length * distance, bones[chain + 2].rotation,
                bones[chain].position + frame.forward * .7f + (KickFootSide == 0 ? -frame.right : frame.right) * .08f, 1f, .999f, true);
        }

        public void Restore()
        {
            if (applied)
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i] != null) { bones[i].localPosition = basePositions[i]; bones[i].localRotation = baseRotations[i]; }
            applied = false;
            KickWorldBlocked = false;
            ImpactMotion?.Restore();
        }
        public void Forget()
        {
            applied = false; catching = catchAwaitingContact = catchDecisionPending = catchStabilityPending = hasPresentedContacts = false;
            recoveryStepOwned = recoveryStepFirstLanded = recoveryStepSecondLanded = recoveryStepObstructed = false;
            recoveryStepSupport = -1;
            kickOwnsFoot = kickLandingPending = false; KickWorldBlocked = false;
            recoveryGaitReleased = false;
            ordinaryStepTracked = false;
            ImpactMotion?.CancelMovementLanding();
            ImpactMotion?.CancelRecoveryStep(); ImpactMotion?.Forget();
        }
        private static float Smooth(float value) => value * value * (3f - 2f * value);
    }
}
