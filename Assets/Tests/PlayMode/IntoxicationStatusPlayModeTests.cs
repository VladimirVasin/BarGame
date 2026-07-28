using System.Collections;
using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class IntoxicationStatusPlayModeTests
    {
        private GameObject groundObject;
        private GameObject playerObject;
        private GameObject cameraObject;
        private GameObject uiObject;
        private PlayerMotor motor;
        private PlayerInteractor interactor;
        private PlayerCameraFollow cameraFollow;
        private IntoxicationHudView hud;
        private BalanceCheckView balanceView;
        private IntoxicationStatusController status;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            ResetSession();

            groundObject = GameObject.CreatePrimitive(
                PrimitiveType.Cube);
            groundObject.name = "Intoxication Test Ground";
            groundObject.transform.position =
                new Vector3(0f, -0.1f, 0f);
            groundObject.transform.localScale =
                new Vector3(8f, 0.2f, 8f);

            playerObject = new GameObject(
                "Intoxication Test Player");
            CharacterController controller =
                playerObject.AddComponent<CharacterController>();
            controller.height = 1.7f;
            controller.radius = 0.32f;
            controller.center = new Vector3(0f, 0.85f, 0f);
            controller.stepOffset = 0.28f;
            motor = playerObject.AddComponent<PlayerMotor>();
            interactor =
                playerObject.AddComponent<PlayerInteractor>();

            cameraObject = new GameObject(
                "Intoxication Test Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraFollow =
                cameraObject.AddComponent<PlayerCameraFollow>();
            cameraFollow.Initialize(
                camera,
                playerObject.transform,
                false);
            motor.Initialize(camera, null, null);

            uiObject = new GameObject(
                "Intoxication Test UI");
            hud = uiObject.AddComponent<IntoxicationHudView>();
            balanceView =
                uiObject.AddComponent<BalanceCheckView>();
            balanceView.Initialize(
                playerObject.transform,
                camera);
            status =
                uiObject.AddComponent<IntoxicationStatusController>();

            Physics.SyncTransforms();
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(uiObject);
            Object.Destroy(cameraObject);
            Object.Destroy(playerObject);
            Object.Destroy(groundObject);
            ResetSession();
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator IncreasingLevel_AppliesStrongerPresentation()
        {
            GameSessionState.UpdateDrinkingProgress(
                20,
                DrinkId.LightBeer,
                1);
            status.Initialize(
                CreatePlayerRuntime(),
                cameraFollow,
                hud,
                balanceView);
            yield return null;

            Assert.That(
                status.CurrentProfile.Stage,
                Is.EqualTo(IntoxicationStage.LightBuzz));
            Assert.That(motor.SpeedMultiplier, Is.EqualTo(1f));

            GameSessionState.UpdateDrinkingProgress(
                100,
                DrinkId.Vodka,
                5);
            float deadline = Time.realtimeSinceStartup + 1f;
            while (status.CurrentProfile.Level < 100 &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(status.CurrentProfile.Level, Is.EqualTo(100));
            Assert.That(
                status.CurrentProfile.Stage,
                Is.EqualTo(IntoxicationStage.VeryDrunk));
            Assert.That(
                motor.SpeedMultiplier,
                Is.EqualTo(0.7f).Within(0.001f));
            Assert.That(
                IntoxicationRenderState.Current.VignetteStrength,
                Is.GreaterThan(0.25f));
            Assert.That(
                IntoxicationRenderState.Current.GhostPixels,
                Is.EqualTo(3f).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator BalanceCheck_AboveSixtyLocksMovementAndCancelsAtThreshold()
        {
            GameSessionState.UpdateDrinkingProgress(
                80,
                DrinkId.RedWine,
                4);
            status.Initialize(
                CreatePlayerRuntime(),
                cameraFollow,
                hud,
                balanceView);

            float groundedDeadline =
                Time.realtimeSinceStartup + 1f;
            while (!motor.IsGrounded &&
                   Time.realtimeSinceStartup < groundedDeadline)
            {
                yield return null;
            }

            Assert.That(motor.IsGrounded, Is.True);
            Assert.That(status.TryStartBalanceCheck(), Is.True);
            Assert.That(status.IsBalanceCheckActive, Is.True);
            Assert.That(balanceView.Visible, Is.True);
            Assert.That(balanceView.IsWarning, Is.True);
            Assert.That(motor.InputEnabled, Is.False);
            Assert.That(interactor.InputEnabled, Is.False);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.False);
            Assert.That(cameraFollow.CinematicMotionEnabled, Is.True);
            Assert.That(hud.Visible, Is.True);

            GameSessionState.UpdateDrinkingProgress(
                60,
                DrinkId.RedWine,
                4);
            yield return null;

            Assert.That(status.IsBalanceCheckActive, Is.False);
            Assert.That(balanceView.Visible, Is.False);
            Assert.That(motor.InputEnabled, Is.True);
            Assert.That(interactor.InputEnabled, Is.True);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.True);
            Assert.That(cameraFollow.CinematicMotionEnabled, Is.True);
            Assert.That(
                GameSessionState.BalanceCheckDelayRemaining,
                Is.Zero);
        }

        [UnityTest]
        public IEnumerator FailedBalanceCheck_FallsRecoversAndSchedulesCooldown()
        {
            GameSessionState.UpdateDrinkingProgress(
                100,
                DrinkId.Vodka,
                5);
            status.Initialize(
                CreatePlayerRuntime(),
                cameraFollow,
                hud,
                balanceView);

            float groundedDeadline =
                Time.realtimeSinceStartup + 1f;
            while (!motor.IsGrounded &&
                   Time.realtimeSinceStartup < groundedDeadline)
            {
                yield return null;
            }

            Assert.That(motor.IsGrounded, Is.True);
            Assert.That(status.TryStartBalanceCheck(), Is.True);

            float fallDeadline =
                Time.realtimeSinceStartup + 6f;
            while (!status.IsFalling &&
                   Time.realtimeSinceStartup < fallDeadline)
            {
                yield return null;
            }

            Assert.That(status.IsFalling, Is.True);
            Assert.That(balanceView.Visible, Is.False);
            Assert.That(motor.InputEnabled, Is.False);
            Assert.That(interactor.InputEnabled, Is.False);

            float recoveryDeadline =
                Time.realtimeSinceStartup + 4f;
            while (status.IsFalling &&
                   Time.realtimeSinceStartup < recoveryDeadline)
            {
                yield return null;
            }

            Assert.That(status.IsFalling, Is.False);
            Assert.That(status.IsBalanceCheckActive, Is.False);
            Assert.That(motor.InputEnabled, Is.True);
            Assert.That(interactor.InputEnabled, Is.True);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.True);
            Assert.That(
                GameSessionState.BalanceCheckDelayRemaining,
                Is.GreaterThan(12f));
            Assert.That(
                GameSessionState.BalanceCheckSequence,
                Is.EqualTo(2));
        }

        private PlayerRuntime CreatePlayerRuntime()
        {
            return new PlayerRuntime(
                playerObject,
                motor,
                interactor,
                null);
        }

        private static void ResetSession()
        {
            GameSessionState.SetCitySeed(
                GameSessionState.DefaultCitySeed);
            GameSessionState.EnterBar(null);
            GameSessionState.CompleteCityReturn();
            GameSessionState.ResetDrinkingState();
        }
    }
}
