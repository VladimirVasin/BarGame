using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Places two shared Blender pendants under the gazebo cross-beam.
    /// Only the working fitting receives a bulb, light and depth-tested halo;
    /// the existing site-light and City lighting owners retain their budgets,
    /// day/night law, bounded air support and source-derived bounce.
    /// </summary>
    public static class CityParkChessLampWorldBuilder
    {
        public const string RootName = "Park Chess Lamp";
        public const float LightNightIntensity = 52f;
        public const float LightDayIntensity = 14f;
        public const float LightRange = 10f;

        private static readonly Color LampColor =
            new Color(1f, 0.78f, 0.52f, 1f);
        private static readonly Color EnamelColor =
            new Color(0.205f, 0.225f, 0.205f, 1f);
        private static readonly Color WireColor =
            new Color(0.052f, 0.056f, 0.052f, 1f);

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

            CityMiscAssetProvider provider =
                CityMiscAssetProvider.LoadOrThrow();
            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.localScale = Vector3.one;

            BuildPendant(root, provider, plan, plan.LitPendant, "Lit");
            BuildPendant(root, provider, plan, plan.DeadPendant, "Dead");
            return root.gameObject;
        }

        private static void BuildPendant(
            Transform root,
            CityMiscAssetProvider provider,
            CityParkChessLampPlan plan,
            CityParkChessLampPendant pendant,
            string label)
        {
            Transform fitting = new GameObject(
                $"Chess Lamp Pendant {label}").transform;
            fitting.SetParent(root, false);
            fitting.SetPositionAndRotation(
                pendant.ShadeRimCenter,
                Quaternion.LookRotation(plan.Forward, Vector3.up));

            int partCount = CityMiscAssetProvider.GetPartCount(
                CityMiscKind.ParkChessLampPendant);
            for (int index = 0; index < partCount; index++)
            {
                CityMiscMeshPart part = provider.GetPartOrThrow(
                    CityMiscKind.ParkChessLampPendant,
                    0,
                    index);
                bool bulb = part.Role == CityMiscMeshRole.Neon;
                if (bulb && !pendant.IsLit)
                {
                    continue;
                }

                // Both fittings share imported meshes directly: there are no
                // per-instance mesh copies or material instances to dispose.
                var partObject = new GameObject(part.Component);
                partObject.transform.SetParent(fitting, false);
                partObject.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
                MeshRenderer renderer =
                    partObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = bulb
                    ? CityNightResources.EmissiveMaterial
                    : RuntimePrimitiveFactory.DefaultMaterial;
                if (bulb)
                {
                    // Keeping blue below one preserves the warm glass instead
                    // of saturating all three channels to a white chip.
                    RuntimePrimitiveFactory.SetColor(
                        renderer,
                        MultiplyRgb(LampColor, 1.5f, 1f));
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
                else if (part.Surface == CityMiscSurfaceKind.PaintedMetal)
                {
                    CityParkSurfaceAppearance.ApplyCombined(
                        renderer,
                        CityParkSurfaceKind.PaintedMetal,
                        EnamelColor);
                }
                else
                {
                    RuntimePrimitiveFactory.SetColor(renderer, WireColor);
                }
            }

            if (!pendant.IsLit)
            {
                return;
            }

            // Keep this name: the existing City practical owner uses it to
            // classify park light, occluded air and the small bounce patch.
            var emitter = new GameObject("Chess Lamp Light");
            emitter.transform.SetParent(fitting, false);
            emitter.transform.position = pendant.BulbCenter;
            Light light = emitter.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = LampColor;
            light.intensity = LightNightIntensity;
            light.range = LightRange;
            // The accepted park practical remains weak and shadowless.
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.lightmapBakeType = LightmapBakeType.Realtime;

            CityLightHalo halo = CityLightHalo.CreateAlwaysBurning(
                emitter.transform,
                Vector3.zero,
                0.42f,
                1.20f,
                MultiplyRgb(LampColor, 4.2f, 0.18f),
                MultiplyRgb(LampColor, 2.1f, 0.05f));
            // The site registry applies the shared two-thirds daylight floor.
            // The emissive bulb deliberately stays outside the night-glow
            // registry, because this fitting also burns in daylight.
            CityNightSiteLightRegistry.Register(
                light,
                LightNightIntensity,
                LightDayIntensity,
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
    }
}
