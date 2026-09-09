using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class AlpineVillageLifeController
    {
        private VillageWorkroomController workroom;

        public void AttachWorkroom(VillageWorkroomController room)
        {
            workroom = room;
            foreach (Collider collider in room.Room.SolidColliders)
            {
                if (!solids.Contains(collider)) solids.Add(collider);
                if (!neighbourSolids.Contains(collider)) neighbourSolids.Add(collider);
            }
        }

        private void BeginIndoorWork(VillageNeighbourState n)
        {
            workroom.ReserveIndoorWork(n);
            VillageWorkroomPlan room = workroom.Plan;
            bool repair = n.Role == VillageResidentRole.RepairNeighbor;
            string dock = repair ? "RepairDock" : "SewingDock";
            Vector3 corner = room.World(new Vector3(-.65f, 0f, -.65f));
            Vector3[] approach = repair ? new[] { room.World(new Vector3(-1.20f, 0f, -.97f)) } :
                new[] { room.World(new Vector3(-1.15f, 0f, .05f)), room.World(new Vector3(-2.53f, 0f, .05f)),
                    room.World(new Vector3(-2.53f, 0f, 1.06f)) };
            // The direct right-hand approach crosses the low box shelf. Pass
            // between the bench and the chair, then enter ahead of its backrest.
            var inward = new List<Vector3> { n.Actor.transform.position, room.Anchor("Turn" + n.Slot),
                room.Anchor("Interior"), corner };
            inward.AddRange(approach); inward.Add(room.Anchor(dock));
            var outward = new List<Vector3> { room.Anchor(dock) };
            for (int i = approach.Length - 1; i >= 0; i--) outward.Add(approach[i]);
            outward.Add(corner); outward.Add(room.Anchor("Interior"));
            outward.Add(room.Anchor("Turn" + n.Slot)); outward.Add(room.Anchor("Hidden" + n.Slot));
            Add(n, VillageNeighbourTask.ReserveRoomRoute);
            Walk(n, inward.ToArray(), room.Facing(dock), true);
            Add(n, VillageNeighbourTask.Workroom);
            Add(n, VillageNeighbourTask.ReserveRoomRoute);
            Walk(n, outward.ToArray(), n.Home.Facing, true);
        }

        private bool RoomRouteIsClear(VillageNeighbourState walker)
        {
            foreach (VillageNeighbourState other in neighbours)
                if (other != walker && other.Home.StableId == VillageWorkroomPlan.HouseId &&
                    other.Steps.Count > 0 && other.Task == VillageNeighbourTask.Walk && other.Steps.Peek().Indoors) return false;
            return true;
        }

        public bool SayWorkroom(VillageResidentRole role, string key)
        {
            int index = (int)role;
            if (workroom == null || !workroom.CanSeeResident(neighbours[index].Actor)) return false;
            if (!bubbles.Show(neighbours[index].Actor, LocalizationService.Get(key))) return false;
            greeted[index] = true; speechRemaining[index] = NpcSpeechBubbleView.VisibleSeconds;
            speechCooldown[index] = 16f; LastGreetingKey = key;
            return true;
        }
    }
}
