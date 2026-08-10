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
                AssertRuntimeCollisionContract(director);

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
                    if (director.Actors[index].HasPresentation)
                    {
                        Assert.That(
                            director.Actors[index].LastDisplacement.x,
                            Is.GreaterThan(0f),
                            "Presented actors must report their actual move.");
                    }
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
                Assert.That(
                    director.Actors.All(actor => !actor.CollisionEnabled),
                    Is.True);
                player.transform.position = Vector3.zero;
                director.RefreshPresentationPool();
                Assert.That(
                    director.ActiveCount,
                    Is.EqualTo(
                        CityPedestrianDirector.MaximumActiveModels));
                Assert.That(
                    director.Actors.All(actor =>
                        actor.CollisionEnabled == actor.HasPresentation),
                    Is.True);

                director.Shutdown();
                Assert.That(
                    director.Actors.All(actor =>
                        !actor.HasPresentation &&
                        !actor.CollisionEnabled),
                    Is.True);
            }
            finally
            {
                director?.Shutdown();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Factory_DelaysCollisionWhenPlayerOverlapsSpawn()
        {
            GameObject root = new GameObject("Overlap Test Root");
            CityPedestrianDirector director = null;
            try
            {
                GameObject player = new GameObject("Player");
                player.transform.SetParent(root.transform, false);
                player.transform.position = new Vector3(35f, 0.08f, 0f);
                CharacterController playerController =
                    player.AddComponent<CharacterController>();
                playerController.height = 1.75f;
                playerController.radius = 0.32f;
                playerController.center = new Vector3(0f, 0.875f, 0f);

                CityPedestrianPlan plan = CreateLinearPlan(1, 35f);
                director = CityPedestrianFactory.Create(
                    root.transform,
                    plan,
                    player.transform,
                    new RoadWalkableArea(
                        new[] { Rect.MinMaxRect(30f, -2f, 45f, 2f) }),
                    null,
                    CityPedestrianResources.LoadPrefab());

                CityPedestrianActor actor = director.Actors[0];
                Assert.That(actor.HasPresentation, Is.False);
                Assert.That(actor.CollisionEnabled, Is.False);

                player.transform.position = Vector3.zero;
                Physics.SyncTransforms();
                director.RefreshPresentationPool();

                Assert.That(actor.HasPresentation, Is.True);
                Assert.That(actor.CollisionEnabled, Is.True);
            }
            finally
            {
                director?.Shutdown();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void HeadOnActors_UseStablePlanOrderToAvoidMutualYield()
        {
            GameObject root = new GameObject("Yield Priority Test Root");
            CityPedestrianDirector director = null;
            try
            {
                GameObject player = new GameObject("Player");
                player.transform.SetParent(root.transform, false);
                CityPedestrianPlan plan = CreateHeadOnPlan();
                director = CityPedestrianFactory.Create(
                    root.transform,
                    plan,
                    player.transform,
                    new RoadWalkableArea(
                        new[] { Rect.MinMaxRect(30f, -2f, 44f, 2f) }),
                    null,
                    CityPedestrianResources.LoadPrefab());

                director.Advance(1.7f);
                director.Advance(0.1f);

                Assert.That(director.Actors[0].IsYielding, Is.False);
                Assert.That(
                    director.Actors[0].LastDisplacement.x,
                    Is.GreaterThan(0f));
                Assert.That(director.Actors[1].IsYielding, Is.True);
                Assert.That(
                    director.Actors[1].LastDisplacement,
                    Is.EqualTo(Vector3.zero));
            }
            finally
            {
                director?.Shutdown();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CollisionLayer_IsPhysicalButExcludedFromQueries()
        {
            Assert.That(
                LayerMask.NameToLayer(CityPedestrianCollision.LayerName),
                Is.EqualTo(CityPedestrianCollision.LayerIndex));
            CityPedestrianCollision.EnsureRuntimePolicy();
            Assert.That(
                Physics.GetIgnoreLayerCollision(
                    CityPedestrianCollision.DefaultLayerIndex,
                    CityPedestrianCollision.LayerIndex),
                Is.False);
            Assert.That(
                Physics.GetIgnoreLayerCollision(
                    CityPedestrianCollision.LayerIndex,
                    CityPedestrianCollision.LayerIndex),
                Is.True);

            int pedestrianBit =
                1 << CityPedestrianCollision.LayerIndex;
            Assert.That(
                PlayerInteractor.InteractionLayerMask & pedestrianBit,
                Is.Zero);

            GameObject root = new GameObject("Camera Mask Test Root");
            try
            {
                Camera camera = root.AddComponent<Camera>();
                GameObject target = new GameObject("Target");
                target.transform.SetParent(root.transform, false);
                PlayerCameraFollow follow =
                    root.AddComponent<PlayerCameraFollow>();
                follow.Initialize(camera, target.transform, false);
                Assert.That(
                    follow.CollisionLayerMask & pedestrianBit,
                    Is.Zero);
            }
            finally
            {
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

        private static void AssertRuntimeCollisionContract(
            CityPedestrianDirector director)
        {
            Assert.That(
                director.Actors.Count(actor => actor.CollisionEnabled),
                Is.EqualTo(director.ActiveCount));
            for (int index = 0; index < director.Actors.Count; index++)
            {
                CityPedestrianActor actor = director.Actors[index];
                CharacterController controller = actor.CharacterController;
                Assert.That(controller, Is.Not.Null);
                Assert.That(
                    actor.gameObject.layer,
                    Is.EqualTo(CityPedestrianCollision.LayerIndex));
                Assert.That(
                    controller.height,
                    Is.EqualTo(
                        CityPedestrianActor.CollisionHeight).Within(0.0001f));
                Assert.That(
                    controller.radius,
                    Is.EqualTo(actor.AgentRadius).Within(0.0001f));
                Assert.That(
                    controller.center,
                    Is.EqualTo(new Vector3(
                        0f,
                        CityPedestrianActor.CollisionCenterHeight,
                        0f)));
                Assert.That(
                    actor.CollisionEnabled,
                    Is.EqualTo(actor.HasPresentation));
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

        private static CityPedestrianPlan CreateHeadOnPlan()
        {
            var waypoints = new[]
            {
                new Vector3(35f, 0f, 0f),
                new Vector3(39f, 0f, 0f)
            };
            var edge = new[]
            {
                new RoadEdge(Vector2Int.zero, Vector2Int.right)
            };
            var definitions = new List<CityPedestrianDefinition>
            {
                new CityPedestrianDefinition(
                    "head-on:0",
                    edge,
                    waypoints,
                    1f,
                    1f,
                    0f,
                    0,
                    10u,
                    false),
                new CityPedestrianDefinition(
                    "head-on:1",
                    edge,
                    waypoints,
                    1f,
                    1f,
                    0.5f,
                    1,
                    11u,
                    true)
            };
            return new CityPedestrianPlan(
                1,
                2,
                3u,
                definitions.Count,
                CityPedestrianPlanner.AgentRadius,
                definitions);
        }
    }
}
