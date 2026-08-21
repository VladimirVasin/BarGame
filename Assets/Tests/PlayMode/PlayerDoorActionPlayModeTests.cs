using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class PlayerDoorActionPlayModeTests
    {
        private const float TimeoutSeconds = 4f;

        [UnityTest]
        public IEnumerator Target_CallbackRunsOnlyAfterTerminalCompletion()
        {
            DoorTestContext context = CreateContext("Door Completion");
            int callbackCount = 0;
            PlayerAnimatedInteractionPhase callbackPhase =
                PlayerAnimatedInteractionPhase.Positioning;
            bool callbackClipActive = true;
            bool callbackControllerPlaying = true;

            try
            {
                Assert.That(
                    context.Target.CanInteract(context.Player.Interactor),
                    Is.True);
                Assert.That(
                    context.Target.TryBegin(
                        context.Player.Interactor,
                        () =>
                        {
                            callbackCount++;
                            callbackPhase = context.Interaction.Phase;
                            callbackClipActive =
                                context.ClipPresentation.IsClipActive;
                            callbackControllerPlaying =
                                context.Controller.IsPlaying;
                        }),
                    Is.True);

                Assert.That(callbackCount, Is.Zero);
                Assert.That(context.Controller.IsPlaying, Is.True);
                Assert.That(
                    context.Target.CanInteract(context.Player.Interactor),
                    Is.False);
                Assert.That(context.Player.Motor.InputEnabled, Is.False);
                Assert.That(
                    context.Player.Interactor.InputEnabled,
                    Is.False);

                yield return WaitForPhase(
                    context.Interaction,
                    PlayerAnimatedInteractionPhase.Entering);
                Assert.That(callbackCount, Is.Zero);
                Assert.That(
                    context.ClipPresentation.ActiveClipName,
                    Is.EqualTo("DoorUseEnter"));

                yield return WaitForPhase(
                    context.Interaction,
                    PlayerAnimatedInteractionPhase.Looping);
                Assert.That(callbackCount, Is.Zero);
                Assert.That(
                    context.ClipPresentation.ActiveClipName,
                    Is.EqualTo("DoorUseLoop"));

                yield return WaitForPhase(
                    context.Interaction,
                    PlayerAnimatedInteractionPhase.Exiting);
                Assert.That(callbackCount, Is.Zero);
                Assert.That(
                    context.ClipPresentation.ActiveClipName,
                    Is.EqualTo("DoorUseExit"));

                float deadline =
                    Time.realtimeSinceStartup + TimeoutSeconds;
                while (callbackCount == 0 &&
                       Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                Assert.That(callbackCount, Is.EqualTo(1));
                Assert.That(
                    callbackPhase,
                    Is.EqualTo(PlayerAnimatedInteractionPhase.Idle));
                Assert.That(callbackClipActive, Is.False);
                Assert.That(callbackControllerPlaying, Is.False);
                Assert.That(context.Controller.IsPlaying, Is.False);
                Assert.That(context.Interaction.IsActive, Is.False);
                Assert.That(
                    context.ClipPresentation.IsClipActive,
                    Is.False);
                Assert.That(context.Player.Motor.InputEnabled, Is.True);
                Assert.That(
                    context.Player.Interactor.InputEnabled,
                    Is.True);
                Assert.That(
                    context.Target.CanInteract(context.Player.Interactor),
                    Is.True);
            }
            finally
            {
                context.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator Cancellation_RestoresWithoutCallbackAndReleasesOwnership()
        {
            DoorTestContext context = CreateContext("Door Cancellation");
            int callbackCount = 0;

            try
            {
                Assert.That(
                    context.Target.TryBegin(
                        context.Player.Interactor,
                        () => callbackCount++),
                    Is.True);
                yield return WaitForPhase(
                    context.Interaction,
                    PlayerAnimatedInteractionPhase.Entering);

                Assert.That(context.Controller.IsPlaying, Is.True);
                Assert.That(
                    context.ClipPresentation.IsClipActive,
                    Is.True);
                context.Target.enabled = false;
                yield return null;

                Assert.That(callbackCount, Is.Zero);
                Assert.That(context.Controller.IsPlaying, Is.False);
                Assert.That(
                    context.Interaction.Phase,
                    Is.EqualTo(PlayerAnimatedInteractionPhase.Idle));
                Assert.That(context.Interaction.IsActive, Is.False);
                Assert.That(
                    context.ClipPresentation.IsClipActive,
                    Is.False);
                Assert.That(context.Player.Motor.InputEnabled, Is.True);
                Assert.That(
                    context.Player.Interactor.InputEnabled,
                    Is.True);

                context.Target.enabled = true;
                Assert.That(
                    context.Target.CanInteract(context.Player.Interactor),
                    Is.True,
                    "Cancellation must release the target's owner.");

                Assert.That(
                    context.Target.TryBegin(
                        context.Player.Interactor,
                        () => callbackCount++),
                    Is.True);
                Assert.That(
                    context.Interaction.CancelActiveInteraction(),
                    Is.True);
                yield return null;

                Assert.That(callbackCount, Is.Zero);
                Assert.That(
                    context.Target.CanInteract(context.Player.Interactor),
                    Is.True,
                    "Controller-side cancellation must release stale " +
                    "target ownership.");
            }
            finally
            {
                context.Dispose();
            }
        }

        private static DoorTestContext CreateContext(string label)
        {
            GameObject cameraObject =
                new GameObject($"{label} Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            Vector3 playerRoot =
                Vector3.up * PlayerFactory.GroundedRootOffset;
            PlayerRuntime player = PlayerFactory.Create(
                null,
                playerRoot,
                camera,
                null,
                null);
            PlayerAnimatedInteractionController interaction =
                player.GameObject.GetComponent<
                    PlayerAnimatedInteractionController>();
            PlayerDoorActionController controller =
                player.GameObject.GetComponent<
                    PlayerDoorActionController>();
            IPlayerClipPresentation clipPresentation =
                (IPlayerClipPresentation)player.Visual;

            GameObject targetObject =
                new GameObject($"{label} Target");
            PlayerDoorActionTarget target =
                targetObject.AddComponent<PlayerDoorActionTarget>();
            target.Configure(PlayerDoorActionPlan.CreateStationary(
                Vector3.forward,
                playerRoot,
                Vector3.forward));

            Assert.That(interaction, Is.Not.Null);
            Assert.That(interaction.IsInitialized, Is.True);
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.IsInitialized, Is.True);
            Assert.That(clipPresentation, Is.Not.Null);

            return new DoorTestContext(
                cameraObject,
                player,
                targetObject,
                target,
                interaction,
                controller,
                clipPresentation);
        }

        private static IEnumerator WaitForPhase(
            PlayerAnimatedInteractionController interaction,
            PlayerAnimatedInteractionPhase expected)
        {
            float deadline =
                Time.realtimeSinceStartup + TimeoutSeconds;
            while (interaction.Phase != expected &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(interaction.Phase, Is.EqualTo(expected));
        }

        private sealed class DoorTestContext
        {
            public DoorTestContext(
                GameObject cameraObject,
                PlayerRuntime player,
                GameObject targetObject,
                PlayerDoorActionTarget target,
                PlayerAnimatedInteractionController interaction,
                PlayerDoorActionController controller,
                IPlayerClipPresentation clipPresentation)
            {
                CameraObject = cameraObject;
                Player = player;
                TargetObject = targetObject;
                Target = target;
                Interaction = interaction;
                Controller = controller;
                ClipPresentation = clipPresentation;
            }

            public GameObject CameraObject { get; }
            public PlayerRuntime Player { get; }
            public GameObject TargetObject { get; }
            public PlayerDoorActionTarget Target { get; }
            public PlayerAnimatedInteractionController Interaction
            {
                get;
            }
            public PlayerDoorActionController Controller { get; }
            public IPlayerClipPresentation ClipPresentation { get; }

            public void Dispose()
            {
                if (TargetObject != null)
                {
                    Object.DestroyImmediate(TargetObject);
                }

                if (Player.GameObject != null)
                {
                    Object.DestroyImmediate(Player.GameObject);
                }

                if (CameraObject != null)
                {
                    Object.DestroyImmediate(CameraObject);
                }
            }
        }
    }
}
