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

        private const int WetGroundCrossSteps = 3;

        /// <summary>
        /// How far the wet ground stands over the terrain it lies on. Enough
        /// to win the depth test against a `2 m` grid's chord, which is the
        /// same problem the lane skin solves with its own lift.
        /// </summary>
        private const float WetGroundLift = 0.045f;

        private const float BowlBedLift = 0.02f;

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

        private static readonly Color SeepGroundColor =
            new Color(0.480f, 0.468f, 0.435f, 1f);

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
            List<AlpineVillageBrookSample> renderSamples = CreateRenderSamples(brook);
            var root = new GameObject(RootName);
            root.transform.SetParent(parent, false);

            BuildWetGround(root.transform, plan, brook);
            BuildLedge(root.transform, brook, kit);
            BuildCatchStone(root.transform, brook, kit, semanticObjects);
            BuildBed(root.transform, plan, renderSamples);
            BuildBedStones(root.transform, brook, kit);
            BuildChannelWater(root.transform, renderSamples, semanticObjects);
            BuildBowl(root.transform, brook);
            BuildSeeps(root.transform, brook);
            BuildCascadeStones(root.transform, brook, kit, semanticObjects);
            return root;
        }

        private static List<AlpineVillageBrookSample> CreateRenderSamples(
            AlpineVillageBrookPlan brook)
        {
            var result = new List<AlpineVillageBrookSample>();
            for (int index = 0; index < brook.Samples.Count - 1; index++)
            {
                AlpineVillageBrookSample first = brook.Samples[index];
                AlpineVillageBrookSample second = brook.Samples[index + 1];
                int spans = Mathf.Max(1, Mathf.CeilToInt(
                    Vector3.Distance(first.Position, second.Position) / 0.20f));
                int last = index == brook.Samples.Count - 2 ? spans : spans - 1;
                for (int step = 0; step <= last; step++)
                {
                    float amount = step / (float)spans;
                    result.Add(new AlpineVillageBrookSample(
                        Mathf.Lerp(first.Distance, second.Distance, amount),
                        Vector3.Lerp(first.Position, second.Position, amount),
                        Vector3.Lerp(first.Right, second.Right, amount).normalized,
                        Mathf.Lerp(first.Width, second.Width, amount),
                        Mathf.Lerp(first.BedDepth, second.BedDepth, amount),
                        first.Reach));
                }
            }
            return result;
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
                brook.BowlFacing,
                Vector3.up);
            AlpineVillageWorldBuilder.PlaceKitAssembly(
                host.transform,
                kit,
                VillageAssetKind.SourceBowl,
                1,
                brook.CatchOuterSize,
                CatchHeight,
                _ => WetStoneColor);

            var blocker = new GameObject("Spring Catch Collision");
            blocker.transform.SetParent(host.transform, false);
            blocker.transform.localPosition =
                Vector3.up * (CatchHeight * 0.5f);
            BoxCollider box = blocker.AddComponent<BoxCollider>();
            box.size = new Vector3(
                brook.CatchOuterSize.x,
                CatchHeight,
                brook.CatchOuterSize.y);
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
                    0.95f,
                    Unit(stableId, 0x53544E32u));
                float lift = Mathf.Lerp(
                    0.015f,
                    0.060f,
                    Unit(stableId, 0x53544E33u));
                float height = 0.18f * scale;
                float bankSide = (side < 0f ? -1f : 1f) *
                    Mathf.Lerp(0.65f, 1.05f, Mathf.Abs(side));

                var host = new GameObject($"Brook Stone {index:000}");
                host.transform.SetParent(parent, false);
                host.transform.position =
                    sample.Position +
                    sample.Right * (bankSide * sample.HalfWidth) +
                    Vector3.down * (height - lift);
                host.transform.rotation = Quaternion.Euler(
                    0f,
                    Unit(stableId, 0x53544E34u) * 360f,
                    0f);
                AlpineVillageWorldBuilder.PlaceKitAssembly(
                    host.transform,
                    kit,
                    VillageAssetKind.BedStone,
                    variant,
                    new Vector2(Mathf.Min(0.42f * scale, sample.Width * 0.40f),
                        0.45f * scale),
                    height,
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
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                float side = Unit(cascade.StableId, 0x43535431u) < 0.5f ? -1f : 1f;
                // Keep the sound owner, but use a submerged rounded stone:
                // the old authored slab read as a shelf laid on the bank.
                host.transform.position = cascade.Lip + forward * 0.32f +
                    right * (side * cascade.Width * 0.44f) -
                    Vector3.up * (cascade.Drop * 0.32f + 0.14f);
                host.transform.rotation = Quaternion.LookRotation(
                    forward.normalized,
                    Vector3.up);
                AlpineVillageWorldBuilder.PlaceKitAssembly(
                    host.transform,
                    kit,
                    VillageAssetKind.BedStone,
                    VillageAssetProvider.SelectVariant(VillageAssetKind.BedStone,
                        cascade.StableId),
                    new Vector2(cascade.Width * 0.38f, 0.34f),
                    0.16f,
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

            // The plan describes a broad damp contour. Its visible centre
            // is a narrow, uneven stain, not a second paved route. Resample
            // only this ribbon so the pinches and tapered ends stay smooth.
            float seepLength = brook.SeepLine.Count == 0 ? 0f :
                brook.SeepLine[brook.SeepLine.Count - 1].Distance;
            for (int index = 0; index < brook.SeepLine.Count - 1; index++)
            {
                AlpineVillageBrookSample first = brook.SeepLine[index];
                AlpineVillageBrookSample second = brook.SeepLine[index + 1];
                int spans = Mathf.Max(1, Mathf.CeilToInt(
                    (second.Distance - first.Distance) / 0.75f));
                int last = index == brook.SeepLine.Count - 2 ? spans : spans - 1;
                for (int step = 0; step <= last; step++)
                {
                    float amount = step / (float)spans;
                    float distance = Mathf.Lerp(first.Distance, second.Distance, amount);
                    float taper = Mathf.SmoothStep(0f, 1f,
                        Mathf.Clamp01(Mathf.Min(distance, seepLength - distance) / 3f));
                    float patch = 0.5f +
                        Mathf.Sin(distance * 1.13f + 0.8f) * 0.29f +
                        Mathf.Sin(distance * 2.47f + 2.1f) * 0.21f;
                    Vector3 right = Vector3.Lerp(first.Right, second.Right, amount).normalized;
                    Vector3 point = Vector3.Lerp(first.Position, second.Position, amount);
                    point += right * taper *
                        (Mathf.Sin(distance * 1.37f + 1.7f) * 0.10f +
                         Mathf.Sin(distance * 0.51f) * 0.07f);
                    point.y = AlpineVillageTerrainSampler.SampleHeight(
                        plan, new Vector2(point.x, point.z)) + WetGroundLift;
                    centres.Add(point);
                    rights.Add(right);
                    halfWidths.Add(Mathf.Lerp(first.HalfWidth, second.HalfWidth, amount) *
                        Mathf.Lerp(0.10f, 0.30f, patch) * taper);
                }
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
                    SeepGroundColor);
            }

        }

        private static void BuildBed(
            Transform parent,
            AlpineVillagePlan plan,
            IReadOnlyList<AlpineVillageBrookSample> samples)
        {
            // One cross-section owns bed and both banks. No wet-ground sheet
            // crosses the water or overlays the bed. Each outer edge follows
            // the same refined ground triangles as the terrain collider.
            const int Strips = 8;
            var profiles = new Vector3[samples.Count][];
            for (int index = 0; index < samples.Count; index++)
            {
                profiles[index] = CreateBedProfile(plan, samples[index]);
            }
            var vertices = new List<Vector3>(samples.Count * Strips * 2);
            var uvs = new List<Vector2>(vertices.Capacity);
            var stoneTriangles = new List<int>();
            var bankTriangles = new List<int>();
            for (int strip = 0; strip < Strips; strip++)
            {
                bool bank = strip < 2 || strip >= Strips - 2;
                float pitch = MountainRoadSurfaceAppearance.GetRecipe(bank
                    ? MountainRoadSurfaceKind.ForestFloor
                    : MountainRoadSurfaceKind.LayeredStone).MetersPerTile;
                int start = vertices.Count;
                for (int index = 0; index < samples.Count; index++)
                {
                    for (int edge = 0; edge < 2; edge++)
                    {
                        Vector3 point = profiles[index][strip + edge];
                        vertices.Add(point);
                        uvs.Add(new Vector2(point.x, point.z) / pitch);
                    }
                }
                List<int> triangles = bank ? bankTriangles : stoneTriangles;
                for (int index = 0; index < samples.Count - 1; index++)
                {
                    int here = start + index * 2;
                    triangles.Add(here);
                    triangles.Add(here + 2);
                    triangles.Add(here + 1);
                    triangles.Add(here + 1);
                    triangles.Add(here + 2);
                    triangles.Add(here + 3);
                }
            }
            var mesh = new Mesh { name = "Spring Brook Bed Mesh",
                hideFlags = HideFlags.HideAndDontSave, subMeshCount = 2 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(stoneTriangles, 0);
            mesh.SetTriangles(bankTriangles, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var host = new GameObject("Spring Brook Bed");
            host.transform.SetParent(parent, false);
            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            host.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
            host.AddComponent<MeshCollider>().sharedMesh = mesh;
            MeshRenderer renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { RuntimePrimitiveFactory.DefaultMaterial,
                RuntimePrimitiveFactory.DefaultMaterial };
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            MountainRoadSurfaceAppearance.ApplyCombined(renderer,
                MountainRoadSurfaceKind.LayeredStone, BedColor, 0);
            MountainRoadSurfaceAppearance.ApplyCombined(renderer,
                MountainRoadSurfaceKind.ForestFloor, WetGroundColor, 1);
        }

        private static Vector3[] CreateBedProfile(AlpineVillagePlan plan,
            AlpineVillageBrookSample sample)
        {
            float half = sample.HalfWidth;
            float Margin(int side) => 0.30f +
                Mathf.Sin(sample.Distance * 1.07f + side * 1.3f) * 0.055f +
                Mathf.Sin(sample.Distance * 2.19f - side * 0.8f) * 0.035f;
            float left = Margin(-1);
            float right = Margin(1);
            float[] offsets = { -half - left, -half - left * 0.5f,
                -half, -half * 0.55f, 0f, half * 0.55f, half,
                half + right * 0.5f, half + right };
            var profile = new Vector3[offsets.Length];
            for (int edge = 0; edge < profile.Length; edge++)
            {
                Vector3 point = sample.Position + sample.Right * offsets[edge];
                point.y = AlpineVillageTerrainSampler.SampleMeshHeight(plan,
                    new Vector2(point.x, point.z)) + WetGroundLift;
                profile[edge] = point;
            }
            return profile;
        }

        private static void BuildChannelWater(
            Transform parent,
            IReadOnlyList<AlpineVillageBrookSample> samples,
            IDictionary<string, Transform> semanticObjects)
        {
            var centres = new List<Vector3>();
            var rights = new List<Vector3>();
            var halfWidths = new List<float>();

            for (int index = 0; index < samples.Count; index++)
            {
                AlpineVillageBrookSample sample = samples[index];
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
            // The authored inner wall faces, after footprint scaling, on
            // the catch's own rotated axes.
            Vector3 along = brook.BowlFacing;
            Vector3 right = brook.BowlOutletDirection;
            Vector2 inner = brook.BowlInnerHalfSize;
            var centres = new List<Vector3>();
            var rights = new List<Vector3>();
            var halfWidths = new List<float>();
            const int Sections = 3;
            for (int index = 0; index < Sections; index++)
            {
                float amount =
                    index / (float)(Sections - 1) * 2f - 1f;
                Vector3 point = brook.BowlCenter + along * (amount * inner.y);
                point.y = brook.BowlCenter.y + BowlBedLift;
                centres.Add(point);
                rights.Add(right);
                halfWidths.Add(inner.x);
            }

            // The swale follows the brook, not the inside of this catch.
            // Cover uncut ground with wet stone. The former water was buried
            // in that ground and only 12 mm above the imported floor, making
            // its exposed sliver entirely intersection foam.
            GameObject bed = CreateGroundRibbon(
                "Spring Bowl Bed", parent, centres, rights, halfWidths, 3);
            MountainRoadSurfaceAppearance.Apply(bed.GetComponent<Renderer>(),
                MountainRoadSurfaceKind.LayeredStone, BedColor);
            for (int index = 0; index < centres.Count; index++)
            {
                Vector3 point = centres[index];
                point.y = brook.BowlWaterTopY;
                centres[index] = point;
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

            // Start at the bowl's edge with no coplanar overlap, then share
            // the last cross-section and water material with the brook.
            centres.Clear();
            rights.Clear();
            halfWidths.Clear();
            Vector3 inside = brook.BowlCenter + right * inner.x;
            inside.y = brook.BowlWaterTopY;
            centres.Add(inside);
            centres.Add(brook.OverflowLip);
            rights.Add(-along);
            rights.Add(brook.Samples[0].Right);
            halfWidths.Add(brook.OverflowWidth * 0.5f);
            halfWidths.Add(brook.Samples[0].HalfWidth);
            GameObject spill = CityWaterSurfaceFactory.CreateRibbonSurface(
                "Spring Bowl Overflow", parent, centres, rights, halfWidths,
                WaterCrossSteps, AlpineSpringWaterResources.BrookMaterial);
            ConfigureWaterRenderer(spill);
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
                Vector3 fall = seep.Mouth - seep.Landing;
                var column = RuntimePrimitiveFactory.CreateMaterialBox(
                    $"Spring Seep {index:00}",
                    parent,
                    (seep.Mouth + seep.Landing) * 0.5f,
                    new Vector3(seep.Width, fall.magnitude, FallThickness),
                    AlpineSpringWaterResources.FallMaterial,
                    false);
                column.transform.rotation = Quaternion.LookRotation(
                    Vector3.Cross(Vector3.Cross(Vector3.up, seep.Outward), fall),
                    fall.normalized);
                ConfigureWaterRenderer(column);

                Vector3 landing = seep.Landing + Vector3.up * SplashLift;
                var ring = RuntimePrimitiveFactory.CreateMaterialBox(
                    $"Spring Seep Splash {index:00}",
                    parent,
                    landing,
                    new Vector3(
                        seep.Width * 2f,
                        SplashThickness,
                        seep.Width * 2f),
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
