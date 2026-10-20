using System.Collections;
using System.Collections.Generic;
using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class SceneCompositeRenderGraphPlayModeTests
    {
        private GameObject cameraObject;
        private GameObject sourceObject;
        private RenderTexture target;
        private Texture2D sourceTexture;
        private Texture2D readback;
        private Material sourceMaterial;
        private bool previousFogEnabled;
        private bool fogStateCaptured;
        private readonly Dictionary<string, int?> savedPreferences =
            new Dictionary<string, int?>();
        private bool effectFlagsCaptured;
        private bool warehouseCaptureLoaded;
        private PresentationLifecycleEvidence lifecycle;
        private RenderPipeline.StandardRequest warehouseRequest;

        [System.Serializable]
        private sealed class PresentationLifecycleEvidence
        {
            public string interpretation = "Session-cold scene construction and first/refresh probe work in the already-running Editor; not OS-cold loading. sceneLoadAwakeMs ends at LoadSceneAsync completion and initialized root. firstRetainedDiagnosticFrameMs includes twelve intentional warmup frames, target setup and explicit render/readback; it is not time to first visible game frame. Probe wall latency includes GPU scheduling and waits, and is not CPU time.";
            public double sceneLoadAwakeMs, firstRetainedDiagnosticFrameMs;
            public ProbeEvidence firstProbe, refreshedProbe;
            public ProbeGpuWindow firstProbeGpu, refreshedProbeGpu;
            public ShadowEvidence shadow = new ShadowEvidence();
            public VolumePathCoverageEvidence volumePathCoverage;
        }

        [System.Serializable]
        private sealed class ProbeEvidence
        {
            public int starts, completed, failures, cancelled, timeouts;
            public double submitCpuMs, pollCpuMs, latencyTotalSeconds, lastLatencySeconds;
            public bool ready;

            internal static ProbeEvidence Snapshot(WarehouseReflectionController controller) => new ProbeEvidence
            {
                starts = controller.CaptureCount, completed = controller.CaptureCompletedCount,
                failures = controller.CaptureFailureCount, cancelled = controller.CaptureCancelledCount,
                timeouts = controller.CaptureTimeoutCount, ready = controller.IsReady,
                submitCpuMs = controller.CaptureSubmitTicks * 1000d / System.Diagnostics.Stopwatch.Frequency,
                pollCpuMs = controller.CapturePollTicks * 1000d / System.Diagnostics.Stopwatch.Frequency,
                latencyTotalSeconds = controller.TotalCaptureLatencySeconds,
                lastLatencySeconds = controller.LastCaptureLatencySeconds
            };

            internal static ProbeEvidence Delta(ProbeEvidence before, WarehouseReflectionController controller)
            {
                ProbeEvidence after = Snapshot(controller);
                after.starts -= before.starts; after.completed -= before.completed;
                after.failures -= before.failures; after.cancelled -= before.cancelled;
                after.timeouts -= before.timeouts; after.submitCpuMs -= before.submitCpuMs;
                after.pollCpuMs -= before.pollCpuMs; after.latencyTotalSeconds -= before.latencyTotalSeconds;
                return after;
            }
        }

        [System.Serializable]
        private sealed class ShadowEvidence
        {
            public bool available;
            public int visibleLightIndex = -1, atlasWidth, atlasHeight, requestedResolution;
            public int allocatedResolution, atlasScaleFactor;
            public string unavailableReason = "The volume shadow-layout hook has not been observed.";
        }

        [System.Serializable]
        private sealed class VolumePathCoverageEvidence
        {
            public string interpretation = "Editor-only opaque path mask, SMAA disabled for counting: green ordinary, blue safe prefix/ring, red exact full-resolution repair, black outside conservative projected bounds. Red counts path invocation, not actual shadow fetches; the integrator may exit early. Mixed/postprocessed boundary colours remain separate.";
            public int totalPixels, ordinaryGreen, safeBlue, exactRepairRed, outsideBlack, mixed;
            public double exactFractionOfFrame;
        }

        [System.Serializable]
        private sealed class ProbeGpuWindow
        {
            public string interpretation = "Whole rendered Editor frames around probe submit/filter/completion, plus five drain frames. Delayed GPU timing boundaries are approximate; includes all rendering, not isolated probe cost. No screenshot readback during collection.";
            public bool featureEnabled;
            public int observedFrames, unfocusedFrames;
            public int renderWidth, renderHeight, targetFrameRate, vSyncCount, sampleLimit = 512;
            public bool sampleLimitReached;
            public string graphicsApi, gpu;
            public PerformanceDistribution gpuMs;
            private readonly FrameTiming[] timing = new FrameTiming[16];
            private readonly List<double> values = new List<double>(512);
            private ulong lastTimestamp;

            internal void Arm(Camera camera)
            {
                renderWidth = camera.pixelWidth; renderHeight = camera.pixelHeight;
                targetFrameRate = Application.targetFrameRate; vSyncCount = QualitySettings.vSyncCount;
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(); gpu = SystemInfo.graphicsDeviceName;
                featureEnabled = FrameTimingManager.IsFeatureEnabled();
                if (!featureEnabled) return;
                uint count = FrameTimingManager.GetLatestTimings(16, timing);
                for (int i = 0; i < count; i++) lastTimestamp = System.Math.Max(lastTimestamp, timing[i].frameStartTimestamp);
                FrameTimingManager.CaptureFrameTimings();
            }

            internal void Sample()
            {
                observedFrames++;
                if (!Application.isFocused) unfocusedFrames++;
                if (!featureEnabled) return;
                uint count = FrameTimingManager.GetLatestTimings(16, timing);
                ulong newest = lastTimestamp;
                for (int i = 0; i < count; i++)
                {
                    FrameTiming value = timing[i];
                    if (value.frameStartTimestamp > lastTimestamp && value.gpuFrameTime > 0d && values.Count < 512)
                        values.Add(value.gpuFrameTime);
                    newest = System.Math.Max(newest, value.frameStartTimestamp);
                }
                sampleLimitReached |= values.Count == sampleLimit;
                lastTimestamp = newest;
                FrameTimingManager.CaptureFrameTimings();
            }

            internal void Finish()
            {
                values.Sort();
                if (values.Count == 0) return;
                double sum = 0d;
                foreach (double value in values) sum += value;
                gpuMs = new PerformanceDistribution
                {
                    sampleCount = values.Count, mean = sum / values.Count,
                    maximum = values[values.Count - 1],
                    p50 = values[(values.Count - 1) / 2],
                    p95 = values[(int)System.Math.Ceiling((values.Count - 1) * .95d)],
                    p99 = values[(int)System.Math.Ceiling((values.Count - 1) * .99d)]
                };
            }
        }
        private static readonly string[] RetiredPreferenceKeys =
        {
            "graphics.style", "graphics.dither", "graphics.scanlines", "graphics.vertex_jitter"
        };

        [SetUp]
        public void CaptureEffectFlags()
        {
            foreach (string key in new[]
            {
                "graphics.style", "graphics.clean_aa", "graphics.dither", "graphics.scanlines",
                "graphics.aspect_4_3", "graphics.vertex_jitter", "graphics.clean_reflections",
                "graphics.clean_volume", "graphics.clean_halation", "graphics.clean_directional_highlights",
                "graphics.clean_bounce", "graphics.dof", "graphics.intoxication_fx"
            })
                savedPreferences[key] = PlayerPrefs.HasKey(key)
                    ? PlayerPrefs.GetInt(key) : (int?)null;
            effectFlagsCaptured = true;
            PlayerPrefs.SetInt("graphics.style", 0);
            foreach (string key in RetiredPreferenceKeys)
                if (key != "graphics.style") PlayerPrefs.SetInt(key, 1);
            GraphicsEffectsSettings.ResetLoadedStateForTests();
            GraphicsEffectsSettings.CleanAntialiasingEnabled = false;
            GraphicsEffectsSettings.AspectRatio43Enabled = false;
            GraphicsEffectsSettings.IntoxicationLensFxEnabled = true;
            IntoxicationRenderState.Clear();
        }

        [UnityTest]
        public IEnumerator GameCamera_NativeEffectsAndWarehouseLightingRemainStable()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Actual RenderGraph output requires a graphics device.");

            RenderComposite(1280, 720);
            Camera camera = cameraObject.GetComponent<Camera>();
            SceneCompositeRendererFeature feature = FindFeature();
            Assert.That(feature.CompositeMaterial.passCount, Is.EqualTo(2));
            Assert.That(feature.CompositeMaterial.FindPass("NativeComposite"), Is.Zero);
            Assert.That(feature.CompositeMaterial.FindPass("NativePresentation"), Is.EqualTo(1));
            Assert.That(CheckerContrast(), Is.GreaterThan(.8f),
                "Native rendering must retain the source's one-pixel checker.");
            Assert.That(((Color32)readback.GetPixel(960, 180)).r, Is.EqualTo(20).Within(1));
            Assert.That(((Color32)readback.GetPixel(960, 540)).r, Is.EqualTo(228).Within(1));
            Assert.That(readback.GetPixel(960, 500), Is.EqualTo(readback.GetPixel(960, 501)),
                "Retired scanline preferences must not alter native rows.");
            Color32[] withRetiredPreferences = readback.GetPixels32();
            foreach (string key in RetiredPreferenceKeys) PlayerPrefs.DeleteKey(key);
            GraphicsEffectsSettings.ResetLoadedStateForTests();
            RenderAgain(camera);
            CollectionAssert.AreEqual(withRetiredPreferences, readback.GetPixels32(),
                "Retired style, dither, scanline and jitter preferences must be ignored.");
            try
            {
                Color32[] sober = readback.GetPixels32();
                IntoxicationRenderState.Set(IntoxicationStageRules.Evaluate(100),
                    1.25f, 0f, Vector2.zero, Vector3.zero);
                RenderAgain(camera);
                Assert.That(ChangedSamples(sober, readback.GetPixels32()), Is.GreaterThan(100),
                    "Intoxication must remain visible in the native presentation.");
                IntoxicationRenderState.Clear();
                GraphicsEffectsSettings.AspectRatio43Enabled = true;
                RenderAgain(camera);
                AssertPillarbox();
                GraphicsEffectsSettings.AspectRatio43Enabled = false;
                yield return AssertCleanAntialiasing(camera, feature);
            }
            finally { IntoxicationRenderState.Clear(); }

            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-bp-render-capture") >= 0)
                yield return CaptureWarehouseNativeEffects();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (warehouseCaptureLoaded)
            {
                Scene scene = SceneManager.GetActiveScene();
                if (scene.IsValid() && scene.name == SceneIds.CombatTest)
                {
                    Scene cleanup = SceneManager.CreateScene("Test presentation cleanup");
                    SceneManager.SetActiveScene(cleanup);
                    AsyncOperation unload = SceneManager.UnloadSceneAsync(scene);
                    if (unload != null) yield return unload;
                }
                warehouseCaptureLoaded = false;
            }
            if (effectFlagsCaptured)
            {
                foreach (KeyValuePair<string, int?> preference in savedPreferences)
                {
                    if (preference.Value.HasValue)
                        PlayerPrefs.SetInt(preference.Key, preference.Value.Value);
                    else PlayerPrefs.DeleteKey(preference.Key);
                }
                PlayerPrefs.Save();
                GraphicsEffectsSettings.ResetLoadedStateForTests();
                savedPreferences.Clear();
                effectFlagsCaptured = false;
            }

            if (fogStateCaptured)
            {
                RenderSettings.fog = previousFogEnabled;
                fogStateCaptured = false;
            }

            if (cameraObject != null)
            {
                Object.Destroy(cameraObject);
            }

            if (sourceObject != null)
            {
                Object.Destroy(sourceObject);
            }

            if (target != null)
            {
                target.Release();
                Object.Destroy(target);
            }

            if (sourceMaterial != null)
            {
                Object.Destroy(sourceMaterial);
            }

            if (sourceTexture != null)
            {
                Object.Destroy(sourceTexture);
            }

            if (readback != null)
            {
                Object.Destroy(readback);
            }

            yield return null;
        }

        private void RenderComposite(int outputWidth, int outputHeight)
        {
            cameraObject = new GameObject("Test presentation camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 1 << 30;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.allowDynamicResolution = false;
            camera.orthographic = true;
            camera.orthographicSize = 1f;
            camera.aspect = outputWidth / (float)outputHeight;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 10f;
            camera.transform.position = new Vector3(5000f, 5000f, -2f);
            UniversalAdditionalCameraData cameraData =
                camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            cameraData.renderShadows = false;
            cameraData.volumeLayerMask = 0;
            previousFogEnabled = RenderSettings.fog;
            fogStateCaptured = true;
            RenderSettings.fog = false;

            CreateSource(outputWidth, outputHeight, camera);

            target = new RenderTexture(
                outputWidth,
                outputHeight,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear)
            {
                name = "Test presentation target",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            Assert.That(target.Create(), Is.True);
            camera.targetTexture = target;

            readback = new Texture2D(
                outputWidth,
                outputHeight,
                TextureFormat.RGBA32,
                false,
                true);
            RenderAgain(camera);
        }

        private void RenderAgain(Camera camera)
        {
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0, false);
                readback.Apply(false, false);
            }
            finally { RenderTexture.active = previous; }
        }

        private IEnumerator AssertCleanAntialiasing(Camera camera, SceneCompositeRendererFeature feature)
        {
            GraphicsEffectsSettings.CleanAntialiasingEnabled = false;
            // A shallow diagonal has real staircase edges; a one-pixel checker
            // instead tests the native-resolution contract, not edge smoothing.
            var diagonal = new Texture2D(target.width, target.height,
                TextureFormat.RGBA32, false, true)
            {
                name = "Test diagonal source", filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
            };
            var pixels = new Color32[target.width * target.height];
            for (int y = 0; y < target.height; y++)
                for (int x = 0; x < target.width; x++)
                {
                    byte tone = x < 160f + y * .65f ? (byte)20 : (byte)228;
                    pixels[y * target.width + x] = new Color32(tone, tone, tone, 255);
                }
            diagonal.SetPixels32(pixels);
            diagonal.Apply(false, true);
            sourceMaterial.SetTexture("_BaseMap", diagonal);
            Object.DestroyImmediate(sourceTexture);
            sourceTexture = diagonal;
            RenderAgain(camera);
            Color32[] unfiltered = readback.GetPixels32();
            Assert.That(feature.DebugCleanSmaaApplied, Is.False);

            GraphicsEffectsSettings.CleanAntialiasingEnabled = true;
            RenderAgain(camera);
            Assert.That(feature.DebugCleanSmaaApplied, Is.True,
                "Clean's enabled SMAA must actually record its spatial passes.");
            Color32[] filtered = readback.GetPixels32();
            Assert.That(ChangedSamples(unfiltered, filtered), Is.GreaterThan(5));
            int softened = 0;
            for (int y = 32; y < target.height - 32; y++)
                for (int x = Mathf.FloorToInt(160f + y * .65f) - 2;
                     x <= Mathf.CeilToInt(160f + y * .65f) + 2; x++)
                {
                    Color32 pixel = readback.GetPixel(x, y);
                    if (pixel.r > 22 && pixel.r < 226) softened++;
                }
            Assert.That(softened, Is.GreaterThan(100), "Diagonal edges must receive partial coverage.");
            Assert.That(((Color32)readback.GetPixel(1000, 180)).r, Is.EqualTo(228).Within(1));
            Assert.That(((Color32)readback.GetPixel(20, 180)).r, Is.EqualTo(20).Within(1));

            // Returning to the same camera pose must reproduce the same image:
            // spatial AA has no history to leave a trail after camera motion.
            Vector3 position = camera.transform.position;
            camera.transform.position += Vector3.right * .05f;
            camera.ResetWorldToCameraMatrix();
            yield return null;
            RenderAgain(camera);
            Assert.That(ChangedSamples(filtered, readback.GetPixels32()), Is.GreaterThan(5));
            camera.transform.position = position;
            camera.ResetWorldToCameraMatrix();
            yield return null;
            RenderAgain(camera);
            CollectionAssert.AreEqual(filtered, readback.GetPixels32());

            var excluded = cameraObject.AddComponent<ScenePresentationExclusion>();
            GraphicsEffectsSettings.AspectRatio43Enabled = true;
            IntoxicationRenderState.Set(IntoxicationStageRules.Evaluate(100),
                1.25f, .2f, Vector2.one, new Vector3(5000f, 5000f, 0f));
            RenderAgain(camera);
            Assert.That(feature.DebugCleanSmaaApplied, Is.False);
            CollectionAssert.AreEqual(unfiltered, readback.GetPixels32());
            Object.DestroyImmediate(excluded);
            IntoxicationRenderState.Clear();
            GraphicsEffectsSettings.AspectRatio43Enabled = false;

            GraphicsEffectsSettings.AspectRatio43Enabled = true;
            RenderAgain(camera);
            Assert.That(feature.DebugCleanSmaaApplied, Is.True);
            AssertPillarbox();
            GraphicsEffectsSettings.AspectRatio43Enabled = false;
            IntoxicationRenderState.Set(IntoxicationStageRules.Evaluate(100),
                1.25f, .2f, Vector2.one, new Vector3(5000f, 5000f, 0f));
            RenderAgain(camera);
            Assert.That(feature.DebugCleanSmaaApplied, Is.True);
            IntoxicationRenderState.Clear();
            GraphicsEffectsSettings.CleanAntialiasingEnabled = false;
            RenderAgain(camera);
            Assert.That(feature.DebugCleanSmaaApplied, Is.False);
        }

        private float CheckerContrast()
        {
            float contrast = 0f;
            for (int x = 320; x < 384; x++)
                contrast += Mathf.Abs(readback.GetPixel(x, 180).r - readback.GetPixel(x + 1, 180).r);
            return contrast / 64f;
        }

        private static int ChangedSamples(Color32[] first, Color32[] second)
        {
            int changed = 0;
            for (int i = 0; i < first.Length; i += 47)
                if (first[i].r != second[i].r || first[i].g != second[i].g || first[i].b != second[i].b)
                    changed++;
            return changed;
        }

        private void AssertPillarbox()
        {
            int left = (target.width - Mathf.RoundToInt(target.height * 4f / 3f)) / 2;
            int right = target.width - left;
            foreach (int x in new[] { 0, left / 2, left - 2, right + 2,
                (right + target.width) / 2, target.width - 1 })
            {
                Color32 pixel = readback.GetPixel(x, target.height / 2);
                Assert.That((int)pixel.r + pixel.g + pixel.b, Is.Zero,
                    "The shared 4:3 frame must keep its black pillarbox.");
            }
            int content = 0;
            for (int x = target.width / 3; x <= target.width * 2 / 3; x += target.width / 16)
            {
                Color32 pixel = readback.GetPixel(x, target.height * 3 / 4);
                content += pixel.r + pixel.g + pixel.b;
            }
            Assert.That(content, Is.GreaterThan(0), "The visible frame must retain its image.");
        }

        private static SceneCompositeRendererFeature FindFeature()
        {
            foreach (SceneCompositeRendererFeature feature in
                Resources.FindObjectsOfTypeAll<SceneCompositeRendererFeature>())
                if (feature != null && feature.isActive) return feature;
            Assert.Fail("The PC renderer's presentation feature is not loaded.");
            return null;
        }

        private IEnumerator CaptureWarehouseNativeEffects()
        {
            bool previousArena = CombatTestRoot.UseLegacyArenaForTests;
            int? previousSeed = CombatTestRoot.WarehouseSeedForTests;
            float previousCaptureDelta = Time.captureDeltaTime;
            bool previousBackground = Application.runInBackground;
            int previousTargetFrameRate = Application.targetFrameRate;
            int previousVSync = QualitySettings.vSyncCount;
            int previousRenderInterval = OnDemandRendering.renderFrameInterval;
            CombatTestRoot root = null;
            Camera camera = null;
            bool previousCameraEnabled = false;
            RenderTexture previousTarget = null;
            Light[] lights = null;
            bool[] enabledLights = null;
            lifecycle = new PresentationLifecycleEvidence();
            double loadStarted = Time.realtimeSinceStartupAsDouble;
            try
            {
                Application.runInBackground = true;
                Time.captureDeltaTime = 1f / 60f;
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = 0;
                GameSessionState.BeginNewGame();
                // Disable before scene construction: otherwise the automatic
                // camera can submit the first probe during warmup, unobserved.
                SetCleanEnhancements(false);
                warehouseCaptureLoaded = true;
                yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest, LoadSceneMode.Single);
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                Assert.That(root != null && root.IsInitialized && root.WarehousePlan != null, Is.True);
                lifecycle.sceneLoadAwakeMs = (Time.realtimeSinceStartupAsDouble - loadStarted) * 1000d;
                root.AutomaticSimulation = false;
                root.AutomaticOpponentDecisions = false;
                root.SetDuelLogging(false);
                root.SetSparring(false);
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                for (int frame = 0; frame < 12; frame++) yield return null;
                camera = root.CameraFollow.Camera;
                previousCameraEnabled = camera.enabled;
                root.CameraFollow.enabled = false;
                previousTarget = camera.targetTexture;
                target.Release();
                Object.Destroy(target);
                target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB) { name = "Test warehouse presentation target", filterMode = FilterMode.Point };
                Assert.That(target.Create(), Is.True);
                Object.Destroy(readback);
                readback = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, true);
                camera.targetTexture = target;
                warehouseRequest = new RenderPipeline.StandardRequest
                {
                    destination = target, mipLevel = 0, slice = 0, face = CubemapFace.Unknown
                };
                camera.aspect = target.width / (float)target.height;
                SetCleanEnhancements(false);
                // Allow URP to install the new HDR/post-process target before comparison.
                yield return null;
                yield return null;
                WarehouseCleanPresentation warehouse = WarehouseCleanPresentation.Current;
                Assert.That(warehouse != null && warehouse.IsForCamera(camera), Is.True);
                Assert.That(warehouse.BounceRegionCount, Is.InRange(1, 4));
                SceneCompositeRendererFeature feature = FindFeature();
                var mixer = Resources.Load<UnityEngine.Audio.AudioMixer>("Audio/Mixers/BarPromenadeAudio");
                Assert.That(mixer, Is.Not.Null);
                StringAssert.DoesNotContain("Begotten", System.IO.File.ReadAllText(
                    System.IO.Path.Combine(Application.dataPath, "Resources/Audio/Mixers/BarPromenadeAudio.mixer")),
                    "The retired film mode must not retain mixer effects or exposed controls.");
                int[] effectDifferences = new int[5];
                bool previousRealtimeProbes = QualitySettings.realtimeReflectionProbes;
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    GraphicsEffectsSettings.CleanAntialiasingEnabled = true;
                    SetCleanEnhancements(false);
                    RenderAgain(camera);
                    lifecycle.firstRetainedDiagnosticFrameMs = (Time.realtimeSinceStartupAsDouble - loadStarted) * 1000d;
                    SaveWarehouseFrame("Test warehouse - Clean baseline");
                    camera.enabled = false;
                    // Arm GPU statistics before the first genuine RenderProbe.
                    // Keep this separate from later warm steady-state windows.
                    for (int warmup = 0; warmup < 8; warmup++)
                    {
                        FrameTimingManager.CaptureFrameTimings();
                        SubmitWarehouseFrame(camera);
                        yield return null;
                    }
                    for (int effect = 0; effect < 5; effect++)
                    {
                        SetCleanEnhancements(false);
                        ProbeEvidence firstProbeStart = effect == 0 ? ProbeEvidence.Snapshot(warehouse.Reflection) : null;
                        if (effect == 0)
                        {
                            lifecycle.firstProbeGpu = new ProbeGpuWindow();
                            lifecycle.firstProbeGpu.Arm(camera);
                        }
#if UNITY_EDITOR
                        if (effect == 1) WarehouseFanVolumePass.ShadowLayoutForTests = ObserveShadowLayout;
#endif
                        SetCleanEnhancement(effect, true);
                        Debug.Log("Clean warehouse comparison: " + CleanEnhancementName(effect));
                        if (effect == 0)
                        {
                            double deadline = Time.realtimeSinceStartupAsDouble + 10d;
                            do
                            {
                                SubmitWarehouseFrame(camera);
                                yield return null;
                                lifecycle.firstProbeGpu.Sample();
                            } while (!warehouse.Reflection.IsReady && Time.realtimeSinceStartupAsDouble < deadline);
                            Assert.That(warehouse.Reflection.IsReady, Is.True,
                                "The bounded reflection capture must finish before comparison.");
                            Assert.That(QualitySettings.realtimeReflectionProbes, Is.EqualTo(previousRealtimeProbes),
                                "A completed capture must restore the original quality setting.");
                            lifecycle.firstProbe = ProbeEvidence.Delta(firstProbeStart, warehouse.Reflection);
                            for (int drain = 0; drain < 5; drain++)
                            {
                                SubmitWarehouseFrame(camera); yield return null;
                                lifecycle.firstProbeGpu.Sample();
                            }
                            lifecycle.firstProbeGpu.Finish();
                            Debug.Log("Warehouse first probe: " + JsonUtility.ToJson(lifecycle.firstProbe));
                            Debug.Log("Warehouse first probe GPU window: " + JsonUtility.ToJson(lifecycle.firstProbeGpu));
                        }
                        SetCleanEnhancements(false);
                        RenderAgain(camera);
                        Color32[] effectBaseline = readback.GetPixels32();
                        SetCleanEnhancement(effect, true);
                        RenderAgain(camera);
                        int changed = ChangedSamples(effectBaseline, readback.GetPixels32());
                        Debug.Log("Clean warehouse visible samples: " + CleanEnhancementName(effect) + " = " + changed);
                        effectDifferences[effect] = changed;
                        SaveWarehouseFrame("Test warehouse - " + CleanEnhancementName(effect));
                        if (effect == 1)
                        {
                            Assert.That(feature.DebugCleanVolumeApplied, Is.True);
                            Assert.That(feature.DebugCleanVolumeResolution.x, Is.InRange(1, 320));
                            Assert.That(feature.DebugCleanVolumeResolution.y, Is.InRange(1, 180));
                            Color32[] firstVolume = readback.GetPixels32();
                            Transform rotor = warehouse.Mechanisms.FanRotor;
                            Quaternion pose = rotor.localRotation;
                            rotor.localRotation = pose * Quaternion.Euler(0f, 27f, 0f);
                            // GPU Resident Drawer uploads transformed meshes once per real frame.
                            yield return null;
                            GraphicsEffectsSettings.CleanVolumeEnabled = false;
                            RenderAgain(camera);
                            Color32[] turnedBaseline = readback.GetPixels32();
                            GraphicsEffectsSettings.CleanVolumeEnabled = true;
                            RenderAgain(camera);
                            Assert.That(ChangedEffectSamples(effectBaseline, firstVolume,
                                turnedBaseline, readback.GetPixels32()), Is.GreaterThan(0),
                                "The air's lighting must follow the actual moving blade shadow.");
                            SaveWarehouseFrame("Test warehouse - Light volume blade turn");
                            rotor.localRotation = pose;
                            yield return null;
#if UNITY_EDITOR
                            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(),
                                    "-bp-volume-diagnostics") >= 0)
                            {
                                try
                                {
                                    foreach (int view in new[] { 1, 2, 3, 4 })
                                    {
                                        WarehouseFanVolumePass.DebugViewForTests = view;
                                        bool antialiasing = GraphicsEffectsSettings.CleanAntialiasingEnabled;
                                        try
                                        {
                                            if (view == 4) GraphicsEffectsSettings.CleanAntialiasingEnabled = false;
                                            RenderAgain(camera);
                                            SaveWarehouseFrame("Test warehouse - Volume " + (view == 1
                                                ? "surface depth" : view == 2 ? "raw scattering" :
                                                view == 3 ? "resolved scattering" : "path coverage"));
                                            if (view == 4) CaptureVolumePathCoverage();
                                        }
                                        finally { GraphicsEffectsSettings.CleanAntialiasingEnabled = antialiasing; }
                                    }
                                    Debug.Log("Volume diagnostics: surface depth is eye metres / 32; " +
                                        "raw and resolved scattering share 16x radiance exposure.");
                                }
                                finally { WarehouseFanVolumePass.DebugViewForTests = 0; }
                                RenderAgain(camera);
                            }
