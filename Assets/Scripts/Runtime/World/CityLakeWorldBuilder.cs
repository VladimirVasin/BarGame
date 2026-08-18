using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Materialises a <see cref="CityLakePlan"/>: the still-water sheet
    /// over its cut-cornered footprint, the bank ring that falls to it
    /// and the bed cap beneath it, then the dressing - parts batch into
    /// one combined oriented-box mesh per 48 m chunk and style (plank,
    /// bank and hull batches carry world-planar UVs and a lake sheet),
    /// and each lamp descriptor becomes a real fixture with an emissive
    /// lens, a fog halo and a night-scaled point light on the shared
    /// registries.
    ///
    /// The lamps are not decoration here in a way they are nowhere else
    /// in the city: the water shader reads URP additional lights, so the
    /// hand lamp left standing on the rail at the end of the pier is
    /// what puts anything at all on the pond. The shore carries none,
    /// and that lamp never switches off.
    /// </summary>
    public static class CityLakeWorldBuilder
    {
        public const string RootName = "Lake Boat Station";
        private const float SpatialChunkSize = 48f;

        // The flat palette. The textured styles are transcribed into
        // tools/build-city-lake-textures.py, which solves the albedo
        // compensation that keeps the textured product at this exact
        // brightness — edit them together.
        internal static readonly Color BankClay =
            new Color(0.24f, 0.22f, 0.17f);
        internal static readonly Color Planking =
            new Color(0.31f, 0.28f, 0.24f);
        internal static readonly Color TarredTimber =
            new Color(0.16f, 0.14f, 0.12f);
        internal static readonly Color HullPaint =
            new Color(0.26f, 0.32f, 0.31f);
        internal static readonly Color HullTar =
            new Color(0.13f, 0.12f, 0.11f);
        internal static readonly Color Concrete =
            new Color(0.29f, 0.29f, 0.27f);
        internal static readonly Color Iron =
            new Color(0.075f, 0.085f, 0.085f);
        internal static readonly Color Reeds =
            new Color(0.23f, 0.30f, 0.15f);
        internal static readonly Color LakeStone =
            new Color(0.24f, 0.26f, 0.24f);
        // The one legible colour on the whole plot: what is left of the
        // paint on the hire board.
        internal static readonly Color PaintAccent =
            new Color(0.52f, 0.44f, 0.20f);
        internal static readonly Color Litter =
            new Color(0.27f, 0.22f, 0.16f);

        // The silt under the water. Darker than the bank it continues,
        // because a metre of pond water is what is between them.
        internal static readonly Color Silt =
            new Color(0.13f, 0.13f, 0.11f);

        // The hand lamp at the end of the boards: a kerosene-warm
        // filament, well to the orange side of the hut bulb. Warm light
        // is what shows on dark water, and this is a lamp somebody
        // carried out and set down, not a fitting the town installed.
        internal static readonly Color HeadLampColor =
            new Color(1.00f, 0.74f, 0.42f);

        // It never goes out. The station is abandoned, but this lamp is
        // still burning, and the day floor is what makes that legible:
        // "always working" has to read at noon too, not merely survive
        // the night factor.
        //
        // Sized as an object rather than as infrastructure. A municipal
        // post out here would say the pier is still maintained, which is
        // the opposite of true, so this throws a close pool over the end
        // of the deck and the water beside it and no further.
        internal const float HeadLampNightIntensity = 46f;
        internal const float HeadLampDayIntensity = 16f;

        // Still far enough to reach the water it stands over. The water
        // shader's additional-light glint is a lit highlight, not a
        // mirror, so it exists only where the fixture's own range covers
        // the surface: a short-ranged lamp puts no reflection on the
        // water at all, however bright it is. This number and ForcePixel
        // below are load bearing, not art direction.
        internal const float HeadLampRange = 11.0f;

        // Height of the flame above the rail cap it is standing on.
        internal const float HeadLampHeight = 0.13f;

        // The hut bulb is the same value as the cemetery lodge's porch
        // bulb on purpose: it is the same kind of bulb over the same
        // kind of door, and inventing a second warm tungsten for it
        // would be dishonest. Slightly weaker, because the hut is
        // smaller and nobody stands under it.
        internal static readonly Color HutBulbColor =
            new Color(1.00f, 0.80f, 0.55f);
        internal const float HutBulbNightIntensity = 64f;
        internal const float HutBulbDayIntensity = 14f;
        internal const float HutBulbRange = 7.0f;

        private static readonly Color LampIronColor =
            new Color(0.070f, 0.075f, 0.075f);

        public static GameObject Build(
            Transform parent,
            CityLakePlan plan)
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

            BuildBasin(root, plan.Basin);
            BuildDressing(root, plan);

            for (int index = 0; index < plan.Lamps.Count; index++)
            {
                CityLakeLampDescriptor lamp = plan.Lamps[index];
                if (lamp.Kind == CityLakeLampKind.HutDoor)
                {
                    BuildHutDoorLamp(root, lamp);
                }
                else
                {
                    BuildPierHeadLamp(root, lamp);
                }
            }

            return root.gameObject;
        }

        /// <summary>
        /// The bed, then the bank, then the water over both. Order is
        /// only for the hierarchy's readability; the water writes depth
        /// and sorts itself.
        /// </summary>
        private static void BuildBasin(
            Transform root,
            in CityLakeBasin basin)
        {
            CityLakeBankMeshFactory.CreateBedCap(
                "Lake Bed",
                root,
                basin,
                Silt);
            CityLakeBankMeshFactory.CreateBankRing(
                "Lake Bank",
                root,
                basin,
                BankClay);

            // The waterline sits a hair inside the revetment's inner
            // face, so the boards are always seen standing in water
            // rather than beside it.
            Rect waterline = basin.WaterlineBounds;
            CityWaterSurfaceFactory.CreateBevelledSurface(
                "Lake Water",
                root,
                waterline,
                basin.BevelMeters,
                basin.WaterTopY,
                CityLakeResources.WaterMaterial);
        }

        private static void BuildDressing(Transform root, CityLakePlan plan)
        {
            var batches =
                new Dictionary<BatchKey, List<RuntimeOrientedBox>>();
            for (int index = 0; index < plan.Parts.Count; index++)
            {
                CityLakePartDescriptor part = plan.Parts[index];
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
                CityLakeSurfaceKind? surface = ResolveSurface(key.Style);
                float? uvTileSize = surface.HasValue
                    ? CityLakeSurfaceAppearance
                        .GetRecipe(surface.Value).MetersPerTile
                    : (float?)null;
                GameObject chunk =
                    RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                        $"Lake Chunk {key.X} {key.Z} {key.Style}",
                        root,
                        batches[key],
                        ResolveColor(key.Style),
                        CityLakeRules.BlocksMovement(key.Style),
                        uvTileSize);
                if (surface.HasValue)
                {
                    CityLakeSurfaceAppearance.ApplyCombined(
                        chunk.GetComponent<Renderer>(),
                        surface.Value,
                        ResolveColor(key.Style));
                }
            }
        }

        /// <summary>
        /// The hand lamp standing on the rail cap at the end of the
        /// pier: a small tin body, a warm glass, a wire bail over the
        /// top, and the point light that is the only thing lighting the
        /// water.
        ///
        /// Everything about it is object-scale. It is roughly a
        /// forearm tall and it sits where a person would have put it
        /// down, at hand height on the rail, so it reads as something
        /// left behind rather than as a fitting the town installed and
        /// still maintains.
        ///
        /// Two deliberate registry choices. The light goes on the site
        /// registry through the day-floor overload, so it burns around
        /// the clock. The glass stays OUT of the glow registry: that
        /// registry lerps every registered emissive down to a tenth of
        /// itself under a day sky, which is right for a lamp that
        /// switches off and wrong for one that does not - the light
        /// would still be thrown while the lamp itself looked dead. The
        /// always-on yard spotlight sits outside it for the same reason.
        ///
        /// No collider: it stands on a rail at the end of a deck barely
        /// two metres wide, and a collider here would be something the
        /// player has to squeeze past to reach the end.
        /// </summary>
        private static void BuildPierHeadLamp(
            Transform parent,
            CityLakeLampDescriptor descriptor)
        {
            Transform assembly = new GameObject(
                "Lake Pier Hand Lamp").transform;
            assembly.SetParent(parent, false);
            assembly.SetPositionAndRotation(
                descriptor.GroundPosition,
                Quaternion.Euler(0f, descriptor.YawDegrees, 0f));

            // The foot it stands on, and the tin cap over the glass.
            RuntimePrimitiveFactory.CreateBox(
                "Hand Lamp Foot",
                assembly,
                new Vector3(0f, 0.02f, 0f),
                new Vector3(0.13f, 0.04f, 0.13f),
                LampIronColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Hand Lamp Cap",
                assembly,
                new Vector3(0f, 0.215f, 0f),
                new Vector3(0.14f, 0.04f, 0.14f),
                LampIronColor,
                false);

            // The bail: two uprights and a cross piece, so the lamp has
            // the one silhouette that says "carried" at this size.
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                RuntimePrimitiveFactory.CreateBox(
                    $"Hand Lamp Bail {side}",
                    assembly,
                    new Vector3(sign * 0.055f, 0.262f, 0f),
                    new Vector3(0.012f, 0.07f, 0.012f),
                    LampIronColor,
                    false);
            }

            RuntimePrimitiveFactory.CreateBox(
                "Hand Lamp Handle",
                assembly,
                new Vector3(0f, 0.292f, 0f),
                new Vector3(0.122f, 0.012f, 0.012f),
                LampIronColor,
                false);

            // A low multiplier on purpose, and the number is set by the
            // blue channel rather than by taste.
            //
            // The street fixtures push their lens hard because they are
            // read from across a district. This one is read from two
            // metres, and the moment the multiplier lifts blue past 1
            // all three channels clip together and the amber glass
            // becomes a white chip - the cast light stays warm while the
            // lamp making it looks like a fluorescent tube. At 1.5x this
            // colour's blue lands at 0.63, so red and green saturate
            // into a hot core while the glass keeps its hue.
            Color glow = MultiplyRgb(HeadLampColor, 1.5f, 1f);
            RuntimePrimitiveFactory.CreateBox(
                "Hand Lamp Glass",
                assembly,
                new Vector3(0f, HeadLampHeight, 0f),
                new Vector3(0.095f, 0.145f, 0.095f),
                glow,
                CityNightResources.EmissiveMaterial,
                false);

            GameObject emitter = new GameObject("Hand Lamp Light");
            emitter.transform.SetParent(assembly, false);
            emitter.transform.localPosition =
                new Vector3(0f, HeadLampHeight, 0f);
            Light light = emitter.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = HeadLampColor;
            light.intensity = HeadLampNightIntensity;
            light.range = HeadLampRange;
            light.shadows = LightShadows.None;

            // ForcePixel is not a quality setting here: a vertex-mode
            // light is not an additional light the water shader can
            // read, and the reflection is the whole point of the lamp.
            light.renderMode = LightRenderMode.ForcePixel;
            light.lightmapBakeType = LightmapBakeType.Realtime;

            GameObject haloObject = new GameObject("Hand Lamp Halo");
            haloObject.transform.SetParent(emitter.transform, false);
            CityLightHalo halo =
                haloObject.AddComponent<CityLightHalo>();
            halo.Initialize(
                CityNightResources.AtmosphereMaterial,
                0.26f,
                0.80f,
                MultiplyRgb(HeadLampColor, 1.9f, 0.20f),
                MultiplyRgb(HeadLampColor, 1.1f, 0.06f));
            CityNightSiteLightRegistry.Register(
                light,
                HeadLampNightIntensity,
                HeadLampDayIntensity,
                halo);
        }

        /// <summary>
        /// The rental hut's door bulb: a short stem into the eave, a tin
        /// hood and a bare emissive bulb. Burns around the clock -
        /// dimmed by day, full at night - because it is the second
        /// fixture in the city nobody ever came back to switch off.
        /// </summary>
        private static void BuildHutDoorLamp(
            Transform parent,
            CityLakeLampDescriptor descriptor)
        {
            Transform assembly = new GameObject(
                "Lake Hut Door Lamp").transform;
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
            emitter.transform.localPosition = new Vector3(0f, 1.99f, 0f);
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

        private static CityLakeSurfaceKind? ResolveSurface(
            CityLakeStyle style)
        {
            switch (style)
            {
                case CityLakeStyle.Planking:
                case CityLakeStyle.TarredTimber:
                    return CityLakeSurfaceKind.Plank;
                case CityLakeStyle.BankClay:
                // The boulders and the lost tackle take the bank sheet
                // too. Left flat they read as bright blocks dropped on
                // a textured shore rather than as things lying on it.
                case CityLakeStyle.LakeStone:
                case CityLakeStyle.Litter:
                    return CityLakeSurfaceKind.Bank;
                case CityLakeStyle.HullPaint:
                case CityLakeStyle.HullTar:
                    return CityLakeSurfaceKind.Hull;
                default:
                    // Iron, concrete, reeds, stone, sign paint and
                    // litter stay flat colour: their members are too
                    // thin, or too small, for a sheet to read through
                    // the PS1 composite.
                    return null;
            }
        }

        private static Color ResolveColor(CityLakeStyle style)
        {
            switch (style)
            {
                case CityLakeStyle.BankClay:
                    return BankClay;
                case CityLakeStyle.Planking:
                    return Planking;
                case CityLakeStyle.TarredTimber:
                    return TarredTimber;
                case CityLakeStyle.HullPaint:
                    return HullPaint;
                case CityLakeStyle.HullTar:
                    return HullTar;
                case CityLakeStyle.Concrete:
                    return Concrete;
                case CityLakeStyle.Iron:
                    return Iron;
                case CityLakeStyle.Reeds:
                    return Reeds;
                case CityLakeStyle.LakeStone:
                    return LakeStone;
                case CityLakeStyle.PaintAccent:
                    return PaintAccent;
                case CityLakeStyle.Litter:
                    return Litter;
                default:
                    throw new ArgumentOutOfRangeException(nameof(style));
            }
        }

        private readonly struct BatchKey : IEquatable<BatchKey>
        {
            public BatchKey(int x, int z, CityLakeStyle style)
            {
                X = x;
                Z = z;
                Style = style;
            }

            public int X { get; }
            public int Z { get; }
            public CityLakeStyle Style { get; }

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
}
