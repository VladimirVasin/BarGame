using System.Collections;
using System.Collections.Generic;
using System.IO;
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

        [System.Serializable]
        private sealed class FrostWalkReport
        {
            public int seed;
            public float maximum_step_seconds;
            public int movement_steps;
            public float walk_seconds;
            public float walked_planar_metres;
            public float full_frost_seconds;
            public float frost_at_door;
            public Vector3 start;
            public Vector3 finish;
            public Vector3 door_dock;
            public bool reached_entry;
        }

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

        [UnityTest]
        public IEnumerator StationToMotherHouseFrostTiming()
        {
            const float stepSeconds = 0.25f;
            const float arrivalRadius = 0.015f;
            const float ordinaryWalkSpeed = 2.6f;
            var scene = new GameObject("Station To Mother House Frost Timing Test");
            try
            {
                AlpineVillagePlan plan = AlpineVillagePlanner.Create(
                    GameSessionState.DefaultCitySeed);
                var cameraObject = new GameObject("Camera");
                cameraObject.transform.SetParent(scene.transform, false);
                Camera camera = cameraObject.AddComponent<Camera>();
                var promptObject = new GameObject("Prompt");
                promptObject.transform.SetParent(scene.transform, false);
                InteractionPromptView prompt = promptObject.AddComponent<InteractionPromptView>();
                AlpineVillageWorldResult world =
                    AlpineVillageWorldBuilder.Build(scene.transform, plan);
                Vector3 start = plan.Station.BoardingDockPosition +
                    Vector3.up * PlayerFactory.GroundedRootOffset;
                PlayerRuntime player = PlayerFactory.Create(scene.transform, start,
                    camera, world.WalkableArea, prompt);
                PlayerMotor motor = player.Motor;
                CharacterController capsule = player.GameObject.GetComponent<CharacterController>();
                Assert.That(capsule, Is.Not.Null);
                Assert.That(capsule.enabled, Is.True);
                motor.SetInputEnabled(false);
                for (int frame = 0; frame < 20; frame++) yield return null;

                // Use the actual bends of the street, without adding an idle
                // frame at each dense mesh sample or inventing a shortcut.
                var waypoints = new List<Vector3> { plan.Lane.Start };
                for (int index = 1; index < plan.Lane.Samples.Count; index++)
                {
                    if (index == plan.Lane.Samples.Count - 1 ||
                        Vector3.Dot(plan.Lane.Samples[index - 1].Forward,
                            plan.Lane.Samples[index].Forward) < 0.9999f)
                        waypoints.Add(plan.Lane.Samples[index].Position);
                }
                Vector3 dock = plan.MothersHouse.DoorDockPosition +
                    Vector3.up * PlayerFactory.GroundedRootOffset;
                waypoints.Add(dock);
                var exposure = new AlpineColdExposureModel();
                int movementSteps = 0;
                float walkedMetres = 0f;
                double walkedSeconds = 0d;
                Time.captureDeltaTime = stepSeconds;
                yield return null;

                foreach (Vector3 waypoint in waypoints)
                {
                    bool arrived = false;
                    while (movementSteps < MaximumSteps)
                    {
                        Vector3 before = motor.transform.position;
                        Vector3 remaining = waypoint - before;
                        remaining.y = 0f;
                        // Shorten the final step at each bend instead of
                        // charging a whole quarter-second for a partial stride.
                        float delta = Mathf.Min(stepSeconds,
                            remaining.magnitude / ordinaryWalkSpeed);
                        Time.captureDeltaTime = delta;
                        arrived = motor.MoveTowardsApproachWaypoint(
                            waypoint, arrivalRadius, delta);
                        if (arrived) break;
                        Vector3 displacement = motor.transform.position - before;
                        displacement.y = 0f;
                        walkedMetres += displacement.magnitude;
                        movementSteps++;
                        walkedSeconds += delta;
                        exposure.Step(delta, false);
                        Assert.That(motor.InteractionPoseMoveStalled, Is.False,
                            $"The real capsule stalled at {motor.transform.position} toward {waypoint}.");
                        // The motor's normal Update still supplies gravity;
                        // its clock and this exposure clock advance equally.
                        yield return null;
                    }
                    Assert.That(arrived, Is.True,
                        $"The station-to-house route stopped at {motor.transform.position} toward {waypoint}.");
                }
                motor.CancelInteractionPoseMove();
                Time.captureDeltaTime = PinnedFrameSeconds;
                yield return null;

                Vector3 finish = motor.transform.position;
                Vector3 planarError = finish - dock;
                planarError.y = 0f;
                Assert.That(planarError.magnitude, Is.LessThanOrEqualTo(arrivalRadius + 0.02f));
                Assert.That(Mathf.Abs(finish.y - dock.y), Is.LessThan(0.12f),
                    "The hero must reach the door at its real ground height.");
                Assert.That(Physics.Raycast(finish + Vector3.up * 0.15f, Vector3.down,
                    out RaycastHit ground, 0.5f, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore), Is.True);
                Assert.That(ground.collider.transform.IsChildOf(player.GameObject.transform), Is.False);
                Assert.That(world.MothersHouseEntrance.CanInteract(player.Interactor), Is.True);
                Assert.That(player.Interactor.ActiveInteractable,
                    Is.SameAs(world.MothersHouseEntrance), "The reached entrance must offer its real prompt.");

                float walkSeconds = (float)walkedSeconds;
                var report = new FrostWalkReport
                {
                    seed = plan.Seed,
                    maximum_step_seconds = stepSeconds,
                    movement_steps = movementSteps,
                    walk_seconds = walkSeconds,
                    walked_planar_metres = walkedMetres,
                    full_frost_seconds = AlpineColdExposureModel.FullExposureSeconds,
                    frost_at_door = exposure.FrostAmount,
                    start = start,
                    finish = finish,
                    door_dock = dock,
                    reached_entry = true
                };
                string json = JsonUtility.ToJson(report, true);
                TestContext.Out.WriteLine(json);
                string reportDirectory = Path.Combine(Directory.GetCurrentDirectory(),
                    "Captures", "ColdHeroVerification");
                Directory.CreateDirectory(reportDirectory);
                File.WriteAllText(Path.Combine(reportDirectory,
                    "station-to-mothers-house-frost-timing.json"), json);
                Assert.That(AlpineColdExposureModel.FullExposureSeconds,
                    Is.InRange(walkSeconds * 1.12f, walkSeconds * 1.25f),
                    "Maximum frost must arrive shortly after an ordinary station-to-house walk.");
                Assert.That(exposure.ExposureSeconds, Is.EqualTo(walkSeconds));
                Assert.That(exposure.FrostAmount, Is.GreaterThan(0.85f).And.LessThan(1f),
                    "The uninterrupted walk should arrive visibly frosted, before maximum coverage.");
            }
            finally
            {
                Time.captureDeltaTime = PinnedFrameSeconds;
                Object.DestroyImmediate(scene);
            }
        }
    }
}
