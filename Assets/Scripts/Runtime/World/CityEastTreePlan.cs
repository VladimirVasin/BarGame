using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct CityEastTreePart
    {
        internal CityEastTreePart(string id, Vector3 position, Quaternion rotation, float height, int variant, bool front)
        {
            Id = id; Position = position; Rotation = rotation; Height = height; Variant = variant; IsFront = front;
            // Fixed-metre ParkTree crown tops from the existing Blender kit.
            Scale = height / (5.23f + variant * .24f);
            TrunkFootprint = CityEastExitDressingPlan.FootprintFor(position, rotation,
                new Vector3(.68f, 2.3f, .68f) * Scale);
            float crownRadius = 1.75f * Scale;
            CrownFootprint = new Rect(position.x - crownRadius, position.z - crownRadius,
                crownRadius * 2f, crownRadius * 2f);
        }
        public string Id { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public float Height { get; }
        public int Variant { get; }
        public bool IsFront { get; }
        public float Scale { get; }
        public Rect TrunkFootprint { get; }
        public Rect CrownFootprint { get; }
    }

    /// <summary>Rare deciduous silhouettes on both sides of the municipal fence; art bible §10e.</summary>
    public sealed class CityEastTreePlan
    {
        private static readonly ConditionalWeakTable<CityEastExitPlan, CityEastTreePlan> Plans =
            new ConditionalWeakTable<CityEastExitPlan, CityEastTreePlan>();
        public IReadOnlyList<CityEastTreePart> Parts { get; }

        public static CityEastTreePlan Create(CityEastExitPlan exit)
        {
            if (exit == null) throw new ArgumentNullException(nameof(exit));
            return Plans.GetValue(exit, source => new CityEastTreePlan(source));
        }

        private CityEastTreePlan(CityEastExitPlan exit)
        {
            var parts = new List<CityEastTreePart>();
            Parts = new ReadOnlyCollection<CityEastTreePart>(parts);
            if (!exit.IsEnabled) return;
            var exclusions = new List<Rect>
            {
                Expand(exit.ClearanceBounds, 3f), Expand(exit.BoothPad, 2f)
            };
            CityEastExitDressingPlan dressing = CityEastExitDressingPlan.Create(exit);
            foreach (CityEastExitDressingSolid solid in dressing.Solids)
                exclusions.Add(Expand(solid.Footprint, .65f));
            foreach (CityEastExitDressingPart part in dressing.Parts)
                if (part.GroupId == "Post Foot Traces" || part.GroupId == "Service Yard Traces")
                    exclusions.Add(Expand(part.Footprint, .6f));
            foreach (CityFringeYardPartDescriptor part in CityFringeYardPlanner.CreateEastUtilityYard(exit.Layout).Parts)
                if (part.BlocksMovement || part.Kind == CityFringeYardPartKind.UtilityPole)
                    exclusions.Add(Expand(part.Footprint, .6f));
            CityEastLitterPlan litter = CityEastLitterPlan.Create(exit);
            foreach (CityEastLitterPart part in litter.Parts)
                if (part.Item.Solid) exclusions.Add(Expand(part.Footprint, .5f));
            exclusions.Add(litter.PedestrianCorridor);
            var guards = new CityEastGuardPlan(exit);
            for (int actor = 0; actor < 2; actor++)
            for (int waypoint = 1; waypoint < 5; waypoint++)
            {
                Vector3 a = guards.Target(actor, waypoint - 1), b = guards.Target(actor, waypoint);
                exclusions.Add(Expand(Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z),
                    Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z)), 1f));
            }

            AddSide(true);
            AddSide(false);

            void AddSide(bool front)
            {
                var random = new System.Random(unchecked(exit.Layout.Seed * 397 ^ (front ? 0x18AE713 : 0x649EB35)));
                float z = exit.YardBounds.yMin + (front ? 19f : 30f) + Next(random) * 5f;
                int slot = 0;
                while (z < exit.NorthYardBounds.yMax - 6f)
                {
                    for (int attempt = 0; attempt < 12; attempt++)
                    {
                        float x = exit.CheckpointPosition.x + (front
                            ? -Mathf.Lerp(1.45f, 2.1f, Next(random))
                            : Mathf.Lerp(3.8f, 10.5f, Next(random)));
                        float candidateZ = z + (Next(random) - .5f) * 5f;
                        Vector2 point = new Vector2(x, candidateZ);
                        int variant = (slot + (front ? 0 : 2)) % CityMiscAssetProvider.GetVariantCount(CityMiscKind.ParkTree);
                        var candidate = new CityEastTreePart("East Tree " + (front ? "Front " : "Rear ") + slot,
                            new Vector3(x, exit.SampleGroundTop(point), candidateZ),
                            Quaternion.Euler(0f, Next(random) * 360f, 0f),
                            Mathf.Lerp(3f, 5f, Next(random)), variant, front);
                        Rect crown = candidate.CrownFootprint;
                        // Keep church/beach ends and the remote outer boundary open.
                        if (crown.xMin < exit.YardBounds.xMin + 1f || crown.xMax > exit.YardBounds.xMax - 3f ||
                            crown.yMin < exit.YardBounds.yMin + 4f || crown.yMax > exit.NorthYardBounds.yMax - 4f ||
                            exit.Swale.OverlapsCrossing(crown)) continue;
                        bool blocked = false;
                        foreach (Rect excluded in exclusions)
                            if (excluded.Overlaps(crown)) { blocked = true; break; }
                        foreach (CityEastTreePart other in parts)
                            if (other.IsFront == front ? Mathf.Abs(other.Position.z - candidateZ) < 25f :
                                Mathf.Abs(other.Position.z - candidateZ) < 6f) { blocked = true; break; }
                        if (blocked) continue;
                        parts.Add(candidate);
                        break;
                    }
                    z += Mathf.Lerp(front ? 34f : 25f, front ? 40f : 32f, Next(random));
                    slot++;
                }
            }
        }

        public bool BlocksStandingAt(Vector2 point, float radius)
        {
            foreach (CityEastTreePart part in Parts)
                if (Expand(part.TrunkFootprint, radius).Contains(point)) return true;
            return false;
        }

        private static float Next(System.Random random) => (float)random.NextDouble();
        private static Rect Expand(Rect rect, float amount) => Rect.MinMaxRect(
            rect.xMin - amount, rect.yMin - amount, rect.xMax + amount, rect.yMax + amount);
    }
}
