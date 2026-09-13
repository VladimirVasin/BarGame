using UnityEngine;

namespace BarPromenade
{
    public enum CityCanneryDriverRestPhase
    {
        None, WalkingToBench, Sitting, GettingLunch, Eating, StowingLunch, Standing, WalkingToTrolley
    }

    public sealed partial class CityCanneryController
    {
        private const float DriverSitSeconds = 3f;
        private const float DriverStowSeconds = 2.4f;
        private readonly Vector3[] driverBenchRoute = new Vector3[4];
        private Vector3 driverSeatedPelvisLocal;
        public CityCanneryDriverRestPhase DriverRestPhase { get; private set; }
        public Vector3 DriverBenchSeatContact { get; private set; }
        public Vector3 DriverBenchStandingPosition { get; private set; }
        public float DriverBenchSeatWeight { get; private set; }

        private void CreateDriverRest()
        {
            // YardBench is authored directly in metres. Its timber's highest
            // surface is the seat; the other timber part is the shorter leg.
            CityMiscAssetProvider provider = CityMiscAssetProvider.LoadOrThrow();
            float seatHeight = 0f;
            for (int i = 0; i < CityMiscAssetProvider.GetPartCount(CityMiscKind.YardBench); i++)
            {
                CityMiscMeshPart part = provider.GetPartOrThrow(CityMiscKind.YardBench, 0, i);
                if (part.Role == CityMiscMeshRole.Timber) seatHeight = Mathf.Max(seatHeight, part.Mesh.bounds.max.y);
            }
            Vector3 bench = CityCanneryPlan.WaitingBenchLocalPosition;
            DriverBenchSeatContact = Plan.World(bench + Vector3.right * .05f + Vector3.up * seatHeight);
            DriverBenchStandingPosition = Plan.World(bench + Vector3.right * .55f);
            VillageResidentPresentation actor = workers[4];
            actor.Apply(VillageResidentAction.SewingEnter, actor.ClipLength(VillageResidentAction.SewingEnter));
            driverSeatedPelvisLocal = Quaternion.Inverse(actor.transform.rotation) *
                (driverPelvis.position - actor.transform.position);
            CreateDriverLunch();
        }

        private bool ApplyDriverRest()
        {
            float seconds = (float)Snapshot.Seconds;
            if (Snapshot.Stage == CityFishSupplyStage.UnloadFish)
            {
                float remaining = (float)(Snapshot.Duration - Snapshot.Seconds);
                if (remaining > TrolleyHandleArrival) return false;
                float arriving = TrolleyHandleArrival - remaining;
                if (remaining > DriverSitSeconds)
                {
                    DriverRestPhase = CityCanneryDriverRestPhase.WalkingToBench;
                    WalkDriverBenchRoute(arriving / (TrolleyHandleArrival - DriverSitSeconds),
                        TrolleyHandleArrival - DriverSitSeconds, false);
                }
                else
                {
                    DriverRestPhase = CityCanneryDriverRestPhase.Sitting;
                    ApplyDriverBenchPose(DriverSitSeconds - remaining, false);
                }
                return true;
            }
            if (Snapshot.Stage == CityFishSupplyStage.WaitForProduction)
            {
                DriverRestPhase = seconds < DriverStowSeconds
                    ? CityCanneryDriverRestPhase.GettingLunch : CityCanneryDriverRestPhase.Eating;
                ApplyDriverBenchPose(DriverSitSeconds, false);
                ApplyDriverLunch(seconds);
                return true;
            }
            if (Snapshot.Stage != CityFishSupplyStage.LoadFinished || seconds > TrolleyHandleArrival) return false;
            // Inspection has released all three boxes. The ordinary trolley
            // approach interval also fits putting lunch away and standing up.
            if (seconds < DriverStowSeconds)
            {
                DriverRestPhase = CityCanneryDriverRestPhase.StowingLunch;
                ApplyDriverBenchPose(DriverSitSeconds, false);
                float lunchAge = (float)(Cycle.StageStart(CityFishSupplyStage.LoadFinished, Snapshot.Batch) -
                    Cycle.StageStart(CityFishSupplyStage.WaitForProduction, Snapshot.Batch));
                ApplyDriverLunch(lunchAge, seconds / DriverStowSeconds);
            }
            else if (seconds < DriverStowSeconds + DriverSitSeconds)
            {
                DriverRestPhase = CityCanneryDriverRestPhase.Standing;
                ApplyDriverBenchPose(seconds - DriverStowSeconds, true);
            }
            else
            {
                DriverRestPhase = CityCanneryDriverRestPhase.WalkingToTrolley;
                float duration = TrolleyHandleArrival - DriverStowSeconds - DriverSitSeconds;
                WalkDriverBenchRoute((seconds - DriverStowSeconds - DriverSitSeconds) / duration, duration, true);
            }
            return true;
        }

        private void WalkDriverBenchRoute(float progress, float duration, bool returning)
        {
            driverBenchRoute[0] = trolleyOperatorPosition;
            driverBenchRoute[1] = trolleyOperatorPosition + Plan.Forward * .75f;
            driverBenchRoute[2] = DriverBenchStandingPosition - Plan.Forward * 1.45f;
            driverBenchRoute[3] = DriverBenchStandingPosition;
            if (returning) System.Array.Reverse(driverBenchRoute);
            Vector3 trolleyFacing = trolleyOperatorRotation * Vector3.forward;
            WalkWorker(workers[4], progress, duration, returning ? Plan.Right : trolleyFacing,
                returning ? trolleyFacing : Plan.Right, false, driverBenchRoute);
        }

        private void ApplyDriverBenchPose(float seconds, bool standing)
        {
            VillageResidentPresentation actor = workers[4];
            actor.transform.SetPositionAndRotation(DriverBenchStandingPosition, Quaternion.LookRotation(Plan.Right));
            actor.Apply(VillageResidentAction.Idle, 0f);
            for (int i = 0; i < 2; i++)
            {
                driverPlantedFeet[i] = driverFeet[i].position;
                driverPlantedFootRotations[i] = driverFeet[i].rotation;
            }
            actor.Apply(standing ? VillageResidentAction.SewingExit : VillageResidentAction.SewingEnter, seconds);
            // The shared clip reaches its chair at 2.3 seconds. Adapt only
            // that seat displacement to this bench, keeping both soles planted.
            DriverBenchSeatWeight = Ease((standing ? DriverSitSeconds - seconds : seconds) / 2.3f);
            Vector3 authoredSeat = DriverBenchStandingPosition + actor.transform.rotation * driverSeatedPelvisLocal;
            driverPelvis.position += (DriverBenchSeatContact + Vector3.up * .08f - authoredSeat) * DriverBenchSeatWeight;
            for (int i = 0; i < 2; i++)
                LimbTwoBoneIk.Solve(driverThighs[i], driverShins[i], driverFeet[i], driverPlantedFeet[i],
                    driverPlantedFootRotations[i], driverThighs[i].position + Plan.Right * .65f, 1f, 1f, true);
            float life = (float)(LifeSeconds % 120d);
            workerSpines[4].rotation = Quaternion.AngleAxis(Mathf.Sin(life * 1.4f) * .65f * DriverBenchSeatWeight,
                actor.transform.right) * workerSpines[4].rotation;
            ApplyCrewLook(actor, Plan.World(new Vector3(2.75f, 1.5f, -1.2f)), .35f * DriverBenchSeatWeight);
        }
    }
}
