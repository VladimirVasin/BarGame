using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Holds the joint between the Blender bar model and
    /// `BarInteriorLayoutPlan`.
    ///
    /// Everything else about the room can be judged by eye. This cannot:
    /// the model and the plan describe the SAME room from two files, and
    /// if they ever disagree the failure is silent - the bartender pours
    /// through a wall, or a booth stands where the plan says there is
    /// floor. So the size of the room and the position of every anchor
    /// are asserted against the planner, not merely against themselves.
    /// </summary>
    public sealed class BarModelContractTests
    {
        private const string ManifestPath = "Assets/Bar/Models/Bar3D.json";
        private const string ModelPath =
            "Assets/Bar/Models/BarInterior3D.fbx";
        private const string ServiceManifestPath =
            "Assets/Bar/Models/BarServiceProps3D.json";
        private const string ExteriorManifestPath =
            "Assets/Bar/Models/BarFacade3D.json";
        private const string ExteriorModelPath =
            "Assets/Bar/Models/BarFacade3D.fbx";
        private const float Tolerance = 0.01f;

        [Test]
        public void BarModel_ImportsAsAPassiveResourcePrefab()
        {
            BarManifest manifest = LoadManifest();

            Assert.That(manifest.design_id, Is.EqualTo("bar_interior_v3"));
            Assert.That(manifest.colliders, Is.False);
            Assert.That(manifest.lights, Is.False);
            Assert.That(manifest.cameras, Is.False);
            Assert.That(manifest.animation_count, Is.Zero);

            ModelImporter importer =
                AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            Assert.That(importer, Is.Not.Null, "the model did not import");
            Assert.That(
                importer.animationType,
                Is.EqualTo(ModelImporterAnimationType.None));
            Assert.That(importer.preserveHierarchy, Is.True);
            Assert.That(importer.optimizeGameObjects, Is.False);
            Assert.That(importer.importCameras, Is.False);
            Assert.That(importer.importLights, Is.False);
            Assert.That(
                importer.addCollider,
                Is.False,
                "collision is authored, not taken from the meshes");
            Assert.That(
                importer.materialImportMode,
                Is.EqualTo(ModelImporterMaterialImportMode.None),
                "materials from the FBX would break district tinting");
        }

        [Test]
        public void BarModels_AssignARecognizedSurfaceToEveryVisiblePart()
        {
            foreach (string path in new[]
                     {
                         ManifestPath,
                         ServiceManifestPath,
                     })
            {
                BarManifest manifest = LoadManifest(path);
                BarManifestPart[] visible = manifest.parts
                    .Where(part => !part.emissive)
                    .ToArray();
                Assert.That(visible, Is.Not.Empty);
                foreach (BarManifestPart part in visible)
                {
                    Assert.That(
                        string.IsNullOrWhiteSpace(part.sheet),
                        Is.False,
                        $"'{part.name}' in {path} has only a flat tint");
                    Assert.That(
                        BarSurfaceAppearance.TryResolveSheet(
                            part.sheet,
                            out _),
                        Is.True,
                        $"'{part.name}' names unknown sheet " +
                        $"'{part.sheet}'");
                }

                int distinctSheets = visible
                    .Select(part => part.sheet)
                    .Distinct(StringComparer.Ordinal)
                    .Count();
                int minimum = path == ManifestPath ? 12 : 5;
                Assert.That(
                    distinctSheets,
                    Is.GreaterThanOrEqualTo(minimum),
                    $"{path} collapses visibly different materials into " +
                    "too few texture families");
            }
        }

        [Test]
        public void BarSurfaceTextures_UseTheMeasuredRepeatImportContract()
        {
            foreach (BarSurfaceKind kind in
                     Enum.GetValues(typeof(BarSurfaceKind)))
            {
                HomeSurfaceRecipe recipe =
                    BarSurfaceAppearance.GetRecipe(kind);
                string assetPath =
                    $"Assets/Resources/{recipe.ResourcePath}.png";
                TextureImporter importer =
                    AssetImporter.GetAtPath(assetPath) as TextureImporter;
                Assert.That(
                    importer,
                    Is.Not.Null,
                    $"missing generated texture '{assetPath}'");
                Assert.That(importer.sRGBTexture, Is.True);
                Assert.That(importer.mipmapEnabled, Is.True);
                Assert.That(
                    importer.wrapMode,
                    Is.EqualTo(TextureWrapMode.Repeat));
                Assert.That(importer.filterMode,
                    Is.EqualTo(FilterMode.Bilinear));
                Assert.That(importer.anisoLevel, Is.EqualTo(4));
                Assert.That(
                    importer.textureCompression,
                    Is.EqualTo(
                        TextureImporterCompression.Uncompressed));
                Assert.That(importer.maxTextureSize, Is.EqualTo(512));
            }
        }

        [Test]
        public void BarExterior_IsAFixedMetrePassiveAuthoredBuilding()
        {
            BarManifest manifest = LoadManifest(ExteriorManifestPath);

            Assert.That(manifest.design_id, Is.EqualTo("bar_exterior_v2"));
            Assert.That(manifest.dimensions_m.width,
                Is.EqualTo(12.2645f).Within(0.0001f));
            Assert.That(manifest.dimensions_m.depth,
                Is.EqualTo(13.5237f).Within(0.0001f));
            Assert.That(manifest.dimensions_m.height,
                Is.EqualTo(9.3435f).Within(0.0001f));
            Assert.That(manifest.colliders, Is.False);
            Assert.That(manifest.lights, Is.False);
            Assert.That(manifest.cameras, Is.False);
            Assert.That(manifest.animation_count, Is.Zero);

            BarManifestAnchor door = manifest.anchors.Single(
                anchor => anchor.role == "exterior_door");
            Assert.That(door.name, Is.EqualTo("Door"));
            Assert.That(door.local_position, Has.Length.EqualTo(3));
            Assert.That(door.local_position,
                Is.EqualTo(new[] { 0f, 0f, 0f }));
            Assert.That(
                manifest.anchors.Count(anchor =>
                    anchor.role == "sign_pivot"),
                Is.EqualTo(1));
            BarManifestAnchor sign = manifest.anchors.Single(anchor =>
                anchor.role == "sign_pivot");
            Assert.That(sign.local_position, Has.Length.EqualTo(3));
            Assert.That(
                sign.local_position[1],
                Is.InRange(0.90f, 2.80f),
                "The blade sign must attach to the solid pier, not the " +
                "centre upper window axis.");

            string[] requiredParts =
            {
                "Pub Brick Shell",
                "Pub Rendered Upper Storey",
                "Pub Slate Roof",
                "Pub Brick Chimneys",
                "Pub Chimney Pots",
                "Pub Ground Floor Glass",
                "Pub Upper Sash Frames",
                "Pub Upper Windows Warm",
                "Pub Upper Window Dark",
                "Pub Side Service Door",
                "Bar Entrance Flanking Panels",
                "Bar Outer Bay Flanking Panels",
                "Bar Entrance Reveal Panels",
                "Bar Door",
                "Bar Door Frame Left",
                "Bar Door Frame Right",
                "Bar Entrance Canopy"
            };
            for (int index = 0; index < requiredParts.Length; index++)
            {
                Assert.That(
                    manifest.parts.Any(part =>
                        part.name == requiredParts[index]),
                    Is.True,
                    $"the exterior has no '{requiredParts[index]}' part");
            }

            BarManifestPart brick = manifest.parts.Single(part =>
                part.name == "Pub Brick Shell");
            BarManifestPart plaster = manifest.parts.Single(part =>
                part.name == "Pub Rendered Upper Storey");
            Assert.That(brick.sheet, Is.EqualTo("ExteriorBrick"));
            Assert.That(plaster.sheet, Is.EqualTo("ExteriorPlaster"));
            Assert.That(
                BarExteriorSurfaceAppearance.TryResolveSheet(
                    brick.sheet,
                    out BarExteriorSurfaceKind brickSurface),
                Is.True);
            Assert.That(brickSurface,
                Is.EqualTo(BarExteriorSurfaceKind.Brick));
            Assert.That(
                BarExteriorSurfaceAppearance.GetRecipe(brickSurface)
                    .ResourcePath,
                Is.EqualTo(
                    BarExteriorSurfaceAppearance.BrickTextureResourcePath));
            Assert.That(
                BarExteriorSurfaceAppearance.TryResolveSheet(
                    plaster.sheet,
                    out BarExteriorSurfaceKind plasterSurface),
                Is.True);
            Assert.That(plasterSurface,
                Is.EqualTo(BarExteriorSurfaceKind.Plaster));
            Assert.That(
                BarExteriorSurfaceAppearance.GetRecipe(plasterSurface)
                    .ResourcePath,
                Is.EqualTo(
                    BarExteriorSurfaceAppearance
                        .PlasterTextureResourcePath));

            ModelImporter importer =
                AssetImporter.GetAtPath(ExteriorModelPath) as ModelImporter;
            Assert.That(importer, Is.Not.Null, "the exterior did not import");
            Assert.That(importer.animationType,
                Is.EqualTo(ModelImporterAnimationType.None));
            Assert.That(importer.importCameras, Is.False);
            Assert.That(importer.importLights, Is.False);
            Assert.That(importer.addCollider, Is.False);

            GameObject prefab = BarModelResources.LoadFacadePrefab();
            Assert.That(prefab, Is.Not.Null);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                BarAssetRegistry registry =
                    instance.GetComponent<BarAssetRegistry>();
                Assert.That(registry, Is.Not.Null);
                Assert.That(registry.DesignId,
                    Is.EqualTo("bar_exterior_v2"));
                Assert.That(instance.transform.localScale,
                    Is.EqualTo(Vector3.one));
                Assert.That(registry.Dimensions.Width,
                    Is.EqualTo(12.2645f).Within(0.0001f));
                Assert.That(registry.Dimensions.Depth,
                    Is.EqualTo(13.5237f).Within(0.0001f));
                Assert.That(registry.Dimensions.Height,
                    Is.EqualTo(9.3435f).Within(0.0001f));
                AssertAnchor(
                    registry,
                    instance.transform,
                    "exterior_door",
                    Vector3.zero);
                Assert.That(
                    registry.TryGetAnchor(
                        "sign_pivot",
                        out Transform signPivot),
                    Is.True);
                Assert.That(signPivot, Is.Not.Null);
                Vector3 signPosition = instance.transform
                    .InverseTransformPoint(signPivot.position);
                Assert.That(
                    signPosition.z,
                    Is.InRange(0.90f, 2.80f),
                    "The imported blade-sign anchor must remain on the " +
                    "solid pier beside the upper windows.");
                Assert.That(
                    instance.GetComponentsInChildren<Collider>(true),
                    Is.Empty);
                Assert.That(
                    instance.GetComponentsInChildren<Light>(true),
                    Is.Empty);
                Assert.That(
                    instance.GetComponentsInChildren<Camera>(true),
                    Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void BarModel_MatchesTheRoomTheLayoutPlannerPublishes()
        {
            BarManifest manifest = LoadManifest();
            BarInteriorLayoutPlan plan = SamplePlan();

            Assert.That(
                manifest.dimensions_m.width,
                Is.EqualTo(plan.RoomSize.x).Within(0.001f),
                "the model and the planner disagree on room width");
            Assert.That(
                manifest.dimensions_m.depth,
                Is.EqualTo(plan.RoomSize.y).Within(0.001f),
                "the model and the planner disagree on room depth");
            Assert.That(
                manifest.dimensions_m.height,
                Is.EqualTo(plan.RoomHeight).Within(0.001f));
            Assert.That(
                manifest.wall_thickness_m,
                Is.EqualTo(plan.WallThickness).Within(0.001f));
            BarManifest facade = LoadManifest(ExteriorManifestPath);
            Assert.That(
                manifest.door_opening_m.width,
                Is.EqualTo(1.45f).Within(0.001f));
            Assert.That(
                manifest.door_opening_m.height,
                Is.EqualTo(2.34f).Within(0.001f));
            Assert.That(
                manifest.door_opening_m.width,
                Is.EqualTo(facade.door_opening_m.width).Within(0.001f),
                "The interior threshold must match this bar's facade.");
            Assert.That(
                manifest.door_opening_m.height,
                Is.EqualTo(facade.door_opening_m.height).Within(0.001f));

            BarManifestPart frontWall = manifest.parts.Single(part =>
                part.name == "Front Wall");
            Assert.That(
                frontWall.colliders,
                Has.Length.EqualTo(3),
                "The standard opening needs two piers and a lintel collider.");
            BarManifestCollider lintel = frontWall.colliders.Single(collider =>
                Mathf.Abs(collider.center[0]) < 0.001f);
            Assert.That(
                lintel.center[1] - lintel.size[1] * 0.5f,
                Is.EqualTo(2.34f).Within(0.001f));
        }

        [Test]
        public void BarModel_AnchorsLandOnThePlansOwnStations()
        {
            BarInteriorLayoutPlan plan = SamplePlan();
            GameObject prefab = BarModelResources.LoadInteriorPrefab();
            Assert.That(
                prefab,
                Is.Not.Null,
                "the bar prefab has not been built; run " +
                "Bar Promenade/Bar/Build Runtime Prefabs");

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                BarAssetRegistry registry =
                    instance.GetComponent<BarAssetRegistry>();
                Assert.That(registry, Is.Not.Null);

                AssertAnchor(
                    registry, instance.transform, "counter_station",
                    plan.CounterStationPosition);
                AssertAnchor(
                    registry, instance.transform, "activity_station",
                    plan.ActivityStationPosition);
                AssertAnchor(
                    registry, instance.transform, "entrance",
                    new Vector3(0f, 0f, -plan.RoomSize.y * 0.5f));
                AssertAnchor(
                    registry, instance.transform, "room_centre",
                    Vector3.zero);
                AssertAnchor(
                    registry, instance.transform, "hero_seat",
                    new Vector3(-1.15f, 0.8175f, 4.53f));
                AssertAnchor(
                    registry, instance.transform, "hero_approach",
                    new Vector3(-1.15f, 0f, 3.35f));
                AssertAnchor(
                    registry, instance.transform, "hero_stand",
                    new Vector3(
                        -1.15f,
                        0f,
                        registry.SourceGeneratorVersion == "3.2.1"
                            ? 4.02f
                            : 3.89f));
                AssertAnchor(
                    registry, instance.transform, "hero_camera",
                    new Vector3(-1.15f, 1.6175f, 4.65f));
                AssertAnchor(
                    registry, instance.transform, "hero_camera_look",
                    new Vector3(-1.15f, 1.7175f, 7.37f));
                AssertAnchor(
                    registry, instance.transform,
                    "bartender_platform_top",
                    new Vector3(-0.55f, 0f, 6.50f));

                BarDrinkServicePlan servicePlan =
                    BarDrinkServicePlan.FromLayout(plan);
                Assert.That(
                    servicePlan.MenuPose.Position.x,
                    Is.EqualTo(servicePlan.SeatPose.Position.x)
                        .Within(Tolerance),
                    "the fallback menu dock is not directly before the " +
                    "hero");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void BarModel_CarriesTheAuthoredPubAndInteractionFixtures()
        {
            BarManifest manifest = LoadManifest();
            string[] requiredParts =
            {
                "Bar Counter Return",
                "Counter Return Foot Rail",
                "Backbar Mirror Panels",
                "Backbar Patterned Glass Lattice",
                "Snug Divider Timber",
                "Snug Divider Patterned Glass",
                "Pub Carpet Fields",
                "Pub Carpet Diamond Motifs",
                "Entrance Door Frame",
                "Entrance Door",
                "Entrance Door Panels",
                "Entrance Door Transom Glass",
                "Entrance Door Furniture",
                "Bar Drink Service Stool Legs",
                "Bar Drink Service Stool Footring",
                "Bar Drink Service Stool",
                "Exit Header"
            };

            for (int index = 0; index < requiredParts.Length; index++)
            {
                Assert.That(
                    manifest.parts.Any(part =>
                        part.name == requiredParts[index]),
                    Is.True,
                    $"the pub redesign has no '{requiredParts[index]}'");
            }

            Assert.That(
                manifest.parts.Any(part =>
                    part.name == "Entrance Heavy Curtains" ||
                    part.name == "Entrance Curtain Brass Rail"),
                Is.False,
                "The former full-height entrance portal returned.");
            Assert.That(
                manifest.parts.Any(part =>
                    part.name == "Bartender Duckboard" ||
                    part.role == "bartender_duckboard"),
                Is.False,
                "The obsolete six-arm bartender duckboard returned.");

            if (manifest.generator_version != "3.2.1")
            {
                Assert.That(
                    manifest.parts.Any(part =>
                        part.name == "Drink Order Sign" ||
                        part.name == "Drink Order Sign Frame"),
                    Is.False,
                    "The retired single-seat marker returned to the model.");
            }

            BarManifestPart[] tapStems = manifest.parts
                .Where(part => part.role == "tap_stem")
                .ToArray();
            BarManifestPart[] tapHandles = manifest.parts
                .Where(part => part.role == "tap_handle")
                .ToArray();
            Assert.That(
                tapStems,
                Has.Length.EqualTo(3),
                "the bar must expose one compact bank of three taps");
            Assert.That(tapHandles, Has.Length.EqualTo(3));

            BarManifestPart regularSeat = manifest.parts.Single(part =>
                part.name == "Bar Stool 1");
            BarManifestPart regularMetal = manifest.parts.Single(part =>
                part.name == "Bar Stool 1 Leg");
            BarManifestPart heroSeat = manifest.parts.Single(part =>
                part.name == "Bar Drink Service Stool");
            BarManifestPart heroLegs = manifest.parts.Single(part =>
                part.name == "Bar Drink Service Stool Legs");
            BarManifestPart heroFootring = manifest.parts.Single(part =>
                part.name == "Bar Drink Service Stool Footring");
            Assert.That(heroSeat.vertices, Is.EqualTo(regularSeat.vertices));
            Assert.That(heroSeat.triangles, Is.EqualTo(regularSeat.triangles));
            Assert.That(
                heroLegs.vertices + heroFootring.vertices,
                Is.EqualTo(regularMetal.vertices));
            Assert.That(
                heroLegs.triangles + heroFootring.triangles,
                Is.EqualTo(regularMetal.triangles));

            BarManifestPart heroStool = manifest.parts.Single(part =>
                part.name == "Bar Drink Service Stool");
            Assert.That(
                heroStool.colliders,
                Has.Length.EqualTo(1),
                "the authored hero stool seat must keep its collider");
            if (manifest.generator_version != "3.2.1")
            {
                Assert.That(
                    regularSeat.colliders,
                    Has.Length.EqualTo(1),
                    "every current counter stool must share the same " +
                    "solid seat contract");
            }

            for (int index = 1; index <= 4; index++)
            {
                Assert.That(
                    manifest.parts.Any(part =>
                        part.name == $"Social Pub Table {index}"),
                    Is.True);
            }
        }

        [Test]
        public void BarServiceProps_AreAuthoredPassiveAndFactoryFlattened()
        {
            BarManifest manifest = LoadManifest(ServiceManifestPath);
            Assert.That(
                manifest.design_id,
                Is.EqualTo("bar_service_props_v1"));
            Assert.That(manifest.colliders, Is.False);
            Assert.That(manifest.lights, Is.False);
            Assert.That(manifest.cameras, Is.False);
            Assert.That(manifest.animation_count, Is.Zero);

            string[] requiredGroups =
            {
                "service:bottle:WaterBottle",
                "service:bottle:BeerLongneck",
                "service:bottle:WineBottle",
                "service:bottle:VodkaBottle",
                "service:bottle:CognacBottle",
                "service:vessel:Tumbler",
                "service:vessel:Pint",
                "service:vessel:WineGlass",
                "service:vessel:ShotGlass",
                "service:vessel:Snifter",
                "service:menu",
                "service:pour_stream"
            };
            for (int index = 0; index < requiredGroups.Length; index++)
            {
                Assert.That(
                    manifest.parts.Any(part =>
                        part.group == requiredGroups[index]),
                    Is.True,
                    $"the service pack has no '{requiredGroups[index]}'");
            }

            var host = new GameObject("Bar Service Prop Test");
            try
            {
                BarServicePropInstance bottle =
                    BarServicePropFactory.CreateBottle(
                        host.transform,
                        BarDrinkBottleStyle.WaterBottle);
                Assert.That(bottle.transform.localScale, Is.EqualTo(Vector3.one));
                Assert.That(
                    bottle.TryGetRenderer(
                        "service_bottle_body",
                        out Renderer bottleBody),
                    Is.True);
                Assert.That(
                    bottle.TryGetRenderer(
                        "service_bottle_label",
                        out Renderer bottleLabel),
                    Is.True);
                Assert.That(bottleBody, Is.Not.Null);
                Assert.That(bottleLabel, Is.Not.Null);
                Assert.That(
                    bottle.TryGetAnchor(
                        "service_bottle_mouth:WaterBottle",
                        out Transform mouth),
                    Is.True);
                Assert.That(
                    mouth.localPosition.y,
                    Is.EqualTo(0.68f).Within(Tolerance));

                BarServicePropInstance vessel =
                    BarServicePropFactory.CreateVessel(
                        host.transform,
                        BarDrinkVesselKind.Pint);
                Assert.That(
                    vessel.TryGetRenderer(
                        "service_vessel_shell",
                        out Renderer shell),
                    Is.True);
                Assert.That(shell.bounds.size.y,
                    Is.EqualTo(0.39f).Within(0.02f));
                Assert.That(
                    vessel.TryGetAnchor(
                        "service_vessel_target:Pint",
                        out Transform pourTarget),
                    Is.True);
                Assert.That(
                    pourTarget.localPosition.y,
                    Is.EqualTo(0.365f).Within(Tolerance));

                BarServicePropInstance menu =
                    BarServicePropFactory.CreateMenu(host.transform);
                Assert.That(
                    menu.TryGetRenderer(
                        "service_menu_cover",
                        out Renderer menuCover),
                    Is.True);
                Assert.That(
                    menu.TryGetRenderer(
                        "service_menu_pages",
                        out Renderer menuPages),
                    Is.True);
                Assert.That(menuCover, Is.Not.Null);
                Assert.That(menuPages, Is.Not.Null);
                var menuProperties = new MaterialPropertyBlock();
                menuPages.GetPropertyBlock(menuProperties);
                Color menuPageTint =
                    menuProperties.GetColor("_BaseColor");
                Color expectedMenuPageTint =
                    BarSurfaceAppearance.CreateDisplayTint(
                        new Color(0.74f, 0.66f, 0.47f),
                        BarSurfaceKind.Paper);
                Assert.That(menuPageTint.r,
                    Is.EqualTo(expectedMenuPageTint.r).Within(Tolerance));
                Assert.That(menuPageTint.g,
                    Is.EqualTo(expectedMenuPageTint.g).Within(Tolerance));
                Assert.That(menuPageTint.b,
                    Is.EqualTo(expectedMenuPageTint.b).Within(Tolerance));

                Assert.That(
                    menu.TryGetAnchor(
                        BarServicePropFactory.MenuOriginRole,
                        out Transform menuOrigin),
                    Is.True);
                Assert.That(
                    Vector3.Dot(menuOrigin.right, Vector3.right),
                    Is.GreaterThan(0.999f));
                Assert.That(
                    Vector3.Dot(menuOrigin.up, Vector3.up),
                    Is.GreaterThan(0.999f));
                Assert.That(
                    Vector3.Dot(menuOrigin.forward, Vector3.forward),
                    Is.GreaterThan(0.999f));

                Assert.That(
                    menu.TryGetAnchor(
                        BarServicePropFactory.MenuPageOriginRole,
                        out Transform pageOrigin),
                    Is.True);
                Assert.That(
                    menu.TryGetAnchor(
                        BarServicePropFactory.MenuPageRightRole,
                        out Transform pageRight),
                    Is.True);
                Assert.That(
                    menu.TryGetAnchor(
                        BarServicePropFactory.MenuPageUpRole,
                        out Transform pageUp),
                    Is.True);
                Assert.That(
                    menu.TryGetAnchor(
                        BarServicePropFactory.MenuPageNormalRole,
                        out Transform pageNormal),
                    Is.True);
                Assert.That(
                    Vector3.Distance(
                        pageRight.localPosition - pageOrigin.localPosition,
                        Vector3.right * 0.1f),
                    Is.LessThan(0.001f));
                Assert.That(
                    Vector3.Distance(
                        pageUp.localPosition - pageOrigin.localPosition,
                        Vector3.forward * 0.1f),
                    Is.LessThan(0.001f));
                Assert.That(
                    Vector3.Distance(
                        pageNormal.localPosition - pageOrigin.localPosition,
                        Vector3.up * 0.1f),
                    Is.LessThan(0.001f));

                string[] expectedVisibleMenuRoles =
                {
                    "service_menu_text_item:00",
                    "service_menu_text_item:04",
                    "service_menu_text_item:05",
                    "service_menu_text_item:08"
                };
                float[] expectedVisibleMenuPageOffsets =
                {
                    0.14f,
                    -0.14f,
                    0.105f,
                    -0.105f
                };
                for (int index = 0;
                     index < BarServicePropFactory.MenuItemCount;
                     index++)
                {
                    Assert.That(
                        BarServicePropFactory.MenuTextItemRole(index),
                        Is.EqualTo(expectedVisibleMenuRoles[index]));
                    Assert.That(
                        menu.TryGetAnchor(
                            BarServicePropFactory.MenuTextItemRole(index),
                            out Transform row),
                        Is.True,
                        $"the open bar menu has no row {index:00}");
                    Assert.That(row.right.x, Is.GreaterThan(0.98f));
                    Assert.That(row.up.z, Is.GreaterThan(0.98f));
                    Assert.That(-row.forward.y, Is.GreaterThan(0.98f));
                    Assert.That(
                        row.localPosition.z,
                        Is.EqualTo(expectedVisibleMenuPageOffsets[index])
                            .Within(0.001f),
                        $"visible menu block {index} lost its page spacing");
                    if (index < 2)
                    {
                        Assert.That(row.localPosition.x, Is.LessThan(0f));
                    }
                    else
                    {
                        Assert.That(row.localPosition.x, Is.GreaterThan(0f));
                    }
                }
                Assert.That(
                    menu.TryGetAnchor(
                        BarServicePropFactory.MenuTextSelectionRole,
                        out Transform selection),
                    Is.True);
                Assert.That(selection.localPosition.x, Is.LessThan(0f));

                BarServicePropInstance stream =
                    BarServicePropFactory.CreatePourStream(host.transform);
                Assert.That(
                    stream.TryGetRenderer(
                        "service_pour_stream",
                        out Renderer streamRenderer),
                    Is.True);
                Assert.That(
                    streamRenderer.bounds.size.y,
                    Is.EqualTo(2f).Within(0.02f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void BarModel_CarriesGeometryAndNothingElse()
        {
            GameObject prefab = BarModelResources.LoadInteriorPrefab();
            Assert.That(prefab, Is.Not.Null);

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                Assert.That(
                    instance.GetComponentsInChildren<Collider>(true),
                    Is.Empty,
                    "collision is added by the placer, from the manifest");
                Assert.That(
                    instance.GetComponentsInChildren<Light>(true),
                    Is.Empty,
                    "light comes from the layout plan's light anchors");
                Assert.That(
                    instance.GetComponentsInChildren<Camera>(true),
                    Is.Empty);
                Assert.That(
                    instance.GetComponentsInChildren<Animator>(true),
                    Is.Empty);
                Assert.That(
                    instance.GetComponentsInChildren<Rigidbody>(true),
                    Is.Empty);

                BarAssetRegistry registry =
                    instance.GetComponent<BarAssetRegistry>();
                Assert.That(registry, Is.Not.Null);
                Assert.That(registry.Parts, Is.Not.Empty);
                Assert.That(
                    registry.Parts.All(
                        binding =>
                            binding != null &&
                            binding.Renderer != null &&
                            !string.IsNullOrWhiteSpace(binding.Role) &&
                            binding.Renderer.sharedMaterials.Length == 1 &&
                            binding.Renderer.sharedMaterial != null),
                    Is.True,
                    "every part needs exactly one bound material");

                //  Exactly two shared materials across the complete part set: lit
                //  and emissive. That is what makes a district tint a
                //  property block rather than an asset per district per
                //  part.
                Assert.That(
                    registry.Parts
                        .Select(binding => binding.Renderer.sharedMaterial)
                        .Distinct()
                        .Count(),
                    Is.EqualTo(2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void BarModel_CarriesEveryActivitySetAndDistrictDressing()
        {
            BarManifest manifest = LoadManifest();

            //  Four activity sets and four district dressings, because
            //  the room varies in exactly those two ways and no other.
            //  If an enum ever gains a member with no authored set, the
            //  bar would silently lose its dressing rather than fail.
            foreach (BarActivityKind activity in
                     Enum.GetValues(typeof(BarActivityKind))
                         .Cast<BarActivityKind>())
            {
                if (activity == BarActivityKind.None)
                {
                    continue;
                }

                string group = $"activity:{activity}";
                Assert.That(
                    manifest.parts.Any(part => part.group == group),
                    Is.True,
                    $"the model has no authored set for {activity}");
            }

            foreach (BarDistrictMood mood in
                     Enum.GetValues(typeof(BarDistrictMood))
                         .Cast<BarDistrictMood>())
            {
                string group = $"district:{mood}";
                Assert.That(
                    manifest.parts.Any(part => part.group == group),
                    Is.True,
                    $"the model has no authored dressing for {mood}");
            }
        }

        [Test]
        public void BarModel_DeclaresOnlyTintsTheIdentityCanAnswer()
        {
            BarManifest manifest = LoadManifest();
            BarInteriorLayoutPlan plan = SamplePlan();

            //  A tint naming a field that does not exist throws at
            //  resolve time - deep inside a room build, on one district
            //  only. Asking every one of them here turns that into a
            //  named failure.
            foreach (BarManifestPart part in manifest.parts)
            {
                Assert.That(part.tint, Is.Not.Null, part.name);
                var spec = new BarTintSpec(
                    part.tint.field,
                    Color.white,
                    part.tint.scale,
                    part.tint.lerp_field,
                    Color.white,
                    part.tint.lerp_t);
                Assert.DoesNotThrow(
                    () => spec.Resolve(plan.DistrictIdentity),
                    $"part '{part.name}' asks for an unknown tint");
            }
        }

        [Test]
        public void PlacedRoom_PutsItsLandmarksWhereTheyWereBuiltBefore()
        {
            //  The model's anchors are asserted above; this asserts that
            //  the PLACER reads them correctly. It did not: it took
            //  `localPosition` from a node whose parent carries the FBX
            //  unit factor of 100, so the jukebox stood in the middle of
            //  the floor at a sixteenth of a metre from the room centre.
            //  Every numeric check passed. Only a rendered frame showed
            //  it, and this is that frame turned into an assertion.
            var host = new GameObject("Bar Placement Test");
            try
            {
                BarInteriorLayoutPlan plan = SamplePlan();
                Transform room = BarInteriorWorldBuilder.Build(
                    host.transform,
                    plan);

                AssertPlaced(room, "Bar Jukebox",
                    new Vector3(6.4f, 0f, -6.78f));
                AssertPlaced(room, "Slow Ceiling Fan",
                    new Vector3(0f, 4.35f, 0.75f));
                AssertPlaced(room, "HeroSeat",
                    new Vector3(-1.15f, 0.8175f, 4.53f));
                AssertPlaced(room, "HeroApproach",
                    new Vector3(-1.15f, 0f, 3.35f));
                AssertPlaced(room, "HeroStand",
                    new Vector3(
                        -1.15f,
                        0f,
                        room.Find("HeroStand").localPosition.z < 3.95f
                            ? 3.89f
                            : 4.02f));
                AssertPlaced(room, "HeroCamera",
                    new Vector3(-1.15f, 1.6175f, 4.65f));
                AssertPlaced(room, "HeroCameraLook",
                    new Vector3(-1.15f, 1.7175f, 7.37f));
                AssertPlaced(room, "MenuDock",
                    new Vector3(-1.15f, 1.045f, 5.44f));
                Assert.That(room.Find("Drink Order Point"), Is.Null);
                Assert.That(room.Find("Exit Header"), Is.Not.Null);
                Assert.That(room.Find("Bartender Duckboard"), Is.Null);

                Transform rightmostStool = room.Find("Bar Stool 6");
                Assert.That(rightmostStool, Is.Not.Null);
                Assert.That(
                    rightmostStool.GetComponent<Renderer>().bounds.center.x,
                    Is.EqualTo(4.00f).Within(Tolerance));

                Renderer[] tapStems = room
                    .GetComponentsInChildren<Renderer>(true)
                    .Where(item => item.name.StartsWith(
                        "Beer Tap Stem ",
                        StringComparison.Ordinal))
                    .OrderBy(item => item.name, StringComparer.Ordinal)
                    .ToArray();
                Renderer[] tapHandles = room
                    .GetComponentsInChildren<Renderer>(true)
                    .Where(item => item.name.StartsWith(
                        "Beer Tap Handle ",
                        StringComparison.Ordinal))
                    .OrderBy(item => item.name, StringComparer.Ordinal)
                    .ToArray();
                Assert.That(tapStems, Has.Length.EqualTo(3));
                Assert.That(tapHandles, Has.Length.EqualTo(3));
                float[] expectedTapXs = { 4.75f, 5.08f, 5.41f };
                for (int index = 0; index < expectedTapXs.Length; index++)
                {
                    Assert.That(
                        tapStems[index].bounds.center.x,
                        Is.EqualTo(expectedTapXs[index]).Within(Tolerance));
                    Assert.That(
                        tapHandles[index].bounds.center.x,
                        Is.EqualTo(expectedTapXs[index]).Within(Tolerance));
                    if (index == 0)
                    {
                        continue;
                    }

                    Assert.That(
                        tapStems[index].bounds.center.x -
                        tapStems[index - 1].bounds.center.x,
                        Is.EqualTo(0.33f).Within(Tolerance),
                        "the beer taps are no longer tightly grouped");
                }

                Assert.That(
                    tapHandles[0].bounds.min.x,
                    Is.GreaterThan(
                        rightmostStool.GetComponent<Renderer>()
                            .bounds.max.x),
                    "the beer taps intrude into the last seating bay");

                Transform regularStool = room.Find("Bar Stool 1");
                Transform heroStool = room.Find(
                    "Bar Drink Service Stool");
                Assert.That(regularStool, Is.Not.Null);
                Assert.That(heroStool, Is.Not.Null);
                Bounds regularSeat =
                    regularStool.GetComponent<Renderer>().bounds;
                Bounds heroSeat =
                    heroStool.GetComponent<Renderer>().bounds;
                Assert.That(
                    Vector3.Distance(regularSeat.size, heroSeat.size),
                    Is.LessThan(Tolerance));
                Assert.That(
                    heroSeat.size.x,
                    Is.EqualTo(
                            MountainRoadCafeWorldBuilder.StoolSeatDiameter)
                        .Within(Tolerance));
                Assert.That(
                    heroSeat.size.y,
                    Is.EqualTo(
                            MountainRoadCafeWorldBuilder.StoolSeatThickness)
                        .Within(Tolerance));
                Assert.That(
                    heroSeat.max.y,
                    Is.EqualTo(
                            MountainRoadCafeWorldBuilder
                                .StoolSeatTopAboveFloor)
                        .Within(Tolerance));
                Assert.That(
                    heroSeat.center.z,
                    Is.EqualTo(regularSeat.center.z).Within(Tolerance),
                    "The hero stool must sit in the same visual row.");

                string[] regularStoolNames =
                {
                    "Bar Stool 1",
                    "Bar Stool 2",
                    "Bar Stool 4",
                    "Bar Stool 5",
                    "Bar Stool 6"
                };
                for (int index = 0;
                     index < regularStoolNames.Length;
                     index++)
                {
                    Transform collision = room.Find(
                        regularStoolNames[index] + " Collision");
                    Assert.That(
                        collision,
                        Is.Not.Null,
                        regularStoolNames[index] +
                        " has no physical seat disk");
                    BoxCollider seatCollider =
                        collision.GetComponent<BoxCollider>();
                    Assert.That(seatCollider, Is.Not.Null);
                    Transform visual = room.Find(
                        regularStoolNames[index]);
                    Assert.That(visual, Is.Not.Null);
                    Renderer visualRenderer =
                        visual.GetComponent<Renderer>();
                    Assert.That(visualRenderer, Is.Not.Null);
                    Assert.That(
                        Vector3.Distance(
                            seatCollider.bounds.center,
                            visualRenderer.bounds.center),
                        Is.LessThan(Tolerance),
                        regularStoolNames[index] +
                        " collision is not centered on its visible seat");
                    Assert.That(
                        seatCollider.size,
                        Is.EqualTo(new Vector3(
                            MountainRoadCafeWorldBuilder
                                .StoolSeatDiameter,
                            MountainRoadCafeWorldBuilder
                                .StoolSeatThickness,
                            MountainRoadCafeWorldBuilder
                                .StoolSeatDiameter)));
                }

                //  One pendant per light anchor, hung where the plan says
                //  and at the size it was authored - a lamp cloned from a
                //  template is the easiest thing in this room to place
                //  correctly and lose the scale of.
                for (int index = 0;
                     index < plan.LightAnchors.Count;
                     index++)
                {
                    BarInteriorLightAnchor light = plan.LightAnchors[index];
                    Transform shade =
                        room.Find($"Practical Shade {index + 1}");
                    Assert.That(
                        shade,
                        Is.Not.Null,
                        $"no shade for light anchor {index + 1}");
                    Assert.That(
                        Vector3.Distance(
                            shade.localPosition,
                            light.Position),
                        Is.LessThan(0.02f),
                        $"shade {index + 1} hangs away from its anchor");

                    //  Across in BOTH ground axes and shallow in the
                    //  vertical one. Measuring only `size.x` passed a
                    //  shade lying on its side, which is how every
                    //  pendant in the room came to hang horizontally:
                    //  the clone dropped the template's rotation, and a
                    //  disc 0.58 m across is 0.58 m across whichever way
                    //  up it is.
                    Renderer shadeRenderer = shade.GetComponent<Renderer>();
                    Assert.That(shadeRenderer, Is.Not.Null);
                    Bounds shadeBounds = shadeRenderer.bounds;
                    Assert.That(
                        shadeBounds.size.x,
                        Is.EqualTo(0.58f).Within(0.05f),
                        $"shade {index + 1} is {shadeBounds.size} across, " +
                        "not 0.58 m");
                    Assert.That(
                        shadeBounds.size.z,
                        Is.EqualTo(0.58f).Within(0.05f),
                        $"shade {index + 1} is {shadeBounds.size} across, " +
                        "not 0.58 m");
                    Assert.That(
                        shadeBounds.size.y,
                        Is.EqualTo(0.28f).Within(0.05f),
                        $"shade {index + 1} is {shadeBounds.size} - it is " +
                        "lying on its side instead of hanging");

                    //  And the flex reaches the ceiling from the anchor.
                    //  This is the assertion that catches a lamp cloned
                    //  at the wrong orientation outright: stretched along
                    //  the wrong axis the cable grew thicker, not longer,
                    //  and never left the anchor.
                    Transform flex =
                        room.Find($"Practical Cable {index + 1}");
                    Assert.That(
                        flex,
                        Is.Not.Null,
                        $"no cable for light anchor {index + 1}");
                    Bounds flexBounds =
                        flex.GetComponent<Renderer>().bounds;
                    Assert.That(
                        flexBounds.max.y,
                        Is.EqualTo(plan.RoomHeight).Within(0.05f),
                        $"cable {index + 1} tops out at {flexBounds.max.y}, " +
                        $"not at the ceiling ({plan.RoomHeight} m)");
                    Assert.That(
                        flexBounds.size.y,
                        Is.EqualTo(
                                plan.RoomHeight - light.Position.y)
                            .Within(0.05f),
                        $"cable {index + 1} hangs {flexBounds.size} - it " +
                        "was stretched along the wrong axis");
                }

                //  And the room is the size it was authored at, not a
                //  hundredth of it.
                Transform floor = room.Find("Floor");
                Assert.That(floor, Is.Not.Null);
                Renderer floorRenderer = floor.GetComponent<Renderer>();
                Assert.That(floorRenderer, Is.Not.Null);
                Assert.That(
                    floorRenderer.bounds.size.x,
                    Is.EqualTo(plan.RoomSize.x).Within(0.05f),
                    "the placed floor is not the size the plan publishes");
                Assert.That(
                    floorRenderer.bounds.size.z,
                    Is.EqualTo(plan.RoomSize.y).Within(0.05f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void PlacedRoom_CollidesWhereItsGeometryIs()
        {
            //  Collision is authored in ROOM-space metres, so it has to
            //  hang off the room. Hung off the model part it describes,
            //  it is read in a local space that carries the FBX unit
            //  factor of a hundred AND the Blender-to-Unity axis
            //  conversion of ninety degrees about X: the floor came out
            //  a 2200x1600x24 m slab tipped on its side and sunk twelve
            //  metres. The room had no ground. The hero fell through it
            //  forever and the chase camera, whose probe started inside
            //  the slab, collapsed onto his head - and every number the
            //  tests above measure was still correct, because none of
            //  them is a collider.
            var host = new GameObject("Bar Collision Test");
            try
            {
                BarInteriorLayoutPlan plan = SamplePlan();
                Transform room = BarInteriorWorldBuilder.Build(
                    host.transform,
                    plan);

                var envelope = new Bounds(
                    new Vector3(0f, plan.RoomHeight * 0.5f, 0f),
                    new Vector3(
                        plan.RoomSize.x + 2f,
                        plan.RoomHeight + 6f,
                        plan.RoomSize.y + 2f));

                Collider[] colliders =
                    room.GetComponentsInChildren<Collider>(true);
                Assert.That(
                    colliders,
                    Is.Not.Empty,
                    "the placed room carries no collision at all");

                foreach (Collider collider in colliders)
                {
                    Bounds bounds = collider.bounds;
                    Assert.That(
                        envelope.Contains(bounds.min) &&
                        envelope.Contains(bounds.max),
                        Is.True,
                        $"'{collider.transform.name}' collides at " +
                        $"{bounds.center} across {bounds.size}, which is " +
                        "not inside the room it belongs to");
                }

                //  The one that matters most: something to stand on
                //  where the plan puts the hero down.
                Assert.That(
                    HasGroundUnder(colliders, plan.PlayerSpawn),
                    Is.True,
                    "there is nothing to stand on under the player's " +
                    $"spawn point {plan.PlayerSpawn}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static bool HasGroundUnder(
            Collider[] colliders,
            Vector3 spawn)
        {
            foreach (Collider collider in colliders)
            {
                if (collider.isTrigger)
                {
                    continue;
                }

                Bounds bounds = collider.bounds;
                if (bounds.min.x <= spawn.x &&
                    bounds.max.x >= spawn.x &&
                    bounds.min.z <= spawn.z &&
                    bounds.max.z >= spawn.z &&
                    bounds.max.y > -0.5f &&
                    bounds.max.y <= spawn.y + 0.2f)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AssertPlaced(
            Transform room,
            string name,
            Vector3 expected)
        {
            Transform placed = room.Find(name);
            Assert.That(placed, Is.Not.Null, $"the room has no '{name}'");
            Assert.That(
                Vector3.Distance(placed.localPosition, expected),
                Is.LessThan(0.02f),
                $"'{name}' stands at {placed.localPosition}, not {expected}");
        }

        [Test]
        public void BarModel_KeepsTheRoomTheSameShapeInEveryDistrict()
        {
            //  One authored pub serves every district, so its envelope may
            //  not vary with the district - only the tint may. If a district ever
            //  changed the room, one authored model would be the wrong
            //  tool and this test is where that surfaces.
            Vector2 first = default;
            bool started = false;
            foreach (CityDistrictKind district in
                     Enum.GetValues(typeof(CityDistrictKind))
                         .Cast<CityDistrictKind>())
            {
                BarInteriorLayoutPlan plan = BarInteriorLayoutPlanner.Generate(
                    20260826,
                    "bar-contract",
                    BarActivityKind.BeerPong,
                    district);
                if (!started)
                {
                    first = plan.RoomSize;
                    started = true;
                    continue;
                }

                Assert.That(
                    plan.RoomSize,
                    Is.EqualTo(first),
                    $"district {district} changes the room's size");
            }

            Assert.That(started, Is.True);
        }

        private static void AssertAnchor(
            BarAssetRegistry registry,
            Transform root,
            string role,
            Vector3 expected)
        {
            Assert.That(
                registry.TryGetAnchor(role, out Transform anchor),
                Is.True,
                $"the model has no '{role}' anchor");

            Vector3 actual = root.InverseTransformPoint(anchor.position);
            Assert.That(
                actual.x, Is.EqualTo(expected.x).Within(Tolerance),
                $"'{role}' x drifted from the plan");
            Assert.That(
                actual.y, Is.EqualTo(expected.y).Within(Tolerance),
                $"'{role}' y drifted from the plan");
            Assert.That(
                actual.z, Is.EqualTo(expected.z).Within(Tolerance),
                $"'{role}' z drifted from the plan");
        }

        private static BarInteriorLayoutPlan SamplePlan()
        {
            return BarInteriorLayoutPlanner.Generate(
                20260826,
                "bar-contract",
                BarActivityKind.BeerPong,
                CityDistrictKind.Nightlife);
        }

        private static BarManifest LoadManifest()
        {
            return LoadManifest(ManifestPath);
        }

        private static BarManifest LoadManifest(string path)
        {
            TextAsset source =
                AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            Assert.That(
                source,
                Is.Not.Null,
                $"'{path}' is missing; run the Blender generator");
            BarManifest manifest =
                JsonUtility.FromJson<BarManifest>(source.text);
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.parts, Is.Not.Null.And.Not.Empty);
            return manifest;
        }

        [Serializable]
        private sealed class BarManifest
        {
            public string design_id;
            public string generator_version;
            public BarDimensionsManifest dimensions_m;
            public float wall_thickness_m;
            public BarOpeningManifest door_opening_m;
            public bool colliders;
            public bool lights;
            public bool cameras;
            public int animation_count;
            public int triangle_count;
            public BarManifestAnchor[] anchors;
            public BarManifestPart[] parts;
        }

        [Serializable]
        private sealed class BarManifestAnchor
        {
            public string name;
            public string role;
            public float[] local_position;
        }

        [Serializable]
        private sealed class BarManifestPart
        {
            public string name;
            public string role;
            public string group;
            public string sheet;
            public bool emissive;
            public BarManifestTint tint;
            public BarManifestCollider[] colliders;
            public int vertices;
            public int triangles;
        }

        [Serializable]
        private sealed class BarManifestCollider
        {
            public float[] center;
            public float[] size;
        }

        [Serializable]
        private sealed class BarManifestTint
        {
            public string field;
            public float scale;
            public string lerp_field;
            public float lerp_t;
        }

        [Serializable]
        private sealed class BarDimensionsManifest
        {
            public float width;
            public float depth;
            public float height;
        }

        [Serializable]
        private sealed class BarOpeningManifest
        {
            public float width;
            public float height;
        }
    }
}
