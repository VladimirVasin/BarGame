using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class ClothPanelFactoryTests
    {
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");

        [Test]
        public void CreateHangingRag_BuildsSkinnedClothWithOwnedMesh()
        {
            var parent = new GameObject("Cloth Factory Test");
            try
            {
                Color color = new Color(0.3f, 0.2f, 0.1f);
                GameObject rag = ClothPanelFactory.CreateHangingRag(
                    "Test Rag",
                    parent.transform,
                    new Vector3(1f, 3f, -2f),
                    35f,
                    0.6f,
                    1.2f,
                    color);

                Assert.That(rag.name, Is.EqualTo("Test Rag"));
                Assert.That(
                    rag.transform.parent,
                    Is.SameAs(parent.transform));
                Assert.That(
                    rag.transform.localPosition,
                    Is.EqualTo(new Vector3(1f, 3f, -2f)));

                var renderer =
                    rag.GetComponent<SkinnedMeshRenderer>();
                Assert.That(renderer, Is.Not.Null);
                Assert.That(
                    renderer.rootBone,
                    Is.SameAs(rag.transform));
                // One shared cull-off clone of the primitive material
                // renders the back face of the single-sided sim mesh.
                Assert.That(
                    renderer.sharedMaterial,
                    Is.SameAs(ClothPanelFactory.TwoSidedMaterial));
                Assert.That(
                    renderer.sharedMaterial.shader,
                    Is.SameAs(
                        RuntimePrimitiveFactory
                            .DefaultMaterial.shader));
                Assert.That(
                    renderer.sharedMaterial.GetFloat("_Cull"),
                    Is.Zero);

                Mesh mesh = renderer.sharedMesh;
                Assert.That(mesh, Is.Not.Null);
                int expectedVertices =
                    (ClothPanelFactory.DefaultColumns + 1) *
                    (ClothPanelFactory.DefaultRows + 1);
                Assert.That(
                    mesh.vertexCount,
                    Is.EqualTo(expectedVertices));

                // Strictly single-sided sim topology — a reversed
                // duplicate would cancel the cloth normals.
                int frontIndices =
                    ClothPanelFactory.DefaultColumns *
                    ClothPanelFactory.DefaultRows * 6;
                Assert.That(
                    (int)mesh.GetIndexCount(0),
                    Is.EqualTo(frontIndices));
                Assert.That(mesh.boneWeights.Length,
                    Is.EqualTo(expectedVertices));
                Assert.That(mesh.bindposes.Length, Is.EqualTo(1));

                Assert.That(
                    rag.GetComponent<RuntimeGeneratedMeshOwner>(),
                    Is.Not.Null);

                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                Color applied = properties.GetColor(BaseColorId);
                Assert.That(
                    applied.r,
                    Is.EqualTo(color.r).Within(0.001f));
                Assert.That(
                    applied.g,
                    Is.EqualTo(color.g).Within(0.001f));
                Assert.That(
                    applied.b,
                    Is.EqualTo(color.b).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void CreateHangingRag_PinsExactlyTheTopRow()
        {
            var parent = new GameObject("Cloth Pinning Test");
            try
            {
                const float Height = 1.0f;
                GameObject rag = ClothPanelFactory.CreateHangingRag(
                    "Pinned Rag",
                    parent.transform,
                    Vector3.zero,
                    0f,
                    0.5f,
                    Height,
                    Color.gray);
                var cloth = rag.GetComponent<Cloth>();
                Assert.That(cloth, Is.Not.Null);

                // Coefficients index welded cloth particles, never the
                // raw mesh vertices.
                Vector3[] particles = cloth.vertices;
                ClothSkinningCoefficient[] coefficients =
                    cloth.coefficients;
                Assert.That(
                    coefficients.Length,
                    Is.EqualTo(particles.Length));

                int pinned = 0;
                for (int index = 0;
                     index < coefficients.Length;
                     index++)
                {
                    bool topRow =
                        particles[index].y >
                        ClothPanelFactory.PinnedTopThreshold;
                    if (topRow)
                    {
                        pinned++;
                        Assert.That(
                            coefficients[index].maxDistance,
                            Is.Zero);
                    }
                    else
                    {
                        Assert.That(
                            coefficients[index].maxDistance,
                            Is.EqualTo(
                                Height *
                                ClothPanelFactory
                                    .FreeTravelHeightFraction)
                                .Within(0.0001f));
                    }
                }

                Assert.That(
                    pinned,
                    Is.EqualTo(ClothPanelFactory.DefaultColumns + 1));
                Assert.That(cloth.useGravity, Is.True);
                Assert.That(cloth.worldVelocityScale, Is.Zero);
                Assert.That(cloth.worldAccelerationScale, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void CreateHangingRag_TornVariantIsRaggedAndDeterministic()
        {
            var parent = new GameObject("Cloth Torn Test");
            try
            {
                GameObject first = ClothPanelFactory.CreateHangingRag(
                    "Torn Rag A",
                    parent.transform,
                    Vector3.zero,
                    0f,
                    0.5f,
                    1.0f,
                    Color.gray,
                    3);
                GameObject second = ClothPanelFactory.CreateHangingRag(
                    "Torn Rag B",
                    parent.transform,
                    Vector3.zero,
                    0f,
                    0.5f,
                    1.0f,
                    Color.gray,
                    3);
                GameObject straight =
                    ClothPanelFactory.CreateHangingRag(
                        "Straight Rag",
                        parent.transform,
                        Vector3.zero,
                        0f,
                        0.5f,
                        1.0f,
                        Color.gray);

                Vector3[] tornVertices = first
                    .GetComponent<SkinnedMeshRenderer>()
                    .sharedMesh.vertices;
                Vector3[] repeatVertices = second
                    .GetComponent<SkinnedMeshRenderer>()
                    .sharedMesh.vertices;
                Vector3[] straightVertices = straight
                    .GetComponent<SkinnedMeshRenderer>()
                    .sharedMesh.vertices;

                Assert.That(
                    tornVertices,
                    Is.EqualTo(repeatVertices));

                bool anyLifted = false;
                for (int index = 0;
                     index < tornVertices.Length;
                     index++)
                {
                    if (tornVertices[index].y >
                        straightVertices[index].y + 0.001f)
                    {
                        anyLifted = true;
                        break;
                    }
                }

                Assert.That(
                    anyLifted,
                    Is.True,
                    "A torn hem must lift at least one bottom vertex.");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }
    }
}
