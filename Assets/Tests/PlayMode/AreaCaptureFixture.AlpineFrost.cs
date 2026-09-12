using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BarPromenade.Rendering;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Same-frame frost diffusion A/B on the actual house camera, without the journey/audio regression.")]
        public IEnumerator AlpineFrostDiffusion()
        {
            bool previous43 = GraphicsEffectsSettings.AspectRatio43Enabled;
            float? previousFilmWeight = BegottenModeRamp.DebugWeightOverride;
            bool previousDiffusion = AlpineColdFrostPass.DebugDisableDiffusion;
            float previousCaptureDelta = Time.captureDeltaTime;
            AlpineColdExposureDriver driver = null;
            Camera camera = null;
            float previousAspect = 0f;
            IDisposable pause = null;
            try
            {
                GameSessionState.BeginNewGame();
                GameSessionState.TryStartGameTimeFromWake();
                GraphicsEffectsSettings.AspectRatio43Enabled = false;
                BegottenModeRamp.DebugWeightOverride = 0f;
                AlpineColdFrostPass.DebugDisableDiffusion = false;
                Time.captureDeltaTime = 1f / 60f;
                yield return SceneManager.LoadSceneAsync(SceneIds.MothersHouseInterior, LoadSceneMode.Single);
                yield return WaitForFrostRoot(true);
                driver = Object.FindAnyObjectByType<AlpineColdExposureDriver>();
                Assert.That(driver, Is.Not.Null);
                driver.enabled = false;
                driver.Model.Reset();
                camera = Camera.main;
                previousAspect = camera.aspect;
                camera.aspect = 16f / 9f;
                pause = GameTimeScaleRuntime.AcquirePause();
                int heldFrame = Time.frameCount;
                Color32[] clear = ReadFrostPixels(camera, "diffusion-00-clear");
                Color32[] clearBaseline = ReadFrostPixels(camera);

                // All these renders happen without a yielded frame: the room,
                // light, camera and crystal pattern are the same A/B subject.
                driver.Model.Step(AlpineColdExposureModel.FullExposureSeconds, false);
                AlpineColdFrostPass.DebugDisableDiffusion = true;
                Color32[] crystalsOnly = ReadFrostPixels(camera, "diffusion-03-full-crystals-only");
                AlpineColdFrostPass.DebugDisableDiffusion = false;
                Color32[] diffused = ReadFrostPixels(camera, "diffusion-04-full-blurred");
                WriteFrostDiffusionComparison(crystalsOnly, diffused);

                float[] times = { 0.3263518f, 0.5f };
                string[] names = { "diffusion-01-25pct", "diffusion-02-50pct" };
                var growthFrames = new Color32[times.Length][];
                var blurStrength = new double[3];
                for (int stage = 0; stage < times.Length; stage++)
                {
                    driver.Model.Reset();
                    driver.Model.Step(Mathf.Lerp(AlpineColdExposureModel.FrostDelaySeconds,
                        AlpineColdExposureModel.FullExposureSeconds, times[stage]), false);
                    AlpineColdFrostPass.DebugDisableDiffusion = true;
                    Color32[] stageCrystals = ReadFrostPixels(camera, names[stage] + "-crystals-only");
                    AlpineColdFrostPass.DebugDisableDiffusion = false;
                    growthFrames[stage] = ReadFrostPixels(camera, names[stage]);
                    blurStrength[stage] = MeasureFrostDiffusionStrength(stageCrystals, growthFrames[stage]);
                }
                blurStrength[2] = MeasureFrostDiffusionStrength(crystalsOnly, diffused);
                TestContext.Out.WriteLine($"Blur-only edge difference at 25/50/100%: " +
                    $"{blurStrength[0]:F5} / {blurStrength[1]:F5} / {blurStrength[2]:F5} (0–255).");
                Assert.That(blurStrength[0], Is.GreaterThan(0d));
                Assert.That(blurStrength[1], Is.GreaterThan(blurStrength[0]));
                Assert.That(blurStrength[2], Is.GreaterThan(blurStrength[1]),
                    "Maximum diffusion must be reached only at full frost coverage.");

                driver.Model.Reset();
                driver.Model.Step(AlpineColdExposureModel.FullExposureSeconds, false);
                driver.Model.Step(AlpineColdExposureModel.FullThawSeconds * 0.5f, true);
                Assert.That(driver.Model.FrostAmount, Is.EqualTo(0.5f));
                Color32[] halfThawed = ReadFrostPixels(camera, "diffusion-06-half-thawed");
                Assert.That(CountFrostChangedPixels(growthFrames[1], halfThawed), Is.Zero,
                    "The same 50% frost amount must render identically during growth and thaw.");
                driver.Model.Step(AlpineColdExposureModel.FullThawSeconds * 0.5f, true);
                Assert.That(driver.Model.FrostAmount, Is.Zero);
                Color32[] fullyThawed = ReadFrostPixels(camera, "diffusion-07-thawed-clear");
                Assert.That(CountFrostChangedPixels(clear, fullyThawed), Is.Zero,
                    "Complete thaw must restore the original clear image exactly.");
                driver.Model.Reset();
                driver.Model.Step(AlpineColdExposureModel.FullExposureSeconds, false);
                GraphicsEffectsSettings.AspectRatio43Enabled = true;
                ReadFrostPixels(camera, "diffusion-05-full-4x3");
                AssertFrostDiffusionDetail(clear, clearBaseline, crystalsOnly, diffused);
                AssertFrostCameraRegions(camera, driver, true, "Diffusion house 4:3");
                GraphicsEffectsSettings.AspectRatio43Enabled = false;
                AssertFrostCameraRegions(camera, driver, false, "Diffusion house 16:9");
                Assert.That(Time.frameCount, Is.EqualTo(heldFrame));
                Assert.That(driver.Model.FrostAmount, Is.EqualTo(1f));
            }
            finally
            {
                pause?.Dispose();
                if (camera != null) camera.aspect = previousAspect;
                if (driver != null) driver.enabled = true;
                AlpineColdExposure.ResetSession();
                GraphicsEffectsSettings.AspectRatio43Enabled = previous43;
                BegottenModeRamp.DebugWeightOverride = previousFilmWeight;
                AlpineColdFrostPass.DebugDisableDiffusion = previousDiffusion;
                Time.captureDeltaTime = previousCaptureDelta;
            }
        }

        [UnityTest]
        [Explicit("Focused frost clock, audio, camera composition and real doorway round trip.")]
        [PrebuildSetup(typeof(MothersHouseLivingRoomAssetsSetup))]
        public IEnumerator AlpineFrostJourney()
        {
            AssertFrostClockAndAudio();
            bool previous43 = GraphicsEffectsSettings.AspectRatio43Enabled;
            float? previousFilmWeight = BegottenModeRamp.DebugWeightOverride;
            float previousCaptureDelta = Time.captureDeltaTime;
            AlpineColdExposureDriver driver = null;
            IDisposable pause = null;
            try
            {
                GameSessionState.BeginNewGame();
                GameSessionState.TryStartGameTimeFromWake();
                GraphicsEffectsSettings.AspectRatio43Enabled = false;
                // Inspect frost in the ordinary game image without inheriting
                // a saved film mode and its image-wide exposure/crop response.
                BegottenModeRamp.DebugWeightOverride = 0f;
                Time.captureDeltaTime = 1f / 60f;
                yield return SceneManager.LoadSceneAsync(SceneIds.AlpineVillage, LoadSceneMode.Single);
                yield return WaitForFrostRoot(false);
                var village = Object.FindAnyObjectByType<AlpineVillageRoot>();
                driver = Object.FindAnyObjectByType<AlpineColdExposureDriver>();
                Assert.That(driver, Is.Not.Null);
                Texture2D frostMask = Resources.Load<Texture2D>("Textures/AlpineColdFrostMask");
                Assert.That(frostMask, Is.Not.Null);
                Assert.That(frostMask.width, Is.GreaterThanOrEqualTo(1024));
                Assert.That(frostMask.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
                Assert.That(Resources.Load<Shader>("Shaders/AlpineColdFrost").isSupported, Is.True);
                Assert.That(driver.IsSheltered, Is.False, "An open station canopy stays cold.");
                for (int frame = 0; frame < 45; frame++) yield return null;
                Assert.That(driver.Model.ExposureSeconds, Is.GreaterThan(0f));
                driver.enabled = false;
                driver.Model.Reset();
                Camera camera = Camera.main;
                camera.aspect = 16f / 9f;
                CaptureCurrentCamera(camera, SceneIds.AlpineVillage, "frost-00-clear");
                // Inverse smoothstep values give actual coverage amounts,
                // independent of the authored outdoor exposure duration.
                float[] growthTimes = { 0.1958001f, 0.3263518f, 0.5f, 0.6736482f };
                float[] growthAmounts = { 0.1f, 0.25f, 0.5f, 0.75f };
                string[] growthNames = { "10", "25", "50", "75" };
                for (int stage = 0; stage < growthTimes.Length; stage++)
                {
                    driver.Model.Reset();
                    driver.Model.Step(Mathf.Lerp(AlpineColdExposureModel.FrostDelaySeconds,
                        AlpineColdExposureModel.FullExposureSeconds, growthTimes[stage]), false);
                    Assert.That(driver.Model.FrostAmount,
                        Is.EqualTo(growthAmounts[stage]).Within(0.00001f));
                    CaptureCurrentCamera(camera, SceneIds.AlpineVillage,
                        "frost-01-growth-" + growthNames[stage] + "pct");
                }
                driver.Model.Reset();
                driver.Model.Step(AlpineColdExposureModel.FullExposureSeconds * 0.5f, false);
                CaptureCurrentCamera(camera, SceneIds.AlpineVillage, "frost-01-growing");
                driver.Model.Step(AlpineColdExposureModel.FullExposureSeconds * 0.5f, false);
                CaptureCurrentCamera(camera, SceneIds.AlpineVillage, "frost-02-full");
                Assert.That(AlpineColdExposure.IsVisible(camera), Is.True);
                AssertFrostCameraRegions(camera, driver, false, "Village 16:9");
                float beforeRender = driver.Model.ExposureSeconds;
                GraphicsEffectsSettings.AspectRatio43Enabled = true;
                CaptureCurrentCamera(camera, SceneIds.AlpineVillage, "frost-03-full-4x3");
                AssertFrostCameraRegions(camera, driver, true, "Village 4:3");
                GraphicsEffectsSettings.AspectRatio43Enabled = false;
                Assert.That(driver.Model.ExposureSeconds, Is.EqualTo(beforeRender),
                    "Extra render calls must never age the frost.");
                var otherCamera = new GameObject("Frost excluded camera probe").AddComponent<Camera>();
                otherCamera.enabled = false;
                Assert.That(AlpineColdExposure.IsVisible(otherCamera), Is.False);
                Object.Destroy(otherCamera.gameObject);

                AssertFrostAudioScheduling(driver.FrostAudio);
                driver.FrostAudio.Step(1f, 1f, false, false);
                Assert.That(driver.FrostAudio.CuesPlayed, Is.EqualTo(1));
                Assert.That(driver.FrostAudio.Source.outputAudioMixerGroup, Is.Not.Null);
                Assert.That(driver.FrostAudio.Source.spatialBlend, Is.Zero);
                driver.enabled = true;
                pause = GameTimeScaleRuntime.AcquirePause();
                yield return null;
                int pausedSample = driver.FrostAudio.Source.timeSamples;
                for (int i = 0; i < 4; i++) yield return null;
                Assert.That(driver.Model.ExposureSeconds, Is.EqualTo(beforeRender));
                Assert.That(driver.FrostAudio.Source.timeSamples, Is.EqualTo(pausedSample));
                pause.Dispose();
                pause = null;

                Assert.That(SceneTransitionService.RequestDoorLoad(SceneIds.MothersHouseInterior,
                    DoorTransitionDirection.EnterApartment), Is.True);
                yield return WaitForFrostDoor(driver, beforeRender);
                yield return WaitForFrostRoot(true);
                Assert.That(AlpineColdExposure.FrostAmount, Is.GreaterThan(0.95f),
                    "The first visible house frames must still have the accumulated frost. " +
                    "Exact clock preservation was checked while the door transition owned the view.");
                Assert.That(driver.IsSheltered, Is.True);
                var house = Object.FindAnyObjectByType<MothersHouseInteriorRoot>();
                Assert.That(((Player3DCharacterPresentation)house.Player.Visual).ColdBodyWeight, Is.Zero);
                driver.enabled = false;
                camera = Camera.main;
                camera.aspect = 16f / 9f;
                Assert.That(AlpineColdExposure.IsVisible(camera), Is.True,
                    "The actual indoor gameplay camera must display residual frost.");
                AssertFrostCameraRegions(camera, driver, false, "House 16:9");
                CaptureCurrentCamera(camera, SceneIds.MothersHouseInterior, "frost-04-house-entry");

                // Record the actual eight-second thaw at twenty frames per
                // second, with the room animation and frost sharing that time.
                string motion = Path.Combine(Directory.GetCurrentDirectory(), "Captures",
                    SceneIds.MothersHouseInterior, "frost-thaw-motion");
                Directory.CreateDirectory(motion);
                const int thawFrames = 160;
                Time.captureDeltaTime = AlpineColdExposureModel.FullThawSeconds / thawFrames;
                for (int frame = 0; frame < thawFrames; frame++)
                {
                    driver.Model.Step(Time.captureDeltaTime, true);
                    driver.FrostAudio.Step(Time.captureDeltaTime,
                        driver.Model.FrostAmount, true, false);
                    yield return null;
                    CaptureCurrentCamera(camera, SceneIds.MothersHouseInterior,
                        "frost-thaw-motion/" + frame.ToString("000"));
                    if (frame == thawFrames / 2 - 1)
                    {
                        Assert.That(driver.FrostAudio.ThawCuesPlayed, Is.GreaterThanOrEqualTo(2),
                            "Warm arrival must play and repeat its own cues before the layer is gone.");
                        Assert.That(driver.FrostAudio.LastCueKind,
                            Is.EqualTo(AlpineFrostAudio.CueKind.Thawing));
                        CaptureCurrentCamera(camera, SceneIds.MothersHouseInterior, "frost-05-half-thawed");
                    }
                }
                Assert.That(AlpineColdExposure.FrostAmount, Is.Zero);
                Assert.That(driver.FrostAudio.Source.volume, Is.Zero);
                Assert.That(driver.FrostAudio.Source.isPlaying, Is.False);
                Assert.That(driver.FrostAudio.Source.clip, Is.Null);
                CaptureCurrentCamera(camera, SceneIds.MothersHouseInterior, "frost-06-thawed");
                Time.captureDeltaTime = 1f / 60f;
                yield return CaptureFrostHouseSofaAndHearth(house);

                // Return with residual frost, as if the player left halfway
                // through warming; the transition must retain that exact state.
                driver.Model.Step((AlpineColdExposureModel.FullExposureSeconds +
                    AlpineColdExposureModel.FrostDelaySeconds) * 0.5f, false);
                float residual = driver.Model.ExposureSeconds;
                driver.enabled = true;
                Assert.That(SceneTransitionService.RequestDoorLoad(SceneIds.AlpineVillage,
                    DoorTransitionDirection.ExitApartment), Is.True);
                yield return WaitForFrostDoor(driver, residual);
                yield return WaitForFrostRoot(false);
                Assert.That(driver.Model.ExposureSeconds, Is.InRange(residual, residual + 2f),
                    "Only the first visible outdoor frames may add exposure after the transition.");
                Assert.That(driver.IsSheltered, Is.False);
                float resumed = AlpineColdExposure.FrostAmount;
                for (int frame = 0; frame < 8; frame++) yield return null;
                Assert.That(AlpineColdExposure.FrostAmount, Is.GreaterThan(resumed));
                driver.enabled = false;
                Camera.main.aspect = 16f / 9f;
                CaptureCurrentCamera(Camera.main, SceneIds.AlpineVillage, "frost-07-return-residual");
                GameSessionState.BeginNewGame();
                Assert.That(AlpineColdExposure.FrostAmount, Is.Zero);
                driver.Model.Step(AlpineColdExposureModel.FullExposureSeconds, false);
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                Assert.That(AlpineColdExposure.FrostAmount, Is.Zero,
                    "Unrelated scenes must clear the session presentation.");
                Debug.Log("Frost journey verified: growth, pause/audio, 4:3, doorway freeze, 8s thaw, return and session reset.");
            }
            finally
            {
                pause?.Dispose();
                if (driver != null) driver.enabled = true;
                AlpineColdExposure.ResetSession();
                GraphicsEffectsSettings.AspectRatio43Enabled = previous43;
                BegottenModeRamp.DebugWeightOverride = previousFilmWeight;
                Time.captureDeltaTime = previousCaptureDelta;
            }
        }

        private static IEnumerator CaptureFrostHouseSofaAndHearth(MothersHouseInteriorRoot house)
        {
            foreach (MothersHouseInteriorPartBinding part in house.World.Registry.Parts)
            {
                Assert.That(part.SourceName, Is.Not.EqualTo("DRESS_Sofa.PatchedThrow"));
                Assert.That(part.Role, Is.Not.EqualTo("patched_throw"));
            }
            foreach (Transform part in house.Room.GetComponentsInChildren<Transform>(true))
                Assert.That(part.name, Is.Not.EqualTo("DRESS_Sofa.PatchedThrow"));
            Assert.That(house.World.Registry.TryGetPart("FIX_Sofa.Frame", out _), Is.True);
            Assert.That(house.World.Registry.TryGetPart("FIX_Sofa.Cushions", out _), Is.True);
            var hero = (Player3DCharacterPresentation)house.Player.Visual;
            AnimatorCullingMode heroCulling = hero.Registry.Animator.cullingMode;
            AnimatorCullingMode motherCulling = house.Mother.Registry.Animator.cullingMode;
            CityBenchSitInteraction sofa = house.Sofa;
            try
            {
                hero.Registry.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                house.Mother.Registry.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                Assert.That(sofa.CanInteract(house.Player.Interactor), Is.True);
                sofa.Interact(house.Player.Interactor);
                for (int frame = 0; frame < 900 &&
                    sofa.Controller.Phase != PlayerAnimatedInteractionPhase.Looping; frame++)
                    yield return null;
                Assert.That(sofa.Controller.Phase, Is.EqualTo(PlayerAnimatedInteractionPhase.Looping));
                Assert.That(sofa.IsSeated, Is.True);
                for (int frame = 0; frame < 12; frame++) yield return null;
                CaptureCurrentCamera(Camera.main, SceneIds.MothersHouseInterior, "frost-06-sofa-clear");
                yield return CaptureFrostHearthMix(house, hero.Registry.Anchors.Head.position,
                    house.Mother.Registry.HeadAnchor.position);
                sofa.Interact(house.Player.Interactor);
                for (int frame = 0; frame < 480 &&
                    sofa.Controller.Phase != PlayerAnimatedInteractionPhase.Idle; frame++)
                    yield return null;
                Assert.That(sofa.Controller.Phase, Is.EqualTo(PlayerAnimatedInteractionPhase.Idle));
            }
            finally
            {
                sofa.Controller.CancelActiveInteraction();
                hero.Registry.Animator.cullingMode = heroCulling;
                house.Mother.Registry.Animator.cullingMode = motherCulling;
            }
        }

        private static IEnumerator CaptureFrostHearthMix(MothersHouseInteriorRoot house,
            Vector3 sofaHead, Vector3 rockerHead)
        {
            AudioSource fire = house.Atmosphere.FireCrackleSource;
            Camera camera = Camera.main;
            AudioListener listener = camera.GetComponent<AudioListener>();
            Assert.That(listener != null && listener.isActiveAndEnabled, Is.True);
            int listenerCount = 0;
            foreach (AudioListener current in Object.FindObjectsByType<AudioListener>())
                if (current.isActiveAndEnabled) listenerCount++;
            Assert.That(listenerCount, Is.EqualTo(1));
            Assert.That(fire.enabled && !fire.mute && fire.isPlaying, Is.True);
            Assert.That(fire.outputAudioMixerGroup, Is.SameAs(GameAudioMixer.AmbienceDetailsGroup));
            Assert.That(fire.clip.name, Is.EqualTo("MothersHouseWarmWoodFire"));
            Assert.That(fire.clip.loadState, Is.EqualTo(AudioDataLoadState.Loaded));
            Assert.That(fire.spatialBlend, Is.EqualTo(1f));
            Assert.That(fire.rolloffMode, Is.EqualTo(AudioRolloffMode.Linear));
            Assert.That(fire.volume, Is.GreaterThan(0.22f));
            Assert.That(AudioSettings.GetConfiguration().speakerMode, Is.EqualTo(AudioSpeakerMode.Stereo));
            AudioSource[] sources = Object.FindObjectsByType<AudioSource>(
                FindObjectsInactive.Include);
            var muted = new bool[sources.Length];
            for (int i = 0; i < sources.Length; i++) muted[i] = sources[i].mute;
            float listenerVolume = AudioListener.volume;
            bool listenerPaused = AudioListener.pause;
            float gain = fire.volume, near = fire.minDistance, far = fire.maxDistance;
            int phase = fire.timeSamples;
            bool wasPlaying = fire.isPlaying;
            Vector3 cameraPosition = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            float cameraFieldOfView = camera.fieldOfView;
            float cameraAspect = camera.aspect;
            bool fixedEnabled = house.FixedCamera.enabled;
            bool followEnabled = house.CameraFollow.enabled;
            bool capturing = false;
            int rate = AudioSettings.outputSampleRate;
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.MothersHouseInterior);
            try
            {
                for (int i = 0; i < sources.Length; i++)
                    if (sources[i] != fire) sources[i].mute = true;
                house.FixedCamera.enabled = false;
                house.CameraFollow.enabled = false;
                AudioListener.pause = false;
                // Reuse the existing Begotten audio fixture's throwaway first
                // session: Unity's initial offline session may return silence.
                AudioRenderer.Start();
                capturing = true;
                AudioListener.volume = 1f;
                yield return PumpFrostHearthAudio(rate / 5, null, 30, false);
                AudioListener.volume = 0f;
                AudioRenderer.Stop();
                capturing = false;
                yield return null;
                AudioRenderer.Start();
                capturing = true;
                AudioListener.volume = 1f;
                double previousGainRms = 0d;
                Vector3[] positions = { cameraPosition, cameraPosition, sofaHead, rockerHead };
                string[] names = { "hearth-gameplay-previous-gain", "hearth-gameplay", "hearth-sofa", "hearth-rocker" };
                for (int shot = 0; shot < positions.Length; shot++)
                {
                    camera.transform.position = positions[shot];
                    fire.volume = shot == 0 ? 0.22f : gain;
                    fire.minDistance = shot == 0 ? 2f : near;
                    fire.maxDistance = shot == 0 ? 13f : far;
                    fire.Stop();
                    fire.timeSamples = 0;
                    fire.Play();
                    // Identical source offset and DSP settling for A/B. The
                    // real low-pass, distance attenuation and mixer stay on.
                    yield return PumpFrostHearthAudio(rate / 2, null);
                    var samples = new List<float>();
                    yield return PumpFrostHearthAudio(Mathf.CeilToInt(rate * 2f * 1.2f), samples);
                    float[] pcm = samples.ToArray();
                    double rms = FrostHearthRms(pcm, rate, false);
                    double high = FrostHearthRms(pcm, rate, true);
                    WritePcmWave(Path.Combine(folder, names[shot] + ".wav"), pcm, rate, 2, 1f);
                    TestContext.Out.WriteLine($"{names[shot]}: listener distance " +
                        $"{Vector3.Distance(positions[shot], fire.transform.position):F3} m, " +
                        $"DSP RMS {rms:F6}, above-150Hz RMS {high:F6}.");
                    if (shot == 0)
                    {
                        Assert.That(rms, Is.GreaterThan(0.0001d),
                            "The previous-gain reference must contain real output for a meaningful A/B.");
                        previousGainRms = rms;
                    }
                    else
                    {
                        Assert.That(rms, Is.GreaterThan(0.003d),
                            names[shot] + ": the actual post-distance mix must contain audible hearth energy.");
                        Assert.That(high, Is.GreaterThan(rms * 0.35d),
                            "The fire needs audible wood/air frequencies, not only the old sub-bass signal.");
                        if (shot == 1)
                            Assert.That(rms, Is.GreaterThan(previousGainRms * 1.5d),
                                "The real gameplay-listener mix must gain audibility over the previous source settings.");
                    }
                }
            }
            finally
            {
                AudioListener.volume = 0f;
                if (capturing) AudioRenderer.Stop();
                fire.Stop();
                fire.volume = gain;
                fire.minDistance = near;
                fire.maxDistance = far;
                fire.timeSamples = phase;
                if (wasPlaying) fire.Play();
                for (int i = 0; i < sources.Length; i++)
                    if (sources[i] != null) sources[i].mute = muted[i];
                house.FixedCamera.enabled = fixedEnabled;
                house.CameraFollow.enabled = followEnabled;
                camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                camera.fieldOfView = cameraFieldOfView;
                camera.aspect = cameraAspect;
                AudioListener.pause = listenerPaused;
                AudioListener.volume = listenerVolume;
            }
        }

        private static IEnumerator PumpFrostHearthAudio(int target, List<float> sink,
            int frameBudget = 240, bool requireSamples = true)
        {
            int rendered = 0;
            for (int frame = 0; frame < frameBudget && rendered < target; frame++)
            {
                yield return null;
                int count = AudioRenderer.GetSampleCountForCaptureFrame();
                using var buffer = new NativeArray<float>(Math.Max(0, count) * 2, Allocator.Temp);
                AudioRenderer.Render(buffer);
                rendered += buffer.Length;
                if (sink != null)
                    for (int i = 0; i < buffer.Length; i++) sink.Add(buffer[i]);
            }
            if (requireSamples)
                Assert.That(rendered, Is.GreaterThanOrEqualTo(target),
                    "Offline hearth capture produced too few samples within its bounded frame budget.");
        }

        private static double FrostHearthRms(float[] pcm, int rate, bool highPass)
        {
            double coefficient = Math.Exp(-2d * Math.PI * 150d / rate);
            var previous = new double[2];
            var filtered = new double[2];
            double squares = 0d;
            bool finite = true;
            float peak = 0f;
            for (int i = 0; i < pcm.Length; i++)
            {
                finite &= !float.IsNaN(pcm[i]) && !float.IsInfinity(pcm[i]);
                peak = Mathf.Max(peak, Mathf.Abs(pcm[i]));
                int channel = i % 2;
                double sample = pcm[i];
                if (highPass)
                {
                    filtered[channel] = coefficient * (filtered[channel] + sample - previous[channel]);
                    previous[channel] = sample;
                    sample = filtered[channel];
                }
                squares += sample * sample;
            }
            Assert.That(finite, Is.True);
            Assert.That(peak, Is.LessThan(1f), "The hearth output must not clip.");
            return Math.Sqrt(squares / pcm.Length);
        }

        private static IEnumerator WaitForFrostRoot(bool indoors)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                bool ready = indoors
                    ? Object.FindAnyObjectByType<MothersHouseInteriorRoot>()?.IsInitialized == true
                    : Object.FindAnyObjectByType<AlpineVillageRoot>()?.IsInitialized == true;
                if (ready && !SceneTransitionService.IsTransitioning && !CompositionDriver.IsComposing)
                {
                    yield return null;
                    yield break;
                }
                yield return null;
            }
            Assert.Fail("Frost route destination did not initialize.");
        }

        private static IEnumerator WaitForFrostDoor(AlpineColdExposureDriver driver, float exposure)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            bool sawDoor = false;
            while (SceneTransitionService.IsTransitioning && Time.realtimeSinceStartup < deadline)
            {
                Assert.That(driver.Model.ExposureSeconds, Is.EqualTo(exposure));
                Assert.That(AlpineColdExposure.IsVisible(Camera.main), Is.False);
                sawDoor |= SceneManager.GetActiveScene().name == SceneIds.DoorTransition;
                yield return null;
            }
            Assert.That(SceneTransitionService.IsTransitioning, Is.False);
            Assert.That(sawDoor, Is.True, "Use the actual transition scene, not a direct load.");
        }

        private static void AssertFrostClockAndAudio()
        {
            var model = new AlpineColdExposureModel();
            model.Step(6f, false);
            Assert.That(model.FrostAmount, Is.Zero);
            model.Step(AlpineColdExposureModel.FullExposureSeconds -
                AlpineColdExposureModel.FrostDelaySeconds, false);
            Assert.That(model.FrostAmount, Is.EqualTo(1f));
            model.Step(1000f, false);
            Assert.That(model.FrostAmount, Is.EqualTo(1f));
            model.Step(AlpineColdExposureModel.FullThawSeconds * 0.5f, true);
            Assert.That(model.FrostAmount, Is.EqualTo(0.5f).Within(0.00001f));
            model.Step(0f, true);
            Assert.That(model.FrostAmount, Is.EqualTo(0.5f).Within(0.00001f));
            model.Step(AlpineColdExposureModel.FullThawSeconds * 0.5f, true);
            Assert.That(model.FrostAmount, Is.Zero);
            model.Step((AlpineColdExposureModel.FullExposureSeconds +
                AlpineColdExposureModel.FrostDelaySeconds) * 0.5f, false);
            Assert.That(model.FrostAmount, Is.EqualTo(0.5f).Within(0.00001f));
            model.Step(AlpineColdExposureModel.FullThawSeconds * 0.5f - 0.25f, true);
            Assert.That(model.FrostAmount, Is.GreaterThan(0f),
                "Partial frost must still fade gradually, rather than disappear on entry.");
            model.Step(0.25f, true);
            Assert.That(model.FrostAmount, Is.Zero,
                "Half coverage must thaw in half the full thaw time.");
            var frequent = new AlpineColdExposureModel();
            model.Reset();
            model.Step(37f, false);
            for (int i = 0; i < 592; i++) frequent.Step(0.0625f, false);
            Assert.That(model.FrostAmount, Is.EqualTo(frequent.FrostAmount));
            Assert.Throws<ArgumentOutOfRangeException>(() => model.Step(float.NaN, false));

            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "AlpineVillage");
            Directory.CreateDirectory(folder);
            for (int variant = 0; variant < 3; variant++)
            {
                float[] samples = AlpineFrostAudio.GenerateSamples(variant);
                Assert.That(samples, Is.EqualTo(AlpineFrostAudio.GenerateSamples(variant)));
                double squares = 0d;
                foreach (float sample in samples)
                {
                    Assert.That(float.IsNaN(sample) || float.IsInfinity(sample), Is.False);
                    Assert.That(Math.Abs(sample), Is.LessThanOrEqualTo(0.721f));
                    squares += sample * sample;
                }
                Assert.That(Math.Sqrt(squares / samples.Length), Is.InRange(0.02d, 0.2d));
                WriteFrostWave(Path.Combine(folder, "frost-crackle-" + variant + ".wav"), samples, 0.24f);
                float[] thaw = AlpineFrostAudio.GenerateThawSamples(variant);
                Assert.That(thaw, Is.EqualTo(AlpineFrostAudio.GenerateThawSamples(variant)));
                Assert.That(thaw.Length,
                    Is.EqualTo((int)(AlpineFrostAudio.SampleRate * AlpineFrostAudio.ThawClipSeconds)));
                float thawPeak = 0f;
                double soundDifference = 0d;
                for (int i = 0; i < thaw.Length; i++)
                {
                    Assert.That(float.IsNaN(thaw[i]) || float.IsInfinity(thaw[i]), Is.False);
                    thawPeak = Mathf.Max(thawPeak, Mathf.Abs(thaw[i]));
                    soundDifference += Math.Abs(thaw[i] - samples[i]);
                }
                Assert.That(thawPeak, Is.InRange(0.499f, 0.501f));
                Assert.That(soundDifference / thaw.Length, Is.GreaterThan(0.01d),
                    "Thawing needs a distinct sound, not a quieter replay of the freezing samples.");
                WriteFrostWave(Path.Combine(folder, "frost-thaw-" + variant + ".wav"), thaw, 0.18f);
            }
        }

        private static void AssertFrostAudioScheduling(AlpineFrostAudio audio)
        {
            try
            {
                // Cover both a nearly due first cold cue and the long wait
                // after an already played crack. Warm arrival owns its delay.
                foreach (float coldElapsed in new[] { 0.79f, 3f })
                {
                    audio.Reset();
                    audio.Step(coldElapsed, 1f, false, false);
                    int coldCues = audio.FreezingCuesPlayed;
                    audio.Step(0.21f, 1f, true, false);
                    Assert.That(audio.ThawCuesPlayed, Is.Zero);
                    audio.Step(0f, 1f, true, true);
                    float pausedVolume = audio.Source.volume;
                    int pausedSamples = audio.Source.timeSamples;
                    audio.Step(100f, 1f, true, true);
                    Assert.That(audio.ThawCuesPlayed, Is.Zero);
                    Assert.That(audio.Source.volume, Is.EqualTo(pausedVolume));
                    Assert.That(audio.Source.timeSamples, Is.EqualTo(pausedSamples));
                    audio.Step(0.011f, 1f, true, false);
                    Assert.That(audio.ThawCuesPlayed, Is.EqualTo(1),
                        "The first thaw cue must arrive at about 0.22 s, independent of the cold timer.");
                    Assert.That(audio.LastCueKind, Is.EqualTo(AlpineFrostAudio.CueKind.Thawing));
                    Assert.That(audio.Source.clip.name, Does.StartWith("Alpine Frost Thaw "));
                    for (int repeat = 0; repeat < 3; repeat++)
                    {
                        int before = audio.ThawCuesPlayed;
                        float elapsed = 0f;
                        while (audio.ThawCuesPlayed == before && elapsed < 2.52f)
                        {
                            audio.Step(0.01f, 1f, true, false);
                            elapsed += 0.01f;
                        }
                        Assert.That(audio.ThawCuesPlayed, Is.EqualTo(before + 1));
                        Assert.That(elapsed, Is.InRange(1.49f, 2.51f),
                            "Thaw releases repeat every 1.5–2.5 seconds while frost remains.");
                    }
                    Assert.That(audio.FreezingCuesPlayed, Is.EqualTo(coldCues));
                    Assert.That(audio.Source.volume, Is.EqualTo(0.18f).Within(0.00001f),
                        "The exported thaw WAV uses the actual maximum source gain.");

                    int thawCues = audio.ThawCuesPlayed;
                    audio.Step(0.21f, 1f, false, false);
                    Assert.That(audio.ThawCuesPlayed, Is.EqualTo(thawCues));
                    Assert.That(audio.Source.clip, Is.Null,
                        "Cold re-entry releases the old thaw tail before selecting a cold clip.");
                    audio.Step(0.57f, 1f, false, false);
                    Assert.That(audio.FreezingCuesPlayed, Is.EqualTo(coldCues));
                    audio.Step(0.021f, 1f, false, false);
                    Assert.That(audio.FreezingCuesPlayed, Is.EqualTo(coldCues + 1));
                    Assert.That(audio.ThawCuesPlayed, Is.EqualTo(thawCues));
                    Assert.That(audio.LastCueKind, Is.EqualTo(AlpineFrostAudio.CueKind.Freezing));
                    Assert.That(audio.Source.clip.name, Does.StartWith("Alpine Frost Crystal "));
                    audio.Step(0f, 0f, true, false);
                    audio.Step(10f, 0f, true, false);
                    Assert.That(audio.Source.volume, Is.Zero);
                    Assert.That(audio.Source.isPlaying, Is.False);
                    Assert.That(audio.Source.clip, Is.Null);
                    Assert.That(audio.CuesPlayed, Is.Zero);
                    Assert.That(audio.LastCueKind, Is.EqualTo(AlpineFrostAudio.CueKind.None));
                }
            }
            finally
            {
                audio.Reset();
            }
        }

        private static void WriteFrostWave(string path, float[] samples, float gain)
        {
            WritePcmWave(path, samples, AlpineFrostAudio.SampleRate, 1, gain);
        }

        private static void WritePcmWave(string path, float[] samples, int sampleRate, int channels, float gain)
        {
            using var output = new BinaryWriter(File.Create(path));
            output.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            output.Write(36 + samples.Length * 2);
            output.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            output.Write(16); output.Write((short)1); output.Write((short)channels);
            output.Write(sampleRate); output.Write(sampleRate * channels * 2);
            output.Write((short)(channels * 2)); output.Write((short)16);
            output.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            output.Write(samples.Length * 2);
            foreach (float sample in samples) output.Write((short)(sample * gain * short.MaxValue));
        }

        private static void AssertFrostCameraRegions(Camera camera,
            AlpineColdExposureDriver driver, bool pillarboxed, string label)
        {
            float exposure = driver.Model.ExposureSeconds;
            Color32[] clear;
            Color32[] baseline;
            Color32[] frozen;
            try
            {
                driver.Model.Reset();
                clear = ReadFrostPixels(camera);
                baseline = ReadFrostPixels(camera);
                driver.Model.Step(exposure, false);
                frozen = ReadFrostPixels(camera);
            }
            finally
            {
                driver.Model.Reset();
                driver.Model.Step(exposure, false);
            }

            int imageWidth = pillarboxed ? Mathf.RoundToInt(Height * (4f / 3f)) : Width;
            int imageLeft = (Width - imageWidth) / 2;
            // Leave one low-resolution PS1 pixel around the protected centre
            // and image-window boundaries; sample both renders in one frame.
            const int border = 4;
            double edge = 0d, edgeNoise = 0d, centre = 0d, centreNoise = 0d;
            int edgeCount = 0, centreCount = 0, blackMaximum = 0;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int index = y * Width + x;
                    double difference = FrostPixelDifference(frozen[index], baseline[index]);
                    double noise = FrostPixelDifference(baseline[index], clear[index]);
                    if (pillarboxed && (x < imageLeft - border || x >= imageLeft + imageWidth + border))
                    {
                        Color32 pixel = frozen[index];
                        blackMaximum = Math.Max(blackMaximum, Math.Max(pixel.r, Math.Max(pixel.g, pixel.b)));
                        continue;
                    }
                    if (x < imageLeft + border || x >= imageLeft + imageWidth - border ||
                        y < border || y >= Height - border) continue;
                    float u = (x - imageLeft + 0.5f) / imageWidth;
                    float v = (y + 0.5f) / Height;
                    if (u < 0.14f || u > 0.86f || v < 0.14f || v > 0.86f)
                    {
                        edge += difference;
                        edgeNoise += noise;
                        edgeCount++;
                    }
                    if (u > 0.18f + border / (float)imageWidth && u < 0.82f - border / (float)imageWidth &&
                        v > 0.18f + border / (float)Height && v < 0.82f - border / (float)Height)
                    {
                        centre += difference;
                        centreNoise += noise;
                        centreCount++;
                    }
                }
            edge /= edgeCount;
            edgeNoise /= edgeCount;
            centre /= centreCount;
            centreNoise /= centreCount;
            TestContext.Out.WriteLine($"{label}: edge {edge:F3}, baseline {edgeNoise:F3}; " +
                $"centre {centre:F3}, baseline {centreNoise:F3}; bar maximum {blackMaximum} (0–255).");
            Assert.That(centreNoise, Is.LessThan(2d),
                label + ": the clear image must be stable enough to assess a local effect.");
            Assert.That(centre, Is.LessThanOrEqualTo(centreNoise * 1.5d + 0.25d),
                label + ": frost and its blur must leave the central image unchanged.");
            Assert.That(edge, Is.GreaterThan(Math.Max(0.5d, edgeNoise * 1.5d + 0.25d)),
                label + ": frost must reach the rendered edge, not only its model.");
            if (pillarboxed)
                Assert.That(blackMaximum, Is.LessThanOrEqualTo(1),
                    "4:3 black bars must remain black even at maximum frost.");
        }

        private static double MeasureFrostDiffusionStrength(Color32[] crystalsOnly, Color32[] diffused)
        {
            double difference = 0d;
            int count = 0;
            for (int y = 4; y < Height - 4; y++)
                for (int x = 4; x < Width - 4; x++)
                    if (x < Width * 0.18f || x >= Width * 0.82f ||
                        y < Height * 0.18f || y >= Height * 0.82f)
                    {
                        int index = y * Width + x;
                        difference += FrostPixelDifference(crystalsOnly[index], diffused[index]);
                        count++;
                    }
            return difference / count;
        }

        private static int CountFrostChangedPixels(Color32[] a, Color32[] b)
        {
            Assert.That(a.Length, Is.EqualTo(b.Length));
            int changed = 0;
            for (int i = 0; i < a.Length; i++)
                if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b) changed++;
            return changed;
        }

        private static void WriteFrostDiffusionComparison(Color32[] crystalsOnly, Color32[] diffused)
        {
            var combined = new Color32[Width * 2 * Height];
            for (int y = 0; y < Height; y++)
            {
                Array.Copy(crystalsOnly, y * Width, combined, y * Width * 2, Width);
                Array.Copy(diffused, y * Width, combined, y * Width * 2 + Width, Width);
            }
            var image = new Texture2D(Width * 2, Height, TextureFormat.RGB24, false);
            try
            {
                image.SetPixels32(combined);
                image.Apply();
                File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), "Captures",
                    SceneIds.MothersHouseInterior, "diffusion-04-comparison-left-off-right-on.png"), image.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
        }

        private static void AssertFrostDiffusionDetail(Color32[] clear, Color32[] baseline,
            Color32[] crystalsOnly, Color32[] diffused)
        {
            // Fixed scene ROIs chosen before rendering. The left edge contains
            // the window jamb or timber texture as the gameplay lens settles;
            // the ceiling/beam edge stays under the upper ice film.
            double sideResponse = MeasureFrostDiffusionRegion(clear, baseline, crystalsOnly, diffused,
                new RectInt(72, 385, 68, 150), "left rim", false);
            double beamResponse = MeasureFrostDiffusionRegion(clear, baseline, crystalsOnly, diffused,
                new RectInt(Mathf.RoundToInt(Width * 0.25f), Mathf.RoundToInt(Height * 0.89f),
                    Mathf.RoundToInt(Width * 0.40f), Mathf.RoundToInt(Height * 0.07f)), "ceiling beam", true);
            if (!double.IsNaN(sideResponse))
                Assert.That(sideResponse, Is.LessThan(0.75d),
                    "The side ice film must visibly diffuse the scene, independently of the ceiling.");
            Assert.That(beamResponse, Is.LessThan(0.75d),
                "Strong ice diffusion must remove at least a quarter of the scene's edge response, " +
                "while the same foreground crystals remain visible.");

            const int step = 4;
            double centreDifference = 0d, centreNoise = 0d;
            int centrePixels = 0;
            for (int y = Mathf.CeilToInt(Height * 0.18f) + step;
                y < Mathf.FloorToInt(Height * 0.82f) - step; y++)
                for (int x = Mathf.CeilToInt(Width * 0.18f) + step;
                    x < Mathf.FloorToInt(Width * 0.82f) - step; x++)
                {
                    int index = y * Width + x;
                    centreDifference += FrostPixelDifference(crystalsOnly[index], diffused[index]);
                    centreNoise += FrostPixelDifference(clear[index], baseline[index]);
                    centrePixels++;
                }
            Assert.That(centreDifference / centrePixels,
                Is.LessThanOrEqualTo(centreNoise / centrePixels * 1.5d + 0.25d),
                "Enabling diffusion must preserve the exact central image, allowing one PS1 boundary pixel.");
        }

        private static double MeasureFrostDiffusionRegion(Color32[] clear, Color32[] baseline,
            Color32[] crystalsOnly, Color32[] diffused, RectInt region, string label, bool required)
        {
            const int step = 4;
            double sharpEnergy = 0d, blurredEnergy = 0d, baselineEnergy = 0d;
            double clearEnergy = 0d, sharpProjection = 0d, blurredProjection = 0d;
            int detailPixels = 0;
            for (int y = region.yMin; y < region.yMax - step; y++)
                for (int x = region.xMin; x < region.xMax - step; x++)
                {
                    int index = y * Width + x;
                    double clearX = FrostLuma(clear[index + step]) - FrostLuma(clear[index]);
                    double clearY = FrostLuma(clear[index + step * Width]) - FrostLuma(clear[index]);
                    // Select scene detail in the clean reference, never by
                    // how much a candidate pixel happened to blur.
                    if (clearX * clearX + clearY * clearY < 36d) continue;
                    double sharpX = FrostLuma(crystalsOnly[index + step]) - FrostLuma(crystalsOnly[index]);
                    double sharpY = FrostLuma(crystalsOnly[index + step * Width]) - FrostLuma(crystalsOnly[index]);
                    double blurredX = FrostLuma(diffused[index + step]) - FrostLuma(diffused[index]);
                    double blurredY = FrostLuma(diffused[index + step * Width]) - FrostLuma(diffused[index]);
                    double baselineX = FrostLuma(baseline[index + step]) - FrostLuma(baseline[index]) - clearX;
                    double baselineY = FrostLuma(baseline[index + step * Width]) - FrostLuma(baseline[index]) - clearY;
                    sharpEnergy += sharpX * sharpX + sharpY * sharpY;
                    blurredEnergy += blurredX * blurredX + blurredY * blurredY;
                    baselineEnergy += baselineX * baselineX + baselineY * baselineY;
                    clearEnergy += clearX * clearX + clearY * clearY;
                    sharpProjection += sharpX * clearX + sharpY * clearY;
                    blurredProjection += blurredX * clearX + blurredY * clearY;
                    detailPixels++;
                }
            double sharpRms = Math.Sqrt(sharpEnergy / Math.Max(1, detailPixels));
            double blurredRms = Math.Sqrt(blurredEnergy / Math.Max(1, detailPixels));
            double baselineRms = Math.Sqrt(baselineEnergy / Math.Max(1, detailPixels));
            double sceneRatio = sharpProjection > 0d ? blurredProjection / sharpProjection : double.NaN;
            TestContext.Out.WriteLine($"Frost diffusion {label}: pixels {detailPixels}, " +
                $"crystals-only RMS {sharpRms:F3}, blurred {blurredRms:F3}, " +
                $"raw HF ratio {blurredRms / sharpRms:F3}, clear-gradient ratio {sceneRatio:F3}, " +
                $"clear-repeat RMS {baselineRms:F3} (0–255).");
            if (detailPixels <= 128)
            {
                if (required)
                    Assert.That(detailPixels, Is.GreaterThan(128),
                        "The fixed house reference must contain real scene detail beneath the ice film.");
                TestContext.Out.WriteLine(label + ": insufficient clean scene detail; this ROI provides no blur proof.");
                return double.NaN;
            }
            Assert.That(sharpRms, Is.GreaterThan(4d));
            Assert.That(baselineRms, Is.LessThan(1d), "The A/B reference must stay still.");
            Assert.That(sharpProjection, Is.GreaterThan(clearEnergy * 0.2d),
                "The crystals-only reference must still carry the scene edge being measured.");
            return sceneRatio;
        }

        private static double FrostLuma(Color32 color)
        {
            return color.r * 0.2126d + color.g * 0.7152d + color.b * 0.0722d;
        }

        private static double FrostPixelDifference(Color32 a, Color32 b)
        {
            return (Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b)) / 3d;
        }

        private static Color32[] ReadFrostPixels(Camera camera, string shotName = null)
        {
            var target = new RenderTexture(Width, Height, 24);
            var pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                pixels.Apply();
                if (shotName != null)
                {
                    string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures",
                        SceneManager.GetActiveScene().name);
                    Directory.CreateDirectory(folder);
                    File.WriteAllBytes(Path.Combine(folder, shotName + ".png"), pixels.EncodeToPNG());
                }
                return pixels.GetPixels32();
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(pixels);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }
    }
}
