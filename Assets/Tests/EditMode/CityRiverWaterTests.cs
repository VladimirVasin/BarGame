using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The geometry the water shader needs under it.
    ///
    /// A transparent surface changes what the geometry has to be honest
    /// about. While the river was an opaque lid, nothing behind it
    /// existed: the city emits no terrain under a river cell, the quay
    /// walls stop a hand's width under the waterline and the bridge piers
    /// ended above it. None of that showed. All of it shows now, so these
    /// are the pins: the surface is a grid a wave can move, there is a
    /// floor under it, the floor is closed against the walls, and nothing
    /// that stands in the water stops short of the floor.
    /// </summary>
    public sealed class CityRiverWaterTests
    {
        private const int Seed = GameSessionState.DefaultCitySeed;

        // The visible water top sits this far under the plan's datum.
        private const float WaterTopOffset =
            CitySurfaceDescriptor.WaterTopOffset;

        private CityLayout layout;
        private GameObject parent;
        private GameObject river;

        [SetUp]
        public void BuildRiver()
        {
            layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                Seed);
            parent = new GameObject("River Water Test");
            river = CityRiverWorldBuilder.Build(parent.transform, layout);
            Assert.That(
                river,
                Is.Not.Null,
                "The default city must have a river to test.");
        }

        [TearDown]
        public void DestroyRiver()
        {
            if (parent != null)
            {
                Object.DestroyImmediate(parent);
            }
        }

        /// <summary>
        /// The old surface was a stretched cube: eight vertices, nowhere
        /// for a wave to go. Each sheet must now carry a vertex roughly
        /// every <see cref="CityWaterSurfaceFactory.SurfacePitch"/> metres
        /// in both directions, and its top must still fall downstream -
        /// the river runs from the south into the sea.
        /// </summary>
        [Test]
        public void Water_IsAGridWhoseTopFollowsTheChannelFall()
        {
            Transform water = FindChild(river.transform, "Flowing Water");
            var sheets = new List<MeshFilter>(
                water.GetComponentsInChildren<MeshFilter>(true));
            Assert.That(
                sheets,
                Has.Count.EqualTo(layout.River.Segments.Count),
                "One water sheet per river segment.");

            foreach (MeshFilter filter in sheets)
            {
                Mesh mesh = filter.sharedMesh;
                Assert.That(mesh, Is.Not.Null);

                Bounds local = mesh.bounds;
                int expectedColumns = Mathf.Max(
                    1,
                    Mathf.RoundToInt(
                        local.size.x / CityWaterSurfaceFactory.SurfacePitch));
                int expectedRows = Mathf.Max(
                    1,
                    Mathf.RoundToInt(
                        local.size.z / CityWaterSurfaceFactory.SurfacePitch));
                Assert.That(
                    mesh.vertexCount,
                    Is.EqualTo((expectedColumns + 1) * (expectedRows + 1)),
                    $"'{filter.name}' is not subdivided at the water " +
                    "pitch; a vertex wave has nothing to displace.");
                Assert.That(
                    mesh.vertexCount,
                    Is.GreaterThan(8),
                    $"'{filter.name}' is still a box.");
            }
        }

        [Test]
        public void Water_FallsFromTheSouthIntoTheSea()
        {
            Transform water = FindChild(river.transform, "Flowing Water");
            float southernmost = float.MaxValue;
            float northernmost = float.MinValue;
            float southY = 0f;
            float northY = 0f;
            foreach (MeshFilter filter in
                     water.GetComponentsInChildren<MeshFilter>(true))
            {
                Bounds world = filter.GetComponent<Renderer>().bounds;
                if (world.center.z < southernmost)
                {
                    southernmost = world.center.z;
                    southY = world.center.y;
                }

                if (world.center.z > northernmost)
                {
                    northernmost = world.center.z;
                    northY = world.center.y;
                }
            }

            Assert.That(
                southY,
                Is.GreaterThan(northY + 0.5f),
                "The river must visibly fall towards its mouth; the " +
                "shader scrolls its flow that way regardless.");
        }

        /// <summary>
        /// The channel floor exists under every segment, at the authored
        /// depth, and the submerged sides that close it reach higher than
        /// the underside of the granite they hide behind. If that overlap
        /// ever goes negative the band between them is a hole straight
        /// through the world, seen through the water at a grazing angle
        /// from the promenade.
        /// </summary>
        [Test]
        public void Riverbed_SitsUnderEverySegmentAndLeavesNoGapAtTheQuay()
        {
            // The relationship the geometry hangs on, asserted on the
            // numbers rather than measured off a slope: the submerged
            // sides must start higher than the granite's underside. The
            // segment fall makes any single measured pair pass whichever
            // way this constant went.
            Assert.That(
                CityRiverWorldBuilder.SubmergedSideTop,
                Is.LessThan(QuayWallUndersideBelowWaterTop),
                "The submerged sides start below the underside of the " +
                "quay walls, which leaves an open band the water is now " +
                "clear enough to see through.");

            Transform bed = FindChild(river.transform, "Channel Floor");
            var floors = new List<Renderer>();
            var sides = new List<Renderer>();
            foreach (Renderer renderer in
                     bed.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name.StartsWith("River Floor"))
                {
                    floors.Add(renderer);
                }
                else
                {
                    sides.Add(renderer);
                }
            }

            Assert.That(
                floors,
                Has.Count.EqualTo(layout.River.Segments.Count),
                "One channel floor per river segment.");
            Assert.That(
                sides,
                Has.Count.EqualTo(layout.River.Segments.Count * 2),
                "Two submerged sides per river segment.");

            for (int index = 0;
                 index < layout.River.Segments.Count;
                 index++)
            {
                CityRiverSegmentDescriptor segment =
                    layout.River.Segments[index];
                float waterTop = segment.AverageWaterY + WaterTopOffset;
                Renderer floor = FindNamed(floors, segment.Cell.y);

                // Measured against the segment average, so the tolerance
                // has to swallow half the segment's own fall and the
                // slight tilt the sloped box carries across the channel.
                Assert.That(
                    floor.bounds.max.y,
                    Is.EqualTo(
                            waterTop - CityRiverWorldBuilder.RiverBedDepth)
                        .Within(0.25f),
                    $"'{floor.name}' is not at the authored depth; the " +
                    "shader's depth fade is calibrated against it.");
                Assert.That(
                    floor.bounds.min.x,
                    Is.LessThan(segment.WaterBounds.xMin),
                    "The floor must run under the walls, not meet them.");
                Assert.That(
                    floor.bounds.max.x,
                    Is.GreaterThan(segment.WaterBounds.xMax),
                    "The floor must run under the walls, not meet them.");
            }

            foreach (Renderer side in sides)
            {
                CityRiverSegmentDescriptor segment = NearestSegment(
                    side.bounds.center.z);

                // A side follows the fall, so its own extremes are the
                // two ends of the segment, not its average: the top must
                // clear the water at the upstream end and the bottom
                // must reach the floor at the downstream one.
                float highestWaterTop = Mathf.Max(
                    segment.SouthWaterY,
                    segment.NorthWaterY) + WaterTopOffset;
                float lowestWaterTop = Mathf.Min(
                    segment.SouthWaterY,
                    segment.NorthWaterY) + WaterTopOffset;
                Assert.That(
                    side.bounds.max.y,
                    Is.LessThan(highestWaterTop),
                    $"'{side.name}' breaks the surface; it is meant to " +
                    "be under the water, not in it.");
                Assert.That(
                    side.bounds.min.y,
                    Is.LessThan(
                        lowestWaterTop -
                        CityRiverWorldBuilder.RiverBedDepth +
                        0.3f),
                    $"'{side.name}' does not reach the channel floor.");
            }
        }

        /// <summary>
        /// A pier used to stop at the plan datum, which is 12 cm clear of
        /// the water top - invisible under an opaque lid, and a pier
        /// hanging in mid-air under a transparent one.
        /// </summary>
        [Test]
        public void BridgePiers_ReachTheRiverbed()
        {
            Transform bridges = FindChild(river.transform, "River Bridges");
            var piers = new List<Renderer>();
            foreach (Renderer renderer in
                     bridges.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name.StartsWith("Bridge Pier"))
                {
                    piers.Add(renderer);
                }
            }

            Assert.That(
                piers,
                Has.Count.GreaterThanOrEqualTo(4),
                "Two road bridges carry two piers each.");
            foreach (Renderer pier in piers)
            {
                CityRiverSegmentDescriptor segment = NearestSegment(
                    pier.bounds.center.z);
                float floorY = segment.AverageWaterY +
                               WaterTopOffset -
                               CityRiverWorldBuilder.RiverBedDepth;
                Assert.That(
                    pier.bounds.min.y,
                    Is.LessThanOrEqualTo(floorY + 0.25f),
                    $"'{pier.name}' stops above the channel floor and " +
                    "reads as floating once the water is clear.");
            }
        }

        /// <summary>
        /// The water stays untouchable and stays undivided: no collider,
        /// because nothing may stand in the river, and no property block,
        /// because night factor and rain intensity are pushed once into
        /// the shared material and a block would shadow them on every
        /// segment it was written to.
        /// </summary>
        [Test]
        public void Water_CarriesNoColliderAndNoPropertyBlock()
        {
            Transform water = FindChild(river.transform, "Flowing Water");
            Assert.That(
                water.GetComponentsInChildren<Collider>(true),
                Is.Empty,
                "The river is kept out of the walkable area by plan, and " +
                "must not become standable by accident.");

            Material shared = CityRiverResources.WaterMaterial;
            foreach (Renderer renderer in
                     water.GetComponentsInChildren<Renderer>(true))
            {
                Assert.That(
                    renderer.sharedMaterial,
                    Is.SameAs(shared),
                    $"'{renderer.name}' must share the one water " +
                    "material.");
                Assert.That(
                    renderer.HasPropertyBlock(),
                    Is.False,
                    $"'{renderer.name}' carries a property block, which " +
                    "shadows the material-wide night and rain values.");
            }
        }

        /// <summary>
        /// The sheets the shader samples are on the shared material, not
        /// on a per-renderer block, and they are the sheets the contract
        /// names.
        /// </summary>
        [Test]
        public void WaterMaterial_CarriesItsRippleAndFoamSheets()
        {
            Material water = CityRiverResources.WaterMaterial;
            Assert.That(
                water.GetTexture("_RippleMap"),
                Is.SameAs(
                    Resources.Load<Texture2D>(
                        CityRiverResources.RippleTextureResourcePath)));
            Assert.That(
                water.GetTexture("_FoamMap"),
                Is.SameAs(
                    Resources.Load<Texture2D>(
                        CityRiverResources.FoamTextureResourcePath)));
            Assert.That(
                water.GetFloat("_RippleTiling"),
                Is.EqualTo(CityRiverResources.RippleMetersPerTile)
                    .Within(0.0001f));
            Assert.That(
                water.GetFloat("_FoamTiling"),
                Is.EqualTo(CityRiverResources.FoamMetersPerTile)
                    .Within(0.0001f));

            Vector4 flow = water.GetVector("_FlowDirection");
            Assert.That(
                new Vector2(flow.x, flow.y),
                Is.EqualTo(CityRiverResources.RiverFlowDirection),
                "The river must flow north, into the sea.");
        }

        /// <summary>
        /// The shader compiles, and it is queued where the rest of the
        /// frame expects it.
        ///
        /// Nothing else catches a broken water shader: the material is
        /// built from `Shader.Find`, so a shader that fails to compile
        /// still resolves, still binds, and shows up as magenta the first
        /// time somebody walks to the embankment. The queue matters for
        /// the same reason it was chosen - the water writes depth and
        /// outputs opaque, so it has to be laid down before the light
        /// halos and atmosphere particles that test against it.
        /// </summary>
        [Test]
        public void WaterShader_CompilesAndDrawsBeforeTheTransparents()
        {
            Material water = CityRiverResources.WaterMaterial;
            Shader shader = water.shader;
            Assert.That(shader, Is.Not.Null);
            Assert.That(
                shader.name,
                Is.EqualTo("Bar Promenade/City River Water"));

            if (ShaderUtil.ShaderHasError(shader))
            {
                var report = new StringBuilder(
                    "The water shader does not compile:");
                foreach (var message in
                         ShaderUtil.GetShaderMessages(shader))
                {
                    report.Append("\n  ")
                        .Append(message.file)
                        .Append('(')
                        .Append(message.line)
                        .Append("): ")
                        .Append(message.message);
                }

                Assert.Fail(report.ToString());
            }

            Assert.That(
                shader.isSupported,
                Is.True,
                "The water shader is not supported on this platform.");
            Assert.That(
                water.renderQueue,
                Is.EqualTo((int)RenderQueue.Transparent - 100),
                "The water must draw ahead of the halos and particles " +
                "that expect to depth-test against it.");
        }

        /// <summary>
        /// How far under the water top a full quay wall's underside
        /// lands. `BuildFullQuayWallSpan` centres its span at
        /// `datum - 0.08` and gives it the bank-to-datum drop plus a
        /// `0.32` extension, so the underside sits `0.08 + 0.32/2 = 0.24`
        /// under the datum whatever the bank height - and the water top
        /// is itself `0.12` under the datum.
        /// </summary>
        private const float QuayWallUndersideBelowWaterTop =
            0.08f + 0.32f * 0.5f + WaterTopOffset;

        private CityRiverSegmentDescriptor NearestSegment(float z)
        {
            CityRiverSegmentDescriptor nearest =
                layout.River.Segments[0];
            float best = float.MaxValue;
            for (int index = 0;
                 index < layout.River.Segments.Count;
                 index++)
            {
                CityRiverSegmentDescriptor candidate =
                    layout.River.Segments[index];
                float distance = Mathf.Abs(
                    candidate.WaterBounds.center.y - z);
                if (distance < best)
                {
                    best = distance;
                    nearest = candidate;
                }
            }

            return nearest;
        }

        private static Renderer FindNamed(
            List<Renderer> renderers,
            int cellZ)
        {
            string suffix = " " + cellZ.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            foreach (Renderer candidate in renderers)
            {
                if (candidate.name.EndsWith(
                        suffix,
                        System.StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            Assert.Fail($"No channel floor built for river cell {cellZ}.");
            return null;
        }

        private static Transform FindChild(Transform root, string name)
        {
            Transform child = root.Find(name);
            Assert.That(
                child,
                Is.Not.Null,
                $"The river must build a '{name}' branch.");
            return child;
        }
    }
}