#endif
                        }
                        if (effect == 2) Assert.That(feature.DebugCleanHalationApplied, Is.True);
                        if (effect == 4 && warehouse.TryGetBounceRegion(0, out var region))
                        {
                            Vector3 originalPosition = camera.transform.position;
                            Quaternion originalRotation = camera.transform.rotation;
                            float originalFieldOfView = camera.fieldOfView;
                            camera.fieldOfView = 48f;
                            camera.transform.position = region.Center + new Vector3(0f, .9f, -1.15f);
                            camera.transform.LookAt(region.Center + new Vector3(0f, .65f, 1.55f));
                            camera.ResetWorldToCameraMatrix();
                            // Upload the new view before comparing both settings in one frame.
                            yield return null;
                            GraphicsEffectsSettings.CleanBounceEnabled = false;
                            RenderAgain(camera);
                            Color32[] bounceBaseline = readback.GetPixels32();
                            GraphicsEffectsSettings.CleanBounceEnabled = true;
                            RenderAgain(camera);
                            int witness = ChangedSamples(bounceBaseline, readback.GetPixels32());
                            effectDifferences[effect] = Mathf.Max(effectDifferences[effect], witness);
                            Debug.Log("Clean warehouse bounce close view samples: " + witness);
                            SaveWarehouseFrame("Test warehouse - Reflected light close view");
                            camera.fieldOfView = originalFieldOfView;
                            camera.transform.SetPositionAndRotation(originalPosition, originalRotation);
                            camera.ResetWorldToCameraMatrix();
                            yield return null;
                        }
                    }
                    SetCleanEnhancements(true);
                    RenderAgain(camera);
                    SaveWarehouseFrame("Test warehouse - Clean all effects");
                    Vector3 cameraPosition = camera.transform.position;
                    camera.transform.position += new Vector3(.7f, .15f, 0f);
                    camera.ResetWorldToCameraMatrix();
                    RenderAgain(camera);
                    SaveWarehouseFrame("Test warehouse - Clean camera turn");
                    camera.transform.position = cameraPosition;
                    camera.ResetWorldToCameraMatrix();
                    GraphicsEffectsSettings.AspectRatio43Enabled = true;
                    RenderAgain(camera);
                    AssertPillarbox();
                    GraphicsEffectsSettings.AspectRatio43Enabled = false;
                    var exclusion = camera.gameObject.AddComponent<ScenePresentationExclusion>();
                    RenderAgain(camera);
                    Assert.That(feature.DebugCleanVolumeApplied, Is.False);
                    Assert.That(feature.DebugCleanHalationApplied, Is.False);
                    Assert.That(Shader.GetGlobalVector("_WarehouseCleanEffects"), Is.EqualTo(Vector4.zero));
                    Object.DestroyImmediate(exclusion);
                    // Metal also receives the hero's scene-owned light, outside
                    // the warehouse's cached fixed-source population.
                    lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include);
                    enabledLights = new bool[lights.Length];
                    for (int i = 0; i < lights.Length; i++)
                    {
                        enabledLights[i] = lights[i].enabled;
                        lights[i].enabled = false;
                    }
                    RenderAgain(camera);
                    Color32[] darkEffects = readback.GetPixels32();
                    Assert.That(warehouse.Reflection.CurrentGain, Is.Zero,
                        "Extinguished sources must invalidate the previous bright reflection immediately.");
                    Assert.That(feature.DebugCleanVolumeApplied, Is.False);
                    Assert.That(feature.DebugCleanHalationApplied, Is.False);
                    SetCleanEnhancements(false);
                    RenderAgain(camera);
                    CollectionAssert.AreEqual(darkEffects, readback.GetPixels32(),
                        "Disabled sources must leave no retained glow, bounce or volume.");
                    // Flush prior diagnostic readbacks before arming the refresh
                    // GPU window; never discard their late samples afterwards.
                    for (int warmup = 0; warmup < 8; warmup++)
                    {
                        FrameTimingManager.CaptureFrameTimings();
                        SubmitWarehouseFrame(camera);
                        yield return null;
                    }
                    ProbeEvidence refreshStart = ProbeEvidence.Snapshot(warehouse.Reflection);
                    lifecycle.refreshedProbeGpu = new ProbeGpuWindow();
                    lifecycle.refreshedProbeGpu.Arm(camera);
                    for (int i = 0; i < lights.Length; i++) lights[i].enabled = enabledLights[i];
                    lights = null;
                    SetCleanEnhancements(true);
                    double reflectionDeadline = Time.realtimeSinceStartupAsDouble + 10d;
                    do
                    {
                        SubmitWarehouseFrame(camera);
                        yield return null;
                        lifecycle.refreshedProbeGpu.Sample();
                    } while (!warehouse.Reflection.IsReady && Time.realtimeSinceStartupAsDouble < reflectionDeadline);
                    Assert.That(warehouse.Reflection.IsReady, Is.True);
                    lifecycle.refreshedProbe = ProbeEvidence.Delta(refreshStart, warehouse.Reflection);
                    for (int drain = 0; drain < 5; drain++)
                    {
                        SubmitWarehouseFrame(camera); yield return null;
                        lifecycle.refreshedProbeGpu.Sample();
                    }
                    lifecycle.refreshedProbeGpu.Finish();
                    Debug.Log("Warehouse refreshed probe: " + JsonUtility.ToJson(lifecycle.refreshedProbe));
                    Debug.Log("Warehouse refreshed probe GPU window: " + JsonUtility.ToJson(lifecycle.refreshedProbeGpu));
