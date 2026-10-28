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

        [TestCase(MountainCablewayStationKind.Drive)]
        [TestCase(MountainCablewayStationKind.Return)]
        [Category("MountainRoad")]
        public void WorldBuilder_KeepsOnlyLocalStationPhysical(MountainCablewayStationKind stationKind)
        {
            bool drive = stationKind == MountainCablewayStationKind.Drive;
            MountainRoadCablewayPlan plan = drive
                ? MountainRoadPlanner.Create(GameSessionState.DefaultCitySeed).Terminal.Cableway
                : AlpineVillagePlanner.Create(GameSessionState.DefaultCitySeed).Station.Cableway;
            var host = new GameObject("Cableway Test Host");
            try
            {
                MountainCablewayWorldResult result =
                    MountainCablewayWorldBuilder.Build(
                        host.transform,
                        plan,
                        stationKind,
                        villageSurfaces: !drive);
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
                    Has.Count.EqualTo((drive ? 2 : 1) + supportCount));
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

                AssertCraftedMetreSlots(result, plan, drive);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        private static void AssertCraftedMetreSlots(MountainCablewayWorldResult result,
            MountainRoadCablewayPlan plan, bool drive)
        {
            MountainCablewayCraftedGeometry geometry = result.Root.GetComponent<MountainCablewayCraftedGeometry>();
            Assert.That(geometry, Is.Not.Null);
            Assert.That(geometry.SharedMeshCount, Is.EqualTo(MountainCablewayCraftedGeometry.ExpectedMeshCount));
            Assert.That(geometry.TemplateVertexCount, Is.InRange(1, MountainCablewayCraftedGeometry.MaximumTemplateVertices));
            Assert.That(geometry.ReplacementCount, Is.InRange(40, MountainCablewayCraftedGeometry.MaximumReplacements));
            MeshFilter[] slots = result.Root.GetComponentsInChildren<MeshFilter>(true)
                .Where(filter => MountainCablewayCraftedGeometry.RoleFor(filter.name) != null).ToArray();
            Assert.That(slots.Length, Is.EqualTo(geometry.ReplacementCount));
            foreach (var group in slots.GroupBy(filter => MountainCablewayCraftedGeometry.RoleFor(filter.name)))
            {
                Mesh shared = group.First().sharedMesh;
                Assert.That(group.All(filter => filter.sharedMesh == shared), Is.True,
                    "Cabins and support towers must reuse one authored mesh per role.");
                bool cylinder = group.Key == "Bullwheel" || group.Key == "Hub" || group.Key == "Roller";
                Vector3 expected = new Vector3(1f, cylinder ? 2f : 1f, 1f);
                Assert.That(Vector3.Distance(shared.bounds.center, Vector3.zero), Is.LessThan(.002f));
                Assert.That(Vector3.Distance(shared.bounds.size, expected), Is.LessThan(.002f),
                    "Imported axis/unit factors changed the actual visible slot " + group.Key);
            }
            foreach (MeshFilter slot in slots)
            {
                var renderer = slot.GetComponent<MeshRenderer>();
                Assert.That(renderer.isPartOfStaticBatch, Is.False,
                    "Authored slot geometry must not freeze a moving cabin, wheel or reducer.");
                if (slot.TryGetComponent(out BoxCollider solid))
                {
                    Assert.That(Vector3.Distance(renderer.bounds.min, solid.bounds.min), Is.LessThan(.003f));
                    Assert.That(Vector3.Distance(renderer.bounds.max, solid.bounds.max), Is.LessThan(.003f));
                }
            }
            foreach (Transform cabin in result.Cabins)
            {
                Transform seat = cabin.Find(MountainCablewayWorldBuilder.CabinSeatAnchorName);
                float seatY = -MountainRoadCablewayPlan.CabinRoofDrop - plan.CabinSize.y +
                    MountainRoadCablewayPlan.CabinSkirtHeight + MountainCablewayWorldBuilder.CabinBenchHeight;
                Assert.That(seat.localPosition.y, Is.EqualTo(seatY).Within(.001f));
                Assert.That(cabin.GetComponentsInChildren<MeshFilter>(true)
                    .Where(filter => filter.name.Contains("Window")).All(filter =>
                        !filter.sharedMesh.name.StartsWith("GEO_Cableway_")), Is.True,
                    "The authored pack must preserve cabin glass and its open side entrance.");
            }
            Assert.That(result.StationRoot.GetComponentsInChildren<MeshFilter>(true)
                .Any(filter => filter.name == "Visible Cableway Motor Reducer"), Is.EqualTo(drive),
                "Only the lower drive station owns a motor.");
        }
    }
}
