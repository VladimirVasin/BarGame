using System;
using System.Collections;
using System.IO;
using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class HawthornCaptureAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            foreach (string name in new[] { "HawthornTinctureAssetSetup", "PlayerVodkaActionAssetSetup" })
                Type.GetType("BarPromenade.Editor." + name + ", BarPromenade.Editor", true)
                    .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [UnityTest, Explicit("Focused warehouse tincture pickup, one rig-driven swallow and finite red perception cue.")]
        [PrebuildSetup(typeof(HawthornCaptureAssetsSetup))]
        public IEnumerator CombatWarehouseHawthorn()
        {
            AssertHawthornPureContracts();
            CombatTestRoot root = null;
            var input = new InputTestFixture();
            float previousDelta = Time.captureDeltaTime;
            bool previousBackground = Application.runInBackground;
            bool previousLens = GraphicsEffectsSettings.IntoxicationLensFxEnabled;
            input.Setup();
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            Time.captureDeltaTime = 1f / 60f;
            Application.runInBackground = true;
            GraphicsEffectsSettings.IntoxicationLensFxEnabled = true;
            CombatTestRoot.UseLegacyArenaForTests = false;
            // The ordinary single-opponent warehouse reserves its only Docker for the final
            // refrigerator. Use the existing live-population fixture hook while exercising
            // the hero's normal inventory/weapon handoff clock, with opponent decisions off.
            CombatTestRoot.UseDockerPopulationForTests = true;
            CombatTestRoot.WarehouseSeedForTests = 0;
            GameSessionState.BeginNewGame();
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest, LoadSceneMode.Single);
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                Assert.That(root != null && root.IsInitialized, Is.True);
                root.AutomaticSimulation = false;
                root.AutomaticOpponentDecisions = false;
                root.SetSparring(false);
                root.SetDuelLogging(false);
                ForceHawthornFocus(root);
                yield return null;
                WarehouseTincturePickup[] stock = root.GetComponentsInChildren<WarehouseTincturePickup>();
                Assert.That(stock.Length, Is.EqualTo(3));
                Assert.That(root.WarehousePlan.AmmoAnchors.Count, Is.EqualTo(12));
                WarehouseTincturePickup bottle = Array.Find(stock,
                    item => item.SourceId == WarehouseTincturePickup.SourceIdForFloor(0));
                Assert.That(bottle, Is.Not.Null);
                WarehouseLocker[] lockers = root.GetComponentsInChildren<WarehouseLocker>();
                WarehouseLocker bank = FindTestStockLocker(lockers, bottle.transform.position, out int bay);
                PlaceTestLockerHero(root, root.WarehousePlan.TinctureApproaches[0], bank);
                Assert.That(bottle.CanInteract(root.Player.Interactor), Is.False, "Closed leaves must hide their stock.");
                Assert.That(bottle.RenderedOutlineCount, Is.Zero);
                Assert.That(bank.BeginOpen(bay), Is.True);
                bank.Advance(2f);
                yield return null;
                ForceHawthornFocus(root);
                Assert.That(bottle.CanInteract(root.Player.Interactor), Is.True);
                yield return CaptureTestLockerCloseView(root, bank, "hawthorn-00-shelf");
                Assert.That(bottle.RenderedOutlineCount, Is.GreaterThan(0), "Visible open-shelf medicine has a yellow contour.");
                CombatAmmoPickup ammo = root.AmmoSpawns.GetSlot(0);
                WarehouseLocker ammoBank = FindTestStockLocker(lockers, ammo.transform.position, out int ammoBay);
                PlaceTestLockerHero(root, root.WarehousePlan.AmmoApproaches[0], ammoBank);
                Assert.That(ammoBank.BeginOpen(ammoBay), Is.True);
                ammoBank.Advance(2f);
                yield return CaptureTestLockerCloseView(root, ammoBank, "hawthorn-00-ammo-outline");
                Assert.That(ammo.RenderedOutlineCount, Is.GreaterThan(0), "Visible ammunition shares the yellow contour.");
                root.CameraFollow.ClearFixedPose();
                PlaceTestLockerHero(root, root.WarehousePlan.TinctureApproaches[0], bank);
                root.CameraFollow.ClearFixedPose();
                WorldItemFoundScreen screen = WorldItemFoundScreen.For(root.Player.Interactor);
                Assert.That(bottle.TryBeginPickup(root.Player.Interactor), Is.True);
                yield return AwaitTestLockerScreen(() => screen.IsShowing);
                yield return null;
                Assert.That(screen.ActiveItemId, Is.EqualTo(InventoryItemId.HawthornTincture));
                Assert.That(bottle.RenderedOutlineCount, Is.Zero, "Found-item presentation borrows the model without a loot contour.");
                Assert.That(bank.BeginClose(bay), Is.False);
                yield return CaptureFishUi("hawthorn-01-shared-inspection-label");
                Assert.That(screen.Dismiss(), Is.True);
                yield return AwaitTestLockerScreen(() => !screen.IsPresenting);
                yield return null;
                ForceHawthornFocus(root);
                Assert.That(bottle.IsAvailable, Is.True);
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.HawthornTincture), Is.Zero);
                Assert.That(bottle.Model.parent, Is.SameAs(bottle.transform));
                Assert.That(bottle.TryBeginPickup(root.Player.Interactor), Is.True);
                yield return AwaitTestLockerScreen(() => screen.IsShowing);
                yield return null;
                Assert.That(screen.Confirm(), Is.True);
                Assert.That(screen.Confirm(), Is.False);
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.HawthornTincture), Is.EqualTo(1));
                yield return AwaitTestLockerScreen(() => !screen.IsPresenting);
                yield return null;
                ForceHawthornFocus(root);
                root.ResetRound();
                yield return null;
                Assert.That(bottle.IsAvailable || bottle.Model.gameObject.activeSelf, Is.False,
                    "A normal round reset must not refill a collected medicine source.");
                Assert.That(GameSessionState.TryCollectWorldItem(bottle.SourceId, InventoryItemId.HawthornTincture), Is.False);

                root.Hero.State.RestoreVitals(40f, root.Hero.State.Settings.MaxStamina);
                root.AutomaticSimulation = true;
                ForceHawthornFocus(root);
                Assert.That(root.Hero.TrySelectWeapon(CombatWeaponId.Pistol), Is.True);
                yield return AwaitHawthorn(() => root.Hero.HasWeaponInHand && root.Hero.WeaponId == CombatWeaponId.Pistol && !root.Hero.IsSwitchingWeapon,
                    "Prepare the actual previous hand item.");
                GameObject previousWeaponModel = root.Hero.CarriedWeapon(CombatWeaponId.Pistol);
                IFirearmState previousFirearm = root.Hero.CarriedFirearm(CombatWeaponId.Pistol);
                int previousRounds = previousFirearm.Rounds, previousReserve = previousFirearm.ReserveRounds;
                root.AdvanceHudPresentation(.2f);
                root.AdvanceHudPresentation(5f);
                yield return CaptureHawthornHealthHud(root, "hawthorn-02-hp-before", .4f);
                Assert.That(root.Inventory.Open(), Is.True);
                int selected = -1;
                for (int index = 0; index < GameSessionState.InventoryItems.Count; index++)
                    if (GameSessionState.InventoryItems[index].ItemId == InventoryItemId.HawthornTincture) selected = index;
                Assert.That(selected, Is.GreaterThanOrEqualTo(0));
                if (root.Inventory.SelectedItemIndex != selected) Assert.That(root.Inventory.SelectItem(selected), Is.True);
                PlayerVodkaBottleController drink = root.Player.GameObject.GetComponent<PlayerVodkaBottleController>();
                Assert.That(root.Inventory.SelectedIsEquippable || root.Inventory.SelectedIsEquipped, Is.False);
                Assert.That(root.Inventory.SelectedUseActionLabel, Is.EqualTo("Использовать").IgnoreCase);
                Assert.That(root.Inventory.Health, Is.EqualTo(40f));
                Assert.That(root.Inventory.MaximumHealth, Is.EqualTo(100f));
                yield return null;
                yield return CaptureFishUi("hawthorn-02-inventory-health-before");
                input.Press(keyboard.uKey, queueEventOnly: true); yield return null;
                input.Release(keyboard.uKey, queueEventOnly: true); yield return null;
                Assert.That(root.Inventory.IsOpen, Is.False, "Use immediately closes inventory without a second action.");
                Assert.That(root.Inventory.UseSelected(), Is.False, "Closed inventory cannot queue a duplicate use.");
                Assert.That(root.Hero.State.Health, Is.EqualTo(40f));
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.HawthornTincture), Is.EqualTo(1));
                Assert.That(GameSessionState.IsInventoryItemEquipped(InventoryItemId.HawthornTincture), Is.False);
                ForceHawthornFocus(root);
                yield return AwaitHawthorn(() => drink.Phase == PlayerVodkaBottleController.BottlePhase.Sipping,
                    "Use must close inventory and automatically start the single gulp without LMB.", () =>
                        Assert.That(root.Hero.HasWeaponInHand && drink.Phase != PlayerVodkaBottleController.BottlePhase.Hidden, Is.False), () =>
                        $"phase={drink.Phase},auto={drink.IsAutomaticMedicineUse},pending={drink.HasPendingMedicineUse},return={drink.HasPendingMedicineReturn}," +
                        $"owned={GameSessionState.IsHawthornUseOwned(drink)},held={root.Hero.HasWeaponInHand},switch={root.Hero.IsSwitchingWeapon},weapon={root.Hero.WeaponId}," +
                        $"motor={root.Player.Motor.InputEnabled},interactor={root.Player.Interactor.InputEnabled},pause={GameTimeScaleRuntime.IsPaused},focus={GameCursor.ApplicationFocused}," +
                        $"clip={((Player3DCharacterPresentation)root.Player.Visual).CanAcquireClip(drink)},sips={drink.CompletedSips},hp={root.Hero.State.Health}");
                Assert.That(drink.IsAutomaticMedicineUse, Is.True);
                Assert.That(drink.PhaseDurationSeconds, Is.EqualTo(.30f));
                float pausedProgress = drink.PhaseProgress;
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    for (int frame = 0; frame < 3; frame++) yield return null;
                    Assert.That(drink.PhaseProgress, Is.EqualTo(pausedProgress));
                    Assert.That(drink.CompletedSips, Is.Zero);
                }
                drink.enabled = false;
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.HawthornTincture), Is.EqualTo(1));
                Assert.That(root.Hero.State.Health, Is.EqualTo(40f));
                drink.enabled = true;
                Assert.That(root.Hero.TrySelectWeapon(CombatWeaponId.Pistol), Is.True);
                yield return AwaitHawthorn(() => root.Hero.HasWeaponInHand && root.Hero.WeaponId == CombatWeaponId.Pistol && !root.Hero.IsSwitchingWeapon,
                    "Restore the prior weapon after the deliberate disable fixture.");
                Assert.That(root.Inventory.Open(), Is.True);
                Assert.That(root.Inventory.UseSelected(), Is.True, "Cancelled preparation leaves the vial usable.");
                Assert.That(root.Inventory.IsOpen, Is.False);
                ForceHawthornFocus(root);
                yield return AwaitHawthorn(() => drink.CompletedSips == 1, "The automatic medicine swallow did not commit.");
                Assert.That(root.Hero.State.Health, Is.EqualTo(70f).Within(.001f));
                Assert.That(GameSessionState.IntoxicationLevel, Is.EqualTo(40));
                Assert.That(GameSessionState.HeroCorruptionDose, Is.EqualTo(40));
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.HawthornTincture), Is.Zero);
                HawthornHealingAudio sound = HawthornHealingAudio.Current;
                Assert.That(sound, Is.Not.Null);
                Assert.That(sound.IsActive && sound.TriggerCount == 1, Is.True);
                float soundElapsed = sound.ElapsedActiveSeconds;
                sound.Advance(1f, true);
                Assert.That(sound.ElapsedActiveSeconds, Is.EqualTo(soundElapsed));
                sound.Advance(2f, false);
                Assert.That(sound.IsActive, Is.False);
                yield return new WaitForEndOfFrame();
                Assert.That(drink.MouthContactError, Is.LessThan(.012f));
                Camera camera = root.CameraFollow.Camera;
                Vector3 lip = drink.Bottle.Mouth.position;
                Vector3 eye = lip + root.Hero.transform.forward * .65f + root.Hero.transform.right * .35f + Vector3.up * .08f;
                root.CameraFollow.SetFixedPose(eye, Quaternion.LookRotation(lip - eye), 58f);
                yield return CaptureFishUi("hawthorn-03-mouth-contact");
                yield return AwaitHawthorn(() => root.HealthHudOpacity > .95f, "Healing must reveal the normal HP meter.");
                yield return CaptureHawthornHealthHud(root, "hawthorn-04-hp-after", .7f);
                yield return AwaitHawthorn(() => drink.Phase == PlayerVodkaBottleController.BottlePhase.Recoiling,
                    "The foul swallow needs its short same-rig head reaction.");
                Transform head = ((Player3DCharacterPresentation)root.Player.Visual).Registry.Anchors.Head;
                Quaternion firstHead = head.localRotation;
                float headSpan = 0f;
                bool reactionCaptured = false;
                while (drink.Phase == PlayerVodkaBottleController.BottlePhase.Recoiling)
                {
                    yield return new WaitForEndOfFrame();
                    headSpan = Mathf.Max(headSpan, Quaternion.Angle(firstHead, head.localRotation));
                    if (!reactionCaptured && headSpan > 8f)
                    { yield return CaptureFishUi("hawthorn-05-head-reaction"); reactionCaptured = true; }
                    yield return null;
                }
                Assert.That(headSpan, Is.GreaterThan(8f), "The actual head bones must move through the reaction.");
                yield return AwaitHawthorn(() => drink.Phase == PlayerVodkaBottleController.BottlePhase.Hidden,
                    "The used vial must leave the hand after the short reaction.", () =>
                        Assert.That(drink.Phase, Is.Not.EqualTo(PlayerVodkaBottleController.BottlePhase.CheckingEmpty)));
                Assert.That(drink.CompletedSips, Is.EqualTo(1));
                yield return AwaitHawthorn(() => root.Hero.HasWeaponInHand && root.Hero.WeaponId == CombatWeaponId.Pistol && !root.Hero.IsSwitchingWeapon,
                    "The same prior pistol must return after the vial leaves the hand.");
                Assert.That(root.Hero.CarriedWeapon(CombatWeaponId.Pistol), Is.SameAs(previousWeaponModel));
                Assert.That(previousFirearm.Rounds, Is.EqualTo(previousRounds));
                Assert.That(previousFirearm.ReserveRounds, Is.EqualTo(previousReserve));
                root.AdvanceHudPresentation(5f);
                yield return CaptureHawthornHealthHud(root, "hawthorn-05-persistent-hp", .7f);
                Assert.That(root.Inventory.Open(), Is.True);
                Assert.That(root.Inventory.Health, Is.EqualTo(70f));
                yield return null;
                yield return CaptureFishUi("hawthorn-05-inventory-health-after");
                Assert.That(root.Inventory.IsOpen, Is.True);
                Assert.That(root.Inventory.Close(), Is.True);

                root.CameraFollow.ClearFixedPose();
                root.CameraFollow.Snap();
                ForceHawthornFocus(root);
                HawthornHealingPulse.Trigger(camera);
                yield return AwaitHawthorn(() => FindHawthornHistoryFeature(false) != null,
                    "An enabled gameplay lens must capture the held scene after the initial zero-envelope frame.");
                SceneCompositeRendererFeature feature = FindHawthornHistoryFeature();
                Assert.That(feature.DebugHawthornSnapshotValid, Is.True);
                Assert.That(feature.DebugHawthornSnapshotTexture.width, Is.LessThanOrEqualTo(960));
                Assert.That(feature.DebugHawthornSnapshotTexture.height, Is.LessThanOrEqualTo(540));
                HawthornHealingPulse.Advance(.18f, false);
                yield return null;
                yield return CaptureFishUi("hawthorn-06-red-peak");
                int captures = feature.DebugHawthornSnapshotCount;
                float heldTime = HawthornHealingPulse.ForCamera(camera).w;
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    for (int frame = 0; frame < 3; frame++) yield return null;
                    Assert.That(feature.DebugHawthornSnapshotCount, Is.EqualTo(captures));
                    Assert.That(HawthornHealingPulse.ForCamera(camera).w, Is.EqualTo(heldTime));
                }
                float advance = Mathf.Max(0f, 2.15f - HawthornHealingPulse.ForCamera(camera).w);
                HawthornHealingPulse.Advance(advance, false);
                yield return null;
                yield return new WaitForEndOfFrame();
                Assert.That(feature.DebugHawthornSnapshotCount, Is.GreaterThan(captures), "A later interval captures a new live scene.");
                RenderTexture heldTexture = feature.DebugHawthornSnapshotTexture;
                int heldCount = feature.DebugHawthornSnapshotCount;
                ulong heldPixels = HashHawthornSnapshot(heldTexture);
                HawthornHealingPulse.Advance(.16f, false);
                Vector3 heldEye = camera.transform.position;
                Quaternion heldRotation = camera.transform.rotation;
                root.CameraFollow.SetFixedPose(heldEye + camera.transform.right * .14f,
                    heldRotation * Quaternion.Euler(0f, 2f, 0f), camera.fieldOfView);
                yield return null;
                yield return new WaitForEndOfFrame();
                Assert.That(feature.DebugHawthornAfterimageStrength, Is.GreaterThan(.1f));
                Assert.That(feature.DebugHawthornSnapshotCount, Is.EqualTo(heldCount));
                Assert.That(HashHawthornSnapshot(heldTexture), Is.EqualTo(heldPixels), "The overlay really holds older pixels while the live camera moves.");
                yield return CaptureFishUi("hawthorn-07-frozen-frame-tail");
                HawthornHealingPulse.Advance(4.1f, false);
                Assert.That(HawthornHealingPulse.ForCamera(camera), Is.EqualTo(Vector4.zero));
                Assert.That(feature.DebugHawthornSnapshotTexture, Is.Null, "Expired history releases its bounded texture.");
                yield return CaptureFishUi("hawthorn-08-after");
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.HawthornTincture), Is.Zero);
                Assert.That(root.Hero.State.Health, Is.EqualTo(70f).Within(.001f));
                // Exercise the distinct ordinary-drink return path with a partial, opened bottle.
                root.CameraFollow.ClearFixedPose();
                GameSessionState.ResetDrinkingState();
                Assert.That(GameSessionState.TryAddInventoryItem(InventoryItemId.WineBottle), Is.True);
                Assert.That(GameSessionState.TrySetInventoryItemEquipped(InventoryItemId.WineBottle, true), Is.True);
                yield return AwaitHawthorn(() => drink.ItemId == InventoryItemId.WineBottle && drink.Phase == PlayerVodkaBottleController.BottlePhase.Carrying,
                    "Prepare an ordinary bottle as the previous hand item.");
                Assert.That(drink.TrySip(), Is.True);
                yield return AwaitHawthorn(() => drink.CompletedSips == 2 && drink.Phase == PlayerVodkaBottleController.BottlePhase.Carrying,
                    "Preserve a real partially drunk bottle.");
                int wineId = GameSessionState.DrinkContainerId(InventoryItemId.WineBottle);
                int wineMl = GameSessionState.DrinkRemainingMl(InventoryItemId.WineBottle);
                bool wineOpened = GameSessionState.DrinkOpened(InventoryItemId.WineBottle);
                Assert.That(wineMl, Is.LessThan(750));
                GameSessionState.ResetDrinkingState();
                root.Hero.State.RestoreVitals(40f, root.Hero.State.Settings.MaxStamina);
                Assert.That(GameSessionState.TryAddInventoryItem(InventoryItemId.HawthornTincture), Is.True);
                Assert.That(root.Inventory.Open(), Is.True);
                SelectHawthornTestInventoryItem(root.Inventory, InventoryItemId.HawthornTincture);
                Assert.That(root.Inventory.UseSelected(), Is.True);
                Assert.That(root.Inventory.IsOpen, Is.False);
                yield return AwaitHawthorn(() => drink.CompletedSips == 3 && drink.ItemId == InventoryItemId.WineBottle &&
                    drink.Phase == PlayerVodkaBottleController.BottlePhase.Carrying,
                    "The prior ordinary bottle must return without an extra sip.");
                Assert.That(GameSessionState.DrinkContainerId(InventoryItemId.WineBottle), Is.EqualTo(wineId));
                Assert.That(GameSessionState.DrinkRemainingMl(InventoryItemId.WineBottle), Is.EqualTo(wineMl));
                Assert.That(GameSessionState.DrinkOpened(InventoryItemId.WineBottle), Is.EqualTo(wineOpened));
                Assert.That(GameSessionState.IsInventoryItemEquipped(InventoryItemId.WineBottle), Is.True);
                yield return CaptureFishUi("hawthorn-09-previous-bottle-returned");
                Assert.That(root.Hero.State.Heal(30f), Is.EqualTo(30f));
                root.AdvanceHudPresentation(.2f);
                root.AdvanceHudPresentation(3.5f);
                Assert.That(root.HealthHudOpacity, Is.Zero, "At full health the ordinary HUD timeout still fades the bar.");
                drink.enabled = false;

            }
            finally
            {
                if (root != null) { root.AutomaticSimulation = false; root.Inventory?.Close(); }
                HawthornHealingPulse.Clear();
                if (keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (mouse.added) InputSystem.RemoveDevice(mouse);
                input.TearDown();
                Time.captureDeltaTime = previousDelta;
                Application.runInBackground = previousBackground;
                GraphicsEffectsSettings.IntoxicationLensFxEnabled = previousLens;
                CombatTestRoot.WarehouseSeedForTests = null;
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.UseDockerPopulationForTests = false;
                GameSessionState.BeginNewGame();
            }
        }

        private static void SelectHawthornTestInventoryItem(InventoryController inventory, InventoryItemId id)
        {
            int selected = -1;
            for (int index = 0; index < GameSessionState.InventoryItems.Count; index++)
                if (GameSessionState.InventoryItems[index].ItemId == id) selected = index;
            Assert.That(selected, Is.GreaterThanOrEqualTo(0));
            if (inventory.SelectedItemIndex != selected) Assert.That(inventory.SelectItem(selected), Is.True);
        }

        private static void ForceHawthornFocus(CombatTestRoot root)
        { root.SendMessage("OnApplicationFocus", true); GameInput.HandleApplicationFocus(true); }

        private static SceneCompositeRendererFeature FindHawthornHistoryFeature(bool required = true)
        {
            foreach (SceneCompositeRendererFeature feature in Resources.FindObjectsOfTypeAll<SceneCompositeRendererFeature>())
                if (feature != null && feature.isActive && feature.DebugHawthornSnapshotValid) return feature;
            if (required) Assert.Fail("The gameplay camera must capture its bounded world afterimage.");
            return null;
        }

        private static ulong HashHawthornSnapshot(RenderTexture texture)
        {
            RenderTexture previous = RenderTexture.active;
            var pixels = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(texture.width / 2 - 32, texture.height / 2 - 32, 64, 64), 0, 0);
                pixels.Apply();
                ulong hash = 14695981039346656037UL;
                foreach (Color32 value in pixels.GetPixels32())
                    unchecked { hash = (hash ^ value.r) * 1099511628211UL; hash = (hash ^ value.g) * 1099511628211UL;
                        hash = (hash ^ value.b) * 1099511628211UL; }
                return hash;
            }
            finally { RenderTexture.active = previous; Object.Destroy(pixels); }
        }

        private static IEnumerator AwaitHawthorn(Func<bool> ready, string message, Action eachFrame = null, Func<string> details = null)
        {
            // Bottle actions follow active real time. A frame cap can expire
            // before a short action when Game View runs faster than real time.
            double deadline = Time.realtimeSinceStartupAsDouble + 15d;
            while (!ready() && Time.realtimeSinceStartupAsDouble < deadline)
            { eachFrame?.Invoke(); yield return null; }
            bool satisfied = ready();
            Assert.That(satisfied, Is.True, !satisfied && details != null ? message + " " + details() : message);
        }

        private static IEnumerator CaptureHawthornHealthHud(CombatTestRoot root, string name, float expectedFraction)
        {
            yield return new WaitForEndOfFrame();
            Assert.That(root.HealthHudOpacity, Is.GreaterThan(.95f), "The real health meter must be visible during healing.");
            Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                var canvas = RetroUiTheme.CalculateCanvas(frame.width, frame.height);
                var layout = new CombatHudLayout(canvas, root.IsLaboratory, false);
                Rect meter = layout.Health;
                Rect track = canvas.LogicalToScreen(new Rect(meter.x + 4f, meter.yMax - 5f, meter.width - 8f, 3f));
                int y = Mathf.Clamp(frame.height - 1 - Mathf.RoundToInt(track.center.y), 0, frame.height - 1);
                float At(float fraction) => frame.GetPixel(Mathf.Clamp(Mathf.RoundToInt(track.x + track.width * fraction),
                    0, frame.width - 1), y).grayscale;
                float filled = At(.15f), empty = At(.9f);
                Assert.That(filled - empty, Is.GreaterThan(.15f), "The drawn bar needs distinct filled and empty segments.");
                float threshold = (filled + empty) * .5f;
                int bright = 0;
                for (int index = 0; index < 100; index++)
                    if (At((index + .5f) / 100f) > threshold) bright++;
                Assert.That(bright / 100f, Is.EqualTo(expectedFraction).Within(.035f),
                    "The captured Game View HP fill must reflect the authoritative health value.");
                string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest);
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), frame.EncodeToPNG());
            }
            finally { Object.Destroy(frame); }
        }

        private static void AssertHawthornPureContracts()
        {
            foreach (int seed in new[] { 0, 37, -42 })
            {
                WarehouseArenaPlan plan = WarehouseArenaPlanner.Create(seed), repeated = WarehouseArenaPlanner.Create(seed);
                Assert.That(plan.TinctureAnchors, Is.EqualTo(repeated.TinctureAnchors));
                Assert.That(plan.AmmoAnchors, Is.EqualTo(repeated.AmmoAnchors));
                Assert.That(plan.AmmoAnchors.Count, Is.EqualTo(12));
                Assert.That(plan.TinctureAnchors.Count, Is.EqualTo(3));
                for (int floor = 0; floor < 3; floor++)
                {
                    Assert.That(plan.TinctureAnchors[floor].y,
                        Is.EqualTo(floor * WarehouseArenaPlan.LevelRise + WarehouseLockerPlacement.ShelfHeight).Within(.001f));
                    foreach (Vector3 ammo in plan.AmmoAnchors)
                        Assert.That(Vector3.Distance(ammo, plan.TinctureAnchors[floor]), Is.GreaterThan(.01f));
                }
            }
            var actor = new MeleeCombatant();
            actor.RestoreVitals(40f, 72f);
            Assert.That(actor.RequestAttack(), Is.True);
            MeleePhase phase = actor.Phase;
            Assert.That(actor.Heal(30f), Is.EqualTo(30f));
            Assert.That(actor.Phase, Is.EqualTo(phase));
            Assert.That(actor.Stamina, Is.EqualTo(72f));
            Assert.That(actor.Heal(float.MaxValue), Is.EqualTo(30f));
            Assert.That(actor.CanHeal, Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => actor.Heal(float.NaN));

            GameSessionState.BeginNewGame();
            Assert.That(GameSessionState.TryAddInventoryItem(InventoryItemId.HawthornTincture, 2), Is.True);
            object useOwner = new object(), otherOwner = new object();
            Assert.That(GameSessionState.TrySetInventoryItemEquipped(InventoryItemId.HawthornTincture, true), Is.False);
            Assert.That(GameSessionState.TryQueueHawthornUse(useOwner, actor), Is.False,
                "Full health must refuse without spending a bottle.");
            Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.HawthornTincture), Is.EqualTo(2));
            actor.RestoreVitals(85f, 72f);
            Assert.That(GameSessionState.TryQueueHawthornUse(useOwner, actor), Is.True);
            int id = GameSessionState.DrinkContainerId(InventoryItemId.HawthornTincture);
            Assert.That(GameSessionState.TryQueueHawthornUse(otherOwner, actor), Is.False);
            Assert.That(GameSessionState.StartHawthornUse(otherOwner, actor), Is.False);
            Assert.That(GameSessionState.TryDrinkContainerSip(InventoryItemId.HawthornTincture, id, 50, out _, actor, useOwner), Is.False);
            Assert.That(GameSessionState.StartHawthornUse(useOwner, actor), Is.True);
            GameSessionState.UpdateDrinkingProgress(100, DrinkId.Vodka, 1);
            Assert.That(GameSessionState.TryDrinkContainerSip(InventoryItemId.HawthornTincture, id, 50, out var receipt, actor, useOwner), Is.True);
            Assert.That(actor.Health, Is.EqualTo(100f));
            Assert.That(actor.Stamina, Is.EqualTo(72f));
            Assert.That(receipt.AlcoholDose, Is.EqualTo(40));
            Assert.That(GameSessionState.IntoxicationLevel, Is.EqualTo(100));
            Assert.That(GameSessionState.HeroCorruptionDose, Is.EqualTo(40));
            Assert.That(GameSessionState.TryDrinkContainerSip(InventoryItemId.HawthornTincture, id, 50, out _, actor, useOwner), Is.False);
            actor.ReceiveProjectileHit(1000f);
            Assert.That(actor.CanHeal || actor.Heal(30f) > 0f, Is.False);

            var pulse = new HawthornHealingPulseTimeline();
            pulse.Trigger(); pulse.Advance(.25f, false);
            Vector4 peak = pulse.Sample(true);
            Assert.That(peak.x, Is.GreaterThan(.9f));
            pulse.Advance(3f, true);
            Assert.That(pulse.Sample(true), Is.EqualTo(peak));
            Assert.That(pulse.Sample(false).z, Is.Zero);
            pulse.Advance(1.75f, false);
            Assert.That(pulse.Active, Is.True, "The new afterimage tail continues beyond two seconds.");
            pulse.Advance(2f, false);
            Assert.That(pulse.Active, Is.False);
            Assert.That(pulse.Sample(true), Is.EqualTo(Vector4.zero));
            float[] pcm = HawthornHealingSoundSynthesis.GenerateSamples(22050);
            Assert.That(pcm.Length, Is.EqualTo(44100));
            Assert.That(pcm[0], Is.Zero);
            Assert.That(pcm[pcm.Length - 1], Is.Zero);
            float peakAmplitude = 0f;
            foreach (float sample in pcm)
            {
                Assert.That(float.IsNaN(sample) || float.IsInfinity(sample), Is.False);
                peakAmplitude = Mathf.Max(peakAmplitude, Mathf.Abs(sample));
            }
            Assert.That(peakAmplitude * RetroSfxLibrary.GetDefinition(HawthornHealingAudio.CueId).Volume,
                Is.GreaterThan(0f).And.LessThan(.18f), "The companion cue stays quiet at its authored source gain.");
            GameSessionState.BeginNewGame();
        }
    }
}
