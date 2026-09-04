using System;
using System.Collections.Generic;
using BarPromenade.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    /// <summary>
    /// The church's own light. Two layers that never blend into one
    /// wash: a warm candle layer the room is lived in by, and a cold
    /// daylight layer that only exists while the sun is up and only
    /// enters through the ten aisle lancets the model actually draws.
    /// <see cref="ChurchInteriorDayNightController"/> owns the balance
    /// between them; this class owns where they are.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChurchInteriorAtmosphere : MonoBehaviour
    {
        public const string RootName = "Church Interior Atmosphere";

        /// <summary>
        /// XZ of INT_StainedGlass in the authored interior: five lancets
        /// a side at x +/- 11.04, their glass spanning y 3.85 to 6.95.
        /// One shaft each, or the daylight arrives from nowhere.
        /// </summary>
        public static readonly float[] WindowDepths =
        {
            -11f,
            -6f,
            0f,
            6f,
            11f
        };

        public const float WindowWallX = 11.04f;
        public const float WindowCenterY = 5.4f;
        /// <summary>Ten lancets, five a side.</summary>
        public const int DaylightShaftCount = 10;
        public const int DaylightGlowCount = 10;
        public const int WarmPracticalCount = 16;
        public const int PracticalLightCount =
            WarmPracticalCount + DaylightGlowCount;

        public const float WarmRange = 9.5f;

        /// <summary>
        /// The votive stands' candle ring, mirroring
        /// `votive_candle_xy` and `VOTIVE_FLAME_HEIGHT` in
        /// tools/build-church-3d-model.py. The model authors the wax
        /// and the stand; the FLAMES are built here because they are
        /// the one part of that fixture that has to move, and a merged
        /// mesh of thirty-two of them cannot.
        /// </summary>
        public const int VotiveCandleCount = 16;
        public const float VotiveClusterRadius = 0.28f;
        public const float VotiveFlameHeight = 1.28f;

        public static readonly Vector2[] VotiveStandCentres =
        {
            new Vector2(-8.8f, 10.5f),
            new Vector2(8.8f, 10.5f)
        };

        /// <summary>
        /// Three concentric rings, offset so no two candles line up.
        /// </summary>
        public static Vector3 VotiveFlamePosition(
            Vector2 centre,
            int index)
        {
            int ring = index % 3;
            float angle =
                (Mathf.PI * 2f * index / VotiveCandleCount) +
                (ring * 0.31f);
            float distance =
                VotiveClusterRadius * (0.3f + (ring * 0.32f));
            return new Vector3(
                centre.x + (Mathf.Cos(angle) * distance),
                VotiveFlameHeight,
                centre.y + (Mathf.Sin(angle) * distance));
        }
        /// <summary>
        /// Mid-thickness of the outer wall leaf: the plane the aperture
        /// actually cuts through, and where a light entering the
        /// building has to start from if the jambs are to clip it.
        /// </summary>
        public const float ShaftApertureX = 11.33f;

        /// <summary>
        /// The opening itself, mirroring LANCET_APERTURE_* in the model
        /// generator. The beams are cut to it, so a column of light is
        /// the shape of the window it came through.
        /// </summary>
        public const float ApertureWidth = 1.25f;
        public const float ApertureHeight = 3.1f;

        /// <summary>
        /// The shaded aisle's own daylight. A north window never passes
        /// SUN, but it passes SKY all day, and sky arrives soft and
        /// from everywhere - which is what these points are: a small
        /// diffuse source at each shaded lancet, throwing no shaft and
        /// no pool, just lifting its own reveal and the wall around it
        /// out of the dark. At 0.24 that aisle read as unlit masonry
        /// with black slots in it.
        /// </summary>
        public const float ShadedGlowScale = 0.45f;

        // There used to be ten SPOT lights here as well, one per
        // lancet, and they are gone. They existed to stand in for a sun
        // that could not get into the building; now that the shell is
        // sealed and the ten apertures are real, the sun does its own
        // work, and it is a parallel source - it delivers the same
        // light at three metres and at thirteen. A spot cannot: sized
        // by `illuminance x distance squared` for the pool it aims at,
        // it necessarily blasts everything nearer, which at a low
        // morning sun means the whole aisle wall. The one thing they
        // were still wanted for - the COLOUR the light picked up
        // passing through coloured glass - is now carried by the
        // directional's own tint, which is honest here precisely
        // because every ray in this room came through a window.

        /// <summary>
        /// A real window does two things at once and needs a light for
        /// each: the cone is the pool it throws on the floor, and this
        /// is the glass itself lighting its own reveal and the wall
        /// around it. With only the cone the aisle wall stays dead and
        /// the daylight seems to come from nowhere.
        /// </summary>
        public const float GlowInsetFromWall = 1.7f;
        public const float GlowRange = 8f;

        public static readonly Color CandleColor =
            new Color(1f, 0.60f, 0.28f);
        public static readonly Color CandleFlameColor =
            new Color(1.35f, 0.62f, 0.16f);
        public static readonly Color StainedGlassColor =
            new Color(0.56f, 0.73f, 0.95f);

        private static readonly Color SconceIron =
            new Color(0.11f, 0.115f, 0.11f);
        private static readonly Color SconceWax =
            new Color(0.86f, 0.83f, 0.74f);

        /// <summary>
        /// The warm layer, in build order: two votive stands, two high
        /// altar lights, the altar candles, four aisle sconces, two
        /// narthex sconces and the font candle. Every one of them sits
        /// on a fixture the model draws.
        /// </summary>
        private static readonly (string Name, Vector3 Position,
            float Intensity, float Range)[] WarmPracticals =
        {
            ("Votive Stand Light North",
                new Vector3(-8.8f, 1.32f, 10.5f), 1.45f, 7.5f),
            ("Votive Stand Light South",
                new Vector3(8.8f, 1.32f, 10.5f), 1.45f, 7.5f),
            ("High Altar Light North",
                new Vector3(-1.8f, 2.65f, 17.2f), 1.30f, 8f),
            ("High Altar Light South",
                new Vector3(1.8f, 2.65f, 17.2f), 1.30f, 8f),
            ("Altar Candle Light",
                new Vector3(0f, 1.42f, 15.3f), 1.05f, 6f),
            ("Font Candle Light",
                new Vector3(-8.8f, 1.38f, -16.8f), 0.95f, 6f)
        };

        /// <summary>
        /// The coronas: rings of candles hung over the centre line,
        /// the fixture a nave of this size is actually lit by. The
        /// sconces are on the aisle walls eleven metres from the middle
        /// of the room, so with only those the whole centre - the main
        /// aisle, the pews, the door the hero comes in by and the hero
        /// himself - fell into one black hole no matter how bright the
        /// walls were.
        /// </summary>
/// <summary>
        /// ChainTop is what each corona actually hangs FROM, and it is
        /// per fixture because the ceiling over them is not one plane:
        /// the vault ridge stands at y 14 over the centre line, while
        /// the narthex corona hangs under the choir loft slab at 4.4.
        /// One shared height left every chain stopping in mid air.
        /// </summary>
        private static readonly (string Name, float Depth, float Height,
            float Radius, float Intensity, float Range,
            float ChainTop)[] Coronas =
        {
            ("Narthex Corona", -18.2f, 3.55f, 0.85f, 12f, 12f, 4.4f),
            ("Nave Corona West", -12f, 6f, 1.1f, 26f, 16f, 13.9f),
            ("Nave Corona East", -6.5f, 6f, 1.1f, 26f, 16f, 13.9f),
            ("Crossing Corona", 1.5f, 6f, 1.1f, 26f, 16f, 13.9f)
        };

        /// <summary>
        /// Wall sconces: the ones that carry the walk. Four down the
        /// aisles between the Stations of the Cross, two in the narthex
        /// where the hero arrives under the choir loft — which had no
        /// light of any kind and is where he opens his eyes.
        /// </summary>
        private static readonly (string Name, float Side, float Depth,
            float Height, float Intensity)[] Sconces =
        {
            ("Aisle Sconce North Nave", -1f, -8f, 3.1f, 1.75f),
            ("Aisle Sconce South Nave", 1f, -8f, 3.1f, 1.75f),
            ("Aisle Sconce North Crossing", -1f, 2.5f, 3.1f, 1.75f),
            ("Aisle Sconce South Crossing", 1f, 2.5f, 3.1f, 1.75f),
            ("Narthex Sconce North", -1f, -17.4f, 2.75f, 1.9f),
            ("Narthex Sconce South", 1f, -17.4f, 2.75f, 1.9f)
        };

        public Light[] Practicals { get; private set; } =
            Array.Empty<Light>();

        /// <summary>The warm layer, brightest after dark.</summary>
        public Light[] WarmLights { get; private set; } =
            Array.Empty<Light>();

        /// <summary>
        /// One per warm fixture, in the same order. The day and night
        /// schedule writes their BaseIntensity; they own the light's
        /// own intensity from frame to frame.
        /// </summary>
        public ChurchCandleFlame[] CandleFlames { get; private set; } =
            Array.Empty<ChurchCandleFlame>();

        /// <summary>The glass lighting its own reveal.</summary>
        public Light[] DaylightGlows { get; private set; } =
            Array.Empty<Light>();

        public Volume PostProcessVolume { get; private set; }
        public VolumeProfile RuntimeProfile { get; private set; }

        /// <summary>The two aisles' glazing, north first.</summary>
        public Renderer[] StainedGlass { get; private set; } =
            Array.Empty<Renderer>();

        /// <summary>
        /// The columns of light themselves, one per lancet. Additive
        /// geometry, not lights: the pool on the floor is the real
        /// sun's business, and this is the air it crosses to get there.
        /// </summary>
        public ChurchLightShaft[] LightShafts { get; private set; } =
            Array.Empty<ChurchLightShaft>();

        /// <summary>
        /// The whole daylight layer at one position of the sun.
        ///
        /// Every window is asked the same question - does the sun
        /// actually reach you - and everything else follows from the
        /// answer. This replaces a version that wrote one intensity to
        /// all twenty lights at once, so both aisles blazed equally at
        /// every hour whatever the sky was doing.
        /// </summary>
        public void ApplyDaylight(
            Color color,
            float dayFactor,
            float glowIntensity)
        {
            for (int index = 0; index < DaylightGlows.Length; index++)
            {
                Light glow = DaylightGlows[index];
                if (glow == null)
                {
                    continue;
                }

                float side = Mathf.Sign(glow.transform.localPosition.x);
                float weight = ChurchInteriorSunRules.WindowWeight(
                    side,
                    dayFactor);
                glow.color = color;
                glow.intensity = glowIntensity *
                    Mathf.Lerp(ShadedGlowScale, 1f, weight);
            }

        }

        /// <summary>
        /// The visible columns, re-solved every FRAME rather than every
        /// game minute.
        ///
        /// They are cheap - four vertex writes and a normal recompute
        /// on eight vertices each - and they have to be, because the
        /// sun's own pose is continuous. Left on the minute gate with
        /// everything else, the shadows would glide while the beams
        /// standing in them jumped a quarter degree once a second,
        /// which on a long bright column in a dark room is exactly the
        /// kind of thing an eye finds. One owner, one cadence.
        /// </summary>
        public void ApplyBeams(Color color, float dayFactor)
        {
            for (int index = 0; index < LightShafts.Length; index++)
            {
                ChurchLightShaft beam = LightShafts[index];
                if (beam == null)
                {
                    continue;
                }

                beam.Apply(
                    color,
                    ChurchInteriorSunRules.WindowWeight(
                        beam.WallSide,
                        dayFactor));
            }
        }

        /// <summary>
        /// The panes themselves. They are opaque unlit HDR, so nothing
        /// lights them and they showed the same flat cyan at three in
        /// the morning as at noon; the sunlit wall of a church is the
        /// brightest thing in the building and the shaded one is not.
        /// Written through the shared property block, so no material is
        /// instanced and nothing leaks.
        /// </summary>
        public void ApplyStainedGlass(
            Color tint,
            float dayFactor,
            float nightGain,
            float shadedGain,
            float sunlitGain)
        {
            for (int index = 0; index < StainedGlass.Length; index++)
            {
                Renderer pane = StainedGlass[index];
                if (pane == null)
                {
                    continue;
                }

                float side = index == 0
                    ? ChurchInteriorSunRules.NorthWallSide
                    : ChurchInteriorSunRules.SouthWallSide;
                float weight = ChurchInteriorSunRules.WindowWeight(
                    side,
                    dayFactor);
                float gain = Mathf.Lerp(
                    nightGain,
                    Mathf.Lerp(shadedGain, sunlitGain, weight),
                    Mathf.Clamp01(dayFactor));
                RuntimePrimitiveFactory.SetColor(
                    pane,
                    new Color(
                        tint.r * gain,
                        tint.g * gain,
                        tint.b * gain,
                        1f));
            }
        }


        public static ChurchInteriorAtmosphere Install(
            Transform parent,
            ChurchInteriorLayoutPlan plan,
            ChurchAssetRegistry registry = null)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            var root = new GameObject(RootName);
            root.transform.SetParent(parent, false);
            var atmosphere = root.AddComponent<
                ChurchInteriorAtmosphere>();
            atmosphere.WarmLights = BuildWarmLayer(root.transform);
            var drivers =
                new ChurchCandleFlame[atmosphere.WarmLights.Length];
            for (int index = 0; index < drivers.Length; index++)
            {
                drivers[index] = atmosphere.WarmLights[index]
                    .GetComponent<ChurchCandleFlame>();
            }

            atmosphere.CandleFlames = drivers;
            atmosphere.DaylightGlows = BuildDaylightGlows(
                root.transform);

            var all = new Light[PracticalLightCount];
            atmosphere.WarmLights.CopyTo(all, 0);
            atmosphere.DaylightGlows.CopyTo(all, WarmPracticalCount);
            atmosphere.Practicals = all;
            atmosphere.StainedGlass = BindStainedGlass(registry);
            atmosphere.LightShafts = BuildLightShafts(root.transform);
            atmosphere.CreatePostProcessVolume();
            return atmosphere;
        }

        /// <summary>
        /// North pane first, then south. The model splits the glazing
        /// by side for exactly one reason: the side is the only thing
        /// about a lancet that changes through the day.
        /// </summary>
        private static Renderer[] BindStainedGlass(
            ChurchAssetRegistry registry)
        {
            if (registry == null)
            {
                return Array.Empty<Renderer>();
            }

            var panes = new Renderer[2];
            foreach (ChurchRendererBinding binding in
                     registry.RendererBindings)
            {
                if (binding == null || binding.Renderer == null)
                {
                    continue;
                }

                if (binding.SourceName == "INT_StainedGlassNorth")
                {
                    panes[0] = binding.Renderer;
                }
                else if (binding.SourceName == "INT_StainedGlassSouth")
                {
                    panes[1] = binding.Renderer;
                }
            }

            if (panes[0] == null || panes[1] == null)
            {
                throw new InvalidOperationException(
                    "The church interior model must expose the north " +
                    "and south aisle glazing as separate renderers.");
            }

            return panes;
        }

        private static Light[] BuildWarmLayer(Transform parent)
        {
            var lights = new Light[WarmPracticalCount];
            for (int index = 0; index < WarmPracticals.Length; index++)
            {
                (string name, Vector3 position, float intensity,
                    float range) = WarmPracticals[index];
                lights[index] = CreatePoint(
                    parent,
                    name,
                    position,
                    CandleColor,
                    intensity,
                    range);
                // The first two are the votive stands and get a real
                // burning cluster. The altar and font candles are wax
                // the imported model owns, so those flicker in light
                // alone.
                List<Transform> flames = index < VotiveStandCentres.Length
                    ? BuildVotiveFlames(
                        parent,
                        VotiveStandCentres[index],
                        name)
                    : null;
                AttachFlame(lights[index], flames, (uint)index + 1u);
            }

            for (int index = 0; index < Sconces.Length; index++)
            {
                (string name, float side, float depth, float height,
                    float intensity) = Sconces[index];
                Transform flame = BuildSconceFixture(
                    parent,
                    name,
                    side,
                    depth,
                    height);
                Light sconce = CreatePoint(
                    parent,
                    name,
                    flame.localPosition,
                    CandleColor,
                    intensity,
                    WarmRange);
                lights[WarmPracticals.Length + index] = sconce;
                AttachFlame(
                    sconce,
                    new[] { flame },
                    (uint)(WarmPracticals.Length + index) + 1u);
            }

            int coronaStart = WarmPracticals.Length + Sconces.Length;
            for (int index = 0; index < Coronas.Length; index++)
            {
                (string name, float depth, float height, float radius,
                    float intensity, float range, float chainTop) =
                        Coronas[index];
                List<Transform> flames = BuildCoronaFixture(
                    parent,
                    name,
                    depth,
                    height,
                    radius,
                    chainTop);
                Light corona = CreatePoint(
                    parent,
                    name,
                    new Vector3(0f, height + 0.16f, depth),
                    CandleColor,
                    intensity,
                    range);
                lights[coronaStart + index] = corona;
                AttachFlame(
                    corona,
                    flames,
                    (uint)(coronaStart + index) + 1u);
            }

            return lights;
        }

        /// <summary>
        /// A bracket, a cup, a candle and a flame against the aisle
        /// wall. A pool of warm light with nothing making it reads as a
        /// mistake; this is the thing that is burning. Returns the world
        /// of the flame so the light sits inside it.
        /// </summary>
        private static Transform BuildSconceFixture(
            Transform parent,
            string name,
            float side,
            float depth,
            float height)
        {
            float wall = side * (WindowWallX - 0.02f);
            float inward = -side;
            var fixtureRoot = new GameObject($"{name} Fixture");
            fixtureRoot.transform.SetParent(parent, false);

            RuntimePrimitiveFactory.CreateBox(
                "Sconce Backplate",
                fixtureRoot.transform,
                new Vector3(wall + (inward * 0.04f), height, depth),
                new Vector3(0.08f, 0.42f, 0.16f),
                SconceIron,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Sconce Arm",
                fixtureRoot.transform,
                new Vector3(
                    wall + (inward * 0.17f),
                    height + 0.06f,
                    depth),
                new Vector3(0.30f, 0.05f, 0.05f),
                SconceIron,
                false);
            RuntimePrimitiveFactory.CreateCylinder(
                "Sconce Cup",
                fixtureRoot.transform,
                new Vector3(
                    wall + (inward * 0.30f),
                    height + 0.11f,
                    depth),
                new Vector3(0.24f, 0.05f, 0.24f),
                SconceIron,
                false);
            RuntimePrimitiveFactory.CreateCylinder(
                "Sconce Candle",
                fixtureRoot.transform,
                new Vector3(
                    wall + (inward * 0.30f),
                    height + 0.26f,
                    depth),
                new Vector3(0.09f, 0.13f, 0.09f),
                SconceWax,
                false);

            var flamePosition = new Vector3(
                wall + (inward * 0.30f),
                height + 0.44f,
                depth);
            return RuntimePrimitiveFactory.CreateBox(
                "Sconce Flame",
                fixtureRoot.transform,
                flamePosition,
                new Vector3(0.055f, 0.11f, 0.055f),
                CandleFlameColor,
                CityNightResources.EmissiveMaterial,
                false).transform;
        }

        /// <summary>
        /// A hoop of iron on a chain with candles standing on it, hung
        /// high enough to clear a head and low enough to be a thing in
        /// the room rather than a glow in the vault.
        ///
        /// It is built as ONE connected object, which the first version
        /// was not. Two separate mistakes made it read as loose parts
        /// floating near each other, and both are worth naming because
        /// neither is visible in the numbers:
        ///
        /// The hoop segments were laid RADIALLY. `Euler(0, -angle, 0)`
        /// turns a box's local +X onto the radius, so ten bars pointed
        /// outward like the spokes of a starburst instead of lying
        /// along the circle. A tangent needs the extra quarter turn.
        ///
        /// And the chain came down to the hoop's CENTRE, where there
        /// was nothing at all - no hub, no arms - so it ended in mid
        /// air with the ring floating around it. Chain, hub, arms and
        /// hoop now meet.
        /// </summary>
        private static List<Transform> BuildCoronaFixture(
            Transform parent,
            string name,
            float depth,
            float height,
            float radius,
            float chainTop)
        {
            var flames = new List<Transform>();
            var fixtureRoot = new GameObject($"{name} Fixture");
            fixtureRoot.transform.SetParent(parent, false);

            float chainLength = Mathf.Max(0.3f, chainTop - height);
            RuntimePrimitiveFactory.CreateCylinder(
                "Corona Chain",
                fixtureRoot.transform,
                new Vector3(0f, height + chainLength * 0.5f, depth),
                new Vector3(0.05f, chainLength * 0.5f, 0.05f),
                SconceIron,
                false);

            // The boss the chain lands on, and what the arms hang from.
            RuntimePrimitiveFactory.CreateCylinder(
                "Corona Hub",
                fixtureRoot.transform,
                new Vector3(0f, height, depth),
                new Vector3(0.26f, 0.05f, 0.26f),
                SconceIron,
                false);

            const int segments = 10;

            // Each bar spans the CHORD between its neighbours, plus a
            // little, so the ring closes instead of leaving ten gaps.
            float chord =
                2f * radius * Mathf.Sin(Mathf.PI / segments);
            for (int index = 0; index < segments; index++)
            {
                float angle = Mathf.PI * 2f * index / segments;
                float degrees = angle * Mathf.Rad2Deg;
                var offset = new Vector3(
                    Mathf.Cos(angle) * radius,
                    0f,
                    Mathf.Sin(angle) * radius);
                GameObject segment =
                    RuntimePrimitiveFactory.CreateBox(
                        "Corona Ring",
                        fixtureRoot.transform,
                        new Vector3(offset.x, height, depth + offset.z),
                        new Vector3(
                            chord + 0.05f,
                            0.07f,
                            0.10f),
                        SconceIron,
                        false);
                // The extra quarter turn is the whole difference
                // between a hoop and a starburst.
                segment.transform.localRotation = Quaternion.Euler(
                    0f,
                    -degrees - 90f,
                    0f);

                if (index % 2 != 0)
                {
                    continue;
                }

                // An arm out from the hub to this point of the hoop.
                GameObject arm = RuntimePrimitiveFactory.CreateBox(
                    "Corona Arm",
                    fixtureRoot.transform,
                    new Vector3(
                        offset.x * 0.5f,
                        height,
                        depth + (offset.z * 0.5f)),
                    new Vector3(radius, 0.045f, 0.045f),
                    SconceIron,
                    false);
                arm.transform.localRotation =
                    Quaternion.Euler(0f, -degrees, 0f);

                RuntimePrimitiveFactory.CreateCylinder(
                    "Corona Candle",
                    fixtureRoot.transform,
                    new Vector3(
                        offset.x,
                        height + 0.16f,
                        depth + offset.z),
                    new Vector3(0.08f, 0.13f, 0.08f),
                    SconceWax,
                    false);
                flames.Add(
                    RuntimePrimitiveFactory.CreateBox(
                        "Corona Flame",
                        fixtureRoot.transform,
                        new Vector3(
                            offset.x,
                            height + 0.36f,
                            depth + offset.z),
                        new Vector3(0.05f, 0.10f, 0.05f),
                        CandleFlameColor,
                        CityNightResources.EmissiveMaterial,
                        false).transform);
            }

            return flames;
        }

        /// <summary>
        /// One visible column per lancet, standing in the aperture it
        /// comes through and sized to it. The church had no volumetric
        /// layer of any kind before this - it is the one lit interior
        /// in the game where the light itself should be an object.
        /// </summary>
        private static ChurchLightShaft[] BuildLightShafts(
            Transform parent)
        {
            var shafts = new ChurchLightShaft[DaylightShaftCount];
            int cursor = 0;
            for (float side = -1f; side <= 1f; side += 2f)
            {
                for (int index = 0;
                     index < WindowDepths.Length;
                     index++)
                {
                    shafts[cursor] = ChurchLightShaft.Create(
                        parent,
                        side < 0f
                            ? $"Lancet Shaft North {index + 1}"
                            : $"Lancet Shaft South {index + 1}",
                        new Vector3(
                            side * ShaftApertureX,
                            WindowCenterY,
                            WindowDepths[index]),
                        side,
                        ApertureHeight,
                        ApertureWidth);
                    cursor++;
                }
            }

            return shafts;
        }

        private static Light[] BuildDaylightGlows(Transform parent)
        {
            var lights = new Light[DaylightGlowCount];
            int cursor = 0;
            for (float side = -1f; side <= 1f; side += 2f)
            {
                for (int index = 0;
                     index < WindowDepths.Length;
                     index++)
                {
                    lights[cursor] = CreatePoint(
                        parent,
                        side < 0f
                            ? $"Lancet Glow North {index + 1}"
                            : $"Lancet Glow South {index + 1}",
                        new Vector3(
                            side * (WindowWallX - GlowInsetFromWall),
                            WindowCenterY,
                            WindowDepths[index]),
                        StainedGlassColor,
                        0f,
                        GlowRange);
                    cursor++;
                }
            }

            return lights;
        }

        /// <summary>
        /// Sixteen flames standing on the wicks the model authored, one
        /// object each so they can burn independently.
        /// </summary>
        private static List<Transform> BuildVotiveFlames(
            Transform parent,
            Vector2 centre,
            string name)
        {
            var fixtureRoot = new GameObject($"{name} Fixture");
            fixtureRoot.transform.SetParent(parent, false);
            var flames = new List<Transform>(VotiveCandleCount);
            for (int index = 0; index < VotiveCandleCount; index++)
            {
                flames.Add(
                    RuntimePrimitiveFactory.CreateBox(
                        "Votive Flame",
                        fixtureRoot.transform,
                        VotiveFlamePosition(centre, index),
                        new Vector3(0.05f, 0.13f, 0.05f),
                        CandleFlameColor,
                        CityNightResources.EmissiveMaterial,
                        false).transform);
            }

            return flames;
        }

        private static void AttachFlame(
            Light light,
            IReadOnlyList<Transform> flames,
            uint seed)
        {
            light.gameObject
                .AddComponent<ChurchCandleFlame>()
                .Configure(light, flames, seed);
        }

        private static Light CreatePoint(
            Transform parent,
            string name,
            Vector3 localPosition,
            Color color,
            float intensity,
            float range)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = localPosition;
            Light light = holder.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            return light;
        }

        private void CreatePostProcessVolume()
        {
            GameObject volumeObject = new GameObject(
                "Church Interior Grade");
            volumeObject.transform.SetParent(transform, false);
            PostProcessVolume = volumeObject.AddComponent<Volume>();
            PostProcessVolume.isGlobal = true;
            PostProcessVolume.priority = 4f;
            PostProcessVolume.weight = 1f;

            RuntimeProfile =
                ScriptableObject.CreateInstance<VolumeProfile>();
            RuntimeProfile.name = "Runtime Church Interior Grade";
            RuntimeProfile.hideFlags = HideFlags.HideAndDontSave;
            PostProcessVolume.profile = RuntimeProfile;

            Tonemapping tonemapping =
                RuntimeProfile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);

            // The candles and the glass are the only bright things in
            // here, so the bloom threshold sits low enough for a flame
            // across the nave to read as a flame.
            Bloom bloom = RuntimeProfile.Add<Bloom>(true);
            bloom.threshold.Override(0.62f);
            bloom.intensity.Override(0.52f);
            bloom.scatter.Override(0.56f);
            bloom.highQualityFiltering.Override(false);

            ColorAdjustments color =
                RuntimeProfile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.42f);
            color.contrast.Override(-6f);
            color.saturation.Override(-4f);
            color.colorFilter.Override(
                new Color(1f, 0.97f, 0.92f, 1f));

            Vignette vignette = RuntimeProfile.Add<Vignette>(true);
            vignette.intensity.Override(0.08f);
            vignette.smoothness.Override(0.42f);

            RuntimeSceneSetup.AddIndoorGaussianDepthOfField(
                RuntimeProfile,
                7f,
                26f);
            volumeObject
                .AddComponent<DepthOfFieldSettingsBinder>()
                .Initialize(RuntimeProfile);
        }

        private void OnDestroy()
        {
            if (RuntimeProfile == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(RuntimeProfile);
            }
            else
            {
                DestroyImmediate(RuntimeProfile);
            }

            RuntimeProfile = null;
        }
    }
}
