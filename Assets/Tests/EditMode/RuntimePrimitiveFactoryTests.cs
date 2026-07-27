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
    }
}
