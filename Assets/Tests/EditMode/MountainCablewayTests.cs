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
                Assert.That(
                    result.Cabins,
                    Has.Count.EqualTo(
                        MountainRoadTerminalPlanner.CablewayCabinCount));
                int supportCount = plan.Nodes.Count(
                    node => node.Kind == MountainCablewayNodeKind.Support);
                Assert.That(result.Supports, Has.Count.EqualTo(supportCount));
                Assert.That(result.StationLight.shadows,
                    Is.EqualTo(LightShadows.None));
                // Three: the lens over the pad, the flood on the outer
                // canopy edge that reaches the freight kerb and the yard, and
                // the boom lamp over the boarding dock. One lamp under a
                // canopy lights only what it hangs over, which left the
                // station a dark shape with a glow inside it - and the two
                // canopy fixtures both throw BACKWARDS, which left the dock
                // itself, the one place a passenger has to find, unlit.
                Assert.That(
                    result.Root.GetComponentsInChildren<Light>(true),
                    Has.Length.EqualTo(3));

                // And each of those three is a lamp you can SEE, not just a
                // pool of light on the ground under it. The station stands
                // over twenty metres from the vehicle apron through Exp2 fog
                // at `0.026`, which eats a `0.07 m` emissive lens long before
                // that: every fixed lamp in the City carries a halo for
                // exactly this reason and not one fixture on this mountain
                // did, which is most of why the entrance did not read.
                Assert.That(
                    result.Root.GetComponentsInChildren<CityLightHalo>(true),
                    Has.Length.EqualTo(3),
                    "A station lamp with no halo is invisible from the pad.");

                // They must also be OUTSIDE the night registry. Its factor is
                // a process-wide static only the City writes, so a halo
                // registered here rides whatever the City last left - dimmed
                // to two thirds for a whole visit if the hero travelled at
                // noon. Pushing the registry to full day must not move them.
                CityNightGlowRegistry.SetNightFactor(0f);
                try
                {
                    foreach (CityLightHalo halo in result.Root
                                 .GetComponentsInChildren<CityLightHalo>(true))
                    {
                        Assert.That(
                            halo.IntensityFactor,
                            Is.EqualTo(1f).Within(0.0001f),
                            "A station halo is following the City's night " +
                            "factor, which nothing on this mountain writes.");
                    }
                }
                finally
                {
                    CityNightGlowRegistry.SetNightFactor(1f);
                }
                // The motor, the lower bullwheel's clack and one clack per
                // tower's rollers: every voice a visible machine's.
                Assert.That(result.Controller.AudioSources,
                    Has.Count.EqualTo(2 + supportCount));
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
