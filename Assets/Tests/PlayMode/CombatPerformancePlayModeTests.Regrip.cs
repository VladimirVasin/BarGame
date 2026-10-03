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
            LogAssert.NoUnexpectedReceived();
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
            PlacePair(.82f);
            for (int frame = 0; frame < 6; frame++)
            { root.Tick(TickSeconds); yield return Application.isBatchMode ? null : new WaitForEndOfFrame(); }
            CombatActor actor = root.Hero;
            Assert.That(actor.RequestAttack(), Is.True, "A real shove opens the supporting hand.");
            for (int frame = 0; frame < 90 && !actor.SupportGrip.IsRegripping; frame++)
            { root.Tick(TickSeconds); yield return Application.isBatchMode ? null : new WaitForEndOfFrame(); }
            Assert.That(root.Opponent.ReceivedImpactCount, Is.EqualTo(1));
            Assert.That(actor.SupportGrip.IsRegripping, Is.True);
            // The recipient has left reach; the source keeps its actual returning arm.
            root.Opponent.ResetActor(actor.transform.position + actor.transform.forward * 4f,
                -actor.transform.forward);
            Physics.SyncTransforms();
            actor.SetBlock(true);
            Assert.That(actor.GuardRequested, Is.True);
            Assert.That(actor.State.IsBlocking, Is.False, "The open returning hand cannot block yet.");

            int movingReachFrames = 0;
            float returnSeconds = 0f;
            bool restored = false;
            for (int frame = 0; frame < 120 && !restored; frame++)
            {
                root.Tick(TickSeconds);
                returnSeconds += TickSeconds;
                bool reaching = actor.SupportGrip.IsRegripping;
                Vector3 palmBefore = actor.transform.InverseTransformPoint(actor.SupportGrip.ShovePalmPosition);
                // Live ordering: duel Update, then motor translation/yaw, then
                // LateUpdate poses the same simulation time on the moved body.
                actor.Body.Move(actor.transform.right * (2f * TickSeconds));
                actor.transform.rotation *= Quaternion.Euler(0f, 30f * TickSeconds, 0f);
                Physics.SyncTransforms();
                actor.SupportGrip.Apply();
                if (reaching)
                {
                    movingReachFrames++;
                    Vector3 palmAfter = actor.transform.InverseTransformPoint(actor.SupportGrip.ShovePalmPosition);
                    Assert.That(Vector3.Distance(palmBefore, palmAfter), Is.LessThan(.002f),
                        "Walking/turning at the same duel time must carry the returning arm, not pull it back to its old world point.");
                }
                yield return Application.isBatchMode ? null : new WaitForEndOfFrame();
                if (frame == 3) CaptureDuelFrame("balance/moving-regrip", "returning");
                restored = actor.State.Phase == MeleePhase.Ready && actor.HasTwoHandSupport;
            }
            Assert.That(movingReachFrames, Is.GreaterThan(3), "Exercise the moving open hand before contact closes.");
            Assert.That(restored, Is.True, "Walking cannot keep an otherwise ready fighter waiting for his own weapon.");
            Assert.That(returnSeconds, Is.LessThanOrEqualTo(.60f),
                "An unobstructed return after a shove must restore contact promptly while walking and turning.");
            Assert.That(actor.SupportGrip.JournalContactError, Is.LessThanOrEqualTo(.025f));
            Assert.That(actor.SupportGrip.JournalContactAngle, Is.LessThanOrEqualTo(12f));
            Assert.That(actor.SupportGrip.JournalWristSafe, Is.True);
            actor.AdvanceSimulation(CombatTestRoot.SimulationStep);
            actor.Present();
            Assert.That(actor.GuardReady && actor.State.IsBlocking, Is.True,
                "The held guard must restore on the next duel step after actual palm contact.");
            actor.SetBlock(false);
            CaptureDuelFrame("balance/moving-regrip", "restored");
            Assert.That(actor.RequestCharge(), Is.True, "The next attack press must work while the body is moving.");
            Assert.That(actor.ReleaseCharge(), Is.True);
            Assert.That(actor.State.IsAttacking, Is.True);
            LogAssert.NoUnexpectedReceived();
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
