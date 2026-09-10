using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Port social life: paired talk, facing, greetings/farewells, clear routes, hero collisions and vessel light.")]
        public IEnumerator CityPortSocialLife()
        {
            Type setup = Type.GetType("BarPromenade.Editor.CityPortAssetSetup, BarPromenade.Editor");
            Assert.That(setup, Is.Not.Null);
            setup.GetMethod("ValidateOrThrow").Invoke(null, null);
            ValidatePortSocialLocalization();
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            GameSessionState.AdvanceGameTime(360f);
            CityGameRoot city = null;
            CityPortController port = null;
            CityPortCrew crew = null;
            CityPortConversationController speech = null;
            Transform listener = null;
            double held = CityPortCycle.CycleDurationSeconds - .001d;
            yield return Capture(SceneIds.City, () =>
            {
                city = Object.FindAnyObjectByType<CityGameRoot>();
                return city != null && city.IsInitialized ? city : null;
            }, () =>
            {
                port = Object.FindAnyObjectByType<CityPortController>();
                crew = Object.FindAnyObjectByType<CityPortCrew>();
                Assert.That(port, Is.Not.Null);
                Assert.That(crew, Is.Not.Null);
                speech = crew.GetComponent<CityPortConversationController>();
                Assert.That(speech, Is.Not.Null);
                if (city.Cannery != null)
                {
                    city.Cannery.AutoAdvance = false;
                    city.Cannery.ForcePresentation = true;
                }
                port.AutoAdvance = false;
                port.ForcePresentation = true;
                crew.UseManualClock = true;
                listener = new GameObject("Port Social Capture Listener").transform;
                listener.SetParent(port.transform, false);
                listener.position = port.Plan.World(new Vector3(-10f, 3f, -5f));
                speech.Initialize(port, crew, Camera.main, listener);
                SamplePortSocial(port, crew, speech, held, 0d);
                city.Player.Motor.SetInputEnabled(false);
                return new[] { Shot.At("port-social-00-rest-group",
                    port.Plan.World(new Vector3(-16f, 3.22f, -10f)),
                    port.Plan.World(new Vector3(-11.5f, 2.5f, -5.6f)), 60f) };
            });

            Camera camera = Camera.main;
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            bool followWasEnabled = follow != null && follow.enabled;
            if (follow != null) follow.enabled = false;
            foreach (Renderer renderer in city.Player.GameObject.GetComponentsInChildren<Renderer>())
                renderer.enabled = false;
            try
            {
                Assert.That(crew.WorkerCount, Is.EqualTo(5));
                var initialHead = new Quaternion[3];
                var headMoved = new bool[3];
                for (int role = 2; role < 5; role++)
                {
                    Assert.That(crew.IsRoleResting(role), Is.True, "Long truck waits free every shore role.");
                    Assert.That(Vector3.Distance(crew.GetWorker(role).transform.position, crew.RestPosition(role)), Is.LessThan(.02f));
                    initialHead[role - 2] = crew.GetWorker(role).Head.rotation;
                }
                var restVariants = new HashSet<int>();
                int restReplies = 0, lastSerial = -1, smoker = -1, smokeFrame = 0;
                double life = 0d;
                int initialBursts = 0;
                // This is the concrete regression: logistics remains clamped
                // at the end of the visit while the crew's life keeps advancing.
                for (int sample = 1; sample <= 700; sample++)
                {
                    life = sample * .2d;
                    SamplePortSocial(port, crew, speech, held, life);
                    Assert.That(port.ElapsedSeconds, Is.EqualTo(held));
                    for (int role = 2; role < 5; role++)
                    {
                        Assert.That(crew.IsRoleResting(role), Is.True);
                        headMoved[role - 2] |= Quaternion.Angle(initialHead[role - 2], crew.GetWorker(role).Head.rotation) > .5f;
                        if (smoker < 0 && crew.GetGesture(role).IsSmoking)
                        {
                            smoker = role;
                            initialBursts = crew.GetGesture(role).SmokeEffect.ManualBurstCount;
                        }
                    }
                    var turn = speech.Schedule.Current;
                    if (turn.IsSpeaking && turn.LineSerial != lastSerial)
                    {
                        lastSerial = turn.LineSerial;
                        Assert.That(turn.Exchange.Kind, Is.EqualTo(CityPortConversationKind.Rest));
                        restVariants.Add(turn.Exchange.Variant);
                        if (turn.SpeakerRole == turn.Exchange.SecondRole) restReplies++;
                    }
                    if (smoker >= 0 && smokeFrame < 3)
                    {
                        var gesture = crew.GetGesture(smoker);
                        double elapsed = gesture.SmokeElapsedSeconds;
                        double threshold = smokeFrame == 0 ? 1.6d : smokeFrame == 1 ? 2.8d : 5.2d;
                        if (elapsed >= threshold)
                        {
                            Assert.That(gesture.IsSmoking, Is.True);
                            if (smokeFrame == 1)
                            {
                                Assert.That(gesture.MouthBusy, Is.True);
                                var actor = crew.GetWorker(smoker);
                                Transform elbow = CityPedestrianHandProps.FindSocket(actor.ModelRoot, "forearm.R");
                                Assert.That(elbow, Is.Not.Null);
                                Assert.That(Vector3.Dot(gesture.MouthPosition - elbow.position, actor.transform.up),
                                    Is.GreaterThan(.15f), "A smoking elbow stays below the mouth, not across the face.");
                                Assert.That(Vector3.Distance(gesture.CigaretteButtPosition, gesture.MouthPosition),
                                    Is.LessThan(.025f), "The actual cigarette endpoint reaches the lips during the draw.");
                            }
                            if (smokeFrame == 2)
                            {
                                Assert.That(gesture.SmokeEffect.ManualBurstCount, Is.EqualTo(initialBursts + 1));
                                Assert.That(gesture.SmokeEffect.Particles.particleCount, Is.GreaterThan(0));
                            }
                            Vector3 head = crew.GetWorker(smoker).Head.position;
                            yield return CapturePortSocialPose(camera, "port-social-01-smoke-" + smokeFrame,
                                head + new Vector3(-2.6f, .25f, -2.3f), head - Vector3.up * .35f, 50f);
                            smokeFrame++;
                        }
                    }
                    if (sample % 50 == 0) yield return null;
                }
                Assert.That(smokeFrame, Is.EqualTo(3), "A complete lift/draw/exhale is visible during the held visit.");
                Assert.That(restVariants.Count, Is.GreaterThanOrEqualTo(2));
                Assert.That(restReplies, Is.GreaterThan(0), "A real partner answers the shore conversation.");
                foreach (bool moved in headMoved) Assert.That(moved, Is.True, "Every idle shore head remains alive.");
                Debug.Log("PORT SOCIAL: held logistics retains varied conversations, replies, idle and smoke.");

                var previous = new Vector3[3];
                var didReturn = new bool[3];
                for (int role = 2; role < 5; role++) previous[role - 2] = crew.GetWorker(role).transform.position;
                var firstWaveTime = new[] { -1d, -1d };
                var waveRole = new[] { -1, -1 };
                var waveFrames = new int[2];
                var greetingSpeakers = new HashSet<int>();
                var salutationPartners = new[] { -1, -1, -1, -1, -1 };
                bool workSpeech = false;
                int lastGreetingSerial = -1;
                double arrivalLife = life;
                double nextVisit = CityPortCycle.CycleDurationSeconds;
                double throughWork = CityPortCycle.UnloadStartSeconds + 73d;
                for (int sample = 0; sample <= (int)(throughWork * 5d); sample++)
                {
                    double elapsed = sample * .2d;
                    life = arrivalLife + .001d + elapsed;
                    SamplePortSocial(port, crew, speech, nextVisit + elapsed, life);
                    ValidatePortTareClearance(port, crew);
                    for (int role = 2; role < 5; role++)
                    {
                        Vector3 position = crew.GetWorker(role).transform.position;
                        if (elapsed <= CityPortCycle.ApproachDurationSeconds || role < 4 && elapsed <= CityPortCycle.UnloadStartSeconds)
                            Assert.That(Vector3.Distance(previous[role - 2], position), Is.LessThan(.5f),
                                "A returning worker must walk continuously, including the duty boundary: role " + role + " at " + elapsed);
                        didReturn[role - 2] |= crew.IsRoleReturning(role);
                        previous[role - 2] = position;
                        bool duty = role == 4 ? elapsed >= CityPortCycle.ApproachDurationSeconds : elapsed >= CityPortCycle.UnloadStartSeconds;
                        if (duty)
                        {
                            Assert.That(crew.IsRoleResting(role), Is.False);
                            Assert.That(crew.IsRoleReturning(role), Is.False, "Each worker arrives before its required task.");
                        }
                    }
                    var turn = speech.Schedule.Current;
                    if (turn.IsSpeaking && turn.Exchange.Kind == CityPortConversationKind.Greeting && turn.LineSerial != lastGreetingSerial)
                    {
                        greetingSpeakers.Add(turn.SpeakerRole);
                        salutationPartners[turn.Exchange.FirstRole] = turn.Exchange.SecondRole;
                        salutationPartners[turn.Exchange.SecondRole] = turn.Exchange.FirstRole;
                        lastGreetingSerial = turn.LineSerial;
                    }
                    workSpeech |= turn.IsSpeaking && turn.Exchange.Kind == CityPortConversationKind.Work;
                    int waving = 0;
                    for (int role = 0; role < 5; role++)
                    {
                        var gesture = crew.GetGesture(role);
                        if (!gesture.IsWaving) continue;
                        waving++;
                        int side = role >= 2 ? 0 : 1;
                        if (waveRole[side] < 0)
                        {
                            waveRole[side] = role;
                            firstWaveTime[side] = life;
                        }
                        if (waveRole[side] == role && waveFrames[side] < 2 &&
                            life - firstWaveTime[side] >= (waveFrames[side] == 0 ? .8d : 1.6d))
                        {
                            Assert.That(gesture.WaveWeight, Is.GreaterThan(.95f));
                            ValidatePortWaveClearance(crew.GetWorker(role), role);
                            if (waveFrames[side] == 1)
                                ValidatePortFacingPartner(crew, role, salutationPartners[role]);
                            Vector3 head = crew.GetWorker(role).Head.position;
                            yield return CapturePortSocialPose(camera,
                                "port-social-02-" + (side == 0 ? "shore" : "ship") + "-wave-" + waveFrames[side],
                                head + new Vector3(-3f, .3f, -3f), head - Vector3.up * .25f, 54f);
                            waveFrames[side]++;
                        }
                    }
                    Assert.That(waving, Is.LessThanOrEqualTo(2), "Greetings are staggered, not a five-person chorus.");
                    if (port.Snapshot.Stage == CityPortCycleStage.Unload)
                        Assert.That(crew.CraneHandsMatch, Is.True, "Speaking must preserve crane grip contacts at " + elapsed);
                    if (port.Snapshot.Stage == CityPortCycleStage.Approach)
                        Assert.That(crew.CaptainHandsMatch, Is.True, "The captain retains helm contacts while acknowledging the shore.");
                    if (sample % 50 == 0) yield return null;
                }
                foreach (bool returned in didReturn) Assert.That(returned, Is.True);
                Assert.That(greetingSpeakers.Count, Is.GreaterThanOrEqualTo(3));
                Assert.That(greetingSpeakers.Contains(0) || greetingSpeakers.Contains(1), Is.True);
                Assert.That(waveFrames[0], Is.EqualTo(2), "A shore greeting must include a visible hand wave.");
                Assert.That(waveFrames[1], Is.EqualTo(2), "A ship worker answers with a visible hand wave when free.");
                Assert.That(workSpeech, Is.True, "Shore and ship continue exchanging lines during unloading.");
                Debug.Log("PORT SOCIAL: continuous duty returns, staggered shore/ship greetings and working exchanges.");

                double departure = CityPortCycle.UnloadStartSeconds + CityPortCycle.UnloadDurationSeconds +
                    CityPortCycle.SecureDurationSeconds + CityPortCycle.UnmoorDurationSeconds;
                bool shoreFarewell = false, shipFarewell = false, farewellReply = false;
                bool[] goodbyeFrames = new bool[2];
                double[] goodbyeWaveStart = { -1d, -1d, -1d, -1d, -1d };
                int lastFarewell = -1;
                bool detourFrame = false;
                for (int sample = (int)(throughWork * 5d) + 1; sample <= (int)((departure + 27d) * 5d); sample++)
                {
                    double elapsed = sample * .2d;
                    life = arrivalLife + .001d + elapsed;
                    SamplePortSocial(port, crew, speech, nextVisit + elapsed, life);
                    ValidatePortTareClearance(port, crew);
                    if (!detourFrame && port.Snapshot.Stage == CityPortCycleStage.Secure && port.Snapshot.SecondsInStage >= 7d)
                    {
                        yield return CapturePortSocialPose(camera, "port-social-07-tare-detour",
                            port.Plan.World(new Vector3(1f, 3.6f, -10.5f)),
                            port.Plan.World(new Vector3(6.2f, 2.2f, -8.9f)), 62f);
                        detourFrame = true;
                    }
                    var turn = speech.Schedule.Current;
                    if (elapsed < departure)
                        Assert.That(turn.HasExchange && turn.Exchange.Kind == CityPortConversationKind.Farewell, Is.False,
                            "Goodbyes begin after the vessel actually casts off.");
                    if (turn.IsSpeaking && turn.Exchange.Kind == CityPortConversationKind.Farewell && turn.LineSerial != lastFarewell)
                    {
                        lastFarewell = turn.LineSerial;
                        shoreFarewell |= turn.SpeakerRole >= 2;
                        shipFarewell |= turn.SpeakerRole < 2;
                        farewellReply |= turn.SpeakerRole == turn.Exchange.SecondRole;
                        salutationPartners[turn.Exchange.FirstRole] = turn.Exchange.SecondRole;
                        salutationPartners[turn.Exchange.SecondRole] = turn.Exchange.FirstRole;
                    }
                    for (int role = 0; role < 5; role++)
                    {
                        if (elapsed < departure || !crew.GetGesture(role).IsWaving) continue;
                        if (goodbyeWaveStart[role] < 0d) goodbyeWaveStart[role] = life;
                        int side = role < 2 ? 1 : 0;
                        if (goodbyeFrames[side] || life - goodbyeWaveStart[role] < 1.7d) continue;
                        ValidatePortFacingPartner(crew, role, salutationPartners[role]);
                        Vector3 head = crew.GetWorker(role).Head.position;
                        yield return CapturePortSocialPose(camera, "port-social-06-goodbye-" + (side == 0 ? "shore" : "ship"),
                            head + new Vector3(-3f, .4f, -3f), head - Vector3.up * .25f, 54f);
                        goodbyeFrames[side] = true;
                    }
                    if (port.Snapshot.Stage == CityPortCycleStage.Unload)
                        Assert.That(crew.CraneHandsMatch, Is.True, "Body turns retain working hand contacts.");
                    if (port.Snapshot.Stage == CityPortCycleStage.Depart)
                        Assert.That(crew.CaptainHandsMatch, Is.True, "Farewells preserve steering contacts.");
                    if (sample % 50 == 0) yield return null;
                }
                Assert.That(shoreFarewell && shipFarewell && farewellReply, Is.True, "Departure has a complete reciprocal farewell.");
                Assert.That(goodbyeFrames[0] && goodbyeFrames[1], Is.True, "Shore and free ship crew wave toward their partner.");
                Assert.That(speech.Schedule.PendingFarewellCount, Is.Zero);
                // Entering an already departing scene, or returning from a
                // distance cull, cannot invent the missed departure event.
                SamplePortSocial(port, crew, speech, nextVisit + departure + 4d, life += 20d);
                for (int sample = 0; sample < 35; sample++)
                {
                    SamplePortSocial(port, crew, speech, nextVisit + departure + 4d + sample * .2d, life += .2d);
                    Assert.That(speech.Schedule.PendingFarewellCount, Is.Zero);
                    Assert.That(speech.Schedule.Current.HasExchange &&
                        speech.Schedule.Current.Exchange.Kind == CityPortConversationKind.Farewell, Is.False);
                }
                ValidatePortPhysicalBodies(city, port, crew);

                for (int crane = 0; crane < 2; crane++)
                {
                    double cargo = nextVisit + CityPortCycle.UnloadStartSeconds + crane * CityPortCycle.CargoDurationSeconds;
                    Transform lever = CityPortAssetProvider.FindPart(port.CraneBases[crane].gameObject, "LeverLeft");
                    Transform grip = CityPortAssetProvider.FindPart(port.CraneBases[crane].gameObject, "ANCHOR_ControlLeft");
                    Assert.That(grip.IsChildOf(lever), Is.True, "The grip must belong to the moving authored handle.");
                    SamplePortSocial(port, crew, speech, cargo + 6d, life += 3d);
                    Quaternion neutral = lever.localRotation;
                    Vector3 neutralGrip = grip.position;
                    SamplePortSocial(port, crew, speech, cargo + 9d, life += 3d);
                    Assert.That(Quaternion.Angle(neutral, lever.localRotation), Is.GreaterThan(10f));
                    Assert.That(Vector3.Distance(neutralGrip, grip.position), Is.GreaterThan(.025f));
                    Assert.That(crew.CraneHandsMatch, Is.True);
                    Assert.That(Vector3.Distance(crew.GetWorker(crane + 2).LeftGrip.position, grip.position), Is.LessThan(.075f));
                    var actor = crew.GetWorker(crane + 2);
                    Transform shoulderRight = CityPedestrianHandProps.FindSocket(actor.ModelRoot, "upper_arm.R");
                    Transform shoulderLeft = CityPedestrianHandProps.FindSocket(actor.ModelRoot, "upper_arm.L");
                    Vector3 bodyRight = Vector3.ProjectOnPlane(shoulderRight.position - shoulderLeft.position, actor.transform.up).normalized;
                    Vector3 bodyForward = Vector3.Cross(bodyRight, actor.transform.up).normalized;
                    Vector3 panel = (actor.RightGrip.position + actor.LeftGrip.position) * .5f;
                    Vector3 shoulders = (shoulderRight.position + shoulderLeft.position) * .5f;
                    Assert.That(Vector3.Dot(bodyForward, (panel - shoulders).normalized), Is.GreaterThan(.2f),
                        "Successful IK is insufficient: the operator must face the controls, not reach behind its back.");
                    ValidatePortVisibleFace(actor, panel);
                    Vector3 head = actor.Head.position;
                    yield return CapturePortSocialPose(camera, "port-social-03-crane-" + crane,
                        head + new Vector3(2.4f, .45f, .35f), grip.position + Vector3.up * .35f, 52f);
                }

                // A seek into an already working berth never invents its old arrival.
                double seek = 4d * CityPortCycle.CycleDurationSeconds + CityPortCycle.UnloadStartSeconds + 40d;
                SamplePortSocial(port, crew, speech, seek, life += 20d);
                Assert.That(speech.Schedule.PendingGreetingCount, Is.Zero);
                for (int sample = 0; sample < 60; sample++)
                {
                    SamplePortSocial(port, crew, speech, seek + sample * .2d, life += .2d);
                    Assert.That(speech.Schedule.Current.HasExchange &&
                        speech.Schedule.Current.Exchange.Kind == CityPortConversationKind.Greeting, Is.False);
                    for (int role = 0; role < 5; role++) Assert.That(crew.GetGesture(role).IsWaving, Is.False);
                }

                Light lamp = CityPortAssetProvider.FindPart(port.Vessel.gameObject, "Port Vessel Searchlight").GetComponent<Light>();
                Transform fixture = CityPortAssetProvider.FindPart(port.Vessel.gameObject, "ANCHOR_Searchlight");
                Assert.That(lamp, Is.Not.Null);
                Assert.That(lamp.type, Is.EqualTo(LightType.Spot));
                Assert.That(lamp.color.r, Is.GreaterThan(lamp.color.b));
                Assert.That(Vector3.Dot(lamp.transform.forward, port.Vessel.up), Is.LessThan(-.1f));
                Assert.That(Vector3.Distance(lamp.transform.position, fixture.position), Is.LessThan(.01f));
                Renderer beam = ValidatePortSearchlightBeam(port, lamp);
                var beamProperties = new MaterialPropertyBlock();
                double lampTime = nextVisit + CityPortCycle.UnloadStartSeconds + 16d;
                GameSessionState.AdvanceGameTime((float)(12d * 60d - GameSessionState.GameTimeOfDayMinutes));
                city.DayNight.ApplyCurrentTime(true);
                SamplePortSocial(port, crew, speech, lampTime, life += 3d);
                yield return null;
                float dayIntensity = lamp.intensity;
                beam.GetPropertyBlock(beamProperties);
                float dayBeamIntensity = beamProperties.GetFloat("_Intensity");
                Assert.That(dayBeamIntensity, Is.GreaterThan(0f), "The visible fog shaft survives daylight.");
                Assert.That(lamp.enabled && lamp.gameObject.activeInHierarchy, Is.True);
                yield return CapturePort(camera, city, port, crew, lampTime, "port-social-04-searchlight-day",
                    new Vector3(-17f, 7f, -5f), new Vector3(-2f, 3f, 2f));
                GameSessionState.AdvanceGameTime((float)(21d * 60d - GameSessionState.GameTimeOfDayMinutes));
                city.DayNight.ApplyCurrentTime(true);
                yield return null;
                Assert.That(dayIntensity, Is.GreaterThanOrEqualTo(lamp.intensity * GameTimeDayNightRules.DayFixtureFloor - .001f));
                beam.GetPropertyBlock(beamProperties);
                Assert.That(dayBeamIntensity, Is.GreaterThanOrEqualTo(
                    beamProperties.GetFloat("_Intensity") * GameTimeDayNightRules.DayFixtureFloor - .0001f));
                yield return CapturePort(camera, city, port, crew, lampTime, "port-social-05-searchlight-night",
                    new Vector3(-17f, 7f, -5f), new Vector3(-2f, 3f, 2f));
                Vector3 lampLocal = port.Vessel.InverseTransformPoint(lamp.transform.position);
                SamplePortSocial(port, crew, speech, nextVisit + 38d, life += 3d);
                Assert.That(Vector3.Distance(lamp.transform.position, fixture.position), Is.LessThan(.01f));
                Assert.That(Vector3.Distance(port.Vessel.InverseTransformPoint(lamp.transform.position), lampLocal), Is.LessThan(.001f));

                // Exercise the real life clock's pause contract, not just a repeated sample.
                crew.UseManualClock = false;
                yield return null;
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    yield return null;
                    double stopped = crew.LifeElapsedSeconds;
                    Quaternion head = crew.FirstCraneOperator.Head.rotation;
                    int lines = speech.Schedule.StartedLineCount;
                    yield return null;
                    yield return null;
                    Assert.That(crew.LifeElapsedSeconds, Is.EqualTo(stopped));
                    Assert.That(Quaternion.Angle(crew.FirstCraneOperator.Head.rotation, head), Is.LessThan(.001f));
                    Assert.That(speech.Schedule.StartedLineCount, Is.EqualTo(lines));
                }
                crew.UseManualClock = true;

                port.ForcePresentation = false;
                port.PresentationObserver = listener;
                listener.position = port.Plan.World(new Vector3(800f, 3f, -800f));
                port.RefreshPresentation();
                SamplePortSocial(port, crew, speech, lampTime, life += 3d);
                Assert.That(lamp.gameObject.activeInHierarchy, Is.False);
                Assert.That(beam.forceRenderingOff, Is.True,
                    "The authored shaft sleeps with the vessel's presentation gate.");
                for (int role = 0; role < 5; role++) Assert.That(speech.Bubbles.IsShowing(crew.GetWorker(role)), Is.False);
                listener.position = port.Plan.World(new Vector3(-10f, 3f, -5f));
                port.RefreshPresentation();
                SamplePortSocial(port, crew, speech, lampTime, life += .2d);
                Assert.That(speech.Schedule.PendingGreetingCount, Is.Zero);
                Assert.That(lamp.gameObject.activeInHierarchy, Is.True);
                Assert.That(beam.forceRenderingOff, Is.False);

                port.gameObject.SetActive(false);
                Assert.That(speech.Schedule.Current.HasExchange, Is.False);
                for (int role = 0; role < 5; role++)
                {
                    var gesture = crew.GetGesture(role);
                    Assert.That(gesture.IsSmoking || gesture.IsWaving, Is.False);
                    Assert.That(gesture.SmokeEffect.Particles.particleCount, Is.Zero);
                    Assert.That(speech.Bubbles.IsShowing(crew.GetWorker(role)), Is.False);
                }
                foreach (AudioSource source in speech.GetComponentsInChildren<AudioSource>(true))
                    Assert.That(source.isPlaying, Is.False);
                Debug.Log("CITY PORT SOCIAL LIFE ACCEPTANCE OK: independent paused life clock, finite bilingual exchanges, rest/smoke, continuous duty returns, staggered reciprocal greetings, live crane grips, vessel day/night light and teardown.");
            }
            finally
            {
                if (follow != null) follow.enabled = followWasEnabled;
                if (listener != null) Object.Destroy(listener.gameObject);
            }
        }

        [Test]
        public void CityPortDriverConversation_OnlyDockerAndObservedDeliveryWindows()
        {
            ValidatePortSocialLocalization();
            var pairBits = new HashSet<uint>();
            for (int first = 0; first < CityPortConversationCatalog.RoleCount; first++)
            for (int second = first + 1; second < CityPortConversationCatalog.RoleCount; second++)
                Assert.That(pairBits.Add(CityPortConversationCatalog.PairBit(first, second)), Is.True,
                    "The sixth role must not alias an existing speech pair.");
            foreach (CityPortConversationKind kind in Enum.GetValues(typeof(CityPortConversationKind)))
            for (int variant = 0; variant < CityPortConversationCatalog.Count(kind); variant++)
            {
                var entry = CityPortConversationCatalog.Get(kind, variant);
                if (!CityPortConversationCatalog.IncludesDriver(entry)) continue;
                Assert.That(kind, Is.Not.EqualTo(CityPortConversationKind.Rest),
                    "The visiting driver has no separate port break; ambient pairs must be selectable during loading.");
                CollectionAssert.AreEquivalent(new[] { CityPortConversationCatalog.DockerRole, CityPortConversationCatalog.DriverRole },
                    new[] { entry.FirstRole, entry.SecondRole });
            }

            const int driverBit = 1 << CityPortConversationCatalog.DriverRole;
            uint pair = CityPortConversationCatalog.PairBit(CityPortConversationCatalog.DriverRole, CityPortConversationCatalog.DockerRole);
            double portSeconds = CityPortCycle.CycleDurationSeconds - .001d;
            var snapshot = CityPortCycle.Sample(portSeconds);
            var schedule = new CityPortConversationSchedule(3197);
            int greetingRoles = 0, farewellRoles = 0, workRoles = 0;
            for (int sample = 0; sample <= 525; sample++)
            {
                double life = sample * .2d;
                // The docker is walking: available only to the driver's
                // short exchange, not to the old shore/ship conversations.
                var turn = schedule.Advance(life, portSeconds, snapshot, driverBit, driverBit, 0,
                    pair, pair, true, life >= 3.5d && life < 12d, life >= 90d && life < 98.5d, true);
                if (!turn.IsSpeaking) continue;
                Assert.That(CityPortConversationCatalog.IncludesDriver(turn.Exchange), Is.True);
                int bit = 1 << turn.SpeakerRole;
                if (turn.Exchange.Kind == CityPortConversationKind.Greeting) greetingRoles |= bit;
                if (turn.Exchange.Kind == CityPortConversationKind.Farewell) farewellRoles |= bit;
                if (turn.Exchange.Kind == CityPortConversationKind.Work) workRoles |= bit;
            }
            int both = driverBit | (1 << CityPortConversationCatalog.DockerRole);
            Assert.That(greetingRoles, Is.EqualTo(both));
            Assert.That(farewellRoles, Is.EqualTo(both));
            Assert.That(workRoles, Is.EqualTo(both));

            var ambientVariants = new HashSet<int>();
            var ambient = new CityPortConversationSchedule(3197);
            for (int sample = 0; sample < 7500; sample++)
            {
                var turn = ambient.Advance(sample * .2d, portSeconds, snapshot, driverBit, driverBit, 0,
                    pair, pair, true, false, false, true);
                if (turn.IsSpeaking) ambientVariants.Add(turn.Exchange.Variant);
            }
            int expectedDriverPairs = 0;
            for (int variant = 0; variant < CityPortConversationCatalog.WorkCount; variant++)
                if (CityPortConversationCatalog.IncludesDriver(CityPortConversationCatalog.Get(CityPortConversationKind.Work, variant)))
                    expectedDriverPairs++;
            Assert.That(ambientVariants.Count, Is.EqualTo(expectedDriverPairs), "Every driver work/conversation pair is reachable.");

            // Reconstruct inside a greeting window, then miss the next one
            // outside earshot. Neither may replay on returning to the port.
            for (int sample = 0; sample < 100; sample++)
            {
                double life = 500d + sample * .2d;
                bool greeting = sample < 40 || sample >= 50;
                int heardDriver = sample >= 50 && sample < 60 ? 0 : driverBit;
                var turn = schedule.Advance(life, portSeconds, snapshot, heardDriver, driverBit, 0,
                    pair, pair, true, greeting, false, true);
                Assert.That(turn.HasExchange && turn.Exchange.Kind == CityPortConversationKind.Greeting, Is.False);
            }
        }

        private static void SamplePortSocial(CityPortController port, CityPortCrew crew,
            CityPortConversationController speech, double portSeconds, double lifeSeconds)
        {
            port.ApplyAt(portSeconds, 15f);
            crew.ApplyAt(portSeconds, lifeSeconds);
            speech.ApplyAt();
        }

        private static IEnumerator CapturePortSocialPose(Camera camera, string name, Vector3 from, Vector3 target, float field)
        {
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            bool wasEnabled = follow != null && follow.enabled;
            if (follow != null) follow.enabled = false;
            try
            {
                camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from));
                camera.fieldOfView = field;
                Physics.SyncTransforms();
                for (int frame = 0; frame < 3; frame++) yield return null;
                Assert.That(Vector3.Distance(camera.transform.position, from), Is.LessThan(.001f));
                CaptureCurrentCamera(camera, SceneIds.City, name);
            }
            finally { if (follow != null) follow.enabled = wasEnabled; }
        }

        private static void ValidatePortVisibleFace(VillageResidentPresentation actor, Vector3 panel)
        {
            var face = CityPortAssetProvider.FindPart(actor.ModelRoot.gameObject, "GEO_FaceSurface").GetComponent<SkinnedMeshRenderer>();
            Assert.That(face, Is.Not.Null);
            var mesh = new Mesh();
            try
            {
                face.BakeMesh(mesh, true);
                Vector3 center = Vector3.zero;
                foreach (Vector3 vertex in mesh.vertices)
                    center += face.localToWorldMatrix.MultiplyPoint3x4(vertex);
                center /= mesh.vertexCount;
                Vector3 forward = Vector3.ProjectOnPlane(center - actor.Head.position, actor.transform.up).normalized;
                Vector3 towardPanel = Vector3.ProjectOnPlane(panel - actor.transform.position, actor.transform.up).normalized;
                Assert.That(Vector3.Dot(forward, towardPanel), Is.GreaterThan(.2f),
                    "The visible face surface, as well as the rig, must face toward the working panel.");
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        private static void ValidatePortFacingPartner(CityPortCrew crew, int role, int partner)
        {
            Assert.That(partner, Is.InRange(0, 4));
            var actor = crew.GetWorker(role);
            Transform right = CityPedestrianHandProps.FindSocket(actor.ModelRoot, "upper_arm.R");
            Transform left = CityPedestrianHandProps.FindSocket(actor.ModelRoot, "upper_arm.L");
            Vector3 forward = Vector3.Cross(right.position - left.position, actor.transform.up).normalized;
            Vector3 toward = Vector3.ProjectOnPlane(crew.GetWorker(partner).Head.position - actor.transform.position,
                actor.transform.up).normalized;
            Assert.That(Vector3.Dot(forward, toward), Is.GreaterThan(.90f),
                "The saluting worker turns the body toward its actual partner, not just the head: role " + role);
        }

        private static void ValidatePortWaveClearance(VillageResidentPresentation actor, int role)
        {
            Transform rightShoulder = CityPedestrianHandProps.FindSocket(actor.ModelRoot, "upper_arm.R");
            Transform leftShoulder = CityPedestrianHandProps.FindSocket(actor.ModelRoot, "upper_arm.L");
            Vector3 up = actor.transform.up;
            Vector3 right = Vector3.ProjectOnPlane(rightShoulder.position - leftShoulder.position, up).normalized;
            Vector3 hand = (role % 2 == 0 ? actor.RightGrip : actor.LeftGrip).position - actor.Head.position;
            float crown = float.NegativeInfinity, halfWidth = 0f;
            bool foundHead = false;
            // Renderer AABBs may still describe the previous render frame.
            // Use the same scaled bake + renderer matrix as VillageLife's
            // measured bodies: TransformPoint double-applies the FBX unit root.
            foreach (Renderer renderer in actor.GetComponentsInChildren<Renderer>(true))
            {
                bool head = renderer.name == "GEO_Head";
                if (!head && !renderer.name.StartsWith("HAIR_", StringComparison.Ordinal) &&
                    !renderer.name.StartsWith("CLO_Cap", StringComparison.Ordinal) &&
                    renderer.name != "CLO_KnitCap") continue;
                foundHead |= head;
                Mesh mesh = null;
                bool baked = renderer is SkinnedMeshRenderer;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    mesh = new Mesh();
                    skinned.BakeMesh(mesh, true);
                }
                else if (renderer.TryGetComponent<MeshFilter>(out var filter)) mesh = filter.sharedMesh;
                try
                {
                    if (mesh == null) continue;
                    foreach (Vector3 vertex in mesh.vertices)
                    {
                        Vector3 point = renderer.localToWorldMatrix.MultiplyPoint3x4(vertex) - actor.Head.position;
                        crown = Mathf.Max(crown, Vector3.Dot(point, up));
                        halfWidth = Mathf.Max(halfWidth, Mathf.Abs(Vector3.Dot(point, right)));
                    }
                }
                finally { if (baked && mesh != null) Object.DestroyImmediate(mesh); }
            }
            Assert.That(foundHead, Is.True);
            Assert.That(crown, Is.InRange(.06f, .5f), "The measured crown offset must be in world metres.");
            Assert.That(Vector3.Dot(hand, up), Is.GreaterThan(crown + .04f), "A full wave rises above the real head/cap crown.");
            Assert.That(Mathf.Abs(Vector3.Dot(hand, right)), Is.GreaterThan(halfWidth + .10f),
                "The waving hand stays beside the head rather than pressing against the temple.");
        }

        private static Renderer ValidatePortSearchlightBeam(CityPortController port, Light lamp)
        {
            Transform root = CityPortAssetProvider.FindPart(port.Vessel.gameObject, "SearchlightBeam");
            Renderer beam = root.GetComponent<Renderer>();
            Renderer glass = CityPortAssetProvider.FindPart(port.Vessel.gameObject, "SearchlightGlass").GetComponent<Renderer>();
            Assert.That(beam, Is.Not.Null);
            Assert.That(root.IsChildOf(port.Vessel), Is.True);
            Assert.That(beam.sharedMaterial, Is.SameAs(glass.sharedMaterial), "Lens and shaft share the existing fleet glow material.");
            Assert.That(beam.sharedMaterial.shader.name, Is.EqualTo("Bar Promenade/City Lighthouse Beam"));
            Assert.That(beam.shadowCastingMode, Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.Off));
            Assert.That(beam.receiveShadows, Is.False);
            var properties = new MaterialPropertyBlock();
            beam.GetPropertyBlock(properties);
            Assert.That(properties.GetFloat("_Uniform"), Is.Zero, "The shaft uses its authored axial fade.");
            Assert.That(properties.GetColor("_BeamColor").r, Is.GreaterThan(properties.GetColor("_BeamColor").b));
            Assert.That(properties.GetFloat("_FadeStartDistance"), Is.LessThan(properties.GetFloat("_FadeEndDistance")));
            glass.GetPropertyBlock(properties);
            Assert.That(properties.GetFloat("_Uniform"), Is.EqualTo(1f));

            Mesh mesh = root.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh, Is.Not.Null);
            Vector3[] vertices = mesh.vertices;
            Vector2[] uv = mesh.uv;
            Assert.That(uv.Length, Is.EqualTo(vertices.Length));
            float first = float.PositiveInfinity, last = float.NegativeInfinity;
            for (int index = 0; index < vertices.Length; index++)
            {
                float along = Vector3.Dot(root.TransformPoint(vertices[index]) - lamp.transform.position, lamp.transform.forward);
                Assert.That(uv[index].x, Is.InRange(-.001f, 1.001f));
                Assert.That(along, Is.EqualTo(uv[index].x * 22f).Within(.035f),
                    "Axial UV must track measured metres, not a palette tile.");
                first = Mathf.Min(first, along);
                last = Mathf.Max(last, along);
            }
            Assert.That(first, Is.EqualTo(0f).Within(.035f));
            Assert.That(last, Is.EqualTo(22f).Within(.035f));
            int[] triangles = mesh.triangles;
            for (int index = 0; index < triangles.Length; index += 3)
            {
                float a = uv[triangles[index]].x, b = uv[triangles[index + 1]].x, c = uv[triangles[index + 2]].x;
                Assert.That(Mathf.Max(a, Mathf.Max(b, c)) - Mathf.Min(a, Mathf.Min(b, c)), Is.GreaterThan(.01f),
                    "The soft shaft has open ends, not an opaque glowing cap.");
            }
            return beam;
        }

        [Serializable]
        private sealed class PortSocialLocale
        {
            public PortSocialLocaleEntry[] entries = Array.Empty<PortSocialLocaleEntry>();
        }

        [Serializable]
        private sealed class PortSocialLocaleEntry
        {
            public string key = string.Empty;
            public string value = string.Empty;
        }

        private static void ValidatePortConversationRounds()
        {
            const int allRoles = (1 << CityPortConversationCatalog.RoleCount) - 1;
            foreach (var kind in new[] { CityPortConversationKind.Rest, CityPortConversationKind.Work })
            {
                var schedule = new CityPortConversationSchedule(812);
                double life = 0;
                int count = CityPortConversationCatalog.Count(kind);
                var first = ReadPortConversationRound(schedule, kind, allRoles, count, ref life);
                var second = ReadPortConversationRound(schedule, kind, allRoles, count, ref life);
                Assert.That(new HashSet<int>(first).Count, Is.EqualTo(count), kind + " repeats before exhausting its pool.");
                Assert.That(new HashSet<int>(second).Count, Is.EqualTo(count));
                Assert.That(second[0], Is.Not.EqualTo(first[count - 1]), "No immediate repeat across rounds.");
                CollectionAssert.AreNotEqual(first, second, "A fresh round is randomized again.");
            }

            // A missing partner must defer unheard entries, not silently refill
            // just the available driver's lines over and over.
            var restricted = new CityPortConversationSchedule(1701);
            int driverPair = (1 << CityPortConversationCatalog.DriverRole) | (1 << CityPortConversationCatalog.DockerRole);
            int driverCount = 0;
            for (int variant = 0; variant < CityPortConversationCatalog.WorkCount; variant++)
                if (CityPortConversationCatalog.IncludesDriver(CityPortConversationCatalog.Get(CityPortConversationKind.Work, variant)))
                    driverCount++;
            double seconds = 0;
            var heard = ReadPortConversationRound(restricted, CityPortConversationKind.Work, driverPair, driverCount, ref seconds);
            restricted.Reset(); // Scene-clock reset preserves the spoken history.
            double held = CityPortCycle.UnloadStartSeconds + 46;
            for (int sample = 0; sample < 1200; sample++)
            {
                seconds += .25;
                var turn = restricted.Advance(seconds, held, CityPortCycle.Sample(held), driverPair, driverPair, 0,
                    uint.MaxValue, uint.MaxValue, true);
                Assert.That(turn.HasExchange, Is.False, "Unavailable workers do not reset the work round.");
            }
            heard.AddRange(ReadPortConversationRound(restricted, CityPortConversationKind.Work, allRoles,
                CityPortConversationCatalog.WorkCount - driverCount, ref seconds));
            Assert.That(new HashSet<int>(heard).Count, Is.EqualTo(CityPortConversationCatalog.WorkCount),
                "When partners return, the remaining unheard exchanges finish the same round.");
        }

        private static List<int> ReadPortConversationRound(CityPortConversationSchedule schedule,
            CityPortConversationKind kind, int available, int count, ref double life)
        {
            var heard = new List<int>();
            int previousSerial = schedule.StartedLineCount;
            double held = kind == CityPortConversationKind.Rest ? CityPortCycle.CycleDurationSeconds - .001 :
                CityPortCycle.UnloadStartSeconds + 46;
            int working = kind == CityPortConversationKind.Work ? available : 0;
            int resting = kind == CityPortConversationKind.Rest ? available : 0;
            for (int sample = 0; sample < count * 240 && heard.Count < count; sample++)
            {
                life += .25;
                var turn = schedule.Advance(life, held, CityPortCycle.Sample(held), available, working, resting,
                    uint.MaxValue, uint.MaxValue, true);
                if (!turn.IsSpeaking || turn.LineSerial == previousSerial) continue;
                previousSerial = turn.LineSerial;
                Assert.That(turn.Exchange.Kind, Is.EqualTo(kind));
                Assert.That(turn.LineKey, Is.EqualTo(turn.SpeakerRole == turn.Exchange.FirstRole ?
                    turn.Exchange.FirstKey : turn.Exchange.SecondKey), "Replies remain paired with their first line.");
                if (turn.SpeakerRole == turn.Exchange.FirstRole) heard.Add(turn.Exchange.Variant);
            }
            Assert.That(heard.Count, Is.EqualTo(count), "Eligible unheard exchanges must keep playing.");
            return heard;
        }

        private static void ValidatePortSocialLocalization()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                TextAsset asset = Resources.Load<TextAsset>("Localization/" + language);
                Assert.That(asset, Is.Not.Null);
                var catalog = JsonUtility.FromJson<PortSocialLocale>(asset.text);
                var lines = new Dictionary<string, string>();
                foreach (var entry in catalog.entries)
                    if (entry.key.StartsWith("city.port.", StringComparison.Ordinal))
                        Assert.That(lines.TryAdd(entry.key, entry.value), Is.True, "Duplicate localized port key.");
                Assert.That(lines.Count, Is.EqualTo(1 + 2 * (CityPortConversationCatalog.RestCount + CityPortConversationCatalog.WorkCount +
                    CityPortConversationCatalog.GreetingCount + CityPortConversationCatalog.FarewellCount)));
                Assert.That(lines[CityPortConversationController.AccessWaitLineKey],
                    Is.EqualTo(language == "ru" ? "Жду тебя, дружище" : "Waiting for you, buddy"));
                var ambientText = new HashSet<string>(StringComparer.Ordinal);
                foreach (CityPortConversationKind kind in Enum.GetValues(typeof(CityPortConversationKind)))
                for (int variant = 0; variant < CityPortConversationCatalog.Count(kind); variant++)
                {
                    var exchange = CityPortConversationCatalog.Get(kind, variant);
                    foreach (string key in new[] { exchange.FirstKey, exchange.SecondKey })
                    {
                        Assert.That(lines.ContainsKey(key), Is.True, language + ": " + key);
                        string value = lines[key];
                        if (kind == CityPortConversationKind.Rest || kind == CityPortConversationKind.Work)
                            Assert.That(ambientText.Add(value), Is.True, "Repeated authored phrase: " + key);
                        Assert.That(value.Length, Is.InRange(3, 120));
                        Assert.That(value.Contains("!") || value.Contains("(") || value.Contains(")"), Is.False, key);
                        Assert.That(Regex.Matches(value, "[.?!]").Count, Is.InRange(1, 2), key);
                    }
                    if (lines[exchange.FirstKey].Contains("?"))
                        Assert.That(lines[exchange.SecondKey].Contains("?"), Is.False, "A port question has an authored answer.");
                }
            }
        }
    }
}
