using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class PersonalSpaceAssetPrebuild : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type setup = assembly.GetType(
                    "BarPromenade.Editor.CityPedestrianPersonalSpaceAssetSetup");
                if (setup == null)
                {
                    continue;
                }

                MethodInfo build = setup.GetMethod("BuildOrThrow",
                    BindingFlags.Public | BindingFlags.Static);
                if (build == null)
                {
                    throw new MissingMethodException(setup.FullName, "BuildOrThrow");
                }

                build.Invoke(null, null);
                return;
            }

            throw new InvalidOperationException(
                "The personal-space authoring setup must be loaded before PlayMode tests.");
#endif
        }
    }

    [PrebuildSetup(typeof(PersonalSpaceAssetPrebuild))]
    public sealed class CityPedestrianPersonalSpacePlayModeTests
    {
        private sealed class OpenArea : IWalkableArea
        {
            public bool Contains(Vector3 position, float radius = 0f) => true;

            public Vector3 Constrain(Vector3 currentPosition,
                Vector3 desiredPosition, float radius = 0f) => desiredPosition;
        }

        private const float Step = 1f / 60f;
        private static readonly Vector3 GroundOrigin = new Vector3(1000f, 100f, 1000f);
        private GameObject root;
        private GameObject obstruction;
        private Transform pool;
        private Camera camera;
        private PlayerRuntime player;
        private CityPedestrianActor actor;
        private CityPedestrianPresentation presentation;
        private CityPedestrianArchetype archetype;
        private CityPedestrianPersonalSpaceController personalSpace;
        private InputTestFixture input;
        private float previousTimeScale;
        private float previousCaptureDeltaTime;
        private Vector3 PlayerStart => GroundOrigin + Vector3.up * PlayerFactory.GroundedRootOffset;
        private Vector3 WalkerStart => GroundOrigin + Vector3.forward * 0.7f;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousTimeScale = Time.timeScale;
            previousCaptureDeltaTime = Time.captureDeltaTime;
            GameSessionState.BeginNewGame();
            Time.timeScale = 1f;
            Time.captureDeltaTime = Step;
            input = new InputTestFixture();
            input.Setup();
            InputSystem.AddDevice<Keyboard>();

            root = new GameObject("Pedestrian Personal Space Production Test");
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.SetParent(root.transform, false);
            ground.transform.position = GroundOrigin + Vector3.down * 0.5f;
            ground.transform.localScale = new Vector3(30f, 1f, 30f);
            GameObject cameraObject = new GameObject("Personal Space Camera");
            cameraObject.transform.SetParent(root.transform, false);
            camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = GroundOrigin + new Vector3(3f, 1.5f, -3f);
            camera.transform.LookAt(GroundOrigin + Vector3.up * 1f);
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 30f;
            GameObject promptObject = new GameObject("Personal Space Prompt");
            promptObject.transform.SetParent(root.transform, false);
            var prompt = promptObject.AddComponent<InteractionPromptView>();
            var area = new OpenArea();
            player = PlayerFactory.Create(root.transform, PlayerStart, camera, area, prompt);
            // Keep the already-tested continuous sway inert here. The session
            // percentage still exercises the production stage gate, independent
            // of the hero's smoothed presentation level.
            player.Balance.SetIntoxication(0f);

            pool = new GameObject("Personal Space Presentation Pool").transform;
            pool.SetParent(root.transform, false);
            Assert.That(CityPedestrianResources.TryGetArchetype(
                CityPedestrianResources.WeighAttendantDesignId, out archetype), Is.True);
            Assert.That(CityPedestrianResources.TryInstantiate(
                CityPedestrianResources.LoadPrefab(archetype), pool, out var registry), Is.True);
            Assert.That(registry.PersonalSpaceGuardClip, Is.Not.Null);
            Assert.That(registry.PersonalSpaceShoveClip, Is.Not.Null);
            presentation = registry.gameObject.AddComponent<CityPedestrianPresentation>();
            presentation.Initialize(registry);
            registry.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            GameObject actorObject = new GameObject("Personal Space Roaming Actor");
            actorObject.transform.SetParent(root.transform, false);
            actor = actorObject.AddComponent<CityPedestrianActor>();
            actor.Initialize(area, CityPedestrianPlanner.AgentRadius);
            SpawnWalker();
            personalSpace = new CityPedestrianPersonalSpaceController(
                player.GameObject.transform, new[] { actor });
            Physics.SyncTransforms();
            yield return WaitForPlayerReady();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            personalSpace?.Reset();
            if (actor != null && actor.IsSpawned)
            {
                actor.ReleasePresentation(pool);
            }

            if (presentation != null)
            {
                presentation.Shutdown();
            }

            if (root != null)
            {
                UnityEngine.Object.Destroy(root);
            }

            input?.TearDown();
            GameSessionState.BeginNewGame();
            Time.captureDeltaTime = previousCaptureDeltaTime;
            Time.timeScale = previousTimeScale;
            yield return null;
        }

        [UnityTest]
        public IEnumerator RawStageBoundaries_UseAuthoredGuardOrOnePhysicalContact()
        {
            int[] levels = { 60, 61, 80, 81 };
            foreach (int level in levels)
            {
                yield return ResetEncounter(level);
                int pushesBefore = personalSpace.PushCount;
                Vector3 before = player.GameObject.transform.position;
                Vector3 walkerBefore = actor.Position;
                int previousNode = actor.PreviousNodeIndex;
                int targetNode = actor.TargetNodeIndex;
                yield return TickFor(0.1f);
                if (level == 60)
                {
                    Assert.That(personalSpace.ActiveActor, Is.Null);
                    Assert.That(presentation.AuthoredActionWeight, Is.Zero);
                    Assert.That(personalSpace.PushCount, Is.EqualTo(pushesBefore));
                    continue;
                }

                bool shove = level == 81;
                Assert.That(personalSpace.ActiveActor, Is.SameAs(actor));
                Assert.That(personalSpace.Reaction, Is.EqualTo(shove
                    ? CityPedestrianPersonalSpaceReaction.Shove
                    : CityPedestrianPersonalSpaceReaction.Guard));
                Assert.That(presentation.AuthoredActionClip, Is.SameAs(shove
                    ? presentation.Registry.PersonalSpaceShoveClip
                    : presentation.Registry.PersonalSpaceGuardClip));
                Assert.That(personalSpace.PushCount, Is.EqualTo(pushesBefore),
                    "The hand's windup cannot move the hero.");
                Assert.That(PlanarDistance(before, player.GameObject.transform.position),
                    Is.LessThan(0.001f));

                for (int frame = 0; frame < 90 && personalSpace.Elapsed <
                     CityPedestrianPersonalSpaceRules.ContactTime; frame++)
                {
                    personalSpace.Advance(Step);
                    actor.Advance(Step);
                    if (personalSpace.Elapsed >= CityPedestrianPersonalSpaceRules.ContactTime)
                    {
                        Transform hand = null;
                        foreach (Transform bone in presentation.GetComponentsInChildren<Transform>(true))
                        {
                            if (bone.name == "hand.L")
                            {
                                hand = bone;
                                break;
                            }
                        }

                        Assert.That(hand, Is.Not.Null, "The imported rig must expose its authored palm.");
                        Assert.That(Vector3.Dot(hand.position - actor.Position, actor.transform.forward),
                            Is.GreaterThan(0.25f),
                            "The imported contact arm must extend toward the hero, not behind the walker.");
                        if (shove)
                        {
                            Vector3 toChest = player.GameObject.transform.position - hand.position;
                            Assert.That(Vector3.Dot(toChest, actor.transform.forward),
                                Is.LessThan(0.14f),
                                "The palm must reach the visible chest, not shove across an air gap.");
                        }

                        if (level == 61 || level == 81)
                        {
                            CityPedestrianPersonalSpaceCapture.Write(player, actor,
                                level == 61 ? "guard61-contact" : "shove81-contact");
                        }

                        break;
                    }

                    yield return null;
                }

                Assert.That(personalSpace.Elapsed,
                    Is.EqualTo(CityPedestrianPersonalSpaceRules.ContactTime).Within(0.0001f));
                Assert.That(presentation.AuthoredActionWeight, Is.GreaterThan(0.99f));
                Assert.That(personalSpace.PushCount,
                    Is.EqualTo(pushesBefore + (shove ? 1 : 0)));
                yield return TickFor(1.2f);
                if (shove)
                {
                    CityPedestrianPersonalSpaceCapture.Write(player, actor, "shove81-recovered");
                }
                Assert.That(personalSpace.ActiveActor, Is.Null);
                Assert.That(actor.IsPersonalSpaceReacting, Is.False);
                Assert.That(presentation.AuthoredActionWeight, Is.Zero);
                Assert.That(personalSpace.PushCount,
                    Is.EqualTo(pushesBefore + (shove ? 1 : 0)),
                    "The same extended hand must never apply another impact.");
                Assert.That(before.z - player.GameObject.transform.position.z,
                    Is.EqualTo(shove ? 0.4f : 0f).Within(0.02f));
                Assert.That(PlanarDistance(walkerBefore, actor.Position), Is.LessThan(0.001f));
                Assert.That(actor.PreviousNodeIndex, Is.EqualTo(previousNode));
                Assert.That(actor.TargetNodeIndex, Is.EqualTo(targetNode));
                Assert.That(actor.DetourCount, Is.Zero);
            }
        }

        [UnityTest]
        public IEnumerator ObstructionHeightAndFacing_BlockTheInitialEncounter()
        {
            SetLevel(81);
            CreateObstruction();
            yield return TickFor(0.2f);
            Assert.That(personalSpace.ActiveActor, Is.Null);
            RemoveObstruction();
            yield return null;

            SetWalkerPosition(WalkerStart + Vector3.up * 0.4f);
            personalSpace.Advance(Step);
            Assert.That(personalSpace.ActiveActor, Is.Null,
                "A person on another step cannot reach through the height gap.");
            SetWalkerPosition(WalkerStart);
            actor.transform.rotation = Quaternion.identity;
            personalSpace.Advance(Step);
            Assert.That(personalSpace.ActiveActor, Is.Null,
                "A walker with his back to the hero cannot shove behind himself.");
            Assert.That(personalSpace.PushCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ChangedContactDuringWindup_NeverLeavesADelayedShove()
        {
            for (int interruption = 0; interruption < 5; interruption++)
            {
                yield return ResetEncounter(81);
                int before = personalSpace.PushCount;
                yield return TickFor(0.15f);
                Assert.That(personalSpace.ActiveActor, Is.SameAs(actor));
                Assert.That(personalSpace.Elapsed,
                    Is.LessThan(CityPedestrianPersonalSpaceRules.ContactTime));
                switch (interruption)
                {
                    case 0: CreateObstruction(); break;
                    case 1: player.Motor.Teleport(PlayerStart + Vector3.back * 3f); break;
                    case 2: player.Motor.SetInputEnabled(false); break;
                    case 3: SetLevel(80); break;
                    case 4: SetWalkerPosition(WalkerStart + Vector3.up * 0.4f); break;
                }

                Vector3 afterInterruption = player.GameObject.transform.position;
                yield return TickFor(1.2f);
                Assert.That(personalSpace.PushCount, Is.EqualTo(before),
                    $"Interrupted contact {interruption} still pushed the hero.");
                Assert.That(player.Motor.ExternalPushActive, Is.False);
                Assert.That(PlanarDistance(afterInterruption, player.GameObject.transform.position),
                    Is.LessThan(0.001f));
                RemoveObstruction();
                player.Motor.SetInputEnabled(true);
                yield return TickFor(0.15f);
                Assert.That(personalSpace.PushCount, Is.EqualTo(before),
                    "Unlocking or clearing the path must not release an old impact.");
            }
        }

        [UnityTest]
        public IEnumerator RepeatContact_RequiresSeparationAndCooldownThenResumesTheSameRoute()
        {
            SetLevel(81);
            yield return TickFor(1.3f);
            Assert.That(personalSpace.PushCount, Is.EqualTo(1));
            yield return TickFor(CityPedestrianPersonalSpaceRules.CooldownSeconds + 0.2f);
            Assert.That(personalSpace.ActiveActor, Is.Null);
            Assert.That(personalSpace.PushCount, Is.EqualTo(1),
                "Standing nearby after cooldown cannot restart the same encounter.");

            player.Motor.Teleport(PlayerStart + Vector3.back * 3f);
            yield return TickFor(Step);
            player.Motor.Teleport(PlayerStart);
            yield return WaitForPlayerReady();
            yield return TickFor(1.3f);
            Assert.That(personalSpace.PushCount, Is.EqualTo(2));

            player.Motor.Teleport(PlayerStart + Vector3.back * 3f);
            yield return TickFor(Step);
            player.Motor.Teleport(PlayerStart);
            yield return WaitForPlayerReady();
            yield return TickFor(0.25f);
            Assert.That(personalSpace.ActiveActor, Is.Null,
                "Separation cannot bypass the cooldown after the second encounter.");
            Assert.That(personalSpace.PushCount, Is.EqualTo(2));

            player.Motor.Teleport(PlayerStart + Vector3.left * 3f);
            Vector3 resumePosition = actor.Position;
            int previousNode = actor.PreviousNodeIndex;
            int targetNode = actor.TargetNodeIndex;
            yield return TickFor(0.25f, allowRoaming: true);
            Assert.That(PlanarDistance(resumePosition, actor.Position), Is.GreaterThan(0.1f));
            Assert.That(actor.PreviousNodeIndex, Is.EqualTo(previousNode));
            Assert.That(actor.TargetNodeIndex, Is.EqualTo(targetNode));
            Assert.That(actor.DetourCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator PoolRelease_ClearsThePoseAndReusedActorCanReactAgain()
        {
            SetLevel(61);
            yield return TickFor(0.2f);
            Assert.That(actor.IsPersonalSpaceReacting, Is.True);
            Assert.That(presentation.AuthoredActionWeight, Is.GreaterThan(0f));
            Assert.That(actor.ReleasePresentation(pool), Is.SameAs(presentation));
            personalSpace.Advance(Step);
            Assert.That(personalSpace.ActiveActor, Is.Null);
            Assert.That(actor.MotionState, Is.EqualTo(CityPedestrianMotionState.Dormant));
            Assert.That(actor.IsPersonalSpaceReacting, Is.False);
            Assert.That(presentation.AuthoredActionWeight, Is.Zero);
            Assert.That(presentation.gameObject.activeSelf, Is.False);
            personalSpace.Reset();
            SpawnWalker();
            personalSpace.Advance(Step);
            Assert.That(personalSpace.ActiveActor, Is.SameAs(actor));
            Assert.That(presentation.AuthoredActionWeight, Is.EqualTo(1f),
                "The reused rig starts at the new action's entry pose.");
        }

        [UnityTest]
        public IEnumerator SeatedBenchAndBusRider_DoNotEnterPersonalSpaceReactions()
        {
            SetLevel(81);
            SpawnWalker(atTargetNode: true);
            var seat = new GameObject("Personal Space Excluded Seat").transform;
            seat.SetParent(root.transform, false);
            seat.position = WalkerStart + Vector3.up * 0.6f;
            Assert.That(actor.BeginBenchApproach(1, WalkerStart, Vector3.back,
                new[] { 5f, 0f }), Is.True);
            personalSpace.Advance(Step);
            Assert.That(personalSpace.ActiveActor, Is.Null);
            for (int frame = 0; frame < 8 &&
                 actor.MotionState != CityPedestrianMotionState.WaitingAtBench; frame++)
            {
                actor.Advance(Step);
            }

            Assert.That(actor.MotionState, Is.EqualTo(CityPedestrianMotionState.WaitingAtBench));
            Assert.That(actor.BeginBenchSit(WalkerStart, Quaternion.Euler(0f, 180f, 0f),
                seat, archetype.SeatedRide), Is.True);
            yield return TickFor(0.2f);
            Assert.That(personalSpace.ActiveActor, Is.Null);
            Assert.That(presentation.IsSeated, Is.True);

            SpawnWalker();
            Assert.That(actor.BeginSeatedRide(seat, archetype.SeatedRide), Is.True);
            yield return TickFor(0.2f);
            Assert.That(actor.MotionState, Is.EqualTo(CityPedestrianMotionState.Riding));
            Assert.That(personalSpace.ActiveActor, Is.Null);
            Assert.That(presentation.AuthoredActionWeight, Is.Zero);
            Assert.That(personalSpace.PushCount, Is.Zero);
        }

        private IEnumerator ResetEncounter(int level)
        {
            personalSpace.Reset();
            RemoveObstruction();
            player.Motor.SetInputEnabled(true);
            player.Motor.Teleport(PlayerStart);
            player.Balance.ResetModel();
            SpawnWalker();
            SetLevel(level);
            yield return WaitForPlayerReady();
        }

        private void SpawnWalker(bool atTargetNode = false)
        {
            if (actor.IsSpawned)
            {
                actor.ReleasePresentation(pool);
            }

            Vector3 target = atTargetNode ? WalkerStart : GroundOrigin + Vector3.back * 5f;
            var anchor = new CityPedestrianSpawnAnchor("personal-space-spawn", WalkerStart, 0, 1);
            var plan = new CityPedestrianPlan(7, 11, 17u,
                CityPedestrianPlanner.AgentRadius,
                new[]
                {
                    new CityPedestrianNode("before", GroundOrigin + Vector3.forward * 5f, false),
                    new CityPedestrianNode("after", target, false)
                },
                new[] { new CityPedestrianLink("pavement", 0, 1, CityPedestrianLinkKind.Sidewalk) },
                new[] { anchor },
                new[] { new Rect(GroundOrigin.x - 10f, GroundOrigin.z - 10f, 20f, 20f) });
            actor.PrepareSpawn(plan, anchor, 1, 1f, 1f, 0f, 0, 23u);
            actor.BindPresentation(presentation);
            actor.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            Physics.SyncTransforms();
        }

        private IEnumerator TickFor(float seconds, bool allowRoaming = false)
        {
            int frames = Mathf.CeilToInt(seconds / Step);
            for (int frame = 0; frame < frames; frame++)
            {
                personalSpace.Advance(Step);
                // Keep this fixed contact arrangement outside an encounter.
                // Advancing an artificial yield for seconds would correctly
                // trigger the unrelated pavement jam/detour behaviour.
                actor.Advance(actor.IsPersonalSpaceReacting || allowRoaming ? Step : 0f,
                    shouldYield: !allowRoaming);
                yield return null;
            }
        }

        private IEnumerator WaitForPlayerReady()
        {
            // At least one real motor move is required after a teleport;
            // neither its old grounded flag nor its old balance state counts.
            yield return null;
            for (int frame = 0; frame < 90 &&
                 (!player.Motor.IsGrounded || !player.Balance.IsActive); frame++)
            {
                yield return null;
            }

            Assert.That(player.Motor.IsGrounded, Is.True);
            Assert.That(player.Balance.IsActive, Is.True);
        }

        private static void SetLevel(int level)
        {
            GameSessionState.UpdateDrinkingProgress(level, DrinkId.RedWine, 4);
        }

        private void SetWalkerPosition(Vector3 position)
        {
            actor.CharacterController.enabled = false;
            actor.transform.position = position;
            actor.CharacterController.enabled = true;
            Physics.SyncTransforms();
        }

        private void CreateObstruction()
        {
            obstruction = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstruction.name = "Personal Space Chest Height Obstruction";
            obstruction.transform.SetParent(root.transform, false);
            obstruction.transform.position = GroundOrigin + new Vector3(0f, 1.25f, 0.35f);
            obstruction.transform.localScale = new Vector3(0.8f, 0.5f, 0.03f);
            Physics.SyncTransforms();
        }

        private void RemoveObstruction()
        {
            if (obstruction != null)
            {
                UnityEngine.Object.DestroyImmediate(obstruction);
                obstruction = null;
            }
        }

        private static float PlanarDistance(Vector3 first, Vector3 second)
        {
            first.y = second.y = 0f;
            return Vector3.Distance(first, second);
        }
    }
}
