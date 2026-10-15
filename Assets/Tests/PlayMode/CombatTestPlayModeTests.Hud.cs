using System;
using System.Collections;
using System.IO;
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
        public IEnumerator Range_GameplayHudRespondsToAimDamageAndDebugToggle()
        {
            int? previousSeed = CombatTestRoot.WarehouseSeedForTests;
            bool previousBackground = Application.runInBackground;
            var input = new InputTestFixture();
            Keyboard keyboard = null;
            Mouse mouse = null;
            Gamepad gamepad = null;
            var modal = new BarMinigameModalLock();
            try
            {
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                mouse = InputSystem.AddDevice<Mouse>();
                gamepad = InputSystem.AddDevice<Gamepad>();
                Application.runInBackground = true;
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = 0;
                var pacing = new CombatEncounterSettings(1, 60f, 60f, 60f, 60f);
                Assert.That(CombatTestStartService.TryStart(new CombatTestSelection(
                    new CombatLoadoutSelection(CombatWeaponId.Pistol), CombatTestMode.Endless, pacing)), Is.True);
                yield return AwaitSelectedCombatRange();
                root.AutomaticHudPresentation = false;
#if UNITY_EDITOR
                if (!Application.isBatchMode)
                {
                    Type viewType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
                    UnityEditor.EditorWindow view = UnityEditor.EditorWindow.GetWindow(viewType);
                    view.Show();
                    view.Focus();
                    yield return null;
                }
#endif
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                GameCursor.SetApplicationFocused(true);
                root.CameraFollow.SetCinematicMotionEnabled(false);
                root.AdvanceHudPresentation(.4f);
                Assert.That(root.WarehousePlan, Is.Not.Null);
                Assert.That(root.LiveOpponentCount, Is.Zero);
                Assert.That(root.GameplayHudVisible, Is.True);
                Assert.That(root.DebugHudVisible, Is.False, "The player starts with the ordinary HUD.");
                AssertCombatHudHidden();
                yield return CaptureGameplayHud("Test gameplay HUD at rest");

                input.Press(keyboard.leftAltKey, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height);
                Vector2 toolbar = canvas.LogicalToScreen(new Vector2(160f, 20f));
                input.Set(mouse.position, new Vector2(toolbar.x, Screen.height - toolbar.y), queueEventOnly: true);
                InputSystem.Update();
                Assert.That(root.PointerOverToolbar(), Is.False, "A hidden toolbar cannot intercept combat input.");
                input.Release(keyboard.leftAltKey, queueEventOnly: true);
                yield return ConsumeMouseLookInput();

                input.Press(keyboard.f8Key, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                Assert.That(root.DebugHudVisible, Is.True);
                Assert.That(root.GameplayHudVisible, Is.False, "The legacy HUD replaces the ordinary view.");
                Assert.That(GameCursor.GameplayMouseInputAllowed, Is.True, "F8 only changes presentation.");
                Assert.That(GameInput.CanRead(GameInputContext.Gameplay), Is.True);
                yield return CaptureGameplayHud("Test legacy HUD on F8");
                Assert.That(root.PauseMenu.Open(), Is.True);
                Assert.That(root.HudCanRender, Is.False, "Pause hides the retained legacy HUD too.");
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause did not release the legacy HUD.");
                Assert.That(root.DebugHudVisible && root.HudCanRender, Is.True);
                input.Release(keyboard.f8Key, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                input.Press(keyboard.f8Key, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                input.Release(keyboard.f8Key, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                Assert.That(root.DebugHudVisible, Is.False);

                root.AutomaticSimulation = true;
                input.Press(mouse.rightButton, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                root.AutomaticSimulation = false;
                Assert.That(root.Hero.Firearm.AimRequested, Is.True);
                // The actual device establishes aim ownership; presentation uses
                // explicit steps so slow editor frames cannot skip its midpoint.
                root.Hero.SetPistolAim(false);
                root.AdvanceHudPresentation(.4f);
                root.Hero.SetPistolAim(true);
                root.AdvanceHudPresentation(.075f);
                Assert.That(root.AmmunitionHudOpacity, Is.InRange(.001f, .999f), "Aim begins a gradual reveal.");
                yield return CaptureGameplayHud("Test ammunition HUD appearing");
                root.AdvanceHudPresentation(.2f);
                Assert.That(root.AmmunitionHudOpacity, Is.EqualTo(1f).Within(.001f));
                root.Hero.Step(1f);
                yield return CaptureGameplayHud("Test ammunition HUD while aiming");

                root.Hero.SetPistolAim(false);
                root.AdvanceHudPresentation(.075f);
                Assert.That(root.Hero.Firearm.AimRequested, Is.False);
                float loweringOpacity = root.AmmunitionHudOpacity;
                Assert.That(loweringOpacity, Is.InRange(.001f, .999f), "Lowering the weapon fades the ammunition away.");
                root.Hero.SetPistolAim(true);
                root.AdvanceHudPresentation(.025f);
                Assert.That(root.AmmunitionHudOpacity, Is.GreaterThan(loweringOpacity), "Reaim reverses the existing fade.");
                root.Hero.SetPistolAim(false);
                root.AdvanceHudPresentation(.4f);
                Assert.That(root.AmmunitionHudOpacity, Is.Zero);
                root.AutomaticSimulation = true;
                input.Release(mouse.rightButton, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                root.AutomaticSimulation = false;
                Assert.That(root.Hero.Firearm.AimRequested, Is.False);

                root.AutomaticSimulation = true;
                input.Press(gamepad.leftTrigger, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                root.AutomaticSimulation = false;
                root.AdvanceHudPresentation(.2f);
                Assert.That(root.Hero.Firearm.AimRequested, Is.True);
                Assert.That(root.AmmunitionHudOpacity, Is.EqualTo(1f).Within(.001f), "Gamepad aim uses the same HUD policy.");
                Assert.That(root.PauseMenu.Open(), Is.True);
                Assert.That(root.GameplayHudVisible || root.DebugHudVisible, Is.False);
                root.AdvanceHudPresentation(10f);
                AssertCombatHudHidden();
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause did not release the HUD.");
                Assert.That(root.GameplayHudVisible, Is.True);
                Assert.That(root.AmmunitionHudOpacity, Is.EqualTo(1f).Within(.001f), "Pause retains the presentation clock.");
                Assert.That(modal.TryCaptureAndDisable(root.Player.Interactor, root.CameraFollow, null), Is.True);
                Assert.That(root.GameplayHudVisible || root.DebugHudVisible, Is.False);
                root.AdvanceHudPresentation(10f);
                AssertCombatHudHidden();
                Assert.That(modal.Restore(), Is.True);
                Assert.That(root.GameplayHudVisible, Is.True);

                root.SendMessage("OnApplicationFocus", false);
                GameInput.HandleApplicationFocus(false);
                Assert.That(root.GameplayHudVisible || root.DebugHudVisible, Is.False);
                AssertCombatHudHidden();
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                GameCursor.SetApplicationFocused(true);
                root.AdvanceHudPresentation(.2f);
                Assert.That(root.AmmunitionHudOpacity, Is.Zero, "Focus return cannot restore an old aim reveal.");
                input.Release(gamepad.leftTrigger, queueEventOnly: true);
                root.AutomaticSimulation = true;
                yield return ConsumeMouseLookInput();
                input.Press(gamepad.leftTrigger, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                root.AutomaticSimulation = false;
                root.Hero.Firearm.Advance(root.Hero.Firearm.Settings.RaiseSeconds);
                Assert.That(root.Hero.Firearm.TryFire(), Is.True);
                Assert.That(root.Hero.Firearm.AddReserveRounds(1), Is.EqualTo(1));
                input.Release(gamepad.leftTrigger, queueEventOnly: true);
                root.AutomaticSimulation = true;
                yield return ConsumeMouseLookInput();
                input.Press(keyboard.rKey, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                input.Release(keyboard.rKey, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                root.AutomaticSimulation = false;
                Assert.That(root.Hero.Firearm.ReloadPending, Is.True);
                root.AdvanceHudPresentation(.4f);
                Assert.That(root.ReloadHudOpacity, Is.EqualTo(1f).Within(.001f));
                Assert.That(root.AmmunitionHudOpacity, Is.Zero, "Reload feedback does not force the aim counter on screen.");
                root.Hero.Firearm.Advance(root.Hero.Firearm.Settings.ReloadSeconds + .1f);
                root.AdvanceHudPresentation(.4f);
                Assert.That(root.ReloadHudOpacity, Is.Zero);

                Assert.That(root.Hero.State.ReceiveUnarmedHit(10f, 0f, false), Is.EqualTo(MeleeHitResult.Hit));
                root.AdvanceHudPresentation(.2f);
                Assert.That(root.HealthHudOpacity, Is.EqualTo(1f).Within(.001f));
                yield return CaptureGameplayHud("Test health HUD after damage");
                root.AdvanceHudPresentation(2.5f);
                Assert.That(root.HealthHudOpacity, Is.EqualTo(1f).Within(.001f), "Recent damage remains readable.");
                root.AdvanceHudPresentation(1f);
                Assert.That(root.HealthHudOpacity, Is.Zero);
                Assert.That(root.Hero.State.ReceiveUnarmedHit(65f, 0f, false), Is.EqualTo(MeleeHitResult.Hit));
                root.AdvanceHudPresentation(10f);
                Assert.That(root.Hero.State.Health, Is.EqualTo(root.Hero.State.Settings.MaxHealth * .25f));
                Assert.That(root.HealthHudOpacity, Is.EqualTo(1f).Within(.001f), "Critical health remains visible.");

                root.ResetRound();
                AssertCombatHudHidden();
                Assert.That(root.Hero.State.TryStartStep(), Is.True);
                root.AdvanceHudPresentation(.2f);
                Assert.That(root.StaminaHudOpacity, Is.EqualTo(1f).Within(.001f));
                root.Hero.State.Advance(30f);
                Assert.That(root.Hero.State.Stamina, Is.EqualTo(root.Hero.State.Settings.MaxStamina));
                root.AdvanceHudPresentation(.1f);
                root.AdvanceHudPresentation(.5f);
                Assert.That(root.StaminaHudOpacity, Is.EqualTo(1f).Within(.001f), "Full stamina holds briefly before fading.");
                root.AdvanceHudPresentation(1f);
                Assert.That(root.StaminaHudOpacity, Is.Zero);

                root.ResetRound();
                root.AutomaticSimulation = true;
                input.Press(keyboard.digit2Key, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                input.Release(keyboard.digit2Key, queueEventOnly: true);
                yield return WaitFor(() => root.HeroWeapon == CombatWeaponId.Crowbar && !root.Hero.IsSwitchingWeapon,
                    "The HUD weapon-switch case did not finish drawing the crowbar.");
                root.AutomaticSimulation = false;
                root.AdvanceHudPresentation(.4f);
                Assert.That(root.AmmunitionHudOpacity + root.ReloadHudOpacity, Is.Zero);

                root.AutomaticSimulation = true;
                input.Press(mouse.rightButton, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                root.AutomaticSimulation = false;
                root.AdvanceHudPresentation(.2f);
                Assert.That(root.Hero.GuardRequested, Is.True);
                Assert.That(root.GuardHudOpacity, Is.EqualTo(1f).Within(.001f));
                root.AutomaticSimulation = true;
                input.Release(mouse.rightButton, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                root.AutomaticSimulation = false;
                root.AdvanceHudPresentation(.4f);
                Assert.That(root.GuardHudOpacity, Is.Zero);
                root.Hero.Step(.5f);
                root.AutomaticSimulation = true;
                input.Press(mouse.leftButton, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                root.AutomaticSimulation = false;
                root.AdvanceHudPresentation(.2f);
                Assert.That(root.Hero.State.IsCharging, Is.True);
                Assert.That(root.ChargeHudOpacity, Is.EqualTo(1f).Within(.001f));
                root.AutomaticSimulation = true;
                input.Release(mouse.leftButton, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                root.AutomaticSimulation = false;
                root.AdvanceHudPresentation(.4f);
                Assert.That(root.ChargeHudOpacity, Is.Zero);

                root.Hero.State.ReceiveUnarmedHit(root.Hero.State.Settings.MaxHealth, 0f, false);
                root.Tick(.2f);
                root.AdvanceHudPresentation(.2f);
                Assert.That(root.Hero.State.IsDefeated && root.RoundFinished, Is.True);
                Assert.That(root.GameplayHudVisible, Is.True);
                yield return CaptureGameplayHud("Test gameplay HUD after defeat");
                input.Press(keyboard.enterKey, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                input.Release(keyboard.enterKey, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                Assert.That(root.RoundFinished, Is.False, "Enter starts a new run from the player result screen.");
                Assert.That(root.Hero.State.Health, Is.EqualTo(root.Hero.State.Settings.MaxHealth));
                AssertCombatHudHidden();

                input.Press(keyboard.f8Key, queueEventOnly: true);
                yield return ConsumeMouseLookInput();
                Assert.That(root.DebugHudVisible, Is.True);
                root.enabled = false;
                Assert.That(root.DebugHudVisible || root.GameplayHudVisible, Is.False);
                AssertCombatHudHidden();
                root.enabled = true;
                Assert.That(root.DebugHudVisible, Is.False, "Disable releases the legacy HUD state.");
                Assert.That(root.GameplayHudVisible, Is.True);
            }
            finally
            {
                modal.Restore();
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
                Application.runInBackground = previousBackground;
                CombatTestRoot.WarehouseSeedForTests = previousSeed;
                GameCursor.Refresh();
            }
        }

        private void AssertCombatHudHidden()
        {
            Assert.That(root.AmmunitionHudOpacity + root.ReloadHudOpacity +
                root.HealthHudOpacity + root.StaminaHudOpacity + root.GuardHudOpacity + root.ChargeHudOpacity, Is.Zero);
        }

        private static IEnumerator CaptureGameplayHud(string name)
        {
            // Camera.Render omits IMGUI. Only an actual Game view proves this visual contract.
            if (Application.isBatchMode) yield break;
#if UNITY_EDITOR
            Type viewType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
            UnityEditor.EditorWindow view = UnityEditor.EditorWindow.GetWindow(viewType);
            view.Show();
            view.Focus();
            view.Repaint();
            yield return null;
#endif
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures", SceneIds.CombatTest, name + ".png"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            DateTime previousWrite = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.realtimeSinceStartup + 3f;
            do { yield return null; }
            while ((!File.Exists(path) || File.GetLastWriteTimeUtc(path) <= previousWrite) && Time.realtimeSinceStartup < deadline);
            Assert.That(File.Exists(path) && File.GetLastWriteTimeUtc(path) > previousWrite, Is.True,
                "The Game view must capture the actual gameplay or legacy HUD.");
        }
    }
}
