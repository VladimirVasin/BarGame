using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class BarNpcRuntimeTests
    {
        private readonly List<GameObject> gameObjects =
            new List<GameObject>();
        private readonly List<Texture2D> textures =
            new List<Texture2D>();
        private readonly List<BarNpcSpriteLibrary> libraries =
            new List<BarNpcSpriteLibrary>();

        [TearDown]
        public void TearDown()
        {
            for (int index = gameObjects.Count - 1;
                 index >= 0;
                 index--)
            {
                if (gameObjects[index] != null)
                {
                    Object.DestroyImmediate(gameObjects[index]);
                }
            }

            for (int index = 0; index < libraries.Count; index++)
            {
                libraries[index]?.Dispose();
            }

            for (int index = 0; index < textures.Count; index++)
            {
                if (textures[index] != null)
                {
                    Object.DestroyImmediate(textures[index]);
                }
            }

            gameObjects.Clear();
            textures.Clear();
            libraries.Clear();
        }

        [Test]
        public void SpriteLibrary_SlicesTopFirstGridAndSharesSprites()
        {
            Texture2D atlas = CreateAtlas();
            BarNpcSpriteLibrary library =
                CreateLibrary(atlas);

            Assert.That(
                library.Sprites,
                Has.Count.EqualTo(
                    BarNpcSpriteLibrary.VariantCount));
            Assert.That(
                library.GetSprite(0).rect,
                Is.EqualTo(new Rect(0f, 10f, 10f, 10f)));
            Assert.That(
                library.GetSprite(2).rect,
                Is.EqualTo(new Rect(20f, 10f, 10f, 10f)));
            Assert.That(
                library.GetSprite(3).rect,
                Is.EqualTo(new Rect(0f, 0f, 10f, 10f)));
            Assert.That(
                library.GetSprite(0),
                Is.SameAs(library.GetSprite(0)));
            Assert.That(
                library.GetSprite(0).pixelsPerUnit,
                Is.EqualTo(
                    BarNpcSpriteLibrary.PixelsPerUnit));
        }

        [Test]
        public void DefaultSpriteLibrary_LoadsAuthoredAtlasAtNpcScale()
        {
            Assert.That(
                BarNpcSpriteLibrary.TryLoadDefault(
                    out BarNpcSpriteLibrary library),
                Is.True);
            libraries.Add(library);

            Assert.That(
                library.Atlas.width,
                Is.EqualTo(1536));
            Assert.That(
                library.Atlas.height,
                Is.EqualTo(1024));
            Assert.That(
                library.GetSprite(0).rect.size,
                Is.EqualTo(new Vector2(512f, 512f)));
            Assert.That(
                library.GetSprite(0).pivot.y,
                Is.EqualTo(
                    BarNpcSpriteLibrary.FeetPivotPixels)
                    .Within(0.01f));
            Assert.That(
                library.GetSprite(3).pivot.y,
                Is.EqualTo(
                    BarNpcSpriteLibrary.LowerRowFeetPivotPixels)
                    .Within(0.01f));
            Assert.That(
                library.GetSprite(0).bounds.size.y,
                Is.EqualTo(2f).Within(0.001f));
        }

        [Test]
        public void Factory_CreatesLightweightActorsUsingSharedLibrary()
        {
            BarNpcPlan plan = BarNpcPlanner.Create(
                712,
                "bar-runtime",
                BarActivityKind.BeerPong,
                BarNpcPlannerTests.CreateAnchors(16));
            Camera camera = CreateCamera();
            GameObject parent = CreateGameObject("NPC Parent");
            BarNpcSpriteLibrary library =
                CreateLibrary(CreateAtlas());

            BarNpcDirector director = BarNpcFactory.Create(
                parent.transform,
                camera,
                plan,
                library);

            Assert.That(director.IsInitialized, Is.True);
            Assert.That(
                director.Actors,
                Has.Count.EqualTo(plan.Count));
            Assert.That(
                director.GetComponentsInChildren<SpriteRenderer>(
                    true),
                Has.Length.EqualTo(plan.Count));
            Assert.That(
                director.GetComponentsInChildren<CharacterController>(
                    true),
                Is.Empty);
            Assert.That(
                director.GetComponentsInChildren<PlayerMotor>(true),
                Is.Empty);
            Assert.That(
                director.GetComponentsInChildren<PlayerInteractor>(
                    true),
                Is.Empty);
            Assert.That(
                director.GetComponentsInChildren<PlayerDynamicShadow>(
                    true),
                Is.Empty);

            foreach (BarNpcActor actor in director.Actors)
            {
                Assert.That(actor.IsInitialized, Is.True);
                Assert.That(
                    actor.Renderer.sprite,
                    Is.SameAs(library.GetSprite(
                        actor.Definition.VisualVariant)));
            }

            int distinctMaterials = director.Actors
                .Select(actor => actor.Renderer.sharedMaterial)
                .Distinct()
                .Count();
            Assert.That(distinctMaterials, Is.LessThanOrEqualTo(1));

            IGrouping<int, BarNpcActor> sharedVariant =
                director.Actors
                    .GroupBy(
                        actor =>
                            actor.Definition.VisualVariant)
                    .First(group => group.Count() > 1);
            BarNpcActor[] matchingActors =
                sharedVariant.ToArray();
            Assert.That(
                matchingActors[1].Renderer.sprite,
                Is.SameAs(matchingActors[0].Renderer.sprite));
        }

        [Test]
        public void Director_SortsEachNpcAroundThePlayerPuppet()
        {
            BarNpcPlan plan = BarNpcPlanner.Create(
                819,
                "bar-depth-sort",
                BarActivityKind.Cocktail,
                BarNpcPlannerTests.CreateAnchors(12));
            Camera camera = CreateCamera();
            GameObject parent = CreateGameObject("NPC Parent");
            GameObject player = CreateGameObject("Player Reference");
            player.transform.position = new Vector3(0f, 0f, 1f);
            BarNpcDirector director = BarNpcFactory.Create(
                parent.transform,
                camera,
                plan,
                CreateLibrary(CreateAtlas()));

            director.ConfigureDepthSorting(
                camera,
                player.transform);
            director.Advance(0f);

            float playerDepth = Vector3.Dot(
                player.transform.position -
                camera.transform.position,
                camera.transform.forward);
            foreach (BarNpcActor actor in director.Actors)
            {
                float actorDepth = Vector3.Dot(
                    actor.transform.position -
                    camera.transform.position,
                    camera.transform.forward);
                Assert.That(
                    actor.Renderer.sortingOrder,
                    Is.EqualTo(
                        actorDepth < playerDepth
                            ? 10
                            : -10));
            }
        }

        [Test]
        public void Director_FixedDecisionTick_IsFrameChunkInvariant()
        {
            BarNpcPlan plan = BarNpcPlanner.Create(
                994,
                "bar-fixed-tick",
                BarActivityKind.SplitTheG,
                BarNpcPlannerTests.CreateAnchors(8),
                8);
            Camera camera = CreateCamera();
            BarNpcSpriteLibrary library =
                CreateLibrary(CreateAtlas());
            GameObject firstParent =
                CreateGameObject("First NPC Parent");
            GameObject secondParent =
                CreateGameObject("Second NPC Parent");
            BarNpcDirector oneChunk = BarNpcFactory.Create(
                firstParent.transform,
                camera,
                plan,
                library);
            BarNpcDirector manyChunks = BarNpcFactory.Create(
                secondParent.transform,
                camera,
                plan,
                library);

            oneChunk.Advance(5f);
            for (int index = 0; index < 40; index++)
            {
                manyChunks.Advance(
                    BarNpcDirector.DecisionStepSeconds);
            }

            Assert.That(
                manyChunks.DecisionTickCount,
                Is.EqualTo(oneChunk.DecisionTickCount));
            for (int index = 0; index < plan.Count; index++)
            {
                Assert.That(
                    manyChunks.GetActionSequence(index),
                    Is.EqualTo(
                        oneChunk.GetActionSequence(index)));
                Assert.That(
                    manyChunks.GetRemainingActionTicks(index),
                    Is.EqualTo(
                        oneChunk.GetRemainingActionTicks(index)));
                Assert.That(
                    manyChunks.Actors[index].CurrentAction,
                    Is.EqualTo(
                        oneChunk.Actors[index].CurrentAction));
            }
        }

        [Test]
        public void BehaviorRules_OnlySelectRoleCompatibleActions()
        {
            BarNpcPlan plan = BarNpcPlanner.Create(
                183,
                "bar-behavior",
                BarActivityKind.TinctureMatch,
                BarNpcPlannerTests.CreateAnchors(14),
                14);

            foreach (BarNpcDefinition definition
                     in plan.Definitions)
            {
                for (int sequence = 0;
                     sequence < 50;
                     sequence++)
                {
                    BarNpcAction action =
                        BarNpcBehaviorRules.SelectAction(
                            definition,
                            sequence);
                    Assert.That(
                        BarNpcBehaviorRules.IsAllowed(
                            definition,
                            action),
                        Is.True,
                        $"{definition.Id}, sequence {sequence}");
                    Assert.That(
                        BarNpcBehaviorRules.GetDurationTicks(
                            definition,
                            action,
                            sequence),
                        Is.GreaterThan(0));
                }
            }
        }

        private Camera CreateCamera()
        {
            GameObject cameraObject =
                CreateGameObject("NPC Test Camera");
            cameraObject.transform.position =
                new Vector3(0f, 2f, -8f);
            cameraObject.transform.rotation =
                Quaternion.LookRotation(Vector3.forward);
            return cameraObject.AddComponent<Camera>();
        }

        private Texture2D CreateAtlas()
        {
            var atlas = new Texture2D(
                30,
                20,
                TextureFormat.RGBA32,
                false)
            {
                name = "Bar NPC Test Atlas"
            };
            textures.Add(atlas);
            return atlas;
        }

        private BarNpcSpriteLibrary CreateLibrary(
            Texture2D atlas)
        {
            BarNpcSpriteLibrary library =
                BarNpcSpriteLibrary.Create(atlas);
            libraries.Add(library);
            return library;
        }

        private GameObject CreateGameObject(string name)
        {
            var gameObject = new GameObject(name);
            gameObjects.Add(gameObject);
            return gameObject;
        }
    }
}
