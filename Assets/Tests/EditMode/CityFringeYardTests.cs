using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityFringeYardTests
    {
        [Test]
        [Category("CityFringeYard")]
        public void DefaultCoastal_PlansFiveDeterministicFringesAndSealedForecourt()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityMountainBoundaryPlan mountains =
                CityMountainBoundaryPlanner.Create(layout);
            CityFringeYardPlan first =
                CityFringeYardPlanner.Create(layout, mountains);
            CityFringeYardPlan second =
                CityFringeYardPlanner.Create(layout, mountains);

            Assert.That(first.IsEnabled, Is.True);
            Assert.That(
                first.Yards,
                Has.Count.EqualTo(CityFringeYardPlanner.ExpectedYardCount));
            Assert.That(
                first.PartCount,
                Is.InRange(120, CityFringeYardPlanner.MaximumPartCount));
            Assert.That(first.HasTunnelForecourt, Is.True);
            Assert.That(
                first.Practicals,
                Has.Count.EqualTo(
                    CityFringeYardPracticalValidator.ExpectedPracticalCount));
            Assert.That(first.Practicals, Is.EqualTo(second.Practicals));
            Assert.That(
                first.Practicals.Select(item => item.AreaId),
                Is.EquivalentTo(new[]
                {
                    CityMountainBoundaryDefinition.WestNorthAreaId,
                    CityMountainBoundaryDefinition.WestSouthAreaId,
                    CityMountainBoundaryDefinition.SouthWestAreaId,
                    CityMountainBoundaryDefinition.SouthEastAreaId
                }));
            Assert.That(
                first.Practicals.Any(item =>
                    item.YardKind == CityFringeYardKind.EastUtilityEdge),
                Is.False);
            Assert.That(
                first.TunnelForecourt,
                Is.EqualTo(second.TunnelForecourt));
            Assert.That(
                first.TunnelForecourt.TunnelStableId,
                Is.EqualTo(mountains.Tunnel.StableId));
            Assert.That(first.TunnelForecourt.IsSealed, Is.True);
            Assert.That(mountains.Tunnel.IsSealed, Is.True);
            Assert.That(
                first.TunnelForecourt.ApproachWidth,
                Is.EqualTo(6.9f).Within(0.01f));
            Assert.That(
                first.TunnelForecourt.DriveClearWidth,
                Is.GreaterThanOrEqualTo(6f));

            string[] expectedAreas =
            {
                CityMountainBoundaryDefinition.WestNorthAreaId,
                CityMountainBoundaryDefinition.WestSouthAreaId,
                CityMountainBoundaryDefinition.SouthWestAreaId,
                CityMountainBoundaryDefinition.SouthEastAreaId,
                "yard-east"
            };
            CityFringeYardKind[] expectedKinds =
            {
                CityFringeYardKind.WestStoneTerraces,
                CityFringeYardKind.WestIndustrialBelt,
                CityFringeYardKind.SouthTunnelForecourt,
                CityFringeYardKind.SouthFloodWorks,
                CityFringeYardKind.EastUtilityEdge
            };
            Assert.That(
                first.Yards.Select(item => item.AreaId),
                Is.EqualTo(expectedAreas));
            Assert.That(
                first.Yards.Select(item => item.Kind),
                Is.EqualTo(expectedKinds));
            Assert.That(
                first.Yards.Select(item => item.Access.Id).Distinct().Count(),
                Is.EqualTo(expectedAreas.Length));
            Assert.That(
                mountains.Ridges.Any(item => item.SourceAreaId == "yard-east"),
                Is.False,
                "The eastern utility edge must not acquire a mountain.");

            for (int yardIndex = 0;
                 yardIndex < first.Yards.Count;
                 yardIndex++)
            {
                CityFringeYardDescriptor left = first.Yards[yardIndex];
                CityFringeYardDescriptor right = second.Yards[yardIndex];
                Assert.That(right.StableId, Is.EqualTo(left.StableId));
                Assert.That(right.AreaBounds, Is.EqualTo(left.AreaBounds));
                Assert.That(right.Access, Is.EqualTo(left.Access));
                Assert.That(
                    right.TraversalBounds,
                    Is.EqualTo(left.TraversalBounds));
                Assert.That(
                    right.Parts,
                    Is.EqualTo(left.Parts));
                foreach (CityFringeYardPartDescriptor part in left.Parts)
                {
                    Assert.That(
                        Contains(left.AreaBounds, part.Footprint),
                        Is.True,
                        part.StableId);
                    if (part.BlocksMovement)
                    {
                        Assert.That(
                            part.Footprint.Overlaps(
                                left.Access.ApproachBounds),
                            Is.False,
                            part.StableId);
                        Assert.That(
                            part.Footprint.Overlaps(left.TraversalBounds),
                            Is.False,
                            part.StableId);
                    }
                }
            }

            AssertVocabulary(first);
            CityFringeYardPracticalDescriptor tunnelPractical =
                first.Practicals.Single(item =>
                    item.Kind ==
                    CityFringeYardPracticalKind.TunnelReturnLamp);
            Assert.That(
                Vector3.Dot(
                    tunnelPractical.Forward,
                    -first.TunnelForecourt.Axis),
                Is.GreaterThan(0.75f));
            Assert.DoesNotThrow(() =>
                CityFringeYardValidator.ValidateOrThrow(
                    layout,
                    mountains,
                    first));

            var host = new GameObject("Fringe Yard Test Host");
            try
            {
                CityFringeYardWorldResult result =
                    CityFringeYardWorldBuilder.Build(
                    host.transform,
                    first);
                GameObject root = result.Root;
                Assert.That(root, Is.Not.Null);
                Assert.That(
                    result.PracticalAnchors,
                    Has.Count.EqualTo(
                        CityFringeYardPracticalValidator
                            .ExpectedPracticalCount));
                MeshRenderer[] renderers =
                    root.GetComponentsInChildren<MeshRenderer>(true);
                Assert.That(
                    renderers.Length,
                    Is.InRange(12, 116),
                    "The 48-metre style/collision batches must stay bounded.");
                Assert.That(
                    renderers.Count(item =>
                        item.name == "Practical Emissive Lens"),
                    Is.EqualTo(4));
                foreach (MeshRenderer lens in renderers.Where(item =>
                             item.name == "Practical Emissive Lens"))
                {
                    Assert.That(
                        lens.transform.localPosition,
                        Is.EqualTo(
                            Vector3.forward *
                            CityFringeYardWorldBuilder
                                .PracticalLensForwardOffset));
                }
                Assert.That(
                    root.GetComponentsInChildren<MeshCollider>(true).Length,
                    Is.GreaterThan(0));
                Assert.That(
                    root.GetComponentsInChildren<Light>(true),
                    Is.Empty);
                Assert.That(
                    root.GetComponentsInChildren<MonoBehaviour>(true)
                        .Any(item =>
                            item is IInteractable ||
                            item is SceneTransitionService),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        private static void AssertVocabulary(CityFringeYardPlan plan)
        {
            var required = new Dictionary<CityFringeYardKind,
                CityFringeYardPartKind[]>
            {
                [CityFringeYardKind.WestStoneTerraces] = new[]
                {
                    CityFringeYardPartKind.RetainingSection,
                    CityFringeYardPartKind.DrainChannel,
                    CityFringeYardPartKind.TerraceShelf,
                    CityFringeYardPartKind.CulvertHeadwall
                },
                [CityFringeYardKind.WestIndustrialBelt] = new[]
                {
                    CityFringeYardPartKind.RepairStack,
                    CityFringeYardPartKind.UtilityPole,
                    CityFringeYardPartKind.RepairFrame,
                    CityFringeYardPartKind.PipeStock
                },
                [CityFringeYardKind.SouthTunnelForecourt] = new[]
                {
                    CityFringeYardPartKind.WheelRut,
                    CityFringeYardPartKind.DrainCover,
                    CityFringeYardPartKind.TunnelCheek,
                    CityFringeYardPartKind.PracticalHousing
                },
                [CityFringeYardKind.SouthFloodWorks] = new[]
                {
                    CityFringeYardPartKind.Gabion,
                    CityFringeYardPartKind.DrainChannel,
                    CityFringeYardPartKind.GabionCage,
                    CityFringeYardPartKind.FloodGauge
                },
                [CityFringeYardKind.EastUtilityEdge] = new[]
                {
                    CityFringeYardPartKind.UtilityShed,
                    CityFringeYardPartKind.EarthBerm
                }
            };

            foreach (CityFringeYardDescriptor yard in plan.Yards)
            {
                foreach (CityFringeYardPartKind partKind in required[yard.Kind])
                {
                    Assert.That(
                        yard.Parts.Any(item => item.Kind == partKind),
                        Is.True,
                        $"{yard.AreaId} lacks {partKind}.");
                }
            }
        }

        private static bool Contains(Rect outer, Rect inner)
        {
            const float tolerance = 0.04f;
            return inner.xMin >= outer.xMin - tolerance &&
                   inner.xMax <= outer.xMax + tolerance &&
                   inner.yMin >= outer.yMin - tolerance &&
                   inner.yMax <= outer.yMax + tolerance;
        }
    }
}
