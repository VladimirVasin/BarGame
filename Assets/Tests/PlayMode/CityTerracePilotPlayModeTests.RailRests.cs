using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest, Timeout(240000)]
        public IEnumerator LiveCity_CourtyardAndRidgeRailsCarryWeight()
        {
            Assert.That(GameSessionState.TryStartGameTimeAt(14 * 60), Is.True);
            yield return LoadProductionCity();
            var ambient = city.AmbientPedestrians;
            var motor = city.Player.Motor;
            motor.SetInputEnabled(false);
            city.Camera.GetComponent<PlayerCameraFollow>().enabled = false;
            Vector3 rootOffset = Vector3.up * PlayerFactory.GroundedRootOffset;
            Vector3 courtyard = CityTerraceDistrictPlan.ForLayout(city.Layout).Origin;
            var garage = RidgeGaragePlan.ForLayout(city.Layout);
            foreach (bool ridge in new[] { false, true })
            {
                ambient.enabled = false;
                motor.Teleport((ridge ? garage.RoofArrival : courtyard) + rootOffset);
                string guard = ridge ? "COL_GuardFront" : "edge guard";
                CityRailRestSpot[] sites = null;
                for (int frame = 0; frame < 45; frame++)
                {
                    yield return null;
                    Physics.SyncTransforms();
                    sites = city.World.RailRests.Where(s => s.HasClearApproach() &&
                        Physics.Raycast(s.RailTop + Vector3.up * .15f, Vector3.down, out var hit, .25f,
                            CityPedestrianCollision.NonPedestrianMask, QueryTriggerInteraction.Ignore) &&
                        hit.collider.name.Contains(guard)).OrderBy(s => (s.Feet - motor.transform.position).sqrMagnitude).ToArray();
                    if (sites.Length > 0) break;
                }
                Assert.That(sites, Is.Not.Empty, guard + " needs a clear, built rail in the loaded district.");
                CityPedestrianPresentation body = null;
                CityRailRestSpot site = default;
                foreach (var candidate in sites)
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        ambient.enabled = false;
                        motor.Teleport(candidate.Feet + candidate.RailRight * (7.4f * side) + rootOffset);
                        ambient.enabled = true;
                        if (!ambient.WarmNext()) continue;
                        var first = ambient.GetWalkerBody(0);
                        if (first == null || !first.IsRestingAtRail) continue;
                        int found = System.Array.FindIndex(sites, s => (s.Feet - first.transform.position).sqrMagnitude < .001f);
                        if (found < 0) continue;
                        site = sites[found];
                        body = first;
                        break;
                    }
                    if (body != null) break;
                }
                Assert.That(body, Is.Not.Null, "Production ambient arrival at " + guard);
                Assert.That(body.GetComponentsInChildren<Collider>(), Is.Empty);
                Transform elbow = CityPedestrianHandProps.FindSocket(body.Registry.ModelRoot, "forearm.L");
                Vector3 contact = site.RailTop + Vector3.Project(elbow.position - site.RailTop, site.RailRight);
                Assert.That(Physics.Raycast(contact + Vector3.up * .13f, Vector3.down, out var beam, .22f,
                    CityPedestrianCollision.NonPedestrianMask, QueryTriggerInteraction.Ignore), Is.True);
                Assert.That(Vector3.Distance(beam.point, elbow.position), Is.LessThan(.085f), guard);
                var camera = city.Camera;
                camera.transform.position = site.Feet + site.Facing * .9f + site.RailRight * 3.4f + Vector3.up * 1.65f;
                camera.transform.LookAt(site.Feet + Vector3.up * 1.08f);
                yield return null;
                AreaCaptureFixture.CaptureCurrentCamera(camera, "DistrictRailRests", ridge ? "ridge" : "court", 960, 540);

                Vector3 lane = site.Feet - site.Facing * 1.05f + rootOffset;
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    motor.Teleport(lane - site.RailRight * (sign * 1.5f));
                    Vector3 end = lane + site.RailRight * (sign * 1.5f);
                    bool passed = false;
                    for (int tick = 0; tick < 100 && !passed; tick++)
                        passed = motor.MoveTowardsApproachWaypoint(end, .07f, FrameSeconds);
                    Assert.That(passed, Is.True, guard + " retains a two-way public route.");
                }
                motor.CancelInteractionPoseMove();
            }
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator LiveCity_RailRestsLeaveThePromenadeClear()
        {
            Assert.That(GameSessionState.TryStartGameTimeAt(14 * 60), Is.True);
            yield return LoadProductionCity();
            var ambient = city.AmbientPedestrians;
            var motor = city.Player.Motor;
            motor.SetInputEnabled(false);
            Vector3 originalStreet = motor.transform.position;
            city.Camera.GetComponent<PlayerCameraFollow>().enabled = false;
            var sites = CityRailRestSpot.AlongQuays(city.Layout);
            var graph = new CityAmbientWalkPlan(city.PedestrianPlan, city.Layout, city.BusPlans);
            var movement = new CityAmbientWalking(graph, CityPedestrianPlanner.CreateWalkableArea(city.PedestrianPlan));
            bool sawSmoke = false, sawLook = false;
            for (int visit = 0; visit < 6; visit++)
            {
                if (visit >= 2 && sawSmoke && sawLook) break;
                int bank = visit % 2 == 0 ? 1 : -1;
                ambient.enabled = false;
                var site = sites.Where(s => s.Facing.x == bank && s.HasClearApproach())
                    .OrderBy(s => Mathf.Abs(s.Feet.z - city.Layout.WorldOrigin.z)).First();
                Vector3 along = Vector3.Cross(Vector3.up, site.Facing);
                Vector3 behind = site.Feet - site.Facing * 1.1f;
                Vector3 rootOffset = Vector3.up * PlayerFactory.GroundedRootOffset;
                motor.Teleport(behind - along * 8.5f + rootOffset);
                var camera = city.Camera;
                camera.transform.SetPositionAndRotation(site.Feet + site.Facing * .98f + along * 3.45f +
                    Vector3.up * 1.7f, Quaternion.identity);
                camera.transform.LookAt(site.Feet + Vector3.up * 1.08f);
                yield return null;
                Physics.SyncTransforms();
                ambient.enabled = true;
                Assert.That(ambient.WarmNext(), Is.True);
                var body = ambient.GetWalkerBody(0);
                Assert.That(ambient.TryGetWalker(0, out _, out _, out int arrival), Is.True);
                Assert.That(body.IsRestingAtRail, Is.True, "First pose on the real ambient spawn path.");
                Assert.That(Vector3.Distance(body.transform.position, site.Feet), Is.LessThan(.005f));
                Assert.That(body.GetComponentsInChildren<Collider>(true), Is.Empty);
                var left = CityPedestrianHandProps.FindSocket(body.Registry.ModelRoot, "forearm.L");
                var right = CityPedestrianHandProps.FindSocket(body.Registry.ModelRoot, "forearm.R");
                var smoke = body.GetComponentInChildren<HomeBalconySmokingExhaleEffect>();
                bool smoking = body.IsSmokingAtRail;
                sawSmoke |= smoking;
                sawLook |= !smoking;
                Assert.That(smoke != null, Is.EqualTo(smoking));
                Quaternion initialHead = body.Registry.HeadAnchor.rotation;
                float turn = 0f, closest = 1f;
                bool photographed = false;
                var prop = body.GetComponentInChildren<CityPedestrianHandPropRegistry>();
                var mouth = CityPedestrianHandProps.FindSocket(body.Registry.ModelRoot, CityPedestrianHandProps.MouthSocketName);
                Renderer paper = smoking ? prop.FindRenderer("ACC_Cigarette") : null;
                // Let Unity skin the new pose for the gameplay camera before reading its pixels.
                yield return null;
                AreaCaptureFixture.CaptureCurrentCamera(camera, "QuayRailRests",
                    smoking ? "smoker-arrival" : "observer-arrival", 960, 540);
                CheckRail(left);
                if (!smoking) CheckRail(right);
                for (int tick = 0; tick < 235; tick++)
                {
                    body.Advance(.1f, false);
                    turn = Mathf.Max(turn, Quaternion.Angle(initialHead, body.Registry.HeadAnchor.rotation));
                    Assert.That(body.transform.position, Is.EqualTo(site.Feet));
                    CheckRail(left);
                    Vector3 shoulder = left.parent.position;
                    Vector3 ankles = (body.Registry.LeftFoot.position + body.Registry.RightFoot.position) * .5f;
                    Assert.That(Vector3.Dot(shoulder - ankles, site.Facing), Is.GreaterThan(.25f),
                        "Shoulders must carry weight over the rail while the boots stay back.");
                    if (!smoking) CheckRail(right);
                    float gap = smoking ? Vector3.Distance(paper.bounds.center, mouth.position) : 0f;
                    closest = Mathf.Min(closest, gap);
                    if (!photographed && tick > 3 && gap < .066f)
                    {
                        yield return null;
                        AreaCaptureFixture.CaptureCurrentCamera(camera, "QuayRailRests",
                            smoking ? "smoking" : "looking", 960, 540);
                        photographed = true;
                    }
                }
                Assert.That(turn, Is.GreaterThan(smoking ? 3f : 8f), "Head moves at the pace of the activity.");
                if (smoking) Assert.That(Vector3.Dot(mouth.up, Vector3.down), Is.GreaterThan(.20f),
                    "The smoker looks down towards the water.");
                Assert.That(photographed, Is.True, $"Cigarette centre to mouth {closest:F3} m.");
                if (smoking) Assert.That(smoke.ManualBurstCount, Is.GreaterThan(0));

                // An opposing background pedestrian takes the existing through
                // lane while the production hero uses the inside of the walk.
                var entries = new System.Collections.Generic.List<CityAmbientWalkPlan.Entry>();
                graph.CollectEntries(site.Feet, 12f, entries);
                var entry = entries.Where(e => graph.LinkId(e.Link).StartsWith("river-promenade:") &&
                    Mathf.Abs(e.Position.x - site.Feet.x) < 5f)
                    .OrderBy(e => (e.Position - site.Feet).sqrMagnitude).First();
                graph.Sample(entry.Link, entry.Distance, out Vector3 direction);
                var contacts = new CityAmbientWalking.Contact[2];
                contacts[0] = new CityAmbientWalking.Contact(-100, site.Feet, CityAmbientWalking.Radius);
                float oppositeRest = entry.Distance + Vector3.Dot(site.Feet - entry.Position, direction);
                const float passMargin = 1.65f;
                for (int pass = -1; pass <= 1; pass += 2)
                {
                    var walk = new CityAmbientWalking.Route(entry.Link, pass, oppositeRest - pass * passMargin);
                    Vector3 peer = graph.Sample(entry.Link, walk.Along, out _);
                    Assert.That(Vector3.Dot(peer - site.Feet, direction) * pass, Is.LessThan(-1.5f));
                    var middle = behind;
                    Vector3 end = middle - direction * (pass * 2.1f) + rootOffset;
                    motor.Teleport(middle + direction * (pass * 2.1f) + rootOffset);
                    bool arrived = false;
                    for (int step = 0; step < 125; step++)
                    {
                        contacts[1] = new CityAmbientWalking.Contact(-1, motor.transform.position, .32f);
                        movement.Advance(ref walk, 1, peer, .82f, FrameSeconds, contacts, contacts.Length, out peer, out _);
                        if (!arrived) arrived = motor.MoveTowardsApproachWaypoint(end, .07f, FrameSeconds);
                        if (arrived && Vector3.Dot(peer - site.Feet, direction) * pass > passMargin) break;
                    }
                    Assert.That(arrived, Is.True, "Hero can pass the occupied rail in either direction.");
                    Assert.That(Vector3.Dot(peer - site.Feet, direction) * pass, Is.GreaterThan(passMargin),
                        $"Peer did not clear the far shoulder at {site.Feet:F2}, peer={peer:F2}, hero={motor.transform.position:F2}, blocker={walk.Blocker}.");
                }
                motor.CancelInteractionPoseMove();
                motor.Teleport(originalStreet);
                Assert.That(Vector3.Distance(motor.transform.position, site.Feet), Is.GreaterThan(15f));
                ambient.Advance(32f);
                Assert.That(body.IsRestingAtRail && body.gameObject.activeSelf, Is.True, "Still visible past turnover age.");
                camera.transform.SetPositionAndRotation(site.Feet + Vector3.right * 70f + Vector3.up * 2f,
                    Quaternion.LookRotation(Vector3.right));
                yield return null;
                Assert.That(city.Visibility.CanChangeAmbientBodyAt(site.Feet), Is.True, "The whole body left the shot.");
                ambient.Advance(1f);
                Assert.That(Enumerable.Range(0, CityAmbientPedestrians.MaximumActive).Any(slot =>
                    ambient.TryGetWalker(slot, out _, out _, out int id) && id == arrival), Is.False,
                    "The original encounter ended after leaving the shot.");
                Assert.That(body.IsRestingAtRail, Is.False, "The city-entry street releases the quay pose.");
                Assert.That(body.Registry.Animator.enabled, Is.True, "Walking graph resumes at pool handoff.");
                Assert.That(body.GetComponentsInChildren<CityPedestrianHandPropRegistry>(), Is.Empty);
                if (smoke != null) Assert.That(smoke.Particles.particleCount, Is.Zero);
                if (visit < 2) yield return CheckStature(site, bank > 0);

                void CheckRail(Transform elbow)
                {
                    float gap = Vector3.ProjectOnPlane(elbow.position - site.RailTop, site.RailRight).magnitude;
                    Assert.That(gap, Is.LessThan(.085f), $"{elbow.name} must rest on the beam, gap {gap:F3}.");
                }
            }
            Assert.That(sawLook && sawSmoke, Is.True);

            IEnumerator CheckStature(CityRailRestSpot site, bool shortest)
            {
                var looks = Enumerable.Range(0, DefaultNpcPopulation.AmbientAppearancesPerGender)
                    .Select(i => DefaultNpcPopulation.GetAssignment(DefaultNpcPopulation.AmbientPedestrianId(i, shortest)))
                    .OrderBy(a => a.HeightCm);
                var look = shortest ? looks.First() : looks.Last();
                var model = CityPedestrianFactory.CreateAmbientPresentation(city.transform, look.CharacterId);
                try
                {
                    model.transform.SetPositionAndRotation(site.Feet, Quaternion.LookRotation(site.Facing));
                    model.gameObject.SetActive(true);
                    model.ConfigureCycle(1f, .1f);
                    model.RestAtRail(site, false, .1f);
                    var eye = site.Feet - site.Facing * 1.35f + site.RailRight * 2.9f + Vector3.up * 1.55f;
                    city.Camera.transform.SetPositionAndRotation(eye,
                        Quaternion.LookRotation(site.Feet + Vector3.up - eye));
                    yield return null;
                    AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "QuayRailRests",
                        shortest ? "shortest" : "tallest", 960, 540);
                    foreach (string side in new[] { "L", "R" })
                    {
                        var elbow = CityPedestrianHandProps.FindSocket(model.Registry.ModelRoot, "forearm." + side);
                        float gap = Vector3.ProjectOnPlane(elbow.position - site.RailTop, site.RailRight).magnitude;
                        Assert.That(gap, Is.LessThan(.085f), $"{look.HeightCm} cm, elbow {side} gap {gap:F3}.");
                        var sole = model.Registry.Renderers.OfType<SkinnedMeshRenderer>().Single(
                            r => r.name.EndsWith("Sole." + side, System.StringComparison.Ordinal));
                        Vector3 bottom = CityPedestrianShoeContacts.SkinWorldVertices(sole).OrderBy(v => v.y).First();
                        Assert.That(Physics.Raycast(bottom + Vector3.up * .24f, Vector3.down, out var hit,
                            .45f, CityPedestrianCollision.NonPedestrianMask, QueryTriggerInteraction.Ignore), Is.True);
                        Assert.That(bottom.y - hit.point.y, Is.InRange(-.035f, .07f),
                            $"{look.HeightCm} cm, sole {side} at {bottom:F2} over {hit.collider.name}.");
                    }
                    model.gameObject.SetActive(false);
                    model.gameObject.SetActive(true);
                    model.ConfigureCycle(1f, .25f);
                    model.Advance(.25f, true, true);
                    Assert.That(model.IsMoving && !model.IsRestingAtRail && model.Registry.Animator.enabled, Is.True);
                }
                finally { Object.Destroy(model.gameObject); }
            }
        }
    }
}
