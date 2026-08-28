using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// Getting off the upper station and into the village, through the REAL
    /// world with a real <c>CharacterController</c>.
    ///
    /// This exists because an EditMode test that walked the same route through
    /// the WALKABLE MASK passed while the player was still stuck on the
    /// platform. The mask is a polygon and knows nothing about furniture -
    /// the site validator on the mountain says exactly that about itself - so
    /// a mask-only check cannot see a collider standing in the way, and the
    /// village has no capsule flood of its own.
    /// </summary>
    public sealed class AlpineVillageStationExitPlayModeTests
    {
        private const float PinnedFrameSeconds = 1f / 60f;
        private const int MaximumSteps = 3000;

        [SetUp]
        public void PinTheClock()
        {
            Time.captureDeltaTime = PinnedFrameSeconds;
            GameSessionState.BeginNewGame();
        }

        [TearDown]
        public void ReleaseTheClock()
        {
            Time.captureDeltaTime = 0f;
            GameSessionState.BeginNewGame();
        }

        /// <summary>
        /// He walks off the platform and into the village.
        ///
        /// The line a person actually takes: out of the cabin, straight at the
        /// foot of the lane, letting the controller slide along whatever it
        /// meets. A route that merely EXISTS is not enough - a waypoint
        /// version of this passed while the player was wedged against a fence
        /// `5.94 m` short, because it knew where the gate was and he did not.
        /// </summary>
        [UnityTest]
        public IEnumerator Station_HeGetsOutWalkingStraightAtTheVillage()
        {
            var scene = new GameObject("Alpine Village Direct Exit Test");
            try
            {
                AlpineVillagePlan plan = AlpineVillagePlanner.Create(
                    GameSessionState.DefaultCitySeed);
                MountainRoadCablewayPlan cableway = plan.Station.Cableway;

                var cameraObject = new GameObject("Camera");
                cameraObject.transform.SetParent(scene.transform, false);
                Camera camera = cameraObject.AddComponent<Camera>();
                var promptObject = new GameObject("Prompt");
                promptObject.transform.SetParent(scene.transform, false);
                InteractionPromptView prompt =
                    promptObject.AddComponent<InteractionPromptView>();

                AlpineVillageWorldResult world =
                    AlpineVillageWorldBuilder.Build(scene.transform, plan);
                PlayerRuntime player = PlayerFactory.Create(
                    scene.transform,
                    cableway.BoardingDockPosition +
                    Vector3.up * PlayerFactory.GroundedRootOffset,
                    camera,
                    world.WalkableArea,
                    prompt);
                PlayerCameraFollow follow =
                    cameraObject.AddComponent<PlayerCameraFollow>();
                follow.Initialize(camera, player.GameObject.transform, false);

                for (int frame = 0; frame < 20; frame++)
                {
                    yield return null;
                }

                Transform root = player.GameObject.transform;
                PlayerMotor motor = player.Motor;
                Vector3 target = plan.Lane.Start;
                bool arrived = false;
                for (int step = 0; step < MaximumSteps && !arrived; step++)
                {
                    arrived = motor.MoveTowardsApproachWaypoint(
                        target,
                        1.2f,
                        Time.deltaTime);
                    yield return null;
                }

                Assert.That(
                    arrived,
                    Is.True,
                    "Walking straight at the village from the cabin does not " +
                    $"get him there: he stopped at {root.position}, " +
                    $"{Vector3.Distance(root.position, target):0.00} m short " +
                    "of the foot of the lane. The gate is somewhere he is " +
                    "not walking.");
            }
            finally
            {
                Object.DestroyImmediate(scene);
            }
        }
    }
}
