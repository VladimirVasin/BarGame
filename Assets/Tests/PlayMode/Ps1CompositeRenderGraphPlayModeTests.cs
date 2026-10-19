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
    public sealed class Ps1CompositeRenderGraphPlayModeTests
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

        [SetUp]
        public void CaptureEffectFlags()
        {
            foreach (string key in new[]
            {
                "graphics.style", "graphics.clean_aa", "graphics.dither", "graphics.scanlines",
                "graphics.aspect_4_3", "graphics.begotten", "graphics.vertex_jitter"
            })
                savedPreferences[key] = PlayerPrefs.HasKey(key)
                    ? PlayerPrefs.GetInt(key) : (int?)null;
            effectFlagsCaptured = true;
            GraphicsEffectsSettings.Style = GraphicsStyle.Ps1;
            GraphicsEffectsSettings.CleanAntialiasingEnabled = false;
            GraphicsEffectsSettings.DitherEnabled = false;
            GraphicsEffectsSettings.ScanlinesEnabled = false;
            GraphicsEffectsSettings.AspectRatio43Enabled = false;
            // The print replaces the whole composite; these tests measure
            // the composite.
            GraphicsEffectsSettings.BegottenModeEnabled = false;
        }

        [UnityTest]
        public IEnumerator GameCamera_StylesSwitchImmediatelyAndRetainSharedEffects()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Actual RenderGraph output requires a graphics device.");

            RenderComposite(1280, 720);
            Camera camera = cameraObject.GetComponent<Camera>();
            Ps1CompositeRendererFeature feature = FindFeature();
            bool? previousFilmFrame = feature.DebugForceFilmFrame;
            float? previousFilmWeight = BegottenModeRamp.DebugWeightOverride;
            Color32[] ps1 = readback.GetPixels32();
            int frame = Time.frameCount;
            try
            {
                GraphicsEffectsSettings.Style = GraphicsStyle.CleanLowPoly;
                RenderAgain(camera);
                Color32[] clean = readback.GetPixels32();
                Assert.That(ChangedSamples(ps1, clean), Is.GreaterThan(100));
                Assert.That(CheckerContrast(), Is.GreaterThan(.8f),
                    "Clean Low Poly must retain the native one-pixel checker.");
                Assert.That(((Color32)readback.GetPixel(960, 180)).r, Is.EqualTo(20).Within(1));
                Assert.That(((Color32)readback.GetPixel(960, 540)).r, Is.EqualTo(228).Within(1));

                GraphicsEffectsSettings.Style = GraphicsStyle.Ps1;
                RenderAgain(camera);
                CollectionAssert.AreEqual(ps1, readback.GetPixels32(),
                    "Returning to PS1 in the same frame must preserve its original rendering.");
                Assert.That(Time.frameCount, Is.EqualTo(frame),
                    "The mode transitions above must exercise the same-frame render path.");

                Color32[] filmReference = null;
                foreach (GraphicsStyle style in new[]
                {
                    GraphicsStyle.Ps1, GraphicsStyle.CleanLowPoly
                })
                {
                    GraphicsEffectsSettings.Style = style;
                    GraphicsEffectsSettings.VertexJitterEnabled = true;
                    GraphicsEffectsSettings.ScanlinesEnabled = true;
                    RenderAgain(camera);
                    float snap = Shader.GetGlobalVector("_Ps1VertexSnapParams").z;
                    if (style == GraphicsStyle.Ps1) Assert.That(snap, Is.GreaterThan(0f));
                    else Assert.That(snap, Is.Zero, "Only PS1 may snap projected vertices.");
                    if (style != GraphicsStyle.Ps1)
                        Assert.That(readback.GetPixel(960, 500), Is.EqualTo(readback.GetPixel(960, 501)),
                            "The retained scanline preference must not affect the other styles.");

                    var exclusion = cameraObject.AddComponent<Ps1VertexJitterExclusion>();
                    RenderAgain(camera);
                    Assert.That(Shader.GetGlobalVector("_Ps1VertexSnapParams").z, Is.Zero,
                        "An excluded camera must clear the preceding camera's snap globals.");
                    Object.DestroyImmediate(exclusion);
                    GraphicsEffectsSettings.VertexJitterEnabled = false;
                    GraphicsEffectsSettings.ScanlinesEnabled = false;

                    RenderAgain(camera);
                    Color32[] sober = readback.GetPixels32();
                    IntoxicationRenderState.Set(IntoxicationStageRules.Evaluate(100),
                        1.25f, 0f, Vector2.zero, Vector3.zero);
                    RenderAgain(camera);
                    Assert.That(ChangedSamples(sober, readback.GetPixels32()), Is.GreaterThan(100),
                        "Intoxication must remain visible in every presentation style.");
                    IntoxicationRenderState.Clear();

                    GraphicsEffectsSettings.AspectRatio43Enabled = true;
                    RenderAgain(camera);
                    AssertPillarbox();
                    GraphicsEffectsSettings.AspectRatio43Enabled = false;

                    // The authored film replaces each base style and retains
                    // its own resolution, rather than inheriting the native one.
                    sourceMaterial.SetColor("_BaseColor", new Color(.5f, .75f, 1f));
                    GraphicsEffectsSettings.CleanAntialiasingEnabled = true;
                    GraphicsEffectsSettings.BegottenModeEnabled = true;
                    BegottenModeRamp.DebugWeightOverride = 1f;
                    feature.DebugForceFilmFrame = true;
                    feature.DebugResetProjector();
                    RenderAgain(camera);
                    Assert.That(feature.DebugCleanSmaaApplied, Is.False,
                        "The full film must skip SMAA, independently of its base style.");
                    AssertPillarbox();
                    Color32[] film = readback.GetPixels32();
                    for (int y = 96; y < 624; y += 32)
                    {
                        Color32 pixel = readback.GetPixel(960, y);
                        Assert.That(Mathf.Abs(pixel.r - pixel.g) + Mathf.Abs(pixel.g - pixel.b),
                            Is.LessThanOrEqualTo(2), "BEGOTTEN must remove the source's colour.");
                    }
                    if (filmReference == null) filmReference = film;
                    else CollectionAssert.AreEqual(filmReference, film,
                        "The same held film frame must be independent of its base style.");
                    GraphicsEffectsSettings.BegottenModeEnabled = false;
                    GraphicsEffectsSettings.CleanAntialiasingEnabled = false;
                    BegottenModeRamp.DebugWeightOverride = 0f;
                    sourceMaterial.SetColor("_BaseColor", Color.white);
                }
                yield return AssertCleanAntialiasing(camera, feature);
            }
            finally
            {
                IntoxicationRenderState.Clear();
                feature.DebugForceFilmFrame = previousFilmFrame;
                BegottenModeRamp.DebugWeightOverride = previousFilmWeight;
            }

            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-bp-style-capture") >= 0)
                yield return CaptureWarehouseStyleComparison();
            yield return null;
        }

        [UnityTest]
        public IEnumerator GameCamera_AveragesDetailAndPreservesTones()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore(
                    "Actual RenderGraph output requires a graphics device.");
            }

            RenderComposite(1280, 720);

            const int sampleOriginX = 320;
            const int sampleOriginY = 176;
            const int blockSize = 2;
            const int blockCount = 8;
            HashSet<uint> colors = new HashSet<uint>();

            for (int blockY = 0; blockY < blockCount; blockY++)
            {
                for (int blockX = 0; blockX < blockCount; blockX++)
                {
                    int x = sampleOriginX + blockX * blockSize;
                    int y = sampleOriginY + blockY * blockSize;
                    Color32 reference = readback.GetPixel(x, y);
                    Color32 right = readback.GetPixel(x + 1, y);
                    Color32 up = readback.GetPixel(x, y + 1);
                    Color32 diagonal = readback.GetPixel(x + 1, y + 1);

                    Assert.That(
                        right,
                        Is.EqualTo(reference),
                        "Point upscale must duplicate each low-resolution " +
                        "texel horizontally.");
                    Assert.That(
                        up,
                        Is.EqualTo(reference),
                        "Point upscale must duplicate each low-resolution " +
                        "texel vertically.");
                    Assert.That(
                        diagonal,
                        Is.EqualTo(reference));

                    AssertAveragedChecker(reference);
                    colors.Add(Pack(reference));
                }
            }

            Assert.That(
                colors.Count,
                Is.EqualTo(1),
                "The averaged checker must stay flat without a visible " +
                "screen-space dither pattern.");

            AssertToneMatchesPerceptualBlend(
                readback.GetPixel(960, 180),
                20);
            AssertToneMatchesPerceptualBlend(
                readback.GetPixel(960, 540),
                228);

            yield return null;
        }

        [UnityTest]
        public IEnumerator GameCamera_DitherSplitsFlatDarkTone()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore(
                    "Actual RenderGraph output requires a graphics device.");
            }

            GraphicsEffectsSettings.DitherEnabled = true;

            RenderComposite(1280, 720);

            // A 4x4-internal-texel block covers every Bayer cell. The
            // flat 20-byte tone sits 0.118 of a step above its RGB555
            // level, so at strength 0.6 five of the sixteen thresholds
            // push it down one level. (The mid-gray averaged checker
            // sits 0.298 above its level — outside the +-0.28 dither
            // amplitude — which is why that region must NOT be used
            // here.)
            HashSet<uint> blockColors = new HashSet<uint>();
            for (int blockY = 0; blockY < 4; blockY++)
            {
                for (int blockX = 0; blockX < 4; blockX++)
                {
                    blockColors.Add(Pack(readback.GetPixel(
                        900 + blockX * 2,
                        176 + blockY * 2)));
                }
            }

            Assert.That(
                blockColors.Count,
                Is.GreaterThan(1),
                "Enabled dithering must break a flat dark tone into " +
                "more than one quantized color across a Bayer block.");

            yield return null;
        }

        [UnityTest]
        public IEnumerator GameCamera_AspectRatio43PillarboxesTheFrame()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore(
                    "Actual RenderGraph output requires a graphics device.");
            }

            GraphicsEffectsSettings.AspectRatio43Enabled = true;

            RenderComposite(1280, 720);

            // At 1280x720 the 4:3 window keeps the central 960
            // columns; 160-column bars flank it and stay pure black.
            int[] barColumns = { 0, 80, 158, 1122, 1200, 1279 };
            for (int index = 0; index < barColumns.Length; index++)
            {
                Color32 bar = readback.GetPixel(
                    barColumns[index],
                    360);
                Assert.That(
                    (int)bar.r + bar.g + bar.b,
                    Is.Zero,
                    "The pillarbox bars must stay pure black at " +
                    $"column {barColumns[index]}.");
            }

            // Just inside the frame the averaged checker still shows.
            Color32 inside = readback.GetPixel(200, 500);
            Assert.That((int)inside.r, Is.InRange(60, 200));

            // The crop and the pillarbox cancel over the visible
            // region, so the flat tones keep the exact perceptual
            // RGB555 blend at their original screen positions.
            AssertToneMatchesPerceptualBlend(
                readback.GetPixel(960, 180),
                20);
            AssertToneMatchesPerceptualBlend(
                readback.GetPixel(960, 540),
                228);

            yield return null;
        }

        [UnityTest]
        public IEnumerator GameCamera_ScanlinesDarkenAlternateRows()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore(
                    "Actual RenderGraph output requires a graphics device.");
            }

            GraphicsEffectsSettings.ScanlinesEnabled = true;

            RenderComposite(1280, 720);

            // At the 2x vertical scale every internal row spans two
            // output rows and the scanline mask darkens exactly one of
            // them, so alternate output rows must differ in the flat
            // bright region.
            float evenSum = 0f;
            float oddSum = 0f;
            for (int y = 500; y < 532; y++)
            {
                Color pixel = readback.GetPixel(960, y);
                float luminance = pixel.r + pixel.g + pixel.b;
                if (y % 2 == 0)
                {
                    evenSum += luminance;
                }
                else
                {
                    oddSum += luminance;
                }
            }

            float brighter = Mathf.Max(evenSum, oddSum);
            float darker = Mathf.Min(evenSum, oddSum);
            Assert.That(brighter, Is.GreaterThan(0f));
            Assert.That(
                darker / brighter,
                Is.LessThan(0.92f),
                "Enabled scanlines must darken one output row of every " +
                "internal row pair.");

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

        private IEnumerator AssertCleanAntialiasing(Camera camera, Ps1CompositeRendererFeature feature)
        {
            GraphicsEffectsSettings.Style = GraphicsStyle.CleanLowPoly;
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

            var excluded = cameraObject.AddComponent<Ps1VertexJitterExclusion>();
            RenderAgain(camera);
            Assert.That(feature.DebugCleanSmaaApplied, Is.False);
            CollectionAssert.AreEqual(unfiltered, readback.GetPixels32());
            Object.DestroyImmediate(excluded);

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
            GraphicsEffectsSettings.BegottenModeEnabled = true;
            BegottenModeRamp.DebugWeightOverride = .5f;
            RenderAgain(camera);
            Assert.That(feature.DebugCleanSmaaApplied, Is.True,
                "A partial film must keep smoothing the live colour, before the print is overlaid.");
            GraphicsEffectsSettings.BegottenModeEnabled = false;
            BegottenModeRamp.DebugWeightOverride = 0f;
            GraphicsEffectsSettings.Style = GraphicsStyle.Ps1;
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
            foreach (int x in new[] { 0, 80, 158, 1122, 1200, 1279 })
            {
                Color32 pixel = readback.GetPixel(x, 360);
                Assert.That((int)pixel.r + pixel.g + pixel.b, Is.Zero,
                    "The shared 4:3 frame must keep its black pillarbox.");
            }
            int content = 0;
            for (int x = 480; x <= 960; x += 80)
            {
                Color32 pixel = readback.GetPixel(x, 540);
                content += pixel.r + pixel.g + pixel.b;
            }
            Assert.That(content, Is.GreaterThan(0), "The visible frame must retain its image.");
        }

        private static Ps1CompositeRendererFeature FindFeature()
        {
            foreach (Ps1CompositeRendererFeature feature in
                Resources.FindObjectsOfTypeAll<Ps1CompositeRendererFeature>())
                if (feature != null && feature.isActive) return feature;
            Assert.Fail("The PC renderer's presentation feature is not loaded.");
            return null;
        }

        private IEnumerator CaptureWarehouseStyleComparison()
        {
            bool previousArena = CombatTestRoot.UseLegacyArenaForTests;
            int? previousSeed = CombatTestRoot.WarehouseSeedForTests;
            float previousCaptureDelta = Time.captureDeltaTime;
            bool previousBackground = Application.runInBackground;
            CombatTestRoot root = null;
            try
            {
                Application.runInBackground = true;
                Time.captureDeltaTime = 1f / 60f;
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = 0;
                GameSessionState.BeginNewGame();
                warehouseCaptureLoaded = true;
                yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest, LoadSceneMode.Single);
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                Assert.That(root != null && root.IsInitialized && root.WarehousePlan != null, Is.True);
                root.AutomaticSimulation = false;
                root.AutomaticOpponentDecisions = false;
                root.SetDuelLogging(false);
                root.SetSparring(false);
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                for (int frame = 0; frame < 12; frame++) yield return null;
                Camera camera = root.CameraFollow.Camera;
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    GraphicsEffectsSettings.DitherEnabled = true;
                    GraphicsEffectsSettings.ScanlinesEnabled = true;
                    GraphicsEffectsSettings.VertexJitterEnabled = false;
                    GraphicsEffectsSettings.Style = GraphicsStyle.Ps1;
                    AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.CombatTest,
                        "Test warehouse presentation - PS1");
                    GraphicsEffectsSettings.Style = GraphicsStyle.CleanLowPoly;
                    GraphicsEffectsSettings.CleanAntialiasingEnabled = false;
                    AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.CombatTest,
                        "Test warehouse presentation - Clean without smoothing");
                    GraphicsEffectsSettings.CleanAntialiasingEnabled = true;
                    AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.CombatTest,
                        "Test warehouse presentation - Clean with smoothing");
                    Quaternion rotation = camera.transform.rotation;
                    camera.transform.Rotate(Vector3.up, .25f, Space.World);
                    AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.CombatTest,
                        "Test warehouse presentation - Clean camera turn");
                    camera.transform.rotation = rotation;
                    Time.captureDeltaTime = 0f;
                    foreach (bool antialiasing in new[] { false, true })
                    {
                        GraphicsEffectsSettings.CleanAntialiasingEnabled = antialiasing;
                        Assert.That(RuntimePerformanceCapture.StartCapture(new PerformanceCaptureOptions(
                            SceneIds.CombatTest, antialiasing ? "Test clean smoothing on" : "Test clean smoothing off",
                            .5, 2), System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(),
                            "Captures", "CleanPerformance")), Is.True);
                        double deadline = Time.realtimeSinceStartupAsDouble + 8d;
                        while (RuntimePerformanceCapture.IsRunning && Time.realtimeSinceStartupAsDouble < deadline)
                            yield return null;
                        Assert.That(RuntimePerformanceCapture.IsRunning, Is.False,
                            "The bounded comparison must finish in its capture window.");
                        Assert.That(RuntimePerformanceCapture.LastReportPath, Is.Not.Empty);
                    }
                }
            }
            finally
            {
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
                GameSessionState.BeginNewGame();
                RetroAudioService.Instance?.StopAll();
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
                name = "PS1 Composite Test Source",
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
                name = "PS1 Composite Test Source Material",
                hideFlags = HideFlags.DontSave
            };
            sourceMaterial.SetTexture("_BaseMap", sourceTexture);
            sourceMaterial.SetColor("_BaseColor", Color.white);
            sourceMaterial.SetFloat("_Cull", 0f);

            sourceObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            sourceObject.name = "PS1 Composite Test Source Quad";
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

        private static void AssertAveragedChecker(Color32 color)
        {
            Assert.That(Mathf.Abs(color.r - color.g), Is.LessThanOrEqualTo(1));
            Assert.That(Mathf.Abs(color.g - color.b), Is.LessThanOrEqualTo(1));
            Assert.That(
                color.r,
                Is.InRange(118, 138),
                "The four-tap downsample must average one-pixel black/white " +
                "detail instead of aliasing it.");
        }

        private static void AssertToneMatchesPerceptualBlend(
            Color32 color,
            byte sourceByte)
        {
            Ps1PresentationSettings profile =
                Resources.Load<Ps1PresentationSettings>(
                    "Rendering/Ps1PresentationProfile");
            Assert.That(profile, Is.Not.Null);

            float sourceLinear = sourceByte / 255f;
            float perceptual = Mathf.LinearToGammaSpace(sourceLinear);
            float quantizedPerceptual =
                Mathf.Round(perceptual * 31f) / 31f;
            float quantizedLinear =
                Mathf.GammaToLinearSpace(quantizedPerceptual);
            int expectedByte = Mathf.RoundToInt(
                Mathf.Lerp(
                    sourceLinear,
                    quantizedLinear,
                    profile.QuantizationStrength) *
                255f);

            Assert.That(Mathf.Abs(color.r - color.g), Is.LessThanOrEqualTo(1));
            Assert.That(Mathf.Abs(color.g - color.b), Is.LessThanOrEqualTo(1));
            Assert.That(
                (int)color.r,
                Is.EqualTo(expectedByte).Within(1),
                $"Tone {sourceByte} must use perceptual RGB555 blended at " +
                "the profile strength.");
        }

        private static uint Pack(Color32 color)
        {
            return (uint)(color.r << 24 |
                          color.g << 16 |
                          color.b << 8 |
                          color.a);
        }
    }
}
