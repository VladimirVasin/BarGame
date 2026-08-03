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
        private readonly List<StairwellCatFeedingSpriteLibrary>
            feedingLibraries =
                new List<StairwellCatFeedingSpriteLibrary>();

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
                 index < feedingLibraries.Count;
                 index++)
            {
                feedingLibraries[index]?.Dispose();
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
            feedingLibraries.Clear();
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
                    new Vector3(-1.55f, 1.64f, 1.78f)));
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
        public void FeedingSpriteLibrary_SlicesTopFirstGridWithAuthoredPivot()
        {
            StairwellCatFeedingSpriteLibrary library =
                CreateFeedingLibrary(CreateFeedingAtlas());

            Assert.That(
                library.Sprites,
                Has.Count.EqualTo(
                    StairwellCatFeedingSpriteLibrary.FrameCount));
            Assert.That(
                StairwellCatFeedingSpriteLibrary.FrameCount,
                Is.EqualTo(16));
            Assert.That(
                library.GetSprite(0).rect,
                Is.EqualTo(new Rect(0f, 64f, 64f, 64f)));
            Assert.That(
                library.GetSprite(7).rect,
                Is.EqualTo(new Rect(448f, 64f, 64f, 64f)));
            Assert.That(
                library.GetSprite(8).rect,
                Is.EqualTo(new Rect(0f, 0f, 64f, 64f)));
            Assert.That(
                library.GetSprite(15).rect,
                Is.EqualTo(new Rect(448f, 0f, 64f, 64f)));
            Assert.That(
                library.GetSprite(0).pivot,
                Is.EqualTo(new Vector2(32f, 4f)));
            Assert.That(
                library.GetSprite(0).pixelsPerUnit,
                Is.EqualTo(96f));
            Assert.That(
                library.Atlas.filterMode,
                Is.EqualTo(FilterMode.Point));
            Assert.That(
                library.Atlas.wrapMode,
                Is.EqualTo(TextureWrapMode.Clamp));
        }

        [Test]
        public void FeedingSpriteLibrary_RejectsWrongAtlasDimensions()
        {
            var atlas = new Texture2D(
                512,
                256,
                TextureFormat.RGBA32,
                false);
            textures.Add(atlas);

            Assert.Throws<ArgumentException>(
                () => StairwellCatFeedingSpriteLibrary.Create(
                    atlas));
        }

        [Test]
        public void FeedingTimeline_IsOneShotAndFrameChunkIndependent()
        {
            var singleStep = new StairwellCatFeedingTimeline();
            var chunked = new StairwellCatFeedingTimeline();

            Assert.That(singleStep.FrameIndex, Is.EqualTo(-1));
            Assert.That(singleStep.IsActive, Is.False);
            Assert.That(singleStep.Begin(), Is.True);
            Assert.That(singleStep.Begin(), Is.False);
            Assert.That(chunked.Begin(), Is.True);

            singleStep.Advance(1.75f);
            for (int index = 0; index < 7; index++)
            {
                chunked.Advance(0.25f);
            }

            Assert.That(singleStep.IsActive, Is.True);
            Assert.That(singleStep.FrameIndex, Is.EqualTo(10));
            Assert.That(
                chunked.FrameIndex,
                Is.EqualTo(singleStep.FrameIndex));
            Assert.That(
                chunked.ElapsedSeconds,
                Is.EqualTo(singleStep.ElapsedSeconds)
                    .Within(0.00001d));

            singleStep.Advance(0.75f);
            chunked.Advance(0.75f);
            Assert.That(singleStep.FrameIndex, Is.EqualTo(15));
            Assert.That(chunked.FrameIndex, Is.EqualTo(15));

            singleStep.Advance(1f / 6f);
            chunked.Advance(1f / 6f);
            Assert.That(singleStep.IsActive, Is.False);
            Assert.That(singleStep.FrameIndex, Is.EqualTo(-1));
            Assert.That(chunked.IsActive, Is.False);
            Assert.That(singleStep.Complete(), Is.False);
            Assert.That(singleStep.Cancel(), Is.False);
        }

        [Test]
        public void FeedingTimeline_RejectsInvalidDeltaAndSupportsCancel()
        {
            var timeline = new StairwellCatFeedingTimeline();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => timeline.Advance(-0.01f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => timeline.Advance(float.NaN));
            Assert.That(timeline.Begin(), Is.True);
            timeline.Advance(0.5f);
            Assert.That(timeline.FrameIndex, Is.EqualTo(3));
            Assert.That(timeline.Cancel(), Is.True);
            Assert.That(timeline.IsActive, Is.False);
            Assert.That(timeline.FrameIndex, Is.EqualTo(-1));
        }

        [Test]
        public void FeedingPlan_StagesEntryAndExitSafelyFacingCat()
        {
            StairwellLayoutPlan stairwell =
                StairwellLayoutPlanner.Generate();
            StairwellCatPlan cat =
                StairwellCatPlan.Create(stairwell);
            StairwellCatFeedingPlan feeding =
                StairwellCatFeedingPlan.Create(
                    stairwell,
                    cat);
            var walkable = new RoadWalkableArea(
                stairwell.WalkableRectangles);
            var selector = new StairwellCameraShotSelector(
                StairwellFixedCameraController
                    .CreateDefaultShots(stairwell));

            Assert.That(
                feeding.EntryRootLocalPosition,
                Is.EqualTo(cat.InteractionLocalPosition));
            Assert.That(
                walkable.Contains(
                    feeding.EntryRootLocalPosition,
                    StairwellLayoutValidator.PlayerRadius),
                Is.True);
            Assert.That(
                walkable.Contains(
                    feeding.ExitRootLocalPosition,
                    StairwellLayoutValidator.PlayerRadius),
                Is.True);
            AssertFinite(feeding.EntryRootLocalPosition);
            AssertFinite(feeding.ExitRootLocalPosition);
            Assert.That(
                feeding.ExitRootLocalPosition,
                Is.EqualTo(feeding.EntryRootLocalPosition));
            Assert.That(
                selector.Select(
                    feeding.EntryRootLocalPosition).Kind,
                Is.EqualTo(
                    StairwellCatFeedingPlan
                        .RequiredCameraShotKind));
            Assert.That(
                selector.Select(
                    feeding.ExitRootLocalPosition).Kind,
                Is.EqualTo(
                    StairwellCatFeedingPlan
                        .RequiredCameraShotKind));
            Assert.That(
                feeding.EntryHipLocalPosition,
                Is.EqualTo(feeding.ActionHipLocalPosition));
            Assert.That(
                feeding.ExitHipLocalPosition,
                Is.EqualTo(feeding.EntryHipLocalPosition));
            AssertFinite(feeding.EntryHipLocalPosition);
            AssertFinite(feeding.ExitHipLocalPosition);
            Assert.That(
                feeding.EntryHipLocalPosition.y,
                Is.EqualTo(
                    feeding.EntryRootLocalPosition.y +
                    (PlayerAnimatedInteractionController
                        .HipPivotYPixels -
                     PlayerSpriteRig.FeetPivotPixels) /
                    PlayerAnimatedInteractionController
                        .PixelsPerUnit +
                    StairwellCatFeedingPlan
                        .UprightVisualOffset)
                    .Within(0.0001f));
            Assert.That(
                feeding.EntryFacingLocalDirection.y,
                Is.Zero.Within(0.0001f));
            Assert.That(
                feeding.ExitFacingLocalDirection.y,
                Is.Zero.Within(0.0001f));
            Assert.That(
                feeding.EntryFacingLocalDirection.magnitude,
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(
                feeding.ExitFacingLocalDirection.magnitude,
                Is.EqualTo(1f).Within(0.0001f));
            AssertFinite(feeding.EntryFacingLocalDirection);
            AssertFinite(feeding.ExitFacingLocalDirection);
            Assert.That(
                Vector3.Dot(
                    feeding.EntryFacingLocalDirection,
                    cat.VisualLocalPosition -
                    feeding.EntryRootLocalPosition),
                Is.GreaterThan(0f));
            Assert.That(
                Vector3.Dot(
                    feeding.ExitFacingLocalDirection,
                    cat.VisualLocalPosition -
                    feeding.ExitRootLocalPosition),
                Is.GreaterThan(0f));
            Assert.That(
                Vector3.Angle(
                    feeding.EntryFacingLocalRotation *
                    Vector3.forward,
                    feeding.EntryFacingLocalDirection),
                Is.LessThan(0.001f));
            Assert.That(
                Vector3.Angle(
                    feeding.ExitFacingLocalRotation *
                    Vector3.forward,
                    feeding.ExitFacingLocalDirection),
                Is.LessThan(0.001f));

            Assert.That(
                feeding.PlayerRootLocalPosition,
                Is.EqualTo(feeding.EntryRootLocalPosition));
            Assert.That(
                feeding.StandHipLocalPosition,
                Is.EqualTo(feeding.EntryHipLocalPosition));
            Assert.That(
                feeding.FacingLocalDirection,
                Is.EqualTo(feeding.EntryFacingLocalDirection));
            Assert.That(
                feeding.FacingLocalRotation,
                Is.EqualTo(feeding.EntryFacingLocalRotation));
            Assert.That(
                feeding.EntryLocalRotation,
                Is.EqualTo(feeding.EntryFacingLocalRotation));
            Assert.That(
                feeding.ExitLocalRotation,
                Is.EqualTo(feeding.ExitFacingLocalRotation));
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

        [Test]
        public void Actor_FeedingOverridePausesAndRestoresIdlePresentation()
        {
            GameObject cameraObject =
                CreateGameObject("Feeding Test Camera");
            cameraObject.transform.rotation =
                Quaternion.LookRotation(Vector3.forward);
            Camera camera = cameraObject.AddComponent<Camera>();
            GameObject player =
                CreateGameObject("Feeding Test Player");
            player.transform.position = Vector3.right;
            GameObject cat =
                CreateGameObject("Feeding Test Actor");
            StairwellCatActor actor =
                cat.AddComponent<StairwellCatActor>();

            actor.Initialize(
                camera,
                player.transform,
                CreateAtlas(),
                CreateFeedingAtlas());
            actor.AdvancePresentation(
                StairwellCatIdleModel
                    .FirstGroomStartSeconds);
            Sprite pausedIdleSprite = actor.Renderer.sprite;
            int pausedIdleFrame = actor.CurrentFrame;
            StairwellCatIdleKind pausedIdleKind =
                actor.CurrentIdleKind;
            StairwellCatLook pausedLook = actor.CurrentLook;

            Assert.That(actor.TryPrepareFeeding(), Is.True);
            Assert.That(actor.IsFeedingPrepared, Is.True);
            Assert.That(actor.IsFeeding, Is.False);
            Assert.That(actor.Renderer.sprite, Is.SameAs(pausedIdleSprite));
            Assert.That(actor.BeginPreparedFeeding(), Is.True);
            Assert.That(actor.IsFeedingPrepared, Is.False);
            Assert.That(actor.BeginFeeding(), Is.False);
            Assert.That(actor.IsFeeding, Is.True);
            Assert.That(actor.CurrentFeedingFrame, Is.Zero);
            Assert.That(
                actor.Renderer.sprite.rect,
                Is.EqualTo(new Rect(0f, 64f, 64f, 64f)));

            player.transform.position = -Vector3.right;
            actor.AdvancePresentation(0.5f);
            Assert.That(actor.CurrentFeedingFrame, Is.EqualTo(3));
            Assert.That(actor.CurrentFrame, Is.EqualTo(pausedIdleFrame));
            Assert.That(actor.CurrentIdleKind, Is.EqualTo(pausedIdleKind));
            Assert.That(actor.CurrentLook, Is.EqualTo(pausedLook));

            Assert.That(actor.CancelFeeding(), Is.True);
            Assert.That(actor.CancelFeeding(), Is.False);
            Assert.That(actor.IsFeeding, Is.False);
            Assert.That(actor.CurrentFeedingFrame, Is.EqualTo(-1));
            Assert.That(actor.Renderer.sprite, Is.SameAs(pausedIdleSprite));

            Assert.That(actor.BeginFeeding(), Is.True);
            Assert.That(actor.CompleteFeeding(), Is.True);
            Assert.That(actor.CompleteFeeding(), Is.False);
            Assert.That(actor.Renderer.sprite, Is.SameAs(pausedIdleSprite));

            Assert.That(actor.TryPrepareFeeding(), Is.True);
            Assert.That(actor.CancelFeedingPreparation(), Is.True);
            Assert.That(actor.CancelFeedingPreparation(), Is.False);
            Assert.That(actor.BeginPreparedFeeding(), Is.False);
            Assert.That(actor.Renderer.sprite, Is.SameAs(pausedIdleSprite));

            Assert.That(actor.BeginFeeding(), Is.True);
            actor.AdvancePresentation(
                (float)StairwellCatFeedingTimeline
                    .DurationSeconds);
            Assert.That(actor.IsFeeding, Is.False);
            Assert.That(actor.Renderer.sprite, Is.SameAs(pausedIdleSprite));
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

        private Texture2D CreateFeedingAtlas()
        {
            var atlas = new Texture2D(
                512,
                128,
                TextureFormat.RGBA32,
                false)
            {
                name = "Stairwell Cat Feeding Test Atlas"
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

        private StairwellCatFeedingSpriteLibrary
            CreateFeedingLibrary(Texture2D atlas)
        {
            StairwellCatFeedingSpriteLibrary library =
                StairwellCatFeedingSpriteLibrary.Create(atlas);
            feedingLibraries.Add(library);
            return library;
        }

        private GameObject CreateGameObject(string name)
        {
            var gameObject = new GameObject(name);
            gameObjects.Add(gameObject);
            return gameObject;
        }

        private static void AssertFinite(Vector3 value)
        {
            Assert.That(IsFinite(value.x), Is.True);
            Assert.That(IsFinite(value.y), Is.True);
            Assert.That(IsFinite(value.z), Is.True);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
