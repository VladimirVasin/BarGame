using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum CityPedestrianMotionState
    {
        Dormant = 0,
        Walking,
        RouteEnded
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
            IsYielding = shouldYield &&
                MotionState == CityPedestrianMotionState.Walking;
            bool moving = false;
            if (!IsYielding &&
                MotionState == CityPedestrianMotionState.Walking)
            {
                moving = AdvanceWalking(safeDeltaTime);
            }
            else if (IsYielding)
            {
                // A yield that never clears is a deadlock, not courtesy.
                blockedTime += safeDeltaTime;
            }

            if (blockedTime >= BlockedEscapeSeconds &&
                MotionState == CityPedestrianMotionState.Walking)
            {
                TakeDetour();
            }

            approachTarget = null;
            approachNodeDistances = null;
            presentation.Advance(
                safeDeltaTime,
                MotionState == CityPedestrianMotionState.Walking &&
                !IsYielding &&
                (moving || safeDeltaTime <= 0f));
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
            Vector3 target = plan.Nodes[targetNodeIndex].Position;
            Vector3 planarOffset = target - current;
            planarOffset.y = 0f;
            float distance = planarOffset.magnitude;
            if (distance <= ArrivalRadius)
            {
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
                ReachTargetNode();
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

            if (approachTarget.HasValue)
            {
                return SelectClosestCandidate(
                    candidates,
                    preferredCount > 0,
                    approachTarget.Value);
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
            Vector3 target)
        {
            int selectedLink = -1;
            float selectedGraphDistance = float.PositiveInfinity;
            float selectedDistance = float.PositiveInfinity;
            bool hasGraphDistances =
                approachNodeDistances != null &&
                approachNodeDistances.Count == plan.Nodes.Count;
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
                    ? approachNodeDistances[other]
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
