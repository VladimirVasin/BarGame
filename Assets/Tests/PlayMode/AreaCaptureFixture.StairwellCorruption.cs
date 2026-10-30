using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Focused Escher puzzle, presence-light and abyss capture.")]
        [PrebuildSetup(typeof(StairwellAuthoredAssetsSetup))]
        public IEnumerator StairwellLabyrinthPuzzles()
        {
            StairwellInteriorRoot stairwell = null;
            GameSessionState.BeginNewGame();
            GameSessionState.CommitHomeSleepAppearance();
            GameSessionState.PrepareStairwellArrival(StairwellArrivalKind.ApartmentDoor);
            float previousDelta = Time.captureDeltaTime;
            bool previousFocus = GameInput.MovementFocused;
            try
            {
                yield return Capture(SceneIds.StairwellInterior,
                    () => stairwell = Object.FindAnyObjectByType<StairwellInteriorRoot>(),
                    () => CorruptedStairwellShots(stairwell));
                Time.captureDeltaTime = 1f / 30f;
                if (!GameSessionState.IsGameTimeRunning)
                    Assert.That(GameSessionState.TryStartPreparedGameTime() || GameSessionState.TryStartGameTimeFromWake(), Is.True);
                GameInput.HandleApplicationFocus(true);
                stairwell.Player.Motor.SetInputEnabled(false);
                yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 3.34f, -3.72f));
                yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 3.34f, -3.2f));
                yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 4.94f, 1.35f));
                yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 4.94f, 1.78f));
                Assert.That(stairwell.Atmosphere.IsLabyrinthDarkness, Is.False,
                    "The initial stairs and stone Cat retain the familiar lighting before the first long flight.");
                Assert.That(stairwell.LabyrinthPuzzles.State.AllSolved, Is.False);
                Assert.That(stairwell.WarehouseEntrance.CanInteract(stairwell.Player.Interactor), Is.False);
                yield return WalkCorruptedLabyrinth(stairwell, capture: true);
                Assert.That(stairwell.LabyrinthPuzzles.State.AllSolved, Is.True);
                for (int stage = 1; stage <= 3; stage++)
                    Assert.That(stairwell.LabyrinthPuzzles.State.CanPassGate(stage), Is.True);
                yield return VerifyDepartedLabyrinthRoomDarkness(stairwell);
                double motion = RequireLabyrinthPresentation(stairwell).AnimationTime;
                int lamp = stairwell.Atmosphere.ActiveLabyrinthLightIndex;
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    for (int frame = 0; frame < 3; frame++) yield return null;
                    Assert.That(RequireLabyrinthPresentation(stairwell).AnimationTime, Is.EqualTo(motion));
                    Assert.That(stairwell.Atmosphere.ActiveLabyrinthLightIndex, Is.EqualTo(lamp));
                }
                var returnRoute = stairwell.Layout.Labyrinth.RouteWaypoints;
                for (int step = returnRoute.Count - 2; step >= 0; step--)
                    yield return WalkStairwellWaypoint(stairwell, returnRoute[step]);
                yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 4.94f, 1.78f));
                yield return new WaitForSecondsRealtime(.15f);
                Assert.That(stairwell.Atmosphere.IsLabyrinthDarkness, Is.False);
                Assert.That(RenderSettings.fog, Is.True);
                foreach (Light practical in stairwell.Atmosphere.PracticalLights)
                    Assert.That(practical.enabled, Is.True, "Returning restores the original lower stairwell lights.");
            }
            finally
            {
                Time.captureDeltaTime = previousDelta;
                GameInput.HandleApplicationFocus(previousFocus);
                GameSessionState.BeginNewGame();
            }
        }

        [UnityTest]
        [Explicit("Focused wider activation radius, shadows and lower-light return capture.")]
        [PrebuildSetup(typeof(StairwellAuthoredAssetsSetup))]
        public IEnumerator StairwellLabyrinthLightRadius()
        {
            StairwellInteriorRoot stairwell = null;
            GameSessionState.BeginNewGame();
            GameSessionState.CommitHomeSleepAppearance();
            GameSessionState.PrepareStairwellArrival(StairwellArrivalKind.ApartmentDoor);
            float previousDelta = Time.captureDeltaTime;
            bool previousFocus = GameInput.MovementFocused;
            try
            {
                yield return Capture(SceneIds.StairwellInterior,
                    () => stairwell = Object.FindAnyObjectByType<StairwellInteriorRoot>(),
                    () => CorruptedStairwellShots(stairwell));
                Time.captureDeltaTime = 1f / 30f;
                if (!GameSessionState.IsGameTimeRunning)
                    Assert.That(GameSessionState.TryStartPreparedGameTime() || GameSessionState.TryStartGameTimeFromWake(), Is.True);
                GameInput.HandleApplicationFocus(true);
                stairwell.Player.Motor.SetInputEnabled(false);
                yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 3.34f, -3.72f));
                yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 3.34f, -3.2f));
                yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 4.94f, 1.35f));
                yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 4.94f, 1.78f));
                Assert.That(stairwell.Atmosphere.IsLabyrinthDarkness, Is.False);
                var route = stairwell.Layout.Labyrinth.RouteWaypoints;
                const int observation = 5; // First upper landing, before any puzzle gate.
                for (int step = 0; step <= observation; step++) yield return WalkStairwellWaypoint(stairwell, route[step]);
                yield return WalkStairwellWaypoint(stairwell, new Vector3(-5.8f, 8.44f, 28f));
                yield return new WaitForSecondsRealtime(.15f);
                Assert.That(stairwell.Atmosphere.ActiveLabyrinthLightCount, Is.EqualTo(2));
                Assert.That(stairwell.Atmosphere.ActiveLabyrinthShadowCount, Is.EqualTo(2));
                Assert.That(stairwell.Atmosphere.LabyrinthLights[3].enabled, Is.True);
                Assert.That(stairwell.Atmosphere.LabyrinthLights[4].enabled, Is.True,
                    "The next lamp must switch on before the hero reaches its pool.");
                var powered = new bool[stairwell.Atmosphere.LabyrinthLights.Length];
                float farthest = 0f;
                for (int index = 0; index < powered.Length; index++)
                {
                    Light lamp = stairwell.Atmosphere.LabyrinthLights[index];
                    powered[index] = lamp.enabled;
                    if (!lamp.enabled) continue;
                    Assert.That(lamp.shadows, Is.EqualTo(LightShadows.Hard));
                    float distance = Vector3.Distance(lamp.transform.position, stairwell.Player.GameObject.transform.position);
                    Assert.That(distance, Is.LessThanOrEqualTo(StairwellInteriorAtmosphere.LabyrinthActivationRadius));
                    farthest = Mathf.Max(farthest, distance);
                }
                Assert.That(farthest, Is.GreaterThan(10f), "A second lamp must already light the distant part of the landing.");
                AimLabyrinthOrbit(stairwell, new Vector3(3f, 9.84f, 28f));
                yield return CaptureCorruptedGameplay(stairwell, "labyrinth-expanded-light-radius");
                stairwell.CameraFollow.RotateYaw(65f);
                yield return new WaitForSecondsRealtime(.15f);
                for (int index = 0; index < powered.Length; index++)
                    Assert.That(stairwell.Atmosphere.LabyrinthLights[index].enabled, Is.EqualTo(powered[index]));
                yield return VerifyDepartedLabyrinthRoomDarkness(stairwell);
                yield return WalkStairwellWaypoint(stairwell, route[observation]);
                for (int step = observation - 1; step >= 0; step--) yield return WalkStairwellWaypoint(stairwell, route[step]);
                yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 4.94f, 1.78f));
                yield return new WaitForSecondsRealtime(.15f);
                Assert.That(stairwell.Atmosphere.IsLabyrinthDarkness, Is.False);
                Assert.That(stairwell.Atmosphere.ActiveLabyrinthLightCount, Is.Zero);
                foreach (Light lamp in stairwell.Atmosphere.LabyrinthLights) Assert.That(lamp.enabled, Is.False);
                foreach (Light lamp in stairwell.Atmosphere.PracticalLights) Assert.That(lamp.enabled, Is.True);
            }
            finally
            {
                Time.captureDeltaTime = previousDelta;
                GameInput.HandleApplicationFocus(previousFocus);
                GameSessionState.BeginNewGame();
            }
        }

        private static Shot[] CorruptedStairwellShots(StairwellInteriorRoot stairwell)
        {
            Assert.That(stairwell.Layout.IsCorrupted, Is.True);
            Assert.That(stairwell.Arrival, Is.EqualTo(StairwellArrivalKind.ApartmentDoor));
            Assert.That(stairwell.Layout.GetSpawn(StairwellArrivalKind.StreetDoor),
                Is.EqualTo(stairwell.Layout.ApartmentSpawn));
            Assert.That(stairwell.World.UpperBlocker, Is.Null);
            Assert.That(stairwell.World.StreetDoor, Is.Null);
            Assert.That(stairwell.StreetExit, Is.Null);
            Assert.That(stairwell.Cat, Is.Null);
            Assert.That(stairwell.DescentBlocker, Is.Null);
            Assert.That(stairwell.Soundscape, Is.Not.Null);
            Assert.That(stairwell.Ambience, Is.Not.Null);
            Assert.That(stairwell.Atmosphere.Dust, Is.Not.Null);
            Assert.That(stairwell.World.DescentWall, Is.Not.Null);
            Assert.That(stairwell.World.UpperLanding, Is.Not.Null);
            Assert.That(stairwell.World.ApartmentDoor, Is.Not.Null);
            Assert.That(stairwell.World.CatRelief, Is.Not.Null);
            Assert.That(stairwell.CatReliefInspection, Is.Not.Null);
            Assert.That(stairwell.Layout.Labyrinth, Is.Not.Null);
            Assert.That(stairwell.Layout.Labyrinth.Flights, Has.Count.EqualTo(3));
            Assert.That(stairwell.World.StairColliders.Count, Is.GreaterThanOrEqualTo(6));
            Assert.That(stairwell.WarehouseEntrance, Is.Not.Null);
            Transform room = stairwell.World.Root;
            foreach (string absent in new[] { "Stairwell Ceiling", "Stairwell Corrupted Ceiling",
                "Stairwell Back Wall", "Stairwell Corrupted Back Wall", "Street Door", "Under Stair Grille",
                "Corrupted Upper Temporary Grille", "Corrupted Upper Landing End Wall" })
                Assert.That(room.Find(absent), Is.Null, "The broken route retained " + absent);
            foreach (string required in new[] { "Stairwell Ground Floor", "Stairwell Dressing",
                "Stairwell Front Wall", "Stairwell Left Wall", "Stairwell Right Wall",
                "Corrupted Back Wall Base", "Corrupted Back Wall Broken Left", "Corrupted Back Wall Broken Right",
                "Corrupted Roof East", "Corrupted Roof South", "Middle Landing" })
                Assert.That(room.Find(required), Is.Not.Null, "The recognizable stairwell is missing " + required);
            Assert.That(room.Find("Corrupted Upper Limit Rail"), Is.Null,
                "The old view-only boundary must open onto the playable continuation.");
            Assert.That(RenderSettings.fog, Is.True);
            Assert.That(RenderSettings.ambientLight.maxColorComponent, Is.GreaterThan(0f));
            Assert.That(Camera.main.backgroundColor, Is.EqualTo(StairwellInteriorAtmosphere.CorruptedFogColor));
            Assert.That(stairwell.Atmosphere.UpperGlow, Is.Not.Null);
            Assert.That(stairwell.Atmosphere.UpperGlow.isActiveAndEnabled, Is.False);
            Assert.That(stairwell.Atmosphere.LabyrinthLights,
                Has.Length.EqualTo(stairwell.Layout.Labyrinth.LightPositions.Count));
            Assert.That(stairwell.Atmosphere.ActiveLabyrinthLightCount, Is.LessThanOrEqualTo(8));
            Assert.That(stairwell.Atmosphere.ActiveLabyrinthShadowCount, Is.LessThanOrEqualTo(2));
            StairwellLabyrinthPresentation presentation = RequireLabyrinthPresentation(stairwell);
            Assert.That(presentation.Rooms, Has.Count.EqualTo(3));
            Assert.That(presentation.Doors, Has.Count.EqualTo(presentation.Rooms.Count));
            Assert.That(presentation.MotionRoots.Count,
                Is.InRange(1, StairwellLabyrinthPresentation.MaximumMotionRoots));
            for (int index = 0; index < presentation.Rooms.Count; index++)
            {
                Assert.That(presentation.Rooms[index].Stage, Is.EqualTo(index + 1));
                Assert.That(presentation.Doors[index].IsClosed, Is.True,
                    "Each puzzle room starts with a usable closed leaf.");
            }
            AssertLabyrinthStoreySelection(stairwell.Layout);
            Assert.That(GameSessionState.HeroCorruptionLevel, Is.EqualTo(7));
            var hero = (Player3DCharacterPresentation)stairwell.Player.Visual;
            Assert.That(hero.CorruptionAppearance.AppliedLevel, Is.EqualTo(7));
            var shots = StairwellFixedCameraController.CreateDefaultShots(stairwell.Layout);
            Assert.That(shots.Count, Is.EqualTo(3));
            Physics.SyncTransforms();
            foreach (StairwellCameraShot shot in shots)
            {
                Assert.That(shot.Focus.Enabled, Is.True);
                Assert.That(shot.FieldOfView, Is.LessThanOrEqualTo(64f));
                Assert.That(Vector3.Angle(shot.Rotation * Vector3.forward,
                    Vector3.ProjectOnPlane(shot.Rotation * Vector3.forward, Vector3.up)), Is.LessThan(30f),
                    "The broken route must retain diagonal views rather than overhead views.");
                Assert.That(Mathf.Abs(shot.Position.x), Is.LessThan(stairwell.Layout.RoomSize.x * .5f));
                Assert.That(Mathf.Abs(shot.Position.z), Is.LessThan(stairwell.Layout.RoomSize.y * .5f));
                Assert.That(Physics.CheckSphere(room.TransformPoint(shot.Position), .08f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.False,
                    "The interior camera must stand clear of the broken shell: " + shot.Kind);
            }
            StairwellCameraShot apartment = shots[0];
            Assert.That(apartment.Kind, Is.EqualTo(StairwellCameraShotKind.ApartmentLanding));
            return new[] { Shot.At("corrupted-00-broken-stairwell",
                room.TransformPoint(apartment.Position),
                room.TransformPoint(apartment.Position + apartment.Rotation * Vector3.forward * 4f),
                apartment.FieldOfView, delayFrames: 4) };
        }

        private static IEnumerator VerifyCorruptedStairwellRoute(StairwellInteriorRoot stairwell)
        {
            PlayerMotor motor = stairwell.Player.Motor;
            Transform hero = stairwell.Player.GameObject.transform;
            Transform room = stairwell.World.Root;
            bool previousMotorInput = motor.InputEnabled;
            bool previousInteractorInput = stairwell.Player.Interactor.InputEnabled;
            bool previousFocus = GameInput.MovementFocused;
            float previousDelta = Time.captureDeltaTime;
            int previousIntoxication = GameSessionState.IntoxicationLevel;
            DrinkId previousDrink = GameSessionState.LastAlcoholicDrink;
            int previousDrinks = GameSessionState.DrinksConsumed;
            Time.captureDeltaTime = 1f / 30f;
            GameInput.HandleApplicationFocus(true);
            motor.SetInputEnabled(false);
            try
            {
                if (!GameSessionState.IsGameTimeRunning)
                    Assert.That(GameSessionState.TryStartPreparedGameTime() ||
                        GameSessionState.TryStartGameTimeFromWake(), Is.True);
                for (int frame = 0; frame < 3; frame++) yield return null;
                Debug.Log("Corrupted stairwell proof: spawn and blocked descent");
                yield return CaptureCorruptedGameplay(stairwell, "corrupted-01-spawn");
                yield return WalkStairwellWaypoint(stairwell, new Vector3(1.45f, 3.34f, -3.62f));
                for (int frame = 0; frame < 90 && !motor.InteractionPoseMoveStalled; frame++)
                {
                    motor.MoveTowardsApproachWaypoint(room.TransformPoint(new Vector3(1.45f, 3.34f, -1.9f)), .025f, 1f / 60f);
                    yield return null;
                }
                Assert.That(room.InverseTransformPoint(hero.position).z, Is.LessThan(-3.20f),
                    "Real wall collision must stop the descent.");
                motor.CancelInteractionPoseMove();
                yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 3.34f, -3.72f));
                yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 3.34f, -3.2f));
                yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 4.44f, -.16f));
                Assert.That(stairwell.FixedCamera.ActiveShotKind, Is.EqualTo(StairwellCameraShotKind.UpperFlight));
                yield return CaptureCorruptedGameplay(stairwell, "corrupted-02-climbing");
                yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 4.94f, 1.35f));
                for (int frame = 0; frame < 3; frame++) yield return null;
                Assert.That(room.InverseTransformPoint(hero.position).y,
                    Is.EqualTo(stairwell.Layout.UpperFlight.TopElevation + PlayerFactory.GroundedRootOffset).Within(.03f));
                Assert.That(stairwell.FixedCamera.ActiveShotKind, Is.EqualTo(StairwellCameraShotKind.UpperLanding));
                yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 4.94f, 1.78f));
                yield return CaptureCorruptedGameplay(stairwell, "corrupted-03-labyrinth-threshold");
                yield return WalkCorruptedLabyrinth(stairwell, capture: true);
                Assert.That(stairwell.Atmosphere.RuntimeProfile.TryGet(out Bloom bloom), Is.True);
                bool bloomActive = bloom.active;
                try
                {
                    bloom.active = false;
                    yield return CaptureCorruptedGameplay(stairwell, "corrupted-04-warehouse-door-bloom-off");
                }
                finally { bloom.active = bloomActive; }
                Vector3 pausePosition = hero.position;
                float pauseProgress = stairwell.Atmosphere.LabyrinthProgress;
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    for (int frame = 0; frame < 3; frame++) yield return null;
                    Assert.That(hero.position, Is.EqualTo(pausePosition));
                    Assert.That(stairwell.Atmosphere.LabyrinthProgress, Is.EqualTo(pauseProgress));
                }
                Debug.Log("Corrupted stairwell proof: returning through all three flights");
                IReadOnlyList<Vector3> route = stairwell.Layout.Labyrinth.RouteWaypoints;
                for (int index = route.Count - 2; index >= 0; index--)
                    yield return WalkStairwellWaypoint(stairwell, route[index]);
                yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 4.94f, 1.78f));
                Assert.That(stairwell.FixedCamera.IsLabyrinthOrbit, Is.False);
                Assert.That(stairwell.CameraFollow.FixedPoseActive, Is.True);
                yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 3.34f, -3.72f));
                yield return WalkStairwellWaypoint(stairwell, stairwell.Layout.ApartmentDoorActionRoot);
                Assert.That(stairwell.ApartmentEntrance.isActiveAndEnabled, Is.True);
                Assert.That(stairwell.FixedCamera.ActiveShotKind, Is.EqualTo(StairwellCameraShotKind.ApartmentLanding));
                yield return CaptureCorruptedGameplay(stairwell, "corrupted-05-return-door");
                yield return VerifyCatReliefInspection(stairwell);

                motor.SetInputEnabled(false);
                GameSessionState.UpdateDrinkingProgress(100, previousDrink, previousDrinks);
                stairwell.CameraFollow.SetIntoxication(1f);
                for (int frame = 0; frame < 30; frame++)
                {
                    GameSessionState.UpdateDrinkingProgress(100, previousDrink, previousDrinks);
                    yield return null;
                }
                Assert.That(GameSessionState.IntoxicationLevel, Is.EqualTo(100));
                yield return CaptureCorruptedGameplay(stairwell, "corrupted-07-maximum-intoxication");
            }
            finally
            {
                NarrativeInteractionController.For(stairwell.Player.Interactor).RestoreImmediate();
                motor.CancelInteractionPoseMove();
                GameSessionState.UpdateDrinkingProgress(previousIntoxication, previousDrink, previousDrinks);
                stairwell.CameraFollow.SetIntoxication(previousIntoxication / 100f);
                motor.SetInputEnabled(previousMotorInput);
                stairwell.Player.Interactor.SetInputEnabled(previousInteractorInput);
                Time.captureDeltaTime = previousDelta;
                GameInput.HandleApplicationFocus(previousFocus);
            }
        }

        private static IEnumerator WalkCorruptedLabyrinth(StairwellInteriorRoot stairwell, bool capture)
        {
            StairwellLabyrinthPlan labyrinth = stairwell.Layout.Labyrinth;
            StairwellLabyrinthPresentation presentation = RequireLabyrinthPresentation(stairwell);
            var captured = new bool[3];
            var visited = new bool[labyrinth.Branches.Count];
            var roomsVisited = new bool[presentation.Rooms.Count];
            for (int routeIndex = 0; routeIndex < labyrinth.RouteWaypoints.Count; routeIndex++)
            {
                Vector3 waypoint = labyrinth.RouteWaypoints[routeIndex];
                yield return WalkStairwellWaypoint(stairwell, waypoint);
                if (labyrinth.ContainsPosition(waypoint))
                {
                    Assert.That(stairwell.FixedCamera.IsLabyrinthOrbit, Is.True,
                        "Every playable maze branch retains the ordinary orbit camera.");
                    Assert.That(stairwell.CameraFollow.FixedPoseActive, Is.False);
                }
                if (!capture) continue;
                for (int index = 0; index < labyrinth.Branches.Count; index++)
                {
                    StairwellLabyrinthBranchPlan branch = labyrinth.Branches[index];
                    if (visited[index] || Vector3.Distance(waypoint, branch.Waypoints[0]) > .1f) continue;
                    Debug.Log("Corrupted stairwell proof: branch " + branch.Id);
                    for (int step = 1; step < branch.Waypoints.Count; step++)
                    {
                        yield return WalkStairwellWaypoint(stairwell, branch.Waypoints[step]);
                        Assert.That(stairwell.FixedCamera.IsLabyrinthOrbit, Is.True,
                            "An excursion must not re-enter a fixed camera: " + branch.Id);
                        Assert.That(stairwell.CameraFollow.FixedPoseActive, Is.False);
                    }
                    for (int roomIndex = 0; roomIndex < presentation.Rooms.Count; roomIndex++)
                    {
                        if (presentation.Rooms[roomIndex].BranchId != branch.Id) continue;
                        Assert.That(roomsVisited[roomIndex], Is.False);
                        yield return VerifyLabyrinthPuzzleRoom(stairwell, presentation, roomIndex);
                        yield return WalkStairwellWaypoint(stairwell, branch.Waypoints[branch.Waypoints.Count - 1]);
                        roomsVisited[roomIndex] = true;
                    }
                    if (branch.IsLoop)
                    {
                        int end = LabyrinthWaypointIndex(labyrinth.RouteWaypoints,
                            branch.Waypoints[branch.Waypoints.Count - 1]);
                        Assert.That(end, Is.GreaterThanOrEqualTo(0));
                        int direction = end > routeIndex ? -1 : 1;
                        for (int step = end + direction; step != routeIndex + direction; step += direction)
                            yield return WalkStairwellWaypoint(stairwell, labyrinth.RouteWaypoints[step]);
                    }
                    else
                    {
                        for (int step = branch.Waypoints.Count - 2; step >= 0; step--)
                            yield return WalkStairwellWaypoint(stairwell, branch.Waypoints[step]);
                    }
                    visited[index] = true;
                }
                for (int stage = 0; stage < 3; stage++)
                {
                    if (captured[stage] || Vector3.Distance(waypoint, labyrinth.CapturePositions[stage]) > .1f) continue;
                    AimLabyrinthOrbit(stairwell, labyrinth.CaptureLookTargets[stage]);
                    yield return new WaitForSecondsRealtime(.15f);
                    Assert.That(labyrinth.StageAt(stairwell.World.Root.InverseTransformPoint(
                        stairwell.Player.GameObject.transform.position)), Is.EqualTo(stage + 1));
                    Assert.That(stairwell.Atmosphere.ActiveLabyrinthLightCount, Is.InRange(1, 8));
                    Assert.That(stairwell.Atmosphere.ActiveLabyrinthShadowCount, Is.LessThanOrEqualTo(2));
                    Debug.Log("Corrupted stairwell proof: large labyrinth stage " + (stage + 1));
                    yield return CaptureCorruptedGameplay(stairwell, "labyrinth-0" + (stage + 1) + "-gameplay");
                    yield return VerifyLabyrinthMotion(stairwell, presentation, stage == 0);
                    yield return CaptureLabyrinthEnclosure(stairwell, stage + 1);
                    captured[stage] = true;
                }
            }
            if (capture)
            {
                foreach (bool value in captured) Assert.That(value, Is.True, "Each stage requires a real gameplay composition.");
                foreach (bool value in visited) Assert.That(value, Is.True, "Each false route must be entered and returned from.");
                foreach (bool value in roomsVisited) Assert.That(value, Is.True,
                    "Each stage requires an opened, entered and exited puzzle room.");
            }
            AimLabyrinthOrbit(stairwell, labyrinth.DoorPosition + Vector3.up * 1.4f);
            // The atmosphere is deliberately updated at 10 Hz of unscaled
            // time; accelerated capture frames are not a real refresh period.
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(stairwell.Atmosphere.LabyrinthProgress, Is.GreaterThan(.97f));
            Assert.That(RenderSettings.fog, Is.False, "The warehouse threshold must remain readable without the former terminal haze.");
            Assert.That(stairwell.World.Root.InverseTransformPoint(stairwell.Player.GameObject.transform.position).y,
                Is.EqualTo(labyrinth.DoorPosition.y + PlayerFactory.GroundedRootOffset).Within(.04f));
        }

        private static StairwellLabyrinthPresentation RequireLabyrinthPresentation(StairwellInteriorRoot stairwell)
        {
            Transform root = stairwell.World.Root.Find("Stairwell Labyrinth");
            Assert.That(root, Is.Not.Null);
            StairwellLabyrinthPresentation presentation = root.GetComponent<StairwellLabyrinthPresentation>();
            Assert.That(presentation, Is.Not.Null);
            return presentation;
        }

        private static IEnumerator VerifyLabyrinthPuzzleRoom(StairwellInteriorRoot stairwell,
            StairwellLabyrinthPresentation presentation, int index)
        {
            var room = presentation.Rooms[index];
            InteriorHingedDoorInteraction door = presentation.Doors[index];
            PlayerMotor motor = stairwell.Player.Motor;
            bool previousMotorInput = motor.InputEnabled;
            bool previousInteractorInput = stairwell.Player.Interactor.InputEnabled;
            Debug.Log("Corrupted stairwell proof: puzzle room stage " + room.Stage);
            try
            {
                motor.SetInputEnabled(false);
                stairwell.Player.Interactor.SetInputEnabled(true);
                yield return WalkStairwellWaypoint(stairwell, room.OutsideDock);
                AimLabyrinthOrbit(stairwell, stairwell.World.Root.InverseTransformPoint(door.InteractionPosition));
                Assert.That(door.IsClosed, Is.True);
                Assert.That(door.PromptKey, Is.EqualTo("interaction.open_village_door"));
                Assert.That(door.Begin(), Is.True, "The room door must open from its real approach.");
                yield return WaitForCorruptedStairwell(() =>
                    stairwell.AnimatedInteraction.Phase == PlayerAnimatedInteractionPhase.Looping,
                    "The room door never reached the shared hero action.");
                Assert.That(door.OwnsActiveInteraction, Is.True);
                Assert.That(((Player3DCharacterPresentation)stairwell.Player.Visual).ActiveClipName,
                    Is.EqualTo("DoorUseLoop"));
                AssertLabyrinthDoorHandContact(door, room.Stage, "outside opening");
                if (index == 0)
                {
                    float pausedFraction = door.OpenFraction;
                    using (GameTimeScaleRuntime.AcquirePause())
                    {
                        for (int frame = 0; frame < 3; frame++) yield return null;
                        Assert.That(door.OpenFraction, Is.EqualTo(pausedFraction));
                    }
                }
                yield return WaitForCorruptedStairwell(() => !door.OwnsActiveInteraction,
                    "The room door did not finish opening.");
                Assert.That(door.IsOpen, Is.True);
                AssertLabyrinthDoorSolid(door);
                motor.SetInputEnabled(false);
                yield return WalkStairwellWaypoint(stairwell, room.PassagePoint);
                yield return WalkStairwellWaypoint(stairwell, room.InteriorPoint);
                Assert.That(stairwell.Layout.Labyrinth.StageAt(stairwell.World.Root.InverseTransformPoint(
                    stairwell.Player.GameObject.transform.position)), Is.EqualTo(room.Stage));
                Assert.That(stairwell.FixedCamera.IsLabyrinthOrbit, Is.True,
                    "Entering a room must retain the labyrinth camera.");
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneIds.StairwellInterior));
                Assert.That(SceneTransitionService.IsTransitioning, Is.False);
                AimLabyrinthOrbit(stairwell, room.InteriorPoint + Vector3.left * 1.2f + Vector3.up * 1.4f);
                yield return CaptureCorruptedGameplay(stairwell, "labyrinth-0" + room.Stage + "-puzzle-room");

                yield return VerifyLabyrinthPuzzleActions(stairwell, room.Stage);

                Assert.That(door.Begin(), Is.True, "The hero must be able to close a room from inside.");
                yield return WaitForCorruptedStairwell(() => !door.OwnsActiveInteraction,
                    "Closing the room from inside did not finish.");
                Assert.That(door.IsClosed, Is.True);
                AssertLabyrinthDoorSolid(door);
                Assert.That(door.Begin(), Is.True, "The room must reopen from inside after solving its mechanism.");
                yield return WaitForCorruptedStairwell(() =>
                    stairwell.AnimatedInteraction.Phase == PlayerAnimatedInteractionPhase.Looping,
                    "Reopening the room never reached the shared hero action.");
                AssertLabyrinthDoorHandContact(door, room.Stage, "inside reopening");
                yield return WaitForCorruptedStairwell(() => !door.OwnsActiveInteraction,
                    "Reopening the room from inside did not finish.");
                Assert.That(door.IsOpen, Is.True);
                Assert.That(stairwell.Player.Interactor.InputEnabled, Is.True);
                Assert.That(stairwell.AnimatedInteraction.Phase, Is.EqualTo(PlayerAnimatedInteractionPhase.Idle));
                motor.SetInputEnabled(false);
                yield return WalkStairwellWaypoint(stairwell, room.InteriorPoint);
                yield return WalkStairwellWaypoint(stairwell, room.PassagePoint);
                yield return WalkStairwellWaypoint(stairwell, room.OutsideDock);
                Assert.That(stairwell.FixedCamera.IsLabyrinthOrbit, Is.True);
                AssertLabyrinthDoorSolid(door);
            }
            finally
            {
                door.Cancel();
                motor.CancelInteractionPoseMove();
                motor.SetInputEnabled(previousMotorInput);
                stairwell.Player.Interactor.SetInputEnabled(previousInteractorInput);
            }
        }

        private static IEnumerator VerifyLabyrinthPuzzleActions(StairwellInteriorRoot stairwell, int stage)
        {
            StairwellLabyrinthPuzzleState state = stairwell.LabyrinthPuzzles.State;
            Assert.That(state.CanPassGate(stage), Is.False, "The next part must remain locked before its puzzle.");
            var leaf = stairwell.LabyrinthPuzzles.GateRoots[stage - 1].GetComponent<BoxCollider>();
            Vector3 gateFloor = stairwell.LabyrinthPuzzles.GateRoots[stage - 1].position;
            Vector3 normal = stairwell.World.Root.TransformDirection(stage == 2 ? Vector3.forward : Vector3.right);
            var gateRay = new Ray(gateFloor + Vector3.up * 1.3f - normal * .8f, normal);
            Assert.That(leaf.enabled && !leaf.isTrigger && leaf.Raycast(gateRay, out _, 1.6f), Is.True,
                "A locked passage requires an actual solid gate.");
            string failed = stage == 1 ? "cloth-bolt" : stage == 2 ? "carriage-send" : "bird-lift";
            int revision = state.Revision;
            yield return OperateLabyrinthControl(stairwell, failed);
            Assert.That(state.Revision, Is.EqualTo(revision), "An obstructed mechanism cannot commit progress.");
            string[] actions = stage == 1
                ? new[] { "roll-0", "roll-0", "roll-1", "roll-2", "roll-2", "cloth-bolt" }
                : stage == 2
                    ? new[] { "shutter", "weight-switch", "weight-switch", "shutter", "shutter-stop", "weight-switch", "carriage-send" }
                    : new[] { "bird-0", "bird-0", "bird-1", "bird-2", "bird-2", "bird-lift" };
            foreach (string id in actions) yield return OperateLabyrinthControl(stairwell, id);
            Assert.That(state.CanPassGate(stage), Is.True);
            Physics.SyncTransforms();
            Assert.That(leaf.Raycast(gateRay, out _, 1.6f), Is.False,
                "The solved gate must physically clear the hero's passage.");
            Assert.That(state.SolvedStages, Is.EqualTo(stage));
            if (stage < 3) Assert.That(state.CanPassGate(stage + 1), Is.False);
            Debug.Log("STAIRWELL_MYTH_PUZZLE_SOLVED stage=" + stage);
            Vector3 roomCentre = stairwell.LabyrinthPresentation.Rooms[stage - 1].InteriorPoint;
            yield return WalkStairwellWaypoint(stairwell, roomCentre);
            AimLabyrinthOrbit(stairwell, roomCentre + Vector3.left * 1.2f + Vector3.up * 1.4f);
            yield return CaptureCorruptedGameplay(stairwell, "labyrinth-0" + stage + "-solved-mechanism");
        }

        private static IEnumerator OperateLabyrinthControl(StairwellInteriorRoot stairwell, string id)
        {
            StairwellLabyrinthPuzzleInteraction control = null;
            foreach (var candidate in stairwell.LabyrinthPuzzles.Controls)
                if (candidate.Id == id) { control = candidate; break; }
            Assert.That(control, Is.Not.Null, "Missing physical control " + id);
            stairwell.Player.Motor.SetInputEnabled(false);
            stairwell.Player.Interactor.SetInputEnabled(true);
            yield return WalkStairwellWaypoint(stairwell,
                stairwell.World.Root.InverseTransformPoint(control.Plan.EntryRootPosition));
            AimLabyrinthOrbit(stairwell, stairwell.World.Root.InverseTransformPoint(control.InteractionPosition));
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(control.Begin(), Is.True, "The shared hero must reach and use " + id);
            yield return WaitForCorruptedStairwell(() =>
                stairwell.AnimatedInteraction.Phase == PlayerAnimatedInteractionPhase.Looping,
                "The puzzle control never reached hand contact " + id);
            control.RefreshHandContact();
            Assert.That(control.ContactWeight, Is.GreaterThan(.99f));
            Assert.That(control.RightWristDistance, Is.LessThanOrEqualTo(control.RightReachLimit + .02f),
                id + " wrist=" + control.RightWristDistance + " reach=" + control.RightReachLimit +
                " root=" + stairwell.Player.GameObject.transform.position + " target=" + control.InteractionPosition);
            Assert.That(Vector3.Distance(control.RightGrip.position, control.InteractionPosition), Is.LessThan(.03f),
                "The original hero hand must meet the actual puzzle handle " + id);
            yield return WaitForCorruptedStairwell(() => !control.OwnsActiveInteraction,
                "The physical puzzle control never released " + id);
            Assert.That(stairwell.AnimatedInteraction.Phase, Is.EqualTo(PlayerAnimatedInteractionPhase.Idle));
            stairwell.Player.Motor.SetInputEnabled(false);
        }

        private static IEnumerator VerifyDepartedLabyrinthRoomDarkness(StairwellInteriorRoot stairwell)
        {
            // The hero remains in the final lit sector. A diagnostic camera inside the
            // departed first room proves that its surfaces/plafond and post stack are black.
            Camera camera = stairwell.CameraFollow.Camera;
            var room = stairwell.LabyrinthPresentation.Rooms[0];
            Transform space = stairwell.World.Root;
            var target = new RenderTexture(320, 180, 24);
            var pixels = new Texture2D(320, 180, TextureFormat.RGB24, false);
            RenderTexture oldTarget = camera.targetTexture;
            Pose oldPose = new Pose(camera.transform.position, camera.transform.rotation);
            float oldAspect = camera.aspect;
            int lamp = stairwell.Atmosphere.ActiveLabyrinthLightIndex;
            target.Create();
            using (stairwell.CameraFollow.SuspendPresentation())
            {
                try
                {
                    camera.targetTexture = target;
                    camera.aspect = 16f / 9f;
                    Vector3 origin = space.TransformPoint(room.InteriorPoint + Vector3.up * 1.4f);
                    camera.transform.SetPositionAndRotation(origin,
                        Quaternion.LookRotation(space.TransformPoint(room.OutsideDock + Vector3.up * 1.4f) - origin));
                    yield return new WaitForSecondsRealtime(.15f);
                    yield return ScarfRenderedFrame(camera, () =>
                    {
                        RenderTexture active = RenderTexture.active;
                        try
                        {
                            RenderTexture.active = target;
                            pixels.ReadPixels(new Rect(0, 0, 320, 180), 0, 0);
                            pixels.Apply();
                            Color32[] values = pixels.GetPixels32();
                            long sum = 0; int maximum = 0;
                            foreach (Color32 value in values)
                            {
                                int channel = Mathf.Max(value.r, Mathf.Max(value.g, value.b));
                                sum += channel; maximum = Mathf.Max(maximum, channel);
                            }
                            float mean = sum / (float)values.Length;
                            Debug.Log("STAIRWELL_DEPARTED_ROOM_BLACK mean=" + mean + ", max=" + maximum);
                            Assert.That(mean, Is.LessThanOrEqualTo(1f), "An unoccupied room must not retain readable ambient or luminous fixtures.");
                            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.StairwellInterior);
                            Directory.CreateDirectory(folder);
                            File.WriteAllBytes(Path.Combine(folder, "labyrinth-departed-room-black.png"), pixels.EncodeToPNG());
                        }
                        finally { RenderTexture.active = active; }
                    });
                    Assert.That(stairwell.Atmosphere.ActiveLabyrinthLightIndex, Is.EqualTo(lamp),
                        "A camera move must not change presence lighting.");
                }
                finally
                {
                    camera.targetTexture = oldTarget;
                    camera.aspect = oldAspect;
                    camera.transform.SetPositionAndRotation(oldPose.position, oldPose.rotation);
                    target.Release();
                    Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
                }
            }
        }

        private static void AssertLabyrinthDoorSolid(InteriorHingedDoorInteraction door)
        {
            bool solidLeaf = false;
            foreach (BoxCollider collider in door.GetComponentsInChildren<BoxCollider>())
                if (collider.enabled && !collider.isTrigger) solidLeaf = true;
            Assert.That(solidLeaf, Is.True, "The openable leaf must retain its physical collider.");
        }

        private static void AssertLabyrinthDoorHandContact(InteriorHingedDoorInteraction door, int stage, string action)
        {
            // The coroutine resumes after animation sampling, before LateUpdate.
            // Use the production same-frame contact seam before reading bones.
            door.RefreshHandContact();
            Assert.That(door.RightGrip, Is.Not.Null);
            float weight = door.ContactWeight;
            float error = Vector3.Distance(door.RightGrip.position, door.InteractionPosition);
            string diagnostic = $"stage={stage}, action={action}, gripError={error:F5}, weight={weight:F5}, " +
                $"wristDistance={door.RightWristDistance:F5}, reachLimit={door.RightReachLimit:F5}, " +
                $"grip={door.RightGrip.position:F5}, handle={door.InteractionPosition:F5}";
            Debug.Log("STAIRWELL_PUZZLE_DOOR_CONTACT " + diagnostic);
            Assert.That(weight, Is.GreaterThan(.65f), "Observe the engaged handle contact. " + diagnostic);
            Assert.That(door.RightWristDistance, Is.LessThanOrEqualTo(door.RightReachLimit + .02f),
                "The authored door dock must fit the hero's actual arm reach. " + diagnostic);
            float tolerance = .025f + (weight >= .9999f ? 0f : (1f - weight) * .5f);
            Assert.That(error, Is.LessThanOrEqualTo(tolerance),
                "The shared rig must reach the moving physical handle. " + diagnostic);
        }

        private static IEnumerator VerifyLabyrinthMotion(StairwellInteriorRoot stairwell,
            StairwellLabyrinthPresentation presentation, bool verifyDisable)
        {
            int count = presentation.MotionRoots.Count;
            Assert.That(count, Is.InRange(1, StairwellLabyrinthPresentation.MaximumMotionRoots));
            Pose[] before = ReadLabyrinthMotion(presentation);
            double previousTime = presentation.AnimationTime;
            for (int frame = 0; frame < 12; frame++) yield return null;
            Assert.That(presentation.AnimationTime, Is.GreaterThan(previousTime));
            Assert.That(presentation.MotionRoots.Count, Is.EqualTo(count));
            bool moved = false;
            for (int index = 0; index < count; index++)
            {
                Transform root = presentation.MotionRoots[index];
                if (Vector3.Distance(root.localPosition, before[index].position) > .0001f ||
                    Quaternion.Angle(root.localRotation, before[index].rotation) > .01f) moved = true;
            }
            Assert.That(moved, Is.True, "Each stage must present actual changing geometry.");
            Pose[] paused = ReadLabyrinthMotion(presentation);
            double pausedTime = presentation.AnimationTime;
            using (GameTimeScaleRuntime.AcquirePause())
            {
                for (int frame = 0; frame < 3; frame++) yield return null;
                Assert.That(presentation.AnimationTime, Is.EqualTo(pausedTime));
                AssertLabyrinthMotionUnchanged(presentation, paused);
            }
            if (!verifyDisable) yield break;
            bool wasEnabled = presentation.enabled;
            try
            {
                presentation.enabled = false;
                Pose[] disabled = ReadLabyrinthMotion(presentation);
                double disabledTime = presentation.AnimationTime;
                for (int frame = 0; frame < 3; frame++) yield return null;
                Assert.That(presentation.AnimationTime, Is.EqualTo(disabledTime));
                AssertLabyrinthMotionUnchanged(presentation, disabled);
            }
            finally { presentation.enabled = wasEnabled; }
            yield return null;
            Assert.That(stairwell.Atmosphere.ActiveLabyrinthLightCount, Is.LessThanOrEqualTo(8));
            Assert.That(stairwell.Atmosphere.ActiveLabyrinthShadowCount, Is.LessThanOrEqualTo(2));
        }

        private static Pose[] ReadLabyrinthMotion(StairwellLabyrinthPresentation presentation)
        {
            var poses = new Pose[presentation.MotionRoots.Count];
            for (int index = 0; index < poses.Length; index++)
            {
                Transform root = presentation.MotionRoots[index];
                Assert.That(root, Is.Not.Null);
                poses[index] = new Pose(root.localPosition, root.localRotation);
            }
            return poses;
        }

        private static void AssertLabyrinthMotionUnchanged(StairwellLabyrinthPresentation presentation, Pose[] poses)
        {
            Assert.That(presentation.MotionRoots.Count, Is.EqualTo(poses.Length));
            for (int index = 0; index < poses.Length; index++)
            {
                Assert.That(presentation.MotionRoots[index].localPosition, Is.EqualTo(poses[index].position));
                Assert.That(presentation.MotionRoots[index].localRotation, Is.EqualTo(poses[index].rotation));
            }
        }

        private static IEnumerator CaptureLabyrinthEnclosure(StairwellInteriorRoot stairwell, int stage)
        {
            // Diagnostic eye-level views complement the real third-person
            // frames. The final view looks beyond a safety rail into the abyss,
            // rather than straight at the walkway under the hero's feet.
            Camera camera = stairwell.CameraFollow.Camera;
            Transform hero = stairwell.Player.GameObject.transform;
            Vector3 forward = Vector3.ProjectOnPlane(hero.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3[] directions = { forward, right, -forward, -right, Vector3.up, Vector3.down };
            Vector3 origin = hero.position + Vector3.up * 1.5f;
            Vector3 abyssEdge = stage == 1 ? Vector3.forward : stage == 2 ? Vector3.left :
                Vector3.right + Vector3.forward;
            const int width = 640, height = 360;
            var target = new RenderTexture(width, height, 24);
            var sheet = new Texture2D(width * 3, height * 2, TextureFormat.RGB24, false);
            RenderTexture previousTarget = camera.targetTexture;
            Pose previousPose = new Pose(camera.transform.position, camera.transform.rotation);
            float previousAspect = camera.aspect, previousLens = camera.fieldOfView;
            target.Create();
            using (stairwell.CameraFollow.SuspendPresentation())
            using (stairwell.Player.PresentationVisibility.AcquireHidden(new object()))
            {
                try
                {
                    camera.targetTexture = target;
                    camera.aspect = width / (float)height;
                    camera.fieldOfView = 90f;
                    for (int view = 0; view < directions.Length; view++)
                    {
                        int tile = view;
                        Vector3 viewOrigin = view == 5 ? origin + abyssEdge * 2.1f : origin;
                        Vector3 viewDirection = view == 5 ? Vector3.down + abyssEdge * 1.3f : directions[view];
                        camera.transform.SetPositionAndRotation(viewOrigin, Quaternion.LookRotation(viewDirection,
                            view == 4 ? forward : Vector3.up));
                        yield return new WaitForSecondsRealtime(.15f);
                        yield return ScarfRenderedFrame(camera, () =>
                        {
                            RenderTexture previousActive = RenderTexture.active;
                            try
                            {
                                RenderTexture.active = target;
                                sheet.ReadPixels(new Rect(0, 0, width, height),
                                    (tile % 3) * width, (1 - tile / 3) * height, false);
                            }
                            finally { RenderTexture.active = previousActive; }
                        });
                    }
                    sheet.Apply();
                    string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.StairwellInterior);
                    Directory.CreateDirectory(folder);
                    string path = Path.Combine(folder, "labyrinth-0" + stage +
                        "-structure-forward-right-back__left-up-abyss.png");
                    File.WriteAllBytes(path, sheet.EncodeToPNG());
                    Debug.Log("Area structure capture wrote " + path + "; top: forward/right/back; bottom: left/up/abyss");
                    Assert.That(IsBlank(sheet), Is.False);
                }
                finally
                {
                    camera.targetTexture = previousTarget;
                    camera.aspect = previousAspect;
                    camera.fieldOfView = previousLens;
                    camera.transform.SetPositionAndRotation(previousPose.position, previousPose.rotation);
                    target.Release();
                    Object.DestroyImmediate(target);
                    Object.DestroyImmediate(sheet);
                }
            }
            stairwell.CameraFollow.Snap();
        }

        private static int LabyrinthWaypointIndex(IReadOnlyList<Vector3> route, Vector3 point)
        {
            for (int i = 0; i < route.Count; i++)
                if (Vector3.Distance(route[i], point) < .1f) return i;
            return -1;
        }

        private static void AimLabyrinthOrbit(StairwellInteriorRoot stairwell, Vector3 localTarget)
        {
            PlayerCameraFollow follow = stairwell.CameraFollow;
            follow.Snap();
            Vector3 direction = stairwell.World.Root.TransformPoint(localTarget) -
                (stairwell.Player.GameObject.transform.position + Vector3.up * 1.4f);
            float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;
            follow.RotateYaw(Mathf.DeltaAngle(follow.Camera.transform.eulerAngles.y, yaw));
            follow.RotatePitch(pitch - follow.TargetOrbitPitch);
            follow.Snap();
        }

        private static void AssertLabyrinthStoreySelection(StairwellLayoutPlan plan)
        {
            var area = new StairwellWalkableArea(plan);
            bool crossing = false;
            foreach (StairwellLabyrinthSupportPlan upper in plan.Labyrinth.Supports)
            foreach (StairwellLabyrinthSupportPlan lower in plan.Labyrinth.Supports)
            {
                if (upper.IsRamp || lower.IsRamp || upper.Start.y - lower.Start.y < 2.3f || upper.Start.y - lower.Start.y > 4.5f ||
                    !upper.Bounds.Overlaps(lower.Bounds)) continue;
                Rect overlap = Rect.MinMaxRect(Mathf.Max(upper.Bounds.xMin, lower.Bounds.xMin),
                    Mathf.Max(upper.Bounds.yMin, lower.Bounds.yMin), Mathf.Min(upper.Bounds.xMax, lower.Bounds.xMax),
                    Mathf.Min(upper.Bounds.yMax, lower.Bounds.yMax));
                if (overlap.width < .7f || overlap.height < .7f) continue;
                Vector3 onUpper = new Vector3(overlap.center.x, upper.Start.y + PlayerFactory.GroundedRootOffset, overlap.center.y);
                Vector3 onLower = new Vector3(overlap.center.x, lower.Start.y + PlayerFactory.GroundedRootOffset, overlap.center.y);
                if (!area.TryGetHeight(onUpper, out float upperFloor, .32f) ||
                    !area.TryGetHeight(onLower, out float lowerFloor, .32f)) continue;
                Assert.That(upperFloor, Is.EqualTo(upper.Start.y).Within(.01f));
                Assert.That(lowerFloor, Is.EqualTo(lower.Start.y).Within(.01f));
                Vector3 between = new Vector3(overlap.center.x, (upper.Start.y + lower.Start.y) * .5f, overlap.center.y);
                Assert.That(area.Contains(between, .32f), Is.False,
                    "A lower storey cannot grant support to a capsule in the shaft above it.");
                crossing = true;
                break;
            }
            Assert.That(crossing, Is.True, "The larger maze needs a real playable crossing with sufficient headroom.");
        }

        private static IEnumerator VerifyStairwellWarehouseTransfer(StairwellInteriorRoot stairwell)
        {
            PlayerMotor motor = stairwell.Player.Motor;
            bool previousMotorInput = motor.InputEnabled;
            bool previousInteractorInput = stairwell.Player.Interactor.InputEnabled;
            bool previousFocus = GameInput.MovementFocused;
            float previousDelta = Time.captureDeltaTime;
            var input = new InputTestFixture();
            input.Setup();
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                Time.captureDeltaTime = 1f / 30f;
                GameInput.HandleApplicationFocus(true);
                motor.SetInputEnabled(false);
                yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 3.34f, -3.72f));
                yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 4.94f, 1.78f));
                yield return WalkCorruptedLabyrinth(stairwell, capture: false);
                var door = stairwell.WarehouseEntrance;
                PlayerDoorActionTarget target = door.GetComponent<PlayerDoorActionTarget>();
                Assert.That(target, Is.Not.Null);
                yield return WalkStairwellWaypoint(stairwell,
                    stairwell.World.Root.InverseTransformPoint(target.Plan.EntryRootPosition));
                motor.SetInputEnabled(true);
                stairwell.Player.Interactor.SetInputEnabled(true);
                Physics.SyncTransforms();
                yield return WaitForCorruptedStairwell(() =>
                    ReferenceEquals(stairwell.Player.Interactor.ActiveInteractable, door),
                    "The final warehouse door did not offer its real E interaction.");

                const string continuityItem = "test.stairwell-warehouse.continuity";
                Assert.That(GameSessionState.TryCollectWorldItem(continuityItem, InventoryItemId.VodkaBottle), Is.True);
                var inventory = new List<InventoryItemStack>(GameSessionState.InventoryItems);
                var quests = new List<QuestLogEntry>(GameSessionState.Quests);
                int day = GameSessionState.GameDayNumber, dose = GameSessionState.HeroCorruptionDose;
                double minutes = GameSessionState.GameTimeOfDayMinutes;
                Assert.That(GameSessionState.IsGameTimeRunning, Is.True);
                Debug.Log("Corrupted stairwell proof: shared door gesture and story warehouse transfer");
                input.Press(keyboard.eKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.eKey, queueEventOnly: true);
                Assert.That(stairwell.AnimatedInteraction.IsActive, Is.True,
                    "Entering the warehouse must begin the shared door rig action.");
                yield return WaitForCorruptedStairwell(() => stairwell.AnimatedInteraction.Phase == PlayerAnimatedInteractionPhase.Looping,
                    "The warehouse entrance never reached its shared DoorUseLoop.");
                var contactProbe = stairwell.gameObject.AddComponent<WarehouseRefrigeratorPresentationProbe>();
                yield return AtWarehouseFridgePresentation(contactProbe, () =>
                {
                    var presentation = (Player3DCharacterPresentation)stairwell.Player.Visual;
                    Assert.That(presentation.ActiveClipName, Is.EqualTo("DoorUseLoop"));
                    MeshRenderer lever = stairwell.World.WarehouseDoor.Find("Home Entry Door Levers").GetComponent<MeshRenderer>();
                    Vector3 grip = presentation.Registry.Anchors.RightGrip.position;
                    float gap = Vector3.Distance(grip, lever.bounds.ClosestPoint(grip));
                    Debug.Log("STAIRWELL_LABYRINTH_DOOR_GRIP gap=" + gap.ToString("F5", System.Globalization.CultureInfo.InvariantCulture));
                    Assert.That(gap, Is.LessThanOrEqualTo(.025f),
                        "The guided world-rig door action must reach the actual imported lever.");
                    CaptureCurrentCamera(Camera.main, SceneIds.StairwellInterior, "labyrinth-04-warehouse-door-contact");
                });

                CombatTestRoot warehouse = null;
                float deadline = Time.realtimeSinceStartup + 30f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    if (SceneManager.GetActiveScene().name == SceneIds.CombatTest)
                        warehouse = Object.FindAnyObjectByType<CombatTestRoot>();
                    if (warehouse != null && warehouse.IsInitialized && !SceneTransitionService.IsTransitioning) break;
                    yield return null;
                }
                if (warehouse == null && SceneManager.GetActiveScene().name == SceneIds.CombatTest)
                    warehouse = Object.FindAnyObjectByType<CombatTestRoot>();
                Assert.That(warehouse, Is.Not.Null, "The warehouse did not load through the shared door transition.");
                Assert.That(warehouse.IsInitialized, Is.True);
                Assert.That(SceneTransitionService.IsTransitioning, Is.False);
                warehouse.AutomaticOpponentDecisions = false;
                Assert.That(warehouse.IsStoryContinuation, Is.True);
                Assert.That(warehouse.Mode, Is.EqualTo(CombatTestMode.Endless));
                Assert.That(warehouse.EncounterSettings, Is.EqualTo(CombatEncounterSettings.Default));
                Assert.That(warehouse.LoadoutSelection, Is.EqualTo(CombatLoadoutSelection.Default));
                Assert.That(GameSessionState.WarehouseStorySeed.HasValue, Is.True);
                Assert.That(warehouse.WarehousePlan.Seed, Is.EqualTo(GameSessionState.WarehouseStorySeed.Value));
                Assert.That(GameSessionState.HomeSleepAppearanceActive, Is.True);
                Assert.That(GameSessionState.HeroCorruptionDose, Is.EqualTo(dose));
                Assert.That(GameSessionState.GameDayNumber, Is.EqualTo(day));
                Assert.That(GameSessionState.GameTimeOfDayMinutes, Is.GreaterThanOrEqualTo(minutes));
                Assert.That(GameSessionState.IsGameTimeRunning, Is.True);
                Assert.That(GameSessionState.IsWorldItemCollected(continuityItem), Is.True);
                CollectionAssert.AreEqual(inventory, GameSessionState.InventoryItems);
                CollectionAssert.AreEqual(quests, GameSessionState.Quests);
                Assert.That(Object.FindObjectsByType<PlayerMotor>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
                Assert.That(Object.FindAnyObjectByType<StairwellInteriorRoot>(), Is.Null);
                Assert.That(warehouse.Player.Motor.InputEnabled && warehouse.Player.Interactor.InputEnabled, Is.True);
                Assert.That(warehouse.CameraFollow.FixedPoseActive, Is.False);
                Assert.That(warehouse.DoorAmbush.IsActive, Is.False);
                Assert.That(GameSessionState.WarehouseDoorAmbushPlayed, Is.False);
                Assert.That(Vector3.Distance(warehouse.Player.GameObject.transform.position,
                    warehouse.WarehousePlan.HeroSpawn + Vector3.up * PlayerFactory.GroundedRootOffset), Is.LessThan(.15f));
                GameObject entrance = GameObject.Find("Warehouse Entrance Door");
                Assert.That(entrance, Is.Not.Null);
                Assert.That(entrance.GetComponent<BoxCollider>().enabled, Is.True);
                Assert.That(warehouse.NewWarehouseLayout(warehouse.WarehousePlan.Seed + 1), Is.False);
                Assert.That(warehouse.ReturnToWeapons(), Is.False);
                var arrivalProbe = warehouse.gameObject.AddComponent<WarehouseRefrigeratorPresentationProbe>();
                try
                {
                    yield return AtWarehouseFridgePresentation(arrivalProbe, () =>
                    {
                        Assert.That(warehouse.CameraFollow.Camera, Is.Not.Null);
                        CaptureCurrentCamera(warehouse.CameraFollow.Camera, SceneIds.StairwellInterior,
                            "labyrinth-04-warehouse-arrival");
                    });
                }
                finally { arrivalProbe.CancelPending(); Object.Destroy(arrivalProbe); }
                Debug.Log("Corrupted stairwell proof: warehouse arrival preserves the active session");
            }
            finally
            {
                // The successful transition destroys the source scene; never restore its old leases afterward.
                if (stairwell != null)
                {
                    motor.CancelInteractionPoseMove();
                    motor.SetInputEnabled(previousMotorInput);
                    stairwell.Player.Interactor.SetInputEnabled(previousInteractorInput);
                }
                Time.captureDeltaTime = previousDelta;
                GameInput.HandleApplicationFocus(previousFocus);
                input.TearDown();
            }
        }

        private static IEnumerator VerifyCatReliefInspection(StairwellInteriorRoot stairwell)
        {
            Camera camera = Camera.main;
            float previousAspect = camera.aspect;
            var input = new InputTestFixture();
            input.Setup();
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                foreach (float aspect in new[] { 16f / 9f, 4f / 3f, 21f / 9f })
                {
                    camera.aspect = aspect;
                    yield return VerifyCatReliefInspectionAtAspect(stairwell, input, keyboard);
                }
            }
            finally
            {
                NarrativeInteractionController.For(stairwell.Player.Interactor).RestoreImmediate();
                camera.aspect = previousAspect;
                input.TearDown();
            }
        }

        private static IEnumerator VerifyCatReliefInspectionAtAspect(StairwellInteriorRoot stairwell,
            InputTestFixture input, Keyboard keyboard)
        {
            Debug.Log("Corrupted stairwell proof: keyboard relief inspection, aspect=" + Camera.main.aspect);
            NarrativeInteraction inspection = stairwell.CatReliefInspection;
            Assert.That(inspection.Definition.IsSilent, Is.True);
            Assert.That(inspection.Definition.Pages, Is.Empty);
            Assert.That(inspection.SubjectRoot, Is.SameAs(stairwell.World.CatRelief));
            Assert.That(inspection.Staging.CameraTransition, Is.EqualTo(NarrativeCameraTransition.Cut));
            Assert.That(inspection.PlayerToHide, Is.SameAs(stairwell.Player.PresentationVisibility));
            var quests = new List<QuestLogEntry>(GameSessionState.Quests);
            var inventory = new List<InventoryItemStack>(GameSessionState.InventoryItems);
            var controller = NarrativeInteractionController.For(stairwell.Player.Interactor);
            Vector3 approach = stairwell.World.Root.TransformPoint(new Vector3(1.45f,
                stairwell.Layout.ApartmentElevation + PlayerFactory.GroundedRootOffset, -3.95f));
            yield return WalkStairwellWaypoint(stairwell, stairwell.World.Root.InverseTransformPoint(approach));
            stairwell.Player.GameObject.transform.rotation = inspection.Staging.Entry.RootRotation;
            stairwell.Player.Motor.SetInputEnabled(true);
            stairwell.Player.Interactor.SetInputEnabled(true);
            Physics.SyncTransforms();
            yield return WaitForCorruptedStairwell(() =>
                ReferenceEquals(stairwell.Player.Interactor.ActiveInteractable, inspection),
                "The stone head did not offer its actual E inspection prompt.");
            Vector3 start = stairwell.Player.GameObject.transform.position;
            Camera camera = Camera.main;
            Vector3 gameplayCameraPosition = camera.transform.position;
            float gameplayCameraFov = camera.fieldOfView;
            Pose closeUp = default;
            bool cutSeen = false;
            var rendererStates = new Dictionary<Renderer, bool>();
            foreach (Renderer renderer in stairwell.Player.Visual.Renderers)
                if (renderer != null) rendererStates.Add(renderer, renderer.enabled);
            bool shadowEnabled = stairwell.Player.ContactShadow.enabled;
            input.Press(keyboard.eKey, queueEventOnly: true);
            Assert.That(stairwell.Player.GameObject.transform.position, Is.EqualTo(start),
                "E must begin a guided approach, not teleport the hero into the inspection.");
            yield return null;
            input.Release(keyboard.eKey, queueEventOnly: true);
            Assert.That(controller.IsActive, Is.True, "Actual keyboard E must start inspection: " + controller.LastFailureReason);
            Assert.That(Vector3.Distance(stairwell.Player.GameObject.transform.position, start), Is.LessThan(.2f),
                "The first approach frame must walk, not jump directly into the dock.");
            yield return WaitForCorruptedStairwell(() =>
            {
                Assert.That(controller.IsActive, Is.True, "Inspection aborted after keyboard E: " +
                    controller.LastFailureReason + "; " + controller.CameraDirector.LastRejectedShotReason);
                if (controller.CameraDirector.IsFinished)
                {
                    Assert.That(cutSeen, Is.False, "The inspection unexpectedly released its close-up.");
                    Assert.That(stairwell.Player.PresentationVisibility.IsHidden, Is.False,
                        "The hero must stay visible throughout the physical approach.");
                    Assert.That(Vector3.Distance(camera.transform.position, gameplayCameraPosition), Is.LessThan(.001f),
                        "The camera must keep the gameplay position throughout the guided approach.");
                }
                else
                {
                    Assert.That(controller.CameraDirector.IsSettled, Is.True,
                        "The first inspection camera frame must already be the final close-up, without a blend.");
                    Assert.That(stairwell.Player.PresentationVisibility.IsHiddenBy(controller.CameraDirector), Is.True,
                        "The close-up camera must own the shared visibility lease in its first frame.");
                    Assert.That(stairwell.Player.ContactShadow.enabled, Is.False);
                    foreach (Renderer renderer in rendererStates.Keys)
                        Assert.That(renderer.enabled, Is.False, "The world hero must not obscure the object close-up.");
                    if (!cutSeen)
                    {
                        closeUp = new Pose(camera.transform.position, camera.transform.rotation);
                        cutSeen = true;
                    }
                    Assert.That(Vector3.Distance(camera.transform.position, closeUp.position), Is.LessThan(.001f));
                    Assert.That(Quaternion.Angle(camera.transform.rotation, closeUp.rotation), Is.LessThan(.05f));
                }
                return controller.Phase == NarrativeInteractionPhase.Reading;
            },
                "The stone head did not reach its silent inspection: " + controller.LastFailureReason);
            Assert.That(cutSeen, Is.True);
            Assert.That(stairwell.Player.GameObject.activeInHierarchy, Is.True);
            Assert.That(stairwell.Player.GameObject.GetComponent<CharacterController>().enabled, Is.True,
                "Inspection hides only presentation, not the physical character.");
            Assert.That(Vector3.Distance(camera.transform.position, inspection.Staging.FocusBounds.center), Is.LessThan(1.5f),
                "Inspection must physically bring the camera next to the stone head.");
            Assert.That(Vector3.Distance(camera.transform.position, gameplayCameraPosition), Is.GreaterThan(3f));
            Vector2 low = Vector2.one, high = Vector2.zero;
            Bounds head = inspection.Staging.FocusBounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 viewport = camera.WorldToViewportPoint(head.center + Vector3.Scale(head.extents,
                    new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f)));
                Assert.That(viewport.z, Is.GreaterThan(camera.nearClipPlane));
                Assert.That(viewport.x, Is.InRange(.025f, .975f));
                Assert.That(viewport.y, Is.InRange(.025f, .975f));
                low = Vector2.Min(low, viewport); high = Vector2.Max(high, viewport);
            }
            Assert.That(high.y - low.y, Is.GreaterThan(.4f), "The complete head must occupy a readable close-up.");
            if (GraphicsEffectsSettings.DepthOfFieldEnabled)
            {
                var focusVolume = GameObject.Find("Cinematic Depth Of Field").GetComponent<UnityEngine.Rendering.Volume>();
                Assert.That(focusVolume.weight, Is.EqualTo(1f), "Focus must cut with the camera, without a late blur transition.");
            }
            Assert.That(controller.CurrentPage.TextKey, Is.Null.Or.Empty);
            Assert.That(stairwell.InteractionPrompt.HasHeldPage, Is.False);
            Assert.That(inspection.IsPresenting, Is.True);
            Assert.That(GameTimeScaleRuntime.IsPaused, Is.False);
            CollectionAssert.AreEqual(quests, GameSessionState.Quests);
            CollectionAssert.AreEqual(inventory, GameSessionState.InventoryItems);
            yield return null;
            CaptureCurrentCamera(Camera.main, SceneIds.StairwellInterior,
                "corrupted-06-silent-head-inspection-" + Mathf.RoundToInt(camera.aspect * 100f),
                width: Mathf.RoundToInt(camera.aspect * 720f));
            controller.Cancel();
            Assert.That(controller.CameraDirector.IsFinished, Is.True, "Closing must cut back immediately.");
            Assert.That(Vector3.Distance(camera.transform.position, gameplayCameraPosition), Is.LessThan(.001f));
            Assert.That(camera.fieldOfView, Is.EqualTo(gameplayCameraFov).Within(.001f));
            Assert.That(stairwell.CameraFollow.FixedFocusActive, Is.True);
            Assert.That(CinematicDepthOfField.IsActive, Is.False);
            Assert.That(stairwell.Player.PresentationVisibility.IsHidden, Is.False);
            Assert.That(stairwell.Player.ContactShadow.enabled, Is.EqualTo(shadowEnabled));
            foreach (var state in rendererStates)
                Assert.That(state.Key.enabled, Is.EqualTo(state.Value), "Returning camera must restore each original renderer state.");
            yield return WaitForCorruptedStairwell(() => !controller.IsActive && !inspection.IsPresenting &&
                stairwell.Player.Motor.InputEnabled && stairwell.Player.Interactor.InputEnabled,
                "Cancelling the inspection did not restore hero, camera and input ownership.");
            for (int frame = 0; frame < 3; frame++) yield return null;
            Assert.That(stairwell.InteractionPrompt.HasHeldPage, Is.False);
            Assert.That(stairwell.CameraFollow.FixedPoseActive, Is.True);
            Assert.That(stairwell.CameraFollow.FixedFocusActive, Is.True);
            Assert.That(stairwell.FixedCamera.ActiveShotKind, Is.EqualTo(StairwellCameraShotKind.ApartmentLanding));
            Assert.That(Vector3.Distance(stairwell.CameraFollow.FixedBasePosition,
                stairwell.FixedCamera.ActiveShot.Position), Is.LessThan(.001f));
            Assert.That(stairwell.CameraFollow.FixedBaseFieldOfView,
                Is.EqualTo(stairwell.FixedCamera.ActiveShot.FieldOfView).Within(.001f));
            Assert.That(Vector3.Distance(stairwell.Player.GameObject.transform.position,
                inspection.Staging.Exit.RootPosition), Is.LessThan(.035f));
            CollectionAssert.AreEqual(quests, GameSessionState.Quests);
            CollectionAssert.AreEqual(inventory, GameSessionState.InventoryItems);
            // The same director can later serve an ordinary painting-style blend;
            // the opt-in cut must not survive the previous session's cleanup.
            float inspectionAspect = camera.aspect;
            try
            {
                camera.aspect = 4f / 3f;
                Assert.That(controller.CameraDirector.BeginObject(stairwell.CameraFollow, inspection.SubjectRoot,
                    head, stairwell.Player.GameObject.transform, inspection.Staging.CameraSideHint,
                    NarrativeCameraMode.ObjectCloseUp, inspection.Staging.CameraFront,
                    authoredFocusBoundsOnly: true), Is.True);
                Assert.That(controller.CameraDirector.IsSettled, Is.False);
                Assert.That(Vector3.Distance(camera.transform.position, gameplayCameraPosition), Is.LessThan(.001f));
                controller.CameraDirector.BeginExit();
                Assert.That(controller.CameraDirector.IsReturning, Is.True, "Ordinary object inspections still blend.");
            }
            finally { controller.CameraDirector.RestoreImmediate(); camera.aspect = inspectionAspect; }

            // A bad shot must release even a lease acquired before camera admission.
            Bounds blockedHead = new Bounds(head.center + Vector3.forward, head.size);
            Assert.That(controller.CameraDirector.BeginObject(stairwell.CameraFollow, inspection.SubjectRoot,
                blockedHead, stairwell.Player.GameObject.transform, inspection.Staging.CameraSideHint,
                NarrativeCameraMode.ObjectCloseUp, inspection.Staging.CameraFront, authoredFocusBoundsOnly: true,
                cameraTransition: NarrativeCameraTransition.Cut, playerToHide: inspection.PlayerToHide), Is.False,
                "The existing descent wall must reject an obstructed lens even when the hero is hidden.");
            Assert.That(stairwell.Player.PresentationVisibility.IsHidden, Is.False,
                "Rejected camera framing must not leave the hero hidden.");
            Assert.That(Vector3.Distance(camera.transform.position, gameplayCameraPosition), Is.LessThan(.001f));

            yield return WaitForCorruptedStairwell(() =>
                ReferenceEquals(stairwell.Player.Interactor.ActiveInteractable, inspection),
                "The final dock must remain in range for a repeat E inspection.");
            input.Press(keyboard.eKey, queueEventOnly: true);
            yield return null;
            input.Release(keyboard.eKey, queueEventOnly: true);
            yield return WaitForCorruptedStairwell(() => controller.Phase == NarrativeInteractionPhase.Reading,
                "Repeat keyboard E did not reopen the inspection from its final dock.");
            // Release the opening press and let the shared close action arm.
            yield return null;
            yield return null;
            input.Press(keyboard.eKey, queueEventOnly: true);
            yield return null;
            input.Release(keyboard.eKey, queueEventOnly: true);
            Assert.That(controller.CameraDirector.IsFinished, Is.True, "The second E must cut back to gameplay.");
            Assert.That(stairwell.Player.PresentationVisibility.IsHidden, Is.False);
            yield return WaitForCorruptedStairwell(() => !controller.IsActive &&
                stairwell.Player.Motor.InputEnabled && stairwell.Player.Interactor.InputEnabled,
                "Keyboard close did not restore gameplay input.");

            if (inspectionAspect > 1.7f && inspectionAspect < 1.8f)
            {
                for (int frame = 0; frame < 3; frame++) yield return null;
                input.Press(keyboard.eKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.eKey, queueEventOnly: true);
                yield return WaitForCorruptedStairwell(() => controller.Phase == NarrativeInteractionPhase.Reading,
                    "The inspection did not open for visibility cleanup verification.");
                try
                {
                    controller.enabled = false;
                    Assert.That(controller.IsActive, Is.False);
                    Assert.That(stairwell.Player.PresentationVisibility.IsHidden, Is.False);
                    Assert.That(stairwell.Player.ContactShadow.enabled, Is.EqualTo(shadowEnabled));
                    foreach (var state in rendererStates)
                        Assert.That(state.Key.enabled, Is.EqualTo(state.Value));
                    Assert.That(Vector3.Distance(camera.transform.position, gameplayCameraPosition), Is.LessThan(.001f));
                }
                finally { controller.enabled = true; }
            }
        }

        private static IEnumerator CaptureCorruptedGameplay(StairwellInteriorRoot stairwell,
            string name)
        {
            Camera camera = Camera.main;
            RenderTexture oldTarget = camera.targetTexture;
            float oldAspect = camera.aspect;
            try
            {
                foreach (int width in new[] { 1280, 960 })
                {
                    const int height = 720;
                    var target = new RenderTexture(width, height, 24);
                    target.Create();
                    camera.targetTexture = target;
                    camera.aspect = width / (float)height;
                    stairwell.CameraFollow.Snap();
                    try
                    {
                        // The bounded local/far light selection runs at 10 Hz.
                        // A fast batch frame can precede its response to an
                        // orbit/aspect change; capture the settled live view.
                        if (stairwell.FixedCamera.IsLabyrinthOrbit)
                            yield return new WaitForSecondsRealtime(.15f);
                        yield return ScarfRenderedFrame(camera, () =>
                        {
                            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
                            RenderTexture previous = RenderTexture.active;
                            try
                            {
                                RenderTexture.active = target;
                                pixels.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                                pixels.Apply();
                                string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.StairwellInterior);
                                Directory.CreateDirectory(folder);
                                string path = Path.Combine(folder, name + (width == 960 ? "-4x3.png" : "-16x9.png"));
                                File.WriteAllBytes(path, pixels.EncodeToPNG());
                                Debug.Log("Area capture wrote " + path);
                                Assert.That(IsBlank(pixels), Is.False);
                                AssertCorruptedHeroInFrame(stairwell, camera);
                            }
                            finally { RenderTexture.active = previous; Object.DestroyImmediate(pixels); }
                        });
                    }
                    finally
                    {
                        camera.targetTexture = oldTarget;
                        target.Release();
                        Object.DestroyImmediate(target);
                    }
                }
            }
            finally
            {
                camera.targetTexture = oldTarget;
                camera.aspect = oldAspect;
                stairwell.CameraFollow.Snap();
            }
        }

        private static void AssertCorruptedHeroInFrame(StairwellInteriorRoot stairwell, Camera camera)
        {
            Vector3 root = stairwell.Player.GameObject.transform.position;
            bool orbit = stairwell.FixedCamera.IsLabyrinthOrbit;
            // Ordinary close orbit keeps its actual upper-body focus while a
            // rail can shorten the lens distance. Authored fixed shots retain
            // their full feet-to-head contract.
            float focusHeight = orbit ? 1.5f : FixedCameraFocus.BodyAimHeight;
            Vector3[] points =
            {
                root + Vector3.up * (orbit ? focusHeight : FixedCameraFocus.BodyLowerHeight),
                root + Vector3.up * FixedCameraFocus.BodyUpperHeight,
                root + Vector3.up * focusHeight + Vector3.right * .32f,
                root + Vector3.up * focusHeight - Vector3.right * .32f
            };
            string[] names = { orbit ? "orbit focus" : "feet", "head", "right shoulder", "left shoulder" };
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 view = camera.WorldToViewportPoint(points[i]);
                string label = (orbit ? "Labyrinth orbit" : stairwell.FixedCamera.ActiveShotKind.ToString()) +
                    " " + names[i] + " viewport=" + view;
                Assert.That(view.z, Is.GreaterThan(0f), label + " is behind the camera");
                Assert.That(view.x, Is.InRange(.02f, .98f), label + " horizontal body framing");
                Assert.That(view.y, Is.InRange(.02f, .98f), label + " vertical body framing");
            }
            if (Physics.Linecast(camera.transform.position, root + Vector3.up * 1.2f,
                    out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                Assert.That(hit.transform == stairwell.Player.GameObject.transform ||
                    hit.transform.IsChildOf(stairwell.Player.GameObject.transform), Is.True,
                    "The gameplay camera cannot look through a wall or column at the hero: " + hit.collider.name);
        }

        private static IEnumerator WaitForCorruptedStairwell(Func<bool> ready, string failure)
        {
            float deadline = Time.realtimeSinceStartup + 12f;
            while (!ready() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(ready(), Is.True, failure);
        }

        private static IEnumerator WalkStairwellWaypoint(StairwellInteriorRoot stairwell, Vector3 local)
        {
            PlayerMotor motor = stairwell.Player.Motor;
            Vector3 target = stairwell.World.Root.TransformPoint(local);
            bool reached = false;
            for (int frame = 0; frame < 1200 && !reached; frame++)
            {
                reached = motor.MoveTowardsApproachWaypoint(target, .025f, 1f / 30f);
                Assert.That(motor.InteractionPoseMoveStalled, Is.False,
                    "The real stairwell capsule must reach " + local + "; stopped at " + motor.transform.position);
                yield return null;
            }
            Assert.That(reached, Is.True, "The stairwell route must reach " + local);
            motor.CancelInteractionPoseMove();
        }
    }
}
