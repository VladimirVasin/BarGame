using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
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
        {
            if (!Finite(weight)) throw new ArgumentOutOfRangeException(nameof(weight));
            Hand(isLeft).SetWeight(Mathf.Clamp01(weight));
        }

        public void ResetGrip()
        {
            if (hands == null || hands.Length == 0) return;
            EnsureBindings();
            left.SetWeight(0f); right.SetWeight(0f);
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
            private readonly int[] shapes;

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

            public void SetWeight(float weight)
            {
                // Sampling a body clip can restore renderer defaults without
                // changing this owner's cached weight. Reconcile the actual
                // mesh after sampling, including an unchanged held grip.
                for (int i = 0; i < shapes.Length; i++)
                    if (Binding.Renderers[i] != null && Binding.Renderers[i].GetBlendShapeWeight(shapes[i]) != weight * 100f)
                        Binding.Renderers[i].SetBlendShapeWeight(shapes[i], weight * 100f);
                Weight = weight;
            }
        }
    }
}
