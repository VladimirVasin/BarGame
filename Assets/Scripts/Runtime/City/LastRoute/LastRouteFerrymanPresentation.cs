using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    public enum LastRouteFerrymanPhase
    {
        /// <summary>On the bonnet, throwing the coin. Where he has been
        /// for twenty years and where he stays unless asked.</summary>
        Waiting = 0,

        /// <summary>Off the metal and down onto the lot, with the car
        /// coming up on its springs behind him.</summary>
        Dismounting = 1,

        /// <summary>Round the nose to his own door, at the pace of a man
        /// who has already waited twenty years.</summary>
        WalkingToDoor = 2,

        /// <summary>The handle, the door, the seat, and the door again.
        /// Once, and not reversible.</summary>
        Boarding = 3,

        /// <summary>Behind the wheel, waiting again - now for a
        /// passenger.</summary>
        Driving = 4
    }

    /// <summary>
    /// Drives the Ferryman through his five postures on one manual
    /// PlayableGraph - the watchman/fisherman idiom, with a mixer instead
    /// of a single clip because he has somewhere to go.
    ///
    /// The clip library contains no root motion by contract, so the body
    /// motion of every transition is authored and the METRES it covers are
    /// not: this component carries the root off the bonnet, round the car
    /// and into the driver's seat while the clips play. None of those
    /// places is a constant. The perch comes from the car's own soles
    /// anchor, the walk from <see cref="LastRouteFerrymanBoardingPlan"/>
    /// (which reads the car's anchors and rays the ground under each
    /// point), and the seat is solved by measuring where this rig actually
    /// puts its pelvis in the drive pose and offsetting the root until that
    /// lands on the car's seat anchor. All of them therefore survive either
    /// generator moving.
    ///
    /// The door is his too, and for the same reason the coin is: it has to
    /// belong to the hand that is pulling it rather than to a second
    /// free-running timer. Openness is a pure function of the board clip's
    /// own progress, exactly as the coin's arc is a pure function of the
    /// wait loop's.
    ///
    /// It also publishes the wait loop's own phase, because the coin has to
    /// belong to the hand that is throwing it, and the only way to
    /// guarantee that is to read it off the clip that is moving the hand.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LastRouteFerrymanPresentation : MonoBehaviour
    {
        /// <summary>A hitch longer than this advances the graph by a
        /// bounded step instead of teleporting him mid-throw.</summary>
        public const float MaximumStepSeconds = 0.1f;

        /// <summary>
        /// How many breaths the wait loop contains. Mirrors the
        /// `FerrymanWait` key grid in
        /// `tools/build-city-pedestrian-3d-model.py`, which rests at every
        /// quarter of the loop.
        /// </summary>
        public const int BreathsPerLoop = 4;

        /// <summary>
        /// Where in the wait loop the coin leaves his palm, and where it
        /// arrives back in it.
        ///
        /// These two are a CONTRACT with the authored key grid: the flick
        /// key sits at 1/16 of the loop and the catch key at 5/16, and the
        /// hand rises and gives between them. Re-timing that grid without
        /// re-timing these constants detaches the coin from the hand in
        /// mid-air, which is the single most visible way this character can
        /// break.
        /// </summary>
        public const float TossReleasePhase = 0.0625f;
        public const float TossCatchPhase = 0.3125f;

        /// <summary>
        /// How long one posture blends into the next. Short: he is supposed
        /// to move the instant he is asked, and the two seams that need it
        /// at all - the stand into the walk cycle, and the walk cycle into
        /// the door - are a stride apart rather than a posture apart.
        ///
        /// The board does NOT blend out into the drive. That clip is
        /// authored to close on the drive loop's own base pose, so there is
        /// nothing at the far end to blend.
        /// </summary>
        public const float BoardBlendSeconds = 0.12f;

        /// <summary>
        /// The pace of the walk round the car, in metres per second. A
        /// shade quicker than the mourner's 1.05: she is grieving and he
        /// has just been told yes.
        /// </summary>
        public const float WalkSpeedMetersPerSecond = 1.3f;

        /// <summary>How much of the walk's last stretch is spent turning to
        /// square up with the door.</summary>
        public const float DockTurnFraction = 0.16f;

        private const int WaitInput = 0;
        private const int DismountInput = 1;
        private const int WalkInput = 2;
        private const int BoardInput = 3;
        private const int DriveInput = 4;
        private const int InputCount = 5;

        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private AnimationClipPlayable waitPlayable;
        private AnimationClipPlayable dismountPlayable;
        private AnimationClipPlayable walkPlayable;
        private AnimationClipPlayable boardPlayable;
        private AnimationClipPlayable drivePlayable;
        private float waitLengthSeconds = 1f;
        private float dismountLengthSeconds = 1f;
        private float boardLengthSeconds = 1f;
        private float playbackSpeed = 1f;
        private bool hasGraph;

        private Vector3 perchPosition;
        private Quaternion perchRotation;
        private Vector3 drivePosition;
        private Quaternion driveRotation;
        private LastRouteFerrymanBoardingPlan boardingPlan;
        private LastRouteFerrymanBoardingTimeline boarding;
        private LastRouteCarDoors doors;
        private LastRouteCarSuspension suspension;
        private Quaternion landingRotation;
        private Quaternion dockRotation;
        private float firstLegLength;
        private float secondLegLength;
        private float blendElapsedSeconds;
        private int previousInput = WaitInput;
        private int currentInput = WaitInput;

        public bool IsInitialized { get; private set; }
        public LastRouteFerrymanPhase Phase { get; private set; }
        public bool IsWaiting => Phase == LastRouteFerrymanPhase.Waiting;
        public bool IsDriving => Phase == LastRouteFerrymanPhase.Driving;

        /// <summary>True once his boots are off the car - the moment the
        /// answer has visibly been given and the menu has nothing left to
        /// hold the player for.</summary>
        public bool HasLeftTheBonnet =>
            Phase != LastRouteFerrymanPhase.Waiting &&
            (Phase != LastRouteFerrymanPhase.Dismounting ||
             (boarding != null &&
              boarding.PhaseProgress >=
                  LastRouteFerrymanBoardingTimeline.LandingPhase));

        public LastRouteFerrymanBoardingPlan BoardingPlan => boardingPlan;

        /// <summary>
        /// The wait loop's own position, in `[0, 1)`. Zero before the graph
        /// exists, so a reader never has to special-case it.
        /// </summary>
        public float NormalizedTime
        {
            get
            {
                if (!hasGraph || waitLengthSeconds <= 0f)
                {
                    return 0f;
                }

                return Mathf.Repeat(
                    (float)waitPlayable.GetTime() / waitLengthSeconds,
                    1f);
            }
        }

        /// <summary>Where he is in the current breath, in `[0, 1)`.
        /// </summary>
        public float BreathPhase =>
            Mathf.Repeat(NormalizedTime * BreathsPerLoop, 1f);

        /// <summary>Pure: is the coin off the palm at this loop position?
        /// </summary>
        public static bool IsCoinAirborneAt(float normalizedTime)
        {
            float phase = Mathf.Repeat(normalizedTime, 1f);
            return phase >= TossReleasePhase && phase <= TossCatchPhase;
        }

        /// <summary>
        /// Pure: how far through the throw, in `[0, 1]` - zero at the
        /// release, one at the catch. Zero outside the flight, so callers
        /// that forget to ask <see cref="IsCoinAirborneAt"/> get a coin in
        /// the hand rather than one halfway up.
        /// </summary>
        public static float TossFlightPhaseAt(float normalizedTime)
        {
            if (!IsCoinAirborneAt(normalizedTime))
            {
                return 0f;
            }

            float phase = Mathf.Repeat(normalizedTime, 1f);
            return Mathf.Clamp01(
                (phase - TossReleasePhase) /
                (TossCatchPhase - TossReleasePhase));
        }

        public void Initialize(
            CityPedestrianAssetRegistry registry,
            LastRouteFerrymanRigAnchors anchors,
            LastRouteFerrymanStance stance,
            LastRouteCarAssetRegistry car)
        {
            if (anchors == null)
            {
                throw new ArgumentNullException(nameof(anchors));
            }

            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (car == null)
            {
                throw new ArgumentNullException(nameof(car));
            }

            if (registry.Animator == null)
            {
                throw new InvalidOperationException(
                    "The Ferryman prefab has no Animator.");
            }

            if (registry.Pelvis == null)
            {
                throw new InvalidOperationException(
                    "The Ferryman prefab has no serialized pelvis anchor, " +
                    "so his seat cannot be solved.");
            }

            AnimationClip wait = registry.IdleClip;
            AnimationClip walk = registry.WalkClip;
            AnimationClip board = registry.ActionClip;
            AnimationClip drive = registry.SitClip;
            AnimationClip dismount = anchors.DismountClip;
            if (wait == null || walk == null || board == null ||
                drive == null || dismount == null)
            {
                throw new InvalidOperationException(
                    "The Ferryman prefab needs its wait loop, his drop off " +
                    "the bonnet, his walk, his board transition and his " +
                    "driving loop.");
            }

            waitLengthSeconds = Mathf.Max(0.0001f, wait.length);
            dismountLengthSeconds = Mathf.Max(0.0001f, dismount.length);
            boardLengthSeconds = Mathf.Max(0.0001f, board.length);
            playbackSpeed = Mathf.Max(0.05f, stance.PlaybackSpeed);
            registry.ApplyPaletteVariant(stance.PaletteVariant);

            perchRotation = Quaternion.LookRotation(stance.Facing, Vector3.up);
            perchPosition = stance.Position;

            graph = PlayableGraph.Create("Last Route Ferryman");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, InputCount);
            waitPlayable = CreateClipPlayable(wait);
            dismountPlayable = CreateClipPlayable(dismount);
            walkPlayable = CreateClipPlayable(walk);
            boardPlayable = CreateClipPlayable(board);
            drivePlayable = CreateClipPlayable(drive);
            graph.Connect(waitPlayable, 0, mixer, WaitInput);
            graph.Connect(dismountPlayable, 0, mixer, DismountInput);
            graph.Connect(walkPlayable, 0, mixer, WalkInput);
            graph.Connect(boardPlayable, 0, mixer, BoardInput);
            graph.Connect(drivePlayable, 0, mixer, DriveInput);
            AnimationPlayableOutput
                .Create(graph, "Last Route Ferryman Pose", registry.Animator)
                .SetSourcePlayable(mixer);
            graph.Play();
            hasGraph = true;

            SolveDriverSeat(registry, car);
            ResolveCarMechanisms(car);
            SolveWalk(car);

            previousInput = WaitInput;
            currentInput = WaitInput;
            blendElapsedSeconds = BoardBlendSeconds;
            ApplyWeights();
            waitPlayable.SetTime(
                Mathf.Repeat(stance.PhaseOffsetSeconds, wait.length));
            graph.Evaluate(0f);

            SolvePerch(registry, anchors, stance);

            Phase = LastRouteFerrymanPhase.Waiting;
            transform.SetPositionAndRotation(perchPosition, perchRotation);
            IsInitialized = true;
        }

        /// <summary>
        /// He said yes. Off the bonnet, round the car and into it - once; a
        /// second call is ignored rather than restarting the walk.
        /// </summary>
        public bool TryBeginBoarding()
        {
            if (!IsInitialized ||
                Phase != LastRouteFerrymanPhase.Waiting)
            {
                return false;
            }

            float walkSeconds = WalkSpeedMetersPerSecond > 0f
                ? (firstLegLength + secondLegLength) /
                  WalkSpeedMetersPerSecond
                : 0f;
            boarding = new LastRouteFerrymanBoardingTimeline(
                dismountLengthSeconds,
                walkSeconds,
                boardLengthSeconds);
            dismountPlayable.SetTime(0.0);
            walkPlayable.SetTime(0.0);
            boardPlayable.SetTime(0.0);
            EnterPhase(LastRouteFerrymanPhase.Dismounting);
            return true;
        }

        /// <summary>
        /// Where the drawn rig would put its pelvis in the drive pose, and
        /// therefore where the root has to be for that pelvis to land on
        /// the car's own seat anchor.
        ///
        /// This is measured rather than declared for the reason the whole
        /// project keeps re-learning: an imported model's nodes do not
        /// share the object's axes, so any constant written here would be
        /// re-deriving the FBX conversion and the prefab's 180 degree model
        /// flip by hand. Evaluating the pose and reading the result cannot
        /// be wrong about either.
        /// </summary>
        private void SolveDriverSeat(
            CityPedestrianAssetRegistry registry,
            LastRouteCarAssetRegistry car)
        {
            if (car.DriverSeatAnchor == null ||
                car.SteeringWheelPivot == null)
            {
                throw new InvalidOperationException(
                    "The Last Route car must carry a bound driver seat " +
                    "and steering wheel before the Ferryman can sit in it.");
            }

            // Facing taken from the drawn cabin - seat towards wheel -
            // rather than from any transform's forward. Two sessions have
            // now been caught assuming an imported node's basis; the
            // vector between two anchors has no basis to get wrong.
            Vector3 toWheel = car.SteeringWheelPivot.position -
                              car.DriverSeatAnchor.position;
            toWheel.y = 0f;
            driveRotation = toWheel.sqrMagnitude > 0.000001f
                ? Quaternion.LookRotation(toWheel.normalized, Vector3.up)
                : perchRotation;

            Vector3 previousPosition = transform.position;
            Quaternion previousRotation = transform.rotation;
            SetWeightsForInput(DriveInput);
            drivePlayable.SetTime(0.0);
            transform.SetPositionAndRotation(Vector3.zero, driveRotation);
            graph.Evaluate(0f);

            // With the root at the origin and already turned the way he
            // will drive, the pelvis's world position IS the rotated offset
            // from root to pelvis. The root that seats him is therefore the
            // seat anchor minus exactly that.
            Vector3 rotatedPelvisOffset = registry.Pelvis.position;
            drivePosition =
                car.DriverSeatAnchor.position - rotatedPelvisOffset;

            transform.SetPositionAndRotation(
                previousPosition,
                previousRotation);
        }

        /// <summary>
        /// The doors and the springs belong to the car, not to him - but
        /// the moments they move belong to his clips, so he holds a
        /// reference to each. Both are optional: a car raised straight from
        /// its prefab, as the placement fixtures do, has neither, and he
        /// still gets in.
        /// </summary>
        private void ResolveCarMechanisms(LastRouteCarAssetRegistry car)
        {
            doors = car.GetComponentInParent<LastRouteCarDoors>();
            suspension = car.GetComponentInParent<LastRouteCarSuspension>();
        }

        /// <summary>
        /// The three places he stands between the two seats, and the two
        /// legs of walk between them.
        /// </summary>
        private void SolveWalk(LastRouteCarAssetRegistry car)
        {
            boardingPlan = LastRouteFerrymanBoardingPlan.Create(
                car,
                car.transform.position.y);
            if (!boardingPlan.IsPresent)
            {
                // No plan means no walk: he drops where he stands and gets
                // in from there. Degraded rather than broken, because a
                // Ferryman who cannot board at all is a dead end in the
                // one conversation the island has.
                landingRotation = perchRotation;
                dockRotation = driveRotation;
                firstLegLength = 0f;
                secondLegLength = 0f;
                return;
            }

            landingRotation = Quaternion.LookRotation(
                boardingPlan.LandingFacing,
                Vector3.up);
            dockRotation = Quaternion.LookRotation(
                boardingPlan.DoorDockFacing,
                Vector3.up);
            firstLegLength = Vector3.Distance(
                boardingPlan.LandingPosition,
                boardingPlan.ApproachCorner);
            secondLegLength = Vector3.Distance(
                boardingPlan.ApproachCorner,
                boardingPlan.DoorDockPosition);
        }

        /// <summary>
        /// Sets him down on the car, now that the perch pose is evaluated.
        ///
        /// The model origin is the sole plane of the BIND pose - standing
        /// straight - and the perch is nothing like it: both knees are up
        /// on a bonnet, so the feet leave that plane entirely. Placing the
        /// root on the soles anchor therefore left him hanging in the air
        /// above the car with his coat draped on nothing.
        ///
        /// So the pelvis is placed instead, exactly as the driver's seat
        /// is: Blender measured how far the pelvis rides above the lowest
        /// drawn point of this pose and the prefab carries the number, so
        /// putting the pelvis that far above the bumper puts his boots on
        /// the bumper and - because the pose was converged against the
        /// car's own 0.505 m bonnet drop - his backside on the bonnet.
        /// Only the height is solved; where he sits along and across the
        /// car is the anchor's business and is left alone.
        /// </summary>
        private void SolvePerch(
            CityPedestrianAssetRegistry registry,
            LastRouteFerrymanRigAnchors anchors,
            LastRouteFerrymanStance stance)
        {
            if (anchors == null || anchors.PerchPelvisDrop <= 0f)
            {
                throw new InvalidOperationException(
                    "The Ferryman prefab must carry a measured perch " +
                    "pelvis drop; without it he cannot be set down.");
            }

            transform.SetPositionAndRotation(stance.Position, perchRotation);
            graph.Evaluate(0f);

            float targetPelvisY =
                stance.Position.y + anchors.PerchPelvisDrop;
            perchPosition = stance.Position +
                            (Vector3.up *
                             (targetPelvisY - registry.Pelvis.position.y));
        }

        private AnimationClipPlayable CreateClipPlayable(AnimationClip clip)
        {
            AnimationClipPlayable playable =
                AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            return playable;
        }

        private void EnterPhase(LastRouteFerrymanPhase phase)
        {
            Phase = phase;
            previousInput = currentInput;
            currentInput = ResolveInput(phase);
            blendElapsedSeconds = 0f;
        }

        private static int ResolveInput(LastRouteFerrymanPhase phase)
        {
            switch (phase)
            {
                case LastRouteFerrymanPhase.Dismounting:
                    return DismountInput;
                case LastRouteFerrymanPhase.WalkingToDoor:
                    return WalkInput;
                case LastRouteFerrymanPhase.Boarding:
                    return BoardInput;
                case LastRouteFerrymanPhase.Driving:
                    return DriveInput;
                default:
                    return WaitInput;
            }
        }

        private void SetWeightsForInput(int input)
        {
            for (int index = 0; index < InputCount; index++)
            {
                mixer.SetInputWeight(index, index == input ? 1f : 0f);
            }
        }

        private void ApplyWeights()
        {
            float blend = BoardBlendSeconds > 0f
                ? Mathf.Clamp01(blendElapsedSeconds / BoardBlendSeconds)
                : 1f;
            for (int index = 0; index < InputCount; index++)
            {
                float weight = 0f;
                if (index == currentInput)
                {
                    weight += blend;
                }

                if (index == previousInput)
                {
                    weight += 1f - blend;
                }

                mixer.SetInputWeight(index, weight);
            }
        }

        private void LateUpdate()
        {
            if (!hasGraph)
            {
                return;
            }

            float step =
                Mathf.Min(Time.deltaTime, MaximumStepSeconds) * playbackSpeed;
            if (boarding != null && !boarding.IsDone)
            {
                AdvanceBoarding(step);
            }

            blendElapsedSeconds += step;
            ApplyWeights();
            graph.Evaluate(step);
        }

        private void AdvanceBoarding(float step)
        {
            LastRouteFerrymanPhase before = boarding.Phase;
            boarding.Advance(step);
            if (boarding.Phase != before)
            {
                EnterPhase(boarding.Phase);
            }

            switch (boarding.Phase)
            {
                case LastRouteFerrymanPhase.Dismounting:
                    ApplyDrop();
                    break;
                case LastRouteFerrymanPhase.WalkingToDoor:
                    ApplyWalk();
                    break;
                case LastRouteFerrymanPhase.Boarding:
                    ApplyBoard();
                    break;
                default:
                    transform.SetPositionAndRotation(
                        drivePosition,
                        driveRotation);
                    doors?.SetDriverOpenness(0f);
                    break;
            }

            if (boarding.ConsumeLandingCue())
            {
                suspension?.NudgeForDismount();
            }

            if (boarding.ConsumeSeatCue())
            {
                // He gets in on the driver's side, which is the flank the
                // passenger anchor is NOT on. Asked of the drawn anchors
                // rather than assumed, because which side of a car the
                // wheel is on is exactly the kind of thing that gets
                // mirrored in a generator one day.
                suspension?.NudgeForSeating(IsDriverSideCarRight());
            }
        }

        private void ApplyDrop()
        {
            Vector3 target = boardingPlan.IsPresent
                ? boardingPlan.LandingPosition
                : drivePosition;
            var planar = new Vector3(
                Mathf.Lerp(perchPosition.x, target.x, boarding.DropTravel),
                Mathf.Lerp(perchPosition.y, target.y, boarding.DropFall),
                Mathf.Lerp(perchPosition.z, target.z, boarding.DropTravel));
            transform.SetPositionAndRotation(
                planar,
                Quaternion.Slerp(
                    perchRotation,
                    landingRotation,
                    boarding.DropTravel));
        }

        private void ApplyWalk()
        {
            if (!boardingPlan.IsPresent)
            {
                return;
            }

            float total = firstLegLength + secondLegLength;
            float travelled = boarding.PhaseProgress * total;
            Vector3 position;
            Vector3 heading;
            if (travelled <= firstLegLength && firstLegLength > 0.0001f)
            {
                position = Vector3.Lerp(
                    boardingPlan.LandingPosition,
                    boardingPlan.ApproachCorner,
                    travelled / firstLegLength);
                heading = boardingPlan.ApproachCorner -
                          boardingPlan.LandingPosition;
            }
            else if (secondLegLength > 0.0001f)
            {
                position = Vector3.Lerp(
                    boardingPlan.ApproachCorner,
                    boardingPlan.DoorDockPosition,
                    (travelled - firstLegLength) / secondLegLength);
                heading = boardingPlan.DoorDockPosition -
                          boardingPlan.ApproachCorner;
            }
            else
            {
                position = boardingPlan.DoorDockPosition;
                heading = boardingPlan.DoorDockFacing;
            }

            heading.y = 0f;
            Quaternion facing = heading.sqrMagnitude > 0.000001f
                ? Quaternion.LookRotation(heading.normalized, Vector3.up)
                : dockRotation;

            // The last stretch is spent squaring up with the door, so he
            // arrives already looking at it instead of snapping round on
            // the first frame of the board clip.
            float turn = DockTurnFraction > 0f
                ? Mathf.InverseLerp(
                    1f - DockTurnFraction,
                    1f,
                    boarding.PhaseProgress)
                : 0f;
            transform.SetPositionAndRotation(
                position,
                Quaternion.Slerp(
                    facing,
                    dockRotation,
                    Mathf.SmoothStep(0f, 1f, turn)));
        }

        private void ApplyBoard()
        {
            Vector3 dock = boardingPlan.IsPresent
                ? boardingPlan.DoorDockPosition
                : perchPosition;
            float travel = boarding.SeatTravel;
            transform.SetPositionAndRotation(
                Vector3.Lerp(dock, drivePosition, travel),
                Quaternion.Slerp(dockRotation, driveRotation, travel));
            doors?.SetDriverOpenness(boarding.DriverDoorOpenness);
        }

        private bool IsDriverSideCarRight()
        {
            if (suspension == null)
            {
                return false;
            }

            Transform car = suspension.transform;
            Vector3 toSeat = drivePosition - car.position;
            return Vector3.Dot(toSeat, car.right) >= 0f;
        }

        private void OnDestroy()
        {
            if (hasGraph && graph.IsValid())
            {
                graph.Destroy();
            }

            hasGraph = false;
        }
    }
}
