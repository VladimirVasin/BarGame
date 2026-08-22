using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Materialises the lighthouse island: one vertex-coloured mesh
    /// on the self-hazing silhouette material, and the un-batched
    /// lantern assembly — lens core plus two opposed beam cones on
    /// the additive beam material, turned by the lantern controller.
    /// The lantern stays out of the baked mesh for the playground
    /// swing's reason: it is the one piece of the island that moves.
    ///
    /// Presentation only, end to end: no colliders, no real lights,
    /// no walkable or bounds contribution. The island is scenery the
    /// fog owns.
    /// </summary>
    public static class CityLighthouseIslandWorldBuilder
    {
        public const string RootName = "Lighthouse Island";

        // The island's flat palette, weathered greys against the fog.
        // The silhouette shader mixes all of it toward the haze, so
        // these read at 8-25 % contrast by distance; only relative
        // value survives, which is why the tower's banding spans so
        // wide.
        private static readonly Color Rock =
            new Color(0.21f, 0.22f, 0.20f);
        private static readonly Color ShackWall =
            new Color(0.28f, 0.26f, 0.23f);
        private static readonly Color ShackRoof =
            new Color(0.20f, 0.185f, 0.165f);
        private static readonly Color Timber =
            new Color(0.17f, 0.15f, 0.13f);
        private static readonly Color Wreck =
            new Color(0.19f, 0.15f, 0.12f);
        private static readonly Color TowerBase =
            new Color(0.30f, 0.30f, 0.28f);
        // The pale day-mark course; the dark courses are this times
        // the planner's dark band shade.
        private static readonly Color TowerShaft =
            new Color(0.56f, 0.54f, 0.49f);
        private static readonly Color Ironwork =
            new Color(0.10f, 0.11f, 0.11f);

        // The lantern assembly shares the planner's forced-perspective
        // factor: a shrunken tower with a full-size lens and beams
        // would read as a near, small lighthouse instead of a far one.
        private const float BeamLength =
            12f * CityLighthouseIslandPlanner.VisualScale;
        private const float BeamStartRadius =
            0.18f * CityLighthouseIslandPlanner.VisualScale;
        private const float BeamEndRadius =
            1.6f * CityLighthouseIslandPlanner.VisualScale;
        private const int BeamSegments = 10;
        private const float BeamPitchDegrees = 2f;
        private const float LensSize =
            0.9f * CityLighthouseIslandPlanner.VisualScale;

        public static GameObject Build(
            Transform parent,
            CityLighthouseIslandPlan plan)
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

            BuildSilhouette(root, plan);
            BuildLantern(root, plan);

            return root.gameObject;
        }

        private static void BuildSilhouette(
            Transform root,
            CityLighthouseIslandPlan plan)
        {
            var colors = new Color[plan.Parts.Count];
            for (int index = 0; index < plan.Parts.Count; index++)
            {
                colors[index] = ResolveColor(plan.Parts[index].Kind);
            }

            Mesh mesh = CityLighthouseIslandMeshFactory
                .CreateIslandMesh(
                    "Lighthouse Island Mesh",
                    plan.Parts,
                    colors);

            var island = new GameObject("Island Silhouette");
            island.transform.SetParent(root, false);
            island.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer =
                island.AddComponent<MeshRenderer>();
            renderer.sharedMaterial =
                CityLighthouseIslandResources.SilhouetteMaterial;
            ConfigurePresentationRenderer(renderer);
            island.AddComponent<RuntimeGeneratedMeshOwner>()
                .Initialize(mesh);
            // Kept readable so the whole silhouette can be verified
            // as presentation data, the mountain backdrop's way.
            mesh.UploadMeshData(false);
        }

        private static void BuildLantern(
            Transform root,
            CityLighthouseIslandPlan plan)
        {
            Transform assembly = new GameObject(
                "Lighthouse Lantern").transform;
            assembly.SetParent(root, false);
            assembly.localPosition = plan.LanternPosition;

            GameObject lens = RuntimePrimitiveFactory.CreateBox(
                "Lighthouse Lens",
                assembly,
                Vector3.zero,
                new Vector3(LensSize, LensSize, LensSize),
                Color.white,
                CityLighthouseIslandResources.BeamMaterial,
                false);
            var lensRenderer = lens.GetComponent<MeshRenderer>();
            ConfigurePresentationRenderer(lensRenderer);

            Transform pivot = new GameObject(
                "Lantern Pivot").transform;
            pivot.SetParent(assembly, false);

            Mesh beamMesh = CityLighthouseIslandMeshFactory
                .CreateBeamCone(
                    "Lighthouse Beam Mesh",
                    BeamLength,
                    BeamStartRadius,
                    BeamEndRadius,
                    BeamSegments);
            pivot.gameObject
                .AddComponent<RuntimeGeneratedMeshOwner>()
                .Initialize(beamMesh);

            var beamRenderers = new Renderer[2];
            for (int index = 0; index < 2; index++)
            {
                var beam = new GameObject(
                    index == 0
                        ? "Lighthouse Beam North"
                        : "Lighthouse Beam South");
                beam.transform.SetParent(pivot, false);
                beam.transform.localRotation = Quaternion.Euler(
                    BeamPitchDegrees,
                    index * 180f,
                    0f);
                beam.AddComponent<MeshFilter>().sharedMesh =
                    beamMesh;
                MeshRenderer renderer =
                    beam.AddComponent<MeshRenderer>();
                renderer.sharedMaterial =
                    CityLighthouseIslandResources.BeamMaterial;
                ConfigurePresentationRenderer(renderer);
                beamRenderers[index] = renderer;
            }

            beamMesh.UploadMeshData(false);

            CityLighthouseLanternController controller =
                assembly.gameObject
                    .AddComponent<CityLighthouseLanternController>();
            controller.Initialize(
                pivot,
                lensRenderer,
                beamRenderers,
                assembly.position);
        }

        private static void ConfigurePresentationRenderer(
            MeshRenderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;
        }

        private static Color ResolveColor(
            CityLighthouseIslandPartKind kind)
        {
            switch (kind)
            {
                case CityLighthouseIslandPartKind.Rock:
                    return Rock;
                case CityLighthouseIslandPartKind.ShackWall:
                    return ShackWall;
                case CityLighthouseIslandPartKind.ShackRoof:
                    return ShackRoof;
                case CityLighthouseIslandPartKind.Timber:
                    return Timber;
                case CityLighthouseIslandPartKind.Wreck:
                    return Wreck;
                case CityLighthouseIslandPartKind.TowerBase:
                    return TowerBase;
                case CityLighthouseIslandPartKind.TowerShaft:
                    return TowerShaft;
                case CityLighthouseIslandPartKind.Gallery:
                case CityLighthouseIslandPartKind.LampRoom:
                    return Ironwork;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind));
            }
        }
    }
}
