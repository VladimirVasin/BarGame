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
            Assert.DoesNotThrow(() =>
                CityFringeYardValidator.ValidateOrThrow(
                    layout,
                    mountains,
                    first));

            var host = new GameObject("Fringe Yard Test Host");
            try
            {
                GameObject root = CityFringeYardWorldBuilder.Build(
                    host.transform,
                    first);
                Assert.That(root, Is.Not.Null);
                MeshRenderer[] renderers =
                    root.GetComponentsInChildren<MeshRenderer>(true);
                Assert.That(
                    renderers.Length,
                    Is.InRange(12, 128),
                    "The 48-metre style/collision batches must stay bounded.");
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
                    CityFringeYardPartKind.DrainChannel
                },
                [CityFringeYardKind.WestIndustrialBelt] = new[]
                {
                    CityFringeYardPartKind.RepairStack,
                    CityFringeYardPartKind.UtilityPole
                },
                [CityFringeYardKind.SouthTunnelForecourt] = new[]
                {
                    CityFringeYardPartKind.WheelRut,
                    CityFringeYardPartKind.DrainCover,
                    CityFringeYardPartKind.TunnelCheek
                },
                [CityFringeYardKind.SouthFloodWorks] = new[]
                {
                    CityFringeYardPartKind.Gabion,
                    CityFringeYardPartKind.DrainChannel
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
