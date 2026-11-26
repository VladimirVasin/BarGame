using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// Photographs the available library designs on their STREET pair - the
    /// shared citizen gait - through the production presentation: the
    /// line-up waiting (street idle), then walking at four phases of the
    /// hero's cycle, from the front, from three-quarters and from the
    /// side. The numbers in <c>CityPedestrianStreetGaitTests</c> prove the
    /// arms hang and swing; a frame here shows whether the walk reads as
    /// a person walking.
    ///
    /// `[Explicit]`: a capture, not a regression. Frames land in
    /// `Captures/PedestrianGait/` (gitignored). Look at them.
    /// </summary>
    public sealed class CityPedestrianGaitCapturePlayModeTests
    {
        private const int Width = 960;
        private const int Height = 720;
        private const float Spacing = 1.3f;

        /// <summary>Well above any loaded world; the camera's 30 m far
        /// plane never reaches back down to it.</summary>
        private static readonly Vector3 StageOrigin = new Vector3(0f, 4000f, 0f);

        private static readonly string[] RoamingPrefabPaths =
        {
            CityPedestrianResources.BabushkaPrefabResourcePath,
            CityPedestrianResources.WeighAttendantPrefabResourcePath,
            CityPedestrianResources.WatchmanPrefabResourcePath,
            CityPedestrianResources.MournerPrefabResourcePath
        };

        [UnityTest]
        public IEnumerator DefaultWalkers_MatchTravelAndExtendOverEachSupportBoot()
        {
            var stage = new GameObject("Ordinary pedestrian gait stage");
            stage.transform.position = StageOrigin;
            var cameraObject = new GameObject("Ordinary pedestrian gait camera");
            var lightObject = new GameObject("Ordinary pedestrian gait light");
            var target = new RenderTexture(Width, Height, 24);
            var pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            bool previousFog = RenderSettings.fog;
            AmbientMode previousMode = RenderSettings.ambientMode;
            Color previousAmbient = RenderSettings.ambientLight;
            var walkers = new CityPedestrianPresentation[2];
            try
            {
                RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.53f, .54f, .58f);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.4f;
                light.transform.rotation = Quaternion.Euler(44f, -35f, 0f);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.13f, .15f, .17f);
                camera.targetTexture = target;
                camera.farClipPlane = 20f;

                GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.transform.SetParent(stage.transform, false);
                Physics.SyncTransforms();

                for (int index = 0; index < walkers.Length; index++)
                {
                    int resident = index == 0 ? 0 : 14;
                    CityPedestrianAssetRegistry registry = CityPedestrianDefaultNpcBody.Create(stage.transform,
                        DefaultNpcPopulation.PedestrianId(resident), null);
                    registry.transform.localPosition = Vector3.right * ((index - .5f) * Spacing);
                    registry.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                    walkers[index] = registry.gameObject.AddComponent<CityPedestrianPresentation>();
                    CityPedestrianPresentation walker = walkers[index];
                    walker.Initialize(registry);
                    registry.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    Assert.That(registry.WalkClip.name, Is.EqualTo(index == 0 ? "OrdinaryWorkerWalk" : "OrdinaryWomanWalk"));
                    Assert.That(registry.WalkClip.length, Is.EqualTo(1f).Within(.01f));
                    AssertCadenceThroughEvaluationThrottle(walker);
                }

                float[] phases = { 0f, .125f, .25f, .375f, .5f, .75f };
                Vector3 look = StageOrigin + Vector3.up * .9f;
                Vector3 eye = look + new Vector3(0f, .19f, -3.55f);
                string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "OrdinaryNpcWalk");
                Directory.CreateDirectory(folder);
                for (int index = 0; index < phases.Length; index++)
                {
                    float phase = phases[index];
                    foreach (CityPedestrianPresentation walker in walkers)
                    {
                        walker.SetMoving(true);
                        walker.ConfigureCycle(1f, phase);
                        walker.Advance(0f, true, true);
                        if (phase == .25f || phase == .75f)
                            AssertSupportKnee(phase < .5f ? walker.Registry.LeftFoot : walker.Registry.RightFoot);
                    }
                    yield return null;
                    Shoot(camera, pixels, target, Path.Combine(folder, $"phase-{index}.png"), eye, look, 39f);
                }
            }
            finally
            {
                foreach (CityPedestrianPresentation walker in walkers) walker?.Shutdown();
                RenderSettings.fog = previousFog;
                RenderSettings.ambientMode = previousMode;
                RenderSettings.ambientLight = previousAmbient;
                Object.Destroy(stage);
                Object.Destroy(lightObject);
                Object.Destroy(cameraObject);
                Object.Destroy(pixels);
                target.Release();
                Object.Destroy(target);
            }
        }

        [UnityTest]
        public IEnumerator CityStreetGreetings_WalkersWaveInTurnWithoutBreakingStride()
        {
            var stage = new GameObject("Walking greeting");
            stage.transform.position = StageOrigin;
            var camera = new GameObject("Witness").AddComponent<Camera>();
            camera.transform.SetParent(stage.transform, false);
            camera.transform.localPosition = new Vector3(-.6f, 1.5f, -4.6f);
            camera.transform.LookAt(StageOrigin + Vector3.up);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.gray;
            camera.enabled = false;
            var light = new GameObject("Street light").AddComponent<Light>();
            light.transform.SetParent(stage.transform, false);
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            bool fog = RenderSettings.fog;
            var actors = new CityPedestrianActor[2];
            var bodies = new CityPedestrianPresentation[2];
            var wrists = new Transform[2];
            var greetings = new CityStreetGreetings(2, i => bodies[i], 43);
            try
            {
                RenderSettings.fog = false;
                var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.transform.SetParent(stage.transform, false);
                for (int i = 0; i < 2; i++)
                {
                    var body = CityPedestrianDefaultNpcBody.Create(stage.transform,
                        DefaultNpcPopulation.PedestrianId(i == 0 ? 0 : 14), null);
                    var start = StageOrigin + new Vector3(i == 0 ? -2.4f : 2.4f, 0f, i == 0 ? .48f : -.48f);
                    var direction = i == 0 ? Vector3.right : Vector3.left;
                    var anchor = new CityPedestrianSpawnAnchor("start", start, 0, 1);
                    var plan = new CityPedestrianPlan(7, 11, 3u, CityPedestrianPlanner.AgentRadius,
                        new[] { new CityPedestrianNode("a", start - direction * 5f, false),
                            new CityPedestrianNode("b", start + direction * 8f, false) },
                        new[] { new CityPedestrianLink("walk", 0, 1, CityPedestrianLinkKind.Sidewalk) },
                        new[] { anchor }, new[] { new Rect(-10f, -10f, 20f, 20f) });
                    var actor = new GameObject("Street walker").AddComponent<CityPedestrianActor>();
                    actors[i] = actor;
                    actor.transform.SetParent(stage.transform, false);
                    actor.gameObject.layer = CityPedestrianCollision.LayerIndex;
                    actor.Initialize(new LevelStreet(), CityPedestrianPlanner.AgentRadius);
                    actor.PrepareSpawn(plan, anchor, 1, .82f, 1f, i * .3f, 0, 31u);
                    bodies[i] = body.gameObject.AddComponent<CityPedestrianPresentation>();
                    bodies[i].Initialize(body);
                    actor.BindPresentation(bodies[i]);
                    body.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    wrists[i] = CityPedestrianHandProps.FindSocket(body.ModelRoot, "hand.R");
                }
                Physics.SyncTransforms();
                yield return null;

                // A missed attempt also waits a new interval.
                greetings.Advance(60f, StageOrigin + Vector3.right * 80f, camera);
                greetings.Advance(29.9f, StageOrigin, camera);
                Assert.That(bodies[0].GreetingPartner, Is.Null);
                greetings.Advance(30.1f, StageOrigin, camera);
                Assert.That(bodies[0].GreetingPartner, Is.SameAs(bodies[1]));
                int firstRaised = -1;
                bool replied = false;
                const float step = 1f / 30f;
                for (int frame = 0; frame < 80; frame++)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        float phase = bodies[i].WalkCycle;
                        actors[i].Advance(step);
                        float forwardStep = actors[i].LastDisplacement.x * (i == 0 ? 1f : -1f);
                        Assert.That(forwardStep, Is.EqualTo(.82f * step).Within(.003f));
                        Assert.That(bodies[i].WalkCycle, Is.GreaterThan(phase));
                    }
                    Vector3 leftSole = bodies[0].Registry.LeftFoot.position;
                    Vector3 rightSole = bodies[1].Registry.RightFoot.position;
                    greetings.Advance(step, StageOrigin, camera);
                    Assert.That(bodies[0].Registry.LeftFoot.position, Is.EqualTo(leftSole));
                    Assert.That(bodies[1].Registry.RightFoot.position, Is.EqualTo(rightSole));
                    bool a = Raised(0), b = Raised(1);
                    Assert.That(a && b, Is.False, "The reply must follow the first hand.");
                    if (!(a || b)) continue;
                    int raised = a ? 0 : 1;
                    if (firstRaised < 0)
                    {
                        firstRaised = raised;
                    }
                    else if (raised != firstRaised)
                    {
                        replied = true;
                    }
                    if (frame == 13 || frame == 51)
                    {
                        // Skinned renderers cache a camera's palette within a rendered frame.
                        yield return null;
                        AreaCaptureFixture.CaptureCurrentCamera(camera, "CityStreetGreetings", frame == 13 ? "wave" : "reply", 960, 640);
                    }
                }
                Assert.That(replied, Is.True, "Both wrists must reach head height on separate turns.");
                Assert.That(bodies[0].GreetingPartner, Is.Null);
                Assert.That(Raised(0) || Raised(1), Is.False);

                greetings.Advance(60f, StageOrigin, camera);
                Assert.That(bodies[0].GreetingPartner, Is.SameAs(bodies[1]));
                bodies[0].gameObject.SetActive(false);
                bodies[0].gameObject.SetActive(true);
                bodies[0].SetMoving(true);
                greetings.Advance(step, StageOrigin, camera);
                Assert.That(bodies[1].GreetingPartner, Is.Null, "A recycled walker cannot receive the delayed reply.");
            }
            finally
            {
                greetings.Cancel();
                foreach (var body in bodies) if (body != null) body.Shutdown();
                RenderSettings.fog = fog;
                Object.Destroy(stage);
            }

            bool Raised(int i) => wrists[i].position.y > bodies[i].Registry.HeadAnchor.position.y - .20f;
        }

        private sealed class LevelStreet : IWalkableArea
        {
            public bool Contains(Vector3 position, float radius = 0f) => true;
            public Vector3 Constrain(Vector3 currentPosition, Vector3 desiredPosition, float radius = 0f) => desiredPosition;
        }

        [UnityTest]
        public IEnumerator CityStreetGusts_HatAndCollarFollowWalkingBodiesOnSeparateCrests()
        {
            var stage = new GameObject("Wind on the pavement");
            stage.transform.position = StageOrigin;
            var camera = new GameObject("Street view").AddComponent<Camera>();
            camera.transform.SetParent(stage.transform, false);
            camera.transform.localPosition = new Vector3(1.7f, 1.5f, 3.6f);
            camera.transform.LookAt(StageOrigin + Vector3.up);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.gray;
            camera.enabled = false;
            var light = new GameObject("Street daylight").AddComponent<Light>();
            light.transform.SetParent(stage.transform, false);
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, 145f, 0f);
            var bodies = new CityPedestrianPresentation[2];
            var gusts = new CityStreetGusts(2, i => bodies[i], 0);
            var baked = new Mesh();
            bool fog = RenderSettings.fog;
            try
            {
                RenderSettings.fog = false;
                var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.transform.SetParent(stage.transform, false);
                for (int i = 0; i < 2; i++)
                {
                    var body = CityPedestrianDefaultNpcBody.Create(stage.transform,
                        DefaultNpcPopulation.PedestrianId(i == 0 ? 0 : 14), null);
                    var wardrobe = body.GetComponentInChildren<NpcWardrobe>(true);
                    wardrobe.SetSlot("headwear", i == 0 ? "headwear.work" : null);
                    if (i == 1) wardrobe.SetSlot("outerwear", "outerwear.cardigan");
                    body.transform.localPosition = Vector3.right * (i - .5f) * 1.4f;
                    bodies[i] = body.gameObject.AddComponent<CityPedestrianPresentation>();
                    bodies[i].Initialize(body);
                    body.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    bodies[i].SetMoving(true);
                }
                Physics.SyncTransforms();
                yield return null;
                var calm = new WindSample(90f, .2f);
                var crest = new WindSample(90f, .88f);
                gusts.Advance(20f, StageOrigin, calm);
                Assert.That(bodies[0].RespondingToGust || bodies[1].RespondingToGust, Is.False);
                for (int i = 0; i < 2; i++)
                {
                    var body = bodies[i];
                    gusts.Advance(.05f, StageOrigin, crest);
                    float phase = body.WalkCycle;
                    for (int tick = 0; tick < 6; tick++)
                    {
                        foreach (var walker in bodies)
                        {
                            var travel = Vector3.forward * .082f;
                            walker.transform.position += travel;
                            walker.AdvanceTravelled(.1f, true, travel);
                        }
                        var sole = body.Registry.LeftFoot.position;
                        gusts.Advance(.1f, StageOrigin, crest);
                        Assert.That(body.Registry.LeftFoot.position, Is.EqualTo(sole));
                    }
                    Assert.That(body.WalkCycle, Is.GreaterThan(phase));
                    Assert.That(body.RespondingToGust, Is.True);
                    Assert.That(bodies[1 - i].RespondingToGust, Is.False);
                    var wrist = CityPedestrianHandProps.FindSocket(body.Registry.ModelRoot, "hand.R");
                    var palm = body.GetComponentInChildren<NpcHandPose>().GetAnatomicalFrame(false);
                    Vector3 contact = wrist.position + wrist.rotation * palm.PalmCentreInHand;
                    if (i == 0)
                    {
                        float gap = float.PositiveInfinity;
                        foreach (var part in body.GetComponentInChildren<NpcWardrobe>().Garments)
                        {
                            if (part.Slot != "headwear" || part.Renderer is not SkinnedMeshRenderer hat || !hat.enabled) continue;
                            foreach (var point in CityBusNpcPassengerPlayModeTests.CalibratedBakedWorldVertices(hat, baked))
                                gap = Mathf.Min(gap, Vector3.Distance(point, contact));
                        }
                        Assert.That(gap, Is.LessThan(.045f), "The palm must actually meet the worn cap.");
                    }
                    else Assert.That(Vector3.Distance(contact,
                        CityPedestrianHandProps.FindSocket(body.Registry.ModelRoot, "neck").position), Is.LessThan(.14f));
                    Quaternion held = wrist.rotation;
                    gusts.Advance(0f, StageOrigin, calm);
                    Assert.That(Quaternion.Angle(held, wrist.rotation), Is.LessThan(.01f));
                    yield return null;
                    AreaCaptureFixture.CaptureCurrentCamera(camera, "CityStreetGusts", i == 0 ? "cap" : "collar", 960, 640);
                    gusts.Advance(15f, StageOrigin, calm);
                    Assert.That(body.RespondingToGust, Is.False);
                }
            }
            finally
            {
                gusts.Cancel();
                foreach (var body in bodies) if (body != null) body.Shutdown();
                RenderSettings.fog = fog;
                Object.Destroy(baked);
                Object.Destroy(stage);
            }
        }

        private static void AssertCadenceThroughEvaluationThrottle(CityPedestrianPresentation walker)
        {
            const float phase = .1f;
            var character = walker.GetComponent<CityPedestrianDefaultNpcBody>().Character;
            float cycleMetres = CityPedestrianResources.DefaultNpcWalkCycleMetres * character.transform.localScale.y;
            walker.ConfigureCycle(1f, phase);
            walker.SetMoving(true);
            walker.MinimumEvaluationInterval = .1f;
            walker.AdvanceTravelled(.04f, true, Vector3.right * (cycleMetres * .06f));
            walker.AdvanceTravelled(.04f, true, Vector3.right * (cycleMetres * .02f));
            Assert.That(walker.WalkCycle, Is.EqualTo(phase).Within(.0001f));
            walker.AdvanceTravelled(.04f, false, Vector3.zero);
            Assert.That(walker.WalkCycle, Is.EqualTo(phase + .08f).Within(.0001f));
            walker.AdvanceTravelled(.2f, false, Vector3.zero);
            Assert.That(walker.WalkCycle, Is.EqualTo(phase + .08f).Within(.0001f));
        }

        private static void AssertSupportKnee(Transform ankle)
        {
            Transform knee = ankle.parent;
            float flex = Vector3.Angle(knee.position - knee.parent.position, ankle.position - knee.position);
            Assert.That(flex, Is.LessThan(34f), "The passing pose must rise above the supporting boot.");
        }

        [UnityTest]
        [Explicit("Capture, not a test. Look at Captures/PedestrianGait/.")]
        public IEnumerator RoamingWalkers_WaitAndWalkOnCamera()
        {
            string folder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Captures",
                "PedestrianGait");
            Directory.CreateDirectory(folder);

            var stage = new GameObject("Gait Capture Stage");
            stage.transform.position = StageOrigin;
            var lightObject = new GameObject("Gait Capture Light");
            var cameraObject = new GameObject("Gait Capture Camera");
            var target = new RenderTexture(Width, Height, 24);
            var pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            bool previousFog = RenderSettings.fog;
            AmbientMode previousMode = RenderSettings.ambientMode;
            Color previousAmbient = RenderSettings.ambientLight;
            var presentations = new CityPedestrianPresentation[RoamingPrefabPaths.Length];
            try
            {
                RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.5f);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.3f;
                light.shadows = LightShadows.Soft;
                light.transform.rotation = Quaternion.Euler(40f, -30f, 0f);

                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.12f, 0.14f, 0.15f);
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 30f;
                camera.targetTexture = target;

                // A floor under the boots, so the leg layer has ground to
                // probe and a frame shows whether the soles meet it.
                GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.name = "Floor";
                floor.transform.SetParent(stage.transform, false);
                floor.transform.localScale = new Vector3(3f, 1f, 3f);

                float lineHalf = (RoamingPrefabPaths.Length - 1) * Spacing * 0.5f;
                for (int index = 0; index < RoamingPrefabPaths.Length; index++)
                {
                    GameObject prefab = Resources.Load<GameObject>(RoamingPrefabPaths[index]);
                    Assert.That(prefab, Is.Not.Null, $"{RoamingPrefabPaths[index]} is missing.");
                    GameObject instance = Object.Instantiate(prefab, stage.transform);
                    instance.transform.localPosition =
                        new Vector3(-lineHalf + index * Spacing, 0f, 0f);
                    // Face the camera, which stands on -Z.
                    instance.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    var registry = instance.GetComponent<CityPedestrianAssetRegistry>();
                    Assert.That(registry, Is.Not.Null);
                    CityPedestrianPresentation presentation =
                        instance.AddComponent<CityPedestrianPresentation>();
                    presentation.Initialize(registry, CityPedestrianClipSource.Roaming);
                    registry.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    presentations[index] = presentation;
                }

                yield return null;

                // Let the leg layer's blend settle on the floor.
                for (int frame = 0; frame < 30; frame++)
                {
                    foreach (CityPedestrianPresentation presentation in presentations)
                    {
                        presentation.Advance(1f / 60f, false, true);
                    }

                    yield return null;
                }

                Vector3 lineCentre = StageOrigin + new Vector3(0f, 0.95f, 0f);
                // The first render of a session has no shadow maps yet.
                Shoot(camera, pixels, target, null,
                    StageOrigin + new Vector3(0f, 1.6f, -6f), lineCentre, 55f);

                // Waiting at a crossing: the street idle at rest and mid-breath.
                foreach (CityPedestrianPresentation presentation in presentations)
                {
                    presentation.SetMoving(false, true);
                    presentation.ConfigureCycle(1f, 0f);
                    presentation.Advance(0f, false, true);
                }

                yield return null;
                Shoot(camera, pixels, target, Path.Combine(folder, "00-idle-front.png"),
                    StageOrigin + new Vector3(0f, 1.5f, -6.4f), lineCentre, 60f);
                Shoot(camera, pixels, target, Path.Combine(folder, "01-idle-three-quarter.png"),
                    StageOrigin + new Vector3(-4.6f, 1.7f, -4.6f), lineCentre, 60f);

                // Walking: four phases of the hero's cycle, every walker in
                // step so one frame compares all six.
                float[] phases = { 0f, 0.25f, 0.5f, 0.75f };
                for (int phaseIndex = 0; phaseIndex < phases.Length; phaseIndex++)
                {
                    float phase = phases[phaseIndex];
                    foreach (CityPedestrianPresentation presentation in presentations)
                    {
                        presentation.SetMoving(true, true);
                        presentation.ConfigureCycle(1f, phase);
                        presentation.Advance(0f, true, true);
                    }

                    yield return null;
                    Shoot(camera, pixels, target,
                        Path.Combine(folder, $"1{phaseIndex}-walk-{phase:0.00}-front.png"),
                        StageOrigin + new Vector3(0f, 1.5f, -6.4f), lineCentre, 60f);
                    Shoot(camera, pixels, target,
                        Path.Combine(folder, $"2{phaseIndex}-walk-{phase:0.00}-three-quarter.png"),
                        StageOrigin + new Vector3(-4.6f, 1.7f, -4.6f), lineCentre, 60f);
                }

                // Side views at heel contact, one walker at a time, so the
                // fore-and-aft swing of each arm reads against the legs.
                // The line stands along X, so a camera out on X would sit
                // inside a neighbour: every walker turns to face +X instead
                // and is photographed from -Z, close enough that the
                // neighbours stay out of frame.
                foreach (CityPedestrianPresentation presentation in presentations)
                {
                    presentation.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                    presentation.SetMoving(true, true);
                    presentation.ConfigureCycle(1f, 0f);
                    presentation.Advance(0f, true, true);
                }

                yield return null;
                for (int index = 0; index < presentations.Length; index++)
                {
                    Vector3 focus = presentations[index].transform.position +
                                    new Vector3(0f, 0.95f, 0f);
                    Shoot(camera, pixels, target,
                        Path.Combine(folder, $"3{index}-walk-side-{Path.GetFileName(RoamingPrefabPaths[index])}.png"),
                        focus + new Vector3(0f, 0.2f, -2.4f), focus, 40f);
                }

                Debug.Log($"Pedestrian gait captures wrote {folder}");
            }
            finally
            {
                foreach (CityPedestrianPresentation presentation in presentations)
                {
                    if (presentation != null)
                    {
                        presentation.Shutdown();
                    }
                }

                RenderSettings.fog = previousFog;
                RenderSettings.ambientMode = previousMode;
                RenderSettings.ambientLight = previousAmbient;
                Object.Destroy(stage);
                Object.Destroy(lightObject);
                Object.Destroy(cameraObject);
                Object.Destroy(pixels);
                target.Release();
                Object.Destroy(target);
            }
        }

        private static void Shoot(
            Camera camera,
            Texture2D pixels,
            RenderTexture target,
            string path,
            Vector3 position,
            Vector3 lookAt,
            float fieldOfView)
        {
            camera.transform.position = position;
            camera.transform.rotation = Quaternion.LookRotation(
                lookAt - position,
                Vector3.up);
            camera.fieldOfView = fieldOfView;
            camera.Render();
            if (path == null)
            {
                return;
            }

            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
                pixels.Apply();
            }
            finally
            {
                RenderTexture.active = previous;
            }

            File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
    }
}
