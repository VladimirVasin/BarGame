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
        [Explicit("The warehouse fish puzzle, physical pieces, retained progress and shared key receipt. Run without -batchmode for UI frames.")]
        public IEnumerator CombatWarehouseFishAssembly()
        {
            CombatTestRoot root = null;
            var input = new InputTestFixture();
            input.Setup();
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            GameSessionState.BeginNewGame();
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                var pacing = new CombatEncounterSettings(2, 60f, 60f, 60f, 60f);
                Assert.That(CombatTestStartService.TryStart(new CombatTestSelection(
                    CombatLoadoutSelection.FromLegacyWeapon(CombatWeaponId.Crowbar), CombatTestMode.Endless, pacing)), Is.True);
                for (int frame = 0; frame < 600; frame++)
                {
                    root = Object.FindAnyObjectByType<CombatTestRoot>();
                    if (root != null && root.IsInitialized)
                    {
                        root.AutomaticSimulation = false;
                        if (!SceneTransitionService.IsTransitioning) break;
                    }
                    yield return null;
                }
                Assert.That(root, Is.Not.Null);
                Assert.That(SceneTransitionService.IsTransitioning, Is.False);
                root.AutomaticSimulation = false;
                root.SetSparring(false);
                root.SetDuelLogging(false);
                for (int frame = 0; frame < SettleFrames; frame++) yield return null;
                Assert.That(root.Mode, Is.EqualTo(CombatTestMode.Endless));
                Assert.That(root.AdvanceEndlessInterval(root.NextOpponentSeconds + .01f), Is.True);
                Assert.That(root.LiveOpponentCount, Is.EqualTo(1));
                CombatActor survivor = root.Opponents[0];
                Assert.That(survivor.gameObject.activeInHierarchy, Is.True);
                Assert.That(root.WaitingForNextOpponent, Is.True);
                Assert.That(root.NextOpponentSeconds, Is.GreaterThan(0f));

                WarehouseFishAssembly table = root.GetComponentInChildren<WarehouseFishAssembly>();
                Assert.That(table, Is.Not.Null);
                Assert.That(LocalizationService.Get(table.Interaction.PromptKey), Does.StartWith("E — "));
                Camera camera = root.CameraFollow.Camera;
                var state = GameSessionState.WarehouseFishAssembly;
                var cans = new[] { InventoryItemId.RottenFishTailCan, InventoryItemId.RottenFishBellyCan,
                    InventoryItemId.RottenFishHeadCan };
                yield return VerifyWarehousePendantBeforeKey(root);

                // Seed only the existing refrigerator transaction. The table,
                // modal lifecycle, physical drops and receipt are the real flow.
                MoveToFishAssembly(root, table);
                Assert.That(table.BeginInteraction(), Is.False);
                for (int part = 0; part < 2; part++)
                    Assert.That(GameSessionState.TryCollectWorldItem(root.WarehousePlan.Refrigerators[part].SourceId,
                        cans[part]), Is.True);
                Assert.That(table.BeginInteraction(), Is.False, "Two cans must not start or debit a partial puzzle.");
                Assert.That(state.Phase, Is.EqualTo(WarehouseFishAssemblyPhase.WaitingForCans));
                for (int part = 0; part < 2; part++)
                    Assert.That(GameSessionState.GetInventoryItemCount(cans[part]), Is.EqualTo(1));
                Assert.That(root.EndlessSpawnsBlockedByFish, Is.False);
                Assert.That(root.WaitingForNextOpponent, Is.True);
                Assert.That(GameSessionState.TryCollectWorldItem(root.WarehousePlan.Refrigerators[2].SourceId,
                    cans[2]), Is.True);
                AssertFishAssemblySpawnCutoff(root, 1);
                Assert.That(survivor.gameObject.activeInHierarchy && !survivor.State.IsDefeated, Is.True,
                    "Collecting the final can stops arrivals while keeping the existing live opponent.");
                Assert.That(root.AdvanceEndlessInterval(12f), Is.False);
                AssertFishAssemblySpawnCutoff(root, 1);
                Assert.That(root.Opponents[0], Is.SameAs(survivor));
                yield return EnterFishAssembly(root, table, input, keyboard);
                Assert.That(root.GetComponentInChildren<WarehouseFishLamp>().Phase, Is.EqualTo(WarehouseFishLampPhase.Waiting));
                foreach (InventoryItemId can in cans)
                    Assert.That(GameSessionState.GetInventoryItemCount(can), Is.Zero);
                Assert.That(state.HasTransferredCans, Is.True);
                Assert.That(table.BeginInteraction(), Is.False, "A live table cannot transfer the cans again.");
                Assert.That(root.Player.Motor.InputEnabled || root.Player.Interactor.InputEnabled, Is.False);
                Assert.That(root.CameraFollow.FixedPoseActive, Is.True);
                table.Advance(.8f);
                yield return null;
                AssertFishAssemblyCans(table, true);
                AssertFishAssemblyFraming(root, table);
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse - Fish poured from cans");
                table.Advance(WarehouseFishAssemblyState.PourSeconds);
                Assert.That(table.Phase, Is.EqualTo(WarehouseFishAssemblyPhase.Assembling));
                AssertFishAssemblyCans(table, false);
                float headReleaseHeight = table.GetPartBody(WarehouseFishPart.Head).position.y;
                for (int frame = 0; frame < 35; frame++)
                {
                    yield return new WaitForFixedUpdate();
                    AssertFishAssemblyFraming(root, table);
                }
                Assert.That(table.GetPartBody(WarehouseFishPart.Head).position.y,
                    Is.LessThan(headReleaseHeight - .15f), "Gravity must carry the released part onto the play surface.");
                foreach (WarehouseFishPart part in Enum.GetValues(typeof(WarehouseFishPart)))
                {
                    Rigidbody body = table.GetPartBody(part);
                    Assert.That(body, Is.Not.Null);
                    Assert.That(body.isKinematic, Is.False, "An unselected loose part must retain real physics.");
                    Assert.That(body.useGravity, Is.True);
                    Assert.That(Vector3.Dot(body.transform.up, Vector3.up), Is.GreaterThan(.97f),
                        "Each fish section lands on its flat authored side.");
                    Assert.That(body.GetComponentInChildren<Collider>().bounds.min.y,
                        Is.GreaterThan(root.WarehousePlan.FishStationPosition.y + .45f),
                        "A fish section must rest on the table, not fall through its basin.");
                }
                AssertFishLampPowered(root.GetComponentInChildren<WarehouseFishLamp>(), true);
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse - Three physical fish sections");
                yield return CaptureFishUi("Test warehouse - Fish assembly controls");

                // A real pointer press selects an authored collider; method
                // calls then isolate the seam distance and orientation rules.
                Rigidbody tail = table.GetPartBody(WarehouseFishPart.Tail);
                Vector3 tailScreen = camera.WorldToScreenPoint(tail.GetComponentInChildren<Collider>().bounds.center);
                Assert.That(tailScreen.z, Is.GreaterThan(0f));
                input.Set(mouse.position, new Vector2(tailScreen.x, tailScreen.y));
                input.Press(mouse.leftButton, queueEventOnly: true);
                yield return null;
                Assert.That(table.SelectedPartIndex, Is.EqualTo((int)WarehouseFishPart.Tail));
                Assert.That(table.MoveSelected(new Vector2(-.35f, -.15f), 90f), Is.True,
                    "The pointer must select the real fish piece.");
                input.Release(mouse.leftButton, queueEventOnly: true);
                yield return null;
                table.DropSelected();
                Assert.That(state.LinkedMask, Is.Zero, "A misplaced and rotated piece cannot join.");

                Assert.That(table.SelectPart(WarehouseFishPart.Belly), Is.True);
                Assert.That(table.MoveSelected(Vector2.zero, 0f), Is.True);
                table.DropSelected();
                Assert.That(table.SelectPart(WarehouseFishPart.Tail), Is.True);
                Assert.That(table.MoveSelected(new Vector2(-WarehouseFishAssemblyState.PartSpacing, 0f), 0f), Is.True);
                table.DropSelected();
                Assert.That(state.IsPartLinked(WarehouseFishPart.Tail), Is.True);
                Assert.That(state.IsPartLinked(WarehouseFishPart.Belly), Is.True);
                Assert.That(state.IsPartLinked(WarehouseFishPart.Head), Is.False);
                Assert.That(table.SelectPart(WarehouseFishPart.Head), Is.True);
                Assert.That(table.MoveSelected(new Vector2(WarehouseFishAssemblyState.PartSpacing, 0f), 180f), Is.True);
                table.DropSelected();
                Assert.That(state.Phase, Is.EqualTo(WarehouseFishAssemblyPhase.Assembling),
                    "A matching location with a reversed cut must not complete the fish.");
                Assert.That(state.LinkedMask, Is.EqualTo(1));
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse - Fish seam rejects reversed head");

                WarehouseFishPartPose retainedTail = state.GetPose(WarehouseFishPart.Tail);
                WarehouseFishPartPose retainedBelly = state.GetPose(WarehouseFishPart.Belly);
                input.Press(keyboard.escapeKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.escapeKey, queueEventOnly: true);
                yield return WaitForFishAssemblyExit(root);
                AssertFishAssemblyReleased(root);
                Assert.That(state.LinkedMask, Is.EqualTo(1));
                AssertFishAssemblyCans(table, false);
                yield return EnterFishAssembly(root, table, input, keyboard);
                AssertFishAssemblyCans(table, false);
                table.enabled = false;
                yield return null;
                AssertFishAssemblyReleased(root);
                Assert.That(state.LinkedMask, Is.EqualTo(1), "Disabling the source retains its joined parts.");
                table.enabled = true;
                root.ResetRound();
                AssertFishAssemblySpawnCutoff(root, 0);
                Assert.That(root.AdvanceEndlessInterval(12f), Is.False);
                AssertFishAssemblySpawnCutoff(root, 0);
                Assert.That(GameSessionState.WarehouseFishAssembly, Is.SameAs(state));
                Assert.That(state.LinkedMask, Is.EqualTo(1));
                AudioSource[] oldTableSources = table.GetComponentsInChildren<AudioSource>(true);
                Assert.That(root.NewWarehouseLayout(5), Is.True);
                yield return null;
                Assert.That(table == null, Is.True, "Replacing a warehouse destroys the old table owner.");
                foreach (AudioSource source in oldTableSources) Assert.That(source == null, Is.True);
                table = root.GetComponentInChildren<WarehouseFishAssembly>();
                Assert.That(table, Is.Not.Null);
                Assert.That(table.Phase, Is.EqualTo(WarehouseFishAssemblyPhase.Assembling));
                AssertFishAssemblyCans(table, false);
                AssertFishAssemblySpawnCutoff(root, 0);
                Assert.That(root.AdvanceEndlessInterval(12f), Is.False);
                AssertFishAssemblySpawnCutoff(root, 0);
                Assert.That(state.LinkedMask, Is.EqualTo(1));
                AssertFishAssemblyPose(state.GetPose(WarehouseFishPart.Tail), retainedTail);
                AssertFishAssemblyPose(state.GetPose(WarehouseFishPart.Belly), retainedBelly);
                foreach (InventoryItemId can in cans)
                    Assert.That(GameSessionState.GetInventoryItemCount(can), Is.Zero);
                AssertFishLampPowered(root.GetComponentInChildren<WarehouseFishLamp>(), true);

                yield return EnterFishAssembly(root, table, input, keyboard);
                AssertFishAssemblyCans(table, false);
                Assert.That(state.LinkedMask, Is.EqualTo(1), "Re-entry resumes the existing two-piece group.");
                Assert.That(table.SelectPart(WarehouseFishPart.Head), Is.True);
                Assert.That(table.MoveSelected(new Vector2(WarehouseFishAssemblyState.PartSpacing, 0f), 0f), Is.True);
                table.DropSelected();
                Assert.That(table.Phase, Is.EqualTo(WarehouseFishAssemblyPhase.Convulsing));
                Assert.That(state.IsAssembled, Is.True);
                AssertFishAssemblyCans(table, false);
                SkinnedMeshRenderer fishSkin = table.AssembledFish.GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.That(fishSkin, Is.Not.Null, "The joined fish must use its continuous authored skin.");
                Transform[] fishBones = fishSkin.bones;
                var restRotations = new Quaternion[fishBones.Length];
                for (int bone = 0; bone < fishBones.Length; bone++) restRotations[bone] = fishBones[bone].localRotation;
                table.Advance(1.6f);
                yield return null;
                float largestBoneMotion = 0f;
                for (int bone = 0; bone < fishBones.Length; bone++)
                    largestBoneMotion = Mathf.Max(largestBoneMotion, Quaternion.Angle(restRotations[bone], fishBones[bone].localRotation));
                Assert.That(largestBoneMotion, Is.GreaterThan(8f), "The convulsion must drive the imported rig, not just its state clock.");
                AssertFishAssemblyFraming(root, table);
                AssertFishAssemblyCans(table, false);
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse - Reassembled fish convulsions");
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    float elapsed = state.FinaleElapsed;
                    Vector3 pausedCamera = camera.transform.position;
                    table.Advance(30f);
                    for (int frame = 0; frame < 3; frame++) yield return null;
                    Assert.That(state.FinaleElapsed, Is.EqualTo(elapsed));
                    Assert.That(camera.transform.position, Is.EqualTo(pausedCamera));
                    foreach (AudioSource source in table.GetComponentsInChildren<AudioSource>())
                        Assert.That(source.isPlaying, Is.False, "Fish sounds must pause with its motion.");
                }
                for (int step = 0; step < 100 && table.Phase == WarehouseFishAssemblyPhase.Convulsing; step++)
                    table.Advance(.05f);
                Assert.That(table.Phase, Is.EqualTo(WarehouseFishAssemblyPhase.Spitting));
                table.Advance(.35f);
                yield return null;
                AssertFishAssemblyFraming(root, table);
                AssertFishAssemblyCans(table, false);
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse - Dying fish spits the key");
                table.Advance(WarehouseFishAssemblyState.SpitSeconds);
                Assert.That(state.Phase, Is.EqualTo(WarehouseFishAssemblyPhase.Complete));

                WorldItemFoundScreen receipt = WorldItemFoundScreen.For(root.Player.Interactor);
                for (int frame = 0; frame < 300 && !receipt.IsPresenting; frame++) yield return null;
                Assert.That(receipt.IsPresenting, Is.True, "The completed table must hand off to the shared receipt.");
                Assert.That(state.RewardGranted, Is.True);
                Assert.That(GameSessionState.IsWorldItemCollected(GameSessionState.WarehouseFishKeySourceId), Is.True);
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.WarehouseGateKey), Is.EqualTo(1));
                Assert.That(receipt.ActiveItemId, Is.EqualTo(InventoryItemId.WarehouseGateKey));
                Assert.That(receipt.ActionKey, Is.EqualTo(WorldItemFoundScreen.CloseActionKey));
                Assert.That(LocalizationService.Get(receipt.ActiveDefinition.NameLocalizationKey), Is.Not.Empty);
                Assert.That(LocalizationService.Get(receipt.ActiveDefinition.DescriptionLocalizationKey), Is.Not.Empty);
                yield return WaitForScarfFound(receipt, screen => screen.IsShowing);
                Assert.That(root.GetComponentInChildren<WarehouseFishLamp>().Phase, Is.EqualTo(WarehouseFishLampPhase.Waiting),
                    "Receiving the key at the table does not heat the lamp while its receipt still owns input.");
                Assert.That(root.GetComponentInChildren<WarehouseFishLamp>().HeatElapsed, Is.Zero);
                yield return CaptureFishUi("Test warehouse - Fish key received");
                input.Press(keyboard.eKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.eKey, queueEventOnly: true);
                yield return WaitForScarfFound(receipt, screen => !screen.IsPresenting);
                yield return null;
                AssertFishAssemblyReleased(root);
                Assert.That(state.ReceiptPending, Is.False);
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.WarehouseGateKey), Is.EqualTo(1));
                Assert.That(root.PauseMenu.IsOpen, Is.False, "The close key must not leak into another world owner.");
                yield return VerifyWarehousePendantFailure(root);
                yield return VerifyWarehouseFishGate(root, input, keyboard);
                Assert.That(root.NewWarehouseLayout(7), Is.True);
                yield return null;
                Assert.That(root.GetComponentInChildren<WarehouseFishAssembly>().Phase,
                    Is.EqualTo(WarehouseFishAssemblyPhase.Complete));
                AssertFishAssemblyCans(root.GetComponentInChildren<WarehouseFishAssembly>(), false);
                AssertFishAssemblySpawnCutoff(root, 0);
                WarehouseGateController restoredGate = root.GetComponentInChildren<WarehouseGateController>();
                Assert.That(restoredGate.IsUnlocked && restoredGate.IsOpen, Is.True);
                Assert.That(restoredGate.Progress, Is.EqualTo(1f));
                Assert.That(restoredGate.ChainReleased, Is.True);
                WarehouseFishLamp restoredLamp = root.GetComponentInChildren<WarehouseFishLamp>();
                Assert.That(restoredLamp.Phase, Is.EqualTo(WarehouseFishLampPhase.Burst));
                Assert.That(restoredLamp.HeatingPlayCount + restoredLamp.BurstPlayCount, Is.Zero,
                    "Rebuilding the warehouse restores the burnt bulb without replaying its failure.");
                AssertWarehousePendantBlackout(root);
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.WarehouseGateKey), Is.EqualTo(1));
                Assert.That(receipt.IsPresenting, Is.False, "A new layout cannot replay an acknowledged receipt.");
            }
            finally
            {
                if (root != null)
                {
                    NarrativeInteractionController.For(root.Player.Interactor).RestoreImmediate();
                    WorldItemFoundScreen.For(root.Player.Interactor)?.Abandon();
                }
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = null;
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                input.TearDown();
            }
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
            GameSessionState.BeginNewGame();
            Assert.That(GameSessionState.WarehouseFishAssembly.Phase,
                Is.EqualTo(WarehouseFishAssemblyPhase.WaitingForCans));
            Assert.That(GameSessionState.WarehouseFishAssembly.LinkedMask, Is.Zero);
            Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.WarehouseGateKey), Is.Zero);
            Assert.That(GameSessionState.WarehouseGate.Unlocked || GameSessionState.WarehouseGate.IsOpen, Is.False);
            Assert.That(GameSessionState.WarehouseGate.Progress, Is.Zero);
            Assert.That(GameSessionState.WarehouseFishLamp.Phase, Is.EqualTo(WarehouseFishLampPhase.Waiting));
        }

        private static IEnumerator VerifyWarehousePendantBeforeKey(CombatTestRoot root)
        {
            WarehouseFishLamp lamp = root.GetComponentInChildren<WarehouseFishLamp>();
            WarehouseArenaStair stair = WarehousePendantUpperStair(root, false);
            PlaceHeroAtWarehousePendantStair(root, stair);
            Vector3 target = stair.Top + Vector3.back * .9f;
            for (int frame = 0; frame < 40 && root.Hero.transform.position.z > target.z; frame++)
            {
                root.Hero.ApplyMotorConstraint();
                root.Player.Motor.ApplyOwnedImpulseDisplacement(root.Hero, Vector3.back * .05f);
                yield return null;
                lamp.Advance(.05f);
            }
            Assert.That(root.Hero.transform.position.y,
                Is.LessThan(WarehouseArenaPlan.TopHeight + PlayerFactory.GroundedRootOffset - .07f),
                "The prerequisite check must use a real capsule descending an upper stair.");
            Assert.That(lamp.Phase, Is.EqualTo(WarehouseFishLampPhase.Waiting), "Descending before the fish key cannot heat the lamp.");
            Assert.That(lamp.HeatingPlayCount + lamp.BurstPlayCount, Is.Zero);
        }

        private static IEnumerator VerifyWarehousePendantFailure(CombatTestRoot root)
        {
            WarehouseFishLamp lamp = root.GetComponentInChildren<WarehouseFishLamp>();
            Assert.That(lamp.Phase, Is.EqualTo(WarehouseFishLampPhase.Waiting));
            WarehouseArenaStair stair = WarehousePendantUpperStair(root, true);
            PlaceHeroAtWarehousePendantStair(root, stair);
            for (int frame = 0; frame < 4; frame++)
            {
                root.Hero.ApplyMotorConstraint();
                root.Player.Motor.ApplyOwnedImpulseDisplacement(root.Hero, Vector3.back * .05f);
                yield return null;
                lamp.Advance(.05f);
            }
            Assert.That(root.Hero.transform.position.y,
                Is.EqualTo(WarehouseArenaPlan.TopHeight + PlayerFactory.GroundedRootOffset).Within(.04f));
            Assert.That(Vector3.Distance(root.Hero.transform.position, root.WarehousePlan.FishStationPosition), Is.GreaterThan(6.5f));
            Assert.That(lamp.Phase, Is.EqualTo(WarehouseFishLampPhase.Waiting),
                "Distance from the table on a flat gallery does not count as beginning a stair descent.");
            AdvanceFishPendantToFactor(lamp, HomeBathroomLightFlicker.MaximumFactor);
            float normalIntensity = lamp.Light.intensity, normalRange = lamp.Light.range;
            AdvanceFishPendantToFactor(lamp, HomeBathroomLightFlicker.MinimumFactor);
            Assert.That(lamp.Light.intensity, Is.LessThan(normalIntensity), "The top gallery keeps the ordinary flicker after receiving the key.");

            for (int frame = 0; frame < 40 && lamp.Phase == WarehouseFishLampPhase.Waiting; frame++)
            {
                root.Hero.ApplyMotorConstraint();
                root.Player.Motor.ApplyOwnedImpulseDisplacement(root.Hero, Vector3.back * .05f);
                yield return null;
                lamp.Advance(0f);
            }
            Assert.That(lamp.Phase, Is.EqualTo(WarehouseFishLampPhase.Heating),
                "The first real upper-stair descent after the key begins the retained lamp failure.");
            Assert.That(root.Hero.transform.position.y,
                Is.LessThan(WarehouseArenaPlan.TopHeight + PlayerFactory.GroundedRootOffset - .07f));
            Assert.That(lamp.HeatingPlayCount, Is.EqualTo(1));
            Assert.That(lamp.CueSource.clip, Is.Not.Null);
            Assert.That(lamp.CueSource.spatialBlend, Is.EqualTo(1f));
            Assert.That(lamp.CueSource.maxDistance,
                Is.GreaterThan(Vector3.Distance(lamp.CueSource.transform.position, root.Hero.transform.position)),
                "The escalating electrical cue must reach the stair where the hero triggers it.");
            lamp.Advance(.35f);
            Renderer bulb = FishPendantRenderer(lamp.gameObject, "LampBulb__");
            var properties = new MaterialPropertyBlock();
            bulb.GetPropertyBlock(properties);
            Color warmEmission = properties.GetColor("_EmissionColor");
            Assert.That(lamp.Light.intensity, Is.GreaterThan(normalIntensity));
            Assert.That(warmEmission.maxColorComponent, Is.GreaterThan(1.4f));
            CaptureWarehousePendantFailure(root, "Test warehouse - Pendant heating on stair descent");
            using (GameTimeScaleRuntime.AcquirePause())
            {
                float elapsed = lamp.HeatElapsed, intensity = lamp.Light.intensity;
                lamp.Advance(30f);
                for (int frame = 0; frame < 3; frame++) yield return null;
                Assert.That(lamp.Phase, Is.EqualTo(WarehouseFishLampPhase.Heating));
                Assert.That(lamp.HeatElapsed, Is.EqualTo(elapsed));
                Assert.That(lamp.Light.intensity, Is.EqualTo(intensity));
                bulb.GetPropertyBlock(properties);
                Assert.That(properties.GetColor("_EmissionColor"), Is.EqualTo(warmEmission));
                Assert.That(lamp.CueSource.isPlaying, Is.False);
            }
            lamp.Advance(Mathf.Max(0f, WarehouseFishLamp.HeatingSeconds * .96f - lamp.HeatElapsed));
            Assert.That(lamp.Phase, Is.EqualTo(WarehouseFishLampPhase.Heating));
            Assert.That(lamp.CurrentFactor, Is.GreaterThanOrEqualTo(6f));
            Assert.That(lamp.Light.intensity, Is.GreaterThan(normalIntensity * 6f));
            Assert.That(lamp.Light.range, Is.GreaterThan(normalRange));
            bulb.GetPropertyBlock(properties);
            Assert.That(properties.GetColor("_EmissionColor").maxColorComponent, Is.GreaterThan(10f));
            CaptureWarehousePendantFailure(root, "Test warehouse - Pendant peak heat");
            lamp.Advance(WarehouseFishLamp.HeatingSeconds);
            Assert.That(lamp.Phase, Is.EqualTo(WarehouseFishLampPhase.Burst));
            Assert.That(lamp.BurstPlayCount, Is.EqualTo(1));
            Assert.That(bulb.enabled && !bulb.forceRenderingOff && bulb.gameObject.activeInHierarchy, Is.False);
            Assert.That(lamp.Light.enabled, Is.False);
            Assert.That(lamp.ShardBodies.Count, Is.EqualTo(8));
            var releaseHeights = new float[lamp.ShardBodies.Count];
            for (int index = 0; index < lamp.ShardBodies.Count; index++)
            {
                Rigidbody body = lamp.ShardBodies[index];
                Assert.That(body.gameObject.activeInHierarchy && !body.isKinematic && body.useGravity, Is.True);
                Assert.That(body.GetComponentsInChildren<Collider>(), Is.Not.Empty);
                releaseHeights[index] = body.position.y;
            }
            AssertWarehousePendantBlackout(root);
            StageWarehousePendantTorch(root);
            for (int frame = 0; frame < 12; frame++) yield return new WaitForFixedUpdate();
            float largestFall = 0f;
            for (int index = 0; index < lamp.ShardBodies.Count; index++)
                largestFall = Mathf.Max(largestFall, releaseHeights[index] - lamp.ShardBodies[index].position.y);
            Assert.That(largestFall, Is.GreaterThan(.08f), "The bulb fragments fall under actual gravity after the burst.");
            CaptureWarehousePendantFailure(root, "Test warehouse - Burst bulb and falling glass");
            for (int frame = 0; frame < 65; frame++) yield return new WaitForFixedUpdate();
            bool onWarehouseFloor = false;
            foreach (Rigidbody body in lamp.ShardBodies)
            {
                float bottom = body.GetComponentInChildren<Collider>().bounds.min.y;
                Assert.That(bottom, Is.GreaterThan(-.1f), "The warehouse floor must catch the physical fragments.");
                if (root.WarehouseArea.TryGetHeight(body.position, out float floor))
                    onWarehouseFloor |= bottom > floor - .04f && bottom < floor + .2f;
            }
            Assert.That(onWarehouseFloor, Is.True, "The burst must scatter fragments onto an actual warehouse floor.");
            lamp.Advance(20f);
            root.ResetRound();
            Assert.That(lamp.Phase, Is.EqualTo(WarehouseFishLampPhase.Burst));
            Assert.That(lamp.HeatingPlayCount, Is.EqualTo(1));
            Assert.That(lamp.BurstPlayCount, Is.EqualTo(1));
            AssertWarehousePendantBlackout(root);
        }

        private static WarehouseArenaStair WarehousePendantUpperStair(CombatTestRoot root, bool right)
        {
            foreach (WarehouseArenaStair stair in root.WarehousePlan.Stairs)
                if (Mathf.Abs(stair.Top.y - WarehouseArenaPlan.TopHeight) < .01f && (stair.Top.x > 0f) == right) return stair;
            throw new InvalidOperationException("The warehouse needs both authored upper-stair descents.");
        }

        private static void PlaceHeroAtWarehousePendantStair(CombatTestRoot root, WarehouseArenaStair stair)
        {
            root.Hero.ResetActor(stair.Top + Vector3.forward * .6f + Vector3.up * PlayerFactory.GroundedRootOffset, Vector3.back);
            root.Hero.SetCombatFocused(true);
            root.Hero.ApplyMotorConstraint();
            Physics.SyncTransforms();
            root.CameraFollow.ClearFixedPose();
            root.CameraFollow.Snap();
        }

        private static void AssertWarehousePendantBlackout(CombatTestRoot root)
        {
            Assert.That(root.Flashlight.SpotLight.enabled && root.Flashlight.SpotLight.intensity > 0f, Is.True);
            foreach (Light light in root.GetComponentsInChildren<Light>(true))
                if (!light.transform.IsChildOf(root.Hero.transform))
                    Assert.That(light.enabled && light.intensity > 0f, Is.False, "A burst leaves the chest flashlight as the only warehouse light: " + light.name);
            int emissionId = Shader.PropertyToID("_EmissionColor");
            var properties = new MaterialPropertyBlock();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy ||
                    renderer.transform.IsChildOf(root.Hero.transform) ||
                    !(renderer.name.EndsWith("__Lamp", StringComparison.Ordinal) ||
                      renderer.name.EndsWith("__SignalRed", StringComparison.Ordinal) ||
                      renderer.name.EndsWith("__SignalRedOff", StringComparison.Ordinal))) continue;
                renderer.GetPropertyBlock(properties);
                Color emission = properties.HasProperty(emissionId) ? properties.GetColor(emissionId) :
                    renderer.sharedMaterial.GetColor(emissionId);
                Assert.That(emission.maxColorComponent, Is.LessThan(.001f), "A fixed warehouse lens must no longer glow: " + renderer.name);
            }
            Assert.That(RenderSettings.ambientIntensity, Is.Zero);
        }

        private static void CaptureWarehousePendantFailure(CombatTestRoot root, string name)
        {
            Camera camera = root.CameraFollow.Camera;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            try
            {
                Vector3 eye = root.WarehousePlan.FishStationPosition + new Vector3(2.4f, 1.5f, -3.5f);
                camera.transform.SetPositionAndRotation(eye,
                    Quaternion.LookRotation(root.WarehousePlan.FishStationPosition + Vector3.up * 1.1f - eye));
                camera.fieldOfView = 58f;
                CaptureCurrentCamera(camera, SceneIds.CombatTest, name);
            }
            finally { camera.transform.SetPositionAndRotation(position, rotation); camera.fieldOfView = fov; }
        }

        private static void StageWarehousePendantTorch(CombatTestRoot root)
        {
            Vector3 foot = root.WarehousePlan.FishStationPosition + new Vector3(2.4f, 0f, -.8f);
            Vector3 target = root.WarehousePlan.FishStationPosition + Vector3.up * 1.15f;
            Vector3 facing = target - foot; facing.y = 0f;
            Assert.That(root.WarehouseArea.TryGetHeight(foot, out float height, .32f), Is.True);
            foot.y = height;
            root.Hero.ResetActor(foot + Vector3.up * PlayerFactory.GroundedRootOffset, facing.normalized);
            root.Hero.SetCombatFocused(false);
            root.Hero.ApplyMotorConstraint();
            Physics.SyncTransforms();
            var visual = (Player3DCharacterPresentation)root.Player.Visual;
            visual.SetTorsoLookIntent(root.Hero, target - (root.Hero.transform.position + Vector3.up * 1.4f), 0f, false);
            visual.AdvanceTorsoLook(root.Hero, .5f);
            visual.ReapplyLatePresentationPose();
            WarehouseArenaLighting.UpdateHeroVisibility(root.Flashlight.transform);
        }

        private static IEnumerator VerifyWarehouseFishGate(CombatTestRoot root, InputTestFixture input, Keyboard keyboard)
        {
            WarehouseGateController gate = root.GetComponentInChildren<WarehouseGateController>();
            Assert.That(gate, Is.Not.Null);
            Assert.That(gate.IsUnlocked || gate.IsOpen || gate.ChainReleased, Is.False);
            Assert.That(gate.Progress, Is.Zero);
            MoveToWarehouseFishTarget(root, gate.DoorInteraction);
            for (int frame = 0; frame < 3; frame++) yield return null;
            Assert.That(gate.DoorInteraction.CanInteract(root.Player.Interactor), Is.False);
            Vector3 doorway = root.WarehousePlan.GoalDoorPosition - Vector3.forward * .25f;
            Assert.That(root.WarehouseArea.CanTraverse(root.WarehousePlan.GateApproach, doorway, .32f), Is.False);

            // Cancel before the physical release marker. The retained key and
            // locked state must survive an unfinished unlock animation.
            yield return EnterWarehouseFishGate(root, gate, input, keyboard, "Test valve - First unlock");
            gate.Advance(.15f);
            input.Release(keyboard.eKey, queueEventOnly: true);
            yield return null;
            input.Press(keyboard.escapeKey, queueEventOnly: true);
            yield return null;
            input.Release(keyboard.escapeKey, queueEventOnly: true);
            yield return WaitForFishAssemblyExit(root);
            AssertFishAssemblyReleased(root);
            Assert.That(gate.IsUnlocked || gate.ChainReleased, Is.False);
            Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.WarehouseGateKey), Is.EqualTo(1));

            yield return EnterWarehouseFishGate(root, gate, input, keyboard, "Test valve - Unlock after cancellation");
            gate.Advance(WarehouseGateController.UnlockSeconds * .55f);
            Assert.That(gate.IsUnlocked && gate.ChainReleased, Is.True);
            Assert.That(gate.ChainBodies, Is.Not.Empty);
            var releasePositions = new Vector3[gate.ChainBodies.Count];
            for (int index = 0; index < gate.ChainBodies.Count; index++)
            {
                Rigidbody body = gate.ChainBodies[index];
                Assert.That(body.gameObject.activeInHierarchy && body.useGravity && !body.isKinematic, Is.True,
                    "The released chain and padlock must enter world physics.");
                Assert.That(body.GetComponentsInChildren<Collider>(), Is.Not.Empty);
                releasePositions[index] = body.position;
            }
            for (int frame = 0; frame < 70; frame++) yield return new WaitForFixedUpdate();
            float largestFall = 0f;
            for (int index = 0; index < gate.ChainBodies.Count; index++)
                largestFall = Mathf.Max(largestFall, releasePositions[index].y - gate.ChainBodies[index].position.y);
            Assert.That(largestFall, Is.GreaterThan(.08f), "Gravity must visibly drop the released authored chain links.");
            foreach (Rigidbody body in gate.ChainBodies)
                Assert.That(body.position.y, Is.LessThan(.5f), "The complete open strand must clear the valve and fall to the floor.");
            CaptureCurrentCamera(root.CameraFollow.Camera, SceneIds.CombatTest, "Test warehouse - Valve chain released");
            Assert.That(NarrativeInteractionController.For(root.Player.Interactor).Phase,
                Is.EqualTo(NarrativeInteractionPhase.Activity), NarrativeInteractionController.For(root.Player.Interactor).LastFailureReason);
            gate.Advance(WarehouseGateController.UnlockSeconds);
            Assert.That(gate.Progress, Is.Zero, "Holding the opening E cannot also start turning the unlocked valve.");
            input.Release(keyboard.eKey, queueEventOnly: true);
            yield return null;

            float closedLift = gate.GateLift;
            MeshRenderer grille = null;
            foreach (MeshRenderer part in FindWarehouseModel(root, "Gate").GetComponentsInChildren<MeshRenderer>(true))
                if (part.name.StartsWith("VerticalBars__", StringComparison.Ordinal)) grille = part;
            Assert.That(grille, Is.Not.Null);
            float closedGrilleCenter = grille.bounds.center.y;
            Transform wheel = null;
            foreach (Transform part in gate.ValveInteraction.SubjectRoot.GetComponentsInChildren<Transform>(true))
                if (part.name == WarehouseValveInspection.WheelPivotName) wheel = part;
            Assert.That(wheel, Is.Not.Null);
            Quaternion restingWheel = wheel.rotation;
            input.Press(keyboard.eKey, queueEventOnly: true);
            yield return null;
            gate.Advance(WarehouseGateState.TurnSeconds * .25f);
            Assert.That(gate.Progress, Is.InRange(.24f, .34f));
            Assert.That(gate.GateLift, Is.GreaterThan(closedLift + .2f));
            Assert.That(grille.bounds.center.y - closedGrilleCenter, Is.EqualTo(gate.GateLift - closedLift).Within(.002f),
                "The actual authored grille must follow the retained travel amount.");
            Assert.That(Quaternion.Angle(restingWheel, wheel.rotation), Is.GreaterThan(15f),
                "Holding E must turn the authored wheel as well as advance the gate state.");
            Assert.That(gate.IsOpen, Is.False);
            Assert.That(gate.DoorInteraction.CanInteract(root.Player.Interactor), Is.False);
            input.Release(keyboard.eKey, queueEventOnly: true);
            yield return null;
            float partial = gate.Progress, partialLift = gate.GateLift;
            Quaternion partialWheel = wheel.rotation;
            gate.Advance(12f);
            Assert.That(gate.Progress, Is.EqualTo(partial));
            Assert.That(gate.GateLift, Is.EqualTo(partialLift), "Releasing E holds the raised grille at its exact height.");
            Assert.That(Quaternion.Angle(partialWheel, wheel.rotation), Is.LessThan(.001f));
            CaptureWarehouseFishGateOverview(root, "Test warehouse - Grille held partly open");
            using (GameTimeScaleRuntime.AcquirePause())
            {
                input.Press(keyboard.eKey, queueEventOnly: true);
                yield return null;
                gate.Advance(30f);
                for (int frame = 0; frame < 3; frame++) yield return null;
                Assert.That(gate.Progress, Is.EqualTo(partial));
                Assert.That(gate.GateLift, Is.EqualTo(partialLift));
                Assert.That(Quaternion.Angle(partialWheel, wheel.rotation), Is.LessThan(.001f));
                input.Release(keyboard.eKey, queueEventOnly: true);
                yield return null;
            }
            input.Press(keyboard.escapeKey, queueEventOnly: true);
            yield return null;
            input.Release(keyboard.escapeKey, queueEventOnly: true);
            yield return WaitForFishAssemblyExit(root);
            AssertFishAssemblyReleased(root);
            root.ResetRound();
            Assert.That(gate.Progress, Is.EqualTo(partial));
            Assert.That(gate.GateLift, Is.EqualTo(partialLift));
            Assert.That(Quaternion.Angle(partialWheel, wheel.rotation), Is.LessThan(.001f));
            Assert.That(grille.bounds.center.y - closedGrilleCenter, Is.EqualTo(partialLift - closedLift).Within(.002f));
            Assert.That(gate.IsUnlocked && gate.ChainReleased, Is.True);

            yield return EnterWarehouseFishGate(root, gate, input, keyboard, "Test valve - Continue opening");
            Assert.That(gate.Progress, Is.EqualTo(partial));
            input.Release(keyboard.eKey, queueEventOnly: true);
            yield return null;
            input.Press(keyboard.eKey, queueEventOnly: true);
            yield return null;
            gate.Advance(WarehouseGateState.TurnSeconds);
            Assert.That(gate.Progress, Is.EqualTo(1f));
            Assert.That(gate.IsOpen, Is.True);
            Assert.That(gate.GateLift, Is.GreaterThan(partialLift + .5f));
            Assert.That(grille.bounds.center.y - closedGrilleCenter, Is.EqualTo(gate.GateLift - closedLift).Within(.002f));
            input.Release(keyboard.eKey, queueEventOnly: true);
            yield return WaitForFishAssemblyExit(root);
            AssertFishAssemblyReleased(root);
            Assert.That(root.WarehouseArea.CanTraverse(root.WarehousePlan.GateApproach, doorway, .32f), Is.True,
                "The real motor area must allow the opened passage, beyond the original gate obstacle.");
            Vector3 passage = root.WarehousePlan.GateApproach;
            Assert.That(Physics.CapsuleCast(passage + Vector3.up * .36f, passage + Vector3.up * 1.4f,
                .32f, Vector3.forward, out _, 1.65f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.False,
                "The actual raised grille collider must clear a standing capsule before the closed door.");
            CaptureWarehouseFishGateOverview(root, "Test warehouse - Grille fully raised");
            Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.WarehouseGateKey), Is.EqualTo(1));

            NarrativeInteraction door = gate.DoorInteraction;
            MoveToWarehouseFishTarget(root, door);
            for (int frame = 0; frame < 3; frame++) yield return null;
            Assert.That(door.CanInteract(root.Player.Interactor), Is.True);
            Assert.That(root.Player.Interactor.ActiveInteractable, Is.SameAs(door));
            input.Press(keyboard.eKey, queueEventOnly: true);
            yield return null;
            input.Release(keyboard.eKey, queueEventOnly: true);
            NarrativeInteractionController session = NarrativeInteractionController.For(root.Player.Interactor);
            for (int frame = 0; frame < 240 && session.IsActive &&
                session.Phase != NarrativeInteractionPhase.Reading; frame++) yield return null;
            Assert.That(session.Phase, Is.EqualTo(NarrativeInteractionPhase.Reading), session.LastFailureReason);
            Assert.That(session.Target, Is.SameAs(door), "E must operate the authored door behind the raised grille.");
            Assert.That(root.Prompt.HasHeldPage && !root.Prompt.IsSpeaking, Is.True);
            Assert.That(LocalizationService.Get(session.CurrentPage.TextKey), Is.Not.Empty.And.Not.EqualTo(session.CurrentPage.TextKey));
            yield return CaptureFishUi("Test warehouse - Door behind raised grille");
            input.Press(keyboard.escapeKey, queueEventOnly: true);
            yield return null;
            input.Release(keyboard.escapeKey, queueEventOnly: true);
            yield return WaitForFishAssemblyExit(root);
            AssertFishAssemblyReleased(root);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneIds.CombatTest), "The door is the current placeholder.");
        }

        private static IEnumerator EnterWarehouseFishGate(CombatTestRoot root, WarehouseGateController gate,
            InputTestFixture input, Keyboard keyboard, string step)
        {
            MoveToWarehouseFishTarget(root, gate.ValveInteraction);
            for (int frame = 0; frame < 3; frame++) yield return null;
            Assert.That(root.Player.Interactor.ActiveInteractable, Is.SameAs(gate.ValveInteraction), step);
            Assert.That(keyboard.eKey.isPressed, Is.False, step + ": entering needs a new physical E press.");
            Assert.That(root.Prompt.HasHeldPage, Is.False, step + ": the preceding interaction must release its silent page.");
            Assert.That(root.Prompt.IsFeedbackVisible, Is.False, step + ": world feedback must not swallow the opening E.");
            Assert.That(gate.ValveInteraction.CanInteract(root.Player.Interactor), Is.True, step);
            input.Press(keyboard.eKey, queueEventOnly: true);
            yield return null;
            NarrativeInteractionController session = NarrativeInteractionController.For(root.Player.Interactor);
            bool sawActive = session.IsActive;
            for (int frame = 0; frame < 240 && session.Phase != NarrativeInteractionPhase.Activity; frame++)
            {
                // A queued key can reach the Input System after this coroutine
                // resumes. Initial Idle is not an attempted interaction failure.
                if (!session.IsActive && (sawActive || !string.IsNullOrEmpty(session.LastFailureReason))) break;
                sawActive |= session.IsActive;
                yield return null;
            }
            Assert.That(session.Phase, Is.EqualTo(NarrativeInteractionPhase.Activity),
                step + ": " + session.LastFailureReason + "; entered=" + sawActive + "; E held=" + keyboard.eKey.isPressed +
                "; feedback=" + root.Prompt.IsFeedbackVisible + "; held page=" + root.Prompt.HasHeldPage +
                "; interactor enabled=" + root.Player.Interactor.InputEnabled);
            Assert.That(session.Target, Is.SameAs(gate.ValveInteraction));
            // Keep the opening press held so the activity proves that it waits
            // for a neutral key before accepting a new deliberate hold.
        }

        private static void MoveToWarehouseFishTarget(CombatTestRoot root, NarrativeInteraction target)
        {
            PlayerAnimatedInteractionPose dock = target.Staging.Entry;
            root.Hero.ResetActor(dock.RootPosition, dock.RootRotation * Vector3.forward);
            Physics.SyncTransforms();
            root.CameraFollow.enabled = true;
            root.CameraFollow.ClearFixedPose();
        }

        private static void CaptureWarehouseFishGateOverview(CombatTestRoot root, string name)
        {
            Camera camera = root.CameraFollow.Camera;
            Vector3 previousPosition = camera.transform.position;
            Quaternion previousRotation = camera.transform.rotation;
            float previousFov = camera.fieldOfView;
            try
            {
                Vector3 eye = root.WarehousePlan.GatePosition + new Vector3(0f, 2f, -5f);
                camera.transform.SetPositionAndRotation(eye,
                    Quaternion.LookRotation(root.WarehousePlan.GatePosition + Vector3.up * 2.7f - eye));
                camera.fieldOfView = 70f;
                CaptureCurrentCamera(camera, SceneIds.CombatTest, name);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
                camera.fieldOfView = previousFov;
            }
        }

        private static void MoveToFishAssembly(CombatTestRoot root, WarehouseFishAssembly table)
        {
            PlayerAnimatedInteractionPose dock = table.Interaction.Staging.Entry;
            root.Hero.ResetActor(dock.RootPosition, dock.RootRotation * Vector3.forward);
            Physics.SyncTransforms();
            root.CameraFollow.enabled = true;
            root.CameraFollow.ClearFixedPose();
        }

        private static IEnumerator EnterFishAssembly(CombatTestRoot root, WarehouseFishAssembly table,
            InputTestFixture input, Keyboard keyboard)
        {
            MoveToFishAssembly(root, table);
            for (int frame = 0; frame < 3; frame++) yield return null;
            Assert.That(root.Player.Interactor.ActiveInteractable, Is.SameAs(table.Interaction));
            input.Press(keyboard.eKey, queueEventOnly: true);
            yield return null;
            input.Release(keyboard.eKey, queueEventOnly: true);
            NarrativeInteractionController session = NarrativeInteractionController.For(root.Player.Interactor);
            for (int frame = 0; frame < 240 && session.IsActive &&
                session.Phase != NarrativeInteractionPhase.Activity; frame++) yield return null;
            Assert.That(session.Phase, Is.EqualTo(NarrativeInteractionPhase.Activity), session.LastFailureReason);
            Assert.That(table.OwnsInteraction, Is.True);
            AssertFishAssemblyFraming(root, table);
        }

        private static IEnumerator WaitForFishAssemblyExit(CombatTestRoot root)
        {
            NarrativeInteractionController session = NarrativeInteractionController.For(root.Player.Interactor);
            for (int frame = 0; frame < 240 && session.IsActive; frame++) yield return null;
            Assert.That(session.IsActive, Is.False, session.LastFailureReason);
            yield return null;
        }

        private static void AssertFishAssemblyReleased(CombatTestRoot root)
        {
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
            Assert.That(root.Player.Motor.InputEnabled && root.Player.Interactor.InputEnabled, Is.True);
            Assert.That(root.CameraFollow.FixedPoseActive, Is.False);
        }

        private static void AssertFishAssemblyPose(WarehouseFishPartPose actual, WarehouseFishPartPose expected)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(.02f));
            Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(.02f));
            Assert.That(Mathf.DeltaAngle(actual.Yaw, expected.Yaw), Is.EqualTo(0f).Within(.5f));
        }

        private static void AssertFishAssemblySpawnCutoff(CombatTestRoot root, int expectedLive)
        {
            Assert.That(root.EndlessSpawnsBlockedByFish, Is.True);
            Assert.That(root.WaitingForNextOpponent, Is.False, "The third collected can clears a pending arrival.");
            Assert.That(root.NextOpponentSeconds, Is.Zero);
            Assert.That(root.LiveOpponentCount, Is.EqualTo(expectedLive));
            Assert.That(root.EndlessRunFinished || root.RoundFinished, Is.False,
                "The source cutoff does not declare the combat session finished.");
        }

        private static void AssertFishAssemblyCans(WarehouseFishAssembly table, bool visible)
        {
            foreach (string name in new[] { "RottenFishTailCan", "RottenFishBellyCan", "RottenFishHeadCan" })
            {
                Transform can = table.TableFrame.Find("Warehouse " + name);
                Assert.That(can, Is.Not.Null, "The table retains its bounded set of temporary pouring props.");
                Assert.That(can.gameObject.activeInHierarchy, Is.EqualTo(visible),
                    "Cans are transient pouring props and disappear when their fish sections are on the table.");
            }
        }

        private static void AssertFishAssemblyFraming(CombatTestRoot root, WarehouseFishAssembly table)
        {
            Camera camera = root.CameraFollow.Camera;
            Renderer bulb = FishPendantRenderer(FindWarehouseModel(root, "FishStationLamp"), "LampBulb__");
            Assert.That(bulb.enabled && !bulb.forceRenderingOff && bulb.gameObject.activeInHierarchy, Is.True);
            Assert.That(camera.transform.position.y, Is.LessThanOrEqualTo(bulb.bounds.min.y - .03f),
                "The actual minigame eye must stay below the lowest visible bulb surface after framing settles.");

            // The full authored basin lip, including both end rims, is wider
            // than the fish's inner collision floor. Legs do not define this shot.
            Vector2 low = Vector2.one * float.PositiveInfinity;
            Vector2 high = Vector2.one * float.NegativeInfinity;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 world = table.TableFrame.TransformPoint(new Vector3(
                    (corner & 1) == 0 ? -1.13f : 1.13f,
                    (corner & 2) == 0 ? .78f : 1.09f,
                    (corner & 4) == 0 ? -.611f : .611f));
                Vector3 screen = camera.WorldToViewportPoint(world);
                Assert.That(screen.z, Is.GreaterThan(camera.nearClipPlane));
                Assert.That(screen.x, Is.InRange(.015f, .985f), "The full table rim must fit horizontally.");
                Assert.That(screen.y, Is.InRange(.015f, .985f), "The full table rim must fit vertically.");
                low = Vector2.Min(low, new Vector2(screen.x, screen.y));
                high = Vector2.Max(high, new Vector2(screen.x, screen.y));
            }
            Vector2 coverage = high - low;
            Assert.That(coverage.x, Is.GreaterThanOrEqualTo(.78f), "The tabletop must occupy most of the viewport width.");
            Assert.That(coverage.y, Is.GreaterThanOrEqualTo(.50f), "The tabletop must remain large enough to assemble its parts.");
            Assert.That(coverage.x * coverage.y, Is.GreaterThanOrEqualTo(.45f));
        }
    }
}
