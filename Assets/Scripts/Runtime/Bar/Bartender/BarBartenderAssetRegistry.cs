using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum BarBartenderClipKind
    {
        Wipe = 0, Walk = 1, Pour = 2, Notice = 3, ServiceStep = 4
    }

    /// <summary>
    /// Scene-local service bindings over a population-owned default character.
    /// The population keeps the model, face and wardrobe; this adapter only
    /// lends the common rig to the bar's existing service clock.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BarBartenderAssetRegistry : MonoBehaviour
    {
        public const float WipeMenuClearance = .015f;
        private VillageResidentPresentation actor;
        private MountainRoadCafeCastAssetRegistry serviceClips;
        private CityPedestrianHandPropRegistry serviceTowel;
        private Renderer[] renderers = Array.Empty<Renderer>();
        private Vector3[] towelBoundsInGrip = Array.Empty<Vector3>();
        private Vector3[] towelVerticesInRoot = Array.Empty<Vector3>();
        private Vector3 towelPocketCentreInPelvis;
        private Quaternion towelPocketHandRotationInPelvis;
        private Vector3 towelRearPlaneInPelvis, towelRearNormalInPelvis;
        private Plane wipeSurface;
        private bool hasWipeSurface;
        private Bounds counterSurfaceBounds;
        private Bounds wipeStrokeInActor;
        private Vector3[] wipeShoulderFromClavicle = Array.Empty<Vector3>();
        private Vector3[] wipeWristFromClavicle = Array.Empty<Vector3>();
        private bool hasWipeStrokeBounds;
        private BarDrinkServiceView serviceView;

        public VillageResidentPresentation Actor => actor;
        public Animator Animator => actor.Animator;
        public Transform ModelRoot => actor.ModelRoot;
        public IReadOnlyList<Renderer> Renderers => renderers;
        public CityPedestrianHandPropRegistry ServiceTowel => serviceTowel;
        public NpcHandPose HandPose { get; private set; }
        public Transform Pelvis { get; private set; }
        public Transform Spine { get; private set; }
        public Transform Chest { get; private set; }
        public Transform Neck { get; private set; }
        public Transform Head => actor.Head;
        public Transform FaceEyeLeft { get; private set; }
        public Transform FaceEyeRight { get; private set; }
        public Transform LeftClavicle { get; private set; }
        public Transform LeftUpperArm { get; private set; }
        public Transform LeftForearm { get; private set; }
        public Transform LeftHand { get; private set; }
        public Transform RightClavicle { get; private set; }
        public Transform RightUpperArm { get; private set; }
        public Transform RightForearm { get; private set; }
        public Transform RightHand { get; private set; }
        public Transform LeftThigh { get; private set; }
        public Transform LeftShin { get; private set; }
        public Transform LeftFoot { get; private set; }
        public Transform RightThigh { get; private set; }
        public Transform RightShin { get; private set; }
        public Transform RightFoot { get; private set; }
        public Transform LeftGripSocket => actor.LeftGrip;
        public Transform LeftVesselSocket { get; private set; }
        public Transform RightGripSocket => actor.RightGrip;
        public Transform RightBottleSocket { get; private set; }
        public Transform VesselGripAnchor { get; private set; }
        public Transform MenuGripAnchor { get; private set; }
        public Transform BottleGripAnchor { get; private set; }
        public string DesignId { get; private set; }
        public Vector3 TowelCentreInHand { get; private set; }
        // Forward is the cloth's thin axis; up is its longer broad axis.
        public Quaternion TowelPlaneRotationInHand { get; private set; }
        public Vector3 TowelPocketWorldPosition => Pelvis.position +
            Pelvis.rotation * towelPocketCentreInPelvis;
        public bool IsTowelHandBehindBack => Vector3.Dot(
            LeftHand.position - (Pelvis.position + Pelvis.rotation * towelRearPlaneInPelvis),
            Pelvis.rotation * towelRearNormalInPelvis) >= 0f;
        public Bounds TowelWorldBounds
        {
            get
            {
                var bounds = new Bounds(serviceTowel.transform.TransformPoint(towelVerticesInRoot[0]), Vector3.zero);
                for (int index = 1; index < towelVerticesInRoot.Length; index++)
                    bounds.Encapsulate(serviceTowel.transform.TransformPoint(towelVerticesInRoot[index]));
                return bounds;
            }
        }
        public float TowelWipeContactError => hasWipeSurface
            ? Mathf.Abs(ResolveLowestTowelWipeCorner()) : float.PositiveInfinity;
        /// <summary>The mounted cloth's conservative renderer footprint used for wood and menu contact.</summary>
        public Bounds TowelWipeWorldBounds
        {
            get
            {
                var bounds = new Bounds(LeftGripSocket.TransformPoint(towelBoundsInGrip[0]), Vector3.zero);
                for (int index = 1; index < towelBoundsInGrip.Length; index++)
                    bounds.Encapsulate(LeftGripSocket.TransformPoint(towelBoundsInGrip[index]));
                return bounds;
            }
        }
        public bool HasWipeStrokeBounds => hasWipeStrokeBounds;
        public bool HasSafeWipePatch => !hasWipeStrokeBounds ||
            TryResolveMenuClearanceOffset(ResolveWipeStrokeBounds(), true, out _);
        public Vector3 WipeStrokeOffset => hasWipeStrokeBounds &&
            TryResolveMenuClearanceOffset(ResolveWipeStrokeBounds(), true, out Vector3 offset) ? offset : Vector3.zero;
        public Bounds WipeStrokeWorldBounds
        {
            get
            {
                Bounds bounds = ResolveWipeStrokeBounds();
                if (TryResolveMenuClearanceOffset(bounds, true, out Vector3 offset)) bounds.center += offset;
                return bounds;
            }
        }
        internal bool RightHandCarriesBottle => serviceView != null && serviceView.IsCarriedBottleVisible;
        internal bool HasCounterSurface => hasWipeSurface;

        public void Configure(VillageResidentPresentation character)
        {
            actor = character != null ? character
                : throw new ArgumentNullException(nameof(character));
            DesignId = DefaultNpcPopulation.GetAssignment(DefaultNpcPopulation.BarBartender).ModelId;
            // Exactly one graph drives this Animator. Appearance and wardrobe
            // stay enabled, and this adapter never rewrites their MPBs.
            actor.ReleaseAnimation();
            var provider = MountainRoadCafeCastProvider.Load();
            serviceClips = provider != null && provider.AttendantPrefab != null
                ? provider.AttendantPrefab.GetComponent<MountainRoadCafeCastAssetRegistry>()
                : null;
            if (serviceClips == null)
                throw new InvalidOperationException("The shared cafe service clips are missing.");

            Transform Require(string name) =>
                CityPedestrianHandProps.FindSocket(ModelRoot, name)
                ?? throw new InvalidOperationException("Default bartender lost joint/socket " + name);
            Pelvis = Require("pelvis"); Spine = Require("spine");
            Chest = Require("chest"); Neck = Require("neck");
            FaceEyeLeft = Require("face.eye.L"); FaceEyeRight = Require("face.eye.R");
            LeftClavicle = Require("clavicle.L");
            LeftUpperArm = Require("upper_arm.L"); LeftForearm = Require("forearm.L");
            LeftHand = Require("hand.L");
            RightClavicle = Require("clavicle.R");
            RightUpperArm = Require("upper_arm.R"); RightForearm = Require("forearm.R");
            RightHand = Require("hand.R");
            LeftThigh = Require("thigh.L"); LeftShin = Require("shin.L"); LeftFoot = Require("foot.L");
            RightThigh = Require("thigh.R"); RightShin = Require("shin.R"); RightFoot = Require("foot.R");
            LeftVesselSocket = Require("SOCKET_Vessel.L");
            RightBottleSocket = Require("SOCKET_Bottle.R");
            HandPose = actor.GetComponent<NpcHandPose>();
            if (HandPose == null)
                throw new InvalidOperationException("The default bartender needs its shared authored hand grips.");
            VesselGripAnchor = CreateAnatomicalGrip(LeftHand, true,
                "Bar Left Vessel Grasp Centre");
            MenuGripAnchor = CreateMenuGrip();
            BottleGripAnchor = CreateAnatomicalGrip(RightHand, false,
                "Bar Right Bottle Grasp Centre");
            renderers = actor.GetComponentsInChildren<Renderer>(true);
            Material shared = renderers.Length > 0 ? renderers[0].sharedMaterial : null;
            serviceTowel = CityPedestrianHandProps.Attach(
                LeftGripSocket, CityPedestrianHandPropId.ServiceTowel, shared, 0);
            CacheTowelBounds();
            CacheTowelPocket();
            NpcSkinnedMeshCullingGuard.EnableDynamicBounds(ModelRoot);
            foreach (BarBartenderClipKind kind in Enum.GetValues(typeof(BarBartenderClipKind)))
                if (!TryGetClip(kind, out _, out _))
                    throw new InvalidOperationException("Default bartender lost shared service clip " + kind);
        }

        private Transform CreateAnatomicalGrip(Transform hand, bool isLeft, string name)
        {
            var grip = new GameObject(name).transform;
            grip.SetPositionAndRotation(HandPose.AnatomicalCylinderCentre(isLeft), hand.rotation);
            grip.SetParent(hand, true);
            return grip;
        }

        private Transform CreateMenuGrip()
        {
            AnatomicalHandFrame frame = HandPose.GetAnatomicalFrame(true);
            Quaternion supportHand = frame.ResolveHandRotation(transform.up, transform.forward);
            Quaternion booklet = Quaternion.LookRotation(-transform.forward, transform.up);
            var grip = new GameObject("Bar Left Menu Edge Grasp").transform;
            grip.SetPositionAndRotation(VesselGripAnchor.position,
                LeftHand.rotation * Quaternion.Inverse(supportHand) * booklet);
            grip.SetParent(LeftHand, true);
            return grip;
        }

        public void ConfigureGripKind(bool isLeft, NpcHandGripKind kind)
        {
            Transform hand = isLeft ? LeftHand : RightHand;
            Transform grip = isLeft ? VesselGripAnchor : BottleGripAnchor;
            AnatomicalHandFrame frame = HandPose.GetAnatomicalFrame(isLeft, kind);
            grip.position = hand.position + hand.rotation * frame.CylinderCentreInHand;
        }

        /// <summary>Bind the imported counter's measured top after the room is placed.</summary>
        public void ConfigureWipeSurface(Renderer counterSurface)
        {
            if (counterSurface == null)
                throw new ArgumentNullException(nameof(counterSurface));
            Bounds bounds = counterSurface.bounds;
            counterSurfaceBounds = bounds;
            wipeSurface = new Plane(Vector3.up,
                new Vector3(bounds.center.x, bounds.max.y, bounds.center.z));
            hasWipeSurface = true;
        }

        // The service view already owns and caches the real closed menu's
        // renderers. Share that measured obstacle with ordinary counter work.
        public void ConfigureServiceView(BarDrinkServiceView view) => serviceView = view;

        internal bool TryResolveWorkPatchTarget(int patchIndex, out Vector3 rootWorldPosition)
        {
            rootWorldPosition = transform.position;
            if (!hasWipeSurface || !hasWipeStrokeBounds) return false;
            Bounds stroke = ResolveWipeStrokeBounds();
            bool alongX = counterSurfaceBounds.size.x >= counterSurfaceBounds.size.z;
            float root = alongX ? transform.position.x : transform.position.z;
            float minimum = (alongX ? counterSurfaceBounds.min.x - stroke.min.x
                : counterSurfaceBounds.min.z - stroke.min.z) + root + WipeMenuClearance;
            float maximum = (alongX ? counterSurfaceBounds.max.x - stroke.max.x
                : counterSurfaceBounds.max.z - stroke.max.z) + root - WipeMenuClearance;
            float bodyRadius = Vector3.Distance(LeftUpperArm.position, RightUpperArm.position) * .7f;
            minimum = Mathf.Max(minimum, (alongX ? counterSurfaceBounds.min.x : counterSurfaceBounds.min.z) + bodyRadius);
            maximum = Mathf.Min(maximum, (alongX ? counterSurfaceBounds.max.x : counterSurfaceBounds.max.z) - bodyRadius);
            if (maximum - minimum < .5f) return false;
            float fraction = patchIndex == 0 ? .25f : patchIndex == 1 ? .75f : .5f;
            float target = Mathf.Lerp(minimum, maximum, fraction);
            Vector3 offset = (alongX ? Vector3.right : Vector3.forward) * (target - root);
            Bounds moved = stroke;
            moved.center += offset;
            if (!TryResolveMenuClearanceOffset(moved, true, out _)) return false;
            rootWorldPosition += offset;
            return true;
        }

        internal bool TryGetCounterSurfaceHeight(out float height)
        { height = counterSurfaceBounds.max.y; return hasWipeSurface; }

        internal void CacheWipeStrokeBounds(Bounds actorLocalBounds,
            Vector3[] shoulderFromClavicle, Vector3[] wristFromClavicle)
        {
            wipeStrokeInActor = actorLocalBounds;
            wipeShoulderFromClavicle = shoulderFromClavicle;
            wipeWristFromClavicle = wristFromClavicle;
            hasWipeStrokeBounds = true;
        }

        private Bounds ResolveWipeStrokeBounds()
        {
            if (!hasWipeStrokeBounds) return default;
            Bounds world = new Bounds(transform.TransformPoint(wipeStrokeInActor.center), Vector3.zero);
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 signs = new Vector3((corner & 1) == 0 ? -1f : 1f,
                    (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                world.Encapsulate(transform.TransformPoint(wipeStrokeInActor.center +
                    Vector3.Scale(wipeStrokeInActor.extents, signs)));
            }
            return world;
        }

        private bool TryResolveMenuClearanceOffset(Bounds footprint, bool checkArmReach, out Vector3 offset)
        {
            offset = Vector3.zero;
            if (!hasWipeSurface || serviceView == null ||
                !serviceView.TryGetCounterMenuBounds(out Bounds menu)) return true;
            if (footprint.max.x < menu.min.x - WipeMenuClearance ||
                footprint.min.x > menu.max.x + WipeMenuClearance ||
                footprint.max.z < menu.min.z - WipeMenuClearance ||
                footprint.min.z > menu.max.z + WipeMenuClearance) return true;

            // Move the entire cached stroke to one side of the book. Its
            // offset depends on the fixed sweep envelope, never the current
            // hand position, so alternate strokes cannot switch book sides.
            float bestDistance = float.PositiveInfinity;
            for (int axis = 0; axis < 2; axis++)
            {
                bool alongX = axis == 0;
                float strokeMin = alongX ? footprint.min.x : footprint.min.z;
                float strokeMax = alongX ? footprint.max.x : footprint.max.z;
                float menuMin = alongX ? menu.min.x : menu.min.z;
                float menuMax = alongX ? menu.max.x : menu.max.z;
                for (int side = 0; side < 2; side++)
                {
                    float distance = side == 0 ? menuMin - WipeMenuClearance - strokeMax
                        : menuMax + WipeMenuClearance - strokeMin;
                    Vector3 candidate = (alongX ? Vector3.right : Vector3.forward) * distance;
                    Bounds moved = footprint;
                    moved.center += candidate;
                    if (moved.min.x < counterSurfaceBounds.min.x - .0001f || moved.max.x > counterSurfaceBounds.max.x + .0001f ||
                        moved.min.z < counterSurfaceBounds.min.z - .0001f || moved.max.z > counterSurfaceBounds.max.z + .0001f ||
                        (checkArmReach && !CanReachWipeStroke(candidate)) || Mathf.Abs(distance) >= bestDistance) continue;
                    offset = candidate;
                    bestDistance = Mathf.Abs(distance);
                }
            }
            return float.IsFinite(bestDistance);
        }

        private bool CanReachWipeStroke(Vector3 offset)
        {
            float reach = LimbTwoBoneIk.ChainLength(LeftUpperArm, LeftForearm, LeftHand) * .98f;
            for (int index = 0; index < wipeWristFromClavicle.Length; index++)
            {
                Vector3 shoulder = transform.TransformVector(wipeShoulderFromClavicle[index]);
                Vector3 target = transform.TransformVector(wipeWristFromClavicle[index]) + offset;
                if ((target - shoulder).sqrMagnitude <= reach * reach) continue;
                Vector3 axis = Vector3.Cross(shoulder, target);
                if (axis.sqrMagnitude < .000001f) return false;
                // Match the actual protraction direction and limit. A
                // spherical allowance can admit an unreachable outward wipe
                // and let the solver shorten it back onto the book.
                float angle = Mathf.Min(BarBartenderPresentation.MaximumClavicleReachDegrees,
                    Vector3.Angle(shoulder, target));
                Vector3 guided = Quaternion.AngleAxis(angle, axis.normalized) * shoulder;
                if ((target - guided).sqrMagnitude > reach * reach) return false;
            }
            return true;
        }

        internal bool TryResolveCounterSupportTarget(Vector3 desired, float palmRadius, out Vector3 target)
        {
            target = desired;
            if (!hasWipeSurface) return false;
            target.x = Mathf.Clamp(target.x, counterSurfaceBounds.min.x + palmRadius,
                counterSurfaceBounds.max.x - palmRadius);
            target.z = Mathf.Clamp(target.z, counterSurfaceBounds.min.z + palmRadius,
                counterSurfaceBounds.max.z - palmRadius);
            target.y = counterSurfaceBounds.max.y;
            if (!TryResolveMenuClearanceOffset(new Bounds(target,
                new Vector3(palmRadius * 2f, 0f, palmRadius * 2f)), false, out Vector3 offset)) return false;
            target += offset;
            return true;
        }

        private void CacheTowelBounds()
        {
            towelBoundsInGrip = new Vector3[serviceTowel.Renderers.Count * 8];
            var vertices = new List<Vector3>();
            Quaternion inverseHand = Quaternion.Inverse(LeftHand.rotation);
            bool hasPlane = false;
            int point = 0;
            foreach (Renderer renderer in serviceTowel.Renderers)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                    throw new InvalidOperationException("The shared service towel needs its rigid mesh.");
                Bounds bounds = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 signs = new Vector3((corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                    Vector3 local = bounds.center + Vector3.Scale(bounds.extents, signs);
                    towelBoundsInGrip[point++] = LeftGripSocket.InverseTransformPoint(
                        renderer.transform.TransformPoint(local));
                }
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                {
                    Vector3 world = renderer.transform.TransformPoint(vertex);
                    vertices.Add(serviceTowel.transform.InverseTransformPoint(world));
                    TowelCentreInHand += inverseHand * (world - LeftHand.position);
                }
                if (!hasPlane)
                {
                    Vector3 size = bounds.size;
                    int thin = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
                    int longest = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                    Vector3 normal = inverseHand * renderer.transform.TransformVector(Axis(thin));
                    Vector3 up = inverseHand * renderer.transform.TransformVector(Axis(longest));
                    TowelPlaneRotationInHand = Quaternion.LookRotation(normal.normalized,
                        Vector3.ProjectOnPlane(up, normal).normalized);
                    hasPlane = true;
                }
            }
            towelVerticesInRoot = vertices.ToArray();
            TowelCentreInHand /= vertices.Count;
        }

        private static Vector3 Axis(int index) => index == 0 ? Vector3.right
            : index == 1 ? Vector3.up : Vector3.forward;

        private void CacheTowelPocket()
        {
            var clothingPoints = new List<Vector3>();
            Bounds trousers = default;
            bool foundTrousers = false;
            Quaternion inverseActor = Quaternion.Inverse(transform.rotation);
            foreach (Renderer candidate in renderers)
            {
                if (!(candidate is SkinnedMeshRenderer renderer) || !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy) continue;
                bool isTrousers = renderer.name.StartsWith("CLO_Trousers_", StringComparison.Ordinal) &&
                    renderer.name.EndsWith("Upper", StringComparison.Ordinal);
                bool isCoat = renderer.name.StartsWith("CLO_Outer_", StringComparison.Ordinal) &&
                    renderer.name.EndsWith("Body", StringComparison.Ordinal);
                if (!isTrousers && !isCoat) continue;
                foreach (Vector3 world in MeasureInitialGarmentVertices(renderer))
                {
                    Vector3 local = inverseActor * (world - transform.position);
                    clothingPoints.Add(local);
                    if (!isTrousers) continue;
                    if (!foundTrousers) { trousers = new Bounds(local, Vector3.zero); foundTrousers = true; }
                    else trousers.Encapsulate(local);
                }
            }
            if (!foundTrousers || clothingPoints.Count == 0)
                throw new InvalidOperationException("The bartender's rear towel dock needs its actual trouser and coat surfaces.");

            // The user permits disappearance/appearance once the actual hand
            // reaches behind the back. Measure that plane and the reachable
            // dock once; clothing geometry is not retained or skinned again.
            float left = trousers.center.x - trousers.extents.x * .5f;
            float mouthHeight = trousers.max.y - .05f;
            Quaternion bestHand = default;
            float bestScore = float.NegativeInfinity;
            float halfHeight = 0f;
            for (int normalSign = -1; normalSign <= 1; normalSign += 2)
                for (int upSign = -1; upSign <= 1; upSign += 2)
                {
                    Quaternion hand = Quaternion.LookRotation(transform.forward * normalSign,
                        transform.up * upSign) * Quaternion.Inverse(TowelPlaneRotationInHand);
                    Vector3 wristFromCentre = -(hand * TowelCentreInHand);
                    float score = Vector3.Dot(wristFromCentre, transform.up) -
                        Vector3.Dot(wristFromCentre, transform.forward);
                    if (score <= bestScore) continue;
                    bestScore = score;
                    bestHand = hand;
                    halfHeight = 0f;
                    foreach (Vector3 rootVertex in towelVerticesInRoot)
                    {
                        Vector3 inHand = Quaternion.Inverse(LeftHand.rotation) *
                            (serviceTowel.transform.TransformPoint(rootVertex) - LeftHand.position);
                        halfHeight = Mathf.Max(halfHeight,
                            Mathf.Abs(Vector3.Dot(hand * (inHand - TowelCentreInHand), transform.up)));
                    }
                }
            float rear = trousers.min.z;
            foreach (Vector3 local in clothingPoints)
                if (Mathf.Abs(local.y - mouthHeight) <= .11f)
                    rear = Mathf.Min(rear, local.z);
            Vector3 centre = transform.position + transform.rotation *
                new Vector3(left, mouthHeight - halfHeight, rear - .03f);
            towelPocketCentreInPelvis = Quaternion.Inverse(Pelvis.rotation) * (centre - Pelvis.position);
            towelPocketHandRotationInPelvis = Quaternion.Inverse(Pelvis.rotation) * bestHand;
            Vector3 plane = transform.position + transform.rotation * new Vector3(left, mouthHeight, rear);
            towelRearPlaneInPelvis = Quaternion.Inverse(Pelvis.rotation) * (plane - Pelvis.position);
            towelRearNormalInPelvis = Quaternion.Inverse(Pelvis.rotation) * -transform.forward;
        }

        public Pose ResolveTowelPocketHandPose()
        {
            Quaternion rotation = Pelvis.rotation * towelPocketHandRotationInPelvis;
            return new Pose(TowelPocketWorldPosition - rotation * TowelCentreInHand, rotation);
        }

        private static Vector3[] MeasureInitialGarmentVertices(SkinnedMeshRenderer renderer)
        {
            Mesh mesh = renderer.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Vector3[] delta = null;
            for (int shape = 0; shape < mesh.blendShapeCount; shape++)
            {
                float amount = renderer.GetBlendShapeWeight(shape) * .01f;
                if (Mathf.Abs(amount) <= .00001f) continue;
                if (delta == null) delta = new Vector3[vertices.Length];
                mesh.GetBlendShapeFrameVertices(shape, 0, delta, null, null);
                for (int index = 0; index < vertices.Length; index++)
                    vertices[index] += delta[index] * amount;
            }
            BoneWeight[] weights = mesh.boneWeights;
            Transform[] bones = renderer.bones;
            Matrix4x4[] bindPoses = mesh.bindposes;
            var skin = new Matrix4x4[bones.Length];
            for (int index = 0; index < skin.Length; index++)
                skin[index] = bones[index].localToWorldMatrix * bindPoses[index];
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 vertex = vertices[index];
                BoneWeight weight = weights[index];
                vertices[index] = skin[weight.boneIndex0].MultiplyPoint3x4(vertex) * weight.weight0 +
                    skin[weight.boneIndex1].MultiplyPoint3x4(vertex) * weight.weight1 +
                    skin[weight.boneIndex2].MultiplyPoint3x4(vertex) * weight.weight2 +
                    skin[weight.boneIndex3].MultiplyPoint3x4(vertex) * weight.weight3;
            }
            return vertices;
        }

        internal bool TryResolveWipeHandTarget(out Vector3 target, bool translateStroke = false)
        {
            target = VesselGripAnchor.position;
            if (!hasWipeSurface || towelBoundsInGrip.Length == 0)
                return false;
            float lowest = ResolveLowestTowelWipeCorner();
            Vector3 offset = translateStroke ? WipeStrokeOffset : Vector3.zero;
            if (Mathf.Abs(lowest) < 0.0005f && offset.sqrMagnitude < .00000001f)
                return false;
            target += offset - wipeSurface.normal * lowest;
            return true;
        }

        private float ResolveLowestTowelWipeCorner()
        {
            float lowest = float.PositiveInfinity;
            for (int index = 0; index < towelBoundsInGrip.Length; index++)
                lowest = Mathf.Min(lowest, wipeSurface.GetDistanceToPoint(
                    LeftGripSocket.TransformPoint(towelBoundsInGrip[index])));
            return lowest;
        }

        public bool TryGetClip(BarBartenderClipKind kind, out AnimationClip clip, out bool loop)
        {
            if (kind == BarBartenderClipKind.Walk)
            {
                clip = actor != null ? actor.GetClip(VillageResidentAction.Walk) : null;
                loop = true;
                return clip != null;
            }
            MountainRoadCafeCastClipKind shared = kind switch
            {
                BarBartenderClipKind.Wipe => MountainRoadCafeCastClipKind.Wipe,
                BarBartenderClipKind.ServiceStep => MountainRoadCafeCastClipKind.Walk,
                BarBartenderClipKind.Pour => MountainRoadCafeCastClipKind.Pour,
                _ => MountainRoadCafeCastClipKind.Notice
            };
            clip = null; loop = false;
            return serviceClips != null && serviceClips.TryGetClip(shared, out clip, out loop);
        }

        public void SetServiceTowelVisible(bool visible) => serviceTowel?.SetVisible(visible);

        private void OnEnable() => NpcFootstepSources.Register(transform);
        private void OnDisable() => NpcFootstepSources.Unregister(transform);
    }
}
