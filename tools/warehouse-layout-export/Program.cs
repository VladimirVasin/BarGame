using System.Text;
using System.Text.Json;
using BarPromenade;
using UnityEngine;

if (args.Length != 2) throw new ArgumentException("Expected projection metadata and output paths.");
using JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(args[0]));
float[] Position(Vector3 value) => new[] { value.x, value.y, value.z };
var plans = new[] { 0, 1, 3 }.Select(WarehouseArenaPlanner.Create).ToArray();
if (plans.Select(plan => plan.Template).Distinct().Count() != 3)
    throw new InvalidOperationException("Export seeds must cover all three warehouse templates.");
var manifest = new
{
    schema_version = 1,
    authority = metadata.RootElement.Clone(),
    generator_version = WarehouseArenaPlan.GeneratorVersion,
    coordinates = "Unity metres; position [x,y,z], yaw degrees around +Y",
    templates = plans.Select(plan => new
    {
        seed = plan.Seed,
        template = plan.Template.ToString(),
        modules = plan.ModulePlacements.Select((part, slot) => new
        {
            slot,
            kind = part.Kind,
            position = Position(part.Position),
            yaw = part.Yaw,
        }),
        stairs = plan.Stairs.Select((stair, slot) => new
        {
            slot,
            foot = Position(stair.Foot),
            top = Position(stair.Top),
            width = WarehouseArenaStair.Width,
            run = WarehouseArenaStair.Run,
            tread_count = WarehouseArenaStair.TreadCount,
            tread_depth = WarehouseArenaStair.TreadDepth,
            tread_rise = WarehouseArenaStair.TreadRise,
            // The base Stair slot stays reserved for core painting; runtime
            // substitutes the separately authored broken-flight model.
            broken = stair.IsBroken,
        }),
        rails = plan.RailSpans.Select((rail, slot) => new
        {
            slot,
            position = Position(rail.Position),
            yaw = rail.Yaw,
            length = rail.Length,
        }),
        mechanisms = plan.Mechanisms.Select((part, slot) => new
        {
            slot,
            kind = part.Kind,
            position = Position(part.Position),
            yaw = part.Yaw,
            phase = part.Phase,
        }),
        lamps = plan.Lamps.Select((part, slot) => new
        {
            slot,
            kind = part.Kind,
            position = Position(part.Position),
            enabled = part.Enabled,
        }),
        room_centers = plan.RoomCenters.Select(Position),
        refrigerators = plan.Refrigerators.Select(part => new
        {
            floor = part.Floor,
            room_index = part.RoomIndex,
            position = Position(part.Position),
            yaw = part.Yaw,
        }),
        fish_crates = plan.FishCrateAnchors.Select(part => new
        {
            id = part.Id,
            position = Position(part.Position),
            yaw = part.Yaw,
        }),
    }),
};
string output = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true })
    .Replace("\r\n", "\n") + "\n";
Directory.CreateDirectory(Path.GetDirectoryName(args[1])!);
File.WriteAllText(args[1], output, new UTF8Encoding(false));
Console.WriteLine("Exported placement-only warehouse manifest: " + args[1]);
foreach (WarehouseArenaPlan plan in plans)
    Console.WriteLine($"  seed {plan.Seed}: {plan.Template}, {plan.ModulePlacements.Count} modules, {plan.Stairs.Count} stairs, {plan.RailSpans.Count} rails, {plan.Lamps.Count} lamps");
