using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The three places the Ferryman stands between the bonnet and the
    /// wheel: where his boots hit the ground, the corner he rounds, and
    /// where he stops to open his own door.
    ///
    /// Every one of them is read off the car's drawn anchors and its own
    /// dimensions, exactly as the hero's passenger dock is, so a car that
    /// moves in the generator carries the walk with it. Heights come from a
    /// downward ray at each point rather than from the car's ground: the
    /// island's slab, its foundation and the flattened lot around it are
    /// three different tops within a couple of metres, and a man walking
    /// eight centimetres inside the paving is the same bug the bench had.
    ///
    /// Unlike the hero's dock this one does not have to be walkable - he is
    /// a staged NPC with no capsule and no collider. It only has to be
    /// TRUE: outside the bodywork, outside the swing of the door he is
    /// about to pull, and on whatever the ground actually is.
    /// </summary>
    public readonly struct LastRouteFerrymanBoardingPlan
    {
        /// <summary>
        /// How far ahead of his own soles he lands. He is perched facing out
        /// over the nose, so the drop is a step forward off the bumper and
        /// not a scramble sideways - it is the one direction his pose is
        /// already pointing.
        /// </summary>
        public const float LandingReach = 0.80f;

        /// <summary>
        /// The standing point at the driver's door, held to the same two
        /// numbers as the hero's at the passenger's. The rule they exist for
        /// is <see cref="LastRouteCarDoors.MeasureSwingClearance"/>: a
        /// 1.51 m leaf on a hinge at the A-pillar sweeps every bearing
        /// between shut and open, so the only safe place to stand is beyond
        /// its radius.
        /// </summary>
        public const float DockStandoff = LastRouteCarSeatPlan.DockStandoff;
        public const float DockRearwardShift =
            LastRouteCarSeatPlan.DockRearwardShift;

        /// <summary>
        /// How far ahead of the bumper the rounding corner sits. He cannot
        /// walk straight from the landing point to the door - that line cuts
        /// the nose of his own car - so the walk is two legs with one corner
        /// out beyond the front wing.
        /// </summary>
        public const float CornerLead = 0.45f;

        private LastRouteFerrymanBoardingPlan(
            bool isPresent,
            Vector3 landingPosition,
            Vector3 landingFacing,
            Vector3 approachCorner,
            Vector3 doorDockPosition,
            Vector3 doorDockFacing,
            Vector3 driverHingePosition,
            float driverLeafReach)
        {
            IsPresent = isPresent;
            LandingPosition = landingPosition;
            LandingFacing = landingFacing;
            ApproachCorner = approachCorner;
            DoorDockPosition = doorDockPosition;
            DoorDockFacing = doorDockFacing;
            DriverHingePosition = driverHingePosition;
            DriverLeafReach = driverLeafReach;
        }

        public bool IsPresent { get; }

        /// <summary>Where his boots arrive, in front of the bumper.
        /// </summary>
        public Vector3 LandingPosition { get; }

        /// <summary>The way he is already looking as he drops - out over the
        /// nose, which is the perch's own facing.</summary>
        public Vector3 LandingFacing { get; }

        /// <summary>The one corner of the walk, off the front wing on the
        /// driver's side.</summary>
        public Vector3 ApproachCorner { get; }

        /// <summary>Where he stops, clear of the door he is about to open.
        /// </summary>
        public Vector3 DoorDockPosition { get; }

        /// <summary>Facing the doorway he is about to step into, derived
        /// from the drawn door entry anchor.</summary>
        public Vector3 DoorDockFacing { get; }

        public Vector3 DriverHingePosition { get; }
        public float DriverLeafReach { get; }

        /// <summary>How much daylight his standing point keeps between
        /// itself and the far edge of the swinging leaf.</summary>
        public float DoorSwingClearance =>
            LastRouteCarDoors.MeasureSwingClearance(
                DoorDockPosition,
                DriverHingePosition,
                DriverLeafReach);

        public static LastRouteFerrymanBoardingPlan Absent =>
            new LastRouteFerrymanBoardingPlan(
                false,
                Vector3.zero,
                Vector3.forward,
                Vector3.zero,
                Vector3.zero,
                Vector3.forward,
                Vector3.zero,
                0f);

        public static LastRouteFerrymanBoardingPlan Create(
            LastRouteCarAssetRegistry registry,
            float groundY)
        {
            if (registry == null || !registry.IsBound)
            {
                return Absent;
            }

            Transform car = registry.transform;

            // Forward from the drawn cabin - driver's seat towards the
            // steering wheel - and not from any transform's own axes. The
            // seat plan beside this one carries the full story of why.
            Vector3 forward = Vector3.ProjectOnPlane(
                registry.SteeringWheelPivot.position -
                registry.DriverSeatAnchor.position,
                Vector3.up);
            if (forward.sqrMagnitude < 0.000001f)
            {
                forward = Vector3.ProjectOnPlane(car.forward, Vector3.up);
                if (forward.sqrMagnitude < 0.000001f)
                {
                    return Absent;
                }
            }

            forward = forward.normalized;

            Vector3 right = Vector3.ProjectOnPlane(car.right, Vector3.up);
            if (right.sqrMagnitude < 0.000001f)
            {
                return Absent;
            }

            right = right.normalized;

            // Out of the DRIVER's flank, taken from that door's own anchor.
            Vector3 driverDoorGround = registry.DriverDoorEntryAnchor.position;
            Vector3 outward =
                Vector3.Dot(driverDoorGround - car.position, right) >= 0f
                    ? right
                    : -right;

            // His facing on the bonnet, the way his own plan derives it:
            // soles anchor minus seat anchor, which points out over the nose.
            Vector3 perchFacing = Vector3.ProjectOnPlane(
                registry.PerchSolesAnchor.position -
                registry.PerchSeatAnchor.position,
                Vector3.up);
            perchFacing = perchFacing.sqrMagnitude > 0.000001f
                ? perchFacing.normalized
                : forward;

            Vector3 landing = registry.PerchSolesAnchor.position +
                              (perchFacing * LandingReach);
            landing.y = LastRouteCarSeatPlan.ResolveStandingHeight(
                landing, groundY);

            Vector3 corner = car.position +
                (outward * DockStandoff) +
                (forward * ((registry.Dimensions.Length * 0.5f) + CornerLead));
            corner.y = LastRouteCarSeatPlan.ResolveStandingHeight(
                corner, groundY);

            Vector3 centreline = Vector3.ProjectOnPlane(
                driverDoorGround - car.position, right);
            Vector3 dock = car.position + centreline +
                (outward * DockStandoff) -
                (forward * DockRearwardShift);
            dock.y = LastRouteCarSeatPlan.ResolveStandingHeight(dock, groundY);

            // He turns to face the doorway he is about to step into, not the
            // car's flank in general. Two drawn points again.
            Vector3 dockFacing = Vector3.ProjectOnPlane(
                driverDoorGround - dock, Vector3.up);
            dockFacing = dockFacing.sqrMagnitude > 0.000001f
                ? dockFacing.normalized
                : -outward;

            Transform leaf = registry.DriverDoorLeaf;
            return new LastRouteFerrymanBoardingPlan(
                true,
                landing,
                perchFacing,
                corner,
                dock,
                dockFacing,
                leaf != null ? leaf.position : dock,
                LastRouteCarDoors.MeasureLeafReach(leaf));
        }
    }
}
