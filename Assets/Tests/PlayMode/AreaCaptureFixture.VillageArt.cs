using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    // Like the other focused capture prebuilds: import and validate only the
    // assets this scene actually needs, before the player domain is entered.
    public sealed class VillageArtAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            foreach (string name in new[] { "VillageFacadeTextureSetup",
                         "VillageAssetSetup", "VillageRockAssetSetup",
                         "UpperCablewayCanopyAssetSetup" })
            {
                Type setup = Type.GetType(
                    "BarPromenade.Editor." + name + ", BarPromenade.Editor", true);
                setup.GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
            }
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        private static void AppendVillageArtShots(
            AlpineVillageRoot root, List<Shot> shots)
        {
            AlpineVillagePlan plan = root.Plan;
            var paths = AlpineVillagePathPlanner.Create(plan);
            // The art pass lets snow reach foundations while every working
            // threshold, the station and all trodden paths stay clear.
            foreach (AlpineVillagePlotDescriptor plot in plan.Plots)
            {
                Vector3 door = plot.Kind == AlpineVillagePlotKind.Spring
                    ? plan.Brook.ApproachPosition : plot.DoorDockPosition;
                Assert.That(AlpineVillageSnowDrift.SampleDepth(plan, paths,
                    new Vector2(door.x, door.z)), Is.LessThan(0.01f), plot.StableId);
            }

            MountainRoadCablewayPlan cableway = plan.Station.Cableway;
            Vector3 station = cableway.StationArea.Center;
            Vector3 forward = cableway.LineForward;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 overlook = station + forward * 2.0f + right * 3.2f +
                Vector3.up * EyeHeight;
            shots.Add(Shot.At("26-upper-station-under-canopy",
                station - forward * 1.7f + right * 2.7f + Vector3.up * EyeHeight,
                station + forward * 1.6f + Vector3.up * 4.0f, 65f));
            shots.Add(Shot.At("27-upper-station-down-the-cut", overlook,
                station + forward * 38f + Vector3.down * 5f, 62f,
                0, () => root.StormWave <= GustTroughWave));

            AlpineVillageLaneSample mid = plan.Lane.Sample(plan.Lane.Length * 0.5f);
            shots.Add(Shot.At("28-houses-and-rock-wall",
                mid.Position + mid.Forward * 3f + Vector3.up * EyeHeight,
                mid.Position + mid.Forward * 8f + mid.Right * 26f +
                Vector3.up * 12f, 60f, 0,
                () => root.StormWave <= GustTroughWave));
            Transform rocks = root.World.Root.transform.Find(AlpineVillageRockBuilder.RootName);
            Assert.That(rocks, Is.Not.Null, "The authored ridge faces must be built.");
            Assert.That(rocks.childCount, Is.GreaterThan(1));
            Transform rock = rocks.GetChild(1);
            Vector3 toe = rock.position - rock.forward * 12f;
            toe.y = AlpineVillageTerrainSampler.SampleHeight(plan,
                new Vector2(toe.x, toe.z)) + EyeHeight;
            shots.Add(Shot.At("30-rock-ledge-close", toe,
                rock.position + Vector3.up * 22f + rock.forward * 4f,
                70f, 0, () => root.StormWave <= GustTroughWave));
            foreach (AlpineVillagePlotDescriptor plot in plan.Plots)
            {
                if (plot.Kind != AlpineVillagePlotKind.House ||
                    VillageAssetProvider.SelectVariant(
                        VillageAssetKind.House, plot.StableId) != 0)
                {
                    continue;
                }

                Vector3 houseRight = Vector3.Cross(Vector3.up, plot.Facing);
                shots.Add(Shot.At("31-house-door-and-shutters",
                    plot.DoorGroundPosition + plot.Facing * 3.1f +
                    houseRight * 0.8f + Vector3.up * EyeHeight,
                    plot.DoorGroundPosition + houseRight * 0.2f +
                    Vector3.up * 1.4f, 62f, 0,
                    () => root.StormWave <= GustTroughWave));
                break;
            }

            AppendVillageRepairShots(root, shots);

            bool dimApplied = false;
            AlpineVillageLaneSample foot = plan.Lane.Sample(2f);
            shots.Add(Shot.At("29-lower-uphill-after-dimming",
                foot.Position - foot.Forward * 2.6f + foot.Right * 0.25f +
                Vector3.up * EyeHeight,
                plan.Lane.Sample(plan.Lane.Length * 0.62f).Position +
                Vector3.up * 2.2f, 58f, 0, () =>
                {
                    if (!dimApplied)
                    {
                        root.SetWarmthGrade(1f);
                        dimApplied = true;
                    }
                    return root.StormWave <= GustTroughWave;
                }));
        }

        private static void AppendVillageRepairShots(
            AlpineVillageRoot root, List<Shot> shots)
        {
            AlpineVillagePlan plan = root.Plan;
            foreach (int seed in new[] { GameSessionState.DefaultCitySeed,
                         -99992, -99895, -96746, -87107, -58640, -29563,
                         3677, 57657, 89380 })
            {
                // The changed spring endpoint must keep the existing seeded
                // bypass contract while the capture judges its visible skin.
                AlpineVillagePathPlanner.Create(AlpineVillagePlanner.Create(seed));
            }
            AlpineVillagePlotDescriptor house = plan.MothersHouse;
            Vector3 right = Vector3.Cross(Vector3.up, house.Facing).normalized;
            Vector3 Local(float x, float y, float z) => house.GroundCenter +
                right * x + Vector3.up * y + house.Facing * z;

            shots.Add(Shot.At("32-mother-front-window-layout",
                Local(4.7f, 2.1f, 9f), Local(0f, 3.6f, 3.75f), 66f));
            shots.Add(Shot.At("33-mother-rear-window-layout",
                Local(-5.5f, 2.6f, -9f), Local(-0.3f, 3.1f, -3.75f), 68f));
            shots.Add(Shot.At("34-mother-annex-front-seam-a",
                Local(3.10f, 2.1f, 6.6f), Local(1.47f, 2.55f, 3.735f), 70f));
            shots.Add(Shot.At("35-mother-annex-front-seam-b",
                Local(3.22f, 2.1f, 6.6f), Local(1.47f, 2.55f, 3.735f), 70f));
            shots.Add(Shot.At("36-mother-annex-rear-seam-a",
                Local(3.10f, 2.1f, -6.6f), Local(1.47f, 2.55f, -3.735f), 70f));
            shots.Add(Shot.At("37-mother-annex-rear-seam-b",
                Local(3.22f, 2.1f, -6.6f), Local(1.47f, 2.55f, -3.735f), 70f));

            AlpineVillageBrookPlan brook = plan.Brook;
            VerifySpringPathAboveRenderedGround(root);
            shots.Add(Shot.At("38-spring-water-and-path-approach",
                brook.ApproachPosition + brook.LedgeFacing * 2.7f +
                brook.BowlOutletDirection * 1.6f + Vector3.up * 2.4f,
                brook.BowlCenter + Vector3.up * 0.1f, 62f, 24));
            var paths = AlpineVillagePathPlanner.Create(plan);
            Vector3 pathCenter = brook.ApproachPosition;
            int points = 1;
            foreach (AlpineVillagePathDescriptor path in paths)
            {
                if (path.Kind != AlpineVillagePathKind.SpringSpur)
                {
                    continue;
                }
                pathCenter += path.Start;
                points++;
            }
            pathCenter /= points;
            shots.Add(Shot.At("39-spring-joined-path-from-above",
                pathCenter + Vector3.up * 27f + brook.LedgeFacing * 6f,
                pathCenter, 68f, 24));
        }

        private static void VerifySpringPathAboveRenderedGround(AlpineVillageRoot root)
        {
            Transform world = root.World.Root.transform;
            var ground = world.Find("Village Ground").GetComponent<MeshCollider>();
            Assert.That(ground, Is.Not.Null);
            float oldBurial = 0f;
            foreach (MeshFilter filter in world.Find("Visible Village Paths")
                         .GetComponentsInChildren<MeshFilter>())
            {
                if (!filter.name.StartsWith("Visible Path - village-spring-", StringComparison.Ordinal))
                    continue;
                Vector3[] vertices = filter.sharedMesh.vertices;
                int[] triangles = filter.sharedMesh.triangles;
                for (int index = 0; index < triangles.Length; index += 3)
                {
                    Vector3 a = vertices[triangles[index]], b = vertices[triangles[index + 1]],
                        c = vertices[triangles[index + 2]];
                    foreach (Vector3 local in new[] { (a + b + c) / 3f,
                                 (a + b) * .5f, (b + c) * .5f, (c + a) * .5f })
                    {
                        Vector3 point = filter.transform.TransformPoint(local);
                        Assert.That(ground.Raycast(new Ray(point + Vector3.up * 30f,
                            Vector3.down), out RaycastHit hit, 60f), Is.True);
                        Assert.That(point.y - hit.point.y, Is.GreaterThan(.004f),
                            "Spring path hidden by its actual terrain triangle at " + point);
                        oldBurial = Mathf.Max(oldBurial, hit.point.y -
                            AlpineVillageTerrainSampler.SampleHeight(root.Plan,
                                new Vector2(point.x, point.z)) - AlpineVillageWorldBuilder.LaneSkinLift);
                    }
                }
            }
            Debug.Log("Spring path: previous analytic skin buried by up to " + oldBurial + " m.");
        }
    }
}
