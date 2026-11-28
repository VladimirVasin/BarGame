using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One resident walks from the recessed doorway to the sorting table and back each day.</summary>
    [DefaultExecutionOrder(215), DisallowMultipleComponent]
    internal sealed class RidgeCellarKeeper : MonoBehaviour
    {
        private const int StartMinute = 9 * 60, EndMinute = 18 * 60;
        private const float WalkingSpeed = 1.16f, SortLegSeconds = 10.5f;
        private CityWalkingPath commute;
        private Vector3 source, destination;
        private Quaternion restRotation;
        private Transform spine;
        private NpcHandPose hands;
        private Camera viewer;
        private double nextCheck;
        private bool initialized, visible;

        internal VillageResidentPresentation Worker { get; private set; }
        internal Transform Produce { get; private set; }
        internal bool IsWorking { get; private set; }
        internal bool HandsOnProduce { get; private set; }
        internal float HandContactError { get; private set; }
        internal Vector3 HomeDoor { get; private set; }
        internal Vector3 WorkFeet { get; private set; }
        internal IReadOnlyList<Vector3> Route => commute.Points;
        internal float TravelSeconds => commute.Length / WalkingSpeed;
        internal bool AutoAdvance { get; set; } = true;

        internal static RidgeCellarKeeper Build(Transform court, CityLayout layout, RidgeCellarPlan site)
        {
            var keeper = court.gameObject.AddComponent<RidgeCellarKeeper>();
            keeper.Produce = CityPedestrianHandProps.FindSocket(court, "SortingPotato") ??
                throw new InvalidOperationException("The sorting table needs the loose potato.");
            keeper.source = keeper.Produce.position;
            keeper.restRotation = keeper.Produce.rotation;
            keeper.destination = site.Origin + new Vector3(8.40f, 1.00f, 5.12f);
            keeper.WorkFeet = site.Origin + new Vector3(8.12f, 0f, 5.70f);
            keeper.commute = keeper.MakeCommute(layout, site);
            keeper.initialized = true;
            keeper.SampleAt(GameSessionState.GameTimeOfDayMinutes, false);
            return keeper;
        }

        private CityWalkingPath MakeCommute(CityLayout layout, RidgeCellarPlan site)
        {
            // This is the occupied house on the same side of the road as the short stair.
            CityHillsideBuilding house = null;
            foreach (var candidate in CityHillsideNeighborhoodPlan.ForLayout(layout).Buildings)
                if (candidate.Bank == CityTerraceTransportBank.East && candidate.Lot.Cell == new Vector2Int(23, -8))
                { house = candidate; break; }
            if (house == null) throw new InvalidOperationException("Missing cellar keeper's east Ridge residence.");
            HomeDoor = house.ThresholdWorld;
            Vector3 forward = house.Lot.FacadeForward;
            Vector3 vestibule = HomeDoor - forward * (CityBuildingAssetProvider.EntranceDepth - .34f);
            // The resident rounds the blind jamb before the outside body retires.
            var points = new List<Vector3>(16) {
                vestibule + Vector3.Cross(Vector3.up, forward) * .92f, vestibule, HomeDoor,
                house.DoorWorld, house.StreetContactWorld
            };
            var ground = CityHillsideGroundPlan.ForLayout(layout);
            var route = site.WalkRoute;
            for (int i = route.Count - 1; i >= 0; i--)
            {
                points.Add(route[i]);
                if ((new Vector2(route[i].x, route[i].z) - site.StairFoot).sqrMagnitude < .001f) break;
            }
            // Round the west end of the table. The worker stands between its north
            // apron and the handcart; the through route and the bench dock stay open.
            Vector3 corner = site.Origin + new Vector3(6.63f, 0f, 5.74f);
            if (ground.TrySampleTop(new Vector2(corner.x, corner.z), out float top)) corner.y = top;
            points.Add(corner);
            points.Add(WorkFeet);
            return new CityWalkingPath(points);
        }

        private void LateUpdate()
        {
            if (!initialized || !AutoAdvance || GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused) return;
            if (!visible && Time.unscaledTimeAsDouble < nextCheck) return;
            SampleAt(GameSessionState.GameTimeOfDayMinutes, false);
        }

        internal void SampleAt(double minute, bool forceNear = true)
        {
            if (!initialized) return;
            nextCheck = Time.unscaledTimeAsDouble + .24d;
            if (minute < StartMinute || minute >= EndMinute)
            {
                IsWorking = false;
                HandsOnProduce = false;
                HandContactError = 0f;
                if (Worker && Worker.gameObject.activeSelf) Worker.gameObject.SetActive(false);
                if (Produce.position != source || Produce.rotation != restRotation)
                    Produce.SetPositionAndRotation(source, restRotation);
                visible = false;
                return;
            }

            float elapsed = (float)((minute - StartMinute) / GameTimeState.GameMinutesPerRealSecond);
            float remaining = (float)((EndMinute - minute) / GameTimeState.GameMinutesPerRealSecond);
            float travelled = Mathf.Min(elapsed, remaining) * WalkingSpeed;
            IsWorking = travelled >= commute.Length;
            Vector3 feet = commute.Sample(travelled, out Vector3 forward);
            if (remaining < elapsed) forward = -forward;
            Quaternion rotation = Quaternion.Slerp(Quaternion.LookRotation(forward),
                Quaternion.LookRotation(Vector3.back), Ease(travelled, commute.Length - .6f, commute.Length));
            if (!viewer) viewer = Camera.main;
            float distance = viewer ? (viewer.transform.position - feet).sqrMagnitude : 0f;
            bool near = forceNear || distance <= (visible ? 76f * 76f : 68f * 68f);

            // Spawn once from the permanent global assignment. The capsule retains
            // the real calendar position, including on a distant or late district load.
            if (!Worker)
            {
                Worker = DefaultNpcFactory.CreateForCharacter(transform, DefaultNpcPopulation.RidgeCellarKeeper);
                Worker.name = "Cellar keeper";
                CityPortCrew.AlignWorkerModelWithPlacement(Worker);
                var body = Worker.gameObject.AddComponent<CapsuleCollider>();
                body.height = 1.70f; body.radius = .20f; body.center = Vector3.up * .85f;
                hands = Worker.GetComponent<NpcHandPose>();
                spine = CityPedestrianHandProps.FindSocket(Worker.ModelRoot, "spine");
            }
            if (!Worker.gameObject.activeSelf) Worker.gameObject.SetActive(true);
            Worker.transform.SetPositionAndRotation(feet, rotation);
            if (Worker.ModelRoot.gameObject.activeSelf != near)
            {
                if (!near) Worker.ReleaseAnimation();
                Worker.ModelRoot.gameObject.SetActive(near);
            }
            visible = near;
            HandsOnProduce = false;
            HandContactError = 0f;
            if (IsWorking)
            {
                // Finish the last full there-and-back motion before the walk home:
                // the potato is resting in its box when the hands release it.
                float shift = (float)((EndMinute - StartMinute) / GameTimeState.GameMinutesPerRealSecond) - 2f * TravelSeconds;
                float lastCycle = Mathf.Floor(shift / (2f * SortLegSeconds)) * (2f * SortLegSeconds);
                Sort(Mathf.Min(lastCycle, elapsed - TravelSeconds), near);
            }
            else
            {
                if (Produce.position != source) Produce.SetPositionAndRotation(source, restRotation);
                if (near) Worker.ApplyLocomotion(WalkingSpeed, false, elapsed);
            }
        }

        private void Sort(float seconds, bool pose)
        {
            float phase = Mathf.Repeat(seconds, 2f * SortLegSeconds);
            bool back = phase >= SortLegSeconds;
            float t = back ? phase - SortLegSeconds : phase;
            Vector3 start = back ? destination : source, end = back ? source : destination;
            Vector3 inspection = WorkFeet + new Vector3(0f, 1.22f, -.28f);
            float lift = Ease(t, .9f, 2.23f), place = Ease(t, 3.85f, 5.28f);
            Vector3 item = Vector3.Lerp(Vector3.Lerp(start, inspection, lift), end, place);
            float turn = Mathf.Sin(Mathf.PI * Ease(t, 2.12f, 3.80f)) * (back ? -42f : 42f);
            Produce.SetPositionAndRotation(item, Quaternion.AngleAxis(turn, Vector3.forward) * restRotation);
            if (!pose) return;

            Worker.Apply(VillageResidentAction.Idle, seconds, item);
            float pick = Ease(t, 0f, .9f) * (1f - Ease(t, 3.32f, 3.76f));
            float set = Ease(t, 2.92f, 3.32f) * (1f - Ease(t, 5.50f, 6.28f));
            float lean = Mathf.Max(pick, set);
            // A second, shallow sweep straightens the rim once the potato is down.
            float sweep = Ease(t, 6.5f, 7.4f) * (1f - Ease(t, 8.25f, 9.05f));
            spine.rotation = Quaternion.AngleAxis(27f * lean + 18f * sweep, Worker.transform.right) * spine.rotation;
            float right = back ? set : pick, left = back ? pick : set;
            Reach(false, right, item + Vector3.left * .019f);
            Reach(true, left, item + Vector3.right * .019f);
            if (sweep > 0f)
            {
                Vector3 rim = end + Vector3.up * .049f + Vector3.forward * .048f;
                rim.x += .105f * Ease(t, 7.35f, 8.2f);
                Worker.ApplyHandContacts(back ? (Vector3?)rim : null, back ? null : (Vector3?)rim, sweep);
            }
        }

        private void Reach(bool left, float weight, Vector3 target)
        {
            hands.SetGrip(left, .76f * weight);
            if (weight <= .001f) return;
            Worker.ApplyHandContacts(left ? null : (Vector3?)target, left ? (Vector3?)target : null, weight);
            if (weight < .99f) return;
            HandsOnProduce = true;
            var grip = left ? Worker.LeftGrip : Worker.RightGrip;
            HandContactError = Mathf.Max(HandContactError, Vector3.Distance(grip.position, target));
        }

        private static float Ease(float t, float start, float end) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, t));

        private void OnDisable()
        {
            if (Worker) Worker.gameObject.SetActive(false);
            if (Produce) Produce.SetPositionAndRotation(source, restRotation);
            IsWorking = false;
            HandsOnProduce = false;
            HandContactError = 0f;
            visible = false;
            nextCheck = 0d;
        }

        private void OnDestroy()
        {
            if (Worker) NpcFootstepSources.Unregister(Worker.transform);
        }
    }
}
