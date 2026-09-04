using System;
using System.Collections;
using System.Linq;
using BarPromenade.Runtime.World;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class MountainRoadCafeMenuPlayModeTests
    {
        private const float PinnedFrameSeconds = 1f / 30f;
        private const float LoadTimeoutSeconds = 60f;
        private const int MaximumSeatFrames = 180;
        private const float OpenMenuLeafAngleDegrees = 5.5f;
        private const float MidFoldLeafAngleDegrees = -90f;
        private const float ClosedMenuLeafAngleDegrees = -185.5f;
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static int teardownSequence;

        [SetUp]
        public void SetUp()
        {
            Time.captureDeltaTime = PinnedFrameSeconds;
            GameSessionState.BeginNewGame();
            GameSessionState.ClearRoute();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.captureDeltaTime = 0f;
            GameSessionState.BeginNewGame();
            GameSessionState.ClearRoute();
            Scene road = SceneManager.GetSceneByName(SceneIds.MountainRoad);
            if (!road.isLoaded)
            {
                yield break;
            }

            Scene blank = SceneManager.CreateScene(
                $"Mountain Cafe Menu Teardown {++teardownSequence}");
            SceneManager.SetActiveScene(blank);
            yield return SceneManager.UnloadSceneAsync(road);
        }

        [UnityTest]
        public IEnumerator
            CounterSeat_FocusesConfirmsAndRetrievesWorldMenu()
        {
            InputTestFixture inputFixture = null;
            Keyboard keyboard = null;
            try
            {
                inputFixture = new InputTestFixture();
                inputFixture.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();

                MountainRoadRoot root = null;
                yield return LoadSceneAndWaitForRoot(value => root = value);
                yield return null;

                Assert.That(root.IsInitialized, Is.True);
                Assert.That(root.CafeMenu, Is.Not.Null);
                Assert.That(root.CafeMenu.IsInitialized, Is.True);
                Assert.That(root.CafeSeatView, Is.Not.Null);

                CityBenchSitInteraction seat = root.Seats.Single(candidate =>
                    string.Equals(
                        candidate.Plan.Id,
                        root.Plan.Terminal.Site.CounterSeat.StableId,
                        StringComparison.Ordinal));
                TeleportPlayer(
                    root.Player,
                    seat.Plan.EntryRootPosition,
                    seat.Plan.EntryRotation);
                yield return null;

                Assert.That(seat.CanInteract(root.Player.Interactor), Is.True);
                seat.Interact(root.Player.Interactor);
                int seatFrames = 0;
                while (!seat.IsSeated && seatFrames++ < MaximumSeatFrames)
                {
                    yield return null;
                }

                Assert.That(seat.IsSeated, Is.True);
                Assert.That(
                    seat.Controller.Phase,
                    Is.EqualTo(PlayerAnimatedInteractionPhase.Looping));
                Assert.That(
                    root.CafeMenu.State,
                    Is.EqualTo(MountainRoadCafeMenuState.Delivering));
                MountainRoadCafeCastController cast = root.World.Cafe.Cast;
                MountainRoadCafeCastAssetRegistry attendantRegistry = cast
                    .GetPresentationRoot(MountainRoadCafeCastRole.Attendant)
                    .GetComponentInChildren<
                        MountainRoadCafeCastAssetRegistry>(true);
                MountainRoadCafeCastPresentation attendantPresentation = cast
                    .GetPresentationRoot(MountainRoadCafeCastRole.Attendant)
                    .GetComponent<MountainRoadCafeCastPresentation>();
                Assert.That(attendantRegistry, Is.Not.Null);
                Assert.That(attendantPresentation, Is.Not.Null);
                Assert.That(
                    attendantRegistry.Animator.cullingMode,
                    Is.EqualTo(AnimatorCullingMode.AlwaysAnimate),
                    "The off-screen attendant hand must keep sampling while " +
                    "the close-up owns the camera.");
                MountainRoadCafeMenuPresentation presentation =
                    root.CafeMenu.Presentation;
                Assert.That(cast.ServiceFrame.HeroMenuRequested, Is.True);
                Vector3 seatedViewerPosition =
                    root.CafeSeatView.CurrentCameraPosition;

                cast.Advance(MountainRoadCafeServiceTimeline.NoticeSeconds);
                yield return null;
                Assert.That(
                    cast.ServiceFrame.Phase,
                    Is.EqualTo(MountainRoadCafeServicePhase.WalkToHero));
                float beforeWalkEnd = Mathf.Max(
                    0f,
                    cast.ServiceFrame.PhaseDurationSeconds -
                    cast.ServiceFrame.PhaseElapsedSeconds -
                    PinnedFrameSeconds * 2f);
                cast.Advance(beforeWalkEnd);
                presentation.RefreshFromServiceFrame();
                Assert.That(
                    cast.ServiceFrame.Phase,
                    Is.EqualTo(MountainRoadCafeServicePhase.WalkToHero));
                Vector3 carryPosition = presentation.PropRoot.position;
                Quaternion carryRotation = presentation.PropRoot.rotation;

                cast.Advance(
                    cast.ServiceFrame.PhaseDurationSeconds -
                    cast.ServiceFrame.PhaseElapsedSeconds +
                    PinnedFrameSeconds * 0.5f);
                presentation.RefreshFromServiceFrame();
                Assert.That(
                    cast.ServiceFrame.Phase,
                    Is.EqualTo(MountainRoadCafeServicePhase.PlaceMenu));
                Assert.That(
                    Vector3.Distance(
                        carryPosition,
                        presentation.PropRoot.position),
                    Is.LessThan(0.03f),
                    "The booklet must not jump when Walk hands off to Place.");
                Assert.That(
                    Quaternion.Angle(
                        carryRotation,
                        presentation.PropRoot.rotation),
                    Is.LessThan(4f),
                    "The booklet must preserve its carried pose at handoff.");
                Assert.That(
                    Vector3.Distance(
                        presentation.GripAnchor.position,
                        cast.AttendantMenuHandSocket.position),
                    Is.LessThan(0.01f),
                    "The attendant must retain the booklet until the " +
                    "placement motion actually begins.");

                cast.Advance(MountainRoadCafeServiceTimeline.NoticeSeconds);
                Assert.That(cast.ServiceFrame.HeroMenuPlaced, Is.True);

                int menuFrames = 0;
                while (root.CafeMenu.State !=
                           MountainRoadCafeMenuState.Open &&
                       menuFrames++ < 6)
                {
                    yield return null;
                }

                yield return null;
                MountainRoadCafeMenuController menu = root.CafeMenu;
                Assert.That(menu.State,
                    Is.EqualTo(MountainRoadCafeMenuState.Open));
                Assert.That(menu.IsInputActive, Is.True);
                Assert.That(presentation.IsConfigured, Is.True);
                Assert.That(presentation.IsVisible, Is.True);
                Assert.That(presentation.IsPlaced, Is.True);
                Assert.That(
                    MountainRoadCafeMenuItemIds.Ordered.Count,
                    Is.EqualTo(3));
                for (int index = 0;
                     index < MountainRoadCafeMenuItemIds.Ordered.Count;
                     index++)
                {
                    Transform line = presentation.PropRoot.Find(
                        $"MenuText.Item.{index:00} Text");
                    Assert.That(line, Is.Not.Null, $"menu row {index}");
                    Assert.That(line.gameObject.activeInHierarchy, Is.True);
                    Renderer lineRenderer = line.GetComponent<Renderer>();
                    TMP_Text lineText = line.GetComponent<TMP_Text>();
                    Assert.That(lineRenderer, Is.Not.Null,
                        $"menu row {index}");
                    Assert.That(lineText, Is.Not.Null,
                        $"menu row {index}");
                    Assert.That(
                        lineText.fontSizeMax,
                        Is.GreaterThanOrEqualTo(0.14f),
                        "World lettering must remain readable at the " +
                        "menu close-up distance.");
                    Assert.That(
                        lineRenderer.enabled,
                        Is.True,
                        line.name);
                }

                Transform marker = presentation.PropRoot.Find(
                    MountainRoadCafeMenuPresentation.SelectionAnchorName +
                    " Text");
                Assert.That(marker, Is.Not.Null);
                Renderer markerRenderer = marker.GetComponent<Renderer>();
                Assert.That(markerRenderer, Is.Not.Null);
                Assert.That(markerRenderer.enabled, Is.True);
                Assert.That(
                    menu.SelectedItemId,
                    Is.EqualTo(MountainRoadCafeMenuItemIds.FriedEggs));

                Camera camera = root.CameraFollow.GetComponent<Camera>();
                Assert.That(camera, Is.Not.Null);
                float focusDeadline = Time.realtimeSinceStartup + 2f;
                while (!root.CafeSeatView.IsMenuFocusComplete &&
                       Time.realtimeSinceStartup < focusDeadline)
                {
                    yield return null;
                    Assert.That(
                        Mathf.Abs(Vector3.Dot(
                            camera.transform.right,
                            Vector3.up)),
                        Is.LessThan(0.001f),
                        "The blend into the menu must remain roll-free.");
                }

                Assert.That(root.CafeSeatView.IsMenuFocusComplete, Is.True);
                Pose expectedFocus = root.CafeSeatView.MenuFocusPose;
                Assert.That(
                    Vector3.Distance(
                        camera.transform.position,
                        expectedFocus.position),
                    Is.LessThan(0.01f));
                Assert.That(
                    Quaternion.Angle(
                        camera.transform.rotation,
                        expectedFocus.rotation),
                    Is.LessThan(1f));
                Assert.That(
                    camera.transform.position.y,
                    Is.GreaterThan(presentation.PropRoot.position.y),
                    "The close-up must stay above the counter and menu.");
                Assert.That(
                    Vector3.Dot(camera.transform.up, Vector3.up),
                    Is.GreaterThan(0f),
                    "The menu close-up must never roll upside down.");
                Vector3 focusTarget = expectedFocus.position +
                    expectedFocus.rotation * Vector3.forward *
                    MountainRoadCafeSeatViewPlan.MenuFocusDistanceMeters;
                Assert.That(
                    Vector3.Distance(seatedViewerPosition, focusTarget),
                    Is.GreaterThan(
                        MountainRoadCafeSeatViewPlan.MenuFocusDistanceMeters),
                    "The close-up must move toward the menu from the seated " +
                    "view, not jump through it.");
                Assert.That(
                    Vector3.Dot(
                        (expectedFocus.position - focusTarget).normalized,
                        (seatedViewerPosition - focusTarget).normalized),
                    Is.GreaterThan(0.999f),
                    "The close-up must remain on the seated viewer's ray.");
                Assert.That(
                    Mathf.Abs(Vector3.Dot(
                        camera.transform.right,
                        Vector3.up)),
                    Is.LessThan(0.001f),
                    "The menu close-up must have no camera roll.");
                Assert.That(
                    camera.fieldOfView,
                    Is.EqualTo(
                        MountainRoadCafeSeatViewPlan.MenuFocusFieldOfView)
                        .Within(0.1f));

                Renderer pageRenderer = presentation.PropRoot
                    .GetComponentsInChildren<Renderer>(true)
                    .Single(candidate =>
                        candidate.name == "Cafe_MenuPages");
                var pageProperties = new MaterialPropertyBlock();
                pageRenderer.GetPropertyBlock(pageProperties);
                Assert.That(
                    pageProperties.GetTexture(
                        Shader.PropertyToID("_BaseMap")),
                    Is.SameAs(Texture2D.whiteTexture),
                    "Menu paper must not sample the green props stripe.");
                Plane page = MeasurePagePlane(root.World.Cafe.Model);
                for (int index = 0;
                     index < presentation.ItemLines.Count;
                     index++)
                {
                    AssertReadableWorldText(
                        presentation.ItemLines[index],
                        camera,
                        $"menu row {index}");
                    AssertTextLiesOnThePage(
                        presentation.ItemLines[index],
                        page,
                        $"menu row {index}");
                }

                AssertTextLiesOnThePage(
                    presentation.SelectionMarker,
                    page,
                    "menu selection mark");

                Vector3 positionBeforeArrow = camera.transform.position;
                Quaternion rotationBeforeArrow = camera.transform.rotation;
                inputFixture.Press(
                    keyboard.upArrowKey,
                    queueEventOnly: true);
                yield return null;
                inputFixture.Release(
                    keyboard.upArrowKey,
                    queueEventOnly: true);
                yield return null;
                Assert.That(
                    Vector3.Distance(
                        positionBeforeArrow,
                        camera.transform.position),
                    Is.LessThan(0.001f),
                    "Menu close-up must lock camera translation.");
                Assert.That(
                    Quaternion.Angle(
                        rotationBeforeArrow,
                        camera.transform.rotation),
                    Is.LessThan(0.01f),
                    "Menu close-up must ignore every look input.");
                Assert.That(
                    menu.SelectedItemId,
                    Is.EqualTo(MountainRoadCafeMenuItemIds.FriedEggs),
                    "Arrow Up is neither camera nor menu input in close-up.");

                inputFixture.Press(
                    keyboard.sKey,
                    queueEventOnly: true);
                yield return null;
                inputFixture.Release(
                    keyboard.sKey,
                    queueEventOnly: true);
                yield return null;

                Assert.That(
                    menu.SelectedItemId,
                    Is.EqualTo(MountainRoadCafeMenuItemIds.CheeseSandwich));

                inputFixture.Press(
                    keyboard.wKey,
                    queueEventOnly: true);
                yield return null;
                inputFixture.Release(
                    keyboard.wKey,
                    queueEventOnly: true);
                yield return null;

                Assert.That(
                    menu.SelectedItemId,
                    Is.EqualTo(MountainRoadCafeMenuItemIds.FriedEggs));

                inputFixture.Press(
                    keyboard.wKey,
                    queueEventOnly: true);
                yield return null;
                inputFixture.Release(
                    keyboard.wKey,
                    queueEventOnly: true);
                yield return null;

                Assert.That(
                    menu.SelectedItemId,
                    Is.EqualTo(MountainRoadCafeMenuItemIds.BlackCoffee));
                Assert.That(presentation.SelectedIndex,
                    Is.EqualTo(menu.SelectedIndex));

                inputFixture.Press(
                    keyboard.spaceKey,
                    queueEventOnly: true);
                yield return null;
                inputFixture.Release(
                    keyboard.spaceKey,
                    queueEventOnly: true);
                yield return null;

                Assert.That(seat.IsSeated, Is.True,
                    "Space confirms the order and must not stand the hero.");
                Assert.That(menu.State,
                    Is.EqualTo(MountainRoadCafeMenuState.Resting));
                Assert.That(
                    menu.ConfirmedItemId,
                    Is.EqualTo(MountainRoadCafeMenuItemIds.BlackCoffee));
                Assert.That(presentation.IsConfirmed, Is.True);
                Assert.That(
                    presentation.SelectionMarker.text,
                    Is.EqualTo("X"));
                Assert.That(menu.IsInputActive, Is.False);
                Assert.That(
                    cast.ServiceFrame.HeroMenuRetrievalRequested,
                    Is.False,
                    "Confirming may close the booklet, but staff wait " +
                    "until the hero has stood up.");
                Assert.That(presentation.IsRestingOnCounter, Is.True);
                Assert.That(presentation.IsVisible, Is.True);

                Vector3 restingMenuDirection =
                    presentation.Page.RestingWorldCenter -
                    camera.transform.position;
                camera.transform.rotation = Quaternion.LookRotation(
                    -restingMenuDirection.normalized,
                    Vector3.up);
                seat.Interact(root.Player.Interactor);
                Assert.That(
                    seat.Controller.Phase,
                    Is.EqualTo(PlayerAnimatedInteractionPhase.Exiting));
                Assert.That(menu.State,
                    Is.EqualTo(MountainRoadCafeMenuState.Resting));
                Assert.That(
                    cast.ServiceFrame.HeroMenuRetrievalRequested,
                    Is.False,
                    "The menu must remain on the counter during stand-up.");

                float exitDeadline = Time.realtimeSinceStartup + 5f;
                while (seat.Controller.IsActive &&
                       Time.realtimeSinceStartup < exitDeadline)
                {
                    yield return null;
                }

                Assert.That(seat.Controller.IsActive, Is.False);
                Assert.That(menu.State,
                    Is.EqualTo(MountainRoadCafeMenuState.Retrieving));
                Assert.That(
                    cast.ServiceFrame.HeroMenuRetrievalRequested,
                    Is.True);

                int approachPhases = 0;
                while (cast.ServiceFrame.Phase !=
                           MountainRoadCafeServicePhase.WalkToMenu &&
                       !cast.ServiceFrame.HeroMenuRetrieved &&
                       approachPhases++ < 12)
                {
                    cast.Advance(Mathf.Max(
                        0.001f,
                        cast.ServiceFrame.PhaseDurationSeconds -
                        cast.ServiceFrame.PhaseElapsedSeconds));
                }

                Assert.That(
                    cast.ServiceFrame.Phase,
                    Is.EqualTo(MountainRoadCafeServicePhase.WalkToMenu));
                float beforeApproachEnd = Mathf.Max(
                    0f,
                    cast.ServiceFrame.PhaseDurationSeconds -
                    cast.ServiceFrame.PhaseElapsedSeconds -
                    PinnedFrameSeconds * 2f);
                cast.Advance(beforeApproachEnd);
                presentation.RefreshFromServiceFrame();
                Assert.That(
                    cast.ServiceFrame.Phase,
                    Is.EqualTo(MountainRoadCafeServicePhase.WalkToMenu));
                Assert.That(
                    attendantPresentation.CurrentClipKind,
                    Is.EqualTo(MountainRoadCafeCastClipKind.Walk));
                Assert.That(
                    attendantPresentation.CurrentClipTimeSeconds,
                    Is.GreaterThan(
                        MountainRoadCafeServiceTimeline.WalkSeconds -
                        PinnedFrameSeconds * 3f),
                    "WalkToMenu must sample its authored end pose before " +
                    "TakeMenu freezes the attendant.");
                float frozenWalkTime =
                    attendantPresentation.CurrentClipTimeSeconds;
                Vector3 dockedMenuPosition = presentation.PropRoot.position;

                cast.Advance(
                    cast.ServiceFrame.PhaseDurationSeconds -
                    cast.ServiceFrame.PhaseElapsedSeconds +
                    PinnedFrameSeconds * 0.5f);
                presentation.RefreshFromServiceFrame();
                Assert.That(
                    cast.ServiceFrame.Phase,
                    Is.EqualTo(MountainRoadCafeServicePhase.TakeMenu));
                Assert.That(
                    attendantPresentation.CurrentClipTimeSeconds,
                    Is.EqualTo(frozenWalkTime).Within(0.0001f),
                    "TakeMenu must retain the sampled end pose.");
                Assert.That(
                    Vector3.Distance(
                        dockedMenuPosition,
                        presentation.PropRoot.position),
                    Is.LessThan(0.005f),
                    "TakeMenu begins with the booklet still on the counter.");
                float beforeTakeEnd = Mathf.Max(
                    0f,
                    cast.ServiceFrame.PhaseDurationSeconds -
                    cast.ServiceFrame.PhaseElapsedSeconds -
                    PinnedFrameSeconds * 2f);
                cast.Advance(beforeTakeEnd);
                presentation.RefreshFromServiceFrame();
                Assert.That(
                    Vector3.Distance(
                        presentation.GripAnchor.position,
                        cast.AttendantMenuHandSocket.position),
                    Is.LessThan(0.01f),
                    "The hand must reach the booklet before carrying it.");
                Vector3 takePosition = presentation.PropRoot.position;
                Quaternion takeRotation = presentation.PropRoot.rotation;

                cast.Advance(
                    cast.ServiceFrame.PhaseDurationSeconds -
                    cast.ServiceFrame.PhaseElapsedSeconds +
                    PinnedFrameSeconds * 0.5f);
                presentation.RefreshFromServiceFrame();
                Assert.That(
                    cast.ServiceFrame.Phase,
                    Is.EqualTo(MountainRoadCafeServicePhase.CarryMenuBack));
                Assert.That(
                    Vector3.Distance(
                        takePosition,
                        presentation.PropRoot.position),
                    Is.LessThan(0.03f),
                    "The booklet must not jump when pickup becomes carry.");
                Assert.That(
                    Quaternion.Angle(
                        takeRotation,
                        presentation.PropRoot.rotation),
                    Is.LessThan(4f));
                Assert.That(
                    Vector3.Distance(
                        presentation.GripAnchor.position,
                        cast.AttendantMenuHandSocket.position),
                    Is.LessThan(0.01f));

                cast.Advance(
                    cast.ServiceFrame.PhaseDurationSeconds -
                    cast.ServiceFrame.PhaseElapsedSeconds);
                yield return null;
                Assert.That(cast.ServiceFrame.HeroMenuRetrieved, Is.True);
                Assert.That(menu.State,
                    Is.EqualTo(MountainRoadCafeMenuState.Closed));
                Assert.That(presentation.IsVisible, Is.False);

                Assert.That(seat.IsSeated, Is.False);
                Assert.That(root.CafeSeatView.IsFirstPerson, Is.False);
                Assert.That(menu.IsInputActive, Is.False);
            }
            finally
            {
                if (keyboard != null && keyboard.added)
                {
                    InputSystem.RemoveDevice(keyboard);
                }

                inputFixture?.TearDown();
            }
        }

        [UnityTest]
        public IEnumerator
            CounterSeat_StandingWithoutChoiceRetrievesMenuAndRestoresCamera()
        {
            MountainRoadRoot root = null;
            yield return LoadSceneAndWaitForRoot(value => root = value);
            yield return null;

            CityBenchSitInteraction seat = root.Seats.Single(candidate =>
                string.Equals(
                    candidate.Plan.Id,
                    root.Plan.Terminal.Site.CounterSeat.StableId,
                    StringComparison.Ordinal));
            TeleportPlayer(
                root.Player,
                seat.Plan.EntryRootPosition,
                seat.Plan.EntryRotation);
            yield return null;
            bool previousFixedPose = root.CameraFollow.FixedPoseActive;
            float previousFieldOfView =
                root.CameraFollow.GetComponent<Camera>().fieldOfView;

            seat.Interact(root.Player.Interactor);
            int seatFrames = 0;
            while (!seat.IsSeated && seatFrames++ < MaximumSeatFrames)
            {
                yield return null;
            }

            Assert.That(seat.IsSeated, Is.True);
            MountainRoadCafeCastController cast = root.World.Cafe.Cast;
            MountainRoadCafeMenuController menu = root.CafeMenu;
            MountainRoadCafeMenuPresentation presentation =
                menu.Presentation;
            cast.Advance(
                MountainRoadCafeServiceTimeline.NoticeSeconds +
                MountainRoadCafeServiceTimeline.WalkSeconds +
                MountainRoadCafeServiceTimeline.NoticeSeconds);
            int menuFrames = 0;
            while (menu.State != MountainRoadCafeMenuState.Open &&
                   menuFrames++ < 10)
            {
                yield return null;
            }

            float focusDeadline = Time.realtimeSinceStartup + 2f;
            while (!root.CafeSeatView.IsMenuFocusComplete &&
                   Time.realtimeSinceStartup < focusDeadline)
            {
                yield return null;
            }

            Assert.That(menu.State,
                Is.EqualTo(MountainRoadCafeMenuState.Open));
            Assert.That(root.CafeSeatView.IsMenuFocusComplete, Is.True);
            Assert.That(menu.ConfirmedItemId, Is.Null);
            CounterMenuPageView page = presentation.Page;
            page.AdvanceFold(CounterMenuPageView.FoldDurationSeconds);
            Assert.That(page.FoldAmount, Is.EqualTo(0f).Within(0.001f));
            Assert.That(page.IsTextVisible, Is.True);

            seat.Interact(root.Player.Interactor);

            Assert.That(seat.IsSeated, Is.True,
                "The first E closes the menu without standing up.");
            Assert.That(menu.State,
                Is.EqualTo(MountainRoadCafeMenuState.Resting));
            Assert.That(presentation.IsRestingOnCounter, Is.True);
            Assert.That(presentation.IsVisible, Is.True);
            AssertMenuPhysicallyFoldsClosed(page);
            Assert.That(
                cast.ServiceFrame.HeroMenuRetrievalRequested,
                Is.False);
            yield return null;

            Camera camera = root.CameraFollow.GetComponent<Camera>();
            Vector3 restingCenter = presentation.Page
                .RestingWorldCenter;
            Assert.That(
                seat.PromptKey,
                Is.EqualTo(CityBenchSitInteraction.StandPromptKey),
                "Closing must expose stand before gaze-based reopening " +
                "can re-arm.");

            float focusReleaseDeadline = Time.realtimeSinceStartup + 2f;
            while (root.CafeSeatView.IsMenuFocusLocked &&
                   Time.realtimeSinceStartup < focusReleaseDeadline)
            {
                yield return null;
            }

            Assert.That(root.CafeSeatView.IsMenuFocusLocked, Is.False);
            InputTestFixture lookFixture = new InputTestFixture();
            Keyboard lookKeyboard = null;
            try
            {
                lookFixture.Setup();
                lookKeyboard = InputSystem.AddDevice<Keyboard>();
                lookFixture.Press(
                    lookKeyboard.downArrowKey,
                    queueEventOnly: true);
                int lookFrames = 0;
                while (seat.PromptKey !=
                           MountainRoadCafeMenuController.OpenMenuPromptKey &&
                       lookFrames++ < 60)
                {
                    yield return null;
                }

                lookFixture.Release(
                    lookKeyboard.downArrowKey,
                    queueEventOnly: true);
                yield return null;
            }
            finally
            {
                if (lookKeyboard != null && lookKeyboard.added)
                {
                    InputSystem.RemoveDevice(lookKeyboard);
                }

                lookFixture.TearDown();
            }

            Assert.That(
                seat.PromptKey,
                Is.EqualTo(
                    MountainRoadCafeMenuController.OpenMenuPromptKey));
            seat.Interact(root.Player.Interactor);
            Assert.That(menu.State,
                Is.EqualTo(MountainRoadCafeMenuState.Open));
            Assert.That(seat.IsSeated, Is.True);
            AssertMenuPhysicallyUnfoldsOpen(page);
            seat.Interact(root.Player.Interactor);
            Assert.That(menu.State,
                Is.EqualTo(MountainRoadCafeMenuState.Resting));

            camera.transform.rotation = Quaternion.LookRotation(
                -(restingCenter - camera.transform.position).normalized,
                Vector3.up);
            seat.Interact(root.Player.Interactor);
            Assert.That(
                seat.Controller.Phase,
                Is.EqualTo(PlayerAnimatedInteractionPhase.Exiting));
            Assert.That(menu.State,
                Is.EqualTo(MountainRoadCafeMenuState.Resting));
            Assert.That(
                cast.ServiceFrame.HeroMenuRetrievalRequested,
                Is.False);

            float exitDeadline = Time.realtimeSinceStartup + 5f;
            while (seat.Controller.IsActive &&
                   Time.realtimeSinceStartup < exitDeadline)
            {
                yield return null;
            }

            Assert.That(seat.IsSeated, Is.False);
            Assert.That(root.CafeSeatView.IsFirstPerson, Is.False);
            Assert.That(root.CafeSeatView.IsMenuFocusLocked, Is.False);
            Assert.That(
                root.CameraFollow.FixedPoseActive,
                Is.EqualTo(previousFixedPose));
            if (!previousFixedPose)
            {
                Assert.That(
                    root.CameraFollow.GetComponent<Camera>().fieldOfView,
                    Is.EqualTo(previousFieldOfView).Within(0.1f));
            }

            Assert.That(menu.State,
                Is.EqualTo(MountainRoadCafeMenuState.Retrieving));
            Assert.That(menu.ConfirmedItemId, Is.Null);
            Assert.That(
                cast.ServiceFrame.HeroMenuRetrievalRequested,
                Is.True);

            int retrievalPhases = 0;
            while (!cast.ServiceFrame.HeroMenuRetrieved &&
                   retrievalPhases++ < 16)
            {
                cast.Advance(Mathf.Max(
                    0.001f,
                    cast.ServiceFrame.PhaseDurationSeconds -
                    cast.ServiceFrame.PhaseElapsedSeconds));
                yield return null;
            }

            Assert.That(cast.ServiceFrame.HeroMenuRetrieved, Is.True);
            Assert.That(cast.ServiceFrame.Phase,
                Is.EqualTo(MountainRoadCafeServicePhase.Wiping));
            Assert.That(menu.State,
                Is.EqualTo(MountainRoadCafeMenuState.Closed));
            Assert.That(menu.ConfirmedItemId, Is.Null);
            Assert.That(presentation.IsVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator
            CounterSeat_ForcedExitDuringDeliveryClosesBeforeRetrieval()
        {
            MountainRoadRoot root = null;
            yield return LoadSceneAndWaitForRoot(value => root = value);
            yield return null;

            CityBenchSitInteraction seat = root.Seats.Single(candidate =>
                string.Equals(
                    candidate.Plan.Id,
                    root.Plan.Terminal.Site.CounterSeat.StableId,
                    StringComparison.Ordinal));
            TeleportPlayer(
                root.Player,
                seat.Plan.EntryRootPosition,
                seat.Plan.EntryRotation);
            yield return null;
            seat.Interact(root.Player.Interactor);
            int seatFrames = 0;
            while (!seat.IsSeated && seatFrames++ < MaximumSeatFrames)
            {
                yield return null;
            }

            Assert.That(seat.IsSeated, Is.True);
            MountainRoadCafeCastController cast = root.World.Cafe.Cast;
            MountainRoadCafeMenuController menu = root.CafeMenu;
            MountainRoadCafeMenuPresentation presentation =
                menu.Presentation;
            Assert.That(
                menu.State,
                Is.EqualTo(MountainRoadCafeMenuState.Delivering));

            Assert.That(seat.RequestExit(), Is.True);
            Assert.That(
                seat.Controller.Phase,
                Is.EqualTo(PlayerAnimatedInteractionPhase.Exiting));

            int deliveryPhases = 0;
            while (!cast.ServiceFrame.HeroMenuPlaced &&
                   deliveryPhases++ < 12)
            {
                cast.Advance(Mathf.Max(
                    0.001f,
                    cast.ServiceFrame.PhaseDurationSeconds -
                    cast.ServiceFrame.PhaseElapsedSeconds +
                    PinnedFrameSeconds * 0.5f));
                yield return null;
            }

            Assert.That(cast.ServiceFrame.HeroMenuPlaced, Is.True);
            Assert.That(
                seat.Controller.Phase,
                Is.EqualTo(PlayerAnimatedInteractionPhase.Exiting),
                "The delivery was stepped before the stand-up finished.");
            Assert.That(
                menu.State,
                Is.EqualTo(MountainRoadCafeMenuState.Resting));
            Assert.That(presentation.IsRestingOnCounter, Is.True);
            Assert.That(presentation.IsVisible, Is.True);
            Assert.That(
                cast.ServiceFrame.HeroMenuRetrievalRequested,
                Is.False,
                "Staff must wait for the stand-up animation to finish.");

            float exitDeadline = Time.realtimeSinceStartup + 5f;
            while (seat.Controller.IsActive &&
                   Time.realtimeSinceStartup < exitDeadline)
            {
                yield return null;
            }

            Assert.That(seat.IsSeated, Is.False);
            Assert.That(
                menu.State,
                Is.EqualTo(MountainRoadCafeMenuState.Retrieving));
            Assert.That(
                cast.ServiceFrame.HeroMenuRetrievalRequested,
                Is.True);
        }

        [UnityTest]
        public IEnumerator
            CounterSeat_CancelledQuickReentryRetrievesDeliveredMenu()
        {
            MountainRoadRoot root = null;
            yield return LoadSceneAndWaitForRoot(value => root = value);
            yield return null;

            CityBenchSitInteraction seat = root.Seats.Single(candidate =>
                string.Equals(
                    candidate.Plan.Id,
                    root.Plan.Terminal.Site.CounterSeat.StableId,
                    StringComparison.Ordinal));
            TeleportPlayer(
                root.Player,
                seat.Plan.EntryRootPosition,
                seat.Plan.EntryRotation);
            yield return null;
            seat.Interact(root.Player.Interactor);
            int seatFrames = 0;
            while (!seat.IsSeated && seatFrames++ < MaximumSeatFrames)
            {
                yield return null;
            }

            Assert.That(seat.IsSeated, Is.True);
            MountainRoadCafeCastController cast = root.World.Cafe.Cast;
            MountainRoadCafeMenuController menu = root.CafeMenu;
            MountainRoadCafeMenuPresentation presentation =
                menu.Presentation;
            Assert.That(
                menu.State,
                Is.EqualTo(MountainRoadCafeMenuState.Delivering));

            Assert.That(seat.RequestExit(), Is.True);
            float exitDeadline = Time.realtimeSinceStartup + 5f;
            while (seat.Controller.IsActive &&
                   Time.realtimeSinceStartup < exitDeadline)
            {
                yield return null;
            }

            Assert.That(seat.Controller.IsActive, Is.False);
            Assert.That(cast.ServiceFrame.HeroMenuPlaced, Is.False,
                "The exit must finish before this delayed delivery.");
            Assert.That(
                menu.State,
                Is.EqualTo(MountainRoadCafeMenuState.Delivering));

            Assert.That(seat.CanInteract(root.Player.Interactor), Is.True);
            seat.Interact(root.Player.Interactor);
            Assert.That(seat.OwnsActiveInteraction, Is.True);
            Assert.That(seat.IsSeated, Is.False);
            Assert.That(
                seat.Controller.Phase,
                Is.EqualTo(PlayerAnimatedInteractionPhase.Positioning));

            int deliveryPhases = 0;
            while (!cast.ServiceFrame.HeroMenuPlaced &&
                   deliveryPhases++ < 12)
            {
                cast.Advance(Mathf.Max(
                    0.001f,
                    cast.ServiceFrame.PhaseDurationSeconds -
                    cast.ServiceFrame.PhaseElapsedSeconds +
                    PinnedFrameSeconds * 0.5f));
            }

            Assert.That(cast.ServiceFrame.HeroMenuPlaced, Is.True);
            yield return null;
            Assert.That(
                seat.Controller.Phase ==
                    PlayerAnimatedInteractionPhase.Positioning ||
                seat.Controller.Phase ==
                    PlayerAnimatedInteractionPhase.Entering,
                Is.True);
            Assert.That(seat.IsSeated, Is.False);
            Assert.That(
                menu.State,
                Is.EqualTo(MountainRoadCafeMenuState.Open),
                "The in-flight re-entry temporarily owns this delivery.");
            Assert.That(
                cast.ServiceFrame.HeroMenuRetrievalRequested,
                Is.False);

            Assert.That(
                seat.Controller.CancelActiveInteraction(),
                Is.True);
            Assert.That(
                seat.Controller.Phase,
                Is.EqualTo(PlayerAnimatedInteractionPhase.Idle));
            Assert.That(seat.OwnsActiveInteraction, Is.False);
            Assert.That(seat.IsSeated, Is.False);
            Assert.That(
                menu.State,
                Is.EqualTo(MountainRoadCafeMenuState.Retrieving));
            Assert.That(presentation.IsRestingOnCounter, Is.True);
            Assert.That(presentation.IsVisible, Is.True);
            Assert.That(
                cast.ServiceFrame.HeroMenuRetrievalRequested,
                Is.True,
                "Cancelling the reserved re-entry must not orphan an open " +
                "booklet on the counter.");

            int retrievalPhases = 0;
            while (!cast.ServiceFrame.HeroMenuRetrieved &&
                   retrievalPhases++ < 16)
            {
                cast.Advance(Mathf.Max(
                    0.001f,
                    cast.ServiceFrame.PhaseDurationSeconds -
                    cast.ServiceFrame.PhaseElapsedSeconds));
                yield return null;
            }

            Assert.That(cast.ServiceFrame.HeroMenuRetrieved, Is.True);
            Assert.That(
                menu.State,
                Is.EqualTo(MountainRoadCafeMenuState.Closed));
            Assert.That(presentation.IsVisible, Is.False);
        }

        private static void AssertMenuPhysicallyFoldsClosed(
            CounterMenuPageView page)
        {
            Assert.That(page, Is.Not.Null);
            Assert.That(page.LeftFoldHinge, Is.Not.Null);
            Assert.That(page.RightFoldHinge, Is.Not.Null);
            Assert.That(
                page.LeftFoldHinge,
                Is.Not.SameAs(page.RightFoldHinge));
            Assert.That(
                page.LeftFoldHinge.parent,
                Is.SameAs(page.RightFoldHinge.parent));
            Assert.That(
                page.LeftFoldHinge.GetComponentsInChildren<Renderer>(true),
                Has.Length.EqualTo(2));
            Assert.That(
                page.RightFoldHinge.GetComponentsInChildren<Renderer>(true),
                Has.Length.EqualTo(2));
            Assert.That(page.RestingPropRenderers, Has.Count.EqualTo(5));
            Assert.That(page.IsFoldTransitionActive, Is.True);
            Assert.That(page.FoldAmount, Is.EqualTo(0f).Within(0.001f));

            page.AdvanceFold(
                CounterMenuPageView.FoldDurationSeconds * 0.5f);

            Assert.That(page.FoldAmount, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(
                page.LeftLeafAngleDegrees,
                Is.EqualTo(MidFoldLeafAngleDegrees).Within(0.1f));
            Assert.That(page.IsFoldTransitionActive, Is.True);
            Assert.That(page.IsTextVisible, Is.False);
            AssertOpaquePhysicalFold(page);
            Assert.That(
                Vector3.Dot(
                    page.LeftFoldHinge
                        .GetComponentsInChildren<Renderer>(true)[0]
                        .bounds.center -
                    page.LeftFoldHinge.parent.position,
                    page.LeftFoldHinge.parent.up),
                Is.GreaterThan(0.08f),
                "The booklet leaf must fold over the counter, not under it.");

            page.AdvanceFold(
                CounterMenuPageView.FoldDurationSeconds * 0.5f);

            Assert.That(page.FoldAmount, Is.EqualTo(1f).Within(0.001f));
            Assert.That(
                page.LeftLeafAngleDegrees,
                Is.EqualTo(ClosedMenuLeafAngleDegrees).Within(0.1f));
            Assert.That(page.IsFoldTransitionActive, Is.False);
            Assert.That(page.IsRestingVisible, Is.True);
            Assert.That(page.IsTextVisible, Is.False);
            AssertOpaquePhysicalFold(page);
            Assert.That(
                page.LeftFoldHinge.parent
                    .GetComponentsInChildren<Collider>(true),
                Is.Empty);
        }

        private static void AssertMenuPhysicallyUnfoldsOpen(
            CounterMenuPageView page)
        {
            Assert.That(page.IsFoldTransitionActive, Is.True);
            Assert.That(page.FoldAmount, Is.EqualTo(1f).Within(0.001f));
            Assert.That(page.IsTextVisible, Is.False,
                "Text must remain hidden while the booklet unfolds.");

            page.AdvanceFold(
                CounterMenuPageView.FoldDurationSeconds * 0.5f);

            Assert.That(page.FoldAmount, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(
                page.LeftLeafAngleDegrees,
                Is.EqualTo(MidFoldLeafAngleDegrees).Within(0.1f));
            Assert.That(page.IsFoldTransitionActive, Is.True);
            Assert.That(page.IsTextVisible, Is.False);
            AssertOpaquePhysicalFold(page);

            page.AdvanceFold(
                CounterMenuPageView.FoldDurationSeconds * 0.5f);

            Assert.That(page.FoldAmount, Is.EqualTo(0f).Within(0.001f));
            Assert.That(
                page.LeftLeafAngleDegrees,
                Is.EqualTo(OpenMenuLeafAngleDegrees).Within(0.1f));
            Assert.That(page.IsFoldTransitionActive, Is.False);
            Assert.That(page.IsRestingVisible, Is.False);
            Assert.That(page.IsTextVisible, Is.True,
                "Readable text appears only after the booklet is open.");
            Assert.That(
                page.PropRenderers.All(renderer => renderer.enabled),
                Is.True);
        }

        private static void AssertOpaquePhysicalFold(
            CounterMenuPageView page)
        {
            Assert.That(
                page.RestingPropRenderers.All(renderer =>
                    renderer != null &&
                    renderer.enabled &&
                    renderer.gameObject.activeInHierarchy),
                Is.True);
            foreach (Renderer renderer in page.RestingPropRenderers)
            {
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                Assert.That(
                    properties.GetColor(BaseColorId).a,
                    Is.EqualTo(1f).Within(0.001f),
                    $"{renderer.name} must remain physically opaque.");
            }
        }

        private static IEnumerator LoadSceneAndWaitForRoot(
            Action<MountainRoadRoot> capture)
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(
                SceneIds.MountainRoad,
                LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);
            float deadline = Time.realtimeSinceStartup + LoadTimeoutSeconds;
            while (!operation.isDone && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(operation.isDone, Is.True,
                "Mountain Road did not load before the timeout.");
            MountainRoadRoot root = null;
            while (root == null && Time.realtimeSinceStartup < deadline)
            {
                root = Object.FindAnyObjectByType<MountainRoadRoot>();
                if (root == null)
                {
                    yield return null;
                }
            }

            Assert.That(root, Is.Not.Null);
            capture(root);
        }

        private static void TeleportPlayer(
            PlayerRuntime player,
            Vector3 position,
            Quaternion rotation)
        {
            CharacterController controller =
                player.GameObject.GetComponent<CharacterController>();
            bool wasEnabled = controller != null && controller.enabled;
            if (controller != null)
            {
                controller.enabled = false;
            }

            player.GameObject.transform.SetPositionAndRotation(
                position,
                rotation);
            if (controller != null)
            {
                controller.enabled = wasEnabled;
            }

            Physics.SyncTransforms();
        }

        /// <summary>
        /// The page itself, pinned by three authored anchors that are not
        /// in a line: the selection mark and item 00 share a line, items 00
        /// and 01 share a column. Its normal points up out of the paper.
        /// </summary>
        private static Plane MeasurePagePlane(
            MountainRoadCafeAssetRegistry model)
        {
            Assert.That(
                model.TryGetAnchor("MenuText.Selection", out Transform mark),
                Is.True);
            Assert.That(
                model.TryGetAnchor("MenuText.Item.00", out Transform first),
                Is.True);
            Assert.That(
                model.TryGetAnchor("MenuText.Item.01", out Transform second),
                Is.True);
            Vector3 right = (first.position - mark.position).normalized;
            Vector3 up = Vector3.ProjectOnPlane(
                first.position - second.position,
                right).normalized;
            Vector3 normal = Vector3.Cross(up, right).normalized;
            Assert.That(
                Vector3.Dot(normal, Vector3.up),
                Is.GreaterThan(0.9f),
                "The authored page must lie face up on the counter.");
            return new Plane(normal, first.position);
        }

        /// <summary>
        /// Every glyph must lie ON the paper: above the page plane and
        /// within a few millimetres of it.
        ///
        /// This is the assertion that was missing. The lettering was built
        /// from the anchors' `unity_local_forward`, which is written in
        /// Unity axes while the model root's own space is the model's, so
        /// every line stood UPRIGHT INSIDE the page and showed only the
        /// sliver that cleared the paper. Nothing else here caught it:
        /// a quad on edge still faces the camera, still reads left to
        /// right, still lands on screen and still fits its line.
        /// </summary>
        private static void AssertTextLiesOnThePage(
            TMP_Text text,
            Plane page,
            string label)
        {
            Assert.That(text, Is.Not.Null, label);
            text.ForceMeshUpdate();
            Assert.That(
                Vector3.Angle(-text.transform.forward, page.normal),
                Is.LessThan(1f),
                $"{label} must lie in the page's own plane.");
            for (int index = 0;
                 index < text.textInfo.characterCount;
                 index++)
            {
                TMP_CharacterInfo character =
                    text.textInfo.characterInfo[index];
                if (!character.isVisible)
                {
                    continue;
                }

                foreach (Vector3 corner in new[]
                         {
                             character.bottomLeft,
                             character.topLeft,
                             character.topRight,
                             character.bottomRight
                         })
                {
                    float height = page.GetDistanceToPoint(
                        text.transform.TransformPoint(corner));
                    Assert.That(
                        height,
                        Is.InRange(0f, 0.006f),
                        $"{label} glyph {index} must rest on the page, " +
                        $"not stand in it ({height * 1000f:0.0} mm).");
                }
            }
        }

        private static void AssertReadableWorldText(
            TMP_Text text,
            Camera camera,
            string label)
        {
            Assert.That(text, Is.Not.Null, label);
            text.ForceMeshUpdate();
            Assert.That(text.isTextOverflowing, Is.False,
                $"{label} must fit its physical page line.");
            Assert.That(text.textInfo.lineCount, Is.EqualTo(1),
                $"{label} must remain a single line.");
            Assert.That(
                Vector3.Dot(
                    -text.transform.forward,
                    (camera.transform.position -
                     text.transform.position).normalized),
                Is.GreaterThan(0.35f),
                $"{label} must show the readable TMP face.");

            int expectedVisible = text.text.Count(character =>
                !char.IsWhiteSpace(character));
            int actualVisible = 0;
            for (int index = 0;
                 index < text.textInfo.characterCount;
                 index++)
            {
                TMP_CharacterInfo character =
                    text.textInfo.characterInfo[index];
                if (!character.isVisible)
                {
                    continue;
                }

                actualVisible++;
                Vector3 bottomLeft = camera.WorldToViewportPoint(
                    text.transform.TransformPoint(character.bottomLeft));
                Vector3 topLeft = camera.WorldToViewportPoint(
                    text.transform.TransformPoint(character.topLeft));
                Vector3 topRight = camera.WorldToViewportPoint(
                    text.transform.TransformPoint(character.topRight));
                Vector3 bottomRight = camera.WorldToViewportPoint(
                    text.transform.TransformPoint(character.bottomRight));
                Vector3 left = (bottomLeft + topLeft) * 0.5f;
                Vector3 right = (bottomRight + topRight) * 0.5f;
                Vector3 top = (topLeft + topRight) * 0.5f;
                Vector3 bottom = (bottomLeft + bottomRight) * 0.5f;

                Assert.That(right.x, Is.GreaterThan(left.x),
                    $"{label} glyph {index} must not be mirrored.");
                Assert.That(top.y, Is.GreaterThan(bottom.y),
                    $"{label} glyph {index} must not be upside down.");
                foreach (Vector3 corner in new[]
                         {
                             bottomLeft,
                             topLeft,
                             topRight,
                             bottomRight,
                         })
                {
                    Assert.That(corner.z, Is.GreaterThan(0f),
                        $"{label} glyph {index} must face the camera.");
                    Assert.That(corner.x, Is.InRange(0f, 1f),
                        $"{label} glyph {index} must stay on screen.");
                    Assert.That(corner.y, Is.InRange(0f, 1f),
                        $"{label} glyph {index} must stay on screen.");
                }
            }

            Assert.That(actualVisible, Is.EqualTo(expectedVisible),
                $"{label} must render every non-space character.");
        }
    }
}
