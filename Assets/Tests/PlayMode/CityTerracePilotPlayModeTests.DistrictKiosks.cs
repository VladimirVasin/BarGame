using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class DistrictKioskCaptureSetup : IPrebuildSetup
    {
        public const string CaptureEnvironment = "BAR_PROMENADE_CAPTURE_DISTRICT_KIOSKS";

        public void Setup()
        {
#if UNITY_EDITOR
            Type exporter = Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true);
            if (Environment.GetEnvironmentVariable(CaptureEnvironment) == "1")
                exporter.GetMethod("FocusGameViewForCapture", Type.EmptyTypes).Invoke(null, null);
            // One prebuild survey, after focusing the real Game view. Never export during a capture.
            exporter.GetMethod("ExportCurrent", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest, PrebuildSetup(typeof(DistrictKioskCaptureSetup)), Timeout(240000)]
        public IEnumerator LiveCity_DistrictKiosksKeepDistinctSellersSafeApproachesAndAtomicVodkaSales()
        {
            var input = new InputTestFixture();
            input.Setup();
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            keyboard.MakeCurrent();
            bool previousFocus = GameInput.MovementFocused;
            int previousInterval = OnDemandRendering.renderFrameInterval;
            var cursorOwner = new object();
            PlayerMotor motor = null;
            PlayerInteractor hero = null;
            PlayerCameraFollow follow = null;
            WorldItemFoundScreen receipt = null;
            bool capture = Environment.GetEnvironmentVariable(DistrictKioskCaptureSetup.CaptureEnvironment) == "1";
            try
            {
                GameInput.HandleApplicationFocus(true);
                GameCursor.Capture(cursorOwner, CursorLockMode.Locked, false, 0);
                if (capture) Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null),
                    "Requested kiosk review must use the actual rendered Game view.");
                yield return LoadProductionCity();
                motor = city.Player.Motor;
                hero = city.Player.Interactor;
                follow = city.Camera.GetComponent<PlayerCameraFollow>();
                receipt = WorldItemFoundScreen.For(hero);
                Assert.That(receipt != null && receipt.IsInitialized, Is.True);
                follow.SetOrbitInputEnabled(false);
                isolatedHeroCapsule = motor.GetComponent<CharacterController>();
                OnDemandRendering.renderFrameInterval = 24;
                Assert.That(GameSessionState.IsGameTimeRunning, Is.False);
                Assert.That(GameSessionState.TryPrepareGameTimeAt(12 * 60), Is.True);
                city.DayNight.ApplyCurrentTime(true);
                city.Night.Atmosphere.RefreshImmediate();

                var sites = CityKioskSites.ForLayout(city.Layout).Sites;
                string[] expectedIds = { "ridge-kiosk", "residential-kiosk", "industrial-kiosk" };
                Assert.That(sites.Select(site => site.StableId), Is.EquivalentTo(expectedIds));
                Assert.That(sites.Where(site => !site.IsRidge).Select(site => site.District),
                    Is.EquivalentTo(new[] { CityDistrictKind.Residential, CityDistrictKind.Industrial }),
                    "Both active lower urban districts need a shop; legacy districts and Park do not.");
                Assert.That(sites.Select(site => site.CharacterId).Distinct().Count(), Is.EqualTo(3));
                Assert.That(sites.Select(site => site.CharacterId), Is.EquivalentTo(new[] {
                    DefaultNpcPopulation.RidgeKioskSeller, DefaultNpcPopulation.CityResidentialKioskSeller,
                    DefaultNpcPopulation.CityIndustrialKioskSeller }));
                AssertResidentialKioskDistance(sites.Single(site => site.StableId == "residential-kiosk"));

                var mapGround = new CityMapCityTeleportGround(city.Layout);
                var mapKiosks = city.Map.PointsOfInterest.Where(point => point.Kind == CityMapPointOfInterestKind.RidgeKiosk).ToArray();
                Assert.That(mapKiosks.Select(point => point.StableId), Is.EquivalentTo(expectedIds));
                var sourceIds = new HashSet<string>(StringComparer.Ordinal);
                var visibleLooks = new HashSet<string>(StringComparer.Ordinal);
                var headwearLooks = new HashSet<string>(StringComparer.Ordinal);
                foreach (CityKioskSitePlan site in sites)
                {
                    Debug.Log($"DISTRICT KIOSK SITE id={site.StableId} district={site.District} " +
                        $"origin={site.Origin:F3} rotation={site.Rotation:F3} " +
                        $"barRoute={site.BarRouteDistance:F3} barXZ={site.BarPlanarDistance:F3}");
                    Transform kiosk = city.World.Root.transform.Find(site.IsRidge ? "Ridge Kiosk" : "Kiosk " + site.StableId);
                    Assert.That(kiosk, Is.Not.Null, site.StableId);
                    Assert.That(Vector3.Distance(kiosk.position, site.Origin), Is.LessThan(.01f));
                    Assert.That(Quaternion.Angle(kiosk.rotation, site.Rotation), Is.LessThan(.01f));
                    RidgeKioskVendor vendor = kiosk.GetComponentInChildren<RidgeKioskVendor>();
                    Assert.That(vendor, Is.Not.Null);
                    Assert.That(vendor.CharacterId, Is.EqualTo(site.CharacterId));
                    Assert.That(vendor.Actor.GetComponent<DefaultNpcAppearance>().AppearanceKey, Is.EqualTo(site.CharacterId));
                    Assert.That(vendor.HasDialogue, Is.EqualTo(site.IsRidge));
                    if (site.IsRidge) Assert.That(vendor.Session, Is.Not.Null);
                    else
                    {
                        Assert.That(vendor.Session, Is.Null, "A new seller must not inherit Ridge's speech pool.");
                        Assert.That(vendor.GetComponent<Collider>(), Is.Null, "Silent staff offer no talk trigger.");
                        Assert.That(vendor.CanInteract(hero), Is.False);
                    }
                    var products = kiosk.GetComponentsInChildren<RidgeKioskPurchase>();
                    Assert.That(products.Select(product => product.ItemId), Is.EquivalentTo(new[] {
                        InventoryItemId.BeerCan, InventoryItemId.VodkaBottle, InventoryItemId.PotatoPirozhok }));
                    string prefix = site.IsRidge ? "city.ridge.kiosk." : "city.kiosk." + site.StableId + ".";
                    foreach (RidgeKioskPurchase product in products)
                    {
                        Assert.That(product.SourceId, Does.StartWith(prefix));
                        Assert.That(sourceIds.Add(product.SourceId), Is.True, "Every till/sample needs its own source ID.");
                    }
                    CityMapPointOfInterest point = mapKiosks.Single(candidate => candidate.StableId == site.StableId);
                    Vector3 standing = site.Approach + Vector3.up * PlayerFactory.GroundedRootOffset;
                    Assert.That(point.Footprint, Is.EqualTo(site.Bounds));
                    Assert.That(Vector3.Distance(point.WorldPosition, standing), Is.LessThan(.02f));
                    Assert.That(mapGround.TryClampArrival(point.WorldPosition, out Vector3 arrival), Is.True);
                    Assert.That(Vector3.Distance(arrival, standing), Is.LessThan(.03f), site.StableId + " map arrival");
                    Assert.That(city.World.WalkableArea.Contains(arrival, isolatedHeroCapsule.radius), Is.True);
                    Assert.That(city.World.WalkableArea.Contains(site.Origin, isolatedHeroCapsule.radius), Is.False);

                    Vector3 through = site.IsRidge
                        ? new Vector3(standing.x, site.Origin.y, site.Apron.yMax + 1f) : site.PublicThroughCenter;
                    Vector3 left = site.IsRidge ? through + Vector3.left * 2.5f : site.PublicThroughLeft;
                    Vector3 right = site.IsRidge ? through + Vector3.right * 2.5f : site.PublicThroughRight;
                    motor.Teleport(left + Vector3.up * PlayerFactory.GroundedRootOffset);
                    Physics.SyncTransforms();
                    for (int frame = 0; frame < 5; frame++) yield return null;
                    yield return WalkKioskTo(through + Vector3.up * PlayerFactory.GroundedRootOffset);
                    yield return WalkKioskTo(standing);
                    DefaultNpcAppearance appearance = vendor.Actor.GetComponent<DefaultNpcAppearance>();
                    NpcWardrobe wardrobe = vendor.Actor.GetComponent<NpcWardrobe>();
                    Assert.That(appearance.CurrentAtlas, Is.Not.Null);
                    Assert.That(wardrobe, Is.Not.Null);
                    string outerwear = wardrobe.GetEquippedItem("outerwear");
                    string headwear = wardrobe.GetEquippedItem("headwear");
                    string apron = wardrobe.GetEquippedItem("apron");
                    string scarf = wardrobe.GetEquippedItem("scarf");
                    string visibleLook = appearance.CurrentAtlas.name + "|" + outerwear + "|" +
                        (headwear ?? "<bare>") + "|" + apron + "|" + scarf;
                    Assert.That(visibleLooks.Add(visibleLook), Is.True, "Compare the applied look, not the character ID.");
                    Assert.That(headwearLooks.Add(headwear ?? "<bare>"), Is.True,
                        "Each seller needs a visibly different head silhouette.");
                    Assert.That(outerwear, Is.EqualTo(site.StableId == "residential-kiosk"
                        ? "outerwear.cardigan" : "outerwear.work"));
                    Assert.That(headwear, Is.EqualTo(site.IsRidge ? null : site.StableId == "residential-kiosk"
                        ? "headwear.work" : "headwear.everyday"));
                    Assert.That(apron, Is.EqualTo(site.IsRidge ? "apron.work" : null));
                    Assert.That(scarf, Is.EqualTo(site.StableId == "residential-kiosk" ? "scarf.warm" : null));
                    if (headwear != null)
                        Assert.That(wardrobe.Items.Single(item => item.Id == headwear).Parts.Any(part =>
                            part.Renderer != null && part.Renderer.enabled && part.Renderer.gameObject.activeInHierarchy), Is.True,
                            "The allocated hat must actually be visible near the counter.");
                    DefaultNpcPopulation.Assignment assigned = DefaultNpcPopulation.GetAssignment(site.CharacterId);
                    Assert.That(appearance.CurrentFaceId, Is.EqualTo(assigned.FaceId));
                    Assert.That(appearance.CurrentHairColorId, Is.EqualTo(assigned.HairColorId));
                    Debug.Log($"DISTRICT KIOSK LOOK id={site.StableId} atlas={appearance.CurrentAtlas.name} " +
                        $"face={appearance.CurrentFaceId} hair={appearance.CurrentHairColorId} outerwear={outerwear} " +
                        $"headwear={headwear ?? "<bare>"} apron={apron ?? "<none>"} scarf={scarf ?? "<none>"} " +
                        $"assigned={assigned.VisibleSignature}");
                    if (capture)
                    {
                        Vector3 eye = kiosk.TransformPoint(new Vector3(.38f, 1.62f, 3.55f));
                        follow.SetFixedPose(eye, Quaternion.LookRotation(vendor.Actor.Head.position - eye), 52f);
                        yield return KioskPresentationAt(720, site.StableId + "-seller-day", kiosk);
                    }
                    if (!site.IsRidge)
                    {
                        Vector3 eye = kiosk.TransformPoint(new Vector3(3.5f, 2.1f, 6f));
                        follow.SetFixedPose(eye, Quaternion.LookRotation(kiosk.TransformPoint(new Vector3(0f, 1.25f, .6f)) - eye), 60f);
                        yield return KioskPresentationAt(720, capture ? site.StableId + "-day" : null, kiosk);
                        yield return KioskPresentationAt(0, capture ? site.StableId + "-night" : null, kiosk);
                    }

                    RidgeKioskPurchase vodka = products.Single(product => product.ItemId == InventoryItemId.VodkaBottle);
                    yield return AimKioskAt(site, vodka.InteractionPosition);
                    Assert.That(hero.ActiveInteractable, Is.SameAs(vodka), site.StableId + " real optical selection");
                    int cash = GameSessionState.CashBalance;
                    int count = GameSessionState.GetInventoryItemCount(InventoryItemId.VodkaBottle);
                    yield return PressKioskE();
                    Assert.That(city.TargetInteraction.IsOpen, Is.True, "E must open the shared confirmation.");
                    Assert.That(city.TargetInteraction.Cancel(), Is.True);
                    Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash));
                    Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.VodkaBottle), Is.EqualTo(count));
                    yield return AimKioskAt(site, vodka.InteractionPosition);
                    yield return PressKioskE();
                    Assert.That(city.TargetInteraction.IsOpen, Is.True);
                    city.TargetInteraction.SelectConfirmation(true);
                    Assert.That(city.TargetInteraction.Confirm(), Is.True);
                    Assert.That(receipt.IsPresenting, Is.True);
                    Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash - 28));
                    Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.VodkaBottle), Is.EqualTo(count + 1));
                    Assert.That(city.TargetInteraction.Confirm(), Is.False, "A confirmation may credit only once.");
                    yield return WaitForKiosk(() => receipt.IsShowing, "The shared 3D vodka receipt did not settle.");
                    Assert.That(receipt.ActiveItemId, Is.EqualTo(InventoryItemId.VodkaBottle));
                    Assert.That(receipt.Dismiss(), Is.True);
                    yield return WaitForKiosk(() => !receipt.IsPresenting, "Closing the receipt retained its modal ownership.");
                    yield return null;
                    Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash - 28));
                    Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.VodkaBottle), Is.EqualTo(count + 1));
                    Assert.That(vodka.isActiveAndEnabled, Is.True, "The counter sample remains replenishable.");
                    Assert.That(hero.InputEnabled && motor.InputEnabled, Is.True);
                    yield return WalkKioskTo(through + Vector3.up * PlayerFactory.GroundedRootOffset);
                    yield return WalkKioskTo(right + Vector3.up * PlayerFactory.GroundedRootOffset);
                }

                Assert.That(sourceIds.Count, Is.EqualTo(9));
                Assert.That(visibleLooks.Count, Is.EqualTo(3));
                Assert.That(headwearLooks.Count, Is.EqualTo(3));
                Assert.That(city.Map.Open(), Is.True);
                Assert.That(city.Map.SetMapPointInspectionEnabled(true), Is.True);
                int selected = city.Map.ActiveMapPoints.ToList().FindIndex(point => point.StableId == "city:poi:residential-kiosk");
                Assert.That(selected, Is.GreaterThanOrEqualTo(0));
                Assert.That(city.Map.SelectMapPoint(selected), Is.True);
                OnDemandRendering.renderFrameInterval = 1;
                for (int frame = 0; frame < 5; frame++) yield return null;
                var labels = city.Map.ActiveMapPoints.Where(point => expectedIds.Any(id => point.StableId == "city:poi:" + id)).ToArray();
                Assert.That(labels.Length, Is.EqualTo(3));
                Assert.That(labels.Select(point => point.Label).Distinct().Count(), Is.EqualTo(3));
                foreach (var label in labels)
                {
                    Assert.That(label.Label, Is.Not.Empty);
                    Assert.That(label.Label, Does.Not.StartWith("map.poi."));
                }
                if (capture) yield return CaptureDistrictKioskGameView("map-three-kiosks");
                Assert.That(city.Map.Close(), Is.True);
            }
            finally
            {
                if (receipt != null && receipt.IsPresenting) receipt.Abandon();
                if (city != null)
                {
                    if (city.TargetInteraction != null && city.TargetInteraction.IsOpen) city.TargetInteraction.Cancel();
                    if (city.Map != null && city.Map.IsOpen) city.Map.Close();
                }
                if (follow != null) follow.ClearFixedPose();
                OnDemandRendering.renderFrameInterval = previousInterval;
                GameCursor.Release(cursorOwner);
                GameInput.HandleApplicationFocus(previousFocus);
                input.TearDown();
            }

            void AssertResidentialKioskDistance(CityKioskSitePlan site)
            {
                Assert.That(site.FrontageEdge.HasValue, Is.True);
                float route = float.PositiveInfinity, planar = float.PositiveInfinity;
                foreach (var bar in city.Layout.BuildingLots.Where(lot => lot.IsBar))
                {
                    Assert.That(city.Layout.TryGetFrontageEdge(bar, out RoadEdge barEdge), Is.True);
                    route = Mathf.Min(route, CityTravelDistance.BetweenAnchors(city.Layout, site.FrontageEdge.Value,
                        site.StreetAnchor, barEdge, bar.ReturnPosition));
                    planar = Mathf.Min(planar, Vector2.Distance(new Vector2(site.Origin.x, site.Origin.z),
                        new Vector2(bar.DoorPosition.x, bar.DoorPosition.z)));
                }
                Assert.That(float.IsInfinity(route) || float.IsInfinity(planar), Is.False, "Measure the actual bar route.");
                Assert.That(route, Is.GreaterThanOrEqualTo(120f));
                Assert.That(planar, Is.GreaterThanOrEqualTo(60f));
                Assert.That(site.BarRouteDistance, Is.EqualTo(route).Within(.02f));
                Assert.That(site.BarPlanarDistance, Is.EqualTo(planar).Within(.02f));
            }

            IEnumerator WalkKioskTo(Vector3 destination)
            {
                bool arrived = false;
                for (int frame = 0; frame < 140 && !arrived; frame++)
                {
                    IsolateAmbientCollisionPairs();
                    arrived = motor.MoveTowardsApproachWaypoint(destination, .045f, Time.captureDeltaTime);
                    yield return null;
                    Assert.That(city.World.WalkableArea.Contains(motor.transform.position, isolatedHeroCapsule.radius), Is.True);
                }
                Assert.That(arrived, Is.True, $"Kiosk walk stopped at {motor.transform.position:F3}, target {destination:F3}.");
                Assert.That(motor.transform.position.y, Is.EqualTo(destination.y).Within(.17f));
            }

            IEnumerator AimKioskAt(CityKioskSitePlan site, Vector3 target)
            {
                Vector3 eye = hero.transform.position + site.Rotation * new Vector3(.25f, 1.53f, .48f);
                follow.SetFixedPose(eye, Quaternion.LookRotation(target - eye), 59f);
                Physics.SyncTransforms();
                yield return null;
                yield return null;
            }

            IEnumerator PressKioskE()
            {
                input.Press(keyboard.eKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.eKey, queueEventOnly: true);
                yield return null;
            }

            IEnumerator KioskPresentationAt(int minute, string shot, Transform kiosk)
            {
                int previousMinute = GameSessionState.GameMinuteOfDay;
                int interval = OnDemandRendering.renderFrameInterval;
                Assert.That(GameSessionState.IsGameTimeRunning, Is.False);
                Assert.That(GameSessionState.TryPrepareGameTimeAt(minute), Is.True);
                OnDemandRendering.renderFrameInterval = 1;
                try
                {
                    city.DayNight.ApplyCurrentTime(true);
                    for (int frame = 0; frame < 3; frame++) yield return null;
                    city.Night.Atmosphere.RefreshImmediate();
                    foreach (var anchor in kiosk.GetComponentsInChildren<Transform>().Where(part =>
                        part.name == CityNightAtmosphere.IndustrialTaskLightPrefix + "RidgeKiosk" ||
                        part.name == CityNightAtmosphere.IndustrialTaskLightPrefix + "RidgeKioskInterior"))
                        Assert.That(city.Night.Atmosphere.StreetLightPool.Any(light => light.enabled && light.intensity > 0f &&
                            Vector3.Distance(light.transform.position, anchor.position) < .01f), Is.True,
                            "The visible kiosk fixture needs its real pooled light: " + shot);
                    if (shot != null) yield return CaptureDistrictKioskGameView(shot);
                }
                finally
                {
                    Assert.That(GameSessionState.TryPrepareGameTimeAt(previousMinute), Is.True);
                    city.DayNight.ApplyCurrentTime(true);
                    city.Night.Atmosphere.RefreshImmediate();
                    OnDemandRendering.renderFrameInterval = interval;
                }
            }
        }

        private static IEnumerator WaitForKiosk(Func<bool> condition, string message)
        {
            float deadline = Time.realtimeSinceStartup + 8f;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, message);
        }

        private static IEnumerator CaptureDistrictKioskGameView(string shot)
        {
            string path = Path.GetFullPath(Path.Combine("Captures", "DistrictKiosks", shot + ".png"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            DateTime previous = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            ScreenCapture.CaptureScreenshot(path);
            yield return WaitForKiosk(() => File.Exists(path) && File.GetLastWriteTimeUtc(path) > previous &&
                new FileInfo(path).Length > 1000, "The actual Game view did not write the requested kiosk frame.");
        }
    }
}
