using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// Watches the first two seconds of the bar through the camera the
    /// player actually gets.
    ///
    /// This exists because the room passed every measurement it had -
    /// anchors, sizes, tints, renderer bounds, a folder of rendered
    /// frames - while the hero fell through its floor forever and the
    /// chase camera sat inside his skull. None of those checks could see
    /// it: `BarModelContractTests` measures geometry and never a
    /// collider, and `AreaCaptureFixture` photographs the room from
    /// invented camera poses with the hero's renderers switched off, so
    /// it can neither see him fall nor see where his camera ended up.
    ///
    /// So this fixture asserts the two things a player notices in his
    /// first second: he stands on the floor, and he can see himself.
    /// </summary>
    public sealed class BarInteriorSpawnPlayModeTests
    {
        private const float TimeoutSeconds = 20f;

        //  Long enough to outlast the arrival shot (1.35 s), which owns
        //  the camera while it plays and hands it back to the follow.
        private const float SettleSeconds = 2f;

        //  The chase camera wants 2.2 m indoors and gives that up to
        //  geometry behind the hero. Under a metre it is inside him.
        private const float MinimumCameraDistance = 1f;

        //  Keep the seated lens visibly above the 1.02 m counter silhouette.
        //  This is deliberately smaller than ordinary seated eye clearance,
        //  leaving authoring room without letting the timber bury the view.
        private const float MinimumEyeAboveCounter = 0.18f;

        //  Below this cosine the page is less than 17.5 degrees above an
        //  edge-on view. Text may still own valid meshes and screen bounds,
        //  but its glyphs collapse into an unreadable strip.
        private const float MinimumReadablePageFacing = 0.72f;
        private const float MinimumOverheadSurfaceFacing = 0.97f;
        private const float MaximumCameraPlanarOffset = 0.14f;
        private const float MaximumWipePenetration = 0.015f;
        private const float MaximumWipeGap = 0.03f;
        private const float MinimumVisibleWipeTravel = 0.04f;
        private const float MaximumWipeReturnError = 0.04f;
        private const float WipeSurfaceMargin = 0.02f;

        //  Keep the physical booklet clear of the shared bottom hint. The
        //  hint occupies BottomMargin + Height logical pixels; four pixels of
        //  breathing room prevent the paper from reading as part of the HUD.
        private static readonly float MenuSafeViewportBottom =
            (CounterMenuHintView.BottomMargin +
             CounterMenuHintView.Height + 4f) /
            RetroUiTheme.LogicalHeight;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GameSessionState.EnterBar("bar-spawn-test");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Scene bar = SceneManager.GetSceneByName(SceneIds.BarInterior);
            if (bar.IsValid() && bar.isLoaded)
            {
                Scene cleanup = SceneManager.CreateScene(
                    "Bar Spawn Test Cleanup");
                SceneManager.SetActiveScene(cleanup);
                AsyncOperation unload = SceneManager.UnloadSceneAsync(bar);
                while (unload != null && !unload.isDone)
                {
                    yield return null;
                }
            }

            GameSessionState.ClearRoute();
            yield return null;
        }

        [UnityTest]
        public IEnumerator
            ArrivingHero_KeepsHisFeetOnTheFloorAndHisCameraBehindHim()
        {
            BarInteriorRoot bar = null;
            yield return LoadBar(result => bar = result);

            Transform hero = bar.Player.GameObject.transform;
            float spawnHeight = bar.Layout.PlayerSpawn.y;

            float deadline = Time.realtimeSinceStartup + SettleSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                Assert.That(
                    hero.position.y,
                    Is.GreaterThan(spawnHeight - 0.5f),
                    $"the hero is falling through the bar's floor: he " +
                    $"spawned at y={spawnHeight:F2} and is now at " +
                    $"{hero.position}. The room's collision is not where " +
                    "its geometry is.");
                yield return null;
            }

            CharacterController controller =
                bar.Player.GameObject.GetComponent<CharacterController>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(
                controller.isGrounded,
                Is.True,
                "the hero never found the floor of the bar");
        }

        [UnityTest]
        public IEnumerator ArrivingHero_IsNotStandingInsideHisOwnCamera()
        {
            BarInteriorRoot bar = null;
            yield return LoadBar(result => bar = result);

            float deadline = Time.realtimeSinceStartup + SettleSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null, "the bar has no main camera");
            Assert.That(
                bar.ArrivalPresentation.IsPlaying,
                Is.False,
                "the arrival shot never handed the camera back");

            Transform hero = bar.Player.GameObject.transform;
            Vector3 head = hero.position + Vector3.up * 1.3f;
            float distance = Vector3.Distance(
                camera.transform.position,
                head);
            Assert.That(
                distance,
                Is.GreaterThan(MinimumCameraDistance),
                $"the camera sits {distance:F2} m from the hero's head - " +
                "inside him. Its collision probe is starting inside a " +
                "collider, so the chase distance collapsed to nothing.");
        }

        [UnityTest]
        public IEnumerator Bartender_WipeTouchesAndTravelsAcrossCounter()
        {
            BarInteriorRoot bar = null;
            yield return LoadBar(result => bar = result);
            bar.ArrivalPresentation.Skip();
            yield return null;

            BarBartenderPresentation bartender = bar.Bartender;
            Assert.That(bartender, Is.Not.Null);
            Assert.That(bartender.UsesOrdinaryRig, Is.True);
            Assert.That(
                bartender.CurrentClipKind,
                Is.EqualTo(BarBartenderClipKind.Wipe));
            Assert.That(
                bar.transform.InverseTransformPoint(
                    bartender.transform.position).y,
                Is.Zero.Within(0.001f),
                "The ordinary bartender must stand at the same ground level " +
                "for which CafeAttendantWipe was authored.");

            Transform counterTop = bar.Room.Find("Counter Top");
            Assert.That(counterTop, Is.Not.Null);
            Renderer counterRenderer = counterTop.GetComponent<Renderer>();
            Assert.That(counterRenderer, Is.Not.Null);

            SkinnedMeshRenderer towel = null;
            for (int index = 0;
                 index < bartender.Registry.RendererBindings.Count;
                 index++)
            {
                BarBartenderRendererBinding binding =
                    bartender.Registry.RendererBindings[index];
                if (binding != null &&
                    binding.RendererName == "ACC_ServiceTowel")
                {
                    towel = binding.Renderer as SkinnedMeshRenderer;
                    break;
                }
            }

            Assert.That(towel, Is.Not.Null);
            Assert.That(towel.enabled, Is.True);
            Assert.That(
                bartender.Registry.TryGetClip(
                    BarBartenderClipKind.Wipe,
                    out AnimationClip wipeClip,
                    out bool wipeLoops),
                Is.True);
            Assert.That(wipeLoops, Is.True);
            var scratch = new Mesh();
            try
            {
                AdvanceWipeToPhase(bartender, wipeClip.length, 0.12f);
                Vector3 first = MeasureWipeTowel(
                    towel,
                    scratch,
                    counterRenderer.bounds);
                AdvanceWipeToPhase(bartender, wipeClip.length, 0.28f);
                Vector3 second = MeasureWipeTowel(
                    towel,
                    scratch,
                    counterRenderer.bounds);
                AdvanceWipeToPhase(bartender, wipeClip.length, 0.44f);
                Vector3 third = MeasureWipeTowel(
                    towel,
                    scratch,
                    counterRenderer.bounds);

                first.y = 0f;
                second.y = 0f;
                third.y = 0f;
                Assert.That(
                    Vector3.Distance(first, second),
                    Is.GreaterThanOrEqualTo(MinimumVisibleWipeTravel),
                    "The first Wipe stroke is not visible across the top.");
                Assert.That(
                    Vector3.Distance(second, third),
                    Is.GreaterThanOrEqualTo(MinimumVisibleWipeTravel),
                    "The return Wipe stroke is not visible across the top.");
                Assert.That(
                    Vector3.Distance(first, third),
                    Is.LessThanOrEqualTo(MaximumWipeReturnError),
                    "CafeAttendantWipe does not return across the same patch.");
            }
            finally
            {
                Object.DestroyImmediate(scratch);
            }
        }

        [UnityTest]
        public IEnumerator
            SeatedCounterMenu_ClearsCounterAndFitsReadableViewport()
        {
            BarInteriorRoot bar = null;
            yield return LoadBar(result => bar = result);

            Assert.That(bar.ArrivalPresentation, Is.Not.Null);
            bar.ArrivalPresentation.Skip();

            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null, "the bar has no main camera");
            camera.aspect = 16f / 9f;

            BarCounterStation station = bar.CounterStation;
            Assert.That(station, Is.Not.Null);
            Assert.That(bar.CounterStations, Has.Count.EqualTo(4));
            Assert.That(station.Seat, Is.Not.Null);
            Assert.That(station.SeatView, Is.Not.Null);
            CounterSeatPlan seatPlan = station.Seat.Plan;
            Assert.That(seatPlan, Is.Not.Null);

            for (int index = 0; index < bar.CounterStations.Count; index++)
            {
                BarCounterStation available = bar.CounterStations[index];
                Assert.That(
                    available.Seat.Plan.ApproachWaypointCount,
                    Is.GreaterThan(0));
                bar.Player.Motor.Teleport(
                    available.Seat.Plan.ApproachWaypoints[0]);
                bar.Player.GameObject.transform.rotation =
                    available.Seat.Plan.EntryPose.RootRotation;
                Physics.SyncTransforms();
                float focusDeadline = Time.realtimeSinceStartup + 1f;
                while (Time.realtimeSinceStartup < focusDeadline &&
                       !ReferenceEquals(
                           bar.Player.Interactor.ActiveInteractable,
                           available))
                {
                    yield return null;
                }

                Assert.That(
                    bar.Player.Interactor.ActiveInteractable,
                    Is.SameAs(available),
                    $"free counter stool {index + 1} cannot be targeted " +
                    "from its authored approach lane");
                available.Interact(bar.Player.Interactor);

                PlayerAnimatedInteractionController interaction =
                    available.Seat.Controller;
                float positioningDeadline =
                    Time.realtimeSinceStartup + 2f;
                while (Time.realtimeSinceStartup < positioningDeadline &&
                       interaction.Phase ==
                           PlayerAnimatedInteractionPhase.Positioning)
                {
                    yield return null;
                }

                Assert.That(
                    interaction.Phase,
                    Is.EqualTo(PlayerAnimatedInteractionPhase.Entering),
                    $"free counter stool {index + 1} aborted before its " +
                    "seating animation");
                Assert.That(
                    bar.DrinkShop.IsOpen,
                    Is.False,
                    "the menu must wait until the hero is actually seated");
                Assert.That(available.Seat.Cancel(), Is.True);
                yield return null;
                Assert.That(
                    interaction.Phase,
                    Is.EqualTo(PlayerAnimatedInteractionPhase.Idle));
                Assert.That(bar.DrinkShop.IsOpen, Is.False);
            }

            Vector3 reportedBlockedPosition = bar.Room.TransformPoint(
                new Vector3(-1.15f, 0f, 3.93f));
            reportedBlockedPosition.y = seatPlan.EntryPose.RootPosition.y;
            bar.Player.Motor.Teleport(reportedBlockedPosition);
            bar.Player.GameObject.transform.rotation =
                seatPlan.EntryPose.RootRotation;
            Physics.SyncTransforms();
            Assert.That(
                station.CanInteract(bar.Player.Interactor),
                Is.True,
                "the reported blocked position cannot start the counter seat");
            station.Interact(bar.Player.Interactor);

            float deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline &&
                   (!station.Seat.IsSeated ||
                    bar.DrinkShop.MenuState !=
                    BarPromenade.Runtime.World.CounterMenuState.Open ||
                    !station.SeatView.IsMenuFocusComplete))
            {
                yield return null;
            }

            Assert.That(station.Seat.IsSeated, Is.True);
            Assert.That(
                bar.Player.Motor.InteractionPoseMoveStalled,
                Is.False,
                "the safe entry dock still collides with the stool");
            Assert.That(
                bar.DrinkShop.MenuState,
                Is.EqualTo(
                    BarPromenade.Runtime.World.CounterMenuState.Open));
            Assert.That(station.SeatView.IsMenuFocusComplete, Is.True);
            // WaitForEndOfFrame is never pumped by Unity's headless runner;
            // one ordinary update is sufficient because TMP is forced below.
            yield return null;

            Transform counterTop = bar.Room.Find("Counter Top");
            Assert.That(
                counterTop,
                Is.Not.Null,
                "the authored room has no main counter-top renderer");
            Renderer counterTopRenderer =
                counterTop.GetComponent<Renderer>();
            Assert.That(counterTopRenderer, Is.Not.Null);
            float counterSurfaceY = counterTopRenderer.bounds.max.y;

            Vector3 plannedSeatedCamera =
                seatPlan.ActionHipPosition +
                seatPlan.CameraOffsetFromActionHip;
            float plannedEyeClearance =
                plannedSeatedCamera.y - counterSurfaceY;
            float focusedEyeClearance =
                camera.transform.position.y - counterSurfaceY;

            BarDrinkMenuPresentation menu =
                bar.DrinkShop.MenuPresentation;
            Assert.That(menu, Is.Not.Null);
            Assert.That(menu.IsPlaced, Is.True);
            Assert.That(menu.IsTextVisible, Is.True);
            Assert.That(
                menu.ItemLines.Count,
                Is.EqualTo(BarServicePropFactory.MenuItemCount));

            Vector3 menuInRoom = bar.Room.InverseTransformPoint(
                menu.PropRoot.position);
            Vector3 heroInRoom = bar.Room.InverseTransformPoint(
                seatPlan.ActionHipPosition);
            Assert.That(
                menuInRoom.x,
                Is.EqualTo(heroInRoom.x).Within(0.01f),
                "the bartender did not put the menu directly before the " +
                "seated hero");

            BarServicePropInstance prop =
                menu.PropRoot.GetComponent<BarServicePropInstance>();
            Assert.That(prop, Is.Not.Null);
            Assert.That(
                prop.TryGetAnchor(
                    BarServicePropFactory.MenuPageOriginRole,
                    out Transform pageOrigin),
                Is.True);
            Assert.That(
                prop.TryGetAnchor(
                    BarServicePropFactory.MenuPageNormalRole,
                    out Transform pageNormal),
                Is.True);
            Assert.That(
                prop.TryGetAnchor(
                    BarServicePropFactory.MenuTextItemRole(0),
                    out Transform firstRow),
                Is.True);
            Vector3 authoredPageNormal =
                (pageNormal.position - pageOrigin.position).normalized;
            Vector3 cameraFromPage =
                camera.transform.position - pageOrigin.position;
            float overheadSurfaceFacing = Vector3.Dot(
                authoredPageNormal,
                cameraFromPage.normalized);
            float cameraPlanarOffset = Vector3.ProjectOnPlane(
                cameraFromPage,
                authoredPageNormal).magnitude;
            float closestMenuDistance = float.PositiveInfinity;
            for (int index = 0; index < prop.Renderers.Count; index++)
            {
                Renderer renderer = prop.Renderers[index];
                Assert.That(renderer, Is.Not.Null);
                closestMenuDistance = Mathf.Min(
                    closestMenuDistance,
                    Mathf.Sqrt(renderer.bounds.SqrDistance(
                        camera.transform.position)));
            }

            float minimumPageFacing = 1f;
            float minimumRenderedFontSize = float.PositiveInfinity;
            Vector3 firstPageOutward = Vector3.zero;
            Vector3 firstTowardCamera = Vector3.zero;
            for (int index = 0; index < menu.ItemLines.Count; index++)
            {
                TMPro.TMP_Text line = menu.ItemLines[index];
                Assert.That(line, Is.Not.Null);
                line.ForceMeshUpdate();
                Assert.That(
                    line.isTextOverflowing,
                    Is.False,
                    $"bar menu row {index + 1} is truncated");
                Assert.That(
                    line.enableAutoSizing,
                    Is.False,
                    $"bar menu row {index + 1} may not shrink independently");
                minimumRenderedFontSize = Mathf.Min(
                    minimumRenderedFontSize,
                    line.fontSize);
                Vector3 outward = -line.transform.forward.normalized;
                Vector3 towardCamera =
                    (camera.transform.position -
                     line.transform.position).normalized;
                if (index == 0)
                {
                    firstPageOutward = outward;
                    firstTowardCamera = towardCamera;
                }

                minimumPageFacing = Mathf.Min(
                    minimumPageFacing,
                    Vector3.Dot(outward, towardCamera));
            }

            ViewportEnvelope viewport = MeasureMenuViewport(
                camera,
                prop.Renderers,
                menu.ItemLines,
                menu.SelectionMarker);
            Debug.Log(
                $"Bar counter-menu visual contract: counterTopY=" +
                $"{counterSurfaceY:F3}, seatedClearance=" +
                $"{plannedEyeClearance:F3}, focusedClearance=" +
                $"{focusedEyeClearance:F3}, closestMenu=" +
                $"{closestMenuDistance:F3}, pageFacing=" +
                $"{minimumPageFacing:F3}, firstOutward=" +
                $"{firstPageOutward:F3}, firstTowardCamera=" +
                $"{firstTowardCamera:F3}, minFont=" +
                $"{minimumRenderedFontSize:F3}, camera=" +
                $"{camera.transform.position:F3}, authoredPageNormal=" +
                $"{authoredPageNormal:F3}, overheadFacing=" +
                $"{overheadSurfaceFacing:F3}, planarOffset=" +
                $"{cameraPlanarOffset:F3}, rowAxes=" +
                $"{firstRow.right:F3}/{firstRow.up:F3}/" +
                $"{firstRow.forward:F3}, viewport={viewport}.");

            Assert.That(
                plannedEyeClearance,
                Is.GreaterThanOrEqualTo(MinimumEyeAboveCounter),
                "the authored seated lens is below/inside the visual " +
                "counter edge");
            Assert.That(
                focusedEyeClearance,
                Is.GreaterThanOrEqualTo(MinimumEyeAboveCounter),
                "menu focus lowers the real camera into the counter");
            Assert.That(
                closestMenuDistance,
                Is.GreaterThanOrEqualTo(MinimumEyeAboveCounter),
                "one edge of the booklet is too close to the lens");
            Assert.That(
                minimumPageFacing,
                Is.GreaterThanOrEqualTo(MinimumReadablePageFacing),
                "the camera sees the menu text almost edge-on");
            Assert.That(
                minimumRenderedFontSize,
                Is.EqualTo(CounterMenuPageStyle.Bar.ItemFontSize)
                    .Within(0.0001f),
                "the descriptive bar menu does not use one fixed type size");
            Assert.That(
                overheadSurfaceFacing,
                Is.GreaterThanOrEqualTo(MinimumOverheadSurfaceFacing),
                "the camera does not hang steeply enough over the menu");
            Assert.That(
                cameraPlanarOffset,
                Is.LessThanOrEqualTo(MaximumCameraPlanarOffset),
                "the camera projection lands beyond the menu footprint");

            Volume cinematicVolume = FindCinematicDepthOfFieldVolume();
            Assert.That(cinematicVolume, Is.Not.Null);
            Assert.That(
                cinematicVolume.profile.TryGet(
                    out DepthOfField cinematicDepthOfField),
                Is.True);
            Assert.That(
                cinematicDepthOfField.focusDistance.value,
                Is.EqualTo(Vector3.Distance(
                        camera.transform.position,
                        menu.CameraFocusWorldPosition))
                    .Within(0.01f),
                "the close-up DOF is focused behind the menu");
            Assert.That(
                cinematicDepthOfField.aperture.value,
                Is.EqualTo(
                    BarDrinkShopController.CounterMenuDepthOfFieldAperture),
                "the menu close-up uses the restrained indoor aperture");
            Assert.That(
                cinematicDepthOfField.focalLength.value,
                Is.EqualTo(
                    BarDrinkShopController.CounterMenuDepthOfFieldFocalLength),
                "the menu close-up uses the restrained indoor focal length");
            Assert.That(viewport.HasPoints, Is.True);
            Assert.That(
                viewport.MinimumDepth,
                Is.GreaterThan(camera.nearClipPlane + 0.02f),
                "part of the menu lies on the near clip plane");
            Assert.That(
                viewport.MinimumX,
                Is.GreaterThanOrEqualTo(0.03f),
                "the menu is cropped at the left edge");
            Assert.That(
                viewport.MaximumX,
                Is.LessThanOrEqualTo(0.97f),
                "the menu is cropped at the right edge");
            Assert.That(
                viewport.MinimumY,
                Is.GreaterThanOrEqualTo(MenuSafeViewportBottom),
                "the menu runs under the bottom controls/status hint");
            Assert.That(
                viewport.MaximumY,
                Is.LessThanOrEqualTo(0.95f),
                "the menu is cropped at the top edge");

            PlayerCameraFollow cameraFollow =
                station.SeatView.CameraFollow;
            Assert.That(cameraFollow, Is.Not.Null);
            var heroPresentation =
                bar.Player.Visual as Player3DCharacterPresentation;
            Assert.That(heroPresentation, Is.Not.Null);
            Assert.That(heroPresentation.Registry, Is.Not.Null);
            seatPlan.EvaluateCamera(
                heroPresentation.Registry.Anchors.Pelvis.position,
                0f,
                0f,
                out Vector3 returnedPosition,
                out Quaternion returnedRotation);
            Assert.That(
                Vector3.Distance(
                    cameraFollow.FixedBasePosition,
                    returnedPosition),
                Is.GreaterThan(0.15f),
                "The menu close-up is not visibly closer than the seated " +
                "view.");
            Assert.That(
                cameraFollow.FixedBaseFieldOfView,
                Is.EqualTo(BarDrinkMenuPresentation.CameraFocusFieldOfView)
                    .Within(0.001f));

            Assert.That(bar.DrinkShop.RestPhysicalMenuAtCounter(), Is.True);
            deadline = Time.realtimeSinceStartup + 2f;
            while (station.SeatView.IsMenuFocusLocked &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                station.SeatView.IsMenuFocusLocked,
                Is.False,
                "Closing the menu never released its camera focus.");
            Assert.That(
                station.SeatView.MenuFocusWeight,
                Is.Zero.Within(0.0001f));
            Assert.That(
                Vector3.Distance(
                    cameraFollow.FixedBasePosition,
                    returnedPosition),
                Is.LessThan(0.002f),
                "The camera did not return to the seated pose.");
            Assert.That(
                Quaternion.Angle(
                    cameraFollow.FixedBaseRotation,
                    returnedRotation),
                Is.LessThan(0.1f));
            Assert.That(
                cameraFollow.FixedBaseFieldOfView,
                Is.EqualTo(seatPlan.CameraFieldOfView).Within(0.001f));
        }

        private static ViewportEnvelope MeasureMenuViewport(
            Camera camera,
            IReadOnlyList<Renderer> physicalRenderers,
            IReadOnlyList<TMPro.TMP_Text> lines,
            TMPro.TMP_Text marker)
        {
            var envelope = new ViewportEnvelope();
            for (int index = 0; index < physicalRenderers.Count; index++)
            {
                Renderer renderer = physicalRenderers[index];
                IncludeLocalBounds(
                    camera,
                    envelope,
                    renderer.transform,
                    renderer.localBounds);
            }

            for (int index = 0; index < lines.Count; index++)
            {
                TMPro.TMP_Text line = lines[index];
                IncludeLocalBounds(
                    camera,
                    envelope,
                    line.transform,
                    line.textBounds);
            }

            Assert.That(marker, Is.Not.Null);
            marker.ForceMeshUpdate();
            IncludeLocalBounds(
                camera,
                envelope,
                marker.transform,
                marker.textBounds);
            return envelope;
        }

        private static Vector3 MeasureWipeTowel(
            SkinnedMeshRenderer towel,
            Mesh scratch,
            Bounds counter)
        {
            scratch.Clear();
            towel.BakeMesh(scratch, true);
            Vector3[] vertices = scratch.vertices;
            Assert.That(vertices, Is.Not.Empty);
            Matrix4x4 localToWorld = towel.localToWorldMatrix;
            float lowest = float.PositiveInfinity;
            Vector3 centre = Vector3.zero;
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 world = localToWorld.MultiplyPoint3x4(vertices[index]);
                lowest = Mathf.Min(lowest, world.y);
                centre += world;
            }

            centre /= vertices.Length;
            float gap = lowest - counter.max.y;
            Assert.That(
                gap,
                Is.InRange(-MaximumWipePenetration, MaximumWipeGap),
                $"The towel is wiping air ({gap:F3} m from the counter). ");
            Assert.That(
                centre.x,
                Is.InRange(
                    counter.min.x - WipeSurfaceMargin,
                    counter.max.x + WipeSurfaceMargin),
                "The towel left the counter width.");
            Assert.That(
                centre.z,
                Is.InRange(
                    counter.min.z - WipeSurfaceMargin,
                    counter.max.z + WipeSurfaceMargin),
                "The towel left the counter depth.");
            return centre;
        }

        private static void AdvanceWipeToPhase(
            BarBartenderPresentation bartender,
            float clipLength,
            float normalizedPhase)
        {
            float current = Mathf.Repeat(
                bartender.CurrentClipTimeSeconds,
                clipLength);
            float target = normalizedPhase * clipLength;
            float delta = target - current;
            if (delta < 0f)
            {
                delta += clipLength;
            }

            bartender.Advance(delta);
        }

        private static void IncludeLocalBounds(
            Camera camera,
            ViewportEnvelope envelope,
            Transform transform,
            Bounds bounds)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 local = new Vector3(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z);
                        envelope.Include(
                            camera.WorldToViewportPoint(
                                transform.TransformPoint(local)));
                    }
                }
            }
        }

        private static Volume FindCinematicDepthOfFieldVolume()
        {
            Volume[] volumes = Resources.FindObjectsOfTypeAll<Volume>();
            for (int index = 0; index < volumes.Length; index++)
            {
                if (volumes[index] != null &&
                    volumes[index].name == "Cinematic Depth Of Field")
                {
                    return volumes[index];
                }
            }

            return null;
        }

        private sealed class ViewportEnvelope
        {
            public bool HasPoints { get; private set; }
            public float MinimumX { get; private set; } =
                float.PositiveInfinity;
            public float MaximumX { get; private set; } =
                float.NegativeInfinity;
            public float MinimumY { get; private set; } =
                float.PositiveInfinity;
            public float MaximumY { get; private set; } =
                float.NegativeInfinity;
            public float MinimumDepth { get; private set; } =
                float.PositiveInfinity;

            public void Include(Vector3 viewportPoint)
            {
                HasPoints = true;
                MinimumX = Mathf.Min(MinimumX, viewportPoint.x);
                MaximumX = Mathf.Max(MaximumX, viewportPoint.x);
                MinimumY = Mathf.Min(MinimumY, viewportPoint.y);
                MaximumY = Mathf.Max(MaximumY, viewportPoint.y);
                MinimumDepth = Mathf.Min(
                    MinimumDepth,
                    viewportPoint.z);
            }

            public override string ToString()
            {
                return $"x={MinimumX:F3}..{MaximumX:F3}, " +
                       $"y={MinimumY:F3}..{MaximumY:F3}, " +
                       $"zMin={MinimumDepth:F3}";
            }
        }

        private static IEnumerator LoadBar(
            System.Action<BarInteriorRoot> onReady)
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(
                SceneIds.BarInterior,
                LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
            {
                yield return null;
            }

            BarInteriorRoot bar = null;
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                bar = Object.FindAnyObjectByType<BarInteriorRoot>();
                if (bar != null && bar.IsInitialized)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(
                bar,
                Is.Not.Null,
                "the bar interior never built its root");
            Assert.That(bar.IsInitialized, Is.True);
            Assert.That(bar.Player, Is.Not.Null);
            onReady(bar);
        }
    }
}
