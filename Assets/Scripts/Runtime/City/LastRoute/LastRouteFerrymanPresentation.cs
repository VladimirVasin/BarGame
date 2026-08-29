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
        Driving = 4,

        /// <summary>
        /// The journey is over and the hero is out. The door again, and the
        /// climb out of the seat - `FerrymanBoard` run backwards.
        /// </summary>
        Alighting = 5,

        /// <summary>Back round the nose the way he came, at the same pace.
        /// </summary>
        WalkingToBonnet = 6,

        /// <summary>Up onto the bonnet - `FerrymanDismount` run backwards -
        /// after which he is <see cref="Waiting"/> again, for good.</summary>
        Mounting = 7
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
        private LastRouteFerrymanAlightingTimeline alighting;
        private Vector3 drivePelvisOffset;
        private float perchPelvisLift;
        private float walkSeconds;
        private LastRouteCarAssetRegistry carRegistry;
        private LastRouteCarDoors doors;
        private LastRouteCarSuspension suspension;
        private Quaternion landingRotation;
        private Quaternion dockRotation;
        private float firstLegLength;
        private float secondLegLength;
        private float blendElapsedSeconds;
        private int previousInput = WaitInput;
        private int currentInput = WaitInput;
        private LastRouteFerrymanRigAnchors rigAnchors;
        private SeatedArmHandAttachment leftHandAttachment;
        private SeatedArmHandAttachment rightHandAttachment;
        private bool hasSteeringArms;
        private float steeringHandsWeight;

        public bool IsInitialized { get; private set; }
        public LastRouteFerrymanPhase Phase { get; private set; }
        public bool IsWaiting => Phase == LastRouteFerrymanPhase.Waiting;
        public bool IsDriving => Phase == LastRouteFerrymanPhase.Driving;

        /// <summary>
        /// True from the moment he starts climbing back out at the far end.
        /// He waits on his bonnet again afterwards, but he does not take
        /// anybody anywhere a second time.
        /// </summary>
        public bool HasCompletedJourney { get; private set; }

        /// <summary>True while he is getting out, walking back or climbing on
        /// - the whole way home.</summary>
        public bool IsAlighting =>
            alighting != null && !alighting.IsDone;

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
            waitPlayable = CreateClipPlayable(wait, false);
            dismountPlayable = CreateClipPlayable(dismount, true);
            walkPlayable = CreateClipPlayable(walk, true);
            boardPlayable = CreateClipPlayable(board, true);
            drivePlayable = CreateClipPlayable(drive, true);
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
            CaptureSteeringArms(anchors, car);

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
                Phase != LastRouteFerrymanPhase.Waiting ||
                HasCompletedJourney)
            {
                // He is back on a bonnet, but it is the bonnet at the cafe and
                // the route he ran is over. Without this the wait loop coming
                // back would put the offer back up with it, and answering it
                // would drive him off the mountain terrace to a tunnel that is
                // six hundred metres downhill.
                return false;
            }

            boarding = new LastRouteFerrymanBoardingTimeline(
                dismountLengthSeconds,
                walkSeconds,
                boardLengthSeconds);
            // Each clip is rewound by EnterPhase when its OWN beat starts.
            // Rewinding all three here is what broke the boarding: the walk
            // and the board were seeked a dismount and a walk too early and
            // then left running, so both arrived at their phase already
            // spent.
            EnterPhase(LastRouteFerrymanPhase.Dismounting);
            return true;
        }

        /// <summary>
        /// Puts him behind the wheel with no beat at all, already driving.
        ///
        /// One caller, one moment: the mountain road finishing its load. He
        /// got into this car in a scene that no longer exists, and the whole
        /// island - car, man and coin - has just been built again around a
        /// hero who never left the passenger seat. Playing the boarding beat
        /// here would have him climb into a moving car out of the air.
        /// </summary>
        public bool BeginSeatedAtTheWheel()
        {
            if (!IsInitialized || Phase != LastRouteFerrymanPhase.Waiting)
            {
                return false;
            }

            boarding = new LastRouteFerrymanBoardingTimeline(
                dismountLengthSeconds,
                walkSeconds,
                boardLengthSeconds);
            boarding.Advance(
                dismountLengthSeconds + walkSeconds + boardLengthSeconds + 1f);
            EnterPhase(LastRouteFerrymanPhase.Driving);
            // The springs are not kicked and the door is never opened: from
            // the player's side he has been sitting here for a minute.
            boarding.ConsumeLandingCue();
            boarding.ConsumeSeatCue();
            RefreshDriverSeat();
            transform.SetPositionAndRotation(drivePosition, driveRotation);
            doors?.SetDriverOpenness(0f);
            return true;
        }

        /// <summary>
        /// The journey is over and the passenger is out. He gets out himself,
        /// walks back round the nose and sits up onto the bonnet, and there he
        /// stays.
        ///
        /// Once only, and only from behind the wheel - a second call while he
        /// is already climbing out is ignored rather than restarting him.
        /// </summary>
        public bool TryBeginAlighting()
        {
            if (!IsInitialized || Phase != LastRouteFerrymanPhase.Driving)
            {
                return false;
            }

            RefreshDriverSeat();
            RefreshPerchFromCar();
            HasCompletedJourney = true;
            alighting = new LastRouteFerrymanAlightingTimeline(
                boardLengthSeconds,
                walkSeconds,
                dismountLengthSeconds);
            EnterPhase(LastRouteFerrymanPhase.Alighting);
            return true;
        }

        /// <summary>
        /// Re-reads the walk and the bonnet off the car where it now stands.
        ///
        /// Everything the boarding beat used was solved on the island: the
        /// landing point, the corner he rounds, the door dock and the perch
        /// are all world-space and all six hundred metres and twenty-six
        /// metres of altitude out of date by the time he gets out at the cafe.
        /// The perch in particular is re-derived exactly as
        /// <see cref="SolvePerch"/> did it, from the car's own soles anchor
        /// plus the measured pelvis drop, so he lands on the metal rather than
        /// above or through it.
        /// </summary>
        private void RefreshPerchFromCar()
        {
            if (carRegistry == null)
            {
                return;
            }

            SolveWalk(carRegistry);
            if (carRegistry.PerchSolesAnchor == null ||
                carRegistry.PerchSeatAnchor == null)
            {
                return;
            }

            Vector3 facing = carRegistry.PerchSolesAnchor.position -
                             carRegistry.PerchSeatAnchor.position;
            facing.y = 0f;
            if (facing.sqrMagnitude > 0.000001f)
            {
                perchRotation = Quaternion.LookRotation(
                    facing.normalized,
                    Vector3.up);
            }

            perchPosition = carRegistry.PerchSolesAnchor.position +
                            (Vector3.up * perchPelvisLift);
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

            // Kept unrotated as well, because this car no longer stands
            // still. Once it is driving, the seat anchor and the wheel move
            // every frame and his root has to be re-solved from them; with
            // the offset in the pose's own frame that is the same two lines
            // as above rather than a second solve.
            drivePelvisOffset =
                Quaternion.Inverse(driveRotation) * rotatedPelvisOffset;

            transform.SetPositionAndRotation(
                previousPosition,
                previousRotation);
        }

        /// <summary>
        /// The driving pose against wherever the car is NOW.
        ///
        /// While it was parked this was solved once and never asked again.
        /// A car that drives six hundred metres makes that a man left standing
        /// on the island, so the seat is re-derived every frame he is in it -
        /// from the same two drawn anchors, so it cannot drift from the solve
        /// that placed him there.
        /// </summary>
        private void RefreshDriverSeat()
        {
            if (carRegistry == null ||
                carRegistry.DriverSeatAnchor == null ||
                carRegistry.SteeringWheelPivot == null)
            {
                return;
            }

            Vector3 toWheel = carRegistry.SteeringWheelPivot.position -
                              carRegistry.DriverSeatAnchor.position;
            toWheel.y = 0f;
            if (toWheel.sqrMagnitude > 0.000001f)
            {
                driveRotation = Quaternion.LookRotation(
                    toWheel.normalized,
                    Vector3.up);
            }

            drivePosition = carRegistry.DriverSeatAnchor.position -
                            (driveRotation * drivePelvisOffset);
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
            carRegistry = car;
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
            walkSeconds = WalkSpeedMetersPerSecond > 0f
                ? (firstLegLength + secondLegLength) /
                  WalkSpeedMetersPerSecond
                : 0f;
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
            // Kept as the LIFT rather than only as the answer, so getting
            // back onto the bonnet at the far end of the mountain road can
            // re-derive the same seat from the car's soles anchor without a
            // second evaluation of the pose.
            perchPelvisLift = targetPelvisY - registry.Pelvis.position.y;
            perchPosition = stance.Position +
                            (Vector3.up * perchPelvisLift);
        }

        /// <summary>
        /// Every clip but the wait loop is PARKED at speed zero until its
        /// own beat begins.
        ///
        /// The graph is manual, and one <c>Evaluate</c> advances every
        /// playable hanging off it whether the mixer is listening to it or
        /// not. So a clip left running from Initialize is not waiting its
        /// turn - it is playing to itself in the dark, and by the time the
        /// mixer crosses into it, it is wherever that dead time left it.
        ///
        /// The board clip is 2.5 s long and its beat starts about 5 s in,
        /// after the dismount and the walk round the car. It therefore used
        /// to arrive CLAMPED ON ITS LAST KEY - the seated drive pose - so he
        /// finished getting in before the door had finished opening, and
        /// then slid into the car already sitting down. Every authored beat
        /// between (reach, pull, duck under the roofline, fold into the
        /// seat, pull the door shut) was skipped, and the three seat blends
        /// the clip was authored around - 0.22 / 0.70 / 0.97, matched by
        /// hand to this timeline's own 0.198 / 0.675 / 0.974 - never ran.
        ///
        /// <see cref="EnterPhase"/> rewinds and unparks each clip as its
        /// phase begins, which is the idiom the park chess player already
        /// uses for its one-shot.
        /// </summary>
        private AnimationClipPlayable CreateClipPlayable(
            AnimationClip clip,
            bool parked)
        {
            AnimationClipPlayable playable =
                AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            if (parked)
            {
                playable.SetSpeed(0.0);
            }

            return playable;
        }

        private void EnterPhase(LastRouteFerrymanPhase phase)
        {
            Phase = phase;
            previousInput = currentInput;
            currentInput = ResolveInput(phase);
            blendElapsedSeconds = 0f;
            StartClipForInput(currentInput);
            if (alighting != null)
            {
                // The way back out runs its one-shots BACKWARDS, and it does
                // it by writing their time rather than by giving a playable a
                // negative speed. Parking the incoming clip at zero speed here
                // is what leaves the applier in sole charge of where it
                // stands, and it is the same idiom that keeps every one-shot
                // on this graph from playing to itself in the dark.
                ParkClipForInput(currentInput);
            }
        }

        private void ParkClipForInput(int input)
        {
            switch (input)
            {
                case DismountInput:
                    dismountPlayable.SetSpeed(0.0);
                    break;
                case BoardInput:
                    boardPlayable.SetSpeed(0.0);
                    break;
            }
        }

        private void SetReversedClipTime(
            int input,
            float reversedPhase,
            float length)
        {
            double time = Mathf.Clamp01(reversedPhase) * length;
            switch (input)
            {
                case DismountInput:
                    dismountPlayable.SetTime(time);
                    break;
                case BoardInput:
                    boardPlayable.SetTime(time);
                    break;
            }
        }

        /// <summary>
        /// Rewinds the incoming clip to its first frame and lets it run.
        ///
        /// The OUTGOING clip is deliberately left where it stands rather
        /// than parked or rewound: the crossfade still needs a pose to
        /// blend out of for <see cref="BoardBlendSeconds"/>. The dismount
        /// ends exactly on its own last frame, and the walk loop should
        /// hold the stride it was in rather than snap to a base pose
        /// halfway through a blend. Nothing needs re-parking afterwards
        /// because the phases only ever run forwards, once each.
        /// </summary>
        private void StartClipForInput(int input)
        {
            switch (input)
            {
                case DismountInput:
                    dismountPlayable.SetTime(0.0);
                    dismountPlayable.SetSpeed(1.0);
                    break;
                case WalkInput:
                    walkPlayable.SetTime(0.0);
                    walkPlayable.SetSpeed(1.0);
                    break;
                case BoardInput:
                    boardPlayable.SetTime(0.0);
                    boardPlayable.SetSpeed(1.0);
                    break;
                case DriveInput:
                    drivePlayable.SetTime(0.0);
                    drivePlayable.SetSpeed(1.0);
                    break;
            }
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
                case LastRouteFerrymanPhase.Alighting:
                    return BoardInput;
                case LastRouteFerrymanPhase.Driving:
                    return DriveInput;
                case LastRouteFerrymanPhase.WalkingToBonnet:
                    return WalkInput;
                case LastRouteFerrymanPhase.Mounting:
                    return DismountInput;
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
            if (alighting != null)
            {
                AdvanceAlighting(step);
            }
            else if (boarding != null && !boarding.IsDone)
            {
                AdvanceBoarding(step);
            }
            else if (Phase == LastRouteFerrymanPhase.Driving)
            {
                // He is at the wheel of a car that is now capable of going
                // somewhere. The boarding timeline is finished and no longer
                // writes his root, so without this he would sit at the world
                // position the island left him at while the car drove up a
                // mountain without him.
                RefreshDriverSeat();
                transform.SetPositionAndRotation(drivePosition, driveRotation);
            }

            blendElapsedSeconds += step;
            ApplyWeights();
            graph.Evaluate(step);

            // After the graph, never before: the graph rewrites every bone
            // on each Evaluate, so an arm posed earlier in this frame would
            // be silently unposed by the very next line.
            AdvanceSteeringHands(step);
        }

        public const float SteeringHandsEaseSeconds = 0.35f;

        /// <summary>The bus driver's own contact tolerance, unchanged.
        /// </summary>
        public const float MaximumGripError = 0.02f;

        /// <summary>Live distance from each palm's grip socket to the rim
        /// grip it chases - the bus's contact diagnostics, mirrored, and
        /// what a test asserts instead of a screenshot a bind-posed skinned
        /// mesh would lie in.</summary>
        public float LeftGripDistance { get; private set; }

        public float RightGripDistance { get; private set; }

        /// <summary>How much of the wheel his hands currently take, `0`
        /// clip-authored to `1` riding the grips.</summary>
        public float SteeringHandsWeight => steeringHandsWeight;

        private void CaptureSteeringArms(
            LastRouteFerrymanRigAnchors anchors,
            LastRouteCarAssetRegistry car)
        {
            rigAnchors = anchors;
            hasSteeringArms =
                anchors.LeftUpperArm != null &&
                anchors.LeftForearm != null &&
                anchors.LeftHand != null &&
                anchors.LeftGripSocket != null &&
                anchors.RightUpperArm != null &&
                anchors.RightForearm != null &&
                anchors.RightHand != null &&
                anchors.RightGripSocket != null &&
                car.LeftSteeringGrip != null &&
                car.RightSteeringGrip != null;
            if (!hasSteeringArms)
            {
                // A prefab from before the arm bindings still perches,
                // walks, boards and drives - his hands just stay where the
                // clip drew them. Degraded rather than broken, like a plan
                // that could not offer a walk.
                return;
            }

            // Captured once, in the bind pose. The socket is a child bone
            // of the hand, so its offset in hand space never changes; the
            // capture is world-measured metres and a pure rotation, which
            // is what makes it indifferent to the 100x scale the imported
            // bone hierarchy carries.
            leftHandAttachment = new SeatedArmHandAttachment(
                anchors.LeftHand,
                anchors.LeftGripSocket);
            rightHandAttachment = new SeatedArmHandAttachment(
                anchors.RightHand,
                anchors.RightGripSocket);
        }

        /// <summary>
        /// His hands close on the grips - which are children of the wheel
        /// pivot the car's driver now rolls, so through this his hands turn
        /// the wheel the front wheels are answering. The bus driver's
        /// arrangement, on the bus driver's solver.
        ///
        /// The weight eases in as the drive settles and back out as it
        /// ends. The drive clip already draws both hands ON the unturned
        /// rim and the rim is straight at both seams - `Halt` sees to the
        /// arrival, and a departure begins at rest - so the blend crosses
        /// centimetres, not the cabin.
        /// </summary>
        private void AdvanceSteeringHands(float step)
        {
            bool wantsWheel =
                hasSteeringArms &&
                Phase == LastRouteFerrymanPhase.Driving &&
                alighting == null &&
                (boarding == null || boarding.IsDone);
            steeringHandsWeight = Mathf.MoveTowards(
                steeringHandsWeight,
                wantsWheel ? 1f : 0f,
                step / SteeringHandsEaseSeconds);
            if (steeringHandsWeight <= 0f || !hasSteeringArms)
            {
                LeftGripDistance = 0f;
                RightGripDistance = 0f;
                return;
            }

            // His left side is -transform.right: the root faces the wheel,
            // and each elbow is hinted out its own side and a little down,
            // where an arm holding a rim actually hangs.
            ApplySteeringArm(
                rigAnchors.LeftUpperArm,
                rigAnchors.LeftForearm,
                rigAnchors.LeftHand,
                rigAnchors.LeftGripSocket,
                leftHandAttachment,
                carRegistry.LeftSteeringGrip,
                -transform.right);
            ApplySteeringArm(
                rigAnchors.RightUpperArm,
                rigAnchors.RightForearm,
                rigAnchors.RightHand,
                rigAnchors.RightGripSocket,
                rightHandAttachment,
                carRegistry.RightSteeringGrip,
                transform.right);
            LeftGripDistance = Vector3.Distance(
                rigAnchors.LeftGripSocket.position,
                carRegistry.LeftSteeringGrip.position);
            RightGripDistance = Vector3.Distance(
                rigAnchors.RightGripSocket.position,
                carRegistry.RightSteeringGrip.position);
        }

        private void ApplySteeringArm(
            Transform upperArm,
            Transform forearm,
            Transform hand,
            Transform socket,
            in SeatedArmHandAttachment attachment,
            Transform grip,
            Vector3 elbowSide)
        {
            // At partial weight the hand is asked for a point BETWEEN where
            // the clip drew its socket and the grip, so the ease is a short
            // travel rather than a crossfade of two solved poses.
            Vector3 targetPosition = Vector3.Lerp(
                socket.position,
                grip.position,
                steeringHandsWeight);
            Quaternion targetRotation = Quaternion.Slerp(
                socket.rotation,
                grip.rotation,
                steeringHandsWeight);
            Quaternion handRotation = targetRotation *
                Quaternion.Inverse(attachment.SocketRotationInHand);
            Vector3 handPosition = targetPosition -
                handRotation * attachment.SocketPositionInHand;
            Vector3 elbowHint =
                upperArm.position +
                elbowSide * 0.32f +
                transform.forward * 0.08f -
                transform.up * 0.08f;
            SeatedArmIk.SolveTwoBone(
                upperArm,
                forearm,
                hand,
                handPosition,
                handRotation,
                elbowHint);
        }

        private void AdvanceAlighting(float step)
        {
            LastRouteFerrymanPhase before = alighting.Phase;
            if (!alighting.IsDone)
            {
                alighting.Advance(step);
            }

            if (alighting.Phase != before)
            {
                EnterPhase(alighting.Phase);
            }

            switch (alighting.Phase)
            {
                case LastRouteFerrymanPhase.Alighting:
                    ApplyAlight();
                    break;
                case LastRouteFerrymanPhase.WalkingToBonnet:
                    ApplyWalkBack();
                    break;
                case LastRouteFerrymanPhase.Mounting:
                    ApplyMount();
                    break;
                default:
                    transform.SetPositionAndRotation(
                        perchPosition,
                        perchRotation);
                    doors?.SetDriverOpenness(0f);
                    break;
            }

            if (alighting.ConsumeUnseatCue())
            {
                // His weight leaving the seat is the seating kick inverted:
                // that side of the car comes back up.
                suspension?.NudgeForUnseating(IsDriverSideCarRight());
            }

            if (alighting.ConsumeMountCue())
            {
                suspension?.NudgeForMount();
            }
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

        /// <summary>
        /// The climb out: the board clip played backwards, the leaf on its own
        /// reversed curve, and the root carried from the seat back to the door
        /// dock along the exact line it came in on.
        /// </summary>
        private void ApplyAlight()
        {
            RefreshDriverSeat();
            Vector3 dock = boardingPlan.IsPresent
                ? boardingPlan.DoorDockPosition
                : perchPosition;
            float travel = alighting.SeatTravel;
            transform.SetPositionAndRotation(
                Vector3.Lerp(dock, drivePosition, travel),
                Quaternion.Slerp(dockRotation, driveRotation, travel));
            doors?.SetDriverOpenness(alighting.DriverDoorOpenness);
            SetReversedClipTime(
                BoardInput,
                alighting.ReversedClipPhase,
                boardLengthSeconds);
        }

        /// <summary>
        /// The same two legs as the walk in, walked the other way. The clip
        /// itself plays FORWARDS - a walk cycle run backwards is a man moon-
        /// walking round a car - and only the path is reversed.
        /// </summary>
        private void ApplyWalkBack()
        {
            if (!boardingPlan.IsPresent)
            {
                return;
            }

            float total = firstLegLength + secondLegLength;
            float travelled = alighting.PhaseProgress * total;
            Vector3 position;
            Vector3 heading;
            if (travelled <= secondLegLength && secondLegLength > 0.0001f)
            {
                position = Vector3.Lerp(
                    boardingPlan.DoorDockPosition,
                    boardingPlan.ApproachCorner,
                    travelled / secondLegLength);
                heading = boardingPlan.ApproachCorner -
                          boardingPlan.DoorDockPosition;
            }
            else if (firstLegLength > 0.0001f)
            {
                position = Vector3.Lerp(
                    boardingPlan.ApproachCorner,
                    boardingPlan.LandingPosition,
                    (travelled - secondLegLength) / firstLegLength);
                heading = boardingPlan.LandingPosition -
                          boardingPlan.ApproachCorner;
            }
            else
            {
                position = boardingPlan.LandingPosition;
                heading = boardingPlan.LandingFacing;
            }

            heading.y = 0f;
            Quaternion facing = heading.sqrMagnitude > 0.000001f
                ? Quaternion.LookRotation(heading.normalized, Vector3.up)
                : landingRotation;

            // He squares up with the bumper over the last stretch, the mirror
            // of squaring up with the door on the way in.
            float turn = DockTurnFraction > 0f
                ? Mathf.InverseLerp(
                    1f - DockTurnFraction,
                    1f,
                    alighting.PhaseProgress)
                : 0f;
            transform.SetPositionAndRotation(
                position,
                Quaternion.Slerp(
                    facing,
                    landingRotation,
                    Mathf.SmoothStep(0f, 1f, turn)));
        }

        /// <summary>
        /// Up onto the bonnet: the drop played backwards, with the rise held
        /// separate from the carry for the same reason the fall is - he pushes
        /// himself up rather than floating.
        /// </summary>
        private void ApplyMount()
        {
            Vector3 from = boardingPlan.IsPresent
                ? boardingPlan.LandingPosition
                : drivePosition;
            var planar = new Vector3(
                Mathf.Lerp(from.x, perchPosition.x, alighting.MountTravel),
                Mathf.Lerp(from.y, perchPosition.y, alighting.MountRise),
                Mathf.Lerp(from.z, perchPosition.z, alighting.MountTravel));
            transform.SetPositionAndRotation(
                planar,
                Quaternion.Slerp(
                    landingRotation,
                    perchRotation,
                    alighting.MountTravel));
            SetReversedClipTime(
                DismountInput,
                alighting.ReversedClipPhase,
                dismountLengthSeconds);
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
