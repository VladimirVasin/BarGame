using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class ColdHeroAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type setup = Type.GetType(
                "BarPromenade.Editor.Player3DV2AssetSetup, BarPromenade.Editor", true);
            setup.GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Focused cold hero integration and rendered village captures. Run alone.")]
        [PrebuildSetup(typeof(ColdHeroAssetsSetup))]
        public IEnumerator AlpineVillageColdHero()
        {
            AssertColdClockContract();
            float previousCaptureDelta = Time.captureDeltaTime;
            AlpineVillageRoot village = null;
            Camera camera = null;
            Vector3 cameraPosition = default;
            Quaternion cameraRotation = default;
            float cameraFieldOfView = 60f;
            bool cameraFollowEnabled = false;
            bool motorEnabled = false;
            bool statusEnabled = false;
            bool statusStateCaptured = false;
            IDisposable pause = null;
            IDisposable hidden = null;
            PlayerColdArmSeparationProbe armSeparation = null;
            try
            {
                GameSessionState.BeginNewGame();
                Assert.That(GameSessionState.TryStartGameTimeFromWake(), Is.True);
                Time.captureDeltaTime = 1f / 60f;
                AsyncOperation load = SceneManager.LoadSceneAsync(
                    SceneIds.AlpineVillage, LoadSceneMode.Single);
                Assert.That(load, Is.Not.Null);
                float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
                while (Time.realtimeSinceStartup < deadline)
                {
                    village = Object.FindAnyObjectByType<AlpineVillageRoot>();
                    if (load.isDone && village != null && village.IsInitialized)
                        break;
                    yield return null;
                }

                Assert.That(village, Is.Not.Null);
                Assert.That(village.IsInitialized, Is.True);
                var hero = village.Player.Visual as Player3DCharacterPresentation;
                Assert.That(hero, Is.Not.Null);
                Assert.That(hero.Registry.TryGetAnimation("ColdShiver", out var shiver), Is.True);
                Assert.That(shiver.Clip, Is.Not.Null);
                Assert.That(shiver.AuthoredDuration,
                    Is.EqualTo(PlayerColdPresentationModel.ShiverDurationSeconds));
                Assert.That(shiver.Looping, Is.False);
                Assert.That(hero.ColdBreath, Is.Not.Null);
                Assert.That(hero.ColdBreath.MouthAnchor,
                    Is.SameAs(hero.Registry.Anchors.Mouth));
                Assert.That(hero.ColdBreath.BreathRenderer.sharedMaterial,
                    Is.SameAs(CityNightResources.AtmosphereMaterial));
                Assert.That(hero.ColdBreath.Particles.main.simulationSpace,
                    Is.EqualTo(ParticleSystemSimulationSpace.World));
                Assert.That(hero.ColdBreath.Particles.main.useUnscaledTime, Is.False);
                Assert.That(hero.ColdBreath.Particles.main.maxParticles,
                    Is.EqualTo(PlayerColdBreathEffect.MaximumParticles));

                motorEnabled = village.Player.Motor.enabled;
                village.Player.Motor.enabled = false;
                hero.SetMotion(PlayerMotionSample.Stationary);
                armSeparation = new PlayerColdArmSeparationProbe(hero.Registry);
                // Recreate the same exterior eligibility at zero layer weight
                // so the numerical probe includes the full ordinary-to-cold blend.
                hero.ConfigureCold(
                    () => !GameSessionState.IsRidingAVehicle &&
                          (village.CabinSeat == null || !village.CabinSeat.IsSeated) &&
                          !SceneTransitionService.IsTransitioning &&
                          !village.Player.PresentationVisibility.RenderersHidden,
                    () => village.Weather.CurrentWind);
                yield return ColdHeroFrames(45, hero, armSeparation);
                Assert.That(hero.ColdBodyWeight, Is.GreaterThan(0.98f));
                Assert.That(hero.ColdArmWeight, Is.GreaterThan(0.98f),
                    "The village arrival and station canopy must retain the cold pose.");

                camera = Camera.main;
                Assert.That(camera, Is.Not.Null);
                cameraPosition = camera.transform.position;
                cameraRotation = camera.transform.rotation;
                cameraFieldOfView = camera.fieldOfView;
                cameraFollowEnabled = village.CameraFollow.enabled;
                SeekColdHero(hero, 2.1f);
                yield return ColdHeroFrames(10, hero, armSeparation);
                Assert.That(hero.ColdBreath.Particles.particleCount, Is.GreaterThan(0));
                CaptureColdHero(camera, hero,
                    "cold-00-idle-game-camera");
                village.CameraFollow.enabled = false;
                AimAtColdHero(camera, village.Player.GameObject.transform,
                    new Vector3(2.25f, 1.45f, 2.6f));
                CaptureColdHero(camera, hero,
                    "cold-01-idle-three-quarter-exhale");
                AimAtColdHero(camera, village.Player.GameObject.transform,
                    new Vector3(3.1f, 1.4f, 0.1f));
                CaptureColdHero(camera, hero,
                    "cold-02-idle-side-exhale");
                AssertColdShoulderReach(hero, "idle");

                // Both the animation clock and already emitted world particles
                // freeze under the same real pause lease as the game uses.
                pause = GameTimeScaleRuntime.AcquirePause();
                yield return null;
                hero.ReapplyLatePresentationPose();
                double pausedClock = hero.ColdModel.ElapsedSeconds;
                float pausedParticles = hero.ColdBreath.Particles.time;
                Vector3 pausedMouth = hero.Registry.Anchors.Mouth.position;
                yield return ColdHeroFrames(4, hero, armSeparation);
                Assert.That(hero.ColdModel.ElapsedSeconds, Is.EqualTo(pausedClock));
                Assert.That(hero.ColdBreath.Particles.time,
                    Is.EqualTo(pausedParticles).Within(0.00001f));
                Assert.That(Vector3.Distance(pausedMouth,
                    hero.Registry.Anchors.Mouth.position), Is.LessThan(0.0001f));
                pause.Dispose();
                pause = null;

                AimAtColdHero(camera, village.Player.GameObject.transform,
                    new Vector3(2.25f, 1.45f, 2.6f));
                // Photograph opposite ends of the first stroke after the
                // quarter-second mixer settle, rather than two nearby lows.
                SeekColdHero(hero, PlayerColdPresentationModel.FirstShoulderRubSeconds + 0.17f);
                yield return ColdHeroFrames(15, hero, armSeparation);
                Assert.That(hero.ColdModel.IsRubbing, Is.True);
                Assert.That(hero.ColdModel.RubWeight01, Is.GreaterThan(0.98f));
                AssertColdShoulderReach(hero, "rub-first-stroke");
                Vector3 firstRubGrip = hero.Registry.Anchors.LeftGrip.position;
                CaptureColdHero(camera, hero,
                    "cold-03-rub-first-stroke");
                SeekColdHero(hero, PlayerColdPresentationModel.FirstShoulderRubSeconds + 0.58f);
                yield return ColdHeroFrames(15, hero, armSeparation);
                AssertColdShoulderReach(hero, "rub-return-stroke");
                Assert.That(Vector3.Distance(firstRubGrip,
                    hero.Registry.Anchors.LeftGrip.position), Is.GreaterThan(0.012f),
                    "The imported rub clip must actually move the hand along the sleeve.");
                CaptureColdHero(camera, hero,
                    "cold-04-rub-return-stroke");

                SeekColdHero(hero, 4.9f);
                yield return CaptureColdShiver(camera, hero, armSeparation);

                // Observe the natural cadence, including low continuous hand
                // motion between stronger strokes, through the final rig.
                SeekColdHero(hero, 0f);
                yield return CaptureColdMotion(camera, hero, armSeparation,
                    "cold-idle-motion", 480);

                var motions = new[]
                {
                    new PlayerMotionSample(Vector3.forward * 2.6f, 2.6f, 0f),
                    new PlayerMotionSample(Vector3.back * 1.4f, -1.4f, 0f),
                    new PlayerMotionSample(Vector3.zero, 0f, -1f),
                    new PlayerMotionSample(Vector3.zero, 0f, 1f),
                    new PlayerMotionSample(Vector3.forward * 4.2f, 4.2f, 0f, 1f)
                };
                var states = new[]
                {
                    Player3DLocomotionState.Walk, Player3DLocomotionState.WalkBack,
                    Player3DLocomotionState.TurnLeft, Player3DLocomotionState.TurnRight,
                    Player3DLocomotionState.Run
                };
                for (int index = 0; index < motions.Length; index++)
                {
                    if (states[index] == Player3DLocomotionState.Run)
                    {
                        // Enter running during a stroke: the warming gesture
                        // must continue over the running leg animation.
                        SeekColdHero(hero,
                            PlayerColdPresentationModel.FirstShoulderRubSeconds + 0.2f);
                        yield return ColdHeroFrames(15, hero, armSeparation);
                        Assert.That(hero.ColdModel.IsRubbing, Is.True);
                    }
                    hero.SetMotion(motions[index]);
                    yield return ColdHeroFrames(45, hero, armSeparation);
                    Assert.That(hero.CurrentLocomotionState, Is.EqualTo(states[index]));
                    Assert.That(hero.ColdBodyWeight, Is.GreaterThan(0.98f));
                    if (states[index] == Player3DLocomotionState.Run)
                    {
                        Assert.That(hero.ColdArmWeight, Is.GreaterThan(0.98f));
                        Assert.That(hero.ColdModel.IsRubbing, Is.True,
                            "Running must not cancel an in-progress shoulder rub.");
                        AssertColdShoulderReach(hero, "running");
                        SeekColdHero(hero, 0f);
                        yield return CaptureColdMotion(camera, hero, armSeparation,
                            "cold-run-motion", 480);
                    }
                    else
                    {
                        Assert.That(hero.ColdArmWeight, Is.GreaterThan(0.98f));
                    }
                    CaptureColdHero(camera, hero,
                        "cold-" + (5 + index).ToString("00") + "-" + states[index]);
                }

                hero.SetMotion(PlayerMotionSample.Stationary);
                yield return ColdHeroFrames(45, hero, armSeparation);
                statusEnabled = village.IntoxicationStatus.enabled;
                statusStateCaptured = true;
                village.IntoxicationStatus.enabled = false;
                hero.SetNausea(new PlayerNauseaPose(true, 1f, 0f, 0f));
                // The controller normally publishes after the hero Update:
                // the late pass must release the hug in this very frame.
                armSeparation.Sample(hero);
                Assert.That(hero.ColdArmWeight, Is.Zero);
                Assert.That(hero.ColdBodyWeight, Is.Zero);
                yield return ColdHeroFrames(45, hero, armSeparation);
                Assert.That(hero.ColdBodyWeight, Is.Zero,
                    "A protective nausea pose owns the torso before the cold pose.");
                Assert.That(hero.ColdArmWeight, Is.Zero);
                Assert.That(hero.ColdBreath.IsEmissionEnabled, Is.False);
                CaptureColdHero(camera, hero,
                    "cold-12-protective-pose-priority");
                hero.SetNausea(PlayerNauseaPose.None);
                village.IntoxicationStatus.enabled = statusEnabled;
                yield return ColdHeroFrames(45, hero, armSeparation);
                hero.SetInteractionHandoffLocked(true);
                yield return ColdHeroFrames(2, hero, armSeparation);
                Assert.That(hero.ColdBodyWeight, Is.Zero);
                Assert.That(hero.ColdArmWeight, Is.Zero);
                Assert.That(hero.ColdBreath.IsEmissionEnabled, Is.False);
                Assert.That(hero.ColdBreath.Particles.particleCount, Is.Zero);
                CaptureColdHero(camera, hero,
                    "cold-10-neutral-interaction-handoff");
                hero.SetInteractionHandoffLocked(false);
                yield return ColdHeroFrames(45, hero, armSeparation);
                Assert.That(hero.ColdArmWeight, Is.GreaterThan(0.98f));

                hidden = village.Player.PresentationVisibility.AcquireHidden(this);
                yield return ColdHeroFrames(2, hero, armSeparation);
                Assert.That(hero.ColdBreath.IsEmissionEnabled, Is.False);
                Assert.That(hero.ColdBreath.Particles.particleCount, Is.Zero);
                hidden.Dispose();
                hidden = null;
                yield return ColdHeroFrames(45, hero, armSeparation);
                Assert.That(hero.ColdArmWeight, Is.GreaterThan(0.98f));

                hero.ConfigureCold(() => false, () => village.Weather.CurrentWind);
                yield return ColdHeroFrames(2, hero, armSeparation);
                Assert.That(hero.ColdBodyWeight, Is.Zero);
                Assert.That(hero.ColdBreath.IsEmissionEnabled, Is.False);
                Assert.That(hero.ColdBreath.Particles.particleCount, Is.Zero);
                CaptureColdHero(camera, hero,
                    "cold-11-exterior-profile-disabled");
                hero.ConfigureCold(() => true, () => village.Weather.CurrentWind);
                yield return ColdHeroFrames(45, hero, armSeparation);
                Assert.That(hero.ColdBodyWeight, Is.GreaterThan(0.98f));
                Assert.That(hero.ColdArmWeight, Is.GreaterThan(0.98f));
                hero.ColdBreath.enabled = false;
                Assert.That(hero.ColdBreath.Particles.particleCount, Is.Zero);
                hero.ColdBreath.enabled = true;
                armSeparation.AssertNoInterpenetration();
            }
            finally
            {
                pause?.Dispose();
                hidden?.Dispose();
                armSeparation?.Dispose();
                Time.captureDeltaTime = previousCaptureDelta;
                if (camera != null)
                {
                    camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                    camera.fieldOfView = cameraFieldOfView;
                }
                if (village != null && village.IsInitialized)
                {
                    village.Player.Motor.enabled = motorEnabled;
                    village.CameraFollow.enabled = cameraFollowEnabled;
                    if (statusStateCaptured)
                        village.IntoxicationStatus.enabled = statusEnabled;
                    var hero = village.Player.Visual as Player3DCharacterPresentation;
                    if (hero != null)
                    {
                        hero.SetNausea(PlayerNauseaPose.None);
                        hero.SetInteractionHandoffLocked(false);
                        hero.ConfigureCold(
                            () => !GameSessionState.IsRidingAVehicle &&
                                  (village.CabinSeat == null || !village.CabinSeat.IsSeated) &&
                                  !SceneTransitionService.IsTransitioning &&
                                  !village.Player.PresentationVisibility.RenderersHidden,
                            () => village.Weather.CurrentWind);
                    }
                }
                GameSessionState.BeginNewGame();
            }
        }

        private static IEnumerator ColdHeroFrames(int count,
            Player3DCharacterPresentation hero = null,
            PlayerColdArmSeparationProbe armSeparation = null)
        {
            for (int frame = 0; frame < count; frame++)
            {
                yield return null;
                if (hero != null && armSeparation != null) armSeparation.Sample(hero);
            }
        }

        private static IEnumerator CaptureColdMotion(Camera camera,
            Player3DCharacterPresentation hero, PlayerColdArmSeparationProbe armSeparation,
            string folder, int frameCount)
        {
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(),
                "Captures", SceneIds.AlpineVillage, folder));
            Assert.That(hero.Registry.TryGetPart(Player3DAnatomicalPart.RightUpperArm,
                out var rightSleeve), Is.True);
            Assert.That(hero.Registry.TryGetPart(Player3DAnatomicalPart.LeftUpperArm,
                out var leftSleeve), Is.True);
            Bounds leftTravel = default, rightTravel = default;
            Bounds leftHeldTravel = default, rightHeldTravel = default;
            int heldSamples = 0, rubSamples = 0, shiverSamples = 0;
            bool shiverIsolationChecked = false;
            float previousAspect = camera.aspect;
            try
            {
                camera.aspect = 16f / 9f;
                for (int frame = 0; frame < frameCount; frame++)
                {
                    yield return null;
                    armSeparation.Sample(hero);
                    Assert.That(hero.ColdArmWeight, Is.GreaterThan(0.98f));
                    Vector3 left = GripInSleeveSpace(rightSleeve.Bone,
                        hero.Registry.Anchors.LeftGrip.position);
                    Vector3 right = GripInSleeveSpace(leftSleeve.Bone,
                        hero.Registry.Anchors.RightGrip.position);
                    if (frame == 0)
                    {
                        leftTravel = new Bounds(left, Vector3.zero);
                        rightTravel = new Bounds(right, Vector3.zero);
                    }
                    leftTravel.Encapsulate(left);
                    rightTravel.Encapsulate(right);
                    if (hero.ColdModel.IsShivering)
                    {
                        shiverSamples++;
                        Assert.That(hero.ColdModel.IsRubbing, Is.False);
                        if (!shiverIsolationChecked && hero.ColdModel.ShiverWeight01 > 0.98f &&
                            hero.ColdModel.ShiverNormalizedTime > 0.4f)
                        {
                            AssertColdShiverLowerBodyIsolation(hero);
                            shiverIsolationChecked = true;
                        }
                    }
                    else if (hero.ColdModel.IsRubbing) rubSamples++;
                    else
                    {
                        if (heldSamples++ == 0)
                        {
                            leftHeldTravel = new Bounds(left, Vector3.zero);
                            rightHeldTravel = new Bounds(right, Vector3.zero);
                        }
                        leftHeldTravel.Encapsulate(left);
                        rightHeldTravel.Encapsulate(right);
                    }
                    if (frame % 3 == 0)
                        CaptureColdHero(camera, hero, $"{folder}/{frame / 3:000}");
                }
                Assert.That(rubSamples, Is.GreaterThan(120), "The first rub must arrive promptly.");
                Assert.That(shiverSamples, Is.GreaterThan(50), "Observe the first complete chill.");
                Assert.That(shiverIsolationChecked, Is.True);
                Assert.That(heldSamples, Is.GreaterThan(90), "Keep quieter movement between the series.");
                Assert.That(leftTravel.size.magnitude, Is.GreaterThan(0.035f),
                    "The left palm must make a clearly visible stroke along the opposite sleeve.");
                Assert.That(rightTravel.size.magnitude, Is.GreaterThan(0.035f),
                    "The right palm must also rub, not remain glued to the shoulder.");
                Assert.That(leftHeldTravel.size.magnitude, Is.GreaterThan(0.006f));
                Assert.That(rightHeldTravel.size.magnitude, Is.GreaterThan(0.006f));
                TestContext.Out.WriteLine($"{folder}: sleeve-relative hand spans " +
                    $"L={leftTravel.size.magnitude:F4}, R={rightTravel.size.magnitude:F4} m; " +
                    $"between rubs L={leftHeldTravel.size.magnitude:F4}, " +
                    $"R={rightHeldTravel.size.magnitude:F4} m; {frameCount / 3} frames at 20 fps.");
            }
            finally { camera.aspect = previousAspect; }
        }

        private static IEnumerator CaptureColdShiver(Camera camera,
            Player3DCharacterPresentation hero, PlayerColdArmSeparationProbe armSeparation)
        {
            const string folder = "cold-shiver-motion";
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(),
                "Captures", SceneIds.AlpineVillage, folder));
            Assert.That(hero.Registry.TryGetPart(Player3DAnatomicalPart.Torso,
                out var torso), Is.True);
            Assert.That(hero.Registry.TryGetPart(Player3DAnatomicalPart.LeftUpperArm,
                out var leftShoulder), Is.True);
            Assert.That(hero.Registry.TryGetPart(Player3DAnatomicalPart.RightUpperArm,
                out var rightShoulder), Is.True);
            Bounds leftTravel = default, rightTravel = default;
            Quaternion firstChest = default;
            float chestTravel = 0f;
            int contractionSamples = 0;
            float previousAspect = camera.aspect;
            try
            {
                camera.aspect = 16f / 9f;
                // 4.9–7.2 seconds: quiet hold, one entire chill, then recovery.
                for (int frame = 0; frame < 138; frame++)
                {
                    yield return null;
                    armSeparation.Sample(hero);
                    if (hero.ColdModel.IsShivering && hero.ColdModel.ShiverWeight01 > 0.95f)
                    {
                        Assert.That(hero.ColdModel.IsRubbing, Is.False);
                        Vector3 left = GripInSleeveSpace(torso.Bone, leftShoulder.Bone.position);
                        Vector3 right = GripInSleeveSpace(torso.Bone, rightShoulder.Bone.position);
                        if (contractionSamples++ == 0)
                        {
                            leftTravel = new Bounds(left, Vector3.zero);
                            rightTravel = new Bounds(right, Vector3.zero);
                            firstChest = torso.Bone.localRotation;
                        }
                        leftTravel.Encapsulate(left);
                        rightTravel.Encapsulate(right);
                        chestTravel = Mathf.Max(chestTravel,
                            Quaternion.Angle(firstChest, torso.Bone.localRotation));
                    }
                    if (frame % 3 == 0)
                        CaptureColdHero(camera, hero, $"{folder}/{frame / 3:000}");
                }
                Assert.That(contractionSamples, Is.GreaterThan(30));
                Assert.That(leftTravel.size.magnitude, Is.GreaterThan(0.006f),
                    "The left shoulder must contract relative to the chest, beyond body sway.");
                Assert.That(rightTravel.size.magnitude, Is.GreaterThan(0.006f));
                Assert.That(chestTravel, Is.GreaterThan(0.5f),
                    "The imported chill must visibly contract the chest as well as the shoulders.");
                Assert.That(hero.ColdModel.IsShivering, Is.False);
                TestContext.Out.WriteLine($"Shiver: shoulder spans L={leftTravel.size.magnitude:F4}, " +
                    $"R={rightTravel.size.magnitude:F4} m; chest={chestTravel:F2} degrees; " +
                    "46 frames at 20 fps, clock 4.9–7.2 s.");
            }
            finally { camera.aspect = previousAspect; }
        }

        private static void AssertColdShiverLowerBodyIsolation(Player3DCharacterPresentation hero)
        {
            var evaluate = typeof(Player3DCharacterPresentation).GetMethod("EvaluateGraph",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(evaluate, Is.Not.Null);
            var parts = new[]
            {
                Player3DAnatomicalPart.Pelvis,
                Player3DAnatomicalPart.LeftThigh, Player3DAnatomicalPart.LeftShin,
                Player3DAnatomicalPart.LeftFoot, Player3DAnatomicalPart.RightThigh,
                Player3DAnatomicalPart.RightShin, Player3DAnatomicalPart.RightFoot
            };
            var transforms = new Transform[parts.Length + 2];
            transforms[0] = hero.transform;
            transforms[1] = hero.Registry.ModelRoot;
            for (int index = 0; index < parts.Length; index++)
            {
                Assert.That(hero.Registry.TryGetPart(parts[index], out var part), Is.True);
                transforms[index + 2] = part.Bone;
            }
            var positions = new Vector3[transforms.Length];
            var rotations = new Quaternion[transforms.Length];
            var scales = new Vector3[transforms.Length];
            for (int index = 0; index < transforms.Length; index++)
            {
                positions[index] = transforms[index].localPosition;
                rotations[index] = transforms[index].localRotation;
                scales[index] = transforms[index].localScale;
            }
            float shiverClock = (float)hero.ColdModel.ElapsedSeconds;
            try
            {
                // Re-sample only the cold clock within this frame. The base
                // idle/run playables and foot IK keep the same locomotion time.
                SeekColdHero(hero, 7f);
                evaluate.Invoke(hero, new object[] { 0f });
                hero.ReapplyLatePresentationPose();
                Assert.That(hero.ColdModel.IsShivering, Is.False);
                for (int index = 0; index < transforms.Length; index++)
                {
                    string label = transforms[index].name + " must retain its locomotion pose.";
                    Assert.That(Vector3.Distance(positions[index], transforms[index].localPosition),
                        Is.LessThan(0.00001f), label);
                    Assert.That(Quaternion.Angle(rotations[index], transforms[index].localRotation),
                        Is.LessThan(0.05f), label);
                    Assert.That(Vector3.Distance(scales[index], transforms[index].localScale),
                        Is.LessThan(0.00001f), label);
                }
            }
            finally
            {
                SeekColdHero(hero, shiverClock);
                evaluate.Invoke(hero, new object[] { 0f });
                hero.ReapplyLatePresentationPose();
            }
        }

        private static Vector3 GripInSleeveSpace(Transform sleeve, Vector3 grip)
        {
            // Remove body sway, breathing and gait: measure actual travel along
            // the opposite sleeve, retaining metres despite FBX unit scaling.
            return Vector3.Scale(sleeve.InverseTransformPoint(grip), sleeve.lossyScale);
        }

        private static void SeekColdHero(Player3DCharacterPresentation hero, float seconds)
        {
            hero.ColdBreath.StopAndClear();
            hero.ColdModel.Reset();
            hero.ColdModel.Step(seconds);
        }

        private static void AimAtColdHero(Camera camera, Transform actor, Vector3 offset)
        {
            Vector3 position = actor.TransformPoint(offset);
            Vector3 target = actor.TransformPoint(new Vector3(0f, 1.12f, 0f));
            camera.transform.SetPositionAndRotation(position,
                Quaternion.LookRotation(target - position, Vector3.up));
            camera.fieldOfView = 43f;
        }

        private static void CaptureColdHero(
            Camera camera, Player3DCharacterPresentation hero, string shotName)
        {
            // A yielded batch coroutine resumes before LateUpdate. Reapply the
            // zero-time final pose so the frame includes status and foot IK.
            hero.ReapplyLatePresentationPose();
            CaptureCurrentCamera(camera, SceneIds.AlpineVillage, shotName);
        }

        private static void AssertColdShoulderReach(Player3DCharacterPresentation hero, string pose)
        {
            hero.ReapplyLatePresentationPose();
            Player3DAssetRegistry registry = hero.Registry;
            Assert.That(registry.TryGetPart(Player3DAnatomicalPart.LeftHand,
                out var leftHand), Is.True);
            Assert.That(registry.TryGetPart(Player3DAnatomicalPart.RightHand,
                out var rightHand), Is.True);
            Assert.That(registry.TryGetPart(Player3DAnatomicalPart.LeftUpperArm,
                out var leftShoulder), Is.True);
            Assert.That(registry.TryGetPart(Player3DAnatomicalPart.RightUpperArm,
                out var rightShoulder), Is.True);
            float leftReach = Vector3.Distance(leftHand.Bone.position, rightShoulder.Bone.position);
            float rightReach = Vector3.Distance(rightHand.Bone.position, leftShoulder.Bone.position);
            Debug.Log($"Cold {pose}: opposite-shoulder wrist distance L={leftReach:F3}m R={rightReach:F3}m.");
            Assert.That(leftReach, Is.LessThan(0.32f), pose + " left hand must reach opposite shoulder");
            Assert.That(rightReach, Is.LessThan(0.32f), pose + " right hand must reach opposite shoulder");
        }

        private static void AssertColdClockContract()
        {
            var whole = new PlayerColdPresentationModel();
            var partitioned = new PlayerColdPresentationModel();
            whole.Step(200f);
            for (int index = 0; index < 3200; index++) partitioned.Step(0.0625f);
            Assert.That(partitioned.ElapsedSeconds, Is.EqualTo(whole.ElapsedSeconds));
            Assert.That(partitioned.RubNormalizedTime, Is.EqualTo(whole.RubNormalizedTime));
            Assert.That(partitioned.RubWeight01, Is.EqualTo(whole.RubWeight01));
            Assert.That(partitioned.IsShivering, Is.EqualTo(whole.IsShivering));
            Assert.That(partitioned.ShiverNormalizedTime, Is.EqualTo(whole.ShiverNormalizedTime));
            Assert.That(partitioned.ShiverWeight01, Is.EqualTo(whole.ShiverWeight01));
            Assert.That(partitioned.ExhaleEnvelope01, Is.EqualTo(whole.ExhaleEnvelope01));
            // Also compare inside a later chill, rather than only inactive zeros.
            whole.Step(1.75f);
            for (int index = 0; index < 28; index++) partitioned.Step(0.0625f);
            Assert.That(whole.IsShivering, Is.True);
            Assert.That(partitioned.ShiverNormalizedTime, Is.EqualTo(whole.ShiverNormalizedTime));
            Assert.That(partitioned.ShiverWeight01, Is.EqualTo(whole.ShiverWeight01));
            whole.Reset();
            whole.Step(PlayerColdPresentationModel.FirstShoulderRubSeconds + 0.25f);
            Assert.That(whole.IsRubbing, Is.True);
            Assert.That(whole.RubWeight01, Is.EqualTo(1f));
            double beforePause = whole.ElapsedSeconds;
            float beforeRub = whole.RubNormalizedTime;
            whole.Step(0f);
            Assert.That(whole.ElapsedSeconds, Is.EqualTo(beforePause));
            Assert.That(whole.RubNormalizedTime, Is.EqualTo(beforeRub));
            whole.Step(0.25f, false);
            Assert.That(whole.IsRubbing, Is.False);
            whole.Reset();
            whole.Step(2.2f);
            Assert.That(whole.ExhaleEnvelope01, Is.EqualTo(1f).Within(0.0001f));
            whole.Reset();
            whole.Step(PlayerColdPresentationModel.FirstShoulderRubSeconds +
                PlayerColdPresentationModel.ShoulderRubDurationSeconds);
            Assert.That(whole.IsRubbing, Is.False);
            Assert.That(whole.RubWeight01, Is.Zero);
            whole.Reset();
            whole.Step(PlayerColdPresentationModel.FirstShiverSeconds + 0.25f);
            Assert.That(whole.IsShivering, Is.True);
            Assert.That(whole.IsRubbing, Is.False);
            Assert.That(whole.ShiverNormalizedTime, Is.EqualTo(0.25f));
            Assert.That(whole.ShiverWeight01, Is.EqualTo(1f));
            whole.Step(0f);
            Assert.That(whole.ShiverNormalizedTime, Is.EqualTo(0.25f));
            whole.Step(0.125f, false);
            Assert.That(whole.IsShivering, Is.False);
            Assert.That(whole.ShiverWeight01, Is.Zero);
            whole.Reset();
            whole.Step(PlayerColdPresentationModel.FirstShiverSeconds +
                PlayerColdPresentationModel.ShiverDurationSeconds);
            Assert.That(whole.IsShivering, Is.False);
            Assert.That(whole.ShiverWeight01, Is.Zero);
            whole.Reset();
            Assert.That(whole.ElapsedSeconds, Is.Zero);
            Assert.That(whole.ExhaleEnvelope01, Is.Zero);
            Assert.That(whole.ShiverNormalizedTime, Is.Zero);
        }
    }
}
