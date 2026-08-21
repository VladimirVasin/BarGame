using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The deformable bed surfaces are the project's first per-frame
    /// written meshes. These tests pin the build contract (grid layout,
    /// rest silhouette, texturing, bounds allowance) and the depression
    /// model's physics: depth follows penetration, the ring never moves,
    /// denting is fast, recovery is slow, and everything is deterministic.
    /// </summary>
    public sealed class HomeBedDeformableSurfaceTests
    {
        private const float Tolerance = 0.0005f;

        private static readonly Vector3 MattressSize = new Vector3(
            2.39f,
            HomeInteriorWorldBuilder.BedMattressThickness,
            1.57f);

        [Test]
        public void Factory_BuildsTheGridWithTheRestSilhouetteOfTheBox()
        {
            var parent = new GameObject("Bed Surface Factory Test");
            try
            {
                GameObject surfaceObject =
                    HomeBedDeformableSurfaceFactory
                        .CreateDeformableSurface(
                            "Test Mattress",
                            parent.transform,
                            new Vector3(0f, 0.47f, 0f),
                            MattressSize,
                            Color.gray,
                            HomeSurfaceKind.BedLinen,
                            SurfaceProjection.BoxXZ,
                            HomeInteriorWorldBuilder
                                .BedSleeperSinkDepth);
                HomeBedDeformableSurface surface =
                    surfaceObject
                        .GetComponent<HomeBedDeformableSurface>();
                Assert.That(surface, Is.Not.Null);
                Assert.That(surface.Columns, Is.EqualTo(17));
                Assert.That(surface.Rows, Is.EqualTo(11));
                Assert.That(
                    surface.TopVertexCount,
                    Is.EqualTo(surface.Columns * surface.Rows * 4),
                    "Per-cell quads: faceted shading is what makes the " +
                    "dent readable on this project's noisy albedos.");
                Assert.That(
                    surface.VertexCount,
                    Is.EqualTo(surface.TopVertexCount + 20),
                    "The top cells plus five flat quad faces.");

                Renderer renderer =
                    surfaceObject.GetComponent<Renderer>();
                Assert.That(
                    renderer.sharedMaterial,
                    Is.SameAs(
                        RuntimePrimitiveFactory.DefaultMaterial),
                    "The surface must ride the shared home material so " +
                    "the appearance and occlusion passes accept it.");
                Assert.That(
                    renderer.bounds.max.y,
                    Is.EqualTo(0.47f + (MattressSize.y * 0.5f))
                        .Within(Tolerance),
                    "The rest top must sit exactly where the box top " +
                    "sat — live tests assert it.");
                Assert.That(
                    renderer.bounds.size.x,
                    Is.EqualTo(MattressSize.x).Within(Tolerance));
                Assert.That(
                    renderer.bounds.size.z,
                    Is.EqualTo(MattressSize.z).Within(Tolerance));

                Mesh mesh = surfaceObject
                    .GetComponent<MeshFilter>().sharedMesh;
                Assert.That(
                    mesh.bounds.min.y,
                    Is.EqualTo(
                        (-MattressSize.y * 0.5f) -
                        HomeInteriorWorldBuilder.BedSleeperSinkDepth)
                        .Within(Tolerance),
                    "The bounds carry a downward allowance for the dent " +
                    "so a dented surface cannot cull itself.");

                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                Assert.That(
                    properties.GetTexture(
                        Shader.PropertyToID("_BaseMap")),
                    Is.Not.Null,
                    "The surface must carry the packaged BedLinen " +
                    "texture like every ordinary home renderer.");
                Vector4 tiling = properties.GetVector(
                    Shader.PropertyToID("_BaseMap_ST"));
                Assert.That(tiling.x, Is.GreaterThan(0f));
                Assert.That(tiling.y, Is.GreaterThan(0f));
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Factory_TopGridUvsSpanZeroToOne()
        {
            var parent = new GameObject("Bed Surface Factory Test");
            try
            {
                GameObject surfaceObject =
                    HomeBedDeformableSurfaceFactory
                        .CreateDeformableSurface(
                            "Test Mattress",
                            parent.transform,
                            Vector3.zero,
                            MattressSize,
                            Color.gray,
                            HomeSurfaceKind.BedLinen,
                            SurfaceProjection.BoxXZ,
                            0.045f);
                HomeBedDeformableSurface surface =
                    surfaceObject
                        .GetComponent<HomeBedDeformableSurface>();
                Mesh mesh = surface.Mesh;
                Vector2[] uvs = mesh.uv;
                for (int index = 0;
                     index < surface.TopVertexCount;
                     index++)
                {
                    Assert.That(
                        uvs[index].x,
                        Is.InRange(0f, 1f));
                    Assert.That(
                        uvs[index].y,
                        Is.InRange(0f, 1f));
                }

                // Within every cell quad the ST transform scales 0..1
                // face UVs, so a shuffled corner layout would tile wrong.
                for (int quad = 0;
                     quad < surface.TopVertexCount;
                     quad += 4)
                {
                    Assert.That(
                        uvs[quad + 1].x,
                        Is.GreaterThan(uvs[quad].x));
                    Assert.That(
                        uvs[quad + 2].y,
                        Is.GreaterThan(uvs[quad].y));
                }
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Model_SurfaceMeetsTheBodyUndersideAtFullWeight()
        {
            HomeBedSurfaceDepressionModel model = CreateMattressModel();
            var sources = new[]
            {
                new HomeBedDepressionSource(
                    0f, 0f, 0.25f, 0.3f, 0.045f),
            };
            model.SetSources(sources, sources.Length);
            model.SetBodyWeight(1f);
            model.SnapToTarget();

            Assert.That(
                model.SampleDepth(0f, 0f),
                Is.EqualTo(0.045f).Within(0.0005f),
                "Inside the footprint at full weight the surface must " +
                "meet the body's underside exactly — that is the plane " +
                "the sleeping hip height was lowered to.");
        }

        [Test]
        public void Model_ThePinnedRingNeverMoves()
        {
            HomeBedSurfaceDepressionModel model = CreateMattressModel();
            var sources = new[]
            {
                // A footprint deliberately shoved against the border.
                new HomeBedDepressionSource(
                    1.1f, 0.7f, 0.4f, 0.4f, 0.045f),
            };
            model.SetSources(sources, sources.Length);
            model.SetBodyWeight(1f);
            model.SnapToTarget();

            for (int column = 0;
                 column < model.VertexCountX;
                 column++)
            {
                Assert.That(
                    model.GetDepth(column, 0),
                    Is.Zero);
                Assert.That(
                    model.GetDepth(column, model.Rows),
                    Is.Zero);
            }

            for (int row = 0; row < model.VertexCountZ; row++)
            {
                Assert.That(model.GetDepth(0, row), Is.Zero);
                Assert.That(
                    model.GetDepth(model.Columns, row),
                    Is.Zero);
            }
        }

        [Test]
        public void Model_DentsFastAndRecoversSlow()
        {
            HomeBedSurfaceDepressionModel model = CreateMattressModel();
            var sources = new[]
            {
                new HomeBedDepressionSource(
                    0f, 0f, 0.25f, 0.3f, 0.045f),
            };
            model.SetSources(sources, sources.Length);
            model.SetBodyWeight(1f);
            AdvanceInFrames(model, 0.25f);
            float afterQuarterSecond = model.SampleDepth(0f, 0f);
            Assert.That(
                afterQuarterSecond,
                Is.GreaterThan(0.045f * 0.9f),
                "Denting must keep up with a lowering body: at least " +
                "90% of the way down within a quarter second.");

            model.SetBodyWeight(0f);
            AdvanceInFrames(model, 1.5f);
            Assert.That(
                model.SampleDepth(0f, 0f),
                Is.LessThan(afterQuarterSecond * 0.06f),
                "Recovery is the slow half of the thick-cloth read: " +
                "~95% refilled over a second and a half.");
        }

        [Test]
        public void Model_IsDeterministicAcrossIdenticalStepSequences()
        {
            HomeBedSurfaceDepressionModel first = CreateMattressModel();
            HomeBedSurfaceDepressionModel second = CreateMattressModel();
            var sources = new[]
            {
                new HomeBedDepressionSource(
                    -0.3f, 0.1f, 0.2f, 0.25f, 0.038f),
                new HomeBedDepressionSource(
                    0.6f, -0.2f, 0.1f, 0.12f, 0.021f),
            };
            float[] steps =
            {
                0.013f, 0.021f, 0.008f, 0.033f, 0.016f, 0.05f,
            };
            foreach (HomeBedSurfaceDepressionModel model in
                     new[] { first, second })
            {
                model.SetSources(sources, sources.Length);
                model.SetBodyWeight(1f);
                foreach (float step in steps)
                {
                    model.Advance(step);
                }
            }

            for (int row = 0; row < first.VertexCountZ; row++)
            {
                for (int column = 0;
                     column < first.VertexCountX;
                     column++)
                {
                    Assert.That(
                        second.GetDepth(column, row),
                        Is.EqualTo(first.GetDepth(column, row)),
                        "Identical step sequences must produce " +
                        "bit-identical depths.");
                }
            }
        }

        [Test]
        public void Model_RaisesAWeltAroundTheFootprint()
        {
            HomeBedSurfaceDepressionModel model = CreateMattressModel();
            var sources = new[]
            {
                new HomeBedDepressionSource(
                    0f, 0f, 0.25f, 0.3f, 0.045f),
            };
            model.SetSources(sources, sources.Length);
            model.SetBodyWeight(1f);
            model.SnapToTarget();

            // Mid-skirt beyond the footprint the displaced bedding humps
            // UP — the lit welt is what makes the hollow read on camera.
            float weltDepth = model.SampleDepth(0.25f + 0.14f, 0f);
            Assert.That(
                weltDepth,
                Is.LessThan(-0.012f),
                "The rim of the dent must bulge upward, not stay flat.");
            Assert.That(
                model.SampleDepth(0f, 0f),
                Is.EqualTo(0.045f).Within(0.0005f),
                "while the hollow under the body keeps its full depth.");
        }

        [Test]
        public void Model_ShadowRectCapsTheDentUnderThePillow()
        {
            HomeBedSurfaceDepressionModel model = CreateMattressModel();
            model.SetShadowRect(-1.024f, -0.525f, -0.404f, 0.525f, 0.028f);
            var sources = new[]
            {
                // A chest-like footprint reaching over the pillow edge.
                new HomeBedDepressionSource(
                    -0.25f, 0f, 0.25f, 0.2f, 0.045f),
            };
            model.SetSources(sources, sources.Length);
            model.SetBodyWeight(1f);
            model.SnapToTarget();

            Assert.That(
                model.SampleDepth(-0.45f, 0f),
                Is.LessThanOrEqualTo(0.028f + 0.0005f),
                "The mattress dent under the pillow footprint must stay " +
                "shallower than the pillow's embed, or a gap opens " +
                "beneath its rigid box.");
            Assert.That(
                model.SampleDepth(0f, 0f),
                Is.EqualTo(0.045f).Within(0.0005f),
                "and the cap must not leak outside the rectangle.");
        }

        [Test]
        public void BodyWeight_FollowsTheSeatWindows()
        {
            PlayerAnimatedInteractionDefinition definition =
                new PlayerAnimatedInteractionDefinition(
                    "BedEnter",
                    "BedSleepLoop",
                    "BedExit",
                    enterFrameCount:
                        HomeBedInteraction.BedEnterFrameCount,
                    enterFramesPerSecond: 12f,
                    loopFrameCount: 16,
                    loopFramesPerSecond: 4f,
                    exitFrameCount:
                        HomeBedInteraction.BedExitFrameCount,
                    exitFramesPerSecond: 12f);

            // Seated on the edge during the lie-down: no weight yet.
            int seatedFrame = (int)(
                HomeBedInteractionPlan.EnterSeatDepartureProgress *
                definition.EnterFrameCount) - 1;
            Assert.That(
                HomeBedSurfaceDeformer.ResolveBodyWeight(
                    PlayerAnimatedInteractionPhase.Entering,
                    seatedFrame,
                    definition),
                Is.Zero);

            // Fully lying at the clip's end.
            Assert.That(
                HomeBedSurfaceDeformer.ResolveBodyWeight(
                    PlayerAnimatedInteractionPhase.Entering,
                    definition.EnterFrameCount - 1,
                    definition),
                Is.GreaterThan(0.9f));
            Assert.That(
                HomeBedSurfaceDeformer.ResolveBodyWeight(
                    PlayerAnimatedInteractionPhase.Looping,
                    definition.LoopStartFrame,
                    definition),
                Is.EqualTo(1f));

            // Waking keeps full weight for the whole exit: the dent must
            // linger and refill visibly BEHIND the rising body — sources
            // vanish naturally as parts leave the rest plane, so an early
            // weight ramp would only erase the dent while it is still
            // hidden under him.
            int exitSeatFrame = definition.ExitStartFrame + (int)(
                HomeBedInteractionPlan.ExitSeatArrivalProgress *
                definition.ExitFrameCount) + 1;
            Assert.That(
                HomeBedSurfaceDeformer.ResolveBodyWeight(
                    PlayerAnimatedInteractionPhase.Exiting,
                    exitSeatFrame,
                    definition),
                Is.EqualTo(1f));
            Assert.That(
                HomeBedSurfaceDeformer.ResolveBodyWeight(
                    PlayerAnimatedInteractionPhase.Exiting,
                    definition.ExitStartFrame,
                    definition),
                Is.EqualTo(1f));

            // Standing around, positioning, sitting: nothing.
            Assert.That(
                HomeBedSurfaceDeformer.ResolveBodyWeight(
                    PlayerAnimatedInteractionPhase.Idle,
                    0,
                    definition),
                Is.Zero);
            Assert.That(
                HomeBedSurfaceDeformer.ResolveBodyWeight(
                    PlayerAnimatedInteractionPhase.Positioning,
                    0,
                    definition),
                Is.Zero);
        }

        /// <summary>
        /// The model clamps a single Advance to half a second — a frame
        /// spike guard — so wall-clock spans are stepped as frames, the
        /// way the deformer feeds it.
        /// </summary>
        private static void AdvanceInFrames(
            HomeBedSurfaceDepressionModel model,
            float seconds)
        {
            const float frame = 1f / 60f;
            for (float elapsed = 0f;
                 elapsed < seconds;
                 elapsed += frame)
            {
                model.Advance(frame);
            }
        }

        private static HomeBedSurfaceDepressionModel
            CreateMattressModel()
        {
            return new HomeBedSurfaceDepressionModel(
                HomeBedSurfaceDepressionSettings.Default,
                MattressSize.x,
                MattressSize.z,
                24,
                16,
                HomeInteriorWorldBuilder.BedSleeperSinkDepth);
        }
    }
}
