using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// The drink on the hero's face and head: the resting face follows
    /// the level and the blinks grow heavy, the floor shuts the eyes and
    /// the stir is a wince, the head sinks and wanders blind drunk, the
    /// head's pitch and roll signs are pinned against the actor's frame,
    /// and <c>TestResults/drunk-face-sheet.png</c> shows five close-ups.
    /// </summary>
    public sealed class Player3DDrunkFacePlayModeTests
    {
        private const float PinnedFrameSeconds = 1f / 60f;
        private const int TestCitySeed = 4244;
        private const int TileSize = 256;
        private const int Tiles = 5;
        private const int CaptureLayer = 28;

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
            ResetSession();
            GameSessionState.SetCitySeed(TestCitySeed);

            groundObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            groundObject.name = "Drunk Face Ground";
            groundObject.transform.position = new Vector3(0f, -0.1f, 0f);
            groundObject.transform.localScale = new Vector3(14f, 0.2f, 14f);
            groundObject.layer = CaptureLayer;

            cameraObject = new GameObject("Drunk Face Camera");
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
                name = "Drunk Face Capture Target",
                antiAliasing = 1
            };
            renderTarget.Create();
            camera.targetTexture = renderTarget;

            lightObject = new GameObject("Drunk Face Key Light");
            Light keyLight = lightObject.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 1.6f;
            keyLight.color = new Color(1f, 0.86f, 0.74f);
            keyLight.shadows = LightShadows.Hard;
            keyLight.cullingMask = 1 << CaptureLayer;
            lightObject.transform.rotation = Quaternion.Euler(30f, 200f, 0f);

            sheet = new Texture2D(TileSize * Tiles, TileSize, TextureFormat.RGBA32, false, true)
            {
                name = "Drunk Face Sheet",
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

            ResetSession();
            Time.captureDeltaTime = 0f;
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator DrunkFace_FollowsTheLevelBlinksHeavyAndTheFloorShutsTheEyes()
        {
            Camera camera = cameraObject.GetComponent<Camera>();
            int[] foreground = new int[Tiles];
            var levels = new[] { 0, 40, 70, 100 };
            var seenByLevel = new Dictionary<int, HashSet<PlayerFacialExpression>>();
            var longestClosedByLevel = new Dictionary<int, int>();
            Quaternion soberHead = Quaternion.identity;
            Quaternion drunkHead = Quaternion.identity;
            for (int index = 0; index < levels.Length; index++)
            {
                int level = levels[index];
                PlayerRuntime hero = CreateHero();
                var presentation = (Player3DCharacterPresentation)hero.Visual;
                IntoxicationStatusController status = CreateStatus(hero, level);
                status.Balance.ArmGrace(600f);
                Transform head = presentation.Registry.Anchors.Head;
                var seen = new HashSet<PlayerFacialExpression>();
                int longestClosed = 0;
                int run = 0;
                for (int frame = 0; frame < 8 * 60; frame++)
                {
                    yield return null;
                    PlayerFacialExpression expression = presentation.CurrentFacialExpression;
                    if (frame > 120)
                    {
                        seen.Add(expression);
                    }

                    if (expression == PlayerFacialExpression.ClosedBlink)
                    {
                        run++;
                        longestClosed = Mathf.Max(longestClosed, run);
                    }
                    else
                    {
                        run = 0;
                    }
                }

                seenByLevel[level] = seen;
                longestClosedByLevel[level] = longestClosed;
                presentation.ReapplyLatePresentationPose();
                if (level == 0)
                {
                    soberHead = head.rotation;
                }
                else if (level == 100)
                {
                    drunkHead = head.rotation;
                }

                foreground[index] = CaptureTile(camera, presentation, index);
                DestroyHero();
                yield return null;
            }

            Debug.Log(
                "Drunk face: " +
                string.Join("; ", System.Array.ConvertAll(levels, level =>
                    $"{level}: {string.Join("/", seenByLevel[level])} closed {longestClosedByLevel[level]}f")) +
                $"; head sober-vs-drunk {Quaternion.Angle(soberHead, drunkHead):F1} deg");

            Assert.That(seenByLevel[0], Has.No.Member(PlayerFacialExpression.Slack));
            Assert.That(seenByLevel[0], Has.No.Member(PlayerFacialExpression.Glazed));
            Assert.That(seenByLevel[0], Has.No.Member(PlayerFacialExpression.Drowsy));
            Assert.That(seenByLevel[40], Has.Member(PlayerFacialExpression.Drowsy), "at forty the lids droop in spells");
            Assert.That(seenByLevel[70], Has.Member(PlayerFacialExpression.Glazed), "at seventy the eyes glaze");
            Assert.That(seenByLevel[100], Has.Member(PlayerFacialExpression.Slack), "at a hundred the jaw hangs");
            Assert.That(
                longestClosedByLevel[100],
                Is.GreaterThanOrEqualTo(15),
                "blind drunk a blink stays shut a quarter second");
            Assert.That(
                longestClosedByLevel[100],
                Is.GreaterThan(longestClosedByLevel[0] + 5),
                "and longer than the sober blink");
            Assert.That(
                Quaternion.Angle(soberHead, drunkHead),
                Is.GreaterThan(4f),
                "blind drunk the head has moved off the sober head");

            // The fall: the floor shuts the eyes, the stir is a wince.
            PlayerRuntime faller = CreateHero();
            var fallPresentation = (Player3DCharacterPresentation)faller.Visual;
            IntoxicationStatusController fallStatus = CreateStatus(faller, 100);
            fallStatus.Balance.ArmGrace(0f);
            for (int frame = 0; frame < 90; frame++)
            {
                yield return null;
            }

            float deadline = Time.realtimeSinceStartup + 6f;
            while (!fallStatus.Balance.FallAllowedNow && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(fallStatus.Balance.FallAllowedNow, Is.True);
            fallStatus.Balance.InjectPerturbation(new Vector2(3f, 0f));
            deadline = Time.realtimeSinceStartup + 8f;
            while (!fallStatus.IsFalling && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(fallStatus.IsFalling, Is.True, "the shove must floor him");
            bool sawOut = false;
            bool sawGrimace = false;
            bool capturedGrimace = false;
            var transitions = new List<string>();
            string lastTransition = string.Empty;
            PlayerFacialMood previousMood = PlayerFacialMood.None;
            deadline = Time.realtimeSinceStartup + 20f;
            while (fallStatus.IsFalling && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                // The mood is read now (after Update); the face was drawn
                // in the previous LateUpdate, so judge it by last frame's
                // mood, and only once the mood has held a frame.
                PlayerFacialMood moodNow = fallPresentation.CurrentFacialMood;
                PlayerFacialMood mood = previousMood;
                previousMood = moodNow;
                if (mood != moodNow)
                {
                    continue;
                }

                PlayerFacialExpression expression = fallPresentation.CurrentFacialExpression;
                string transition = $"{fallStatus.BalanceStateName}/{fallStatus.RiseStageName}:{mood}:{expression}";
                if (transition != lastTransition)
                {
                    lastTransition = transition;
                    if (transitions.Count < 60)
                    {
                        transitions.Add(transition);
                    }
                }
                if (mood == PlayerFacialMood.Out)
                {
                    sawOut = true;
                    Assert.That(
                        expression,
                        Is.EqualTo(PlayerFacialExpression.ClosedBlink),
                        "out cold, the eyes are shut");
                }

                if (mood == PlayerFacialMood.Grimace && expression == PlayerFacialExpression.Grimace)
                {
                    sawGrimace = true;
                    if (!capturedGrimace && fallStatus.RiseStageName == PlayerRiseStage.Stirring.ToString())
                    {
                        capturedGrimace = true;
                        fallPresentation.ReapplyLatePresentationPose();
                        foreground[4] = CaptureTile(camera, fallPresentation, 4);
                    }
                }

                if (capturedGrimace && fallStatus.Rise != null && fallStatus.Rise.Stage > PlayerRiseStage.Stirring)
                {
                    break;
                }
            }

            Debug.Log("Drunk face through the fall: " + string.Join(" > ", transitions));
            Assert.That(sawOut, Is.True, "the lie puts him out");
            Assert.That(sawGrimace, Is.True, "the floor and the stir wince");
            if (!capturedGrimace)
            {
                fallPresentation.ReapplyLatePresentationPose();
                foreground[4] = CaptureTile(camera, fallPresentation, 4);
            }

            sheet.Apply(false, false);
            string outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestResults"));
            Directory.CreateDirectory(outputDirectory);
            string outputPath = Path.Combine(outputDirectory, "drunk-face-sheet.png");
            File.WriteAllBytes(outputPath, sheet.EncodeToPNG());
            Debug.Log("Drunk face sheet -> " + outputPath);
            Assert.That(File.Exists(outputPath), Is.True);
            for (int index = 0; index < foreground.Length; index++)
            {
                Assert.That(foreground[index], Is.GreaterThan(400), $"Tile {index} produced no substantial GPU-visible model.");
            }
        }

        [UnityTest]
        public IEnumerator DrunkHead_PitchAndRollSignsFollowTheActor()
        {
            PlayerRuntime hero = CreateHero();
            var presentation = (Player3DCharacterPresentation)hero.Visual;
            IntoxicationStatusController status = CreateStatus(hero, 0);
            Transform head = presentation.Registry.Anchors.Head;
            Transform root = hero.GameObject.transform;
            for (int frame = 0; frame < 60; frame++)
            {
                yield return null;
            }

            presentation.DebugForceDrunkHead(IntoxicationHeadPose.None);
            yield return null;
            presentation.ReapplyLatePresentationPose();
            Quaternion reference = head.rotation;

            presentation.DebugForceDrunkHead(new IntoxicationHeadPose(0f, 15f, 0f));
            yield return null;
            presentation.ReapplyLatePresentationPose();
            Quaternion pitched = head.rotation * Quaternion.Inverse(reference);
            Vector3 forwardAfterPitch = pitched * root.forward;
            Debug.Log($"Drunk head probe: chin-down 15 tips the actor's forward to y {forwardAfterPitch.y:F3}; angle {Quaternion.Angle(reference, head.rotation):F1}");
            Assert.That(Quaternion.Angle(reference, head.rotation), Is.GreaterThan(8f), "the pitch reaches the bones");
            Assert.That(forwardAfterPitch.y, Is.LessThan(-0.08f), "a chin-down pitch tips the face DOWN (else flip AttentionPitchSign's use)");

            presentation.DebugForceDrunkHead(new IntoxicationHeadPose(0f, 0f, 10f));
            yield return null;
            presentation.ReapplyLatePresentationPose();
            Quaternion rolled = head.rotation * Quaternion.Inverse(reference);
            Vector3 upAfterRoll = rolled * Vector3.up;
            float toward = Vector3.Dot(upAfterRoll, root.right);
            Debug.Log($"Drunk head probe: roll +10 tips the actor's up toward his right by {toward:F3}; angle {Quaternion.Angle(reference, head.rotation):F1}");
            Assert.That(Quaternion.Angle(reference, head.rotation), Is.GreaterThan(5f), "the roll reaches the bones");
            Assert.That(toward, Is.GreaterThan(0.05f), "a positive roll tilts the head toward the RIGHT shoulder (else flip DrunkHeadRollSign)");

            presentation.DebugForceDrunkHead(null);
            yield return null;
        }

        private int CaptureTile(Camera camera, Player3DCharacterPresentation presentation, int tile)
        {
            Transform head = presentation.Registry.Anchors.Head;
            Vector3 forward = playerObject.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 focus = head.position + Vector3.up * 0.08f;
            camera.transform.position = focus + forward * 4f;
            camera.transform.rotation = Quaternion.LookRotation(-forward, Vector3.up);
            camera.orthographicSize = 0.25f;
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTarget;
            var result = new Texture2D(TileSize, TileSize, TextureFormat.RGBA32, false, true);
            result.ReadPixels(new Rect(0f, 0f, TileSize, TileSize), 0, 0, false);
            result.Apply(false, false);
            RenderTexture.active = previous;
            int count = CountForegroundPixels(result, camera.backgroundColor);
            sheet.SetPixels(tile * TileSize, 0, TileSize, TileSize, result.GetPixels());
            Object.Destroy(result);
            return count;
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

            uiObject = new GameObject("Drunk Face UI");
            IntoxicationHudView hud = uiObject.AddComponent<IntoxicationHudView>();
            var followCameraObject = new GameObject("Drunk Face Follow");
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
