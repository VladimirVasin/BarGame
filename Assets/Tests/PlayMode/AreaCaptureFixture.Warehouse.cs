using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [SetUp]
        public void SelectLegacyCombatCapture()
        {
            CombatTestRoot.UseLegacyArenaForTests = true;
            CombatTestRoot.WarehouseSeedForTests = null;
        }

        [TearDown]
        public void ReleaseCombatCaptureSelection()
        {
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = null;
        }

        [UnityTest]
        [Explicit("The real seeded warehouse, imported metres, stair traversal, opponent routes and rendered lighting.")]
        public IEnumerator CombatWarehouse()
        {
            float previousCaptureDelta = Time.captureDeltaTime;
            CombatTestRoot root = null;
            Time.captureDeltaTime = 1f / 60f;
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            try
            {
#if UNITY_EDITOR
                System.Type.GetType("BarPromenade.Editor.CombatWarehouseAssetValidation, BarPromenade.Editor", true)
                    .GetMethod("ValidateOrThrow", System.Type.EmptyTypes).Invoke(null, null);
#endif
                var plans = new[] { WarehouseArenaPlanner.Create(0), WarehouseArenaPlanner.Create(1), WarehouseArenaPlanner.Create(3) };
                AssertWarehouseDeterminism(plans[0], WarehouseArenaPlanner.Create(0));
                var templates = new HashSet<WarehouseArenaTemplate>();
                var route = new List<Vector3>();
                foreach (WarehouseArenaPlan plan in plans)
                {
                    templates.Add(plan.Template);
                    var area = new WarehouseArenaArea(plan);
                    foreach (Vector3 spawn in plan.SpawnPoints)
                    {
                        Assert.That(area.FindRoute(plan.HeroSpawn, spawn, route), Is.True,
                            "Every seeded spawn must be reachable from the actual lower entrance.");
                        Assert.That(route, Is.Not.Empty);
                        Assert.That(Vector3.Distance(route[route.Count - 1], spawn), Is.LessThan(.001f));
                    }
                }
                Assert.That(templates.Count, Is.EqualTo(3), "Representative seeds must exercise all warehouse shapes.");
                foreach (int seed in new[] { int.MinValue, int.MaxValue })
                {
                    WarehouseArenaPlan edgePlan = WarehouseArenaPlanner.Create(seed);
                    var edgeArea = new WarehouseArenaArea(edgePlan);
                    foreach (Vector3 spawn in edgePlan.SpawnPoints)
                        Assert.That(edgeArea.FindRoute(edgePlan.HeroSpawn, spawn, route), Is.True,
                            "The signed seed limits must preserve connected warehouse corridors.");
                }

                GameSessionState.BeginNewGame();
                yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest, LoadSceneMode.Single);
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                Assert.That(root, Is.Not.Null);
                Assert.That(root.IsInitialized, Is.True);
                Assert.That(root.WarehousePlan.Seed, Is.EqualTo(0));
                root.AutomaticSimulation = false;
                root.SetDuelLogging(false);
                for (int frame = 0; frame < SettleFrames; frame++) yield return null;
                Assert.That(GameInput.CanRead(GameInputContext.Gameplay), Is.True);
                Camera camera = root.CameraFollow.Camera;
                Assert.That(camera, Is.SameAs(Camera.main));
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "warehouse-00-entry");
                AssertWarehouseImportedMetres(root);
                AssertWarehousePhysicalSurfaces(root);

                root.SetSparring(false);
                WarehouseArenaStair stair = root.WarehousePlan.Stairs[0];
                Vector3 lower = stair.Foot + Vector3.back * .6f;
                Vector3 upper = stair.Top + Vector3.forward * .6f;
                root.Hero.ResetActor(lower + Vector3.up * PlayerFactory.GroundedRootOffset, Vector3.forward);
                root.Hero.ApplyMotorConstraint();
                Physics.SyncTransforms();
                yield return WalkWarehouseHero(root, upper);
                Assert.That(root.Hero.transform.position.y, Is.EqualTo(WarehouseArenaPlan.UpperHeight + PlayerFactory.GroundedRootOffset).Within(.1f),
                    "The production hero must climb the imported treads onto the actual gallery.");
                for (int frame = 0; frame < 8; frame++) yield return null;
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "warehouse-01-gameplay-upper");
                yield return WalkWarehouseHero(root, lower);
                Assert.That(root.Hero.transform.position.y, Is.EqualTo(PlayerFactory.GroundedRootOffset).Within(.1f),
                    "The same normal motor gravity must return the hero to the lower concrete floor.");

                // Matching XZ on separate storeys must not become a close-range
                // swing or a read of an attack through the gallery floor.
                root.SetSparring(true);
                root.Hero.ResetActor(new Vector3(0f, WarehouseArenaPlan.UpperHeight + PlayerFactory.GroundedRootOffset, 7f), Vector3.forward);
                root.Opponent.ResetActor(new Vector3(0f, PlayerFactory.GroundedRootOffset, 7f), Vector3.back);
                Physics.SyncTransforms();
                for (int frame = 0; frame < 12; frame++)
                {
                    root.Tick(1f / 60f);
                    Assert.That(root.Opponent.State.IsAttacking || root.Opponent.State.IsCharging ||
                        root.Opponent.State.IsShoving || root.Opponent.State.IsKicking, Is.False,
                        "A partner below the gallery must route to its stairs rather than attack through the floor.");
                    yield return null;
                }
                Assert.That(root.OpponentDecisionSequence, Is.Zero,
                    "Occluded target phases must not reach the opponent's combat perception.");

                root.SetSparring(true);
                root.Hero.ResetActor(stair.Top + Vector3.forward + Vector3.up * PlayerFactory.GroundedRootOffset, Vector3.back);
                root.Opponent.ResetActor(stair.Foot + Vector3.back + Vector3.up * PlayerFactory.GroundedRootOffset, Vector3.forward);
                Physics.SyncTransforms();
                yield return FollowWarehouseOpponent(root, ascend: true, captureStair: false);
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "warehouse-02-npc-upper");

                root.SetSparring(true);
                root.Hero.ResetActor(stair.Foot + Vector3.back + Vector3.up * PlayerFactory.GroundedRootOffset, Vector3.forward);
                root.Opponent.ResetActor(stair.Top + Vector3.forward + Vector3.up * PlayerFactory.GroundedRootOffset, Vector3.back);
                Physics.SyncTransforms();
                yield return FollowWarehouseOpponent(root, ascend: false, captureStair: true);

                root.SetSparring(false);
                WarehouseArenaPlan retained = root.WarehousePlan;
                root.ResetRound();
                Assert.That(root.WarehousePlan, Is.SameAs(retained), "Reset must preserve the seeded warehouse.");
                Assert.That(Vector3.Distance(root.Hero.transform.position,
                    retained.HeroSpawn + Vector3.up * PlayerFactory.GroundedRootOffset), Is.LessThan(.001f));
                Assert.That(root.PauseMenu.Open(), Is.True);
                Assert.That(root.NewWarehouseLayout(1), Is.False, "Pause must refuse a layout change.");
                Assert.That(root.WarehousePlan, Is.SameAs(retained));
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                for (int frame = 0; frame < 4; frame++) yield return null;

                foreach (WarehouseArenaPlan plan in plans)
                {
                    if (root.WarehousePlan.Seed != plan.Seed)
                    {
                        Assert.That(root.NewWarehouseLayout(plan.Seed), Is.True);
                        Assert.That(root.WarehousePlan.Seed, Is.EqualTo(plan.Seed));
                        Assert.That(root.Hero.State.IsDefeated || root.Opponent.State.IsDefeated, Is.False);
                        Assert.That(Vector3.Distance(root.Hero.transform.position,
                            plan.HeroSpawn + Vector3.up * PlayerFactory.GroundedRootOffset), Is.LessThan(.001f),
                            "New layout must restart its pair at the new plan anchors.");
                        for (int frame = 0; frame < 2; frame++) yield return null;
                    }
                    AssertWarehouseImportedMetres(root);
                    yield return CaptureWarehouseEnvironment(root);
                }
            }
            finally
            {
                if (root != null) root.AutomaticSimulation = false;
                Time.captureDeltaTime = previousCaptureDelta;
                CombatTestRoot.WarehouseSeedForTests = null;
                CombatTestRoot.UseLegacyArenaForTests = false;
            }
        }

        private static void AssertWarehouseDeterminism(WarehouseArenaPlan a, WarehouseArenaPlan b)
        {
            Assert.That(b.Template, Is.EqualTo(a.Template));
            CollectionAssert.AreEqual(a.Platforms, b.Platforms);
            CollectionAssert.AreEqual(a.SpawnPoints, b.SpawnPoints);
            CollectionAssert.AreEqual(a.NavigationNodes, b.NavigationNodes);
            Assert.That(b.Obstacles.Count, Is.EqualTo(a.Obstacles.Count));
            for (int i = 0; i < a.Obstacles.Count; i++)
            {
                Assert.That(b.Obstacles[i].Kind, Is.EqualTo(a.Obstacles[i].Kind));
                Assert.That(b.Obstacles[i].Bounds, Is.EqualTo(a.Obstacles[i].Bounds));
                Assert.That(b.Obstacles[i].Yaw, Is.EqualTo(a.Obstacles[i].Yaw));
            }
            for (int i = 0; i < a.Stairs.Count; i++) Assert.That(b.Stairs[i].Foot, Is.EqualTo(a.Stairs[i].Foot));
            Assert.That(b.NavigationEdges.Count, Is.EqualTo(a.NavigationEdges.Count));
            for (int i = 0; i < a.NavigationEdges.Count; i++)
            {
                Assert.That(b.NavigationEdges[i].From, Is.EqualTo(a.NavigationEdges[i].From));
                Assert.That(b.NavigationEdges[i].To, Is.EqualTo(a.NavigationEdges[i].To));
            }
        }

        private static void AssertWarehouseImportedMetres(CombatTestRoot root)
        {
            foreach (string kind in new[] { "Shell", "Platform", "Stair", "Rail", "Rack", "Cargo", "Pipes", "Lamp" })
            {
                GameObject wrapper = null;
                foreach (Transform child in root.GetComponentsInChildren<Transform>())
                    if (child.name == "Warehouse " + kind) { wrapper = child.gameObject; break; }
                Assert.That(wrapper, Is.Not.Null, "The real built warehouse must contain " + kind + ".");
                WarehouseArenaAssetProvider.ValidatePlacedModel(kind, wrapper);
            }
        }

        private static void AssertWarehousePhysicalSurfaces(CombatTestRoot root)
        {
            WarehouseArenaStair stair = root.WarehousePlan.Stairs[0];
            for (int step = 0; step < 16; step++)
            {
                Vector3 tread = stair.Foot + Vector3.forward * ((step + .5f) * .3f);
                Assert.That(Physics.Raycast(tread + Vector3.up * 4f, Vector3.down, out RaycastHit hit,
                    5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True);
                Assert.That(hit.point.y, Is.EqualTo((step + 1) * .1625f).Within(.025f),
                    "Rendered stair treads and their collision must agree in imported metres.");
            }
            Vector3 entrance = root.WarehousePlan.HeroSpawn + Vector3.right * .85f;
            Assert.That(Physics.Raycast(entrance + Vector3.up * .5f, Vector3.down, out RaycastHit floor,
                1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True);
            Assert.That(floor.point.y, Is.EqualTo(0f).Within(.025f));
            foreach (WarehouseArenaObstacle obstacle in root.WarehousePlan.Obstacles)
            {
                if (obstacle.Kind != WarehouseArenaObstacleKind.Cargo) continue;
                Bounds bounds = obstacle.Bounds;
                Vector3 outward = bounds.center.x < 0f ? Vector3.right : Vector3.left;
                Vector3 from = bounds.center + outward * (bounds.extents.x + .5f);
                Assert.That(Physics.Raycast(from, -outward, out RaycastHit cover,
                    bounds.size.x + 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True);
                bounds.Expand(.05f);
                Assert.That(bounds.Contains(cover.point), Is.True,
                    "Solid authored cargo must block a line through its visible body.");
                break;
            }
        }

        private static IEnumerator WalkWarehouseHero(CombatTestRoot root, Vector3 destination)
        {
            var area = new WarehouseArenaArea(root.WarehousePlan);
            bool arrived = false;
            for (int frame = 0; frame < 180; frame++)
            {
                root.Hero.ApplyMotorConstraint();
                Vector3 delta = destination - root.Hero.transform.position; delta.y = 0f;
                if (delta.sqrMagnitude <= .0025f) { arrived = true; break; }
                root.Player.Motor.ApplyOwnedImpulseDisplacement(root.Hero, Vector3.ClampMagnitude(delta, .05f));
                yield return null;
                AssertWarehouseSurfaceSupport(area, root.Hero.transform.position);
            }
            Assert.That(arrived, Is.True, "The actual hero capsule must traverse the first warehouse stair without teleporting.");
            for (int frame = 0; frame < 10; frame++) yield return null;
            Assert.That(root.Player.Motor.IsGrounded, Is.True);
        }

        private static IEnumerator FollowWarehouseOpponent(CombatTestRoot root, bool ascend, bool captureStair)
        {
            var area = new WarehouseArenaArea(root.WarehousePlan);
            bool arrived = false, stairCaptured = false;
            for (int frame = 0; frame < 140; frame++)
            {
                root.Tick(.05f);
                Vector3 position = root.Opponent.transform.position;
                AssertWarehouseSurfaceSupport(area, position);
                Assert.That(root.Opponent.IsRagdollActive, Is.False, "Stair traversal must retain the living world rig.");
                if (captureStair && !stairCaptured && position.y > .85f && position.y < 1.8f)
                {
                    CaptureCurrentCamera(root.CameraFollow.Camera, SceneIds.CombatTest, "warehouse-03-npc-stair");
                    stairCaptured = true;
                    // A target only one metre behind the foot invites a valid
                    // melee stop on the first tread. Put this staged target
                    // farther along the clear lower apron so pursuit must
                    // actually finish the descent before entering reach.
                    WarehouseArenaStair stair = root.WarehousePlan.Stairs[0];
                    Vector3 target = stair.Foot + Vector3.back * 2.5f + Vector3.up * PlayerFactory.GroundedRootOffset;
                    Assert.That(area.TryGetHeight(target, out float lowerHeight, .32f), Is.True);
                    Assert.That(lowerHeight, Is.EqualTo(0f));
                    root.Hero.ResetActor(target, Vector3.forward);
                    Physics.SyncTransforms();
                }
                float targetHeight = ascend ? WarehouseArenaPlan.UpperHeight : 0f;
                if (Mathf.Abs(position.y - targetHeight - PlayerFactory.GroundedRootOffset) < .1f)
                {
                    arrived = true;
                    break;
                }
                yield return null;
            }
            string diagnostic = $"seed={root.WarehousePlan.Seed}, NPC={root.Opponent.transform.position.ToString("F3")}, " +
                $"hero={root.Hero.transform.position.ToString("F3")}, phase={root.Opponent.State.Phase}, " +
                $"intent={root.OpponentIntent}, balance={root.Opponent.HasAttackBalance}, " + root.DescribeWarehouseOpponentNavigation();
            Assert.That(arrived, Is.True, "Normal NPC decisions and gravity must reach the " +
                (ascend ? "gallery" : "lower floor") + ". " + diagnostic);
            if (captureStair) Assert.That(stairCaptured, Is.True);
            for (int frame = 0; frame < 8; frame++) { root.Tick(1f / 60f); yield return null; }
            Assert.That(root.Opponent.Body.isGrounded, Is.True, "The arrived opponent must settle onto the physical floor.");
        }

        private static void AssertWarehouseSurfaceSupport(WarehouseArenaArea area, Vector3 position)
        {
            Assert.That(float.IsNaN(position.y) || float.IsInfinity(position.y), Is.False);
            Assert.That(area.TryGetHeight(position, out float height, .32f), Is.True,
                "The real moving capsule must remain in a declared warehouse corridor.");
            Assert.That(position.y - height, Is.InRange(-.04f, .32f),
                "A stair step may briefly descend under gravity, but cannot hover above or cut below its surface.");
        }

        private static IEnumerator CaptureWarehouseEnvironment(CombatTestRoot root)
        {
            Camera camera = root.CameraFollow.Camera;
            bool following = root.CameraFollow.enabled;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            string template = root.WarehousePlan.Template.ToString().ToLowerInvariant();
            WarehouseArenaStair stair = root.WarehousePlan.Stairs[0];
            var shots = new[]
            {
                Shot.At("warehouse-" + template + "-01-room", new Vector3(-10.8f, 4.8f, -7.8f), new Vector3(0f, 2.2f, 2.5f), 68f),
                Shot.At("warehouse-" + template + "-02-gallery", new Vector3(-8.5f, 4.35f, 7.9f), new Vector3(5f, 3.1f, 5.7f), 62f),
                Shot.At("warehouse-" + template + "-03-stair", stair.Foot + new Vector3(-2.3f, 1.6f, -1.5f),
                    stair.Foot + new Vector3(0f, 1.3f, 2.5f), 58f)
            };
            try
            {
                root.CameraFollow.enabled = false;
                foreach (Shot shot in shots)
                {
                    camera.transform.SetPositionAndRotation(shot.Position, Quaternion.LookRotation(shot.Target - shot.Position));
                    camera.fieldOfView = shot.FieldOfView;
                    for (int frame = 0; frame < 2; frame++) yield return null;
                    CaptureCurrentCamera(camera, SceneIds.CombatTest, shot.Name);
                }
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fov;
                root.CameraFollow.enabled = following;
            }
        }
    }
}
