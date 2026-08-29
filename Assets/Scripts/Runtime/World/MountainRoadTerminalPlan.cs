using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum MountainRoadTerminalLandmarkKind
    {
        Cafe = 0,
        Cableway = 1,

        /// <summary>Where the ground stops.</summary>
        Brink = 2
    }

    public enum MountainCablewayNodeKind
    {
        LowerStation = 0,
        Support = 1,
        UpperTurn = 2
    }

    public readonly struct MountainRoadTerminalRect
    {
        internal MountainRoadTerminalRect(
            Vector3 center,
            Vector3 right,
            Vector3 forward,
            Vector2 size)
        {
            Center = center;
            Right = right.normalized;
            Forward = forward.normalized;
            Size = size;
        }

        public Vector3 Center { get; }
        public Vector3 Right { get; }
        public Vector3 Forward { get; }
        public Vector2 Size { get; }
        public Vector2 HalfSize => Size * 0.5f;

        public bool ContainsXZ(Vector3 point, float inset = 0f)
        {
            Vector3 offset = point - Center;
            float halfRight = Mathf.Max(0f, Size.x * 0.5f - inset);
            float halfForward = Mathf.Max(0f, Size.y * 0.5f - inset);
            return Mathf.Abs(Vector3.Dot(offset, Right)) <= halfRight &&
                   Mathf.Abs(Vector3.Dot(offset, Forward)) <= halfForward;
        }

        public Vector3 GetCorner(int index)
        {
            float right = (index & 1) == 0 ? -1f : 1f;
            float forward = (index & 2) == 0 ? -1f : 1f;
            return Center +
                   Right * (right * Size.x * 0.5f) +
                   Forward * (forward * Size.y * 0.5f);
        }
    }

    public readonly struct MountainRoadTerminalLandmark
    {
        internal MountainRoadTerminalLandmark(
            string stableId,
            MountainRoadTerminalLandmarkKind kind,
            Vector3 position,
            string localizationKey)
        {
            StableId = stableId ?? string.Empty;
            Kind = kind;
            Position = position;
            LocalizationKey = localizationKey ?? string.Empty;
        }

        public string StableId { get; }
        public MountainRoadTerminalLandmarkKind Kind { get; }
        public Vector3 Position { get; }
        public string LocalizationKey { get; }
    }

    public sealed class MountainRoadVehicleApronPlan
    {
        internal MountainRoadVehicleApronPlan(
            Vector3 center,
            Vector3 entryCenter,
            Vector3 forward,
            float entryWidth,
            float turningRadius)
        {
            Center = center;
            EntryCenter = entryCenter;
            Forward = forward.normalized;
            Right = Vector3.Cross(Vector3.up, Forward).normalized;
            EntryWidth = entryWidth;
            TurningRadius = turningRadius;
        }

        public Vector3 Center { get; }
        public Vector3 EntryCenter { get; }
        public Vector3 Forward { get; }
        public Vector3 Right { get; }
        public float EntryWidth { get; }
        public float TurningRadius { get; }
    }

    public sealed class MountainRoadCafePlan
    {
        private readonly ReadOnlyCollection<Vector2> footprintXZ;

        internal MountainRoadCafePlan(
            string stableId,
            Vector3 center,
            Vector3 right,
            Vector3 forward,
            float floorY,
            float height,
            float chamferDepth,
            Vector3 doorCenter,
            float doorWidth,
            IList<Vector2> sourceFootprintXZ)
        {
            StableId = stableId ?? string.Empty;
            Center = center;
            Right = right.normalized;
            Forward = forward.normalized;
            FloorY = floorY;
            Height = height;
            ChamferDepth = chamferDepth;
            DoorCenter = doorCenter;
            DoorForward = -Forward;
            DoorWidth = doorWidth;
            footprintXZ = new ReadOnlyCollection<Vector2>(
                new List<Vector2>(sourceFootprintXZ));
        }

        public string StableId { get; }
        public Vector3 Center { get; }
        public Vector3 Right { get; }
        public Vector3 Forward { get; }
        public float FloorY { get; }
        public float Height { get; }
        public float ChamferDepth { get; }
        public Vector3 DoorCenter { get; }
        public Vector3 DoorForward { get; }
        public float DoorWidth { get; }
        public IReadOnlyList<Vector2> FootprintXZ => footprintXZ;

        public bool ContainsInterior(Vector3 point, float edgeInset = 0.18f)
        {
            if (point.y < FloorY - 0.2f ||
                point.y > FloorY + Height + 0.2f)
            {
                return false;
            }

            Vector2 tested = new Vector2(point.x, point.z);
            if (!Contains(footprintXZ, tested))
            {
                return false;
            }

            if (edgeInset <= 0f)
            {
                return true;
            }

            float insetSquared = edgeInset * edgeInset;
            for (int index = 0; index < footprintXZ.Count; index++)
            {
                Vector2 first = footprintXZ[index];
                Vector2 second = footprintXZ[
                    (index + 1) % footprintXZ.Count];
                if (DistanceToSegmentSquared(tested, first, second) <
                    insetSquared)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Contains(
            IReadOnlyList<Vector2> polygon,
            Vector2 point)
        {
            bool inside = false;
            for (int first = 0, second = polygon.Count - 1;
                 first < polygon.Count;
                 second = first++)
            {
                Vector2 a = polygon[first];
                Vector2 b = polygon[second];
                bool crosses = (a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x - a.x) *
                    (point.y - a.y) /
                    ((b.y - a.y) + Mathf.Epsilon) + a.x;
                if (crosses)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static float DistanceToSegmentSquared(
            Vector2 point,
            Vector2 first,
            Vector2 second)
        {
            Vector2 segment = second - first;
            float denominator = segment.sqrMagnitude;
            float t = denominator <= 0.000001f
                ? 0f
                : Mathf.Clamp01(Vector2.Dot(point - first, segment) /
                                denominator);
            return (point - Vector2.Lerp(first, second, t)).sqrMagnitude;
        }
    }

    public readonly struct MountainCablewayNodeDescriptor
    {
        internal MountainCablewayNodeDescriptor(
            string stableId,
            MountainCablewayNodeKind kind,
            float distance,
            Vector3 cableCenter,
            Vector3 groundPosition)
        {
            StableId = stableId ?? string.Empty;
            Kind = kind;
            Distance = distance;
            CableCenter = cableCenter;
            GroundPosition = groundPosition;
        }

        public string StableId { get; }
        public MountainCablewayNodeKind Kind { get; }
        public float Distance { get; }
        public Vector3 CableCenter { get; }
        public Vector3 GroundPosition { get; }
    }

    public readonly struct MountainCablewayCabinDescriptor
    {
        internal MountainCablewayCabinDescriptor(
            string stableId,
            float phase)
        {
            StableId = stableId ?? string.Empty;
            Phase = phase;
        }

        public string StableId { get; }
        public float Phase { get; }
    }

    public sealed class MountainRoadCablewayPlan
    {
        public const float CabinRoofDrop = 1.08f;

        private readonly ReadOnlyCollection<MountainCablewayNodeDescriptor>
            nodes;
        private readonly ReadOnlyCollection<MountainCablewayCabinDescriptor>
            cabins;

        internal MountainRoadCablewayPlan(
            string stableId,
            MountainRoadTerminalRect stationArea,
            Vector3 lineForward,
            Vector3 lineRight,
            float trackSeparation,
            float lineLength,
            float cabinSpeed,
            Vector3 cabinSize,
            IList<MountainCablewayNodeDescriptor> sourceNodes,
            IList<MountainCablewayCabinDescriptor> sourceCabins,
            string upperOccluderStableId)
        {
            StableId = stableId ?? string.Empty;
            StationArea = stationArea;
            LineForward = lineForward.normalized;
            LineRight = lineRight.normalized;
            TrackSeparation = trackSeparation;
            LineLength = lineLength;
            CabinSpeed = cabinSpeed;
            CabinSize = cabinSize;
            nodes = new ReadOnlyCollection<MountainCablewayNodeDescriptor>(
                new List<MountainCablewayNodeDescriptor>(sourceNodes));
            cabins = new ReadOnlyCollection<MountainCablewayCabinDescriptor>(
                new List<MountainCablewayCabinDescriptor>(sourceCabins));
            UpperOccluderStableId = upperOccluderStableId ?? string.Empty;
        }

        public string StableId { get; }
        public MountainRoadTerminalRect StationArea { get; }
        public Vector3 LineForward { get; }
        public Vector3 LineRight { get; }
        public float TrackSeparation { get; }
        public float LineLength { get; }
        public float CabinSpeed { get; }
        public Vector3 CabinSize { get; }
        public IReadOnlyList<MountainCablewayNodeDescriptor> Nodes => nodes;
        public IReadOnlyList<MountainCablewayCabinDescriptor> Cabins => cabins;
        public string UpperOccluderStableId { get; }
        public Vector3 LowerCableCenter => nodes[0].CableCenter;
        public Vector3 UpperCableCenter => nodes[nodes.Count - 1].CableCenter;
        public float TurnRadius => TrackSeparation * 0.5f;
        public float CabinAttachmentToBottom =>
            CabinRoofDrop + CabinSize.y;
        public float LoopLength => LineLength * 2f +
                                   Mathf.PI * TrackSeparation;

        /// <summary>
        /// Where the far snow ridge that swallows the upper turn stands, in
        /// the line's own frame.
        ///
        /// These two numbers used to be literals inside
        /// `MountainRoadPlanner`, and the ride guessed its blackout
        /// independently of them. That is the whole bug: the ridge is not
        /// scenery that happens to be near the top, it is planted ON the
        /// line, and the last `6.8 m` of the visible line - the upper turn
        /// included - is inside solid rock BY DESIGN. A fade lead chosen
        /// without them let the passenger ride `3.9 m` into the mountain with
        /// his own eye, and a single-sided ridge has no back face, so what he
        /// saw was the world straight through the rock.
        /// </summary>
        public const float UpperOccluderSetback = 1.8f;

        public const float UpperOccluderDepth = 10f;

        /// <summary>
        /// Clear air between the cabin's leading face and the rock at the
        /// moment the screen is fully out. Small on purpose: the ridge has to
        /// FILL the frame when the cut lands, because a cabin that vanishes
        /// in open air is a worse cut than no cut at all.
        /// </summary>
        public const float UpperOccluderApproachClearance = 1f;

        /// <summary>Where the ridge's near face crosses the track, in metres
        /// along the line from the boarding station.</summary>
        public float UpperOccluderNearFaceDistance =>
            LineLength - UpperOccluderSetback - UpperOccluderDepth * 0.5f;

        /// <summary>
        /// How far the cabin's roof slab oversails its body. The leading
        /// thing on a cabin is not the wall the passenger sits behind, it is
        /// this lip - `8%` of the body's length ahead of it - and measuring
        /// the approach to the rock against the body instead quietly spends
        /// `6 cm` of the clearance it claims to keep.
        /// </summary>
        public const float CabinRoofOverhang = 1.08f;

        /// <summary>Half the drawn cabin, along the line: what actually
        /// touches the rock first.</summary>
        public float CabinLeadingHalfLength =>
            CabinSize.z * CabinRoofOverhang * 0.5f;

        /// <summary>
        /// The last metre of line on which a cabin may still be on screen.
        /// Past it the cabin's own roof is in the mountain.
        /// </summary>
        public float LastVisibleDistance =>
            UpperOccluderNearFaceDistance -
            CabinLeadingHalfLength -
            UpperOccluderApproachClearance;

        /// <summary>
        /// How far the ridge's built crest has to stand over the cable where
        /// the line crosses it. One number, two readers: the planner sizes
        /// the ridge so this is true and the validator refuses a plan where
        /// it is not, which is the only arrangement in which the rule cannot
        /// come out true for seven seeds in eight and throw on the eighth.
        /// </summary>
        public const float UpperOccluderCrestClearance = 2f;

        /// <summary>
        /// A step into the cabin, not a climb. The cabin floor hangs `0.87 m`
        /// over a bare station pad on the authored line, which is a height a
        /// person hauls themselves up rather than steps over - so both
        /// terminals raise a boarding strip until the move is this.
        /// </summary>
        public const float BoardingStepHeight = 0.42f;

        /// <summary>
        /// Clear air between the hero's dock and the cabin's near face. Wide
        /// enough that the moving body never brushes him: contact is read
        /// back as achieved movement here, so a graze zeroes his speed.
        /// </summary>
        public const float BoardingDockStandoff = 0.75f;

        /// <summary>
        /// Where on the loop a cabin stands when it is at the platform. Zero
        /// is the near terminal on the outbound track, for both stations -
        /// the village's line simply runs the other way down the hill.
        /// </summary>
        public float BoardingLoopDistance => 0f;

        /// <summary>
        /// How far the standable floor sits above the cabin's own underside.
        /// The lower skirt is a solid `0.40 m` band and you stand on TOP of
        /// it, not on the bottom of the box - measuring boarding against
        /// <see cref="CabinAttachmentToBottom"/> alone puts the platform
        /// `0.40 m` too low and turns the step straight back into the climb
        /// it exists to remove.
        /// </summary>
        public const float CabinSkirtHeight = 0.4f;

        public float CabinFloorY =>
            LowerCableCenter.y -
            CabinAttachmentToBottom +
            CabinSkirtHeight;

        public float BoardingPlatformTopY =>
            CabinFloorY - BoardingStepHeight;

        /// <summary>
        /// Where the docked cabin GRIPS THE ROPE - which is about three
        /// metres over the passenger's head, not the middle of the box. The
        /// cabin transform is posed here, so this is what a cabin's
        /// `position` equals while it stands at the platform.
        /// </summary>
        public Vector3 BoardingCabinAttachment =>
            MountainCablewayMotion.SampleTrackPosition(this, 0f, 1);

        /// <summary>The middle of the cabin's own floor while it is docked.
        /// </summary>
        public Vector3 BoardingCabinFloorCenter
        {
            get
            {
                Vector3 center = BoardingCabinAttachment;
                center.y = CabinFloorY;
                return center;
            }
        }

        /// <summary>
        /// Where the hero stands to board: OUTBOARD of the outbound track, on
        /// the raised strip, facing in at the doorway.
        ///
        /// Outboard and not between the two tracks, which is where this first
        /// went. The gap between them is `1.15 m` wide and the bullwheel's own
        /// pedestal foot fills it - the dock landed inside a pillar, physics
        /// shoved the hero half a metre clear of it at spawn, and he could
        /// then never walk back to a point that was inside solid steel.
        /// Boarding from the outside of the loop is also simply what a
        /// station does.
        /// </summary>
        public Vector3 BoardingDockPosition
        {
            get
            {
                Vector3 dock = BoardingCabinFloorCenter + LineRight *
                    (CabinSize.x * 0.5f + BoardingDockStandoff);
                dock.y = BoardingPlatformTopY;
                return dock;
            }
        }

        /// <summary>Which way he faces while boarding: in at the cabin.
        /// </summary>
        public Vector3 BoardingFacing => -LineRight;

        /// <summary>The outboard face of a docked cabin - where the platform
        /// has to stop.</summary>
        public float BoardingCabinOuterOffset =>
            TrackSeparation * 0.5f + CabinSize.x * 0.5f;

        /// <summary>
        /// The top of the station's own concrete pad, over the station frame's
        /// origin. Everything on the pad is a step measured from here.
        /// </summary>
        public const float StationPadTopY = 0.16f;

        /// <summary>
        /// How far the four corner columns stand in from the pad's edges, and
        /// how thick they are.
        ///
        /// These were literals inside the world builder, and the boarding
        /// strip was laid out without them: it ran to `4.075` and the columns
        /// stand at `3.81` to `4.09`, so the strip was built THROUGH one. The
        /// strip now stops against a number the frame is also built from.
        /// </summary>
        public const float StationColumnRightInset = 0.55f;

        public const float StationColumnForwardInset = 0.48f;
        public const float StationColumnThickness = 0.28f;
        public const float StationColumnHeight = 4.5f;

        public float StationColumnRightOffset =>
            StationArea.Size.x * 0.5f - StationColumnRightInset;

        public float StationColumnForwardOffset =>
            StationArea.Size.y * 0.5f - StationColumnForwardInset;

        /// <summary>The inboard face of an outboard corner column.</summary>
        public float StationColumnInnerFace =>
            StationColumnRightOffset - StationColumnThickness * 0.5f;

        private const float BoardingPlatformCabinGap = 0.06f;
        private const float BoardingPlatformColumnGap = 0.06f;

        /// <summary>Where the raised strip starts, just clear of a docked
        /// cabin's outboard face.</summary>
        public float BoardingPlatformInnerOffset =>
            BoardingCabinOuterOffset + BoardingPlatformCabinGap;

        /// <summary>And where it stops, just short of the station's own
        /// column.</summary>
        public float BoardingPlatformOuterOffset =>
            StationColumnInnerFace - BoardingPlatformColumnGap;

        public float BoardingPlatformWidth =>
            BoardingPlatformOuterOffset - BoardingPlatformInnerOffset;

        public float BoardingPlatformCenterOffset =>
            (BoardingPlatformInnerOffset + BoardingPlatformOuterOffset) * 0.5f;

        /// <summary>The dock's own offsets in the station frame, which is the
        /// frame every piece of boarding furniture is laid out in.</summary>
        public float BoardingDockRightOffset =>
            Vector3.Dot(BoardingDockPosition - StationArea.Center, LineRight);

        public float BoardingDockForwardOffset =>
            Vector3.Dot(BoardingDockPosition - StationArea.Center, LineForward);

        /// <summary>How high the strip stands over the station frame's own
        /// origin.</summary>
        public float BoardingPlatformLocalTop =>
            BoardingPlatformTopY - StationArea.Center.y;

        public const float BoardingFencePostThickness = 0.13f;
        public const float BoardingFenceRailThickness = 0.10f;
        public const float BoardingFencePostHeight = 1.55f;
        public const float BoardingFenceLeftEndOffset = -2.2f;
        public const int BoardingTreadCount = 3;
        public const float BoardingTreadDepth = 0.34f;

        /// <summary>
        /// The jamb the gate leaf hangs off: the fence's outboard end, set
        /// against the strip rather than on the station's centre line.
        ///
        /// The opening runs from here out to <see cref="StationColumnInnerFace"/>,
        /// which is the far side of the bay and is the building's own column.
        /// A second jamb post cannot stand outboard of the strip - the column
        /// is already there - so the fence ends and the way through is the bay
        /// beside it.
        /// </summary>
        public float BoardingGateJambOffset =>
            BoardingPlatformInnerOffset - 0.09f;

        public float BoardingGateWidth =>
            StationColumnInnerFace -
            (BoardingGateJambOffset + BoardingFencePostThickness * 0.5f);

        /// <summary>
        /// How wide the way out is at a RETURN terminal, and it opens the
        /// other way.
        ///
        /// The fence is one barrier serving two opposite journeys. At the
        /// drive terminal the hero comes off the yard BEHIND it and the
        /// platform is in front, so the opening belongs beside the strip. At
        /// the village he arrives ON the platform and everything he wants is
        /// behind the fence, on the inboard side, up the lane - and a copy of
        /// the drive terminal's fence put a wall across the whole of that with
        /// its one gap at the far end from where he is going. Walking straight
        /// at the village he met the rails broadside, slid the length of them
        /// and wedged on the end post `5.94 m` short.
        ///
        /// So the return terminal's fence ends INBOARD and the way out is the
        /// rest of the pad. It still stands across the track, which is the
        /// thing a barrier at a cable station is actually for.
        /// </summary>
        public const float BoardingReturnGateWidth = 2.4f;

        public float BoardingReturnGateJambOffset =>
            -(StationArea.Size.x * 0.5f - BoardingReturnGateWidth);

        /// <summary>The fence's outboard end at a return terminal, just clear
        /// of the station's own column.</summary>
        public float BoardingReturnFenceEndOffset =>
            StationColumnInnerFace - 0.09f;

        /// <summary>
        /// How far back from the dock the strip begins, and how far past it it
        /// runs. The strip is centred on the dock, so a cabin always stands
        /// against its middle.
        /// </summary>
        public const float BoardingPlatformReach = 1.73f;

        /// <summary>The clear ground between the barrier and the first tread.
        /// </summary>
        public const float BoardingFenceStepGap = 0.19f;

        /// <summary>
        /// THE WHOLE BOARDING SIDE HANGS OFF THE DOCK, and this is the fix for
        /// a defect that shipped invisible.
        ///
        /// It used to hang off a fence line authored at a fixed `1.56`, with
        /// the strip running from the top of the steps to twice the dock minus
        /// that. At the summit the dock is `4.50` forward and the chain came
        /// out fine. **At the village it is `1.90`** - `AlpineVillagePlanner`
        /// puts the near cable `1.9 m` in front of its pad, not `4.5` - so the
        /// strip was solved as `2.77` to `1.03`: a box **`1.74 m` LONG IN THE
        /// WRONG DIRECTION**, with its steps in front of its own far end. No
        /// test saw it, because the only test that measured the strip built
        /// the SUMMIT.
        ///
        /// Ordered from the dock outwards, every terminal gets the same
        /// boarding side wherever its cable happens to fall. At the summit
        /// these reproduce the authored numbers to the millimetre.
        /// </summary>
        public float BoardingPlatformNearForward =>
            BoardingDockForwardOffset - BoardingPlatformReach;

        public float BoardingPlatformFarForward =>
            BoardingDockForwardOffset + BoardingPlatformReach;

        public float BoardingPlatformLength =>
            BoardingPlatformFarForward - BoardingPlatformNearForward;

        /// <summary>The flight climbs to the strip's near end.</summary>
        public float BoardingStepsFarForward => BoardingPlatformNearForward;

        public float BoardingStepsNearForward =>
            BoardingStepsFarForward - BoardingTreadDepth * BoardingTreadCount;

        /// <summary>
        /// And the barrier stands back of the flight. It was a constant
        /// `1.56`; deriving it is what stops a terminal whose cable sits close
        /// to the pad from putting its own fence on top of its own steps.
        /// </summary>
        public float BoardingFenceForward =>
            BoardingStepsNearForward - BoardingFenceStepGap;

        /// <summary>
        /// The concrete under the boarding strip where it runs off the front
        /// of the pad.
        ///
        /// The bullwheel is authored `4.5 m` forward of the station centre -
        /// outside the canopy footprint entirely - and the pad is only
        /// `6.2 m` deep, so the strip that serves a docked cabin stands over
        /// open ground for most of its length. It is kept to the STRIP's width
        /// rather than the pad's: at the pad's width its outer-forward corner
        /// lands `0.07 m` from the plateau's rim, and the ground it is meant
        /// to be standing on stops there.
        /// </summary>
        public float BoardingApronInnerOffset =>
            BoardingPlatformInnerOffset - 0.385f;

        public float BoardingApronOuterOffset => BoardingPlatformOuterOffset;

        public float BoardingApronNearForward =>
            StationArea.Size.y * 0.5f - 0.5f;

        public float BoardingApronFarForward =>
            BoardingPlatformFarForward + 0.2f;

        /// <summary>
        /// The apron as a rectangle in world space - the ground the boarding
        /// strip stands on, and the shape a walkable mask has to follow.
        ///
        /// The strip runs off the FRONT of the pad by design, so a mask that
        /// stops at the pad's edge turns the far end of the platform into a
        /// wall the hero walks into while standing on concrete.
        /// </summary>
        public MountainRoadTerminalRect BoardingApronArea =>
            new MountainRoadTerminalRect(
                StationArea.Center +
                LineRight *
                ((BoardingApronInnerOffset + BoardingApronOuterOffset) *
                 0.5f) +
                LineForward *
                ((BoardingApronNearForward + BoardingApronFarForward) * 0.5f),
                LineRight,
                LineForward,
                new Vector2(
                    BoardingApronOuterOffset - BoardingApronInnerOffset,
                    BoardingApronFarForward - BoardingApronNearForward));

        public bool ContainsClearanceXZ(Vector2 point, float clearance)
        {
            Vector2 start = new Vector2(
                LowerCableCenter.x,
                LowerCableCenter.z);
            Vector2 end = new Vector2(
                UpperCableCenter.x,
                UpperCableCenter.z);
            Vector2 segment = end - start;
            float denominator = segment.sqrMagnitude;
            float t = denominator <= 0.000001f
                ? 0f
                : Mathf.Clamp01(Vector2.Dot(point - start, segment) /
                                denominator);
            float radius = TrackSeparation * 0.5f + clearance;
            return (point - Vector2.Lerp(start, end, t)).sqrMagnitude <=
                   radius * radius;
        }
    }

    public sealed class MountainRoadTerminalPlan
    {
        private readonly ReadOnlyCollection<MountainRoadTerminalLandmark>
            landmarks;

        internal MountainRoadTerminalPlan(
            MountainRoadVehicleApronPlan vehicleApron,
            MountainRoadCafePlan cafe,
            MountainRoadCablewayPlan cableway,
            IList<MountainRoadTerminalLandmark> sourceLandmarks,
            MountainRoadTerminalSitePlan site)
        {
            VehicleApron = vehicleApron ??
                throw new ArgumentNullException(nameof(vehicleApron));
            Cafe = cafe ?? throw new ArgumentNullException(nameof(cafe));
            Cableway = cableway ??
                throw new ArgumentNullException(nameof(cableway));
            landmarks = new ReadOnlyCollection<MountainRoadTerminalLandmark>(
                new List<MountainRoadTerminalLandmark>(sourceLandmarks));
            Site = site;
        }

        public MountainRoadVehicleApronPlan VehicleApron { get; }
        public MountainRoadCafePlan Cafe { get; }
        public MountainRoadCablewayPlan Cableway { get; }
        public IReadOnlyList<MountainRoadTerminalLandmark> Landmarks =>
            landmarks;

        /// <summary>Everything on the pad that is not one of those three.</summary>
        public MountainRoadTerminalSitePlan Site { get; }

        public bool IsSheltered(Vector3 position)
        {
            return Cafe.ContainsInterior(position) ||
                   Cableway.StationArea.ContainsXZ(position, 0.2f) &&
                   position.y >= Cableway.StationArea.Center.y - 0.3f &&
                   position.y <= Cableway.StationArea.Center.y + 5.4f;
        }
    }
}
