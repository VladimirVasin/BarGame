using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// The street insult through the production pieces: a real player, a
    /// real roaming body on a synthetic pavement, the shared bubble view
    /// and the personal-space controller's hero gate. What is pinned is
    /// the lifecycle the numbers cannot see - one line, over the right
    /// head, declared for exactly as long as it hangs, gone with the body
    /// when the body goes back to the pool, and never a second one while
    /// the first is up.
    /// </summary>
    public sealed class CityPedestrianInsultPlayModeTests
    {
        private sealed class OpenArea : IWalkableArea
        {
            public bool Contains(Vector3 position, float radius = 0f) => true;

            public Vector3 Constrain(Vector3 currentPosition,
                Vector3 desiredPosition, float radius = 0f) => desiredPosition;
        }

        private const float Step = 1f / 60f;
        private const int Seed = 20260905;

        /// <summary>The bubble lives on unscaled time, which the pinned
        /// clock does not touch; its closing is waited for in real seconds.</summary>
        private const float BubbleRealtimeDeadlineSeconds = 12f;

        private static readonly Vector3 GroundOrigin = new Vector3(1200f, 100f, 1200f);
        private GameObject root;
        private GameObject obstruction;
        private Transform pool;
        private Camera camera;
        private PlayerRuntime player;
        private CityPedestrianActor actor;
        private CityPedestrianPresentation presentation;
        private CityPedestrianActor secondActor;
        private CityPedestrianPresentation secondPresentation;
        private CityPedestrianPersonalSpaceController personalSpace;
        private NpcSpeechBubbleView bubbles;
        private CityPedestrianInsultController insults;
        private InputTestFixture input;
        private float previousTimeScale;
        private float previousCaptureDeltaTime;

        private Vector3 PlayerStart => GroundOrigin + Vector3.up * PlayerFactory.GroundedRootOffset;
        private Vector3 WalkerStart => GroundOrigin + Vector3.forward * 2f;
        private Vector3 SecondWalkerStart => GroundOrigin + Vector3.forward * 2.4f + Vector3.right * 1.2f;
        private Transform PlayerTransform => player.GameObject.transform;

        /// <summary>What the director hands every walker: the hero's head,
        /// or the fallback height over his feet.</summary>
        private Vector3 HeroFocus => PlayerTransform.position + Vector3.up * 1.58f;

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

            root = new GameObject("Pedestrian Insult Production Test");
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.SetParent(root.transform, false);
            ground.transform.position = GroundOrigin + Vector3.down * 0.5f;
            ground.transform.localScale = new Vector3(30f, 1f, 30f);
            GameObject cameraObject = new GameObject("Insult Camera");
            cameraObject.transform.SetParent(root.transform, false);
            camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = GroundOrigin + new Vector3(3f, 1.5f, -3f);
            camera.transform.LookAt(GroundOrigin + Vector3.up * 1f);
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 30f;
            GameObject promptObject = new GameObject("Insult Prompt");
            promptObject.transform.SetParent(root.transform, false);
            var prompt = promptObject.AddComponent<InteractionPromptView>();
            var area = new OpenArea();
            player = PlayerFactory.Create(root.transform, PlayerStart, camera, area, prompt);
            player.Balance.SetIntoxication(0f);

            pool = new GameObject("Insult Presentation Pool").transform;
            pool.SetParent(root.transform, false);
            presentation = CreatePresentation(CityPedestrianResources.WeighAttendantDesignId);
            secondPresentation = CreatePresentation(CityPedestrianResources.BabushkaDesignId);
            actor = CreateActor("Insult Roaming Actor", area);
            secondActor = CreateActor("Insult Second Roaming Actor", area);
            SpawnWalker(actor, presentation, WalkerStart);

            bubbles = root.AddComponent<NpcSpeechBubbleView>();
            bubbles.Initialize(camera, PlayerTransform);
            var actors = new[] { actor, secondActor };
            personalSpace = new CityPedestrianPersonalSpaceController(PlayerTransform, actors);
            insults = CityPedestrianInsultController.Create(
                root.transform, actors, personalSpace, PlayerTransform, bubbles, Seed);
            Assert.That(insults, Is.Not.Null);
            Physics.SyncTransforms();
            yield return WaitForPlayerReady();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (insults != null)
            {
                insults.Shutdown();
            }

            personalSpace?.Reset();
            ReleaseIfSpawned(actor);
            ReleaseIfSpawned(secondActor);
            if (presentation != null)
            {
                presentation.Shutdown();
            }

            if (secondPresentation != null)
            {
                secondPresentation.Shutdown();
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
        public IEnumerator LastStage_OneWalkerCursesOnceOverItsOwnHead()
        {
            SetLevel(81);
            yield return TickUntil(() => insults.SpokenLineCount == 1, 1f);
            Assert.That(insults.SpokenLineCount, Is.EqualTo(1), "Nobody spoke.");
            Assert.That(insults.ActiveActor, Is.SameAs(actor));
            Assert.That(insults.ActivePresentation, Is.SameAs(presentation));
            Assert.That(bubbles.IsDeclared(presentation), Is.True,
                "The line hangs off the walker's own head.");
            Assert.That(bubbles.IsShowing(presentation), Is.True);
            Assert.That(Array.IndexOf(CityPedestrianInsultLines.LineKeys, insults.LastLineKey),
                Is.GreaterThanOrEqualTo(0));
            Assert.That(insults.IsEncounterUsed(actor), Is.True);
            Assert.That(insults.CooldownRemaining, Is.GreaterThan(0f));
            Assert.That(actor.MotionState, Is.EqualTo(CityPedestrianMotionState.Walking),
                "A remark in passing does not stop the man.");

            yield return WaitUntilBubbleCloses(presentation);
            Assert.That(bubbles.IsShowing(presentation), Is.False);
            Assert.That(bubbles.IsDeclared(presentation), Is.False,
                "A closed line gives its speaker slot back to the shared view.");
            Assert.That(insults.ActiveActor, Is.Null);
            Assert.That(insults.SpokenLineCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator BelowTheLastStage_NobodySpeaks()
        {
            int[] levels = { 0, 60, 80 };
            for (int index = 0; index < levels.Length; index++)
            {
                SetLevel(levels[index]);
                yield return TickFor(1f);
                Assert.That(insults.SpokenLineCount, Is.Zero,
                    $"Level {levels[index]} drew a line.");
                Assert.That(bubbles.IsDeclared(presentation), Is.False);
            }
        }

        [UnityTest]
        public IEnumerator SameWalker_RearmsOnlyAfterTheHeroWithdraws()
        {
            SetLevel(81);
            yield return TickUntil(() => insults.SpokenLineCount == 1, 1f);
            Assert.That(insults.SpokenLineCount, Is.EqualTo(1));
            yield return WaitUntilBubbleCloses(presentation);
            yield return TickUntil(() => insults.CooldownRemaining <= 0f,
                CityPedestrianInsultRules.CooldownSeconds + 1f);
            Assert.That(insults.CooldownRemaining, Is.Zero);
            yield return TickFor(1f);
            Assert.That(insults.SpokenLineCount, Is.EqualTo(1),
                "Standing there after the cooldown cannot earn the same walker a second line.");

            player.Motor.Teleport(PlayerStart + Vector3.back * 6f);
            yield return TickFor(3f * Step);
            Assert.That(insults.IsEncounterUsed(actor), Is.False,
                "Six metres away the walker is rearmed.");
            player.Motor.Teleport(PlayerStart);
            yield return WaitForPlayerReady();
            yield return TickUntil(() => insults.SpokenLineCount == 2, 2f);
            Assert.That(insults.SpokenLineCount, Is.EqualTo(2));
            Assert.That(insults.ActiveActor, Is.SameAs(actor));
        }

        [UnityTest]
        public IEnumerator SecondWalker_WaitsForTheOpenLineAndTheCooldown()
        {
            SpawnWalker(secondActor, secondPresentation, SecondWalkerStart);
            SetLevel(81);
            yield return TickUntil(() => insults.SpokenLineCount == 1, 1f);
            Assert.That(insults.SpokenLineCount, Is.EqualTo(1));
            Assert.That(insults.ActiveActor, Is.SameAs(actor), "The nearer walker speaks first.");

            int frames = Mathf.CeilToInt(1f / Step);
            for (int frame = 0; frame < frames; frame++)
            {
                AdvanceWalkers();
                yield return null;
                Assert.That(
                    bubbles.IsShowing(presentation) && bubbles.IsShowing(secondPresentation),
                    Is.False,
                    "Two walkers never talk over each other.");
            }

            Assert.That(insults.SpokenLineCount, Is.EqualTo(1));
            Assert.That(bubbles.IsDeclared(secondPresentation), Is.False);

            yield return WaitUntilBubbleCloses(presentation);
            yield return TickUntil(() => insults.CooldownRemaining <= 0f,
                CityPedestrianInsultRules.CooldownSeconds + 1f);
            yield return TickUntil(() => insults.SpokenLineCount == 2, 2f);
            Assert.That(insults.SpokenLineCount, Is.EqualTo(2));
            Assert.That(insults.ActiveActor, Is.SameAs(secondActor),
                "After the pause the other walker gets his turn.");
            Assert.That(bubbles.IsShowing(secondPresentation), Is.True);
            Assert.That(bubbles.IsShowing(presentation), Is.False);
            Assert.That(insults.IsEncounterUsed(actor), Is.True,
                "The first walker has had his say until the hero leaves.");
        }

        [UnityTest]
        public IEnumerator ReleasedActor_TakesItsLineDownAndWithdrawsTheSpeaker()
        {
            SetLevel(81);
            yield return TickUntil(() => insults.SpokenLineCount == 1, 1f);
            Assert.That(bubbles.IsShowing(presentation), Is.True);

            Assert.That(actor.ReleasePresentation(pool), Is.SameAs(presentation));
            yield return null;
            Assert.That(bubbles.IsShowing(presentation), Is.False,
                "A body back in the pool has no line hanging over the pool root.");
            Assert.That(bubbles.IsDeclared(presentation), Is.False);
            Assert.That(insults.ActiveActor, Is.Null);

            SpawnWalker(actor, presentation, WalkerStart);
            yield return TickUntil(() => insults.CooldownRemaining <= 0f,
                CityPedestrianInsultRules.CooldownSeconds + 1f);
            yield return TickUntil(() => insults.SpokenLineCount == 2, 2f);
            Assert.That(insults.SpokenLineCount, Is.EqualTo(2),
                "The reused rig can be declared and speak again.");
            Assert.That(bubbles.IsShowing(presentation), Is.True);
        }

        [UnityTest]
        public IEnumerator BackTurnedOrObstructed_DoesNotSpeak()
        {
            SetLevel(81);
            actor.transform.rotation = Quaternion.identity;
            Physics.SyncTransforms();
            yield return TickFor(1f);
            Assert.That(insults.SpokenLineCount, Is.Zero,
                "A walker with his back to the hero says nothing.");

            actor.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            CreateObstruction();
            yield return TickFor(1f);
            Assert.That(insults.SpokenLineCount, Is.Zero,
                "A wall between them keeps him quiet.");

            RemoveObstruction();
            yield return TickUntil(() => insults.SpokenLineCount == 1, 1f);
            Assert.That(insults.SpokenLineCount, Is.EqualTo(1),
                "With the wall gone the same walker speaks.");
        }

        [UnityTest]
        public IEnumerator Disengage_LeavesOtherSpeakersAlone()
        {
            SetLevel(81);
            yield return TickUntil(() => insults.SpokenLineCount == 1, 1f);
            Assert.That(bubbles.IsShowing(presentation), Is.True);

            var bystander = new GameObject("Insult Bystander");
            bystander.transform.SetParent(root.transform, false);
            bystander.transform.position = GroundOrigin + new Vector3(-2f, 1.6f, 1f);
            try
            {
                Assert.That(bubbles.DeclareSpeaker(bystander, bystander.transform,
                    "bystander", NpcEarshotProfile.Conversation), Is.True);
                Assert.That(bubbles.Show(bystander, "Somebody else's line."), Is.True);
                insults.Disengage();
                Assert.That(bubbles.IsShowing(presentation), Is.False);
                Assert.That(bubbles.IsDeclared(presentation), Is.False);
                Assert.That(bubbles.IsShowing(bystander), Is.True,
                    "The view is shared; the street closes only its own line.");
                Assert.That(insults.ActiveActor, Is.Null);
            }
            finally
            {
                bubbles.WithdrawSpeaker(bystander);
                UnityEngine.Object.Destroy(bystander);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Create_WithoutABubbleViewOrDirectorPieces_ReturnsNull()
        {
            Assert.That(CityPedestrianInsultController.Create(
                root.transform, new[] { actor }, personalSpace, PlayerTransform, null, Seed),
                Is.Null, "Home has walkers but no bubble view, and gets no insults.");
            Assert.That(CityPedestrianInsultController.Create(
                root.transform, null, personalSpace, PlayerTransform, bubbles, Seed), Is.Null);
            Assert.That(CityPedestrianInsultController.Create(
                root.transform, (CityPedestrianDirector)null, PlayerTransform, bubbles, Seed),
                Is.Null);
            yield return null;
        }

        private CityPedestrianPresentation CreatePresentation(string designId)
        {
            Assert.That(CityPedestrianResources.TryGetArchetype(designId, out var archetype), Is.True);
            Assert.That(CityPedestrianResources.TryInstantiate(
                CityPedestrianResources.LoadPrefab(archetype), pool, out var registry), Is.True);
            Assert.That(registry.HeadAnchor, Is.Not.Null, $"{designId} has no head to speak from.");
            var created = registry.gameObject.AddComponent<CityPedestrianPresentation>();
            created.Initialize(registry, CityPedestrianClipSource.Roaming);
            registry.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return created;
        }

        private CityPedestrianActor CreateActor(string name, IWalkableArea area)
        {
            GameObject actorObject = new GameObject(name);
            actorObject.transform.SetParent(root.transform, false);
            var created = actorObject.AddComponent<CityPedestrianActor>();
            created.Initialize(area, CityPedestrianPlanner.AgentRadius);
            return created;
        }

        private void SpawnWalker(
            CityPedestrianActor walker,
            CityPedestrianPresentation body,
            Vector3 position)
        {
            if (walker.IsSpawned)
            {
                walker.ReleasePresentation(pool);
            }

            var anchor = new CityPedestrianSpawnAnchor("insult-spawn", position, 0, 1);
            var plan = new CityPedestrianPlan(7, 11, 17u,
                CityPedestrianPlanner.AgentRadius,
                new[]
                {
                    new CityPedestrianNode("before", GroundOrigin + Vector3.forward * 5f, false),
                    new CityPedestrianNode("after", GroundOrigin + Vector3.back * 5f, false)
                },
                new[] { new CityPedestrianLink("pavement", 0, 1, CityPedestrianLinkKind.Sidewalk) },
                new[] { anchor },
                new[] { new Rect(GroundOrigin.x - 10f, GroundOrigin.z - 10f, 20f, 20f) });
            walker.PrepareSpawn(plan, anchor, 1, 1f, 1f, 0f, 0, 23u);
            walker.BindPresentation(body);
            Vector3 toHero = PlayerStart - position;
            toHero.y = 0f;
            walker.transform.rotation = Quaternion.LookRotation(toHero.normalized, Vector3.up);
            Physics.SyncTransforms();
        }

        private void ReleaseIfSpawned(CityPedestrianActor walker)
        {
            if (walker != null && walker.IsSpawned)
            {
                walker.ReleasePresentation(pool);
            }
        }

        /// <summary>
        /// One frame of the two walkers standing still on their pavement
        /// with the hero's head offered as the thing to glance at, exactly
        /// as the director offers it. A zero step keeps the fixed
        /// arrangement; the glance is resolved regardless. The insult
        /// controller's own LateUpdate runs after the yield.
        /// </summary>
        private void AdvanceWalkers()
        {
            if (actor.IsSpawned)
            {
                actor.Advance(0f, shouldYield: true, attentionCandidate: HeroFocus);
            }

            if (secondActor.IsSpawned)
            {
                secondActor.Advance(0f, shouldYield: true, attentionCandidate: HeroFocus);
            }
        }

        private IEnumerator TickFor(float seconds)
        {
            int frames = Mathf.CeilToInt(seconds / Step);
            for (int frame = 0; frame < frames; frame++)
            {
                AdvanceWalkers();
                yield return null;
            }
        }

        private IEnumerator TickUntil(Func<bool> condition, float seconds)
        {
            int frames = Mathf.CeilToInt(seconds / Step);
            for (int frame = 0; frame < frames && !condition(); frame++)
            {
                AdvanceWalkers();
                yield return null;
            }
        }

        private IEnumerator WaitUntilBubbleCloses(CityPedestrianPresentation owner)
        {
            float deadline = Time.realtimeSinceStartup + BubbleRealtimeDeadlineSeconds;
            while (bubbles.IsShowing(owner) && Time.realtimeSinceStartup < deadline)
            {
                AdvanceWalkers();
                yield return null;
            }

            Assert.That(bubbles.IsShowing(owner), Is.False,
                "The bubble must take itself down after its visible seconds.");
            // One more frame so the controller sees the closed line.
            AdvanceWalkers();
            yield return null;
        }

        private IEnumerator WaitForPlayerReady()
        {
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

        private void CreateObstruction()
        {
            obstruction = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstruction.name = "Insult Chest Height Obstruction";
            obstruction.transform.SetParent(root.transform, false);
            obstruction.transform.position = GroundOrigin + new Vector3(0f, 1.25f, 1f);
            obstruction.transform.localScale = new Vector3(1.2f, 0.5f, 0.03f);
            Physics.SyncTransforms();
        }

        private void RemoveObstruction()
        {
            if (obstruction != null)
            {
                UnityEngine.Object.DestroyImmediate(obstruction);
                obstruction = null;
                Physics.SyncTransforms();
            }
        }
    }
}
