using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct HomeShowerSoapTarget
    {
        public readonly HomeShowerWashRegion Region;
        public readonly Transform Anchor;
        public readonly Vector3 LocalPoint;
        public readonly Vector3 LocalNormal;
        public readonly string SurfaceName;
        public Transform SurfaceTransform => Anchor;
        public bool IsValid => HomeShowerWashingProgress.IsWashableRegion(Region) && Anchor != null;
        public Vector3 WorldPoint => Anchor != null ? Anchor.TransformPoint(LocalPoint) : Vector3.zero;
        public Vector3 WorldNormal => Anchor != null ? Anchor.TransformDirection(LocalNormal).normalized : Vector3.up;

        public HomeShowerSoapTarget(HomeShowerWashRegion region, Transform anchor,
            Vector3 point, Vector3 normal, string surfaceName)
        {
            Region = region; Anchor = anchor; SurfaceName = surfaceName;
            LocalPoint = anchor != null ? anchor.InverseTransformPoint(point) : Vector3.zero;
            LocalNormal = anchor != null ? anchor.InverseTransformDirection(normal).normalized : Vector3.up;
        }
    }

    /// <summary>
    /// The existing bar of soap held exclusively by the hero's right hand.
    /// Input requests a nearby skin contact; bounded travel and the shared
    /// real-mesh arm guard decide what actually happens and earns progress.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HomeShowerSoapPose : MonoBehaviour
    {
        public const float PickupSeconds = 1.4f;
        public const float PutDownSeconds = 1.6f;
        public const float SoapHalfThickness = 0.014f;
        public const float ContactTolerance = 0.018f;
        public const float HandTravelSpeed = 0.65f;
        public const float WithdrawalDistance = 0.10f;
        private const float PickupContact = 0.58f;
        private const float PutDownContact = 0.68f;
        private const float TransferContactTolerance = 0.008f;

        private sealed class Surface
        {
            public Renderer Renderer;
            public Transform Anchor;
            public HomeShowerWashRegion Region;
            public Vector3[] Vertices;
            public int[] Triangles;
            public Bounds Bounds;
        }

        private sealed class Hand
        {
            public HomeTeethBrushingArmPose Guard;
            public Transform Effector;
            public Transform Bone;
            public bool Left;
            public Vector3 Current;
            public Quaternion Frame;
            public Vector3 Brace;
            public Quaternion BraceFrame;
            public Vector3 PalmSurfaceInHand;
            public float PalmToSoap;
        }

        private readonly Hand[] hands = { new Hand(), new Hand { Left = true } };
        private readonly List<Surface> surfaces = new List<Surface>();
        private readonly List<Vector3> vertices = new List<Vector3>();
        private Player3DAssetRegistry registry;
        private Camera eyeCamera;
        private MeshFilter soapMesh;
        private Vector3[] soapVertices;
        private int[] soapTriangles;
        private Transform actor, soap, soapParent;
        private HomeShowerWashPose wash;
        private Mesh sample;
        private Vector3 soapRestPosition, soapRestScale;
        private Quaternion soapRestRotation;
        private Vector3 soapRestWorld;
        private Quaternion soapRestWorldRotation;
        private HomeShowerSoapTarget previousTarget;
        private Vector3 previousContactLocal;
        private Vector3 withdrawal;
        private Vector3 pickupStart;
        private Quaternion pickupFrame;
        private Vector3 putDownStart;
        private Quaternion putDownFrame;
        private int routeStage;
        private bool started, wasContact, hasPutDownStart, pickupAttached, putDownReleased;
        private bool lastHeld;
        private int pickupContactFrame = -1, putDownContactFrame = -1;
        private float pickupProgress, putDownProgress;
        private HomeShowerSoapTarget currentTarget;
        private Vector3 currentAim;
        private Quaternion requestedFrame;
        private bool requestedHeld;

        public bool IsInitialized => registry != null && soap != null && soapMesh != null && wash != null;
        public bool IsActive => started;
        public bool HasSoap { get; private set; }
        public bool IsTransferComplete { get; private set; }
        public bool IsContacting { get; private set; }
        public HomeShowerWashRegion CurrentRegion { get; private set; } = HomeShowerWashRegion.None;
        public float ContactTravelMetres { get; private set; }
        public long ContactSolveCount { get; private set; }
        public double ContactSolveTotalMilliseconds { get; private set; }
        public double ContactSolveMaxMilliseconds { get; private set; }
        public float HandError { get; private set; }
        public float BodyClearance => Mathf.Min(hands[0].Guard != null ? hands[0].Guard.BodyClearance : 0f,
            hands[1].Guard != null ? hands[1].Guard.BodyClearance : 0f);
        public int BodyIntersectionCount => (hands[0].Guard != null ? hands[0].Guard.BodyIntersectionCount : 0)
            + (hands[1].Guard != null ? hands[1].Guard.BodyIntersectionCount : 0);
        public Vector3 ContactPoint { get; private set; }
        public Transform Soap => soap;
        public Vector3 PalmContactPoint => hands[0].Bone != null
            ? hands[0].Bone.position + hands[0].Bone.rotation * hands[0].PalmSurfaceInHand : Vector3.zero;
        public float SoapGripGapMetres
        {
            get
            {
                if (!HasSoap) return 0f;
                // Independent geometry readback: test the real soap surface,
                // not the effector point used to place it beside the palm.
                const float startOffset = 0.025f;
                Vector3 normal = hands[0].Frame * Vector3.right;
                Ray ray = new Ray(PalmContactPoint + normal * startOffset, -normal);
                Matrix4x4 matrix = soapMesh.transform.localToWorldMatrix;
                float nearest = float.PositiveInfinity;
                for (int index = 0; index < soapTriangles.Length; index += 3)
                    if (RayTriangle(ray, matrix.MultiplyPoint3x4(soapVertices[soapTriangles[index]]),
                            matrix.MultiplyPoint3x4(soapVertices[soapTriangles[index + 1]]),
                            matrix.MultiplyPoint3x4(soapVertices[soapTriangles[index + 2]]), out float distance) &&
                        distance < nearest) nearest = distance;
                return nearest - startOffset;
            }
        }
        public Vector3 TargetPoint => currentTarget.WorldPoint;
        public Vector3 TargetNormal => currentTarget.WorldNormal;
        public Vector3 AimPoint => currentAim;
        public int RouteStage => routeStage;
        public float PickupProgress => pickupProgress;
        public bool PickupAttached => pickupAttached;
        public float HandFrameError => Quaternion.Angle(hands[0].Frame, requestedFrame);
        public string ContactDiagnostic => $"held={requestedHeld}, region={CurrentRegion}, surface={currentTarget.SurfaceName}, " +
            $"target={TargetPoint:F4}, normal={TargetNormal:F4}, aim={currentAim:F4}, hand={hands[0].Current:F4}, " +
            $"error={HandError:F4}, aimError={Vector3.Distance(hands[0].Current, currentAim):F4}, " +
            $"gripGap={SoapGripGapMetres:F4}, frameError={HandFrameError:F2}, route={routeStage}, " +
            $"pickup={pickupProgress:F3}, pickupAttached={pickupAttached}, putDown={putDownProgress:F3}, " +
            $"right={hands[0].Guard?.LastContactRejectReason}, left={hands[1].Guard?.LastContactRejectReason}";

        public bool Initialize(HomeInteriorRoot home, Transform soapTransform, HomeShowerWashPose washPose)
        {
            End();
            ReleasePreparation();
            if (home == null || soapTransform == null || washPose == null ||
                !(home.Player.Visual is Player3DCharacterPresentation visual) || visual.Registry == null) return false;
            registry = visual.Registry;
            eyeCamera = home.CameraFollow != null ? home.CameraFollow.Camera : null;
            actor = home.Player.GameObject.transform;
            soap = soapTransform; wash = washPose;
            soapMesh = soap.GetComponentInChildren<MeshFilter>();
            if (soapMesh == null || soapMesh.sharedMesh == null) return false;
            soapVertices = soapMesh.sharedMesh.vertices;
            soapTriangles = soapMesh.sharedMesh.triangles;
            soapParent = soap.parent; soapRestPosition = soap.localPosition;
            soapRestRotation = soap.localRotation; soapRestScale = soap.localScale;
            sample = new Mesh { name = "Shower Soap Surface Readback", hideFlags = HideFlags.HideAndDontSave };
            surfaces.Clear();
            foreach (Player3DMeshBinding binding in registry.MeshBindings)
            {
                if (binding?.Renderer == null || !binding.MeshName.StartsWith("GEO_", StringComparison.Ordinal)) continue;
                HomeShowerWashRegion region = RegionFor(binding.MeshName);
                if (region != HomeShowerWashRegion.None)
                    surfaces.Add(new Surface { Renderer = binding.Renderer, Anchor = binding.Bone, Region = region });
            }
            AddAttachment(wash.AnatomyRoot);
            AddAttachment(wash.LeftScrotum);
            AddAttachment(wash.RightScrotum);
            for (int index = 0; index < hands.Length; index++)
            {
                Hand hand = hands[index];
                Player3DAnatomicalPart part = hand.Left ? Player3DAnatomicalPart.LeftHand : Player3DAnatomicalPart.RightHand;
                if (!registry.TryGetPart(part, out var binding) || binding?.Bone == null) return false;
                hand.Bone = binding.Bone;
                hand.Effector = new GameObject(hand.Left ? "Shower Soap Left Contact" : "Shower Soap Right Contact").transform;
                hand.Effector.SetParent(hand.Bone, false);
                hand.Guard = gameObject.AddComponent<HomeTeethBrushingArmPose>();
                hand.Guard.Initialize(registry, actor, hand.Left);
                hand.Guard.Effector = hand.Effector;
            }
            return surfaces.Count >= 6;
        }

        public void Begin()
        {
            if (!IsInitialized || started) return;
            soapRestWorld = soap.position; soapRestWorldRotation = soap.rotation;
            routeStage = 0;
            HasSoap = IsTransferComplete = pickupAttached = putDownReleased = hasPutDownStart = false;
            pickupContactFrame = putDownContactFrame = -1;
            pickupProgress = putDownProgress = 0f;
            wasContact = lastHeld = false;
            for (int index = 0; index < hands.Length; index++)
            {
                Hand hand = hands[index];
                ConfigureGuardBody(hand);
                // First capture measures this exact production palm. Then
                // put the soap's centre beyond the palm and recapture its
                // effector offset in the measured hand frame.
                hand.Effector.position = hand.Bone.position;
                hand.Guard.Capture();
                Vector3 palmNormal = (hand.Left ? 1f : -1f) * (hand.Guard.PhysicalHandFrame * Vector3.right);
                if (!hand.Guard.TryGetPalmSurfacePoint(out Vector3 palmSurface))
                    throw new InvalidOperationException("The production shower hand must expose its measured palm surface.");
                hand.PalmSurfaceInHand = Quaternion.Inverse(hand.Bone.rotation) * (palmSurface - hand.Bone.position);
                hand.Effector.position = palmSurface + palmNormal * SoapHalfThickness;
                hand.PalmToSoap = Vector3.Distance(hand.Guard.PalmCenter, hand.Effector.position);
                hand.Guard.Capture();
                hand.Current = hand.Brace = hand.Effector.position;
                hand.Frame = hand.BraceFrame = hand.Guard.PhysicalHandFrame;
            }
            pickupStart = hands[0].Current; pickupFrame = hands[0].Frame;
            currentAim = pickupStart; requestedFrame = pickupFrame;
            started = true;
        }

        public void ApplyPickup(float normalized)
        {
            ResetCredit();
            if (!started) return;
            float step = TransferDeltaTime();
            wash.ApplyPassiveSoapContact(Vector3.zero, step);
            pickupProgress = Mathf.Min(Mathf.Clamp01(normalized), pickupProgress + step / PickupSeconds);
            float t = pickupProgress;
            IsTransferComplete = false;
            hands[1].Guard.RestoreContactArm();
            if (t <= 0f && !pickupAttached)
            {
                hands[0].Guard.RestoreContactArm();
                hands[0].Current = hands[0].Effector.position;
                hands[0].Frame = hands[0].Guard.PhysicalHandFrame;
                return;
            }
            if (!pickupAttached || Time.frameCount <= pickupContactFrame)
                pickupProgress = t = Mathf.Min(t, PickupContact);
            Quaternion frame = FrameForContact(Vector3.up, actor.forward, false);
            Vector3 destination = soapRestWorld;
            Vector3 target;
            if (t <= PickupContact)
            {
                float reach = Smooth(t / PickupContact);
                target = Vector3.Lerp(pickupStart, destination, reach) +
                    (actor.right * 0.04f - actor.forward * 0.04f + Vector3.up * 0.09f) * Mathf.Sin(Mathf.PI * reach);
                frame = Quaternion.Slerp(pickupFrame, frame, reach);
            }
            else
            {
                float lift = Smooth((t - PickupContact) / (1f - PickupContact));
                target = Vector3.Lerp(destination, HoverPoint(), lift) + Vector3.up * 0.04f * Mathf.Sin(Mathf.PI * lift);
            }
            currentAim = target; requestedFrame = frame;
            bool contact = StepHand(hands[0], target, frame, step);
            if (!pickupAttached && t >= PickupContact && contact && Vector3.Distance(hands[0].Effector.position, soapRestWorld) <= TransferContactTolerance)
            {
                pickupAttached = HasSoap = true;
                pickupContactFrame = Time.frameCount;
            }
            if (HasSoap) FollowSoap();
            HandError = hands[0].Guard.ContactError;
            IsTransferComplete = t >= 1f && HasSoap && contact;
        }

        public void ApplyPutDown(float normalized)
        {
            ResetCredit();
            if (!started) return;
            IsTransferComplete = false;
            float step = TransferDeltaTime();
            wash.ApplyPassiveSoapContact(Vector3.zero, step);
            putDownProgress = Mathf.Min(Mathf.Clamp01(normalized), putDownProgress + step / PutDownSeconds);
            float t = putDownProgress;
            hands[1].Guard.RestoreContactArm();
            if (!hasPutDownStart)
            {
                putDownStart = hands[0].Current; putDownFrame = hands[0].Frame;
                hasPutDownStart = true;
            }
            if (!putDownReleased || Time.frameCount <= putDownContactFrame)
                putDownProgress = t = Mathf.Min(t, PutDownContact);
            Quaternion restFrame = FrameForContact(Vector3.up, actor.forward, false);
            Vector3 target;
            Quaternion frame;
            if (t <= PutDownContact)
            {
                float reach = Smooth(t / PutDownContact);
                // The raised front arc leaves the skin before travelling
                // to the shelf. The exact shelf pose is the release contact.
                target = Vector3.Lerp(putDownStart, soapRestWorld, reach) +
                    (actor.forward * 0.08f + Vector3.up * 0.14f) * Mathf.Sin(Mathf.PI * reach);
                frame = Quaternion.Slerp(putDownFrame, restFrame, reach);
            }
            else
            {
                float release = Smooth((t - PutDownContact) / (1f - PutDownContact));
                target = Vector3.Lerp(soapRestWorld, hands[0].Brace, release) +
                    (-actor.forward * 0.04f + Vector3.up * 0.08f) * Mathf.Sin(Mathf.PI * release);
                frame = Quaternion.Slerp(restFrame, hands[0].BraceFrame, release);
            }
            currentAim = target; requestedFrame = frame;
            bool contact = StepHand(hands[0], target, frame, step);
            if (!putDownReleased && t >= PutDownContact && contact &&
                Vector3.Distance(hands[0].Effector.position, soapRestWorld) <= TransferContactTolerance)
            {
                HasSoap = false; putDownReleased = true;
                putDownContactFrame = Time.frameCount;
                RestoreSoap();
            }
            if (HasSoap) FollowSoap();
            if (t >= 1f && putDownReleased && contact)
            {
                hands[0].Guard.RestoreContactArm();
                hands[1].Guard.RestoreContactArm();
                IsTransferComplete = true;
            }
        }

        public void ApplyScrub(HomeShowerSoapTarget target, bool held, float deltaTime)
        {
            ResetCredit();
            if (!started || !HasSoap) return;
            if (!target.IsValid) target = default;
            currentTarget = target; requestedHeld = held;
            IsTransferComplete = false;
            float dt = Mathf.Clamp(deltaTime, 0f, 0.05f);
            hands[1].Guard.RestoreContactArm();
            Hand hand = hands[0];
            if (!target.IsValid)
            {
                wash.ApplyPassiveSoapContact(Vector3.zero, dt);
                StepHand(hand, HoverPoint(), FrameForContact(Vector3.up, actor.forward, false), dt);
                FollowSoap(); wasContact = lastHeld = false;
                return;
            }
            Vector3 point = target.WorldPoint, normal = target.WorldNormal;
            Vector3 desired = point + normal * SoapHalfThickness;
            bool changed = previousTarget.IsValid &&
                (previousTarget.Region != target.Region || Vector3.Distance(previousTarget.WorldPoint, point) > 0.13f ||
                 Vector3.Dot(previousTarget.WorldNormal, normal) < 0.65f);
            if ((changed || (!held && lastHeld)) && routeStage == 0)
            {
                withdrawal = hand.Current + ApproachDirection(previousTarget.IsValid ? previousTarget : target) * WithdrawalDistance;
                routeStage = 1;
            }
            Vector3 aim = desired;
            if (routeStage == 1)
            {
                aim = withdrawal;
                if (Vector3.Distance(hand.Current, aim) < 0.02f) routeStage = 2;
            }
            if (routeStage == 2)
            {
                aim = desired + ApproachDirection(target) * WithdrawalDistance;
                if (Vector3.Distance(hand.Current, aim) < 0.02f) routeStage = 0;
            }
            if (!held && routeStage == 0) aim = desired + ApproachDirection(target) * 0.07f;
            // A low central contact uses a sideways hand: fingers toward
            // the opposite side, wrist outside the pelvis instead of above it.
            Vector3 fingerDirection = target.Region == HomeShowerWashRegion.Intimate
                ? -actor.right : -actor.up;
            Vector3 fingers = Vector3.ProjectOnPlane(fingerDirection, normal);
            if (fingers.sqrMagnitude < 0.01f) fingers = Vector3.ProjectOnPlane(actor.forward, normal);
            Quaternion frame = FrameForContact(normal, fingers.normalized, false);
            currentAim = aim; requestedFrame = frame;
            bool safeContact = StepHand(hand, aim, frame, dt);
            FollowSoap();
            HandError = Vector3.Distance(hand.Effector.position, desired);
            IsContacting = held && routeStage == 0 && safeContact && HandError <= ContactTolerance &&
                Quaternion.Angle(hand.Frame, frame) < 8f && hand.Guard.BodyIntersectionCount == 0;
            CurrentRegion = target.Region;
            ContactPoint = hand.Effector.position - normal * SoapHalfThickness;
            Vector3 local = target.Anchor.InverseTransformPoint(ContactPoint);
            Vector3 contactTravel = Vector3.zero;
            if (IsContacting && wasContact && previousTarget.Region == target.Region && previousTarget.Anchor == target.Anchor)
            {
                contactTravel = target.Anchor.TransformVector(local - previousContactLocal);
                ContactTravelMetres = contactTravel.magnitude;
            }
            previousContactLocal = local;
            previousTarget = target; wasContact = IsContacting; lastHeld = held;
            if (IsContacting && target.Region == HomeShowerWashRegion.Intimate)
                wash.ApplyPassiveSoapContact(target.Anchor, ContactPoint, normal, contactTravel, dt);
            else
                wash.ApplyPassiveSoapContact(Vector3.zero, dt);
        }

        /// <summary>Find a short stroke on the selected exposed skin, with room at both ends.</summary>
        public bool TryPrepareWashStroke(HomeShowerSoapTarget centre, out Vector3 localDirection, out float radius)
        {
            localDirection = default; radius = 0f;
            if (!centre.IsValid) return false;
            RefreshSurfaces(centre);
            foreach (float length in new[] { 0.026f, 0.018f, 0.012f, 0.006f, 0.003f })
                foreach (Vector3 axis in new[] { actor.right, actor.up, actor.forward })
                {
                    Vector3 tangent = Vector3.ProjectOnPlane(axis, centre.WorldNormal);
                    if (tangent.sqrMagnitude < 0.01f) continue;
                    Vector3 direction = centre.Anchor.InverseTransformVector(tangent.normalized);
                    bool fits = true;
                    for (int index = -2; index <= 2; index++)
                        if (!TryProjectWashStroke(centre, direction * (index * length * 0.5f), out _))
                        { fits = false; break; }
                    if (!fits) continue;
                    localDirection = direction; radius = length;
                    return true;
                }
            return false;
        }

        public bool TrySampleWashStroke(HomeShowerSoapTarget centre, Vector3 localOffset, out HomeShowerSoapTarget target)
        {
            RefreshSurfaces(centre);
            return TryProjectWashStroke(centre, localOffset, out target);
        }

        private bool TryProjectWashStroke(HomeShowerSoapTarget centre, Vector3 localOffset, out HomeShowerSoapTarget target)
        {
            target = default;
            if (!centre.IsValid) return false;
            Vector3 normal = centre.WorldNormal;
            Vector3 point = centre.Anchor.TransformPoint(centre.LocalPoint + localOffset);
            Ray ray = new Ray(point + normal * 0.06f, -normal);
            float nearest = 0.12f;
            foreach (Surface surface in surfaces)
            {
                if (surface.Anchor != centre.Anchor || surface.Renderer.name != centre.SurfaceName || !CanPick(surface)) continue;
                for (int index = 0; index < surface.Triangles.Length; index += 3)
                {
                    Vector3 a = surface.Vertices[surface.Triangles[index]], b = surface.Vertices[surface.Triangles[index + 1]], c = surface.Vertices[surface.Triangles[index + 2]];
                    if (!RayTriangle(ray, a, b, c, out float distance) || distance >= nearest) continue;
                    Vector3 hitNormal = Vector3.Cross(b - a, c - a).normalized;
                    if (Vector3.Dot(hitNormal, normal) < 0f) hitNormal = -hitNormal;
                    if (Vector3.Dot(hitNormal, normal) < 0.75f || !CanContactPatch(surface, hitNormal)) continue;
                    nearest = distance;
                    target = new HomeShowerSoapTarget(centre.Region, centre.Anchor, ray.GetPoint(distance), hitNormal, centre.SurfaceName);
                }
            }
            return target.IsValid;
        }

        public bool TryPickBody(Ray ray, out HomeShowerSoapTarget target)
        {
            target = default;
            if (!IsInitialized) return false;
            // The parent rebuilt the torso/brace before this presentation.
            // Input points at the hands and upper arms actually shown last
            // frame, so restore their accepted pose before reading the skin.
            // ApplyScrub then advances that same guarded pose normally.
            if (started && HasSoap)
                foreach (Hand hand in hands) hand.Guard.RestoreLastContactArm();
            RefreshSurfaces();
            return TryPickBodyCached(ray, out target);
        }

        public bool TryGetRegionTarget(HomeShowerWashRegion region, out HomeShowerSoapTarget target)
        {
            target = default;
            if (!IsInitialized) return false;
            RefreshSurfaces();
            Vector3 eye = registry.Anchors.Mouth.position + Vector3.up * 0.068f;
            float best = float.PositiveInfinity;
            foreach (Surface surface in surfaces)
            {
                if (surface.Region != region || !CanPick(surface)) continue;
                for (int index = 0; index < surface.Triangles.Length; index += 3)
                {
                    Vector3 a = surface.Vertices[surface.Triangles[index]], b = surface.Vertices[surface.Triangles[index + 1]], c = surface.Vertices[surface.Triangles[index + 2]];
                    Vector3 point = (a + b + c) / 3f;
                    if (!IsInLookCone(point, eye)) continue;
                    Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                    if (Vector3.Dot(normal, eye - point) < 0f) normal = -normal;
                    if (!CanContactPatch(surface, normal)) continue;
                    float height = Mathf.InverseLerp(surface.Bounds.min.y, surface.Bounds.max.y, point.y);
                    bool pelvis = surface.Renderer.name == "GEO_Pelvis";
                    if (region == HomeShowerWashRegion.Intimate &&
                        (Vector3.Dot(normal, actor.forward) < 0.65f || Mathf.Abs(Vector3.Dot(normal, actor.up)) > 0.65f ||
                         (!pelvis && (height < 0.20f || height > 0.80f ||
                                      !surface.Renderer.name.EndsWith("_Skin", StringComparison.Ordinal))))) continue;
                    float anatomyAlong = surface.Anchor.InverseTransformPoint(point).z;
                    if (region == HomeShowerWashRegion.Intimate && surface.Renderer.name == "Anatomy_Skin" &&
                        (anatomyAlong < HomeToiletFirstPersonView.AnatomyShaftLengthMetres * 0.45f ||
                         anatomyAlong > HomeToiletFirstPersonView.AnatomyShaftLengthMetres * 0.85f)) continue;
                    if (region == HomeShowerWashRegion.Torso &&
                        (height < 0.25f || height > 0.80f || Vector3.Dot(normal, actor.forward) < 0.45f)) continue;
                    // Pick exposed skin, not the closed mesh's joint cap.
                    // Lower legs are rejected by the arm's physical reach.
                    Transform shoulder = registry.TryGetPart(Player3DAnatomicalPart.RightUpperArm, out var binding) ? binding.Bone : actor;
                    float reach = Vector3.Distance(shoulder.position, point + normal * (SoapHalfThickness + hands[0].PalmToSoap));
                    if (reach > 0.62f) continue;
                    Vector3 relative = actor.InverseTransformPoint(point);
                    float score = reach + Mathf.Abs(relative.x) * (region == HomeShowerWashRegion.Torso ? 0.4f : 0f);
                    if (region == HomeShowerWashRegion.LeftArm)
                        score += surface.Renderer.name.Contains("Forearm", StringComparison.Ordinal) ? 0f : 0.25f;
                    if (IsLeg(region)) score += (1f - Mathf.Max(0f, Vector3.Dot(normal, actor.forward))) * 0.05f;
                    // Distance alone picks the neck or an attachment root
                    // under the belly. Aim the diagnostic pointer at a broad
                    // middle patch that can support an actual rubbing stroke.
                    if (region == HomeShowerWashRegion.Torso) score += Mathf.Abs(height - 0.60f) * 0.35f;
                    if (region == HomeShowerWashRegion.Intimate)
                        score += surface.Renderer.name == "Anatomy_Skin"
                            ? Mathf.Abs(anatomyAlong - HomeToiletFirstPersonView.AnatomyShaftLengthMetres * 0.65f) * 0.5f
                            : (pelvis ? 0.5f : 0.25f) + Mathf.Abs(height - 0.50f) * 0.35f;
                    if (score >= best) continue;
                    var ray = new Ray(eye, point - eye);
                    if (!TryPickBodyCached(ray, out HomeShowerSoapTarget visible) || visible.Region != region ||
                        Vector3.Distance(visible.WorldPoint, point) > 0.025f) continue;
                    // Overlapping attachments must not substitute a nearby
                    // neck/root triangle for the exterior patch just checked.
                    if (region == HomeShowerWashRegion.Intimate &&
                        (visible.SurfaceName != surface.Renderer.name || Vector3.Distance(visible.WorldPoint, point) > 0.002f ||
                         Vector3.Dot(visible.WorldNormal, normal) < 0.995f)) continue;
                    best = score;
                    target = visible;
                }
            }
            return target.IsValid;
        }

        public bool IsInLookCone(Vector3 point)
        {
            return registry != null && IsInLookCone(point,
                registry.Anchors.Mouth.position + Vector3.up * HomeShowerFirstPersonView.EyeHeightAboveMouth);
        }

        private bool IsInLookCone(Vector3 point, Vector3 eye)
        {
            Vector3 direction = actor.InverseTransformDirection(point - eye);
            float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Atan2(-direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;
            // A low point behind the bent head has two equivalent Euler
            // descriptions: yaw 180/pitch 75 and yaw 0/pitch 105. It remains
            // visible near the lower edge of the allowed downward view. Test the
            // actual frustum after clamping both descriptions, not yaw alone.
            float otherYaw = Mathf.DeltaAngle(180f, yaw);
            return IsInsideClampedFrame(direction, yaw, pitch) ||
                IsInsideClampedFrame(direction, otherYaw, 180f - pitch);
        }

        private bool IsInsideClampedFrame(Vector3 direction, float yaw, float pitch)
        {
            yaw = Mathf.Clamp(yaw, -HomeShowerFirstPersonView.MaximumLookYawDegrees, HomeShowerFirstPersonView.MaximumLookYawDegrees);
            pitch = Mathf.Clamp(pitch,
                HomeShowerSceneTimeline.WashPitchDegrees + HomeShowerFirstPersonView.MinimumLookPitchDegrees,
                HomeShowerSceneTimeline.WashPitchDegrees + HomeShowerFirstPersonView.MaximumLookPitchDegrees);
            Vector3 cameraDirection = Quaternion.Inverse(Quaternion.Euler(pitch, yaw, 0f)) * direction;
            if (cameraDirection.z <= 0.001f) return false;
            float aspect = eyeCamera != null ? eyeCamera.aspect : 16f / 9f;
            float vertical = Mathf.Tan((HomeShowerFirstPersonView.FieldOfView * 0.5f - 3f) * Mathf.Deg2Rad);
            return Mathf.Abs(cameraDirection.y / cameraDirection.z) <= vertical &&
                Mathf.Abs(cameraDirection.x / cameraDirection.z) <= vertical * aspect;
        }

        private bool TryPickBodyCached(Ray ray, out HomeShowerSoapTarget target)
        {
            target = default;
            float nearest = float.PositiveInfinity;
            foreach (Surface surface in surfaces)
            {
                if (!CanPick(surface)) continue;
                if (!surface.Bounds.IntersectRay(ray)) continue;
                for (int index = 0; index < surface.Triangles.Length; index += 3)
                {
                    Vector3 a = surface.Vertices[surface.Triangles[index]], b = surface.Vertices[surface.Triangles[index + 1]], c = surface.Vertices[surface.Triangles[index + 2]];
                    if (!RayTriangle(ray, a, b, c, out float distance) || distance >= nearest) continue;
                    Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                    if (Vector3.Dot(normal, ray.direction) > 0f) normal = -normal;
                    nearest = distance;
                    // An inaccessible near cap still occludes farther skin;
                    // rejecting contact must never turn into picking through it.
                    target = CanContactPatch(surface, normal)
                        ? new HomeShowerSoapTarget(surface.Region, surface.Anchor, ray.GetPoint(distance), normal, surface.Renderer.name)
                        : default;
                }
            }
            return target.IsValid;
        }

        private static bool IsLeg(HomeShowerWashRegion region)
        {
            return region == HomeShowerWashRegion.LeftLeg || region == HomeShowerWashRegion.RightLeg;
        }

        private bool CanContactPatch(Surface surface, Vector3 normal)
        {
            // These separate closed body meshes have top/bottom joint caps.
            // Their nearly vertical normal sends a palm into an adjacent part;
            // only their exposed front and lateral skin is a washing surface.
            bool capped = IsLeg(surface.Region) || surface.Renderer.name == "GEO_Torso" || surface.Renderer.name == "GEO_Pelvis";
            return !capped || (Mathf.Abs(Vector3.Dot(normal, actor.up)) < 0.65f &&
                Vector3.Dot(normal, actor.forward) >= -0.10f);
        }

        private Vector3 ApproachDirection(HomeShowerSoapTarget target)
        {
            Vector3 normal = target.WorldNormal;
            if (!IsLeg(target.Region) && target.Region != HomeShowerWashRegion.Intimate) return normal;
            // Keep a transfer in front of the hanging thigh instead of
            // travelling upward under the pelvis when a facet tilts upward.
            return (Vector3.ProjectOnPlane(normal, actor.up).normalized + actor.forward * 0.75f).normalized;
        }

        private bool StepHand(Hand hand, Vector3 target, Quaternion frame, float dt)
        {
            long startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                Vector3 next = Vector3.MoveTowards(hand.Current, target, HandTravelSpeed * dt);
                Quaternion nextFrame = Quaternion.RotateTowards(hand.Frame, frame, 220f * dt);
                bool safe = PlaceHand(hand, next, nextFrame);
                return safe && Vector3.Distance(hand.Current, target) <= ContactTolerance && Quaternion.Angle(hand.Frame, frame) < 3f;
            }
            finally
            {
                double milliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - startedAt) *
                    (1000d / System.Diagnostics.Stopwatch.Frequency);
                ContactSolveCount++;
                ContactSolveTotalMilliseconds += milliseconds;
                ContactSolveMaxMilliseconds = System.Math.Max(ContactSolveMaxMilliseconds, milliseconds);
            }
        }

        private bool PlaceHand(Hand hand, Vector3 target, Quaternion frame)
        {
            bool accepted = hand.Guard.ApplyWorldContact(target, frame);
            hand.Current = hand.Effector.position;
            hand.Frame = hand.Guard.PhysicalHandFrame;
            HandError = hand.Guard.ContactError;
            return accepted;
        }

        private void FollowSoap()
        {
            if (!HasSoap) return;
            Hand hand = hands[0];
            Vector3 normal = hand.Frame * Vector3.right;
            Vector3 fingers = hand.Frame * Vector3.forward;
            soap.SetPositionAndRotation(hand.Effector.position, Quaternion.LookRotation(fingers, normal));
        }

        // The feet moved back 10 cm; keep the held bar at its previous
        // world-forward clearance, ahead of the more deeply bent torso.
        private Vector3 HoverPoint() => actor.position + actor.up * 1.20f +
            actor.forward * 0.56f + actor.right * 0.11f;
        private static Quaternion FrameForContact(Vector3 normal, Vector3 fingers, bool left)
        {
            fingers = Vector3.ProjectOnPlane(fingers, normal).normalized;
            if (fingers.sqrMagnitude < 0.001f) fingers = Vector3.ProjectOnPlane(Vector3.forward, normal).normalized;
            Vector3 right = left ? -normal : normal;
            Vector3 thumb = Vector3.Cross(fingers, right).normalized;
            return Quaternion.LookRotation(fingers, thumb);
        }

        private void ConfigureGuardBody(Hand hand)
        {
            var body = new List<Renderer>();
            foreach (Surface surface in surfaces)
            {
                if (surface.Renderer == null || !surface.Renderer.enabled || !surface.Renderer.gameObject.activeInHierarchy) continue;
                if (surface.Region == (hand.Left ? HomeShowerWashRegion.LeftArm : HomeShowerWashRegion.RightArm)) continue;
                body.Add(surface.Renderer);
            }
            hand.Guard.ConfigureContactBody(body);
        }

        private static bool CanPick(Surface surface)
        {
            // Soap stays in the right hand. No part of that arm is a washing
            // target; its meshes remain available to the body collision guard.
            return HomeShowerWashingProgress.IsWashableRegion(surface.Region) &&
                surface.Renderer != null && surface.Renderer.enabled && surface.Renderer.gameObject.activeInHierarchy;
        }

        private void AddAttachment(Transform attachment)
        {
            if (attachment == null) return;
            foreach (Renderer renderer in attachment.GetComponentsInChildren<Renderer>(true))
                surfaces.Add(new Surface { Renderer = renderer, Anchor = attachment, Region = HomeShowerWashRegion.Intimate });
        }

        private static HomeShowerWashRegion RegionFor(string name)
        {
            if (name == "GEO_Torso") return HomeShowerWashRegion.Torso;
            if (name == "GEO_Pelvis") return HomeShowerWashRegion.Intimate;
            if (name.StartsWith("GEO_UpperArm", StringComparison.Ordinal) || name.StartsWith("GEO_Forearm", StringComparison.Ordinal) ||
                name.StartsWith("GEO_Hand", StringComparison.Ordinal) || name.StartsWith("GEO_Thumb", StringComparison.Ordinal))
                return name.EndsWith(".L", StringComparison.Ordinal) ? HomeShowerWashRegion.LeftArm : HomeShowerWashRegion.RightArm;
            if (name.StartsWith("GEO_Thigh", StringComparison.Ordinal) || name.StartsWith("GEO_Shin", StringComparison.Ordinal))
                return name.EndsWith(".L", StringComparison.Ordinal) ? HomeShowerWashRegion.LeftLeg : HomeShowerWashRegion.RightLeg;
            return HomeShowerWashRegion.None;
        }

        private void RefreshSurfaces(HomeShowerSoapTarget? selected = null)
        {
            foreach (Surface surface in surfaces)
            {
                // A held stroke reprojects only onto its selected skin. Picking
                // a new point still refreshes every surface for full occlusion.
                if (selected.HasValue && (surface.Anchor != selected.Value.Anchor ||
                    surface.Renderer.name != selected.Value.SurfaceName)) continue;
                vertices.Clear();
                Mesh source;
                if (surface.Renderer is SkinnedMeshRenderer skinned)
                {
                    sample.Clear(false);
                    skinned.BakeMesh(sample, true);
                    source = sample;
                }
                else
                {
                    MeshFilter filter = surface.Renderer != null ? surface.Renderer.GetComponent<MeshFilter>() : null;
                    if (filter?.sharedMesh == null) { surface.Vertices = Array.Empty<Vector3>(); surface.Triangles = Array.Empty<int>(); continue; }
                    source = filter.sharedMesh;
                }
                source.GetVertices(vertices);
                Matrix4x4 matrix = surface.Renderer.transform.localToWorldMatrix;
                if (surface.Vertices == null || surface.Vertices.Length != vertices.Count)
                {
                    surface.Vertices = new Vector3[vertices.Count]; surface.Triangles = source.triangles;
                }
                for (int index = 0; index < vertices.Count; index++) surface.Vertices[index] = matrix.MultiplyPoint3x4(vertices[index]);
                surface.Bounds = new Bounds(surface.Vertices.Length > 0 ? surface.Vertices[0] : Vector3.zero, Vector3.zero);
                foreach (Vector3 vertex in surface.Vertices) surface.Bounds.Encapsulate(vertex);
            }
        }

        private static bool RayTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float distance)
        {
            distance = 0f;
            Vector3 edge1 = b - a, edge2 = c - a, cross = Vector3.Cross(ray.direction, edge2);
            float determinant = Vector3.Dot(edge1, cross);
            if (Mathf.Abs(determinant) < 0.00000001f) return false;
            float inverse = 1f / determinant;
            Vector3 relative = ray.origin - a;
            float u = Vector3.Dot(relative, cross) * inverse;
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(relative, edge1);
            float v = Vector3.Dot(ray.direction, q) * inverse;
            if (v < 0f || u + v > 1f) return false;
            distance = Vector3.Dot(edge2, q) * inverse;
            return distance > 0.00001f;
        }

        private void ResetCredit()
        {
            IsContacting = false; ContactTravelMetres = 0f; CurrentRegion = HomeShowerWashRegion.None;
        }
        private void RestoreSoap()
        {
            if (soap == null) return;
            soap.SetParent(soapParent, false);
            soap.localPosition = soapRestPosition; soap.localRotation = soapRestRotation; soap.localScale = soapRestScale;
        }
        private static float Smooth(float value)
        {
            float t = Mathf.Clamp01(value); return t * t * (3f - 2f * t);
        }
        private static float TransferDeltaTime() => PauseMenuController.IsAnyPaused ? 0f : Mathf.Clamp(Time.deltaTime, 0f, 0.05f);
        public void End()
        {
            if (started)
                foreach (Hand hand in hands) hand.Guard?.End();
            RestoreSoap();
            wash?.ResetPassiveSoapContact();
            started = HasSoap = IsTransferComplete = wasContact = false;
            routeStage = 0;
            ResetCredit();
        }
        private void OnDisable() => End();
        private void OnDestroy()
        {
            End();
            ReleasePreparation();
        }
        private void ReleasePreparation()
        {
            if (sample != null) { if (Application.isPlaying) Destroy(sample); else DestroyImmediate(sample); }
            sample = null;
            soapMesh = null; soapVertices = null; soapTriangles = null;
            foreach (Hand hand in hands)
            {
                if (hand.Effector != null) { if (Application.isPlaying) Destroy(hand.Effector.gameObject); else DestroyImmediate(hand.Effector.gameObject); }
                if (hand.Guard != null) { if (Application.isPlaying) Destroy(hand.Guard); else DestroyImmediate(hand.Guard); }
                hand.Effector = null; hand.Guard = null;
            }
            registry = null;
            surfaces.Clear();
        }
    }
}
