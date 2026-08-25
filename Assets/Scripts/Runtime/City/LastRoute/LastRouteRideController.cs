using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Owns the one journey out of the city, on whichever side of it this
    /// scene happens to be.
    ///
    /// Nothing else knows the whole shape. The seat knows how to hold a
    /// passenger, the driver knows how to follow a road, the Ferryman knows
    /// his own postures and the area service knows how to swap two worlds -
    /// and this is the thing that says in what order, which is exactly the
    /// division `CityBusRideController` already draws around the bus.
    ///
    /// **City side.** He sits down, the leaf shuts, and the car pulls off the
    /// lot and drives the real streets to the south portal. Somewhere inside
    /// the tunnel the screen goes under and the area load is asked for.
    ///
    /// **Mountain side.** It comes back already moving, six metres inside the
    /// other tunnel, with the same man at the wheel and the same hero in the
    /// seat. Six hundred and twenty metres later it stops on the terrace by
    /// the cafe and lets him out; and then the Ferryman gets out himself and
    /// climbs back onto his bonnet, and that is where he stays.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(320)]
    public sealed class LastRouteRideController : MonoBehaviour
    {
        public const string RuntimeObjectName = "Last Route Ride";

        /// <summary>
        /// How much road is left when the screen starts going under. The car
        /// is braking into the end of its path by then, so this is a little
        /// over a second and a half of tunnel and the fade finishes with the
        /// car still rolling.
        /// </summary>
        public const float FadeLeadMeters = 13f;

        /// <summary>
        /// How fast the car is going the instant the mountain road appears.
        /// It never stopped, so it must not arrive from rest - and it is a
        /// touch under the mountain cruise because it is coming out of a
        /// tunnel onto a climb.
        /// </summary>
        public const float MountainEntrySpeed = 5.2f;

        private enum Leg
        {
            City,
            Mountain
        }

        private Leg leg;
        private LastRouteCarSeatInteraction seat;
        private LastRouteCarDriver driver;
        private LastRouteFerrymanPresentation ferryman;
        private LastRouteRideFadeView fade;
        private Func<LastRouteCarDrivePath> buildPath;
        private bool driveBegun;
        private bool travelRequested;
        private bool warnedTravelRefused;
        private bool awaitingMountainStart;
        private float awaitedSeconds;

        public bool IsRiding { get; private set; }

        /// <summary>
        /// True while the mountain half is built but holding, waiting for the
        /// area service to finish before it dares put the hero in the seat.
        /// The screen is black throughout.
        /// </summary>
        public bool IsAwaitingStart => awaitingMountainStart;

        /// <summary>
        /// The city half: armed and waiting for the hero to actually sit
        /// down. Nothing happens until he does, and if he never does, this
        /// costs one idle component.
        /// </summary>
        public static LastRouteRideController CreateForCity(
            Transform parent,
            LastRouteCarSeatInteraction carSeat,
            LastRouteCarDriver carDriver,
            LastRouteFerrymanPresentation ferrymanPresentation,
            Func<LastRouteCarDrivePath> cityPathFactory)
        {
            LastRouteRideController controller = Create(
                parent,
                carSeat,
                carDriver,
                ferrymanPresentation);
            if (controller == null)
            {
                return null;
            }

            controller.leg = Leg.City;
            controller.buildPath = cityPathFactory;
            controller.seat.Seated += controller.HandleSeated;
            return controller;
        }

        /// <summary>
        /// The mountain half: the hero is already in the seat and the car is
        /// already inside the tunnel, so this starts driving on its first
        /// frame rather than waiting to be asked.
        /// </summary>
        public static LastRouteRideController CreateForMountain(
            Transform parent,
            LastRouteCarSeatInteraction carSeat,
            LastRouteCarDriver carDriver,
            LastRouteFerrymanPresentation ferrymanPresentation,
            Func<LastRouteCarDrivePath> mountainPathFactory)
        {
            LastRouteRideController controller = Create(
                parent,
                carSeat,
                carDriver,
                ferrymanPresentation);
            if (controller == null)
            {
                return null;
            }

            controller.leg = Leg.Mountain;
            controller.buildPath = mountainPathFactory;
            controller.fade.SetBlack();

            // NOT started here, and that is the whole point. This runs from
            // `MountainRoadRoot.Awake`, which the area service calls while its
            // own coroutine is still running - `allowSceneActivation` is set,
            // the destination wakes, and only some frames later does
            // `Complete` clear the flag. Until it does,
            // `SceneTransitionService.IsTransitioning` is true, and
            // `PlayerAnimatedInteractionController.Update` force-completes any
            // interaction that is running while it is. Seating the hero here
            // therefore seated him and then threw him straight back out onto
            // the tunnel floor, and his car drove up the mountain without him.
            controller.awaitingMountainStart = true;
            return controller;
        }

        private static LastRouteRideController Create(
            Transform parent,
            LastRouteCarSeatInteraction carSeat,
            LastRouteCarDriver carDriver,
            LastRouteFerrymanPresentation ferrymanPresentation)
        {
            if (carSeat == null || carDriver == null)
            {
                return null;
            }

            var host = new GameObject(RuntimeObjectName);
            host.transform.SetParent(parent, false);
            var controller = host.AddComponent<LastRouteRideController>();
            controller.seat = carSeat;
            controller.driver = carDriver;
            controller.ferryman = ferrymanPresentation;
            controller.fade = LastRouteRideFadeView.Create(host.transform);
            carSeat.AttachDriver(carDriver);
            carSeat.Alighted += controller.HandleAlighted;
            carDriver.Arrived += controller.HandleArrived;
            return controller;
        }

        private void HandleSeated()
        {
            if (driveBegun || leg != Leg.City)
            {
                return;
            }

            LastRouteCarDrivePath path = TryBuildPath();
            if (path == null)
            {
                return;
            }

            driveBegun = true;
            IsRiding = true;
            GameSessionState.TryAdvanceFerrymanRide(
                LastRouteFerrymanRideStage.InTransit);
            seat.BeginRideAttachment();
            driver.Begin(path, LastRouteCarDriveProfile.City);
        }

        /// <summary>
        /// Holds the mountain half on the starting line until the area service
        /// has genuinely finished. One or two frames in practice, spent under
        /// a screen that is already fully black.
        /// </summary>
        private void AwaitMountainStart()
        {
            if (SceneTransitionService.IsTransitioning)
            {
                awaitedSeconds += Time.unscaledDeltaTime;
                if (awaitedSeconds > 5f && !warnedTravelRefused)
                {
                    // Never seen; if it ever is, the whole scene service is
                    // wedged and this is the breadcrumb that says so.
                    warnedTravelRefused = true;
                    GameLog.Warning(
                        "lastroute",
                        "mountain_start_still_waiting",
                        GameLog.Field("seconds", awaitedSeconds));
                }

                return;
            }

            awaitingMountainStart = false;
            BeginMountainLeg();
        }

        private void BeginMountainLeg()
        {
            LastRouteCarDrivePath path = TryBuildPath();
            if (path == null)
            {
                fade.FadeIn();
                return;
            }

            driveBegun = true;
            IsRiding = true;
            ferryman?.BeginSeatedAtTheWheel();
            if (!seat.ResumeSeated())
            {
                // He could not be put back in the seat, which would leave him
                // standing in a tunnel watching his own car drive off. Better
                // to hand the area back its ordinary spawn than to run the
                // beat without him in it.
                GameLog.Warning("lastroute", "mountain_resume_failed");
                fade.FadeIn();
                return;
            }

            seat.BeginRideAttachment();
            driver.Begin(
                path,
                LastRouteCarDriveProfile.Mountain,
                MountainEntrySpeed);
            fade.FadeIn();
        }

        private LastRouteCarDrivePath TryBuildPath()
        {
            if (buildPath == null)
            {
                return null;
            }

            try
            {
                return buildPath();
            }
            catch (Exception exception)
            {
                GameLog.Warning(
                    "lastroute",
                    "ride_path_failed",
                    GameLog.Field("message", exception.Message));
                return null;
            }
        }

        private void Update()
        {
            if (awaitingMountainStart)
            {
                AwaitMountainStart();
                return;
            }

            if (leg != Leg.City || !IsRiding || travelRequested)
            {
                return;
            }

            if (driver.Model != null &&
                driver.Model.Remaining <= FadeLeadMeters)
            {
                fade.FadeOut();
            }

            if (!fade.IsFullyBlack)
            {
                return;
            }

            GameLog.Info("lastroute", "ride_requesting_mountain_road");
            if (AreaTravelService.Request(
                    new AreaTravelRequest(
                        GameAreaId.MountainRoad,
                        AreaArrivalToken.Ferryman)))
            {
                travelRequested = true;
                return;
            }

            // Refused, which in practice means the service was already busy.
            // The screen stays under and this asks again next frame rather
            // than fading back up onto a tunnel wall: the ride stage is
            // already `InTransit` and there is nothing on this side of the
            // load left to look at.
            if (!warnedTravelRefused)
            {
                warnedTravelRefused = true;
                GameLog.Warning("lastroute", "ride_travel_refused");
            }
        }

        /// <summary>
        /// The car has stopped on the terrace. The hero gets his controller
        /// back and the seat re-solves its dock against a car that is now six
        /// hundred metres and twenty-six metres of altitude from where that
        /// dock was worked out.
        /// </summary>
        private void HandleArrived()
        {
            if (leg != Leg.Mountain)
            {
                return;
            }

            IsRiding = false;

            // Re-solve the seat BEFORE giving the hero his controller back,
            // and the order is load-bearing. `LastRouteCarSeatPlan` finds the
            // height the hero will stand at by raycasting down at the dock,
            // and the hero is standing on that dock - so with his
            // `CharacterController` live again the probe hits HIM and returns
            // his shoulder. That put the entry root a metre and a half in the
            // air, and `CanInteract`'s own vertical tolerance then refused to
            // open the door at all: the ride worked perfectly and ended with
            // the passenger sealed in.
            seat.RebuildPlanFromCar();
            seat.EndRideAttachment();
            GameSessionState.TryAdvanceFerrymanRide(
                LastRouteFerrymanRideStage.Arrived);
        }

        /// <summary>
        /// He is out and standing on the terrace. Only now does the Ferryman
        /// get out himself - he waited for his passenger, which is the whole
        /// difference between a driver and a machine.
        /// </summary>
        private void HandleAlighted()
        {
            if (leg != Leg.Mountain || driver.IsDriving)
            {
                return;
            }

            ferryman?.TryBeginAlighting();
        }

        private void OnDestroy()
        {
            if (seat != null)
            {
                seat.Seated -= HandleSeated;
                seat.Alighted -= HandleAlighted;
            }

            if (driver != null)
            {
                driver.Arrived -= HandleArrived;
            }
        }
    }
}
