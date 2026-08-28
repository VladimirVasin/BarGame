using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The one physical connection between the cemetery's north fence and
    /// the church precinct immediately beyond it. Both world builders read
    /// this immutable geometry so the gravel alley, fence opening and church
    /// courtyard cannot drift apart.
    /// </summary>
    public sealed class CityChurchCemeteryPassagePlan :
        IEquatable<CityChurchCemeteryPassagePlan>
    {
        internal CityChurchCemeteryPassagePlan(
            string id,
            float axisX,
            float boundaryZ,
            float openingWidth,
            Rect fenceOpeningBounds,
            Rect fenceBreakBounds,
            Rect sharedTraversalBounds,
            Rect cemeteryAlleyExtensionBounds,
            Rect cemeteryGrounds,
            Rect churchGrounds,
            float cemeteryGroundTopY,
            float churchGroundTopY)
        {
            Id = id ?? string.Empty;
            AxisX = axisX;
            BoundaryZ = boundaryZ;
            OpeningWidth = openingWidth;
            FenceOpeningBounds = fenceOpeningBounds;
            FenceBreakBounds = fenceBreakBounds;
            SharedTraversalBounds = sharedTraversalBounds;
            CemeteryAlleyExtensionBounds = cemeteryAlleyExtensionBounds;
            CemeteryGrounds = cemeteryGrounds;
            ChurchGrounds = churchGrounds;
            CemeteryGroundTopY = cemeteryGroundTopY;
            ChurchGroundTopY = churchGroundTopY;
        }

        public string Id { get; }

        /// <summary>World X of the existing transverse cemetery alley.</summary>
        public float AxisX { get; }

        /// <summary>The shared cemetery-north/church-south boundary.</summary>
        public float BoundaryZ { get; }

        /// <summary>Clear distance between the two fence end posts.</summary>
        public float OpeningWidth { get; }

        /// <summary>
        /// The clear, collider-free opening itself. Its shallow Z depth is
        /// the physical thickness of the cemetery fence.
        /// </summary>
        public Rect FenceOpeningBounds { get; }

        /// <summary>
        /// Interval removed from the fence runs. It is wider than the clear
        /// opening by half a post on each side, so ordinary end posts leave
        /// exactly <see cref="OpeningWidth"/> metres clear between them.
        /// </summary>
        public Rect FenceBreakBounds { get; }

        /// <summary>
        /// Radius-safe seam strip shared by both precincts. This is not a
        /// street access and must not enter CityOpenAreaAccessDescriptor.
        /// </summary>
        public Rect SharedTraversalBounds { get; }

        /// <summary>
        /// The missing last stretch of the existing cemetery cross alley,
        /// from its authored fence inset to the shared boundary.
        /// </summary>
        public Rect CemeteryAlleyExtensionBounds { get; }

        public Rect CemeteryGrounds { get; }
        public Rect ChurchGrounds { get; }
        public float CemeteryGroundTopY { get; }
        public float ChurchGroundTopY { get; }
        public float SignedStepToChurch =>
            ChurchGroundTopY - CemeteryGroundTopY;
        public float StepHeight => Mathf.Abs(SignedStepToChurch);

        public Vector3 CemeteryThreshold => new Vector3(
            AxisX,
            CemeteryGroundTopY,
            BoundaryZ);

        public Vector3 ChurchThreshold => new Vector3(
            AxisX,
            ChurchGroundTopY,
            BoundaryZ);

        public bool Equals(CityChurchCemeteryPassagePlan other)
        {
            return other != null &&
                   string.Equals(Id, other.Id, StringComparison.Ordinal) &&
                   AxisX.Equals(other.AxisX) &&
                   BoundaryZ.Equals(other.BoundaryZ) &&
                   OpeningWidth.Equals(other.OpeningWidth) &&
                   FenceOpeningBounds.Equals(other.FenceOpeningBounds) &&
                   FenceBreakBounds.Equals(other.FenceBreakBounds) &&
                   SharedTraversalBounds.Equals(other.SharedTraversalBounds) &&
                   CemeteryAlleyExtensionBounds.Equals(
                       other.CemeteryAlleyExtensionBounds) &&
                   CemeteryGrounds.Equals(other.CemeteryGrounds) &&
                   ChurchGrounds.Equals(other.ChurchGrounds) &&
                   CemeteryGroundTopY.Equals(other.CemeteryGroundTopY) &&
                   ChurchGroundTopY.Equals(other.ChurchGroundTopY);
        }

        public override bool Equals(object obj)
        {
            return obj is CityChurchCemeteryPassagePlan other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(
                    Id ?? string.Empty);
                hash = (hash * 397) ^ AxisX.GetHashCode();
                hash = (hash * 397) ^ BoundaryZ.GetHashCode();
                hash = (hash * 397) ^ OpeningWidth.GetHashCode();
                hash = (hash * 397) ^ FenceOpeningBounds.GetHashCode();
                hash = (hash * 397) ^ FenceBreakBounds.GetHashCode();
                hash = (hash * 397) ^ SharedTraversalBounds.GetHashCode();
                hash = (hash * 397) ^
                       CemeteryAlleyExtensionBounds.GetHashCode();
                hash = (hash * 397) ^ CemeteryGrounds.GetHashCode();
                hash = (hash * 397) ^ ChurchGrounds.GetHashCode();
                hash = (hash * 397) ^ CemeteryGroundTopY.GetHashCode();
                return (hash * 397) ^ ChurchGroundTopY.GetHashCode();
            }
        }
    }

    /// <summary>
    /// Selects a shared passage only when an existing cemetery cross alley
    /// can meet the church yard on a radius-safe, level-safe boundary span.
    /// </summary>
    public static class CityChurchCemeteryPassagePlanner
    {
        public const string DefaultId = "church-cemetery-passage";
        public const float OpeningWidth = 3f;

        internal const float FenceThickness = 0.16f;
        internal const float FencePostWidth = 0.18f;
        internal const float CemeteryFenceInset = 1.6f;
        internal const float CrossAlleyHalfWidth = 0.9f;
        internal const float CrossAlleySpacing = 20f;

        private const float GeometryTolerance = 0.001f;

        public static CityChurchCemeteryPassagePlan Create(
            CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            CityChurchPlan church = CityChurchPlanner.Create(layout);
            return Create(layout, church);
        }

        public static CityChurchCemeteryPassagePlan Create(
            CityLayout layout,
            CityChurchPlan church)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (church == null ||
                !TryGetCemeterySite(
                    layout,
                    out Rect cemeteryGrounds,
                    out CityOpenAreaAccessDescriptor cemeteryAccess))
            {
                return null;
            }

            // The authored cemetery cross alleys grow east from its west
            // street gate. A differently oriented custom precinct keeps its
            // old independent accesses instead of receiving invented paths.
            if (cemeteryAccess.OutwardNormal.x < 0.999f ||
                Mathf.Abs(cemeteryGrounds.yMax - church.Grounds.yMin) >
                    GeometryTolerance)
            {
                return null;
            }

            float sharedMinimumX = Mathf.Max(
                cemeteryGrounds.xMin,
                church.Grounds.xMin);
            float sharedMaximumX = Mathf.Min(
                cemeteryGrounds.xMax,
                church.Grounds.xMax);
            float halfOpening = OpeningWidth * 0.5f;
            float radius = CityGroundTraversalPlanner.MaximumAgentRadius;
            float minimumAxis = Mathf.Max(
                sharedMinimumX + halfOpening + radius,
                church.ModelFootprint.xMax + halfOpening + radius);
            float maximumAxis =
                sharedMaximumX - halfOpening - radius;

            List<float> depths =
                CityCemeteryPlanner.CreateCrossAlleyDepths(
                    cemeteryGrounds.width);
            for (int index = 0; index < depths.Count; index++)
            {
                float axisX = cemeteryGrounds.xMin + depths[index];
                if (axisX < minimumAxis - GeometryTolerance ||
                    axisX > maximumAxis + GeometryTolerance)
                {
                    continue;
                }

                float openingMinimumX = axisX - halfOpening;
                float openingMaximumX = axisX + halfOpening;
                if (!TryGetBoundarySurfaces(
                        layout,
                        cemeteryGrounds.yMax,
                        openingMinimumX,
                        openingMaximumX,
                        out CitySurfaceDescriptor cemeterySurface,
                        out CitySurfaceDescriptor churchSurface) ||
                    !CityRoadGroundBoundaryPlanner.IsGroundBoundarySafe(
                        layout,
                        cemeterySurface,
                        churchSurface,
                        true,
                        cemeteryGrounds.yMax,
                        openingMinimumX,
                        openingMaximumX))
                {
                    continue;
                }

                float halfPost = FencePostWidth * 0.5f;
                float halfSeam =
                    CityGroundTraversalPlanner.ConnectorReach;
                var plan = new CityChurchCemeteryPassagePlan(
                    DefaultId,
                    axisX,
                    cemeteryGrounds.yMax,
                    OpeningWidth,
                    Rect.MinMaxRect(
                        openingMinimumX,
                        cemeteryGrounds.yMax - FenceThickness * 0.5f,
                        openingMaximumX,
                        cemeteryGrounds.yMax + FenceThickness * 0.5f),
                    Rect.MinMaxRect(
                        openingMinimumX - halfPost,
                        cemeteryGrounds.yMax - FenceThickness * 0.5f,
                        openingMaximumX + halfPost,
                        cemeteryGrounds.yMax + FenceThickness * 0.5f),
                    Rect.MinMaxRect(
                        openingMinimumX,
                        cemeteryGrounds.yMax - halfSeam,
                        openingMaximumX,
                        cemeteryGrounds.yMax + halfSeam),
                    Rect.MinMaxRect(
                        axisX - CrossAlleyHalfWidth,
                        cemeteryGrounds.yMax - CemeteryFenceInset,
                        axisX + CrossAlleyHalfWidth,
                        cemeteryGrounds.yMax),
                    cemeteryGrounds,
                    church.Grounds,
                    cemeterySurface.PhysicalTopY,
                    churchSurface.PhysicalTopY);
                ValidateOrThrow(layout, church, plan);
                return plan;
            }

            return null;
        }

        public static void ValidateOrThrow(
            CityLayout layout,
            CityChurchPlan church,
            CityChurchCemeteryPassagePlan plan)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (church == null)
            {
                throw new ArgumentNullException(nameof(church));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            float radius = CityGroundTraversalPlanner.MaximumAgentRadius;
            if (string.IsNullOrWhiteSpace(plan.Id) ||
                Mathf.Abs(plan.OpeningWidth - OpeningWidth) >
                    GeometryTolerance ||
                Mathf.Abs(plan.FenceOpeningBounds.width - OpeningWidth) >
                    GeometryTolerance ||
                Mathf.Abs(plan.BoundaryZ - plan.CemeteryGrounds.yMax) >
                    GeometryTolerance ||
                Mathf.Abs(plan.BoundaryZ - plan.ChurchGrounds.yMin) >
                    GeometryTolerance ||
                plan.FenceOpeningBounds.xMin - radius <
                    church.ModelFootprint.xMax - GeometryTolerance ||
                plan.StepHeight >
                    CityRoadGroundBoundaryPlanner.MaximumSafeStep +
                    GeometryTolerance ||
                !Contains(plan.CemeteryGrounds,
                    plan.CemeteryAlleyExtensionBounds) ||
                plan.SharedTraversalBounds.xMin - radius <
                    plan.CemeteryGrounds.xMin - GeometryTolerance ||
                plan.SharedTraversalBounds.xMax + radius >
                    plan.CemeteryGrounds.xMax + GeometryTolerance)
            {
                throw new InvalidOperationException(
                    "The church-cemetery passage violates its site, " +
                    "capsule-clearance or ground-step contract.");
            }

            List<float> depths =
                CityCemeteryPlanner.CreateCrossAlleyDepths(
                    plan.CemeteryGrounds.width);
            bool matchesCrossAlley = false;
            for (int index = 0; index < depths.Count; index++)
            {
                float axis = plan.CemeteryGrounds.xMin + depths[index];
                if (Mathf.Abs(axis - plan.AxisX) <= GeometryTolerance)
                {
                    matchesCrossAlley = true;
                    break;
                }
            }

            if (!matchesCrossAlley)
            {
                throw new InvalidOperationException(
                    "The church-cemetery passage must extend a real " +
                    "cemetery cross alley.");
            }
        }

        private static bool TryGetCemeterySite(
            CityLayout layout,
            out Rect grounds,
            out CityOpenAreaAccessDescriptor access)
        {
            bool foundGround = false;
            grounds = default;
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                if (surface.Kind != CitySurfaceKind.CemeteryGround)
                {
                    continue;
                }

                grounds = foundGround
                    ? Encapsulate(grounds, surface.WorldBounds)
                    : surface.WorldBounds;
                foundGround = true;
            }

            bool foundAccess = false;
            access = default;
            for (int index = 0;
                 index < layout.OpenAreaAccesses.Count;
                 index++)
            {
                CityOpenAreaAccessDescriptor candidate =
                    layout.OpenAreaAccesses[index];
                if (candidate.Feature != CityAreaFeatureKind.Cemetery)
                {
                    continue;
                }

                if (foundAccess)
                {
                    return false;
                }

                access = candidate;
                foundAccess = true;
            }

            return foundGround && foundAccess;
        }

        private static bool TryGetBoundarySurfaces(
            CityLayout layout,
            float boundaryZ,
            float minimumX,
            float maximumX,
            out CitySurfaceDescriptor cemetery,
            out CitySurfaceDescriptor church)
        {
            cemetery = default;
            church = default;
            bool foundCemetery = false;
            bool foundChurch = false;
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                if (surface.WorldBounds.xMin > minimumX +
                        GeometryTolerance ||
                    surface.WorldBounds.xMax < maximumX -
                        GeometryTolerance)
                {
                    continue;
                }

                if (surface.Kind == CitySurfaceKind.CemeteryGround &&
                    Mathf.Abs(surface.WorldBounds.yMax - boundaryZ) <=
                        GeometryTolerance)
                {
                    cemetery = surface;
                    foundCemetery = true;
                }
                else if (surface.Kind == CitySurfaceKind.ChurchGround &&
                         Mathf.Abs(surface.WorldBounds.yMin - boundaryZ) <=
                            GeometryTolerance)
                {
                    church = surface;
                    foundChurch = true;
                }
            }

            return foundCemetery && foundChurch;
        }

        private static Rect Encapsulate(Rect first, Rect second)
        {
            return Rect.MinMaxRect(
                Mathf.Min(first.xMin, second.xMin),
                Mathf.Min(first.yMin, second.yMin),
                Mathf.Max(first.xMax, second.xMax),
                Mathf.Max(first.yMax, second.yMax));
        }

        private static bool Contains(Rect outer, Rect inner)
        {
            return inner.xMin >= outer.xMin - GeometryTolerance &&
                   inner.xMax <= outer.xMax + GeometryTolerance &&
                   inner.yMin >= outer.yMin - GeometryTolerance &&
                   inner.yMax <= outer.yMax + GeometryTolerance;
        }
    }
}
