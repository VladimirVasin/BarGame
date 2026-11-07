using System.Collections;
using System.Collections.Generic;
using System.IO;
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
            Assert.That(GameSessionState.TryPrepareGameTimeAt(NewGameStartService.StoryStartMinuteOfDay), Is.True);
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
            var speech = keeper.GetComponent<CityPigeonKeeperSpeech>();
            Assert.That(speech, Is.Not.Null);
            Assert.That(speech.SilenceRemaining, Is.InRange(45f, 75f));
            keeper.AutoAdvance = false;
            // Leave the rig at the feeding shelf while the hero walks the stair approach.
            keeper.ApplyAt(360d / GameTimeState.GameMinutesPerRealSecond + 11d);
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
                if (renderer.name != "LoftStructure" && !renderer.name.StartsWith("Support", System.StringComparison.Ordinal)) continue;
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
                yield return VerifyPigeonKeeperDay(city, keeper, camera);
                keeper.AutoAdvance = true;
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
                keeper.ApplyAt(dawn + 24d + 11d * 25d / 33d);
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
                city.Player.Motor.Teleport(keeper.CommutePlan.HomeDoor);
                float nightSilence = speech.SilenceRemaining;
                speech.Advance(600f);
                Assert.That(speech.SilenceRemaining, Is.EqualTo(nightSilence));
                Assert.That(speech.IsSpeaking || keeper.IsCrying, Is.False);
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

        private static IEnumerator VerifyPigeonKeeperDay(CityGameRoot city, CityPigeonKeeper keeper, Camera camera)
        {
            var plan = keeper.CommutePlan;
            var courtyard = CityPigeonLoftPlan.ForLayout(city.Layout).Courtyard;
            var ground = CityHillsideGroundPlan.ForLayout(city.Layout);
            var stops = plan.DayStops;
            double frozenNoon = NewGameStartService.StoryStartMinuteOfDay / GameTimeState.GameMinutesPerRealSecond;
            Assert.That(GameSessionState.IsGameTimeRunning, Is.False);
            city.Player.Motor.Teleport(plan.GateOutsideStand + new Vector3(-2.0f, PlayerFactory.GroundedRootOffset, -2.4f));
            Physics.SyncTransforms();
            VerifyPigeonKeeperWorkRoutes(keeper, city.Player.GameObject.transform);
            Transform viceGrip = keeper.DayViceContact, lidHandle = keeper.DayGrainContact, bristles = keeper.DayBroomFoot;
            var fingers = keeper.Worker.GetComponent<NpcHandPose>();
            var thigh = CityPedestrianHandProps.FindSocket(keeper.Worker.ModelRoot, "thigh.L");
            Assert.That(viceGrip && lidHandle && bristles && fingers && thigh, Is.True);
            Vector3 viceRest = viceGrip.position, lidRest = lidHandle.position;
            Quaternion viceRestAngle = viceGrip.rotation, lidRestAngle = lidHandle.rotation;
            keeper.ApplyAt(frozenNoon);
            Assert.That(keeper.NextGriefAt - keeper.DayActivitySeconds, Is.InRange(119.9d, 180.1d));
            var visits = new int[stops.Count];
            int lastStop = -1;
            bool binHandled = false, viceTurned = false, bathCleaned = false, broomSwept = false;
            bool gaitMoved = false;
            bool speechChecked = false, griefChecked = false;
            var speech = keeper.GetComponent<CityPigeonKeeperSpeech>();
            Quaternion? walkingThigh = null;
            Vector3 firstStroke = default;
            bool foundStroke = false;
            const float tick = .10f;
            float upperRun = 0f, longestUpperRun = 0f;
            Assert.That(keeper.DayCycleSeconds, Is.GreaterThan(CityPigeonKeeper.CycleSeconds));
            var frames = Mathf.CeilToInt((keeper.DayCycleSeconds * 2f + 40f) / tick);
            for (int frame = 0; frame <= frames; frame++)
            {
                Vector3 before = keeper.Worker.transform.position;
                keeper.AdvanceAt(frozenNoon, tick);
                upperRun = keeper.Worker.transform.position.y >=
                    CityPigeonLoftPlan.ForLayout(city.Layout).Origin.y + 1.8f ? upperRun + tick : 0f;
                longestUpperRun = Mathf.Max(longestUpperRun, upperRun);
                float moved = Vector3.Distance(keeper.Worker.transform.position, before);
                if (moved > .19f) Assert.Fail("Keeper teleported " + moved + " m during " + keeper.CurrentDayTask);
                if (!griefChecked && keeper.IsCrying)
                {
                    yield return VerifyPigeonKeeperGrief(city, keeper, speech, camera, frozenNoon, tick);
                    griefChecked = true;
                }
                if (!gaitMoved && keeper.CurrentDayTask == CityPigeonKeeperDayTask.GrainStore &&
                    keeper.DayTaskTime < -.5f && moved > .085f)
                {
                    walkingThigh ??= thigh.localRotation;
                    gaitMoved = Quaternion.Angle(walkingThigh.Value, thigh.localRotation) > 6f;
                }
                for (int i = 0; i < stops.Count; i++)
                {
                    var stop = stops[i];
                    if (keeper.CurrentDayTask != stop.Task ||
                        Vector3.Distance(keeper.Worker.transform.position, stop.Stand) > .36f) continue;
                    if (lastStop != i) { visits[i]++; lastStop = i; }
                    if (keeper.DayTaskTime < 5f || keeper.DayTaskTime > stop.WorkSeconds - 5f) break;
                    var hand = fingers.CylinderCentre(false);
                    if (stop.Task == CityPigeonKeeperDayTask.GrainStore && !binHandled &&
                        (Quaternion.Angle(lidHandle.rotation, lidRestAngle) > 3f || Vector3.Distance(lidHandle.position, lidRest) > .025f) &&
                        Vector3.Distance(hand, lidHandle.position) < .035f)
                    {
                        binHandled = true;
                        yield return PigeonLoftFrame(camera, stop.Stand + new Vector3(-2.1f, 1.2f, -.8f),
                            lidHandle.position + Vector3.up * .16f, "08-keeper-grain-bin");
                        Assert.That(Vector3.Distance(fingers.CylinderCentre(false), lidHandle.position), Is.LessThan(.035f),
                            "The rendered frame must retain the lid contact after Unity evaluates animation.");
                    }
                    if (stop.Task == CityPigeonKeeperDayTask.Workbench && !viceTurned &&
                        (Vector3.Distance(viceGrip.position, viceRest) > .015f || Quaternion.Angle(viceGrip.rotation, viceRestAngle) > 4f) &&
                        Vector3.Distance(hand, viceGrip.position) < .035f)
                    {
                        viceTurned = true;
                        yield return PigeonLoftFrame(camera, stop.Stand + new Vector3(1.8f, 1.35f, -1.65f),
                            viceGrip.position + Vector3.up * .13f, "09-keeper-workbench");
                        Assert.That(Vector3.Distance(fingers.CylinderCentre(false), viceGrip.position), Is.LessThan(.035f));
                    }
                    if (!speechChecked && !keeper.IsCrying && stop.Task == CityPigeonKeeperDayTask.Workbench)
                    {
                        yield return VerifyPigeonKeeperSpeech(city, keeper, speech, camera);
                        speechChecked = true;
                    }
                    if (stop.Task == CityPigeonKeeperDayTask.Bath && !bathCleaned &&
                        Vector3.Distance(bristles.position, (stop.RightContact + stop.LeftContact) * .5f) < .25f)
                    {
                        bathCleaned = true;
                        yield return PigeonLoftFrame(camera, courtyard.Bath + new Vector3(1.8f, 1.1f, -.9f),
                            courtyard.Bath + Vector3.up * .53f, "10-keeper-cleaning-bath");
                        Transform broom = CityPedestrianHandProps.FindSocket(keeper.transform, "MOVE_Broom") ??
                            CityPedestrianHandProps.FindSocket(keeper.transform, "Broom");
                        Transform lower = CityPedestrianHandProps.FindSocket(broom, "BroomLowerGrip");
                        Assert.That(Vector3.Distance(fingers.CylinderCentre(false), broom.position), Is.LessThan(.035f));
                        Assert.That(Vector3.Distance(fingers.CylinderCentre(true), lower.position), Is.LessThan(.035f),
                            "Both rendered hands must stay around the broom shaft between simulation samples.");
                    }
                    if (stop.Task == CityPigeonKeeperDayTask.Sweep &&
                        Vector3.Distance(bristles.position, (stop.RightContact + stop.LeftContact) * .5f) < .65f &&
                        ground.TrySampleTop(new Vector2(bristles.position.x, bristles.position.z), out float soil) &&
                        Mathf.Abs(bristles.position.y - soil) < .04f)
                    {
                        if (foundStroke && Vector3.Distance(bristles.position, firstStroke) > .12f) broomSwept = true;
                        if (!foundStroke) { foundStroke = true; firstStroke = bristles.position; }
                    }
                    break;
                }
                if (frame == frames / 2)
                {
                    double elapsed = keeper.DayActivitySeconds;
                    Vector3 feet = keeper.Worker.transform.position, brush = bristles.position;
                    using (GameTimeScaleRuntime.AcquirePause()) keeper.AdvanceAt(frozenNoon, tick);
                    Assert.That(keeper.DayActivitySeconds, Is.EqualTo(elapsed));
                    Assert.That(keeper.Worker.transform.position, Is.EqualTo(feet));
                    Assert.That(bristles.position, Is.EqualTo(brush));
                }
            }
            for (int i = 0; i < stops.Count; i++)
                Assert.That(visits[i], Is.GreaterThanOrEqualTo(2), "Keeper must revisit " + stops[i].Task);
            Assert.That(longestUpperRun, Is.LessThan(80f),
                "Feeding, water and broom handling must form a short upper visit rather than a long stay.");
            Assert.That(gaitMoved, Is.True, "The walking rig must take strides at a frozen noon.");
            Assert.That(speechChecked && griefChecked, Is.True, "Observe both rare behaviors on the ordinary working circuit.");
            Assert.That(binHandled, Is.True, "Right hand must lift the actual bin lid.");
            Assert.That(viceTurned, Is.True, "Right hand must turn the actual vice handle.");
            Assert.That(bathCleaned, Is.True, "Bristles must reach the bath rim opposite the bird's foothold.");
            Assert.That(broomSwept, Is.True, "Bristles must move across the actual yard soil.");
            Assert.That(GameSessionState.GameTimeOfDayMinutes, Is.EqualTo(NewGameStartService.StoryStartMinuteOfDay));
            Vector3 beforeStart = keeper.Worker.transform.position;
            double localElapsed = keeper.DayActivitySeconds;
            Assert.That(GameSessionState.TryStartPreparedGameTime(), Is.True);
            GameSessionState.AdvanceGameTime(tick);
            keeper.AdvanceAt(frozenNoon + tick, tick);
            Assert.That(keeper.DayActivitySeconds - localElapsed, Is.EqualTo(tick).Within(.025d));
            Assert.That(Vector3.Distance(beforeStart, keeper.Worker.transform.position), Is.LessThan(.40f),
                "Starting the calendar must preserve visible work.");
        }

        private static IEnumerator VerifyPigeonKeeperSpeech(CityGameRoot city, CityPigeonKeeper keeper,
            CityPigeonKeeperSpeech speech, Camera camera)
        {
            var bubbles = city.SpeechBubbles;
            var actor = keeper.Worker;
            var player = city.Player.Motor;
            Vector3 savedHero = player.transform.position;
            Vector3 near = actor.transform.position - actor.transform.forward * 2f + Vector3.up * PlayerFactory.GroundedRootOffset;
            Vector3 far = keeper.CommutePlan.HomeDoor;
            var heard = new HashSet<string>();
            Assert.That(speech.Speaker.Anchor, Is.EqualTo(actor.Head));
            Assert.That(speech.Speaker.Owner, Is.EqualTo(actor));
            Assert.That(speech.Speaker.DesignId, Is.EqualTo(DefaultNpcPopulation.CityPigeonKeeper));
            Assert.That(speech.Speaker.VoiceOrdinal, Is.EqualTo(NpcVoiceCatalog.ResolveOrdinal(
                DefaultNpcPopulation.CityPigeonKeeper, RussianVocalizerGender.Male)));
            Assert.That(speech.Speaker.Earshot.IsAudible(Vector3.Distance(far, actor.Head.position)), Is.False);
            try
            {
                float remaining = speech.SilenceRemaining;
                player.Teleport(far);
                speech.Advance(600f);
                Assert.That(speech.SilenceRemaining, Is.EqualTo(remaining));
                player.Teleport(near);
                using (GameTimeScaleRuntime.AcquirePause()) speech.Advance(600f);
                Assert.That(speech.SilenceRemaining, Is.EqualTo(remaining));
                Assert.That(speech.IsSpeaking, Is.False);

                string previous = null;
                for (int line = 0; line <= 7; line++)
                {
                    speech.Advance(speech.SilenceRemaining - .25f);
                    Assert.That(speech.IsSpeaking, Is.False);
                    if (line == 0)
                    {
                        // Exercise rejection at the real head before the first card is spent.
                        Assert.That(bubbles.DeclareSpeaker(speech, actor.Head, speech.Speaker.DesignId,
                            speech.Speaker.Earshot), Is.True);
                        Assert.That(bubbles.Show(speech, LocalizationService.Get("city.pigeon_keeper.line.01")), Is.True);
                        speech.Advance(.3f);
                        Assert.That(speech.LastLineKey, Is.Empty);
                        Assert.That(bubbles.WithdrawSpeaker(speech), Is.True);
                    }
                    speech.Advance(.3f);
                    string key = speech.LastLineKey;
                    Assert.That(speech.IsSpeaking && bubbles.IsShowing(actor), Is.True, "Eligible work must admit the due line.");
                    Assert.That(key, Does.StartWith("city.pigeon_keeper.line."));
                    Assert.That(key, Is.Not.EqualTo(previous));
                    if (line < 7) Assert.That(heard.Add(key), Is.True, "A keeper line repeated before the pool was spent.");
                    else Assert.That(heard.Contains(key), Is.True);
                    previous = key;
                    Assert.That(speech.CanStartGrief, Is.False);
                    Assert.That(speech.SilenceRemaining, Is.InRange(120f, 180f));
                    Assert.That(bubbles.TryGetSpeechFaceSample(actor, out var sample), Is.True);
                    Assert.That(sample.Text, Is.EqualTo(LocalizationService.Get(key)));
                    if (line == 0)
                    {
                        Vector3 head = actor.Head.position;
                        Vector3 eye = head + actor.transform.right * 2.1f + actor.transform.forward * .65f;
                        camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(head - eye));
                        float deadline = Time.realtimeSinceStartup + 6f;
                        while (speech.IsSpeaking && Time.realtimeSinceStartup < deadline)
                        {
                            if (bubbles.TryGetSpeechFaceSample(actor, out sample) &&
                                sample.HasPhoneticTiming && sample.RevealedCharacters > 2) break;
                            yield return null;
                        }
                        Assert.That(sample.HasPhoneticTiming && sample.RevealedCharacters > 2, Is.True,
                            "The shared Russian vocalizer must drive the real head caption.");
                        yield return PigeonLoftFrame(camera, eye, head, "12-keeper-speaking");
                        if (!Application.isBatchMode && bubbles.HasRenderedLayout)
                        {
                            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures",
                                "PigeonLoft", "12-keeper-speaking-game-view.png"));
                            ScreenCapture.CaptureScreenshot(path);
                            yield return null;
                        }
                    }
                    int leases = NpcSpeechVoice.Instance.LeasedCount;
                    // Release the exact speaker while the line is active, including
                    // a queued synthesis job on the immediately admitted later lines.
                    player.Teleport(far);
                    speech.Advance(.01f);
                    Assert.That(bubbles.IsDeclared(actor) || bubbles.IsShowing(actor), Is.False);
                    if (line > 0) Assert.That(NpcSpeechVoice.Instance.LeasedCount, Is.EqualTo(leases - 1));
                    speech.Advance(19f);
                    Assert.That(speech.CanStartGrief, Is.False);
                    speech.Advance(1.1f);
                    Assert.That(speech.CanStartGrief, Is.True);
                    player.Teleport(near);
                }
            }
            finally
            {
                bubbles.WithdrawSpeaker(speech);
                player.Teleport(savedHero);
                Physics.SyncTransforms();
            }
        }

        private static IEnumerator VerifyPigeonKeeperGrief(CityGameRoot city, CityPigeonKeeper keeper,
            CityPigeonKeeperSpeech speech, Camera camera, double noon, float tick)
        {
            var actor = keeper.Worker;
            var rightHand = CityPedestrianHandProps.FindSocket(actor.ModelRoot, "hand.R");
            var shoulder = CityPedestrianHandProps.FindSocket(actor.ModelRoot, "clavicle.R");
            var leftFoot = CityPedestrianHandProps.FindSocket(actor.ModelRoot, "foot.L");
            var rightFoot = CityPedestrianHandProps.FindSocket(actor.ModelRoot, "foot.R");
            Assert.That(rightHand && shoulder && leftFoot && rightFoot, Is.True);
            var fingers = actor.GetComponent<NpcHandPose>();
            var handFrame = fingers.GetAnatomicalFrame(false);
            Vector3 root = actor.transform.position, footL = leftFoot.position, footR = rightFoot.position;
            Quaternion headAtStart = actor.Head.rotation;
            Quaternion startShoulder = shoulder.localRotation;
            float phase = keeper.DayPhase, biggestShoulderMotion = 0f;
            var task = keeper.CurrentDayTask;
            Assert.That(task.HasValue, Is.True);
            Assert.That(keeper.CryDuration, Is.InRange(10f, 14f));
            Assert.That(keeper.IsAtHome || keeper.IsCommuting || speech.IsSpeaking, Is.False);
            Vector3 savedHero = city.Player.Motor.transform.position;
            city.Player.Motor.Teleport(root - actor.transform.forward * 1.8f + Vector3.up * PlayerFactory.GroundedRootOffset);
            speech.Advance(speech.SilenceRemaining + 1f);
            Assert.That(speech.SilenceRemaining, Is.Zero);
            Assert.That(speech.IsSpeaking, Is.False, "A due utterance must stay silent during the private gesture.");
            bool contact = false;
            for (int i = 0; keeper.IsCrying && i < 160; i++)
            {
                speech.Advance(tick);
                Assert.That(speech.IsSpeaking, Is.False);
                Assert.That(keeper.CurrentDayTask, Is.EqualTo(task));
                Assert.That(keeper.DayPhase, Is.EqualTo(phase).Within(.025f));
                Assert.That(Vector3.Distance(actor.transform.position, root), Is.LessThan(.01f));
                Assert.That(Vector3.Distance(leftFoot.position, footL), Is.LessThan(.075f));
                Assert.That(Vector3.Distance(rightFoot.position, footR), Is.LessThan(.075f));
                biggestShoulderMotion = Mathf.Max(biggestShoulderMotion,
                    Quaternion.Angle(shoulder.localRotation, startShoulder));
                if (!contact && keeper.CryWeight >= .999f && keeper.CryElapsed > 3f)
                {
                    Vector3 palm = rightHand.position + rightHand.rotation * handFrame.PalmCentreInHand;
                    Assert.That(Vector3.Distance(palm, keeper.CryFaceTarget), Is.LessThan(.045f));
                    Assert.That(Vector3.Distance(palm, actor.Head.position), Is.LessThan(.21f));
                    Assert.That(Quaternion.Angle(headAtStart, actor.Head.rotation), Is.GreaterThan(10f));
                    float held = keeper.CryElapsed;
                    using (GameTimeScaleRuntime.AcquirePause())
                    {
                        keeper.AdvanceAt(noon, tick);
                        speech.Advance(60f);
                        Assert.That(keeper.CryElapsed, Is.EqualTo(held));
                        Assert.That(rightHand.position + rightHand.rotation * handFrame.PalmCentreInHand, Is.EqualTo(palm));
                    }
                    contact = true;
                    // Stay inside the grille when he pauses at its welds.
                    Vector3 eye = actor.Head.position + actor.transform.forward * .18f + actor.transform.right * 1.25f;
                    yield return PigeonLoftFrame(camera, eye, actor.Head.position - Vector3.up * .12f, "13-keeper-wiping-face");
                    Assert.That(Vector3.Distance(rightHand.position + rightHand.rotation * handFrame.PalmCentreInHand,
                        keeper.CryFaceTarget), Is.LessThan(.045f), "The face contact must survive a rendered frame.");
                    using (GameTimeScaleRuntime.AcquirePause())
                    {
                        yield return null;
                        Assert.That(Vector3.Distance(rightHand.position + rightHand.rotation * handFrame.PalmCentreInHand,
                            keeper.CryFaceTarget), Is.LessThan(.045f), "Pause must retain the actual hand pose.");
                    }
                }
                keeper.AdvanceAt(noon, tick);
            }
            Assert.That(contact && !keeper.IsCrying, Is.True);
            Assert.That(biggestShoulderMotion, Is.GreaterThan(.5f), "His shoulder moves while his feet stay planted.");
            Assert.That(keeper.NextGriefAt - keeper.LastCryActivityTime, Is.InRange(239.9d, 360.1d));
            speech.Advance(19f);
            Assert.That(speech.IsSpeaking, Is.False, "The keeper must not fill the silence immediately after wiping his face.");
            city.Player.Motor.Teleport(savedHero);
            Physics.SyncTransforms();
            keeper.AdvanceAt(noon, tick);
            Assert.That(keeper.CurrentDayTask, Is.EqualTo(task));
            Assert.That(keeper.DayPhase, Is.GreaterThan(phase));
        }

        private static void VerifyPigeonKeeperWorkRoutes(CityPigeonKeeper keeper, Transform hero)
        {
            var plan = keeper.CommutePlan;
            Assert.That(plan.DayStops, Has.Count.EqualTo(7));
            Vector3 previous = plan.WorkToGate[0];
            var obstructions = new System.Collections.Generic.List<string>();
            foreach (var stop in plan.DayStops)
            {
                Assert.That(stop.Route[0], Is.EqualTo(previous), "Discontinuous leg to " + stop.Task);
                previous = stop.Stand;
                for (int i = 1; i < stop.Route.Count; i++)
                {
                    Vector3 a = stop.Route[i - 1], b = stop.Route[i], span = b - a;
                    if (span.sqrMagnitude < .0001f) continue;
                    // Clear the 16.7 cm tread rise under the feet. Static poles,
                    // walls, bench, stored grain and the low deck remain real obstructions.
                    foreach (var hit in Physics.CapsuleCastAll(a + Vector3.up * .41f, a + Vector3.up * 1.49f,
                        .19f, span.normalized, span.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    {
                        if (hit.transform.IsChildOf(keeper.Worker.transform) || hit.transform.IsChildOf(hero)) continue;
                        obstructions.Add("Keeper route to " + stop.Task + " crosses " + hit.collider.name + " at " + hit.point +
                            " on " + a + " -> " + b);
                    }
                }
            }
            Assert.That(previous, Is.EqualTo(plan.WorkToGate[0]));
            Assert.That(obstructions, Is.Empty, string.Join("\n", obstructions));
            // Both yard and commute use these foot stations. Test the imported
            // tread surfaces themselves, not the authored points alone.
            for (int i = 0; i < 12; i++)
            {
                Vector3 foot = plan.WorkToGate[i + 2];
                bool supported = false;
                foreach (var hit in Physics.RaycastAll(foot + Vector3.up * .06f, Vector3.down, .10f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    if (hit.collider.name == "LoftStructure" && Mathf.Abs(hit.point.y - foot.y) < .015f) supported = true;
                Assert.That(supported, Is.True, "Keeper route misses physical stair tread at " + foot);
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
            double approach = departure - keeper.NominalDayCycleSeconds * .75d;
            keeper.ApplyAt(approach);
            bool gateFrame = false, stairsFrame = false;
            float upperRun = 0f, longestUpperRun = 0f;
            Vector3 previous = keeper.Worker.transform.position;
            for (int tick = 0; tick <= Mathf.CeilToInt((float)(departure - approach + keeper.CommuteSeconds + 35d) / .05f); tick++)
            {
                double clock = approach + tick * .05d;
                keeper.AdvanceAt(clock);
                Vector3 feet = keeper.Worker.transform.position;
                upperRun = feet.y >= CityPigeonLoftPlan.ForLayout(city.Layout).Origin.y + 1.8f ? upperRun + .05f : 0f;
                longestUpperRun = Mathf.Max(longestUpperRun, upperRun);
                Assert.That(Vector3.Distance(previous, feet), Is.LessThan(.16f), "Keeper commute must remain continuous at " + tick);
                previous = feet;
                if (!stairsFrame && clock >= departure && keeper.IsCommuting &&
                    feet.y < CityPigeonLoftPlan.ForLayout(city.Layout).Origin.y + 1.4f &&
                    clock - departure < keeper.GateApproachSeconds * .55f + 35d)
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
            Assert.That(longestUpperRun, Is.LessThan(80f), "The final evening cycle must fill the gap in the yard, not wait upstairs.");
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
            var texture = new RenderTexture(Width, Height, 24);
            var pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            RenderTexture previous = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                // Observe the ordinary render after LateUpdate and skinning;
                // a nested Camera.Render can read the animator's earlier stream.
                camera.targetTexture = texture;
                yield return null;
                yield return null;
                RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                pixels.Apply();
                Assert.That(IsBlank(pixels), Is.False);
                string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "PigeonLoft");
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previous;
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(pixels);
                texture.Release();
                Object.DestroyImmediate(texture);
            }
        }
    }
}
