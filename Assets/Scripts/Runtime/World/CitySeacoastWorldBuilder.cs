using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Materialises a <see cref="CitySeacoastPlan"/>: the dressing
    /// batches into one combined oriented-box mesh per 48 m chunk and
    /// style, and each lamp descriptor becomes a real fixture — the
    /// rental hut's hooded door bulb and the pier's kerosene hand lamp
    /// exactly as they were at the lake, the esplanade's cast-iron
    /// posts as glow-only fixtures one shade dimmer than the river
    /// embankment's, and the beacon's lens burning on the mol head.
    ///
    /// The sea itself is built here too: chunked animated sheets of
    /// the shared water shader where the flat municipal slab used to
    /// be, over a shelving silt bed that rises to meet the sand — the
    /// depth-threshold foam draws the surf line along that shelf on
    /// its own. Each sheet runs a cosmetic apron past the map's north
    /// edge, because past the apron there is only fog anyway.
    /// </summary>
    public static class CitySeacoastWorldBuilder
    {
        public const string RootName = "North Seacoast";
        private const float SpatialChunkSize = 48f;

        // The flat palette. The textured styles will be transcribed
        // into the seacoast texture generator when the skin pass
        // lands, which solves the albedo compensation that keeps the
        // textured product at this exact brightness — edit them
        // together. The timber and hull values are the lake's, moved
        // here with the boats they colour.
        internal static readonly Color Concrete =
            new Color(0.29f, 0.29f, 0.27f);
        internal static readonly Color Granite =
            new Color(0.34f, 0.34f, 0.32f);
        internal static readonly Color Planking =
            new Color(0.31f, 0.28f, 0.24f);
        internal static readonly Color TarredTimber =
            new Color(0.16f, 0.14f, 0.12f);
        internal static readonly Color HullPaint =
            new Color(0.26f, 0.32f, 0.31f);
        internal static readonly Color HullTar =
            new Color(0.13f, 0.12f, 0.11f);
        internal static readonly Color Iron =
            new Color(0.075f, 0.085f, 0.085f);
        internal static readonly Color RustIron =
            new Color(0.28f, 0.17f, 0.12f);
        internal static readonly Color Grass =
            new Color(0.30f, 0.32f, 0.19f);
        // The one legible colour on the whole shore: what is left of
        // the paint on the hire board.
        internal static readonly Color PaintAccent =
            new Color(0.52f, 0.44f, 0.20f);
        internal static readonly Color Litter =
            new Color(0.27f, 0.22f, 0.16f);

        // The silt shelf under the shallows. Darker than the sand it
        // continues, because half a metre of sea water is what is
        // between them.
        internal static readonly Color Silt =
            new Color(0.10f, 0.10f, 0.085f);

        // The hut bulb is the lake's, bolt for bolt: same warm
        // tungsten over the same kind of door, dimmed by day, full at
        // night, never switched off.
        internal static readonly Color HutBulbColor =
            new Color(1.00f, 0.80f, 0.55f);
        internal const float HutBulbNightIntensity = 64f;
        internal const float HutBulbDayIntensity = 14f;
        internal const float HutBulbRange = 7.0f;

        // The esplanade lamps glow without casting: iron posts and an
        // emissive plafond on the glow registry, the embankment lamp
        // recipe gone older and dimmer — these were here first.
        internal static readonly Color EsplanadeLampGlow =
            new Color(1.08f, 0.62f, 0.28f);

        // The beacon's lens. Pale warm white, unlike every tungsten
        // bulb in the city: it is navigation light, not domestic.
        // Floodlight class on the documented intensity ladder — it is
        // infrastructure, unlike the hand lamp on the pier.
        internal static readonly Color BeaconLensColor =
            new Color(0.95f, 0.90f, 0.78f);
        internal const float BeaconNightIntensity = 150f;
        internal const float BeaconRange = 16f;

        private static readonly Color LampIronColor =
            new Color(0.070f, 0.075f, 0.075f);

        public static GameObject Build(
            Transform parent,
            CitySeacoastPlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);

            BuildSea(root, plan.Frame);
            BuildDressing(root, plan);
            BuildLamps(root, plan);

            return root.gameObject;
        }

        /// <summary>
        /// The sea: animated sheets over a shelving bed. The bed only
        /// exists near the shore — deeper out the depth fade bottoms
        /// into the deep colour on its own, which is all the honesty a
        /// sea seen through this fog can use.
        /// </summary>
        private static void BuildSea(
            Transform root,
            in CitySeacoastFrame frame)
        {
            Transform sea = new GameObject("Sea").transform;
            sea.SetParent(root, false);

            var sheets = new List<Rect>();
            CitySeacoastSeaLayout.CreateSheetRects(frame, sheets);
            for (int index = 0; index < sheets.Count; index++)
            {
                CityWaterSurfaceFactory.CreateSlopedSurface(
                    $"Sea Water {index:D2}",
                    sea,
                    sheets[index],
                    frame.SeaTopY,
                    frame.SeaTopY,
                    CitySeaResources.WaterMaterial);
            }

            var shelves = new List<Bounds>();
            CitySeacoastSeaLayout.CreateShelfBoxes(frame, shelves);
            if (shelves.Count > 0)
            {
                RuntimePrimitiveFactory.CreateCombinedBoxes(
                    "Sea Bed Shelf",
                    sea,
                    shelves,
                    Silt,
                    false);
            }
        }

        private static void BuildDressing(
            Transform root,
            CitySeacoastPlan plan)
        {
            var batches =
                new Dictionary<BatchKey, List<RuntimeOrientedBox>>();
            for (int index = 0; index < plan.Parts.Count; index++)
            {
                CitySeacoastPartDescriptor part = plan.Parts[index];
                var key = new BatchKey(
                    Mathf.FloorToInt(part.Center.x / SpatialChunkSize),
                    Mathf.FloorToInt(part.Center.z / SpatialChunkSize),
                    part.Style);
                if (!batches.TryGetValue(
                        key,
                        out List<RuntimeOrientedBox> boxes))
                {
                    boxes = new List<RuntimeOrientedBox>();
                    batches.Add(key, boxes);
                }

                boxes.Add(new RuntimeOrientedBox(
                    part.Center,
                    part.Rotation,
                    part.Size));
            }

            var keys = new List<BatchKey>(batches.Keys);
            keys.Sort(BatchKey.Compare);
            for (int index = 0; index < keys.Count; index++)
            {
                BatchKey key = keys[index];
                CitySeacoastSurfaceKind? surface =
                    ResolveSurface(key.Style);
                float? uvTileSize = surface.HasValue
                    ? CitySeacoastSurfaceAppearance
                        .GetRecipe(surface.Value).MetersPerTile
                    : (float?)null;
                GameObject chunk =
                    RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                        $"Seacoast Chunk {key.X} {key.Z} {key.Style}",
                        root,
                        batches[key],
                        ResolveColor(key.Style),
                        CitySeacoastRules.BlocksMovement(key.Style),
                        uvTileSize);
                if (surface.HasValue)
                {
                    CitySeacoastSurfaceAppearance.ApplyCombined(
                        chunk.GetComponent<Renderer>(),
                        surface.Value,
                        ResolveColor(key.Style));
                }
            }
        }

        private static CitySeacoastSurfaceKind? ResolveSurface(
            CitySeacoastStyle style)
        {
            switch (style)
            {
                case CitySeacoastStyle.Concrete:
                    return CitySeacoastSurfaceKind.Concrete;
                case CitySeacoastStyle.Granite:
                    return CitySeacoastSurfaceKind.Granite;
                case CitySeacoastStyle.Planking:
                case CitySeacoastStyle.TarredTimber:
                    return CitySeacoastSurfaceKind.Plank;
                case CitySeacoastStyle.HullPaint:
                case CitySeacoastStyle.HullTar:
                    return CitySeacoastSurfaceKind.Hull;
                default:
                    // Iron, rust, grass, sign paint and litter stay
                    // flat colour: their members are too thin for a
                    // sheet to read through the PS1 composite.
                    return null;
            }
        }

        private static void BuildLamps(
            Transform root,
            CitySeacoastPlan plan)
        {
            var esplanadePosts = new List<Bounds>();
            var esplanadeBulbs = new List<Bounds>();
            for (int index = 0; index < plan.Lamps.Count; index++)
            {
                CitySeacoastLampDescriptor lamp = plan.Lamps[index];
                switch (lamp.Kind)
                {
                    case CitySeacoastLampKind.HutDoor:
                        BuildHutDoorLamp(root, lamp);
                        break;
                    case CitySeacoastLampKind.PierHead:
                        CityHandLampWorldBuilder.Build(
                            root,
                            "Seacoast Pier Hand Lamp",
                            lamp.GroundPosition,
                            lamp.YawDegrees);
                        break;
                    case CitySeacoastLampKind.Beacon:
                        BuildBeaconLens(root, lamp);
                        break;
                    default:
                        esplanadePosts.Add(new Bounds(
                            lamp.GroundPosition + Vector3.up * 1.25f,
                            new Vector3(0.16f, 2.5f, 0.16f)));
                        esplanadeBulbs.Add(new Bounds(
                            lamp.GroundPosition + Vector3.up * 2.62f,
                            new Vector3(0.42f, 0.24f, 0.42f)));
                        break;
                }
            }

            if (esplanadePosts.Count == 0)
            {
                return;
            }

            Transform lights = new GameObject(
                "Esplanade Lamps").transform;
            lights.SetParent(root, false);
            RuntimePrimitiveFactory.CreateCombinedBoxes(
                "Esplanade Lamp Posts",
                lights,
                esplanadePosts,
                Iron,
                true);
            GameObject glow =
                RuntimePrimitiveFactory.CreateCombinedBoxes(
                    "Esplanade Lamp Glow",
                    lights,
                    esplanadeBulbs,
                    EsplanadeLampGlow,
                    CityNightResources.EmissiveMaterial);
            CityNightGlowRegistry.Register(
                glow.GetComponent<Renderer>(),
                EsplanadeLampGlow);
        }

        /// <summary>
        /// The rental hut's door bulb, rebuilt board for board from
        /// its lake recipe: a short stem into the eave, a tin hood, a
        /// bare emissive bulb, a fog halo, and a night-scaled point
        /// light with the day floor of a bulb nobody ever came back
        /// to switch off.
        /// </summary>
        private static void BuildHutDoorLamp(
            Transform parent,
            CitySeacoastLampDescriptor descriptor)
        {
            Transform assembly = new GameObject(
                "Seacoast Hut Door Lamp").transform;
            assembly.SetParent(parent, false);
            assembly.SetPositionAndRotation(
                descriptor.GroundPosition,
                Quaternion.Euler(0f, descriptor.YawDegrees, 0f));

            RuntimePrimitiveFactory.CreateBox(
                "Hut Lamp Stem",
                assembly,
                new Vector3(0f, 2.32f, 0f),
                new Vector3(0.05f, 0.24f, 0.05f),
                LampIronColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Hut Lamp Hood",
                assembly,
                new Vector3(0f, 2.15f, 0f),
                new Vector3(0.24f, 0.07f, 0.24f),
                LampIronColor,
                false);

            Color glow = MultiplyRgb(HutBulbColor, 4.6f, 1f);
            GameObject lens = RuntimePrimitiveFactory.CreateBox(
                "Hut Lamp Bulb",
                assembly,
                new Vector3(0f, 2.01f, 0f),
                new Vector3(0.14f, 0.22f, 0.14f),
                glow,
                CityNightResources.EmissiveMaterial,
                false);
            CityNightGlowRegistry.Register(
                lens.GetComponent<Renderer>(),
                glow);

            GameObject emitter = new GameObject("Hut Lamp Light");
            emitter.transform.SetParent(assembly, false);
            emitter.transform.localPosition =
                new Vector3(0f, 1.99f, 0f);
            Light light = emitter.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = HutBulbColor;
            light.intensity = HutBulbNightIntensity;
            light.range = HutBulbRange;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.lightmapBakeType = LightmapBakeType.Realtime;

            GameObject haloObject = new GameObject("Hut Lamp Halo");
            haloObject.transform.SetParent(emitter.transform, false);
            CityLightHalo halo =
                haloObject.AddComponent<CityLightHalo>();
            halo.Initialize(
                CityNightResources.AtmosphereMaterial,
                0.40f,
                1.10f,
                MultiplyRgb(HutBulbColor, 4.2f, 0.18f),
                MultiplyRgb(HutBulbColor, 2.1f, 0.05f));
            CityNightSiteLightRegistry.Register(
                light,
                HutBulbNightIntensity,
                HutBulbDayIntensity,
                halo);
        }

        /// <summary>
        /// The beacon on the mol-head gallery: an emissive glass block
        /// between the gallery posts on the glow registry (steady — a
        /// lamp room reads lit even mid-occultation), and the real
        /// pulsing light behind it. Floodlight class, ForcePixel so
        /// the water shader's additional-light loop lays its glitter
        /// road, and driven by its own controller rather than the
        /// site registry: the pulse is the point.
        /// </summary>
        private static void BuildBeaconLens(
            Transform parent,
            CitySeacoastLampDescriptor descriptor)
        {
            Transform assembly = new GameObject(
                "Seacoast Beacon Lamp").transform;
            assembly.SetParent(parent, false);
            assembly.SetPositionAndRotation(
                descriptor.GroundPosition,
                Quaternion.Euler(0f, descriptor.YawDegrees, 0f));

            Color glow = MultiplyRgb(BeaconLensColor, 5.2f, 1f);
            GameObject lens = RuntimePrimitiveFactory.CreateBox(
                "Beacon Lens",
                assembly,
                new Vector3(0f, 0.30f, 0f),
                new Vector3(0.46f, 0.54f, 0.46f),
                glow,
                CityNightResources.EmissiveMaterial,
                false);
            CityNightGlowRegistry.Register(
                lens.GetComponent<Renderer>(),
                glow);

            GameObject emitter = new GameObject("Beacon Light");
            emitter.transform.SetParent(assembly, false);
            emitter.transform.localPosition = new Vector3(0f, 0.32f, 0f);
            Light light = emitter.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = BeaconLensColor;
            light.intensity = BeaconNightIntensity;
            light.range = BeaconRange;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.lightmapBakeType = LightmapBakeType.Realtime;

            GameObject haloObject = new GameObject("Beacon Halo");
            haloObject.transform.SetParent(emitter.transform, false);
            CityLightHalo halo =
                haloObject.AddComponent<CityLightHalo>();
            halo.Initialize(
                CityNightResources.AtmosphereMaterial,
                0.60f,
                1.60f,
                MultiplyRgb(BeaconLensColor, 4.0f, 0.20f),
                MultiplyRgb(BeaconLensColor, 2.0f, 0.06f));

            CitySeacoastBeaconController controller =
                assembly.gameObject
                    .AddComponent<CitySeacoastBeaconController>();
            controller.Initialize(light, halo, BeaconNightIntensity);
        }

        private static Color MultiplyRgb(
            Color color,
            float multiplier,
            float alpha)
        {
            return new Color(
                color.r * multiplier,
                color.g * multiplier,
                color.b * multiplier,
                alpha);
        }

        private static Color ResolveColor(CitySeacoastStyle style)
        {
            switch (style)
            {
                case CitySeacoastStyle.Concrete:
                    return Concrete;
                case CitySeacoastStyle.Granite:
                    return Granite;
                case CitySeacoastStyle.Planking:
                    return Planking;
                case CitySeacoastStyle.TarredTimber:
                    return TarredTimber;
                case CitySeacoastStyle.HullPaint:
                    return HullPaint;
                case CitySeacoastStyle.HullTar:
                    return HullTar;
                case CitySeacoastStyle.Iron:
                    return Iron;
                case CitySeacoastStyle.RustIron:
                    return RustIron;
                case CitySeacoastStyle.Grass:
                    return Grass;
                case CitySeacoastStyle.PaintAccent:
                    return PaintAccent;
                case CitySeacoastStyle.Litter:
                    return Litter;
                default:
                    throw new ArgumentOutOfRangeException(nameof(style));
            }
        }

        private readonly struct BatchKey : IEquatable<BatchKey>
        {
            public BatchKey(int x, int z, CitySeacoastStyle style)
            {
                X = x;
                Z = z;
                Style = style;
            }

            public int X { get; }
            public int Z { get; }
            public CitySeacoastStyle Style { get; }

            public bool Equals(BatchKey other)
            {
                return X == other.X &&
                       Z == other.Z &&
                       Style == other.Style;
            }

            public override bool Equals(object obj)
            {
                return obj is BatchKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = X;
                    hash = (hash * 397) ^ Z;
                    return (hash * 397) ^ (int)Style;
                }
            }

            public static int Compare(BatchKey left, BatchKey right)
            {
                int x = left.X.CompareTo(right.X);
                if (x != 0)
                {
                    return x;
                }

                int z = left.Z.CompareTo(right.Z);
                return z != 0 ? z : left.Style.CompareTo(right.Style);
            }
        }
    }

    /// <summary>
    /// The sea's pure geometry: where the sheets go and how the shore
    /// shelves. Split out of the builder so the arithmetic that keeps
    /// the sheets chunked, the apron cosmetic and the surf line inside
    /// the shader's foam reach is testable without a scene.
    /// </summary>
    internal static class CitySeacoastSeaLayout
    {
        /// <summary>
        /// Sheets never exceed a world chunk, so far ones frustum-cull
        /// under the 48 m far plane; world-position waves make the
        /// seams invisible by construction.
        /// </summary>
        internal const float MaximumSheetWidth = 48f;

        /// <summary>
        /// How far each sheet runs past the sea row into the fog. The
        /// far plane sits at 48 m and the fog eats everything long
        /// before that, so the apron only exists to keep the sheet's
        /// north edge out of any legal camera.
        /// </summary>
        internal const float ApronReach = 18f;

        // The shore shelf: silt rising to meet the sand. The inner
        // step sits well inside the foam distance, so the surf line
        // draws the whole shore; the outer step half-fades before the
        // bottom drops away to the deep colour.
        internal const float InnerShelfReach = 2.6f;
        private const float InnerShelfDepth = 0.30f;
        private const float OuterShelfDepth = 0.62f;
        private const float ShelfRunLength = 10f;
        private const float ShelfBottomDrop = 1.9f;

        internal static void CreateSheetRects(
            in CitySeacoastFrame frame,
            ICollection<Rect> destination)
        {
            Rect row = frame.SeaRowBounds;
            int count = Mathf.Max(
                1,
                Mathf.CeilToInt(row.width / MaximumSheetWidth));
            float width = row.width / count;
            for (int index = 0; index < count; index++)
            {
                float from = row.xMin + index * width;
                destination.Add(Rect.MinMaxRect(
                    from,
                    frame.WaterlineZ,
                    from + width,
                    row.yMax + ApronReach));
            }
        }

        internal static void CreateShelfBoxes(
            in CitySeacoastFrame frame,
            ICollection<Bounds> destination)
        {
            Rect row = frame.SeaRowBounds;
            float bottom = frame.SeaTopY - ShelfBottomDrop;
            int runs = Mathf.Max(
                1,
                Mathf.CeilToInt(row.width / ShelfRunLength));
            float runLength = row.width / runs;
            for (int index = 0; index < runs; index++)
            {
                float from = row.xMin + index * runLength;
                float center = from + runLength * 0.5f;
                uint hash = Hash(index);

                // The inner step: ankle-deep, foam over its whole
                // width. Its jitter is what makes the surf line
                // wander instead of ruling itself along the map.
                float innerTop = frame.SeaTopY - InnerShelfDepth +
                                 ((hash & 0xFFu) / 255f - 0.5f) * 0.10f;
                destination.Add(new Bounds(
                    new Vector3(
                        center,
                        (innerTop + bottom) * 0.5f,
                        frame.WaterlineZ + InnerShelfReach * 0.5f),
                    new Vector3(
                        runLength,
                        innerTop - bottom,
                        InnerShelfReach)));

                // The outer step: knee-deep, foam breaking up, gone a
                // few metres further out.
                float reach = 5f +
                              (((hash >> 8) & 0xFFu) / 255f) * 3.5f;
                float outerTop = frame.SeaTopY - OuterShelfDepth +
                                 (((hash >> 16) & 0xFFu) / 255f - 0.5f) *
                                 0.16f;
                destination.Add(new Bounds(
                    new Vector3(
                        center,
                        (outerTop + bottom) * 0.5f,
                        frame.WaterlineZ + InnerShelfReach +
                            (reach - InnerShelfReach) * 0.5f),
                    new Vector3(
                        runLength,
                        outerTop - bottom,
                        reach - InnerShelfReach)));
            }
        }

        /// <summary>
        /// Positional, not seeded: the shelf wander is texture, and a
        /// different city does not need a different shore any more
        /// than it needs a different ripple sheet.
        /// </summary>
        private static uint Hash(int index)
        {
            unchecked
            {
                uint value = 0x53454142u ^ (uint)index; // "SEAB"
                value *= 16777619u;
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                return value;
            }
        }
    }
}
