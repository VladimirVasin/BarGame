using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum WarehouseArenaTemplate { Straight, LeftGallery, UShape }
    public enum WarehouseArenaObstacleKind { Rack, Cargo, Support, Wall, Rail, Gate, Mechanism, Furniture }

    public readonly struct WarehouseArenaStair
    {
        public const float Width = 2.4f, ClearWidth = 2.13f, Run = 6f, TreadDepth = .3f, TreadRise = .16f;
        public const int TreadCount = 20;
        public readonly Vector3 Foot;
        public Vector3 Top => Foot + new Vector3(0f, WarehouseArenaPlan.LevelRise, Run);
        public Rect Bounds => new Rect(Foot.x - Width * .5f, Foot.z, Width, Run);
        public WarehouseArenaStair(float x, float floor = 0f) { Foot = new Vector3(x, floor, -3f); }
        public float HeightAt(float z) => Foot.y + Mathf.Clamp(Mathf.Floor((z - Foot.z) / TreadDepth) + 1f,
            1f, TreadCount) * TreadRise;
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

    public readonly struct WarehouseArenaSurface
    {
        public readonly Rect Bounds;
        public readonly float Height;
        public WarehouseArenaSurface(Rect bounds, float height) { Bounds = bounds; Height = height; }
    }

    public readonly struct WarehouseArenaPlacement
    {
        public readonly string Kind;
        public readonly Vector3 Position;
        public readonly float Yaw;
        public WarehouseArenaPlacement(string kind, Vector3 position, float yaw = 0f)
        { Kind = kind; Position = position; Yaw = yaw; }
    }

    public readonly struct WarehouseArenaRailSpan
    {
        public readonly Vector3 Position;
        public readonly float Yaw, Length;
        public WarehouseArenaRailSpan(Vector3 position, float yaw, float length)
        { Position = position; Yaw = yaw; Length = length; }
    }

    public readonly struct WarehouseArenaLamp
    {
        public readonly Vector3 Position, Direction;
        public readonly float Range, Intensity;
        public WarehouseArenaLamp(Vector3 position, Vector3 direction, float range, float intensity)
        { Position = position; Direction = direction.normalized; Range = range; Intensity = intensity; }
    }

    public readonly struct WarehouseArenaMechanism
    {
        public readonly string Kind;
        public readonly Vector3 Position;
        public readonly float Yaw, Phase;
        public WarehouseArenaMechanism(string kind, Vector3 position, float yaw, float phase)
        { Kind = kind; Position = position; Yaw = yaw; Phase = phase; }
    }

    public readonly struct WarehouseArenaEdge
    {
        public readonly int From, To;
        public WarehouseArenaEdge(int from, int to) { From = from; To = to; }
    }

    /// <summary>Metre-space authority for imported geometry, all storeys, guarded machinery and safe routes.</summary>
    public sealed class WarehouseArenaPlan
    {
        public const string GeneratorVersion = "warehouse-v2";
        public const float LevelRise = 3.2f, TopHeight = 6.4f, TileSize = 3f, DeckThickness = .18f;
        public const float HalfWidth = 15f, HalfDepth = 12f, CeilingHeight = 10.2f;
        // Compatibility for clients that capture the first gallery. Storey logic uses LevelRise/TopHeight.
        public const float UpperHeight = LevelRise;
        public int Seed { get; }
        public string Version => GeneratorVersion;
        public WarehouseArenaTemplate Template { get; }
        public Bounds InteriorBounds => new Bounds(new Vector3(0f, CeilingHeight * .5f, 0f),
            new Vector3(HalfWidth * 2f, CeilingHeight, HalfDepth * 2f));
        public Vector3 HeroSpawn => new Vector3(0f, 0f, -9f);
        public Vector3 OpponentSpawn => new Vector3(0f, 0f, 3.4f);
        public Vector3 GatePosition => new Vector3(0f, 0f, 11.6f);
        public Vector3 GoalDoorPosition => new Vector3(0f, 0f, 11.9f);
        public Vector3 GateApproach => new Vector3(0f, 0f, 9.8f);
        public IReadOnlyList<Vector3> SpawnPoints { get; }
        public IReadOnlyList<Vector3> Platforms { get; }
        public IReadOnlyList<WarehouseArenaSurface> Surfaces { get; }
        public IReadOnlyList<WarehouseArenaStair> Stairs { get; }
        public IReadOnlyList<WarehouseArenaObstacle> Obstacles { get; }
        public IReadOnlyList<WarehouseArenaPlacement> ModulePlacements { get; }
        public IReadOnlyList<WarehouseArenaRailSpan> RailSpans { get; }
        public IReadOnlyList<WarehouseArenaLamp> Lamps { get; }
        public IReadOnlyList<WarehouseArenaMechanism> Mechanisms { get; }
        public IReadOnlyList<Vector3> RoomCenters { get; }
        public IReadOnlyList<Vector3> AmmoAnchors { get; }
        public IReadOnlyList<Vector3> NavigationNodes { get; private set; }
        public IReadOnlyList<WarehouseArenaEdge> NavigationEdges { get; private set; }

        internal WarehouseArenaPlan(int seed, WarehouseArenaTemplate template, Vector3[] platforms,
            WarehouseArenaSurface[] surfaces, WarehouseArenaStair[] stairs, WarehouseArenaObstacle[] obstacles,
            WarehouseArenaPlacement[] modules, WarehouseArenaRailSpan[] rails, WarehouseArenaLamp[] lamps,
            WarehouseArenaMechanism[] mechanisms, Vector3[] rooms, Vector3[] ammo, Vector3[] spawns)
        {
            Seed = seed; Template = template; Platforms = Array.AsReadOnly(platforms);
            Surfaces = Array.AsReadOnly(surfaces); Stairs = Array.AsReadOnly(stairs); Obstacles = Array.AsReadOnly(obstacles);
            ModulePlacements = Array.AsReadOnly(modules); RailSpans = Array.AsReadOnly(rails);
            Lamps = Array.AsReadOnly(lamps); Mechanisms = Array.AsReadOnly(mechanisms);
            RoomCenters = Array.AsReadOnly(rooms); AmmoAnchors = Array.AsReadOnly(ammo); SpawnPoints = Array.AsReadOnly(spawns);
            NavigationNodes = Array.Empty<Vector3>(); NavigationEdges = Array.Empty<WarehouseArenaEdge>();
        }

        internal void SetNavigation(Vector3[] nodes, WarehouseArenaEdge[] edges)
        { NavigationNodes = Array.AsReadOnly(nodes); NavigationEdges = Array.AsReadOnly(edges); }
    }

    public static class WarehouseArenaPlanner
    {
        public static WarehouseArenaPlan Create(int seed)
        {
            // Seed variation stays in bounded cargo slots and machine phase, preserving clear thresholds.
            uint state = unchecked((uint)seed) ^ 0x57415245u;
            WarehouseArenaTemplate template = (WarehouseArenaTemplate)(Next(ref state) % 3u);
            var modules = new List<WarehouseArenaPlacement> { new WarehouseArenaPlacement("Shell", Vector3.zero) };
            var platforms = new List<Vector3>();
            var surfaces = new List<WarehouseArenaSurface>
            { new WarehouseArenaSurface(new Rect(-15f, -12f, 30f, 24f), 0f) };
            var stairs = new[]
            {
                new WarehouseArenaStair(-7.5f), new WarehouseArenaStair(7.5f),
                new WarehouseArenaStair(-7.5f, WarehouseArenaPlan.LevelRise),
                new WarehouseArenaStair(7.5f, WarehouseArenaPlan.LevelRise)
            };
            for (int level = 1; level <= 2; level++)
                for (int row = 0; row < 8; row++)
                    for (int column = 0; column < 10; column++)
                    {
                        float x = -13.5f + column * 3f, z = -10.5f + row * 3f;
                        bool hall = Mathf.Abs(x) < 6f && Mathf.Abs(z) < 6f;
                        bool stairOpening = Mathf.Abs(Mathf.Abs(x) - 7.5f) < .01f && Mathf.Abs(z) < 3f;
                        bool liftOpening = Mathf.Abs(x + 10.5f) < .01f && Mathf.Abs(z - 7.5f) < .01f;
                        if (hall || stairOpening || liftOpening) continue;
                        Vector3 tile = new Vector3(x, level * WarehouseArenaPlan.LevelRise, z);
                        platforms.Add(tile); modules.Add(new WarehouseArenaPlacement("Platform", tile));
                        surfaces.Add(new WarehouseArenaSurface(new Rect(x - 1.5f, z - 1.5f, 3f, 3f), tile.y));
                    }
            var obstacles = new List<WarehouseArenaObstacle>();
            var rooms = new List<Vector3>();
            for (int level = 0; level <= 2; level++)
            {
                float y = level * WarehouseArenaPlan.LevelRise;
                AddRoom(new Vector3(-12f, y, -9f), 0f, level, rooms, modules, obstacles);
                AddRoom(new Vector3(12f, y, -9f), 180f, level, rooms, modules, obstacles);
                AddRoom(new Vector3(12f, y, 9f), 180f, level, rooms, modules, obstacles);
            }
            foreach (float side in new[] { -1f, 1f })
            {
                float yaw = template == WarehouseArenaTemplate.LeftGallery ? 90f : 0f;
                AddProp("Rack", WarehouseArenaObstacleKind.Rack, new Vector3(side * 3.9f, 0f, .1f),
                    yaw, new Vector3(3f, 2.2f, 1.2f), modules, obstacles);
                float drift = (Next(ref state) % 3u) * .2f;
                AddProp("Cargo", WarehouseArenaObstacleKind.Cargo, new Vector3(side * 4f, 0f, -4.7f + drift),
                    template == WarehouseArenaTemplate.UShape ? 90f : 0f, new Vector3(2f, 1.25f, 1.4f), modules, obstacles);
                AddProp("Cargo", WarehouseArenaObstacleKind.Cargo,
                    new Vector3(side * 3.9f, WarehouseArenaPlan.TopHeight, 10.2f - drift),
                    0f, new Vector3(2f, 1.25f, 1.4f), modules, obstacles);
            }
            modules.Add(new WarehouseArenaPlacement("Pipes", new Vector3(-14.75f, 0f, .3f), 90f));
            modules.Add(new WarehouseArenaPlacement("TwistedFrame", new Vector3(-2.4f, 3.5f, 3.8f), 10f));
            modules.Add(new WarehouseArenaPlacement("TwistedFrame", new Vector3(2.4f, 3.5f, 3.8f), -10f));
            modules.Add(new WarehouseArenaPlacement("MainDoor", new Vector3(0f, 0f, 11.9f)));
            modules.Add(new WarehouseArenaPlacement("Gate", new Vector3(0f, 0f, 11.6f)));
            obstacles.Add(new WarehouseArenaObstacle(WarehouseArenaObstacleKind.Gate,
                new Vector3(0f, 0f, 11.6f), 0f, new Vector3(4f, 3f, .18f)));
            var mechanisms = new[]
            {
                new WarehouseArenaMechanism("Fan", new Vector3(0f, 9.78f, 0f), 0f, Phase(ref state)),
                new WarehouseArenaMechanism("Lift", new Vector3(-10.5f, 0f, 7.5f), 0f, Phase(ref state)),
                new WarehouseArenaMechanism("Drum", new Vector3(-12f, 3.2f, 0f), 0f, Phase(ref state)),
                new WarehouseArenaMechanism("Pusher", new Vector3(-4.5f, 0f, 9f), 0f, Phase(ref state))
            };
            obstacles.Add(new WarehouseArenaObstacle(WarehouseArenaObstacleKind.Mechanism,
                mechanisms[1].Position, 0f, new Vector3(3.1f, 8.9f, 2.9f)));
            obstacles.Add(new WarehouseArenaObstacle(WarehouseArenaObstacleKind.Mechanism,
                mechanisms[2].Position, 0f, new Vector3(3f, 2.6f, 1.6f)));
            obstacles.Add(new WarehouseArenaObstacle(WarehouseArenaObstacleKind.Mechanism,
                mechanisms[3].Position, 0f, new Vector3(3.4f, 2.2f, 2.6f)));
            var lamps = new[]
            {
                new WarehouseArenaLamp(new Vector3(-7.5f, 2.72f, -4.6f), Vector3.down, 5.5f, .65f),
                new WarehouseArenaLamp(new Vector3(0f, 2.72f, 10.3f), new Vector3(0f,-1f,-.25f), 5f, .58f),
                new WarehouseArenaLamp(new Vector3(-12f, 5.92f, 5f), Vector3.down, 4.8f, .52f),
                new WarehouseArenaLamp(new Vector3(7.5f, 5.92f, 4.4f), Vector3.down, 5.2f, .62f),
                new WarehouseArenaLamp(new Vector3(11f, 9.12f, 9f), new Vector3(1f,-1f,0f), 4.8f, .48f),
                // A rim-mounted maintenance lamp reveals the rotor's underside from the entrance.
                new WarehouseArenaLamp(new Vector3(3.78f, 9.4f, 0f), new Vector3(-1f,.1f,0f), 8f, 4f)
            };
            var rails = new List<WarehouseArenaRailSpan>();
            BuildRails(platforms, stairs, rails, obstacles);
            var ammo = new[]
            {
                new Vector3(-12f,0f,-9f), new Vector3(12f,0f,-9f),
                new Vector3(0f,3.2f,10.5f), new Vector3(12f,6.4f,9f)
            };
            var spawns = new List<Vector3> { new Vector3(0f,0f,-9f), new Vector3(0f,0f,3.4f) };
            spawns.AddRange(rooms);
            for (int level = 0; level <= 2; level++)
            {
                float y = level * WarehouseArenaPlan.LevelRise;
                spawns.Add(new Vector3(-12f,y,4.5f)); spawns.Add(new Vector3(0f,y,7.5f));
                spawns.Add(new Vector3(0f,y,-7.5f));
            }
            var plan = new WarehouseArenaPlan(seed, template, platforms.ToArray(), surfaces.ToArray(), stairs,
                obstacles.ToArray(), modules.ToArray(), rails.ToArray(), lamps, mechanisms, rooms.ToArray(), ammo, spawns.ToArray());
            BuildNavigation(plan);
            WarehouseArenaValidator.ValidateOrThrow(plan);
            return plan;
        }

        private static void AddRoom(Vector3 center, float yaw, int level, List<Vector3> rooms,
            List<WarehouseArenaPlacement> modules, List<WarehouseArenaObstacle> obstacles)
        {
            rooms.Add(center); modules.Add(new WarehouseArenaPlacement("RoomShell", center, yaw));
            // The +X wall has a real 2.4m opening. Right-side rooms rotate it toward the hall.
            AddRoomWall(center, yaw, new Vector3(-2.92f,0f,0f), new Vector3(.16f,3f,6f), obstacles);
            AddRoomWall(center, yaw, new Vector3(0f,0f,-2.92f), new Vector3(6f,3f,.16f), obstacles);
            AddRoomWall(center, yaw, new Vector3(0f,0f,2.92f), new Vector3(6f,3f,.16f), obstacles);
            AddRoomWall(center, yaw, new Vector3(2.92f,0f,-2.1f), new Vector3(.16f,3f,1.8f), obstacles);
            AddRoomWall(center, yaw, new Vector3(2.92f,0f,2.1f), new Vector3(.16f,3f,1.8f), obstacles);
            AddRoomWall(center, yaw, new Vector3(2.92f,2.55f,0f), new Vector3(.16f,.45f,2.4f), obstacles);
            AddRoomWall(center, yaw, new Vector3(0f,2.96f,0f), new Vector3(6f,.12f,6f), obstacles);
            string props = level == 0 ? "TechnicalProps" : level == 1 ? "LockerProps" : "BreakProps";
            // Place furnishings against the wall opposite the +X doorway.
            // Every imported module's front (-Z) faces that entrance.
            Vector3 propPosition = center + RotateRoom(new Vector3(-1.8f,0f,0f),yaw);
            float propYaw = yaw - 90f;
            AddProp(props, WarehouseArenaObstacleKind.Furniture, propPosition, propYaw,
                new Vector3(2.2f,1.9f,1.3f), modules, obstacles);
        }

        private static void AddRoomWall(Vector3 center, float yaw, Vector3 local, Vector3 size,
            List<WarehouseArenaObstacle> obstacles) => obstacles.Add(new WarehouseArenaObstacle(WarehouseArenaObstacleKind.Wall,
                center + RotateRoom(local,yaw), yaw, size));

        private static Vector3 RotateRoom(Vector3 local, float yaw) => yaw == 0f ? local : new Vector3(-local.x,local.y,-local.z);

        private static void AddProp(string model, WarehouseArenaObstacleKind kind, Vector3 position, float yaw,
            Vector3 size, List<WarehouseArenaPlacement> modules, List<WarehouseArenaObstacle> obstacles)
        { modules.Add(new WarehouseArenaPlacement(model, position, yaw)); obstacles.Add(new WarehouseArenaObstacle(kind, position, yaw, size)); }

        private static uint Next(ref uint state)
        { state = unchecked(state * 1664525u + 1013904223u); return state; }
        private static float Phase(ref uint state) => (Next(ref state) & 0xffffu) / 65536f;

        private static void BuildRails(List<Vector3> platforms, WarehouseArenaStair[] stairs,
            List<WarehouseArenaRailSpan> rails, List<WarehouseArenaObstacle> obstacles)
        {
            var occupied = new HashSet<Vector3>(platforms);
            var spans = new List<Vector2>(3);
            foreach (Vector3 tile in platforms)
                foreach (Vector3 side in new[] { Vector3.left, Vector3.right, Vector3.back, Vector3.forward })
                {
                    if (occupied.Contains(tile + side * 3f)) continue;
                    Vector3 edge = tile + side * 1.5f;
                    if (Mathf.Abs(edge.x) >= 14.99f || Mathf.Abs(edge.z) >= 11.99f) continue;
                    bool alongX = Mathf.Abs(side.z) > .5f;
                    float from = alongX ? edge.x - 1.5f : edge.z - 1.5f;
                    spans.Clear(); spans.Add(new Vector2(from, from + 3f));
                    if (alongX)
                        foreach (WarehouseArenaStair stair in stairs)
                            if ((Mathf.Abs(stair.Foot.y - edge.y) < .01f && Mathf.Abs(stair.Foot.z - edge.z) < .01f) ||
                                (Mathf.Abs(stair.Top.y - edge.y) < .01f && Mathf.Abs(stair.Top.z - edge.z) < .01f))
                                CutOpening(spans, stair.Foot.x - 1.25f, stair.Foot.x + 1.25f);
                    foreach (Vector2 span in spans)
                        for (float start = span.x; start < span.y - .001f; start += 1f)
                        {
                            float length = Mathf.Min(1f, span.y - start);
                            Vector3 position = alongX ? new Vector3(start + length * .5f, edge.y, edge.z) :
                                new Vector3(edge.x, edge.y, start + length * .5f);
                            rails.Add(new WarehouseArenaRailSpan(position, alongX ? 0f : 90f, length));
                            obstacles.Add(new WarehouseArenaObstacle(WarehouseArenaObstacleKind.Rail, position,
                                alongX ? 0f : 90f, new Vector3(length, 1.1f, .09f)));
                        }
                }
        }

        private static void CutOpening(List<Vector2> spans, float from, float to)
        {
            for (int i = spans.Count - 1; i >= 0; i--)
            {
                Vector2 span = spans[i];
                if (from >= span.y || to <= span.x) continue;
                spans.RemoveAt(i);
                if (from > span.x) spans.Add(new Vector2(span.x, from));
                if (to < span.y) spans.Add(new Vector2(to, span.y));
            }
        }

        private static void BuildNavigation(WarehouseArenaPlan plan)
        {
            var area = new WarehouseArenaArea(plan);
            var nodes = new List<Vector3>(800);
            var unique = new HashSet<Vector3>();
            for (int level = 0; level <= 2; level++)
                for (float z = -10.5f; z <= 10.51f; z += 1.5f)
                    for (float x = -13.5f; x <= 13.51f; x += 1.5f)
                    {
                        Vector3 position = new Vector3(x, level * WarehouseArenaPlan.LevelRise, z);
                        if (area.TryGetHeight(position, out float height, .4f) && Mathf.Abs(height - position.y) < .01f)
                            AddNode(position, nodes, unique);
                    }
            foreach (WarehouseArenaStair stair in plan.Stairs)
            {
                AddNode(stair.Foot + Vector3.back * .6f, nodes, unique);
                for (int step = 0; step < WarehouseArenaStair.TreadCount; step++)
                    AddNode(stair.Foot + new Vector3(0f, (step + 1) * WarehouseArenaStair.TreadRise,
                        (step + .5f) * WarehouseArenaStair.TreadDepth), nodes, unique);
                AddNode(stair.Top + Vector3.forward * .6f, nodes, unique);
            }
            foreach (Vector3 spawn in plan.SpawnPoints) AddNode(spawn, nodes, unique);
            foreach (Vector3 ammo in plan.AmmoAnchors) AddNode(ammo, nodes, unique);
            AddNode(plan.GateApproach, nodes, unique);
            // Only spatial neighbours can form a short valid edge; avoid an all-pairs graph build.
            const float cellSize = 2.11f;
            var buckets = new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < nodes.Count; i++)
            {
                Vector3Int key = Cell(nodes[i], cellSize);
                if (!buckets.TryGetValue(key, out List<int> bucket)) { bucket = new List<int>(8); buckets.Add(key, bucket); }
                bucket.Add(i);
            }
            var edges = new List<WarehouseArenaEdge>(nodes.Count * 4);
            for (int i = 0; i < nodes.Count; i++)
            {
                Vector3Int key = Cell(nodes[i], cellSize);
                for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++)
                {
                    if (!buckets.TryGetValue(key + new Vector3Int(x,y,z), out List<int> bucket)) continue;
                    foreach (int j in bucket)
                        if (j > i && (nodes[i] - nodes[j]).sqrMagnitude <= cellSize * cellSize &&
                            area.CanTraverse(nodes[i], nodes[j], .36f)) edges.Add(new WarehouseArenaEdge(i,j));
                }
            }
            plan.SetNavigation(nodes.ToArray(), edges.ToArray());
        }

        private static Vector3Int Cell(Vector3 point, float size) => new Vector3Int(Mathf.FloorToInt(point.x / size),
            Mathf.FloorToInt(point.y / size), Mathf.FloorToInt(point.z / size));
        private static void AddNode(Vector3 point, List<Vector3> nodes, HashSet<Vector3> unique)
        { if (unique.Add(point)) nodes.Add(point); }
    }

    public static class WarehouseArenaValidator
    {
        public static void ValidateOrThrow(WarehouseArenaPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (plan.Stairs.Count != 4 || plan.RoomCenters.Count != 9 || plan.Platforms.Count < 100 ||
                plan.AmmoAnchors.Count != 4 || plan.NavigationNodes.Count == 0 || plan.Lamps.Count != 6)
                throw new InvalidOperationException("Warehouse needs three storeys, nine rooms, four stairs, four ammo anchors and sparse light.");
            var area = new WarehouseArenaArea(plan);
            foreach (Vector3 spawn in plan.SpawnPoints)
                if (!area.TryGetHeight(spawn, out float height, .5f) || Mathf.Abs(height - spawn.y) > .02f)
                    throw new InvalidOperationException($"Warehouse seed {plan.Seed} ({plan.Template}) spawn {spawn} has no capsule clearance.");
            for (int i = 0; i < plan.Obstacles.Count; i++)
                for (int j = i + 1; j < plan.Obstacles.Count; j++)
                {
                    WarehouseArenaObstacle a = plan.Obstacles[i], b = plan.Obstacles[j];
                    if ((a.Kind != WarehouseArenaObstacleKind.Rack && a.Kind != WarehouseArenaObstacleKind.Cargo) ||
                        (b.Kind != WarehouseArenaObstacleKind.Rack && b.Kind != WarehouseArenaObstacleKind.Cargo)) continue;
                    if (a.Bounds.Intersects(b.Bounds)) throw new InvalidOperationException("Warehouse cargo and rack footprints intersect.");
                }
            var route = new List<Vector3>();
            foreach (Vector3 spawn in plan.SpawnPoints) RequireRoute(area, plan.HeroSpawn, spawn, route);
            foreach (Vector3 ammo in plan.AmmoAnchors) RequireRoute(area, plan.HeroSpawn, ammo, route);
            RequireRoute(area, plan.HeroSpawn, plan.GateApproach, route);
            foreach (WarehouseArenaStair stair in plan.Stairs)
                RequireRoute(area, stair.Foot + Vector3.back * .6f, stair.Top + Vector3.forward * .6f, route);
            if (area.CanTraverse(plan.GateApproach, new Vector3(0f,0f,11.9f), .36f))
                throw new InvalidOperationException("The future goal gate must remain closed and solid.");
        }

        private static void RequireRoute(WarehouseArenaArea area, Vector3 from, Vector3 to, List<Vector3> route)
        {
            if (!area.FindRoute(from, to, route))
                throw new InvalidOperationException($"Warehouse seed {area.Plan.Seed} ({area.Plan.Template}) route {from} to {to} is disconnected.");
        }
    }
}
