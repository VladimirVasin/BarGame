using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum CityDistrictPointOfInterestKind
    {
        OldTownWaterworksCourt = 0,
        ResidentialDryingYard = 1,
        IndustrialWeighbridge = 2,
        NightlifeLastRouteIsland = 3
    }

    public readonly struct CityDistrictPointOfInterestAccessDescriptor
    {
        internal CityDistrictPointOfInterestAccessDescriptor(
            string id,
            Vector2Int streetSideDirection,
            Vector3 center,
            Vector3 outwardNormal,
            float width,
            Rect approachBounds,
            RoadEdge frontageEdge)
        {
            Id = id ?? string.Empty;
            StreetSideDirection = streetSideDirection;
            Center = center;
            OutwardNormal = outwardNormal;
            Width = width;
            ApproachBounds = approachBounds;
            FrontageEdge = frontageEdge;
        }

        public string Id { get; }

        // Direction from the public-space center towards its adjacent street.
        public Vector2Int StreetSideDirection { get; }

        // Center of the fully open street/public-space boundary.
        public Vector3 Center { get; }

        // Fence-boundary normal from the street into the public space.
        public Vector3 OutwardNormal { get; }

        // Full open width along the public-space street side.
        public float Width { get; }

        // XZ rectangle bridging the street and the interior of the space.
        public Rect ApproachBounds { get; }

        public RoadEdge FrontageEdge { get; }
    }

    public sealed class CityDistrictPointOfInterestDescriptor
    {
        internal CityDistrictPointOfInterestDescriptor(
            string id,
            CityDistrictKind district,
            CityDistrictPointOfInterestKind kind,
            Vector2Int cell,
            Vector3 center,
            Rect publicBounds,
            IList<CityDistrictPointOfInterestAccessDescriptor> accesses)
        {
            Id = id ?? string.Empty;
            District = district;
            Kind = kind;
            Cell = cell;
            Center = center;
            PublicBounds = publicBounds;
            Accesses =
                new ReadOnlyCollection<
                    CityDistrictPointOfInterestAccessDescriptor>(
                    new List<
                        CityDistrictPointOfInterestAccessDescriptor>(
                        accesses ??
                        throw new ArgumentNullException(nameof(accesses))));
        }

        public string Id { get; }
        public CityDistrictKind District { get; }
        public CityDistrictPointOfInterestKind Kind { get; }
        public Vector2Int Cell { get; }
        public Vector3 Center { get; }

        // XZ rectangle occupied by the open public ground.
        public Rect PublicBounds { get; }

        public IReadOnlyList<CityDistrictPointOfInterestAccessDescriptor>
            Accesses { get; }
    }
}
