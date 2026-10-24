using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Eight visit-owned slots converge on the clock's attendance target.
    /// Only one guest traverses the doorway/aisles at once; settled rigs keep
    /// their ordinary shared drinking and conversation owners.
    /// </summary>
    [DefaultExecutionOrder(900)]
    [DisallowMultipleComponent]
    public sealed class BarCrowdController : MonoBehaviour
    {
        public const float ChangeIntervalSeconds = 9f;
        public const float WalkingSpeed = .82f;
        public const float SeatTransitionSeconds = 1.8f;
        public const float ReleaseHandSeconds = .3f;
        private const float TurnSpeed = 150f;
        private const float DoorSeconds = .8f;
        private const float BodyRadius = .17f;

        private enum TransitPhase { OpeningForArrival, Arriving, Sitting, ReleasingDrink, Standing, Leaving, OpeningForDeparture, CrossingOut, ClosingAfterDeparture }

        private sealed class Transit
        {
            public BarPatron Patron;
            public int Slot;
            public TransitPhase Phase;
            public float Seconds;
            public int Corner;
            public Vector3 Dock;
            public Transform Seat;
            public CityPedestrianSeatedRide Ride;
            public RigPose Pose;
            public Transform Vessel;
            public Transform Hand;
            public Transform UpperArm;
            public Transform Forearm;
            public NpcHandPose Fingers;
            public Vector3 VesselHandPosition;
            public Quaternion VesselHandRotation;
            public AnimatorCullingMode PreviousCulling;
            public bool CarryDrink;
            public bool ReleaseEndpointShown;
            public readonly FootContact[] Feet = new FootContact[2];
        }

        private sealed class FootContact
        {
            public Transform Thigh, Shin, Foot;
            public Vector3 SeatedPosition, StandingPosition;
            public Quaternion SeatedRotation, StandingRotation;
        }

        /// <summary>Cached shared-graph endpoints; interpolation never replaces the character or hides it.</summary>
        private sealed class RigPose
        {
            private readonly Transform[] bones;
            private readonly Vector3[] seatedPositions;
            private readonly Quaternion[] seatedRotations;
            private readonly Vector3[] standingPositions;
            private readonly Quaternion[] standingRotations;

            public RigPose(Transform model)
            {
                bones = model.GetComponentsInChildren<Transform>(true);
                seatedPositions = new Vector3[bones.Length];
                seatedRotations = new Quaternion[bones.Length];
                standingPositions = new Vector3[bones.Length];
                standingRotations = new Quaternion[bones.Length];
                Capture(seatedPositions, seatedRotations);
            }

            public void CaptureStanding() => Capture(standingPositions, standingRotations);

            private void Capture(Vector3[] positions, Quaternion[] rotations)
            {
                for (int index = 0; index < bones.Length; index++)
                {
                    positions[index] = bones[index].localPosition;
                    rotations[index] = bones[index].localRotation;
                }
            }

            public void Apply(float seated)
            {
                for (int index = 0; index < bones.Length; index++)
                {
                    if (bones[index] == null) continue;
                    bones[index].localPosition = Vector3.Lerp(standingPositions[index], seatedPositions[index], seated);
                    bones[index].localRotation = Quaternion.Slerp(standingRotations[index], seatedRotations[index], seated);
                }
            }
        }

        private readonly List<BarPatron> patrons = new List<BarPatron>(BarBusinessHours.MaximumGuests);
        private readonly Vector3[] route = new Vector3[7];
        private BarPatronWorldBuilder.Visit visit;
        private BarInteriorLayoutPlan layout;
        private BarPatron[] slots;
        private BarAbandonedDrink[] abandoned;
        private Func<string, bool> isAnchorAvailable;
        private Func<BarPatron, bool> canDepart;
        private Transform room;
        private Transform door;
        private Quaternion closedDoorRotation;
        private float doorOpen;
        private Transit transit;
        private int routeCount;
        private int seed;
        private uint random;
        private float nextChange;
        private int lastMinute = -1;
        private int lastDay = -1;
        private RaycastHit[] movementHits = new RaycastHit[16];
        private bool doorPhysicsDirty;

        public IReadOnlyList<BarPatron> Patrons => patrons;
        public bool IsInitialized { get; private set; }
        public int TargetCount { get; private set; }
        public bool HasGuestInTransit => transit != null;
        public bool AutoAdvance { get; set; } = true;
        public string TransitPhaseName => transit != null ? transit.Phase.ToString() : string.Empty;
        public string TransitPatronId => transit != null ? transit.Patron.Anchor.Id : string.Empty;
        public Vector3 TransitPosition => transit != null && transit.Patron.Registry != null
            ? transit.Patron.Registry.transform.position : Vector3.zero;
        public Vector3 TransitTarget { get; private set; }
        public string TransitBlockedBy { get; private set; } = string.Empty;
        public float TransitFootContactError { get; private set; }
        public float TransitVesselGripError { get; private set; }
        public int PendingAbandonedCount { get; private set; }
        public event Action PresenceChanged;
        public event Action<BarAbandonedDrink> DrinkAbandoned;

        public void Initialize(Transform authoredRoom, BarInteriorLayoutPlan plan, int attendanceSeed,
            Func<string, bool> anchorAvailable = null, Func<BarPatron, bool> safeToDepart = null)
        {
            if (IsInitialized) throw new InvalidOperationException("Bar crowd is already initialized.");
            room = authoredRoom != null ? authoredRoom : throw new ArgumentNullException(nameof(authoredRoom));
            layout = plan ?? throw new ArgumentNullException(nameof(plan));
            seed = attendanceSeed;
            random = unchecked((uint)seed ^ (uint)GameSessionState.GameDayIndex * 0x9E3779B9u ^
                               (uint)(GameSessionState.GameMinuteOfDay / 60) * 0x85EBCA6Bu) | 1u;
            isAnchorAvailable = anchorAvailable;
            canDepart = safeToDepart;
            visit = BarPatronWorldBuilder.CreateVisit(transform, layout, room);
            slots = new BarPatron[visit.Count];
            abandoned = new BarAbandonedDrink[visit.Count];
            CreateEntranceHinge();
            RefreshTarget();
            int[] order = new int[visit.Count];
            for (int index = 0; index < order.Length; index++) order[index] = index;
            for (int index = order.Length - 1; index > 0; index--)
            {
                int other = (int)(NextRandom() % (uint)(index + 1));
                (order[index], order[other]) = (order[other], order[index]);
            }
            for (int index = 0; index < order.Length && patrons.Count < TargetCount; index++)
            {
                int slot = order[index];
                BarPatron guest = visit.Create(slot);
                if (guest == null) continue;
                slots[slot] = guest;
                patrons.Add(guest);
            }
            visit.Root.gameObject.AddComponent<BarPatronAnimator>().Initialize(patrons);
            nextChange = ChangeIntervalSeconds;
            IsInitialized = true;
        }

        public bool IsAnchorOccupied(string id)
        {
            if (visit == null || string.IsNullOrEmpty(id)) return false;
            for (int slot = 0; slot < slots.Length; slot++)
                if ((slots[slot] != null || abandoned[slot] != null) && visit.GetAnchor(slot).Id == id) return true;
            return false;
        }

        public bool TryGetAbandonedDrink(string anchorId, out BarAbandonedDrink drink)
        {
            if (abandoned != null)
                for (int slot = 0; slot < abandoned.Length; slot++)
                    if (abandoned[slot] != null && abandoned[slot].AnchorId == anchorId)
                    {
                        drink = abandoned[slot];
                        return true;
                    }
            drink = null;
            return false;
        }

        /// <summary>Enumerates only live reservations, with no temporary list or scene search.</summary>
        public BarAbandonedDrink GetPendingAbandonedDrink(int index)
        {
            if (index < 0 || index >= PendingAbandonedCount) throw new ArgumentOutOfRangeException(nameof(index));
            for (int slot = 0; slot < abandoned.Length; slot++)
                if (abandoned[slot] != null && index-- == 0) return abandoned[slot];
            throw new InvalidOperationException("The abandoned-drink reservation count is inconsistent.");
        }

        /// <summary>The physical collector disposes the vessel before releasing this exact reservation.</summary>
        public bool CompleteDrinkCleanup(BarAbandonedDrink drink)
        {
            if (!IsInitialized || drink == null || drink.IsCleanupComplete || !drink.IsReadyForCollection ||
                drink.Slot < 0 || drink.Slot >= abandoned.Length || !ReferenceEquals(abandoned[drink.Slot], drink)) return false;
            Transform vessel = drink.DrinkProp.Root;
            if (vessel != null && vessel.gameObject.activeInHierarchy) return false;
            abandoned[drink.Slot] = null;
            PendingAbandonedCount--;
            drink.IsCleanupComplete = true;
            PresenceChanged?.Invoke();
            return true;
        }

        private void Update() { if (AutoAdvance) Advance(Time.deltaTime); }

        public void Advance(float deltaTime)
        {
            if (!float.IsFinite(deltaTime) || deltaTime < 0f) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (!IsInitialized || !isActiveAndEnabled || GameTimeScaleRuntime.IsPaused ||
                !GameSessionState.IsGameTimeRunning) return;
            RefreshTarget();
            // A hitch cannot skip doorway or contact phases. Calendar authority
            // still updates the target immediately; physical attendance catches up.
            float step = Mathf.Min(deltaTime, .1f);
            UpdateDoor(step);
            if (transit != null) AdvanceTransit(step);
            else
            {
                nextChange -= deltaTime;
                if (nextChange > 0f) return;
                nextChange = ChangeIntervalSeconds;
                if (patrons.Count < TargetCount) TryArrival();
                else if (patrons.Count > TargetCount) TryDeparture();
            }
        }

        private void RefreshTarget()
        {
            int minute = GameSessionState.GameMinuteOfDay;
            int day = GameSessionState.GameDayIndex;
            if (minute == lastMinute && day == lastDay) return;
            lastMinute = minute;
            lastDay = day;
            TargetCount = Mathf.Min(visit.Count, BarBusinessHours.TargetGuestCount(day, minute, seed));
        }

        private void TryArrival()
        {
            if (slots.Length == 0 || door == null || doorOpen > .001f) return;
            int first = (int)(NextRandom() % (uint)slots.Length);
            for (int probe = 0; probe < slots.Length; probe++)
            {
                int slot = (first + probe) % slots.Length;
                BarNpcAnchor anchor = visit.GetAnchor(slot);
                if (slots[slot] != null || abandoned[slot] != null ||
                    isAnchorAvailable != null && !isAnchorAvailable(anchor.Id)) continue;
                BarPatron patron = visit.Create(slot);
                if (patron == null) continue;
                slots[slot] = patron;
                transit = PrepareTransit(patron, slot, true);
                transit.Phase = TransitPhase.OpeningForArrival;
                patron.Registry.transform.SetPositionAndRotation(visit.Root.TransformPoint(OutsideDoor),
                    visit.Root.rotation);
                patron.Presentation.Advance(0f, false, true);
                BuildRoute(anchor, transit.Dock);
                PresenceChanged?.Invoke();
                return;
            }
        }

        private void TryDeparture()
        {
            if (patrons.Count <= TargetCount || patrons.Count == 0 || door == null) return;
            int first = (int)(NextRandom() % (uint)slots.Length);
            for (int probe = 0; probe < slots.Length; probe++)
            {
                int slot = (first + probe) % slots.Length;
                BarPatron patron = slots[slot];
                if (patron == null || patron.Registry == null || abandoned[slot] != null ||
                    patron.Drinking != null && patron.Drinking.Timeline.Phase != BarPatronDrinkPhase.Rest ||
                    canDepart != null && !canDepart(patron)) continue;
                transit = PrepareTransit(patron, slot, false);
                transit.Phase = patron.Drinking != null ? TransitPhase.ReleasingDrink : TransitPhase.Standing;
                patrons.Remove(patron);
                BuildRoute(patron.Anchor, transit.Dock);
                PresenceChanged?.Invoke();
                return;
            }
        }

        private Transit PrepareTransit(BarPatron patron, int slot, bool carryDrink)
        {
            // The settled animator may have overwritten the last visible
            // hand pose earlier in Update. Ask its existing contact owner
            // to restore the exact typed vessel grip before caching it.
            patron.Drinking?.SampleFinalPose();
            var result = new Transit { Patron = patron, Slot = slot, CarryDrink = carryDrink, Dock = StandingDock(patron.Anchor),
                Seat = patron.Presentation.SeatAnchor };
            CityPedestrianResources.TryGetArchetype(patron.Registry.DesignId, out CityPedestrianArchetype archetype);
            result.Ride = archetype?.SeatedRide;
            for (int index = 0; index < result.Feet.Length; index++)
            {
                string side = index == 0 ? ".L" : ".R";
                var contact = new FootContact
                {
                    Thigh = CityPedestrianHandProps.FindSocket(patron.Registry.ModelRoot, "thigh" + side),
                    Shin = CityPedestrianHandProps.FindSocket(patron.Registry.ModelRoot, "shin" + side),
                    Foot = index == 0 ? patron.Registry.LeftFootAnchor : patron.Registry.RightFootAnchor
                };
                contact.SeatedPosition = contact.Foot.position;
                contact.SeatedRotation = contact.Foot.rotation;
                result.Feet[index] = contact;
            }
            if (patron.Drinking != null)
            {
                result.Vessel = patron.Drinking.BottleRoot;
                result.Hand = CityPedestrianHandProps.FindSocket(patron.Registry.ModelRoot, "hand.R");
                result.UpperArm = CityPedestrianHandProps.FindSocket(patron.Registry.ModelRoot, "upper_arm.R");
                result.Forearm = CityPedestrianHandProps.FindSocket(patron.Registry.ModelRoot, "forearm.R");
                result.Fingers = patron.Registry.GetComponentInChildren<NpcHandPose>();
                if (carryDrink)
                {
                    // An arriving guest retains the existing vessel/grip.
                    result.Vessel.SetParent(result.Hand, true);
                    result.VesselHandPosition = result.Vessel.localPosition;
                    result.VesselHandRotation = result.Vessel.localRotation;
                }
                else
                {
                    // Rest already solved the upright vessel on the real
                    // surface. Reparenting preserves every visible contact
                    // and keeps it alive when the guest leaves the scene.
                    result.Vessel.SetParent(room, true);
                    abandoned[slot] = new BarAbandonedDrink(slot, patron.Anchor,
                        patron.Drinking.DrinkProp, patron.Drinking.SurfaceRenderer);
                    PendingAbandonedCount++;
                }
            }
            result.Pose = new RigPose(patron.Registry.ModelRoot);
            if (patron.Drinking != null) patron.Drinking.enabled = false;
            result.PreviousCulling = patron.Registry.Animator.cullingMode;
            patron.Registry.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            patron.Presentation.ClearSeat();
            patron.Presentation.ClearAttention();
            patron.Presentation.ClearAuthoredAction();
            patron.Presentation.Advance(0f, false, true);
            ApplyCarryPose(result);
            result.Pose.CaptureStanding();
            Vector3 dockOffset = visit.Root.TransformPoint(result.Dock) - patron.Registry.transform.position;
            for (int index = 0; index < result.Feet.Length; index++)
            {
                FootContact foot = result.Feet[index];
                foot.StandingPosition = foot.Foot.position + dockOffset;
                foot.StandingRotation = foot.Foot.rotation;
            }
            if (result.CarryDrink && result.Vessel != null)
                result.Vessel.SetLocalPositionAndRotation(result.VesselHandPosition, result.VesselHandRotation);
            return result;
        }

        private void AdvanceTransit(float dt)
        {
            Transit current = transit;
            if (current.Patron.Registry == null)
            {
                slots[current.Slot] = null;
                patrons.Remove(current.Patron);
                if (current.Seat != null) Destroy(current.Seat.gameObject);
                transit = null;
                NotifyAbandonedReady(current.Slot);
                ClearTransitDiagnostics();
                PresenceChanged?.Invoke();
                return;
            }
            current.Seconds += dt;
            switch (current.Phase)
            {
                case TransitPhase.OpeningForArrival:
                    current.Patron.Presentation.Advance(dt, false);
                    if (doorOpen >= .999f) SetPhase(TransitPhase.Arriving);
                    break;
                case TransitPhase.Arriving:
                    if (MoveToward(route[current.Corner], dt))
                    {
                        if (current.Corner == routeCount - 1 && !TurnAtDock(dt)) break;
                        current.Corner++;
                        if (current.Corner == routeCount) SetPhase(TransitPhase.Sitting);
                    }
                    break;
                case TransitPhase.Sitting:
                    current.Patron.Presentation.Advance(0f, false, true);
                    if (current.Seconds >= SeatTransitionSeconds) CompleteArrival();
                    break;
                case TransitPhase.Standing:
                    current.Patron.Presentation.Advance(0f, false, true);
                    if (current.Seconds >= SeatTransitionSeconds)
                    {
                        current.Patron.Registry.transform.localPosition = current.Dock;
                        current.Pose.Apply(0f);
                        current.Corner = routeCount - 2;
                        SetPhase(TransitPhase.Leaving);
                    }
                    break;
                case TransitPhase.ReleasingDrink:
                    current.Patron.Presentation.Advance(0f, false, true);
                    if (current.Seconds >= ReleaseHandSeconds && current.ReleaseEndpointShown)
                        SetPhase(TransitPhase.Standing);
                    break;
                case TransitPhase.Leaving:
                    if (MoveToward(route[current.Corner], dt))
                    {
                        current.Corner--;
                        if (current.Corner < 0) SetPhase(TransitPhase.OpeningForDeparture);
                    }
                    break;
                case TransitPhase.OpeningForDeparture:
                    current.Patron.Presentation.Advance(dt, false);
                    if (doorOpen >= .999f) SetPhase(TransitPhase.CrossingOut);
                    break;
                case TransitPhase.CrossingOut:
                    if (MoveToward(OutsideDoor, dt)) SetPhase(TransitPhase.ClosingAfterDeparture);
                    break;
                case TransitPhase.ClosingAfterDeparture:
                    current.Patron.Presentation.Advance(dt, false);
                    // The opaque authored leaf is closed before releasing the
                    // body, even when the hero follows it with the camera.
                    if (doorOpen <= .001f) CompleteDeparture();
                    break;
            }
        }

        private void LateUpdate()
        {
            if (transit == null || transit.Patron.Registry == null) return;
            TransitFootContactError = 0f;
            Transit current = transit;
            bool seating = current.Phase == TransitPhase.Sitting;
            bool standing = current.Phase == TransitPhase.Standing;
            bool releasing = current.Phase == TransitPhase.ReleasingDrink;
            if (seating || standing || releasing)
            {
                float progress = Mathf.SmoothStep(0f, 1f, current.Seconds / SeatTransitionSeconds);
                float seated = releasing ? 1f : seating ? progress : 1f - progress;
                Transform body = current.Patron.Registry.transform;
                body.localPosition = Vector3.Lerp(current.Dock, current.Patron.Anchor.Position, seated);
                body.localRotation = Quaternion.Euler(0f, current.Patron.Anchor.YawDegrees, 0f);
                current.Pose.Apply(seated);
                ApplySeatFootContacts(current, seated);
            }
            else ApplyCarryPose(current);
            if (current.Fingers != null && current.Patron.Drinking != null)
            {
                float grip = current.CarryDrink ? 1f : releasing
                    ? 1f - Mathf.SmoothStep(0f, 1f, current.Seconds / ReleaseHandSeconds) : 0f;
                current.Fingers.SetGrip(false, grip, current.Patron.Drinking.GripKind);
                if (releasing && current.Seconds >= ReleaseHandSeconds) current.ReleaseEndpointShown = true;
                if (current.CarryDrink)
                {
                    BarPatronDrinkProp prop = current.Patron.Drinking.DrinkProp;
                    TransitVesselGripError = Vector3.Distance(current.Vessel.position + current.Vessel.rotation * prop.GripLocalPosition,
                        current.Fingers.AnatomicalCylinderCentre(false));
                }
            }
        }

        private void ApplySeatFootContacts(Transit current, float seated)
        {
            Transform actor = current.Patron.Registry.transform;
            for (int index = 0; index < current.Feet.Length; index++)
            {
                FootContact foot = current.Feet[index];
                // One boot steps while the other keeps its contact. The final
                // target comes from the real shared seat sample, including a
                // counter sitter's footrest height and the selected footwear.
                float progress = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(index == 0 ? .08f : .28f, index == 0 ? .48f : .72f, seated));
                Vector3 target = Vector3.Lerp(foot.StandingPosition, foot.SeatedPosition, progress) +
                    actor.up * (.06f * Mathf.Sin(progress * Mathf.PI));
                Quaternion rotation = Quaternion.Slerp(foot.StandingRotation, foot.SeatedRotation, progress);
                LimbTwoBoneIk.Solve(foot.Thigh, foot.Shin, foot.Foot, target, rotation,
                    foot.Thigh.position + actor.forward * .65f, 1f, .995f, true);
                TransitFootContactError = Mathf.Max(TransitFootContactError, Vector3.Distance(foot.Foot.position, target));
            }
        }

        private static void ApplyCarryPose(Transit current)
        {
            if (!current.CarryDrink || current.Hand == null || current.UpperArm == null || current.Forearm == null ||
                current.Vessel == null || current.Patron.Drinking == null) return;
            Transform actor = current.Patron.Registry.transform;
            BarPatronDrinkProp prop = current.Patron.Drinking.DrinkProp;
            Quaternion propRotation = Quaternion.LookRotation(Vector3.Cross(actor.right, actor.up), actor.up) *
                Quaternion.Inverse(Quaternion.LookRotation(Vector3.Cross(prop.HandleLocalAxis, prop.OpeningLocalAxis),
                    prop.OpeningLocalAxis));
            Quaternion handRotation = propRotation * Quaternion.Inverse(current.VesselHandRotation);
            Vector3 grip = actor.position + actor.up * 1.02f + actor.right * .26f + actor.forward * .23f;
            Vector3 root = grip - propRotation * prop.GripLocalPosition;
            Vector3 hand = root - handRotation * Vector3.Scale(current.VesselHandPosition, current.Hand.lossyScale);
            LimbTwoBoneIk.Solve(current.UpperArm, current.Forearm, current.Hand, hand, handRotation,
                current.UpperArm.position + actor.right * .35f - actor.up * .3f, 1f, .995f, true);
        }

        private bool TurnAtDock(float dt)
        {
            Transform actor = transit.Patron.Registry.transform;
            Quaternion facing = visit.Root.rotation * Quaternion.Euler(0f, transit.Patron.Anchor.YawDegrees, 0f);
            actor.rotation = Quaternion.RotateTowards(actor.rotation, facing, TurnSpeed * dt);
            return Quaternion.Angle(actor.rotation, facing) <= .1f;
        }

        private void CompleteArrival()
        {
            Transit current = transit;
            Transform body = current.Patron.Registry.transform;
            body.localPosition = current.Patron.Anchor.Position;
            body.localRotation = Quaternion.Euler(0f, current.Patron.Anchor.YawDegrees, 0f);
            current.Pose.Apply(1f);
            if (current.Seat != null) current.Patron.Presentation.TrySeat(current.Seat, current.Ride);
            current.Patron.Presentation.Advance(0f, false, true);
            if (current.Vessel != null) current.Vessel.SetParent(body, true);
            current.Patron.Registry.Animator.cullingMode = current.PreviousCulling;
            if (current.Patron.Drinking != null)
            {
                current.Patron.Drinking.enabled = true;
                current.Patron.Drinking.SampleFinalPose();
            }
            patrons.Add(current.Patron);
            transit = null;
            ClearTransitDiagnostics();
            nextChange = ChangeIntervalSeconds;
            PresenceChanged?.Invoke();
        }

        private void NotifyAbandonedReady(int slot)
        {
            BarAbandonedDrink drink = abandoned[slot];
            if (drink == null || drink.IsReadyForCollection || drink.IsCleanupComplete) return;
            drink.IsReadyForCollection = true;
            DrinkAbandoned?.Invoke(drink);
        }

        private void CompleteDeparture()
        {
            Transit current = transit;
            slots[current.Slot] = null;
            if (current.Seat != null) Destroy(current.Seat.gameObject);
            Destroy(current.Patron.Registry.gameObject);
            transit = null;
            ClearTransitDiagnostics();
            nextChange = ChangeIntervalSeconds;
            PresenceChanged?.Invoke();
            NotifyAbandonedReady(current.Slot);
        }

        private void SetPhase(TransitPhase phase)
        {
            transit.Phase = phase;
            transit.Seconds = 0f;
        }

        private bool MoveToward(Vector3 localTarget, float dt)
        {
            Transform actor = transit.Patron.Registry.transform;
            Vector3 target = visit.Root.TransformPoint(localTarget);
            TransitTarget = target;
            TransitBlockedBy = string.Empty;
            Vector3 offset = target - actor.position;
            offset.y = 0f;
            float distance = offset.magnitude;
            if (distance <= .015f)
            {
                transit.Patron.Presentation.Advance(dt, false);
                return true;
            }
            Vector3 direction = offset / distance;
            actor.rotation = Quaternion.RotateTowards(actor.rotation, Quaternion.LookRotation(direction, visit.Root.up), TurnSpeed * dt);
            float facing = Mathf.Clamp01(1f - Vector3.Angle(actor.forward, direction) / 70f);
            float travel = Mathf.Min(distance, WalkingSpeed * dt * facing);
            bool blocked = IsPathBlocked(actor.position, direction, travel);
            if (!blocked) actor.position += direction * travel;
            transit.Patron.Presentation.Advance(dt, !blocked && travel > .0001f);
            return false;
        }

        private bool IsPathBlocked(Vector3 origin, Vector3 direction, float distance)
        {
            if (distance <= .00001f) return false;
            if (doorPhysicsDirty)
            {
                // The project deliberately disables AutoSyncTransforms. Flush
                // the owned hinge once before querying its moved leaf, also
                // when a focused fixture drives several steps in one frame.
                Physics.SyncTransforms();
                doorPhysicsDirty = false;
            }
            int count;
            do
            {
                count = Physics.CapsuleCastNonAlloc(origin + Vector3.up * .36f,
                    origin + Vector3.up * 1.35f, BodyRadius, direction, movementHits,
                    distance + .025f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                if (count < movementHits.Length) break;
                if (movementHits.Length >= 64)
                    return HasBlockingHit(Physics.CapsuleCastAll(origin + Vector3.up * .36f,
                        origin + Vector3.up * 1.35f, BodyRadius, direction, distance + .025f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), -1);
                Array.Resize(ref movementHits, movementHits.Length * 2);
            } while (true);
            return HasBlockingHit(movementHits, count);
        }

        private bool HasBlockingHit(RaycastHit[] hits, int count)
        {
            if (count < 0) count = hits.Length;
            for (int index = 0; index < count; index++)
            {
                Collider hit = hits[index].collider;
                if (hit == null || hit.transform.IsChildOf(transit.Patron.Registry.transform)) continue;
                TransitBlockedBy = hit.name;
                return true;
            }
            return false;
        }

        private static readonly Vector3 OutsideDoor = new Vector3(0f, 0f, -5.05f);

        private Vector3 StandingDock(BarNpcAnchor anchor)
        {
            if (anchor.Role == BarNpcRole.CounterPatron)
                return new Vector3(anchor.Position.x, 0f, BarCounterSeatPlanner.EntryZ);
            if (anchor.Role == BarNpcRole.SeatedPatron)
                return new Vector3(-3.03f, 0f, anchor.Position.z);
            return new Vector3(anchor.Position.x, anchor.Position.y, Mathf.Max(-3.34f, anchor.Position.z));
        }

        private void BuildRoute(BarNpcAnchor anchor, Vector3 dock)
        {
            routeCount = 0;
            route[routeCount++] = new Vector3(0f, 0f, -3.10f);
            if (anchor.Role == BarNpcRole.SeatedPatron)
            {
                route[routeCount++] = new Vector3(0f, 0f, -1.35f);
                route[routeCount++] = new Vector3(-2.75f, 0f, -1.35f);
                route[routeCount++] = new Vector3(-2.75f, 0f, dock.z);
            }
            else if (anchor.Role == BarNpcRole.CounterPatron)
            {
                route[routeCount++] = new Vector3(0f, 0f, -.75f);
                route[routeCount++] = new Vector3(dock.x, 0f, BarCounterSeatPlanner.ApproachZ);
            }
            else
                route[routeCount++] = new Vector3(0f, 0f, dock.z);
            route[routeCount++] = dock;
        }

        private void CreateEntranceHinge()
        {
            Transform leaf = room.Find("Entrance Door");
            Renderer renderer = leaf != null ? leaf.GetComponent<Renderer>() : null;
            if (renderer == null) throw new InvalidOperationException("A bar crowd requires its authored entrance door.");
            Bounds bounds = renderer.bounds;
            door = new GameObject("Bar Entrance Door Hinge").transform;
            door.SetParent(room, false);
            door.position = new Vector3(bounds.min.x, bounds.min.y, bounds.center.z);
            closedDoorRotation = door.localRotation;
            string[] parts = { "Entrance Door", "Entrance Door Panels", "Entrance Door Transom Glass", "Entrance Door Furniture" };
            for (int index = 0; index < parts.Length; index++)
            {
                Transform part = room.Find(parts[index]);
                if (part != null) part.SetParent(door, true);
                Transform collision = room.Find(parts[index] + " Collision");
                if (collision != null) collision.SetParent(door, true);
            }
        }

        private void UpdateDoor(float dt)
        {
            bool wantsOpen = transit != null && (transit.Phase == TransitPhase.OpeningForArrival ||
                transit.Phase == TransitPhase.OpeningForDeparture || transit.Phase == TransitPhase.CrossingOut ||
                transit.Phase == TransitPhase.Arriving && transit.Patron.Registry.transform.localPosition.z < -2.8f);
            float previous = doorOpen;
            doorOpen = Mathf.MoveTowards(doorOpen, wantsOpen ? 1f : 0f, dt / DoorSeconds);
            if (door != null) door.localRotation = closedDoorRotation * Quaternion.Euler(0f, 95f * Mathf.SmoothStep(0f, 1f, doorOpen), 0f);
            doorPhysicsDirty |= previous != doorOpen;
        }

        private void ClearTransitDiagnostics()
        {
            TransitTarget = Vector3.zero;
            TransitBlockedBy = string.Empty;
            TransitFootContactError = TransitVesselGripError = 0f;
        }

        private uint NextRandom()
        {
            random ^= random << 13;
            random ^= random >> 17;
            random ^= random << 5;
            return random;
        }

        private void OnDestroy()
        {
            IsInitialized = false;
            PresenceChanged = null;
            DrinkAbandoned = null;
            patrons.Clear();
            if (abandoned != null)
                for (int slot = 0; slot < abandoned.Length; slot++)
                {
                    BarAbandonedDrink drink = abandoned[slot];
                    if (drink == null) continue;
                    drink.IsCleanupComplete = true;
                    if (drink.DrinkProp.Root != null) Destroy(drink.DrinkProp.Root.gameObject);
                    abandoned[slot] = null;
                }
            PendingAbandonedCount = 0;
            if (visit != null && visit.Root != null) Destroy(visit.Root.gameObject);
            if (door == null) return;
            door.localRotation = closedDoorRotation;
            while (door.childCount > 0) door.GetChild(0).SetParent(room, true);
            Destroy(door.gameObject);
        }
    }
}
