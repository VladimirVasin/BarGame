using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// Dense final-rig continuity and rendered sequences for both recovery
    /// routes. Directed lying poses use the real imported skeleton and the
    /// controller's real handoff; the last episode also runs live physics.
    /// </summary>
    public sealed class Player3DToppleRiseCapturePlayModeTests
    {
        private const int TileSize = 256;
        private const int Columns = 6;
        private const int Rows = 5;
        private const float PinnedFrameSeconds = 1f / 60f;
        private const int CaptureLayer = 28;
        private const int TestCitySeed = 4244;

        private GameObject groundObject;
        private GameObject cameraObject;
        private GameObject lightObject;
        private GameObject playerObject;
        private GameObject uiObject;
        private GameObject obstacleObject;
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
        [PrebuildSetup(typeof(PlayerRecoveryAssetsSetup))]
        public IEnumerator ToppleAndRise_RenderSheet()
        {
            string directory = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "TestResults", "topple-rise"));
            Directory.CreateDirectory(directory);
            var reports = new StringBuilder("case,frame,stage,route,clip,max_position_delta_m,max_rotation_delta_deg,max_position_bone,max_rotation_bone,balance_step_active,balance_step_side,balance_step_progress,balance_brace_weight,sample_delta_seconds,unity_frame\n");
            var discontinuities = new List<string>();
            CaptureSavedToppleRecovery(directory, reports, discontinuities);
            DestroyHero();
            yield return Frames(2);
            var cases = new[]
            {
                new RecoveryCase("supine", -90f, 0f, PlayerRiseRoute.Seated),
                new RecoveryCase("prone", 90f, 0f, PlayerRiseRoute.AllFours),
                new RecoveryCase("left-back", 0f, 90f, PlayerRiseRoute.Seated, 25f),
                new RecoveryCase("left-front", 0f, 90f, PlayerRiseRoute.AllFours, -25f),
                new RecoveryCase("right-back-crawl", 0f, -90f, PlayerRiseRoute.Seated, 25f, true),
                new RecoveryCase("right-front-crawl", 0f, -90f, PlayerRiseRoute.AllFours, -25f, true),
                new RecoveryCase("side-near-back-slope", 0f, 90f, null, 1f, false, true),
                new RecoveryCase("side-near-front-obstacle", 0f, -90f, null, -1f, false, false, true)
            };

            foreach (RecoveryCase scenario in cases)
            {
                bool continuityFailed = false;
                PlayerRuntime hero = CreateHero();
                var presentation = (Player3DCharacterPresentation)hero.Visual;
                IntoxicationStatusController status = CreateStatus(hero, 100);
                yield return Frames(3);
                var bones = new RigSample(presentation.Registry);
                Assert.That(status.DebugForceLoseBalance(1f), Is.True);
                for (int frame = 0; frame < 8 && !status.IsFalling; frame++)
                {
                    yield return null;
                }

                Assert.That(hero.Ragdoll.IsSimulating, Is.True, scenario.Name);
                if (scenario.Slope)
                {
                    groundObject.transform.rotation = Quaternion.Euler(0f, 0f, 5f);
                }
                if (scenario.Obstacle)
                {
                    obstacleObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    obstacleObject.name = "Recovery support obstacle";
                    obstacleObject.layer = CaptureLayer;
                    obstacleObject.transform.position = new Vector3(0.45f, 0.16f, 0.3f);
                    obstacleObject.transform.localScale = new Vector3(0.28f, 0.32f, 0.6f);
                }

                // A rigid rotation of the measured production pose. Bone-local
                // axes and original shove direction cannot predetermine the route.
                // No physics step occurs between placement and the real freeze.
                foreach (Rigidbody body in playerObject.GetComponentsInChildren<Rigidbody>(true))
                {
                    body.isKinematic = true;
                }
                Quaternion rotation = Quaternion.Euler(scenario.Pitch, 0f, scenario.Roll);
                Vector3 headward = rotation * Vector3.up;
                Vector3 bodyFront = rotation * Vector3.forward;
                // Positive scenario tilt always means face toward the ceiling.
                // The headward axis reverses between the two lying sides, so
                // using an identical signed rotation would swap front/back.
                float upwardSign = Mathf.Sign(Vector3.Dot(
                    Vector3.Cross(headward, bodyFront), Vector3.up));
                rotation = Quaternion.AngleAxis(scenario.FaceTilt * upwardSign, headward) * rotation;
                bones.PlaceRotated(rotation, presentation.Registry.Anchors.Pelvis.position);
                Physics.SyncTransforms();
                float lyingBottom = MinimumBodyContactY();
                float floorClearance = 0.01f + (scenario.Slope ? 0.1f : 0f);
                Vector3 translation = Vector3.up * (floorClearance - lyingBottom);
                Assert.That(Mathf.Abs(translation.y), Is.LessThan(3f),
                    scenario.Name + " placement must stay at character scale");
                bones.TranslateCurrent(translation);
                Physics.SyncTransforms();
                Assert.That(MinimumBodyContactY(), Is.EqualTo(floorClearance).Within(0.003f),
                    scenario.Name + " physical body must actually lie on the capture floor");
                RigSample lying = new RigSample(presentation.Registry);
                Assert.That(status.DebugBeginRiseFromCurrentPose(), Is.True, scenario.Name);
                RigSample entry = new RigSample(presentation.Registry);
                RecordDeltaViolation(entry, lying, 0.003f, 0.3f, scenario.Name + " frozen-body handoff",
                    discontinuities, ref continuityFailed);
                bool wrongRoute = scenario.ExpectedRoute.HasValue && status.Rise.Route != scenario.ExpectedRoute.Value;
                if (wrongRoute)
                    discontinuities.Add(scenario.Name + " expected " + scenario.ExpectedRoute.Value +
                        " from the measured lying torso, got " + status.Rise.Route);

                status.Rise.DebugPlanSlumps(1);
                status.DebugDownedInput(scenario.Crawl ? new Vector2(0f, 1f) : Vector2.zero);
                ClearSheet();
                int tile = 0;
                int page = 0;
                int totalTiles = 0;
                int crawlFrames = 0;
                int ordinaryFrames = 0;
                int boundaryFrames = 0;
                bool sawSlump = false;
                bool sawSeated = false;
                bool sawTransfer = false;
                bool sawCrawl = false;
                PlayerRiseRoute selectedRoute = status.Rise.Route;
                PlayerRiseStage previousStage = status.Rise.Stage;
                RigSample previous = entry;
                int entryPixels = CaptureSequenceTile(directory, scenario.Name, presentation,
                    ref tile, ref page, ref totalTiles);
                Assert.That(entryPixels, Is.GreaterThan(400), scenario.Name + " visible lying rig");

                for (int frame = 0; frame < 1200; frame++)
                {
                    if (status.Rise != null)
                    {
                        sawSlump |= status.Rise.Output.SlumpActive;
                        sawSeated |= status.Rise.Stage == PlayerRiseStage.Seated;
                        sawTransfer |= status.Rise.Stage == PlayerRiseStage.SeatedToCrawl;
                        if (status.Rise.Stage == PlayerRiseStage.Crawling)
                        {
                            sawCrawl = true;
                            if (++crawlFrames >= 45)
                            {
                                status.DebugDownedInput(Vector2.zero);
                            }
                        }
                    }

                    // One deliberately long frame through each directed rise,
                    // followed by normal frames: the real timeline must remain
                    // continuous when it crosses a stage during a hitch.
                    float dt = frame == 47 ? 1f / 12f : PinnedFrameSeconds;
                    status.DebugAdvanceRise(dt);
                    var current = new RigSample(presentation.Registry);
                    PlayerRiseStage stage = status.Rise != null ? status.Rise.Stage : PlayerRiseStage.Done;
                    float scale = dt / PinnedFrameSeconds;
                    current.AppendDelta(reports, previous, scenario.Name, frame, stage,
                        status.Rise != null ? status.Rise.Route : selectedRoute,
                        status.Rise != null ? status.Rise.Output.ClipTime : 1f);
                    RecordDeltaViolation(current, previous, 0.12f * scale, 16f * scale,
                        scenario.Name + " frame " + frame + " " + previousStage + " -> " + stage,
                        discontinuities, ref continuityFailed);
                    bool changedStage = stage != previousStage;
                    if (changedStage)
                    {
                        boundaryFrames = 6;
                    }
                    if (frame % 10 == 0 || boundaryFrames > 0)
                    {
                        CaptureSequenceTile(directory, scenario.Name, presentation,
                            ref tile, ref page, ref totalTiles);
                        boundaryFrames = Mathf.Max(0, boundaryFrames - 1);
                    }
                    previous = current;
                    previousStage = stage;
                    if (!status.IsFalling && ++ordinaryFrames >= 36)
                    {
                        break;
                    }
                }

                FinishSequence(directory, scenario.Name, tile, page, totalTiles, continuityFailed || wrongRoute);
                File.WriteAllText(Path.Combine(directory, "final-rig-deltas.csv"), reports.ToString());
                Assert.That(status.IsFalling, Is.False, scenario.Name + " must finish");
                Assert.That(ordinaryFrames, Is.GreaterThanOrEqualTo(36), scenario.Name + " follows the handback");
                Assert.That(sawSlump, Is.True, scenario.Name + " exercises a failed effort");
                Assert.That(sawSeated, Is.EqualTo(selectedRoute == PlayerRiseRoute.Seated), scenario.Name);
                Assert.That(sawCrawl, Is.EqualTo(scenario.Crawl), scenario.Name + " held movement");
                Assert.That(sawTransfer, Is.EqualTo(scenario.Crawl && selectedRoute == PlayerRiseRoute.Seated),
                    scenario.Name + " seated crawling needs its own transfer");
                Debug.Log("Recovery sequence " + scenario.Name + ": " + selectedRoute + ", " + totalTiles + " frames");
                DestroyHero();
                groundObject.transform.rotation = Quaternion.identity;
                yield return Frames(2);
            }

            // A live episode complements the controlled geometry: physical
            // motion, freeze, reconciliation, all late writers, final handback.
            PlayerRuntime liveHero = CreateHero();
            var livePresentation = (Player3DCharacterPresentation)liveHero.Visual;
            IntoxicationStatusController liveStatus = CreateStatus(liveHero, 100);
            yield return Frames(3);
            Assert.That(liveStatus.DebugForceLoseBalance(-1f), Is.True);
            bool liveContinuityFailed = false;
            bool sawPhysics = false;
            bool sawRecovery = false;
            int completedFrames = 0;
            int liveTile = 0;
            int livePage = 0;
            int liveTotalTiles = 0;
            int liveBoundaryFrames = 0;
            PlayerRiseStage livePreviousStage = PlayerRiseStage.Settling;
            float livePreviousSampleTime = -1f;
            int livePreviousSampleFrame = -1;
            int liveMaximumFrameGap = 0;
            ClearSheet();
            RigSample last = null;
            for (int frame = 0; frame < 1600; frame++)
            {
                yield return AtFinalPresentation(() =>
                {
                    bool physical = liveHero.Ragdoll.IsSimulating;
                    sawPhysics |= physical;
                    sawRecovery |= liveStatus.Rise != null && liveStatus.Rise.Stage >= PlayerRiseStage.Stirring;
                    float sampleDelta = livePreviousSampleTime >= 0f
                        ? Mathf.Max(0f, Time.time - livePreviousSampleTime)
                        : PinnedFrameSeconds;
                    if (livePreviousSampleFrame >= 0)
                        liveMaximumFrameGap = Mathf.Max(liveMaximumFrameGap, Time.frameCount - livePreviousSampleFrame);
                    var current = new RigSample(livePresentation.Registry);
                    if (last != null && sawRecovery && !physical)
                    {
                        current.AppendDelta(reports, last, "physical-handoff", frame,
                            liveStatus.Rise != null ? liveStatus.Rise.Stage : PlayerRiseStage.Done,
                            liveStatus.Rise != null ? liveStatus.Rise.Route : PlayerRiseRoute.AllFours,
                            liveStatus.Rise != null ? liveStatus.Rise.Output.ClipTime : 1f,
                            sampleDeltaSeconds: sampleDelta, unityFrame: Time.frameCount);
                        // Nested test coroutines can skip a Unity frame. Preserve
                        // the same motion-rate bound over the actual sample interval.
                        float sampleScale = Mathf.Max(1f, sampleDelta / PinnedFrameSeconds);
                        RecordDeltaViolation(current, last, 0.12f * sampleScale, 16f * sampleScale,
                            "live physical recovery frame " + frame,
                            discontinuities, ref liveContinuityFailed);
                    }
                    PlayerRiseStage liveStage = liveStatus.Rise != null ? liveStatus.Rise.Stage : PlayerRiseStage.Done;
                    if (sawRecovery && liveStage != livePreviousStage) liveBoundaryFrames = 6;
                    if (frame % 10 == 0 || liveBoundaryFrames > 0)
                    {
                        CaptureSequenceTile(directory, "physical-handoff", livePresentation,
                            ref liveTile, ref livePage, ref liveTotalTiles);
                        liveBoundaryFrames = Mathf.Max(0, liveBoundaryFrames - 1);
                    }
                    livePreviousStage = liveStage;
                    last = current;
                    livePreviousSampleTime = Time.time;
                    livePreviousSampleFrame = Time.frameCount;
                    if (sawRecovery && !liveStatus.IsFalling) completedFrames++;
                });
                if (completedFrames >= 36) break;
            }
            FinishSequence(directory, "physical-handoff", liveTile, livePage, liveTotalTiles, liveContinuityFailed);
            File.WriteAllText(Path.Combine(directory, "final-rig-deltas.csv"), reports.ToString());
            Debug.Log("Live recovery capture maximum interval: " + liveMaximumFrameGap + " Unity frames.");
            Assert.That(sawPhysics, Is.True);
            Assert.That(sawRecovery, Is.True);
            Assert.That(completedFrames, Is.GreaterThanOrEqualTo(36), "live handback must settle into ordinary sway");
            Assert.That(discontinuities, Is.Empty, string.Join("\n", discontinuities));
        }

        private IEnumerator AtFinalPresentation(System.Action sample)
        {
            // Batch coroutines resume before LateUpdate. Observe the real final
            // rig once, without applying any pose a second time in the test.
            var probe = playerObject.GetComponent<PlayerRecoveryPresentationProbe>() ??
                playerObject.AddComponent<PlayerRecoveryPresentationProbe>();
            bool completed = false;
            System.Exception failure = null;
            probe.Sample = () =>
            {
                try { sample(); }
                catch (System.Exception exception) { failure = exception; }
                finally { completed = true; }
            };
            while (!completed) yield return null;
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private void CaptureSavedToppleRecovery(string directory, StringBuilder reports, List<string> discontinuities)
        {
            PlayerRuntime hero = CreateHero();
            var presentation = (Player3DCharacterPresentation)hero.Visual;
            for (int frame = 0; frame < 36; frame++)
                presentation.DebugAdvanceRecoveryPresentation(PinnedFrameSeconds);

            // The same reproducible save as ShoveForward_ModerateIsRunOffWithLunges:
            // production model output supplies the lunge, moving support polygon,
            // root drift, torso and brace together. Holding an invented 35-degree
            // lean over two stationary feet is not a state this model emits.
            bool continuityFailed = false;
            var model = new PlayerBalanceModel(4242);
            PlayerBalanceInput input = PlayerBalanceInput.Quiet(1f);
            model.InjectPerturbation(new Vector2(0f, 3f));
            ClearSheet();
            int tile = 0;
            int page = 0;
            int totalTiles = 0;
            int recoveringFrames = 0;
            bool sawToppling = false;
            bool sawLunge = false;
            bool sawRecovering = false;
            BalancePhase previousPhase = BalancePhase.Steady;
            RigSample previous = new RigSample(presentation.Registry);
            CaptureSequenceTile(directory, "saved-topple", presentation, ref tile, ref page, ref totalTiles);
            for (int frame = 0; frame < 180; frame++)
            {
                model.Advance(PinnedFrameSeconds, input);
                PlayerBalanceOutput output = model.Output;
                Assert.That(model.LostBalance, Is.False, "the established forward shove must be caught");
                sawToppling |= output.Phase == BalancePhase.Toppling;
                sawLunge |= model.StepIsLunge && output.Step.Active;
                sawRecovering |= sawToppling && output.Phase == BalancePhase.Recovering;

                Transform root = hero.GameObject.transform;
                Vector3 drift = root.right * output.DriftVelocity.x + root.forward * output.DriftVelocity.y;
                playerObject.GetComponent<CharacterController>().Move(drift * PinnedFrameSeconds);
                Physics.SyncTransforms();
                // Measure phase ownership at zero time AFTER the root's legitimate
                // movement; the animation handoff must add no displacement of its own.
                RigSample beforePhase = new RigSample(presentation.Registry);
                presentation.SetBalance(BalancePoseFromOutput(output, root));
                if (output.Phase != previousPhase)
                {
                    presentation.DebugAdvanceRecoveryPresentation(0f);
                    RecordDeltaViolation(new RigSample(presentation.Registry), beforePhase, 0.003f, 0.3f,
                        "saved topple ownership switch to " + output.Phase, discontinuities, ref continuityFailed);
                }
                presentation.DebugAdvanceRecoveryPresentation(PinnedFrameSeconds);
                var current = new RigSample(presentation.Registry);
                current.AppendDelta(reports, previous, "saved-topple", frame, output.Phase,
                    PlayerRiseRoute.AllFours, 0f, presentation.BalancePose);
                RecordDeltaViolation(current, previous, 0.12f, 16f,
                    "saved topple " + output.Phase + " frame " + frame, discontinuities, ref continuityFailed);
                CaptureSequenceTile(directory, "saved-topple", presentation, ref tile, ref page, ref totalTiles);
                previous = current;
                previousPhase = output.Phase;
                if (sawRecovering && ++recoveringFrames >= 48) break;
            }
            FinishSequence(directory, "saved-topple", tile, page, totalTiles, continuityFailed);
            File.WriteAllText(Path.Combine(directory, "final-rig-deltas.csv"), reports.ToString());
            Assert.That(sawToppling && sawLunge && sawRecovering, Is.True,
                "the visual probe must contain a real lunge that saves the topple");
            Assert.That(recoveringFrames, Is.GreaterThanOrEqualTo(48), "follow the saved lunge into ordinary sway");
        }

        private static PlayerBalancePose BalancePoseFromOutput(in PlayerBalanceOutput output, Transform root)
        {
            BalanceStepCommand step = output.Step;
            Vector3 fallWorld = root.right * output.FallAxis.x + root.forward * output.FallAxis.y;
            if (fallWorld.sqrMagnitude > 0.0001f) fallWorld.Normalize();
            else fallWorld = root.right;
            Vector3 braceCentre = root.position + fallWorld * PlayerBalanceController.BraceReachAhead;
            braceCentre.y = PlayerBalanceController.BracePalmClearance;
            bool rightSide = output.FallAxis.x >= 0f;
            float offHand = PlayerBalanceController.BraceOffHandWeight;
            var leftBrace = new PlayerArmReachPose(output.BraceWeight > 0f, false,
                braceCentre - root.right * PlayerBalanceController.BraceShoulderHalfWidth, Vector3.up,
                output.BraceWeight * (rightSide ? offHand : 1f), 0.15f, 0.05f);
            var rightBrace = new PlayerArmReachPose(output.BraceWeight > 0f, true,
                braceCentre + root.right * PlayerBalanceController.BraceShoulderHalfWidth, Vector3.up,
                output.BraceWeight * (rightSide ? 1f : offHand), 0.15f, 0.05f);
            return new PlayerBalancePose(1f, output.LeanRollDegrees, output.LeanPitchDegrees,
                output.Instability, output.ArmReaction, output.CrouchMetres,
                new PlayerBalanceStepPose(step.Active, step.Side, step.Progress, step.From, step.To, step.Lift),
                PlayerWallReachPose.None, output.LeftFoot, output.RightFoot,
                output.TorsoReactionDegrees.x, output.TorsoReactionDegrees.y, output.Phase,
                output.BraceWeight, output.FallAxis, leftBrace, rightBrace);
        }

        private static void RecordDeltaViolation(RigSample current, RigSample previous,
            float metres, float degrees, string context, List<string> discontinuities, ref bool alreadyRecorded)
        {
            if (alreadyRecorded) return;
            try
            {
                current.AssertDelta(previous, metres, degrees, context);
            }
            catch (AssertionException failure)
            {
                // Retain the first broken seam per episode, finish every capture,
                // then fail the selection once with all concrete reproductions.
                // Unexpected exceptions and non-continuity assertions still fail immediately.
                alreadyRecorded = true;
                discontinuities.Add(failure.Message);
                Debug.Log("Recovery continuity violation: " + failure.Message);
            }
        }

        private int CaptureSequenceTile(string directory, string name,
            Player3DCharacterPresentation presentation, ref int tile, ref int page, ref int totalTiles)
        {
            int pixels = CaptureTile(cameraObject.GetComponent<Camera>(), presentation,
                tile++, 0.9f, 0.65f, 1.2f);
            totalTiles++;
            if (tile == Columns * Rows)
            {
                SaveSheet(directory, name + "-" + page.ToString("D2"), tile);
                page++;
                tile = 0;
                ClearSheet();
            }
            return pixels;
        }

        private void FinishSequence(string directory, string name, int tile, int page,
            int totalTiles, bool failed = false)
        {
            if (tile > 0)
                SaveSheet(directory, name + "-" + page.ToString("D2") + (failed ? "-failed" : ""), tile);
            File.WriteAllText(Path.Combine(directory, name + "-index.txt"),
                "Ordered frames: " + totalTiles + "\nPages: " + (page + (tile > 0 ? 1 : 0)) +
                "\nLayout: " + Columns + " columns x " + Rows + " rows, left to right then top to bottom." +
                "\nFinal page frames: " + (tile > 0 ? tile : Columns * Rows) +
                "\nResult: " + (failed ? "first discontinuity captured" : "complete") + "\n");
        }

        private void ClearSheet()
        {
            sheet.SetPixels32(new Color32[TileSize * Columns * TileSize * Rows]);
        }

        private void SaveSheet(string directory, string name, int frames)
        {
            sheet.Apply(false, false);
            string path = Path.Combine(directory, name + ".png");
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Assert.That(new FileInfo(path).Length, Is.GreaterThan(4096), name);
            Debug.Log("Recovery capture " + name + " (" + frames + " ordered frames): " + path);
        }

        private readonly struct RecoveryCase
        {
            public RecoveryCase(string name, float pitch, float roll, PlayerRiseRoute? expectedRoute,
                float faceTilt = 0f, bool crawl = false, bool slope = false, bool obstacle = false)
            {
                Name = name;
                Pitch = pitch;
                Roll = roll;
                ExpectedRoute = expectedRoute;
                FaceTilt = faceTilt;
                Crawl = crawl;
                Slope = slope;
                Obstacle = obstacle;
            }
            public string Name { get; }
            public float Pitch { get; }
            public float Roll { get; }
            public PlayerRiseRoute? ExpectedRoute { get; }
            public float FaceTilt { get; }
            public bool Crawl { get; }
            public bool Slope { get; }
            public bool Obstacle { get; }
        }

        private sealed class RigSample
        {
            private readonly Transform[] bones;
            private readonly Vector3[] positions;
            private readonly Quaternion[] rotations;

            public RigSample(Player3DAssetRegistry registry)
            {
                var ordered = new List<Transform>();
                foreach (Player3DAnatomicalPartBinding part in registry.AnatomicalParts)
                {
                    if (part.Bone != null && !ordered.Contains(part.Bone))
                    {
                        ordered.Add(part.Bone);
                    }
                }
                ordered.Sort((a, b) => Depth(a).CompareTo(Depth(b)));
                bones = ordered.ToArray();
                positions = new Vector3[bones.Length];
                rotations = new Quaternion[bones.Length];
                for (int index = 0; index < bones.Length; index++)
                {
                    positions[index] = bones[index].position;
                    rotations[index] = bones[index].rotation;
                    Assert.That(float.IsNaN(positions[index].sqrMagnitude) ||
                        float.IsInfinity(positions[index].sqrMagnitude), Is.False, bones[index].name);
                }
            }

            public void PlaceRotated(Quaternion rotation, Vector3 centre)
            {
                for (int index = 0; index < bones.Length; index++)
                {
                    bones[index].SetPositionAndRotation(
                        centre + rotation * (positions[index] - centre), rotation * rotations[index]);
                }
            }

            public void TranslateCurrent(Vector3 translation)
            {
                var current = new Vector3[bones.Length];
                for (int index = 0; index < bones.Length; index++) current[index] = bones[index].position;
                for (int index = 0; index < bones.Length; index++) bones[index].position = current[index] + translation;
            }

            public void AssertDelta(RigSample previous, float metres, float degrees, string context)
            {
                for (int index = 0; index < bones.Length; index++)
                {
                    Assert.That(Vector3.Distance(positions[index], previous.positions[index]),
                        Is.LessThanOrEqualTo(metres), context + " " + bones[index].name + " position");
                    Assert.That(Quaternion.Angle(rotations[index], previous.rotations[index]),
                        Is.LessThanOrEqualTo(degrees), context + " " + bones[index].name + " rotation");
                }
            }

            public void AppendDelta(StringBuilder report, RigSample previous, string name, int frame,
                object stage, PlayerRiseRoute route, float clip, PlayerBalancePose? balance = null,
                float sampleDeltaSeconds = float.NaN, int unityFrame = -1)
            {
                float metres = 0f;
                float degrees = 0f;
                string positionBone = "none";
                string rotationBone = "none";
                for (int index = 0; index < bones.Length; index++)
                {
                    float distance = Vector3.Distance(positions[index], previous.positions[index]);
                    float angle = Quaternion.Angle(rotations[index], previous.rotations[index]);
                    if (distance > metres) { metres = distance; positionBone = bones[index].name; }
                    if (angle > degrees) { degrees = angle; rotationBone = bones[index].name; }
                }
                var invariant = System.Globalization.CultureInfo.InvariantCulture;
                report.AppendFormat(invariant,
                    "{0},{1},{2},{3},{4:F5},{5:F5},{6:F3},{7},{8},{9},{10},{11},{12},{13},{14}\n",
                    name, frame, stage, route, clip, metres, degrees, positionBone, rotationBone,
                    balance.HasValue ? balance.Value.Step.Active.ToString() : "",
                    balance.HasValue ? balance.Value.Step.Side.ToString() : "",
                    balance.HasValue ? balance.Value.Step.Progress.ToString("F5", invariant) : "",
                    balance.HasValue ? balance.Value.BraceWeight.ToString("F5", invariant) : "",
                    float.IsNaN(sampleDeltaSeconds) ? "" : sampleDeltaSeconds.ToString("F6", invariant),
                    unityFrame >= 0 ? unityFrame.ToString(invariant) : "");
            }

            private static int Depth(Transform bone)
            {
                int depth = 0;
                while (bone.parent != null) { depth++; bone = bone.parent; }
                return depth;
            }
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
            // Several explicitly sampled poses are rendered inside one Unity
            // frame. Without this, the GPU can reuse the first skin matrices
            // while the bone assertions inspect each later pose correctly.
            foreach (SkinnedMeshRenderer skin in playerObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                skin.forceMatrixRecalculationPerRender = true;
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
            if (obstacleObject != null)
            {
                Object.Destroy(obstacleObject);
                obstacleObject = null;
            }

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

        private float MinimumBodyContactY()
        {
            float minimum = float.PositiveInfinity;
            foreach (Collider collider in playerObject.GetComponentsInChildren<Collider>(true))
            {
                if (!collider.enabled || collider.isTrigger || collider is CharacterController) continue;
                minimum = Mathf.Min(minimum, collider.bounds.min.y);
            }
            Assert.That(float.IsInfinity(minimum), Is.False, "lying placement needs physical body colliders");
            return minimum;
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

    [DefaultExecutionOrder(20000)]
    public sealed class PlayerRecoveryPresentationProbe : MonoBehaviour
    {
        public System.Action Sample;
        private void LateUpdate()
        {
            System.Action pending = Sample;
            Sample = null;
            pending?.Invoke();
        }
    }

    public sealed class PlayerRecoveryAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            System.Type setup = System.Type.GetType(
                "BarPromenade.Editor.Player3DV2AssetSetup, BarPromenade.Editor", true);
            setup.GetMethod("BuildOrThrow", System.Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }
}
