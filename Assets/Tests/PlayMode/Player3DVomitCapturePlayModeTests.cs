using System;
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
    /// The drunk hero being sick, written to <c>TestResults/vomit-sheet.png</c>
    /// as a three-by-two sheet: the first burst on his feet from his
    /// right-front, the third burst over his shoulder with the puddle
    /// under it, the soiled face close up once the head is back, then a
    /// second episode on the same hero — a bout lying where he fell, a
    /// bout on the way up, and the wide view of the floor he leaves.
    /// The numbers beside the pictures are the ones a picture cannot
    /// settle: which way the head term and the joint drive turn the
    /// chin, that the stream lands and the residue grows, that the
    /// relief and the mark reach the session, and that neither the fall
    /// nor the rise interrupts a bout.
    /// </summary>
    public sealed class Player3DVomitCapturePlayModeTests
    {
        private const int TileSize = 512;
        private const int Columns = 3;
        private const int Rows = 2;
        private const float PinnedFrameSeconds = 1f / 60f;
        private const int CaptureLayer = 28;
        private const int TestCitySeed = 4245;

        private GameObject groundObject;
        private GameObject cameraObject;
        private GameObject lightObject;
        private GameObject playerObject;
        private GameObject uiObject;
        private PlayerCameraFollow followCamera;
        private IntoxicationHudView hudView;
        private RenderTexture renderTarget;
        private Texture2D sheet;
        private bool sheetWritten;
        private AmbientMode previousAmbientMode;
        private Color previousAmbientLight;
        private Light previousSun;
        private bool previousFog;
        private bool renderSettingsCaptured;

        /// <summary>
        /// Intoxication metabolises on the calendar clock — twelve real
        /// seconds a point at the top — and a pinned-frame capture takes
        /// however many real seconds the machine needs, so the levels the
        /// sheet asserts (100 → 80 → 60: the relief and nothing else)
        /// would drift by the speed of the box. In the game the fall's
        /// own modal lock halts the recovery for as long as he is down;
        /// the standing stretches here hold the same lock for the same
        /// reason. The bout runs under a lock by design.
        /// </summary>
        private readonly BarMinigameModalLock recoveryHold =
            new BarMinigameModalLock();

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

            // The ground is the surface the stream lands on: a collider
            // on the default raycast layers, which the effect's rods
            // sweep against, and on the capture layer so the puddle has
            // something to lie on in the picture.
            groundObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            groundObject.name = "Vomit Capture Ground";
            groundObject.transform.position = new Vector3(0f, -0.1f, 0f);
            groundObject.transform.localScale = new Vector3(12f, 0.2f, 12f);
            groundObject.layer = CaptureLayer;

            cameraObject = new GameObject("Vomit Capture Camera");
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
                name = "Vomit Capture Target",
                antiAliasing = 1
            };
            renderTarget.Create();
            camera.targetTexture = renderTarget;

            lightObject = new GameObject("Vomit Capture Key Light");
            Light keyLight = lightObject.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 1.6f;
            keyLight.color = new Color(1f, 0.86f, 0.74f);
            keyLight.shadows = LightShadows.Hard;
            keyLight.cullingMask = 1 << CaptureLayer;
            lightObject.transform.rotation = Quaternion.Euler(48f, 145f, 0f);
            RenderSettings.sun = keyLight;

            sheetWritten = false;
            sheet = new Texture2D(
                TileSize * Columns,
                TileSize * Rows,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = "Vomit Sheet",
                filterMode = FilterMode.Point
            };
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            ReleaseRecoveryHold();
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
                UnityEngine.Object.Destroy(renderTarget);
            }

            if (sheet != null)
            {
                if (!sheetWritten)
                {
                    WriteSheet("Vomit sheet (partial — the test stopped before the last tile)");
                }

                UnityEngine.Object.Destroy(sheet);
            }

            if (lightObject != null)
            {
                UnityEngine.Object.Destroy(lightObject);
            }

            if (cameraObject != null)
            {
                UnityEngine.Object.Destroy(cameraObject);
            }

            if (groundObject != null)
            {
                UnityEngine.Object.Destroy(groundObject);
            }

            ResetSession();
            Time.captureDeltaTime = 0f;
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Vomit_RenderSheet()
        {
            Camera camera = cameraObject.GetComponent<Camera>();
            int[] foreground = new int[Columns * Rows];
            string[] notes = new string[Columns * Rows];

            // One blind-drunk hero for both episodes: the same floor keeps
            // the marks of the first while the second is made on it.
            PlayerRuntime hero = CreateHero();
            var presentation = (Player3DCharacterPresentation)hero.Visual;
            Transform root = hero.GameObject.transform;
            IntoxicationStatusController status = CreateStatus(hero, 100);
            Assert.That(status.Vomit, Is.Not.Null, "the status controller raises the vomit controller");
            Assert.That(
                status.VomitHost,
                Is.Not.Null,
                "the hero rig has a mouth (or a head) anchor, so the stream effect exists");
            HeroVomitStreamEffect effect = status.Vomit.Effect;
            Assert.That(effect, Is.Not.Null);
            Assert.That(effect.Residue, Is.Not.Null);
            HeroVomitResidue residue = effect.Residue;
            Assert.That(GameSessionState.HeroMouthSoiled, Is.False, "a fresh session has a clean face");
            HoldRecovery(hero);
            yield return Frames(30);

            // Tile 0: the first burst on his feet. The chin comes down
            // before the stream starts; the measured pitch pins the sign
            // of the bone-path head term.
            presentation.ReapplyLatePresentationPose();
            float standingPitchBefore = presentation.DebugHeadPitchDownDegrees;
            Assert.That(status.Vomit.DebugForceBout(), Is.True, "a bout starts on demand when none is running");
            yield return Frames(30);
            presentation.ReapplyLatePresentationPose();
            float standingPitchAfter = presentation.DebugHeadPitchDownDegrees;
            Assert.That(
                standingPitchAfter - standingPitchBefore,
                Is.GreaterThanOrEqualTo(8f),
                "chin down — else the vomit head term's sign is inverted");

            int peakAlive = 0;
            while (status.Vomit.Model.Time < 1.2f - 0.0001f)
            {
                yield return null;
                if (status.Vomit.Model.Time >= 0.9f)
                {
                    peakAlive = Mathf.Max(peakAlive, effect.StreamAliveCount);
                }
            }

            Assert.That(status.Vomit.Model.IsVomiting, Is.True, "1.2 s in, the first burst is running");
            Assert.That(
                peakAlive,
                Is.GreaterThan(40),
                "the first burst fills the air with rods (peak alive between 0.9 and 1.2 s)");
            presentation.ReapplyLatePresentationPose();
            notes[0] =
                $"standing pitch +{standingPitchAfter - standingPitchBefore:F1} deg, alive {effect.StreamAliveCount} (peak {peakAlive}), flow {status.Vomit.Pose.Flow:F2}";
            foreground[0] = CaptureTile(camera, presentation, status, 0, 1f, 0.9f, 0.28f, 1.3f, 0.15f);

            yield return WaitForBoutTime(status, 2.2f, 120);
            Assert.That(residue.PatchCount, Is.GreaterThanOrEqualTo(1), "a second of stream leaves a puddle");
            Assert.That(
                residue.TryGetNearestPatch(
                    root.position,
                    out Vector3 patchCenter,
                    out Vector3 patchNormal,
                    out float patchRadius),
                Is.True);
            Assert.That(
                Vector3.Distance(patchCenter, root.position),
                Is.LessThan(1.8f),
                "the puddle lies at his feet, not across the room");
            Assert.That(patchNormal.y, Is.GreaterThan(0.9f), "the puddle lies on the floor");

            // Tile 1: the third burst, seen from behind and above so the
            // floor under the stream is in the picture.
            yield return WaitForBoutTime(status, 8.7f, 480);
            Assert.That(status.Vomit.Model.BurstIndex, Is.EqualTo(2), "8.7 s in is the third burst");
            Assert.That(residue.PatchCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(residue.LargestRadius, Is.GreaterThanOrEqualTo(0.15f), "three bursts grow the puddle");
            Assert.That(residue.ChunkCount, Is.GreaterThanOrEqualTo(3), "lumps land in the puddle");
            presentation.ReapplyLatePresentationPose();
            notes[1] =
                $"burst {status.Vomit.Model.BurstIndex} patches {residue.PatchCount} largest {residue.LargestRadius:F2} m chunks {residue.ChunkCount} first patch r {patchRadius:F2}";
            foreground[1] = CaptureTile(camera, presentation, status, 1, -1f, 0.35f, 1.1f, 1.4f, 0.35f);

            // Tile 2: the head is back up and the face wears the mark.
            yield return WaitForFrames(() => !status.Vomit.IsActive, 200, "the end of the first bout");
            yield return Frames(6);
            Assert.That(status.Vomit.IsActive, Is.False);
            Assert.That(
                GameSessionState.IntoxicationLevel,
                Is.EqualTo(80),
                "a whole bout takes twenty points off, seven, seven and six");
            Assert.That(GameSessionState.HeroMouthSoiled, Is.True, "the first burst soils the mouth");
            Assert.That(presentation.IsMouthSoiledVisible, Is.True, "the soiled face is on screen");
            Renderer faceRenderer = presentation.Registry != null && presentation.Registry.FaceAtlas != null
                ? presentation.Registry.FaceAtlas.Renderer
                : null;
            Assert.That(faceRenderer, Is.Not.Null, "the face atlas binding names its renderer");
            var faceBlock = new MaterialPropertyBlock();
            faceRenderer.GetPropertyBlock(faceBlock);
            Vector4 faceTransform = faceBlock.GetVector("_BaseMap_ST");
            Assert.That(
                faceTransform.x,
                Is.EqualTo(0.125f).Within(0.001f),
                "one column of the eight-column face atlas");
            Assert.That(
                faceTransform.z,
                Is.GreaterThanOrEqualTo(0.5f - 0.0001f),
                "a soiled column: the twins live in the right half of the atlas");
            Assert.That(
                status.Nausea.Clock.RestDuration,
                Is.EqualTo(HeroNauseaClock.InitialRestSeconds).Within(0.001f),
                "the gauge's clock rearms the full rest under a bout");
            presentation.ReapplyLatePresentationPose();
            notes[2] =
                $"level {GameSessionState.IntoxicationLevel}, face ST ({faceTransform.x:F3}, {faceTransform.y:F3}, {faceTransform.z:F3}, {faceTransform.w:F3}), expression {presentation.CurrentFacialExpression}";
            foreground[2] = CaptureFace(camera, presentation, status, 2);

            // Second episode: the drink lands again, and a shove floors
            // him. The recovery hold comes off so the session's grace
            // can run out and the fall can be allowed.
            ReleaseRecoveryHold();
            GameSessionState.UpdateDrinkingProgress(100, DrinkId.Vodka, 5);
            status.Balance.ArmGrace(0f);
            yield return WaitRealtime(
                () => status.Balance.FallAllowedNow,
                25f,
                "falls being allowed after the drink");

            // A three-metre-per-second shove floors him; a seed that
            // saves the first is shoved again.
            for (int shove = 0; shove < 3 && !status.IsFalling; shove++)
            {
                status.Balance.InjectPerturbation(new Vector2(3f, 0f));
                for (int frame = 0; frame < 420 && !status.IsFalling; frame++)
                {
                    yield return null;
                }
            }

            Assert.That(status.IsFalling, Is.True, "the shove floors him");
            Assert.That(presentation.RagdollPoseActive, Is.True, "the ragdoll has the body from the fall frame");

            // Tile 3: a bout lying where he fell. Wait for the body to
            // come to rest (the rise model's Stunned) so the measured
            // head pitch reads the drive and not the tumble, then hold
            // him down for the whole bout: the stun is lengthened by its
            // length, so he stirs only after the head has come back up.
            yield return WaitForFrames(
                () => !status.IsFalling ||
                      (status.Rise != null && status.Rise.Stage >= PlayerRiseStage.Stunned),
                900,
                "the fallen body coming to rest");
            Assert.That(status.IsFalling, Is.True, "he is still down when the body comes to rest");
            Assert.That(status.Rise, Is.Not.Null);
            Assert.That(status.Rise.Stage, Is.EqualTo(PlayerRiseStage.Stunned));
            Assert.That(hero.Ragdoll, Is.Not.Null);
            Assert.That(hero.Ragdoll.IsSimulating, Is.True, "the ragdoll still has the body while he lies stunned");
            status.Rise.NudgeStun(HeroVomitRules.TotalSeconds);
            float lyingPitchBefore = hero.Ragdoll.MeasureHeadPitchDownDegrees();
            Assert.That(status.Vomit.DebugForceBout(), Is.True, "a bout starts lying down — the fall is no gate");
            yield return Frames(60);
            float lyingPitchAfter = hero.Ragdoll.MeasureHeadPitchDownDegrees();
            Assert.That(
                lyingPitchAfter - lyingPitchBefore,
                Is.GreaterThanOrEqualTo(5f),
                $"the head drive must bring the chin to the chest (HeadDriveSign is {Player3DRagdollController.HeadDriveSign:+0;-0}) — else flip Player3DRagdollController.HeadDriveSign");
            Assert.That(effect.StreamAliveCount, Is.GreaterThan(0), "the stream runs lying down");
            Assert.That(status.IsFalling, Is.True, "the bout does not interrupt the fall, nor the fall the bout");
            Assert.That(status.Vomit.IsActive, Is.True);
            notes[3] =
                $"lying pitch +{lyingPitchAfter - lyingPitchBefore:F1} deg (sign {Player3DRagdollController.HeadDriveSign:+0;-0}), alive {effect.StreamAliveCount}, stage {status.RiseStageName}, stun {status.Rise.StunSeconds:F1} s";
            foreground[3] = CaptureTile(camera, presentation, status, 3, 0.5f, 0.9f, 0.8f, 1.6f, 0f);

            // Tile 4: a bout on the way up. The first bout ends with him
            // still stunned; the rise then runs to the half-kneel, where
            // a second bout is forced and the bone path must lower the
            // head over the Rise clip.
            yield return WaitForFrames(
                () => !status.IsFalling || !status.Vomit.IsActive,
                720,
                "the end of the bout on the floor");
            Assert.That(status.IsFalling, Is.True, "the lengthened stun outlasts the bout");
            Assert.That(status.Vomit.IsActive, Is.False);
            Assert.That(GameSessionState.IntoxicationLevel, Is.EqualTo(80), "the second full bout takes its twenty");
            yield return WaitForRise(status, PlayerRiseStage.Kneeling, 0.5f, 900);
            Assert.That(status.RiseStageName, Is.EqualTo(PlayerRiseStage.Kneeling.ToString()));
            presentation.ReapplyLatePresentationPose();
            float risingPitchBefore = presentation.DebugHeadPitchDownDegrees;
            Assert.That(status.Vomit.DebugForceBout(), Is.True, "a bout starts mid-rise");
            yield return Frames(30);
            Assert.That(status.IsFalling, Is.True, "the bout does not end the rise");
            Assert.That(IsRiseStage(status), Is.True, $"still a rise stage (at {status.RiseStageName})");
            presentation.ReapplyLatePresentationPose();
            float risingPitchAfter = presentation.DebugHeadPitchDownDegrees;
            Assert.That(
                risingPitchAfter - risingPitchBefore,
                Is.GreaterThanOrEqualTo(6f),
                "the bone path lowers the head over the Rise clip");
            notes[4] =
                $"rising pitch +{risingPitchAfter - risingPitchBefore:F1} deg at {status.RiseStageName}, alive {effect.StreamAliveCount}";
            foreground[4] = CaptureTile(camera, presentation, status, 4, 1f, 0.9f, 0.4f, 1.3f, 0.1f);

            // Tile 5: the rise completes under the bout (or is cut the
            // moment the level reaches sixty, where falls switch off —
            // the documented consequence). The instant the fall's lock
            // is gone the test's hold returns, so the calendar clock does
            // not steal a point before the bout's last relief.
            for (int frame = 0; frame < 1200; frame++)
            {
                yield return null;
                if (!status.IsFalling)
                {
                    HoldRecovery(hero);
                    if (!status.Vomit.IsActive)
                    {
                        break;
                    }
                }
            }

            Assert.That(status.IsFalling, Is.False, "he is back on his feet");
            Assert.That(status.Vomit.IsActive, Is.False, "the third bout has finished");
            Assert.That(
                GameSessionState.IntoxicationLevel,
                Is.EqualTo(60),
                "three bouts from a hundred: 100 → 80 → 60");
            Assert.That(
                residue.PatchCount,
                Is.GreaterThanOrEqualTo(2),
                "the floor keeps a puddle from where he stood and one from where he lay");
            presentation.ReapplyLatePresentationPose();
            notes[5] =
                $"level {GameSessionState.IntoxicationLevel}, patches {residue.PatchCount}, chunks {residue.ChunkCount}, falls allowed {status.Balance.FallAllowedNow}, state {status.BalanceStateName}";
            foreground[5] = CaptureTile(camera, presentation, status, 5, 1f, 0.9f, 0.5f, 1.8f, 0.2f);

            string outputPath = WriteSheet("Vomit sheet: " + string.Join("; ", notes));

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

        /// <summary>
        /// Writes whatever the sheet holds. Called at the end of the test
        /// and again from TearDown when an assertion stopped the test
        /// early: the picture is the deliverable, and a half-filled sheet
        /// still shows which tile went wrong and how.
        /// </summary>
        private string WriteSheet(string note)
        {
            sheet.Apply(false, false);
            string outputDirectory = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "TestResults"));
            Directory.CreateDirectory(outputDirectory);
            string outputPath = Path.Combine(outputDirectory, "vomit-sheet.png");
            File.WriteAllBytes(outputPath, sheet.EncodeToPNG());
            sheetWritten = true;
            Debug.Log(note + " -> " + outputPath);
            return outputPath;
        }

        private static bool IsRiseStage(IntoxicationStatusController status)
        {
            PlayerRiseModel rise = status.Rise;
            return status.IsFalling &&
                   rise != null &&
                   rise.Stage >= PlayerRiseStage.Stirring &&
                   rise.Stage < PlayerRiseStage.Done;
        }

        /// <summary>
        /// Runs pinned frames until the current bout's clock reaches
        /// <paramref name="seconds"/>; the bout ticks once per frame, so
        /// the wait is a frame count, not a real-time deadline.
        /// </summary>
        private static IEnumerator WaitForBoutTime(
            IntoxicationStatusController status,
            float seconds,
            int frameCap)
        {
            for (int frame = 0; frame < frameCap; frame++)
            {
                if (status.Vomit.Model.Time >= seconds - 0.0001f)
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail(
                $"The bout never reached {seconds:F2} s within {frameCap} frames (at {status.Vomit.Model.Time:F2} s, active {status.Vomit.IsActive}).");
        }

        private static IEnumerator WaitForFrames(
            Func<bool> condition,
            int frameCap,
            string what)
        {
            for (int frame = 0; frame < frameCap; frame++)
            {
                if (condition())
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail($"Waited {frameCap} pinned frames for {what}.");
        }

        private static IEnumerator WaitRealtime(
            Func<bool> condition,
            float seconds,
            string what)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (condition())
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail($"Waited {seconds:F0} real seconds for {what}.");
        }

        /// <summary>
        /// The sibling sheet's helper on a frame cap: the rise runs on
        /// the pinned clock, so a count of frames is the honest deadline.
        /// </summary>
        private static IEnumerator WaitForRise(
            IntoxicationStatusController status,
            PlayerRiseStage stage,
            float stageProgress,
            int frameCap)
        {
            for (int frame = 0; frame < frameCap; frame++)
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

            Assert.Fail(
                $"The rise never reached {stage} at {stageProgress:F2} within {frameCap} frames (at {status.RiseStageName}).");
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
            uiObject = new GameObject("Vomit Capture UI");
            hudView = uiObject.AddComponent<IntoxicationHudView>();
            var followCameraObject = new GameObject("Vomit Capture Follow");
            followCameraObject.transform.SetParent(uiObject.transform, false);
            Camera followCameraComponent = followCameraObject.AddComponent<Camera>();
            followCameraComponent.enabled = false;
            followCamera = followCameraObject.AddComponent<PlayerCameraFollow>();
            followCamera.Initialize(followCameraComponent, hero.GameObject.transform, false);
            followCamera.enabled = false;

            IntoxicationStatusController status =
                uiObject.AddComponent<IntoxicationStatusController>();
            status.Initialize(hero, followCamera, hudView);
            return status;
        }

        private void HoldRecovery(PlayerRuntime hero)
        {
            if (recoveryHold.IsLocked)
            {
                return;
            }

            Assert.That(
                recoveryHold.TryCaptureAndDisable(
                    hero.Interactor,
                    followCamera,
                    hudView,
                    BarMinigameModalLockOptions.BalanceCheck),
                Is.True,
                "the test's recovery hold takes the modal lock (nothing else holds it on his feet)");
        }

        private void ReleaseRecoveryHold()
        {
            if (recoveryHold.IsLocked)
            {
                recoveryHold.Restore();
            }
        }

        private void DestroyHero()
        {
            if (uiObject != null)
            {
                UnityEngine.Object.Destroy(uiObject);
                uiObject = null;
            }

            followCamera = null;
            hudView = null;
            if (playerObject != null)
            {
                UnityEngine.Object.Destroy(playerObject);
                playerObject = null;
            }

            ResetSession();
            GameSessionState.SetCitySeed(TestCitySeed);
        }

        /// <summary>
        /// Frames the hero's visible bounds from a three-quarter view
        /// (<paramref name="front"/> of forward — negative for a view from
        /// behind — <paramref name="side"/> of the actor's right,
        /// <paramref name="up"/> of up) at the given orthographic size,
        /// the focus pushed <paramref name="focusForward"/> metres ahead
        /// of him so the floor the stream lands on shares the frame.
        /// </summary>
        private int CaptureTile(
            Camera camera,
            Player3DCharacterPresentation presentation,
            IntoxicationStatusController status,
            int tile,
            float front,
            float side,
            float up,
            float size,
            float focusForward)
        {
            PrepareVomitForCapture(status);
            Bounds bounds = GetEnabledBounds(presentation);
            Vector3 forward = playerObject.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 viewOffset = (forward * front + right * side + Vector3.up * up).normalized;
            Vector3 focus = bounds.center + forward * focusForward;
            camera.transform.position = focus + viewOffset * 10f;
            camera.transform.rotation = Quaternion.LookRotation(
                focus - camera.transform.position,
                Vector3.up);
            camera.orthographicSize = size;
            return RenderTile(camera, tile);
        }

        /// <summary>
        /// The face close-up of the drunk-face sheet: the head anchor a
        /// hand's breadth up, from straight ahead, a quarter-metre frame.
        /// </summary>
        private int CaptureFace(
            Camera camera,
            Player3DCharacterPresentation presentation,
            IntoxicationStatusController status,
            int tile)
        {
            PrepareVomitForCapture(status);
            Transform head = presentation.Registry.Anchors.Head;
            Assert.That(head, Is.Not.Null, "the rig has a head anchor to frame");
            Vector3 forward = playerObject.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 focus = head.position + Vector3.up * 0.08f;
            camera.transform.position = focus + forward * 4f;
            camera.transform.rotation = Quaternion.LookRotation(-forward, Vector3.up);
            camera.orthographicSize = 0.25f;
            return RenderTile(camera, tile);
        }

        /// <summary>
        /// The stream, the lumps and the residue live under the status
        /// controller's object, not under the hero, so the hero's layer
        /// never reached them; and a burst may have raised emitters since
        /// the last picture. Put the whole host on the capture layer
        /// before every render.
        /// </summary>
        private static void PrepareVomitForCapture(IntoxicationStatusController status)
        {
            if (status.VomitHost != null)
            {
                SetLayerRecursively(status.VomitHost, CaptureLayer);
            }
        }

        private int RenderTile(Camera camera, int tile)
        {
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
            UnityEngine.Object.Destroy(result);
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
