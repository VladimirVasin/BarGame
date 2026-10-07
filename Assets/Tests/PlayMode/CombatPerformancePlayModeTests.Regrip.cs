using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatPerformancePlayModeTests
    {
        private string lastSourceShoveDiagnostics;

        [UnityTest]
        public IEnumerator Range_BackhandSupportSearchHasBoundedWorkAndKeepsAdmittedContactsSafe()
        {
            // Fresh duel a46 had 0.2-0.5 second backhand stalls from nested
            // shoulder/contact searches. Count work, not Editor frame timing;
            // unreachable authored contacts may release the supporting hand.
            long evaluatedContacts = 0, evaluatedShoulders = 0;
            int admittedContacts = 0;
            int supportProbes = 0, exactRejects = 0, correctedFeasible = 0, fullRejects = 0;
            string firstCorrection = null, firstFullReject = null;
            foreach (bool hero in new[] { true, false })
            foreach (float power in new[] { -1f, 0f, .5f, 1f })
            {
                PlacePair(4f);
                for (int frame = 0; frame < 3; frame++)
                { root.Tick(TickSeconds); yield return null; }
                CombatActor actor = hero ? root.Hero : root.Opponent;
                CombatSupportGrip grip = actor.SupportGrip;
                var constraint = (CombatWeaponConstraint)typeof(CombatActor).GetField("weaponConstraint",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(actor);
                Transform shoulder = (Transform)typeof(CombatSupportGrip).GetField("upper",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(grip);
                Transform elbow = (Transform)typeof(CombatSupportGrip).GetField("forearm",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(grip);
                Transform wrist = (Transform)typeof(CombatSupportGrip).GetField("hand",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(grip);
                NpcHandPose hands = wrist.GetComponentInParent<NpcHandPose>();
                Transform[] probeBones = ContinuationArmBones(actor);
                Transform mountedWeapon = actor.Weapon.transform, weaponMount = mountedWeapon.parent;
                Vector3 mountPosition = mountedWeapon.localPosition;
                Quaternion mountRotation = mountedWeapon.localRotation;
                float probedClock = float.NegativeInfinity;
                string label = $"{(hero ? "hero" : "opponent")}/Backhand/power={power}";
                actor.State.ObserveLateralCue(1);
                if (power < 0f) Assert.That(actor.TryAttack(), Is.True, label);
                else
                {
                    Assert.That(actor.RequestCharge(), Is.True, label);
                    int chargeSamples = 0;
                    while (actor.State.Charge01 + .00001f < power && chargeSamples++ < 120)
                    {
                        root.Tick(CombatTestRoot.SimulationStep);
                        CheckPresentation();
                    }
                    Assert.That(actor.State.Charge01, Is.EqualTo(power).Within(.015f), label);
                    Assert.That(actor.ReleaseCharge(), Is.True, label);
                }
                Assert.That(actor.State.Swing, Is.EqualTo(MeleeSwing.Backhand), label);
                int admittedBefore = admittedContacts;
                int samples = 0;
                while (actor.State.IsAttacking && samples++ < 360)
                {
                    root.Tick(CombatTestRoot.SimulationStep);
                    CheckPresentation();
                    // Re-enter the same pose without advancing its arm clock.
                    // Each solve gets a fresh budget and validates live geometry.
                    PresentBounded();
                    PresentBounded();
                }
                Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Ready), label);
                Assert.That(actor.ReceivedImpactCount, Is.Zero, label);
                Assert.That(admittedContacts, Is.GreaterThan(admittedBefore), label + ": some contact must be physically admitted");

                void PresentBounded()
                {
                    long shouldersBefore = constraint.CandidateChecks;
                    long contactsBefore = grip.SupportCandidateEvaluations;
                    actor.Present();
                    long shoulders = constraint.CandidateChecks - shouldersBefore;
                    long contacts = grip.SupportCandidateEvaluations - contactsBefore;
                    Assert.That(shoulders, Is.InRange(0L, 12L), label + ": bounded shoulder choices per presentation");
                    Assert.That(contacts, Is.InRange(0L, 96L), label + ": one bounded final contact solve per presentation");
                    evaluatedShoulders += shoulders; evaluatedContacts += contacts;
                    CheckPresentation();
                }

                void CheckPresentation()
                {
                    string pose = $"{label}: phase={actor.State.Phase}, progress={actor.State.AttackProgress:F5}, " +
                        $"grip={grip.State}, reject={grip.LastPoseRejection}, evaluations={grip.LastContactSolveEvaluations}";
                    Assert.That(grip.LastContactSolveEvaluations, Is.InRange(0, 96), pose);
                    if (grip.RequiresWeaponSupportConstraint && !actor.JournalPoseClock.Equals(probedClock))
                    {
                        // Probe each actually sampled pose once, outside the
                        // measured Present budget. No synthetic joint or weapon
                        // rotations substitute for the imported, live geometry.
                        probedClock = actor.JournalPoseClock;
                        var rotations = new Quaternion[probeBones.Length];
                        var positions = new Vector3[probeBones.Length];
                        for (int joint = 0; joint < probeBones.Length; joint++)
                        { rotations[joint] = probeBones[joint].localRotation; positions[joint] = probeBones[joint].position; }
                        Vector3 weaponPosition = mountedWeapon.position;
                        Quaternion weaponRotation = mountedWeapon.rotation;
                        bool conservative = grip.IsWeaponSupportPoseGeometricallyFeasible(out bool exact);
                        bool full = grip.IsWeaponSupportPoseFeasible(out string reason);
                        supportProbes++;
                        for (int joint = 0; joint < probeBones.Length; joint++)
                        {
                            Assert.That(Quaternion.Angle(rotations[joint], probeBones[joint].localRotation), Is.LessThan(.001f), pose);
                            Assert.That(Vector3.Distance(positions[joint], probeBones[joint].position), Is.LessThan(.000001f), pose);
                        }
                        Assert.That(mountedWeapon.parent, Is.SameAs(weaponMount), pose);
                        Assert.That(Vector3.Distance(mountPosition, mountedWeapon.localPosition), Is.LessThan(.000001f), pose);
                        Assert.That(Quaternion.Angle(mountRotation, mountedWeapon.localRotation), Is.LessThan(.001f), pose);
                        Assert.That(Vector3.Distance(weaponPosition, mountedWeapon.position), Is.LessThan(.000001f), pose);
                        Assert.That(Quaternion.Angle(weaponRotation, mountedWeapon.rotation), Is.LessThan(.001f), pose);
                        if (!exact) exactRejects++;
                        if (full)
                        {
                            Assert.That(conservative, Is.True, "The shoulder filter cannot reject a feasible bounded contact. " + pose);
                            if (!exact) { correctedFeasible++; firstCorrection ??= pose; }
                        }
                        else { fullRejects++; firstFullReject ??= pose + ", fullReason=" + reason; }
                    }
                    if (!grip.IsSupportingWeapon) return;
                    admittedContacts++;
                    Assert.That(grip.JournalContactCurrent, Is.True, pose);
                    Assert.That(grip.JournalContactError, Is.LessThanOrEqualTo(.025f), pose);
                    Assert.That(grip.JournalContactAngle, Is.LessThanOrEqualTo(12f), pose);
                    Assert.That(grip.JournalWristSafe, Is.True, pose);
                    Assert.That(grip.LiveArmAngles.x, Is.LessThanOrEqualTo(25.1f), pose);
                    Assert.That(grip.LiveArmAngles.y, Is.LessThanOrEqualTo(55.1f), pose);
                    Vector3 offset = hands.CylinderCentre(true) - grip.Target;
                    Vector3 shaft = actor.Weapon.transform.up;
                    Assert.That(Vector3.ProjectOnPlane(offset, shaft).magnitude, Is.LessThanOrEqualTo(.001f), pose);
                    Assert.That(Mathf.Abs(Vector3.Dot(offset, shaft)), Is.LessThanOrEqualTo(.025f), pose);
                    Assert.That(constraint.SupportArmClearance.IsSupportPathClear(shoulder.position, elbow.position, wrist.position),
                        Is.True, pose + ": admitted supporting arm stays clear of the body");
                }
            }
            Assert.That(evaluatedShoulders, Is.GreaterThan(0L), "The test must exercise right-arm candidate selection.");
            Assert.That(evaluatedContacts, Is.GreaterThan(0L), "The test must exercise actual contact solving.");
            Assert.That(supportProbes, Is.GreaterThan(0), "Probe live imported supporting poses.");
            string result = $"Backhand support probes: poses={supportProbes}, exactRejects={exactRejects}, correctedFeasible={correctedFeasible}, " +
                $"fullRejects={fullRejects}; firstCorrection={firstCorrection ?? "none"}; firstFullReject={firstFullReject ?? "none"}";
            LogAssert.Expect(LogType.Log, result);
            Debug.Log(result);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Range_HitContinuationKeepsSupportSolveAndVisibleContactConsistent()
        {
            // Duel action 65 followed a real NPC forehand hit with a short
            // backhand while the hero staggered about a metre away. A miss is
            // still legal; a cheap contact filter cannot freeze a clear weapon
            // merely because the final solver needs a new bounded correction.
            PlacePair(1f);
            for (int tick = 0; tick < 3; tick++) root.Tick(CombatTestRoot.SimulationStep);
            CombatActor actor = root.Opponent, target = root.Hero;
            CombatSupportGrip grip = actor.SupportGrip;
            grip.CaptureContactSearchDiagnostics = true;
            var constraint = (CombatWeaponConstraint)typeof(CombatActor).GetField("weaponConstraint",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(actor);
            Transform[] bones = ContinuationArmBones(actor);
            Transform shoulder = bones[0], elbow = bones[1], wrist = bones[2];
            FieldInfo stepUpper = typeof(CombatSupportGrip).GetField("armStepUpper", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo stepElbow = typeof(CombatSupportGrip).GetField("armStepForearm", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo stepWrist = typeof(CombatSupportGrip).GetField("armStepHand", BindingFlags.Instance | BindingFlags.NonPublic);
            NpcHandPose hands = wrist.GetComponentInParent<NpcHandPose>();
            Transform strikeBase = CombatAssetProvider.FindAnchor(actor.Weapon, "StrikeBase");
            Transform strikeTip = CombatAssetProvider.FindAnchor(actor.Weapon, "StrikeTip");
            Transform[] mountedWeapons = { actor.Weapon.transform, target.Weapon.transform };
            Transform[] mounts = { mountedWeapons[0].parent, mountedWeapons[1].parent };
            Vector3[] mountPositions = { mountedWeapons[0].localPosition, mountedWeapons[1].localPosition };
            Quaternion[] mountRotations = { mountedWeapons[0].localRotation, mountedWeapons[1].localRotation };
            int impacts = 0, contactSequence = -1, exactRejects = 0, correctedFeasible = 0, fullRejects = 0;
            Vector3 contactBase = default, contactTip = default;
            bool continued = false, staggerAtContinuation = false, capturedCorrection = false;
            string firstCorrection = null, firstFullReject = null;
            void RememberContact(CombatImpact impact)
            {
                if (impact.Source != actor || impact.Kind != CombatImpactKind.Weapon) return;
                impacts++;
                contactSequence = impact.AttackSequence;
                contactBase = strikeBase.position;
                contactTip = strikeTip.position;
            }
            target.ImpactReceived += RememberContact;
            try
            {
                actor.State.ObserveLateralCue(-1);
                Assert.That(actor.TryAttack(), Is.True);
                Assert.That(actor.State.Swing, Is.EqualTo(MeleeSwing.Forehand));
                int firstSequence = actor.State.AttackSequence;
                Assert.That(actor.RequestAttack(), Is.True, "One real next press waits for the first contact window.");
                int observedImpacts = 0;
                for (int tick = 0; tick < 360; tick++)
                {
                    root.Tick(CombatTestRoot.SimulationStep);
                    AssertWeaponMounts();
                    string pose = $"tick={tick}, sequence={actor.State.AttackSequence}, phase={actor.State.Phase}, " +
                        $"progress={actor.State.AttackProgress:F5}, range={Vector3.Distance(actor.transform.position, target.transform.position):F3}, " +
                        $"grip={grip.State}, reject={grip.LastPoseRejection}, motionBlocked={constraint.MotionBlocked}, " +
                        $"worldBlocked={constraint.WorldBlocked}, shape={constraint.BlockingShape}, " +
                        $"tip={actor.transform.InverseTransformPoint(strikeTip.position):F3}, " +
                        $"holdingRoll={grip.LiveShoulderRoll:F2}, holdingElbow={grip.LiveSignedElbow:F2}";
                    if (actor.State.AttackSequence != firstSequence && !continued)
                    {
                        continued = true;
                        staggerAtContinuation = target.State.Phase == MeleePhase.Stagger;
                        Assert.That(impacts, Is.EqualTo(1), "The continuation follows a real forehand contact. " + pose);
                        Assert.That(actor.State.AttackSequence, Is.EqualTo(firstSequence + 1), pose);
                        Assert.That(actor.State.IsContinuation, Is.True, pose);
                        Assert.That(actor.State.Swing, Is.EqualTo(MeleeSwing.Backhand), pose);
                        Assert.That(Vector3.Distance(actor.transform.position, target.transform.position), Is.InRange(.95f, 1.2f),
                            "The actual achieved gap remains comparable to the manual episode. " + pose);
                        yield return CapturePresented("continuation-start");
                    }
                    if (impacts != observedImpacts)
                    {
                        Assert.That(contactSequence, Is.EqualTo(actor.State.AttackSequence), pose);
                        Assert.That(Vector3.Distance(strikeBase.position, contactBase), Is.LessThan(.012f), "Accepted source contact base. " + pose);
                        Assert.That(Vector3.Distance(strikeTip.position, contactTip), Is.LessThan(.012f), "Accepted source contact tip. " + pose);
                        yield return CapturePresented(impacts == 1 ? "forehand-contact" : "backhand-contact");
                        observedImpacts = impacts;
                    }

                    long shouldersBefore = constraint.CandidateChecks, contactsBefore = grip.SupportCandidateEvaluations;
                    Vector3 visibleBase = strikeBase.position, visibleTip = strikeTip.position;
                    actor.Present();
                    Assert.That(constraint.CandidateChecks - shouldersBefore, Is.InRange(0L, 12L), "Bounded right-arm work. " + pose);
                    Assert.That(grip.SupportCandidateEvaluations - contactsBefore, Is.InRange(0L, 96L), "One bounded final support solve. " + pose);
                    Assert.That(Vector3.Distance(strikeBase.position, visibleBase), Is.LessThan(.012f), "Repeated presentation keeps the accepted base. " + pose);
                    Assert.That(Vector3.Distance(strikeTip.position, visibleTip), Is.LessThan(.012f), "Repeated presentation keeps the accepted tip. " + pose);
                    if (grip.IsSupportingWeapon)
                    {
                        Assert.That(grip.JournalContactError, Is.LessThanOrEqualTo(.025f), pose);
                        Assert.That(grip.JournalContactAngle, Is.LessThanOrEqualTo(12f), pose);
                        Assert.That(grip.JournalWristSafe, Is.True, pose);
                        Assert.That(grip.LiveArmAngles.x, Is.LessThanOrEqualTo(25.1f), pose);
                        Assert.That(grip.LiveArmAngles.y, Is.LessThanOrEqualTo(55.1f), pose);
                        Vector3 offset = hands.CylinderCentre(true) - grip.Target;
                        Assert.That(Vector3.ProjectOnPlane(offset, actor.Weapon.transform.up).magnitude, Is.LessThanOrEqualTo(.001f), pose);
                        Assert.That(Mathf.Abs(Vector3.Dot(offset, actor.Weapon.transform.up)), Is.LessThanOrEqualTo(.025f), pose);
                        Assert.That(constraint.SupportArmClearance.IsSupportPathClear(shoulder.position, elbow.position, wrist.position),
                            Is.True, "Admitted holding contact keeps actual body clearance. " + pose);
                        if (actor.State.IsContinuation && !constraint.MotionBlocked)
                        {
                            Vector3 axis = wrist.position - shoulder.position;
                            Vector3 pole = Vector3.ProjectOnPlane(elbow.position - shoulder.position, axis);
                            Quaternion oldUpper = shoulder.localRotation, oldElbow = elbow.localRotation, oldWrist = wrist.localRotation;
                            Vector3 priorAxis, priorPole;
                            try
                            {
                                // Carry the exact cached local start through the
                                // current torso, as the holding solver does. This
                                // checks branch speed without travelling limits.
                                shoulder.localRotation = (Quaternion)stepUpper.GetValue(grip);
                                elbow.localRotation = (Quaternion)stepElbow.GetValue(grip);
                                wrist.localRotation = (Quaternion)stepWrist.GetValue(grip);
                                priorAxis = wrist.position - shoulder.position;
                                priorPole = Vector3.ProjectOnPlane(elbow.position - shoulder.position, priorAxis);
                            }
                            finally
                            {
                                shoulder.localRotation = oldUpper;
                                elbow.localRotation = oldElbow;
                                wrist.localRotation = oldWrist;
                            }
                            Vector3 carried = Quaternion.FromToRotation(priorAxis, axis) * priorPole;
                            Assert.That(Mathf.Abs(Vector3.SignedAngle(carried, pole, axis)),
                                Is.LessThanOrEqualTo(600f * CombatTestRoot.SimulationStep + .25f), "Continuous holding elbow branch. " + pose);
                        }
                    }
                    if (grip.RequiresWeaponSupportConstraint)
                    {
                        // Diagnostic probes are outside the measured presentation.
                        // They restore the live joints, and do not add another
                        // contact solve to the production pose composition.
                        Quaternion oldUpper = shoulder.localRotation, oldElbow = elbow.localRotation, oldWrist = wrist.localRotation;
                        Quaternion[] priorRight = { bones[3].localRotation, bones[4].localRotation, bones[5].localRotation };
                        Vector3 priorWeapon = actor.Weapon.transform.position;
                        Quaternion priorWeaponRotation = actor.Weapon.transform.rotation;
                        bool conservative = grip.IsWeaponSupportPoseGeometricallyFeasible(out bool exact);
                        bool full = grip.IsWeaponSupportPoseFeasible(out string reason);
                        Assert.That(Quaternion.Angle(oldUpper, shoulder.localRotation), Is.LessThan(.001f), pose);
                        Assert.That(Quaternion.Angle(oldElbow, elbow.localRotation), Is.LessThan(.001f), pose);
                        Assert.That(Quaternion.Angle(oldWrist, wrist.localRotation), Is.LessThan(.001f), pose);
                        for (int joint = 0; joint < priorRight.Length; joint++)
                            Assert.That(Quaternion.Angle(priorRight[joint], bones[joint + 3].localRotation), Is.LessThan(.001f), pose);
                        Assert.That(Vector3.Distance(priorWeapon, actor.Weapon.transform.position), Is.LessThan(.000001f), pose);
                        Assert.That(Quaternion.Angle(priorWeaponRotation, actor.Weapon.transform.rotation), Is.LessThan(.001f), pose);
                        AssertWeaponMounts();
                        if (!exact) exactRejects++;
                        if (full)
                        {
                            Assert.That(conservative, Is.True, "A feasible bounded contact cannot be rejected by the shoulder filter. " + pose);
                            if (!exact)
                            {
                                correctedFeasible++;
                                firstCorrection ??= pose;
                                if (!capturedCorrection)
                                {
                                    capturedCorrection = true;
                                    yield return CapturePresented("new-contact-correction");
                                }
                            }
                        }
                        else { fullRejects++; firstFullReject ??= pose + ", fullReason=" + reason; }
                    }
                    if (continued && actor.State.Phase == MeleePhase.Ready) break;
                }
                Assert.That(continued && staggerAtContinuation, Is.True, "Exercise the hit-to-backhand handoff against the real staggered hero.");
                Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Ready));
                Assert.That(actor.State.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Hit).Or.EqualTo(MeleeAttackOutcome.Miss),
                    "The actual blade geometry decides contact; the support fix grants no synthetic hit.");
                Assert.That(impacts, Is.InRange(1, 2), "At most one actual contact per swing.");
                string result = $"Support continuation: exactRejects={exactRejects}, correctedFeasible={correctedFeasible}, fullRejects={fullRejects}, " +
                    $"backhand={actor.State.AttackOutcome}; firstCorrection={firstCorrection ?? "none"}; firstFullReject={firstFullReject ?? "none"}";
                LogAssert.Expect(LogType.Log, result);
                Debug.Log(result);
                yield return CapturePresented("final-return");
                LogAssert.NoUnexpectedReceived();
            }
            finally { target.ImpactReceived -= RememberContact; grip.CaptureContactSearchDiagnostics = false; }

            void AssertWeaponMounts()
            {
                for (int weaponIndex = 0; weaponIndex < mountedWeapons.Length; weaponIndex++)
                {
                    Transform mounted = mountedWeapons[weaponIndex];
                    Assert.That(mounted.parent, Is.SameAs(mounts[weaponIndex]), "The weapon stays on its original right-hand mount.");
                    Assert.That(Vector3.Distance(mounted.localPosition, mountPositions[weaponIndex]), Is.LessThan(.000001f));
                    Assert.That(Quaternion.Angle(mounted.localRotation, mountRotations[weaponIndex]), Is.LessThan(.001f));
                }
            }

            IEnumerator CapturePresented(string name)
            {
                // Unity can cache skinning once per rendered frame. Let that
                // frame complete before another camera.Render photographs a
                // new manually advanced pose; the duel clock remains frozen.
                yield return null;
                actor.Present();
                target.Present();
                AssertWeaponMounts();
                CaptureDuelFrame("balance/support-continuation/opponent", name);
            }
        }

        [UnityTest]
        public IEnumerator Range_SupportedGripKeepsWristLimitsThroughImportedSwings()
        {
            // Manual duel f524 lost contact near .42 of the forehand and .12
            // of the backhand, on both rigs, without an incoming impact. Read
            // every 120 Hz pose, including times between imported FBX keys.
            bool heroCaptured = false, opponentCaptured = false;
            foreach (bool hero in new[] { true, false })
            foreach (MeleeSwing swing in new[] { MeleeSwing.Forehand, MeleeSwing.Backhand })
            foreach (float power in new[] { -1f, 0f, .5f, 1f })
            {
                PlacePair(4f);
                for (int frame = 0; frame < 3; frame++)
                { root.Tick(TickSeconds); yield return null; }
                CombatActor actor = hero ? root.Hero : root.Opponent;
                actor.SupportGrip.CaptureContactSearchDiagnostics = true;
                var weaponConstraint = (CombatWeaponConstraint)typeof(CombatActor).GetField("weaponConstraint",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(actor);
                Transform leftElbow = (Transform)typeof(CombatSupportGrip).GetField("forearm",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(actor.SupportGrip);
                Transform leftShoulder = (Transform)typeof(CombatSupportGrip).GetField("upper",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(actor.SupportGrip);
                Transform leftWrist = (Transform)typeof(CombatSupportGrip).GetField("hand",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(actor.SupportGrip);
                NpcHandPose hands = leftWrist.GetComponentInParent<NpcHandPose>();
                Vector3 previousElbow = actor.transform.InverseTransformPoint(leftElbow.position);
                Vector3 previousShoulder = actor.transform.InverseTransformPoint(leftShoulder.position);
                Vector3 previousWrist = actor.transform.InverseTransformPoint(leftWrist.position);
                float previousContactAngle = actor.SupportGrip.JournalContactAngle;
                float previousStation = Vector3.Dot(hands.CylinderCentre(true) - actor.SupportGrip.Target, actor.Weapon.transform.up);
                string label = $"{(hero ? "hero" : "opponent")}/{swing}/power={power}";
                actor.State.ObserveLateralCue(swing == MeleeSwing.Backhand ? 1 : -1);
                if (power < 0f) Assert.That(actor.TryAttack(), Is.True, label);
                else
                {
                    Assert.That(actor.RequestCharge(), Is.True, label);
                    int chargeSamples = 0;
                    while (actor.State.Charge01 + .00001f < power && chargeSamples++ < 120)
                    {
                        root.Tick(CombatTestRoot.SimulationStep);
                        CheckGrip();
                    }
                    Assert.That(actor.State.Charge01, Is.EqualTo(power).Within(.015f), label);
                    Assert.That(actor.ReleaseCharge(), Is.True, label);
                }
                Assert.That(actor.State.Swing, Is.EqualTo(swing), label);
                int samples = 0;
                while (actor.State.IsAttacking && samples < 360)
                {
                    root.Tick(CombatTestRoot.SimulationStep);
                    CheckGrip();
                    if (power < 0f && ((hero && swing == MeleeSwing.Forehand && !heroCaptured && actor.State.AttackProgress >= .45f) ||
                        (!hero && swing == MeleeSwing.Backhand && !opponentCaptured && actor.State.AttackProgress >= .12f)))
                    {
                        yield return null;
                        CheckGrip(afterGraph: true);
                        CaptureWrist(hero ? "wrist-boundary-hero-forehand-045" : "wrist-boundary-npc-backhand-012");
                        if (hero) heroCaptured = true;
                        else opponentCaptured = true;
                    }
                    // Repeated LateUpdate presentation must use the same
                    // accepted branch without advancing the arm clock.
                    if (++samples % 24 == 0)
                    { yield return null; CheckGrip(afterGraph: true); }
                }
                Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Ready), label);
                Assert.That(actor.ReceivedImpactCount, Is.Zero, label);

                void CheckGrip(bool afterGraph = false)
                {
                    // yield null resumes after graph sampling but before this
                    // frame's LateUpdate. Inspect the same final composition as
                    // rendering; the unmodified imported pose is not the live IK.
                    // Immediate post-Tick checks stay untouched so an unsafe
                    // runtime presentation cannot be hidden by another solve.
                    if (afterGraph && actor.IsHero)
                        ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
                    CombatSupportGrip grip = actor.SupportGrip;
                    string pose = $"{label}: phase={actor.State.Phase}, progress={actor.State.AttackProgress:F5}, " +
                        $"reject={grip.LastPoseRejection}, wrist={grip.LiveArmAngles}, contactAngle={grip.JournalContactAngle}, previousContactAngle={previousContactAngle}, weaponBlocked={actor.WeaponClearanceBlocked}, " +
                        $"motionBlocked={weaponConstraint.MotionBlocked}, shape={actor.WeaponBlockingShape}, search={grip.LastContactSearchDiagnostics}";
                    Assert.That(grip.IsSupportingWeapon, Is.True, pose);
                    Assert.That(grip.JournalContactError, Is.LessThanOrEqualTo(.025f), pose);
                    Assert.That(grip.JournalContactAngle, Is.LessThanOrEqualTo(12f), pose);
                    Assert.That(grip.JournalWristSafe, Is.True, pose);
                    Assert.That(grip.LiveArmAngles.x, Is.LessThanOrEqualTo(25.1f), pose);
                    Assert.That(grip.LiveArmAngles.y, Is.LessThanOrEqualTo(55.1f), pose);
                    Vector3 cylinderOffset = hands.CylinderCentre(true) - grip.Target;
                    Vector3 shaftAxis = actor.Weapon.transform.up;
                    float currentStation = Vector3.Dot(cylinderOffset, shaftAxis);
                    Assert.That(Vector3.ProjectOnPlane(cylinderOffset, shaftAxis).magnitude, Is.LessThanOrEqualTo(.001f), pose);
                    Assert.That(Mathf.Abs(currentStation), Is.LessThanOrEqualTo(.025f), pose);
                    if (!afterGraph)
                    {
                        Vector3 currentElbow = actor.transform.InverseTransformPoint(leftElbow.position);
                        Vector3 currentShoulder = actor.transform.InverseTransformPoint(leftShoulder.position);
                        Vector3 currentWrist = actor.transform.InverseTransformPoint(leftWrist.position);
                        // The legacy nominal solve follows the authored hint.
                        // The new bounded contact correction must preserve
                        // the previous branch on entry, travel and return.
                        if (grip.JournalContactAngle > .1f || previousContactAngle > .1f ||
                            Mathf.Abs(currentStation) > .0001f || Mathf.Abs(previousStation) > .0001f)
                        {
                            Vector3 oldAxis = previousWrist - previousShoulder, newAxis = currentWrist - currentShoulder;
                            Vector3 oldPole = Vector3.ProjectOnPlane(previousElbow - previousShoulder, oldAxis);
                            Vector3 carriedPole = Quaternion.FromToRotation(oldAxis, newAxis) * oldPole;
                            Vector3 currentPole = Vector3.ProjectOnPlane(currentElbow - currentShoulder, newAxis);
                            float branchTurn = Mathf.Abs(Vector3.SignedAngle(carriedPole, currentPole, newAxis));
                            Assert.That(branchTurn, Is.LessThanOrEqualTo(600f * CombatTestRoot.SimulationStep + .25f),
                                pose + ": continuous corrected elbow branch");
                        }
                        previousElbow = currentElbow;
                        previousShoulder = currentShoulder;
                        previousWrist = currentWrist;
                        previousContactAngle = grip.JournalContactAngle;
                        previousStation = currentStation;
                    }
                }

                void CaptureWrist(string name)
                {
                    Camera camera = Camera.main;
                    Assert.That(camera, Is.Not.Null);
                    Vector3 before = camera.transform.position;
                    Quaternion rotation = camera.transform.rotation;
                    try
                    {
                        camera.transform.position = actor.transform.position + actor.transform.forward * 1.8f -
                            actor.transform.right * 1.3f + Vector3.up * 1.6f;
                        camera.transform.LookAt(actor.transform.position + Vector3.up * 1.15f + actor.transform.forward * .15f);
                        AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.CombatTest, name);
                    }
                    finally { camera.transform.SetPositionAndRotation(before, rotation); }
                }
            }
            Assert.That(heroCaptured && opponentCaptured, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Range_OneHandAttacksStartWithoutWaitingForSupport()
        {
            var input = new InputTestFixture();
            Mouse mouse = null;
            MethodInfo consumeInput = typeof(CombatTestRoot).GetMethod("UpdateCombatInput",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(consumeInput, Is.Not.Null);
            try
            {
                input.Setup();
                mouse = InputSystem.AddDevice<Mouse>();
                RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height);
                Vector2 pointer = canvas.LogicalToScreen(new Vector2(320f, 180f));
                pointer.y = Screen.height - pointer.y;
                input.Set(mouse.position, pointer);
                for (int movement = 0; movement < 2; movement++)
                {
                    bool moving = movement != 0;
                    string label = moving ? "moving one-hand" : "stationary one-hand";
                    PlacePair(4f);
                    for (int frame = 0; frame < 6; frame++)
                        yield return AdvanceContinuationFrame(root.Hero, moving, false, label);
                    foreach (CombatActor fighter in new[] { root.Hero, root.Opponent })
                    {
                        ReleaseSupportForAttack(fighter, label);
                        fighter.SetBlock(true);
                        Assert.That(fighter.State.IsBlocking, Is.False, "The two-hand guard still requires contact.");
                        Assert.That(fighter.GuardRequested, Is.True);
                        Assert.That(fighter.GuardReady, Is.False);
                        Assert.That(fighter.GuardSupportRejection, Is.EqualTo("two_hand_support"));
                        fighter.SetBlock(false);
                        int sequence = fighter.State.AttackSequence;
                        Assert.That(moving ? fighter.RequestAttack() : fighter.TryAttack(), Is.True, label);
                        Assert.That(fighter.State.Phase, Is.EqualTo(MeleePhase.Windup), label + ": start in this call");
                        Assert.That(fighter.State.AttackSequence, Is.EqualTo(sequence + 1), label);
                        Assert.That(fighter.State.HasBufferedAttack, Is.False, label + ": no waiting for the left hand");
                    }

                    PlacePair(4f);
                    CombatActor actor = root.Hero;
                    for (int frame = 0; frame < 6; frame++)
                        yield return AdvanceContinuationFrame(actor, moving, false, label);
                    ReleaseSupportForAttack(actor, label);
                    int beforePress = actor.State.AttackSequence;
                    float stamina = actor.State.Stamina;
                    // Drive the real mouse-to-command bridge without advancing
                    // simulation: same-call assertions cannot hide a regrip wait.
                    input.Press(mouse.leftButton);
                    ConsumeCombatMouseInput(consumeInput, true);
                    Assert.That(actor.State.IsCharging, Is.True, label + ": a mouse press starts with one hand");
                    Assert.That(actor.State.AttackSequence, Is.EqualTo(beforePress + 1), label);
                    Assert.That(actor.State.HasBufferedCharge, Is.False, label);
                    for (int frame = 0; frame < 6; frame++)
                        yield return AdvanceContinuationFrame(actor, moving, false, label);
                    float power = actor.State.Charge01;
                    Assert.That(power, Is.GreaterThan(0f));
                    Assert.That(actor.State.Stamina, Is.EqualTo(stamina - actor.State.Settings.AttackCost -
                        power * actor.State.Settings.ChargeStaminaCost).Within(.001f), label + ": pay charge only once");
                    ReleaseSupportForAttack(actor, label);
                    float beforeRelease = actor.State.Stamina;
                    input.Release(mouse.leftButton);
                    ConsumeCombatMouseInput(consumeInput, false);
                    Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Windup), label + ": release must not cancel a short click");
                    Assert.That(actor.State.AttackSequence, Is.EqualTo(beforePress + 1), label + ": release retains its action");
                    Assert.That(actor.State.AttackPower, Is.EqualTo(power).Within(.0001f), label);
                    Assert.That(actor.State.Stamina, Is.EqualTo(beforeRelease), label + ": no second charge cost");

                    for (int frame = 0; frame < 90 && actor.State.Phase != MeleePhase.Recovery; frame++)
                        yield return AdvanceContinuationFrame(actor, moving, false, label);
                    Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Recovery), label);
                    Assert.That(actor.State.RecoveryRemaining, Is.GreaterThan(actor.State.Settings.AttackBufferSeconds), label);
                    ReleaseSupportForAttack(actor, label);
                    input.Press(mouse.leftButton);
                    ConsumeCombatMouseInput(consumeInput, true);
                    Assert.That(actor.State.IsCharging, Is.True, label + ": cut the return in the input call");
                    Assert.That(actor.State.IsContinuation, Is.True, label);
                    Assert.That(actor.State.AttackSequence, Is.EqualTo(beforePress + 2), label);
                    Assert.That(actor.State.HasBufferedCharge, Is.False, label);
                    ReleaseSupportForAttack(actor, label);
                    input.Release(mouse.leftButton);
                    ConsumeCombatMouseInput(consumeInput, false);
                    Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Windup), label);
                    Assert.That(actor.State.AttackPower, Is.Zero, label + ": the immediate tap stays light");
                    for (int frame = 0; frame < 6; frame++)
                        yield return AdvanceContinuationFrame(actor, moving, true, label);
                    Assert.That(actor.State.AttackSequence, Is.EqualTo(beforePress + 2), label + ": no duplicate click");
                    CaptureDuelFrame("balance/one-hand", moving ? "moving-continuation" : "stationary-continuation");

                    PlacePair(4f);
                    ReleaseSupportForAttack(actor, label);
                    input.Press(mouse.leftButton);
                    ConsumeCombatMouseInput(consumeInput, true);
                    Assert.That(actor.State.IsCharging, Is.True, label);
                    Assert.That(actor.State.ReceiveShove(), Is.True);
                    input.Release(mouse.leftButton);
                    ConsumeCombatMouseInput(consumeInput, false);
                    Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Stagger), "A real interruption still cancels the held action.");
                    Assert.That(actor.State.HasBufferedAttack, Is.False, "An interrupted click cannot fire later.");
                }

                PlacePair(1.1f);
                for (int frame = 0; frame < 6; frame++)
                { root.Tick(TickSeconds); yield return null; }
                float targetHealth = root.Opponent.State.Health;
                int targetImpacts = root.Opponent.ReceivedImpactCount;
                ReleaseSupportForAttack(root.Hero, "one-hand contact");
                Assert.That(root.Hero.RequestAttack(), Is.True);
                for (int frame = 0; frame < 90 && root.Opponent.ReceivedImpactCount == targetImpacts; frame++)
                {
                    ReleaseSupportForAttack(root.Hero, "one-hand contact");
                    root.Tick(TickSeconds);
                    yield return null;
                    Assert.That(root.Hero.SupportGrip.IsSupportingWeapon, Is.False,
                        "Keep the left hand released through the actual weapon-contact window.");
                }
                Assert.That(root.Opponent.ReceivedImpactCount, Is.EqualTo(targetImpacts + 1),
                    "A one-hand attack must resolve a real weapon hit, not only enter Windup.");
                Assert.That(root.Opponent.State.Health, Is.LessThan(targetHealth));
                CaptureDuelFrame("balance/one-hand", "weapon-contact");

                PlacePair(4f);
                root.Hero.ImpactMotion.BeginRecoveryStep(0, root.Hero.transform.position + Vector3.forward * .2f, .2f);
                Assert.That(root.Hero.ImpactMotion.RecoveryInProgress, Is.True);
                Assert.That(root.Hero.TryAttack() || root.Hero.RequestAttack(), Is.False,
                    "A missing support hand is allowed; an unfinished physical balance step still owns the body.");
                Assert.That(root.Hero.RequestCharge(), Is.True, "A short charge intent may wait for the returning support.");
                Assert.That(root.Hero.State.HasBufferedCharge, Is.True);
                Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Ready), "The intent cannot begin an attack during the step.");
                PlacePair(4f);
                root.Hero.State.BeginKnockdown();
                Assert.That(root.Hero.TryAttack() || root.Hero.RequestAttack() || root.Hero.RequestCharge(), Is.False,
                    "One-hand attacks cannot bypass knockdown.");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                input.TearDown();
            }
        }

        private void ConsumeCombatMouseInput(MethodInfo consumeInput, bool held)
        {
            // InputTestFixture always queues events in a UnityTest. Process the
            // device event explicitly so the bridge sees it without a duel tick.
            InputSystem.Update();
            Assert.That(GameInput.IsHeld(GameInputAction.MeleeAttack, GameInputContext.Gameplay), Is.EqualTo(held));
            if (held) Assert.That(GameInput.WasPressed(GameInputAction.MeleeAttack, GameInputContext.Gameplay), Is.True);
            Assert.That((bool)consumeInput.Invoke(root, null), Is.True);
        }

        private static void ReleaseSupportForAttack(CombatActor actor, string label)
        {
            // Use the same release path as a rejected continuation pose, rather
            // than modifying the arm state or deleting its physical constraint.
            actor.SupportGrip.RejectObstructedPose("contact_world_path");
            Assert.That(actor.SupportGrip.IsSupportingWeapon, Is.False, label + ": the supporting hand is actually open");
            Assert.That(actor.IsKnockedDown || actor.ImpactMotion.RecoveryInProgress, Is.False, label);
        }

        [UnityTest]
        public IEnumerator Range_AttackContinuationCancelsOnlyReturnTail()
        {
            for (int movement = 0; movement < 2; movement++)
                for (int scenario = 0; scenario < 3; scenario++)
                {
                    bool moving = movement != 0, held = scenario == 2;
                    string label = (moving ? "moving" : "stationary") + "/" +
                        (scenario == 0 ? "queued-tap" : held ? "held-charge" : "tail-tap");
                    string capture = "balance/continuation/" + label;
                    PlacePair(4f);
                    CombatActor actor = root.Hero;
                    if (scenario != 0)
                    {
                        // A persistent target bearing can select the SAME side
                        // twice; a bridge must not assume alternating clips.
                        // Keep that bearing: automatic facing would turn it
                        // back into the neutral cue before the next press.
                        root.Player.Motor.ClearMovementTarget(root);
                        root.Opponent.ResetActor(new Vector3(-2f, PlayerFactory.GroundedRootOffset, 4f), Vector3.back);
                        Physics.SyncTransforms();
                    }
                    for (int frame = 0; frame < 6; frame++)
                        yield return AdvanceContinuationFrame(actor, moving, false, label);
                    Assert.That(actor.HasTwoHandSupport, Is.True, label + ": initial physical grip");
                    Assert.That(actor.RequestCharge(), Is.True, label);
                    for (int frame = 0; frame < 6; frame++)
                        yield return AdvanceContinuationFrame(actor, moving, false, label);
                    Assert.That(actor.ReleaseCharge(), Is.True, label + ": first short held press");
                    int firstSequence = actor.State.AttackSequence;
                    MeleeSwing firstSide = actor.State.Swing;
                    float firstDuration = actor.State.CurrentAttackDurationSeconds;

                    if (scenario == 1)
                    {
                        for (int frame = 0; frame < 90 &&
                            !(actor.State.Phase == MeleePhase.Recovery && actor.State.PhaseProgress >= .2f); frame++)
                            yield return AdvanceContinuationFrame(actor, moving, false, label);
                        Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Recovery), label + ": visible return has begun");
                        Assert.That(actor.State.RecoveryRemaining, Is.GreaterThan(actor.State.Settings.AttackBufferSeconds),
                            label + ": this click is earlier than the old late buffer window");
                    }
                    else
                    {
                        for (int frame = 0; frame < 8; frame++)
                            yield return AdvanceContinuationFrame(actor, moving, false, label);
                        Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Windup), label);
                    }
                    CaptureDuelFrame(capture, "00-before-click");
                    float oldElapsed = actor.State.AttackElapsed;
                    Transform[] clickBones = ContinuationArmBones(actor);
                    var clickRotations = new Quaternion[clickBones.Length];
                    for (int i = 0; i < clickBones.Length; i++) clickRotations[i] = clickBones[i].localRotation;
                    Vector3 clickWeaponPosition = actor.transform.InverseTransformPoint(actor.Weapon.transform.position);
                    Quaternion clickWeaponRotation = Quaternion.Inverse(actor.transform.rotation) * actor.Weapon.transform.rotation;
                    Assert.That(actor.RequestCharge(), Is.True, label + ": keep one next press");
                    if (!held) Assert.That(actor.ReleaseCharge(), Is.True, label + ": keep its release too");
                    if (scenario != 1 || actor.State.AttackSequence == firstSequence)
                    {
                        Assert.That(actor.State.HasBufferedCharge, Is.True, label);
                        Assert.That(actor.State.AttackSequence, Is.EqualTo(firstSequence),
                            label + ": input must not replace the unresolved swing");
                    }
                    else
                        AssertContinuationPoseStep(actor, clickBones, clickRotations, clickWeaponPosition, clickWeaponRotation, label);

                    float untilNext = 0f;
                    bool hadReadyGap = false;
                    for (int frame = 0; frame < 90 && actor.State.AttackSequence == firstSequence; frame++)
                    {
                        bool readyToContinue = actor.State.Phase == MeleePhase.Recovery && actor.HasAttackBalance;
                        yield return AdvanceContinuationFrame(actor, moving, true, label);
                        untilNext += TickSeconds;
                        hadReadyGap |= actor.State.Phase == MeleePhase.Ready;
                        if (readyToContinue)
                            Assert.That(actor.State.AttackSequence, Is.EqualTo(firstSequence + 1),
                                label + ": take the next swing after contact, independently of the support hand");
                    }
                    Assert.That(actor.State.AttackSequence, Is.EqualTo(firstSequence + 1), label + ": exactly one continuation");
                    Assert.That(hadReadyGap, Is.False, label + ": do not visit the idle stance between swings");
                    Assert.That(oldElapsed + untilNext, Is.LessThan(firstDuration - TickSeconds),
                        label + ": continuation must begin before the full return ends");
                    Assert.That(actor.State.IsContinuation, Is.True, label);
                    Assert.That(actor.State.HasBufferedCharge, Is.False, label + ": consume the one-slot input");
                    if (scenario != 0)
                        Assert.That(actor.State.Swing, Is.EqualTo(firstSide), label + ": the target cue retains the swing side");
                    CaptureDuelFrame(capture, "01-continuation-start");

                    if (held)
                    {
                        Assert.That(actor.State.IsCharging, Is.True, label);
                        float initialCharge = actor.State.Charge01;
                        Assert.That(initialCharge, Is.LessThanOrEqualTo(TickSeconds / actor.State.Settings.ChargeSeconds + .0001f),
                            label + ": time spent waiting for the previous swing must not grow charge");
                        Assert.That(actor.State.Settings.ChargeSeconds, Is.EqualTo(.6f).Within(.0001f));
                        int chargeFrames = Mathf.CeilToInt(actor.State.Settings.ChargeSeconds / TickSeconds);
                        for (int frame = 1; frame <= chargeFrames + 8; frame++)
                        {
                            yield return AdvanceContinuationFrame(actor, moving, true, label);
                            Assert.That(actor.State.IsCharging, Is.True, label + ": holding full charge must not fire");
                            Assert.That(actor.State.AttackSequence, Is.EqualTo(firstSequence + 1), label);
                            float expected = Mathf.Min(1f, initialCharge + frame * TickSeconds / actor.State.Settings.ChargeSeconds);
                            Assert.That(actor.State.Charge01, Is.EqualTo(expected).Within(.001f), label + ": fresh charge clock");
                            if (frame == 6) CaptureDuelFrame(capture, "02-continuation-blend");
                        }
                        CaptureDuelFrame(capture, "02-full-charge");
                        Assert.That(actor.ReleaseCharge(), Is.True, label + ": release the held continuation");
                        Assert.That(actor.State.AttackPower, Is.EqualTo(1f).Within(.001f), label);
                    }
                    else
                    {
                        Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Windup), label);
                        Assert.That(actor.State.AttackPower, Is.Zero, label + ": a queued tap stays light");
                        for (int frame = 0; frame < 6; frame++)
                            yield return AdvanceContinuationFrame(actor, moving, true, label);
                        CaptureDuelFrame(capture, "02-continuation-blend");
                    }

                    int recoveryFrames = 0;
                    for (int frame = 0; frame < 180 && actor.State.Phase != MeleePhase.Ready; frame++)
                    {
                        yield return AdvanceContinuationFrame(actor, moving, true, label);
                        if (actor.State.Phase == MeleePhase.Recovery) recoveryFrames++;
                        Assert.That(actor.State.AttackSequence, Is.EqualTo(firstSequence + 1), label + ": no phantom third attack");
                    }
                    Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Ready), label);
                    Assert.That(actor.State.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Miss), label + ": clear-space swings must miss");
                    Assert.That(recoveryFrames * TickSeconds, Is.GreaterThanOrEqualTo(actor.State.AttackRecoverySeconds - TickSeconds * 2f),
                        label + ": without another click the complete final tail must play");
                    for (int frame = 0; frame < 120 && !actor.HasTwoHandSupport; frame++)
                        yield return AdvanceContinuationFrame(actor, moving, false, label);
                    Assert.That(actor.HasTwoHandSupport, Is.True, label + ": final own-weapon contact");
                    Assert.That(actor.SupportGrip.JournalContactError, Is.LessThanOrEqualTo(.025f), label);
                    Assert.That(actor.SupportGrip.JournalContactAngle, Is.LessThanOrEqualTo(12f), label);
                    Assert.That(actor.SupportGrip.JournalWristSafe, Is.True, label);
                    Assert.That(actor.SupportGrip.LiveArmAngles.x, Is.LessThanOrEqualTo(25.1f), label);
                    Assert.That(actor.SupportGrip.LiveArmAngles.y, Is.LessThanOrEqualTo(55.1f), label);
                    Assert.That(root.Opponent.ReceivedImpactCount, Is.Zero, label);
                    CaptureDuelFrame(capture, "03-final-return");
                }
            LogAssert.NoUnexpectedReceived();
        }

        private IEnumerator AdvanceContinuationFrame(CombatActor actor, bool moving, bool measureBridge, string label)
        {
            // Coroutine resumption precedes LateUpdate; compare complete combat
            // poses on both sides of the tick, never its restored animation base.
            actor.Present();
            Transform[] bones = ContinuationArmBones(actor);
            var rotations = new Quaternion[bones.Length];
            for (int i = 0; i < bones.Length; i++) rotations[i] = bones[i].localRotation;
            Vector3 weaponPosition = actor.transform.InverseTransformPoint(actor.Weapon.transform.position);
            Quaternion weaponRotation = Quaternion.Inverse(actor.transform.rotation) * actor.Weapon.transform.rotation;
            root.Tick(TickSeconds);
            if (moving)
            {
                // Match live ordering: duel Update, movement/yaw, rig LateUpdate.
                actor.Body.Move(actor.transform.right * (.6f * TickSeconds));
                actor.transform.rotation *= Quaternion.Euler(0f, 15f * TickSeconds, 0f);
                Physics.SyncTransforms();
            }
            // A normal frame yield works in batch mode and crosses the previous
            // LateUpdate. Reapply the same complete pose after moved roots, without
            // advancing the duel clock, before reading the final limb transforms.
            yield return null;
            actor.Present();
            Assert.That(actor.IsKnockedDown || actor.State.IsDefeated, Is.False, label);
            if (!measureBridge || !actor.State.IsContinuation ||
                (actor.State.IsCharging ? actor.State.Charge01 * actor.State.Settings.ChargeSeconds >= .21f :
                    actor.State.AttackElapsed >= .21f)) yield break;
            AssertContinuationPoseStep(actor, bones, rotations, weaponPosition, weaponRotation, label);
        }

        private static Transform[] ContinuationArmBones(CombatActor actor) => new[]
        {
            ArmMotionBone(actor, "upper_arm.L"), ArmMotionBone(actor, "forearm.L"), ArmMotionBone(actor, "hand.L"),
            ArmMotionBone(actor, "upper_arm.R"), ArmMotionBone(actor, "forearm.R"), ArmMotionBone(actor, "hand.R")
        };

        private void AssertContinuationPoseStep(CombatActor actor, Transform[] bones, Quaternion[] rotations,
            Vector3 weaponPosition, Quaternion weaponRotation, string label)
        {
            var jointSteps = new float[bones.Length];
            bool discontinuous = false;
            for (int i = 0; i < bones.Length; i++)
            {
                jointSteps[i] = Quaternion.Angle(rotations[i], bones[i].localRotation);
                discontinuous |= jointSteps[i] > 35f;
            }
            float weaponStep = Vector3.Distance(weaponPosition, actor.transform.InverseTransformPoint(actor.Weapon.transform.position));
            float weaponTurn = Quaternion.Angle(weaponRotation, Quaternion.Inverse(actor.transform.rotation) * actor.Weapon.transform.rotation);
            string diagnostic = $"{label}: phase={actor.State.Phase}, elapsed={actor.State.AttackElapsed:F5}, charge={actor.State.Charge01:F3}, " +
                $"support={actor.SupportArmState}, grip={actor.HasTwoHandSupport}, gap={actor.SupportGrip.JournalContactError:F5}, " +
                $"reason={actor.SupportGrip.JournalGripReason}, rejected={actor.SupportGrip.LastPoseRejection}";
            if (discontinuous || weaponStep > .30f || weaponTurn > 35f)
                CaptureDuelFrame("balance/continuation/" + label, "failure-discontinuity");
            for (int i = 0; i < bones.Length; i++)
                Assert.That(jointSteps[i], Is.LessThanOrEqualTo(35f),
                    "Continuous continuation joint " + bones[i].name + ". " + diagnostic);
            Assert.That(weaponStep, Is.LessThanOrEqualTo(.30f),
                "The weapon must enter the next windup continuously. " + diagnostic);
            Assert.That(weaponTurn, Is.LessThanOrEqualTo(35f),
                "No weapon rotation reset to stance. " + diagnostic);
        }

        [UnityTest]
        public IEnumerator Range_MovingRegripCarriesTheArmAndRestoresAttack()
        {
            foreach (bool heroActs in new[] { true, false })
            {
                PlacePair(.82f);
                CombatActor actor = heroActs ? root.Hero : root.Opponent;
                CombatActor target = heroActs ? root.Opponent : root.Hero;
                string label = heroActs ? "Test hero returning grip" : "Test opponent returning grip";
                for (int frame = 0; frame < 6; frame++) { root.Tick(TickSeconds); yield return null; }
                Assert.That(actor.RequestAttack(), Is.True, "A real shove opens the supporting hand.");
                for (int frame = 0; frame < 90 && !actor.SupportGrip.IsRegripping; frame++)
                { root.Tick(TickSeconds); yield return null; }
                Assert.That(target.ReceivedImpactCount, Is.EqualTo(1), label);
                Assert.That(actor.SupportGrip.IsRegripping, Is.True, label);
                // Reposition only the recipient: the source keeps its actual
                // post-shove shoulder, wrist, and returning open palm.
                target.ResetActor(actor.transform.position + actor.transform.forward * 4f, -actor.transform.forward);
                Physics.SyncTransforms();
                var route = new MovingGripRoute(actor.transform.position.x);
                actor.SetBlock(true);
                Assert.That(actor.GuardRequested && !actor.State.IsBlocking, Is.True, label);
                yield return ObserveMovingGripReturn(actor, label, "after-shove", true, route);
                actor.AdvanceSimulation(CombatTestRoot.SimulationStep);
                actor.Present();
                Assert.That(actor.GuardReady && actor.State.IsBlocking, Is.True, label);
                actor.SetBlock(false);
                for (int frame = 0; frame < 18; frame++) { root.Tick(TickSeconds); yield return null; }

                // Exercise the real recoil owner and a subsequent queued swing,
                // without resetting the source or waiting for L to recover.
                for (int repetition = 0; repetition < 2; repetition++)
                {
                    actor.SupportGrip.RequestRelease(-actor.transform.right, .65f);
                    Assert.That(actor.RequestCharge(), Is.True, label + ": R can charge with L open");
                    for (int frame = 0; frame < 6; frame++) { root.Tick(TickSeconds); yield return null; }
                    Assert.That(actor.ReleaseCharge(), Is.True, label);
                    for (int frame = 0; frame < 90 && actor.State.Phase != MeleePhase.Active; frame++)
                    { root.Tick(CombatTestRoot.SimulationStep); yield return null; }
                    Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Active), label);
                    CombatActor.Contact.Obstacle(actor, null, actor.Weapon.transform.position,
                        -actor.transform.forward, actor.transform.forward, 0f, false).Apply();
                    Assert.That(actor.State.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Obstacle), label);
                    actor.SupportGrip.RequestRelease(-actor.transform.right, .65f);
                    root.Tick(CombatTestRoot.SimulationStep);
                    yield return null;
                    int sequence = actor.State.AttackSequence;
                    Assert.That(actor.RequestAttack(), Is.True, label + ": queue the next swing during recoil");
                    for (int frame = 0; frame < 120 && actor.State.AttackSequence == sequence; frame++)
                    { root.Tick(CombatTestRoot.SimulationStep); yield return null; }
                    Assert.That(actor.State.AttackSequence, Is.Not.EqualTo(sequence), label);
                    yield return ObserveMovingGripReturn(actor, label, (repetition == 0 ? "after-first-recoil" : "after-second-recoil"), false, route);
                }

                // Block the live contact volume with a separate moving world
                // solid. It is never parented to the actor or its weapon.
                CombatSupportGrip grip = actor.SupportGrip;
                actor.Present();
                // A Ready release opens fingers but need not move the palm far
                // from its bar. Use the actual extended shove hand instead.
                target.ResetActor(actor.transform.position + actor.transform.forward * .82f, -actor.transform.forward);
                Physics.SyncTransforms();
                int shoveImpacts = target.ReceivedImpactCount;
                Assert.That(actor.RequestAttack() && actor.State.IsShoving, Is.True, label + ": open L through a real shove");
                for (int frame = 0; frame < 60 && (target.ReceivedImpactCount == shoveImpacts ||
                    Vector3.Distance(grip.ShovePalmPosition, grip.Target) < .24f); frame++)
                { root.Tick(CombatTestRoot.SimulationStep); yield return null; }
                Assert.That(target.ReceivedImpactCount, Is.EqualTo(shoveImpacts + 1), label + ": the opening shove contacts the real chest");
                actor.Present();
                Vector3 openPalm = grip.ShovePalmPosition;
                Assert.That(Vector3.Distance(openPalm, grip.Target), Is.GreaterThan(.20f), label);
                Assert.That(actor.State.IsShoving, Is.True, label + ": place the barrier before the real return starts");
                target.ResetActor(actor.transform.position + actor.transform.forward * 4f, -actor.transform.forward);
                Physics.SyncTransforms();
                var obstruction = new GameObject("Test returning hand obstruction");
                var obstacleObservations = new System.Text.StringBuilder("frame,phase,arm,wait,palm_x,palm_y,palm_z,contact_x,contact_y,contact_z,wall_x,wall_y,wall_z,wall_qx,wall_qy,wall_qz,wall_qw,palm_overlap,prior_arm_penetration,arm_penetration\n");
                try
                {
                    BoxCollider wall = obstruction.AddComponent<BoxCollider>();
                    // Leave the physical shaft clear while overlapping the
                    // live 35 mm palm sphere. The 120 mm shaft length covers
                    // every admitted +/-25 mm station and 12 degree hand turn.
                    wall.size = new Vector3(.01f, .12f, .08f);
                    Pose initialContact = PlaceReturningGripObstacle(actor, wall);
                    Assert.That(ReturningGripObstacleStartsClear(actor, wall), Is.True,
                        label + ": the real obstacle begins outside the extended arm and physical bar");
                    Assert.That(ReturningGripPalmOverlaps(wall, initialContact.position), Is.True,
                        label + ": the external obstacle covers the live palm volume");
                    bool waitedOpen = false, contactRejected = false;
                    for (int frame = 0; frame < 120; frame++)
                    {
                        actor.Present();
                        PlaceReturningGripObstacle(actor, wall);
                        float priorArmPenetration = ReturningGripObstacleArmPenetration(actor, wall);
                        root.Tick(CombatTestRoot.SimulationStep);
                        Pose liveContact = ReturningGripContact(grip);
                        Vector3 palm = grip.ShovePalmPosition;
                        bool palmOverlap = ReturningGripPalmOverlaps(wall, liveContact.position);
                        float armPenetration = ReturningGripObstacleArmPenetration(actor, wall);
                        Vector3 wallPosition = wall.transform.position;
                        Quaternion wallRotation = wall.transform.rotation;
                        obstacleObservations.AppendLine(System.FormattableString.Invariant($"{frame},{actor.State.Phase},{actor.SupportArmState},{grip.JournalGripReason},{palm.x:F6},{palm.y:F6},{palm.z:F6},{liveContact.position.x:F6},{liveContact.position.y:F6},{liveContact.position.z:F6},{wallPosition.x:F6},{wallPosition.y:F6},{wallPosition.z:F6},{wallRotation.x:F6},{wallRotation.y:F6},{wallRotation.z:F6},{wallRotation.w:F6},{palmOverlap},{priorArmPenetration:F6},{armPenetration:F6}"));
                        if (actor.HasTwoHandSupport || priorArmPenetration > .00001f || armPenetration > .00001f ||
                            (actor.State.Phase == MeleePhase.Ready && !palmOverlap))
                            CaptureDuelFrame("balance/" + label, "obstructed-contact-diagnostic");
                        Assert.That(priorArmPenetration, Is.LessThanOrEqualTo(.00001f),
                            label + ": the moving fixture cannot be placed through the accepted open arm, sample=" + frame);
                        if (actor.State.Phase == MeleePhase.Ready)
                            Assert.That(palmOverlap, Is.True, label + ": the live Ready contact remains physically obstructed, sample=" + frame);
                        Assert.That(actor.HasTwoHandSupport, Is.False,
                            label + ": an obstructed live palm volume forbids restored grip, sample=" + frame + ", overlap=" + palmOverlap);
                        Assert.That(armPenetration, Is.LessThanOrEqualTo(.00001f), label + ": the open arm cannot pass through the obstacle");
                        Assert.That(actor.State.IsBlocking, Is.False, label);
                        waitedOpen |= grip.State == CombatArmSupportState.Free;
                        contactRejected |= grip.JournalGripReason == "contact_palm_overlap";
                        AssertRepeatedGripPresentation(actor, label);
                        yield return null;
                    }
                    Assert.That(waitedOpen, Is.True, label + ": an impossible route waits open rather than chasing forever");
                    Assert.That(contactRejected, Is.True, label + ": the actual world-contact gate rejects the covered palm");
                    CaptureDuelFrame("balance/" + label, "obstructed-open-hand");
                    Assert.That(actor.RequestAttack(), Is.True, label + ": the open L does not disable R");
                    Assert.That(actor.State.IsAttacking, Is.True, label);
                }
                finally
                {
                    WriteReturningGripObservations(label, "obstruction", obstacleObservations);
                    Object.Destroy(obstruction);
                }
                yield return null;
                Physics.SyncTransforms();
                yield return ObserveMovingGripReturn(actor, label, "after-obstruction", false, route);
            }
            LogAssert.NoUnexpectedReceived();
        }

        private static Pose PlaceReturningGripObstacle(CombatActor actor, Collider wall)
        {
            Pose contact = ReturningGripContact(actor.SupportGrip);
            Vector3 axis = actor.Weapon.transform.up;
            Vector3 outward = Vector3.ProjectOnPlane(contact.position - actor.SupportGrip.Target, axis).normalized;
            Assert.That(outward.sqrMagnitude, Is.GreaterThan(.9f));
            wall.transform.SetPositionAndRotation(contact.position + outward * .025f,
                Quaternion.LookRotation(Vector3.Cross(outward, axis), axis));
            Physics.SyncTransforms();
            return contact;
        }

        private static Pose ReturningGripContact(CombatSupportGrip grip)
        {
            object[] arguments = { default(Pose) };
            Assert.That((bool)typeof(CombatSupportGrip).GetMethod("TryContact",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(grip, arguments), Is.True);
            return (Pose)arguments[0];
        }

        private static bool ReturningGripPalmOverlaps(Collider wall, Vector3 contact)
        {
            foreach (Collider hit in Physics.OverlapSphere(contact, .035f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (hit == wall) return true;
            return false;
        }

        private static float ReturningGripObstacleArmPenetration(CombatActor actor, Collider wall)
        {
            float maximum = 0f;
            foreach (var entry in actor.Ragdoll.PhysicsController.AnatomicalColliders)
            {
                if (entry.Value != Player3DAnatomicalPart.LeftUpperArm &&
                    entry.Value != Player3DAnatomicalPart.LeftForearm && entry.Value != Player3DAnatomicalPart.LeftHand) continue;
                if (Physics.ComputePenetration(wall, wall.transform.position, wall.transform.rotation,
                    entry.Key, entry.Key.transform.position, entry.Key.transform.rotation, out _, out float depth))
                    maximum = Mathf.Max(maximum, depth);
            }
            return maximum;
        }

        private static void WriteReturningGripObservations(string label, string capture, System.Text.StringBuilder observations)
        {
            string folder = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(),
                "TestResults", "Test combat follow-up", "Grip observations");
            System.IO.Directory.CreateDirectory(folder);
            System.IO.File.WriteAllText(System.IO.Path.Combine(folder, label + " - " + capture + ".csv"), observations.ToString());
        }

        private static bool ReturningGripObstacleStartsClear(CombatActor actor, Collider wall)
        {
            foreach (var entry in actor.Ragdoll.PhysicsController.AnatomicalColliders)
                if (Physics.ComputePenetration(wall, wall.transform.position, wall.transform.rotation,
                    entry.Key, entry.Key.transform.position, entry.Key.transform.rotation, out _, out float depth) && depth > .00001f)
                    return false;
            foreach (CombatWeaponGeometry.Segment segment in CombatWeaponGeometry.Segments)
            {
                Vector3 a = actor.Weapon.transform.position + actor.Weapon.transform.rotation * segment.A;
                Vector3 b = actor.Weapon.transform.position + actor.Weapon.transform.rotation * segment.B;
                foreach (Collider hit in Physics.OverlapCapsule(a, b, segment.Radius + .004f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    if (hit == wall) return false;
            }
            return true;
        }

        private sealed class MovingGripRoute
        {
            private readonly float centreX;
            private float travelDirection = 1f, yawDirection = 1f, yawOffset;

            internal MovingGripRoute(float centreX) { this.centreX = centreX; }

            internal void Advance(CombatActor actor, string label)
            {
                // Keep one continuous route across shove and both recoils. The
                // previous accumulating rightward walk reached the arena wall
                // at x=2.775, invalidating the unobstructed Ready premise.
                if ((actor.transform.position.x - centreX) * travelDirection >= .30f)
                    travelDirection = -travelDirection;
                actor.Body.Move(Vector3.right * (travelDirection * 2f * TickSeconds));
                if (yawOffset * yawDirection >= 10f) yawDirection = -yawDirection;
                float yawStep = yawDirection * 30f * TickSeconds;
                yawOffset += yawStep;
                actor.transform.rotation *= Quaternion.Euler(0f, yawStep, 0f);
                Assert.That(Mathf.Abs(actor.transform.position.x - centreX), Is.LessThan(.40f),
                    label + ": the free return route stays in the central aisle without resetting the source");
            }
        }

        private IEnumerator ObserveMovingGripReturn(CombatActor actor, string label, string capture, bool freeze, MovingGripRoute route)
        {
            // A nested coroutine can resume after the character's Update has
            // restored the authored clip and before LateUpdate reapplies L.
            // Measure final accepted pose -> final accepted pose, as the source
            // regrip observer below does, never that intermediate clip sample.
            actor.Present();
            Transform upper = ArmMotionBone(actor, "upper_arm.L");
            Transform lower = ArmMotionBone(actor, "forearm.L");
            Transform hand = ArmMotionBone(actor, "hand.L");
            Quaternion oldUpper = upper.localRotation, oldLower = lower.localRotation, oldHand = hand.localRotation;
            int reachingFrames = 0;
            float readySeconds = 0f;
            bool restored = false;
            var observations = new System.Text.StringBuilder("frame,phase,arm,wait,ready_seconds,gap,angle,wrist_safe,weight,search_evaluations,budget_exhausted,shoulder_roll,wrist_deviation,wrist_flexion,elbow,root_x,root_z,blocking_shape,pose_rejection\n");
            var constraint = (CombatWeaponConstraint)typeof(CombatActor).GetField("weaponConstraint",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(actor);
            for (int frame = 0; frame < 180 && !restored; frame++)
            {
                root.Tick(TickSeconds);
                if (actor.State.Phase == MeleePhase.Ready) readySeconds += TickSeconds;
                bool reaching = actor.SupportGrip.IsRegripping;
                Transform armFrame = upper.parent;
                Vector3 palmBefore = armFrame.InverseTransformPoint(actor.SupportGrip.ShovePalmPosition);
                Vector3 targetBefore = armFrame.InverseTransformPoint(actor.SupportGrip.Target);
                Vector3 rootPalmBefore = actor.transform.InverseTransformPoint(actor.SupportGrip.ShovePalmPosition);
                Vector3 parentBefore = actor.transform.InverseTransformPoint(armFrame.position);
                Quaternion carriedUpper = upper.localRotation, carriedLower = lower.localRotation, carriedHand = hand.localRotation;
                // Update -> motor travel/yaw -> LateUpdate at the same duel time.
                route.Advance(actor, label);
                Physics.SyncTransforms();
                actor.Present();
                if (reaching)
                {
                    reachingFrames++;
                    float carryJointChange = Mathf.Max(Quaternion.Angle(carriedUpper, upper.localRotation),
                        Mathf.Max(Quaternion.Angle(carriedLower, lower.localRotation), Quaternion.Angle(carriedHand, hand.localRotation)));
                    Assert.That(carryJointChange, Is.LessThan(.05f), label + ": same-clock carry preserves all accepted L joints");
                    // Full Present also closes world-space soles. Its pelvis
                    // lowering carries both the chest and the held bar; only
                    // a new arm solve, not that parent motion, is a regrip step.
                    Vector3 palmAfter = armFrame.InverseTransformPoint(actor.SupportGrip.ShovePalmPosition);
                    Assert.That(Vector3.Distance(palmBefore, palmAfter), Is.LessThan(.002f),
                        label + ": walking carries the accepted returning arm with its chest at the same duel time");
                    Assert.That(Vector3.Distance(targetBefore, armFrame.InverseTransformPoint(actor.SupportGrip.Target)),
                        Is.LessThan(.002f), label + ": both live contact endpoints share the accepted chest motion");
                    float rootCarry = Vector3.Distance(rootPalmBefore, actor.transform.InverseTransformPoint(actor.SupportGrip.ShovePalmPosition));
                    if (rootCarry >= .002f)
                        TestContext.Out.WriteLine($"{label} carry sample {frame}: root palm delta={rootCarry:F6}, " +
                            $"parent root delta={Vector3.Distance(parentBefore, actor.transform.InverseTransformPoint(armFrame.position)):F6}, " +
                            $"chest palm delta={Vector3.Distance(palmBefore, palmAfter):F6}, L joint delta={carryJointChange:F6}");
                    float step = Mathf.Max(Quaternion.Angle(oldUpper, upper.localRotation),
                        Mathf.Max(Quaternion.Angle(oldLower, lower.localRotation), Quaternion.Angle(oldHand, hand.localRotation)));
                    Assert.That(step, Is.LessThanOrEqualTo(600f * TickSeconds + .2f),
                        label + ": joint-speed limit, sample=" + frame + ", phase=" + actor.State.Phase +
                        ", arm=" + actor.SupportArmState + ", wait=" + actor.SupportGrip.JournalGripReason);
                }
                oldUpper = upper.localRotation; oldLower = lower.localRotation; oldHand = hand.localRotation;
                AssertRepeatedGripPresentation(actor, label);
                CombatSupportGrip observedGrip = actor.SupportGrip;
                observations.AppendLine(System.FormattableString.Invariant($"{frame},{actor.State.Phase},{actor.SupportArmState},{observedGrip.JournalGripReason},{readySeconds:F6},{observedGrip.JournalContactError:F6},{observedGrip.JournalContactAngle:F6},{observedGrip.JournalWristSafe},{observedGrip.Weight:F6},{observedGrip.LastContactSolveEvaluations},{observedGrip.LastContactSolveBudgetExhausted},{observedGrip.LiveShoulderRoll:F6},{observedGrip.LiveArmAngles.x:F6},{observedGrip.LiveArmAngles.y:F6},{observedGrip.LiveSignedElbow:F6},{actor.transform.position.x:F6},{actor.transform.position.z:F6},{constraint.BlockingShape},{observedGrip.LastPoseRejection}"));
                if (freeze && frame == 3)
                {
                    CombatArmSupportState state = actor.SupportArmState;
                    float weight = actor.SupportGripWeight;
                    Vector3 palm = actor.SupportGrip.ShovePalmPosition;
                    using (GameTimeScaleRuntime.AcquirePause())
                    {
                        root.Tick(.20f);
                        for (int repeat = 0; repeat < 4; repeat++) actor.Present();
                        yield return null;
                        Assert.That(actor.SupportArmState, Is.EqualTo(state), label + ": pause preserves state");
                        Assert.That(actor.SupportGripWeight, Is.EqualTo(weight), label + ": pause cannot close the fingers");
                        Assert.That(Vector3.Distance(actor.SupportGrip.ShovePalmPosition, palm), Is.LessThan(.0002f), label);
                    }
                }
                yield return Application.isBatchMode ? null : new WaitForEndOfFrame();
                if (frame == 3) CaptureDuelFrame("balance/" + label, capture + "-returning");
                if (actor.HasTwoHandSupport)
                {
                    Assert.That(actor.SupportGrip.JournalContactError, Is.LessThanOrEqualTo(.025f), label);
                    Assert.That(actor.SupportGrip.JournalContactAngle, Is.LessThanOrEqualTo(12f), label);
                    Assert.That(actor.SupportGrip.JournalWristSafe, Is.True, label);
                    Assert.That(constraint.SupportArmClearance.IsSupportPathClear(upper.position, lower.position, hand.position),
                        Is.True, label + ": the admitted arm keeps body clearance");
                }
                restored = actor.State.Phase == MeleePhase.Ready && actor.HasTwoHandSupport;
                if (readySeconds > .60f) break;
            }
            string detail = label + ": " + actor.State.Phase + "/" + actor.SupportArmState +
                ", wait=" + actor.SupportGrip.JournalGripReason + ", gap=" + actor.SupportGrip.JournalContactError +
                ", angle=" + actor.SupportGrip.JournalContactAngle + ", Ready seconds=" + readySeconds;
            WriteReturningGripObservations(label, capture, observations);
            Assert.That(restored, Is.True, detail);
            Assert.That(readySeconds, Is.LessThanOrEqualTo(.60f), "A stable unobstructed Ready must restore grip promptly. " + detail);
            if (freeze) Assert.That(reachingFrames, Is.GreaterThan(3), "Exercise actual returning poses. " + detail);
            CaptureDuelFrame("balance/" + label, capture + "-restored");
        }

        private static void AssertRepeatedGripPresentation(CombatActor actor, string label)
        {
            Vector3 palm = actor.SupportGrip.ShovePalmPosition;
            CombatArmSupportState state = actor.SupportArmState;
            float weight = actor.SupportGripWeight;
            for (int repeat = 0; repeat < 3; repeat++) actor.Present();
            Assert.That(actor.SupportArmState, Is.EqualTo(state), label + ": repeated Present cannot advance regrip");
            Assert.That(actor.SupportGripWeight, Is.EqualTo(weight), label + ": repeated Present cannot close fingers");
            Assert.That(Vector3.Distance(palm, actor.SupportGrip.ShovePalmPosition), Is.LessThan(.0002f),
                label + ": repeated Present preserves the accepted final arm");
        }

        [UnityTest]
        public IEnumerator Range_ShoveAndSwingRestoreAttackAndBlock()
        {
            for (int rig = 0; rig < 2; rig++)
                for (int action = 0; action < 2; action++)
                {
                    bool shove = action == 0;
                    PlacePair(shove ? .82f : 4f);
                    if (shove && rig == 0)
                    {
                        // Recorded manual stance before the hero's own shove.
                        root.Hero.ResetActor(new Vector3(.1180975f, .0400002f, -2.406575f),
                            new Quaternion(0f, -.08395952f, 0f, .9964692f) * Vector3.forward);
                        root.Opponent.ResetActor(new Vector3(-.1634964f, .0400002f, -1.657811f),
                            new Quaternion(0f, .9941874f, 0f, .107663542f) * Vector3.forward);
                        Physics.SyncTransforms();
                    }
                    for (int frame = 0; frame < 6; frame++)
                    { root.Tick(TickSeconds); yield return null; }
                    CombatActor actor = rig == 0 ? root.Hero : root.Opponent;
                    CombatActor target = rig == 0 ? root.Opponent : root.Hero;
                    string label = (actor.IsHero ? "hero" : "opponent") + (shove ? " shove source" : " swing source");
                    Assert.That(actor.HasTwoHandSupport, Is.True, label + ": initial contact");

                    for (int repetition = 0; repetition < 2; repetition++)
                    {
                        bool crowdedWindup = shove && rig == 1 && repetition == 1;
                        // Reposition only the recipient. Resetting the source
                        // here would erase the permanent post-action grip stall.
                        if (shove && repetition > 0)
                        {
                            target.ResetActor(actor.transform.position + actor.transform.forward * (crowdedWindup ? 1.1f : .82f),
                                -actor.transform.forward);
                            Physics.SyncTransforms();
                            root.Tick(TickSeconds);
                            yield return null;
                        }
                        int impacts = target.ReceivedImpactCount;
                        int sequence = actor.State.AttackSequence;
                        if (crowdedWindup)
                        {
                            // The recorded NPC was already returning its left
                            // arm from a weapon windup when close pressure routed
                            // it into the shove. Keep that actual source chain.
                            Assert.That(actor.RequestAttack(), Is.True, label + ": start a weapon windup");
                            for (int frame = 0; frame < 6; frame++)
                            { root.Tick(TickSeconds); yield return null; }
                            Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Windup), label);
                            target.ResetActor(actor.transform.position + actor.transform.forward * .82f, -actor.transform.forward);
                            Physics.SyncTransforms();
                            impacts = target.ReceivedImpactCount;
                            root.Tick(CombatTestRoot.SimulationStep);
                            yield return null;
                        }
                        else if (shove)
                            Assert.That(actor.RequestAttack(), Is.True, label + ": repeated command " + repetition);
                        else
                        {
                            // The recorded first swing was a brief held press,
                            // whose charge/release pose seeds the returning arm.
                            Assert.That(actor.RequestCharge(), Is.True, label + ": charge press " + repetition);
                            for (int frame = 0; frame < 6; frame++)
                            { root.Tick(TickSeconds); yield return null; }
                            Assert.That(actor.ReleaseCharge(), Is.True, label + ": release after 0.1 seconds");
                        }
                        Assert.That(actor.State.AttackSequence, Is.Not.EqualTo(sequence), label);
                        Assert.That(shove ? actor.State.IsShoving : actor.State.IsAttacking, Is.True, label);
                        yield return WaitForSourceRegrip(actor, label + " " + repetition,
                            stepAfterShove: shove && rig == 1);
                        Assert.That(target.ReceivedImpactCount - impacts, Is.EqualTo(shove ? 1 : 0),
                            label + ": the shove must actually land; a separated swing must miss. " + lastSourceShoveDiagnostics);
                    }

                    // Exercise the public command and its presented guard,
                    // rather than accepting a Ready enum as proof of control.
                    actor.SetBlock(true);
                    Assert.That(actor.State.IsBlocking, Is.True, label + ": block command after repeated actions");
                    Assert.That(actor.GuardReady && actor.GuardRequested, Is.True);
                    for (int frame = 0; frame < 18; frame++)
                    { root.Tick(TickSeconds); yield return null; }
                    Assert.That(actor.State.IsBlocking && actor.HasTwoHandSupport, Is.True,
                        label + ": the raised guard must retain actual contact");
                    if (actor.IsHero && shove && !Application.isBatchMode)
                    {
                        yield return new WaitForEndOfFrame();
                        CaptureGuardHud("raised");
                    }
                    actor.SetBlock(false);
                    Assert.That(actor.GuardRequested, Is.False);
                    if (actor.IsHero && shove && !Application.isBatchMode)
                    {
                        yield return new WaitForEndOfFrame();
                        CaptureGuardHud("ready");
                    }
                    yield return WaitForSourceRegrip(actor, label + " guard release");

                    target.ResetActor(actor.transform.position + actor.transform.forward * 4f, -actor.transform.forward);
                    Physics.SyncTransforms();
                    Assert.That(actor.RequestAttack(), Is.True, label + ": ordinary attack after block");
                    Assert.That(actor.State.IsAttacking, Is.True, label + ": start the swing, not a queued request or shove");
                    yield return WaitForSourceRegrip(actor, label + " follow-up swing");
                    CaptureDuelFrame("balance/source-regrip/" + (actor.IsHero ? "hero" : "opponent"),
                        shove ? "after-shove-and-block" : "after-swing-and-block");
                }
            LogAssert.NoUnexpectedReceived();
        }

        private IEnumerator WaitForSourceRegrip(CombatActor actor, string label, bool stepAfterShove = false)
        {
            // A freshly resumed parent may expose Update's restored clip pose.
            // Use the same final-pose path as the samples below for the baseline.
            actor.Present();
            Transform upper = ArmMotionBone(actor, "upper_arm.L");
            Transform lower = ArmMotionBone(actor, "forearm.L");
            Transform hand = ArmMotionBone(actor, "hand.L");
            Quaternion previousUpper = upper.localRotation, previousLower = lower.localRotation, previousHand = hand.localRotation;
            Quaternion entryUpper = previousUpper, entryLower = previousLower, entryHand = previousHand;
            FieldInfo frozenShovePoint = typeof(CombatSupportGrip).GetField("shovePoint", BindingFlags.Instance | BindingFlags.NonPublic);
            float minimumShoveGap = float.PositiveInfinity;
            lastSourceShoveDiagnostics = string.Empty;
            float readySeconds = 0f, maxReturnJointStep = 0f;
            bool restored = false;
            bool stepRequested = false, sawStep = false, shoveCaptured = false, stepCaptured = false;
            float stepReadySeconds = 0f;
            for (int frame = 0; frame < 240; frame++)
            {
                root.Tick(TickSeconds);
                yield return null;
                actor.Present();
                float jointStep = Mathf.Max(Quaternion.Angle(previousUpper, upper.localRotation),
                    Mathf.Max(Quaternion.Angle(previousLower, lower.localRotation), Quaternion.Angle(previousHand, hand.localRotation)));
                previousUpper = upper.localRotation; previousLower = lower.localRotation; previousHand = hand.localRotation;
                Assert.That(actor.IsKnockedDown || actor.State.IsDefeated, Is.False, label);
                if (actor.State.IsShoving && actor.State.ShoveElapsed <= actor.State.Settings.ShoveContactSeconds + CombatActor.ShoveContactWindowSeconds)
                {
                    CombatActor recipient = actor.IsHero ? root.Opponent : root.Hero;
                    Vector3 push = Vector3.ProjectOnPlane(recipient.transform.position - actor.transform.position, Vector3.up).normalized;
                    if (recipient.Hurtboxes.ChestSurface(actor.transform.position - actor.transform.right * .12f, push, out var surface))
                    {
                        float gap = Vector3.Distance(actor.ShovePalmPosition, surface.Point);
                        if (gap < minimumShoveGap)
                        {
                            minimumShoveGap = gap;
                            Vector3 aimedPoint = (Vector3)frozenShovePoint.GetValue(actor.SupportGrip);
                            lastSourceShoveDiagnostics = $"closest elapsed={actor.State.ShoveElapsed:F4}, gap={gap:F4}, " +
                                $"palm={actor.ShovePalmPosition:F4}, current chest={surface.Point:F4}, aimed chest={aimedPoint:F4}, " +
                                $"entry-to-presented joint degrees={Quaternion.Angle(entryUpper, upper.localRotation):F2}/" +
                                $"{Quaternion.Angle(entryLower, lower.localRotation):F2}/{Quaternion.Angle(entryHand, hand.localRotation):F2}";
                        }
                    }
                }
                if (!shoveCaptured && actor.State.IsShoving && actor.State.ShoveElapsed >= actor.State.Settings.ShoveContactSeconds)
                {
                    CaptureDuelFrame("balance/source-regrip/" + (actor.IsHero ? "hero" : "opponent"), "01-shove-contact");
                    shoveCaptured = true;
                }
                if (stepAfterShove)
                {
                    CombatSupportGrip liveGrip = actor.SupportGrip;
                    string pose = $"{label}: phase={actor.State.Phase}, arm={actor.SupportArmState}, " +
                        $"wrist={liveGrip.LiveArmAngles}, roll={liveGrip.LiveShoulderRoll:F2}, elbow={liveGrip.LiveSignedElbow:F2}";
                    Assert.That(liveGrip.LiveArmAngles.x, Is.LessThanOrEqualTo(25.1f), pose);
                    Assert.That(liveGrip.LiveArmAngles.y, Is.LessThanOrEqualTo(55.1f), pose);
                    Assert.That(Mathf.Abs(liveGrip.LiveShoulderRoll), Is.LessThanOrEqualTo(90.1f), pose);
                    Assert.That(liveGrip.LiveSignedElbow, Is.InRange(-5.1f, 120.1f), pose);
                    if (actor.State.IsShoving)
                    {
                        Assert.That(jointStep, Is.LessThanOrEqualTo(600f * TickSeconds + .2f),
                            pose + ": the shove uses the existing per-joint speed budget");
                    }
                    if (actor.State.Phase == MeleePhase.Step)
                    {
                        sawStep = true;
                        if (!stepCaptured && actor.State.StepProgress >= .4f)
                        { CaptureDuelFrame("balance/source-regrip/opponent", "02-step-return"); stepCaptured = true; }
                    }
                    if (!stepRequested && actor.State.Phase == MeleePhase.Ready)
                    {
                        stepReadySeconds += TickSeconds;
                        if (stepReadySeconds >= .20f)
                        {
                            Assert.That(actor.TryStep(Vector2.right), Is.True, label + ": recorded shove -> right step");
                            Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Step), label);
                            stepRequested = true;
                        }
                    }
                    if (!stepRequested || !sawStep) continue;
                }
                if (actor.State.Phase != MeleePhase.Ready) continue;
                readySeconds += TickSeconds;
                maxReturnJointStep = Mathf.Max(maxReturnJointStep, jointStep);
                if (actor.HasTwoHandSupport) { restored = true; break; }
                if (readySeconds >= 2f) break;
            }
            CombatSupportGrip grip = actor.SupportGrip;
            string diagnostic = $"{label}: phase={actor.State.Phase}, arm={actor.SupportArmState}, wait={grip.JournalGripReason}, " +
                $"gap={grip.JournalContactError:F4}, angle={grip.JournalContactAngle:F2}, wrist={grip.LiveArmAngles}, " +
                $"roll={grip.LiveShoulderRoll:F2}, elbow={grip.LiveSignedElbow:F2}, " +
                $"ready={readySeconds:F3}, jointStep={maxReturnJointStep:F2}";
            Assert.That(restored, Is.True, "Restore physical contact within two seconds of Ready. " + diagnostic);
            Assert.That(maxReturnJointStep, Is.LessThanOrEqualTo(35f), "The returning arm must move continuously. " + diagnostic);
            Assert.That(grip.JournalContactError, Is.LessThanOrEqualTo(.025f), diagnostic);
            Assert.That(grip.JournalContactAngle, Is.LessThanOrEqualTo(12f), diagnostic);
            Assert.That(grip.JournalWristSafe, Is.True, diagnostic);
            Assert.That(grip.LiveArmAngles.x, Is.LessThanOrEqualTo(25.1f), diagnostic);
            Assert.That(grip.LiveArmAngles.y, Is.LessThanOrEqualTo(55.1f), diagnostic);
            if (stepAfterShove)
            {
                Assert.That(stepRequested && sawStep, Is.True, label + ": exercise the actual shove -> Step -> Ready path");
                Assert.That(readySeconds, Is.LessThanOrEqualTo(.60f),
                    "An unobstructed NPC must restore physical grip promptly after its returning step. " + diagnostic);
                CaptureDuelFrame("balance/source-regrip/opponent", "03-ready-grip");
            }
        }

        [UnityTest]
        public IEnumerator Range_ChargeKeepsGripThroughRecordedCloseStance()
        {
            PlacePair(4f);
            // Last complete sample before manual duel c8ccc's first charge.
            root.Hero.ResetActor(new Vector3(-1.71913815f, .04000006f, -1.41135383f),
                new Quaternion(0f, .705188f, 0f, .709020436f) * Vector3.forward);
            root.Opponent.ResetActor(new Vector3(-.747940361f, .04000018f, -1.46618545f),
                new Quaternion(0f, .922409832f, 0f, -.386212528f) * Vector3.forward);
            Physics.SyncTransforms();
            for (int frame = 0; frame < 6; frame++) { root.Tick(TickSeconds); yield return null; }
            Assert.That(root.Hero.HasTwoHandSupport, Is.True);
            Assert.That(root.Hero.RequestCharge(), Is.True);
            bool lost = false;
            string rejection = null;
            for (int frame = 0; frame < 10; frame++)
            {
                root.Hero.Body.Move(new Vector3(-.014296f, 0f, 1.31878f) * (TickSeconds * root.Hero.MovementScale));
                root.Tick(TickSeconds);
                yield return new WaitForEndOfFrame();
                lost |= !root.Hero.HasTwoHandSupport;
                rejection = root.Hero.SupportGrip.LastPoseRejection ?? rejection;
            }
            CaptureDuelFrame("balance/charge", "nearby-charge");
            TestContext.Out.WriteLine("Recorded close charge: rejected=" + rejection + ", grip=" + root.Hero.SupportArmState);
            Assert.That(root.Hero.ReceivedImpactCount, Is.Zero);
            Assert.That(lost, Is.False, "A collision-safe charge must keep its two-hand support: " + rejection);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Range_GripRejectionReleasesFromPresentedArm()
        {
            PlacePair(4f);
            for (int frame = 0; frame < 6; frame++)
            { root.Tick(TickSeconds); yield return new WaitForEndOfFrame(); }
            foreach (CombatActor victim in new[] { root.Hero, root.Opponent })
            {
                Transform shoulder = ArmMotionBone(victim, "upper_arm.L");
                Transform elbow = ArmMotionBone(victim, "forearm.L");
                Transform wrist = ArmMotionBone(victim, "hand.L");
                Quaternion beforeShoulder = shoulder.localRotation, beforeElbow = elbow.localRotation, beforeWrist = wrist.localRotation;
                Assert.That(victim.HasTwoHandSupport, Is.True);
                // An actual occupied palm must reject the contact, including
                // the previous chain, without substituting an authored arm.
                var obstacle = new GameObject("GripPalmObstacle");
                try
                {
                    obstacle.transform.position = victim.SupportGripWorldPosition;
                    obstacle.AddComponent<SphereCollider>().radius = .015f;
                    Physics.SyncTransforms();
                    victim.SupportGrip.Apply();
                    Assert.That(victim.SupportGrip.LastPoseRejection, Is.EqualTo("contact_palm_overlap"));
                }
                finally { Object.DestroyImmediate(obstacle); Physics.SyncTransforms(); }
                Assert.That(victim.SupportArmState, Is.EqualTo(CombatArmSupportState.Releasing));
                Assert.That(victim.SupportGrip.Weight, Is.GreaterThan(.99f));
                Assert.That(Quaternion.Angle(beforeShoulder, shoulder.localRotation), Is.LessThan(.1f));
                Assert.That(Quaternion.Angle(beforeElbow, elbow.localRotation), Is.LessThan(.1f));
                Assert.That(Quaternion.Angle(beforeWrist, wrist.localRotation), Is.LessThan(.1f));
            }
            for (int frame = 0; frame < 8; frame++)
            { root.Tick(TickSeconds); yield return new WaitForEndOfFrame(); }
            foreach (CombatActor victim in new[] { root.Hero, root.Opponent })
            {
                Assert.That(victim.SupportArmState, Is.EqualTo(CombatArmSupportState.Free));
                Assert.That(victim.SupportGrip.Weight, Is.Zero);
                Assert.That(victim.SupportGrip.LiveArmAngles.x, Is.LessThanOrEqualTo(25.1f));
                Assert.That(victim.SupportGrip.LiveArmAngles.y, Is.LessThanOrEqualTo(55.1f));
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Range_WeakShoveKeepsGrip()
        {
            for (int rig = 0; rig < 2; rig++)
            {
                PlacePair(4f);
                for (int frame = 0; frame < 6; frame++)
                { root.Tick(TickSeconds); yield return null; }
                CombatActor victim = rig == 0 ? root.Hero : root.Opponent;
                float health = victim.State.Health;
                PublishShoveReaction(victim, 12f);
                victim.Present();
                Assert.That(victim.State.Phase, Is.EqualTo(MeleePhase.Stagger));
                Assert.That(victim.ImpactMotion.BalanceLoad, Is.LessThan(.35f), "This contact must retain stable support.");
                Assert.That(victim.SupportGrip.IsSupportingWeapon, Is.True,
                    "A non-damaging stagger must not release a physically supported hand through CombatHit.");
                for (int frame = 0; frame < 12; frame++)
                {
                    root.Tick(TickSeconds); yield return null;
                    Assert.That(victim.SupportGrip.IsSupportingWeapon, Is.True, "Weak shove, rig=" + rig);
                    Assert.That(victim.SupportGrip.JournalContactError, Is.LessThanOrEqualTo(.025f));
                    Assert.That(victim.SupportGrip.JournalContactAngle, Is.LessThanOrEqualTo(12f));
                    Assert.That(victim.SupportGrip.JournalWristSafe, Is.True);
                }
                Assert.That(victim.Footwork.CatchStepCount, Is.Zero);

                // Strong release/regrip is covered through actual palm contact
                // and the final anatomical chain in ArmMotion, not target error.
                Assert.That(victim.State.Health, Is.EqualTo(health));
            }
            LogAssert.NoUnexpectedReceived();
        }

        private void PublishShoveReaction(CombatActor victim, float momentum)
        {
            CombatActor source = victim == root.Hero ? root.Opponent : root.Hero;
            Vector3 direction = -victim.transform.forward;
            Vector3 point = victim.Ragdoll.PhysicsController.ChestBody.position - direction * .10f;
            Assert.That(victim.State.ReceiveShove(), Is.True);
            var impact = new CombatImpact(source, victim, source.State.AttackSequence,
                point, -direction, direction, victim.State.Health, victim.State.Health,
                MeleeHitResult.Hit, new MeleeHitLocation(MeleeBodyRegion.Torso, MeleeHitSide.Front),
                part: Player3DAnatomicalPart.Torso, impulse: direction * momentum);
            MethodInfo publish = typeof(CombatActor).GetMethod("PublishImpact", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(publish, Is.Not.Null);
            publish.Invoke(victim, new object[] { impact });
        }

        [UnityTest]
        public IEnumerator Range_FinalCatchRequiresStableMovingSupportBeforeRegrip()
        {
            PlacePair(.82f);
            for (int frame = 0; frame < 6; frame++)
            { root.Tick(TickSeconds); yield return null; }
            CombatActor victim = root.Opponent;
            CombatImpactMotion motion = victim.ImpactMotion;
            Assert.That(motion.ExperimentalRecovery, Is.False);
            Assert.That(root.Hero.RequestAttack(), Is.True);

            // Stop on the visible end of the second swing, before its next
            // simulation step accepts the real presented sole contact.
            bool finalContactPresented = false, gripLost = false;
            for (int tick = 0; tick < 180 && !finalContactPresented; tick++)
            {
                root.Tick(CombatTestRoot.SimulationStep);
                yield return null;
                if (!gripLost && victim.ReceivedImpactCount > 0)
                {
                    // Regrip timing needs a hand that actually lost contact;
                    // a recoverable torso shove alone now keeps its grip.
                    victim.SupportGrip.RejectObstructedPose("contact_world_path");
                    gripLost = true;
                }
                Assert.That(victim.IsKnockedDown, Is.False, DescribeBalance(victim));
                Assert.That(victim.Footwork.CatchStepCount, Is.LessThanOrEqualTo(2));
                finalContactPresented = victim.Footwork.CatchStepCount == 2 &&
                    victim.Footwork.CatchStepActive && victim.Footwork.CatchStepProgress >= 1f;
            }
            Assert.That(finalContactPresented, Is.True, "The real shove must exercise the final catch landing.");
            Assert.That(motion.LandedRecoverySteps, Is.EqualTo(1));
            Assert.That(victim.SupportGrip.IsRegripping, Is.False);
            Assert.That(victim.SupportGrip.JournalRegripAllowed, Is.False);
            victim.SetBlock(true);
            Assert.That(victim.GuardRequested, Is.True);
            Assert.That(victim.GuardReady, Is.False);
            Assert.That(victim.GuardSupportRejection, Is.EqualTo("balance_recovery"),
                "The catch step must not masquerade as a missing two-hand grip.");
            Assert.That(victim.State.IsBlocking, Is.False);

            Vector3 outward = Vector3.ProjectOnPlane(motion.CentreOfMass - motion.SupportCentre, Vector3.up);
            if (outward.sqrMagnitude < .0001f) outward = victim.transform.forward;
            outward.Normalize();
            float step = CombatTestRoot.SimulationStep;
            Vector3 before = victim.transform.position;
            victim.Body.Move(outward * (2f * step));
            Vector3 achievedVelocity = Vector3.ProjectOnPlane(victim.transform.position - before, Vector3.up) / step;
            Assert.That(achievedVelocity.magnitude, Is.EqualTo(2f).Within(.01f), "Use achieved capsule travel, not a desired speed.");
            // This is the NPC locomotion contract. Advance the actor directly
            // here so the disabled AI does not replace the sample with zero.
            victim.SetLocomotion(achievedVelocity);
            victim.AdvanceSimulation(step);
            victim.Present();

            Assert.That(motion.LandedRecoverySteps, Is.EqualTo(2), DescribeBalance(victim));
            Assert.That(victim.Footwork.CatchStepCount, Is.EqualTo(2));
            Assert.That(victim.Footwork.JournalLeftSupport && victim.Footwork.JournalRightSupport, Is.True);
            Assert.That(motion.HasStableRecoverySupport(Vector3.zero), Is.True,
                "The planted stance itself is stable; ongoing outward movement is the missing condition.");
            Assert.That(motion.HasStableRecoverySupport(achievedVelocity), Is.False);
            Assert.That(victim.Footwork.RecoveryEpisodeActive, Is.True);
            Assert.That(victim.SupportGrip.JournalRegripAllowed, Is.False);
            Assert.That(victim.SupportGrip.IsRegripping, Is.False);

            root.Tick(0f);
            victim.Present(); victim.Present();
            Assert.That(victim.Footwork.RecoveryEpisodeActive, Is.True, "Presentation cannot finish the recovery decision.");
            Assert.That(victim.SupportGrip.JournalRegripAllowed, Is.False);
            Assert.That(motion.LandedRecoverySteps, Is.EqualTo(2));

            victim.SetLocomotion(Vector3.zero);
            victim.AdvanceSimulation(step);
            victim.Present();
            Assert.That(victim.Footwork.RecoveryEpisodeActive, Is.False,
                "Stable support releases the episode on its next decision, without a dwell timer.");
            Assert.That(victim.SupportGrip.JournalRegripAllowed, Is.True);
            Assert.That(victim.IsKnockedDown, Is.False);
            for (int frame = 0; frame < 90 && !victim.HasTwoHandSupport; frame++)
            { root.Tick(TickSeconds); yield return null; }
            Assert.That(victim.HasTwoHandSupport, Is.True, "The hand must actually return to the weapon.");
            victim.AdvanceSimulation(step);
            victim.Present();
            Assert.That(victim.GuardReady && victim.State.IsBlocking, Is.True,
                "A held guard restores on the next duel step, without another input command.");
            Assert.That(victim.State.ReceiveHit(victim.State.Settings.Damage, victim.State.Settings.BlockCost, true),
                Is.EqualTo(MeleeHitResult.Blocked), "Restoring support is not a fresh parry press.");
            Assert.That(victim.Footwork.CatchStepCount, Is.EqualTo(2));
            Assert.That(victim.ReceivedImpactCount, Is.EqualTo(1));
            Assert.That(victim.State.Health, Is.EqualTo(victim.State.Settings.MaxHealth));
            LogAssert.NoUnexpectedReceived();
        }

        private static void CaptureGuardHud(string name)
        {
            string folder = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(),
                "Captures", SceneIds.CombatTest, "guard-readiness");
            System.IO.Directory.CreateDirectory(folder);
            Texture2D image = ScreenCapture.CaptureScreenshotAsTexture();
            try { System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, name + ".png"), image.EncodeToPNG()); }
            finally { Object.Destroy(image); }
        }
    }
}
