using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class LastRouteRadioMusicPlayerPlayModeTests
    {
        [UnityTest]
        public IEnumerator Radio_ThreeStationsKeepTheirOwnTracksAndPlayheads()
        {
            GameSessionState.BeginNewGame();
            MusicMix.ClearFadeOuts();
            var host = new GameObject("Three radio stations contract");
            Child(host, "Listener").AddComponent<AudioListener>();
            var resources = new AudioClip[3];
            var probes = new AudioClip[3];
            var remembered = new float[3];
            try
            {
                for (int station = 0; station < 3; station++)
                {
                    string path = LastRouteRadioMusicPlayer.ResourcePathForStation(station);
                    Assert.That(path, Is.EqualTo(
                        "Audio/LastRouteRadio/Station" + (station + 1) + "/radio_theme"));
                    string folder = "Audio/LastRouteRadio/Station" + (station + 1);
                    AudioClip[] stationClips = Resources.LoadAll<AudioClip>(folder);
                    System.Array.Sort(stationClips,
                        (left, right) => string.CompareOrdinal(left.name, right.name));
                    AudioClip preferred = Resources.Load<AudioClip>(path);
                    AudioClip expected = preferred != null ? preferred :
                        stationClips.Length > 0 ? stationClips[0] : null;
                    resources[station] = LastRouteRadioMusicPlayer.LoadStationClip(station);
                    Assert.That(resources[station], Is.SameAs(expected),
                        "Use radio_theme when present, otherwise the first clip name in this station.");
                    if (resources[station] != null)
                        CollectionAssert.Contains(stationClips, resources[station],
                            "A station's fallback may not borrow another station's music.");
                    // These stand in only for slots the user has not filled yet.
                    // Equal names deliberately expose playheads keyed by clip name.
                    if (resources[station] == null)
                        probes[station] = AudioClip.Create("radio_theme", 64000, 1, 8000, false);
                }
                Assert.That(resources[0], Is.Not.Null,
                    "The supplied music file belongs to Station 1.");

                GameSessionState.SetCarDashboard(
                    GameSessionState.CarDashboard.WithRadioOn(true));
                var city = Child(host, "City").AddComponent<CityMusicPlayer>();
                yield return WaitForTheme(city);
                LastRouteCarAssetRegistry car = LastRouteCarFactory.Create(host.transform,
                    LastRouteCarPlan.At(Vector3.zero, Vector3.forward));
                Assert.That(car, Is.Not.Null);
                var dashboard = car.GetComponentInParent<LastRouteCarDashboard>();
                var carAudio = car.GetComponentInParent<LastRouteCarAudio>();
                var radio = car.RadioDialRenderer.GetComponentInChildren<LastRouteRadioMusicPlayer>();
                Assert.That(dashboard, Is.Not.Null);
                Assert.That(carAudio, Is.Not.Null);
                Assert.That(radio, Is.Not.Null);
                AudioSource tuning = carAudio.RadioTuningSource;
                Assert.That(tuning, Is.Not.Null);
                Assert.That(tuning.spatialBlend, Is.EqualTo(1f));
                Assert.That(tuning.loop, Is.False);
                Assert.That(Vector3.Distance(tuning.transform.position,
                    car.RadioDialRenderer.bounds.center), Is.LessThan(0.001f));
                Assert.That(tuning.GetComponent<AudioHighPassFilter>().cutoffFrequency,
                    Is.EqualTo(180f).Within(0.01f));
                Assert.That(tuning.GetComponent<AudioLowPassFilter>().cutoffFrequency,
                    Is.EqualTo(3500f).Within(0.01f));
                Assert.That(tuning.clip.length,
                    Is.EqualTo(LastRouteCarSoundSynthesis.RadioTuningClipSeconds).Within(0.001f));
                var tuningSamples = new float[tuning.clip.samples];
                Assert.That(tuning.clip.GetData(tuningSamples, 0), Is.True);
                double tuningEnergy = 0d;
                int fromSample = Mathf.RoundToInt(tuning.clip.frequency * 0.10f);
                int toSample = Mathf.RoundToInt(tuning.clip.frequency * 0.24f);
                for (int sample = fromSample; sample < toSample; sample++)
                    tuningEnergy += tuningSamples[sample] * tuningSamples[sample];
                Assert.That(System.Math.Sqrt(tuningEnergy / (toSample - fromSample)),
                    Is.GreaterThan(0.015d),
                    "Tuning has audible radio static after the initial mechanical click.");

                for (int station = 0; station < 3; station++)
                {
                    Assert.That(radio.StationIndex, Is.EqualTo(station));
                    Assert.That(radio.ActiveClip, Is.SameAs(resources[station]),
                        "A station must resolve its own folder immediately.");
                    yield return WaitForStationReady(radio);
                    Assert.That(city.IsPlaybackSuppressed, Is.True);
                    Assert.That(city.Source.isPlaying, Is.False);
                    if (resources[station] == null)
                    {
                        Assert.That(radio.Source.isPlaying, Is.False,
                            "An empty station must not retain another station's track.");
                        radio.Source.clip = probes[station];
                    }
                    radio.ResumeWithFadeIn(0f);
                    Assert.That(radio.Source.isPlaying, Is.True);
                    float marker = Mathf.Min(0.45f + station * 0.9f,
                        radio.ActiveClip.length * 0.55f);
                    radio.Source.timeSamples = Mathf.RoundToInt(marker * radio.ActiveClip.frequency);
                    yield return null;
                    remembered[station] =
                        LastRouteRadioMusicPlayer.SavedPlaybackSecondsForStation(station);
                    Assert.That(remembered[station], Is.GreaterThan(0f));

                    dashboard.Operate(LastRouteCarDashboardTarget.RadioTuning);
                    Assert.That(radio.StationIndex, Is.EqualTo((station + 1) % 3));
                    Assert.That(carAudio.RadioTuningCueCount, Is.EqualTo(station + 1));
                    Assert.That(tuning.isPlaying, Is.True);
                }
                Assert.That(radio.ActiveClip, Is.SameAs(resources[0]),
                    "Tuning after Station 3 returns to Station 1, not an empty fourth slot.");

                Object.Destroy(radio.gameObject);
                yield return null;
                MusicMix.ClearFadeOuts();
                radio = Child(host, "Replacement radio").AddComponent<LastRouteRadioMusicPlayer>();
                for (int station = 0; station < 3; station++)
                {
                    if (station > 0)
                        dashboard.Operate(LastRouteCarDashboardTarget.RadioTuning);
                    Assert.That(radio.ActiveClip, Is.SameAs(resources[station]));
                    yield return WaitForStationReady(radio);
                    if (resources[station] == null)
                        radio.Source.clip = probes[station];
                    radio.ResumeWithFadeIn(0f);
                    Assert.That(radio.Source.timeSamples / (float)radio.ActiveClip.frequency,
                        Is.EqualTo(remembered[station]).Within(0.2f),
                        "Replacement Station " + (station + 1) + " must resume its own playhead.");
                }

                int poweredStation = radio.StationIndex;
                float positionBeforeOff = radio.Source.timeSamples / (float)radio.ActiveClip.frequency;
                dashboard.Operate(LastRouteCarDashboardTarget.RadioPower);
                AssertRadioPowerIsSilent(radio);
                Assert.That(radio.StationIndex, Is.EqualTo(poweredStation));
                Assert.That(GameSessionState.CarDashboard.TuningDetent, Is.EqualTo(poweredStation));
                Assert.That(LastRouteRadioMusicPlayer.SavedPlaybackSecondsForStation(poweredStation),
                    Is.EqualTo(positionBeforeOff).Within(0.1f), "Power-off remembers the selected station immediately.");
                dashboard.Operate(LastRouteCarDashboardTarget.RadioPower);
                city.AdvanceFade(MusicMix.FadeOutSeconds);
                radio.ResumeWithFadeIn(0f);
                Assert.That(radio.Source.isPlaying, Is.True);
                Assert.That(radio.Source.timeSamples / (float)radio.ActiveClip.frequency,
                    Is.EqualTo(positionBeforeOff).Within(0.1f), "Power-on resumes this station where its switch stopped it.");
                dashboard.Operate(LastRouteCarDashboardTarget.RadioPower);
                AssertRadioPowerIsSilent(radio);
                int tuningCuesBeforeOff = carAudio.RadioTuningCueCount;
                int detentsBeforeOff = carAudio.KnobDetentCueCount;
                Assert.That(tuning.isPlaying, Is.False);
                for (int station = 0; station < 3; station++)
                {
                    dashboard.Operate(LastRouteCarDashboardTarget.RadioTuning);
                    yield return WaitForStationReady(radio);
                    radio.ResumeWithFadeIn(0f);
                    Assert.That(radio.IsPlaybackSuppressed, Is.True);
                    Assert.That(radio.Source.isPlaying, Is.False,
                        "Changing station with the power off must never start its track.");
                    Assert.That(carAudio.RadioTuningCueCount, Is.EqualTo(tuningCuesBeforeOff));
                    Assert.That(carAudio.KnobDetentCueCount, Is.EqualTo(detentsBeforeOff));
                    Assert.That(tuning.isPlaying, Is.False);
                }

                GameSessionState.BeginNewGame();
                yield return null;
                for (int station = 0; station < 3; station++)
                    Assert.That(LastRouteRadioMusicPlayer.SavedPlaybackSecondsForStation(station),
                        Is.Zero, "A new session clears every station, including nonselected slots.");
            }
            finally
            {
                MusicMix.ClearFadeOuts();
                Object.DestroyImmediate(host);
                foreach (AudioClip probe in probes)
                    if (probe != null)
                        Object.DestroyImmediate(probe);
                GameSessionState.BeginNewGame();
            }
        }

        [UnityTest]
        public IEnumerator Radio_HoldsCityAcrossReplacementAndLocationChanges()
        {
            GameSessionState.BeginNewGame();
            MusicMix.ClearFadeOuts();
            var host = new GameObject("Radio handover contract");
            Child(host, "Listener").AddComponent<AudioListener>();
            AudioClip tape = AudioClip.Create("Test radio tape", 32000, 2, 8000, false);
            LastRouteRadioMusicPlayer radio = null;
            LastRouteRadioMusicPlayer detachedRadio = null;
            try
            {
                CityMusicPlayer city = Child(host, "City").AddComponent<CityMusicPlayer>();
                yield return WaitForTheme(city);
                city.AdvanceFade(MusicMix.FadeInSeconds);
                radio = CreateRadio(host, tape);
                Assert.That(radio.IsPlaybackSuppressed, Is.True);
                radio.ResumeWithFadeIn(0f);
                Assert.That(radio.Source.isPlaying, Is.False);

                GameSessionState.SetCarDashboard(
                    GameSessionState.CarDashboard.WithRadioOn(true));
                Assert.That(city.IsPlaybackSuppressed, Is.True);
                Assert.That(city.PlaybackState, Is.EqualTo(SceneMusicPlaybackState.FadingOut));
                Assert.That(radio.Source.isPlaying, Is.False,
                    "The tape must wait for the city score's entire outgoing tail.");
                city.AdvanceFade(MusicMix.FadeOutSeconds);
                yield return null;
                radio.AdvanceFade(MusicMix.FadeInSeconds);
                Assert.That(city.Source.isPlaying, Is.False);
                Assert.That(radio.Source.isPlaying, Is.True);
                Assert.That(radio.Source.loop, Is.True);
                Assert.That(radio.Source.spatialBlend, Is.EqualTo(1f));
                Assert.That(radio.ToneFilter.cutoffFrequency, Is.EqualTo(3500f).Within(0.01f));
                Assert.That(radio.GetComponent<AudioHighPassFilter>().cutoffFrequency,
                    Is.EqualTo(180f).Within(0.01f));
                var texture = radio.GetComponent<LastRouteRadioSpeakerTexture>();
                float[] stereo = { 0.2f, 0.6f, -0.8f, 0.2f };
                texture.SetHissGain(0f);
                texture.Process(stereo, 2);
                Assert.That(stereo[0], Is.EqualTo(0.4f).Within(0.0001f));
                Assert.That(stereo[1], Is.EqualTo(stereo[0]));
                Assert.That(stereo[2], Is.EqualTo(-0.3f).Within(0.0001f));
                Assert.That(stereo[3], Is.EqualTo(stereo[2]));

                radio.Source.timeSamples = tape.samples - 800;
                yield return new WaitForSecondsRealtime(0.2f);
                Assert.That(radio.Source.isPlaying, Is.True);
                Assert.That(radio.Source.timeSamples, Is.LessThan(4000),
                    "Playback must wrap at the end instead of stopping.");

                // A location director must remember its desired theme even
                // though the radio currently prevents that theme from playing.
                var hero = Child(host, "Hero");
                var place = Child(host, "Place").AddComponent<BarMusicPlayer>();
                yield return WaitForTheme(place);
                var director = Child(host, "Director").AddComponent<CityLocationMusicDirector>();
                var grounds = new Rect(10f, 10f, 20f, 20f);
                director.Initialize(hero.transform, city,
                    new[] { new CityLocationMusicSlot("test-place", grounds, place) });
                director.enabled = false;
                hero.transform.position = new Vector3(15f, 0f, 15f);
                director.RefreshLocation();
                city.AdvanceFade(MusicMix.FadeOutSeconds);
                place.AdvanceFade(MusicMix.FadeInSeconds);

                GameSessionState.SetCarDashboard(
                    GameSessionState.CarDashboard.WithRadioOn(false));
                AssertRadioPowerIsSilent(radio);
                Assert.That(city.Source.isPlaying, Is.False,
                    "Turning the radio off inside another music zone must not revive City.");
                hero.transform.position = Vector3.zero;
                director.RefreshLocation();
                // The power-off above paid the deferred load; the return to
                // the street is the first resume of a streaming clip.
                yield return WaitForTheme(city);
                place.AdvanceFade(MusicMix.FadeOutSeconds);
                yield return null;
                city.AdvanceFade(MusicMix.FadeInSeconds);
                Assert.That(city.Source.isPlaying, Is.True);

                GameSessionState.SetCarDashboard(
                    GameSessionState.CarDashboard.WithRadioOn(true));
                city.AdvanceFade(MusicMix.FadeOutSeconds);
                yield return null;
                radio.AdvanceFade(MusicMix.FadeInSeconds);
                radio.Source.timeSamples = 10000;
                yield return null;
                float remembered = LastRouteRadioMusicPlayer.SavedPlaybackSeconds;
                Assert.That(remembered, Is.GreaterThan(1f));

                // Simulate the old car leaving and the next scene constructing
                // a new city player and car from the same session switch.
                Object.Destroy(radio.gameObject);
                Object.Destroy(city.gameObject);
                Object.Destroy(director.gameObject);
                Object.Destroy(place.gameObject);
                yield return null;
                remembered = LastRouteRadioMusicPlayer.SavedPlaybackSeconds;
                MusicMix.ClearFadeOuts();
                city = Child(host, "Replacement City").AddComponent<CityMusicPlayer>();
                Assert.That(city.IsPlaybackSuppressed, Is.True);
                Assert.That(city.Source.isPlaying, Is.False,
                    "No Play is permitted during Awake of a radio-on city arrival.");
                yield return WaitForTheme(city);
                city.ResumeWithFadeIn(0f);
                Assert.That(city.Source.isPlaying, Is.False,
                    "Later resume requests must not bypass the stored switch.");
                radio = CreateRadio(host, tape);
                radio.ResumeWithFadeIn(0f);
                Assert.That(radio.Source.isPlaying, Is.True);
                Assert.That(radio.Source.timeSamples / (float)tape.frequency,
                    Is.EqualTo(remembered).Within(0.1f));

                GameSessionState.SetCarDashboard(
                    GameSessionState.CarDashboard.WithRadioOn(false));
                AssertRadioPowerIsSilent(radio);
                // The power-off is what pays the city theme's deferred load.
                yield return WaitForTheme(city);
                Assert.That(city.IsFadeInDeferred, Is.False, "Power-off leaves no radio tail for City to wait on.");
                Assert.That(city.Source.isPlaying, Is.True);
                Assert.That(city.PlaybackState, Is.EqualTo(SceneMusicPlaybackState.FadingIn));

                // Scene exit still has its normal tail while power is on.
                // A switch-off during that tail must cut both old and new cars.
                city.AdvanceFade(MusicMix.FadeInSeconds);
                GameSessionState.SetCarDashboard(GameSessionState.CarDashboard.WithRadioOn(true));
                city.AdvanceFade(MusicMix.FadeOutSeconds);
                yield return null;
                radio.ResumeWithFadeIn(0f);
                radio.Source.timeSamples = 12000;
                Assert.That(radio.RequestSceneExitFade(MusicMix.FadeOutSeconds), Is.True);
                Assert.That(radio.IsDetachedForSceneExit, Is.True);
                Assert.That(radio.Source.isPlaying, Is.True);
                detachedRadio = radio;
                radio = CreateRadio(host, tape);
                radio.ResumeWithFadeIn(0f);
                Assert.That(radio.IsFadeInDeferred, Is.True);
                Assert.That(radio.Source.isPlaying, Is.False);
                float tailPosition = detachedRadio.Source.timeSamples / (float)tape.frequency;
                GameSessionState.SetCarDashboard(GameSessionState.CarDashboard.WithRadioOn(false));
                AssertRadioPowerIsSilent(detachedRadio);
                AssertRadioPowerIsSilent(radio);
                Assert.That(detachedRadio.IsSceneExitFadeComplete, Is.True);
                Assert.That(MusicMix.IsFadeOutActive, Is.False);
                Assert.That(city.Source.isPlaying, Is.True);
                Assert.That(LastRouteRadioMusicPlayer.SavedPlaybackSeconds,
                    Is.EqualTo(tailPosition).Within(0.1f));
                yield return null;
                Assert.That(detachedRadio == null, Is.True, "The silenced detached carrier is released.");
                GameSessionState.SetCarDashboard(GameSessionState.CarDashboard.WithRadioOn(true));
                city.AdvanceFade(MusicMix.FadeOutSeconds);
                radio.ResumeWithFadeIn(0f);
                Assert.That(radio.Source.isPlaying, Is.True);
                Assert.That(radio.Source.timeSamples / (float)tape.frequency,
                    Is.EqualTo(tailPosition).Within(0.1f), "The waiting car inherits the cut tail's final playhead.");
                GameSessionState.SetCarDashboard(GameSessionState.CarDashboard.WithRadioOn(false));
                AssertRadioPowerIsSilent(radio);

                GameSessionState.BeginNewGame();
                yield return null;
                Assert.That(LastRouteRadioMusicPlayer.SavedPlaybackSeconds, Is.Zero,
                    "An old paused car may not restore the previous session's tape position.");
            }
            finally
            {
                MusicMix.ClearFadeOuts();
                if (detachedRadio != null) Object.DestroyImmediate(detachedRadio.gameObject);
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(tape);
                GameSessionState.BeginNewGame();
            }
        }

        private static void AssertRadioPowerIsSilent(LastRouteRadioMusicPlayer radio)
        {
            // No frame, timer or AdvanceFade may pass between the switch and these checks.
            Assert.That(radio.Source.isPlaying, Is.False, "The power switch pauses/stops music in the same call.");
            Assert.That(radio.Source.volume, Is.Zero);
            Assert.That(radio.NormalizedGain, Is.Zero);
            Assert.That(radio.IsFadeActive, Is.False, "Power-off cannot leave an audible fading tail.");
        }

        private static GameObject Child(GameObject parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        private static LastRouteRadioMusicPlayer CreateRadio(GameObject host, AudioClip tape)
        {
            GameObject owner = Child(host, "Radio");
            owner.SetActive(false);
            owner.AddComponent<AudioSource>().playOnAwake = false;
            var radio = owner.AddComponent<LastRouteRadioMusicPlayer>();
            owner.SetActive(true);
            radio.Source.Stop();
            radio.Source.clip = tape;
            return radio;
        }

        private static IEnumerator WaitForTheme(SceneMusicPlayer player)
        {
            for (int frame = 0; frame < 300 &&
                 player.PlaybackState == SceneMusicPlaybackState.Loading; frame++)
                yield return null;
            if (player.IsPlaybackSuppressed)
            {
                // A theme born suppressed defers its load until the switch
                // releases it: nothing to wait for, and no clip to own yet.
                Assert.That(player.ActiveClip, Is.Null);
                Assert.That(player.PlaybackState, Is.EqualTo(SceneMusicPlaybackState.Unavailable));
                yield break;
            }
            Assert.That(player.ActiveClip, Is.Not.Null);
            Assert.That(player.PlaybackState, Is.Not.EqualTo(SceneMusicPlaybackState.Loading));
        }

        private static IEnumerator WaitForStationReady(LastRouteRadioMusicPlayer player)
        {
            for (int frame = 0; frame < 300 &&
                 player.PlaybackState == SceneMusicPlaybackState.Loading; frame++)
                yield return null;
            Assert.That(player.PlaybackState, Is.Not.EqualTo(SceneMusicPlaybackState.Loading));
        }
    }
}
