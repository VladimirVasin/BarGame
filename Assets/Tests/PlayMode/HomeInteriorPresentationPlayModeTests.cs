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
            GameSessionState.EnterHome();
            GameSessionState.ClearRoute();
            GameSessionState.ClearVisitedBars();
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

            GameSessionState.ClearRoute();
            GameSessionState.ClearVisitedBars();
            GameSessionState.ResetDrinkingState();
            GameSessionState.ResetEconomyState();
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
                "Home Table Green Bottle");
            AssertRequiredObject(
                home.Room,
                "Home Bed Crumpled Shirt");
            AssertRequiredObject(
                home.Room,
                "Home Faded Photograph");
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
                "Home Table Green Bottle");
            AssertRestingOn(
                home.Room,
                "Home Kitchen Top Left",
                "Home Kitchen Dirty Dishes");
            AssertRestingOn(
                home.Room,
                "Home Bed Crooked Blanket",
                "Home Bed Crumpled Shirt",
                0.08f);
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
                "Home Table Green Bottle");
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
            AssertSpritePlaneFacesCamera(
                camera,
                home.Player.Visual.VisualRoot);
            BillboardSprite playerBillboard =
                home.Player.Visual.VisualRoot
                    .GetComponent<BillboardSprite>();
            Assert.That(playerBillboard, Is.Not.Null);
            Assert.That(
                playerBillboard
                    .CameraPlaneAlignmentEnabled,
                Is.True);

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
            AssertSpriteSilhouetteNotSquashed(
                camera,
                home.Player.Visual.VisualRoot);

            home.Player.Motor.Teleport(
                new Vector3(2.40f, 0.12f, 1.30f));
            yield return null;

            Assert.That(
                home.FixedCamera.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.Bathroom));
            Assert.That(
                home.CameraFollow.FixedBaseFieldOfView,
                Is.EqualTo(92f).Within(0.001f));
            AssertSpritePlaneFacesCamera(
                camera,
                home.Player.Visual.VisualRoot);
            AssertEmitterVisible(camera, bathroomEmitter);
            AssertSpriteSilhouetteNotSquashed(
                camera,
                home.Player.Visual.VisualRoot);
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
                new Vector3(0.50f, 0.12f, 1.50f));
            yield return null;

            Assert.That(
                home.FixedCamera.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.MainRoom));
            AssertSpritePlaneFacesCamera(
                camera,
                home.Player.Visual.VisualRoot);
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
            Renderer cardboardBox =
                AssertRequiredObject(
                    home.Room,
                    "Home Cardboard Box")
                    .GetComponent<Renderer>();
            Assert.That(cardboardBox, Is.Not.Null);
            Assert.That(
                sofaGroup.Renderers,
                Does.Contain(cardboardBox),
                "The tall box on the sofa must reveal with the sofa group.");

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

        private static void AssertSpriteSilhouetteNotSquashed(
            Camera camera,
            Transform visualRoot)
        {
            float halfWidth =
                PlayerSpriteRig.FrameWidth /
                (PlayerSpriteRig.PixelsPerUnit * 2f);
            float bottom =
                -PlayerSpriteRig.FeetPivotPixels /
                PlayerSpriteRig.PixelsPerUnit;
            float top =
                (PlayerSpriteRig.FrameHeight -
                 PlayerSpriteRig.FeetPivotPixels) /
                PlayerSpriteRig.PixelsPerUnit;
            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 local = new Vector3(
                    (corner & 1) == 0 ? -halfWidth : halfWidth,
                    (corner & 2) == 0 ? bottom : top,
                    0f);
                Vector3 viewport = camera.WorldToViewportPoint(
                    visualRoot.TransformPoint(local));
                Assert.That(viewport.z, Is.GreaterThan(0f));
                minX = Mathf.Min(minX, viewport.x);
                minY = Mathf.Min(minY, viewport.y);
                maxX = Mathf.Max(maxX, viewport.x);
                maxY = Mathf.Max(maxY, viewport.y);
            }

            Assert.That(minX, Is.GreaterThanOrEqualTo(0.02f));
            Assert.That(maxX, Is.LessThanOrEqualTo(0.98f));
            Assert.That(minY, Is.GreaterThanOrEqualTo(0.02f));
            Assert.That(maxY, Is.LessThanOrEqualTo(0.98f));
            float width = maxX - minX;
            float height = maxY - minY;
            float presentedAspect =
                height /
                width /
                camera.aspect;
            Assert.That(
                presentedAspect,
                Is.InRange(1.42f, 1.58f),
                "The camera-aligned sprite plane must preserve the " +
                "64x96 player frame instead of compressing it.");
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

        private static void AssertSpritePlaneFacesCamera(
            Camera camera,
            Transform visualRoot)
        {
            Vector3 expectedForward =
                -camera.transform.forward;
            Assert.That(
                Vector3.Angle(
                    visualRoot.forward,
                    expectedForward),
                Is.LessThan(0.1f),
                "The player sprite plane must be refreshed on the same " +
                "hard cut as the fixed camera.");
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
