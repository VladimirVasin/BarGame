using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class MothersHouseInteriorPlayModeTests
    {
        private const string InteriorRootName =
            "[Bar Promenade] Mother's House Interior Runtime";
        private const string VillageRootName =
            "[Bar Promenade] Alpine Village Runtime";
        private const string DoorRootName =
            "[Bar Promenade] Door Transition Runtime";
        private const float TimeoutSeconds = 60f;
        private const float PositionTolerance = 0.05f;
        private const float KettleDockTolerance = 0.005f;
        private const float UprightToleranceDegrees = 0.1f;

        private static readonly string[] KettleRendererNames =
        {
            "ACC_KettleBody",
            "ACC_KettleHandlePost.L",
            "ACC_KettleHandlePost.R",
            "ACC_KettleHandleTop",
            "ACC_KettleKnob",
            "ACC_KettleLid",
            "ACC_KettleRimBand",
            "ACC_KettleShoulder",
            "ACC_KettleSpout",
            "ACC_KettleSpoutTip"
        };

        private static readonly string[] PositiveAtlasSheets =
        {
            "Wallpaper",
            "CeilingPlaster",
            "PlankFloor",
            "DarkWood",
            "Upholstery",
            "BedLinen",
            "Rug",
            "Concrete",
            "Ceramic",
            "PaintedMetal",
            "Glass",
            "Fire"
        };

        private static readonly string[] ForbiddenRoomTextureResourcePaths =
        {
            "Home/Textures/HomeWallpaperAlbedo",
            "Home/Textures/HomeCeilingPlasterAlbedo",
            "Home/Textures/HomePlankFloorAlbedo",
            "Home/Textures/HomeDarkWoodAlbedo",
            "Home/Textures/HomeWornLaminateAlbedo",
            "Home/Textures/HomeUpholsteryAlbedo",
            "Home/Textures/HomeBedLinenAlbedo",
            "Home/Textures/HomeBathroomTileAlbedo",
            "Home/Textures/HomeEnamelAlbedo",
            "Home/Textures/HomePaintedMetalAlbedo",
            "Home/Textures/HomeConcreteAlbedo",
            "Home/Textures/HomeRugAlbedo",
            "Textures/CityWindowAlbedo"
        };

        [SetUp]
        public void SetUp()
        {
            Time.captureDeltaTime = 1f / 60f;
            GameSessionState.BeginNewGame();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            GameSessionState.BeginNewGame();
        }

        [UnityTest]
        public IEnumerator DirectSceneBoot_BuildsTheFixedWarmRoomWithTheExactNpcKettle()
        {
            AssertSceneIsStreamable(SceneIds.MothersHouseInterior);

            MothersHouseInteriorRoot interior = null;
            yield return LoadSceneAndWaitForRoot(
                SceneIds.MothersHouseInterior,
                InteriorRootName,
                (MothersHouseInteriorRoot root) => interior = root);
            yield return WaitUntil(
                () => interior.IsInitialized &&
                      !SceneTransitionService.IsTransitioning,
                "Mother's house interior did not finish booting.");

            AssertDirectBootContract(interior);
            AssertPositiveAtlasContract(interior.World.Registry);
            AssertExactKettleContract(interior);
            AssertFixedCameraAndLightContract(interior);
            AssertCalmSoundscapeContract(interior);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ExistingVillageDoor_EntersAndReturnsToItsOneShotDock()
        {
            AssertSceneIsStreamable(SceneIds.AlpineVillage);
            AssertSceneIsStreamable(SceneIds.MothersHouseInterior);
            AssertSceneIsStreamable(SceneIds.DoorTransition);

            AlpineVillageRoot village = null;
            yield return LoadSceneAndWaitForRoot(
                SceneIds.AlpineVillage,
                VillageRootName,
                (AlpineVillageRoot root) => village = root);
            yield return WaitUntil(
                () => village.IsInitialized &&
                      !SceneTransitionService.IsTransitioning,
                "AlpineVillage did not finish its direct boot.");

            MothersHouseEntrance entrance = village.MothersHouseEntrance;
            Assert.That(entrance, Is.Not.Null);
            Assert.That(entrance, Is.SameAs(village.World.MothersHouseEntrance));
            Assert.That(
                entrance.PromptKey,
                Is.EqualTo("interaction.enter_mothers_house"));
            Vector3 expectedReturn =
                village.Plan.MothersHouseReturnPosition +
                Vector3.up * PlayerFactory.GroundedRootOffset;
            AssertVectorApproximately(
                entrance.ReturnPosition,
                expectedReturn,
                PositionTolerance,
                "The exterior door and return spawn must share the plan-owned dock.");

            PlacePlayerAtDoor(village.Player, entrance);
            yield return null;
            Assert.That(
                entrance.CanInteract(village.Player.Interactor),
                Is.True,
                "The already-visible top-house door must actually open.");
            entrance.Interact(village.Player.Interactor);
            AssertDoorActionStarted(village.Player, "mother's house entrance");
            yield return WaitUntil(
                () => SceneTransitionService.IsTransitioning,
                "The village door action never requested its transition.");

            DoorTransitionRoot enteringDoor = null;
            yield return WaitForLoadedRoot(
                SceneIds.DoorTransition,
                DoorRootName,
                (DoorTransitionRoot root) => enteringDoor = root);
            yield return WaitUntil(
                () => enteringDoor.IsInitialized,
                "The entering door vignette did not initialize.");
            Assert.That(
                enteringDoor.Direction,
                Is.EqualTo(DoorTransitionDirection.EnterApartment));

            MothersHouseInteriorRoot interior = null;
            yield return WaitForLoadedRoot(
                SceneIds.MothersHouseInterior,
                InteriorRootName,
                (MothersHouseInteriorRoot root) => interior = root);
            yield return WaitUntil(
                () => interior.IsInitialized &&
                      !SceneTransitionService.IsTransitioning,
                "The mother's house did not settle after entering.");
            Assert.That(
                GameSessionState.AlpineVillageArrival,
                Is.EqualTo(AlpineVillageArrivalKind.Default));

            PlacePlayerAtDoor(interior.Player, interior.Exit);
            yield return null;
            Assert.That(
                interior.Exit.CanInteract(interior.Player.Interactor),
                Is.True);
            interior.Exit.Interact(interior.Player.Interactor);
            AssertDoorActionStarted(interior.Player, "mother's house exit");
            yield return WaitUntil(
                () => GameSessionState.AlpineVillageArrival ==
                      AlpineVillageArrivalKind.MothersHouseDoor,
                "The exit never armed the mother's-door return token.");
            Assert.That(SceneTransitionService.IsTransitioning, Is.True);

            DoorTransitionRoot exitingDoor = null;
            yield return WaitForLoadedRoot(
                SceneIds.DoorTransition,
                DoorRootName,
                (DoorTransitionRoot root) => exitingDoor = root);
            yield return WaitUntil(
                () => exitingDoor.IsInitialized,
                "The exiting door vignette did not initialize.");
            Assert.That(
                exitingDoor.Direction,
                Is.EqualTo(DoorTransitionDirection.ExitApartment));

            AlpineVillageRoot returnedVillage = null;
            yield return WaitForLoadedRoot(
                SceneIds.AlpineVillage,
                VillageRootName,
                (AlpineVillageRoot root) => returnedVillage = root);
            yield return WaitUntil(
                () => returnedVillage.IsInitialized &&
                      !SceneTransitionService.IsTransitioning,
                "The village did not settle after leaving the house.");

            Assert.That(
                returnedVillage.VillageArrival,
                Is.EqualTo(AlpineVillageArrivalKind.MothersHouseDoor));
            Assert.That(
                GameSessionState.AlpineVillageArrival,
                Is.EqualTo(AlpineVillageArrivalKind.Default),
                "AlpineVillage must consume the return token exactly once.");
            Assert.That(
                GameSessionState.ConsumeAlpineVillageArrival(),
                Is.EqualTo(AlpineVillageArrivalKind.Default),
                "A second consumer must not see the mother's-door arrival.");

            Vector3 actualReturn =
                returnedVillage.Player.GameObject.transform.position;
            AssertVectorApproximately(
                actualReturn,
                returnedVillage.Plan.MothersHouseReturnPosition +
                Vector3.up * PlayerFactory.GroundedRootOffset,
                PositionTolerance,
                "The hero did not return to the plan-owned exterior dock.");

            Vector3 expectedFacing =
                returnedVillage.Plan.MothersHouse.Facing;
            expectedFacing.y = 0f;
            expectedFacing.Normalize();
            Vector3 actualFacing =
                returnedVillage.Player.GameObject.transform.forward;
            actualFacing.y = 0f;
            actualFacing.Normalize();
            Assert.That(
                Vector3.Dot(actualFacing, expectedFacing),
                Is.GreaterThan(0.995f),
                "Leaving the house must face the hero away from its leaf.");
            LogAssert.NoUnexpectedReceived();
        }

        private static void AssertDirectBootContract(
            MothersHouseInteriorRoot interior)
        {
            Assert.That(interior, Is.Not.Null);
            Assert.That(interior.Layout, Is.Not.Null);
            MothersHouseInteriorLayoutValidator.ValidateOrThrow(
                interior.Layout);
            Assert.That(interior.World, Is.Not.Null);
            Assert.That(interior.World.Registry, Is.Not.Null);
            Assert.That(interior.Room, Is.SameAs(interior.World.Root));
            Assert.That(interior.Player, Is.Not.Null);
            Assert.That(interior.Player.GameObject, Is.Not.Null);
            Assert.That(interior.Exit, Is.Not.Null);
            Assert.That(
                interior.Exit.PromptKey,
                Is.EqualTo("interaction.exit_mothers_house"));
            Assert.That(interior.Inventory, Is.Not.Null);
            Assert.That(interior.Journal, Is.Not.Null);
            Assert.That(interior.PauseMenu, Is.Not.Null);
            Assert.That(interior.InteractionPrompt, Is.Not.Null);
            Assert.That(interior.IntoxicationStatus, Is.Not.Null);
            Assert.That(interior.Soundscape, Is.Not.Null);
            Assert.That(interior.Atmosphere, Is.Not.Null);

            int blockingFixtureCount = interior.Layout.Fixtures.Count(
                fixture => fixture.BlocksMovement);
            Assert.That(
                interior.World.GameplayColliders,
                Has.Count.EqualTo(6 + blockingFixtureCount));
            Assert.That(
                interior.World.GameplayColliders.All(
                    collider =>
                        collider != null &&
                        collider.enabled &&
                        collider.transform.IsChildOf(
                            interior.World.CollisionRoot)),
                Is.True);
            Assert.That(
                interior.World.Registry.GetComponentsInChildren<Collider>(
                    true).All(collider => !collider.enabled),
                Is.True,
                "Imported art must not own gameplay collision.");
            AssertBlockingFixtureCollider(
                interior,
                MothersHouseInteriorFixtureKind.Cupboard);
            AssertBlockingFixtureCollider(
                interior,
                MothersHouseInteriorFixtureKind.YarnBasket);
            AssertBlockingFixtureCollider(
                interior,
                MothersHouseInteriorFixtureKind.FloorLamp);
            AssertSouthDoorAndSpawnContract(interior);

            PlayerDoorActionTarget exitAction =
                interior.Exit.GetComponent<PlayerDoorActionTarget>();
            Assert.That(exitAction, Is.Not.Null);
            Assert.That(exitAction.IsConfigured, Is.True);
            Assert.That(
                CountExactRoots(
                    SceneManager.GetActiveScene(),
                    InteriorRootName),
                Is.EqualTo(1));
        }

        private static void AssertSouthDoorAndSpawnContract(
            MothersHouseInteriorRoot interior)
        {
            MothersHouseInteriorLayoutPlan layout = interior.Layout;
            Assert.That(
                Mathf.Abs(
                    layout.EntryPosition.z - layout.RoomBounds.yMin),
                Is.LessThan(0.2f),
                "The entrance must be cut into the south wall.");
            Assert.That(
                layout.EntryPosition.x,
                Is.EqualTo(layout.FireplacePosition.x).Within(0.001f),
                "The entrance must stand directly opposite the hearth.");
            AssertVectorApproximately(
                interior.World.EntryAnchor.position,
                interior.World.Root.TransformPoint(layout.EntryPosition),
                0.001f,
                "The imported entrance anchor drifted from the south door.");
            AssertVectorApproximately(
                interior.World.SpawnAnchor.position,
                interior.World.Root.TransformPoint(
                    MothersHouseInteriorLayoutPlanner.SpawnAnchorPosition),
                0.001f,
                "The imported spawn anchor drifted from the south door.");
            AssertVectorApproximately(
                interior.World.ExitAnchor.position,
                interior.World.Root.TransformPoint(
                    MothersHouseInteriorLayoutPlanner.ExitAnchorPosition),
                0.001f,
                "The imported exit anchor drifted from the south door.");
            AssertVectorApproximately(
                interior.Player.GameObject.transform.position,
                interior.transform.TransformPoint(layout.PlayerSpawn),
                PositionTolerance,
                "The hero must appear just inside the south entrance.");
            Assert.That(
                Vector3.Dot(
                    interior.Player.GameObject.transform.forward,
                    interior.World.Root.TransformDirection(Vector3.forward)),
                Is.GreaterThan(0.995f),
                "The arriving hero must face north toward the fireplace.");

            string[] requiredWallColliders =
            {
                "East Wall",
                "South Wall West of Door",
                "South Wall East of Door"
            };
            for (int index = 0;
                 index < requiredWallColliders.Length;
                 index++)
            {
                string name = requiredWallColliders[index];
                Assert.That(
                    interior.World.GameplayColliders.Count(
                        collider => collider.gameObject.name == name),
                    Is.EqualTo(1),
                    $"The room collision is missing '{name}'.");
            }

            PlayerDoorActionTarget action =
                interior.Exit.GetComponent<PlayerDoorActionTarget>();
            Vector3 expectedDock = interior.World.Root.TransformPoint(
                new Vector3(
                    layout.ExitPosition.x,
                    PlayerFactory.GroundedRootOffset,
                    layout.WalkableBounds.yMin +
                    PlayerDoorActionPlan.DockBoundaryClearance));
            AssertVectorApproximately(
                action.Plan.EntryRootPosition,
                expectedDock,
                0.001f,
                "The exit action must dock inside the south threshold.");
            Assert.That(
                Vector3.Dot(
                    action.Plan.EntryFacingDirection,
                    interior.World.Root.TransformDirection(Vector3.back)),
                Is.GreaterThan(0.999f),
                "Using the exit must face the hero south toward the door.");
        }

        private static void AssertExactKettleContract(
            MothersHouseInteriorRoot interior)
        {
            MothersHouseKettleProp kettle = interior.Kettle;
            Assert.That(kettle, Is.Not.Null);
            Assert.That(kettle.SourceInstance, Is.Not.Null);
            Assert.That(
                kettle.SourceDesignId,
                Is.EqualTo(CityPedestrianResources.KettleHatDesignId));
            Assert.That(kettle.UniformScale, Is.GreaterThan(0f));
            AssertVectorApproximately(
                kettle.transform.localScale,
                Vector3.one * kettle.UniformScale,
                0.0001f,
                "Only a uniform wrapper scale may resize the source kettle.");

            Transform teapotDock = interior.World.TeapotDockAnchor;
            Transform tabletop = interior.World.TabletopAnchor;
            Quaternion relativeDockRotation =
                Quaternion.Inverse(
                    interior.World.Registry.ModelRoot.rotation) *
                teapotDock.rotation;
            Assert.That(
                Quaternion.Angle(
                    Quaternion.identity,
                    relativeDockRotation),
                Is.LessThanOrEqualTo(UprightToleranceDegrees),
                "teapot_dock must keep its authored identity rotation.");
            Assert.That(
                Quaternion.Angle(
                    Quaternion.identity,
                    kettle.transform.localRotation),
                Is.LessThanOrEqualTo(UprightToleranceDegrees),
                "The table kettle wrapper must inherit the upright dock pose.");

            GameObject sourcePrefab = Resources.Load<GameObject>(
                CityPedestrianResources.KettleHatPrefabResourcePath);
            Assert.That(sourcePrefab, Is.Not.Null);
            CityPedestrianAssetRegistry prefabRegistry =
                sourcePrefab.GetComponent<CityPedestrianAssetRegistry>();
            CityPedestrianAssetRegistry liveRegistry =
                kettle.SourceInstance.GetComponent<
                    CityPedestrianAssetRegistry>();
            Assert.That(prefabRegistry, Is.Not.Null);
            Assert.That(liveRegistry, Is.Not.Null);
            Assert.That(
                liveRegistry.DesignId,
                Is.EqualTo(prefabRegistry.DesignId));
            Assert.That(liveRegistry.DetailAtlas, Is.Not.Null);
            Assert.That(
                liveRegistry.DetailAtlas,
                Is.SameAs(prefabRegistry.DetailAtlas),
                "The table prop must keep the NPC's detail atlas asset.");

            Renderer[] allLiveRenderers =
                kettle.SourceInstance.GetComponentsInChildren<Renderer>(true);
            Renderer[] enabledLiveRenderers = allLiveRenderers
                .Where(renderer => renderer.enabled)
                .ToArray();
            Assert.That(enabledLiveRenderers, Has.Length.EqualTo(10));
            Assert.That(
                enabledLiveRenderers
                    .Select(renderer => renderer.gameObject.name)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray(),
                Is.EqualTo(
                    KettleRendererNames.OrderBy(
                        name => name,
                        StringComparer.Ordinal).ToArray()));
            Assert.That(
                kettle.VisibleRenderers.Count,
                Is.EqualTo(10));
            Assert.That(
                kettle.VisibleRenderers.All(
                    renderer =>
                        renderer != null &&
                        renderer.enabled &&
                        enabledLiveRenderers.Contains(renderer)),
                Is.True);

            Bounds visibleBounds = CombineBounds(enabledLiveRenderers);
            Vector3 visibleBottomCenter = new Vector3(
                visibleBounds.center.x,
                visibleBounds.min.y,
                visibleBounds.center.z);
            AssertVectorApproximately(
                visibleBottomCenter,
                teapotDock.position,
                KettleDockTolerance,
                "The visible NPC kettle must sit bottom-center on teapot_dock.");
            Assert.That(
                visibleBounds.min.y,
                Is.GreaterThanOrEqualTo(
                    tabletop.position.y - KettleDockTolerance),
                "The kettle renderers must not overlap the tabletop.");
            Assert.That(
                visibleBounds.min.y - tabletop.position.y,
                Is.InRange(-KettleDockTolerance, 0.08f),
                "The kettle must remain seated on the planned tabletop dock.");

            for (int index = 0;
                 index < KettleRendererNames.Length;
                 index++)
            {
                string rendererName = KettleRendererNames[index];
                Renderer live = FindUniqueRenderer(
                    allLiveRenderers,
                    rendererName,
                    "live table kettle");
                Renderer source = FindUniqueRenderer(
                    sourcePrefab.GetComponentsInChildren<Renderer>(true),
                    rendererName,
                    "Kettle Hat source prefab");
                var liveSkinned = live as SkinnedMeshRenderer;
                var sourceSkinned = source as SkinnedMeshRenderer;
                Assert.That(liveSkinned, Is.Not.Null, rendererName);
                Assert.That(sourceSkinned, Is.Not.Null, rendererName);
                Assert.That(
                    liveSkinned.sharedMesh,
                    Is.SameAs(sourceSkinned.sharedMesh),
                    $"'{rendererName}' must use the source NPC mesh asset.");
                AssertSharedMaterials(live, source, rendererName);
            }

            CityPedestrianRendererBinding[] atlasBindings =
                liveRegistry.RendererBindings
                    .Where(binding =>
                        binding != null &&
                        binding.UsesDetailAtlas &&
                        KettleRendererNames.Contains(
                            binding.RendererName,
                            StringComparer.Ordinal))
                    .ToArray();
            Assert.That(
                atlasBindings,
                Is.Not.Empty,
                "At least one visible kettle part must sample its source atlas.");
            int baseMapId = Shader.PropertyToID("_BaseMap");
            var properties = new MaterialPropertyBlock();
            for (int index = 0; index < atlasBindings.Length; index++)
            {
                properties.Clear();
                atlasBindings[index].Renderer.GetPropertyBlock(properties);
                Assert.That(
                    properties.GetTexture(baseMapId),
                    Is.SameAs(liveRegistry.DetailAtlas),
                    $"'{atlasBindings[index].RendererName}' lost the NPC atlas.");
            }

            Animator[] animators = kettle.SourceInstance
                .GetComponentsInChildren<Animator>(true);
            Assert.That(animators, Is.Not.Empty);
            Assert.That(animators.All(animator => !animator.enabled), Is.True);
            Assert.That(
                kettle.GetComponentsInChildren<Collider>(true)
                    .All(collider => !collider.enabled),
                Is.True,
                "The table kettle must not add movement collision.");
            Assert.That(
                kettle.GetComponentsInChildren<CityKettleHatBoilEffect>(true)
                    .All(effect => !effect.enabled),
                Is.True,
                "The NPC-only boiling effect must stay disabled indoors.");
            Assert.That(
                kettle.GetComponentsInChildren<ParticleSystem>(true)
                    .All(particles => !particles.isPlaying),
                Is.True,
                "The table kettle must not vent the NPC's steam plume.");
        }

        private static void AssertPositiveAtlasContract(
            MothersHouseInteriorAssetRegistry registry)
        {
            MothersHouseInteriorAtlasContract atlas = registry.PositiveAtlas;
            Assert.That(atlas, Is.Not.Null);
            Assert.That(atlas.IsConfigured, Is.True);
            Texture2D expectedTexture = Resources.Load<Texture2D>(
                MothersHouseInteriorAssetRegistry.PositiveAtlasResourcePath);
            Assert.That(expectedTexture, Is.Not.Null);
            Assert.That(atlas.Texture, Is.SameAs(expectedTexture));
            Assert.That(
                atlas.ResourcePath,
                Is.EqualTo(
                    MothersHouseInteriorAssetRegistry
                        .PositiveAtlasResourcePath));
            Assert.That(
                atlas.Width,
                Is.EqualTo(
                    MothersHouseInteriorAssetRegistry.PositiveAtlasWidth));
            Assert.That(
                atlas.Height,
                Is.EqualTo(
                    MothersHouseInteriorAssetRegistry.PositiveAtlasHeight));
            Assert.That(atlas.Texture.width, Is.EqualTo(atlas.Width));
            Assert.That(atlas.Texture.height, Is.EqualTo(atlas.Height));
            Assert.That(
                atlas.Columns,
                Is.EqualTo(
                    MothersHouseInteriorAssetRegistry.PositiveAtlasColumns));
            Assert.That(
                atlas.Rows,
                Is.EqualTo(
                    MothersHouseInteriorAssetRegistry.PositiveAtlasRows));
            Assert.That(
                atlas.InsetPixels,
                Is.EqualTo(
                    MothersHouseInteriorAssetRegistry
                        .PositiveAtlasInsetPixels));
            Assert.That(atlas.SRgb, Is.True);
            Assert.That(atlas.Mipmaps, Is.False);
            Assert.That(atlas.Uncompressed, Is.True);
            Assert.That(atlas.WrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(atlas.FilterMode, Is.EqualTo(FilterMode.Bilinear));
            Assert.That(
                atlas.Texture.wrapMode,
                Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(
                atlas.Texture.filterMode,
                Is.EqualTo(FilterMode.Bilinear));
            Assert.That(atlas.Texture.mipmapCount, Is.EqualTo(1));
            Assert.That(
                atlas.Cells.Count,
                Is.EqualTo(PositiveAtlasSheets.Length));

            for (int index = 0;
                 index < PositiveAtlasSheets.Length;
                 index++)
            {
                string sheet = PositiveAtlasSheets[index];
                Assert.That(
                    atlas.TryGetCell(
                        sheet,
                        out MothersHouseInteriorAtlasCell cell),
                    Is.True,
                    $"Positive atlas has no '{sheet}' cell.");
                Assert.That(cell.Column, Is.EqualTo(index % 4), sheet);
                Assert.That(cell.Row, Is.EqualTo(3 - index / 4), sheet);
            }

            Texture[] forbiddenTextures =
                ForbiddenRoomTextureResourcePaths
                    .Select(path => Resources.Load<Texture2D>(path))
                    .Where(texture => texture != null)
                    .Cast<Texture>()
                    .ToArray();
            Assert.That(
                forbiddenTextures.Contains(atlas.Texture),
                Is.False);

            MothersHouseInteriorPartBinding[] parts = registry.Parts
                .Where(part => part != null)
                .ToArray();
            Renderer[] roomRenderers = registry
                .GetComponentsInChildren<Renderer>(true);
            Assert.That(parts, Has.Length.EqualTo(registry.Parts.Count));
            Assert.That(roomRenderers, Has.Length.EqualTo(parts.Length));
            Assert.That(
                parts.Select(part => part.Renderer.sharedMaterial)
                    .Distinct()
                    .Count(),
                Is.EqualTo(2),
                "The room must retain one shared lit and one shared " +
                "emission material.");

            int baseMapId = Shader.PropertyToID("_BaseMap");
            int baseMapTransformId = Shader.PropertyToID("_BaseMap_ST");
            int baseColorId = Shader.PropertyToID("_BaseColor");
            var properties = new MaterialPropertyBlock();
            for (int index = 0; index < parts.Length; index++)
            {
                MothersHouseInteriorPartBinding part = parts[index];
                Assert.That(part.Renderer, Is.Not.Null, part.SourceName);
                Assert.That(
                    roomRenderers.Contains(part.Renderer),
                    Is.True,
                    part.SourceName);
                part.Renderer.GetPropertyBlock(properties);
                Texture boundTexture = properties.GetTexture(baseMapId);
                Assert.That(
                    boundTexture,
                    Is.SameAs(atlas.Texture),
                    $"'{part.SourceName}' does not use the room atlas.");
                Assert.That(
                    forbiddenTextures.Contains(boundTexture),
                    Is.False,
                    $"'{part.SourceName}' reused a Home/City texture.");
                Assert.That(
                    Vector4.Distance(
                        properties.GetVector(baseMapTransformId),
                        part.BaseMapTransform),
                    Is.LessThanOrEqualTo(0.00001f),
                    $"'{part.SourceName}' lost its serialized atlas ST.");
                Assert.That(
                    atlas.TryGetInsetCellBounds(
                        part.Sheet,
                        out Rect cellBounds),
                    Is.True,
                    part.Sheet);
                AssertRectContains(
                    cellBounds,
                    part.TransformedUvBounds,
                    0.00001f,
                    $"'{part.SourceName}' samples outside '{part.Sheet}'.");
                AssertRectApproximately(
                    part.TransformedUvBounds,
                    cellBounds,
                    0.00001f,
                    $"'{part.SourceName}' UV bounds were not normalized.");

                Color expectedTint = Color.white;
                if (part.Emissive ||
                    string.Equals(
                        part.Sheet,
                        "Fire",
                        StringComparison.Ordinal))
                {
                    expectedTint = part.Tint;
                }
                else if (string.Equals(
                             part.Sheet,
                             "Glass",
                             StringComparison.Ordinal))
                {
                    expectedTint.a = part.Tint.a;
                }
                AssertColorApproximately(
                    properties.GetColor(baseColorId),
                    expectedTint,
                    0.0001f,
                    $"'{part.SourceName}' does not use its clean tint.");

                Material[] sharedMaterials = part.Renderer.sharedMaterials;
                for (int materialIndex = 0;
                     materialIndex < sharedMaterials.Length;
                     materialIndex++)
                {
                    Material material = sharedMaterials[materialIndex];
                    Texture materialTexture =
                        material != null && material.HasProperty(baseMapId)
                            ? material.GetTexture(baseMapId)
                            : null;
                    Assert.That(
                        forbiddenTextures.Contains(materialTexture),
                        Is.False,
                        $"'{part.SourceName}' shared material retained a " +
                        "Home/City texture.");
                }

                properties.Clear();
            }
        }

        private static void AssertBlockingFixtureCollider(
            MothersHouseInteriorRoot interior,
            MothersHouseInteriorFixtureKind fixtureKind)
        {
            Assert.That(
                interior.Layout.TryGetFixture(
                    fixtureKind,
                    out MothersHouseInteriorFixturePlan fixture),
                Is.True,
                $"The layout is missing the {fixtureKind} fixture.");
            Assert.That(
                fixture.BlocksMovement,
                Is.True,
                $"The {fixtureKind} fixture must block movement.");

            Collider[] matches = interior.World.GameplayColliders
                .Where(collider =>
                    collider != null &&
                    string.Equals(
                        collider.gameObject.name,
                        $"Fixture {fixture.Id}",
                        StringComparison.Ordinal))
                .ToArray();
            Assert.That(
                matches,
                Has.Length.EqualTo(1),
                $"The {fixtureKind} fixture must own exactly one proxy.");
            var box = matches[0] as BoxCollider;
            Assert.That(box, Is.Not.Null, fixtureKind.ToString());
            AssertVectorApproximately(
                box.center,
                fixture.Center,
                0.0001f,
                $"The {fixtureKind} collider center differs from its plan.");
            AssertVectorApproximately(
                box.size,
                fixture.Size,
                0.0001f,
                $"The {fixtureKind} collider size differs from its plan.");
        }

        private static void AssertFixedCameraAndLightContract(
            MothersHouseInteriorRoot interior)
        {
            Assert.That(interior.FixedCamera, Is.Not.Null);
            Assert.That(interior.FixedCamera.IsInitialized, Is.True);
            Assert.That(
                interior.GetComponentsInChildren<HomeFixedCameraController>(
                    true),
                Has.Length.EqualTo(1),
                "The room owns one fixed gameplay shot, not a camera network.");
            HomeCameraShot shot = interior.Layout.CameraShot;
            Assert.That(
                interior.FixedCamera.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.MainRoom));
            AssertVectorApproximately(
                interior.FixedCamera.ActiveShot.Position,
                shot.Position,
                0.001f,
                "Active fixed-camera position differs from the layout.");
            Assert.That(
                Quaternion.Angle(
                    interior.FixedCamera.ActiveShot.Rotation,
                    shot.Rotation),
                Is.LessThan(0.01f));
            Assert.That(
                interior.FixedCamera.ActiveShot.FieldOfView,
                Is.EqualTo(shot.FieldOfView).Within(0.001f));
            Assert.That(interior.CameraFollow.FixedPoseActive, Is.True);
            AssertVectorApproximately(
                interior.CameraFollow.FixedBasePosition,
                shot.Position,
                0.001f,
                "The player camera did not take the one planned fixed pose.");
            Assert.That(
                Quaternion.Angle(
                    interior.CameraFollow.FixedBaseRotation,
                    shot.Rotation),
                Is.LessThan(0.01f));
            Assert.That(
                interior.CameraFollow.FixedBaseFieldOfView,
                Is.EqualTo(shot.FieldOfView).Within(0.001f));

            MothersHouseInteriorAtmosphere atmosphere = interior.Atmosphere;
            Assert.That(atmosphere.FireLight, Is.Not.Null);
            Assert.That(atmosphere.FireLight.enabled, Is.True);
            Assert.That(atmosphere.FireLight.type, Is.EqualTo(LightType.Point));
            AssertVectorApproximately(
                atmosphere.FireLight.transform.position,
                interior.World.FireLightAnchor.position,
                0.001f,
                "The fire light moved off its authored hearth anchor.");
            Assert.That(
                atmosphere.WindowLights,
                Has.Length.EqualTo(
                    MothersHouseInteriorAtmosphere.WindowLightCount));
            Assert.That(
                MothersHouseInteriorAtmosphere.PracticalLightCount,
                Is.EqualTo(4));
            Assert.That(
                atmosphere.GetComponentsInChildren<Light>(true),
                Has.Length.EqualTo(4));
            Assert.That(
                atmosphere.transform.Find("Warm Ceiling Fill"),
                Is.Null,
                "The scene must not return to an invisible global fill.");
            Assert.That(
                MothersHouseInteriorAtmosphere.LampLightCount,
                Is.EqualTo(1));
            Assert.That(atmosphere.LampLights, Has.Length.EqualTo(1));
            Assert.That(
                atmosphere.LampLights[0],
                Is.SameAs(atmosphere.FloorLampLight));
            Assert.That(
                atmosphere.LampLights.All(
                    light =>
                        light != null &&
                        light.enabled &&
                        light.type == LightType.Spot &&
                        light.shadows == LightShadows.None &&
                        light.intensity < atmosphere.FireLight.intensity &&
                        light.range < interior.Layout.RoomSize.x &&
                        Vector3.Dot(
                            light.transform.forward,
                            Vector3.down) > 0.8f),
                Is.True,
                "Visible lamps must own restrained local pools of light.");
            Assert.That(
                atmosphere.FloorLampLight.spotAngle,
                Is.EqualTo(
                    MothersHouseInteriorAtmosphere.FloorLampSpotAngle)
                    .Within(0.001f));
            AssertVectorApproximately(
                atmosphere.FloorLampLight.transform.position,
                interior.World.FloorLampLightAnchor.position,
                0.001f,
                "The floor-lamp light moved off its visible shade.");
            AssertNoTableLamp(interior);
            Assert.That(
                RenderSettings.ambientLight.maxColorComponent,
                Is.LessThanOrEqualTo(0.171f),
                "Ambient illumination must stay below the motivated lights.");
            Assert.That(
                atmosphere.WindowLights.All(
                    light =>
                        light != null &&
                        light.enabled &&
                        light.type == LightType.Spot &&
                        light.shadows == LightShadows.None &&
                        light.intensity < atmosphere.FireLight.intensity),
                Is.True,
                "Two weak window spills must remain subordinate to the fire.");
            AssertVectorApproximately(
                atmosphere.WindowLights[0].transform.position,
                interior.transform.TransformPoint(
                    interior.Layout.WestWindowPosition),
                0.001f,
                "West window light moved off its planned opening.");
            AssertVectorApproximately(
                atmosphere.WindowLights[1].transform.position,
                interior.transform.TransformPoint(
                    interior.Layout.EastWindowPosition),
                0.001f,
                "East window light moved off its planned opening.");
            Assert.That(atmosphere.FireFlicker, Is.Not.Null);
            Assert.That(
                atmosphere.FireFlicker.FireLight,
                Is.SameAs(atmosphere.FireLight));
            Assert.That(atmosphere.FireFlicker.Flames.Count, Is.EqualTo(2));
            Assert.That(atmosphere.FireFlicker.Embers, Is.Not.Null);
            Assert.That(atmosphere.FireCrackleSource, Is.Not.Null);
            Assert.That(atmosphere.FireCrackleSource.loop, Is.True);
            Assert.That(atmosphere.FireCrackleSource.clip, Is.Not.Null);
            Assert.That(RenderSettings.fog, Is.False);
        }

        private static void AssertNoTableLamp(MothersHouseInteriorRoot interior)
        {
            bool hasAnchor = interior.World.Registry.TryGetAnchor(
                "table_lamp_light", out _);
            Assert.That(hasAnchor, Is.False, "Removed table-lamp anchor.");
            bool hasPart = interior.World.Registry.Parts.Any(part =>
                part != null && (ContainsTableLampToken(part.SourceName) ||
                                 ContainsTableLampToken(part.Role)));
            Assert.That(hasPart, Is.False, "Removed DRESS_TableLamp part.");
            bool hasRuntimeObject = interior
                .GetComponentsInChildren<Transform>(true)
                .Any(item => ContainsTableLampToken(item.gameObject.name));
            Assert.That(hasRuntimeObject, Is.False,
                "Removed TableLamp runtime object.");
        }
        private static bool ContainsTableLampToken(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   (value.IndexOf("tablelamp", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    value.IndexOf("table_lamp", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    value.IndexOf("table lamp", StringComparison.OrdinalIgnoreCase) >= 0);
        }
        private static void AssertCalmSoundscapeContract(
            MothersHouseInteriorRoot interior)
        {
            MothersHouseInteriorSoundscape soundscape = interior.Soundscape;
            Assert.That(soundscape, Is.Not.Null);
            Assert.That(soundscape.IsInitialized, Is.True);
            Assert.That(
                soundscape.DeterministicSeed,
                Is.EqualTo(GameSessionState.CitySeed));
            Assert.That(
                soundscape.OwnedSourceCount,
                Is.EqualTo(
                    MothersHouseInteriorSoundscape.MaximumOwnedSourceCount));
            Assert.That(
                soundscape.OwnedRuntimeClipCount,
                Is.EqualTo(
                    MothersHouseInteriorSoundscape.MaximumOwnedSourceCount));

            AudioSource[] sources = soundscape
                .GetComponentsInChildren<AudioSource>(true);
            Assert.That(
                sources,
                Has.Length.EqualTo(
                    MothersHouseInteriorSoundscape.MaximumOwnedSourceCount));
            Assert.That(
                sources,
                Is.EquivalentTo(new[]
                {
                    soundscape.MuffledWindSource,
                    soundscape.ClockSource,
                    soundscape.WoodSettleSource
                }));
            Assert.That(
                sources.Count(source => source.loop),
                Is.EqualTo(
                    MothersHouseInteriorSoundscape.LoopingSourceCount));
            Assert.That(
                sources.Count(source => !source.loop),
                Is.EqualTo(
                    MothersHouseInteriorSoundscape.ScheduledSourceCount));

            AudioClip[] clips =
            {
                soundscape.MuffledWindClip,
                soundscape.ClockClip,
                soundscape.WoodSettleClip
            };
            Assert.That(clips.All(clip => clip != null), Is.True);
            Assert.That(clips.Distinct().Count(), Is.EqualTo(3));
            AssertRuntimeClipMetadata(
                soundscape.MuffledWindClip,
                MothersHouseInteriorSoundSynthesis.WindLoopDuration);
            AssertRuntimeClipMetadata(
                soundscape.ClockClip,
                MothersHouseInteriorSoundSynthesis.ClockLoopDuration);
            AssertRuntimeClipMetadata(
                soundscape.WoodSettleClip,
                MothersHouseInteriorSoundSynthesis.WoodSettleDuration);

            AssertCalmSpatialSource(
                soundscape.MuffledWindSource,
                soundscape.MuffledWindClip,
                true,
                MothersHouseInteriorSoundscape.MuffledWindVolume,
                MothersHouseInteriorSoundscape.MuffledWindMinimumDistance,
                MothersHouseInteriorSoundscape.MuffledWindMaximumDistance,
                soundscape.MuffledWindLowPass,
                MothersHouseInteriorSoundscape.MuffledWindLowPassCutoff);
            AssertCalmSpatialSource(
                soundscape.ClockSource,
                soundscape.ClockClip,
                true,
                MothersHouseInteriorSoundscape.ClockVolume,
                MothersHouseInteriorSoundscape.ClockMinimumDistance,
                MothersHouseInteriorSoundscape.ClockMaximumDistance,
                soundscape.ClockLowPass,
                MothersHouseInteriorSoundscape.ClockLowPassCutoff);
            AssertCalmSpatialSource(
                soundscape.WoodSettleSource,
                soundscape.WoodSettleClip,
                false,
                MothersHouseInteriorSoundscape.WoodSettleVolume,
                MothersHouseInteriorSoundscape.WoodSettleMinimumDistance,
                MothersHouseInteriorSoundscape.WoodSettleMaximumDistance,
                soundscape.WoodSettleLowPass,
                MothersHouseInteriorSoundscape.WoodSettleLowPassCutoff);
            Assert.That(soundscape.WoodSettleSource.isPlaying, Is.False);

            Assert.That(
                soundscape.MuffledWindSource.outputAudioMixerGroup,
                Is.SameAs(GameAudioMixer.AmbienceBedsGroup));
            Assert.That(
                soundscape.ClockSource.outputAudioMixerGroup,
                Is.SameAs(GameAudioMixer.AmbienceDetailsGroup));
            Assert.That(
                soundscape.WoodSettleSource.outputAudioMixerGroup,
                Is.SameAs(GameAudioMixer.AmbienceDetailsGroup));
            Assert.That(
                sources.All(source =>
                    source.outputAudioMixerGroup !=
                    GameAudioMixer.MusicGroup),
                Is.True);

            Vector3 expectedWindowPosition = interior.transform.TransformPoint(
                Vector3.Lerp(
                    interior.Layout.WestWindowPosition,
                    interior.Layout.EastWindowPosition,
                    0.5f));
            AssertVectorApproximately(
                soundscape.WindowWindPosition,
                expectedWindowPosition,
                0.001f,
                "The muffled gale must stay between the visible windows.");
            AssertVectorApproximately(
                soundscape.MuffledWindSource.transform.position,
                soundscape.WindowWindPosition,
                0.001f,
                "The wind source moved off its window-frame owner.");

            Renderer clock = FindVisibleSoundOwner(
                interior.World.Registry,
                MothersHouseInteriorSoundscape.ClockOwnerRole);
            Renderer cupboard = FindVisibleSoundOwner(
                interior.World.Registry,
                MothersHouseInteriorSoundscape.WoodSettleOwnerRole);
            AssertVectorApproximately(
                soundscape.ClockPosition,
                clock.bounds.center,
                0.001f,
                "The clock sound must come from the visible wall clock.");
            AssertVectorApproximately(
                soundscape.ClockSource.transform.position,
                soundscape.ClockPosition,
                0.001f,
                "The clock source moved off its visible owner.");
            AssertVectorApproximately(
                soundscape.WoodSettlePosition,
                cupboard.bounds.center,
                0.001f,
                "The rare timber sound must come from the old cupboard.");
            AssertVectorApproximately(
                soundscape.WoodSettleSource.transform.position,
                soundscape.WoodSettlePosition,
                0.001f,
                "The timber source moved off its visible owner.");

            Vector3 listenerPosition = interior.transform.TransformPoint(
                interior.Layout.CameraShot.Position);
            Assert.That(
                Vector3.Distance(
                    listenerPosition,
                    soundscape.WindowWindPosition),
                Is.LessThan(soundscape.MuffledWindSource.maxDistance),
                "The fixed-camera listener must hear the muffled wind.");
            Assert.That(
                Vector3.Distance(listenerPosition, soundscape.ClockPosition),
                Is.LessThan(soundscape.ClockSource.maxDistance),
                "The fixed-camera listener must hear the quiet clock.");
            Assert.That(
                Vector3.Distance(
                    listenerPosition,
                    soundscape.WoodSettlePosition),
                Is.LessThan(soundscape.WoodSettleSource.maxDistance),
                "The fixed-camera listener must hear the rare timber settle.");

            Assert.That(soundscape.WoodSettleSequence, Is.Zero);
            Assert.That(soundscape.HasPlayedWoodSettle, Is.False);
            Assert.That(
                soundscape.SecondsUntilNextWoodSettle,
                Is.GreaterThan(0f).And.LessThanOrEqualTo(
                    MothersHouseInteriorSoundSchedule
                        .MaximumWoodSettleDelaySeconds));
            for (int sequence = 0; sequence < 6; sequence++)
            {
                MothersHouseWoodSettleCue cue =
                    MothersHouseInteriorSoundSchedule.GetWoodSettleCue(
                        soundscape.DeterministicSeed,
                        sequence);
                MothersHouseWoodSettleCue repeat =
                    MothersHouseInteriorSoundSchedule.GetWoodSettleCue(
                        soundscape.DeterministicSeed,
                        sequence);
                Assert.That(
                    cue.DelaySeconds,
                    Is.InRange(
                        MothersHouseInteriorSoundSchedule
                            .MinimumWoodSettleDelaySeconds,
                        MothersHouseInteriorSoundSchedule
                            .MaximumWoodSettleDelaySeconds));
                Assert.That(
                    cue.Pitch,
                    Is.InRange(
                        MothersHouseInteriorSoundSchedule
                            .MinimumWoodSettlePitch,
                        MothersHouseInteriorSoundSchedule
                            .MaximumWoodSettlePitch));
                Assert.That(
                    cue.VolumeScale,
                    Is.InRange(
                        MothersHouseInteriorSoundSchedule
                            .MinimumWoodSettleVolumeScale,
                        MothersHouseInteriorSoundSchedule
                            .MaximumWoodSettleVolumeScale));
                Assert.That(cue.DelaySeconds, Is.EqualTo(repeat.DelaySeconds));
                Assert.That(cue.Pitch, Is.EqualTo(repeat.Pitch));
                Assert.That(cue.VolumeScale, Is.EqualTo(repeat.VolumeScale));
            }

            string[] forbiddenSourceWords = { "music", "voice", "kettle" };
            Assert.That(
                sources.All(source => forbiddenSourceWords.All(word =>
                    source.name.IndexOf(
                        word,
                        StringComparison.OrdinalIgnoreCase) < 0 &&
                    source.clip.name.IndexOf(
                        word,
                        StringComparison.OrdinalIgnoreCase) < 0)),
                Is.True,
                "The calm room owns no music, voice or kettle audio source.");
            Assert.That(
                interior.Kettle.GetComponentsInChildren<AudioSource>(true)
                    .All(source => !source.enabled),
                Is.True,
                "The reused NPC kettle must stay silent on the table.");
        }

        private static void AssertCalmSpatialSource(
            AudioSource source,
            AudioClip clip,
            bool loop,
            float volume,
            float minimumDistance,
            float maximumDistance,
            AudioLowPassFilter lowPass,
            float cutoff)
        {
            Assert.That(source, Is.Not.Null);
            Assert.That(source.clip, Is.SameAs(clip));
            Assert.That(source.loop, Is.EqualTo(loop));
            Assert.That(source.playOnAwake, Is.False);
            Assert.That(source.spatialBlend, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(source.dopplerLevel, Is.Zero.Within(0.0001f));
            Assert.That(source.rolloffMode, Is.EqualTo(AudioRolloffMode.Linear));
            Assert.That(source.volume, Is.EqualTo(volume).Within(0.0001f));
            Assert.That(
                source.minDistance,
                Is.EqualTo(minimumDistance).Within(0.0001f));
            Assert.That(
                source.maxDistance,
                Is.EqualTo(maximumDistance).Within(0.0001f));
            Assert.That(lowPass, Is.Not.Null);
            Assert.That(
                source.GetComponent<AudioLowPassFilter>(),
                Is.SameAs(lowPass));
            Assert.That(lowPass.enabled, Is.True);
            Assert.That(
                lowPass.cutoffFrequency,
                Is.EqualTo(cutoff).Within(0.1f));
            Assert.That(
                lowPass.lowpassResonanceQ,
                Is.EqualTo(1f).Within(0.0001f));
        }

        private static void AssertRuntimeClipMetadata(
            AudioClip clip,
            float durationSeconds)
        {
            Assert.That(clip.channels, Is.EqualTo(1));
            Assert.That(
                clip.frequency,
                Is.EqualTo(MothersHouseInteriorSoundSynthesis.SampleRate));
            Assert.That(
                clip.samples,
                Is.EqualTo(Mathf.RoundToInt(
                    MothersHouseInteriorSoundSynthesis.SampleRate *
                    durationSeconds)));
        }

        private static Renderer FindVisibleSoundOwner(
            MothersHouseInteriorAssetRegistry registry,
            string role)
        {
            MothersHouseInteriorPartBinding[] matches = registry.Parts
                .Where(part =>
                    part != null &&
                    string.Equals(
                        part.Role,
                        role,
                        StringComparison.Ordinal) &&
                    part.Renderer != null)
                .ToArray();
            Assert.That(matches, Has.Length.EqualTo(1), role);
            Renderer renderer = matches[0].Renderer;
            Assert.That(renderer.enabled, Is.True, role);
            Assert.That(renderer.gameObject.activeInHierarchy, Is.True, role);
            return renderer;
        }

        private static Renderer FindUniqueRenderer(
            Renderer[] renderers,
            string name,
            string owner)
        {
            Renderer[] matches = renderers
                .Where(renderer =>
                    string.Equals(
                        renderer.gameObject.name,
                        name,
                        StringComparison.Ordinal))
                .ToArray();
            Assert.That(
                matches,
                Has.Length.EqualTo(1),
                $"{owner} renderer '{name}'.");
            return matches[0];
        }

        private static Bounds CombineBounds(Renderer[] renderers)
        {
            Assert.That(renderers, Is.Not.Empty);
            Bounds combined = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                combined.Encapsulate(renderers[index].bounds);
            }

            return combined;
        }

        private static void AssertSharedMaterials(
            Renderer live,
            Renderer source,
            string rendererName)
        {
            Material[] liveMaterials = live.sharedMaterials;
            Material[] sourceMaterials = source.sharedMaterials;
            Assert.That(
                liveMaterials,
                Has.Length.EqualTo(sourceMaterials.Length),
                rendererName);
            for (int index = 0; index < sourceMaterials.Length; index++)
            {
                Assert.That(
                    liveMaterials[index],
                    Is.SameAs(sourceMaterials[index]),
                    $"'{rendererName}' material slot {index} is not shared " +
                    "with the Kettle Hat source prefab.");
            }
        }

        private static void AssertDoorActionStarted(
            PlayerRuntime player,
            string description)
        {
            PlayerDoorActionController action =
                player.GameObject.GetComponent<PlayerDoorActionController>();
            Assert.That(action, Is.Not.Null, description);
            Assert.That(
                action.IsPlaying,
                Is.True,
                $"The {description} refused its configured dock.");
        }

        private static void PlacePlayerAtDoor(
            PlayerRuntime player,
            Component door)
        {
            PlayerDoorActionTarget action =
                door.GetComponent<PlayerDoorActionTarget>();
            Assert.That(action, Is.Not.Null, door.name);
            Assert.That(action.IsConfigured, Is.True, door.name);
            player.Motor.Teleport(action.Plan.EntryRootPosition);
            player.GameObject.transform.rotation = action.Plan.EntryRotation;
            Physics.SyncTransforms();
        }

        private static void AssertSceneIsStreamable(string sceneName)
        {
            Assert.That(
                Application.CanStreamedLevelBeLoaded(sceneName),
                Is.True,
                $"Scene '{sceneName}' must be enabled in Build Settings.");
        }

        private static IEnumerator LoadSceneAndWaitForRoot<T>(
            string sceneName,
            string exactRootName,
            Action<T> capture)
            where T : Component
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(
                sceneName,
                LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);
            yield return WaitUntil(
                () => operation.isDone,
                $"Scene '{sceneName}' did not load.");
            yield return WaitForLoadedRoot(
                sceneName,
                exactRootName,
                capture);
        }

        private static IEnumerator WaitForLoadedRoot<T>(
            string sceneName,
            string exactRootName,
            Action<T> capture)
            where T : Component
        {
            T found = null;
            yield return WaitUntil(
                () =>
                {
                    Scene scene = SceneManager.GetActiveScene();
                    found = FindExactRoot<T>(scene, exactRootName);
                    return scene.name == sceneName && found != null;
                },
                $"Scene '{sceneName}' did not create root " +
                $"'{exactRootName}'.");
            capture(found);
        }

        private static IEnumerator WaitUntil(
            Func<bool> predicate,
            string failureMessage)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!predicate() &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(predicate(), Is.True, failureMessage);
        }

        private static T FindExactRoot<T>(
            Scene scene,
            string exactRootName)
            where T : Component
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return null;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                if (roots[index].name == exactRootName)
                {
                    return roots[index].GetComponent<T>();
                }
            }

            return null;
        }

        private static int CountExactRoots(
            Scene scene,
            string exactRootName)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return 0;
            }

            return scene.GetRootGameObjects().Count(
                root => root.name == exactRootName);
        }

        private static void AssertRectContains(
            Rect outer,
            Rect inner,
            float tolerance,
            string message)
        {
            Assert.That(
                inner.xMin,
                Is.GreaterThanOrEqualTo(outer.xMin - tolerance),
                message);
            Assert.That(
                inner.xMax,
                Is.LessThanOrEqualTo(outer.xMax + tolerance),
                message);
            Assert.That(
                inner.yMin,
                Is.GreaterThanOrEqualTo(outer.yMin - tolerance),
                message);
            Assert.That(
                inner.yMax,
                Is.LessThanOrEqualTo(outer.yMax + tolerance),
                message);
        }

        private static void AssertRectApproximately(
            Rect actual,
            Rect expected,
            float tolerance,
            string message)
        {
            Assert.That(
                Mathf.Abs(actual.xMin - expected.xMin),
                Is.LessThanOrEqualTo(tolerance),
                message);
            Assert.That(
                Mathf.Abs(actual.xMax - expected.xMax),
                Is.LessThanOrEqualTo(tolerance),
                message);
            Assert.That(
                Mathf.Abs(actual.yMin - expected.yMin),
                Is.LessThanOrEqualTo(tolerance),
                message);
            Assert.That(
                Mathf.Abs(actual.yMax - expected.yMax),
                Is.LessThanOrEqualTo(tolerance),
                message);
        }

        private static void AssertColorApproximately(
            Color actual,
            Color expected,
            float tolerance,
            string message)
        {
            Assert.That(
                Mathf.Abs(actual.r - expected.r),
                Is.LessThanOrEqualTo(tolerance),
                message);
            Assert.That(
                Mathf.Abs(actual.g - expected.g),
                Is.LessThanOrEqualTo(tolerance),
                message);
            Assert.That(
                Mathf.Abs(actual.b - expected.b),
                Is.LessThanOrEqualTo(tolerance),
                message);
            Assert.That(
                Mathf.Abs(actual.a - expected.a),
                Is.LessThanOrEqualTo(tolerance),
                message);
        }

        private static void AssertVectorApproximately(
            Vector3 actual,
            Vector3 expected,
            float tolerance,
            string message)
        {
            Assert.That(
                Vector3.Distance(actual, expected),
                Is.LessThanOrEqualTo(tolerance),
                message + $" Expected {expected}, got {actual}.");
        }
    }
}
