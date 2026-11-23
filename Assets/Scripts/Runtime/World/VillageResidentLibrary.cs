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
        [SerializeField] private GameObject snowNeighbor;
        [SerializeField] private GameObject basketVisitor;
        public bool IsComplete => stationWorker != null && woodWoman != null &&
            snowNeighbor != null && basketVisitor != null;
        public static VillageResidentLibrary Load() => Resources.Load<VillageResidentLibrary>(ResourcePath);
        public GameObject GetPrefab(VillageResidentRole role) => role switch
        {
            VillageResidentRole.StationWorker => stationWorker,
            VillageResidentRole.WoodWoman => woodWoman,
            VillageResidentRole.RepairNeighbor => DefaultNpcCatalog.GetPrefab(DefaultNpcCatalog.OrdinaryWorker),
            VillageResidentRole.SewingWoman => DefaultNpcCatalog.GetPrefab(DefaultNpcCatalog.OrdinaryWoman),
            VillageResidentRole.SnowNeighbor => snowNeighbor,
            VillageResidentRole.BasketVisitor => basketVisitor,
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        };
        public void Configure(GameObject worker, GameObject woman) { stationWorker = worker; woodWoman = woman; }
        public void Configure(GameObject[] cast)
        {
            if (cast == null || cast.Length != 6) throw new ArgumentException("The village has exactly six authored residents.", nameof(cast));
            stationWorker = cast[0]; woodWoman = cast[1]; snowNeighbor = cast[4]; basketVisitor = cast[5];
        }

        public VillageResidentPresentation Create(VillageResidentRole role, Transform parent)
        {
            if (role == VillageResidentRole.RepairNeighbor || role == VillageResidentRole.SewingWoman)
            {
                bool carpenter = role == VillageResidentRole.RepairNeighbor;
                var actor = DefaultNpcFactory.CreateForCharacter(parent, carpenter ? DefaultNpcPopulation.Severin : DefaultNpcPopulation.Marta);
                actor.Role = role;
                // The two work tracks were authored at these statures on the 1.75 m common rig.
                actor.transform.localScale = Vector3.one * ((carpenter ? 1.86f : 1.56f) / 1.75f);
                return actor;
            }
            GameObject prefab = GetPrefab(role);
            if (prefab == null) throw new InvalidOperationException("Village resident assets are incomplete.");
            var instance = Instantiate(prefab, parent, false);
            var presentation = instance.GetComponent<VillageResidentPresentation>();
            presentation.Initialize();
            // Role-bound village residents share the same footstep service
            // as the reusable bodies created through DefaultNpcFactory.
            NpcFootstepSources.Register(instance.transform);
            return presentation;
        }
    }
}
