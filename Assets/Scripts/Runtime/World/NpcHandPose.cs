using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum NpcHandGripKind { Cylindrical = 0, WineBowl = 1, SnifterBowl = 2, Cigarette = 3 }

    /// <summary>Neutral hand anatomy measured in rotation-only hand space, in metres.</summary>
    public readonly struct AnatomicalHandFrame
    {
        public AnatomicalHandFrame(Vector3 palmCentre, Vector3 fingerDirection,
            Vector3 thumbDirection, Vector3 palmNormal, Vector3 cylinderCentre)
        {
            PalmCentreInHand = palmCentre;
            FingerDirectionInHand = fingerDirection.normalized;
            ThumbDirectionInHand = thumbDirection.normalized;
            PalmNormalInHand = palmNormal.normalized;
            CylinderCentreInHand = cylinderCentre;
        }

        public Vector3 PalmCentreInHand { get; }
        public Vector3 FingerDirectionInHand { get; }
        public Vector3 ThumbDirectionInHand { get; }
        public Vector3 PalmNormalInHand { get; }
        public Vector3 CylinderCentreInHand { get; }

        public Quaternion ResolveHandRotation(Vector3 targetPalmNormal, Vector3 targetDistalAxis)
        {
            Vector3 distal = targetDistalAxis.normalized;
            Vector3 palm = Vector3.ProjectOnPlane(targetPalmNormal, distal).normalized;
            return Quaternion.LookRotation(distal, palm) * Quaternion.Inverse(
                Quaternion.LookRotation(FingerDirectionInHand, PalmNormalInHand));
        }

        public Vector3 ResolveHandPosition(Vector3 targetPalmCentre, Quaternion rotation)
            => targetPalmCentre - rotation * PalmCentreInHand;

        /// <summary>The wrist pose that puts the authored curled fingers around a physical cylinder.</summary>
        public Pose ResolveCylinderPose(Vector3 centre, Vector3 axis, Vector3 palmTowardObject)
        {
            Vector3 cylinderAxis = axis.normalized;
            Vector3 palm = Vector3.ProjectOnPlane(palmTowardObject, cylinderAxis).normalized;
            Quaternion rotation = Quaternion.LookRotation(cylinderAxis, palm) *
                Quaternion.Inverse(Quaternion.LookRotation(ThumbDirectionInHand, PalmNormalInHand));
            return new Pose(centre - rotation * CylinderCentreInHand, rotation);
        }
    }

    /// <summary>Authored hand shapes on the existing rig. Contact owners apply them after sampling a body action.</summary>
    [DisallowMultipleComponent]
    public sealed class NpcHandPose : MonoBehaviour
    {
        [Serializable]
        public sealed class HandBinding
        {
            [SerializeField] private bool isLeft;
            [SerializeField] private Transform hand, gripSocket, centreAnchor, axisAnchor, palmAnchor;
            [SerializeField] private SkinnedMeshRenderer[] renderers;

            public HandBinding(bool isLeft, Transform hand, Transform gripSocket, Transform centreAnchor,
                Transform axisAnchor, Transform palmAnchor, SkinnedMeshRenderer[] renderers)
            {
                this.isLeft = isLeft; this.hand = hand; this.gripSocket = gripSocket;
                this.centreAnchor = centreAnchor; this.axisAnchor = axisAnchor; this.palmAnchor = palmAnchor;
                this.renderers = renderers == null ? null : (SkinnedMeshRenderer[])renderers.Clone();
            }

            public bool IsLeft => isLeft;
            public Transform Hand => hand;
            public Transform GripSocket => gripSocket;
            public Transform CentreAnchor => centreAnchor;
            public Transform AxisAnchor => axisAnchor;
            public Transform PalmAnchor => palmAnchor;
            public IReadOnlyList<SkinnedMeshRenderer> Renderers => renderers;
        }

        [SerializeField] private HandBinding[] hands = Array.Empty<HandBinding>();
        [SerializeField] private string shapeName;
        [SerializeField] private float cylinderRadius;
        private BoundHand left, right;

        public IReadOnlyList<HandBinding> Hands => hands;
        public string ShapeName => shapeName;
        public float CylinderRadius => cylinderRadius;
        public float LeftGripWeight => left?.Weight ?? 0f;
        public float RightGripWeight => right?.Weight ?? 0f;

        public AnatomicalHandFrame GetAnatomicalFrame(bool isLeft)
            => Hand(isLeft).AnatomicalFrame;

        public AnatomicalHandFrame GetAnatomicalFrame(bool isLeft, NpcHandGripKind kind)
            => Hand(isLeft).GetVariantFrame(kind);

        public NpcHandGripKind GetGripKind(bool isLeft) => Hand(isLeft).Kind;

        public static NpcHandGripKind GripKindForVessel(BarDrinkVesselKind vessel)
            => vessel == BarDrinkVesselKind.WineGlass ? NpcHandGripKind.WineBowl
                : vessel == BarDrinkVesselKind.Snifter ? NpcHandGripKind.SnifterBowl
                : NpcHandGripKind.Cylindrical;

        public float GripRadius(NpcHandGripKind kind) => kind switch
        {
            NpcHandGripKind.Cylindrical => cylinderRadius,
            NpcHandGripKind.WineBowl => .037f,
            NpcHandGripKind.SnifterBowl => .046f,
            NpcHandGripKind.Cigarette => .00325f,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        public Vector3 AnatomicalPalmNormal(bool isLeft)
        {
            BoundHand bound = Hand(isLeft);
            return bound.Binding.Hand.rotation * bound.AnatomicalFrame.PalmNormalInHand;
        }

        public Vector3 AnatomicalFingerDirection(bool isLeft)
        {
            BoundHand bound = Hand(isLeft);
            return bound.Binding.Hand.rotation * bound.AnatomicalFrame.FingerDirectionInHand;
        }

        public Vector3 AnatomicalCylinderCentre(bool isLeft)
        {
            BoundHand bound = Hand(isLeft);
            Transform hand = bound.Binding.Hand;
            return hand.position + hand.rotation * bound.GetVariantFrame(bound.Kind).CylinderCentreInHand;
        }

        public void Configure(HandBinding[] bindings, string authoredShapeName, float authoredCylinderRadius)
        {
            if (bindings == null || bindings.Length != 2)
                throw new ArgumentException("A hand-pose catalog requires one binding per hand.", nameof(bindings));
            if (string.IsNullOrWhiteSpace(authoredShapeName)) throw new ArgumentException("An authored hand shape is required.", nameof(authoredShapeName));
            if (!Finite(authoredCylinderRadius) || authoredCylinderRadius <= 0f)
                throw new ArgumentOutOfRangeException(nameof(authoredCylinderRadius));
            var owned = new HashSet<SkinnedMeshRenderer>();
            BoundHand newLeft = null, newRight = null;
            var copy = new HandBinding[bindings.Length];
            for (int i = 0; i < bindings.Length; i++)
            {
                HandBinding binding = bindings[i] ?? throw new ArgumentException("A hand binding is missing.", nameof(bindings));
                copy[i] = new HandBinding(binding.IsLeft, binding.Hand, binding.GripSocket, binding.CentreAnchor,
                    binding.AxisAnchor, binding.PalmAnchor, Copy(binding.Renderers));
                var bound = new BoundHand(copy[i], authoredShapeName, transform, owned);
                if (binding.IsLeft)
                {
                    if (newLeft != null) throw new ArgumentException("Duplicate left hand.", nameof(bindings));
                    newLeft = bound;
                }
                else
                {
                    if (newRight != null) throw new ArgumentException("Duplicate right hand.", nameof(bindings));
                    newRight = bound;
                }
            }
            left?.SetWeight(0f); right?.SetWeight(0f);
            hands = copy; shapeName = authoredShapeName; cylinderRadius = authoredCylinderRadius;
            left = newLeft; right = newRight;
            ResetGrip();
        }

        public void SetGrip(bool isLeft, float weight)
            => SetGrip(isLeft, weight, NpcHandGripKind.Cylindrical);

        public void SetGrip(bool isLeft, float weight, NpcHandGripKind kind)
        {
            if (!Finite(weight)) throw new ArgumentOutOfRangeException(nameof(weight));
            Hand(isLeft).SetManualWeight(Mathf.Clamp01(weight), kind);
        }

        /// <summary>Resolve a finite authored variant once, without changing either hand.</summary>
        public void PrepareGrip(bool isLeft, NpcHandGripKind kind) => Hand(isLeft).Prepare(kind);

        /// <summary>A temporary contact restores only the hand it captured, with its exact prior variant/weight.</summary>
        public bool TryCaptureGrip(UnityEngine.Object owner, bool isLeft, NpcHandGripKind kind)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            return Hand(isLeft).TryCapture(owner, kind);
        }

        public bool SetOwnedGrip(UnityEngine.Object owner, bool isLeft, float weight)
        {
            if (!Finite(weight)) throw new ArgumentOutOfRangeException(nameof(weight));
            return Hand(isLeft).SetOwned(owner, Mathf.Clamp01(weight));
        }

        public bool ReleaseGrip(UnityEngine.Object owner, bool isLeft) => Hand(isLeft).Release(owner);

        public void ResetGrip()
        {
            if (hands == null || hands.Length == 0) return;
            EnsureBindings();
            left.SetManualWeight(0f, NpcHandGripKind.Cylindrical);
            right.SetManualWeight(0f, NpcHandGripKind.Cylindrical);
        }

        public Vector3 CylinderCentre(bool isLeft) => Hand(isLeft).Binding.CentreAnchor.position;
        public Vector3 CylinderAxis(bool isLeft)
        {
            HandBinding binding = Hand(isLeft).Binding;
            return (binding.AxisAnchor.position - binding.CentreAnchor.position).normalized;
        }
        public Vector3 PalmNormal(bool isLeft)
        {
            HandBinding binding = Hand(isLeft).Binding;
            return Vector3.ProjectOnPlane(binding.PalmAnchor.position - binding.CentreAnchor.position,
                CylinderAxis(isLeft)).normalized;
        }

        /// <summary>Returns a socket target and world hand rotation for the authored cylinder contact.</summary>
        public Pose GetSocketPose(bool isLeft, Vector3 centre, Vector3 axis, Vector3 palmNormal)
        {
            if (!Finite(centre) || !Finite(axis) || !Finite(palmNormal) || axis.sqrMagnitude < .0001f)
                throw new ArgumentException("A finite cylinder contact frame is required.");
            Vector3 normal = Vector3.ProjectOnPlane(palmNormal, axis).normalized;
            if (normal.sqrMagnitude < .9f) throw new ArgumentException("Cylinder axis and palm normal cannot be parallel.");
            BoundHand bound = Hand(isLeft);
            Transform hand = bound.Binding.Hand;
            Quaternion rotation = Quaternion.LookRotation(axis.normalized, normal) * Quaternion.Inverse(bound.FrameInHand);
            // Measure metres through the live transforms, including the FBX
            // scale and actor scale. Neither source sockets nor bones move.
            Vector3 offset = Quaternion.Inverse(hand.rotation) *
                (bound.Binding.GripSocket.position - bound.Binding.CentreAnchor.position);
            return new Pose(centre + rotation * offset, rotation);
        }

        private BoundHand Hand(bool isLeft)
        {
            EnsureBindings();
            return isLeft ? left : right;
        }

        private void EnsureBindings()
        {
            if (left != null && right != null) return;
            if (hands == null || hands.Length != 2 || string.IsNullOrEmpty(shapeName))
                throw new InvalidOperationException("This NPC has no authored hand-grip catalog.");
            Configure(hands, shapeName, cylinderRadius);
        }

        private void OnEnable() => ResetGrip();
        private void OnDisable() => ResetGrip();

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static SkinnedMeshRenderer[] Copy(IReadOnlyList<SkinnedMeshRenderer> values)
        {
            if (values == null) return null;
            var copy = new SkinnedMeshRenderer[values.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = values[i];
            return copy;
        }

        private sealed class BoundHand
        {
            public readonly HandBinding Binding;
            public readonly Quaternion FrameInHand;
            public float Weight { get; private set; } = -1f;
            public NpcHandGripKind Kind { get; private set; }
            private readonly int[] shapes;
            private UnityEngine.Object gripOwner;
            private NpcHandGripKind ownedKind, previousKind;
            private float previousWeight;
            private Transform cigaretteSocket;
            private readonly Dictionary<NpcHandGripKind, int[]> variantShapes = new();
            private readonly Dictionary<NpcHandGripKind, AnatomicalHandFrame> variantFrames = new();
            private AnatomicalHandFrame anatomicalFrame;
            private bool hasAnatomicalFrame;

            public AnatomicalHandFrame AnatomicalFrame
            {
                get
                {
                    if (!hasAnatomicalFrame)
                    {
                        anatomicalFrame = MeasureAnatomy();
                        hasAnatomicalFrame = true;
                    }
                    return anatomicalFrame;
                }
            }

            public BoundHand(HandBinding binding, string shape, Transform owner, HashSet<SkinnedMeshRenderer> owned)
            {
                Binding = binding;
                Transform hand = binding.Hand;
                if (hand == null || !hand.IsChildOf(owner) || binding.GripSocket == null ||
                    !binding.GripSocket.IsChildOf(hand) || binding.CentreAnchor == null ||
                    !binding.CentreAnchor.IsChildOf(hand) || binding.AxisAnchor == null ||
                    !binding.AxisAnchor.IsChildOf(hand) || binding.PalmAnchor == null || !binding.PalmAnchor.IsChildOf(hand))
                    throw new ArgumentException("Every authored grip anchor must belong to its original hand.");
                Vector3 axis = binding.AxisAnchor.position - binding.CentreAnchor.position;
                Vector3 normal = Vector3.ProjectOnPlane(binding.PalmAnchor.position - binding.CentreAnchor.position, axis).normalized;
                if (!Finite(axis) || !Finite(normal) || axis.sqrMagnitude < .000001f || normal.sqrMagnitude < .9f)
                    throw new ArgumentException("The authored cylinder frame is degenerate.");
                Quaternion inverse = Quaternion.Inverse(hand.rotation);
                FrameInHand = Quaternion.LookRotation(inverse * axis.normalized, inverse * normal);
                if (binding.Renderers == null || binding.Renderers.Count == 0)
                    throw new ArgumentException("An authored grip needs its existing hand renderers.");
                shapes = new int[binding.Renderers.Count];
                for (int part = 0; part < shapes.Length; part++)
                {
                    SkinnedMeshRenderer renderer = binding.Renderers[part];
                    if (renderer == null || !renderer.transform.IsChildOf(owner) || !owned.Add(renderer) || renderer.sharedMesh == null)
                        throw new ArgumentException("Grip renderers must be unique parts of this NPC.");
                    Mesh mesh = renderer.sharedMesh;
                    shapes[part] = -1;
                    for (int i = 0; i < mesh.blendShapeCount; i++)
                    {
                        string name = mesh.GetBlendShapeName(i);
                        if (name != shape && !name.EndsWith("." + shape, StringComparison.Ordinal)) continue;
                        if (shapes[part] >= 0) throw new ArgumentException("Ambiguous authored hand shape on " + renderer.name);
                        shapes[part] = i;
                    }
                    if (shapes[part] < 0) throw new ArgumentException("Missing " + shape + " on " + renderer.name);
                }
            }

            public void Prepare(NpcHandGripKind kind) => GetVariantShapes(kind);

            public void SetManualWeight(float weight, NpcHandGripKind kind)
            {
                GetVariantShapes(kind);
                // An explicit presentation write takes this hand over. A stale
                // scoped contact may no longer restore over that new owner.
                gripOwner = null;
                SetWeight(weight, kind);
            }

            public bool TryCapture(UnityEngine.Object owner, NpcHandGripKind kind)
            {
                if (!ReferenceEquals(gripOwner, null))
                    return ReferenceEquals(gripOwner, owner) && ownedKind == kind;
                GetVariantShapes(kind);
                previousKind = Kind; previousWeight = Mathf.Max(0f, Weight);
                ownedKind = kind; gripOwner = owner;
                SetWeight(0f, kind);
                return true;
            }

            public bool SetOwned(UnityEngine.Object owner, float weight)
            {
                if (ReferenceEquals(gripOwner, null) || !ReferenceEquals(gripOwner, owner)) return false;
                SetWeight(weight, ownedKind);
                return true;
            }

            public bool Release(UnityEngine.Object owner)
            {
                if (ReferenceEquals(gripOwner, null) || !ReferenceEquals(gripOwner, owner)) return false;
                gripOwner = null;
                SetWeight(previousWeight, previousKind);
                return true;
            }

            public void SetWeight(float weight, NpcHandGripKind kind = NpcHandGripKind.Cylindrical)
            {
                GetVariantShapes(kind);
                foreach (KeyValuePair<NpcHandGripKind, int[]> variant in variantShapes)
                    WriteWeights(variant.Value, variant.Key == kind ? weight : 0f);
                WriteWeights(shapes, kind == NpcHandGripKind.Cylindrical ? weight : 0f);
                Kind = kind;
                Weight = weight;
            }

            private void WriteWeights(int[] indices, float weight)
            {
                // Sampling a body clip can restore renderer defaults without
                // changing this owner's cached weight. Reconcile the actual
                // mesh after sampling, including an unchanged held grip.
                for (int i = 0; i < indices.Length; i++)
                    if (Binding.Renderers[i] != null && Binding.Renderers[i].GetBlendShapeWeight(indices[i]) != weight * 100f)
                        Binding.Renderers[i].SetBlendShapeWeight(indices[i], weight * 100f);
            }

            private int[] GetVariantShapes(NpcHandGripKind kind)
            {
                if (kind == NpcHandGripKind.Cylindrical) return shapes;
                if (kind == NpcHandGripKind.Cigarette)
                {
                    if (Binding.IsLeft) throw new InvalidOperationException("SmokingGrip is authored only for the physical right hand.");
                    if (cigaretteSocket == null) cigaretteSocket = FindAnchor(Binding.Hand, "SOCKET_Cigarette.R");
                    if (cigaretteSocket == null) throw new InvalidOperationException("SmokingGrip requires the original right cigarette socket.");
                }
                if (variantShapes.TryGetValue(kind, out int[] found)) return found;
                string shape = VariantName(kind);
                found = new int[Binding.Renderers.Count];
                for (int index = 0; index < found.Length; index++)
                {
                    found[index] = CharacterJointDeformation.FindShape(Binding.Renderers[index].sharedMesh, shape);
                    if (found[index] < 0)
                        throw new InvalidOperationException("Missing " + shape + " on " + Binding.Renderers[index].name);
                }
                variantShapes.Add(kind, found);
                return found;
            }

            public AnatomicalHandFrame GetVariantFrame(NpcHandGripKind kind)
            {
                if (kind == NpcHandGripKind.Cylindrical) return AnatomicalFrame;
                if (kind == NpcHandGripKind.Cigarette)
                {
                    GetVariantShapes(kind);
                    AnatomicalHandFrame neutral = AnatomicalFrame;
                    // Smoking animates the existing filter socket. Its pinch
                    // centre is 22.5 mm outward along the live cigarette axis;
                    // unlike vessel anchors, that offset must not be cached.
                    Vector3 pinch = cigaretteSocket.position + cigaretteSocket.up * .0225f;
                    Vector3 liveOffset = Quaternion.Inverse(Binding.Hand.rotation) * (pinch - Binding.Hand.position);
                    return new AnatomicalHandFrame(neutral.PalmCentreInHand, neutral.FingerDirectionInHand,
                        neutral.ThumbDirectionInHand, neutral.PalmNormalInHand, liveOffset);
                }
                if (variantFrames.TryGetValue(kind, out AnatomicalHandFrame frame)) return frame;
                GetVariantShapes(kind);
                string prefix = "ANCHOR_" + VariantName(kind);
                string suffix = Binding.IsLeft ? ".L" : ".R";
                Transform centre = FindAnchor(Binding.Hand, prefix + "Centre" + suffix);
                if (centre == null || FindAnchor(Binding.Hand, prefix + "Axis" + suffix) == null ||
                    FindAnchor(Binding.Hand, prefix + "Palm" + suffix) == null)
                    throw new InvalidOperationException("The shared grip variant lost its authored hand anchors: " + prefix);
                AnatomicalHandFrame anatomy = AnatomicalFrame;
                Vector3 offset = Quaternion.Inverse(Binding.Hand.rotation) *
                    (centre.position - Binding.Hand.position);
                frame = new AnatomicalHandFrame(anatomy.PalmCentreInHand, anatomy.FingerDirectionInHand,
                    anatomy.ThumbDirectionInHand, anatomy.PalmNormalInHand, offset);
                variantFrames.Add(kind, frame);
                return frame;
            }

            private static string VariantName(NpcHandGripKind kind) => kind switch
            {
                NpcHandGripKind.WineBowl => "WineBowlGrip",
                NpcHandGripKind.SnifterBowl => "SnifterBowlGrip",
                NpcHandGripKind.Cigarette => "SmokingGrip",
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };

            private static Transform FindAnchor(Transform root, string name)
            {
                if (root.name == name) return root;
                for (int index = 0; index < root.childCount; index++)
                {
                    Transform found = FindAnchor(root.GetChild(index), name);
                    if (found != null) return found;
                }
                return null;
            }

            private AnatomicalHandFrame MeasureAnatomy()
            {
                Vector3 palm = default, thumb = default, index = default, fingers = default;
                bool hasPalm = false, hasThumb = false, hasIndex = false;
                int fingerCount = 0;
                foreach (SkinnedMeshRenderer renderer in Binding.Renderers)
                {
                    string name = renderer.name;
                    // Default NPC catalogs also bind the optional glove copy.
                    // Anatomy comes from the original skin, independently of
                    // the wardrobe selection; both copies receive the grip.
                    if (!name.StartsWith("GEO_", StringComparison.Ordinal))
                        continue;
                    bool isThumb = name.Contains("Thumb.");
                    bool isFinger = name.Contains("Finger");
                    bool isPalm = name.Contains("HandPalm.") ||
                        name.StartsWith("GEO_Hand.", StringComparison.Ordinal);
                    if (!isThumb && !isFinger && !isPalm)
                        continue;
                    Vector3 centre = MeasureNeutralCentre(renderer);
                    if (isPalm) { palm = centre; hasPalm = true; }
                    if (isThumb) { thumb = centre; hasThumb = true; }
                    if (isFinger)
                    {
                        fingers += centre;
                        fingerCount++;
                        if (name.Contains("Finger0.")) { index = centre; hasIndex = true; }
                    }
                }

                if (!hasPalm || !hasThumb || !hasIndex || fingerCount != 4)
                    throw new InvalidOperationException("An anatomical grip needs the existing palm, thumb and four finger meshes.");
                Vector3 distal = (fingers / fingerCount - palm).normalized;
                Vector3 towardThumb = Vector3.ProjectOnPlane(thumb - index, distal).normalized;
                // Unity's imported coordinates reflect Blender's hand frame.
                // Named left/right anatomy fixes which side of this measured
                // thumb/index plane is palm, independently of socket rotation.
                Vector3 normal = Vector3.Cross(distal, towardThumb).normalized *
                    (Binding.IsLeft ? -1f : 1f);
                if (distal.sqrMagnitude < 0.9f || normal.sqrMagnitude < 0.9f)
                    throw new InvalidOperationException("The measured hand anatomy is degenerate.");
                Transform hand = Binding.Hand;
                Vector3 cylinder = Quaternion.Inverse(hand.rotation) *
                    (Binding.CentreAnchor.position - hand.position);
                return new AnatomicalHandFrame(palm, distal, towardThumb, normal, cylinder);
            }

            private Vector3 MeasureNeutralCentre(SkinnedMeshRenderer renderer)
            {
                Mesh mesh = renderer.sharedMesh;
                Transform[] bones = renderer.bones;
                int bone = Array.IndexOf(bones, Binding.Hand);
                if (bone < 0)
                    throw new InvalidOperationException("An anatomical hand mesh must retain its original hand bone.");
                Matrix4x4 bindPose = mesh.bindposes[bone];
                Vector3[] vertices = mesh.vertices;
                Vector3 sum = Vector3.zero;
                Quaternion inverse = Quaternion.Inverse(Binding.Hand.rotation);
                for (int index = 0; index < vertices.Length; index++)
                    sum += inverse * Binding.Hand.TransformVector(bindPose.MultiplyPoint3x4(vertices[index]));
                return sum / vertices.Length;
            }
        }
    }
}
