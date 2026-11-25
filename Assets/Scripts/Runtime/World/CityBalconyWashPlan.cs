using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    internal enum WashMount { Rail, Wall, PowerPole, Lamp }

    internal sealed class CityBalconyWashLine
    {
        internal const float DefaultClothTravel = .40f;
        internal const float ExposedClothTravel = .62f;
        internal readonly Vector3 Start, End, StartAxis, EndNormal;
        internal readonly WashMount Mount;
        internal readonly string Id;
        internal Vector3 Center => (Start + End) * .5f;
        internal float Length => Vector3.Distance(Start, End);
        internal float Sag => .04f * Length;
        internal bool ToPole => Mount == WashMount.PowerPole || Mount == WashMount.Lamp;
        internal float ClothTravel { get; private set; } = DefaultClothTravel;

        internal void AllowExposedTravel() => ClothTravel = ExposedClothTravel;

        internal CityBalconyWashLine(string id, Vector3 start, Vector3 end, Vector3 startAxis, WashMount mount, Vector3 normal)
        {
            Id = id;
            Start = start;
            End = end;
            StartAxis = startAxis;
            Mount = mount;
            EndNormal = normal;
        }

        internal Vector3 Point(float t) => CityRopeSpanGeometry.SamplePoint(Start, End, Sag, t);

        // Layout is measured along the horizontal projection. Garments remain at their
        // authored width even on a tilted rope, including the shirt's loose sleeves.
        private float Run => Vector3.ProjectOnPlane(End - Start, Vector3.up).magnitude;
        internal int Count => Run < 4.15f ? 3 : Run < 5.25f ? 4 : Run < 6.30f ? 5 : 6;

        internal string Garment(int index)
        {
            if (index == 0) return Count == 3 ? "Towel" : "Sheet";
            if (index == 1) return ToPole ? "Towel" : "Shirt";
            if (index == 2) return ToPole ? "Shirt" : "Towel";
            return index == 4 ? "Shirt" : "Towel";
        }

        private static float Width(string name) => name == "Sheet" ? 1.28f : name == "Shirt" ? .80f : .56f;

        internal void Pegs(int index, out Vector3 a, out Vector3 b)
        {
            int count = Count;
            float occupied = 0f, before = 0f;
            for (int i = 0; i < count; i++)
            {
                float item = Width(Garment(i));
                occupied += item;
                if (i < index) before += item;
            }
            // Two margins and the gaps receive equal shares of the spare length.
            float gap = (Run - occupied) / (count + 1);
            string name = Garment(index);
            float t = (gap * (index + 1) + before + Width(name) * .5f) / Run;
            float pins = name == "Shirt" ? .48f : Width(name);
            // The secant slope of symmetrical points on a parabola is its centre tangent.
            float delta = pins * .5f / (End - Start - Vector3.up * (4f * Sag * (1f - 2f * t))).magnitude;
            a = Point(t - delta);
            b = Point(t + delta);
        }

        internal CityWindDressingClothDescriptor Cloth(int index)
        {
            Pegs(index, out var a, out var b);
            string name = Garment(index);
            Color tint = name == "Sheet" ? new Color(.70f, .67f, .59f) : name == "Shirt" ? new Color(.49f, .54f, .62f)
                : index % 2 == 0 ? new Color(.62f, .52f, .44f) : new Color(.48f, .56f, .55f);
            return new CityWindDressingClothDescriptor($"{Id}-{index}-{name}",
                CityWindDressingKind.BalconyWash, CityWindDressingZone.Residential,
                (a + b) * .5f, Mathf.Atan2(a.z - b.z, b.x - a.x) * Mathf.Rad2Deg,
                Width(name), name == "Sheet" ? .89f : name == "Shirt" ? .67f : .70f,
                tint, 0, name == "Towel" ? 6 : 10, 8, false);
        }
    }

    internal static class CityBalconyWashPlan
    {
        internal const int MaximumLines = 16;
        internal const int MaximumGarments = 80;
        internal const float MaximumLength = 8f;
        internal const float Spacing = 12f;
        private static readonly ConditionalWeakTable<CityLayout, List<CityBalconyWashLine>> Cached = new();
        internal static IReadOnlyList<CityBalconyWashLine> ForLayout(CityLayout layout) =>
            Cached.GetValue(layout, Select);

        private readonly struct Residence
        {
            internal readonly BuildingLot Lot;
            internal readonly CityBuildingAssetRegistry Model;
            internal readonly CityBuildingPrototypePose Pose;
            internal Residence(BuildingLot lot, CityBuildingAssetRegistry model)
            {
                Lot = lot; Model = model; Pose = CityBuildingPrototypePlacement.ResolveCityPose(lot, model);
            }
        }

        private readonly struct Rail
        {
            internal readonly BuildingLot Lot;
            internal readonly Vector3 Point, Outward, Axis;
            internal readonly string Id;
            internal Rail(Residence house, CityBuildingBalconySlot deck, int side)
            {
                Lot = house.Lot;
                Bounds b = deck.LocalDeckBounds;
                Point = house.Pose.TransformPoint(new Vector3(b.center.x + side * (b.extents.x - .04f),
                    b.max.y + (side == 0 ? .96f : .89f), b.max.z - .04f));
                Outward = house.Pose.Rotation * (Vector3.right * side);
                Axis = side == 0 ? house.Pose.Rotation * Vector3.right : Vector3.up;
                Id = $"wash-{Lot.Cell.x}-{Lot.Cell.y}-{deck.StableId}-{side}";
            }
            internal bool CanLeave(Vector3 towards) =>
                Vector3.Dot(towards, Lot.FacadeForward) > .12f || Vector3.Dot(towards, Outward) > .45f;
        }

        private static List<CityBalconyWashLine> Select(CityLayout layout)
        {
            var houses = new List<Residence>();
            var rails = new List<Rail>();
            var reserved = new List<Vector3>();
            var smokerSlots = new HashSet<(Vector2Int, string)>();
            foreach (var smoker in CityBalconySmokerPlan.CreateCandidates(layout))
                smokerSlots.Add((smoker.LotCell, smoker.BalconySlotStableId));
            var provider = CityBuildingAssetProvider.LoadOrThrow();
            var models = new Dictionary<(CityDistrictKind, int), CityBuildingAssetRegistry>();
            void AddHouse(BuildingLot lot, bool airer)
            {
                if (!lot.IsOrdinaryBuilding) return;
                var key = (lot.District, lot.BuildingVariant);
                if (!models.TryGetValue(key, out var model))
                    models.Add(key, model = provider.GetPrefabOrThrow(lot.District,
                        lot.BuildingVariant).GetComponent<CityBuildingAssetRegistry>());
                var house = new Residence(lot, model);
                houses.Add(house);
                if (lot.District != CityDistrictKind.Residential) return;
                foreach (var deck in model.BalconySlots)
                {
                    if (deck.Floor > 3 || smokerSlots.Contains((lot.Cell, deck.StableId)) ||
                        airer && deck.Floor == 1 && deck.LocalNpcDock.x > 0f) continue;
                    rails.Add(new Rail(house, deck, -1));
                    rails.Add(new Rail(house, deck, 1));
                    rails.Add(new Rail(house, deck, 0));
                }
            }
            foreach (var lot in layout.BuildingMasses) AddHouse(lot, false);
            foreach (var house in CityHillsideNeighborhoodPlan.ForLayout(layout).AllBuildings)
            {
                AddHouse(house.Lot, house.HasAirer);
                if (house.HasAirer) reserved.Add(house.PegWorld);
            }
            var selected = new List<CityBalconyWashLine>(MaximumLines);
            if (rails.Count == 0) return selected;
            var districts = new CityDistrictStreamingPlan(layout);
            var infrastructure = CityCoastalInfrastructurePlan.ForLayout(layout);
            var lamps = new List<StreetLampDescriptor>(CityLayoutCache.GetOrCreateNightPlan(layout).StreetLamps);
            CityNightWorldBuilder.AppendHillsideLamps(layout, lamps);
            var candidates = new List<CityBalconyWashLine>();
            foreach (var rail in rails)
            {
                for (int i = 0; i < infrastructure.Poles.Count; i++)
                    Pole(infrastructure.Poles[i], WashMount.PowerPole, .146f, 6.7f, i);
                for (int i = 0; i < lamps.Count; i++)
                    Pole(lamps[i].Position, WashMount.Lamp, .061f, 4.48f, i);
                foreach (var other in rails)
                    if (other.Lot != rail.Lot && other.CanLeave(rail.Point - other.Point))
                        Offer(other.Point, WashMount.Rail, other.Axis, other.Id);
                foreach (var house in houses)
                {
                    if (house.Lot == rail.Lot || (rail.Point - house.Lot.Center).sqrMagnitude > 25f * 25f) continue;
                    Vector3 local = Quaternion.Inverse(house.Pose.Rotation) * (rail.Point - house.Pose.Position);
                    for (int axis = 0; axis <= 2; axis += 2)
                    {
                        int across = 2 - axis;
                        float side = Mathf.Sign(local[axis]);
                        float outer = side * (axis == 0 ? house.Model.FrontageWidth : house.Model.Depth) * .5f;
                        if (axis == 2 && side > 0f && house.Lot.District == CityDistrictKind.Residential) outer -= 1.2f;
                        if (side * (local[axis] - outer) < .2f) continue;
                        int solidIndex = 0;
                        foreach (var solid in house.Model.ColliderBounds)
                        {
                            solidIndex++;
                            float wallPlane = (side > 0f ? solid.max : solid.min)[axis];
                            if (Mathf.Abs(wallPlane - outer) > .06f || local.y < solid.min.y + .15f ||
                                local.y > solid.max.y - .15f || solid.size[across] < .55f) continue;
                            Vector3 wall = local;
                            wall[axis] = wallPlane;
                            wall[across] = Mathf.Clamp(local[across], solid.min[across] + .25f, solid.max[across] - .25f);
                            bool openingInWay = false;
                            foreach (var opening in house.Model.WindowSlots)
                            {
                                Vector3 offset = opening.LocalCenter - wall;
                                if (Mathf.Abs(offset[axis]) >= .20f || Mathf.Abs(offset.y) >= opening.SizeMeters.y * .5f + .18f ||
                                    Mathf.Abs(offset[across]) >= opening.SizeMeters.x * .5f + .18f) continue;
                                openingInWay = true;
                                break;
                            }
                            if (openingInWay) continue;
                            Vector3 normal = house.Pose.Rotation * (axis == 0 ? Vector3.right : Vector3.forward) * side;
                            Offer(house.Pose.TransformPoint(wall) + normal * .052f, WashMount.Wall, normal,
                                $"{house.Lot.Cell}-{axis}-{solidIndex}");
                        }
                    }
                }
                void Pole(Vector3 foot, WashMount type, float radius, float highest, int i)
                {
                    Vector3 horizontal = Vector3.ProjectOnPlane(rail.Point - foot, Vector3.up);
                    if (horizontal.sqrMagnitude > (MaximumLength + .15f) * (MaximumLength + .15f)) return;
                    var end = foot + Vector3.up * Mathf.Clamp(rail.Point.y - foot.y, 3f, highest);
                    if (type == WashMount.Lamp)
                        radius = Mathf.Lerp(.075f, .055f, Mathf.InverseLerp(.16f, 4.73f, end.y - foot.y));
                    Offer(end + horizontal.normalized * radius, type, horizontal.normalized * radius, i.ToString());
                }
                void Offer(Vector3 end, WashMount type, Vector3 normal, string receiver)
                {
                    Vector3 delta = end - rail.Point;
                    if (delta.sqrMagnitude < 9f || delta.sqrMagnitude > MaximumLength * MaximumLength ||
                        Mathf.Abs(delta.y) > 1f || !rail.CanLeave(delta)) return;
                    if (districts.IsRidge(rail.Point) != districts.IsRidge(end)) return;
                    candidates.Add(new CityBalconyWashLine(rail.Id + "-" + type + "-" + receiver,
                        rail.Point, end, rail.Axis, type, normal));
                }
            }

            candidates.Sort((a, b) =>
            {
                int score = Mathf.Abs(a.Length - 4.5f).CompareTo(Mathf.Abs(b.Length - 4.5f));
                return score != 0 ? score : string.CompareOrdinal(a.Id, b.Id);
            });
            var geometry = new CityElectricalClearanceGeometry(layout);
            var street = CityStreetSupportPlan.ForLayout(layout);
            var nearby = new List<CityStreetSupportPatch>();
            Rect? yard = null;
            foreach (var poi in layout.DistrictPointsOfInterest)
                if (poi.Kind == CityDistrictPointOfInterestKind.ResidentialDryingYard) yard = poi.PublicBounds;
            // Interleave both kinds of receiver in both streamed districts. Within each
            // group, prefer a typical span and leave space between neighbouring lines.
            var groups = new List<CityBalconyWashLine>[4];
            for (int i = 0; i < groups.Length; i++) groups[i] = new List<CityBalconyWashLine>();
            foreach (var candidate in candidates)
                groups[(districts.IsRidge(candidate.Start) ? 2 : 0) + (candidate.ToPole ? 0 : 1)].Add(candidate);

            var next = new int[groups.Length];
            int remaining = MaximumGarments;
            bool added;
            do
            {
                added = false;
                for (int group = 0; group < groups.Length && selected.Count < MaximumLines; group++)
                {
                    while (next[group] < groups[group].Count)
                    {
                        var candidate = groups[group][next[group]++];
                        if (candidate.Count > remaining ||
                            reserved.Exists(p => HorizontalDistance(p, candidate.Center) < Spacing) ||
                            !Clear(layout, candidate, geometry, street, nearby, infrastructure, yard)) continue;
                        if (Clear(layout, candidate, geometry, street, nearby, infrastructure, yard, CityBalconyWashLine.ExposedClothTravel + .02f))
                            candidate.AllowExposedTravel();
                        selected.Add(candidate);
                        reserved.Add(candidate.Center);
                        remaining -= candidate.Count;
                        added = true;
                        break;
                    }
                }
            } while (added && selected.Count < MaximumLines && remaining >= 3);
            return selected;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b) =>
            Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        private static bool Clear(CityLayout layout, CityBalconyWashLine line, CityElectricalClearanceGeometry geometry,
            CityStreetSupportPlan street, List<CityStreetSupportPatch> nearby, CityCoastalInfrastructurePlan infrastructure,
            Rect? yard, float clothSweep = CityBalconyWashLine.DefaultClothTravel + .02f)
        {
            // Exclude only the tied ends from the solid test. Keep room for the hems
            // and their lateral displacement along the occupied middle of the rope.
            if (!geometry.Clear(new CityCoastalCableSpan(line.Point(.015f), line.Point(.985f), .94f * line.Sag, false))) return false;
            line.Pegs(0, out var first, out _);
            line.Pegs(line.Count - 1, out _, out var last);
            Vector3 run = Vector3.ProjectOnPlane(line.End - line.Start, Vector3.up);
            float tMin = Mathf.Max(0f, (Vector3.Dot(first - line.Start, run) / run.sqrMagnitude) - .20f / run.magnitude);
            float tMax = Mathf.Min(1f, (Vector3.Dot(last - line.Start, run) / run.sqrMagnitude) + .20f / run.magnitude);
            Vector3 lateral = Vector3.Cross(Vector3.up, run).normalized;
            for (int level = 0; level <= 3; level++)
                for (int side = -1; side <= 1; side++)
                {
                    Vector3 offset = Vector3.down * (level * .35f) + lateral * (side * clothSweep);
                    if (!geometry.Clear(new CityCoastalCableSpan(line.Point(tMin) + offset, line.Point(tMax) + offset,
                        (tMax - tMin) * (tMax - tMin) * line.Sag, false))) return false;
                }
            var mid = line.Center;
            street.GetNearby(new Rect(mid.x - 5f, mid.z - 5f, 10f, 10f), nearby);
            for (int i = 0; i <= 20; i++)
            {
                float t = i / 20f;
                Vector3 point = line.Point(t);
                float drop = t >= tMin && t <= tMax ? 1.1f : .02f;
                for (int side = -1; side <= 1; side++)
                {
                    Vector3 p = point + lateral * (side * (drop > 1f ? clothSweep : 0f));
                    Vector2 xz = new(p.x, p.z);
                    if (yard is Rect r && Vector2.Distance(xz, Vector2.Max(r.min, Vector2.Min(r.max, xz))) <
                        CityWindDressingPlanner.DryingYardClearance) return false;
                    bool earth = CityTerrainSurfacePlan.TrySampleGroundTop(layout, xz, out float ground, out _);
                    if (CityHillsideGroundPlan.ForLayout(layout).TrySampleTop(xz, out float ridge))
                    { ground = earth ? Mathf.Max(ridge, ground) : ridge; earth = true; }
                    foreach (var patch in nearby)
                        if (patch.TrySampleTop(xz, out float top))
                        {
                            if (patch.Kind == CityStreetSupportKind.Asphalt) return false;
                            ground = earth ? Mathf.Max(ground, top) : top; earth = true;
                        }
                    if (!earth || p.y - drop - ground < 2.5f) return false;
                }
            }
            foreach (var cable in infrastructure.CableSpans)
            {
                // Conservative swept bounds include the free cable's maximum wind travel.
                Bounds corridor = new(line.Start, Vector3.zero);
                corridor.Encapsulate(line.End);
                corridor.Encapsulate(line.Point(.5f) - Vector3.up * 1.1f);
                corridor.Expand(2f * (clothSweep + .08f + cable.WindSwing));
                Vector3 a = cable.Start;
                for (int i = 1; i <= 16; i++)
                {
                    Vector3 b = cable.Sample(i / 16f);
                    if (corridor.Contains(a) || corridor.IntersectRay(new Ray(a, b - a), out float hit) && hit <= Vector3.Distance(a, b))
                        return false;
                    a = b;
                }
            }
            return true;
        }
    }
}
