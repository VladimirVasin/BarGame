using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarPromenade.Tests.EditMode
{
    public sealed class StartMenuModelTests
    {
        [Test]
        public void Open_SelectsNewGameAndArmsTheCard()
        {
            var model = new StartMenuModel();

            model.Open();

            Assert.That(
                model.SelectedOption,
                Is.EqualTo(StartMenuOption.NewGame));
            Assert.That(model.IsCommitted, Is.False);
            Assert.That(model.IsChoosingLocation, Is.False);
            Assert.That(model.IsChoosingCombatWeapon, Is.False);
            Assert.That(model.SelectedCombatWeapon, Is.EqualTo(CombatWeaponId.Crowbar));
            Assert.That(model.SelectedLocation, Is.EqualTo(NewGameLocation.AlpineVillage));
            Assert.That(model.IsBackSelected, Is.False);
        }

        [Test]
        public void Navigation_WrapsBetweenTheThreeOptions()
        {
            var model = new StartMenuModel();
            model.Open();

            Assert.That(model.MoveSelection(-1), Is.True);
            Assert.That(
                model.SelectedOption,
                Is.EqualTo(StartMenuOption.Quit));
            Assert.That(model.MoveSelection(1), Is.True);
            Assert.That(
                model.SelectedOption,
                Is.EqualTo(StartMenuOption.NewGame));
            Assert.That(model.MoveSelection(1), Is.True);
            Assert.That(model.SelectedOption, Is.EqualTo(StartMenuOption.CombatTest));
            Assert.That(model.MoveSelection(1), Is.True);
            Assert.That(model.SelectedOption, Is.EqualTo(StartMenuOption.Quit));
            Assert.That(model.MoveSelection(0), Is.False);
        }

        [Test]
        public void Confirm_ChoosesALocationBeforeStartingTheRunOnce()
        {
            var model = new StartMenuModel();
            model.Open();

            Assert.That(
                model.Confirm(),
                Is.EqualTo(StartMenuAction.ChooseLocation));
            Assert.That(model.IsChoosingLocation, Is.True);
            Assert.That(model.IsCommitted, Is.False, "Opening the picker must not latch the new game.");
            Assert.That(model.SelectedLocation, Is.EqualTo(NewGameLocation.AlpineVillage));
            Assert.That(model.SelectLocation(NewGameLocation.Cannery), Is.True);
            Assert.That(
                model.Confirm(),
                Is.EqualTo(StartMenuAction.NewGame));
            Assert.That(model.IsCommitted, Is.True);

            // A mouse click does not consult the input policy, so a second
            // activation must die here rather than ask for a second trip.
            Assert.That(
                model.Confirm(),
                Is.EqualTo(StartMenuAction.None));
            Assert.That(model.MoveSelection(1), Is.False);
            Assert.That(
                model.SelectOption(StartMenuOption.Quit),
                Is.False);
            Assert.That(
                model.SelectedOption,
                Is.EqualTo(StartMenuOption.NewGame));
            Assert.That(model.SelectLocation(NewGameLocation.Home), Is.False);
            Assert.That(model.SelectBack(), Is.False);
            Assert.That(model.ReturnToMainMenu(), Is.False);
            Assert.That(model.SelectedLocation, Is.EqualTo(NewGameLocation.Cannery));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LocationNavigation_WrapsAndCanReturnWithoutCommitting(bool confirmBack)
        {
            var model = new StartMenuModel();
            model.Open();
            model.Confirm();
            Assert.That(model.MoveSelection(-1), Is.True);
            Assert.That(model.IsBackSelected, Is.True);
            Assert.That(model.MoveSelection(1), Is.True);
            Assert.That(model.IsBackSelected, Is.False);
            Assert.That(model.SelectedLocation, Is.EqualTo(NewGameLocation.AlpineVillage));
            Assert.That(model.SelectLocation(NewGameLocation.Church), Is.True);
            Assert.That(model.MoveSelection(1), Is.True);
            Assert.That(model.SelectedLocation, Is.EqualTo(NewGameLocation.SkiLodge));
            Assert.That(model.MoveSelection(1), Is.True);
            Assert.That(model.IsBackSelected, Is.True);
            if (confirmBack) Assert.That(model.Confirm(), Is.EqualTo(StartMenuAction.Back));
            else Assert.That(model.ReturnToMainMenu(), Is.True);
            Assert.That(model.IsChoosingLocation, Is.False);
            Assert.That(model.IsCommitted, Is.False);
            Assert.That(model.SelectedOption, Is.EqualTo(StartMenuOption.NewGame));
            Assert.That(model.Confirm(), Is.EqualTo(StartMenuAction.ChooseLocation));
            Assert.That(model.IsChoosingLocation, Is.True);
            Assert.That(model.IsCommitted, Is.False);
        }

        [Test]
        public void LocationCatalog_HasTwelveDistinctLocalizedChoicesInGameplayScenes()
        {
            Assert.That(NewGameLocationCatalog.Count, Is.EqualTo(12));
            Assert.That((int)NewGameLocation.Count, Is.EqualTo(NewGameLocationCatalog.Count));
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var locations = new HashSet<NewGameLocation>();
            for (int index = 0; index < NewGameLocationCatalog.Count; index++)
            {
                NewGameLocation location = NewGameLocationCatalog.Get(index);
                Assert.That(locations.Add(location), Is.True);
                string key = NewGameLocationCatalog.LabelKey(location);
                Assert.That(keys.Add(key), Is.True, "Every visible row owns a distinct localized label.");
                string scene = NewGameLocationCatalog.SceneName(location);
                Assert.That(scene, Is.Not.EqualTo(SceneIds.MainMenu).And.Not.EqualTo(SceneIds.AreaLoading)
                    .And.Not.EqualTo(SceneIds.DoorTransition));
                Assert.That(SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/" + scene + ".unity"),
                    Is.GreaterThanOrEqualTo(0), location + " needs an enabled gameplay build scene.");
            }
            foreach (string language in new[] { "ru", "en" })
            {
                TextAsset asset = Resources.Load<TextAsset>("Localization/" + language);
                Assert.That(asset, Is.Not.Null);
                var catalog = JsonUtility.FromJson<MenuCatalog>(asset.text);
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (MenuEntry entry in catalog.entries) values[entry.key] = entry.value;
                var labels = new HashSet<string>(StringComparer.Ordinal);
                foreach (string key in keys)
                {
                    Assert.That(values.TryGetValue(key, out string label), Is.True, language + ": " + key);
                    Assert.That(label, Is.Not.Null.And.Not.Empty.And.Not.EqualTo(key));
                    Assert.That(labels.Add(label), Is.True, language + " must distinguish all twelve destinations.");
                }
                foreach (string key in new[] { "opening.choose_location", "opening.back", "combat.title",
                    "combat.target", "combat.sparring", "combat.reset", "combat.menu", "combat.health",
                    "combat.stamina", "combat.opponent", "combat.victory", "combat.defeat", "combat.controls",
                    "combat.choose_weapon", "combat.weapon.crowbar", "combat.weapon.pistol", "combat.start" })
                    Assert.That(values.TryGetValue(key, out string value) && !string.IsNullOrWhiteSpace(value), Is.True, key);
            }
        }

        [TestCase(StartMenuOption.Quit, StartMenuAction.Quit)]
        public void Confirm_DirectActionCommitsOnceWithoutChoosingALocation(
            StartMenuOption option, StartMenuAction action)
        {
            var model = new StartMenuModel();
            model.Open();

            Assert.That(
                model.SelectOption(option),
                Is.True);
            Assert.That(
                model.Confirm(),
                Is.EqualTo(action));
            Assert.That(model.IsChoosingLocation, Is.False);
            Assert.That(model.IsCommitted, Is.True);
            Assert.That(model.Confirm(), Is.EqualTo(StartMenuAction.None));
            Assert.That(model.MoveSelection(1), Is.False);
        }

        [TestCase(CombatWeaponId.Crowbar)]
        [TestCase(CombatWeaponId.Pistol)]
        public void CombatPreparation_WeaponChoiceRequiresExplicitStartAndCommitsOnce(CombatWeaponId weapon)
        {
            var model = new StartMenuModel();
            model.Open();
            model.SelectOption(StartMenuOption.CombatTest);
            Assert.That(model.Confirm(), Is.EqualTo(StartMenuAction.ChooseCombatWeapon));
            Assert.That(model.IsChoosingCombatWeapon, Is.True);
            Assert.That(model.IsChoosingLocation, Is.False);
            Assert.That(model.IsCommitted, Is.False);
            Assert.That(model.SelectedCombatWeapon, Is.EqualTo(CombatWeaponId.Crowbar));
            model.SelectCombatOption(weapon == CombatWeaponId.Pistol
                ? CombatPreparationOption.Pistol : CombatPreparationOption.Crowbar);
            Assert.That(model.Confirm(), Is.EqualTo(StartMenuAction.ChooseCombatWeapon),
                "Confirming the weapon row must not launch a duel.");
            Assert.That(model.IsCommitted, Is.False);
            Assert.That(model.SelectOption(StartMenuOption.Quit), Is.False);
            Assert.That(model.SelectLocation(NewGameLocation.Home), Is.False);
            Assert.That(model.SelectCombatOption(CombatPreparationOption.Start), Is.True);
            Assert.That(model.SelectedCombatWeapon, Is.EqualTo(weapon));
            Assert.That(model.Confirm(), Is.EqualTo(StartMenuAction.CombatTest));
            Assert.That(model.IsCommitted, Is.True);
            Assert.That(model.Confirm(), Is.EqualTo(StartMenuAction.None));
            Assert.That(model.MoveSelection(1), Is.False);
            Assert.That(model.SelectBack(), Is.False);
            Assert.That(model.ReturnToMainMenu(), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CombatPreparation_NavigationAndBackKeepTheWeaponWithoutStarting(bool confirmBack)
        {
            var model = new StartMenuModel();
            model.OpenCombatPreparation(CombatWeaponId.Crowbar);
            Assert.That(model.MoveSelection(-1), Is.True);
            Assert.That(model.IsBackSelected, Is.True);
            Assert.That(model.MoveSelection(1), Is.True);
            Assert.That(model.SelectedCombatOption, Is.EqualTo(CombatPreparationOption.Crowbar));
            Assert.That(model.MoveSelection(1), Is.True);
            Assert.That(model.SelectedCombatWeapon, Is.EqualTo(CombatWeaponId.Pistol));
            Assert.That(model.MoveSelection(1), Is.True);
            Assert.That(model.SelectedCombatOption, Is.EqualTo(CombatPreparationOption.Start));
            if (confirmBack)
            {
                Assert.That(model.SelectBack(), Is.True);
                Assert.That(model.Confirm(), Is.EqualTo(StartMenuAction.Back));
            }
            else Assert.That(model.ReturnToMainMenu(), Is.True);
            Assert.That(model.IsChoosingCombatWeapon, Is.False);
            Assert.That(model.SelectedOption, Is.EqualTo(StartMenuOption.CombatTest));
            Assert.That(model.IsCommitted, Is.False);
            Assert.That(model.Confirm(), Is.EqualTo(StartMenuAction.ChooseCombatWeapon));
            Assert.That(model.SelectedCombatWeapon, Is.EqualTo(CombatWeaponId.Pistol));
            Assert.That(model.SelectedCombatOption, Is.EqualTo(CombatPreparationOption.Pistol));

            model.SelectCombatOption(CombatPreparationOption.Start);
            model.Confirm();
            model.OpenCombatPreparation(CombatWeaponId.Pistol);
            Assert.That(model.IsCommitted, Is.False, "A refused trip must return to an interactive preparation screen.");
            Assert.That(model.SelectedCombatWeapon, Is.EqualTo(CombatWeaponId.Pistol));
        }

        [Test]
        public void Reopening_ReArmsACardWhoseTripWasRefused()
        {
            var model = new StartMenuModel();
            model.Open();
            model.Confirm();
            model.Confirm();

            model.Open();

            Assert.That(model.IsCommitted, Is.False);
            Assert.That(
                model.SelectedOption,
                Is.EqualTo(StartMenuOption.NewGame));
            Assert.That(
                model.Confirm(),
                Is.EqualTo(StartMenuAction.ChooseLocation));
        }

        [Serializable]
        private sealed class MenuCatalog { public MenuEntry[] entries = Array.Empty<MenuEntry>(); }
        [Serializable]
        private sealed class MenuEntry { public string key = string.Empty; public string value = string.Empty; }
    }
}
