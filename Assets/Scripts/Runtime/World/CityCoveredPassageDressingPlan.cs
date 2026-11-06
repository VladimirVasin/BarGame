using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum CityCoveredPassageWallSlotKind
    {
        Empty, Graffiti, Repair, JunctionBox, ConduitRun, CableTray, VentGrille, CornerGuard
    }

    public readonly struct CityCoveredPassageWallSlot
    {
        public int Side { get; }
        public int Index { get; }
        public float AlongZ { get; }
        public float Height { get; }
        public Vector2 Size { get; }
        public float Width => Size.x;
        public Vector3 ModelScale { get; }
        public CityCoveredPassageWallSlotKind Kind { get; }
        public int Variant { get; }
        public bool PaintOverRepair => false;
        internal CityCoveredPassageWallSlot(int side, int index, float along, float height,
            Vector2 size, Vector3 scale, CityCoveredPassageWallSlotKind kind, int variant)
        { Side = side; Index = index; AlongZ = along; Height = height;
            Size = size; ModelScale = scale; Kind = kind; Variant = variant; }
    }

    /// <summary>Independent near, middle and far wall occupations. Paint and repairs have separate slots.</summary>
    internal static class CityCoveredPassageDressingPlan
    {
        private sealed class Draft
        {
            internal int Side, Index, Count, GraffitiVariant;
            internal uint Seed;
            internal CityCoveredPassageWallSlotKind Kind;
        }

        internal static IReadOnlyList<CityCoveredPassageWallSlot> Create(Bounds bounds, uint seed, int graffitiIndex)
        {
            float usable = Mathf.Max(.9f, bounds.size.z - 1.10f);
            int maximum = Mathf.Clamp(Mathf.FloorToInt(usable / 1.65f), 3, 5);
            var drafts = new List<Draft>(10);
            Wall(-1, CityCoveredPassagePlan.Mix(seed ^ 0x68bc21ebu));
            Wall(1, CityCoveredPassagePlan.Mix(seed ^ 0x02e5be93u));

            // Repair is a separate occupation selected after both art layouts.
            // Some passages have none; no mural automatically gets a plaster bed.
            var repairCandidates = new List<int>(drafts.Count);
            for (int index = 0; index < drafts.Count; index++)
                if (drafts[index].Kind != CityCoveredPassageWallSlotKind.Graffiti) repairCandidates.Add(index);
            uint repairSeed = CityCoveredPassagePlan.Mix(seed ^ 0xa529314du);
            Shuffle(repairCandidates, ref repairSeed);
            int repairs = (int)(CityCoveredPassagePlan.Mix(seed ^ 0x49e53a27u) % 3u);
            for (int index = 0; index < repairs && index < repairCandidates.Count; index++)
                drafts[repairCandidates[index]].Kind = CityCoveredPassageWallSlotKind.Repair;
            var tiles = new List<int>(8);
            for (int index = 0; index < 8; index++) tiles.Add((graffitiIndex + index) % 8);
            uint tileSeed = CityCoveredPassagePlan.Mix(seed ^ 0x09e73861u);
            Shuffle(tiles, ref tileSeed);
            int nextTile = 0;
            foreach (var draft in drafts)
                if (draft.Kind == CityCoveredPassageWallSlotKind.Graffiti) draft.GraffitiVariant = tiles[nextTile++];

            var slots = new CityCoveredPassageWallSlot[drafts.Count];
            for (int index = 0; index < drafts.Count; index++) slots[index] = Resolve(drafts[index], bounds, usable, graffitiIndex);
            return Array.AsReadOnly(slots);

            void Wall(int side, uint wallSeed)
            {
                int count = 3 + (int)(wallSeed % (uint)(maximum - 2));
                var kinds = new CityCoveredPassageWallSlotKind[count];
                var occupied = new List<int>(3);
                // Each wall has a real occupation in each longitudinal band,
                // with independently selected cells where a band has two slots.
                for (int band = 0; band < 3; band++)
                {
                    var candidates = new List<int>(2);
                    for (int slot = 0; slot < count; slot++)
                        if (Mathf.Min(2, Mathf.FloorToInt((slot + .5f) * 3f / count)) == band) candidates.Add(slot);
                    uint choice = CityCoveredPassagePlan.Mix(wallSeed + (uint)band * 0x85ebca6bu);
                    occupied.Add(candidates[(int)(choice % (uint)candidates.Count)]);
                }
                Shuffle(occupied, ref wallSeed);
                kinds[occupied[0]] = CityCoveredPassageWallSlotKind.Graffiti;
                kinds[occupied[1]] = Utility(wallSeed, occupied[1], count);
                kinds[occupied[2]] = Utility(CityCoveredPassagePlan.Mix(wallSeed ^ 0xc2b2ae35u), occupied[2], count);

                var extra = new List<int>(count);
                for (int slot = 0; slot < count; slot++) if (kinds[slot] == CityCoveredPassageWallSlotKind.Empty) extra.Add(slot);
                Shuffle(extra, ref wallSeed);
                if (extra.Count > 0 && (wallSeed & 3u) == 0u)
                    kinds[extra[0]] = CityCoveredPassageWallSlotKind.Graffiti;
                for (int slot = 0; slot < count; slot++)
                {
                    uint slotSeed = CityCoveredPassagePlan.Mix(wallSeed + (uint)(slot + 1) * 0x9e3779b9u);
                    if (kinds[slot] == CityCoveredPassageWallSlotKind.Empty && (slotSeed & 7u) == 0u)
                        kinds[slot] = Utility(slotSeed, slot, count);
                    drafts.Add(new Draft { Side = side, Index = slot, Count = count, Seed = slotSeed, Kind = kinds[slot] });
                }
            }
        }

        private static CityCoveredPassageWallSlot Resolve(Draft draft, Bounds bounds, float usable, int graffitiIndex)
        {
            float cell = usable / draft.Count;
            float jitter = (Unit(draft.Seed, 0x192b1fadu) - .5f) * .24f;
            float along = bounds.min.z + .55f + (draft.Index + .5f + jitter) * cell;
            float maximumWidth = cell * .64f;
            float variation = Unit(draft.Seed, 0x91e10da5u);
            Vector2 size;
            float height;
            Vector2 authored;
            switch (draft.Kind)
            {
                case CityCoveredPassageWallSlotKind.Graffiti:
                    size = new Vector2(maximumWidth * Mathf.Lerp(.72f, 1f, variation), Mathf.Lerp(.67f, 1.32f, Unit(draft.Seed, 0x7f4a7c15u)));
                    height = Mathf.Lerp(.88f, 1.78f, Unit(draft.Seed, 0x37d84a39u)); authored = Vector2.one; break;
                case CityCoveredPassageWallSlotKind.Repair:
                    size = new Vector2(Mathf.Min(maximumWidth, Mathf.Lerp(1.12f, 1.84f, variation)),
                        Mathf.Lerp(.63f, 1.28f, Unit(draft.Seed, 0x7021e853u)));
                    height = Mathf.Lerp(.66f, 1.88f, Unit(draft.Seed, 0x29481e35u)); authored = new Vector2(1.70f, 1.15f); break;
                case CityCoveredPassageWallSlotKind.JunctionBox:
                    size = new Vector2(.5f, .62f) * Mathf.Lerp(.83f, 1.12f, variation);
                    height = Mathf.Lerp(1.45f, 1.95f, Unit(draft.Seed, 0x97a3d51bu)); authored = new Vector2(.5f, .62f); break;
                case CityCoveredPassageWallSlotKind.ConduitRun:
                    size = new Vector2(.09f, 1.8f) * Mathf.Lerp(.80f, 1.08f, variation);
                    height = Mathf.Lerp(1.23f, 2.05f, Unit(draft.Seed, 0x61cb53afu)); authored = new Vector2(.09f, 1.8f); break;
                case CityCoveredPassageWallSlotKind.CableTray:
                    size = new Vector2(Mathf.Min(maximumWidth, Mathf.Lerp(1.4f, 2.25f, variation)), .145f);
                    height = bounds.size.y - Mathf.Lerp(.27f, .48f, Unit(draft.Seed, 0x19e312f1u)); authored = new Vector2(2f, .145f); break;
                case CityCoveredPassageWallSlotKind.VentGrille:
                    size = new Vector2(.62f, .46f) * Mathf.Lerp(.82f, 1.20f, variation);
                    height = Mathf.Lerp(1.90f, 2.43f, Unit(draft.Seed, 0x3a932c47u)); authored = new Vector2(.62f, .46f); break;
                case CityCoveredPassageWallSlotKind.CornerGuard:
                    size = new Vector2(.12f, 1.2f) * Mathf.Lerp(.82f, 1.08f, variation);
                    height = size.y * .5f + .10f;
                    along = draft.Index == 0 ? bounds.min.z + .18f : bounds.max.z - .18f;
                    authored = new Vector2(.12f, 1.2f); break;
                default:
                    size = Vector2.zero; height = 0f; authored = Vector2.one; break;
            }
            height = Mathf.Clamp(height, size.y * .5f + .12f, bounds.size.y - size.y * .5f - .16f);
            Vector3 scale = draft.Kind == CityCoveredPassageWallSlotKind.Empty ? Vector3.one
                : new Vector3(size.x / authored.x, size.y / authored.y, 1f);
            int variant = draft.Kind == CityCoveredPassageWallSlotKind.Graffiti ? draft.GraffitiVariant
                : (graffitiIndex + (int)(CityCoveredPassagePlan.Mix(draft.Seed ^ 0x2c9277b5u) & 7u) + (draft.Side < 0 ? 0 : 3)) % 8;
            return new CityCoveredPassageWallSlot(draft.Side, draft.Index, along, height, size, scale, draft.Kind, variant);
        }

        private static CityCoveredPassageWallSlotKind Utility(uint seed, int slot, int count)
        {
            int kind = 3 + (int)(CityCoveredPassagePlan.Mix(seed) % 5u);
            if (kind == (int)CityCoveredPassageWallSlotKind.CornerGuard && slot != 0 && slot != count - 1)
                return CityCoveredPassageWallSlotKind.VentGrille;
            return (CityCoveredPassageWallSlotKind)kind;
        }

        private static float Unit(uint seed, uint salt) => (CityCoveredPassagePlan.Mix(seed ^ salt) & 0xffffu) / 65535f;
        private static void Shuffle(List<int> values, ref uint seed)
        {
            for (int index = values.Count - 1; index > 0; index--)
            {
                seed = CityCoveredPassagePlan.Mix(seed + (uint)index * 0x85ebca6bu);
                int other = (int)(seed % (uint)(index + 1));
                (values[index], values[other]) = (values[other], values[index]);
            }
        }
    }
}
