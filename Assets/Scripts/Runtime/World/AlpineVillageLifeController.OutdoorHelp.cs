using UnityEngine;

namespace BarPromenade
{
    public sealed partial class AlpineVillageLifeController
    {
        private Transform stationStrap;
        private bool stationReserved, stationHelperHolding, stationReturning;
        private float stationHelpTime, stationHelpWalkTime;
        public Transform StationLid => lid;
        public Transform StationStrap => stationStrap;
        public Vector3 StationHelpDock => neighbourhood.Ground(Plan.StationCrate +
            Quaternion.LookRotation(Plan.StationForward) * new Vector3(-.66f, 0f, .49f));
        private Vector3 StationHelpFacing => Quaternion.LookRotation(Plan.StationForward) * new Vector3(.66f, 0f, -.175f).normalized;
        public bool StationHelpCompleted { get; private set; }
        public bool StationPartnerReady => stationReserved &&
            Vector3.Distance(StationWorker.transform.position, StationHelpDock) < .04f &&
            Quaternion.Angle(StationWorker.transform.rotation, Quaternion.LookRotation(StationHelpFacing)) < 2f;
        public bool CanReserveStationHelp => !stationReserved && !stationReturning &&
            neighbours[0].IsOutside && neighbours[0].Task == VillageNeighbourTask.Household &&
            Mathf.Repeat(stationTime, StationWorker.ClipLength(VillageResidentAction.StationWork) + 7f) < 7f;

        public bool ReserveStationHelp(bool reserve)
        {
            if (reserve && !CanReserveStationHelp) return false;
            if (!reserve && !stationReserved) return false;
            stationReserved = reserve; stationReturning = !reserve;
            stationHelperHolding = false; stationHelpTime = 0f;
            if (reserve) StationHelpCompleted = false;
            return true;
        }
        public void SetStationHelperHolding(bool value) => stationHelperHolding = value;

        private bool AdvanceStationHelp(float dt)
        {
            if (!stationReserved && !stationReturning) return false;
            Transform actor = StationWorker.transform;
            Vector3 target = stationReserved ? StationHelpDock : Plan.StationWork;
            Vector3 delta = target - actor.position; delta.y = 0f;
            bool moving = delta.magnitude > .025f;
            Vector3 facing = moving ? delta.normalized : stationReserved ?
                StationHelpFacing : -Plan.StationForward;
            facing.y = 0f;
            Quaternion desired = Quaternion.LookRotation(facing);
            actor.rotation = Quaternion.RotateTowards(actor.rotation, desired, 105f * dt);
            float speed = moving && Quaternion.Angle(actor.rotation, desired) < 18f ? .75f : 0f;
            Vector3 proposed = actor.position + delta.normalized * Mathf.Min(delta.magnitude, speed * dt);
            if (speed > 0f && (ResidentBlocks(neighbours[0], proposed) || !walkable.Contains(proposed, .25f))) speed = 0f;
            if (speed > 0f) actor.position = neighbourhood.Ground(proposed);
            stationHelpWalkTime += dt * speed / .75f;
            if (moving || Quaternion.Angle(actor.rotation, desired) > 2f)
            { StationWorker.ApplyLocomotion(stationHelpWalkTime, speed, false); return true; }
            if (stationReturning)
            {
                stationReturning = false; stationTime = 0f;
                lid.localRotation = Quaternion.identity;
                StationWorker.Apply(VillageResidentAction.Idle, 0f); return true;
            }
            if (!stationHelperHolding || StationHelpCompleted)
            { StationWorker.Apply(VillageResidentAction.Idle, 0f); return true; }
            float before = stationHelpTime;
            stationHelpTime += dt;
            StationWorker.Apply(VillageResidentAction.StationStrap, stationHelpTime);
            float contact = stationHelpTime < 1.5f ? SmoothFirewood(stationHelpTime / 1.5f) :
                1f - SmoothFirewood((stationHelpTime - 4.5f) / 1.5f);
            stationStrap.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(stationHelpTime * Mathf.PI) * 3f * contact);
            // Sample the moved, authored fastening and loose end. The same
            // motion and contacts author the six-second source action.
            StationWorker.ApplyHandContacts(stationStrap.Find("ANCHOR_Fastener").position,
                stationStrap.Find("ANCHOR_Grip").position, contact);
            if (before < 4.5f && stationHelpTime >= 4.5f) sound.PlayCloth(stationStrap.position);
            if (stationHelpTime >= 6f)
            {
                StationHelpCompleted = true; stationStrap.localRotation = Quaternion.identity;
                Thank(VillageResidentRole.StationWorker, "village.life.station.thanks", false);
            }
            return true;
        }

        public void PlayHouseholdWood(Vector3 position) => sound.PlayWood(position, .65f);
        public void PlayHouseholdScrape(Vector3 position) => sound.PlayScrape(position);
        public void PlayHouseholdHinge(Vector3 position) => sound.PlayHinge(position);
    }
}
