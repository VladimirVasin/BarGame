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
        private readonly Vector3[] restFeet = new Vector3[2], feet = new Vector3[2], correction = new Vector3[2];
        private readonly Quaternion[] restRotations = new Quaternion[2], rotations = new Quaternion[2], swingRotations = new Quaternion[2];
        private readonly float[] legLengths = new float[2];
        private readonly RaycastHit[] groundHits = new RaycastHit[16], sweepHits = new RaycastHit[16];
        private readonly Collider[] landingOverlaps = new Collider[16];
        private readonly bool[] supportConfirmed = { true, true };
        private readonly Vector3[] presentedFeet = new Vector3[2];
        private readonly Vector3 readyPelvis;
        private Vector3 previousPosition, previousForward, gaitOffset, settleOffset, settleStart;
        private Quaternion settleRotation, catchRotation;
        private Vector3 travelDirection;
        private float cycle, settling, settleDuration, idleSeconds;
        private int direction, swing, attackSequence = -1;
        private bool initialized, moving, applied, yielded, settlingFoot, settlingAttack;
        private bool catching, catchAwaitingContact, hasPresentedContacts;
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
        internal float CatchStepProgress => catching ? Mathf.Clamp01(settling / Mathf.Max(.001f, settleDuration)) : 0f;
        internal Vector3 LastCatchTarget { get; private set; }
        internal int LastCatchSide { get; private set; } = -1;
        internal bool JournalLeftSupport => supportConfirmed[0];
        internal bool JournalRightSupport => supportConfirmed[1];
        internal string SupportDiagnostics => $"confirmed={supportConfirmed[0]}/{supportConfirmed[1]}, " +
            $"presented={hasPresentedContacts}, gaps={Vector3.Distance(presentedFeet[0], feet[0]):F3}/" +
            $"{Vector3.Distance(presentedFeet[1], feet[1]):F3}, swing={swing}, awaiting={catchAwaitingContact}, " +
            $"feet={feet[0]:F3}/{feet[1]:F3}";

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
            idleSeconds = catchRetry = 0f; catchAwaitingContact = hasPresentedContacts = false;
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

        public void Advance(float seconds, MeleeCombatant state)
        {
            if (!float.IsFinite(seconds) || seconds <= 0f) return;
            AdvanceFootwork(seconds, state);
            // Only the duel clock reports support. Pose previews and repeated Apply
            // calls cannot land a boot or change the outcome of a recovery.
            ReportSupport();
        }

        private void ReportSupport()
        {
            bool active = initialized && !yielded;
            bool transferring = moving || settlingFoot || catching;
            for (int side = 0; side < 2; side++)
                if (active && hasPresentedContacts && ImpactMotion != null && (ImpactMotion.IsActive || catching) &&
                    supportConfirmed[side] && (!transferring || swing != side) &&
                    Vector3.Distance(presentedFeet[side], feet[side]) > .055f)
                    supportConfirmed[side] = false;
            ImpactMotion?.SetFootSupport(feet[0], feet[1],
                active && supportConfirmed[0] && (!transferring || swing != 0),
                active && supportConfirmed[1] && (!transferring || swing != 1));
        }

        // Called only after the actor's final constrained pose. Root movement in
        // the next simulation step moves the rig temporarily; those intermediate
        // bone positions are not evidence that a planted foot left the floor.
        internal void CapturePresentedContacts()
        {
            if (!initialized || yielded) { hasPresentedContacts = false; return; }
            presentedFeet[0] = bones[3].position;
            presentedFeet[1] = bones[6].position;
            hasPresentedContacts = true;
        }

        private void AdvanceFootwork(float seconds, MeleeCombatant state)
        {
            Vector3 displacement = frame.position - previousPosition;
            float turn = Vector3.Angle(previousForward, frame.forward) * Mathf.Deg2Rad;
            previousPosition = frame.position; previousForward = frame.forward;
            displacement.y = 0f;
            bool yield = state.Phase == MeleePhase.Step || state.IsDefeated || state.IsKnockedDown;
            if (yield)
            {
                if (catching) JournalCatch("catch_cancelled", "action_owns_feet");
                ImpactMotion?.CancelRecoveryStep();
                yielded = true; initialized = false; moving = settlingFoot = catching = catchAwaitingContact = false;
                gaitOffset = Vector3.zero; return;
            }
            if (!initialized || displacement.sqrMagnitude > 1f)
            {
                if (catching) JournalCatch("catch_cancelled", "root_discontinuity");
                ImpactMotion?.CancelRecoveryStep();
                PlantReady(); moving = settlingFoot = catching = catchAwaitingContact = false;
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
            if (state.Phase == MeleePhase.Windup) return;
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
        }

        private bool AdvanceCatchStep(float seconds)
        {
            if (ImpactMotion == null) return false;
            if (recoverySequence != ImpactMotion.RecoverySequence)
            {
                recoverySequence = ImpactMotion.RecoverySequence;
                sequenceSteps = 0; catchRetry = 0f;
            }
            catchRetry = Mathf.Max(0f, catchRetry - seconds);
            bool needsLanding = !supportConfirmed[0] || !supportConfirmed[1];
            if (!ImpactMotion.IsActive && !catching && !needsLanding) return false;
            if (!catching)
            {
                int displaced = FarthestFoot();
                Vector3 capture = ImpactMotion.CaptureOffset;
                float error = Mathf.Max(Vector3.ProjectOnPlane(feet[0] - RecoveryTarget(0, capture), Vector3.up).magnitude,
                    Vector3.ProjectOnPlane(feet[1] - RecoveryTarget(1, capture), Vector3.up).magnitude);
                float urgency = ImpactMotion.RecoveryUrgency;
                if ((!needsLanding && urgency < .42f && error < .11f) || catchRetry > 0f ||
                    sequenceSteps >= ImpactMotion.MaximumRecoverySteps)
                    return needsLanding || sequenceSteps > 0;
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
                settling = 0f; catching = true; catchAwaitingContact = false;
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
                    RetroAudio.PlayAt(RetroSfxId.FootstepConcrete, grounded, .7f);
                }
                else
                {
                    // Continue from the foot that was really drawn, never teleport
                    // it to the failed destination before trying a nearer target.
                    if (hasPresentedContacts) feet[swing] = presentedFeet[swing];
                    catching = catchAwaitingContact = false;
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
            if (!yielded) ImpactMotion?.Apply();
            if (!initialized || yielded) return;
            for (int i = 0; i < bones.Length; i++) { basePositions[i] = bones[i].localPosition; baseRotations[i] = bones[i].localRotation; }
            applied = true;
            pelvis.position += frame.TransformVector(gaitOffset);
            ConstrainContacts();
        }

        // Transition source/target poses already include the weight shift. Re-close
        // their contacts afterwards without applying that shift a second time.
        public void ConstrainContacts()
        {
            if (!initialized || yielded) return;
            // The animation owns the weight shift; only lower a hip if a planted leg would lock straight.
            float lower = 0f;
            for (int side = 0; side < 2; side++)
            {
                Vector3 delta = bones[1 + side * 3].position - feet[side];
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
                int i = 1 + side * 3;
                LimbTwoBoneIk.Solve(bones[i], bones[i + 1], bones[i + 2], feet[side], rotations[side],
                    bones[i].position + frame.forward * .7f + (side == 0 ? -frame.right : frame.right) * .08f,
                    1f, .999f, true);
            }
        }

        public void Restore()
        {
            if (applied)
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i] != null) { bones[i].localPosition = basePositions[i]; bones[i].localRotation = baseRotations[i]; }
            applied = false;
            ImpactMotion?.Restore();
        }
        public void Forget()
        {
            applied = false; catching = catchAwaitingContact = hasPresentedContacts = false;
            ImpactMotion?.CancelRecoveryStep(); ImpactMotion?.Forget();
        }
        private static float Smooth(float value) => value * value * (3f - 2f * value);
    }
}
