using System;
using System.Collections;
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
                // Photograph opposite ends of the middle stroke after the
                // quarter-second mixer settle, rather than two nearby lows.
                SeekColdHero(hero, PlayerColdPresentationModel.FirstShoulderRubSeconds + 0.79f);
                yield return ColdHeroFrames(15, hero, armSeparation);
                Assert.That(hero.ColdModel.IsRubbing, Is.True);
                Assert.That(hero.ColdModel.RubWeight01, Is.GreaterThan(0.98f));
                AssertColdShoulderReach(hero, "rub-first-stroke");
                Vector3 firstRubGrip = hero.Registry.Anchors.LeftGrip.position;
                CaptureColdHero(camera, hero,
                    "cold-03-rub-first-stroke");
                SeekColdHero(hero, PlayerColdPresentationModel.FirstShoulderRubSeconds + 1.21f);
                yield return ColdHeroFrames(15, hero, armSeparation);
                AssertColdShoulderReach(hero, "rub-return-stroke");
                Assert.That(Vector3.Distance(firstRubGrip,
                    hero.Registry.Anchors.LeftGrip.position), Is.GreaterThan(0.012f),
                    "The imported rub clip must actually move the hand along the sleeve.");
                CaptureColdHero(camera, hero,
                    "cold-04-rub-return-stroke");

                // Source clips can be clear while the masked Unity graph is
                // not. Observe both full cycles through its rendered output.
                SeekColdHero(hero, 0f);
                yield return ColdHeroFrames(240, hero, armSeparation);
                SeekColdHero(hero, PlayerColdPresentationModel.FirstShoulderRubSeconds);
                yield return ColdHeroFrames(150, hero, armSeparation);

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
                        // Cancel a real in-progress stroke while the arms are
                        // still crossed, rather than after its natural release.
                        SeekColdHero(hero,
                            PlayerColdPresentationModel.FirstShoulderRubSeconds + 1.1f);
                        yield return ColdHeroFrames(15, hero, armSeparation);
                        Assert.That(hero.ColdModel.IsRubbing, Is.True);
                    }
                    hero.SetMotion(motions[index]);
                    yield return ColdHeroFrames(45, hero, armSeparation);
                    Assert.That(hero.CurrentLocomotionState, Is.EqualTo(states[index]));
                    Assert.That(hero.ColdBodyWeight, Is.GreaterThan(0.98f));
                    if (states[index] == Player3DLocomotionState.Run)
                    {
                        Assert.That(hero.ColdArmWeight, Is.LessThan(0.02f));
                        Assert.That(hero.ColdModel.IsRubbing, Is.False);
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
            Assert.That(partitioned.ExhaleEnvelope01, Is.EqualTo(whole.ExhaleEnvelope01));
            whole.Reset();
            whole.Step(10.25f);
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
            whole.Step(10f + PlayerColdPresentationModel.ShoulderRubDurationSeconds);
            Assert.That(whole.IsRubbing, Is.False);
            Assert.That(whole.RubWeight01, Is.Zero);
            whole.Reset();
            Assert.That(whole.ElapsedSeconds, Is.Zero);
            Assert.That(whole.ExhaleEnvelope01, Is.Zero);
        }
    }
}
