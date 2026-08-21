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

        [Test]
        [Category("CityFringeYard")]
        public void DefaultCoastal_OpensRingFrontagesAndKeepsRockRoutesClear()
        {
            const float playerRadius = 0.32f;
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityMountainBoundaryPlan mountains =
                CityMountainBoundaryPlanner.Create(layout);
            CityFringeYardPlan fringe =
                CityFringeYardPlanner.Create(layout, mountains);
            CityRoadGroundBoundaryPlan boundaries =
                CityRoadGroundBoundaryPlanner.Create(layout);
            RoadWalkableArea walkable = RoadWalkableArea.FromLayout(layout);
            string[] mountainAreas =
            {
                CityMountainBoundaryDefinition.WestNorthAreaId,
                CityMountainBoundaryDefinition.WestSouthAreaId,
                CityMountainBoundaryDefinition.SouthWestAreaId,
                CityMountainBoundaryDefinition.SouthEastAreaId
            };

            foreach (string areaId in mountainAreas)
            {
                Assert.That(
                    fringe.TryGetYard(
                        areaId,
                        out CityFringeYardDescriptor yard),
                    Is.True,
                    areaId);
                Assert.That(
                    TryFindOpenSampleBeyondAuthoredGate(
                        boundaries,
                        yard,
                        playerRadius,
                        out CityRoadGroundBoundarySpan openSpan,
                        out float openAlong),
                    Is.True,
                    $"{areaId} kept its invisible single-gate boundary.");
                AssertWalkableAcrossRoadSeam(
                    walkable,
                    openSpan,
                    openAlong,
                    playerRadius,
                    areaId);

                Vector3 target = CreateToeApproach(yard, playerRadius);
                Assert.That(
                    TraversalReaches(yard, target),
                    Is.True,
                    $"{areaId} traversal stops before the mountain toe.");
                AssertCapsuleClearRoute(
                    walkable,
                    yard,
                    target,
                    playerRadius);

                if (yard.Kind == CityFringeYardKind.SouthTunnelForecourt)
                {
                    Assert.That(mountains.Tunnel.IsSealed, Is.True);
                    Assert.That(
                        HorizontalDistance(
                            target,
                            mountains.Tunnel.PortalGroundCenter),
                        Is.LessThanOrEqualTo(playerRadius + 0.05f));
                }
                else
                {
                    Assert.That(
                        DistanceToRidgeToe(mountains, areaId, target),
                        Is.LessThanOrEqualTo(playerRadius + 0.05f),
                        $"{areaId} route does not reach its rock face.");
                    Assert.That(
                        yard.Parts.Any(part =>
                            part.Kind ==
                                CityFringeYardPartKind.AccessApron &&
                            Expanded(part.Footprint, 0.08f).Contains(
                                new Vector2(target.x, target.z))),
                        Is.True,
                        $"{areaId} route needs a visible gravel spur.");
                }
            }

            Assert.That(
                fringe.TryGetYard("yard-east", out _),
                Is.True);
            Assert.That(
                CityMountainBoundaryDefinition.IsMountainFacingAreaId(
                    "yard-east"),
                Is.False);
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

        private static bool TryFindOpenSampleBeyondAuthoredGate(
            CityRoadGroundBoundaryPlan boundaries,
            CityFringeYardDescriptor yard,
            float radius,
            out CityRoadGroundBoundarySpan selected,
            out float along)
        {
            float gateCenter = yard.Access.FrontageEdge.IsHorizontal
                ? yard.Access.Center.x
                : yard.Access.Center.z;
            float gateMinimum = gateCenter - yard.Access.Width * 0.5f;
            float gateMaximum = gateCenter + yard.Access.Width * 0.5f;
            for (int index = 0;
                 index < boundaries.SafeConnections.Count;
                 index++)
            {
                CityRoadGroundBoundarySpan span =
                    boundaries.SafeConnections[index];
                if (span.Surface.AreaId != yard.AreaId ||
                    span.Edge != yard.Access.FrontageEdge)
                {
                    continue;
                }

                float usableMinimum = span.MinimumCoordinate + radius + 0.02f;
                float usableMaximum = span.MaximumCoordinate - radius - 0.02f;
                if (usableMinimum < gateMinimum - 0.05f)
                {
                    selected = span;
                    along = Mathf.Min(
                        gateMinimum - 0.08f,
                        usableMaximum);
                    return along >= usableMinimum;
                }

                if (usableMaximum > gateMaximum + 0.05f)
                {
                    selected = span;
                    along = Mathf.Max(
                        gateMaximum + 0.08f,
                        usableMinimum);
                    return along <= usableMaximum;
                }
            }

            selected = default;
            along = 0f;
            return false;
        }

        private static void AssertWalkableAcrossRoadSeam(
            RoadWalkableArea walkable,
            CityRoadGroundBoundarySpan span,
            float along,
            float radius,
            string label)
        {
            Rect surface = span.Surface.WorldBounds;
            Vector3 inward = span.IsHorizontal
                ? new Vector3(
                    0f,
                    0f,
                    Mathf.Sign(surface.center.y - span.FixedCoordinate))
                : new Vector3(
                    Mathf.Sign(surface.center.x - span.FixedCoordinate),
                    0f,
                    0f);
            Vector3 seam = span.IsHorizontal
                ? new Vector3(along, 0f, span.FixedCoordinate)
                : new Vector3(span.FixedCoordinate, 0f, along);
            for (int index = -4; index <= 4; index++)
            {
                Vector3 sample = seam + inward * (index * 0.10f);
                Assert.That(
                    walkable.Contains(sample, radius),
                    Is.True,
                    $"{label} road seam sample {index}");
            }
        }

        private static Vector3 CreateToeApproach(
            CityFringeYardDescriptor yard,
            float inset)
        {
            Vector3 result = yard.Access.Center;
            Vector3 outward = yard.Access.OutwardNormal;
            if (outward.x < -0.5f)
            {
                result.x = yard.AreaBounds.xMin + inset;
            }
            else if (outward.x > 0.5f)
            {
                result.x = yard.AreaBounds.xMax - inset;
            }
            else if (outward.z < -0.5f)
            {
                result.z = yard.AreaBounds.yMin + inset;
            }
            else
            {
                result.z = yard.AreaBounds.yMax - inset;
            }

            return result;
        }

        private static bool TraversalReaches(
            CityFringeYardDescriptor yard,
            Vector3 target)
        {
            const float tolerance = 0.04f;
            return target.x >= yard.TraversalBounds.xMin - tolerance &&
                   target.x <= yard.TraversalBounds.xMax + tolerance &&
                   target.z >= yard.TraversalBounds.yMin - tolerance &&
                   target.z <= yard.TraversalBounds.yMax + tolerance;
        }

        private static void AssertCapsuleClearRoute(
            RoadWalkableArea walkable,
            CityFringeYardDescriptor yard,
            Vector3 target,
            float radius)
        {
            Vector3 start = yard.Access.Center;
            float distance = HorizontalDistance(start, target);
            int sampleCount = Mathf.CeilToInt(distance / 0.10f);
            for (int sampleIndex = 0;
                 sampleIndex <= sampleCount;
                 sampleIndex++)
            {
                float amount = sampleIndex / (float)sampleCount;
                Vector3 sample = Vector3.Lerp(start, target, amount);
                Assert.That(
                    walkable.Contains(sample, radius),
                    Is.True,
                    $"{yard.AreaId} route sample {sampleIndex}");
                foreach (CityFringeYardPartDescriptor part in yard.Parts)
                {
                    if (!part.BlocksMovement)
                    {
                        continue;
                    }

                    Assert.That(
                        Expanded(part.Footprint, radius + 0.05f).Contains(
                            new Vector2(sample.x, sample.z)),
                        Is.False,
                        $"{part.StableId} blocks {yard.AreaId} route.");
                }
            }
        }

        private static float DistanceToRidgeToe(
            CityMountainBoundaryPlan mountains,
            string areaId,
            Vector3 target)
        {
            Vector2 point = new Vector2(target.x, target.z);
            float best = float.PositiveInfinity;
            foreach (CityMountainRidgeDescriptor ridge in mountains.Ridges)
            {
                if (ridge.SourceAreaId != areaId)
                {
                    continue;
                }

                for (int index = 1; index < ridge.Stations.Count; index++)
                {
                    best = Mathf.Min(
                        best,
                        DistanceToSegment(
                            point,
                            ridge.Stations[index - 1].WorldXZ,
                            ridge.Stations[index].WorldXZ));
                }
            }

            return best;
        }

        private static float DistanceToSegment(
            Vector2 point,
            Vector2 start,
            Vector2 end)
        {
            Vector2 delta = end - start;
            float lengthSquared = delta.sqrMagnitude;
            if (lengthSquared <= 0.0001f)
            {
                return Vector2.Distance(point, start);
            }

            float amount = Mathf.Clamp01(
                Vector2.Dot(point - start, delta) / lengthSquared);
            return Vector2.Distance(point, start + delta * amount);
        }

        private static float HorizontalDistance(Vector3 first, Vector3 second)
        {
            return Vector2.Distance(
                new Vector2(first.x, first.z),
                new Vector2(second.x, second.z));
        }

        private static Rect Expanded(Rect source, float amount)
        {
            return Rect.MinMaxRect(
                source.xMin - amount,
                source.yMin - amount,
                source.xMax + amount,
                source.yMax + amount);
        }
    }
}
