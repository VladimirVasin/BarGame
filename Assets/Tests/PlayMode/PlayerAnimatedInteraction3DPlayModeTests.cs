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

        [UnityTest]
        public IEnumerator PositionedRig_FollowsMovingActionTarget_AndUsesIndependentExit()
        {
            GameObject cameraObject =
                new GameObject("Moving Contextual Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            GameObject playerObject =
                new GameObject("Moving Contextual Player");
            PlayerClipPresentationTestDouble presentation =
                playerObject.AddComponent<
                    PlayerClipPresentationTestDouble>();
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
                new GameObject("Moving Contextual Controller")
                    .AddComponent<PlayerAnimatedInteractionController>();
            GameObject anchorObject =
                new GameObject("Moving Action Pelvis Target");

            try
            {
                controller.Initialize(player, camera);
                var definition = new PlayerAnimatedInteractionDefinition(
                    "TestEnter",
                    "TestLoop",
                    "TestExit",
                    enterFrameCount: 8,
                    enterFramesPerSecond: 20f,
                    loopFrameCount: 1,
                    loopFramesPerSecond: 5f,
                    exitFrameCount: 8,
                    exitFramesPerSecond: 20f);
                Vector3 standPelvis = new Vector3(0f, 0.72f, 0f);
                var entryPose = new PlayerAnimatedInteractionPose(
                    Vector3.zero,
                    Quaternion.identity,
                    standPelvis);
                anchorObject.transform.position =
                    new Vector3(2f, 1.15f, -3f);
                var initialExitPose = new PlayerAnimatedInteractionPose(
                    Vector3.zero,
                    Quaternion.identity,
                    standPelvis);

                Assert.That(
                    controller.BeginPositioned(
                        definition,
                        entryPose,
                        anchorObject.transform.position,
                        initialExitPose),
                    Is.True);
                Assert.That(
                    controller.BindActionPelvisTarget(
                        anchorObject.transform),
                    Is.True);

                yield return WaitForPhase(
                    controller,
                    PlayerAnimatedInteractionPhase.Entering);
                yield return null;

                float enteringPelvisX = presentation.PelvisPosition.x;
                anchorObject.transform.position +=
                    new Vector3(4f, 0f, 0f);
                Assert.That(
                    controller.RefreshActiveClipAlignment(),
                    Is.True);
                Assert.That(
                    presentation.PelvisPosition.x,
                    Is.GreaterThan(enteringPelvisX),
                    "The moving target must affect the in-progress entry.");

                yield return WaitForPhase(
                    controller,
                    PlayerAnimatedInteractionPhase.Looping);

                Vector3 loopTarget = new Vector3(8f, 1.3f, -4f);
                anchorObject.transform.position = loopTarget;
                Assert.That(
                    controller.RefreshActiveClipAlignment(),
                    Is.True);
                Assert.That(
                    presentation.PelvisPosition,
                    Is.EqualTo(loopTarget));

                Assert.That(
                    controller.FreezeActionPelvisTarget(),
                    Is.True);
                anchorObject.transform.position =
                    new Vector3(20f, 5f, 20f);
                controller.RefreshActiveClipAlignment();
                Assert.That(
                    presentation.PelvisPosition,
                    Is.EqualTo(loopTarget));

                Vector3 exitStart = new Vector3(9f, 1.25f, 2f);
                anchorObject.transform.position = exitStart;
                Assert.That(
                    controller.BindActionPelvisTarget(
                        anchorObject.transform),
                    Is.True);
                var exitPose = new PlayerAnimatedInteractionPose(
                    new Vector3(5f, 0f, 6f),
                    Quaternion.Euler(0f, 90f, 0f),
                    new Vector3(5f, 0.72f, 6f));
                Vector3 exitWaypoint =
                    exitStart + new Vector3(0f, 0f, 1f);
                var exitTransition =
                    new PlayerAnimatedInteractionPelvisTransition(
                        exitWaypoint,
                        enterArrivalProgress: 0.25f,
                        enterDepartureProgress: 0.5f,
                        exitArrivalProgress: 0.25f,
                        exitDepartureProgress: 0.5f);

                Assert.That(
                    controller.RequestExit(
                        exitPose,
                        exitStart,
                        durationMultiplier: 1f,
                        transition: exitTransition),
                    Is.True);
                Assert.That(
                    presentation.PelvisPosition,
                    Is.EqualTo(exitStart));
                anchorObject.transform.position =
                    new Vector3(-30f, 8f, -30f);
                controller.RefreshActiveClipAlignment();
                Assert.That(
                    presentation.PelvisPosition,
                    Is.EqualTo(exitStart),
                    "RequestExit must freeze the supplied action pelvis.");

                float waypointDeadline = Time.realtimeSinceStartup + 1f;
                while (controller.Phase ==
                           PlayerAnimatedInteractionPhase.Exiting &&
                       Vector3.Distance(
                           presentation.PelvisPosition,
                           exitWaypoint) > 0.001f &&
                       Time.realtimeSinceStartup < waypointDeadline)
                {
                    yield return null;
                }

                Assert.That(
                    Vector3.Distance(
                        presentation.PelvisPosition,
                        exitWaypoint),
                    Is.LessThan(0.001f),
                    "The exit request must replace the pelvis transition.");

                yield return WaitForPhase(
                    controller,
                    PlayerAnimatedInteractionPhase.Idle);

                Assert.That(
                    playerObject.transform.position,
                    Is.EqualTo(exitPose.RootPosition));
                Assert.That(
                    Quaternion.Angle(
                        playerObject.transform.rotation,
                        exitPose.RootRotation),
                    Is.LessThan(0.001f));

                Vector3 staticActionPelvis =
                    new Vector3(-2f, 0.9f, 3f);
                Assert.That(
                    controller.BeginLooping(
                        definition,
                        standPelvis,
                        staticActionPelvis),
                    Is.True);
                anchorObject.transform.position =
                    new Vector3(40f, 10f, 40f);
                controller.RefreshActiveClipAlignment();
                Assert.That(
                    presentation.PelvisPosition,
                    Is.EqualTo(staticActionPelvis),
                    "A completed interaction must not retain its anchor.");
                Assert.That(
                    controller.CancelActiveInteraction(),
                    Is.True);
            }
            finally
            {
                UnityEngine.Object.Destroy(anchorObject);
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
