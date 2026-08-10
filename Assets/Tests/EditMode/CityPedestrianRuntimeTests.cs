using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityPedestrianRuntimeTests
    {
        private const string ModelPath =
            "Assets/Pedestrians/Models/CityPedestrian3D.fbx";
        private const string PlayerModelPath =
            "Assets/Player3D/Models/PlayerCharacter3D.fbx";
        private const string PlayerAnimationPath =
            "Assets/Player3D/Animations/PlayerCharacter3DAnimations.fbx";
        private const string SharedMaterialPath =
            "Assets/Player3D/Materials/Player3DLit.mat";

        [Test]
        public void ProductionPrefab_ReusesPlayerRigClipsAndGroundsWalk()
        {
            ModelImporter importer =
                AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(
                importer.animationType,
                Is.EqualTo(ModelImporterAnimationType.Generic));
            Assert.That(
                importer.avatarSetup,
                Is.EqualTo(ModelImporterAvatarSetup.CopyFromOther));
            Assert.That(importer.sourceAvatar, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(importer.sourceAvatar),
                Is.EqualTo(PlayerModelPath));
            Assert.That(importer.importAnimation, Is.False);
            Assert.That(importer.preserveHierarchy, Is.True);
            Assert.That(importer.optimizeGameObjects, Is.False);
            Assert.That(importer.addCollider, Is.False);
            Assert.That(
                AssetDatabase.LoadAllAssetsAtPath(ModelPath)
                    .OfType<AnimationClip>()
                    .Where(clip => !clip.name.StartsWith(
                        "__preview__",
                        StringComparison.Ordinal)),
                Is.Empty,
                "The pedestrian FBX must not duplicate Player animations.");

            GameObject pedestrianPrefab =
                CityPedestrianResources.LoadPrefab();
            GameObject playerPrefab = Player3DResources.LoadPrefab();
            Material sharedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);
            Assert.That(pedestrianPrefab, Is.Not.Null);
            Assert.That(playerPrefab, Is.Not.Null);
            Assert.That(sharedMaterial, Is.Not.Null);

            Player3DAssetRegistry playerRegistry =
                playerPrefab.GetComponent<Player3DAssetRegistry>();
            CityPedestrianAssetRegistry registry =
                pedestrianPrefab.GetComponent<
                    CityPedestrianAssetRegistry>();
            Assert.That(playerRegistry, Is.Not.Null);
            Assert.That(registry, Is.Not.Null);
            Assert.That(registry.Animator, Is.Not.Null);
            Assert.That(
                registry.Animator.avatar,
                Is.SameAs(playerRegistry.Animator.avatar));
            Assert.That(registry.Animator.applyRootMotion, Is.False);
            Assert.That(registry.Animator.runtimeAnimatorController, Is.Null);
            Assert.That(
                registry.Animator.cullingMode,
                Is.EqualTo(AnimatorCullingMode.CullUpdateTransforms));
            Assert.That(registry.DesignId, Is.EqualTo("lampshade_walker_v1"));
            Assert.That(registry.SourceTriangleCount, Is.EqualTo(1160));
            Assert.That(registry.Renderers.Count, Is.EqualTo(38));
            Assert.That(
                registry.LocalBounds.min.y,
                Is.EqualTo(0f).Within(0.025f));
            Assert.That(
                registry.LocalBounds.size.y,
                Is.EqualTo(1.75f).Within(0.035f));
            Assert.That(
                AssetDatabase.GetAssetPath(registry.IdleClip),
                Is.EqualTo(PlayerAnimationPath));
            Assert.That(
                AssetDatabase.GetAssetPath(registry.WalkClip),
                Is.EqualTo(PlayerAnimationPath));
            Assert.That(
                playerRegistry.TryGetAnimation(
                    "Idle",
                    out Player3DAnimationBinding idle),
                Is.True);
            Assert.That(
                playerRegistry.TryGetAnimation(
                    "Walk",
                    out Player3DAnimationBinding walk),
                Is.True);
            Assert.That(registry.IdleClip, Is.SameAs(idle.Clip));
            Assert.That(registry.WalkClip, Is.SameAs(walk.Clip));
            Assert.That(registry.IdleClip.isLooping, Is.True);
            Assert.That(registry.WalkClip.isLooping, Is.True);

            AssertPassiveSharedPresentation(
                pedestrianPrefab,
                registry,
                sharedMaterial);
            AssertWalkSolesStayGrounded(pedestrianPrefab);
        }

        [Test]
        public void Factory_SimulatesEveryRouteAndCapsTheVisualPool()
        {
            GameObject root = new GameObject("Pedestrian Test Root");
            CityPedestrianDirector director = null;
            try
            {
                GameObject player = new GameObject("Player");
                player.transform.SetParent(root.transform, false);
                CityPedestrianPlan plan = CreateLinearPlan(8, 35f);
                var walkableArea = new RoadWalkableArea(
                    new[] { Rect.MinMaxRect(30f, -20f, 45f, 20f) });
                director = CityPedestrianFactory.Create(
                        root.transform,
                        plan,
                        player.transform,
                        walkableArea,
                        null,
                        CityPedestrianResources.LoadPrefab());

                Assert.That(director.Count, Is.EqualTo(8));
                Assert.That(
                    director.PoolCapacity,
                    Is.EqualTo(
                        CityPedestrianDirector.MaximumActiveModels));
                Assert.That(
                    director.ActiveCount,
                    Is.EqualTo(
                        CityPedestrianDirector.MaximumActiveModels));

                Vector3[] before = director.Actors
                    .Select(actor => actor.Position)
                    .ToArray();
                director.Advance(0.5f);
                for (int index = 0; index < director.Actors.Count; index++)
                {
                    Assert.That(
                        director.Actors[index].Position.x,
                        Is.GreaterThan(before[index].x),
                        "Virtual routes must advance even without a model.");
                }

                Assert.That(
                    director.Actors
                        .Where(actor => actor.HasPresentation)
                        .All(actor =>
                            actor.Presentation.WalkWeight == 1f),
                    Is.True);
                Vector3[] frozen = director.Actors
                    .Select(actor => actor.Position)
                    .ToArray();
                director.Advance(0f);
                CollectionAssert.AreEqual(
                    frozen,
                    director.Actors
                        .Select(actor => actor.Position)
                        .ToArray());

                director.Advance(4f);
                Assert.That(
                    director.Actors.All(actor =>
                        actor.MotionState ==
                        CityPedestrianMotionState.EndpointPause),
                    Is.True);
                director.Advance(
                    CityPedestrianActor.MaximumEndpointPause + 0.01f);
                Assert.That(
                    director.Actors.All(actor =>
                        actor.MotionState ==
                        CityPedestrianMotionState.Turning),
                    Is.True);
                director.Advance(CityPedestrianActor.TurnDuration);
                Assert.That(
                    director.Actors.All(actor =>
                        actor.MotionState ==
                        CityPedestrianMotionState.Walking &&
                        actor.RouteDirection == -1),
                    Is.True);

                player.transform.position = Vector3.right * 1000f;
                director.Advance(0f);
                Assert.That(director.ActiveCount, Is.Zero);
                player.transform.position = Vector3.zero;
                director.RefreshPresentationPool();
                Assert.That(
                    director.ActiveCount,
                    Is.EqualTo(
                        CityPedestrianDirector.MaximumActiveModels));
            }
            finally
            {
                director?.Shutdown();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Director_WithShortFarClip_DoesNotReleaseAndRebind()
        {
            GameObject root = new GameObject("Short Fog Test Root");
            CityPedestrianDirector director = null;
            try
            {
                GameObject player = new GameObject("Player");
                player.transform.SetParent(root.transform, false);
                GameObject cameraObject = new GameObject("Camera");
                cameraObject.transform.SetParent(root.transform, false);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.farClipPlane = 30f;
                CityPedestrianPlan plan = CreateLinearPlan(1, 25f);
                var walkableArea = new RoadWalkableArea(
                    new[] { Rect.MinMaxRect(20f, -2f, 32f, 2f) });
                director = CityPedestrianFactory.Create(
                        root.transform,
                        plan,
                        player.transform,
                        walkableArea,
                        camera,
                        CityPedestrianResources.LoadPrefab());

                Assert.That(director.ActiveCount, Is.EqualTo(1));
                player.transform.position = Vector3.left * 10f;
                director.Advance(0f);
                Assert.That(director.ActiveCount, Is.Zero);
                director.Advance(0f);
                Assert.That(
                    director.ActiveCount,
                    Is.Zero,
                    "A model beyond the short fog cap must not thrash " +
                    "between release and activation.");
            }
            finally
            {
                director?.Shutdown();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void AssertPassiveSharedPresentation(
            GameObject prefab,
            CityPedestrianAssetRegistry registry,
            Material sharedMaterial)
        {
            Assert.That(
                prefab.GetComponentsInChildren<Collider>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<Rigidbody>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<Light>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<MonoBehaviour>(true)
                    .Any(behaviour => behaviour is IInteractable),
                Is.False);
            for (int index = 0; index < registry.Renderers.Count; index++)
            {
                Material[] materials =
                    registry.Renderers[index].sharedMaterials;
                Assert.That(materials, Is.Not.Empty);
                Assert.That(
                    materials.All(material => material == sharedMaterial),
                    Is.True);
            }
        }

        private static void AssertWalkSolesStayGrounded(GameObject prefab)
        {
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            CityPedestrianPresentation presentation = null;
            try
            {
                CityPedestrianAssetRegistry registry =
                    instance.GetComponent<CityPedestrianAssetRegistry>();
                presentation =
                    instance.AddComponent<CityPedestrianPresentation>();
                presentation.Initialize(registry);
                float neutralHeight = GetLowestBootSoleHeight(registry);
                presentation.SetMoving(true);
                for (int phase = 0; phase < 12; phase++)
                {
                    presentation.ConfigureCycle(
                        0.91f,
                        phase / 12f);
                    Assert.That(
                        GetLowestBootSoleHeight(registry),
                        Is.EqualTo(neutralHeight).Within(0.025f),
                        $"Walk phase {phase}/12 lost the grounded sole.");
                }
            }
            finally
            {
                presentation?.Shutdown();
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static float GetLowestBootSoleHeight(
            CityPedestrianAssetRegistry registry)
        {
            float lowest = float.PositiveInfinity;
            for (int index = 0;
                 index < registry.RendererBindings.Count;
                 index++)
            {
                CityPedestrianRendererBinding binding =
                    registry.RendererBindings[index];
                if (binding == null ||
                    binding.RendererName.IndexOf(
                        "BootSole",
                        StringComparison.Ordinal) < 0 ||
                    !(binding.Renderer is SkinnedMeshRenderer renderer))
                {
                    continue;
                }

                Mesh baked = new Mesh();
                try
                {
                    renderer.BakeMesh(baked);
                    Vector3[] vertices = baked.vertices;
                    for (int vertex = 0;
                         vertex < vertices.Length;
                         vertex++)
                    {
                        lowest = Mathf.Min(
                            lowest,
                            renderer.transform
                                .TransformPoint(vertices[vertex]).y);
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(baked);
                }
            }

            Assert.That(
                float.IsPositiveInfinity(lowest),
                Is.False,
                "The production pedestrian must expose both boot soles.");
            return lowest;
        }

        private static CityPedestrianPlan CreateLinearPlan(
            int count,
            float startX)
        {
            var definitions = new List<CityPedestrianDefinition>(count);
            float center = (count - 1) * 0.5f;
            for (int index = 0; index < count; index++)
            {
                float z = (index - center) * 4f;
                definitions.Add(new CityPedestrianDefinition(
                    $"test-pedestrian:{index}",
                    new[]
                    {
                        new RoadEdge(
                            Vector2Int.zero,
                            Vector2Int.right)
                    },
                    new[]
                    {
                        new Vector3(startX, 0f, z),
                        new Vector3(startX + 4f, 0f, z)
                    },
                    1f,
                    0.91f,
                    index / (float)Math.Max(1, count),
                    index % CityPedestrianPlanner.PaletteVariantCount,
                    unchecked((uint)(100 + index)),
                    false));
            }

            return new CityPedestrianPlan(
                1,
                2,
                3u,
                count,
                CityPedestrianPlanner.AgentRadius,
                definitions);
        }
    }
}
