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
    /// Four frames of the procedural locomotion for the eye, written to
    /// <c>TestResults/procedural-locomotion-sheet.png</c>: a sober hero
    /// with one boot on a block, a sober hero on two render-only treads,
    /// a blind-drunk hero mid-stagger, and the same hero catching a wall.
    /// Numbers cannot see a boot inside a tread; this sheet can.
    /// </summary>
    public sealed class Player3DProceduralLocomotionCapturePlayModeTests
    {
        private const int TileSize = 512;
        private const float PinnedFrameSeconds = 1f / 60f;
        private const int CaptureLayer = 28;
        private const int TestCitySeed = 4242;

        private GameObject groundObject;
        private GameObject cameraObject;
        private GameObject lightObject;
        private GameObject playerObject;
        private GameObject uiObject;
        private GameObject propObject;
        private GameObject secondPropObject;
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
            groundObject.name = "Procedural Capture Ground";
            groundObject.transform.position = new Vector3(0f, -0.1f, 0f);
            groundObject.transform.localScale = new Vector3(12f, 0.2f, 12f);
            groundObject.layer = CaptureLayer;

            cameraObject = new GameObject("Procedural Capture Camera");
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
                name = "Procedural Capture Target",
                antiAliasing = 1
            };
            renderTarget.Create();
            camera.targetTexture = renderTarget;

            lightObject = new GameObject("Procedural Capture Key Light");
            Light keyLight = lightObject.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 1.6f;
            keyLight.color = new Color(1f, 0.86f, 0.74f);
            keyLight.shadows = LightShadows.Hard;
            keyLight.cullingMask = 1 << CaptureLayer;
            lightObject.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            RenderSettings.sun = keyLight;

            sheet = new Texture2D(
                TileSize * 2,
                TileSize * 2,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = "Procedural Locomotion Sheet",
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
        public IEnumerator ProceduralLocomotion_RendersBlockTreadsStaggerAndWallSheet()
        {
            Camera camera = cameraObject.GetComponent<Camera>();
            int[] foreground = new int[4];

            // Tile 0: sober, left boot on a 0.10 m block.
            PlayerRuntime hero = CreateHero(motorEnabled: false);
            var presentation = (Player3DCharacterPresentation)hero.Visual;
            yield return Frames(20);
            propObject = CreateBlock(
                "Capture Block",
                presentation,
                0.10f,
                renderOnly: false);
            yield return Frames(45);
            presentation.ReapplyLatePresentationPose();
            foreground[0] = CaptureTile(camera, presentation, 0);
            DestroyHero();
            yield return null;

            // Tile 1: sober, two render-only treads under the boots.
            hero = CreateHero(motorEnabled: false);
            presentation = (Player3DCharacterPresentation)hero.Visual;
            yield return Frames(20);
            propObject = CreateBlock(
                "Capture Tread Low",
                presentation,
                0.10f,
                renderOnly: true,
                side: FootSide.Right);
            FootProbeSurface.AddTreadCollider(propObject);
            secondPropObject = CreateBlock(
                "Capture Tread High",
                presentation,
                0.20f,
                renderOnly: true,
                side: FootSide.Left);
            FootProbeSurface.AddTreadCollider(secondPropObject);
            yield return Frames(45);
            presentation.ReapplyLatePresentationPose();
            foreground[1] = CaptureTile(camera, presentation, 1);
            DestroyHero();
            yield return null;

            // Tile 2: blind drunk, standing, mid-stagger.
            hero = CreateHero(motorEnabled: true);
            presentation = (Player3DCharacterPresentation)hero.Visual;
            IntoxicationStatusController status = CreateStatus(hero, 100);
            status.Balance.ArmGrace(60f);
            int bestFrame = 0;
            float bestScore = -1f;
            for (int frame = 0; frame < 8 * 60; frame++)
            {
                yield return null;
                PlayerBalancePose pose = presentation.BalancePose;
                float score = Mathf.Abs(pose.LeanRollDegrees) +
                              (pose.Step.Active ? 20f : 0f);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestFrame = frame;
                }

                if (pose.Step.Active && pose.Step.Progress > 0.35f &&
                    pose.Step.Progress < 0.65f)
                {
                    break;
                }
            }

            presentation.ReapplyLatePresentationPose();
            foreground[2] = CaptureTile(camera, presentation, 2);
            Debug.Log(
                $"Procedural sheet: stagger tile at frame {bestFrame}, " +
                $"lean {presentation.BalancePose.LeanRollDegrees:F1} deg, " +
                $"step {presentation.BalancePose.Step.Active}, " +
                $"instability {status.Balance.Instability:F2}");
            DestroyHero();
            yield return null;

            // Tile 3: blind drunk beside a wall on his right.
            hero = CreateHero(motorEnabled: true);
            presentation = (Player3DCharacterPresentation)hero.Visual;
            status = CreateStatus(hero, 100);
            status.Balance.ArmGrace(60f);
            Transform root = hero.GameObject.transform;
            propObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            propObject.name = "Capture Wall";
            propObject.transform.position =
                root.position + root.right * 0.55f + Vector3.up * 1.2f;
            propObject.transform.rotation = root.rotation;
            propObject.transform.localScale = new Vector3(0.12f, 2.4f, 3f);
            propObject.layer = CaptureLayer;
            Physics.SyncTransforms();
            for (int frame = 0; frame < 15 * 60; frame++)
            {
                yield return null;
                if (status.Balance.WallReach.Active &&
                    status.Balance.WallReach.Weight > 0.9f)
                {
                    break;
                }
            }

            presentation.ReapplyLatePresentationPose();
            foreground[3] = CaptureTile(camera, presentation, 3, -1f);
            Debug.Log(
                $"Procedural sheet: wall tile reach " +
                $"{status.Balance.WallReach.Active} weight " +
                $"{status.Balance.WallReach.Weight:F2}, " +
                $"within reach {status.Balance.WallWithinReach}");

            sheet.Apply(false, false);
            string outputDirectory = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "TestResults"));
            Directory.CreateDirectory(outputDirectory);
            string outputPath = Path.Combine(
                outputDirectory,
                "procedural-locomotion-sheet.png");
            File.WriteAllBytes(outputPath, sheet.EncodeToPNG());

            Assert.That(File.Exists(outputPath), Is.True);
            Assert.That(new FileInfo(outputPath).Length, Is.GreaterThan(4096));
            for (int index = 0; index < foreground.Length; index++)
            {
                Assert.That(
                    foreground[index],
                    Is.GreaterThan(900),
                    $"Tile {index} produced no substantial GPU-visible model.");
            }
        }

        private PlayerRuntime CreateHero(bool motorEnabled)
        {
            PlayerRuntime hero = PlayerFactory.Create(
                null,
                Vector3.up * PlayerFactory.GroundedRootOffset,
                cameraObject.GetComponent<Camera>(),
                null,
                null);
            playerObject = hero.GameObject;
            hero.Motor.enabled = motorEnabled;
            SetLayerRecursively(playerObject.transform, CaptureLayer);
            Physics.SyncTransforms();
            return hero;
        }

        private IntoxicationStatusController CreateStatus(
            PlayerRuntime hero,
            int level)
        {
            GameSessionState.UpdateDrinkingProgress(level, DrinkId.Vodka, 5);
            uiObject = new GameObject("Procedural Capture UI");
            IntoxicationHudView hud = uiObject.AddComponent<IntoxicationHudView>();
            // The status controller wants a chase camera to roll; it gets
            // a dummy on its own object so the orthographic capture camera
            // is never re-projected by the follow's Initialize.
            var followCameraObject = new GameObject("Procedural Capture Follow");
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

            if (propObject != null)
            {
                Object.Destroy(propObject);
                propObject = null;
            }

            if (secondPropObject != null)
            {
                Object.Destroy(secondPropObject);
                secondPropObject = null;
            }

            ResetSession();
            GameSessionState.SetCitySeed(TestCitySeed);
        }

        private GameObject CreateBlock(
            string name,
            Player3DCharacterPresentation presentation,
            float height,
            bool renderOnly,
            FootSide side = FootSide.Left)
        {
            Transform actor = playerObject.transform;
            Vector3 forward = actor.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 right = actor.right;
            right.y = 0f;
            right.Normalize();
            Player3DAssetRegistry registry = presentation.Registry;
            Vector3 ankle = side == FootSide.Left
                ? registry.Anchors.LeftFoot.position
                : registry.Anchors.RightFoot.position;
            Vector3 other = side == FootSide.Left
                ? registry.Anchors.RightFoot.position
                : registry.Anchors.LeftFoot.position;
            float lateral = Vector3.Dot(ankle - other, right);
            float sideSign = Mathf.Sign(lateral);
            const float footprint = 0.6f;
            float inwardReach = Mathf.Min(0.12f, Mathf.Abs(lateral) * 0.45f);
            Vector3 centre = ankle +
                             right * (sideSign * (footprint * 0.5f - inwardReach)) +
                             forward * 0.08f;
            centre.y = height * 0.5f;
            Vector3 size = new Vector3(footprint, height, footprint);
            GameObject block;
            if (renderOnly)
            {
                block = RuntimePrimitiveFactory.CreateBox(
                    name,
                    null,
                    centre,
                    size,
                    new Color(0.55f, 0.52f, 0.48f),
                    false);
            }
            else
            {
                block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                block.name = name;
                block.transform.position = centre;
                block.transform.localScale = size;
            }

            block.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            block.layer = CaptureLayer;
            Physics.SyncTransforms();
            return block;
        }

        private int CaptureTile(
            Camera camera,
            Player3DCharacterPresentation presentation,
            int tile,
            float viewSide = 1f)
        {
            Bounds bounds = GetEnabledBounds(presentation);
            Vector3 forward = playerObject.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            // viewSide picks the shoulder the camera looks over: a wall on
            // his right is shot from his left, or it hides him.
            Vector3 viewOffset =
                (forward + right * (0.9f * viewSide) + Vector3.up * 0.28f)
                .normalized;
            Vector3 focus = bounds.center + Vector3.down * 0.25f;
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
