using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class CityLocationMusicDirectorPlayModeTests
    {
        private static readonly Rect Grounds =
            new Rect(10f, 10f, 20f, 20f);

        private GameObject heroObject;
        private GameObject sceneThemeObject;
        private GameObject placeThemeObject;
        private GameObject directorObject;

        [SetUp]
        public void SetUp()
        {
            MusicMix.ClearFadeOuts();
        }

        [UnityTest]
        public IEnumerator EmptyLocationTrack_LeavesTheSceneThemePlaying()
        {
            CityMusicPlayer sceneTheme = CreateSceneTheme();
            placeThemeObject = new GameObject("Empty Place Theme");
            CemeteryMusicPlayer emptyTheme =
                placeThemeObject.AddComponent<CemeteryMusicPlayer>();
            yield return WaitForThemeReady(sceneTheme);

            CityLocationMusicDirector director = CreateDirector(
                sceneTheme,
                new CityLocationMusicSlot(
                    CityLocationMusicDirector.CemeteryLocationId,
                    Grounds,
                    emptyTheme));

            Assert.That(
                emptyTheme.ActiveClip,
                Is.Null,
                "The optional cemetery slot ships empty by design.");
            Assert.That(
                director.SlotCount,
                Is.Zero,
                "An empty slot must not be able to silence the city.");

            heroObject.transform.position = new Vector3(15f, 0f, 15f);
            Assert.That(director.RefreshLocation(), Is.False);
            Assert.That(
                director.ActiveLocationId,
                Is.EqualTo(CityLocationMusicDirector.DefaultLocationId));
        }

        [UnityTest]
        public IEnumerator EnteringGrounds_HandsTheMixOverThroughTheRule()
        {
            CityMusicPlayer sceneTheme = CreateSceneTheme();
            placeThemeObject = new GameObject("Place Theme");
            BarMusicPlayer placeTheme =
                placeThemeObject.AddComponent<BarMusicPlayer>();
            yield return WaitForThemeReady(sceneTheme);
            yield return WaitForThemeReady(placeTheme);

            CityLocationMusicDirector director = CreateDirector(
                sceneTheme,
                new CityLocationMusicSlot(
                    CityLocationMusicDirector.CemeteryLocationId,
                    Grounds,
                    placeTheme));

            Assert.That(director.SlotCount, Is.EqualTo(1));
            Assert.That(
                placeTheme.IsPaused,
                Is.True,
                "A place theme is parked until the hero walks into it.");
            sceneTheme.AdvanceFade(MusicMix.FadeInSeconds);
            Assert.That(sceneTheme.NormalizedGain, Is.EqualTo(1f));

            heroObject.transform.position = new Vector3(15f, 0f, 15f);
            Assert.That(director.RefreshLocation(), Is.True);
            Assert.That(
                director.ActiveLocationId,
                Is.EqualTo(CityLocationMusicDirector.CemeteryLocationId));
            Assert.That(
                sceneTheme.PlaybackState,
                Is.EqualTo(SceneMusicPlaybackState.FadingOut));
            Assert.That(
                placeTheme.IsFadeInDeferred,
                Is.True,
                "The place theme waits for the city theme to reach zero.");
            Assert.That(placeTheme.Source.isPlaying, Is.False);

            sceneTheme.AdvanceFade(MusicMix.FadeOutSeconds);
            Assert.That(sceneTheme.IsPaused, Is.True);
            yield return null;

            Assert.That(placeTheme.IsFadeInDeferred, Is.False);
            Assert.That(placeTheme.Source.isPlaying, Is.True);
            placeTheme.AdvanceFade(MusicMix.FadeInSeconds);
            Assert.That(
                placeTheme.PlaybackState,
                Is.EqualTo(SceneMusicPlaybackState.Playing));

            heroObject.transform.position = new Vector3(
                Grounds.xMin - CityLocationMusicZones.ExitMarginMeters * 0.5f,
                0f,
                15f);
            Assert.That(
                director.RefreshLocation(),
                Is.False,
                "The hold margin keeps the mix while the hero hugs the gate.");

            heroObject.transform.position = Vector3.zero;
            Assert.That(director.RefreshLocation(), Is.True);
            Assert.That(
                director.ActiveLocationId,
                Is.EqualTo(CityLocationMusicDirector.DefaultLocationId));
            Assert.That(
                placeTheme.PlaybackState,
                Is.EqualTo(SceneMusicPlaybackState.FadingOut));
            Assert.That(sceneTheme.IsFadeInDeferred, Is.True);

            placeTheme.AdvanceFade(MusicMix.FadeOutSeconds);
            yield return null;

            Assert.That(sceneTheme.Source.isPlaying, Is.True);
            sceneTheme.AdvanceFade(MusicMix.FadeInSeconds);
            Assert.That(
                sceneTheme.PlaybackState,
                Is.EqualTo(SceneMusicPlaybackState.Playing));
        }

        [UnityTest]
        public IEnumerator StartingInsideGrounds_OpensOnThatPlaceTheme()
        {
            CityMusicPlayer sceneTheme = CreateSceneTheme();
            placeThemeObject = new GameObject("Place Theme");
            BarMusicPlayer placeTheme =
                placeThemeObject.AddComponent<BarMusicPlayer>();
            yield return WaitForThemeReady(sceneTheme);
            yield return WaitForThemeReady(placeTheme);

            heroObject = new GameObject("Hero");
            heroObject.transform.position = new Vector3(15f, 0f, 15f);
            directorObject = new GameObject("City Location Music Test");
            CityLocationMusicDirector director =
                directorObject.AddComponent<CityLocationMusicDirector>();
            director.Initialize(
                heroObject.transform,
                sceneTheme,
                new[]
                {
                    new CityLocationMusicSlot(
                        CityLocationMusicDirector.CemeteryLocationId,
                        Grounds,
                        placeTheme)
                });
            director.enabled = false;

            Assert.That(
                director.ActiveLocationId,
                Is.EqualTo(CityLocationMusicDirector.CemeteryLocationId));
            Assert.That(
                sceneTheme.IsPaused,
                Is.True,
                "There is no handover to hear on the first frame.");
            Assert.That(MusicMix.IsFadeOutActive, Is.False);
            Assert.That(placeTheme.IsFadeInDeferred, Is.False);
            Assert.That(placeTheme.Source.isPlaying, Is.True);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MusicMix.ClearFadeOuts();
            DestroyObject(directorObject);
            DestroyObject(placeThemeObject);
            DestroyObject(sceneThemeObject);
            DestroyObject(heroObject);
            yield return null;
        }

        private CityMusicPlayer CreateSceneTheme()
        {
            sceneThemeObject = new GameObject("Scene Theme");
            return sceneThemeObject.AddComponent<CityMusicPlayer>();
        }

        private CityLocationMusicDirector CreateDirector(
            SceneMusicPlayer sceneTheme,
            CityLocationMusicSlot slot)
        {
            heroObject = new GameObject("Hero");
            heroObject.transform.position = Vector3.zero;
            directorObject = new GameObject("City Location Music Test");
            CityLocationMusicDirector director =
                directorObject.AddComponent<CityLocationMusicDirector>();
            director.Initialize(
                heroObject.transform,
                sceneTheme,
                new[] { slot });

            // The fixture drives the boundary test by hand so the assertions
            // do not race the per-frame refresh.
            director.enabled = false;
            return director;
        }

        private static void DestroyObject(GameObject target)
        {
            if (target != null)
            {
                Object.Destroy(target);
            }
        }

        private static IEnumerator WaitForThemeReady(SceneMusicPlayer player)
        {
            float deadline = Time.realtimeSinceStartup + 5f;
            while (player.PlaybackState ==
                       SceneMusicPlaybackState.Loading &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                player.PlaybackState,
                Is.Not.EqualTo(SceneMusicPlaybackState.Loading),
                "The streaming theme did not finish loading.");
        }
    }
}
