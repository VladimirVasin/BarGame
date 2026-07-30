using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade.Tests.EditMode
{
    public sealed class StairwellCatRuntimeTests
    {
        private readonly List<GameObject> gameObjects =
            new List<GameObject>();
        private readonly List<Texture2D> textures =
            new List<Texture2D>();
        private readonly List<StairwellCatSpriteLibrary> libraries =
            new List<StairwellCatSpriteLibrary>();

        [TearDown]
        public void TearDown()
        {
            for (int index = gameObjects.Count - 1;
                 index >= 0;
                 index--)
            {
                if (gameObjects[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        gameObjects[index]);
                }
            }

            for (int index = 0;
                 index < libraries.Count;
                 index++)
            {
                libraries[index]?.Dispose();
            }

            for (int index = 0;
                 index < textures.Count;
                 index++)
            {
                if (textures[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        textures[index]);
                }
            }

            gameObjects.Clear();
            textures.Clear();
            libraries.Clear();
        }

        [Test]
        public void SpriteLibrary_SlicesTopFirstGridWithAuthoredPivot()
        {
            StairwellCatSpriteLibrary library =
                CreateLibrary(CreateAtlas());

            Assert.That(
                library.Sprites,
                Has.Count.EqualTo(
                    StairwellCatSpriteLibrary.FrameCount));
            Assert.That(
                StairwellCatSpriteLibrary.FrameCount,
                Is.EqualTo(32));
            Assert.That(
                library.Atlas.width,
                Is.EqualTo(512));
            Assert.That(
                library.Atlas.height,
                Is.EqualTo(256));
            Assert.That(
                library.GetSprite(
                    StairwellCatLook.LookLeft,
                    0).rect,
                Is.EqualTo(new Rect(0f, 192f, 64f, 64f)));
            Assert.That(
                library.GetSprite(
                    StairwellCatLook.Center,
                    3).rect,
                Is.EqualTo(new Rect(192f, 128f, 64f, 64f)));
            Assert.That(
                library.GetSprite(
                    StairwellCatLook.LookRight,
                    7).rect,
                Is.EqualTo(new Rect(448f, 64f, 64f, 64f)));
            Assert.That(
                library.GetGroomSprite(7).rect,
                Is.EqualTo(new Rect(448f, 0f, 64f, 64f)));
            Assert.That(
                library.GetSprite(
                    StairwellCatLook.Center,
                    0).pivot,
                Is.EqualTo(new Vector2(32f, 4f)));
            Assert.That(
                library.GetSprite(
                    StairwellCatLook.Center,
                    0).pixelsPerUnit,
                Is.EqualTo(96f));
            Assert.That(
                library.Atlas.filterMode,
                Is.EqualTo(FilterMode.Point));
            Assert.That(
                library.Atlas.wrapMode,
                Is.EqualTo(TextureWrapMode.Clamp));
        }

        [Test]
        public void Plan_PerchesAboveMiddleRailAndKeepsApproachWalkable()
        {
            StairwellLayoutPlan stairwell =
                StairwellLayoutPlanner.Generate();
            StairwellCatPlan plan =
                StairwellCatPlan.Create(stairwell);
            var walkable = new RoadWalkableArea(
                stairwell.WalkableRectangles);

            Assert.That(
                plan.VisualLocalPosition,
                Is.EqualTo(
                    new Vector3(-1.70f, 2.83f, 2.32f)));
            Assert.That(
                plan.InteractionLocalPosition,
                Is.EqualTo(
                    new Vector3(-1.55f, 1.74f, 1.78f)));
            Assert.That(
                walkable.Contains(
                    plan.InteractionLocalPosition,
                    StairwellLayoutValidator.PlayerRadius),
                Is.True);
            Assert.That(
                Vector3.Distance(
                    plan.VisualLocalPosition,
                    plan.InteractionLocalPosition),
                Is.LessThan(1.65f));
            Assert.That(
                plan.TriggerLocalCenter,
                Is.EqualTo(
                    new Vector3(0f, -0.55f, -0.27f)));
            Assert.That(
                StairwellCatPlan.TriggerSize,
                Is.EqualTo(
                    new Vector3(0.72f, 1.30f, 1.05f)));
        }

        [Test]
        public void SpriteLibrary_RejectsAtlasWithWrongCellSize()
        {
            var atlas = new Texture2D(
                512,
                192,
                TextureFormat.RGBA32,
                false);
            textures.Add(atlas);

            Assert.Throws<ArgumentException>(
                () => StairwellCatSpriteLibrary.Create(atlas));
        }

        [Test]
        public void IdleModel_IsInvariantToFrameChunking()
        {
            var oneChunk = new StairwellCatIdleModel(321);
            var manyChunks = new StairwellCatIdleModel(321);

            oneChunk.Advance(24.75f);
            for (int index = 0; index < 99; index++)
            {
                manyChunks.Advance(0.25f);
            }

            Assert.That(
                oneChunk.CurrentKind,
                Is.EqualTo(StairwellCatIdleKind.Groom));
            Assert.That(
                manyChunks.CurrentKind,
                Is.EqualTo(oneChunk.CurrentKind));
            Assert.That(
                manyChunks.CurrentFrame,
                Is.EqualTo(oneChunk.CurrentFrame));
            Assert.That(
                manyChunks.ElapsedSeconds,
                Is.EqualTo(oneChunk.ElapsedSeconds)
                    .Within(0.00001d));
        }

        [Test]
        public void IdleModel_GroomRunsAllFramesAndEnds()
        {
            var model = new StairwellCatIdleModel(321);

            model.Advance(
                StairwellCatIdleModel
                    .FirstGroomStartSeconds -
                0.01f);
            Assert.That(
                model.CurrentKind,
                Is.Not.EqualTo(StairwellCatIdleKind.Groom));

            model.Reset();
            model.Advance(
                StairwellCatIdleModel
                    .FirstGroomStartSeconds);
            for (int frame = 0;
                 frame <
                 StairwellCatIdleModel.GroomFrameCount;
                 frame++)
            {
                Assert.That(
                    model.CurrentKind,
                    Is.EqualTo(StairwellCatIdleKind.Groom));
                Assert.That(
                    model.CurrentFrame,
                    Is.EqualTo(frame));
                model.Advance(
                    StairwellCatIdleModel
                        .GroomFrameSeconds);
            }

            Assert.That(
                model.CurrentKind,
                Is.Not.EqualTo(StairwellCatIdleKind.Groom));
            Assert.That(
                model.CurrentFrame,
                Is.InRange(0, 7));

            model.Advance(
                StairwellCatIdleModel
                    .GroomIntervalSeconds -
                StairwellCatIdleModel
                    .GroomDurationSeconds);
            Assert.That(
                model.CurrentKind,
                Is.EqualTo(StairwellCatIdleKind.Groom));
            Assert.That(model.CurrentFrame, Is.Zero);
        }

        [Test]
        public void LookSelector_UsesCenterHysteresis()
        {
            var selector = new StairwellCatLookSelector(
                0.4f,
                0.15f);

            Assert.That(
                selector.UpdateProjectedOffset(0.41f),
                Is.EqualTo(StairwellCatLook.LookRight));
            Assert.That(
                selector.UpdateProjectedOffset(0.25f),
                Is.EqualTo(StairwellCatLook.LookRight));
            Assert.That(
                selector.UpdateProjectedOffset(0.14f),
                Is.EqualTo(StairwellCatLook.Center));
            Assert.That(
                selector.UpdateProjectedOffset(-0.41f),
                Is.EqualTo(StairwellCatLook.LookLeft));
            Assert.That(
                selector.UpdateProjectedOffset(-0.25f),
                Is.EqualTo(StairwellCatLook.LookLeft));
            Assert.That(
                selector.UpdateProjectedOffset(-0.14f),
                Is.EqualTo(StairwellCatLook.Center));
        }

        [Test]
        public void Actor_BuildsDepthTestedCameraPlaneSprite()
        {
            GameObject cameraObject =
                CreateGameObject("Cat Test Camera");
            cameraObject.transform.position =
                new Vector3(0f, 2f, -4f);
            cameraObject.transform.rotation =
                Quaternion.LookRotation(Vector3.forward);
            Camera camera = cameraObject.AddComponent<Camera>();
            GameObject player =
                CreateGameObject("Cat Test Player");
            player.transform.position =
                new Vector3(1f, 0f, 0f);
            GameObject cat =
                CreateGameObject("Cat Test Actor");
            StairwellCatActor actor =
                cat.AddComponent<StairwellCatActor>();

            actor.Initialize(
                camera,
                player.transform,
                CreateAtlas());

            Assert.That(actor.IsInitialized, Is.True);
            Assert.That(actor.Renderer, Is.Not.Null);
            Assert.That(actor.Billboard, Is.Not.Null);
            Assert.That(
                actor.Billboard.CameraPlaneAlignmentEnabled,
                Is.True);
            Assert.That(
                actor.Renderer.shadowCastingMode,
                Is.EqualTo(ShadowCastingMode.Off));
            Assert.That(actor.Renderer.receiveShadows, Is.False);
            Assert.That(
                actor.CurrentLook,
                Is.EqualTo(StairwellCatLook.LookRight));
            Assert.That(
                actor.Renderer.sprite,
                Is.Not.Null);

            actor.AdvancePresentation(
                StairwellCatIdleModel
                    .FirstGroomStartSeconds);
            Assert.That(
                actor.CurrentIdleKind,
                Is.EqualTo(StairwellCatIdleKind.Groom));
            Assert.That(
                actor.Renderer.sprite.rect,
                Is.EqualTo(
                    new Rect(0f, 0f, 64f, 64f)));

            actor.AdvancePresentation(
                StairwellCatIdleModel
                    .GroomDurationSeconds);
            Assert.That(
                actor.CurrentIdleKind,
                Is.Not.EqualTo(StairwellCatIdleKind.Groom));
            Assert.That(
                actor.Renderer.sprite.rect.y,
                Is.GreaterThanOrEqualTo(64f));
        }

        private Texture2D CreateAtlas()
        {
            var atlas = new Texture2D(
                512,
                256,
                TextureFormat.RGBA32,
                false)
            {
                name = "Stairwell Cat Test Atlas"
            };
            textures.Add(atlas);
            return atlas;
        }

        private StairwellCatSpriteLibrary CreateLibrary(
            Texture2D atlas)
        {
            StairwellCatSpriteLibrary library =
                StairwellCatSpriteLibrary.Create(atlas);
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
