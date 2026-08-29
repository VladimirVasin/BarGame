using System;
using UnityEngine;

namespace BarPromenade
{
    public static class CityArchShelterPlacementResolver
    {
        public static readonly Vector2Int WestCell = new Vector2Int(10, 5);
        public static readonly Vector2Int EastCell = new Vector2Int(11, 5);

        public const float PortalInset = 0.35f;
        public const float AuthoredGalleryDepth = 3f;
        public const float GalleryLocalZ = -1.55f;
        public const float StepBandLocalZ = 2.25f;
        public const float StepBandDepth = 1.60f;
        public const float StepRun = 3.10f;
        public const float StepUpperOverlap = 0.12f;
        public const float UpperLandingLength = 1.50f;
        // The visible east facade support occupies the last 0.296 m of the
        // measured building gap. The service terrace terminates against its
        // inner face rather than penetrating the wall volume.
        public const float PlatformWallInset = 0.296f;
        public const float PlatformGuardRailHeight = 1.09f;
        public const float PlatformGuardRailThickness = 0.12f;
        public const float FacadeAttachmentOverlap = 0.19f;
        public const float MinimumClearHeight = 3.4f;
        public const float GalleryCrownRise = 1.66f;

        public static bool TryCreate(
            CityLayout layout,
            out CityArchShelterPlacement placement)
        {
            placement = default;
            if (layout == null ||
                !string.Equals(
                    layout.BlueprintId,
                    CityBlueprintCatalog.DefaultBlueprintId,
                    StringComparison.Ordinal) ||
                layout.Seed != GameSessionState.DefaultCitySeed ||
                layout.HasRoad(
                    RoadEdge.ForCellFrontage(
                        WestCell,
                        Vector2Int.right)) ||
                !TryFindLot(layout, WestCell, out BuildingLot westLot) ||
                !TryFindLot(layout, EastCell, out BuildingLot eastLot) ||
                !IsSupportedLot(westLot) ||
                !IsSupportedLot(eastLot) ||
                !TryFindSurface(
                    layout,
                    WestCell,
                    out CitySurfaceDescriptor westSurface) ||
                !TryFindSurface(
                    layout,
                    EastCell,
                    out CitySurfaceDescriptor eastSurface))
            {
                return false;
            }

            // The one authored shell has an east-rising stair group. The
            // production default layout is deterministic in that orientation;
            // rejecting a synthetic reversed datum is safer than rotating the
            // stair into the opposite north/south band around the facade root.
            if (westSurface.PhysicalTopY >= eastSurface.PhysicalTopY)
            {
                return false;
            }

            Bounds westBounds = ResolveExpectedBuildingBounds(westLot);
            Bounds eastBounds = ResolveExpectedBuildingBounds(eastLot);
            float xMin = westBounds.max.x;
            float xMax = eastBounds.min.x;
            float commonZMin = Mathf.Max(
                westBounds.min.z,
                eastBounds.min.z);
            float commonZMax = Mathf.Min(
                westBounds.max.z,
                eastBounds.max.z);
            float zMin = commonZMin + PortalInset;
            float zMax = commonZMax - PortalInset;
            if (xMax <= xMin || zMax <= zMin)
            {
                return false;
            }

            Rect commonFacade = Rect.MinMaxRect(
                xMin,
                commonZMin,
                xMax,
                commonZMax);
            Rect passage = Rect.MinMaxRect(xMin, zMin, xMax, zMax);
            float galleryDepth = Mathf.Min(
                AuthoredGalleryDepth,
                passage.height - PortalInset * 2f);
            if (galleryDepth <= 0f)
            {
                return false;
            }

            Rect tableau = Rect.MinMaxRect(
                passage.xMin,
                passage.center.y + GalleryLocalZ - galleryDepth * 0.5f,
                passage.xMax,
                passage.center.y + GalleryLocalZ + galleryDepth * 0.5f);
            Rect sheltered = commonFacade;
            float sharedBoundaryX =
                (westSurface.WorldBounds.xMax +
                 eastSurface.WorldBounds.xMin) * 0.5f;
            float westY = westSurface.PhysicalTopY;
            float eastY = eastSurface.PhysicalTopY;
            float stepXMin = westY <= eastY
                ? sharedBoundaryX - StepRun
                : sharedBoundaryX - StepUpperOverlap;
            float stepXMax = westY <= eastY
                ? sharedBoundaryX + StepUpperOverlap
                : sharedBoundaryX + StepRun;
            Rect railSuppression = Rect.MinMaxRect(
                stepXMin,
                passage.center.y + StepBandLocalZ - StepBandDepth * 0.5f,
                stepXMax,
                passage.center.y + StepBandLocalZ + StepBandDepth * 0.5f);

            float lowerY = Mathf.Min(westY, eastY);
            float upperY = Mathf.Max(westY, eastY);
            float roofBottom = upperY + MinimumClearHeight;
            float roofTop = roofBottom + GalleryCrownRise;
            var structureBounds = new Bounds();
            structureBounds.SetMinMax(
                new Vector3(
                    passage.xMin - FacadeAttachmentOverlap,
                    lowerY,
                    commonFacade.yMin),
                new Vector3(
                    passage.xMax + FacadeAttachmentOverlap,
                    roofTop,
                    commonFacade.yMax));
            Quaternion structureRotation = westY <= eastY
                ? Quaternion.identity
                : Quaternion.Euler(0f, 180f, 0f);
            Vector3 structurePosition = new Vector3(
                passage.center.x,
                lowerY,
                passage.center.y);

            placement = new CityArchShelterPlacement(
                WestCell,
                EastCell,
                westBounds,
                eastBounds,
                commonFacade,
                passage,
                sheltered,
                tableau,
                railSuppression,
                westY,
                eastY,
                sharedBoundaryX,
                structurePosition,
                structureRotation,
                structureBounds);
            return true;
        }

