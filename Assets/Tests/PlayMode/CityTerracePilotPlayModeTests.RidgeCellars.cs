using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest, Timeout(300000)]
        public IEnumerator LiveCity_CellarCourtWalksBothEntrancesAndKeepsViewBench()
        {
            Assert.That(GameSessionState.TryStartGameTimeAt(14 * 60), Is.True);
            yield return LoadProductionCity();
            var site = RidgeCellarPlan.ForLayout(city.Layout);
            Assert.That(site, Is.Not.Null);
#if UNITY_EDITOR
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-exportCityMap") >= 0)
                Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                    .GetMethod("ExportCurrent", Type.EmptyTypes).Invoke(null, null);
#endif
            var motor = city.Player.Motor;
            var camera = city.Camera;
            var follow = camera.GetComponent<PlayerCameraFollow>();
            var route = site.WalkRoute;
            Assert.That(route.Count, Is.GreaterThan(2));
            var offset = Vector3.up * PlayerFactory.GroundedRootOffset;
            motor.SetInputEnabled(false);
            yield return RidgeStand(route[0]);
            var root = GameObject.Find("East Ridge Cellar Court");
            Assert.That(root, Is.Not.Null, "The live eastern hillside must own the five cellars.");
            Physics.SyncTransforms();
            var street = CityHillsideStreetPlan.ForLayout(city.Layout).Routes.Single(
                road => road.Bank == CityTerraceTransportBank.East);
            Assert.That(CitySurfaceLibrary.TryGroundJointMaps(CityExteriorAppearance.SidewalkTexture,
                out Texture2D sidewalkTexture, out _), Is.True);
            var surface = new MaterialPropertyBlock();
            var reportedBend = new Vector3(172.9f, 36.2f, -316f);
            var reference = new Vector3(177.9f, 36f, -358f);
            Vector3 doors = site.Origin + Vector3.up * .95f;
            var farView = route.OrderBy(point => Mathf.Abs(point.z + 333f)).First() + Vector3.up * 1.6f;
            var towardCourt = route.OrderBy(point => Mathf.Abs(point.z + 347f)).First();
            yield return Frame("reported-day", reference + Vector3.up * 1.65f, doors);
            yield return Frame("sidewalk-bend", reportedBend + Vector3.up * 1.6f, farView - Vector3.up * 2.4f);
            yield return Frame("curve-day", farView, towardCourt + Vector3.up * .55f);
            CheckSidewalk(new Vector2(reportedBend.x, reportedBend.z), reportedBend.y);
            var fractions = new[] { .15f, .5f, .85f };
            for (int i = 1; i < site.Drain.Count; i++)
            {
                foreach (float fraction in fractions)
                {
                    Vector2 edge = Vector2.Lerp(site.Drain[i - 1], site.Drain[i], fraction);
                    Vector2 streetCenter = street.Path.Project(edge).Position;
                    Vector2 towardStreet = (streetCenter - edge).normalized;
                    for (float distance = .57f; distance < Vector2.Distance(edge, streetCenter); distance += .30f)
                    {
                        Vector2 point = edge + towardStreet * distance;
                        if (street.TrySampleTop(point, out _, out _)) break;
                        CheckSidewalk(point, site.Origin.y);
                    }
                }
            }
            yield return Frame("court-day", site.Origin + new Vector3(-9.3f, 1.62f, 6.8f),
                site.Origin + new Vector3(2f, 1.05f, 0f));
            AdvanceRidgeClock(21 * 60);
            yield return Frame("reported-evening", reference + Vector3.up * 1.65f, doors);
            yield return Frame("curve-evening", farView, towardCourt + Vector3.up * .55f);
            follow.ClearFixedPose();
            AdvanceRidgeClock(14 * 60);
            yield return CheckOtherBends();
            var bench = city.BenchSits.Where(seat => seat.Plan.Kind == CityBenchSeatKind.Plank)
                .OrderBy(seat => (seat.InteractionPosition - reference).sqrMagnitude).First();
            Debug.Log($"East cellar bench {bench.Plan.Id} at {bench.InteractionPosition:F3}, entry {bench.Plan.EntryRootPosition:F3}, " +
                $"court {site.Origin:F3}, stair foot {site.StairFoot:F3}");
            var nearTrees = Physics.OverlapSphere(reference, 22f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                .Where(part => part.name.IndexOf("Tree", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(part => (part.bounds.center - reference).sqrMagnitude).Take(3);
            Debug.Log("Trees at the cellar court: " + string.Join(", ", nearTrees.Select(part => $"{part.name} {part.bounds.center:F3}")));
            var parts = root.GetComponentsInChildren<Transform>(true);
            for (int index = 1; index <= 5; index++)
            {
                var door = parts.Single(part => part.name == $"Door{index:00}");
                var front = door.position + Vector3.forward * 1.25f;
                Assert.That(Physics.Raycast(front, Vector3.back, out var hit, 1.7f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True, door.name + " is shut.");
                Assert.That(hit.transform.IsChildOf(root.transform), Is.True, door.name + " must hit its own cellar.");
                Assert.That(Mathf.Abs(hit.point.z - door.position.z), Is.LessThan(.16f),
                    door.name + " collision must meet the recessed door leaf.");
            }

            var map = new CityMapCityTeleportGround(city.Layout);
            var marker = city.Map.PointsOfInterest.Single(place => place.StableId == RidgeCellarPlan.Id);
            Assert.That(Vector3.Distance(marker.WorldPosition, site.Arrival + offset), Is.LessThan(.035f));
            Assert.That(map.TryClampArrival(marker.WorldPosition, out var landing), Is.True);
            Assert.That(Vector3.Distance(landing, marker.WorldPosition), Is.LessThan(.075f));
            foreach (var polygon in site.Solids)
            {
                Vector2 center = polygon.Aggregate(Vector2.zero, (sum, point) => sum + point) / polygon.Length;
                if (map.TryResolveStandingPosition(center, out var resolved))
                    Assert.That(CityRoadPolygon.Contains(polygon, new Vector2(resolved.x, resolved.z)), Is.False,
                        $"Map arrival inside a solid cellar part at {center}.");
            }
            foreach (var foot in route)
            {
                Assert.That(city.World.WalkableArea.Contains(foot + offset, motor.GetComponent<CharacterController>().radius),
                    Is.True, $"Full body clearance at cellar route point {foot}.");
            }

            Time.captureDeltaTime = .1f;
            int previousInterval = OnDemandRendering.renderFrameInterval;
            try
            {
                OnDemandRendering.renderFrameInterval = 12;
                yield return RidgeWalk(route, "Cart path to cellars and stair to street");
                yield return RidgeWalk(route.Reverse().ToArray(), "Street stair to cellars and far end of turn");
                Assert.That(Vector3.Distance(bench.InteractionPosition, reference), Is.LessThan(22f),
                    "The original bench must be present at the cellar court.");
                yield return ExerciseCellarBench(bench, follow);
            }
            finally
            {
                follow.ClearFixedPose();
                OnDemandRendering.renderFrameInterval = previousInterval;
            }

            IEnumerator CheckOtherBends()
            {
                var streets = CityHillsideStreetPlan.ForLayout(city.Layout);
                var extensions = streets.SidewalkExtensions;
                Assert.That(streets.Routes.All(road => road.BendSidewalks.Count == CityHillsideStreetRoute.LaneCount), Is.True);
                Assert.That(extensions.Count + 1, Is.EqualTo(2 * CityHillsideStreetRoute.LaneCount),
                    "Both approaches and all switchbacks must be paved. The cellar bend is checked separately.");
                Assert.That(CityMapArtResources.TryGet(city.Map, out _, out _), Is.True);
                var ground = CityHillsideGroundPlan.ForLayout(city.Layout);
                var publicStreet = CityStreetSupportPlan.ForLayout(city.Layout);
                int number = 0;
                foreach (var bend in extensions)
                {
                    int middle = bend.Pieces.Count / 2;
                    for (int j = middle - 2; j < middle + 2; j++)
                    {
                        var polygon = bend.Pieces[j].Points;
                        var centre = polygon.Aggregate(Vector2.zero, (sum, point) => sum + point) / polygon.Length;
                        if (publicStreet.TrySampleTop(centre, out _, out _)) continue;
                        Assert.That(ground.TrySampleTop(centre, out float top), Is.True, $"Shoulder support at {centre}");
                        CheckSidewalk(centre, top);
                        if (j != middle || (number != 0 && number != 3 && number != 8)) continue;
                        var lead = bend.Pieces[middle + 1].Bounds.center;
                        yield return Frame($"ridge-sidewalk-{number}", new Vector3(centre.x, top + 1.65f, centre.y),
                            new Vector3(lead.x, top + .4f, lead.y));
                    }
                    number++;
                }
            }

            void CheckSidewalk(Vector2 point, float groundY)
            {
                var start = new Vector3(point.x, groundY + 4f, point.y);
                Assert.That(Physics.Raycast(start, Vector3.down, out var hit, 8f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True, $"Paved shoulder at {point}");
                Assert.That(hit.collider.name, Is.EqualTo("Terraced ground and courts").Or.EqualTo("Shared residential hillside"),
                    $"Paved grade at {point}");
                var mesh = ((MeshCollider)hit.collider).sharedMesh;
                int sidewalkSlot = mesh.subMeshCount - 1;
                int startTriangle = (int)mesh.GetIndexStart(sidewalkSlot) / 3;
                int endTriangle = startTriangle + (int)mesh.GetIndexCount(sidewalkSlot) / 3;
                Assert.That(hit.triangleIndex, Is.InRange(startTriangle, endTriangle - 1), $"Sidewalk at {point}");
                hit.collider.GetComponent<Renderer>().GetPropertyBlock(surface, sidewalkSlot);
                Assert.That(surface.GetTexture("_BaseMap"), Is.SameAs(sidewalkTexture), $"Sidewalk tiles at {point}");
                Assert.That(hit.collider.GetComponent<FootstepGround>().ResolveKind(hit.triangleIndex),
                    Is.EqualTo(FootstepGroundKind.Stone), $"Sidewalk footsteps at {point}");
            }

            IEnumerator Frame(string name, Vector3 eye, Vector3 target)
            {
                follow.SetFixedPose(eye, Quaternion.LookRotation(target - eye), 66f);
                for (int i = 0; i < 3; i++) yield return null;
                Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null),
                    "The street and courtyard require a rendered review.");
                AreaCaptureFixture.CaptureCurrentCamera(camera, "RidgeCellarCourt", name, 1280, 720);
            }
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator LiveCity_CellarKeeperSortsByDayAndLampsReachWorkAndSteps()
        {
            Assert.That(GameSessionState.TryStartGameTimeAt(14 * 60), Is.True);
            yield return LoadProductionCity();
            var site = RidgeCellarPlan.ForLayout(city.Layout);
            city.Player.Motor.SetInputEnabled(false);
            yield return RidgeStand(site.Arrival);
            var court = GameObject.Find("East Ridge Cellar Court");
            Assert.That(court, Is.Not.Null);
            var keeper = court.GetComponentInChildren<RidgeCellarKeeper>(true);
            Assert.That(keeper, Is.Not.Null);
            var camera = city.Camera;
            var follow = camera.GetComponent<PlayerCameraFollow>();
            var lamps = court.GetComponentsInChildren<Light>();
            Assert.That(lamps.Length, Is.EqualTo(4));
            var work = lamps.Single(lamp => lamp.name == "Cellar Work light");
            var entry = lamps.Single(lamp => lamp.name == "Cellar Entry light");
            var wall = lamps.Single(lamp => lamp.name == "Cellar Wall light");
            var lane = lamps.Single(lamp => lamp.name == "Cellar Lane light");
            var worktop = site.Origin + new Vector3(8.12f, .92f, 5.12f);
            var steps = new Vector3(site.StairFoot.x, site.UpperY, site.StairFoot.y + .8f);
            var wallFace = site.Origin + new Vector3(-4.78f, 1.05f, .25f);
            var forward = new Vector3(site.LaneLampForward.x, 0f, site.LaneLampForward.y);
            var right = Vector3.Cross(Vector3.up, forward);
            var pole = court.GetComponentsInChildren<Transform>().Single(part => part.name == "CellarLaneLamp(Clone)");
            var track = pole.position + 2.3f * right + 4f * forward;
            Assert.That(CityHillsideGroundPlan.ForLayout(city.Layout).TrySampleTop(new Vector2(track.x, track.z), out track.y), Is.True);
            var trackEye = pole.position + 2f * right - 4.7f * forward + 1.65f * Vector3.up;
            var wallEye = wallFace + new Vector3(2.7f, .58f, 5.1f);
            Assert.That(Vector3.Distance(lane.transform.position, pole.position + .64f * right + 2.35f * Vector3.up + .11f * forward),
                Is.LessThan(.025f), "The road lamp sits above its footing on the arm that projects over the ruts.");
            Assert.That(CityMapArtResources.TryGet(city.Map, out _, out _), Is.True,
                "The footing is a street obstacle on the existing surveyed ground.");
            CheckBeam(work, worktop);
            CheckBeam(entry, steps);
            CheckBeam(wall, wallFace);
            CheckBeam(lane, track);
            Assert.That(pole.GetComponentInChildren<MeshCollider>(), Is.Not.Null);
            Assert.That(work.color.r - work.color.b, Is.GreaterThan(.2f));
            Assert.That(court.GetComponentsInChildren<Transform>().Count(part => part.name == "CellarGrate(Clone)" &&
                part.GetComponentInChildren<MeshRenderer>()), Is.EqualTo(2));
            float daytime = work.intensity;
            Assert.That(keeper.Worker != null && keeper.Worker.gameObject.activeInHierarchy, Is.True,
                "Entering a running city at midday must already show the resident at work.");
            keeper.AutoAdvance = false;
            const double clockRate = GameTimeState.GameMinutesPerRealSecond;
            try
            {
                keeper.SampleAt(539.99);
                Assert.That(keeper.Worker.gameObject.activeSelf, Is.False);
                double halfTrip = keeper.TravelSeconds * .5 * clockRate;
                keeper.SampleAt(540 + halfTrip);
                var outgoing = keeper.Worker.transform.position;
                Assert.That(Vector3.Distance(outgoing, keeper.HomeDoor), Is.GreaterThan(2f));
                Assert.That(Vector3.Distance(outgoing, keeper.WorkFeet), Is.GreaterThan(2f));
                keeper.SampleAt(1080 - halfTrip);
                Assert.That(Vector3.Distance(outgoing, keeper.Worker.transform.position), Is.LessThan(.10f));
                double atTable = 540 + keeper.TravelSeconds * clockRate;
                keeper.SampleAt(atTable + .05 * clockRate);
                Vector3 source = keeper.Produce.position;
                keeper.SampleAt(atTable + 2.0 * clockRate);
                Assert.That(keeper.IsWorking && keeper.HandsOnProduce, Is.True);
                Assert.That(keeper.HandContactError, Is.LessThan(.10f), "Pick-up contact");
                Assert.That(Vector3.Distance(source, keeper.Produce.position), Is.GreaterThan(.035f));
                keeper.SampleAt(atTable + 4.9 * clockRate);
                Assert.That(keeper.HandsOnProduce, Is.True);
                Assert.That(keeper.HandContactError, Is.LessThan(.10f), "Hand-off contact");
                yield return Frame("resident-sorting", site.Origin + new Vector3(5.62f, 1.68f, 3.34f),
                    worktop + new Vector3(0f, .33f, .40f));
                yield return Frame("day-court", new Vector3(177.9f, 37.65f, -358f),
                    site.Origin + Vector3.up * .95f);
                keeper.SampleAt(atTable + 7.0 * clockRate);
                Assert.That(Vector3.Distance(source, keeper.Produce.position), Is.GreaterThan(.30f),
                    "The potato reaches the other crate.");
                keeper.SampleAt(1079.99);
                Assert.That(Vector3.Distance(keeper.Worker.transform.position, keeper.Route[0]), Is.LessThan(.15f),
                    "The resident rounds the inside jamb before leaving the outdoor scene.");
                keeper.SampleAt(1080);
                Assert.That(keeper.Worker.gameObject.activeSelf, Is.False);
                keeper.AutoAdvance = true;
                AdvanceRidgeClock(21 * 60);
                city.Player.Motor.Teleport(site.WalkRoute[0]);
                yield return Frame("lit-court", new Vector3(177.9f, 37.65f, -358f), site.Origin + Vector3.up * .95f);
                Assert.That(keeper.Worker.gameObject.activeSelf, Is.False);
                Assert.That(daytime, Is.LessThan(work.intensity));
                Assert.That(work.enabled && entry.enabled, Is.True);
                yield return Frame("lit-table", site.Origin + new Vector3(5.45f, 1.65f, 3.25f), worktop);
                yield return Frame("lit-steps", site.Origin + new Vector3(-4.0f, 1.5f, 9.4f), steps);
                yield return Frame("lit-bend", site.Origin + new Vector3(12.3f, 1.8f, 36.5f),
                    site.Origin + new Vector3(4.5f, .85f, .8f));
                yield return Frame("lit-doors", wallEye, wallFace);
                yield return Frame("lit-track", trackEye, track);
                // Record the same receivers with the bulbs masked for a local rendered comparison.
                try
                {
                    wall.enabled = lane.enabled = false;
                    yield return Frame("unlit-doors", wallEye, wallFace);
                    yield return Frame("unlit-track", trackEye, track);
                    Assert.That(ReceiverLuma("lit-doors") - ReceiverLuma("unlit-doors"), Is.GreaterThan(.025f),
                        "The grazing fixture must reach the masonry near the recessed doors.");
                    Assert.That(ReceiverLuma("lit-track") - ReceiverLuma("unlit-track"), Is.GreaterThan(.025f),
                        "The bend lamp must reveal the track underfoot.");
                    work.enabled = entry.enabled = false;
                    yield return Frame("unlit-table", site.Origin + new Vector3(5.45f, 1.65f, 3.25f), worktop);
                    yield return Frame("unlit-steps", site.Origin + new Vector3(-4.0f, 1.5f, 9.4f), steps);
                }
                finally { foreach (var lamp in lamps) lamp.enabled = true; }
            }
            finally { keeper.AutoAdvance = true; follow.ClearFixedPose(); }

            void CheckBeam(Light lamp, Vector3 target)
            {
                Assert.That(lamp.type, Is.EqualTo(LightType.Spot));
                Vector3 ray = target - lamp.transform.position;
                Assert.That(ray.magnitude, Is.LessThan(lamp.range));
                Assert.That(Vector3.Angle(lamp.transform.forward, ray), Is.LessThan(lamp.innerSpotAngle * .5f),
                    $"{lamp.name} at {lamp.transform.position} facing {lamp.transform.forward}, receiver {target}.");
            }

            IEnumerator Frame(string id, Vector3 eye, Vector3 target)
            {
                follow.SetFixedPose(eye, Quaternion.LookRotation(target - eye), 62f);
                for (int frame = 0; frame < 3; frame++) yield return null;
                Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null));
                AreaCaptureFixture.CaptureCurrentCamera(camera, "RidgeCellarDaylight", id, 1280, 720);
            }

            float ReceiverLuma(string id)
            {
                string path = System.IO.Path.Combine(Application.dataPath, "..", "Captures", "RidgeCellarDaylight", id + ".png");
                var frame = new Texture2D(2, 2, TextureFormat.RGB24, false);
                try
                {
                    Assert.That(frame.LoadImage(System.IO.File.ReadAllBytes(path)), Is.True);
                    float sum = 0;
                    for (int y = -24; y < 24; y += 4)
                        for (int x = -32; x < 32; x += 4)
                            sum += frame.GetPixel(frame.width / 2 + x, frame.height / 2 + y).grayscale;
                    return sum / (12 * 16);
                }
                finally { UnityEngine.Object.Destroy(frame); }
            }
        }

        private IEnumerator ExerciseCellarBench(CityBenchSitInteraction seat, PlayerCameraFollow follow)
        {
            var motor = city.Player.Motor;
            var cursor = new object();
            bool previousFocus = GameInput.MovementFocused;
            var input = new InputTestFixture();
            input.Setup();
            try
            {
                var keyboard = InputSystem.AddDevice<Keyboard>();
                keyboard.MakeCurrent();
                motor.Teleport(seat.Plan.EntryRootPosition);
                motor.SetInputEnabled(true);
                city.Player.Interactor.SetInputEnabled(true);
                GameInput.HandleApplicationFocus(true);
                GameCursor.Capture(cursor, CursorLockMode.Locked, false, 0);
                Vector3 eye = motor.transform.position + Vector3.up * 1.5f - seat.Plan.ApproachDirection * .15f;
                follow.SetFixedPose(eye, Quaternion.LookRotation(seat.InteractionPosition - eye), 59f);
                yield return null;
                yield return null;
                Assert.That(city.Player.Interactor.ActiveInteractable, Is.SameAs(seat));
                yield return PressE();
                for (int frame = 0; frame < 100 && !seat.IsSeated; frame++) yield return null;
                Assert.That(seat.IsSeated, Is.True);
                yield return PressE();
                for (int frame = 0; frame < 100 && seat.OwnsActiveInteraction; frame++) yield return null;
                Assert.That(seat.OwnsActiveInteraction, Is.False);
                Assert.That(Vector3.Distance(motor.transform.position, seat.Plan.EntryRootPosition), Is.LessThan(.09f));

                IEnumerator PressE()
                {
                    input.Press(keyboard.eKey, queueEventOnly: true);
                    yield return null;
                    input.Release(keyboard.eKey, queueEventOnly: true);
                    yield return null;
                }
            }
            finally
            {
                if (seat.OwnsActiveInteraction) seat.Controller.CancelActiveInteraction();
                input.TearDown();
                GameCursor.Release(cursor);
                GameInput.HandleApplicationFocus(previousFocus);
            }
        }
    }
}
