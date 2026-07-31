using System;
using UnityEngine;

namespace BarPromenade
{
    public enum CityDecorationKind
    {
        OldTownChimneysAndDormers = 0,
        OldTownScaffolding = 1,
        OldTownStreetMarket = 2,
        OldTownClockTower = 3,
        ResidentialBalconies = 4,
        ResidentialLaundryAndAntenna = 5,
        ResidentialDiscardedFurniture = 6,
        ResidentialRooftopGreenhouse = 7,
        IndustrialStacksAndTanks = 8,
        IndustrialPipeRack = 9,
        IndustrialCargo = 10,
        IndustrialGantry = 11,
        NightlifeBillboard = 12,
        NightlifeFireEscape = 13,
        NightlifeVendingAndQueue = 14,
        NightlifeCinema = 15,
        RoadsideDumpsterAndUtility = 16,
        RoadsidePhoneBooth = 17,
        RoadsideBusShelter = 18,
        RoadsideRoadworkAndBicycle = 19,
        ParkFountainAndStatue = 20,
        ParkBandstand = 21,
        ParkChessTables = 22,
        ParkPlayground = 23
    }

    public enum CityDecorationAnchorKind
    {
        BuildingRoof = 0,
        BuildingFacade = 1,
        BuildingFrontage = 2,
        UrbanLandmark = 3,
        Roadside = 4,
        ParkFeature = 5,
        ParkLandmark = 6
    }

    public enum CityDecorationPalette
    {
        OldTownBrick = 0,
        OldTownStone = 1,
        ResidentialCool = 2,
        ResidentialWarm = 3,
        IndustrialSteel = 4,
        IndustrialRust = 5,
        NightlifeMagenta = 6,
        NightlifeCyan = 7,
        StreetUtility = 8,
        ParkStone = 9,
        ParkPaint = 10
    }

    public enum CityDecorationVisibilityTier
    {
        Near = 0,
        MidRange = 1,
        Landmark = 2
    }

    public enum CityDecorationCollisionTier
    {
        None = 0,
        Detail = 1,
        Blocking = 2
    }

    public readonly struct CityDecorationDescriptor :
        IEquatable<CityDecorationDescriptor>
    {
        public static readonly Vector2Int NoLot =
            new Vector2Int(-1, -1);

        public CityDecorationDescriptor(
            string stableId,
            CityDecorationKind kind,
            CityDecorationAnchorKind anchorKind,
            CityDistrictKind district,
            Vector2Int lotCell,
            Vector3 position,
            Vector3 forward,
            int variant,
            CityDecorationPalette palette,
            CityDecorationVisibilityTier visibilityTier,
            CityDecorationCollisionTier collisionTier)
        {
            StableId = stableId ?? string.Empty;
            Kind = kind;
            AnchorKind = anchorKind;
            District = district;
            LotCell = lotCell;
            Position = position;
            Forward = forward;
            Variant = variant;
            Palette = palette;
            VisibilityTier = visibilityTier;
            CollisionTier = collisionTier;
        }

        public string StableId { get; }
        public string Id => StableId;
        public CityDecorationKind Kind { get; }
        public CityDecorationAnchorKind AnchorKind { get; }
        public CityDistrictKind District { get; }
        public Vector2Int LotCell { get; }
        public Vector3 Position { get; }
        public Vector3 WorldPosition => Position;
        public Vector3 Forward { get; }
        public Vector3 WorldForward => Forward;
        public int Variant { get; }
        public CityDecorationPalette Palette { get; }
        public CityDecorationVisibilityTier VisibilityTier { get; }
        public CityDecorationCollisionTier CollisionTier { get; }

        public bool HasLotAnchor =>
            AnchorKind == CityDecorationAnchorKind.BuildingRoof ||
            AnchorKind == CityDecorationAnchorKind.BuildingFacade ||
            AnchorKind == CityDecorationAnchorKind.BuildingFrontage ||
            AnchorKind == CityDecorationAnchorKind.UrbanLandmark;

        public bool IsLandmark =>
            AnchorKind == CityDecorationAnchorKind.UrbanLandmark ||
            AnchorKind == CityDecorationAnchorKind.ParkLandmark;

        public bool TryResolveLot(
            CityLayout layout,
            out BuildingLot lot)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (!HasLotAnchor)
            {
                lot = null;
                return false;
            }

            for (int index = 0;
                 index < layout.BuildingLots.Count;
                 index++)
            {
                BuildingLot candidate = layout.BuildingLots[index];
                if (candidate.Cell == LotCell)
                {
                    lot = candidate;
                    return true;
                }
            }

            lot = null;
            return false;
        }

        public bool Equals(CityDecorationDescriptor other)
        {
            return string.Equals(
                       StableId,
                       other.StableId,
                       StringComparison.Ordinal) &&
                   Kind == other.Kind &&
                   AnchorKind == other.AnchorKind &&
                   District == other.District &&
                   LotCell == other.LotCell &&
                   Position.Equals(other.Position) &&
                   Forward.Equals(other.Forward) &&
                   Variant == other.Variant &&
                   Palette == other.Palette &&
                   VisibilityTier == other.VisibilityTier &&
                   CollisionTier == other.CollisionTier;
        }

        public override bool Equals(object obj)
        {
            return obj is CityDecorationDescriptor other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) +
                       StringComparer.Ordinal.GetHashCode(
                           StableId ?? string.Empty);
                hash = (hash * 31) + (int)Kind;
                hash = (hash * 31) + (int)AnchorKind;
                hash = (hash * 31) + (int)District;
                hash = (hash * 31) + LotCell.GetHashCode();
                hash = (hash * 31) + Position.GetHashCode();
                hash = (hash * 31) + Forward.GetHashCode();
                hash = (hash * 31) + Variant;
                hash = (hash * 31) + (int)Palette;
                hash = (hash * 31) + (int)VisibilityTier;
                hash = (hash * 31) + (int)CollisionTier;
                return hash;
            }
        }

        public static bool operator ==(
            CityDecorationDescriptor left,
            CityDecorationDescriptor right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            CityDecorationDescriptor left,
            CityDecorationDescriptor right)
        {
            return !left.Equals(right);
        }
    }
}
