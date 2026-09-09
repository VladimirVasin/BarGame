using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class ScarfAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type setup = Type.GetType(
                "BarPromenade.Editor.PlayerScarfAssetSetup, BarPromenade.Editor", true);
            setup.GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [Serializable]
        private sealed class ScarfJourneyReport
        {
            public Vector3 pickup_position;
            public int pinned_vertex_samples;
            public int free_vertex_samples;
            public float maximum_pinned_error_metres;
            public float maximum_knot_seam_gap_metres;
            public float maximum_free_deformation_metres;
            public float maximum_frame_motion_metres;
            public float pause_drift_metres;
            public float maximum_mouth_contact_error_metres;
            public bool survives_scene_load;
            public bool return_has_no_duplicate;
            public bool unequip_preserves_quantity;
            public bool new_game_resets;
            public bool mirror_and_visibility_verified;
        }

        [UnityTest]
        [Timeout(180000)]
        [Explicit("One scarf pickup/equipment/cloth journey with production scene captures. Run alone.")]
        [PrebuildSetup(typeof(ScarfAssetsSetup))]
        public IEnumerator ScarfJourney()
        {
            var report = new ScarfJourneyReport();
            float previousDelta = Time.captureDeltaTime;
            MothersHouseInteriorRoot house = null;
            AlpineVillageRoot village = null;
            Camera camera = null;
            var liveTarget = new RenderTexture(640, 360, 24);
            var scratch = new Mesh { name = "Scarf Journey Live Cloth Probe" };
            IDisposable pause = null;
            PlayerScarfController.MouthAccess mouth = null;
            try
            {
                AssertScarfColdProtection();
                GameSessionState.BeginNewGame();
                Assert.That(GameSessionState.TryStartGameTimeFromWake(), Is.True);
                Time.captureDeltaTime = 1f / 60f;
                liveTarget.Create();
                yield return LoadScarfScene<MothersHouseInteriorRoot>(
                    SceneIds.MothersHouseInterior, value => value.IsInitialized,
                    value => house = value);
                camera = Camera.main;
                Assert.That(camera, Is.Not.Null);
                camera.targetTexture = liveTarget;
                house.CameraFollow.enabled = false;
                house.FixedCamera.enabled = false;
                house.Player.Motor.enabled = false;
                var pickup = house.ScarfPickup;
                Assert.That(pickup, Is.Not.Null);
                report.pickup_position = pickup.Plan.Position;
                Assert.That(house.Layout.UpperFloor.NorthRoomBounds.Contains(
                    new Vector2(pickup.Plan.Position.x, pickup.Plan.Position.z)), Is.True);
                Assert.That(pickup.Plan.Position.y,
                    Is.EqualTo(house.Layout.UpperFloor.FloorElevation +
                        MothersHouseInteriorLayoutPlanner.UpperCorridorChestHeight).Within(.001f));
                Assert.That(GameSessionState.HasInventoryItem(InventoryItemId.Scarf), Is.False);
                Vector3 standing = pickup.transform.position + Vector3.right * 1.1f;
                standing.y = house.Layout.UpperFloor.FloorElevation + PlayerFactory.GroundedRootOffset;
                house.Player.Motor.Teleport(standing);
                house.Player.GameObject.transform.rotation = Quaternion.LookRotation(Vector3.left);
                Physics.SyncTransforms();
                AimScarfCamera(camera, pickup.transform.position + new Vector3(1.1f, .75f, -1f),
                    pickup.transform.position + Vector3.up * .04f, 48f);
                yield return ScarfFrames(12);
                yield return CaptureScarfFrame(camera, "00-bedroom-pickup");
                Assert.That(pickup.CanInteract(house.Player.Interactor), Is.True);
                pickup.Interact(house.Player.Interactor);
                pickup.Interact(house.Player.Interactor);
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.Scarf), Is.EqualTo(1));
                Assert.That(pickup.gameObject.activeSelf, Is.False);
                Assert.That(GameSessionState.IsWorldItemCollected(MothersHouseScarfPickupPlan.SourceId), Is.True);

                InventoryController inventory = house.Inventory;
                Assert.That(inventory.Open(), Is.True);
                Assert.That(inventory.SelectItem(ScarfInventoryIndex()), Is.True);
                Assert.That(inventory.SelectedEquipmentStatusLabel,
                    Is.EqualTo(LocalizationService.Get("inventory.equipment.unused")));
                Assert.That(inventory.SelectedUseActionLabel,
                    Is.EqualTo(LocalizationService.Get("inventory.action.equip")));
                Assert.That(inventory.UseSelected(), Is.True);
                Assert.That(inventory.SelectedEquipmentStatusLabel,
                    Is.EqualTo(LocalizationService.Get("inventory.equipment.used")));
                Assert.That(inventory.SelectedUseActionLabel,
                    Is.EqualTo(LocalizationService.Get("inventory.action.unequip")));
                Assert.That(inventory.IsOpen, Is.True);
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.Scarf), Is.EqualTo(1));
                Assert.That(inventory.Close(), Is.True);
                var controller = house.Player.GameObject.GetComponent<PlayerScarfController>();
                Assert.That(controller, Is.Not.Null);
                Assert.That(controller.Presentation.IsEquipped, Is.True);
                yield return ScarfFrames(12);
                Assert.That(controller.Presentation.TailSimulation.ExternalAcceleration, Is.EqualTo(Vector3.zero));
                foreach (var shot in new[]
                {
                    ("01-equipped-front", new Vector3(0f, 1.52f, 1.3f)),
                    ("02-equipped-side", new Vector3(.95f, 1.52f, .05f)),
                    ("03-equipped-back", new Vector3(0f, 1.52f, -1.25f))
                })
                {
                    AimScarfHero(camera, house.Player, shot.Item2);
                    yield return ScarfFrames(2);
                    yield return CaptureScarfFrame(camera, shot.Item1);
                }

                mouth = PlayerScarfController.RequireMouthAccess(house.Player, house);
                int gestureFrames = Mathf.CeilToInt(PlayerScarfController.GestureSeconds * 60f) + 5;
                AimScarfHero(camera, house.Player, new Vector3(.65f, 1.5f, 1.2f));
                for (int frame = 0; frame < gestureFrames; frame++)
                {
                    yield return null;
                    if (controller.IsGestureActive &&
                        controller.GestureProgress > .3f && controller.GestureProgress < .7f)
                    {
                        report.maximum_mouth_contact_error_metres = Mathf.Max(
                            report.maximum_mouth_contact_error_metres, controller.HandContactError);
                    }
                    if (frame == gestureFrames / 2)
                        yield return CaptureScarfFrame(camera, "04-lower-with-hand");
                }
                Assert.That(mouth.IsReady, Is.True);
                Assert.That(controller.Presentation.MouthLowered, Is.GreaterThan(.99f));
                Assert.That(report.maximum_mouth_contact_error_metres, Is.LessThan(.025f));
                Assert.That(GameSessionState.IsInventoryItemEquipped(InventoryItemId.Scarf), Is.True);
                yield return CaptureScarfFrame(camera, "05-mouth-access");
                mouth.Dispose();
                mouth = null;
                yield return ScarfFrames(gestureFrames);
                Assert.That(controller.Presentation.MouthLowered, Is.LessThan(.01f));

                yield return LoadScarfScene<AlpineVillageRoot>(SceneIds.AlpineVillage,
                    value => value.IsInitialized, value => village = value);
                house = null;
                camera = Camera.main;
                Assert.That(camera, Is.Not.Null);
                camera.targetTexture = liveTarget;
                village.CameraFollow.enabled = false;
                village.Player.Motor.enabled = false;
                AlpineVillageLaneSample lane = village.Plan.Lane.Sample(10f);
                village.Player.Motor.Teleport(lane.Position + Vector3.up * PlayerFactory.GroundedRootOffset);
                village.Player.GameObject.transform.rotation = Quaternion.LookRotation(lane.Forward);
                controller = village.Player.GameObject.GetComponent<PlayerScarfController>();
                Assert.That(controller, Is.Not.Null);
                report.survives_scene_load = controller.Presentation.IsEquipped;
                Assert.That(report.survives_scene_load, Is.True);
                AimScarfHero(camera, village.Player, new Vector3(.9f, 1.54f, -1.3f));
                yield return ScarfFrames(30);
                var tail = controller.Presentation.TailRenderer;
                Assert.That(controller.Presentation.TailSimulation.IsActive, Is.True);
                Assert.That(controller.Presentation.TailSimulation.ExternalAcceleration.magnitude, Is.GreaterThan(.01f));
                Vector3[] previous = null;
                for (int frame = 0; frame < 12; frame++)
                {
                    yield return ScarfFrames(6);
                    yield return ScarfRenderedFrame(camera, () =>
                    {
                        tail.BakeMesh(scratch);
                        Vector3[] vertices = scratch.vertices;
                        MeasureScarfCloth(tail, controller.Presentation.TailRestVertices, vertices, previous, report);
                        report.maximum_knot_seam_gap_metres = Mathf.Max(
                            report.maximum_knot_seam_gap_metres,
                            MeasureScarfSeam(controller.Presentation, vertices));
                        previous = vertices;
                        SaveScarfRenderedFrame(camera, "wind-" + frame.ToString("000"));
                    });
                }
                Assert.That(report.pinned_vertex_samples, Is.GreaterThan(0));
                Assert.That(report.free_vertex_samples, Is.GreaterThan(0));
                Debug.Log("Scarf cloth measurements " + JsonUtility.ToJson(report));
                Assert.That(report.maximum_pinned_error_metres, Is.LessThan(.015f));
                Assert.That(report.maximum_knot_seam_gap_metres, Is.LessThan(.05f),
                    "The live pinned edge must stay beside the independently skinned nape knot.");
                Assert.That(report.maximum_free_deformation_metres, Is.InRange(.005f, .28f));
                Assert.That(report.maximum_frame_motion_metres, Is.GreaterThan(.0001f));

                pause = GameTimeScaleRuntime.AcquirePause();
                yield return ScarfFrames(2);
                tail.BakeMesh(scratch);
                Vector3[] paused = scratch.vertices;
                yield return ScarfFrames(4);
                tail.BakeMesh(scratch);
                Vector3[] afterPause = scratch.vertices;
                for (int index = 0; index < paused.Length; index++)
                    report.pause_drift_metres = Mathf.Max(report.pause_drift_metres,
                        Vector3.Distance(paused[index], afterPause[index]));
                Assert.That(report.pause_drift_metres, Is.LessThan(.001f));
                pause.Dispose();
                pause = null;

                yield return LoadScarfScene<MothersHouseInteriorRoot>(SceneIds.MothersHouseInterior,
                    value => value.IsInitialized, value => house = value);
                village = null;
                camera = Camera.main;
                camera.targetTexture = liveTarget;
                report.return_has_no_duplicate = house.ScarfPickup == null &&
                    GameSessionState.GetInventoryItemCount(InventoryItemId.Scarf) == 1;
                Assert.That(report.return_has_no_duplicate, Is.True);
                controller = house.Player.GameObject.GetComponent<PlayerScarfController>();
                Assert.That(controller.Presentation.IsEquipped, Is.True);
                yield return ScarfFrames(2);
                Assert.That(controller.Presentation.TailSimulation.ExternalAcceleration, Is.EqualTo(Vector3.zero));
                Assert.That(house.Inventory.Open(), Is.True);
                Assert.That(house.Inventory.SelectItem(ScarfInventoryIndex()), Is.True);
                Assert.That(house.Inventory.UseSelected(), Is.True);
                Assert.That(controller.Presentation.IsEquipped, Is.False);
                report.unequip_preserves_quantity =
                    GameSessionState.GetInventoryItemCount(InventoryItemId.Scarf) == 1;
                Assert.That(report.unequip_preserves_quantity, Is.True);
                Assert.That(house.Inventory.Close(), Is.True);
                yield return VerifyScarfMirror(liveTarget);
                house = null;
                camera = Camera.main;
                report.mirror_and_visibility_verified = true;
                GameSessionState.BeginNewGame();
                report.new_game_resets = !GameSessionState.HasInventoryItem(InventoryItemId.Scarf) &&
                    !GameSessionState.IsInventoryItemEquipped(InventoryItemId.Scarf) &&
                    !GameSessionState.IsWorldItemCollected(MothersHouseScarfPickupPlan.SourceId);
                Assert.That(report.new_game_resets, Is.True);
                string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "Scarf");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "scarf-journey.json"), JsonUtility.ToJson(report, true));
            }
            finally
            {
                mouth?.Dispose();
                pause?.Dispose();
                if (house != null)
                {
                    house.Inventory.Close();
                    house.Player.Motor.enabled = true;
                    house.CameraFollow.enabled = true;
                    house.FixedCamera.enabled = true;
                }
                if (village != null)
                {
                    village.Player.Motor.enabled = true;
                    village.CameraFollow.enabled = true;
                }
                if (camera != null) camera.targetTexture = null;
                Time.captureDeltaTime = previousDelta;
                liveTarget.Release();
                Object.DestroyImmediate(liveTarget);
                Object.DestroyImmediate(scratch);
                GameSessionState.BeginNewGame();
            }
        }

        private static IEnumerator LoadScarfScene<T>(string sceneId, Func<T, bool> initialized,
            Action<T> ready) where T : MonoBehaviour
        {
            Debug.Log("Scarf journey loading " + sceneId);
            AsyncOperation load = SceneManager.LoadSceneAsync(sceneId, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            float deadline = Time.realtimeSinceStartup + 30f;
            T root = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                root = Object.FindAnyObjectByType<T>();
                if (load.isDone && root != null && initialized(root)) break;
                yield return null;
            }
            Assert.That(root != null && initialized(root), Is.True, sceneId + " did not initialize.");
            ready(root);
            yield return ScarfFrames(2);
        }

        private static IEnumerator VerifyScarfMirror(RenderTexture target)
        {
            Assert.That(GameSessionState.TrySetInventoryItemEquipped(InventoryItemId.Scarf, true), Is.True);
            HomeInteriorRoot home = null;
            yield return LoadScarfScene<HomeInteriorRoot>(SceneIds.HomeInterior,
                value => value.IsInitialized, value => home = value);
            home.Player.Motor.Teleport(new Vector3(2.075f, .12f, 2.78f));
            Camera camera = Camera.main;
            camera.targetTexture = target;
            yield return ScarfFrames(12);
            Assert.That(home.BathroomMirror.IsActive, Is.True);
            var source = home.Player.GameObject.GetComponent<PlayerScarfController>();
            var twin = home.BathroomMirror.Twin.GetComponent<PlayerScarfPresentation>();
            Assert.That(twin, Is.Not.Null);
            Assert.That(twin.IsVisible, Is.True);
            Assert.That(twin.TailSimulation, Is.Null, "The reflection copies the one simulation.");
            Assert.That(home.BathroomMirror.TwinUnpairedBoneCount, Is.Zero);
            Assert.That(home.BathroomMirror.TwinUnpairedRendererCount, Is.Zero);
            yield return CaptureScarfFrame(camera, "06-bathroom-mirror");
            Player3DHeadVisibility headHidden = null;
            Player3DBathingAppearance bathing = null;
            IDisposable worldHidden = null;
            PlayerScarfController.MouthAccess access = null;
            try
            {
                headHidden = Player3DHeadVisibility.Hide(source.Presentation.Registry);
                yield return ScarfFrames(2);
                Assert.That(source.Presentation.IsVisible, Is.False);
                Assert.That(twin.IsVisible, Is.True, "A first-person head stays present in the mirror.");
                GameSessionState.TrySetInventoryItemEquipped(InventoryItemId.Scarf, false);
                yield return ScarfFrames(2);
                Assert.That(twin.IsVisible, Is.False, "The head visibility rule cannot resurrect an unequipped scarf.");
                GameSessionState.TrySetInventoryItemEquipped(InventoryItemId.Scarf, true);
                headHidden.Restore(); headHidden = null;
                worldHidden = home.Player.PresentationVisibility.AcquireHidden(home);
                yield return ScarfFrames(2);
                Assert.That(source.Presentation.IsVisible, Is.False);
                Assert.That(twin.IsVisible, Is.False);
                worldHidden.Dispose(); worldHidden = null;
                bathing = Player3DBathingAppearance.Apply(source.Presentation.Registry);
                yield return ScarfFrames(2);
                Assert.That(source.Presentation.IsVisible, Is.False);
                Assert.That(twin.IsVisible, Is.False);
                bathing.Restore(); bathing = null;
                yield return ScarfFrames(2);
                Assert.That(source.Presentation.IsVisible, Is.True);
                Assert.That(twin.IsVisible, Is.True);

                access = PlayerScarfController.RequireMouthAccess(home.Player, home);
                source.enabled = false;
                Assert.That(access.IsReady, Is.True, "Disabled accessories revoke outstanding preparation waits.");
                access.Dispose(); access = null;
                source.enabled = true;
                yield return ScarfFrames(2);
                Assert.That(source.Presentation.IsVisible, Is.True);
            }
            finally
            {
                access?.Dispose();
                worldHidden?.Dispose();
                bathing?.Restore();
                headHidden?.Restore();
                source.enabled = true;
                camera.targetTexture = null;
            }
        }

        private static IEnumerator ScarfFrames(int count)
        {
            for (int frame = 0; frame < count; frame++) yield return null;
        }

        private static IEnumerator CaptureScarfFrame(Camera camera, string shot) =>
            ScarfRenderedFrame(camera, () => SaveScarfRenderedFrame(camera, shot));

        private static IEnumerator ScarfRenderedFrame(Camera camera, Action sample)
        {
            // The hero samples a base rig in Update and completes its pose in
            // LateUpdate. Observe a real rendered frame, not the coroutine's
            // intermediate pose between those two animation passes.
            bool completed = false;
            Exception failure = null;
            void OnRendered(ScriptableRenderContext context, Camera rendered)
            {
                if (rendered != camera || completed) return;
                completed = true;
                try { sample(); }
                catch (Exception error) { failure = error; }
            }
            RenderPipelineManager.endCameraRendering += OnRendered;
            try
            {
                float deadline = Time.realtimeSinceStartup + 10f;
                while (!completed && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(completed, Is.True, "The scarf camera must render a complete frame.");
                if (failure != null) throw failure;
            }
            finally { RenderPipelineManager.endCameraRendering -= OnRendered; }
        }

        private static void SaveScarfRenderedFrame(Camera camera, string shot)
        {
            RenderTexture target = camera.targetTexture;
            Assert.That(target, Is.Not.Null);
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
                pixels.Apply();
                Assert.That(IsBlank(pixels), Is.False, "Scarf capture must show the live scene.");
                string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "Scarf");
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, shot + ".png"), pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(pixels);
            }
        }

        private static int ScarfInventoryIndex()
        {
            for (int index = 0; index < GameSessionState.InventoryItems.Count; index++)
                if (GameSessionState.InventoryItems[index].ItemId == InventoryItemId.Scarf) return index;
            Assert.Fail("The collected scarf is absent from inventory.");
            return -1;
        }

        private static void AimScarfHero(Camera camera, PlayerRuntime player, Vector3 localOffset)
        {
            Transform hero = player.GameObject.transform;
            AimScarfCamera(camera, hero.TransformPoint(localOffset),
                hero.TransformPoint(new Vector3(0f, 1.52f, 0f)), 42f);
        }

        private static void AimScarfCamera(Camera camera, Vector3 position, Vector3 target, float fov)
        {
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
            camera.fieldOfView = fov;
            // A camera fill makes garment fit readable in inspection frames;
            // it belongs only to this capture fixture and never to the scene.
            if (camera.transform.Find("Scarf inspection fill") == null)
            {
                var fill = new GameObject("Scarf inspection fill");
                fill.transform.SetParent(camera.transform, false);
                var light = fill.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = .8f;
                light.shadows = LightShadows.None;
            }
        }

        private static void MeasureScarfCloth(SkinnedMeshRenderer renderer, Vector3[] rest, Vector3[] vertices,
            Vector3[] previous, ScarfJourneyReport report)
        {
            Mesh source = renderer.sharedMesh;
            BoneWeight[] weights = source.boneWeights;
            Matrix4x4[] poses = source.bindposes;
            Transform[] bones = renderer.bones;
            Assert.That(vertices.Length, Is.EqualTo(rest.Length));
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 point = vertices[index];
                Assert.That(float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z) ||
                    float.IsInfinity(point.x) || float.IsInfinity(point.y) || float.IsInfinity(point.z), Is.False);
                BoneWeight weight = weights[index];
                Vector3 bound = ScarfSkinPoint(rest[index], weight.boneIndex0, weight.weight0, bones, poses) +
                    ScarfSkinPoint(rest[index], weight.boneIndex1, weight.weight1, bones, poses) +
                    ScarfSkinPoint(rest[index], weight.boneIndex2, weight.weight2, bones, poses) +
                    ScarfSkinPoint(rest[index], weight.boneIndex3, weight.weight3, bones, poses);
                float drift = Vector3.Distance(renderer.transform.TransformPoint(point), bound);
                bool pinned = (1.567f - rest[index].y) / PlayerScarfPresentation.TailLength < .035f;
                if (pinned)
                {
                    report.pinned_vertex_samples++;
                    report.maximum_pinned_error_metres = Mathf.Max(report.maximum_pinned_error_metres, drift);
                }
                else
                {
                    report.free_vertex_samples++;
                    report.maximum_free_deformation_metres = Mathf.Max(report.maximum_free_deformation_metres, drift);
                    if (previous != null)
                        report.maximum_frame_motion_metres = Mathf.Max(report.maximum_frame_motion_metres,
                            Vector3.Distance(point, previous[index]));
                }
            }
        }

        private static Vector3 ScarfSkinPoint(Vector3 point, int bone, float weight,
            Transform[] bones, Matrix4x4[] bindposes)
        {
            return weight > 0f
                ? (bones[bone].localToWorldMatrix * bindposes[bone]).MultiplyPoint3x4(point) * weight
                : Vector3.zero;
        }

        private static float MeasureScarfSeam(PlayerScarfPresentation scarf, Vector3[] bakedTail)
        {
            Vector3[] rest = scarf.TailRestVertices;
            Vector3 edge = Vector3.zero;
            int edgeCount = 0;
            for (int i = 0; i < rest.Length; i++)
                if ((1.567f - rest[i].y) / PlayerScarfPresentation.TailLength < .035f)
                {
                    edge += scarf.TailRenderer.transform.TransformPoint(bakedTail[i]);
                    edgeCount++;
                }
            Assert.That(edgeCount, Is.GreaterThan(0));
            SkinnedMeshRenderer knot = null;
            foreach (Renderer part in scarf.Renderers)
                if (part.name == "ScarfKnot") knot = part as SkinnedMeshRenderer;
            Assert.That(knot, Is.Not.Null);
            Mesh mesh = knot.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            Matrix4x4[] poses = mesh.bindposes;
            Transform[] bones = knot.bones;
            Vector3 centre = Vector3.zero;
            for (int i = 0; i < vertices.Length; i++)
            {
                BoneWeight w = weights[i];
                centre += ScarfSkinPoint(vertices[i], w.boneIndex0, w.weight0, bones, poses) +
                    ScarfSkinPoint(vertices[i], w.boneIndex1, w.weight1, bones, poses) +
                    ScarfSkinPoint(vertices[i], w.boneIndex2, w.weight2, bones, poses) +
                    ScarfSkinPoint(vertices[i], w.boneIndex3, w.weight3, bones, poses);
            }
            return Vector3.Distance(edge / edgeCount, centre / vertices.Length);
        }

        private static void AssertScarfColdProtection()
        {
            var exposed = new AlpineColdExposureModel();
            var covered = new AlpineColdExposureModel();
            exposed.Step(20f, false);
            covered.Step(20f, false, true);
            Assert.That(covered.ExposureSeconds, Is.EqualTo(exposed.ExposureSeconds * .5f).Within(.0001f));
            covered.Step(20f, false, true);
            Assert.That(covered.FrostAmount, Is.EqualTo(exposed.FrostAmount).Within(.0001f));
            exposed.Step(1f, true);
            covered.Step(1f, true, true);
            Assert.That(covered.ExposureSeconds, Is.EqualTo(exposed.ExposureSeconds).Within(.0001f));
            var shiver = new PlayerColdPresentationModel();
            shiver.Step(PlayerColdPresentationModel.FirstShiverSeconds + .5f);
            Assert.That(shiver.GetShiverWeight(false), Is.GreaterThan(.9f));
            Assert.That(shiver.GetShiverWeight(true), Is.EqualTo(shiver.GetShiverWeight(false) * .5f));
        }
    }
}
