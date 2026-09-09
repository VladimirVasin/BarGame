using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The first inhabited courtyard. All positions are fixed metres in the real plot frame.</summary>
    public sealed class AlpineVillageLifePlan
    {
        public const string WoodHouseId = "village-house-04";
        public const int BasketCount = 2;
        public const float StandHeight = 0.38f;
        public const float PickupForward = 0.44f;

        private AlpineVillageLifePlan(AlpineVillagePlan village)
        {
            Village = village;
            foreach (var plot in village.Plots)
                if (plot.StableId == WoodHouseId) House = plot;
            if (House == null) throw new InvalidOperationException("Missing firewood courtyard house.");
            Forward = House.Facing;
            Right = Vector3.Cross(Vector3.up, Forward).normalized;
            Shelter = Yard(2.1f, 0.9f);
            Stack = Yard(2.1f, 0.9f);
            Block = Yard(4.15f, 3.9f);
            Sled = Yard(4.2f, 1.4f);
            Rest = Yard(-0.8f, 1.7f);
            Work = Yard(2.1f, 1.43f);
            Pickups = new Vector3[BasketCount];
            Deliveries = new Vector3[BasketCount];
            for (int i = 0; i < BasketCount; i++)
            {
                Pickups[i] = Yard(1.45f + i * 1.05f, 2.45f);
                Deliveries[i] = Yard(-1.5f - i * 1.05f, 1.02f);
            }
            var cable = village.Station.Cableway;
            StationForward = -cable.LineForward;
            StationCrate = village.Station.PadArea.Center - cable.LineRight * 2.7f -
                cable.LineForward * 1.55f + Vector3.up * AlpineVillagePlanner.StationPadTopOffset;
            StationWork = StationCrate + StationForward * 0.66f;
            StationRest = StationWork + cable.LineRight * 0.8f;
        }

        public AlpineVillagePlan Village { get; }
        public AlpineVillagePlotDescriptor House { get; }
        public Vector3 Forward { get; }
        public Vector3 Right { get; }
        public Vector3 Shelter { get; }
        public Vector3 Stack { get; }
        public Vector3 Block { get; }
        public Vector3 Sled { get; }
        public Vector3 Rest { get; }
        public Vector3 Work { get; }
        public Vector3[] Pickups { get; }
        public Vector3[] Deliveries { get; }
        public Vector3 StationCrate { get; }
        public Vector3 StationWork { get; }
        public Vector3 StationRest { get; }
        public Vector3 StationForward { get; }

        public static AlpineVillageLifePlan Create(AlpineVillagePlan village) =>
            new AlpineVillageLifePlan(village ?? throw new ArgumentNullException(nameof(village)));

        public Vector3 Yard(float across, float inFront)
        {
            Vector3 point = House.DoorGroundPosition + Right * across + Forward * inFront;
            return Ground(point);
        }

        public Vector3 Ground(Vector3 point)
        {
            var xz = new Vector2(point.x, point.z);
            point.y = Mathf.Max(AlpineVillageTerrainSampler.SampleHeight(Village, xz),
                AlpineVillageTerrainSampler.SampleMeshHeight(Village, xz));
            return point;
        }

        public Vector3 Dock(Vector3 support) => Ground(support + Forward * PickupForward);

        public Vector3[] CarryRoute(int basket)
        {
            Vector3 start = Dock(Pickups[basket]), end = Dock(Deliveries[basket]);
            // The existing threshold stays open between the two groups.
            return new[] { start, Yard(1.45f + basket * 1.05f, 3.5f),
                Yard(-1.5f - basket * 1.05f, 3.5f), end };
        }

        public void AppendWorkPaths(ICollection<AlpineVillagePathDescriptor> paths)
        {
            // Narrow worn working lines, not a cleared rectangular courtyard.
            Add("crossing", Yard(-2.55f, 3.5f), Yard(2.5f, 3.5f));
            for (int i = 0; i < BasketCount; i++)
            {
                Vector3[] route = CarryRoute(i);
                Add("pickup-" + i, route[0], route[1]);
                Add("delivery-" + i, route[2], route[3]);
            }
            Add("stack-side", Yard(3.3f, 3.5f), Yard(3.3f, 2f));
            Add("stack-front", Yard(3.3f, 2f), Yard(2.1f, 2f));
            Add("stack", Yard(2.1f, 2f), Work);
            Add("porch", House.DoorDockPosition, Rest);

            void Add(string name, Vector3 start, Vector3 end) => paths.Add(
                new AlpineVillagePathDescriptor("village-work-" + name, WoodHouseId,
                    AlpineVillagePathKind.HouseholdWork, start, end, 0.5f, 0.52f));
        }
    }
}
