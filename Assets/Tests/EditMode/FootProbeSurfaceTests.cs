using System;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class FootProbeSurfaceTests
    {
        [Test]
        public void FootProbeLayer_IsNamedAtIndexTen()
        {
            Assert.That(FootProbeSurface.LayerIndex, Is.EqualTo(10));
            Assert.That(FootProbeSurface.LayerName, Is.EqualTo("FootProbe"));
            Assert.That(
                LayerMask.NameToLayer(FootProbeSurface.LayerName),
                Is.EqualTo(FootProbeSurface.LayerIndex));
            Assert.That(
                LayerMask.NameToLayer("FootProbe"),
                Is.EqualTo(10));

            // It is its own layer, apart from every walking body.
            Assert.That(
                FootProbeSurface.LayerIndex,
                Is.Not.EqualTo(CityPedestrianCollision.LayerIndex));
            Assert.That(
                FootProbeSurface.LayerIndex,
                Is.Not.EqualTo(CityBusCollision.LayerIndex));

            // The probes still see it: it is inside the default raycast mask.
            Assert.That(
                FootProbeSurface.ProbeMask & (1 << FootProbeSurface.LayerIndex),
                Is.Not.EqualTo(0));

            Assert.DoesNotThrow(() => FootProbeSurface.EnsureRuntimePolicy());
        }

        [Test]
        public void AddTreadCollider_SetsLayerAndIgnoresWalkingBodies()
        {
            int[] hiddenFrom =
            {
                0,
                CityPedestrianCollision.LayerIndex,
                CityBusCollision.LayerIndex,
                FootProbeSurface.LayerIndex
            };
            GameObject tread = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject bare = null;

            try
            {
                // Open the matrix and forget the policy, so this call has
                // to apply it itself rather than inherit it from an earlier
                // builder in the same editor session.
                foreach (int layer in hiddenFrom)
                {
                    Physics.IgnoreLayerCollision(
                        layer,
                        FootProbeSurface.LayerIndex,
                        false);
                }

                FootProbeSurface.ResetPolicyForTests();

                int treadLayerBefore = tread.layer;
                BoxCollider collider = FootProbeSurface.AddTreadCollider(tread);

                // The probe collider lives on a child on the FootProbe
                // layer; the visible tread keeps its own layer and its own
                // colliders, so no camera mask loses the step.
                Assert.That(collider, Is.Not.Null);
                Assert.That(collider.gameObject, Is.Not.SameAs(tread));
                Assert.That(
                    collider.transform.parent,
                    Is.SameAs(tread.transform));
                Assert.That(
                    collider.gameObject.name,
                    Is.EqualTo(FootProbeSurface.ProbeChildName));
                Assert.That(collider.isTrigger, Is.True);
                Assert.That(collider.enabled, Is.True);
                Assert.That(collider.size, Is.EqualTo(Vector3.one));
                Assert.That(
                    collider.gameObject.layer,
                    Is.EqualTo(FootProbeSurface.LayerIndex));
                Assert.That(collider.gameObject.layer, Is.EqualTo(10));
                Assert.That(tread.layer, Is.EqualTo(treadLayerBefore));
                Assert.That(
                    tread.GetComponents<Collider>(),
                    Has.Length.EqualTo(1),
                    "The primitive's own collider is left alone.");
                Assert.That(FootProbeSurface.IsProbeSurface(collider), Is.True);
                Assert.That(
                    FootProbeSurface.IsProbeSurface(
                        tread.GetComponent<Collider>()),
                    Is.False);
                foreach (int layer in hiddenFrom)
                {
                    Assert.That(
                        Physics.GetIgnoreLayerCollision(
                            layer,
                            FootProbeSurface.LayerIndex),
                        Is.True,
                        $"Layer {layer} must not collide with the treads.");
                }

                // A bare object receives one probe child too.
                bare = new GameObject("Bare Tread");
                BoxCollider added = FootProbeSurface.AddTreadCollider(bare);
                Assert.That(added, Is.Not.Null);
                Assert.That(added.transform.parent, Is.SameAs(bare.transform));
                Assert.That(
                    bare.GetComponentsInChildren<Collider>(true),
                    Has.Length.EqualTo(1));
                Assert.That(bare.layer, Is.EqualTo(0));

                // Calling it twice on the same tread keeps one collider.
                Assert.That(
                    FootProbeSurface.AddTreadCollider(bare),
                    Is.SameAs(added));
                Assert.That(
                    bare.GetComponentsInChildren<Collider>(true),
                    Has.Length.EqualTo(1));

                Assert.Throws<ArgumentNullException>(
                    () => FootProbeSurface.AddTreadCollider(null));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tread);
                if (bare != null)
                {
                    UnityEngine.Object.DestroyImmediate(bare);
                }

                // Leave the matrix the way the runtime wants it whatever
                // happened above.
                FootProbeSurface.ResetPolicyForTests();
                FootProbeSurface.EnsureRuntimePolicy();
            }
        }
    }
}
