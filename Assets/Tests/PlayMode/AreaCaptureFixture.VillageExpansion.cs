using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Focused expansion capture and traversal/asset contracts.")]
        [PrebuildSetup(typeof(VillageArtAssetsSetup))]
        public IEnumerator AlpineVillageExpansion()
        {
            GameSessionState.TryStartGameTimeFromWake();
            Assert.That(GameSessionState.TrySetDebugGameDay(2), Is.True);
            GameSessionState.AdvanceGameTime((float)(100f / GameTimeState.GameMinutesPerRealSecond));
            AlpineVillageRoot root = null;
            yield return Capture(SceneIds.AlpineVillage,
                () => root = Object.FindAnyObjectByType<AlpineVillageRoot>(), () =>
                {
                    AlpineVillageLaneSample foot = root.Plan.Lane.Sample(2f);
                    var shots = new List<Shot>
                    {
                        Shot.At("expansion-00-preserved-village-axis",
                            foot.Position - foot.Forward * PlatformApronSetback + Vector3.up * EyeHeight,
                            root.Plan.MothersHouse.GroundCenter + Vector3.up * LandmarkAimHeight,
                            50f, 0, () => root.StormWave <= GustTroughWave)
                    };
                    AppendVillageExpansionShots(root, shots);
                    return shots.ToArray();
                });
        }

        private static void AppendVillageExpansionShots(AlpineVillageRoot root, List<Shot> shots)
        {
            AlpineVillagePlan plan = root.Plan;
            AlpineVillageExpansionPlan expansion = plan.Expansion;
            int originalGround = 0, expandedGround = 0;
            Rect extent = plan.TerrainBounds;
            Rect core = plan.CoreTerrainBounds;
            for (float z = extent.yMin; z <= extent.yMax; z += 2f)
            for (float x = extent.xMin; x <= extent.xMax; x += 2f)
            {
                var point = new Vector2(x, z);
                bool wasGround = x >= core.xMin - 3f && x <= core.xMax + 3f &&
                    z >= core.yMin - 3f && z <= core.yMax + 3f;
                if (wasGround) originalGround++;
                if (wasGround || expansion.ContainsGround(point)) expandedGround++;
            }
            float areaRatio = expandedGround / (float)originalGround;
            Assert.That(areaRatio, Is.InRange(3f, 4f), "The expansion must materially enlarge the traversable ground.");
            TestContext.WriteLine("Expanded village ground ratio: " + areaRatio.ToString("F2"));
            IReadOnlyList<AlpineVillagePathDescriptor> paths = AlpineVillagePathPlanner.Create(plan);
            Assert.That(AlpineVillageSnowDrift.SampleDepth(plan, paths,
                new Vector2(expansion.LodgeCenter.x, expansion.LodgeCenter.z)), Is.Zero);
            Assert.That(root.World.TerrainRoot.GetComponentsInChildren<MeshRenderer>().Length,
                Is.GreaterThan(2), "The expanded ground needs spatial render batches.");
            Assert.That(root.World.WalkableArea.Contains(expansion.LodgeCenter, .35f), Is.True);

            // Measure the actual imported building, not only its authoring anchors.
            Transform lodge = root.World.Root.transform.Find("Village Expansion/Ski Lodge");
            Assert.That(lodge, Is.Not.Null);
            Bounds bounds = default;
            bool first = true;
            foreach (Renderer renderer in lodge.GetComponentsInChildren<Renderer>())
            {
                if (first) { bounds = renderer.bounds; first = false; }
                else bounds.Encapsulate(renderer.bounds);
            }
            Assert.That(first, Is.False);
            Assert.That(bounds.size.y, Is.InRange(3.5f, 8f), "Imported lodge scale.");
            Assert.That(bounds.size.x, Is.GreaterThan(12f));
            Assert.That(bounds.size.z, Is.GreaterThan(10f));

            Physics.SyncTransforms();
            float previousFloor = float.NaN;
            for (float along = -10f; along <= 0f; along += .5f)
            {
                Vector3 point = expansion.LodgeCenter + expansion.LodgeForward * along;
                Assert.That(Physics.Raycast(point + Vector3.up, Vector3.down,
                    out RaycastHit hit, 2f), Is.True, "The lodge entrance has no floor.");
                if (!float.IsNaN(previousFloor))
                    Assert.That(Mathf.Abs(hit.point.y - previousFloor), Is.LessThan(.25f),
                        "A floor or terrain lip blocks the lodge doorway.");
                previousFloor = hit.point.y;
                if (along >= -5.5f)
                    Assert.That(hit.point.y, Is.EqualTo(expansion.LodgeFloorHeight + .02f).Within(.04f),
                        "The hero must stand on the imported lodge floor.");
                Assert.That(root.World.WalkableArea.Contains(point, .35f), Is.True,
                    "The physical doorway and movement mask disagree.");
            }

            // Exercise the expanded snow's spatial query on an actual rendered
            // vertex, including its mutable footprint rather than only plan depth.
            bool pressed = false;
            foreach (Vector3 vertex in root.World.SnowTreading.GetComponent<MeshFilter>().sharedMesh.vertices)
            {
                if (plan.CoreTerrainBounds.Contains(new Vector2(vertex.x, vertex.z)) ||
                    !expansion.ContainsGround(new Vector2(vertex.x, vertex.z))) continue;
                float before = root.World.SnowTreading.SampleVisibleDepth(vertex);
                if (before < .2f) continue;
                root.World.SnowTreading.Press(vertex);
                Assert.That(root.World.SnowTreading.SampleVisibleDepth(vertex), Is.LessThan(before * .5f));
                pressed = true;
                break;
            }
            Assert.That(pressed, Is.True, "The new forest lost its lying snow.");

            // A reachable eye-height view must clear both the rubble and the
            // upper road shelf, otherwise the broken descent reads as a dead end.
            Vector3 cliffView = expansion.ToWorld(new Vector2(-128.6f, -52.85f));
            cliffView.y = AlpineVillageTerrainSampler.SampleHeight(plan,
                new Vector2(cliffView.x, cliffView.z));
            Assert.That(root.World.WalkableArea.Contains(cliffView, .35f), Is.True);
            Vector3 lowerRoad = expansion.ToWorld(new Vector2(-128.25f, -70f));
            lowerRoad.y = AlpineVillageTerrainSampler.SampleHeight(plan,
                new Vector2(lowerRoad.x, lowerRoad.z)) + .2f;
            Assert.That(Physics.Linecast(cliffView + Vector3.up * EyeHeight, lowerRoad,
                out RaycastHit obstruction), Is.False,
                "The lower road is hidden behind " + (obstruction.collider == null ? "terrain" : obstruction.collider.name));
            for (float across = -5f; across <= 5f; across += 1f)
                Assert.That(root.World.WalkableArea.Contains(expansion.CliffBarrierCenter +
                    plan.SlopeRight * across, .35f), Is.False, "The road barrier has a gap.");
            Assert.That(root.World.WalkableArea.Contains(expansion.CliffEdge - plan.Uphill * 4f, .35f), Is.False);

            Add("40-station-forest-entry", new Vector2(-12f, -2f), new Vector2(-55f, 0f), 65f);
            Add("41-deep-forest-trail", new Vector2(-78f, 6f), new Vector2(-120f, 22f), 64f);
            Add("42-ski-lodge-front", new Vector2(-137f, 43f), new Vector2(-137f, 56f), 76f);
            Add("43-ski-lodge-inside", new Vector2(-137f, 52f), new Vector2(-134f, 60f), 78f);
            Add("44-abandoned-ski-tow", new Vector2(-157f, 81f), new Vector2(-151f, 113f), 64f);
            Add("45-old-road-descent", new Vector2(-127f, -5f), new Vector2(-130f, -48f), 62f);
            Add("46-broken-city-road", new Vector2(-128.6f, -52.85f), new Vector2(-128.25f, -70f), 72f,
                lowerRoad.y - expansion.ToWorld(new Vector2(-128.25f, -70f)).y);
            Add("47-forest-return", new Vector2(-75f, 69f), new Vector2(-49f, 25f), 64f);

            void Add(string name, Vector2 from, Vector2 toward, float fov, float targetLift = 1.8f)
            {
                Vector3 foot = expansion.ToWorld(from);
                foot.y = AlpineVillageTerrainSampler.SampleHeight(plan, new Vector2(foot.x, foot.z));
                if (expansion.IsInterior(foot)) foot.y = expansion.LodgeFloorHeight + .02f;
                Vector3 target = expansion.ToWorld(toward) + Vector3.up * targetLift;
                bool moved = false;
                int frames = 0;
                shots.Add(Shot.At(name, foot + Vector3.up * EyeHeight, target, fov, 0, () =>
                {
                    if (!moved)
                    {
                        root.SetWarmthGrade(0f);
                        root.Player.Motor.Teleport(foot + Vector3.up * PlayerFactory.GroundedRootOffset);
                        moved = true;
                    }
                    return ++frames > 12 && root.StormWave <= GustTroughWave;
                }));
            }
        }
    }
}
