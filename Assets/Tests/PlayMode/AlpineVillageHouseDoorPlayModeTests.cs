using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// A shut house door, driven the way the player drives it: the real
    /// world, a real <c>CharacterController</c> and the real interactor.
    ///
    /// The EditMode test beside it can only prove the door is CONFIGURED,
    /// and configuration is exactly what a door-action failure looks like -
    /// a dock a couple of centimetres off the hero's root is refused in
    /// silence, so the prompt shows, the key is pressed and nothing ever
    /// happens. The only way to know the gesture starts and finishes is to
    /// run it.
    /// </summary>
    public sealed class AlpineVillageHouseDoorPlayModeTests
    {
        private const float PinnedFrameSeconds = 1f / 60f;

        /// <summary>Enter, loop and exit at their authored frame rates,
        /// plus room for the walk onto the dock.</summary>
        private const int GestureFrames = 300;

        /// <summary>
        /// How long an inherited transition may take to land. REAL seconds:
        /// an async scene load progresses on the wall clock, and under a
        /// pinned frame clock a frame budget is a fraction of a second of it.
        /// </summary>
        private const float SettleSeconds = 60f;

        [UnitySetUp]
        public IEnumerator PinTheClockAndLetTheWorldSettle()
        {
            // The gesture is timed in SECONDS. Batch mode runs frames as
            // fast as it can, so an unpinned clock turns "wait 300 frames"
            // into most of a second.
            Time.captureDeltaTime = PinnedFrameSeconds;

            // A test that ran before this one may still have a scene load in
            // flight - the cableway ride leaves the area and its scene is
            // torn down mid-transition. That load lands whenever it lands,
            // and a single-mode load destroys everything in the active
            // scene, this fixture's village included. Let it arrive BEFORE
            // anything is built rather than during the twenty frames the
            // interactor needs to see a door.
            float deadline = Time.realtimeSinceStartup + SettleSeconds;
            while (Time.realtimeSinceStartup < deadline &&
                   (SceneTransitionService.IsTransitioning ||
                    AreaTravelService.IsTraveling ||
                    AreaTravelService.HasPendingTravel))
            {
                yield return null;
            }

            for (int frame = 0; frame < 5; frame++)
            {
                yield return null;
            }

            Assert.That(
                SceneTransitionService.IsTransitioning ||
                AreaTravelService.IsTraveling ||
                AreaTravelService.HasPendingTravel,
                Is.False,
                "A transition inherited from an earlier test never landed.");
            GameSessionState.BeginNewGame();
        }

        [TearDown]
        public void ReleaseTheClock()
        {
            Time.captureDeltaTime = 0f;
            GameSessionState.BeginNewGame();
        }

        [UnityTest]
        public IEnumerator HouseDoor_OffersItselfAndAnswersThatItIsLocked()
        {
            var scene = new GameObject("Alpine Village House Door Test");
            try
            {
                AlpineVillagePlan plan = AlpineVillagePlanner.Create(
                    GameSessionState.DefaultCitySeed);
                AlpineVillagePlotDescriptor house = null;
                foreach (AlpineVillagePlotDescriptor plot in plan.Plots)
                {
                    if (plot.Kind == AlpineVillagePlotKind.House)
                    {
                        house = plot;
                        break;
                    }
                }

                Assert.That(house, Is.Not.Null);

                var cameraObject = new GameObject("Camera");
                cameraObject.transform.SetParent(scene.transform, false);
                Camera camera = cameraObject.AddComponent<Camera>();
                var promptObject = new GameObject("Prompt");
                promptObject.transform.SetParent(scene.transform, false);
                InteractionPromptView prompt =
                    promptObject.AddComponent<InteractionPromptView>();

                AlpineVillageWorldResult world =
                    AlpineVillageWorldBuilder.Build(scene.transform, plan);
                Assert.That(world.HouseDoors.Count, Is.GreaterThan(0));

                PlayerRuntime player = PlayerFactory.Create(
                    scene.transform,
                    house.DoorDockPosition +
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

                // Standing on the dock, the door is what the interactor
                // finds - not a neighbour, and not nothing at all.
                LockedDoorInteraction nearest = world.HouseDoors[0];
                foreach (LockedDoorInteraction candidate in world.HouseDoors)
                {
                    if (Vector3.Distance(
                            candidate.InteractionPosition,
                            house.DoorGroundPosition) <
                        Vector3.Distance(
                            nearest.InteractionPosition,
                            house.DoorGroundPosition))
                    {
                        nearest = candidate;
                    }
                }

                Assert.That(
                    player.Interactor.ActiveInteractable,
                    Is.InstanceOf<LockedDoorInteraction>(),
                    "Standing at the threshold offers no door. " +
                    $"active={player.Interactor.ActiveInteractable}, " +
                    $"input={player.Interactor.InputEnabled}, " +
                    "transitioning=" +
                    $"{SceneTransitionService.IsTransitioning}, " +
                    $"travelling={AreaTravelService.IsTraveling}, " +
                    $"configured={nearest.IsConfigured}, " +
                    $"canInteract={nearest.CanInteract(player.Interactor)}, " +
                    "reach=" +
                    $"{Vector3.Distance(nearest.InteractionPosition, player.GameObject.transform.position):0.00} m");
                var door =
                    (LockedDoorInteraction)
                    player.Interactor.ActiveInteractable;
                Assert.That(
                    prompt.GetPromptKeyAt(Time.unscaledTime),
                    Is.EqualTo(
                        AlpineVillageWorldBuilder.HouseDoorPromptKey));
                Assert.That(
                    LocalizationService.Get(door.PromptKey),
                    Is.Not.Empty);

                Assert.That(
                    prompt.TryInvokePrompt(),
                    Is.True,
                    "The prompt is offered but the key does nothing.");

                // The gesture has to actually run. A door action that never
                // begins leaves the prompt up and reports nothing at all.
                bool played = false;
                var controller = player.GameObject
                    .GetComponent<PlayerDoorActionController>();
                bool answered = false;
                for (int frame = 0; frame < GestureFrames; frame++)
                {
                    played |= controller.IsPlaying;
                    if (prompt.IsFeedbackVisibleAt(Time.unscaledTime))
                    {
                        answered = true;
                        break;
                    }

                    yield return null;
                }

                Assert.That(
                    played,
                    Is.True,
                    "The standard door gesture never started, so the dock " +
                    "or the facing was refused in silence.");
                Assert.That(
                    answered,
                    Is.True,
                    "He tried the door and it never said anything.");
                Assert.That(
                    prompt.GetPromptKeyAt(Time.unscaledTime),
                    Is.EqualTo(
                        AlpineVillageWorldBuilder.HouseDoorLockedKey));
                Assert.That(
                    prompt.GetDisplayedTextAt(Time.unscaledTime),
                    Is.Not.Empty,
                    "The refusal has no line in the catalog.");

                // Nothing was loaded and nothing was consumed: he is still
                // standing in the village, in front of the same door.
                Assert.That(
                    SceneTransitionService.IsTransitioning,
                    Is.False,
                    "A shut door must not take him anywhere.");
                Assert.That(
                    Vector3.Distance(
                        player.GameObject.transform.position,
                        house.DoorDockPosition +
                        Vector3.up * PlayerFactory.GroundedRootOffset),
                    Is.LessThan(0.35f),
                    "The gesture left him somewhere other than the dock.");
                Assert.That(player.Motor.InputEnabled, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(scene);
            }
        }
    }
}