        public static Bounds ResolveExpectedBuildingBounds(BuildingLot lot)
        {
            if (!IsSupportedLot(lot))
            {
                throw new ArgumentException(
                    "An arch-shelter building must be an ordinary " +
                    "Nightlife lot.",
                    nameof(lot));
            }

            Vector3 envelope = CityBuildingAssetProvider
                .GetExpectedEnvelope(lot.District);
            Vector3 forward = CityBuildingPrototypePlacement
                .ResolveForward(lot);
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 baseAnchor = lot.HasRoadFrontage
                ? lot.DoorPosition +
                  Vector3.up * CityFacadeGrid.MassBaseElevation
                : lot.Center +
                  Vector3.up * CityFacadeGrid.MassBaseElevation;
            Vector3 center = baseAnchor - forward * (envelope.z * 0.5f) +
                             Vector3.up * (envelope.y * 0.5f);
            Vector3 extents = Abs(right) * (envelope.x * 0.5f) +
                              Vector3.up * (envelope.y * 0.5f) +
                              Abs(forward) * (envelope.z * 0.5f);
            return new Bounds(center, extents * 2f);
        }

        private static bool TryFindLot(
            CityLayout layout,
            Vector2Int cell,
            out BuildingLot lot)
        {
            for (int index = 0; index < layout.BuildingLots.Count; index++)
            {
                BuildingLot candidate = layout.BuildingLots[index];
                if (candidate != null && candidate.Cell == cell)
                {
                    lot = candidate;
                    return true;
                }
            }

            lot = null;
            return false;
        }

        private static bool TryFindSurface(
            CityLayout layout,
            Vector2Int cell,
            out CitySurfaceDescriptor surface)
        {
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor candidate = layout.Surfaces[index];
                if (candidate.Cell == cell &&
                    candidate.Kind == CitySurfaceKind.BuildableGround)
                {
                    surface = candidate;
                    return true;
                }
            }

            surface = default;
            return false;
        }

        private static bool IsSupportedLot(BuildingLot lot)
        {
            return lot != null &&
                   lot.IsOrdinaryBuilding &&
                   lot.District == CityDistrictKind.Nightlife;
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(
                Mathf.Abs(value.x),
                Mathf.Abs(value.y),
                Mathf.Abs(value.z));
        }
    }
}
