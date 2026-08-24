using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Stable gameplay geometry for the south-tunnel boundary. The visual
    /// tunnel may continue far beyond the decision plane; this plan owns only
    /// the point at which travel is attempted and the grounded pose used when
    /// that destination is unavailable.
    /// </summary>
    public readonly struct CityTunnelTravelPlan :
        IEquatable<CityTunnelTravelPlan>
    {
        public CityTunnelTravelPlan(
            string stableId,
            Vector3 portalGroundCenter,
            Vector3 axis,
            float openingWidth,
            float decisionDistance,
            float returnDistance,
            float walkableDepth,
            float floorSurfaceY,
            bool travelAvailable)
        {
            if (string.IsNullOrWhiteSpace(stableId))
            {
                throw new ArgumentException(
                    "A tunnel-travel plan requires a stable id.",
                    nameof(stableId));
            }

            Axis = FlattenAndNormalize(axis);
            ValidateFinitePositive(openingWidth, nameof(openingWidth));
            ValidateFinitePositive(
                decisionDistance,
                nameof(decisionDistance));
            ValidateFiniteNonNegative(
                returnDistance,
                nameof(returnDistance));
            ValidateFinitePositive(walkableDepth, nameof(walkableDepth));
            if (returnDistance >= decisionDistance)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(returnDistance),
                    returnDistance,
                    "The refusal return must remain cityward of the " +
                    "decision plane.");
            }

            if (walkableDepth <= decisionDistance)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(walkableDepth),
                    walkableDepth,
                    "The walkable tunnel must continue beyond its " +
                    "decision plane.");
            }

            if (!IsFinite(portalGroundCenter) || !IsFinite(floorSurfaceY))
            {
                throw new ArgumentException(
                    "Tunnel travel geometry must be finite.");
            }

            StableId = stableId.Trim();
            PortalGroundCenter = portalGroundCenter;
            OpeningWidth = openingWidth;
            DecisionDistance = decisionDistance;
            ReturnDistance = returnDistance;
            WalkableDepth = walkableDepth;
            FloorSurfaceY = floorSurfaceY;
            TravelAvailable = travelAvailable;
        }

        public string StableId { get; }
        public Vector3 PortalGroundCenter { get; }

        /// <summary>Direction from the city into the mountain.</summary>
        public Vector3 Axis { get; }

        public float OpeningWidth { get; }
        public float OpeningHalfWidth => OpeningWidth * 0.5f;
        public float DecisionDistance { get; }
        public float ReturnDistance { get; }
        public float WalkableDepth { get; }
        public float FloorSurfaceY { get; }
        public bool TravelAvailable { get; }

        public Vector3 DecisionPlaneCenter =>
            AtDistance(DecisionDistance, FloorSurfaceY);

        public Vector3 ReturnRootPosition =>
            AtDistance(
                ReturnDistance,
                FloorSurfaceY + PlayerFactory.GroundedRootOffset);

        public Quaternion ReturnRootRotation =>
            Quaternion.LookRotation(Axis, Vector3.up);

        public float GetSignedDistance(Vector3 worldPosition)
        {
            Vector3 offset = worldPosition - PortalGroundCenter;
            offset.y = 0f;
            return Vector3.Dot(offset, Axis);
        }

        public float GetLateralDistance(Vector3 worldPosition)
        {
            Vector3 right = Vector3.Cross(Vector3.up, Axis).normalized;
            Vector3 offset = worldPosition - PortalGroundCenter;
            offset.y = 0f;
            return Mathf.Abs(Vector3.Dot(offset, right));
        }

        public bool Equals(CityTunnelTravelPlan other)
        {
            return string.Equals(
                       StableId,
                       other.StableId,
                       StringComparison.Ordinal) &&
                   PortalGroundCenter.Equals(other.PortalGroundCenter) &&
                   Axis.Equals(other.Axis) &&
                   OpeningWidth.Equals(other.OpeningWidth) &&
                   DecisionDistance.Equals(other.DecisionDistance) &&
                   ReturnDistance.Equals(other.ReturnDistance) &&
                   WalkableDepth.Equals(other.WalkableDepth) &&
                   FloorSurfaceY.Equals(other.FloorSurfaceY) &&
                   TravelAvailable == other.TravelAvailable;
        }

        public override bool Equals(object obj)
        {
            return obj is CityTunnelTravelPlan other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(
                    StableId ?? string.Empty);
                hash = (hash * 397) ^ PortalGroundCenter.GetHashCode();
                hash = (hash * 397) ^ Axis.GetHashCode();
                hash = (hash * 397) ^ OpeningWidth.GetHashCode();
                hash = (hash * 397) ^ DecisionDistance.GetHashCode();
                hash = (hash * 397) ^ ReturnDistance.GetHashCode();
                hash = (hash * 397) ^ WalkableDepth.GetHashCode();
                hash = (hash * 397) ^ FloorSurfaceY.GetHashCode();
                return (hash * 397) ^ TravelAvailable.GetHashCode();
            }
        }

        private Vector3 AtDistance(float distance, float y)
        {
            Vector3 result = PortalGroundCenter + Axis * distance;
            result.y = y;
            return result;
        }

        private static Vector3 FlattenAndNormalize(Vector3 direction)
        {
            if (!IsFinite(direction))
            {
                throw new ArgumentException(
                    "A tunnel axis must be finite.",
                    nameof(direction));
            }

            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                throw new ArgumentException(
                    "A tunnel axis must have an XZ component.",
                    nameof(direction));
            }

            return direction.normalized;
        }

        private static void ValidateFinitePositive(
            float value,
            string parameterName)
        {
            if (!IsFinite(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static void ValidateFiniteNonNegative(
            float value,
            string parameterName)
        {
            if (!IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// Pure one-way boundary state. A refusal disarms the plane until the
    /// player has genuinely retreated to the authored return distance.
    /// </summary>
    public sealed class CityTunnelTravelCrossingModel
    {
        private readonly CityTunnelTravelPlan plan;
        private bool hasSample;
        private bool isArmed;
        private float previousDistance;

        public CityTunnelTravelCrossingModel(CityTunnelTravelPlan travelPlan)
        {
            plan = travelPlan;
        }

        public bool HasSample => hasSample;
        public bool IsArmed => isArmed;

        public bool Observe(Vector3 worldPosition)
        {
            float distance = plan.GetSignedDistance(worldPosition);
            float lateral = plan.GetLateralDistance(worldPosition);
            if (!hasSample)
            {
                hasSample = true;
                previousDistance = distance;
                isArmed = distance < plan.DecisionDistance;
                return false;
            }

            if (!isArmed && distance <= plan.ReturnDistance)
            {
                isArmed = true;
            }

            bool crossedInward =
                isArmed &&
                previousDistance < plan.DecisionDistance &&
                distance >= plan.DecisionDistance &&
                lateral <= plan.OpeningHalfWidth;
            previousDistance = distance;
            if (crossedInward)
            {
                isArmed = false;
            }

            return crossedInward;
        }

        public void Reset()
        {
            hasSample = false;
            isArmed = false;
            previousDistance = 0f;
        }
    }
}
