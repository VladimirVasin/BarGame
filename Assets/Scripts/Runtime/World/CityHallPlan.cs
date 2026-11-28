using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The north-facing doorway is on the southern upper court. The clock faces west.</summary>
    public sealed class CityHallPlan
    {
        public readonly Vector3 Threshold;
        public readonly Rect Footprint;
        public readonly Rect ClockWing;
        public readonly IReadOnlyList<RuntimeOrientedBox> GroundSolids;
        public Vector3 Arrival => Threshold + Vector3.forward * 4.78f + Vector3.up * PlayerFactory.GroundedRootOffset;
        public Vector3 Return => Threshold + Vector3.forward * 2.48f + Vector3.up * PlayerFactory.GroundedRootOffset;
        public PlayerDoorActionPlan DoorAction => PlayerDoorActionPlan.CreateStationary(
            Threshold + new Vector3(.45f, 1.05f, .22f),
            Threshold + new Vector3(.19f, PlayerFactory.GroundedRootOffset, .79f), Vector3.back);

        internal CityHallPlan(CityTerraceDistrictPlan terrace)
        {
            Threshold = terrace.UpperArrivalWorld + new Vector3(3.12f, 0f, -8.5f);
            Footprint = new Rect(Threshold.x - 16.54f, Threshold.z - 12.56f, 18.47f, 12.56f);
            ClockWing = new Rect(Threshold.x - 16.54f, Threshold.z - .24f, 8.72f, 6.64f);
            var solids = new List<RuntimeOrientedBox>();
            Add(.13f, -3.03f, -5.65f, 2.94f, 13.74f, 10.65f);
            Add(0, 1.23f, -.19f, 1.6f, 2.46f, .20f);
            foreach (float x in new[] { -13.9f, -7.8f, -1.7f })
                Add(x, -2.83f, -10.98f, .95f, 13.4f, 1.26f);
            Add(-13.35f, -.92f, -10.97f, 4.4f, 9.62f, .76f);
            foreach (float x in new[] { -1.52f, 1.52f }) Add(x, 1.25f, 2.11f, .11f, 2.5f, 4.14f);
            GroundSolids = solids.AsReadOnly();
            void Add(float x,float y,float z,float width,float height,float depth) =>
                solids.Add(new RuntimeOrientedBox(Threshold + new Vector3(x,y,z), Quaternion.identity,
                    new Vector3(width,height,depth)));
        }

        public Vector3 GuardStation(int index) => Threshold + (index == 0
            ? new Vector3(-1.07f, 0f, .95f) : new Vector3(1.04f, 0f, 2.83f));
    }
}
