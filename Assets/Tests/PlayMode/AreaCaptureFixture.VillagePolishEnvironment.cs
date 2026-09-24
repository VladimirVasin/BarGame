using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        private static void AppendVillagePolishEnvironmentShots(AlpineVillageRoot root, List<Shot> shots)
        {
            VerifyVillagePolishForest(root);
            VerifyVillageOvercastLighting();
            AlpineVillageLaneSample axis = root.Plan.Lane.Sample(2f);
            Vector3 axisFoot = axis.Position - axis.Forward * PlatformApronSetback;
            Vector3 house = root.Plan.MothersHouse.GroundCenter + Vector3.up * LandmarkAimHeight;
            shots.Add(AbandonmentShot(root, "polish-10-core-day", axisFoot, house, 58f, true));

            AlpineVillageAbandonedPlot hall = null;
            foreach (AlpineVillageAbandonedPlot plot in root.Plan.Expansion.Abandonment.Plots)
                if (plot.Id == "town-hall") hall = plot;
            Assert.That(hall, Is.Not.Null);
            Assert.That(TryAbandonmentFoot(root, hall.GroundCenter, hall.Rotation, hall.Size, 1,
                out Vector3 forestFoot, true), Is.True, "The abandoned town hall needs a clear side approach.");
            Vector3 forestTarget = hall.GroundCenter + Vector3.up * 7f;
            shots.Add(AbandonmentShot(root, "polish-11-abandoned-forest-day", forestFoot, forestTarget, 88f, true));

            Vector3 trail = root.Plan.Expansion.ForestEntrance;
            trail.y = AlpineVillageTerrainSampler.SampleHeight(root.Plan, new Vector2(trail.x, trail.z));
            Vector3 trailTarget = root.Plan.Expansion.ToWorld(new Vector2(-78f, 6f)) + Vector3.up * 9f;
            shots.Add(AbandonmentShot(root, "polish-12-forest-trail-day", trail, trailTarget, 80f, true));
            shots.Add(AbandonmentShot(root, "polish-20-abandoned-forest-night", forestFoot, forestTarget,
                88f, true, false, true));
            shots.Add(AbandonmentShot(root, "polish-21-core-night", axisFoot, house, 58f, true, false, true));
        }

        private static void VerifyVillagePolishForest(AlpineVillageRoot root)
        {
            AlpineVillagePlan plan = root.Plan;
            foreach (AlpineVillageAbandonedPlot plot in plan.Expansion.Abandonment.Plots)
                Assert.That(AlpineVillageTreePlanner.TreeScaleAt(plan,
                    new Vector2(plot.GroundCenter.x, plot.GroundCenter.z)), Is.EqualTo(2f), plot.Id);
            foreach (AlpineVillagePlotDescriptor plot in plan.Plots)
                if (plot.Kind == AlpineVillagePlotKind.House || plot.Kind == AlpineVillagePlotKind.MothersHouse)
                    Assert.That(AlpineVillageTreePlanner.TreeScaleAt(plan,
                        new Vector2(plot.GroundCenter.x, plot.GroundCenter.z)), Is.EqualTo(1f), plot.StableId);

            int expansionTrees = 0, abandonedInfill = 0, residentialTrees = 0;
            var crowned = new List<MountainRoadForestDescriptor>(plan.Trees.CrownedTrees);
            var paths = AlpineVillagePathPlanner.Create(plan);
            foreach (MountainRoadForestDescriptor tree in plan.Trees.ForestTrees)
            {
                Vector2 point = new Vector2(tree.Position.x, tree.Position.z);
                bool expansion = tree.StableId.StartsWith("village-expansion-forest-");
                float scale = AlpineVillageTreePlanner.TreeScaleAt(plan, point);
                Assert.That(tree.Height, expansion ? Is.InRange(13f, 30f) : Is.InRange(4.5f * scale, 17f * scale), tree.StableId);
                Assert.That(tree.TrunkRadius,
                    Is.EqualTo(Mathf.Clamp(tree.CrownRadius / scale * .16f, .18f, .46f) * scale).Within(.0001f));
                Assert.That(plan.Expansion.Abandonment.ClearsFeatures(plan.Expansion.ToLocal(point), tree.CrownRadius),
                    Is.True, tree.StableId + " blocks a yard or neighbour sightline.");
                Assert.That(root.World.WalkableArea.Contains(tree.Position), Is.False, tree.StableId + " lost its trunk obstacle.");
                Assert.That(AlpineVillagePathPlanner.MeasureDistanceOutsideTrodden(plan, paths, point, out _),
                    Is.GreaterThanOrEqualTo((expansion ? AlpineVillageTreePlanner.ExpansionTrailClearing :
                        AlpineVillageTreePlanner.ForestClearing) + tree.CrownRadius - .001f), tree.StableId);
                if (expansion) expansionTrees++;
                else if (scale > 1f) abandonedInfill++;
                else residentialTrees++;
            }
            Assert.That(expansionTrees, Is.GreaterThan(0));
            Assert.That(abandonedInfill, Is.GreaterThan(0), "Old terrain bounds also contain abandoned forest.");
            Assert.That(residentialTrees, Is.GreaterThan(0));
            for (int a = 0; a < crowned.Count; a++)
            for (int b = a + 1; b < crowned.Count; b++)
                Assert.That(Vector2.Distance(new Vector2(crowned[a].Position.x, crowned[a].Position.z),
                    new Vector2(crowned[b].Position.x, crowned[b].Position.z)),
                    Is.GreaterThanOrEqualTo(crowned[a].CrownRadius + crowned[b].CrownRadius - .001f),
                    crowned[a].StableId + " intersects " + crowned[b].StableId);
            int westPanels = 0;
            foreach (AlpineVillageRockPlacement rock in AlpineVillageRockPlanner.Create(plan))
                if ((rock.Rotation * Vector3.forward).x < -.9f) westPanels++;
            Debug.Log($"Village polish forest: expansion {expansionTrees}, abandoned infill {abandonedInfill}, " +
                $"residential {residentialTrees}, wall {plan.Trees.WallTrees.Count}, west rock panels {westPanels}.");
        }

        private static void VerifyVillageOvercastLighting()
        {
            DayNightVisualSample current = GameTimeDayNightRules.Evaluate(GameSessionState.GameTimeOfDayMinutes);
            try
            {
                // A real daytime writer must remain overcast at both ends of
                // story dimming; setting the story grade is not the weather fix.
                DayNightVisualSample noon = GameTimeDayNightRules.Evaluate(12d * 60d);
                foreach (float grade in new[] { 0f, 1f })
                {
                    RuntimeSceneSetup.ApplyAlpineVillageLighting(noon, grade, false);
                    Assert.That(RenderSettings.sun.intensity, Is.LessThan(noon.DirectionalLightIntensity * .7f));
                    Assert.That(RenderSettings.sun.shadowStrength, Is.LessThan(.4f));
                    Assert.That(RenderSettings.ambientLight.maxColorComponent, Is.LessThan(.5f));
                }
                DayNightVisualSample night = GameTimeDayNightRules.Evaluate(21d * 60d);
                RuntimeSceneSetup.ApplyAlpineVillageLighting(night, 0f, false);
                Assert.That(RenderSettings.sun.intensity,
                    Is.EqualTo(night.DirectionalLightIntensity * 1.06f).Within(.0001f),
                    "Overcast daylight must not darken the established night fill.");
                Assert.That(ExteriorCloudProfiles.AlpineVillage.Coverage, Is.GreaterThan(.95f));
                Assert.That(ExteriorCloudProfiles.AlpineVillage.SupportsLightning, Is.False);
            }
            finally
            {
                RuntimeSceneSetup.ApplyAlpineVillageLighting(current, 0f);
            }
        }
    }
}
