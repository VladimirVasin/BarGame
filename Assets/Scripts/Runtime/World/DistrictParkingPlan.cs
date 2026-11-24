using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Two public lots in otherwise unbuilt strips between street mouths.
    /// The local X axis points into the bays and Z runs along the through aisle.</summary>
    internal sealed class DistrictParkingPlan
    {
        internal const int Capacity = RidgeParkingPlan.Capacity;
        internal const float HalfLength = 7.4f;
        internal const float AisleX = -2.4f;
        internal const float ParkX = 5.55f;
        internal const float MouthReach = 10.8f;
        internal const string AssetPath = "City/DistrictParking/DistrictParking";
        private const float QuarterTurnRadius = RidgeParkingPlan.TurnRadius;
        private static readonly ConditionalWeakTable<CityLayout, DistrictParkingPlan[]> Plans = new();

        private readonly CityStreetSupportPlan street;
        private readonly Vector3 negativeNode;
        private readonly Vector3 positiveNode;
        private readonly float negativeTop;
        private readonly float positiveTop;

        internal string Id { get; }
        internal CityDistrictKind District { get; }
        internal Vector3 Origin { get; }
        internal Quaternion Rotation { get; }
        internal Vector3 BayForward => Rotation * Vector3.right;
        internal Rect Lot { get; }
        internal IReadOnlyList<Rect> Surfaces { get; }
        internal IReadOnlyList<Rect> Curbs { get; }
        internal IReadOnlyList<StreetLampDescriptor> Lamps { get; }
        internal Vector3 PedestrianArrival => Point(-5.35f, 0f);

        internal static IReadOnlyList<DistrictParkingPlan> ForLayout(CityLayout layout) =>
            layout.SpatialPlan.IsCompactCoastal && layout.Seed == GameSessionState.DefaultCitySeed
                ? Plans.GetValue(layout, Create) : Array.Empty<DistrictParkingPlan>();

        private static DistrictParkingPlan[] Create(CityLayout layout)
        {
            CityStreetSupportPlan street = CityStreetSupportPlan.ForLayout(layout);
            return new[]
            {
                new DistrictParkingPlan(layout, street, "industrial-parking", CityDistrictKind.Industrial,
                    new Vector2Int(2, 2), new Vector2Int(3, 2)),
                new DistrictParkingPlan(layout, street, "residential-parking", CityDistrictKind.Residential,
                    new Vector2Int(8, 1), new Vector2Int(8, 2))
            };
        }

        private DistrictParkingPlan(CityLayout layout, CityStreetSupportPlan street, string id,
            CityDistrictKind district, Vector2Int from, Vector2Int to)
        {
            this.street = street;
            Id = id;
            District = district;
            negativeNode = layout.GetNodeWorldPosition(from);
            positiveNode = layout.GetNodeWorldPosition(to);
            Origin = Vector3.Lerp(negativeNode, positiveNode, .5f);
            Rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(positiveNode - negativeNode, Vector3.up));
            negativeTop = StreetHeight(AisleX, -MouthReach);
            positiveTop = StreetHeight(AisleX, MouthReach);

            Lot = RectAt(-6.2f, -HalfLength, 8.6f, HalfLength);
            Surfaces = Array.AsReadOnly(new[]
            {
                Lot,
                RectAt(-4.95f, -MouthReach, .1f, -HalfLength),
                RectAt(-4.95f, HalfLength, .1f, MouthReach)
            });
            Curbs = Array.AsReadOnly(new[]
            {
                RectAt(8.41f, -HalfLength, 8.59f, HalfLength),
                RectAt(-6.19f, -HalfLength, -6.01f, -1.1f),
                RectAt(-6.19f, 1.1f, -6.01f, HalfLength)
            });
            Lamps = Array.AsReadOnly(new[] { Lamp(-4.9f), Lamp(4.9f) });
        }

        private StreetLampDescriptor Lamp(float along)
        {
            Vector3 foot = Point(-6.05f, along);
            Vector3 target = Point(4.4f, along * .66f);
            return new StreetLampDescriptor(default, 0f, StreetLampSide.Right,
                foot, (target - foot).normalized, target, StreetLampPurpose.Parking);
        }

        internal static bool Reserves(CityLayout layout, Rect footprint)
        {
            foreach (DistrictParkingPlan site in ForLayout(layout))
                foreach (Rect surface in site.Surfaces)
                    if (surface.Overlaps(footprint)) return true;
            return false;
        }

        internal static bool Reserves(CityLayout layout, Vector3 position, float radius) =>
            Reserves(layout, new Rect(position.x - radius, position.z - radius, radius * 2f, radius * 2f));

        internal Vector3 Point(float x, float z)
        {
            Vector3 point = Origin + Rotation * new Vector3(x, 0f, z);
            point.y = Top(x, z);
            return point;
        }

        internal float Top(float x, float z)
        {
            float grade = Mathf.Lerp(negativeTop, positiveTop, Mathf.InverseLerp(-MouthReach, MouthReach, z));
            float distance = Mathf.Abs(z);
            if (distance <= HalfLength) return grade + .012f;
            if (distance >= MouthReach) return StreetHeight(x, z);
            // Keep the outer joint on the real street/sidewalk support,
            // including the small crossfall of the missing junction arm.
            float along = Mathf.InverseLerp(HalfLength, MouthReach, distance);
            float outerZ = Mathf.Sign(z) * MouthReach;
            float centerTop = z < 0f ? negativeTop : positiveTop;
            return grade + Mathf.Lerp(.012f, StreetHeight(x, outerZ) - centerTop + .008f, along);
        }

        internal bool TryTop(Vector2 xz, out float top)
        {
            foreach (Rect surface in Surfaces)
            {
                if (!surface.Contains(xz)) continue;
                Vector3 local = Quaternion.Inverse(Rotation) * (new Vector3(xz.x, Origin.y, xz.y) - Origin);
                top = Top(local.x, local.z);
                return true;
            }
            top = default;
            return false;
        }

        private float StreetHeight(float x, float z)
        {
            Vector3 point = Origin + Rotation * new Vector3(x, 0f, z);
            if (street.TrySampleTop(new Vector2(point.x, point.z), out float height, out _)) return height;
            return (z < 0f ? negativeNode.y : positiveNode.y) + CityStreetSurfacePlanner.RoadTop;
        }

        internal Rect Bay(int index)
        {
            float z = BayZ(index);
            return RectAt(ParkX - RidgeParkingPlan.BayLength * .5f, z - RidgeParkingPlan.BayWidth * .5f,
                ParkX + RidgeParkingPlan.BayLength * .5f, z + RidgeParkingPlan.BayWidth * .5f);
        }

        internal Vector3 ParkedPosition(int bay) => Point(ParkX, BayZ(bay));
        internal static float BayZ(int index) => (index - 1.5f) * RidgeParkingPlan.BayWidth;

        private Rect RectAt(float x0, float z0, float x1, float z1)
        {
            Vector3 a = Origin + Rotation * new Vector3(x0, 0f, z0);
            Vector3 b = Origin + Rotation * new Vector3(x1, 0f, z1);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z),
                Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z));
        }

        // The driving paths are used by acceptance tests, not by City traffic.
        internal LastRouteCarDrivePath Manoeuvre(int bay, bool departure)
        {
            float z = BayZ(bay);
            float direction = Mathf.Sign(z);
            float tangentZ = z - direction * QuarterTurnRadius;
            float aisleEnd = 13.5f;
            var points = new List<Vector3>(180);
            if (!departure)
                Line(AisleX, -direction * aisleEnd, AisleX, tangentZ);

            if (departure)
                Line(ParkX, z, AisleX + QuarterTurnRadius, z);
            const int arcSamples = 80;
            for (int sample = 0; sample <= arcSamples; sample++)
            {
                float fraction = (float)sample / arcSamples;
                float angle = (departure ? 1f - fraction : fraction) * Mathf.PI * .5f;
                points.Add(Point(AisleX + QuarterTurnRadius * (1f - Mathf.Cos(angle)),
                    tangentZ + direction * QuarterTurnRadius * Mathf.Sin(angle)));
            }
            if (!departure)
            {
                Line(AisleX + QuarterTurnRadius, z, ParkX, z);
                return new LastRouteCarDrivePath(points);
            }

            int reverseCount = points.Count;
            Line(AisleX, tangentZ, AisleX, direction * aisleEnd);
            return new LastRouteCarDrivePath(points, reverseCount);

            void Line(float x0, float z0, float x1, float z1)
            {
                int count = Mathf.CeilToInt(Vector2.Distance(new Vector2(x0, z0), new Vector2(x1, z1)) / .1f);
                for (int sample = 0; sample <= count; sample++)
                {
                    float fraction = (float)sample / count;
                    points.Add(Point(Mathf.Lerp(x0, x1, fraction), Mathf.Lerp(z0, z1, fraction)));
                }
            }
        }
    }
}
