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
        [UnityTest, Timeout(240000)]
        public IEnumerator LiveCity_DistrictKioskCourtsUseDistinctFurnitureRegularsAndChores()
        {
            yield return LoadProductionCity();
            var residents = city.Pedestrians.Residents;
            var courts = city.World.Root.GetComponentsInChildren<DistrictKioskCourtWorld>()
                .OrderByDescending(court => court.Plan.Residential).ToArray();
            var failures = new List<string>();
            var seen = new HashSet<int>();
            var motor = city.Player.Motor;
            var hero = city.Player.Interactor;
            bool previousFocus = GameInput.MovementFocused;
            GameInput.HandleApplicationFocus(true);
            try
            {
                Check(courts.Length == 2, "Production City must compose both district courts.");
                Check(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "City captures require a graphics device.");
                foreach (var court in courts)
                {
                    var plan = court.Plan;
                    var station = court.GetComponentInParent<RidgeKioskStation>();
                    string id = plan.Site.StableId;
                    Check(!station.Vendor.HasDialogue, id + " seller dialogue");
                    Check(Mathf.Abs(court.Table(0).position.y - plan.Site.Origin.y - (plan.Residential ? .75f : 1.04f)) < .02f,
                        id + " imported first table height");
                    Check(Mathf.Abs(court.Table(1).position.y - plan.Site.Origin.y - (plan.Residential ? .75f : 1.06f)) < .02f,
                        id + " imported second table height");
                    foreach (Vector3 spot in new[] { plan.Gate, plan.Site.PublicThroughLeft, plan.Site.PublicThroughRight })
                        Check(city.World.WalkableArea.Contains(spot, .22f), id + " walking clearance " + spot);

                    int[] cast = Enumerable.Range(0, CityResidentLifeRules.ResidentCount)
                        .Where(person => residents.DistrictKioskVisit(person).Court == plan).ToArray();
                    Check(cast.Length == plan.Capacity, id + " assigned regulars " + cast.Length);
                    foreach (int person in cast)
                    {
                        Check(seen.Add(person) && residents.KioskPlace(person) < 0 &&
                            !DefaultNpcPopulation.IsWomanPedestrian(person), id + " exclusive male regular " + person);
                        int node = residents.Destination(person, CityResidentActivity.Leisure);
                        Check(float.IsFinite(residents.DistancesTo(node)[residents.Workplace(person).PavementNode]),
                            id + " work journey " + person);
                        Check(residents.DistrictKioskVisit(person).Departure < 23 * 60, id + " departure " + person);
                    }

                    int bestMinute = 18 * 60, bestCount = 0;
                    for (int minute = 15 * 60; minute < 22 * 60; minute++)
                    {
                        int count = cast.Count(person =>
                        {
                            var sample = residents.Schedule(person, 0, minute);
                            return sample.Activity == CityResidentActivity.Leisure &&
                                sample.StartedAt + 22 < minute && minute + 5 < sample.EndsAt;
                        });
                        if (count <= bestCount) continue;
                        bestCount = count;
                        bestMinute = minute;
                    }
                    Check(bestCount >= 2, id + " guests must overlap under the shift timetable");
                    Check(GameSessionState.TryPrepareGameTimeAt(bestMinute), id + " prepared capture time");
                    city.DayNight.ApplyCurrentTime(true);
                    city.Night.Atmosphere.RefreshImmediate();
                    motor.Teleport(plan.Site.PublicThroughCenter + Vector3.up * PlayerFactory.GroundedRootOffset);
                    for (int i = 0; i < 6; i++) yield return null;
                    city.Pedestrians.ReleaseResidentPresentationsForTimeJump();
                    city.ResidentJourneys.Reset(bestMinute);
                    city.ResidentJourneys.SeedKioskAtCityStart();
                    Physics.SyncTransforms();
                    for (int i = 0; i < plan.Capacity; i++)
                        if (!city.Pedestrians.WarmKioskPopulation()) break;
                    for (int i = 0; i < 8; i++) yield return null;

                    var drinkers = city.Pedestrians.Actors.Where(actor => actor.IsSpawned &&
                        cast.Contains(city.Pedestrians.ResidentIndex(actor)) &&
                        actor.Presentation.GetComponent<BarPatronDrinkingArmPose>() is { isActiveAndEnabled: true })
                        .ToArray();
                    int seated = drinkers.Count(actor => actor.Presentation.IsSeated);
                    Debug.Log($"DISTRICT KIOSK {id} at {bestMinute}: {bestCount} scheduled, " +
                        $"{drinkers.Length} drinking, {seated} seated at {plan.Site.Origin:F2}.");
                    Check(drinkers.Length >= bestCount, id + " drinkers " + drinkers.Length + "/" + bestCount);
                    Check(plan.Residential ? seated >= 1 : seated == 0, id + " seated count " + seated);
                    var poses = drinkers.Select(actor => actor.Presentation.GetComponent<BarPatronDrinkingArmPose>()).ToArray();
                    float sipDeadline = Time.realtimeSinceStartup + 9f;
                    while (poses.Length > 0 && !poses.Any(IsSipping) && Time.realtimeSinceStartup < sipDeadline)
                        yield return null;
                    Check(poses.Any(IsSipping), id + " visible sip");
                    Capture(court, "company", company: true);

                    if (plan.Residential)
                    {
                        Check(ChairClearsTables(court), id + " chair at rest clears both tables");
                        // The departing visitor has cleared the seller's work spot.
                        int departing = cast.Single(person => residents.DistrictKioskVisit(person).Slot == 2);
                        int departure = Mathf.CeilToInt((float)residents.DistrictKioskVisit(departing).Departure);
                        Check(GameSessionState.TryPrepareGameTimeAt(departure + 2), id + " time after departure");
                        for (int i = 0; i < 6; i++) yield return null;
                        court.MarkDeparture(2);
                    }
                    else
                    {
                        for (int i = 0; i < 2; i++)
                        {
                            var bottle = VodkaBottleView.Create(court.transform, InventoryItemId.VodkaBottle);
                            bottle.SetRemaining(0f);
                            bottle.SetOpened(true);
                            Vector3 dock = court.BottleDock;
                            Check(court.ReturnBottle(bottle), id + " empty bottle " + i);
                            float baseY = bottle.GlassCells.Min(glass => glass.bounds.min.y);
                            Check(Mathf.Abs(baseY - dock.y) < .004f &&
                                bottle.Mouth.position.y - baseY > bottle.Profile.Height * .85f,
                                id + " upright bottle base contacts the crate floor " + i);
                        }
                    }

                    motor.Teleport(plan.Site.PublicThroughRight + Vector3.up * PlayerFactory.GroundedRootOffset);
                    float deadline = Time.realtimeSinceStartup + 24f;
                    while (station.Vendor.AtCounter && Time.realtimeSinceStartup < deadline) yield return null;
                    Check(!station.Vendor.AtCounter, id + " seller leaves the window");
                    motor.Teleport(plan.Site.Approach + Vector3.up * PlayerFactory.GroundedRootOffset);
                    Physics.SyncTransforms();
                    var camera = city.Camera;
                    camera.transform.rotation = Quaternion.LookRotation(station.InteractionPosition - camera.transform.position);
                    Check(station.CanInteract(hero), id + " service request at the window");
                    station.Interact(hero);

                    bool capturedChore = false, capturedChair = false;
                    float choreTime = 0f;
                    while (court.ChoresCompleted == 0 && Time.realtimeSinceStartup < deadline)
                    {
                        if (station.Vendor.IsWorkingInCourt) choreTime += Time.deltaTime;
                        if (!capturedChair && station.Vendor.IsAdjustingCourtChair)
                        {
                            Check(ChairClearsTables(court), id + " moving chair clears both tables");
                            Capture(court, "chair", company: false, chair: true);
                            capturedChair = true;
                        }
                        if (!capturedChore && choreTime >= (plan.Residential ? 1f : .95f))
                        {
                            Capture(court, "seller", company: false);
                            capturedChore = true;
                        }
                        yield return null;
                    }
                    Debug.Log($"DISTRICT KIOSK {id}: chores={court.ChoresCompleted}, returns={court.Returned}, " +
                        $"seller={station.Vendor.Actor.transform.position:F2}, dock={plan.StaffWork:F2}.");
                    Check(court.ChoresCompleted == 1 && !court.NeedsChore && court.Returned == 0, id + " completed chore");
                    Check(capturedChore, id + " captured seller at work");
                    if (plan.Residential)
                        Check(capturedChair && ChairClearsTables(court), id + " captured and reset chair clear of tables");
                    yield return null;
                    Check(station.Menu.IsOpen, id + " pending window request after chore");
                    station.Menu.Close();
                }
                Assert.That(failures, Is.Empty, string.Join("\n", failures));
            }
            finally { GameInput.HandleApplicationFocus(previousFocus); }

            bool IsSipping(BarPatronDrinkingArmPose pose)
                => pose && pose.Timeline.Phase == BarPatronDrinkPhase.Sip && pose.Timeline.PhaseElapsed > .35f;

            void Check(bool condition, string message)
            {
                if (!condition) failures.Add(message);
            }

            bool ChairClearsTables(DistrictKioskCourtWorld court)
            {
                var chair = CityPedestrianHandProps.FindSocket(court.transform, "LooseChair");
                var chairParts = chair.GetComponentsInChildren<MeshCollider>();
                Physics.SyncTransforms();
                for (int table = 0; table < 2; table++)
                    foreach (var tablePart in court.Table(table).GetComponentsInChildren<MeshCollider>())
                        foreach (var chairPart in chairParts)
                            if (chairPart.bounds.Intersects(tablePart.bounds)) return false;
                return true;
            }

            void Capture(DistrictKioskCourtWorld court, string name, bool company, bool chair = false)
            {
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
                var plan = court.Plan;
                var camera = city.Camera;
                var previous = new Pose(camera.transform.position, camera.transform.rotation);
                Vector3 target = company ? Vector3.Lerp(court.Table(0).position, court.Table(1).position, .5f) :
                    (chair ? plan.ChairWork : plan.StaffWork) + Vector3.up * .85f;
                Vector3 eye = company
                    ? (plan.Residential ? plan.Point(-6.5f, 3.55f) : plan.Point(-4.4f, -2.35f)) + Vector3.up * 2.15f
                    : (plan.Residential ? plan.Point(-1.88f, 1.72f) : plan.Point(-2.6f, -2.5f)) + Vector3.up * 1.79f;
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                try { AreaCaptureFixture.CaptureCurrentCamera(camera, "DistrictKioskCourts", plan.Site.StableId + "-" + name, 1280, 720); }
                finally { camera.transform.SetPositionAndRotation(previous.position, previous.rotation); }
            }
        }

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
            RidgeKioskMenuController menu = null;
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
                    Transform kiosk = (site.IsRidge ? city.World.RidgeRoot : city.World.Root).transform.Find(
                        site.IsRidge ? "Ridge Kiosk" : "Kiosk " + site.StableId);
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
                    var station = kiosk.GetComponentInChildren<RidgeKioskStation>();
                    Assert.That(station, Is.Not.Null);
                    yield return AimKioskAt(site, station.InteractionPosition);
                    Assert.That(hero.ActiveInteractable, Is.SameAs(station), site.StableId + " window prompt");
                    int cash = GameSessionState.CashBalance;
                    int count = GameSessionState.GetInventoryItemCount(InventoryItemId.VodkaBottle);
                    yield return PressKioskE();
                    menu = station.Menu;
                    Assert.That(menu.IsOpen, Is.True, "E must open the shelf menu at the window.");
                    Assert.That(menu.Select(3), Is.True);
                    Assert.That(menu.SelectedItemId, Is.EqualTo(vodka.ItemId));
                    menu.Exit();
                    Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash));
                    Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.VodkaBottle), Is.EqualTo(count));
                    yield return AimKioskAt(site, station.InteractionPosition);
                    yield return PressKioskE();
                    Assert.That(menu.IsOpen, Is.True);
                    Assert.That(menu.Select(3), Is.True);
                    // A newly opened shelf ignores the input frame that opened it.
                    yield return null;
                    Assert.That(menu.ConfirmSelection(), Is.True, $"{site.StableId}: {menu.FeedbackKey}");
                    Assert.That(receipt.IsPresenting, Is.True);
                    Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash - 28));
                    Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.VodkaBottle), Is.EqualTo(count + 1));
                    Assert.That(menu.ConfirmSelection(), Is.False, "A confirmation may credit only once.");
                    yield return WaitForKiosk(() => receipt.IsShowing, "The shared 3D vodka receipt did not settle.");
                    Assert.That(receipt.ActiveItemId, Is.EqualTo(InventoryItemId.VodkaBottle));
                    Assert.That(receipt.Dismiss(), Is.True);
                    yield return WaitForKiosk(() => !receipt.IsPresenting, "Closing the receipt retained its modal ownership.");
                    yield return null;
                    Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash - 28));
                    Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.VodkaBottle), Is.EqualTo(count + 1));
                    Assert.That(vodka.isActiveAndEnabled, Is.True, "The counter sample remains replenishable.");
                    Assert.That(menu.IsOpen && menu.SelectedItemId == vodka.ItemId, Is.True);
                    menu.Exit();
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
                menu?.Close();
                if (city != null)
                {
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
