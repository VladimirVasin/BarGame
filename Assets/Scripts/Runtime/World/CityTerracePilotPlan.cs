using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct CityTerracePilotFloor
    {
        internal CityTerracePilotFloor(string id, Vector3 center, Vector2 size, bool isCourt,
            string modelName = null, float thickness = 0f, bool hasFoundation = true)
        {
            Id = id; Center = center; Size = size; IsCourt = isCourt;
            ModelName = modelName ?? (isCourt ? "TerraceCourt" : "TerraceLanding");
            Thickness = thickness > 0f ? thickness : isCourt ? .30f : .25f;
            HasFoundation = hasFoundation;
        }

        public string Id { get; }
        /// <summary>The top plane, in metres above the prototype's zero datum.</summary>
        public Vector3 Center { get; }
        /// <summary>Planar X and Z dimensions.</summary>
        public Vector2 Size { get; }
        public bool IsCourt { get; }
        public string ModelName { get; }
        public float Thickness { get; }
        /// <summary>An elevated bridge owns a deck, not the ground below it.</summary>
        public bool HasFoundation { get; }
        public Rect Bounds => new Rect(Center.x - Size.x * .5f, Center.z - Size.y * .5f, Size.x, Size.y);
    }

    /// <summary>
    /// Isolated two-route courtyard study. All floor, flight and route positions
    /// are ground positions; only SpawnPosition includes the hero root offset.
    /// There are no stacked walkable levels at one XZ in this pilot.
    /// </summary>
    public sealed class CityTerracePilotPlan
    {
        public const int StepsPerFlight = 12;
        public const float StepRise = .16f;
        public const float TreadDepth = .32f;
        public const float FlightWidth = 1.8f;
        public const float FlightRise = StepsPerFlight * StepRise;
        public const float FlightRun = StepsPerFlight * TreadDepth;
        public const float CourtSize = 10f;
        public const float LandingSize = 2.4f;
        private const float Epsilon = .0001f;

        private CityTerracePilotPlan()
        {
            var floors = new List<CityTerracePilotFloor>(9)
            {
                new CityTerracePilotFloor("lower-court", Vector3.zero, Vector2.one * CourtSize, true)
            };
            var flights = new List<CityExteriorStairFlightDescriptor>(8);
            WestRoute = AddRoute(-1, "west", floors, flights);
            EastRoute = AddRoute(1, "east", floors, flights);
            Floors = new ReadOnlyCollection<CityTerracePilotFloor>(floors);
            Flights = new ReadOnlyCollection<CityExteriorStairFlightDescriptor>(flights);

            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            foreach (CityTerracePilotFloor floor in floors)
            {
                bounds.Encapsulate(floor.Center + new Vector3(floor.Size.x, 0f, floor.Size.y) * .5f);
                bounds.Encapsulate(floor.Center - new Vector3(floor.Size.x, 0f, floor.Size.y) * .5f);
            }
            foreach (CityExteriorStairFlightDescriptor flight in flights)
            {
                bounds.Encapsulate(flight.Start - flight.Right * (flight.Width * .5f));
                bounds.Encapsulate(flight.Start + flight.Right * (flight.Width * .5f));
                bounds.Encapsulate(flight.End - flight.Right * (flight.Width * .5f));
                bounds.Encapsulate(flight.End + flight.Right * (flight.Width * .5f));
            }
            Bounds = bounds;
        }

        public IReadOnlyList<CityTerracePilotFloor> Floors { get; }
        public IReadOnlyList<CityExteriorStairFlightDescriptor> Flights { get; }
        public IReadOnlyList<Vector3> WestRoute { get; }
        public IReadOnlyList<Vector3> EastRoute { get; }
        public Vector3 SpawnPosition => new Vector3(0f, PlayerFactory.GroundedRootOffset, -2f);
        public float TotalRise => FlightRise * 4f;
        public Bounds Bounds { get; }

        public static CityTerracePilotPlan Create() => new CityTerracePilotPlan();

        /// <summary>Samples the continuous capsule support, not the visual treads.</summary>
        public bool TryGetSurfaceHeight(Vector3 position, out float height)
        {
            height = 0f;
            if (!Finite(position)) return false;
            bool found = false;
            for (int i = 0; i < Floors.Count; i++)
            {
                CityTerracePilotFloor floor = Floors[i];
                if (!Inside(floor.Bounds, position.x, position.z)) continue;
                height = found ? Mathf.Max(height, floor.Center.y) : floor.Center.y;
                found = true;
            }
            for (int i = 0; i < Flights.Count; i++)
            {
                CityExteriorStairFlightDescriptor flight = Flights[i];
                if (!Inside(FlightBounds(flight), position.x, position.z)) continue;
                float run = Vector3.Dot(position - flight.Start, flight.Direction);
                float value = flight.BaseElevation + Mathf.Clamp01(run / flight.RunLength) * flight.TotalRise;
                height = found ? Mathf.Max(height, value) : value;
                found = true;
            }
            return found;
        }

        internal static Rect FlightBounds(CityExteriorStairFlightDescriptor flight)
        {
            Vector3 halfWidth = flight.Right * (flight.Width * .5f);
            Vector3 first = Vector3.Min(flight.Start - halfWidth, flight.End - halfWidth);
            Vector3 last = Vector3.Max(flight.Start + halfWidth, flight.End + halfWidth);
            // Right can face negative X/Z, so include both sides explicitly.
            first = Vector3.Min(first, Vector3.Min(flight.Start + halfWidth, flight.End + halfWidth));
            last = Vector3.Max(last, Vector3.Max(flight.Start - halfWidth, flight.End - halfWidth));
            return Rect.MinMaxRect(first.x, first.z, last.x, last.z);
        }

        internal static bool Inside(Rect bounds, float x, float z) =>
            x >= bounds.xMin - Epsilon && x <= bounds.xMax + Epsilon &&
            z >= bounds.yMin - Epsilon && z <= bounds.yMax + Epsilon;

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);

        private static IReadOnlyList<Vector3> AddRoute(int side, string id,
            List<CityTerracePilotFloor> floors, List<CityExteriorStairFlightDescriptor> flights)
        {
            var first = Flight(id + "-flight-1", new Vector3(side * 3f, 0f, 5f), Vector3.forward);
            var landingOne = new Vector3(side * 3f, FlightRise, 10.04f);
            var second = Flight(id + "-flight-2", new Vector3(side * 4.2f, FlightRise, 10.04f), Vector3.right * side);
            var middleCourt = new Vector3(side * 13.04f, FlightRise * 2f, 10.04f);
            var third = Flight(id + "-flight-3", new Vector3(side * 15f, FlightRise * 2f, 15.04f), Vector3.forward);
            var landingTwo = new Vector3(side * 15f, FlightRise * 3f, 20.08f);
            var fourth = Flight(id + "-flight-4", new Vector3(side * 13.8f, FlightRise * 3f, 20.08f), Vector3.left * side);
            var upperCourt = new Vector3(side * 5f, FlightRise * 4f, 20.08f);
            floors.Add(new CityTerracePilotFloor(id + "-landing-1", landingOne, Vector2.one * LandingSize, false));
            floors.Add(new CityTerracePilotFloor(id + "-middle-court", middleCourt, Vector2.one * CourtSize, true));
            floors.Add(new CityTerracePilotFloor(id + "-landing-2", landingTwo, Vector2.one * LandingSize, false));
            floors.Add(new CityTerracePilotFloor(id + "-upper-court", upperCourt, Vector2.one * CourtSize, true));
            flights.Add(first); flights.Add(second); flights.Add(third); flights.Add(fourth);
            return new ReadOnlyCollection<Vector3>(new List<Vector3>
            {
                new Vector3(0f, 0f, -2f), new Vector3(side * 3f, 0f, 4f),
                first.Start, first.End, landingOne, second.Start, second.End, middleCourt,
                new Vector3(side * 15f, FlightRise * 2f, 14.04f), third.Start, third.End,
                landingTwo, fourth.Start, fourth.End, upperCourt,
                new Vector3(0f, FlightRise * 4f, 20.08f)
            });
        }

        private static CityExteriorStairFlightDescriptor Flight(string id, Vector3 start, Vector3 direction) =>
            new CityExteriorStairFlightDescriptor(id, start, direction, FlightWidth, StepsPerFlight,
                StepRise, TreadDepth, CityExteriorStairDropSide.Both);
    }
}
