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
        private PlayerRuntime playerRuntime;
        private Player3DRagdollController ragdoll;
        private Player3DCharacterPresentation presentation;
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

            cameraObject = new GameObject(
                "Intoxication Test Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            playerRuntime = PlayerFactory.Create(
                null,
                Vector3.zero,
                camera,
                null,
                null);
            playerObject = playerRuntime.GameObject;
            motor = playerRuntime.Motor;
            interactor = playerRuntime.Interactor;
            ragdoll = playerRuntime.Ragdoll;
            presentation =
                (Player3DCharacterPresentation)playerRuntime.Visual;
            cameraFollow =
                cameraObject.AddComponent<PlayerCameraFollow>();
            cameraFollow.Initialize(
                camera,
                playerObject.transform,
                false);

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
        public IEnumerator BalanceCheck_AboveSixtyKeepsMovementAndCancelsAtThreshold()
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
            Assert.That(motor.InputEnabled, Is.True);
            Assert.That(interactor.InputEnabled, Is.False);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.False);
            Assert.That(cameraFollow.CinematicMotionEnabled, Is.True);
            Assert.That(hud.Visible, Is.True);

            float activeDeadline = Time.realtimeSinceStartup + 1.5f;
            while (status.BalanceStateName != "Active" &&
                   Time.realtimeSinceStartup < activeDeadline)
            {
                yield return null;
            }

            Assert.That(status.BalanceStateName, Is.EqualTo("Active"));
            Assert.That(motor.InputEnabled, Is.True);

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
            Assert.That(motor.InputEnabled, Is.True);
            Assert.That(interactor.InputEnabled, Is.False);

            float fallDeadline =
                Time.realtimeSinceStartup + 6f;
            while (!status.IsFalling &&
                   Time.realtimeSinceStartup < fallDeadline)
            {
                yield return null;
            }

            Assert.That(status.IsFalling, Is.True);
            Assert.That(status.BalanceStateName, Is.EqualTo("Falling"));
            Assert.That(balanceView.Visible, Is.False);
            Assert.That(motor.InputEnabled, Is.False);
            Assert.That(interactor.InputEnabled, Is.False);
            Assert.That(ragdoll, Is.Not.Null);
            Assert.That(ragdoll.IsSimulating, Is.False);
            Assert.That(presentation.RagdollPoseActive, Is.False);
            Assert.That(
                presentation.ActiveClipName,
                Does.StartWith("Fall"));

            Vector3 rootAtFall = playerObject.transform.position;
            float ragdollDeadline = Time.realtimeSinceStartup + 1f;
            while (!ragdoll.IsSimulating &&
                   Time.realtimeSinceStartup < ragdollDeadline)
            {
                yield return null;
            }

            Vector3 chestAtHandoff = ragdoll.ChestBody.position;
            Vector3 pelvisAtHandoff = ragdoll.PelvisBody.position;
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.That(ragdoll.IsSimulating, Is.True);
            Assert.That(presentation.RagdollPoseActive, Is.True);
            Assert.That(ragdoll.PelvisBody.isKinematic, Is.False);
            Assert.That(ragdoll.ChestBody.isKinematic, Is.False);
            Assert.That(
                Vector3.Distance(
                    chestAtHandoff,
                    ragdoll.ChestBody.position),
                Is.GreaterThan(0.001f));
            Assert.That(
                Vector3.Distance(
                    pelvisAtHandoff,
                    ragdoll.PelvisBody.position),
                Is.LessThan(0.75f));

            CharacterController primaryCollider =
                playerObject.GetComponent<CharacterController>();
            Collider[] playerColliders =
                playerObject.GetComponentsInChildren<Collider>(true);
            int enabledRagdollColliders = 0;
            for (int first = 0; first < playerColliders.Length; first++)
            {
                Collider current = playerColliders[first];
                if (current == primaryCollider)
                {
                    continue;
                }

                enabledRagdollColliders++;
                Assert.That(current.enabled, Is.True);
                Assert.That(
                    Physics.GetIgnoreCollision(
                        current,
                        primaryCollider),
                    Is.True);
                for (int second = first + 1;
                     second < playerColliders.Length;
                     second++)
                {
                    Collider other = playerColliders[second];
                    if (other != primaryCollider)
                    {
                        Assert.That(
                            Physics.GetIgnoreCollision(current, other),
                            Is.True);
                    }
                }
            }

            Assert.That(
                enabledRagdollColliders,
                Is.EqualTo(ragdoll.BodyCount));
            Assert.That(
                playerObject.transform.position.x,
                Is.EqualTo(rootAtFall.x).Within(0.001f));
            Assert.That(
                playerObject.transform.position.z,
                Is.EqualTo(rootAtFall.z).Within(0.001f));

            float risingDeadline = Time.realtimeSinceStartup + 3f;
            while (status.BalanceStateName != "Rising" &&
                   Time.realtimeSinceStartup < risingDeadline)
            {
                yield return null;
            }

            Assert.That(status.BalanceStateName, Is.EqualTo("Rising"));
            Assert.That(ragdoll.IsActive, Is.False);
            Assert.That(presentation.RagdollPoseActive, Is.False);
            Assert.That(ragdoll.PelvisBody.isKinematic, Is.True);
            Assert.That(ragdoll.ChestBody.isKinematic, Is.True);
            Assert.That(
                presentation.ActiveClipName,
                Does.StartWith("Rise"));

            float recoveryDeadline =
                Time.realtimeSinceStartup + 4f;
            bool sawTerminalRisePresentationFrame = false;
            while (status.IsFalling &&
                   Time.realtimeSinceStartup < recoveryDeadline)
            {
                yield return null;
                sawTerminalRisePresentationFrame |=
                    status.BalanceStateName == "Rising" &&
                    presentation.FallAmount <= 0.001f &&
                    presentation.ActiveClipName.StartsWith("Rise");
            }

            Assert.That(
                sawTerminalRisePresentationFrame,
                Is.True,
                "Rise(1) must remain active for one presentation frame " +
                "before ordinary locomotion is restored.");
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
            return playerRuntime;
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
