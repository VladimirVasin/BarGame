using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        [UnityTest]
        public IEnumerator Range_EndlessReusesOpponentPreservesHeroRefillsAmmoAndCancelsOnExit()
        {
            foreach (CombatWeaponId secondary in new[] { CombatWeaponId.Crowbar, CombatWeaponId.Shotgun })
            {
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                var selection = new CombatTestSelection(new CombatLoadoutSelection(secondary, secondary), CombatTestMode.Endless);
                StartMenuRoot menu = Object.FindAnyObjectByType<StartMenuRoot>();
                Assert.That(menu.SelectOption(StartMenuOption.CombatTest), Is.True);
                Assert.That(menu.ConfirmSelection(), Is.True);
                Assert.That(menu.IsChoosingCombatWeapon, Is.True);
                if (secondary == CombatWeaponId.Shotgun)
                    Assert.That(menu.SelectCombatOption(CombatPreparationOption.Shotgun), Is.True);
                Assert.That(menu.SelectCombatOption(CombatPreparationOption.Secondary), Is.True);
                Assert.That(menu.SelectCombatOption(CombatPreparationOption.Endless), Is.True);
                Assert.That(menu.SelectedCombatSelection, Is.EqualTo(selection));
                if (secondary == CombatWeaponId.Crowbar)
                {
                    yield return CaptureEndlessGameView("endless-preparation-ru", "ru");
                    yield return CaptureEndlessGameView("endless-preparation-en", "en");
                }
                Assert.That(menu.SelectCombatOption(CombatPreparationOption.Start), Is.True);
                Assert.That(menu.ConfirmSelection(), Is.True);
                yield return AwaitSelectedCombatRange();
                Assert.That(root.Mode, Is.EqualTo(CombatTestMode.Endless));
                Assert.That(root.Sparring, Is.True, "An endless run starts with the opponent active.");
                root.SetSparring(false);
                Assert.That(root.Sparring, Is.True, "A running endless fight cannot become a passive dummy.");

                // A living fallen opponent is still the same encounter. The rules
                // signal is sufficient here; physical recovery has its own coverage.
                root.Opponent.State.BeginKnockdown();
                root.Tick(.025f);
                Assert.That(root.WaitingForNextOpponent || root.EndlessRunFinished, Is.False);
                Assert.That(root.EndlessKills, Is.Zero);
                root.Opponent.State.EndKnockdown();

                CombatActor hero = root.Hero, opponent = root.Opponent;
                GameObject opponentWeapon = opponent.Weapon;
                GameObject heldWeapon = hero.Weapon;
                IFirearmState pistol = hero.CarriedFirearm(CombatWeaponId.Pistol);
                IFirearmState shotgun = hero.CarriedFirearm(CombatWeaponId.Shotgun);
                // Setup does not reset either fighter: the following boundaries
                // must retain this wound and this deliberately displaced player.
                hero.State.ReceiveProjectileHit(13f);
                hero.State.Advance(.2f);
                Assert.That(hero.BodyDamage.Apply(BodyDamageRegion.LeftForearm, 0, .25f, 1, 1, 0), Is.True);
                root.BodyEffects.SynchronizeActor(hero, hero.LastImpact);
                hero.Body.Move(new Vector3(.4f, 0f, -.3f));
                Physics.SyncTransforms();
                float health = hero.State.Health;
                float trauma = hero.BodyDamage.TissueLoss(BodyDamageRegion.LeftForearm, 0);
                Vector3 position = hero.transform.position;

                for (int encounter = 1; encounter <= 3; encounter++)
                {
                    SpendEndlessRound(pistol);
                    if (shotgun != null) SpendEndlessRound(shotgun);
                    int pistolSequence = pistol.ShotSequence;
                    int shotgunSequence = shotgun?.ShotSequence ?? 0;
                    Assert.That(pistol.TryReload(), Is.True);
                    pistol.Advance(.15f);
                    if (shotgun != null)
                    {
                        Assert.That(shotgun.TryReload(), Is.True);
                        shotgun.Advance(.15f);
                    }

                    // Use a real swept anatomical head contact for every kill.
                    QueueEndlessHeadshot(hero, opponent, encounter);
                    root.Projectiles.Advance(CombatTestRoot.SimulationStep, hero, opponent);
                    root.Projectiles.ApplyContacts();
                    Assert.That(opponent.State.IsDefeated && opponent.IsRagdollActive, Is.True);
                    Assert.That(root.HeadEffects.DetachedSectorCountFor(opponent), Is.GreaterThan(0));
                    Assert.That(root.Projectiles.TrySpawn(hero, new Vector3(-4f, 8f, -4f), Vector3.up, encounter + 20), Is.True,
                        "An old flight is deliberately left alive at the encounter boundary.");
                    root.Tick(.2f); // Consume the terminal contact hold, then start the interval.
                    Assert.That(root.EndlessKills, Is.EqualTo(encounter));
                    Assert.That(root.WaitingForNextOpponent, Is.True);
                    Assert.That(root.EndlessRunFinished, Is.False);
                    Assert.That(root.Taunt.CanInteract(root.Player.Interactor), Is.False,
                        "The automatic next encounter cannot overlap the settled-body camera interaction.");

                    opponent.State.ReceiveProjectileHit(1f);
                    root.Tick(.025f);
                    Assert.That(root.EndlessKills, Is.EqualTo(encounter), "A defeated body is counted once.");
                    if (encounter == 1)
                    {
                        float remaining = root.NextOpponentSeconds;
                        Assert.That(remaining, Is.InRange(2f, 2.5f));
                        Assert.That(root.PauseMenu.Open(), Is.True);
                        root.Tick(4f);
                        yield return null;
                        Assert.That(root.NextOpponentSeconds, Is.EqualTo(remaining).Within(.00001f));
                        Assert.That(root.Opponent.State.IsDefeated, Is.True);
                        Assert.That(root.PauseMenu.Cancel(), Is.True);
                        yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay),
                            "Endless intermission pause did not release input.");
                        if (secondary == CombatWeaponId.Crowbar)
                            yield return CaptureEndlessGameView("endless-hud-ru", "ru");
                    }
                    float untilSpawn = root.NextOpponentSeconds;
                    root.Tick(Mathf.Max(0f, untilSpawn - .05f));
                    Assert.That(root.WaitingForNextOpponent, Is.True, "The next opponent cannot arrive before the interval ends.");
                    Assert.That(root.Projectiles.TrySpawn(hero, new Vector3(-4f, 8f, -4f), Vector3.up, encounter + 30), Is.True);
                    Assert.That(root.Projectiles.ActiveCount, Is.GreaterThan(0), "A late old flight must still exist when the reset begins.");
                    root.Tick(.075f);
                    Assert.That(root.WaitingForNextOpponent || root.EndlessRunFinished || root.RoundFinished, Is.False);
                    Assert.That(root.Opponent, Is.SameAs(opponent));
                    Assert.That(opponent.Weapon, Is.SameAs(opponentWeapon));
                    Assert.That(root.GetComponentsInChildren<CombatActor>(true).Length, Is.EqualTo(2),
                        "Repeated encounters reuse the permanent opponent and its existing models.");
                    Assert.That(opponent.State.Health, Is.EqualTo(opponent.State.Settings.MaxHealth));
                    Assert.That(opponent.IsRagdollActive || opponent.IsWeaponDropped, Is.False);
                    Assert.That(root.HeadEffects.DetachedSectorCountFor(opponent), Is.Zero);
                    Assert.That(root.BloodEffects.WoundCountFor(opponent), Is.Zero);
                    Assert.That(root.Projectiles.ActiveCount, Is.Zero, "The old encounter cannot send a bullet into the new opponent.");
                    Assert.That(hero.State.Health, Is.EqualTo(health).Within(.0001f));
                    Assert.That(hero.BodyDamage.TissueLoss(BodyDamageRegion.LeftForearm, 0), Is.EqualTo(trauma));
                    Assert.That(Vector3.Distance(hero.transform.position, position), Is.LessThan(.03f));
                    Assert.That(hero.Weapon, Is.SameAs(heldWeapon));
                    Assert.That(hero.WeaponId, Is.EqualTo(secondary));
                    Assert.That(root.Player.Motor.InputEnabled, Is.True);
                    AssertEndlessAmmunition(pistol, pistolSequence);
                    if (shotgun != null) AssertEndlessAmmunition(shotgun, shotgunSequence);
                    Assert.That(Vector3.ProjectOnPlane(opponent.transform.position - hero.transform.position, Vector3.up).magnitude,
                        Is.GreaterThanOrEqualTo(3f), "A reused opponent appears at a safe distance from the surviving player.");
                }

                // Both terminal outcomes cancel the pending spawn; simultaneous
                // contact still credits the opponent's death exactly once.
                foreach (bool simultaneous in new[] { false, true })
                {
                    root.ResetRound();
                    Assert.That(root.EndlessKills, Is.Zero);
                    Assert.That(root.WaitingForNextOpponent || root.EndlessRunFinished || root.RoundFinished, Is.False);
                    Assert.That(hero.State.Health, Is.EqualTo(hero.State.Settings.MaxHealth));
                    Assert.That(hero.BodyDamage.TissueLoss(BodyDamageRegion.LeftForearm, 0), Is.Zero);
                    QueueEndlessHeadshot(opponent, hero, 100);
                    if (simultaneous) QueueEndlessHeadshot(hero, opponent, 101);
                    root.Projectiles.Advance(CombatTestRoot.SimulationStep, hero, opponent);
                    root.Projectiles.ApplyContacts();
                    root.Tick(.2f);
                    Assert.That(root.EndlessRunFinished, Is.True);
                    Assert.That(root.WaitingForNextOpponent, Is.False);
                    Assert.That(root.EndlessKills, Is.EqualTo(simultaneous ? 1 : 0));
                    root.Tick(6f);
                    Assert.That(hero.State.IsDefeated, Is.True);
                    Assert.That(opponent.State.IsDefeated, Is.EqualTo(simultaneous));
                    Assert.That(root.WaitingForNextOpponent, Is.False);
                }

                root.ResetRound();
                Assert.That(root.EndlessRunFinished || root.RoundFinished, Is.False);
                QueueEndlessHeadshot(hero, opponent, 110);
                root.Projectiles.Advance(CombatTestRoot.SimulationStep, hero, opponent);
                root.Projectiles.ApplyContacts();
                root.Tick(.2f);
                Assert.That(root.WaitingForNextOpponent, Is.True);
                Assert.That(root.ReturnToWeapons(), Is.True);
                yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneIds.MainMenu &&
                    !SceneTransitionService.IsTransitioning, "Endless run did not return to preparation.");
                Assert.That(opponent == null && opponentWeapon == null && heldWeapon == null, Is.True);
                Assert.That(Object.FindAnyObjectByType<CombatActor>(), Is.Null);
                menu = Object.FindAnyObjectByType<StartMenuRoot>();
                Assert.That(menu.IsChoosingCombatWeapon, Is.True);
                Assert.That(menu.SelectedCombatSelection, Is.EqualTo(selection), "Preparation retains the complete mode and loadout.");
                Assert.That(menu.SelectCombatOption(CombatPreparationOption.Start), Is.True);
                Assert.That(menu.ConfirmSelection(), Is.True);
                yield return AwaitSelectedCombatRange();
                Assert.That(root.Mode, Is.EqualTo(CombatTestMode.Endless));
                Assert.That(root.EndlessKills, Is.Zero);
                Assert.That(root.WaitingForNextOpponent || root.EndlessRunFinished, Is.False);
                Assert.That(root.ReturnToMenu(), Is.True);
                yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneIds.MainMenu &&
                    !SceneTransitionService.IsTransitioning, "Fresh endless run did not return to the main menu.");
                Assert.That(Object.FindAnyObjectByType<CombatActor>(), Is.Null);
                Assert.That(PauseMenuController.IsAnyPaused, Is.False);
            }
            LogAssert.NoUnexpectedReceived();
        }

        private static void SpendEndlessRound(IFirearmState firearm)
        {
            firearm.SetAim(true);
            firearm.Advance(firearm.Settings.RaiseSeconds + firearm.Settings.FireCooldownSeconds);
            Assert.That(firearm.TryFire(), Is.True);
            firearm.CancelAction();
            Assert.That(firearm.Rounds, Is.EqualTo(firearm.Settings.MagazineCapacity - 1));
        }

        private void QueueEndlessHeadshot(CombatActor source, CombatActor target, int sequence)
        {
            target.CaptureContactPose();
            Assert.That(target.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head, out Vector3 point,
                out Vector3 forward, out Vector3 up), Is.True);
            Vector3 outward = target.IsHero ? Vector3.Cross(up, forward).normalized : forward;
            Assert.That(root.Projectiles.TrySpawn(source, point + outward * .75f,
                -outward * CombatProjectilePool.MuzzleSpeed, sequence), Is.True);
        }

        private static void AssertEndlessAmmunition(IFirearmState firearm, int sequence)
        {
            Assert.That(firearm.Rounds, Is.EqualTo(firearm.Settings.MagazineCapacity));
            Assert.That(firearm.ShotSequence, Is.EqualTo(sequence), "Refilling must retain the monotonic shot identity.");
            Assert.That(firearm.IsReloading || firearm.ReloadPending, Is.False);
            Assert.That(firearm.MagazineAttached, Is.True);
            if (firearm is ShotgunState shotgun)
            {
                Assert.That(shotgun.ChamberLoaded(0) && shotgun.ChamberLoaded(1), Is.True);
                Assert.That(shotgun.ChamberSpent(0) || shotgun.ChamberSpent(1), Is.False);
            }
        }

        private static IEnumerator CaptureEndlessGameView(string name, string language)
        {
            const int width = 640, height = 360;
            int previousWidth = Screen.width, previousHeight = Screen.height;
            FullScreenMode previousMode = Screen.fullScreenMode;
            IDisposable gameView = null;
            using (var localized = new EndlessLanguageScope(language))
            {
                try
                {
#if UNITY_EDITOR
                    // Reuse the existing temporary Game View size owner, including
                    // its restoration, rather than introduce another editor helper.
                    Type sizeOwner = typeof(AreaLoadingVisualCapture).GetNestedType("GameViewResolution", BindingFlags.NonPublic);
                    Assert.That(sizeOwner, Is.Not.Null);
                    gameView = (IDisposable)Activator.CreateInstance(sizeOwner, true);
                    sizeOwner.GetMethod("Set").Invoke(gameView, new object[] { width, height });
#else
                    Screen.SetResolution(width, height, FullScreenMode.Windowed);
#endif
                    float deadline = Time.realtimeSinceStartup + 1f;
                    while ((Screen.width != width || Screen.height != height) && Time.realtimeSinceStartup < deadline)
                        yield return null;
                    // Batch Editor does not execute WaitForEndOfFrame. Attempt its
                    // real framebuffer as well; no camera-only substitute is used.
                    if (Application.isBatchMode) { yield return null; yield return null; }
                    else yield return new WaitForEndOfFrame();
                    Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
                    try
                    {
                        Assert.That(frame, Is.Not.Null, "The endless UI capture requires a rendered framebuffer.");
                        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures", SceneIds.CombatTest, name + ".png"));
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        File.WriteAllBytes(path, frame.EncodeToPNG());
                        TestContext.Progress.WriteLine("Endless UI frame: " + path + "; requested " + width + "x" + height +
                            "; framebuffer " + frame.width + "x" + frame.height + "; batch=" + Application.isBatchMode);
                        if (!Application.isBatchMode)
                            Assert.That(new Vector2Int(frame.width, frame.height), Is.EqualTo(new Vector2Int(width, height)));
                    }
                    finally { if (frame != null) Object.Destroy(frame); }
                }
                finally
                {
                    gameView?.Dispose();
#if !UNITY_EDITOR
                    Screen.SetResolution(previousWidth, previousHeight, previousMode);
#endif
                }
            }
        }

        [Serializable]
        private sealed class EndlessCatalog { public EndlessCatalogEntry[] entries; }
        [Serializable]
        private sealed class EndlessCatalogEntry { public string key; public string value; }

        private sealed class EndlessLanguageScope : IDisposable
        {
            private readonly Dictionary<string, string> values, previous;
            public EndlessLanguageScope(string language)
            {
                LocalizationService.Get("combat.title");
                values = (Dictionary<string, string>)typeof(LocalizationService).GetField("Values",
                    BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                previous = new Dictionary<string, string>(values);
                TextAsset asset = Resources.Load<TextAsset>("Localization/" + language);
                Assert.That(asset, Is.Not.Null);
                foreach (EndlessCatalogEntry entry in JsonUtility.FromJson<EndlessCatalog>(asset.text).entries)
                    values[entry.key] = entry.value;
            }
            public void Dispose()
            {
                values.Clear();
                foreach (var entry in previous) values.Add(entry.Key, entry.Value);
            }
        }
    }
}
