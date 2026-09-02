using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Protects the deterministic hand-off between the authored Nighthawks
    /// cafe and its passive runtime prefab.
    /// </summary>
    [Category("MountainRoad")]
    public sealed class MountainRoadCafeModelContractTests
    {
        private const string ManifestPath =
            "Assets/MountainRoad/Cafe/Models/MountainRoadCafe3D.json";
        private const string ModelPath =
            "Assets/MountainRoad/Cafe/Models/MountainRoadCafe3D.fbx";
        private const int ExpectedMeshCount = 59;
        private const int ExpectedTriangleCount = 5682;
        private const int ExpectedAnchorCount = 45;
        private const int ExpectedPropCount = 6;
        private const int ExpectedColliderDescriptorCount = 17;
        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int SmoothnessId =
            Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId =
            Shader.PropertyToID("_Metallic");

        private static readonly string[] ExpectedTextureSheets =
        {
            "CafeExteriorDetail",
            "CafeInteriorDetail",
            "CafeCounterDetail",
            "CafeMetalDetail",
            "CafePropsDetail",
            "CafeGlassDetail",
        };

        [Test]
        public void AuthoredCafe_IsExactPassivePrefabWithSharedGlass()
        {
            CafeManifest manifest = LoadManifest();
            AssertManifest(manifest);
            AssertPassiveImporter();

            GameObject prefab = MountainRoadCafeModelResources.LoadPrefab();
            Assert.That(prefab, Is.Not.Null,
                "Run BarPromenade.Editor.MountainRoadCafeAssetSetup.RunBatch.");

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                AssertPrefab(instance, manifest);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void AssertManifest(CafeManifest manifest)
        {
            Assert.That(manifest.design_id,
                Is.EqualTo("mountain_road_cafe_nighthawks_v1"));
            Assert.That(manifest.mesh_count, Is.EqualTo(ExpectedMeshCount));
            Assert.That(manifest.parts, Has.Length.EqualTo(ExpectedMeshCount));
            Assert.That(manifest.triangle_count,
                Is.EqualTo(ExpectedTriangleCount));
            Assert.That(manifest.anchors,
                Has.Length.EqualTo(ExpectedAnchorCount));
            Assert.That(manifest.dynamic_props,
                Has.Length.EqualTo(ExpectedPropCount));
            Assert.That(manifest.collider_descriptors,
                Has.Length.EqualTo(ExpectedColliderDescriptorCount));
            Assert.That(manifest.textures, Has.Length.EqualTo(6));
            Assert.That(
                manifest.textures.Select(texture => texture.sheet),
                Is.EquivalentTo(ExpectedTextureSheets));
            Assert.That(manifest.textures.All(texture =>
                    texture.width == 512 && texture.height == 512),
                Is.True);
            Assert.That(manifest.overlap_count, Is.Zero,
                "Broad coplanar overlaps reintroduce visible flicker.");
            AssertDoorHeaderClosesFacade(manifest);
            AssertPassiveKitchenManifest(manifest);
            Assert.That(manifest.stool_count, Is.EqualTo(7));
            Assert.That(manifest.cup_assembly_count, Is.EqualTo(2));
            Assert.That(manifest.colliders, Is.False);
            Assert.That(manifest.lights, Is.False);
            Assert.That(manifest.cameras, Is.False);
            Assert.That(manifest.materials, Is.False);
            Assert.That(manifest.animation_count, Is.Zero);
        }

        private static void AssertPassiveKitchenManifest(
            CafeManifest manifest)
        {
            var expectedParts = new[]
            {
                new { Name = "Cafe_KitchenCabinetFronts", Role = "kitchen_cabinet_front" },
                new { Name = "Cafe_KitchenHandles", Role = "kitchen_cabinet_handle" },
                new { Name = "Cafe_KitchenBacksplash", Role = "kitchen_backsplash" },
                new { Name = "Cafe_Stove", Role = "stove" },
                new { Name = "Cafe_FryingPan", Role = "frying_pan" },
                new { Name = "Cafe_CuttingBoard", Role = "cutting_board" },
                new { Name = "Cafe_StoveTaskFixture", Role = "stove_task_fixture" },
                new { Name = "Cafe_StoveTaskLens", Role = "stove_task_lens" },
                new { Name = "Cafe_RefrigeratorBody", Role = "refrigerator_body" },
                new { Name = "Cafe_RefrigeratorCavity", Role = "refrigerator_cavity" },
                new { Name = "Cafe_RefrigeratorShelves", Role = "refrigerator_shelf" },
                new { Name = "Cafe_FridgeDoor", Role = "fridge_door" },
            };
            foreach (var expected in expectedParts)
            {
                CafePart part = manifest.parts.Single(candidate =>
                    candidate.name == expected.Name);
                Assert.That(part.role, Is.EqualTo(expected.Role), expected.Name);
            }

            foreach (string name in new[]
                     {
                         "Cafe_RefrigeratorBody",
                         "Cafe_RefrigeratorCavity",
                         "Cafe_RefrigeratorShelves",
                         "Cafe_FridgeDoor",
                     })
            {
                CafePart part = manifest.parts.Single(candidate =>
                    candidate.name == name);
                Assert.That(part.sheet, Is.EqualTo("CafePropsDetail"), name);
                Assert.That(part.base_surface, Is.EqualTo("PaleEnamel"), name);
                Assert.That(
                    part.uv_strategy,
                    Is.EqualTo(
                        "single_inset_appliance_patch_without_pattern_edges"),
                    name);
            }
            foreach (string name in new[] { "Cafe_Stove", "Cafe_FryingPan" })
            {
                CafePart part = manifest.parts.Single(candidate =>
                    candidate.name == name);
                Assert.That(part.sheet, Is.EqualTo("CafeMetalDetail"), name);
                Assert.That(part.base_surface, Is.EqualTo("PaleEnamel"), name);
                Assert.That(
                    part.uv_strategy,
                    Is.EqualTo(
                        "single_inset_appliance_patch_without_pattern_edges"),
                    name);
            }
            CafePart taskLens = manifest.parts.Single(part =>
                part.name == "Cafe_StoveTaskLens");
            Assert.That(taskLens.role, Is.EqualTo("stove_task_lens"));
            Assert.That(taskLens.sheet, Is.EqualTo("CafePropsDetail"));
            Assert.That(taskLens.emissive, Is.True);
            Assert.That(taskLens.shadows, Is.False);

            CafeKitchenContract kitchen = manifest.kitchen_contract;
            Assert.That(kitchen, Is.Not.Null);
            Assert.That(
                kitchen.visible_rear_lining_z_m,
                Is.EqualTo(5.2725f).Within(0.0001f));
            Assert.That(
                kitchen.worktop_wall_gap_m,
                Is.EqualTo(0.003f).Within(0.0001f));
            Assert.That(
                kitchen.cabinet_wall_gap_m,
                Is.EqualTo(0.020f).Within(0.0001f));
            Assert.That(
                kitchen.rear_door_clearance_m,
                Is.EqualTo(0.08f).Within(0.0001f));
            CafePart rearDoor = manifest.parts.Single(part =>
                part.name == "Cafe_RearDoor");
            foreach (var surface in new[]
                     {
                         new { Name = "Cafe_ServiceCabinet", Gap = 0.020f },
                         new { Name = "Cafe_ServiceWorktop", Gap = 0.003f },
                         new { Name = "Cafe_KitchenBacksplash", Gap = 0.003f },
                         new { Name = "Cafe_RefrigeratorBody", Gap = 0.003f },
                     })
            {
                CafePart part = manifest.parts.Single(candidate =>
                    candidate.name == surface.Name);
                Assert.That(
                    kitchen.visible_rear_lining_z_m - part.bounds_max[1],
                    Is.EqualTo(surface.Gap).Within(0.001f),
                    surface.Name);
                if (surface.Name != "Cafe_RefrigeratorBody")
                {
                    Assert.That(
                        rearDoor.bounds_min[0] - part.bounds_max[0],
                        Is.GreaterThanOrEqualTo(0.08f),
                        $"{surface.Name} must stop before the rear door.");
                }
            }

            CafeDynamicProp door = manifest.dynamic_props.Single(prop =>
                prop.name == "FridgeDoor");
            Assert.That(door.role, Is.EqualTo("fridge_door"));
            Assert.That(door.owner, Is.EqualTo("FridgeDoor"));
            Assert.That(door.root_name, Is.EqualTo("PROP_FridgeDoor"));
            Assert.That(door.lift_root_name, Is.Empty);
            Assert.That(door.liquid_part, Is.Empty);
            Assert.That(
                door.part_names,
                Is.EqualTo(new[] { "Cafe_FridgeDoor" }));

            string[] anchorNames = manifest.anchors
                .Select(anchor => anchor.name)
                .ToArray();
            Assert.That(
                anchorNames,
                Does.Contain("FridgeDoorPivot")
                    .And.Contain("Grip.FridgeDoor")
                    .And.Contain("CuttingBoardDock")
                    .And.Contain("StovePanDock")
                    .And.Contain("Light.ColdService"));
        }

        private static void AssertDoorHeaderClosesFacade(
            CafeManifest manifest)
        {
            CafePart header = manifest.parts.Single(part =>
                part.name == "Cafe_DoorHeaderWall");
            Assert.That(header.role, Is.EqualTo("door_header_wall"));
            Assert.That(header.group, Is.EqualTo("shell"));
            Assert.That(header.sheet, Is.EqualTo("CafeExteriorDetail"));
            Assert.That(header.bounds_min, Has.Length.EqualTo(3));
            Assert.That(header.bounds_max, Has.Length.EqualTo(3));
            Assert.That(
                header.bounds_min[0],
                Is.EqualTo(-4.32f).Within(0.001f));
            Assert.That(
                header.bounds_max[0],
                Is.EqualTo(-2.72f).Within(0.001f));
            Assert.That(
                header.bounds_min[2],
                Is.EqualTo(2.37f).Within(0.001f));
            Assert.That(
                header.bounds_max[2],
                Is.EqualTo(3.78f).Within(0.001f));

            CafePart rails = manifest.parts.Single(part =>
                part.name == "Cafe_WindowRails");
            CafePart luminousBand = manifest.parts.Single(part =>
                part.name == "Cafe_LuminousBand");
            CafePart fascia = manifest.parts.Single(part =>
                part.name == "Cafe_DeepFascia");
            Assert.That(
                luminousBand.bounds_min[2],
                Is.LessThanOrEqualTo(rails.bounds_max[2]),
                "The luminous head must overlap the top window rail.");
            Assert.That(
                luminousBand.bounds_max[2],
                Is.GreaterThanOrEqualTo(fascia.bounds_min[2]),
                "The luminous head must meet the opaque fascia.");
        }

        private static void AssertPassiveImporter()
        {
            ModelImporter importer =
                AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            Assert.That(importer, Is.Not.Null,
                "The authored cafe FBX has not been imported.");
            Assert.That(importer.animationType,
                Is.EqualTo(ModelImporterAnimationType.None));
            Assert.That(importer.importAnimation, Is.False);
            Assert.That(importer.importCameras, Is.False);
            Assert.That(importer.importLights, Is.False);
            Assert.That(importer.addCollider, Is.False);
            Assert.That(importer.importBlendShapes, Is.False);
            Assert.That(importer.bakeAxisConversion, Is.True);
            Assert.That(importer.preserveHierarchy, Is.True);
            Assert.That(importer.optimizeGameObjects, Is.False);
            Assert.That(importer.globalScale, Is.EqualTo(1f));
            Assert.That(importer.materialImportMode,
                Is.EqualTo(ModelImporterMaterialImportMode.None));
        }

        private static void AssertPrefab(
            GameObject instance,
            CafeManifest manifest)
        {
            MountainRoadCafeAssetRegistry registry =
                instance.GetComponent<MountainRoadCafeAssetRegistry>();
            Assert.That(registry, Is.Not.Null);
            Assert.That(registry.ModelRoot, Is.Not.Null);
            Assert.That(registry.DesignId, Is.EqualTo(manifest.design_id));
            Assert.That(registry.BuildSignature,
                Is.EqualTo(manifest.build_signature));
            Assert.That(registry.SourceGeneratorVersion,
                Is.EqualTo(manifest.generator_version));
            Assert.That(registry.SourceTriangleCount,
                Is.EqualTo(ExpectedTriangleCount));
            Assert.That(registry.Parts.Count, Is.EqualTo(ExpectedMeshCount));
            Assert.That(registry.Anchors.Count,
                Is.EqualTo(ExpectedAnchorCount));
            Assert.That(registry.Props.Count,
                Is.EqualTo(ExpectedPropCount));
            Assert.That(registry.Colliders.Count,
                Is.EqualTo(ExpectedColliderDescriptorCount));

            MeshFilter[] filters =
                instance.GetComponentsInChildren<MeshFilter>(true);
            Assert.That(filters, Has.Length.EqualTo(ExpectedMeshCount));
            int measuredTriangles = filters.Sum(filter =>
                filter.sharedMesh != null
                    ? filter.sharedMesh.triangles.Length / 3
                    : 0);
            Assert.That(measuredTriangles, Is.EqualTo(ExpectedTriangleCount));

            Assert.That(instance.GetComponentsInChildren<Collider>(true),
                Is.Empty);
            Assert.That(instance.GetComponentsInChildren<Light>(true),
                Is.Empty);
            Assert.That(instance.GetComponentsInChildren<Camera>(true),
                Is.Empty);
            Assert.That(instance.GetComponentsInChildren<AudioSource>(true),
                Is.Empty);
            Assert.That(instance.GetComponentsInChildren<AudioListener>(true),
                Is.Empty);
            Assert.That(instance.GetComponentsInChildren<Rigidbody>(true),
                Is.Empty);
            Assert.That(instance.GetComponentsInChildren<Animator>(true),
                Is.Empty);

            Assert.That(
                registry.Colliders.Count(descriptor =>
                    descriptor.Shape == MountainRoadCafeColliderShape.Box),
                Is.EqualTo(10));
            Assert.That(
                registry.Colliders.Count(descriptor =>
                    descriptor.Shape == MountainRoadCafeColliderShape.Capsule),
                Is.EqualTo(7));
            Assert.That(registry.TryGetAnchor("DoorThreshold", out _), Is.True);
            Assert.That(registry.TryGetAnchor("CounterCorner", out _), Is.True);
            Assert.That(registry.TryGetAnchor("GlassCorner", out _), Is.True);
            Assert.That(
                registry.TryGetAnchor("Stool.00", out Transform stool),
                Is.True);
            Assert.That(
                stool.position.y - registry.transform.position.y,
                Is.EqualTo(
                    MountainRoadCafeWorldBuilder.StoolSeatTopAboveFloor)
                    .Within(0.001f));
            MountainRoadCafePartBinding stoolSeats = registry.Parts.Single(
                part => part.SourceName == "Cafe_StoolSeats");
            Assert.That(
                stoolSeats.Renderer.bounds.max.y -
                registry.transform.position.y,
                Is.EqualTo(
                    MountainRoadCafeWorldBuilder.StoolSeatTopAboveFloor)
                    .Within(0.001f),
                "The rendered bar stool and its seat anchor must share a top.");

            const float expectedCounterTop = 1.02f;
            foreach (string counterAnchorName in new[]
                     { "CounterStart", "CounterCorner", "CounterEnd" })
            {
                Assert.That(
                    registry.TryGetAnchor(
                        counterAnchorName,
                        out Transform counterAnchor),
                    Is.True);
                Assert.That(
                    counterAnchor.position.y - registry.transform.position.y,
                    Is.EqualTo(expectedCounterTop).Within(0.001f),
                    counterAnchorName);
            }
            MountainRoadCafePartBinding counterTop = registry.Parts.Single(
                part => part.SourceName == "Cafe_CounterTop");
            Assert.That(
                counterTop.Renderer.bounds.max.y -
                registry.transform.position.y,
                Is.EqualTo(expectedCounterTop).Within(0.001f),
                "The sleeper and attendant contacts require the authored " +
                "counter plane at 1.02 m.");

            AssertCupBindings(registry, manifest);
            AssertPassiveKitchenBindings(registry);
            registry.ApplyAppearance();
            AssertSharedTransparentGlass(registry);
            AssertApplianceAppearance(registry);
        }

        private static void AssertApplianceAppearance(
            MountainRoadCafeAssetRegistry registry)
        {
            AssertSurface(
                RequirePart(registry, "Cafe_RefrigeratorBody").Renderer,
                "MountainRoad/Cafe/Textures/MountainRoadCafePropsDetail",
                new Color(0.68f, 0.69f, 0.54f, 1f),
                0.45f,
                0.05f);
            AssertSurface(
                RequirePart(registry, "Cafe_RefrigeratorCavity").Renderer,
                "MountainRoad/Cafe/Textures/MountainRoadCafePropsDetail",
                new Color(0.78f, 0.81f, 0.70f, 1f),
                0.38f,
                0.02f);
            AssertSurface(
                RequirePart(registry, "Cafe_RefrigeratorShelves").Renderer,
                "MountainRoad/Cafe/Textures/MountainRoadCafePropsDetail",
                new Color(0.62f, 0.72f, 0.70f, 1f),
                0.34f,
                0.12f);
            AssertSurface(
                RequirePart(registry, "Cafe_FridgeDoor").Renderer,
                "MountainRoad/Cafe/Textures/MountainRoadCafePropsDetail",
                new Color(0.68f, 0.69f, 0.54f, 1f),
                0.45f,
                0.05f);
            AssertSurface(
                RequirePart(registry, "Cafe_Stove").Renderer,
                "MountainRoad/Cafe/Textures/MountainRoadCafeMetalDetail",
                new Color(0.88f, 0.91f, 0.86f, 1f),
                0.36f,
                0.16f);
            AssertSurface(
                RequirePart(registry, "Cafe_FryingPan").Renderer,
                "MountainRoad/Cafe/Textures/MountainRoadCafeMetalDetail",
                new Color(0.56f, 0.59f, 0.56f, 1f),
                0.30f,
                0.35f);
            AssertColdTaskLens(
                RequirePart(registry, "Cafe_StoveTaskLens"));
        }

        private static void AssertColdTaskLens(
            MountainRoadCafePartBinding lens)
        {
            Assert.That(lens.Emissive, Is.True);
            Assert.That(lens.CastsShadows, Is.False);
            Assert.That(
                lens.Renderer.sharedMaterial,
                Is.SameAs(CityNightResources.EmissiveMaterial));
            var properties = new MaterialPropertyBlock();
            lens.Renderer.GetPropertyBlock(properties);
            Assert.That(
                properties.GetTexture(BaseMapId),
                Is.EqualTo(Texture2D.whiteTexture));
            AssertColorNear(
                properties.GetColor(BaseColorId),
                new Color(0.72f, 2.20f, 1.85f, 1f),
                lens.SourceName);
        }

        private static void AssertSurface(
            Renderer renderer,
            string texturePath,
            Color expectedColor,
            float expectedSmoothness,
            float expectedMetallic)
        {
            Texture2D expectedTexture = Resources.Load<Texture2D>(texturePath);
            Assert.That(expectedTexture, Is.Not.Null, texturePath);
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            Assert.That(
                properties.GetTexture(BaseMapId),
                Is.EqualTo(expectedTexture),
                renderer.name);
            AssertColorNear(
                properties.GetColor(BaseColorId),
                expectedColor,
                renderer.name);
            Assert.That(
                properties.GetFloat(SmoothnessId),
                Is.EqualTo(expectedSmoothness).Within(0.001f),
                renderer.name);
            Assert.That(
                properties.GetFloat(MetallicId),
                Is.EqualTo(expectedMetallic).Within(0.001f),
                renderer.name);
        }

        private static void AssertColorNear(
            Color actual,
            Color expected,
            string context)
        {
            Assert.That(
                actual.r,
                Is.EqualTo(expected.r).Within(0.0001f),
                context);
            Assert.That(
                actual.g,
                Is.EqualTo(expected.g).Within(0.0001f),
                context);
            Assert.That(
                actual.b,
                Is.EqualTo(expected.b).Within(0.0001f),
                context);
            Assert.That(
                actual.a,
                Is.EqualTo(expected.a).Within(0.0001f),
                context);
        }

        private static void AssertPassiveKitchenBindings(
            MountainRoadCafeAssetRegistry registry)
        {
            Assert.That(
                registry.TryGetProp(
                    "FridgeDoor",
                    out MountainRoadCafeDynamicPropBinding fridgeDoor),
                Is.True);
            Assert.That(fridgeDoor.Role, Is.EqualTo("fridge_door"));
            Assert.That(fridgeDoor.Owner, Is.EqualTo("FridgeDoor"));
            Assert.That(fridgeDoor.PropRoot, Is.Not.Null);
            Assert.That(fridgeDoor.LiftRoot, Is.Null);
            Assert.That(fridgeDoor.GripAnchor, Is.Not.Null);
            Assert.That(fridgeDoor.PourTarget, Is.Null);
            Assert.That(fridgeDoor.LiquidTransform, Is.Null);
            Assert.That(fridgeDoor.LiquidRenderer, Is.Null);
            Assert.That(fridgeDoor.Renderers.Count, Is.EqualTo(1));
            Assert.That(
                fridgeDoor.Renderers[0].name,
                Is.EqualTo("Cafe_FridgeDoor"));
            Assert.That(
                fridgeDoor.GripAnchor.IsChildOf(fridgeDoor.PropRoot),
                Is.True,
                "The handle contact must follow the hinge root.");
            Assert.That(
                registry.TryGetAnchor(
                    "FridgeDoorPivot",
                    out Transform fridgeDoorPivot),
                Is.True);
            Assert.That(
                Vector3.Distance(
                    fridgeDoor.PropRoot.position,
                    fridgeDoorPivot.position),
                Is.LessThan(0.002f));

            Renderer fridgeBody = RequirePart(
                registry,
                "Cafe_RefrigeratorBody").Renderer;
            Renderer fridgeLeaf = fridgeDoor.Renderers[0];
            Assert.That(
                fridgeLeaf.bounds.min.x,
                Is.EqualTo(fridgeDoor.PropRoot.position.x).Within(0.03f),
                "The refrigerator leaf root must sit on its hinge edge.");
            Assert.That(
                fridgeLeaf.bounds.max.z,
                Is.EqualTo(fridgeBody.bounds.min.z).Within(0.01f),
                "The closed refrigerator leaf must meet the body.");

            Quaternion closedRotation = fridgeDoor.PropRoot.localRotation;
            Vector3 closedGrip = fridgeDoor.GripAnchor.position;
            Vector3 hinge = fridgeDoor.PropRoot.position;
            fridgeDoor.PropRoot.Rotate(0f, 90f, 0f, Space.Self);
            Assert.That(fridgeDoor.PropRoot.position, Is.EqualTo(hinge));
            Assert.That(
                Vector3.Distance(closedGrip, fridgeDoor.GripAnchor.position),
                Is.GreaterThan(0.50f),
                "The separate leaf must be able to swing around its hinge.");
            fridgeDoor.PropRoot.localRotation = closedRotation;

            Renderer stove = RequirePart(registry, "Cafe_Stove").Renderer;
            Renderer pan = RequirePart(registry, "Cafe_FryingPan").Renderer;
            Renderer board = RequirePart(registry, "Cafe_CuttingBoard").Renderer;
            Renderer fixture = RequirePart(
                registry,
                "Cafe_StoveTaskFixture").Renderer;
            Renderer lens = RequirePart(
                registry,
                "Cafe_StoveTaskLens").Renderer;
            Assert.That(
                registry.TryGetAnchor(
                    "CuttingBoardDock",
                    out Transform boardDock),
                Is.True);
            Assert.That(
                Vector3.Distance(
                    boardDock.position,
                    board.bounds.ClosestPoint(boardDock.position)),
                Is.LessThan(0.01f));
            Assert.That(
                registry.TryGetAnchor(
                    "StovePanDock",
                    out Transform panDock),
                Is.True);
            Assert.That(
                Vector3.Distance(
                    panDock.position,
                    pan.bounds.ClosestPoint(panDock.position)),
                Is.LessThan(0.01f));
            Assert.That(
                registry.TryGetAnchor(
                    "Light.ColdService",
                    out Transform taskLight),
                Is.True);
            Assert.That(
                Vector3.Distance(
                    taskLight.position,
                    lens.bounds.ClosestPoint(taskLight.position)),
                Is.LessThan(0.01f),
                "The cold service light must originate inside the task lens.");
            Assert.That(
                fixture.bounds.min.y,
                Is.GreaterThan(stove.bounds.max.y + 0.90f));
            Assert.That(
                HorizontalDistance(fixture.bounds.center, stove.bounds.center),
                Is.LessThan(0.04f),
                "The visible task fixture must stay directly above the stove.");
        }

        private static MountainRoadCafePartBinding RequirePart(
            MountainRoadCafeAssetRegistry registry,
            string sourceName)
        {
            MountainRoadCafePartBinding part = registry.Parts.Single(candidate =>
                candidate.SourceName == sourceName);
            Assert.That(part.Renderer, Is.Not.Null, sourceName);
            return part;
        }

        private static float HorizontalDistance(Vector3 first, Vector3 second)
        {
            return Vector2.Distance(
                new Vector2(first.x, first.z),
                new Vector2(second.x, second.z));
        }

        private static void AssertCupBindings(
            MountainRoadCafeAssetRegistry registry,
            CafeManifest manifest)
        {
            Assert.That(registry.TryGetProp("Cup.Lone", out _), Is.False,
                "The sleeping door-side patron must not retain hidden cup data.");
            Assert.That(registry.TryGetAnchor("Grip.Lone", out _), Is.False);
            Assert.That(
                registry.TryGetAnchor("PourTarget.Lone", out _),
                Is.False);
            foreach (string owner in new[] { "PairMan", "PairWoman" })
            {
                CafeDynamicProp authored = manifest.dynamic_props.Single(prop =>
                    string.Equals(
                        prop.name,
                        $"Cup.{owner}",
                        StringComparison.Ordinal));
                Assert.That(
                    registry.TryGetProp(
                        $"Cup.{owner}",
                        out MountainRoadCafeDynamicPropBinding cup),
                    Is.True);
                Assert.That(cup.PropRoot, Is.Not.Null);
                Assert.That(cup.LiftRoot, Is.Not.Null);
                Assert.That(cup.GripAnchor, Is.Not.Null);
                Assert.That(cup.PourTarget, Is.Not.Null);
                Assert.That(cup.LiquidTransform, Is.Not.Null);
                Assert.That(cup.LiquidRenderer, Is.Not.Null);
                Assert.That(cup.FillTravelDistance, Is.GreaterThan(0.001f));
                Transform fillParent = cup.LiquidTransform.parent;
                Vector3 emptyWorld = fillParent.TransformPoint(
                    cup.EmptyLocalPosition);
                Vector3 fullWorld = fillParent.TransformPoint(
                    cup.FullLocalPosition);
                Assert.That(
                    Vector3.Dot(fullWorld - emptyWorld, registry.transform.up),
                    Is.EqualTo(0.079f).Within(0.002f));
                Assert.That(cup.LiquidTransform.parent,
                    Is.EqualTo(cup.LiftRoot));
                Vector3 gripOffset =
                    registry.transform.InverseTransformVector(
                        cup.GripAnchor.position - cup.LiftRoot.position);
                float expectedGripX = owner == "PairMan" ? 0.092f : -0.092f;
                Assert.That(
                    gripOffset.x,
                    Is.EqualTo(expectedGripX).Within(0.002f),
                    $"{owner} handle did not reverse to its requested side.");
                Vector3 emptyOffset = registry.transform.InverseTransformVector(
                    emptyWorld - cup.LiftRoot.position);
                Vector3 fullOffset = registry.transform.InverseTransformVector(
                    fullWorld - cup.LiftRoot.position);
                Assert.That(
                    emptyOffset.y,
                    Is.EqualTo(authored.empty_local_y).Within(0.002f),
                    $"{owner} empty coffee level must stay local to its cup.");
                Assert.That(
                    fullOffset.y,
                    Is.EqualTo(authored.full_local_y).Within(0.002f),
                    $"{owner} full coffee level must stay local to its cup.");
                Assert.That(
                    new Vector2(emptyOffset.x, emptyOffset.z).magnitude,
                    Is.LessThan(0.002f),
                    $"{owner} empty coffee level cannot drift sideways.");
                Assert.That(
                    new Vector2(fullOffset.x, fullOffset.z).magnitude,
                    Is.LessThan(0.002f),
                    $"{owner} full coffee level cannot drift sideways.");

                Renderer saucer = cup.Renderers.Single(renderer =>
                    renderer.name.EndsWith("_Saucer", StringComparison.Ordinal));
                Assert.That(saucer.transform.IsChildOf(cup.LiftRoot), Is.False,
                    "Drinking must lift the cup and liquid, not the saucer.");
            }
        }

        private static void AssertSharedTransparentGlass(
            MountainRoadCafeAssetRegistry registry)
        {
            MountainRoadCafePartBinding[] glass = registry.Parts
                .Where(part => part.Sheet == "CafeGlassDetail")
                .ToArray();
            Assert.That(glass, Is.Not.Empty);

            Material expected = HomeBalconyResources.GlassMaterial;
            Assert.That(expected, Is.Not.Null);
            Assert.That(expected.renderQueue, Is.GreaterThanOrEqualTo(3000),
                "The shared Home balcony glass must stay transparent.");
            foreach (MountainRoadCafePartBinding part in glass)
            {
                Assert.That(part.Renderer, Is.Not.Null);
                Assert.That(part.Renderer.sharedMaterial,
                    Is.SameAs(expected),
                    $"'{part.SourceName}' does not use shared window glass.");
            }
        }

        private static CafeManifest LoadManifest()
        {
            TextAsset source =
                AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            Assert.That(source, Is.Not.Null,
                "Run the deterministic Blender cafe generator first.");
            CafeManifest manifest =
                JsonUtility.FromJson<CafeManifest>(source.text);
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.parts, Is.Not.Null);
            Assert.That(manifest.anchors, Is.Not.Null);
            Assert.That(manifest.dynamic_props, Is.Not.Null);
            Assert.That(manifest.collider_descriptors, Is.Not.Null);
            Assert.That(manifest.textures, Is.Not.Null);
            return manifest;
        }

        [Serializable]
        private sealed class CafeManifest
        {
            public string generator_version;
            public string design_id;
            public string build_signature;
            public bool colliders;
            public bool lights;
            public bool cameras;
            public bool materials;
            public int animation_count;
            public int mesh_count;
            public int triangle_count;
            public int overlap_count;
            public int stool_count;
            public int cup_assembly_count;
            public CafeKitchenContract kitchen_contract;
            public CafeTexture[] textures;
            public CafePart[] parts;
            public CafeAnchor[] anchors;
            public CafeDynamicProp[] dynamic_props;
            public CafeCollider[] collider_descriptors;
        }

        [Serializable]
        private sealed class CafeKitchenContract
        {
            public float visible_rear_lining_z_m;
            public float worktop_wall_gap_m;
            public float cabinet_wall_gap_m;
            public float rear_door_clearance_m;
        }

        [Serializable]
        private sealed class CafeTexture
        {
            public string sheet;
            public int width;
            public int height;
        }

        [Serializable]
        private sealed class CafePart
        {
            public string name;
            public string role;
            public string group;
            public string sheet;
            public string base_surface;
            public string uv_strategy;
            public bool emissive;
            public bool shadows;
            public float[] bounds_min;
            public float[] bounds_max;
        }

        [Serializable]
        private sealed class CafeAnchor
        {
            public string name;
        }

        [Serializable]
        private sealed class CafeDynamicProp
        {
            public string name;
            public string root_name;
            public string lift_root_name;
            public string role;
            public string owner;
            public string[] part_names;
            public string liquid_part;
            public float empty_local_y;
            public float full_local_y;
        }

        [Serializable]
        private sealed class CafeCollider
        {
            public string id;
        }
    }
}
