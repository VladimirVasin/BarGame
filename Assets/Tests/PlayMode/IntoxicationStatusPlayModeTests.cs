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
            bool previousLensFxEnabled =
                GraphicsEffectsSettings.IntoxicationLensFxEnabled;
            GraphicsEffectsSettings.IntoxicationLensFxEnabled = true;
            GameSessionState.UpdateDrinkingProgress(
                20,
                DrinkId.LightBeer,
                1);
            status.Initialize(
                CreatePlayerRuntime(),
                cameraFollow,
                hud);
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
            Assert.That(
                status.Balance,
                Is.Not.Null,
                "The production hero carries the balance controller.");
            Assert.That(
                status.Balance.Intoxication,
                Is.EqualTo(1f).Within(0.001f),
                "The status controller feeds the balance model its level.");

            IntoxicationLensVolumeDriver lensDriver =
                uiObject
                    .GetComponent<IntoxicationLensVolumeDriver>();
            Assert.That(lensDriver, Is.Not.Null);
            Assert.That(
                lensDriver.AppliedChromaticAberration,
                Is.EqualTo(0.45f).Within(0.001f));
            Assert.That(
                lensDriver.AppliedLensDistortion,
                Is.EqualTo(-0.14f).Within(0.001f));

            // The drunk dolly zoom rides the same level through the
            // production stack: at one hundred the lens leaves its base.
            Camera statusCamera = cameraObject.GetComponent<Camera>();
            cameraFollow.ReseedDollyZoom(4242);
            float dollyDeadline = Time.realtimeSinceStartup + 8f;
            while (Mathf.Abs(statusCamera.fieldOfView - 53f) <= 5f &&
                   Time.realtimeSinceStartup < dollyDeadline)
            {
                yield return null;
            }

            Assert.That(
                Mathf.Abs(statusCamera.fieldOfView - 53f),
                Is.GreaterThan(5f),
                "At one hundred the drunk dolly zoom must move the lens.");

            // The vertigo whirlpool rides the same level: the disc over his
            // body floats as soon as he is past the threshold, and the water
            // around him winds up once an attack starts.
            Assert.That(
                IntoxicationRenderState.Current.VertigoCorePixels.magnitude,
                Is.EqualTo(
                    IntoxicationVertigoModel.CoreWobbleInternalPixels)
                    .Within(0.01f));
            Assert.That(
                IntoxicationRenderState.Current.VertigoEyeWorldPosition.y,
                Is.GreaterThan(playerObject.transform.position.y),
                "The whirlpool's eye sits on his body, not at his feet.");
            status.ReseedVertigo(4242);
            status.Vertigo.Reset(0f);
            float vertigoDeadline = Time.realtimeSinceStartup + 3f;
            while (Mathf.Abs(
                       IntoxicationRenderState.Current
                           .VertigoTwistRadians) < 0.01f &&
                   Time.realtimeSinceStartup < vertigoDeadline)
            {
                yield return null;
            }

            Assert.That(
                Mathf.Abs(
                    IntoxicationRenderState.Current.VertigoTwistRadians),
                Is.GreaterThan(0.01f),
                "At one hundred the whirlpool must wind the frame.");
            Assert.That(
                Mathf.Abs(
                    IntoxicationRenderState.Current.VertigoTwistRadians),
                Is.LessThanOrEqualTo(
                    IntoxicationVertigoModel.MaximumTwistRadians + 0.0001f));

            GraphicsEffectsSettings.IntoxicationLensFxEnabled =
                false;
            yield return null;
            Assert.That(
                lensDriver.AppliedChromaticAberration,
                Is.Zero,
                "Disabling the drunk lens toggle must collapse the " +
                "aberration immediately.");
            Assert.That(lensDriver.AppliedLensDistortion, Is.Zero);
            Assert.That(
                IntoxicationRenderState.Current.VertigoTwistRadians,
                Is.Zero,
                "The drunk lens toggle must cut the whirlpool at once.");
            Assert.That(
                IntoxicationRenderState.Current.VertigoCorePixels,
                Is.EqualTo(Vector2.zero));

            // The dolly zoom fades out under the same toggle rather than
            // cutting, so it gets its own short wait.
            float fadeDeadline = Time.realtimeSinceStartup + 2f;
            while (Mathf.Abs(statusCamera.fieldOfView - 53f) > 0.05f &&
                   Time.realtimeSinceStartup < fadeDeadline)
            {
                yield return null;
            }

            Assert.That(
                statusCamera.fieldOfView,
                Is.EqualTo(53f).Within(0.05f),
                "The drunk lens toggle must fade the dolly zoom to base.");
            GraphicsEffectsSettings.IntoxicationLensFxEnabled =
                previousLensFxEnabled;
        }

        [UnityTest]
        public IEnumerator Staggering_AboveSixtyKeepsInputAndStopsAtThreshold()
        {
            GameSessionState.UpdateDrinkingProgress(
                80,
                DrinkId.RedWine,
                4);
            status.Initialize(
                CreatePlayerRuntime(),
                cameraFollow,
                hud);

            float groundedDeadline =
                Time.realtimeSinceStartup + 1f;
            while (!motor.IsGrounded &&
                   Time.realtimeSinceStartup < groundedDeadline)
            {
                yield return null;
            }

            Assert.That(motor.IsGrounded, Is.True);
            yield return null;
            yield return null;

            // No modal, no arrow: the model runs while he keeps every
            // control, and the only thing the level did was let a fall
            // become possible.
            Assert.That(status.Balance, Is.Not.Null);
            Assert.That(status.Balance.IsActive, Is.True);
            Assert.That(status.Balance.FallAllowedNow, Is.True);
            Assert.That(status.IsFalling, Is.False);
            Assert.That(motor.InputEnabled, Is.True);
            Assert.That(interactor.InputEnabled, Is.True);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.True);
            Assert.That(cameraFollow.CinematicMotionEnabled, Is.True);
            Assert.That(hud.Visible, Is.True);
            Assert.That(
                status.Balance.Intoxication,
                Is.GreaterThan(0.5f));

            GameSessionState.UpdateDrinkingProgress(
                60,
                DrinkId.RedWine,
                4);
            yield return null;
            yield return null;

            Assert.That(status.Balance.FallAllowedNow, Is.False);
            Assert.That(status.IsFalling, Is.False);
            Assert.That(motor.InputEnabled, Is.True);
            Assert.That(interactor.InputEnabled, Is.True);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.True);
            Assert.That(cameraFollow.CinematicMotionEnabled, Is.True);
            Assert.That(
                GameSessionState.BalanceCheckDelayRemaining,
                Is.Zero);
        }

        [UnityTest]
        public IEnumerator LostBalance_FallsRecoversAndArmsGrace()
        {
            GameSessionState.UpdateDrinkingProgress(
                100,
                DrinkId.Vodka,
                5);
            status.Initialize(
                CreatePlayerRuntime(),
                cameraFollow,
                hud);

            float groundedDeadline =
                Time.realtimeSinceStartup + 1f;
            while (!motor.IsGrounded &&
                   Time.realtimeSinceStartup < groundedDeadline)
            {
                yield return null;
            }

            Assert.That(motor.IsGrounded, Is.True);
            yield return null;
            Assert.That(motor.InputEnabled, Is.True);
            Assert.That(interactor.InputEnabled, Is.True);
            Assert.That(
                status.DebugForceLoseBalance(1f),
                Is.True,
                "A grounded, unblocked hero above the threshold can " +
                "lose his balance.");

            float fallDeadline =
                Time.realtimeSinceStartup + 6f;
            while (!status.IsFalling &&
                   Time.realtimeSinceStartup < fallDeadline)
            {
                yield return null;
            }

            Assert.That(status.IsFalling, Is.True);
            Assert.That(status.BalanceStateName, Is.EqualTo("Falling"));
            Assert.That(status.FallDirection, Is.EqualTo(1f));
            Assert.That(motor.InputEnabled, Is.False);
            Assert.That(interactor.InputEnabled, Is.False);
            // The fall takes the interactor and the motor, never the
            // camera: he may look around while he goes down and lies
            // there, and the shot follows the body, not the capsule.
            Assert.That(
                cameraFollow.OrbitInputEnabled,
                Is.True,
                "a fall must not lock the orbit camera");
            Assert.That(cameraFollow.FocusOverrideWeight, Is.EqualTo(1f));
            Assert.That(
                Vector3.Distance(
                    cameraFollow.FocusOverridePoint,
                    ragdoll.PelvisBody.transform.position),
                Is.LessThan(0.001f),
                "the focus follows the ragdoll's pelvis while he falls");
            Assert.That(
                status.Balance.IsActive,
                Is.False,
                "The model is frozen while the fall plays.");
            Assert.That(ragdoll, Is.Not.Null);
            // No Fall clip leads in any more: the ragdoll has the body
            // from the first frame, from the pose the late layer wrote.
            Assert.That(ragdoll.IsSimulating, Is.True);
            Assert.That(presentation.RagdollPoseActive, Is.True);
            Assert.That(presentation.IsClipActive, Is.False);

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
            Assert.That(
                Vector3.Dot(
                    ragdoll.ChestBody.linearVelocity,
                    playerObject.transform.right),
                Is.GreaterThan(0.1f),
                "The ragdoll starts with the topple's motion toward the " +
                "fall side, not from a standstill.");

            CharacterController primaryCollider =
                playerObject.GetComponent<CharacterController>();
            Collider[] playerColliders =
                playerObject.GetComponentsInChildren<Collider>(true);
            int enabledRagdollColliders = 0;
            int collidingPairs = 0;
            for (int first = 0; first < playerColliders.Length; first++)
            {
                Collider current = playerColliders[first];
                // Triggers are not ragdoll bones and never were. The hero
                // also carries a passive "Cloth Body Capsule" trigger, which
                // exists only because Cloth presses against CapsuleColliders
                // and a CharacterController is not one. It blocks nothing,
                // so asking whether it IGNORES the controller is a question
                // with no meaning - and this loop, written when every other
                // collider on the hero was a bone, asked it anyway.
                if (current == primaryCollider || current.isTrigger)
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
                // Only the two halves of a joint ignore each other; every
                // other pair of bones collides, so no limb passes through
                // the body or another limb.
                Rigidbody currentBody = current.GetComponentInParent<Rigidbody>();
                for (int second = first + 1;
                     second < playerColliders.Length;
                     second++)
                {
                    Collider other = playerColliders[second];
                    if (other.isTrigger || other == primaryCollider)
                    {
                        continue;
                    }

                    Rigidbody otherBody = other.GetComponentInParent<Rigidbody>();
                    bool jointed = AreJointed(currentBody, otherBody);
                    Assert.That(
                        Physics.GetIgnoreCollision(current, other),
                        Is.EqualTo(jointed),
                        $"{current.name} vs {other.name} (jointed: {jointed})");
                    if (!jointed)
                    {
                        collidingPairs++;
                    }
                }
            }

            Assert.That(
                enabledRagdollColliders,
                Is.EqualTo(ragdoll.BodyCount));
            Assert.That(collidingPairs, Is.GreaterThan(40), "the limbs must collide with one another");
            Assert.That(
                playerObject.transform.position.x,
                Is.EqualTo(rootAtFall.x).Within(0.001f));
            Assert.That(
                playerObject.transform.position.z,
                Is.EqualTo(rootAtFall.z).Within(0.001f));

            // The ragdoll lies until it is still and the stun passes (up
            // to ~4.5 s real); then the rise begins with the frozen body
            // blending into the clip while he stirs.
            float risingDeadline = Time.realtimeSinceStartup + 9f;
            while (status.BalanceStateName != "Rising" &&
                   Time.realtimeSinceStartup < risingDeadline)
            {
                yield return null;
            }

            Assert.That(status.BalanceStateName, Is.EqualTo("Rising"));
            Assert.That(presentation.RagdollPoseActive, Is.False);
            Assert.That(ragdoll.IsSimulating, Is.False);
            Assert.That(ragdoll.PelvisBody.isKinematic, Is.True);
            Assert.That(ragdoll.ChestBody.isKinematic, Is.True);
            Assert.That(
                presentation.ActiveClipName,
                Does.StartWith("Rise"));
            Assert.That(
                Vector2.Distance(
                    new Vector2(playerObject.transform.position.x, playerObject.transform.position.z),
                    new Vector2(ragdoll.PelvisBody.position.x, ragdoll.PelvisBody.position.z)),
                Is.LessThan(0.5f),
                "The root was brought back under the lying pelvis.");

            // Once he is pushing up the frozen pose has been let go.
            float pushDeadline = Time.realtimeSinceStartup + 4f;
            while (status.RiseStageName != "PushingUp" &&
                   status.BalanceStateName == "Rising" &&
                   Time.realtimeSinceStartup < pushDeadline)
            {
                yield return null;
            }

            Assert.That(status.RiseStageName, Is.EqualTo("PushingUp"));
            Assert.That(ragdoll.IsActive, Is.False);
            Assert.That(presentation.RisePose.Active, Is.True);
            Assert.That(
                presentation.ActiveClipName,
                Does.StartWith("Rise"));

            float recoveryDeadline =
                Time.realtimeSinceStartup + 10f;
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
            Assert.That(motor.InputEnabled, Is.True);
            Assert.That(interactor.InputEnabled, Is.True);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.True);
            Assert.That(
                status.Balance.Model.LostBalance,
                Is.False,
                "The next episode starts on a fresh model.");
            Assert.That(
                GameSessionState.BalanceCheckDelayRemaining,
                Is.GreaterThan(
                    IntoxicationStatusController.PostFallGraceDuration - 1f),
                "A fall arms the post-fall grace in the session.");
            Assert.That(
                GameSessionState.BalanceCheckSequence,
                Is.EqualTo(1),
                "One episode was consumed by the fall.");
            yield return null;
            Assert.That(
                status.Balance.FallAllowedNow,
                Is.False,
                "Balance cannot be lost again inside the grace.");
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

        /// <summary>Whether a joint connects the two bodies directly, either way round.</summary>
        private static bool AreJointed(Rigidbody first, Rigidbody second)
        {
            if (first == null || second == null || first == second)
            {
                return false;
            }

            foreach (ConfigurableJoint joint in first.GetComponents<ConfigurableJoint>())
            {
                if (joint.connectedBody == second)
                {
                    return true;
                }
            }

            foreach (ConfigurableJoint joint in second.GetComponents<ConfigurableJoint>())
            {
                if (joint.connectedBody == first)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
