using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The kerosene hand lamp: a small tin body, a warm glass, a wire
    /// bail over the top and the point light that is the whole reason
    /// it exists. It is the lamp standing on the rail cap at the end
    /// of the seacoast pier, and it is the same lamp the gravedigger sets
    /// down beside an open grave — one fixture, built once, so the two
    /// can never drift apart.
    ///
    /// Everything about it is object-scale. It is roughly a forearm
    /// tall and it sits where a person would have put it down, so it
    /// reads as something carried out and left rather than as a
    /// fitting the town installed and still maintains.
    ///
    /// Two deliberate registry choices, both inherited from the pier.
    /// The light goes on the site registry through the day-floor
    /// overload, so it burns around the clock. The glass stays OUT of
    /// the glow registry: that registry lerps every registered
    /// emissive down to a tenth of itself under a day sky, which is
    /// right for a lamp that switches off and wrong for one that does
    /// not — the light would still be thrown while the lamp itself
    /// looked dead. The always-on yard spotlight sits outside it for
    /// the same reason.
    ///
    /// No collider. On the pier it stands at the end of a deck barely
    /// two metres wide, and beside a grave it stands on the collar the
    /// hero walks over; in both places a collider is something he
    /// would have to squeeze past.
    /// </summary>
    public static class CityHandLampWorldBuilder
    {
        /// <summary>
        /// A kerosene-warm filament, well to the orange side of a
        /// tungsten door bulb. Warm light is what shows on dark water,
        /// and it is what shows on turned earth too.
        /// </summary>
        public static readonly Color LampColor =
            new Color(1.00f, 0.74f, 0.42f);

        /// <summary>
        /// It never goes out, and the day floor is what makes that
        /// legible: "still burning" has to read at noon too, not
        /// merely survive the night factor.
        ///
        /// Sized as an object rather than as infrastructure. A
        /// municipal post would say the place is maintained, so this
        /// throws a close pool over the boards or the grave it stands
        /// by and no further.
        /// </summary>
        public const float NightIntensity = 46f;
        public const float DayIntensity = 16f;

        /// <summary>
        /// Still far enough to reach the water it stands over. The
        /// water shader's additional-light glint is a lit highlight,
        /// not a mirror, so it exists only where the fixture's own
        /// range covers the surface: a short-ranged lamp puts no
        /// reflection on the water at all, however bright it is. This
        /// number and ForcePixel below are load bearing, not art
        /// direction.
        /// </summary>
        public const float Range = 11.0f;

        /// <summary>Height of the flame above whatever the lamp is
        /// standing on.</summary>
        public const float FlameHeightMeters = 0.13f;

        /// <summary>Overall envelope: the cap is the widest part and
        /// the bail handle the highest. The assembly's origin lies on
        /// the plane the lamp stands on, so an anchor is a resting
        /// surface and never needs a lift.</summary>
        public const float SpanMeters = 0.14f;
        public const float HeightMeters = 0.298f;

        private static readonly Color IronColor =
            new Color(0.070f, 0.075f, 0.075f);

        /// <summary>
        /// Stands one lamp at a world point, turned by a yaw. The name
        /// is the caller's because the two places that own one call it
        /// different things, and the hierarchy is what a reader of the
        /// scene sees first.
        /// </summary>
        public static GameObject Build(
            Transform parent,
            string name,
            Vector3 stand,
            float yawDegrees)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            Transform assembly = new GameObject(
                string.IsNullOrWhiteSpace(name)
                    ? "Hand Lamp"
                    : name).transform;
            assembly.SetParent(parent, false);
            assembly.SetPositionAndRotation(
                stand,
                Quaternion.Euler(0f, yawDegrees, 0f));

            // The foot it stands on, and the tin cap over the glass.
            RuntimePrimitiveFactory.CreateBox(
                "Hand Lamp Foot",
                assembly,
                new Vector3(0f, 0.02f, 0f),
                new Vector3(0.13f, 0.04f, 0.13f),
                IronColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Hand Lamp Cap",
                assembly,
                new Vector3(0f, 0.215f, 0f),
                new Vector3(0.14f, 0.04f, 0.14f),
                IronColor,
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
                    IronColor,
                    false);
            }

            RuntimePrimitiveFactory.CreateBox(
                "Hand Lamp Handle",
                assembly,
                new Vector3(0f, 0.292f, 0f),
                new Vector3(0.122f, 0.012f, 0.012f),
                IronColor,
                false);

            // A low multiplier on purpose, and the number is set by the
            // blue channel rather than by taste.
            //
            // The street fixtures push their lens hard because they are
            // read from across a district. This one is read from two
            // metres, and the moment the multiplier lifts blue past 1
            // all three channels clip together and the amber glass
            // becomes a white chip — the cast light stays warm while the
            // lamp making it looks like a fluorescent tube. At 1.5x this
            // colour's blue lands at 0.63, so red and green saturate
            // into a hot core while the glass keeps its hue.
            Color glow = MultiplyRgb(LampColor, 1.5f, 1f);
            RuntimePrimitiveFactory.CreateBox(
                "Hand Lamp Glass",
                assembly,
                new Vector3(0f, FlameHeightMeters, 0f),
                new Vector3(0.095f, 0.145f, 0.095f),
                glow,
                CityNightResources.EmissiveMaterial,
                false);

            var emitter = new GameObject("Hand Lamp Light");
            emitter.transform.SetParent(assembly, false);
            emitter.transform.localPosition =
                new Vector3(0f, FlameHeightMeters, 0f);
            Light light = emitter.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = LampColor;
            light.intensity = NightIntensity;
            light.range = Range;
            light.shadows = LightShadows.None;

            // ForcePixel is not a quality setting here: a vertex-mode
            // light is not an additional light the water shader can
            // read, and the reflection is the whole point of the lamp.
            light.renderMode = LightRenderMode.ForcePixel;
            light.lightmapBakeType = LightmapBakeType.Realtime;

            var haloObject = new GameObject("Hand Lamp Halo");
            haloObject.transform.SetParent(emitter.transform, false);
            CityLightHalo halo =
                haloObject.AddComponent<CityLightHalo>();
            halo.Initialize(
                CityNightResources.AtmosphereMaterial,
                0.26f,
                0.80f,
                MultiplyRgb(LampColor, 1.9f, 0.20f),
                MultiplyRgb(LampColor, 1.1f, 0.06f));
            CityNightSiteLightRegistry.Register(
                light,
                NightIntensity,
                DayIntensity,
                halo);
            return assembly.gameObject;
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
    }
}
