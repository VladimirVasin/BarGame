using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    public enum CityEastExitDressingFit { Feet, Ground, Road }

    /// <summary>A whole collinear fence run, independent of its four-metre mesh bays.</summary>
    public readonly struct CityEastFenceLandscapeRun
    {
        internal CityEastFenceLandscapeRun(string id, Vector2 start, Vector2 end, Vector2 inward, bool front)
        { Id = id; Start = start; End = end; Inward = inward; IsFront = front; }
        public string Id { get; }
        public Vector2 Start { get; }
        public Vector2 End { get; }
        public Vector2 Inward { get; }
        public bool IsFront { get; }
        public float Length => Vector2.Distance(Start, End);
    }

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
        public IReadOnlyList<CityEastFenceLandscapeRun> FenceRuns { get; }
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
            FenceRuns = CreateFenceRuns(exit);
            if (!exit.IsEnabled) return;
            float x = exit.CheckpointPosition.x, z = exit.CheckpointPosition.z;
            // The authored apron outline selects existing terrain triangles;
            // it has no second, raised skin under the canopy or its approach.
            Part("Canopy Gravel Apron", "CanopyApron", "Booth Ground", new Vector2(x - 2.7f, z - 10.2f), 0f,
                Vector3.one, new Vector3(4.8f, .02f, 3.65f), CityEastExitDressingFit.Ground);
            Part("Booth Shelter", "Shelter", "Booth Surroundings", new Vector2(x - 2f, z - 10.15f), 0f,
                Vector3.one, new Vector3(3.6f, 2.65f, 3.2f), CityEastExitDressingFit.Feet, exit.BoothPosition.y);
            for (int side = -1; side <= 1; side += 2)
                Solid("Shelter Post " + side, "Booth Shelter", new Vector2(x - 2f + side * 1.55f, z - 11.45f),
                    Quaternion.identity, new Vector3(.30f, 2.56f, .30f), exit.BoothPosition.y + 2.56f);
            // The bench sits outside the western canopy edge and faces the
            // main street. Its north end clears the door approach and patrol.
            Vector2 benchPoint = new Vector2(x - 4.85f, z - 11.4f);
            Part("Shelter Bench", "Bench", "Booth Surroundings", benchPoint, 90f,
                Vector3.one, new Vector3(1.9f, .88f, .66f), CityEastExitDressingFit.Feet);
            Solid("Shelter Bench", "Shelter Bench", benchPoint, Quaternion.Euler(0f, 90f, 0f),
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
                new Vector2(x - 5.2f, z - 9.8f), new Vector2(x - 2.7f, z - 10.2f), .82f);
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
            // One channel continues beneath the crossing. Joining two raised
            // drain sheets at a bent endpoint doubled their fitted lips.
            Trace("Dry Road Drain", "DryDrain", "Road Margins",
                new Vector2(exit.YardBounds.xMin + 2.5f, z - 6.2f), new Vector2(x - 4.8f, z - 6.2f), .76f);
            Part("Post Drain Crossing", "DrainCrossing", "Road Margins", new Vector2(x - 5.65f, z - 6.2f), 0f,
                Vector3.one, new Vector3(1.3f, .058f, 1.02f), CityEastExitDressingFit.Ground);

            float serviceX = exit.YardBounds.xMin + 24f;
            float firstServiceX = exit.YardBounds.xMin + CityFringeYardPlanner.FirstEastUtilityShedDepth - 5f;
            float firstShedZ = Mathf.Lerp(exit.YardBounds.yMin + 12f, exit.YardBounds.yMax - 12f, .18f);
            // The nearest shed sits forward of the other two. Follow its
            // western door frontage, then rejoin the original northern trace;
            // the old straight backbone would pass through the building.
            Trace("Service Yard Entry", "GravelPatch", "Service Yard Traces",
                new Vector2(exit.YardBounds.xMin + 18f, z + 4.3f), new Vector2(firstServiceX, z + 9f), .78f);
            Trace("Service Yard Frontage", "GravelPatch", "Service Yard Traces",
                new Vector2(firstServiceX, z + 9f), new Vector2(firstServiceX, firstShedZ + 5f), .78f);
            Trace("Service Yard Return", "GravelPatch", "Service Yard Traces",
                new Vector2(firstServiceX, firstShedZ + 5f), new Vector2(serviceX, firstShedZ + 5f), .78f);
            Trace("Service Yard Trace", "GravelPatch", "Service Yard Traces",
                new Vector2(serviceX, firstShedZ + 5f), new Vector2(serviceX + .3f, exit.YardBounds.yMax - 8f), .78f);
            for (int i = 0; i < 3; i++)
            {
                float shedZ = Mathf.Lerp(exit.YardBounds.yMin + 12f, exit.YardBounds.yMax - 12f, .18f + i * .32f);
                float shedDepth = i == 0 ? CityFringeYardPlanner.FirstEastUtilityShedDepth : 35f + i * 2.2f;
                Trace("Shed Approach " + i, "GravelPatch", "Service Yard Traces",
                    new Vector2(i == 0 ? firstServiceX : serviceX, shedZ),
                    new Vector2(exit.YardBounds.xMin + shedDepth - 2.8f, shedZ), .76f);
                Part("Shed Door Apron " + i, "GravelPatch", "Service Yard Traces",
                    new Vector2(exit.YardBounds.xMin + shedDepth - 3.55f, shedZ), 0f,
                    new Vector3(1.8f / 6f, 1f, 1.4f / 3.46f),
                    new Vector3(6f, .02f, 3.46f), CityEastExitDressingFit.Ground);
            }
            foreach (float poleZ in new[] { 0f, exit.YardBounds.yMax - 8f })
                Trace("Pole Approach " + poleZ, "GravelPatch", "Service Yard Traces",
                    new Vector2(serviceX, poleZ), new Vector2(exit.YardBounds.xMin + 26.7f, poleZ), .62f);

            // Every run, including its returns, receives an interrupted worn
            // toe. The old front trace covered only 32 m of a 192 m run.
            foreach (CityEastFenceLandscapeRun run in FenceRuns)
            {
                Vector2 direction = (run.End - run.Start).normalized;
                int count = Mathf.Max(1, Mathf.RoundToInt(run.Length / 12f));
                for (int i = 0; i < count; i++)
                {
                    float middle = run.Length * (i + .5f) / count;
                    float half = Mathf.Min(4.2f + i % 3 * .55f, run.Length / count * .40f);
                    Vector2 center = run.Start + direction * middle + run.Inward * .58f;
                    Trace(run.Id + " Worn Toe " + i, "FenceToe", "Fence Ground",
                        center - direction * half, center + direction * half, .68f + i % 2 * .14f);
                }
            }
            int footing = 0;
            foreach (CityEastExitFence span in exit.Fences)
            {
                if (footing++ % 3 != 0) continue;
                Vector3 foot = span.Start;
                // South bases are embedded on the closed-yard side, never
                // on the church's independently graded garden surface.
                Vector2 point = new Vector2(Mathf.Clamp(foot.x, x + .03f, exit.YardBounds.xMax - .24f),
                    Mathf.Clamp(foot.z, exit.YardBounds.yMin + .24f, exit.NorthYardBounds.yMax - .24f));
                Part("Old Fence Footing " + footing, "FenceFooting", "Fence Ground", point, 0f,
                    Vector3.one, new Vector3(.46f, .105f, .42f), CityEastExitDressingFit.Ground);
            }

            // Density belongs to distance along the whole boundary, not to
            // six fixed points. Independent seeded streams keep a change to
            // one run from reshuffling all the others.
            for (int r = 0; r < FenceRuns.Count; r++)
            {
                CityEastFenceLandscapeRun run = FenceRuns[r];
                Vector2 direction = (run.End - run.Start).normalized;
                float alongYaw = -Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                var random = new System.Random(unchecked(exit.Layout.Seed * 397 ^ (r + 1) * 7919));
                int count = Mathf.Max(1, Mathf.RoundToInt(run.Length / (run.IsFront ? 14f : 16f)));
                for (int i = 0; i < count; i++)
                {
                    string group = run.Id + " Patch " + i;
                    float t = run.Length * (i + .5f) / count + (float)(random.NextDouble() - .5) * 1.8f;
                    Vector2 axis = run.Start + direction * t;
                    Vector2 inside = axis + run.Inward * (2.5f + (float)random.NextDouble() * .5f);
                    int variety = (i + r + random.Next(3)) % 4;
                    // A few long, low ground masses sit wholly behind the
                    // fence. The city-side walk has no raised earth island.
                    if (run.Length > 25f && i % 3 == 1)
                        LandscapePart(group + " Bank", "EarthBank", group, axis + run.Inward * 4.8f,
                            alongYaw + (float)(random.NextDouble() - .5) * 10f,
                            new Vector3(.92f + variety * .09f, .42f + variety * .055f, .80f),
                            new Vector3(10.1f, .59f, 2.8f), CityEastExitDressingFit.Ground, false, true);

                    Plant(group + " Inner Shrub", variety % 2 == 0 ? "BranchShrub" : "CreepingScrub",
                        group, inside, alongYaw + variety * 23f, .84f + variety * .055f, false);
                    Plant(group + " Inner Grass", variety % 2 == 0 ? "MattedGrass" : "TallWeeds", group,
                        inside + direction * 2.8f + run.Inward * .45f, alongYaw - 17f, .88f, false);
                    if (i % 2 == 0)
                        Plant(group + " Inner Scatter", "GravelScatter", group,
                            axis + run.Inward * 1.15f - direction * 2.1f, alongYaw + 9f, .82f, false);

                    if (run.IsFront && run.Length > 25f)
                    {
                        // The 12 m foreground retains a continuous walking
                        // corridor. Plants spread across its fence-side edge,
                        // with enough body clearance to approach from the road.
                        Vector2 foreground = axis - run.Inward * (2.45f + (float)random.NextDouble() * .35f);
                        bool broadSwale = axis.y > z + 34f && axis.y < exit.Swale.EndZ - 8f;
                        if (broadSwale)
                            foreground.x = exit.Swale.CenterX(axis.y) + exit.Swale.HalfWidth(axis.y) * .70f;
                        string primary = new[] { "CreepingScrub", "BranchShrub", "LowShrub", "MattedGrass" }[variety];
                        Plant(group + " Front Main", primary, group, foreground, alongYaw + variety * 19f, 1f, true);
                        int tufts = 2 + random.Next(3);
                        for (int j = 0; j < tufts; j++)
                        {
                            float along = (j - (tufts - 1) * .5f) * 2.4f + (float)(random.NextDouble() - .5);
                            Vector2 tuft = foreground + direction * along - run.Inward * (j % 2 == 0 ? 1.1f : -.8f);
                            if (broadSwale)
                                tuft.x = exit.Swale.CenterX(tuft.y) + exit.Swale.HalfWidth(tuft.y) *
                                    (j % 2 == 0 ? -.62f : .70f);
                            string assembly = (j + variety) % 3 == 0 ? "MattedGrass" :
                                (j + variety) % 3 == 1 ? "TallWeeds" : "DryGrass";
                            Plant(group + " Front Grass " + j, assembly, group, tuft,
                                alongYaw + (float)(random.NextDouble() - .5) * 42f, .72f + j % 3 * .11f, true);
                        }
                        if (i % 3 == 0)
                        {
                            Vector2 scatter = foreground + direction * 3.8f;
                            if (broadSwale) scatter.x = exit.Swale.CenterX(scatter.y) + .15f;
                            Plant(group + " Front Scatter", "GravelScatter", group,
                                scatter, alongYaw + 6f, .95f, true);
                        }
                        // This broad worn patch is a material region in the
                        // standing ground; it cannot float or introduce steps.
                        if (!broadSwale)
                            LandscapePart(group + " Wear", "GravelPatch", "Fence Wear", foreground + direction * 1.4f,
                                alongYaw + 6f, new Vector3(.9f + variety * .08f, 1f, .55f),
                                new Vector3(6f, .02f, 3.46f), CityEastExitDressingFit.Ground, true);
                    }

                    if (run.IsFront && run.Length > 25f && i % 4 == 2)
                    {
                        // Short dry branches make the long drainage system
                        // visible through the bars; the grate has a real bed.
                        Vector2 drain = axis + run.Inward * 1.3f;
                        if (ClearLandscape(FootprintFor(new Vector3(drain.x, 0, drain.y),
                            Quaternion.Euler(0, alongYaw, 0), new Vector3(6f, .1f, .9f)), false))
                        {
                            Trace(group + " Dry Drain", "DryDrain", group, drain - direction * 3f, drain + direction * 3f, .76f);
                            LandscapePart(group + " Inspection", "DrainInspection", group, drain, alongYaw,
                                Vector3.one, new Vector3(1.3f, .12f, .85f), CityEastExitDressingFit.Ground, false);
                        }
                    }
                }
            }

            // One small, supported repair reserve belongs to the existing
            // nearest shed; it is kept off its western door and service trace.
            LandscapePart("Shed Repair Reserve", "RepairStock", "Service Yard Traces",
                new Vector2(exit.YardBounds.xMin + CityFringeYardPlanner.FirstEastUtilityShedDepth + 4.7f, firstShedZ + 7.4f),
                0f, Vector3.one, new Vector3(2f, .45f, .8f), CityEastExitDressingFit.Feet, false, true);

            void Plant(string id, string assembly, string group, Vector2 point, float yaw, float scale, bool front)
            {
                Vector3 size = assembly == "CreepingScrub" ? new Vector3(2.4f, .45f, 1.7f) :
                    assembly == "BranchShrub" ? new Vector3(1.8f, 1.05f, 1.5f) :
                    assembly == "MattedGrass" ? new Vector3(2.8f, .27f, 1.5f) :
                    assembly == "TallWeeds" ? new Vector3(1.4f, .85f, 1f) :
                    assembly == "GravelScatter" ? new Vector3(2.1f, .12f, 1.2f) :
                    assembly == "LowShrub" ? new Vector3(1.6f, .8f, 1.35f) : new Vector3(1.4f, .65f, 1.1f);
                LandscapePart(id, assembly, group, point, yaw, Vector3.one * scale, size,
                    assembly == "BranchShrub" || assembly == "LowShrub" ? CityEastExitDressingFit.Feet : CityEastExitDressingFit.Ground, front);
            }
            void LandscapePart(string id, string assembly, string group, Vector2 point, float yaw, Vector3 scale,
                Vector3 size, CityEastExitDressingFit fit, bool front, bool solid = false)
            {
                Quaternion rotation = Quaternion.Euler(0, yaw, 0);
                Rect footprint = FootprintFor(new Vector3(point.x, 0, point.y), rotation, Vector3.Scale(size, scale));
                if (!ClearLandscape(footprint, front, !solid)) return;
                Part(id, assembly, group, point, yaw, scale, size, fit);
                if (solid) Solid(id, id, point, rotation, Vector3.Scale(size, scale));
            }
            bool ClearLandscape(Rect footprint, bool front, bool allowBank = false)
            {
                // Reject the whole footprint instead of clamping its centre
                // onto a fence, church boundary, street or neighbouring prop.
                // Behind the closure, low planting clears the actual road
                // shoulder. The much wider shed/patrol reservation otherwise
                // removes every plant along the entire southern return.
                Rect roadReserve = exit.RoadBounds;
                roadReserve.yMin -= .8f; roadReserve.yMax += .8f;
                if (footprint.xMin < exit.YardBounds.xMin + .4f || footprint.xMax > exit.YardBounds.xMax - .4f ||
                    footprint.yMin < exit.YardBounds.yMin + .4f || footprint.yMax > exit.NorthYardBounds.yMax - .4f ||
                    footprint.Overlaps(front ? exit.ClearanceBounds : roadReserve)) return false;
                if (front && exit.Swale.OverlapsCrossing(footprint)) return false;
                if (front ? footprint.xMin < exit.YardBounds.xMin + 5.6f || footprint.xMax > x - .45f :
                    footprint.xMin < x + .45f) return false;
                foreach (CityEastExitDressingSolid obstacle in solids)
                {
                    if (allowBank && obstacle.Id.EndsWith(" Bank", StringComparison.Ordinal)) continue;
                    Rect b = obstacle.Footprint; b.xMin -= .55f; b.xMax += .55f; b.yMin -= .55f; b.yMax += .55f;
                    if (footprint.Overlaps(b)) return false;
                }
                foreach (CityEastExitDressingPart existing in parts)
                {
                    if (existing.GroupId != "Post Foot Traces" && existing.GroupId != "Service Yard Traces") continue;
                    Rect b = existing.Footprint; b.xMin -= .35f; b.xMax += .35f; b.yMin -= .35f; b.yMax += .35f;
                    if (footprint.Overlaps(b)) return false;
                }
                // All three shed shells predate this kit and have their own
                // collision. Reserve their real footprints before decoration.
                for (int i = 0; i < 3; i++)
                {
                    float shedZ = Mathf.Lerp(exit.YardBounds.yMin + 12f, exit.YardBounds.yMax - 12f, .18f + i * .32f);
                    float depth = i == 0 ? CityFringeYardPlanner.FirstEastUtilityShedDepth : 35f + i * 2.2f;
                    var shed = new Rect(exit.YardBounds.xMin + depth - 3.1f, shedZ - 4.1f, 6.2f, 8.2f);
                    if (footprint.Overlaps(shed)) return false;
                }
                return true;
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
                Vector3 size = assembly == "DryDrain" ? new Vector3(10f, .10f, 1.382f) :
                    assembly == "FenceToe" ? new Vector3(7.8f, .02f, .56f) : new Vector3(6f, .02f, 3.46f);
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
            if (Mathf.Abs(center.x - exit.CheckpointPosition.x) > .01f) return false;
            float first = exit.CheckpointPosition.z + 13f;
            // One whole bay at each old repair, widely separated along the
            // front. They remain complete barriers with the same collision.
            for (float repair = first; repair < exit.NorthYardBounds.yMax - 8f; repair += 53f)
                if (repair >= Mathf.Min(span.Start.z, span.End.z) && repair < Mathf.Max(span.Start.z, span.End.z)) return true;
            return false;
        }
        private static IReadOnlyList<CityEastFenceLandscapeRun> CreateFenceRuns(CityEastExitPlan source)
        {
            var runs = new List<CityEastFenceLandscapeRun>();
            if (!source.IsEnabled) return runs.AsReadOnly();
            Vector2 start = default, end = default, direction = default;
            bool pending = false;
            foreach (CityEastExitFence span in source.Fences)
            {
                var a = new Vector2(span.Start.x, span.Start.z);
                var b = new Vector2(span.End.x, span.End.z);
                Vector2 forward = (b - a).normalized;
                if (pending && ((end - a).sqrMagnitude > .001f || Vector2.Dot(direction, forward) < .999f)) Flush();
                if (!pending) { start = a; direction = forward; pending = true; }
                end = b;
            }
            if (pending) Flush();
            return runs.AsReadOnly();

            void Flush()
            {
                Vector2 middle = (start + end) * .5f;
                Vector2 inward = new Vector2(-direction.y, direction.x);
                Vector2 yardCenter = new Vector2((source.CheckpointPosition.x + source.YardBounds.xMax) * .5f,
                    (source.YardBounds.yMin + source.NorthYardBounds.yMax) * .5f);
                if (Vector2.Dot(inward, yardCenter - middle) < 0f) inward = -inward;
                bool front = Mathf.Abs(middle.x - source.CheckpointPosition.x) < .01f;
                runs.Add(new CityEastFenceLandscapeRun("Fence Run " + runs.Count, start, end, inward, front));
                pending = false;
            }
        }
        internal static Rect FootprintFor(Vector3 position, Quaternion rotation, Vector3 size)
        {
            Vector3 right = rotation * new Vector3(size.x * .5f, 0, 0), forward = rotation * new Vector3(0, 0, size.z * .5f);
            float x = Mathf.Abs(right.x) + Mathf.Abs(forward.x), z = Mathf.Abs(right.z) + Mathf.Abs(forward.z);
            return Rect.MinMaxRect(position.x - x, position.z - z, position.x + x, position.z + z);
        }
    }
}
