using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest]
        public IEnumerator LiveCity_DumpsterFindReachesPawnshopAndPaysOnce()
        {
            var input = new InputTestFixture();
            input.Setup();
            var keyboard = InputSystem.AddDevice<Keyboard>();
            bool focused = GameCursor.ApplicationFocused;
            try
            {
                GameCursor.SetApplicationFocused(true);
                GameSessionState.TryStartGameTimeAt(12 * 60);
                yield return LoadProductionCity();
                int[] weights = { 10, 8, 6, 4, 2, 10 };
                var observed = Enumerable.Range(0, 40).Select(PawnshopFindCatalog.Roll)
                    .GroupBy(item => item).Select(group => group.Count()).ToArray();
                Assert.That(observed, Is.EqualTo(weights));
                var bins = Object.FindObjectsByType<CityStreetUtilityInteraction>()
                    .Where(candidate => candidate.Dock.Kind == CityStreetUtilityKind.Dumpster &&
                        !candidate.Dock.Id.StartsWith("ridge-", StringComparison.Ordinal)).ToArray();
                int day = GameSessionState.GameDayIndex;
                InventoryItemId Find(CityStreetUtilityInteraction candidate) => GameSessionState.DumpsterLoot(candidate.Dock.Id, day);
                var bin = bins.Where(candidate => PawnshopFindCatalog.TryGet(Find(candidate), out _))
                    .OrderBy(candidate => Find(candidate) == InventoryItemId.MechanicalWatch ? 0 : 1)
                    .ThenBy(candidate => candidate.Dock.Id, StringComparer.Ordinal).First();
                InventoryItemId item = Find(bin);
                Assert.That(PawnshopFindCatalog.TryGet(item, out var quote), Is.True);
                bool capture = Environment.GetEnvironmentVariable("BARGAME_PAWNSHOP_FIND_FRAMES") == "1";
                var player = city.Player;
                var dock = bin.Dock;
                player.Motor.Teleport(dock.Approach + Vector3.up * PlayerFactory.GroundedRootOffset);
                Physics.SyncTransforms();
                for (int i = 0; i < 5; i++) yield return null;
                Vector3 stand = dock.StandPosition + Vector3.up * PlayerFactory.GroundedRootOffset;
                bool stood = false;
                for (int i = 0; i < 105 && !stood; i++)
                {
                    stood = player.Motor.MoveTowardsSupportedInteractionPose(stand, Quaternion.LookRotation(dock.Facing), Time.deltaTime);
                    yield return null;
                }
                Assert.That(stood && bin.CanInteract(player.Interactor), Is.True, dock.Id);
                bin.Interact(player.Interactor);
                WorldItemFoundScreen screen = WorldItemFoundScreen.For(player.Interactor);
                yield return Until(() => screen.IsShowing, "found model in the shared pickup");
                Assert.That(screen.ActiveItemId, Is.EqualTo(item));
                if (capture) AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "PawnshopFinds", "dumpster-find", 960, 540);
                yield return Press(keyboard.eKey);
                yield return Until(() => player.Interactor.InputEnabled, "dumpster closes");
                Assert.That(GameSessionState.GetInventoryItemCount(item), Is.EqualTo(1));
                Assert.That(GameSessionState.GetDumpsterSearchState(dock.Id, day),
                    Is.EqualTo(GameSessionState.DumpsterSearchState.Depleted));

                // Collision and the real door path already have separate coverage.
                yield return SceneManager.LoadSceneAsync(SceneIds.PawnshopInterior, LoadSceneMode.Single);
                pilotScene = SceneManager.GetActiveScene();
                var interior = Object.FindAnyObjectByType<PawnshopInteriorRoot>();
                Assert.That(interior && interior.IsInitialized, Is.True);
                player = interior.Player;
                screen = WorldItemFoundScreen.For(player.Interactor);
                foreach (var offer in PawnshopFindCatalog.Offers)
                {
                    Transform preview = InventoryItemModelFactory.BuildPreviewModel(offer.Item, interior.World.Root);
                    var grip = PawnshopFindCatalog.FindAnchor(preview, "Grip");
                    Assert.That(WorldItemInspectionPresenter.CalculateWorldBounds(preview).SqrDistance(grip.position), Is.LessThan(.00001f), offer.Item.ToString());
                    Object.Destroy(preview.gameObject);
                }
                Assert.That(GameSessionState.TryAddInventoryItem(item), Is.True);
                int cash = GameSessionState.CashBalance;
                player.Motor.Teleport(interior.Layout.TradePosition + Vector3.up * PlayerFactory.GroundedRootOffset);
                Physics.SyncTransforms();
                yield return Until(() => ReferenceEquals(player.Interactor.ActiveInteractable, interior.Trade), "reachable counter");
                yield return Press(keyboard.eKey);
                Assert.That(interior.Trade.IsOpen, Is.True);
                Assert.That(interior.Trade.SelectSell(), Is.True);
                yield return Press(keyboard.eKey);
                yield return Until(() => screen.IsShowing, "sale quote");
                Assert.That(screen.ActionLabel, Does.Contain("$" + quote.Price));
                yield return Press(keyboard.escapeKey);
                yield return Until(() => !screen.IsPresenting, "quote dismissed");
                yield return null;
                yield return null;
                Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash));
                Assert.That(GameSessionState.GetInventoryItemCount(item), Is.EqualTo(2));

                yield return Press(keyboard.eKey);
                yield return Until(() => screen.IsShowing, "sale quote reopened");
                yield return Press(keyboard.eKey);
                yield return Until(() => GameSessionState.CashBalance == cash + quote.Price, "payout at handoff");
                if (capture) AreaCaptureFixture.CaptureCurrentCamera(interior.Camera, "PawnshopFinds", "handoff", 960, 540);
                yield return Press(keyboard.eKey);
                yield return Until(() => !interior.Trade.IsOpen && player.Interactor.InputEnabled, "sale releases input");
                Assert.That(GameSessionState.GetInventoryItemCount(item), Is.EqualTo(1));
                Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash + quote.Price));
                Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);

                IEnumerator Press(ButtonControl button)
                {
                    keyboard.MakeCurrent();
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
