using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum WarehouseArenaTemplate { Straight, LeftGallery, UShape }
    public enum WarehouseArenaObstacleKind { Rack, Cargo, Support }

    public readonly struct WarehouseArenaStair
    {
        public readonly Vector3 Foot;
        public Vector3 Top => Foot + new Vector3(0f, WarehouseArenaPlan.UpperHeight, 4.8f);
        public Rect Bounds => new Rect(Foot.x - 1f, Foot.z, 2f, 4.8f);
        public WarehouseArenaStair(float x) { Foot = new Vector3(x, 0f, .7f); }
        public float HeightAt(float z) => Mathf.Clamp(Mathf.Floor((z - Foot.z) / .3f) + 1f, 1f, 16f) * .1625f;
    }

    public readonly struct WarehouseArenaObstacle
    {
        public readonly WarehouseArenaObstacleKind Kind;
        public readonly Vector3 Position;
        public readonly float Yaw;
        public readonly Bounds Bounds;
        public WarehouseArenaObstacle(WarehouseArenaObstacleKind kind, Vector3 position, float yaw, Vector3 size)
        {
            Kind = kind; Position = position; Yaw = yaw;
            if (Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad)) > .5f) size = new Vector3(size.z, size.y, size.x);
            Bounds = new Bounds(position + Vector3.up * size.y * .5f, size);
        }
    }

    public readonly struct WarehouseArenaEdge
    {
        public readonly int From, To;
        public WarehouseArenaEdge(int from, int to) { From = from; To = to; }
    }

    /// <summary>All dimensions are metres. The plan owns collision, safe routes and imported-kit placement.</summary>
    public sealed class WarehouseArenaPlan
    {
        public const string GeneratorVersion = "warehouse-v1";
        public const float UpperHeight = 2.6f;
        public int Seed { get; }
        public string Version => GeneratorVersion;
        public WarehouseArenaTemplate Template { get; }
        public Vector3 HeroSpawn { get; }
        public Vector3 OpponentSpawn { get; }
        public IReadOnlyList<Vector3> SpawnPoints { get; }
        public IReadOnlyList<Vector3> Platforms { get; }
        public IReadOnlyList<WarehouseArenaStair> Stairs { get; }
        public IReadOnlyList<WarehouseArenaObstacle> Obstacles { get; }
        public IReadOnlyList<Vector3> NavigationNodes { get; private set; }
        public IReadOnlyList<WarehouseArenaEdge> NavigationEdges { get; private set; }

        internal WarehouseArenaPlan(int seed, WarehouseArenaTemplate template, Vector3[] platforms,
            WarehouseArenaStair[] stairs, WarehouseArenaObstacle[] obstacles, Vector3[] spawns)
        {
            Seed = seed; Template = template; Platforms = Array.AsReadOnly(platforms);
            Stairs = Array.AsReadOnly(stairs); Obstacles = Array.AsReadOnly(obstacles);
            SpawnPoints = Array.AsReadOnly(spawns);
            HeroSpawn = new Vector3(0f, 0f, -6f); OpponentSpawn = new Vector3(0f, 0f, 3.4f);
            NavigationNodes = Array.Empty<Vector3>(); NavigationEdges = Array.Empty<WarehouseArenaEdge>();
        }

        internal void SetNavigation(Vector3[] nodes, WarehouseArenaEdge[] edges)
        { NavigationNodes = Array.AsReadOnly(nodes); NavigationEdges = Array.AsReadOnly(edges); }
    }

    public static class WarehouseArenaPlanner
    {
        public static WarehouseArenaPlan Create(int seed)
        {
            // Fixed compatible slots make generation bounded. The selected cargo arrangement,
            // stair spacing and upper gallery change routes without risking the reserved aisles.
            uint state = unchecked((uint)seed) ^ 0x57415245u;
            WarehouseArenaTemplate template = (WarehouseArenaTemplate)(Next(ref state) % 3u);
            float left = (Next(ref state) & 1u) == 0u ? -4f : -7f;
            float right = (Next(ref state) & 1u) == 0u ? 4f : 7f;
            var stairs = new[] { new WarehouseArenaStair(left), new WarehouseArenaStair(right) };
            var platforms = new List<Vector3>();
            for (int i = 0; i < 8; i++) platforms.Add(new Vector3(-10.5f + i * 3f, 2.6f, 7f));
            if (template != WarehouseArenaTemplate.Straight)
            { platforms.Add(new Vector3(-10.5f, 2.6f, 1f)); platforms.Add(new Vector3(-10.5f, 2.6f, 4f)); }
            if (template == WarehouseArenaTemplate.UShape)
            { platforms.Add(new Vector3(10.5f, 2.6f, 1f)); platforms.Add(new Vector3(10.5f, 2.6f, 4f)); }
            var obstacles = new List<WarehouseArenaObstacle>();
            foreach (Vector3 platform in platforms)
                foreach (float x in new[] { -1.34f, 1.34f })
                    foreach (float z in new[] { -1.34f, 1.34f })
                        obstacles.Add(new WarehouseArenaObstacle(WarehouseArenaObstacleKind.Support,
                            new Vector3(platform.x + x, 0f, platform.z + z), 0f, new Vector3(.26f, 2.42f, .26f)));
            bool across = (Next(ref state) & 1u) == 0u;
            float rackYaw = across ? 0f : 90f;
            float rackZ = across ? -3f : -4f;
            foreach (float x in new[] { -6.1f, 6.1f })
                obstacles.Add(new WarehouseArenaObstacle(WarehouseArenaObstacleKind.Rack,
                    new Vector3(x, 0f, rackZ), rackYaw, new Vector3(3f, 2.2f, 1.2f)));
            foreach (float side in new[] { -1f, 1f })
            {
                float cargoX = side * (3.4f + (Next(ref state) % 3u) * .45f);
                obstacles.Add(new WarehouseArenaObstacle(WarehouseArenaObstacleKind.Cargo,
                    new Vector3(cargoX, 0f, across ? -5.6f : -3.1f), across ? 90f : 0f, new Vector3(2f, 1.25f, 1.4f)));
                // The rear pile occupies the gap between the central aisle and its stair.
                float stairX = side < 0f ? left : right;
                if (Mathf.Abs(stairX) > 6f)
                    obstacles.Add(new WarehouseArenaObstacle(WarehouseArenaObstacleKind.Cargo,
                        new Vector3(side * 3.7f, 0f, 3.3f), 0f, new Vector3(2f, 1.25f, 1.4f)));
            }
            var spawns = new List<Vector3>
            {
                new Vector3(0f,0f,-6f), new Vector3(-8.8f,0f,-6.8f), new Vector3(8.8f,0f,-6.8f),
                new Vector3(0f,0f,3.4f), new Vector3(0f,2.6f,7f),
                new Vector3(-7f,2.6f,7f), new Vector3(7f,2.6f,7f)
            };
            if (template != WarehouseArenaTemplate.Straight) spawns.Add(new Vector3(-10.5f,2.6f,1f));
            if (template == WarehouseArenaTemplate.UShape) spawns.Add(new Vector3(10.5f,2.6f,1f));
            var plan = new WarehouseArenaPlan(seed, template, platforms.ToArray(), stairs, obstacles.ToArray(), spawns.ToArray());
            BuildNavigation(plan);
            WarehouseArenaValidator.ValidateOrThrow(plan);
            return plan;
        }

        private static uint Next(ref uint state)
        { state = unchecked(state * 1664525u + 1013904223u); return state; }

        private static void BuildNavigation(WarehouseArenaPlan plan)
        {
            var area = new WarehouseArenaArea(plan);
            var nodes = new List<Vector3>();
            for (float z = -7.5f; z <= 7.51f; z += 1.5f)
                for (float x = -10.5f; x <= 10.51f; x += 1.5f)
                {
                    var position = new Vector3(x, 0f, z);
                    if (area.TryGetHeight(position, out float height, .4f) && Mathf.Abs(height) < .01f) nodes.Add(position);
                }
            foreach (Vector3 platform in plan.Platforms)
            {
                nodes.Add(platform);
                nodes.Add(platform + new Vector3(-.75f,0f,0f));
                nodes.Add(platform + new Vector3(.75f,0f,0f));
                // Gallery tiles join along either axis. The side wings need
                // seam nodes in Z just as the rear run needs them in X.
                nodes.Add(platform + new Vector3(0f,0f,-.75f));
                nodes.Add(platform + new Vector3(0f,0f,.75f));
            }
            foreach (WarehouseArenaStair stair in plan.Stairs)
            {
                nodes.Add(stair.Foot + Vector3.back * .5f);
                for (int step = 0; step < 16; step++)
                    nodes.Add(stair.Foot + new Vector3(0f,(step+1)*.1625f,(step+.5f)*.3f));
                nodes.Add(stair.Top + Vector3.forward * .5f);
            }
            foreach (Vector3 spawn in plan.SpawnPoints) nodes.Add(spawn);
            var edges = new List<WarehouseArenaEdge>();
            for (int i = 0; i < nodes.Count; i++)
                for (int j = i + 1; j < nodes.Count; j++)
                {
                    if ((nodes[i] - nodes[j]).sqrMagnitude > 4.42f || !area.CanTraverse(nodes[i], nodes[j], .36f)) continue;
                    edges.Add(new WarehouseArenaEdge(i,j));
                }
            plan.SetNavigation(nodes.ToArray(), edges.ToArray());
        }
    }

    public static class WarehouseArenaValidator
    {
        public static void ValidateOrThrow(WarehouseArenaPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (plan.Stairs.Count != 2 || plan.Platforms.Count < 8 || plan.NavigationNodes.Count == 0)
                throw new InvalidOperationException("Warehouse needs two stairs, an upper gallery and navigation.");
            var area = new WarehouseArenaArea(plan);
            foreach (Vector3 spawn in plan.SpawnPoints)
                if (!area.TryGetHeight(spawn, out float height, .5f) || Mathf.Abs(height - spawn.y) > .02f)
                    throw new InvalidOperationException($"Warehouse seed {plan.Seed} ({plan.Template}) spawn {spawn} does not have capsule clearance.");
            for (int i = 0; i < plan.Obstacles.Count; i++)
                for (int j = i + 1; j < plan.Obstacles.Count; j++)
                {
                    WarehouseArenaObstacle a = plan.Obstacles[i], b = plan.Obstacles[j];
                    if (a.Kind == WarehouseArenaObstacleKind.Support || b.Kind == WarehouseArenaObstacleKind.Support) continue;
                    if (a.Bounds.Intersects(b.Bounds))
                        throw new InvalidOperationException("Warehouse cargo and rack footprints intersect.");
                }
            var route = new List<Vector3>();
            foreach (Vector3 spawn in plan.SpawnPoints)
                if (!area.FindRoute(plan.HeroSpawn, spawn, route))
                    throw new InvalidOperationException($"Warehouse seed {plan.Seed} ({plan.Template}) spawn {spawn} is disconnected from lower entrance {plan.HeroSpawn}.");
            foreach (WarehouseArenaStair stair in plan.Stairs)
                if (!area.FindRoute(stair.Foot + Vector3.back * .5f, stair.Top + Vector3.forward * .5f, route))
                    throw new InvalidOperationException($"Warehouse seed {plan.Seed} ({plan.Template}) stair {stair.Foot} does not join both levels.");
        }
    }
}
