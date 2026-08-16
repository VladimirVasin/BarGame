using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class BarBartenderAssetTests
    {
        [Test]
        public void Provider_BindsTheSixArmedPrefabContract()
        {
            BarBartenderProvider provider = BarBartenderProvider.Load();
            Assert.That(
                provider,
                Is.Not.Null,
                "The bartender provider asset must be addressable.");
            GameObject prefab = provider.BartenderPrefab;
            Assert.That(prefab, Is.Not.Null);

            BarBartenderAssetRegistry registry =
                prefab.GetComponent<BarBartenderAssetRegistry>();
            Assert.That(registry, Is.Not.Null);
            Assert.That(
                registry.DesignId,
                Is.EqualTo(BarBartenderProvider.DesignId));
            Assert.That(
                registry.BuildSignature,
                Has.Length.EqualTo(64));
            Assert.That(
                registry.SourceTriangleCount,
                Is.InRange(1400, 3400));
            Assert.That(
                registry.Renderers.Count,
                Is.InRange(30, 72));
            Assert.That(
                registry.RendererBindings.Count,
                Is.EqualTo(registry.Renderers.Count));

            Assert.That(
                registry.ExtraArmChains.Count,
                Is.EqualTo(
                    BarBartenderAssetRegistry.ExtraArmChainCount));
            var expectedChainIds = new[]
            {
                "Arm2.L",
                "Arm2.R",
                "Arm3.L",
                "Arm3.R"
            };
            var seenGrips = new HashSet<Transform>();
            for (int index = 0;
                 index < registry.ExtraArmChains.Count;
                 index++)
            {
                BarBartenderArmChain chain =
                    registry.ExtraArmChains[index];
                Assert.That(
                    chain.ChainId,
                    Is.EqualTo(expectedChainIds[index]));
                Assert.That(chain.ShoulderPivot, Is.Not.Null);
                Assert.That(chain.ElbowPivot, Is.Not.Null);
                Assert.That(chain.WristPivot, Is.Not.Null);
                Assert.That(chain.GripPivot, Is.Not.Null);
                Assert.That(
                    seenGrips.Add(chain.GripPivot),
                    Is.True,
                    "Every chain must own a distinct grip pivot.");

                // The authored chain runs outward: shoulder to elbow
                // to wrist to grip, each strictly further from the
                // torso midline than the last.
                float shoulderReach = Mathf.Abs(
                    chain.ShoulderPivot.position.x);
                float wristReach = Mathf.Abs(
                    chain.WristPivot.position.x);
                Assert.That(
                    wristReach,
                    Is.GreaterThan(shoulderReach),
                    $"{chain.ChainId} rest pose must reach outward.");
            }

            Assert.That(
                registry.Chest,
                Is.Not.Null,
                "The chains re-parent under the chest at runtime.");
            Assert.That(registry.Head, Is.Not.Null);
            Assert.That(registry.LeftUpperArm, Is.Not.Null);
            Assert.That(registry.RightUpperArm, Is.Not.Null);

            Assert.That(
                prefab.GetComponentsInChildren<Collider>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<Light>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<Rigidbody>(true),
                Is.Empty);
            Assert.That(
                registry.LocalBounds.size.y,
                Is.EqualTo(2.00f).Within(0.05f));
        }
    }
}
