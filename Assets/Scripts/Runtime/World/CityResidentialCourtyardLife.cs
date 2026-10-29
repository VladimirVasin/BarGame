using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One silent ordinary resident moves one real basket between
    /// the two open shelter shelves. The body and basket stay physical when
    /// distant; only rig work and renderers scale with presentation distance.</summary>
    [DefaultExecutionOrder(215)]
    [DisallowMultipleComponent]
    public sealed class CityResidentialCourtyardLife : MonoBehaviour
    {
        public const float CycleSeconds = 60f;
        public const float PresentationEnterDistance = 64f;
        public const float PresentationExitDistance = 72f;
        public CityResidentialCourtyardPlan Plan { get; private set; }
        public CityResidentialCourtyardDescriptor Court { get; private set; }
        public VillageResidentPresentation Worker { get; private set; }
        public Transform Basket { get; private set; }
        public Transform PresentationObserver { get; set; }
        public bool ForcePresentation { get; set; }
        public bool AutoAdvance { get; set; } = true;
        public bool IsPresentationActive { get; private set; } = true;
        public double ElapsedSeconds { get; private set; }
        public bool HandsMatch { get; private set; }
        public float HandContactError { get; private set; }
        public float ContactWeight { get; private set; }
        public bool IsBlocked { get; private set; }
        private Transform spine, leftHandle, rightHandle;
        private Renderer[] renderers;
        private bool[] rendererVisibility;
        private bool visibilityRefresh;
        private SkinnedMeshRenderer[] leftSoles, rightSoles;
        private CapsuleCollider body;
        private BoxCollider basketBody;
        private readonly Collider[] overlaps = new Collider[16];
        private readonly RaycastHit[] hits = new RaycastHit[16];
        private Quaternion shelfRotation;
        private double lastCalendarSeconds, phaseDelay, lastPoseSeconds = double.NegativeInfinity;
        private bool initialized;

        public void Initialize(CityResidentialCourtyardPlan plan, Transform modelRoot)
        {
            if (initialized) throw new InvalidOperationException("Courtyard life is already initialized.");
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            if (modelRoot == null) throw new ArgumentNullException(nameof(modelRoot));
            foreach (CityResidentialCourtyardDescriptor candidate in plan.Courts)
                if (candidate.CarryRoute.Count >= 2) { Court = candidate; break; }
            if (Court == null) { enabled = false; return; }
            shelfRotation = Court.ServiceProp.Rotation;
            GameObject basket = CityResidentialCourtyardAssetProvider.CreateBasket(transform,
                Court.BasketRestPosition, shelfRotation);
            Basket = basket.transform;
            Basket.name = "MOVE_ServiceBasket";
            leftHandle = CityResidentialCourtyardAssetProvider.FindPart(Basket, "ANCHOR_BasketLeft");
            rightHandle = CityResidentialCourtyardAssetProvider.FindPart(Basket, "ANCHOR_BasketRight");
            basketBody = Basket.gameObject.AddComponent<BoxCollider>();
            basketBody.center = new Vector3(0f, .14f, 0f);
            basketBody.size = new Vector3(.56f, .28f, .40f);
            Worker = DefaultNpcFactory.CreateForCharacter(transform,
                DefaultNpcPopulation.CityResidentialCourtyardCarrier);
            Worker.name = "Silent Courtyard Carrier";
            CityPortCrew.AlignWorkerModelWithPlacement(Worker);
            // This scene adds no independent voice, sound or footstep source.
            NpcFootstepSources.Unregister(Worker.transform);
            spine = CityPedestrianHandProps.FindSocket(Worker.ModelRoot, "spine");
            if (spine == null) throw new InvalidOperationException("Courtyard carrier has no shared spine.");
            body = Worker.gameObject.AddComponent<CapsuleCollider>();
            body.center = Vector3.up * .88f; body.height = 1.7f; body.radius = .22f;
            var movingRenderers = new List<Renderer>(Worker.GetComponentsInChildren<Renderer>(true));
            movingRenderers.AddRange(Basket.GetComponentsInChildren<Renderer>(true));
            renderers = movingRenderers.ToArray();
            rendererVisibility = new bool[renderers.Length];
            var leftFootwear = new List<SkinnedMeshRenderer>();
            var rightFootwear = new List<SkinnedMeshRenderer>();
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                rendererVisibility[index] = renderer.enabled;
                if (!renderer.enabled || !(renderer is SkinnedMeshRenderer skinned)) continue;
                if (renderer.name.EndsWith("Sole.L", StringComparison.Ordinal)) leftFootwear.Add(skinned);
                else if (renderer.name.EndsWith("Sole.R", StringComparison.Ordinal)) rightFootwear.Add(skinned);
            }
            leftSoles = leftFootwear.ToArray(); rightSoles = rightFootwear.ToArray();
            initialized = true;
            lastCalendarSeconds = CalendarSeconds();
            ApplyAt(lastCalendarSeconds);
        }

        private static double CalendarSeconds() => (GameSessionState.GameDayIndex * 1440d +
            GameSessionState.GameTimeOfDayMinutes) / GameTimeState.GameMinutesPerRealSecond;

        public void ApplyAt(double seconds)
        {
            if (!initialized || double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            ElapsedSeconds = seconds;
            Sample(Evaluate(seconds), seconds, true);
        }

        public readonly struct ContactSample
        {
            internal ContactSample(double seconds, float contactWeight, float rightError, float leftError,
                bool hasLeft, bool hasRight, float leftClearance, float rightClearance)
            {
                Seconds = seconds; ContactWeight = contactWeight;
                RightGripError = rightError; LeftGripError = leftError;
                HasLeftFootSurface = hasLeft; HasRightFootSurface = hasRight;
                LeftSoleClearance = leftClearance; RightSoleClearance = rightClearance;
            }
            public double Seconds { get; }
            public float ContactWeight { get; }
            public float RightGripError { get; }
            public float LeftGripError { get; }
            public bool HasLeftFootSurface { get; }
            public bool HasRightFootSurface { get; }
            public float LeftSoleClearance { get; }
            public float RightSoleClearance { get; }
        }

        /// <summary>Read-only authoring proof over actual imported handles,
        /// sampled skin and equipped boot soles. No render or camera is needed;
        /// callers decide tolerances and the original phase is restored.</summary>
        public IReadOnlyList<ContactSample> MeasureContactsAtSamples(IReadOnlyList<double> seconds)
        {
            if (!initialized) throw new InvalidOperationException("Courtyard life has no initialized carrier.");
            if (seconds == null) throw new ArgumentNullException(nameof(seconds));
            Transform footLeft = CityPedestrianHandProps.FindSocket(Worker.ModelRoot, "foot.L");
            Transform footRight = CityPedestrianHandProps.FindSocket(Worker.ModelRoot, "foot.R");
            if (footLeft == null || footRight == null || leftSoles.Length == 0 || rightSoles.Length == 0)
                throw new InvalidOperationException("Carrier contact proof requires both actual equipped boot soles and feet.");
            double restoreSeconds = ElapsedSeconds;
            var samples = new List<ContactSample>(seconds.Count);
            using var feet = Player3DFootGroundProbe.Create(leftSoles, rightSoles, Worker.transform);
            try
            {
                foreach (double time in seconds)
                {
                    ApplyAt(time);
                    Physics.SyncTransforms();
                    bool hasLeftSole = feet.TryGetSoleHeight(FootSide.Left, out float soleLeft);
                    bool hasRightSole = feet.TryGetSoleHeight(FootSide.Right, out float soleRight);
                    FootGroundSample groundLeft = feet.Probe(footLeft.position, Worker.transform.forward, Worker.transform.position.y);
                    FootGroundSample groundRight = feet.Probe(footRight.position, Worker.transform.forward, Worker.transform.position.y);
                    bool hasLeft = hasLeftSole && groundLeft.HasSurface;
                    bool hasRight = hasRightSole && groundRight.HasSurface;
                    samples.Add(new ContactSample(time, ContactWeight,
                        Vector3.Distance(Worker.RightGrip.position, leftHandle.position),
                        Vector3.Distance(Worker.LeftGrip.position, rightHandle.position), hasLeft, hasRight,
                        hasLeft ? soleLeft - groundLeft.HeelY : float.PositiveInfinity,
                        hasRight ? soleRight - groundRight.HeelY : float.PositiveInfinity));
                }
            }
            finally
            {
                ApplyAt(restoreSeconds);
                Physics.SyncTransforms();
            }
            return samples.AsReadOnly();
        }

        private void LateUpdate()
        {
            if (!initialized) return;
            bool paused = GameTimeScaleRuntime.IsPaused || !GameSessionState.IsGameTimeRunning;
            double calendar = CalendarSeconds();
            double delta = Math.Max(0d, calendar - lastCalendarSeconds);
            lastCalendarSeconds = calendar;
            float distanceSquared = PresentationObserver == null ? 0f :
                (PresentationObserver.position - Court.ServiceProp.Position).sqrMagnitude;
            float limit = IsPresentationActive ? PresentationExitDistance : PresentationEnterDistance;
            bool present = ForcePresentation || distanceSquared <= limit * limit;
            bool changed = present != IsPresentationActive;
            IsPresentationActive = present;
            if (changed || visibilityRefresh)
            {
                for (int index = 0; index < renderers.Length; index++)
                    renderers[index].enabled = present && rendererVisibility[index];
                visibilityRefresh = false;
                if (!present) Worker.ReleaseAnimation();
            }
            if (!paused && AutoAdvance)
            {
                // Only an actual obstruction defers this local activity;
                // distance and frame rate never slow its authoritative clock.
                double desiredSeconds = Math.Max(0d, calendar - phaseDelay);
                Frame frame = Evaluate(desiredSeconds);
                IsBlocked = HasObstruction(frame);
                if (IsBlocked) phaseDelay += delta;
                else ElapsedSeconds = desiredSeconds;
            }
            bool pose = present && (!paused || changed) &&
                (ForcePresentation || changed || ElapsedSeconds - lastPoseSeconds >= 1d / 30d);
            if (!paused || changed) Sample(Evaluate(ElapsedSeconds), ElapsedSeconds, pose);
        }

        private struct Frame
        {
            public Vector3 Actor, Basket;
            public Quaternion Facing, BasketRotation;
            public float Speed, Contact;
            public bool Carrying;
        }

        private Frame Evaluate(double seconds)
        {
            float phase = (float)(seconds % CycleSeconds);
            bool atEnd = phase >= 20f && phase < 46f;
            Vector3 first = Court.CarryRoute[0], last = Court.CarryRoute[Court.CarryRoute.Count - 1];
            Vector3 actor = atEnd ? last : first;
            Vector3 shelf = atEnd ? Court.BasketEndPosition : Court.BasketRestPosition;
            Vector3 shelfForward = shelfRotation * Vector3.back;
            shelfForward.y = 0f;
            Quaternion shelfFacing = Quaternion.LookRotation(shelfForward, Vector3.up);
            Route(0f, out Vector3 firstDirection, out _);
            Route(1f, out Vector3 lastDirection, out _);
            Quaternion outgoingFacing = Quaternion.LookRotation(firstDirection, Vector3.up);
            Quaternion arrivalFacing = Quaternion.LookRotation(lastDirection, Vector3.up);
            Quaternion returnFacing = Quaternion.LookRotation(-lastDirection, Vector3.up);
            Quaternion homeFacing = Quaternion.LookRotation(-firstDirection, Vector3.up);
            Quaternion facing = shelfFacing;
            float speed = 0f;
            if (phase >= 12f && phase < 14f)
                facing = Quaternion.Slerp(shelfFacing, outgoingFacing, Smooth((phase - 12f) / 2f));
            else if (phase >= 14f && phase < 20f)
            {
                float t = (phase - 14f) / 6f;
                actor = Route(Smooth(t), out Vector3 direction, out float pathSpeed);
                facing = Quaternion.LookRotation(direction, Vector3.up);
                speed = pathSpeed * t * (1f - t);
            }
            else if (phase >= 20f && phase < 22f)
                facing = Quaternion.Slerp(arrivalFacing, shelfFacing, Smooth((phase - 20f) / 2f));
            else if (phase >= 38f && phase < 40f)
                facing = Quaternion.Slerp(shelfFacing, returnFacing, Smooth((phase - 38f) / 2f));
            else if (phase >= 40f && phase < 46f)
            {
                float t = (phase - 40f) / 6f;
                actor = Route(1f - Smooth(t), out Vector3 direction, out float pathSpeed);
                facing = Quaternion.LookRotation(-direction, Vector3.up);
                speed = pathSpeed * t * (1f - t);
            }
            else if (phase >= 46f && phase < 48f)
                facing = Quaternion.Slerp(homeFacing, shelfFacing, Smooth((phase - 46f) / 2f));
            actor = GroundRoutePoint(actor);
            bool outbound = phase >= 10f && phase < 24f;
            bool inbound = phase >= 36f && phase < 50f;
            Vector3 held = actor + facing * new Vector3(0f, .95f, .50f);
            Quaternion heldRotation = facing * Quaternion.Euler(0f, 180f, 0f);
            Vector3 basket = shelf;
            Quaternion basketRotation = shelfRotation;
            if (outbound || inbound)
            {
                float lift = outbound ? Smooth((phase - 10f) / 2f) : Smooth((phase - 36f) / 2f);
                float lower = outbound ? Smooth((phase - 22f) / 2f) : Smooth((phase - 48f) / 2f);
                Vector3 origin = outbound ? Court.BasketRestPosition : Court.BasketEndPosition;
                Vector3 destination = outbound ? Court.BasketEndPosition : Court.BasketRestPosition;
                basket = Vector3.Lerp(Vector3.Lerp(origin, held, lift), destination, lower);
                basketRotation = Quaternion.Slerp(Quaternion.Slerp(shelfRotation, heldRotation, lift), shelfRotation, lower);
            }
            float contact = phase >= 8f && phase < 26f ?
                Smooth((phase - 8f) / 2f) * (1f - Smooth((phase - 24f) / 2f)) :
                phase >= 34f && phase < 52f ? Smooth((phase - 34f) / 2f) *
                    (1f - Smooth((phase - 50f) / 2f)) : 0f;
            return new Frame { Actor = actor, Facing = facing, Basket = basket,
                BasketRotation = basketRotation, Speed = speed, Contact = contact,
                Carrying = outbound || inbound };
        }

        private void Sample(Frame frame, double seconds, bool pose)
        {
            Worker.transform.SetPositionAndRotation(frame.Actor, frame.Facing);
            Basket.SetPositionAndRotation(frame.Basket, frame.BasketRotation);
            ContactWeight = frame.Contact;
            if (!pose) return;
            lastPoseSeconds = seconds;
            Worker.ApplyLocomotion(frame.Speed, frame.Contact > .001f,
                (float)(seconds % 3600d), frame.Contact > .001f ? (Vector3?)Basket.position : null);
            HandsMatch = true; HandContactError = 0f;
            if (frame.Contact < .001f) return;
            Quaternion upright = spine.rotation;
            Vector3 right = leftHandle.position, left = rightHandle.position;
            // The body bends over the actual shelf; neither the arms nor the
            // carried prop are stretched to conceal an unreachable grip.
            for (int attempt = 0; attempt <= 14; attempt++)
            {
                float lean = attempt * 4f;
                spine.rotation = Quaternion.AngleAxis(lean, Worker.transform.right) * upright;
                HandsMatch = Worker.ApplyHandContacts(right, left, frame.Contact);
                HandContactError = frame.Contact > .999f ? Mathf.Max(
                    Vector3.Distance(Worker.RightGrip.position, right),
                    Vector3.Distance(Worker.LeftGrip.position, left)) : 0f;
                if (HandsMatch || frame.Contact < .999f) break;
            }
        }

        private Vector3 Route(float progress, out Vector3 direction, out float speedPerProgress)
        {
            // The middle control point bends one continuous walking arc.
            // A polyline would snap the torso and held basket at its corner.
            if (Court.CarryRoute.Count == 3)
            {
                float t = Mathf.Clamp01(progress);
                Vector3 a = Court.CarryRoute[0], b = Court.CarryRoute[1], c = Court.CarryRoute[2];
                Vector3 tangent = 2f * ((1f - t) * (b - a) + t * (c - b));
                speedPerProgress = tangent.magnitude;
                direction = tangent; direction.y = 0f;
                return Vector3.Lerp(Vector3.Lerp(a, b, t), Vector3.Lerp(b, c, t), t);
            }
            float remaining = Mathf.Clamp01(progress) * RouteLength();
            speedPerProgress = RouteLength();
            for (int index = 1; index < Court.CarryRoute.Count; index++)
            {
                Vector3 a = Court.CarryRoute[index - 1], b = Court.CarryRoute[index];
                float length = Vector3.Distance(a, b);
                if (remaining <= length || index == Court.CarryRoute.Count - 1)
                {
                    direction = b - a; direction.y = 0f;
                    if (direction.sqrMagnitude < .0001f) direction = shelfRotation * Vector3.right;
                    return Vector3.Lerp(a, b, length > .0001f ? remaining / length : 1f);
                }
                remaining -= length;
            }
            direction = shelfRotation * Vector3.right;
            return Court.CarryRoute[0];
        }

        private float RouteLength()
        {
            float length = 0f;
            for (int index = 1; index < Court.CarryRoute.Count; index++)
                length += Vector3.Distance(Court.CarryRoute[index - 1], Court.CarryRoute[index]);
            return length;
        }

        private Vector3 GroundRoutePoint(Vector3 point)
        {
            // The walking curve follows the real floor between its authored
            // grounded controls; a fitted sloping shelter does not make the
            // terrain itself a plane. This one bounded actor reuses its query
            // buffer, with a complete fallback if a dense site fills it.
            Vector3 origin = point + Vector3.up * .4f;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, .8f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            RaycastHit[] contacts = hits;
            if (count == hits.Length)
            {
                contacts = Physics.RaycastAll(origin, Vector3.down, .8f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                count = contacts.Length;
            }
            float closest = float.PositiveInfinity;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = contacts[index];
                if (hit.collider == null || hit.collider == body || hit.collider == basketBody ||
                    hit.normal.y < .65f || hit.distance >= closest) continue;
                point.y = hit.point.y; closest = hit.distance;
            }
            return point;
        }

        private bool HasObstruction(Frame frame)
        {
            Vector3 delta = frame.Actor - Worker.transform.position;
            if (delta.sqrMagnitude < .00000001f &&
                (frame.Basket - Basket.position).sqrMagnitude < .00000001f &&
                Quaternion.Angle(frame.BasketRotation, Basket.rotation) < .001f) return false;
            if (delta.sqrMagnitude > .000001f)
            {
                Vector3 low = Worker.transform.position + Vector3.up * .25f;
                Vector3 high = Worker.transform.position + Vector3.up * 1.51f;
                int count = Physics.CapsuleCastNonAlloc(low, high, .215f, delta.normalized, hits,
                    delta.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                if (count == hits.Length)
                {
                    foreach (RaycastHit hit in Physics.CapsuleCastAll(low, high, .215f, delta.normalized,
                        delta.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        if (Other(hit.collider)) return true;
                }
                else for (int index = 0; index < count; index++) if (Other(hits[index].collider)) return true;
            }
            // Shelves touch the basket bottom, so the query is inset by a
            // few millimetres. Overflow uses a complete query, never truncation.
            Vector3 center = frame.Basket + frame.BasketRotation * (Vector3.up * .14f);
            int overlapCount = Physics.OverlapBoxNonAlloc(center, new Vector3(.275f, .135f, .195f),
                overlaps, frame.BasketRotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (overlapCount == overlaps.Length)
            {
                foreach (Collider collider in Physics.OverlapBox(center, new Vector3(.275f, .135f, .195f),
                    frame.BasketRotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    if (Other(collider)) return true;
            }
            else for (int index = 0; index < overlapCount; index++) if (Other(overlaps[index])) return true;
            return false;
        }

        private bool Other(Collider collider) => collider != null && collider != body && collider != basketBody;
        private static float Smooth(float value) { float t = Mathf.Clamp01(value); return t * t * (3f - 2f * t); }
        private void OnEnable()
        {
            if (!initialized) return;
            visibilityRefresh = true;
            lastCalendarSeconds = CalendarSeconds();
            if (AutoAdvance) ElapsedSeconds = Math.Max(0d, lastCalendarSeconds - phaseDelay);
            Sample(Evaluate(ElapsedSeconds), ElapsedSeconds, IsPresentationActive);
        }
        private void OnDisable() { if (Worker != null) Worker.ReleaseAnimation(); }
        private void OnDestroy() { if (Worker != null) NpcFootstepSources.Unregister(Worker.transform); }
    }
}
