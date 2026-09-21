using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using static BarPromenade.Tests.PlayMode.CombatTuning;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        private const float ImpactFrameSeconds = 1f / 60f;
        private const float ChargedDiagnosticImpulse = 205f;
        private int diagnosticImpactSequence;

        [UnityTest]
        public IEnumerator Range_ImpactRecoveryMovesBothRigsThroughSupportFallRiseAndRegrip()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
            yield return EnterRange();
            foreach (bool heroVictim in new[] { true, false })
            {
                CombatActor victim = heroVictim ? root.Hero : root.Opponent;
                CombatActor attacker = heroVictim ? root.Opponent : root.Hero;
                string subject = heroVictim ? "hero" : "opponent";
                foreach (string side in new[] { "L", "R" })
                {
                    Transform forearm = FindAnatomicalBone(victim, "forearm." + side);
                    Transform hand = FindAnatomicalBone(victim, "hand." + side);
                    ConfigurableJoint elbow = forearm.GetComponent<ConfigurableJoint>();
                    Assert.That(elbow, Is.Not.Null);
                    Assert.That(Mathf.Abs(Vector3.Dot(forearm.TransformDirection(elbow.axis).normalized,
                        (hand.position - forearm.position).normalized)), Is.LessThan(.05f),
                        subject + ": the elbow hinge must bend across the forearm, not twist along it.");
                }

                // Start at the actual authored blade. Controlled impulses below
                // then isolate direction, repeat contacts and physical recovery
                // without defeating a character merely to set up a fall.
                PlacePair(1.1f);
                yield return null;
                yield return null;
                Assert.That(attacker.TryAttack(), Is.True);
                for (int frame = 0; frame < 90 && victim.ReceivedImpactCount == 0; frame++)
                {
                    root.Tick(ImpactFrameSeconds);
                    yield return null;
                    PresentInertiaPose();
                    AssertWeaponClearance(attacker, subject + "/incoming live strike frame=" + frame);
                }
                Assert.That(victim.ReceivedImpactCount, Is.EqualTo(1), subject + ": the real crowbar must connect once.");
                CombatImpact contact = victim.LastImpact;
                Assert.That(contact.Damage, Is.GreaterThan(0f));
                Assert.That(contact.Impulse.sqrMagnitude, Is.GreaterThan(.01f));
                Assert.That(Vector3.Dot(contact.Impulse.normalized, contact.Direction), Is.GreaterThan(.9f),
                    subject + ": the physical impulse must follow the blade into the struck body.");
                Assert.That(float.IsFinite(contact.WeaponSpeed), Is.True);
                Assert.That(contact.WeaponSpeed, Is.GreaterThanOrEqualTo(0f));
                Assert.That(IsFiniteImpactVector(contact.LocalPoint), Is.True);
                Assert.That(victim.State.Health, Is.GreaterThan(0f));
                Assert.That(root.RoundFinished, Is.False);
                yield return VerifyStandingImpactRegrip(victim, subject);

                yield return VerifyWeaponClearanceStates(victim, subject);
                yield return VerifyMixedWeaponSupport(victim, subject);
                yield return VerifyImpactDirectionAndAccumulation(victim, subject);
                yield return VerifyImpactObstruction(victim, subject);
                yield return CaptureImpactRecoveryCycle(victim, subject);
                yield return CaptureImpactRecoveryCycle(victim, subject + "-forward", true);
                yield return VerifyImpactResetDuringFall(victim, subject);
            }

            // Unloading an owned fall must release the same input/presentation
            // leases as R, without needing the recovery coroutine to finish.
            PlacePair(4f);
            yield return null;
            ApplyControlledImpact(root.Hero, -root.Hero.transform.forward, ChargedDiagnosticImpulse);
            for (int frame = 0; frame < 180 && !root.Hero.IsRagdollActive; frame++)
            {
                root.Tick(ImpactFrameSeconds);
                yield return null;
            }
            Assert.That(root.Hero.IsKnockedDown, Is.True);
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
            root = null;
            Assert.That(Object.FindAnyObjectByType<CombatActor>(), Is.Null);
            Assert.That(PauseMenuController.IsAnyPaused, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        private IEnumerator VerifyStandingImpactRegrip(CombatActor victim, string subject)
        {
            // A normal recoil may briefly free the supporting hand. If the hit
            // becomes a fall, the full recovery scenario below owns that case.
            for (int frame = 0; frame < 90 && !victim.IsKnockedDown &&
                victim.SupportArmState != CombatArmSupportState.SupportingWeapon; frame++)
            {
                root.Tick(ImpactFrameSeconds);
                yield return null;
                PresentInertiaPose();
            }
            if (victim.IsKnockedDown) yield break;
            Assert.That(victim.SupportArmState, Is.EqualTo(CombatArmSupportState.SupportingWeapon),
                subject + ": an ordinary standing recoil must restore its supporting hand within 1.5 seconds.");
            NpcHandPose hands = victim.GetComponentInChildren<NpcHandPose>();
            Assert.That(Vector3.Distance(hands.CylinderCentre(true), victim.SupportGripWorldPosition), Is.LessThan(.035f),
                subject + ": the support state must describe an actual hand contact after ordinary recoil.");
        }

        private IEnumerator VerifyImpactDirectionAndAccumulation(CombatActor victim, string subject)
        {
            foreach (Vector3 localDirection in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            {
                PlacePair(4f);
                yield return null;
                yield return null;
                Vector3 direction = victim.transform.TransformDirection(localDirection);
                Vector3 start = victim.transform.position;
                Quaternion facing = victim.transform.rotation;
                string capture = localDirection == Vector3.right ? subject + "-standing-side" :
                    localDirection == Vector3.back ? subject + "-standing-back" : null;
                if (capture != null) CaptureImpactRecoveryFrame(victim, capture, "ready", start, facing);
                float health = victim.State.Health;
                Transform chest = FindAnatomicalBone(victim, "chest");
                Quaternion chestBefore = chest.rotation;
                float maximumChestAngle = 0f;
                ApplyControlledImpact(victim, direction, 24f);
                float firstSpeed = Vector3.Dot(victim.ImpactMotion.Velocity, direction);
                Assert.That(firstSpeed, Is.GreaterThan(.05f), subject + ": an impulse must carry velocity immediately.");
                ApplyControlledImpact(victim, direction, 24f);
                Assert.That(Vector3.Dot(victim.ImpactMotion.Velocity, direction), Is.GreaterThan(firstSpeed + .02f),
                    subject + ": a second contact adds momentum instead of replacing or rejecting the first.");
                Assert.That(victim.ImpactMotion.AngularVelocity.sqrMagnitude, Is.GreaterThan(.001f),
                    subject + ": a torso contact above the centre of mass must also rock the body.");
                Vector3 heldVelocity = victim.ImpactMotion.Velocity;
                for (int sample = 0; sample < 5; sample++) victim.Present();
                Assert.That(Vector3.Distance(victim.ImpactMotion.Velocity, heldVelocity), Is.LessThan(.0001f),
                    "Presenting or querying a pose cannot spend the impulse clock.");

                for (int frame = 0; frame < 18; frame++)
                {
                    root.Tick(ImpactFrameSeconds);
                    yield return null;
                    PresentInertiaPose();
                    maximumChestAngle = Mathf.Max(maximumChestAngle, Quaternion.Angle(chestBefore, chest.rotation));
                    if (capture != null && (frame == 5 || frame == 11 || frame == 17))
                        CaptureImpactRecoveryFrame(victim, capture, "recoil-" + frame.ToString("D2"), start, facing);
                }
                Vector3 displacement = victim.transform.position - start;
                displacement.y = 0f;
                Assert.That(Vector3.Dot(displacement, direction), Is.GreaterThan(.015f),
                    subject + ": the root must follow the contact direction, including lateral and rear contacts.");
                Assert.That(Vector3.ProjectOnPlane(displacement, direction).magnitude, Is.LessThan(.10f),
                    subject + ": a side hit must not become an unrelated shove away from the opponent.");
                Assert.That(maximumChestAngle, Is.GreaterThan(.25f),
                    subject + ": the visible torso must answer the momentum as well as the gameplay capsule.");
                Assert.That(victim.State.Health, Is.EqualTo(health), "Resolved impulse diagnostics cannot apply HP twice.");
                Assert.That(root.RoundFinished, Is.False);
            }
        }

        private IEnumerator VerifyImpactObstruction(CombatActor victim, string subject)
        {
            PlacePair(4f);
            yield return null;
            yield return null;
            Vector3 direction = victim.transform.right;
            Vector3 start = victim.transform.position;
            var obstacle = new GameObject("Impact recovery wall fixture");
            obstacle.transform.SetParent(root.transform, false);
            obstacle.transform.SetPositionAndRotation(start + direction * (victim.Body.radius + .13f) + Vector3.up,
                Quaternion.LookRotation(direction));
            BoxCollider wall = obstacle.AddComponent<BoxCollider>();
            wall.size = new Vector3(3f, 2.2f, .12f);
            Physics.SyncTransforms();
            try
            {
                ApplyControlledImpact(victim, direction, 48f);
                for (int frame = 0; frame < 60; frame++)
                {
                    root.Tick(ImpactFrameSeconds);
                    yield return null;
                }
                Assert.That(Vector3.Dot(victim.transform.position - start, direction), Is.LessThan(.17f),
                    subject + ": a collision must stop the forced travel at the real capsule.");
                Assert.That(victim.IsKnockedDown, Is.False, "This small wall contact exercises grounded movement.");
                wall.enabled = false;
                Vector3 stopped = victim.transform.position;
                for (int frame = 0; frame < 24; frame++)
                {
                    root.Tick(ImpactFrameSeconds);
                    yield return null;
                }
                Assert.That(Vector3.Distance(stopped, victim.transform.position), Is.LessThan(.03f),
                    subject + ": a wall may not bank refused impulse for later release.");
            }
            finally { Object.Destroy(obstacle); }
        }

        private IEnumerator CaptureImpactRecoveryCycle(CombatActor victim, string subject, bool forward = false)
        {
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest,
                "impact-recovery", subject);
            if (Directory.Exists(folder))
                foreach (string frame in Directory.EnumerateFiles(folder, "frame-*.png"))
                {
                    string name = Path.GetFileNameWithoutExtension(frame);
                    if (name.Length == 10 && int.TryParse(name.Substring(6), out _)) File.Delete(frame);
                }
            PlacePair(4f);
            yield return null;
            yield return null;
            if (forward)
            {
                victim.SetBlock(true);
                for (int settle = 0; settle < 18; settle++)
                {
                    root.Tick(ImpactFrameSeconds);
                    yield return null;
                }
            }
            Vector3 origin = victim.transform.position;
            Quaternion facing = victim.transform.rotation;
            Transform pelvis = FindAnatomicalBone(victim, "pelvis");
            Transform weaponParent = victim.Weapon.transform.parent;
            Vector3 previousPelvis = pelvis.position;
            float health = victim.State.Health;
            float minimumGrip = victim.SupportGripWeight;
            float maximumBodyDrop = 0f;
            float initialPelvisHeight = pelvis.position.y;
            bool sawKnockdown = false, sawPhysics = false, sawRise = false, pausedPhysics = false, recovered = false;
            bool interruptedRegrip = false;
            bool capturedRiseCamera = false;
            bool capturedWeaponCarry = false;
            int regripInterruptedFrame = -1;
            int capturedFrames = 0;
            using var observation = new ImpactRecoveryObservation(victim);
            var measurements = new StringBuilder("frame,seconds,phase,knocked_down,physics,recovering,grip,root_x,root_y,root_z,pelvis_y,balance_load,motion_age,speed,velocity_x,velocity_y,velocity_z," +
                "rise_stage,clip_progress,arm_state,hand_gap,left_sole,right_sole,land_time,since_land,ground_static,rise_static,regrip_seconds,pelvis_step,max_joint_step,central_speed,thigh_l,shin_l,thigh_r,shin_r,knee_l,knee_r,knee_separation,right_wrist_bend,right_grip_gap,shaft_axis_error,right_wrist_deviation,left_wrist_deviation\n");
            CaptureImpactRecoveryFrame(victim, subject, "ready", origin, facing);
            CaptureImpactRecoveryFrame(victim, subject, "weapon-ready", origin, facing, true);
            ApplyControlledImpact(victim, (victim.transform.forward * (forward ? 1f : -1f) + victim.transform.right * .35f).normalized,
                ChargedDiagnosticImpulse);

            // A bounded simulation budget, not a fixed clip-duration expectation:
            // landing orientation and support geometry choose the rise route.
            for (int frame = 0; frame < 1800; frame++)
            {
                bool wasPhysical = victim.Ragdoll.PhysicsController.IsSimulating;
                root.Tick(ImpactFrameSeconds);
                if (!wasPhysical && victim.Ragdoll.PhysicsController.IsSimulating)
                    AssertImpactHandoff(victim, subject + ": initial handoff");
                yield return null;
                PresentInertiaPose();
                sawKnockdown |= victim.IsKnockedDown;
                bool physical = victim.Ragdoll.PhysicsController.IsSimulating;
                sawPhysics |= physical;
                sawRise |= victim.Ragdoll.IsRecovering;
                minimumGrip = Mathf.Min(minimumGrip, victim.SupportGripWeight);
                maximumBodyDrop = Mathf.Max(maximumBodyDrop, initialPelvisHeight - pelvis.position.y);
                Assert.That(IsFiniteImpactVector(pelvis.position), Is.True, subject + ": the fall cannot corrupt the rig.");
                Assert.That(Vector3.Distance(previousPelvis, pelvis.position), Is.LessThan(.60f),
                    subject + ": impact, physics and recovery must share a continuous visible pelvis, frame=" + frame +
                    ", phase=" + victim.State.Phase + ", arm=" + victim.SupportArmState);
                Assert.That(Vector3.Distance(origin, pelvis.position), Is.LessThan(5f),
                    subject + ": the bounded hit must not launch the body out of the duel.");
                previousPelvis = pelvis.position;
                Assert.That(victim.State.Health, Is.EqualTo(health));
                Assert.That(root.RoundFinished, Is.False, "A living knockdown must not finish the round.");
                Assert.That(root.RoundCameraReleased, Is.False, "A knockdown is not the victory camera handoff.");
                Assert.That(victim.IsWeaponDropped, Is.False);
                Assert.That(victim.Weapon.transform.parent, Is.SameAs(weaponParent),
                    "The right hand keeps its weapon through the recoverable fall.");
                if (interruptedRegrip && frame > regripInterruptedFrame && physical)
                {
                    Assert.That(victim.IsKnockedDown, Is.True,
                        subject + ": an interrupted regrip cannot complete the previous recovery.");
                    Assert.That(victim.SupportArmState, Is.Not.EqualTo(CombatArmSupportState.SupportingWeapon));
                    Assert.That(victim.SupportGripWeight, Is.LessThan(.35f),
                        subject + ": the interrupted hand must remain released while the new fall owns it.");
                }
                AssertWeaponClearance(victim, subject + " recovery frame=" + frame);
                string motionFailure = observation.Sample(frame * ImpactFrameSeconds);
                AppendImpactMeasurement(measurements, victim, pelvis, frame, observation);
                if (motionFailure != null)
                {
                    WriteImpactMeasurements(subject, measurements);
                    CaptureImpactRecoveryFrame(victim, subject, "motion-failure", origin, facing);
                    Assert.Fail(subject + ": " + motionFailure + ", frame=" + frame + ". " + DescribeImpactRecoveryGate(victim));
                }
                if (frame == 179 && !sawKnockdown)
                {
                    WriteImpactMeasurements(subject, measurements);
                    Assert.Fail(subject + ": the strong contact never entered knockdown. " + DescribeImpactRecoveryGate(victim));
                }

                // Ten frames per second catch the fast loss of balance; the
                // remaining full cycle is sampled at five, including both hands
                // and both feet in the same fixed oblique camera.
                if (frame < 90 ? frame % 6 == 0 : frame % 12 == 0)
                {
                    CaptureImpactRecoveryFrame(victim, subject, "frame-" + capturedFrames.ToString("D4"), origin, facing);
                    capturedFrames++;
                }
                if (!capturedWeaponCarry && victim.Ragdoll.IsRecovering && observation.ClipProgress >= .35f)
                {
                    capturedWeaponCarry = true;
                    CaptureImpactRecoveryFrame(victim, subject, "weapon-carry", origin, facing, true);
                }
                if (physical && !pausedPhysics)
                {
                    CaptureImpactPlayerCamera(subject, "fall-shoulder");
                    pausedPhysics = true;
                    yield return VerifyImpactLocalHitStop(victim, subject);
                    yield return VerifyImpactPhysicsPause(victim, subject);
                    previousPelvis = pelvis.position;
                }
                if (victim.Ragdoll.IsRecovering && !capturedRiseCamera)
                {
                    capturedRiseCamera = true;
                    CaptureImpactPlayerCamera(subject, "rise-shoulder");
                }
                if (!interruptedRegrip && victim.IsKnockedDown && victim.Ragdoll.IsRecovering &&
                    victim.SupportArmState == CombatArmSupportState.Regripping)
                {
                    CaptureImpactRecoveryFrame(victim, subject, "regrip-interrupted", origin, facing);
                    interruptedRegrip = true;
                    regripInterruptedFrame = frame;
                    // A new ordinary hit meets the still-living standing recovery.
                    // Its pose, pending hand contact and physical ownership all
                    // have to return to the fall before another rise can finish.
                    var handoffPose = CaptureImpactHandoff(victim);
                    ApplyControlledImpact(victim, -victim.transform.forward, 100f);
                    AssertImpactHandoff(victim, subject + ": interrupted regrip handoff", handoffPose);
                    Assert.That(victim.IsKnockedDown && victim.IsRagdollActive, Is.True);
                    Assert.That(victim.Ragdoll.IsRecovering, Is.False);
                    Assert.That(victim.Ragdoll.PhysicsController.IsSimulating, Is.True,
                        subject + ": a hit during regrip must return the displayed body to physics.");
                    Assert.That(victim.State.Health, Is.EqualTo(health));
                }
                if (sawKnockdown && !victim.IsKnockedDown && !victim.IsRagdollActive &&
                    victim.State.Phase == MeleePhase.Ready && victim.SupportGripWeight > .90f)
                {
                    Assert.That(interruptedRegrip, Is.True,
                        subject + ": the first recovery must expose a real regrip before returning combat ownership.");
                    recovered = true;
                    break;
                }
            }
            WriteImpactMeasurements(subject, measurements);
            if (!(sawKnockdown && sawPhysics && sawRise && recovered))
                Assert.Fail(subject + ": a strong living hit must lose support, fall physically, rise and regrip within its bounded recovery. " +
                    DescribeImpactRecoveryGate(victim));
            Assert.That(maximumBodyDrop, Is.GreaterThan(.28f), subject + ": the body must actually reach the ground.");
            Assert.That(minimumGrip, Is.LessThan(.35f), subject + ": the supporting hand must release the shaft for recovery.");
            Assert.That(victim.Body.enabled && victim.IsAvailable, Is.True);
            Assert.That(root.CameraFollow.TargetLockActive, Is.True,
                "Manual duel stepping must leave the live combat camera attached after both recoveries.");
            Assert.That(IsFiniteImpactVector(root.CameraFollow.Camera.transform.position), Is.True);
            NpcHandPose hands = victim.GetComponentInChildren<NpcHandPose>();
            Assert.That(Vector3.Distance(hands.CylinderCentre(true), victim.SupportGripWorldPosition), Is.LessThan(.035f),
                subject + ": the returned grip must touch the shaft before it claims support.");
            Vector3 pelvisFromRoot = pelvis.position - victim.transform.position;
            pelvisFromRoot.y = 0f;
            Assert.That(pelvisFromRoot.magnitude, Is.LessThan(.55f),
                subject + ": standing gameplay collision must follow the place where the body recovered.");
            CaptureImpactRecoveryFrame(victim, subject, "recovered", origin, facing);
            CaptureImpactRecoveryFrame(victim, subject, "weapon-recovered", origin, facing, true);
            CaptureImpactPlayerCamera(subject, "recovered-shoulder");
            Assert.That(victim.TryAttack(), Is.True, subject + ": recovery must return usable combat ownership.");
        }

        private IEnumerator VerifyImpactLocalHitStop(CombatActor victim, string subject)
        {
            var bodies = victim.Ragdoll.Bodies;
            var positions = new Vector3[bodies.Count];
            var rotations = new Quaternion[bodies.Count];
            var velocities = new Vector3[bodies.Count];
            var angularVelocities = new Vector3[bodies.Count];
            float maximumSpeed = 0f;
            for (int i = 0; i < bodies.Count; i++)
            {
                positions[i] = bodies[i].position;
                rotations[i] = bodies[i].rotation;
                velocities[i] = bodies[i].linearVelocity;
                angularVelocities[i] = bodies[i].angularVelocity;
                maximumSpeed = Mathf.Max(maximumSpeed, velocities[i].magnitude, angularVelocities[i].magnitude);
            }
            Assert.That(maximumSpeed, Is.GreaterThan(.01f), "Local hit-stop must be tested on a moving physical body.");
            victim.SetPresentationFrozen(true);
            try
            {
                Assert.That(victim.Ragdoll.PhysicsController.IsSimulationSuspended, Is.True);
                // Do not call root.Tick here: the root deliberately clears an
                // unrequested duel freeze after a normal simulated step. Real
                // fixed frames still run, proving this is local PhysX suspension.
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
                yield return null;
                for (int i = 0; i < bodies.Count; i++)
                {
                    Assert.That(Vector3.Distance(positions[i], bodies[i].position), Is.LessThan(.002f),
                        subject + ": a contact freeze must hold each physical part's position.");
                    Assert.That(Quaternion.Angle(rotations[i], bodies[i].rotation), Is.LessThan(.10f),
                        subject + ": a contact freeze must hold each physical part's rotation.");
                }
            }
            finally { victim.SetPresentationFrozen(false); }
            Assert.That(victim.Ragdoll.PhysicsController.IsSimulationSuspended, Is.False);
            for (int i = 0; i < bodies.Count; i++)
            {
                Assert.That(Vector3.Distance(velocities[i], bodies[i].linearVelocity), Is.LessThan(.001f),
                    subject + ": releasing hit-stop must restore the earned linear velocity exactly once.");
                Assert.That(Vector3.Distance(angularVelocities[i], bodies[i].angularVelocity), Is.LessThan(.001f),
                    subject + ": releasing hit-stop must restore angular velocity without a new impulse.");
            }
        }

        private IEnumerator VerifyImpactPhysicsPause(CombatActor victim, string subject)
        {
            Assert.That(root.PauseMenu.Open(), Is.True);
            Vector3 pelvis = victim.Ragdoll.PelvisBody.position;
            Vector3 chest = victim.Ragdoll.PhysicsController.ChestBody.position;
            float grip = victim.SupportGripWeight;
            for (int frame = 0; frame < 4; frame++)
            {
                root.Tick(.5f);
                yield return null;
            }
            Assert.That(Vector3.Distance(pelvis, victim.Ragdoll.PelvisBody.position), Is.LessThan(.002f),
                subject + ": pause must hold the falling body, not only the combat rules.");
            Assert.That(Vector3.Distance(chest, victim.Ragdoll.PhysicsController.ChestBody.position), Is.LessThan(.002f));
            Assert.That(victim.SupportGripWeight, Is.EqualTo(grip).Within(.0001f));
            Assert.That(root.PauseMenu.Cancel(), Is.True);
            yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause kept the impact recovery input lock.");
        }

        private IEnumerator VerifyImpactResetDuringFall(CombatActor victim, string subject)
        {
            PlacePair(4f);
            yield return null;
            ApplyControlledImpact(victim, -victim.transform.forward, ChargedDiagnosticImpulse);
            for (int frame = 0; frame < 180 && !victim.IsRagdollActive; frame++)
            {
                root.Tick(ImpactFrameSeconds);
                yield return null;
            }
            Assert.That(victim.IsKnockedDown && victim.IsRagdollActive, Is.True);
            // The authored sweep was exercised above. Resolve another real
            // attack against this live anatomical contact to prove that the
            // downed phase still accepts HP damage without becoming a standing
            // Stagger. Reset immediately afterwards also discards its hit-stop.
            CombatActor attacker = victim == root.Hero ? root.Opponent : root.Hero;
            Assert.That(attacker.TryAttack(), Is.True);
            Transform chest = FindAnatomicalBone(victim, "chest");
            Vector3 centre = victim.Ragdoll.PhysicsController.ChestBody.worldCenterOfMass;
            Vector3 direction = (centre - (attacker.transform.position + Vector3.up)).normalized;
            Vector3 point = centre - direction * .10f;
            float healthBeforeContact = victim.State.Health;
            new CombatActor.Contact(attacker, victim, point, -direction, direction,
                new MeleeHitLocation(MeleeBodyRegion.Torso, MeleeHitSide.Front), Player3DAnatomicalPart.Torso,
                chest.InverseTransformPoint(point), 3f).Apply();
            Assert.That(victim.State.Health, Is.LessThan(healthBeforeContact),
                subject + ": a living downed body must remain vulnerable to an actual attack's damage.");
            Assert.That(victim.State.Health, Is.GreaterThan(0f));
            Assert.That(victim.State.Phase, Is.EqualTo(MeleePhase.KnockedDown),
                subject + ": another hit must preserve the physical downed phase instead of standing Stagger.");
            Assert.That(victim.IsKnockedDown && victim.IsRagdollActive, Is.True);
            Assert.That(root.RoundFinished, Is.False);
            root.ResetRound();
            yield return null;
            Assert.That(victim.IsKnockedDown || victim.IsRagdollActive || victim.ImpactMotion.IsActive, Is.False,
                subject + ": R must dispose of both physical and pending impact state.");
            Assert.That(victim.Body.enabled && victim.IsAvailable, Is.True);
            Assert.That(victim.State.Health, Is.EqualTo(S.MaxHealth));
            Assert.That(victim.State.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(victim.IsWeaponDropped, Is.False);
            Assert.That(victim.Ragdoll.PhysicsController.IsSimulationSuspended, Is.False);
            Vector3 resetPosition = victim.transform.position;
            for (int frame = 0; frame < 18; frame++)
            {
                root.Tick(ImpactFrameSeconds);
                yield return null;
            }
            Assert.That(Vector3.Distance(resetPosition, victim.transform.position), Is.LessThan(.03f),
                "An old hit or recovery callback cannot move the reset round.");
        }

        private static (Transform bone, Vector3 position, Quaternion rotation)[] CaptureImpactHandoff(CombatActor actor)
        {
            var result = new (Transform bone, Vector3 position, Quaternion rotation)[3];
            Transform[] targets = { FindAnatomicalBone(actor, "forearm.L"),
                FindAnatomicalBone(actor, "forearm.R"), actor.Weapon.transform };
            for (int i = 0; i < targets.Length; i++)
                result[i] = (targets[i], targets[i].position, targets[i].rotation);
            return result;
        }

        private static void AssertImpactHandoff(CombatActor actor, string context,
            (Transform bone, Vector3 position, Quaternion rotation)[] before = null)
        {
            // This runs in the activating call, before a yield can let PhysX
            // advance the body. A repeated hit must retain the exact live pose.
            if (before != null)
                foreach (var pose in before)
                {
                    Assert.That(Vector3.Distance(pose.bone.position, pose.position), Is.LessThan(.001f),
                        context + ": rebasing an elbow cannot move " + pose.bone.name);
                    Assert.That(Quaternion.Angle(pose.bone.rotation, pose.rotation), Is.LessThan(.1f),
                        context + ": rebasing an elbow cannot unwind " + pose.bone.name);
                }
            foreach (string side in new[] { "L", "R" })
            {
                Transform upper = FindAnatomicalBone(actor, "upper_arm." + side);
                Transform forearm = FindAnatomicalBone(actor, "forearm." + side);
                Vector3 lowerDirection = (FindAnatomicalBone(actor, "hand." + side).position - forearm.position).normalized;
                Vector3 upperDirection = (forearm.position - upper.position).normalized;
                float flexion = Vector3.Angle(upperDirection, lowerDirection);
                ConfigurableJoint elbow = null;
                foreach (ConfigurableJoint candidate in forearm.GetComponents<ConfigurableJoint>())
                {
                    if (candidate == null) continue;
                    if (candidate.angularXMotion == ConfigurableJointMotion.Free)
                    {
                        Assert.That(candidate.xMotion == ConfigurableJointMotion.Free && candidate.yMotion == ConfigurableJointMotion.Free &&
                            candidate.zMotion == ConfigurableJointMotion.Free && candidate.angularYMotion == ConfigurableJointMotion.Free &&
                            candidate.angularZMotion == ConfigurableJointMotion.Free, Is.True, context + ": retired constraints must all be free.");
                        continue;
                    }
                    Assert.That(elbow, Is.Null, context + ": a retired elbow must stop constraining before deferred destruction.");
                    elbow = candidate;
                }
                Assert.That(elbow, Is.Not.Null, context);
                Vector3 axis = forearm.TransformDirection(elbow.axis).normalized;
                Vector3 hinge = Vector3.Cross(lowerDirection, upperDirection);
                Assert.That(Mathf.Abs(Vector3.Dot(axis, lowerDirection)), Is.LessThan(.001f), context + ": hinge cannot become pronation.");
                if (hinge.sqrMagnitude > .000001f)
                    Assert.That(Vector3.Dot(axis, hinge.normalized), Is.GreaterThan(.999f), context + ": the hinge follows the live elbow plane.");
                Assert.That(elbow.lowAngularXLimit.limit + flexion, Is.EqualTo(-5f).Within(.05f), context);
                Assert.That(elbow.highAngularXLimit.limit + flexion, Is.EqualTo(150f).Within(.05f), context);
                Assert.That(elbow.angularYLimit.limit, Is.EqualTo(8f).Within(.01f), context);
                Assert.That(elbow.angularZLimit.limit, Is.EqualTo(8f).Within(.01f), context);
            }
        }

        private void ApplyControlledImpact(CombatActor victim, Vector3 direction, float impulse)
        {
            CombatActor source = victim == root.Hero ? root.Opponent : root.Hero;
            Transform chest = FindAnatomicalBone(victim, "chest");
            Vector3 point = chest.position - direction.normalized * .10f + victim.transform.right * .10f;
            var impact = new CombatImpact(source, victim, ++diagnosticImpactSequence,
                point, -direction.normalized, direction.normalized, victim.State.Health, victim.State.Health - S.Damage,
                MeleeHitResult.Hit, new MeleeHitLocation(MeleeBodyRegion.Torso, MeleeHitSide.Front),
                impulse >= 100f ? 1f : 0f, Player3DAnatomicalPart.Torso, chest.InverseTransformPoint(point), 3f,
                direction.normalized * impulse);
            victim.ApplyImpactForDiagnostics(impact);
        }

        private void CaptureImpactRecoveryFrame(CombatActor victim, string subject, string name, Vector3 origin, Quaternion facing,
            bool weaponDetail = false)
        {
            Camera camera = root.CameraFollow.Camera;
            Vector3 oldPosition = camera.transform.position;
            Quaternion oldRotation = camera.transform.rotation;
            float oldFov = camera.fieldOfView;
            try
            {
                Vector3 focus = origin + Vector3.up * .70f;
                // The hero's former positive-X view put the arena box in front
                // of his fallen torso and knees. Both views now face clear floor.
                Vector3 eye = origin + facing * new Vector3(victim.IsHero ? -3.4f : 3.4f, 2.1f, 4.1f);
                if (weaponDetail)
                {
                    focus = (FindAnatomicalBone(victim, "chest").position +
                        FindAnatomicalBone(victim, "hand.R").position +
                        CombatAssetProvider.FindAnchor(victim.Weapon, "StrikeTip").position) / 3f;
                    eye = focus + victim.transform.rotation * new Vector3(victim.IsHero ? -1.3f : 1.3f, .45f, 1.5f);
                }
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
                camera.fieldOfView = weaponDetail ? 38f : 48f;
                string sceneFolder = SceneIds.CombatTest + "/impact-recovery/" + subject;
                string path = Path.Combine(Directory.GetCurrentDirectory(), "Captures", sceneFolder, name + ".png");
                LogAssert.Expect(LogType.Log, "Area capture wrote " + path);
                AreaCaptureFixture.CaptureCurrentCamera(camera, sceneFolder, name);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
                camera.fieldOfView = oldFov;
            }
        }

        private void CaptureImpactPlayerCamera(string subject, string name)
        {
            string sceneFolder = SceneIds.CombatTest + "/impact-recovery/" + subject;
            string path = Path.Combine(Directory.GetCurrentDirectory(), "Captures", sceneFolder, name + ".png");
            LogAssert.Expect(LogType.Log, "Area capture wrote " + path);
            AreaCaptureFixture.CaptureCurrentCamera(root.CameraFollow.Camera, sceneFolder, name);
        }

        private static void AppendImpactMeasurement(StringBuilder output, CombatActor victim, Transform pelvis, int frame,
            ImpactRecoveryObservation observation)
        {
            Vector3 point = victim.transform.position;
            Vector3 velocity = victim.ImpactMotion.Velocity;
            output.AppendFormat(CultureInfo.InvariantCulture, "{0},{1:F4},{2},{3},{4},{5},{6:F4},{7:F4},{8:F4},{9:F4},{10:F4},{11:F4},{12:F4},{13:F4},{14:F4},{15:F4},{16:F4}",
                frame, frame * ImpactFrameSeconds, victim.State.Phase, victim.IsKnockedDown,
                victim.Ragdoll.PhysicsController.IsSimulating, victim.Ragdoll.IsRecovering, victim.SupportGripWeight,
                point.x, point.y, point.z, pelvis.position.y, victim.ImpactMotion.BalanceLoad, victim.ImpactMotion.Age,
                velocity.magnitude, velocity.x, velocity.y, velocity.z);
            output.AppendFormat(CultureInfo.InvariantCulture,
                ",{0},{1:F4},{2},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10:F4},{11:F4},{12:F4},{13:F4},{14:F4},{15:F4},{16:F4},{17:F4},{18:F2},{19:F2},{20:F4},{21:F2},{22:F4},{23:F2},{24:F2},{25:F2}\n",
                observation.StageLabel.Replace(',', ';'), observation.ClipProgress, victim.SupportArmState,
                observation.HandGap, observation.SoleHeights[0], observation.SoleHeights[1], observation.LandTime,
                observation.SinceLand, observation.GroundStillSeconds, observation.RiseStillSeconds, observation.RegripSeconds,
                observation.PelvisStep, observation.MaximumJointStep, observation.CentralSpeed, observation.LegLengths[0], observation.LegLengths[1],
                observation.LegLengths[2], observation.LegLengths[3], observation.KneeBend[0], observation.KneeBend[1], observation.KneeSeparation,
                observation.RightWristBend, observation.RightGripGap, observation.ShaftAxisError,
                observation.RightWristDeviation, observation.LeftWristDeviation);
        }

        private static void WriteImpactMeasurements(string subject, StringBuilder measurements)
        {
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest, "impact-recovery", subject);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "motion.csv"), measurements.ToString());
        }

        /// <summary>Measures the displayed rig in metres, independently of clip names and pose tuning.</summary>
        private sealed class ImpactRecoveryObservation : System.IDisposable
        {
            private readonly CombatActor actor;
            private readonly NpcHandPose hands;
            private readonly Transform[] joints, legs;
            private readonly Transform rightForearm, leftForearm;
            private readonly Vector3[] previous, riseReference, groundReference;
            private readonly Quaternion[] riseRotationReference;
            private readonly float[] originalLegLengths = new float[4];
            private readonly Player3DFootGroundProbe soles;
            private readonly float kneeSideSign;
            private bool hadGround;

            public string StageLabel { get; private set; } = "none";
            public float ClipProgress { get; private set; }
            public float HandGap { get; private set; }
            public float[] SoleHeights { get; } = new[] { float.NaN, float.NaN };
            public float[] LegLengths { get; } = new float[4];
            public float[] KneeBend { get; } = new float[2];
            public float LandTime { get; private set; } = -1f;
            public float SinceLand { get; private set; } = -1f;
            public float GroundStillSeconds { get; private set; }
            public float RiseStillSeconds { get; private set; }
            public float RegripSeconds { get; private set; }
            public float PelvisStep { get; private set; }
            public float MaximumJointStep { get; private set; }
            public float CentralSpeed { get; private set; }
            public float KneeSeparation { get; private set; }
            public float RightWristBend { get; private set; }
            public float LeftWristBend { get; private set; }
            public float RightWristDeviation { get; private set; }
            public float LeftWristDeviation { get; private set; }
            public float RightGripGap { get; private set; }
            public float ShaftAxisError { get; private set; }

            public ImpactRecoveryObservation(CombatActor actor)
            {
                this.actor = actor;
                hands = actor.GetComponentInChildren<NpcHandPose>();
                rightForearm = FindAnatomicalBone(actor, "forearm.R");
                leftForearm = FindAnatomicalBone(actor, "forearm.L");
                string[] names = { "pelvis", "chest", "head", "hand.L", "hand.R", "thigh.L", "thigh.R",
                    "shin.L", "shin.R", "foot.L", "foot.R" };
                joints = new Transform[names.Length];
                for (int i = 0; i < names.Length; i++) joints[i] = FindAnatomicalBone(actor, names[i]);
                legs = new[] { joints[5], joints[7], joints[9], joints[6], joints[8], joints[10] };
                kneeSideSign = Mathf.Sign(Vector3.Dot(legs[5].position - legs[2].position, actor.transform.right));
                previous = new Vector3[joints.Length];
                riseReference = new Vector3[joints.Length];
                groundReference = new Vector3[joints.Length];
                riseRotationReference = new Quaternion[joints.Length];
                Remember(previous);
                Remember(riseReference, riseRotationReference);
                Remember(groundReference);
                MeasureLegs();
                for (int i = 0; i < originalLegLengths.Length; i++) originalLegLengths[i] = LegLengths[i];

                Player3DAssetRegistry registry = actor.DamageRigRoot.GetComponentInParent<Player3DAssetRegistry>();
                if (registry != null) soles = Player3DFootGroundProbe.CreateForHero(registry, actor.transform);
                else
                {
                    var left = new List<SkinnedMeshRenderer>();
                    var right = new List<SkinnedMeshRenderer>();
                    foreach (SkinnedMeshRenderer skin in actor.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        if (skin.name.EndsWith("Sole.L", System.StringComparison.Ordinal)) left.Add(skin);
                        if (skin.name.EndsWith("Sole.R", System.StringComparison.Ordinal)) right.Add(skin);
                    }
                    soles = Player3DFootGroundProbe.Create(left, right, actor.transform);
                }
            }

            public string Sample(float seconds)
            {
                bool recovering = actor.Ragdoll.IsRecovering;
                object pose = ReadImpactMember(actor, "knockdownPose");
                StageLabel = ReadImpactMember(pose, "StageLabel")?.ToString() ?? actor.State.Phase.ToString();
                if (recovering) StageLabel = ReadImpactMember(pose, "ClipName") + "/" + StageLabel;
                ClipProgress = ReadImpactMember(pose, "ClipProgress") is float progress ? progress : float.NaN;
                HandGap = Vector3.Distance(hands.CylinderCentre(true), actor.SupportGripWorldPosition);
                Vector3 shaft = hands.CylinderAxis(false);
                Vector3 fingers = Vector3.ProjectOnPlane(hands.CylinderCentre(false) - joints[4].position, hands.PalmNormal(false)).normalized;
                RightWristBend = Vector3.Angle(joints[4].position - rightForearm.position, fingers);
                LeftWristBend = Vector3.Angle(joints[3].position - leftForearm.position,
                    Vector3.ProjectOnPlane(hands.CylinderCentre(true) - joints[3].position, hands.PalmNormal(true)));
                RightWristDeviation = WristDeviation(joints[4], rightForearm, false);
                LeftWristDeviation = WristDeviation(joints[3], leftForearm, true);
                RightGripGap = Vector3.Distance(hands.CylinderCentre(false), actor.Weapon.transform.position);
                ShaftAxisError = Vector3.Angle(shaft, actor.Weapon.transform.up);
                RegripSeconds = actor.SupportArmState == CombatArmSupportState.Regripping
                    ? RegripSeconds + ImpactFrameSeconds : 0f;
                bool ground = actor.Ragdoll.HasGroundContact;
                if (ground && !hadGround) LandTime = seconds;
                hadGround = ground;
                SinceLand = ground && LandTime >= 0f ? seconds - LandTime : -1f;
                MaximumJointStep = CentralSpeed = 0f;
                string failure = null;
                if (RightGripGap > .005f || ShaftAxisError > 1f)
                    failure = "the crowbar axis or grip centre leaves the actual closed right palm";
                if (((recovering && ClipProgress >= .15f) || (!actor.IsKnockedDown && LandTime >= 0f)) && RightWristBend > 30f)
                    failure ??= "the weapon is held with an excessive right wrist bend: " + RightWristBend.ToString("F1");
                if (((recovering && ClipProgress >= .15f) || (!actor.IsKnockedDown && LandTime >= 0f)) && RightWristDeviation > 25.1f)
                    failure ??= "the right wrist bends sideways excessively: " + RightWristDeviation.ToString("F1");
                for (int i = 0; i < joints.Length; i++)
                {
                    if (!IsFiniteImpactVector(joints[i].position)) failure ??= joints[i].name + " has a non-finite position";
                    float travel = Vector3.Distance(previous[i], joints[i].position);
                    if (i == 0) PelvisStep = travel;
                    if (i < 3) CentralSpeed = Mathf.Max(CentralSpeed, travel / ImpactFrameSeconds);
                    MaximumJointStep = Mathf.Max(MaximumJointStep, travel);
                    if (recovering && travel > (i == 0 ? .08f : .16f))
                        failure ??= joints[i].name + " jumps " + travel.ToString("F3", CultureInfo.InvariantCulture) +
                            " m in one recovery frame; rise must move continuously";
                }
                Remember(previous);
                MeasureLegs();
                KneeSeparation = Vector3.Dot(joints[8].position - joints[7].position, actor.transform.right) * kneeSideSign;
                if (recovering && ClipProgress >= .15f && KneeSeparation < .03f)
                    failure ??= "the recovery crosses its knees while its feet stay in separate support corridors";
                for (int i = 0; i < LegLengths.Length; i++)
                    if (recovering && Mathf.Abs(LegLengths[i] - originalLegLengths[i]) > Mathf.Max(.01f, originalLegLengths[i] * .03f))
                        failure ??= "recovery stretches leg segment " + i + " from " + originalLegLengths[i].ToString("F3") +
                            " m to " + LegLengths[i].ToString("F3") + " m";

                // Meaningful movement is measured against a retained pose, not
                // per-frame speed: slow continuous travel must not look frozen.
                // A dangling hand cannot excuse a torso lying still after landing.
                bool bodyMoved = Moved(groundReference, null, 3);
                if (!ground || recovering || bodyMoved)
                { GroundStillSeconds = 0f; Remember(groundReference); }
                else GroundStillSeconds += ImpactFrameSeconds;
                bool poseMoved = Moved(riseReference, riseRotationReference, joints.Length);
                if (!recovering || poseMoved)
                { RiseStillSeconds = 0f; Remember(riseReference, riseRotationReference); }
                else RiseStillSeconds += ImpactFrameSeconds;
                if (GroundStillSeconds > 1.25f)
                    failure ??= "the landed torso waits motionless for more than 1.25 seconds before getting up";
                if (RiseStillSeconds > .6f)
                    failure ??= "the visible recovery pose freezes for more than 0.6 seconds";
                if (RegripSeconds > 1.5f)
                    failure ??= "the supporting hand cannot complete an uninterrupted regrip within 1.5 seconds";
                if (!actor.IsKnockedDown && LandTime >= 0f && actor.SupportGripWeight >= .99f && LeftWristBend > 40f)
                    failure ??= "the regripped weapon bends the left wrist excessively: " + LeftWristBend.ToString("F1");
                if (!actor.IsKnockedDown && LandTime >= 0f && actor.SupportGripWeight >= .99f && LeftWristDeviation > 25.1f)
                    failure ??= "the regripped left wrist bends sideways excessively: " + LeftWristDeviation.ToString("F1");
                if (actor.SupportArmState == CombatArmSupportState.SupportingWeapon && HandGap > .035f)
                    failure ??= "the hand claims weapon support while its palm is more than 3.5 cm from the shaft";

                bool claimsFeet = ReadImpactMember(pose, "FeetSupported") is bool supported && supported;
                bool completed = ReadImpactMember(pose, "IsComplete") is bool complete && complete;
                bool standingAgain = !actor.IsKnockedDown && LandTime >= 0f;
                for (int side = 0; side < 2; side++)
                {
                    Transform foot = legs[side * 3 + 2];
                    float floor = float.NaN;
                    bool found = soles.TryGetSoleHeight((FootSide)side, out float sole) &&
                        soles.TryProbeActorGround(foot.position, out floor, out _);
                    SoleHeights[side] = found ? sole - floor : float.NaN;
                    if (recovering && found && SoleHeights[side] < -.04f)
                        failure ??= "boot " + side + " passes through the floor during the recovery transition";
                    if ((claimsFeet || completed || standingAgain) &&
                        (!found || SoleHeights[side] < -.04f || SoleHeights[side] > .06f))
                        failure ??= "standing support is claimed before boot " + side + " is planted on its actual floor";
                }
                return failure;
            }

            private float WristDeviation(Transform hand, Transform forearm, bool left)
            {
                Vector3 palm = hands.PalmNormal(left);
                Vector3 fingers = Vector3.ProjectOnPlane(hands.CylinderCentre(left) - hand.position, palm).normalized;
                Vector3 across = Vector3.Cross(palm, fingers).normalized;
                Vector3 direction = (hand.position - forearm.position).normalized;
                return Mathf.Abs(Mathf.Asin(Mathf.Clamp(Vector3.Dot(direction, across), -1f, 1f)) * Mathf.Rad2Deg);
            }

            private void MeasureLegs()
            {
                for (int side = 0; side < 2; side++)
                {
                    Vector3 thigh = legs[side * 3 + 1].position - legs[side * 3].position;
                    Vector3 shin = legs[side * 3 + 2].position - legs[side * 3 + 1].position;
                    LegLengths[side * 2] = thigh.magnitude;
                    LegLengths[side * 2 + 1] = shin.magnitude;
                    // Record flexion for visual diagnosis. Do not infer a hinge
                    // sign from world axes while the whole fallen rig is turned.
                    KneeBend[side] = Vector3.Angle(thigh, shin);
                }
            }

            private bool Moved(Vector3[] positions, Quaternion[] rotations, int count)
            {
                for (int i = 0; i < count; i++)
                    if (Vector3.Distance(positions[i], joints[i].position) > .012f ||
                        (rotations != null && Quaternion.Angle(rotations[i], joints[i].rotation) > 1.25f)) return true;
                return false;
            }

            private void Remember(Vector3[] positions, Quaternion[] rotations = null)
            {
                for (int i = 0; i < joints.Length; i++)
                {
                    positions[i] = joints[i].position;
                    if (rotations != null) rotations[i] = joints[i].rotation;
                }
            }

            public void Dispose() => soles.Dispose();
        }

        private static object ReadImpactMember(object target, string name)
        {
            if (target == null) return null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            System.Type type = target.GetType();
            return type.GetProperty(name, flags)?.GetValue(target) ?? type.GetField(name, flags)?.GetValue(target);
        }

        private static object InvokeImpactProbe(object target, string name, params object[] arguments)
        {
            if (target == null) return null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (MethodInfo method in target.GetType().GetMethods(flags))
                if (method.Name == name && method.GetParameters().Length == arguments.Length)
                    return method.Invoke(target, arguments);
            return null;
        }

        private static string DescribeImpactRecoveryGate(CombatActor victim)
        {
            object pose = ReadImpactMember(victim, "knockdownPose");
            var grip = ReadImpactMember(victim, "supportGrip") as CombatSupportGrip;
            object feet = ReadImpactMember(pose, "FeetSupported") ?? InvokeImpactProbe(pose, "FeetSupported");
            bool? reach = null;
            if (grip != null)
            {
                object[] contact = { default(Pose) };
                bool found = InvokeImpactProbe(grip, "TryContact", contact) is bool valid && valid;
                reach = found && InvokeImpactProbe(grip, "CanReach", contact) is bool reachable && reachable;
            }
            // Queries only, and only after a failure: no advancing the rise,
            // changing its support or adding regular collision-probe overhead.
            return
                $"phase={victim.State.Phase} load={victim.ImpactMotion.BalanceLoad:F3} age={victim.ImpactMotion.Age:F3} speed={victim.ImpactMotion.Velocity.magnitude:F3} " +
                $"fall={victim.Ragdoll.SimulationSeconds:F2} settled={victim.Ragdoll.IsSettled} ground={victim.Ragdoll.HasGroundContact} frozen={ReadImpactMember(victim, "knockdownFrozen")} " +
                $"rise={ReadImpactMember(pose, "StageLabel") ?? "none"}@{ReadImpactMember(pose, "ClipProgress"):F2} poseBegun={ReadImpactMember(victim, "recoveryPoseBegun")} advance={ReadImpactMember(pose, "CanAdvance")} feet={feet} " +
                $"complete={ReadImpactMember(pose, "IsComplete")} handsReleased={ReadImpactMember(pose, "HandsReleased")} clearance={InvokeImpactProbe(pose, "HasStandingClearance")} " +
                $"supportReason={ReadImpactMember(pose, "SupportReason")} supportDistance={ReadImpactMember(pose, "SupportDistance"):F3} armReach={ReadImpactMember(pose, "ArmReach"):F3} " +
                $"arm={victim.SupportArmState} recoveryOwned={grip?.IsRecoveryOwned} regripAllowed={ReadImpactMember(grip, "regripAllowed")} " +
                $"hold={ReadImpactMember(grip, "releaseHold"):F2} stable={ReadImpactMember(grip, "stableElapsed"):F2} close={ReadImpactMember(grip, "closeElapsed"):F2} reach={reach} grip={victim.SupportGripWeight:F3}";
        }

        private static bool IsFiniteImpactVector(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }
}
