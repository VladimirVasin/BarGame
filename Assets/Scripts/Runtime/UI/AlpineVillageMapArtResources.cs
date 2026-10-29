using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The brochure's geographic panel and its pure-plan dependencies.</summary>
    public static class AlpineVillageMapArtResources
    {
        public const string ResourcePath = "UI/Maps/AlpineVillageMapImage";
        public const int SchemaVersion = 1;
        // Texture coordinates have their origin at the bottom left. Advertising
        // occupies the remaining paper and is never navigation ground.
        public static readonly Rect MapUvRect = new Rect(.035f, .055f, .69f, .89f);
        private const float DisplayPadding = 9f;

        [Serializable]
        public sealed class Metadata
        {
            public int schemaVersion;
            public int villageSeed;
            public string fingerprint;
            public Rect worldBounds;
            public Rect mapUvRect;
            public int imageWidth, imageHeight;
        }
        [Serializable] private sealed class Catalog { public Entry[] entries; }
        [Serializable] private sealed class Entry { public string key, value; }
        private sealed class Artwork
        {
            public Texture2D Texture;
            public Rect Bounds, UvRect;
        }
        private sealed class Fingerprint { public string Value; }
        private static ConditionalWeakTable<AlpineVillagePlan, Artwork> artwork = new ConditionalWeakTable<AlpineVillagePlan, Artwork>();
        private static ConditionalWeakTable<AlpineVillagePlan, Fingerprint> fingerprints = new ConditionalWeakTable<AlpineVillagePlan, Fingerprint>();
        private static bool resourcesLoaded, staleWarning;
        private static Texture2D texture;
        private static Metadata metadata;

        public static bool TryGet(AlpineVillagePlan plan, out Texture2D image, out Rect worldBounds, out Rect mapUvRect)
        {
            if (plan == null)
            { image = null; worldBounds = mapUvRect = default; return false; }
            Artwork bound = artwork.GetValue(plan, Bind);
            image = bound.Texture; worldBounds = bound.Bounds; mapUvRect = bound.UvRect;
            return image != null;
        }

        private static Artwork Bind(AlpineVillagePlan plan)
        {
            var result = new Artwork();
            if (plan.Seed != GameSessionState.DefaultCitySeed) return result;
            if (!resourcesLoaded)
            {
                resourcesLoaded = true;
                TextAsset source = Resources.Load<TextAsset>(ResourcePath);
                metadata = source == null ? null : JsonUtility.FromJson<Metadata>(source.text);
                texture = Resources.Load<Texture2D>(ResourcePath);
            }
            if (metadata == null || texture == null) return result;
            Rect bounds = GetWorldBounds(plan);
            bool matches = metadata.schemaVersion == SchemaVersion && metadata.villageSeed == plan.Seed &&
                metadata.imageWidth == texture.width && metadata.imageHeight == texture.height &&
                SameRect(metadata.worldBounds, bounds) && SameRect(metadata.mapUvRect, MapUvRect) &&
                string.Equals(metadata.fingerprint, CreateFingerprint(plan), StringComparison.Ordinal);
            if (!matches)
            {
                if (!staleWarning)
                {
                    staleWarning = true;
                    GameLog.Warning("map", "village_illustration_outdated_redraw_with_imagegen");
                }
                return result;
            }
            result.Texture = texture; result.Bounds = bounds; result.UvRect = metadata.mapUvRect;
            return result;
        }

        public static Rect GetWorldBounds(AlpineVillagePlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            Rect bounds = plan.TerrainBounds;
            AlpineVillageExpansionPlan expansion = plan.Expansion;
            foreach (Vector2 vertex in AlpineVillageAvalanchePlan.Footprint)
                Include(ref bounds, expansion.ToWorld(vertex + AlpineVillageAvalanchePlan.Origin));
            Include(ref bounds, expansion.FarRoadEdge);
            foreach (AlpineVillageRidgeDescriptor ridge in plan.Ridges)
                for (int x = -1; x <= 1; x += 2)
                    for (int z = -1; z <= 1; z += 2)
                        Include(ref bounds, ridge.Center + ridge.Forward * (ridge.Length * .5f * x) +
                            ridge.Right * (ridge.Thickness * .5f * z));
            return Rect.MinMaxRect(bounds.xMin - DisplayPadding, bounds.yMin - DisplayPadding,
                bounds.xMax + DisplayPadding, bounds.yMax + DisplayPadding);
        }

        /// <summary>Virtual sheet extent used only to fit/pan the complete paper.</summary>
        public static Rect GetSheetWorldBounds(AlpineVillagePlan plan) => GetSheetWorldBounds(GetWorldBounds(plan), MapUvRect);

        public static Rect GetSheetWorldBounds(Rect geography, Rect mapUvRect) =>
            new Rect(geography.xMin - geography.width * mapUvRect.xMin / mapUvRect.width,
                geography.yMin - geography.height * mapUvRect.yMin / mapUvRect.height,
                geography.width / mapUvRect.width, geography.height / mapUvRect.height);

        public static string CreateFingerprint(AlpineVillagePlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            return fingerprints.GetValue(plan, p => new Fingerprint { Value = HashGeography(p) }).Value;
        }

        private static string HashGeography(AlpineVillagePlan plan)
        {
            using (var stream = new MemoryStream(32768))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write("alpine_village_brochure_v1"); writer.Write(plan.Seed);
                RectValue(writer, GetWorldBounds(plan)); RectValue(writer, MapUvRect);
                RectValue(writer, plan.CoreTerrainBounds); RectValue(writer, plan.TerrainBounds);
                Point(writer, plan.SlopeOrigin); Point(writer, plan.Uphill); Scalar(writer, plan.Grade);
                Point(writer, plan.Station.PadArea.Center); Point(writer, plan.Station.PadArea.Forward);
                Point(writer, plan.Station.PadArea.Size);
                writer.Write(plan.Lane.Samples.Count);
                foreach (AlpineVillageLaneSample sample in plan.Lane.Samples)
                { Point(writer, sample.Position); Scalar(writer, sample.Width); }
                writer.Write(plan.Plots.Count);
                foreach (AlpineVillagePlotDescriptor plot in plan.Plots)
                {
                    writer.Write(plot.StableId); writer.Write((int)plot.Kind);
                    Point(writer, plot.GroundCenter); Point(writer, plot.Facing); Point(writer, plot.FootprintSize);
                    Point(writer, plot.DoorGroundPosition); Point(writer, plot.DoorDockPosition); Scalar(writer, plot.Height);
                }
                var paths = AlpineVillagePathPlanner.Create(plan);
                writer.Write(paths.Count);
                foreach (AlpineVillagePathDescriptor path in paths)
                {
                    writer.Write(path.StableId); writer.Write((int)path.Kind);
                    Point(writer, path.Start); Point(writer, path.End); Scalar(writer, path.SurfaceHalfWidth);
                }
                AlpineVillageExpansionPlan expansion = plan.Expansion;
                Point(writer, expansion.LodgeCenter); Point(writer, expansion.LodgeForward); Point(writer, expansion.LodgeSize);
                Point(writer, expansion.LodgeApproach); Point(writer, expansion.ServiceShedCenter); Point(writer, expansion.ServiceShedSize);
                Point(writer, expansion.LiftBasePosition); Point(writer, expansion.LiftTopPosition);
                Point(writer, expansion.CliffEdge); Point(writer, expansion.FarRoadEdge);
                Point(writer, expansion.CliffBarrierCenter); Scalar(writer, expansion.CliffBarrierWidth);
                Point(writer, expansion.WarehouseCenter); Point(writer, expansion.TruckWreckCenter); Point(writer, expansion.ChairPileCenter);
                writer.Write(expansion.Paths.Count);
                foreach (AlpineVillagePathDescriptor path in expansion.Paths)
                {
                    writer.Write(path.StableId); writer.Write((int)path.Kind);
                    Point(writer, path.Start); Point(writer, path.End); Scalar(writer, path.SurfaceHalfWidth);
                }
                writer.Write(expansion.Abandonment.Plots.Count);
                foreach (AlpineVillageAbandonedPlot plot in expansion.Abandonment.Plots)
                {
                    writer.Write(plot.Id); writer.Write(plot.Model); writer.Write(plot.Closed);
                    Point(writer, plot.GroundCenter); Point(writer, plot.Forward); Point(writer, plot.Size);
                    RectValue(writer, plot.YardBounds); Scalar(writer, plot.Height);
                }
                writer.Write(AlpineVillageAvalanchePlan.Footprint.Count);
                foreach (Vector2 vertex in AlpineVillageAvalanchePlan.Footprint)
                    Point(writer, expansion.ToWorld(vertex + AlpineVillageAvalanchePlan.Origin));
                writer.Write(plan.Ridges.Count);
                foreach (AlpineVillageRidgeDescriptor ridge in plan.Ridges)
                {
                    writer.Write(ridge.StableId); Point(writer, ridge.Center); Point(writer, ridge.Forward);
                    Scalar(writer, ridge.Length); Scalar(writer, ridge.Thickness); Scalar(writer, ridge.Height);
                }
                writer.Write(plan.Brook != null);
                if (plan.Brook != null)
                {
                    writer.Write(plan.Brook.Samples.Count);
                    foreach (AlpineVillageBrookSample sample in plan.Brook.Samples) BrookSample(writer, sample);
                    writer.Write(plan.Brook.SeepLine.Count);
                    foreach (AlpineVillageBrookSample sample in plan.Brook.SeepLine) BrookSample(writer, sample);
                    Point(writer, plan.Brook.BowlCenter); Point(writer, plan.Brook.BowlFacing); Point(writer, plan.Brook.CatchOuterSize);
                }
                TextAsset russian = Resources.Load<TextAsset>("Localization/ru");
                Catalog catalog = russian == null ? null : JsonUtility.FromJson<Catalog>(russian.text);
                if (catalog?.entries != null)
                    foreach (Entry entry in catalog.entries)
                        if (entry.key != null && entry.key.StartsWith("map.alpine_village.", StringComparison.Ordinal))
                        { writer.Write(entry.key); writer.Write(entry.value ?? string.Empty); }
                writer.Flush();
                using (SHA256 hash = SHA256.Create())
                {
                    stream.Position = 0;
                    byte[] digest = hash.ComputeHash(stream);
                    var text = new StringBuilder(digest.Length * 2);
                    foreach (byte value in digest) text.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                    return text.ToString();
                }
            }
        }

        private static void BrookSample(BinaryWriter writer, AlpineVillageBrookSample sample)
        { Point(writer, sample.Position); Point(writer, sample.Right); Scalar(writer, sample.Width); writer.Write((int)sample.Reach); }
        private static void Include(ref Rect bounds, Vector3 point) => bounds = Rect.MinMaxRect(
            Mathf.Min(bounds.xMin, point.x), Mathf.Min(bounds.yMin, point.z), Mathf.Max(bounds.xMax, point.x), Mathf.Max(bounds.yMax, point.z));
        private static bool SameRect(Rect a, Rect b) => Mathf.Abs(a.x - b.x) < .001f && Mathf.Abs(a.y - b.y) < .001f &&
            Mathf.Abs(a.width - b.width) < .001f && Mathf.Abs(a.height - b.height) < .001f;
        private static void RectValue(BinaryWriter writer, Rect value)
        { Scalar(writer, value.x); Scalar(writer, value.y); Scalar(writer, value.width); Scalar(writer, value.height); }
        private static void Point(BinaryWriter writer, Vector2 value) { Scalar(writer, value.x); Scalar(writer, value.y); }
        private static void Point(BinaryWriter writer, Vector3 value) { Scalar(writer, value.x); Scalar(writer, value.y); Scalar(writer, value.z); }
        private static void Scalar(BinaryWriter writer, float value) =>
            writer.Write((long)Math.Round(value * 1000d, MidpointRounding.AwayFromZero));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetResources()
        {
            artwork = new ConditionalWeakTable<AlpineVillagePlan, Artwork>();
            fingerprints = new ConditionalWeakTable<AlpineVillagePlan, Fingerprint>();
            resourcesLoaded = staleWarning = false; texture = null; metadata = null;
        }
    }
}
