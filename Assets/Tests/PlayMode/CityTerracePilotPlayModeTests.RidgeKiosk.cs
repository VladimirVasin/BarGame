using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest]
        public IEnumerator LiveCity_RidgeKioskTradesAndTalksOffTheSummitSidewalk()
        {
            yield return LoadProductionCity();
            Assert.That(RidgeKioskSitePlan.TryCreate(city.Layout, out var site), Is.True,
                "The default summit needs a site along the home and bus stop sidewalk.");
            bool capture = System.Environment.GetEnvironmentVariable("BAR_PROMENADE_RIDGE_KIOSK") == "1";
#if UNITY_EDITOR
            if (capture)
                System.Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                    .GetMethod("ExportCurrent", System.Type.EmptyTypes).Invoke(null, null);
#endif
            Transform kiosk = city.World.Root.transform.Find("Ridge Kiosk");
            Assert.That(kiosk, Is.Not.Null);
            Assert.That(kiosk.position, Is.EqualTo(site.Origin));
            var vendor = kiosk.GetComponentInChildren<RidgeKioskVendor>();
            var products = kiosk.GetComponentsInChildren<RidgeKioskPurchase>();
            Assert.That(products.Select(p => p.ItemId), Is.EquivalentTo(new[] {
                InventoryItemId.BeerCan, InventoryItemId.VodkaBottle, InventoryItemId.PotatoPirozhok }));
            var modelRenderers = kiosk.GetComponentsInChildren<Renderer>(true);
            Assert.That(modelRenderers.Any(r => r.name == "RidgeKiosk_RidgeKioskSurface" &&
                r.bounds.size.x > 3f && r.bounds.size.y > 3f), Is.True, "Imported shell retains its authored metres.");
            Assert.That(Vector3.Distance(vendor.Actor.transform.position,
                site.Origin + new Vector3(0f, .08f, .45f)), Is.LessThan(.01f));

            var point = city.Map.PointsOfInterest.Single(p => p.StableId == "ridge-kiosk");
            Vector3 standing = site.Approach + Vector3.up * PlayerFactory.GroundedRootOffset;
            var ground = new CityMapCityTeleportGround(city.Layout);
            Assert.That(ground.TryClampArrival(point.WorldPosition, out var safe), Is.True);
            Assert.That(Vector3.Distance(safe, standing), Is.LessThan(.005f));
            Assert.That(point.Footprint, Is.EqualTo(site.Bounds));
            Assert.That(city.World.WalkableArea.Contains(site.Origin, .30f), Is.False);

            capture &= SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            var hero = city.Player.Interactor;
            var motor = city.Player.Motor;
            var follow = city.Camera.GetComponent<PlayerCameraFollow>();
            var receipt = WorldItemFoundScreen.For(hero);
            var angles = kiosk.GetComponentsInChildren<Transform>();
            var approachAnchor = angles.Single(t => t.name == "KioskApproach");
            Assert.That(Vector3.Distance(approachAnchor.position, site.Approach), Is.LessThan(.01f));
            // Direct City loading skips the apartment wake that normally starts time.
            if (!GameSessionState.IsGameTimeRunning) GameSessionState.TryStartGameTimeAt(12 * 60);
            double toNoon = (2160d - GameSessionState.GameTimeOfDayMinutes) % 1440d;
            GameSessionState.AdvanceGameTime((float)(toNoon / GameTimeState.GameMinutesPerRealSecond));
            Assert.That(GameSessionState.IsGameTimeRunning, Is.True);
            Assert.That(GameSessionState.GameTimeOfDayMinutes, Is.EqualTo(720d).Within(.01d));
            city.DayNight.ApplyCurrentTime(true);

            try
            {
                Vector3 sidewalk = new Vector3(standing.x, standing.y, site.Apron.yMax + 1f);
                motor.Teleport(sidewalk + Vector3.left * 2.5f);
                Physics.SyncTransforms();
                yield return WalkTo(sidewalk);
                yield return WalkTo(standing);
                if (capture)
                {
                    Vector3 at = kiosk.position + new Vector3(-4.8f, 2f, 5.9f);
                    follow.SetFixedPose(at, Quaternion.LookRotation(kiosk.position + Vector3.up * 1.25f - at), 58f);
                    AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "RidgeKiosk", "01-home-and-window", 1280, 720);
                    // Looking back along the player's approach from the house, the
                    // serving hatch, goods and waiting strip share the same frame.
                    at = kiosk.position + new Vector3(2.55f, 1.68f, 4.45f);
                    follow.SetFixedPose(at, Quaternion.LookRotation(vendor.InteractionPosition - at), 55f);
                    AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "RidgeKiosk", "02-window-from-home", 1280, 720);
                }

                foreach (var product in products)
                {
                    yield return AimAt(product.InteractionPosition);
                    Assert.That(hero.ActiveInteractable, Is.SameAs(product), $"Aiming at {product.ItemId} should select that sample.");
                    product.Interact(hero);
                    var confirmation = city.TargetInteraction;
                    Assert.That(confirmation.IsOpen, Is.True, product.ItemId.ToString());
                    int cash = GameSessionState.CashBalance;
                    int count = GameSessionState.GetInventoryItemCount(product.ItemId);
                    Assert.That(confirmation.Cancel(), Is.True);
                    Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash), "Cancelling must not charge.");
                    Assert.That(GameSessionState.GetInventoryItemCount(product.ItemId), Is.EqualTo(count));
                    yield return AimAt(product.InteractionPosition);
                    product.Interact(hero);
                    confirmation.SelectConfirmation(true);
                    Assert.That(confirmation.Confirm(), Is.True);
                    Assert.That(receipt.IsPresenting, Is.True);
                    Assert.That(SupermarketProductCatalog.TryGetOffer(RetailStock.RidgeKiosk, product.ItemId,
                        out var offer), Is.True);
                    Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash - offer.Price));
                    Assert.That(GameSessionState.GetInventoryItemCount(product.ItemId), Is.EqualTo(count + 1));
                    Assert.That(confirmation.Confirm(), Is.False, "One confirmation owns exactly one grant.");
                    if (product.ItemId == InventoryItemId.PotatoPirozhok)
                    {
                        for (int i = 0; i < 100 && !receipt.IsShowing; i++) yield return null;
                        Assert.That(receipt.IsShowing, Is.True, "The full-size pie is held on the receipt.");
                        Assert.That(receipt.ActiveItemId, Is.EqualTo(product.ItemId));
                        Assert.That(receipt.Dismiss(), Is.True);
                        for (int i = 0; i < 100 && receipt.IsPresenting; i++) yield return null;
                        Assert.That(receipt.IsPresenting, Is.False);
                        yield return null;
                        GameSessionState.UpdateNeeds(56, GameSessionState.StressLevel);
                        Assert.That(city.Inventory.Open(), Is.True);
                        int index = GameSessionState.InventoryItems.ToList().FindIndex(s => s.ItemId == product.ItemId);
                        Assert.That(index, Is.GreaterThanOrEqualTo(0));
                        city.Inventory.SelectItem(index);
                        Assert.That(city.Inventory.SelectedStack.ItemId, Is.EqualTo(product.ItemId));
                        Assert.That(city.Inventory.UseSelected(), Is.True);
                        Assert.That(GameSessionState.HungerLevel, Is.EqualTo(32));
                        Assert.That(GameSessionState.GetInventoryItemCount(product.ItemId), Is.EqualTo(count));
                        Assert.That(city.Inventory.Close(), Is.True);
                    }
                    else Assert.That(receipt.Abandon(), Is.True);
                    yield return null;
                }

                yield return AimAt(vendor.Actor.Head.position);
                Assert.That(hero.ActiveInteractable, Is.SameAs(vendor), "The window lets the player address the person behind the bottles.");
                int beforeTalk = GameSessionState.CashBalance;
                vendor.Interact(hero);
                Assert.That(vendor.Session.IsActive, Is.True);
                for (int i = 0; i < 220 && !vendor.Session.IsChoosing && vendor.Session.IsActive; i++) yield return null;
                Assert.That(vendor.Session.IsChoosing, Is.True,
                    $"Dialogue phase={vendor.Session.Phase}, clock={vendor.Session.SpeechClock:F2}, " +
                    $"rig={hero.GetComponent<PlayerAnimatedInteractionController>().Phase}, hero={hero.transform.position:F2}, " +
                    $"vendor={vendor.Actor.transform.position:F2}, game minute={GameSessionState.GameMinuteOfDay}, " +
                    $"camera={vendor.Session.CameraDirector.LastRejectedShotReason}");
                yield return null;
                vendor.Session.SelectChoice(0);
                Assert.That(vendor.Session.Confirm(), Is.True);
                bool heardHero = false, heardSeller = false;
                for (int i = 0; i < 450 && vendor.Session.IsActive; i++)
                {
                    if (vendor.Session.Phase == DialoguePhase.Speaking)
                    {
                        if (vendor.Session.CurrentNode.Speaker == DialogueSpeaker.Hero) heardHero = true;
                        if (vendor.Session.CurrentNode.Speaker == DialogueSpeaker.Npc)
                        {
                            if (capture && !heardSeller)
                                AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "RidgeKiosk", "03-seller-at-window", 1280, 720);
                            heardSeller = true;
                        }
                    }
                    yield return null;
                }
                Assert.That(heardHero && heardSeller, Is.True, "A complete exchange needs both speakers.");
                Assert.That(vendor.Session.IsActive || BarMinigameModalLock.IsAnyLocked, Is.False);
                Assert.That(GameSessionState.CashBalance, Is.EqualTo(beforeTalk));
                Assert.That(hero.InputEnabled && motor.InputEnabled, Is.True);
                yield return WalkTo(sidewalk);
                yield return WalkTo(sidewalk + Vector3.right * 2.5f);
            }
            finally
            {
                if (receipt.IsPresenting) receipt.Abandon();
                if (city.TargetInteraction.IsOpen) city.TargetInteraction.Cancel();
                if (city.Inventory.IsOpen) city.Inventory.Close();
                if (vendor.Session.IsActive) vendor.Session.Cancel();
                follow.ClearFixedPose();
            }

            IEnumerator AimAt(Vector3 pointToFace)
            {
                // A normal shoulder-height view from the street. The real interactor
                // must resolve the targets before the test dispatches the action.
                Vector3 eye = hero.transform.position + new Vector3(.37f, 1.53f, .65f);
                follow.SetFixedPose(eye, Quaternion.LookRotation(pointToFace - eye), 59f);
                Physics.SyncTransforms();
                yield return null;
                yield return null;
            }

            IEnumerator WalkTo(Vector3 destination)
            {
                bool arrived = false;
                for (int i = 0; i < 96 && !arrived; i++)
                {
                    arrived = motor.MoveTowardsApproachWaypoint(destination, .045f, Time.captureDeltaTime);
                    yield return null;
                }
                Assert.That(arrived, Is.True, $"Hero must physically reach {destination:F2}, stopped at {motor.transform.position:F2}.");
                Assert.That(motor.transform.position.y, Is.EqualTo(destination.y).Within(.17f));
            }
        }
    }
}
