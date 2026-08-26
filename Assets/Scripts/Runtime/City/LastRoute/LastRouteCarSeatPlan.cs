using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The passenger seat of the parked car, described the way the bus and
    /// the benches describe theirs: a dock to walk to, a hip to settle on,
    /// and a waypoint in the doorway between the two.
    ///
    /// Everything is read off the car's own anchors rather than authored a
    /// second time here, so the seat can never drift from the bodywork the
    /// hero is looking at through the glass.
    /// </summary>
    public readonly struct LastRouteCarSeatPlan
    {
        // How far out from the car's centreline the hero stands before he
        // gets in, and how far BACK along the flank.
        //
        // The standoff alone used to be 1.52 m at the door's own row, and
        // that was wrong the moment the door became a thing that opens: the
        // leaf is 1.51 m long on a hinge at the A-pillar, so a hero standing
        // square to the doorway sits 0.99 m from that hinge and the door
        // sweeps clean through him on its way to sixty-six degrees. Nothing
        // caught it because until now nothing ever moved the leaf.
        //
        // A standing point is only ever safe OUTSIDE the blade's radius -
        // the swept sector covers every bearing in between - so the dock
        // moved out and, mostly, back. That is also where a person stands to
        // open a car door: behind the swing, in the aperture, not in front
        // of it. `LastRouteCarDoors.MeasureSwingClearance` states the rule
        // and one EditMode test holds both docks to it.
        public const float DockStandoff = 1.85f;
        public const float DockRearwardShift = 1.00f;

        /// <summary>
        /// How far out the pelvis passes through the doorway on its way to
        /// the seat. Its own constant rather than a fraction of the dock:
        /// this one is a point in the door aperture and has to stay at the
        /// door's row however far back the standing point moves.
        /// </summary>
        public const float DoorwayStandoff = 0.72f;

        /// <summary>
        /// When the hero's body is allowed to be moving, as fractions of his
        /// own clips.
        ///
        /// Every one of these eight numbers is a KEY of those clips and none
        /// of them is a taste. `CarBoardEnter` is authored `relaxed 0.0,
        /// reach 0.10, pull 0.22, door_clear 0.34, seat_step 0.52,
        /// seat_settle 0.66, seat_down 0.78, door_shut 0.90, seated 1.0`, and
        /// `CarAlightExit` is that read backwards.
        ///
        /// The two HOLDS are the whole point of naming them. Without a hold
        /// the pelvis starts travelling on the clip's first frame: the hero
        /// was ninety-two per cent of the way from the dock to the doorway by
        /// the time the leaf finished opening at `0.34`, so he walked through
        /// a door he was simultaneously miming a pull on. The Ferryman never
        /// did - `LastRouteFerrymanBoardingTimeline.TravelStartPhase` holds
        /// his root at `0.36` until his own leaf stands open - and the two men
        /// doing the same beat differently was the whole of the fault.
        ///
        /// The SETTLES are the same mistake at the far end: the pelvis used to
        /// reach the seat only on the closing frame, so he was still sliding
        /// down into it while the clip already had him seated and pulling the
        /// leaf shut behind him.
        ///
        /// A test holds all four against the leaf's own phases, which is where
        /// the contract actually lives.
        /// </summary>
        public const float EnterHoldProgress = 0.34f;
        public const float EnterArrivalProgress = 0.52f;
        public const float EnterDepartureProgress = 0.60f;
        public const float EnterSettleProgress = 0.78f;
        public const float ExitHoldProgress = 0.24f;
        public const float ExitArrivalProgress = 0.52f;
        public const float ExitDepartureProgress = 0.60f;
        public const float ExitSettleProgress = 0.94f;

        public const float SeatClearance = 0.02f;
        // How far above the car's own ground the surface probe starts, and
        // therefore how much of a step up or down it can find.
        private const float ProbeHeight = 1.6f;
        public const float TriggerHeight = 1.9f;
        public const float ApproachVerticalTolerance = 0.35f;

        private LastRouteCarSeatPlan(
            bool isPresent,
            Vector3 entryRootPosition,
            Quaternion entryRotation,
            Vector3 entryHipPosition,
            Vector3 actionHipPosition,
            PlayerAnimatedInteractionPelvisTransition pelvisTransition,
            Vector3 interactionPosition,
            Vector3 triggerCenter,
            Quaternion triggerRotation,
            Vector3 triggerSize)
        {
            IsPresent = isPresent;
            EntryRootPosition = entryRootPosition;
            EntryRotation = entryRotation;
            EntryHipPosition = entryHipPosition;
            ActionHipPosition = actionHipPosition;
            PelvisTransition = pelvisTransition;
            InteractionPosition = interactionPosition;
            TriggerCenter = triggerCenter;
            TriggerRotation = triggerRotation;
            TriggerSize = triggerSize;
        }

        public bool IsPresent { get; }
        public Vector3 EntryRootPosition { get; }
        public Quaternion EntryRotation { get; }
        public Vector3 EntryHipPosition { get; }
        public Vector3 ActionHipPosition { get; }
        public PlayerAnimatedInteractionPelvisTransition PelvisTransition { get; }
        public Vector3 InteractionPosition { get; }
        public Vector3 TriggerCenter { get; }
        public Quaternion TriggerRotation { get; }
        public Vector3 TriggerSize { get; }

        public static LastRouteCarSeatPlan Absent =>
            new LastRouteCarSeatPlan(
                false,
                Vector3.zero,
                Quaternion.identity,
                Vector3.zero,
                Vector3.zero,
                default,
                Vector3.zero,
                Vector3.zero,
                Quaternion.identity,
                Vector3.one);

        /// <summary>
        /// The top of whatever the hero will be standing on at this point.
        /// Runtime rather than plan data on purpose: by the time the seat is
        /// installed the island's slab, kerbs and props are all real
        /// colliders, and the only honest answer is the one physics gives.
        /// </summary>
        internal static float ResolveStandingHeight(
            Vector3 position,
            float fallbackGroundY)
        {
            var origin = new Vector3(
                position.x,
                fallbackGroundY + ProbeHeight,
                position.z);
            if (Physics.Raycast(
                    origin,
                    Vector3.down,
                    out RaycastHit hit,
                    ProbeHeight * 2f,
                    ~0,
                    QueryTriggerInteraction.Ignore))
            {
                return hit.point.y;
            }

            return fallbackGroundY;
        }

        public static LastRouteCarSeatPlan Create(
            LastRouteCarAssetRegistry registry,
            float groundY)
        {
            if (registry == null || !registry.IsBound)
            {
                return Absent;
            }

            // The car's own axes, taken from the PREFAB ROOT and never from
            // the imported Body node.
            //
            // This was the imported-basis trap for the sixth time, and it
            // was silent: `registry.Body` is an FBX node whose forward comes
            // out very nearly vertical, so projecting it onto the ground
            // plane left a zero vector, `Quaternion.LookRotation` warned
            // "Look rotation viewing vector is zero" into the log and
            // handed back identity - and the hero rode facing world +Z
            // instead of down the bonnet, in a car whose whole point is the
            // glass. Nothing threw and no test caught it; only the City's
            // own build log said so.
            Transform car = registry.transform;
            Vector3 seat = registry.PassengerSeatAnchor.position;
            Vector3 doorHipGround = registry.PassengerDoorEntryAnchor.position;

            // Which way is out. Taken from the anchor rather than assumed,
            // so moving the passenger side in the generator moves the dock
            // with it instead of leaving the hero docking through the car.
            Vector3 right = Vector3.ProjectOnPlane(car.right, Vector3.up);
            if (right.sqrMagnitude < 0.000001f)
            {
                return Absent;
            }

            right = right.normalized;
            Vector3 outward =
                Vector3.Dot(doorHipGround - car.position, right) >= 0f
                    ? right
                    : -right;

            // Forward is the way he will be looking, so it is derived from
            // the drawn cabin rather than from any transform: the vector
            // from the driver's seat to the steering wheel has no basis to
            // get wrong. The root's own forward is the fallback for a car
            // whose wheel sits directly over its seat, which cannot happen
            // but must not produce a zero either.
            Vector3 forward = Vector3.ProjectOnPlane(
                registry.SteeringWheelPivot.position -
                registry.DriverSeatAnchor.position,
                Vector3.up);
            if (forward.sqrMagnitude < 0.000001f)
            {
                forward = Vector3.ProjectOnPlane(car.forward, Vector3.up);
                if (forward.sqrMagnitude < 0.000001f)
                {
                    return Absent;
                }
            }

            forward = forward.normalized;

            Vector3 centreline = Vector3.ProjectOnPlane(
                doorHipGround - car.position, right);
            Vector3 dock = car.position + centreline +
                (outward * DockStandoff) -
                (forward * DockRearwardShift);

            // The dock's height has to be the height the hero will actually
            // stand at, not the car's own ground. The island's paving slab,
            // its foundation and the flattened lot around it are three
            // different tops within a couple of metres, and the motor's
            // entry tolerance is two centimetres - the bench learned this
            // the same way, with a seat eight centimetres above its own
            // pavement that stalled every sitter. Sample the surface under
            // the dock; fall back to the car's ground only if nothing is
            // there to stand on.
            dock.y = ResolveStandingHeight(dock, groundY) +
                PlayerFactory.GroundedRootOffset;

            Vector3 doorwayGround = car.position + centreline +
                (outward * DoorwayStandoff);
            doorwayGround.y = dock.y;

            var action = new Vector3(
                seat.x,
                seat.y + SeatClearance,
                seat.z);

            // He rides facing the way the car is pointed - that is the whole
            // point of the glass. The dock faces the same way, so he backs
            // onto the seat exactly as he backs onto a bench.
            Quaternion entryRotation =
                Quaternion.LookRotation(forward, Vector3.up);

            var transition = new PlayerAnimatedInteractionPelvisTransition(
                PlayerCharacterDimensions.GetUprightPelvisPosition(
                    doorwayGround),
                EnterArrivalProgress,
                EnterDepartureProgress,
                ExitArrivalProgress,
                ExitDepartureProgress,
                EnterHoldProgress,
                EnterSettleProgress,
                ExitHoldProgress,
                ExitSettleProgress);

            Vector3 triggerCentre = dock;
            triggerCentre.y = groundY + TriggerHeight * 0.5f;
            return new LastRouteCarSeatPlan(
                true,
                dock,
                entryRotation,
                PlayerCharacterDimensions.GetUprightPelvisPosition(dock),
                action,
                transition,
                dock,
                triggerCentre,
                entryRotation,
                new Vector3(1.5f, TriggerHeight, 1.4f));
        }
    }
}
