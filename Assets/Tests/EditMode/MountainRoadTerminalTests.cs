using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class MountainRoadTerminalTests
    {
        [Test]
        [Category("MountainRoad")]
        public void DefaultPlan_KeepsAutomotiveSeamAndLandmarksInsideSite()
        {
            MountainRoadPlan plan = MountainRoadPlanner.Create(
                GameSessionState.DefaultCitySeed);
            MountainRoadTerminalPlan terminal = plan.Terminal;
            MountainRoadRouteSample entry = plan.Route.Sample(
                plan.Plateau.EntryDistance);
            MountainRoadVehicleApronPlan apron = terminal.VehicleApron;

            Assert.That(
                plan.Plateau.Size.x,
                Is.EqualTo(42f).Within(0.15f));
            Assert.That(
                plan.Plateau.Size.y,
                Is.EqualTo(27f).Within(0.15f));
            Assert.That(
                entry.Position.y,
                Is.EqualTo(plan.Plateau.Center.y).Within(0.001f),
                "The road must not step into the terminal.");
            Assert.That(
                Vector3.Distance(entry.Position, apron.EntryCenter),
                Is.LessThan(0.001f),
                "The vehicle apron detached from the road sample.");
            Assert.That(
                apron.EntryWidth,
                Is.EqualTo(entry.Width).Within(0.001f));
            Assert.That(
                apron.TurningRadius,
                Is.EqualTo(7.5f).Within(0.001f));

            Vector3 entryLeft = entry.Position -
                                entry.Right * (entry.Width * 0.5f);
            Vector3 entryRight = entry.Position +
                                 entry.Right * (entry.Width * 0.5f);
            Assert.That(
                Vector2.Distance(
                    plan.Plateau.VerticesXZ[0],
                    ToXZ(entryLeft)),
                Is.LessThan(0.001f),
                "The plateau lost the left shared road vertex.");
            Assert.That(
                Vector2.Distance(
                    plan.Plateau.VerticesXZ[
                        plan.Plateau.VerticesXZ.Count - 1],
                    ToXZ(entryRight)),
                Is.LessThan(0.001f),
                "The plateau lost the right shared road vertex.");

            const int turnSamples = 48;
            for (int index = 0; index < turnSamples; index++)
            {
                float angle = index / (float)turnSamples *
                              Mathf.PI * 2f;
                Vector3 point = apron.Center +
                    apron.Right *
                    (Mathf.Cos(angle) * apron.TurningRadius) +
                    apron.Forward *
                    (Mathf.Sin(angle) * apron.TurningRadius);
                Assert.That(
                    plan.Plateau.Contains(ToXZ(point)),
                    Is.True,
                    $"The 7.5 m turning circle leaves the actual plateau " +
                    $"polygon at sample {index}.");
            }

            Assert.That(
                new MountainRoadWalkableArea(plan).Contains(
                    apron.Center,
                    apron.TurningRadius),
                Is.True,
                "The traversal mask must retain the complete turning circle.");
            float cafeSide = Vector3.Dot(
                terminal.Cafe.Center - apron.Center,
                apron.Right);
            float cablewaySide = Vector3.Dot(
                terminal.Cableway.StationArea.Center - apron.Center,
                apron.Right);
            Assert.That(
                cafeSide,
                Is.LessThanOrEqualTo(-8f),
                "The cafe must remain left of arrival.");
            Assert.That(
                cablewaySide,
                Is.GreaterThanOrEqualTo(8f),
                "The cableway must remain right of arrival.");

            for (int index = 0;
                 index < terminal.Cafe.FootprintXZ.Count;
                 index++)
            {
                Assert.That(
                    plan.Plateau.Contains(
                        terminal.Cafe.FootprintXZ[index]),
                    Is.True,
                    $"Cafe corner {index} leaves the actual plateau polygon.");
            }

            for (int corner = 0; corner < 4; corner++)
            {
                Assert.That(
                    plan.Plateau.Contains(ToXZ(
                        terminal.Cableway.StationArea.GetCorner(corner))),
                    Is.True,
                    $"Cable station corner {corner} leaves the actual " +
                    "plateau polygon.");
            }

            Assert.That(terminal.Landmarks, Has.Count.EqualTo(2));
            Assert.That(
                terminal.Landmarks.Select(landmark => landmark.Kind),
                Is.EquivalentTo(new[]
                {
                    MountainRoadTerminalLandmarkKind.Cafe,
                    MountainRoadTerminalLandmarkKind.Cableway
                }));
            for (int index = 0;
                 index < terminal.Landmarks.Count;
                 index++)
            {
                MountainRoadTerminalLandmark landmark =
                    terminal.Landmarks[index];
                Assert.That(
                    plan.Plateau.Contains(ToXZ(landmark.Position)),
                    Is.True,
                    $"Map landmark '{landmark.StableId}' is only inside " +
                    "the rectangular bounds, not the actual plateau.");
            }
        }

        [Test]
        [Category("MountainRoad")]
        public void CablewayCabinBody_ClearsSampledTerrainOnBothTracks()
        {
            MountainRoadPlan plan = MountainRoadPlanner.Create(
                GameSessionState.DefaultCitySeed);
            MountainRoadCablewayPlan cableway = plan.Terminal.Cableway;
            var minimumBySide = new Dictionary<int, ClearanceSample>
            {
                { -1, new ClearanceSample(float.PositiveInfinity, 0f) },
                { 1, new ClearanceSample(float.PositiveInfinity, 0f) }
            };

            const float distanceStep = 0.25f;
            for (float distance = 0f;
                 distance <= cableway.LineLength + 0.001f;
                 distance += distanceStep)
            {
                float sampledDistance = Mathf.Min(
                    distance,
                    cableway.LineLength);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 attachment =
                        MountainCablewayMotion.SampleTrackPosition(
                            cableway,
                            sampledDistance,
                            side);
                    float terrain = MountainRoadTerrainSampler.SampleHeight(
                        plan.Route,
                        plan.Plateau,
                        ToXZ(attachment));
                    float clearance = attachment.y -
                                      cableway.CabinAttachmentToBottom -
                                      terrain;
                    if (clearance < minimumBySide[side].Clearance)
                    {
                        minimumBySide[side] = new ClearanceSample(
                            clearance,
                            sampledDistance);
                    }
                }
            }

            foreach (KeyValuePair<int, ClearanceSample> pair in
                     minimumBySide)
            {
                Assert.That(
                    pair.Value.Clearance,
                    Is.GreaterThanOrEqualTo(0.5f),
                    $"Cabin body on track {pair.Key:+#;-#} enters " +
                    $"terrain near {pair.Value.Distance:0.00} m.");
            }
        }

        [Test]
        [Category("MountainRoad")]
        public void TerminalApproach_BlendsTerrainWithoutShoulderStep()
        {
            MountainRoadPlan plan = MountainRoadPlanner.Create(
                GameSessionState.DefaultCitySeed);
            MountainRoadRouteSample approach = plan.Route.Sample(
                plan.Plateau.EntryDistance - 6f);

            for (int side = -1; side <= 1; side += 2)
            {
                float previousHeight = 0f;
                bool hasPrevious = false;
                for (float offset = approach.Width * 0.5f + 0.2f;
                     offset <= approach.Width * 0.5f + 2.3f;
                     offset += 0.1f)
                {
                    Vector3 point = approach.Position +
                                    approach.Right * (offset * side);
                    float height = MountainRoadTerrainSampler.SampleHeight(
                        plan.Route,
                        plan.Plateau,
                        ToXZ(point));
                    if (hasPrevious)
                    {
                        Assert.That(
                            Mathf.Abs(height - previousHeight),
                            Is.LessThan(0.12f),
                            $"Terminal terrain forms a shoulder step on " +
                            $"side {side:+#;-#} near offset {offset:0.0} m.");
                    }

                    previousHeight = height;
                    hasPrevious = true;
                }
            }
        }

        private static Vector2 ToXZ(Vector3 value)
        {
            return new Vector2(value.x, value.z);
        }

        private readonly struct ClearanceSample
        {
            public ClearanceSample(float clearance, float distance)
            {
                Clearance = clearance;
                Distance = distance;
            }

            public float Clearance { get; }
            public float Distance { get; }
        }
    }
}
