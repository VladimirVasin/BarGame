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
            Assert.That(model.SelectedCombatLoadout, Is.EqualTo(CombatLoadoutSelection.Default));
            Assert.That(model.SelectedCombatMode, Is.EqualTo(CombatTestMode.Standard));
            Assert.That(model.SelectedCombatSelection, Is.EqualTo(CombatTestSelection.Default));
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
                    "combat.choose_weapon", "combat.weapon.crowbar", "combat.weapon.pistol", "combat.weapon.shotgun",
                    "combat.weapon.shotgun.description", "combat.shotgun.ammo", "combat.shotgun.reloading",
                    "combat.shotgun.ready", "combat.shotgun.empty", "combat.shotgun.reload", "combat.controls.shotgun",
                    "combat.weapon.pickup_shotgun", "inventory.item.combat_shotgun.name",
                    "inventory.item.combat_shotgun.description", "combat.start", "combat.loadout.pistol_carried",
                    "combat.loadout.initial_held",
                    "combat.loadout.crowbar_carried", "combat.loadout.shotgun_carried", "combat.switch.controls",
                    "combat.weapon.switching", "combat.mode.title", "combat.mode.standard", "combat.mode.endless" })
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
        [TestCase(CombatWeaponId.Shotgun)]
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
            model.SelectCombatOption(weapon switch
            {
                CombatWeaponId.Pistol => CombatPreparationOption.Pistol,
                CombatWeaponId.Shotgun => CombatPreparationOption.Shotgun,
                _ => CombatPreparationOption.Crowbar
            });
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
            Assert.That(model.SelectedCombatOption, Is.EqualTo(CombatPreparationOption.Shotgun));
            Assert.That(model.SelectedCombatWeapon, Is.EqualTo(CombatWeaponId.Crowbar),
                "Navigation highlights the row; only activation changes the equipment.");
            Assert.That(model.Confirm(), Is.EqualTo(StartMenuAction.ChooseCombatWeapon));
            Assert.That(model.SelectedCombatWeapon, Is.EqualTo(CombatWeaponId.Shotgun));
            Assert.That(model.MoveSelection(1), Is.True);
            Assert.That(model.SelectedCombatOption, Is.EqualTo(CombatPreparationOption.Pistol));
            Assert.That(model.MoveSelection(1), Is.True);
            Assert.That(model.SelectedCombatOption, Is.EqualTo(CombatPreparationOption.Standard));
            Assert.That(model.MoveSelection(1), Is.True);
            Assert.That(model.SelectedCombatOption, Is.EqualTo(CombatPreparationOption.Endless));
            Assert.That(model.SelectedCombatMode, Is.EqualTo(CombatTestMode.Standard),
                "Mode focus must leave the active radio choice unchanged until confirmation.");
            Assert.That(model.Confirm(), Is.EqualTo(StartMenuAction.ChooseCombatWeapon));
            Assert.That(model.SelectedCombatMode, Is.EqualTo(CombatTestMode.Endless));
            Assert.That(model.IsCommitted, Is.False);
            Assert.That(model.MoveSelection(1), Is.True);
            Assert.That(model.SelectedCombatOption, Is.EqualTo(CombatPreparationOption.Crane));
            Assert.That(model.SelectedCombatMode, Is.EqualTo(CombatTestMode.Endless),
                "Highlighting the crane prototype must not change the active mode.");
            Assert.That(model.MoveSelection(1), Is.True);
            Assert.That(model.SelectedCombatOption, Is.EqualTo(CombatPreparationOption.OpponentLimit));
            Assert.That(model.SelectedCombatEncounterSettings.MaximumLiveOpponents,
                Is.EqualTo(CombatEncounterSettings.DefaultMaximumLiveOpponents));
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
            Assert.That(model.SelectedCombatWeapon, Is.EqualTo(CombatWeaponId.Shotgun));
            Assert.That(model.SelectedCombatOption, Is.EqualTo(CombatPreparationOption.Shotgun));
            Assert.That(model.SelectedCombatMode, Is.EqualTo(CombatTestMode.Endless));

            model.SelectCombatOption(CombatPreparationOption.Start);
            model.Confirm();
            model.OpenCombatPreparation(CombatWeaponId.Shotgun);
            Assert.That(model.IsCommitted, Is.False, "A refused trip must return to an interactive preparation screen.");
            Assert.That(model.SelectedCombatWeapon, Is.EqualTo(CombatWeaponId.Shotgun));
            Assert.That(model.SelectedCombatMode, Is.EqualTo(CombatTestMode.Standard),
                "Legacy preparation overloads retain the ordinary polygon default.");
        }

        [TestCase(CombatWeaponId.Pistol, CombatTestMode.Standard)]
        [TestCase(CombatWeaponId.Crowbar, CombatTestMode.Standard)]
        [TestCase(CombatWeaponId.Shotgun, CombatTestMode.Standard)]
        [TestCase(CombatWeaponId.Pistol, CombatTestMode.Endless)]
        [TestCase(CombatWeaponId.Crowbar, CombatTestMode.Endless)]
        [TestCase(CombatWeaponId.Shotgun, CombatTestMode.Endless)]
        [TestCase(CombatWeaponId.Pistol, CombatTestMode.Crane)]
        [TestCase(CombatWeaponId.Crowbar, CombatTestMode.Crane)]
        [TestCase(CombatWeaponId.Shotgun, CombatTestMode.Crane)]
        public void CombatPreparation_ThreeWeaponsAndInitialHandSurviveBackAndRefusedLaunch(
            CombatWeaponId initialHeld, CombatTestMode mode)
        {
            var model = new StartMenuModel();
            var expected = new CombatLoadoutSelection(initialHeld);
            var expectedSelection = new CombatTestSelection(expected, mode);
            model.OpenCombatPreparation(CombatLoadoutSelection.Default);
            model.SelectCombatOption(initialHeld switch
            {
                CombatWeaponId.Pistol => CombatPreparationOption.Pistol,
                CombatWeaponId.Shotgun => CombatPreparationOption.Shotgun,
                _ => CombatPreparationOption.Crowbar
            });
            model.FocusCombatOption(mode switch
            {
                CombatTestMode.Endless => CombatPreparationOption.Endless,
                CombatTestMode.Crane => CombatPreparationOption.Crane,
                _ => CombatPreparationOption.Standard
            });
            model.Confirm();
            Assert.That(model.SelectedCombatSelection, Is.EqualTo(expectedSelection));
            Assert.That(model.SelectedCombatLoadout, Is.EqualTo(expected));
            foreach (CombatWeaponId carried in new[] { CombatWeaponId.Pistol, CombatWeaponId.Crowbar, CombatWeaponId.Shotgun })
                Assert.That(model.SelectedCombatLoadout.Contains(carried), Is.True);
            Assert.That(new CombatLoadoutSelection(CombatWeaponId.Crowbar, initialHeld), Is.EqualTo(expected));
            Assert.That(new CombatLoadoutSelection(CombatWeaponId.Shotgun, initialHeld), Is.EqualTo(expected));

            model.SelectBack();
            model.Confirm();
            model.Confirm();
            Assert.That(model.SelectedCombatLoadout, Is.EqualTo(expected));
            Assert.That(model.SelectedCombatMode, Is.EqualTo(mode));
            model.SelectCombatOption(CombatPreparationOption.Start);
            model.Confirm();
            model.OpenCombatPreparation(expectedSelection);
            Assert.That(model.SelectedCombatLoadout, Is.EqualTo(expected));
            Assert.That(model.SelectedCombatMode, Is.EqualTo(mode));
            Assert.That(model.IsCommitted, Is.False);

            CombatWeaponId replacement = initialHeld == CombatWeaponId.Crowbar ?
                CombatWeaponId.Shotgun : CombatWeaponId.Crowbar;
            model.SelectCombatOption(replacement == CombatWeaponId.Shotgun ?
                CombatPreparationOption.Shotgun : CombatPreparationOption.Crowbar);
            Assert.That(model.SelectedCombatLoadout.InitialHeld, Is.EqualTo(replacement));
            foreach (CombatWeaponId carried in new[] { CombatWeaponId.Pistol, CombatWeaponId.Crowbar, CombatWeaponId.Shotgun })
                Assert.That(model.SelectedCombatLoadout.Contains(carried), Is.True);
            Assert.That(model.SelectedCombatMode, Is.EqualTo(mode));
        }

        [Test]
        public void CombatPreparation_CapacityActivationWrapsAndSurvivesPreparationReturn()
        {
            var model = new StartMenuModel();
            model.OpenCombatPreparation(new CombatTestSelection(CombatLoadoutSelection.Default, CombatTestMode.Endless));
            model.FocusCombatOption(CombatPreparationOption.OpponentLimit);
            Assert.That(model.SelectedCombatEncounterSettings.MaximumLiveOpponents,
                Is.EqualTo(CombatEncounterSettings.DefaultMaximumLiveOpponents),
                "Focus does not change the selected capacity.");
            Assert.That(model.Confirm(), Is.EqualTo(StartMenuAction.ChooseCombatWeapon));
            Assert.That(model.SelectedCombatEncounterSettings.MaximumLiveOpponents, Is.EqualTo(3));
            for (int expectedCapacity = 4; expectedCapacity <= CombatEncounterSettings.OpponentCapacity; expectedCapacity++)
            {
                model.Confirm();
                Assert.That(model.SelectedCombatEncounterSettings.MaximumLiveOpponents, Is.EqualTo(expectedCapacity));
            }
            model.Confirm();
            Assert.That(model.SelectedCombatEncounterSettings.MaximumLiveOpponents, Is.EqualTo(1));
            model.Confirm();
            model.Confirm();
            Assert.That(model.SelectedCombatEncounterSettings.MaximumLiveOpponents, Is.EqualTo(3));
            model.SelectCombatOption(CombatPreparationOption.Standard);
            model.SelectCombatOption(CombatPreparationOption.Endless);
            Assert.That(model.SelectedCombatEncounterSettings.MaximumLiveOpponents, Is.EqualTo(3));
            CombatTestSelection expected = model.SelectedCombatSelection;
            model.ReturnToMainMenu();
            model.Confirm();
            Assert.That(model.SelectedCombatSelection, Is.EqualTo(expected));
            model.SelectCombatOption(CombatPreparationOption.Start);
            model.Confirm();
            model.OpenCombatPreparation(expected);
            Assert.That(model.SelectedCombatSelection, Is.EqualTo(expected));
            Assert.That(model.IsCommitted, Is.False);
            model.SelectCombatOption(CombatPreparationOption.Start);
            model.Confirm();
            Assert.That(model.SelectCombatOption(CombatPreparationOption.OpponentLimit), Is.False);
            Assert.That(model.SelectedCombatSelection, Is.EqualTo(expected));
        }

        [TestCase(CombatWeaponId.Pistol, CombatWeaponId.Pistol)]
        [TestCase((CombatWeaponId)99, CombatWeaponId.Pistol)]
        [TestCase(CombatWeaponId.Crowbar, (CombatWeaponId)99)]
        public void CombatLoadout_RejectsInvalidWeaponAndInvalidLegacyLongArgument(
            CombatWeaponId secondary, CombatWeaponId initialHeld)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CombatLoadoutSelection(secondary, initialHeld));
            if (initialHeld == (CombatWeaponId)99)
                Assert.Throws<ArgumentOutOfRangeException>(() => new CombatLoadoutSelection(initialHeld));
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
