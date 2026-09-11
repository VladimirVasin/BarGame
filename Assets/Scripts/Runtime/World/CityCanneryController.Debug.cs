using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityCanneryController
    {
        private static readonly float[] DebugApproachDistances = { 20f, 16f, 24f, 12f, 28f, 8f, 32f, 4f, 36f };

        /// <summary>Stage the existing loaded truck on its real final approach,
        /// 15–25 horizontal metres from the factory. This works in a paused F9
        /// menu, changes only the shared fish-delivery clock, and never creates
        /// another truck, driver or cargo unit. A repeated request starts a new
        /// batch so its future physical sound events keep their normal order.</summary>
        public bool TryDebugSpawnLoadedTruckNearFactory()
        {
            if (!IsInitialized || !isActiveAndEnabled || Truck == null || Route == null || Cycle == null ||
                port == null || SceneTransitionService.IsTransitioning || AreaTravelService.IsComposing)
                return false;
            Physics.SyncTransforms();
            if (!TryFindDebugLoadedTruckApproach(out float progress)) return false;

            long batch = 0;
            if (CityFishSupplySession.HasStarted)
            {
                if (CityFishSupplySession.WorkingSeconds / Cycle.RepeatingDuration >= long.MaxValue) return false;
                long current = Math.Max(Snapshot.Batch, Cycle.Sample(CityFishSupplySession.WorkingSeconds).Batch);
                if (current >= long.MaxValue - 1) return false;
                batch = current + 1;
            }
            double seconds = Cycle.StageStart(CityFishSupplyStage.PortToFactory, batch) +
                Cycle.StageDuration(CityFishSupplyStage.PortToFactory, batch) * progress;
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds / Cycle.RepeatingDuration >= long.MaxValue)
                return false;
            CityFishSupplySnapshot loaded = Cycle.Sample(seconds);
            if (loaded.Stage != CityFishSupplyStage.PortToFactory || loaded.TruckFish != CityFishSupplyCycle.HandlingUnits)
                return false;
            if (!CityFishSupplySession.TrySetDebugWorkingSeconds(seconds)) return false;

            // The old manoeuvre and any line attached to the relocated driver
            // end here. Ordinary presentation reconstructs every door, grip,
            // trolley, load and machine from the one sampled source of truth.
            Traffic?.Release();
            FactoryConversation?.Suspend();
            if (portConversation != null)
            {
                portConversation.SetDriverState(false, false, false, false, false, false);
                portConversation.Bubbles?.Dismiss(workers[4]);
            }
            IsBlocked = false;
            LastObstacleName = null;
            movementRate = 0f;
            AutoAdvance = true;
            ApplyAt(seconds);
            Physics.SyncTransforms();
            return true;
        }

        private bool TryFindDebugLoadedTruckApproach(out float progress)
        {
            progress = 0f;
            float length = Route.Length(CityCanneryTruckLeg.PortToFactory);
            if (length <= 1f || float.IsNaN(length) || float.IsInfinity(length)) return false;
            foreach (float remaining in DebugApproachDistances)
            {
                if (remaining >= length) continue;
                float candidate = 1f - remaining / length;
                CityPortTruckPose pose = Route.Sample(CityCanneryTruckLeg.PortToFactory, candidate);
                Vector3 offset = pose.RearAxle - Plan.Origin;
                float distanceSquared = offset.x * offset.x + offset.z * offset.z;
                if (distanceSquared < 15f * 15f || distanceSquared > 25f * 25f || !DebugTruckBodyIsClear(pose)) continue;
                progress = candidate;
                return true;
            }
            return false;
        }

        private bool DebugTruckBodyIsClear(CityPortTruckPose pose)
        {
            Vector3 center = pose.RearAxle + pose.Rotation * CityCanneryTruckDimensions.BodyCenter;
            int count = Physics.OverlapBoxNonAlloc(center, CityCanneryTruckDimensions.BodyHalfExtents +
                new Vector3(.08f, 0f, .08f), obstacles, pose.Rotation, ~0, QueryTriggerInteraction.Ignore);
            // A full buffer cannot establish that a hidden extra body is clear.
            if (count == obstacles.Length) return false;
            for (int i = 0; i < count; i++)
            {
                Collider hit = obstacles[i];
                if (hit == null || hit.transform.IsChildOf(transform) || hit.bounds.max.y < pose.RearAxle.y + .45f) continue;
                return false;
            }
            return true;
        }
    }
}
