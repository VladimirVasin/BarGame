using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest, Timeout(360000)]
        public IEnumerator LiveCity_CoveredPassagesKeepUniqueDressingLightAndClearRoutes()
        {
            Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
            yield return LoadProductionCity();
            Object.FindAnyObjectByType<GameTimeRuntime>().enabled = false;
            city.Weather.enabled = false;
            city.Player.Motor.SetInputEnabled(false);
            city.Player.Interactor.SetInputEnabled(false);
            var follow = city.Camera.GetComponent<PlayerCameraFollow>();
            follow.SetOrbitInputEnabled(false);
            isolatedHeroCapsule = city.Player.Motor.GetComponent<CharacterController>();
            var atmosphere = city.Night.Atmosphere;
            var expected = city.Layout.BuildingMasses.Concat(CityHillsideNeighborhoodPlan.ForLayout(city.Layout)
                    .AllBuildings.Select(house => house.Lot)).GroupBy(lot => lot.Cell).Select(group => group.First())
                .Where(lot => CityCoveredPassagePlan.TryCreate(lot, city.Layout.Seed, out _)).ToArray();
            var passages = city.World.Root.GetComponentsInChildren<CityCoveredPassageWorld>()
                .OrderBy(passage => passage.Plan.Id).ToArray();
            Assert.That(passages.Select(passage => passage.Plan.Lot.Cell), Is.EquivalentTo(expected.Select(lot => lot.Cell)));
            Assert.That(passages.Length, Is.GreaterThan(1));
            Assert.That(passages.Select(passage => passage.DressingSignature).Distinct().Count(), Is.EqualTo(passages.Length),
                "Every actual opening needs its own physical arrangement, beyond a unique object name.");
            Assert.That(passages.Select(passage => SlotPattern(passage.Plan)).Distinct().Count(),
                Is.GreaterThanOrEqualTo(Mathf.Min(4, passages.Length)),
                "Occupancy and decoration kinds must vary, beyond small positional jitter.");
            Assert.That(passages.SelectMany(passage => passage.Plan.WallSlots)
                .Any(slot => slot.Kind == CityCoveredPassageWallSlotKind.Empty), Is.True, "The wall layout must retain real vacant positions.");
            Assert.That(passages.Any(passage => !passage.Plan.WallSlots.Where(slot => slot.Side < 0).OrderBy(slot => slot.Index)
                .Select(slot => slot.Kind).SequenceEqual(passage.Plan.WallSlots.Where(slot => slot.Side > 0)
                    .OrderBy(slot => slot.Index).Select(slot => slot.Kind))), Is.True,
                "The two walls must not always mirror one categorical occupancy pattern.");
            var faulty = passages.Select(passage => passage.Lights.Single(light => light.Fixture.IsFaulty)).ToArray();
            Assert.That(faulty.Select(light => light.Fixture.Seed).Distinct().Count(), Is.EqualTo(faulty.Length));
            foreach (var light in faulty)
            {
                Assert.That(CityCoveredPassageFlickerEvaluator.IntervalSeconds(light.Fixture.Seed), Is.InRange(19d, 37d));
                Assert.That(Enumerable.Range(0, 1200).Any(step => CityCoveredPassageFlickerEvaluator.Evaluate(step * .1d,
                    light.Fixture.Seed) < .2f), Is.True, "The fault must actually dip, rather than merely vary its seed.");
            }
            Assert.That(Enumerable.Range(0, 1200).Any(step => Mathf.Abs(
                CityCoveredPassageFlickerEvaluator.Evaluate(step * .1d, faulty[0].Fixture.Seed) -
                CityCoveredPassageFlickerEvaluator.Evaluate(step * .1d, faulty[1].Fixture.Seed)) > .5f), Is.True,
                "Different passages must not flash on one synchronized clock.");
            bool slotsPreview = System.Environment.GetEnvironmentVariable("BAR_PROMENADE_CAPTURE_PASSAGE_SLOTS") == "1";
            var previewIds = new HashSet<string>(passages.GroupBy(passage => SlotPattern(passage.Plan))
                .Select(group => group.First()).Take(3).Select(passage => passage.Plan.Id));
            previewIds.Add(passages.Single(passage => passage.Plan.Lot.Cell == new Vector2Int(40, -21)).Plan.Id);
            bool capture = (slotsPreview || System.Environment.GetEnvironmentVariable("BAR_PROMENADE_CAPTURE_CITY_PASSAGES") == "1") &&
                SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            int previousInterval = OnDemandRendering.renderFrameInterval;
            GameObject homeCopyParent = null;
            CityCoveredPassageWorld disabledPassage = null;
            bool capsuleEnabled = isolatedHeroCapsule.enabled;
            bool unpairedMural = false, unpairedRepair = false;
            try
            {
                Time.captureDeltaTime = .1f;
                OnDemandRendering.renderFrameInterval = 24;
                foreach (var passage in passages)
                {
                    var plan = passage.Plan;
                    Assert.That(passage.Lights.Count, Is.InRange(2, 4), plan.Id);
                    Assert.That(passage.Lights.Count(light => !light.Fixture.IsFaulty), Is.GreaterThanOrEqualTo(1), plan.Id);
                    foreach (int side in new[] { -1, 1 })
                    {
                        var slots = plan.WallSlots.Where(slot => slot.Side == side).ToArray();
                        var occupied = slots.Where(slot => slot.Kind != CityCoveredPassageWallSlotKind.Empty).ToArray();
                        Assert.That(slots.Length, Is.InRange(3, 5), plan.Id + " wall " + side);
                        Assert.That(slots.Select(slot => Mathf.Min(2, Mathf.FloorToInt(Mathf.InverseLerp(
                            plan.LocalBounds.min.z, plan.LocalBounds.max.z, slot.AlongZ) * 3f))).Distinct().Count(), Is.EqualTo(3),
                            plan.Id + " needs available near/middle/far positions on each wall.");
                        Assert.That(occupied.Length, Is.GreaterThanOrEqualTo(3), plan.Id + " wall " + side);
                        Assert.That(occupied.Count(slot => slot.Kind == CityCoveredPassageWallSlotKind.Graffiti), Is.InRange(1, 2));
                        Assert.That(occupied.Max(slot => slot.AlongZ) - occupied.Min(slot => slot.AlongZ),
                            Is.GreaterThan(plan.LocalBounds.size.z * .3f), "Occupied details must extend along the wall.");
                    }
                    Assert.That(CityCoveredPassagePlan.TryCreate(plan.Lot, city.Layout.Seed, out var repeated), Is.True);
                    Assert.That(plan.WallSlots.SequenceEqual(repeated.WallSlots), Is.True, "A return must retain the whole slot plan.");
                    Assert.That(passage.GraffitiCount, Is.EqualTo(plan.WallSlots.Count(slot => slot.Kind == CityCoveredPassageWallSlotKind.Graffiti)));
                    Assert.That(passage.DetailCount, Is.EqualTo(plan.WallSlots.Count(slot => slot.Kind != CityCoveredPassageWallSlotKind.Empty &&
                        slot.Kind != CityCoveredPassageWallSlotKind.Graffiti)));
                    foreach (var slot in plan.WallSlots.Where(slot => slot.Kind != CityCoveredPassageWallSlotKind.Empty))
                    {
                        string model = slot.Kind == CityCoveredPassageWallSlotKind.Graffiti ? "GraffitiPanel" :
                            slot.Kind == CityCoveredPassageWallSlotKind.Repair ? "PlasterPatch" : slot.Kind.ToString();
                        Assert.That(passage.transform.Cast<Transform>().Any(detail => detail.name == model &&
                            Mathf.Sign(detail.localPosition.x) == slot.Side && Mathf.Abs(detail.localPosition.z - slot.AlongZ) < .001f &&
                            Mathf.Abs(detail.localPosition.y - plan.LocalBounds.min.y - slot.Height) < .001f), Is.True,
                            plan.Id + " did not instantiate its independent occupied slot " + sideLabel(slot.Side, slot.Index));
                    }
                    var murals = passage.transform.Cast<Transform>().Where(detail => detail.name == "GraffitiPanel").ToArray();
                    var repairs = passage.transform.Cast<Transform>().Where(detail => detail.name == "PlasterPatch").ToArray();
                    foreach (Transform mural in murals)
                    {
                        bool overlappingRepair = false;
                        foreach (Transform repair in repairs)
                        {
                            if (Vector3.Dot(mural.forward, repair.forward) < .999f ||
                                !WallFootprint(mural, passage.transform).Overlaps(WallFootprint(repair, passage.transform))) continue;
                            overlappingRepair = true;
                            float front = float.NegativeInfinity;
                            foreach (var mesh in repair.GetComponentsInChildren<MeshFilter>())
                            {
                                Bounds local = mesh.sharedMesh.bounds;
                                for (int corner = 0; corner < 8; corner++)
                                {
                                    Vector3 point = local.center + Vector3.Scale(local.extents, new Vector3(
                                        (corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                                    front = Mathf.Max(front, Vector3.Dot(mesh.transform.TransformPoint(point), mural.forward));
                                }
                            }
                            Assert.That(Vector3.Dot(mural.position, mural.forward), Is.GreaterThan(front + .002f),
                                plan.Id + ": the visible paint must lie in front of its actual imported repair surface.");
                        }
                        if (!overlappingRepair) unpairedMural = true;
                    }
                    if (repairs.Any(repair => !murals.Any(mural => Vector3.Dot(mural.forward, repair.forward) > .999f &&
                        WallFootprint(mural, passage.transform).Overlaps(WallFootprint(repair, passage.transform))))) unpairedRepair = true;
                    Assert.That(passage.GetComponentsInChildren<Collider>(true), Is.Empty,
                        "Wall and ceiling dressing must not introduce a blocking prop or a second building collider.");
                    Bounds actual = LocalRendererBounds(passage.transform), permitted = plan.LocalBounds;
                    permitted.Expand(.16f);
                    Assert.That(permitted.Contains(actual.min) && permitted.Contains(actual.max), Is.True,
                        plan.Id + " dressing escaped its actual covered opening: " + actual);
                    foreach (var marker in passage.Lights)
                    {
                        Assert.That(marker.PlanId, Is.EqualTo(plan.Id));
                        Assert.That(marker.LensRenderer, Is.Not.Null);
                        Assert.That(Vector3.Distance(marker.transform.position, plan.ToWorldPoint(marker.Fixture.LocalPosition)),
                            Is.LessThan(.01f), "The physical lens and cached source must share the authored metre pose.");
                    }
                }
                Assert.That(unpairedMural && unpairedRepair, Is.True,
                    "Some graffiti must occupy original wall and some repairs must remain unpainted, independently.");
                // The same imported kit and identity must survive the rotated,
                // bounded Home reconstruction without an extra City load.
                homeCopyParent = new GameObject("Covered passage Home verification");
                homeCopyParent.SetActive(false);
                var sample = passages[0];
                var homePose = CityBuildingPrototypePlacement.ResolveHomePose(city.Layout.PlayerHome, sample.Plan.Pose);
                var homeCopy = CityCoveredPassageWorld.Build(homeCopyParent.transform, sample.Plan.Lot, city.Layout.Seed, homePose);
                Assert.That(homeCopy.DressingSignature, Is.EqualTo(sample.DressingSignature));
                homeCopyParent.SetActive(true);
                Assert.That(Vector3.Distance(LocalRendererBounds(homeCopy.transform).size,
                    LocalRendererBounds(sample.transform).size), Is.LessThan(.01f), "Home retains actual imported metres.");
                foreach (var marker in homeCopy.Lights)
                    Assert.That(Vector3.Distance(marker.transform.position, PlayerHomeBalconyGeometry.ToHomeLocal(
                        city.Layout.PlayerHome, sample.Plan.ToWorldPoint(marker.Fixture.LocalPosition))), Is.LessThan(.01f));
                homeCopyParent.SetActive(false);

                // Catalogue openings can stand above sloping original ground.
                // Observational lenses/pool focus do not require a new floor.
                isolatedHeroCapsule.enabled = false;
                foreach (int minute in new[] { 12 * 60, 0 })
                {
                    AdvanceRidgeClock(minute);
                    for (int index = 0; index < passages.Length; index++)
                    {
                        var passage = passages[index];
                        if (slotsPreview && !previewIds.Contains(passage.Plan.Id)) continue;
                        var center = passage.Plan.LocalBounds.center; center.y = passage.Plan.LocalBounds.min.y;
                        city.Player.Motor.Teleport(passage.transform.TransformPoint(center) + Vector3.up * PlayerFactory.GroundedRootOffset);
                        Physics.SyncTransforms(); atmosphere.RefreshImmediate(); yield return null;
                        Assert.That(atmosphere.RealtimeLightCount, Is.LessThanOrEqualTo(CityNightAtmosphere.MaximumRealtimeLights));
                        Assert.That(passage.Lights.Where(marker => !marker.Fixture.IsFaulty).Any(marker =>
                            Lease(marker) != null && Lease(marker).intensity > .001f), Is.True,
                            passage.Plan.Id + " needs a real steady source while the faulty tube dips.");
                        foreach (var marker in passage.Lights)
                        {
                            Assert.That(marker.PowerMultiplier, Is.EqualTo(passage.Plan.EvaluateFactor(marker.Fixture.Index,
                                CityCoveredPassageFlickerEvaluator.AbsoluteSeconds)).Within(.001f));
                            Assert.That(marker.FixtureHalo.IntensityFactor, Is.EqualTo(marker.AppliedFactor).Within(.001f));
                        }
                        if (capture)
                            foreach (int side in new[] { -1, 1 })
                            {
                                SetPassageView(passage, side);
                                AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "CoveredPassages",
                                    $"{minute:0000}-{index:00}-{(side < 0 ? "rear" : "front")}", 960, 540);
                            }
                    }
                }
                isolatedHeroCapsule.enabled = capsuleEnabled;
                if (slotsPreview) yield break;
                var sharedRoute = CityHillsideAccessPlan.ForLayout(city.Layout).SharedCourt.RouteWorld;
                var representative = passages.Single(passage => passage.Plan.Lot.Cell == new Vector2Int(40, -21));
                Debug.Log("Covered passage traversal: existing authored shared-court route, " + representative.Plan.Id);
                yield return RidgeStand(sharedRoute[0]);
                yield return RidgeWalk(sharedRoute, "covered shared-court route");
                // Survey actual static floors from below the ceiling, not a
                // narrow window around the prototype's nominal origin. A missing
                // legacy floor is recorded rather than turned into unrelated work.
                List<Vector3> lowerRoute = null;
                foreach (var passage in passages.Where(passage => passage != representative))
                {
                    var route = new List<Vector3>();
                    bool supported = true;
                    for (int i = 0; i <= 6; i++)
                    {
                        Bounds bounds = passage.Plan.LocalBounds;
                        Vector3 local = new Vector3(0f, bounds.max.y - .12f,
                            Mathf.Lerp(bounds.min.z + .30f, bounds.max.z - .30f, i / 6f));
                        Vector3 origin = passage.transform.TransformPoint(local);
                        var floors = Physics.RaycastAll(origin, Vector3.down, bounds.size.y + 8f,
                                ~(1 << FootProbeSurface.LayerIndex), QueryTriggerInteraction.Ignore)
                            .Where(hit => hit.normal.y > .55f && !(hit.collider is CharacterController) &&
                                hit.collider.attachedRigidbody == null && !hit.transform.IsChildOf(city.Player.GameObject.transform))
                            .OrderBy(hit => hit.distance).ToArray();
                        if (floors.Length == 0 || !city.World.WalkableArea.Contains(floors[0].point +
                            Vector3.up * PlayerFactory.GroundedRootOffset, isolatedHeroCapsule.radius))
                        {
                            Debug.Log($"Covered passage existing ground outside focused traversal: {passage.Plan.Id}, " +
                                $"cast={origin:F3}, hits={floors.Length}, " +
                                (floors.Length > 0 ? $"floor={floors[0].point:F3}, collider={floors[0].collider.name}" : "no static floor below opening"));
                            supported = false; break;
                        }
                        route.Add(floors[0].point);
                    }
                    if (supported && lowerRoute == null && passage.Plan.Lot.Cell.y >= 0 &&
                        RouteHasClearCapsule(route)) lowerRoute = route;
                }
                if (lowerRoute != null)
                {
                    Debug.Log("Covered passage traversal: supported lower-city opening.");
                    yield return RidgeStand(lowerRoute[0]);
                    yield return RidgeWalk(lowerRoute, "covered supported lower-city route");
                }
                var fault = representative.Lights.Single(marker => marker.Fixture.IsFaulty);
                city.Player.Motor.Teleport(representative.Plan.ToWorldPoint(new Vector3(0f, 0f,
                    representative.Plan.LocalBounds.center.z)) + Vector3.up * PlayerFactory.GroundedRootOffset);
                double now = CityCoveredPassageFlickerEvaluator.AbsoluteSeconds;
                double steadyDelay = Enumerable.Range(0, 50).Select(step => step * .05d)
                    .First(delay => CityCoveredPassageFlickerEvaluator.Evaluate(now + delay, fault.Fixture.Seed) > .999f);
                GameSessionState.AdvanceGameTime((float)steadyDelay); atmosphere.RefreshImmediate(); yield return null;
                Light faultLease = Lease(fault);
                Assert.That(faultLease, Is.Not.Null);
                float fullIntensity = faultLease.intensity / fault.AppliedFactor;
                var properties = new MaterialPropertyBlock(); fault.LensRenderer.GetPropertyBlock(properties);
                Color fullLens = properties.GetColor("_BaseColor") / fault.AppliedFactor;
                if (capture) { SetPassageView(representative, 1); AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "CoveredPassages", "fault-steady", 960, 540); }
                now = CityCoveredPassageFlickerEvaluator.AbsoluteSeconds;
                double dipDelay = Enumerable.Range(0, 4000).Select(step => step * .01d)
                    .First(delay => CityCoveredPassageFlickerEvaluator.Evaluate(now + delay, fault.Fixture.Seed) < .16f);
                GameSessionState.AdvanceGameTime((float)dipDelay); atmosphere.RefreshImmediate(); yield return null;
                Assert.That(fault.PowerMultiplier, Is.LessThan(.16f));
                Assert.That(faultLease.intensity, Is.EqualTo(fullIntensity * fault.AppliedFactor).Within(.002f));
                fault.LensRenderer.GetPropertyBlock(properties);
                Assert.That(properties.GetColor("_BaseColor").maxColorComponent,
                    Is.EqualTo(fullLens.maxColorComponent * fault.AppliedFactor).Within(.002f));
                Assert.That(representative.Lights.Where(marker => !marker.Fixture.IsFaulty).All(marker => marker.PowerMultiplier == 1f), Is.True);
                if (capture) { SetPassageView(representative, 1); AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "CoveredPassages", "fault-dip", 960, 540); }
                disabledPassage = representative; representative.gameObject.SetActive(false); atmosphere.RefreshImmediate();
                Assert.That(representative.Lights.All(marker => !marker.FixtureHalo.IsVisible), Is.True);
                Assert.That(representative.Lights.All(marker => Lease(marker) == null || Lease(marker).intensity <= .001f), Is.True,
                    "Disabling the decorated world cannot leave a pooled source lighting an absent fixture.");
                representative.gameObject.SetActive(true); disabledPassage = null; atmosphere.RefreshImmediate();
                var taskAnchors = atmosphere.LampAnchors.Where(anchor => anchor != null && anchor.name.StartsWith("IndustrialTaskLight_")).ToArray();
                Transform mast = city.Night.LampAnchors.OrderByDescending(candidate =>
                    taskAnchors.Min(anchor => (anchor.position - candidate.position).sqrMagnitude)).First();
                city.Player.Motor.Teleport(mast.position - Vector3.up * 4.7f + Vector3.up * PlayerFactory.GroundedRootOffset);
                Physics.SyncTransforms(); atmosphere.RefreshImmediate();
                Assert.That(faultLease.shadows, Is.EqualTo(LightShadows.None));
                Assert.That(faultLease.intensity, Is.GreaterThan(.01f), "A retasked slot cannot retain the old contact fault.");
            }
            finally
            {
                if (disabledPassage != null) disabledPassage.gameObject.SetActive(true);
                if (homeCopyParent != null) Object.Destroy(homeCopyParent);
                isolatedHeroCapsule.enabled = capsuleEnabled;
                follow.ClearFixedPose(); OnDemandRendering.renderFrameInterval = previousInterval;
            }

            Light Lease(CityCoveredPassageLight marker) => atmosphere.StreetLightPool.FirstOrDefault(light =>
                light.isActiveAndEnabled && Vector3.Distance(light.transform.position, marker.transform.position) < .01f);
            string SlotPattern(CityCoveredPassagePlan plan) => string.Join("/", plan.WallSlots
                .OrderBy(slot => slot.Side).ThenBy(slot => slot.Index).Select(slot => $"{slot.Side}:{slot.Index}:{slot.Kind}"));
            string sideLabel(int side, int index) => side + ":" + index;
            Rect WallFootprint(Transform detail, Transform frame)
            {
                Bounds bounds = LocalRendererBounds(detail);
                Vector3 minimum = Vector3.one * float.PositiveInfinity, maximum = Vector3.one * float.NegativeInfinity;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 local = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                        (corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                    Vector3 point = frame.InverseTransformPoint(detail.TransformPoint(local));
                    minimum = Vector3.Min(minimum, point); maximum = Vector3.Max(maximum, point);
                }
                return Rect.MinMaxRect(minimum.z, minimum.y, maximum.z, maximum.y);
            }
            bool RouteHasClearCapsule(IReadOnlyList<Vector3> route)
            {
                float radius = isolatedHeroCapsule.radius;
                float half = isolatedHeroCapsule.height * .5f - radius;
                for (int i = 1; i < route.Count; i++)
                {
                    Vector3 start = route[i - 1] + Vector3.up * PlayerFactory.GroundedRootOffset;
                    Vector3 destination = route[i] + Vector3.up * PlayerFactory.GroundedRootOffset;
                    if ((city.World.WalkableArea.Constrain(start, destination, radius) - destination).sqrMagnitude > .0025f) return false;
                    Vector3 center = start + isolatedHeroCapsule.center, delta = destination - start;
                    if (Physics.CapsuleCastAll(center - Vector3.up * half, center + Vector3.up * half,
                            radius * .96f, delta.normalized, delta.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                        .Any(hit => hit.normal.y < .55f && !(hit.collider is CharacterController) &&
                            hit.collider.attachedRigidbody == null && !hit.transform.IsChildOf(city.Player.GameObject.transform))) return false;
                }
                return true;
            }
            void SetPassageView(CityCoveredPassageWorld passage, int side)
            {
                Bounds bounds = passage.Plan.LocalBounds;
                var eye = passage.transform.TransformPoint(new Vector3(0f, 1.65f,
                    side > 0 ? bounds.max.z + 4.2f : bounds.min.z - 4.2f));
                var target = passage.transform.TransformPoint(new Vector3(0f, 1.45f, bounds.center.z));
                follow.SetFixedPose(eye, Quaternion.LookRotation(target - eye), follow.FollowFieldOfView);
            }
        }
    }
}
