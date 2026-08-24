using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Utils;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class CityBusRidePlayModeTests
    {
        [UnityTest]
        public IEnumerator Passenger_BoardsRidesAndExitsAtLaterStop()
        {
            float previousTimeScale = Time.timeScale;
            GameObject root = null;
            CityBusDirector director = null;
            InputTestFixture inputFixture = null;
            Mouse mouse = null;
            Gamepad gamepad = null;
            try
            {
                inputFixture = new InputTestFixture();
                inputFixture.Setup();
                mouse = InputSystem.AddDevice<Mouse>();
                gamepad = InputSystem.AddDevice<Gamepad>();
                Time.timeScale = 20f;
                root = new GameObject("City Bus Ride Integration Root");
                var walkableArea = new AlwaysWalkableArea();
                CreateGround(root.transform);

                GameObject cameraObject =
                    new GameObject("City Bus Ride Camera");
                cameraObject.transform.SetParent(root.transform, false);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;

                GameObject uiObject =
                    new GameObject("City Bus Ride Interaction UI");
                uiObject.transform.SetParent(root.transform, false);
                InteractionPromptView prompt =
                    uiObject.AddComponent<InteractionPromptView>();

                PlayerRuntime player = PlayerFactory.Create(
                    root.transform,
                    new Vector3(0f, PlayerFactory.GroundedRootOffset, 20f),
                    camera,
                    walkableArea,
                    prompt);
                PlayerCameraFollow cameraFollow =
                    cameraObject.AddComponent<PlayerCameraFollow>();
                cameraFollow.Initialize(
                    camera,
                    player.GameObject.transform,
                    interior: false);

                CityBusPlan route = CreateTwoStopRoute();
                Transform pool = new GameObject("Bus Model Pool").transform;
                pool.SetParent(root.transform, false);
                CityBusAssetRegistry registry =
                    CityBusResources.Instantiate(pool);
                Assert.That(
                    registry,
                    Is.Not.Null,
                    "The production bus prefab must be available.");
                CityBusPresentation presentation =
                    registry.GetComponent<CityBusPresentation>();
                if (presentation == null)
                {
                    presentation = registry.gameObject.AddComponent<
                        CityBusPresentation>();
                }

                presentation.Initialize(registry);
                presentation.gameObject.SetActive(false);

                GameObject actorObject = new GameObject("Bus Actor");
                actorObject.layer = CityBusCollision.LayerIndex;
                actorObject.transform.SetParent(root.transform, false);
                CityBusActor actor =
                    actorObject.AddComponent<CityBusActor>();
                actor.Initialize(
                    registry.LocalBounds,
                    registry.Dimensions);

                director = root.AddComponent<CityBusDirector>();
                director.Initialize(
                    route,
                    actor,
                    presentation,
                    player.GameObject.transform,
                    null,
                    pool,
                    () => 0f);
                director.enabled = false;

                CityBusSpawnAnchor anchor = route.SpawnAnchors[0];
                actor.PrepareSpawn(route, anchor, 0x42555354u);
                actor.BindPresentation(presentation);
                AdvanceActorUntil(
                    actor,
                    () => actor.ServiceOrdinal == 1 &&
                          actor.DoorsFullyOpen);

                var visual =
                    (Player3DCharacterPresentation)player.Visual;
                CharacterController characterController =
                    player.GameObject.GetComponent<CharacterController>();
                Vector3 neutralPelvisLocalPosition =
                    player.GameObject.transform.InverseTransformPoint(
                        visual.Registry.Anchors.Pelvis.position);
                Assert.That(
                    CityBusRidePlan.TryCreate(
                        actor,
                        walkableArea,
                        neutralPelvisLocalPosition,
                        characterController.radius,
                        CityBusPassengerDoor.Front,
                        out CityBusRidePlan frontBoardingPlan),
                    Is.True);
                Assert.That(
                    CityBusRidePlan.TryCreate(
                        actor,
                        walkableArea,
                        neutralPelvisLocalPosition,
                        characterController.radius,
                        CityBusPassengerDoor.Rear,
                        out CityBusRidePlan rearBoardingPlan),
                    Is.True);
                player.Motor.Teleport(
                    frontBoardingPlan.EntryPose.RootPosition);
                player.GameObject.transform.rotation =
                    frontBoardingPlan.EntryPose.RootRotation;
                cameraFollow.Snap();
                Physics.SyncTransforms();

                Transform originalParent =
                    player.GameObject.transform.parent;
                int originalSiblingIndex =
                    player.GameObject.transform.GetSiblingIndex();
                bool originalOrbitInput = cameraFollow.OrbitInputEnabled;
                bool originalCinematicMotion =
                    cameraFollow.CinematicMotionEnabled;
                CityBusRideController ride =
                    CityBusRideController.Create(
                        director,
                        player,
                        walkableArea,
                        camera,
                        cameraFollow);

                Assert.That(ride, Is.Not.Null);
                yield return null;
                yield return null;
                Assert.That(
                    player.Interactor.ActiveInteractable,
                    Is.SameAs(ride),
                    "The ordinary interaction query must discover the " +
                    "front-door prompt.");
                Assert.That(
                    prompt.PromptKey,
                    Is.EqualTo(CityBusRideController.BoardPromptKey));
                Assert.That(prompt.IsClickable, Is.True);
                Assert.That(
                    ride.CanInteract(player.Interactor),
                    Is.True,
                    "The open first stop must expose boarding.");

                player.Motor.Teleport(
                    rearBoardingPlan.EntryPose.RootPosition);
                player.GameObject.transform.rotation =
                    rearBoardingPlan.EntryPose.RootRotation;
                Physics.SyncTransforms();
                yield return null;
                yield return null;
                Assert.That(
                    player.Interactor.ActiveInteractable,
                    Is.SameAs(ride),
                    "The ordinary interaction query must discover the " +
                    "rear-door prompt.");
                Assert.That(
                    prompt.PromptKey,
                    Is.EqualTo(CityBusRideController.BoardPromptKey));
                Assert.That(prompt.IsClickable, Is.True);
                Assert.That(
                    ride.InteractionPosition,
                    Is.EqualTo(rearBoardingPlan.EntryPose.RootPosition)
                        .Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(prompt.TryInvokePrompt(), Is.True);
                Assert.That(
                    ride.ActivePlan.PassengerDoor,
                    Is.EqualTo(CityBusPassengerDoor.Rear));
                yield return WaitForRidingWithLevelHorizon(
                    ride,
                    camera);
                yield return null;

                Assert.That(actor.HasPassenger, Is.True);
                Assert.That(actor.HasServiceHold, Is.False);
                Assert.That(
                    player.GameObject.transform.parent,
                    Is.SameAs(originalParent),
                    "Riding must not move the player into the bus slot " +
                    "hierarchy.");
                Assert.That(player.Motor.enabled, Is.False);
                Assert.That(characterController.enabled, Is.False);
                Assert.That(player.ContactShadow.enabled, Is.False);
                Assert.That(cameraFollow.FixedPoseActive, Is.True);
                Assert.That(
                    ride.ActivePlan.SeatAnchor,
                    Is.SameAs(
                        registry.PassengerSeatAnchors[
                            CityBusRidePlan.PassengerSeatIndex]));
                float driverSide = Vector3.Dot(
                    registry.DriverSeatAnchor.position - actor.Position,
                    actor.transform.right);
                float passengerSide = Vector3.Dot(
                    ride.ActivePlan.SeatAnchor.position - actor.Position,
                    actor.transform.right);
                Assert.That(
                    driverSide * passengerSide,
                    Is.LessThan(0f),
                    "The fixed passenger seat must be on the side opposite " +
                    "the driver.");
                Vector3 windowOutward = ResolveWindowOutward(
                    ride.ActivePlan);
                Vector3 cameraForward = Vector3.ProjectOnPlane(
                    camera.transform.forward,
                    Vector3.up).normalized;
                Assert.That(
                    Vector3.Dot(cameraForward, windowOutward),
                    Is.GreaterThan(0.95f),
                    "The default passenger view must face the nearest " +
                    "side window.");
                Assert.That(
                    Mathf.Abs(Vector3.Dot(
                        camera.transform.right,
                        Vector3.up)),
                    Is.LessThan(0.01f),
                    "The passenger view must keep a level world horizon.");
                Assert.That(
                    Vector3.Dot(
                        camera.transform.position -
                            ride.ActivePlan.SeatAnchor.position,
                        windowOutward),
                    Is.LessThan(-0.4f),
                    "The passenger camera must stay on the aisle side of " +
                    "the selected seat.");

                Transform suspension = presentation.SuspensionVisual;
                Assert.That(suspension, Is.Not.Null);
                Quaternion neutralSuspensionRotation = suspension.rotation;
                suspension.rotation =
                    Quaternion.AngleAxis(
                        7f,
                        actor.transform.forward) *
                    Quaternion.AngleAxis(
                        5f,
                        actor.transform.right) *
                    neutralSuspensionRotation;
                yield return null;
                Assert.That(
                    Mathf.Abs(Vector3.Dot(
                        camera.transform.right,
                        Vector3.up)),
                    Is.LessThan(0.01f),
                    "Sprung body pitch/roll must not tilt the camera " +
                    "horizon.");
                suspension.rotation = neutralSuspensionRotation;
                yield return null;

                Vector3 beforeMouseYaw = camera.transform.forward;
                inputFixture.Press(
                    mouse.rightButton,
                    queueEventOnly: true);
                inputFixture.Set(
                    mouse.delta,
                    new Vector2(42f, 0f),
                    queueEventOnly: true);
                yield return null;
                yield return null;
                Assert.That(
                    SignedPlanarYaw(
                        beforeMouseYaw,
                        camera.transform.forward),
                    Is.GreaterThan(2f),
                    "Positive mouse X must turn the view right.");
                Assert.That(
                    Mathf.Abs(
                        camera.transform.forward.y -
                        beforeMouseYaw.y),
                    Is.LessThan(0.002f),
                    "Horizontal mouse input must not change pitch.");

                Vector3 beforeMousePitch = camera.transform.forward;
                inputFixture.Set(
                    mouse.delta,
                    new Vector2(0f, 18f),
                    queueEventOnly: true);
                yield return null;
                yield return null;
                Assert.That(
                    Mathf.Abs(SignedPlanarYaw(
                        beforeMousePitch,
                        camera.transform.forward)),
                    Is.LessThan(0.2f),
                    "Vertical mouse input must not change yaw.");
                Assert.That(
                    camera.transform.forward.y,
                    Is.GreaterThan(beforeMousePitch.y + 0.01f),
                    "Positive mouse Y must raise the view.");
                Assert.That(
                    Mathf.Abs(Vector3.Dot(
                        camera.transform.right,
                        Vector3.up)),
                    Is.LessThan(0.01f));
                inputFixture.Release(
                    mouse.rightButton,
                    queueEventOnly: true);
                yield return null;

                Vector3 beforeGamepadYaw = camera.transform.forward;
                inputFixture.Set(
                    gamepad.rightStick,
                    new Vector2(0.9f, 0f),
                    queueEventOnly: true);
                yield return new WaitForSecondsRealtime(0.06f);
                inputFixture.Set(
                    gamepad.rightStick,
                    Vector2.zero,
                    queueEventOnly: true);
                yield return null;
                Assert.That(
                    SignedPlanarYaw(
                        beforeGamepadYaw,
                        camera.transform.forward),
                    Is.GreaterThan(1f),
                    "Positive right-stick X must turn the view right.");
                Assert.That(
                    Mathf.Abs(
                        camera.transform.forward.y -
                        beforeGamepadYaw.y),
                    Is.LessThan(0.002f),
                    "Horizontal stick input must not change pitch.");

                Vector3 beforeGamepadPitch = camera.transform.forward;
                inputFixture.Set(
                    gamepad.rightStick,
                    new Vector2(0f, 0.7f),
                    queueEventOnly: true);
                yield return new WaitForSecondsRealtime(0.06f);
                inputFixture.Set(
                    gamepad.rightStick,
                    Vector2.zero,
                    queueEventOnly: true);
                yield return null;
                Assert.That(
                    Mathf.Abs(SignedPlanarYaw(
                        beforeGamepadPitch,
                        camera.transform.forward)),
                    Is.LessThan(0.2f),
                    "Vertical stick input must not change yaw.");
                Assert.That(
                    camera.transform.forward.y,
                    Is.GreaterThan(beforeGamepadPitch.y + 0.005f),
                    "Positive right-stick Y must raise the view.");
                Assert.That(
                    ride.BeginAlighting(),
                    Is.False,
                    "The boarding service is not a valid exit stop.");

                Vector3 boardedActorPosition = actor.Position;
                Vector3 boardedPlayerPosition =
                    player.GameObject.transform.position;
                Vector3 boardedPlayerActorLocalPosition =
                    actor.transform.InverseTransformPoint(
                        player.GameObject.transform.position);
                bool reachedLaterStop = false;
                for (int step = 0; step < 1600; step++)
                {
                    director.Advance(0.10f);
                    Assert.That(
                        director.ActiveCount,
                        Is.EqualTo(1),
                        "A passenger bus must not be recycled.");
                    Assert.That(
                        actor.MotionState,
                        Is.Not.EqualTo(CityBusMotionState.Yielding),
                        "The attached player must not become a bus " +
                        "obstacle.");
                    if (actor.ServiceOrdinal > ride.BoardedServiceOrdinal &&
                        actor.DoorsFullyOpen)
                    {
                        reachedLaterStop = true;
                        break;
                    }
                }

                Assert.That(
                    reachedLaterStop,
                    Is.True,
                    "The deterministic route must reach its second stop.");
                yield return null;
                Assert.That(
                    Vector3.Distance(actor.Position, boardedActorPosition),
                    Is.GreaterThan(1f));
                Assert.That(
                    Vector3.Distance(
                        player.GameObject.transform.position,
                        boardedPlayerPosition),
                    Is.GreaterThan(1f));
                Assert.That(
                    Vector3.Distance(
                        actor.transform.InverseTransformPoint(
                            player.GameObject.transform.position),
                        boardedPlayerActorLocalPosition),
                    Is.LessThan(0.001f),
                    "The logically attached player must keep a stable " +
                    "actor-local seat pose.");
                Assert.That(actor.HasPassenger, Is.True);

                Assert.That(
                    ride.CanInteract(player.Interactor),
                    Is.True,
                    "A later open stop must expose alighting.");
                ride.Interact(player.Interactor);
                Assert.That(
                    ride.State,
                    Is.EqualTo(CityBusRideState.Alighting));
                Assert.That(
                    ride.ActivePlan.PassengerDoor,
                    Is.EqualTo(CityBusPassengerDoor.Rear));
                PlayerAnimatedInteractionPose expectedExitPose =
                    ride.ActivePlan.ExitPose;
                Assert.That(actor.HasServiceHold, Is.True);
                yield return WaitForState(
                    ride,
                    CityBusRideState.Outside);

                Assert.That(actor.HasPassenger, Is.False);
                Assert.That(actor.HasServiceHold, Is.False);
                Assert.That(
                    player.GameObject.transform.parent,
                    Is.SameAs(originalParent));
                Assert.That(
                    player.GameObject.transform.GetSiblingIndex(),
                    Is.EqualTo(originalSiblingIndex));
                Assert.That(player.Motor.enabled, Is.True);
                Assert.That(player.Motor.InputEnabled, Is.True);
                Assert.That(characterController.enabled, Is.True);
                Assert.That(player.ContactShadow.enabled, Is.True);
                Assert.That(cameraFollow.FixedPoseActive, Is.False);
                Assert.That(
                    cameraFollow.OrbitInputEnabled,
                    Is.EqualTo(originalOrbitInput));
                Assert.That(
                    cameraFollow.CinematicMotionEnabled,
                    Is.EqualTo(originalCinematicMotion));
                Assert.That(
                    Vector3.Distance(
                        player.GameObject.transform.position,
                        expectedExitPose.RootPosition),
                    Is.LessThan(0.01f));
                Assert.That(
                    Quaternion.Angle(
                        player.GameObject.transform.rotation,
                        expectedExitPose.RootRotation),
                    Is.LessThan(0.1f));

                Assert.That(
                    CityBusRidePlan.TryCreate(
                        actor,
                        walkableArea,
                        neutralPelvisLocalPosition,
                        characterController.radius,
                        CityBusPassengerDoor.Rear,
                        out CityBusRidePlan lifecyclePlan),
                    Is.True);
                player.Motor.Teleport(
                    lifecyclePlan.EntryPose.RootPosition);
                player.GameObject.transform.rotation =
                    lifecyclePlan.EntryPose.RootRotation;
                Physics.SyncTransforms();
                Assert.That(ride.BeginBoarding(), Is.True);
                yield return WaitForState(
                    ride,
                    CityBusRideState.Riding);

                var hierarchyWarnings = new List<string>();
                Application.LogCallback captureHierarchyWarning =
                    (condition, stackTrace, type) =>
                    {
                        if (condition.Contains("Cannot set the parent") ||
                            condition.Contains(
                                "Cannot change the sibling position"))
                        {
                            hierarchyWarnings.Add(condition);
                        }
                    };
                Application.logMessageReceived += captureHierarchyWarning;
                try
                {
                    actorObject.SetActive(false);
                }
                finally
                {
                    Application.logMessageReceived -=
                        captureHierarchyWarning;
                }

                Assert.That(
                    hierarchyWarnings,
                    Is.Empty,
                    "Bus lifecycle cleanup must not mutate the Transform " +
                    "hierarchy during slot deactivation.");
                Assert.That(ride.State, Is.EqualTo(CityBusRideState.Outside));
                Assert.That(actor.HasPassenger, Is.False);
                Assert.That(actor.HasServiceHold, Is.False);
                Assert.That(
                    player.GameObject.transform.parent,
                    Is.SameAs(originalParent));
                Assert.That(
                    player.GameObject.transform.GetSiblingIndex(),
                    Is.EqualTo(originalSiblingIndex));
                Assert.That(
                    player.GameObject.activeInHierarchy,
                    Is.True,
                    "Deactivating the bus slot must not deactivate its " +
                    "passenger.");
                Assert.That(player.Motor.enabled, Is.True);
                Assert.That(characterController.enabled, Is.True);
                Assert.That(player.ContactShadow.enabled, Is.True);
                Assert.That(cameraFollow.FixedPoseActive, Is.False);
            }
            finally
            {
                Time.timeScale = previousTimeScale;
                director?.Shutdown();
                if (root != null)
                {
                    Object.DestroyImmediate(root);
                }

                if (gamepad != null && gamepad.added)
                {
                    InputSystem.RemoveDevice(gamepad);
                }

                if (mouse != null && mouse.added)
                {
                    InputSystem.RemoveDevice(mouse);
                }

                inputFixture?.TearDown();
            }
        }

        private static Vector3 ResolveWindowOutward(
            CityBusRidePlan plan)
        {
            Vector3 up = Vector3.up;
            Vector3 forward = plan.ActorRoot.forward;
            forward = Vector3.ProjectOnPlane(forward, up).normalized;
            Vector3 right = Vector3.Cross(up, forward).normalized;
            float side = Vector3.Dot(
                plan.SeatAnchor.position - plan.ActorRoot.position,
                right);
            return side < 0f ? -right : right;
        }

        private static float SignedPlanarYaw(
            Vector3 from,
            Vector3 to)
        {
            Vector3 fromPlanar = Vector3.ProjectOnPlane(
                from,
                Vector3.up).normalized;
            Vector3 toPlanar = Vector3.ProjectOnPlane(
                to,
                Vector3.up).normalized;
            return Vector3.SignedAngle(
                fromPlanar,
                toPlanar,
                Vector3.up);
        }

        [UnityTest]
        public IEnumerator ProductionCityRoute_AllStopsExposeBothDoorPrompts()
        {
            const int productionSeed = 20260727;
            float previousTimeScale = Time.timeScale;
            GameObject root = null;
            CityBusDirector director = null;
            try
            {
                Time.timeScale = 0f;
                CityGenerationSettings settings =
                    CityGenerationSettings.Default;
                CityLayout layout = CityLayoutGenerator.Generate(
                    CityBlueprintCatalog.Default,
                    settings,
                    productionSeed);
                CityStreetSurfacePlan streetSurfacePlan =
                    CityStreetSurfacePlanner.Create(layout);
                var walkableArea = RoadWalkableArea.FromLayout(layout);
                CityBusPlan route = CityBusPlanner.Create(layout);
                Assert.That(
                    route.Stops,
                    Has.Count.GreaterThan(10),
                    "The grand loop serves every district, gate and " +
                    "spread stop, not just the five semantic targets.");

                root = new GameObject(
                    "Production City Bus Prompt Diagnostic Root");
                GameObject cameraObject = new GameObject(
                    "Production City Bus Prompt Camera");
                cameraObject.transform.SetParent(root.transform, false);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                GameObject uiObject = new GameObject(
                    "Production City Bus Prompt UI");
                uiObject.transform.SetParent(root.transform, false);
                InteractionPromptView prompt =
                    uiObject.AddComponent<InteractionPromptView>();

                Vector3 waitPosition = route.Stops[0].ShelterPosition;
                waitPosition.y = CityStreetSurfacePlanner.SidewalkTop +
                                 PlayerFactory.GroundedRootOffset;
                waitPosition = walkableArea.ClosestPoint(
                    waitPosition,
                    0.32f);
                PlayerRuntime player = PlayerFactory.Create(
                    root.transform,
                    waitPosition,
                    camera,
                    walkableArea,
                    prompt);
                PlayerCameraFollow cameraFollow =
                    cameraObject.AddComponent<PlayerCameraFollow>();
                cameraFollow.Initialize(
                    camera,
                    player.GameObject.transform,
                    interior: false);

                director = CityBusFactory.Create(
                    root.transform,
                    route,
                    player.GameObject.transform,
                    null,
                    () => 0f);
                CityBusRideController ride =
                    CityBusRideController.Create(
                        director,
                        player,
                        walkableArea,
                        camera,
                        cameraFollow,
                        streetSurfacePlan);
                Assert.That(ride, Is.Not.Null);
                CityBusActor actor = director.Actor;
                var presentationField = typeof(CityBusDirector).GetField(
                    "presentation",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                Assert.That(presentationField, Is.Not.Null);
                var presentation = presentationField.GetValue(director) as
                    CityBusPresentation;
                Assert.That(presentation, Is.Not.Null);
                Assert.That(route.SpawnAnchors, Is.Not.Empty);
                actor.PrepareSpawn(
                    route,
                    route.SpawnAnchors[0],
                    0x42555354u);
                actor.BindPresentation(presentation);
                Assert.That(director.ActiveCount, Is.EqualTo(1));
                var visual =
                    (Player3DCharacterPresentation)player.Visual;
                CharacterController characterController =
                    player.GameObject.GetComponent<CharacterController>();
                Vector3 neutralPelvisLocalPosition =
                    player.GameObject.transform.InverseTransformPoint(
                        visual.Registry.Anchors.Pelvis.position);
                var visitedStops = new HashSet<string>();
                CityBusStopDescriptor waitingStop = null;
                Vector3 waitingDock = default;
                bool roadApronBoardingVerified = false;

                for (int guard = 0;
                     guard < 30000 &&
                     visitedStops.Count < route.Stops.Count;
                     guard++)
                {
                    if (!actor.DoorsFullyOpen ||
                        actor.CurrentStop == null ||
                        visitedStops.Contains(actor.CurrentStop.Id))
                    {
                        actor.Advance(
                            0.20f,
                            CityBusObstacleState.Clear,
                            0f);
                        continue;
                    }

                    CityBusStopDescriptor stop = actor.CurrentStop;
                    visitedStops.Add(stop.Id);
                    foreach (CityBusPassengerDoor passengerDoor in
                             new[]
                             {
                                 CityBusPassengerDoor.Front,
                                 CityBusPassengerDoor.Rear
                             })
                    {
                        bool created = CityBusRidePlan.TryCreate(
                            actor,
                            walkableArea,
                            neutralPelvisLocalPosition,
                            characterController.radius,
                            passengerDoor,
                            streetSurfacePlan,
                            out CityBusRidePlan boardingPlan);
                        Assert.That(
                            created,
                            Is.True,
                            DescribeDoorPlanGate(
                                actor,
                                walkableArea,
                                characterController.radius,
                                passengerDoor,
                                stop));
                        Assert.That(
                            boardingPlan.DoorAnchor,
                            Is.SameAs(
                                passengerDoor ==
                                CityBusPassengerDoor.Front
                                    ? actor.Presentation.Registry
                                        .FrontDoorEntryAnchor
                                    : actor.Presentation.Registry
                                        .RearDoorEntryAnchor));
                        Assert.That(
                            walkableArea.Contains(
                                boardingPlan.EntryPose.RootPosition,
                                characterController.radius),
                            Is.True);
                        Assert.That(
                            walkableArea.Contains(
                                boardingPlan.ExitPose.RootPosition,
                                characterController.radius),
                            Is.True);
                        if (waitingStop == null &&
                            passengerDoor == CityBusPassengerDoor.Front)
                        {
                            waitingStop = stop;
                            waitingDock =
                                boardingPlan.EntryPose.RootPosition;
                        }

                        if (!roadApronBoardingVerified &&
                            passengerDoor == CityBusPassengerDoor.Front &&
                            !ContainsPlanar(
                                streetSurfacePlan.Sidewalks,
                                boardingPlan.EntryPose.RootPosition))
                        {
                            // A road/apron dock's own grounded height IS
                            // the carriageway height at that stop — the
                            // grand loop serves graded boundary streets,
                            // where the old constant world RoadTop sat
                            // metres below the dock and silently killed
                            // the prompt.
                            Vector3 roadHeightApproach =
                                boardingPlan.EntryPose.RootPosition;
                            player.Motor.Teleport(roadHeightApproach);
                            player.GameObject.transform.rotation =
                                boardingPlan.EntryPose.RootRotation;
                            Physics.SyncTransforms();
                            yield return null;
                            yield return null;
                            Assert.That(
                                player.Interactor.ActiveInteractable,
                                Is.SameAs(ride),
                                "The normal curb step must not suppress " +
                                "the bus prompt.");
                            Assert.That(prompt.IsClickable, Is.True);
                            Assert.That(prompt.TryInvokePrompt(), Is.True);
                            Assert.That(
                                ride.State,
                                Is.EqualTo(CityBusRideState.Boarding));
                            var activeInteraction =
                                player.GameObject.GetComponent<
                                    PlayerAnimatedInteractionController>();
                            Assert.That(activeInteraction, Is.Not.Null);
                            yield return null;
                            Assert.That(
                                activeInteraction.Phase,
                                Is.EqualTo(
                                    PlayerAnimatedInteractionPhase.Entering),
                                $"{stop.Id}/{passengerDoor}: boarding " +
                                "must leave positioning after the player " +
                                "reaches the physical road/apron dock.");
                            Assert.That(
                                activeInteraction.CancelActiveInteraction(),
                                Is.True);
                            Assert.That(
                                ride.State,
                                Is.EqualTo(CityBusRideState.Outside));
                            roadApronBoardingVerified = true;
                        }

                        player.Motor.Teleport(
                            boardingPlan.EntryPose.RootPosition);
                        player.GameObject.transform.rotation =
                            boardingPlan.EntryPose.RootRotation;
                        Physics.SyncTransforms();
                        yield return null;
                        yield return null;

                        string triggerName = passengerDoor ==
                            CityBusPassengerDoor.Front
                                ? "Front Door Interaction"
                                : "Rear Door Interaction";
                        Transform trigger = actor.transform.Find(
                            triggerName);
                        Assert.That(trigger, Is.Not.Null);
                        Assert.That(
                            Vector3.Distance(
                                trigger.position,
                                boardingPlan.EntryPose.RootPosition +
                                Vector3.up * 0.80f),
                            Is.LessThan(0.001f),
                            $"{stop.Id}/{passengerDoor}: trigger is not " +
                            "positioned on its production dock.");
                        Assert.That(
                            ride.CanInteract(player.Interactor),
                            Is.True,
                            DescribeInteractionGates(
                                actor,
                                player,
                                ride,
                                boardingPlan,
                                stop,
                                passengerDoor));
                        Assert.That(
                            player.Interactor.ActiveInteractable,
                            Is.SameAs(ride),
                            $"{stop.Id}/{passengerDoor}: the production " +
                            "physics query did not discover the bus.");
                        Assert.That(
                            prompt.PromptKey,
                            Is.EqualTo(
                                CityBusRideController.BoardPromptKey));
                        Assert.That(prompt.IsClickable, Is.True);
                    }

                    actor.Advance(
                        CityBusActor.DwellDuration +
                        CityBusActor.DoorTransitionDuration + 0.1f,
                        CityBusObstacleState.Clear,
                        0f);
                }

                Assert.That(
                    visitedStops,
                    Has.Count.EqualTo(route.Stops.Count),
                    "The diagnostic must visit every production stop.");
                Assert.That(waitingStop, Is.Not.Null);
                Assert.That(
                    roadApronBoardingVerified,
                    Is.True,
                    "The production route must exercise one grounded " +
                    "road/apron boarding dock.");

                // Re-run the same production route through the real director
                // with a passenger waiting at the authored dock. This is the
                // organic gameplay path: the director, rather than the test,
                // owns obstacle resolution while the bus approaches.
                director.Shutdown();
                director = CityBusFactory.Create(
                    root.transform,
                    route,
                    player.GameObject.transform,
                    null,
                    () => 0f);
                actor = director.Actor;
                presentationField = typeof(CityBusDirector).GetField(
                    "presentation",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                presentation = presentationField.GetValue(director) as
                    CityBusPresentation;
                Assert.That(presentation, Is.Not.Null);
                actor.PrepareSpawn(
                    route,
                    route.SpawnAnchors[0],
                    0x42555354u);
                actor.BindPresentation(presentation);
                player.Motor.Teleport(waitingDock);
                Physics.SyncTransforms();

                bool openedAtWaitingStop = false;
                int stoppedYieldSamples = 0;
                float closestStopDistance = float.PositiveInfinity;
                for (int step = 0; step < 30000; step++)
                {
                    director.Advance(0.10f);
                    closestStopDistance = Mathf.Min(
                        closestStopDistance,
                        Vector3.Distance(
                            actor.Position,
                            waitingStop.Position));
                    if (actor.CurrentStop != null &&
                        actor.CurrentStop.Id == waitingStop.Id &&
                        actor.DoorsFullyOpen)
                    {
                        openedAtWaitingStop = true;
                        break;
                    }

                    if (actor.IsYielding && actor.Speed <= 0.001f)
                    {
                        stoppedYieldSamples++;
                        if (stoppedYieldSamples >= 100)
                        {
                            break;
                        }
                    }
                    else
                    {
                        stoppedYieldSamples = 0;
                    }
                }

                float obstacleLateralLimit =
                    actor.Presentation.Registry.Dimensions.Width * 0.5f +
                    characterController.radius +
                    CityBusActor.ObstacleLateralPadding;
                float authoredDockDepth =
                    actor.Presentation.Registry.Dimensions.Width * 0.5f +
                    characterController.radius +
                    CityBusRidePlan.DoorBodyClearance;
                Assert.That(
                    openedAtWaitingStop,
                    Is.True,
                    $"A player waiting at {waitingStop.Id} blocks service: " +
                    $"motion={actor.MotionState}, yielding={actor.IsYielding}, " +
                    $"speed={actor.Speed}, current_stop=" +
                    $"{actor.CurrentStop?.Id ?? "none"}, " +
                    $"closest_stop_distance={closestStopDistance}, " +
                    $"dock_depth={authoredDockDepth}, " +
                    $"obstacle_lateral_limit={obstacleLateralLimit}.");
            }
            finally
            {
                Time.timeScale = previousTimeScale;
                director?.Shutdown();
                if (root != null)
                {
                    Object.DestroyImmediate(root);
                }
            }
        }

        [Test]
        public void ProductionCityDoorDocks_MatchPhysicalSurfaceHeight()
        {
            const int productionSeed = 20260727;
            GameObject root = null;
            CityBusDirector director = null;
            try
            {
                CityGenerationSettings settings =
                    CityGenerationSettings.Default;
                CityLayout layout = CityLayoutGenerator.Generate(
                    CityBlueprintCatalog.Default,
                    settings,
                    productionSeed);
                CityStreetSurfacePlan surfaces =
                    CityStreetSurfacePlanner.Create(layout);
                root = new GameObject(
                    "Production Bus Dock Ground Diagnostic Root");
                CityWorldResult world = CityWorldBuilder.Build(
                    root.transform,
                    layout,
                    settings);
                CityBusPlan route = CityBusPlanner.Create(
                    layout,
                    world.DecorationPlan);
                Transform player = new GameObject(
                    "Bus Dock Ground Diagnostic Player").transform;
                player.SetParent(root.transform, false);
                player.position = new Vector3(
                    layout.WorldXZBounds.xMin - 20f,
                    PlayerFactory.GroundedRootOffset,
                    layout.WorldXZBounds.yMin - 20f);
                director = CityBusFactory.Create(
                    root.transform,
                    route,
                    player,
                    null,
                    () => 0f);
                CityBusActor actor = director.Actor;
                var presentationField = typeof(CityBusDirector).GetField(
                    "presentation",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                Assert.That(presentationField, Is.Not.Null);
                var presentation = presentationField.GetValue(director) as
                    CityBusPresentation;
                Assert.That(presentation, Is.Not.Null);
                actor.PrepareSpawn(
                    route,
                    route.SpawnAnchors[0],
                    0x42555354u);
                actor.BindPresentation(presentation);
                Physics.SyncTransforms();

                var visitedStops = new HashSet<string>();
                var mismatches = new List<string>();
                for (int guard = 0;
                     guard < 30000 &&
                     visitedStops.Count < route.Stops.Count;
                     guard++)
                {
                    if (!actor.DoorsFullyOpen ||
                        actor.CurrentStop == null ||
                        visitedStops.Contains(actor.CurrentStop.Id))
                    {
                        actor.Advance(
                            0.20f,
                            CityBusObstacleState.Clear,
                            0f);
                        continue;
                    }

                    CityBusStopDescriptor stop = actor.CurrentStop;
                    visitedStops.Add(stop.Id);
                    foreach (CityBusPassengerDoor passengerDoor in
                             new[]
                             {
                                 CityBusPassengerDoor.Front,
                                 CityBusPassengerDoor.Rear
                             })
                    {
                        Assert.That(
                            CityBusRidePlan.TryCreate(
                                actor,
                                world.WalkableArea,
                                new Vector3(0f, 0.92f, 0f),
                                0.32f,
                                passengerDoor,
                                surfaces,
                                out CityBusRidePlan plan),
                            Is.True,
                            DescribeDoorPlanGate(
                                actor,
                                world.WalkableArea,
                                0.32f,
                                passengerDoor,
                                stop));
                        InspectGroundedPose(
                            $"{stop.Id}/{passengerDoor}/entry",
                            plan.EntryPose.RootPosition,
                            surfaces,
                            mismatches);
                        InspectGroundedPose(
                            $"{stop.Id}/{passengerDoor}/exit",
                            plan.ExitPose.RootPosition,
                            surfaces,
                            mismatches);
                    }

                    actor.Advance(
                        CityBusActor.DwellDuration +
                        CityBusActor.DoorTransitionDuration + 0.1f,
                        CityBusObstacleState.Clear,
                        0f);
                }

                Assert.That(
                    visitedStops,
                    Has.Count.EqualTo(route.Stops.Count));
                AssertRepresentativeSurfaceHeight(
                    "road",
                    surfaces.StreetSurfaces[0].center,
                    surfaces.StreetGeometry,
                    mismatches);
                AssertRepresentativeSurfaceHeight(
                    "sidewalk",
                    surfaces.Sidewalks[0].center,
                    surfaces.SidewalkGeometry,
                    mismatches);
                Assert.That(
                    mismatches,
                    Is.Empty,
                    "Grounded bus poses must use the physical street " +
                    "surface height:\n" + string.Join("\n", mismatches));
            }
            finally
            {
                director?.Shutdown();
                if (root != null)
                {
                    Object.DestroyImmediate(root);
                }
            }
        }

        private static void InspectGroundedPose(
            string label,
            Vector3 rootPosition,
            CityStreetSurfacePlan surfaces,
            ICollection<string> mismatches)
        {
            bool sidewalk = ContainsPlanar(
                surfaces.Sidewalks,
                rootPosition);
            bool road = ContainsPlanar(
                surfaces.StreetSurfaces,
                rootPosition);
            if (!TryResolvePhysicalGroundTop(
                    rootPosition,
                    out float physicalTop,
                    out string colliderName))
            {
                mismatches.Add($"{label}: no physical ground at " +
                               $"{rootPosition}.");
                return;
            }

            float plannedTop = rootPosition.y -
                               PlayerFactory.GroundedRootOffset;
            string classification = sidewalk
                ? "sidewalk"
                : road
                    ? "road/apron"
                    : "other";
            TestContext.WriteLine(
                $"{label}: xz=({rootPosition.x:F3}," +
                $"{rootPosition.z:F3}), class={classification}, " +
                $"physical_top={physicalTop:F3}, " +
                $"planned_top={plannedTop:F3}, " +
                $"collider={colliderName}");
            if (Mathf.Abs(plannedTop - physicalTop) > 0.005f)
            {
                mismatches.Add(
                    $"{label}: planned top {plannedTop:F3}, physical " +
                    $"top {physicalTop:F3}, class={classification}, " +
                    $"collider={colliderName}.");
            }
        }

        private static void AssertRepresentativeSurfaceHeight(
            string label,
            Vector3 position,
            IReadOnlyList<RuntimeOrientedBox> geometry,
            ICollection<string> mismatches)
        {
            if (!TryResolvePlannedTop(
                    position,
                    geometry,
                    out float expectedTop))
            {
                mismatches.Add($"{label}: representative surface has no " +
                               "planned geometry.");
                return;
            }

            if (!TryResolvePhysicalGroundTop(
                    position,
                    out float physicalTop,
                    out string colliderName))
            {
                mismatches.Add($"{label}: representative surface has no " +
                               "physical ground.");
                return;
            }

            TestContext.WriteLine(
                $"representative/{label}: xz=({position.x:F3}," +
                $"{position.z:F3}), planned_top={expectedTop:F3}, " +
                $"physical_top={physicalTop:F3}, " +
                $"collider={colliderName}");
            if (Mathf.Abs(physicalTop - expectedTop) > 0.005f)
            {
                mismatches.Add(
                    $"{label}: expected physical top {expectedTop:F3}, " +
                    $"got {physicalTop:F3} ({colliderName}).");
            }
        }

        private static bool TryResolvePhysicalGroundTop(
            Vector3 position,
            out float top,
            out string colliderName)
        {
            Vector3 origin = new Vector3(
                position.x,
                position.y + 8f,
                position.z);
            if (Physics.Raycast(
                    origin,
                    Vector3.down,
                    out RaycastHit hit,
                    16f,
                    CityBusCollision.NonBusMask,
                    QueryTriggerInteraction.Ignore))
            {
                top = hit.point.y;
                colliderName = hit.collider.name;
                return true;
            }

            top = 0f;
            colliderName = string.Empty;
            return false;
        }

        private static bool TryResolvePlannedTop(
            Vector3 position,
            IReadOnlyList<RuntimeOrientedBox> geometry,
            out float top)
        {
            top = float.NegativeInfinity;
            for (int index = 0; index < geometry.Count; index++)
            {
                RuntimeOrientedBox box = geometry[index];
                Vector3 normal = box.Rotation * Vector3.up;
                if (Mathf.Abs(normal.y) <= 0.0001f)
                {
                    continue;
                }

                Vector3 planePoint = box.Center +
                    normal * (box.Size.y * 0.5f);
                float candidate = planePoint.y -
                    ((normal.x * (position.x - planePoint.x) +
                      normal.z * (position.z - planePoint.z)) /
                     normal.y);
                Vector3 local = Quaternion.Inverse(box.Rotation) *
                    (new Vector3(position.x, candidate, position.z) -
                     box.Center);
                if (Mathf.Abs(local.x) > box.Size.x * 0.5f + 0.001f ||
                    Mathf.Abs(local.z) > box.Size.z * 0.5f + 0.001f)
                {
                    continue;
                }

                top = Mathf.Max(top, candidate);
            }

            return !float.IsNegativeInfinity(top);
        }

        private static bool ContainsPlanar(
            IReadOnlyList<Bounds> bounds,
            Vector3 position)
        {
            for (int index = 0; index < bounds.Count; index++)
            {
                Bounds candidate = bounds[index];
                if (position.x >= candidate.min.x - 0.0001f &&
                    position.x <= candidate.max.x + 0.0001f &&
                    position.z >= candidate.min.z - 0.0001f &&
                    position.z <= candidate.max.z + 0.0001f)
                {
                    return true;
                }
            }

            return false;
        }

        private static string DescribeDoorPlanGate(
            CityBusActor actor,
            RoadWalkableArea walkableArea,
            float radius,
            CityBusPassengerDoor passengerDoor,
            CityBusStopDescriptor stop)
        {
            CityBusAssetRegistry registry = actor.Presentation.Registry;
            Transform door = passengerDoor == CityBusPassengerDoor.Front
                ? registry.FrontDoorEntryAnchor
                : registry.RearDoorEntryAnchor;
            Vector3 forward = Vector3.ProjectOnPlane(
                actor.transform.forward,
                actor.transform.up).normalized;
            Vector3 doorOffset = Vector3.ProjectOnPlane(
                door.position - actor.transform.position,
                actor.transform.up);
            Vector3 outward = doorOffset -
                forward * Vector3.Dot(doorOffset, forward);
            outward.Normalize();
            float currentDepth = Vector3.Dot(
                door.position - actor.transform.position,
                outward);
            float desiredDepth = registry.Dimensions.Width * 0.5f +
                                 radius +
                                 CityBusRidePlan.DoorBodyClearance;
            Vector3 dock = door.position + outward * Mathf.Max(
                0f,
                desiredDepth - currentDepth);
            dock.y = CityStreetSurfacePlanner.SidewalkTop +
                     PlayerFactory.GroundedRootOffset;
            Vector3 closest = walkableArea.ClosestPoint(dock, radius);
            return $"{stop.Id}/{passengerDoor}: no plan; " +
                   $"door={door.position}, dock={dock}, " +
                   $"dock_walkable={walkableArea.Contains(dock, radius)}, " +
                   $"closest={closest}, " +
                   $"closest_distance={Vector3.Distance(dock, closest)}.";
        }

        private static string DescribeInteractionGates(
            CityBusActor actor,
            PlayerRuntime player,
            CityBusRideController ride,
            CityBusRidePlan plan,
            CityBusStopDescriptor stop,
            CityBusPassengerDoor passengerDoor)
        {
            Vector3 offset = player.GameObject.transform.position -
                             plan.EntryPose.RootPosition;
            var animated = player.GameObject.GetComponent<
                PlayerAnimatedInteractionController>();
            return $"{stop.Id}/{passengerDoor}: CanInteract=false; " +
                   $"spawned={actor.IsSpawned}, " +
                   $"doors_open={actor.DoorsFullyOpen}, " +
                   $"state={ride.State}, " +
                   $"phase={animated?.Phase}, " +
                   $"motor_enabled={player.Motor.enabled}, " +
                   $"motor_input={player.Motor.InputEnabled}, " +
                   $"interactor_input={player.Interactor.InputEnabled}, " +
                   $"planar_distance={new Vector2(offset.x, offset.z).magnitude}, " +
                   $"vertical_distance={Mathf.Abs(offset.y)}.";
        }

        private static IEnumerator WaitForState(
            CityBusRideController ride,
            CityBusRideState expected)
        {
            float deadline = Time.realtimeSinceStartup + 4f;
            while (ride != null &&
                   ride.State != expected &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(ride, Is.Not.Null);
            Assert.That(ride.State, Is.EqualTo(expected));
        }

        private static IEnumerator WaitForRidingWithLevelHorizon(
            CityBusRideController ride,
            Camera camera)
        {
            float deadline = Time.realtimeSinceStartup + 4f;
            while (ride != null &&
                   ride.State != CityBusRideState.Riding &&
                   Time.realtimeSinceStartup < deadline)
            {
                Assert.That(
                    Mathf.Abs(Vector3.Dot(
                        camera.transform.right,
                        Vector3.up)),
                    Is.LessThan(0.01f),
                    "The boarding camera blend must keep a level world " +
                    "horizon on every rendered frame.");
                yield return null;
            }

            Assert.That(ride, Is.Not.Null);
            Assert.That(
                ride.State,
                Is.EqualTo(CityBusRideState.Riding));
        }

        private static void AdvanceActorUntil(
            CityBusActor actor,
            System.Func<bool> condition)
        {
            for (int step = 0; step < 1600 && !condition(); step++)
            {
                actor.Advance(
                    0.10f,
                    CityBusObstacleState.Clear,
                    0f);
            }

            Assert.That(condition(), Is.True);
        }

        private static CityBusPlan CreateTwoStopRoute()
        {
            Vector3 start = new Vector3(
                0f,
                CityStreetSurfacePlanner.RoadTop,
                0f);
            Vector3 east = start + Vector3.right * 30f;
            Vector3 southEast = east + Vector3.back * 20f;
            Vector3 southWest = southEast + Vector3.left * 30f;
            RoadEdge edge = new RoadEdge(
                new Vector2Int(0, 0),
                new Vector2Int(0, 1));
            var samples = new List<CityBusPathSample>
            {
                new CityBusPathSample(start, Vector3.right, 0f),
                new CityBusPathSample(east, Vector3.back, 30f),
                new CityBusPathSample(southEast, Vector3.left, 50f),
                new CityBusPathSample(southWest, Vector3.forward, 80f),
                new CityBusPathSample(start, Vector3.right, 100f)
            };
            var clearance = new CityBusClearanceResult(
                true,
                CityBusClearanceFailureKind.None,
                -1,
                default,
                CityBusDesignVehicle.Default.ClearanceMargin);
            var nodes = new List<CityBusRouteNode>
            {
                new CityBusRouteNode(
                    "ride-node",
                    start,
                    Vector3.right,
                    edge,
                    edge.A,
                    edge.B,
                    new[] { 0 })
            };
            var links = new List<CityBusRouteLink>
            {
                new CityBusRouteLink(
                    "ride-link",
                    0,
                    0,
                    CityBusRouteLinkKind.Straight,
                    edge.B,
                    samples,
                    float.PositiveInfinity,
                    clearance)
            };
            var anchors = new List<CityBusSpawnAnchor>
            {
                new CityBusSpawnAnchor(
                    "ride-anchor",
                    0,
                    0f,
                    start,
                    Vector3.right,
                    edge)
            };
            var stops = new List<CityBusStopDescriptor>
            {
                CreateStop("ride-stop-a", start, 8f, edge),
                CreateStop("ride-stop-b", start, 48f, edge)
            };
            return new CityBusPlan(
                91,
                37,
                0x42555331u,
                1.5f,
                CityBusDesignVehicle.Default,
                nodes,
                links,
                anchors,
                stops,
                new List<CityBusClearanceFailure>(),
                1,
                1);
        }

        private static CityBusStopDescriptor CreateStop(
            string id,
            Vector3 start,
            float distance,
            RoadEdge edge)
        {
            Vector3 position = distance <= 30f
                ? start + Vector3.right * distance
                : start + Vector3.right * 30f +
                  Vector3.back * (distance - 30f);
            Vector3 forward = distance <= 30f
                ? Vector3.right
                : Vector3.back;
            return new CityBusStopDescriptor(
                id,
                $"{id}-shelter",
                position + Vector3.forward * 3f,
                0,
                distance,
                position,
                forward,
                edge);
        }

        private static void CreateGround(Transform parent)
        {
            GameObject ground = GameObject.CreatePrimitive(
                PrimitiveType.Cube);
            ground.name = "Bus Ride Test Ground";
            ground.transform.SetParent(parent, false);
            ground.transform.position = new Vector3(
                10f,
                CityStreetSurfacePlanner.SidewalkTop * 0.5f,
                -10f);
            ground.transform.localScale = new Vector3(
                120f,
                CityStreetSurfacePlanner.SidewalkTop,
                120f);
        }

        private sealed class AlwaysWalkableArea : IWalkableArea
        {
            public bool Contains(Vector3 position, float radius = 0f)
            {
                return true;
            }

            public Vector3 Constrain(
                Vector3 currentPosition,
                Vector3 desiredPosition,
                float radius = 0f)
            {
                return desiredPosition;
            }

            public Vector3 ClosestPoint(
                Vector3 position,
                float radius = 0f)
            {
                return position;
            }
        }
    }
}
