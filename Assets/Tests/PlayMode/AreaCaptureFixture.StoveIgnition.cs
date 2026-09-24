using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Stove fuel atomicity, three strikes, warmth and the actual first-person inventory screen.")]
        [PrebuildSetup(typeof(VillageArtAssetsSetup))]
        public IEnumerator AlpineVillageStoveIgnition()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            Assert.That(GameSessionState.TrySetDebugGameDay(2), Is.True);
            GameSessionState.AdvanceGameTime((float)(100f / GameTimeState.GameMinutesPerRealSecond));
            AlpineVillageRoot root = null;
            yield return Capture(SceneIds.AlpineVillage, () =>
            {
                root = Object.FindAnyObjectByType<AlpineVillageRoot>();
                return root != null && root.IsInitialized ? root : null;
            }, () => new[]
            {
                Shot.At("stove-ignition-00-cold",
                    root.Stove.Plan.CameraDock.position,
                    root.Stove.Plan.CameraTarget, 58f)
            });

            LodgeStoveInteraction stove = root.Stove;
            Assert.That(stove, Is.Not.Null);
            Assert.That(LodgeStoveSessionState.Stage, Is.EqualTo(LodgeStoveStage.Empty));
            Assert.That(stove.Fire.Strength, Is.Zero);
            Assert.That(stove.Fire.FireLight.enabled, Is.False);
            Assert.That(stove.ProvidesWarmth(stove.Plan.EntryPose.RootPosition), Is.False);
            Assert.That(GameSessionState.TryAddInventoryItem(InventoryItemId.FirewoodLog), Is.True);
            int lighterCount = GameSessionState.GetInventoryItemCount(InventoryItemId.Lighter);
            Quaternion closedDoor = stove.Plan.Door.localRotation;
            SkinnedMeshRenderer[] heroMeshes = root.Player.GameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var heroVisibility = new bool[heroMeshes.Length];
            for (int index = 0; index < heroMeshes.Length; index++) heroVisibility[index] = heroMeshes[index].enabled;
            bool contactShadowEnabled = root.Player.ContactShadow.enabled;
            var input = new InputTestFixture();
            input.Setup();
            try
            {
                Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
                yield return OpenStoveForCapture(root);
                Assert.That(stove.InventoryView.Hint, Is.EqualTo(LocalizationService.Get("stove.hint.log")));
                Assert.That(stove.InventoryView.CanUse(InventoryItemId.FirewoodLog), Is.True);
                Assert.That(stove.InventoryView.CanUse(InventoryItemId.Lighter), Is.False);
                Assert.That(stove.InventoryView.CanUse(InventoryItemId.ApartmentKeys), Is.False);
                Assert.That(root.Inventory.IsOpen, Is.False, "The inset must keep the contextual owner.");
                Assert.That(GameTimeScaleRuntime.IsPaused, Is.False);
                Assert.That(root.Player.PresentationVisibility.IsHidden, Is.False,
                    "The prop-only first-person action leaves the world hero visible without a hand subset.");
                Assert.That(Vector3.Distance(root.CameraFollow.FixedBasePosition,
                    stove.Plan.CameraDock.position), Is.LessThan(.02f));
                yield return CaptureWoodpileScreen("stove-ignition-01-inventory");

                // Preparation and a partial reach must not spend the only log.
                Assert.That(stove.TryUseItem(InventoryItemId.FirewoodLog), Is.True);
                stove.AdvanceInteraction(LodgeStovePlan.PlaceSeconds * .2f);
                stove.CancelInteraction();
                yield return WaitForStovePhase(stove, LodgeStovePhase.Closed);
                Assert.That(LodgeStoveSessionState.Stage, Is.EqualTo(LodgeStoveStage.Empty));
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.FirewoodLog), Is.EqualTo(1));
                AssertStoveReleased(root, closedDoor, heroMeshes, heroVisibility, contactShadowEnabled);

                yield return OpenStoveForCapture(root);
                // Use the actual focused inventory binding, including its next-frame guard.
                input.Press(keyboard.enterKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.enterKey, queueEventOnly: true);
                Assert.That(stove.Phase, Is.EqualTo(LodgeStovePhase.PlacingLog));
                Assert.That(stove.TryUseItem(InventoryItemId.FirewoodLog), Is.False);
                yield return WaitForStovePhase(stove, LodgeStovePhase.Browsing);
                Assert.That(LodgeStoveSessionState.Stage, Is.EqualTo(LodgeStoveStage.LogPlaced));
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.FirewoodLog), Is.Zero);
                Assert.That(stove.LogModel, Is.Not.Null);
                Assert.That(stove.LogModel.gameObject.activeInHierarchy, Is.True);
                Assert.That(Vector3.Distance(stove.LogModel.position, stove.Plan.LogDock.position), Is.LessThan(.015f));
                Assert.That(stove.InventoryView.Hint, Is.EqualTo(LocalizationService.Get("stove.hint.ignite")));
                Assert.That(stove.InventoryView.CanUse(InventoryItemId.Lighter), Is.True);
                yield return CaptureWoodpileScreen("stove-ignition-02-log-placed");

                Assert.That(stove.TryUseItem(InventoryItemId.Lighter), Is.True);
                stove.AdvanceInteraction(LodgeStovePlan.FirstClickSeconds + .01f);
                AssertUnlitStoveStrike(stove, 1);
                stove.AdvanceInteraction(LodgeStovePlan.SecondClickSeconds - LodgeStovePlan.FirstClickSeconds);
                AssertUnlitStoveStrike(stove, 2);
                yield return null;
                // A synchronous world capture preserves this exact failed-strike pose.
                Assert.That(Quaternion.Angle(stove.LighterModel.Find("LighterLidHinge").localRotation,
                    Quaternion.Euler(0f, 0f, 110f)), Is.LessThan(.1f));
                CaptureCurrentCamera(Camera.main, SceneIds.AlpineVillage, "stove-ignition-03-lighter");
                stove.RequestExit();
                yield return WaitForStovePhase(stove, LodgeStovePhase.Closed);
                Assert.That(LodgeStoveSessionState.Stage, Is.EqualTo(LodgeStoveStage.LogPlaced),
                    "Leaving after failed strikes must leave the installed fuel unlit.");
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.FirewoodLog), Is.Zero);
                AssertStoveReleased(root, closedDoor, heroMeshes, heroVisibility, contactShadowEnabled);

                yield return OpenStoveForCapture(root);
                Assert.That(stove.InventoryView.CanUse(InventoryItemId.Lighter), Is.True);
                Assert.That(stove.TryUseItem(InventoryItemId.Lighter), Is.True);
                // A hitch must not collapse the dry attempts into three simultaneous clicks.
                stove.AdvanceInteraction(30f);
                AssertUnlitStoveStrike(stove, 1);
                yield return null;
                stove.AdvanceInteraction(LodgeStovePlan.SecondClickSeconds - LodgeStovePlan.FirstClickSeconds);
                AssertUnlitStoveStrike(stove, 2);
                yield return null;
                stove.AdvanceInteraction(LodgeStovePlan.ThirdClickSeconds - LodgeStovePlan.SecondClickSeconds);
                Assert.That(stove.Fire.ClickCount, Is.EqualTo(3));
                Assert.That(LodgeStoveSessionState.IsBurning, Is.True);
                Assert.That(stove.Fire.Strength, Is.Zero, "The third strike starts growth rather than a full-strength fire.");
                Assert.That(stove.TryUseItem(InventoryItemId.Lighter), Is.False);
                Assert.That(LodgeStoveSessionState.TryIgnite(), Is.False);
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.Lighter), Is.EqualTo(lighterCount));
                yield return WaitForStovePhase(stove, LodgeStovePhase.Browsing);
                float fireDeadline = Time.realtimeSinceStartup + LodgeStoveFire.GrowSeconds + 2f;
                while (stove.Fire.Strength < .99f && Time.realtimeSinceStartup < fireDeadline) yield return null;
                Assert.That(stove.Fire.Strength, Is.GreaterThanOrEqualTo(.99f));
                Assert.That(stove.Fire.FireLight.enabled, Is.True);
                Assert.That(stove.Fire.ClickCount, Is.EqualTo(3));
                Assert.That(stove.InventoryView.CanUse(InventoryItemId.Lighter), Is.False);
                yield return CaptureWoodpileScreen("stove-ignition-04-burning");

                Vector3 warmPoint = stove.Plan.EntryPose.RootPosition;
                Assert.That(stove.ProvidesWarmth(warmPoint), Is.True);
                Assert.That(stove.ProvidesWarmth(warmPoint + stove.Plan.Lodge.right * 10f), Is.False);
                Assert.That(stove.ProvidesWarmth(warmPoint - Vector3.up * 5f), Is.False,
                    "Horizontal range cannot warm a point below the room.");
                AlpineColdExposureDriver cold = Object.FindAnyObjectByType<AlpineColdExposureDriver>();
                Assert.That(cold, Is.Not.Null);
                Assert.That(cold.IsSheltered, Is.True, "The live cold driver must read stove warmth.");
                cold.Model.Step(AlpineColdExposureModel.FullExposureSeconds, false);
                float frostBefore = cold.Model.FrostAmount;
                yield return null;
                yield return null;
                Assert.That(cold.Model.FrostAmount, Is.LessThan(frostBefore), "Nearby fire must thaw real session frost.");

                input.Press(keyboard.escapeKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.escapeKey, queueEventOnly: true);
                yield return WaitForStovePhase(stove, LodgeStovePhase.Closed);
                Assert.That(PauseMenuController.IsAnyPaused, Is.False, "Escape belongs to the stove until it releases.");
                AssertStoveReleased(root, closedDoor, heroMeshes, heroVisibility, contactShadowEnabled);
                Assert.That(stove.Fire.FireLight.enabled, Is.True, "Closing the door does not extinguish the fire.");
                yield return CaptureWoodpileScreen("stove-ignition-05-restored");

                // A later visit cannot accept a second fuel item; disable still restores ownership.
                Assert.That(GameSessionState.TryAddInventoryItem(InventoryItemId.FirewoodLog), Is.True);
                yield return OpenStoveForCapture(root);
                Assert.That(stove.TryUseItem(InventoryItemId.FirewoodLog), Is.False);
                Assert.That(stove.TryUseItem(InventoryItemId.Lighter), Is.False);
                stove.enabled = false;
                yield return null;
                AssertStoveReleased(root, closedDoor, heroMeshes, heroVisibility, contactShadowEnabled);
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.FirewoodLog), Is.EqualTo(1));
                Assert.That(LodgeStoveSessionState.IsBurning, Is.True);
                stove.enabled = true;
                Assert.That(root.Inventory.Open(), Is.True);
                for (int index = 0; index < GameSessionState.InventoryItems.Count; index++)
                    if (GameSessionState.InventoryItems[index].ItemId == InventoryItemId.Lighter)
                        root.Inventory.SelectItem(index);
                Assert.That(root.Inventory.ExamineSelected(), Is.True);
                Transform preview = root.Inventory.View.PreviewRenderer.ModelRoot;
                Transform lid = preview.Find("LighterLidHinge");
                Assert.That(lid, Is.Not.Null, "Inventory uses the same authored flip-top lighter.");
                Assert.That(Quaternion.Angle(lid.localRotation, Quaternion.identity), Is.LessThan(.01f));
                Assert.That(preview.Find("LighterFlame").GetComponent<Renderer>().enabled, Is.False);
                yield return CaptureWoodpileScreen("stove-ignition-06-inventory-lighter");
                root.Inventory.Close();
                GameSessionState.BeginNewGame();
                Assert.That(LodgeStoveSessionState.Stage, Is.EqualTo(LodgeStoveStage.Empty));
            }
            finally
            {
                if (stove != null) stove.CancelInteraction();
                input.TearDown();
            }
        }

        private static IEnumerator OpenStoveForCapture(AlpineVillageRoot root)
        {
            LodgeStoveInteraction stove = root.Stove;
            root.Player.Motor.Teleport(stove.Plan.EntryPose.RootPosition);
            root.Player.GameObject.transform.rotation = stove.Plan.EntryPose.RootRotation;
            Physics.SyncTransforms();
            yield return null;
            yield return null;
            Assert.That(stove.BeginInteraction(), Is.True);
            Assert.That(stove.OwnsInteraction, Is.True);
            yield return WaitForStovePhase(stove, LodgeStovePhase.Browsing);
            Assert.That(stove.DoorOpenAmount, Is.EqualTo(1f).Within(.001f));
            Assert.That(stove.InventoryView.IsOpen, Is.True);
            Assert.That(root.Player.Motor.InputEnabled, Is.False);
            Assert.That(root.Player.Interactor.InputEnabled, Is.False);
        }

        private static IEnumerator WaitForStovePhase(LodgeStoveInteraction stove,
            LodgeStovePhase phase)
        {
            float deadline = Time.realtimeSinceStartup + 5f;
            while (stove.Phase != phase && Time.realtimeSinceStartup < deadline)
            {
                stove.AdvanceInteraction(.2f);
                // Shared neutral/terminal pose sampling still gets real rendered frames.
                yield return null;
            }
            Assert.That(stove.Phase, Is.EqualTo(phase), "Stove phase deadline.");
            yield return null;
        }

        private static void AssertUnlitStoveStrike(LodgeStoveInteraction stove, int expectedClicks)
        {
            Assert.That(stove.Fire.ClickCount, Is.EqualTo(expectedClicks));
            Assert.That(LodgeStoveSessionState.Stage, Is.EqualTo(LodgeStoveStage.LogPlaced));
            Assert.That(stove.Fire.Strength, Is.Zero);
            Assert.That(stove.Fire.FireLight.enabled, Is.False);
        }

        private static void AssertStoveReleased(AlpineVillageRoot root, Quaternion closedDoor,
            SkinnedMeshRenderer[] heroMeshes, bool[] heroVisibility, bool contactShadowEnabled)
        {
            LodgeStoveInteraction stove = root.Stove;
            Assert.That(stove.OwnsInteraction, Is.False);
            Assert.That(stove.InventoryView.IsOpen, Is.False);
            Assert.That(stove.DoorOpenAmount, Is.Zero.Within(.001f));
            Assert.That(Quaternion.Angle(stove.Plan.Door.localRotation, closedDoor), Is.LessThan(.01f));
            Assert.That(root.CameraFollow.FixedPoseActive, Is.False);
            Assert.That(root.CameraFollow.OrbitInputEnabled, Is.True);
            Assert.That(root.Player.Motor.InputEnabled, Is.True);
            Assert.That(root.Player.Interactor.InputEnabled, Is.True);
            Assert.That(root.Player.PresentationVisibility.IsHidden, Is.False);
            Assert.That(root.Player.ContactShadow.enabled, Is.EqualTo(contactShadowEnabled));
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
            for (int index = 0; index < heroMeshes.Length; index++)
                Assert.That(heroMeshes[index].enabled, Is.EqualTo(heroVisibility[index]), heroMeshes[index].name);
        }
    }
}
