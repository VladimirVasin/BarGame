using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class KioskMenuCaptureSetup : IPrebuildSetup
    {
        public const string CaptureEnvironment = "BAR_PROMENADE_KIOSK_MENU_CAPTURE";
        public void Setup()
        {
#if UNITY_EDITOR
            if (Environment.GetEnvironmentVariable(CaptureEnvironment) == "1")
                Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                    .GetMethod("FocusGameViewForCapture", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    internal static class CompactShelfMenuAssertions
    {
        internal static void Check(SupermarketShelfShopView view, bool hasAction, string descriptionKey = null)
        {
            view.GetPanelRects(out Rect card, out Rect balance);
            Assert.That(card.width, Is.InRange(180f, 320f));
            Assert.That(card.height, Is.InRange(40f, 105f));
            Assert.That(balance.width, Is.InRange(100f, 170f));
            Assert.That(balance.height, Is.InRange(16f, 28f));
            var canvas = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height);
            Rect viewport = canvas.ViewportRect;
            Assert.That(card.xMin, Is.EqualTo(viewport.xMin + 6f).Within(.1f));
            Assert.That(card.yMax, Is.EqualTo(viewport.yMax - 6f).Within(.1f));
            Assert.That(balance.xMax, Is.EqualTo(viewport.xMax - 6f).Within(.1f));
            Assert.That(balance.yMin, Is.EqualTo(viewport.yMin + 6f).Within(.1f));
            Assert.That(card.Overlaps(balance), Is.False);
            Assert.That((card.width * card.height + balance.width * balance.height) /
                (viewport.width * viewport.height), Is.LessThan(.15f),
                "The common shop panels must leave most of the actual world view clear.");
            bool Blocks(Vector2 logical)
            {
                Vector2 screen = canvas.LogicalToScreen(logical);
                return view.ContainsPointerBlockingWorldSelection(new Vector2(screen.x, canvas.ScreenHeight - screen.y));
            }
            Assert.That(Blocks(card.center), Is.True);
            Assert.That(Blocks(balance.center), Is.True);
            Assert.That(Blocks(card.min + Vector2.one), Is.True);
            Assert.That(Blocks(balance.max - Vector2.one), Is.True,
                "Actual viewport corner panels remain clickable outside the centered640 canvas.");
            Assert.That(Blocks(new Vector2(350f, 52f)), Is.False,
                "An empty part of the former full-width header must allow world selection.");
            Assert.That(Blocks(new Vector2(580f, 320f)), Is.False,
                "An empty part of the former full-width footer must allow world selection.");
            Assert.That(view.TryGetActionRects(out Rect confirm, out Rect exit), Is.True);
            bool Inside(Rect button) => button.width > 0f && button.height > 0f &&
                card.Contains(new Vector2(button.xMin + .1f, button.yMin + .1f)) &&
                card.Contains(new Vector2(button.xMax - .1f, button.yMax - .1f));
            Assert.That(Inside(exit), Is.True, "Exit remains inside the visible compact card.");
            Assert.That(Blocks(exit.center), Is.True);
            if (hasAction)
            {
                Assert.That(Inside(confirm), Is.True);
                Assert.That(confirm.Overlaps(exit), Is.False);
                Assert.That(Blocks(confirm.center), Is.True);
            }
            else Assert.That(confirm.width * confirm.height, Is.Zero, "A silent seller has no hidden confirmation hit region.");
            Assert.That(view.TryGetNavigationRects(out Rect left, out Rect right), Is.True);
            Assert.That(left.width, Is.LessThanOrEqualTo(24f));
            Assert.That(left.height, Is.LessThanOrEqualTo(30f));
            Assert.That(left.Overlaps(card) || left.Overlaps(balance) || right.Overlaps(card) || right.Overlaps(balance), Is.False);
            Assert.That(Blocks(left.center) && Blocks(right.center), Is.True);
            CheckDescription(view, descriptionKey);
        }

        internal static void CheckDescription(SupermarketShelfShopView view, string descriptionKey, bool requirePaint = false)
        {
            if (string.IsNullOrEmpty(descriptionKey)) return;
            Assert.That(view.TryGetDescriptionRect(out Rect rect), Is.True);
            var style = (GUIStyle)typeof(SupermarketShelfShopView).GetField("descriptionStyle",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(view);
            if (style == null)
            {
                Assert.That(requirePaint, Is.False, "The native UI frame must initialize the actual description style.");
                return;
            }
            Assert.That(style.wordWrap, Is.True, "Catalog descriptions must wrap inside the compact card.");
            float needed = style.CalcHeight(new GUIContent(LocalizationService.Get(descriptionKey)), rect.width);
            Assert.That(needed, Is.LessThanOrEqualTo(rect.height + .5f),
                descriptionKey + " needs its complete second line inside the actual draw rectangle.");
        }
    }

    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest, PrebuildSetup(typeof(KioskMenuCaptureSetup)), Timeout(240000)]
        public IEnumerator LiveCity_KioskMenuUsesOneEntryAndFourFocusedTargets()
        {
            var input = new InputTestFixture(); input.Setup();
            var keyboard = InputSystem.AddDevice<Keyboard>(); keyboard.MakeCurrent();
            var mouse = InputSystem.AddDevice<Mouse>();
            bool previousFocus = GameInput.MovementFocused;
            var cursorOwner = new object();
            bool capture = Environment.GetEnvironmentVariable(KioskMenuCaptureSetup.CaptureEnvironment) == "1";
            RidgeKioskMenuController activeMenu = null;
            WorldItemFoundScreen receipt = null;
            try
            {
                GameCursor.Capture(cursorOwner, CursorLockMode.Locked, false, 0);
                if (capture) Assert.That(Application.isBatchMode, Is.False, "The review includes the real menu UI.");
                yield return LoadProductionCity();
                GameInput.HandleApplicationFocus(true);
                var hero = city.Player.Interactor;
                var motor = city.Player.Motor;
                var follow = city.Camera.GetComponent<PlayerCameraFollow>();
                receipt = WorldItemFoundScreen.For(hero);
                Assert.That(receipt && receipt.IsInitialized, Is.True);
                if (!GameSessionState.IsGameTimeRunning) GameSessionState.TryStartGameTimeAt(720);
                city.Weather.enabled = false;
                follow.SetOrbitInputEnabled(false);
                var sites = CityKioskSites.ForLayout(city.Layout).Sites;
                Assert.That(sites.Count, Is.EqualTo(3));
                InventoryItemId?[] order = { null, InventoryItemId.PotatoPirozhok, InventoryItemId.BeerCan, InventoryItemId.VodkaBottle };
                int siteIndex = 0;
                foreach (var site in sites)
                {
                    Transform kiosk = city.World.Root.transform.Find(site.IsRidge ? "Ridge Kiosk" : "Kiosk " + site.StableId);
                    var station = kiosk.GetComponentInChildren<RidgeKioskStation>();
                    Assert.That(station, Is.Not.Null);
                    activeMenu = station.Menu;
                    Assert.That(activeMenu, Is.Not.Null);
                    motor.Teleport(site.Approach + Vector3.up * PlayerFactory.GroundedRootOffset);
                    hero.transform.rotation = Quaternion.LookRotation(-kiosk.forward);
                    Vector3 eye = site.Approach + site.Rotation * new Vector3(.37f, 1.53f, .65f);
                    follow.SetFixedPose(eye, Quaternion.LookRotation(station.InteractionPosition - eye), 59f);
                    var previousCamera = new Pose(follow.FixedBasePosition, follow.FixedBaseRotation);
                    float previousFov = follow.FixedBaseFieldOfView;
                    var previousFixedFocus = new FixedCameraFocus(12f, 8f, .8f, .3f);
                    follow.SetFixedFocus(previousFixedFocus);
                    Vector3 feet = hero.transform.position;
                    Physics.SyncTransforms();
                    yield return null; yield return null;
                    Assert.That(hero.ActiveInteractable, Is.SameAs(station));
                    Assert.That(kiosk.GetComponentsInChildren<MonoBehaviour>(true).OfType<IInteractable>()
                        .Where(action => action.CanInteract(hero)).Distinct().ToArray(), Is.EqualTo(new IInteractable[] { station }),
                        "Goods and the seller must not compete with the one window entry.");
                    foreach (Vector3 target in station.Offers.Select(offer => offer.InteractionPosition)
                        .Prepend(station.Vendor.Actor.Head.position))
                    {
                        follow.SetFixedPose(eye, Quaternion.LookRotation(target - eye), 59f);
                        yield return null; yield return null;
                        Assert.That(hero.ActiveInteractable, Is.SameAs(station), "Looking at a sample or seller still selects the single kiosk entry.");
                    }
                    follow.SetFixedPose(previousCamera.position, previousCamera.rotation, previousFov);
                    follow.SetFixedFocus(previousFixedFocus);

                    input.Press(keyboard.eKey);
                    yield return null; yield return null;
                    Assert.That(activeMenu.IsOpen, Is.True);
                    Assert.That(activeMenu.SelectedIndex, Is.Zero);
                    Assert.That(activeMenu.SelectedItemId, Is.Null);
                    Assert.That(activeMenu.IsReceiptOpen, Is.False);
                    Assert.That(station.Vendor.HasDialogue && station.Vendor.Session.IsActive, Is.False,
                        "Opening E cannot start a conversation or purchase.");
                    input.Release(keyboard.eKey);
                    yield return null; yield return null;
                    Assert.That(hero.InputEnabled || motor.InputEnabled, Is.False);
                    Assert.That(city.Player.PresentationVisibility.RenderersHidden, Is.False,
                        "This menu does not replace or hide the production hero.");
                    CompactShelfMenuAssertions.Check(activeMenu.View, station.Vendor.HasDialogue);
                    if (capture && site.IsRidge) yield return CaptureShopMenuFrame("01-vendor");
                    for (int selected = 1; selected < order.Length; selected++)
                    {
                        input.Press(keyboard.rightArrowKey); yield return null;
                        input.Release(keyboard.rightArrowKey); yield return null;
                        Assert.That(activeMenu.SelectedIndex, Is.EqualTo(selected));
                        Assert.That(activeMenu.SelectedItemId, Is.EqualTo(order[selected]));
                        CompactShelfMenuAssertions.Check(activeMenu.View, true, activeMenu.SelectedOffer.DescriptionLocalizationKey);
                        Vector3 focus = city.Camera.WorldToViewportPoint(activeMenu.SelectedFocusPosition);
                        Assert.That(focus.z, Is.GreaterThan(city.Camera.nearClipPlane));
                        Assert.That(focus.x, Is.InRange(.1f, .9f));
                        Assert.That(focus.y, Is.InRange(.12f, .88f));
                        Assert.That(Vector3.Distance(hero.transform.position, feet), Is.LessThan(.025f),
                            "Switching focus must not relocate the hero.");
                        if (capture && site.IsRidge)
                        {
                            yield return CaptureShopMenuFrame("0" + (selected + 1) + "-focus-" + selected);
                            CompactShelfMenuAssertions.CheckDescription(activeMenu.View, activeMenu.SelectedOffer.DescriptionLocalizationKey, true);
                        }
                    }
                    Assert.That(activeMenu.MoveSelection(1), Is.True);
                    Assert.That(activeMenu.SelectedIndex, Is.Zero);
                    if (!station.Vendor.HasDialogue)
                    {
                        CompactShelfMenuAssertions.Check(activeMenu.View, false);
                        Assert.That(activeMenu.ConfirmSelection(), Is.False);
                        Assert.That(activeMenu.IsOpen, Is.True, "A silent vendor remains a selectable view without new speech.");
                    }

                    int purchaseIndex = ++siteIndex;
                    activeMenu.Select(purchaseIndex);
                    yield return null; yield return null;
                    if (capture && site.IsRidge)
                    {
                        Assert.That(GameCursor.ApplicationFocused, Is.True);
                        Assert.That(GameInput.CanRead(GameInputContext.Menu), Is.True);
                        Assert.That(Mouse.current, Is.SameAs(mouse));
                        Assert.That(activeMenu.View.TryGetNavigationRects(out Rect left, out _), Is.True);
                        RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height);
                        Vector2 pixel = canvas.LogicalToScreen(left.center);
                        Vector2 mousePosition = new Vector2(pixel.x, Screen.height - pixel.y);
                        InputSystem.QueueStateEvent(mouse, new MouseState { position = mousePosition });
                        yield return null;
                        int paintRevision = activeMenu.View.PaintRevision;
                        yield return WaitForKiosk(() => activeMenu.View.PaintRevision != paintRevision,
                            "The active shop view did not paint the moved pointer.");
                        InputSystem.QueueStateEvent(mouse, new MouseState { position = mousePosition, buttons = 1 });
                        yield return null;
                        paintRevision = activeMenu.View.PaintRevision;
                        yield return WaitForKiosk(() => activeMenu.View.PaintRevision != paintRevision,
                            "The active shop view did not paint the pointer press.");
                        InputSystem.QueueStateEvent(mouse, new MouseState { position = mousePosition });
                        yield return null;
                        paintRevision = activeMenu.View.PaintRevision;
                        yield return WaitForKiosk(() => activeMenu.View.PaintRevision != paintRevision,
                            "The active shop view did not paint the pointer release.");
                        yield return WaitForKiosk(() => activeMenu.SelectedIndex == 0,
                            "The shared mouse arrow did not finish its native release paint.");
                        Assert.That(activeMenu.SelectedIndex, Is.Zero, "The shared mouse arrow changes the active world focus.");
                        activeMenu.Select(purchaseIndex);
                        yield return null; yield return null;
                    }
                    InventoryItemId item = order[purchaseIndex].Value;
                    int cash = GameSessionState.CashBalance, count = GameSessionState.GetInventoryItemCount(item);
                    Assert.That(SupermarketProductCatalog.TryGetOffer(RetailStock.RidgeKiosk, item, out var offer), Is.True);
                    input.Press(keyboard.eKey); yield return null;
                    input.Release(keyboard.eKey); yield return null;
                    Assert.That(activeMenu.IsReceiptOpen && receipt.IsPresenting, Is.True);
                    Assert.That(activeMenu.View.TryGetActionRects(out _, out _), Is.False,
                        "Only the shared receipt exposes actions while it owns the screen.");
                    Assert.That(activeMenu.ConfirmSelection(), Is.False, "Only one sale is permitted per receipt.");
                    Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash - offer.Price));
                    Assert.That(GameSessionState.GetInventoryItemCount(item), Is.EqualTo(count + 1));
                    Assert.That(activeMenu.Select(0), Is.False, "The receipt owns navigation until it closes.");
                    yield return WaitForKiosk(() => receipt.IsShowing, "The shared 3D receipt did not settle.");
                    if (capture && site.IsRidge) yield return CaptureShopMenuFrame("05-pie-receipt");
                    Assert.That(receipt.Dismiss(), Is.True);
                    Assert.That(activeMenu.ConfirmSelection(), Is.False);
                    yield return WaitForKiosk(() => !receipt.IsPresenting, "Closing the receipt did not return to the kiosk.");
                    yield return null; yield return null;
                    Assert.That(activeMenu.IsOpen && BarMinigameModalLock.IsAnyLocked, Is.True);
                    Assert.That(activeMenu.SelectedIndex, Is.EqualTo(purchaseIndex));
                    CompactShelfMenuAssertions.Check(activeMenu.View, true, activeMenu.SelectedOffer.DescriptionLocalizationKey);
                    Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash - offer.Price));
                    Assert.That(GameSessionState.GetInventoryItemCount(item), Is.EqualTo(count + 1));

                    input.Press(keyboard.escapeKey); yield return null;
                    input.Release(keyboard.escapeKey); yield return null; yield return null;
                    Assert.That(activeMenu.IsOpen || BarMinigameModalLock.IsAnyLocked || city.PauseMenu.IsOpen, Is.False);
                    Assert.That(hero.InputEnabled && motor.InputEnabled, Is.True);
                    Assert.That(Vector3.Distance(follow.FixedBasePosition, previousCamera.position), Is.LessThan(.001f));
                    Assert.That(Quaternion.Angle(follow.FixedBaseRotation, previousCamera.rotation), Is.LessThan(.01f));
                    Assert.That(follow.FixedBaseFieldOfView, Is.EqualTo(previousFov).Within(.001f));
                    Assert.That(follow.FixedFocus, Is.EqualTo(previousFixedFocus));
                    Assert.That(activeMenu.Open(station, hero), Is.True);
                    Assert.That(activeMenu.SelectedIndex, Is.Zero, "Every fresh visit starts on the seller.");
                    activeMenu.enabled = false;
                    Assert.That(activeMenu.IsOpen || BarMinigameModalLock.IsAnyLocked, Is.False);
                    Assert.That(hero.InputEnabled && motor.InputEnabled, Is.True);
                    activeMenu.enabled = true;
                    follow.ClearFixedFocus(); follow.ClearFixedPose();
                }

                var ridge = city.World.Root.transform.Find("Ridge Kiosk").GetComponentInChildren<RidgeKioskStation>();
                motor.Teleport(CityKioskSites.ForLayout(city.Layout).Sites.Single(site => site.IsRidge).Approach +
                    Vector3.up * PlayerFactory.GroundedRootOffset);
                Physics.SyncTransforms(); yield return null;
                activeMenu = ridge.Menu;
                Assert.That(activeMenu.Open(ridge, hero), Is.True);
                yield return null; yield return null;
                Assert.That(activeMenu.ConfirmSelection(), Is.True);
                Assert.That(activeMenu.IsOpen, Is.False);
                Assert.That(ridge.Vendor.Session.IsActive, Is.True, "The existing staged dialogue takes ownership after the menu releases it.");
                yield return WaitForKiosk(() => ridge.Vendor.Session.IsChoosing, "The existing conversation did not reach its choice.");
                if (capture) yield return CaptureShopMenuFrame("06-existing-vendor-dialogue");
                ridge.Vendor.Session.RestoreImmediate();
                yield return null; yield return null;
                Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
                Assert.That(hero.InputEnabled && motor.InputEnabled, Is.True);
                Assert.That(receipt.IsPresenting, Is.False);
            }
            finally
            {
                if (receipt && receipt.IsPresenting) receipt.Abandon();
                if (activeMenu) activeMenu.Close();
                if (city)
                {
                    foreach (var vendor in city.World.Root.GetComponentsInChildren<RidgeKioskVendor>())
                        vendor.Session?.RestoreImmediate();
                    city.Camera.GetComponent<PlayerCameraFollow>().ClearFixedPose();
                }
                GameCursor.Release(cursorOwner);
                GameInput.HandleApplicationFocus(previousFocus);
                input.TearDown();
            }
        }

        internal static IEnumerator CaptureShopMenuFrame(string name)
        {
            string path = Path.GetFullPath(Path.Combine("Captures", "KioskMenu", name + ".png"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            DateTime before = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            ScreenCapture.CaptureScreenshot(path);
            yield return WaitForKiosk(() => File.Exists(path) && File.GetLastWriteTimeUtc(path) > before &&
                new FileInfo(path).Length > 1000, "The real Game view did not capture kiosk menu " + name);
        }
    }
}
