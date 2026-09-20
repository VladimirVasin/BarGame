using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Continuous charged windup on both original rigs: wrists, elbow bends and the rendered two-hand grip.")]
        [PrebuildSetup(typeof(CombatTestAssetsSetup))]
        public IEnumerator CombatChargeArmAlignment()
        {
            float captureDelta = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest);
                var root = Object.FindAnyObjectByType<CombatTestRoot>();
                Assert.That(root, Is.Not.Null);
                root.AutomaticSimulation = false;
                foreach (CombatActor actor in new[] { root.Hero, root.Opponent })
                {
                    PlaceTwoHandPosePair(root);
                    for (int frame = 0; frame < 24; frame++) { root.Tick(1f / 60f); yield return null; }
                    Assert.That(actor.RequestCharge(), Is.True);
                    string subject = actor == root.Hero ? "hero" : "opponent";
                    for (int frame = 0; frame <= 60; frame++)
                    {
                        if (frame > 0) root.Tick(actor.State.Settings.ChargeSeconds / 60f);
                        yield return null;
                        ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
                        AssertTwoHandWeaponContact(actor);
                        if (frame % 15 == 0)
                            CaptureTwoHandActorViews(root.CameraFollow.Camera, actor,
                                "charge-alignment-" + subject + "-" + frame, upperBody: true);
                        AssertChargingArmAlignment(actor);
                    }
                    Vector3 tip = actor.Weapon.transform.position;
                    Quaternion rotation = actor.Weapon.transform.rotation;
                    Assert.That(actor.ReleaseCharge(), Is.True);
                    ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
                    Assert.That(Vector3.Distance(tip, actor.Weapon.transform.position), Is.LessThan(.008f));
                    Assert.That(Quaternion.Angle(rotation, actor.Weapon.transform.rotation), Is.LessThan(.75f));
                    // Follow the same corrected pose into the release, including its
                    // return to ready; the support palm must remain on the shaft.
                    for (int frame = 0; frame < 120; frame++)
                    {
                        root.Tick(1f / 60f);
                        yield return null;
                        ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
                        AssertTwoHandWeaponContact(actor);
                        if (frame == 12 || frame == 30 || frame == 60)
                            CaptureTwoHandActorViews(root.CameraFollow.Camera, actor,
                                "charge-alignment-" + subject + "-release-" + frame, upperBody: true);
                    }
                }
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
            }
            finally { Time.captureDeltaTime = captureDelta; }
        }

        private static void AssertChargingArmAlignment(CombatActor actor)
        {
            var hands = actor.GetComponentInChildren<NpcHandPose>();
            foreach (NpcHandPose.HandBinding binding in hands.Hands)
            {
                string side = binding.IsLeft ? "L" : "R";
                Transform upper = CityPedestrianHandProps.FindSocket(actor.DamageRigRoot, "upper_arm." + side);
                Transform forearm = CityPedestrianHandProps.FindSocket(actor.DamageRigRoot, "forearm." + side);
                Vector3 upperDirection = forearm.position - upper.position;
                Vector3 forearmDirection = binding.Hand.position - forearm.position;
                // The cylinder centre sits along the fingers and off the palm;
                // removing the palm offset gives the actual hand direction without
                // assuming an imported bone axis or the FBX's unit scale.
                Vector3 fingers = Vector3.ProjectOnPlane(binding.CentreAnchor.position - binding.Hand.position,
                    hands.PalmNormal(binding.IsLeft));
                string sample = $"{actor.name}/{side}: charge={actor.State.Charge01:F3}";
                Assert.That(Vector3.Angle(forearmDirection, fingers), Is.LessThanOrEqualTo(55f),
                    sample + ": the wrist must continue the forearm while lifting the crowbar.");
                Assert.That(Vector3.Angle(upperDirection, forearmDirection), Is.LessThanOrEqualTo(150f),
                    sample + ": the elbow must not fold flat and reverse during charge.");
            }
        }

        [UnityTest]
        [Explicit("Both actual rigs: two-hand ready, raised lower-face guard, moving contact and lowered round-end rest.")]
        [PrebuildSetup(typeof(CombatTestAssetsSetup))]
        public IEnumerator CombatTwoHandPose()
        {
            float captureDelta = Time.captureDeltaTime;
            var input = new InputTestFixture();
            Keyboard keyboard = null;
            Time.captureDeltaTime = 1f / 60f;
            try
            {
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                yield return CaptureCombatTwoHandPose(input, keyboard);
            }
            finally
            {
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
                Time.captureDeltaTime = captureDelta;
            }
        }

        private IEnumerator CaptureCombatTwoHandPose(InputTestFixture input, Keyboard keyboard)
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest);
            var root = Object.FindAnyObjectByType<CombatTestRoot>();
            Assert.That(root, Is.Not.Null);
            root.AutomaticSimulation = false;
            PlaceTwoHandPosePair(root);
            Camera camera = root.CameraFollow.Camera;
            for (int frame = 0; frame < 24; frame++) { root.Tick(1f / 60f); yield return null; }

            foreach (bool blocking in new[] { false, true })
            {
                root.Hero.SetBlock(blocking);
                root.Opponent.SetBlock(blocking);
                // Record the actual intermediate per-hand regrip, then its settled endpoint.
                for (int frame = 0; frame < 24; frame++)
                {
                    root.Tick(1f / 60f);
                    yield return null;
                    if (blocking && frame == 5)
                        CaptureTwoHandPoseViews(root, camera, "regrip");
                }
                string phase = blocking ? "block" : "ready";
                CaptureTwoHandPoseViews(root, camera, phase);
                foreach (CombatActor actor in new[] { root.Hero, root.Opponent })
                {
                    AssertTwoHandWeaponContact(actor);
                    AssertCombatSolesGrounded(actor);
                    float span = Vector3.Distance(actor.SupportGripWorldPosition,
                        actor.GetComponentInChildren<NpcHandPose>().CylinderCentre(false));
                    Assert.That(span, blocking ? Is.InRange(.40f, .51f) : Is.InRange(.14f, .34f), actor.name);
                    if (blocking)
                    {
                        float gripHeight = actor.GetComponentInChildren<NpcHandPose>().CylinderCentre(false).y -
                            actor.transform.position.y;
                        Assert.That(gripHeight, Is.InRange(1.42f, 1.60f),
                            actor.name + ": the actual held guard must rise in front of the lower face.");
                        Assert.That(Mathf.Abs(Vector3.Dot(actor.Weapon.transform.up, Vector3.up)),
                            Is.LessThan(.35f), "Guard must present the crowbar nearly horizontally across the body.");
                    }
                }
                // Both planted feet stay fixed through the full authored breathing cycle.
                Transform[] feet = {
                    CityPedestrianHandProps.FindSocket(root.Hero.DamageRigRoot, "foot.L"),
                    CityPedestrianHandProps.FindSocket(root.Hero.DamageRigRoot, "foot.R"),
                    CityPedestrianHandProps.FindSocket(root.Opponent.DamageRigRoot, "foot.L"),
                    CityPedestrianHandProps.FindSocket(root.Opponent.DamageRigRoot, "foot.R") };
                var positions = new Vector3[feet.Length];
                for (int i = 0; i < feet.Length; i++) { Assert.That(feet[i], Is.Not.Null); positions[i] = feet[i].position; }
                for (int sample = 0; sample < 8; sample++)
                {
                    root.Tick(.5f);
                    yield return null;
                    ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
                    AssertTwoHandWeaponContact(root.Hero);
                    AssertTwoHandWeaponContact(root.Opponent);
                    for (int i = 0; i < feet.Length; i++)
                        Assert.That(Vector3.Distance(positions[i], feet[i].position), Is.LessThan(.02f),
                            phase + ": breathing may not slide or lift a planted foot: " + feet[i].name);
                }
            }

            PlaceTwoHandPosePair(root);
            for (int frame = 0; frame < 24; frame++) { root.Tick(1f / 60f); yield return null; }
            ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
            var heroLegs = new CombatTestPlayModeTests.WalkingLegProbe(root.Hero);
            var opponentLegs = new CombatTestPlayModeTests.WalkingLegProbe(root.Opponent);
            Vector3 start = root.Hero.transform.position;
            input.Press(keyboard.dKey, queueEventOnly: true);
            for (int frame = 0; frame < 24; frame++)
            {
                root.Hero.Step(1f / 60f);
                root.Opponent.SetLocomotion(.6f);
                root.Opponent.Body.Move(root.Opponent.transform.forward * (.6f / 60f));
                root.Opponent.Step(1f / 60f);
                yield return null;
                ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
                heroLegs.Sample(); opponentLegs.Sample();
                AssertTwoHandWeaponContact(root.Hero);
                AssertTwoHandWeaponContact(root.Opponent);
            }
            Assert.That(Vector3.Distance(start, root.Hero.transform.position), Is.GreaterThan(.1f),
                "The moving sample must exercise the actual player motor and gait.");
            heroLegs.AssertMoving(3f, "The owned two-hand torso must allow the hero's ordinary gait.");
            opponentLegs.AssertMoving(3f, "The two-hand overlay must allow the opponent's ordinary gait.");
            CaptureTwoHandPoseViews(root, camera, "moving");
            input.Release(keyboard.dKey, queueEventOnly: true);
            yield return null;

            foreach (bool hero in new[] { true, false })
            {
                CombatActor actor = hero ? root.Hero : root.Opponent;
                foreach (float power in new[] { .25f, .75f })
                {
                    PlaceTwoHandPosePair(root);
                    Assert.That(actor.RequestCharge(), Is.True);
                    root.Tick(actor.State.Settings.ChargeSeconds * power);
                    for (int frame = 0; frame < 6; frame++) yield return null;
                    ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
                    AssertTwoHandWeaponContact(actor);
                    string name = "charge-" + (hero ? "hero-" : "opponent-") + Mathf.RoundToInt(power * 100f);
                    CaptureTwoHandActorViews(camera, actor, name);
                    Assert.That(actor.ReleaseCharge(), Is.True);
                    for (int frame = 0; frame < 32; frame++)
                    {
                        root.Tick(1f / 60f);
                        yield return null;
                        ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
                        AssertTwoHandWeaponContact(actor);
                    }
                    CaptureTwoHandActorViews(camera, actor, name + "-release");
                }
            }

            foreach (bool heroWins in new[] { true, false })
            {
                CombatCapturePair(root);
                CombatActor winner = heroWins ? root.Hero : root.Opponent;
                CombatActor loser = heroWins ? root.Opponent : root.Hero;
                for (int contact = 0; contact < 4; contact++)
                {
                    Vector3 facing = heroWins ? Vector3.forward : Vector3.back;
                    winner.ResetActor(loser.transform.position - facing * 1.1f, facing);
                    // Holding guard while attacking reproduces the stale guard at round end.
                    winner.SetBlock(true);
                    Physics.SyncTransforms();
                    CombatStrikeUntilContact(winner, loser);
                    if (!loser.State.IsDefeated) root.Tick(.8f);
                    if (contact == 2)
                    {
                        yield return null;
                        ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
                        AssertTwoHandWeaponContact(loser);
                        CaptureTwoHandActorViews(camera, loser, "injured-" + (heroWins ? "opponent" : "hero"));
                    }
                }
                Assert.That(root.RoundFinished, Is.True);
                for (int frame = 0; frame < 120; frame++) { root.Tick(1f / 60f); yield return null; }
                ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
                Assert.That(winner.State.IsBlocking, Is.False, "A completed round must clear held guard.");
                Assert.That(winner.ActiveClipName, Is.EqualTo("CombatRest"));
                var hand = winner.GetComponentInChildren<NpcHandPose>();
                Assert.That(hand.RightGripWeight, Is.EqualTo(1f));
                Assert.That(hand.LeftGripWeight, Is.Zero);
                Assert.That(winner.SupportGripWeight, Is.Zero);
                Assert.That(hand.CylinderCentre(false).y - winner.transform.position.y, Is.LessThan(1.05f),
                    "The winner lowers the weapon instead of freezing in a guard.");
                AssertCombatSolesGrounded(winner);
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "twohand-rest-" + (heroWins ? "hero" : "opponent") + "-gameplay");
                CaptureTwoHandActorViews(camera, winner, "rest-" + (heroWins ? "hero" : "opponent"));
            }
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
        }

        private static void PlaceTwoHandPosePair(CombatTestRoot root)
        {
            root.SetSparring(false);
            root.Hero.ResetActor(Vector3.up * PlayerFactory.GroundedRootOffset, Vector3.forward);
            root.Opponent.ResetActor(new Vector3(0f, PlayerFactory.GroundedRootOffset, 3.5f), Vector3.back);
            Physics.SyncTransforms();
            root.CameraFollow.Snap();
        }

        private static void AssertTwoHandWeaponContact(CombatActor actor)
        {
            var hand = actor.GetComponentInChildren<NpcHandPose>();
            Vector3 measured = hand.CylinderCentre(true), target = actor.SupportGripWorldPosition;
            Vector3 shaft = actor.Weapon.transform.up;
            Vector3 rightPalm = Vector3.ProjectOnPlane(hand.PalmNormal(false), shaft).normalized;
            Vector3 leftPalm = Vector3.ProjectOnPlane(hand.PalmNormal(true), shaft).normalized;
            NpcHandPose.HandBinding left = null;
            foreach (NpcHandPose.HandBinding binding in hand.Hands)
                if (binding.IsLeft) left = binding;
            Assert.That(left, Is.Not.Null, actor.name + ": the original left palm binding is required.");
            Transform upper = CityPedestrianHandProps.FindSocket(actor.DamageRigRoot, "upper_arm.L");
            Transform forearm = CityPedestrianHandProps.FindSocket(actor.DamageRigRoot, "forearm.L");
            Assert.That(upper, Is.Not.Null); Assert.That(forearm, Is.Not.Null);
            Pose wantedSocket = hand.GetSocketPose(true, target, shaft, -rightPalm);
            Vector3 socketOffset = Quaternion.Inverse(left.Hand.rotation) * (left.GripSocket.position - left.Hand.position);
            Vector3 wantedWrist = wantedSocket.position - wantedSocket.rotation * socketOffset;
            float reach = Vector3.Distance(upper.position, wantedWrist);
            float chain = Vector3.Distance(upper.position, forearm.position) + Vector3.Distance(forearm.position, left.Hand.position);
            string sample = $"{actor.name}: phase={actor.State.Phase}, progress={actor.State.AttackProgress:F3}, " +
                $"power={actor.State.AttackPower:F2}, support={actor.SupportGripWeight:F3}, " +
                $"left={measured:F4}, target={target:F4}, wantedWrist={wantedWrist:F4}, " +
                $"wristReach={reach:F4}, chainLength={chain:F4}, reachMargin={chain - reach:F4}";
            Assert.That(hand.RightGripWeight, Is.EqualTo(1f), sample);
            Assert.That(hand.LeftGripWeight, Is.GreaterThan(.99f), sample + ": support fingers must close on the bar.");
            Assert.That(actor.SupportGripWeight, Is.GreaterThan(.99f), sample);
            Vector3 grip = CombatAssetProvider.FindAnchor(actor.Weapon, "Grip").position;
            Assert.That(Vector3.Distance(hand.CylinderCentre(false), grip), Is.LessThan(.002f), sample);
            Assert.That(Vector3.Distance(measured, target), Is.LessThan(.012f),
                sample + ": the actual left finger cylinder must meet the moving shaft target after all pose layers.");
            Assert.That(Vector3.ProjectOnPlane(measured - grip, shaft).magnitude,
                Is.LessThan(.012f), sample + ": the support hand may not float beside the shaft.");
            Assert.That(Mathf.Abs(Vector3.Dot(hand.CylinderAxis(true), shaft)), Is.GreaterThan(.98f),
                sample + ": the left grip cylinder must follow the shaft direction.");
            Assert.That(Vector3.Dot(leftPalm, rightPalm), Is.LessThanOrEqualTo(-.98f),
                sample + ": the upper hand must grip the opposite side of the shaft from the lower hand.");
        }

        private static void AssertCombatSolesGrounded(CombatActor actor)
        {
            var mesh = new Mesh();
            float left = float.PositiveInfinity, right = float.PositiveInfinity;
            try
            {
                foreach (SkinnedMeshRenderer renderer in actor.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                        !(renderer.name.Contains("Boot") || renderer.name.Contains("Shoe"))) continue;
                    bool isLeft = renderer.name.EndsWith(".L", StringComparison.Ordinal);
                    bool isRight = renderer.name.EndsWith(".R", StringComparison.Ordinal);
                    if (!isLeft && !isRight) continue;
                    renderer.BakeMesh(mesh, true);
                    foreach (Vector3 vertex in mesh.vertices)
                    {
                        float y = renderer.transform.TransformPoint(vertex).y;
                        if (isLeft) left = Mathf.Min(left, y); else right = Mathf.Min(right, y);
                    }
                }
                Assert.That(left, Is.InRange(-.025f, .08f), actor.name + ": actual left sole must meet the arena floor.");
                Assert.That(right, Is.InRange(-.025f, .08f), actor.name + ": actual right sole must meet the arena floor.");
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        private static void CaptureTwoHandPoseViews(CombatTestRoot root, Camera camera, string phase)
        {
            // Batch-mode coroutines resume before LateUpdate. Capture the same
            // final grip/ground/recovery composition that the game will render.
            ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
            Assert.That(root.CameraFollow.enabled && root.CameraFollow.TargetLockActive, Is.True);
            CaptureCurrentCamera(camera, SceneIds.CombatTest, "twohand-" + phase + "-gameplay");
            CaptureTwoHandActorViews(camera, root.Hero, phase + "-hero");
            CaptureTwoHandActorViews(camera, root.Opponent, phase + "-opponent");
        }

        private static void CaptureTwoHandActorViews(Camera camera, CombatActor actor, string name, bool upperBody = false)
        {
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            try
            {
                Vector3 target = actor.transform.position + Vector3.up * (upperBody ? 1.4f : .95f);
                foreach (bool side in new[] { false, true })
                {
                    // The authored arena obstacle occupies x=3: keep both rigs'
                    // side views on the open western half of the floor.
                    Vector3 eye = side
                        ? actor.transform.position + Vector3.left * 3f + Vector3.up * 1.3f + actor.transform.forward * .2f
                        : actor.transform.TransformPoint(new Vector3(1.4f, 1.3f, 3f));
                    if (upperBody) eye = target + (eye - target) * .65f;
                    camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                    camera.fieldOfView = 42f;
                    CaptureCurrentCamera(camera, SceneIds.CombatTest, "twohand-" + name + (side ? "-side" : "-front"));
                }
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fov;
            }
        }
    }
}
