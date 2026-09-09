using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Occasional household routes follow the same visible side paths as the visitor.</summary>
    public sealed class VillageErrandPlan
    {
        public readonly AlpineVillageLifePlan Life;
        public readonly VillageNeighbourhoodPlan Neighbourhood;
        public readonly AlpineVillagePlotDescriptor Chapel, Spring;
        public readonly VillageSnowPatch PorchPatch, ChapelPatch, StationPatch;
        public readonly IReadOnlyList<VillageSnowPatch> SnowPatches;
        public readonly Vector3 BucketSupport, BucketDock, SpringDock, SpringFacing, BucketDip;
        public readonly Vector3[] SpringPath, ChapelPath;

        public VillageErrandPlan(AlpineVillageLifePlan life, VillageNeighbourhoodPlan neighbourhood)
        {
            Life = life ?? throw new ArgumentNullException(nameof(life));
            Neighbourhood = neighbourhood ?? throw new ArgumentNullException(nameof(neighbourhood));
            Chapel = neighbourhood.FindHouse("village-chapel");
            Spring = neighbourhood.FindHouse("village-spring");
            var home = neighbourhood.QuietHouse;
            Vector3 right = Vector3.Cross(Vector3.up, home.Facing);
            PorchPatch = new VillageSnowPatch(VillageHouseholdProgress.PorchClearingId,
                Ground(neighbourhood.ShovelWork + right * .60f), home.Facing);
            Vector3 chapelRight = Vector3.Cross(Vector3.up, Chapel.Facing);
            // The doorstep and crossing source path are already bare. Work on
            // their snow edge, where the same depth field still has material.
            ChapelPatch = new VillageSnowPatch(VillageHouseholdProgress.ChapelClearingId,
                Ground(Chapel.DoorGroundPosition + Chapel.Facing * 3.25f + chapelRight * 3.25f), chapelRight);
            var pad = life.Village.Station.PadArea;
            Vector3 stationOut = -life.Village.Station.Cableway.LineForward;
            StationPatch = new VillageSnowPatch(VillageHouseholdProgress.StationEdgeClearingId,
                Ground(pad.Center + stationOut * (pad.HalfSize.y + 2.35f) + pad.Right * 1.75f), stationOut);
            SnowPatches = new[] { PorchPatch, ChapelPatch, StationPatch };
            BucketSupport = neighbourhood.Yard(home, -.70f, 1.05f);
            BucketDock = Ground(BucketSupport + home.Facing * .44f);
            var brook = life.Village.Brook;
            SpringFacing = -brook.LedgeFacing;
            SpringDock = Ground(brook.BowlCenter + brook.LedgeFacing * (brook.CatchOuterSize.y * .5f + .33f));
            BucketDip = brook.BowlCenter + brook.LedgeFacing * .20f;
            // On its side the mouth reaches the shallow water while the real
            // metal wall stays above the basin bed. Upright dipping cannot fill
            // a tall pail from this eight-centimetre-deep catch.
            BucketDip.y = brook.BowlWaterTopY + .165f;
            var paths = AlpineVillagePathPlanner.Create(life.Village);
            SpringPath = Route(paths, AlpineVillagePathKind.SpringSpur);
            ChapelPath = Route(paths, AlpineVillagePathKind.ChapelSpur);
        }

        public Vector3 Ground(Vector3 p) => Neighbourhood.Ground(p);
        private static Vector3[] Route(IReadOnlyList<AlpineVillagePathDescriptor> paths, AlpineVillagePathKind kind)
        {
            var points = new List<Vector3>();
            foreach (var path in paths)
            {
                if (path.Kind != kind) continue;
                if (points.Count == 0) points.Add(path.Start);
                points.Add(path.End);
            }
            if (points.Count < 2) throw new InvalidOperationException("Missing actual village errand path " + kind);
            return points.ToArray();
        }
    }
}
