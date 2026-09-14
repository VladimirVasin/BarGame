using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    public enum CityEastExitDressingFit { Feet, Ground, Road }

    public readonly struct CityEastExitDressingPart
    {
        internal CityEastExitDressingPart(string id, string assembly, string group, Vector3 position,
            Quaternion rotation, Vector3 scale, Vector3 size, CityEastExitDressingFit fit)
        { Id = id; Assembly = assembly; GroupId = group; Position = position; Rotation = rotation;
            Scale = scale; Size = size; Fit = fit; Footprint = CityEastExitDressingPlan.FootprintFor(position, rotation, Vector3.Scale(size, scale)); }
        public string Id { get; }
        public string Assembly { get; }
        public string GroupId { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public Vector3 Scale { get; }
        public Vector3 Size { get; }
        public Rect Footprint { get; }
        public CityEastExitDressingFit Fit { get; }
    }

    public readonly struct CityEastExitDressingSolid
    {
        internal CityEastExitDressingSolid(string id, string partId, Vector3 position, Quaternion rotation, Vector3 size)
        { Id = id; PartId = partId; Position = position; Rotation = rotation; Size = size;
            Footprint = CityEastExitDressingPlan.FootprintFor(position, rotation, size); }
        public string Id { get; }
        public string PartId { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public Vector3 Size { get; }
        public Rect Footprint { get; }
    }

    /// <summary>Finite authored objects and thin terrain-following traces around the existing post.</summary>
    public sealed class CityEastExitDressingPlan
    {
        private static readonly ConditionalWeakTable<CityEastExitPlan, CityEastExitDressingPlan> Plans =
            new ConditionalWeakTable<CityEastExitPlan, CityEastExitDressingPlan>();
        private readonly CityEastExitPlan exit;
        public bool IsEnabled => exit.IsEnabled;
        public IReadOnlyList<CityEastExitDressingPart> Parts { get; }
        /// <summary>Only grounded obstacles; the shelter's overhead roof never excludes map landings.</summary>
        public IReadOnlyList<CityEastExitDressingSolid> Solids { get; }
        public static CityEastExitDressingPlan Create(CityEastExitPlan exit)
        {
            if (exit == null) throw new ArgumentNullException(nameof(exit));
            return Plans.GetValue(exit, value => new CityEastExitDressingPlan(value));
        }
        private CityEastExitDressingPlan(CityEastExitPlan source)
        {
            exit = source;
            var parts = new List<CityEastExitDressingPart>();
            var solids = new List<CityEastExitDressingSolid>();
            Parts = new ReadOnlyCollection<CityEastExitDressingPart>(parts);
            Solids = new ReadOnlyCollection<CityEastExitDressingSolid>(solids);
            if (!exit.IsEnabled) return;
            float x = exit.CheckpointPosition.x, z = exit.CheckpointPosition.z;
            Part("Booth Shelter", "Shelter", "Booth Surroundings", new Vector2(x - 2f, z - 10.15f), 0f,
                Vector3.one, new Vector3(3.6f, 2.65f, 3.2f), CityEastExitDressingFit.Feet, exit.BoothPosition.y);
            for (int side = -1; side <= 1; side += 2)
                Solid("Shelter Post " + side, "Booth Shelter", new Vector2(x - 2f + side * 1.55f, z - 11.45f),
                    Quaternion.identity, new Vector3(.30f, 2.56f, .30f), exit.BoothPosition.y + 2.56f);
            Part("Shelter Bench", "Bench", "Booth Surroundings", new Vector2(x - 2.8f, z - 10.8f), 0f,
                Vector3.one, new Vector3(1.9f, .88f, .66f), CityEastExitDressingFit.Feet);
            Solid("Shelter Bench", "Shelter Bench", new Vector2(x - 2.8f, z - 10.8f), Quaternion.identity,
                new Vector3(1.9f, .88f, .66f));
            Part("Booth Utility Cabinet", "UtilityCabinet", "Booth Surroundings", new Vector2(x - 3.78f, z - 7.5f), 90f,
                Vector3.one, new Vector3(.82f, 1.45f, .56f), CityEastExitDressingFit.Feet);
            Solid("Booth Utility Cabinet", "Booth Utility Cabinet", new Vector2(x - 3.78f, z - 7.5f), Quaternion.Euler(0, 90, 0),
                new Vector3(.82f, 1.45f, .56f));

            // Worn ground records ordinary use without introducing another
            // patrol. The west/south approach passes outside the cabinet,
            // bench and front posts; the gate branch skirts the lamp north.
            Trace("Post Foot Trace West 0", "GravelPatch", "Post Foot Traces",
                new Vector2(x - 6.5f, z - 4.6f), new Vector2(x - 5.35f, z - 6.8f), .68f);
            Trace("Post Foot Trace West 1", "GravelPatch", "Post Foot Traces",
                new Vector2(x - 5.35f, z - 6.8f), new Vector2(x - 5.2f, z - 9.8f), .68f);
            Trace("Post Foot Trace South 0", "GravelPatch", "Post Foot Traces",
                new Vector2(x - 5.2f, z - 9.8f), new Vector2(x - 3.9f, z - 9.9f), .68f);
            Trace("Post Foot Trace South 1", "GravelPatch", "Post Foot Traces",
                new Vector2(x - 3.9f, z - 9.9f), new Vector2(x - 2f, z - 9.65f), .68f);
            Trace("Post Foot Trace Door", "GravelPatch", "Post Foot Traces",
                new Vector2(x - 2f, z - 9.65f), new Vector2(x - 2f, z - 8.82f), .68f);
            Trace("Post Foot Trace Gate 0", "GravelPatch", "Post Foot Traces",
                new Vector2(x - 6.5f, z - 4.6f), new Vector2(x - 4.85f, z - 3.45f), .66f);
            Trace("Post Foot Trace Gate 1", "GravelPatch", "Post Foot Traces",
                new Vector2(x - 4.85f, z - 3.45f), new Vector2(x - 1.05f, z - 3.3f), .66f);
            Trace("Post Foot Trace Gate 2", "GravelPatch", "Post Foot Traces",
                new Vector2(x - 1.05f, z - 3.3f), new Vector2(x - .5f, z - 3.7f), .66f);
            Trace("Post Foot Trace North", "GravelPatch", "Post Foot Traces",
                new Vector2(x - 5.2f, z + 4.5f), new Vector2(x - .55f, z + 3.8f), .66f);

            for (int side = -1; side <= 1; side += 2)
                Trace("Road Shoulder " + side, "GravelPatch", "Road Margins",
                    new Vector2(exit.YardBounds.xMin + 1.5f, z + side * 4.35f),
                    new Vector2(x - 8f, z + side * 4.43f), .72f);
            Part("Old Road Repair", "RoadRepair", "Road Margins", new Vector2(exit.YardBounds.xMin + 5f, z - .35f), -5f,
                Vector3.one, new Vector3(4.4f, .02f, 3.10f), CityEastExitDressingFit.Road);
            Trace("Dry Road Drain", "DryDrain", "Road Margins",
                new Vector2(exit.YardBounds.xMin + 2.5f, z - 6.2f), new Vector2(x - 8f, z - 6.45f), .76f);

            float serviceX = exit.YardBounds.xMin + 24f;
            Trace("Service Yard Entry", "GravelPatch", "Service Yard Traces",
                new Vector2(exit.YardBounds.xMin + 18f, z + 4.3f), new Vector2(serviceX, z + 9f), .78f);
            Trace("Service Yard Trace", "GravelPatch", "Service Yard Traces",
                new Vector2(serviceX, z + 9f), new Vector2(serviceX + .3f, exit.YardBounds.yMax - 8f), .78f);
            for (int i = 0; i < 3; i++)
            {
                float shedZ = Mathf.Lerp(exit.YardBounds.yMin + 12f, exit.YardBounds.yMax - 12f, .18f + i * .32f);
                Trace("Shed Approach " + i, "GravelPatch", "Service Yard Traces",
                    new Vector2(serviceX, shedZ), new Vector2(exit.YardBounds.xMin + 32.2f + i * 2.2f, shedZ), .76f);
                Part("Shed Door Apron " + i, "GravelPatch", "Service Yard Traces",
                    new Vector2(exit.YardBounds.xMin + 31.45f + i * 2.2f, shedZ), 0f,
                    new Vector3(1.8f / 6f, 1f, 1.4f / 3.46f),
                    new Vector3(6f, .02f, 3.46f), CityEastExitDressingFit.Ground);
            }
            foreach (float poleZ in new[] { 0f, exit.YardBounds.yMax - 8f })
                Trace("Pole Approach " + poleZ, "GravelPatch", "Service Yard Traces",
                    new Vector2(serviceX, poleZ), new Vector2(exit.YardBounds.xMin + 26.7f, poleZ), .62f);

            Vector2[] groups = { new Vector2(x + 13f, z + 22f), new Vector2(x - 7f, exit.NorthYardBounds.yMin + 22f),
                new Vector2(x + 12f, exit.NorthYardBounds.yMin + 42f), new Vector2(exit.YardBounds.xMin + 47f, exit.NorthYardBounds.yMax - 17f) };
            Vector2[] grass = { new Vector2(-2.4f, -.9f), new Vector2(1.8f, -1.25f), new Vector2(3.7f, .7f),
                new Vector2(-1.1f, 2.1f), new Vector2(1.5f, 3.2f) };
            for (int i = 0; i < groups.Length; i++)
            {
                string group = "Roadside Group " + (i + 1);
                float yaw = 17f + i * 53f;
                Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
                Vector3 ridgeScale = new Vector3(1.2f + i % 2 * .18f, .8f + i % 2 * .12f, 1.15f);
                Part(group + " Low Ridge", "GroundRidge", group, groups[i], yaw, ridgeScale,
                    new Vector3(4.04f, .48f, 1.81f), CityEastExitDressingFit.Ground);
                Solid(group + " Low Ridge", group + " Low Ridge", groups[i], turn,
                    Vector3.Scale(new Vector3(4.04f, .48f, 1.81f), ridgeScale));
                for (int shrub = 0; shrub < 2; shrub++)
                {
                    Vector3 offset = turn * new Vector3(shrub == 0 ? -1.7f : 2.15f, 0, shrub == 0 ? .8f : 1.2f);
                    Part(group + " Shrub " + shrub, "Shrub", group, groups[i] + new Vector2(offset.x, offset.z), yaw + shrub * 71f,
                        Vector3.one * (.85f + (i + shrub) % 3 * .12f), new Vector3(1.80f, 1.17f, 1.60f), CityEastExitDressingFit.Feet);
                }
                for (int j = 0; j < grass.Length; j++)
                {
                    Vector3 offset = turn * new Vector3(grass[j].x, 0, grass[j].y);
                    float scale = .72f + (i + j) % 4 * .14f;
                    Part(group + " Dry Grass " + j, "DryGrass", group, groups[i] + new Vector2(offset.x, offset.z), yaw + j * 61f,
                        Vector3.one * scale, new Vector3(1.4f, .65f, 1.1f), CityEastExitDressingFit.Ground);
                }
            }

            void Part(string id, string assembly, string group, Vector2 point, float yaw, Vector3 scale, Vector3 size,
                CityEastExitDressingFit fit, float? ground = null)
            {
                float top = ground ?? (fit == CityEastExitDressingFit.Road ? exit.SampleRoadTop(point.x) : exit.SampleGroundTop(point));
                parts.Add(new CityEastExitDressingPart(id, assembly, group, new Vector3(point.x, top, point.y),
                    Quaternion.Euler(0, yaw, 0), scale, size, fit));
            }
            void Solid(string id, string partId, Vector2 point, Quaternion rotation, Vector3 size, float? top = null)
            {
                Rect bounds = FootprintFor(new Vector3(point.x, 0, point.y), rotation, size);
                float low = Mathf.Min(exit.SampleGroundTop(new Vector2(bounds.xMin, bounds.yMin)),
                    exit.SampleGroundTop(new Vector2(bounds.xMax, bounds.yMin)), exit.SampleGroundTop(new Vector2(bounds.xMin, bounds.yMax)),
                    exit.SampleGroundTop(new Vector2(bounds.xMax, bounds.yMax)));
                float high = top ?? exit.SampleGroundTop(point) + size.y;
                size.y = Mathf.Max(.05f, high - low);
                solids.Add(new CityEastExitDressingSolid(id, partId, new Vector3(point.x, (high + low) * .5f, point.y), rotation, size));
            }
            void Trace(string id, string assembly, string group, Vector2 start, Vector2 end, float width)
            {
                float total = Vector2.Distance(start, end);
                int count = Mathf.Max(1, Mathf.CeilToInt(total / 8f));
                Vector2 direction = (end - start).normalized;
                float yaw = -Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                Vector3 size = assembly == "DryDrain" ? new Vector3(10f, .10f, 1.382f) : new Vector3(6f, .02f, 3.46f);
                for (int i = 0; i < count; i++)
                    Part(id + " " + i, assembly, group, Vector2.Lerp(start, end, (i + .5f) / count), yaw,
                        new Vector3(total / count / size.x, 1f, width / size.z), size, CityEastExitDressingFit.Ground);
            }
        }

        public bool BlocksStandingAt(Vector2 point, float radius = 0f)
        {
            foreach (CityEastExitDressingSolid solid in Solids)
            {
                Rect bounds = solid.Footprint;
                if (point.x >= bounds.xMin - radius && point.x <= bounds.xMax + radius &&
                    point.y >= bounds.yMin - radius && point.y <= bounds.yMax + radius) return true;
            }
            return false;
        }
        public bool IsRepairSpan(CityEastExitFence span)
        {
            Vector3 center = (span.Start + span.End) * .5f;
            return Mathf.Abs(center.x - exit.CheckpointPosition.x) < .01f &&
                center.z > exit.CheckpointPosition.z + 11f && center.z < exit.CheckpointPosition.z + 15f;
        }
        internal static Rect FootprintFor(Vector3 position, Quaternion rotation, Vector3 size)
        {
            Vector3 right = rotation * new Vector3(size.x * .5f, 0, 0), forward = rotation * new Vector3(0, 0, size.z * .5f);
            float x = Mathf.Abs(right.x) + Mathf.Abs(forward.x), z = Mathf.Abs(right.z) + Mathf.Abs(forward.z);
            return Rect.MinMaxRect(position.x - x, position.z - z, position.x + x, position.z + z);
        }
    }
}
