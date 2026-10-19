using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class CombatRoomAttackAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            bool existing = Array.Exists(Environment.GetCommandLineArgs(), value => value == "-combatExistingAssets");
            Type.GetType("BarPromenade.Editor.CombatRoomAttackAssetSetup, BarPromenade.Editor", true)
                .GetMethod(existing ? "ValidateImportedOrThrow" : "BuildOrThrow", BindingFlags.Static | BindingFlags.Public)
                .Invoke(null, null);
#endif
        }
    }

    public sealed partial class CombatTestPlayModeTests
    {
        [UnityTest]
        [PrebuildSetup(typeof(CombatRoomAssetsSetup))]
        public IEnumerator Range_RoomAttackKeepsShotsDamageLaughPauseAndCleanup()
        {
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            CombatTestRoot.AuthoredEnemyKindForTests = CombatEnemyKind.Room;
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            var settings = new CombatEncounterSettings(3, .1f, .1f, .15f, .15f);
            Assert.That(CombatTestStartService.TryStart(new CombatTestSelection(
                new CombatLoadoutSelection(CombatWeaponId.Pistol), CombatTestMode.Endless, settings)), Is.True);
            yield return AwaitSelectedCombatRange();
            root.SendMessage("OnApplicationFocus", true);
            CombatActor room = root.Opponents[0];
            Assert.That(room.IsRoom, Is.True);
            PlaceRoomAttackTestPair(room);
            Vector3 heroRoot = root.Hero.transform.position;
            float ordinaryNearClip = root.CameraFollow.Camera.nearClipPlane;
            root.CameraFollow.RotateYaw(18f); // Leave an orbit target pending when the camera is borrowed.
            yield return TraceRoomCameraToPhase(CombatRoomSequencePhase.PointHero, "Finding the pointing hand");
            Assert.That(root.RoomAttackCinematicActive, Is.True);
            Assert.That(root.RoomAttackTableauVisible, Is.True, "The seated hero is already present inside the real chest opening.");
            Assert.That(root.Player.Motor.InputEnabled, Is.False);
            Assert.That(GameInput.CanRead(GameInputContext.Gameplay), Is.False);
            Assert.That(root.CameraFollow.FixedPoseActive, Is.True);

            var opponents = new Vector3[root.Opponents.Count];
            var heads = new Quaternion[root.Opponents.Count];
            for (int i = 0; i < opponents.Length; i++)
            {
                opponents[i] = root.Opponents[i].transform.position;
                heads[i] = FindAnatomicalBone(root.Opponents[i], "head").rotation;
            }
            Assert.That(root.Projectiles.TrySpawn(root.Hero, heroRoot + Vector3.up * 2.5f,
                Vector3.right * CombatProjectilePool.MuzzleSpeed, 1), Is.True);
            Vector3 bullet = root.Projectiles.LastPosition;
            int impacts = root.Projectiles.ImpactCount;
            root.Tick(.75f);
            Assert.That(root.RoomAttackPhase, Is.EqualTo(CombatRoomSequencePhase.PointHero));
            Transform pointingHand = FindAnatomicalBone(room, "hand.R");
            Vector3 towardHero = ((Player3DCharacterPresentation)root.Player.Visual).Registry.Anchors.Head.position - pointingHand.position;
            Assert.That(Vector3.Dot(pointingHand.TransformDirection(Vector3.up).normalized, towardHero.normalized), Is.GreaterThan(.65f),
                "The pointing finger is directed at the actual hero.");
            AssertRoomTestFreeze(heroRoot, opponents, heads, bullet, impacts);
            yield return AtCombatPresentation(() =>
            {
                AssertRoomTestInFrame(FindAnatomicalBone(room, "head").position);
                AssertRoomTestInFrame(room.RoomHandTipWorld(false));
                CaptureMixedCameraFrame(root.CameraFollow.Camera, "Test Room attack - Pointing at hero");
            });

            float held = root.RoomAttackPhaseElapsed;
            Pose heldCamera = new Pose(root.CameraFollow.Camera.transform.position, root.CameraFollow.Camera.transform.rotation);
            Assert.That(root.PauseMenu.Open(), Is.True);
            root.Tick(1f);
            yield return null;
            Assert.That(root.RoomAttackPhaseElapsed, Is.EqualTo(held));
            Assert.That(root.CameraFollow.Camera.transform.position, Is.EqualTo(heldCamera.position));
            Assert.That(Quaternion.Angle(root.CameraFollow.Camera.transform.rotation, heldCamera.rotation), Is.LessThan(.01f));
            Assert.That(root.PauseMenu.Cancel(), Is.True);
            yield return WaitFor(() => !root.PauseMenu.IsOpen && !GameTimeScaleRuntime.IsPaused,
                "Pause did not release its overlay on the Room shot.");
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.True, "Closing pause preserves the attack's input loan.");
            Assert.That(GameInput.CanRead(GameInputContext.Gameplay), Is.False);
            root.Tick(1.2f);
            Assert.That(root.RoomAttackPhase, Is.EqualTo(CombatRoomSequencePhase.PointHero), "The point shot lasts the full two seconds.");
            root.Tick(.06f);
            Assert.That(root.RoomAttackPhase, Is.EqualTo(CombatRoomSequencePhase.PointChest));
            yield return TraceRoomCameraToPhase(CombatRoomSequencePhase.SeatedHero, "Entering the room");
            Assert.That(root.RoomAttackTableauVisible, Is.True);
            Player3DAssetRegistry image = root.RoomAttackHeroImage;
            Assert.That(image, Is.Not.Null);
            Assert.That(image.GetComponent<CombatActor>(), Is.Null, "The seated hero is a visual of the production model.");
            foreach (Collider collider in image.GetComponentsInChildren<Collider>(true)) Assert.That(collider.enabled, Is.False);
            MeshRenderer corridor = null, chair = null;
            foreach (MeshRenderer surface in image.transform.parent.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (surface.name == "GEO_RoomAttackOriginalCorridor") corridor = surface;
                if (surface.name == "GEO_RoomAttackWoodChair") chair = surface;
            }
            Assert.That(corridor, Is.Not.Null, "The insert retains the original chest corridor.");
            Assert.That(corridor.enabled, Is.False, "The insert must not draw overlapping corridor walls.");
            Assert.That(corridor.sharedMaterial, Is.SameAs(CombatRoomAssetProvider.LoadBodyMaterial("plaster")));
            SkinnedMeshRenderer originalCorridor = null;
            foreach (SkinnedMeshRenderer surface in room.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (surface.name == "GEO_Torso") originalCorridor = surface;
            Assert.That(originalCorridor, Is.Not.Null, "The camera enters the NPC's actual authored torso corridor.");
            Assert.That(originalCorridor.enabled, Is.True, "The original corridor remains visible throughout the camera journey.");
            Assert.That(chair, Is.Not.Null);
            Assert.That(chair.sharedMaterials, Has.Length.EqualTo(1), "The authored chair has a drawable material slot.");
            Assert.That(root.Hero.ReceivedImpactCount, Is.Zero, "The room insert cannot apply damage early.");
            AssertRoomTestSeatedFootContact(image, originalCorridor);
            root.Tick(.45f);
            yield return AtCombatPresentation(() => AssertRoomTestSeatedFootContact(image, originalCorridor));
            root.Tick(.45f);
            yield return AtCombatPresentation(() =>
            {
                AssertRoomTestSeatedFootContact(image, originalCorridor);
                AssertRoomTestInFrame(image.Anchors.Head.position);
                CaptureMixedCameraFrame(root.CameraFollow.Camera, "Test Room attack - Seated hero");
            });
            float interiorNearClip = root.CameraFollow.Camera.nearClipPlane;
            Assert.That(interiorNearClip, Is.LessThan(ordinaryNearClip), "The lens can see the actual miniature interior without clipping it.");
            float seatedHeld = root.RoomAttackPhaseElapsed;
            Pose seatedCamera = new Pose(root.CameraFollow.Camera.transform.position, root.CameraFollow.Camera.transform.rotation);
            Assert.That(root.PauseMenu.Open(), Is.True);
            root.Tick(.5f);
            yield return null;
            Assert.That(root.RoomAttackPhaseElapsed, Is.EqualTo(seatedHeld));
            Assert.That(root.CameraFollow.Camera.nearClipPlane, Is.EqualTo(interiorNearClip));
            Assert.That(root.CameraFollow.Camera.transform.position, Is.EqualTo(seatedCamera.position));
            Assert.That(Quaternion.Angle(root.CameraFollow.Camera.transform.rotation, seatedCamera.rotation), Is.LessThan(.01f));
            Assert.That(root.PauseMenu.Cancel(), Is.True);
            yield return WaitFor(() => !root.PauseMenu.IsOpen && !GameTimeScaleRuntime.IsPaused,
                "Pause did not release its overlay inside the Room interior.");
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.True);
            Assert.That(root.CameraFollow.Camera.nearClipPlane, Is.EqualTo(interiorNearClip));
            root.Tick(.5f);
            yield return AtCombatPresentation(() => AssertRoomTestSeatedFootContact(image, originalCorridor));
            root.Tick(.55f);
            yield return AtCombatPresentation(() => AssertRoomTestSeatedFootContact(image, originalCorridor));
            Assert.That(root.RoomAttackPhase, Is.EqualTo(CombatRoomSequencePhase.SeatedHero));
            AssertRoomTestFreeze(heroRoot, opponents, heads, bullet, impacts);
            root.Tick(.06f);
            Assert.That(root.RoomAttackPhase, Is.EqualTo(CombatRoomSequencePhase.ReturnRoom));
            Assert.That(root.RoomAttackTableauVisible, Is.True, "The hero stays inside the real chest during the reverse journey.");
            yield return TraceRoomCameraToPhase(CombatRoomSequencePhase.HeadPain, "Returning to the hero");
            Assert.That(root.RoomAttackTableauVisible, Is.False);
            Assert.That(root.Hero.ReceivedImpactCount, Is.EqualTo(1));
            Assert.That(root.Hero.State.Health, Is.EqualTo(root.Hero.State.Settings.MaxHealth - 18f).Within(.001f));
            Assert.That(root.Hero.LastImpact.Source, Is.SameAs(room));
            Assert.That(root.Hero.LastImpact.Impulse, Is.EqualTo(Vector3.zero));
            Assert.That(root.Hero.LastImpact.WoundDamage, Is.Zero);
            Assert.That(root.BloodEffects.EmissionCount, Is.Zero);
            Assert.That(root.CameraFollow.FixedPoseActive, Is.False);
            Assert.That(root.CameraFollow.Camera.nearClipPlane, Is.EqualTo(ordinaryNearClip).Within(.00001f),
                "The miniature interior lens must release its near plane with the camera.");
            root.Tick(.9f);
            yield return AtCombatPresentation(() =>
            {
                var hero = ((Player3DCharacterPresentation)root.Player.Visual).Registry;
                Assert.That(Vector3.Distance(hero.Anchors.LeftGrip.position, hero.Anchors.Head.position), Is.LessThan(.4f),
                    "The actual left hand contacts the head during the pain action.");
                CaptureMixedCameraFrame(root.CameraFollow.Camera, "Test Room attack - Left hand on head");
            });
            root.Tick(1.05f);
            Assert.That(root.RoomAttackPhase, Is.EqualTo(CombatRoomSequencePhase.HeadPain));
            AssertRoomTestFreeze(heroRoot, opponents, heads, bullet, impacts);
            root.Tick(.06f);
            yield return null; // The terminal pain sample must reach LateUpdate before input is returned.
            Assert.That(root.RoomAttackPhase, Is.EqualTo(CombatRoomSequencePhase.Laugh));
            AssertRoomTestRestored();
            Assert.That(root.Hero.ReceivedImpactCount, Is.EqualTo(1));
            Assert.That(room.RoomLaughRecoveryActive, Is.True);
            Assert.That(root.Hero.RoomHeadPainActive, Is.False);
            Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Ready), "Visible head pain consumes the hit recovery before control returns.");
            AudioSource laugh = RoomTestLaughSource(room);
            Assert.That(laugh.clip.length, Is.EqualTo(5f).Within(.02f));
            root.SendMessage("OnApplicationFocus", false);
            float laughHeld = root.RoomAttackPhaseElapsed;
            root.Tick(.5f);
            Assert.That(root.RoomAttackPhaseElapsed, Is.EqualTo(laughHeld));
            root.SendMessage("OnApplicationFocus", true);
            root.Tick(.8f);
            yield return AtCombatPresentation(() => CaptureMixedCameraFrame(root.CameraFollow.Camera, "Test Room attack - Laughing recovery"));
            root.Hero.ResetActor(heroRoot + Vector3.back * 14f, Vector3.forward);
            Vector3 laughingRoot = room.transform.position;
            root.Tick(4.1f);
            Assert.That(root.RoomAttackPhase, Is.EqualTo(CombatRoomSequencePhase.Laugh));
            Assert.That(Vector3.Distance(room.transform.position, laughingRoot), Is.LessThan(.001f));
            root.Tick(.11f);
            Assert.That(room.RoomLaughRecoveryActive, Is.False);
            Assert.That(laugh.isPlaying, Is.False);

            PlaceRoomAttackTestPair(room);
            yield return AdvanceRoomTestToPhase(CombatRoomSequencePhase.Laugh);
            CombatHurtboxes.Hit chestHit = AuthoredSurfaceContact(room, CombatRoomAssetProvider.Bank, "GEO_Torso");
            room.ReceiveProjectile(root.Hero, 2, chestHit, -chestHit.Normal * CombatProjectilePool.MuzzleSpeed);
            root.Tick(.05f);
            Assert.That(room.RoomAttackPoseActive, Is.False, "A new hit interrupts the exposed laughing enemy.");
            Assert.That(laugh.isPlaying, Is.False);
            AssertRoomTestRestored();

            PlaceRoomAttackTestPair(room);
            yield return AdvanceRoomTestToPhase(CombatRoomSequencePhase.SeatedHero);
            root.ResetRound();
            Assert.That(root.RoomAttackPhase, Is.EqualTo(CombatRoomSequencePhase.Idle));
            Assert.That(root.RoomAttackTableauVisible, Is.False);
            Assert.That(room.RoomAttackPoseActive, Is.False);
            AssertRoomTestRestored();
            PlaceRoomAttackTestPair(room);
            yield return AdvanceRoomTestToPhase(CombatRoomSequencePhase.PointHero);
            root.enabled = false;
            Assert.That(root.RoomAttackPhase, Is.EqualTo(CombatRoomSequencePhase.Idle));
            Assert.That(root.RoomAttackTableauVisible, Is.False);
            Assert.That(room.RoomAttackPoseActive, Is.False);
            AssertRoomTestRestored();
            root.enabled = true;

            PlaceRoomAttackTestPair(room);
            root.Hero.State.ReceiveUnarmedHit(root.Hero.State.Settings.MaxHealth - 9f, 0f, false);
            yield return AdvanceRoomTestToPhase(CombatRoomSequencePhase.SeatedHero);
            yield return AdvanceRoomTestToPhase(CombatRoomSequencePhase.Idle);
            Assert.That(root.Hero.State.IsDefeated, Is.True, "A lethal Room hit enters the ordinary defeat lifecycle.");
            Assert.That(root.Hero.RoomHeadPainActive, Is.False);
            Assert.That(root.RoomAttackTableauVisible, Is.False);
            AssertRoomTestRestored();
            LogAssert.NoUnexpectedReceived();
        }

        private void PlaceRoomAttackTestPair(CombatActor room)
        {
            root.ResetRound();
            Vector3 ground = Vector3.up * PlayerFactory.GroundedRootOffset;
            root.Hero.ResetActor(ground, Vector3.forward);
            room.ResetActor(ground + Vector3.forward * (1.25f * room.PresentationScale + room.AdditionalArmReach), Vector3.back);
            foreach (CombatActor actor in root.Opponents)
                if (actor != room && !actor.IsCrane) actor.ResetActor(ground + Vector3.right * 12f, Vector3.left);
            root.CameraFollow.Snap();
            Physics.SyncTransforms();
        }

        private IEnumerator AdvanceRoomTestToPhase(CombatRoomSequencePhase phase)
        {
            for (int step = 0; step < 500 && root.RoomAttackPhase != phase; step++)
            {
                root.Tick(.05f);
                if ((root.RoomAttackPhase == CombatRoomSequencePhase.HeadPain && root.RoomAttackPhaseElapsed >= 2f) || step % 40 == 39)
                    yield return null;
            }
            Assert.That(root.RoomAttackPhase, Is.EqualTo(phase), "The real Room pursuit/sequence did not reach " + phase + ".");
        }

        /// <summary>
        /// Samples the actual presentation at 60 Hz, including phase boundaries
        /// and the frame after fixed-camera ownership is returned. The bounded
        /// 12 Hz captures retain the real lighting/post-processing for motion review.
        /// </summary>
        private IEnumerator TraceRoomCameraToPhase(CombatRoomSequencePhase phase, string journey)
        {
            const float stepSeconds = 1f / 60f;
            Camera camera = root.CameraFollow.Camera;
            Pose previous = new Pose(camera.transform.position, camera.transform.rotation);
            float previousFov = camera.fieldOfView;
            CombatRoomSequencePhase previousPhase = root.RoomAttackPhase;
            Vector3 previousHead = RoomTestHeadProjection(camera);
            int captured = 0;
            yield return AtCombatPresentation(() => CaptureMixedCameraFrame(camera,
                "Test Room attack - " + journey + " - Frame " + (++captured).ToString("D2")));
            previous = new Pose(camera.transform.position, camera.transform.rotation);
            previousFov = camera.fieldOfView;
            previousHead = RoomTestHeadProjection(camera);
            for (int step = 0; step < 600 && root.RoomAttackPhase != phase; step++)
            {
                root.Tick(stepSeconds);
                bool sampleCapture = step % 5 == 4 || root.RoomAttackPhase != previousPhase;
                yield return AtCombatPresentation(() =>
                {
                    AssertRoomCameraFrameContinuity(camera, previous, previousFov, previousPhase, previousHead);
                    if (sampleCapture) CaptureMixedCameraFrame(camera,
                        "Test Room attack - " + journey + " - Frame " + (++captured).ToString("D2"));
                });
                previous = new Pose(camera.transform.position, camera.transform.rotation);
                previousFov = camera.fieldOfView;
                previousPhase = root.RoomAttackPhase;
                previousHead = RoomTestHeadProjection(camera);
            }
            Assert.That(root.RoomAttackPhase, Is.EqualTo(phase), "The smooth Room camera did not reach " + phase + ".");
            yield return AtCombatPresentation(() => AssertRoomCameraFrameContinuity(camera,
                previous, previousFov, previousPhase, previousHead));
        }

        private Vector3 RoomTestHeadProjection(Camera camera) => root.RoomAttackHeroImage == null
            ? Vector3.back : camera.WorldToViewportPoint(root.RoomAttackHeroImage.Anchors.Head.position);

        private void AssertRoomCameraFrameContinuity(Camera camera, Pose previous, float previousFov,
            CombatRoomSequencePhase previousPhase, Vector3 previousHead)
        {
            if (previousPhase == CombatRoomSequencePhase.Idle && root.RoomAttackPhase == CombatRoomSequencePhase.Idle) return;
            string boundary = previousPhase + " → " + root.RoomAttackPhase;
            Assert.That(Vector3.Distance(previous.position, camera.transform.position), Is.LessThan(.2f),
                "There must be no one-frame camera position cut during " + boundary + ".");
            Assert.That(Quaternion.Angle(previous.rotation, camera.transform.rotation), Is.LessThan(8f),
                "There must be no one-frame camera rotation cut during " + boundary + ".");
            Assert.That(Mathf.Abs(previousFov - camera.fieldOfView), Is.LessThan(3f),
                "The lens must change continuously during " + boundary + ".");
            Vector3 currentHead = RoomTestHeadProjection(camera);
            if (previousPhase != root.RoomAttackPhase && RoomTestProjectionOnScreen(previousHead) && RoomTestProjectionOnScreen(currentHead))
                Assert.That(new Vector2((previousHead.x - currentHead.x) * 1280f,
                    (previousHead.y - currentHead.y) * 720f).magnitude, Is.LessThan(20f),
                    "The seated hero must not jump across the screen at " + boundary + ".");
        }

        private static bool RoomTestProjectionOnScreen(Vector3 point) => point.z > .001f &&
            point.x > 0f && point.x < 1f && point.y > 0f && point.y < 1f;

        private void AssertRoomTestSeatedFootContact(Player3DAssetRegistry image, SkinnedMeshRenderer originalCorridor)
        {
            Transform stage = image.transform.parent;
            float pelvisHeight = stage.InverseTransformPoint(image.Anchors.Pelvis.position).y;
            int plaster = Array.IndexOf(originalCorridor.sharedMaterials, CombatRoomAssetProvider.LoadBodyMaterial("plaster"));
            Assert.That(plaster, Is.GreaterThanOrEqualTo(0));
            var scratch = new Mesh { name = "Test Room seated foot contact" };
            try
            {
                // Preserve the imported rig's scale, as the production sole probe
                // does, then measure in normalized stage metres.
                originalCorridor.BakeMesh(scratch, true);
                Vector3[] floorVertices = scratch.vertices;
                for (int i = 0; i < floorVertices.Length; i++)
                    floorVertices[i] = stage.InverseTransformPoint(originalCorridor.transform.TransformPoint(floorVertices[i]));
                int[] plasterTriangles = scratch.GetTriangles(plaster);
                var floorTriangles = new List<int>();
                for (int i = 0; i < plasterTriangles.Length; i += 3)
                {
                    Vector3 a = floorVertices[plasterTriangles[i]], b = floorVertices[plasterTriangles[i + 1]],
                        c = floorVertices[plasterTriangles[i + 2]];
                    Vector3 normal = Vector3.Cross(b - a, c - a);
                    if (Mathf.Abs(normal.y) < normal.magnitude * .5f || Mathf.Max(a.y, Mathf.Max(b.y, c.y)) > pelvisHeight) continue;
                    floorTriangles.Add(plasterTriangles[i]);
                    floorTriangles.Add(plasterTriangles[i + 1]);
                    floorTriangles.Add(plasterTriangles[i + 2]);
                }
                Assert.That(floorTriangles, Is.Not.Empty, "The actual skinned plaster must provide the floor and raised thresholds.");
                var floor = new List<HeadContactSurface> { new HeadContactSurface
                    { Name = originalCorridor.name, Vertices = floorVertices, Triangles = floorTriangles.ToArray() } };
                var minimum = new[] { float.PositiveInfinity, float.PositiveInfinity };
                var points = new int[2];
                foreach (SkinnedMeshRenderer skin in image.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!skin.enabled || !skin.gameObject.activeInHierarchy ||
                        !(skin.name.StartsWith("CLO_Boot", StringComparison.Ordinal) || skin.name.StartsWith("GEO_Foot", StringComparison.Ordinal))) continue;
                    int side = skin.name.EndsWith(".L", StringComparison.Ordinal) ? 0 :
                        skin.name.EndsWith(".R", StringComparison.Ordinal) ? 1 : -1;
                    if (side < 0) continue;
                    skin.BakeMesh(scratch, true);
                    foreach (Vector3 vertex in scratch.vertices)
                    {
                        Vector3 point = stage.InverseTransformPoint(skin.transform.TransformPoint(vertex));
                        Assert.That(IndependentHeadRaycast(floor, new Ray(new Vector3(point.x, pelvisHeight + .05f, point.z), Vector3.down),
                            out Vector3 support), Is.True, "Every visible " + skin.name + " vertex needs an actual corridor surface beneath it.");
                        minimum[side] = Mathf.Min(minimum[side], point.y - support.y);
                        points[side]++;
                    }
                }
                for (int side = 0; side < 2; side++)
                {
                    string context = (side == 0 ? "Left" : "Right") + " seated boot at " + root.RoomAttackPhaseElapsed.ToString("F2") + " s";
                    Assert.That(points[side], Is.GreaterThan(0), context + " must use the rendered production footwear.");
                    Assert.That(minimum[side], Is.GreaterThanOrEqualTo(-.005f), context + " must not penetrate the real floor or nested-frame threshold.");
                    Assert.That(minimum[side], Is.LessThanOrEqualTo(.015f), context + " must remain planted instead of floating above the room.");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(scratch); }
        }

        private void AssertRoomTestFreeze(Vector3 heroRoot, Vector3[] opponents, Quaternion[] heads, Vector3 bullet, int impacts)
        {
            Assert.That(Vector3.Distance(root.Hero.transform.position, heroRoot), Is.LessThan(.01f), "The actual hero stays in the arena.");
            Assert.That(root.Projectiles.LastPosition, Is.EqualTo(bullet), "An in-flight projectile stays frozen throughout the cinematic.");
            Assert.That(root.Projectiles.ActiveCount, Is.EqualTo(1));
            Assert.That(root.Projectiles.ImpactCount, Is.EqualTo(impacts));
            for (int i = 1; i < opponents.Length; i++)
            {
                Assert.That(root.Opponents[i].transform.position, Is.EqualTo(opponents[i]));
                Assert.That(Quaternion.Angle(FindAnatomicalBone(root.Opponents[i], "head").rotation, heads[i]), Is.LessThan(.01f));
            }
        }

        private void AssertRoomTestRestored()
        {
            Assert.That(root.RoomAttackCinematicActive, Is.False);
            Assert.That(root.Player.Motor.InputEnabled, Is.True);
            Assert.That(root.CameraFollow.FixedPoseActive, Is.False);
            Assert.That(root.CameraFollow.OrbitInputEnabled, Is.True);
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
            Assert.That(GameInput.CanRead(GameInputContext.Gameplay), Is.True);
        }

        private void AssertRoomTestInFrame(Vector3 point)
        {
            Vector3 screen = root.CameraFollow.Camera.WorldToViewportPoint(point);
            Assert.That(screen.z, Is.GreaterThan(0f));
            Assert.That(screen.x, Is.InRange(.02f, .98f));
            Assert.That(screen.y, Is.InRange(.02f, .98f));
        }

        private static AudioSource RoomTestLaughSource(CombatActor room)
        {
            foreach (AudioSource source in room.GetComponentsInChildren<AudioSource>(true))
                if (source.name == "Room laughter") return source;
            Assert.Fail("Room must retain its own spatial laughter voice.");
            return null;
        }
    }
}
