using System.Collections;
using System.IO;
using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// The Begotten print through the real RenderGraph: a coloured
    /// source comes out as soot and bone with almost nothing between,
    /// the 4:3 gate is forced, a held frame is byte-identical to the
    /// picture before it while a new picture boils, a marked camera keeps
    /// its colour, and <c>TestResults/begotten-sheet.png</c> shows a lit
    /// stage with the hero in colour, in the print by day and by night,
    /// and three consecutive pictures.
    /// </summary>
    public sealed class BegottenFilmRenderGraphPlayModeTests
    {
        private const int Width = 1280;
        private const int Height = 720;
        private const int TileWidth = 640;
        private const int TileHeight = 480;
        private const int SourceLayer = 30;
        private static readonly Vector3 StageOrigin =
            new Vector3(3000f, 0f, 3000f);

        private GameObject cameraObject;
        private GameObject sourceObject;
        private GameObject stageRoot;
        private GameObject playerObject;
        private RenderTexture target;
        private Texture2D sourceTexture;
        private Texture2D readback;
        private Material sourceMaterial;
        private Ps1CompositeRendererFeature feature;

        private bool previousDitherEnabled;
        private bool previousScanlinesEnabled;
        private bool previousAspectRatio43Enabled;
        private bool previousBegottenModeEnabled;
        private bool previousVertexJitterEnabled;
        private bool effectFlagsCaptured;
        private bool previousFogEnabled;
        private FogMode previousFogMode;
        private Color previousFogColor;
        private float previousFogDensity;
        private AmbientMode previousAmbientMode;
        private Color previousAmbientLight;
        private bool renderStateCaptured;

        [SetUp]
        public void CaptureEffectFlags()
        {
            previousDitherEnabled = GraphicsEffectsSettings.DitherEnabled;
            previousScanlinesEnabled =
                GraphicsEffectsSettings.ScanlinesEnabled;
            previousAspectRatio43Enabled =
                GraphicsEffectsSettings.AspectRatio43Enabled;
            previousBegottenModeEnabled =
                GraphicsEffectsSettings.BegottenModeEnabled;
            previousVertexJitterEnabled =
                GraphicsEffectsSettings.VertexJitterEnabled;
            effectFlagsCaptured = true;
            GraphicsEffectsSettings.DitherEnabled = false;
            GraphicsEffectsSettings.ScanlinesEnabled = false;
            GraphicsEffectsSettings.AspectRatio43Enabled = false;
            GraphicsEffectsSettings.VertexJitterEnabled = false;
            GraphicsEffectsSettings.BegottenModeEnabled = true;

            feature = FindFeature();
            feature.DebugForceFilmFrame = null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (feature != null)
            {
                feature.DebugForceFilmFrame = null;
            }

            if (effectFlagsCaptured)
            {
                GraphicsEffectsSettings.DitherEnabled = previousDitherEnabled;
                GraphicsEffectsSettings.ScanlinesEnabled =
                    previousScanlinesEnabled;
                GraphicsEffectsSettings.AspectRatio43Enabled =
                    previousAspectRatio43Enabled;
                GraphicsEffectsSettings.VertexJitterEnabled =
                    previousVertexJitterEnabled;
                GraphicsEffectsSettings.BegottenModeEnabled =
                    previousBegottenModeEnabled;
                effectFlagsCaptured = false;
            }

            if (renderStateCaptured)
            {
                RenderSettings.fog = previousFogEnabled;
                RenderSettings.fogMode = previousFogMode;
                RenderSettings.fogColor = previousFogColor;
                RenderSettings.fogDensity = previousFogDensity;
                RenderSettings.ambientMode = previousAmbientMode;
                RenderSettings.ambientLight = previousAmbientLight;
                renderStateCaptured = false;
            }

            if (playerObject != null)
            {
                Object.Destroy(playerObject);
                playerObject = null;
            }

            if (stageRoot != null)
            {
                Object.Destroy(stageRoot);
                stageRoot = null;
            }

            if (cameraObject != null)
            {
                Object.Destroy(cameraObject);
                cameraObject = null;
            }

            if (sourceObject != null)
            {
                Object.Destroy(sourceObject);
                sourceObject = null;
            }

            if (target != null)
            {
                target.Release();
                Object.Destroy(target);
                target = null;
            }

            if (sourceMaterial != null)
            {
                Object.Destroy(sourceMaterial);
                sourceMaterial = null;
            }

            if (sourceTexture != null)
            {
                Object.Destroy(sourceTexture);
                sourceTexture = null;
            }

            if (readback != null)
            {
                Object.Destroy(readback);
                readback = null;
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Begotten_PrintsOnlySootAndBone()
        {
            RequireGraphicsDevice();
            CreateSourceRig(Width, Height, false);
            yield return WarmUp();
            Render();

            // The central two thirds of the 4:3 window, clear of the
            // vignette: every pixel neutral, almost every pixel either
            // soot or bone, and both present.
            int neutralViolations = 0;
            int dark = 0;
            int bright = 0;
            int total = 0;
            for (int y = 72; y < 648; y += 4)
            {
                for (int x = 352; x < 928; x += 4)
                {
                    Color32 pixel = readback.GetPixel(x, y);
                    if (Mathf.Abs(pixel.r - pixel.g) > 1 ||
                        Mathf.Abs(pixel.g - pixel.b) > 1)
                    {
                        neutralViolations++;
                    }

                    if (pixel.r < 24)
                    {
                        dark++;
                    }
                    else if (pixel.r > 150)
                    {
                        bright++;
                    }

                    total++;
                }
            }

            float bimodal = (dark + bright) / (float)total;
            Debug.Log(
                $"Begotten print: {dark} soot, {bright} bone of {total} " +
                $"samples ({bimodal:P0} without a mid-tone).");
            Assert.That(
                neutralViolations,
                Is.Zero,
                "A coloured source must print neutral.");
            Assert.That(
                bimodal,
                Is.GreaterThanOrEqualTo(0.75f),
                "The print has no mid-tones beyond the boiling edges and " +
                "the rims of local contrast.");
            Assert.That(dark, Is.GreaterThan(0));
            Assert.That(bright, Is.GreaterThan(0));
            yield return null;
        }

        [UnityTest]
        public IEnumerator Begotten_ForcesTheFourThreeFrame()
        {
            RequireGraphicsDevice();
            Assert.That(GraphicsEffectsSettings.AspectRatio43Enabled, Is.False);
            CreateSourceRig(Width, Height, false);
            yield return WarmUp();
            Render();

            int[] barColumns = { 0, 80, 158, 1122, 1200, 1279 };
            for (int index = 0; index < barColumns.Length; index++)
            {
                Color32 bar = readback.GetPixel(barColumns[index], 360);
                Assert.That(
                    (int)bar.r + bar.g + bar.b,
                    Is.Zero,
                    "The film's gate is 1.33:1: the bars stay pure black " +
                    $"at column {barColumns[index]}.");
            }

            // Bytes, not floats: GetPixel returns a Color in [0, 1].
            int bone = 0;
            for (int x = 200; x < 1080; x += 8)
            {
                Color32 pixel = readback.GetPixel(x, 540);
                if (pixel.r > 150)
                {
                    bone++;
                }
            }

            Assert.That(bone, Is.GreaterThan(0), "Bone shows inside the gate.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Begotten_HoldsBetweenTicksAndBoilsOnTicks()
        {
            RequireGraphicsDevice();
            CreateSourceRig(Width, Height, false);

            feature.DebugForceFilmFrame = true;
            yield return WarmUp();
            Render();
            Color32[] first = readback.GetPixels32();
            Render();
            Color32[] second = readback.GetPixels32();
            float boiled = DifferingFraction(first, second);
            Debug.Log($"Begotten boil: {boiled:P1} of the window changed between pictures.");
            Assert.That(
                boiled,
                Is.GreaterThanOrEqualTo(0.03f),
                "A new picture reseeds the grain: the edges boil.");

            feature.DebugForceFilmFrame = false;
            Render();
            Color32[] held = readback.GetPixels32();
            Render();
            Color32[] heldAgain = readback.GetPixels32();
            Assert.That(
                DifferingFraction(second, held),
                Is.Zero,
                "A held frame is the last picture, byte for byte.");
            Assert.That(DifferingFraction(held, heldAgain), Is.Zero);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Begotten_LeavesMarkedCamerasAlone()
        {
            RequireGraphicsDevice();
            CreateSourceRig(Width, Height, true);
            Render();

            // The ordinary composite: no gate, and the coloured tone
            // keeps its colour.
            Color32 leftEdge = readback.GetPixel(80, 360);
            Assert.That(
                (int)leftEdge.r + leftEdge.g + leftEdge.b,
                Is.GreaterThan(0),
                "A marked camera is not pillarboxed.");
            Color32 tone = readback.GetPixel(960, 540);
            Assert.That(
                tone.r - tone.b,
                Is.GreaterThan(60),
                "A marked camera keeps its colour.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Begotten_Sheet()
        {
            RequireGraphicsDevice();
            CreateStage();
            yield return null;
            yield return null;
            yield return null;

            Texture2D sheet = new Texture2D(
                TileWidth * 3,
                TileHeight * 2,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = "Begotten Sheet"
            };
            try
            {
                LightDay();
                GraphicsEffectsSettings.BegottenModeEnabled = false;
                // The first render of a camera draws without its shadow
                // maps; throw it away.
                Render();
                Render();
                CopyTile(sheet, 0, 1);

                GraphicsEffectsSettings.BegottenModeEnabled = true;
                feature.DebugForceFilmFrame = true;
                yield return WarmUp();

                // Four day pictures: one beside the colour tile and three
                // in a row below it. The measure is their median, because
                // any single picture may be the lamp's flash.
                float[] dayBone = new float[4];
                int dayViolations = 0;
                Render();
                dayBone[0] = BoneFraction(out int violations);
                dayViolations += violations;
                LogPicture("day");
                CopyTile(sheet, 1, 1);

                LightNight();
                Render();
                float nightBone = BoneFraction(out int nightViolations);
                LogPicture("night");
                CopyTile(sheet, 2, 1);

                LightDay();
                for (int index = 0; index < 3; index++)
                {
                    Render();
                    dayBone[index + 1] = BoneFraction(out violations);
                    dayViolations += violations;
                    LogPicture("day " + (index + 2));
                    CopyTile(sheet, index, 0);
                }

                sheet.Apply(false, false);
                string directory = Path.GetFullPath(
                    Path.Combine(Application.dataPath, "..", "TestResults"));
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "begotten-sheet.png");
                File.WriteAllBytes(path, sheet.EncodeToPNG());
                System.Array.Sort(dayBone);
                float dayMedian = (dayBone[1] + dayBone[2]) * 0.5f;
                Debug.Log(
                    $"Begotten sheet -> {path}; bone by day " +
                    $"{dayBone[0]:P1}/{dayBone[1]:P1}/{dayBone[2]:P1}/{dayBone[3]:P1} " +
                    $"(median {dayMedian:P1}), by night {nightBone:P1}.");

                Assert.That(File.Exists(path), Is.True);
                Assert.That(dayViolations, Is.Zero);
                Assert.That(nightViolations, Is.Zero);
                Assert.That(
                    dayMedian,
                    Is.InRange(0.10f, 0.90f),
                    "By day the print keeps both soot and bone.");
                Assert.That(
                    nightBone,
                    Is.InRange(0.005f, 0.6f),
                    "By night the threshold follows the scene: the lamp " +
                    "and what it lights stay bone, the rest is soot.");
            }
            finally
            {
                Object.Destroy(sheet);
            }
        }

        private void LogPicture(string label)
        {
            BegottenFilmFrame picture = feature.DebugFilmState;
            Debug.Log(
                $"Begotten picture ({label}): exposure {picture.Exposure:F2}, " +
                $"threshold roll {picture.Threshold:F2}, weave " +
                $"{picture.WeaveInternalPixels.x:F2}/{picture.WeaveInternalPixels.y:F2}, " +
                $"slip {picture.SlipPixels:F1}, scratches {picture.ActiveScratchCount}.");
        }

        /// <summary>
        /// Renders until at least a twentieth of the frame is bone, up to
        /// thirty frames. The editor compiles a shader pass the first
        /// time it is drawn, and until it is ready the draw is skipped -
        /// the first picture after a shader edit is a black texture with
        /// white dust specks on it, so a single bright pixel proves
        /// nothing.
        /// </summary>
        private IEnumerator WarmUp()
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                Render();
                Color32[] pixels = readback.GetPixels32();
                int bone = 0;
                int sampled = 0;
                for (int index = 0; index < pixels.Length; index += 97)
                {
                    sampled++;
                    if (pixels[index].r > 150)
                    {
                        bone++;
                    }
                }

                if (bone >= sampled / 20)
                {
                    yield break;
                }

                yield return null;
            }

            Debug.LogWarning("Begotten warm-up never printed bone.");
        }

        private static void RequireGraphicsDevice()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore(
                    "Actual RenderGraph output requires a graphics device.");
            }
        }

        private static Ps1CompositeRendererFeature FindFeature()
        {
            Ps1CompositeRendererFeature[] features =
                Resources.FindObjectsOfTypeAll<Ps1CompositeRendererFeature>();
            for (int index = 0; index < features.Length; index++)
            {
                if (features[index] != null && features[index].isActive)
                {
                    return features[index];
                }
            }

            Assert.Fail("The PC renderer's PS1 composite feature is not loaded.");
            return null;
        }

        private Camera CreateCamera(
            int outputWidth,
            int outputHeight,
            bool marked)
        {
            cameraObject = new GameObject("Begotten Test Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.allowDynamicResolution = false;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 60f;
            UniversalAdditionalCameraData cameraData =
                camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            cameraData.renderShadows = true;
            cameraData.volumeLayerMask = 0;
            if (marked)
            {
                cameraObject.AddComponent<Ps1VertexJitterExclusion>();
            }

            target = new RenderTexture(
                outputWidth,
                outputHeight,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear)
            {
                name = "Begotten Test Target",
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
            return camera;
        }

        private void CreateSourceRig(
            int outputWidth,
            int outputHeight,
            bool marked)
        {
            Camera camera = CreateCamera(outputWidth, outputHeight, marked);
            camera.cullingMask = 1 << SourceLayer;
            camera.orthographic = true;
            camera.orthographicSize = 1f;
            camera.aspect = outputWidth / (float)outputHeight;
            camera.transform.position = new Vector3(5000f, 5000f, -2f);
            CaptureRenderState();
            RenderSettings.fog = false;

            // Left half a one-pixel checker, right half two flat
            // coloured tones: a warm bright one below, a cold dark one
            // above.
            sourceTexture = new Texture2D(
                outputWidth,
                outputHeight,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = "Begotten Test Source",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            Color32[] pixels = new Color32[outputWidth * outputHeight];
            Color32 darkTone = new Color32(40, 20, 60, 255);
            Color32 brightTone = new Color32(228, 120, 40, 255);
            for (int y = 0; y < outputHeight; y++)
            {
                for (int x = 0; x < outputWidth; x++)
                {
                    if (x < outputWidth / 2)
                    {
                        bool white = (x + y) % 2 == 0;
                        pixels[y * outputWidth + x] = white
                            ? new Color32(255, 255, 255, 255)
                            : new Color32(0, 0, 0, 255);
                    }
                    else
                    {
                        pixels[y * outputWidth + x] =
                            y < outputHeight / 2 ? darkTone : brightTone;
                    }
                }
            }

            sourceTexture.SetPixels32(pixels);
            sourceTexture.Apply(false, true);

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);
            sourceMaterial = new Material(shader)
            {
                name = "Begotten Test Source Material",
                hideFlags = HideFlags.DontSave
            };
            sourceMaterial.SetTexture("_BaseMap", sourceTexture);
            sourceMaterial.SetColor("_BaseColor", Color.white);
            sourceMaterial.SetFloat("_Cull", 0f);

            sourceObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            sourceObject.name = "Begotten Test Source Quad";
            sourceObject.layer = SourceLayer;
            sourceObject.transform.position = new Vector3(5000f, 5000f, 0f);
            sourceObject.transform.localScale = new Vector3(
                camera.orthographicSize * 2f * camera.aspect,
                camera.orthographicSize * 2f,
                1f);
            sourceObject.GetComponent<Renderer>().sharedMaterial =
                sourceMaterial;
        }

        private void CreateStage()
        {
            Camera camera = CreateCamera(TileWidth, TileHeight, false);
            camera.cullingMask = ~0;
            camera.fieldOfView = 50f;
            camera.transform.position =
                StageOrigin + new Vector3(3.2f, 1.6f, 3.4f);
            camera.transform.LookAt(StageOrigin + new Vector3(0f, 0.9f, 0f));
            CaptureRenderState();

            stageRoot = new GameObject("Begotten Stage");
            AddBlock("Ground", new Vector3(0f, -0.1f, 0f), new Vector3(40f, 0.2f, 40f));
            AddBlock("Wall", new Vector3(0f, 1.5f, -4f), new Vector3(7f, 3f, 0.3f));
            AddBlock("Column", new Vector3(2f, 1.5f, 1f), new Vector3(0.5f, 3f, 0.5f));
            AddBlock("Block", new Vector3(-2f, 0.4f, 0.5f), new Vector3(1.2f, 0.8f, 1.2f));
            AddBlock("Far", new Vector3(-6f, 1f, -9f), new Vector3(3f, 2f, 3f));

            GameObject sunObject = new GameObject("Begotten Sun");
            sunObject.transform.SetParent(stageRoot.transform, false);
            Light sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Hard;
            sunObject.transform.rotation = Quaternion.Euler(40f, 210f, 0f);

            GameObject lampObject = new GameObject("Begotten Lamp");
            lampObject.transform.SetParent(stageRoot.transform, false);
            lampObject.transform.position = StageOrigin + new Vector3(1.2f, 2.2f, 1.0f);
            Light lamp = lampObject.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.range = 6f;
            lamp.color = new Color(1f, 0.85f, 0.6f);
            lamp.shadows = LightShadows.None;

            PlayerRuntime hero = PlayerFactory.Create(
                null,
                StageOrigin + Vector3.up * PlayerFactory.GroundedRootOffset,
                camera,
                null,
                null);
            playerObject = hero.GameObject;
            playerObject.transform.rotation = Quaternion.Euler(0f, 35f, 0f);
            Physics.SyncTransforms();
        }

        private void AddBlock(string name, Vector3 position, Vector3 scale)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "Begotten " + name;
            block.transform.SetParent(stageRoot.transform, false);
            block.transform.position = StageOrigin + position;
            block.transform.localScale = scale;
        }

        private void LightDay()
        {
            Light sun = stageRoot.transform.Find("Begotten Sun").GetComponent<Light>();
            sun.intensity = 1.4f;
            sun.color = new Color(1f, 0.95f, 0.88f);
            Light lamp = stageRoot.transform.Find("Begotten Lamp").GetComponent<Light>();
            lamp.intensity = 0f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.36f, 0.38f, 0.4f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.55f, 0.58f, 0.6f);
            RenderSettings.fogDensity = 0.04f;
            cameraObject.GetComponent<Camera>().backgroundColor =
                RenderSettings.fogColor;
        }

        private void LightNight()
        {
            Light sun = stageRoot.transform.Find("Begotten Sun").GetComponent<Light>();
            sun.intensity = 0.12f;
            sun.color = new Color(0.6f, 0.7f, 1f);
            Light lamp = stageRoot.transform.Find("Begotten Lamp").GetComponent<Light>();
            lamp.intensity = 4f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.03f, 0.035f, 0.05f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.05f, 0.06f, 0.08f);
            RenderSettings.fogDensity = 0.06f;
            cameraObject.GetComponent<Camera>().backgroundColor =
                RenderSettings.fogColor;
        }

        private void CaptureRenderState()
        {
            if (renderStateCaptured)
            {
                return;
            }

            previousFogEnabled = RenderSettings.fog;
            previousFogMode = RenderSettings.fogMode;
            previousFogColor = RenderSettings.fogColor;
            previousFogDensity = RenderSettings.fogDensity;
            previousAmbientMode = RenderSettings.ambientMode;
            previousAmbientLight = RenderSettings.ambientLight;
            renderStateCaptured = true;
        }

        private void Render()
        {
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            readback.ReadPixels(
                new Rect(0f, 0f, target.width, target.height),
                0,
                0,
                false);
            readback.Apply(false, false);
            RenderTexture.active = previous;
        }

        private void CopyTile(Texture2D sheet, int column, int row)
        {
            sheet.SetPixels32(
                column * TileWidth,
                row * TileHeight,
                TileWidth,
                TileHeight,
                readback.GetPixels32());
        }

        private float BoneFraction(out int neutralViolations)
        {
            Color32[] pixels = readback.GetPixels32();
            int bone = 0;
            neutralViolations = 0;
            for (int index = 0; index < pixels.Length; index++)
            {
                Color32 pixel = pixels[index];
                if (Mathf.Abs(pixel.r - pixel.g) > 1 ||
                    Mathf.Abs(pixel.g - pixel.b) > 1)
                {
                    neutralViolations++;
                }

                if (pixel.r > 150)
                {
                    bone++;
                }
            }

            return bone / (float)pixels.Length;
        }

        /// <summary>Fraction of pixels inside the 4:3 window that differ
        /// between two readbacks of the same size.</summary>
        private static float DifferingFraction(Color32[] first, Color32[] second)
        {
            int differing = 0;
            int counted = 0;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 160; x < 1120; x++)
                {
                    int index = y * Width + x;
                    Color32 a = first[index];
                    Color32 b = second[index];
                    if (a.r != b.r || a.g != b.g || a.b != b.b)
                    {
                        differing++;
                    }

                    counted++;
                }
            }

            return differing / (float)counted;
        }
    }
}