#if UNITY_EDITOR
                    // Fault injection belongs after the genuine cold/refresh
                    // measurements, never inside either GPU timing window.
                    yield return AssertReflectionTimeoutRecovery(camera, warehouse.Reflection, warehouse.FanLight);
#endif
                    Time.captureDeltaTime = 0f;
                    Application.targetFrameRate = -1;
                    QualitySettings.vSyncCount = 0;
                    OnDemandRendering.renderFrameInterval = 1;
                    // A hidden Editor need not repaint its Game view. Submit one
                    // retained target per real frame, with automatic rendering off
                    // so the measured workload never depends on Editor tab focus.
                    camera.enabled = false;
                    var request = new RenderPipeline.StandardRequest
                    {
                        destination = target,
                        mipLevel = 0,
                        slice = 0,
                        face = CubemapFace.Unknown
                    };
                    Debug.Log("Clean performance GPU timing enabled: " + FrameTimingManager.IsFeatureEnabled());
                    for (int warmup = 0; warmup < 10; warmup++)
                    {
                        FrameTimingManager.CaptureFrameTimings();
                        RenderPipeline.SubmitRenderRequest(camera, request);
                        yield return null;
                    }
                    // This static pair isolates only the saved lighting switches.
                    // The following live combat window is a separate workload, not an A/B claim.
                    foreach (bool effects in new[] { false, true })
                    {
                        SetCleanEnhancements(effects);
                        yield return WaitForCaptureFocus();
                        Assert.That(RuntimePerformanceCapture.StartCapture(new PerformanceCaptureOptions(
                            SceneIds.CombatTest, effects ? "Test static effects on" : "Test static effects off", 1, 4),
                            PerformanceDirectory(), camera), Is.True);
                        double deadline = Time.realtimeSinceStartupAsDouble + 10d;
                        while (RuntimePerformanceCapture.IsRunning && Time.realtimeSinceStartupAsDouble < deadline)
                        {
                            RenderPipeline.SubmitRenderRequest(camera, request);
                            yield return null;
                        }
                        ReadPerformanceReport(true);
                    }
                    for (int effect = 0; effect < 5; effect++)
                        Assert.That(effectDifferences[effect], Is.GreaterThan(0),
                            CleanEnhancementName(effect) + " must make a visible difference in the actual warehouse.");
                }
                yield return AssertIdleLiquidSurfaceRefresh();
                yield return CaptureActiveWarehouse(root, camera, warehouse);
                if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-bp-render-profile") >= 0)
                    yield return CaptureActiveWarehouse(root, camera, warehouse, true);
            }
            finally
            {
#if UNITY_EDITOR
                WarehouseFanVolumePass.ShadowLayoutForTests = null;
#endif
                if (lights != null)
                    for (int i = 0; i < lights.Length; i++) if (lights[i] != null) lights[i].enabled = enabledLights[i];
                if (camera != null)
                {
                    camera.enabled = previousCameraEnabled;
                    camera.targetTexture = previousTarget;
                }
                if (root != null)
                {
                    root.AutomaticSimulation = false;
                    root.PauseMenu?.Cancel();
                    Object.Destroy(root.gameObject);
                }
                RuntimePerformanceCapture.StopCapture();
                CombatTestRoot.UseLegacyArenaForTests = previousArena;
                CombatTestRoot.WarehouseSeedForTests = previousSeed;
                Time.captureDeltaTime = previousCaptureDelta;
                Application.runInBackground = previousBackground;
                Application.targetFrameRate = previousTargetFrameRate;
                QualitySettings.vSyncCount = previousVSync;
                OnDemandRendering.renderFrameInterval = previousRenderInterval;
                GameSessionState.BeginNewGame();
                RetroAudioService.Instance?.StopAll();
                string folder = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(),
                    "TestResults", "Test render profile");
                System.IO.Directory.CreateDirectory(folder);
                System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "Presentation lifecycle.json"),
                    JsonUtility.ToJson(lifecycle, true));
            }
        }

        private static IEnumerator AssertIdleLiquidSurfaceRefresh()
        {
            var host = new GameObject("Test liquid surface refresh");
            HomeUrineEffect effect = null;
            const string scope = "Test liquid surface refresh";
            try
            {
                host.transform.position = new Vector3(1000f, 0f, 1000f);
                GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.name = "Test liquid floor";
                floor.transform.SetParent(host.transform, false);
                floor.transform.localScale = new Vector3(4f, .2f, 4f);
                var owner = new GameObject("Test liquid effect");
                owner.transform.SetParent(host.transform, false);
                effect = owner.AddComponent<HomeUrineEffect>();
                effect.Initialize(host.transform, scope, null);
                int refreshes = effect.SurfaceRefreshCount;
                yield return null;
                yield return null;
                Assert.That(effect.SurfaceRefreshCount, Is.EqualTo(refreshes),
                    "An idle effect with no deposits or packets must not refresh receiver transforms.");

                effect.BeginEmission();
                floor.transform.position += Vector3.right * 3f;
                Physics.SyncTransforms();
                Vector3 outlet = floor.transform.position + Vector3.up * .45f;
                effect.EmitStep(outlet, Vector3.down, .15f, 1f, false);
                effect.StopEmission();
                Assert.That(effect.SurfaceHitCount, Is.GreaterThan(0),
                    "The first emission step must hit a receiver moved outside its old cached bounds.");
                Assert.That(effect.ScopedResidueCount, Is.GreaterThan(0));
                double deadline = Time.realtimeSinceStartupAsDouble + 2d;
                while (effect.ActivePacketCount > 0 && Time.realtimeSinceStartupAsDouble < deadline)
                    yield return null;
                Assert.That(effect.ActivePacketCount, Is.Zero);
                refreshes = effect.SurfaceRefreshCount;
                yield return null;
                yield return null;
                Assert.That(effect.SurfaceRefreshCount, Is.EqualTo(refreshes),
                    "Unchanged attached residue must not wake idle receiver refreshes.");

                int addedSlot = -1;
                for (int revision = 0; revision < 2; revision++)
                {
                    floor.transform.position += Vector3.right * 3f;
                    Physics.SyncTransforms();
                    // A fresh independent map supplies a real hit. The effect's
                    // own cached map must be refreshed by dirty projection.
                    var map = new HomeUrineSurfaceMap(host.transform, owner.transform);
                    Vector3 point = floor.transform.position + Vector3.right * .7f;
                    Assert.That(map.Cast(point + Vector3.up, point - Vector3.up, out var hit), Is.True);
                    refreshes = effect.SurfaceRefreshCount;
                    int slot = HomeUrineResidue.Add(hit, 1f, scope);
                    if (revision == 0) addedSlot = slot;
                    else Assert.That(slot, Is.EqualTo(addedSlot), "The second update must revise the same attached deposit.");
                    yield return null;
                    Assert.That(effect.SurfaceRefreshCount, Is.EqualTo(refreshes + 1),
                        "A new or revised idle deposit must refresh receiver geometry exactly once for projection.");
                    Transform stain = floor.transform.Find("Urine Stain " + slot);
                    Assert.That(stain, Is.Not.Null);
                    Assert.That(stain.parent, Is.EqualTo(floor.transform));
                    Bounds bounds = stain.GetComponent<MeshRenderer>().bounds;
                    Assert.That(bounds.size.x, Is.GreaterThan(.02f));
                    Assert.That(bounds.size.z, Is.GreaterThan(.02f),
                        "Dirty projection on a moved floor must retain a real patch rather than collapse to its fallback centre.");
                }
                Debug.Log("Idle liquid receivers: no idle refresh, moved emission hit, new/revised deposits refreshed once and remain attached.");
            }
            finally
            {
                if (effect != null) effect.ClearResidue();
                Object.DestroyImmediate(host);
                HomeUrineResidue.RemoveScope(scope);
            }
        }

        private IEnumerator CaptureActiveWarehouse(CombatTestRoot root, Camera camera,
            WarehouseCleanPresentation warehouse, bool profileCpuTimeline = false)
        {
            Assert.That(Application.isBatchMode, Is.False,
                "The rendered GPU diagnostic requires the normal Editor loop.");
            Time.captureDeltaTime = 0f;
            Application.targetFrameRate = BarPromenadeRuntimeBootstrap.TargetFrameRate;
            QualitySettings.vSyncCount = 0;
            OnDemandRendering.renderFrameInterval = 1;
            SetCleanEnhancements(true);
            root.AutomaticOpponentDecisions = true;
            root.AutomaticSimulation = true;
            camera.enabled = false;
            root.CameraFollow.enabled = false;
            var request = new RenderPipeline.StandardRequest
            {
                destination = target, mipLevel = 0, slice = 0, face = CubemapFace.Unknown
            };
            yield return WaitForCaptureFocus();
            root.SendMessage("OnApplicationFocus", true);
            GameInput.HandleApplicationFocus(true);
            root.SetSparring(true);
            Assert.That(root.Sparring, Is.True);
            // SetSparring resets the round and spawns. Place the live pair after
            // that reset, while the test command input gate is explicitly open.
            PlaceActivePair(root);
            StageActiveCamera(root, camera, 0f);
            // Let the real renderer upload the placed actors before proving
            // that the timed camera sees the hall rather than a wall surface.
            yield return null;
            StageActiveCamera(root, camera, 0f);
            RenderAgain(camera);
            string suffix = profileCpuTimeline ? " profiled" : string.Empty;
            SaveWarehouseFrame("Test warehouse - Active combat staging" + suffix);
            AssertActiveFrameContent(root, camera, "before capture");
            using var timeline = new CpuTimelineLease(profileCpuTimeline);
            Assert.That(RuntimePerformanceCapture.StartCapture(new PerformanceCaptureOptions(
                SceneIds.CombatTest, "Test active camera and combat" + suffix, 1, 6),
                PerformanceDirectory(), camera), Is.True);
            int commands = 0, attacks = 0, steps = 0, resets = 0;
            int aiDecisions = 0, previousDecision = root.OpponentDecisionSequence, contacts = 0;
            float heroTravel = 0f, opponentTravel = 0f, rotorTravel = 0f, stepTravel = 0f;
            Vector3 heroStart = root.Hero.transform.position;
            Vector3 stepStart = heroStart;
            Vector3 opponentStart = root.Opponent.transform.position;
            Quaternion rotorStart = warehouse.Mechanisms.FanRotor.localRotation;
            long simulationStart = root.TotalSimulationTicks;
            double routeStarted = Time.realtimeSinceStartupAsDouble, nextCommand = routeStarted;
            double deadline = Time.realtimeSinceStartupAsDouble + 13d;
            while (RuntimePerformanceCapture.IsRunning && Time.realtimeSinceStartupAsDouble < deadline)
            {
                aiDecisions += Mathf.Max(0, root.OpponentDecisionSequence - previousDecision);
                previousDecision = root.OpponentDecisionSequence;
                if (root.RoundFinished)
                {
                    contacts += root.Hero.ReceivedImpactCount + root.Opponent.ReceivedImpactCount;
                    root.ResetRound();
                    PlaceActivePair(root);
                    resets++;
                    previousDecision = root.OpponentDecisionSequence;
                }
                if (root.Opponent.ZombieAwake) root.SetOpponentFocus(true);
                if (Time.realtimeSinceStartupAsDouble >= nextCommand &&
                    root.Hero.State.Phase == MeleePhase.Ready && root.Hero.HasAttackBalance)
                {
                    // Footwork comes first, before damage can stagger the hero.
                    bool stepping = commands % 3 == 0;
                    bool accepted = stepping
                        ? root.Hero.TryStep(commands % 2 == 0 ? Vector2.left : Vector2.right)
                        : root.Hero.TryAttack();
                    if (accepted)
                    {
                        if (stepping) { steps++; stepStart = root.Hero.transform.position; }
                        else attacks++;
                        commands++;
                        nextCommand = Time.realtimeSinceStartupAsDouble + .75d;
                    }
                }
                heroTravel = Mathf.Max(heroTravel, (root.Hero.transform.position - heroStart).magnitude);
                if (root.Hero.State.Phase == MeleePhase.Step)
                    stepTravel = Mathf.Max(stepTravel, (root.Hero.transform.position - stepStart).magnitude);
                opponentTravel = Mathf.Max(opponentTravel, (root.Opponent.transform.position - opponentStart).magnitude);
                rotorTravel = Mathf.Max(rotorTravel,
                    Quaternion.Angle(rotorStart, warehouse.Mechanisms.FanRotor.localRotation));
                float phase = (float)(Time.realtimeSinceStartupAsDouble - routeStarted);
                StageActiveCamera(root, camera, phase);
                RenderPipeline.SubmitRenderRequest(camera, request);
                yield return null;
            }
            if (RuntimePerformanceCapture.IsRunning) RuntimePerformanceCapture.StopCapture();
            // Export/readback/PNG encoding are outside both measured windows.
            timeline.Dispose();
            contacts += root.Hero.ReceivedImpactCount + root.Opponent.ReceivedImpactCount;
            Debug.Log($"Native active workload: attacks={attacks}, steps={steps}, AI decisions={aiDecisions}, " +
                $"contacts={contacts}, round resets={resets}, step travel={stepTravel:F2} m, " +
                $"hero travel={heroTravel:F2} m, opponent travel={opponentTravel:F2} m, rotor travel={rotorTravel:F1} degrees.");
            // Keep the frame and workload evidence even when a subsequent
            // contract assertion fails. Readback/PNG encoding are outside timing.
            StageActiveCamera(root, camera, (float)(Time.realtimeSinceStartupAsDouble - routeStarted));
            RenderAgain(camera);
            SaveWarehouseFrame("Test warehouse - Active camera and combat" + suffix);
            AssertActiveFrameContent(root, camera, "after capture");
            PerformanceCaptureReport report = ReadPerformanceReport(false);
            Assert.That(root.TotalSimulationTicks, Is.GreaterThan(simulationStart),
                "The live render window must advance the production combat simulation.");
            Assert.That(attacks, Is.GreaterThan(0), "The hero must commit real melee attacks.");
            Assert.That(steps, Is.GreaterThan(0), "The hero must commit real combat footwork.");
            Assert.That(stepTravel, Is.GreaterThan(.05f), "The committed footwork must actually move the hero.");
            Assert.That(heroTravel, Is.GreaterThan(.05f));
            Assert.That(opponentTravel, Is.GreaterThan(.05f), "The live opponent must move under its AI.");
            Assert.That(aiDecisions, Is.GreaterThan(0), "The opponent must commit a real AI combat decision.");
            Assert.That(contacts, Is.GreaterThan(0), "The live combat window must resolve actual body contacts.");
            Assert.That(rotorTravel, Is.GreaterThan(1f), "The production fan must keep moving.");
            Debug.Log($"Native active GPU p95={report.gpuMs.p95:F3} ms against a 16.667 ms frame budget. " +
                "This single Editor route is not a paired effect-cost benchmark. " +
                (profileCpuTimeline ? "CPU Timeline is enabled: profiling overhead applies; use inclusive hierarchy, not summed scopes." : "CPU Timeline is disabled for this ordinary baseline."));
            root.AutomaticSimulation = false;
        }

        private sealed class CpuTimelineLease : System.IDisposable
        {
            private System.Reflection.MethodInfo stop;
            private string directory;

            internal CpuTimelineLease(bool enabled)
            {
                if (!enabled) return;
#if UNITY_EDITOR
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                System.Type owner = null;
                foreach (System.Type candidate in UnityEditor.TypeCache
                    .GetTypesWithAttribute<UnityEditor.InitializeOnLoadAttribute>())
                {
                    if (candidate.FullName != "BarPromenade.Editor.CombatCpuTimelineCapture") continue;
                    owner = candidate;
                    break;
                }
                Assert.That(owner, Is.Not.Null, "The existing Editor CPU Timeline helper must be loaded.");
                var start = owner.GetMethod("StartCapture", flags);
                stop = owner.GetMethod("StopCapture", flags);
                Assert.That(start != null && stop != null, Is.True);
                directory = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(),
                    "TestResults", "Test render profile", "CPU Timeline");
                bool started = (bool)start.Invoke(null, new object[] { directory });
                if (!started) stop = null;
                Assert.That(started, Is.True,
                    "The bounded diagnostic must not interrupt an existing Profiler capture.");
                directory = (string)owner.GetProperty("LastCaptureDirectory", flags).GetValue(null);
#else
                Assert.Fail("CPU Timeline collection requires the Editor.");
#endif
            }

            public void Dispose()
            {
                if (stop == null) return;
                var finish = stop;
                stop = null;
                finish.Invoke(null, new object[] { "render_profile_completed" });
                string manifest = System.IO.Path.Combine(directory, "Capture.json");
                Assert.That(System.IO.File.Exists(manifest), Is.True);
                Debug.Log("Bounded CPU Timeline (Deep off, inclusive scopes and Editor waits): " +
                    System.IO.File.ReadAllText(manifest));
            }
        }

