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
    /// The hand at the mouth, seen from the front and from his right,
    /// written to <c>TestResults/nausea-hand-sheet.png</c>: a standing
    /// hero with the nausea's hand fully up, then the same hero with it
    /// let go. The numbers beside the picture are the ones no picture
    /// settles quickly — the palm within reach of the mouth anchor, the
    /// elbow below the shoulder and out to his right rather than folded
    /// into the ribs or up like a wing.
    /// </summary>
    public sealed class Player3DNauseaHandCapturePlayModeTests
    {
        private const int TileSize = 512;
        private const float PinnedFrameSeconds = 1f / 60f;
        private const int CaptureLayer = 28;
        private const int TestCitySeed = 4243;

        private GameObject groundObject;
        private GameObject cameraObject;
        private GameObject lightObject;
        private GameObject playerObject;
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
            groundObject.name = "Nausea Hand Capture Ground";
            groundObject.transform.position = new Vector3(0f, -0.1f, 0f);
            groundObject.transform.localScale = new Vector3(12f, 0.2f, 12f);
            groundObject.layer = CaptureLayer;

            cameraObject = new GameObject("Nausea Hand Capture Camera");
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
                name = "Nausea Hand Capture Target",
                antiAliasing = 1
            };
            renderTarget.Create();
            camera.targetTexture = renderTarget;

            lightObject = new GameObject("Nausea Hand Capture Key Light");
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
                name = "Nausea Hand Sheet",
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
        public IEnumerator NauseaHand_ReachesTheMouthAndRendersTheSheet()
        {
            Camera camera = cameraObject.GetComponent<Camera>();
            int[] foreground = new int[4];

            PlayerRuntime hero = CreateHero();
            var presentation = (Player3DCharacterPresentation)hero.Visual;
            yield return Frames(30);
            presentation.ReapplyLatePresentationPose();
            float hangingDistance = HandToMouth(presentation);

            // The pose the controller pushes at the top of its blend.
            presentation.SetNausea(new PlayerNauseaPose(true, 1f, 0f, 0f));
            yield return Frames(45);
            presentation.ReapplyLatePresentationPose();
            float raisedDistance = HandToMouth(presentation);
            MeasureElbow(
                presentation,
                out float elbowBelowShoulder,
                out float elbowOutToRight,
                out float elbowForward);
            foreground[0] = CaptureTile(camera, presentation, 0, false);
            foreground[1] = CaptureTile(camera, presentation, 1, true);
            Debug.Log(
                $"Nausea hand sheet: hanging {hangingDistance:F3} m from the mouth, " +
                $"raised {raisedDistance:F3} m; elbow {elbowBelowShoulder:F3} m below the shoulder, " +
                $"{elbowOutToRight:F3} m to his right, {elbowForward:F3} m forward.");

            presentation.SetNausea(PlayerNauseaPose.None);
            yield return Frames(45);
            presentation.ReapplyLatePresentationPose();
            float loweredDistance = HandToMouth(presentation);
            foreground[2] = CaptureTile(camera, presentation, 2, false);
            foreground[3] = CaptureTile(camera, presentation, 3, true);
            DestroyHero();
            yield return null;

            sheet.Apply(false, false);
            string outputDirectory = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "TestResults"));
            Directory.CreateDirectory(outputDirectory);
            string outputPath = Path.Combine(outputDirectory, "nausea-hand-sheet.png");
            File.WriteAllBytes(outputPath, sheet.EncodeToPNG());
            Debug.Log($"Nausea hand sheet -> {outputPath}");

            Assert.That(File.Exists(outputPath), Is.True);
            Assert.That(new FileInfo(outputPath).Length, Is.GreaterThan(4096));
            for (int index = 0; index < foreground.Length; index++)
            {
                Assert.That(
                    foreground[index],
                    Is.GreaterThan(900),
                    $"Tile {index} produced no substantial GPU-visible model.");
            }

            Assert.That(hangingDistance, Is.GreaterThan(0.45f), "A hanging hand is nowhere near the mouth.");
            Assert.That(raisedDistance, Is.LessThan(0.12f), "The palm comes to the mouth.");
            Assert.That(elbowBelowShoulder, Is.GreaterThan(0.03f), "The elbow stays below the shoulder — no chicken wing.");
            Assert.That(elbowForward, Is.GreaterThan(0.12f), "The elbow is out in front of the chest — not in the ribs.");
            Assert.That(
                elbowOutToRight,
                Is.GreaterThan(-0.16f),
                "The elbow sits before the sternum at most, never across the body.");
            Assert.That(loweredDistance, Is.GreaterThan(0.45f), "Let go, the hand hangs again.");
            Assert.That(
                Mathf.Abs(loweredDistance - hangingDistance),
                Is.LessThan(0.05f),
                "A None pose leaves the arm exactly where the clip has it.");
        }

        private static float HandToMouth(Player3DCharacterPresentation presentation)
        {
            Player3DAssetRegistry registry = presentation.Registry;
            Transform hand = PartBone(registry, Player3DAnatomicalPart.RightHand);
            Transform mouth = registry.Anchors.Mouth;
            Assert.That(hand, Is.Not.Null);
            Assert.That(mouth, Is.Not.Null, "The Hero V2 rig carries a mouth socket.");
            return Vector3.Distance(hand.position, mouth.position);
        }

        private void MeasureElbow(
            Player3DCharacterPresentation presentation,
            out float belowShoulder,
            out float outToRight,
            out float forward)
        {
            Player3DAssetRegistry registry = presentation.Registry;
            Transform shoulder = PartBone(registry, Player3DAnatomicalPart.RightUpperArm);
            Transform elbow = PartBone(registry, Player3DAnatomicalPart.RightForearm);
            Assert.That(shoulder, Is.Not.Null);
            Assert.That(elbow, Is.Not.Null);
            Vector3 actorForward = playerObject.transform.forward;
            actorForward.y = 0f;
            actorForward.Normalize();
            Vector3 actorRight = Vector3.Cross(Vector3.up, actorForward);
            Vector3 offset = elbow.position - shoulder.position;
            belowShoulder = -offset.y;
            outToRight = Vector3.Dot(offset, actorRight);
            forward = Vector3.Dot(offset, actorForward);
        }

        private static Transform PartBone(
            Player3DAssetRegistry registry,
            Player3DAnatomicalPart part)
        {
            return registry.TryGetPart(part, out var binding) && binding != null
                ? binding.Bone
                : null;
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

        private void DestroyHero()
        {
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
            int tile,
            bool fromHisRight)
        {
            Bounds bounds = GetEnabledBounds(presentation);
            Vector3 forward = playerObject.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            // From the front the hand reads against the face; from his
            // right the elbow reads against the torso.
            Vector3 viewOffset = fromHisRight
                ? (right + Vector3.up * 0.12f).normalized
                : (forward + Vector3.up * 0.18f).normalized;
            Vector3 focus = bounds.center + Vector3.up * 0.25f;
            camera.transform.position = focus + viewOffset * 10f;
            camera.transform.rotation = Quaternion.LookRotation(
                focus - camera.transform.position,
                Vector3.up);
            camera.orthographicSize = 0.85f;
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
