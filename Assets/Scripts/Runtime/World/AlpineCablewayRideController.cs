using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    /// <summary>
    /// One leg of the cableway journey, from the moment the hero settles on
    /// the bench to the moment the far station has him.
    ///
    /// Shaped on <c>LastRouteRideController</c>, and it inherits that ride's
    /// hard-won rules verbatim, because every one of them cost a day:
    ///
    /// - the passenger is written from the line's own <c>Moved</c> event, in
    ///   the same call that posed the cabin, never from a `LateUpdate`;
    /// - the far end only ARMS the arrival in `Awake` and waits under an
    ///   already-black screen for the transition flag to clear, because the
    ///   interaction controller force-completes everything while it is up;
    /// - the seat plan is world-space and is re-solved on arrival, before the
    ///   hero's `CharacterController` comes back on.
    /// </summary>
    [DefaultExecutionOrder(325)]
    public sealed class AlpineCablewayRideController : MonoBehaviour
    {
        /// <summary>
        /// How far short of the far turn the screen starts going out. The
        /// cabin has to be genuinely behind the snow ridge by the time it is
        /// black - a cabin that vanishes in open air is a worse cut than no
        /// cut at all - and a test pins this against the occluder's footprint
        /// rather than trusting the number.
        /// </summary>
        public const float FadeLeadMeters = 5.5f;

        public const float FadeOutSeconds = 1.3f;
        public const float FadeInSeconds = 0.9f;
        public const float SkipFadeOutSeconds = 0.6f;
        public const float SkipFadeInSeconds = 0.8f;
        public const string SkipPromptKey = "lastroute.ride.skip";

        private AlpineCablewayCabinSeat seat;
        private MountainCablewayController line;
        private GameAreaId destinationArea;
        private Func<MountainRoadCablewayPlan> arrivalCablewayFactory;
        private float departureLineLength;

        private LastRouteRideFadeView fade;
        private LastRouteRideSkipHintView skipHint;

        private bool riding;
        private bool awaitingArrivalStart;
        private bool fading;
        private bool skipping;
        private bool resumingArrival;

        public bool IsRiding => riding;
        public bool IsAwaitingStart => awaitingArrivalStart;
        public bool CanSkipRide => riding && !fading && !skipping;
        public bool IsSkipping => skipping;
        public LastRouteRideFadeView Fade => fade;

        /// <summary>
        /// The departure leg: the hero is standing on the platform and has
        /// just been seated. Ends by handing the far area a
        /// <see cref="AreaArrivalToken.Cableway"/>.
        /// </summary>
        public static AlpineCablewayRideController CreateForDeparture(
            Transform parent,
            AlpineCablewayCabinSeat cabinSeat,
            MountainCablewayController cablewayLine,
            MountainRoadCablewayPlan departureCableway,
            GameAreaId destination)
        {
            if (departureCableway == null)
            {
                throw new ArgumentNullException(nameof(departureCableway));
            }

            AlpineCablewayRideController ride = Create(
                parent,
                cabinSeat,
                cablewayLine);
            ride.departureLineLength = departureCableway.LineLength;
            ride.destinationArea = destination;
            return ride;
        }

        /// <summary>
        /// The arrival leg. It only ARMS: `Awake` of a destination root runs
        /// INSIDE the area transition, and while that flag is up the
        /// interaction controller force-completes any running interaction -
        /// which on the car dumped the hero on the tunnel floor and drove off
        /// without him.
        /// </summary>
        public static AlpineCablewayRideController CreateForArrival(
            Transform parent,
            AlpineCablewayCabinSeat cabinSeat,
            MountainCablewayController cablewayLine,
            Func<MountainRoadCablewayPlan> arrivalCableway,
            GameAreaId returnArea)
        {
            AlpineCablewayRideController ride = Create(
                parent,
                cabinSeat,
                cablewayLine);
            ride.arrivalCablewayFactory = arrivalCableway;

            // The way back is armed from the start. He gets off, walks the
            // village, comes back and boards again - and that second boarding
            // is an ordinary departure from this terminal.
            ride.destinationArea = returnArea;
            ride.awaitingArrivalStart = true;
            ride.fade.SetBlack();
            ride.StartCoroutine(ride.AwaitArrivalStart());
            return ride;
        }

        private static AlpineCablewayRideController Create(
            Transform parent,
            AlpineCablewayCabinSeat cabinSeat,
            MountainCablewayController cablewayLine)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (cabinSeat == null)
            {
                throw new ArgumentNullException(nameof(cabinSeat));
            }

            if (cablewayLine == null)
            {
                throw new ArgumentNullException(nameof(cablewayLine));
            }

            var host = new GameObject("Alpine Cableway Ride");
            host.transform.SetParent(parent, false);
            AlpineCablewayRideController ride =
                host.AddComponent<AlpineCablewayRideController>();
            ride.seat = cabinSeat;
            ride.line = cablewayLine;
            ride.fade = host.AddComponent<LastRouteRideFadeView>();
            ride.skipHint = host.AddComponent<LastRouteRideSkipHintView>();
            cablewayLine.Moved += ride.HandleLineMoved;
            cabinSeat.SeatedChanged += ride.HandleSeatedChanged;
            return ride;
        }

        private void HandleSeatedChanged(bool seated)
        {
            if (!seated)
            {
                return;
            }

            if (riding || resumingArrival)
            {
                // The arrival seats him itself, with the line already at rest
                // and the journey already over. That is not a departure.
                return;
            }

            BeginLeg();
        }

        private void BeginLeg()
        {
            seat.BeginAttachment();
            line.Resume();
            riding = true;
            GameSessionState.SetRidingTheCableway(true);
            skipHint.Show(SkipPromptKey);
            GameLog.Info(
                "cableway",
                "ride_started",
                GameLog.Field("destination", destinationArea.ToString()));
        }

        /// <summary>
        /// The arrival, held under the black screen until the transition flag
        /// is down. Nothing an arrival wants to START may run in `Awake`.
        /// </summary>
        private IEnumerator AwaitArrivalStart()
        {
            while (AreaTravelService.IsTraveling ||
                   SceneTransitionService.IsTransitioning)
            {
                yield return null;
            }

            // One more frame, so the force-complete pass that runs on the
            // falling edge of the flag has been and gone before a seat is
            // started that it would tear straight back down.
            yield return null;

            awaitingArrivalStart = false;
            MountainRoadCablewayPlan cableway =
                arrivalCablewayFactory?.Invoke();
            if (cableway == null)
            {
                fade.FadeIn(FadeInSeconds);
                yield break;
            }

            // Bring a cabin in and seat him in it before anything is shown.
            line.RequestDockAt(cableway.BoardingLoopDistance);
            while (!line.IsDocked)
            {
                yield return null;
            }

            // Re-solve the seat against THIS station before anything reads
            // it: every point in the plan is world-space and was solved at
            // the terminal on the other mountain.
            seat.RebuildPlan(cableway);
            resumingArrival = true;
            try
            {
                if (!seat.ResumeSeated(line.DockedCabin))
                {
                    GameLog.Warning("cableway", "arrival_seat_failed");
                }
            }
            finally
            {
                resumingArrival = false;
            }

            fade.FadeIn(FadeInSeconds);
            GameLog.Info("cableway", "ride_arrived");
        }

        /// <summary>
        /// The passenger is written here, in the line's own call, and the
        /// fade is decided on the same sample the cabin was posed from.
        /// </summary>
        private void HandleLineMoved()
        {
            if (!riding)
            {
                return;
            }

            seat.RefreshAttachedPose();
            if (fading || skipping)
            {
                return;
            }

            float travelled = line.TravelledDistance;
            if (travelled < FadeTriggerDistance())
            {
                return;
            }

            BeginDeparture();
        }

        /// <summary>
        /// How much rope has to run before the cabin is behind the ridge. The
        /// cabin boards at loop distance zero, so this is simply how far up
        /// the visible line it has to get.
        /// </summary>
        private float FadeTriggerDistance()
        {
            return Mathf.Max(1f, departureLineLength - FadeLeadMeters);
        }

        private void BeginDeparture()
        {
            fading = true;
            skipHint.Hide();
            fade.FadeOut(FadeOutSeconds);
            StartCoroutine(TravelWhenBlack(FadeOutSeconds));
        }

        private IEnumerator TravelWhenBlack(float fadeSeconds)
        {
            float deadline = Time.unscaledTime + fadeSeconds + 1.5f;
            while (!fade.IsFullyBlack && Time.unscaledTime < deadline)
            {
                yield return null;
            }

            riding = false;
            GameSessionState.SetRidingTheCableway(false);
            seat.EndAttachment();
            AreaTravelService.Request(
                new AreaTravelRequest(
                    destinationArea,
                    AreaArrivalToken.Cableway));
        }

        /// <summary>
        /// Skips the climb THROUGH THE BLACK. Nothing moves in the open: the
        /// screen goes out first, the jump is applied while it is fully out,
        /// and it comes back. A mountain that changes shape around a cabin
        /// that did not turn is a glitch in any framing.
        /// </summary>
        public bool TrySkipRide()
        {
            if (!CanSkipRide)
            {
                return false;
            }

            skipping = true;
            skipHint.Hide();
            fade.FadeOut(SkipFadeOutSeconds);
            StartCoroutine(SkipWhenBlack());
            return true;
        }

        private IEnumerator SkipWhenBlack()
        {
            float deadline = Time.unscaledTime + SkipFadeOutSeconds + 1.5f;
            while (!fade.IsFullyBlack && Time.unscaledTime < deadline)
            {
                yield return null;
            }

            riding = false;
            skipping = false;
            GameSessionState.SetRidingTheCableway(false);
            seat.EndAttachment();
            AreaTravelService.Request(
                new AreaTravelRequest(
                    destinationArea,
                    AreaArrivalToken.Cableway));
        }

        private void Update()
        {
            if (CanSkipRide && WasSkipPressed())
            {
                TrySkipRide();
            }
        }

        /// <summary>
        /// The house idiom for a hotkey: keyboard, null-guarded, and the
        /// effect behind a public method so it can be exercised without an
        /// `Update` tick. `F10` is the car's key too - the two rides can
        /// never be in flight at once.
        /// </summary>
        private static bool WasSkipPressed()
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && keyboard.f10Key.wasPressedThisFrame;
        }

        private void OnDestroy()
        {
            if (line != null)
            {
                line.Moved -= HandleLineMoved;
            }

            if (seat != null)
            {
                seat.SeatedChanged -= HandleSeatedChanged;
            }
        }
    }
}
