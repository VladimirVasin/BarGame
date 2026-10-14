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
        [Explicit("Warehouse stair look, the real chest lamp, firearm contacts and bounded torso ownership.")]
        [PrebuildSetup(typeof(CombatFlashlightCaptureSetup))]
        public IEnumerator CombatWarehouseTorsoLook()
        {
            float previousCaptureDelta = Time.captureDeltaTime;
            var input = new InputTestFixture();
            bool inputStarted = false;
            CombatTestRoot root = null;
            Time.captureDeltaTime = 1f / 60f;
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            try
            {
                input.Setup();
                inputStarted = true;
                Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
                Mouse mouse = InputSystem.AddDevice<Mouse>();
                GameSessionState.BeginNewGame();
                yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest, LoadSceneMode.Single);
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                Assert.That(root != null && root.IsInitialized && root.WarehousePlan != null, Is.True);
                root.AutomaticSimulation = false;
                root.SetDuelLogging(false);
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                root.SetSparring(false);
                Assert.That(root.SetOpponentFocus(false), Is.True);
                var visual = (Player3DCharacterPresentation)root.Player.Visual;
                PlayerCameraFollow follow = root.CameraFollow;
                WarehouseArenaStair stair = root.WarehousePlan.Stairs[0];
                PlaceTorsoLookHero(root, stair.Foot + Vector3.back * .65f, Vector3.forward);
                SetOrdinaryTorsoOrbit(root, 0f, 0f);
                yield return AdvanceTorsoCapture(root, 36);
                Assert.That(follow.TryGetOrdinaryLookDirection(out _, out float neutral), Is.True);
                Assert.That(neutral, Is.EqualTo(14f).Within(.001f));
                Assert.That(visual.TorsoLookAnglesDegrees.magnitude, Is.LessThan(.5f),
                    "The elevated chase framing must not permanently bend a neutral hero.");
                Quaternion heading = root.Hero.transform.rotation;
                Vector3 neutralBeam = root.Flashlight.Lens.forward;
                CaptureCurrentCamera(follow.Camera, SceneIds.CombatTest, "Test torso neutral at warehouse stairs");

                // Arrow look is available without RMB; it must not engage block or weapon aim.
                input.Press(keyboard.upArrowKey, queueEventOnly: true);
                yield return AdvanceTorsoCapture(root, 12);
                input.Release(keyboard.upArrowKey, queueEventOnly: true);
                yield return AdvanceTorsoCapture(root, 24);
                Assert.That(follow.CurrentOrbitPitch, Is.LessThan(neutral - 2f));
                Assert.That(root.Hero.State.IsBlocking, Is.False);
                Assert.That(Quaternion.Angle(heading, root.Hero.transform.rotation), Is.LessThan(.05f));

                SetOrdinaryTorsoOrbit(root, -34f, 0f);
                yield return AdvanceTorsoCapture(root, 42);
                Assert.That(visual.TorsoLookAnglesDegrees.x, Is.InRange(-20.01f, -17f));
                Assert.That(Vector3.Dot(root.Flashlight.Lens.forward, Vector3.up),
                    Is.GreaterThan(Vector3.Dot(neutralBeam, Vector3.up) + .15f));
                AssertTorsoEnvelope(visual);
                AssertTorsoReplayStable(visual);
                CaptureCurrentCamera(follow.Camera, SceneIds.CombatTest, "Test torso looking up warehouse stairs");
                yield return WalkTorsoLookStairs(root, input, keyboard, ascending: true);

                SetOrdinaryTorsoOrbit(root, 35f, 15f);
                yield return AdvanceTorsoCapture(root, 42);
                Assert.That(visual.TorsoLookAnglesDegrees.x, Is.GreaterThan(18f));
                Assert.That(visual.TorsoLookAnglesDegrees.y, Is.GreaterThan(5f));
                Assert.That(Quaternion.Angle(heading, root.Hero.transform.rotation), Is.LessThan(.05f),
                    "A diagonal orbit bends and twists the torso without steering the tank-control root.");
                AssertTorsoEnvelope(visual);
                AssertTorsoReplayStable(visual);
                CaptureCurrentCamera(follow.Camera, SceneIds.CombatTest, "Test torso diagonal warehouse look");
                object otherOwner = new object();
                Vector3 acceptedAngles = visual.TorsoLookAnglesDegrees;
                visual.ClearTorsoLook(otherOwner);
                visual.SetTorsoLookIntent(otherOwner, Vector3.down, 0f, false);
                visual.AdvanceTorsoLook(otherOwner, 1f);
                Assert.That(Vector3.Distance(acceptedAngles, visual.TorsoLookAnglesDegrees), Is.LessThan(.0001f),
                    "Another owner cannot replace, advance or clear the warehouse torso pose.");

                SetOrdinaryTorsoOrbit(root, 35f, 165f);
                yield return AdvanceTorsoCapture(root, 48);
                Assert.That(visual.TorsoLookAnglesDegrees.magnitude, Is.LessThan(.5f),
                    "Looking back at the hero releases the torso overlay rather than twisting him around.");
                Assert.That(Quaternion.Angle(heading, root.Hero.transform.rotation), Is.LessThan(.05f));

                PlaceTorsoLookHero(root, stair.Top + Vector3.forward * .7f, Vector3.back);
                SetOrdinaryTorsoOrbit(root, 35f, -15f);
                yield return AdvanceTorsoCapture(root, 42);
                AssertTorsoEnvelope(visual);
                CaptureCurrentCamera(follow.Camera, SceneIds.CombatTest, "Test torso looking down warehouse stairs");
                yield return WalkTorsoLookStairs(root, input, keyboard, ascending: false);
                Transform chest = visual.Registry.Anchors.Chest;
                Quaternion pausedChest = chest.rotation;
                Vector3 pausedAngles = visual.TorsoLookAnglesDegrees;
                Assert.That(root.PauseMenu.Open(), Is.True);
                yield return AdvanceTorsoCapture(root, 8);
                Assert.That(Quaternion.Angle(pausedChest, chest.rotation), Is.LessThan(.1f));
                Assert.That(Vector3.Distance(pausedAngles, visual.TorsoLookAnglesDegrees), Is.LessThan(.0001f));
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                yield return AdvanceTorsoCapture(root, 2);
                visual.SetInteractionHandoffLocked(true);
                visual.ReapplyLatePresentationPose();
                Assert.That(visual.TorsoLookAnglesDegrees, Is.EqualTo(Vector3.zero),
                    "An authored interaction handoff owns the torso without the ordinary look overlay.");
                visual.SetInteractionHandoffLocked(false);
                yield return AdvanceTorsoCapture(root, 4);

                foreach (CombatWeaponId weapon in new[] { CombatWeaponId.Pistol, CombatWeaponId.Shotgun })
                {
                    PlaceTorsoLookHero(root, stair.Foot + Vector3.back * 1.5f, Vector3.forward);
                    SetOrdinaryTorsoOrbit(root, 0f, 0f);
                    Assert.That(root.Hero.TrySelectWeapon(weapon), Is.True);
                    for (int frame = 0; frame < 180 && (root.Hero.IsSwitchingWeapon ||
                        !root.Hero.HasWeaponInHand || root.Hero.WeaponId != weapon); frame++)
                    {
                        root.Tick(1f / 30f);
                        yield return null;
                    }
                    Assert.That(root.Hero.WeaponId, Is.EqualTo(weapon));
                    Assert.That(root.Hero.IsSwitchingWeapon, Is.False);
                    Assert.That(root.SetOpponentFocus(false), Is.True);
                    root.Hero.SetCombatFocused(false);
                    // Enter the production RMB path once; all subsequent rig clocks
                    // are explicit so they are not accidentally advanced twice.
                    root.AutomaticSimulation = true;
                    input.Press(mouse.rightButton, queueEventOnly: true);
                    for (int frame = 0; frame < 3; frame++) yield return null;
                    root.AutomaticSimulation = false;
                    Assert.That(follow.FreeAimActive && root.Hero.Firearm.AimRequested, Is.True);
                    foreach (Vector2 aim in new[] { new Vector2(-15f, 0f), new Vector2(25f, 0f), new Vector2(-12f, 12f) })
                    {
                        SetFirearmTorsoOrbit(root, aim.x, aim.y);
                        yield return AdvanceTorsoCapture(root, 42);
                        visual.ReapplyLatePresentationPose();
                        Assert.That(root.Hero.Firearm.IsAiming, Is.True);
                        Assert.That(root.Hero.PistolAimErrorDegrees, Is.LessThanOrEqualTo(5f),
                            weapon + ": torso inclination must preserve the actual muzzle target.");
                        Assert.That(root.Hero.PistolSupportError, Is.LessThan(weapon == CombatWeaponId.Shotgun ? .025f : .02f),
                            weapon + ": the original left palm must stay on its authored support grip.");
                        AssertTorsoEnvelope(visual);
                        AssertTorsoReplayStable(visual);
                        string view = aim.y != 0f ? "diagonal" : aim.x < 0f ? "upstairs" : "downstairs";
                        CaptureCurrentCamera(follow.Camera, SceneIds.CombatTest,
                            "Test torso " + (weapon == CombatWeaponId.Pistol ? "pistol " : "shotgun ") + view);
                    }
                    root.AutomaticSimulation = true;
                    input.Release(mouse.rightButton, queueEventOnly: true);
                    for (int frame = 0; frame < 3; frame++) yield return null;
                    root.AutomaticSimulation = false;
                    yield return AdvanceTorsoCapture(root, 42);
                    Assert.That(follow.FreeAimActive || root.Hero.Firearm.AimRequested, Is.False);
                }

                SetOrdinaryTorsoOrbit(root, 25f, 0f);
                yield return AdvanceTorsoCapture(root, 36);
                root.enabled = false;
                visual.ReapplyLatePresentationPose();
                Assert.That(visual.TorsoLookAnglesDegrees, Is.EqualTo(Vector3.zero));
                root.enabled = true;
                yield return AdvanceTorsoCapture(root, 24);
                Assert.That(visual.TorsoLookAnglesDegrees.x, Is.GreaterThan(15f));
                root.ResetRound();
                Assert.That(visual.TorsoLookAnglesDegrees, Is.EqualTo(Vector3.zero));
                Assert.That(root.SetOpponentFocus(false), Is.True);
                SetOrdinaryTorsoOrbit(root, 0f, 0f);
                yield return AdvanceTorsoCapture(root, 24);
                Assert.That(visual.TorsoLookAnglesDegrees.magnitude, Is.LessThan(.5f));
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                Assert.That(visual == null, Is.True);
            }
            finally
            {
                if (root != null) root.AutomaticSimulation = false;
                if (inputStarted) input.TearDown();
                Time.captureDeltaTime = previousCaptureDelta;
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = null;
            }
        }

        private static void PlaceTorsoLookHero(CombatTestRoot root, Vector3 position, Vector3 facing)
        {
            root.Hero.ResetActor(position + Vector3.up * PlayerFactory.GroundedRootOffset, facing);
            root.Hero.SetCombatFocused(false);
            Physics.SyncTransforms();
        }

        private static void SetOrdinaryTorsoOrbit(CombatTestRoot root, float pitchFromNeutral, float yawFromHero)
        {
            Assert.That(root.CameraFollow.TryGetOrdinaryLookDirection(out Vector3 look, out float neutral), Is.True);
            float yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
            root.CameraFollow.RotateYaw(Mathf.DeltaAngle(yaw, root.Hero.transform.eulerAngles.y + yawFromHero));
            root.CameraFollow.RotatePitch(neutral + pitchFromNeutral - root.CameraFollow.TargetOrbitPitch);
            root.CameraFollow.Snap();
        }

        private static void SetFirearmTorsoOrbit(CombatTestRoot root, float pitch, float yawFromHero)
        {
            float currentYaw = root.CameraFollow.Camera.transform.eulerAngles.y;
            root.CameraFollow.RotateYaw(Mathf.DeltaAngle(currentYaw, root.Hero.transform.eulerAngles.y + yawFromHero));
            root.CameraFollow.RotatePitch(pitch - root.CameraFollow.TargetOrbitPitch);
            root.CameraFollow.Snap();
        }

        private static IEnumerator AdvanceTorsoCapture(CombatTestRoot root, int frames)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                root.Tick(1f / 60f);
                yield return null;
            }
            // UnityTest resumes after Update and before LateUpdate. Read/render
            // the completed rig pose without advancing any presentation clock.
            ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
        }

        private static IEnumerator WalkTorsoLookStairs(CombatTestRoot root, InputTestFixture input,
            Keyboard keyboard, bool ascending)
        {
            float startHeight = root.Hero.transform.position.y;
            input.Press(keyboard.wKey, queueEventOnly: true);
            try
            {
                yield return AdvanceTorsoCapture(root, 40);
                float travelledHeight = root.Hero.transform.position.y - startHeight;
                Assert.That(ascending ? travelledHeight : -travelledHeight, Is.GreaterThan(.08f),
                    "Live W input must move the real hero " + (ascending ? "up" : "down") + " the imported stair.");
                AssertTorsoEnvelope((Player3DCharacterPresentation)root.Player.Visual);
                CaptureCurrentCamera(root.CameraFollow.Camera, SceneIds.CombatTest,
                    ascending ? "Test torso walking up warehouse stairs" : "Test torso walking down warehouse stairs");
            }
            finally { input.Release(keyboard.wKey, queueEventOnly: true); }
            yield return AdvanceTorsoCapture(root, 12);
        }

        private static void AssertTorsoEnvelope(Player3DCharacterPresentation visual)
        {
            Vector3 angles = visual.TorsoLookAnglesDegrees;
            Assert.That(angles.x, Is.InRange(-20.01f, 35.01f));
            Assert.That(Mathf.Abs(angles.y), Is.LessThanOrEqualTo(20.01f));
            Assert.That(Mathf.Abs(angles.z), Is.LessThanOrEqualTo(6.01f));
            float pitchLimit = angles.x >= 0f ? 35f : 20f;
            float p = angles.x / pitchLimit, y = angles.y / 20f, r = angles.z / 6f;
            Assert.That(p * p + y * y + r * r, Is.LessThanOrEqualTo(1.0001f),
                "Pitch, twist and lateral lean must stay inside the shared elliptical envelope.");
        }

        private static void AssertTorsoReplayStable(Player3DCharacterPresentation visual)
        {
            visual.ReapplyLatePresentationPose();
            Transform[] bones = {
                CityPedestrianHandProps.FindSocket(visual.Registry.transform, "spine"),
                visual.Registry.Anchors.Chest, visual.Registry.Anchors.Head,
                CityPedestrianHandProps.FindSocket(visual.Registry.transform, "hand.R"),
                CityPedestrianHandProps.FindSocket(visual.Registry.transform, "hand.L") };
            var accepted = new Pose[bones.Length];
            for (int bone = 0; bone < bones.Length; bone++)
            {
                Assert.That(bones[bone], Is.Not.Null);
                accepted[bone] = new Pose(bones[bone].position, bones[bone].rotation);
            }
            for (int replay = 0; replay < 6; replay++)
            {
                visual.ReapplyLatePresentationPose();
                for (int bone = 0; bone < bones.Length; bone++)
                {
                    Assert.That(Vector3.Distance(accepted[bone].position, bones[bone].position), Is.LessThan(.002f),
                        "Repeated late presentation cannot accumulate torso translation: " + bones[bone].name);
                    Assert.That(Quaternion.Angle(accepted[bone].rotation, bones[bone].rotation), Is.LessThan(.2f),
                        "Repeated late presentation cannot accumulate torso rotation: " + bones[bone].name);
                }
            }
        }
    }
}
