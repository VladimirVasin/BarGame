using UnityEngine;

namespace BarPromenade
{
    public enum MothersHouseLightingRoom
    {
        GroundMain, GroundNiche, Stairs, Corridor, ParentsBedroom, ChildhoodBedroom, Bathroom
    }

    /// <summary>A finite physical room, independent of presentation cutaways.</summary>
    public readonly struct MothersHouseLightingRoomInfo
    {
        public readonly MothersHouseLightingRoom Id;
        public readonly Bounds Bounds;
        public MothersHouseLightingRoomInfo(MothersHouseLightingRoom id, Bounds bounds)
        { Id = id; Bounds = bounds; }
    }
}
