using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class PlayerAnimatedInteraction3DPlayModeTests
    {
        [UnityTest]
        public IEnumerator ContinuousRig_SamplesAllPhasesWithoutHiding()
        {
            GameObject cameraObject = new GameObject("Contextual 3D Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            GameObject playerObject = new GameObject("Contextual 3D Player");
            PlayerClipPresentationTestDouble presentation =
                playerObject.AddComponent<PlayerClipPresentationTestDouble>();
            PlayerMotor motor = playerObject.AddComponent<PlayerMotor>();
            PlayerInteractor interactor =
                playerObject.AddComponent<PlayerInteractor>();
            motor.Initialize(camera, null, presentation);
            interactor.Initialize(null);
            PlayerRuntime player = new PlayerRuntime(
                playerObject,
                motor,
                interactor,
                presentation);
            PlayerAnimatedInteractionController controller =
                new GameObject("Contextual 3D Controller")
                    .AddComponent<PlayerAnimatedInteractionController>();

            try
            {
                controller.Initialize(player, camera);
                var definition = new PlayerAnimatedInteractionDefinition(
                    "TestEnter",
                    "TestLoop",
                    "TestExit",
                    enterFrameCount: 1,
                    enterFramesPerSecond: 5f,
                    loopFrameCount: 1,
                    loopFramesPerSecond: 5f,
                    exitFrameCount: 1,
                    exitFramesPerSecond: 5f);
                Vector3 standPelvis = new Vector3(0f, 0.72f, 0f);
                Vector3 actionPelvis = new Vector3(2f, 1.15f, -3f);

                Assert.That(
                    controller.Begin(
                        definition,
                        standPelvis,
                        actionPelvis),
                    Is.True);
                Assert.That(presentation.WorldRenderer.enabled, Is.True);
                Assert.That(
                    presentation.ActiveClipName,
                    Is.EqualTo("TestEnter"));
                Assert.That(
                    presentation.PelvisPosition,
                    Is.EqualTo(standPelvis));

                yield return WaitForPhase(
                    controller,
                    PlayerAnimatedInteractionPhase.Looping);

                Assert.That(
                    presentation.ActiveClipName,
                    Is.EqualTo("TestLoop"));
                Assert.That(
                    presentation.PelvisPosition,
                    Is.EqualTo(actionPelvis));
                Assert.That(presentation.WorldRenderer.enabled, Is.True);
                Assert.That(controller.RequestExit(), Is.True);
                Assert.That(
                    presentation.ActiveClipName,
                    Is.EqualTo("TestExit"));

                yield return WaitForPhase(
                    controller,
                    PlayerAnimatedInteractionPhase.Idle);

                CollectionAssert.AreEqual(
                    new[] { "TestEnter", "TestLoop", "TestExit" },
                    presentation.BegunClips);
                Assert.That(
                    presentation.SawTerminalSample,
                    Is.True,
                    "The exit terminal pose must be sampled before cleanup.");
                Assert.That(presentation.EndClipCount, Is.EqualTo(1));
                Assert.That(
                    presentation.ResetSpatialOffsetCount,
                    Is.GreaterThanOrEqualTo(1));
                Assert.That(presentation.IsClipActive, Is.False);
                Assert.That(presentation.WorldRenderer.enabled, Is.True);
                Assert.That(presentation.InteractionHandoffLocked, Is.False);
                Assert.That(motor.InputEnabled, Is.True);
                Assert.That(interactor.InputEnabled, Is.True);
            }
            finally
            {
                UnityEngine.Object.Destroy(controller.gameObject);
                UnityEngine.Object.Destroy(playerObject);
                UnityEngine.Object.Destroy(cameraObject);
            }
        }

        private static IEnumerator WaitForPhase(
            PlayerAnimatedInteractionController controller,
            PlayerAnimatedInteractionPhase expected)
        {
            float deadline = Time.realtimeSinceStartup + 2f;
            while (controller.Phase != expected &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(controller.Phase, Is.EqualTo(expected));
        }
    }

    internal sealed class PlayerClipPresentationTestDouble :
        MonoBehaviour,
        IPlayerPresentation,
        IPlayerClipPresentation
    {
        private readonly List<string> begunClips = new List<string>();
        private Renderer worldRenderer;

        public IReadOnlyList<string> BegunClips => begunClips;
        public Renderer WorldRenderer => worldRenderer;
        public IReadOnlyList<Renderer> Renderers =>
            new[] { worldRenderer };
        public Transform VisualRoot => transform;
        public PlayerPresentationMetrics Metrics =>
            new PlayerPresentationMetrics(
                1.75f,
                0.32f,
                transform,
                transform.position,
                transform.position,
                1f,
                0f,
                1f);
        public bool InteractionHandoffLocked { get; private set; }
        public string ActiveClipName { get; private set; } = string.Empty;
        public bool IsClipActive => ActiveClipName.Length > 0;
        public bool SawTerminalSample { get; private set; }
        public int EndClipCount { get; private set; }
        public int ResetSpatialOffsetCount { get; private set; }
        public Vector3 PelvisPosition { get; private set; }

        private void Awake()
        {
            worldRenderer = gameObject.AddComponent<MeshRenderer>();
        }

        public bool HasClip(string clipName)
        {
            return clipName == "TestEnter" ||
                   clipName == "TestLoop" ||
                   clipName == "TestExit";
        }

        public bool TryBeginClip(string clipName)
        {
            if (!HasClip(clipName))
            {
                return false;
            }

            ActiveClipName = clipName;
            begunClips.Add(clipName);
            return true;
        }

        public void SampleActiveClip(float normalizedTime)
        {
            if (!IsClipActive)
            {
                throw new InvalidOperationException("No test clip is active.");
            }

            SawTerminalSample |=
                ActiveClipName == "TestExit" &&
                normalizedTime >= 0.999f;
        }

        public void AlignActiveClipAnchor(Vector3 worldPelvisTarget)
        {
            PelvisPosition = worldPelvisTarget;
        }

        public void ResetClipSpatialOffset()
        {
            PelvisPosition = transform.position;
            ResetSpatialOffsetCount++;
        }

        public void EndClip()
        {
            ActiveClipName = string.Empty;
            EndClipCount++;
        }

        public void SetMotion(Vector3 planarVelocity)
        {
        }

        public void SetIntoxication(float intensity)
        {
        }

        public void SetBalancePose(float signedLean)
        {
        }

        public void SetFallPose(float signedDirection, float amount)
        {
        }

        public void SetFallAnimation(
            PlayerFallAnimationPhase phase,
            float normalizedProgress)
        {
        }

        public void SetInteractionHandoffLocked(bool locked)
        {
            InteractionHandoffLocked = locked;
        }
    }
}
