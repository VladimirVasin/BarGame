using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Draws the water either side of the road's culvert, and the pour out of
    /// the bore itself.
    ///
    /// The bore was a dark cylinder. That was honest while there was no water
    /// on this mountain - "the bore is a hole, not a material", as the road's
    /// own builder puts it - but the road has carried a `CulvertWater` sound
    /// anchor beside it the whole time, and a sound with nothing making it is
    /// the one thing a player notices without being able to say why. The
    /// cylinder stays; a pour now comes out of it.
    /// </summary>
    internal static class MountainRoadBrookBuilder
    {
        public const string RootName = "Mountain Road Water";

        private const int WaterCrossSteps = 4;
        private const int BedCrossSteps = 3;
        private const float BedOverhang = 0.20f;
        private const float SplashThickness = 0.02f;
        private const float FallThickness = 0.06f;

        /// <summary>Wet channel stone: the road's own layered rock, dark.
        /// </summary>
        private static readonly Color BedColor =
            new Color(0.300f, 0.302f, 0.296f, 1f);

        public static GameObject Build(
            Transform parent,
            MountainRoadPlan plan)
        {
            if (parent == null || plan == null)
            {
                return null;
            }

            MountainRoadBrookPlan brook = MountainRoadBrookPlanner.Create(plan);
            var root = new GameObject(RootName);
            root.transform.SetParent(parent, false);

            // The material's flow bearing is the road's fall line, taken from
            // the two mouths the water actually runs between.
            Vector3 fall = brook.OutletMouth - brook.InletMouth;
            AlpineSpringWaterResources.ConfigureRoadBrookFlow(
                new Vector2(fall.x, fall.z));

            BuildReach(root.transform, "Culvert Inlet", brook.Inlet);
            BuildReach(root.transform, "Culvert Outlet", brook.Outlet);
            BuildBorePour(root.transform, brook);
            return root;
        }

        private static void BuildReach(
            Transform parent,
            string name,
            IReadOnlyList<MountainRoadBrookSample> samples)
        {
            if (samples == null || samples.Count < 2)
            {
                return;
            }

            var bedCentres = new List<Vector3>(samples.Count);
            var waterCentres = new List<Vector3>(samples.Count);
            var rights = new List<Vector3>(samples.Count);
            var bedHalfWidths = new List<float>(samples.Count);
            var waterHalfWidths = new List<float>(samples.Count);

            for (int index = 0; index < samples.Count; index++)
            {
                MountainRoadBrookSample sample = samples[index];
                waterCentres.Add(sample.Position);
                bedCentres.Add(
                    sample.Position - Vector3.up * sample.BedDepth);
                rights.Add(sample.Right);
                waterHalfWidths.Add(sample.HalfWidth);
                bedHalfWidths.Add(sample.HalfWidth + BedOverhang);
            }

            // The bed first and always: the water shader composites against
            // `_CameraOpaqueTexture`, so a sheet over nothing samples sky.
            GameObject bed = CreateGroundRibbon(
                $"{name} Bed",
                parent,
                bedCentres,
                rights,
                bedHalfWidths,
                BedCrossSteps);
            MountainRoadSurfaceAppearance.Apply(
                bed.GetComponent<Renderer>(),
                MountainRoadSurfaceKind.LayeredStone,
                BedColor);

            GameObject water = CityWaterSurfaceFactory.CreateRibbonSurface(
                $"{name} Water",
                parent,
                waterCentres,
                rights,
                waterHalfWidths,
                WaterCrossSteps,
                AlpineSpringWaterResources.RoadBrookMaterial);
            ConfigureWaterRenderer(water);
        }

        private static void BuildBorePour(
            Transform parent,
            MountainRoadBrookPlan brook)
        {
            float fall = Mathf.Max(
                0.10f,
                brook.Bore.y - brook.OutletMouth.y);
            var column = RuntimePrimitiveFactory.CreateMaterialBox(
                "Culvert Pour",
                parent,
                brook.Bore - Vector3.up * (fall * 0.5f),
                new Vector3(
                    brook.BoreRadius * 1.7f,
                    fall,
                    FallThickness),
                AlpineSpringWaterResources.FallMaterial,
                false);
            ConfigureWaterRenderer(column);

            var ring = RuntimePrimitiveFactory.CreateMaterialBox(
                "Culvert Splash",
                parent,
                new Vector3(
                    brook.Bore.x,
                    brook.OutletMouth.y + 0.015f,
                    brook.Bore.z),
                new Vector3(
                    brook.BoreRadius * 3.6f,
                    SplashThickness,
                    brook.BoreRadius * 3.6f),
                AlpineSpringWaterResources.SplashMaterial,
                false);
            ConfigureWaterRenderer(ring);
        }

        private static GameObject CreateGroundRibbon(
            string name,
            Transform parent,
            IReadOnlyList<Vector3> centres,
            IReadOnlyList<Vector3> rights,
            IReadOnlyList<float> halfWidths,
            int crossSteps)
        {
            int across = Mathf.Max(1, crossSteps) + 1;
            var vertices = new Vector3[centres.Count * across];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[(centres.Count - 1) * (across - 1) * 6];

            for (int index = 0; index < centres.Count; index++)
            {
                Vector3 right = rights[index];
                right.y = 0f;
                right = right.sqrMagnitude <= 0.000001f
                    ? Vector3.right
                    : right.normalized;
                for (int step = 0; step < across; step++)
                {
                    float side = Mathf.Lerp(
                        -halfWidths[index],
                        halfWidths[index],
                        step / (float)(across - 1));
                    Vector3 point = centres[index] + right * side;
                    int vertex = index * across + step;
                    vertices[vertex] = point;
                    uvs[vertex] = new Vector2(point.x, point.z);
                }
            }

            int triangle = 0;
            for (int index = 0; index < centres.Count - 1; index++)
            {
                for (int step = 0; step < across - 1; step++)
                {
                    int here = index * across + step;
                    int ahead = here + across;
                    triangles[triangle++] = here;
                    triangles[triangle++] = ahead;
                    triangles[triangle++] = here + 1;
                    triangles[triangle++] = here + 1;
                    triangles[triangle++] = ahead;
                    triangles[triangle++] = ahead + 1;
                }
            }

            var mesh = new Mesh
            {
                name = $"{name} Mesh",
                hideFlags = HideFlags.HideAndDontSave
            };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var host = new GameObject(name);
            host.transform.SetParent(parent, false);
            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            host.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);

            // No collider: the road's terrain already carries collision, and
            // a second surface a few centimetres over it is only something
            // for the hero to graze - and a graze reads back as a crawl.
            return host;
        }

        private static void ConfigureWaterRenderer(GameObject instance)
        {
            if (instance == null)
            {
                return;
            }

            var renderer = instance.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }
}
