using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Review both child routes through the two-deck playground.")]
        [Timeout(240000)]
        public IEnumerator CityBridgePlaygroundChildVisit()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.PrepareHomeReturn();
            Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
            AsyncOperation load = SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
            CityGameRoot city = null;
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds * 2f;
            while (Time.realtimeSinceStartup < deadline)
            {
                city = Object.FindAnyObjectByType<CityGameRoot>();
                if (load.isDone && city != null && city.IsInitialized && !CompositionDriver.IsComposing) break;
                yield return null;
            }
            Assert.That(city != null && city.IsInitialized && !CompositionDriver.IsComposing, Is.True);
            CityCourtyardPlayground site = city.World.BridgePlayground;
            Assert.That(site, Is.Not.Null, "Default City needs a two-deck playground.");
            Assert.That(city.World.CourtyardPlayground, Is.Not.Null);
            Assert.That(city.World.SlidePlayground, Is.Not.Null);
            Debug.Log($"Two-deck playground at {site.Site.Cell}, center {site.transform.position:F2}, " +
                $"join {site.Site.RouteJoin:F2}, facing {site.transform.forward:F2}. Small and slide are at " +
                $"{city.World.CourtyardPlayground.Site.Cell} and {city.World.SlidePlayground.Site.Cell}.");
            Assert.That(site.Site.Cell, Is.Not.EqualTo(city.World.CourtyardPlayground.Site.Cell));
            Assert.That(site.Site.Cell, Is.Not.EqualTo(city.World.SlidePlayground.Site.Cell));
            Assert.That(site.First.Exit, Is.SameAs(site.Alternate.Exit));
            Assert.That(city.PedestrianPlan.Links, Has.Some.Matches<CityPedestrianLink>(
                link => link.Id.StartsWith(site.Site.RoutePrefix, StringComparison.Ordinal)));

            var grass = city.World.Root.GetComponentInChildren<CityGrassField>(true);
            Assert.That(grass, Is.Not.Null);
            AssertPlaygroundGroundClear(city, grass, CityRoadPolygon.Rectangle(site.Site.Footprint));

            Physics.SyncTransforms();
            CityPlaygroundVisit access = default;
            if (!access.TryStart(Array.Empty<CityPlaygroundSwing>(), 100000, site.Site.RouteJoin, bridge: site))
                Assert.Fail($"Blocked approach in {site.Site.Cell}. Join to side: " +
                    $"{SlideApproachBlocker(site.Site.RouteJoin, site.SideEntry)}. Side to entry: " +
                    $"{SlideApproachBlocker(site.SideEntry, site.First.Entry.position)}.");
            access.Cancel(null, 100000);

            Camera camera = city.Camera;
            var follow = camera.GetComponent<PlayerCameraFollow>();
            if (follow != null) follow.enabled = false;
            city.Player.Motor.SetInputEnabled(false);
            city.Player.Motor.Teleport(site.Site.RouteJoin + site.transform.right * 2f);
            Vector3 center = site.transform.position;
            Vector3 forward = site.transform.forward, right = site.transform.right;
            camera.transform.position = center + right * 4.9f - forward * 2.5f + Vector3.up * 2f;
            camera.transform.rotation = Quaternion.LookRotation(center + right * .70f + Vector3.up * .8f -
                camera.transform.position);

            var obstacle = new GameObject("Shared slide runout test obstacle");
            var blocker = obstacle.AddComponent<BoxCollider>();
            obstacle.transform.position = site.First.Exit.position + Vector3.up * .5f;
            blocker.size = new Vector3(.42f, 1f, .42f);
            float previousDelta = Time.captureDeltaTime;
            bool cameraEnabled = camera.enabled;
            try
            {
                // Render the three inspection frames on demand. The City's ordinary
                // render loop would tie the number of simulated visits to GPU speed.
                camera.enabled = false;
                Time.captureDeltaTime = .10f;
                Physics.SyncTransforms();
                CityAmbientPedestrians ambient = city.AmbientPedestrians;
                CityAmbientWalkPlan.Entry entry = PlaygroundRouteEntry(city, site.Site);
                const BindingFlags walkerMethods = BindingFlags.Instance | BindingFlags.NonPublic;
                MethodInfo spawn = typeof(CityAmbientPedestrians).GetMethod("Spawn", walkerMethods);
                MethodInfo release = typeof(CityAmbientPedestrians).GetMethod("Release", walkerMethods);
                Assert.That(spawn, Is.Not.Null);
                Assert.That(release, Is.Not.Null);
                for (int visit = 0; visit < 2; visit++)
                {
                    bool viaBridge = visit == 0;
                    CityCourtyardPlayground.Crossing route = viaBridge ? site.First : site.Alternate;
                    int slot = 0;
                    while (slot < CityAmbientPedestrians.MaximumActive && ambient.TryGetWalker(slot, out _, out _, out _))
                        slot++;
                    Assert.That(spawn.Invoke(ambient, new object[] { entry, 1, null, true }), Is.True);
                    Assert.That(ambient.TryGetWalker(slot, out Vector3 departure, out _, out int encounter), Is.True);
                    CityPedestrianPresentation child = ambient.GetWalkerBody(slot);
                    Transform leftGrip = null;
                    foreach (Transform bone in child.Registry.Animator.GetComponentsInChildren<Transform>())
                        if (bone.name == "SOCKET_Grip.L") leftGrip = bone;
                    Assert.That(leftGrip, Is.Not.Null);
                    float actionStart = float.NaN;
                    bool climbed = false, crossed = false, descended = false, landed = false, returned = false;
                    bool waited = false, reserved = false;
                    CityPlaygroundVisit.Phase phase = CityPlaygroundVisit.Phase.None;
                    deadline = Time.realtimeSinceStartup + 75f;
                    while (Time.realtimeSinceStartup < deadline && !returned)
                    {
                        Assert.That(ambient.GetWalkerBody(slot), Is.SameAs(child));
                        phase = ambient.GetPlaygroundPhase(slot);
                        if (!reserved && site.Visitor == encounter)
                        {
                            Assert.That(site.TryReserve(encounter + 999), Is.False);
                            reserved = true;
                        }
                        if (reserved)
                        {
                            if (phase == CityPlaygroundVisit.Phase.Slide)
                            {
                                if (float.IsNaN(actionStart)) actionStart = Time.time;
                                ambient.Advance(.0001f);
                                Assert.That(child.AuthoredActionClip.name, Is.EqualTo(CityFairChildActions.ClipName(route.Action)));
                                Vector3 pelvis = child.Registry.PelvisAnchor.position;
                                float along = Vector3.Dot(pelvis - route.Entry.position, forward);
                                float across = Vector3.Dot(pelvis - site.First.Entry.position, right);
                                if (!climbed && SegmentDistance(leftGrip.position, route.Contacts[4].position - right * .29f,
                                        route.Contacts[5].position - right * .29f) < .04f &&
                                    OnSlideTread(child.Registry, route.Contacts, forward))
                                {
                                    if (!viaBridge)
                                        CaptureCurrentCamera(camera, nameof(CityBridgePlaygroundChildVisit), "02-frame", 960, 720);
                                    climbed = true;
                                }
                                if (viaBridge && !crossed && across > .70f && across < 1.03f)
                                {
                                    Assert.That(along, Is.EqualTo(1.68f).Within(.07f));
                                    Assert.That(pelvis.y - route.Contacts[6].position.y, Is.InRange(.54f, .66f));
                                    float bootHeight = Mathf.Min(child.Registry.LeftFoot.position.y, child.Registry.RightFoot.position.y);
                                    Assert.That(bootHeight - route.Contacts[6].position.y, Is.EqualTo(.065f).Within(.023f));
                                    CaptureCurrentCamera(camera, nameof(CityBridgePlaygroundChildVisit), "01-bridge", 960, 720);
                                    crossed = true;
                                }
                                if (viaBridge && !waited && Time.time - actionStart > route.DescentTime + .8f)
                                {
                                    Assert.That(along, Is.EqualTo(1.97f).Within(.07f));
                                    blocker.enabled = false;
                                    Physics.SyncTransforms();
                                    waited = true;
                                }
                                if (!descended && along > 2.66f && along < 3.26f)
                                {
                                    Assert.That(across, Is.EqualTo(CityCourtyardPlaygroundSite.BridgeLane.x).Within(.05f));
                                    float height = SlideHeightAt(along, route, forward);
                                    Assert.That(pelvis.y - height, Is.EqualTo(.118f).Within(.035f));
                                    if (viaBridge)
                                        CaptureCurrentCamera(camera, nameof(CityBridgePlaygroundChildVisit), "03-slide", 960, 720);
                                    descended = true;
                                }
                            }
                            else if (phase == CityPlaygroundVisit.Phase.SlideExitSide && !landed)
                            {
                                Assert.That(child.AuthoredActionWeight, Is.Zero);
                                Assert.That(Vector3.Distance(child.transform.position, route.Exit.position), Is.LessThan(.15f));
                                landed = true;
                            }
                            else if (phase == CityPlaygroundVisit.Phase.None)
                            {
                                Assert.That(Vector3.Distance(child.transform.position, departure), Is.LessThan(.7f));
                                Assert.That(site.Visitor, Is.Not.EqualTo(encounter));
                                returned = true;
                            }
                        }
                        yield return null;
                    }
                    Assert.That(climbed && descended && landed && returned && (!viaBridge || crossed && waited), Is.True,
                        $"cell={site.Site.Cell}, action={route.Action}, visitor={site.Visitor}, phase={phase}, " +
                        $"climb={climbed}, bridge={crossed}, wait={waited}, slide={descended}, landed={landed}, " +
                        $"returned={returned}, departure={departure:F2}, feet={child.transform.position:F2}");
                    release.Invoke(ambient, new object[] { slot });
                }
            }
            finally
            {
                Object.Destroy(obstacle);
                Time.captureDeltaTime = previousDelta;
                camera.enabled = cameraEnabled;
                city.Player.Motor.SetInputEnabled(true);
                if (follow != null) follow.enabled = true;
            }
        }

        private static CityAmbientWalkPlan.Entry PlaygroundRouteEntry(CityGameRoot city, CityCourtyardPlaygroundSite site)
        {
            var plan = new CityAmbientWalkPlan(city.PedestrianPlan, playgroundRoutePrefix: site.RoutePrefix);
            CityAmbientWalkPlan.Entry entry = default;
            float best = float.PositiveInfinity;
            for (int link = 0; link < city.PedestrianPlan.Links.Count; link++)
            {
                if (!plan.IsPlaygroundLink(link)) continue;
                float distance = plan.ProjectDistance(link, site.RouteJoin);
                Vector3 point = plan.Sample(link, distance, out _);
                float difference = (point - site.RouteJoin).sqrMagnitude;
                if (difference >= best) continue;
                best = difference;
                entry = new CityAmbientWalkPlan.Entry(link, distance, point);
            }
            Assert.That(best, Is.LessThan(.5f));
            return entry;
        }
    }
}
