using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum CityPedestrianMotionState
    {
        Dormant = 0,
        Walking,
        RouteEnded,

        /// <summary>Walking the graph toward a Route 01 wait slot.</summary>
        ApproachingStop,

        /// <summary>Standing on the pavement facing the carriageway.</summary>
        WaitingAtStop,

        /// <summary>Crossing the doorway into a reserved seat.</summary>
        Boarding,

        /// <summary>Seated; the passenger controller owns the pose.</summary>
        Riding,

        /// <summary>Crossing the doorway back onto a roadside dock.</summary>
        Alighting
    }

    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public sealed class CityPedestrianActor : MonoBehaviour
    {
        public const float CollisionHeight = 1.7f;
        public const float CollisionCenterHeight = 0.85f;
        public const float TurnSpeedDegrees = 360f;

        /// <summary>
        /// A `1 m` sidewalk minus a `0.35 m` agent leaves this much room to
        /// either side of the lane centre. It is a shoulder-shift, not a way
        /// past: two walkers would need `0.70 m` of separation to pass, and
        /// the pavement does not have it.
        /// </summary>
        public const float MaximumLateralOffset = 0.15f;
        public const float LateralOffsetSpeed = 0.5f;
        public const float ArrivalRadius = 0.18f;
        public const float BlockedEscapeSeconds = 1.5f;
        public const float BlockedDisplacementFraction = 0.25f;

        /// <summary>
        /// Inside this range a walker heading for a stop leaves the graph and
        /// steps straight onto its wait slot. The slot sits on the same
        /// sidewalk lane as the node it came from, so the last few metres are
        /// ordinary pavement rather than a route.
        /// </summary>
        public const float DirectWaitSlotDistance = 7f;

        /// <summary>
        /// A doorway transfer is a short scripted walk, not a teleport. It
        /// runs at the design's own pace and gives up if the doorway cannot be
        /// reached in time, which returns the walker to the pavement.
        /// </summary>
        public const float TransferArrivalRadius = 0.12f;
        public const float TransferTurnSpeedDegrees = 540f;

        private readonly List<int> normalCandidates = new List<int>(4);
        private readonly List<int> crosswalkCandidates = new List<int>(2);
        private IWalkableArea walkableArea;
        private CityPedestrianPlan plan;
        private CityPedestrianPresentation presentation;
        private CharacterController characterController;
        private int previousNodeIndex = -1;
        private int targetNodeIndex = -1;
        private CityPedestrianLinkKind incomingLinkKind;
        private float agentRadius;
        private float speed;
        private float animationSpeed;
        private float animationPhase01;
        private int paletteVariant;
        private uint randomState;
        private bool isPrepared;
        private float lateralOffset;
        private float requestedLateralBias;
        private float requestedSpeedScale = 1f;
        private float blockedTime;
        private float? forcedCrosswalkRoll;
        private Vector3? approachTarget;
        private IReadOnlyList<float> approachNodeDistances;
        private Vector3? waitSlot;
        private Vector3 waitFacing = Vector3.forward;
        private IReadOnlyList<float> stopApproachNodeDistances;
        private Vector3 transferWaypoint;
        private Vector3 transferDestination;
        private Quaternion transferRotation = Quaternion.identity;
        private bool transferPassedWaypoint;

        public bool IsInitialized { get; private set; }
        public bool IsSpawned => presentation != null;
        public bool IsYielding { get; private set; }
        public CityPedestrianMotionState MotionState { get; private set; } =
            CityPedestrianMotionState.Dormant;
        public CityPedestrianPresentation Presentation => presentation;
        public bool HasPresentation => presentation != null;
        public bool CollisionEnabled =>
            characterController != null && characterController.enabled;
        public CharacterController CharacterController => characterController;
        public Vector3 Position => transform.position;
        public Vector3 LastDisplacement { get; private set; }
        public int PreviousNodeIndex => previousNodeIndex;
        public int TargetNodeIndex => targetNodeIndex;
        public float AgentRadius => agentRadius;
        public string SpawnAnchorId { get; private set; } = string.Empty;
        public string DesignId => presentation != null &&
                                  presentation.Registry != null
            ? presentation.Registry.DesignId
            : string.Empty;
        public float MovementSpeed => speed;
        public float AnimationSpeed => animationSpeed;
        public bool RouteEnded =>
            MotionState == CityPedestrianMotionState.RouteEnded;

        /// <summary>
        /// True while this walker belongs to Route 01 rather than to ordinary
        /// roaming. The population director must not recycle it on distance
        /// and must not accelerate its simulation.
        /// </summary>
        public bool IsRouteBound =>
            MotionState == CityPedestrianMotionState.ApproachingStop ||
            MotionState == CityPedestrianMotionState.WaitingAtStop ||
            IsAttachedToVehicle;

        /// <summary>
        /// True from the first step through the doorway until the last step
        /// back out. The bus must treat such a walker as cargo, never as an
        /// obstacle to yield to.
        /// </summary>
        public bool IsAttachedToVehicle =>
            MotionState == CityPedestrianMotionState.Boarding ||
            MotionState == CityPedestrianMotionState.Riding ||
            MotionState == CityPedestrianMotionState.Alighting;

        public bool TransferCompleted { get; private set; }
        public int StopWaitNodeIndex { get; private set; } = -1;
        public int CrosswalkDecisionCount { get; private set; }
        public int CrosswalksTaken { get; private set; }
        public Vector3 TravelDirection => GetTravelDirection();
        public float LateralOffset => lateralOffset;
        public float BlockedTime => blockedTime;
        public int DetourCount { get; private set; }

        /// <summary>
        /// Asks this walker to make room without stopping: a speed scale for
        /// queueing behind someone slower, and a shoulder-shift bias where
        /// `+1` leans to its own right.
        /// </summary>
        public void SetAvoidance(float speedScale, float lateralBias)
        {
            requestedSpeedScale = IsFinite(speedScale)
                ? Mathf.Clamp01(speedScale)
                : 1f;
            requestedLateralBias = IsFinite(lateralBias)
                ? Mathf.Clamp(lateralBias, -1f, 1f)
                : 0f;
        }

        public void Initialize(
            IWalkableArea allowedWalkableArea,
            float pedestrianRadius)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The city pedestrian actor is already initialized.");
            }

            walkableArea = allowedWalkableArea ??
                throw new ArgumentNullException(nameof(allowedWalkableArea));
            if (!IsFinite(pedestrianRadius) || pedestrianRadius <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pedestrianRadius));
            }

            agentRadius = pedestrianRadius;
            characterController = GetComponent<CharacterController>();
            characterController.enabled = false;
            characterController.height = CollisionHeight;
            characterController.radius = agentRadius;
            characterController.center = new Vector3(
                0f,
                CollisionCenterHeight,
                0f);
            characterController.minMoveDistance = 0f;
            characterController.stepOffset = 0.2f;
            IsInitialized = true;
        }

        public void PrepareSpawn(
            CityPedestrianPlan pedestrianPlan,
            CityPedestrianSpawnAnchor anchor,
            int firstTargetNodeIndex,
            float movementSpeed,
            float cycleSpeed,
            float cyclePhase01,
            int visualPaletteVariant,
            uint behaviorSeed)
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException(
                    "Initialize the city pedestrian actor first.");
            }

            if (isPrepared || presentation != null)
            {
                throw new InvalidOperationException(
                    "The city pedestrian actor already owns a spawn state.");
            }

            plan = pedestrianPlan ??
                throw new ArgumentNullException(nameof(pedestrianPlan));
            if (anchor == null)
            {
                throw new ArgumentNullException(nameof(anchor));
            }

            if (firstTargetNodeIndex != anchor.FirstNodeIndex &&
                firstTargetNodeIndex != anchor.SecondNodeIndex)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(firstTargetNodeIndex));
            }

            if (!IsFinite(movementSpeed) || movementSpeed <= 0f ||
                !IsFinite(cycleSpeed) || cycleSpeed <= 0f ||
                !IsFinite(cyclePhase01))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(movementSpeed),
                    "Spawn motion values must be finite and positive.");
            }

            characterController.enabled = false;
            transform.position = anchor.Position;
            previousNodeIndex = firstTargetNodeIndex == anchor.FirstNodeIndex
                ? anchor.SecondNodeIndex
                : anchor.FirstNodeIndex;
            targetNodeIndex = firstTargetNodeIndex;
            incomingLinkKind = CityPedestrianLinkKind.Sidewalk;
            speed = movementSpeed;
            animationSpeed = cycleSpeed;
            animationPhase01 = Mathf.Repeat(cyclePhase01, 1f);
            paletteVariant = visualPaletteVariant;
            randomState = behaviorSeed != 0u
                ? behaviorSeed
                : 0xA341316Cu;
            SpawnAnchorId = anchor.Id;
            MotionState = CityPedestrianMotionState.Walking;
            IsYielding = false;
            LastDisplacement = Vector3.zero;
            CrosswalkDecisionCount = 0;
            CrosswalksTaken = 0;
            isPrepared = true;
            FaceTargetImmediately();
        }

        public void BindPresentation(
            CityPedestrianPresentation pooledPresentation)
        {
            if (!isPrepared || plan == null)
            {
                throw new InvalidOperationException(
                    "Prepare a spawn before binding its presentation.");
            }

            if (presentation != null)
            {
                throw new InvalidOperationException(
                    "The city pedestrian actor already owns a presentation.");
            }

            if (pooledPresentation == null ||
                !pooledPresentation.IsInitialized)
            {
                throw new ArgumentException(
                    "A bound presentation must be initialized.",
                    nameof(pooledPresentation));
            }

            presentation = pooledPresentation;
            Transform visual = presentation.transform;
            visual.SetParent(transform, false);
            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;
            visual.localScale = Vector3.one;
            presentation.Registry.ApplyPaletteVariant(paletteVariant);
            presentation.gameObject.SetActive(true);
            presentation.ConfigureCycle(animationSpeed, animationPhase01);
            presentation.Advance(0f, true, true);
            characterController.enabled = true;
        }

        public CityPedestrianPresentation ReleasePresentation(
            Transform poolRoot)
        {
            if (characterController != null)
            {
                characterController.enabled = false;
            }

            CityPedestrianPresentation released = presentation;
            presentation = null;
            if (released != null)
            {
                released.ClearSeat();
                released.SetMoving(false);
                released.gameObject.SetActive(false);
                released.transform.SetParent(poolRoot, false);
                released.transform.localPosition = Vector3.zero;
                released.transform.localRotation = Quaternion.identity;
                released.transform.localScale = Vector3.one;
            }

            ResetSpawnState();
            return released;
        }

        public void Advance(
            float deltaTime,
            bool shouldYield = false,
            Vector3? initialApproachTarget = null,
            IReadOnlyList<float> initialApproachNodeDistances = null)
        {
            if (!IsSpawned)
            {
                return;
            }

            float safeDeltaTime = SanitizeDeltaTime(deltaTime);
            approachTarget = initialApproachTarget;
            approachNodeDistances = initialApproachNodeDistances;
            LastDisplacement = Vector3.zero;
            bool roaming = MotionState == CityPedestrianMotionState.Walking ||
                MotionState == CityPedestrianMotionState.ApproachingStop;
            bool transferring =
                MotionState == CityPedestrianMotionState.Boarding ||
                MotionState == CityPedestrianMotionState.Alighting;
            IsYielding = shouldYield && roaming;
            bool moving = false;
            if (transferring)
            {
                // The passenger controller owns the doorway. Ordinary
                // avoidance and the walkable area do not apply inside a bus.
                moving = AdvanceTransfer(safeDeltaTime);
            }
            else if (!IsYielding && roaming)
            {
                moving = AdvanceWalking(safeDeltaTime);
            }
            else if (IsYielding)
            {
                // A yield that never clears is a deadlock, not courtesy.
                blockedTime += safeDeltaTime;
            }

            if (blockedTime >= BlockedEscapeSeconds && roaming)
            {
                TakeDetour();
            }

            approachTarget = null;
            approachNodeDistances = null;
            presentation.Advance(
                safeDeltaTime,
                (roaming || transferring) &&
                !IsYielding &&
                (moving || safeDeltaTime <= 0f));
        }

        /// <summary>
        /// Sends this walker to a Route 01 wait slot. Routing reuses the same
        /// graph guidance the population director already runs toward the
        /// hero, seeded at the stop instead.
        /// </summary>
        public bool BeginStopApproach(
            int waitNodeIndex,
            Vector3 slotPosition,
            Vector3 facing,
            IReadOnlyList<float> waitNodeDistances)
        {
            if (!IsSpawned ||
                MotionState != CityPedestrianMotionState.Walking ||
                waitNodeIndex < 0 ||
                targetNodeIndex < 0 ||
                plan == null ||
                waitNodeDistances == null ||
                waitNodeDistances.Count != plan.Nodes.Count ||
                float.IsPositiveInfinity(waitNodeDistances[targetNodeIndex]))
            {
                return false;
            }

            StopWaitNodeIndex = waitNodeIndex;
            stopApproachNodeDistances = waitNodeDistances;
            waitSlot = slotPosition;
            Vector3 planarFacing = facing;
            planarFacing.y = 0f;
            waitFacing = planarFacing.sqrMagnitude > 0.0001f
                ? planarFacing.normalized
                : transform.forward;
            MotionState = CityPedestrianMotionState.ApproachingStop;
            blockedTime = 0f;
            return true;
        }

        /// <summary>
        /// Returns a stop-bound walker to ordinary roaming without moving it.
        /// </summary>
        public void CancelStopApproach()
        {
            if (MotionState != CityPedestrianMotionState.ApproachingStop &&
                MotionState != CityPedestrianMotionState.WaitingAtStop)
            {
                return;
            }

            StopWaitNodeIndex = -1;
            stopApproachNodeDistances = null;
            waitSlot = null;
            blockedTime = 0f;
            MotionState = targetNodeIndex >= 0
                ? CityPedestrianMotionState.Walking
                : CityPedestrianMotionState.RouteEnded;
        }

        /// <summary>
        /// Starts a scripted doorway transfer through
        /// <paramref name="waypoint"/> to <paramref name="destination"/>. The
        /// capsule is released for the crossing: a bus floor is not walkable
        /// area, and the doorway is narrower than the lateral room the
        /// pavement avoidance assumes.
        /// </summary>
        public bool BeginTransfer(
            bool boarding,
            Vector3 waypoint,
            Vector3 destination,
            Quaternion destinationRotation)
        {
            if (!IsSpawned)
            {
                return false;
            }

            if (boarding)
            {
                if (MotionState != CityPedestrianMotionState.WaitingAtStop &&
                    MotionState != CityPedestrianMotionState.ApproachingStop)
                {
                    return false;
                }
            }
            else if (MotionState != CityPedestrianMotionState.Riding)
            {
                return false;
            }

            transferWaypoint = waypoint;
            transferDestination = destination;
            transferRotation = destinationRotation;
            transferPassedWaypoint = false;
            TransferCompleted = false;
            blockedTime = 0f;
            waitSlot = null;
            if (characterController != null)
            {
                characterController.enabled = false;
            }

            presentation.ClearSeat();
            MotionState = boarding
                ? CityPedestrianMotionState.Boarding
                : CityPedestrianMotionState.Alighting;
            return true;
        }

        /// <summary>
        /// Hands the seated pose to the passenger controller. The walker stops
        /// simulating: its root is late-synchronised to the actor-local seat
        /// pose every frame, exactly as the hero's is.
        /// </summary>
        public bool BeginRide(
            Transform seatAnchor,
            CityPedestrianSeatedRide seatedRide)
        {
            if (!IsSpawned ||
                MotionState != CityPedestrianMotionState.Boarding ||
                !presentation.TrySeat(seatAnchor, seatedRide))
            {
                return false;
            }

            if (characterController != null)
            {
                characterController.enabled = false;
            }

            TransferCompleted = false;
            MotionState = CityPedestrianMotionState.Riding;
            return true;
        }

        /// <summary>
        /// Seats a freshly activated walker that is already aboard. A bus
        /// spawns with passengers in it the same way it spawns with a driver:
        /// they were never on this pavement, so there is no doorway crossing
        /// to play and no transfer to complete.
        /// </summary>
        public bool BeginSeatedRide(
            Transform seatAnchor,
            CityPedestrianSeatedRide seatedRide)
        {
            if (!IsSpawned ||
                MotionState != CityPedestrianMotionState.Walking ||
                !presentation.TrySeat(seatAnchor, seatedRide))
            {
                return false;
            }

            if (characterController != null)
            {
                characterController.enabled = false;
            }

            waitSlot = null;
            stopApproachNodeDistances = null;
            blockedTime = 0f;
            TransferCompleted = false;
            MotionState = CityPedestrianMotionState.Riding;
            return true;
        }

        /// <summary>
        /// Places this walker back on the pavement and resumes ordinary
        /// roaming from the stop's own graph node.
        /// </summary>
        public void ResumeRoaming(int fromNodeIndex)
        {
            if (presentation != null)
            {
                presentation.ClearSeat();
            }

            StopWaitNodeIndex = -1;
            stopApproachNodeDistances = null;
            waitSlot = null;
            transferPassedWaypoint = false;
            TransferCompleted = false;
            blockedTime = 0f;
            lateralOffset = 0f;
            if (fromNodeIndex >= 0 && plan != null &&
                fromNodeIndex < plan.Nodes.Count)
            {
                previousNodeIndex = -1;
                targetNodeIndex = fromNodeIndex;
                incomingLinkKind = CityPedestrianLinkKind.Sidewalk;
            }

            if (characterController != null && IsSpawned)
            {
                characterController.enabled = true;
            }

            MotionState = targetNodeIndex >= 0
                ? CityPedestrianMotionState.Walking
                : CityPedestrianMotionState.RouteEnded;
            FaceTargetImmediately();
        }

        /// <summary>
        /// Moves the world root while riding. The gameplay hierarchy is never
        /// reparented, so a pooled or deactivated bus slot cannot disable a
        /// passenger.
        /// </summary>
        public void SetRidePose(Vector3 position, Quaternion rotation)
        {
            if (MotionState != CityPedestrianMotionState.Riding)
            {
                return;
            }

            transform.SetPositionAndRotation(position, rotation);
        }

        private bool AdvanceTransfer(float deltaTime)
        {
            if (deltaTime <= 0f || TransferCompleted)
            {
                return false;
            }

            Vector3 current = transform.position;
            Vector3 target = transferPassedWaypoint
                ? transferDestination
                : transferWaypoint;
            Vector3 offset = target - current;
            float distance = offset.magnitude;
            if (distance <= TransferArrivalRadius)
            {
                if (!transferPassedWaypoint)
                {
                    transferPassedWaypoint = true;
                    return true;
                }

                transform.position = transferDestination;
                transform.rotation = transferRotation;
                TransferCompleted = true;
                return false;
            }

            Vector3 direction = offset / distance;
            float step = Mathf.Min(speed * deltaTime, distance);
            transform.position = current + (direction * step);
            Vector3 planar = direction;
            planar.y = 0f;
            if (planar.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    Quaternion.LookRotation(planar.normalized, Vector3.up),
                    TransferTurnSpeedDegrees * deltaTime);
            }

            LastDisplacement = transform.position - current;
            return true;
        }

        internal void ForceNextCrosswalkRoll(float roll)
        {
            forcedCrosswalkRoll = Mathf.Clamp01(roll);
        }

        private bool AdvanceWalking(float deltaTime)
        {
            if (deltaTime <= 0f || targetNodeIndex < 0)
            {
                return false;
            }

            Vector3 current = transform.position;
            bool directToWaitSlot = TryGetDirectWaitSlot(current, out Vector3 slot);
            Vector3 target = directToWaitSlot
                ? slot
                : plan.Nodes[targetNodeIndex].Position;
            Vector3 planarOffset = target - current;
            planarOffset.y = 0f;
            float distance = planarOffset.magnitude;
            if (distance <= ArrivalRadius)
            {
                if (directToWaitSlot)
                {
                    ArriveAtWaitSlot();
                    return false;
                }

                ReachTargetNode();
                return false;
            }

            Vector3 direction = planarOffset / distance;
            // Steer at a point offset across the lane rather than at the node
            // itself, so making room is ordinary steering and the walker
            // re-centres on its own once the way is clear.
            Vector3 right = new Vector3(direction.z, 0f, -direction.x);
            lateralOffset = Mathf.MoveTowards(
                lateralOffset,
                requestedLateralBias * MaximumLateralOffset,
                LateralOffsetSpeed * deltaTime);
            Vector3 steerOffset =
                (target + (right * lateralOffset)) - current;
            steerOffset.y = 0f;
            float steerDistance = steerOffset.magnitude;
            Vector3 steerDirection = steerDistance > 0.0001f
                ? steerOffset / steerDistance
                : direction;
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(steerDirection, Vector3.up),
                TurnSpeedDegrees * deltaTime);
            float step = speed * requestedSpeedScale * deltaTime;
            float intended = Mathf.Min(step, steerDistance);
            float heightAmount = Mathf.Min(1f, intended / distance);
            Vector3 desired = current + (steerDirection * intended);
            desired.y = Mathf.Lerp(current.y, target.y, heightAmount);
            Vector3 constrained = walkableArea.Constrain(
                current,
                desired,
                agentRadius);
            if (CollisionEnabled)
            {
                characterController.Move(constrained - current);
            }
            else
            {
                transform.position = constrained;
            }

            LastDisplacement = transform.position - current;
            Vector3 travelled = LastDisplacement;
            travelled.y = 0f;
            // Wanting to move and not moving is the signal that matters: a
            // walker pressed against a prop and one nose to nose with another
            // walker look identical from here, and both need the same way out.
            if (intended > 0.0001f &&
                travelled.magnitude < intended * BlockedDisplacementFraction)
            {
                blockedTime += deltaTime;
            }
            else
            {
                blockedTime = 0f;
            }

            Vector3 remaining = transform.position - target;
            remaining.y = 0f;
            if (remaining.magnitude <= ArrivalRadius)
            {
                if (directToWaitSlot)
                {
                    ArriveAtWaitSlot();
                }
                else
                {
                    ReachTargetNode();
                }
            }

            return LastDisplacement.sqrMagnitude > 0.000001f;
        }

        private void ReachTargetNode()
        {
            int reachedNode = targetNodeIndex;
            normalCandidates.Clear();
            crosswalkCandidates.Clear();
            IReadOnlyList<int> linkIndices =
                plan.GetLinkIndices(reachedNode);
            for (int index = 0; index < linkIndices.Count; index++)
            {
                int linkIndex = linkIndices[index];
                CityPedestrianLink link = plan.Links[linkIndex];
                int other = link.Other(reachedNode);
                if (other == previousNodeIndex)
                {
                    continue;
                }

                if (link.Kind == CityPedestrianLinkKind.Crosswalk)
                {
                    crosswalkCandidates.Add(linkIndex);
                }
                else
                {
                    normalCandidates.Add(linkIndex);
                }
            }

            int selectedLink = -1;
            bool canChooseCrosswalk =
                incomingLinkKind != CityPedestrianLinkKind.Crosswalk &&
                crosswalkCandidates.Count > 0;
            if (canChooseCrosswalk)
            {
                CrosswalkDecisionCount++;
                bool takeCrosswalk = NextCrosswalkRoll() <
                                     CityPedestrianPlanner
                                         .CrosswalkChoiceProbability;
                if (takeCrosswalk || normalCandidates.Count == 0)
                {
                    selectedLink = SelectCandidate(crosswalkCandidates);
                    CrosswalksTaken++;
                }
            }

            if (selectedLink < 0 && normalCandidates.Count > 0)
            {
                selectedLink = SelectCandidate(normalCandidates);
            }

            if (selectedLink < 0 && crosswalkCandidates.Count > 0)
            {
                selectedLink = SelectCandidate(crosswalkCandidates);
            }

            if (selectedLink < 0)
            {
                targetNodeIndex = -1;
                MotionState = CityPedestrianMotionState.RouteEnded;
                return;
            }

            CityPedestrianLink selected = plan.Links[selectedLink];
            previousNodeIndex = reachedNode;
            targetNodeIndex = selected.Other(reachedNode);
            incomingLinkKind = selected.Kind;
        }

        private bool TryGetDirectWaitSlot(Vector3 current, out Vector3 slot)
        {
            slot = default;
            if (MotionState != CityPedestrianMotionState.ApproachingStop ||
                !waitSlot.HasValue)
            {
                return false;
            }

            Vector3 offset = waitSlot.Value - current;
            offset.y = 0f;
            if (offset.sqrMagnitude >
                DirectWaitSlotDistance * DirectWaitSlotDistance)
            {
                return false;
            }

            slot = waitSlot.Value;
            return true;
        }

        private void ArriveAtWaitSlot()
        {
            MotionState = CityPedestrianMotionState.WaitingAtStop;
            blockedTime = 0f;
            lateralOffset = 0f;
            if (waitFacing.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(
                    waitFacing,
                    Vector3.up);
            }
        }

        /// <summary>
        /// Turns back along the current link. On a pavement this narrow there
        /// is no way past a blocked lane, so the only honest resolution is to
        /// go the other way; the node behind then offers its other branches,
        /// because <see cref="ReachTargetNode"/> refuses to backtrack.
        /// </summary>
        private void TakeDetour()
        {
            blockedTime = 0f;
            lateralOffset = 0f;
            if (previousNodeIndex < 0 || targetNodeIndex < 0)
            {
                return;
            }

            int reached = targetNodeIndex;
            targetNodeIndex = previousNodeIndex;
            previousNodeIndex = reached;
            DetourCount++;
            FaceTargetImmediately();
        }

        private int SelectCandidate(IReadOnlyList<int> candidates)
        {
            int preferredCount = 0;
            for (int index = 0; index < candidates.Count; index++)
            {
                CityPedestrianLink link = plan.Links[candidates[index]];
                int other = link.Other(targetNodeIndex);
                if (plan.GetLinkIndices(other).Count > 1)
                {
                    preferredCount++;
                }
            }

            if (MotionState == CityPedestrianMotionState.ApproachingStop &&
                stopApproachNodeDistances != null &&
                waitSlot.HasValue)
            {
                return SelectClosestCandidate(
                    candidates,
                    preferredCount > 0,
                    waitSlot.Value,
                    stopApproachNodeDistances);
            }

            if (approachTarget.HasValue)
            {
                return SelectClosestCandidate(
                    candidates,
                    preferredCount > 0,
                    approachTarget.Value,
                    approachNodeDistances);
            }

            int selectableCount = preferredCount > 0
                ? preferredCount
                : candidates.Count;
            int selection = Mathf.FloorToInt(
                NextRandom01() * selectableCount);
            for (int index = 0; index < candidates.Count; index++)
            {
                CityPedestrianLink link = plan.Links[candidates[index]];
                int other = link.Other(targetNodeIndex);
                if (preferredCount > 0 &&
                    plan.GetLinkIndices(other).Count <= 1)
                {
                    continue;
                }

                if (selection-- == 0)
                {
                    return candidates[index];
                }
            }

            return candidates[0];
        }

        private int SelectClosestCandidate(
            IReadOnlyList<int> candidates,
            bool preferConnectedNodes,
            Vector3 target,
            IReadOnlyList<float> nodeDistances)
        {
            int selectedLink = -1;
            float selectedGraphDistance = float.PositiveInfinity;
            float selectedDistance = float.PositiveInfinity;
            bool hasGraphDistances =
                nodeDistances != null &&
                nodeDistances.Count == plan.Nodes.Count;
            for (int index = 0; index < candidates.Count; index++)
            {
                CityPedestrianLink link = plan.Links[candidates[index]];
                int other = link.Other(targetNodeIndex);
                if (preferConnectedNodes &&
                    plan.GetLinkIndices(other).Count <= 1)
                {
                    continue;
                }

                Vector3 position = plan.Nodes[other].Position;
                float deltaX = position.x - target.x;
                float deltaZ = position.z - target.z;
                float distance = (deltaX * deltaX) +
                                 (deltaZ * deltaZ);
                float graphDistance = hasGraphDistances
                    ? nodeDistances[other]
                    : float.PositiveInfinity;
                if (selectedLink < 0 ||
                    graphDistance < selectedGraphDistance - 0.0001f ||
                    (Mathf.Abs(
                         graphDistance - selectedGraphDistance) <= 0.0001f &&
                     distance < selectedDistance))
                {
                    selectedGraphDistance = graphDistance;
                    selectedDistance = distance;
                    selectedLink = candidates[index];
                }
            }

            return selectedLink >= 0 ? selectedLink : candidates[0];
        }

        private float NextCrosswalkRoll()
        {
            if (forcedCrosswalkRoll.HasValue)
            {
                float result = forcedCrosswalkRoll.Value;
                forcedCrosswalkRoll = null;
                return result;
            }

            return NextRandom01();
        }

        private float NextRandom01()
        {
            randomState ^= randomState << 13;
            randomState ^= randomState >> 17;
            randomState ^= randomState << 5;
            return CityPedestrianStableHash.ToUnitFloat(randomState);
        }

        private void FaceTargetImmediately()
        {
            Vector3 direction = GetTravelDirection();
            if (direction.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(
                    direction,
                    Vector3.up);
            }
        }

        private Vector3 GetTravelDirection()
        {
            if (plan == null || targetNodeIndex < 0)
            {
                return Vector3.zero;
            }

            Vector3 direction =
                plan.Nodes[targetNodeIndex].Position - transform.position;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : Vector3.zero;
        }

        private void ResetSpawnState()
        {
            plan = null;
            previousNodeIndex = -1;
            targetNodeIndex = -1;
            incomingLinkKind = CityPedestrianLinkKind.Sidewalk;
            speed = 0f;
            animationSpeed = 0f;
            animationPhase01 = 0f;
            paletteVariant = 0;
            randomState = 0u;
            isPrepared = false;
            lateralOffset = 0f;
            requestedLateralBias = 0f;
            requestedSpeedScale = 1f;
            blockedTime = 0f;
            forcedCrosswalkRoll = null;
            approachTarget = null;
            approachNodeDistances = null;
            stopApproachNodeDistances = null;
            waitSlot = null;
            waitFacing = Vector3.forward;
            transferPassedWaypoint = false;
            transferRotation = Quaternion.identity;
            TransferCompleted = false;
            StopWaitNodeIndex = -1;
            SpawnAnchorId = string.Empty;
            MotionState = CityPedestrianMotionState.Dormant;
            IsYielding = false;
            LastDisplacement = Vector3.zero;
            CrosswalkDecisionCount = 0;
            CrosswalksTaken = 0;
            DetourCount = 0;
        }

        private static float SanitizeDeltaTime(float deltaTime)
        {
            return IsFinite(deltaTime)
                ? Mathf.Max(0f, deltaTime)
                : 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
