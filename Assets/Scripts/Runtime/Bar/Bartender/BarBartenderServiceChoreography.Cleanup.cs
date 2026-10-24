using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class BarBartenderServiceChoreography
    {
        private enum CleanupPhase
        {
            None, StowTowel, WalkToDrink, Reach, Lift,
            ReturnBehindCounter, Lower, ReturnForService
        }

        private const int CleanupCapacity = BarBusinessHours.MaximumGuests;
        private const float CleanupBodyRadius = .16f;
        private const float CleanupReachSeconds = .75f;
        private const float CleanupLiftSeconds = .9f;
        private readonly BarAbandonedDrink[] cleanupQueue = new BarAbandonedDrink[CleanupCapacity];
        private readonly Vector3[] cleanupRoute = new Vector3[8];
        private RaycastHit[] cleanupHits = new RaycastHit[16];
        private BarCrowdController cleanupCrowd;
        private BarAbandonedDrink cleanupDrink;
        private CleanupPhase cleanupPhase;
        private int cleanupCount;
        private int cleanupRouteCount;
        private int cleanupRouteIndex;
        private int cleanupLastReached;
        private bool cleanupSubscribed;
        private bool cleanupAttached;
        private bool cleanupLastHidden;
        private float cleanupElapsed;
        private float cleanupWalkElapsed;
        private Vector3 cleanupHandStart;
        private Vector3 cleanupHandTarget;
        private Vector3 cleanupLiftStart;
        private Vector3 cleanupLowerStart;
        private Quaternion cleanupPickupFacing;
        private Quaternion cleanupPropInActor;
        private Quaternion cleanupLowerRotation;
        private Pose cleanupRestPose;
        private Transform cleanupRestParent;
        private Bounds cleanupCounter;
        private Bounds cleanupCounterBody;
        private Camera cleanupCamera;
        private float cleanupGripError;
        private string cleanupBlockedBy = string.Empty;

        public bool IsCleaningPatronDrink => cleanupPhase != CleanupPhase.None;
        public int PendingCleanupCount => cleanupCount;
        public int CompletedCleanupCount { get; private set; }
        public string CleanupPhaseName => cleanupPhase.ToString();
        public BarAbandonedDrink ActiveCleanupDrink => cleanupDrink;
        public bool CleanupVesselAttached => cleanupAttached;
        public bool IsCleanupVesselHidden => cleanupLastHidden;
        public float CleanupGripError => cleanupGripError;
        public string CleanupBlockedBy => cleanupBlockedBy;
        public bool IsReadyForForegroundService => !IsCleaningPatronDrink &&
            (presentation == null || presentation.LowReachLowering <= .002f);

        /// <summary>The crowd retains dirty docks; this single bounded FIFO owns collection.</summary>
        public void ConfigureCleanup(BarCrowdController crowd)
        {
            if (!isInitialized) throw new InvalidOperationException("Initialize bartender choreography before cleanup.");
            if (crowd == null) throw new ArgumentNullException(nameof(crowd));
            if (cleanupCrowd != null && cleanupCrowd != crowd)
                throw new InvalidOperationException("A collector cannot change rooms while owning abandoned drinks.");
            cleanupCrowd = crowd;
            shop.ConfigureBartenderAvailability(this, () => IsReadyForForegroundService);
            cleanupCounter = ToCleanupSpace(shop.ServiceView.CounterSurfaceWorldBounds);
            CacheCleanupCounterBody();
            ConnectCleanup();
        }

        private void OnEnable() => ConnectCleanup();

        private void ConnectCleanup()
        {
            if (cleanupCrowd == null || !isActiveAndEnabled) return;
            if (!cleanupSubscribed)
            {
                cleanupCrowd.DrinkAbandoned += EnqueueCleanup;
                cleanupSubscribed = true;
            }
            for (int index = 0; index < cleanupCrowd.PendingAbandonedCount; index++)
                EnqueueCleanup(cleanupCrowd.GetPendingAbandonedDrink(index));
        }

        private void DisconnectCleanup()
        {
            if (cleanupSubscribed && cleanupCrowd != null)
                cleanupCrowd.DrinkAbandoned -= EnqueueCleanup;
            cleanupSubscribed = false;
        }

        private void EnqueueCleanup(BarAbandonedDrink drink)
        {
            if (drink == null || !drink.IsReadyForCollection || drink.IsCleanupComplete) return;
            for (int index = 0; index < cleanupCount; index++)
                if (ReferenceEquals(cleanupQueue[index], drink)) return;
            if (cleanupCount == cleanupQueue.Length)
                throw new InvalidOperationException("Abandoned drinks exceeded the bar's fixed visit slots.");
            cleanupQueue[cleanupCount++] = drink;
        }

        private bool AdvanceCleanup(float deltaTime)
        {
            if (cleanupCrowd == null) return false;
            bool foreground = shop.WantsBartenderService || shop.IsReturningCounterMenuHome;
            if (!IsCleaningPatronDrink)
            {
                while (cleanupCount > 0 && (cleanupQueue[0].IsCleanupComplete ||
                    cleanupQueue[0].DrinkProp.Root == null))
                {
                    if (!cleanupQueue[0].IsCleanupComplete)
                        cleanupCrowd.CompleteDrinkCleanup(cleanupQueue[0]);
                    RemoveCleanupHead();
                }
                if (cleanupCount == 0 || foreground) return false;
                BeginCleanup(cleanupQueue[0]);
            }

            // The same actor root has one movement owner. An accepted order may
            // interrupt the outward trip, but a held glass first returns safely.
            if (foreground && !cleanupAttached && cleanupPhase != CleanupPhase.ReturnForService)
            {
                cleanupRouteIndex = cleanupLastReached;
                SetCleanupPhase(CleanupPhase.ReturnForService);
                ReleaseAll();
            }
            deltaTime = Mathf.Min(deltaTime, .1f);
            cleanupElapsed += deltaTime;
            cleanupBlockedBy = string.Empty;
            presentation.RequestTowelService(true);
            presentation.SetCounterHandSupport(Vector3.zero, 0f);
            shop.ReportCounterServerAtTarget(false);
            shop.ReportBeerServerAtGuest(false);

            switch (cleanupPhase)
            {
                case CleanupPhase.StowTowel:
                    ReleaseAll();
                    presentation.ApplyCounterTurnPose(0f);
                    if (presentation.IsTowelServiceReady) SetCleanupPhase(CleanupPhase.WalkToDrink);
                    break;
                case CleanupPhase.WalkToDrink:
                    if (MoveCleanupOutward(deltaTime))
                    {
                        cleanupHandStart = presentation.Registry.VesselGripAnchor.position;
                        SetCleanupPhase(CleanupPhase.Reach);
                    }
                    break;
                case CleanupPhase.Reach:
                    presentation.ApplyCounterTurnPose(0f);
                    SetCleanupReach(Vector3.Lerp(cleanupHandStart, CleanupRestGrip,
                        Mathf.SmoothStep(0f, 1f, cleanupElapsed / CleanupReachSeconds)), true);
                    break;
                case CleanupPhase.Lift:
                    presentation.ApplyCounterTurnPose(0f, true);
                    // Lift above the support before retracting horizontally.
                    Vector3 carry = CleanupCarryGrip;
                    Vector3 clear = cleanupLiftStart;
                    clear.y = Mathf.Max(clear.y, carry.y);
                    float progress = Mathf.Clamp01(cleanupElapsed / CleanupLiftSeconds);
                    SetCleanupReach(progress < .5f
                        ? Vector3.Lerp(cleanupLiftStart, clear, Mathf.SmoothStep(0f, 1f, progress * 2f))
                        : Vector3.Lerp(clear, carry, Mathf.SmoothStep(0f, 1f, progress * 2f - 1f)), false);
                    if (progress >= 1f && cleanupGripError < .015f)
                    {
                        cleanupRouteIndex = cleanupRouteCount - 2;
                        SetCleanupPhase(CleanupPhase.ReturnBehindCounter);
                    }
                    break;
                case CleanupPhase.ReturnBehindCounter:
                    if (MoveCleanupHome(deltaTime, true))
                    {
                        cleanupLowerStart = cleanupDrink.DrinkProp.Root.position;
                        cleanupLowerRotation = cleanupDrink.DrinkProp.Root.rotation;
                        SetCleanupPhase(CleanupPhase.Lower);
                    }
                    SetCleanupReach(CleanupCarryGrip, false);
                    break;
                case CleanupPhase.Lower:
                    presentation.ApplyCounterTurnPose(0f, true);
                    BarPatronDrinkProp prop = cleanupDrink.DrinkProp;
                    Vector3 lower = cleanupLowerStart;
                    float topOffset = CleanupTopOffset(prop, cleanupLowerRotation);
                    lower.y = CleanupWorldPoint(new Vector3(0f, cleanupCounter.min.y - .16f, 0f)).y - topOffset;
                    Vector3 root = Vector3.Lerp(cleanupLowerStart, lower,
                        Mathf.SmoothStep(0f, 1f, cleanupElapsed / 1.1f));
                    SetCleanupReach(root + cleanupLowerRotation * prop.GripLocalPosition, true);
                    break;
                case CleanupPhase.ReturnForService:
                    ReleaseAll();
                    if (MoveCleanupHome(deltaTime, false)) EndCleanup(false);
                    break;
            }
            return true;
        }

        private void BeginCleanup(BarAbandonedDrink drink)
        {
            cleanupDrink = drink;
            cleanupAttached = false;
            cleanupLastHidden = false;
            cleanupGripError = float.PositiveInfinity;
            Transform root = drink.DrinkProp.Root;
            cleanupRestParent = root.parent;
            cleanupRestPose = new Pose(root.position, root.rotation);
            cleanupPropInActor = Quaternion.Inverse(transform.rotation) * root.rotation;
            cleanupCounter = ToCleanupSpace(shop.ServiceView.CounterSurfaceWorldBounds);
            BuildCleanupRoute();
            cleanupRouteIndex = 0;
            cleanupLastReached = -1;
            cleanupWalkElapsed = 0f;
            changingWipePatch = false;
            ReleaseAll();
            SetCleanupPhase(CleanupPhase.StowTowel);
        }

        private void SetCleanupPhase(CleanupPhase value)
        {
            cleanupPhase = value;
            cleanupElapsed = 0f;
        }

        private Vector3 CleanupRestGrip => cleanupRestPose.position +
            cleanupRestPose.rotation * cleanupDrink.DrinkProp.GripLocalPosition;

        private Vector3 CleanupCarryGrip
        {
            get
            {
                Vector3 target = transform.position - transform.right * .32f + transform.forward * .25f;
                BarPatronDrinkProp prop = cleanupDrink.DrinkProp;
                Quaternion rotation = transform.rotation * cleanupPropInActor;
                float bottomFromGrip = (rotation * (prop.GripLocalPosition - prop.BaseLocalPosition)).y;
                target.y = Mathf.Max(presentation.Registry.LeftUpperArm.position.y - .28f,
                    shop.ServiceView.CounterSurfaceWorldBounds.max.y + .08f + bottomFromGrip);
                return target;
            }
        }

        private void SetCleanupReach(Vector3 target, bool lean)
        {
            cleanupHandTarget = target;
            BarPatronDrinkProp prop = cleanupDrink.DrinkProp;
            Quaternion rotation = CleanupPropRotation;
            presentation.SetCounterReachLean(target, lean ? 1f : 0f);
            presentation.SetLowReachStance(lean ? .12f : 0f);
            presentation.SetChainTarget(VesselChainIndex, target, 1f);
            presentation.SetChainTarget(BottleChainIndex, Vector3.zero, 0f);
            presentation.SetChainGripFrame(VesselChainIndex, rotation * prop.OpeningLocalAxis,
                CleanupPalm(rotation), prop.GripKind);
            presentation.SetChainElbowDirection(VesselChainIndex, -transform.right);
        }

        private Quaternion CleanupPropRotation => cleanupPhase == CleanupPhase.Lower
            ? cleanupLowerRotation : cleanupAttached
                ? transform.rotation * cleanupPropInActor : cleanupRestPose.rotation;

        private Vector3 CleanupPalm(Quaternion rotation)
        {
            BarPatronDrinkProp prop = cleanupDrink.DrinkProp;
            if (prop.Kind == BarPatronDrinkPropKind.Pint) return -(rotation * prop.HandleLocalAxis);
            Vector3 radial = Vector3.ProjectOnPlane(cleanupHandTarget - presentation.Registry.LeftUpperArm.position,
                rotation * prop.OpeningLocalAxis);
            return radial.sqrMagnitude > .000001f ? radial.normalized : transform.forward;
        }

        /// <summary>Runs after ordinary animation/IK; never moves an ungripped supported prop.</summary>
        public void RefreshCleanupContact()
        {
            if (!gameObject.activeInHierarchy || !IsCleaningPatronDrink || cleanupDrink == null ||
                cleanupDrink.DrinkProp.Root == null || GameTimeScaleRuntime.IsPaused) return;
            // A disabled motion owner keeps a held prop seated on its frozen
            // target, but cannot acquire a new grip or complete a collection.
            if (!enabled && !cleanupAttached) return;
            if (cleanupPhase != CleanupPhase.Reach && !cleanupAttached) return;
            BarPatronDrinkProp prop = cleanupDrink.DrinkProp;
            Quaternion rotation = CleanupPropRotation;
            presentation.ResolveAnatomicalGripContactAfterWristRoll(VesselChainIndex,
                cleanupHandTarget, rotation * prop.OpeningLocalAxis, CleanupPalm(rotation), prop.GripKind);
            Vector3 hand = presentation.Registry.VesselGripAnchor.position;
            if (!cleanupAttached)
            {
                cleanupGripError = Vector3.Distance(hand, CleanupRestGrip);
                if (cleanupElapsed < CleanupReachSeconds || presentation.GetChainWeight(VesselChainIndex) < .95f ||
                    cleanupGripError > .012f) return;
                cleanupAttached = true;
                cleanupPropInActor = Quaternion.Inverse(transform.rotation) * cleanupRestPose.rotation;
                cleanupLiftStart = CleanupRestGrip;
                SetCleanupPhase(CleanupPhase.Lift);
            }
            prop.Root.SetPositionAndRotation(hand - rotation * prop.GripLocalPosition, rotation);
            cleanupGripError = Vector3.Distance(presentation.Registry.VesselGripAnchor.position,
                prop.Root.position + prop.Root.rotation * prop.GripLocalPosition);
            if (enabled && cleanupPhase == CleanupPhase.Lower && cleanupElapsed >= 1.1f && IsCleanupOccluded())
            {
                cleanupLastHidden = true;
                prop.Root.gameObject.SetActive(false);
                if (!cleanupCrowd.CompleteDrinkCleanup(cleanupDrink))
                    throw new InvalidOperationException("The collector lost its dirty-dock reservation.");
                Destroy(prop.Root.gameObject);
                CompletedCleanupCount++;
                RemoveCleanupHead();
                EndCleanup(true);
            }
        }

        private void BuildCleanupRoute()
        {
            Bounds surface = ToCleanupSpace(cleanupDrink.SurfaceRenderer.bounds);
            Vector3 grip = CleanupLocalPoint(CleanupRestGrip);
            Vector3 shoulder = menuShoulderInActor;
            Vector3 dock;
            Vector3 facing;
            BarNpcRole role = cleanupDrink.Anchor.Role;
            if (role == BarNpcRole.SeatedPatron)
            {
                dock = new Vector3(surface.max.x + CleanupBodyRadius + .055f, homeLocalPosition.y,
                    Mathf.Clamp(grip.z + shoulder.x, surface.min.z + .08f, surface.max.z - .08f));
                facing = Vector3.left;
            }
            else if (role == BarNpcRole.StandingPatron)
            {
                bool left = surface.center.x < 0f;
                dock = new Vector3(left ? surface.max.x + CleanupBodyRadius + .06f : surface.min.x - CleanupBodyRadius - .06f,
                    homeLocalPosition.y, grip.z + (left ? shoulder.x : -shoulder.x));
                facing = left ? Vector3.left : Vector3.right;
            }
            else
            {
                dock = new Vector3(ResolveCleanupStoolGap(grip.x - shoulder.x), homeLocalPosition.y,
                    cleanupCounterBody.min.z - CleanupBodyRadius - .03f);
                facing = Vector3.forward;
            }
            cleanupPickupFacing = Quaternion.LookRotation(CleanupWorldDirection(facing), Vector3.up);
            cleanupRouteCount = 0;
            float leftExit = cleanupCounter.min.x - .55f;
            AddCleanupRoute(new Vector3(leftExit, homeLocalPosition.y,
                Mathf.Max(homeLocalPosition.z, cleanupCounter.max.z + .38f)));
            AddCleanupRoute(new Vector3(leftExit, homeLocalPosition.y, cleanupCounter.min.z - .55f));
            if (role == BarNpcRole.SeatedPatron)
            {
                AddCleanupRoute(new Vector3(leftExit, homeLocalPosition.y, dock.z));
            }
            else
            {
                // The front cross-aisle passes between the fixed counter stools
                // and pub tables. Approach the table from the central free aisle.
                AddCleanupRoute(new Vector3(leftExit, homeLocalPosition.y, -.75f));
                AddCleanupRoute(new Vector3(dock.x, homeLocalPosition.y, -.75f));
            }
            AddCleanupRoute(dock);
        }

        private void AddCleanupRoute(Vector3 local) => cleanupRoute[cleanupRouteCount++] = CleanupWorldPoint(local);

        private void CacheCleanupCounterBody()
        {
            cleanupCounterBody = cleanupCounter;
            // Authored room parts are flattened under the same parent as its
            // measured counter slab; no generated primitive or guessed body.
            Transform parts = shop.ServiceView.transform.parent;
            if (cleanupCrowd != null)
            {
                // The service root may be nested; the actual room is found from
                // the immutable, directly named counter part once at binding.
                Transform owner = transform.parent;
                for (int child = 0; owner != null && child < owner.childCount; child++)
                {
                    Transform candidate = owner.GetChild(child);
                    Transform body = candidate.Find("Bar Counter");
                    if (body == null) continue;
                    parts = candidate;
                    break;
                }
            }
            Transform counter = parts != null ? parts.Find("Bar Counter") : null;
            Renderer renderer = counter != null ? counter.GetComponent<Renderer>() : null;
            if (renderer != null) cleanupCounterBody = ToCleanupSpace(renderer.bounds);
        }

        private float ResolveCleanupStoolGap(float preferred)
        {
            Transform parts = cleanupDrink.SurfaceRenderer.transform.parent;
            Bounds target = default;
            Bounds right = default;
            float targetDistance = float.PositiveInfinity;
            bool foundTarget = false;
            // Each imported stool seat has one measured renderer. Pick the
            // departed guest's seat, then the actual gap to its right neighbor.
            for (int child = 0; child < parts.childCount; child++)
            {
                Transform part = parts.GetChild(child);
                if (part.name != "Bar Drink Service Stool" &&
                    (!part.name.StartsWith("Bar Stool ", StringComparison.Ordinal) || part.name.EndsWith(" Leg", StringComparison.Ordinal))) continue;
                Renderer seat = part.GetComponent<Renderer>();
                if (seat == null) continue;
                Bounds measured = ToCleanupSpace(seat.bounds);
                float distance = Mathf.Abs(measured.center.x - cleanupDrink.Anchor.Position.x);
                if (distance >= targetDistance) continue;
                targetDistance = distance;
                target = measured;
                foundTarget = true;
            }
            if (!foundTarget) return preferred;
            float nearestRight = float.PositiveInfinity;
            for (int child = 0; child < parts.childCount; child++)
            {
                Transform part = parts.GetChild(child);
                if (part.name != "Bar Drink Service Stool" &&
                    (!part.name.StartsWith("Bar Stool ", StringComparison.Ordinal) || part.name.EndsWith(" Leg", StringComparison.Ordinal))) continue;
                Renderer seat = part.GetComponent<Renderer>();
                if (seat == null) continue;
                Bounds measured = ToCleanupSpace(seat.bounds);
                if (measured.min.x <= target.max.x || measured.min.x >= nearestRight) continue;
                nearestRight = measured.min.x;
                right = measured;
            }
            return float.IsFinite(nearestRight) && right.min.x - target.max.x > CleanupBodyRadius * 2f + .02f
                ? (target.max.x + right.min.x) * .5f : preferred;
        }

        private bool MoveCleanupOutward(float dt)
        {
            ReleaseAll();
            if (cleanupRouteIndex < cleanupRouteCount)
            {
                if (!MoveCleanupBody(cleanupRoute[cleanupRouteIndex], dt, false)) return false;
                cleanupLastReached = cleanupRouteIndex++;
            }
            if (cleanupRouteIndex < cleanupRouteCount) return false;
            return TurnCleanupBody(cleanupPickupFacing, dt, false);
        }

        private bool MoveCleanupHome(float dt, bool carried)
        {
            if (cleanupRouteIndex >= 0)
            {
                if (!MoveCleanupBody(cleanupRoute[cleanupRouteIndex], dt, carried)) return false;
                cleanupRouteIndex--;
                return false;
            }
            if (!MoveCleanupBody(CleanupWorldPoint(homeLocalPosition), dt, carried)) return false;
            return TurnCleanupBody(CleanupWorldRotation(homeLocalRotation), dt, carried);
        }

        private bool MoveCleanupBody(Vector3 target, float dt, bool carried)
        {
            if (presentation.LowReachLowering > .002f)
            {
                presentation.ApplyCounterTurnPose(0f, carried);
                return false;
            }
            Vector3 delta = target - transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude <= .000025f) return true;
            Quaternion facing = Quaternion.LookRotation(delta.normalized, Vector3.up);
            if (!TurnCleanupBody(facing, dt, carried)) return false;
            float step = Mathf.Min(CounterTravelSpeed * dt, delta.magnitude);
            if (!CleanupPathClear(delta.normalized, step))
            {
                presentation.ApplyCounterTurnPose(0f, carried);
                return false;
            }
            transform.position += delta.normalized * step;
            cleanupWalkElapsed += dt;
            presentation.ApplyCounterTravelPose(cleanupWalkElapsed, carried);
            return step + .0001f >= delta.magnitude;
        }

        private bool TurnCleanupBody(Quaternion facing, float dt, bool carried)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, facing, CounterTurnSpeedDegrees * dt);
            presentation.ApplyCounterTurnPose(0f, carried);
            return Quaternion.Angle(transform.rotation, facing) <= .1f;
        }

        private bool CleanupPathClear(Vector3 direction, float distance)
        {
            Physics.SyncTransforms();
            Vector3 bottom = transform.position + Vector3.up * .38f;
            Vector3 top = transform.position + Vector3.up * 1.30f;
            int count;
            while (true)
            {
                count = Physics.CapsuleCastNonAlloc(bottom, top, CleanupBodyRadius, direction,
                    cleanupHits, distance + .015f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                if (count < cleanupHits.Length) break;
                if (cleanupHits.Length < 64)
                {
                    Array.Resize(ref cleanupHits, cleanupHits.Length * 2);
                    continue;
                }
                // Rare saturated scenes use a correct cold fallback; no blocker
                // silently disappears from a full NonAlloc result.
                RaycastHit[] all = Physics.CapsuleCastAll(bottom, top, CleanupBodyRadius, direction,
                    distance + .015f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                for (int index = 0; index < all.Length; index++)
                    if (IsCleanupBlocker(all[index].collider)) return false;
                return true;
            }
            for (int index = 0; index < count; index++)
                if (IsCleanupBlocker(cleanupHits[index].collider)) return false;
            return true;
        }

        private bool IsCleanupBlocker(Collider hit)
        {
            if (hit == null || hit.transform.IsChildOf(transform)) return false;
            if (cleanupDrink?.DrinkProp.Root != null && hit.transform.IsChildOf(cleanupDrink.DrinkProp.Root)) return false;
            cleanupBlockedBy = hit.name;
            return true;
        }

        private bool IsCleanupOccluded()
        {
            if (cleanupCamera == null || !cleanupCamera.isActiveAndEnabled) cleanupCamera = Camera.main;
            if (cleanupCamera == null) return false;
            BarPatronDrinkProp prop = cleanupDrink.DrinkProp;
            Bounds bounds = prop.PhysicalLocalBounds;
            // The actual opaque counter volume, below its measured slab.
            Bounds opaque = cleanupCounterBody;
            opaque.SetMinMax(new Vector3(opaque.min.x, homeLocalPosition.y, opaque.min.z),
                new Vector3(opaque.max.x, Mathf.Min(opaque.max.y, cleanupCounter.min.y), opaque.max.z));
            Vector3 eye = CleanupLocalPoint(cleanupCamera.transform.position);
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 sign = new Vector3((corner & 1) == 0 ? -1f : 1f,
                    (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                Vector3 point = CleanupLocalPoint(prop.Root.position + prop.Root.rotation *
                    (bounds.center + Vector3.Scale(bounds.extents, sign)));
                if (point.y >= cleanupCounter.min.y - .01f || point.z <= cleanupCounterBody.max.z) return false;
                Vector3 ray = point - eye;
                if (!opaque.IntersectRay(new Ray(eye, ray.normalized), out float entry) || entry >= ray.magnitude - .01f)
                    return false;
            }
            return true;
        }

        private static float CleanupTopOffset(BarPatronDrinkProp prop, Quaternion rotation)
        {
            Bounds bounds = prop.PhysicalLocalBounds;
            float top = float.NegativeInfinity;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 sign = new Vector3((corner & 1) == 0 ? -1f : 1f,
                    (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                top = Mathf.Max(top, (rotation * (bounds.center + Vector3.Scale(bounds.extents, sign))).y);
            }
            return top;
        }

        private Bounds ToCleanupSpace(Bounds world)
        {
            Bounds result = new Bounds(CleanupLocalPoint(world.center), Vector3.zero);
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 sign = new Vector3((corner & 1) == 0 ? -1f : 1f,
                    (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                result.Encapsulate(CleanupLocalPoint(world.center + Vector3.Scale(world.extents, sign)));
            }
            return result;
        }

        private Vector3 CleanupLocalPoint(Vector3 point) => transform.parent != null
            ? transform.parent.InverseTransformPoint(point) : point;
        private Vector3 CleanupWorldPoint(Vector3 point) => transform.parent != null
            ? transform.parent.TransformPoint(point) : point;
        private Vector3 CleanupWorldDirection(Vector3 direction) => transform.parent != null
            ? transform.parent.TransformDirection(direction) : direction;
        private Quaternion CleanupWorldRotation(Quaternion rotation) => transform.parent != null
            ? transform.parent.rotation * rotation : rotation;

        private void RemoveCleanupHead()
        {
            for (int index = 1; index < cleanupCount; index++) cleanupQueue[index - 1] = cleanupQueue[index];
            if (cleanupCount > 0) cleanupQueue[--cleanupCount] = null;
        }

        private void EndCleanup(bool completed)
        {
            cleanupAttached = false;
            cleanupDrink = null;
            cleanupPhase = CleanupPhase.None;
            cleanupElapsed = 0f;
            cleanupBlockedBy = string.Empty;
            wipePatchTargetLocalPosition = homeLocalPosition;
            ReleaseAll();
            shop.ReportCounterServerAtTarget(true);
            if (!completed) cleanupLastHidden = false;
        }

        private void OnDestroy()
        {
            DisconnectCleanup();
            if (shop != null) shop.ConfigureBartenderAvailability(this, null);
            if (presentation != null) presentation.SetLowReachStance(0f);
            if (cleanupAttached && cleanupDrink != null && cleanupDrink.DrinkProp.Root != null &&
                !cleanupDrink.IsCleanupComplete)
            {
                // Component replacement restores the same supported vessel;
                // ordinary collection never teleports or releases a dirty dock.
                Transform root = cleanupDrink.DrinkProp.Root;
                root.SetParent(cleanupRestParent, true);
                root.SetPositionAndRotation(cleanupRestPose.position, cleanupRestPose.rotation);
            }
        }
    }
}
