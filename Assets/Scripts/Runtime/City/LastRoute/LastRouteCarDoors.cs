using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Opens and shuts the two front doors of the parked car.
    ///
    /// Both leaves were authored on their own hinge pivots by
    /// `tools/build-last-route-car-3d-model.py` precisely so that "he opens
    /// the door" would one day be a rotation rather than a re-author, and
    /// this is the day. The bus's own leaves are driven the same way -
    /// cache the closed local pose once, then rotate about a resolved hinge
    /// axis in world space - because a leaf that is re-based every frame
    /// from its own current rotation drifts.
    ///
    /// Two things here refuse to trust an imported node's axes, and the
    /// project has been bitten six times for doing otherwise:
    ///
    ///  - The hinge axis is the PREFAB ROOT's up. A car door hinges about
    ///    the vertical and the car stands level; `Body.up` is an FBX node's
    ///    idea of up and is not the same vector.
    ///  - Which way is OPEN is derived from two DRAWN points - the hinge and
    ///    the centre of the leaf's own rendered bounds - crossed with the
    ///    outward side. A vector between two drawn things has no basis to
    ///    get wrong, and a sign that comes out backwards would swing the
    ///    door into the cabin instead of into the daylight.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LastRouteCarDoors : MonoBehaviour
    {
        /// <summary>
        /// How far a front door opens. Less than the bus's 72 degrees: a
        /// saloon door on a single check strap stops short of square, and
        /// the extra six degrees would only buy sweep the docks then have
        /// to stand clear of.
        /// </summary>
        public const float MaximumDoorAngle = 66f;

        private sealed class Leaf
        {
            public Transform Target;
            public Vector3 ClosedLocalPosition;
            public Quaternion ClosedLocalRotation;
            public float OpenSign;
            public float Reach;
        }

        private LastRouteCarAssetRegistry registry;
        private Leaf driver;
        private Leaf passenger;

        public bool IsInitialized { get; private set; }
        public float DriverOpenness { get; private set; }
        public float PassengerOpenness { get; private set; }

        /// <summary>How far the drawn driver's leaf reaches from its hinge,
        /// on the ground plane. The radius a dock has to stand outside of.
        /// </summary>
        public float DriverLeafReach => driver?.Reach ?? 0f;

        public float PassengerLeafReach => passenger?.Reach ?? 0f;

        public void Initialize(LastRouteCarAssetRegistry carRegistry)
        {
            if (carRegistry == null)
            {
                throw new ArgumentNullException(nameof(carRegistry));
            }

            if (!carRegistry.IsBound)
            {
                throw new ArgumentException(
                    "The car's doors cannot be driven before its registry " +
                    "is bound.",
                    nameof(carRegistry));
            }

            registry = carRegistry;
            Vector3 outward = ResolveOutward(
                registry,
                registry.PassengerDoorEntryAnchor.position);
            driver = CaptureLeaf(registry.DriverDoorLeaf, -outward);
            passenger = CaptureLeaf(registry.PassengerDoorLeaf, outward);
            IsInitialized = driver != null && passenger != null;
            SetDriverOpenness(0f);
            SetPassengerOpenness(0f);
        }

        public void SetDriverOpenness(float openness)
        {
            DriverOpenness = Sanitize(openness);
            ApplyLeaf(driver, DriverOpenness);
        }

        public void SetPassengerOpenness(float openness)
        {
            PassengerOpenness = Sanitize(openness);
            ApplyLeaf(passenger, PassengerOpenness);
        }

        /// <summary>
        /// The hero's capsule and its skin, from `PlayerFactory`. A dock
        /// closer to a hinge than the leaf's reach plus this is a dock the
        /// door opens THROUGH.
        /// </summary>
        public const float SwingBodyClearance = 0.36f;

        /// <summary>
        /// How much daylight a standing point has between it and the
        /// farthest the leaf can swing. Negative means the door sweeps
        /// through whoever is standing there.
        ///
        /// Distance rather than angle, and deliberately so: the swept
        /// sector reaches every bearing between shut and open, so no
        /// standing point inside the leaf's radius is ever safe no matter
        /// which way it lies. The only way out is to stand beyond the
        /// blade.
        /// </summary>
        public static float MeasureSwingClearance(
            Vector3 standingPosition,
            Vector3 hingePosition,
            float leafReach)
        {
            Vector3 offset = standingPosition - hingePosition;
            offset.y = 0f;
            return offset.magnitude - leafReach;
        }

        /// <summary>
        /// The planar distance from a hinge to the farthest point the leaf
        /// is actually drawn at. Measured off the renderers rather than
        /// re-typed from the generator, so a redrawn door moves the docks
        /// that have to clear it instead of quietly sweeping through them.
        /// </summary>
        public static float MeasureLeafReach(Transform leaf)
        {
            if (leaf == null)
            {
                return 0f;
            }

            Renderer[] renderers = leaf.GetComponentsInChildren<Renderer>(true);
            Vector3 hinge = leaf.position;
            float reach = 0f;
            for (int index = 0; index < renderers.Length; index++)
            {
                Bounds bounds = renderers[index].bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var offset = new Vector3(
                        (corner & 1) == 0 ? -bounds.extents.x : bounds.extents.x,
                        (corner & 2) == 0 ? -bounds.extents.y : bounds.extents.y,
                        (corner & 4) == 0 ? -bounds.extents.z : bounds.extents.z);
                    Vector3 planar = bounds.center + offset - hinge;
                    planar.y = 0f;
                    reach = Mathf.Max(reach, planar.magnitude);
                }
            }

            return reach;
        }

        /// <summary>
        /// Which way is out of the cabin on the passenger side, taken from
        /// the drawn door anchor rather than assumed. Shared with
        /// <see cref="LastRouteCarSeatPlan"/>, which docks the hero by it.
        /// </summary>
        internal static Vector3 ResolveOutward(
            LastRouteCarAssetRegistry carRegistry,
            Vector3 doorAnchorPosition)
        {
            Transform car = carRegistry.transform;
            Vector3 right = Vector3.ProjectOnPlane(car.right, Vector3.up);
            if (right.sqrMagnitude < 0.000001f)
            {
                return Vector3.right;
            }

            right = right.normalized;
            return Vector3.Dot(doorAnchorPosition - car.position, right) >= 0f
                ? right
                : -right;
        }

        private Leaf CaptureLeaf(Transform target, Vector3 outward)
        {
            if (target == null)
            {
                return null;
            }

            // Where the leaf is drawn, relative to its own hinge. The
            // pivot's origin IS the hinge, so this is the door's length
            // direction and nothing else.
            Vector3 alongLeaf = Vector3.ProjectOnPlane(
                MeasureLeafCentre(target) - target.position,
                Vector3.up);
            float openSign = 1f;
            if (alongLeaf.sqrMagnitude > 0.000001f)
            {
                float swing = Vector3.Dot(
                    Vector3.Cross(transform.up, alongLeaf.normalized),
                    outward);
                openSign = swing >= 0f ? 1f : -1f;
            }

            return new Leaf
            {
                Target = target,
                ClosedLocalPosition = target.localPosition,
                ClosedLocalRotation = target.localRotation,
                OpenSign = openSign,
                Reach = MeasureLeafReach(target)
            };
        }

        private static Vector3 MeasureLeafCentre(Transform leaf)
        {
            Renderer[] renderers = leaf.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return leaf.position;
            }

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds.center;
        }

        private void ApplyLeaf(Leaf leaf, float openness)
        {
            if (leaf == null || leaf.Target == null)
            {
                return;
            }

            leaf.Target.localPosition = leaf.ClosedLocalPosition;
            leaf.Target.localRotation = leaf.ClosedLocalRotation;
            if (openness <= 0f)
            {
                return;
            }

            leaf.Target.rotation =
                Quaternion.AngleAxis(
                    MaximumDoorAngle * openness * leaf.OpenSign,
                    ResolveHingeAxis()) *
                leaf.Target.rotation;
        }

        /// <summary>
        /// The vertical of the car itself. Deliberately the runtime root's
        /// up rather than the imported body node's: that node's forward
        /// comes out nearly vertical, which is how a sibling of this file
        /// spent a day seating the hero facing world +Z.
        /// </summary>
        private Vector3 ResolveHingeAxis()
        {
            Vector3 up = transform.up;
            return up.sqrMagnitude > 0.000001f ? up.normalized : Vector3.up;
        }

        private static float Sanitize(float openness)
        {
            return float.IsNaN(openness) || float.IsInfinity(openness)
                ? 0f
                : Mathf.Clamp01(openness);
        }
    }
}
