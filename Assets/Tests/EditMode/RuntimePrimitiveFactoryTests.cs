using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class RuntimePrimitiveFactoryTests
    {
        [Test]
        public void CylindersReuseEightSidedPresentationMesh()
        {
            GameObject first = RuntimePrimitiveFactory.CreateCylinder(
                "First",
                null,
                Vector3.zero,
                Vector3.one,
                Color.white,
                false);
            GameObject second = RuntimePrimitiveFactory.CreateCylinder(
                "Second",
                null,
                Vector3.zero,
                Vector3.one,
                Color.white,
                false);

            try
            {
                Mesh firstMesh = first.GetComponent<MeshFilter>().sharedMesh;
                Mesh secondMesh = second.GetComponent<MeshFilter>().sharedMesh;

                Assert.That(firstMesh, Is.SameAs(secondMesh));
                Assert.That(firstMesh.vertexCount, Is.EqualTo(50));
                Assert.That(firstMesh.triangles.Length / 3, Is.EqualTo(32));
                Assert.That(
                    firstMesh.name,
                    Is.EqualTo("Shared PS1 Eight-Sided Cylinder"));
                Assert.That(first.GetComponent<Collider>(), Is.Null);
                Assert.That(second.GetComponent<Collider>(), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void CombinedBoxesUseOneColliderFreeRenderer()
        {
            Bounds[] boxes =
            {
                new Bounds(
                    new Vector3(-1.5f, 0.5f, 0f),
                    new Vector3(2f, 1f, 0.25f)),
                new Bounds(
                    new Vector3(1.5f, 0.75f, 0f),
                    new Vector3(1f, 1.5f, 0.5f))
            };
            GameObject combined =
                RuntimePrimitiveFactory.CreateCombinedBoxes(
                    "Combined",
                    null,
                    boxes,
                    Color.yellow);

            try
            {
                Mesh mesh =
                    combined.GetComponent<MeshFilter>().sharedMesh;

                Assert.That(mesh, Is.Not.Null);
                Assert.That(mesh.vertexCount, Is.EqualTo(48));
                Assert.That(
                    mesh.triangles.Length / 3,
                    Is.EqualTo(24));
                Assert.That(
                    combined.GetComponentsInChildren<Renderer>(true),
                    Has.Length.EqualTo(1));
                Assert.That(
                    combined.GetComponentsInChildren<Collider>(true),
                    Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(combined);
            }
        }

        [Test]
        public void CombinedBoxesCanShareTheirMeshWithAStaticCollider()
        {
            Bounds[] boxes =
            {
                new Bounds(
                    new Vector3(-1f, 0.08f, 0f),
                    new Vector3(2f, 0.16f, 1f)),
                new Bounds(
                    new Vector3(1f, 0.08f, 0f),
                    new Vector3(2f, 0.16f, 1f))
            };
            GameObject combined =
                RuntimePrimitiveFactory.CreateCombinedBoxes(
                    "Walkable Combined",
                    null,
                    boxes,
                    Color.gray,
                    true);

            try
            {
                Mesh renderMesh =
                    combined.GetComponent<MeshFilter>().sharedMesh;
                MeshCollider meshCollider =
                    combined.GetComponent<MeshCollider>();

                Assert.That(meshCollider, Is.Not.Null);
                Assert.That(
                    combined.GetComponents<Collider>(),
                    Has.Length.EqualTo(1));
                Assert.That(
                    meshCollider.sharedMesh,
                    Is.SameAs(renderMesh));
                Assert.That(meshCollider.convex, Is.False);
                Assert.That(
                    meshCollider.bounds.max.y,
                    Is.EqualTo(0.16f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(combined);
            }
        }
    }
}
