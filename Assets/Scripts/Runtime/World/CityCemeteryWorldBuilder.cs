using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Materialises a <see cref="CityCemeteryPlan"/>: parts batch into
    /// one combined oriented-box mesh per 48 m chunk and style (stone,
    /// gravel and soil batches carry world-planar UVs and a cemetery
    /// sheet), and each lamp descriptor becomes a cast-iron fixture
    /// with an emissive lens, a fog halo and a night-scaled point
    /// light that dies by day through the shared registries.
    /// </summary>
    public static class CityCemeteryWorldBuilder
    {
        public const string RootName = "Cemetery Landmarks";
        private const float SpatialChunkSize = 48f;

        // The flat palette. The textured styles are transcribed into
        // tools/build-cemetery-textures.py, which solves the albedo
        // compensation that keeps the textured product at this exact
        // brightness — edit them together.
        internal static readonly Color Gravel =
            new Color(0.30f, 0.28f, 0.23f);
        internal static readonly Color Iron =
            new Color(0.07f, 0.09f, 0.09f);
        internal static readonly Color GraniteDark =
            new Color(0.21f, 0.22f, 0.24f);
        internal static readonly Color MarbleLight =
            new Color(0.44f, 0.44f, 0.41f);
        internal static readonly Color WeatheredConcrete =
            new Color(0.30f, 0.31f, 0.28f);
        internal static readonly Color Soil =
            new Color(0.16f, 0.13f, 0.09f);
        internal static readonly Color TrunkDark =
            new Color(0.12f, 0.09f, 0.06f);
        internal static readonly Color TrunkBirch =
            new Color(0.55f, 0.55f, 0.50f);
        internal static readonly Color FoliageDark =
            new Color(0.09f, 0.16f, 0.10f);
        // One muted warm note per plot: wreaths and plastic flowers.
        internal static readonly Color Flowers =
            new Color(0.30f, 0.16f, 0.18f);
        // Bench planks: sun-bleached painted timber, repainted less
        // often than anyone would like.
        internal static readonly Color Timber =
            new Color(0.26f, 0.20f, 0.14f);

        // A cold gas-mantle green, dimmer than street practicals: the
        // cemetery should glow, not shine.
        internal static readonly Color LampLightColor =
            new Color(0.70f, 0.76f, 0.66f);
        internal const float LampNightIntensity = 42f;
        internal const float LampRange = 9.5f;
        private static readonly Color LampIronColor =
            new Color(0.060f, 0.070f, 0.070f);

        public static GameObject Build(
            Transform parent,
            CityCemeteryPlan plan)
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

            var batches =
                new Dictionary<BatchKey, List<RuntimeOrientedBox>>();
            for (int index = 0; index < plan.Parts.Count; index++)
            {
                CityCemeteryPartDescriptor part = plan.Parts[index];
                var key = new BatchKey(
                    Mathf.FloorToInt(
                        part.Center.x / SpatialChunkSize),
                    Mathf.FloorToInt(
                        part.Center.z / SpatialChunkSize),
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
                CityCemeterySurfaceKind? surface =
                    ResolveSurface(key.Style);
                float? uvTileSize = surface.HasValue
                    ? CityCemeterySurfaceAppearance
                        .GetRecipe(surface.Value).MetersPerTile
                    : (float?)null;
                GameObject chunk =
                    RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                        $"Cemetery Chunk {key.X} {key.Z} {key.Style}",
                        root,
                        batches[key],
                        ResolveColor(key.Style),
                        CityCemeteryRules.BlocksMovement(key.Style),
                        uvTileSize);
                if (surface.HasValue)
                {
                    CityCemeterySurfaceAppearance.ApplyCombined(
                        chunk.GetComponent<Renderer>(),
                        surface.Value,
                        ResolveColor(key.Style));
                }
            }

            for (int index = 0; index < plan.Lamps.Count; index++)
            {
                BuildAlleyLamp(root, plan.Lamps[index], index);
            }

            return root.gameObject;
        }

        /// <summary>
        /// One alley lamp: iron base, pole, a caged head with an
        /// emissive mantle lens, and the night-scaled point light.
        /// The pole carries the only collider; the head is overhead.
        /// </summary>
        private static void BuildAlleyLamp(
            Transform parent,
            CityCemeteryLampDescriptor descriptor,
            int index)
        {
            Transform assembly = new GameObject(
                $"Cemetery Lamp {index}").transform;
            assembly.SetParent(parent, false);
            assembly.SetPositionAndRotation(
                descriptor.GroundPosition,
                Quaternion.Euler(0f, descriptor.YawDegrees, 0f));

            RuntimePrimitiveFactory.CreateBox(
                "Lamp Base",
                assembly,
                new Vector3(0f, 0.225f, 0f),
                new Vector3(0.26f, 0.45f, 0.26f),
                LampIronColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Lamp Pole",
                assembly,
                new Vector3(0f, 1.625f, 0f),
                new Vector3(0.11f, 2.35f, 0.11f),
                LampIronColor,
                true);
            RuntimePrimitiveFactory.CreateBox(
                "Lamp Head",
                assembly,
                new Vector3(0f, 2.99f, 0f),
                new Vector3(0.30f, 0.38f, 0.30f),
                LampIronColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Lamp Cap",
                assembly,
                new Vector3(0f, 3.22f, 0f),
                new Vector3(0.38f, 0.08f, 0.38f),
                LampIronColor,
                false);

            Color glow = MultiplyRgb(LampLightColor, 4.6f, 1f);
            GameObject lens = RuntimePrimitiveFactory.CreateBox(
                "Lamp Mantle",
                assembly,
                new Vector3(0f, 2.97f, 0f),
                new Vector3(0.20f, 0.26f, 0.20f),
                glow,
                CityNightResources.EmissiveMaterial,
                false);
            CityNightGlowRegistry.Register(
                lens.GetComponent<Renderer>(),
                glow);

            GameObject emitter = new GameObject("Cemetery Lamp Light");
            emitter.transform.SetParent(assembly, false);
            emitter.transform.localPosition = new Vector3(0f, 2.97f, 0f);
            Light light = emitter.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = LampLightColor;
            light.intensity = LampNightIntensity;
            light.range = LampRange;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.lightmapBakeType = LightmapBakeType.Realtime;

            GameObject haloObject = new GameObject(
                "Cemetery Lamp Halo");
            haloObject.transform.SetParent(emitter.transform, false);
            CityLightHalo halo =
                haloObject.AddComponent<CityLightHalo>();
            halo.Initialize(
                CityNightResources.AtmosphereMaterial,
                0.55f,
                1.55f,
                MultiplyRgb(LampLightColor, 4.2f, 0.18f),
                MultiplyRgb(LampLightColor, 2.1f, 0.05f));
            CityNightSiteLightRegistry.Register(
                light,
                LampNightIntensity,
                halo);
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

        private static CityCemeterySurfaceKind? ResolveSurface(
            CityCemeteryStyle style)
        {
            switch (style)
            {
                case CityCemeteryStyle.Gravel:
                    return CityCemeterySurfaceKind.Gravel;
                case CityCemeteryStyle.Soil:
                    return CityCemeterySurfaceKind.Soil;
                case CityCemeteryStyle.GraniteDark:
                case CityCemeteryStyle.MarbleLight:
                    return CityCemeterySurfaceKind.Granite;
                case CityCemeteryStyle.WeatheredConcrete:
                    return CityCemeterySurfaceKind.Stone;
                default:
                    // Iron rails, trunks, crowns, flowers and bench
                    // planks stay flat colour: their members are too
                    // thin for a sheet to read through the PS1
                    // composite.
                    return null;
            }
        }

        private static Color ResolveColor(CityCemeteryStyle style)
        {
            switch (style)
            {
                case CityCemeteryStyle.Gravel:
                    return Gravel;
                case CityCemeteryStyle.Iron:
                    return Iron;
                case CityCemeteryStyle.GraniteDark:
                    return GraniteDark;
                case CityCemeteryStyle.MarbleLight:
                    return MarbleLight;
                case CityCemeteryStyle.WeatheredConcrete:
                    return WeatheredConcrete;
                case CityCemeteryStyle.Soil:
                    return Soil;
                case CityCemeteryStyle.TrunkDark:
                    return TrunkDark;
                case CityCemeteryStyle.TrunkBirch:
                    return TrunkBirch;
                case CityCemeteryStyle.FoliageDark:
                    return FoliageDark;
                case CityCemeteryStyle.Flowers:
                    return Flowers;
                case CityCemeteryStyle.Timber:
                    return Timber;
                default:
                    throw new ArgumentOutOfRangeException(nameof(style));
            }
        }

        private readonly struct BatchKey : IEquatable<BatchKey>
        {
            public BatchKey(int x, int z, CityCemeteryStyle style)
            {
                X = x;
                Z = z;
                Style = style;
            }

            public int X { get; }
            public int Z { get; }
            public CityCemeteryStyle Style { get; }

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
                return z != 0
                    ? z
                    : left.Style.CompareTo(right.Style);
            }
        }
    }
}
