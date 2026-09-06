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

        // THE MODEL'S numbers, in recipe space, measured off the
        // imported meshes rather than the recipe that no longer draws
        // them (`build_park_fountain_and_statue` in
        // tools/build-city-misc-3d-model.py): the basin is a twenty
        // sided stone ring, outer radius 3.20, inner face 2.72, floor
        // top 0.28, rim top 0.82. The water was authored when the
        // fountain was still four boxes, and a square sheet in a round
        // bowl hung its four corners over the grass - so the sheet is
        // a disc drawn to the wall's own circumradius, buried in the
        // stone between the wall's corners instead of leaving a ring
        // of dry floor.
        internal const float BasinInnerRadius = 2.72f;
        internal const int BasinSides = 40;
        internal const float BasinFloorTopY = 0.28f;
        internal const float BasinRimTopY = 0.82f;

        // Water halfway up the rim: the old 0.36 was eight centimetres
        // over the floor of a basin walled 0.54 deep, so the fountain
        // read as an empty trough with a puddle in it.
        internal const float BasinWaterTopY = 0.58f;

        // The statue's two spout tubes end at ±0.72 on the tangent
        // axis with their tip centre at 3.30 and a 0.07 radius, so the
        // pour leaves just inside the mouth and dips below the basin
        // surface, and the join is never a visible seam at either end.
        internal const float SpoutTipOffset = 0.72f;
        internal const float SpoutMouthY = 3.26f;
        internal const float StreamThickness = 0.11f;
        internal const float StreamPlunge = 0.10f;
        internal const float SplashSize = 0.62f;

        // The splash rides this far over the sheet: clear of the
        // breathing centimetre of chop the basin shader displaces the
        // surface by, so the two never fight for the same pixel.
        internal const float SplashThickness = 0.035f;
        internal const int SplashSides = 16;

        // The pour leans out as it falls. The statue's arms are short
        // and the pedestal under it flares back out to 0.70 at the
        // water line, so a plumb drop from the spout would pour onto
        // the stone instead of into the basin - and water leaving a
        // spout sideways travels sideways anyway. Nine degrees off
        // plumb clears the pedestal with the splash ring to spare.
        internal const float PedestalWaterlineRadius = 0.70f;
        internal const float StreamLandingOffset = 1.15f;

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

            // The basin sheet, a disc to the rim's inner face. The
            // bowl is a ring of revolution, so no rotation of the
            // footprint can matter and none is applied.
            float top = origin.y + BasinWaterTopY;
            GameObject basin = CityWaterSurfaceFactory
                .CreateDiscSurface(
                    $"Fountain Basin Water {ordinal}",
                    root,
                    new Vector3(origin.x, top, origin.z),
                    BasinInnerRadius,
                    BasinSides,
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
                                tangent * (side * SpoutTipOffset) +
                                Vector3.up * SpoutMouthY;
                Vector3 landing = origin +
                                  tangent *
                                  (side * StreamLandingOffset) +
                                  Vector3.up *
                                  (BasinWaterTopY - StreamPlunge);
                Vector3 fall = mouth - landing;
                GameObject stream =
                    RuntimePrimitiveFactory.CreateMaterialBox(
                        $"Fountain Stream {ordinal} {side}",
                        root,
                        (mouth + landing) * 0.5f,
                        new Vector3(
                            StreamThickness,
                            fall.magnitude,
                            StreamThickness),
                        CityFountainWaterResources.StreamMaterial,
                        false);
                stream.transform.localRotation =
                    Quaternion.FromToRotation(
                        Vector3.up,
                        fall.normalized);
                ConfigureRenderer(stream);

                // A ring, not a slab: the splash shader reads world
                // position alone, so the patch can be any shape, and a
                // square one on open water reads as a decal lying
                // there rather than as water being hit.
                GameObject splash = CityWaterSurfaceFactory
                    .CreateDiscSurface(
                        $"Fountain Splash {ordinal} {side}",
                        root,
                        new Vector3(
                            landing.x,
                            top + SplashThickness,
                            landing.z),
                        SplashSize * 0.5f,
                        SplashSides,
                        CityFountainWaterResources.SplashMaterial);
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
