using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Daily last service, one final request, then the ordinary visible door action.</summary>
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class BarClosingController : MonoBehaviour
    {
        public const float GraceSeconds = 60f;
        public const float FinalChanceSeconds = 8f;
        private readonly Vector3[] waypoints = new Vector3[128];
        private BarInteriorRoot bar;
        private BarExit exit;
        private PlayerDoorActionTarget exitTarget;
        private PlayerAnimatedInteractionController interaction;
        private PlayerDoorActionController doorAction;
        private Player3DRagdollController ragdoll;
        private BarClosingRoute route;
        private BarClosingCameraFocus cameraFocus;
        private BarBartenderServiceChoreography bartenderMotion;
        private bool announcementRequested;
        private bool closed, ownsExit, ownsWashroomDoor;
        private float finalChance = FinalChanceSeconds, retrySeconds;
        public bool AutoAdvance { get; set; } = true;
        public bool WarningIssued { get; private set; }
        public bool FinalRequestIssued { get; private set; }
        public bool IsDepartureRequested { get; private set; }
        public float GraceRemainingSeconds { get; private set; } = GraceSeconds;
        public bool IsWalkingToExit => ownsExit && interaction != null &&
            interaction.Phase == PlayerAnimatedInteractionPhase.Positioning;

        public void Initialize(BarInteriorRoot root, BarExit barExit)
        {
            bar = root;
            exit = barExit;
            exitTarget = exit.GetComponent<PlayerDoorActionTarget>();
            interaction = bar.Player.GameObject.GetComponent<PlayerAnimatedInteractionController>();
            doorAction = bar.Player.GameObject.GetComponent<PlayerDoorActionController>();
            ragdoll = bar.Player.GameObject.GetComponent<Player3DRagdollController>();
            cameraFocus = gameObject.AddComponent<BarClosingCameraFocus>();
            bartenderMotion = bar.Bartender.GetComponent<BarBartenderServiceChoreography>();
            bar.DrinkShop.ServiceOrderDelivered += HandleDelivery;
        }

        private void HandleDelivery(BarServiceOrder order)
        {
            if (order.IsHero && !bar.IsBusinessOpen) GraceRemainingSeconds = GraceSeconds;
        }

        private void Update()
        {
            if (AutoAdvance) Advance(GameTimeScaleRuntime.CalendarDeltaTime);
        }

        public void Advance(float calendarSeconds)
        {
            if (!float.IsFinite(calendarSeconds) || calendarSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(calendarSeconds));
            if (bar == null || !isActiveAndEnabled || GameTimeScaleRuntime.IsPaused ||
                !GameSessionState.IsGameTimeRunning || SceneTransitionService.IsTransitioning) return;
            int minute = GameSessionState.GameMinuteOfDay;
            if (bar.IsBusinessOpen)
            {
                if (closed) ResetVisit();
                if (minute >= 90 && minute < BarBusinessHours.ClosingMinute && !WarningIssued)
                {
                    WarningIssued = TryAnnounce(false);
                }
                else if (announcementRequested && !cameraFocus.IsActive && !bar.Conversation.IsBartenderSpeaking)
                    ReleaseAnnouncement();
                return;
            }
            if (!closed)
            {
                if (announcementRequested && !cameraFocus.IsActive) ReleaseAnnouncement();
                closed = true;
                GraceRemainingSeconds = GraceSeconds;
                // The first observation establishes closing; a hitch cannot spend the entire grace.
                return;
            }
            if (HasPendingHeroOrder()) return;
            if (!FinalRequestIssued)
            {
                GraceRemainingSeconds = Mathf.Max(0f, GraceRemainingSeconds - calendarSeconds);
                if (GraceRemainingSeconds <= 0f)
                {
                    FinalRequestIssued = TryAnnounce(true);
                }
                return;
            }
            if (!IsDepartureRequested)
            {
                if (bar.Conversation.IsBartenderSpeaking || cameraFocus.IsActive) return;
                finalChance = Mathf.Max(0f, finalChance - calendarSeconds);
                if (finalChance > 0f) return;
                IsDepartureRequested = true;
            }
            AdvanceDeparture(calendarSeconds);
        }

        private void LateUpdate()
        {
            if (cameraFocus == null || !cameraFocus.IsActive) return;
            if (!bar.Conversation.IsBartenderSpeaking) cameraFocus.End();
            cameraFocus.Tick(GameTimeScaleRuntime.IsPaused ? 0f : Time.unscaledDeltaTime);
            if (!cameraFocus.IsActive) ReleaseAnnouncement();
        }

        private bool TryAnnounce(bool finalRequest)
        {
            if (bar.Karaoke != null && bar.Karaoke.IsPerforming) return false;
            if (!announcementRequested)
            {
                Vector2 center = bar.Layout.RoomBounds.center;
                bartenderMotion.RequestClosingAnnouncement(bar.transform.TransformPoint(
                    new Vector3(center.x, PlayerFactory.GroundedRootOffset, center.y)));
                announcementRequested = true;
            }
            if (!bartenderMotion.IsReadyForClosingAnnouncement || bar.Conversation.IsBartenderSpeaking ||
                cameraFocus.IsActive || !cameraFocus.Begin(bar)) return false;
            if (bar.Conversation.TryStartClosingLine(finalRequest)) return true;
            cameraFocus.RestoreImmediate();
            return false;
        }

        private void ReleaseAnnouncement()
        {
            if (announcementRequested && bartenderMotion != null) bartenderMotion.ReleaseClosingAnnouncement();
            announcementRequested = false;
        }

        private bool HasPendingHeroOrder()
        {
            for (int index = 0; index < bar.DrinkShop.PendingServiceOrderCount; index++)
                if (bar.DrinkShop.GetPendingServiceOrder(index).IsHero) return true;
            return false;
        }

        private void AdvanceDeparture(float seconds)
        {
            bar.Inventory?.Close();
            bar.Journal?.Close();
            if (bar.Washroom.Choice.IsOpen) bar.Washroom.Choice.Cancel();
            foreach (BarCounterStation station in bar.CounterStations)
                if (station.Seat != null && station.Seat.OwnsActiveInteraction)
                { station.RequestClosingExit(); return; }
            if (ownsExit)
            {
                if (doorAction.IsOwnedBy(exitTarget)) return;
                ownsExit = false;
                bar.AisleTraffic.Release(this);
                retrySeconds = 2f;
            }
            if (ownsWashroomDoor)
            {
                if (bar.Washroom.Door.OwnsActiveInteraction) return;
                ownsWashroomDoor = false;
                retrySeconds = .2f;
            }
            // The shop releases its camera/menu/input ownership through its normal terminal frames.
            if (bar.DrinkShop.IsOpen || bartenderMotion.IsClosingAnnouncementActive ||
                interaction.IsActive || ragdoll != null && ragdoll.IsActive ||
                CounterMenuInput.IsBlockedByOtherUi()) return;
            retrySeconds = Mathf.Max(0f, retrySeconds - seconds);
            if (retrySeconds > 0f) return;
            route ??= new BarClosingRoute(bar);
            Physics.SyncTransforms();
            if (bar.Washroom.Access.IsHeroInside && !bar.Washroom.Door.IsOpen)
            {
                if (route.TryBuild(bar.Washroom.Door.GroundedInsideDock, waypoints, out int wcCount))
                    ownsWashroomDoor = bar.Washroom.Door.Begin(waypoints, wcCount);
                if (!ownsWashroomDoor) retrySeconds = 2f;
                return;
            }
            // Ask existing narrow-aisle owners to finish/yield before claiming the route.
            if (!bar.AisleTraffic.TryAcquire(this, returning: true)) return;
            if (route.TryBuild(exitTarget.Plan.EntryPose.RootPosition, waypoints, out int count) &&
                exit.TryBeginClosingExit(bar.Player.Interactor, waypoints, count))
            {
                ownsExit = true;
                return;
            }
            bar.AisleTraffic.Release(this);
            retrySeconds = 2f;
        }

        private void ResetVisit()
        {
            CancelOwnedApproach();
            closed = WarningIssued = FinalRequestIssued = IsDepartureRequested = false;
            finalChance = FinalChanceSeconds;
            GraceRemainingSeconds = GraceSeconds;
        }

        private void CancelOwnedApproach()
        {
            if (ownsExit && doorAction != null) doorAction.Cancel(exitTarget);
            if (ownsWashroomDoor && bar != null) bar.Washroom.Door.Cancel();
            ownsExit = ownsWashroomDoor = false;
            if (bar != null) bar.AisleTraffic?.Release(this);
            cameraFocus?.RestoreImmediate();
            ReleaseAnnouncement();
        }

        private void OnDisable() => CancelOwnedApproach();
        private void OnDestroy()
        {
            CancelOwnedApproach();
            if (bar != null && bar.DrinkShop != null) bar.DrinkShop.ServiceOrderDelivered -= HandleDelivery;
        }
    }
}
