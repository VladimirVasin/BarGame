using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class RetroAudioServicePlayModeTests
    {
        [UnityTest]
        public IEnumerator EnsureInstalled_CreatesOnePersistentBoundedService()
        {
            RetroAudioService service =
                RetroAudioService.EnsureInstalled();
            yield return null;

            Assert.That(service, Is.Not.Null);
            Assert.That(service.IsInitialized, Is.True);
            Assert.That(
                RetroAudioService.EnsureInstalled(),
                Is.SameAs(service));
            Assert.That(
                Object.FindObjectsByType<RetroAudioService>(
                    FindObjectsInactive.Include),
                Has.Length.EqualTo(1));
            Assert.That(
                service.GeneratedClipCount,
                Is.EqualTo(RetroSfxLibrary.Count));
            Assert.That(
                service.SourceCount,
                Is.EqualTo(RetroAudioService.TotalPoolSize));
            Assert.That(
                service.GetPoolSize(RetroSfxCategory.Ui),
                Is.EqualTo(RetroAudioService.UiPoolSize));
            Assert.That(
                service.GetPoolSize(RetroSfxCategory.World),
                Is.EqualTo(RetroAudioService.WorldPoolSize));
            Assert.That(
                service.GetPoolSize(RetroSfxCategory.Bar),
                Is.EqualTo(RetroAudioService.BarPoolSize));

            for (int index = 1;
                 index < (int)RetroSfxId.Count;
                 index++)
            {
                AudioClip clip = service.GetClip((RetroSfxId)index);
                Assert.That(clip, Is.Not.Null);
                Assert.That(
                    clip.frequency,
                    Is.EqualTo(RetroSfxLibrary.SampleRate));
                Assert.That(clip.channels, Is.EqualTo(1));
            }
        }

        [UnityTest]
        public IEnumerator TryPlay_UsesCooldownAndAllThreePools()
        {
            RetroAudioService service =
                RetroAudioService.EnsureInstalled();
            service.StopAll();

            Assert.That(
                service.TryPlay(
                    RetroSfxId.UiMove,
                    Vector3.zero),
                Is.True);
            Assert.That(
                service.TryPlay(
                    RetroSfxId.UiMove,
                    Vector3.zero),
                Is.False,
                "Immediate repeats must respect the per-effect cooldown.");
            Assert.That(
                service.TryPlay(
                    RetroSfxId.Footstep,
                    new Vector3(2f, 0f, -3f)),
                Is.True);
            Assert.That(
                service.TryPlay(
                    RetroSfxId.Pour,
                    Vector3.zero),
                Is.True);
            Assert.That(
                service.GetComponentsInChildren<AudioSource>(true),
                Has.Length.EqualTo(RetroAudioService.TotalPoolSize));

            yield return null;
            service.StopAll();
        }

        [UnityTest]
        public IEnumerator SceneMusicPlayer_KeepsThemeAndUsesMildToneFilter()
        {
            GameObject musicObject =
                new GameObject("Retro Music Test");
            CityMusicPlayer player =
                musicObject.AddComponent<CityMusicPlayer>();
            yield return null;

            Assert.That(player.Source, Is.Not.Null);
            Assert.That(player.ActiveClip, Is.Not.Null);
            Assert.That(
                player.ActiveClip.name,
                Is.EqualTo(CityMusicPlayer.TrackName));
            Assert.That(player.Source.loop, Is.True);
            Assert.That(player.Source.spatialBlend, Is.Zero);
            Assert.That(player.ToneFilter, Is.Not.Null);
            Assert.That(
                player.ToneFilter.cutoffFrequency,
                Is.EqualTo(15500f).Within(1f));

            Object.Destroy(musicObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SceneAmbience_IsQuietLoopingAndSceneLocal()
        {
            GameObject cityObject =
                new GameObject("City Ambience Test");
            CityAmbiencePlayer city =
                cityObject.AddComponent<CityAmbiencePlayer>();
            GameObject barObject =
                new GameObject("Bar Ambience Test");
            BarAmbiencePlayer bar =
                barObject.AddComponent<BarAmbiencePlayer>();
            GameObject homeObject =
                new GameObject("Home Ambience Test");
            HomeAmbiencePlayer home =
                homeObject.AddComponent<HomeAmbiencePlayer>();
            yield return null;

            AssertAmbience(city, "RetroAmbience_City", 5200f);
            AssertAmbience(bar, "RetroAmbience_Bar", 4300f);
            AssertAmbience(home, "RetroAmbience_Home", 3200f);
            Assert.That(
                city.gameObject.scene,
                Is.EqualTo(bar.gameObject.scene),
                "Both ambience players must stay in their active scene.");
            Assert.That(
                home.gameObject.scene,
                Is.EqualTo(bar.gameObject.scene),
                "Home ambience must remain scene-local.");
            Assert.That(
                home.ActiveClip,
                Is.Not.SameAs(bar.ActiveClip));

            Object.Destroy(cityObject);
            Object.Destroy(barObject);
            Object.Destroy(homeObject);
            yield return null;
        }

        private static void AssertAmbience(
            SceneAmbiencePlayer ambience,
            string expectedClipName,
            float expectedCutoff)
        {
            Assert.That(ambience.Source, Is.Not.Null);
            Assert.That(ambience.ActiveClip, Is.Not.Null);
            Assert.That(
                ambience.ActiveClip.name,
                Is.EqualTo(expectedClipName));
            Assert.That(ambience.Source.loop, Is.True);
            Assert.That(ambience.Source.spatialBlend, Is.Zero);
            Assert.That(ambience.Source.volume, Is.LessThan(0.1f));
            Assert.That(ambience.Source.priority, Is.GreaterThan(128));
            Assert.That(ambience.ToneFilter, Is.Not.Null);
            Assert.That(
                ambience.ToneFilter.cutoffFrequency,
                Is.EqualTo(expectedCutoff).Within(1f));
        }
    }
}
