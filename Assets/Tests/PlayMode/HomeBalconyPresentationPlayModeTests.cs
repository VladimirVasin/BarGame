using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class HomeBalconyPresentationPlayModeTests
    {
        private const float TimeoutSeconds = 15f;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GameSessionState.EnterHome();
            GameSessionState.ClearRoute();
            GameSessionState.ClearVisitedBars();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Scene homeScene =
                SceneManager.GetSceneByName(
                    SceneIds.HomeInterior);
            if (homeScene.IsValid() &&
                homeScene.isLoaded)
            {
                Scene cleanup =
                    SceneManager.CreateScene(
                        "Home Balcony Test Cleanup");
                SceneManager.SetActiveScene(cleanup);
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
            yield return null;
        }

        [UnityTest]
        public IEnumerator
            HomeScene_BuildsWalkableBalconyOnSeededStreet()
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
                Time.realtimeSinceStartup +
                TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                home =
                    Object.FindAnyObjectByType<
                        HomeInteriorRoot>();
                if (home != null &&
                    home.IsInitialized)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(home, Is.Not.Null);
            Assert.That(home.IsInitialized, Is.True);
            Assert.That(home.BalconyLayout, Is.Not.Null);
            Assert.That(home.ExteriorContext, Is.Not.Null);
            Assert.That(
                home.ExteriorContext.NearbyDecorations,
                Is.Not.Empty);
            Assert.That(home.Balcony, Is.Not.Null);
            Assert.That(home.ExteriorView, Is.Not.Null);
            AssertRenderedExteriorDecorations(home.ExteriorView);
            AssertRenderedExteriorDistrictPointsOfInterest(
                home.ExteriorView,
                home.ExteriorContext);
            Assert.That(
                SceneManager.GetActiveScene().name,
                Is.EqualTo(SceneIds.HomeInterior));
            Assert.That(
                Object.FindAnyObjectByType<CityGameRoot>(),
                Is.Null);
            Assert.That(
                Object.FindAnyObjectByType<CityMusicPlayer>(),
                Is.Null);
            Assert.That(
                Object.FindObjectsByType<Camera>(
                    FindObjectsInactive.Exclude),
                Has.Length.EqualTo(1));
            Assert.That(
                Object.FindObjectsByType<AudioListener>(
                    FindObjectsInactive.Exclude),
                Has.Length.EqualTo(1));

            Transform ceiling = AssertRequiredObject(
                home.Room,
                "Home Ceiling");
            Renderer ceilingRenderer =
                ceiling.GetComponent<Renderer>();
            Assert.That(ceilingRenderer, Is.Not.Null);
            Assert.That(
                ceilingRenderer.bounds.min.y,
                Is.LessThanOrEqualTo(
                    home.Layout.RoomHeight));
            Assert.That(
                ceilingRenderer.bounds.max.y,
                Is.GreaterThan(
                    home.Layout.RoomHeight));
            Assert.That(
                ceiling.GetComponent<Collider>(),
                Is.Null);

            Transform southReturn =
                AssertRequiredObject(
                    home.Room,
                    "Home South Exterior Return Wall");
            Transform northReturn =
                AssertRequiredObject(
                    home.Room,
                    "Home North Exterior Return Wall");
            AssertExteriorReturnSealsCorner(
                southReturn,
                home.Layout);
            AssertExteriorReturnSealsCorner(
                northReturn,
                home.Layout);

            AssertPhysicalSurface(
                home.Balcony,
                "Home Balcony Deck");
            AssertPhysicalSurface(
                home.Balcony,
                "Home Balcony Threshold");
            AssertPhysicalSurface(
                home.Balcony,
                "Home Balcony Outer Guard");
            AssertPhysicalSurface(
                home.Balcony,
                "Home Balcony South Guard");
            AssertPhysicalSurface(
                home.Balcony,
                "Home Balcony North Guard");

            Transform glass = AssertRequiredObject(
                home.Balcony,
                "Home Balcony Window Glass");
            Assert.That(
                glass.GetComponent<Collider>(),
                Is.Null);
            Assert.That(
                glass.GetComponent<Renderer>()
                    .sharedMaterial,
                Is.SameAs(
                    HomeBalconyResources
                        .GlassMaterial));
            Transform doorGlass = AssertRequiredObject(
                home.Balcony,
                "Home Balcony Ajar Door Pivot/" +
                "Home Balcony Door Glass");
            Assert.That(
                doorGlass.GetComponent<Collider>(),
                Is.Null);
            Assert.That(
                home.ExteriorView
                    .GetComponentsInChildren<Collider>(
                        true),
                Is.Empty);
            AssertExteriorStaysBeyondFacade(
                home.Room,
                home.ExteriorView);

            Physics.SyncTransforms();
            for (int sample = 0;
                 sample < 3;
                 sample++)
            {
                float x = 4.76f + sample * 0.24f;
                Assert.That(
                    Physics.CheckCapsule(
                        new Vector3(
                            x,
                            0.45f,
                            PlayerHomeBalconyGeometry
                                .DoorCenterZ),
                        new Vector3(
                            x,
                            1.45f,
                            PlayerHomeBalconyGeometry
                                .DoorCenterZ),
                        0.31f,
                        ~0,
                        QueryTriggerInteraction.Ignore),
                    Is.False,
                    "The apartment and balcony must share a capsule-clear doorway.");
            }

            Camera camera = Camera.main;
            Rect balconyActivation =
                home.BalconyLayout
                    .BalconyCameraActivationBounds;
            home.Player.Motor.Teleport(
                new Vector3(
                    balconyActivation.center.x,
                    home.Layout.PlayerSpawn.y,
                    balconyActivation.center.y));
            yield return null;

            Assert.That(
                home.FixedCamera.ActiveShotKind,
                Is.EqualTo(
                    HomeCameraShotKind.Balcony));
            yield return AssertBalconyOcclusionPresentation(home);
            AssertPlayerVisible(
                camera,
                home.Player.GameObject.transform);
            Assert.That(
                SceneManager.GetActiveScene().name,
                Is.EqualTo(SceneIds.HomeInterior),
                "Walking onto the balcony must not load another scene.");

            home.Player.Motor.Teleport(
                new Vector3(
                    0.50f,
                    home.Layout.PlayerSpawn.y,
                    -0.50f));
            yield return null;
            Assert.That(
                home.FixedCamera.ActiveShotKind,
                Is.EqualTo(
                    HomeCameraShotKind.MainRoom));
            Assert.That(
                SceneManager.GetActiveScene().name,
                Is.EqualTo(SceneIds.HomeInterior));
            LogAssert.NoUnexpectedReceived();
        }

        private static Transform AssertRequiredObject(
            Transform parent,
            string path)
        {
            Transform result = parent.Find(path);
            Assert.That(
                result,
                Is.Not.Null,
                $"Missing generated object '{path}'.");
            return result;
        }

        private static IEnumerator AssertBalconyOcclusionPresentation(
            HomeInteriorRoot home)
        {
            Assert.That(home.OcclusionRegistry, Is.Not.Null);
            Assert.That(home.PlayerOcclusion, Is.Not.Null);
            Assert.That(home.PlayerOcclusion.IsInitialized, Is.True);
            string[] railGroupIds =
            {
                "home.balcony.rail.outer",
                "home.balcony.rail.south",
                "home.balcony.rail.north"
            };
            for (int index = 0;
                 index < railGroupIds.Length;
                 index++)
            {
                Assert.That(
                    home.OcclusionRegistry.TryGetGroup(
                        railGroupIds[index],
                        out HomeOccluderGroup group),
                    Is.True);
                Assert.That(
                    group.Kind,
                    Is.EqualTo(HomeOccluderKind.VisualRail));
                Assert.That(group.Renderers, Is.Not.Empty);
                for (int rendererIndex = 0;
                     rendererIndex < group.Renderers.Count;
                     rendererIndex++)
                {
                    Renderer renderer = group.Renderers[rendererIndex];
                    Assert.That(
                        renderer.sharedMaterial,
                        Is.SameAs(
                            HomeOcclusionResources.DitherMaterial));
                    Assert.That(
                        renderer.GetComponent<Collider>(),
                        Is.Null,
                        "Visible balcony rails must remain separate from safety colliders.");
                }

            }

            Assert.That(
                home.OcclusionRegistry.TryGetGroup(
                    "home.balcony.ajar-door",
                    out HomeOccluderGroup doorGroup),
                Is.True);
            Assert.That(
                doorGroup.Kind,
                Is.EqualTo(HomeOccluderKind.StructuralCutaway));
            Assert.That(doorGroup.Renderers, Is.Not.Empty);
            for (int index = 0;
                 index < doorGroup.Renderers.Count;
                 index++)
            {
                Renderer renderer = doorGroup.Renderers[index];
                Assert.That(
                    renderer.name,
                    Does.Not.Contain("Glass"));
                Assert.That(
                    renderer.sharedMaterial,
                    Is.SameAs(HomeOcclusionResources.DitherMaterial));
            }

            Rect activation =
                home.BalconyLayout.BalconyCameraActivationBounds;
            Vector2[] samples =
            {
                new Vector2(
                    activation.xMin + 0.20f,
                    PlayerHomeBalconyGeometry.DoorCenterZ),
                new Vector2(
                    activation.xMin + 0.50f,
                    PlayerHomeBalconyGeometry.DoorCenterZ),
                new Vector2(
                    activation.xMin + 0.80f,
                    PlayerHomeBalconyGeometry.DoorCenterZ),
                new Vector2(
                    activation.xMin + 1.10f,
                    PlayerHomeBalconyGeometry.DoorCenterZ)
            };
            bool fadedDoorFound = false;
            for (int sampleIndex = 0;
                 sampleIndex < samples.Length &&
                 !fadedDoorFound;
                 sampleIndex++)
            {
                home.Player.Motor.Teleport(
                    new Vector3(
                        samples[sampleIndex].x,
                        home.Layout.PlayerSpawn.y,
                        samples[sampleIndex].y));
                yield return null;
                home.PlayerOcclusion.RefreshImmediate();
                fadedDoorFound =
                    home.PlayerOcclusion.GetVisibility(
                        doorGroup) < 0.999f;
            }

            Assert.That(
                fadedDoorFound,
                Is.True,
                "The balcony shot must reveal the player through the open door leaf near the doorway.");

            home.Player.Motor.Teleport(
                new Vector3(
                    activation.center.x,
                    home.Layout.PlayerSpawn.y,
                    activation.center.y));
            yield return null;
        }

        private static void AssertPhysicalSurface(
            Transform parent,
            string path)
        {
            Collider collider =
                AssertRequiredObject(parent, path)
                    .GetComponent<Collider>();
            Assert.That(collider, Is.Not.Null);
            Assert.That(collider.enabled, Is.True);
        }

        private static void AssertExteriorReturnSealsCorner(
            Transform returnWall,
            HomeInteriorLayoutPlan layout)
        {
            Renderer renderer =
                returnWall.GetComponent<Renderer>();
            Assert.That(renderer, Is.Not.Null);
            Assert.That(
                renderer.bounds.min.x,
                Is.LessThanOrEqualTo(
                    PlayerHomeBalconyGeometry
                        .HomeFacadeX +
                    0.001f));
            Assert.That(
                renderer.bounds.max.x,
                Is.GreaterThanOrEqualTo(
                    PlayerHomeBalconyGeometry
                        .HomeFacadeX +
                    PlayerHomeBalconyGeometry
                        .BalconyDepth +
                    0.50f));
            Assert.That(
                renderer.bounds.max.y,
                Is.EqualTo(layout.RoomHeight)
                    .Within(0.001f));
            Assert.That(
                returnWall.GetComponent<Collider>(),
                Is.Null);
        }

        private static void AssertExteriorStaysBeyondFacade(
            Transform room,
            Transform exterior)
        {
            float localMinimum =
                PlayerHomeBalconyGeometry.HomeFacadeX +
                PlayerHomeBalconyGeometry.WallThickness *
                0.5f +
                0.01f;
            float worldMinimum =
                room.TransformPoint(
                    new Vector3(
                        localMinimum,
                        0f,
                        0f)).x;
            Renderer[] renderers =
                exterior.GetComponentsInChildren<Renderer>(
                    true);
            Assert.That(renderers, Is.Not.Empty);
            bool foundGround = false;
            bool foundStreet = false;
            bool foundBuilding = false;
            for (int index = 0;
                 index < renderers.Length;
                 index++)
            {
                Renderer renderer = renderers[index];
                if (!renderer.enabled)
                {
                    continue;
                }

                Assert.That(
                    renderer.bounds.min.x,
                    Is.GreaterThanOrEqualTo(
                        worldMinimum - 0.02f),
                    $"'{renderer.name}' must stay outside the apartment facade.");
                foundGround |=
                    renderer.name ==
                    "Home Exterior Ground";
                foundStreet |=
                    renderer.name ==
                    "Home Exterior Street Surfaces";
                foundBuilding |=
                    renderer.name ==
                    "Exterior Building Mass";
            }

            Assert.That(foundGround, Is.True);
            Assert.That(foundStreet, Is.True);
            Assert.That(
                foundBuilding,
                Is.True,
                "Half-space clipping must preserve the street view outside.");
        }

        private static void AssertPlayerVisible(
            Camera camera,
            Transform player)
        {
            Assert.That(camera, Is.Not.Null);
            Vector3 viewport =
                camera.WorldToViewportPoint(
                    player.position +
                    Vector3.up * 0.85f);
            Assert.That(viewport.z, Is.GreaterThan(0f));
            Assert.That(
                viewport.x,
                Is.InRange(0.04f, 0.96f));
            Assert.That(
                viewport.y,
                Is.InRange(0.04f, 0.96f));
        }

        private static void AssertRenderedExteriorDecorations(
            Transform exterior)
        {
            Transform decorationRoot = null;
            for (int index = 0; index < exterior.childCount; index++)
            {
                Transform child = exterior.GetChild(index);
                if (string.Equals(
                        child.name,
                        "Home Exterior City Details",
                        System.StringComparison.Ordinal))
                {
                    decorationRoot = child;
                    break;
                }
            }

            Assert.That(
                decorationRoot,
                Is.Not.Null,
                "The reconstructed exterior must own its decoration child.");
            Assert.That(
                decorationRoot.GetComponentsInChildren<Renderer>(true),
                Is.Not.Empty);
            Assert.That(
                decorationRoot.GetComponentsInChildren<Collider>(true),
                Is.Empty);
            Assert.That(
                decorationRoot.GetComponentsInChildren<Light>(true),
                Is.Empty);
            Assert.That(
                decorationRoot.GetComponentsInChildren<AudioSource>(true),
                Is.Empty);
            Assert.That(
                decorationRoot.GetComponentsInChildren<ParticleSystem>(true),
                Is.Empty);
        }

        private static void
            AssertRenderedExteriorDistrictPointsOfInterest(
                Transform exterior,
                HomeExteriorContextPlan context)
        {
            Transform pointOfInterestRoot = null;
            for (int index = 0; index < exterior.childCount; index++)
            {
                Transform child = exterior.GetChild(index);
                if (child.name ==
                    CityDistrictPointOfInterestWorldBuilder
                        .HomeExteriorRootName)
                {
                    pointOfInterestRoot = child;
                    break;
                }
            }

            Assert.That(pointOfInterestRoot, Is.Not.Null);
            int expectedVisibleCount = 0;
            for (int index = 0;
                 index <
                 context.NearbyDistrictPointsOfInterest.Count;
                 index++)
            {
                CityDistrictPointOfInterestDescriptor descriptor =
                    context.NearbyDistrictPointsOfInterest[index];
                Rect bounds =
                    PlayerHomeBalconyGeometry.ToHomeLocalRect(
                        context.PlayerHome,
                        descriptor.PublicBounds);
                if (bounds.xMin <
                    HomeExteriorViewBuilder.ExteriorMinimumX)
                {
                    continue;
                }

                expectedVisibleCount++;
                Assert.That(
                    pointOfInterestRoot.Find(
                        CityDistrictPointOfInterestWorldBuilder
                            .GetSiteName(descriptor.Id)),
                    Is.Not.Null);
            }

            Assert.That(
                pointOfInterestRoot.childCount,
                Is.EqualTo(expectedVisibleCount));
            Assert.That(
                pointOfInterestRoot
                    .GetComponentsInChildren<Collider>(true),
                Is.Empty);
            Assert.That(
                pointOfInterestRoot
                    .GetComponentsInChildren<Light>(true),
                Is.Empty);
            Assert.That(
                pointOfInterestRoot
                    .GetComponentsInChildren<AudioSource>(true),
                Is.Empty);
            Assert.That(
                pointOfInterestRoot
                    .GetComponentsInChildren<ParticleSystem>(true),
                Is.Empty);
        }
    }
}
