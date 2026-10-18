using System;
using System.Collections;
using System.Collections.Generic;
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
        [PrebuildSetup(typeof(CombatMixedEndlessAssetsSetup))]
        public IEnumerator Range_LaboratoryMenuSpawnsEachEnemyAndReturnsWithoutWarehouseLatch()
        {
            // The production menu selection must choose the old arena itself.
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.AuthoredEnemyKindForTests = null;
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            yield return WaitFor(() => GameInput.CanRead(GameInputContext.Menu), "The launch card did not release its input gate.");
            StartMenuRoot menu = Object.FindAnyObjectByType<StartMenuRoot>();
            Assert.That(menu, Is.Not.Null);
            yield return CaptureEndlessGameView("Test old arena - Main menu Russian", "ru", true);
            yield return CaptureEndlessGameView("Test old arena - Main menu English", "en", true);
            Assert.That(menu.SelectOption(StartMenuOption.Laboratory), Is.True);
            Assert.That(menu.ConfirmSelection(), Is.True);
            Assert.That(menu.SelectedCombatMode, Is.EqualTo(CombatTestMode.Laboratory));

            var kinds = new[] { CombatEnemyKind.Zombie, CombatEnemyKind.Docker, CombatEnemyKind.Crane,
                CombatEnemyKind.Room, CombatEnemyKind.Imprint, CombatEnemyKind.LongExposure };
            foreach (CombatEnemyKind kind in kinds)
            {
                menu.SelectCombatOption(CombatPreparationOption.LaboratoryEnemy);
                Assert.That(menu.SelectedCombatOption, Is.EqualTo(CombatPreparationOption.LaboratoryEnemy));
                Assert.That(menu.ConfirmSelection(), Is.True);
                Assert.That(menu.IsChoosingLaboratoryEnemy, Is.True);
                bool changedEnemy = menu.SelectedLaboratoryEnemy != kind;
                Assert.That(menu.SelectLaboratoryEnemy(kind), Is.EqualTo(changedEnemy));
                if (kind == CombatEnemyKind.LongExposure)
                {
                    yield return CaptureEndlessGameView("Test old arena - Enemy choices Russian", "ru", true);
                    yield return CaptureEndlessGameView("Test old arena - Enemy choices English", "en", true);
                }
                Assert.That(menu.ConfirmSelection(), Is.True);
                Assert.That(menu.IsChoosingLaboratoryEnemy, Is.False);
                Assert.That(menu.SelectedCombatSelection.LaboratoryEnemy, Is.EqualTo(kind));
            }
            Assert.That(menu.SelectCombatOption(CombatPreparationOption.Pistol), Is.True);
            CombatTestSelection selected = menu.SelectedCombatSelection;
            Assert.That(selected.Mode, Is.EqualTo(CombatTestMode.Laboratory));
            Assert.That(selected.LaboratoryEnemy, Is.EqualTo(CombatEnemyKind.LongExposure));
            Assert.That(selected.Loadout.InitialHeld, Is.EqualTo(CombatWeaponId.Pistol));
            yield return CaptureEndlessGameView("Test old arena - Preparation Russian", "ru", true);
            yield return CaptureEndlessGameView("Test old arena - Preparation English", "en", true);
            Assert.That(menu.SelectCombatOption(CombatPreparationOption.Start), Is.True);
            Assert.That(menu.ConfirmSelection(), Is.True);
            Assert.That(menu.ConfirmSelection(), Is.False, "One confirmation owns the complete launch selection.");
            Assert.That(menu.SelectOption(StartMenuOption.CombatTest), Is.False, "A second card cannot replace an in-flight launch.");
            yield return WaitForLaboratoryRoot();
            Assert.That(root.Mode, Is.EqualTo(selected.Mode));
            Assert.That(root.LaboratoryEnemy, Is.EqualTo(selected.LaboratoryEnemy));
            Assert.That(root.LoadoutSelection, Is.EqualTo(selected.Loadout));
            Assert.That(root.Hero.WeaponId, Is.EqualTo(CombatWeaponId.Pistol));
            Assert.That(root.WarehousePlan, Is.Null);
            Assert.That(Vector3.Distance(root.Opponent.transform.position,
                CombatAssetProvider.FindAnchor(root.gameObject, "OpponentSpawn").position), Is.LessThan(.15f));
            Assert.That(CombatTestRoot.UseLegacyArenaForTests, Is.False);
            Assert.That(CombatTestRoot.AuthoredEnemyKindForTests, Is.Null);
            Assert.That(root.Flashlight, Is.Null, "This is the real earlier arena, not a warehouse with its plan hidden.");

            CombatActor[] prepared = root.GetComponentsInChildren<CombatActor>(true);
            Assert.That(prepared, Has.Length.EqualTo(7), "Six prepared targets share one live opponent slot beside the hero.");
            var targets = new Dictionary<CombatEnemyKind, CombatActor>();
            var graphs = new Dictionary<CombatActor, int>();
            foreach (CombatActor actor in prepared)
            {
                if (actor == root.Hero) continue;
                targets.Add(actor.EnemyKind, actor);
                graphs.Add(actor, actor.NpcAnimationGraphBuilds);
                Assert.That(actor.NpcAnimationGraphBuilds, Is.GreaterThan(0));
            }
            Assert.That(targets.Count, Is.EqualTo(kinds.Length));
            AssertLaboratorySlot(targets, graphs, CombatEnemyKind.LongExposure);
            yield return CaptureEndlessGameView("Test old arena - Laboratory controls Russian", "ru", true);
            yield return CaptureEndlessGameView("Test old arena - Laboratory controls English", "en", true);
            CaptureLaboratoryArena(root.Opponent, "Test old arena - Long exposure target");

            foreach (CombatEnemyKind kind in kinds)
            {
                TestContext.Progress.WriteLine("Laboratory target: " + kind);
                Assert.That(root.SpawnLaboratoryEnemy(kind), Is.True);
                AssertLaboratorySlot(targets, graphs, kind);
                CombatActor actor = root.Opponent;
                CombatAuthoredBodyBank bank = kind is CombatEnemyKind.Room or CombatEnemyKind.Imprint or CombatEnemyKind.LongExposure
                    ? AuthoredBodyBank(kind) : null;
                if (bank != null) AssertAuthoredBodyAppearance(actor, bank, true);
                if (kind == CombatEnemyKind.Crane)
                {
                    AssertLaboratorySuspension(actor);
                    CaptureLaboratoryArena(actor, "Test old arena - Suspended crane target");
                    Vector3 heldBody = actor.transform.position, heldCable = actor.GetCraneCableEnd(0);
                    Assert.That(root.PauseMenu.Open(), Is.True);
                    Assert.That(root.SpawnLaboratoryEnemy(CombatEnemyKind.Zombie), Is.False);
                    Assert.That(root.ReturnToWeapons(), Is.False);
                    root.Tick(1f);
                    yield return null;
                    Assert.That(actor.transform.position, Is.EqualTo(heldBody));
                    Assert.That(actor.GetCraneCableEnd(0), Is.EqualTo(heldCable));
                    AssertLaboratorySlot(targets, graphs, kind);
                    Assert.That(root.PauseMenu.Cancel(), Is.True);
                    yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause did not release laboratory input.");
                }
                ApplyKnownBodyContact(actor, root.Hero, BodyDamageRegion.LeftForearm, 0, .3f, 201);
                Assert.That(actor.BodyDamage.TissueLoss(BodyDamageRegion.LeftForearm, 0), Is.GreaterThan(0f));
                if (bank != null) AssertAuthoredBodyDerivativeSources(actor, bank);
                FireLaboratoryHeadProjectile(actor, bank, 202);
                Assert.That(actor.ReceivedImpactCount, Is.EqualTo(1), kind + ": the confirmed source head contact reaches the active body once.");
                Assert.That(actor.State.Health, Is.EqualTo(actor.State.Settings.MaxHealth * .5f).Within(.001f), kind.ToString());
                Assert.That(actor.State.IsDefeated || actor.IsRagdollActive, Is.False, kind + ": the first headshot leaves a live rig.");
                Assert.That(root.HeadEffects.DetachedSectorCountFor(actor), Is.Zero, kind + ": first-contact endurance retains the head surface.");
                Assert.That(root.BloodEffects.TryGetProjectilePresentation(actor, 0, out var firstWound), Is.True, kind.ToString());
                Assert.That(firstWound.HitCount, Is.EqualTo(1), kind.ToString());
                Assert.That(root.BloodEffects.TryGetProjectileWound(actor, 0, out _, out _), Is.True,
                    kind + ": the first shot binds a retained wound to its live source skin.");
                FireLaboratoryHeadProjectile(actor, bank, 203);
                Assert.That(actor.ReceivedImpactCount, Is.EqualTo(2), kind.ToString());
                Assert.That(actor.State.IsDefeated && actor.IsRagdollActive, Is.True, kind + ": the second confirmed head contact defeats this body.");
                Assert.That(root.HeadEffects.DetachedSectorCountFor(actor), Is.GreaterThan(0),
                    kind + ": terminal damage uses the prepared structural head bank.");
                Assert.That(root.BloodEffects.EmissionCount, Is.GreaterThan(0), kind.ToString());
                root.ResetRound();
                AssertLaboratorySlot(targets, graphs, kind);
                Assert.That(actor.ReceivedImpactCount, Is.Zero);
                Assert.That(actor.State.Health, Is.EqualTo(actor.State.Settings.MaxHealth));
                Assert.That(TotalBodyTissueLoss(actor), Is.Zero);
                Assert.That(root.HeadEffects.DetachedSectorCountFor(actor), Is.Zero);
                Assert.That(root.BloodEffects.ProjectileWoundCountFor(actor), Is.Zero);
                if (bank != null) AssertAuthoredBodyAppearance(actor, bank, true);
            }
            foreach (CombatWeaponId weapon in new[] { CombatWeaponId.Crowbar, CombatWeaponId.Shotgun, CombatWeaponId.Pistol })
            {
                Assert.That(root.LoadoutSelection.Contains(weapon), Is.True, weapon.ToString());
                Assert.That(root.Hero.CarriedWeapon(weapon), Is.Not.Null, weapon.ToString());
            }
            Assert.That(root.Hero.CarriedFirearm(CombatWeaponId.Pistol), Is.Not.Null);
            Assert.That(root.Hero.CarriedFirearm(CombatWeaponId.Shotgun), Is.Not.Null);
            Assert.That(root.Hero.WeaponId, Is.EqualTo(selected.Loadout.InitialHeld));
            Assert.That(root.Hero.Weapon, Is.SameAs(root.Hero.CarriedWeapon(selected.Loadout.InitialHeld)));
            Assert.That(root.Hero.HasWeaponInHand, Is.True, "Reset retains the selected hand weapon beside the complete carried kit.");
            Assert.That(root.SpawnLaboratoryEnemy(CombatEnemyKind.Room), Is.True);
            AssertLaboratorySlot(targets, graphs, CombatEnemyKind.Room);
            CombatLoadoutSelection returningKit = root.LoadoutSelection;
            CombatActor formerHero = root.Hero;
            Assert.That(root.ReturnToWeapons(), Is.True);
            Assert.That(root.ReturnToWeapons(), Is.False);
            yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneIds.MainMenu && !SceneTransitionService.IsTransitioning,
                "The laboratory did not return to its preparation card.");
            Assert.That(formerHero == null, Is.True);
            Assert.That(Object.FindAnyObjectByType<CombatActor>(), Is.Null);
            Assert.That(PauseMenuController.IsAnyPaused, Is.False);
            menu = Object.FindAnyObjectByType<StartMenuRoot>();
            Assert.That(menu.IsChoosingCombatWeapon, Is.True);
            Assert.That(menu.SelectedCombatSelection, Is.EqualTo(new CombatTestSelection(returningKit, CombatTestMode.Laboratory,
                selected.EncounterSettings, laboratoryEnemy: CombatEnemyKind.Room)));
            Assert.That(CombatTestStartService.TryConsumePreparationSelection(out _), Is.False,
                "The return card consumes the whole mode, kit and enemy selection once.");
            Assert.That(menu.ReturnToMainMenu(), Is.True);
            Assert.That(menu.SelectOption(StartMenuOption.CombatTest), Is.True);
            Assert.That(menu.ConfirmSelection(), Is.True);
            Assert.That(menu.SelectedCombatMode, Is.EqualTo(CombatTestMode.Standard));
            Assert.That(menu.SelectCombatOption(CombatPreparationOption.Start), Is.True);
            Assert.That(menu.ConfirmSelection(), Is.True);
            yield return WaitFor(() =>
            {
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                return root != null && root.IsInitialized && !SceneTransitionService.IsTransitioning && GameInput.CanRead(GameInputContext.Gameplay);
            }, "A normal warehouse launch inherited the old arena selection.");
            root.AutomaticSimulation = false;
            root.SetSparring(false);
            Assert.That(root.IsLaboratory, Is.False);
            Assert.That(root.Mode, Is.EqualTo(CombatTestMode.Standard));
            Assert.That(root.WarehousePlan, Is.Not.Null);
            Assert.That(root.SpawnLaboratoryEnemy(CombatEnemyKind.Imprint), Is.False);
            Assert.That(root.ReturnToWeapons(), Is.True);
            yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneIds.MainMenu && !SceneTransitionService.IsTransitioning,
                "The normal warehouse did not clean up after the laboratory round trip.");
            Assert.That(Object.FindAnyObjectByType<CombatActor>(), Is.Null);
            Assert.That(PauseMenuController.IsAnyPaused, Is.False);
            Assert.That(CombatTestStartService.TryConsumePreparationSelection(out _), Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        private IEnumerator WaitForLaboratoryRoot()
        {
            yield return WaitFor(() =>
            {
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                return root != null && root.IsInitialized && !SceneTransitionService.IsTransitioning && GameInput.CanRead(GameInputContext.Gameplay);
            }, "The old arena launch did not reach live gameplay.");
            root.AutomaticSimulation = false;
            root.SetSparring(false);
            Assert.That(root.IsLaboratory, Is.True);
        }

        private void FireLaboratoryHeadProjectile(CombatActor actor, CombatAuthoredBodyBank bank, int sequence)
        {
            if (bank == null)
            {
                FireMixedEnemyProjectile(actor, Player3DAnatomicalPart.Head);
                return;
            }
            CombatHurtboxes.Hit hit = AuthoredSurfaceContact(actor, bank, "GEO_Head", BodyDamageRegion.Head);
            actor.ReceiveProjectile(root.Hero, sequence, hit, -hit.Normal * CombatProjectilePool.MuzzleSpeed);
        }

        private void AssertLaboratorySlot(Dictionary<CombatEnemyKind, CombatActor> targets,
            Dictionary<CombatActor, int> graphs, CombatEnemyKind expected)
        {
            Assert.That(root.LaboratoryEnemy, Is.EqualTo(expected));
            Assert.That(root.Opponents, Has.Count.EqualTo(1));
            Assert.That(root.Opponent, Is.SameAs(targets[expected]));
            Assert.That(root.Opponents[0], Is.SameAs(root.Opponent));
            Assert.That(root.GetComponentsInChildren<CombatActor>(true), Has.Length.EqualTo(targets.Count + 1));
            int active = 0;
            foreach (var pair in targets)
            {
                CombatActor actor = pair.Value;
                Assert.That(actor.gameObject.activeInHierarchy, Is.EqualTo(pair.Key == expected));
                Assert.That(actor.NpcAnimationGraphBuilds, Is.EqualTo(graphs[actor]), "Switching and reset reuse the prepared rig graph.");
                if (actor.gameObject.activeInHierarchy) active++;
                if (actor.IsCrane) Assert.That(actor.GetCraneHookTransform().gameObject.activeInHierarchy, Is.EqualTo(pair.Key == expected));
            }
            Assert.That(active, Is.EqualTo(1));
        }

        private static void AssertLaboratorySuspension(CombatActor crane)
        {
            Assert.That(crane.CraneAttached, Is.True);
            Assert.That(crane.CraneCableCount, Is.EqualTo(CombatCraneAssetProvider.SuspensionBoneNames.Length));
            for (int cable = 0; cable < crane.CraneCableCount; cable++)
            {
                Vector3 start = crane.GetCraneCableStart(cable), end = crane.GetCraneCableEnd(cable);
                Assert.That(start.y, Is.GreaterThan(end.y));
                Transform bone = FindAnatomicalBone(crane, CombatCraneAssetProvider.SuspensionBoneNames[cable]);
                Vector3 anchor = bone.position + bone.rotation * CombatCraneAssetProvider.SuspensionLocalOffsets[cable];
                Assert.That(Vector3.Distance(end, anchor), Is.LessThan(.025f));
                for (int segment = 0; segment < CombatActor.CraneCableSegments; segment++)
                {
                    Transform geometry = crane.GetCraneCableTransform(cable, segment);
                    Assert.That(geometry.gameObject.activeInHierarchy, Is.True);
                    AssertCraneCableMesh(geometry, crane.GetCraneCableSegmentStart(cable, segment), crane.GetCraneCableSegmentEnd(cable, segment));
                }
            }
        }

        private void CaptureLaboratoryArena(CombatActor actor, string name)
        {
            Camera camera = root.CameraFollow.Camera;
            bool following = root.CameraFollow.enabled;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            try
            {
                root.CameraFollow.enabled = false;
                Vector3 focus = actor.IsCrane ? (actor.CraneAnchorPosition + actor.transform.position) * .5f : actor.transform.position + Vector3.up;
                Vector3 eye = focus + (actor.IsCrane ? new Vector3(7f, 2f, 8f) : new Vector3(3.5f, 1.6f, 4.5f));
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
                camera.fieldOfView = 60f;
                CaptureMixedCameraFrame(camera, name);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fov;
                root.CameraFollow.enabled = following;
            }
        }
    }
}
