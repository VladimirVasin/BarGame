using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        [UnityTest]
        public IEnumerator Range_MouseLookKeepsAimAndCursorHandoffsIndependent()
        {
            var input = new InputTestFixture();
            Keyboard keyboard = null;
            Mouse mouse = null;
            Gamepad gamepad = null;
            try
            {
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                mouse = InputSystem.AddDevice<Mouse>();
                gamepad = InputSystem.AddDevice<Gamepad>();
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                yield return EnterRange(false, CombatWeaponId.Pistol);
                PlacePair(8f);
                Assert.That(root.SetOpponentFocus(false), Is.True);
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                GameCursor.SetApplicationFocused(true);
                root.AutomaticSimulation = true;
                yield return ConsumeMouseLookInput();

                Camera camera = root.CameraFollow.Camera;
                root.CameraFollow.SetCinematicMotionEnabled(false);
                Quaternion view = camera.transform.rotation;
                Quaternion heading = root.Hero.transform.rotation;
                float pitch = root.CameraFollow.TargetOrbitPitch;
                input.Set(mouse.delta, new Vector2(80f, 30f), queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                root.CameraFollow.Snap();
                Assert.That(Quaternion.Angle(view, camera.transform.rotation), Is.GreaterThan(5f));
                Assert.That(root.CameraFollow.TargetOrbitPitch, Is.LessThan(pitch - 1f));
                Assert.That(Quaternion.Angle(heading, root.Hero.transform.rotation), Is.LessThan(.001f));
                Assert.That(root.Hero.Firearm.AimRequested || root.CameraFollow.FreeAimActive, Is.False,
                    "Ordinary mouse look must leave the pistol lowered.");

                input.Press(mouse.rightButton, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                Assert.That(root.Hero.Firearm.AimRequested && root.CameraFollow.FreeAimActive, Is.True,
                    "RMB must request firearm aim without owning ordinary look.");
                pitch = root.CameraFollow.TargetOrbitPitch;
                input.Set(mouse.delta, new Vector2(0f, 20f), queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                Assert.That(root.CameraFollow.TargetOrbitPitch, Is.LessThan(pitch - 1f),
                    "The mouse must continue looking while RMB is held.");
                input.Release(mouse.rightButton, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                Assert.That(root.Hero.Firearm.AimRequested || root.CameraFollow.FreeAimActive, Is.False);
                Assert.That(GameCursor.GameplayMouseInputAllowed, Is.True,
                    "Lowering a firearm must retain ordinary gameplay cursor capture.");

                input.Press(mouse.rightButton, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                Assert.That(root.Hero.Firearm.AimRequested && root.CameraFollow.FreeAimActive, Is.True);
                int shots = root.Hero.Firearm.ShotSequence;
                pitch = root.CameraFollow.TargetOrbitPitch;
                input.Press(keyboard.leftAltKey, queueEventOnly: true);
                input.Press(mouse.leftButton, queueEventOnly: true);
                input.Set(mouse.delta, new Vector2(0f, 60f), queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                Assert.That(GameCursor.GameplayMouseInputAllowed, Is.False);
                Assert.That(root.Hero.Firearm.AimRequested || root.CameraFollow.FreeAimActive, Is.False);
                Assert.That(root.CameraFollow.TargetOrbitPitch, Is.EqualTo(pitch).Within(.001f));
                Assert.That(root.Hero.Firearm.ShotSequence, Is.EqualTo(shots),
                    "A pointer click made with Alt held cannot fire.");
                RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height);
                Vector2 toolbarPoint = canvas.LogicalToScreen(new Vector2(160f, 20f));
                input.Set(mouse.position, new Vector2(toolbarPoint.x, Screen.height - toolbarPoint.y),
                    queueEventOnly: true);
                InputSystem.Update();
                MethodInfo pointerOverToolbar = typeof(CombatTestRoot).GetMethod(
                    "PointerOverToolbar", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(pointerOverToolbar, Is.Not.Null);
                Assert.That((bool)pointerOverToolbar.Invoke(root, null), Is.False,
                    "The default gameplay HUD has no invisible toolbar hitbox.");
                input.Press(keyboard.f8Key, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                input.Release(keyboard.f8Key, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                Assert.That((bool)pointerOverToolbar.Invoke(root, null), Is.True);
                input.Press(gamepad.rightTrigger, queueEventOnly: true);
                InputSystem.Update();
                Assert.That((bool)pointerOverToolbar.Invoke(root, null), Is.False,
                    "A mouse hovering the toolbar cannot block an independent gamepad trigger.");
                input.Release(gamepad.rightTrigger, queueEventOnly: true);
                InputSystem.Update();
                input.Release(keyboard.leftAltKey, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                Assert.That(GameCursor.GameplayMouseInputAllowed, Is.True);
                Assert.That(root.Hero.Firearm.AimRequested || root.CameraFollow.FreeAimActive, Is.False,
                    "Buttons held through a pointer handoff need a fresh release and press.");
                Assert.That(root.Hero.Firearm.ShotSequence, Is.EqualTo(shots));

                // Button delta events include their shared byte. Process the
                // first release before queuing the second, or it restores RMB.
                input.Release(mouse.rightButton, queueEventOnly: true);
                InputSystem.Update();
                input.Release(mouse.leftButton, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                Assert.That(mouse.rightButton.isPressed || mouse.leftButton.isPressed, Is.False,
                    "Both mouse buttons must reach neutral before requesting fresh aim.");
                input.Press(mouse.rightButton, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                Assert.That(root.Hero.Firearm.AimRequested && root.CameraFollow.FreeAimActive, Is.True,
                    "A fresh RMB press must restore aim intent before its physical raise completes.");
                yield return WaitFor(() => root.Hero.Firearm.CanFire && root.Hero.PistolAimAligned &&
                    root.Hero.PistolVisualAimProgress >= .999f, "A fresh RMB press did not restore aim.");
                input.Press(mouse.leftButton, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                Assert.That(root.Hero.Firearm.ShotSequence, Is.EqualTo(shots + 1),
                    "Fresh input after Alt must still permit an ordinary shot.");
                input.Release(mouse.leftButton, queueEventOnly: true);
                yield return ConsumeMouseLookInput();

                Assert.That(root.PauseMenu.Open(), Is.True);
                yield return ConsumeMouseLookInput();
                Assert.That(GameCursor.GameplayMouseInputAllowed, Is.False);
                Assert.That(root.Hero.Firearm.AimRequested || root.CameraFollow.FreeAimActive, Is.False);
                input.Press(mouse.leftButton, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                Assert.That(root.Hero.Firearm.ShotSequence, Is.EqualTo(shots + 1));
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay),
                    "Pause did not return gameplay input.");
                yield return ConsumeMouseLookInput();
                Assert.That(GameCursor.GameplayMouseInputAllowed, Is.True);
                Assert.That(root.Hero.Firearm.AimRequested && root.CameraFollow.FreeAimActive, Is.True,
                    "The existing held-RMB aim behavior must resume after pause.");
                Assert.That(root.Hero.Firearm.ShotSequence, Is.EqualTo(shots + 1),
                    "Closing pause cannot replay its pointer click as a shot.");

                root.SendMessage("OnApplicationFocus", false);
                GameInput.HandleApplicationFocus(false);
                GameCursor.SetApplicationFocused(false);
                yield return ConsumeMouseLookInput();
                Assert.That(GameCursor.GameplayMouseInputAllowed, Is.False);
                Assert.That(root.Hero.Firearm.AimRequested || root.CameraFollow.FreeAimActive, Is.False);
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                GameCursor.SetApplicationFocused(true);
                yield return ConsumeMouseLookInput();
                Assert.That(GameCursor.GameplayMouseInputAllowed, Is.True);
                Assert.That(root.Hero.Firearm.AimRequested || root.CameraFollow.FreeAimActive, Is.False,
                    "Returning focus must not restore buttons held before Alt+Tab.");
                Assert.That(root.Hero.Firearm.ShotSequence, Is.EqualTo(shots + 1));
                input.Release(mouse.rightButton, queueEventOnly: true);
                InputSystem.Update();
                input.Release(mouse.leftButton, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                input.Press(mouse.rightButton, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                Assert.That(root.Hero.Firearm.AimRequested && root.CameraFollow.FreeAimActive, Is.True);
            }
            finally
            {
                if (root != null)
                {
                    root.AutomaticSimulation = false;
                    if (root.PauseMenu.IsOpen) root.PauseMenu.Cancel();
                    root.SendMessage("OnApplicationFocus", true);
                }
                GameInput.HandleApplicationFocus(true);
                GameCursor.SetApplicationFocused(true);
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (gamepad != null && gamepad.added) InputSystem.RemoveDevice(gamepad);
                input.TearDown();
                GameCursor.Refresh();
            }
        }

        private static IEnumerator ConsumeMouseLookInput()
        {
            // Let the queued Input System event pass Update and LateUpdate.
            yield return null;
            yield return null;
        }
    }
}
