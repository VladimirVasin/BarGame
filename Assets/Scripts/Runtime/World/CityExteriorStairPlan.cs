using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    [Flags]
    public enum CityExteriorStairDropSide
    {
        None = 0,
        Left = 1,
        Right = 2,
        Both = Left | Right
    }

    public enum CityExteriorStairLandingKind
    {
        Lower = 0,
        Upper = 1
    }

    public enum CityExteriorStairRailOwnerKind
    {
        Flight = 0,
        Landing = 1,
        Approach = 2
    }

    /// <summary>
    /// One bottom-to-top exterior stair flight in the local space of the
    /// future stair root. Start is the lower seam shared with its landing.
    /// </summary>
    public readonly struct CityExteriorStairFlightDescriptor
    {
        internal CityExteriorStairFlightDescriptor(
            string id,
            Vector3 start,
            Vector3 direction,
            float width,
            int stepCount,
            float stepRise,
            float treadDepth,
            CityExteriorStairDropSide dropSides)
        {
            Id = id ?? string.Empty;
            Start = start;
            Direction = direction;
            Width = width;
            StepCount = stepCount;
            StepRise = stepRise;
            TreadDepth = treadDepth;
            DropSides = dropSides;
        }

        public string Id { get; }
        public Vector3 Start { get; }
        public Vector3 Direction { get; }
        public float Width { get; }
        public int StepCount { get; }
        public float StepRise { get; }
        public float TreadDepth { get; }
        public CityExteriorStairDropSide DropSides { get; }
        public float BaseElevation => Start.y;
        public float TotalRise => StepCount * StepRise;
        public float TopElevation => BaseElevation + TotalRise;
        public float RunLength => StepCount * TreadDepth;
        public Vector3 Right => new Vector3(
            Direction.z,
            0f,
            -Direction.x);
        public Vector3 End =>
            Start +
            (Direction * RunLength) +
            (Vector3.up * TotalRise);

        public Vector3 GetStepCenter(int index)
        {
            if (index < 0 || index >= StepCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            float height = (index + 1) * StepRise;
            return Start +
                   (Direction * ((index + 0.5f) * TreadDepth)) +
                   (Vector3.up * (height * 0.5f));
        }

        public Vector3 GetStepSize(
            int index,
            float treadOverlap = 0f)
        {
            if (index < 0 || index >= StepCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            if (float.IsNaN(treadOverlap) ||
                float.IsInfinity(treadOverlap) ||
                treadOverlap < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(treadOverlap));
            }

            return new Vector3(
                Width,
                (index + 1) * StepRise,
                TreadDepth + treadOverlap);
        }

        public Vector3 GetSidePoint(
            CityExteriorStairDropSide side,
            bool upper)
        {
            if (side != CityExteriorStairDropSide.Left &&
                side != CityExteriorStairDropSide.Right)
            {
                throw new ArgumentOutOfRangeException(nameof(side));
            }

            Vector3 center = upper ? End : Start;
            float sign = side == CityExteriorStairDropSide.Right
                ? 1f
                : -1f;
            return center + (Right * (Width * 0.5f * sign));
        }
    }

    public readonly struct CityExteriorStairLandingDescriptor
    {
        internal CityExteriorStairLandingDescriptor(
            string id,
            string connectedFlightId,
            CityExteriorStairLandingKind kind,
            Vector3 center,
            Vector3 direction,
            float width,
            float length,
            float elevation,
            float thickness,
            CityExteriorStairDropSide dropSides)
        {
            Id = id ?? string.Empty;
            ConnectedFlightId = connectedFlightId ?? string.Empty;
            Kind = kind;
            Center = center;
            Direction = direction;
            Width = width;
            Length = length;
            Elevation = elevation;
            Thickness = thickness;
            DropSides = dropSides;
        }

        public string Id { get; }
        public string ConnectedFlightId { get; }
        public CityExteriorStairLandingKind Kind { get; }
        public Vector3 Center { get; }
        public Vector3 Direction { get; }
        public float Width { get; }
        public float Length { get; }
        public float Elevation { get; }
        public float Thickness { get; }
        public CityExteriorStairDropSide DropSides { get; }
        public Vector3 Right => new Vector3(
            Direction.z,
            0f,
            -Direction.x);
        public Vector3 StartEdgeCenter =>
            Center - (Direction * (Length * 0.5f));
        public Vector3 EndEdgeCenter =>
            Center + (Direction * (Length * 0.5f));

        public Vector3 GetSidePoint(
            CityExteriorStairDropSide side,
            bool atEnd)
        {
            if (side != CityExteriorStairDropSide.Left &&
                side != CityExteriorStairDropSide.Right)
            {
                throw new ArgumentOutOfRangeException(nameof(side));
            }

            Vector3 center = atEnd ? EndEdgeCenter : StartEdgeCenter;
            float sign = side == CityExteriorStairDropSide.Right
                ? 1f
                : -1f;
            return center + (Right * (Width * 0.5f * sign));
        }
    }

    /// <summary>
    /// A guard rail follows an owner's exposed edge. SurfaceStart and
    /// SurfaceEnd lie on that edge; Height raises the beam above both points.
    /// </summary>
    public readonly struct CityExteriorStairRailDescriptor
    {
        internal CityExteriorStairRailDescriptor(
            string id,
            string ownerId,
            CityExteriorStairRailOwnerKind ownerKind,
            CityExteriorStairDropSide side,
            Vector3 surfaceStart,
            Vector3 surfaceEnd,
            float height,
            float thickness)
        {
            Id = id ?? string.Empty;
            OwnerId = ownerId ?? string.Empty;
            OwnerKind = ownerKind;
            Side = side;
            SurfaceStart = surfaceStart;
            SurfaceEnd = surfaceEnd;
            Height = height;
            Thickness = thickness;
        }

        public string Id { get; }
        public string OwnerId { get; }
        public CityExteriorStairRailOwnerKind OwnerKind { get; }
        public CityExteriorStairDropSide Side { get; }
        public Vector3 SurfaceStart { get; }
        public Vector3 SurfaceEnd { get; }
        public float Height { get; }
        public float Thickness { get; }
    }

    /// <summary>
    /// A level-topped retaining wall. TopStart and TopEnd describe its upper
    /// edge; BottomElevation supplies the vertical support depth.
    /// </summary>
    public readonly struct CityExteriorStairRetainingWallDescriptor
    {
        internal CityExteriorStairRetainingWallDescriptor(
            string id,
            Vector3 topStart,
            Vector3 topEnd,
            float bottomElevation,
            float thickness)
        {
            Id = id ?? string.Empty;
            TopStart = topStart;
            TopEnd = topEnd;
            BottomElevation = bottomElevation;
            Thickness = thickness;
        }

        public string Id { get; }
        public Vector3 TopStart { get; }
        public Vector3 TopEnd { get; }
        public float BottomElevation { get; }
        public float TopElevation => TopStart.y;
        public float Thickness { get; }
        public float Height => TopElevation - BottomElevation;
    }

    public sealed class CityExteriorStairPlan
    {
        internal CityExteriorStairPlan(
            string id,
            IList<CityExteriorStairFlightDescriptor> flights,
            IList<CityExteriorStairLandingDescriptor> landings,
            IList<CityExteriorStairRailDescriptor> rails,
            IList<CityExteriorStairRetainingWallDescriptor>
                retainingWalls)
        {
            Id = id ?? string.Empty;
            Flights = Copy(
                flights,
                nameof(flights));
            Landings = Copy(
                landings,
                nameof(landings));
            Rails = Copy(
                rails,
                nameof(rails));
            RetainingWalls = Copy(
                retainingWalls,
                nameof(retainingWalls));
        }

        public string Id { get; }
        public IReadOnlyList<CityExteriorStairFlightDescriptor>
            Flights { get; }
        public IReadOnlyList<CityExteriorStairLandingDescriptor>
            Landings { get; }
        public IReadOnlyList<CityExteriorStairRailDescriptor>
            Rails { get; }
        public IReadOnlyList<CityExteriorStairRetainingWallDescriptor>
            RetainingWalls { get; }

        private static IReadOnlyList<T> Copy<T>(
            IList<T> source,
            string parameterName)
        {
            if (source == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            return new ReadOnlyCollection<T>(new List<T>(source));
        }
    }
}
