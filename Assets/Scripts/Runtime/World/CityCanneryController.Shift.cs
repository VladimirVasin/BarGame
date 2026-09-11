using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityCanneryController
    {
        private readonly Vector3[][] factoryEntryRoutes = new Vector3[4][];
        private readonly Vector3[][] factoryExitRoutes = new Vector3[4][];
        private readonly double[] factoryWalkDurations = new double[4];
        private readonly bool[] factoryWaitingOutside = new bool[4];
        private readonly bool[] factoryShiftWalking = new bool[4];

        public Vector3 FactoryWaitingPosition(int role) => factoryEntryRoutes[role][0];
        public double FactoryShiftWalkDuration(int role) => factoryWalkDurations[role];
        public double FactoryShiftEntryTime(int role) =>
            Cycle.StageStart(CityFishSupplyStage.UnloadFish, Snapshot.Batch) + EntryDelay(role);
        public double FactoryShiftExitTime(int role) =>
            Cycle.ProductionStageStart(CityCanneryProductionStage.Pack, Cycle.ProductionLotCount - 1, Snapshot.Batch) +
            Cycle.ProductionStageDuration(CityCanneryProductionStage.Pack) + 6d + ExitDelay(role);

        // The farthest operator enters first and leaves last, so the narrow
        // aisle never asks someone to walk through a colleague at a station.
        private static double EntryDelay(int role) => role <= 1 ? 0d : (role - 1) * 12d;
        private static double ExitDelay(int role) => role == 0 ? 0d : (3 - role) * 12d;

        private void CreateFactoryShift()
        {
            SetRoute(0, new[] {
                Local(1.35f, -2.7f, true), Local(1.35f, -5.5f, true),
                Local(-1.1f, -5.5f),
                Local(-2.55f, -5.5f), Local(-2.55f, -6.55f),
                Local(-3.4f, -6.55f), Anchor("Receiver") });
            SetRoute(1, new[] {
                Local(1.35f, 1.5f, true), Local(1.35f, 8.1f, true),
                Local(-8.55f, 8.1f, true), Local(-8.55f, 2.65f, true),
                Local(-7.18f, 2.65f), Local(-6.9f, 1.25f),
                Local(-6.9f, .15f), Local(-7.18f, -.6f),
                Local(-7.18f, -2.05f), Anchor("PreparationWorker") });
            SetRoute(2, new[] {
                Local(1.35f, .1f, true), Local(1.35f, 8.1f, true),
                Local(-8.55f, 8.1f, true), Local(-8.55f, 2.65f, true),
                Local(-7.18f, 2.65f), Local(-6.9f, 1.25f),
                Local(-6.9f, .15f), Anchor("SeamerWorker") });
            SetRoute(3, new[] {
                Local(1.35f, -1.3f, true), Local(1.35f, 8.1f, true),
                Local(-8.55f, 8.1f, true), Local(-8.55f, 2.65f, true),
                Local(-7.18f, 2.65f), Anchor("RetortOperator") });

            Vector3 Local(float x, float z, bool outside = false) =>
                Plan.World(new Vector3(x, outside ? CityCanneryPlan.YardTop : CityCanneryPlan.FloorTop, z));
            void SetRoute(int role, Vector3[] route)
            {
                factoryEntryRoutes[role] = route;
                var reverse = (Vector3[])route.Clone();
                Array.Reverse(reverse);
                factoryExitRoutes[role] = reverse;
                float length = 0f;
                for (int i = 1; i < route.Length; i++) length += Vector3.Distance(route[i - 1], route[i]);
                // WalkWorker's eased arc peaks at 1.5 times average speed.
                factoryWalkDurations[role] = Math.Max(4d, length * 1.5d / 1.2d);
            }
        }

        private bool ApplyFactoryShift(int role)
        {
            factoryWaitingOutside[role] = false;
            factoryShiftWalking[role] = false;
            double enter = FactoryShiftEntryTime(role), leave = FactoryShiftExitTime(role);
            double duration = factoryWalkDurations[role];
            bool waiting = WorkingSeconds < enter || WorkingSeconds >= leave + duration;
            if (waiting)
            {
                factoryWaitingOutside[role] = true;
                ApplyFactoryOutsideWait(role);
                return true;
            }
            bool entering = WorkingSeconds < enter + duration;
            if (!entering && WorkingSeconds < leave) return false;

            factoryDutyBusy[role] = true;
            factoryShiftWalking[role] = true;
            Vector3 outsideForward = Plan.Right;
            Vector3 workForward = role == 0 ? Plan.Forward : Plan.Right;
            WalkWorker(workers[role], (float)((WorkingSeconds - (entering ? enter : leave)) / duration),
                (float)duration, entering ? outsideForward : workForward,
                entering ? workForward : outsideForward, false,
                entering ? factoryEntryRoutes[role] : factoryExitRoutes[role]);
            // Follow the actual paving, floor and goods-door ramps rather
            // than interpolating a floor height over the whole walking leg.
            Vector3 point = workers[role].transform.position;
            Vector3 local = Plan.Local(point);
            float ground = CityCanneryPlan.YardTop;
            if (local.x >= -8f && local.x <= 0f && Mathf.Abs(local.z) <= 7f)
                ground = CityCanneryPlan.FloorTop;
            else if (local.x >= -8.45f && local.x < -8f && local.z >= 1.95f && local.z <= 3.35f)
                // Lift the leading foot over the ten-centimetre staff-door
                // threshold before the body centre reaches the floor edge.
                ground = Mathf.SmoothStep(CityCanneryPlan.YardTop, CityCanneryPlan.FloorTop,
                    Mathf.InverseLerp(-8.45f, -8.15f, local.x));
            else if (local.x > 0f && local.x < 1.5f &&
                (local.z >= -6.5f && local.z <= -4.5f || local.z >= 3.9f && local.z <= 6.1f))
                ground = Mathf.Lerp(CityCanneryPlan.FloorTop, CityCanneryPlan.YardTop, local.x / 1.5f);
            point.y = Plan.Origin.y + ground;
            workers[role].transform.position = point;
            return true;
        }

        private void ApplyFactoryOutsideWait(int role)
        {
            VillageResidentPresentation actor = workers[role];
            Vector3 forward = Plan.Right;
            Vector3 position = FactoryWaitingPosition(role);
            StandWorker(actor, position, forward, position + forward * 3f + Vector3.up * 1.35f);
            float time = (float)(LifeSeconds % 1000d) + role * 3.41f;
            workerSpines[role].rotation = Quaternion.AngleAxis(.65f * Mathf.Sin(time * (1.05f + role * .08f)),
                actor.transform.right) * workerSpines[role].rotation;
            workerSpines[role].rotation = Quaternion.AngleAxis(.65f * Mathf.Sin(time * .37f),
                actor.transform.forward) * workerSpines[role].rotation;
        }

        private double FactoryShiftBoundaryDistance(int role, bool futureOnly)
        {
            double leave = FactoryShiftExitTime(role);
            double arrived = FactoryShiftEntryTime(role) + factoryWalkDurations[role];
            if (WorkingSeconds < arrived || WorkingSeconds > leave) return double.PositiveInfinity;
            return futureOnly ? leave - WorkingSeconds : Math.Min(WorkingSeconds - arrived, leave - WorkingSeconds);
        }
    }
}
