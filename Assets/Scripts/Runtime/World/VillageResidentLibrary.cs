using System;
using UnityEngine;

namespace BarPromenade
{
    public enum VillageResidentRole { StationWorker, WoodWoman, RepairNeighbor, SewingWoman, SnowNeighbor, BasketVisitor }
    public enum VillageResidentAction
    {
        Idle, Walk, Reach, Carry, Place, StationWork, CarryWalk,
        DoorOpen, DoorClose, ShovelPickUp, ShovelWork, ShovelPutBack, Gust, ShovelHold,
        RepairTakeTool, RepairWork, RepairPutTool,
        SewingEnter, SewingUnpack, SewingWork, SewingFold, SewingStow, SewingExit, BucketFill, StationStrap
    }

    /// <summary>The six authored people; this catalogue never supplies city walkers.</summary>
    public sealed class VillageResidentLibrary : ScriptableObject
    {
        public const string ResourcePath = "VillageLife/VillageResidentLibrary";
        [SerializeField] private GameObject stationWorker;
        [SerializeField] private GameObject woodWoman;
        [SerializeField] private GameObject repairNeighbor;
        [SerializeField] private GameObject sewingWoman;
        [SerializeField] private GameObject snowNeighbor;
        [SerializeField] private GameObject basketVisitor;
        public bool IsComplete => stationWorker != null && woodWoman != null &&
            repairNeighbor != null && sewingWoman != null && snowNeighbor != null && basketVisitor != null;
        public static VillageResidentLibrary Load() => Resources.Load<VillageResidentLibrary>(ResourcePath);
        public GameObject GetPrefab(VillageResidentRole role) => role switch
        {
            VillageResidentRole.StationWorker => stationWorker,
            VillageResidentRole.WoodWoman => woodWoman,
            VillageResidentRole.RepairNeighbor => repairNeighbor,
            VillageResidentRole.SewingWoman => sewingWoman,
            VillageResidentRole.SnowNeighbor => snowNeighbor,
            VillageResidentRole.BasketVisitor => basketVisitor,
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        };
        public void Configure(GameObject worker, GameObject woman) { stationWorker = worker; woodWoman = woman; }
        public void Configure(GameObject[] cast)
        {
            if (cast == null || cast.Length != 6) throw new ArgumentException("The village has exactly six authored residents.", nameof(cast));
            stationWorker = cast[0]; woodWoman = cast[1]; repairNeighbor = cast[2];
            sewingWoman = cast[3]; snowNeighbor = cast[4]; basketVisitor = cast[5];
        }

        public VillageResidentPresentation Create(VillageResidentRole role, Transform parent)
        {
            GameObject prefab = GetPrefab(role);
            if (prefab == null) throw new InvalidOperationException("Village resident assets are incomplete.");
            var instance = Instantiate(prefab, parent, false);
            var presentation = instance.GetComponent<VillageResidentPresentation>();
            presentation.Initialize();
            return presentation;
        }
    }
}
