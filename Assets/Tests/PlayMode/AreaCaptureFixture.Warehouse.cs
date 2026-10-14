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
                    Assert.That(plan.RoomCenters.Count, Is.EqualTo(9));
                    Assert.That(plan.Stairs.Count, Is.EqualTo(4));
                    foreach (Vector3 room in plan.RoomCenters)
                        Assert.That(area.FindRoute(plan.HeroSpawn, room, route), Is.True,
                            "Each small room must connect through its inward doorway on its own storey.");
                    Assert.That(area.FindRoute(plan.HeroSpawn, plan.GateApproach, route), Is.True);
                    Assert.That(area.CanTraverse(plan.GateApproach, plan.GoalDoorPosition), Is.False,
                        "The future main goal remains physically barred.");
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
                AssertWarehouseLightAndMechanisms(root);
                CombatWarehouseMusicPlayer music = root.Music;
                Assert.That(music, Is.Not.Null);
                Assert.That(music.Source.loop, Is.True);
                if (Resources.Load<AudioClip>(CombatWarehouseMusicPlayer.ResourcePath) == null)
                {
                    Assert.That(music.ActiveClip, Is.Null);
                    Assert.That(music.Source.isPlaying, Is.False,
                        "The prepared theme folder stays silent until the optional track is supplied.");
                }

                root.SetSparring(false);
                for (int i = 0; i < root.WarehousePlan.Stairs.Count; i++)
                {
                    WarehouseArenaStair stair = root.WarehousePlan.Stairs[i];
                    bool ascending = i % 2 == 0;
                    Vector3 lower = stair.Foot + Vector3.back * .6f;
                    Vector3 upper = stair.Top + Vector3.forward * .6f;
                    root.Hero.ResetActor((ascending ? lower : upper) + Vector3.up * PlayerFactory.GroundedRootOffset,
                        ascending ? Vector3.forward : Vector3.back);
                    root.Hero.ApplyMotorConstraint();
                    Physics.SyncTransforms();
                    yield return WalkWarehouseHero(root, ascending ? upper : lower);
                    float reachedHeight = ascending ? stair.Top.y : stair.Foot.y;
                    Assert.That(root.Hero.transform.position.y,
                        Is.EqualTo(reachedHeight + PlayerFactory.GroundedRootOffset).Within(.1f),
                        "The production hero must traverse all four physical stairs, including the second floor pair.");
                }

                // Matching XZ on separate storeys must not become a close-range
                // swing or a read of an attack through the gallery floor.
                root.SetSparring(true);
                Vector3 overDeck = new Vector3(0f, WarehouseArenaPlan.TopHeight, 7.5f);
                root.Hero.ResetActor(overDeck + Vector3.up * PlayerFactory.GroundedRootOffset, Vector3.forward);
                root.Opponent.ResetActor(overDeck - Vector3.up * WarehouseArenaPlan.LevelRise +
                    Vector3.up * PlayerFactory.GroundedRootOffset, Vector3.back);
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

                foreach (int stairIndex in new[] { 0, 2, 3 })
                {
                    root.SetSparring(true);
                    WarehouseArenaStair stair = root.WarehousePlan.Stairs[stairIndex];
                    bool ascend = stairIndex != 3;
                    Vector3 target = ascend ? stair.Top + Vector3.forward * 2f : stair.Foot + Vector3.back * 2.5f;
                    Vector3 start = ascend ? stair.Foot + Vector3.back * .8f : stair.Top + Vector3.forward * .8f;
                    root.Hero.ResetActor(target + Vector3.up * PlayerFactory.GroundedRootOffset,
                        ascend ? Vector3.back : Vector3.forward);
                    root.Opponent.ResetActor(start + Vector3.up * PlayerFactory.GroundedRootOffset,
                        ascend ? Vector3.forward : Vector3.back);
                    Physics.SyncTransforms();
                    yield return FollowWarehouseOpponent(root, stair, ascend);
                }

                root.SetSparring(false);
                WarehouseArenaPlan retained = root.WarehousePlan;
                root.ResetRound();
                Assert.That(root.WarehousePlan, Is.SameAs(retained), "Reset must preserve the seeded warehouse.");
                Assert.That(root.Music, Is.SameAs(music), "A combat reset must retain the optional theme carrier.");
                Assert.That(Vector3.Distance(root.Hero.transform.position,
                    retained.HeroSpawn + Vector3.up * PlayerFactory.GroundedRootOffset), Is.LessThan(.001f));
                Assert.That(root.PauseMenu.Open(), Is.True);
                WarehouseArenaMechanisms machines = root.GetComponentInChildren<WarehouseArenaMechanisms>();
                Quaternion pausedRotor = machines.FanRotor.localRotation;
                double pausedTime = machines.ElapsedSeconds;
                machines.Advance(1f);
                Assert.That(machines.ElapsedSeconds, Is.EqualTo(pausedTime));
                Assert.That(Quaternion.Angle(machines.FanRotor.localRotation, pausedRotor), Is.LessThan(.001f),
                    "Pause must freeze the rigged machines rather than only their sound.");
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
                        Assert.That(root.Music, Is.SameAs(music), "A new layout must preserve the theme and its playhead.");
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
            CollectionAssert.AreEqual(a.RoomCenters, b.RoomCenters);
            Assert.That(b.GatePosition, Is.EqualTo(a.GatePosition));
            Assert.That(b.Mechanisms.Count, Is.EqualTo(a.Mechanisms.Count));
            for (int i = 0; i < a.Mechanisms.Count; i++)
            {
                Assert.That(b.Mechanisms[i].Kind, Is.EqualTo(a.Mechanisms[i].Kind));
                Assert.That(b.Mechanisms[i].Position, Is.EqualTo(a.Mechanisms[i].Position));
                Assert.That(b.Mechanisms[i].Phase, Is.EqualTo(a.Mechanisms[i].Phase));
            }
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
            Assert.That(WarehouseArenaAssetProvider.ModelNames.Length, Is.EqualTo(25));
            foreach (string kind in WarehouseArenaAssetProvider.ModelNames)
            {
                Assert.That(Resources.Load<GameObject>(WarehouseArenaAssetProvider.ResourceFolder + kind), Is.Not.Null,
                    "The accepted passive model bank must include " + kind + ".");
                GameObject wrapper = null;
                foreach (Transform child in root.GetComponentsInChildren<Transform>())
                    if (child.name == "Warehouse " + kind) { wrapper = child.gameObject; break; }
                if (kind == "PipeStraight" || kind == "PipeElbow" || kind == "PipeEnd" || kind == "TwistedFrame") continue;
                Assert.That(wrapper, Is.Not.Null, "The real built warehouse must contain " + kind + ".");
                // Its metre vertices are measured below; a rotating wrapper's
                // world AABB cannot be inverse-rotated into its authored bounds.
                if (kind != "FanRotor" && kind != "LiftPlatform" && kind != "ChainDrum" &&
                    kind != "PusherHead" && kind != "Rail")
                    WarehouseArenaAssetProvider.ValidatePlacedModel(kind, wrapper);
            }
        }

        private static void AssertWarehouseLightAndMechanisms(CombatTestRoot root)
        {
            Assert.That(root.WarehousePlan.Lamps.Count, Is.EqualTo(6));
            int practical = 0, active = 0;
            foreach (Light light in Object.FindObjectsByType<Light>())
            {
                if (!light.isActiveAndEnabled || light.intensity <= 0f) continue;
                active++;
                if (light.gameObject.name != "Warehouse Work Lamp")
                {
                    Assert.That(light, Is.SameAs(root.Flashlight.SpotLight));
                    continue;
                }
                practical++;
                Assert.That(light.type, Is.EqualTo(LightType.Spot));
                Assert.That(light.shadows, Is.EqualTo(LightShadows.Hard));
                Assert.That(light.intensity, Is.LessThan(root.Flashlight.SpotLight.intensity * .12f));
            }
            Assert.That(practical, Is.EqualTo(6));
            Assert.That(active, Is.EqualTo(7), "Six rare practical pools plus the hero's existing chest light.");
            WarehouseArenaMechanisms machines = root.GetComponentInChildren<WarehouseArenaMechanisms>();
            Assert.That(machines, Is.Not.Null);
            Assert.That(machines.MachineCount, Is.EqualTo(4));
            Assert.That(machines.FanRotor.position.y, Is.EqualTo(WarehouseArenaPlan.CeilingHeight - .42f).Within(.001f));
            float sweptRadius = 0f;
            foreach (MeshFilter mesh in machines.FanRotor.GetComponentsInChildren<MeshFilter>())
                foreach (Vector3 vertex in mesh.sharedMesh.vertices)
                {
                    Vector3 local = machines.FanRotor.InverseTransformPoint(mesh.transform.TransformPoint(vertex));
                    sweptRadius = Mathf.Max(sweptRadius, new Vector2(local.x, local.z).magnitude);
                }
            Assert.That(sweptRadius * 2f, Is.EqualTo(7f).Within(.01f),
                "The huge ceiling rotor must retain seven authored metres at runtime.");
            Assert.That(machines.FanRotor.GetComponentsInChildren<Collider>().Length, Is.Zero);
            Quaternion before = machines.FanRotor.localRotation;
            machines.Advance(1f);
            Assert.That(Quaternion.Angle(before, machines.FanRotor.localRotation), Is.GreaterThan(8f));
        }

        private static void AssertWarehousePhysicalSurfaces(CombatTestRoot root)
        {
            foreach (WarehouseArenaStair stair in root.WarehousePlan.Stairs)
                for (int step = 0; step < WarehouseArenaStair.TreadCount; step++)
                {
                    float height = stair.Foot.y + (step + 1) * WarehouseArenaStair.TreadRise;
                    Vector3 tread = stair.Foot + Vector3.forward * ((step + .5f) * WarehouseArenaStair.TreadDepth);
                    tread.y = height;
                    Assert.That(Physics.Raycast(tread + Vector3.up * .35f, Vector3.down, out RaycastHit hit,
                        .65f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True);
                    Assert.That(hit.point.y, Is.EqualTo(height).Within(.025f),
                        "A short ray must hit this storey's tread rather than the stacked staircase above it.");
                }
            Vector3 entrance = root.WarehousePlan.HeroSpawn + Vector3.right * .85f;
            Assert.That(Physics.Raycast(entrance + Vector3.up * .5f, Vector3.down, out RaycastHit floor,
                1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True);
            Assert.That(floor.point.y, Is.EqualTo(0f).Within(.025f));
            Vector3 gateRay = root.WarehousePlan.GateApproach + Vector3.up * 1.2f;
            Assert.That(Physics.Raycast(gateRay, Vector3.forward, out RaycastHit gate, 2.2f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True);
            Assert.That(gate.point.z, Is.EqualTo(root.WarehousePlan.GatePosition.z).Within(.08f),
                "Actual barred-gate collision must stop the approach before the main door.");
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
            for (int frame = 0; frame < 240; frame++)
            {
                root.Hero.ApplyMotorConstraint();
                Vector3 delta = destination - root.Hero.transform.position; delta.y = 0f;
                if (delta.sqrMagnitude <= .0025f) { arrived = true; break; }
                root.Player.Motor.ApplyOwnedImpulseDisplacement(root.Hero, Vector3.ClampMagnitude(delta, .05f));
                yield return null;
                AssertWarehouseSurfaceSupport(area, root.Hero.transform.position);
            }
            Assert.That(arrived, Is.True, "The actual hero capsule must traverse each warehouse stair without teleporting.");
            for (int frame = 0; frame < 10; frame++) yield return null;
            Assert.That(root.Player.Motor.IsGrounded, Is.True);
        }

        private static IEnumerator FollowWarehouseOpponent(CombatTestRoot root, WarehouseArenaStair stair, bool ascend)
        {
            var area = new WarehouseArenaArea(root.WarehousePlan);
            bool arrived = false;
            for (int frame = 0; frame < 190; frame++)
            {
                root.Tick(.05f);
                Vector3 position = root.Opponent.transform.position;
                AssertWarehouseSurfaceSupport(area, position);
                Assert.That(root.Opponent.IsRagdollActive, Is.False, "Stair traversal must retain the living world rig.");
                float targetHeight = ascend ? stair.Top.y : stair.Foot.y;
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
            root.SetSparring(false);
            Camera camera = root.CameraFollow.Camera;
            bool following = root.CameraFollow.enabled;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            Vector3 heroPosition = root.Hero.transform.position, heroFacing = root.Hero.transform.forward;
            Renderer[] heroRenderers = root.Hero.GetComponentsInChildren<Renderer>(true);
            var heroRenderingFlags = new bool[heroRenderers.Length];
            for (int i = 0; i < heroRenderers.Length; i++)
                heroRenderingFlags[i] = heroRenderers[i].forceRenderingOff;
            WarehouseArenaPlan plan = root.WarehousePlan;
            string template = plan.Template == WarehouseArenaTemplate.LeftGallery ? "Left gallery" :
                plan.Template == WarehouseArenaTemplate.UShape ? "Looped storage" : "Straight layout";
            string caption = "Test warehouse - " + template + " - ";
            WarehouseArenaStair stair = plan.Stairs[0], topStair = plan.Stairs[2];
            Vector3 room = plan.RoomCenters[0], corridorRoom = plan.RoomCenters[3];
            Vector3 fan = plan.Mechanisms[0].Position, lift = plan.Mechanisms[1].Position,
                drum = plan.Mechanisms[2].Position, pusher = plan.Mechanisms[3].Position;
            var shots = new[]
            {
                // The entry is beneath the front gallery. Step into the actual
                // central void before photographing its ceiling-mounted rotor.
                Shot.At(caption + "Main hall and ceiling fan", plan.HeroSpawn + new Vector3(0f, 1.7f, 4.7f), fan, 72f),
                Shot.At(caption + "Top gallery", topStair.Top + new Vector3(0f, 1.7f, 1f), fan, 68f),
                Shot.At(caption + "Physical stair", stair.Foot + new Vector3(-2.5f, 1.7f, -1.4f),
                    stair.Foot + new Vector3(0f, 1.8f, 3f), 62f),
                Shot.At(caption + "Small technical room", room + Vector3.up * 1.7f,
                    room + new Vector3(-1.5f, 1f, -1.8f), 68f),
                Shot.At(caption + "Locker corridor", corridorRoom + new Vector3(4.5f, 1.7f, .7f),
                    corridorRoom + Vector3.up * 1.4f, 64f),
                Shot.At(caption + "Barred main door", plan.GateApproach + new Vector3(0f, 1.7f, -1f),
                    plan.GatePosition + Vector3.up * 1.35f, 68f),
                Shot.At(caption + "Empty freight lift", lift + new Vector3(3f, 1.7f, -3f),
                    lift + Vector3.up * 4.2f, 72f),
                Shot.At(caption + "Chain drum", drum + new Vector3(0f, 1.7f, -3f),
                    drum + Vector3.up * 1.1f, 65f),
                Shot.At(caption + "Endless load pusher", pusher + new Vector3(2.7f, 1.7f, -2.7f),
                    pusher + Vector3.up, 68f)
            };
            try
            {
                root.CameraFollow.enabled = false;
                var area = new WarehouseArenaArea(plan);
                var visual = (Player3DCharacterPresentation)root.Player.Visual;
                foreach (Shot shot in shots)
                {
                    Vector3 facing = shot.Target - shot.Position; facing.y = 0f; facing.Normalize();
                    Vector3 foot = shot.Position - Vector3.up * 1.7f - facing * .65f;
                    Assert.That(area.TryGetHeight(foot, out float height, .32f), Is.True,
                        "The staged real hero must stand in the declared corridor for " + shot.Name + ".");
                    foot.y = height;
                    root.Hero.ResetActor(foot + Vector3.up * PlayerFactory.GroundedRootOffset, facing);
                    root.Hero.SetCombatFocused(false);
                    root.Hero.ApplyMotorConstraint();
                    Physics.SyncTransforms();
                    camera.transform.SetPositionAndRotation(shot.Position, Quaternion.LookRotation(shot.Target - shot.Position));
                    camera.fieldOfView = shot.FieldOfView;
                    for (int frame = 0; frame < 2; frame++) yield return null;
                    // Use the continuous hero rig and its real chest source;
                    // the six dim practical lamps keep their production settings.
                    visual.SetTorsoLookIntent(root.Hero, shot.Target - (root.Hero.transform.position + Vector3.up * 1.4f), 0f, false);
                    visual.AdvanceTorsoLook(root.Hero, .5f);
                    visual.ReapplyLatePresentationPose();
                    WarehouseArenaLighting.UpdateHeroVisibility(root.Flashlight.transform);
                    // Environment photographs keep the authoritative rig and
                    // chest light active while removing near-camera body/weapon silhouettes.
                    foreach (Renderer renderer in heroRenderers)
                        if (renderer != null) renderer.forceRenderingOff = true;
                    CaptureCurrentCamera(camera, SceneIds.CombatTest, shot.Name);
                }
            }
            finally
            {
                root.Hero.ResetActor(heroPosition, heroFacing);
                ((Player3DCharacterPresentation)root.Player.Visual).ClearTorsoLook(root.Hero);
                for (int i = 0; i < heroRenderers.Length; i++)
                    if (heroRenderers[i] != null) heroRenderers[i].forceRenderingOff = heroRenderingFlags[i];
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fov;
                root.CameraFollow.enabled = following;
            }
        }
    }
}
