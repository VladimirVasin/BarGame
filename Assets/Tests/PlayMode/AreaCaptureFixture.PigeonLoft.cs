using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
#if UNITY_EDITOR
        [UnityEditor.MenuItem("Bar Promenade/City/Verify Pigeon Loft")]
        private static void RunPigeonLoftVerification() => RunPigeonVerification(
            "BarPromenade.Tests.PlayMode.AreaCaptureFixture.CityPigeonLoft");

        [UnityEditor.MenuItem("Bar Promenade/City/Verify Pigeon Loft Bus Clearances")]
        private static void RunPigeonBusVerification() => RunPigeonVerification(
            "BarPromenade.Tests.PlayMode.CityTerracePilotPlayModeTests.LiveCity_EveryBusStopCanBeBoardedFromThePublicPavement");

        private static void RunPigeonVerification(string test)
        {
            const string prefix = "UnityEditor.TestTools.TestRunner.Api.";
            const string assembly = ", UnityEditor.TestRunner";
            var apiType = System.Type.GetType(prefix + "TestRunnerApi" + assembly, true);
            var filterType = System.Type.GetType(prefix + "Filter" + assembly, true);
            var modeType = System.Type.GetType(prefix + "TestMode" + assembly, true);
            var settingsType = System.Type.GetType(prefix + "ExecutionSettings" + assembly, true);
            object filter = System.Activator.CreateInstance(filterType);
            filterType.GetField("testMode").SetValue(filter, System.Enum.Parse(modeType, "PlayMode"));
            filterType.GetField("testNames").SetValue(filter, new[] { test });
            var filters = System.Array.CreateInstance(filterType, 1);
            filters.SetValue(filter, 0);
            object settings = System.Activator.CreateInstance(settingsType, new object[] { filters });
            var api = ScriptableObject.CreateInstance(apiType);
            apiType.GetMethod("Execute").Invoke(api, new[] { settings });
        }
