using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed class VillageSnowPatch
    {
        public readonly string StableId;
        public readonly Vector3 Dock, Facing, Center;
        public readonly Vector2 HalfSize;
        public VillageSnowPatch(string id, Vector3 dock, Vector3 facing)
        {
            StableId = id; Dock = dock; Facing = facing.normalized;
            Center = dock + Facing * .64f;
            HalfSize = new Vector2(.54f, .48f);
        }
    }

    /// <summary>Three small jobs, each with three persistent cuts into the existing snow field.</summary>
    public sealed class VillageSnowClearing
    {
        private readonly AlpineVillageSnowTreading snow;
        private readonly IReadOnlyList<VillageSnowPatch> patches;
        public IReadOnlyList<VillageSnowPatch> Patches => patches;
        public int ChangedVertices { get; private set; }
        public VillageSnowClearing(AlpineVillageSnowTreading field, IReadOnlyList<VillageSnowPatch> workPatches)
        {
            snow = field ?? throw new ArgumentNullException(nameof(field));
            patches = workPatches ?? throw new ArgumentNullException(nameof(workPatches));
            foreach (VillageSnowPatch patch in patches) Apply(patch);
        }
        public int Stage(string stableId) => GameSessionState.VillageHousehold.GetClearingStage(stableId);
        public bool IsComplete(string stableId) => Stage(stableId) == VillageHouseholdProgress.ClearingStageCount;
        public bool Advance(string stableId, int expectedStage)
        {
            VillageSnowPatch patch = Find(stableId);
            if (!GameSessionState.VillageHousehold.TryAdvanceClearing(stableId, expectedStage)) return false;
            Apply(patch);
            return true;
        }
        public VillageSnowPatch Find(string stableId)
        {
            foreach (VillageSnowPatch patch in patches) if (patch.StableId == stableId) return patch;
            throw new ArgumentException("Unknown village clearing " + stableId, nameof(stableId));
        }
        private void Apply(VillageSnowPatch patch)
        {
            float strength = Stage(patch.StableId) / (float)VillageHouseholdProgress.ClearingStageCount;
            ChangedVertices += snow.ClearPatch(patch.Center, Vector3.Cross(Vector3.up, patch.Facing), patch.HalfSize, strength);
        }
    }
}
