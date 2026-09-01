using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Draws the spring: the ledge's seeps, the catch they fill, the brook
    /// leaving it and the wet ground that never dries around all three.
    ///
    /// The plan owns every position; this owns nothing but mesh and material.
    ///
    /// THE BED IS NOT DECORATION. The water shader is `Blend Off` and
    /// composites against `_CameraOpaqueTexture`, so water with nothing
    /// behind it samples the sky and reads as a hole in the hillside. The
    /// city solves this with a riverbed a metre deep; a brook needs only a
    /// hand's worth, but it needs it, and that is why the bed ribbon is built
    /// before the water and always.
    /// </summary>
    internal static class AlpineVillageBrookBuilder
    {
        public const string RootName = "Village Spring";

        /// <summary>
        /// Cross-steps in the swept ribbons. The lane skin's lesson at brook
        /// scale: two vertices cannot follow a curve.
        /// </summary>
        private const int WaterCrossSteps = 4;

        private const int BedCrossSteps = 4;
        private const int WetGroundCrossSteps = 3;

        /// <summary>
        /// How far the bed's edge stands outside the water's, so the sheet
        /// never ends over bare terrain at a meander's outer bank.
        /// </summary>
        private const float BedOverhang = 0.22f;

        /// <summary>
        /// How far the wet ground stands over the terrain it lies on. Enough
        /// to win the depth test against a `2 m` grid's chord, which is the
        /// same problem the lane skin solves with its own lift.
        /// </summary>
        private const float WetGroundLift = 0.035f;

        /// <summary>How far the catch's water stops short of its stone rim,
        /// so the sheet never pokes through the blocks.</summary>
        private const float BowlWaterInset = 0.16f;

        private const float SplashThickness = 0.02f;
        private const float SplashLift = 0.012f;
        private const float FallThickness = 0.055f;

        /// <summary>Bed stone: dark, wet, and never whitened by snow.
        /// </summary>
        private static readonly Color BedColor =
            new Color(0.340f, 0.345f, 0.340f, 1f);

        /// <summary>
        /// The ground the spring keeps too wet to whiten - the one dark patch
        /// in a village that is otherwise all snow.
        ///
        /// LIGHTER than the placeholder's `0.205`. That value was chosen for
        /// a slab the size of one plot; swept the length of the brook against
        /// lit snow it read as a hole cut in the hillside rather than as wet
        /// earth, which the first capture showed at once. Damp ground is a
        /// shade of the ground, not an absence of it.
        /// </summary>
        private static readonly Color WetGroundColor =
            new Color(0.415f, 0.408f, 0.386f, 1f);

        /// <summary>Stone that has water on it: darker and colder than the
        /// village's dry masonry, which is the whole tell.</summary>
        private static readonly Color WetStoneColor =
            new Color(0.318f, 0.318f, 0.300f, 1f);

        public static GameObject Build(
            Transform parent,
            AlpineVillagePlan plan,
            VillageAssetProvider kit,
            IDictionary<string, Transform> semanticObjects)
        {
            if (parent == null || plan == null || plan.Brook == null)
            {
                return null;
            }

            AlpineVillageBrookPlan brook = plan.Brook;
            var root = new GameObject(RootName);
            root.transform.SetParent(parent, false);

            BuildWetGround(root.transform, plan, brook);
            BuildLedge(root.transform, brook, kit);
            BuildCatchStone(root.transform, brook, kit, semanticObjects);
            BuildBed(root.transform, brook);
            BuildBedStones(root.transform, brook, kit);
            BuildChannelWater(root.transform, brook, semanticObjects);
            BuildBowl(root.transform, brook);
            BuildSeeps(root.transform, brook);
            BuildCascadeStones(root.transform, brook, kit, semanticObjects);
            BuildCascades(root.transform, brook);
            return root;
        }

        /// <summary>
        /// The outcrop the seeps come out from under.
        ///
        /// Without it the four falling columns stood in open snow like grave
        /// markers - the first capture of this feature showed exactly that,
        /// and it is why the ledge is a kit module rather than a nice-to-have:
        /// water has to leave ROCK or it is not a spring.
        /// </summary>
        private static void BuildLedge(
            Transform parent,
            AlpineVillageBrookPlan brook,
            VillageAssetProvider kit)
        {
            if (kit == null)
            {
                return;
            }

            var host = new GameObject("Spring Ledge");
            host.transform.SetParent(parent, false);
            host.transform.position = brook.LedgeCenter;
            host.transform.rotation = Quaternion.LookRotation(
                brook.LedgeFacing,
                Vector3.up);
            AlpineVillageWorldBuilder.PlaceKitAssembly(
                host.transform,
                kit,
                VillageAssetKind.SpringLedge,
                0,
                new Vector2(brook.LedgeSize.x, brook.LedgeSize.z),
                brook.LedgeSize.y,
                _ => WetStoneColor);

            // A proxy box, never a collider on the imported mesh: an FBX part
            // arrives on a root scaled by its unit factor, and a collider
            // taken from it is the wrong size in the wrong place.
            var blocker = new GameObject("Spring Ledge Collision");
            blocker.transform.SetParent(host.transform, false);
            blocker.transform.localPosition =
                Vector3.up * (brook.LedgeSize.y * 0.45f);
            BoxCollider box = blocker.AddComponent<BoxCollider>();
            box.size = new Vector3(
                brook.LedgeSize.x * 0.82f,
                brook.LedgeSize.y * 0.9f,
                brook.LedgeSize.z * 0.7f);
        }

        /// <summary>
        /// The stone catch, placed on the plan's own bowl centre.
        ///
        /// It used to be placed by the plot builder at a local offset while
        /// the water was placed by the brook plan, and the two did not meet:
        /// the first capture showed an EMPTY basin with the water lying in
        /// the snow beside it. One owner, one position.
        /// </summary>
        private static void BuildCatchStone(
            Transform parent,
            AlpineVillageBrookPlan brook,
            VillageAssetProvider kit,
            IDictionary<string, Transform> semanticObjects)
        {
            if (kit == null)
            {
                return;
            }

            var host = new GameObject("Spring Catch");
            host.transform.SetParent(parent, false);
            Register(
                semanticObjects,
                AlpineVillageSoundscapePlanner.SpringCatchOwnerStableId,
                host.transform);
            host.transform.position = new Vector3(
                brook.BowlCenter.x,
                brook.BowlCenter.y - CatchSink,
                brook.BowlCenter.z);
            host.transform.rotation = Quaternion.LookRotation(
                brook.LedgeFacing,
                Vector3.up);
            AlpineVillageWorldBuilder.PlaceKitAssembly(
                host.transform,
                kit,
                VillageAssetKind.SourceBowl,
                0,
                new Vector2(
                    brook.BowlSize.x * 1.12f,
                    brook.BowlSize.y * 1.12f),
                CatchHeight,
                _ => WetStoneColor);

            var blocker = new GameObject("Spring Catch Collision");
            blocker.transform.SetParent(host.transform, false);
            blocker.transform.localPosition =
                Vector3.up * (CatchHeight * 0.5f);
            BoxCollider box = blocker.AddComponent<BoxCollider>();
            box.size = new Vector3(
                brook.BowlSize.x * 1.12f,
                CatchHeight,
                brook.BowlSize.y * 1.12f);
        }

        /// <summary>How far the catch is bedded into the ground, so its rim
        /// stands a hand over the water rather than a step.</summary>
        private const float CatchSink = 0.22f;

        private const float CatchHeight = 0.62f;

        /// <summary>
        /// Stones in the channel, part sunk. Three shapes, placed off a hash
        /// of their own id so the arrangement never falls into a rhythm - the
        /// brief's one explicit rule about them.
        /// </summary>
        private static void BuildBedStones(
            Transform parent,
            AlpineVillageBrookPlan brook,
            VillageAssetProvider kit)
        {
            if (kit == null)
            {
                return;
            }

            for (int index = BedStoneStride;
                 index < brook.Samples.Count - 2;
                 index += BedStoneStride)
            {
                AlpineVillageBrookSample sample = brook.Samples[index];
                string stableId = $"village-brook-stone-{index:000}";
                int variant = VillageAssetProvider.SelectVariant(
                    VillageAssetKind.BedStone,
                    stableId);

                // Hashed from the id, like every other seeded placement in
                // the village, so a rebuild puts them back where they were.
                float side = Unit(stableId, 0x53544E31u) * 2f - 1f;
                float scale = Mathf.Lerp(
                    0.55f,
                    1.15f,
                    Unit(stableId, 0x53544E32u));
                float lift = Mathf.Lerp(
                    0.02f,
                    0.09f,
                    Unit(stableId, 0x53544E33u));

                var host = new GameObject($"Brook Stone {index:000}");
                host.transform.SetParent(parent, false);
                host.transform.position =
                    sample.Position +
                    sample.Right * (side * sample.HalfWidth * 0.72f) +
                    Vector3.down * (sample.BedDepth - lift);
                host.transform.rotation = Quaternion.Euler(
                    0f,
                    Unit(stableId, 0x53544E34u) * 360f,
                    0f);
                AlpineVillageWorldBuilder.PlaceKitAssembly(
                    host.transform,
                    kit,
                    VillageAssetKind.BedStone,
                    variant,
                    new Vector2(0.62f, 0.58f) * scale,
                    0.34f * scale,
                    _ => WetStoneColor);
            }
        }

        private const int BedStoneStride = 7;

        /// <summary>The stone lip under each cascade's falling water.
        /// </summary>
        private static void BuildCascadeStones(
            Transform parent,
            AlpineVillageBrookPlan brook,
            VillageAssetProvider kit,
            IDictionary<string, Transform> semanticObjects)
        {
            if (kit == null)
            {
                return;
            }

            for (int index = 0; index < brook.Cascades.Count; index++)
            {
                AlpineVillageBrookCascade cascade = brook.Cascades[index];
                Vector3 forward = cascade.Forward;
                forward.y = 0f;
                if (forward.sqrMagnitude <= 0.000001f)
                {
                    continue;
                }

                var host = new GameObject($"Cascade Step {index:00}");
                host.transform.SetParent(parent, false);

                // Under its own cascade's id: the soundscape names the step
                // it comes out of, and there is more than one step.
                Register(semanticObjects, cascade.StableId, host.transform);
                host.transform.position =
                    cascade.Lip - Vector3.up * (cascade.Drop + 0.06f);
                host.transform.rotation = Quaternion.LookRotation(
                    forward.normalized,
                    Vector3.up);
                AlpineVillageWorldBuilder.PlaceKitAssembly(
                    host.transform,
                    kit,
                    VillageAssetKind.CascadeStep,
                    0,
                    new Vector2(cascade.Width * 1.05f, 0.55f),
                    cascade.Drop + 0.16f,
                    _ => WetStoneColor);
            }
        }

        private static void Register(
            IDictionary<string, Transform> semanticObjects,
            string stableId,
            Transform value)
        {
            if (semanticObjects == null || string.IsNullOrEmpty(stableId))
            {
                return;
            }

            semanticObjects[stableId] = value;
        }

        private static float Unit(string stableId, uint salt)
        {
            uint hash = CitySoundStableHash.String(stableId) ^ salt;
            hash ^= hash >> 13;
            hash *= 0x5BD1E995u;
            hash ^= hash >> 15;
            return (hash & 0xFFFFFF) / (float)0x1000000;
        }

        /// <summary>
        /// The dark band along the water, and the one along the contour to
        /// the chapel's basin.
        ///
        /// The second is the whole of the link between the two stone catches:
        /// they sit `52 m` apart with `0.31 m` between them, which is a
        /// contour rather than a fall, and §10g of the art bible allows
        /// "мокрая земля, каменная приёмная чаша и ручеёк вниз, а не
        /// сооружение". Ground, therefore. Not a launder on posts.
        /// </summary>
        private static void BuildWetGround(
            Transform parent,
            AlpineVillagePlan plan,
            AlpineVillageBrookPlan brook)
        {
            var centres = new List<Vector3>();
            var rights = new List<Vector3>();
            var halfWidths = new List<float>();

            for (int index = 0; index < brook.SeepLine.Count; index++)
            {
                AlpineVillageBrookSample sample = brook.SeepLine[index];
                Vector3 point = sample.Position;
                point.y = AlpineVillageTerrainSampler.SampleHeight(
                    plan,
                    new Vector2(point.x, point.z)) + WetGroundLift;
                centres.Add(point);
                rights.Add(sample.Right);
                halfWidths.Add(sample.HalfWidth * 0.72f);
            }

            if (centres.Count >= 2)
            {
                GameObject seepLine = CreateGroundRibbon(
                    "Spring Seep Line",
                    parent,
                    centres,
                    rights,
                    halfWidths,
                    WetGroundCrossSteps);
                MountainRoadSurfaceAppearance.Apply(
                    seepLine.GetComponent<Renderer>(),
                    MountainRoadSurfaceKind.ForestFloor,
                    WetGroundColor);
            }

            // And a wider apron along the brook itself, so the channel is not
            // a dark line in unbroken white with nothing between them.
            centres.Clear();
            rights.Clear();
            halfWidths.Clear();
            for (int index = 0; index < brook.Samples.Count; index++)
            {
                AlpineVillageBrookSample sample = brook.Samples[index];
                Vector3 point = sample.Position;
                point.y = AlpineVillageTerrainSampler.SampleHeight(
                    plan,
                    new Vector2(point.x, point.z)) + WetGroundLift;
                centres.Add(point);
                rights.Add(sample.Right);
                // A damp MARGIN, not an apron. Swept wide this ribbon is a
                // hard-edged dark polygon lying across the hillside; the
                // channel and the snow that keeps off it already do the
                // work, and this only has to soften where they meet.
                halfWidths.Add(sample.HalfWidth + 0.45f);
            }

            GameObject banks = CreateGroundRibbon(
                "Spring Wet Banks",
                parent,
                centres,
                rights,
                halfWidths,
                WetGroundCrossSteps);
            MountainRoadSurfaceAppearance.Apply(
                banks.GetComponent<Renderer>(),
                MountainRoadSurfaceKind.ForestFloor,
                WetGroundColor);
        }

        private static void BuildBed(
            Transform parent,
            AlpineVillageBrookPlan brook)
        {
            var centres = new List<Vector3>();
            var rights = new List<Vector3>();
            var halfWidths = new List<float>();

            for (int index = 0; index < brook.Samples.Count; index++)
            {
                AlpineVillageBrookSample sample = brook.Samples[index];
                Vector3 point = sample.Position;
                point.y -= sample.BedDepth;
                centres.Add(point);
                rights.Add(sample.Right);
                halfWidths.Add(sample.HalfWidth + BedOverhang);
            }

            GameObject bed = CreateGroundRibbon(
                "Spring Brook Bed",
                parent,
                centres,
                rights,
                halfWidths,
                BedCrossSteps);
            MountainRoadSurfaceAppearance.Apply(
                bed.GetComponent<Renderer>(),
                MountainRoadSurfaceKind.LayeredStone,
                BedColor);
        }

        private static void BuildChannelWater(
            Transform parent,
            AlpineVillageBrookPlan brook,
            IDictionary<string, Transform> semanticObjects)
        {
            var centres = new List<Vector3>();
            var rights = new List<Vector3>();
            var halfWidths = new List<float>();

            for (int index = 0; index < brook.Samples.Count; index++)
            {
                AlpineVillageBrookSample sample = brook.Samples[index];
                centres.Add(sample.Position);
                rights.Add(sample.Right);
                halfWidths.Add(sample.HalfWidth);
            }

            GameObject water = CityWaterSurfaceFactory.CreateRibbonSurface(
                "Spring Brook Water",
                parent,
                centres,
                rights,
                halfWidths,
                WaterCrossSteps,
                AlpineSpringWaterResources.BrookMaterial);
            ConfigureWaterRenderer(water);
            Register(
                semanticObjects,
                AlpineVillageSoundscapePlanner.BrookChannelOwnerStableId,
                water.transform);
        }

        /// <summary>
        /// The catch. Still water, so it gets the pool material and the
        /// shader's own zero-flow switch rather than a slower brook.
        /// </summary>
        private static void BuildBowl(
            Transform parent,
            AlpineVillageBrookPlan brook)
        {
            // A RIBBON, not a Rect. `CreateSlopedSurface` takes an
            // axis-aligned footprint, and the catch is turned to face the
            // ledge - so a rect sheet sat across the stonework at an angle
            // and only a corner of it showed inside the basin. Swept along
            // the catch's own axis it fits whatever way the trough is
            // pointing.
            Vector3 along = brook.LedgeFacing;
            along.y = 0f;
            along = along.sqrMagnitude <= 0.000001f
                ? Vector3.forward
                : along.normalized;
            var right = new Vector3(along.z, 0f, -along.x);
            float half = brook.BowlSize.y * 0.5f - BowlWaterInset;
            var centres = new List<Vector3>();
            var rights = new List<Vector3>();
            var halfWidths = new List<float>();
            const int Sections = 3;
            for (int index = 0; index < Sections; index++)
            {
                float amount =
                    index / (float)(Sections - 1) * 2f - 1f;
                Vector3 point = brook.BowlCenter + along * (amount * half);
                point.y = brook.BowlWaterTopY;
                centres.Add(point);
                rights.Add(right);
                halfWidths.Add(brook.BowlSize.x * 0.5f - BowlWaterInset);
            }

            GameObject water = CityWaterSurfaceFactory.CreateRibbonSurface(
                "Spring Bowl Water",
                parent,
                centres,
                rights,
                halfWidths,
                3,
                AlpineSpringWaterResources.PoolMaterial);
            ConfigureWaterRenderer(water);
        }

        /// <summary>
        /// Water arriving at daylight, from four mouths at four heights.
        ///
        /// The falling columns are the fountain's own material: a sheet whose
        /// every pattern is a function of world XZ cannot fall, and the city
        /// already built the shader that can.
        /// </summary>
        private static void BuildSeeps(
            Transform parent,
            AlpineVillageBrookPlan brook)
        {
            for (int index = 0; index < brook.Seeps.Count; index++)
            {
                AlpineVillageBrookSeep seep = brook.Seeps[index];
                var column = RuntimePrimitiveFactory.CreateMaterialBox(
                    $"Spring Seep {index:00}",
                    parent,
                    seep.Mouth - Vector3.up * (seep.Fall * 0.5f),
                    new Vector3(seep.Width, seep.Fall, FallThickness),
                    AlpineSpringWaterResources.FallMaterial,
                    false);
                ConfigureWaterRenderer(column);

                Vector3 landing = seep.Mouth;
                landing.y -= seep.Fall - SplashLift;
                var ring = RuntimePrimitiveFactory.CreateMaterialBox(
                    $"Spring Seep Splash {index:00}",
                    parent,
                    landing,
                    new Vector3(
                        seep.Width * 3.4f,
                        SplashThickness,
                        seep.Width * 3.4f),
                    AlpineSpringWaterResources.SplashMaterial,
                    false);
                ConfigureWaterRenderer(ring);
            }
        }

        private static void BuildCascades(
            Transform parent,
            AlpineVillageBrookPlan brook)
        {
            for (int index = 0; index < brook.Cascades.Count; index++)
            {
                AlpineVillageBrookCascade cascade = brook.Cascades[index];
                Vector3 forward = cascade.Forward;
                forward.y = 0f;
                forward = forward.sqrMagnitude <= 0.000001f
                    ? Vector3.forward
                    : forward.normalized;

                var face = RuntimePrimitiveFactory.CreateMaterialBox(
                    $"Spring Cascade {index:00}",
                    parent,
                    cascade.Lip - Vector3.up * (cascade.Drop * 0.5f),
                    new Vector3(
                        cascade.Width,
                        cascade.Drop,
                        FallThickness),
                    AlpineSpringWaterResources.FallMaterial,
                    false);
                face.transform.rotation = Quaternion.LookRotation(
                    forward,
                    Vector3.up);
                ConfigureWaterRenderer(face);

                Vector3 foot = cascade.Lip +
                    forward * (cascade.Width * 0.35f);
                foot.y -= cascade.Drop - SplashLift;
                var ring = RuntimePrimitiveFactory.CreateMaterialBox(
                    $"Spring Cascade Splash {index:00}",
                    parent,
                    foot,
                    new Vector3(
                        cascade.Width * 1.25f,
                        SplashThickness,
                        cascade.Width * 1.25f),
                    AlpineSpringWaterResources.SplashMaterial,
                    false);
                ConfigureWaterRenderer(ring);
            }
        }

        /// <summary>
        /// A swept ribbon with world-projected UVs, for the surfaces that
        /// carry a stone or soil sheet. The water's own ribbon lives in
        /// <see cref="CityWaterSurfaceFactory"/> and deliberately carries no
        /// UVs at all, because that shader reads world position instead.
        /// </summary>
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

                    // World-projected, like every other village surface, so
                    // the sheet's scale matches the ground it lies on.
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

            // No collider anywhere in here. The swale IS the terrain, and the
            // terrain already carries the village's one mesh collider; a
            // second surface a few centimetres above it would only give the
            // hero something to graze, and a graze reads back as a crawl.
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
