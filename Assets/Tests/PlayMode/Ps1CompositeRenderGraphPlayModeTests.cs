using System.Collections;
using System.Collections.Generic;
using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
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
        private bool previousDitherEnabled;
        private bool previousScanlinesEnabled;
        private bool previousAspectRatio43Enabled;
        private bool previousBegottenModeEnabled;
        private bool effectFlagsCaptured;

        [SetUp]
        public void CaptureEffectFlags()
        {
            previousDitherEnabled =
                GraphicsEffectsSettings.DitherEnabled;
            previousScanlinesEnabled =
                GraphicsEffectsSettings.ScanlinesEnabled;
            previousAspectRatio43Enabled =
                GraphicsEffectsSettings.AspectRatio43Enabled;
            previousBegottenModeEnabled =
                GraphicsEffectsSettings.BegottenModeEnabled;
            effectFlagsCaptured = true;
            GraphicsEffectsSettings.DitherEnabled = false;
            GraphicsEffectsSettings.ScanlinesEnabled = false;
            GraphicsEffectsSettings.AspectRatio43Enabled = false;
            // The print replaces the whole composite; these tests measure
            // the composite.
            GraphicsEffectsSettings.BegottenModeEnabled = false;
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
            if (effectFlagsCaptured)
            {
                GraphicsEffectsSettings.DitherEnabled =
                    previousDitherEnabled;
                GraphicsEffectsSettings.ScanlinesEnabled =
                    previousScanlinesEnabled;
                GraphicsEffectsSettings.AspectRatio43Enabled =
                    previousAspectRatio43Enabled;
                GraphicsEffectsSettings.BegottenModeEnabled =
                    previousBegottenModeEnabled;
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
            cameraObject = new GameObject("PS1 Composite Smoke Camera");
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
                name = "PS1 Composite Smoke Target",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            Assert.That(target.Create(), Is.True);
            camera.targetTexture = target;

            camera.Render();

            readback = new Texture2D(
                outputWidth,
                outputHeight,
                TextureFormat.RGBA32,
                false,
                true);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            readback.ReadPixels(
                new Rect(0f, 0f, outputWidth, outputHeight),
                0,
                0,
                false);
            readback.Apply(false, false);
            RenderTexture.active = previous;
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
