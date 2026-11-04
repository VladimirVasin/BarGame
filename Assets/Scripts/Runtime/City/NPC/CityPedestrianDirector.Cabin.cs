using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityPedestrianDirector
    {
        // A rider keeps the same dressed body across the doorway. While the bus owns it,
        // it has no street actor, controller, avoidance queries or street update slot.
        internal CityPedestrianPresentation TakeCabinBody(int id, CityPedestrianActor boardingWalker)
        {
            if (!HasResidentLife || !isActiveAndEnabled) return null;
            CityPedestrianPresentation body = null;
            if (boardingWalker != null)
            {
                int slot = actors.IndexOf(boardingWalker);
                if (slot < 0 || actorResidents[slot] != id) return null;
                body = boardingWalker.Presentation;
                ReleaseActor(slot);
            }
            else
            {
                if (journeys.IsStreetPresented(id)) return null;
                foreach (var entry in residentIds)
                    if (entry.Value == id) { body = entry.Key; break; }
            }

            if (body == null) body = createResidentPresentation(id);
            else
            {
                residentIds.Remove(body);
                presentationPool.Remove(body);
            }
            journeys.SetStreetPresented(id, true);
            body.gameObject.SetActive(true);
            return body;
        }

        internal void ReturnCabinBody(int id, CityPedestrianPresentation body)
        {
            journeys?.SetStreetPresented(id, false);
            if (body == null) return;
            body.ClearSeat();
            body.ClearAttention();
            body.gameObject.SetActive(false);
            if (poolRoot != null && presentationPool.Count < profile.PoolSize)
            {
                body.transform.SetParent(poolRoot, false);
                presentationPool.Add(body);
                residentIds.Add(body, id);
            }
            else
            {
                body.Shutdown();
                CityPedestrianResources.DestroyObject(body.gameObject);
            }
        }

        internal CityPedestrianActor TryStartCabinExit(int id, CityPedestrianPresentation body, int pavement,
            Vector3 position, Quaternion rotation)
        {
            int slot = FindAvailableActorIndex();
            if (slot < 0) slot = ReclaimHiddenStreetSlot();
            if (slot < 0 || !MakeRoomInBodyCache()) return null;
            presentationPool.Add(body);
            residentIds.Add(body, id);
            var anchor = new CityPedestrianSpawnAnchor("bus-egress", position, pavement, pavement);
            initialApproachCompleted[slot] = true;
            body.ClearSeat();
            TryBindSpawn(slot, anchor, pavement, (uint)id + 1u, body, false);
            actors[slot].transform.rotation = rotation;
            return actors[slot];
        }
    }
}
