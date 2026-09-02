using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    /// <summary>
    /// Owns one leg of the Ferryman's journey, on whichever side of the
    /// mountain this scene happens to be and whichever way round it is going.
    ///
    /// Nothing else knows the whole shape. The seat knows how to hold a
    /// passenger, the driver knows how to follow a road, the Ferryman knows
    /// his own postures and the area service knows how to swap two worlds -
    /// and this is the thing that says in what order, which is exactly the
    /// division `CityBusRideController` already draws around the bus.
    ///
    /// There are two kinds of leg and four uses of them, which is the whole
    /// reason this is one class rather than two:
    ///
    /// **Departing.** He sits down, the leaf shuts, and the car pulls away and
    /// drives a real road to a tunnel mouth. Somewhere inside the tunnel the
    /// screen goes under and the area load is asked for. That is the island to
    /// the city's south portal on the way up, and the cafe terrace to the
    /// mountain's own portal on the way back down.
    ///
    /// **Arriving.** It comes out of the other tunnel already moving, with the
    /// same man at the wheel and the same hero in the seat, and drives to
    /// wherever this side's journey ends. Then it stops, lets him out, and the
    /// Ferryman gets out himself and climbs back onto his bonnet.
    ///
    /// The pairing is deliberate and it is what keeps the round trip honest:
    /// a departure and the arrival that answers it stop and start at the same
    /// point in the same tunnel, and each of the four is a `Func` handing this
    /// one road.
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

        /// <summary>
        /// And the same on the way home. A shade quicker than the climb's
        /// entry because this one comes out of a straight `12 m` throat onto
        /// a level forecourt rather than out of a stub onto an `8 %` grade -
        /// but still well under the city cruise, because the first thing it
        /// has to do is give way at the junction it is rolling toward.
        /// </summary>
        public const float CityEntrySpeed = 6f;

        /// <summary>
        /// The line offered while a ride is running itself out, and the key
        /// that takes it. `F10` because it is one of the few genuinely unbound
        /// keys left - `E`, `Space`, `Enter` and `Escape` are all spoken for
        /// several times over, and `F8`/`F9` belong to the debug window and
        /// the Home shortcut. The hint names the key, so it does not have to
        /// be guessable.
        /// </summary>
        public const string SkipPromptKey = "lastroute.ride.skip";

        /// <summary>
        /// How long the screen takes to go under for a skip, and to come
        /// back. Brisker than the tunnel's own `1.4`/`0.9`, which is a car
        /// being swallowed and is meant to be watched: this is a player who
        /// has pressed a key and is waiting for the game to get on with it.
        /// </summary>
        public const float SkipFadeOutSeconds = 0.6f;

        public const float SkipFadeInSeconds = 0.8f;

        private enum Leg
        {
            /// <summary>Waiting to be sat in, then driving to a tunnel and
            /// asking for the other world.</summary>
            Departing,

            /// <summary>Already moving when the world appeared, and driving to
            /// the place this side's journey ends.</summary>
            Arriving
        }

        private Leg leg;
        private LastRouteCarSeatInteraction seat;
        private LastRouteCarDriver driver;
        private LastRouteFerrymanPresentation ferryman;
        private LastRouteRideFadeView fade;
        private Func<LastRouteCarDrivePath> buildPath;
        private LastRouteCarDriveProfile profile;
        private CityBusDirector buses;
        private CityPedestrianDirector pedestrians;
        private AreaTravelRequest travelRequest;
        private LastRouteFerrymanRideStage reachedStage;
        private float entrySpeed;
        private Func<LastRouteCarDrivePath> buildNextPath;
        private LastRouteCarDriveProfile nextProfile;
        private AreaTravelRequest nextTravelRequest;
        private LastRouteFerrymanRideStage nextStage;
        private bool driveBegun;
        private bool travelRequested;
        private bool warnedTravelRefused;
        private bool awaitingArrivalStart;
        private float awaitedSeconds;
        private bool skipRequested;
        private bool skipApplied;

        public bool IsRiding { get; private set; }

        /// <summary>
        /// True while an arriving half is built but holding, waiting for the
        /// area service to finish before it dares put the hero in the seat.
        /// The screen is black throughout.
        /// </summary>
        public bool IsAwaitingStart => awaitingArrivalStart;

        /// <summary>
        /// The turn across the road, once the car is on its way to it. Null
        /// until there is a road, and on any road that never leaves its lane.
        /// </summary>
        public LastRouteCarGiveWay GiveWay { get; private set; }

        /// <summary>The corner line telling the player he can skip the rest
        /// of the ride.</summary>
        public LastRouteRideSkipHintView SkipHint { get; private set; }

        /// <summary>The black the journey passes through, exposed so a test
        /// can watch the skip go under rather than infer it.</summary>
        public LastRouteRideFadeView Fade => fade;

        /// <summary>
        /// True while the ride can be cut short: an ARRIVING leg, actually
        /// driving, with road left to cover, and not already being cut short.
        ///
        /// Only arriving legs, and the reason is not politeness. A departure
        /// ends by fading out and asking for the other world, so its last
        /// stretch of road is already the handover; jumping the car to the end
        /// of it would race the thing that is watching for the end of it.
        /// </summary>
        public bool CanSkipRide =>
            leg == Leg.Arriving &&
            IsRiding &&
            !skipRequested &&
            driver != null &&
            driver.IsDriving &&
            driver.Model != null &&
            !driver.Model.HasArrived;

        /// <summary>True from the moment the key is pressed until the car is
        /// at the end of its road and the screen is on its way back.</summary>
        public bool IsSkipping => skipRequested && !skipApplied;

        /// <summary>
        /// Asks for the rest of the ride to be given up.
        ///
        /// It does not move anything. The screen goes under first and the car
        /// is put at the end of its road from inside the black, because the
        /// jump is hundreds of metres in a single frame and there is no
        /// framing in which that is not a glitch: the world would visibly
        /// change shape around a car that did not turn.
        /// </summary>
        public bool TrySkipRide()
        {
            if (!CanSkipRide)
            {
                return false;
            }

            skipRequested = true;
            SkipHint?.Hide();
            fade?.FadeOut(SkipFadeOutSeconds);
            GameLog.Info(
                "lastroute",
                "ride_skip_requested",
                GameLog.Field("distance", driver.Distance));

            // A scene with no fade of its own has nothing to wait for.
            if (fade == null)
            {
                ApplySkip();
            }

            return true;
        }

        /// <summary>
        /// The jump itself, from under a screen that is already fully black.
        ///
        /// It moves the DISTANCE and nothing else, so what follows is the
        /// ordinary arrival rather than a second one written for the skip:
        /// the driver writes the pose, raises `Moved` - which is what carries
        /// the hero - runs out of road and raises `Arrived`. Everything
        /// world-space that would go stale is re-solved there already,
        /// because a car that drives the whole way has the same problem.
        /// </summary>
        private void ApplySkip()
        {
            skipApplied = true;

            // The car may have finished the road on its own while the screen
            // was going down, in which case there is nothing to move and the
            // arrival has already run. Either way the screen comes back.
            driver?.SkipToEnd();
            fade?.FadeIn(SkipFadeInSeconds);
            GameLog.Info("lastroute", "ride_skipped");
        }

        private void UpdateSkip()
        {
            if (!skipRequested || skipApplied || fade == null)
            {
                return;
            }

            if (fade.IsFullyBlack)
            {
                ApplySkip();
            }
        }

        /// <summary>
        /// The way out of the city: armed and waiting for the hero to actually
        /// sit down. Nothing happens until he does, and if he never does, this
        /// costs one idle component.
        /// </summary>
        public static LastRouteRideController CreateForCityDeparture(
            Transform parent,
            LastRouteCarSeatInteraction carSeat,
            LastRouteCarDriver carDriver,
            LastRouteFerrymanPresentation ferrymanPresentation,
            Func<LastRouteCarDrivePath> cityPathFactory,
            CityBusDirector busDirector = null,
            CityPedestrianDirector pedestrianDirector = null)
        {
            return CreateDeparture(
                parent,
                carSeat,
                carDriver,
                ferrymanPresentation,
                cityPathFactory,
                LastRouteCarDriveProfile.City,
                new AreaTravelRequest(
                    GameAreaId.MountainRoad,
                    AreaArrivalToken.Ferryman),
                LastRouteFerrymanRideStage.InTransit,
                busDirector,
                pedestrianDirector);
        }

        /// <summary>
        /// The way off the mountain: the same offer at the other end of the
        /// same road, taken from the terrace by the cafe.
        ///
        /// Nothing up here is traffic - there is no bus and there are no
        /// walkers on that road - so this arms no directors, and the road it
        /// is handed declares no give-way for them to answer.
        /// </summary>
        public static LastRouteRideController CreateForMountainDeparture(
            Transform parent,
            LastRouteCarSeatInteraction carSeat,
            LastRouteCarDriver carDriver,
            LastRouteFerrymanPresentation ferrymanPresentation,
            Func<LastRouteCarDrivePath> mountainPathFactory)
        {
            return CreateDeparture(
                parent,
                carSeat,
                carDriver,
                ferrymanPresentation,
                mountainPathFactory,
                LastRouteCarDriveProfile.Mountain,
                new AreaTravelRequest(
                    GameAreaId.City,
                    AreaArrivalToken.FerrymanReturn),
                LastRouteFerrymanRideStage.Returning);
        }

        /// <summary>
        /// The top of the road: the hero is already in the seat and the car is
        /// already inside the mountain's tunnel, so this starts driving on its
        /// first frame rather than waiting to be asked.
        /// </summary>
        public static LastRouteRideController CreateForMountainArrival(
            Transform parent,
            LastRouteCarSeatInteraction carSeat,
            LastRouteCarDriver carDriver,
            LastRouteFerrymanPresentation ferrymanPresentation,
            Func<LastRouteCarDrivePath> arrivalPathFactory,
            Func<LastRouteCarDrivePath> departurePathFactory = null)
        {
            LastRouteRideController controller = CreateArrival(
                parent,
                carSeat,
                carDriver,
                ferrymanPresentation,
                arrivalPathFactory,
                LastRouteCarDriveProfile.Mountain,
                MountainEntrySpeed,
                LastRouteFerrymanRideStage.Arrived);

            // And the way back down, armed on the same component rather than
            // on a second one beside it. Two controllers over one car would
            // each raise their own black screen and each subscribe to the same
            // driver; one that changes what it is when the car stops has a
            // single fade, a single hint and a single binding for the engine
            // under the bonnet to have been handed.
            controller?.ArmDeparture(
                departurePathFactory,
                LastRouteCarDriveProfile.Mountain,
                new AreaTravelRequest(
                    GameAreaId.City,
                    AreaArrivalToken.FerrymanReturn),
                LastRouteFerrymanRideStage.Returning);
            return controller;
        }

        /// <summary>
        /// And the bottom of it: out of the city's own south portal, back
        /// across the forecourt and down the streets to the island.
        ///
        /// The ring closes here. The stage this arrival reaches is
        /// <see cref="LastRouteFerrymanRideStage.NotTaken"/> - not because
        /// nothing happened, but because that value means one thing only, and
        /// it is where the car is.
        /// </summary>
        public static LastRouteRideController CreateForCityArrival(
            Transform parent,
            LastRouteCarSeatInteraction carSeat,
            LastRouteCarDriver carDriver,
            LastRouteFerrymanPresentation ferrymanPresentation,
            Func<LastRouteCarDrivePath> cityPathFactory,
            CityBusDirector busDirector = null,
            CityPedestrianDirector pedestrianDirector = null)
        {
            LastRouteRideController controller = CreateArrival(
                parent,
                carSeat,
                carDriver,
                ferrymanPresentation,
                cityPathFactory,
                LastRouteCarDriveProfile.City,
                CityEntrySpeed,
                LastRouteFerrymanRideStage.NotTaken);
            if (controller != null)
            {
                controller.buses = busDirector;
                controller.pedestrians = pedestrianDirector;
            }

            return controller;
        }

        /// <summary>
        /// Says what this leg turns into when it stops.
        ///
        /// Only an arriving leg has anywhere to turn into, and only where the
        /// scene has a road for it: the mountain, where the man who has just
        /// driven up can be asked to drive back. The city's homecoming arms
        /// nothing, because the car it parks is turned round in its own bay
        /// and the offer belongs to the next time the city is built.
        /// </summary>
        private void ArmDeparture(
            Func<LastRouteCarDrivePath> pathFactory,
            LastRouteCarDriveProfile driveProfile,
            AreaTravelRequest request,
            LastRouteFerrymanRideStage transitStage)
        {
            if (pathFactory == null || leg != Leg.Arriving)
            {
                return;
            }

            buildNextPath = pathFactory;
            nextProfile = driveProfile;
            nextTravelRequest = request;
            nextStage = transitStage;
        }

        /// <summary>
        /// The car has stopped and there is a road out of here. It stops being
        /// an arrival and becomes an offer: everything the finished leg used
        /// is cleared, and nothing happens again until the hero sits down.
        /// </summary>
        private void TurnIntoDeparture()
        {
            leg = Leg.Departing;
            buildPath = buildNextPath;
            profile = nextProfile;
            travelRequest = nextTravelRequest;
            reachedStage = nextStage;
            buildNextPath = null;
            driveBegun = false;
            travelRequested = false;
            warnedTravelRefused = false;
            skipRequested = false;
            skipApplied = false;
            SkipHint?.Hide();
            seat.Seated += HandleSeated;
        }

        private static LastRouteRideController CreateDeparture(
            Transform parent,
            LastRouteCarSeatInteraction carSeat,
            LastRouteCarDriver carDriver,
            LastRouteFerrymanPresentation ferrymanPresentation,
            Func<LastRouteCarDrivePath> pathFactory,
            LastRouteCarDriveProfile driveProfile,
            AreaTravelRequest request,
            LastRouteFerrymanRideStage transitStage,
            CityBusDirector busDirector = null,
            CityPedestrianDirector pedestrianDirector = null)
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

            controller.leg = Leg.Departing;
            controller.buildPath = pathFactory;
            controller.profile = driveProfile;
            controller.travelRequest = request;
            controller.reachedStage = transitStage;
            controller.buses = busDirector;
            controller.pedestrians = pedestrianDirector;
            controller.seat.Seated += controller.HandleSeated;
            return controller;
        }

        private static LastRouteRideController CreateArrival(
            Transform parent,
            LastRouteCarSeatInteraction carSeat,
            LastRouteCarDriver carDriver,
            LastRouteFerrymanPresentation ferrymanPresentation,
            Func<LastRouteCarDrivePath> pathFactory,
            LastRouteCarDriveProfile driveProfile,
            float initialSpeed,
            LastRouteFerrymanRideStage arrivalStage)
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

            controller.leg = Leg.Arriving;
            controller.buildPath = pathFactory;
            controller.profile = driveProfile;
            controller.entrySpeed = initialSpeed;
            controller.reachedStage = arrivalStage;
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
            controller.awaitingArrivalStart = true;
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
            controller.SkipHint =
                LastRouteRideSkipHintView.Create(host.transform);
            carSeat.AttachDriver(carDriver);
            carSeat.Alighted += controller.HandleAlighted;
            carDriver.Arrived += controller.HandleArrived;
            return controller;
        }

        private void HandleSeated()
        {
            if (driveBegun || leg != Leg.Departing)
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
            GameSessionState.TryAdvanceFerrymanRide(reachedStage);
            seat.BeginRideAttachment();
            driver.Begin(path, profile);

            // And the one place on that road where he has to look before he
            // goes. Armed after the drive rather than with it, because the
            // road is built lazily and until it exists there is no line to
            // measure and no crossing to watch.
            GiveWay = LastRouteCarGiveWay.Attach(
                driver,
                path,
                buses,
                pedestrians);
        }

        /// <summary>
        /// Holds an arriving half on the starting line until the area service
        /// has genuinely finished. One or two frames in practice, spent under
        /// a screen that is already fully black.
        /// </summary>
        private void AwaitArrivalStart()
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
                        "arrival_start_still_waiting",
                        GameLog.Field("seconds", awaitedSeconds));
                }

                return;
            }

            awaitingArrivalStart = false;
            BeginArrivingLeg();
        }

        private void BeginArrivingLeg()
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
                GameLog.Warning("lastroute", "arrival_resume_failed");
                fade.FadeIn();
                return;
            }

            seat.BeginRideAttachment();
            driver.Begin(path, profile, entrySpeed);

            // The city's homecoming crosses live traffic at the same junction
            // the departure crossed - out of the forecourt this time instead
            // of into it - so it is watched the same way. The road says
            // whether there is anything to watch; the mountain's declares
            // nothing and this returns null there.
            GiveWay = LastRouteCarGiveWay.Attach(
                driver,
                path,
                buses,
                pedestrians);
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

        /// <summary>
        /// The house idiom for a hotkey: keyboard, null-guarded, and the
        /// effect behind a public method so it can be exercised without an
        /// `Update` tick. `Keyboard.current` is null in batch mode until a
        /// test supplies a device.
        /// </summary>
        private static bool WasSkipPressed()
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && keyboard.f10Key.wasPressedThisFrame;
        }

        private void UpdateSkipOffer()
        {
            if (SkipHint == null)
            {
                return;
            }

            if (!CanSkipRide)
            {
                SkipHint.Hide();
                return;
            }

            // Not while the screen is still black over the area load: a hint
            // offered to a player who cannot see the road yet is a hint he
            // takes before he has seen anything at all.
            if (fade != null && !fade.IsClear)
            {
                SkipHint.Hide();
                return;
            }

            SkipHint.Show(SkipPromptKey);
            if (WasSkipPressed())
            {
                TrySkipRide();
            }
        }

        private void Update()
        {
            if (awaitingArrivalStart)
            {
                AwaitArrivalStart();
                return;
            }

            UpdateSkip();
            UpdateSkipOffer();
            if (leg != Leg.Departing || !IsRiding || travelRequested)
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

            GameLog.Info(
                "lastroute",
                "ride_requesting_area",
                GameLog.Field(
                    "area",
                    travelRequest.DestinationArea.ToString()));
            if (AreaTravelService.Request(travelRequest))
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
        /// The car has stopped. The hero gets his controller back and the seat
        /// re-solves its dock against a car that is now most of a kilometre -
        /// and, on the climb, twenty-six metres of altitude - from where that
        /// dock was worked out.
        /// </summary>
        private void HandleArrived()
        {
            if (leg != Leg.Arriving)
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
            GameSessionState.TryAdvanceFerrymanRide(reachedStage);
            if (buildNextPath != null)
            {
                TurnIntoDeparture();
            }
        }

        /// <summary>
        /// He is out and standing on his own feet. Only now does the Ferryman
        /// get out himself - he waited for his passenger, which is the whole
        /// difference between a driver and a machine.
        /// </summary>
        private void HandleAlighted()
        {
            if (leg != Leg.Arriving || driver.IsDriving)
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
