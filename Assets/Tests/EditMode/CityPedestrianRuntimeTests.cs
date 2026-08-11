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
        public void Actor_TurnsAtGraphCornerAndKeepsRootUpright()
        {
            GameObject root = new GameObject("Corner Test Root");
            CityPedestrianActor actor = null;
            CityPedestrianPresentation presentation = null;
            try
            {
                CityPedestrianPlan plan = CreateCornerPlan();
                actor = CreateBoundActor(
                    root.transform,
                    plan,
                    plan.SpawnAnchors[0],
                    1,
                    out presentation);

                // Simulate a small vertical correction from curb contact.
                // Graph steering must remain planar as the actor turns.
                actor.CharacterController.enabled = false;
                actor.transform.position = new Vector3(-0.001f, 0.24f, 0f);
                actor.CharacterController.enabled = true;
                Physics.SyncTransforms();
                actor.Advance(0.02f);

                Assert.That(
                    actor.MotionState,
                    Is.EqualTo(CityPedestrianMotionState.Walking));
                Assert.That(actor.PreviousNodeIndex, Is.EqualTo(1));
                Assert.That(actor.TargetNodeIndex, Is.EqualTo(2));
                AssertActorRootIsUpright(actor);

                actor.Advance(0.02f);
                Assert.That(actor.LastDisplacement.z, Is.GreaterThan(0f));
                AssertActorRootIsUpright(actor);
            }
            finally
            {
                ReleaseBoundActor(actor, presentation, root.transform);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Actor_InitialApproachChoosesClosestBranch()
        {
            GameObject root = new GameObject("Approach Branch Test Root");
            CityPedestrianActor actor = null;
            CityPedestrianPresentation presentation = null;
            try
            {
                CityPedestrianPlan plan = CreateApproachBranchPlan();
                actor = CreateBoundActor(
                    root.transform,
                    plan,
                    plan.SpawnAnchors[0],
                    1,
                    out presentation,
                    0xA341316Cu);

                actor.Advance(
                    2.1f,
                    false,
                    Vector3.zero,
                    new[] { 2f, 1f, 0f, 2f });

                Assert.That(
                    actor.PreviousNodeIndex,
                    Is.EqualTo(1));
                Assert.That(
                    actor.TargetNodeIndex,
                    Is.EqualTo(2),
                    "A hidden walker must take the branch that continues " +
                    "toward the stationary player.");
            }
            finally
            {
                ReleaseBoundActor(actor, presentation, root.transform);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [TestCase(0f, true)]
        [TestCase(1f, false)]
        public void Actor_AtCrosswalkUsesForcedChoice(
            float crosswalkRoll,
            bool shouldCross)
        {
            GameObject root = new GameObject("Crosswalk Choice Test Root");
            CityPedestrianActor actor = null;
            CityPedestrianPresentation presentation = null;
            try
            {
                CityPedestrianPlan plan = CreateCrosswalkChoicePlan();
                actor = CreateBoundActor(
                    root.transform,
                    plan,
                    plan.SpawnAnchors[0],
                    1,
                    out presentation);
                actor.ForceNextCrosswalkRoll(crosswalkRoll);

                actor.Advance(
                    1.1f,
                    false,
                    Vector3.zero);

                Assert.That(actor.CrosswalkDecisionCount, Is.EqualTo(1));
                Assert.That(
                    actor.CrosswalksTaken,
                    Is.EqualTo(shouldCross ? 1 : 0));
                Assert.That(
                    actor.TargetNodeIndex,
                    Is.EqualTo(shouldCross ? 3 : 2));
                Assert.That(
                    actor.MotionState,
                    Is.EqualTo(CityPedestrianMotionState.Walking));
            }
            finally
            {
                ReleaseBoundActor(actor, presentation, root.transform);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Factory_StaggersRandomizedFogBandSpawns()
        {
            GameObject root = new GameObject("Spawn Cap Test Root");
            CityPedestrianDirector director = null;
            try
            {
                Transform player = CreatePlayer(root.transform);
                CityPedestrianPlan plan = CreateDistanceSpawnPlan(
                    new[]
                    {
                        new Vector3(0f, 0f, -76f),
                        new Vector3(0f, 0f, -76f),
                        new Vector3(4f, 0f, -76f)
                    });
                director = CityPedestrianFactory.Create(
                    root.transform,
                    plan,
                    player,
                    CityPedestrianPlanner.CreateWalkableArea(plan),
                    CityPedestrianResources.LoadPrefab(),
                    () => false);

                Assert.That(
                    director.Count,
                    Is.EqualTo(CityPedestrianDirector.MaximumActiveModels));
                Assert.That(
                    director.PoolCapacity,
                    Is.EqualTo(CityPedestrianDirector.MaximumActiveModels));
                Assert.That(director.ActiveCount, Is.Zero);

                float initialDelay = director.TimeUntilNextSpawn;
                Assert.That(
                    initialDelay,
                    Is.InRange(
                        CityPedestrianDirector.MinimumInitialSpawnDelay,
                        CityPedestrianDirector.MaximumInitialSpawnDelay));
                director.Advance(initialDelay * 0.5f);
                Assert.That(
                    director.ActiveCount,
                    Is.Zero,
                    "The first pedestrian must honor its random delay.");
                director.Advance((initialDelay * 0.5f) + 0.01f);
                Assert.That(director.ActiveCount, Is.EqualTo(1));

                float secondDelay = director.TimeUntilNextSpawn;
                Assert.That(
                    secondDelay,
                    Is.InRange(
                        CityPedestrianDirector.MinimumSpawnCooldown,
                        CityPedestrianDirector.MaximumSpawnCooldown));
                Assert.That(
                    CityPedestrianDirector.MaximumSpawnCooldown -
                    CityPedestrianDirector.MinimumSpawnCooldown,
                    Is.GreaterThanOrEqualTo(9f));
                director.Advance(secondDelay * 0.5f);
                Assert.That(
                    director.ActiveCount,
                    Is.EqualTo(1),
                    "The second slot must use an independent random delay.");
                director.Advance((secondDelay * 0.5f) + 0.01f);

                Assert.That(
                    director.ActiveCount,
                    Is.EqualTo(CityPedestrianDirector.MaximumActiveModels));
                CityPedestrianActor[] active = director.Actors
                    .Where(candidate => candidate.IsSpawned)
                    .ToArray();
                Assert.That(
                    active.Select(candidate => candidate.SpawnAnchorId)
                        .Distinct(StringComparer.Ordinal).Count(),
                    Is.EqualTo(active.Length));
                Assert.That(
                    PlanarDistance(active[0].Position, active[1].Position),
                    Is.GreaterThan(
                        (CityPedestrianPlanner.AgentRadius * 2f) +
                        CityPedestrianDirector.CollisionActivationPadding));

                const float productionAspect = 16f / 9f;
                const float widestProductionFieldOfView = 70f;
                const float conservativeCameraAndVisualDepth = 6f;
                float verticalTangent = Mathf.Tan(
                    widestProductionFieldOfView * 0.5f * Mathf.Deg2Rad);
                float horizontalTangent =
                    verticalTangent * productionAspect;
                float cornerDepthRatio = 1f / Mathf.Sqrt(
                    1f +
                    (verticalTangent * verticalTangent) +
                    (horizontalTangent * horizontalTangent));
                float conservativeFogDepth =
                    (CityPedestrianDirector.MinimumSpawnDistance *
                     cornerDepthRatio) -
                    conservativeCameraAndVisualDepth;
                float fogTransmittanceAtSpawnEdge = Mathf.Exp(
                    -Mathf.Pow(
                        RuntimeSceneSetup.CityFogDensity *
                        conservativeFogDepth,
                        2f));
                Assert.That(
                    fogTransmittanceAtSpawnEdge,
                    Is.LessThan(0.002f),
                    "The spawn band must stay hidden even at the widest " +
                    "production 16:9 frustum corner after camera and full " +
                    "visual-envelope depth offsets.");
                for (int index = 0; index < active.Length; index++)
                {
                    CityPedestrianSpawnAnchor spawnAnchor =
                        plan.SpawnAnchors.Single(
                            candidate => string.Equals(
                                candidate.Id,
                                active[index].SpawnAnchorId,
                                StringComparison.Ordinal));
                    float distance = PlanarDistance(
                        player.position,
                        spawnAnchor.Position);
                    Assert.That(
                        distance,
                        Is.InRange(
                            CityPedestrianDirector.MinimumSpawnDistance,
                            CityPedestrianDirector.MaximumSpawnDistance));
                }

                AssertRuntimeCollisionContract(director);
                Vector3[] frozen = active
                    .Select(candidate => candidate.Position)
                    .ToArray();
                director.Advance(0f);
                CollectionAssert.AreEqual(
                    frozen,
                    active.Select(candidate => candidate.Position).ToArray());
            }
            finally
            {
                director?.Shutdown();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Factory_NightSpawnsMuchLessOftenAndOnlyOneActor()
        {
            GameObject root = new GameObject("Night Spawn Test Root");
            CityPedestrianDirector director = null;
            bool isNight = true;
            try
            {
                Transform player = CreatePlayer(root.transform);
                CityPedestrianPlan plan = CreateDistanceSpawnPlan(
                    new[]
                    {
                        new Vector3(0f, 0f, -76f),
                        new Vector3(4f, 0f, -76f)
                    });
                director = CityPedestrianFactory.Create(
                    root.transform,
                    plan,
                    player,
                    CityPedestrianPlanner.CreateWalkableArea(plan),
                    CityPedestrianResources.LoadPrefab(),
                    () => isNight);

                Assert.That(director.IsNightSpawnMode, Is.True);
                Assert.That(
                    director.CurrentActiveLimit,
                    Is.EqualTo(
                        CityPedestrianDirector.NightMaximumActiveModels));
                Assert.That(
                    director.TimeUntilNextSpawn,
                    Is.InRange(
                        CityPedestrianDirector.MinimumNightInitialSpawnDelay,
                        CityPedestrianDirector.MaximumNightInitialSpawnDelay));
                Assert.That(
                    CityPedestrianDirector.MinimumNightInitialSpawnDelay,
                    Is.GreaterThanOrEqualTo(
                        CityPedestrianDirector.MaximumInitialSpawnDelay *
                        2f));

                AdvanceToNextSpawn(director);
                Assert.That(director.ActiveCount, Is.EqualTo(1));
                Assert.That(
                    director.TimeUntilNextSpawn,
                    Is.InRange(
                        CityPedestrianDirector.MinimumNightSpawnCooldown,
                        CityPedestrianDirector.MaximumNightSpawnCooldown));
                Assert.That(
                    CityPedestrianDirector.MinimumNightSpawnCooldown,
                    Is.GreaterThanOrEqualTo(
                        CityPedestrianDirector.MaximumSpawnCooldown * 2f));

                director.Advance(
                    CityPedestrianDirector.MaximumNightSpawnCooldown + 1f);
                Assert.That(
                    director.ActiveCount,
                    Is.EqualTo(1),
                    "Night must never fill the second pedestrian slot.");

                isNight = false;
                director.Advance(0f);
                Assert.That(director.IsNightSpawnMode, Is.False);
                Assert.That(
                    director.TimeUntilNextSpawn,
                    Is.InRange(
                        CityPedestrianDirector.MinimumInitialSpawnDelay,
                        CityPedestrianDirector.MaximumInitialSpawnDelay));
                AdvanceToNextSpawn(director);
                Assert.That(
                    director.ActiveCount,
                    Is.EqualTo(2),
                    "Day mode may fill the independently delayed second slot.");
            }
            finally
            {
                director?.Shutdown();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Factory_DaytimeFastForwardsAndCompletesInitialApproach()
        {
            GameObject root = new GameObject(
                "Distant Pedestrian Simulation Test Root");
            CityPedestrianDirector director = null;
            bool isNight = false;
            try
            {
                Transform player = CreatePlayer(root.transform);
                CityPedestrianPlan plan = CreateLongApproachPlan();
                director = CityPedestrianFactory.Create(
                    root.transform,
                    plan,
                    player,
                    CityPedestrianPlanner.CreateWalkableArea(plan),
                    CityPedestrianResources.LoadPrefab(),
                    () => isNight);
                AdvanceToNextSpawn(director);

                CityPedestrianActor actor = director.Actors.Single(
                    candidate => candidate.IsSpawned);
                const float approachBudget = 50f;
                const float simulationStep = 0.1f;
                float elapsed = 0f;
                while (PlanarDistance(actor.Position, player.position) >
                           CityPedestrianDirector
                               .InitialApproachCompletionDistance &&
                       elapsed < approachBudget)
                {
                    director.Advance(simulationStep);
                    elapsed += simulationStep;
                }

                Assert.That(
                    PlanarDistance(actor.Position, player.position),
                    Is.LessThanOrEqualTo(
                        CityPedestrianDirector
                            .InitialApproachCompletionDistance + 0.01f),
                    "A daytime walker must enter the encounter radius " +
                    "while the player remains stationary.");
                const int actorIndex = 0;
                Assert.That(
                    director.IsActorInInitialApproach(actorIndex),
                    Is.False,
                    "Approach guidance must end after the first encounter.");

                actor.transform.position = new Vector3(0f, 0f, -70f);
                Physics.SyncTransforms();
                director.Advance(0f);
                Assert.That(
                    director.IsActorInInitialApproach(actorIndex),
                    Is.False,
                    "A walker must not resume pursuing the player after " +
                    "ordinary roaming carries it away.");

                actor.transform.position = new Vector3(
                    0f,
                    0f,
                    -CityPedestrianDirector
                        .DaytimeDistantSimulationInnerDistance + 1f);
                Physics.SyncTransforms();
                director.Advance(1f);
                Assert.That(
                    actor.LastDisplacement.magnitude,
                    Is.InRange(
                        CityPedestrianPlanner.MinimumSpeed - 0.01f,
                        CityPedestrianPlanner.MaximumSpeed + 0.01f),
                    "A walker near the renderable range must use its authored " +
                    "walking speed.");

                isNight = true;
                actor.transform.position = new Vector3(
                    0f,
                    0f,
                    -CityPedestrianDirector.MaximumSpawnDistance);
                Physics.SyncTransforms();
                director.Advance(1f);
                Assert.That(director.IsNightSpawnMode, Is.True);
                Assert.That(
                    actor.LastDisplacement.magnitude,
                    Is.InRange(
                        CityPedestrianPlanner.MinimumSpeed - 0.01f,
                        CityPedestrianPlanner.MaximumSpeed + 0.01f),
                    "Night walkers must retain their sparse authored pace.");
            }
            finally
            {
                director?.Shutdown();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Factory_DefaultHomeReturnReachesStationaryPlayer()
        {
            GameObject root = new GameObject(
                "Production Pedestrian Approach Test Root");
            CityPedestrianDirector director = null;
            try
            {
                const int citySeed = 20260727;
                CityLayout layout = CityLayoutGenerator.Generate(
                    CityBlueprintCatalog.Default,
                    CityGenerationSettings.Default,
                    citySeed);
                CityStreetSurfacePlan surfaces =
                    CityStreetSurfacePlanner.Create(layout);
                CityPedestrianPlan plan = CityPedestrianPlanner.Create(
                    layout,
                    citySeed,
                    surfaces);
                Transform player = CreatePlayer(root.transform);
                player.position =
                    layout.PlayerHome.SidewalkArrivalPosition +
                    Vector3.up *
                    (CityStreetSurfacePlanner.SidewalkTop +
                     PlayerFactory.GroundedRootOffset);
                int closestPlanNodeIndex = Enumerable.Range(
                        0,
                        plan.Nodes.Count)
                    .OrderBy(index => PlanarDistance(
                        plan.Nodes[index].Position,
                        player.position))
                    .First();
                CityPedestrianNode closestPlanNode =
                    plan.Nodes[closestPlanNodeIndex];
                float closestPlanNodeDistance = PlanarDistance(
                    closestPlanNode.Position,
                    player.position);
                HashSet<int> playerComponent = CollectReachableNodes(
                    plan,
                    closestPlanNodeIndex);
                float[] playerComponentAnchorDistances = plan.SpawnAnchors
                    .Where(anchor => playerComponent.Contains(
                        anchor.FirstNodeIndex))
                    .Select(anchor => PlanarDistance(
                        anchor.Position,
                        player.position))
                    .OrderBy(distance => distance)
                    .ToArray();
                int anchorsInSpawnBand = plan.SpawnAnchors.Count(
                    anchor => PlanarDistance(
                        anchor.Position,
                        player.position) >=
                              CityPedestrianDirector.MinimumSpawnDistance &&
                              PlanarDistance(
                                  anchor.Position,
                                  player.position) <=
                              CityPedestrianDirector.MaximumSpawnDistance);
                Physics.SyncTransforms();
                director = CityPedestrianFactory.Create(
                    root.transform,
                    plan,
                    player,
                    CityPedestrianPlanner.CreateWalkableArea(plan),
                    CityPedestrianResources.LoadPrefab(),
                    () => false);

                const float encounterBudget = 60f;
                const float simulationStep = 0.1f;
                bool encountered = false;
                int maximumActiveCount = 0;
                float closestActorDistance = float.PositiveInfinity;
                float elapsed = 0f;
                while (!encountered && elapsed < encounterBudget)
                {
                    director.Advance(simulationStep);
                    elapsed += simulationStep;
                    maximumActiveCount = Mathf.Max(
                        maximumActiveCount,
                        director.ActiveCount);
                    for (int index = 0;
                         index < director.Actors.Count;
                         index++)
                    {
                        CityPedestrianActor actor =
                            director.Actors[index];
                        if (!actor.IsSpawned)
                        {
                            continue;
                        }

                        float actorDistance = PlanarDistance(
                            actor.Position,
                            player.position);
                        closestActorDistance = Mathf.Min(
                            closestActorDistance,
                            actorDistance);
                        if (actorDistance <=
                            CityPedestrianDirector
                                .InitialApproachCompletionDistance + 0.01f)
                        {
                            encountered = true;
                            break;
                        }
                    }
                }

                Assert.That(
                    encountered,
                    Is.True,
                    "The production home-return graph must deliver a " +
                    "walker to a stationary player within the daytime " +
                    $"encounter budget. Anchors in band: " +
                    $"{anchorsInSpawnBand}; maximum active: " +
                    $"{maximumActiveCount}; closest distance: " +
                    $"{closestActorDistance:0.00} m; closest graph node: " +
                    $"{closestPlanNode.Id} at " +
                    $"{closestPlanNodeDistance:0.00} m; player-component " +
                    $"anchor distances: " +
                    $"{string.Join(",", playerComponentAnchorDistances)}; " +
                    "actors: " +
                    string.Join(
                        "; ",
                        director.Actors.Select(actor =>
                            $"{actor.Position} target=" +
                            $"{actor.TargetNodeIndex} previous=" +
                            $"{actor.PreviousNodeIndex} state=" +
                            $"{actor.MotionState}")));
            }
            finally
            {
                director?.Shutdown();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Factory_DelaysSpawnUntilStaticOverlapClears()
        {
            GameObject root = new GameObject("Overlap Test Root");
            GameObject blocker = null;
            CityPedestrianDirector director = null;
            try
            {
                Transform player = CreatePlayer(root.transform);
                CityPedestrianPlan plan = CreateDistanceSpawnPlan(
                    new[] { new Vector3(0f, 0f, -76f) });
                blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                blocker.name = "Spawn Blocker";
                blocker.transform.SetParent(root.transform, false);
                blocker.transform.position = new Vector3(0f, 0.85f, -76f);
                blocker.transform.localScale = new Vector3(2f, 2f, 2f);
                Physics.SyncTransforms();

                director = CityPedestrianFactory.Create(
                    root.transform,
                    plan,
                    player,
                    CityPedestrianPlanner.CreateWalkableArea(plan),
                    CityPedestrianResources.LoadPrefab(),
                    () => false);
                AdvanceToNextSpawn(director);

                Assert.That(director.ActiveCount, Is.Zero);
                Assert.That(director.Actors[0].CollisionEnabled, Is.False);

                UnityEngine.Object.DestroyImmediate(blocker);
                blocker = null;
                Physics.SyncTransforms();
                AdvanceToNextSpawn(director);

                Assert.That(director.ActiveCount, Is.EqualTo(1));
                Assert.That(director.Actors[0].CollisionEnabled, Is.True);
            }
            finally
            {
                director?.Shutdown();
                if (blocker != null)
                {
                    UnityEngine.Object.DestroyImmediate(blocker);
                }

                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void HeadOnActors_UseStableSlotOrderToAvoidMutualYield()
        {
            GameObject root = new GameObject("Yield Priority Test Root");
            CityPedestrianDirector director = null;
            try
            {
                Transform player = CreatePlayer(root.transform);
                CityPedestrianPlan plan = CreateHeadOnSpawnPlan();
                director = CityPedestrianFactory.Create(
                    root.transform,
                    plan,
                    player,
                    CityPedestrianPlanner.CreateWalkableArea(plan),
                    CityPedestrianResources.LoadPrefab(),
                    () => false);
                AdvanceToNextSpawn(director);
                AdvanceToNextSpawn(director);

                Assert.That(director.ActiveCount, Is.EqualTo(2));
                for (int index = 0; index < director.Actors.Count; index++)
                {
                    CityPedestrianActor actor = director.Actors[index];
                    actor.transform.position = new Vector3(
                        actor.TravelDirection.x > 0f ? -0.35f : 0.35f,
                        0f,
                        -72f);
                }

                Physics.SyncTransforms();
                Assert.That(
                    Vector3.Dot(
                        director.Actors[0].TravelDirection,
                        director.Actors[1].TravelDirection),
                    Is.LessThan(-0.9f));
                director.Advance(0.1f);

                Assert.That(director.Actors[0].IsYielding, Is.False);
                Assert.That(
                    director.Actors[0].LastDisplacement.sqrMagnitude,
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
            int busBit = 1 << CityBusCollision.LayerIndex;
            Assert.That(
                PlayerInteractor.InteractionLayerMask & pedestrianBit,
                Is.Zero);
            Assert.That(
                PlayerInteractor.InteractionLayerMask & busBit,
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
                Assert.That(
                    follow.CollisionLayerMask & busBit,
                    Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Director_IgnoresCameraAndReleasesBeyondPlayerRange()
        {
            GameObject root = new GameObject("Distance Lifecycle Test Root");
            CityPedestrianDirector director = null;
            try
            {
                Transform player = CreatePlayer(root.transform);
                Camera camera = CreateTestCamera(root.transform);
                Vector3 spawnPosition = new Vector3(0f, 0f, -76f);
                camera.transform.LookAt(
                    spawnPosition +
                    (Vector3.up *
                     CityPedestrianActor.CollisionCenterHeight));
                CityPedestrianPlan plan = CreateDistanceSpawnPlan(
                    new[] { spawnPosition });
                director = CityPedestrianFactory.Create(
                    root.transform,
                    plan,
                    player,
                    CityPedestrianPlanner.CreateWalkableArea(plan),
                    CityPedestrianResources.LoadPrefab(),
                    () => false);
                AdvanceToNextSpawn(director);

                Assert.That(director.ActiveCount, Is.EqualTo(1));
                CityPedestrianActor actor = director.Actors[0];
                Vector3 viewport = camera.WorldToViewportPoint(
                    actor.Position +
                    (Vector3.up *
                     CityPedestrianActor.CollisionCenterHeight));
                Assert.That(
                    viewport.z,
                    Is.GreaterThan(0f));
                Assert.That(viewport.x, Is.InRange(0f, 1f));
                Assert.That(viewport.y, Is.InRange(0f, 1f));

                camera.transform.rotation = Quaternion.identity;
                director.Advance(0.2f);
                Assert.That(
                    director.ActiveCount,
                    Is.EqualTo(1),
                    "Camera direction must not affect pedestrian lifetime.");

                player.position = actor.Position +
                    (Vector3.forward *
                     (CityPedestrianDirector.DespawnDistance + 0.01f));
                director.Advance(0f);
                Assert.That(
                    director.ActiveCount,
                    Is.Zero,
                    "A pedestrian must despawn beyond the player radius.");
                Assert.That(
                    director.TimeUntilNextSpawn,
                    Is.InRange(
                        CityPedestrianDirector.MinimumSpawnCooldown,
                        CityPedestrianDirector.MaximumSpawnCooldown),
                    "A released slot must receive a fresh randomized delay.");
                Assert.That(
                    actor.MotionState,
                    Is.EqualTo(CityPedestrianMotionState.Dormant));
                Assert.That(actor.SpawnAnchorId, Is.Empty);
                Assert.That(actor.CollisionEnabled, Is.False);
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

        private static void AssertActorRootIsUpright(
            CityPedestrianActor actor)
        {
            Assert.That(
                Vector3.Dot(actor.transform.up, Vector3.up),
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(
                actor.transform.forward.y,
                Is.EqualTo(0f).Within(0.0001f));
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

        private static CityPedestrianActor CreateBoundActor(
            Transform parent,
            CityPedestrianPlan plan,
            CityPedestrianSpawnAnchor anchor,
            int targetNodeIndex,
            out CityPedestrianPresentation presentation,
            uint behaviorSeed = 1u)
        {
            GameObject actorObject = new GameObject("Pedestrian Actor");
            actorObject.layer = CityPedestrianCollision.LayerIndex;
            actorObject.transform.SetParent(parent, false);
            CityPedestrianActor actor =
                actorObject.AddComponent<CityPedestrianActor>();
            actor.Initialize(
                CityPedestrianPlanner.CreateWalkableArea(plan),
                plan.AgentRadius);

            CityPedestrianAssetRegistry registry =
                CityPedestrianResources.Instantiate(parent);
            presentation = registry.GetComponent<
                CityPedestrianPresentation>();
            if (presentation == null)
            {
                presentation = registry.gameObject.AddComponent<
                    CityPedestrianPresentation>();
            }

            presentation.Initialize(registry);
            actor.PrepareSpawn(
                plan,
                anchor,
                targetNodeIndex,
                1f,
                0.91f,
                0f,
                0,
                behaviorSeed);
            actor.BindPresentation(presentation);
            return actor;
        }

        private static void ReleaseBoundActor(
            CityPedestrianActor actor,
            CityPedestrianPresentation presentation,
            Transform poolRoot)
        {
            if (actor != null && actor.IsSpawned)
            {
                actor.ReleasePresentation(poolRoot);
            }

            presentation?.Shutdown();
        }

        private static Transform CreatePlayer(Transform parent)
        {
            GameObject player = new GameObject("Player");
            player.transform.SetParent(parent, false);
            return player.transform;
        }

        private static Camera CreateTestCamera(Transform parent)
        {
            GameObject cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.position = new Vector3(
                0f,
                CityPedestrianActor.CollisionCenterHeight,
                0f);
            cameraObject.transform.rotation = Quaternion.identity;
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            camera.fieldOfView = 60f;
            camera.aspect = 1f;
            return camera;
        }

        private static CityPedestrianPlan CreateCornerPlan()
        {
            return new CityPedestrianPlan(
                1,
                2,
                3u,
                CityPedestrianPlanner.AgentRadius,
                new[]
                {
                    new CityPedestrianNode(
                        "approach",
                        new Vector3(-1f, 0f, 0f),
                        false),
                    new CityPedestrianNode(
                        "corner",
                        Vector3.zero,
                        false),
                    new CityPedestrianNode(
                        "exit",
                        new Vector3(0f, 0f, 1f),
                        false)
                },
                new[]
                {
                    new CityPedestrianLink(
                        "approach",
                        0,
                        1,
                        CityPedestrianLinkKind.Sidewalk),
                    new CityPedestrianLink(
                        "turn",
                        1,
                        2,
                        CityPedestrianLinkKind.Turn)
                },
                new[]
                {
                    new CityPedestrianSpawnAnchor(
                        "spawn:approach",
                        new Vector3(-0.5f, 0f, 0f),
                        0,
                        1)
                },
                new[] { Rect.MinMaxRect(-2f, -1f, 1f, 2f) });
        }

        private static CityPedestrianPlan CreateCrosswalkChoicePlan()
        {
            return new CityPedestrianPlan(
                1,
                2,
                3u,
                CityPedestrianPlanner.AgentRadius,
                new[]
                {
                    new CityPedestrianNode(
                        "approach",
                        new Vector3(-2f, 0f, 0f),
                        false),
                    new CityPedestrianNode(
                        "zebra",
                        Vector3.zero,
                        true),
                    new CityPedestrianNode(
                        "continue",
                        new Vector3(2f, 0f, 0f),
                        false),
                    new CityPedestrianNode(
                        "cross",
                        new Vector3(0f, 0f, 2f),
                        false)
                },
                new[]
                {
                    new CityPedestrianLink(
                        "approach",
                        0,
                        1,
                        CityPedestrianLinkKind.Sidewalk),
                    new CityPedestrianLink(
                        "continue",
                        1,
                        2,
                        CityPedestrianLinkKind.Sidewalk),
                    new CityPedestrianLink(
                        "cross",
                        1,
                        3,
                        CityPedestrianLinkKind.Crosswalk)
                },
                new[]
                {
                    new CityPedestrianSpawnAnchor(
                        "spawn:approach",
                        new Vector3(-1f, 0f, 0f),
                        0,
                        1)
                },
                new[] { Rect.MinMaxRect(-3f, -1f, 3f, 3f) });
        }

        private static CityPedestrianPlan CreateDistanceSpawnPlan(
            IReadOnlyList<Vector3> anchorPositions)
        {
            var nodes = new List<CityPedestrianNode>(
                anchorPositions.Count * 2);
            var links = new List<CityPedestrianLink>(
                anchorPositions.Count);
            var anchors = new List<CityPedestrianSpawnAnchor>(
                anchorPositions.Count);
            var rectangles = new List<Rect>(anchorPositions.Count);
            for (int index = 0; index < anchorPositions.Count; index++)
            {
                Vector3 anchorPosition = anchorPositions[index];
                int firstNode = nodes.Count;
                int secondNode = firstNode + 1;
                nodes.Add(new CityPedestrianNode(
                    $"route:{index}:first",
                    anchorPosition + (Vector3.back * 4f),
                    false));
                nodes.Add(new CityPedestrianNode(
                    $"route:{index}:second",
                    anchorPosition + (Vector3.forward * 4f),
                    false));
                links.Add(new CityPedestrianLink(
                    $"route:{index}",
                    firstNode,
                    secondNode,
                    CityPedestrianLinkKind.Sidewalk));
                anchors.Add(new CityPedestrianSpawnAnchor(
                    $"spawn:{index}",
                    anchorPosition,
                    firstNode,
                    secondNode));
                rectangles.Add(Rect.MinMaxRect(
                    anchorPosition.x - 1f,
                    anchorPosition.z - 5f,
                    anchorPosition.x + 1f,
                    anchorPosition.z + 5f));
            }

            return new CityPedestrianPlan(
                1,
                2,
                3u,
                CityPedestrianPlanner.AgentRadius,
                nodes,
                links,
                anchors,
                rectangles);
        }

        private static CityPedestrianPlan CreateApproachBranchPlan()
        {
            return new CityPedestrianPlan(
                1,
                2,
                3u,
                CityPedestrianPlanner.AgentRadius,
                new[]
                {
                    new CityPedestrianNode(
                        "far",
                        new Vector3(0f, 0f, -80f),
                        false),
                    new CityPedestrianNode(
                        "junction",
                        new Vector3(0f, 0f, -76f),
                        false),
                    new CityPedestrianNode(
                        "near",
                        new Vector3(0f, 0f, -60f),
                        false),
                    new CityPedestrianNode(
                        "side",
                        new Vector3(20f, 0f, -76f),
                        false)
                },
                new[]
                {
                    new CityPedestrianLink(
                        "approach",
                        0,
                        1,
                        CityPedestrianLinkKind.Sidewalk),
                    new CityPedestrianLink(
                        "near",
                        1,
                        2,
                        CityPedestrianLinkKind.Sidewalk),
                    new CityPedestrianLink(
                        "side",
                        1,
                        3,
                        CityPedestrianLinkKind.Sidewalk)
                },
                new[]
                {
                    new CityPedestrianSpawnAnchor(
                        "spawn:approach",
                        new Vector3(0f, 0f, -78f),
                        0,
                        1)
                },
                new[] { Rect.MinMaxRect(-1f, -81f, 21f, -59f) });
        }

        private static CityPedestrianPlan CreateLongApproachPlan()
        {
            return new CityPedestrianPlan(
                1,
                2,
                3u,
                CityPedestrianPlanner.AgentRadius,
                new[]
                {
                    new CityPedestrianNode(
                        "far",
                        new Vector3(0f, 0f, -120f),
                        false),
                    new CityPedestrianNode(
                        "near",
                        Vector3.zero,
                        false)
                },
                new[]
                {
                    new CityPedestrianLink(
                        "approach",
                        0,
                        1,
                        CityPedestrianLinkKind.Sidewalk)
                },
                new[]
                {
                    new CityPedestrianSpawnAnchor(
                        "spawn:approach",
                        new Vector3(
                            0f,
                            0f,
                            -CityPedestrianDirector.MaximumSpawnDistance),
                        0,
                        1)
                },
                new[] { Rect.MinMaxRect(-1f, -121f, 1f, 1f) });
        }

        private static CityPedestrianPlan CreateHeadOnSpawnPlan()
        {
            return new CityPedestrianPlan(
                1,
                2,
                3u,
                CityPedestrianPlanner.AgentRadius,
                new[]
                {
                    new CityPedestrianNode(
                        "left",
                        new Vector3(-120f, 0f, -72f),
                        false),
                    new CityPedestrianNode(
                        "center",
                        new Vector3(0f, 0f, -72f),
                        false),
                    new CityPedestrianNode(
                        "right",
                        new Vector3(120f, 0f, -72f),
                        false)
                },
                new[]
                {
                    new CityPedestrianLink(
                        "left-half",
                        0,
                        1,
                        CityPedestrianLinkKind.Sidewalk),
                    new CityPedestrianLink(
                        "right-half",
                        1,
                        2,
                        CityPedestrianLinkKind.Sidewalk)
                },
                new[]
                {
                    new CityPedestrianSpawnAnchor(
                        "spawn:left",
                        new Vector3(-35f, 0f, -72f),
                        0,
                        1),
                    new CityPedestrianSpawnAnchor(
                        "spawn:right",
                        new Vector3(35f, 0f, -72f),
                        1,
                        2)
                },
                new[] { Rect.MinMaxRect(-121f, -73f, 121f, -71f) });
        }

        private static float PlanarDistance(Vector3 first, Vector3 second)
        {
            float deltaX = first.x - second.x;
            float deltaZ = first.z - second.z;
            return Mathf.Sqrt((deltaX * deltaX) + (deltaZ * deltaZ));
        }

        private static HashSet<int> CollectReachableNodes(
            CityPedestrianPlan plan,
            int startNode)
        {
            var result = new HashSet<int> { startNode };
            var pending = new Queue<int>();
            pending.Enqueue(startNode);
            while (pending.Count > 0)
            {
                int node = pending.Dequeue();
                IReadOnlyList<int> linkIndices = plan.GetLinkIndices(node);
                for (int index = 0; index < linkIndices.Count; index++)
                {
                    int other = plan.Links[linkIndices[index]].Other(node);
                    if (result.Add(other))
                    {
                        pending.Enqueue(other);
                    }
                }
            }

            return result;
        }

        private static void AdvanceToNextSpawn(
            CityPedestrianDirector director)
        {
            director.Advance(director.TimeUntilNextSpawn + 0.01f);
        }
    }
}
