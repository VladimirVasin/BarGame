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
        private const int ExpectedMeshCount = 48;
        private const int ExpectedTriangleCount = 4568;
        private const int ExpectedAnchorCount = 41;
        private const int ExpectedPropCount = 5;
        private const int ExpectedColliderDescriptorCount = 17;

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
            Assert.That(manifest.stool_count, Is.EqualTo(7));
            Assert.That(manifest.cup_assembly_count, Is.EqualTo(2));
            Assert.That(manifest.colliders, Is.False);
            Assert.That(manifest.lights, Is.False);
            Assert.That(manifest.cameras, Is.False);
            Assert.That(manifest.materials, Is.False);
            Assert.That(manifest.animation_count, Is.Zero);
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
            registry.ApplyAppearance();
            AssertSharedTransparentGlass(registry);
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
            public CafeTexture[] textures;
            public CafePart[] parts;
            public CafeAnchor[] anchors;
            public CafeDynamicProp[] dynamic_props;
            public CafeCollider[] collider_descriptors;
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
