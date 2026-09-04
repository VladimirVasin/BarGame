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
    /// The fight before the fall and the getting up after it, written to
    /// <c>TestResults/topple-rise-sheet.png</c> as a two-by-three sheet:
    /// mid-lunge, the brace at the moment of the fall, stirring on the
    /// floor, a slump on the way to all fours, the half-kneel with a hand
    /// on the knee, and the wobble at the top. Numbers cannot tell a lunge
    /// from a stagger or a rise from a pop; a sheet can.
    /// </summary>
    public sealed class Player3DToppleRiseCapturePlayModeTests
    {
        private const int TileSize = 512;
        private const int Columns = 3;
        private const int Rows = 2;
        private const float PinnedFrameSeconds = 1f / 60f;
        private const int CaptureLayer = 28;
        private const int TestCitySeed = 4244;

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
            groundObject.name = "Topple Rise Capture Ground";
            groundObject.transform.position = new Vector3(0f, -0.1f, 0f);
            groundObject.transform.localScale = new Vector3(14f, 0.2f, 14f);
            groundObject.layer = CaptureLayer;

            cameraObject = new GameObject("Topple Rise Capture Camera");
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
                name = "Topple Rise Capture Target",
                antiAliasing = 1
            };
            renderTarget.Create();
            camera.targetTexture = renderTarget;

            lightObject = new GameObject("Topple Rise Capture Key Light");
            Light keyLight = lightObject.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 1.6f;
            keyLight.color = new Color(1f, 0.86f, 0.74f);
            keyLight.shadows = LightShadows.Hard;
            keyLight.cullingMask = 1 << CaptureLayer;
            lightObject.transform.rotation = Quaternion.Euler(48f, 145f, 0f);
            RenderSettings.sun = keyLight;

            sheet = new Texture2D(
                TileSize * Columns,
                TileSize * Rows,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = "Topple Rise Sheet",
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
        public IEnumerator ToppleAndRise_RenderSheet()
        {
            Camera camera = cameraObject.GetComponent<Camera>();
            int[] foreground = new int[Columns * Rows];
            string[] notes = new string[Columns * Rows];

            // One blind-drunk hero, shoved hard to his right once the
            // drink has landed: he lunges, braces, falls, lies, and gets
            // up — every tile is a moment of that one episode.
            PlayerRuntime hero = CreateHero();
            var presentation = (Player3DCharacterPresentation)hero.Visual;
            IntoxicationStatusController status = CreateStatus(hero, 100);
            status.Balance.ArmGrace(0f);
            yield return Frames(90);
            Assert.That(presentation.IntoxicationAmount, Is.GreaterThan(0.9f));

            // The session's own grace after the level crossing is on the
            // real clock; wait it out before the shove can topple him.
            float graceDeadline = Time.realtimeSinceStartup + 6f;
            while (!status.Balance.FallAllowedNow &&
                   Time.realtimeSinceStartup < graceDeadline)
            {
                yield return null;
            }

            Assert.That(status.Balance.FallAllowedNow, Is.True, "falls must be allowed for the shove to topple him");
            status.Balance.InjectPerturbation(new Vector2(3f, 0f));

            // Tile 0: mid-lunge.
            bool sawLunge = false;
            for (int frame = 0; frame < 120; frame++)
            {
                yield return null;
                PlayerBalanceModel model = status.Balance.Model;
                if (model.Phase == BalancePhase.Toppling &&
                    model.StepIsLunge &&
                    model.Output.Step.Progress > 0.3f)
                {
                    sawLunge = true;
                    break;
                }

                if (model.LostBalance)
                {
                    break;
                }
            }

            Assert.That(sawLunge, Is.True, "the shove must start a topple with a lunge in flight");
            presentation.ReapplyLatePresentationPose();
            notes[0] = $"lunge lean {status.Balance.Model.LeanDegrees:F1} deg, lunges {status.Balance.Model.LungesTaken}";
            foreground[0] = CaptureTile(camera, presentation, 0, 0.9f, 0.28f, 1.3f);

            // Tile 1: the brace, on the frame the fall is declared — the
            // ragdoll has the body from this frame, holding the pose the
            // late layer wrote.
            float fallDeadline = Time.realtimeSinceStartup + 8f;
            while (!status.IsFalling && Time.realtimeSinceStartup < fallDeadline)
            {
                yield return null;
            }

            Assert.That(status.IsFalling, Is.True, "a three-metre-per-second shove floors him");
            Assert.That(presentation.RagdollPoseActive, Is.True);
            notes[1] = $"brace weight {status.Balance.Output.BraceWeight:F2}, cause {status.Balance.Model.FallCause}";
            foreground[1] = CaptureTile(camera, presentation, 1, 0.9f, 0.28f, 1.3f);

            // Tile 2: stirring on the floor, halfway through — the frozen
            // ragdoll pose half blended into the clip's brace, the hands
            // finding the floor.
            yield return WaitForRise(status, PlayerRiseStage.Stirring, 0.5f, 14f);
            Assert.That(status.RiseStageName, Is.EqualTo(PlayerRiseStage.Stirring.ToString()));
            presentation.ReapplyLatePresentationPose();
            notes[2] = $"stirring side {status.RiseSide}, residual {status.RiseResidual.magnitude:F2} m";
            foreground[2] = CaptureTile(camera, presentation, 2, 0.9f, 0.55f, 1.25f);

            // Tile 3: the slump on the way to all fours, at the bottom of
            // the dip. Two slumps are planned whatever the seed drew.
            status.Rise.DebugPlanSlumps(2);
            yield return WaitForSlump(status, 8f);
            presentation.ReapplyLatePresentationPose();
            notes[3] = $"slump dip {status.Rise.Output.PelvisOffsetMetres:F3} m, clip {status.Rise.Output.ClipTime:F2}";
            foreground[3] = CaptureTile(camera, presentation, 3, 0.9f, 0.55f, 1.25f);

            // Tile 4: the half-kneel, a hand on the knee, the lead boot
            // planted forward.
            yield return WaitForRise(status, PlayerRiseStage.Kneeling, 0.85f, 8f);
            Assert.That(status.RiseStageName, Is.EqualTo(PlayerRiseStage.Kneeling.ToString()));
            Assert.That(status.Rise.Output.HandOnKnee, Is.True);
            Assert.That(
                status.Rise.Output.Step.Side,
                Is.EqualTo(status.RiseSide),
                "the boot that leads the kneel is the side he lay on");
            Assert.That(status.Rise.Output.KneeSide, Is.EqualTo(status.RiseSide));
            presentation.ReapplyLatePresentationPose();
            notes[4] = $"kneel step {status.Rise.Output.Step.Side}, weight {status.Rise.Output.Step.Weight:F2}";
            foreground[4] = CaptureTile(camera, presentation, 4, 0.9f, 0.4f, 1.25f);

            // Tile 5: standing, the wobble at the top.
            yield return WaitForRise(status, PlayerRiseStage.Standing, 0.85f, 8f);
            Assert.That(status.RiseStageName, Is.EqualTo(PlayerRiseStage.Standing.ToString()));
            presentation.ReapplyLatePresentationPose();
            notes[5] = $"standing wobble {status.Rise.Output.WobbleLeanDegrees.x:F1} deg, legs {status.Rise.Output.LegsWeight:F2}";
            foreground[5] = CaptureTile(camera, presentation, 5, 0.9f, 0.28f, 1.25f);

            sheet.Apply(false, false);
            string outputDirectory = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "TestResults"));
            Directory.CreateDirectory(outputDirectory);
            string outputPath = Path.Combine(outputDirectory, "topple-rise-sheet.png");
            File.WriteAllBytes(outputPath, sheet.EncodeToPNG());
            Debug.Log(
                "Topple rise sheet: " + string.Join("; ", notes) + " -> " + outputPath);

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

        private static IEnumerator WaitForRise(
            IntoxicationStatusController status,
            PlayerRiseStage stage,
            float stageProgress,
            float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                PlayerRiseModel rise = status.Rise;
                if (rise != null &&
                    (rise.Stage > stage ||
                     (rise.Stage == stage && rise.Output.StageProgress >= stageProgress)))
                {
                    yield break;
                }

                if (!status.IsFalling)
                {
                    Assert.Fail($"The fall ended before the rise reached {stage} at {stageProgress:F2}.");
                }

                yield return null;
            }

            Assert.Fail($"The rise never reached {stage} at {stageProgress:F2} (at {status.RiseStageName}).");
        }

        private static IEnumerator WaitForSlump(
            IntoxicationStatusController status,
            float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                PlayerRiseModel rise = status.Rise;
                if (rise != null &&
                    rise.Output.SlumpActive &&
                    rise.Output.PelvisOffsetMetres < -PlayerRiseRules.SlumpDipMetres * 0.9f)
                {
                    yield break;
                }

                if (rise != null && rise.Stage > PlayerRiseStage.PushingUp)
                {
                    Assert.Fail("The push-up finished without a slump at its bottom being seen.");
                }

                yield return null;
            }

            Assert.Fail($"No slump was seen (at {status.RiseStageName}).");
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
            uiObject = new GameObject("Topple Rise Capture UI");
            IntoxicationHudView hud = uiObject.AddComponent<IntoxicationHudView>();
            var followCameraObject = new GameObject("Topple Rise Capture Follow");
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

        /// <summary>
        /// Frames the hero's visible bounds from a three-quarter view
        /// (<paramref name="side"/> of the actor's right, <paramref name="up"/>
        /// of up, mixed with forward) at the given orthographic size.
        /// </summary>
        private int CaptureTile(
            Camera camera,
            Player3DCharacterPresentation presentation,
            int tile,
            float side,
            float up,
            float size)
        {
            Bounds bounds = GetEnabledBounds(presentation);
            Vector3 forward = playerObject.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 viewOffset = (forward + right * side + Vector3.up * up).normalized;
            Vector3 focus = bounds.center;
            camera.transform.position = focus + viewOffset * 10f;
            camera.transform.rotation = Quaternion.LookRotation(
                focus - camera.transform.position,
                Vector3.up);
            camera.orthographicSize = size;
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
            int x = (tile % Columns) * TileSize;
            int y = (Rows - 1 - tile / Columns) * TileSize;
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