#if UNITY_EDITOR
        private void CaptureVolumePathCoverage()
        {
            Color32[] pixels = readback.GetPixels32();
            var evidence = new VolumePathCoverageEvidence { totalPixels = pixels.Length };
            foreach (Color32 color in pixels)
            {
                if (color.r < 16 && color.g < 16 && color.b < 16) evidence.outsideBlack++;
                else if (color.r > 64 && color.r > color.g * 2 && color.r > color.b * 2) evidence.exactRepairRed++;
                else if (color.g > 64 && color.g > color.r * 2 && color.g > color.b * 2) evidence.ordinaryGreen++;
                else if (color.b > 64 && color.b > color.r * 2 && color.b > color.g * 2) evidence.safeBlue++;
                else evidence.mixed++;
            }
            evidence.exactFractionOfFrame = evidence.exactRepairRed / (double)pixels.Length;
            lifecycle.volumePathCoverage = evidence;
            Debug.Log("Warehouse volume path coverage: " + JsonUtility.ToJson(evidence));
        }

        private IEnumerator AssertReflectionTimeoutRecovery(Camera camera,
            WarehouseReflectionController reflection, Light source)
        {
            Assert.That(source != null && source.enabled, Is.True);
            bool previousQuality = QualitySettings.realtimeReflectionProbes;
            int starts = reflection.CaptureCount;
            int completed = reflection.CaptureCompletedCount;
            int timeouts = reflection.CaptureTimeoutCount;
            int failures = reflection.CaptureFailureCount;
            bool previousSource = source.enabled;
            try
            {
                source.enabled = false;
                SubmitWarehouseFrame(camera);
                yield return null;
                Assert.That(reflection.CurrentGain, Is.Zero);
                source.enabled = true;
                double deadline = Time.realtimeSinceStartupAsDouble + 10d;
                while (!reflection.CaptureInProgress && Time.realtimeSinceStartupAsDouble < deadline)
                {
                    SubmitWarehouseFrame(camera);
                    yield return null;
                }
                Assert.That(reflection.CaptureInProgress, Is.True, "The injected fault must target a real pending capture.");
                Assert.That(reflection.CaptureCount, Is.EqualTo(starts + 1));
                Assert.That(QualitySettings.realtimeReflectionProbes, Is.True);
                reflection.DebugExpireCaptureForTests();
                yield return null;
                Assert.That(reflection.CaptureTimeoutCount, Is.EqualTo(timeouts + 1));
                Assert.That(reflection.CaptureFailureCount, Is.EqualTo(failures + 1));
                Assert.That(reflection.CaptureInProgress, Is.False);
                Assert.That(reflection.IsReady, Is.False);
                Assert.That(reflection.CurrentGain, Is.Zero);
                Assert.That(QualitySettings.realtimeReflectionProbes, Is.EqualTo(previousQuality),
                    "A timed-out asynchronous request must release its global quality lease.");
                deadline = Time.realtimeSinceStartupAsDouble + 10d;
                while (!reflection.IsReady && Time.realtimeSinceStartupAsDouble < deadline)
                {
                    SubmitWarehouseFrame(camera);
                    yield return null;
                }
                Assert.That(reflection.IsReady, Is.True, "The same source state permits one bounded recovery attempt.");
                Assert.That(reflection.CaptureCount, Is.EqualTo(starts + 2));
                Assert.That(reflection.CaptureCompletedCount, Is.EqualTo(completed + 1));
                Assert.That(reflection.CaptureTimeoutCount, Is.EqualTo(timeouts + 1));
                Assert.That(QualitySettings.realtimeReflectionProbes, Is.EqualTo(previousQuality));
                Debug.Log("Warehouse reflection timeout recovery: one real pending request expired, quality restored, gain cleared, one recovery completed.");
            }
            finally { if (source != null) source.enabled = previousSource; }
        }

        private void ObserveShadowLayout(UniversalShadowData data, int visibleLightIndex, Vector2Int atlas)
        {
            ShadowEvidence result = lifecycle.shadow;
            result.visibleLightIndex = visibleLightIndex;
            result.atlasWidth = atlas.x; result.atlasHeight = atlas.y;
            if (data.resolution != null && visibleLightIndex >= 0 && visibleLightIndex < data.resolution.Count)
                result.requestedResolution = data.resolution[visibleLightIndex];
            try
            {
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                object layout = typeof(UniversalShadowData).GetField("shadowAtlasLayout", flags)?.GetValue(data);
                if (layout == null) throw new System.InvalidOperationException("URP shadowAtlasLayout unavailable.");
                object request = layout.GetType().GetMethod("GetSliceShadowResolutionRequest", flags)
                    .Invoke(layout, new object[] { visibleLightIndex, 0 });
                result.allocatedResolution = System.Convert.ToInt32(request.GetType()
                    .GetField("allocatedResolution", flags).GetValue(request));
                result.atlasScaleFactor = System.Convert.ToInt32(layout.GetType()
                    .GetMethod("GetShadowSlicesScaleFactor", flags).Invoke(layout, null));
                result.available = true; result.unavailableReason = null;
            }
            catch (System.Exception exception)
            {
                result.available = false;
                result.unavailableReason = exception.GetType().Name + ": " + exception.Message;
            }
            Debug.Log("Warehouse actual fan shadow allocation: " + JsonUtility.ToJson(result));
        }
