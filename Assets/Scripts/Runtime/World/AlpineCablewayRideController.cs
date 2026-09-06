using System;
using System.Collections;
using UnityEngine;

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
        private float departureFadeLeadMeters;

        private LastRouteRideFadeView fade;
        private LastRouteRideSkipHintView skipHint;

        private bool riding;
        private bool awaitingArrivalStart;
        private bool fading;
        private bool skipping;
        private bool resumingArrival;
        private IDisposable rideOwnership;

        public bool IsRiding => riding;
        public bool IsAwaitingStart => awaitingArrivalStart;
        public bool CanSkipRide => riding && !fading && !skipping &&
            GameInput.CanRead(GameInputContext.Gameplay);
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
            ride.departureFadeLeadMeters =
                EvaluateFadeLeadMeters(departureCableway);
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
            //
            // Its line length and fade lead cannot be taken here: the plan for
            // THIS station arrives through a factory that must not be called
            // until the area transition has let go. They are set in
            // `AwaitArrivalStart`, where it is already in hand.
            //
            // Until the village had ground under its rope this was left unset
            // on purpose, and the `1 m` floor `FadeTriggerDistance` falls back
            // to was the only thing hiding the descent: the line dived into
            // the hillside a metre off the platform. The terrain now cuts a
            // real brink under it and closes again over the far turn, so the
            // ride can be flown.
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
                ReleaseRideOwnership();
                return;
            }

            if (!isActiveAndEnabled || riding || resumingArrival)
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
            rideOwnership?.Dispose();
            rideOwnership = GameSessionState.AcquireCablewayRide();
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

            // The ride back down is armed HERE, off this station's own plan,
            // because this is the first moment that plan exists. Without it
            // `FadeTriggerDistance` falls back to its `1 m` floor and the
            // descent cuts to black almost as soon as it moves.
            departureLineLength = cableway.LineLength;
            departureFadeLeadMeters = EvaluateFadeLeadMeters(cableway);

            // Seat him before anything is shown. The line at this end is built
            // standing with a cabin on the point, so there is normally nothing
            // to call and nothing to wait for; the request stays as the way
            // back for a line that is somehow already running.
            if (!line.IsDocked)
            {
                line.RequestDockAt(cableway.BoardingLoopDistance);
                while (!line.IsDocked)
                {
                    yield return null;
                }
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
        /// How far short of the far turn the screen starts going out.
        ///
        /// This was `5.5 m`, chosen by eye, and it was wrong by four metres.
        /// The ridge that hides the upper turn is planted ON the line and its
        /// near face crosses the track `6.8 m` before the end; the cabin's own
        /// nose reaches that face `0.8 m` sooner still. At `5.5` the blackout
        /// did not even BEGIN until `1.3 m` after the nose was in the rock and
        /// was not complete for another `2.7 m`, so the passenger rode nearly
        /// four metres inside the mountain with the picture up - and a
        /// single-sided ridge has no back face, so from in there he was
        /// looking at the world straight through the rock.
        ///
        /// So it is no longer a number. It is what the rock, the cabin, the
        /// line's own speed and the length of the dissolve say it has to be.
        /// </summary>
        public static float EvaluateFadeLeadMeters(
            MountainRoadCablewayPlan cableway)
        {
            if (cableway == null)
            {
                throw new ArgumentNullException(nameof(cableway));
            }

            return cableway.LineLength -
                   cableway.LastVisibleDistance +
                   cableway.CabinSpeed * FadeOutSeconds;
        }

        /// <summary>
        /// How much rope has to run before the fade starts. The cabin boards
        /// at loop distance zero, so this is simply how far up the visible
        /// line it has to get.
        /// </summary>
        private float FadeTriggerDistance()
        {
            return Mathf.Max(
                1f,
                departureLineLength - departureFadeLeadMeters);
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

            ReleaseRideOwnership();
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

            skipping = false;
            ReleaseRideOwnership();
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
            return GameInput.WasPressed(
                GameInputAction.SkipRide, GameInputContext.Gameplay);
        }

        private void ReleaseRideOwnership()
        {
            riding = false;
            rideOwnership?.Dispose();
            rideOwnership = null;
            if (skipHint != null)
            {
                skipHint.Hide();
            }
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            ReleaseRideOwnership();
        }

        private void OnDestroy()
        {
            ReleaseRideOwnership();
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
