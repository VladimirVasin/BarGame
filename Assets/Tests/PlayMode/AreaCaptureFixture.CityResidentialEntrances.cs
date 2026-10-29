using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class CityResidentialEntrancesAssetSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type.GetType("BarPromenade.Editor.CityResidentialCourtyardAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
            Type.GetType("BarPromenade.Editor.CityBuildingAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("All residential thresholds, physical access to public pavement, five imported forms and the bounded Home exterior.")]
        [PrebuildSetup(typeof(CityResidentialEntrancesAssetSetup))]
        public IEnumerator CityResidentialEntrances()
        {
            GameSessionState.BeginNewGame();
            Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
            CityGameRoot city = null;
            PlayerCameraFollow follow = null;
            GameTimeRuntime clock = null;
            CityResidentialCourtyardLife courtyardLife = null;
            VolumeProfile grayProfile = null;
            CityFountainReflectionController[] fountains = Array.Empty<CityFountainReflectionController>();
            bool[] fountainStates = Array.Empty<bool>();
            bool following = false, clockEnabled = false;
            try
            {
                yield return Capture(SceneIds.City, () =>
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    return city != null && city.IsInitialized && !CompositionDriver.IsComposing ? city : null;
                }, () =>
                {
                    city.Player.Motor.SetInputEnabled(false);
                    follow = city.Camera.GetComponent<PlayerCameraFollow>();
                    following = follow != null && follow.enabled;
                    if (follow != null) follow.enabled = false;
                    clock = Object.FindAnyObjectByType<GameTimeRuntime>();
                    clockEnabled = clock != null && clock.enabled;
                    if (clock != null) clock.enabled = false;
                    // This courtyard selection does not render the fountain.
                    // Its independent cubemap currently trips an unrelated
                    // Forward+ RenderGraph job error on repeated time cuts.
                    fountains = city.World.Root.GetComponentsInChildren<CityFountainReflectionController>(true);
                    fountainStates = new bool[fountains.Length];
                    for (int i = 0; i < fountains.Length; i++)
                    { fountainStates[i] = fountains[i].enabled; fountains[i].enabled = false; }
                    courtyardLife = city.World.ResidentialCourtyardRoot.GetComponent<CityResidentialCourtyardLife>();
                    courtyardLife.ForcePresentation = true; courtyardLife.AutoAdvance = false;
                    Debug.Log("RESIDENTIAL COURTYARD FIT: " + string.Join(",", city.World.ResidentialCourtyards.Courts
                        .Select(court => court.Variant + "@" + court.Cell)));
                    if (courtyardLife.Worker != null) courtyardLife.ApplyAt(16d);
                    var grayOwner = new GameObject("Courtyard acceptance grayscale");
                    grayOwner.transform.SetParent(city.transform, false);
                    Volume gray = grayOwner.AddComponent<Volume>();
                    gray.isGlobal = true; gray.priority = 10000f; gray.weight = 0f;
                    grayProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                    grayProfile.Add<ColorAdjustments>(true).saturation.Override(-100f);
                    gray.sharedProfile = grayProfile;
                    return ResidentialEntranceShots(city, gray);
                });
                AssertResidentialCourtyards(city);
                AssertResidentialEntrancesAndAccess(city);
                foreach (CityCourtyardResidentPresentation resident in city.CourtyardResidents)
                {
                    Assert.That(resident.HasSupportContacts, Is.True, resident.Descriptor.Activity.ToString());
                    Assert.That(resident.MaximumSupportContactError, Is.LessThan(.1f),
                        "Existing pocket hands must meet their physical support: " + resident.Descriptor.Activity);
                    if (resident.Descriptor.Activity == CityCourtyardResidentActivity.Sweeping)
                        Assert.That(resident.HasSweepingTool, Is.True, "The broom is one live source mesh outside the static batch.");
                }
                yield return AssertResidentialCourtyardLife(city, courtyardLife);

                SetChurchRemodelTime(12 * 60);
                yield return CaptureResidentialHomeExterior();
                Debug.Log("CITY RESIDENTIAL ENTRANCES AND COURTYARDS OK: clear measured entrances, three court functions, supported shared seats and basket contacts, finite pooled doorway light, phase/lifecycle and passive Home reuse.");
            }
            finally
            {
                if (follow != null) follow.enabled = following;
                if (clock != null) clock.enabled = clockEnabled;
                if (city != null) city.Player.Motor.SetInputEnabled(true);
                for (int i = 0; i < fountains.Length; i++)
                    if (fountains[i] != null) fountains[i].enabled = fountainStates[i];
                if (grayProfile != null) Object.Destroy(grayProfile);
            }
        }

        private static void AssertResidentialCourtyards(CityGameRoot city)
        {
            CityResidentialCourtyardPlan plan = city.World.ResidentialCourtyards;
            Debug.Log("RESIDENTIAL COURTYARD COVERAGE: courts=" + plan.Courts.Count + "; props=" + plan.Props.Count +
                "; variants=" + string.Join(",", plan.Courts.Select(court => court.Variant + "@" + court.Cell)));
            Assert.That(plan, Is.SameAs(CityResidentialCourtyardPlan.ForLayout(city.Layout)));
            Assert.That(plan.Courts.Count, Is.InRange(3, CityResidentialCourtyardPlan.MaximumCourtCount));
            Assert.That(plan.Courts.Select(court => court.Variant).Distinct().Count(), Is.EqualTo(3));
            Assert.That(plan.Props.Any(prop => prop.Kind == CityResidentialCourtyardPropKind.EntranceCanopy), Is.True);
            Assert.That(plan.Props.Any(prop => prop.Kind == CityResidentialCourtyardPropKind.Drain), Is.True);
            Assert.That(plan.Surfaces.Any(surface => surface.IsSheltered), Is.True);
            var solidIds = new HashSet<string>();
            foreach (CityResidentialCourtyardDescriptor court in plan.Courts)
            {
                Assert.That(court.Block.Primary.IsPlayerHome, Is.False);
                Assert.That(court.Props.Count, Is.LessThanOrEqualTo(CityResidentialCourtyardPlan.MaximumPropsPerCourt));
                float commonArea = court.CommonGroundPolygons.Sum(polygon => CityRoadPolygon.Area(polygon));
                float commonCoatingArea = court.Surfaces.Where(surface => surface.StableId.Contains("-common-space-"))
                    .Sum(surface => CityRoadPolygon.Area(surface.Polygon));
                Assert.That(commonArea, Is.GreaterThan(court.GroupBounds.width * court.GroupBounds.height * 2f),
                    "A real shared court extends beyond its furniture apron.");
                Assert.That(commonCoatingArea, Is.EqualTo(commonArea).Within(.05f), "The whole court owns continuous ground.");
                Assert.That(court.ConnectionPath.Count, Is.GreaterThanOrEqualTo(2));
                Assert.That(court.CirculationSpine.Count, Is.GreaterThanOrEqualTo(2));
                Assert.That(CityRoadPolygon.Area(court.OpenCenterPolygon), Is.EqualTo(16f).Within(.02f),
                    "A shared yard needs an actual open middle, not a decorated alley.");
                Assert.That(Physics.Raycast(court.CommonCenter + Vector3.up, Vector3.down, out RaycastHit commonFloor,
                    2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True);
                FootstepGround commonKind = commonFloor.collider.GetComponentInParent<FootstepGround>();
                Assert.That(commonKind, Is.Not.Null);
                Assert.That(commonKind.ResolveKind(commonFloor.triangleIndex),
                    Is.EqualTo(FootstepGroundKind.Concrete).Or.EqualTo(FootstepGroundKind.Stone),
                    "Shared courtyard paving must reach the real center floor.");
                Vector3 wall = court.WallEnd - court.WallStart;
                Vector3 fromWall = court.GroupPosition - court.WallStart;
                Assert.That(Vector3.Cross(fromWall, wall.normalized).magnitude, Is.LessThan(1.1f),
                    "Furniture inhabits a real housing edge rather than the court center.");
                foreach (CityResidentialCourtyardPropDescriptor prop in court.Props)
                {
                    Assert.That(solidIds.Add(prop.StableId), Is.True);
                    if (prop.Kind == CityResidentialCourtyardPropKind.UtilityShelter)
                    {
                        Assert.That(prop.MaximumSupportContactError,
                            Is.LessThanOrEqualTo(CityResidentialCourtyardPlan.MaximumShelterSupportError));
                        Assert.That(Vector3.Angle(prop.Rotation * Vector3.up, Vector3.up),
                            Is.LessThanOrEqualTo(CityResidentialCourtyardPlan.MaximumShelterSlopeDegrees));
                    }
                    Transform model = city.World.ResidentialCourtyardRoot.transform.Find(prop.StableId);
                    Assert.That(model, Is.Not.Null, prop.StableId);
                    Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
                    Assert.That(renderers, Is.Not.Empty);
                    Bounds measured = renderers[0].bounds;
                    foreach (Renderer renderer in renderers) measured.Encapsulate(renderer.bounds);
                    Assert.That(Vector3.Distance(measured.center, prop.Bounds.center), Is.LessThan(.035f), prop.StableId + " placed centre");
                    Assert.That(Vector3.Distance(measured.size, prop.Bounds.size), Is.LessThan(.045f), prop.StableId + " placed metre size");
                }
            }
            var seats = CityBenchRestPlanner.CreateCourtyardSeats(plan);
            Assert.That(seats, Is.Not.Empty);
            foreach (CityBenchSitPlan seat in seats)
            {
                Assert.That(city.BenchSits.Any(binding => binding.Plan.Id == seat.Id), Is.True, "Court seat joins shared player ownership.");
                Assert.That(city.World.WalkableArea.Contains(seat.EntryRootPosition, .25f), Is.True, "Court seat has a clear standing dock.");
                CityBenchRestPoint rest = city.BenchRests.Plan.Points.FirstOrDefault(point => point.BenchId == seat.Id);
                Assert.That(rest, Is.Not.Null, "The same court bench must be reachable by the roaming population.");
                Assert.That(rest.NodeDistances[rest.NodeIndex], Is.EqualTo(0f));
            }
            Assert.That(city.Night.Atmosphere.RealtimeLightCount, Is.LessThanOrEqualTo(CityNightAtmosphere.MaximumRealtimeLights));
            var ground = city.World.Root.GetComponent<CityGroundSurfaceSystem>();
            Assert.That(ground.ResidentialCourtyardTriangleCount, Is.GreaterThan(0), "Court masks must reach actual shared ground.");
        }

        private static IEnumerator AssertResidentialCourtyardLife(CityGameRoot city, CityResidentialCourtyardLife life)
        {
            Assert.That(life.Worker, Is.Not.Null);
            Assert.That(life.Worker.GetComponent<DefaultNpcAppearance>().AppearanceKey,
                Is.EqualTo(DefaultNpcPopulation.CityResidentialCourtyardCarrier));
            foreach (CityResidentialCourtyardLife.ContactSample sample in life.MeasureContactsAtSamples(
                new[] { 0d, 10d, 12d, 16d, 20d, 22d, 24d, 28d, 36d, 38d, 42d, 48d, 50d }))
            {
                if (sample.ContactWeight > .999f)
                {
                    Assert.That(sample.RightGripError, Is.LessThan(.07f), "Imported right basket grip at " + sample.Seconds);
                    Assert.That(sample.LeftGripError, Is.LessThan(.07f), "Imported left basket grip at " + sample.Seconds);
                }
                Assert.That(sample.HasLeftFootSurface && sample.HasRightFootSurface, Is.True, "Physical court below both boot soles.");
                Assert.That(Mathf.Min(sample.LeftSoleClearance, sample.RightSoleClearance), Is.InRange(-.09f, .12f),
                    "At least one actual boot sole supports the actor at " + sample.Seconds);
            }
            foreach (double seconds in new[] { 10d, 12d, 16d, 20d, 22d, 24d, 36d, 38d, 42d, 48d, 50d })
            {
                life.ApplyAt(seconds);
                Assert.That(life.HandsMatch, Is.True, "Real basket handle reach at " + seconds);
                Assert.That(life.HandContactError, Is.LessThan(.07f), "Real basket handle contact at " + seconds);
                Assert.That(Physics.Raycast(life.Worker.transform.position + Vector3.up * .4f,
                    Vector3.down, out RaycastHit hit, .8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True);
                Assert.That(Mathf.Abs(hit.point.y - life.Worker.transform.position.y), Is.LessThan(.08f), "Carrier feet meet the court.");
            }
            life.ApplyAt(0d);
            Assert.That(Vector3.Distance(life.Basket.position, life.Court.BasketRestPosition), Is.LessThan(.005f));
            life.ApplyAt(28d);
            Assert.That(Vector3.Distance(life.Basket.position, life.Court.BasketEndPosition), Is.LessThan(.005f));
            life.ApplyAt(16d);
            using (GameTimeScaleRuntime.AcquirePause())
            {
                Vector3 held = life.Basket.position;
                yield return null;
                Assert.That(Vector3.Distance(life.Basket.position, held), Is.LessThan(.0001f));
                life.gameObject.SetActive(false); life.gameObject.SetActive(true);
                yield return null;
                Assert.That(Vector3.Distance(life.Basket.position, held), Is.LessThan(.0001f));
                Assert.That(life.HandContactError, Is.LessThan(.07f));
            }
            var observer = new GameObject("Courtyard distance acceptance");
            string identity = life.Worker.GetComponent<DefaultNpcAppearance>().AppearanceKey;
            try
            {
                life.PresentationObserver = observer.transform; life.ForcePresentation = false;
                observer.transform.position = life.Court.ServiceProp.Position + Vector3.forward * 180f;
                yield return null;
                Assert.That(life.IsPresentationActive, Is.False);
                Assert.That(life.Worker.GetComponent<Collider>().enabled, Is.True, "Distance retains physical people.");
                observer.transform.position = life.Court.ServiceProp.Position;
                yield return null;
                Assert.That(life.IsPresentationActive, Is.True);
                Assert.That(life.Worker.GetComponent<DefaultNpcAppearance>().AppearanceKey, Is.EqualTo(identity));
                Assert.That(life.HandContactError, Is.LessThan(.07f));
            }
            finally { Object.Destroy(observer); }
        }

        private static void AssertResidentialEntrancesAndAccess(CityGameRoot city)
        {
            CityLayout layout = city.Layout;
            CityResidentialEntrancePlan plan = CityResidentialEntrancePlan.ForLayout(layout);
            BuildingLot[] homes = layout.BuildingMasses.Where(lot =>
                lot.IsOrdinaryBuilding && lot.District == CityDistrictKind.Residential).ToArray();
            Assert.That(plan.Entrances.Count, Is.EqualTo(homes.Length));
            Assert.That(plan.Entrances.Count, Is.GreaterThan(0));
            CityBuildingAssetRegistry[] models = city.World.Root.GetComponentsInChildren<CityBuildingAssetRegistry>(true);
            var claimed = new HashSet<CityBuildingAssetRegistry>();
            var hits = new RaycastHit[64];
            var overlaps = new Collider[128];
            var failures = new List<string>();
            float[] lateralOffsets = { -.3f, 0f, .3f };
            Physics.SyncTransforms();
            foreach (var entry in plan.Entrances)
            {
                Assert.That(homes.Contains(entry.Lot), Is.True);
                CityBuildingPrototypePose pose = CityBuildingPrototypePlacement.ResolveExpectedCityPose(entry.Lot);
                CityBuildingAssetRegistry model = models.FirstOrDefault(candidate =>
                    candidate.District == CityDistrictKind.Residential &&
                    Vector3.Distance(candidate.transform.position, pose.Position) < .01f);
                Assert.That(model, Is.Not.Null, "Missing placed residential body at " + entry.Lot.Cell);
                Assert.That(claimed.Add(model), Is.True, "Each physical body owns its own entrance.");
                Assert.That(model.HasEntrance, Is.True, model.StableId);
                Assert.That(Vector3.Distance(model.transform.TransformPoint(model.LocalEntranceThreshold), entry.Threshold),
                    Is.LessThan(.01f), "The imported entrance must agree with its world path.");
                Assert.That(Vector3.Dot(model.transform.TransformDirection(model.LocalEntranceOutward).normalized,
                    entry.Outward.normalized), Is.GreaterThan(.999f));
                Assert.That(entry.Points.Count, Is.GreaterThanOrEqualTo(2));
                Assert.That(Vector3.Distance(entry.Points[0], entry.Threshold), Is.LessThan(.01f));
                Assert.That(entry.Paving, Is.Not.Empty);

                // The same person can step behind the facade and back out;
                // cutting only the visible facade leaves a solid mass/skirt here.
                for (float depth = .2f; depth <= 1.3f; depth += .35f)
                    ProbeResidentialFootstep(entry.Threshold - entry.Outward * depth, city, hits, overlaps,
                        failures, entry.Lot.Cell + " vestibule", requireNavigation: true);
                for (int leg = 1; leg < entry.Points.Count; leg++)
                {
                    Vector3 a = entry.Points[leg - 1], b = entry.Points[leg];
                    int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / .4f));
                    Vector3 tangent = b - a; tangent.y = 0f;
                    Vector3 side = Vector3.Cross(Vector3.up, tangent.normalized);
                    for (int step = 0; step <= steps; step++)
                    {
                        Vector3 point = Vector3.Lerp(a, b, step / (float)steps);
                        foreach (float offset in lateralOffsets)
                        {
                            Vector3 foot = point + side * offset;
                            if (plan.TrySampleTop(new Vector2(foot.x, foot.z), out float top)) foot.y = top;
                            else AddResidentialFailure(failures, entry.Lot.Cell + " missing paved support at " + foot);
                            ProbeResidentialFootstep(foot, city, hits, overlaps, failures,
                                entry.Lot.Cell + " route leg " + leg, requireNavigation: true);
                        }
                    }
                }
            }
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        private static void ProbeResidentialFootstep(Vector3 foot, CityGameRoot city, RaycastHit[] hits,
            Collider[] overlaps, List<string> failures, string context, bool requireNavigation)
        {
            if (failures.Count >= 24) return;
            int count = Physics.RaycastNonAlloc(foot + Vector3.up * .7f, Vector3.down, hits, 1.4f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Assert.That(count, Is.LessThan(hits.Length), "Residential ground probe buffer must not truncate.");
            bool supported = false;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (!hit.transform.IsChildOf(city.World.Root.transform) || hit.normal.y < .7f) continue;
                float error = Mathf.Abs(hit.point.y - foot.y);
                nearest = Mathf.Min(nearest, error);
                if (error <= .035f) supported = true;
            }
            if (!supported) AddResidentialFailure(failures,
                context + " no physical floor at " + foot.ToString("F3") + "; closest height error=" + nearest);
            if (requireNavigation && !city.World.WalkableArea.Contains(foot, .25f))
                AddResidentialFailure(failures, context + " missing walkable area at " + foot.ToString("F3"));

            const float radius = .27f;
            int solidCount = Physics.OverlapCapsuleNonAlloc(foot + Vector3.up * (.3f + radius),
                foot + Vector3.up * (1.75f - radius), radius, overlaps,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Assert.That(solidCount, Is.LessThan(overlaps.Length), "Residential clearance probe must not truncate.");
            for (int i = 0; i < solidCount; i++)
            {
                Collider obstacle = overlaps[i];
                if (obstacle.transform.IsChildOf(city.Player.GameObject.transform) ||
                    obstacle.GetComponentInParent<DefaultNpcAppearance>() != null ||
                    obstacle.GetComponentInParent<CityPedestrianActor>() != null) continue;
                AddResidentialFailure(failures, context + " blocked by " + obstacle.name + " at " + foot.ToString("F3"));
            }
        }

        private static void AddResidentialFailure(List<string> failures, string message)
        {
            if (failures.Count < 24) failures.Add(message);
        }

        private static Shot[] ResidentialEntranceShots(CityGameRoot city, Volume gray)
        {
            var shots = new List<Shot>();
            CityResidentialEntrancePlan plan = CityResidentialEntrancePlan.ForLayout(city.Layout);
            for (int variant = 0; variant < CityBuildingAssetProvider.GetVariantCount(CityDistrictKind.Residential); variant++)
            {
                var entry = plan.Entrances.FirstOrDefault(candidate => candidate.Lot.BuildingVariant == variant);
                Assert.That(entry, Is.Not.Null, "The default City must expose residential form " + variant);
                Vector3 side = Vector3.Cross(Vector3.up, entry.Outward);
                Add("residential-entry-form-" + (variant + 1), entry.Threshold + entry.Outward * 3.8f +
                    side * 1.2f + Vector3.up * EyeHeight, entry.Threshold - entry.Outward * .7f + Vector3.up * 1.15f);
            }
            var longest = plan.Entrances.OrderByDescending(entry => entry.Points.Count).First();
            Vector3 end = longest.Points[longest.Points.Count - 1];
            Vector3 towardEntry = longest.Threshold - end; towardEntry.y = 0f;
            if (towardEntry.sqrMagnitude < .01f) towardEntry = -longest.Outward;
            Add("residential-entry-public-connection", end - towardEntry.normalized * 3f + Vector3.up * EyeHeight,
                longest.Threshold + Vector3.up * .8f, 76f);
            var night = plan.Entrances[0];
            Add("residential-entry-night", night.Threshold + night.Outward * 4f + Vector3.up * EyeHeight,
                night.Threshold - night.Outward * .7f + Vector3.up * 1.15f, night: true);
            foreach (CityResidentialCourtyardDescriptor court in city.World.ResidentialCourtyards.Courts.Take(3))
            {
                Vector3 target = Vector3.Lerp(court.CommonCenter, court.GroupPosition, .2f) + Vector3.up * .65f;
                Vector3 preferred = court.CommonCenter + court.GroupForward * 2.4f + Vector3.up * EyeHeight;
                Vector3 eye = ResidentialCourtEye(city, court, preferred, target);
                Add("residential-court-" + court.Variant + "-day", eye, target, 82f);
                Add("residential-court-" + court.Variant + "-night", eye, target, 82f, night: true);
                Vector3 exit = court.ConnectionPath[court.ConnectionPath.Count - 1] + Vector3.up * .9f;
                Vector3 reverse = ResidentialCourtEye(city, court,
                    court.GroupPosition + court.GroupForward * 1.8f + Vector3.up * EyeHeight, exit);
                Add("residential-court-" + court.Variant + "-to-entrances", reverse, exit, 84f);
                Add("residential-court-" + court.Variant + "-layout",
                    court.CommonCenter + Vector3.up * 6.8f + court.GroupForward * 1.5f,
                    court.CommonCenter + Vector3.up * .1f, 78f);
            }
            CityResidentialCourtyardDescriptor service = city.World.ResidentialCourtyards.Courts.FirstOrDefault(court => court.ServiceProp != null);
            if (service != null)
                Add("residential-court-service-contact", ResidentialCourtEye(city, service,
                    service.ServiceProp.World(new Vector3(2.8f, 1.6f, 3.1f)), service.ServiceProp.World(new Vector3(0f, 1f, .55f))),
                    service.ServiceProp.World(new Vector3(0f, 1.0f, .55f)), 70f);
            CityResidentialCourtyardPropDescriptor litEntry = city.World.ResidentialCourtyards.Props.First(prop =>
                prop.Kind == CityResidentialCourtyardPropKind.EntranceCanopy);
            Add("residential-court-door-light", litEntry.World(new Vector3(-2.2f, 1.65f, 4f)),
                litEntry.World(new Vector3(.3f, 1.3f, .45f)), 66f, night: true);
            if (service != null)
                Add("residential-court-grayscale", ResidentialCourtEye(city, service,
                    service.GroupPosition + service.GroupForward * 5f + Vector3.up * EyeHeight,
                    service.GroupPosition + service.GroupForward + Vector3.up),
                    service.GroupPosition + service.GroupForward + Vector3.up, 82f, grayscale: true);
            foreach (CityCourtyardResidentPresentation resident in city.CourtyardResidents)
                Add("residential-pocket-contact-" + resident.Descriptor.Activity + "-" + resident.Descriptor.StableId.Replace('/', '-'),
                    resident.transform.position + resident.transform.forward * 2.6f + resident.transform.right * .8f + Vector3.up * 1.65f,
                    resident.transform.position + resident.transform.forward * .35f + Vector3.up * .9f, 65f);
            return shots.ToArray();

            void Add(string name, Vector3 eye, Vector3 target, float fov = 66f, bool night = false, bool grayscale = false)
            {
                int frames = 0;
                Shot pose = Shot.At(name, eye, target, fov);
                shots.Add(Shot.At(name, eye, target, fov, readyWhen: () =>
                {
                    if (frames++ == 0)
                    {
                        SetChurchRemodelTime(night ? 23 * 60 : 12 * 60);
                        city.DayNight.ApplyCurrentTime(true);
                        gray.weight = grayscale ? 1f : 0f;
                        MoveCityLightingObserver(city, pose);
                    }
                    return frames > SettleFrames;
                }));
            }
        }

        private static Vector3 ResidentialCourtEye(CityGameRoot city, CityResidentialCourtyardDescriptor court,
            Vector3 preferred, Vector3 target)
        {
            var candidates = new List<Vector3> { preferred - Vector3.up * EyeHeight };
            Vector3 side = Vector3.Cross(Vector3.up, court.GroupForward);
            for (float depth = 2.5f; depth <= 8f; depth += 1.1f)
            for (int offset = -2; offset <= 2; offset++)
                candidates.Add(court.GroupPosition + court.GroupForward * depth + side * (offset * 1.2f));
            foreach (Vector3 point in court.CirculationSpine) candidates.Add(point);
            foreach (Vector3 point in court.ConnectionPath) candidates.Add(point);
            foreach (Vector2[] polygon in court.CommonGroundPolygons)
            {
                Vector2 center = Vector2.zero;
                foreach (Vector2 vertex in polygon) center += vertex;
                center /= polygon.Length;
                candidates.Add(new Vector3(center.x, court.GroupPosition.y, center.y));
            }
            Vector3 best = preferred;
            float score = float.PositiveInfinity;
            bool found = false;
            foreach (Vector3 candidate in candidates)
            {
                if (!city.World.WalkableArea.Contains(candidate, .25f)) continue;
                if (!Physics.Raycast(candidate + Vector3.up * .6f, Vector3.down, out RaycastHit floor,
                    1.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                Vector3 eye = floor.point + Vector3.up * EyeHeight;
                float distance = Vector3.Distance(eye, target);
                if (distance < 2.2f || distance > 18f || Physics.CheckSphere(eye, .13f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                if (Physics.Linecast(eye, target, out RaycastHit obstruction, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore) && obstruction.distance < distance - 1.3f) continue;
                float candidateScore = (eye - preferred).sqrMagnitude;
                if (candidateScore >= score) continue;
                best = eye; score = candidateScore; found = true;
            }
            Assert.That(found, Is.True, "A shared court needs an actual unobstructed human-height view: " + court.StableId);
            return best;
        }

        private static IEnumerator CaptureResidentialHomeExterior()
        {
            GameSessionState.EnterHome();
            yield return SceneManager.LoadSceneAsync(SceneIds.HomeInterior, LoadSceneMode.Single);
            HomeInteriorRoot home = null;
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                home = Object.FindAnyObjectByType<HomeInteriorRoot>();
                if (home != null && home.IsInitialized) break;
                yield return null;
            }
            Assert.That(home != null && home.IsInitialized, Is.True);
            HomeExteriorContextPlan context = home.ExteriorContext;
            CityResidentialEntrancePlan plan = CityResidentialEntrancePlan.ForLayout(context.Layout);
            var models = home.ExteriorView.GetComponentsInChildren<CityBuildingAssetRegistry>(true)
                .Where(model => model.District == CityDistrictKind.Residential).ToArray();
            Assert.That(models, Is.Not.Empty);
            foreach (CityBuildingAssetRegistry model in models)
            {
                Assert.That(model.HasEntrance, Is.True);
                Vector3 localThreshold = model.transform.TransformPoint(model.LocalEntranceThreshold);
                Assert.That(plan.Entrances.Any(entry => Vector3.Distance(localThreshold,
                    PlayerHomeBalconyGeometry.ToHomeLocal(context.PlayerHome, entry.Threshold)) < .01f), Is.True,
                    "The Home view must preserve the exact City's entry pose.");
            }
            foreach (Collider collider in home.ExteriorView.GetComponentsInChildren<Collider>(true))
                Assert.That(collider.enabled, Is.False, "The bounded Home exterior remains passive.");
            Assert.That(home.ExteriorView.Find(CityResidentialEntranceWorldBuilder.RootName), Is.Not.Null);
            Transform ground = home.ExteriorView.Find("City Ground");
            Assert.That(ground, Is.Not.Null);
            MeshFilter[] sectors = ground.GetComponentsInChildren<MeshFilter>(true);
            int checkedSurfaces = 0;
            foreach (var entry in plan.Entrances)
            {
                if (!context.NearbyLots.Contains(entry.Lot) || entry.Points.Count < 2) continue;
                // Sample beyond the mouth so the shared ground must carry the
                // paving, rather than accidentally proving the model's floor.
                Vector3 cityPoint = Vector3.Lerp(entry.Points[0], entry.Points[1], .7f);
                Vector3 localPoint = PlayerHomeBalconyGeometry.ToHomeLocal(context.PlayerHome, cityPoint);
                if (localPoint.x < HomeExteriorViewBuilder.ExteriorMinimumX + .05f) continue;
                Assert.That(HasRenderedResidentialGround(sectors, localPoint), Is.True,
                    "The Home ground sectors must retain the City's graded approach at " + localPoint.ToString("F3"));
                checkedSurfaces++;
            }
            Assert.That(checkedSurfaces, Is.GreaterThan(0));
            Rect activation = home.BalconyLayout.BalconyCameraActivationBounds;
            home.Player.Motor.Teleport(new Vector3(activation.center.x, home.Layout.PlayerSpawn.y, activation.center.y));
            for (int frame = 0; frame < SettleFrames; frame++) yield return null;
            Assert.That(home.ExteriorAtmosphere.IsBalconyVisibilityActive, Is.True);
            Camera camera = home.CameraFollow.GetComponent<Camera>();
            CaptureCurrentCamera(camera, SceneIds.HomeInterior, "residential-entry-home-balcony");
        }

        private static bool HasRenderedResidentialGround(IEnumerable<MeshFilter> sectors, Vector3 point)
        {
            foreach (MeshFilter filter in sectors)
            {
                Renderer renderer = filter.GetComponent<Renderer>();
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                Bounds bounds = renderer.bounds;
                if (point.x < bounds.min.x - .01f || point.x > bounds.max.x + .01f ||
                    point.z < bounds.min.z - .01f || point.z > bounds.max.z + .01f) continue;
                Mesh mesh = filter.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                Vector3 local = filter.transform.InverseTransformPoint(point);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                    Vector2 ab = new Vector2(b.x - a.x, b.z - a.z), ac = new Vector2(c.x - a.x, c.z - a.z);
                    float determinant = ab.x * ac.y - ab.y * ac.x;
                    if (Mathf.Abs(determinant) < .000001f) continue;
                    Vector2 ap = new Vector2(local.x - a.x, local.z - a.z);
                    float u = (ap.x * ac.y - ap.y * ac.x) / determinant;
                    float v = (ab.x * ap.y - ab.y * ap.x) / determinant;
                    if (u < -.0001f || v < -.0001f || u + v > 1.0001f) continue;
                    float top = a.y + u * (b.y - a.y) + v * (c.y - a.y);
                    if (Mathf.Abs(top - local.y) < .035f) return true;
                }
            }
            return false;
        }
    }
}
