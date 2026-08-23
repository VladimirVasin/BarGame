using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// The park fountain's running water: a real animated sheet in
    /// the basin, one falling stream under each of the statue's two
    /// spout arms, and the splash ring each stream lands in.
    ///
    /// It stands outside the batched decoration layer for the
    /// playground swing's reason - the batch is one static mesh per
    /// chunk and one material per style, and water is neither. The
    /// stone fountain stays in the batch; this builder reads the same
    /// descriptor frame and stands the water inside it, so the two
    /// can never drift apart.
    ///
    /// Presentation only: no colliders, no walkable contribution.
    /// The basin rim already collides through the decoration's own
    /// proxy bounds.
    /// </summary>
    public static class CityFountainWaterBuilder
    {
        public const string RootName = "Park Fountain Water";

        // The batched fountain's numbers, in recipe space: the water
        // plane the old grey slab occupied, held inside the rim.
        internal const float BasinWaterTopY = 0.36f;
        internal const float BasinWaterHalf = 2.59f;

        // The statue's spout arms end at ±1.01 on the tangent axis
        // (arm centre 0.62, half-length 0.39); the pour leaves just
        // under the arm's underside and dips below the basin surface
        // so the join is never a visible seam.
        internal const float SpoutTipOffset = 1.01f;
        internal const float SpoutMouthY = 2.92f;
        internal const float StreamThickness = 0.11f;
        internal const float StreamPlunge = 0.10f;
        internal const float SplashSize = 0.62f;
        internal const float SplashThickness = 0.03f;

        // The reflection probe point: over the water, stepped off the
        // statue's axis so the statue is IN its own mirror instead of
        // culled around the camera standing inside it.
        internal const float MirrorProbeHeight = 1.15f;
        internal const float MirrorProbeSetback = 1.9f;

        public static GameObject Build(
            Transform parent,
            CityLayout layout,
            CityDecorationPlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            Transform root = null;
            int ordinal = 0;
            for (int index = 0; index < plan.Descriptors.Count; index++)
            {
                CityDecorationDescriptor descriptor =
                    plan.Descriptors[index];
                if (descriptor.Kind !=
                    CityDecorationKind.ParkFountainAndStatue)
                {
                    continue;
                }

                if (root == null)
                {
                    root = new GameObject(RootName).transform;
                    root.SetParent(parent, false);
                }

                BuildFountainWater(
                    root,
                    layout,
                    descriptor,
                    ordinal);
                ordinal++;
            }

            return root != null ? root.gameObject : null;
        }

        private static void BuildFountainWater(
            Transform root,
            CityLayout layout,
            CityDecorationDescriptor descriptor,
            int ordinal)
        {
            CityDecorationWorldBuilder.GetDecorationFrame(
                layout,
                descriptor,
                out Vector3 origin,
                out Vector3 tangent,
                out Vector3 forward);

            // The basin sheet. Decorations are axis-aligned and the
            // basin is square, so the footprint needs no rotation.
            float top = origin.y + BasinWaterTopY;
            GameObject basin = CityWaterSurfaceFactory
                .CreateSlopedSurface(
                    $"Fountain Basin Water {ordinal}",
                    root,
                    Rect.MinMaxRect(
                        origin.x - BasinWaterHalf,
                        origin.z - BasinWaterHalf,
                        origin.x + BasinWaterHalf,
                        origin.z + BasinWaterHalf),
                    top,
                    top,
                    CityFountainWaterResources.BasinMaterial);
            ConfigureRenderer(basin);

            // The Morrowind mirror, one per city: the basin material
            // is shared, so the first fountain's surroundings serve
            // every basin - an environment map has no parallax for a
            // second fountain to miss.
            if (ordinal == 0)
            {
                var mirror = new GameObject(
                    "Fountain Reflection Mirror");
                mirror.transform.SetParent(root, false);
                mirror.transform.localPosition =
                    origin +
                    forward * -MirrorProbeSetback +
                    Vector3.up * (BasinWaterTopY + MirrorProbeHeight);
                mirror
                    .AddComponent<CityFountainReflectionController>()
                    .Initialize(
                        CityFountainWaterResources.BasinMaterial);
            }

            // One pour per spout arm, splash ring where it lands.
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 mouth = origin +
                                tangent * (side * SpoutTipOffset);
                float bottom = top - StreamPlunge;
                float height = origin.y + SpoutMouthY - bottom;
                GameObject stream =
                    RuntimePrimitiveFactory.CreateMaterialBox(
                        $"Fountain Stream {ordinal} {side}",
                        root,
                        new Vector3(
                            mouth.x,
                            bottom + height * 0.5f,
                            mouth.z),
                        new Vector3(
                            StreamThickness,
                            height,
                            StreamThickness),
                        CityFountainWaterResources.StreamMaterial,
                        false);
                ConfigureRenderer(stream);

                GameObject splash =
                    RuntimePrimitiveFactory.CreateMaterialBox(
                        $"Fountain Splash {ordinal} {side}",
                        root,
                        new Vector3(
                            mouth.x,
                            top + SplashThickness * 0.5f,
                            mouth.z),
                        new Vector3(
                            SplashSize,
                            SplashThickness,
                            SplashSize),
                        CityFountainWaterResources.SplashMaterial,
                        false);
                ConfigureRenderer(splash);
            }
        }

        private static void ConfigureRenderer(GameObject value)
        {
            var renderer = value.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
        }
    }
}
