using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class HomeInteriorPresentationPlayModeTests
    {
        private const float TimeoutSeconds = 15f;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.TrySetDebugGameDay(3);
            GameSessionState.EnterHome();
            GameSessionState.ClearRoute();
            GameSessionState.ResetDrinkingState();
            GameSessionState.ResetEconomyState();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Scene homeScene =
                SceneManager.GetSceneByName(
                    SceneIds.HomeInterior);
            if (homeScene.IsValid() && homeScene.isLoaded)
            {
                Scene cleanupScene =
                    SceneManager.CreateScene(
                        "Home Presentation Test Cleanup");
                SceneManager.SetActiveScene(cleanupScene);
                AsyncOperation unload =
                    SceneManager.UnloadSceneAsync(homeScene);
                if (unload != null)
                {
                    while (!unload.isDone)
                    {
                        yield return null;
                    }
                }
            }

            GameSessionState.BeginNewGame();
            yield return null;
        }

        [UnityTest]
        public IEnumerator
            HomeScene_BuildsBathroomAtmosphereAndThreeStableFixedShots()
        {
            AsyncOperation load =
                SceneManager.LoadSceneAsync(
                    SceneIds.HomeInterior,
                    LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
            {
                yield return null;
            }

            HomeInteriorRoot home = null;
            float deadline =
                Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                home =
                    Object.FindAnyObjectByType<
                        HomeInteriorRoot>();
                if (home != null && home.IsInitialized)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(home, Is.Not.Null);
            Assert.That(home.IsInitialized, Is.True);
            Assert.That(home.PauseMenu, Is.Not.Null);
            Assert.That(home.PauseMenu.IsInitialized, Is.True);
            Assert.That(home.Inventory, Is.Not.Null);
            Assert.That(home.Inventory.IsInitialized, Is.True);
            Assert.That(
                GameAudioMixer.CurrentProfile,
                Is.EqualTo(GameAudioProfile.Home));
            Assert.That(home.Atmosphere, Is.Not.Null);
            Assert.That(home.Atmosphere.IsInitialized, Is.True);
            Assert.That(
                home.Atmosphere.PracticalLights,
                Has.Count.EqualTo(2));
            Assert.That(
                home.Atmosphere.BathroomSpillLight,
                Is.Not.Null);
            Renderer exitDoorRenderer = AssertRequiredObject(
                home.Room,
                "Home Exit Door").GetComponent<Renderer>();
            Assert.That(exitDoorRenderer, Is.Not.Null);
            Light bathroomSpillLight =
                home.Atmosphere.BathroomSpillLight;
            Assert.That(
                bathroomSpillLight.cullingMask &
                (1 << exitDoorRenderer.gameObject.layer),
                Is.Not.EqualTo(0));
            Assert.That(
                Vector3.Distance(
                    bathroomSpillLight.transform.position,
                    exitDoorRenderer.bounds.center),
                Is.LessThan(bathroomSpillLight.range));
            Vector3 exitDoorDirection =
                (exitDoorRenderer.bounds.center -
                 bathroomSpillLight.transform.position).normalized;
            Assert.That(
                Vector3.Angle(
                    bathroomSpillLight.transform.forward,
                    exitDoorDirection),
                Is.LessThan(
                    bathroomSpillLight.innerSpotAngle * 0.5f),
                "The apartment exit must remain inside the spill's " +
                "full-strength inner cone.");
            Assert.That(
                bathroomSpillLight.transform.localPosition.x,
                Is.InRange(
                    home.Layout.BathroomDoorway.xMin,
                    home.Layout.BathroomDoorway.xMax));
            Assert.That(
                bathroomSpillLight.transform.localPosition.z,
                Is.GreaterThan(home.Layout.BathroomBounds.yMin),
                "The spill source must sit inside the bathroom threshold.");
            Assert.That(
                bathroomSpillLight.color.b,
                Is.GreaterThan(bathroomSpillLight.color.r));
            Assert.That(
                bathroomSpillLight.shadows,
                Is.EqualTo(LightShadows.Hard));
            Assert.That(RenderSettings.fog, Is.False);

            Transform toiletBowl = AssertRequiredObject(
                home.Room,
                "Home Bathroom Toilet Bowl");
            Transform toiletCistern = AssertRequiredObject(
                home.Room,
                "Home Bathroom Toilet Cistern");
            Assert.That(
                toiletCistern.position.x,
                Is.GreaterThan(toiletBowl.position.x + 0.25f),
                "The toilet cistern must back onto the right-hand wall.");
            Assert.That(
                toiletCistern.GetComponent<Renderer>().bounds.size.z,
                Is.GreaterThan(
                    toiletCistern.GetComponent<Renderer>().bounds.size.x),
                "The toilet cistern must run parallel to the right-hand wall.");
            AssertRequiredObject(
                home.Room,
                "Home Bathroom Shower Tray");
            AssertRequiredObject(
                home.Room,
                "Home Bathroom Cracked Mirror");
            AssertRequiredObject(
                home.Room,
                "Day2 Table Bottle 0");
            AssertRequiredObject(
                home.Room,
                "Home Old Radio");
            AssertEntryWallSealed(home);
            AssertFurnitureOcclusionCoverage(home);
            Transform mainEmitter = AssertRequiredObject(
                home.Room,
                "Home Main Dirty Bulb");
            Transform bathroomEmitter = AssertRequiredObject(
                home.Room,
                "Home Bathroom Cold Tube");
            HomeBathroomLightFixture bathroomFixture =
                bathroomEmitter.GetComponent<
                    HomeBathroomLightFixture>();
            Assert.That(bathroomFixture, Is.Not.Null);
            Assert.That(bathroomFixture.IsInitialized, Is.True);
            Assert.That(
                home.Atmosphere.BathroomFlicker,
                Is.Not.Null);
            Assert.That(
                home.Atmosphere.BathroomFlicker.Fixture,
                Is.SameAs(bathroomFixture));
            Assert.That(
                home.Atmosphere.BathroomFlicker.BathroomLight,
                Is.SameAs(home.Atmosphere.PracticalLights[1]));
            Assert.That(
                home.Atmosphere.BathroomFlicker.SpillLight,
                Is.SameAs(bathroomSpillLight));
            Assert.That(
                home.Soundscape.BoundBathroomFlicker,
                Is.SameAs(home.Atmosphere.BathroomFlicker),
                "The composed Home root must bind each visual flicker " +
                "edge to the bathroom crackle source.");
            AssertPracticalEmitter(
                mainEmitter,
                home.Atmosphere.PracticalLights[0],
                0.08f);
            AssertPracticalEmitter(
                bathroomEmitter,
                home.Atmosphere.PracticalLights[1],
                0.45f);
            AssertPracticalHalo(
                home.Room,
                "Home Main Bulb Halo");
            AssertPracticalHalo(
                home.Room,
                "Home Bathroom Tube Halo");
            AssertRestingOn(
                home.Room,
                "Home Scarred Table",
                "Day2 Table Bottle 0");
            AssertRestingOn(
                home.Room,
                "Home Kitchen Top Left",
                "Day2 Kitchen Plate 0");
            AssertRestingOn(
                home.Room,
                "Home Battered Cabinet",
                "Home Old Radio");

            Transform bathroomWall = AssertRequiredObject(
                home.Room,
                "Home Bathroom West Wall");
            Collider bathroomWallCollider =
                bathroomWall.GetComponent<Collider>();
            Assert.That(bathroomWallCollider, Is.Not.Null);
            Assert.That(bathroomWallCollider.enabled, Is.True);
            Transform bathroomDoor = AssertRequiredObject(
                home.Room,
                "Home Bathroom Door Ajar");
            Collider bathroomDoorCollider =
                bathroomDoor.GetComponent<Collider>();
            Assert.That(bathroomDoorCollider, Is.Not.Null);
            Assert.That(bathroomDoorCollider.enabled, Is.True);
            Physics.SyncTransforms();
            Assert.That(
                bathroomDoorCollider.bounds.min.x -
                home.Layout.BathroomDoorway.xMin,
                Is.GreaterThan(0.74f),
                "The solid ajar door must still leave controller-width " +
                "clearance through the left side of the doorway.");
            for (int sample = 0; sample < 3; sample++)
            {
                float z = 0.28f + sample * 0.36f;
                Assert.That(
                    Physics.CheckCapsule(
                        new Vector3(2.10f, 0.35f, z),
                        new Vector3(2.10f, 1.41f, z),
                        0.31f,
                        ~0,
                        QueryTriggerInteraction.Ignore),
                    Is.False,
                    "The open side of the bathroom doorway must remain " +
                    "traversable by the player capsule.");
            }
            Transform bottle = AssertRequiredObject(
                home.Room,
                "Day2 Table Bottle 0");
            Assert.That(
                bottle.GetComponent<Collider>(),
                Is.Null,
                "Narrative clutter must not become a movement blocker.");

            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            Assert.That(
                camera.GetUniversalAdditionalCameraData()
                    .renderPostProcessing,
                Is.True);
            Assert.That(
                Object.FindObjectsByType<Camera>(
                    FindObjectsInactive.Exclude),
                Has.Length.EqualTo(1));
            Assert.That(
                Object.FindObjectsByType<AudioListener>(
                    FindObjectsInactive.Exclude),
                Has.Length.EqualTo(1));
            Assert.That(home.CameraFollow.FixedPoseActive, Is.True);
            Assert.That(
                home.FixedCamera.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.MainRoom));
            Assert.That(
                home.Player.Visual,
                Is.TypeOf<Player3DCharacterPresentation>());

            Vector3 mainPosition =
                home.CameraFollow.FixedBasePosition;
            Quaternion mainRotation =
                home.CameraFollow.FixedBaseRotation;
            float mainFov =
                home.CameraFollow.FixedBaseFieldOfView;
            Assert.That(mainFov, Is.EqualTo(64f).Within(0.001f));
            Assert.That(
                home.Layout.TryGetFurniture(
                    HomeFurnitureKind.CameraCornerJunk,
                    out HomeFurnitureFootprint cornerJunk),
                Is.True);
            Assert.That(
                cornerJunk.Bounds.Contains(
                    new Vector2(
                        mainPosition.x,
                        mainPosition.z)),
                Is.True,
                "The selected bed-side camera corner must be physically " +
                "blocked by atmospheric junk.");
            Transform cornerJunkBase = AssertRequiredObject(
                home.Room,
                "Home Camera Corner Junk Base");
            Collider cornerJunkCollider =
                cornerJunkBase.GetComponent<Collider>();
            Assert.That(cornerJunkCollider, Is.Not.Null);
            Assert.That(cornerJunkCollider.enabled, Is.True);
            CharacterController playerController =
                home.Player.GameObject
                    .GetComponent<CharacterController>();
            Assert.That(playerController, Is.Not.Null);
            Assert.That(
                cornerJunkCollider.bounds.size.y,
                Is.GreaterThan(
                    playerController.stepOffset +
                    playerController.skinWidth),
                "The low foreground junk must remain too tall to step over.");
            Assert.That(
                cornerJunkCollider.bounds.size.y,
                Is.LessThan(0.60f),
                "Foreground junk must not hide most of the nearby player.");
            AssertPlayerVisible(camera, home.Player.GameObject.transform);
            AssertEmitterVisible(camera, mainEmitter);
            AssertEntryDoorLamp(
                home,
                camera,
                exitDoorRenderer);
            Assert.That(
                Physics.CheckSphere(
                    mainPosition,
                    0.08f,
                    ~0,
                    QueryTriggerInteraction.Ignore),
                Is.False,
                "The main fixed-camera anchor must not intersect geometry.");

            home.Player.Motor.Teleport(
                new Vector3(
                    cornerJunk.Bounds.xMax +
                    playerController.radius +
                    0.02f,
                    0.12f,
                    mainPosition.z));
            yield return null;
            Assert.That(
                home.FixedCamera.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.MainRoom));
            AssertPlayerVisible(
                camera,
                home.Player.GameObject.transform);
            AssertForegroundJunkOcclusionContract(
                home,
                cornerJunkCollider);

            // Framing is proved where the shot is AUTHORED to hold him -
            // the middle of its own hold rect - and not in the camera
            // corner above. That corner is barely two metres from the lens
            // and off at the edge of a 64-degree frame pitched steeply
            // down: nothing standing there can be shown whole, which is
            // exactly why the junk pile is placed in it. The corner earns
            // its own contract (visible, and occluded correctly) a few
            // lines up; this is the one about composition.
            Assert.That(
                home.FixedCamera.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.MainRoom));
            Rect mainHold = home.FixedCamera.ActiveShot.HoldBounds;
            home.Player.Motor.Teleport(
                new Vector3(
                    mainHold.center.x,
                    home.Layout.PlayerSpawn.y,
                    mainHold.center.y));
            yield return null;

            Assert.That(
                home.FixedCamera.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.MainRoom));
            AssertPlayerPresentationInFrame(
                camera,
                home.Player.Visual);

            home.Player.Motor.Teleport(
                new Vector3(2.40f, 0.12f, 1.30f));
            yield return null;

            Assert.That(
                home.FixedCamera.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.Bathroom));
            Assert.That(
                home.CameraFollow.FixedBaseFieldOfView,
                Is.EqualTo(92f).Within(0.001f));
            AssertEmitterVisible(camera, bathroomEmitter);
            AssertPlayerPresentationInFrame(
                camera,
                home.Player.Visual,
                requireWholeBody: false);
            Assert.That(
                Vector3.Distance(
                    camera.transform.position,
                    home.CameraFollow.FixedBasePosition),
                Is.LessThan(0.001f));
            AssertPlayerVisible(camera, home.Player.GameObject.transform);
            Quaternion bathroomRotation =
                home.CameraFollow.FixedBaseRotation;
            Assert.That(
                Vector3.Dot(
                    PlanarForward(mainRotation),
                    PlanarForward(bathroomRotation)),
                Is.GreaterThan(0.95f),
                "The bathroom cut must preserve the movement basis.");
            Assert.That(
                Physics.CheckSphere(
                    camera.transform.position,
                    0.08f,
                    ~0,
                    QueryTriggerInteraction.Ignore),
                Is.False,
                "The bathroom fixed-camera anchor must not intersect geometry.");

            home.CameraFollow.RotateYaw(120f);
            yield return null;
            Assert.That(
                Vector3.Distance(
                    camera.transform.position,
                    home.CameraFollow.FixedBasePosition),
                Is.LessThan(0.001f));
            Assert.That(
                Quaternion.Angle(
                    camera.transform.rotation,
                    bathroomRotation),
                Is.LessThan(0.01f));

            home.Player.Motor.Teleport(
                home.Layout.PlayerSpawn);
            yield return null;

            Assert.That(
                home.FixedCamera.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.MainRoom));
            Assert.That(
                Vector3.Distance(
                    camera.transform.position,
                    mainPosition),
                Is.LessThan(0.001f));
            Assert.That(
                Quaternion.Angle(
                    home.CameraFollow.FixedBaseRotation,
                    mainRotation),
                Is.LessThan(0.01f));
            Assert.That(
                camera.fieldOfView,
                Is.EqualTo(mainFov).Within(0.001f));
            AssertPlayerVisible(camera, home.Player.GameObject.transform);
            LogAssert.NoUnexpectedReceived();
        }

        private static void AssertRestingOn(
            Transform root,
            string supportName,
            string itemName,
            float tolerance = 0.04f)
        {
            Renderer support =
                AssertRequiredObject(
                    root,
                    supportName)
                    .GetComponent<Renderer>();
            Renderer item =
                AssertRequiredObject(
                    root,
                    itemName)
                    .GetComponent<Renderer>();
            Assert.That(support, Is.Not.Null);
            Assert.That(item, Is.Not.Null);
            float separation =
                item.bounds.min.y -
                support.bounds.max.y;
            Assert.That(
                separation,
                Is.InRange(-tolerance, tolerance),
                $"'{itemName}' must rest on '{supportName}' instead of " +
                "floating or intersecting deeply.");
        }

        private static void AssertForegroundJunkOcclusionContract(
            HomeInteriorRoot home,
            Collider blockingCollider)
        {
            Assert.That(home.OcclusionRegistry, Is.Not.Null);
            Assert.That(home.PlayerOcclusion, Is.Not.Null);
            Assert.That(home.PlayerOcclusion.IsInitialized, Is.True);
            Assert.That(
                home.OcclusionRegistry.TryGetGroup(
                    "home.furniture.camera-junk",
                    out HomeOccluderGroup group),
                Is.True);

            Assert.That(
                group.Kind,
                Is.EqualTo(HomeOccluderKind.FurnitureBlocker));
            Assert.That(group.Renderers, Is.Not.Empty);
            Assert.That(blockingCollider, Is.Not.Null);
            Assert.That(blockingCollider.enabled, Is.True);
            Assert.That(
                blockingCollider.gameObject.activeInHierarchy,
                Is.True,
                "Visual cutaway must not alter the blocking geometry.");
            for (int index = 0;
                 index < group.Renderers.Count;
                 index++)
            {
                Assert.That(
                    group.Renderers[index].sharedMaterial,
                    Is.SameAs(HomeOcclusionResources.DitherMaterial));
            }
        }

        private static void AssertFurnitureOcclusionCoverage(
            HomeInteriorRoot home)
        {
            Assert.That(
                home.OcclusionRegistry.TryGetGroup(
                    "home.furniture.sofa",
                    out HomeOccluderGroup sofaGroup),
                Is.True);
            Renderer sofaLaundry =
                AssertRequiredObject(
                    home.Room,
                    "Day3 Sofa Laundry")
                    .GetComponent<Renderer>();
            Assert.That(sofaLaundry, Is.Not.Null);
            Assert.That(
                sofaGroup.Renderers,
                Does.Contain(sofaLaundry),
                "Laundry left on the sofa must reveal with the sofa group.");

            Assert.That(
                home.OcclusionRegistry.TryGetGroup(
                    "home.furniture.alarm-clock",
                    out HomeOccluderGroup alarmGroup),
                Is.True);
            Assert.That(
                alarmGroup.Kind,
                Is.EqualTo(HomeOccluderKind.FurnitureBlocker));
            string[] opaquePartNames =
            {
                "Home Alarm Clock Nightstand",
                "Home Alarm Clock Nightstand Top",
                "Home Alarm Clock Nightstand Handle",
                "Alarm Clock Body",
                "Alarm Clock Face",
                "Alarm Clock Snooze"
            };
            for (int index = 0;
                 index < opaquePartNames.Length;
                 index++)
            {
                Renderer renderer =
                    AssertRequiredObject(
                        home.Room,
                        opaquePartNames[index])
                        .GetComponent<Renderer>();
                Assert.That(renderer, Is.Not.Null);
                Assert.That(
                    alarmGroup.Renderers,
                    Does.Contain(renderer));
                Assert.That(
                    renderer.sharedMaterial,
                    Is.SameAs(HomeOcclusionResources.DitherMaterial));
            }

            Renderer displaySegment =
                AssertRequiredObject(
                    home.Room,
                    "Alarm Clock Digit 0 Segment 0")
                    .GetComponent<Renderer>();
            Assert.That(displaySegment, Is.Not.Null);
            for (int index = 0;
                 index < alarmGroup.Renderers.Count;
                 index++)
            {
                Assert.That(
                    alarmGroup.Renderers[index],
                    Is.Not.SameAs(displaySegment),
                    "Emissive clock digits must remain outside the cutaway.");
            }
            Assert.That(
                displaySegment.sharedMaterial,
                Is.Not.SameAs(HomeOcclusionResources.DitherMaterial));
        }

        private static void AssertEntryWallSealed(
            HomeInteriorRoot home)
        {
            Renderer leftWall =
                AssertRequiredObject(
                    home.Room,
                    "Home Entry Wall Left")
                    .GetComponent<Renderer>();
            Renderer leftInfill =
                AssertRequiredObject(
                    home.Room,
                    "Home Entry Wall Left Door Infill")
                    .GetComponent<Renderer>();
            Renderer door =
                AssertRequiredObject(
                    home.Room,
                    "Home Exit Door")
                    .GetComponent<Renderer>();
            Renderer rightInfill =
                AssertRequiredObject(
                    home.Room,
                    "Home Entry Wall Right Door Infill")
                    .GetComponent<Renderer>();
            Renderer rightWall =
                AssertRequiredObject(
                    home.Room,
                    "Home Entry Wall Right")
                    .GetComponent<Renderer>();
            Renderer transom =
                AssertRequiredObject(
                    home.Room,
                    "Home Entry Door Transom Infill")
                    .GetComponent<Renderer>();
            Renderer lintel =
                AssertRequiredObject(
                    home.Room,
                    "Home Entry Lintel")
                    .GetComponent<Renderer>();

            AssertBoundsMeet(
                leftWall.bounds.max.x,
                leftInfill.bounds.min.x);
            AssertBoundsMeet(
                leftInfill.bounds.max.x,
                door.bounds.min.x);
            AssertBoundsMeet(
                door.bounds.max.x,
                rightInfill.bounds.min.x);
            AssertBoundsMeet(
                rightInfill.bounds.max.x,
                rightWall.bounds.min.x);
            AssertBoundsMeet(
                door.bounds.max.y,
                transom.bounds.min.y);
            AssertBoundsMeet(
                transom.bounds.max.y,
                lintel.bounds.min.y);
            Assert.That(
                leftInfill.GetComponent<Collider>(),
                Is.Not.Null);
            Assert.That(
                rightInfill.GetComponent<Collider>(),
                Is.Not.Null);
            Assert.That(
                transom.GetComponent<Collider>(),
                Is.Not.Null);
            Assert.That(
                door.GetComponent<Collider>(),
                Is.Null);
            Assert.That(
                home.transform.Find("Home Exit Header"),
                Is.Null,
                "The old emissive exit header must not intrude into the " +
                "main-camera frame.");
        }

        private static void AssertBoundsMeet(
            float first,
            float second)
        {
            Assert.That(
                first,
                Is.EqualTo(second)
                    .Within(0.002f),
                "The entry shell must not leave a visible gap.");
        }

        private static void AssertPracticalEmitter(
            Transform emitter,
            Light light,
            float maximumDistance)
        {
            Renderer renderer = emitter.GetComponent<Renderer>();
            Assert.That(renderer, Is.Not.Null);
            Assert.That(
                renderer.sharedMaterial,
                Is.SameAs(CityNightResources.EmissiveMaterial));
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            Color hdrColor = properties.GetColor(
                Shader.PropertyToID("_BaseColor"));
            Assert.That(
                Mathf.Max(
                    hdrColor.r,
                    Mathf.Max(hdrColor.g, hdrColor.b)),
                Is.GreaterThan(1.10f),
                $"'{emitter.name}' must exceed the Home Bloom threshold.");
            Assert.That(
                Vector3.Distance(
                    emitter.position,
                    light.transform.position),
                Is.LessThanOrEqualTo(maximumDistance),
                $"'{light.name}' must originate at its visible fixture.");
        }

        private static void AssertEntryDoorLamp(
            HomeInteriorRoot home,
            Camera camera,
            Renderer exitDoorRenderer)
        {
            Transform lamp = AssertRequiredObject(
                home.Room,
                "Home Entry Door Lamp");
            Transform housing = AssertRequiredObject(
                lamp,
                "Home Entry Door Lamp Housing");
            Transform hood = AssertRequiredObject(
                lamp,
                "Home Entry Door Lamp Hood");
            Transform glow = AssertRequiredObject(
                lamp,
                "Home Entry Door Lamp Glow");
            Transform haloTransform = AssertRequiredObject(
                lamp,
                "Home Entry Door Lamp Halo");

            Assert.That(lamp.parent, Is.SameAs(home.Room));
            Assert.That(
                lamp.GetComponentsInChildren<Collider>(true),
                Is.Empty,
                "The small wall fixture must not block the player.");
            Assert.That(
                home.Atmosphere.GetComponentsInChildren<Light>(true),
                Has.Length.EqualTo(
                    HomeInteriorAtmosphere.MaximumRealtimeLights));

            Renderer housingRenderer =
                housing.GetComponent<Renderer>();
            Renderer hoodRenderer = hood.GetComponent<Renderer>();
            Renderer glowRenderer = glow.GetComponent<Renderer>();
            Assert.That(housingRenderer, Is.Not.Null);
            Assert.That(hoodRenderer, Is.Not.Null);
            Assert.That(glowRenderer, Is.Not.Null);
            Assert.That(
                glowRenderer.sharedMaterial,
                Is.SameAs(CityNightResources.EmissiveMaterial));
            var properties = new MaterialPropertyBlock();
            glowRenderer.GetPropertyBlock(properties);
            Color hdrColor = properties.GetColor(
                Shader.PropertyToID("_BaseColor"));
            Assert.That(
                Mathf.Max(
                    hdrColor.r,
                    Mathf.Max(hdrColor.g, hdrColor.b)),
                Is.GreaterThan(1.10f),
                "The entry lamp must cross the Home bloom threshold.");

            CityLightHalo halo =
                haloTransform.GetComponent<CityLightHalo>();
            Assert.That(halo, Is.Not.Null);
            Assert.That(halo.IsVisible, Is.True);
            Assert.That(
                halo.HaloRenderer.sharedMaterial,
                Is.SameAs(CityNightResources.AtmosphereMaterial));

            Light entryDoorLight = home.Atmosphere.EntryDoorLight;
            Assert.That(entryDoorLight, Is.Not.Null);
            Assert.That(entryDoorLight.enabled, Is.True);
            Assert.That(
                entryDoorLight.type,
                Is.EqualTo(LightType.Spot));
            Assert.That(
                entryDoorLight.intensity,
                Is.GreaterThanOrEqualTo(8f));
            Assert.That(
                entryDoorLight.color.r,
                Is.GreaterThan(entryDoorLight.color.g));
            Assert.That(
                entryDoorLight.color.g,
                Is.GreaterThan(entryDoorLight.color.b));
            Assert.That(
                Vector3.Distance(
                    entryDoorLight.transform.position,
                    glowRenderer.bounds.center),
                Is.LessThanOrEqualTo(0.12f),
                "The real light must originate at the visible door lamp.");

            Vector3 doorDirection =
                (exitDoorRenderer.bounds.center -
                 entryDoorLight.transform.position).normalized;
            Assert.That(
                Vector3.Distance(
                    entryDoorLight.transform.position,
                    exitDoorRenderer.bounds.center),
                Is.LessThan(entryDoorLight.range));
            Assert.That(
                Vector3.Angle(
                    entryDoorLight.transform.forward,
                    doorDirection),
                Is.LessThan(entryDoorLight.innerSpotAngle * 0.5f),
                "The door must sit inside the lamp's full-strength cone.");

            Vector3 floorPoolCenter =
                home.Room.TransformPoint(
                    new Vector3(0f, 0.05f, -2.75f));
            Vector3 floorPoolDirection =
                (floorPoolCenter -
                 entryDoorLight.transform.position).normalized;
            Assert.That(
                Vector3.Distance(
                    entryDoorLight.transform.position,
                    floorPoolCenter),
                Is.LessThan(entryDoorLight.range));
            Assert.That(
                Vector3.Angle(
                    entryDoorLight.transform.forward,
                    floorPoolDirection),
                Is.LessThan(entryDoorLight.innerSpotAngle * 0.5f),
                "The lamp must cast a visible pool onto the entry floor.");

            Bounds fixtureBounds = housingRenderer.bounds;
            fixtureBounds.Encapsulate(hoodRenderer.bounds);
            fixtureBounds.Encapsulate(glowRenderer.bounds);
            Renderer transomRenderer = AssertRequiredObject(
                    home.Room,
                    "Home Entry Door Transom Infill")
                .GetComponent<Renderer>();
            Assert.That(transomRenderer, Is.Not.Null);
            Assert.That(
                fixtureBounds.center.x,
                Is.EqualTo(exitDoorRenderer.bounds.center.x)
                    .Within(0.01f),
                "The entry lamp must stay centered over the door.");
            Assert.That(
                fixtureBounds.size.x,
                Is.LessThanOrEqualTo(0.35f),
                "The entry lamp must remain a compact object, not a header.");
            Assert.That(
                fixtureBounds.min.y,
                Is.GreaterThan(exitDoorRenderer.bounds.max.y + 0.05f));
            Assert.That(
                fixtureBounds.max.y,
                Is.LessThanOrEqualTo(
                    transomRenderer.bounds.max.y + 0.002f));

            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            GameObject validationCameraObject =
                new GameObject("Home Entry Lamp Validation Camera");
            Camera validationCamera =
                validationCameraObject.AddComponent<Camera>();
            validationCamera.CopyFrom(camera);
            validationCamera.enabled = false;
            validationCamera.transform.SetPositionAndRotation(
                camera.transform.position,
                camera.transform.rotation);
            validationCamera.aspect = 16f / 9f;
            try
            {
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 world = fixtureBounds.center + new Vector3(
                        (corner & 1) == 0
                            ? -fixtureBounds.extents.x
                            : fixtureBounds.extents.x,
                        (corner & 2) == 0
                            ? -fixtureBounds.extents.y
                            : fixtureBounds.extents.y,
                        (corner & 4) == 0
                            ? -fixtureBounds.extents.z
                            : fixtureBounds.extents.z);
                    Vector3 viewport =
                        validationCamera.WorldToViewportPoint(world);
                    Assert.That(viewport.z, Is.GreaterThan(0f));
                    minX = Mathf.Min(minX, viewport.x);
                    minY = Mathf.Min(minY, viewport.y);
                    maxX = Mathf.Max(maxX, viewport.x);
                    maxY = Mathf.Max(maxY, viewport.y);
                }
            }
            finally
            {
                Object.DestroyImmediate(validationCameraObject);
            }

            Assert.That(minX, Is.GreaterThanOrEqualTo(0.02f));
            Assert.That(minY, Is.GreaterThanOrEqualTo(0.02f));
            Assert.That(
                maxX,
                Is.LessThanOrEqualTo(0.97f),
                "The compact lamp must not intrude into the right frame edge.");
            Assert.That(
                maxY,
                Is.LessThanOrEqualTo(0.97f),
                "The compact lamp must not intrude into the top frame edge.");
        }

        private static void AssertPracticalHalo(
            Transform root,
            string haloName)
        {
            CityLightHalo halo =
                AssertRequiredObject(root, haloName)
                    .GetComponent<CityLightHalo>();
            Assert.That(halo, Is.Not.Null);
            Assert.That(halo.IsVisible, Is.True);
            Assert.That(
                halo.HaloRenderer.sharedMaterial,
                Is.SameAs(CityNightResources.AtmosphereMaterial));
        }

        private static void AssertEmitterVisible(
            Camera camera,
            Transform emitter)
        {
            Vector3 viewport =
                camera.WorldToViewportPoint(emitter.position);
            Assert.That(viewport.z, Is.GreaterThan(0f));
            Assert.That(viewport.x, Is.InRange(0.05f, 0.95f));
            Assert.That(viewport.y, Is.InRange(0.05f, 0.95f));
        }

        /// <param name="requireWholeBody">
        /// True where the shot is composed to hold a standing man whole.
        /// False for a close shot that cannot: the bathroom camera sits
        /// inside a room barely two metres across and looks at the hero
        /// from about seventy-five centimetres, so his boots leave the
        /// bottom of a 92-degree frame from every position he can stand
        /// in. That is the shot, not a defect - what still has to hold
        /// there is that he is on screen, head included, and reads as
        /// upright.
        /// </param>
        private static void AssertPlayerPresentationInFrame(
            Camera camera,
            IPlayerPresentation presentation,
            bool requireWholeBody = true)
        {
            Assert.That(presentation, Is.Not.Null);
            Assert.That(presentation.Renderers, Is.Not.Empty);
            Bounds bounds = default;
            bool hasBounds = false;
            for (int index = 0;
                 index < presentation.Renderers.Count;
                 index++)
            {
                Renderer renderer = presentation.Renderers[index];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            Assert.That(hasBounds, Is.True);
            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 world = bounds.center + new Vector3(
                    (corner & 1) == 0
                        ? -bounds.extents.x
                        : bounds.extents.x,
                    (corner & 2) == 0
                        ? -bounds.extents.y
                        : bounds.extents.y,
                    (corner & 4) == 0
                        ? -bounds.extents.z
                        : bounds.extents.z);
                Vector3 viewport = camera.WorldToViewportPoint(
                    world);
                Assert.That(viewport.z, Is.GreaterThan(0f));
                minX = Mathf.Min(minX, viewport.x);
                minY = Mathf.Min(minY, viewport.y);
                maxX = Mathf.Max(maxX, viewport.x);
                maxY = Mathf.Max(maxY, viewport.y);
            }

            Assert.That(minX, Is.GreaterThanOrEqualTo(0.02f));
            Assert.That(maxX, Is.LessThanOrEqualTo(0.98f));
            Assert.That(maxY, Is.LessThanOrEqualTo(0.98f));
            if (requireWholeBody)
            {
                Assert.That(minY, Is.GreaterThanOrEqualTo(0.02f));
            }
            else
            {
                // Half of him, at least, and the half with his head in it.
                Assert.That(
                    maxY - Mathf.Max(minY, 0f),
                    Is.GreaterThan(0.5f * (maxY - minY)),
                    "A close shot may cut his boots, not most of him.");
            }

            float width = maxX - minX;
            float height = maxY - minY;
            Assert.That(width, Is.GreaterThan(0.005f));
            Assert.That(height, Is.GreaterThan(0.01f));
            Assert.That(
                height / width,
                Is.GreaterThan(1.05f),
                "The standing 3D hero must remain recognizably upright " +
                "in every authored Home shot.");
        }

        private static Transform AssertRequiredObject(
            Transform root,
            string objectName)
        {
            Transform[] transforms =
                root.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < transforms.Length; index++)
            {
                if (transforms[index].name == objectName)
                {
                    return transforms[index];
                }
            }

            Assert.Fail(
                $"Required home presentation object '{objectName}' was not built.");
            return null;
        }

        private static void AssertPlayerVisible(
            Camera camera,
            Transform player)
        {
            Vector3 viewport =
                camera.WorldToViewportPoint(
                    player.position + Vector3.up);
            Assert.That(viewport.z, Is.GreaterThan(0f));
            Assert.That(
                viewport.x,
                Is.InRange(0.05f, 0.95f));
            Assert.That(
                viewport.y,
                Is.InRange(0.08f, 0.94f));
        }

        private static Vector3 PlanarForward(
            Quaternion rotation)
        {
            return Vector3.ProjectOnPlane(
                    rotation * Vector3.forward,
                    Vector3.up)
                .normalized;
        }
    }
}
