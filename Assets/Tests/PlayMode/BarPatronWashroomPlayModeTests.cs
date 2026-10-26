using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    [PrebuildSetup(typeof(CompactBarAssetsSetup))]
    public sealed class BarPatronWashroomPlayModeTests
    {
        private static readonly BarNpcRole[] Places =
            { BarNpcRole.CounterPatron, BarNpcRole.SeatedPatron, BarNpcRole.StandingPatron };
        private BarInteriorRoot bar;
        private BarBartenderServiceChoreography bartender;
        private Camera camera;
        private BarCleanupReviewCamera reviewCamera;
        private IDisposable pause;
        private Player3DHeadVisibility occupiedViewHead;
        private float previousCaptureDelta;
        private readonly List<string> visitTrace = new List<string>(64);
        private string sampledPhase, lastVisitPhase, lastVisitBlocker;
        private Vector3 lastVisitPosition, lastVisitTarget;
        private float lastDoorFraction;

        [SetUp]
        public void SetUp()
        {
            previousCaptureDelta = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            visitTrace.Clear();
            sampledPhase = lastVisitPhase = lastVisitBlocker = string.Empty;
            lastVisitPosition = lastVisitTarget = Vector3.zero;
            lastDoorFraction = 0f;
            GameSessionState.BeginNewGame("bar-washroom-visit-test");
            BarTestTime.StartFullAttendance("washroom-visits");
            GameSessionState.EnterBar("washroom-visits", BarActivityKind.Cocktail);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            pause?.Dispose();
            pause = null;
            occupiedViewHead?.Restore();
            occupiedViewHead = null;
            yield return AreaCaptureFixture.ReleaseBarService();
            Scene scene = SceneManager.GetSceneByName(SceneIds.BarInterior);
            if (scene.IsValid() && scene.isLoaded)
            {
                Scene exit = SceneManager.CreateScene("Bar Washroom Fixture Exit");
                SceneManager.SetActiveScene(exit);
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            GameSessionState.BeginNewGame("bar-washroom-visit-test-exit");
            Time.captureDeltaTime = previousCaptureDelta;
            bar = null;
            bartender = null;
            camera = null;
            reviewCamera = null;
            yield return null;
            Assert.That(Object.FindObjectsByType<BarCrowdController>(FindObjectsSortMode.None), Is.Empty);
            Assert.That(Object.FindObjectsByType<BarWashroomAccess>(FindObjectsSortMode.None), Is.Empty);
            Assert.That(Object.FindObjectsByType<BarAisleTraffic>(FindObjectsSortMode.None), Is.Empty);
        }

        [Test]
        public void Schedule_IsRareSeededAndAllowsOnlyOneVisitor()
        {
            var schedule = new BarWashroomSchedule(27, 8);
            var repeat = new BarWashroomSchedule(27, 8);
            for (int slot = 0; slot < 3; slot++)
            {
                schedule.SetResident(slot, true);
                repeat.SetResident(slot, true);
            }
            Assert.That(schedule.ChooseCandidate(), Is.EqualTo(-1));
            schedule.Advance(119f, true);
            repeat.Advance(119f, true);
            Assert.That(schedule.ChooseCandidate(), Is.EqualTo(-1));
            schedule.Advance(122f, true);
            repeat.Advance(122f, true);
            int selected = schedule.ChooseCandidate();
            Assert.That(selected, Is.InRange(0, 2));
            Assert.That(repeat.ChooseCandidate(), Is.EqualTo(selected), "The same visit seed has the same bounded cadence.");
            Assert.That(schedule.BeginVisit(selected), Is.True);
            Assert.That(schedule.VisitDurationSeconds, Is.InRange(25f, 45f));
            Assert.That(schedule.ChooseCandidate(), Is.EqualTo(-1));
            Assert.That(schedule.BeginVisit((selected + 1) % 3), Is.False);
            schedule.CompleteVisit(selected);
            Assert.That(schedule.SecondsUntilNextVisit, Is.InRange(240f, 420f));
            Assert.That(schedule.CanVisit(selected), Is.False, "One guest cannot immediately repeat.");
            double active = schedule.ActiveSeconds;
            schedule.Advance(1000f, false);
            Assert.That(schedule.ActiveSeconds, Is.EqualTo(active), "A closed bar cannot age the visit scheduler.");
            Assert.That(schedule.ChooseCandidate(), Is.EqualTo(-1));
            schedule.Advance(421f, true);
            Assert.That(schedule.CanVisit(selected), Is.False, "The personal cooldown exceeds the shared interval.");
            Assert.That(schedule.ChooseCandidate(), Is.InRange(0, 2));
        }

        [UnityTest]
        public IEnumerator ActualGuest_UsesOccupiedWashroomAndReturnsToSameDrink(
            [ValueSource(nameof(Places))] BarNpcRole place)
        {
            Assert.That(Application.isBatchMode, Is.False,
                "The washroom visit contract reviews actual Game view frames.");
            yield return SceneManager.LoadSceneAsync(SceneIds.BarInterior, LoadSceneMode.Single);
            float deadline = Time.realtimeSinceStartup + 20f;
            do
            {
                bar = Object.FindAnyObjectByType<BarInteriorRoot>();
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The production bar did not initialize.");
                if (bar == null || !bar.IsInitialized) yield return null;
            } while (bar == null || !bar.IsInitialized);
            bar.ArrivalPresentation?.Skip();
            bar.Crowd.AutoAdvance = false;
            bar.Crowd.AutoScheduleWashroom = false;
            bar.Conversation.AutoAdvance = false;
            bar.Conversation.enabled = false;
            foreach (BarPatronAnimator animator in bar.GetComponentsInChildren<BarPatronAnimator>(true)) animator.enabled = false;
            bartender = bar.Bartender.GetComponent<BarBartenderServiceChoreography>();
            bartender.AutoAdvance = false;
            camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            camera.GetComponent<PlayerCameraFollow>().enabled = false;
            reviewCamera = camera.gameObject.AddComponent<BarCleanupReviewCamera>();
            reviewCamera.TargetCamera = camera;
            StageRoomCamera();

            BarPatron guest = null;
            foreach (BarPatron patron in bar.Patrons)
            {
                SetDrinkPhase(patron.Drinking.Timeline, BarPatronDrinkPhase.Rest);
                patron.Drinking.enabled = false;
                patron.Presentation.Advance(0f, false, true);
                patron.Drinking.SampleFinalPose();
                if (guest == null && patron.Anchor.Role == place) guest = patron;
            }
            Assert.That(guest, Is.Not.Null, place.ToString());
            BarPatron otherGuest = null;
            foreach (BarPatron patron in bar.Patrons)
                if (!ReferenceEquals(patron, guest)) { otherGuest = patron; break; }
            Assert.That(otherGuest, Is.Not.Null);
            int originalCount = bar.Patrons.Count;
            Transform originalActor = guest.Registry.transform;
            Transform originalModel = guest.Registry.ModelRoot;
            BarPatronDrinkProp prop = guest.Drinking.DrinkProp;
            Transform originalVessel = prop.Root;
            if (prop.Vessel != null) prop.Vessel.SetFillProgress(.37f);
            float fill = prop.Vessel != null ? prop.Vessel.FillProgress : 0f;
            Pose drinkRest = new Pose(originalVessel.position, originalVessel.rotation);
            Vector3 actorRest = originalActor.position;
            Renderer surface = guest.Drinking.SurfaceRenderer;
            Assert.That(bar.Crowd.TryBeginWashroomVisit(guest.Anchor.Id), Is.False,
                "A newly arrived guest does not immediately head to the washroom.");
            bar.Crowd.Advance(241f);
            Assert.That(bar.Crowd.HasWashroomVisitor, Is.False, "Manual scheduling cannot silently choose another guest.");
            Vector3 heroHome = bar.Player.GameObject.transform.position;
            Quaternion heroHomeRotation = bar.Player.GameObject.transform.rotation;
            bar.Player.Motor.Teleport(bar.Room.TransformPoint(new Vector3(-3.1f,
                PlayerFactory.GroundedRootOffset, 4.8f)));
            Physics.SyncTransforms();
            Assert.That(bar.Washroom.Access.IsHeroInside, Is.True);
            Assert.That(bar.Crowd.TryBeginWashroomVisit(guest.Anchor.Id), Is.False,
                "A hero already inside has priority over every scheduled NPC.");
            bar.Player.Motor.Teleport(heroHome);
            Physics.SyncTransforms();
            Assert.That(bar.Washroom.Access.TryReserveNpc(otherGuest.Registry.gameObject,
                otherGuest.Registry.transform), Is.True);
            Assert.That(bar.Crowd.TryBeginWashroomVisit(guest.Anchor.Id), Is.False,
                "Another existing guest's room reservation prevents the event from starting.");
            Assert.That(bar.Crowd.HasWashroomVisitor, Is.False);
            Assert.That(originalActor.position, Is.EqualTo(actorRest));
            Assert.That(guest.IsTemporarilyAway, Is.False);
            AssertRetainedDrink(prop, originalVessel, surface, drinkRest, fill);
            Assert.That(bar.Washroom.Access.NpcOwner, Is.SameAs(otherGuest.Registry.gameObject));
            Assert.That(bar.Washroom.Access.ReleaseNpc(otherGuest.Registry.gameObject), Is.True);
            Assert.That(bar.AisleTraffic.TryAcquire(bartender), Is.True);
            Assert.That(bar.Crowd.TryBeginWashroomVisit(guest.Anchor.Id), Is.False,
                "The guest waits at its place while the actual bartender owns the narrow route.");
            Assert.That(bar.Washroom.Access.IsNpcReserved, Is.False,
                "A failed aisle reservation must release the unused room reservation.");
            Assert.That(originalActor.position, Is.EqualTo(actorRest));
            Assert.That(bar.AisleTraffic.Release(bartender), Is.True);
            Assert.That(bar.Crowd.TryBeginWashroomVisit(guest.Anchor.Id), Is.True, Diagnostic());
            Assert.That(bar.Crowd.WashroomPatron, Is.SameAs(guest));
            Assert.That(bar.Patrons.Count, Is.EqualTo(originalCount), "A washroom visit preserves attendance.");
            Assert.That(bar.Crowd.TryGetAbandonedDrink(guest.Anchor.Id, out _), Is.False,
                "Temporary absence never hands a live guest's vessel to the collector.");
            yield return CompileActualForwardPasses();
            if (place == BarNpcRole.SeatedPatron)
            {
                yield return DriveUntil(() => bar.Crowd.WashroomPhaseName == "Approaching", 150,
                    "The booth guest did not physically stand before approaching WC.");
                bar.Player.Motor.Teleport(bar.Room.TransformPoint(new Vector3(-3.1f,
                    PlayerFactory.GroundedRootOffset, 4.8f)));
                Physics.SyncTransforms();
                yield return DriveUntil(() => !bar.Crowd.HasWashroomVisitor, 250,
                    "A hero who entered first must make the approaching guest return safely.");
                Assert.That(bar.Crowd.CompletedWashroomVisits, Is.Zero);
                Assert.That(bar.Washroom.Access.IsNpcReserved, Is.False);
                Assert.That(bar.Washroom.Access.IsLocked, Is.False);
                Assert.That(Vector3.Distance(originalActor.position, actorRest), Is.LessThan(.02f));
                AssertRetainedDrink(prop, originalVessel, surface, drinkRest, fill);
                bar.Player.Motor.Teleport(heroHome);
                Physics.SyncTransforms();
                bar.Crowd.Advance(121f);
                Assert.That(bar.Crowd.TryBeginWashroomVisit(guest.Anchor.Id), Is.True,
                    "The postponed guest tries later, through its ordinary cooldown and real route.");
            }

            bool sawHallWalk = false, sawInside = false, sawOccupied = false, sawReturn = false;
            bool pauseChecked = false, lockChecked = false, exitPriorityChecked = false, closedDuringVisit = false;
            deadline = Time.realtimeSinceStartup + 35f;
            for (int step = 0; step < 2400 && bar.Crowd.HasWashroomVisitor; step++)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), Diagnostic());
                Vector3 before = originalActor.position;
                Advance(.1f);
                Assert.That(Vector3.Distance(before, originalActor.position), Is.LessThan(.30f),
                    "The actual guest cannot jump between its place and the washroom." + Diagnostic());
                Assert.That(guest.Registry.ModelRoot, Is.SameAs(originalModel));
                Assert.That(bar.Patrons.Count, Is.EqualTo(originalCount));
                Assert.That(bar.Crowd.IsAnchorOccupied(guest.Anchor.Id), Is.True);
                Assert.That(bar.Crowd.TryGetAbandonedDrink(guest.Anchor.Id, out _), Is.False);
                AssertRetainedDrink(prop, originalVessel, surface, drinkRest, fill);
                string phase = bar.Crowd.WashroomPhaseName;
                if (phase == "Approaching" || phase == "ReturningToDoor" || phase == "Returning")
                    AssertWalkingBodyClear(originalActor);
                if (bar.Crowd.HasWashroomVisitor)
                {
                    Assert.That(guest.IsTemporarilyAway, Is.True);
                    Assert.That(originalVessel.IsChildOf(originalActor), Is.False,
                        "The visiting guest walks with empty hands while the real vessel stays supported.");
                }
                Vector3 local = bar.Room.InverseTransformPoint(originalActor.position);
                bool inside = BarWashroomLayout.Contains(BarWashroomLayout.Bounds, local, .1f);
                if (!sawHallWalk && Vector3.Distance(originalActor.position, actorRest) > 1f && !inside)
                {
                    sawHallWalk = true;
                    StageRoomCamera();
                    yield return Capture(place, "walk-empty-handed");
                }
                if (inside) sawInside = true;
                if (phase == "Occupied") sawOccupied = true;
                if (bar.Washroom.Access.IsLocked && !lockChecked)
                {
                    lockChecked = true;
                    Assert.That(bar.Washroom.Access.IsNpcReserved, Is.True);
                    Assert.That(bar.Washroom.Access.NpcActor, Is.SameAs(originalActor));
                    Assert.That(bar.Washroom.Door.IsClosed, Is.True);
                    Assert.That(inside, Is.True, "The real leaf locks only after the guest has entered.");
                    Assert.That(bar.Crowd.TryBeginWashroomVisit(otherGuest.Anchor.Id), Is.False,
                        "Another guest cannot start a second event while this visitor occupies WC.");
                    Assert.That(bar.Crowd.WashroomPatron, Is.SameAs(guest));
                    Assert.That(bar.Washroom.Door.PromptKey, Is.EqualTo(BarWashroomAccess.OccupiedKey));
                    Assert.That(bar.Washroom.Door.CanInteract(bar.Player.Interactor), Is.True,
                        "The occupied leaf remains discoverable so its silent status can be displayed.");
                    Assert.That(bar.Washroom.Door.Begin(), Is.False);
                    bar.Player.Motor.Teleport(bar.Washroom.Door.GroundedOutsideDock);
                    Vector3 doorFacing = bar.Washroom.Door.InteractionPosition - bar.Player.GameObject.transform.position;
                    doorFacing.y = 0f;
                    bar.Player.GameObject.transform.rotation = Quaternion.LookRotation(doorFacing, Vector3.up);
                    Physics.SyncTransforms();
                    occupiedViewHead = Player3DHeadVisibility.Hide(
                        bar.Player.GameObject.GetComponentInChildren<Player3DAssetRegistry>());
                    StageOccupiedDoorCamera();
                    yield return null;
                    Assert.That(bar.Player.Interactor.ActiveInteractable, Is.SameAs(bar.Washroom.Door),
                        "The real nearby locked leaf supplies the occupied prompt.");
                    Assert.That(bar.Player.Interactor.PromptView.PromptKey, Is.EqualTo(BarWashroomAccess.OccupiedKey));
                    yield return Capture(place, "occupied-door");
                    Assert.That(bar.Player.Interactor.PromptView.HasRenderedLayout, Is.True);
                    Assert.That(bar.Player.Interactor.PromptView.LastRenderedText, Does.Contain("Занято"),
                        "The occupied state must actually appear in the rendered shared interface.");
                    occupiedViewHead.Restore();
                    occupiedViewHead = null;
                    bar.Player.Motor.Teleport(heroHome);
                    bar.Player.GameObject.transform.rotation = heroHomeRotation;
                    Physics.SyncTransforms();
                    StageRoomCamera();
                    Pose held = new Pose(originalActor.position, originalActor.rotation);
                    bar.Crowd.enabled = false;
                    Advance(2f);
                    yield return null;
                    Assert.That(originalActor.position, Is.EqualTo(held.position));
                    Assert.That(bar.Washroom.Access.IsLocked, Is.True,
                        "Disabling advancement preserves physical occupancy and cannot unlock an occupied room.");
                    bar.Crowd.enabled = true;

                    // Even external recovery placement must never strand the hero behind an occupied lock.
                    bar.Player.Motor.Teleport(bar.Room.TransformPoint(new Vector3(-3.1f,
                        PlayerFactory.GroundedRootOffset, 4.8f)));
                    Physics.SyncTransforms();
                    Assert.That(bar.Washroom.Door.PromptKey, Is.Not.EqualTo(BarWashroomAccess.OccupiedKey));
                    Assert.That(bar.Washroom.Door.CanInteract(bar.Player.Interactor), Is.True,
                        "Inside egress remains available regardless of the outside lock.");
                    bar.Player.Motor.Teleport(heroHome);
                    bar.Player.GameObject.transform.rotation = heroHomeRotation;
                    Physics.SyncTransforms();
                    if (place == BarNpcRole.StandingPatron)
                        Assert.That(bar.AisleTraffic.TryAcquire(bartender), Is.True);
                }
                if (phase == "Occupied" && !pauseChecked)
                {
                    pauseChecked = true;
                    pause = GameTimeScaleRuntime.AcquirePause();
                    yield return null;
                    Pose held = new Pose(originalActor.position, originalActor.rotation);
                    string heldPhase = bar.Crowd.WashroomPhaseName;
                    float heldElapsed = bar.Crowd.WashroomElapsedActiveSeconds;
                    Advance(15f);
                    yield return null;
                    Assert.That(originalActor.position, Is.EqualTo(held.position));
                    Assert.That(Quaternion.Angle(originalActor.rotation, held.rotation), Is.LessThan(.001f));
                    Assert.That(bar.Crowd.WashroomPhaseName, Is.EqualTo(heldPhase));
                    Assert.That(bar.Crowd.WashroomElapsedActiveSeconds, Is.EqualTo(heldElapsed));
                    pause.Dispose();
                    pause = null;
                }
                if (place == BarNpcRole.CounterPatron && !closedDuringVisit && phase == "Occupied" &&
                    bar.Crowd.WashroomPhaseSeconds > 2f)
                {
                    AdvanceClockTo(BarBusinessHours.ClosingMinute);
                    closedDuringVisit = true;
                }
                if (place == BarNpcRole.StandingPatron && !exitPriorityChecked &&
                    bar.Crowd.WashroomPhaseName == "WaitingToExit" && bar.Crowd.WashroomPhaseSeconds > .1f)
                {
                    exitPriorityChecked = true;
                    Assert.That(bar.AisleTraffic.CurrentOwner, Is.SameAs(bartender));
                    Assert.That(bar.AisleTraffic.ReturningOwner, Is.SameAs(originalActor.gameObject));
                    Assert.That(bar.Washroom.Access.IsLocked, Is.True,
                        "The guest waits safely inside until the worker clears the route.");
                    Assert.That(bar.AisleTraffic.HasPriorityWaiter(bartender), Is.True);
                    Assert.That(bar.AisleTraffic.Release(bartender), Is.True);
                    Assert.That(bar.AisleTraffic.TryAcquire(bartender), Is.False,
                        "The guest returning from WC has priority over the worker's next trip.");
                }
                if (sawInside && !inside && !sawReturn)
                {
                    sawReturn = true;
                    StageRoomCamera();
                }
                yield return null;
            }
            Assert.That(bar.Crowd.HasWashroomVisitor, Is.False, Diagnostic());
            Assert.That(sawHallWalk && sawInside && sawOccupied && sawReturn && lockChecked && pauseChecked,
                Is.True, $"Observed: hall={sawHallWalk}, inside={sawInside}, occupied={sawOccupied}," +
                $" return={sawReturn}, lock={lockChecked}, exitPriority={exitPriorityChecked}," +
                $" completed={bar.Crowd.CompletedWashroomVisits}." + Diagnostic());
            if (place == BarNpcRole.StandingPatron)
                Assert.That(exitPriorityChecked, Is.True, "The exiting guest's priority was not observed." + Diagnostic());
            if (place == BarNpcRole.CounterPatron)
                Assert.That(closedDuringVisit, Is.True, "The in-flight visit must finish after business closes.");
            Assert.That(bar.Crowd.CompletedWashroomVisits, Is.EqualTo(1), Diagnostic());
            Assert.That(originalActor != null, Is.True, "The same guest returns before any ordinary departure.");
            Assert.That(Vector3.Distance(originalActor.position, actorRest), Is.LessThan(.02f));
            Assert.That(guest.Registry.ModelRoot, Is.SameAs(originalModel));
            AssertRetainedDrink(prop, originalVessel, surface, drinkRest, fill);
            Assert.That(bar.Crowd.IsAnchorOccupied(guest.Anchor.Id), Is.True);
            Assert.That(bar.Crowd.PendingAbandonedCount, Is.Zero);
            Assert.That(bartender.PendingCleanupCount, Is.Zero);
            Assert.That(bar.Washroom.Access.IsNpcReserved, Is.False);
            Assert.That(bar.Washroom.Access.IsLocked, Is.False);
            Assert.That(bar.Crowd.TryBeginWashroomVisit(guest.Anchor.Id), Is.False,
                closedDuringVisit ? "Closing prevents a new visit." : "The same guest needs a substantial repeat cooldown.");
            StageRoomCamera();
            yield return Capture(place, "returned-to-same-place");
            if (place == BarNpcRole.StandingPatron)
            {
                bar.Crowd.Advance(721f);
                Assert.That(bar.Crowd.TryBeginWashroomVisit(guest.Anchor.Id), Is.True);
                yield return DriveUntil(() => bar.Washroom.Access.IsLocked, 1800,
                    "The lifecycle proof needs a real occupied room before scene unloading.");
                Assert.That(bar.Washroom.Access.NpcActor, Is.SameAs(originalActor));
                StageRoomCamera();
                yield return Capture(place, "unload-while-occupied");
                // UnityTearDown unloads this still-occupied production scene and checks every owner is gone.
            }
        }

        private IEnumerator DriveUntil(Func<bool> done, int maximumSteps, string failure)
        {
            float deadline = Time.realtimeSinceStartup + 25f;
            for (int step = 0; step < maximumSteps && !done(); step++)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), failure + Diagnostic());
                Advance(.1f);
                yield return null;
            }
            Assert.That(done(), Is.True, failure + Diagnostic());
        }

        private void Advance(float seconds)
        {
            reviewCamera?.PublishNow();
            RecordVisitTrace();
            bar.Crowd.Advance(seconds);
            RecordVisitTrace();
            bartender.Advance(seconds);
            bar.Bartender.Advance(seconds);
            bartender.RefreshCleanupContact();
            bar.DrinkShop.AdvancePresentation(seconds);
        }

        private void AssertRetainedDrink(BarPatronDrinkProp prop, Transform original, Renderer surface, Pose rest, float fill)
        {
            Assert.That(prop.Root, Is.SameAs(original));
            Assert.That(original != null && original.gameObject.activeInHierarchy, Is.True);
            Assert.That(Vector3.Distance(original.position, rest.position), Is.LessThan(.003f));
            Assert.That(Quaternion.Angle(original.rotation, rest.rotation), Is.LessThan(.15f));
            Vector3 bottom = original.position + original.rotation * prop.BaseLocalPosition;
            Assert.That(Mathf.Abs(bottom.y - surface.bounds.max.y), Is.LessThan(.025f));
            if (prop.Vessel != null) Assert.That(prop.Vessel.FillProgress, Is.EqualTo(fill));
        }

        private void AssertWalkingBodyClear(Transform actor)
        {
            Physics.SyncTransforms();
            foreach (Collider hit in Physics.OverlapCapsule(actor.position + Vector3.up * .38f,
                         actor.position + Vector3.up * 1.3f, .12f, Physics.DefaultRaycastLayers,
                         QueryTriggerInteraction.Ignore))
            {
                if (hit == null || hit.transform.IsChildOf(actor)) continue;
                Assert.Fail("The walking guest's body penetrated " + hit.name +
                    $" boundsMin={hit.bounds.min.ToString("F3")}, boundsMax={hit.bounds.max.ToString("F3")}," +
                    $" actorLocal={bar.Room.InverseTransformPoint(actor.position).ToString("F3")}." + Diagnostic());
            }
        }

        private void RecordVisitTrace()
        {
            if (bar == null || bar.Crowd == null) return;
            BarCrowdController crowd = bar.Crowd;
            string phase = crowd.WashroomPhaseName;
            lastDoorFraction = bar.Washroom.Door.OpenFraction;
            if (!string.IsNullOrEmpty(crowd.WashroomBlockedBy)) lastVisitBlocker = crowd.WashroomBlockedBy;
            if (!string.IsNullOrEmpty(phase))
            {
                lastVisitPhase = phase;
                lastVisitTarget = crowd.WashroomTarget;
                if (crowd.WashroomPatron?.Registry != null)
                    lastVisitPosition = crowd.WashroomPatron.Registry.transform.position;
            }
            if (phase == sampledPhase) return;
            sampledPhase = phase;
            string sample = $"{(string.IsNullOrEmpty(phase) ? "Finished" : phase)}" +
                $" body={lastVisitPosition.ToString("F3")} target={lastVisitTarget.ToString("F3")}" +
                $" blocked={lastVisitBlocker} completed={crowd.CompletedWashroomVisits}" +
                $" abort={crowd.LastWashroomAbortReason}";
            if (visitTrace.Count == 64) visitTrace.RemoveAt(0);
            visitTrace.Add(sample);
            Debug.Log("WC visit trace: " + sample);
        }

        private string Diagnostic()
        {
            if (bar == null) return string.Empty;
            return $" WC={bar.Crowd.WashroomPhaseName}, guest={bar.Crowd.WashroomPatronId}," +
                $" phaseSeconds={bar.Crowd.WashroomPhaseSeconds:F2}, target={bar.Crowd.WashroomTarget}," +
                $" body={bar.Crowd.WashroomPatron?.Registry?.transform.position}, blocked={bar.Crowd.WashroomBlockedBy}," +
                $" lastPhase={lastVisitPhase}, lastBody={lastVisitPosition.ToString("F3")}," +
                $" lastTarget={lastVisitTarget.ToString("F3")}, lastBlocker={lastVisitBlocker}," +
                $" doorFraction={lastDoorFraction:F4}," +
                $" abort={bar.Crowd.LastWashroomAbortReason}. Trace: " + string.Join(" | ", visitTrace);
        }

        private void StageRoomCamera()
        {
            Vector3 eye = bar.Room.TransformPoint(new Vector3(-1.4f, 2.3f, -2.25f));
            reviewCamera.Stage(new Pose(eye,
                Quaternion.LookRotation(bar.Room.TransformPoint(new Vector3(-3.4f, 1f, 2.7f)) - eye)), 76f);
        }

        private void StageOccupiedDoorCamera()
        {
            Player3DAssetRegistry registry = bar.Player.GameObject.GetComponentInChildren<Player3DAssetRegistry>();
            Assert.That(registry != null && registry.Anchors.Head != null, Is.True,
                "The occupied-door review uses the production hero's actual head anchor.");
            Vector3 eye = registry.Anchors.Head.position + bar.Player.GameObject.transform.forward * .06f;
            Vector3 target = bar.Washroom.Door.InteractionPosition + Vector3.up * .2f;
            reviewCamera.Stage(new Pose(eye, Quaternion.LookRotation(target - eye)), 68f);
        }

        private static void SetDrinkPhase(BarPatronDrinkTimeline clock, BarPatronDrinkPhase phase)
        {
            for (int step = 0; step < 4 && clock.Phase != phase; step++)
                clock.Advance(Mathf.Max(.001f, clock.PhaseDuration - clock.PhaseElapsed + .001f));
            Assert.That(clock.Phase, Is.EqualTo(phase));
        }

        private static void AdvanceClockTo(int minute)
        {
            double delta = minute - GameSessionState.GameTimeOfDayMinutes;
            if (delta < 0d) delta += 24 * 60;
            GameSessionState.AdvanceGameTime((float)(delta / GameTimeState.GameMinutesPerRealSecond + .002d));
            Assert.That(GameSessionState.GameMinuteOfDay, Is.EqualTo(minute));
        }

        private IEnumerator CompileActualForwardPasses()
        {
#if UNITY_EDITOR
            var materials = new HashSet<Material>();
            foreach (Renderer renderer in bar.GetComponentsInChildren<Renderer>(true))
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null || !materials.Add(material)) continue;
                int pass = material.FindPass("ForwardLit");
                if (pass < 0) continue;
                if (!UnityEditor.ShaderUtil.IsPassCompiled(material, pass)) UnityEditor.ShaderUtil.CompilePass(material, pass);
                float deadline = Time.realtimeSinceStartup + 10f;
                while (!UnityEditor.ShaderUtil.IsPassCompiled(material, pass))
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The actual bar forward pass did not compile: " + material.name);
                    yield return null;
                }
            }
#endif
            yield return null;
            yield return null;
        }

        private static IEnumerator Capture(BarNpcRole place, string phase)
        {
#if UNITY_EDITOR
            Type type = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
            Assert.That(type, Is.Not.Null);
            UnityEditor.EditorWindow view = UnityEditor.EditorWindow.GetWindow(type);
            view.Show();
            view.Focus();
            view.Repaint();
#endif
            yield return null;
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures", SceneIds.BarInterior,
                "wc-visit-" + place + "-" + phase + ".png"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            DateTime before = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.realtimeSinceStartup + 3f;
            do { yield return null; }
            while ((!File.Exists(path) || File.GetLastWriteTimeUtc(path) <= before) && Time.realtimeSinceStartup < deadline);
            Assert.That(File.Exists(path) && File.GetLastWriteTimeUtc(path) > before, Is.True, path);
        }
    }
}
