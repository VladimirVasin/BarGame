using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class MountainCablewayTests
    {
        [Test]
        [Category("MountainRoad")]
        public void Motion_UsesSaggedTracksAndContinuousHiddenTurns()
        {
            MountainRoadCablewayPlan plan = MountainRoadPlanner.Create(
                GameSessionState.DefaultCitySeed).Terminal.Cableway;
            float turnLength = Mathf.PI * plan.TurnRadius;
            float[] boundaries =
            {
                plan.LineLength,
                plan.LineLength + turnLength,
                plan.LineLength * 2f + turnLength,
                plan.LoopLength
            };
            const float epsilon = 0.001f;
            for (int index = 0; index < boundaries.Length; index++)
            {
                Vector3 before = MountainCablewayMotion.Sample(
                    plan,
                    boundaries[index] - epsilon).Position;
                Vector3 after = MountainCablewayMotion.Sample(
                    plan,
                    boundaries[index] + epsilon).Position;
                Assert.That(
                    Vector3.Distance(before, after),
                    Is.LessThan(0.015f),
                    $"Cable loop jumps at boundary {index}.");
            }

            MountainCablewayNodeDescriptor first = plan.Nodes[1];
            MountainCablewayNodeDescriptor second = plan.Nodes[2];
            float midpoint = (first.Distance + second.Distance) * 0.5f;
            float linearHeight =
                (first.CableCenter.y + second.CableCenter.y) * 0.5f;
            float sampledHeight = MountainCablewayMotion
                .SampleTrackPosition(plan, midpoint, 1).y;
            Assert.That(sampledHeight, Is.LessThan(linearHeight - 0.2f));
            Assert.That(
                MountainCablewayMotion.CountForwardCrossings(
                    plan.LoopLength - 0.1f,
                    plan.LoopLength + 0.1f,
                    0f,
                    plan.LoopLength),
                Is.EqualTo(1));
        }

        [Test]
        [Category("MountainRoad")]
        public void WorldBuilder_KeepsOnlyLowerStationPhysical()
        {
            MountainRoadCablewayPlan plan = MountainRoadPlanner.Create(
                GameSessionState.DefaultCitySeed).Terminal.Cableway;
            var host = new GameObject("Cableway Test Host");
            try
            {
                MountainCablewayWorldResult result =
                    MountainCablewayWorldBuilder.Build(
                        host.transform,
                        plan);
                Assert.That(result.Cabins, Has.Count.EqualTo(4));
                Assert.That(result.Supports, Has.Count.EqualTo(3));
                Assert.That(result.StationLight.shadows,
                    Is.EqualTo(LightShadows.None));
                // Two: the lens over the platform and the flood on the
                // outer canopy edge that reaches the freight kerb and the
                // yard. One lamp under a canopy lights only what it hangs
                // over, which left the station a dark shape with a glow
                // inside it.
                Assert.That(
                    result.Root.GetComponentsInChildren<Light>(true),
                    Has.Length.EqualTo(2));
                Assert.That(result.Controller.AudioSources,
                    Has.Count.EqualTo(5));
                Assert.That(
                    result.StationRoot.GetComponentsInChildren<Collider>(true)
                        .Length,
                    Is.GreaterThan(0));

                Assert.That(
                    result.Cabins.All(cabin =>
                        cabin.GetComponentsInChildren<Collider>(true)
                            .Length == 0 &&
                        cabin.GetComponentsInChildren<Rigidbody>(true)
                            .Length == 0),
                    Is.True,
                    "Moving cabins must remain presentation-only.");
                Assert.That(
                    result.Supports.All(support =>
                        support.GetComponentsInChildren<Collider>(true)
                            .Length == 0),
                    Is.True,
                    "Remote supports must never block the world.");
                Assert.That(
                    result.Controller.AudioSources.All(source =>
                        source.spatialBlend == 1f &&
                        source.GetComponentInChildren<Renderer>() != null),
                    Is.True,
                    "Every cableway voice must belong to visible machinery.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
