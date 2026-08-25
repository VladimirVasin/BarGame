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

        /// <summary>Off the metal and round into the driver's seat. Three
        /// quarters of a second, once, and not reversible.</summary>
        Boarding = 1,

        /// <summary>Behind the wheel, waiting again.</summary>
        Driving = 2
    }

    /// <summary>
    /// Drives the Ferryman through his three postures on one manual
    /// PlayableGraph - the watchman/fisherman idiom, with a mixer instead
    /// of a single clip because he has somewhere to go.
    ///
    /// The clip library contains no root motion by contract, so the body
    /// motion of the board transition is authored and the METRE it covers
    /// is not: this component carries the root from the bonnet to the
    /// driver's seat while the clip plays. Neither pose is a constant.
    /// The perch comes from the car's own soles anchor and the seat is
    /// solved by measuring where this rig actually puts its pelvis in the
    /// drive pose and offsetting the root until that lands on the car's
    /// seat anchor. Both therefore survive either generator moving.
    ///
    /// It also publishes the wait loop's own phase, because the coin has to
    /// belong to the hand that is throwing it rather than to a second
    /// free-running timer, and the only way to guarantee that is to read it
    /// off the clip that is moving the hand.
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

        /// <summary>How long the wait blends into the board transition.
        /// Short: he is supposed to move the instant he is asked.</summary>
        public const float BoardBlendSeconds = 0.12f;

        private const int WaitInput = 0;
        private const int BoardInput = 1;
        private const int DriveInput = 2;

        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private AnimationClipPlayable waitPlayable;
        private AnimationClipPlayable boardPlayable;
        private AnimationClipPlayable drivePlayable;
        private float waitLengthSeconds = 1f;
        private float boardLengthSeconds = 1f;
        private float playbackSpeed = 1f;
        private bool hasGraph;

        private Vector3 perchPosition;
        private Quaternion perchRotation;
        private Vector3 drivePosition;
        private Quaternion driveRotation;
        private float boardElapsedSeconds;
        private float blendElapsedSeconds;

        public bool IsInitialized { get; private set; }
        public LastRouteFerrymanPhase Phase { get; private set; }
        public bool IsWaiting => Phase == LastRouteFerrymanPhase.Waiting;
        public bool IsDriving => Phase == LastRouteFerrymanPhase.Driving;

        /// <summary>
        /// The wait loop's own position, in `[0, 1)`. Zero before the graph
        /// exists and frozen once he boards, so a reader never has to
        /// special-case either.
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
            AnimationClip board = registry.ActionClip;
            AnimationClip drive = registry.SitClip;
            if (wait == null || board == null || drive == null)
            {
                throw new InvalidOperationException(
                    "The Ferryman prefab needs its wait loop, its board " +
                    "transition and its driving loop.");
            }

            waitLengthSeconds = Mathf.Max(0.0001f, wait.length);
            boardLengthSeconds = Mathf.Max(0.0001f, board.length);
            playbackSpeed = Mathf.Max(0.05f, stance.PlaybackSpeed);
            registry.ApplyPaletteVariant(stance.PaletteVariant);

            perchRotation = Quaternion.LookRotation(stance.Facing, Vector3.up);
            perchPosition = stance.Position;

            graph = PlayableGraph.Create("Last Route Ferryman");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, 3);
            waitPlayable = CreateClipPlayable(wait);
            boardPlayable = CreateClipPlayable(board);
            drivePlayable = CreateClipPlayable(drive);
            graph.Connect(waitPlayable, 0, mixer, WaitInput);
            graph.Connect(boardPlayable, 0, mixer, BoardInput);
            graph.Connect(drivePlayable, 0, mixer, DriveInput);
            AnimationPlayableOutput
                .Create(graph, "Last Route Ferryman Pose", registry.Animator)
                .SetSourcePlayable(mixer);
            graph.Play();
            hasGraph = true;

            SolveDriverSeat(registry, car);

            SetWeights(1f, 0f, 0f);
            waitPlayable.SetTime(
                Mathf.Repeat(stance.PhaseOffsetSeconds, wait.length));
            graph.Evaluate(0f);

            SolvePerch(registry, anchors, stance);

            Phase = LastRouteFerrymanPhase.Waiting;
            transform.SetPositionAndRotation(perchPosition, perchRotation);
            IsInitialized = true;
        }

        /// <summary>
        /// He said yes. Off the bonnet and into the car - once; a second
        /// call is ignored rather than restarting the jump.
        /// </summary>
        public bool TryBeginBoarding()
        {
            if (!IsInitialized ||
                Phase != LastRouteFerrymanPhase.Waiting)
            {
                return false;
            }

            Phase = LastRouteFerrymanPhase.Boarding;
            boardElapsedSeconds = 0f;
            blendElapsedSeconds = 0f;
            boardPlayable.SetTime(0.0);
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
            SetWeights(0f, 0f, 1f);
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

        private void SetWeights(float wait, float board, float drive)
        {
            mixer.SetInputWeight(WaitInput, wait);
            mixer.SetInputWeight(BoardInput, board);
            mixer.SetInputWeight(DriveInput, drive);
        }

        private void LateUpdate()
        {
            if (!hasGraph)
            {
                return;
            }

            float step =
                Mathf.Min(Time.deltaTime, MaximumStepSeconds) * playbackSpeed;
            if (Phase == LastRouteFerrymanPhase.Boarding)
            {
                AdvanceBoarding(step);
            }

            graph.Evaluate(step);
        }

        private void AdvanceBoarding(float step)
        {
            boardElapsedSeconds += step;
            blendElapsedSeconds += step;

            // The wait hands over to the transition quickly, and the
            // transition to the drive not at all: the board clip is
            // authored to CLOSE on the drive clip's own base pose, so
            // there is nothing to blend at the far end.
            float boardWeight = BoardBlendSeconds > 0f
                ? Mathf.Clamp01(blendElapsedSeconds / BoardBlendSeconds)
                : 1f;

            float travel = Mathf.Clamp01(
                boardElapsedSeconds / boardLengthSeconds);
            transform.SetPositionAndRotation(
                Vector3.Lerp(
                    perchPosition,
                    drivePosition,
                    Mathf.SmoothStep(0f, 1f, travel)),
                Quaternion.Slerp(
                    perchRotation,
                    driveRotation,
                    Mathf.SmoothStep(0f, 1f, travel)));

            if (travel < 1f)
            {
                SetWeights(1f - boardWeight, boardWeight, 0f);
                return;
            }

            SetWeights(0f, 0f, 1f);
            Phase = LastRouteFerrymanPhase.Driving;
            transform.SetPositionAndRotation(drivePosition, driveRotation);
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
