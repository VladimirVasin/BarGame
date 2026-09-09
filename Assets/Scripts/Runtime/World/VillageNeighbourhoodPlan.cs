using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Household destinations and routes share the existing street and plot frames.</summary>
    public sealed class VillageNeighbourhoodPlan
    {
        public readonly AlpineVillagePlan Village;
        public readonly AlpineVillagePlotDescriptor WorkHouse, QuietHouse;
        public VillageNeighbourhoodPlan(AlpineVillagePlan village)
        {
            Village = village;
            WorkHouse = FindHouse("village-house-08");
            QuietHouse = FindHouse("village-house-11");
        }
        public AlpineVillagePlotDescriptor FindHouse(string id)
        {
            foreach (var plot in Village.Plots) if (plot.StableId == id) return plot;
            throw new InvalidOperationException("Missing village household " + id);
        }
        public Vector3 Ground(Vector3 p)
        {
            var xz = new Vector2(p.x, p.z);
            p.y = Mathf.Max(AlpineVillageTerrainSampler.SampleHeight(Village, xz),
                AlpineVillageTerrainSampler.SampleMeshHeight(Village, xz));
            if (Village.Station.PadArea.ContainsXZ(p))
                p.y = Mathf.Max(p.y, Village.Station.PadArea.Center.y + AlpineVillagePlanner.StationPadTopOffset);
            return p;
        }
        public Vector3 Yard(AlpineVillagePlotDescriptor plot, float across, float forward) =>
            Ground(plot.DoorGroundPosition + Vector3.Cross(Vector3.up, plot.Facing) * across + plot.Facing * forward);
        public Vector3 GatePosition => Yard(QuietHouse, -2.04f, 3.2f);
        public Vector3 BasketHome => Yard(QuietHouse, -1.5f, 1.05f);
        public Vector3 BasketVisit => Yard(WorkHouse, -1.5f, 1.05f);
        public Vector3 ShovelRack => Yard(QuietHouse, 2.1f, 0.82f);
        public Vector3 ShovelDock => Ground(ShovelRack + QuietHouse.Facing * 0.63f);
        public Vector3 ShovelWork => Yard(QuietHouse, 2.1f, 2.35f);
        public Vector3 BasketDock(AlpineVillagePlotDescriptor plot) =>
            Ground((plot == QuietHouse ? BasketHome : BasketVisit) + plot.Facing * 0.44f);
        public Vector3[] BetweenHouses(AlpineVillagePlotDescriptor from, AlpineVillagePlotDescriptor to)
        {
            var points = new List<Vector3> { from.DoorDockPosition, Village.Lane.Sample(from.LaneDistance).Position };
            float sign = Mathf.Sign(to.LaneDistance - from.LaneDistance);
            for (float d = from.LaneDistance + sign * 1.5f; sign * (to.LaneDistance - d) > 0; d += sign * 1.5f)
                points.Add(Village.Lane.Sample(d).Position);
            points.Add(Village.Lane.Sample(to.LaneDistance).Position);
            points.Add(to.DoorDockPosition);
            return points.ToArray();
        }
        public void AppendPaths(ICollection<AlpineVillagePathDescriptor> paths)
        {
            Add(WorkHouse, "basket", Yard(WorkHouse, 0f, 2.2f), Yard(WorkHouse, -1.5f, 2.2f));
            Add(WorkHouse, "basket-dock", Yard(WorkHouse, -1.5f, 2.2f), BasketDock(WorkHouse));
            Add(QuietHouse, "shovel", Yard(QuietHouse, 0f, 2.3f), Yard(QuietHouse, 2.1f, 2.3f));
            Add(QuietHouse, "shovel-rack", Yard(QuietHouse, 2.1f, 2.3f), ShovelDock);
            Add(QuietHouse, "basket", Yard(QuietHouse, 0f, 2.2f), Yard(QuietHouse, -1.5f, 2.2f));
            Add(QuietHouse, "basket-dock", Yard(QuietHouse, -1.5f, 2.2f), BasketDock(QuietHouse));
            Add(QuietHouse, "gate", Yard(QuietHouse, -1.5f, 2.2f), Yard(QuietHouse, -1.5f, 4.1f));
            Add(QuietHouse, "gate-approach", Yard(QuietHouse, -1.5f, 4.1f), Yard(QuietHouse, 0f, 4.1f));
            void Add(AlpineVillagePlotDescriptor p, string name, Vector3 a, Vector3 b) => paths.Add(
                new AlpineVillagePathDescriptor(p.StableId + "-work-" + name, p.StableId,
                    AlpineVillagePathKind.HouseholdWork, a, b, 0.38f, 0.42f));
        }
    }
}
