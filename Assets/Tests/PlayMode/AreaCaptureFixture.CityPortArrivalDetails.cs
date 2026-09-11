using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Arrival horn gain and harbour reflections, unchanged departure, lifecycle and an offline Unity DSP WAV with speakers muted.")]
        public IEnumerator CityPortArrivalHorn()
        {
            float volume = AudioListener.volume;
            AudioListener.volume = 0f;
            try
            {
                yield return CaptureFocusedPort((camera, city, port, crew) => AssertPortArrivalHorn(camera, port));
            }
            finally { AudioListener.volume = volume; }
        }

        [UnityTest]
        [Explicit("The complete production searchlight, visible fog shaft and shadows in ordinary day/night frames.")]
        public IEnumerator CityPortSearchlightAppearance() => CaptureFocusedPort(CapturePortSearchlightAppearance);

        private static IEnumerator CapturePortSearchlightAppearance(Camera camera, CityGameRoot city,
            CityPortController port, CityPortCrew crew)
        {
            double seconds = CityPortCycle.UnloadStartSeconds + 16d;
            Light lamp = CityPortAssetProvider.FindPart(port.Vessel.gameObject, "Port Vessel Searchlight").GetComponent<Light>();
            Renderer beam = CityPortAssetProvider.FindPart(port.Vessel.gameObject, "SearchlightBeam").GetComponent<Renderer>();
            foreach (int hour in new[] { 12, 21 })
            {
                GameSessionState.AdvanceGameTime((float)((hour * 60d - GameSessionState.GameTimeOfDayMinutes) / GameTimeState.GameMinutesPerRealSecond));
                city.DayNight.ApplyCurrentTime(true);
                port.ApplyAt(seconds, 15f);
                crew.ApplyAt(seconds, seconds);
                yield return null;
                Assert.That(lamp.enabled && lamp.gameObject.activeInHierarchy && beam.enabled, Is.True);
                Assert.That(lamp.shadows, Is.EqualTo(LightShadows.Soft));
                var properties = new MaterialPropertyBlock();
                beam.GetPropertyBlock(properties);
                Assert.That(properties.GetFloat("_Intensity"), Is.GreaterThan(.05f));
                yield return CapturePort(camera, city, port, crew, seconds,
                    hour == 12 ? "port-searchlight-gameplay-day" : "port-searchlight-gameplay-night",
                    new Vector3(-17f, 7f, -5f), new Vector3(-2f, 3f, 2f));
            }
        }

        private static IEnumerator ValidatePortHornAndSearchlight(Camera camera, CityGameRoot city, CityPortController port)
        {
            yield return AssertPortArrivalHorn(camera, port);
            Light lamp = CityPortAssetProvider.FindPart(port.Vessel.gameObject, "Port Vessel Searchlight").GetComponent<Light>();
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Assert.That(pipeline, Is.Not.Null);
            Assert.That(pipeline.supportsAdditionalLightShadows, Is.True);
            Assert.That(pipeline.shadowDistance, Is.GreaterThanOrEqualTo(lamp.range));
            Assert.That(lamp.type, Is.EqualTo(LightType.Spot));
            Assert.That(lamp.shadows, Is.EqualTo(LightShadows.Soft));
            Assert.That(lamp.shadowStrength, Is.GreaterThan(.5f));
            float savedNight = CityNightSiteLightRegistry.NightFactor;
            try
            {
                CityNightSiteLightRegistry.SetNightFactor(1f);
                float nightIntensity = lamp.intensity;
                CityNightSiteLightRegistry.SetNightFactor(0f);
                Assert.That(lamp.enabled, Is.True);
                Assert.That(lamp.intensity, Is.GreaterThanOrEqualTo(nightIntensity * GameTimeDayNightRules.DayFixtureFloor - .001f));
            }
            finally { CityNightSiteLightRegistry.SetNightFactor(savedNight); }
            yield return null;

            double savedTime = port.ElapsedSeconds;
            bool force = port.ForcePresentation, auto = port.AutoAdvance;
            Vector3 cameraPosition = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            float fieldOfView = camera.fieldOfView;
            bool fog = RenderSettings.fog;
            AmbientMode ambientMode = RenderSettings.ambientMode;
            Color ambient = RenderSettings.ambientLight;
            var lights = new Dictionary<Light, bool>();
            var casters = new Dictionary<Renderer, ShadowCastingMode>();
            var glows = new Dictionary<Renderer, bool>();
            Transform cargo = port.Cargo[0];
            Pose cargoPose = new Pose(cargo.position, cargo.rotation);
            bool cargoActive = cargo.gameObject.activeSelf;
            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            bool post = cameraData.renderPostProcessing, shadows = cameraData.renderShadows;
            Assert.That(shadows, Is.True, "The gameplay camera must render the real spotlight shadows.");
            LayerMask volumes = cameraData.volumeLayerMask;
            try
            {
                port.AutoAdvance = false; port.ForcePresentation = true;
                port.ApplyAt(CityPortCycle.UnloadStartSeconds, 15f);
                CityNightSiteLightRegistry.SetNightFactor(1f);
                Assert.That(lamp.enabled && lamp.gameObject.activeInHierarchy, Is.True);
                Assert.That(Vector3.Distance(lamp.transform.position,
                    CityPortAssetProvider.FindPart(port.Vessel.gameObject, "ANCHOR_Searchlight").position), Is.LessThan(.001f));
                // Reuse an actual imported fish crate as the sole changed
                // occluder on the authored foredeck. No replacement geometry.
                cargo.gameObject.SetActive(true);
                cargo.SetPositionAndRotation(port.Vessel.TransformPoint(new Vector3(.2f, 1.8f, 6.6f)), port.Vessel.rotation);
                foreach (Renderer renderer in cargo.GetComponentsInChildren<Renderer>(true))
                {
                    casters.Add(renderer, renderer.shadowCastingMode);
                    Assert.That(renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.On));
                }
                foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    lights.Add(light, light.enabled);
                    light.enabled = light == lamp;
                }
                foreach (Renderer renderer in lamp.GetComponentsInChildren<Renderer>(true))
                    glows.Add(renderer, renderer.enabled);
                foreach (string name in new[] { "SearchlightBeam", "SearchlightGlass" })
                {
                    Renderer renderer = CityPortAssetProvider.FindPart(port.Vessel.gameObject, name).GetComponent<Renderer>();
                    glows[renderer] = renderer.enabled;
                }
                foreach (var pair in glows) pair.Key.enabled = false;
                // Remove unrelated illumination and the additive shaft for
                // this differential capture, retaining the production lamp's
                // exact range, intensity, cone and shadow settings.
                RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.035f, .035f, .035f);
                cameraData.renderPostProcessing = false;
                cameraData.renderShadows = true;
                cameraData.volumeLayerMask = 0;
                camera.fieldOfView = 52f;
                camera.transform.SetPositionAndRotation(port.Vessel.TransformPoint(new Vector3(4.8f, 6.3f, 10.2f)),
                    Quaternion.LookRotation(port.Vessel.TransformPoint(new Vector3(.2f, 1.85f, 7.7f)) -
                        port.Vessel.TransformPoint(new Vector3(4.8f, 6.3f, 10.2f)), port.Vessel.up));
                Physics.SyncTransforms();
                ReadPortSearchlightFrame(camera, "port-arrival-searchlight-visible-caster");
                foreach (var pair in casters) pair.Key.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                Color32[] shadowed = ReadPortSearchlightFrame(camera, "port-arrival-searchlight-shadow");
                foreach (var pair in casters) pair.Key.shadowCastingMode = ShadowCastingMode.Off;
                // Off hides the shadow; explicitly hide the mesh too, so both
                // compared frames contain exactly the same visible geometry.
                var casterEnabled = new Dictionary<Renderer, bool>();
                foreach (var pair in casters) { casterEnabled.Add(pair.Key, pair.Key.enabled); pair.Key.enabled = false; }
                Color32[] clear, unlit;
                try
                {
                    clear = ReadPortSearchlightFrame(camera, "port-arrival-searchlight-clear");
                    lamp.enabled = false;
                    unlit = ReadPortSearchlightFrame(camera, "port-arrival-searchlight-off");
                    lamp.enabled = true;
                }
                finally { foreach (var pair in casterEnabled) pair.Key.enabled = pair.Value; }
                int litPixels = 0, shadowPixels = 0, shadowDelta = 0;
                for (int i = 0; i < clear.Length; ++i)
                {
                    int lighting = PortPixelLuma(clear[i]) - PortPixelLuma(unlit[i]);
                    int occlusion = PortPixelLuma(clear[i]) - PortPixelLuma(shadowed[i]);
                    if (lighting > 6) ++litPixels;
                    if (lighting > 6 && occlusion > 6) { ++shadowPixels; shadowDelta += occlusion; }
                }
                Debug.Log($"PORT SEARCHLIGHT: actual surface light pixels={litPixels}, actual occluded pixels={shadowPixels}, shadow luma delta={shadowDelta}");
                Assert.That(litPixels, Is.GreaterThan(150), "The actual spotlight must light opaque surfaces without its additive fog shaft.");
                Assert.That(shadowPixels, Is.GreaterThan(40), "The imported crate must cast a real spotlight shadow onto the unchanged receiving geometry.");
                Assert.That(shadowDelta, Is.GreaterThan(800));
            }
            finally
            {
                foreach (var pair in casters) if (pair.Key != null) pair.Key.shadowCastingMode = pair.Value;
                foreach (var pair in glows) if (pair.Key != null) pair.Key.enabled = pair.Value;
                foreach (var pair in lights) if (pair.Key != null) pair.Key.enabled = pair.Value;
                CityNightSiteLightRegistry.SetNightFactor(savedNight);
                RenderSettings.fog = fog; RenderSettings.ambientMode = ambientMode; RenderSettings.ambientLight = ambient;
                cameraData.renderPostProcessing = post; cameraData.renderShadows = shadows; cameraData.volumeLayerMask = volumes;
                camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation); camera.fieldOfView = fieldOfView;
                cargo.SetPositionAndRotation(cargoPose.position, cargoPose.rotation); cargo.gameObject.SetActive(cargoActive);
                port.ForcePresentation = force; port.AutoAdvance = auto; port.ApplyAt(savedTime, 15f);
            }
        }

        private static IEnumerator AssertPortArrivalHorn(Camera camera, CityPortController port)
        {
            double saved = port.ElapsedSeconds;
            bool force = port.ForcePresentation;
            Transform observer = port.PresentationObserver;
            // A short-lived instance exercises the production component without
            // consuming the live scene's one-time arrival/departure windows.
            CityPortSound sound = CityPortSound.Build(port.transform, port, 7301);
            sound.enabled = false;
            var farObserver = new GameObject("Port horn distance probe");
            try
            {
                AudioSource horn = sound.HornSource;
                Assert.That(horn.clip.channels, Is.EqualTo(1));
                Assert.That(horn.clip.length, Is.EqualTo(CityOffshoreBoatSynthesis.FirstHornDuration + CityPortSound.HornTailSeconds).Within(.002f));
                Assert.That(horn.loop || horn.playOnAwake, Is.False);
                Assert.That(horn.spatialBlend, Is.EqualTo(1f));
                Assert.That(horn.outputAudioMixerGroup, Is.SameAs(GameAudioMixer.SfxWorldGroup));
                Assert.That(horn.GetComponent<AudioLowPassFilter>().cutoffFrequency, Is.EqualTo(1250f));
                AudioEchoFilter echo = horn.GetComponent<AudioEchoFilter>();
                AudioReverbFilter reverb = horn.GetComponent<AudioReverbFilter>();
                Assert.That(echo.dryMix, Is.EqualTo(1f));
                port.ForcePresentation = true;
                double cycle = (CityPortCycle.Sample(saved).CycleIndex + 1) * CityPortCycle.CycleDurationSeconds;
                void Sample(double time, bool running = true)
                {
                    port.ApplyAt(time, 15f); sound.Advance(time, .04f, running);
                }
                double arrive = cycle + CityPortSound.ArrivalHornAtSeconds;
                Sample(arrive - .02d); Sample(arrive + .02d);
                Assert.That(sound.HornsPlayed, Is.EqualTo(1));
                float arrivalGain = horn.volume, arrivalDecay = reverb.decayTime;
                float arrivalEchoDecay = echo.decayRatio, arrivalWet = echo.wetMix;
                Assert.That(arrivalGain, Is.EqualTo(.64f).Within(.00001f));
                Assert.That(horn.clip.length, Is.EqualTo(CityOffshoreBoatSynthesis.FirstHornDuration +
                    CityPortSound.ArrivalHornTailSeconds).Within(.002f));
                Assert.That(Vector3.Distance(horn.transform.position,
                    CityPortAssetProvider.FindPart(port.Vessel.gameObject, "ANCHOR_Horn").position), Is.LessThan(.001f));
                Sample(arrive + .02d, false);
                Assert.That(sound.IsPaused, Is.True); Assert.That(horn.isPlaying, Is.False);
                Assert.That(echo.enabled || reverb.enabled, Is.False, "A paused blast cannot leave detached DSP echoes playing.");
                Sample(arrive + .02d);
                Assert.That(sound.HornsPlayed, Is.EqualTo(1));
                Assert.That(echo.enabled && reverb.enabled, Is.True);
                Sample(arrive - .02d); Sample(arrive + .02d);
                Assert.That(sound.HornsPlayed, Is.EqualTo(1), "A rewind must not replay the consumed arrival.");
                double depart = cycle + CityPortSound.DepartureHornAtSeconds;
                Sample(depart - .02d); Sample(depart + .02d);
                Assert.That(sound.HornsPlayed, Is.EqualTo(2));
                Assert.That(horn.volume, Is.EqualTo(.32f).Within(.00001f), "Departure keeps its existing gain.");
                Assert.That(arrivalGain / horn.volume, Is.EqualTo(2f).Within(.001f));
                Assert.That(horn.clip.length, Is.EqualTo(CityOffshoreBoatSynthesis.FirstHornDuration +
                    CityPortSound.HornTailSeconds).Within(.002f), "Departure keeps its original reflection-tail clip.");
                Assert.That(echo.delay, Is.EqualTo(620f));
                Assert.That(echo.decayRatio, Is.EqualTo(.42f));
                Assert.That(echo.wetMix, Is.EqualTo(.30f));
                Assert.That(reverb.decayTime, Is.EqualTo(3.8f));
                Assert.That(arrivalDecay, Is.GreaterThan(reverb.decayTime));
                Assert.That(arrivalEchoDecay, Is.GreaterThan(echo.decayRatio));
                Assert.That(arrivalWet, Is.GreaterThan(echo.wetMix));
                port.ForcePresentation = false;
                farObserver.transform.position = port.Plan.Origin + Vector3.one * 5000f;
                port.PresentationObserver = farObserver.transform;
                Sample(arrive + CityPortCycle.CycleDurationSeconds - .02d);
                Sample(arrive + CityPortCycle.CycleDurationSeconds + .02d);
                Assert.That(horn.isPlaying, Is.False);
                Assert.That(echo.enabled || reverb.enabled, Is.False, "Leaving the port must also stop the wet tail.");
                port.ForcePresentation = true;
                Sample(arrive + CityPortCycle.CycleDurationSeconds + .04d);
                Assert.That(sound.HornsPlayed, Is.EqualTo(2), "Returning must not replay the missed signal.");
                Sample(depart + CityPortCycle.CycleDurationSeconds + 1d);
                Sample(depart + CityPortCycle.CycleDurationSeconds + 1.02d);
                Assert.That(sound.HornsPlayed, Is.EqualTo(2), "A seek past departure must remain silent.");
                Sample(arrive + CityPortCycle.CycleDurationSeconds * 2d - .02d);
                Sample(arrive + CityPortCycle.CycleDurationSeconds * 2d + .02d);
                Assert.That(sound.HornsPlayed, Is.EqualTo(3), "A fresh arrival still triggers after the skipped windows.");
                Assert.That(horn.volume, Is.EqualTo(arrivalGain));
                Assert.That(reverb.decayTime, Is.EqualTo(arrivalDecay));
                Assert.That(horn.clip.length, Is.EqualTo(CityOffshoreBoatSynthesis.FirstHornDuration +
                    CityPortSound.ArrivalHornTailSeconds).Within(.002f));
                yield return CapturePortHornTail(camera, horn);
            }
            finally
            {
                Object.DestroyImmediate(sound.gameObject); Object.DestroyImmediate(farObserver);
                port.ForcePresentation = force; port.PresentationObserver = observer; port.ApplyAt(saved, 15f);
            }
        }

        private static IEnumerator CapturePortHornTail(Camera camera, AudioSource horn)
        {
            Assert.That(AudioListener.volume, Is.Zero, "The horn proof must remain inaudible on the user's speakers.");
            float captureDelta = Time.captureDeltaTime, volume = AudioListener.volume;
            bool listenerPause = AudioListener.pause, capturing = false;
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            bool followEnabled = follow != null && follow.enabled;
            Pose pose = new Pose(camera.transform.position, camera.transform.rotation);
            var muted = new Dictionary<AudioSource, bool>();
            AudioEchoFilter echo = horn.GetComponent<AudioEchoFilter>();
            AudioReverbFilter reverb = horn.GetComponent<AudioReverbFilter>();
            bool echoEnabled = echo.enabled, reverbEnabled = reverb.enabled;
            float arrivalGain = horn.volume;
            try
            {
                if (follow != null) follow.enabled = false;
                camera.transform.SetPositionAndRotation(horn.transform.position + new Vector3(7f, 1f, 0),
                    Quaternion.LookRotation(new Vector3(-7f, -1f, 0)));
                horn.Stop();
                AudioListener.pause = false;
                Time.captureDeltaTime = 1f / 30f;
                int rate = AudioSettings.outputSampleRate;
                Assert.That(AudioRenderer.Start(), Is.True);
                capturing = true;
                AudioListener.volume = 1f;
                yield return PumpProductionAudio(rate / 5, null, muted, System.Array.Empty<AudioSource>(), null, 30, false);
                AudioListener.volume = 0f;
                AudioRenderer.Stop(); capturing = false;
                yield return null;
                string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "CityCannery");
                Directory.CreateDirectory(folder);
                double referenceRms = 0d;
                for (int take = 0; take < 3; take++)
                {
                    // Same dry waveform, distance and mixer path isolate the
                    // requested gain from the deliberately stronger reflections.
                    bool wet = take == 2;
                    echo.enabled = reverb.enabled = wet;
                    horn.volume = take == 0 ? .32f : arrivalGain;
                    horn.Stop();
                    horn.timeSamples = 0;
                    Assert.That(AudioRenderer.Start(), Is.True);
                    capturing = true;
                    AudioListener.volume = 1f;
                    horn.Play();
                    float seconds = wet ? horn.clip.length : 2.6f;
                    var pcm = new List<float>();
                    yield return PumpProductionAudio(Mathf.CeilToInt(rate * 2f * seconds), pcm, muted, new[] { horn }, null,
                        Mathf.CeilToInt(seconds * 30f) + 60);
                    AudioListener.volume = 0f;
                    AudioRenderer.Stop(); capturing = false;
                    horn.Stop();
                    float[] samples = pcm.ToArray();
                    ProductionAudioRms(samples); // Entire recording must be finite and unclipped.
                    double body = ProductionAudioRms(samples, -1, rate * 2, rate * 4);
                    Assert.That(body, Is.GreaterThan(.0002d));
                    string name = take == 0 ? "port-horn-dry-reference.wav" : take == 1
                        ? "port-horn-dry-arrival.wav" : "port-arrival-horn-reverb-echo.wav";
                    WritePcmWave(Path.Combine(folder, name), samples, rate, 2, 1f);
                    if (take == 0) referenceRms = body;
                    else if (take == 1)
                    {
                        double ratio = body / referenceRms;
                        Debug.Log($"PORT ARRIVAL DRY GAIN: reference RMS={referenceRms:F7}, arrival RMS={body:F7}, ratio={ratio:F3}");
                        Assert.That(ratio, Is.InRange(1.8d, 2.2d), "Actual Unity DSP dry output approximately doubles without normalizing the WAV.");
                    }
                    else
                    {
                        // Input is exactly silent after 3.4 s. The long late
                        // decay therefore proves the real echo/reverb path.
                        double tail = ProductionAudioRms(samples, -1, rate * 12, rate * 16);
                        Debug.Log($"PORT ARRIVAL WET DSP: body RMS={body:F7}, zero-input tail RMS(6–8s)={tail:F7}");
                        Assert.That(tail, Is.GreaterThan(.00002d));
                        Assert.That(tail, Is.GreaterThan(body * .004d), "The stronger arrival has an audible extended reflection tail.");
                    }
                    yield return null;
                }
                // This assertion is independent of the wet recording: the
                // source itself cannot be responsible for its measured tail.
                var source = new float[horn.clip.samples];
                horn.clip.GetData(source, 0);
                float sourceTailPeak = 0f;
                for (int i = Mathf.CeilToInt(CityOffshoreBoatSynthesis.FirstHornDuration * horn.clip.frequency); i < source.Length; ++i)
                    sourceTailPeak = Mathf.Max(sourceTailPeak, Mathf.Abs(source[i]));
                Assert.That(sourceTailPeak, Is.Zero);
            }
            finally
            {
                AudioListener.volume = 0f;
                if (capturing) AudioRenderer.Stop();
                horn.Stop();
                horn.volume = arrivalGain;
                echo.enabled = echoEnabled;
                reverb.enabled = reverbEnabled;
                foreach (var pair in muted) if (pair.Key != null) pair.Key.mute = pair.Value;
                AudioListener.pause = listenerPause; AudioListener.volume = volume;
                Time.captureDeltaTime = captureDelta;
                camera.transform.SetPositionAndRotation(pose.position, pose.rotation);
                if (follow != null) follow.enabled = followEnabled;
            }
        }

        private static int PortPixelLuma(Color32 pixel) => pixel.r + pixel.g + pixel.b;

        private static Color32[] ReadPortSearchlightFrame(Camera camera, string name)
        {
            var target = new RenderTexture(640, 360, 24);
            var frame = new Texture2D(640, 360, TextureFormat.RGB24, false);
            RenderTexture oldTarget = camera.targetTexture, oldActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                frame.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); frame.Apply();
                string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "CityCannery");
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), frame.EncodeToPNG());
                return frame.GetPixels32();
            }
            finally
            {
                camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
                Object.DestroyImmediate(frame); target.Release(); Object.DestroyImmediate(target);
            }
        }
    }
}