#endif

        private void SubmitWarehouseFrame(Camera camera)
        {
            RenderPipeline.SubmitRenderRequest(camera, warehouseRequest);
        }

        private static void PlaceActivePair(CombatTestRoot root)
        {
            // The central aisle is clear between the authored cargo/racks.
            // Stay well inside the shell's +/-12 m depth, including knockback.
            Vector3 ground = new Vector3(0f, PlayerFactory.GroundedRootOffset, -3.5f);
            root.Hero.ResetActor(ground, Vector3.forward);
            root.Opponent.ResetActor(ground + Vector3.forward * 1.5f, Vector3.back);
            root.Opponent.TryAwakenZombie();
            Physics.SyncTransforms();
        }

        private static void StageActiveCamera(CombatTestRoot root, Camera camera, float phase)
        {
            Vector3 hero = root.Hero.transform.position;
            camera.fieldOfView = 65f;
            // A bounded rear-aisle view stays clear of the shell, side rooms
            // and central racks, even when real combat displaces either actor.
            camera.transform.position = new Vector3(
                Mathf.Clamp(hero.x + 1.6f + Mathf.Sin(phase) * .45f, -2.2f, 2.2f),
                2.3f + Mathf.Sin(phase * .7f) * .12f,
                Mathf.Clamp(hero.z - 3.6f, -9f, -5.8f));
            Vector3 lookAt = (hero + root.Opponent.transform.position) * .5f + Vector3.up * 1.4f;
            camera.transform.LookAt(lookAt);
            camera.ResetWorldToCameraMatrix();
        }

        private void AssertActiveFrameContent(CombatTestRoot root, Camera camera, string stage)
        {
            double sum = 0d, squareSum = 0d;
            int minimum = 255, maximum = 0;
            const int columns = 64, rows = 36;
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                {
                    Color32 color = readback.GetPixel((x * 2 + 1) * readback.width / (columns * 2),
                        (y * 2 + 1) * readback.height / (rows * 2));
                    int luminance = (color.r * 54 + color.g * 183 + color.b * 19) / 256;
                    minimum = Mathf.Min(minimum, luminance);
                    maximum = Mathf.Max(maximum, luminance);
                    sum += luminance;
                    squareSum += luminance * luminance;
                }
            double mean = sum / (columns * rows);
            double variance = squareSum / (columns * rows) - mean * mean;
            Vector3 hero = camera.WorldToViewportPoint(root.Hero.transform.position + Vector3.up * .9f);
            Vector3 opponent = camera.WorldToViewportPoint(root.Opponent.transform.position + Vector3.up * .9f);
            Debug.Log($"Native active frame {stage}: luminance range={maximum - minimum}, variance={variance:F2}, " +
                $"camera={camera.transform.position}, hero viewport={hero}, opponent viewport={opponent}.");
            Assert.That(root.WarehousePlan.InteriorBounds.Contains(camera.transform.position), Is.True,
                "The active camera must remain inside the authored warehouse shell.");
            Assert.That(Physics.CheckSphere(camera.transform.position, .12f, ~0, QueryTriggerInteraction.Ignore), Is.False,
                "The active camera must not be embedded in collision geometry.");
            Assert.That(maximum - minimum, Is.GreaterThan(32),
                "A nearly uniform wall/background frame is not a rendered warehouse workload.");
            Assert.That(variance, Is.GreaterThan(16d),
                "The active frame must contain visible scene structure.");
            foreach (Vector3 actor in new[] { hero, opponent })
            {
                Assert.That(actor.z, Is.GreaterThan(camera.nearClipPlane));
                Assert.That(actor.x, Is.InRange(.05f, .95f));
                Assert.That(actor.y, Is.InRange(.05f, .95f));
            }
        }

        private static IEnumerator WaitForCaptureFocus()
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 2d;
            while (!Application.isFocused && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            if (!Application.isFocused)
                Debug.Log("Performance capture is unfocused; report counters distinguish stable background Editor from mixed-focus windows.");
        }

        private static string PerformanceDirectory() => System.IO.Path.Combine(
            System.IO.Directory.GetCurrentDirectory(), "Captures", "CleanPerformance");

        private static PerformanceCaptureReport ReadPerformanceReport(bool paused)
        {
            Assert.That(RuntimePerformanceCapture.IsRunning, Is.False, "The bounded capture must finish.");
            string path = RuntimePerformanceCapture.LastReportPath;
            Assert.That(System.IO.File.Exists(path), Is.True);
            PerformanceCaptureReport report = JsonUtility.FromJson<PerformanceCaptureReport>(
                System.IO.File.ReadAllText(path));
            Assert.That(report.reason, Is.EqualTo("completed"));
            Assert.That(report.renderWidth, Is.EqualTo(1920));
            Assert.That(report.renderHeight, Is.EqualTo(1080));
            Assert.That(report.gpuMs.sampleCount, Is.GreaterThan(0), "GPU timing must be measured, not assumed.");
            if (paused) Assert.That(report.pausedFrames, Is.GreaterThan(0));
            else
            {
                Assert.That(report.pausedFrames, Is.Zero);
                Assert.That(report.targetFrameRate, Is.EqualTo(BarPromenadeRuntimeBootstrap.TargetFrameRate));
                Assert.That(report.vSyncCount, Is.Zero);
                Assert.That(report.renderFrameInterval, Is.EqualTo(1));
            }
            string focus = report.unfocusedFrames == 0 ? "foreground" :
                report.unfocusedFrames == report.frameIntervalMs.sampleCount ? "background Editor" : "mixed focus";
            bool matched = focus != "mixed focus" &&
                report.pausedFrames == (paused ? report.frameIntervalMs.sampleCount : 0);
            Debug.Log($"Native performance {report.label}: GPU samples={report.gpuMs.sampleCount}, " +
                $"median={report.gpuMs.p50:F3} ms, p95={report.gpuMs.p95:F3} ms; " +
                $"frame median={report.frameIntervalMs.p50:F3} ms; paused={report.pausedFrames}, " +
                $"unfocused={report.unfocusedFrames}, focus={focus}, context stable={matched}. " +
                (matched ? "Editor diagnostic only." : "Context changed: do not use this window for benchmark conclusions."));
            return report;
        }

        private static int ChangedEffectSamples(Color32[] firstOff, Color32[] firstOn,
            Color32[] secondOff, Color32[] secondOn)
        {
            int changed = 0;
            for (int i = 0; i < firstOff.Length; i += 47)
                if (Mathf.Abs((firstOn[i].r - firstOff[i].r) - (secondOn[i].r - secondOff[i].r)) > 1 ||
                    Mathf.Abs((firstOn[i].g - firstOff[i].g) - (secondOn[i].g - secondOff[i].g)) > 1 ||
                    Mathf.Abs((firstOn[i].b - firstOff[i].b) - (secondOn[i].b - secondOff[i].b)) > 1) changed++;
            return changed;
        }

        private void SaveWarehouseFrame(string name)
        {
            string folder = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest);
            System.IO.Directory.CreateDirectory(folder);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, name + ".png"), readback.EncodeToPNG());
        }

        private static string CleanEnhancementName(int effect) => effect switch
        {
            0 => "Reflections", 1 => "Light volume", 2 => "Light halation",
            3 => "Metal highlights", _ => "Reflected light"
        };

        private static void SetCleanEnhancements(bool enabled)
        {
            for (int effect = 0; effect < 5; effect++) SetCleanEnhancement(effect, enabled);
        }

        private static void SetCleanEnhancement(int effect, bool enabled)
        {
            switch (effect)
            {
                case 0: GraphicsEffectsSettings.CleanReflectionsEnabled = enabled; break;
                case 1: GraphicsEffectsSettings.CleanVolumeEnabled = enabled; break;
                case 2: GraphicsEffectsSettings.CleanHalationEnabled = enabled; break;
                case 3: GraphicsEffectsSettings.CleanDirectionalHighlightsEnabled = enabled; break;
                case 4: GraphicsEffectsSettings.CleanBounceEnabled = enabled; break;
            }
        }

        private void CreateSource(
            int width,
            int height,
            Camera camera)
        {
            sourceTexture = new Texture2D(
                width,
                height,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = "Test native presentation source",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            var pixels = new Color32[width * height];
            var darkTone = new Color32(20, 20, 20, 255);
            var brightTone = new Color32(228, 228, 228, 255);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (x < width / 2)
                    {
                        bool white = (x + y) % 2 == 0;
                        pixels[y * width + x] = white
                            ? new Color32(255, 255, 255, 255)
                            : new Color32(0, 0, 0, 255);
                    }
                    else
                    {
                        pixels[y * width + x] =
                            y < height / 2 ? darkTone : brightTone;
                    }
                }
            }

            sourceTexture.SetPixels32(pixels);
            sourceTexture.Apply(false, true);

            Shader shader =
                Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);
            sourceMaterial = new Material(shader)
            {
                name = "Test native presentation source material",
                hideFlags = HideFlags.DontSave
            };
            sourceMaterial.SetTexture("_BaseMap", sourceTexture);
            sourceMaterial.SetColor("_BaseColor", Color.white);
            sourceMaterial.SetFloat("_Cull", 0f);

            sourceObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            sourceObject.name = "Test native presentation source quad";
            sourceObject.layer = 30;
            sourceObject.transform.position =
                new Vector3(5000f, 5000f, 0f);
            sourceObject.transform.localScale = new Vector3(
                camera.orthographicSize * 2f * camera.aspect,
                camera.orthographicSize * 2f,
                1f);
            sourceObject.GetComponent<Renderer>().sharedMaterial =
                sourceMaterial;
        }

    }
}
