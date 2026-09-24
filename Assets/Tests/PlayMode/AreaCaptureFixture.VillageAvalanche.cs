using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Old avalanche: physical closure, retained neighbours and eye-height review.")]
        [PrebuildSetup(typeof(VillageArtAssetsSetup))]
        public IEnumerator AlpineVillageAvalanche()
        {
            GameSessionState.TryStartGameTimeFromWake();
            Assert.That(GameSessionState.TrySetDebugGameDay(2), Is.True);
            GameSessionState.AdvanceGameTime((float)(100f / GameTimeState.GameMinutesPerRealSecond));
            var failures = new List<string>();
            AlpineVillageRoot root = null;
            yield return Capture(SceneIds.AlpineVillage,
                () => root = Object.FindAnyObjectByType<AlpineVillageRoot>(), () =>
                {
                    Physics.SyncTransforms();
                    Transform avalanche = root.World.Root.transform.Find("Village Expansion/Old Avalanche");
                    AbandonmentCheck(failures, "avalanche metre geometry", () =>
                    {
                        Assert.That(avalanche, Is.Not.Null);
                        Bounds bounds = LocalRendererBounds(avalanche);
                        Assert.That(bounds.size.x, Is.InRange(25f, 55f), "The deposit must span the old tow strip.");
                        Assert.That(bounds.size.z, Is.InRange(18f, 42f), "The deposit must join the hillside.");
                        Assert.That(bounds.size.y, Is.InRange(8f, 85f), "The imported hillside mass lost its metre scale.");
                        Assert.That(avalanche.GetComponentsInChildren<MeshCollider>().Length, Is.GreaterThan(0));
                    });
                    AbandonmentCheck(failures, "avalanche physical front and shoulders", () =>
                        VerifyAvalancheFront(root, avalanche));
                    AbandonmentCheck(failures, "cleared avalanche corridor", () =>
                    {
                        foreach (MountainRoadForestDescriptor tree in root.Plan.Trees.CrownedTrees)
                            Assert.That(root.Plan.Expansion.Avalanche.ContainsLocal(
                                root.Plan.Expansion.ToLocal(tree.Position), tree.TrunkRadius), Is.False,
                                "An upright tree remains inside the old avalanche: " + tree.StableId);
                    });
                    AbandonmentCheck(failures, "continuous movement against avalanche", () =>
                        VerifyAvalancheMovement(root));
                    AbandonmentCheck(failures, "retained ski approach", () => VerifyAvalancheApproach(root));

                    var neighbours = ResolveAbandonmentBuildings(root);
                    var report = new AbandonmentReport();
                    AbandonmentBuilding avalancheRuin = null;
                    foreach (AbandonmentBuilding building in neighbours)
                    {
                        if (building.Site.Id != "homestead-14" && building.Site.Id != "homestead-15") continue;
                        if (building.Site.Id == "homestead-15") avalancheRuin = building;
                        AbandonmentCheck(failures, building.Site.Id + " accessible remains", () =>
                            VerifyAbandonedPlot(root, building.Site.Abandoned, building.Transform));
                        AbandonmentCheck(failures, building.Site.Id + " neighbouring views", () =>
                            AuditBuildingNeighbours(root, building, neighbours, report, failures));
                    }

                    var shots = new List<Shot>
                    {
                        AvalancheShot(root, "avalanche-00-tow-approach", new Vector2(-157f, 83f),
                            new Vector2(-155f, 119f), 8f, 64f),
                        AvalancheShot(root, "avalanche-01-impassable-front", new Vector2(-154f, 106f),
                            new Vector2(-158f, 114f), 4f, 74f),
                        AvalancheShot(root, "avalanche-02-ruin-and-mountainside", new Vector2(-149f, 98f),
                            new Vector2(-141f, 113f), 4f, 82f),
                        // Retain the rejected view verbatim: geometry corrections
                        // must survive the camera which exposed the filled wedge
                        // and unsupported roof, as well as the other sides.
                        AvalancheShot(root, "avalanche-04-ruin-rejected-view", new Vector2(-130f, 97f),
                            new Vector2(-155f, 120f), 8f, 72f)
                    };
                    foreach (AbandonmentVisibility view in report.views)
                    {
                        if (!view.visible || view.source != "homestead-14" || view.side != "left") continue;
                        shots.Add(AbandonmentShot(root, "avalanche-03-retained-neighbour",
                            view.eye - Vector3.up * EyeHeight, view.targetPoint, 64f, trough: true));
                        break;
                    }
                    AbandonmentCheck(failures, "ruin open interior and four accessible sides", () =>
                        AppendAvalancheRuinReview(root, avalancheRuin, shots, failures));
                    return shots.ToArray();
                });
            Assert.That(failures, Is.Empty, "Old-avalanche contracts; inspect the retained frames:\n" +
                string.Join("\n", failures));
        }

        private static void AppendAvalancheRuinReview(AlpineVillageRoot root, AbandonmentBuilding ruin,
            List<Shot> shots, List<string> failures)
        {
            Assert.That(ruin, Is.Not.Null, "The avalanche's damaged house remains in the settlement.");
            AlpineVillageAbandonedPlot plot = ruin.Site.Abandoned;
            var interior = new List<Vector3>();
            foreach (float x in new[] { -.3f, -.15f, 0f, .15f, .3f })
            foreach (float z in new[] { -.3f, -.15f, 0f, .15f, .3f })
            {
                Vector3 point = plot.World(Vector2.Scale(plot.Size, new Vector2(x, z)));
                if (root.World.WalkableArea.Contains(point, .3f) && AbandonmentCapsuleFree(point))
                    interior.Add(point);
            }

            int visibleSides = 0;
            string[] names = { "front", "right", "back", "left" };
            for (int side = 0; side < 4; side++)
            {
                if (!TryAbandonmentFoot(root, plot.GroundCenter, plot.Rotation, plot.Size, side, out Vector3 foot))
                {
                    failures.Add("homestead-15/" + names[side] + ": no physically accessible eye-height review point");
                    continue;
                }
                shots.Add(AbandonmentShot(root, "avalanche-0" + (5 + side) + "-ruin-" + names[side],
                    foot, plot.GroundCenter + Vector3.up * 1.8f, 76f, trough: true));
                Vector3 eye = foot + Vector3.up * EyeHeight;
                foreach (Vector3 point in interior)
                {
                    // A free capsule in a sealed cavity is insufficient: the
                    // actual room must be visible through missing wall sections
                    // from independent sides, rather than read as a solid wedge.
                    if (Physics.Linecast(eye, point + Vector3.up * 1.45f, ~0, QueryTriggerInteraction.Ignore))
                        continue;
                    visibleSides++;
                    break;
                }
            }
            TestContext.WriteLine($"Avalanche ruin: open interior visible from {visibleSides} accessible sides.");
            Assert.That(visibleSides, Is.GreaterThanOrEqualTo(2),
                "The damaged house must expose its hollow interior from at least two accessible sides.");
        }

        private static void VerifyAvalancheApproach(AlpineVillageRoot root)
        {
            AlpineVillagePathDescriptor service = default;
            foreach (AlpineVillagePathDescriptor path in root.Plan.Expansion.Paths)
                if (path.StableId == "village-ski-service-1") service = path;
            Assert.That(service.StableId, Is.EqualTo("village-ski-service-1"),
                "The old tow still has its existing approach.");
            int steps = Mathf.CeilToInt(service.LengthXZ / .5f);
            Vector3 previous = AvalancheGround(root, root.Plan.Expansion.ToLocal(service.Start));
            for (int index = 0; index <= steps; index++)
            {
                Vector2 local = root.Plan.Expansion.ToLocal(Vector3.Lerp(service.Start, service.End, index / (float)steps));
                Vector3 point = AvalancheGround(root, local);
                Assert.That(root.World.WalkableArea.Contains(point, .3f), Is.True, "Ski approach mask at " + local);
                Assert.That(AbandonmentCapsuleFree(point), Is.True, "Ski approach physical clearance at " + local);
                Vector3 constrained = root.World.WalkableArea.Constrain(previous, point, .3f);
                Assert.That(Vector2.Distance(new Vector2(constrained.x, constrained.z),
                    new Vector2(point.x, point.z)), Is.LessThan(.03f), "Ski approach movement at " + local);
                previous = point;
            }
        }

        private static void VerifyAvalancheFront(AlpineVillageRoot root, Transform avalanche)
        {
            Assert.That(avalanche, Is.Not.Null);
            AlpineVillageExpansionPlan expansion = root.Plan.Expansion;
            Collider[] solids = avalanche.GetComponentsInChildren<Collider>();
            // The outer probes cross both shoulders, where an invisible footprint
            // or a narrow central obstacle would leave a way around the debris.
            foreach (float across in new[] { -173f, -166f, -158f, -151f, -143f })
            {
                float front = float.NaN;
                for (float along = 106f; along <= 132f; along += .1f)
                    if (expansion.Avalanche.ContainsLocal(new Vector2(across, along)))
                    { front = along; break; }
                Assert.That(float.IsNaN(front), Is.False, "Missing avalanche shoulder at " + across);
                Vector3 before = AvalancheGround(root, new Vector2(across, front - 2.5f));
                Vector3 inside = AvalancheGround(root, new Vector2(across, front + 2f));
                Assert.That(root.World.WalkableArea.Contains(inside, .3f), Is.False,
                    "Movement can enter the avalanche at " + new Vector2(across, front + 2f));
                Vector3 beyond = AvalancheGround(root, new Vector2(across, front + 6f));
                Vector3 start = before;
                start.y = AvalancheGround(root, new Vector2(across, front)).y + 1.05f;
                Vector3 delta = beyond - before;
                delta.y = 0f;
                var ray = new Ray(start, delta.normalized);
                float nearest = float.PositiveInfinity;
                Vector3 nearestPoint = default;
                foreach (Collider solid in solids)
                    if (solid.Raycast(ray, out RaycastHit rayHit, delta.magnitude) && rayHit.distance < nearest)
                    { nearest = rayHit.distance; nearestPoint = rayHit.point; }
                Assert.That(float.IsPositiveInfinity(nearest), Is.False,
                    "The blocked shoulder needs actual deposit collision at " + new Vector2(across, front));
                float hitAlong = expansion.ToLocal(nearestPoint).y;
                TestContext.WriteLine($"Avalanche front {across:F1}: mask {front:F2}, collision {hitAlong:F2}");
                Assert.That(Mathf.Abs(hitAlong - front), Is.LessThan(.8f),
                    "The movement boundary must meet the visible deposit, without an invisible gap.");

                if (!Mathf.Approximately(across, -158f)) continue;
                Vector3 direction = (beyond - before).normalized;
                RaycastHit[] hits = Physics.CapsuleCastAll(before + Vector3.up * .55f,
                    before + Vector3.up * 1.5f, .28f, direction, Vector3.Distance(before, beyond),
                    ~0, QueryTriggerInteraction.Ignore);
                bool stopsCapsule = false;
                foreach (RaycastHit hit in hits)
                    if (hit.transform.IsChildOf(avalanche)) { stopsCapsule = true; break; }
                Assert.That(stopsCapsule, Is.True, "The hero capsule must meet the visible front.");
            }
        }

        private static void VerifyAvalancheMovement(AlpineVillageRoot root)
        {
            AlpineVillageExpansionPlan expansion = root.Plan.Expansion;
            // -157 crosses the shallow concave notch between the front lobes.
            // Repeated small steps must stop/slide locally, never jump to a
            // distant polygon edge or the village's fallback spawn.
            foreach (float across in new[] { -158f, -157f })
            {
                Vector3 point = AvalancheGround(root, new Vector2(across, 106f));
                Assert.That(root.World.WalkableArea.Contains(point, .3f), Is.True);
                for (int step = 0; step < 100; step++)
                {
                    Vector2 local = expansion.ToLocal(point);
                    Vector3 desired = AvalancheGround(root, local + Vector2.up * .1f);
                    Vector3 next = root.World.WalkableArea.Constrain(point, desired, .3f);
                    Vector2 actual = expansion.ToLocal(next);
                    Assert.That(expansion.Avalanche.ContainsLocal(actual, .299f), Is.False,
                        "Small forward steps entered the avalanche at " + actual);
                    Assert.That(Vector2.Distance(actual, local), Is.LessThan(.16f),
                        "Avalanche contact jumped to a remote edge or the village spawn.");
                    point = next;
                }
                Vector2 stopped = expansion.ToLocal(point);
                Assert.That(stopped.y, Is.InRange(108f, 111f), "The hero must stop at the nearby toe.");
                Assert.That(expansion.Avalanche.SignedDistance(stopped), Is.InRange(.299f, .5f),
                    "The stopped capsule must remain beside the visible front.");
            }
        }

        private static Vector3 AvalancheGround(AlpineVillageRoot root, Vector2 local)
        {
            Vector3 point = root.Plan.Expansion.ToWorld(local);
            point.y = AlpineVillageTerrainSampler.SampleHeight(root.Plan, new Vector2(point.x, point.z));
            return point;
        }

        private static Shot AvalancheShot(AlpineVillageRoot root, string name, Vector2 from,
            Vector2 toward, float aimHeight, float fieldOfView)
        {
            Vector3 foot = AvalancheGround(root, from);
            Vector3 target = AvalancheGround(root, toward) + Vector3.up * aimHeight;
            return AbandonmentShot(root, name, foot, target, fieldOfView, trough: true);
        }
    }
}
