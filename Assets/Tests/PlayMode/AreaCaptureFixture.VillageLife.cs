using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class VillageLifeAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            foreach (string name in new[] { "VillageResidentAssetSetup", "VillageLifePropAssetSetup", "VillageResidentDoorAssetSetup" })
            {
                Type setup = Type.GetType("BarPromenade.Editor." + name + ", BarPromenade.Editor", true);
                setup.GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
            }
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        private const float VillageLifeStep = 0.02f;

        [Serializable]
        private sealed class VillageLifeReport
        {
            public int checked_seeds;
            public int resident_count;
            public int hero_triangles;
            public int woman_triangles;
            public int station_worker_triangles;
            public int delivered_baskets;
            public int contact_samples;
            public float maximum_grip_error_metres;
            public float maximum_basket_step_metres;
            public float maximum_basket_step_degrees;
            public bool player_blocks_resident;
            public bool pause_freezes_residents;
            public bool station_exit_walked;
            public bool finite_stock_preserved;
        }

        [UnityTest]
        [Explicit("One focused village household journey, real contacts and equal-scale resident/hero captures. Run alone.")]
        [PrebuildSetup(typeof(VillageLifeAssetsSetup))]
        public IEnumerator VillageLife()
        {
            var report = new VillageLifeReport { checked_seeds = AssertVillageLifePlans() };
            float previousDelta = Time.captureDeltaTime;
            AlpineVillageRoot village = null;
            Camera camera = null;
            Vector3 cameraPosition = default;
            Quaternion cameraRotation = default;
            float cameraFov = 60f;
            float cameraAspect = 16f / 9f;
            bool motorEnabled = false;
            bool followEnabled = false;
            IDisposable pause = null;
            try
            {
                GameSessionState.BeginNewGame();
                GameSessionState.TryStartGameTimeFromWake();
                Time.captureDeltaTime = 1f / 60f;
                AsyncOperation load = SceneManager.LoadSceneAsync(SceneIds.AlpineVillage, LoadSceneMode.Single);
                Assert.That(load, Is.Not.Null);
                float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
                while (Time.realtimeSinceStartup < deadline)
                {
                    village = Object.FindAnyObjectByType<AlpineVillageRoot>();
                    if (load.isDone && village != null && village.IsInitialized) break;
                    yield return null;
                }
                Assert.That(village, Is.Not.Null);
                Assert.That(village.IsInitialized, Is.True);
                AlpineVillageLifeController life = village.Life;
                Assert.That(life, Is.Not.Null);
                life.enabled = false;
                report.resident_count = life.GetComponentsInChildren<VillageResidentPresentation>().Length;
                Assert.That(report.resident_count, Is.EqualTo(6));
                Assert.That(life.Baskets.Count, Is.EqualTo(AlpineVillageLifePlan.BasketCount));
                Assert.That(life.Woman.Role, Is.EqualTo(VillageResidentRole.WoodWoman));
                Assert.That(life.StationWorker.Role, Is.EqualTo(VillageResidentRole.StationWorker));
                AssertVillageLifeCollision(village);

                camera = Camera.main;
                Assert.That(camera, Is.Not.Null);
                cameraPosition = camera.transform.position;
                cameraRotation = camera.transform.rotation;
                cameraFov = camera.fieldOfView;
                cameraAspect = camera.aspect;
                followEnabled = village.CameraFollow.enabled;
                motorEnabled = village.Player.Motor.enabled;
                village.CameraFollow.enabled = false;
                village.Player.Motor.SetInputEnabled(false);
                camera.aspect = 16f / 9f;
                for (int frame = 0; frame < SettleFrames; frame++) yield return null;

                // This goes through the real CharacterController and the new station
                // meshes. A clear plan alone cannot prove that the exit is passable.
                village.Player.Motor.Teleport(village.Plan.Station.BoardingDockPosition +
                    Vector3.up * PlayerFactory.GroundedRootOffset);
                Physics.SyncTransforms();
                bool escaped = false;
                for (int step = 0; step < 240 && !escaped; step++)
                {
                    escaped = village.Player.Motor.MoveTowardsApproachWaypoint(village.Plan.Lane.Start, 0.25f, 0.1f);
                    Assert.That(village.Player.Motor.InteractionPoseMoveStalled, Is.False,
                        "The station resident or his crate blocks the real exit capsule.");
                    yield return null;
                }
                Assert.That(escaped, Is.True, "The real player must walk out of the station.");
                report.station_exit_walked = true;
                TestContext.Out.WriteLine("Village life: the real player walked out of the upper station.");
                village.Player.Motor.CancelInteractionPoseMove();
                village.Player.Motor.enabled = false;
                var hero = village.Player.Visual as Player3DCharacterPresentation;
                Assert.That(hero, Is.Not.Null);
                hero.SetMotion(PlayerMotionSample.Stationary);
                report.hero_triangles = VillageLifeTriangleCount(hero.Registry.transform);
                report.woman_triangles = AssertVillageResidentDetail(life.Woman, report.hero_triangles);
                report.station_worker_triangles = AssertVillageResidentDetail(life.StationWorker, report.hero_triangles);
                TestContext.Out.WriteLine($"Village life imported triangles: hero {report.hero_triangles}, " +
                    $"woman {report.woman_triangles}, station worker {report.station_worker_triangles}.");

                VillageLifeFrame(camera, "00-station", life.Plan.StationWork +
                    life.Plan.StationForward * 4f + village.Plan.Station.Cableway.LineRight * 2.2f + Vector3.up * 1.7f,
                    life.Plan.StationCrate + Vector3.up * 0.9f, 55f);
                VillageLifeFrame(camera, "01-firewood-courtyard", life.Plan.Yard(-1f, 9f) + Vector3.up * 2.1f,
                    life.Plan.Yard(0.7f, 1.7f) + Vector3.up * 0.9f, 58f);
                CaptureVillageResidentComparison(village, camera, life.StationWorker, "02-station-worker");
                CaptureVillageResidentComparison(village, camera, life.Woman, "03-wood-woman");

                // Recognition is bounded, ordinary speech; the resident resumes
                // her own action without a player transaction or an inventory step.
                village.Player.Motor.Teleport(life.Woman.transform.position + life.Plan.Forward * 1.5f);
                Assert.That(life.CanTalk(VillageResidentRole.WoodWoman), Is.True);
                Assert.That(life.TryGreet(VillageResidentRole.WoodWoman), Is.True);
                Assert.That(life.LastGreetingKey, Is.EqualTo("village.life.wood.recognition"));
                Assert.That(life.TryGreet(VillageResidentRole.WoodWoman), Is.False,
                    "One approach must not repeat a recognition line every frame.");
                VillageLifeFrame(camera, "04-recognition", life.Woman.transform.position +
                    life.Plan.Forward * 3.8f + life.Plan.Right * 1.3f + Vector3.up * 1.6f,
                    life.Woman.Head.position, 46f);
                ParkVillageLifeHero(village);

                var basketIds = new Transform[life.Baskets.Count];
                var childIds = new HashSet<Transform>[life.Baskets.Count];
                var previousBasketPositions = new Vector3[life.Baskets.Count];
                var previousBasketRotations = new Quaternion[life.Baskets.Count];
                for (int i = 0; i < life.Baskets.Count; i++)
                {
                    basketIds[i] = life.Baskets[i];
                    childIds[i] = VillageLifeChildren(life.Baskets[i]);
                    previousBasketPositions[i] = life.Baskets[i].position;
                    previousBasketRotations[i] = life.Baskets[i].rotation;
                }
                bool carryCaptured = false;
                bool placeCaptured = false;
                bool stationWorkCaptured = false;
                bool blockedChecked = false;
                for (int step = 0; step < 16000 && life.DeliveredBaskets < 2; step++)
                {
                    life.Advance(VillageLifeStep);
                    MeasureVillageBasketContinuity(life, previousBasketPositions, previousBasketRotations, report);
                    if (life.CarriedBasket != null)
                        MeasureVillageGripContact(life, report);
                    if (step % 10 == 0) AssertVillageResidentClearance(life);
                    if (!stationWorkCaptured && life.StationWorker.CurrentAction == VillageResidentAction.StationWork &&
                        life.StationWorker.CurrentActionSeconds > life.StationWorker.ClipLength(VillageResidentAction.StationWork) * 0.4f)
                    {
                        VillageLifeFrame(camera, "00-station-lid-work", life.Plan.StationWork +
                            life.Plan.StationForward * 3.4f + village.Plan.Station.Cableway.LineRight * 1.6f + Vector3.up * 1.6f,
                            life.Plan.StationCrate + Vector3.up * 0.95f, 43f);
                        stationWorkCaptured = true;
                    }
                    if (life.WomanStage == VillageWomanStage.Carrying && !carryCaptured &&
                        Vector3.Distance(life.Woman.transform.position, life.Plan.Dock(life.Plan.Pickups[0])) > 0.95f)
                    {
                        VillageLifeFrame(camera, "05-first-basket-carry", life.Plan.Yard(0f, 6f) + Vector3.up * 1.3f,
                            (life.Woman.LeftGrip.position + life.Woman.RightGrip.position) * 0.5f, 43f);
                        AssertVillageHandGeometry(life.Woman);
                        carryCaptured = true;
                        TestContext.Out.WriteLine("Village life: first basket reached carry with both visible hand contacts.");
                    }
                    if (life.WomanStage == VillageWomanStage.Carrying && !blockedChecked)
                    {
                        Vector3 direction = life.Plan.CarryRoute(life.DeliveredBaskets)[1] - life.Woman.transform.position;
                        direction.y = 0f;
                        village.Player.Motor.Teleport(life.Woman.transform.position + direction.normalized * 0.55f);
                        Physics.SyncTransforms();
                        Vector3 heldPosition = life.Woman.transform.position;
                        for (int frame = 0; frame < 50; frame++)
                        {
                            life.Advance(VillageLifeStep);
                            MeasureVillageBasketContinuity(life, previousBasketPositions, previousBasketRotations, report);
                            MeasureVillageGripContact(life, report);
                            Assert.That(Vector3.Distance(heldPosition, life.Woman.transform.position), Is.LessThan(0.001f));
                        }
                        Assert.That(life.IsWomanBlocked, Is.True);
                        Assert.That(life.CanTalk(VillageResidentRole.WoodWoman), Is.False,
                            "Carrying and waiting for a clear path must retain both hands on the basket.");
                        report.player_blocks_resident = true;
                        pause = GameTimeScaleRuntime.AcquirePause();
                        Vector3 left = life.Woman.LeftGrip.position;
                        Vector3 stationLeft = life.StationWorker.LeftGrip.position;
                        Vector3 basketPosition = life.CarriedBasket.position;
                        for (int frame = 0; frame < 4; frame++)
                        {
                            life.Advance(0.25f);
                            yield return null;
                        }
                        Assert.That(Vector3.Distance(left, life.Woman.LeftGrip.position), Is.LessThan(0.0001f));
                        Assert.That(Vector3.Distance(stationLeft, life.StationWorker.LeftGrip.position), Is.LessThan(0.0001f));
                        Assert.That(Vector3.Distance(basketPosition, life.CarriedBasket.position), Is.LessThan(0.0001f));
                        report.pause_freezes_residents = true;
                        pause.Dispose();
                        pause = null;
                        ParkVillageLifeHero(village);
                        blockedChecked = true;
                    }
                    if (life.WomanStage == VillageWomanStage.PuttingDown && life.CarriedBasket != null && !placeCaptured &&
                        life.Woman.CurrentAction == VillageResidentAction.Place && life.Woman.CurrentActionSeconds > 0.65f)
                    {
                        CaptureVillageCarry(camera, life, "06-first-basket-lowering");
                        placeCaptured = true;
                    }
                    if (step % 120 == 0) yield return null;
                }
                Assert.That(life.DeliveredBaskets, Is.EqualTo(2), "The finite household task never completed.");
                Assert.That(carryCaptured && placeCaptured && blockedChecked && stationWorkCaptured, Is.True);
                Assert.That(life.CarriedBasket, Is.Null);
                for (int i = 0; i < life.Baskets.Count; i++)
                {
                    Assert.That(life.Baskets[i], Is.SameAs(basketIds[i]));
                    CollectionAssert.AreEquivalent(childIds[i], VillageLifeChildren(life.Baskets[i]),
                        "Logs and basket meshes must stay the same actual objects through carrying and release.");
                    Vector3 expected = life.Plan.Deliveries[i] + Vector3.up * AlpineVillageLifePlan.StandHeight;
                    Assert.That(Vector3.Distance(life.Baskets[i].position, expected), Is.LessThan(0.065f),
                        "The released basket must stand on the real destination support.");
                    previousBasketPositions[i] = life.Baskets[i].position;
                }
                // Beyond completion the woman can work and rest, but loaded
                // baskets cannot silently refill, unload or return to the stack.
                bool rested = false;
                bool worked = false;
                for (int step = 0; step < 700; step++)
                {
                    life.Advance(0.1f);
                    rested |= life.WomanStage == VillageWomanStage.Resting;
                    worked |= life.WomanStage == VillageWomanStage.Working;
                    for (int i = 0; i < life.Baskets.Count; i++)
                        Assert.That(Vector3.Distance(previousBasketPositions[i], life.Baskets[i].position), Is.LessThan(0.0001f));
                    if (step % 100 == 0) yield return null;
                }
                Assert.That(rested && worked, Is.True, "A finished delivery must lead into independent household life.");
                Assert.That(life.DeliveredBaskets, Is.EqualTo(2));
                report.delivered_baskets = life.DeliveredBaskets;
                report.finite_stock_preserved = true;
                VillageLifeFrame(camera, "07-both-baskets-placed", life.Plan.Yard(-1f, 9f) + Vector3.up * 2.1f,
                    life.Plan.Yard(0.7f, 1.7f) + Vector3.up * 0.9f, 58f);
                string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "VillageLife");
                Directory.CreateDirectory(folder);
                string json = JsonUtility.ToJson(report, true);
                File.WriteAllText(Path.Combine(folder, "verification.json"), json);
                TestContext.Out.WriteLine(json);
                yield return VerifyVillageNeighbours(village, camera);
            }
            finally
            {
                pause?.Dispose();
                Time.captureDeltaTime = previousDelta;
                if (camera != null)
                {
                    camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                    camera.fieldOfView = cameraFov;
                    camera.aspect = cameraAspect;
                }
                if (village != null && village.IsInitialized)
                {
                    village.Player.Motor.CancelInteractionPoseMove();
                    village.Player.Motor.SetInputEnabled(true);
                    village.Player.Motor.enabled = motorEnabled;
                    village.CameraFollow.enabled = followEnabled;
                    if (village.Life != null) village.Life.enabled = true;
                }
            }
        }

        private static int AssertVillageLifePlans()
        {
            int[] seeds = { GameSessionState.DefaultCitySeed, -99992, -99895, -96746, -87107,
                -58640, -29563, 3677, 57657, 89380 };
            foreach (int seed in seeds)
            {
                AlpineVillagePlan village = AlpineVillagePlanner.Create(seed);
                AlpineVillageLifePlan life = AlpineVillageLifePlan.Create(village);
                var paths = AlpineVillagePathPlanner.Create(village);
                int workPaths = 0;
                foreach (AlpineVillagePathDescriptor path in paths)
                {
                    if (path.Kind != AlpineVillagePathKind.HouseholdWork) continue;
                    workPaths++;
                    CollectionAssert.Contains(new[] { AlpineVillageLifePlan.WoodHouseId, "village-house-08", "village-house-11" }, path.OwnerPlotStableId);
                    Assert.That(path.SurfaceHalfWidth, Is.LessThan(AlpineVillagePathPlanner.HouseholdSurfaceHalfWidth));
                }
                Assert.That(workPaths, Is.GreaterThanOrEqualTo(7), "Every finite carrying line must wear a narrow path.");
                foreach (Vector3 pickup in life.Pickups)
                    Assert.That(AlpineVillageSnowDrift.SampleDepth(village, paths,
                        new Vector2(life.Dock(pickup).x, life.Dock(pickup).z)), Is.LessThan(0.01f));
            }
            return seeds.Length;
        }

        private static void AssertVillageLifeCollision(AlpineVillageRoot village)
        {
            AlpineVillageLifeController life = village.Life;
            Assert.That(life.SolidProps.Count, Is.GreaterThan(0));
            foreach (Collider collider in life.SolidProps)
            {
                Assert.That(collider, Is.TypeOf<MeshCollider>());
                Assert.That(collider.isTrigger, Is.False);
                Assert.That(((MeshCollider)collider).sharedMesh,
                    Is.SameAs(collider.GetComponent<MeshFilter>().sharedMesh),
                    "The shelter is open where its visible mesh is open.");
            }
            foreach (VillageResidentPresentation resident in new[] { life.Woman, life.StationWorker })
            {
                CapsuleCollider body = resident.GetComponent<CapsuleCollider>();
                Assert.That(body, Is.Not.Null);
                Assert.That(body.enabled && !body.isTrigger, Is.True);
                Assert.That(body.height, Is.InRange(1f, 1.9f));
                Assert.That(resident.GetComponentInChildren<VillageResidentGreeting>(), Is.Not.Null);
            }
            Physics.SyncTransforms();
            // The ordinary door stays usable between the two groups of stands.
            Vector3 door = life.Plan.House.DoorDockPosition;
            foreach (Collider hit in Physics.OverlapCapsule(door + Vector3.up * 0.4f,
                         door + Vector3.up * 1.35f, 0.30f, Physics.DefaultRaycastLayers,
                         QueryTriggerInteraction.Ignore))
                Assert.That(hit.transform.IsChildOf(life.transform), Is.False,
                    "New household objects must not occupy the existing house threshold: " + hit.name);
        }

        private static int AssertVillageResidentDetail(VillageResidentPresentation resident, int heroTriangles)
        {
            Renderer[] renderers = resident.ModelRoot.GetComponentsInChildren<Renderer>();
            Assert.That(renderers.Length, Is.GreaterThan(0));
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
                Assert.That(renderer.sharedMaterial, Is.Not.Null);
                var appearance = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(appearance);
                Texture texture = appearance.GetTexture("_BaseMap");
                Assert.That(texture, Is.Not.Null, "The resident's own cloth/face/boot atlas must reach the imported renderers.");
                Assert.That(texture.width, Is.GreaterThanOrEqualTo(256));
                Assert.That(texture.height, Is.GreaterThanOrEqualTo(256));
            }
            // Skin culling bounds include movement outside the current pose.
            // Measure the visible deformed body, using the same scaled BakeMesh
            // convention as the hand-contact proof below.
            Vector3 low = Vector3.positiveInfinity, high = Vector3.negativeInfinity;
            var bodyMesh = new Mesh();
            try
            {
                foreach (var skin in resident.ModelRoot.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    skin.BakeMesh(bodyMesh, true);
                    foreach (Vector3 vertex in bodyMesh.vertices)
                    {
                        Vector3 point = skin.localToWorldMatrix.MultiplyPoint3x4(vertex);
                        low = Vector3.Min(low, point); high = Vector3.Max(high, point);
                    }
                }
            }
            finally { Object.DestroyImmediate(bodyMesh); }
            Assert.That(high.y - low.y, Is.InRange(1.45f, 1.95f), "Measure the visible imported body of " + resident.Role);
            // Imported skinned bounds may retain the full authoring arm span.
            Assert.That(bounds.size.x, Is.InRange(0.3f, 1.9f));
            int triangles = VillageLifeTriangleCount(resident.ModelRoot);
            Assert.That(triangles, Is.GreaterThanOrEqualTo(heroTriangles),
                "The hero is the minimum detail baseline; the paired captures still require visual review.");
            return triangles;
        }

        private static int VillageLifeTriangleCount(Transform root)
        {
            int count = 0;
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                count += Count(renderer.sharedMesh);
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>()) count += Count(filter.sharedMesh);
            return count;
            int Count(Mesh mesh)
            {
                int indices = 0;
                if (mesh != null)
                    for (int i = 0; i < mesh.subMeshCount; i++) indices += (int)mesh.GetIndexCount(i);
                return indices / 3;
            }
        }

        private static HashSet<Transform> VillageLifeChildren(Transform root)
        {
            var result = new HashSet<Transform>();
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) result.Add(child);
            return result;
        }

        private static void MeasureVillageBasketContinuity(AlpineVillageLifeController life,
            Vector3[] previous, Quaternion[] previousRotations, VillageLifeReport report)
        {
            for (int i = 0; i < life.Baskets.Count; i++)
            {
                float distance = Vector3.Distance(previous[i], life.Baskets[i].position);
                report.maximum_basket_step_metres = Mathf.Max(report.maximum_basket_step_metres, distance);
                Assert.That(distance, Is.LessThan(0.08f),
                    "A basket jumped at a pickup/carry/release boundary: " + life.WomanStage);
                float angle = Quaternion.Angle(previousRotations[i], life.Baskets[i].rotation);
                report.maximum_basket_step_degrees = Mathf.Max(report.maximum_basket_step_degrees, angle);
                Assert.That(angle, Is.LessThan(12f),
                    "A basket snapped around at a pickup/carry/release boundary: " + life.WomanStage);
                previous[i] = life.Baskets[i].position;
                previousRotations[i] = life.Baskets[i].rotation;
            }
        }

        private static void AssertVillageResidentClearance(AlpineVillageLifeController life)
        {
            Physics.SyncTransforms();
            CapsuleCollider body = life.Woman.GetComponent<CapsuleCollider>();
            Vector3 center = body.transform.TransformPoint(body.center);
            Vector3 halfAxis = body.transform.up * (body.height * 0.5f - body.radius);
            foreach (Collider hit in Physics.OverlapCapsule(center - halfAxis,
                         center + halfAxis, body.radius, Physics.DefaultRaycastLayers,
                         QueryTriggerInteraction.Ignore))
                foreach (Collider solid in life.SolidProps)
                    Assert.That(hit, Is.Not.SameAs(solid),
                        "The actual resident body crossed household furniture at " + body.transform.position + ": " + hit.name);
        }

        private static void MeasureVillageGripContact(AlpineVillageLifeController life, VillageLifeReport report)
        {
            Transform basket = life.CarriedBasket;
            Assert.That(basket, Is.Not.Null);
            Transform left = basket.Find("ANCHOR_LeftGrip");
            Transform right = basket.Find("ANCHOR_RightGrip");
            Assert.That(left, Is.Not.Null);
            Assert.That(right, Is.Not.Null);
            float error = Mathf.Max(Vector3.Distance(left.position, life.Woman.LeftGrip.position),
                Vector3.Distance(right.position, life.Woman.RightGrip.position));
            report.maximum_grip_error_metres = Mathf.Max(report.maximum_grip_error_metres, error);
            report.contact_samples++;
            Assert.That(error, Is.LessThan(0.025f), "The actual imported hand sockets must retain both basket handles.");
        }

        private static void AssertVillageHandGeometry(VillageResidentPresentation resident)
        {
            float left = float.PositiveInfinity;
            float right = float.PositiveInfinity;
            foreach (SkinnedMeshRenderer renderer in resident.ModelRoot.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!renderer.name.StartsWith("GEO_Hand.", StringComparison.Ordinal)) continue;
                var baked = new Mesh();
                try
                {
                    renderer.BakeMesh(baked, true);
                    foreach (Vector3 vertex in baked.vertices)
                    {
                        Vector3 world = renderer.localToWorldMatrix.MultiplyPoint3x4(vertex);
                        left = Mathf.Min(left, Vector3.Distance(world, resident.LeftGrip.position));
                        right = Mathf.Min(right, Vector3.Distance(world, resident.RightGrip.position));
                    }
                }
                finally { Object.DestroyImmediate(baked); }
            }
            Assert.That(left, Is.LessThan(0.08f), "The left grip must touch the deformed visible mitten, not just an empty anchor.");
            Assert.That(right, Is.LessThan(0.08f), "The right grip must touch the deformed visible mitten, not just an empty anchor.");
        }

        private static void CaptureVillageResidentComparison(AlpineVillageRoot village, Camera camera,
            VillageResidentPresentation resident, string name)
        {
            Transform actor = resident.transform;
            Vector3 facing = actor.forward;
            Vector3 right = actor.right;
            // The work docks face their props, with the house less than 3 m
            // behind those props. A distant front camera would stand in its wall.
            VillageLifeFrame(camera, name + "-close", actor.position + facing * 1.9f + right * 0.45f + Vector3.up * 1.35f,
                actor.position + Vector3.up * 1.1f, 45f);
            Vector3 heroPosition = actor.position - right * 1.0f;
            heroPosition.y = actor.position.y + PlayerFactory.GroundedRootOffset;
            village.Player.Motor.Teleport(heroPosition);
            village.Player.GameObject.transform.rotation = actor.rotation;
            var hero = (Player3DCharacterPresentation)village.Player.Visual;
            hero.SetMotion(PlayerMotionSample.Stationary);
            hero.ReapplyLatePresentationPose();
            Vector3 center = (actor.position + heroPosition) * 0.5f + Vector3.up * 1.0f;
            VillageLifeFrame(camera, name + "-with-hero-same-scale", center + facing * 1.9f + Vector3.up * 0.25f,
                center, 60f);
            ParkVillageLifeHero(village);
        }

        private static void CaptureVillageCarry(Camera camera, AlpineVillageLifeController life, string name)
        {
            Transform actor = life.Woman.transform;
            VillageLifeFrame(camera, name, actor.position - actor.forward * 0.8f + actor.right * 2.5f + Vector3.up * 1.45f,
                (life.Woman.LeftGrip.position + life.Woman.RightGrip.position) * 0.5f, 39f);
        }

        private static void ParkVillageLifeHero(AlpineVillageRoot village)
        {
            var lane = village.Plan.Lane.Sample(8f);
            Vector3 side = village.Life.Neighbourhood.Ground(lane.Position + lane.Right * 3f);
            village.Player.Motor.Teleport(side + Vector3.up * PlayerFactory.GroundedRootOffset);
            Physics.SyncTransforms();
        }

        private static void VillageLifeFrame(Camera camera, string name, Vector3 position, Vector3 target, float fov)
        {
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, Vector3.up));
            camera.fieldOfView = fov;
            CaptureCurrentCamera(camera, "VillageLife", name);
        }
    }
}
