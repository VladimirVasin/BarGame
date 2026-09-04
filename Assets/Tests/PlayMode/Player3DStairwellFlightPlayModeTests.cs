using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// The stairwell flight contract: the hero walks the real stairwell
    /// (down the apartment flight, up the lower flight, and across the
    /// flat lobby) under the keyboard, and every frame the clip pose and
    /// the late-layer pose are measured side by side into
    /// <c>TestResults/stairwell-flight-diagnostic.csv</c>, with side-view
    /// contact sheets for each segment.
    ///
    /// What it pins is the thing numbers alone missed for a whole slice:
    /// on a flight the boots must stay on the treads the probes find and
    /// the knees must stay out of a crouch. The layer once smoothed each
    /// foot's target as a WORLD height at 0.6 m/s while the capsule
    /// carried the body down the hidden ramp at 1.08, so the targets fell
    /// three quarters of a metre behind, both pelvis deltas went positive,
    /// the pelvis pinned at its lift cap and the hero came down the flight
    /// folded double with his boots in the air. Every bound below would
    /// have failed on that.
    /// </summary>
    public sealed class Player3DStairwellFlightPlayModeTests
    {
        private const int TileSize = 512;
        private const float PinnedFrameSeconds = 1f / 60f;
        private const int CaptureLayer = 28;
        private const int MaximumSegmentFrames = 10 * 60;

        /// <summary>
        /// A straight knee is 180 degrees. A stride authored on a floor has
        /// to fold the trailing leg by the riser it is leaving, and this
        /// one spans nearly five treads, so a flight is never as straight
        /// as a floor: measured over the apartment flight the knees run
        /// <c>79/126/166</c> and <c>82/111/145</c> (min/median/max), the
        /// deepest frame being the last stride onto the landing. What this
        /// bars is the sitting tuck — <c>20-70</c> degrees on BOTH legs at
        /// once, boots in the air — that the world-space smoothing
        /// produced. Nine degrees under the measured worst.
        /// </summary>
        private const float KneeInteriorFloorDegrees = 70f;

        /// <summary>
        /// A sole may ride this far over the surface its own probe found:
        /// the clip's own swing lift plus a boot crossing a nosing.
        /// Measured worst <c>0.22 m</c>; the runaway reached <c>0.8</c>.
        /// </summary>
        private const float SoleAboveSurfaceCeiling = 0.30f;

        /// <summary>
        /// And this far into it. The measured worst is <c>-0.014 m</c>; the
        /// slower follow rate this rejects put the sole <c>9 cm</c> inside
        /// a tread, so the bound sits well clear of both.
        /// </summary>
        private const float SoleBelowSurfaceFloor = -0.04f;

        /// <summary>The pelvis holds its height above the root within this band.</summary>
        private const float PelvisAboveRootFloor = 0.60f;
        private const float PelvisAboveRootCeiling = 0.98f;

        /// <summary>Neither pelvis clamp may be reached on a flight.</summary>
        private const float PelvisDropCeiling = 0.30f;

        /// <summary>
        /// The lift half of that, checked separately because it is the one
        /// the DESCENT pinned: a runaway target pushes both deltas positive
        /// and the pelvis sits on <c>DefaultPelvisMaximumLift</c> forever,
        /// which the magnitude bound above is too loose to see. Measured
        /// worst on a good descent is <c>+0.02 m</c>.
        /// </summary>
        private const float PelvisLiftCeiling = 0.08f;

        /// <summary>Frames after motion starts before the layer has blended in.</summary>
        private const int SettleFrames = 15;

        private GameObject stairwellObject;
        private GameObject cameraObject;
        private GameObject lightObject;
        private GameObject playerObject;
        private RenderTexture renderTarget;
        private InputTestFixture inputFixture;
        private Keyboard keyboard;
        private AmbientMode previousAmbientMode;
        private Color previousAmbientLight;
        private Light previousSun;
        private bool previousFog;
        private bool renderSettingsCaptured;
        private bool previousIgnoreCaptureVsProbe;
        private bool ignoreCaptured;
        private Mesh bakeMesh;
        private readonly System.Collections.Generic.List<Vector3> bakeVertices =
            new System.Collections.Generic.List<Vector3>(256);
        private StringBuilder csv;
        private string outputDirectory;
        private readonly float[] frameKneeInterior = new float[2];
        private readonly float[] frameSoleAboveSurface = new float[2];
        private readonly bool[] frameHasSurface = new bool[2];
        private float framePelvisAboveRoot;
        private float framePelvisDrop;
        private SegmentStats stats;
        private bool usedFallbackTeleport;

        private PlayerRuntime hero;
        private Player3DCharacterPresentation presentation;
        private Player3DAssetRegistry registry;
        private Transform leftThigh;
        private Transform leftShin;
        private Transform leftFoot;
        private Transform rightThigh;
        private Transform rightShin;
        private Transform rightFoot;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.captureDeltaTime = PinnedFrameSeconds;
            ResetSession();
            inputFixture = new InputTestFixture();
            inputFixture.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();

            previousAmbientMode = RenderSettings.ambientMode;
            previousAmbientLight = RenderSettings.ambientLight;
            previousSun = RenderSettings.sun;
            previousFog = RenderSettings.fog;
            renderSettingsCaptured = true;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.52f, 0.54f, 0.58f);
            RenderSettings.fog = false;

            cameraObject = new GameObject("Stairwell Flight Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.aspect = 1f;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 40f;
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
                name = "Stairwell Flight Target",
                antiAliasing = 1
            };
            renderTarget.Create();
            camera.targetTexture = renderTarget;

            lightObject = new GameObject("Stairwell Flight Key Light");
            Light keyLight = lightObject.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 1.6f;
            keyLight.color = new Color(1f, 0.86f, 0.74f);
            keyLight.shadows = LightShadows.Hard;
            keyLight.cullingMask = 1 << CaptureLayer;
            lightObject.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            RenderSettings.sun = keyLight;

            bakeMesh = new Mesh { name = "Stairwell Flight Bake" };
            csv = new StringBuilder(1 << 16);
            outputDirectory = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "TestResults"));
            Directory.CreateDirectory(outputDirectory);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (keyboard != null && keyboard.added)
            {
                inputFixture.Release(keyboard.wKey, queueEventOnly: true);
                InputSystem.RemoveDevice(keyboard);
            }

            keyboard = null;
            inputFixture?.TearDown();
            inputFixture = null;

            if (playerObject != null)
            {
                Object.Destroy(playerObject);
                playerObject = null;
            }

            if (stairwellObject != null)
            {
                Object.Destroy(stairwellObject);
                stairwellObject = null;
            }

            if (ignoreCaptured)
            {
                Physics.IgnoreLayerCollision(
                    CaptureLayer,
                    FootProbeSurface.LayerIndex,
                    previousIgnoreCaptureVsProbe);
                ignoreCaptured = false;
            }

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

            if (bakeMesh != null)
            {
                Object.Destroy(bakeMesh);
            }

            if (lightObject != null)
            {
                Object.Destroy(lightObject);
            }

            if (cameraObject != null)
            {
                Object.Destroy(cameraObject);
            }

            ResetSession();
            Time.captureDeltaTime = 0f;
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator StairwellFlight_KeepsTheBootsOnTheTreadsAndTheKneesOutOfACrouch()
        {
            // The REAL stairwell: treads with probe children, hidden ramps,
            // landings, shell. Only the walkable stair pieces go on the
            // capture layer so the side camera sees through the walls.
            StairwellLayoutPlan plan = StairwellLayoutPlanner.Generate();
            StairwellWorldResult world = StairwellWorldBuilder.Build(null, plan);
            stairwellObject = world.Root.gameObject;
            previousIgnoreCaptureVsProbe = Physics.GetIgnoreLayerCollision(
                CaptureLayer,
                FootProbeSurface.LayerIndex);
            ignoreCaptured = true;
            Physics.IgnoreLayerCollision(
                CaptureLayer,
                FootProbeSurface.LayerIndex,
                true);
            int relayered = 0;
            for (int index = 0; index < world.Root.childCount; index++)
            {
                Transform child = world.Root.GetChild(index);
                string name = child.name;
                if (name.Contains(" Stair ") ||
                    name.Contains("Landing") ||
                    name == "Stairwell Ground Floor")
                {
                    SetLayerRecursivelyExceptProbes(child, CaptureLayer);
                    relayered++;
                }
            }

            Debug.Log($"Stairwell flight: relayered {relayered} stair pieces to layer {CaptureLayer}.");
            Transform sampleTread = world.Root.Find("Apartment Stair 08");
            Assert.That(sampleTread, Is.Not.Null, "The builder must name treads as expected.");
            Assert.That(sampleTread.gameObject.layer, Is.EqualTo(CaptureLayer));
            Transform probeChild = sampleTread.Find(FootProbeSurface.ProbeChildName);
            Assert.That(probeChild, Is.Not.Null);
            Assert.That(
                probeChild.gameObject.layer,
                Is.EqualTo(FootProbeSurface.LayerIndex),
                "Relayering must leave the tread probe children on the FootProbe layer.");
            Physics.SyncTransforms();

            Camera camera = cameraObject.GetComponent<Camera>();
            // The first render of a session has no shadow maps.
            camera.transform.position = new Vector3(8f, 3f, 0f);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
            camera.orthographicSize = 1f;
            camera.Render();

            csv.AppendLine(CsvHeader());
            string csvPath = Path.Combine(outputDirectory, "stairwell-flight-diagnostic.csv");
            File.WriteAllText(csvPath, csv.ToString());
            csv.Clear();

            // ---------- Descent: top of the apartment flight, facing +z ----------
            StairwellFlightPlan apartmentFlight = plan.ApartmentFlight;
            CreateHero(
                new Vector3(1.45f, 3.2f + PlayerFactory.GroundedRootOffset, -3.35f),
                yawDegrees: 0f);
            float[] descentHeights = { 3.05f, 2.80f, 2.55f, 2.30f, 2.05f, 1.80f };
            float startY = hero.GameObject.transform.position.y;
            stats = new SegmentStats();
            yield return DriveSegment(
                "descent",
                plan,
                camera,
                viewSide: 1f,
                stopWhen: root => root.position.y <= 1.6f + 0.06f,
                captureHeights: descentHeights,
                captureFramesAfterMotion: null,
                sheetName: "stairwell-descent-sheet.png",
                tileColumns: 3,
                tileRows: 2);
            SegmentStats descent = stats;
            bool descentHandWalked = usedFallbackTeleport;
            float descentDrop = startY - hero.GameObject.transform.position.y;
            File.AppendAllText(csvPath, csv.ToString());
            csv.Clear();
            Debug.Log($"Stairwell flight: descent dropped {descentDrop:F3} m, root now {hero.GameObject.transform.position}.");

            // ---------- Ascent: bottom of the lower flight, facing +z ----------
            // A FRESH hero per segment: the layer's smoothed foot targets
            // chase a surface change at 0.6 m/s, so a teleport would leave
            // the next segment starting with its boots metres in the air.
            inputFixture.Release(keyboard.wKey, queueEventOnly: true);
            yield return null;
            DestroyHero();
            yield return null;
            CreateHero(new Vector3(-1.45f, PlayerFactory.GroundedRootOffset, -3.35f), yawDegrees: 0f);
            float[] ascentHeights = { 0.15f, 0.40f, 0.65f, 0.90f, 1.15f, 1.40f };
            float ascentStartY = hero.GameObject.transform.position.y;
            stats = new SegmentStats();
            yield return DriveSegment(
                "ascent",
                plan,
                camera,
                viewSide: -1f,
                stopWhen: root => root.position.y >= 1.6f - 0.06f,
                captureHeights: ascentHeights,
                captureFramesAfterMotion: null,
                sheetName: "stairwell-ascent-sheet.png",
                tileColumns: 3,
                tileRows: 2);
            SegmentStats ascent = stats;
            bool ascentHandWalked = usedFallbackTeleport;
            float ascentRise = hero.GameObject.transform.position.y - ascentStartY;
            File.AppendAllText(csvPath, csv.ToString());
            csv.Clear();
            Debug.Log($"Stairwell flight: ascent rose {ascentRise:F3} m, root now {hero.GameObject.transform.position}.");

            // ---------- Flat: the street lobby, facing -x ----------
            inputFixture.Release(keyboard.wKey, queueEventOnly: true);
            yield return null;
            DestroyHero();
            yield return null;
            CreateHero(new Vector3(3.2f, PlayerFactory.GroundedRootOffset, -4.0f), yawDegrees: -90f);
            // The lobby is the control: a floor the layer must not touch.
            stats = new SegmentStats();
            float flatStartX = hero.GameObject.transform.position.x;
            yield return DriveSegment(
                "flat",
                plan,
                camera,
                viewSide: -1f,
                stopWhen: root => root.position.x <= flatStartX - 5.0f,
                captureHeights: null,
                captureFramesAfterMotion: new[] { 30, 60, 90 },
                sheetName: "stairwell-flat-sheet.png",
                tileColumns: 3,
                tileRows: 1,
                maximumFrames: 150);
            inputFixture.Release(keyboard.wKey, queueEventOnly: true);
            File.AppendAllText(csvPath, csv.ToString());
            csv.Clear();
            Debug.Log($"Stairwell flight: flat walked {flatStartX - hero.GameObject.transform.position.x:F3} m.");

            Assert.That(File.Exists(csvPath), Is.True);
            Assert.That(File.Exists(Path.Combine(outputDirectory, "stairwell-descent-sheet.png")), Is.True);
            Assert.That(File.Exists(Path.Combine(outputDirectory, "stairwell-ascent-sheet.png")), Is.True);
            Assert.That(File.Exists(Path.Combine(outputDirectory, "stairwell-flat-sheet.png")), Is.True);
            SegmentStats flat = stats;
            bool flatHandWalked = usedFallbackTeleport;
            float flatWalked = flatStartX - hero.GameObject.transform.position.x;
            stats = null;
            Debug.Log(
                "Stairwell flight: " + descent.Describe("descent") + " " +
                ascent.Describe("ascent") + " " + flat.Describe("flat"));

            // A segment driven by the hand-walk fallback proves nothing:
            // PlayerMotor.Teleport stops planar motion, so the gait weights
            // never rise, the walking-frame gate never opens and every
            // bound below would pass on an empty sample.
            Assert.That(
                new[] { descentHandWalked, ascentHandWalked, flatHandWalked },
                Is.All.False,
                "A segment fell back to a per-frame Teleport, so the motor " +
                "never carried the hero and nothing below was measured on " +
                "a real walk.");
            Assert.That(
                descentDrop,
                Is.GreaterThan(1.0f),
                "The hero must actually walk down the apartment flight under W.");
            Assert.That(
                ascentRise,
                Is.GreaterThan(1.0f),
                "The hero must actually walk up the lower flight under W.");

            // The flight itself: every walking frame, both boots.
            AssertFlightIsWalked(descent, "descent");
            AssertFlightIsWalked(ascent, "ascent");

            // And a floor is a floor: the layer stays out of the clip's way.
            Assert.That(
                flatWalked,
                Is.GreaterThan(2.0f),
                "The hero must actually cross the lobby under W; the flat " +
                "control is the only guard on the layer staying invisible " +
                "on a floor.");
            Assert.That(
                flat.Frames,
                Is.GreaterThan(40),
                "The lobby control produced too few walking frames to " +
                "prove anything. " + flat.Describe("flat"));
            Assert.That(
                flat.SoleSamples,
                Is.GreaterThan(flat.Frames),
                "The lobby control measured too few boots against the " +
                "floor. " + flat.Describe("flat"));
            Assert.That(
                flat.WorstKnee,
                Is.GreaterThanOrEqualTo(110f),
                "The lobby walk must keep the authored knees. " +
                flat.Describe("flat"));
            Assert.That(
                flat.MaximumAbsolutePelvisDrop,
                Is.LessThan(0.10f),
                "The pelvis must barely move on a floor. " +
                flat.Describe("flat"));
        }

        private void CreateHero(Vector3 position, float yawDegrees)
        {
            hero = PlayerFactory.Create(
                null,
                position,
                cameraObject.GetComponent<Camera>(),
                null,
                null);
            playerObject = hero.GameObject;
            hero.Motor.enabled = true;
            hero.GameObject.transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            SetLayerRecursivelyExceptProbes(playerObject.transform, CaptureLayer);
            Assert.That(hero.Visual, Is.TypeOf<Player3DCharacterPresentation>());
            presentation = (Player3DCharacterPresentation)hero.Visual;
            registry = presentation.Registry;
            leftThigh = Bone(Player3DAnatomicalPart.LeftThigh);
            leftShin = Bone(Player3DAnatomicalPart.LeftShin);
            leftFoot = Bone(Player3DAnatomicalPart.LeftFoot);
            rightThigh = Bone(Player3DAnatomicalPart.RightThigh);
            rightShin = Bone(Player3DAnatomicalPart.RightShin);
            rightFoot = Bone(Player3DAnatomicalPart.RightFoot);
            // The sole readings are matched to renderers by bone NAME, so a
            // renamed binding would silently feed NaN into every bound
            // below instead of failing here.
            Assert.That(
                LowestMeshY("foot.L"),
                Is.Not.NaN,
                "No visible mesh is bound to foot.L; the sole measurements " +
                "would all be NaN.");
            Assert.That(
                LowestMeshY("foot.R"),
                Is.Not.NaN,
                "No visible mesh is bound to foot.R; the sole measurements " +
                "would all be NaN.");
            Physics.SyncTransforms();
        }

        private void DestroyHero()
        {
            if (playerObject != null)
            {
                Object.Destroy(playerObject);
                playerObject = null;
            }

            hero = default;
            presentation = null;
            registry = null;
        }

        private Transform Bone(Player3DAnatomicalPart part)
        {
            Assert.That(registry.TryGetPart(part, out var binding), Is.True, $"{part} is not registered");
            Assert.That(binding.Bone, Is.Not.Null, $"{part} has no bone");
            return binding.Bone;
        }

        /// <summary>
        /// Stands 30 frames, then holds W until <paramref name="stopWhen"/>
        /// or the frame budget; measures every frame; captures a tile at
        /// each root height in <paramref name="captureHeights"/> (crossed
        /// in order) or at each frame offset after motion starts.
        /// </summary>
        private IEnumerator DriveSegment(
            string segment,
            StairwellLayoutPlan plan,
            Camera camera,
            float viewSide,
            System.Func<Transform, bool> stopWhen,
            float[] captureHeights,
            int[] captureFramesAfterMotion,
            string sheetName,
            int tileColumns,
            int tileRows,
            int maximumFrames = MaximumSegmentFrames)
        {
            Transform root = hero.GameObject.transform;
            var sheet = new Texture2D(
                TileSize * tileColumns,
                TileSize * tileRows,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = segment + " sheet",
                filterMode = FilterMode.Point
            };
            Color32 fill = new Color32(30, 30, 34, 255);
            Color32[] fillPixels = new Color32[sheet.width * sheet.height];
            for (int index = 0; index < fillPixels.Length; index++)
            {
                fillPixels[index] = fill;
            }

            sheet.SetPixels32(fillPixels);

            int tile = 0;
            int tileCount = tileColumns * tileRows;
            int frame = 0;
            float t = 0f;

            // Standing: the layer calibrates and blends in.
            for (int stand = 0; stand < 30; stand++)
            {
                yield return null;
                t += Time.deltaTime;
                presentation.ReapplyLatePresentationPose();
                AppendRow(segment + "-stand", frame++, t, plan, "");
            }

            Vector3 standingRoot = root.position;
            inputFixture.Press(keyboard.wKey, queueEventOnly: true);
            int motionStartFrame = -1;
            int driven = 0;
            bool fallbackTeleport = false;
            usedFallbackTeleport = false;
            Vector3 fallbackForward = root.forward;
            fallbackForward.y = 0f;
            fallbackForward.Normalize();
            while (driven < maximumFrames)
            {
                if (fallbackTeleport)
                {
                    // Nothing moved the capsule under W: walk him by hand
                    // along the ramp line so the clip at least plays.
                    Vector3 next = root.position + fallbackForward * (1.2f * PinnedFrameSeconds);
                    hero.Motor.Teleport(next);
                }

                yield return null;
                driven++;
                t += Time.deltaTime;
                presentation.ReapplyLatePresentationPose();

                bool moved = Vector3.Distance(root.position, standingRoot) > 0.02f;
                if (motionStartFrame < 0 && moved)
                {
                    motionStartFrame = frame;
                    Debug.Log(
                        $"Stairwell flight [{segment}]: motion starts at frame {frame}, " +
                        $"locomotion {presentation.CurrentLocomotionState} blend {presentation.LocomotionBlend:F2}.");
                }

                if (motionStartFrame < 0 && driven == 60 && !fallbackTeleport)
                {
                    Debug.LogWarning($"Stairwell flight [{segment}]: root has not moved after 60 frames of W; falling back to a per-frame Teleport.");
                    fallbackTeleport = true;
                    usedFallbackTeleport = true;
                }

                string tag = "";
                bool capture = false;
                if (tile < tileCount)
                {
                    if (captureHeights != null && tile < captureHeights.Length)
                    {
                        float target = captureHeights[tile];
                        // Descent crosses downward, ascent upward.
                        bool descending = captureHeights[0] > captureHeights[captureHeights.Length - 1];
                        capture = descending
                            ? root.position.y <= target
                            : root.position.y >= target;
                    }
                    else if (captureFramesAfterMotion != null &&
                             tile < captureFramesAfterMotion.Length &&
                             motionStartFrame >= 0)
                    {
                        capture = frame - motionStartFrame >= captureFramesAfterMotion[tile];
                    }
                }

                if (capture)
                {
                    CaptureTile(camera, sheet, tile, tileColumns, tileRows, viewSide);
                    tag = "tile" + tile;
                    tile++;
                }

                AppendRow(segment, frame++, t, plan, tag);
                if (stats != null &&
                    motionStartFrame >= 0 &&
                    frame - motionStartFrame >= SettleFrames &&
                    presentation.LocomotionBlend > 0.5f)
                {
                    stats.Add(
                        frame,
                        frameKneeInterior,
                        frameSoleAboveSurface,
                        frameHasSurface,
                        framePelvisAboveRoot,
                        framePelvisDrop);
                }

                if (stopWhen(root))
                {
                    break;
                }
            }

            Debug.Log(
                $"Stairwell flight [{segment}]: {driven} driven frames, {tile} tiles, " +
                $"root {root.position}, motion start {motionStartFrame}, fallback {fallbackTeleport}.");
            sheet.Apply(false, false);
            File.WriteAllBytes(Path.Combine(outputDirectory, sheetName), sheet.EncodeToPNG());
            Object.Destroy(sheet);
        }

        private static string CsvHeader()
        {
            return string.Join(",",
                "segment", "frame", "t", "tag",
                "rootX", "rootY", "rootZ", "yaw",
                "rampYUnderRoot", "treadYUnderRoot",
                "clipPelvisY", "pelvisY", "pelvisAboveRoot", "pelvisDrop",
                "locoState", "locoBlend", "runBlend", "gaitCycle", "forwardGait", "ikBlend", "soleClearance",
                LegHeader("L"), LegHeader("R"));
        }

        private static string LegHeader(string side)
        {
            return string.Join(",",
                side + "_ankleX", side + "_ankleZ",
                side + "_clipAnkleY", side + "_ankleY",
                side + "_clipSoleY", side + "_soleY",
                side + "_clipKneeInterior", side + "_kneeInterior",
                side + "_clipSignedFlexion", side + "_signedFlexion",
                side + "_kneeForwardOffset",
                side + "_plantPres", side + "_plantLayer",
                side + "_kind", side + "_hasSurface", side + "_heelY", side + "_toeY", side + "_normalY",
                side + "_treadYUnderAnkle", side + "_rampYUnderAnkle",
                side + "_analyticTargetSole", side + "_analyticDelta",
                side + "_locked");
        }

        /// <summary>
        /// One CSV row: the clip pose (after the layer's Restore) and the
        /// solved pose (after ReapplyLatePresentationPose) side by side.
        /// </summary>
        private void AppendRow(
            string segment,
            int frame,
            float t,
            StairwellLayoutPlan plan,
            string tag)
        {
            Transform root = hero.GameObject.transform;
            Player3DProceduralLocomotionLayer layer = presentation.Layer;
            Transform pelvis = registry.Anchors.Pelvis;

            // Clip pose.
            layer.Restore();
            float clipPelvisY = pelvis.position.y;
            LegClip leftClip = MeasureClip(leftThigh, leftShin, leftFoot, "foot.L", FootSide.Left);
            LegClip rightClip = MeasureClip(rightThigh, rightShin, rightFoot, "foot.R", FootSide.Right);

            // Solved pose.
            presentation.ReapplyLatePresentationPose();
            float pelvisY = pelvis.position.y;
            float rootY = root.position.y;
            framePelvisAboveRoot = pelvisY - rootY;
            framePelvisDrop = presentation.PelvisDrop;
            float rampRoot = RampY(plan, root.position);
            float treadRoot = TreadY(plan, root.position);

            var row = new StringBuilder(512);
            row.Append(segment).Append(',').Append(frame).Append(',').Append(F(t)).Append(',').Append(tag).Append(',');
            row.Append(F(root.position.x)).Append(',').Append(F(rootY)).Append(',').Append(F(root.position.z)).Append(',');
            row.Append(F(root.eulerAngles.y)).Append(',');
            row.Append(F(rampRoot)).Append(',').Append(F(treadRoot)).Append(',');
            row.Append(F(clipPelvisY)).Append(',').Append(F(pelvisY)).Append(',').Append(F(pelvisY - rootY)).Append(',').Append(F(presentation.PelvisDrop)).Append(',');
            row.Append(presentation.CurrentLocomotionState).Append(',').Append(F(presentation.LocomotionBlend)).Append(',').Append(F(presentation.RunBlend)).Append(',');
            row.Append(F(presentation.ForwardGaitCycle)).Append(',').Append(presentation.ForwardGaitDominant ? 1 : 0).Append(',');
            row.Append(F(layer.IkBlend)).Append(',').Append(F(layer.SoleClearance)).Append(',');
            AppendLeg(row, plan, leftClip, leftThigh, leftShin, leftFoot, "foot.L", FootSide.Left, presentation.LeftFootPlant, leftClip.SoleY, rightClip.SoleY);
            row.Append(',');
            AppendLeg(row, plan, rightClip, rightThigh, rightShin, rightFoot, "foot.R", FootSide.Right, presentation.RightFootPlant, rightClip.SoleY, leftClip.SoleY);
            csv.AppendLine(row.ToString());
        }

        private struct LegClip
        {
            public float AnkleY;
            public float SoleY;
            public float KneeInterior;
            public float SignedFlexion;
        }

        private LegClip MeasureClip(Transform thigh, Transform shin, Transform foot, string boneName, FootSide side)
        {
            return new LegClip
            {
                AnkleY = foot.position.y,
                SoleY = LowestMeshY(boneName),
                KneeInterior = KneeInterior(thigh, shin, foot),
                SignedFlexion = SignedFlexion(thigh, shin, foot, presentation.DebugKneeForward(side))
            };
        }

        private void AppendLeg(
            StringBuilder row,
            StairwellLayoutPlan plan,
            LegClip clip,
            Transform thigh,
            Transform shin,
            Transform foot,
            string boneName,
            FootSide side,
            float plantPres,
            float ownClipSole,
            float otherClipSole)
        {
            Player3DProceduralLocomotionLayer layer = presentation.Layer;
            FootGroundSample sample = layer.GetSample(side);
            float plantLayer = layer.GetPlant(side);
            Vector3 ankle = foot.position;
            float soleY = LowestMeshY(boneName);
            Vector3 actorForward = hero.GameObject.transform.forward;
            actorForward.y = 0f;
            actorForward.Normalize();
            Vector3 midpoint = 0.5f * (thigh.position + foot.position);
            float kneeForwardOffset = Vector3.Dot(shin.position - midpoint, actorForward);
            float treadY = TreadY(plan, ankle);
            float rampY = RampY(plan, ankle);
            float lift = PlayerFootPlacementRules.ClipLift(ownClipSole, Mathf.Min(ownClipSole, otherClipSole));
            float analyticTarget = float.NaN;
            float analyticDelta = float.NaN;
            if (sample.HasSurface)
            {
                analyticTarget = PlayerFootPlacementRules.TargetSoleHeight(
                    PlayerFootPlacementRules.SupportHeight(sample.Kind, sample.HeelY, sample.ToeY, plantLayer),
                    layer.SoleClearance,
                    lift);
                analyticDelta = analyticTarget - ownClipSole;
            }

            frameKneeInterior[(int)side] = KneeInterior(thigh, shin, foot);
            frameHasSurface[(int)side] = sample.HasSurface;
            frameSoleAboveSurface[(int)side] = sample.HasSurface
                ? soleY - sample.HeelY
                : float.NaN;

            row.Append(F(ankle.x)).Append(',').Append(F(ankle.z)).Append(',');
            row.Append(F(clip.AnkleY)).Append(',').Append(F(ankle.y)).Append(',');
            row.Append(F(clip.SoleY)).Append(',').Append(F(soleY)).Append(',');
            row.Append(F(clip.KneeInterior)).Append(',').Append(F(KneeInterior(thigh, shin, foot))).Append(',');
            row.Append(F(clip.SignedFlexion)).Append(',').Append(F(SignedFlexion(thigh, shin, foot, presentation.DebugKneeForward(side)))).Append(',');
            row.Append(F(kneeForwardOffset)).Append(',');
            row.Append(F(plantPres)).Append(',').Append(F(plantLayer)).Append(',');
            row.Append(sample.Kind).Append(',').Append(sample.HasSurface ? 1 : 0).Append(',');
            row.Append(F(sample.HeelY)).Append(',').Append(F(sample.ToeY)).Append(',').Append(F(sample.Normal.y)).Append(',');
            row.Append(F(treadY)).Append(',').Append(F(rampY)).Append(',');
            row.Append(F(analyticTarget)).Append(',').Append(F(analyticDelta)).Append(',');
            row.Append(layer.IsFootLocked(side) ? 1 : 0);
        }

        private static string F(float value)
        {
            return value.ToString("F4", CultureInfo.InvariantCulture);
        }

        /// <summary>Interior angle at the knee: 180 is straight.</summary>
        private static float KneeInterior(Transform thigh, Transform shin, Transform foot)
        {
            return Vector3.Angle(thigh.position - shin.position, foot.position - shin.position);
        }

        /// <summary>The rise anatomy test's measure: fold from straight, signed by the calibrated knee-forward.</summary>
        private static float SignedFlexion(Transform root, Transform hinge, Transform tip, Vector3 bendDirection)
        {
            Vector3 pivot = hinge.position;
            float flexion = 180f - Vector3.Angle(root.position - pivot, tip.position - pivot);
            Vector3 axis = tip.position - root.position;
            Vector3 offset = Vector3.ProjectOnPlane(pivot - root.position, axis);
            Vector3 anatomical = Vector3.ProjectOnPlane(bendDirection, axis);
            if (offset.sqrMagnitude < 0.000001f || anatomical.sqrMagnitude < 0.000001f)
            {
                return flexion;
            }

            return Vector3.Dot(offset, anatomical) >= 0f ? flexion : -flexion;
        }

        /// <summary>Analytic hidden-ramp height under a planar point, or the landing/lobby height, or NaN.</summary>
        private static float RampY(StairwellLayoutPlan plan, Vector3 point)
        {
            if (TryFlightDistance(plan.ApartmentFlight, point, out float sA))
            {
                return plan.ApartmentFlight.BaseElevation + sA * (plan.ApartmentFlight.StepRise / plan.ApartmentFlight.StepDepth);
            }

            if (TryFlightDistance(plan.LowerFlight, point, out float sL))
            {
                return plan.LowerFlight.BaseElevation + sL * (plan.LowerFlight.StepRise / plan.LowerFlight.StepDepth);
            }

            return LandingY(plan, point);
        }

        /// <summary>Analytic visible-tread top under a planar point, or the landing/lobby height, or NaN.</summary>
        private static float TreadY(StairwellLayoutPlan plan, Vector3 point)
        {
            if (TryFlightDistance(plan.ApartmentFlight, point, out float sA))
            {
                int index = Mathf.Clamp(Mathf.FloorToInt(sA / plan.ApartmentFlight.StepDepth), 0, plan.ApartmentFlight.StepCount - 1);
                return plan.ApartmentFlight.BaseElevation + (index + 1) * plan.ApartmentFlight.StepRise;
            }

            if (TryFlightDistance(plan.LowerFlight, point, out float sL))
            {
                int index = Mathf.Clamp(Mathf.FloorToInt(sL / plan.LowerFlight.StepDepth), 0, plan.LowerFlight.StepCount - 1);
                return plan.LowerFlight.BaseElevation + (index + 1) * plan.LowerFlight.StepRise;
            }

            return LandingY(plan, point);
        }

        private static bool TryFlightDistance(StairwellFlightPlan flight, Vector3 point, out float distance)
        {
            Vector2 planar = new Vector2(point.x, point.z) - flight.Start;
            distance = Vector2.Dot(planar, flight.Direction);
            Vector2 side = new Vector2(-flight.Direction.y, flight.Direction.x);
            float lateral = Mathf.Abs(Vector2.Dot(planar, side));
            return distance >= 0f && distance <= flight.RunLength && lateral <= flight.Width * 0.5f;
        }

        /// <summary>
        /// The landing or lobby under a point. The lobby and the apartment
        /// landing overlap in plan (different floors), so the candidate
        /// nearest the point's own height wins.
        /// </summary>
        private static float LandingY(StairwellLayoutPlan plan, Vector3 point)
        {
            Vector2 planar = new Vector2(point.x, point.z);
            float best = float.NaN;
            float bestDistance = float.PositiveInfinity;
            Consider(plan.MiddleLandingBounds, plan.MiddleElevation);
            Consider(plan.ApartmentLandingBounds, plan.ApartmentElevation);
            Consider(plan.StreetLobbyBounds, plan.StreetElevation);
            return best;

            void Consider(Rect bounds, float elevation)
            {
                if (!bounds.Contains(planar))
                {
                    return;
                }

                float distance = Mathf.Abs(point.y - elevation);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = elevation;
                }
            }
        }

        private float LowestMeshY(string boneName)
        {
            float lowest = float.PositiveInfinity;
            for (int index = 0; index < registry.MeshBindings.Count; index++)
            {
                Player3DMeshBinding binding = registry.MeshBindings[index];
                if (binding == null ||
                    binding.BoneName != boneName ||
                    binding.Renderer == null ||
                    !binding.Renderer.enabled ||
                    !(binding.Renderer is SkinnedMeshRenderer renderer))
                {
                    continue;
                }

                bakeMesh.Clear(false);
                renderer.BakeMesh(bakeMesh, true);
                bakeVertices.Clear();
                bakeMesh.GetVertices(bakeVertices);
                for (int vertex = 0; vertex < bakeVertices.Count; vertex++)
                {
                    lowest = Mathf.Min(lowest, renderer.transform.TransformPoint(bakeVertices[vertex]).y);
                }
            }

            return float.IsPositiveInfinity(lowest) ? float.NaN : lowest;
        }

        private void CaptureTile(
            Camera camera,
            Texture2D sheet,
            int tile,
            int columns,
            int rows,
            float viewSide)
        {
            Transform root = hero.GameObject.transform;
            Vector3 forward = root.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 pelvis = registry.Anchors.Pelvis.position;
            Vector3 focus = new Vector3(root.position.x, pelvis.y - 0.35f, root.position.z);
            camera.transform.position = focus + right * (viewSide * 10f);
            camera.transform.rotation = Quaternion.LookRotation(focus - camera.transform.position, Vector3.up);
            camera.orthographicSize = 1.0f;
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTarget;
            var result = new Texture2D(TileSize, TileSize, TextureFormat.RGBA32, false, true);
            result.ReadPixels(new Rect(0f, 0f, TileSize, TileSize), 0, 0, false);
            result.Apply(false, false);
            RenderTexture.active = previous;
            int x = (tile % columns) * TileSize;
            int y = (rows - 1 - tile / columns) * TileSize;
            sheet.SetPixels(x, y, TileSize, TileSize, result.GetPixels());
            Object.Destroy(result);
        }

        private static void SetLayerRecursivelyExceptProbes(Transform root, int layer)
        {
            if (root.name == FootProbeSurface.ProbeChildName)
            {
                return;
            }

            root.gameObject.layer = layer;
            for (int index = 0; index < root.childCount; index++)
            {
                SetLayerRecursivelyExceptProbes(root.GetChild(index), layer);
            }
        }

        /// <summary>
        /// What one segment's walking frames did, kept whole so a failure
        /// can name the worst frame and the distribution around it rather
        /// than one number out of context.
        /// </summary>
        private sealed class SegmentStats
        {
            private readonly System.Collections.Generic.List<float>[] knees =
            {
                new System.Collections.Generic.List<float>(256),
                new System.Collections.Generic.List<float>(256)
            };

            private readonly System.Collections.Generic.List<float>[] soles =
            {
                new System.Collections.Generic.List<float>(256),
                new System.Collections.Generic.List<float>(256)
            };

            public int Frames { get; private set; }
            public int WorstKneeFrame { get; private set; } = -1;
            public float WorstKnee { get; private set; } = 180f;
            public int WorstSoleFrame { get; private set; } = -1;
            public float WorstSoleAbove { get; private set; }
            public float WorstSoleBelow { get; private set; }
            public float MinimumPelvisAboveRoot { get; private set; } =
                float.PositiveInfinity;

            public float MaximumPelvisAboveRoot { get; private set; } =
                float.NegativeInfinity;

            public float MaximumAbsolutePelvisDrop { get; private set; }
            public float MaximumPelvisLift { get; private set; }
            public int SoleSamples { get; private set; }

            public void Add(
                int frame,
                float[] kneeInterior,
                float[] soleAboveSurface,
                bool[] hasSurface,
                float pelvisAboveRoot,
                float pelvisDrop)
            {
                Frames++;
                MinimumPelvisAboveRoot = Mathf.Min(
                    MinimumPelvisAboveRoot,
                    pelvisAboveRoot);
                MaximumPelvisAboveRoot = Mathf.Max(
                    MaximumPelvisAboveRoot,
                    pelvisAboveRoot);
                MaximumAbsolutePelvisDrop = Mathf.Max(
                    MaximumAbsolutePelvisDrop,
                    Mathf.Abs(pelvisDrop));
                MaximumPelvisLift = Mathf.Max(MaximumPelvisLift, pelvisDrop);
                for (int side = 0; side < 2; side++)
                {
                    knees[side].Add(kneeInterior[side]);
                    if (kneeInterior[side] < WorstKnee)
                    {
                        WorstKnee = kneeInterior[side];
                        WorstKneeFrame = frame;
                    }

                    if (!hasSurface[side])
                    {
                        continue;
                    }

                    float above = soleAboveSurface[side];
                    if (float.IsNaN(above))
                    {
                        continue;
                    }

                    SoleSamples++;
                    soles[side].Add(above);
                    if (above > WorstSoleAbove)
                    {
                        WorstSoleAbove = above;
                        WorstSoleFrame = frame;
                    }

                    WorstSoleBelow = Mathf.Min(WorstSoleBelow, above);
                }
            }

            public string Describe(string segment)
            {
                return $"{segment}: {Frames} walking frames, knee interior " +
                       $"(min/median/max) L {Range(knees[0])} R {Range(knees[1])}, " +
                       $"worst {WorstKnee:F1} deg at frame {WorstKneeFrame}; " +
                       $"sole over its probed surface L {Range(soles[0])} " +
                       $"R {Range(soles[1])}, highest {WorstSoleAbove:F3} m at " +
                       $"frame {WorstSoleFrame}, deepest {WorstSoleBelow:F3} m; " +
                       $"{SoleSamples} sole samples; " +
                       $"pelvis above root {MinimumPelvisAboveRoot:F3}.." +
                       $"{MaximumPelvisAboveRoot:F3}; max |pelvis drop| " +
                       $"{MaximumAbsolutePelvisDrop:F3}, max lift " +
                       $"{MaximumPelvisLift:F3}.";
            }

            private static string Range(
                System.Collections.Generic.List<float> values)
            {
                if (values.Count == 0)
                {
                    return "none";
                }

                var sorted = new System.Collections.Generic.List<float>(values);
                sorted.Sort();
                return $"{sorted[0]:F1}/{sorted[sorted.Count / 2]:F1}/" +
                       $"{sorted[sorted.Count - 1]:F1}";
            }
        }

        /// <summary>
        /// Every walking frame of a flight, both boots: a knee out of a
        /// crouch, a sole on the surface its own probe found, and a pelvis
        /// holding its height above the capsule instead of pinning at
        /// either clamp.
        /// </summary>
        private static void AssertFlightIsWalked(
            SegmentStats segment,
            string name)
        {
            string described = segment.Describe(name);
            Assert.That(
                segment.Frames,
                Is.GreaterThan(40),
                "The segment produced too few walking frames to prove " +
                "anything. " + described);
            Assert.That(
                segment.SoleSamples,
                Is.GreaterThan(segment.Frames),
                "Most frames must measure BOTH boots against a probed " +
                "surface, or the sole bounds below prove nothing: a layer " +
                "that stopped finding ground falls back to a flat plane " +
                "carried by the capsule and looks like a clean floor walk. " +
                described);
            Assert.That(
                segment.WorstKnee,
                Is.GreaterThanOrEqualTo(KneeInteriorFloorDegrees),
                "A knee folded past a stair walk into a crouch. " + described);
            Assert.That(
                segment.WorstSoleAbove,
                Is.LessThanOrEqualTo(SoleAboveSurfaceCeiling),
                "A boot rode above the tread its own probe found. " + described);
            Assert.That(
                segment.WorstSoleBelow,
                Is.GreaterThanOrEqualTo(SoleBelowSurfaceFloor),
                "A boot sank into the tread its own probe found. " + described);
            Assert.That(
                segment.MinimumPelvisAboveRoot,
                Is.GreaterThanOrEqualTo(PelvisAboveRootFloor),
                "The pelvis squatted on the flight. " + described);
            Assert.That(
                segment.MaximumPelvisAboveRoot,
                Is.LessThanOrEqualTo(PelvisAboveRootCeiling),
                "The pelvis rode up off the flight. " + described);
            Assert.That(
                segment.MaximumAbsolutePelvisDrop,
                Is.LessThan(PelvisDropCeiling),
                "The pelvis reached a clamp, which means it spent the " +
                "flight chasing something it could not catch. " + described);
            Assert.That(
                segment.MaximumPelvisLift,
                Is.LessThan(PelvisLiftCeiling),
                "The pelvis rode up on its lift clamp, the signature of " +
                "foot targets left behind above the treads. " + described);
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
