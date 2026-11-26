using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Review the two-child seesaw and the street cast at sunset.")]
        [Timeout(240000)]
        public IEnumerator CitySeesawDaylight()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.PrepareHomeReturn();
            Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
            AsyncOperation load = SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds * 2f;
            CityGameRoot city = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                city = Object.FindAnyObjectByType<CityGameRoot>();
                if (load.isDone && city != null && city.IsInitialized && !CompositionDriver.IsComposing) break;
                yield return null;
            }

            Assert.That(city != null && city.IsInitialized && !CompositionDriver.IsComposing);
            CityPlaygroundSeesaw seesaw = city.World.Seesaw;
            Assert.That(seesaw, Is.Not.Null, "Default City has room for the seesaw.");
            var grass = city.World.Root.GetComponentInChildren<CityGrassField>(true);
            Assert.That(grass, Is.Not.Null);
            CityPlaygroundLandscapePlan landscape = CityPlaygroundLandscapePlan.Create(
                city.Layout, city.World.ResidentialCourtyards, city.World.DecorationPlan.Descriptors);
            Assert.That(landscape.Sites, Has.Count.EqualTo(5));
            Assert.That(city.World.BalanceStations, Has.Count.EqualTo(4));

            CityAmbientPedestrians ambient = city.AmbientPedestrians;
            CityAmbientWalkPlan.Entry entry = PlaygroundRouteEntry(city, seesaw.Site);
            const BindingFlags walkerMethods = BindingFlags.Instance | BindingFlags.NonPublic;
            var spawn = typeof(CityAmbientPedestrians).GetMethod("Spawn", walkerMethods);
            var release = typeof(CityAmbientPedestrians).GetMethod("Release", walkerMethods);
            var countdown = typeof(CityAmbientPedestrians).GetField("spawnCountdown", walkerMethods);
            Assert.That(spawn, Is.Not.Null);
            Assert.That(release, Is.Not.Null);
            Assert.That(countdown, Is.Not.Null);

            Camera camera = city.Camera;
            var follow = camera.GetComponent<PlayerCameraFollow>();
            var heroCollider = city.Player.GameObject.GetComponent<CharacterController>();
            bool heroColliderEnabled = heroCollider != null && heroCollider.enabled;
            bool cameraEnabled = camera.enabled;
            bool ambientEnabled = ambient.enabled;
            float captureDelta = Time.captureDeltaTime;
            city.Player.Motor.SetInputEnabled(false);
            city.Player.Motor.Teleport(seesaw.Site.RouteJoin + seesaw.transform.right * 2.5f);
            if (follow != null) follow.enabled = false;
            Vector3 center = seesaw.transform.position;
            camera.transform.position = center - seesaw.transform.right * 3.9f - seesaw.transform.forward * 2.8f + Vector3.up * 1.65f;
            camera.transform.rotation = Quaternion.LookRotation(center + Vector3.up * .55f - camera.transform.position);
            try
            {
                camera.enabled = false;
                if (heroCollider != null) heroCollider.enabled = false;
                ambient.enabled = false;
                foreach (CityPlaygroundBalanceStation balance in city.World.BalanceStations) balance.enabled = false;
                countdown.SetValue(ambient, 200f);
                Time.captureDeltaTime = .10f;
                Physics.SyncTransforms();
                int landscapeIndex = 0;
                foreach (CityPlaygroundLandscapePlan.Site land in landscape.Sites)
                {
                    AssertPlaygroundGroundClear(city, grass, land.Pad);
                    Assert.That(land.Connector, Has.Length.EqualTo(4), "The site connects to an existing path.");
                    AssertPlaygroundGroundClear(city, grass, land.Connector);
                    if (land.Court == null || land.Court.HasSlide || land.Court.HasSeesaw)
                    {
                        Assert.That(land.Balance, Is.Not.Null, "A second activity belongs to the same plaza.");
                        AssertPlaygroundGroundClear(city, grass, land.BalancePad);
                        AssertPlaygroundGroundClear(city, grass, land.BalanceConnector);
                    }
                    AssertPlaygroundSurface(land);
                    PositionPlaygroundCamera(city, camera, land);
                    yield return null;
                    string label = land.IsPark ? "park" : land.Court.HasSeesaw ? "seesaw" :
                        land.Court.HasBridge ? "bridge" : land.Court.HasSlide ? "slide" : "bars";
                    CaptureCurrentCamera(camera, nameof(CitySeesawDaylight), $"ground-{++landscapeIndex}-{label}", 960, 720);
                }
                city.Player.Motor.Teleport(seesaw.Site.RouteJoin + seesaw.transform.right * 2.5f);
                camera.transform.position = center - seesaw.transform.right * 3.9f - seesaw.transform.forward * 2.8f + Vector3.up * 1.65f;
                camera.transform.rotation = Quaternion.LookRotation(center + Vector3.up * .55f - camera.transform.position);
                Physics.SyncTransforms();

                CityPlaygroundVisit access = default;
                Assert.That(access.TryStart(System.Array.Empty<CityPlaygroundSwing>(), 100000, entry.Position, pair: seesaw),
                    Is.True, $"Blocked seesaw approach at {seesaw.Site.Cell}; join {seesaw.Site.RouteJoin:F2}.");
                access.Cancel(null, 100000);

                Assert.That(SpawnChild(), Is.True);
                yield return ReachWait(0);
                for (int i = 0; i < 10; i++)
                {
                    ambient.Advance(.1f);
                    yield return null;
                    Assert.That(seesaw.Playing, Is.False);
                }

                Assert.That(access.TryStart(System.Array.Empty<CityPlaygroundSwing>(), 100001, entry.Position, pair: seesaw),
                    Is.True, "The second place has a clear path. " + DescribeSecondApproach());
                access.Cancel(null, 100001);
                Assert.That(SpawnChild(), Is.True);
                CityPedestrianPresentation first = ambient.GetWalkerBody(0), second = ambient.GetWalkerBody(1);
                bool firstReturned = false, secondReturned = false;
                bool rose = false, fell = false;
                bool captured = false, played = false;
                deadline = Time.realtimeSinceStartup + 50f;
                while (Time.realtimeSinceStartup < deadline && (!firstReturned || !secondReturned))
                {
                    ambient.Advance(.1f);
                    if (seesaw.Seconds > CityPlaygroundSeesaw.MountSeconds && seesaw.Seconds < CityPlaygroundSeesaw.DismountStart)
                    {
                        Assert.That(ambient.GetPlaygroundPhase(0), Is.EqualTo(CityPlaygroundVisit.Phase.SeesawRide));
                        Assert.That(ambient.GetPlaygroundPhase(1), Is.EqualTo(CityPlaygroundVisit.Phase.SeesawRide));
                        AssertSeesawContact(seesaw, first, "A");
                        AssertSeesawContact(seesaw, second, "B");
                        float high = FindPart(seesaw.Board, "SeesawSeatA").position.y -
                            FindPart(seesaw.Board, "SeesawSeatB").position.y;
                        rose |= high > .12f;
                        fell |= high < -.12f;
                        if (!captured && Mathf.Abs(high) > .17f)
                        {
                            CaptureCurrentCamera(camera, nameof(CitySeesawDaylight), "01-pair", 960, 720);
                            captured = true;
                        }
                        played = true;
                    }

                    if (played)
                    {
                        CheckReturn(0, first, ref firstReturned);
                        CheckReturn(1, second, ref secondReturned);
                    }
                    yield return null;
                }
                Assert.That(rose && fell && captured && firstReturned && secondReturned, Is.True,
                    $"At {seesaw.Site.Cell}, board={seesaw.Seconds:F2}, riders={ambient.GetPlaygroundPhase(0)}/{ambient.GetPlaygroundPhase(1)}, " +
                    $"returned={firstReturned}/{secondReturned}, feet={first.transform.position:F2}/{second.transform.position:F2}. " +
                    DescribeSecondApproach() + $" Second return: {SlideApproachBlocker(second.transform.position, entry.Position)}.");
                Assert.That(seesaw.Board.localRotation, Is.EqualTo(Quaternion.identity));

                Assert.That(SpawnChild(), Is.True);
                yield return ReachWait(0);
                for (int i = 0; i < 300 && ambient.GetPlaygroundPhase(0) != CityPlaygroundVisit.Phase.None; i++)
                {
                    ambient.Advance(.1f);
                    Assert.That(seesaw.Playing, Is.False);
                    yield return null;
                }
                Assert.That(ambient.GetPlaygroundPhase(0), Is.EqualTo(CityPlaygroundVisit.Phase.None), "A lone child leaves.");
                Assert.That(Vector3.Distance(ambient.GetWalkerBody(0).transform.position, entry.Position), Is.LessThan(.24f));
                release.Invoke(ambient, new object[] { 0 });

                CityPlaygroundBalanceStation step = null;
                foreach (CityPlaygroundBalanceStation candidate in city.World.BalanceStations)
                    if (candidate.Plan.RouteJoin == seesaw.Site.RouteJoin) step = candidate;
                Assert.That(step, Is.Not.Null);
                step.enabled = true;
                Assert.That(SpawnChild(), Is.True);
                CityPlaygroundVisit balanceVisit = default;
                Assert.That(ambient.TryGetWalker(0, out Vector3 start, out _, out int visitor));
                Assert.That(balanceVisit.TryStart(System.Array.Empty<CityPlaygroundSwing>(), visitor, start,
                    balances: new[] { step }), Is.True, "The branch leads to the balance module.");
                CityPedestrianPresentation climber = ambient.GetWalkerBody(0);
                bool planted = false;
                for (int tick = 0; tick < 260 && balanceVisit.CurrentPhase != CityPlaygroundVisit.Phase.None; tick++)
                {
                    balanceVisit.Advance(climber, visitor, 1.2f, .1f);
                    planted |= Mathf.Abs(climber.Registry.LeftFoot.position.y - step.Top.position.y - .065f) < .023f &&
                        Mathf.Abs(climber.Registry.RightFoot.position.y - step.Top.position.y - .065f) < .023f;
                    yield return null;
                }
                Assert.That(planted && step.Available && balanceVisit.CurrentPhase == CityPlaygroundVisit.Phase.None);
                Assert.That(Vector3.Distance(climber.transform.position, start), Is.LessThan(.24f));
                release.Invoke(ambient, new object[] { 0 });

                Assert.That(GameTimeDayNightRules.IsDaylight(359.99d), Is.False);
                Assert.That(GameTimeDayNightRules.IsDaylight(360d));
                Assert.That(GameTimeDayNightRules.IsDaylight(1079.99d));
                Assert.That(GameTimeDayNightRules.IsDaylight(1080d), Is.False);
                Assert.That(SpawnChild(), Is.True);
                float jump = (float)((GameTimeDayNightRules.SunsetMinutes + .05d - GameSessionState.GameTimeOfDayMinutes) /
                    GameTimeState.GameMinutesPerRealSecond);
                GameSessionState.AdvanceGameTime(jump);
                Assert.That(SpawnChild(), Is.False, "No new street children at sunset.");
                Vector3 feet = ambient.GetWalkerBody(0).transform.position;
                camera.transform.position = feet + seesaw.transform.right * 3f + Vector3.up * 1.3f;
                camera.transform.rotation = Quaternion.LookRotation(feet + Vector3.up * .7f - camera.transform.position);
                yield return null;
                Assert.That(city.Visibility.CanChangeAmbientBodyAt(feet), Is.False);
                ambient.Advance(.01f);
                Assert.That(ambient.TryGetWalker(0, out _, out _, out _), Is.True, "A visible child is not popped away.");
                Assert.That(ambient.GetPlaygroundPhase(0), Is.EqualTo(CityPlaygroundVisit.Phase.None));
                camera.transform.rotation *= Quaternion.Euler(0, 180, 0);
                yield return null;
                ambient.Advance(.01f);
                Assert.That(ambient.TryGetWalker(0, out _, out _, out _), Is.False, "The child retires out of view.");
            }
            finally
            {
                for (int slot = 0; slot < CityAmbientPedestrians.MaximumActive; slot++)
                    release.Invoke(ambient, new object[] { slot });
                foreach (CityPlaygroundBalanceStation balance in city.World.BalanceStations) balance.enabled = true;
                countdown.SetValue(ambient, 0f);
                ambient.enabled = ambientEnabled;
                Time.captureDeltaTime = captureDelta;
                city.Player.Motor.SetInputEnabled(true);
                if (heroCollider != null) heroCollider.enabled = heroColliderEnabled;
                if (follow != null) follow.enabled = true;
                camera.enabled = cameraEnabled;
            }

            bool SpawnChild() => (bool)spawn.Invoke(ambient, new object[] { entry, 1, null, true });

            string DescribeSecondApproach() =>
                $"Route: {SlideApproachBlocker(entry.Position, seesaw.Site.RouteJoin)}. " +
                $"Side: {SlideApproachBlocker(seesaw.Site.RouteJoin, seesaw.Approach)}. " +
                $"Wing: {SlideApproachBlocker(seesaw.Approach, seesaw.Wing(1))}. " +
                $"Dock: {SlideApproachBlocker(seesaw.Wing(1), seesaw.Dock(1))}.";

            IEnumerator ReachWait(int slot)
            {
                float limit = Time.realtimeSinceStartup + 28f;
                while (Time.realtimeSinceStartup < limit && ambient.GetPlaygroundPhase(slot) != CityPlaygroundVisit.Phase.SeesawWait)
                {
                    ambient.Advance(.1f);
                    yield return null;
                }
                Assert.That(ambient.GetPlaygroundPhase(slot), Is.EqualTo(CityPlaygroundVisit.Phase.SeesawWait),
                    $"Child did not reach seesaw {seesaw.Site.Cell} from {entry.Position:F2}.");
            }

            void CheckReturn(int slot, CityPedestrianPresentation rider, ref bool returned)
            {
                if (returned || ambient.GetPlaygroundPhase(slot) != CityPlaygroundVisit.Phase.None) return;
                Assert.That(Vector3.Distance(rider.transform.position, entry.Position), Is.LessThan(.24f));
                Assert.That(rider.AuthoredActionWeight, Is.Zero);
                release.Invoke(ambient, new object[] { slot });
                returned = true;
            }
        }

        private static void AssertSeesawContact(CityPlaygroundSeesaw seesaw, CityPedestrianPresentation rider, string end)
        {
            Transform rig = rider.Registry.Animator.transform;
            Vector3 left = FindPart(seesaw.Board, "SeesawGripL" + end).position;
            Vector3 right = FindPart(seesaw.Board, "SeesawGripR" + end).position;
            Assert.That(Vector3.Distance(FindPart(rig, "SOCKET_Grip.L").position, left), Is.LessThan(.045f));
            Assert.That(Vector3.Distance(FindPart(rig, "SOCKET_Grip.R").position, right), Is.LessThan(.045f));
            Vector3 stone = FindPart(seesaw.Board, "SeesawFootL" + end).position;
            Assert.That(Vector3.Distance(rider.Registry.LeftFoot.position, stone + seesaw.Board.up * .065f), Is.LessThan(.04f));
        }

        private static void AssertPlaygroundSurface(CityPlaygroundLandscapePlan.Site site)
        {
            Vector2 center = CityRoadPolygon.Bounds(site.Pad).center;
            FootstepGroundKind expected = site.IsPark ? FootstepGroundKind.Soil : FootstepGroundKind.Concrete;
            Assert.That(Surface(center).kind, Is.EqualTo(expected),
                $"Playground surface at {center:F2} (park={site.IsPark}).");
            float last = Surface(site.Join).height;
            for (int step = 1; step <= 10; step++)
            {
                Vector2 point = Vector2.Lerp(site.Join, site.Approach, step / 10f);
                (float height, FootstepGroundKind kind) = Surface(point);
                Assert.That(Mathf.Abs(height - last), Is.LessThan(.23f), $"Walkable approach at {point:F2}.");
                if (step >= 4) Assert.That(kind, Is.EqualTo(expected), $"Approach finish at {point:F2}.");
                last = height;
            }

            (float height, FootstepGroundKind kind) Surface(Vector2 point)
            {
                var origin = new Vector3(point.x, site.Height + 1.5f, point.y);
                foreach (RaycastHit hit in Physics.RaycastAll(origin, Vector3.down, 3f,
                    CityPedestrianCollision.NonPedestrianMask, QueryTriggerInteraction.Ignore))
                {
                    var ground = hit.collider.GetComponent<FootstepGround>();
                    if (ground != null)
                        return (hit.point.y, ground.ResolveKind(hit.triangleIndex));
                }
                Assert.Fail($"Missing approach ground at {point:F2}.");
                return default;
            }
        }

        private static void PositionPlaygroundCamera(CityGameRoot city, Camera camera, CityPlaygroundLandscapePlan.Site site)
        {
            Vector2 focus = Vector2.Lerp(CityRoadPolygon.Bounds(site.Pad).center, site.Join, .17f);
            Vector3 floor = new Vector3(focus.x, site.Height, focus.y);
            Vector3 aim = floor + Vector3.up * .77f;
            float distance = site.IsPark ? 9.2f : 5.5f;
            for (int angle = 0; angle < 8; angle++)
            {
                Vector3 view = Quaternion.Euler(0f, 36f + angle * 45f, 0f) * Vector3.back;
                Vector3 eye = floor + view * distance + Vector3.up * (site.IsPark ? 3.25f : 2.6f);
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
                if (Physics.CheckSphere(eye, .22f, CityPedestrianCollision.NonPedestrianMask, QueryTriggerInteraction.Ignore)) continue;
                Vector3 ray = aim - eye;
                if (Physics.Raycast(eye, ray.normalized, ray.magnitude - 1.6f,
                    CityPedestrianCollision.NonPedestrianMask, QueryTriggerInteraction.Ignore)) continue;
                city.Player.Motor.Teleport(floor + view * (distance + 1.4f) + Vector3.up * .3f);
                return;
            }
        }

        private static Transform FindPart(Transform node, string name)
        {
            foreach (Transform part in node.GetComponentsInChildren<Transform>())
                if (part.name == name) return part;
            throw new System.InvalidOperationException($"Missing {name} on {node.name}.");
        }
    }
}
