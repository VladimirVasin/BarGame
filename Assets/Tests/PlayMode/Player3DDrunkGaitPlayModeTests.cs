using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// The drunk walk on the real hero: with W held, a blind-drunk walk
    /// lands its boots wider and less evenly than a sober one, every
    /// planted sole still sits on the floor, the walk snakes without the
    /// hero turning, and <c>TestResults/drunk-gait-sheet.png</c> shows
    /// the sober and the drunk stride side by side.
    /// </summary>
    public sealed class Player3DDrunkGaitPlayModeTests
    {
        private const float PinnedFrameSeconds = 1f / 60f;
        private const int TestCitySeed = 4244;
        private const int TileSize = 512;
        private const int Columns = 2;
        private const int Rows = 2;
        private const int CaptureLayer = 28;

        private InputTestFixture inputFixture;
        private Keyboard keyboard;
        private GameObject groundObject;
        private GameObject cameraObject;
        private GameObject lightObject;
        private GameObject playerObject;
        private GameObject uiObject;
        private RenderTexture renderTarget;
        private Texture2D sheet;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.captureDeltaTime = PinnedFrameSeconds;
            inputFixture = new InputTestFixture();
            inputFixture.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            ResetSession();
            GameSessionState.SetCitySeed(TestCitySeed);

            groundObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            groundObject.name = "Drunk Gait Ground";
            groundObject.transform.position = new Vector3(0f, -0.1f, 0f);
            groundObject.transform.localScale = new Vector3(60f, 0.2f, 60f);
            groundObject.layer = CaptureLayer;

            cameraObject = new GameObject("Drunk Gait Camera");
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
            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            cameraData.renderShadows = true;
            cameraData.volumeLayerMask = 0;
            renderTarget = new RenderTexture(TileSize, TileSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = "Drunk Gait Capture Target",
                antiAliasing = 1
            };
            renderTarget.Create();
            camera.targetTexture = renderTarget;

            lightObject = new GameObject("Drunk Gait Key Light");
            Light keyLight = lightObject.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 1.6f;
            keyLight.color = new Color(1f, 0.86f, 0.74f);
            keyLight.shadows = LightShadows.Hard;
            keyLight.cullingMask = 1 << CaptureLayer;
            lightObject.transform.rotation = Quaternion.Euler(48f, 145f, 0f);

            sheet = new Texture2D(TileSize * Columns, TileSize * Rows, TextureFormat.RGBA32, false, true)
            {
                name = "Drunk Gait Sheet",
                filterMode = FilterMode.Point
            };
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            DestroyHero();
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

            inputFixture?.TearDown();
            inputFixture = null;
            keyboard = null;
            ResetSession();
            Time.captureDeltaTime = 0f;
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator DrunkGait_IsWiderAndLessEvenThanSoberAndKeepsSolesOnTheFloor()
        {
            Camera camera = cameraObject.GetComponent<Camera>();
            int[] foreground = new int[Columns * Rows];

            // Sober first: the reference walk.
            WalkRecord sober = null;
            yield return RecordWalk(0, 8f, camera, 0, record => sober = record);
            DestroyHero();
            yield return null;

            WalkRecord drunk = null;
            yield return RecordWalk(100, 8f, camera, 1, record => drunk = record);

            Debug.Log(
                $"Drunk gait: sober width {sober.MeanWidth:F3} m, drunk width {drunk.MeanWidth:F3} m; " +
                $"sober lateral range {sober.LateralRange:F3}, drunk {drunk.LateralRange:F3}; " +
                $"sober half-step CV {sober.HalfStepVariation:F3}, drunk {drunk.HalfStepVariation:F3}; " +
                $"landings sober {sober.Landings.Count}, drunk {drunk.Landings.Count}; " +
                $"worst planted sole sober {sober.WorstPlantedSole:F4} m, drunk {drunk.WorstPlantedSole:F4} m; " +
                $"distance sober {sober.Distance:F2} m, drunk {drunk.Distance:F2} m");

            Assert.That(sober.Landings.Count, Is.GreaterThan(8), "the sober hero walked");
            Assert.That(drunk.Landings.Count, Is.GreaterThan(6), "the drunk hero walked");
            Assert.That(
                drunk.MeanWidth,
                Is.GreaterThan(sober.MeanWidth + 0.05f),
                "blind drunk the boots land a hand wider apart");
            Assert.That(
                drunk.LateralRange,
                Is.GreaterThan(sober.LateralRange + 0.08f),
                "and not at one width twice");
            Assert.That(
                drunk.HalfStepVariation,
                Is.GreaterThan(sober.HalfStepVariation + 0.03f),
                "the half-steps come uneven");
            Assert.That(
                sober.HalfStepVariation,
                Is.LessThan(0.08f),
                "the sober walk keeps its cadence");
            // The ankle-height measure carries about a centimetre of the
            // clip's own heel pitch at contact (the sober walk reads 1.2 cm
            // on it); the drunk boot may add no more than that again.
            Assert.That(
                drunk.WorstPlantedSole,
                Is.LessThan(sober.WorstPlantedSole + 0.02f),
                "a planted drunk sole sits on the floor as the sober one does");

            sheet.Apply(false, false);
            string outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestResults"));
            Directory.CreateDirectory(outputDirectory);
            string outputPath = Path.Combine(outputDirectory, "drunk-gait-sheet.png");
            File.WriteAllBytes(outputPath, sheet.EncodeToPNG());
            Debug.Log("Drunk gait sheet -> " + outputPath);
            Assert.That(File.Exists(outputPath), Is.True);
            for (int index = 0; index < foreground.Length; index++)
            {
                foreground[index] = tileForeground[index];
                Assert.That(foreground[index], Is.GreaterThan(900), $"Tile {index} produced no substantial GPU-visible model.");
            }
        }

        [UnityTest]
        public IEnumerator HeadingWeave_SnakesTheLineWithoutTurning()
        {
            PlayerRuntime hero = CreateHero();
            IntoxicationStatusController status = CreateStatus(hero, 100);
            status.Balance.ArmGrace(600f);
            for (int frame = 0; frame < 90; frame++)
            {
                yield return null;
            }

            Transform root = hero.GameObject.transform;
            Vector3 start = root.position;
            Vector3 heading = root.forward;
            float startYaw = root.eulerAngles.y;
            inputFixture.Press(keyboard.wKey, queueEventOnly: true);
            float largestWeave = 0f;
            float largestSideways = 0f;
            for (int frame = 0; frame < 10 * 60; frame++)
            {
                yield return null;
                Assert.That(status.IsFalling, Is.False, "the grace keeps him on his feet");
                largestWeave = Mathf.Max(largestWeave, Mathf.Abs(status.Balance.Output.HeadingWeaveDegrees));
                Vector3 offset = root.position - start;
                offset.y = 0f;
                largestSideways = Mathf.Max(
                    largestSideways,
                    Mathf.Abs(Vector3.Dot(offset, Vector3.Cross(Vector3.up, heading))));
            }

            inputFixture.Release(keyboard.wKey, queueEventOnly: true);
            yield return null;
            Debug.Log($"Heading weave: largest {largestWeave:F1} deg, largest sideways {largestSideways:F3} m, yaw drift {Mathf.DeltaAngle(startYaw, root.eulerAngles.y):F1} deg");
            Assert.That(largestWeave, Is.GreaterThan(2f), "the model weaves the heading blind drunk");
            Assert.That(largestSideways, Is.GreaterThan(0.04f), "the line he walks bends");
            Assert.That(
                Mathf.Abs(Mathf.DeltaAngle(startYaw, root.eulerAngles.y)),
                Is.LessThan(5f),
                "the weave bends the line, never the hero");
        }

        private readonly int[] tileForeground = new int[Columns * Rows];

        private sealed class WalkRecord
        {
            public readonly List<Vector2> Landings = new List<Vector2>();
            public readonly List<float> LandingTimes = new List<float>();
            public float WorstPlantedSole;
            public float Distance;

            public float MeanWidth
            {
                get
                {
                    if (Landings.Count == 0)
                    {
                        return 0f;
                    }

                    float sum = 0f;
                    foreach (Vector2 landing in Landings)
                    {
                        sum += Mathf.Abs(landing.x);
                    }

                    return sum / Landings.Count;
                }
            }

            public float LateralRange
            {
                get
                {
                    if (Landings.Count == 0)
                    {
                        return 0f;
                    }

                    float minimum = float.PositiveInfinity;
                    float maximum = float.NegativeInfinity;
                    foreach (Vector2 landing in Landings)
                    {
                        minimum = Mathf.Min(minimum, Mathf.Abs(landing.x));
                        maximum = Mathf.Max(maximum, Mathf.Abs(landing.x));
                    }

                    return maximum - minimum;
                }
            }

            public float HalfStepVariation
            {
                get
                {
                    if (LandingTimes.Count < 4)
                    {
                        return 0f;
                    }

                    var intervals = new List<float>();
                    for (int index = 1; index < LandingTimes.Count; index++)
                    {
                        intervals.Add(LandingTimes[index] - LandingTimes[index - 1]);
                    }

                    float mean = 0f;
                    foreach (float interval in intervals)
                    {
                        mean += interval;
                    }

                    mean /= intervals.Count;
                    float variance = 0f;
                    foreach (float interval in intervals)
                    {
                        variance += (interval - mean) * (interval - mean);
                    }

                    return mean > 0f ? Mathf.Sqrt(variance / intervals.Count) / mean : 0f;
                }
            }
        }

        /// <summary>
        /// Walks the hero forward with W for <paramref name="seconds"/>,
        /// recording each boot's landing (its position in the hero's
        /// frame as its plant crosses one half, and the time), the worst
        /// planted sole height, and the ground covered; captures two
        /// tiles of the stride into the sheet's row.
        /// </summary>
        private IEnumerator RecordWalk(
            int level,
            float seconds,
            Camera camera,
            int row,
            System.Action<WalkRecord> deliver)
        {
            PlayerRuntime hero = CreateHero();
            var presentation = (Player3DCharacterPresentation)hero.Visual;
            IntoxicationStatusController status = CreateStatus(hero, level);
            status.Balance.ArmGrace(600f);
            for (int frame = 0; frame < 90; frame++)
            {
                yield return null;
            }

            Player3DAssetRegistry registry = presentation.Registry;
            Transform root = hero.GameObject.transform;
            Transform leftFoot = registry.Anchors.LeftFoot;
            Transform rightFoot = registry.Anchors.RightFoot;
            var record = new WalkRecord();
            Vector3 start = root.position;
            float previousCycle = presentation.ForwardGaitCycle;
            float soleOffset = SoleOffset(presentation, leftFoot);
            bool capturedFirst = false;
            bool capturedSecond = false;
            int lastStepFrame = -1000;
            inputFixture.Press(keyboard.wKey, queueEventOnly: true);
            int frames = Mathf.RoundToInt(seconds / PinnedFrameSeconds);
            for (int frame = 0; frame < frames; frame++)
            {
                yield return null;
                Assert.That(status.IsFalling, Is.False, "the grace keeps him on his feet");
                // A coroutine resumes after Update and before LateUpdate:
                // the bones hold the raw clip until the late layer runs.
                // Put the late pose on before reading any boot.
                presentation.ReapplyLatePresentationPose();
                float left = presentation.LeftFootPlant;
                float right = presentation.RightFootPlant;
                float time = frame * PinnedFrameSeconds;
                // The Walk clip contacts the left heel at cycle zero and
                // the right at one half: a landing is the cycle crossing
                // that mark, and the boot's place then is where it landed.
                float cycle = presentation.ForwardGaitCycle;
                if (presentation.ForwardGaitDominant)
                {
                    if (PlayerDrunkGaitRules.Crossed(previousCycle, cycle, 0f))
                    {
                        record.Landings.Add(Local(root, leftFoot.position));
                        record.LandingTimes.Add(time);
                    }

                    if (PlayerDrunkGaitRules.Crossed(previousCycle, cycle, 0.5f))
                    {
                        record.Landings.Add(Local(root, rightFoot.position));
                        record.LandingTimes.Add(time);
                    }
                }

                previousCycle = cycle;

                bool stepping = status.Balance.Output.Step.Active;
                if (stepping)
                {
                    lastStepFrame = frame;
                }

                // A boot fully planted, no recovery step in flight or just
                // landed: its ankle sits where the standing ankle sits.
                if (frame - lastStepFrame > 20 && frame > 60)
                {
                    if (left > 0.97f)
                    {
                        record.WorstPlantedSole = Mathf.Max(
                            record.WorstPlantedSole,
                            Mathf.Abs(leftFoot.position.y - soleOffset));
                    }

                    if (right > 0.97f)
                    {
                        record.WorstPlantedSole = Mathf.Max(
                            record.WorstPlantedSole,
                            Mathf.Abs(rightFoot.position.y - soleOffset));
                    }
                }

                // Two tiles mid-stride (the swinging boot at the middle of
                // its swing): one three-quarter, one from the front.
                bool leftMidSwing = Mathf.Abs(cycle - 0.5f) < 0.03f;
                bool rightMidSwing = cycle < 0.03f || cycle > 0.97f;
                if (!capturedFirst && frame > 150 && leftMidSwing)
                {
                    capturedFirst = true;
                    tileForeground[row * Columns] = CaptureTile(camera, presentation, row * Columns, 0.9f, 0.28f, 1.3f);
                }
                else if (capturedFirst && !capturedSecond && frame > 240 && rightMidSwing)
                {
                    capturedSecond = true;
                    tileForeground[row * Columns + 1] = CaptureTile(camera, presentation, row * Columns + 1, 0.05f, 0.2f, 1.3f);
                }
            }

            inputFixture.Release(keyboard.wKey, queueEventOnly: true);
            yield return null;
            Vector3 travelled = root.position - start;
            travelled.y = 0f;
            record.Distance = travelled.magnitude;
            deliver(record);
        }

        /// <summary>The ankle bone's height over the floor when the sole rests on it: read from the standing pose.</summary>
        private static float SoleOffset(Player3DCharacterPresentation presentation, Transform foot)
        {
            presentation.ReapplyLatePresentationPose();
            return foot.position.y;
        }

        private static Vector2 Local(Transform root, Vector3 world)
        {
            Vector3 offset = world - root.position;
            Vector3 forward = root.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            return new Vector2(Vector3.Dot(offset, right), Vector3.Dot(offset, forward));
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

        private IntoxicationStatusController CreateStatus(PlayerRuntime hero, int level)
        {
            GameSessionState.ResetDrinkingState();
            if (level > 0)
            {
                GameSessionState.UpdateDrinkingProgress(level, DrinkId.Vodka, 5);
            }

            uiObject = new GameObject("Drunk Gait UI");
            IntoxicationHudView hud = uiObject.AddComponent<IntoxicationHudView>();
            var followCameraObject = new GameObject("Drunk Gait Follow");
            followCameraObject.transform.SetParent(uiObject.transform, false);
            Camera followCamera = followCameraObject.AddComponent<Camera>();
            followCamera.enabled = false;
            var follow = followCameraObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(followCamera, hero.GameObject.transform, false);
            follow.enabled = false;
            IntoxicationStatusController status = uiObject.AddComponent<IntoxicationStatusController>();
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
            camera.transform.rotation = Quaternion.LookRotation(focus - camera.transform.position, Vector3.up);
            camera.orthographicSize = size;
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTarget;
            var result = new Texture2D(TileSize, TileSize, TextureFormat.RGBA32, false, true);
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
