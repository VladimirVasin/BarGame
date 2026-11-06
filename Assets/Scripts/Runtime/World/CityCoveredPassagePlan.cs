using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct CityCoveredPassageLamp
    {
        public int Index { get; }
        public Vector3 LocalPosition { get; }
        public bool IsFaulty { get; }
        public int Seed { get; }
        internal CityCoveredPassageLamp(int index, Vector3 position, bool faulty, int seed)
        { Index = index; LocalPosition = position; IsFaulty = faulty; Seed = seed; }
    }

    /// <summary>Metre-space dressing and fixed fixture identity for one actual building opening.</summary>
    public sealed class CityCoveredPassagePlan
    {
        public string Id { get; }
        public BuildingLot Lot { get; }
        public Bounds LocalBounds { get; }
        public int Theme { get; }
        public int GraffitiIndex { get; }
        public Color AccentColor { get; }
        public uint ArrangementSeed { get; }
        public string Signature { get; }
        public IReadOnlyList<CityCoveredPassageLamp> Lamps { get; }
        public IReadOnlyList<CityCoveredPassageWallSlot> WallSlots { get; }
        internal CityBuildingPrototypePose Pose { get; }

        public static bool TryCreate(BuildingLot lot, int seed, out CityCoveredPassagePlan plan)
        {
            plan = null;
            if (lot == null || !lot.IsOrdinaryBuilding || lot.BuildingVariant != 3) return false;
            plan = new CityCoveredPassagePlan(lot, seed);
            return true;
        }

        private CityCoveredPassagePlan(BuildingLot lot, int seed)
        {
            Lot = lot;
            LocalBounds = CityBuildingAssetProvider.GetExpectedPassageBounds(lot.District, lot.BuildingVariant);
            Pose = CityBuildingPrototypePlacement.ResolveExpectedCityPose(lot);
            Id = $"covered-passage-{seed}-{(int)lot.District}-{lot.Cell.x}-{lot.Cell.y}";
            uint hash = Mix(unchecked((uint)seed) ^ Mix(unchecked((uint)lot.Cell.x)) ^
                Mix(unchecked((uint)lot.Cell.y) + 0x9235a84bu) ^ ((uint)lot.District + 1u) * 0x9e3779b9u);
            ArrangementSeed = hash;
            Theme = (int)(hash & 3u);
            GraffitiIndex = (int)((hash >> 4) & 7u);
            AccentColor = Theme switch {
                0 => new Color(.235f, .31f, .29f),
                1 => new Color(.30f, .285f, .245f),
                2 => new Color(.27f, .30f, .34f),
                _ => new Color(.34f, .285f, .27f)
            };
            WallSlots = CityCoveredPassageDressingPlan.Create(LocalBounds, hash, GraffitiIndex);
            int count = Mathf.Clamp(Mathf.CeilToInt(LocalBounds.size.z / 4.25f), 2, 4);
            int faulty = (int)((hash >> 9) % (uint)count);
            var lamps = new CityCoveredPassageLamp[count];
            for (int index = 0; index < count; index++)
            {
                float z = Mathf.Lerp(LocalBounds.min.z + .9f, LocalBounds.max.z - .9f,
                    (index + .5f) / count);
                float x = ((hash >> (index + 14)) & 1u) == 0u ? -.16f : .16f;
                lamps[index] = new CityCoveredPassageLamp(index,
                    new Vector3(x, LocalBounds.max.y - .145f, z), index == faulty,
                    unchecked((int)Mix(hash + (uint)index * 0x85ebca6bu)));
            }
            Lamps = Array.AsReadOnly(lamps);
            Signature = $"{Id}:{Theme}:{GraffitiIndex}:{hash:x8}:{count}:{faulty}";
        }

        public Vector3 ToWorldPoint(Vector3 localPosition) => Pose.TransformPoint(localPosition);
        public float EvaluateFactor(int fixtureIndex, double absoluteSeconds)
        {
            if (fixtureIndex < 0 || fixtureIndex >= Lamps.Count) throw new ArgumentOutOfRangeException(nameof(fixtureIndex));
            var lamp = Lamps[fixtureIndex];
            return lamp.IsFaulty ? CityCoveredPassageFlickerEvaluator.Evaluate(absoluteSeconds, lamp.Seed) : 1f;
        }

        internal static uint Mix(uint value)
        {
            unchecked
            {
                value ^= value >> 16; value *= 0x7feb352du;
                value ^= value >> 15; value *= 0x846ca68bu;
                return value ^ (value >> 16);
            }
        }
    }
}
