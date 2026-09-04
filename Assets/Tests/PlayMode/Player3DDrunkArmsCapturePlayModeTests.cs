using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// The drunk arms, seen from the front, written to
    /// <c>TestResults/drunk-arms-sheet.png</c>: a sober hero, a level-60
    /// hero at the balance threshold, a blind-drunk hero standing, and a
    /// blind-drunk hero at his most unstable frame. A balancing man holds
    /// his arms out; a man hugging his own ribs is the bug this sheet was
    /// made to catch, and no angle assertion can tell the two apart as
    /// quickly as a front view.
    /// </summary>
    public sealed class Player3DDrunkArmsCapturePlayModeTests
    {
        private const int TileSize = 512;
        private const float PinnedFrameSeconds = 1f / 60f;
        private const int CaptureLayer = 28;
        private const int TestCitySeed = 4243;

        private GameObject groundObject;
        private GameObject cameraObject;
        private GameObject lightObject;
        private GameObject playerObject;
        private GameObject uiObject;
        private RenderTexture renderTarget;
        private Texture2D sheet;
        private AmbientMode previousAmbientMode;
        private Color previousAmbientLight;
        private Light previousSun;
        private bool previousFog;
        private bool renderSettingsCaptured;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.captureDeltaTime = PinnedFrameSeconds;
            ResetSession();
            GameSessionState.SetCitySeed(TestCitySeed);

            previousAmbientMode = RenderSettings.ambientMode;
            previousAmbientLight = RenderSettings.ambientLight;
            previousSun = RenderSettings.sun;
            previousFog = RenderSettings.fog;
            renderSettingsCaptured = true;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.52f, 0.54f, 0.58f);
            RenderSettings.fog = false;

            groundObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            groundObject.name = "Drunk Arms Capture Ground";
            groundObject.transform.position = new Vector3(0f, -0.1f, 0f);
            groundObject.transform.localScale = new Vector3(12f, 0.2f, 12f);
            groundObject.layer = CaptureLayer;

            cameraObject = new GameObject("Drunk Arms Capture Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.aspect = 1f;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 30f;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.cullingMask = 1 << CaptureLayer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.20f, 0.22f, 0.26f);
            UniversalAdditionalCameraData cameraData =
                camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            cameraData.renderShadows = true;
            cameraData.volumeLayerMask = 0;

            renderTarget = new RenderTexture(
                TileSize,
                TileSize,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear)
            {
                name = "Drunk Arms Capture Target",
                antiAliasing = 1
            };
            renderTarget.Create();
            camera.targetTexture = renderTarget;

            lightObject = new GameObject("Drunk Arms Capture Key Light");
            Light keyLight = lightObject.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 1.6f;
            keyLight.color = new Color(1f, 0.86f, 0.74f);
            keyLight.shadows = LightShadows.Hard;
            keyLight.cullingMask = 1 << CaptureLayer;
            lightObject.transform.rotation = Quaternion.Euler(48f, 145f, 0f);
            RenderSettings.sun = keyLight;

            sheet = new Texture2D(
                TileSize * 2,
                TileSize * 2,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = "Drunk Arms Sheet",
                filterMode = FilterMode.Point
            };
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            DestroyHero();
            if (renderSettingsCaptured)
            {
                RenderSettings.ambientMode = previousAmbientMode;
                RenderSettings.ambientLight = previousAmbientLight;
                RenderSettings.sun = previousSun;
                RenderSettings.fog = previousFog;
                renderSettingsCaptured = false;
            }

            if (renderTarget != null)
            {
                renderTarget.Release();
                Object.Destroy(renderTarget);
            }

            if (sheet != null)
            {
                Object.Destroy(sheet);
            }

            if (lightObject != null)
            {
                Object.Destroy(lightObject);
            }

            if (cameraObject != null)
            {
                Object.Destroy(cameraObject);
            }

            if (groundObject != null)
            {
                Object.Destroy(groundObject);
            }

            ResetSession();
            Time.captureDeltaTime = 0f;
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator DrunkArms_RenderFrontSheet()
        {
            Camera camera = cameraObject.GetComponent<Camera>();
            int[] foreground = new int[4];
            float[] handSpan = new float[4];

            // Tile 0: sober, standing.
            PlayerRuntime hero = CreateHero();
            var presentation = (Player3DCharacterPresentation)hero.Visual;
            yield return Frames(45);
            presentation.ReapplyLatePresentationPose();
            handSpan[0] = HandSpan(presentation);
            foreground[0] = CaptureTile(camera, presentation, 0);
            DestroyHero();
            yield return null;

            // Tile 1: level 60, the balance threshold, standing.
            hero = CreateHero();
            presentation = (Player3DCharacterPresentation)hero.Visual;
            IntoxicationStatusController status = CreateStatus(hero, 60);
            status.Balance.ArmGrace(60f);
            yield return Frames(3 * 60);
            presentation.ReapplyLatePresentationPose();
            handSpan[1] = HandSpan(presentation);
            foreground[1] = CaptureTile(camera, presentation, 1);
            Debug.Log(
                $"Drunk arms sheet: level 60 tile, arm reaction " +
                $"{presentation.BalancePose.ArmReaction:F2}, " +
                $"intoxication {presentation.IntoxicationAmount:F2}, " +
                $"hand span {handSpan[1]:F3} m");
            DestroyHero();
            yield return null;

            // Tile 2: blind drunk, standing, two seconds in.
            hero = CreateHero();
            presentation = (Player3DCharacterPresentation)hero.Visual;
            status = CreateStatus(hero, 100);
            status.Balance.ArmGrace(60f);
            yield return Frames(2 * 60);
            presentation.ReapplyLatePresentationPose();
            handSpan[2] = HandSpan(presentation);
            foreground[2] = CaptureTile(camera, presentation, 2);
            Debug.Log(
                $"Drunk arms sheet: level 100 tile, arm reaction " +
                $"{presentation.BalancePose.ArmReaction:F2}, " +
                $"lean {presentation.BalancePose.LeanRollDegrees:F1} deg, " +
                $"hand span {handSpan[2]:F3} m");

            // Tile 3: the same hero on the first frame of the next eight
            // seconds where the model's reaction passes 0.6 — or, if it
            // stays calmer than that, after the full eight seconds.
            int framesWaited = 0;
            for (int frame = 0; frame < 8 * 60; frame++)
            {
                yield return null;
                framesWaited++;
                if (presentation.BalancePose.ArmReaction > 0.6f)
                {
                    break;
                }
            }

            presentation.ReapplyLatePresentationPose();
            handSpan[3] = HandSpan(presentation);
            foreground[3] = CaptureTile(camera, presentation, 3);
            Debug.Log(
                $"Drunk arms sheet: later tile after {framesWaited} frames, " +
                $"arm reaction {presentation.BalancePose.ArmReaction:F2}, " +
                $"lean {presentation.BalancePose.LeanRollDegrees:F1} deg, " +
                $"hand span {handSpan[3]:F3} m");
            DestroyHero();
            yield return null;

            sheet.Apply(false, false);
            string outputDirectory = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "TestResults"));
            Directory.CreateDirectory(outputDirectory);
            string outputPath = Path.Combine(outputDirectory, "drunk-arms-sheet.png");
            File.WriteAllBytes(outputPath, sheet.EncodeToPNG());
            Debug.Log(
                $"Drunk arms sheet: hand spans sober {handSpan[0]:F3}, " +
                $"level 60 {handSpan[1]:F3}, level 100 {handSpan[2]:F3}, " +
                $"unstable {handSpan[3]:F3} m -> {outputPath}");

            Assert.That(File.Exists(outputPath), Is.True);
            Assert.That(new FileInfo(outputPath).Length, Is.GreaterThan(4096));
            for (int index = 0; index < foreground.Length; index++)
            {
                Assert.That(
                    foreground[index],
                    Is.GreaterThan(900),
                    $"Tile {index} produced no substantial GPU-visible model.");
            }

            // The sheet's own numbers: the hands must go OUT with the
            // level, never in. Before the fix they closed from 0.515 m
            // sober to 0.395 m blind drunk.
            Assert.That(
                handSpan[1],
                Is.GreaterThan(handSpan[0] + 0.05f),
                "At the balance threshold the arms already come out.");
            Assert.That(
                handSpan[2],
                Is.GreaterThan(handSpan[0] + 0.15f),
                "Blind drunk, the arms are well out.");
            Assert.That(
                handSpan[3],
                Is.GreaterThan(handSpan[0] + 0.15f),
                "And they stay out as the model works.");
        }

        /// <summary>Distance between the two hands, metres.</summary>
        private static float HandSpan(Player3DCharacterPresentation presentation)
        {
            Player3DAssetRegistry registry = presentation.Registry;
            Transform left = registry.TryGetPart(
                Player3DAnatomicalPart.LeftHand,
                out var leftBinding) && leftBinding != null
                ? leftBinding.Bone
                : null;
            Transform right = registry.TryGetPart(
                Player3DAnatomicalPart.RightHand,
                out var rightBinding) && rightBinding != null
                ? rightBinding.Bone
                : null;
            Assert.That(left, Is.Not.Null);
            Assert.That(right, Is.Not.Null);
            return Vector3.Distance(left.position, right.position);
        }

        private PlayerRuntime CreateHero()
        {
            PlayerRuntime hero = PlayerFactory.Create(
                null,
                Vector3.up * PlayerFactory.GroundedRootOffset,
                cameraObject.GetComponent<Camera>(),
                null,
                null);
            playerObject = hero.GameObject;
            SetLayerRecursively(playerObject.transform, CaptureLayer);
            Physics.SyncTransforms();
            return hero;
        }

        private IntoxicationStatusController CreateStatus(
            PlayerRuntime hero,
            int level)
        {
            GameSessionState.UpdateDrinkingProgress(level, DrinkId.Vodka, 5);
            uiObject = new GameObject("Drunk Arms Capture UI");
            IntoxicationHudView hud = uiObject.AddComponent<IntoxicationHudView>();
            var followCameraObject = new GameObject("Drunk Arms Capture Follow");
            followCameraObject.transform.SetParent(uiObject.transform, false);
            Camera followCamera = followCameraObject.AddComponent<Camera>();
            followCamera.enabled = false;
            var follow = followCameraObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(followCamera, hero.GameObject.transform, false);
            follow.enabled = false;

            IntoxicationStatusController status =
                uiObject.AddComponent<IntoxicationStatusController>();
            status.Initialize(hero, follow, hud);
            return status;
        }

        private void DestroyHero()
        {
            if (uiObject != null)
            {
                Object.Destroy(uiObject);
                uiObject = null;
            }

            if (playerObject != null)
            {
                Object.Destroy(playerObject);
                playerObject = null;
            }

            ResetSession();
            GameSessionState.SetCitySeed(TestCitySeed);
        }

        private int CaptureTile(
            Camera camera,
            Player3DCharacterPresentation presentation,
            int tile)
        {
            Bounds bounds = GetEnabledBounds(presentation);
            Vector3 forward = playerObject.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            // Straight from the front, a touch above eye level, so the
            // arms read against the torso instead of hiding behind it.
            Vector3 viewOffset = (forward + Vector3.up * 0.18f).normalized;
            Vector3 focus = bounds.center + Vector3.down * 0.05f;
            camera.transform.position = focus + viewOffset * 10f;
            camera.transform.rotation = Quaternion.LookRotation(
                focus - camera.transform.position,
                Vector3.up);
            camera.orthographicSize = 1.15f;
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTarget;
            var result = new Texture2D(
                TileSize,
                TileSize,
                TextureFormat.RGBA32,
                false,
                true);
            result.ReadPixels(new Rect(0f, 0f, TileSize, TileSize), 0, 0, false);
            result.Apply(false, false);
            RenderTexture.active = previous;
            int count = CountForegroundPixels(result, camera.backgroundColor);
            int x = (tile % 2) * TileSize;
            int y = (1 - tile / 2) * TileSize;
            sheet.SetPixels(x, y, TileSize, TileSize, result.GetPixels());
            Object.Destroy(result);
            return count;
        }

        private static Bounds GetEnabledBounds(IPlayerPresentation presentation)
        {
            bool hasBounds = false;
            Bounds combined = default;
            for (int index = 0; index < presentation.Renderers.Count; index++)
            {
                Renderer renderer = presentation.Renderers[index];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    combined = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(renderer.bounds);
                }
            }

            Assert.That(hasBounds, Is.True);
            return combined;
        }

        private static int CountForegroundPixels(Texture2D image, Color background)
        {
            Color32 expected = background;
            Color32[] pixels = image.GetPixels32();
            int foreground = 0;
            for (int index = 0; index < pixels.Length; index++)
            {
                Color32 pixel = pixels[index];
                int difference =
                    Mathf.Abs(pixel.r - expected.r) +
                    Mathf.Abs(pixel.g - expected.g) +
                    Mathf.Abs(pixel.b - expected.b);
                if (difference >= 24)
                {
                    foreground++;
                }
            }

            return foreground;
        }

        private static IEnumerator Frames(int count)
        {
            for (int frame = 0; frame < count; frame++)
            {
                yield return null;
            }
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int index = 0; index < root.childCount; index++)
            {
                SetLayerRecursively(root.GetChild(index), layer);
            }
        }

        private static void ResetSession()
        {
            GameSessionState.SetCitySeed(GameSessionState.DefaultCitySeed);
            GameSessionState.EnterBar(null);
            GameSessionState.CompleteCityReturn();
            GameSessionState.ResetDrinkingState();
        }
    }
}
