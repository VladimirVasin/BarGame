using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Holds the joint between the normalized Blender misc kit, its one
    /// Resources provider and the runtime-composed Mountain Road batches.
    /// </summary>
    public sealed class MountainRoadMiscAssetTests
    {
        private const string ModelPath =
            "Assets/MountainRoad/Models/MountainRoadMisc3D.fbx";
        private const string ManifestPath =
            "Assets/MountainRoad/Models/MountainRoadMisc3D.json";
        private const float BoundsTolerance = 0.003f;
        private const float UvTolerance = 0.0001f;

        private static readonly ExpectedPart[] ExpectedParts =
        {
            new ExpectedPart(
                "GEO_MRM_SnowPole_Body",
                MountainRoadMiscKind.SnowPole,
                0,
                0),
            new ExpectedPart(
                "GEO_MRM_SnowPole_Band",
                MountainRoadMiscKind.SnowPole,
                0,
                1),
            new ExpectedPart(
                "GEO_MRM_FallenLog_Variant01_Wood",
                MountainRoadMiscKind.FallenLog,
                0,
                0),
            new ExpectedPart(
                "GEO_MRM_FallenLog_Variant02_Wood",
                MountainRoadMiscKind.FallenLog,
                1,
                0),
            new ExpectedPart(
                "GEO_MRM_FallenLog_Variant03_Wood",
                MountainRoadMiscKind.FallenLog,
                2,
                0),
            new ExpectedPart(
                "GEO_MRM_Stump_Variant01_Wood",
                MountainRoadMiscKind.Stump,
                0,
                0),
            new ExpectedPart(
                "GEO_MRM_Stump_Variant02_Wood",
                MountainRoadMiscKind.Stump,
                1,
                0),
            new ExpectedPart(
                "GEO_MRM_Stump_Variant03_Wood",
                MountainRoadMiscKind.Stump,
                2,
                0),
            new ExpectedPart(
                "GEO_MRM_Stump_Variant04_Wood",
                MountainRoadMiscKind.Stump,
                3,
                0),
            new ExpectedPart(
                "GEO_MRM_DeadTree_Variant01_Wood",
                MountainRoadMiscKind.DeadTree,
                0,
                0),
            new ExpectedPart(
                "GEO_MRM_DeadTree_Variant02_Wood",
                MountainRoadMiscKind.DeadTree,
                1,
                0),
            new ExpectedPart(
                "GEO_MRM_DeadTree_Variant03_Wood",
                MountainRoadMiscKind.DeadTree,
                2,
                0),
            new ExpectedPart(
                "GEO_MRM_GuardRail_Iron",
                MountainRoadMiscKind.GuardRail,
                0,
                0),
            new ExpectedPart(
                "GEO_MRM_ConvexMirror_Pole",
                MountainRoadMiscKind.ConvexMirror,
                0,
                0),
            new ExpectedPart(
                "GEO_MRM_ConvexMirror_Frame",
                MountainRoadMiscKind.ConvexMirror,
                0,
                1),
            new ExpectedPart(
                "GEO_MRM_ConvexMirror_Face",
                MountainRoadMiscKind.ConvexMirror,
                0,
                2),
            new ExpectedPart(
                "GEO_MRM_UtilityCabinet_Body",
                MountainRoadMiscKind.UtilityCabinet,
                0,
                0),
            new ExpectedPart(
                "GEO_MRM_UtilityCabinet_Trim",
                MountainRoadMiscKind.UtilityCabinet,
                0,
                1),
            new ExpectedPart(
                "GEO_MRM_AbandonedChair_Wood",
                MountainRoadMiscKind.AbandonedChair,
                0,
                0)
        };

        private static readonly MountainRoadMiscKind[] MigratedKinds =
        {
            MountainRoadMiscKind.FallenLog,
            MountainRoadMiscKind.Stump,
            MountainRoadMiscKind.DeadTree,
            MountainRoadMiscKind.GuardRail,
            MountainRoadMiscKind.SnowPole,
            MountainRoadMiscKind.ConvexMirror,
            MountainRoadMiscKind.UtilityCabinet,
            MountainRoadMiscKind.AbandonedChair
        };

        [Test]
        public void Manifest_DeclaresTheExactPassiveNormalizedWave()
        {
            MiscManifest manifest = LoadManifest();

            Assert.That(
                manifest.design_id,
                Is.EqualTo(MountainRoadMiscAssetProvider.DesignId));
            Assert.That(manifest.colliders, Is.False);
            Assert.That(manifest.lights, Is.False);
            Assert.That(manifest.cameras, Is.False);
            Assert.That(manifest.animation_count, Is.Zero);
            Assert.That(
                manifest.mesh_count,
                Is.EqualTo(MountainRoadMiscAssetProvider.ExpectedMeshCount));
            Assert.That(manifest.assembly_count, Is.EqualTo(15));
            Assert.That(manifest.parts, Has.Length.EqualTo(19));
            Assert.That(manifest.assemblies, Has.Length.EqualTo(15));
            Assert.That(IsSha256(manifest.build_signature), Is.True);

            Assert.That(manifest.source_axes.right, Is.EqualTo("+X"));
            Assert.That(manifest.source_axes.forward, Is.EqualTo("+Y"));
            Assert.That(manifest.source_axes.up, Is.EqualTo("+Z"));
            Assert.That(manifest.unity_axes.right, Is.EqualTo("+X"));
            Assert.That(manifest.unity_axes.forward, Is.EqualTo("+Z"));
            Assert.That(manifest.unity_axes.up, Is.EqualTo("+Y"));
            Assert.That(manifest.unity_axes.fbx_axis_forward, Is.EqualTo("-Z"));
            Assert.That(manifest.unity_axes.fbx_axis_up, Is.EqualTo("+Y"));
            Assert.That(manifest.unity_axes.bake_space_transform, Is.True);

            Assert.That(
                manifest.root_contract.origin,
                Is.EqualTo("descriptor_center"));
            Assert.That(
                manifest.root_contract.source_ground_axis,
                Is.EqualTo("Z"));
            Assert.That(
                manifest.root_contract.unity_ground_axis,
                Is.EqualTo("Y"));
            Assert.That(
                manifest.root_contract.source_ground_value,
                Is.EqualTo(-0.5f).Within(0.0001f));
            Assert.That(
                manifest.root_contract.unity_ground_value,
                Is.EqualTo(-0.5f).Within(0.0001f));
            AssertVector(
                manifest.root_contract.normalized_descriptor_min,
                new Vector3(-0.5f, -0.5f, -0.5f),
                0.0001f,
                "normalized minimum");
            AssertVector(
                manifest.root_contract.normalized_descriptor_max,
                new Vector3(0.5f, 0.5f, 0.5f),
                0.0001f,
                "normalized maximum");

            HashSet<string> expectedMeshes = ExpectedParts
                .Select(part => part.MeshName)
                .ToHashSet(StringComparer.Ordinal);
            Assert.That(
                manifest.parts.Select(part => part.mesh),
                Is.EquivalentTo(expectedMeshes));
            Assert.That(
                manifest.parts.Select(part => part.mesh).Distinct().Count(),
                Is.EqualTo(19));
            Assert.That(
                manifest.parts.Sum(part => part.triangles),
                Is.EqualTo(manifest.triangle_count));

            foreach (MiscManifestPart part in manifest.parts)
            {
                Assert.That(part.vertices, Is.GreaterThan(0), part.mesh);
                Assert.That(part.triangles, Is.GreaterThan(0), part.mesh);
                AssertBoundsArrays(part, part.mesh);
                Assert.That(part.uv_min, Has.Length.EqualTo(2), part.mesh);
                Assert.That(part.uv_max, Has.Length.EqualTo(2), part.mesh);
                Assert.That(
                    part.uv_max[0] - part.uv_min[0],
                    Is.GreaterThan(0.0001f),
                    part.mesh);
                Assert.That(
                    part.uv_max[1] - part.uv_min[1],
                    Is.GreaterThan(0.0001f),
                    part.mesh);
                AssertSourceToUnityBounds(part);
            }

            HashSet<string> expectedAssemblies = ExpectedParts
                .Select(part => AssemblyKey(part.Kind, part.Variant))
                .ToHashSet(StringComparer.Ordinal);
            Assert.That(
                manifest.assemblies.Select(assembly =>
                    AssemblyKey(assembly.kind, assembly.variant)),
                Is.EquivalentTo(expectedAssemblies));
            foreach (MiscManifestAssembly assembly in manifest.assemblies)
            {
                string label = AssemblyKey(
                    assembly.kind,
                    assembly.variant);
                Assert.That(assembly.part_meshes, Is.Not.Empty, label);
                Assert.That(
                    assembly.part_meshes.All(expectedMeshes.Contains),
                    Is.True,
                    label);
                Assert.That(
                    assembly.scale_mode,
                    Is.EqualTo(assembly.kind == "DeadTree"
                        ? "uniform_by_height"
                        : "normalized_to_descriptor"),
                    label);
                AssertAssemblyNormalized(assembly, label);
            }
        }

        [Test]
        public void Fbx_ImportsTheExactReadableUvMappedPassiveMeshSet()
        {
            MiscManifest manifest = LoadManifest();
            ModelImporter importer =
                AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            Assert.That(importer, Is.Not.Null, "the misc FBX did not import");
            Assert.That(importer.globalScale, Is.EqualTo(1f));
            Assert.That(importer.bakeAxisConversion, Is.True);
            Assert.That(importer.preserveHierarchy, Is.True);
            Assert.That(importer.optimizeGameObjects, Is.False);
            Assert.That(
                importer.animationType,
                Is.EqualTo(ModelImporterAnimationType.None));
            Assert.That(importer.importAnimation, Is.False);
            Assert.That(importer.importCameras, Is.False);
            Assert.That(importer.importLights, Is.False);
            Assert.That(importer.addCollider, Is.False);
            Assert.That(importer.isReadable, Is.True);
            Assert.That(
                importer.materialImportMode,
                Is.EqualTo(ModelImporterMaterialImportMode.None));

            UnityEngine.Object[] imported =
                AssetDatabase.LoadAllAssetsAtPath(ModelPath);
            Assert.That(imported.OfType<Material>(), Is.Empty);
            Assert.That(imported.OfType<AnimationClip>(), Is.Empty);

            GameObject model =
                AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Assert.That(model, Is.Not.Null);
            Assert.That(
                model.GetComponentsInChildren<Collider>(true),
                Is.Empty);
            Assert.That(model.GetComponentsInChildren<Light>(true), Is.Empty);
            Assert.That(model.GetComponentsInChildren<Camera>(true), Is.Empty);
            Assert.That(model.GetComponentsInChildren<Animator>(true), Is.Empty);
            Assert.That(
                model.GetComponentsInChildren<Rigidbody>(true),
                Is.Empty);

            Dictionary<string, Mesh> meshes = imported
                .OfType<Mesh>()
                .ToDictionary(mesh => mesh.name, StringComparer.Ordinal);
            Assert.That(meshes, Has.Count.EqualTo(19));
            Assert.That(
                meshes.Keys,
                Is.EquivalentTo(ExpectedParts.Select(part => part.MeshName)));

            int triangleTotal = 0;
            foreach (MiscManifestPart source in manifest.parts)
            {
                Assert.That(
                    meshes.TryGetValue(source.mesh, out Mesh mesh),
                    Is.True,
                    source.mesh);
                Assert.That(mesh.isReadable, Is.True, source.mesh);
                Assert.That(mesh.vertexCount, Is.GreaterThan(0), source.mesh);
                Assert.That(
                    mesh.uv,
                    Has.Length.EqualTo(mesh.vertexCount),
                    source.mesh + " UV0");
                int triangles = CountTriangles(mesh, source.mesh);
                Assert.That(
                    triangles,
                    Is.EqualTo(source.triangles),
                    source.mesh);
                AssertBounds(mesh.bounds, source, source.mesh);
                AssertUvBounds(mesh.uv, source, source.mesh);
                triangleTotal += triangles;
            }

            Assert.That(
                triangleTotal,
                Is.EqualTo(manifest.triangle_count));
        }

        [Test]
        public void Provider_CarriesEveryAuthoredMeshAndCurrentSignature()
        {
            MiscManifest manifest = LoadManifest();
            MountainRoadMiscAssetProvider provider =
                MountainRoadMiscAssetProvider.Load();

            Assert.That(
                provider,
                Is.Not.Null,
                "Run Mountain Road misc Unity asset setup after Blender.");
            Assert.That(provider.HasCompleteMeshes, Is.True);
            Assert.That(
                provider.BuildSignature,
                Is.EqualTo(manifest.build_signature),
                "The Resources provider was bound against another build.");
            Assert.DoesNotThrow(provider.ValidateOrThrow);

            foreach (ExpectedPart expected in ExpectedParts)
            {
                Assert.That(
                    MountainRoadMiscAssetProvider.GetExpectedMeshName(
                        expected.Kind,
                        expected.Variant,
                        expected.PartIndex),
                    Is.EqualTo(expected.MeshName));
            }

            foreach (MountainRoadMiscKind kind in MigratedKinds)
            {
                int partCount = provider.GetPartCount(kind);
                for (int part = 0; part < partCount; part++)
                {
                    MountainRoadMiscMeshPart binding =
                        provider.GetPartOrThrow(
                            kind,
                            "asset-contract-" + kind,
                            part);
                    Assert.That(binding.Mesh, Is.Not.Null, kind.ToString());
                    Assert.That(binding.Mesh.isReadable, Is.True, kind.ToString());
                }
            }
        }

        [Test]
        [Category("MountainRoad")]
        public void DefaultWorld_Migrates102InstancesIntoTwelveNamedBatches()
        {
            MountainRoadPlan plan = MountainRoadPlanner.Create(
                GameSessionState.DefaultCitySeed);
            var expectedCounts = new Dictionary<MountainRoadMiscKind, int>
            {
                { MountainRoadMiscKind.FallenLog, 24 },
                { MountainRoadMiscKind.Stump, 28 },
                { MountainRoadMiscKind.DeadTree, 16 },
                { MountainRoadMiscKind.GuardRail, 11 },
                { MountainRoadMiscKind.SnowPole, 20 },
                { MountainRoadMiscKind.ConvexMirror, 1 },
                { MountainRoadMiscKind.UtilityCabinet, 1 },
                { MountainRoadMiscKind.AbandonedChair, 1 }
            };

            List<MountainRoadMiscDescriptor> migrated = plan.Misc
                .Where(item =>
                    MountainRoadMiscAssetProvider.Supports(item.Kind))
                .ToList();
            Assert.That(migrated, Has.Count.EqualTo(102));
            foreach (KeyValuePair<MountainRoadMiscKind, int> expected in
                     expectedCounts)
            {
                Assert.That(
                    migrated.Count(item => item.Kind == expected.Key),
                    Is.EqualTo(expected.Value),
                    expected.Key.ToString());
            }

            var parent = new GameObject("Mountain Misc Asset Test");
            try
            {
                MountainRoadWorldResult world = MountainRoadWorldBuilder.Build(
                    parent.transform,
                    plan,
                    null);
                Transform miscRoot = world.PhysicalRoot.transform.Find(
                    "Authored Forest Misc");
                Assert.That(miscRoot, Is.Not.Null);

                var expectedBatchNames = new HashSet<string>(
                    new[]
                    {
                        "Imported Fallen Logs",
                        "Imported Cut Stumps",
                        "Imported Dead Trees",
                        "Imported Guard Rails",
                        "Imported Snow Pole Bodies",
                        "Imported Snow Pole Bands",
                        "Imported Convex Mirror ConvexMirrorPole",
                        "Imported Convex Mirror ConvexMirrorFrame",
                        "Imported Convex Mirror ConvexMirrorFace",
                        "Imported Utility Cabinet UtilityCabinetBody",
                        "Imported Utility Cabinet UtilityCabinetTrim",
                        "Imported Abandoned Chair"
                    },
                    StringComparer.Ordinal);
                Renderer[] migratedRenderers = miscRoot
                    .GetComponentsInChildren<Renderer>(true)
                    .Where(renderer =>
                        renderer.name.StartsWith(
                            "Imported ",
                            StringComparison.Ordinal))
                    .ToArray();
                Assert.That(
                    migratedRenderers,
                    Has.Length.EqualTo(12),
                    "The 102 instances must stay inside the twelve " +
                    "kind/part batches, not become prefab renderers.");
                Assert.That(
                    migratedRenderers.Select(renderer => renderer.name),
                    Is.EquivalentTo(expectedBatchNames));
                Assert.That(
                    migratedRenderers.All(renderer =>
                        renderer.GetComponent<MeshFilter>()?.sharedMesh != null),
                    Is.True);

                AssertNoLegacyMigratedRendererNames(miscRoot);
                AssertSemanticRoots(world, migrated);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        private static void AssertSemanticRoots(
            MountainRoadWorldResult world,
            IReadOnlyList<MountainRoadMiscDescriptor> migrated)
        {
            foreach (MountainRoadMiscDescriptor item in migrated)
            {
                bool shouldBeSemantic =
                    item.Kind == MountainRoadMiscKind.GuardRail ||
                    item.Kind == MountainRoadMiscKind.SnowPole ||
                    item.Kind == MountainRoadMiscKind.ConvexMirror ||
                    item.Kind == MountainRoadMiscKind.UtilityCabinet ||
                    item.Kind == MountainRoadMiscKind.AbandonedChair;
                Assert.That(
                    world.SemanticObjects.ContainsKey(item.StableId),
                    Is.EqualTo(shouldBeSemantic),
                    item.StableId);
                if (!shouldBeSemantic)
                {
                    continue;
                }

                Transform semantic = world.SemanticObjects[item.StableId];
                Assert.That(semantic.name, Is.EqualTo(item.StableId));
                Assert.That(
                    semantic.position,
                    Is.EqualTo(item.Position),
                    item.StableId);
                Assert.That(
                    semantic.GetComponentsInChildren<Renderer>(true),
                    Is.Empty,
                    "Semantic roots retain only gameplay/collision; imported " +
                    "visuals belong to the combined batches.");
            }

            Assert.That(
                world.SemanticObjects.ContainsKey("misc-hairpin-mirror"),
                Is.True);
            Assert.That(
                world.SemanticObjects.ContainsKey("misc-utility-cabinet"),
                Is.True);
            Assert.That(
                world.SemanticObjects.ContainsKey("misc-abandoned-chair"),
                Is.True);
        }

        private static void AssertNoLegacyMigratedRendererNames(
            Transform miscRoot)
        {
            var legacyVisibleNames = new HashSet<string>(
                new[]
                {
                    "Fallen Logs",
                    "Cut Stumps",
                    "Dead Trees",
                    "Loose Iron Beam",
                    "Guard Post",
                    "Mirror Pole",
                    "Cracked Convex Mirror",
                    "Service Cabinet",
                    "Cabinet Door Seam",
                    "Bent Snow Pole",
                    "Faded White Band",
                    "Chair Seat",
                    "Chair Back",
                    "Chair Leg"
                },
                StringComparer.Ordinal);
            Renderer[] renderers =
                miscRoot.GetComponentsInChildren<Renderer>(true);
            Assert.That(
                renderers.Where(renderer =>
                    legacyVisibleNames.Contains(renderer.name)),
                Is.Empty,
                "Collider proxy names may remain, but no migrated legacy " +
                "primitive may still be visible.");
        }

        private static MiscManifest LoadManifest()
        {
            TextAsset source =
                AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            Assert.That(
                source,
                Is.Not.Null,
                $"'{ManifestPath}' is missing; run the Blender generator.");
            MiscManifest manifest =
                JsonUtility.FromJson<MiscManifest>(source.text);
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.source_axes, Is.Not.Null);
            Assert.That(manifest.unity_axes, Is.Not.Null);
            Assert.That(manifest.root_contract, Is.Not.Null);
            Assert.That(manifest.parts, Is.Not.Null);
            Assert.That(manifest.assemblies, Is.Not.Null);
            return manifest;
        }

        private static int CountTriangles(Mesh mesh, string label)
        {
            long indices = 0;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                Assert.That(
                    mesh.GetTopology(subMesh),
                    Is.EqualTo(MeshTopology.Triangles),
                    label);
                Assert.That(
                    mesh.GetIndexCount(subMesh) % 3,
                    Is.Zero,
                    label);
                indices += (long)mesh.GetIndexCount(subMesh);
            }

            return (int)(indices / 3);
        }

        private static void AssertBounds(
            Bounds actual,
            MiscManifestPart expected,
            string label)
        {
            AssertVector(
                actual.min,
                expected.bounds_min_unity,
                BoundsTolerance,
                label + " minimum");
            AssertVector(
                actual.max,
                expected.bounds_max_unity,
                BoundsTolerance,
                label + " maximum");
        }

        private static void AssertUvBounds(
            IReadOnlyList<Vector2> uv,
            MiscManifestPart expected,
            string label)
        {
            Vector2 minimum = uv[0];
            Vector2 maximum = uv[0];
            for (int index = 1; index < uv.Count; index++)
            {
                minimum = Vector2.Min(minimum, uv[index]);
                maximum = Vector2.Max(maximum, uv[index]);
            }

            Assert.That(
                minimum.x,
                Is.EqualTo(expected.uv_min[0]).Within(UvTolerance),
                label);
            Assert.That(
                minimum.y,
                Is.EqualTo(expected.uv_min[1]).Within(UvTolerance),
                label);
            Assert.That(
                maximum.x,
                Is.EqualTo(expected.uv_max[0]).Within(UvTolerance),
                label);
            Assert.That(
                maximum.y,
                Is.EqualTo(expected.uv_max[1]).Within(UvTolerance),
                label);
        }

        private static void AssertBoundsArrays(
            MiscManifestPart part,
            string label)
        {
            Assert.That(part.bounds_min_source, Has.Length.EqualTo(3), label);
            Assert.That(part.bounds_max_source, Has.Length.EqualTo(3), label);
            Assert.That(part.bounds_min_unity, Has.Length.EqualTo(3), label);
            Assert.That(part.bounds_max_unity, Has.Length.EqualTo(3), label);
            for (int axis = 0; axis < 3; axis++)
            {
                Assert.That(
                    part.bounds_min_source[axis],
                    Is.LessThanOrEqualTo(part.bounds_max_source[axis]),
                    label);
                Assert.That(
                    part.bounds_min_unity[axis],
                    Is.LessThanOrEqualTo(part.bounds_max_unity[axis]),
                    label);
            }
        }

        private static void AssertSourceToUnityBounds(
            MiscManifestPart part)
        {
            AssertVector(
                part.bounds_min_unity,
                new Vector3(
                    part.bounds_min_source[0],
                    part.bounds_min_source[2],
                    part.bounds_min_source[1]),
                0.0001f,
                part.mesh + " source-to-Unity minimum");
            AssertVector(
                part.bounds_max_unity,
                new Vector3(
                    part.bounds_max_source[0],
                    part.bounds_max_source[2],
                    part.bounds_max_source[1]),
                0.0001f,
                part.mesh + " source-to-Unity maximum");
        }

        private static void AssertAssemblyNormalized(
            MiscManifestAssembly assembly,
            string label)
        {
            Assert.That(
                assembly.bounds_min_unity,
                Has.Length.EqualTo(3),
                label);
            Assert.That(
                assembly.bounds_max_unity,
                Has.Length.EqualTo(3),
                label);
            Vector3 minimum = Vector3From(assembly.bounds_min_unity);
            Vector3 maximum = Vector3From(assembly.bounds_max_unity);
            Assert.That(minimum.x, Is.GreaterThanOrEqualTo(-0.5001f), label);
            Assert.That(minimum.y, Is.EqualTo(-0.5f).Within(0.0001f), label);
            Assert.That(minimum.z, Is.GreaterThanOrEqualTo(-0.5001f), label);
            Assert.That(maximum.x, Is.LessThanOrEqualTo(0.5001f), label);
            Assert.That(maximum.y, Is.LessThanOrEqualTo(0.5001f), label);
            Assert.That(maximum.z, Is.LessThanOrEqualTo(0.5001f), label);
        }

        private static void AssertVector(
            float[] actual,
            Vector3 expected,
            float tolerance,
            string label)
        {
            Assert.That(actual, Has.Length.EqualTo(3), label);
            AssertVector(Vector3From(actual), expected, tolerance, label);
        }

        private static void AssertVector(
            Vector3 actual,
            float[] expected,
            float tolerance,
            string label)
        {
            Assert.That(expected, Has.Length.EqualTo(3), label);
            AssertVector(actual, Vector3From(expected), tolerance, label);
        }

        private static void AssertVector(
            Vector3 actual,
            Vector3 expected,
            float tolerance,
            string label)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance), label);
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance), label);
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(tolerance), label);
        }

        private static Vector3 Vector3From(float[] value)
        {
            return new Vector3(value[0], value[1], value[2]);
        }

        private static string AssemblyKey(
            MountainRoadMiscKind kind,
            int variant)
        {
            return AssemblyKey(kind.ToString(), variant);
        }

        private static string AssemblyKey(string kind, int variant)
        {
            return $"{kind}#{variant}";
        }

        private static bool IsSha256(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.Length == 64 &&
                   value.All(Uri.IsHexDigit);
        }

        private sealed class ExpectedPart
        {
            public ExpectedPart(
                string meshName,
                MountainRoadMiscKind kind,
                int variant,
                int partIndex)
            {
                MeshName = meshName;
                Kind = kind;
                Variant = variant;
                PartIndex = partIndex;
            }

            public string MeshName { get; }
            public MountainRoadMiscKind Kind { get; }
            public int Variant { get; }
            public int PartIndex { get; }
        }

        [Serializable]
        private sealed class MiscManifest
        {
            public string design_id;
            public MiscSourceAxes source_axes;
            public MiscUnityAxes unity_axes;
            public MiscRootContract root_contract;
            public bool colliders;
            public bool lights;
            public bool cameras;
            public int animation_count;
            public int mesh_count;
            public int assembly_count;
            public int triangle_count;
            public MiscManifestAssembly[] assemblies;
            public MiscManifestPart[] parts;
            public string build_signature;
        }

        [Serializable]
        private sealed class MiscSourceAxes
        {
            public string right;
            public string forward;
            public string up;
        }

        [Serializable]
        private sealed class MiscUnityAxes
        {
            public string right;
            public string forward;
            public string up;
            public string fbx_axis_forward;
            public string fbx_axis_up;
            public bool bake_space_transform;
        }

        [Serializable]
        private sealed class MiscRootContract
        {
            public string origin;
            public string source_ground_axis;
            public float source_ground_value;
            public string unity_ground_axis;
            public float unity_ground_value;
            public float[] normalized_descriptor_min;
            public float[] normalized_descriptor_max;
        }

        [Serializable]
        private sealed class MiscManifestAssembly
        {
            public string kind;
            public int variant;
            public string scale_mode;
            public string[] part_meshes;
            public float[] bounds_min_unity;
            public float[] bounds_max_unity;
        }

        [Serializable]
        private sealed class MiscManifestPart
        {
            public string mesh;
            public int vertices;
            public int triangles;
            public float[] bounds_min_source;
            public float[] bounds_max_source;
            public float[] bounds_min_unity;
            public float[] bounds_max_unity;
            public float[] uv_min;
            public float[] uv_max;
        }
    }
}
