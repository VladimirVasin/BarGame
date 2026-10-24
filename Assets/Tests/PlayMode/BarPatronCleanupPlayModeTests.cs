using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    [PrebuildSetup(typeof(CompactBarAssetsSetup))]
    public sealed class BarPatronCleanupPlayModeTests
    {
        private static readonly BarNpcRole[] Places =
            { BarNpcRole.CounterPatron, BarNpcRole.SeatedPatron, BarNpcRole.StandingPatron };
        private static readonly FieldInfo CleanupHandTargetField = typeof(BarBartenderServiceChoreography)
            .GetField("cleanupHandTarget", BindingFlags.Instance | BindingFlags.NonPublic);
        private BarInteriorRoot bar;
        private BarBartenderServiceChoreography choreography;
        private PlayerAnimatedInteractionController playerAction;
        private IDisposable pause;
        private float previousCaptureDelta;
        private Camera camera;
        private BarCleanupReviewCamera reviewCamera;

        [SetUp]
        public void SetUp()
        {
            previousCaptureDelta = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            GameSessionState.BeginNewGame("bar-patron-cleanup-test");
            BarTestTime.StartFullAttendance("patron-cleanup");
            GameSessionState.EnterBar("patron-cleanup", BarActivityKind.Cocktail);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            pause?.Dispose();
            pause = null;
            if (playerAction != null) playerAction.ReleaseExternalClock(this);
            if (bar != null)
                foreach (BarCounterStation station in bar.GetComponentsInChildren<BarCounterStation>(true))
                    station.Seat?.Cancel();
            yield return AreaCaptureFixture.ReleaseBarService();
            Scene scene = SceneManager.GetSceneByName(SceneIds.BarInterior);
            if (scene.IsValid() && scene.isLoaded)
            {
                Scene cleanup = SceneManager.CreateScene("Bar Patron Cleanup Fixture Exit");
                SceneManager.SetActiveScene(cleanup);
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            GameSessionState.BeginNewGame("bar-patron-cleanup-test-exit");
            Time.captureDeltaTime = previousCaptureDelta;
            bar = null;
            choreography = null;
            playerAction = null;
            yield return null;
            Assert.That(Object.FindObjectsByType<BarCrowdController>(FindObjectsSortMode.None), Is.Empty);
        }

        [UnityTest]
        public IEnumerator ActualPlace_RetainsDrinkUntilPhysicalCollectionAndQueuesPaidOrder(
            [ValueSource(nameof(Places))] BarNpcRole place)
        {
            Assert.That(Application.isBatchMode, Is.False,
                "This contract reviews the actual Game view, without -batchmode or -nographics.");
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
            bar.Conversation.AutoAdvance = false;
            bar.Conversation.enabled = false;
            foreach (BarPatronAnimator animator in bar.GetComponentsInChildren<BarPatronAnimator>(true)) animator.enabled = false;
            Assert.That(bar.Patrons.Count, Is.EqualTo(BarBusinessHours.MaximumGuests));
            choreography = bar.Bartender.GetComponent<BarBartenderServiceChoreography>();
            Assert.That(choreography, Is.Not.Null);
            choreography.AutoAdvance = false;
            camera = Camera.main;
            Assert.That(camera, Is.Not.Null);

            // The hero has a real delivered menu before the collector leaves.
            // Confirmation during collection must queue its payment without advancing service remotely.
            BarCounterStation heroStation = bar.CounterStations[bar.CounterStations.Count - 1];
            bar.Player.Motor.Teleport(heroStation.Seat.Plan.EntryPose.RootPosition);
            bar.Player.GameObject.transform.rotation = heroStation.Seat.Plan.EntryPose.RootRotation;
            Physics.SyncTransforms();
            playerAction = heroStation.Seat.Controller;
            Assert.That(playerAction.BeginExternalClock(this), Is.True);
            Assert.That(heroStation.CanInteract(bar.Player.Interactor), Is.True);
            heroStation.Interact(bar.Player.Interactor);
            yield return DriveServiceUntil(() => heroStation.Seat.IsSeated && bar.DrinkShop.IsBrowsing,
                "The physical seat and menu did not become ready.");
            Assert.That(bar.DrinkShop.Select(3), Is.True);

            BarPatron guest = null;
            float farthest = -1f;
            foreach (BarPatron candidate in bar.Patrons)
            {
                if (candidate.Anchor.Role != place) continue;
                float distance = (candidate.Drinking.BottleRoot.position - bar.Bartender.transform.position).sqrMagnitude;
                if (distance <= farthest) continue;
                guest = candidate;
                farthest = distance;
            }
            Assert.That(guest, Is.Not.Null, place.ToString());
            foreach (BarPatron candidate in bar.Patrons)
            {
                SetDrinkPhase(candidate.Drinking.Timeline, ReferenceEquals(candidate, guest)
                    ? BarPatronDrinkPhase.Rest : BarPatronDrinkPhase.Raise);
                candidate.Presentation.Advance(0f, false, true);
                candidate.Drinking.SampleFinalPose();
            }
            BarPatronDrinkProp prop = guest.Drinking.DrinkProp;
            Transform originalRoot = prop.Root;
            if (prop.Vessel != null) prop.Vessel.SetFillProgress(.37f);
            float fill = prop.Vessel != null ? prop.Vessel.FillProgress : 0f;
            Renderer surface = guest.Drinking.SurfaceRenderer;
            string anchorId = guest.Anchor.Id;
            Transform guestRoot = guest.Registry.transform;
            Pose restPose = new Pose(originalRoot.position, originalRoot.rotation);
            camera.GetComponent<PlayerCameraFollow>().enabled = false;
            reviewCamera = camera.gameObject.AddComponent<BarCleanupReviewCamera>();
            reviewCamera.TargetCamera = camera;
            StageRoomCamera();
            yield return CompileActualForwardPasses();

            AdvanceClockTo(60); // The bar is still open; the late attendance target is lower than eight.
            bar.Crowd.Advance(BarCrowdController.ChangeIntervalSeconds + .1f);
            Assert.That(bar.Crowd.TransitPatronId, Is.EqualTo(anchorId),
                "Only the chosen guest finished the sip; all peers still own an unfinished raise.");
            Assert.That(bar.Crowd.TransitPhaseName, Is.EqualTo("ReleasingDrink"));
            Assert.That(bar.Crowd.TryGetAbandonedDrink(anchorId, out BarAbandonedDrink ticket), Is.True);
            Assert.That(ticket.DrinkProp.Root, Is.SameAs(originalRoot));
            Assert.That(ticket.SurfaceRenderer, Is.SameAs(surface));
            Assert.That(ticket.IsReadyForCollection, Is.False);
            Assert.That(bar.Crowd.IsAnchorOccupied(anchorId), Is.True);
            Assert.That(bar.Crowd.CompleteDrinkCleanup(ticket), Is.False,
                "A visible drink cannot release its spatial reservation.");

            bool sawEmptyHands = false;
            deadline = Time.realtimeSinceStartup + 20f;
            for (int step = 0; step < 1500 && (guestRoot != null || bar.Crowd.HasGuestInTransit); step++)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), CrowdDiagnostic());
                bool contact = NeedsCrowdContactFrame();
                bar.Crowd.Advance(.1f);
                AssertRetainedDrink(ticket, originalRoot, restPose, fill);
                if (guestRoot != null && Vector3.Distance(guestRoot.position, restPose.position) > 1f)
                {
                    Assert.That(originalRoot.IsChildOf(guestRoot), Is.False);
                    sawEmptyHands = true;
                }
                if (contact || NeedsCrowdContactFrame() || step % 3 == 0) yield return null;
            }
            Assert.That(guestRoot == null, Is.True, "The real guest must leave through the door before collection.");
            Assert.That(sawEmptyHands, Is.True);
            Assert.That(ticket.IsReadyForCollection, Is.True);
            AssertRetainedDrink(ticket, originalRoot, restPose, fill);
            Assert.That(bar.Crowd.IsAnchorOccupied(anchorId), Is.True);
            StageDrinkCamera(ticket);
            yield return Capture(place, "left-on-surface");

            StageRoomCamera();
            Vector3 bartenderHome = bar.Bartender.transform.position;
            bool reachedRoomSide = false;
            Bounds counterBounds = bar.DrinkServiceView.CounterSurfaceWorldBounds;
            float counterFrontZ = bar.Room.InverseTransformPoint(new Vector3(counterBounds.center.x,
                counterBounds.center.y, counterBounds.min.z)).z;
            deadline = Time.realtimeSinceStartup + 20f;
            for (int step = 0; step < 1600 && !choreography.CleanupVesselAttached; step++)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), CleanupDiagnostic());
                Vector3 before = bar.Bartender.transform.position;
                AdvanceService(.1f);
                Vector3 after = bar.Bartender.transform.position;
                Assert.That(Vector3.Distance(before, after), Is.LessThan(.35f),
                    "Collection walks the real body; it cannot jump between work docks.");
                Assert.That(choreography.CleanupBlockedBy, Is.Empty, CleanupDiagnostic());
                AssertBodyClear();
                Assert.That(ticket.DrinkProp.Root, Is.SameAs(originalRoot));
                if (!choreography.CleanupVesselAttached) AssertRetainedDrink(ticket, originalRoot, restPose, fill);
                if (bar.Room.InverseTransformPoint(after).z < counterFrontZ - .2f)
                    reachedRoomSide = true;
                yield return null;
            }
            Assert.That(choreography.CleanupVesselAttached, Is.True, CleanupDiagnostic());
            if (place != BarNpcRole.CounterPatron)
                Assert.That(reachedRoomSide, Is.True, "Tables and booths require a visible walk through the counter's left opening.");
            Assert.That(choreography.ActiveCleanupDrink, Is.SameAs(ticket));
            AssertGrip(prop, fill);
            StageDrinkCamera(ticket);
            yield return Capture(place, "actual-pickup");
            StageRoomCamera();

            // Pause must hold the real body, grip, vessel and dirty reservation together.
            pause = GameTimeScaleRuntime.AcquirePause();
            yield return null;
            Pose pausedBody = new Pose(bar.Bartender.transform.position, bar.Bartender.transform.rotation);
            Pose pausedVessel = new Pose(originalRoot.position, originalRoot.rotation);
            string pausedPhase = choreography.CleanupPhaseName;
            choreography.Advance(.5f);
            choreography.RefreshCleanupContact();
            bar.DrinkShop.AdvancePresentation(0f);
            bar.Crowd.Advance(20f);
            yield return null;
            Assert.That(bar.Bartender.transform.position, Is.EqualTo(pausedBody.position));
            Assert.That(Quaternion.Angle(bar.Bartender.transform.rotation, pausedBody.rotation), Is.LessThan(.001f));
            Assert.That(originalRoot.position, Is.EqualTo(pausedVessel.position));
            Assert.That(Quaternion.Angle(originalRoot.rotation, pausedVessel.rotation), Is.LessThan(.001f));
            Assert.That(choreography.CleanupPhaseName, Is.EqualTo(pausedPhase));
            Assert.That(bar.Crowd.IsAnchorOccupied(anchorId), Is.True);
            pause.Dispose();
            pause = null;

            int cashBefore = GameSessionState.CashBalance;
            int price = bar.DrinkShop.SelectedOffer.Price;
            Assert.That(choreography.IsCleaningPatronDrink, Is.True);
            Assert.That(bar.DrinkShop.IsBartenderAvailableForService, Is.False);
            Assert.That(bar.DrinkShop.ConfirmSelection(), Is.True);

            bool disposedHidden = false;
            bool observedCleanupGate = false;
            deadline = Time.realtimeSinceStartup + 20f;
            for (int step = 0; step < 1800 && (!ticket.IsCleanupComplete || !bar.DrinkShop.PurchaseCommitted); step++)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), CleanupDiagnostic());
                AdvanceService(.1f);
                AssertBodyClear();
                if (choreography.IsCleaningPatronDrink)
                {
                    observedCleanupGate = true;
                    Assert.That(bar.DrinkShop.IsBartenderAvailableForService, Is.False,
                        "The service clock waits for the collector's physical return.");
                    if (bar.DrinkShop.PurchaseCommitted)
                    {
                        BarDrinkServicePhase waitingPhase = bar.DrinkShop.Phase;
                        float waitingElapsed = bar.DrinkShop.Timeline.PhaseElapsedSeconds;
                        bar.DrinkShop.AdvancePresentation(.5f);
                        Assert.That(bar.DrinkShop.Phase, Is.EqualTo(waitingPhase));
                        Assert.That(bar.DrinkShop.Timeline.PhaseElapsedSeconds, Is.EqualTo(waitingElapsed));
                    }
                }
                if (originalRoot != null && originalRoot.gameObject.activeInHierarchy)
                {
                    Assert.That(bar.Crowd.IsAnchorOccupied(anchorId), Is.True);
                    AssertGrip(prop, fill);
                }
                if (ticket.IsCleanupComplete && !disposedHidden)
                {
                    Assert.That(choreography.IsCleanupVesselHidden, Is.True,
                        "The exact vessel disappears only below the opaque counter and the actual camera's sightlines.");
                    Assert.That(originalRoot, Is.Not.Null,
                        "Disposal keeps its final hidden transform until the frame's normal Destroy boundary.");
                    Assert.That(originalRoot.gameObject.activeInHierarchy, Is.False);
                    float highestCorner = float.NegativeInfinity;
                    Bounds physical = prop.PhysicalLocalBounds;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        Vector3 sign = new Vector3((corner & 1) == 0 ? -1f : 1f,
                            (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                        Vector3 point = originalRoot.position + originalRoot.rotation *
                            (physical.center + Vector3.Scale(physical.extents, sign));
                        highestCorner = Mathf.Max(highestCorner, point.y);
                    }
                    Assert.That(highestCorner, Is.LessThan(bar.DrinkServiceView.CounterSurfaceWorldBounds.min.y),
                        "Every actual vessel corner must have passed below the counter before deactivation.");
                    disposedHidden = true;
                }
                yield return null;
            }
            Assert.That(ticket.IsCleanupComplete, Is.True, CleanupDiagnostic());
            Assert.That(disposedHidden, Is.True);
            Assert.That(observedCleanupGate, Is.True);
            Assert.That(bar.DrinkShop.PurchaseCommitted, Is.True,
                "The accepted gesture commits exactly one queued hero order, even if collection finished during that gesture.");
            Assert.That(GameSessionState.CashBalance, Is.EqualTo(cashBefore - price));
            Assert.That(bar.DrinkShop.ConfirmSelection(), Is.False);
            Assert.That(bar.Crowd.IsAnchorOccupied(anchorId), Is.False);
            Assert.That(bar.Crowd.TryGetAbandonedDrink(anchorId, out _), Is.False);
            yield return Capture(place, "stowed-behind-counter");
            yield return DriveServiceUntil(() => bar.DrinkShop.CanDrinkServedVessel,
                "The accepted hero service resumes after the collector reaches its proper service dock.");
            Assert.That(bar.DrinkShop.HeroServiceVessel, Is.Not.Null);
            Assert.That(GameSessionState.CashBalance, Is.EqualTo(cashBefore - price));
            Assert.That(choreography.CompletedCleanupCount, Is.EqualTo(1));
            Assert.That(Vector3.Distance(bar.Bartender.transform.position, bartenderHome), Is.LessThan(4f),
                "The service remains behind the physical counter after collection.");
        }

        private void AdvanceService(float seconds)
        {
            playerAction?.AdvanceExternal(this, seconds);
            reviewCamera?.PublishNow();
            choreography.Advance(seconds);
            bar.Bartender.Advance(seconds);
            choreography.RefreshCleanupContact();
            bar.DrinkShop.AdvancePresentation(seconds);
        }

        private IEnumerator DriveServiceUntil(Func<bool> done, string failure)
        {
            float deadline = Time.realtimeSinceStartup + 20f;
            for (int step = 0; step < 1800 && !done(); step++)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), failure + CleanupDiagnostic());
                AdvanceService(.1f);
                yield return null;
            }
            Assert.That(done(), Is.True, failure + CleanupDiagnostic());
        }

        private void AssertRetainedDrink(BarAbandonedDrink ticket, Transform root, Pose rest, float fill)
        {
            Assert.That(ticket.DrinkProp.Root, Is.SameAs(root));
            Assert.That(root != null && root.gameObject.activeInHierarchy, Is.True);
            Assert.That(Vector3.Distance(root.position, rest.position), Is.LessThan(.002f));
            Assert.That(Quaternion.Angle(root.rotation, rest.rotation), Is.LessThan(.1f));
            Vector3 bottom = root.position + root.rotation * ticket.DrinkProp.BaseLocalPosition;
            Assert.That(Mathf.Abs(bottom.y - ticket.SurfaceRenderer.bounds.max.y), Is.LessThan(.025f));
            if (ticket.DrinkProp.Vessel != null) Assert.That(ticket.DrinkProp.Vessel.FillProgress, Is.EqualTo(fill));
        }

        private void AssertGrip(BarPatronDrinkProp prop, float fill)
        {
            Assert.That(choreography.CleanupGripError, Is.LessThan(.03f), CleanupDiagnostic());
            Vector3 grip = prop.Root.position + prop.Root.rotation * prop.GripLocalPosition;
            Assert.That(Vector3.Distance(grip, bar.Bartender.Registry.HandPose.AnatomicalCylinderCentre(true)),
                Is.LessThan(.04f), "The original typed vessel remains in the anatomical left-hand grip.");
            if (prop.Vessel != null) Assert.That(prop.Vessel.FillProgress, Is.EqualTo(fill));
        }

        private void AssertBodyClear()
        {
            Physics.SyncTransforms();
            Transform actor = bar.Bartender.transform;
            foreach (Collider hit in Physics.OverlapCapsule(actor.position + Vector3.up * .38f,
                         actor.position + Vector3.up * 1.3f, .12f, Physics.DefaultRaycastLayers,
                         QueryTriggerInteraction.Ignore))
            {
                if (hit == null || hit.transform.IsChildOf(actor)) continue;
                Assert.Fail("The bartender's walking body penetrated " + hit.name + CleanupDiagnostic());
            }
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

        private bool NeedsCrowdContactFrame() => bar.Crowd.TransitPhaseName == "ReleasingDrink" ||
            bar.Crowd.TransitPhaseName == "Standing" || bar.Crowd.TransitPhaseName == "Sitting";
        private string CrowdDiagnostic() => $" Crowd={bar.Crowd.TransitPhaseName}, target={bar.Crowd.TransitTarget}, blocked={bar.Crowd.TransitBlockedBy}.";
        private string CleanupDiagnostic()
        {
            if (choreography == null) return string.Empty;
            BarBartenderPresentation bartender = bar.Bartender;
            BarBartenderAssetRegistry registry = bartender.Registry;
            Vector3 requested = CleanupHandTargetField?.GetValue(choreography) is Vector3 target ? target : Vector3.zero;
            Vector3 actual = registry.VesselGripAnchor.position;
            BarAbandonedDrink drink = choreography.ActiveCleanupDrink;
            Vector3 prop = drink?.DrinkProp.Root != null ? drink.DrinkProp.Root.position : Vector3.zero;
            return $" Cleanup={choreography.CleanupPhaseName}, anchor={drink?.AnchorId}, kind={drink?.DrinkProp.Kind}," +
                $" actorLocal={bartender.transform.localPosition.ToString("F3")}, actorYaw={bartender.transform.localEulerAngles.y:F1}," +
                $" requestedGrip={requested.ToString("F3")}, actualGrip={actual.ToString("F3")}," +
                $" chainWeight={bartender.GetChainWeight(BarBartenderServiceChoreography.VesselChainIndex):F3}," +
                $" prop={prop.ToString("F3")}, gripError={choreography.CleanupGripError:F4}," +
                $" blocked={choreography.CleanupBlockedBy}, service={bar.DrinkShop.Phase}.";
        }

        private void StageRoomCamera()
        {
            Vector3 position = bar.Room.TransformPoint(new Vector3(1.3f, 2.1f, -3.6f));
            reviewCamera.Stage(new Pose(position,
                Quaternion.LookRotation(bar.Room.TransformPoint(new Vector3(-1f, 1.1f, 1.2f)) - position)), 74f);
        }

        private void StageDrinkCamera(BarAbandonedDrink drink)
        {
            Vector3 target = drink.DrinkProp.Root.position;
            Vector3 localTarget = bar.Room.InverseTransformPoint(target);
            Bounds surface = drink.SurfaceRenderer.bounds;
            Vector3 surfaceCentre = bar.Room.InverseTransformPoint(surface.center);
            Vector3 position;
            switch (drink.Anchor.Role)
            {
                case BarNpcRole.CounterPatron:
                    // Look out from the service aisle: the counter guest's
                    // former side puts the collector's back in front of his hands.
                    position = localTarget + new Vector3(.65f, .95f, 1.1f);
                    break;
                case BarNpcRole.SeatedPatron:
                    // Stay alongside this booth, above the partition and
                    // outside its measured right edge, without crossing a neighbour.
                    float edge = bar.Room.InverseTransformPoint(surface.max).x;
                    position = new Vector3(edge + .55f, 2.48f, surfaceCentre.z + .28f);
                    break;
                default:
                    position = new Vector3(surfaceCentre.x - Mathf.Sign(surfaceCentre.x) * 1.15f,
                        2.35f, surfaceCentre.z + .35f);
                    break;
            }
            position.x = Mathf.Clamp(position.x, -BarInteriorLayoutPlanner.RoomWidth * .5f + .35f,
                BarInteriorLayoutPlanner.RoomWidth * .5f - .35f);
            position.z = Mathf.Clamp(position.z, -BarInteriorLayoutPlanner.RoomDepth * .5f + .35f,
                BarInteriorLayoutPlanner.RoomDepth * .5f - .35f);
            position.y = Mathf.Clamp(position.y, .65f, 2.55f);
            Vector3 eye = bar.Room.TransformPoint(position);
            reviewCamera.Stage(new Pose(eye, Quaternion.LookRotation(target + Vector3.up * .16f - eye)), 58f);
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
                "patron-cleanup-" + place + "-" + phase + ".png"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            DateTime before = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.realtimeSinceStartup + 3f;
            do { yield return null; }
            while ((!File.Exists(path) || File.GetLastWriteTimeUtc(path) <= before) && Time.realtimeSinceStartup < deadline);
            Assert.That(File.Exists(path) && File.GetLastWriteTimeUtc(path) > before, Is.True, path);
        }
    }

    /// <summary>A review camera publishes after the real seated/menu camera owners have sampled their rigs.</summary>
    [DefaultExecutionOrder(20000)]
    internal sealed class BarCleanupReviewCamera : MonoBehaviour
    {
        public Camera TargetCamera;
        private Pose pose;
        private float lens;
        private bool staged;

        public void Stage(Pose target, float fieldOfView)
        {
            pose = target;
            lens = fieldOfView;
            staged = true;
            PublishNow();
        }

        private void LateUpdate() => PublishNow();

        public void PublishNow()
        {
            if (!staged || TargetCamera == null) return;
            // The room review has no page focus; retain the room's normal post-processing profile.
            CinematicDepthOfField.EndImmediately();
            TargetCamera.transform.SetPositionAndRotation(pose.position, pose.rotation);
            TargetCamera.fieldOfView = lens;
        }
    }
}
