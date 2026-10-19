using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
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
                    AssertWarehouseRoomFurnitureFacesEntry(plan);
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
                AssertWarehouseSurfaceMaterials(root);
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
                    if (stair.IsBroken)
                    {
                        AssertWarehouseBrokenStairRoutes(root.WarehousePlan);
                        continue;
                    }
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
                        "The production hero must traverse each intact physical flight, including the remaining upper route.");
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

                for (int traversal = 0; traversal < 3; traversal++)
                {
                    root.SetSparring(true);
                    int stairIndex = traversal == 0 ? 0 : 3;
                    WarehouseArenaStair stair = root.WarehousePlan.Stairs[stairIndex];
                    bool ascend = traversal != 2;
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
                    AssertWarehouseSurfaceMaterials(root);
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

        [UnityTest]
        [Explicit("Real warehouse goal fixture, locked wheel, printed room plates and unchanged closed gate.")]
        public IEnumerator CombatWarehouseGoalVisuals()
        {
            float previousDelta = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            try
            {
#if UNITY_EDITOR
                System.Type.GetType("BarPromenade.Editor.CombatWarehouseAssetValidation, BarPromenade.Editor", true)
                    .GetMethod("ValidateOrThrow", System.Type.EmptyTypes).Invoke(null, null);
#endif
                GameSessionState.BeginNewGame();
                yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest, LoadSceneMode.Single);
                CombatTestRoot root = Object.FindAnyObjectByType<CombatTestRoot>();
                Assert.That(root != null && root.IsInitialized, Is.True);
                root.AutomaticSimulation = false;
                root.SetDuelLogging(false);
                root.SetSparring(false);
                for (int frame = 0; frame < SettleFrames; frame++) yield return null;

                Light emergency = AssertWarehouseGoalVisuals(root);
                WarehouseArenaPlan alternate = WarehouseArenaPlanner.Create(1);
                for (int number = 1; number <= 9; number++)
                {
                    WarehouseArenaPlacement first = FindWarehousePlacement(root.WarehousePlan, "RoomSign" + number);
                    WarehouseArenaPlacement second = FindWarehousePlacement(alternate, "RoomSign" + number);
                    Assert.That(second.Position, Is.EqualTo(first.Position), "Room numbers stay with their doors across seeds.");
                    Assert.That(second.Yaw, Is.EqualTo(first.Yaw));
                }
                float power = emergency.intensity;
                Color color = emergency.color;
                Assert.That(root.PauseMenu.Open(), Is.True);
                yield return null;
                Assert.That(emergency.isActiveAndEnabled, Is.True);
                Assert.That(emergency.intensity, Is.EqualTo(power));
                Assert.That(emergency.color, Is.EqualTo(color), "The emergency source is constant, including during pause.");
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                yield return CaptureWarehouseGoalVisuals(root);

                Assert.That(root.NewWarehouseLayout(1), Is.True);
                yield return null;
                Assert.That(emergency == null, Is.True, "Changing layout must release the old physical source.");
                AssertWarehouseGoalVisuals(root);
                yield return VerifyWarehouseValveInspection(root);
            }
            finally
            {
                Time.captureDeltaTime = previousDelta;
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = null;
            }
        }

        private static WarehouseArenaPlacement FindWarehousePlacement(WarehouseArenaPlan plan, string kind)
        {
            foreach (WarehouseArenaPlacement placement in plan.ModulePlacements)
                if (placement.Kind == kind) return placement;
            Assert.Fail("The warehouse plan requires " + kind + ".");
            return default;
        }

        private static GameObject FindWarehouseModel(CombatTestRoot root, string kind)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>())
                if (child.name == "Warehouse " + kind) return child.gameObject;
            Assert.Fail("The real warehouse requires " + kind + ".");
            return null;
        }

        private static Light AssertWarehouseGoalVisuals(CombatTestRoot root)
        {
            WarehouseArenaPlan plan = root.WarehousePlan;
            GameObject gate = FindWarehouseModel(root, "Gate");
            GameObject control = FindWarehouseModel(root, "GateControl");
            GameObject fixture = FindWarehouseModel(root, "EmergencyLamp");
            WarehouseArenaAssetProvider.ValidatePlacedModel("Gate", gate);
            WarehouseArenaAssetProvider.ValidatePlacedModel("GateControl", control);
            WarehouseArenaAssetProvider.ValidatePlacedModel("EmergencyLamp", fixture);
            Assert.That(control.transform.position, Is.EqualTo(plan.GateControlPosition));
            foreach (Collider collider in control.GetComponentsInChildren<Collider>(true))
                Assert.That(collider.isTrigger, Is.True,
                    "The wall control may offer an inspection trigger, without obstructing the approach.");
            bool chain = false, lockFace = false, openRim = false;
            Vector3 wheelCenter = control.transform.InverseTransformPoint(
                WarehouseArenaAssetProvider.FindAnchor(control, "Wheel").position);
            foreach (MeshRenderer renderer in control.GetComponentsInChildren<MeshRenderer>())
            {
                if (renderer.name.EndsWith("__SafetyYellow", System.StringComparison.Ordinal)) chain = true;
                if (renderer.name.StartsWith("Padlock", System.StringComparison.Ordinal))
                {
                    lockFace = true;
                    Assert.That(renderer.name.EndsWith("__LockSteel", System.StringComparison.Ordinal), Is.True,
                        "The padlock body and shackle use neutral steel, distinct from the yellow chain.");
                    Assert.That(renderer.sharedMaterial.name.EndsWith(" LockSteel Shared", System.StringComparison.Ordinal), Is.True);
                }
                if (!renderer.name.StartsWith("WheelRim", System.StringComparison.Ordinal)) continue;
                openRim = true;
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                for (int triangle = 0; triangle < triangles.Length; triangle += 3)
                {
                    Vector3 center = (vertices[triangles[triangle]] + vertices[triangles[triangle + 1]] +
                        vertices[triangles[triangle + 2]]) / 3f;
                    center = control.transform.InverseTransformPoint(renderer.transform.TransformPoint(center)) - wheelCenter;
                    Assert.That(new Vector2(center.x, center.y).magnitude, Is.GreaterThan(.29f),
                        "The actual rim triangles must leave the wheel centre open, without a filled disc cap.");
                }
            }
            Assert.That(chain && lockFace && openRim, Is.True, "The open wheel must carry a yellow chain and a neutral steel padlock.");

            int printed = 0;
            Material plateMaterial = null;
            foreach (WarehouseArenaPlacement placement in plan.ModulePlacements)
            {
                if (!placement.Kind.StartsWith("RoomSign", System.StringComparison.Ordinal)) continue;
                int number = int.Parse(placement.Kind.Substring("RoomSign".Length));
                Assert.That(number, Is.InRange(1, 9));
                Vector3 room = plan.RoomCenters[number - 1];
                Quaternion rotation = Quaternion.Euler(0f, placement.Yaw, 0f);
                Assert.That(Vector3.Distance(placement.Position,
                    room + rotation * new Vector3(3.015f, 1.7f, 1.65f)), Is.LessThan(.001f));
                GameObject sign = FindWarehouseModel(root, placement.Kind);
                WarehouseArenaAssetProvider.ValidatePlacedModel(placement.Kind, sign);
                Assert.That(sign.GetComponentsInChildren<Collider>(true), Is.Empty);
                Assert.That(sign.GetComponentsInChildren<TextMesh>(true), Is.Empty,
                    "Numbers are printed into the shared atlas, without independent text geometry.");
                bool face = false;
                Bounds plateBounds = default;
                bool hasBounds = false;
                foreach (MeshRenderer renderer in sign.GetComponentsInChildren<MeshRenderer>())
                {
                    if (!hasBounds) { plateBounds = renderer.bounds; hasBounds = true; }
                    else plateBounds.Encapsulate(renderer.bounds);
                    if (!renderer.name.EndsWith("__NumberPlate", System.StringComparison.Ordinal)) continue;
                    face = true;
                    Assert.That(renderer.GetComponent<MeshFilter>().sharedMesh.triangles.Length, Is.EqualTo(6),
                        "Each numeral is printed on a single flat quad, without digit geometry.");
                    Material material = renderer.sharedMaterial;
                    Assert.That(material.GetTexture("_BaseMap").name, Is.EqualTo("WarehouseMasterAtlas"));
                    if (plateMaterial == null) plateMaterial = material;
                    else Assert.That(material, Is.SameAs(plateMaterial), "All printed plates share one atlas material.");
                }
                Assert.That(face, Is.True);
                Assert.That(plateBounds.size.y, Is.EqualTo(.50f).Within(.005f), "Room plates retain their enlarged authored height.");
                Assert.That(plateBounds.size.z, Is.EqualTo(.44f).Within(.005f), "Room plates retain their enlarged authored width.");
                printed++;
            }
            Assert.That(printed, Is.EqualTo(9));

            var area = new WarehouseArenaArea(plan);
            var route = new List<Vector3>();
            Assert.That(area.FindRoute(plan.HeroSpawn, plan.GateControlApproach, route), Is.True,
                "The valve's standing dock must connect to the warehouse entrance.");
            Assert.That(area.CanTraverse(plan.GateApproach, plan.GateControlApproach, .36f), Is.True,
                "The pusher must leave the direct gate-front approach to the valve clear.");
            Vector3 aisle = plan.GateControlApproach - plan.GateApproach;
            Assert.That(Physics.CapsuleCast(plan.GateApproach + Vector3.up * .36f,
                plan.GateApproach + Vector3.up * 1.4f, .32f, aisle.normalized, out _, aisle.magnitude,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.False,
                "The real authored machinery must leave the valve aisle physically clear.");
            Assert.That(area.CanTraverse(plan.GateApproach, plan.GoalDoorPosition), Is.False);
            Assert.That(Physics.Raycast(plan.GateApproach + Vector3.up * 1.2f, Vector3.forward,
                out RaycastHit hit, 2.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True);
            Assert.That(hit.collider.transform.IsChildOf(gate.transform), Is.True,
                "The unchanged gate collision must still stop the approach before the main door.");
            Assert.That(hit.point.z, Is.EqualTo(plan.GatePosition.z).Within(.08f));

            Transform source = WarehouseArenaAssetProvider.FindAnchor(fixture, "Light");
            Light emergency = null;
            int practicals = 0, active = 0, red = 0;
            WarehouseFishLamp fishLamp = root.GetComponentInChildren<WarehouseFishLamp>();
            int poweredFishLamp = fishLamp != null && fishLamp.Light.isActiveAndEnabled && fishLamp.Light.intensity > 0f ? 1 : 0;
            WarehouseArenaMechanisms fanMachines = root.GetComponentInChildren<WarehouseArenaMechanisms>();
            int poweredFan = fanMachines != null && fanMachines.FanLight.isActiveAndEnabled && fanMachines.FanLight.intensity > 0f ? 1 : 0;
            foreach (Light light in root.GetComponentsInChildren<Light>())
            {
                if (!light.isActiveAndEnabled || light.intensity <= 0f) continue;
                active++;
                if (light.name != "Warehouse Work Lamp") continue;
                practicals++;
                if (light.color.r < light.color.g * 3f) continue;
                red++; emergency = light;
            }
            Assert.That(practicals, Is.EqualTo(5 + poweredFishLamp), "The fish-table pendant joins the five powered practicals only after collection.");
            Assert.That(active, Is.EqualTo(6 + ActiveWarehouseRoomIndicatorCount(root) + poweredFishLamp + poweredFan));
            Assert.That(red, Is.EqualTo(1));
            Assert.That(Vector3.Distance(emergency.transform.position, source.position), Is.LessThan(.001f));
            Assert.That(emergency.type, Is.EqualTo(LightType.Spot));
            Assert.That(emergency.shadows, Is.EqualTo(LightShadows.Hard));
            Assert.That(emergency.GetComponent<UniversalAdditionalLightData>().additionalLightsShadowResolutionTier,
                Is.EqualTo(UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow));
            return emergency;
        }

        private static IEnumerator CaptureWarehouseGoalVisuals(CombatTestRoot root)
        {
            WarehouseArenaPlan plan = root.WarehousePlan;
            Transform wheel = WarehouseArenaAssetProvider.FindAnchor(FindWarehouseModel(root, "GateControl"), "Wheel");
            var shots = new List<Shot>
            {
                Shot.At("Test warehouse - Gate goal", new Vector3(0f, 1.7f, 7.1f),
                    plan.GatePosition + new Vector3(-.7f, 1.55f, 0f), 68f),
                Shot.At("Test warehouse - Locked yellow chain", new Vector3(-1.6f, 1.7f, 9.1f), wheel.position, 46f)
            };
            for (int number = 1; number <= 9; number++)
            {
                WarehouseArenaPlacement sign = FindWarehousePlacement(plan, "RoomSign" + number);
                Quaternion rotation = Quaternion.Euler(0f, sign.Yaw, 0f);
                Vector3 room = plan.RoomCenters[number - 1];
                shots.Add(Shot.At("Test warehouse - Room " + number + " sign",
                    room + rotation * new Vector3(5.1f, 1.7f, .3f), sign.Position, 60f));
            }
            Camera camera = root.CameraFollow.Camera;
            bool following = root.CameraFollow.enabled;
            Vector3 position = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            Vector3 heroPosition = root.Hero.transform.position, heroFacing = root.Hero.transform.forward;
            Renderer[] renderers = root.Hero.GetComponentsInChildren<Renderer>(true);
            var hidden = new bool[renderers.Length];
            for (int index = 0; index < renderers.Length; index++) hidden[index] = renderers[index].forceRenderingOff;
            var visual = (Player3DCharacterPresentation)root.Player.Visual;
            try
            {
                root.CameraFollow.enabled = false;
                var area = new WarehouseArenaArea(plan);
                foreach (Shot shot in shots)
                {
                    Vector3 facing = shot.Target - shot.Position; facing.y = 0f; facing.Normalize();
                    Vector3 foot = shot.Position - Vector3.up * 1.7f - facing * .65f;
                    Assert.That(area.TryGetHeight(foot, out float height, .32f), Is.True,
                        "The real hero must stand in the existing aisle for " + shot.Name + ".");
                    foot.y = height;
                    root.Hero.ResetActor(foot + Vector3.up * PlayerFactory.GroundedRootOffset, facing);
                    root.Hero.SetCombatFocused(false);
                    root.Hero.ApplyMotorConstraint();
                    Physics.SyncTransforms();
                    camera.transform.SetPositionAndRotation(shot.Position, Quaternion.LookRotation(shot.Target - shot.Position));
                    camera.fieldOfView = shot.FieldOfView;
                    for (int frame = 0; frame < 2; frame++) yield return null;
                    visual.SetTorsoLookIntent(root.Hero,
                        shot.Target - (root.Hero.transform.position + Vector3.up * 1.4f), 0f, false);
                    visual.AdvanceTorsoLook(root.Hero, .5f);
                    visual.ReapplyLatePresentationPose();
                    WarehouseArenaLighting.UpdateHeroVisibility(root.Flashlight.transform);
                    foreach (Renderer renderer in renderers)
                        if (renderer != null) renderer.forceRenderingOff = true;
                    CaptureCurrentCamera(camera, SceneIds.CombatTest, shot.Name);
                }
            }
            finally
            {
                root.Hero.ResetActor(heroPosition, heroFacing);
                visual.ClearTorsoLook(root.Hero);
                for (int index = 0; index < renderers.Length; index++)
                    if (renderers[index] != null) renderers[index].forceRenderingOff = hidden[index];
                camera.transform.SetPositionAndRotation(position, cameraRotation);
                camera.fieldOfView = fov;
                root.CameraFollow.enabled = following;
            }
        }

        private static IEnumerator VerifyWarehouseValveInspection(CombatTestRoot root)
        {
            GameObject control = FindWarehouseModel(root, "GateControl");
            NarrativeInteraction target = null;
            foreach (NarrativeInteraction candidate in root.GetComponentsInChildren<NarrativeInteraction>())
                if (candidate.SubjectRoot == control.transform ||
                    candidate.SubjectRoot != null && candidate.SubjectRoot.IsChildOf(control.transform))
                {
                    Assert.That(target, Is.Null, "The valve uses one shared inspection target.");
                    target = candidate;
                }
            Assert.That(target, Is.Not.Null);
            Assert.That(target.Staging.CameraMode, Is.EqualTo(NarrativeCameraMode.ObjectCloseUp));
            Assert.That(target.Definition.Pages.Count, Is.EqualTo(1));
            Assert.That(target.Definition.RequiresRunningGameTime, Is.False);
            Assert.That(new NarrativeInteractionDefinition("Test inspection", target.PromptKey,
                target.Definition.Pages).RequiresRunningGameTime, Is.True,
                "Story inspections keep their default calendar admission guard.");
            Assert.That(target.Definition.Pages[0].Compact, Is.True);
            Assert.That(target.Confirmation, Is.Null, "The locked tug precedes the information page without a separate choice.");
            Assert.That(target.InspectionAttemptSeconds, Is.EqualTo(1.2f));
            MeshRenderer[] controlMeshes = control.GetComponentsInChildren<MeshRenderer>();
            MeshRenderer[] movingMeshes =
            {
                controlMeshes.First(mesh => mesh.name.StartsWith("WheelRim", System.StringComparison.Ordinal)),
                controlMeshes.First(mesh => mesh.name.StartsWith("YellowChain", System.StringComparison.Ordinal)),
                controlMeshes.First(mesh => mesh.name.StartsWith("PadlockBody", System.StringComparison.Ordinal))
            };
            Transform[] movers = movingMeshes.Select(mesh => mesh.transform).ToArray();
            Vector3[] restPositions = movers.Select(part => part.position).ToArray();
            Vector3[] restCenters = movingMeshes.Select(mesh => mesh.bounds.center).ToArray();
            Quaternion[] restRotations = movers.Select(part => part.rotation).ToArray();
            Assert.That(Vector3.Distance(target.Staging.Entry.RootPosition,
                root.WarehousePlan.GateControlApproach + Vector3.up * PlayerFactory.GroundedRootOffset), Is.LessThan(.001f));
            string textKey = target.Definition.Pages[0].TextKey;
            Assert.That(target.PromptKey, Is.EqualTo("interaction.warehouse_valve"));
            Assert.That(textKey, Is.EqualTo("combat.warehouse.valve.locked"));
            Assert.That(GameSessionState.IsGameTimeRunning, Is.False,
                "The CombatTest inspection must work without starting the story calendar.");

            PlayerInteractor hero = root.Player.Interactor;
            NarrativeInteractionController session = NarrativeInteractionController.For(hero);
            Vector3 heroPosition = root.Hero.transform.position, heroFacing = root.Hero.transform.forward;
            var input = new InputTestFixture();
            input.Setup();
            bool captureAttempt = true;
            try
            {
                Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
                foreach (string language in new[] { "ru", "en" })
                {
                    var catalog = JsonUtility.FromJson<NameplateCatalog>(Resources.Load<TextAsset>("Localization/" + language).text);
                    string[] keys = { target.PromptKey, textKey, "narrative.controls.close" };
                    using (new NameplateLanguageScope(catalog.entries.Where(entry => keys.Contains(entry.key)).ToArray()))
                    {
                        foreach (string key in keys)
                            Assert.That(LocalizationService.Get(key), Is.Not.Empty.And.Not.EqualTo(key));
                        string locked = LocalizationService.Get(textKey);
                        Assert.That(locked, Does.Contain(language == "ru" ? "цеп" : "chain"));
                        Assert.That(locked, Does.Contain(language == "ru" ? "ключ" : "key"));
                        yield return BeginReading();
                        using (GameTimeScaleRuntime.AcquirePause())
                        {
                            Vector3 pausedCamera = root.CameraFollow.Camera.transform.position;
                            for (int frame = 0; frame < 3; frame++) yield return null;
                            Assert.That(session.Phase, Is.EqualTo(NarrativeInteractionPhase.Reading));
                            Assert.That(session.Confirm(), Is.False);
                            Assert.That(root.CameraFollow.Camera.transform.position, Is.EqualTo(pausedCamera));
                        }
                        yield return null;
                        if (!Application.isBatchMode)
                        {
                            Assert.That(root.Prompt.LastRenderedText, Is.EqualTo(locked));
                            Assert.That(root.Prompt.LastRenderedTextFits, Is.True);
                            yield return CaptureFoundItemGameView("Test warehouse - Locked valve inspection - " +
                                (language == "ru" ? "Russian" : "English"));
                        }
                        yield return PressLodgeUse(input, keyboard);
                        yield return WaitForExit();
                        AssertClean();
                    }
                }
                yield return BeginReading();
                input.Press(keyboard.escapeKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.escapeKey, queueEventOnly: true);
                yield return WaitForExit();
                AssertClean();
                yield return BeginReading(cancelAttempt: true);
                yield return BeginReading(disableAttempt: true);

                IEnumerator BeginReading(bool cancelAttempt = false, bool disableAttempt = false)
                {
                    root.Hero.ResetActor(target.Staging.Entry.RootPosition, target.Staging.Entry.RootRotation * Vector3.forward);
                    root.Hero.SetCombatFocused(true);
                    root.Hero.ApplyMotorConstraint();
                    Physics.SyncTransforms();
                    root.CameraFollow.ClearFixedPose();
                    root.CameraFollow.RotateYaw(Mathf.DeltaAngle(root.CameraFollow.Camera.transform.eulerAngles.y,
                        target.Staging.Entry.RootRotation.eulerAngles.y));
                    root.CameraFollow.Snap();
                    for (int frame = 0; frame < 3; frame++) yield return null;
                    Assert.That(target.CanInteract(hero), Is.True, "The real player can reach the clear valve dock while the story clock is stopped.");
                    Assert.That(hero.ActiveInteractable, Is.SameAs(target));
                    yield return PressLodgeUse(input, keyboard);
                    bool seenAttempt = false, attemptPaused = false;
                    float maxWheel = 0f, maxChain = 0f, maxLock = 0f;
                    for (int frame = 0; frame < 200 && session.Phase != NarrativeInteractionPhase.Reading && session.IsActive; frame++)
                    {
                        if (session.Phase == NarrativeInteractionPhase.Attempting)
                        {
                            seenAttempt = true;
                            Assert.That(root.Prompt.HasHeldPage, Is.False, "The locked tug finishes before its information page.");
                            maxWheel = Mathf.Max(maxWheel, Quaternion.Angle(restRotations[0], movers[0].rotation));
                            maxChain = Mathf.Max(maxChain, Vector3.Distance(restCenters[1], movingMeshes[1].bounds.center));
                            maxLock = Mathf.Max(maxLock, Vector3.Distance(restCenters[2], movingMeshes[2].bounds.center));
                            if (!attemptPaused && maxWheel > 1f)
                            {
                                Vector3[] frozenPositions = movers.Select(part => part.position).ToArray();
                                Quaternion[] frozenRotations = movers.Select(part => part.rotation).ToArray();
                                using (GameTimeScaleRuntime.AcquirePause())
                                {
                                    for (int pausedFrame = 0; pausedFrame < 3; pausedFrame++) yield return null;
                                    Assert.That(session.Phase, Is.EqualTo(NarrativeInteractionPhase.Attempting));
                                    for (int part = 0; part < movers.Length; part++)
                                    {
                                        Assert.That(Vector3.Distance(movers[part].position, frozenPositions[part]), Is.LessThan(.00001f));
                                        Assert.That(Quaternion.Angle(movers[part].rotation, frozenRotations[part]), Is.LessThan(.001f));
                                    }
                                }
                                attemptPaused = true;
                            }
                            if (captureAttempt && maxWheel > 4f && !Application.isBatchMode)
                            {
                                captureAttempt = false;
                                yield return CaptureFoundItemGameView("Test warehouse - Locked valve tug");
                            }
                            if (disableAttempt && maxWheel > 1f && maxChain > .002f)
                            {
                                root.Hero.enabled = false;
                                target.enabled = false;
                                AssertClean();
                                root.Hero.enabled = true;
                                target.enabled = true;
                                yield break;
                            }
                            if (cancelAttempt && maxWheel > 1f && maxChain > .002f)
                            {
                                input.Press(keyboard.escapeKey, queueEventOnly: true);
                                yield return null;
                                input.Release(keyboard.escapeKey, queueEventOnly: true);
                                yield return WaitForExit();
                                AssertClean();
                                yield break;
                            }
                        }
                        yield return null;
                    }
                    Assert.That(seenAttempt && attemptPaused, Is.True);
                    Assert.That(maxWheel, Is.GreaterThan(4f).And.LessThan(10f), "The wheel visibly strains against its locked chain.");
                    Assert.That(maxChain, Is.GreaterThan(.002f).And.LessThan(.12f), "The attached yellow chain visibly tensions and rattles.");
                    Assert.That(maxLock, Is.GreaterThan(.002f).And.LessThan(.15f), "The neutral lock follows the locked chain without opening.");
                    Assert.That(session.Phase, Is.EqualTo(NarrativeInteractionPhase.Reading), session.LastFailureReason);
                    yield return null; yield return null;
                    Assert.That(session.CurrentPage.TextKey, Is.EqualTo(textKey));
                    Assert.That(root.Hero.CombatFocused, Is.False, "Inspection yields the settled combat pose to the shared hero rig.");
                    Assert.That(root.Prompt.HasHeldPage && root.Prompt.IsHeldBy(session), Is.True);
                    Assert.That(root.Prompt.IsSpeaking, Is.False, "The locked condition uses the silent inspection page.");
                    Assert.That(root.Prompt.SpokenBubbles != null && root.Prompt.SpokenBubbles.IsShowing(session), Is.False);
                    Assert.That(session.CameraDirector.CurrentShotIsClear, Is.True);
                    Assert.That(root.CameraFollow.FixedPoseActive, Is.True);
                    Assert.That(GameSessionState.IsGameTimeRunning, Is.False);
                    AssertRestPose();
                }

                void AssertRestPose()
                {
                    for (int part = 0; part < movers.Length; part++)
                    {
                        Assert.That(Vector3.Distance(movers[part].position, restPositions[part]), Is.LessThan(.00001f));
                        Assert.That(Quaternion.Angle(movers[part].rotation, restRotations[part]), Is.LessThan(.001f));
                    }
                }

                IEnumerator WaitForExit()
                {
                    for (int frame = 0; frame < 150 && session.IsActive; frame++) yield return null;
                    yield return null;
                }

                void AssertClean()
                {
                    Assert.That(session.IsActive, Is.False);
                    Assert.That(hero.InputEnabled, Is.True);
                    Assert.That(root.Hero.CombatFocused, Is.True, "Every dismissal restores the prior voluntary combat focus.");
                    Assert.That(root.CameraFollow.FixedPoseActive || root.Prompt.HasHeldPage, Is.False,
                        "Dismissal returns the shared camera and lower page to gameplay.");
                    Assert.That(new WarehouseArenaArea(root.WarehousePlan).CanTraverse(
                        root.WarehousePlan.GateApproach, root.WarehousePlan.GoalDoorPosition), Is.False);
                    Assert.That(GameSessionState.IsGameTimeRunning, Is.False);
                    AssertRestPose();
                }
            }
            finally
            {
                session.RestoreImmediate();
                root.Hero.enabled = true;
                target.enabled = true;
                root.Hero.ResetActor(heroPosition, heroFacing);
                input.TearDown();
            }
        }

        private static void AssertWarehouseDeterminism(WarehouseArenaPlan a, WarehouseArenaPlan b)
        {
            Assert.That(b.Template, Is.EqualTo(a.Template));
            CollectionAssert.AreEqual(a.Platforms, b.Platforms);
            CollectionAssert.AreEqual(a.SpawnPoints, b.SpawnPoints);
            CollectionAssert.AreEqual(a.NavigationNodes, b.NavigationNodes);
            CollectionAssert.AreEqual(a.RoomCenters, b.RoomCenters);
            CollectionAssert.AreEqual(a.Refrigerators, b.Refrigerators);
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

        private static void AssertWarehouseRoomFurnitureFacesEntry(WarehouseArenaPlan plan)
        {
            int rooms = 0;
            foreach (WarehouseArenaPlacement room in plan.ModulePlacements)
            {
                if (room.Kind != "RoomShell") continue;
                Quaternion rotation = Quaternion.Euler(0f,room.Yaw,0f);
                Vector3 expected = room.Position + rotation * new Vector3(-1.8f,0f,0f);
                int matches = 0;
                foreach (WarehouseArenaPlacement prop in plan.ModulePlacements)
                {
                    if (prop.Kind != "TechnicalProps" && prop.Kind != "LockerProps" && prop.Kind != "BreakProps") continue;
                    if (Vector3.Distance(prop.Position,expected) > .001f) continue;
                    matches++;
                    Vector3 front = Quaternion.Euler(0f,prop.Yaw,0f) * Vector3.back;
                    Assert.That(Vector3.Dot(front,rotation * Vector3.right), Is.GreaterThan(.999f),
                        "Each room's furniture front must face its entrance from the opposite wall.");
                    bool collision = false;
                    foreach (WarehouseArenaObstacle obstacle in plan.Obstacles)
                        if (obstacle.Kind == WarehouseArenaObstacleKind.Furniture &&
                            Vector3.Distance(obstacle.Position,prop.Position) < .001f)
                        {
                            collision = true;
                            Assert.That(obstacle.Yaw, Is.EqualTo(prop.Yaw));
                            Assert.That(obstacle.Bounds.size, Is.EqualTo(new Vector3(1.3f,1.9f,2.2f)),
                                "The authoritative furniture footprint must rotate with the visible module.");
                        }
                    Assert.That(collision, Is.True);
                }
                Assert.That(matches, Is.EqualTo(1), "Each room has one furnishing module against its opposite wall.");
                rooms++;
            }
            Assert.That(rooms, Is.EqualTo(plan.RoomCenters.Count));
        }

        private static void AssertWarehouseImportedMetres(CombatTestRoot root)
        {
            Assert.That(WarehouseArenaAssetProvider.ModelNames.Length, Is.EqualTo(42));
            foreach (string kind in WarehouseArenaAssetProvider.ModelNames)
            {
                Assert.That(Resources.Load<GameObject>(WarehouseArenaAssetProvider.ResourceFolder + kind), Is.Not.Null,
                    "The accepted passive model bank must include " + kind + ".");
                GameObject wrapper = null;
                foreach (Transform child in root.GetComponentsInChildren<Transform>())
                    if (child.name == "Warehouse " + kind) { wrapper = child.gameObject; break; }
                if (kind == "PipeStraight" || kind == "PipeElbow" || kind == "PipeEnd" || kind == "TwistedFrame") continue;
                Assert.That(wrapper, Is.Not.Null, "The real built warehouse must contain " + kind + ".");
                if (kind == "GateControl" || kind == "FishPreparationStation" || kind == "FishStationLamp")
                {
                    // These owners attach independent authored chain/key/fish/shard
                    // assets after Create. Only the source module defines its bounds.
                    AssertWarehouseSourceMetres(kind, wrapper);
                    continue;
                }
                // Its metre vertices are measured below; a rotating wrapper's
                // world AABB cannot be inverse-rotated into its authored bounds.
                if (kind != "FanRotor" && kind != "LiftPlatform" && kind != "ChainDrum" && kind != "Lamp" &&
                    kind != "PusherHead" && kind != "Rail")
                    WarehouseArenaAssetProvider.ValidatePlacedModel(kind, wrapper);
            }
        }

        private static void AssertWarehouseSourceMetres(string kind, GameObject wrapper)
        {
            GameObject source = Resources.Load<GameObject>(WarehouseArenaAssetProvider.ResourceFolder + kind);
            var sourceMeshes = new Dictionary<Mesh,int>();
            Bounds expected = default, actual = default;
            bool hasExpected = false, hasActual = false;
            foreach (MeshRenderer renderer in source.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.name.StartsWith("Collision_",System.StringComparison.Ordinal)) continue;
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                sourceMeshes.TryGetValue(mesh, out int count);
                sourceMeshes[mesh] = count + 1;
                foreach (Vector3 vertex in mesh.vertices)
                {
                    // Keep the imported root's centimetre-to-metre factor.
                    Vector3 point = renderer.transform.TransformPoint(vertex);
                    if (!hasExpected) { expected = new Bounds(point,Vector3.zero); hasExpected = true; }
                    else expected.Encapsulate(point);
                }
            }
            var placedMeshes = new Dictionary<Mesh,int>();
            foreach (MeshRenderer renderer in wrapper.GetComponentsInChildren<MeshRenderer>(true))
            {
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                if (!sourceMeshes.ContainsKey(mesh)) continue;
                placedMeshes.TryGetValue(mesh, out int count);
                placedMeshes[mesh] = count + 1;
                foreach (Vector3 vertex in mesh.vertices)
                {
                    // Source meshes can be reparented into interaction pivots;
                    // shared-mesh identity follows that boundary without admitting
                    // separately imported owner attachments or their hidden parts.
                    Vector3 point = wrapper.transform.InverseTransformPoint(renderer.transform.TransformPoint(vertex));
                    if (!hasActual) { actual = new Bounds(point,Vector3.zero); hasActual = true; }
                    else actual.Encapsulate(point);
                }
            }
            Assert.That(hasExpected && hasActual, Is.True, kind);
            foreach (KeyValuePair<Mesh,int> pair in sourceMeshes)
            {
                Assert.That(placedMeshes.TryGetValue(pair.Key, out int count), Is.True, kind + "/" + pair.Key.name);
                Assert.That(count, Is.EqualTo(pair.Value), "The placed module retains every original source mesh exactly once.");
            }
            Assert.That(Vector3.Distance(actual.min,expected.min), Is.LessThan(.03f),
                kind + " source minimum must retain authored metres after runtime composition.");
            Assert.That(Vector3.Distance(actual.max,expected.max), Is.LessThan(.03f),
                kind + " source maximum must retain authored metres after runtime composition.");
        }

        private static void AssertWarehouseSurfaceMaterials(CombatTestRoot root)
        {
            AssertWarehouseSurfaceSlots(root);
            var reused = new Dictionary<string,Material>();
            var families = new HashSet<string>();
            int overlays = 0, structural = 0;
            foreach (Transform wrapper in root.GetComponentsInChildren<Transform>(true))
            {
                if (!wrapper.name.StartsWith("Warehouse ",System.StringComparison.Ordinal)) continue;
                string kind = wrapper.name.Substring("Warehouse ".Length);
                if (kind == "GatePlatformLeft" || kind == "GatePlatformRight")
                {
                    foreach (MeshRenderer deck in wrapper.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        if (!deck.enabled || deck.name.StartsWith("Collision_", System.StringComparison.Ordinal)) continue;
                        Assert.That(deck.sharedMaterial.GetFloat("_WarehouseAuxiliary"), Is.Zero,
                            "Cut gate decks must retain the adjoining Platform painting, rather than an auxiliary patch.");
                        Assert.That(deck.sharedMaterial.GetTexture("_BaseMap").name, Is.EqualTo("WarehouseMasterAtlas"));
                    }
                }
                if (System.Array.IndexOf(WarehouseArenaAssetProvider.ModelNames,kind) < 0) continue;
                string family = WarehouseArenaAssetProvider.AtlasForModel(kind);
                foreach (MeshRenderer renderer in wrapper.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                        renderer.name.StartsWith("Collision_",System.StringComparison.Ordinal)) continue;
                    Material material = renderer.sharedMaterial;
                    Assert.That(material, Is.Not.Null);
                    Assert.That(material.shader.isSupported, Is.True);
                    Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    Assert.That(mesh.uv.Length, Is.EqualTo(mesh.vertexCount));
                    Assert.That(mesh.uv2.Length, Is.EqualTo(mesh.vertexCount));
                    if (reused.TryGetValue(material.name,out Material first))
                        Assert.That(material, Is.SameAs(first), "Instances must reuse their atlas-family and role material.");
                    else reused.Add(material.name,material);
                    string role = renderer.name.Substring(renderer.name.LastIndexOf("__",System.StringComparison.Ordinal) + 2);
                    bool decal = renderer.name.EndsWith("__WearDecal",System.StringComparison.Ordinal);
                    if (decal)
                    {
                        overlays++;
                        Assert.That(material.GetTexture("_BaseMap").name, Is.EqualTo("WarehouseWearDecals"));
                        Assert.That(material.IsKeywordEnabled("_ALPHATEST_ON"), Is.True);
                        Assert.That(renderer.shadowCastingMode, Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.Off));
                        Assert.That(renderer.GetComponent<Collider>(), Is.Null);
                        continue;
                    }
                    if (renderer.name.EndsWith("__FishLabelPaper", System.StringComparison.Ordinal))
                    {
                        Assert.That(material.GetTexture("_BaseMap").name, Is.EqualTo("WarehouseFishLabels"));
                        Assert.That(material.GetColor("_BaseColor"), Is.EqualTo(Color.white));
                        continue;
                    }
                    if (family != null)
                    {
                        families.Add(family);
                        Texture master = material.GetTexture("_BaseMap");
                        Assert.That(master.name, Is.EqualTo(family));
                        Assert.That(master.width, Is.EqualTo(4096));
                        Assert.That(master.height, Is.EqualTo(4096));
                        Assert.That(material.GetColor("_BaseColor"), Is.EqualTo(Color.white),
                            "Baked role tint must be applied once, rather than darkened again at runtime.");
                        foreach (Vector2 uv in mesh.uv)
                        {
                            Assert.That(uv.x, Is.InRange(-.0001f,1.0001f));
                            Assert.That(uv.y, Is.InRange(-.0001f,1.0001f));
                        }
                    }
                    bool layered = role != "Lamp" && role != "Glass" && role != "NumberPlate" &&
                        role != "SignalRed" && role != "SignalRedOff" && role != "FishLabelInk";
                    if (layered)
                    {
                        if (kind == "Shell" || kind == "RoomShell" || kind == "Platform" || kind == "Stair" || kind == "Rail") structural++;
                        Assert.That(material.shader.name, Is.EqualTo("Bar Promenade/Warehouse Surface"));
                        Assert.That(material.HasProperty("_WarehouseMacroMask"), Is.False);
                        Assert.That(material.HasProperty("_WarehouseDetailMap"), Is.False);
                        Assert.That(material.HasProperty("_WarehouseResponseRect"), Is.False,
                            "Roughness and relief must describe the same unique UV0 wear, without repeated material cells.");
                        Assert.That(material.GetTexture("_BaseMap").name, Is.EqualTo("WarehouseMasterAtlas"));
                        Assert.That(material.GetFloat("_WarehouseAuxiliary"), Is.EqualTo(family == null ? 1f : 0f));
                        bool physical = role == "Concrete" || role == "ConcreteDark" || role == "Steel" ||
                            role == "WornSteel" || role == "LockSteel" || role == "SafetyYellow" || role == "Rust" ||
                            role == "Timber" || role == "Cardboard" || role == "Rubber";
                        if (physical)
                        {
                            Texture response = material.GetTexture("_WarehouseMaterialResponse");
                            Assert.That(response, Is.Not.Null);
                            Assert.That(response.name, Is.EqualTo("WarehouseMaterialResponse"));
                            Assert.That(material.GetFloat("_WarehouseResponseStrength"), Is.GreaterThan(0f));
                            Assert.That(material.GetFloat("_WarehouseNormalStrength"), Is.GreaterThan(0f));
                            Assert.That(response.width, Is.EqualTo(2048));
                            Assert.That(response.height, Is.EqualTo(2048));
                        }
                    }
                    else Assert.That(material.shader.name, Is.EqualTo("Bar Promenade/PS1 Lit"));
                }
            }
            Assert.That(families.Count, Is.EqualTo(1));
            Assert.That(structural, Is.GreaterThan(0));
            Assert.That(overlays, Is.EqualTo(1 + root.WarehousePlan.RoomCenters.Count),
                "Only authored Shell and RoomShell overlay meshes add wear draw calls.");
        }

        private static void AssertWarehouseLightAndMechanisms(CombatTestRoot root)
        {
            Assert.That(root.WarehousePlan.Lamps.Count, Is.EqualTo(15));
            WarehouseArenaMechanisms machines = root.GetComponentInChildren<WarehouseArenaMechanisms>();
            Assert.That(machines, Is.Not.Null);
            int practical = 0, active = 0;
            WarehouseFishLamp fishLamp = root.GetComponentInChildren<WarehouseFishLamp>();
            int poweredFishLamp = fishLamp != null && fishLamp.Light.isActiveAndEnabled && fishLamp.Light.intensity > 0f ? 1 : 0;
            int poweredFan = machines.FanLight.isActiveAndEnabled && machines.FanLight.intensity > 0f ? 1 : 0;
            foreach (Light light in Object.FindObjectsByType<Light>())
            {
                if (!light.isActiveAndEnabled || light.intensity <= 0f) continue;
                active++;
                if (light.name == "Warehouse Room Indicator Light")
                {
                    AssertWarehouseIndicatorShadowProfile(light);
                    continue;
                }
                if (light.gameObject.name != "Warehouse Work Lamp")
                {
                    Assert.That(light == root.Flashlight.SpotLight || light == machines.FanLight, Is.True);
                    continue;
                }
                practical++;
                bool fishPendant = fishLamp != null && light == fishLamp.Light;
                Assert.That(light.type, Is.EqualTo(fishPendant ? LightType.Point : LightType.Spot));
                Assert.That(light.shadows, Is.EqualTo(LightShadows.Hard));
                if (fishPendant)
                {
                    Assert.That(light.range, Is.LessThanOrEqualTo(4.5f));
                    Assert.That(light.intensity, Is.LessThanOrEqualTo(18f));
                }
                else if (light.color.r < light.color.g * 3f)
                    Assert.That(light.intensity, Is.LessThan(CombatChestFlashlight.Intensity * .12f));
                Assert.That(light.GetComponent<UniversalAdditionalLightData>().additionalLightsShadowResolutionTier,
                    Is.EqualTo(UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow));
            }
            Assert.That(practical, Is.EqualTo(5 + poweredFishLamp));
            Assert.That(active, Is.EqualTo(6 + ActiveWarehouseRoomIndicatorCount(root) + poweredFishLamp + poweredFan),
                "Five steady practicals, conditional pendant/fan/room indicators and the chest light.");
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
            if (machines.FanSpeedFactor == 0f)
                Assert.That(Quaternion.Angle(before, machines.FanRotor.localRotation), Is.LessThan(.001f));
            else if (!machines.FanShutdownStarted)
                Assert.That(Quaternion.Angle(before, machines.FanRotor.localRotation), Is.GreaterThan(8f));
        }

        private static int ActiveWarehouseRoomIndicatorCount(CombatTestRoot root)
        {
            int count = 0;
            foreach (Light light in root.GetComponentsInChildren<Light>())
                if (light.name == "Warehouse Room Indicator Light" && light.isActiveAndEnabled && light.intensity > 0f) count++;
            return count;
        }

        private static void AssertWarehousePhysicalSurfaces(CombatTestRoot root)
        {
            foreach (WarehouseArenaStair stair in root.WarehousePlan.Stairs)
                for (int step = 0; step < WarehouseArenaStair.TreadCount; step++)
                {
                    float height = stair.Foot.y + (step + 1) * WarehouseArenaStair.TreadRise;
                    // The broken lower remnant has a transverse guard at the
                    // last tread's centre. Probe behind that real barrier.
                    // Probe the tread before the remnant guard's fastener
                    // shoe; that visible shoe legitimately sits above it.
                    Vector3 tread = stair.Foot + Vector3.forward * ((step + (stair.IsBroken ? .1f : .5f)) * WarehouseArenaStair.TreadDepth);
                    tread.y = height;
                    bool supported = Physics.Raycast(tread + Vector3.up * .35f, Vector3.down, out RaycastHit hit,
                        .65f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    if (!stair.HasTread(step))
                    {
                        Assert.That(supported, Is.False, "The missing upper stair section cannot retain an invisible physical ramp.");
                        continue;
                    }
                    Assert.That(supported, Is.True);
                    bool bentEdge = stair.IsBroken && (step == WarehouseArenaStair.FirstMissingTread - 1 ||
                        step == WarehouseArenaStair.MissingTreadEnd);
                    if (bentEdge)
                        Assert.That(hit.point.y, Is.InRange(height - .14f, height + .025f),
                            "The exposed edge piece retains its authored bent collision below the original tread height.");
                    else
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

        private static IEnumerator CaptureWarehouseEnvironment(CombatTestRoot root, string lightingState = null,
            bool includeUvChecker = true, bool closeViewsOnly = false)
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
            string caption = "Test warehouse - " + template + " - " +
                (string.IsNullOrEmpty(lightingState) ? "" : lightingState + " - ");
            WarehouseArenaStair stair = plan.Stairs[0], topStair = plan.Stairs[2];
            Vector3 room = plan.RoomCenters[0], corridorRoom = plan.RoomCenters[3];
            Vector3 fan = plan.Mechanisms[0].Position, lift = plan.Mechanisms[1].Position,
                drum = plan.Mechanisms[2].Position, pusher = plan.Mechanisms[3].Position;
            WarehouseArenaPlacement locker = default;
            WarehouseArenaPlacement technical = default;
            bool hasLocker = false, hasTechnical = false;
            foreach (WarehouseArenaPlacement placement in plan.ModulePlacements)
            {
                if (!hasLocker && placement.Kind == "LockerProps") { locker = placement; hasLocker = true; }
                if (!hasTechnical && placement.Kind == "TechnicalProps") { technical = placement; hasTechnical = true; }
            }
            Assert.That(hasLocker, Is.True);
            Assert.That(hasTechnical, Is.True);
            Quaternion lockerRotation = Quaternion.Euler(0f,locker.Yaw,0f);
            Quaternion technicalRotation = Quaternion.Euler(0f,technical.Yaw,0f);
            Vector3 rearGallery = new Vector3(0f, WarehouseArenaPlan.LevelRise, plan.GatePosition.z - 3.6f);
            var shots = new[]
            {
                // The entry is beneath the front gallery. Step into the actual
                // central void before photographing its ceiling-mounted rotor.
                Shot.At(caption + "Main hall and ceiling fan", plan.HeroSpawn + new Vector3(0f, 1.7f, 4.7f), fan, 72f),
                Shot.At(caption + "Top gallery", topStair.Top + new Vector3(0f, 1.7f, 1f), fan, 68f),
                Shot.At(caption + "Physical stair", stair.Foot + new Vector3(-2.5f, 1.7f, -1.4f),
                    stair.Foot + new Vector3(0f, 1.8f, 3f), 62f),
                Shot.At(caption + "Small technical room", room + Vector3.up * 1.7f,
                    technical.Position + technicalRotation * new Vector3(0f, 1f, -.3f), 68f),
                Shot.At(caption + "Locker corridor", corridorRoom + new Vector3(4.5f, 1.7f, .7f),
                    corridorRoom + Vector3.up * 1.4f, 64f),
                Shot.At(caption + "Barred main door", plan.GateApproach + new Vector3(0f, 1.7f, -1f),
                    plan.GatePosition + Vector3.up * 1.35f, 68f),
                Shot.At(caption + "Empty freight lift", lift + new Vector3(3f, 1.7f, -3f),
                    lift + Vector3.up * 4.2f, 72f),
                Shot.At(caption + "Chain drum", drum + new Vector3(0f, 1.7f, -3f),
                    drum + Vector3.up * 1.1f, 65f),
                Shot.At(caption + "Endless load pusher", pusher + new Vector3(2.7f, 1.7f, -2.7f),
                    pusher + Vector3.up, 68f),
                Shot.At(caption + "Door wear close view", plan.GateApproach + new Vector3(0f, 1.65f, .2f),
                    plan.GoalDoorPosition + new Vector3(-.6f, 1.4f, 0f), 48f),
                // The doors face the module's -Z, independent of room yaw.
                Shot.At(caption + "Locker wear close view", locker.Position + lockerRotation * new Vector3(.5f, 1.6f, -1.4f),
                    locker.Position + lockerRotation * new Vector3(0f, 1.05f, -.3f), 68f),
                Shot.At(caption + "Drum wear close view", drum + new Vector3(.55f, 1.7f, -2f),
                    drum + Vector3.up * 1.1f, 44f),
                Shot.At(caption + "Concrete and floor wear", room + new Vector3(1.1f, 1.7f, 1.1f),
                    room + new Vector3(-1f, .3f, 2.5f), 54f),
                // Keep several adjoining deck modules in the real chest beam,
                // with the hero on the landing beyond the stair opening.
                Shot.At(caption + "Gallery floor joins", stair.Top + new Vector3(0f, 1.7f, 1.05f),
                    stair.Top + new Vector3(1.3f, 0f, 4.2f), 62f),
                Shot.At(caption + "Gate floor joins", rearGallery + new Vector3(-2.4f, 1.7f, -.8f),
                    rearGallery + new Vector3(.8f, 0f, 1.9f), 62f)
            };
            try
            {
                root.CameraFollow.enabled = false;
                var area = new WarehouseArenaArea(plan);
                var visual = (Player3DCharacterPresentation)root.Player.Visual;
                foreach (Shot shot in shots)
                {
                    // The real blackout has no distant fill. Photograph nearby
                    // surfaces inside the chest beam, rather than an unlit ceiling.
                    if (closeViewsOnly && !shot.Name.Contains("wear close view") &&
                        !shot.Name.Contains("Concrete and floor wear") && !shot.Name.Contains("floor joins")) continue;
                    Vector3 facing = shot.Target - shot.Position; facing.y = 0f; facing.Normalize();
                    Vector3 foot = shot.Position - Vector3.up * 1.7f - facing * .65f;
                    // The imported doors face the clear aisle toward the
                    // room entrance; keep the real chest source there too.
                    if (shot.Name.Contains("Locker wear close view"))
                        foot = locker.Position + lockerRotation * new Vector3(0f,0f,-1.5f);
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
                    if (includeUvChecker && plan.Seed == 0 && shot.Name.Contains("wear close view"))
                        yield return CaptureWarehouseUvChecker(root,camera,shot.Name + " - Test UV checker");
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

        private static IEnumerator CaptureWarehouseUvChecker(CombatTestRoot root, Camera camera, string name)
        {
            const int size = 512, square = 16;
            var checker = new Texture2D(size,size,TextureFormat.RGB24,true)
                { name = "Test warehouse UV checker", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    byte value = ((x / square + y / square) & 1) == 0 ? (byte)210 : (byte)65;
                    pixels[y * size + x] = new Color32(value,value,value,255);
                }
            checker.SetPixels32(pixels); checker.Apply(true,false);
            var material = new Material(Resources.Load<Shader>("Shaders/Ps1Lit"))
                { name = "Test warehouse UV checker material" };
            material.SetTexture("_BaseMap",checker);
            material.SetColor("_BaseColor",Color.white);
            material.SetFloat("_Smoothness",.02f);
            var originals = new Dictionary<Renderer,Material[]>();
            try
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!renderer.enabled) continue;
                    Material[] current = renderer.sharedMaterials;
                    Material[] replacement = null;
                    for (int index = 0; index < current.Length; index++)
                    {
                        Material surface = current[index];
                        if (surface == null || !surface.HasProperty("_BaseMap")) continue;
                        Texture texture = surface.GetTexture("_BaseMap");
                        if (texture == null || System.Array.IndexOf(WarehouseArenaAssetProvider.AtlasTextureNames,texture.name) < 0) continue;
                        replacement ??= (Material[])current.Clone();
                        replacement[index] = material;
                    }
                    if (replacement == null) continue;
                    originals.Add(renderer,current); renderer.sharedMaterials = replacement;
                }
                // Let renderer bindings complete a normal frame before this
                // diagnostic's additional Camera.Render.
                yield return null;
                bool drumBody = false;
                foreach (Renderer renderer in originals.Keys)
                    if (renderer.name.StartsWith("DrumRotor__Rust",System.StringComparison.Ordinal))
                    {
                        drumBody = true;
                        Assert.That(renderer.sharedMaterial, Is.SameAs(material));
                        Assert.That(renderer.sharedMaterial.GetTexture("_BaseMap"), Is.SameAs(checker));
                    }
                Assert.That(drumBody, Is.True, "The real rotating drum body must receive the UV checker too.");
                CaptureCurrentCamera(camera,SceneIds.CombatTest,name);
            }
            finally
            {
                foreach (KeyValuePair<Renderer,Material[]> item in originals)
                    if (item.Key != null) item.Key.sharedMaterials = item.Value;
                Object.Destroy(material); Object.Destroy(checker);
            }
        }
    }
}
