using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest]
        public IEnumerator LiveCity_StoresBelowMaximumAndReturnsAMixedBatchOnce()
        {
            bool focused = GameCursor.ApplicationFocused;
            var input = new InputTestFixture();
            input.Setup();
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                GameCursor.SetApplicationFocused(true);
                GameSessionState.TryStartGameTimeAt(12 * 60);
                yield return LoadProductionCity();
                var player = city.Player;
                var trade = Object.FindAnyObjectByType<CityContainerReturnInteraction>();
                var worker = Object.FindAnyObjectByType<CityGlassReturnWorker>();
                var site = CityGlassReturnSitePlan.ForLayout(city.Layout);
                var vessel = player.GameObject.GetComponent<PlayerVodkaBottleController>();
                var screen = WorldItemFoundScreen.For(player.Interactor);
                bool capture = Environment.GetEnvironmentVariable("BARGAME_CONTAINER_RETURN_FRAMES") == "1";

                player.Motor.Teleport(site.HeroDock + Vector3.up * PlayerFactory.GroundedRootOffset);
                Physics.SyncTransforms();
                yield return Until(() => trade && trade.CanInteract(player.Interactor) && worker.IsWorking, "reachable counter");
                CaptureSign("sign-day");

                int capacity = InventoryItemCatalog.Get(InventoryItemId.EmptyBeerCan).MaximumStack;
                Assert.That(GameSessionState.TryAddInventoryItem(InventoryItemId.EmptyBeerCan, capacity), Is.True);
                Assert.That(GameSessionState.TryAddInventoryItem(InventoryItemId.BeerCanLastSip), Is.True);
                Assert.That(GameSessionState.TrySetInventoryItemEquipped(InventoryItemId.BeerCanLastSip, true), Is.True);
                yield return Until(() => vessel.Phase == PlayerVodkaBottleController.BottlePhase.Carrying, "can drawn");
                Assert.That(vessel.TrySip(), Is.False, "full empty stack refuses the last sip");
                Assert.That(GameSessionState.DrinkRemainingMl(InventoryItemId.BeerCanLastSip), Is.EqualTo(50));
                Assert.That(GameSessionState.TryRemoveInventoryItem(InventoryItemId.EmptyBeerCan, capacity), Is.True);
                yield return Press(mouse.leftButton);
                yield return Until(() => GameSessionState.GetInventoryItemCount(InventoryItemId.EmptyBeerCan) == 1, "empty can stored");
                yield return Until(() => !vessel.OccupiesHand, "can visibly stowed");
                Assert.That(vessel.ThrownBottles, Is.Zero);

                Assert.That(GameSessionState.TryAddInventoryItem(InventoryItemId.WineBottleLastSip), Is.True);
                Assert.That(GameSessionState.TrySetInventoryItemEquipped(InventoryItemId.WineBottleLastSip, true), Is.True);
                yield return Until(() => vessel.Phase == PlayerVodkaBottleController.BottlePhase.Carrying, "wine drawn");
                GameSessionState.UpdateDrinkingProgress(99, DrinkId.RedWine, GameSessionState.DrinksConsumed);
                yield return Press(mouse.leftButton);
                yield return Until(() => vessel.ThrownBottles == 1, "at maximum the real bottle is thrown");
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.EmptyWineBottle), Is.Zero);
                GameSessionState.UpdateDrinkingProgress(0, DrinkId.RedWine, GameSessionState.DrinksConsumed);
                yield return Until(() => !vessel.OccupiesHand && trade.CanInteract(player.Interactor), "ready after throw");

                Assert.That(GameSessionState.TryAddInventoryItem(InventoryItemId.EmptyWineBottle, 2), Is.True);
                int cash = GameSessionState.CashBalance;
                yield return Press(keyboard.eKey);
                yield return Until(() => screen.IsShowing, "quote opened from E");
                Assert.That(screen.ActionLabel, Does.Contain("$5"));
                yield return Press(keyboard.escapeKey);
                yield return Until(() => !BarMinigameModalLock.IsAnyLocked, "quote canceled");
                Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash));
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.EmptyWineBottle), Is.EqualTo(2));

                yield return Press(keyboard.eKey);
                yield return Until(() => screen.IsShowing, "quote reopened");
                yield return Press(keyboard.eKey);
                yield return Until(() => GameSessionState.CashBalance == cash + 5, "one payout at the counter");
                if (capture) AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "ContainerReturn", "counter-handoff", 960, 540);
                yield return Until(() => !BarMinigameModalLock.IsAnyLocked, "counter releases input");
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.EmptyWineBottle), Is.Zero);
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.EmptyBeerCan), Is.Zero);
                yield return Until(() => !player.Interactor.PromptView.IsFeedbackVisible, "payout feedback ends");
                yield return Press(keyboard.eKey);
                Assert.That(screen.IsPresenting, Is.False);
                Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash + 5));
                yield return Until(() => !player.Interactor.PromptView.IsFeedbackVisible, "empty feedback ends");

                Assert.That(GameSessionState.TryAddInventoryItem(InventoryItemId.EmptyVodkaBottle), Is.True);
                yield return Press(keyboard.eKey);
                yield return Until(() => screen.IsShowing, "quote before closing");
                GameSessionState.AdvanceGameTime(11.2f * 60f * 4f);
                yield return Until(() => !BarMinigameModalLock.IsAnyLocked, "closing retracts the quote");
                CaptureSign("sign-night");
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.EmptyVodkaBottle), Is.EqualTo(1));
                Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash + 5));
                yield return Press(keyboard.eKey);
                Assert.That(screen.IsPresenting, Is.False);

                void CaptureSign(string name)
                {
                    if (!capture) return;
                    city.DayNight.ApplyCurrentTime(true);
                    var camera = city.Camera.transform;
                    var original = new Pose(camera.position, camera.rotation);
                    Vector3 eye = site.Origin + site.Rotation * new Vector3(-.5f, 1.64f, 5.4f);
                    Vector3 sign = site.Origin + site.Rotation * new Vector3(0f, 2.20f, 2.40f);
                    camera.SetPositionAndRotation(eye, Quaternion.LookRotation(sign - eye));
                    try { AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "ContainerReturn", name, 960, 540); }
                    finally { camera.SetPositionAndRotation(original.position, original.rotation); }
                }

                IEnumerator Press(ButtonControl button)
                {
                    button.device.MakeCurrent();
                    input.Press(button, queueEventOnly: true);
                    yield return null;
                    input.Release(button, queueEventOnly: true);
                    yield return null;
                }

                static IEnumerator Until(Func<bool> condition, string label)
                {
                    for (int i = 0; i < 420 && !condition(); i++) yield return null;
                    Assert.That(condition(), Is.True, label);
                }
            }
            finally
            {
                input.TearDown();
                GameCursor.SetApplicationFocused(focused);
            }
        }
    }
}
