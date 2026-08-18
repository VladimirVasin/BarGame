using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One end of the wire over the chess set: where it is tied, and
    /// whether the thing it is tied to already exists.
    /// </summary>
    public readonly struct CityParkChessLampAnchor
    {
        public CityParkChessLampAnchor(
            Vector3 tiePoint,
            float groundY,
            bool isTree)
        {
            TiePoint = tiePoint;
            GroundY = groundY;
            IsTree = isTree;
        }

        /// <summary>Where the wire is knotted, in world space.</summary>
        public Vector3 TiePoint { get; }

        /// <summary>The ground under it, so a fallback pole can be
        /// stood on the terrain rather than on the set's datum.</summary>
        public float GroundY { get; }

        /// <summary>
        /// True when a park tree carries this end. False means the
        /// world builder has to plant the hook pole itself: the park's
        /// tree field is deterministic but not obliging, and a seed
        /// that leaves one side of the set bare must still get a wire.
        /// </summary>
        public bool IsTree { get; }
    }

    /// <summary>
    /// One pendant on the wire. Two are planned and only one of them
    /// burns.
    /// </summary>
    public readonly struct CityParkChessLampPendant
    {
        public CityParkChessLampPendant(
            Vector3 wirePoint,
            Vector3 shadeCenter,
            bool isLit)
        {
            WirePoint = wirePoint;
            ShadeCenter = shadeCenter;
            IsLit = isLit;
        }

        /// <summary>Where it hangs off the wire.</summary>
        public Vector3 WirePoint { get; }

        /// <summary>Centre of the enamel cone under it.</summary>
        public Vector3 ShadeCenter { get; }

        /// <summary>
        /// Whether this one still has a lamp in it. Exactly one does.
        /// </summary>
        public bool IsLit { get; }
    }

    /// <summary>
    /// The single light over the park chess set: a wire strung between
    /// two trees with two enamel cones on it, one of which still burns.
    ///
    /// It is planned rather than authored into the chess recipe because
    /// the recipe deals only in axis-aligned boxes with no rotation and
    /// no GameObjects, and a sagging wire carrying a real
    /// <see cref="Light"/> is neither. The geometry it does own — where
    /// the two tables are and which way the set faces — is read back
    /// out of that same recipe, so the lamp cannot drift off the board
    /// it lights.
    ///
    /// §10 of the art bible governs the result: park light is rarer and
    /// softer than street light, its sources are partly hidden by the
    /// canopies, and landmarks are modelled weakly — no stage
    /// spotlight. The bar-side yard's deliberately excessive floodlight
    /// is an exception written for that yard and is not carried here.
    /// </summary>
    public sealed class CityParkChessLampPlan
    {
        /// <summary>
        /// How high up the trunk the wire is knotted. Park trees stand
        /// 2.80-3.52 m to the underside of their canopy, so this clears
        /// the bark on the shortest of them and still leaves the knot
        /// inside the leaves when seen from a distance — which is what
        /// §10 means by a partly hidden source.
        /// </summary>
        public const float TieHeightMeters = 2.55f;

        /// <summary>Sag at the middle of the span.</summary>
        public const float SagMeters = 0.26f;

        /// <summary>From the wire down to the top of the cone.</summary>
        public const float PendantDropMeters = 0.14f;

        /// <summary>Height of the enamel cone itself.</summary>
        public const float ShadeHeightMeters = 0.13f;

        /// <summary>
        /// How far off the span line a tree may stand and still be
        /// usable. Beyond this the wire crosses the set diagonally and
        /// its lamp ends up over grass instead of over the set.
        /// </summary>
        public const float AnchorLateralToleranceMeters = 3.5f;

        /// <summary>
        /// The usable span band, per side. The near edge is the set's
        /// own protection radius plus the margin the decoration planner
        /// already keeps from every trunk, so no tree can be nearer;
        /// the far edge is where a wire stops being a local rig over a
        /// chess corner and becomes municipal infrastructure.
        /// </summary>
        public const float MinimumAnchorReachMeters = 4.8f;

        public const float MaximumAnchorReachMeters = 12f;

        /// <summary>
        /// A wire is knotted round the side of a trunk, not through its
        /// middle, and which side matters here: taking the face nearest
        /// the set pulls the whole span - and with it the one lamp on
        /// it - back toward the line between the two boards. Half the
        /// park trunk, which is drawn 0.52 m square.
        /// </summary>
        public const float TrunkTieInsetMeters = 0.26f;

        /// <summary>Where a hook pole goes when no tree qualifies.</summary>
        public const float FallbackPoleReachMeters = 5.2f;

        public const float FallbackPoleHeightMeters = 2.75f;

        /// <summary>
        /// How far down the wire the dead fitting hangs. The working one
        /// is over the middle of the set; this one is out past the
        /// benches, where the corner used to be lit as well.
        /// </summary>
        public const float DeadPendantReachMeters = 3.8f;

        private static readonly CityParkChessLampPlan AbsentPlan =
            new CityParkChessLampPlan(
                default,
                default,
                default,
                default,
                Vector3.zero,
                Vector3.forward,
                Vector3.right,
                false);

        private CityParkChessLampPlan(
            CityParkChessLampAnchor near,
            CityParkChessLampAnchor far,
            CityParkChessLampPendant lit,
            CityParkChessLampPendant dead,
            Vector3 origin,
            Vector3 forward,
            Vector3 tangent,
            bool isPresent)
        {
            NearAnchor = near;
            FarAnchor = far;
            LitPendant = lit;
            DeadPendant = dead;
            Origin = origin;
            Forward = forward;
            Tangent = tangent;
            IsPresent = isPresent;
        }

        /// <summary>The anchor on the negative tangent side.</summary>
        public CityParkChessLampAnchor NearAnchor { get; }

        /// <summary>And the one on the positive side.</summary>
        public CityParkChessLampAnchor FarAnchor { get; }

        /// <summary>
        /// The cone that still burns. It hangs over the table at
        /// negative tangent — the one the chess player sits at.
        /// </summary>
        public CityParkChessLampPendant LitPendant { get; }

        /// <summary>
        /// And the one over the empty table, which is a bare socket in
        /// an identical cone. Both tables were lit once; one of them
        /// still is, and that pair is the whole statement of the place.
        /// </summary>
        public CityParkChessLampPendant DeadPendant { get; }

        public Vector3 Origin { get; }
        public Vector3 Forward { get; }
        public Vector3 Tangent { get; }
        public bool IsPresent { get; }

        public static CityParkChessLampPlan Create(
            CityLayout layout,
            CityDecorationPlan decorations)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (decorations == null)
            {
                return AbsentPlan;
            }

            if (!TryFindChessBasis(
                    layout,
                    decorations,
                    out Vector3 origin,
                    out Vector3 forward,
                    out Vector3 tangent))
            {
                return AbsentPlan;
            }

            // The span runs ACROSS the set, on its forward axis, and
            // that is a measurement rather than a preference. The park's
            // tree field is planted on its own grid and the decoration
            // planner then keeps the chess set 4.8 m clear of every
            // trunk, which drops the set into a gap between tree rows:
            // along the line of the two tables the nearest trunks stand
            // about five metres off-axis, so a wire between them would
            // pass beside the set and hang its lamp over grass. Across
            // the set the same field offers a pair almost exactly on the
            // line. So the wire crosses between the two tables and its
            // one working lamp covers both of them, which is the wider
            // circle this fixture was chosen for.
            CityParkChessLampAnchor near = ResolveAnchor(
                layout,
                origin,
                forward,
                tangent,
                -1f);
            CityParkChessLampAnchor far = ResolveAnchor(
                layout,
                origin,
                forward,
                tangent,
                1f);

            // The working lamp hangs over the middle of the set, between
            // the two boards and between the two bench rows. The dead
            // one is further down the same wire.
            CityParkChessLampPendant lit = DescribePendant(
                near,
                far,
                origin,
                forward,
                0f,
                true);
            CityParkChessLampPendant dead = DescribePendant(
                near,
                far,
                origin,
                forward,
                DeadPendantReachMeters,
                false);

            return new CityParkChessLampPlan(
                near,
                far,
                lit,
                dead,
                origin,
                forward,
                tangent,
                true);
        }

        /// <summary>
        /// The height of the sagging wire at one distance along the set
        /// axis. A parabola rather than a catenary: over a span this
        /// short the two curves differ by less than the timber they
        /// hang over, and the parabola stays a pure function of the two
        /// knots.
        /// </summary>
        public static Vector3 SampleWire(
            CityParkChessLampAnchor near,
            CityParkChessLampAnchor far,
            Vector3 origin,
            Vector3 spanAxis,
            float distanceAlongSpan)
        {
            float nearT = Vector3.Dot(near.TiePoint - origin, spanAxis);
            float farT = Vector3.Dot(far.TiePoint - origin, spanAxis);
            float span = farT - nearT;
            float amount = Mathf.Approximately(span, 0f)
                ? 0.5f
                : Mathf.Clamp01((distanceAlongSpan - nearT) / span);
            float height = Mathf.Lerp(
                near.TiePoint.y,
                far.TiePoint.y,
                amount) - (SagMeters * 4f * amount * (1f - amount));
            Vector3 flat = Vector3.Lerp(
                near.TiePoint,
                far.TiePoint,
                amount);
            return new Vector3(flat.x, height, flat.z);
        }

        private static CityParkChessLampPendant DescribePendant(
            CityParkChessLampAnchor near,
            CityParkChessLampAnchor far,
            Vector3 origin,
            Vector3 spanAxis,
            float distanceAlongSpan,
            bool isLit)
        {
            Vector3 wirePoint = SampleWire(
                near,
                far,
                origin,
                spanAxis,
                distanceAlongSpan);
            Vector3 shadeCenter = wirePoint - (Vector3.up *
                (PendantDropMeters + (ShadeHeightMeters * 0.5f)));
            return new CityParkChessLampPendant(
                wirePoint,
                shadeCenter,
                isLit);
        }

        private static CityParkChessLampAnchor ResolveAnchor(
            CityLayout layout,
            Vector3 origin,
            Vector3 forward,
            Vector3 tangent,
            float side)
        {
            IReadOnlyList<Vector3> trees = layout.Park.TreePositions;
            var bestReach = float.MaxValue;
            Vector3 bestTree = Vector3.zero;
            bool found = false;
            for (int index = 0; index < trees.Count; index++)
            {
                Vector3 offset = trees[index] - origin;
                offset.y = 0f;
                float reach = Vector3.Dot(offset, forward) * side;
                float lateral = Mathf.Abs(Vector3.Dot(offset, tangent));
                if (reach < MinimumAnchorReachMeters ||
                    reach > MaximumAnchorReachMeters ||
                    lateral > AnchorLateralToleranceMeters ||
                    reach >= bestReach)
                {
                    continue;
                }

                bestReach = reach;
                bestTree = trees[index];
                found = true;
            }

            Vector3 foot = found
                ? bestTree
                : origin + (forward * (side * FallbackPoleReachMeters));
            if (found)
            {
                float lateral = Vector3.Dot(foot - origin, tangent);
                foot -= tangent *
                    (Mathf.Sign(lateral) * TrunkTieInsetMeters);
            }

            float groundY = SampleGround(layout, foot, origin.y);
            float tieHeight = found
                ? TieHeightMeters
                : FallbackPoleHeightMeters - 0.20f;
            return new CityParkChessLampAnchor(
                new Vector3(foot.x, groundY + tieHeight, foot.z),
                groundY,
                found);
        }

        private static float SampleGround(
            CityLayout layout,
            Vector3 position,
            float fallback)
        {
            return CityTerrainSurfacePlan.TrySampleGroundTop(
                layout,
                new Vector2(position.x, position.z),
                out float top,
                out _)
                ? top
                : fallback;
        }

        private static bool TryFindChessBasis(
            CityLayout layout,
            CityDecorationPlan decorations,
            out Vector3 origin,
            out Vector3 forward,
            out Vector3 tangent)
        {
            for (int index = 0;
                 index < decorations.Descriptors.Count;
                 index++)
            {
                CityDecorationDescriptor descriptor =
                    decorations.Descriptors[index];
                if (descriptor.Kind !=
                    CityDecorationKind.ParkChessTables)
                {
                    continue;
                }

                if (!CityDecorationWorldBuilder.TryDescribeRecipeBasis(
                        layout,
                        descriptor,
                        out origin,
                        out forward))
                {
                    continue;
                }

                tangent = new Vector3(-forward.z, 0f, forward.x);
                return true;
            }

            origin = Vector3.zero;
            forward = Vector3.forward;
            tangent = Vector3.right;
            return false;
        }
    }
}
