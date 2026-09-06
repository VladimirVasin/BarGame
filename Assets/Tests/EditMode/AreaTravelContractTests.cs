using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class AreaTravelContractTests
    {
        [TestCase(GameAreaId.City, SceneIds.City)]
        [TestCase(GameAreaId.MountainRoad, SceneIds.MountainRoad)]
        [TestCase(GameAreaId.AlpineVillage, SceneIds.AlpineVillage)]
        public void SceneCatalog_MapsEachAreaToOneSeparateScene(
            GameAreaId area,
            string expectedScene)
        {
            Assert.That(
                AreaSceneCatalog.IsSupported(area),
                Is.True);
            Assert.That(
                AreaSceneCatalog.GetSceneName(area),
                Is.EqualTo(expectedScene));
            Assert.That(
                AreaSceneCatalog.TryGetArea(
                    expectedScene,
                    out GameAreaId roundTrip),
                Is.True);
            Assert.That(roundTrip, Is.EqualTo(area));
            Assert.That(
                expectedScene,
                Is.Not.EqualTo(SceneIds.AreaLoading));
        }

        [Test]
        public void Request_PreservesDestinationAndArrivalSemantics()
        {
            var request = new AreaTravelRequest(
                GameAreaId.MountainRoad,
                AreaArrivalToken.MapTeleport);

            Assert.That(request.IsValid, Is.True);
            Assert.That(
                request.DestinationArea,
                Is.EqualTo(GameAreaId.MountainRoad));
            Assert.That(
                request.ArrivalToken,
                Is.EqualTo(AreaArrivalToken.MapTeleport));
            Assert.That(
                request,
                Is.EqualTo(
                    new AreaTravelRequest(
                        GameAreaId.MountainRoad,
                    AreaArrivalToken.MapTeleport)));
        }

        [TestCase(GameAreaId.City, GameAreaId.MountainRoad, AreaLoadingArtCatalog.CityToMountain)]
        [TestCase(GameAreaId.MountainRoad, GameAreaId.City, AreaLoadingArtCatalog.MountainToCity)]
        [TestCase(GameAreaId.MountainRoad, GameAreaId.AlpineVillage, AreaLoadingArtCatalog.MountainToVillage)]
        [TestCase(GameAreaId.AlpineVillage, GameAreaId.MountainRoad, AreaLoadingArtCatalog.VillageToMountain)]
        [TestCase(GameAreaId.City, GameAreaId.AlpineVillage, AreaLoadingArtCatalog.MountainToVillage)]
        [TestCase(GameAreaId.AlpineVillage, GameAreaId.City, AreaLoadingArtCatalog.MountainToCity)]
        public void LoadingArt_EachJourneyUsesItsLastDirectedLeg(
            GameAreaId source, GameAreaId destination, string expectedResource)
        {
            Assert.That(AreaLoadingArtCatalog.GetResourcePath(source, destination),
                Is.EqualTo(expectedResource));
        }

        [Test]
        public void LoadingArt_UnknownOrSameAreaKeepsTheNeutralFallback()
        {
            Assert.That(AreaLoadingArtCatalog.GetResourcePath(null, GameAreaId.City), Is.Empty);
            Assert.That(AreaLoadingArtCatalog.GetResourcePath((GameAreaId)99, GameAreaId.City), Is.Empty);
            Assert.That(AreaLoadingArtCatalog.GetResourcePath(GameAreaId.City, (GameAreaId)99), Is.Empty);
            foreach (GameAreaId area in new[] { GameAreaId.City,
                GameAreaId.MountainRoad, GameAreaId.AlpineVillage })
            {
                Assert.That(AreaLoadingArtCatalog.GetResourcePath(area, area), Is.Empty);
            }
        }

        [TestCase(640, 360)]
        [TestCase(1280, 720)]
        [TestCase(1920, 1080)]
        [TestCase(1280, 960)]
        [TestCase(2560, 1080)]
        [TestCase(3440, 1440)]
        public void LoadingBar_UsesTheViewportBottomAtEveryAspectRatio(int width, int height)
        {
            Rect track = AreaLoadingRoot.CalculateTrackRect(width, height);
            float scale = RetroUiTheme.CalculateCanvas(width, height).Scale;
            Assert.That(track.center.x, Is.EqualTo(width * 0.5f).Within(0.01f));
            Assert.That(track.width, Is.EqualTo(284f * scale).Within(0.01f));
            Assert.That(track.height, Is.EqualTo(12f * scale).Within(0.01f));
            Assert.That(height - track.yMax, Is.EqualTo(22f * scale).Within(0.01f),
                "The margin belongs to the physical screen, not the centered 16:9 canvas.");
            Assert.That(track.xMin, Is.GreaterThanOrEqualTo(0f));
            Assert.That(track.yMin, Is.GreaterThan(height * 0.8f));
            Assert.That(track.xMax, Is.LessThanOrEqualTo(width));
            Assert.That(track.yMax, Is.LessThan(height));
        }

        [Test]
        public void LoadingArt_SharedOwnersLoadOnceAndReleaseOnlyAfterTheLastOverlay()
        {
            var texture = new Texture2D(2, 2);
            int loaded = 0;
            int unloaded = 0;
            var cache = new AreaLoadingArtworkCache(
                _ => { loaded++; return texture; },
                value => { Assert.That(value, Is.SameAs(texture)); unloaded++; });
            AreaLoadingArtworkCache.Lease first = null;
            AreaLoadingArtworkCache.Lease second = null;
            AreaLoadingArtworkCache.Lease later = null;
            try
            {
                first = cache.Acquire(AreaLoadingArtCatalog.CityToMountain);
                second = cache.Acquire(AreaLoadingArtCatalog.CityToMountain);
                Assert.That(loaded, Is.EqualTo(1));
                Assert.That(first.Texture, Is.SameAs(second.Texture));
                first.Dispose();
                first.Dispose();
                Assert.That(first.Texture, Is.Null);
                Assert.That(unloaded, Is.Zero);
                Assert.That(second.Texture, Is.SameAs(texture));
                second.Dispose();
                Assert.That(unloaded, Is.EqualTo(1));

                later = cache.Acquire(AreaLoadingArtCatalog.CityToMountain);
                Assert.That(loaded, Is.EqualTo(2));
                first.Dispose();
                second.Dispose();
                Assert.That(later.Texture, Is.SameAs(texture));
                Assert.That(unloaded, Is.EqualTo(1));
                later.Dispose();
                Assert.That(unloaded, Is.EqualTo(2));
            }
            finally
            {
                first?.Dispose();
                second?.Dispose();
                later?.Dispose();
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void LoadingArt_MissingTextureLeavesNoOwnerOrUnloadWork()
        {
            int loads = 0;
            int unloads = 0;
            var cache = new AreaLoadingArtworkCache(
                _ => { loads++; return null; }, _ => unloads++);
            Assert.That(cache.Acquire(string.Empty), Is.Null);
            Assert.That(cache.Acquire(null), Is.Null);
            Assert.That(loads, Is.Zero);
            Assert.That(cache.Acquire("UI/Loading/missing"), Is.Null);
            Assert.That(loads, Is.EqualTo(1));
            Assert.That(unloads, Is.Zero);
        }

        [Test]
        public void LoadingArt_FourRuntimeResourcesHaveTheScopedWindowsImportContract()
        {
            Assert.That(AreaLoadingArtCatalog.ResourcePaths.Count, Is.EqualTo(4));
            Assert.That(new HashSet<string>(AreaLoadingArtCatalog.ResourcePaths).Count, Is.EqualTo(4));
            foreach (string resource in AreaLoadingArtCatalog.ResourcePaths)
            {
                string path = $"Assets/Resources/{resource}.png";
                Texture2D asset = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Assert.That(asset, Is.Not.Null, path);
                Assert.That(Resources.Load<Texture2D>(resource), Is.SameAs(asset), resource);
                Assert.That(asset.isReadable, Is.False, path);
                Assert.That(asset.mipmapCount, Is.EqualTo(1), path);
            }

            Type importer = Type.GetType(
                "BarPromenade.Editor.AreaLoadingArtImporter, BarPromenade.Editor", true);
            try { importer.GetMethod("ValidateOrThrow").Invoke(null, null); }
            catch (TargetInvocationException failure)
            {
                Assert.Fail((failure.InnerException ?? failure).ToString());
            }

            MethodInfo scope = importer.GetMethod("IsArtworkPath");
            Assert.That(scope.Invoke(null, new object[] {
                "Assets/Resources/UI/Loading/city-to-mountain.png" }), Is.EqualTo(true));
            foreach (string unrelated in new[] {
                "Assets/Resources/UI/Loading/other.png",
                "Assets/Resources/UI/Loading/nested/city-to-mountain.png",
                "Assets/Resources/UI/Other/city-to-mountain.png" })
            {
                Assert.That(scope.Invoke(null, new object[] { unrelated }), Is.EqualTo(false), unrelated);
            }
        }

        [TestCase(0f, 0f, 0f)]
        [TestCase(0.45f, 0.275f, 0.1f)]
        [TestCase(0.90f, 0.275f, 0.1f)]
        [TestCase(0.45f, 1f, 0.1f)]
        [TestCase(0.90f, 1f, 0.2f)]
        public void LoadingProgress_ReservesMostOfTheBarForWorldComposition(
            float sceneProgress,
            float visibleSeconds,
            float expected)
        {
            Assert.That(
                AreaTravelService.EvaluateDisplayedProgress(
                    sceneProgress,
                    visibleSeconds),
                Is.EqualTo(expected).Within(0.0001f));
        }

        [Test]
        public void Composition_BudgetedAndSynchronousPathsHaveIdenticalOrder()
        {
            var synchronous = new List<int>();
            var staged = new List<int>();
            var progress = new List<float>();
            RuntimeComposition.RunSynchronously(Sequence(synchronous));
            int frames = 0;
            using (var work = new RuntimeComposition(Sequence(staged)))
            {
                while (work.AdvanceFrame(step => progress.Add(step.Progress), 0d))
                {
                    Assert.That(++frames, Is.LessThan(10));
                }
            }

            Assert.That(staged, Is.EqualTo(synchronous));
            Assert.That(staged, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(frames, Is.EqualTo(3));
            Assert.That(progress, Is.EqualTo(new[] { 0.1f, 0.5f, 1f }));
        }

        [Test]
        public void Composition_InterruptedNestedStageReleasesItsOwner()
        {
            bool disposed = false;
            using (var work = new RuntimeComposition(
                       RuntimeComposition.Range(OwnedStage(() => disposed = true), 0f, 1f)))
            {
                Assert.That(work.AdvanceFrame(null, 0d), Is.True);
                Assert.That(disposed, Is.False);
            }

            Assert.That(disposed, Is.True);
        }

        private static IEnumerator Sequence(List<int> order)
        {
            order.Add(1);
            yield return new CompositionStep("first", 0.1f);
            yield return RuntimeComposition.Range(Middle(order), 0f, 1f);
            order.Add(3);
            yield return new CompositionStep("last", 1f);
        }

        private static IEnumerator Middle(List<int> order)
        {
            order.Add(2);
            yield return new CompositionStep("middle", 0.5f);
        }

        private static IEnumerator OwnedStage(Action release)
        {
            try { yield return new CompositionStep("held", 0.5f); }
            finally { release(); }
        }
    }
}
