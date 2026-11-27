using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest, Timeout(300000)]
        [Explicit("Walk from the summit bridge through the surviving flats on all three floors and back.")]
        public IEnumerator SummitRavineRoomsAndShoring()
        {
            GameSessionState.BeginNewGame("Summit ravine inspection");
            GameSessionState.TryStartGameTimeAt(12 * 60);
            yield return SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
            var city = Object.FindAnyObjectByType<CityGameRoot>();
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!city.IsInitialized && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(city.IsInitialized, Is.True);
            var site = RidgeRavinePlan.ForLayout(city.Layout);
            var map = new CityMapCityTeleportGround(city.Layout);
            var motor = city.Player.Motor;
            var follow = city.Camera.GetComponent<PlayerCameraFollow>();
            var quarter = CityHillsideQuarterPlan.ForLayout(city.Layout);
            var bridge = quarter.Bridges[1];
            var link = bridge.Link;
            Vector3 Room(float x, float z) => site.Origin + new Vector3(x, 0, z);
            Vector3 Street(float x) => new Vector3(x, link.PavementTopAt(x), link.WestGateWorld.z - 4f);
            var route = new[] { Street(bridge.WestBearingWorld.x - 1.1f), Street(6.71f), Room(4.71f, 3.5f),
                Room(4.71f, 2.10f), Room(.24f, 2.1f), Room(.24f, -2f), Room(3.20f, -2f),
                Room(3.20f, 2.1f), Room(4.71f, 3.5f), Street(6.71f) };
            var input = motor.InputEnabled;
            float previousDelta = Time.captureDeltaTime;
            var followEnabled = follow.enabled;
            var hidden = new List<Renderer>();
            try
            {
                foreach (var renderer in city.Player.GameObject.GetComponentsInChildren<Renderer>(true))
                    if (renderer.enabled) { hidden.Add(renderer); renderer.enabled = false; }
                follow.enabled = false;
                motor.SetInputEnabled(false);
                var fabric = GameObject.Find("Summit ravine repairs and fallen house").transform;
                var shell = fabric.Find("RavineHouse");
                var fallen = fabric.Find("RavineFallenFloor").position;
                var panel = fabric.Find("RavineKitchenFragment").position;
                Assert.That(fallen.y, Is.LessThan(site.Origin.y - 7f));
                Assert.That(panel.y, Is.InRange(fallen.y, site.Origin.y - 1f));
                Assert.That(map.TryResolveStandingPosition(new Vector2(fallen.x, fallen.z), out _), Is.False);
                foreach (var point in new[] { route[4], route[5] })
                {
                    Assert.That(map.TryResolveStandingPosition(new Vector2(point.x,point.z), out var landing), Is.True);
                    Assert.That(landing.y, Is.EqualTo(site.Arrival.y).Within(.03f));
                }
                Assert.That(map.TryResolveStandingPosition(new Vector2(site.Origin.x+2.50f,site.Origin.z), out _), Is.False);
                Assert.That(CityMapArtResources.TryGet(city.Map, out _, out _), Is.True, "Registered summit survey");
                Vector3 eye = Vector3.up * 1.65f;
                void Frame(string id, Vector3 camera, Vector3 target)
                {
                    SetWestInfillCamera(city, camera, target);
                    CaptureCurrentCamera(city.Camera, "RavineHouse", id, 1280, 720);
                }
                Frame("bridge", Street(-13.5f) + eye, Room(-1, .5f) + Vector3.up * 2.6f);
                Frame("west-repairs", new Vector3(-.9f,site.Origin.y+1.65f,-355.8f), new Vector3(-15,site.Origin.y-1.0f,-354.5f));
                Frame("kitchen", Room(1.63f,3.24f) + eye, Room(-1.41f,.36f) + Vector3.up * .98f);
                Frame("room", Room(.17f,-1.08f) + eye, Room(.95f,-4.30f) + Vector3.up * .87f);
                Frame("fallen-floor", Room(-3.55f,2.72f) + eye, fallen);
                var tub = fabric.Find("RavineEnamelBath");
                var cabinet = fabric.Find("RavineKitchenCabinet");
                var door = fabric.Find("RavineApartmentDoor");
                Assert.That(tub && cabinet && door, Is.True, "Apartment fittings below the rooms");
                // Check the furnishings from valid standing roots. Looking straight at
                // a prop through the rock, or from a camera over the void, cannot pass.
                var fittings = new[] { tub, cabinet, door };
                var targets = new[] { Part(tub, "ChippedEnamel"), Part(cabinet, "CabinetShelves"), Part(door, "BrokenDoorLeaf") };
                var closest = new[] { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };
                var views = new Vector3[3];
                int visibleFromRooms = 0;
                for (float z = site.Bounds.yMin+.8f; z < site.Bounds.yMax-.45f; z += .6f)
                {
                    Vector4 section = CityHillsideQuarterRavine.At(z);
                    for (float inset = .45f; inset < 2.4f; inset += .36f)
                    {
                        Probe(new Vector2(section.z + inset,z), true);
                        Probe(new Vector2(section.y - inset,z), false);
                    }
                }
                for (float x = bridge.WestBearingWorld.x; x < bridge.EastBearingWorld.x + 1f; x += .75f)
                    Probe(new Vector2(x,link.Bounds.yMin+.55f), false);
                string[] shots = { "fallen-bath", "fallen-cupboard", "fallen-door" };
                for (int i = 0; i < fittings.Length; i++)
                {
                    Assert.That(closest[i], Is.LessThan(19f), fittings[i].name + " must be visible from a public approach");
                    Frame(shots[i], views[i], targets[i]);
                }
                Assert.That(visibleFromRooms, Is.Not.Zero, "A player inside the ruins sees domestic fittings below");

                void Probe(Vector2 xz, bool inside)
                {
                    if ((inside && !site.ContainsFloor(xz)) || !city.World.WalkableArea.Contains(
                        new Vector3(xz.x,site.Origin.y,xz.y), CityGroundTraversalPlanner.MaximumAgentRadius)) return;
                    if (!map.TryResolveStandingPosition(xz, out var standing) ||
                        (new Vector2(standing.x,standing.z)-xz).sqrMagnitude > .025f) return;
                    Vector3 sight = standing + Vector3.up * (1.65f - PlayerFactory.GroundedRootOffset);
                    for (int i = 0; i < fittings.Length; i++)
                    {
                        Vector3 direction = targets[i] - sight;
                        if (!Physics.Raycast(sight, direction.normalized, out var hit, direction.magnitude+.35f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ||
                            !hit.transform.IsChildOf(fittings[i])) continue;
                        if (inside) visibleFromRooms |= 1 << i;
                        if (direction.magnitude >= closest[i]) continue;
                        closest[i] = direction.magnitude;
                        views[i] = sight;
                    }
                }
                Vector3 Part(Transform fixture, string name)
                {
                    foreach (var renderer in fixture.GetComponentsInChildren<Renderer>())
                        if (renderer.name.StartsWith(name + "__", System.StringComparison.Ordinal))
                            return renderer.bounds.center;
                    throw new AssertionException("Missing " + name);
                }
                Frame("upper-flats", Street(-8.5f) + eye, Room(.35f,.32f) + Vector3.up * 4.37f);
                Vector3 At(float x, float z, float floor) => Room(x,z) + Vector3.up*(floor*site.Storey);
                Frame("third-floor-room", At(.28f,-1.96f,2)+eye, At(.91f,-4.18f,2)+Vector3.up);
                Frame("stair-entry", At(4.55f,3.56f,0)+eye, At(4.7f,-1.65f,.54f));
                Frame("stair-turn", At(5.98f,-2.46f,1.5f)+eye, At(5.68f,1.68f,2.18f));
                // WalkParkLeg advances a .05 s gait step each frame. Gravity
                // must receive the same interval when descending the flights.
                Time.captureDeltaTime = .05f;
                motor.Teleport(route[0] + Vector3.up * PlayerFactory.GroundedRootOffset);
                Physics.SyncTransforms();
                for (int j=0; j<3; j++) yield return null;
                yield return WalkParkLeg(city, Street(-12f), "West half of bridge");
                yield return WalkParkLeg(city, Street(0f), "East half of bridge");
                for (int i=1; i<route.Length; i++)
                {
                    yield return WalkParkLeg(city, route[i], "Ravine walk " + i);
                    if (i < 2 || i == route.Length-1) continue;
                    Assert.That(Physics.Raycast(route[i]+Vector3.up*.3f, Vector3.down, out var hit,
                        .45f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && hit.transform.IsChildOf(shell),
                        Is.True, "Physical room floor at " + route[i]);
                    if (i == 2) yield return WalkStairs();
                }

                IEnumerator WalkStairs()
                {
                    var waypoints = new List<Vector3> { At(4.56f,2.14f,0) };
                    for (int floor = 1; floor <= 2; floor++)
                    {
                        waypoints.Add(At(4.56f,-2.21f,floor-.5f));
                        waypoints.Add(At(5.92f,-2.21f,floor-.5f));
                        waypoints.Add(At(5.92f,2.14f,floor));
                        Vector3 flat = At(.28f,2.14f,floor);
                        waypoints.Add(flat);
                        waypoints.Add(At(.28f,-1.96f,floor));
                        if (floor < 2)
                        {
                            waypoints.Add(flat);
                            waypoints.Add(At(4.56f,2.14f,floor));
                        }
                        Vector3 root = flat + Vector3.up*PlayerFactory.GroundedRootOffset;
                        Assert.That(map.TryClampArrival(root, out var landing), Is.True);
                        Assert.That(landing.y, Is.EqualTo(root.y).Within(.02f));
                        Assert.That(map.TryClampArrival(At(4.56f,0,floor), out _), Is.False, "Open flight");
                        foreach (float x in new[] { 4.56f, 5.92f })
                        {
                            float height = floor - (x < 5f ? .75f : .25f);
                            Vector3 foot = At(x,-.12f,height);
                            Assert.That(Physics.Raycast(foot+Vector3.up*.20f, Vector3.down, out var tread,
                                .32f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                                tread.collider.name.StartsWith("COL_StairFlights"), Is.True, "Flight support");
                            Assert.That(Physics.Raycast(foot+Vector3.up*.08f, Vector3.up, 1.95f,
                                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.False, "Flight headroom");
                        }
                    }
                    foreach (var point in waypoints) yield return WalkParkLeg(city, point, "Up through the stair hall");
                    for (int i=waypoints.Count-2; i>=0; i--)
                        yield return WalkParkLeg(city, waypoints[i], "Down through the stair hall");
                    yield return WalkParkLeg(city, route[2], "Back at street entrance");
                }
            }
            finally
            {
                Time.captureDeltaTime = previousDelta;
                foreach (var renderer in hidden) if (renderer) renderer.enabled = true;
                motor.CancelInteractionPoseMove();
                motor.SetInputEnabled(input);
                follow.enabled = followEnabled;
                GameSessionState.BeginNewGame("Summit ravine inspection complete");
            }
        }
    }
}