#endif

        [UnityTest]
        [Timeout(240000)]
        [Explicit("One live City pigeon loft: footprint, six living birds, keeper contacts, pause and day/night frames.")]
        public IEnumerator CityPigeonLoft()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeAt(12 * 60);
            var load = SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
            float deadline = Time.realtimeSinceStartup + 120f;
            CityGameRoot city = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                city = Object.FindAnyObjectByType<CityGameRoot>();
                if (load.isDone && city != null && city.IsInitialized && !CompositionDriver.IsComposing) break;
                yield return null;
            }
            Assert.That(city != null && city.IsInitialized && !CompositionDriver.IsComposing, Is.True);
            var plan = CityPigeonLoftPlan.ForLayout(city.Layout);
            Assert.That(plan.IsEnabled, Is.True);
            Debug.Log("Pigeon loft origin " + plan.Origin + " footprint " + plan.Bounds);
            var flock = Object.FindAnyObjectByType<CityPigeonFlock>();
            var keeper = Object.FindAnyObjectByType<CityPigeonKeeper>();
            Assert.That(flock, Is.Not.Null);
            Assert.That(keeper, Is.Not.Null);
            Assert.That(flock.BirdCount, Is.EqualTo(6));
            Assert.That(plan.Origin.x, Is.EqualTo(-141.5f));
            Assert.That(plan.Origin.z, Is.EqualTo(-334.75f));
            Assert.That(city.Map.PointsOfInterest, Has.Some.Matches<CityMapPointOfInterest>(
                point => point.StableId == "ridge-pigeon-loft"));
            foreach (var street in CityHillsideStreetPlan.ForLayout(city.Layout).Routes)
                foreach (Vector2 corner in new[] { plan.Bounds.min, plan.Bounds.max,
                    new Vector2(plan.Bounds.xMin, plan.Bounds.yMax), new Vector2(plan.Bounds.xMax, plan.Bounds.yMin) })
                    Assert.That(street.Path.Project(corner).DistanceSquared, Is.GreaterThanOrEqualTo(8.5f * 8.5f));
            var courtyard = plan.Courtyard;
            var handLamp = GameObject.Find("Pigeon Keeper Hand Lamp").GetComponentInChildren<Light>();
            Assert.That(handLamp.type, Is.EqualTo(LightType.Point));
            Assert.That(handLamp.range, Is.EqualTo(8f));
            Assert.That(handLamp.enabled, Is.True);
            city.Player.Motor.Teleport(courtyard.WalkPath[0] + Vector3.up * PlayerFactory.GroundedRootOffset);
            keeper.PresentationObserver = city.Player.GameObject.transform;
            Physics.SyncTransforms();
            yield return null;
            yield return WalkPigeonCourtyard(city, courtyard.WalkPath, false);
            yield return WalkPigeonCourtyard(city, courtyard.WalkPath, true);
            city.Player.Motor.Teleport(courtyard.WalkPath[courtyard.WalkPath.Count - 1] + Vector3.up * PlayerFactory.GroundedRootOffset);
            Physics.SyncTransforms();
            CityHillsideFurniturePlacement phone = default;
            foreach (var item in CityHillsideFurniturePlan.ForLayout(city.Layout).Placements)
                if (item.Id == CityHillsideFurniturePlan.LoftPhoneId) phone = item;
            Assert.That(phone.Id, Is.Not.Null, "The same phone must move to the upper street.");
            Assert.That(phone.Position.z, Is.LessThan(-352f));
            Debug.Log("Upper street phone " + phone.Position);
            Assert.That(courtyard.Bounds.Overlaps(phone.AccessBounds), Is.False);
            bool reachedPublicPavement = false;
            var hillsideGround = CityHillsideGroundPlan.ForLayout(city.Layout);
            foreach (var ramp in CityHillsideFurniturePlan.GroundRamps(city.Layout))
            {
                if (!ramp.Bounds.Contains(new Vector2(phone.Position.x, phone.Position.z + .5f))) continue;
                for (int sample = 0; sample <= 12; sample++)
                {
                    float t = sample / 12f;
                    var point = new Vector2(phone.Position.x, Mathf.Lerp(ramp.StartZ, ramp.EndZ, t));
                    Assert.That(hillsideGround.TrySampleTop(point, out float top), Is.True);
                    Assert.That(top, Is.EqualTo(Mathf.Lerp(ramp.StartY, ramp.EndY, t)).Within(.05f),
                        "Phone approach must meet the graded earth from its landing to the curb.");
                }
                foreach (var street in CityHillsideStreetPlan.ForLayout(city.Layout).Routes)
                {
                    var curb = new Vector2(phone.Position.x, ramp.EndZ + .03f);
                    if (street.TrySampleTop(curb, out float curbTop, out var kind) &&
                        kind == CityStreetSupportKind.Sidewalk && Mathf.Abs(curbTop - ramp.EndY) < .05f)
                        reachedPublicPavement = true;
                }
            }
            Assert.That(reachedPublicPavement, Is.True, "Phone entry must connect to the upper public sidewalk.");
            Transform root = keeper.transform;
            Bounds bounds = default;
            bool first = true;
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (first) { bounds = renderer.bounds; first = false; }
                else bounds.Encapsulate(renderer.bounds);
            }
            Assert.That(bounds.size.y, Is.InRange(4f, 7f), "Imported meshes must retain authored metres.");
            Assert.That(bounds.size.x, Is.InRange(3f, 8f));
            var camera = Camera.main;
            var follow = camera.GetComponent<PlayerCameraFollow>();
            bool followEnabled = follow != null && follow.enabled;
            Vector3 savedPosition = camera.transform.position;
            Quaternion savedRotation = camera.transform.rotation;
            float savedFov = camera.fieldOfView;
            float savedCaptureDelta = Time.captureDeltaTime;
            if (follow != null) follow.enabled = false;
            try
            {
                // Exercise the same bounded simulation entry point as Update.
                for (int slot = 0; slot < 16 && (GameWeatherRules.EvaluateCurrent().Kind == WeatherKind.HeavyRain ||
                    GameWeatherRules.EvaluateCurrent().Kind == WeatherKind.Thunderstorm); slot++)
                {
                    GameSessionState.AdvanceGameTime((float)(90d / GameTimeState.GameMinutesPerRealSecond));
                    if (GameSessionState.GameMinuteOfDay < 360 || GameSessionState.GameMinuteOfDay >= 1320)
                        AdvanceResidentLifeClockTo(8 * 60);
                }
                int landings = flock.LandingCount;
                bool flightCaptured = false;
                bool bathCaptured = false;
                for (int step = 0; step < 2400; step++)
                {
                    flock.Advance(.05f);
                    Assert.That(flock.FlyingCount, Is.InRange(0, 2));
                    if (!flightCaptured && flock.FlyingCount > 0 && step > 30)
                    {
                        flightCaptured = true;
                        yield return PigeonLoftFrame(camera, root.TransformPoint(new Vector3(-4.8f, 3.5f, -5f)),
                            root.TransformPoint(new Vector3(1f, 3f, 0f)), "00-live-flight");
                    }
                    if (!bathCaptured && flock.BathDrinkingCount > 0)
                    {
                        bathCaptured = true;
                        Assert.That(flock.BathContactError, Is.LessThanOrEqualTo(.036f), "Bill must meet the actual bath water.");
                        yield return PigeonLoftFrame(camera, courtyard.Bath + new Vector3(-1.2f, .65f, -1.5f),
                            courtyard.Bath + Vector3.up * .15f, "00-bath-drinking");
                    }
                    for (int bird = 0; bird < flock.BirdCount; bird++)
                        Assert.That(float.IsNaN(flock.GetBirdTransform(bird).position.y), Is.False);
                }
                Assert.That(flock.LandingCount, Is.GreaterThan(landings), "A live bird must finish an actual flight.");
                Assert.That(flock.BathVisitCount, Is.GreaterThan(0), "A real pigeon must land beside the bath. " + flock.DescribeRoutes());
                Assert.That(bathCaptured, Is.True, "A resident must drink from the courtyard bath.");
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    Vector3 point = flock.GetBirdTransform(0).position;
                    flock.Advance(1f);
                    Assert.That(flock.GetBirdTransform(0).position, Is.EqualTo(point));
                }
                keeper.AutoAdvance = false;
                double dawn = 360d / GameTimeState.GameMinutesPerRealSecond;
                keeper.ApplyAt(dawn + 20d);
                yield return PigeonLoftFrame(camera, new Vector3(-154.1f, plan.Origin.y + 1.72f, -336.9f),
                    new Vector3(-145.5f, plan.Origin.y + 1.65f, -333f), "01-day-approach");
                Assert.That(handLamp.intensity, Is.InRange(4f - .01f, 6f + .01f));
                yield return PigeonLoftFrame(camera, courtyard.Bounds.center.x * Vector3.right +
                    new Vector3(0f, plan.Origin.y + 15f, courtyard.Bounds.center.y - 7f),
                    new Vector3(courtyard.Bounds.center.x, plan.Origin.y, courtyard.Bounds.center.y), "01-yard-overview");
                yield return PigeonLoftFrame(camera, phone.Position + new Vector3(-4f, 1.7f, 7f),
                    phone.Position + Vector3.up * 1.2f, "01-upper-street-phone");
                keeper.ApplyAt(dawn + 11d);
                Assert.That(keeper.HandContactError, Is.LessThanOrEqualTo(.035f));
                yield return PigeonLoftFrame(camera, root.TransformPoint(new Vector3(2.8f, 3.4f, -4f)),
                    root.TransformPoint(new Vector3(.25f, 2.9f, -1.3f)), "02-keeper-feeding");
                keeper.ApplyAt(dawn + 66d);
                Assert.That(keeper.HandContactError, Is.LessThanOrEqualTo(.035f));
                yield return PigeonLoftFrame(camera, root.TransformPoint(new Vector3(-3.5f, 3.5f, -4f)),
                    root.TransformPoint(new Vector3(.65f, 2.85f, -1.3f)), "03-water-contact");
                yield return VerifyPigeonKeeperCommute(city, keeper, camera);
                keeper.AutoAdvance = true;
                AdvanceResidentLifeClockTo(22 * 60 + 30);
                // The real frame samples the keeper before the next physics
                // queries. Settle that same night pose before fast simulation.
                keeper.ApplyAt((GameSessionState.GameDayIndex * 1440d +
                    GameSessionState.GameTimeOfDayMinutes) / GameTimeState.GameMinutesPerRealSecond);
                Physics.SyncTransforms();
                for (int step = 0; step < 1800 && flock.ShelteredCount < 6; step++) flock.Advance(.05f);

                for (int frame = 0; frame < 12; frame++) yield return null;
                yield return PigeonLoftFrame(camera, new Vector3(-154.1f, plan.Origin.y + 1.72f, -336.9f),
                    new Vector3(-145.5f, plan.Origin.y + 1.65f, -333f), "04-night-approach");
                Assert.That(handLamp.enabled, Is.True);
                Assert.That(handLamp.intensity, Is.EqualTo(6f).Within(.02f));
                Assert.That(flock.ShelteredCount, Is.EqualTo(6), "Every resident returns to an individual covered perch. " + flock.DescribeRoutes());
                Assert.That(keeper.IsAtHome, Is.True);
                var gate = Object.FindAnyObjectByType<CityPigeonCourtyardGate>();
                Assert.That(gate.Openness, Is.LessThan(.001f));
                Time.captureDeltaTime = 1f / 30f;
                city.Player.Motor.Teleport(gate.Interaction.GroundedInsideDock);
                Physics.SyncTransforms();
                yield return null;
                Assert.That(gate.Interaction.Begin(), Is.True, "The hero must be able to open the night gate from inside.");
                for (int frame = 0; frame < 360 && gate.Interaction.OwnsActiveInteraction; frame++) yield return null;
                Assert.That(gate.Interaction.OwnsActiveInteraction, Is.False);
                Assert.That(gate.Openness, Is.GreaterThan(.999f), "The night gate must allow a real hero exit.");
                Assert.That(gate.Interaction.Begin(), Is.True, "The hero can close the leaf from the yard.");
                for (int frame = 0; frame < 480 && gate.Interaction.OwnsActiveInteraction; frame++) yield return null;
                Assert.That(gate.Interaction.OwnsActiveInteraction, Is.False);
                Assert.That(gate.Openness, Is.LessThan(.001f));
                city.Player.Motor.Teleport(gate.Interaction.GroundedOutsideDock);
                Physics.SyncTransforms();
                yield return null;
                Assert.That(gate.Interaction.Begin(), Is.True, "The hero can open the night gate from the public side.");
                for (int frame = 0; frame < 360 && gate.Interaction.OwnsActiveInteraction; frame++) yield return null;
                Assert.That(gate.Openness, Is.GreaterThan(.999f));
                flock.enabled = false;
                foreach (var source in flock.GetComponentsInChildren<AudioSource>()) Assert.That(source.isPlaying, Is.False);
                flock.enabled = true;
            }
            finally
            {
                camera.transform.SetPositionAndRotation(savedPosition, savedRotation);
                camera.fieldOfView = savedFov;
                Time.captureDeltaTime = savedCaptureDelta;
                if (follow != null) follow.enabled = followEnabled;
                keeper.AutoAdvance = true;
            }
        }

        private static IEnumerator VerifyPigeonKeeperCommute(CityGameRoot city, CityPigeonKeeper keeper, Camera camera)
        {
            var plan = keeper.CommutePlan;
            var gate = Object.FindAnyObjectByType<CityPigeonCourtyardGate>();
            Assert.That(plan.HomeStableId, Is.EqualTo("ridge-west-house:-23:-7"));
            Assert.That(plan.House.Lot.IsPlayerHome, Is.False);
            city.Player.Motor.Teleport(plan.GateOutsideStand + new Vector3(-5f, PlayerFactory.GroundedRootOffset, -3f));
            Physics.SyncTransforms();
            keeper.AutoAdvance = false;
            double departure = keeper.EveningDepartureSeconds;
            keeper.ApplyAt(departure - .05d);
            bool gateFrame = false, stairsFrame = false;
            Vector3 previous = keeper.Worker.transform.position;
            for (int tick = 0; tick <= Mathf.CeilToInt((float)(keeper.CommuteSeconds + 1d) / .05f); tick++)
            {
                keeper.AdvanceAt(departure + tick * .05d);
                Vector3 feet = keeper.Worker.transform.position;
                Assert.That(Vector3.Distance(previous, feet), Is.LessThan(.16f), "Keeper commute must remain continuous at " + tick);
                previous = feet;
                if (!stairsFrame && feet.y < CityPigeonLoftPlan.ForLayout(city.Layout).Origin.y + 1.4f &&
                    tick * .05f < keeper.GateApproachSeconds * .55f)
                {
                    stairsFrame = true;
                    yield return PigeonLoftFrame(camera, feet + new Vector3(-2f, 1.5f, 3f), feet + Vector3.up, "05-keeper-descending");
                }
                if (!gateFrame && keeper.IsUsingGate && keeper.GateActionTime > 1.6f)
                {
                    gateFrame = true;
                    Assert.That(keeper.HandContactError, Is.LessThan(.04f), "Keeper hand must hold the moving grille.");
                    yield return PigeonLoftFrame(camera, plan.GateOutsideStand + new Vector3(-2.5f, 1.4f, -2f),
                        gate.GripPosition, "06-keeper-closing-gate");
                }
            }
            Assert.That(gateFrame && stairsFrame, Is.True);
            Assert.That(keeper.IsAtHome, Is.True);
            Assert.That(Vector3.Distance(keeper.Worker.transform.position, plan.InsideHome), Is.LessThan(.025f));
            Assert.That(gate.Openness, Is.LessThan(.001f));
            yield return PigeonLoftFrame(camera, plan.HomeDoor + plan.House.Lot.FacadeForward * 4f + Vector3.left * 2f + Vector3.up * 1.6f,
                plan.HomeDoor + Vector3.up, "07-keeper-home");

            // The same leaf must refuse the entire swept volume while a real hero stands in its arc.
            Vector3 blocked = gate.Pivot.TransformPoint(new Vector3(.7f, 0f, .7f));
            city.Player.Motor.Teleport(blocked + Vector3.up * PlayerFactory.GroundedRootOffset);
            Physics.SyncTransforms();
            Assert.That(gate.TrySetOpenness(1f), Is.False);
            Assert.That(gate.Openness, Is.LessThan(.001f));
            city.Player.Motor.Teleport(plan.GateOutsideStand + new Vector3(-5f, PlayerFactory.GroundedRootOffset, -3f));
            camera.transform.position = plan.GateOutsideStand + Vector3.up * 1.6f;
            camera.transform.rotation = Quaternion.LookRotation(Vector3.left);
            Physics.SyncTransforms();

            double morning = keeper.MorningDepartureSeconds;
            keeper.ApplyAt(morning - .05d);
            bool opened = false;
            for (int tick = 0; tick <= Mathf.CeilToInt((float)(keeper.CommuteSeconds + .1d) / .05f); tick++)
            {
                keeper.AdvanceAt(morning + tick * .05d);
                if (keeper.IsUsingGate && gate.Openness > .99f) opened = true;
            }
            Assert.That(opened, Is.True);
            Assert.That(keeper.IsAtHome || keeper.IsCommuting, Is.False);
            Assert.That(gate.Openness, Is.GreaterThan(.999f));
            Assert.That(Vector3.Distance(keeper.Worker.transform.position, plan.WorkToGate[0]), Is.LessThan(.06f));

            // A player-closed daytime leaf is reopened before the keeper crosses it at departure.
            keeper.ApplyAt(departure - .05d);
            Assert.That(gate.TrySetOpenness(0f), Is.True);
            bool reopened = false;
            for (int tick = 0; tick <= Mathf.CeilToInt((float)(keeper.CommuteSeconds + 25d) / .05f); tick++)
            {
                keeper.AdvanceAt(departure + tick * .05d);
                if (keeper.IsUsingGate && gate.Openness > .99f) reopened = true;
            }
            Assert.That(reopened, Is.True, "The keeper must reopen a closed leaf before walking out.");
            Assert.That(keeper.IsAtHome, Is.True);
            Assert.That(gate.Openness, Is.LessThan(.001f));
        }

        private static IEnumerator WalkPigeonCourtyard(CityGameRoot city,
            System.Collections.Generic.IReadOnlyList<Vector3> path, bool reverse)
        {
            var motor = city.Player.Motor;
            var capsule = motor.GetComponent<CharacterController>();
            for (int waypoint = 0; waypoint < path.Count; waypoint++)
            {
                int index = reverse ? path.Count - 1 - waypoint : waypoint;
                Vector3 target = path[index] + Vector3.up * PlayerFactory.GroundedRootOffset;
                bool arrived = false;
                for (int step = 0; step < 350 && !arrived; step++)
                {
                    arrived = motor.MoveTowardsApproachWaypoint(target, .05f, .05f);
                    yield return null;
                    Assert.That(city.World.WalkableArea.Contains(motor.transform.position, capsule.radius), Is.True,
                        "Courtyard path must retain supported public access.");
                }
                Assert.That(arrived, Is.True, "Courtyard path blocked at " + index + " from " + motor.transform.position + " to " + target);
            }
            motor.CancelInteractionPoseMove();
        }

        private static IEnumerator PigeonLoftFrame(Camera camera, Vector3 eye, Vector3 target, string name)
        {
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
            camera.fieldOfView = 60f;
            yield return null;
            CaptureCurrentCamera(camera, "PigeonLoft", name);
        }
    }
}
