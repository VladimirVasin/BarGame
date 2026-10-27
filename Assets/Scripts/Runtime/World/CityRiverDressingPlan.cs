using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct CityRiverDressingPlacement
    {
        internal CityRiverDressingPlacement(string id, CityMiscKind kind,
            Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Id = id;
            Kind = kind;
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        public string Id { get; }
        public CityMiscKind Kind { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public Vector3 Scale { get; }
    }

    /// <summary>
    /// Finite imported construction details, derived from the river's actual
    /// spans, openings, treads and fixture frames. Nothing occupies the free
    /// promenade lane or creates another route, actor or lighting owner.
    /// </summary>
    public sealed class CityRiverDressingPlan
    {
        public const int MaximumPlacementCount = 2048;
        public const float CopingJointMeters = 0.025f;
        public const float SkinClearanceMeters = 0.012f;
        public const float TreadClearanceMeters = 0.004f;
        public const float BollardWidthMeters = 0.44f;
        public const float BollardHeightMeters = 0.52f;
        private const float RailThickness = 0.14f;
        private const float FootplateWidth = 0.38f;
        private const float CopingTopAboveBank = 0.08f + SkinClearanceMeters;

        private CityRiverDressingPlan(List<CityRiverDressingPlacement> placements)
            => Placements = new ReadOnlyCollection<CityRiverDressingPlacement>(placements);

        public IReadOnlyList<CityRiverDressingPlacement> Placements { get; }

        public static CityRiverDressingPlan Create(CityLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            var placements = new List<CityRiverDressingPlacement>(768);
            if (!layout.River.IsEnabled) return new CityRiverDressingPlan(placements);
            foreach (CityRiverPromenadeDescriptor promenade in layout.River.Promenades)
                AddBank(layout, promenade, placements);
            foreach (CityRiverBridgeDescriptor bridge in layout.River.Bridges)
                AddBridge(layout.River, bridge, placements);
            foreach (CityRiverLandingDescriptor landing in layout.River.Landings)
                AddLanding(landing, placements);
            AddLanterns(layout, placements);
            return new CityRiverDressingPlan(placements);
        }

        private static void AddBank(CityLayout layout, CityRiverPromenadeDescriptor promenade,
            List<CityRiverDressingPlacement> target)
        {
            CityRiverPlan river = layout.River;
            List<Range> spans = QuaySpans(river, promenade);
            float waterX = promenade.WestBank ? river.Segments[0].WaterBounds.xMin
                : river.Segments[0].WaterBounds.xMax;
            float inward = promenade.WestBank ? -1f : 1f;
            float railX = waterX + inward * CityRiverPlanner.QuayEdgeOffset;
            float lip = promenade.WestBank ? waterX - promenade.Bounds.xMax
                : promenade.Bounds.xMin - waterX;
            // The production river has a one-metre structural lip outside
            // its three-metre logical walkway. Fit the whole plate on that
            // lip, rather than putting its centre on the edge of a narrow cap.
            float landwardEdge = Mathf.Min(0.925f, lip - CopingJointMeters);
            float width = landwardEdge + 0.075f;
            float bevelInset = width / 0.56f * 0.013f;
            if (landwardEdge - bevelInset < CityRiverPlanner.QuayEdgeOffset +
                FootplateWidth * 0.5f + 0.005f) return;
            float wallX = waterX + inward * (landwardEdge - 0.075f) * 0.5f;
            float widthScale = width / 0.56f;
            float grade = (promenade.NorthY - promenade.SouthY) / promenade.Bounds.height;
            Quaternion gradeRotation = Quaternion.LookRotation(new Vector3(0f, grade, 1f), Vector3.up);
            for (int index = 0; index < spans.Count; index++)
            {
                Range span = spans[index];
                int postCount = Mathf.Max(1, Mathf.CeilToInt((span.Max - span.Min) / 2.8f));
                AddBankCoping(target, promenade, span, index, postCount, wallX, widthScale);
                // End posts meet bridge/landing copings at different heights.
                // Their existing structural supports stay; optional sole plates
                // belong only to the uninterrupted, consistently graded run.
                for (int post = 1; post < postCount; post++)
                {
                    float z = Mathf.Lerp(span.Min, span.Max, post / (float)postCount);
                    // The stone's authored top is -2 mm; the plate clears it
                    // by +2 mm. Both use the same inclined support plane.
                    Add(target, $"{promenade.Id}-rail-foot-{index}-{post}",
                        CityMiscKind.RiverWorksFitting,
                        new Vector3(railX, Sample(promenade, z) + CopingTopAboveBank, z),
                        gradeRotation);
                }
            }
        }

        private static void AddBankCoping(List<CityRiverDressingPlacement> target,
            CityRiverPromenadeDescriptor promenade, Range span, int spanIndex,
            int postCount, float x, float widthScale)
        {
            string id = $"{promenade.Id}-coping-{spanIndex}";
            if (span.Max - span.Min < 0.70f)
            {
                AddCoping(target, id, Point(span.Min - 0.18f), Point(span.Max + 0.18f), widthScale);
                return;
            }
            float previousEnd = 0f;
            for (int post = 0; post <= postCount; post++)
            {
                float z = Mathf.Lerp(span.Min, span.Max, post / (float)postCount);
                float start = z - (post == 0 ? 0.18f : 0.30f);
                float end = z + (post == postCount ? 0.18f : 0.30f);
                if (post > 0)
                    AddCoping(target, id + "-infill-" + post,
                        Point(previousEnd + CopingJointMeters * 0.5f),
                        Point(start - CopingJointMeters * 0.5f), widthScale);
                // A coping assembly is centred under each plate, keeping
                // its contact samples away from the infill's 25 mm joints.
                Vector3 from = Point(start), to = Point(end);
                Vector3 delta = to - from;
                Add(target, id + "-support-" + post, CityMiscKind.RiverQuayRepair,
                    (from + to) * 0.5f, Quaternion.LookRotation(delta.normalized, Vector3.up),
                    new Vector3(widthScale, 1f, delta.magnitude / 1.20f));
                previousEnd = end;
            }

            Vector3 Point(float z) => new Vector3(x, Sample(promenade, z) + CopingTopAboveBank, z);
        }

        private static void AddBridge(CityRiverPlan river, CityRiverBridgeDescriptor bridge,
            List<CityRiverDressingPlacement> target)
        {
            float minimum = bridge.SpanBounds.xMin + RailThickness * 0.5f;
            float maximum = bridge.SpanBounds.xMax - RailThickness * 0.5f;
            float deckY = bridge.AverageY + CityStreetSurfacePlanner.RoadTop;
            for (int side = -1; side <= 1; side += 2)
            {
                bool timber = bridge.Definition.Role == CityBridgeRole.ParkFootbridge;
                float z = timber ? bridge.SpanBounds.center.y + side *
                    (bridge.SpanBounds.height * 0.5f - 0.10f)
                    : side < 0 ? bridge.SpanBounds.yMin : bridge.SpanBounds.yMax;
                var spans = new List<Range> { new Range(minimum, maximum) };
                if (!timber && side == bridge.Definition.InteriorDirection.y)
                    foreach (CityRiverLandingDescriptor landing in river.Landings)
                        if (string.Equals(landing.BridgeId, bridge.Definition.Id, StringComparison.Ordinal))
                            Subtract(spans, landing.StairBounds.xMin - 0.12f,
                                landing.StairBounds.xMax + 0.12f);
                for (int index = 0; index < spans.Count; index++)
                {
                    Range span = spans[index];
                    string id = $"{bridge.Definition.Id}-{side}-{index}";
                    if (bridge.Definition.Style == CityBridgeStyle.Mouth)
                    {
                        AddMouthCaps(target, id,
                            new Vector3(span.Min, bridge.AverageY + 1.105f + SkinClearanceMeters, z),
                            span.Max - span.Min);
                        continue;
                    }
                    // A Works post sits on the deck edge; a 380 mm sole
                    // would overhang it. Keep its supported girder fittings
                    // below instead of adding a cosmetic floating footplate.
                    if (!timber) continue;
                    int posts = Mathf.Max(1, Mathf.CeilToInt((span.Max - span.Min) / 2.1f));
                    for (int post = 0; post <= posts; post++)
                    {
                        float x = Mathf.Lerp(span.Min, span.Max, post / (float)posts);
                        Add(target, id + "-foot-" + post,
                            CityMiscKind.RiverTimberJoint,
                            new Vector3(x, bridge.AverageY + 0.002f, z));
                    }
                }
                if (bridge.Definition.Style == CityBridgeStyle.Works)
                    for (int pier = -1; pier <= 1; pier += 2)
                        Add(target, $"{bridge.Definition.Id}-bearing-{side}-{pier}",
                            CityMiscKind.RiverWorksFitting,
                            new Vector3(bridge.SpanBounds.center.x + pier * bridge.SpanBounds.width * 0.30f,
                                deckY - 0.68f, z - side * 0.048f),
                            Quaternion.Euler(side * 90f, 0f, 0f));
            }
        }

        private static void AddLanding(CityRiverLandingDescriptor landing,
            List<CityRiverDressingPlacement> target)
        {
            float direction = Mathf.Sign(landing.DescentDirection.z);
            float upperEdge = landing.StairBounds.center.y - direction * landing.StairBounds.height * 0.5f;
            float tread = landing.StairBounds.height / landing.StepCount;
            Quaternion rotation = Quaternion.Euler(0f, direction > 0f ? 0f : 180f, 0f);
            for (int step = 0; step < landing.StepCount; step++)
            {
                float y = Mathf.Lerp(landing.UpperY, landing.LowerY, (step + 1f) / landing.StepCount);
                // The existing tread extends 12.5 mm past its nominal end;
                // the front of the imported 80 mm nose meets that same edge.
                float z = upperEdge + direction * ((step + 1f) * tread + 0.0125f - 0.04f);
                Add(target, $"{landing.Id}-tread-{step}", CityMiscKind.RiverStairNosing,
                    new Vector3(landing.StairBounds.center.x, y + TreadClearanceMeters, z),
                    rotation, new Vector3(landing.StairBounds.width, 1f, 1f));
            }
            float landwardSign = landing.WestBank ? -1f : 1f;
            float landwardX = landing.WestBank ? landing.PlatformBounds.xMin : landing.PlatformBounds.xMax;
            float waterX = landing.WestBank ? landing.PlatformBounds.xMax : landing.PlatformBounds.xMin;
            float terminal = landing.PlatformBounds.center.y + direction * landing.PlatformBounds.height * 0.5f;
            float start = Mathf.Min(landing.StairBounds.yMin, landing.PlatformBounds.yMin);
            float end = Mathf.Max(landing.StairBounds.yMax, landing.PlatformBounds.yMax);
            // The cut's actual landward retaining wall reaches the upper
            // street, so its cap follows that upper datum beside the stairs.
            AddCoping(target, landing.Id + "-cut-cap",
                new Vector3(landwardX + landwardSign * 0.12f, landing.UpperY + SkinClearanceMeters, start),
                new Vector3(landwardX + landwardSign * 0.12f, landing.UpperY + SkinClearanceMeters, end), 0.5f);
            AddCoping(target, landing.Id + "-terminal-cap",
                new Vector3(landing.PlatformBounds.xMin, landing.UpperY + SkinClearanceMeters,
                    terminal + direction * 0.12f),
                new Vector3(landing.PlatformBounds.xMax, landing.UpperY + SkinClearanceMeters,
                    terminal + direction * 0.12f), 0.5f);
            AddCoping(target, landing.Id + "-lower-water-cap",
                new Vector3(waterX + landwardSign * 0.205f, landing.LowerY + TreadClearanceMeters,
                    landing.PlatformBounds.yMin),
                new Vector3(waterX + landwardSign * 0.205f, landing.LowerY + TreadClearanceMeters,
                    landing.PlatformBounds.yMax), 1f);
            for (int side = -1; side <= 1; side += 2)
            {
                // Existing stair posts straddle the flight/platform edge or
                // its retaining wall. They keep their original continuous
                // rail; optional wide sole plates have no full support here.
                Add(target, $"{landing.Id}-bollard-{side}", CityMiscKind.RiverMooringBollard,
                    new Vector3(landing.PlatformBounds.center.x, landing.LowerY,
                        landing.PlatformBounds.center.y + side * landing.PlatformBounds.height * 0.30f));
            }
        }

        private static void AddLanterns(CityLayout layout, List<CityRiverDressingPlacement> target)
        {
            float centerX = layout.River.Segments[0].WaterBounds.center.x;
            IReadOnlyList<Vector3> low = CityRiverWorldBuilder.CreateQuayWallLampPositions(layout);
            for (int index = 0; index < low.Count; index++)
            {
                Vector3 lens = low[index];
                float sign = lens.x < centerX ? 1f : -1f;
                Add(target, "river-wall-lantern-shell-" + index, CityMiscKind.RiverLanternShell,
                    lens + Vector3.right * (sign * 0.30f));
            }
            IReadOnlyList<Vector3> upper = CityRiverWorldBuilder.CreatePromenadeLampPositions(layout);
            for (int index = 0; index < upper.Count; index++)
                Add(target, "river-upper-lantern-shell-" + index, CityMiscKind.RiverLanternShell,
                    upper[index] + Vector3.up * 2.62f, Quaternion.identity,
                    new Vector3(1.8f, 1.2f, 1.8f));
        }

        private static void AddCoping(List<CityRiverDressingPlacement> target,
            string id, Vector3 start, Vector3 end, float widthScale)
        {
            Vector3 delta = end - start;
            float length = delta.magnitude;
            if (length < 0.15f) return;
            Quaternion rotation = Quaternion.LookRotation(delta / length, Vector3.up);
            int count = Mathf.Max(1, Mathf.CeilToInt(length / 1.20f));
            float pitch = length / count;
            for (int index = 0; index < count; index++)
                Add(target, id + "-" + index, CityMiscKind.RiverQuayRepair,
                    start + delta * ((index + 0.5f) / count), rotation,
                    new Vector3(widthScale, 1f, (pitch - CopingJointMeters) / 1.20f));
        }

        private static void AddMouthCaps(List<CityRiverDressingPlacement> target,
            string id, Vector3 start, float length)
        {
            if (length < 0.15f) return;
            int count = Mathf.Max(1, Mathf.CeilToInt(length / 1.20f));
            float pitch = length / count;
            for (int index = 0; index < count; index++)
                Add(target, id + "-cap-" + index, CityMiscKind.RiverMouthCap,
                    start + Vector3.right * ((index + 0.5f) * pitch), Quaternion.identity,
                    new Vector3((pitch - CopingJointMeters) / 1.20f, 1f, 1f));
        }

        private static List<Range> QuaySpans(CityRiverPlan river, CityRiverPromenadeDescriptor promenade)
        {
            var spans = new List<Range> { new Range(promenade.Bounds.yMin, promenade.Bounds.yMax) };
            foreach (CityRiverLandingDescriptor landing in river.Landings)
                if (landing.WestBank == promenade.WestBank)
                    Subtract(spans, Mathf.Min(landing.StairBounds.yMin, landing.PlatformBounds.yMin) - 0.18f,
                        Mathf.Max(landing.StairBounds.yMax, landing.PlatformBounds.yMax) + 0.18f);
            foreach (CityRiverBridgeDescriptor bridge in river.Bridges)
                Subtract(spans, bridge.DeckBounds.yMin - 0.24f, bridge.DeckBounds.yMax + 0.24f);
            return spans;
        }

        private static void Subtract(List<Range> spans, float minimum, float maximum)
        {
            for (int index = spans.Count - 1; index >= 0; index--)
            {
                Range span = spans[index];
                if (minimum >= span.Max || maximum <= span.Min) continue;
                spans.RemoveAt(index);
                if (maximum < span.Max) spans.Insert(index, new Range(maximum, span.Max));
                if (minimum > span.Min) spans.Insert(index, new Range(span.Min, minimum));
            }
        }

        private static float Sample(CityRiverPromenadeDescriptor promenade, float z)
            => Mathf.LerpUnclamped(promenade.SouthY, promenade.NorthY,
                (z - promenade.Bounds.yMin) / promenade.Bounds.height);

        private static void Add(List<CityRiverDressingPlacement> target, string id,
            CityMiscKind kind, Vector3 position, Quaternion? rotation = null, Vector3? scale = null)
        {
            if (target.Count >= MaximumPlacementCount)
                throw new InvalidOperationException("River construction details exceed their finite placement budget.");
            target.Add(new CityRiverDressingPlacement(id, kind, position,
                rotation ?? Quaternion.identity, scale ?? Vector3.one));
        }

        private readonly struct Range
        {
            internal Range(float minimum, float maximum) { Min = minimum; Max = maximum; }
            internal float Min { get; }
            internal float Max { get; }
        }
    }
}
