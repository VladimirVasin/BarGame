using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        private readonly List<string> pistolGeometryIssues = new List<string>();

        [UnityTest]
        public IEnumerator Range_UnfocusedPistolCarryKeepsBothArmsWalkingAndCanAim()
        {
            var input = new InputTestFixture();
            Keyboard keyboard = null;
            Mouse mouse = null;
            PistolCameraContinuityProbe probe = null;
            try
            {
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                mouse = InputSystem.AddDevice<Mouse>();
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                Assert.That(CombatTestStartService.TryStart(CombatWeaponId.Pistol), Is.True);
                yield return AwaitSelectedCombatRange();
                PlacePair(8f);
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                Assert.That(root.SetOpponentFocus(false), Is.True);
                root.AutomaticSimulation = true;
                for (int frame = 0; frame < 30; frame++) yield return null;
                yield return CapturePistolNearViews("ordinary-idle");
                AssertPistolOrdinaryIdle();

                var visual = (Player3DCharacterPresentation)root.Player.Visual;
                var hands = root.Hero.GetComponentInChildren<NpcHandPose>();
                input.Press(keyboard.wKey, queueEventOnly: true);
                for (int frame = 0; frame < 18; frame++) yield return null;
                Assert.That(visual.CurrentLocomotionState, Is.EqualTo(Player3DLocomotionState.Walk),
                    WalkingDiagnostic("Pistol W without focus", root.Hero.transform.position));
                Vector3 start = root.Hero.transform.position;
                Transform left = CityPedestrianHandProps.FindSocket(root.Hero.DamageRigRoot, "upper_arm.L");
                Transform right = CityPedestrianHandProps.FindSocket(root.Hero.DamageRigRoot, "upper_arm.R");
                Assert.That(left != null && right != null, Is.True);
                Quaternion leftStart = left.localRotation, rightStart = right.localRotation;
                float leftSwing = 0f, rightSwing = 0f, gripError = 0f;
                int samples = 0;
                bool ordinaryWalk = true;
                var legs = new WalkingLegProbe(root.Hero);
                probe = root.gameObject.AddComponent<PistolCameraContinuityProbe>();
                probe.Sample = () =>
                {
                    samples++;
                    leftSwing = Mathf.Max(leftSwing, Quaternion.Angle(leftStart, left.localRotation));
                    rightSwing = Mathf.Max(rightSwing, Quaternion.Angle(rightStart, right.localRotation));
                    gripError = Mathf.Max(gripError,
                        Vector3.Distance(root.Hero.Weapon.transform.position, hands.CylinderCentre(false)));
                    ordinaryWalk &= !visual.OwnsClip(root.Hero) && visual.OwnsCarryPose(root.Hero) &&
                        !root.Hero.Pistol.AimRequested && hands.LeftGripWeight == 0f && hands.RightGripWeight > .99f;
                    legs.Sample();
                };
                for (int frame = 0; frame < 48; frame++)
                {
                    yield return null;
                    if (frame == 12 || frame == 30)
                        yield return CaptureFocusGameView("pistol-free-walk-" + frame);
                }
                probe.Sample = null;
                Assert.That(samples, Is.GreaterThanOrEqualTo(40));
                Assert.That(ordinaryWalk, Is.True, "Free walking must keep the ordinary gait and a one-handed grip.");
                Assert.That(leftSwing, Is.GreaterThan(2f), "The left arm must swing through the walking cycle.");
                Assert.That(rightSwing, Is.GreaterThan(2f), "The armed right arm must swing through the walking cycle.");
                Assert.That(gripError, Is.LessThan(.001f), "The moving palm must retain the pistol every completed frame.");
                Assert.That(Vector3.Distance(start, root.Hero.transform.position), Is.GreaterThan(.25f));
                legs.AssertMoving(2f, "The same walk must animate the legs and both arms.");

                input.Release(keyboard.wKey, queueEventOnly: true);
                for (int frame = 0; frame < 40; frame++) yield return null;
                AssertPistolOrdinaryIdle();

                var transitionFrames = new List<PistolTransitionFrame>();
                var armBones = new Transform[6];
                string[] armNames = { "upper_arm.L", "forearm.L", "hand.L", "upper_arm.R", "forearm.R", "hand.R" };
                for (int index = 0; index < armBones.Length; index++)
                {
                    armBones[index] = CityPedestrianHandProps.FindSocket(root.Hero.DamageRigRoot, armNames[index]);
                    Assert.That(armBones[index], Is.Not.Null, armNames[index]);
                }
                probe.Sample = () => transitionFrames.Add(ReadPistolTransitionFrame(armBones, hands));
                yield return null;
                PistolTransitionFrame idle = transitionFrames[transitionFrames.Count - 1];
                int shotsBeforeRaise = root.Hero.Pistol.ShotSequence;
                int queuedInputFrame = Time.frameCount;
                input.Press(mouse.rightButton, queueEventOnly: true);
                yield return WaitFor(() => probe.CompletedFrame > queuedInputFrame,
                    "The queued aim input needs a completed gameplay frame.");
                Assert.That(root.Hero.Pistol.AimRequested, Is.True,
                    $"Completed aim input: held={mouse.rightButton.isPressed}, body={root.Hero.PistolBodyAvailable}, " +
                    $"gameplay={GameInput.CanRead(GameInputContext.Gameplay)}, freeCamera={root.CameraFollow.FreeAimActive}.");
                queuedInputFrame = Time.frameCount;
                input.Press(mouse.leftButton, queueEventOnly: true);
                yield return WaitFor(() => probe.CompletedFrame > queuedInputFrame,
                    "The early trigger needs a completed gameplay frame.");
                input.Release(mouse.leftButton, queueEventOnly: true);
                Assert.That(root.Hero.Pistol.ShotSequence, Is.EqualTo(shotsBeforeRaise),
                    "An early raise trigger must be rejected rather than deferred.");
                Assert.That(root.PistolCrosshairExpansion, Is.Zero,
                    "A rejected raise trigger cannot animate the crosshair.");
                for (int frame = 0; frame < 39; frame++)
                {
                    yield return null;
                    if (frame == 5) yield return CaptureFocusGameView("pistol-aim-enter-middle");
                }
                Assert.That(root.Hero.Pistol.IsAiming && root.CameraFollow.FreeAimActive, Is.True);
                Assert.That(visual.OwnsClip(root.Hero), Is.True);
                Assert.That(visual.OwnsCarryPose(root.Hero), Is.False);
                Assert.That(root.Hero.PistolSupportError, Is.LessThan(.02f));
                Assert.That(root.Hero.PistolVisualAimProgress, Is.EqualTo(1f).Within(.0001f));
                PistolTransitionFrame aimed = transitionFrames[transitionFrames.Count - 1];
                AssertPistolTransitionFrames(transitionFrames, armNames, "Idle to aim");
                AssertPistolCameraHasIntermediateFrames(transitionFrames, idle.Camera, aimed.Camera, "Idle to aim");
                yield return CaptureFocusGameView("pistol-free-walk-to-aim");

                transitionFrames.Clear();
                yield return null;
                input.Release(mouse.rightButton, queueEventOnly: true);
                for (int frame = 0; frame < 60; frame++)
                {
                    yield return null;
                    if (frame == 6) yield return CaptureFocusGameView("pistol-aim-exit-middle");
                }
                AssertPistolTransitionFrames(transitionFrames, armNames, "Aim to idle");
                AssertPistolCameraHasIntermediateFrames(transitionFrames, aimed.Camera,
                    transitionFrames[transitionFrames.Count - 1].Camera, "Aim to idle");
                Assert.That(root.Hero.PistolVisualAimProgress, Is.Zero);

                // A tap never reaches aim, and reversing an unfinished lower
                // must retain the last rendered camera, joints and grip weight.
                transitionFrames.Clear();
                yield return null;
                input.Press(mouse.rightButton, queueEventOnly: true);
                for (int frame = 0; frame < 2; frame++) yield return null;
                Assert.That(root.Hero.PistolVisualAimProgress, Is.InRange(.01f, .99f));
                input.Release(mouse.rightButton, queueEventOnly: true);
                for (int frame = 0; frame < 40; frame++) yield return null;
                AssertPistolTransitionFrames(transitionFrames, armNames, "Short aim tap");
                AssertPistolOrdinaryIdle();

                transitionFrames.Clear();
                yield return null;
                input.Press(mouse.rightButton, queueEventOnly: true);
                for (int frame = 0; frame < 6; frame++) yield return null;
                Assert.That(root.Hero.PistolVisualAimProgress, Is.InRange(.1f, .9f));
                input.Release(mouse.rightButton, queueEventOnly: true);
                for (int frame = 0; frame < 3; frame++) yield return null;
                Assert.That(root.Hero.PistolVisualAimProgress, Is.InRange(.01f, .9f));
                input.Press(mouse.rightButton, queueEventOnly: true);
                for (int frame = 0; frame < 40; frame++) yield return null;
                AssertPistolTransitionFrames(transitionFrames, armNames, "Reverse unfinished aim");
                Assert.That(root.Hero.PistolVisualAimProgress, Is.EqualTo(1f).Within(.0001f));
                Assert.That(hands.LeftGripWeight, Is.EqualTo(CombatPistolAssetProvider.SupportGripWeight).Within(.001f));
                Assert.That(root.Hero.PistolSupportError, Is.LessThan(.02f));
                AssertPistolCameraShotReady("Reversed aim endpoint");
                probe.Sample = null;

                // Keep the verified aim and move the passive target aside so
                // accepted shots exercise the HUD without a body hit-stop.
                root.Opponent.ResetActor(root.Opponent.transform.position + Vector3.right * 4f,
                    root.Opponent.transform.forward);
                for (int frame = 0; frame < 30; frame++) yield return null;
                AssertPistolCameraShotReady("Crosshair shot");
                Assert.That(root.PistolCrosshairExpansion, Is.Zero);
                yield return CaptureFocusGameView("pistol-crosshair-rest");
                int shots = root.Hero.Pistol.ShotSequence;
                queuedInputFrame = Time.frameCount;
                input.Press(mouse.leftButton, queueEventOnly: true);
                yield return WaitFor(() => probe.CompletedFrame > queuedInputFrame,
                    "The accepted trigger needs a completed gameplay frame.");
                input.Release(mouse.leftButton, queueEventOnly: true);
                Assert.That(root.Hero.Pistol.ShotSequence, Is.EqualTo(shots + 1));
                Assert.That(root.PistolCrosshairExpansion, Is.GreaterThan(0f),
                    "A committed shot must start its crosshair impulse in the same live frame.");
                float pausedExpansion = root.PistolCrosshairExpansion;
                Assert.That(root.PauseMenu.Open(), Is.True);
                root.Tick(.8f);
                for (int frame = 0; frame < 3; frame++) yield return null;
                Assert.That(root.PistolCrosshairExpansion, Is.EqualTo(pausedExpansion).Within(.0001f),
                    "Pause must hold the impulse clock while its HUD is hidden.");
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause did not release the crosshair impulse.");
                yield return null;
                Assert.That(root.PistolCrosshairExpansion, Is.GreaterThan(0f), "The held impulse must resume with aiming after pause.");
                root.AutomaticSimulation = false;
                yield return CaptureFocusGameView("pistol-crosshair-shot-impulse");
                root.AutomaticSimulation = true;
                for (int frame = 0; frame < 30; frame++) yield return null;
                Assert.That(root.PistolCrosshairExpansion, Is.Zero, "The crosshair must settle back to its original size.");
                Assert.That(root.Hero.Pistol.ShotSequence, Is.EqualTo(shots + 1), "Holding a button cannot replay its trigger.");
                yield return CaptureFocusGameView("pistol-crosshair-recovered");
                queuedInputFrame = Time.frameCount;
                input.Press(mouse.leftButton, queueEventOnly: true);
                yield return WaitFor(() => probe.CompletedFrame > queuedInputFrame,
                    "The second trigger needs a completed gameplay frame.");
                queuedInputFrame = Time.frameCount;
                input.Release(mouse.leftButton, queueEventOnly: true);
                Assert.That(root.Hero.Pistol.ShotSequence, Is.EqualTo(shots + 2));
                yield return WaitFor(() => probe.CompletedFrame > queuedInputFrame,
                    "The trigger release must be consumed before releasing the other mouse button.");
                Assert.That(root.PistolCrosshairExpansion, Is.GreaterThan(0f));
                queuedInputFrame = Time.frameCount;
                input.Release(mouse.rightButton, queueEventOnly: true);
                yield return WaitFor(() => probe.CompletedFrame > queuedInputFrame,
                    "Releasing aim needs a completed gameplay frame.");
                Assert.That(root.PistolCrosshairExpansion, Is.Zero, "Hiding aim must clear a live impulse immediately.");
                for (int frame = 0; frame < 60; frame++) yield return null;
                yield return CapturePistolNearViews("ordinary-idle-after-aim");
                AssertPistolOrdinaryIdle();
            }
            finally
            {
                if (root != null) root.AutomaticSimulation = false;
                if (probe != null) Object.DestroyImmediate(probe);
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                GameInput.HandleApplicationFocus(true);
                input.TearDown();
            }
        }

        private PistolTransitionFrame ReadPistolTransitionFrame(Transform[] armBones, NpcHandPose hands)
        {
            var rotations = new Quaternion[armBones.Length];
            for (int index = 0; index < armBones.Length; index++) rotations[index] = armBones[index].localRotation;
            Ray centre = root.CameraFollow.Camera.ViewportPointToRay(new Vector3(.5f, .5f, 0f));
            return new PistolTransitionFrame
            {
                Camera = ReadPistolCameraFrame(), Seconds = Time.deltaTime,
                ArmRotations = rotations, LeftHand = armBones[2].position, RightHand = armBones[5].position,
                LeftGrip = hands.LeftGripWeight, VisualAim = root.Hero.PistolVisualAimProgress,
                PalmError = Vector3.Distance(root.Hero.Weapon.transform.position, hands.CylinderCentre(false)),
                CentreRayError = Vector3.Angle(centre.direction, root.Hero.PistolAimPoint - centre.origin)
            };
        }

        private static void AssertPistolTransitionFrames(List<PistolTransitionFrame> frames, string[] armNames, string context)
        {
            Assert.That(frames.Count, Is.GreaterThan(2), context + " needs completed LateUpdate samples.");
            string reportDirectory = Path.Combine(Directory.GetCurrentDirectory(), "TestResults");
            Directory.CreateDirectory(reportDirectory);
            using (var report = new StreamWriter(Path.Combine(reportDirectory, "pistol-" + context.Replace(' ', '-') + ".csv")))
            {
                report.WriteLine("frame,seconds,visualAim,leftGrip,cameraStep,leftWristStep,rightWristStep,leftUpperAngle,leftForearmAngle,leftHandAngle,rightUpperAngle,rightForearmAngle,rightHandAngle,cameraX,cameraY,cameraZ,cameraAngle,fieldOfView");
                for (int index = 1; index < frames.Count; index++)
                {
                    PistolTransitionFrame previous = frames[index - 1], current = frames[index];
                    report.Write(System.FormattableString.Invariant($"{current.Camera.Frame},{current.Seconds:F6},{current.VisualAim:F6},{current.LeftGrip:F6},{Vector3.Distance(current.Camera.Position, previous.Camera.Position):F6},{Vector3.Distance(current.LeftHand, previous.LeftHand):F6},{Vector3.Distance(current.RightHand, previous.RightHand):F6}"));
                    for (int bone = 0; bone < armNames.Length; bone++)
                        report.Write(System.FormattableString.Invariant($",{Quaternion.Angle(current.ArmRotations[bone], previous.ArmRotations[bone]):F6}"));
                    report.Write(System.FormattableString.Invariant($",{current.Camera.Position.x:F6},{current.Camera.Position.y:F6},{current.Camera.Position.z:F6},{Quaternion.Angle(current.Camera.Rotation, previous.Camera.Rotation):F6},{current.Camera.FieldOfView:F6}"));
                    report.WriteLine();
                }
            }
            for (int index = 1; index < frames.Count; index++)
            {
                PistolTransitionFrame previous = frames[index - 1], current = frames[index];
                float seconds = Mathf.Max(CombatTestRoot.SimulationStep, current.Seconds);
                string at = context + ", completed frame " + current.Camera.Frame;
                Assert.That(current.Camera.Camera, Is.SameAs(previous.Camera.Camera), at);
                Assert.That(current.Camera.FollowEnabled, Is.True, at);
                Assert.That(Vector3.Distance(current.Camera.Position, previous.Camera.Position), Is.LessThan(.01f + 18f * seconds),
                    at + ": camera position must travel rather than snap.");
                Assert.That(Quaternion.Angle(current.Camera.Rotation, previous.Camera.Rotation), Is.LessThan(.1f + 150f * seconds), at);
                Assert.That(Mathf.Abs(current.Camera.FieldOfView - previous.Camera.FieldOfView), Is.LessThan(.01f + 100f * seconds),
                    at + ": FOV must blend with the camera.");
                for (int bone = 0; bone < armNames.Length; bone++)
                    Assert.That(Quaternion.Angle(current.ArmRotations[bone], previous.ArmRotations[bone]),
                        Is.LessThan(.5f + 1100f * seconds), at + ": " + armNames[bone] + " must retain the visible pose.");
                Assert.That(Vector3.Distance(current.LeftHand, previous.LeftHand), Is.LessThan(.005f + 8f * seconds), at + ": left wrist.");
                Assert.That(Vector3.Distance(current.RightHand, previous.RightHand), Is.LessThan(.005f + 8f * seconds), at + ": right wrist.");
                Assert.That(Mathf.Abs(current.LeftGrip - previous.LeftGrip), Is.LessThan(.001f + 14f * seconds), at + ": left grip.");
                Assert.That(Mathf.Abs(current.VisualAim - previous.VisualAim), Is.LessThan(.001f + 8f * seconds),
                    at + ": reversing cannot restart the visible raise or lower.");
                Assert.That(current.PalmError, Is.LessThan(.001f), at + ": the pistol must remain in the right palm.");
                if (current.Camera.FreeAim && current.Camera.AimRequested)
                    Assert.That(current.CentreRayError, Is.LessThan(.05f),
                        at + ": the aim point must use the displayed intermediate camera ray.");
            }
        }

        private static void AssertPistolCameraHasIntermediateFrames(List<PistolTransitionFrame> frames,
            PistolCameraFrame start, PistolCameraFrame end, string context)
        {
            Assert.That(Vector3.Distance(start.Position, end.Position), Is.GreaterThan(.1f), context + " must change the framing.");
            int intermediate = 0;
            foreach (PistolTransitionFrame frame in frames)
                if (Vector3.Distance(frame.Camera.Position, start.Position) > .01f &&
                    Vector3.Distance(frame.Camera.Position, end.Position) > .01f) intermediate++;
            Assert.That(intermediate, Is.GreaterThan(2), context + " must show several intermediate camera positions.");
        }

        private struct PistolTransitionFrame
        {
            public PistolCameraFrame Camera;
            public float Seconds, LeftGrip, VisualAim, PalmError, CentreRayError;
            public Quaternion[] ArmRotations;
            public Vector3 LeftHand, RightHand;
        }

        [UnityTest]
        public IEnumerator Range_PistolWinningAndCorpseShotsKeepTheLiveFreeAimCamera()
        {
            var input = new InputTestFixture();
            Keyboard keyboard = null;
            Mouse mouse = null;
            PistolCameraContinuityProbe probe = null;
            try
            {
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                mouse = InputSystem.AddDevice<Mouse>();
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                Assert.That(CombatTestStartService.TryStart(CombatWeaponId.Pistol), Is.True);
                yield return AwaitSelectedCombatRange();
                PlacePair(6f);
                root.SendMessage("OnApplicationFocus", true);
                Assert.That(root.SetOpponentFocus(false), Is.True);
                root.AutomaticSimulation = true;
                input.Press(mouse.rightButton, queueEventOnly: true);
                yield return null;

                Collider head = FindPistolCameraTarget(Player3DAnatomicalPart.Head);
                yield return AimPistolCameraAt(input, mouse, head);
                AssertPistolCameraShotReady("Standing head");
                Assert.That(root.CameraFollow.FreeAimActive, Is.True);

                var frames = new List<PistolCameraFrame>();
                probe = root.gameObject.AddComponent<PistolCameraContinuityProbe>();
                probe.Sample = () => frames.Add(ReadPistolCameraFrame());
                yield return null;
                yield return CaptureFocusGameView("pistol-camera-winning-aim");
                yield return FirePistolAndCheckContinuousCamera(input, mouse, frames, "Winning head shot");
                Assert.That(root.RoundFinished && root.Opponent.State.IsDefeated, Is.True);
                Assert.That(root.Opponent.LastImpact.Location.Region, Is.EqualTo(MeleeBodyRegion.Head));
                yield return CaptureFocusGameView("pistol-camera-winning-contact");

                // Keep RMB held throughout the finished-round freeze and the
                // body's settling. No manual Tick or camera Snap hides a frame.
                for (int step = 0; step < 250 && !root.Opponent.Ragdoll.IsSettled; step++)
                    yield return new WaitForFixedUpdate();
                Assert.That(root.Opponent.Ragdoll.IsSettled, Is.True);
                // Input System is configured for dynamic updates, so queue
                // mouse events from that phase after the fixed-step wait.
                yield return null;
                Collider torso = FindPistolCameraTarget(Player3DAnatomicalPart.Torso);
                Vector3 corpsePoint = torso.transform.TransformPoint(((BoxCollider)torso).center);
                // The regression is the camera lease during the real corpse
                // contact/freeze. Inject a physical bullet above the settled
                // torso, avoiding unrelated downward arm-aim reach limits.
                yield return FirePistolAndCheckContinuousCamera(input, mouse, frames, "Corpse shot", corpsePoint);
                Assert.That(root.RoundFinished && root.Opponent.State.IsDefeated, Is.True);
                Assert.That(root.Opponent.State.Health, Is.Zero);
                Assert.That(root.Opponent.LastImpact.HealthBefore, Is.Zero);
                yield return CaptureFocusGameView("pistol-camera-corpse-contact");
            }
            finally
            {
                if (root != null) root.AutomaticSimulation = false;
                if (probe != null) Object.DestroyImmediate(probe);
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
            }
        }

        private Collider FindPistolCameraTarget(Player3DAnatomicalPart part)
        {
            foreach (var shape in root.Opponent.Ragdoll.PhysicsController.AnatomicalColliders)
                if (shape.Value == part) return shape.Key;
            Assert.Fail("The camera regression requires the live opponent's " + part + " collider.");
            return null;
        }

        private IEnumerator AimPistolCameraAt(InputTestFixture input, Mouse mouse, Collider target)
        {
            Camera camera = root.CameraFollow.Camera;
            // The shoulder moves around the root as yaw changes, so converge
            // using actual input and the next completed camera pose each time.
            for (int frame = 0; frame < 12; frame++)
            {
                Vector3 localCenter = target is CapsuleCollider capsule ? capsule.center :
                    target is BoxCollider box ? box.center : ((SphereCollider)target).center;
                Vector3 centre = target.transform.TransformPoint(localCenter);
                Vector3 angles = Quaternion.LookRotation(centre - camera.transform.position, Vector3.up).eulerAngles;
                float yaw = Mathf.DeltaAngle(camera.transform.eulerAngles.y, angles.y);
                float pitch = Mathf.DeltaAngle(0f, camera.transform.eulerAngles.x);
                float desiredPitch = Mathf.DeltaAngle(0f, angles.x);
                input.Set(mouse.delta, new Vector2(yaw / .16f, (pitch - desiredPitch) / .14f), queueEventOnly: true);
                yield return null;
            }
            input.Set(mouse.delta, Vector2.zero, queueEventOnly: true);
            for (int frame = 0; frame < 40; frame++) yield return null;
        }

        private void AssertPistolCameraShotReady(string context)
        {
            Assert.That(root.Hero.Pistol.CanFire && root.Hero.PistolAimAligned, Is.True,
                $"{context}: canFire={root.Hero.Pistol.CanFire}, aim={root.Hero.Pistol.AimRequested}, " +
                $"raise={root.Hero.Pistol.AimProgress:F3}, error={root.Hero.PistolAimErrorDegrees:F3}, " +
                $"target={root.Hero.PistolAimPoint:F3}, view={root.CameraFollow.Camera.transform.eulerAngles:F3}.");
        }

        private IEnumerator FirePistolAndCheckContinuousCamera(InputTestFixture input, Mouse mouse,
            List<PistolCameraFrame> frames, string context, Vector3? corpsePoint = null)
        {
            Assert.That(frames, Is.Not.Empty);
            PistolCameraFrame before = frames[frames.Count - 1];
            int start = frames.Count, impacts = root.Opponent.ReceivedImpactCount, spawns = root.Projectiles.SpawnCount;
            float frozenBefore = root.HitStopSecondsConsumed;
            if (corpsePoint.HasValue)
                Assert.That(root.Projectiles.TrySpawn(root.Hero, corpsePoint.Value + Vector3.up * .8f,
                    Vector3.down * CombatProjectilePool.MuzzleSpeed, root.Hero.Pistol.ShotSequence + 1), Is.True);
            else
            {
                input.Press(mouse.leftButton, queueEventOnly: true);
                yield return null;
                input.Release(mouse.leftButton, queueEventOnly: true);
            }
            for (int frame = 0; frame < 90 && root.Opponent.ReceivedImpactCount == impacts; frame++)
                yield return null;
            Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(spawns + 1), context + " must use one physical bullet.");
            Assert.That(root.Opponent.ReceivedImpactCount, Is.EqualTo(impacts + 1), context + " must physically hit the opponent.");
            for (int frame = 0; frame < 60; frame++) yield return null;
            Assert.That(root.HitStopSecondsConsumed, Is.GreaterThan(frozenBefore), context + " must traverse the real hitstop.");
            for (int index = start; index < frames.Count; index++)
            {
                PistolCameraFrame current = frames[index];
                string at = context + ", completed frame " + current.Frame;
                Assert.That(current.Camera, Is.SameAs(before.Camera), at);
                Assert.That(current.FollowEnabled && current.FreeAim && current.AimRequested, Is.True,
                    at + ": held RMB must retain the same live camera and aim through contact/freeze.");
                Assert.That(current.AimProgress, Is.EqualTo(1f).Within(.0001f), at + ": the pistol must not restart its raise.");
                Assert.That(Vector3.Distance(current.HeroPosition, before.HeroPosition), Is.LessThan(.001f), at);
                Assert.That(Vector3.Distance(current.Position, before.Position), Is.LessThan(.003f), at + ": no shoulder recenter.");
                Assert.That(Quaternion.Angle(current.Rotation, before.Rotation), Is.LessThan(.02f), at + ": no view kick.");
                Assert.That(current.FieldOfView, Is.EqualTo(before.FieldOfView).Within(.001f), at);
            }
        }

        private PistolCameraFrame ReadPistolCameraFrame()
        {
            Camera camera = root.CameraFollow.Camera;
            return new PistolCameraFrame
            {
                Frame = Time.frameCount, Camera = camera, Position = camera.transform.position,
                Rotation = camera.transform.rotation, FieldOfView = camera.fieldOfView,
                HeroPosition = root.Hero.transform.position, FollowEnabled = root.CameraFollow.enabled,
                FreeAim = root.CameraFollow.FreeAimActive, AimRequested = root.Hero.Pistol.AimRequested,
                AimProgress = root.Hero.Pistol.AimProgress
            };
        }

        private struct PistolCameraFrame
        {
            public int Frame;
            public Camera Camera;
            public Vector3 Position, HeroPosition;
            public Quaternion Rotation;
            public float FieldOfView, AimProgress;
            public bool FollowEnabled, FreeAim, AimRequested;
        }

        [UnityTest]
        public IEnumerator Range_PistolSelectionProjectileFlightReloadAndResetUseTheLiveDuel()
        {
            pistolGeometryIssues.Clear();
            var input = new InputTestFixture();
            Keyboard keyboard = null;
            Mouse mouse = null;
            GameObject obstacle = null;
            try
            {
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                mouse = InputSystem.AddDevice<Mouse>();
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                var menu = Object.FindAnyObjectByType<StartMenuRoot>();
                Assert.That(menu, Is.Not.Null);
                Assert.That(menu.SelectOption(StartMenuOption.CombatTest), Is.True);
                Assert.That(menu.ConfirmSelection(), Is.True);
                Assert.That(menu.IsChoosingCombatWeapon, Is.True);
                Assert.That(menu.SelectCombatOption(CombatPreparationOption.Pistol), Is.True);
                Assert.That(menu.ConfirmSelection(), Is.True);
                Assert.That(menu.SelectedCombatWeapon, Is.EqualTo(CombatWeaponId.Pistol));
                Assert.That(menu.SelectCombatOption(CombatPreparationOption.Start), Is.True);
                Assert.That(menu.ConfirmSelection(), Is.True);
                yield return AwaitSelectedCombatRange();
                Assert.That(root.HeroWeapon, Is.EqualTo(CombatWeaponId.Pistol));
                Assert.That(root.Hero.IsPistol, Is.True);
                Assert.That(root.Opponent.IsPistol, Is.False, "The opponent retains its original crowbar.");
                Assert.That(root.Hero.Weapon.name, Does.Contain("Pistol"));
                AssertPistolMechanicalAudioClips();

                // Interactive play enables the journal by default; batch mode
                // must opt in to exercise the same late-frame snapshot path.
                string journalFolder = Path.Combine(Directory.GetCurrentDirectory(), "TestResults", "Test pistol duel diagnostics");
                root.SetDuelLogging(true, journalFolder);
                DuelJournal journal = root.JournalForDiagnostics;
                Assert.That(journal.Enabled, Is.True);
                root.CaptureJournalFrame();

                PlacePair(6f);
                root.Hero.SetPistolAim(true);
                Assert.That(root.Hero.RequestPistolShot(), Is.True, "A fresh trigger is resolved on the next completed simulation pose.");
                root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(8), "An early raise request cannot spend a round.");
                Assert.That(root.Projectiles.SpawnCount, Is.Zero);
                Assert.That(root.Casings.EjectionCount + root.Casings.PendingCount, Is.Zero,
                    "A rejected raise trigger cannot queue a spent case.");
                root.Tick(root.Hero.Pistol.Settings.RaiseSeconds);
                Assert.That(root.Hero.Pistol.CanFire, Is.True);
                Assert.That(root.Projectiles.SpawnCount, Is.Zero, "Finishing the raise cannot replay its rejected trigger.");
                Assert.That(root.Hero.State.IsBlocking, Is.False, "Pistol aim never becomes the crowbar guard.");
                Vector3 chest = root.Opponent.Ragdoll.PhysicsController.ChestBody.position;
                Vector3 muzzle = root.Hero.PistolMuzzle.position;
                Vector3 muzzleForward = root.Hero.PistolMuzzle.forward;
                AssertPistolAimAligned("The raised gun's actual muzzle must face the opponent's chest");
                Assert.That(root.Hero.PistolSupportError, Is.LessThan(.02f));
                yield return CapturePistolNearViews("aim");
                yield return CaptureFocusGameView("pistol-01-aim");
                root.CameraFollow.Snap();
                Pose aimedCamera = new Pose(root.CameraFollow.Camera.transform.position, root.CameraFollow.Camera.transform.rotation);
                Vector3 stationaryHero = root.Hero.transform.position;
                Vector3 stationaryOpponent = root.Opponent.transform.position;

                float health = root.Opponent.State.Health;
                Assert.That(root.Hero.RequestPistolShot(), Is.True);
                Assert.That(root.Projectiles.SpawnCount, Is.Zero, "Input queues a trigger; the final simulation pose owns its origin.");
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(8));
                root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(7));
                Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(1));
                Assert.That(root.Projectiles.ActiveCount, Is.EqualTo(1));
                Assert.That(root.Casings.EjectionCount, Is.Zero, "The accepted shot must precede its physical ejection beat.");
                Assert.That(root.Casings.PendingCount, Is.EqualTo(1));
                Assert.That(root.Opponent.State.Health, Is.EqualTo(health), "A new projectile cannot hit its distant target on the spawn substep.");
                yield return null;
                AssertStablePistolCamera(aimedCamera, "Firing from a stationary target-locked pose cannot kick the camera");
                float pausedSlide = root.Hero.PistolSlideBack;
                Assert.That(root.PauseMenu.Open(), Is.True);
                root.Tick(.8f);
                yield return null;
                Assert.That(root.Casings.PendingCount, Is.EqualTo(1), "Pause freezes the accepted case before its ejection beat.");
                Assert.That(root.Casings.EjectionCount, Is.Zero);
                Assert.That(root.Casings.EjectionSoundCount + root.Casings.ContactSoundCount, Is.Zero,
                    "A paused pending case cannot schedule ejection or floor-contact audio.");
                Assert.That(root.Hero.PistolSlideBack, Is.EqualTo(pausedSlide));
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause did not release the accepted shot.");
                Vector3 origin = root.Projectiles.LastPosition;
                root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(Vector3.Distance(origin, root.Projectiles.LastPosition), Is.InRange(1f, 3f),
                    "The next substep advances the actual bullet at its finite prototype speed.");
                Assert.That(root.Opponent.State.Health, Is.EqualTo(health));
                Assert.That(root.Projectiles.VisibleTrailCount, Is.EqualTo(1));
                yield return CaptureFocusGameView("pistol-02-fired");
                for (int tick = 0; tick < 8 && root.Projectiles.ActiveCount > 0; tick++)
                    root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Projectiles.ActiveCount, Is.Zero);
                Assert.That(root.Projectiles.ImpactCount, Is.EqualTo(1));
                Assert.That(root.Opponent.ReceivedImpactCount, Is.EqualTo(1),
                    $"origin={origin:F4}, forward={muzzleForward:F4}, chest={chest:F4}, " +
                    $"lastPosition={root.Projectiles.LastPosition:F4}, worldOrBodyImpact={root.Projectiles.LastImpactPoint:F4}");
                Assert.That(root.Opponent.State.Health, Is.LessThan(health));
                Assert.That(root.Opponent.LastImpact.Kind, Is.EqualTo(CombatImpactKind.Projectile));
                Assert.That(Vector3.Distance(root.Hero.transform.position, stationaryHero), Is.LessThan(.001f));
                Assert.That(Vector3.Distance(root.Opponent.transform.position, stationaryOpponent), Is.LessThan(.001f));
                yield return null;
                AssertStablePistolCamera(aimedCamera, "An outgoing body contact cannot shake or reframe the stationary shooter's camera");
                Assert.That(root.Opponent.LastImpact.Impulse.magnitude, Is.GreaterThanOrEqualTo(18f));
                Assert.That(root.BloodEffects.ProjectileWoundCountFor(root.Opponent), Is.EqualTo(1),
                    "The actual body contact leaves a persistent hole on the original skin.");
                Assert.That(root.Projectiles.VisibleTrailCount, Is.GreaterThan(0),
                    "The swept flight must remain visible after the tiny physical bullet is retired.");
                yield return CaptureFocusGameView("pistol-impact-contact");
                root.Tick(.12f);
                yield return CaptureFocusGameView("pistol-impact-reaction");
                root.Tick(.5f);
                Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(1), "Advancing the duel cannot repeat a trigger request.");
                Assert.That(root.Casings.EjectionCount, Is.EqualTo(1), "Only the accepted shot ejects one actual case.");
                Assert.That(root.Casings.EjectionSoundCount, Is.EqualTo(1));
                Assert.That(root.Casings.PendingCount, Is.Zero);
                Assert.That(root.Casings.ActiveCount, Is.EqualTo(1));
                Assert.That(root.Casings.LastEjectionVelocity.sqrMagnitude, Is.GreaterThan(.1f));
                yield return CaptureFocusGameView("pistol-03-impact");
                root.Tick(2f);
                Assert.That(root.Casings.ContactSoundCount, Is.GreaterThan(0),
                    "A real spent case schedules a metallic contact sound when it reaches the floor.");
                int oldBleedTail = root.BloodEffects.BleedingDropCountFor(root.Opponent);
                root.Tick(.3f);
                Assert.That(root.BloodEffects.ProjectileWoundCountFor(root.Opponent), Is.EqualTo(1));
                Assert.That(root.BloodEffects.BleedingDropCountFor(root.Opponent), Is.GreaterThan(oldBleedTail),
                    "A living bullet wound keeps bleeding after the former finite impact tail.");
                yield return CapturePistolWoundNearView(root.Opponent, "pistol-npc-torso-wound-persistent");

                // A 15 mm collider is crossed within a single 120 Hz step. A sampled
                // end-position overlap would miss it; the projectile must sweep its path.
                root.ResetRound();
                AssertPistolWoundsReset();
                PlacePair(6f);
                obstacle = new GameObject("Test bullet obstacle");
                var wall = obstacle.AddComponent<BoxCollider>();
                wall.size = new Vector3(.015f, 1f, 1f);
                obstacle.transform.position = new Vector3(0f, 1.1f, -2f);
                Physics.SyncTransforms();
                Assert.That(root.Projectiles.TrySpawn(root.Hero, new Vector3(-2f, 1.1f, -2f),
                    Vector3.right * 250f, root.Hero.Pistol.ShotSequence), Is.True);
                root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Projectiles.ImpactCount, Is.EqualTo(1));
                Assert.That(root.Projectiles.ActiveCount, Is.Zero);
                Assert.That(Mathf.Abs(root.Projectiles.LastImpactPoint.x), Is.LessThan(.025f));
                Assert.That(root.Opponent.ReceivedImpactCount, Is.Zero, "A world contact cannot damage the unrelated opponent.");
                Assert.That(root.Projectiles.VisibleTrailCount, Is.EqualTo(1));
                Assert.That(Vector3.Distance(root.Projectiles.LastTrailEnd, root.Projectiles.LastImpactPoint), Is.LessThan(.001f),
                    "The visible streak ends at the first actual surface rather than crossing the wall.");
                int pausedTrails = root.Projectiles.VisibleTrailCount;
                Assert.That(root.PauseMenu.Open(), Is.True);
                root.Tick(.2f);
                for (int frame = 0; frame < 8; frame++) yield return null;
                Assert.That(root.Projectiles.VisibleTrailCount, Is.EqualTo(pausedTrails), "Pause freezes retained flight streaks.");
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause did not release the bullet trail.");
                for (int frame = 0; frame < 10; frame++) yield return null;
                Assert.That(root.Projectiles.VisibleTrailCount, Is.Zero, "The completed streak must fade without another simulation tick.");
                Object.DestroyImmediate(obstacle);
                obstacle = null;

                root.ResetRound();
                PlacePair(6f);
                Vector3 ballisticOrigin = new Vector3(-2f, 2f, -2f);
                Assert.That(root.Projectiles.TrySpawn(root.Hero, ballisticOrigin,
                    Vector3.right * CombatProjectilePool.MuzzleSpeed, root.Hero.Pistol.ShotSequence), Is.True);
                float flightSeconds = CombatTestRoot.SimulationStep * 3f;
                root.Tick(flightSeconds);
                Assert.That(root.Projectiles.ActiveCount, Is.EqualTo(1));
                Assert.That(root.Projectiles.LastPosition.y,
                    Is.EqualTo(ballisticOrigin.y - .5f * CombatProjectilePool.Gravity * flightSeconds * flightSeconds).Within(.0001f),
                    "The physical projectile accumulates gravity during its elapsed flight.");

                // The real keyboard route must make R reload, keeping the existing
                // reset button/API independent from the newly assigned key.
                root.ResetRound();
                PlacePair(6f);
                root.Hero.SetPistolAim(true);
                root.Tick(.25f);
                Assert.That(root.Hero.RequestPistolShot(), Is.True);
                root.Tick(CombatTestRoot.SimulationStep);
                input.Press(keyboard.rKey, queueEventOnly: true);
                root.AutomaticSimulation = true;
                yield return null;
                root.AutomaticSimulation = false;
                input.Release(keyboard.rKey, queueEventOnly: true);
                InputSystem.Update();
                Assert.That(root.Hero.Pistol.IsReloading, Is.True, "R starts the magazine action instead of resetting the duel.");
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(7));
                AdvancePistolReloadTo(.27f);
                AssertPistolReloadAudio(1, RetroSfxId.PistolMagazineLatch);
                AdvancePistolReloadTo(.55f);
                AssertPistolReloadAudio(2, RetroSfxId.PistolMagazineRemove);
                float reloadProgress = root.Hero.Pistol.ReloadProgress;
                Assert.That(reloadProgress, Is.GreaterThan(0f));
                AssertPistolMagazineContact("The removed magazine is visible in the original left hand");
                Assert.That(root.Hero.PistolMagazineSeated, Is.False);
                Transform removedMagazine = root.Hero.PistolMagazineTransform;
                Vector3 pausedCase = root.Casings.LastPosition;
                int pausedCaseContacts = root.Casings.ContactSoundCount;
                yield return CapturePistolNearViews("reload");
                yield return CaptureFocusGameView("pistol-04-reload");
                Assert.That(root.PauseMenu.Open(), Is.True);
                root.Tick(.8f);
                yield return null;
                Assert.That(root.Hero.Pistol.ReloadProgress, Is.EqualTo(reloadProgress));
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(7));
                AssertPistolReloadAudio(2, RetroSfxId.PistolMagazineRemove);
                Assert.That(root.Casings.ContactSoundCount, Is.EqualTo(pausedCaseContacts));
                Assert.That(root.Hero.PistolMagazineTransform, Is.SameAs(removedMagazine));
                AssertPistolMagazineContact("Pause retains the visible magazine handoff");
                Assert.That(root.Casings.LastPosition, Is.EqualTo(pausedCase), "Pause freezes the already ejected case too.");
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause did not release pistol gameplay.");
                Assert.That(root.SetOpponentFocus(false), Is.True);
                Assert.That(root.Hero.Pistol.IsReloading, Is.True, "Reloading remains available outside combat focus.");
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(7));
                int reloadSpawns = root.Projectiles.SpawnCount;
                Assert.That(root.Hero.RequestPistolShot(), Is.True);
                root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(reloadSpawns));
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(7));
                AdvancePistolReloadTo(.76f);
                AssertPistolReloadAudio(3, RetroSfxId.PistolMagazineStow);
                Assert.That(root.Hero.PistolMagazineInHand, Is.False, "The stowed old magazine cannot remain floating in the left palm.");
                Assert.That(removedMagazine.gameObject.activeInHierarchy, Is.False);
                AdvancePistolReloadTo(.95f);
                AssertPistolReloadAudio(4, RetroSfxId.PistolMagazineDraw);
                AssertPistolMagazineContact("A replacement magazine is actually carried by the left hand");
                AdvancePistolReloadTo(1.2f);
                AssertPistolReloadAudio(5, RetroSfxId.PistolMagazineInsert);
                AdvancePistolReloadTo(1.31f);
                AssertPistolReloadAudio(6, RetroSfxId.PistolMagazineSeat);
                Assert.That(root.Hero.PistolMagazineSeated, Is.True);
                Assert.That(root.Hero.PistolMagazineInHand, Is.False);
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(7), "Seating the visible magazine cannot commit ammunition early.");
                AdvancePistolReloadTo(1.5f);
                AssertPistolReloadAudio(7, RetroSfxId.PistolSlidePull);
                Assert.That(root.Hero.PistolSlideBack, Is.GreaterThan(.95f));
                AssertPistolSlidePullContact("The same left hand racks the actual moving slide");
                yield return CapturePistolNearViews("reload-rack");
                AdvancePistolReloadTo(1.61f);
                AssertPistolReloadAudio(8, RetroSfxId.PistolSlideRelease);
                Assert.That(root.Hero.PistolSlideBack, Is.LessThan(.01f));
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(7));
                root.Tick(.3f);
                AssertPistolReloadAudio(9, RetroSfxId.PistolReady);
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(8));
                Assert.That(root.Hero.Pistol.IsReloading, Is.False);
                Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(reloadSpawns), "Reload completion cannot release an old trigger.");
                Assert.That(root.SetOpponentFocus(true), Is.True);

                root.Hero.SetPistolAim(true);
                root.Tick(.25f);
                Assert.That(root.Hero.RequestPistolShot(), Is.True);
                root.Tick(.5f);
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(7));
                Assert.That(root.Hero.TryReloadPistol(), Is.True);
                root.Opponent.ResetActor(root.Hero.transform.position + root.Hero.transform.forward * 1.1f,
                    -root.Hero.transform.forward);
                Physics.SyncTransforms();
                Assert.That(root.Opponent.TryAttack(), Is.True);
                root.Tick(.8f);
                Assert.That(root.Hero.State.Health, Is.LessThan(MeleeCombatSettings.Crowbar.MaxHealth),
                    "The original opponent's real crowbar contact interrupts the magazine action.");
                Assert.That(root.Hero.Pistol.IsReloading, Is.False);
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(7), "An interrupted magazine action cannot refill its ammunition.");
                Assert.That(root.Hero.Pistol.ReloadPending, Is.True, "An interrupted handoff retains its physical magazine stage.");
                float interruptedReload = root.Hero.Pistol.ReloadElapsed;
                bool interruptedMagazine = root.Hero.Pistol.MagazineAttached;
                int interruptedReloadCues = root.Hero.PistolReloadCueCount;
                RetroSfxId interruptedCue = root.Hero.LastPistolReloadCue;
                Assert.That(interruptedReloadCues, Is.InRange(10, 17),
                    "The interrupted exchange must have only its already crossed physical sound beats.");

                if (!root.Hero.IsKnockedDown)
                    Assert.That(root.Hero.TryBeginKnockdown(root.Hero.LastImpact, Vector3.zero, Vector3.zero), Is.True);
                Assert.That(root.Hero.IsWeaponDropped, Is.True, "A pistol is released on a living fall without a crowbar support solver.");
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(7));
                Assert.That(root.Hero.Pistol.ReloadElapsed, Is.EqualTo(interruptedReload));
                AssertPistolReloadAudio(interruptedReloadCues, interruptedCue);
                yield return FinishIsolatedRecovery(root.Hero, "The pistol owner must recover without completing a suspended reload.");
                Assert.That(root.Hero.Pistol.ReloadPending, Is.True);
                Assert.That(root.Hero.Pistol.ReloadElapsed, Is.EqualTo(interruptedReload));
                GameObject droppedPistol = root.Hero.Weapon;
                Assert.That(root.Hero.EquipRecoveredWeapon(), Is.True);
                Assert.That(root.Hero.Weapon, Is.SameAs(droppedPistol));
                Assert.That(root.Hero.Pistol.MagazineAttached, Is.EqualTo(interruptedMagazine));
                Assert.That(root.Hero.Pistol.ReloadElapsed, Is.EqualTo(interruptedReload));
                root.Hero.SetPistolAim(true);
                root.Tick(.3f);
                int interruptedSpawns = root.Projectiles.SpawnCount;
                int interruptedCases = root.Casings.EjectionCount;
                Assert.That(root.Hero.RequestPistolShot(), Is.True);
                root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(interruptedSpawns), "A recovered gun with an unfinished exchange cannot fire.");
                Assert.That(root.Casings.EjectionCount, Is.EqualTo(interruptedCases));
                Assert.That(root.Hero.TryReloadPistol(), Is.True);
                Assert.That(root.Hero.Pistol.ReloadElapsed, Is.EqualTo(interruptedReload), "Resume continues the same exchange instead of replaying magazine removal.");
                root.Tick(.05f);
                Assert.That(root.Hero.Pistol.ReloadElapsed, Is.EqualTo(interruptedReload),
                    "The recovery blend holds the retained clock before its next physical handoff.");
                AssertPistolReloadAudio(interruptedReloadCues, interruptedCue);
                root.Tick(root.Hero.Pistol.Settings.ReloadSeconds - interruptedReload + .2f);
                AssertPistolReloadAudio(18, RetroSfxId.PistolReady);
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(8));
                Assert.That(root.Hero.Pistol.ReloadPending || root.Hero.Pistol.IsReloading, Is.False);
                Assert.That(root.Hero.PistolMagazineSeated, Is.True);
                root.ResetRound();
                Assert.That(root.Hero.IsWeaponDropped || root.Hero.IsKnockedDown, Is.False);
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(8));
                Assert.That(root.Hero.PistolMagazineSeated, Is.True);
                Assert.That(root.Hero.PistolMagazineInHand || root.Hero.Pistol.ReloadPending, Is.False);
                Assert.That(root.Hero.PistolSlideBack, Is.Zero);
                Assert.That(root.Casings.EjectionCount + root.Casings.ActiveCount + root.Casings.PendingCount, Is.Zero);
                Assert.That(root.Casings.EjectionSoundCount + root.Casings.ContactSoundCount, Is.Zero);
                AssertPistolReloadAudio(0, RetroSfxId.None);

                Assert.That(root.Projectiles.TrySpawn(root.Hero, new Vector3(-2f, 2f, -2f),
                    Vector3.right * 250f, root.Hero.Pistol.ShotSequence), Is.True);
                Assert.That(root.Projectiles.ActiveCount, Is.EqualTo(1));
                root.ResetRound();
                Assert.That(root.HeroWeapon, Is.EqualTo(CombatWeaponId.Pistol));
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(8));
                Assert.That(root.Projectiles.ActiveCount, Is.Zero, "Reset removes every old projectile before restoring the selected loadout.");
                Assert.That(root.Opponent.State.Health, Is.EqualTo(MeleeCombatSettings.Crowbar.MaxHealth));
                Assert.That(root.IsOpponentFocused, Is.True);

                // Keep the diagnostic trigger/result rounds inside the journal's
                // bounded retention window; the additional anatomy resets go first.
                yield return VerifyPistolHeadshotPresentation();
                yield return VerifyFreePistolAim(input, mouse);
                yield return VerifyPistolAimGeometryAndTriggerBoundaries(input, mouse);
                yield return VerifyPistolCarryAndWinnerInput(input, mouse, keyboard);

                Assert.That(CombatTestStartService.ReturnToPreparation(root.HeroWeapon), Is.True);
                yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneIds.MainMenu &&
                    !SceneTransitionService.IsTransitioning, "Combat preparation did not reopen.");
                yield return WaitFor(() => journal.Completion.IsCompleted, "Pistol journal did not close with its scene.");
                Assert.That(journal.LastError, Is.Null);
                AssertPistolJournalSnapshots(journalFolder, journal.SessionId);
                menu = Object.FindAnyObjectByType<StartMenuRoot>();
                Assert.That(menu.IsChoosingCombatWeapon, Is.True);
                Assert.That(menu.SelectedCombatWeapon, Is.EqualTo(CombatWeaponId.Pistol));
                Assert.That(menu.SelectCombatOption(CombatPreparationOption.Crowbar), Is.True);
                Assert.That(menu.ConfirmSelection(), Is.True);
                Assert.That(menu.SelectCombatOption(CombatPreparationOption.Start), Is.True);
                Assert.That(menu.ConfirmSelection(), Is.True);
                yield return AwaitSelectedCombatRange();
                Assert.That(root.HeroWeapon, Is.EqualTo(CombatWeaponId.Crowbar));
                Assert.That(root.Hero.IsPistol || root.Opponent.IsPistol, Is.False);
                PlacePair(1.1f);
                Assert.That(root.Hero.TryAttack(), Is.True, "Choosing the crowbar retains its original attack path.");
                root.Tick(MeleeCombatSettings.Crowbar.WindupSeconds + MeleeCombatSettings.Crowbar.ActiveSeconds + .05f);
                Assert.That(root.Opponent.ReceivedImpactCount, Is.EqualTo(1));
                Assert.That(root.Opponent.LastImpact.Kind, Is.EqualTo(CombatImpactKind.Weapon));
                float crowbarHealth = root.Opponent.State.Health;
                input.Press(keyboard.rKey, queueEventOnly: true);
                root.AutomaticSimulation = true;
                yield return null;
                root.AutomaticSimulation = false;
                input.Release(keyboard.rKey, queueEventOnly: true);
                InputSystem.Update();
                Assert.That(root.Opponent.State.Health, Is.EqualTo(crowbarHealth), "R cannot reset a crowbar round after becoming the reload key.");
                LogAssert.NoUnexpectedReceived();
                Assert.That(pistolGeometryIssues, Is.Empty, string.Join("\n", pistolGeometryIssues));
            }
            finally
            {
                if (root != null)
                {
                    root.AutomaticSimulation = false;
                    root.SetDuelLogging(false);
                }
                if (obstacle != null) Object.DestroyImmediate(obstacle);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                input.TearDown();
            }
        }

        private IEnumerator VerifyPistolHeadshotPresentation()
        {
            root.ResetRound();
            PlacePair(6f);
            Assert.That(root.SetOpponentFocus(false), Is.True);
            root.Hero.SetPistolAim(true, root.Hero.transform.position + Vector3.forward * 30f + Vector3.up * 12f);
            root.Tick(.25f);
            Vector3 aim = root.Hero.PistolMuzzle.forward;
            Vector3 slideRest = PistolSlideLocalPosition();
            Assert.That(root.Hero.RequestPistolShot(), Is.True);
            root.Tick(CombatTestRoot.SimulationStep);
            root.Tick(.04f);
            Assert.That(Vector3.Angle(aim, root.Hero.PistolMuzzle.forward), Is.GreaterThan(12f),
                "The final constrained gun pose must retain the strong authored firing kick.");
            Assert.That(CombatPistolAssetProvider.FindAnchor(root.Hero.Weapon, "MuzzleFlash").gameObject.activeSelf, Is.True,
                "A four-substep render interval must still show the accepted shot's flash.");
            Assert.That(root.Hero.PistolSlideBack, Is.GreaterThan(.7f));
            Assert.That(Vector3.Distance(slideRest, PistolSlideLocalPosition()), Is.GreaterThan(.015f),
                "The visible authored slide must translate relative to the fixed frame, not merely report a recoil fraction.");
            Assert.That(root.Casings.EjectionCount, Is.EqualTo(1));
            Assert.That(root.Casings.LastShotSequence, Is.EqualTo(root.Hero.Pistol.ShotSequence));
            Assert.That(Vector3.Distance(root.Casings.LastEjectionPosition, root.Hero.PistolEjectionPort.position), Is.LessThan(.12f),
                "The case is born at the moving authored ejection port during the kick.");
            yield return CapturePistolNearViews("fire-peak");
            yield return CaptureFocusGameView("pistol-fire-peak-game");
            root.Tick(.45f);
            Assert.That(root.Hero.PistolAimAligned, Is.True, "The gun recovers to its original aimed line.");
            Assert.That(root.Hero.PistolSlideBack, Is.LessThan(.001f));
            Assert.That(Vector3.Distance(slideRest, PistolSlideLocalPosition()), Is.LessThan(.001f));
            Assert.That(root.Casings.EjectionCount, Is.EqualTo(1), "The returning slide cannot eject the same case twice.");
            yield return CapturePistolNearViews("fire-recovered");
            foreach (bool rear in new[] { false, true })
            {
                root.ResetRound();
                PlacePair(4f);
                Collider headShape = null;
                foreach (var shape in root.Opponent.Ragdoll.PhysicsController.AnatomicalColliders)
                    if (shape.Value == Player3DAnatomicalPart.Head) { headShape = shape.Key; break; }
                Assert.That(headShape, Is.Not.Null);
                Vector3 target = headShape.bounds.center;
                Vector3 direction = root.Opponent.transform.forward * (rear ? 1f : -1f);
                Assert.That(root.Projectiles.TrySpawn(root.Hero, target - direction * .8f,
                    direction * CombatProjectilePool.MuzzleSpeed, root.Hero.Pistol.ShotSequence), Is.True);
                root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Opponent.LastImpact.Location.Region, Is.EqualTo(MeleeBodyRegion.Head));
                Assert.That(root.Opponent.LastImpact.Location.Side, Is.EqualTo(rear ? MeleeHitSide.Rear : MeleeHitSide.Front));
                Assert.That(root.Opponent.State.Health, Is.Zero, "One head contact is lethal from full health.");
                Assert.That(root.Opponent.ReceivedImpactCount, Is.EqualTo(1));
                Assert.That(root.Opponent.IsRagdollActive, Is.True, "Physics owns the live headshot pose on the contact tick.");
                Assert.That(root.Opponent.IsWeaponDropped, Is.True);
                Assert.That(root.Projectiles.ActiveCount, Is.Zero);
                Assert.That(root.Projectiles.VisibleTrailCount, Is.EqualTo(1),
                    "Round completion clears flight, but the killing shot still gets rendered.");
                Assert.That(root.BloodEffects.ActiveDropCount, Is.GreaterThan(26));
                Assert.That(root.BloodEffects.ProjectileWoundCountFor(root.Opponent), Is.EqualTo(1));
                yield return CaptureFocusGameView(rear ? "pistol-headshot-rear-contact" : "pistol-headshot-front-contact");
                if (!rear) yield return CapturePistolWoundNearView(root.Opponent, "pistol-npc-head-wound-contact");
                root.Tick(.35f);
                for (int frame = 0; frame < 24; frame++) yield return null;
                yield return CaptureFocusGameView(rear ? "pistol-headshot-rear-fall" : "pistol-headshot-front-fall");
                Assert.That(root.Opponent.ReceivedImpactCount, Is.EqualTo(1), "The visual tail cannot repeat damage.");
                Assert.That(root.BloodEffects.ProjectileWoundCountFor(root.Opponent), Is.EqualTo(1),
                    "The hole follows the same skinned head after the animation-to-ragdoll handoff.");
                if (!rear) yield return VerifyPersistentPistolAftermath();
                root.ResetRound();
                AssertPistolWoundsReset();
                Assert.That(root.Projectiles.VisibleTrailCount, Is.Zero, "Reset clears retained traces immediately.");
                Assert.That(root.Opponent.IsRagdollActive, Is.False);
                Assert.That(root.Opponent.State.IsDefeated, Is.False);
            }

            PlacePair(4f);
            root.Hero.SetPistolAim(true);
            root.Tick(.25f);
            Transform hand = CityPedestrianHandProps.FindSocket(root.Hero.DamageRigRoot, "hand.R");
            Vector3 liveHand = hand.position;
            Collider heroHead = null;
            foreach (var shape in root.Hero.Ragdoll.PhysicsController.AnatomicalColliders)
                if (shape.Value == Player3DAnatomicalPart.Head) { heroHead = shape.Key; break; }
            Assert.That(heroHead, Is.Not.Null);
            Assert.That(root.Projectiles.TrySpawn(root.Opponent, heroHead.bounds.center + Vector3.forward * .8f,
                Vector3.back * CombatProjectilePool.MuzzleSpeed, 1), Is.True);
            root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(root.Hero.State.IsDefeated && root.Hero.IsRagdollActive, Is.True);
            Assert.That(Vector3.Distance(hand.position, liveHand), Is.LessThan(.025f),
                "An aimed hero hands the exact visible arm pose to physics, without reevaluating defeated aim.");
            yield return CaptureFocusGameView("pistol-hero-headshot-contact");
            Assert.That(root.BloodEffects.ProjectileWoundCountFor(root.Hero), Is.EqualTo(1));
            yield return CapturePistolWoundNearView(root.Hero, "pistol-hero-head-wound-contact");
            root.Tick(2f);
            int heroBleedTail = root.BloodEffects.BleedingDropCountFor(root.Hero);
            root.Tick(.3f);
            Assert.That(root.Hero.IsRagdollActive, Is.True);
            Assert.That(root.BloodEffects.ProjectileWoundCountFor(root.Hero), Is.EqualTo(1));
            Assert.That(root.BloodEffects.BleedingDropCountFor(root.Hero), Is.GreaterThan(heroBleedTail),
                "The defeated hero's hole remains an active emitter after its former finite headshot tail.");

            root.ResetRound();
            AssertPistolWoundsReset();
            PlacePair(4f);
            var fall = new CombatImpact(root.Hero, root.Opponent, 1, root.Opponent.transform.position,
                Vector3.back, Vector3.forward, 100f, 100f, MeleeHitResult.Hit);
            Assert.That(root.Opponent.TryBeginKnockdown(fall, Vector3.forward * .3f, Vector3.right * .4f), Is.True);
            for (int frame = 0; frame < 40; frame++) yield return null;
            Collider fallenHead = null;
            foreach (var shape in root.Opponent.Ragdoll.PhysicsController.AnatomicalColliders)
                if (shape.Value == Player3DAnatomicalPart.Head) { fallenHead = shape.Key; break; }
            Assert.That(fallenHead, Is.Not.Null);
            Vector3 fallenPelvis = root.Opponent.Ragdoll.PelvisBody.position;
            Assert.That(root.Projectiles.TrySpawn(root.Hero, fallenHead.bounds.center + Vector3.forward * .8f,
                Vector3.back * CombatProjectilePool.MuzzleSpeed, 1), Is.True);
            root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(root.Opponent.State.IsDefeated && root.Opponent.IsRagdollActive, Is.True);
            Assert.That(root.Opponent.IsKnockedDown, Is.False, "The existing fall is terminal rather than recoverable.");
            Assert.That(Vector3.Distance(root.Opponent.Ragdoll.PelvisBody.position, fallenPelvis), Is.LessThan(.025f),
                "A fallen headshot cannot replay a standing defeat pose.");
            yield return CaptureFocusGameView("pistol-fallen-headshot-contact");
            root.ResetRound();
            AssertPistolWoundsReset();
        }

        private IEnumerator VerifyPersistentPistolAftermath()
        {
            root.Tick(10.1f);
            Assert.That(root.RoundFinished && root.Opponent.IsRagdollActive, Is.True);
            int beyondOldPool = root.BloodEffects.BleedingDropCountFor(root.Opponent);
            root.Tick(.5f);
            int singleWoundDrops = root.BloodEffects.BleedingDropCountFor(root.Opponent) - beyondOldPool;
            Assert.That(singleWoundDrops, Is.GreaterThan(0),
                "A finished round keeps emitting from the wound after the former ten-second pool growth window.");
            Assert.That(root.BloodEffects.ProjectileWoundCountFor(root.Opponent), Is.EqualTo(1));
            Assert.That(root.BloodEffects.BleedCount, Is.GreaterThan(0));
            int pausedDrops = root.BloodEffects.BleedingDropCountFor(root.Opponent);
            Assert.That(root.PauseMenu.Open(), Is.True);
            root.Tick(.8f);
            for (int frame = 0; frame < 8; frame++) yield return null;
            Assert.That(root.BloodEffects.BleedingDropCountFor(root.Opponent), Is.EqualTo(pausedDrops),
                "Pause freezes continuing wound emission even after the round result.");
            Assert.That(root.BloodEffects.ProjectileWoundCountFor(root.Opponent), Is.EqualTo(1));
            Assert.That(root.PauseMenu.Cancel(), Is.True);
            yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause did not release the continuing wound.");

            // Caller-clock ticks above advance blood, not PhysX. Wait for the
            // terminal body's actual fixed-step settle before proving wake-up.
            for (int frame = 0; frame < 250 && !root.Opponent.Ragdoll.IsSettled; frame++)
                yield return new WaitForFixedUpdate();
            Assert.That(root.Opponent.Ragdoll.IsSettled, Is.True, "The old corpse must come to physical rest before the new shot.");
            Assert.That(root.Opponent.Ragdoll.PhysicsController.IsFrozen, Is.True);
            yield return CapturePistolWoundNearView(root.Opponent, "pistol-npc-head-wound-aftermath");

            Collider torso = null;
            foreach (var shape in root.Opponent.Ragdoll.PhysicsController.AnatomicalColliders)
                if (shape.Value == Player3DAnatomicalPart.Torso) { torso = shape.Key; break; }
            Assert.That(torso, Is.Not.Null);
            Vector3 pelvis = root.Opponent.Ragdoll.PelvisBody.position;
            float heroHealth = root.Hero.State.Health;
            int impacts = root.Opponent.ReceivedImpactCount;
            Assert.That(root.Projectiles.TrySpawn(root.Hero, torso.bounds.center + Vector3.up * .6f,
                Vector3.down * CombatProjectilePool.MuzzleSpeed, root.Hero.Pistol.ShotSequence + 1), Is.True);
            root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(root.Opponent.ReceivedImpactCount, Is.EqualTo(impacts + 1),
                "A deliberate new shot can still contact the defeated physical body.");
            Assert.That(root.RoundFinished && root.Opponent.State.IsDefeated && root.Opponent.IsRagdollActive, Is.True);
            Assert.That(root.Opponent.State.Health, Is.Zero);
            Assert.That(root.Hero.State.Health, Is.EqualTo(heroHealth));
            Assert.That(root.Opponent.LastImpact.HealthBefore, Is.Zero);
            Assert.That(root.Opponent.LastImpact.HealthAfter, Is.Zero);
            Assert.That(root.Opponent.LastImpact.Impulse.magnitude, Is.GreaterThanOrEqualTo(18f));
            Assert.That(Vector3.Distance(pelvis, root.Opponent.Ragdoll.PelvisBody.position), Is.LessThan(.025f),
                "A corpse receives the new impulse from its live pose without snapping to Ready or defeat entry.");
            Assert.That(root.BloodEffects.ProjectileWoundCountFor(root.Opponent), Is.EqualTo(2),
                "A distinct corpse contact adds a second hole while keeping the original head wound.");
            root.Tick(.2f);
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.That(root.Opponent.Ragdoll.MaximumBodySpeed, Is.GreaterThan(.05f),
                "The postmortem shot wakes the existing ragdoll and imparts physical movement.");
            int multipleWounds = root.BloodEffects.BleedingDropCountFor(root.Opponent);
            root.Tick(.5f);
            Assert.That(root.BloodEffects.BleedingDropCountFor(root.Opponent) - multipleWounds,
                Is.GreaterThan(singleWoundDrops), "Both separate holes continue emitting rather than only the newest contact.");
            Assert.That(root.Opponent.State.Health, Is.Zero);
            yield return CapturePistolWoundNearView(root.Opponent, "pistol-npc-postmortem-torso-wound", 1);
        }

        private void AssertPistolWoundsReset()
        {
            foreach (CombatActor actor in new[] { root.Hero, root.Opponent })
            {
                Assert.That(root.BloodEffects.ProjectileWoundCountFor(actor), Is.Zero);
                Assert.That(root.BloodEffects.BleedingDropCountFor(actor), Is.Zero);
                Assert.That(root.BloodEffects.WoundCountFor(actor), Is.Zero);
                foreach (SkinnedMeshRenderer skin in actor.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (skin.name.StartsWith("Projectile Wounds__", System.StringComparison.Ordinal))
                        Assert.That(skin.enabled, Is.False, "Reset hides the retained hole overlay on both original rigs.");
            }
            Assert.That(root.BloodEffects.BleedCount, Is.Zero);
            Assert.That(root.BloodEffects.ActiveDropCount, Is.Zero);
            Assert.That(root.BloodEffects.StainCount, Is.Zero);
        }

        private IEnumerator VerifyFreePistolAim(InputTestFixture input, Mouse mouse)
        {
            root.SetSparring(false);
            PlacePair(6f);
            root.SendMessage("OnApplicationFocus", true);
            Assert.That(root.SetOpponentFocus(false), Is.True);
            CursorLockMode cursorLock = Cursor.lockState;
            bool cursorVisible = Cursor.visible;
            root.AutomaticSimulation = true;
            input.Press(mouse.rightButton, queueEventOnly: true);
            yield return null;
            Assert.That(root.Hero.Pistol.AimRequested, Is.True, "RMB raises the pistol without reacquiring the opponent.");
            Assert.That(root.CameraFollow.FreeAimActive, Is.True);
            Assert.That(root.IsOpponentFocused || root.CameraFollow.TargetLockActive || root.Player.Motor.MovementTargetActive, Is.False);
            root.Tick(.3f);
            Assert.That(root.Hero.Pistol.CanFire, Is.True);
            Assert.That(((Player3DCharacterPresentation)root.Player.Visual).OwnsClip(root.Hero), Is.True);

            Camera camera = root.CameraFollow.Camera;
            float yaw = camera.transform.eulerAngles.y;
            float pitch = camera.transform.eulerAngles.x;
            input.Set(mouse.delta, new Vector2(180f, -35f), queueEventOnly: true);
            yield return null;
            Assert.That(Mathf.DeltaAngle(yaw, camera.transform.eulerAngles.y), Is.EqualTo(28.8f).Within(.1f),
                "The simulation and LateUpdate must consume a mouse delta only once.");
            Assert.That(Mathf.DeltaAngle(pitch, camera.transform.eulerAngles.x), Is.EqualTo(4.9f).Within(.1f));
            root.Tick(.5f);
            AssertPistolAimAligned("A settled free aim must pass the same gate used by shot commit");
            Assert.That(root.Hero.PistolSupportError, Is.LessThan(.02f));
            yield return CaptureFocusGameView("pistol-05-free-aim");

            // Both ends of the gun can be past a thin wall; clearance must
            // include the reach from the body, not just the weapon's own length.
            Vector3 chest = root.Hero.Ragdoll.PhysicsController.ChestBody.position;
            Vector3 grip = root.Hero.Weapon.transform.position;
            Vector3 clearAimPoint = root.Hero.PistolAimPoint;
            var barrier = new GameObject("Test close aiming wall");
            var shape = barrier.AddComponent<BoxCollider>();
            shape.size = new Vector3(.3f, .3f, .015f);
            barrier.transform.SetPositionAndRotation(Vector3.Lerp(chest, grip, .5f), Quaternion.LookRotation(grip - chest));
            Physics.SyncTransforms();
            try
            {
                Assert.That(root.Projectiles.MuzzleIsClear(root.Hero, root.Hero.PistolMuzzle.position), Is.False);
                // Keep the accepted distant target: a fresh camera ray would
                // select this newly inserted barrier as a different aim point.
                root.Hero.SetPistolAim(true, clearAimPoint);
                Assert.That(root.Hero.RequestPistolShot(), Is.True);
                root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(8), "A held gun beyond the wall must not spend a round.");
                Assert.That(root.Projectiles.SpawnCount, Is.Zero, "A held gun beyond the wall must not spawn a bullet through it.");
            }
            finally { Object.DestroyImmediate(barrier); }

            input.Press(mouse.leftButton, queueEventOnly: true);
            input.Set(mouse.delta, new Vector2(1125f, 0f), queueEventOnly: true);
            yield return null;
            Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(8), "A half-turn cannot fire through the old body facing.");
            Assert.That(root.Projectiles.SpawnCount, Is.Zero);
            root.Tick(1.5f);
            yield return null;
            Assert.That(root.Projectiles.SpawnCount, Is.Zero, "Settling with LMB held must not release a deferred shot.");
            input.Release(mouse.leftButton, queueEventOnly: true);
            yield return null;

            // Fire on the same frame as another mouse turn: the shot must use
            // that accepted look, rather than the preceding rendered frame.
            float health = root.Opponent.State.Health;
            float acceptedYaw = camera.transform.eulerAngles.y + 45f * .16f;
            float acceptedPitch = camera.transform.eulerAngles.x;
            input.Press(mouse.leftButton, queueEventOnly: true);
            input.Set(mouse.delta, new Vector2(45f, 0f), queueEventOnly: true);
            yield return null;
            Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(7));
            Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(1));
            Assert.That(root.Projectiles.ActiveCount, Is.EqualTo(1));
            Assert.That(Mathf.DeltaAngle(acceptedYaw, camera.transform.eulerAngles.y), Is.EqualTo(0f).Within(.02f));
            Assert.That(Mathf.DeltaAngle(acceptedPitch, camera.transform.eulerAngles.x), Is.EqualTo(0f).Within(.02f),
                "The shot keeps the player's accepted pitch instead of adding camera recoil.");
            Assert.That(Mathf.DeltaAngle(0f, camera.transform.eulerAngles.z), Is.EqualTo(0f).Within(.02f));
            Vector3 position = root.Projectiles.LastPosition;
            Vector3 shotForward = (root.Hero.PistolAimPoint - position).normalized;
            root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(Vector3.Dot((root.Projectiles.LastPosition - position).normalized, shotForward), Is.GreaterThan(.99f));
            root.Tick(.1f);
            Assert.That(root.Opponent.State.Health, Is.EqualTo(health), "Free aiming away from the opponent must not auto-target it.");
            input.Release(mouse.leftButton, queueEventOnly: true);
            yield return null;

            Assert.That(root.PauseMenu.Open(), Is.True);
            yield return null;
            Assert.That(root.CameraFollow.FreeAimActive || root.Hero.Pistol.AimRequested, Is.False);
            Assert.That(Cursor.lockState, Is.EqualTo(cursorLock));
            Assert.That(Cursor.visible, Is.EqualTo(cursorVisible));
            input.Release(mouse.rightButton, queueEventOnly: true);
            yield return null;
            Assert.That(root.PauseMenu.Cancel(), Is.True);
            yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause did not release free aiming.");
            Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(1));

            input.Press(mouse.rightButton, queueEventOnly: true);
            yield return null;
            Assert.That(root.CameraFollow.FreeAimActive, Is.True);
            root.SendMessage("OnApplicationFocus", false);
            yield return null;
            Assert.That(root.CameraFollow.FreeAimActive || root.Hero.Pistol.AimRequested, Is.False);
            Assert.That(Cursor.lockState, Is.EqualTo(cursorLock));
            root.SendMessage("OnApplicationFocus", true);
            yield return null;
            Assert.That(root.Hero.Pistol.AimRequested, Is.False, "Returning focus with RMB held must wait for its release.");
            input.Release(mouse.rightButton, queueEventOnly: true);
            yield return null;
            input.Press(mouse.rightButton, queueEventOnly: true);
            yield return null;
            Assert.That(root.CameraFollow.FreeAimActive, Is.True);
            Assert.That(root.SetOpponentFocus(true), Is.True);
            Assert.That(root.CameraFollow.FreeAimActive, Is.False);
            Assert.That(root.CameraFollow.TargetLockActive, Is.True);
            Assert.That(Cursor.lockState, Is.EqualTo(cursorLock));
            input.Release(mouse.rightButton, queueEventOnly: true);
            yield return null;
            root.AutomaticSimulation = false;
            root.SetSparring(true);
        }

        private IEnumerator VerifyPistolAimGeometryAndTriggerBoundaries(InputTestFixture input, Mouse mouse)
        {
            root.AutomaticSimulation = false;
            PlacePair(6f);
            Assert.That(root.SetOpponentFocus(false), Is.True);
            Assert.That(root.Hero.CombatFocused, Is.False);
            Vector3[] targets =
            {
                new Vector3(.75f, .2f, 0f),
                new Vector3(1f, 1.35f, 0f),
                new Vector3(4f, 1.35f, 0f),
                new Vector3(20f, 1.35f, 0f)
            };
            string[] names = { "close low", "close level", "mid range", "far range" };
            for (int index = 0; index < targets.Length; index++)
            {
                root.Hero.SetPistolAim(true, targets[index]);
                Assert.That(root.Hero.PistolAimPoint, Is.EqualTo(targets[index]), names[index]);
                if (index == 0)
                {
                    for (int step = 0; step < 10; step++)
                    {
                        root.Tick(.025f);
                        ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
                        AssertPistolArmsClearBody($"Pistol raise step {step + 1}");
                    }
                    root.Tick(.95f);
                }
                else root.Tick(1.2f);
                AssertPistolAimAligned(names[index]);
                Assert.That(root.Hero.PistolSupportError, Is.LessThan(.02f), names[index]);
                int spawns = root.Projectiles.SpawnCount, rounds = root.Hero.Pistol.Rounds;
                Assert.That(root.Hero.RequestPistolShot(), Is.True);
                root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(spawns + 1), names[index]);
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(rounds - 1), names[index]);
            }

            yield return VerifyPistolTriggerStepBoundaries();

            // Use the player's RMB and mouse path as well as explicit aim points.
            // The old single-pass shoulder correction stayed over five degrees
            // away from this nearby floor point even after the body had settled.
            PlacePair(6f);
            Assert.That(root.SetOpponentFocus(false), Is.True);
            Assert.That(root.Hero.CombatFocused, Is.False);
            root.AutomaticSimulation = true;
            input.Press(mouse.rightButton, queueEventOnly: true);
            yield return null;
            Assert.That(root.Hero.Pistol.AimRequested && root.CameraFollow.FreeAimActive, Is.True);
            // Flush RMB before queuing another full mouse state; otherwise the
            // LMB event can replace the still-pending RMB press with released.
            input.Press(mouse.leftButton, queueEventOnly: true);
            yield return null;
            root.AutomaticSimulation = false;
            Assert.That(root.Projectiles.SpawnCount, Is.Zero, "An early mouse trigger must be rejected during the raise.");
            root.Tick(.4f);
            root.AutomaticSimulation = true;
            yield return null;
            Assert.That(root.Projectiles.SpawnCount, Is.Zero, "Holding the early trigger cannot fire when the raise completes.");
            input.Release(mouse.leftButton, queueEventOnly: true);
            yield return null;
            yield return VerifyMouseAimAtNearbyFloor(input, mouse);
            root.AutomaticSimulation = true;
            input.Release(mouse.rightButton, queueEventOnly: true);
            yield return null;
            root.AutomaticSimulation = false;
            root.Hero.SetPistolAim(false);
            for (int step = 0; step < 10; step++)
            {
                root.Tick(.025f);
                ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
                AssertPistolArmsClearBody($"Pistol lower step {step + 1}");
            }
            root.ResetRound();
        }

        private IEnumerator VerifyPistolTriggerStepBoundaries()
        {
            PlacePair(6f);
            root.Opponent.ResetActor(Vector3.left * 6f + Vector3.up * PlayerFactory.GroundedRootOffset, Vector3.right);
            Physics.SyncTransforms();
            Assert.That(root.SetOpponentFocus(false), Is.True);
            Assert.That(root.Hero.CombatFocused, Is.False);
            root.Hero.SetPistolAim(true, new Vector3(0f, 1.35f, 20f));
            Assert.That(root.Hero.PistolAimPoint, Is.EqualTo(new Vector3(0f, 1.35f, 20f)));
            float step = CombatTestRoot.SimulationStep;
            root.Tick(root.Hero.Pistol.Settings.RaiseSeconds - step);
            Assert.That(root.Hero.Pistol.IsRaising, Is.True);
            Assert.That(root.Hero.RequestPistolShot(), Is.True);
            root.Tick(step);
            Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(1), "The current substep's completed raise must be visible to commit.");
            Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(7));

            root.Tick(root.Hero.Pistol.Settings.FireCooldownSeconds - step);
            Assert.That(root.Hero.Pistol.CooldownRemaining, Is.GreaterThan(0f));
            Assert.That(root.Hero.RequestPistolShot(), Is.True);
            root.Tick(step);
            Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(2), "The current substep's expired cooldown must be visible to commit.");
            Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(6));

            Assert.That(root.Hero.RequestPistolShot(), Is.True);
            root.Tick(step);
            Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(2));
            Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(6));
            root.Tick(.6f);
            Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(2), "A rejected cooldown request cannot wait for a later ready step.");
            Assert.That(root.Casings.EjectionCount, Is.EqualTo(2));
            for (int remaining = root.Hero.Pistol.Rounds; remaining > 0; remaining--)
            {
                Assert.That(root.Hero.RequestPistolShot(), Is.True);
                root.Tick(.5f);
                Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(remaining - 1));
            }
            Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(8));
            Assert.That(root.Casings.EjectionCount, Is.EqualTo(8));
            Assert.That(root.Casings.PendingCount, Is.Zero);
            Assert.That(root.Hero.PistolSlideBack, Is.EqualTo(1f).Within(.001f),
                "The final accepted round leaves the actual slide locked open.");
            Vector3 emptySlide = PistolSlideLocalPosition();
            Assert.That(root.Hero.RequestPistolShot(), Is.True);
            root.Tick(.2f);
            Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(8));
            Assert.That(root.Casings.EjectionCount, Is.EqualTo(8), "An empty trigger cannot emit a spent case.");
            Assert.That(root.Casings.PendingCount, Is.Zero);
            float emptySlideDrift = Vector3.Distance(PistolSlideLocalPosition(), emptySlide);
            Assert.That(emptySlideDrift, Is.LessThan(.00001f),
                $"An empty trigger must retain the locked slide; actual drift={emptySlideDrift:R} m.");
            Assert.That(root.PauseMenu.Open(), Is.True);
            root.Tick(.8f);
            yield return null;
            emptySlideDrift = Vector3.Distance(PistolSlideLocalPosition(), emptySlide);
            Assert.That(emptySlideDrift, Is.LessThan(.00001f),
                $"Pause must retain the locked slide; actual drift={emptySlideDrift:R} m.");
            Assert.That(root.Casings.EjectionCount, Is.EqualTo(8));
            Assert.That(root.PauseMenu.Cancel(), Is.True);
            yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause did not release the empty pistol.");
            Assert.That(root.Hero.TryReloadPistol(), Is.True);
            AdvancePistolReloadTo(1.5f);
            Assert.That(root.Hero.PistolSlideBack, Is.GreaterThan(.95f));
            AssertPistolSlidePullContact("An empty reload racks the same locked-open slide");
            Assert.That(root.Hero.Pistol.Rounds, Is.Zero);
            root.Tick(.11f);
            Assert.That(root.Hero.PistolSlideBack, Is.LessThan(.01f));
            Assert.That(root.Hero.Pistol.Rounds, Is.Zero, "Releasing the empty slide cannot refill before the action completes.");
            root.Tick(.25f);
            Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(8));
            Assert.That(root.Casings.EjectionCount, Is.EqualTo(8), "Magazine exchange and racking do not manufacture fired cases.");
        }

        private Vector3 PistolSlideLocalPosition() => root.Hero.Weapon.transform.InverseTransformPoint(
            CombatPistolAssetProvider.FindAnchor(root.Hero.Weapon, "Slide").position);

        private void AdvancePistolReloadTo(float targetElapsed)
        {
            Assert.That(targetElapsed, Is.LessThan(root.Hero.Pistol.Settings.ReloadSeconds));
            // Root ticks include hitstop; the authored reload clock advances
            // only on live steps, including when its own outgoing bullet lands.
            for (int step = 0; step < 360 && root.Hero.Pistol.ReloadElapsed + .000001f < targetElapsed; step++)
            {
                Assert.That(root.Hero.Pistol.IsReloading, Is.True, "The requested magazine stage was interrupted.");
                root.Tick(CombatTestRoot.SimulationStep);
            }
            Assert.That(root.Hero.Pistol.ReloadElapsed,
                Is.InRange(targetElapsed - .000001f, targetElapsed + CombatTestRoot.SimulationStep + .000001f),
                "The live magazine clock must reach its requested phase with bounded work.");
        }

        private void AssertPistolReloadAudio(int expectedCount, RetroSfxId expectedCue)
        {
            Assert.That(root.Hero.PistolReloadCueCount, Is.EqualTo(expectedCount),
                "Only newly crossed live magazine/slide beats may schedule a reload sound.");
            Assert.That(root.Hero.LastPistolReloadCue, Is.EqualTo(expectedCue));
        }

        private static void AssertPistolMechanicalAudioClips()
        {
            RetroAudioService audio = RetroAudioService.Instance;
            Assert.That(audio, Is.Not.Null);
            var cues = new[]
            {
                RetroSfxId.PistolMagazineLatch, RetroSfxId.PistolMagazineRemove,
                RetroSfxId.PistolMagazineStow, RetroSfxId.PistolMagazineDraw,
                RetroSfxId.PistolMagazineInsert, RetroSfxId.PistolMagazineSeat,
                RetroSfxId.PistolSlidePull, RetroSfxId.PistolSlideRelease,
                RetroSfxId.PistolReady, RetroSfxId.PistolCasingEject, RetroSfxId.PistolCasingBounce
            };
            foreach (RetroSfxId cue in cues)
            {
                int variants = RetroSfxLibrary.GetDefinition(cue).VariantCount;
                for (int variant = 0; variant < variants; variant++)
                {
                    AudioClip clip = audio.GetClip(cue, variant);
                    Assert.That(clip, Is.Not.Null, $"The shared audio owner must generate {cue} variant {variant}.");
                    var samples = new float[clip.samples * clip.channels];
                    Assert.That(clip.GetData(samples, 0), Is.True);
                    float peak = 0f;
                    bool finite = true;
                    foreach (float sample in samples)
                    {
                        finite &= float.IsFinite(sample);
                        peak = Mathf.Max(peak, Mathf.Abs(sample));
                    }
                    Assert.That(finite, Is.True, $"{cue} variant {variant} must contain finite PCM.");
                    Assert.That(peak, Is.GreaterThan(.001f), $"{cue} variant {variant} cannot be a silent mechanical placeholder.");
                }
            }
        }

        private void AssertPistolMagazineContact(string context)
        {
            Assert.That(root.Hero.PistolMagazineInHand, Is.True, context);
            Transform magazine = root.Hero.PistolMagazineTransform;
            Assert.That(magazine, Is.Not.Null, context);
            Assert.That(magazine.gameObject.activeInHierarchy && magazine.IsChildOf(root.Hero.transform), Is.True, context);
            bool visible = false;
            foreach (Renderer renderer in magazine.GetComponentsInChildren<Renderer>())
                visible |= renderer.enabled && renderer.bounds.size.sqrMagnitude > .0001f;
            Assert.That(visible, Is.True, "Magazine exchange requires actual authored geometry, not an empty contact transform.");
            Vector3 grip = CombatPistolAssetProvider.FindAnchor(magazine.gameObject, "Grip").position;
            Vector3 palm = root.Hero.GetComponentInChildren<NpcHandPose>().CylinderCentre(true);
            Assert.That(Vector3.Distance(grip, palm), Is.LessThan(.004f), context);
        }

        private void AssertPistolSlidePullContact(string context)
        {
            Assert.That(root.Hero.PistolSlidePull, Is.Not.Null, context);
            Vector3 palm = root.Hero.GetComponentInChildren<NpcHandPose>().CylinderCentre(true);
            Assert.That(Vector3.Distance(root.Hero.PistolSlidePull.position, palm), Is.LessThan(.004f), context);
        }

        private IEnumerator VerifyMouseAimAtNearbyFloor(InputTestFixture input, Mouse mouse)
        {
            Assert.That(root.CameraFollow.FreeAimActive, Is.True);
            Camera camera = root.CameraFollow.Camera;
            float yaw = camera.transform.eulerAngles.y;
            float pitch = Mathf.DeltaAngle(0f, camera.transform.eulerAngles.x);
            input.Set(mouse.delta, new Vector2(Mathf.DeltaAngle(yaw, 90f) / .16f, (pitch - 55f) / .14f), queueEventOnly: true);
            root.AutomaticSimulation = true;
            yield return null;
            root.AutomaticSimulation = false;
            root.Tick(1.2f);
            root.AutomaticSimulation = true;
            yield return null;
            root.AutomaticSimulation = false;
            Vector3 target = root.Hero.PistolAimPoint;
            Assert.That(target.y, Is.LessThan(.2f), "Mouse aiming down must select the actual nearby floor.");
            Assert.That(Vector3.Distance(target, root.Hero.transform.position), Is.InRange(.5f, 2f));
            AssertPistolAimAligned("Mouse aim at the nearby floor must converge after settling");
            Assert.That(root.Hero.PistolSupportError, Is.LessThan(.02f));
            var hands = root.Hero.GetComponentInChildren<NpcHandPose>();
            float gripForward = Vector3.Dot(hands.CylinderCentre(false) - root.Hero.transform.position,
                root.Hero.transform.forward);
            Assert.That(gripForward, Is.GreaterThanOrEqualTo(.30f),
                $"Low aim must keep the grip ahead of the torso; forward={gripForward:F4}m.");
            yield return CapturePistolNearViews(root.RoundFinished ? "winner-aim-low" : "aim-low");

            int spawns = root.Projectiles.SpawnCount, rounds = root.Hero.Pistol.Rounds;
            int impacts = root.Projectiles.ImpactCount;
            bool finished = root.RoundFinished;
            float opponentHealth = root.Opponent.State.Health;
            Pose stationaryCamera = new Pose(camera.transform.position, camera.transform.rotation);
            Vector3 stationaryRoot = root.Hero.transform.position;
            input.Press(mouse.leftButton, queueEventOnly: true);
            root.AutomaticSimulation = true;
            yield return null;
            root.AutomaticSimulation = false;
            Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(spawns + 1));
            Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(rounds - 1));
            root.Tick(.04f);
            yield return null;
            Assert.That(Vector3.Distance(root.Hero.transform.position, stationaryRoot), Is.LessThan(.001f));
            AssertStablePistolCamera(stationaryCamera, "The free mouse aim stays fixed through the arm and slide firing peak");
            root.Tick(.16f);
            Assert.That(root.Projectiles.ImpactCount, Is.EqualTo(impacts + 1));
            Assert.That(Vector3.Distance(root.Projectiles.LastImpactPoint, target), Is.LessThan(.15f));
            root.Tick(.5f);
            root.AutomaticSimulation = true;
            yield return null;
            Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(spawns + 1), "Holding LMB after a floor shot cannot repeat it.");
            Assert.That(root.RoundFinished, Is.EqualTo(finished));
            Assert.That(root.Opponent.State.Health, Is.EqualTo(opponentHealth));
            input.Release(mouse.leftButton, queueEventOnly: true);
            yield return null;
            root.AutomaticSimulation = false;
        }

        private void AssertStablePistolCamera(Pose expected, string context)
        {
            Transform camera = root.CameraFollow.Camera.transform;
            Assert.That(Quaternion.Angle(expected.rotation, camera.rotation), Is.LessThan(.02f), context);
            Assert.That(Vector3.Distance(expected.position, camera.position), Is.LessThan(.003f), context);
        }

        private void AssertPistolAimAligned(string context)
        {
            // A coroutine resumes before LateUpdate. Measure the completed
            // firearm pose used by Present/commit, not the graph-only interim pose.
            ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
            string detail = $"{context}: error={root.Hero.PistolAimErrorDegrees:F3}, " +
                $"origin={root.Hero.PistolMuzzle.position:F4}, forward={root.Hero.PistolMuzzle.forward:F4}, target={root.Hero.PistolAimPoint:F4}";
            Assert.That(root.Hero.PistolAimAligned, Is.True, detail);
            Assert.That(root.Hero.PistolAimErrorDegrees, Is.LessThanOrEqualTo(CombatActor.MaximumPistolAimErrorDegrees), detail);
            AssertPistolWrist(context, 35f);
            AssertPistolWrist(context, 45f, true);
            AssertPistolHandsClear(context);
            AssertPistolArmsClearBody(context);
        }

        private void AssertPistolWrist(string context, float maximumBend, bool isLeft = false)
        {
            var hands = root.Hero.GetComponentInChildren<NpcHandPose>();
            NpcHandPose.HandBinding binding = null;
            foreach (NpcHandPose.HandBinding hand in hands.Hands)
                if (hand.IsLeft == isLeft) binding = hand;
            Assert.That(binding, Is.Not.Null);
            // Remove the palm's depth offset to recover the actual hand length,
            // independently of source/FBX bone axes and their handedness.
            Vector3 distal = Vector3.ProjectOnPlane(hands.CylinderCentre(isLeft) - binding.Hand.position,
                hands.PalmNormal(isLeft)).normalized;
            string side = isLeft ? "left" : "right";
            Transform forearm = CityPedestrianHandProps.FindSocket(root.Hero.DamageRigRoot, isLeft ? "forearm.L" : "forearm.R");
            Assert.That(forearm, Is.Not.Null);
            float bend = Vector3.Angle(binding.Hand.position - forearm.position, distal);
            Assert.That(Vector3.Dot(distal, root.Hero.PistolMuzzle.forward), Is.GreaterThan(.99f),
                context + $": the barrel must follow the {side} hand's length.");
            if (!(bend < maximumBend))
                RecordPistolGeometryIssue(context + $": {side} wrist bends {bend:F2} degrees; limit={maximumBend:F2}.");
        }

        private sealed class PistolHandSurface
        {
            public string Name;
            public Vector3[] Vertices;
            public int[] Triangles;
            public Bounds Bounds;
            public Vector3 ShoulderSeamOrigin, ShoulderSeamAxis;
            public float ShoulderSeamAllowance;
        }

        private static Vector3 pistolEdgeIntersection;

        private void AssertPistolHandsClear(string context)
        {
            // Anchor contact alone cannot detect fingers passing through the
            // other palm. Check the visible, skinned hand geometry as rendered.
            var sides = new[] { new List<PistolHandSurface>(), new List<PistolHandSurface>() };
            var scratch = new Mesh();
            try
            {
                var hands = root.Hero.GetComponentInChildren<NpcHandPose>();
                foreach (NpcHandPose.HandBinding hand in hands.Hands)
                foreach (SkinnedMeshRenderer skin in hand.Renderers)
                    sides[hand.IsLeft ? 0 : 1].Add(BakePistolSurface(skin, scratch));
            }
            finally { Object.DestroyImmediate(scratch); }
            Assert.That(sides[0].Count, Is.EqualTo(6));
            Assert.That(sides[1].Count, Is.EqualTo(6));
            foreach (PistolHandSurface left in sides[0])
            foreach (PistolHandSurface right in sides[1])
                AssertPistolSurfacesClear(left, right, context);
        }

        private void AssertPistolArmsClearBody(string context)
        {
            var arms = new List<PistolHandSurface>();
            var body = new List<PistolHandSurface>();
            var scratch = new Mesh();
            try
            {
                var registry = ((Player3DCharacterPresentation)root.Player.Visual).Registry;
                foreach (Player3DMeshBinding binding in registry.MeshBindings)
                {
                    bool lowerArm = binding.BodyGroup == "LeftLowerArm" || binding.BodyGroup == "RightLowerArm";
                    bool upperArm = binding.BodyGroup == "LeftUpperArm" || binding.BodyGroup == "RightUpperArm";
                    if (!lowerArm && !upperArm && binding.BodyGroup != "Body") continue;
                    Renderer renderer = binding.Renderer;
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.forceRenderingOff)
                        continue;
                    Assert.That(renderer, Is.InstanceOf<SkinnedMeshRenderer>(), "Visible hero surfaces must use the world rig.");
                    PistolHandSurface surface = BakePistolSurface((SkinnedMeshRenderer)renderer, scratch);
                    if (upperArm)
                    {
                        Transform shoulder = CityPedestrianHandProps.FindSocket(root.Hero.DamageRigRoot, binding.BoneName);
                        Transform elbow = CityPedestrianHandProps.FindSocket(root.Hero.DamageRigRoot,
                            binding.AnatomicalSide == "Left" ? "forearm.L" : "forearm.R");
                        Assert.That(shoulder, Is.Not.Null);
                        Assert.That(elbow, Is.Not.Null);
                        surface.ShoulderSeamOrigin = shoulder.position;
                        surface.ShoulderSeamAxis = (elbow.position - shoulder.position).normalized;
                        surface.ShoulderSeamAllowance = .10f;
                    }
                    (lowerArm || upperArm ? arms : body).Add(surface);
                }
            }
            finally { Object.DestroyImmediate(scratch); }
            Assert.That(arms.Count, Is.GreaterThan(0), "Visible forearms and hands must be measured.");
            Assert.That(body.Count, Is.GreaterThan(0), "Visible torso surfaces must be measured.");
            // Preserve only the natural shoulder attachment; the rest of each
            // upper arm, and every forearm/cuff/hand triangle, must clear the body.
            foreach (PistolHandSurface arm in arms)
            foreach (PistolHandSurface torso in body)
                AssertPistolSurfacesClear(arm, torso, context);
        }

        private static PistolHandSurface BakePistolSurface(SkinnedMeshRenderer skin, Mesh scratch)
        {
            skin.BakeMesh(scratch, true);
            Vector3[] vertices = scratch.vertices;
            var bounds = new Bounds();
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = skin.transform.TransformPoint(vertices[i]);
                if (i == 0) bounds = new Bounds(vertices[i], Vector3.zero);
                else bounds.Encapsulate(vertices[i]);
            }
            return new PistolHandSurface
            {
                Name = skin.name, Vertices = vertices,
                Triangles = scratch.triangles, Bounds = bounds
            };
        }

        private void AssertPistolSurfacesClear(PistolHandSurface left, PistolHandSurface right, string context)
        {
            if (!left.Bounds.Intersects(right.Bounds)) return;
            for (int l = 0; l < left.Triangles.Length; l += 3)
            {
                if (IsPistolShoulderSeam(left, l)) continue;
                Vector3 a = left.Vertices[left.Triangles[l]], b = left.Vertices[left.Triangles[l + 1]],
                    c = left.Vertices[left.Triangles[l + 2]];
                for (int r = 0; r < right.Triangles.Length; r += 3)
                {
                    if (IsPistolShoulderSeam(right, r)) continue;
                    Vector3 d = right.Vertices[right.Triangles[r]], e = right.Vertices[right.Triangles[r + 1]],
                        f = right.Vertices[right.Triangles[r + 2]];
                    if (PistolHandEdgeCrosses(a, b, d, e, f) || PistolHandEdgeCrosses(b, c, d, e, f) ||
                        PistolHandEdgeCrosses(c, a, d, e, f) || PistolHandEdgeCrosses(d, e, a, b, c) ||
                        PistolHandEdgeCrosses(e, f, a, b, c) || PistolHandEdgeCrosses(f, d, a, b, c))
                    {
                        Vector3 leftCentroid = (a + b + c) / 3f, rightCentroid = (d + e + f) / 3f;
                        float leftProjection = Vector3.Dot(leftCentroid - left.ShoulderSeamOrigin, left.ShoulderSeamAxis);
                        float rightProjection = Vector3.Dot(rightCentroid - right.ShoulderSeamOrigin, right.ShoulderSeamAxis);
                        RecordPistolGeometryIssue($"{context}: visible surfaces intersect: {left.Name} / {right.Name}; " +
                            $"triangles={l / 3}/{r / 3}, centroids={leftCentroid:F5}/{rightCentroid:F5}, " +
                            $"edgeBarycentricAndTime={pistolEdgeIntersection:F8}, " +
                            $"seamProjection={leftProjection:F5}/{rightProjection:F5}, " +
                            $"seamAllowance={left.ShoulderSeamAllowance:F3}/{right.ShoulderSeamAllowance:F3}.");
                        return;
                    }
                }
            }
        }

        private void RecordPistolGeometryIssue(string issue)
        {
            Transform frame = root.Hero.transform;
            string detail = issue + $" hero={frame.position:F4}, yaw={frame.eulerAngles.y:F2}";
            foreach (string side in new[] { "R", "L" })
            {
                foreach (string joint in new[] { "upper_arm", "forearm", "hand" })
                {
                    Transform bone = CityPedestrianHandProps.FindSocket(root.Hero.DamageRigRoot, joint + "." + side);
                    detail += bone != null ? $" {joint}.{side}Local={frame.InverseTransformPoint(bone.position):F4}"
                        : $" {joint}.{side}=missing";
                }
            }
            detail += $" target={root.Hero.PistolAimPoint:F4}, targetLocal={frame.InverseTransformPoint(root.Hero.PistolAimPoint):F4}, " +
                $"muzzle={root.Hero.PistolMuzzle.position:F4}, muzzleLocal={frame.InverseTransformPoint(root.Hero.PistolMuzzle.position):F4}, " +
                $"muzzleForward={root.Hero.PistolMuzzle.forward:F4}.";
            pistolGeometryIssues.Add(detail);
            LogAssert.Expect(LogType.Warning, detail);
            Debug.LogWarning(detail);
        }

        private static bool IsPistolShoulderSeam(PistolHandSurface surface, int triangle)
        {
            if (surface.ShoulderSeamAllowance <= 0f) return false;
            Vector3 centroid = (surface.Vertices[surface.Triangles[triangle]] +
                surface.Vertices[surface.Triangles[triangle + 1]] +
                surface.Vertices[surface.Triangles[triangle + 2]]) / 3f;
            return Vector3.Dot(centroid - surface.ShoulderSeamOrigin, surface.ShoulderSeamAxis) < surface.ShoulderSeamAllowance;
        }

        private static bool PistolHandEdgeCrosses(Vector3 start, Vector3 end, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 edge = end - start, ab = b - a, ac = c - a;
            Vector3 cross = Vector3.Cross(edge, ac);
            float determinant = Vector3.Dot(ab, cross);
            if (Mathf.Abs(determinant) < 1e-10f) return false;
            float inverse = 1f / determinant;
            Vector3 fromA = start - a;
            float u = Vector3.Dot(fromA, cross) * inverse;
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(fromA, ab);
            float v = Vector3.Dot(edge, q) * inverse;
            if (v < 0f || u + v > 1f) return false;
            float t = Vector3.Dot(ac, q) * inverse;
            // Surface contact is allowed; an edge must enter the other mesh.
            float endpointTolerance = .00002f / Mathf.Max(edge.magnitude, .00002f);
            bool crosses = t > endpointTolerance && t < 1f - endpointTolerance;
            if (crosses) pistolEdgeIntersection = new Vector3(u, v, t);
            return crosses;
        }

        private IEnumerator VerifyPistolCarryAndWinnerInput(InputTestFixture input, Mouse mouse, Keyboard keyboard)
        {
            PlacePair(6f);
            Assert.That(root.SetOpponentFocus(false), Is.True);
            root.Tick(.6f);
            for (int frame = 0; frame < 24; frame++) yield return null;
            AssertPistolOrdinaryIdle();
            yield return CapturePistolNearViews("idle-free");

            // Win with the same live projectiles used in play, then exercise
            // the winner rather than silently starting another duel.
            Assert.That(root.SetOpponentFocus(true), Is.True);
            root.Hero.SetPistolAim(true);
            root.Tick(.3f);
            for (int shot = 0; shot < 8 && !root.RoundFinished; shot++)
            {
                Assert.That(root.Hero.RequestPistolShot(), Is.True);
                root.Tick(.5f);
            }
            Assert.That(root.RoundFinished, Is.True);
            Assert.That(root.Hero.State.IsDefeated, Is.False);
            Assert.That(root.Opponent.State.Health, Is.Zero);
            root.Tick(2f);
            for (int frame = 0; frame < 24; frame++) yield return null;
            Assert.That(root.Hero.CombatFocused || root.CameraFollow.TargetLockActive, Is.False);
            AssertPistolOrdinaryIdle();
            yield return CapturePistolNearViews("idle-winner");

            int rounds = root.Hero.Pistol.Rounds;
            Assert.That(rounds, Is.LessThan(8));
            input.Press(keyboard.rKey, queueEventOnly: true);
            root.AutomaticSimulation = true;
            yield return null;
            input.Release(keyboard.rKey, queueEventOnly: true);
            yield return null;
            root.AutomaticSimulation = false;
            Assert.That(root.Hero.Pistol.IsReloading, Is.True, "R must still reload after victory.");
            int reloadSpawns = root.Projectiles.SpawnCount;
            Assert.That(root.Hero.RequestPistolShot(), Is.True);
            root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(reloadSpawns), "A winner cannot fire during reload.");
            Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(rounds));
            root.Tick(.9f);
            float progress = root.Hero.Pistol.ReloadProgress;
            Assert.That(progress, Is.GreaterThan(0f));
            yield return CapturePistolNearViews("winner-reload");
            Assert.That(root.PauseMenu.Open(), Is.True);
            root.Tick(.8f);
            Assert.That(root.Hero.Pistol.ReloadProgress, Is.EqualTo(progress));
            Assert.That(root.PauseMenu.Cancel(), Is.True);
            yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause did not release the pistol winner.");
            root.Tick(1.2f);
            Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(8));

            root.AutomaticSimulation = true;
            input.Press(mouse.rightButton, queueEventOnly: true);
            yield return null;
            Assert.That(root.CameraFollow.FreeAimActive, Is.True, "The winner must retain mouse aiming.");
            float yaw = root.CameraFollow.Camera.transform.eulerAngles.y;
            input.Set(mouse.delta, new Vector2(80f, -10f), queueEventOnly: true);
            yield return null;
            Assert.That(Mathf.DeltaAngle(yaw, root.CameraFollow.Camera.transform.eulerAngles.y), Is.EqualTo(12.8f).Within(.1f));
            root.Tick(.6f);
            root.AutomaticSimulation = false;
            Assert.That(root.Hero.Pistol.IsAiming, Is.True);
            Assert.That(root.Hero.PistolSupportError, Is.LessThan(.02f), "The winner's left palm must support the held pistol.");
            yield return CapturePistolNearViews("winner-aim");
            root.AutomaticSimulation = true;
            int spawned = root.Projectiles.SpawnCount;
            input.Press(mouse.leftButton, queueEventOnly: true);
            yield return null;
            root.AutomaticSimulation = false;
            Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(7));
            Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(spawned + 1));
            Assert.That(root.Projectiles.ActiveCount, Is.EqualTo(1));
            Vector3 bullet = root.Projectiles.LastPosition;
            root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(root.Projectiles.ActiveCount, Is.EqualTo(1), "Finished-round ticks cannot clear a new bullet.");
            Assert.That(Vector3.Distance(bullet, root.Projectiles.LastPosition), Is.GreaterThan(1f));
            root.Tick(.15f);
            Assert.That(root.RoundFinished, Is.True);
            Assert.That(root.Opponent.State.Health, Is.Zero, "Free shooting cannot revive or restart the defeated opponent.");
            input.Release(mouse.leftButton, queueEventOnly: true);
            root.AutomaticSimulation = true;
            yield return null;
            yield return VerifyMouseAimAtNearbyFloor(input, mouse);
            root.AutomaticSimulation = true;
            input.Release(mouse.rightButton, queueEventOnly: true);
            yield return null;
            root.AutomaticSimulation = false;
            root.Tick(.4f);
            for (int frame = 0; frame < 24; frame++) yield return null;
            AssertPistolOrdinaryIdle();
            root.ResetRound();
            Assert.That(root.RoundFinished, Is.False);
            Assert.That(root.Hero.Pistol.Rounds, Is.EqualTo(8));
            Assert.That(root.Projectiles.ActiveCount, Is.Zero);
        }

        private void AssertPistolOrdinaryIdle()
        {
            var visual = (Player3DCharacterPresentation)root.Player.Visual;
            visual.ReapplyLatePresentationPose();
            Assert.That(visual.OwnsClip(root.Hero), Is.False, "The ordinary presentation owns the idle body.");
            Assert.That(visual.OwnsCarryPose(root.Hero), Is.True);
            Assert.That(visual.CurrentLocomotionState, Is.EqualTo(Player3DLocomotionState.Idle));
            Assert.That(root.Hero.CombatFocused || root.Hero.Pistol.AimRequested || root.Hero.Pistol.ReloadPending, Is.False);
            var hands = root.Hero.GetComponentInChildren<NpcHandPose>();
            Assert.That(Vector3.Distance(root.Hero.Weapon.transform.position, hands.CylinderCentre(false)), Is.LessThan(.001f));
            Assert.That(Vector3.Angle(root.Hero.Weapon.transform.up, hands.CylinderAxis(false)), Is.LessThan(.1f),
                "The pistol's handle must follow the ordinary right palm orientation.");
            Assert.That(hands.RightGripWeight, Is.EqualTo(1f));
            Assert.That(hands.LeftGripWeight, Is.Zero);

            var bones = new List<Transform> { visual.Registry.Anchors.Pelvis, visual.Registry.Anchors.Spine, visual.Registry.Anchors.Chest };
            foreach (string joint in new[] { "upper_arm.L", "forearm.L", "hand.L", "upper_arm.R", "forearm.R", "hand.R" })
                bones.Add(CityPedestrianHandProps.FindSocket(root.Hero.DamageRigRoot, joint));
            var actual = new Pose[bones.Count];
            for (int index = 0; index < bones.Count; index++)
            {
                Assert.That(bones[index], Is.Not.Null, "Ordinary idle must expose both arms and the torso.");
                actual[index] = new Pose(bones[index].localPosition, bones[index].localRotation);
            }

            // Save the visible candidate first. Removing only the carry layer
            // then samples the shared ordinary graph at the exact same clock,
            // including its idle breath, rather than comparing to a rigid pose.
            Assert.That(visual.UpdateCarryPose(root.Hero, 0f, 0f), Is.True);
            visual.ReapplyLatePresentationPose();
            for (int index = 0; index < bones.Count; index++)
            {
                Transform bone = bones[index];
                Assert.That(Vector3.Distance(actual[index].position, bone.localPosition), Is.LessThan(.0005f),
                    bone.name + ": an unaimed pistol must retain the ordinary idle joint position.");
                Assert.That(Quaternion.Angle(actual[index].rotation, bone.localRotation), Is.LessThan(.1f),
                    bone.name + ": an unaimed pistol must retain the ordinary idle joint rotation.");
            }
        }

        [System.Serializable]
        private sealed class PistolJournalRecord
        {
            public string @event;
            public PistolJournalData data;
        }

        [System.Serializable]
        private sealed class PistolJournalData
        {
            public int actor;
            public int request;
            public int rounds;
            public string grip;
            public string reason;
            public float aim_error_degrees;
        }

        private static void AssertPistolJournalSnapshots(string folder, string session)
        {
            bool heroState = false, heroPresentation = false, opponentArm = false, opponentGrip = false;
            bool pistolState = false;
            var requests = new HashSet<int>();
            var terminals = new Dictionary<int, int>();
            var rejectionReasons = new HashSet<string>();
            int results = 0;
            foreach (string path in Directory.GetFiles(folder, "duel.ndjson", SearchOption.AllDirectories))
            {
                if (!Path.GetDirectoryName(path).Contains(session)) continue;
                foreach (string line in File.ReadAllLines(path))
                {
                    PistolJournalRecord record = JsonUtility.FromJson<PistolJournalRecord>(line);
                    if (record.@event == "round_result") results++;
                    if (record.data.actor == 1)
                    {
                        Assert.That(record.@event, Is.Not.EqualTo("arm_snapshot").And.Not.EqualTo("grip_snapshot"),
                            "A pistol has no crowbar support solver to measure.");
                        heroState |= record.@event == "state";
                        if (record.@event == "pistol_state")
                        {
                            pistolState = true;
                            Assert.That(record.data.rounds, Is.InRange(0, 8));
                        }
                        if (record.@event == "pistol_shot_requested")
                        {
                            Assert.That(record.data.request, Is.GreaterThan(0));
                            Assert.That(requests.Add(record.data.request), Is.True, "Every trigger needs its own diagnostic request.");
                        }
                        if (record.@event is "pistol_fired" or "pistol_shot_rejected")
                        {
                            Assert.That(record.data.request, Is.GreaterThan(0));
                            terminals.TryGetValue(record.data.request, out int count);
                            terminals[record.data.request] = count + 1;
                            if (record.@event == "pistol_shot_rejected")
                            {
                                Assert.That(record.data.reason, Is.Not.Null.And.Not.Empty);
                                rejectionReasons.Add(record.data.reason);
                            }
                            else Assert.That(record.data.aim_error_degrees, Is.LessThanOrEqualTo(CombatActor.MaximumPistolAimErrorDegrees));
                        }
                        if (record.@event == "presentation")
                        {
                            heroPresentation = true;
                            Assert.That(record.data.grip, Is.EqualTo("None"));
                        }
                    }
                    if (record.data.actor == 2)
                    {
                        opponentArm |= record.@event == "arm_snapshot";
                        opponentGrip |= record.@event == "grip_snapshot";
                    }
                }
            }
            Assert.That(heroState && heroPresentation && opponentArm && opponentGrip, Is.True,
                "The same journal must retain pistol state/pose and the opponent's crowbar measurements.");
            Assert.That(pistolState, Is.True, "Pistol snapshots must expose the state behind accepted and rejected triggers.");
            Assert.That(requests, Is.Not.Empty);
            CollectionAssert.AreEquivalent(requests, terminals.Keys, "Every requested trigger must finish with a shot or a rejection.");
            foreach (int request in requests)
                Assert.That(terminals[request], Is.EqualTo(1), $"Trigger {request} must have exactly one terminal outcome.");
            foreach (string reason in new[] { "raising", "cooldown", "reloading", "aim_unaligned", "muzzle_blocked" })
                Assert.That(rejectionReasons, Does.Contain(reason), "The deliberately rejected trigger must retain its exact reason.");
            Assert.That(results, Is.EqualTo(1), "Post-victory input cannot publish the duel result again.");
        }

        private IEnumerator AwaitSelectedCombatRange()
        {
            yield return WaitFor(() =>
            {
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                return root != null && root.IsInitialized && !SceneTransitionService.IsTransitioning &&
                    GameInput.CanRead(GameInputContext.Gameplay);
            }, "The selected combat loadout did not initialize.");
            root.AutomaticSimulation = false;
        }

        private void CapturePistolHandContact(Camera camera, string name)
        {
            ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
            var hands = root.Hero.GetComponentInChildren<NpcHandPose>();
            Transform grip = CombatPistolAssetProvider.FindAnchor(root.Hero.Weapon, "Grip");
            Assert.That(Vector3.Distance(grip.position, hands.CylinderCentre(false)), Is.LessThan(.001f));
            Assert.That(hands.RightGripWeight, Is.EqualTo(1f));
            foreach (NpcHandPose.HandBinding binding in hands.Hands)
                if (!binding.IsLeft)
                    foreach (SkinnedMeshRenderer skin in binding.Renderers)
                        Assert.That(skin.GetBlendShapeWeight(skin.sharedMesh.GetBlendShapeIndex(hands.ShapeName)), Is.EqualTo(100f));
                else if (root.Hero.Pistol.IsAiming)
                {
                    Assert.That(root.Hero.PistolSupportError, Is.LessThan(.02f), "The left hand must keep physical support at the pistol.");
                    Assert.That(hands.LeftGripWeight, Is.EqualTo(CombatPistolAssetProvider.SupportGripWeight));
                    foreach (SkinnedMeshRenderer skin in binding.Renderers)
                        Assert.That(skin.GetBlendShapeWeight(skin.sharedMesh.GetBlendShapeIndex(hands.ShapeName)),
                            Is.EqualTo(CombatPistolAssetProvider.SupportGripWeight * 100f));
                }
            if (root.Hero.Pistol.IsAiming && !root.Hero.Pistol.IsReloading)
            {
                AssertPistolWrist(name, 35f);
                AssertPistolWrist(name, 45f, true);
                AssertPistolHandsClear(name);
                AssertPistolArmsClearBody(name);
            }
            string path = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest, name + ".png");
            LogAssert.Expect(LogType.Log, "Area capture wrote " + path);
            AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.CombatTest, name);
            Assert.That(Vector3.Distance(grip.position, hands.CylinderCentre(false)), Is.LessThan(.001f));
        }

        private IEnumerator CapturePistolWoundNearView(CombatActor actor, string name, int index = 0)
        {
            Camera camera = root.CameraFollow.Camera;
            Vector3 originalPosition = camera.transform.position;
            Quaternion originalRotation = camera.transform.rotation;
            float originalFieldOfView = camera.fieldOfView, originalNearPlane = camera.nearClipPlane;
            bool followEnabled = root.CameraFollow.enabled;
            try
            {
                root.CameraFollow.enabled = false;
                yield return null;
                Assert.That(root.BloodEffects.TryGetProjectileWound(actor, index, out Vector3 point, out Vector3 direction), Is.True,
                    "The close view uses the wound's current skinned surface after any ragdoll movement.");
                Assert.That(direction.sqrMagnitude, Is.GreaterThan(.9f));
                bool visibleSkin = false;
                foreach (SkinnedMeshRenderer skin in actor.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!skin.name.StartsWith("Projectile Wounds__", System.StringComparison.Ordinal) || !skin.enabled) continue;
                    visibleSkin = true;
                    Assert.That(skin.sharedMesh, Is.Not.Null);
                    Assert.That(skin.sharedMesh.vertexCount, Is.GreaterThan(0));
                    Assert.That(skin.bones, Is.Not.Empty);
                    foreach (Transform bone in skin.bones)
                        Assert.That(bone != null && bone.IsChildOf(actor.DamageRigRoot), Is.True,
                            "The hole must deform with the original actor rather than a detached proxy.");
                }
                Assert.That(visibleSkin, Is.True, "The persistent wound has a live visible skinned overlay.");
                Vector3 normal = direction.normalized;
                Vector3 side = Vector3.Cross(normal, Mathf.Abs(normal.y) > .9f ? Vector3.forward : Vector3.up).normalized;
                Vector3 eye = point + normal * .42f + side * .07f;
                Vector3 cameraUp = Vector3.ProjectOnPlane(Mathf.Abs(normal.y) > .9f ? Vector3.forward : Vector3.up, normal).normalized;
                camera.fieldOfView = 34f;
                camera.nearClipPlane = .015f;
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(point - eye, cameraUp));
                string path = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest, name + ".png");
                LogAssert.Expect(LogType.Log, "Area capture wrote " + path);
                AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.CombatTest, name);
            }
            finally
            {
                root.CameraFollow.enabled = followEnabled;
                camera.fieldOfView = originalFieldOfView;
                camera.nearClipPlane = originalNearPlane;
                camera.transform.SetPositionAndRotation(originalPosition, originalRotation);
            }
        }

        private IEnumerator CapturePistolNearViews(string action)
        {
            Camera camera = root.CameraFollow.Camera;
            Transform actor = root.Hero.transform;
            Vector3 originalPosition = camera.transform.position;
            Quaternion originalRotation = camera.transform.rotation;
            float originalFieldOfView = camera.fieldOfView;
            bool followEnabled = root.CameraFollow.enabled;
            Vector3 target = root.Hero.Ragdoll.PhysicsController.ChestBody.position + actor.forward * .15f + actor.up * .06f;
            try
            {
                root.CameraFollow.enabled = false;
                camera.fieldOfView = 45f;
                Vector3 front = actor.position + actor.forward * 1.9f + actor.up * 1.45f;
                camera.transform.SetPositionAndRotation(front, Quaternion.LookRotation(target - front, actor.up));
                yield return null;
                CapturePistolHandContact(camera, "pistol-" + action + "-front");
                Vector3 side = actor.position + actor.right * 1.7f + actor.forward * .25f + actor.up * 1.45f;
                camera.transform.SetPositionAndRotation(side, Quaternion.LookRotation(target - side, actor.up));
                yield return null;
                CapturePistolHandContact(camera, "pistol-" + action + "-side");
                if (root.Hero.Pistol.IsAiming && !root.Hero.Pistol.IsReloading)
                {
                    Vector3 hands = CombatPistolAssetProvider.FindAnchor(root.Hero.Weapon, "Grip").position;
                    Vector3 eye = hands + actor.forward * .6f + actor.right * .28f + actor.up * .16f;
                    camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(hands - eye, actor.up));
                    yield return null;
                    CapturePistolHandContact(camera, "pistol-" + action + "-hands");
                }
            }
            finally
            {
                root.CameraFollow.enabled = followEnabled;
                camera.fieldOfView = originalFieldOfView;
                camera.transform.SetPositionAndRotation(originalPosition, originalRotation);
            }
        }
    }

    // CameraFollow runs at order 100. Sample after its real LateUpdate;
    // WaitForEndOfFrame does not resume reliably in batch-mode tests.
    [DefaultExecutionOrder(20000)]
    public sealed class PistolCameraContinuityProbe : MonoBehaviour
    {
        public System.Action Sample;
        public int CompletedFrame { get; private set; } = -1;
        private void LateUpdate()
        {
            CompletedFrame = Time.frameCount;
            Sample?.Invoke();
        }
    }
}
