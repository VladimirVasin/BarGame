using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The three authored bridge pieces exist, load from Resources under
    /// their pinned names, carry renderers and stay tiny.
    /// </summary>
    public sealed class HomeShowerBridgeResourcesTests
    {
        [Test]
        public void EveryPieceLoadsWithGeometry()
        {
            int triangles = 0;
            foreach (string name in HomeShowerBridgeResources.ModelNames)
            {
                GameObject template = HomeShowerBridgeResources.LoadTemplate(name);
                Assert.That(template, Is.Not.Null, name + " must be built by tools/build-home-shower-action-3d-model.py.");
                MeshFilter[] filters = template.GetComponentsInChildren<MeshFilter>(true);
                Assert.That(filters, Is.Not.Empty, name);
                foreach (MeshFilter filter in filters)
                {
                    Assert.That(filter.sharedMesh, Is.Not.Null, name);
                    triangles += filter.sharedMesh.triangles.Length / 3;
                }
            }

            Assert.That(triangles, Is.GreaterThan(100).And.LessThan(600));
        }

        [Test]
        public void TryCreateParentsAPivotWithRenderersAndNoLiveColliders()
        {
            var parent = new GameObject("Bridge Test Parent").transform;
            try
            {
                Assert.That(HomeShowerBridgeResources.TryCreate(HomeShowerBridgeResources.ShoulderYoke, parent, out Transform pivot), Is.True);
                Assert.That(pivot, Is.Not.Null);
                Assert.That(pivot.parent, Is.EqualTo(parent));
                Assert.That(pivot.GetComponentsInChildren<Renderer>(true), Is.Not.Empty);
                foreach (Collider collider in pivot.GetComponentsInChildren<Collider>(true))
                {
                    Assert.That(collider.enabled, Is.False);
                }

                Assert.That(HomeShowerBridgeResources.TryCreate("NoSuchPiece", parent, out Transform missing), Is.False);
                Assert.That(missing, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(parent.gameObject);
            }
        }
    }
}
