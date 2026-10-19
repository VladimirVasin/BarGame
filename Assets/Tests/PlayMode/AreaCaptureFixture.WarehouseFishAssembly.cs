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
        [Explicit("Focused lower-camera fish assembly captures, using the production table and finale.")]
        public IEnumerator CombatWarehouseFishAssemblyCloseUp()
        {
            CombatTestRoot root = null;
            float previousDelta = Time.captureDeltaTime;
            float previousAspect = 0f;
            bool previousBackground = Application.runInBackground;
            var input = new InputTestFixture();
            input.Setup();
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Time.captureDeltaTime = 1f / 60f;
            Application.runInBackground = true;
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            GameSessionState.BeginNewGame();
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest, LoadSceneMode.Single);
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                Assert.That(root != null && root.IsInitialized, Is.True);
                root.AutomaticSimulation = false;
                root.SetSparring(false);
                root.SetDuelLogging(false);
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                previousAspect = root.CameraFollow.Camera.aspect;
                root.CameraFollow.Camera.aspect = (float)Width / Height;
                for (int frame = 0; frame < SettleFrames; frame++) yield return null;
                WarehouseFishAssembly table = root.GetComponentInChildren<WarehouseFishAssembly>();
                Assert.That(table, Is.Not.Null);
                var cans = new[] { InventoryItemId.RottenFishTailCan, InventoryItemId.RottenFishBellyCan,
                    InventoryItemId.RottenFishHeadCan };
                for (int part = 0; part < cans.Length; part++)
                    Assert.That(GameSessionState.TryCollectWorldItem(root.WarehousePlan.Refrigerators[part].SourceId,
                        cans[part]), Is.True);
                yield return EnterFishAssembly(root, table, input, keyboard);
                table.Advance(WarehouseFishAssemblyState.PourSeconds);
                for (int frame = 0; frame < 35; frame++) yield return new WaitForFixedUpdate();
                Assert.That(table.Phase, Is.EqualTo(WarehouseFishAssemblyPhase.Assembling));
                AssertFishAssemblyFraming(root, table);
                CaptureCurrentCamera(root.CameraFollow.Camera, SceneIds.CombatTest, "Test warehouse - Fish sections close-up");
                foreach (WarehouseFishPart part in new[] { WarehouseFishPart.Belly, WarehouseFishPart.Tail, WarehouseFishPart.Head })
                {
                    Assert.That(table.SelectPart(part), Is.True);
                    Assert.That(table.MoveSelected(new Vector2(((int)part - 1) * WarehouseFishAssemblyState.PartSpacing, 0f), 0f), Is.True);
                    Assert.That(table.DropSelected(), Is.True);
                }
                Assert.That(table.Phase, Is.EqualTo(WarehouseFishAssemblyPhase.Convulsing));
                table.Advance(1.6f);
                yield return null;
                AssertFishAssemblyFraming(root, table);
                CaptureCurrentCamera(root.CameraFollow.Camera, SceneIds.CombatTest, "Test warehouse - Assembled fish close-up");
                Light pendant = root.GetComponentInChildren<WarehouseFishLamp>().Light;
                Light torch = root.Flashlight.SpotLight;
                bool pendantEnabled = pendant.enabled, torchEnabled = torch.enabled;
                try
                {
                    torch.enabled = false;
                    CaptureCurrentCamera(root.CameraFollow.Camera, SceneIds.CombatTest, "Test warehouse - Fish under pendant only");
                    pendant.enabled = false;
                    torch.enabled = torchEnabled;
                    CaptureCurrentCamera(root.CameraFollow.Camera, SceneIds.CombatTest, "Test warehouse - Fish under flashlight only");
                }
                finally { pendant.enabled = pendantEnabled; torch.enabled = torchEnabled; }
                for (int step = 0; step < 100 && table.Phase == WarehouseFishAssemblyPhase.Convulsing; step++) table.Advance(.05f);
                table.Advance(.35f);
                yield return null;
                Assert.That(table.Phase, Is.EqualTo(WarehouseFishAssemblyPhase.Spitting));
                AssertFishAssemblyFraming(root, table);
                CaptureCurrentCamera(root.CameraFollow.Camera, SceneIds.CombatTest, "Test warehouse - Fish key close-up");
            }
            finally
            {
                if (root != null)
                {
                    NarrativeInteractionController.For(root.Player.Interactor).RestoreImmediate();
                    if (previousAspect > 0f) root.CameraFollow.Camera.aspect = previousAspect;
                }
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = null;
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
                Time.captureDeltaTime = previousDelta;
                Application.runInBackground = previousBackground;
            }
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            GameSessionState.BeginNewGame();
        }

        [UnityTest]
        [Explicit("Focused keyed lock, controlled chain release, valve audio and retained gate lifecycle.")]
        public IEnumerator CombatWarehouseGateUnlockAndAudio()
        {
            CombatTestRoot root = null;
            float previousDelta = Time.captureDeltaTime;
            bool previousBackground = Application.runInBackground;
            var input = new InputTestFixture();
            input.Setup();
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Time.captureDeltaTime = 1f / 60f;
            Application.runInBackground = true;
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            GameSessionState.BeginNewGame();
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest, LoadSceneMode.Single);
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                Assert.That(root != null && root.IsInitialized && root.WarehousePlan != null, Is.True);
                Assert.That(root.Mode, Is.EqualTo(CombatTestMode.Standard));
                root.AutomaticSimulation = false;
                root.SetSparring(false);
                root.SetDuelLogging(false);
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                root.CameraFollow.Camera.aspect = (float)Width / Height;
                for (int frame = 0; frame < SettleFrames; frame++) yield return null;
                Assert.That(GameSessionState.TryAddInventoryItem(InventoryItemId.WarehouseGateKey), Is.True);
                yield return VerifyWarehouseFishGate(root, input, keyboard, detailedUnlockProof: true);

                WarehouseGateController gate = root.GetComponentInChildren<WarehouseGateController>();
                root.ResetRound();
                Assert.That(gate.IsUnlocked && gate.IsOpen && gate.ChainReleased, Is.True);
                Assert.That(gate.Progress, Is.EqualTo(1f));
                Assert.That(gate.Sounds.UnlockClickCount, Is.EqualTo(1));
                Assert.That(gate.Sounds.ChainReleaseCount, Is.EqualTo(1));
                Assert.That(gate.Sounds.FullyOpenLatchCount, Is.EqualTo(1), "Resetting combat cannot replay the terminal stop.");
                WarehouseGateAudio oldSounds = gate.Sounds;
                Assert.That(root.NewWarehouseLayout(0), Is.True);
                yield return null;
                Assert.That(gate == null, Is.True, "Replacing the layout destroys the previous mechanism owner.");
                Assert.That(oldSounds == null, Is.True);
                gate = root.GetComponentInChildren<WarehouseGateController>();
                Assert.That(gate.IsUnlocked && gate.IsOpen && gate.ChainReleased, Is.True);
                Assert.That(gate.Progress, Is.EqualTo(1f));
                Assert.That(gate.Sounds.UnlockClickCount, Is.Zero);
                Assert.That(gate.Sounds.ChainReleaseCount, Is.Zero);
                Assert.That(gate.Sounds.FullyOpenLatchCount, Is.Zero, "A restored open gate is silent.");
                Assert.That(gate.Sounds.CreakSource.isPlaying, Is.False);
                foreach (Rigidbody body in gate.ChainBodies)
                    Assert.That(body.isKinematic, Is.True, "A restored chain remains settled without replaying the release.");

                GameSessionState.ResetInventoryState();
                Assert.That(gate.IsUnlocked || gate.IsOpen || gate.ChainReleased, Is.False);
                Assert.That(gate.Progress, Is.Zero);
                Assert.That(GameSessionState.HasInventoryItem(InventoryItemId.WarehouseGateKey), Is.False);
                Assert.That(gate.KeyModel.gameObject.activeInHierarchy, Is.False);
                Assert.That(gate.Sounds.CreakSource.isPlaying || gate.Sounds.ActionSource.isPlaying ||
                    gate.Sounds.ContactSource.isPlaying, Is.False);
                Assert.That(gate.Sounds.FullyOpenLatchCount, Is.Zero);
                foreach (Rigidbody body in gate.ChainBodies)
                    Assert.That(body.isKinematic, Is.True);
            }
            finally
            {
                if (root != null)
                    NarrativeInteractionController.For(root.Player.Interactor).RestoreImmediate();
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = null;
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
                Time.captureDeltaTime = previousDelta;
                Application.runInBackground = previousBackground;
            }
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            GameSessionState.BeginNewGame();
        }

        [UnityTest]
        [Explicit("Third-can retirement, instant full-stop relocation of the original gantry, feed continuity and retained lifecycle.")]
        public IEnumerator CombatWarehouseFishClearsOpponents()
        {
            CombatTestRoot root = null;
            float previousDelta = Time.captureDeltaTime;
            bool previousBackground = Application.runInBackground;
            var input = new InputTestFixture();
            input.Setup();
            Time.captureDeltaTime = 1f / 60f;
            Application.runInBackground = true;
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.UseDockerPopulationForTests = false;
            CombatTestRoot.AuthoredEnemyKindForTests = null;
            CombatTestRoot.WarehouseSeedForTests = 0;
            GameSessionState.BeginNewGame();
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                Assert.That(CombatTestStartService.TryStart(new CombatTestSelection(
                    CombatLoadoutSelection.Default, CombatTestMode.Endless)), Is.True);
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
                Assert.That(root != null && root.IsInitialized, Is.True);
                Assert.That(SceneTransitionService.IsTransitioning, Is.False);
                root.AutomaticSimulation = false;
                root.SetDuelLogging(false);
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                Assert.That(root.Mode, Is.EqualTo(CombatTestMode.Endless));
                Assert.That(root.EncounterSettings.MaximumLiveOpponents, Is.EqualTo(3));
                Assert.That(root.LiveOpponentCount, Is.EqualTo(3));
                Assert.That(root.Opponents[0].IsRoom, Is.True);
                Assert.That(root.Opponents[1].IsDocker, Is.True);
                CombatActor crane = root.Opponents[2];
                Assert.That(crane.IsCrane, Is.True);
                Assert.That(root.WarehouseCraneRemnantActive, Is.False);
                var cans = new[] { InventoryItemId.RottenFishTailCan, InventoryItemId.RottenFishBellyCan,
                    InventoryItemId.RottenFishHeadCan };
                for (int part = 0; part < 2; part++)
                    Assert.That(GameSessionState.TryCollectWorldItem(root.WarehousePlan.Refrigerators[part].SourceId,
                        cans[part]), Is.True);
                Assert.That(root.EndlessSpawnsBlockedByFish, Is.False);
                Assert.That(root.LiveOpponentCount, Is.EqualTo(3));
                foreach (CombatActor actor in root.Opponents)
                    Assert.That(actor.gameObject.activeInHierarchy && !actor.State.IsDefeated, Is.True,
                        "Two collected cans retain the existing opponents.");

                CombatActor corpse = root.Opponents[0];
                for (int shot = 0; shot < 2; shot++)
                {
                    corpse.CaptureContactPose();
                    Assert.That(corpse.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head, out Vector3 head,
                        out Vector3 outward, out _), Is.True);
                    Assert.That(corpse.Hurtboxes.SweepProjectile(head + outward * .4f, head - outward * .4f,
                        CombatProjectilePool.Radius, -outward, out CombatHurtboxes.Hit hit), Is.True);
                    Assert.That(hit.Part, Is.EqualTo(Player3DAnatomicalPart.Head));
                    corpse.ReceiveProjectile(root.Hero, 100 + shot, hit, -outward * CombatProjectilePool.MuzzleSpeed);
                }
                root.Tick(0f);
                Assert.That(corpse.State.IsDefeated && corpse.IsRagdollActive && corpse.gameObject.activeInHierarchy, Is.True);
                Assert.That(root.LiveOpponentCount, Is.EqualTo(2));
                Assert.That(root.CorpseCount, Is.EqualTo(1));
                Assert.That(root.EndlessKills, Is.EqualTo(1));
                Assert.That(root.Opponents[1].gameObject.activeInHierarchy && crane.gameObject.activeInHierarchy, Is.True);

                Camera camera = root.CameraFollow.Camera;
                camera.aspect = (float)Width / Height;
                Vector3 suspension = crane.GetCraneCableStart(0);
                Assert.That(suspension.y, Is.GreaterThan(WarehouseArenaPlan.TopHeight),
                    "The original overhead suspension remains above the galleries.");
                var retainedPieces = new Transform[crane.CraneCableCount, CombatActor.CraneCableSegments];
                var originalFeeds = new Transform[crane.CraneCableCount];
                Transform suspensionHost = crane.GetCraneCableTransform(0, 0).parent;
                Transform rails = suspensionHost.Find("Crane longitudinal rails");
                Transform bridge = suspensionHost.Find("Crane transverse bridge");
                Transform headModel = suspensionHost.Find("Ceiling suspension head");
                Assert.That(rails != null && bridge != null && headModel != null, Is.True);
                Vector3 railsPosition = rails.position, railsScale = rails.localScale;
                Quaternion railsRotation = rails.rotation;
                Vector3 bridgeScale = bridge.localScale, headScale = headModel.localScale;
                for (int cable = 0; cable < crane.CraneCableCount; cable++)
                {
                    originalFeeds[cable] = CityPedestrianHandProps.FindSocket(
                        crane.GetCraneCableTransform(cable, 0).parent, CombatCraneAssetProvider.HeadAnchorNames[cable]);
                    Assert.That(originalFeeds[cable], Is.Not.Null);
                    for (int segment = 0; segment < CombatActor.CraneCableSegments; segment++)
                        retainedPieces[cable, segment] = crane.GetCraneCableTransform(cable, segment);
                }
                Vector3 cameraPosition = suspension + new Vector3(2.8f, -1.8f, -3.6f);
                Quaternion cameraRotation = Quaternion.LookRotation(
                    suspension + Vector3.down * 1.1f - cameraPosition);
                root.CameraFollow.SetFixedPose(cameraPosition, cameraRotation, 56f);
                for (int frame = 0; frame < SettleFrames; frame++) yield return null;
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse - Crane before final can");

                long killsBefore = root.EndlessKills;
                int impactsBefore = root.Hero.ReceivedImpactCount;
                Vector3 headBefore = crane.CraneAnchorPosition;
                var feedsBefore = new Vector3[originalFeeds.Length];
                var releasedHeights = new float[originalFeeds.Length];
                for (int cable = 0; cable < originalFeeds.Length; cable++)
                {
                    feedsBefore[cable] = originalFeeds[cable].position;
                    releasedHeights[cable] = crane.GetCraneCableEnd(cable).y - crane.CraneCarriagePosition.y;
                }
                Assert.That(GameSessionState.TryCollectWorldItem(root.WarehousePlan.Refrigerators[2].SourceId,
                    cans[2]), Is.True);
                AssertWarehouseFishOpponentsCleared(root, killsBefore);
                CombatCraneRemnant remnant = root.GetComponentInChildren<CombatCraneRemnant>();
                Assert.That(remnant, Is.Not.Null);
                Assert.That(crane.CraneAnchorPosition, Is.EqualTo(headBefore), "The gantry must wait for the fan to stop completely.");
                for (int cable = 0; cable < originalFeeds.Length; cable++)
                    Assert.That(Vector3.Distance(crane.GetCraneCableStart(cable), feedsBefore[cable]), Is.LessThan(.001f));
                Assert.That(remnant.IsParked, Is.False);
                Vector3 commandBefore = crane.CraneCarriagePosition;
                WarehouseArenaMechanisms machines = root.GetComponentInChildren<WarehouseArenaMechanisms>();
                machines.Advance(WarehouseArenaMechanisms.FanShutdownDuration - .1f);
                Assert.That(machines.FanShutdownProgress, Is.LessThan(1f));
                Assert.That(remnant.IsParked, Is.False);
                remnant.Advance(.35f);
                Assert.That(crane.CraneCarriagePosition, Is.EqualTo(commandBefore));
                Assert.That(crane.CraneAnchorPosition, Is.EqualTo(headBefore),
                    "Ordinary remnant updates cannot start a gradual mounting move before full fan stop.");
                for (int cable = 0; cable < originalFeeds.Length; cable++)
                    Assert.That(Vector3.Distance(crane.GetCraneCableStart(cable), feedsBefore[cable]), Is.LessThan(.001f));
                Vector3 pausedEnd = remnant.GetCableEnd(0);
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    machines.Advance(1f);
                    remnant.Advance(1f);
                    Assert.That(crane.CraneCarriagePosition, Is.EqualTo(commandBefore), "Pause cannot complete the fan or relocate its gantry.");
                    Assert.That(remnant.GetCableEnd(0), Is.EqualTo(pausedEnd));
                    Assert.That(remnant.IsParked, Is.False);
                }
                machines.Advance(.2f);
                Assert.That(machines.FanShutdownProgress, Is.EqualTo(1f));
                Assert.That(remnant.IsParked, Is.True, "Full fan stop must relocate the complete mounting in this same call.");
                AssertWarehouseFishOpponentsCleared(root, killsBefore, expectParked: true);
                AssertWarehouseRetainedCranePieces(crane, retainedPieces);
                Assert.That(rails.position, Is.EqualTo(railsPosition));
                Assert.That(rails.rotation, Is.EqualTo(railsRotation));
                Assert.That(rails.localScale, Is.EqualTo(railsScale));
                Assert.That(bridge.localScale, Is.EqualTo(bridgeScale));
                Assert.That(headModel.localScale, Is.EqualTo(headScale), "Relocation cannot resize or rebuild the original mechanism.");
                for (int cable = 0; cable < releasedHeights.Length; cable++)
                    Assert.That(crane.GetCraneCableEnd(cable).y - crane.CraneCarriagePosition.y,
                        Is.EqualTo(releasedHeights[cable]).Within(.001f),
                        "Relocation preserves each original attachment's relative height instead of inventing another cable set.");
                SetWarehouseCraneRemnantView(root, crane, false);
                Vector3 cableBefore = remnant.GetCableEnd(0);
                remnant.Advance(.35f);
                Vector3 cableAfter = remnant.GetCableEnd(0);
                Assert.That(Vector3.Distance(cableBefore, cableAfter), Is.InRange(.0001f, .2f),
                    "The free suspension retains a small wind-driven sway after the body disappears.");
                remnant.Advance(2.5f);
                Assert.That(remnant.Source.isPlaying, Is.True, "The free suspension emits a sparse creak.");
                Assert.That(remnant.Source.spatialBlend, Is.EqualTo(1f));
                Assert.That(remnant.Source.loop, Is.False);
                cableAfter = remnant.GetCableEnd(0);
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    remnant.Advance(.5f);
                    Assert.That(Vector3.Distance(cableAfter, remnant.GetCableEnd(0)), Is.LessThan(.00001f),
                        "Pausing freezes the remnant's achieved cable position.");
                    Assert.That(remnant.Source.isPlaying, Is.False);
                }
                for (int step = 0; step < 120; step++) root.Tick(CombatTestRoot.SimulationStep);
                yield return null;
                AssertWarehouseFishOpponentsCleared(root, killsBefore, expectParked: true);
                Assert.That(root.Hero.ReceivedImpactCount, Is.EqualTo(impactsBefore));
                SetWarehouseCraneRemnantView(root, crane, false);
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse - Original crane gantry and valve-side cables");
                SetWarehouseCraneRemnantView(root, crane, true);
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse - Crane cable ends from valve");

                root.ResetRound();
                AssertWarehouseFishOpponentsCleared(root, 0, expectParked: true);
                AssertWarehouseRetainedCranePieces(crane, retainedPieces);
                Assert.That(root.NewWarehouseLayout(0), Is.True);
                yield return null;
                AssertWarehouseFishOpponentsCleared(root, 0, expectParked: true);
                AssertWarehouseRetainedCranePieces(crane, retainedPieces);

                GameSessionState.ResetInventoryState();
                Assert.That(root.EndlessSpawnsBlockedByFish || root.WarehouseCraneRemnantActive, Is.False);
                Assert.That(root.LiveOpponentCount, Is.EqualTo(3),
                    "Resetting the source collection restores the original population through WorldItemsChanged.");
                root.ResetRound();
                Assert.That(root.LiveOpponentCount, Is.EqualTo(3));
                Assert.That(root.WarehouseCraneRemnantActive, Is.False);
                foreach (CombatActor actor in root.Opponents)
                    Assert.That(actor.gameObject.activeInHierarchy && !actor.State.IsDefeated, Is.True);

                for (int part = 0; part < cans.Length; part++)
                    Assert.That(GameSessionState.TryCollectWorldItem(root.WarehousePlan.Refrigerators[part].SourceId,
                        cans[part]), Is.True);
                AssertWarehouseFishOpponentsCleared(root, 0);
                Assert.That(remnant.IsParked, Is.False);
                root.GetComponentInChildren<WarehouseArenaMechanisms>().Advance(WarehouseArenaMechanisms.FanShutdownDuration);
                AssertWarehouseFishOpponentsCleared(root, 0, expectParked: true);
                AssertWarehouseRetainedCranePieces(crane, retainedPieces);
                GameSessionState.BeginNewGame();
                Assert.That(root.EndlessSpawnsBlockedByFish || root.WarehouseCraneRemnantActive, Is.False);
                Assert.That(root.LiveOpponentCount, Is.EqualTo(3));
            }
            finally
            {
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.UseDockerPopulationForTests = false;
                CombatTestRoot.AuthoredEnemyKindForTests = null;
                CombatTestRoot.WarehouseSeedForTests = null;
                input.TearDown();
                Time.captureDeltaTime = previousDelta;
                Application.runInBackground = previousBackground;
            }
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            GameSessionState.BeginNewGame();
        }

        private static void AssertWarehouseFishOpponentsCleared(CombatTestRoot root, long expectedKills, bool expectParked = false)
        {
            AssertFishAssemblySpawnCutoff(root, 0);
            Assert.That(root.CorpseCount, Is.Zero);
            Assert.That(root.EndlessKills, Is.EqualTo(expectedKills), "Retiring opponents does not credit kills.");
            Assert.That(root.WarehouseCraneRemnantActive, Is.True);
            Assert.That(root.SetOpponentFocus(true), Is.False);
            Assert.That(root.IsOpponentFocused || root.FocusMarkerVisible || root.Hero.IsRestrained ||
                root.RoomAttackCinematicActive, Is.False);
            CombatActor crane = null;
            foreach (CombatActor actor in root.GetComponentsInChildren<CombatActor>(true))
            {
                if (actor.IsHero) continue;
                if (actor.IsCrane)
                {
                    Assert.That(crane, Is.Null, "The retained encounter has one crane suspension.");
                    crane = actor;
                }
                Assert.That(actor.gameObject.activeInHierarchy || actor.IsAvailable || actor.State.IsAttacking ||
                    actor.RoomAttackPoseActive || actor.HasCraneVictim, Is.False);
                Assert.That(actor.TryAttack(), Is.False);
                Assert.That(actor.TryAwakenZombie(), Is.False);
                Assert.That(root.Hero.CanDamageParticipant(actor), Is.False);
                Assert.That(root.BloodEffects.ProjectileWoundCountFor(actor), Is.Zero);
                Assert.That(root.HeadEffects.DetachedSectorCountFor(actor), Is.Zero);
                foreach (AudioSource source in actor.GetComponentsInChildren<AudioSource>(true))
                    Assert.That(source.isPlaying, Is.False, "Retired opponents cannot keep enemy audio playing.");
            }
            int remnants = 0;
            foreach (CombatCraneRemnant remnant in root.GetComponentsInChildren<CombatCraneRemnant>(true))
                if (remnant.gameObject.activeInHierarchy) remnants++;
            Assert.That(remnants, Is.EqualTo(1), "Reset and layout re-entry retain a single crane remnant.");
            Assert.That(crane, Is.Not.Null);
            AssertWarehouseOriginalCraneCables(root, crane, expectParked);
        }

        private static void AssertWarehouseRetainedCranePieces(CombatActor crane, Transform[,] retainedPieces)
        {
            for (int cable = 0; cable < crane.CraneCableCount; cable++)
                for (int segment = 0; segment < CombatActor.CraneCableSegments; segment++)
                    Assert.That(crane.GetCraneCableTransform(cable, segment), Is.SameAs(retainedPieces[cable, segment]),
                        "Retirement and relocation reuse all original cable meshes.");
        }

        private static void AssertWarehouseOriginalCraneCables(CombatTestRoot root, CombatActor crane, bool expectParked)
        {
            CombatCraneRemnant remnant = root.GetComponentInChildren<CombatCraneRemnant>();
            Assert.That(remnant != null && remnant.IsVisible, Is.True);
            Assert.That(crane.CraneCableCount, Is.EqualTo(6));
            Vector3 valve = root.WarehousePlan.GateControlPosition;
            Assert.That(Mathf.Abs(crane.CraneCarriagePosition.x), Is.LessThanOrEqualTo(5f + .001f));
            Assert.That(Mathf.Abs(crane.CraneCarriagePosition.z), Is.LessThanOrEqualTo(5f + .001f));
            Assert.That(crane.CraneAnchorPosition.y, Is.EqualTo(9.15f).Within(.001f),
                "The original overhead machine height must remain unchanged.");
            if (expectParked)
            {
                Assert.That(remnant.IsParked, Is.True);
                Vector3 goal = new Vector3(valve.x, crane.CraneGroundPosition.y + .40f, 5f);
                Assert.That(Vector3.Distance(crane.CraneCarriagePosition, goal), Is.LessThan(.025f),
                    "The complete mounting must snap to the closest projected valve point within its original well limits.");
            }
            for (int cable = 0; cable < crane.CraneCableCount; cable++)
            {
                Vector3 start = crane.GetCraneCableStart(cable), end = crane.GetCraneCableEnd(cable);
                Transform feed = CityPedestrianHandProps.FindSocket(crane.GetCraneCableTransform(cable, 0).parent,
                    CombatCraneAssetProvider.HeadAnchorNames[cable]);
                Assert.That(feed, Is.Not.Null);
                Assert.That(Vector3.Distance(start, feed.position), Is.LessThan(.001f),
                    "Each cable must still emerge from its original authored gantry feed.");
                Assert.That(start, Is.EqualTo(remnant.GetCableStart(cable)));
                Assert.That(end, Is.EqualTo(remnant.GetCableEnd(cable)));
                Assert.That(start.y, Is.GreaterThan(end.y + .03f));

                Vector3 previous = start;
                for (int segment = 0; segment < CombatActor.CraneCableSegments; segment++)
                {
                    Vector3 from = crane.GetCraneCableSegmentStart(cable, segment);
                    Vector3 to = crane.GetCraneCableSegmentEnd(cable, segment);
                    Assert.That(Vector3.Distance(from, previous), Is.LessThan(.001f));
                    Assert.That(from.y, Is.InRange(end.y - .001f, start.y + .001f));
                    Assert.That(to.y, Is.InRange(end.y - .001f, start.y + .001f));
                    Transform piece = crane.GetCraneCableTransform(cable, segment);
                    Assert.That(piece.gameObject.activeInHierarchy, Is.True);
                    Assert.That(piece.GetComponentsInChildren<Collider>(true), Is.Empty);
                    Assert.That(piece.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
                    Renderer[] renderers = piece.GetComponentsInChildren<Renderer>(true);
                    Assert.That(renderers, Is.Not.Empty);
                    Bounds bounds = renderers[0].bounds;
                    for (int index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
                    Assert.That(Vector3.Distance(bounds.center, (from + to) * .5f), Is.LessThan(.08f),
                        "The imported cable mesh must visibly span the public segment endpoints.");
                    Assert.That(bounds.size.magnitude, Is.EqualTo(Vector3.Distance(from, to)).Within(.08f));
                    foreach (WarehouseArenaSurface surface in root.WarehousePlan.Surfaces)
                    {
                        Rect area = surface.Bounds;
                        var deck = new Bounds(new Vector3(area.center.x,
                            surface.Height - WarehouseArenaPlan.DeckThickness * .5f, area.center.y),
                            new Vector3(area.width, WarehouseArenaPlan.DeckThickness, area.height));
                        Assert.That(CombatHurtboxes.IntersectsSegment(deck, from, to - from, .01f), Is.False,
                            "The full original-feed cable path must stay outside every solid gallery deck.");
                    }
                    previous = to;
                }
                Assert.That(Vector3.Distance(previous, end), Is.LessThan(.001f));
            }
        }

        private static void SetWarehouseCraneRemnantView(CombatTestRoot root, CombatActor crane, bool detail)
        {
            Vector3 valve = root.WarehousePlan.GateControlPosition;
            Vector3 ends = Vector3.zero;
            for (int cable = 0; cable < crane.CraneCableCount; cable++) ends += crane.GetCraneCableEnd(cable);
            ends /= crane.CraneCableCount;
            Vector3 eye = detail ? valve + new Vector3(0f, 1.6f, -2.1f) : new Vector3(.5f, 3.7f, -3.5f);
            Vector3 focus = detail ? ends + Vector3.up * .25f :
                (crane.CraneAnchorPosition + ends) * .5f + Vector3.forward * 1.4f;
            root.CameraFollow.SetFixedPose(eye, Quaternion.LookRotation(focus - eye), detail ? 62f : 76f);
        }

        [UnityTest]
        [Explicit("The valve's red fixture survives the live pendant burst and silent retained reconstruction.")]
        public IEnumerator CombatWarehouseGateLampSurvivesPendant()
        {
            CombatTestRoot root = null;
            float previousDelta = Time.captureDeltaTime;
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            Time.captureDeltaTime = 1f / 60f;
            GameSessionState.BeginNewGame();
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest, LoadSceneMode.Single);
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                Assert.That(root != null && root.IsInitialized, Is.True);
                root.AutomaticSimulation = false;
                root.SetDuelLogging(false);
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                WarehouseFishLamp lamp = root.GetComponentInChildren<WarehouseFishLamp>();
                Assert.That(lamp.ValveLamp != null && lamp.ValveLampFixture != null, Is.True);
                AssertWarehouseValveMountingClearance(root, lamp);
                root.Hero.ResetActor(root.WarehousePlan.GateControlApproach +
                    Vector3.up * PlayerFactory.GroundedRootOffset, Vector3.forward);
                root.Hero.SetCombatFocused(false);
                root.Hero.ApplyMotorConstraint();
                WarehouseArenaLighting.UpdateHeroVisibility(root.Flashlight.transform);
                root.CameraFollow.enabled = false;
                Camera camera = root.CameraFollow.Camera;
                Vector3 mounting = lamp.ValveLampFixture.position;
                Vector3 mountingEye = mounting + new Vector3(1.25f, .15f, -1.65f);
                camera.transform.SetPositionAndRotation(mountingEye, Quaternion.LookRotation(mounting - mountingEye));
                camera.fieldOfView = 50f;
                camera.aspect = (float)Width / Height;
                Physics.SyncTransforms();
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse - Valve lamp mounting clearance");
                float redIntensity = lamp.ValveLamp.intensity;
                var cans = new[] { InventoryItemId.RottenFishTailCan, InventoryItemId.RottenFishBellyCan,
                    InventoryItemId.RottenFishHeadCan };
                for (int index = 0; index < cans.Length; index++)
                    Assert.That(GameSessionState.TryCollectWorldItem(root.WarehousePlan.Refrigerators[index].SourceId,
                        cans[index]), Is.True);
                // The pure trigger is already covered by the puzzle scenario;
                // advance the production heating/burst path for this lighting regression.
                Assert.That(GameSessionState.WarehouseFishLamp.TryBegin(true, true), Is.True);
                lamp.Advance(WarehouseFishLamp.HeatingSeconds - .02f);
                Assert.That(lamp.Phase, Is.EqualTo(WarehouseFishLampPhase.Heating));
                Assert.That(lamp.ValveLamp.enabled, Is.True);
                lamp.Advance(.03f);
                Assert.That(lamp.Phase, Is.EqualTo(WarehouseFishLampPhase.Burst));
                Assert.That(lamp.BurstPlayCount, Is.EqualTo(1));
                AssertWarehousePendantBlackout(root);
                Assert.That(lamp.ValveLamp.intensity, Is.EqualTo(redIntensity));

                Vector3 target = root.WarehousePlan.GateControlPosition + Vector3.up * 1.45f;
                Vector3 eye = target + new Vector3(2f, .25f, -4f);
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                camera.fieldOfView = 50f;
                camera.aspect = (float)Width / Height;
                root.Flashlight.SpotLight.enabled = false;
                root.Flashlight.SynchronizeBodyFill(0f);
                Physics.SyncTransforms();
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse - Red valve lamp after bulb burst");
                VerifyWarehouseValveFlicker(root, lamp, camera, redIntensity);
                root.Flashlight.SpotLight.enabled = true;
                root.Flashlight.SynchronizeBodyFill(0f);

                lamp.enabled = false;
                Assert.That(lamp.ValveLamp.enabled, Is.True);
                lamp.enabled = true;
                root.ResetRound();
                AssertWarehousePendantBlackout(root);
                Assert.That(root.NewWarehouseLayout(0), Is.True);
                yield return null;
                lamp = root.GetComponentInChildren<WarehouseFishLamp>();
                AssertWarehouseValveMountingClearance(root, lamp);
                Assert.That(lamp.Phase, Is.EqualTo(WarehouseFishLampPhase.Burst));
                Assert.That(lamp.HeatingPlayCount + lamp.BurstPlayCount, Is.Zero);
                AssertWarehousePendantBlackout(root);
                Assert.That(lamp.ValveLamp.intensity, Is.EqualTo(redIntensity));
                GameSessionState.BeginNewGame();
                Assert.That(lamp.Phase, Is.EqualTo(WarehouseFishLampPhase.Waiting));
                Assert.That(lamp.ValveLamp.enabled && lamp.ValveLamp.intensity > 0f, Is.True);
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            }
            finally
            {
                if (root != null) root.AutomaticSimulation = false;
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = null;
                Time.captureDeltaTime = previousDelta;
                GameSessionState.BeginNewGame();
            }
        }

        private static void AssertWarehouseValveMountingClearance(CombatTestRoot root, WarehouseFishLamp lamp)
        {
            Bounds fixtureBounds = default, controlBounds = default;
            bool hasFixture = false, hasControl = false;
            foreach (MeshRenderer renderer in lamp.ValveLampFixture.GetComponentsInChildren<MeshRenderer>())
            {
                if (!hasFixture) { fixtureBounds = renderer.bounds; hasFixture = true; }
                else fixtureBounds.Encapsulate(renderer.bounds);
            }
            foreach (MeshRenderer renderer in FindWarehouseModel(root, "GateControl").GetComponentsInChildren<MeshRenderer>())
            {
                if (!hasControl) { controlBounds = renderer.bounds; hasControl = true; }
                else controlBounds.Encapsulate(renderer.bounds);
            }
            Assert.That(hasFixture && hasControl, Is.True);
            Assert.That(fixtureBounds.min.z - controlBounds.max.z, Is.GreaterThan(.06f),
                "The complete authored lamp must clear the valve riser and its brackets before and after reconstruction.");
        }

        private static void VerifyWarehouseValveFlicker(CombatTestRoot root, WarehouseFishLamp lamp,
            Camera camera, float baseIntensity)
        {
            WarehouseValveLampFlicker flicker = lamp.ValveLampFixture.GetComponent<WarehouseValveLampFlicker>();
            Assert.That(flicker, Is.Not.Null);
            Renderer lens = null;
            foreach (Renderer renderer in lamp.ValveLampFixture.GetComponentsInChildren<Renderer>(true))
                if (renderer.name.EndsWith("__SignalRed", StringComparison.Ordinal)) lens = renderer;
            Assert.That(lens, Is.Not.Null);
            Material sharedLens = lens.sharedMaterial;
            int emissionId = Shader.PropertyToID("_EmissionColor");
            Color authoredEmission = sharedLens.GetColor(emissionId);
            var properties = new MaterialPropertyBlock();
            float minimum = 1f, maximum = 0f;
            bool capturedDip = false;
            for (int step = 0; step < 240; step++)
            {
                flicker.Advance(.05f);
                Assert.That(lamp.ValveLamp.enabled, Is.True);
                Assert.That(flicker.CurrentFactor, Is.InRange(WarehouseValveLampFlicker.MinimumFactor - .0001f, 1f));
                Assert.That(lamp.ValveLamp.intensity, Is.EqualTo(baseIntensity * flicker.CurrentFactor).Within(.0001f));
                minimum = Mathf.Min(minimum, flicker.CurrentFactor);
                maximum = Mathf.Max(maximum, flicker.CurrentFactor);
                lens.GetPropertyBlock(properties);
                Color emission = properties.HasColor(emissionId) ? properties.GetColor(emissionId) : authoredEmission;
                Assert.That(emission.r, Is.EqualTo(authoredEmission.r * flicker.CurrentFactor).Within(.0001f),
                    "The lens and real red light must dip together.");
                if (capturedDip || flicker.CurrentFactor >= .5f) continue;
                capturedDip = true;
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse - Red valve lamp during flicker");
            }
            Assert.That(minimum, Is.LessThan(.5f));
            Assert.That(maximum, Is.EqualTo(1f));
            Assert.That(capturedDip, Is.True);
            Assert.That(lens.sharedMaterial, Is.SameAs(sharedLens));
            Assert.That(sharedLens.GetColor(emissionId), Is.EqualTo(authoredEmission));
            float elapsed = flicker.Elapsed, factor = flicker.CurrentFactor;
            using (GameTimeScaleRuntime.AcquirePause()) flicker.Advance(1.6f);
            Assert.That(flicker.Elapsed, Is.EqualTo(elapsed));
            Assert.That(flicker.CurrentFactor, Is.EqualTo(factor));
            try
            {
                GameInput.HandleApplicationFocus(false);
                flicker.Advance(1.6f);
                Assert.That(flicker.Elapsed, Is.EqualTo(elapsed));
            }
            finally { GameInput.HandleApplicationFocus(true); }
            flicker.enabled = false;
            Assert.That(lamp.ValveLamp.enabled, Is.True);
            Assert.That(lamp.ValveLamp.intensity, Is.EqualTo(baseIntensity));
            flicker.enabled = true;
        }

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
                Assert.That(root.LiveOpponentCount, Is.EqualTo(pacing.MaximumLiveOpponents));
                CombatActor survivor = root.Opponents[0];
                Assert.That(survivor.gameObject.activeInHierarchy, Is.True);
                Assert.That(root.WaitingForNextOpponent, Is.False);
                Assert.That(root.NextOpponentSeconds, Is.Zero);

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
                Assert.That(root.WaitingForNextOpponent, Is.False);
                Assert.That(GameSessionState.TryCollectWorldItem(root.WarehousePlan.Refrigerators[2].SourceId,
                    cans[2]), Is.True);
                AssertFishAssemblySpawnCutoff(root, 0);
                Assert.That(survivor.gameObject.activeInHierarchy, Is.False,
                    "Collecting the final can retires the existing live opponent without defeating it.");
                Assert.That(root.AdvanceEndlessInterval(12f), Is.False);
                AssertFishAssemblySpawnCutoff(root, 0);
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
            WarehouseFishLamp pendant = root.GetComponentInChildren<WarehouseFishLamp>();
            Assert.That(pendant.ValveLamp != null && pendant.ValveLampFixture != null, Is.True);
            Assert.That(pendant.ValveLamp.enabled && pendant.ValveLamp.intensity > 0f, Is.True,
                "The red valve fixture remains a working light after the pendant fails.");
            Assert.That(root.Flashlight.SpotLight.enabled && root.Flashlight.SpotLight.intensity > 0f, Is.True);
            foreach (Light light in root.GetComponentsInChildren<Light>(true))
                if (!light.transform.IsChildOf(root.Hero.transform) && light != pendant.ValveLamp)
                    Assert.That(light.enabled && light.intensity > 0f, Is.False, "Other fixed lights stay off after the pendant burst: " + light.name);
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
                if (renderer.transform.IsChildOf(pendant.ValveLampFixture))
                    Assert.That(emission.maxColorComponent, Is.GreaterThan(.01f), "The red valve lens must retain its visible glow.");
                else Assert.That(emission.maxColorComponent, Is.LessThan(.001f), "Other fixed lenses stay dark: " + renderer.name);
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

        private static IEnumerator VerifyWarehouseGateUnlockStages(CombatTestRoot root, WarehouseGateController gate)
        {
            Assert.That(gate.Sounds, Is.Not.Null);
            Assert.That(gate.KeyModel.gameObject.activeInHierarchy, Is.True);
            Quaternion keyRest = gate.KeyModel.rotation, shackleRest = gate.Shackle.rotation;
            float shackleHeight = gate.Shackle.position.y;
            var closedPoses = new Pose[gate.ChainBodies.Count];
            for (int index = 0; index < gate.ChainBodies.Count; index++)
                closedPoses[index] = new Pose(gate.ChainBodies[index].position, gate.ChainBodies[index].rotation);
            MeshRenderer lockHousing = null;
            foreach (MeshRenderer renderer in gate.ChainBodies[28].GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.name.StartsWith("PadlockBody", StringComparison.Ordinal)) lockHousing = renderer;
            MeshRenderer shackleRenderer = gate.Shackle.GetComponentInChildren<MeshRenderer>(true);
            Assert.That(lockHousing, Is.Not.Null);
            Assert.That(shackleRenderer, Is.Not.Null);
            Assert.That(lockHousing.bounds.Intersects(shackleRenderer.bounds), Is.True,
                "The closed shackle must remain seated in the housing before the key turns.");
            gate.Advance(Mathf.Max(0f, .5f - gate.UnlockElapsed));
            Assert.That(gate.IsUnlocked || gate.ChainReleased, Is.False);
            AssertWarehouseChainHeldBeforeCommit(gate, closedPoses);
            Assert.That(gate.Shackle.position.y, Is.EqualTo(shackleHeight).Within(.001f));
            Assert.That(Quaternion.Angle(shackleRest, gate.Shackle.rotation), Is.LessThan(.001f));
            Assert.That(lockHousing.bounds.Intersects(shackleRenderer.bounds), Is.True,
                "Inserting and turning the key cannot detach the closed lock from its chain.");
            Assert.That(Quaternion.Angle(keyRest, gate.KeyModel.rotation), Is.GreaterThan(15f));
            Vector3 turnedKey = gate.KeyModel.position;
            CaptureWarehouseGateDetail(root, gate.Shackle.position, "Test warehouse - Key turning in lock");

            float pausedUnlock = gate.UnlockElapsed;
            Quaternion pausedKey = gate.KeyModel.rotation;
            using (GameTimeScaleRuntime.AcquirePause())
            {
                gate.Advance(1f);
                gate.Sounds.Advance(1f);
                for (int frame = 0; frame < 3; frame++) yield return null;
                Assert.That(gate.UnlockElapsed, Is.EqualTo(pausedUnlock));
                Assert.That(Quaternion.Angle(pausedKey, gate.KeyModel.rotation), Is.LessThan(.001f));
                Assert.That(gate.Sounds.IsPaused, Is.True);
                Assert.That(gate.Sounds.UnlockClickCount, Is.Zero);
                int heldSample = gate.Sounds.ActionSource.timeSamples;
                for (int frame = 0; frame < 3; frame++) yield return null;
                Assert.That(gate.Sounds.ActionSource.timeSamples, Is.EqualTo(heldSample));
            }
            gate.Sounds.Advance(0f);
            gate.Advance(Mathf.Max(0f, 1.4f - gate.UnlockElapsed));
            Assert.That(gate.IsUnlocked || gate.ChainReleased, Is.False);
            AssertWarehouseChainHeldBeforeCommit(gate, closedPoses);
            Assert.That(gate.Shackle.position.y, Is.GreaterThan(shackleHeight + .04f));
            Assert.That(Quaternion.Angle(shackleRest, gate.Shackle.rotation), Is.GreaterThan(60f),
                "The shackle must be fully open before the strand or padlock can move.");
            Assert.That(Vector3.Distance(turnedKey, gate.KeyModel.position), Is.GreaterThan(.01f),
                "The key must withdraw before the open lock releases the chain.");
            CaptureWarehouseGateDetail(root, gate.Shackle.position, "Test warehouse - Lifted and turned shackle");

            gate.Advance(Mathf.Max(0f, WarehouseGateController.UnlockCommitSeconds + .01f - gate.UnlockElapsed));
            Assert.That(gate.IsUnlocked && gate.ChainReleased, Is.True);
            Assert.That(gate.ChainPhysicsReleased, Is.False);
            Assert.That(gate.KeyModel.gameObject.activeInHierarchy, Is.False);
            Assert.That(gate.Sounds.UnlockClickCount, Is.EqualTo(1));
            Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.WarehouseGateKey), Is.EqualTo(1));
            var slideStart = new Vector3[gate.ChainBodies.Count];
            for (int index = 0; index < gate.ChainBodies.Count; index++)
            {
                Assert.That(gate.ChainBodies[index].isKinematic, Is.True);
                slideStart[index] = gate.ChainBodies[index].position;
            }
            for (int frame = 0; frame < 4; frame++) yield return new WaitForFixedUpdate();
            Assert.That(gate.ChainPhysicsReleased, Is.False);
            float slideDistance = 0f;
            var pausedPositions = new Vector3[gate.ChainBodies.Count];
            for (int index = 0; index < gate.ChainBodies.Count; index++)
            {
                Rigidbody body = gate.ChainBodies[index];
                Assert.That(body.isKinematic, Is.True);
                slideDistance = Mathf.Max(slideDistance, Vector3.Distance(slideStart[index], body.position));
                pausedPositions[index] = body.position;
            }
            Assert.That(slideDistance, Is.GreaterThan(.005f), "The open strand must slide free before gravity is enabled.");
            CaptureWarehouseGateDetail(root, gate.transform.position + Vector3.up * .6f,
                "Test warehouse - Open chain sliding free", overview: true);
            float pausedSlideUnlock = gate.UnlockElapsed;
            using (GameTimeScaleRuntime.AcquirePause())
            {
                gate.Advance(1f);
                gate.Sounds.Advance(1f);
                for (int frame = 0; frame < 3; frame++) yield return null;
                Assert.That(gate.UnlockElapsed, Is.EqualTo(pausedSlideUnlock));
                Assert.That(gate.ChainPhysicsReleased, Is.False);
                Assert.That(gate.Sounds.IsPaused, Is.True);
                for (int index = 0; index < gate.ChainBodies.Count; index++)
                    Assert.That(Vector3.Distance(pausedPositions[index], gate.ChainBodies[index].position), Is.LessThan(.0001f));
            }
            gate.Sounds.Advance(0f);
            Assert.That(gate.Sounds.UnlockClickCount, Is.EqualTo(1), "Resuming cannot replay a committed lock cue.");
            try
            {
                GameInput.HandleApplicationFocus(false);
                gate.Advance(1f);
                gate.Sounds.Advance(1f);
                for (int frame = 0; frame < 3; frame++) yield return null;
                Assert.That(gate.UnlockElapsed, Is.EqualTo(pausedSlideUnlock));
                Assert.That(gate.ChainPhysicsReleased, Is.False);
                Assert.That(gate.Sounds.IsPaused, Is.True);
                for (int index = 0; index < gate.ChainBodies.Count; index++)
                    Assert.That(Vector3.Distance(pausedPositions[index], gate.ChainBodies[index].position), Is.LessThan(.0001f),
                        "Focus loss freezes the achieved controlled-slide pose.");
            }
            finally
            {
                GameInput.HandleApplicationFocus(true);
            }
            gate.Sounds.Advance(0f);
            Assert.That(gate.Sounds.UnlockClickCount, Is.EqualTo(1), "Focus return cannot replay the committed lock cue.");
        }

        private static void AssertWarehouseChainHeldBeforeCommit(WarehouseGateController gate, Pose[] closedPoses)
        {
            for (int index = 0; index < gate.ChainBodies.Count; index++)
            {
                Rigidbody body = gate.ChainBodies[index];
                Assert.That(body.isKinematic, Is.True);
                Assert.That(Vector3.Distance(body.position, closedPoses[index].position), Is.LessThan(.0001f),
                    "The closed strand and padlock stay attached until the unlock commit marker.");
                Assert.That(Quaternion.Angle(body.rotation, closedPoses[index].rotation), Is.LessThan(.001f));
            }
        }

        private static void CaptureWarehouseGateDetail(CombatTestRoot root, Vector3 focus, string name, bool overview = false)
        {
            Camera camera = root.CameraFollow.Camera;
            Vector3 previousPosition = camera.transform.position;
            Quaternion previousRotation = camera.transform.rotation;
            float previousFov = camera.fieldOfView;
            try
            {
                Vector3 eye = focus + (overview ? new Vector3(.65f, .7f, -1.6f) : new Vector3(.2f, .12f, -.62f));
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
                camera.fieldOfView = overview ? 64f : 48f;
                CaptureCurrentCamera(camera, SceneIds.CombatTest, name);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
                camera.fieldOfView = previousFov;
            }
        }

        private static IEnumerator VerifyWarehouseFishGate(CombatTestRoot root, InputTestFixture input, Keyboard keyboard,
            bool detailedUnlockProof = false)
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
            if (detailedUnlockProof)
            {
                Assert.That(gate.KeyModel.gameObject.activeInHierarchy, Is.False);
                Assert.That(gate.Sounds.UnlockClickCount, Is.Zero);
                Assert.That(gate.Sounds.ChainReleaseCount, Is.Zero);
                foreach (Rigidbody body in gate.ChainBodies) Assert.That(body.isKinematic, Is.True);
            }

            yield return EnterWarehouseFishGate(root, gate, input, keyboard, "Test valve - Unlock after cancellation");
            if (detailedUnlockProof) yield return VerifyWarehouseGateUnlockStages(root, gate);
            else gate.Advance(WarehouseGateController.UnlockSeconds * .55f);
            Assert.That(gate.IsUnlocked && gate.ChainReleased, Is.True);
            for (int frame = 0; frame < 60 && !gate.ChainPhysicsReleased; frame++)
                yield return new WaitForFixedUpdate();
            Assert.That(gate.ChainPhysicsReleased, Is.True, "Controlled sliding must finish before real gravity takes ownership.");
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
            for (int frame = 0; frame < (detailedUnlockProof ? 330 : 70); frame++)
            {
                yield return new WaitForFixedUpdate();
                if (!detailedUnlockProof) continue;
                foreach (Rigidbody body in gate.ChainBodies)
                {
                    Assert.That(float.IsFinite(body.position.x) && float.IsFinite(body.position.y) && float.IsFinite(body.position.z), Is.True);
                    Assert.That(body.linearVelocity.magnitude, Is.LessThan(12f), "The released strand must fall without explosive joint separation.");
                    Assert.That(Vector3.Distance(body.position, gate.transform.position), Is.LessThan(4f));
                }
                if (frame == 9)
                    CaptureWarehouseGateDetail(root, gate.transform.position + Vector3.up * .6f,
                        "Test warehouse - Chain falling", overview: true);
            }
            float largestFall = 0f;
            for (int index = 0; index < gate.ChainBodies.Count; index++)
                largestFall = Mathf.Max(largestFall, releasePositions[index].y - gate.ChainBodies[index].position.y);
            Assert.That(largestFall, Is.GreaterThan(.08f), "Gravity must visibly drop the released authored chain links.");
            foreach (Rigidbody body in gate.ChainBodies)
                Assert.That(body.position.y, Is.LessThan(.5f), "The complete open strand must clear the valve and fall to the floor.");
            if (detailedUnlockProof)
            {
                foreach (Rigidbody body in gate.ChainBodies) Assert.That(body.isKinematic, Is.True);
                Assert.That(gate.Sounds.UnlockClickCount, Is.EqualTo(1));
                Assert.That(gate.Sounds.ChainReleaseCount, Is.EqualTo(1));
                Assert.That(gate.Sounds.ImpactCount, Is.GreaterThan(0), "Real environmental contacts must produce bounded metal impacts.");
                CaptureWarehouseGateDetail(root, gate.transform.position + Vector3.up * .25f,
                    "Test warehouse - Chain settled on floor", overview: true);
            }
            CaptureCurrentCamera(root.CameraFollow.Camera, SceneIds.CombatTest, "Test warehouse - Valve chain released");
            // Fixed-update waits and explicit camera rendering can leave the
            // Input System on a non-player buffer. Rejoin the dynamic loop
            // before InputTestFixture creates a delta event for the held key.
            yield return null;
            Assert.That(keyboard.added, Is.True);
            Assert.That(Keyboard.current, Is.SameAs(keyboard));
            Assert.That(keyboard.eKey.isPressed, Is.True);
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
            if (detailedUnlockProof)
            {
                gate.Sounds.Advance(.1f);
                Assert.That(gate.Sounds.FullyOpenLatchCount, Is.Zero, "Partial travel cannot announce a fully open gate.");
                Assert.That(gate.Sounds.CreakStartCount, Is.GreaterThan(0));
                Assert.That(gate.Sounds.CreakSource.volume, Is.GreaterThan(0f));
                Assert.That(gate.Sounds.CreakSource.loop && gate.Sounds.CreakSource.isPlaying, Is.True);
                AudioClip clip = gate.Sounds.CreakSource.clip;
                Assert.That(clip, Is.Not.Null);
                int frames = Mathf.Min(clip.samples, clip.frequency * 2);
                var samples = new float[frames * clip.channels];
                Assert.That(clip.GetData(samples, 0), Is.True);
                string folder = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest);
                System.IO.Directory.CreateDirectory(folder);
                WritePcmWave(System.IO.Path.Combine(folder, "Test warehouse - Valve creak.wav"), samples,
                    clip.frequency, clip.channels, 1f);
            }
            input.Release(keyboard.eKey, queueEventOnly: true);
            yield return null;
            float partial = gate.Progress, partialLift = gate.GateLift;
            Quaternion partialWheel = wheel.rotation;
            gate.Advance(12f);
            if (detailedUnlockProof)
            {
                gate.Sounds.Advance(.2f);
                Assert.That(gate.Sounds.CreakSource.volume, Is.Zero);
                Assert.That(gate.Sounds.CreakSource.isPlaying, Is.False, "Releasing E fades the valve sound out.");
                Assert.That(gate.Sounds.FullyOpenLatchCount, Is.Zero);
            }
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
                if (detailedUnlockProof)
                {
                    gate.Sounds.Advance(.5f);
                    Assert.That(gate.Sounds.IsPaused, Is.True);
                    Assert.That(gate.Sounds.CreakSource.isPlaying, Is.False);
                    Assert.That(gate.Sounds.UnlockClickCount, Is.EqualTo(1));
                    Assert.That(gate.Sounds.FullyOpenLatchCount, Is.Zero);
                }
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
            if (detailedUnlockProof)
            {
                Assert.That(gate.KeyModel.gameObject.activeInHierarchy, Is.False);
                Assert.That(gate.Sounds.UnlockClickCount, Is.EqualTo(1), "Re-entry must not replay the committed lock click.");
                Assert.That(gate.Sounds.ChainReleaseCount, Is.EqualTo(1));
            }
            input.Release(keyboard.eKey, queueEventOnly: true);
            yield return null;
            input.Press(keyboard.eKey, queueEventOnly: true);
            yield return null;
            gate.Advance(WarehouseGateState.TurnSeconds);
            Assert.That(gate.Progress, Is.EqualTo(1f));
            Assert.That(gate.IsOpen, Is.True);
            Assert.That(gate.GateLift, Is.GreaterThan(partialLift + .5f));
            Assert.That(grille.bounds.center.y - closedGrilleCenter, Is.EqualTo(gate.GateLift - closedLift).Within(.002f));
            if (detailedUnlockProof)
            {
                Assert.That(gate.Sounds.FullyOpenLatchCount, Is.EqualTo(1));
                AudioSource terminalVoice = gate.Sounds.ActionSource;
                Assert.That(terminalVoice.isPlaying && !terminalVoice.loop, Is.True);
                Assert.That(terminalVoice.transform.parent, Is.SameAs(wheel), "The final stop sounds at the valve, away from the fallen lock.");
                Assert.That(Vector3.Distance(terminalVoice.transform.position, wheel.position), Is.LessThan(.001f));
                Assert.That(terminalVoice.outputAudioMixerGroup, Is.SameAs(GameAudioMixer.SfxWorldGroup));
                AudioClip terminalClip = terminalVoice.clip;
                Assert.That(terminalClip, Is.Not.Null);
                var terminalSamples = new float[terminalClip.samples * terminalClip.channels];
                Assert.That(terminalClip.GetData(terminalSamples, 0), Is.True);
                float peak = 0f;
                bool finite = true;
                foreach (float sample in terminalSamples)
                {
                    finite &= float.IsFinite(sample);
                    peak = Mathf.Max(peak, Mathf.Abs(sample));
                }
                Assert.That(finite, Is.True);
                Assert.That(peak, Is.InRange(.2f, .89f), "The generated terminal stop has a bounded audible impact.");
                gate.Advance(WarehouseGateState.TurnSeconds);
                Assert.That(gate.Sounds.FullyOpenLatchCount, Is.EqualTo(1), "Holding E at the end cannot replay the latch.");
                yield return null;
                Assert.That(NarrativeInteractionController.For(root.Player.Interactor).Phase,
                    Is.EqualTo(NarrativeInteractionPhase.Exiting));
                Assert.That(terminalVoice.isPlaying, Is.True, "Automatic activity completion must let the world's latch ring out.");
                gate.EndActivity();
                Assert.That(terminalVoice.isPlaying, Is.True, "Repeated activity cleanup cannot cut the terminal tail.");
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    gate.Sounds.Advance(.1f);
                    int pausedLatch = terminalVoice.timeSamples;
                    yield return null;
                    Assert.That(terminalVoice.isPlaying, Is.False);
                    Assert.That(terminalVoice.timeSamples, Is.EqualTo(pausedLatch));
                }
                gate.Sounds.Advance(.01f);
                Assert.That(terminalVoice.isPlaying, Is.True);
                Assert.That(gate.Sounds.FullyOpenLatchCount, Is.EqualTo(1));
                gate.Sounds.Stop();
                Assert.That(terminalVoice.isPlaying, Is.False, "Full world cleanup must stop the terminal tail.");
                gate.Advance(WarehouseGateState.TurnSeconds);
                Assert.That(gate.Sounds.FullyOpenLatchCount, Is.EqualTo(1));
            }
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
            Assert.That(camera.transform.position.y, Is.LessThanOrEqualTo(table.TableFrame.position.y + 1.30f),
                "The fish puzzle retains its lower close-up instead of backing out to the whole table rim.");

            // Keep every reachable part position visible above the control strip;
            // the tall outer metal rim no longer defines the close-up.
            Vector2 low = Vector2.one * float.PositiveInfinity;
            Vector2 high = Vector2.one * float.NegativeInfinity;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 world = table.TableFrame.TransformPoint(new Vector3(
                    (corner & 1) == 0 ? -.88f : .88f,
                    (corner & 2) == 0 ? .78f : .90f,
                    (corner & 4) == 0 ? -.38f : .38f));
                Vector3 screen = camera.WorldToViewportPoint(world);
                Assert.That(screen.z, Is.GreaterThan(camera.nearClipPlane));
                Assert.That(screen.x, Is.InRange(.015f, .985f), "The entire playable surface must fit horizontally.");
                Assert.That(screen.y, Is.InRange(.105f, .985f), "Reachable fish sections must stay above the controls.");
                low = Vector2.Min(low, new Vector2(screen.x, screen.y));
                high = Vector2.Max(high, new Vector2(screen.x, screen.y));
            }
            Vector2 coverage = high - low;
            Assert.That(coverage.x, Is.GreaterThanOrEqualTo(.65f), "The playable surface must fill the close-up.");
            float aspectScale = Mathf.Min(1f, camera.aspect / (16f / 9f));
            Assert.That(coverage.y, Is.GreaterThanOrEqualTo(.50f * aspectScale),
                "The wide work surface must retain its readable height at the current aspect ratio.");
            Assert.That(coverage.x * coverage.y, Is.GreaterThanOrEqualTo(.35f));
        }
    }
}
