using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Builds the wire over the park chess set and the two cones on it.
    ///
    /// Everything here follows the city's ordinary fixture recipe — flat
    /// geometry, an emissive lens box on the shared unlit material, a
    /// shadowless <see cref="Light"/> on ForcePixel, a two-billboard
    /// <see cref="CityLightHalo"/> and one registration — with two
    /// deliberate departures, both of which the boat station learned the
    /// hard way. The lens multiplier is low rather than the usual 4.6x,
    /// and the lens stays out of <see cref="CityNightGlowRegistry"/>.
    /// Each is commented where it happens.
    /// </summary>
    public static class CityParkChessLampWorldBuilder
    {
        public const string RootName = "Park Chess Lamp";

        /// <summary>
        /// Warm and weak. The reference points on this grade are the
        /// street practical at 31 over 16.5 m, a door bulb at 64-110
        /// over 7-8 m and the pier hand lamp at 46 over 11 m. This one
        /// sits between the street post and the door bulb, because it
        /// hangs over the middle of the set rather than over one board
        /// and has to reach both of them: §10 asks for park light that
        /// is rarer and softer than the street's, not for a circle so
        /// small it models nobody.
        /// </summary>
        public const float LightNightIntensity = 52f;

        /// <summary>
        /// It burns weakly at noon too. Nobody has come to switch it
        /// off - that is the whole reason it is still the only lit
        /// thing over two tables - and a fixture that vanishes at dawn
        /// would be saying the opposite.
        /// </summary>
        public const float LightDayIntensity = 14f;

        public const float LightRange = 10f;

        private const int WireSegments = 12;
        private const float WireThickness = 0.028f;
        private const float ShadeRimRadius = 0.17f;
        private const float ShadeTopRadius = 0.06f;
        private const float FlexThickness = 0.022f;
        private const float PoleThickness = 0.085f;
        private const float PoleHookReach = 0.26f;

        private static readonly Color LampColor =
            new Color(1f, 0.78f, 0.52f, 1f);
        private static readonly Color EnamelColor =
            new Color(0.205f, 0.225f, 0.205f, 1f);
        private static readonly Color EnamelInnerColor =
            new Color(0.395f, 0.385f, 0.345f, 1f);
        private static readonly Color WireColor =
            new Color(0.052f, 0.056f, 0.052f, 1f);
        private static readonly Color DeadSocketColor =
            new Color(0.115f, 0.112f, 0.100f, 1f);
        private static readonly Color PoleColor =
            new Color(0.098f, 0.115f, 0.100f, 1f);

        public static GameObject Build(
            Transform parent,
            CityParkChessLampPlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (!plan.IsPresent)
            {
                return null;
            }

            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);

            // The plan is measured in world space against the terrain and
            // the tree field, so the root is pinned to the world basis and
            // every offset below is passed straight through.
            root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.localScale = Vector3.one;

            BuildAnchorPole(root, plan.NearAnchor);
            BuildAnchorPole(root, plan.FarAnchor);
            BuildWire(root, plan);
            BuildPendant(root, plan, plan.LitPendant, "Lit");
            BuildPendant(root, plan, plan.DeadPendant, "Dead");
            return root.gameObject;
        }

        /// <summary>
        /// Only builds anything when the plan could not find a tree on
        /// that side: a thin painted hook, which is what a park wire is
        /// actually tied to when there is nothing else to tie it to.
        /// </summary>
        private static void BuildAnchorPole(
            Transform root,
            CityParkChessLampAnchor anchor)
        {
            if (anchor.IsTree)
            {
                return;
            }

            float height = anchor.TiePoint.y - anchor.GroundY + 0.20f;
            var shaft = new Vector3(
                anchor.TiePoint.x,
                anchor.GroundY + (height * 0.5f),
                anchor.TiePoint.z);
            RuntimePrimitiveFactory.CreateBox(
                "Chess Lamp Pole",
                root,
                shaft,
                new Vector3(PoleThickness, height, PoleThickness),
                PoleColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Chess Lamp Pole Hook",
                root,
                new Vector3(
                    anchor.TiePoint.x,
                    anchor.GroundY + height,
                    anchor.TiePoint.z),
                new Vector3(PoleHookReach, 0.05f, 0.05f),
                PoleColor,
                false);
        }

        /// <summary>
        /// The span, as a short chain of thin boxes each rotated onto
        /// its own chord of the sag. Twelve is chosen against the PS1
        /// composite rather than against a curve error budget: past
        /// that the extra joints land inside one downsampled pixel.
        /// </summary>
        private static void BuildWire(
            Transform root,
            CityParkChessLampPlan plan)
        {
            float nearT = Vector3.Dot(
                plan.NearAnchor.TiePoint - plan.Origin,
                plan.Forward);
            float farT = Vector3.Dot(
                plan.FarAnchor.TiePoint - plan.Origin,
                plan.Forward);
            Vector3 previous = plan.NearAnchor.TiePoint;
            for (int index = 1; index <= WireSegments; index++)
            {
                float distance = Mathf.Lerp(
                    nearT,
                    farT,
                    index / (float)WireSegments);
                Vector3 next = CityParkChessLampPlan.SampleWire(
                    plan.NearAnchor,
                    plan.FarAnchor,
                    plan.Origin,
                    plan.Forward,
                    distance);
                AddStrand(
                    root,
                    $"Chess Lamp Wire {index:00}",
                    previous,
                    next,
                    WireThickness,
                    WireColor);
                previous = next;
            }
        }

        private static void BuildPendant(
            Transform root,
            CityParkChessLampPlan plan,
            CityParkChessLampPendant pendant,
            string label)
        {
            AddStrand(
                root,
                $"Chess Lamp Flex {label}",
                pendant.WirePoint,
                pendant.ShadeCenter +
                    (Vector3.up *
                        (CityParkChessLampPlan.ShadeHeightMeters * 0.5f)),
                FlexThickness,
                WireColor);

            // The enamel cone. Two stacked boxes rather than a real
            // cone: every other city fixture is built the same way and
            // a lathe here would read as a different game's prop.
            RuntimePrimitiveFactory.CreateBox(
                $"Chess Lamp Shade Cap {label}",
                root,
                pendant.ShadeCenter +
                    (Vector3.up *
                        (CityParkChessLampPlan.ShadeHeightMeters * 0.32f)),
                new Vector3(
                    ShadeTopRadius * 2f,
                    CityParkChessLampPlan.ShadeHeightMeters * 0.36f,
                    ShadeTopRadius * 2f),
                EnamelColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                $"Chess Lamp Shade {label}",
                root,
                pendant.ShadeCenter,
                new Vector3(
                    ShadeRimRadius * 2f,
                    CityParkChessLampPlan.ShadeHeightMeters * 0.64f,
                    ShadeRimRadius * 2f),
                EnamelColor,
                false);

            float rimY = pendant.ShadeCenter.y -
                (CityParkChessLampPlan.ShadeHeightMeters * 0.5f);
            if (!pendant.IsLit)
            {
                // A bare socket in an identical shade. No lens, no
                // Light, no halo: the dark half of the set has to be
                // dark in the render, not merely dimmer.
                RuntimePrimitiveFactory.CreateBox(
                    "Chess Lamp Dead Socket",
                    root,
                    new Vector3(
                        pendant.ShadeCenter.x,
                        rimY + 0.03f,
                        pendant.ShadeCenter.z),
                    new Vector3(0.055f, 0.06f, 0.055f),
                    DeadSocketColor,
                    false);
                return;
            }

            // The inside of the working shade catches its own bulb.
            RuntimePrimitiveFactory.CreateBox(
                "Chess Lamp Shade Inner",
                root,
                new Vector3(
                    pendant.ShadeCenter.x,
                    rimY + 0.012f,
                    pendant.ShadeCenter.z),
                new Vector3(
                    ShadeRimRadius * 1.72f,
                    0.024f,
                    ShadeRimRadius * 1.72f),
                EnamelInnerColor,
                false);

            // The multiplier is bounded by the blue channel, not by
            // taste. At the usual 4.6x this colour's blue passes 1, all
            // three channels saturate together and a warm bulb turns
            // into a white chip while the light it casts stays amber.
            // At 1.5x blue lands at 0.78, so red and green burn into a
            // hot core and the glass keeps its hue.
            Color glow = MultiplyRgb(LampColor, 1.5f, 1f);
            var bulbCenter = new Vector3(
                pendant.ShadeCenter.x,
                rimY - 0.028f,
                pendant.ShadeCenter.z);
            RuntimePrimitiveFactory.CreateBox(
                "Chess Lamp Bulb",
                root,
                bulbCenter,
                new Vector3(0.085f, 0.10f, 0.085f),
                glow,
                CityNightResources.EmissiveMaterial,
                false);

            // Deliberately NOT registered in CityNightGlowRegistry. That
            // registry lerps a registered emissive down to a tenth of
            // itself under a day sky, which is right for a fixture that
            // switches off and wrong for one that does not: the glass
            // would read dead at noon while the lamp was still throwing
            // light. The pier lantern and the yard spotlight sit
            // outside it for the same reason.
            var emitter = new GameObject("Chess Lamp Light");
            emitter.transform.SetParent(root, false);
            emitter.transform.position = bulbCenter;
            Light light = emitter.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = LampColor;
            light.intensity = LightNightIntensity;
            light.range = LightRange;

            // No city fixture but the yard spotlight casts shadows, and
            // this one least of all: §10 wants landmarks modelled
            // weakly, with no stage lighting anywhere in the park.
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.lightmapBakeType = LightmapBakeType.Realtime;

            var haloObject = new GameObject("Chess Lamp Halo");
            haloObject.transform.SetParent(emitter.transform, false);
            CityLightHalo halo =
                haloObject.AddComponent<CityLightHalo>();
            halo.Initialize(
                CityNightResources.AtmosphereMaterial,
                0.42f,
                1.20f,
                MultiplyRgb(LampColor, 4.2f, 0.18f),
                MultiplyRgb(LampColor, 2.1f, 0.05f));
            CityNightSiteLightRegistry.Register(
                light,
                LightNightIntensity,
                LightDayIntensity,
                halo);
        }

        private static void AddStrand(
            Transform root,
            string name,
            Vector3 start,
            Vector3 end,
            float thickness,
            Color color)
        {
            Vector3 delta = end - start;
            float length = delta.magnitude;
            if (length <= 0.0001f)
            {
                return;
            }

            GameObject strand = RuntimePrimitiveFactory.CreateBox(
                name,
                root,
                (start + end) * 0.5f,
                new Vector3(thickness, thickness, length),
                color,
                false);
            strand.transform.rotation = Quaternion.LookRotation(
                delta / length,
                Vector3.up);
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
