using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditorInternal;
#endif
using Object = UnityEngine.Object;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class CombatStoredSecondaryMotionAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            if (!Array.Exists(Environment.GetCommandLineArgs(), value => value == "-combatStoredSecondaryMotionProof")) return;
            Type.GetType("BarPromenade.Editor.SecondaryMotionBakeAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BakeCombatStoredAnimationSet", BindingFlags.Static | BindingFlags.Public).Invoke(null, null);
#endif
        }
    }

    public sealed class CombatRoomAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            bool existing = Array.Exists(Environment.GetCommandLineArgs(), value => value == "-combatExistingAssets");
            Type.GetType("BarPromenade.Editor.CombatRoomAssetSetup, BarPromenade.Editor", true)
                .GetMethod(existing ? "ValidateImportedOrThrow" : "BuildOrThrow", BindingFlags.Static | BindingFlags.Public).Invoke(null, null);
#endif
            new CombatRoomAttackAssetsSetup().Setup();
        }
    }

    public sealed class CombatImprintAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            bool existing = Array.Exists(Environment.GetCommandLineArgs(), value => value == "-combatExistingAssets");
            Type.GetType("BarPromenade.Editor.CombatImprintAssetSetup, BarPromenade.Editor", true)
                .GetMethod(existing ? "ValidateImportedOrThrow" : "BuildOrThrow", BindingFlags.Static | BindingFlags.Public).Invoke(null, null);
#endif
        }
    }

    public sealed class CombatMixedEndlessAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
            if (!Array.Exists(Environment.GetCommandLineArgs(), value => value == "-combatExistingAssets"))
                new CombatCraneAssetsSetup().Setup();
            new CombatRoomAssetsSetup().Setup();
        }
    }

    public sealed class CombatLaboratoryAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
            new CombatMixedEndlessAssetsSetup().Setup();
            new CombatImprintAssetsSetup().Setup();
            new CombatLongExposureAssetsSetup().Setup();
        }
    }

    public sealed class CombatLongExposureAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            bool existing = Array.Exists(Environment.GetCommandLineArgs(), value => value == "-combatExistingAssets");
            Type.GetType("BarPromenade.Editor.CombatLongExposureAssetSetup, BarPromenade.Editor", true)
                .GetMethod(existing ? "ValidateImportedOrThrow" : "BuildOrThrow", BindingFlags.Static | BindingFlags.Public).Invoke(null, null);
#endif
        }
    }

    public sealed partial class CombatTestPlayModeTests
    {
        [UnityTest]
        [PrebuildSetup(typeof(CombatRoomAssetsSetup))]
        public IEnumerator Range_RoomBodyKeepsAuthoredSurfacesContactDamageAndReset()
        { yield return VerifyAuthoredBodyLifecycle(CombatEnemyKind.Room); }

        [UnityTest]
        [PrebuildSetup(typeof(CombatLaboratoryAssetsSetup))]
        public IEnumerator Range_ImprintBodyKeepsConcaveSurfacesContactDamageAndReset()
        { yield return VerifyAuthoredBodyLifecycle(CombatEnemyKind.Imprint); }

        [UnityTest]
        [PrebuildSetup(typeof(CombatLaboratoryAssetsSetup))]
        public IEnumerator Range_LongExposureBodyKeepsFusedSurfacesContactDamageAndReset()
        { yield return VerifyAuthoredBodyLifecycle(CombatEnemyKind.LongExposure); }

        private static CombatAuthoredBodyBank AuthoredBodyBank(CombatEnemyKind kind) => kind switch
        {
            CombatEnemyKind.Room => CombatRoomAssetProvider.Bank,
            CombatEnemyKind.Imprint => CombatImprintAssetProvider.Bank,
            CombatEnemyKind.LongExposure => CombatLongExposureAssetProvider.Bank,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        private IEnumerator VerifyAuthoredBodyLifecycle(CombatEnemyKind kind)
        {
            CombatAuthoredBodyBank bank = AuthoredBodyBank(kind);
            string identity = DefaultNpcPopulation.CombatTestAuthoredEnemyId(kind, 0);
            const int authoredSlot = 0;
            bool laboratory = kind != CombatEnemyKind.Room;
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            CombatTestRoot.AuthoredEnemyKindForTests = laboratory ? null : kind;
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            var settings = new CombatEncounterSettings(3, .1f, .1f, .15f, .15f);
            Assert.That(CombatTestStartService.TryStart(new CombatTestSelection(
                new CombatLoadoutSelection(CombatWeaponId.Pistol),
                laboratory ? CombatTestMode.Laboratory : CombatTestMode.Endless, settings, laboratoryEnemy: kind)), Is.True);
            yield return AwaitSelectedCombatRange();
            Assert.That(root.IsLaboratory, Is.EqualTo(laboratory));
            if (laboratory) Assert.That(root.WarehousePlan, Is.Null);
            else Assert.That(root.WarehousePlan, Is.Not.Null);
            Assert.That(root.Opponents.Count, Is.EqualTo(laboratory ? 1 : 3));
            Assert.That(CombatEncounterSettings.Default.MaximumLiveOpponents, Is.EqualTo(3));
            Assert.That(CombatEncounterSettings.OpponentCapacity, Is.EqualTo(5));
            CombatActor room = root.Opponents[authoredSlot];
            foreach (CombatActor actor in root.Opponents)
                Assert.That(actor.EnemyKind, Is.Not.EqualTo(CombatEnemyKind.Zombie));
            if (!laboratory)
            {
                Assert.That(root.Opponents[1].IsDocker, Is.True);
                Assert.That(root.Opponents[2].IsCrane, Is.True);
            }
            Assert.That(room.EnemyKind, Is.EqualTo(kind));
            Assert.That(room.IsAuthoredEnemy && room.IsZombie && room.ZombieAwake, Is.True);
            Assert.That(room.IsDocker || room.IsCrane, Is.False);
            Assert.That(room.State.Settings, Is.SameAs(CombatActor.OrdinaryZombieCombatSettings));
            Assert.That(room.GetComponent<CombatZombieLoad>(), Is.Null);
            Assert.That(room.GetComponentInChildren<CombatZombieAppearance>(true), Is.Null);
            Assert.That(bank.LoadBodyAtlas(), Is.Not.SameAs(CombatZombieAssetProvider.LoadFace(0)));
            Assert.That(DefaultNpcPopulation.GetCharacter(identity).ModelId,
                Is.EqualTo(DefaultNpcCatalog.OrdinaryWorker));
            Assert.That(room.GetComponentInChildren<DefaultNpcAppearance>(true).AppearanceKey,
                Is.EqualTo(identity));

            Vector3 ground = Vector3.up * PlayerFactory.GroundedRootOffset;
            room.ResetActor(ground, Vector3.forward);
            AssertAuthoredBodyAppearance(room, bank, true);
            if (room.IsRoom) AssertRoomScaleAndHeadContacts(room);
            if (room.IsLongExposure) AssertAuthoredTrailingProjectileContact(room, bank);
            if (room.IsRoom)
            {
                AssertRoomAuthoredArmSkin();
                foreach (string clip in new[] { CombatZombieAssetProvider.OrdinaryWalkClip,
                    CombatZombieAssetProvider.OrdinaryAttackRightClip, CombatZombieAssetProvider.OrdinaryAttackLeftClip })
                {
                    room.SampleNpcClip(CombatZombieAssetProvider.LoadOrdinaryClip(clip), .5f);
                    AssertRoomArmPose(room);
                    room.SampleNpcClip(CombatZombieAssetProvider.LoadOrdinaryClip(clip), .5f);
                    AssertRoomArmPose(room);
                }
                room.ResetActor(ground, Vector3.forward);
            }
            yield return CaptureAuthoredBodyAppearance(room, bank);
            if (room.IsRoom) yield return CaptureRoomHeroScaleComparison(room, bank);

            int graphBuilds = room.NpcAnimationGraphBuilds;
            // Room's camera-owned attack has its own focused lifecycle proof.
            // The other authored bodies retain the ordinary physical palm sweep.
            if (!room.IsRoom)
            {
                root.Hero.ResetActor(ground, Vector3.forward);
                foreach (CombatActor actor in root.Opponents)
                    if (actor != room && !actor.IsCrane)
                        actor.ResetActor(ground + Vector3.right * 12f, Vector3.back);
                room.ResetActor(ground + Vector3.forward * (1.25f * room.PresentationScale + room.AdditionalArmReach), Vector3.back);
                for (int step = 0; step < 360 && root.Hero.ReceivedImpactCount == 0; step++)
                    root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Hero.ReceivedImpactCount, Is.EqualTo(1));
                Assert.That(root.Hero.LastImpact.Source, Is.SameAs(room));
                Assert.That(root.Hero.LastImpact.Kind, Is.EqualTo(CombatImpactKind.Unarmed));
                Assert.That(room.ActiveClipName, Does.StartWith("ZombieOrdinary"));
            }

            room.ResetActor(ground, Vector3.forward);
            yield return AimAuthoredInspectionHero(room, bank, room.transform.position + Vector3.up * 1.2f);
            if (kind != CombatEnemyKind.Room)
            {
                CombatHurtboxes.Hit palmHit = AuthoredSurfaceContact(room, bank, "GEO_HandPalm.L", BodyDamageRegion.LeftHand);
                room.ReceiveProjectile(root.Hero, 201, palmHit, -palmHit.Normal * CombatProjectilePool.MuzzleSpeed);
                Assert.That(room.BodyDamage.TissueLoss(BodyDamageRegion.LeftHand, palmHit.DamagePatch), Is.GreaterThan(0f));
                Assert.That(root.BloodEffects.TryGetProjectileWound(room, 0, out _, out _), Is.True);
                var palmInspection = new GameObject("Test " + bank.Name + " palm inspection light");
                palmInspection.transform.SetParent(root.CameraFollow.Camera.transform, false);
                Light palmFill = palmInspection.AddComponent<Light>();
                palmFill.type = LightType.Point; palmFill.intensity = .5f; palmFill.range = 3f; palmFill.shadows = LightShadows.None;
                try { yield return CapturePistolWoundNearView(room, "Test " + bank.Name + " enemy - Damaged palm"); }
                finally { Object.Destroy(palmInspection); }
                for (int shot = 1; shot < 3; shot++)
                    room.ReceiveProjectile(root.Hero, 201 + shot, palmHit, -palmHit.Normal * CombatProjectilePool.MuzzleSpeed);
                Assert.That(room.BodyDamage.TissueLoss(BodyDamageRegion.LeftHand, palmHit.DamagePatch), Is.GreaterThanOrEqualTo(.25f));
                CaptureBodyDestruction(room, "Test " + bank.Name + " enemy - Replaced palm source surface", palmHit.Normal);
            }
            else
            {
                CombatHurtboxes.Hit fingertip = AuthoredSurfaceContact(room, bank, "GEO_HandFinger1.L", BodyDamageRegion.LeftHand, distalHand: true);
                Vector3 fromTip = fingertip.Point + fingertip.Normal * .08f, toTip = fingertip.Point - fingertip.Normal * .025f;
                Assert.That(room.Hurtboxes.SweepProjectile(fromTip, toTip, CombatProjectilePool.Radius, -fingertip.Normal, out var extendedHandHit), Is.True);
                Assert.That(extendedHandHit.Part, Is.EqualTo(Player3DAnatomicalPart.LeftHand));
                Assert.That(Vector3.Distance(extendedHandHit.Point, fingertip.Point), Is.LessThan(.015f));
                Transform longHand = FindAnatomicalBone(room, "hand.L");
                Vector3 handAxis = longHand.TransformVector(Vector3.up).normalized;
                float oldTip = Vector3.Dot(room.RoomHandTipWorld(true) - longHand.position, handAxis) / CombatRoomAssetProvider.HandLengthScale;
                Assert.That(Vector3.Dot(extendedHandHit.Point - longHand.position, handAxis), Is.GreaterThan(oldTip + .01f));
                room.ReceiveProjectile(root.Hero, 190, extendedHandHit, -extendedHandHit.Normal * CombatProjectilePool.MuzzleSpeed);
                Assert.That(room.BodyDamage.TissueLoss(BodyDamageRegion.LeftHand, extendedHandHit.DamagePatch), Is.GreaterThan(0f));
                Assert.That(root.BloodEffects.TryGetProjectileWound(room, 0, out _, out _), Is.True);
                var handInspection = new GameObject("Test Room long hand inspection light");
                handInspection.transform.SetParent(root.CameraFollow.Camera.transform, false);
                Light handFill = handInspection.AddComponent<Light>();
                handFill.type = LightType.Point; handFill.intensity = .5f; handFill.range = 3f; handFill.shadows = LightShadows.None;
                try { yield return CapturePistolWoundNearView(room, "Test Room enemy - Long fingertip wound"); }
                finally { Object.Destroy(handInspection); }
                room.ResetActor(ground, Vector3.forward);
                AssertRoomArmPose(room);
                CombatHurtboxes.Hit forearmHit = AuthoredSurfaceContact(room, bank, "GEO_Arm.L", BodyDamageRegion.LeftForearm);
                room.ReceiveProjectile(root.Hero, 201, forearmHit, -forearmHit.Normal * CombatProjectilePool.MuzzleSpeed);
                Assert.That(room.BodyDamage.TissueLoss(BodyDamageRegion.LeftForearm, forearmHit.DamagePatch), Is.GreaterThan(0f));
                Assert.That(root.BloodEffects.TryGetProjectileWound(room, 0, out _, out _), Is.True);
                // One ordinary projectile removes .09 of this protected patch;
                // the third reaches the existing .25 exterior-replacement boundary.
                for (int shot = 1; shot < 3; shot++)
                    room.ReceiveProjectile(root.Hero, 201 + shot, forearmHit, -forearmHit.Normal * CombatProjectilePool.MuzzleSpeed);
                Assert.That(room.BodyDamage.TissueLoss(BodyDamageRegion.LeftForearm, forearmHit.DamagePatch), Is.GreaterThanOrEqualTo(.25f));
                CaptureBodyDestruction(room, "Test Room enemy - Damaged long forearm", forearmHit.Normal);
            }
            AssertAuthoredBodyAppearance(room, bank, false);
            AssertAuthoredBodyDerivativeSources(room, bank);
            room.gameObject.SetActive(false);
            room.gameObject.SetActive(true);
            yield return null;
            // Actor disable deliberately resets damage for pooled reuse.
            Assert.That(TotalBodyTissueLoss(room), Is.Zero);
            AssertAuthoredBodyAppearance(room, bank, true);
            if (room.IsRoom) AssertRoomScaleAndHeadContacts(room);
            room.ResetActor(ground, Vector3.forward);
            AssertAuthoredBodyAppearance(room, bank, true);

            // Source-derived tissue must preserve the authored concavity when
            // the torso transfers to its damaged surface set.
            CombatHurtboxes.Hit torsoHit = AuthoredSurfaceContact(room, bank, "GEO_Torso");
            int torsoContacts = room.ReceivedImpactCount;
            room.ReceiveProjectile(root.Hero, 210, torsoHit, -torsoHit.Normal * CombatProjectilePool.MuzzleSpeed);
            Assert.That(room.ReceivedImpactCount, Is.EqualTo(torsoContacts + 1));
            Assert.That(room.LastImpact.BodyRegion, Is.EqualTo(torsoHit.DamageRegion));
            Assert.That(TotalBodyTissueLoss(room), Is.GreaterThan(0f));
            Assert.That(room.State.IsDefeated, Is.False);
            AssertAuthoredBodyAppearance(room, bank, false);
            AssertAuthoredBodyDerivativeSources(room, bank);
            yield return CaptureAuthoredBodyAppearance(room, bank, true);
            room.ResetActor(ground, Vector3.forward);
            yield return AimAuthoredInspectionHero(room, bank, FindAnatomicalBone(room, "head").position);

            // Every authored head owns its wound and destruction banks.
            var inspectionLight = new GameObject("Test " + bank.Name + " damage inspection light");
            inspectionLight.transform.SetParent(root.CameraFollow.Camera.transform, false);
            Light light = inspectionLight.AddComponent<Light>();
            light.type = LightType.Point; light.intensity = .5f; light.range = 4f; light.shadows = LightShadows.None;
            try { yield return VerifyMixedEnemyHeadContacts(room, "Test " + bank.Name + " enemy"); }
            finally { Object.Destroy(inspectionLight); }
            AssertAuthoredBodyAppearance(room, bank, false);
            AssertAuthoredBodyDerivativeSources(room, bank, onlyVisible: false);
            root.ResetRound();
            Assert.That(root.Opponents[authoredSlot], Is.SameAs(room));
            Assert.That(room.NpcAnimationGraphBuilds, Is.EqualTo(graphBuilds));
            Assert.That(room.ReceivedImpactCount, Is.Zero);
            Assert.That(root.BloodEffects.ProjectileWoundCountFor(room), Is.Zero);
            AssertAuthoredBodyAppearance(room, bank, true);
            if (room.IsRoom) AssertRoomScaleAndHeadContacts(room);
            if (room.IsLongExposure) AssertAuthoredTrailingProjectileContact(room, bank);
            LogAssert.NoUnexpectedReceived();
        }

        [Serializable] private sealed class RoomScaleManifest { public RoomScaleProportions proportions; }
        [Serializable] private sealed class RoomScaleProportions
        { public float presentation_scale, head_scale, upper_arm_length_scale, forearm_length_scale, hand_length_scale; public float[] head_anchor_blender; public string space, hand_longitudinal_axis; }

        private static void AssertRoomScaleAndHeadContacts(CombatActor room)
        {
            const float scale = 1.2f;
            Assert.That(CombatRoomAssetProvider.PresentationScale, Is.EqualTo(scale));
            Assert.That(room.transform.localScale, Is.EqualTo(Vector3.one));
            Transform[] wrappers = Array.FindAll(room.GetComponentsInChildren<Transform>(true), part => part.name == "Room body scale");
            Assert.That(wrappers, Has.Length.EqualTo(1));
            Assert.That(wrappers[0].parent, Is.SameAs(room.transform));
            Assert.That(wrappers[0].localScale, Is.EqualTo(Vector3.one * scale));
            Assert.That(room.DamageRigRoot.IsChildOf(wrappers[0]), Is.True);
            Assert.That(room.Body.radius, Is.EqualTo(.32f * scale).Within(.00001f));
            Assert.That(room.Body.stepOffset, Is.EqualTo(PlayerFactory.StepOffset * scale).Within(.00001f));
            Assert.That(room.Body.center.y, Is.EqualTo(room.Body.height * .5f).Within(.00001f));
            Assert.That(room.PreviewZombieWalkDistance(CombatZombieAssetProvider.OrdinaryWalkCycleSeconds),
                Is.EqualTo(CombatZombieAssetProvider.OrdinaryWalkCycleDistance * scale).Within(.00001f));
            RoomScaleProportions proportions = JsonUtility.FromJson<RoomScaleManifest>(
                Resources.Load<TextAsset>("Combat/RoomBody").text).proportions;
            Assert.That(proportions, Is.Not.Null);
            Assert.That(proportions.presentation_scale, Is.EqualTo(scale));
            Assert.That(proportions.head_scale, Is.EqualTo(1.35f));
            Assert.That(proportions.upper_arm_length_scale, Is.EqualTo(CombatRoomAssetProvider.UpperArmLengthScale));
            Assert.That(proportions.forearm_length_scale, Is.EqualTo(CombatRoomAssetProvider.ForearmLengthScale));
            Assert.That(proportions.hand_length_scale, Is.EqualTo(CombatRoomAssetProvider.HandLengthScale));
            Assert.That(proportions.hand_longitudinal_axis, Is.EqualTo("local_y"));
            Assert.That(proportions.space, Is.EqualTo("blender_metres"));
            Assert.That(proportions.head_anchor_blender, Is.EqualTo(new[] { 0f, -.009f, 1.490f }).Within(.00001f));
            var nativeHead = Array.Find(CombatRoomAssetProvider.LoadBodyManifest().meshes, part => part.name == "GEO_Head");
            Assert.That(nativeHead.bounds_max[0] - nativeHead.bounds_min[0], Is.EqualTo(.114f * 1.35f).Within(.00002f));
            Assert.That(nativeHead.bounds_max[1] - nativeHead.bounds_min[1], Is.EqualTo(.259f * 1.35f).Within(.00002f));
            Assert.That(room.Body.height, Is.GreaterThanOrEqualTo(nativeHead.bounds_max[1] * scale));
            AssertRoomArmPose(room);
            foreach (CombatActor other in room.transform.parent.GetComponentsInChildren<CombatActor>(true))
                if (other != room && !other.IsRoom)
                {
                    Assert.That(other.transform.localScale, Is.EqualTo(Vector3.one));
                    Assert.That(Array.Exists(other.GetComponentsInChildren<Transform>(true), part => part.name == "Room body scale"), Is.False);
                }

            room.CaptureContactPose();
            CapsuleCollider capsule = null;
            foreach (var shape in room.Ragdoll.PhysicsController.AnatomicalColliders)
                if (shape.Value == Player3DAnatomicalPart.Head) capsule = shape.Key as CapsuleCollider;
            Assert.That(capsule, Is.Not.Null);
            SkinnedMeshRenderer skin = Array.Find(room.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true), part => part.name == "GEO_Head");
            var baked = new Mesh { name = "Test Room enlarged head contact" };
            try
            {
                skin.BakeMesh(baked, true);
                Vector3[] vertices = baked.vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] = skin.transform.TransformPoint(vertices[i]);
                    Vector3 offset = capsule.transform.InverseTransformPoint(vertices[i]) - capsule.center;
                    float halfLine = Mathf.Max(0f, capsule.height * .5f - capsule.radius);
                    offset[capsule.direction] -= Mathf.Clamp(offset[capsule.direction], -halfLine, halfLine);
                    Assert.That(offset.magnitude, Is.LessThanOrEqualTo(capsule.radius + .001f),
                        "The enlarged actual head must remain inside its melee and ragdoll capsule.");
                }
                int[] triangles = baked.triangles;
                float mostLateral = float.NegativeInfinity;
                Vector3 point = default, normal = default;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                    Vector3 faceNormal = Vector3.Cross(b - a, c - a);
                    if (faceNormal.sqrMagnitude < 1e-12f) continue;
                    Vector3 centre = (a + b + c) / 3f;
                    float lateral = capsule.transform.InverseTransformPoint(centre).x;
                    if (lateral <= mostLateral) continue;
                    mostLateral = lateral; point = centre; normal = faceNormal.normalized;
                }
                Assert.That(float.IsFinite(mostLateral), Is.True);
                Vector3 from = point + normal * .12f, to = point - normal * .02f;
                Assert.That(room.Hurtboxes.SweepProjectile(from, to, CombatProjectilePool.Radius, -normal, out var bullet), Is.True);
                Assert.That(bullet.Part, Is.EqualTo(Player3DAnatomicalPart.Head));
                Assert.That(Vector3.Distance(bullet.Point, point), Is.LessThan(.015f));
                Assert.That(room.Hurtboxes.SweepSphere(from, to, .01f, -normal, out var melee), Is.True);
                Assert.That(melee.Part, Is.EqualTo(Player3DAnatomicalPart.Head));
                Assert.That(room.ReceivedImpactCount, Is.Zero);
            }
            finally { Object.Destroy(baked); }
        }

        private static void AssertRoomArmPose(CombatActor room, bool measureSleeves = true)
        {
            Assert.That(room.AdditionalArmReach, Is.GreaterThan(.3f));
            foreach (string side in new[] { "L", "R" })
            {
                Transform upper = FindAnatomicalBone(room, "upper_arm." + side);
                Transform elbow = FindAnatomicalBone(room, "forearm." + side);
                Transform hand = FindAnatomicalBone(room, "hand." + side);
                SkinnedMeshRenderer reference = CombatRoomAssetProvider.LoadBodyRenderer("GEO_Arm." + side);
                Transform nativeElbow = Array.Find(reference.bones, bone => bone.name == elbow.name);
                Transform nativeHand = Array.Find(reference.bones, bone => bone.name == hand.name);
                Assert.That(elbow.localPosition.magnitude, Is.EqualTo(nativeElbow.localPosition.magnitude *
                    CombatRoomAssetProvider.UpperArmLengthScale).Within(.0001f));
                Assert.That(hand.localPosition.magnitude, Is.EqualTo(nativeHand.localPosition.magnitude *
                    CombatRoomAssetProvider.ForearmLengthScale).Within(.0001f));
                Vector3 expectedHandScale = nativeHand.localScale;
                expectedHandScale.y *= CombatRoomAssetProvider.HandLengthScale;
                Assert.That(Vector3.Distance(hand.localScale, expectedHandScale), Is.LessThan(.00001f));
                if (!measureSleeves) continue;
                foreach (var shape in room.Ragdoll.PhysicsController.AnatomicalColliders)
                {
                    bool upperPart = shape.Value == (side == "L" ? Player3DAnatomicalPart.LeftUpperArm : Player3DAnatomicalPart.RightUpperArm);
                    bool lowerPart = shape.Value == (side == "L" ? Player3DAnatomicalPart.LeftForearm : Player3DAnatomicalPart.RightForearm);
                    if (!upperPart && !lowerPart) continue;
                    var capsule = (CapsuleCollider)shape.Key;
                    Vector3 start = upperPart ? upper.position : elbow.position, end = upperPart ? elbow.position : hand.position;
                    Assert.That(Vector3.Distance(capsule.transform.TransformPoint(capsule.center), (start + end) * .5f), Is.LessThan(.01f));
                    Assert.That(capsule.height, Is.GreaterThanOrEqualTo(Vector3.Distance(start, end) - .001f));
                }
                SkinnedMeshRenderer[] skins = room.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                SkinnedMeshRenderer sleeve = Array.Find(skins, skin => skin.name == "GEO_Arm." + side);
                SkinnedMeshRenderer palm = Array.Find(skins, skin => skin.name == "GEO_HandPalm." + side);
                NpcHandPose hands = room.GetComponentInChildren<NpcHandPose>(true);
                Vector3 palmCentre = hands.CylinderCentre(side == "L"), tipCentre = room.RoomDistalHandContactPoint(side == "L");
                Assert.That(Vector3.Distance(palmCentre, tipCentre), Is.LessThanOrEqualTo(.13f * CombatRoomAssetProvider.PresentationScale),
                    "Palm and distal contact spheres must overlap without increasing hand width.");
                var scratch = new Mesh { name = "Test Room sleeve and wrist continuity" };
                try
                {
                    Vector3[] sleevePoints = BakeAuthoredWorldVertices(sleeve, scratch);
                    Vector3[] palmPoints = BakeAuthoredWorldVertices(palm, scratch);
                    Bounds baselinePalmBounds = default, posedPalmBounds = default;
                    Vector3 savedHandScale = hand.localScale;
                    Vector3[] baselinePalmPoints;
                    Matrix4x4 baselineFrame;
                    Vector3 metresPerUnit;
                    try
                    {
                        Vector3 baselineScale = savedHandScale;
                        baselineScale.y /= CombatRoomAssetProvider.HandLengthScale;
                        hand.localScale = baselineScale;
                        baselineFrame = hand.worldToLocalMatrix;
                        metresPerUnit = new Vector3(hand.TransformVector(Vector3.right).magnitude,
                            hand.TransformVector(Vector3.up).magnitude, hand.TransformVector(Vector3.forward).magnitude);
                        // Same live mesh, pose, grip and parent matrices. Only
                        // the requested hand-local Y scale differs between bakes.
                        baselinePalmPoints = BakeAuthoredWorldVertices(palm, scratch);
                    }
                    finally { hand.localScale = savedHandScale; }
                    for (int i = 0; i < baselinePalmPoints.Length; i++)
                    {
                        Vector3 point = Vector3.Scale(baselineFrame.MultiplyPoint3x4(baselinePalmPoints[i]), metresPerUnit);
                        if (i == 0) baselinePalmBounds = new Bounds(point, Vector3.zero); else baselinePalmBounds.Encapsulate(point);
                    }
                    for (int i = 0; i < palmPoints.Length; i++)
                    {
                        Vector3 point = Vector3.Scale(baselineFrame.MultiplyPoint3x4(palmPoints[i]), metresPerUnit);
                        if (i == 0) posedPalmBounds = new Bounds(point, Vector3.zero); else posedPalmBounds.Encapsulate(point);
                    }
                    Vector3 expectedPalmSize = Vector3.Scale(baselinePalmBounds.size,
                        new Vector3(1f, CombatRoomAssetProvider.HandLengthScale, 1f));
                    Assert.That(Vector3.Distance(posedPalmBounds.size, expectedPalmSize), Is.LessThan(.001f),
                        "The actual palm must grow only along its longitudinal axis; its transverse width stays at the accepted body scale.");
                    BoneWeight[] weights = sleeve.sharedMesh.boneWeights;
                    int handIndex = Array.FindIndex(sleeve.bones, bone => bone == hand);
                    Assert.That(handIndex, Is.GreaterThanOrEqualTo(0));
                    int wristVertices = 0;
                    for (int i = 0; i < weights.Length; i++)
                    {
                        BoneWeight weight = weights[i];
                        if (weight.boneIndex0 != handIndex || weight.weight0 < .999f) continue;
                        float nearest = float.PositiveInfinity;
                        foreach (Vector3 point in palmPoints) nearest = Mathf.Min(nearest, Vector3.Distance(sleevePoints[i], point));
                        Assert.That(nearest, Is.LessThan(.035f), "The long source sleeve must end at the actual retained hand.");
                        wristVertices++;
                    }
                    Assert.That(wristVertices, Is.GreaterThanOrEqualTo(12));
                }
                finally { Object.Destroy(scratch); }
            }
        }

        private static void AssertRoomAuthoredArmSkin()
        {
            GameObject body = Resources.Load<GameObject>("CombatGore/BodyRoom");
            Assert.That(body, Is.Not.Null);
            foreach (string side in new[] { "L", "R" })
            {
                string prefix = side == "L" ? "FleshRegion6Patch" : "FleshRegion9Patch";
                bool retainedEndpointBlend = false;
                foreach (SkinnedMeshRenderer piece in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!piece.name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    int forearm = Array.FindIndex(piece.bones, bone => bone.name == "forearm." + side);
                    int hand = Array.FindIndex(piece.bones, bone => bone.name == "hand." + side);
                    Assert.That(forearm, Is.GreaterThanOrEqualTo(0));
                    Assert.That(hand, Is.GreaterThanOrEqualTo(0));
                    foreach (BoneWeight row in piece.sharedMesh.boneWeights)
                    {
                        bool elbowBound = row.boneIndex0 == forearm && row.weight0 > .01f ||
                            row.boneIndex1 == forearm && row.weight1 > .01f || row.boneIndex2 == forearm && row.weight2 > .01f ||
                            row.boneIndex3 == forearm && row.weight3 > .01f;
                        bool wristBound = row.boneIndex0 == hand && row.weight0 > .01f ||
                            row.boneIndex1 == hand && row.weight1 > .01f || row.boneIndex2 == hand && row.weight2 > .01f ||
                            row.boneIndex3 == hand && row.weight3 > .01f;
                        retainedEndpointBlend |= elbowBound && wristBound;
                    }
                }
                Assert.That(retainedEndpointBlend, Is.True,
                    "Internal forearm tissue must retain the actual sleeve's elbow-to-wrist skin field as the real chain lengthens.");
            }
        }

        private static void AssertAuthoredTrailingProjectileContact(CombatActor actor, CombatAuthoredBodyBank bank)
        {
            Assert.That(actor.ReceivedImpactCount, Is.Zero);
            Assert.That(TotalBodyTissueLoss(actor), Is.Zero);
            CombatHurtboxes.Hit source = AuthoredSurfaceContact(actor, bank, "GEO_Torso", outsideBaseline: true);
            Vector3 from = source.Point + source.Normal * .1f, to = source.Point - source.Normal * .1f;
            Assert.That(actor.Hurtboxes.MeasureSweepSurfaceGap(from, to, CombatProjectilePool.Radius, out var gap), Is.True);
            Assert.That(gap.Gap, Is.GreaterThan(.005f), "The complete shot must miss the retained human boxes and capsules.");
            Assert.That(actor.Hurtboxes.SweepProjectile(from, to, CombatProjectilePool.Radius, -source.Normal, out var actual), Is.True,
                "An intact authored trailing surface must accept the first projectile before any damage activates derived anatomy.");
            Assert.That(actual.DamageRegion is BodyDamageRegion.Chest or BodyDamageRegion.Abdomen, Is.True);
            Assert.That(Vector3.Distance(actual.Point, source.Point), Is.LessThan(.015f),
                "The query must hit the visible source triangle, not a conservative bridge over empty space.");
            Assert.That(actor.ReceivedImpactCount, Is.Zero);
            Assert.That(TotalBodyTissueLoss(actor), Is.Zero);
        }

        private static CombatHurtboxes.Hit AuthoredSurfaceContact(CombatActor room, CombatAuthoredBodyBank bank,
            string sourceName, BodyDamageRegion? requiredRegion = null, bool outsideBaseline = false, bool distalHand = false)
        {
            room.CaptureContactPose();
            SkinnedMeshRenderer torso = Array.Find(room.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true),
                skin => skin.name == sourceName);
            Assert.That(torso, Is.Not.Null);
            Assert.That(torso.enabled, Is.True);
            string[] roles = Array.Find(bank.LoadBodyManifest().meshes, part => part.name == torso.name).surfaces;
            string preferred = bank.Name == "Room" ? "cloth" : bank.Name == "Imprint" ? "chalk" : "flesh";
            int clothSlot = Array.IndexOf(roles, preferred);
            if (clothSlot < 0) clothSlot = 0;
            var baked = new Mesh { name = "Test " + bank.Name + " posed source contact" };
            try
            {
                torso.BakeMesh(baked, true);
                Vector3[] vertices = baked.vertices;
                int[] triangles = outsideBaseline ? baked.triangles : baked.GetTriangles(clothSlot);
                CombatHurtboxes.Hit distal = default; float furthest = float.NegativeInfinity;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = torso.transform.TransformPoint(vertices[i]);
                for (int offset = 0; offset + 2 < triangles.Length; offset += 3)
                {
                    Vector3 a = vertices[triangles[offset]], b = vertices[triangles[offset + 1]], c = vertices[triangles[offset + 2]];
                    Vector3 normal = Vector3.Cross(b - a, c - a);
                    if (normal.sqrMagnitude < 1e-12f) continue;
                    Vector3 point = (a + b + c) / 3f;
                    BodyDamageRegion region = BodyDamageRegion.Head; int patch = -1;
                    // The separate head bank owns head patches. Its source does
                    // not appear in the sixteen non-head body region partitions.
                    bool head = sourceName == "GEO_Head" && requiredRegion == BodyDamageRegion.Head;
                    if (!head && (!CombatBodyDestruction.TryClassifySurface(room, torso, point, out region, out patch) ||
                        (requiredRegion.HasValue ? region != requiredRegion.Value : region is not (BodyDamageRegion.Chest or BodyDamageRegion.Abdomen)))) continue;
                    normal.Normalize();
                    if (distalHand && point.y + normal.y * .42f < room.transform.position.y - PlayerFactory.GroundedRootOffset + .1f)
                        continue; // Keep the existing wound close-view camera above the supporting floor.
                    if (outsideBaseline)
                    {
                        if (!room.Hurtboxes.MeasureSweepSurfaceGap(point, point, 0f, out var pointGap) || pointGap.Gap <= .115f ||
                            !room.Hurtboxes.MeasureSweepSurfaceGap(point + normal * .1f, point - normal * .1f,
                                CombatProjectilePool.Radius, out var sweepGap) || sweepGap.Gap <= .005f) continue;
                    }
                    Transform bone = FindAnatomicalBone(room, CombatBodyAnatomy.BoneName(region));
                    var contact = new CombatHurtboxes.Hit(point, normal, -normal, 0f,
                        new MeleeHitLocation(CombatBodyAnatomy.CombatRegion(region), MeleeHitSide.Front), CombatBodyAnatomy.ToPart(region),
                        bone.InverseTransformPoint(point), bone.InverseTransformDirection(-normal).normalized, region, patch);
                    if (!distalHand) return contact;
                    float along = bone.InverseTransformPoint(point).y;
                    if (along > furthest) { furthest = along; distal = contact; }
                }
                if (distalHand && float.IsFinite(furthest)) return distal;
                Assert.Fail("The visible authored source needs an anatomically classified triangle: " + sourceName);
                return default;
            }
            finally { Object.Destroy(baked); }
        }

        private static void AssertAuthoredBodyAppearance(CombatActor room, CombatAuthoredBodyBank bank, bool intact)
        {
            Assert.That(bank.Name switch
            {
                "Room" => room.GetComponentInChildren<CombatRoomAppearance>(true) != null,
                "Imprint" => room.GetComponentInChildren<CombatImprintAppearance>(true) != null,
                "LongExposure" => room.GetComponentInChildren<CombatLongExposureAppearance>(true) != null,
                _ => false
            }, Is.True);
            var joints = room.GetComponentInChildren<CharacterJointDeformation>(true);
            Assert.That(joints == null || !joints.enabled, Is.True,
                "The independent sculpt cannot run the worker's cached joint-shape indices.");
            var manifest = bank.LoadBodyManifest();
            Texture2D atlas = bank.LoadBodyAtlas();
            Texture2D response = Resources.Load<Texture2D>("Combat/" + bank.Name + "MaterialResponse");
            Assert.That(atlas.width, Is.EqualTo(2048));
            Assert.That(response.width, Is.EqualTo(1024));
            SkinnedMeshRenderer[] skins = room.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var properties = new MaterialPropertyBlock();
            bool corridorSeen = false;
            foreach (CombatAuthoredBodyBank.BodyMesh part in manifest.meshes)
            {
                SkinnedMeshRenderer skin = Array.Find(skins, candidate => candidate.name == part.name);
                Assert.That(skin, Is.Not.Null, part.name);
                Assert.That(skin.sharedMesh, Is.SameAs(bank.LoadBodyRenderer(part.name).sharedMesh), part.name);
                Assert.That(skin.sharedMaterials, Has.Length.EqualTo(part.surfaces.Length));
                for (int slot = 0; slot < part.surfaces.Length; slot++)
                {
                    Material material = skin.sharedMaterials[slot];
                    Assert.That(material, Is.SameAs(bank.LoadBodyMaterial(part.surfaces[slot])));
                    Assert.That(material.GetTexture("_WarehouseMaterialResponse"), Is.SameAs(response));
                    corridorSeen |= part.visible && part.name == "GEO_Torso" && (bank.Name == "LongExposure" ||
                        part.surfaces[slot] == (bank.Name == "Room" ? "plaster" : "chalk"));
                }
                if (intact || !part.visible)
                    Assert.That(skin.enabled && skin.gameObject.activeInHierarchy, Is.EqualTo(part.visible), part.name);
                skin.GetPropertyBlock(properties);
                Assert.That(properties.GetTexture("_BaseMap"), Is.SameAs(atlas), part.name);
                properties.Clear();
            }
            Assert.That(corridorSeen, Is.True, "The authored torso must retain its structural surface role.");
            foreach (SkinnedMeshRenderer skin in skins)
                if (skin.name.StartsWith("CLO_", StringComparison.Ordinal) || skin.name.StartsWith("HAIR_", StringComparison.Ordinal))
                    Assert.That(skin.enabled && skin.gameObject.activeInHierarchy, Is.False,
                        "Worker garments or hair cannot return through wardrobe restoration.");
        }

        private static void AssertAuthoredBodyDerivativeSources(CombatActor room, CombatAuthoredBodyBank bank, bool onlyVisible = true)
        {
            Texture2D atlas = bank.LoadBodyAtlas();
            var properties = new MaterialPropertyBlock();
            int exteriorCount = 0;
            foreach (SkinnedMeshRenderer skin in room.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if ((onlyVisible && (!skin.enabled || !skin.gameObject.activeInHierarchy)) ||
                    !skin.name.Contains("__", StringComparison.Ordinal)) continue;
                foreach (Material material in skin.sharedMaterials)
                {
                    if (material == null || material.shader != Resources.Load<Shader>("Shaders/WarehouseSurface")) continue;
                    skin.GetPropertyBlock(properties);
                    Assert.That(properties.GetTexture("_BaseMap") ?? material.GetTexture("_BaseMap"), Is.SameAs(atlas), skin.name);
                    properties.Clear(); exteriorCount++;
                }
            }
            Assert.That(exteriorCount, Is.GreaterThan(0), "Damaged exterior pieces must retain their authored body source surfaces.");
        }

        private IEnumerator AimAuthoredInspectionHero(CombatActor actor, CombatAuthoredBodyBank bank, Vector3 focus)
        {
            SkinnedMeshRenderer source = bank.LoadBodyRenderer("GEO_Torso");
            int index = Array.FindIndex(source.bones, bone => bone != null && bone.name == "chest");
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            Matrix4x4 bind = source.transform.localToWorldMatrix * source.sharedMesh.bindposes[index].inverse;
            Vector3 forward = Vector3.ProjectOnPlane(FindAnatomicalBone(actor, "chest").TransformVector(
                bind.inverse.MultiplyVector(source.transform.root.TransformVector(Vector3.back))), Vector3.up).normalized;
            Vector3 floor = actor.transform.position - Vector3.up * PlayerFactory.GroundedRootOffset;
            Vector3 lampPosition = floor + forward * 3.2f + (bank.Name == "Imprint" ? Vector3.Cross(Vector3.up, forward) * .65f : Vector3.zero);
            PlaceCraneHero(lampPosition, Vector3.ProjectOnPlane(actor.transform.position - lampPosition, Vector3.up).normalized);
            Vector3 delta = focus - (root.Hero.transform.position + Vector3.up * 1.5f);
            float targetPitch = -Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg;
            root.CameraFollow.RotatePitch(Mathf.Clamp(targetPitch, root.CameraFollow.MinimumOrbitPitch,
                root.CameraFollow.MaximumOrbitPitch) - root.CameraFollow.TargetOrbitPitch);
            root.CameraFollow.Snap();
            for (int frame = 0; frame < 4; frame++)
            { root.Hero.AdvanceSimulation(.1f); root.Hero.Present(); yield return null; }
        }

        [Serializable] private sealed class AuthoredHeadCaptureManifest
        { public ImprintCaptureForm imprint_negative_form; public LongExposureCaptureForm long_exposure_form; }
        [Serializable] private sealed class ImprintCaptureForm
        { public string probes_space; public float[] front_axis_blender, head_face_center_blender; }
        [Serializable] private sealed class LongExposureCaptureForm
        { public string space; public float[] head_focus_blender, head_front_blender; }

        private static void AuthoredHeadFrame(CombatActor actor, CombatAuthoredBodyBank bank, out Vector3 center, out Vector3 forward)
        {
            TextAsset manifest = Resources.Load<TextAsset>("Combat/" + bank.Name + "Body");
            AuthoredHeadCaptureManifest capture = JsonUtility.FromJson<AuthoredHeadCaptureManifest>(manifest.text);
            string space;
            float[] point, axis;
            if (bank.Name == "Imprint")
            {
                Assert.That(capture.imprint_negative_form, Is.Not.Null);
                space = capture.imprint_negative_form.probes_space;
                point = capture.imprint_negative_form.head_face_center_blender;
                axis = capture.imprint_negative_form.front_axis_blender;
            }
            else
            {
                Assert.That(capture.long_exposure_form, Is.Not.Null);
                space = capture.long_exposure_form.space;
                point = capture.long_exposure_form.head_focus_blender;
                axis = capture.long_exposure_form.head_front_blender;
            }
            Assert.That(space, Is.EqualTo("blender_metres"));
            Assert.That(point, Has.Length.EqualTo(3));
            Assert.That(axis, Has.Length.EqualTo(3));
            SkinnedMeshRenderer source = bank.LoadBodyRenderer("GEO_Head");
            int index = Array.FindIndex(source.bones, bone => bone != null && bone.name == "head");
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            Matrix4x4 bind = source.transform.localToWorldMatrix * source.sharedMesh.bindposes[index].inverse;
            Transform head = FindAnatomicalBone(actor, "head");
            center = head.TransformPoint(bind.inverse.MultiplyPoint3x4(new Vector3(point[0], point[2], point[1])));
            forward = head.TransformVector(bind.inverse.MultiplyVector(new Vector3(axis[0], axis[2], axis[1]))).normalized;
        }

        private static Vector3 AuthoredPosedSurfaceCenter(CombatActor actor, string name)
        {
            SkinnedMeshRenderer source = Array.Find(actor.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true),
                skin => skin.name == name);
            Assert.That(source, Is.Not.Null);
            var posed = new Mesh { name = "Test authored surface framing" };
            try
            {
                Vector3[] vertices = BakeAuthoredWorldVertices(source, posed);
                Bounds bounds = new Bounds(vertices[0], Vector3.zero);
                foreach (Vector3 point in vertices) bounds.Encapsulate(point);
                return bounds.center;
            }
            finally { Object.Destroy(posed); }
        }

        private static Vector3[] BakeAuthoredWorldVertices(SkinnedMeshRenderer skin, Mesh scratch)
        {
            // Compensate the imported renderer scale before applying its full
            // local-to-world frame, including the Room presentation wrapper.
            skin.BakeMesh(scratch, true);
            Vector3[] points = scratch.vertices;
            for (int i = 0; i < points.Length; i++) points[i] = skin.transform.TransformPoint(points[i]);
            return points;
        }

        private static Bounds AuthoredPosedBodyBounds(CombatActor actor)
        {
            Bounds bounds = default;
            bool measured = false;
            var scratch = new Mesh { name = "Test authored body framing" };
            try
            {
                foreach (SkinnedMeshRenderer skin in actor.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!skin.enabled || !skin.gameObject.activeInHierarchy || !skin.name.StartsWith("GEO_", StringComparison.Ordinal)) continue;
                    foreach (Vector3 point in BakeAuthoredWorldVertices(skin, scratch))
                    {
                        if (!measured) { bounds = new Bounds(point, Vector3.zero); measured = true; }
                        else bounds.Encapsulate(point);
                    }
                }
            }
            finally { Object.Destroy(scratch); }
            Assert.That(measured, Is.True);
            return bounds;
        }

        private IEnumerator CaptureRoomHeroScaleComparison(CombatActor room, CombatAuthoredBodyBank bank) =>
            CaptureAuthoredBodyAppearance(room, bank, heroComparison: true);

        private IEnumerator CaptureAuthoredBodyAppearance(CombatActor room, CombatAuthoredBodyBank bank,
            bool afterTorsoDamage = false, bool heroComparison = false)
        {
            Camera camera = root.CameraFollow.Camera;
            bool following = root.CameraFollow.enabled;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            Vector3 heroPosition = root.Hero.transform.position, heroForward = root.Hero.transform.forward;
            float pitch = root.CameraFollow.TargetOrbitPitch, yaw = camera.transform.eulerAngles.y;
            var flashlight = root.Hero.GetComponentInChildren<CombatChestFlashlight>(true);
            Assert.That(flashlight, Is.Not.Null);
            Assert.That(flashlight.SpotLight.enabled, Is.True);
            // Calibrate authored -Y / Unity -Z against the retained chest bind
            // frame, so the view follows the actual slouched model, not its capsule.
            SkinnedMeshRenderer source = bank.LoadBodyRenderer("GEO_Torso");
            int index = Array.FindIndex(source.bones, bone => bone != null && bone.name == "chest");
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            Matrix4x4 bind = source.transform.localToWorldMatrix * source.sharedMesh.bindposes[index].inverse;
            Vector3 localForward = bind.inverse.MultiplyVector(source.transform.root.TransformVector(Vector3.back));
            Vector3 forward = Vector3.ProjectOnPlane(FindAnatomicalBone(room, "chest").TransformVector(localForward), Vector3.up).normalized;
            Vector3 corridorCenter = FindAnatomicalBone(room, "chest").TransformPoint(
                bind.inverse.MultiplyPoint3x4(new Vector3(0f, 1.20f, -.182f)));
            if (bank.Name == "LongExposure") corridorCenter = AuthoredPosedSurfaceCenter(room, "GEO_Torso");
            room.CaptureContactPose();
            Assert.That(room.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head, out Vector3 headCenter, out _, out _), Is.True);
            Vector3 headForward = forward;
            if (bank.Name != "Room") AuthoredHeadFrame(room, bank, out headCenter, out headForward);
            Bounds roomBounds = room.IsRoom ? AuthoredPosedBodyBounds(room) : default;
            var inspection = new GameObject("Test " + bank.Name + " neutral inspection light");
            inspection.transform.SetParent(camera.transform, false);
            Light fill = inspection.AddComponent<Light>();
            fill.type = LightType.Point; fill.range = 4f; fill.intensity = .5f; fill.shadows = LightShadows.None;
            fill.enabled = false;
            try
            {
                foreach (int view in heroComparison ? new[] { 8 } : afterTorsoDamage ? new[] { 2, 3 } :
                    bank.Name != "Room" ? new[] { 0, 1, 4, 5, 6 } : new[] { 0, 1, 2, 3, 4, 5 })
                {
                    Vector3 viewForward = view >= 4 && view < 8 ? headForward : forward;
                    Vector3 direction = view == 1 || view == 5 ? Quaternion.AngleAxis(bank.Name == "Room" ? 70f : 90f, Vector3.up) * viewForward :
                        view == 4 ? Quaternion.AngleAxis(40f, Vector3.up) * viewForward : viewForward;
                    // Keep the lamp on the clear centre aisle while the camera
                    // orbits. A side-standing hero intersects the warehouse rack;
                    // sharing the camera position would also put it inside his coat.
                    Vector3 focus = view < 2 || view == 8 ? room.IsRoom ? roomBounds.center : room.transform.position + Vector3.up * .95f
                        : view >= 4 ? headCenter : corridorCenter;
                    yield return AimAuthoredInspectionHero(room, bank, focus);
                    if (view == 8)
                    {
                        Vector3 side = Vector3.Cross(Vector3.up, forward).normalized;
                        Vector3 floor = room.transform.position - Vector3.up * PlayerFactory.GroundedRootOffset;
                        PlaceCraneHero(floor + side * .95f, forward);
                        focus += side * .475f;
                        for (int frame = 0; frame < 4; frame++)
                        { root.Hero.AdvanceSimulation(.1f); root.Hero.Present(); yield return null; }
                    }
                    root.CameraFollow.enabled = false;
                    bool closeProfile = bank.Name != "Room" && view == 1;
                    bool fullBody = view < 2 || view == 8;
                    float bodyDistance = room.IsRoom ? Mathf.Max(2.8f, roomBounds.size.y * .62f / Mathf.Tan(24f * Mathf.Deg2Rad)) : closeProfile ? 2f : 2.8f;
                    Vector3 eye = focus + direction * (fullBody ? bodyDistance : 1.0f) + Vector3.up * .04f;
                    camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
                    camera.fieldOfView = fullBody ? (closeProfile ? 58f : 48f) : 34f;
                    fill.enabled = view == 3 || view == 6 || view == 8;
                    string headSubject = bank.Name == "Room" ? "Enlarged head" : bank.Name == "Imprint" ? "Concave face" : "Fused head sweep";
                    string name = "Test " + bank.Name + " enemy - " + (view == 0 ? "Front body with chest light" :
                        view == 8 ? "Hero and enemy scale comparison" : view == 1 ? "Profile body with chest light" : view == 4 ? headSubject + " three quarter with chest light" :
                        view == 5 ? headSubject + " profile with chest light" : view == 6 ? headSubject + " neutral inspection" :
                        view == 2 ? "Torso with chest light" : "Torso neutral inspection");
                    CaptureMixedCameraFrame(camera, name + (afterTorsoDamage ? " - After torso damage" : string.Empty));
                }
            }
            finally
            {
                Object.Destroy(inspection);
                root.Hero.ResetActor(heroPosition, heroForward);
                root.CameraFollow.RotateYaw(Mathf.DeltaAngle(camera.transform.eulerAngles.y, yaw));
                root.CameraFollow.RotatePitch(pitch - root.CameraFollow.TargetOrbitPitch);
                root.CameraFollow.Snap();
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fov; root.CameraFollow.enabled = following;
            }
        }

        [UnityTest]
        [PrebuildSetup(typeof(CombatMixedEndlessAssetsSetup))]
        public IEnumerator Range_MixedEndlessRetainsTwoHitWoundsPopulationAndFishGate()
        {
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            // Small real encounters exercise the reserved crane place and the
            // allowed warehouse roles before the default-size combat proof.
            foreach (int capacity in new[] { 1, 2, 5 })
                yield return VerifyMixedEndlessPoolAtCapacity(capacity);
            GameSessionState.BeginNewGame();
            CombatTestRoot.WarehouseSeedForTests = 0;
            CombatTestRoot.AuthoredEnemyKindForTests = null;
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            var settings = new CombatEncounterSettings(3, .1f, .1f, .15f, .15f);
            var selection = new CombatTestSelection(new CombatLoadoutSelection(CombatWeaponId.Pistol),
                CombatTestMode.Endless, settings);
            Assert.That(CombatTestStartService.TryStart(selection), Is.True);
            yield return AwaitSelectedCombatRange();
            // Close views inspect local geometry independently of the warehouse's
            // intentionally dark distant rooms. This light belongs only to the fixture.
            var inspectionLight = new GameObject("Test wound inspection light");
            inspectionLight.transform.SetParent(root.CameraFollow.Camera.transform, false);
            Light detailLight = inspectionLight.AddComponent<Light>();
            detailLight.type = LightType.Point;
            detailLight.range = 4f;
            detailLight.intensity = .5f;
            detailLight.shadows = LightShadows.None;
            Assert.That(root.WarehousePlan, Is.Not.Null);
            Assert.That(CombatEncounterSettings.Default.MaximumLiveOpponents, Is.EqualTo(3));
            Assert.That(root.Mode, Is.EqualTo(CombatTestMode.Endless));
            Assert.That(root.Opponents.Count, Is.EqualTo(3));
            Assert.That(root.LiveOpponentCount, Is.EqualTo(3), "Every initial role is present on entering the shared warehouse.");
            CombatActor authored = root.Opponents[0], docker = root.Opponents[1], crane = root.Opponents[2];
            AssertMixedEndlessLayout(3);
            CombatActor[] prepared = root.GetComponentsInChildren<CombatActor>(true);
            string[] identities = SnapshotAuthoredPoolIdentities(prepared);
            AssertPreparedEndlessPool(prepared, identities, 3);
            Assert.That(docker.EnemyKind, Is.EqualTo(CombatEnemyKind.Docker));
            Assert.That(crane.EnemyKind, Is.EqualTo(CombatEnemyKind.Crane));
            Assert.That(authored.IsZombie && authored.ZombieAwake, Is.True);
            Assert.That(authored.GetComponent<CombatZombieLoad>(), Is.Null, "The authored ground enemy has no borrowed cargo or disguise.");
            Assert.That(docker.IsDocker && docker.ZombieDormant, Is.True);
            CombatZombieLoad load = docker.GetComponent<CombatZombieLoad>();
            Assert.That(load, Is.Not.Null);
            Assert.That(crane.IsCrane && crane.CraneAttached, Is.True);
            Assert.That(crane.GetComponent<CombatZombieLoad>(), Is.Null);
            Assert.That(root.Sparring, Is.True);
            foreach (CombatActor actor in root.Opponents)
            {
                Assert.That(actor.State.Settings.IncomingDamageScale, Is.EqualTo(.5f));
                AssertMixedRegionalDamage(actor.State.Settings);
            }
            Assert.That(root.Hero.State.Settings.IncomingDamageScale, Is.EqualTo(1f), "Enemy endurance does not change incoming hero damage.");
            yield return CaptureMixedPopulationView("Test mixed encounter - Initial population");

            // Room's replacement attack is verified by its focused camera/HP lifecycle test.
            Vector3 ground = Vector3.up * PlayerFactory.GroundedRootOffset;
            root.ResetRound();
            authored = root.Opponents[0];
            authored.ResetActor(ground, Vector3.forward);
            yield return AimAuthoredInspectionHero(authored, CombatRoomAssetProvider.Bank,
                FindAnatomicalBone(authored, "head").position);

            // The first protected contact must already alter the retained wood,
            // even while the board and its owner are both still alive.
            docker.ResetActor(ground + Vector3.right * 2.5f, Vector3.forward);
            docker.SetZombieDormant();
            int boardIndex = LoaderRamBoard(load, 1);
            Ray boardRay = LoaderBoardRay(load, boardIndex, out _);
            int surfaces = root.Projectiles.SurfaceEffects.EmissionCount, holes = root.Projectiles.SurfaceEffects.HoleCount;
            FireLoaderProjectile(boardRay, new[] { root.Hero, docker }, 1);
            Assert.That(root.Projectiles.SurfaceEffects.EmissionCount, Is.EqualTo(surfaces + 1));
            Assert.That(root.Projectiles.SurfaceEffects.HoleCount, Is.EqualTo(holes + 1));
            Assert.That(load.IsBoardDetached(boardIndex), Is.False);
            Assert.That(docker.State.Health, Is.EqualTo(docker.State.Settings.MaxHealth));
            Assert.That(docker.ZombieRevealing, Is.True);
            yield return AdvanceMimicTo(docker, CombatZombieAssetProvider.RevealSeconds);
            CaptureMimicView(docker, "Test mixed encounter - First board damage");

            IFirearmState pistol = root.Hero.CarriedFirearm(CombatWeaponId.Pistol);
            SpendEndlessRound(pistol);
            int rounds = pistol.Rounds, reserves = pistol.ReserveRounds;
            root.Hero.State.ReceiveProjectileHit(13f);
            float heroHealth = root.Hero.State.Health;
            Assert.That(heroHealth, Is.EqualTo(root.Hero.State.Settings.MaxHealth - 13f));
            ApplyKnownBodyContact(docker, root.Hero, BodyDamageRegion.LeftForearm, 0, .1f, 90);
            float survivingTrauma = docker.BodyDamage.TissueLoss(BodyDamageRegion.LeftForearm, 0);

            yield return VerifyMixedEnemyHeadContacts(authored, "Test Room enemy", pauseOnFirst: true);
            root.Tick(0f);
            Assert.That(root.EndlessKills, Is.EqualTo(1));
            Assert.That(root.LiveOpponentCount, Is.EqualTo(2));
            Assert.That(root.RoundFinished || root.EndlessRunFinished, Is.False);
            Assert.That(root.WaitingForNextOpponent, Is.True);
            CombatActor preparedRoom = authored;
            root.CameraFollow.enabled = false;
            root.CameraFollow.Camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            // The terminal contact owns a short hit hold before the replacement
            // interval can advance; do not bypass the production clock.
            for (int step = 0; step < 120 && root.LiveOpponentCount != 3; step++) root.Tick(CombatTestRoot.SimulationStep);
            root.CameraFollow.enabled = true;
            Assert.That(root.LiveOpponentCount, Is.EqualTo(3));
            authored = root.Opponents[0];
            Assert.That(authored, Is.SameAs(preparedRoom), "A replacement reuses the prepared Room body.");
            AssertMixedEndlessLayout(3);
            AssertPreparedEndlessPool(prepared, identities, 3);
            AssertAuthoredBodyAppearance(authored, CombatRoomAssetProvider.Bank, true);
            Assert.That(authored.State.Health, Is.EqualTo(authored.State.Settings.MaxHealth));
            Assert.That(root.BloodEffects.ProjectileWoundCountFor(authored), Is.Zero);
            Assert.That(docker.BodyDamage.TissueLoss(BodyDamageRegion.LeftForearm, 0), Is.EqualTo(survivingTrauma));
            Assert.That(crane.CraneAttached && !crane.State.IsDefeated, Is.True);
            Assert.That(root.Hero.State.Health, Is.EqualTo(heroHealth));
            Assert.That(pistol.Rounds, Is.EqualTo(rounds));
            Assert.That(pistol.ReserveRounds, Is.EqualTo(reserves));

            yield return VerifyMixedEnemyHeadContacts(docker, "Test docker");
            root.Tick(0f);
            Assert.That(root.EndlessKills, Is.EqualTo(2));
            yield return VerifyMixedEnemyHeadContacts(crane, "Test crane operator");
            root.Tick(0f);
            Assert.That(root.EndlessKills, Is.EqualTo(3));
            Assert.That(root.CorpseCount, Is.LessThanOrEqualTo(settings.MaximumCorpses));
            Assert.That(root.RoundFinished || root.EndlessRunFinished, Is.False);

            root.ResetRound();
            Assert.That(root.EndlessKills, Is.Zero);
            Assert.That(root.LiveOpponentCount, Is.EqualTo(3));
            Assert.That(root.CorpseCount, Is.Zero);
            for (int i = 0; i < root.Opponents.Count; i++)
            {
                CombatActor actor = root.Opponents[i];
                Assert.That(actor.State.Health, Is.EqualTo(actor.State.Settings.MaxHealth));
                Assert.That(actor.ReceivedImpactCount, Is.Zero);
                Assert.That(root.BloodEffects.ProjectileWoundCountFor(actor), Is.Zero);
                Assert.That(actor.NpcAnimationGraphBuilds, Is.EqualTo(1), "Reset reuses the prepared animation graph.");
            }
            Assert.That(docker.ZombieDormant, Is.True);
            Assert.That(load.DetachedBoardCount, Is.Zero);
            Assert.That(root.Projectiles.SurfaceEffects.HoleCount, Is.Zero);
            Assert.That(crane.CraneAttached, Is.True);
            Assert.That(crane.CraneHookPhase, Is.EqualTo(CombatCraneHookPhase.Stowed));
            AssertPreparedEndlessPool(prepared, identities, 3);

            // A further unforced reset keeps only the warehouse roster and
            // reuses its prepared bodies rather than resurrecting lab variants.
            root.ResetRound();
            authored = root.Opponents[0];
            AssertMixedEndlessLayout(3);
            AssertPreparedEndlessPool(prepared, identities, 3);

            var fish = new[] { InventoryItemId.RottenFishTailCan, InventoryItemId.RottenFishBellyCan, InventoryItemId.RottenFishHeadCan };
            for (int i = 0; i < fish.Length; i++)
            {
                Assert.That(GameSessionState.TryCollectWorldItem(root.WarehousePlan.Refrigerators[i].SourceId, fish[i]), Is.True);
                Assert.That(root.EndlessSpawnsBlockedByFish, Is.EqualTo(i == fish.Length - 1));
            }
            Assert.That(root.LiveOpponentCount, Is.Zero, "Collecting the final fish source retires all living roles.");
            Assert.That(root.CorpseCount, Is.Zero);
            Assert.That(root.WarehouseCraneRemnantActive, Is.True);
            Assert.That(root.EndlessKills, Is.Zero);
            foreach (CombatActor actor in root.Opponents)
                Assert.That(actor.gameObject.activeInHierarchy, Is.False);
            root.Tick(.6f);
            Assert.That(root.EndlessKills, Is.Zero);
            Assert.That(root.LiveOpponentCount, Is.Zero);
            Assert.That(root.CorpseCount, Is.Zero);
            Assert.That(root.WarehouseCraneRemnantActive, Is.True);
            foreach (CombatActor actor in root.Opponents)
                Assert.That(actor.gameObject.activeInHierarchy, Is.False);
            Assert.That(root.WaitingForNextOpponent, Is.False, "The fish cutoff stops replacement of every role.");
            root.ResetRound();
            Assert.That(root.EndlessSpawnsBlockedByFish, Is.True);
            Assert.That(root.LiveOpponentCount, Is.Zero, "Reset cannot restore a mixed population after the finite warehouse cutoff.");
            Assert.That(root.CorpseCount, Is.Zero);
            Assert.That(root.EndlessKills, Is.Zero);
            Assert.That(root.ReturnToWeapons(), Is.True);
            yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneIds.MainMenu && !SceneTransitionService.IsTransitioning,
                "The mixed encounter did not return to preparation.");
            Assert.That(Object.FindAnyObjectByType<CombatActor>(), Is.Null);
            Assert.That(Object.FindAnyObjectByType<StartMenuRoot>().SelectedCombatSelection, Is.EqualTo(selection));
            Assert.That(PauseMenuController.IsAnyPaused, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        private IEnumerator VerifyMixedEndlessPoolAtCapacity(int capacity)
        {
            GameSessionState.BeginNewGame();
            CombatTestRoot.WarehouseSeedForTests = 0;
            CombatTestRoot.AuthoredEnemyKindForTests = null;
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            var settings = new CombatEncounterSettings(capacity, .1f, .1f, .15f, .15f);
            Assert.That(CombatTestStartService.TryStart(new CombatTestSelection(
                new CombatLoadoutSelection(CombatWeaponId.Pistol), CombatTestMode.Endless, settings)), Is.True);
            yield return AwaitSelectedCombatRange();
            AssertMixedEndlessLayout(capacity);
            CombatActor[] prepared = root.GetComponentsInChildren<CombatActor>(true);
            string[] identities = SnapshotAuthoredPoolIdentities(prepared);
            AssertPreparedEndlessPool(prepared, identities, capacity);
            int craneSlot = Mathf.Min(2, capacity - 1);
            CombatActor crane = root.Opponents[craneSlot];
            CombatActor docker = capacity >= 3 ? root.Opponents[1] : null;

            RefillAuthoredEndlessPool(capacity, crane, defeatCrane: capacity <= 2);
            foreach (CombatActor actor in root.Opponents)
                if (actor.IsRoom) AssertAuthoredBodyAppearance(actor, CombatRoomAssetProvider.Bank, true);
            AssertPreparedEndlessPool(prepared, identities, capacity);
            Assert.That(root.Opponents[craneSlot], Is.SameAs(crane));
            if (docker != null)
            {
                Assert.That(root.Opponents[1], Is.SameAs(docker));
                Assert.That(docker.State.Health, Is.EqualTo(docker.State.Settings.MaxHealth));
                Assert.That(docker.ZombieDormant, Is.True);
            }
            Assert.That(root.ReturnToWeapons(), Is.True);
            yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneIds.MainMenu && !SceneTransitionService.IsTransitioning,
                "The bounded pool encounter did not return to preparation.");
            Assert.That(Object.FindAnyObjectByType<CombatActor>(), Is.Null);
        }

        private void RefillAuthoredEndlessPool(int capacity, CombatActor crane, bool defeatCrane)
        {
            // At cap two the crane dies last, so its reservation must win over
            // an older authored corpse when the production interval advances.
            int missing = 0;
            foreach (CombatActor actor in root.Opponents)
                if (actor.IsAuthoredEnemy)
                {
                    FireMixedEnemyProjectile(actor, Player3DAnatomicalPart.Head);
                    FireMixedEnemyProjectile(actor, Player3DAnatomicalPart.Head);
                    Assert.That(actor.State.IsDefeated, Is.True);
                    missing++;
                }
            if (defeatCrane)
            {
                FireMixedEnemyProjectile(crane, Player3DAnatomicalPart.Head);
                FireMixedEnemyProjectile(crane, Player3DAnatomicalPart.Head);
                Assert.That(crane.State.IsDefeated, Is.True);
                missing++;
            }
            bool following = root.CameraFollow.enabled;
            Quaternion rotation = root.CameraFollow.Camera.transform.rotation;
            root.CameraFollow.enabled = false;
            root.CameraFollow.Camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            try
            {
                for (int replacement = 0; replacement < missing; replacement++)
                {
                    Assert.That(root.AdvanceEndlessInterval(.3f), Is.True,
                        "The production replacement interval must activate an already prepared body.");
                    if (defeatCrane && replacement == 0)
                    {
                        Assert.That(root.Opponents[Mathf.Min(2, capacity - 1)], Is.SameAs(crane));
                        Assert.That(crane.CraneAttached && !crane.State.IsDefeated, Is.True,
                            "The crane returns first even when an older wildcard corpse is available.");
                        Assert.That(root.LiveOpponentCount, Is.EqualTo(1));
                    }
                }
            }
            finally
            {
                root.CameraFollow.Camera.transform.rotation = rotation;
                root.CameraFollow.enabled = following;
            }
            AssertMixedEndlessLayout(capacity);
        }

        private void AssertMixedEndlessLayout(int capacity)
        {
            Assert.That(root.WarehousePlan, Is.Not.Null);
            Assert.That(root.Opponents.Count, Is.EqualTo(capacity));
            Assert.That(root.LiveOpponentCount, Is.EqualTo(capacity));
            Assert.That(root.HasCraneOpponent, Is.True);
            Assert.That(root.Opponents, Does.Contain(root.Opponent));
            int craneSlot = Mathf.Min(2, capacity - 1);
            for (int slot = 0; slot < capacity; slot++)
            {
                CombatActor actor = root.Opponents[slot];
                Assert.That(actor.gameObject.activeInHierarchy && !actor.State.IsDefeated, Is.True);
                Assert.That(actor.IsRoom || actor.IsDocker || actor.IsCrane, Is.True,
                    "Only Room, Docker and Crane belong to the warehouse population.");
                if (slot == craneSlot)
                    Assert.That(actor.IsCrane && actor.CraneAttached, Is.True);
                else if (slot == 1) Assert.That(actor.IsDocker, Is.True);
                else
                {
                    Assert.That(actor.IsRoom, Is.True);
                    Assert.That(actor.GetComponentInChildren<DefaultNpcAppearance>(true).AppearanceKey,
                        Is.EqualTo(DefaultNpcPopulation.CombatTestAuthoredEnemyId(actor.EnemyKind, slot)));
                }
            }
        }

        private static string[] SnapshotAuthoredPoolIdentities(CombatActor[] prepared)
        {
            var identities = new string[prepared.Length];
            var unique = new HashSet<string>();
            for (int i = 0; i < prepared.Length; i++)
            {
                CombatActor actor = prepared[i];
                if (!actor.IsAuthoredEnemy) continue;
                identities[i] = actor.GetComponentInChildren<DefaultNpcAppearance>(true).AppearanceKey;
                Assert.That(identities[i], Is.Not.Null.And.Not.Empty);
                Assert.That(unique.Add(identities[i]), Is.True, "Every prewarmed authored clone owns a permanent identity.");
                Assert.That(DefaultNpcPopulation.GetCharacter(identities[i]).ModelId, Is.EqualTo(DefaultNpcCatalog.OrdinaryWorker));
            }
            return identities;
        }

        private void AssertPreparedEndlessPool(CombatActor[] prepared, string[] identities, int capacity)
        {
            int roomSlots = capacity == 1 ? 0 : capacity <= 3 ? 1 : capacity - 2;
            Assert.That(prepared.Length, Is.EqualTo(capacity + 1), "The main range prepares one allowed body per live slot plus the hero.");
            Assert.That(root.GetComponentsInChildren<CombatActor>(true), Is.EquivalentTo(prepared),
                "Refill and reset reuse the entire prewarmed actor pool.");
            int rooms = 0;
            var journalIds = new HashSet<int>();
            for (int i = 0; i < prepared.Length; i++)
            {
                CombatActor actor = prepared[i];
                if (actor.IsHero) continue;
                Assert.That(actor.IsRoom || actor.IsDocker || actor.IsCrane, Is.True,
                    "The warehouse must not warm hidden Zombie, Imprint or Long Exposure bodies.");
                Assert.That(journalIds.Add(actor.JournalActorId), Is.True, "Every prepared enemy keeps a distinct journal identity.");
                Assert.That(actor.NpcAnimationGraphBuilds, Is.EqualTo(1), "Every allowed body keeps one prepared animation graph.");
                if (!actor.IsAuthoredEnemy) continue;
                rooms += actor.IsRoom ? 1 : 0;
                if (actor.IsRoom)
                {
                    Assert.That(actor.transform.localScale, Is.EqualTo(Vector3.one));
                    Assert.That(actor.Body.radius, Is.EqualTo(.32f * CombatRoomAssetProvider.PresentationScale).Within(.00001f));
                    AssertRoomArmPose(actor, measureSleeves: false);
                }
                Assert.That(actor.GetComponentInChildren<DefaultNpcAppearance>(true).AppearanceKey, Is.EqualTo(identities[i]));
                Assert.That(actor.GetComponentInChildren<CombatZombieAppearance>(true), Is.Null);
            }
            Assert.That(rooms, Is.EqualTo(roomSlots));
        }

        private static void AssertMixedRegionalDamage(MeleeCombatSettings settings)
        {
            foreach (var contact in new[] { (MeleeBodyRegion.Torso, 12.5f), (MeleeBodyRegion.LeftArm, 6.25f),
                (MeleeBodyRegion.RightLeg, 9.375f) })
            {
                var victim = new MeleeCombatant(settings);
                victim.ReceiveProjectileHit(25f, new MeleeHitLocation(contact.Item1, MeleeHitSide.Front));
                Assert.That(settings.MaxHealth - victim.Health, Is.EqualTo(contact.Item2).Within(.001f));
            }
            var torso = new MeleeCombatant(settings);
            for (int shot = 1; shot <= 8; shot++)
            {
                torso.ReceiveProjectileHit(25f, new MeleeHitLocation(MeleeBodyRegion.Torso, MeleeHitSide.Front));
                Assert.That(torso.IsDefeated, Is.EqualTo(shot == 8), "Eight separate torso wounds scale the former four-hit health budget.");
            }
        }

        private IEnumerator VerifyMixedRepeatedArmWound(CombatActor actor)
        {
            actor.CaptureContactPose();
            Vector3 centre = FindAnatomicalBone(actor, "forearm.L").position;
            CombatHurtboxes.Hit hit = default;
            bool exposed = false;
            foreach (Vector3 outward in new[] { -actor.transform.right, actor.transform.forward, -actor.transform.forward, Vector3.up })
            {
                if (!actor.Hurtboxes.SweepProjectile(centre + outward * .4f, centre - outward * .4f,
                    CombatProjectilePool.Radius, -outward, out hit) || hit.Part != Player3DAnatomicalPart.LeftForearm) continue;
                exposed = true;
                break;
            }
            Assert.That(exposed, Is.True, "The repeat proof needs the ordinary zombie's exposed original forearm.");
            float spreadAtFourth = 0f;
            for (int shot = 1; shot <= 6; shot++)
            {
                // Reuse the same confirmed anatomical entry without advancing
                // simulation, so every contact addresses the same surface hole.
                actor.ReceiveProjectile(root.Hero, 200 + shot, hit, -hit.Normal * CombatProjectilePool.MuzzleSpeed);
                Assert.That(actor.State.Health, Is.EqualTo(actor.State.Settings.MaxHealth - Mathf.Min(35f, 6.25f * shot)).Within(.001f));
                Assert.That(actor.State.IsDefeated, Is.False);
                Assert.That(actor.BodyDamage.IsAttached(BodyDamageRegion.LeftForearm), Is.True);
                Assert.That(root.BloodEffects.TryGetProjectilePresentation(actor, 0, out var wound), Is.True);
                Assert.That(wound.HitCount, Is.EqualTo(shot), "A repeat must keep counting after the former four-contact plateau.");
                if (shot == 1) yield return CapturePistolWoundNearView(actor, "Test ordinary zombie - First arm wound");
                if (shot == 4) spreadAtFourth = wound.Spread;
                if (shot == 6)
                {
                    Assert.That(wound.Spread, Is.GreaterThan(spreadAtFourth));
                    yield return CapturePistolWoundNearView(actor, "Test ordinary zombie - Repeated arm wound");
                }
            }
        }

        private IEnumerator VerifyMixedEnemyHeadContacts(CombatActor actor, string subject, bool pauseOnFirst = false)
        {
            yield return CaptureMixedEnemyHead(actor, subject + " - Before damage");
            FireMixedEnemyProjectile(actor, Player3DAnatomicalPart.Head);
            Assert.That(actor.State.Health, Is.EqualTo(actor.State.Settings.MaxHealth * .5f).Within(.001f), subject);
            Assert.That(actor.State.IsDefeated || actor.IsRagdollActive, Is.False, subject + ": one headshot leaves a live continuous rig.");
            Assert.That(root.HeadEffects.DetachedSectorCountFor(actor), Is.Zero, subject + ": the first shot cannot bypass endurance with terminal geometry.");
            Assert.That(root.BloodEffects.TryGetProjectilePresentation(actor, 0, out var firstWound), Is.True, subject);
            Assert.That(firstWound.HitCount, Is.EqualTo(1));
            Assert.That(root.BloodEffects.TryGetProjectileWound(actor, 0, out _, out _), Is.True);
            yield return CapturePistolWoundNearView(actor, subject + " - First head wound");
            if (pauseOnFirst)
            {
                Vector3 heldHook = root.Opponents[2].CraneHookPosition;
                float heldHealth = actor.State.Health;
                Assert.That(root.PauseMenu.Open(), Is.True);
                root.Tick(1f);
                yield return null;
                Assert.That(actor.State.Health, Is.EqualTo(heldHealth));
                Assert.That(root.Opponents[2].CraneHookPosition, Is.EqualTo(heldHook));
                Assert.That(root.BloodEffects.TryGetProjectilePresentation(actor, 0, out var paused), Is.True);
                Assert.That(paused.AgeSeconds, Is.EqualTo(firstWound.AgeSeconds));
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause did not release the mixed encounter.");
            }
            FireMixedEnemyProjectile(actor, Player3DAnatomicalPart.Head);
            Assert.That(actor.State.IsDefeated && actor.IsRagdollActive, Is.True, subject + ": the second confirmed head contact defeats the same body.");
            Assert.That(actor.State.Health, Is.Zero);
            Assert.That(actor.ReceivedImpactCount, Is.EqualTo(2));
            Assert.That(root.HeadEffects.DetachedSectorCountFor(actor), Is.GreaterThan(0), subject + ": the second shot adds visible structural damage.");
            yield return CapturePistolWoundNearView(actor, subject + " - Second head wound");
        }

        private void FireMixedEnemyProjectile(CombatActor actor, Player3DAnatomicalPart part)
        {
            actor.CaptureContactPose();
            CombatHurtboxes.Hit hit;
            Vector3 outward;
            if (actor.IsLongExposure && part == Player3DAnatomicalPart.Head)
            {
                // The fused sweep has its own physical outline; address a posed
                // source triangle rather than assuming a human head centre ray.
                hit = AuthoredSurfaceContact(actor, CombatLongExposureAssetProvider.Bank, "GEO_Head", BodyDamageRegion.Head);
                outward = hit.Normal;
            }
            else
            {
                Assert.That(actor.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head, out Vector3 center, out outward, out _), Is.True);
                Assert.That(actor.Hurtboxes.SweepProjectile(center + outward * .4f, center - outward * .4f,
                    CombatProjectilePool.Radius, -outward, out hit), Is.True);
            }
            Assert.That(hit.Part, Is.EqualTo(part), "The fixture must confirm the exposed anatomical contact before applying it.");
            int impacts = actor.ReceivedImpactCount;
            actor.ReceiveProjectile(root.Hero, 100 + impacts, hit, -outward * CombatProjectilePool.MuzzleSpeed);
            Assert.That(actor.ReceivedImpactCount, Is.EqualTo(impacts + 1));
            Assert.That(actor.LastImpact.Kind, Is.EqualTo(CombatImpactKind.Projectile));
            Assert.That(actor.LastImpact.Part, Is.EqualTo(part));
        }

        private IEnumerator CaptureMixedEnemyHead(CombatActor actor, string name)
        {
            actor.CaptureContactPose();
            Assert.That(actor.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head, out Vector3 point, out Vector3 outward, out _), Is.True);
            Camera camera = root.CameraFollow.Camera;
            bool following = root.CameraFollow.enabled;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            try
            {
                root.CameraFollow.enabled = false;
                camera.transform.SetPositionAndRotation(point + outward * .8f + Vector3.up * .04f,
                    Quaternion.LookRotation(-outward));
                camera.fieldOfView = 34f;
                CaptureMixedCameraFrame(camera, name);
                yield return null;
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fov;
                root.CameraFollow.enabled = following;
            }
        }

        private IEnumerator CaptureMixedPopulationView(string name)
        {
            Camera camera = root.CameraFollow.Camera;
            bool following = root.CameraFollow.enabled;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            try
            {
                root.CameraFollow.enabled = false;
                Vector3 eye = new Vector3(-10.8f, 4.8f, -7.8f);
                camera.transform.SetPositionAndRotation(eye,
                    Quaternion.LookRotation(new Vector3(0f, 2.2f, 2.5f) - eye));
                camera.fieldOfView = 68f;
                CaptureMixedCameraFrame(camera, name);
                yield return null;
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fov;
                root.CameraFollow.enabled = following;
            }
        }

        private static void CaptureMixedCameraFrame(Camera camera, string name)
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest, name + ".png");
            LogAssert.Expect(LogType.Log, "Area capture wrote " + path);
            AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.CombatTest, name);
        }

        [UnityTest]
        public IEnumerator Range_EndlessPopulationSpawnsGraduallyPreservesSurvivorsAndStopsOnExit()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            var settings = new CombatEncounterSettings(5, .1f, .1f, .15f, .15f);
            var selection = new CombatTestSelection(new CombatLoadoutSelection(
                CombatWeaponId.Shotgun, CombatWeaponId.Shotgun), CombatTestMode.Endless, settings);
            Assert.That(CombatTestStartService.TryStart(selection), Is.True);
            yield return AwaitSelectedCombatRange();
            Assert.That(root.WarehousePlan, Is.Not.Null, "The population contract uses the real warehouse.");
            Assert.That(root.Mode, Is.EqualTo(CombatTestMode.Endless));
            Assert.That(root.EncounterSettings, Is.EqualTo(settings));
            Assert.That(root.Sparring, Is.True);
            root.SetSparring(false);
            Assert.That(root.Sparring, Is.True);
            Assert.That(root.LiveOpponentCount, Is.Zero, "The first opponent is delayed too.");
            Assert.That(CombatEncounterSettings.Default.MaximumLiveOpponents, Is.EqualTo(2));
            Assert.That(root.EncounterSettings.MaximumCorpses, Is.EqualTo(2));
            Assert.That(root.CorpseCount, Is.Zero);
            Assert.That(root.Opponents.Count, Is.EqualTo(5));
            Assert.That(root.CameraFollow.TargetLockActive, Is.False, "There is no invisible target before arrival.");
            for (int index = 0; index < root.Opponents.Count; index++)
            {
                CombatActor actor = root.Opponents[index];
                Assert.That(actor.gameObject.activeInHierarchy, Is.False);
                Assert.That(actor.NpcAnimationGraphBuilds, Is.EqualTo(1),
                    "Every prepared inactive opponent retains its authored combat graph.");
                Assert.That(actor.GetComponentInChildren<DefaultNpcAppearance>(true).AppearanceKey,
                    Is.EqualTo(DefaultNpcPopulation.CombatTestOpponentId(index)));
            }

            root.Tick(.025f);
            float remaining = root.NextOpponentSeconds;
            Assert.That(root.PauseMenu.Open(), Is.True);
            root.Tick(10f);
            yield return null;
            Assert.That(root.NextOpponentSeconds, Is.EqualTo(remaining).Within(.00001f));
            Assert.That(root.LiveOpponentCount, Is.Zero);
            Assert.That(root.PauseMenu.Cancel(), Is.True);
            yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Population pause did not release input.");
            for (int expected = 1; expected <= 5; expected++)
            {
                float untilArrival = root.NextOpponentSeconds;
                root.Tick(Mathf.Max(0f, untilArrival - .025f));
                Assert.That(root.LiveOpponentCount, Is.EqualTo(expected - 1), "An arrival cannot precede its active-time interval.");
                root.Tick(.04f);
                Assert.That(root.LiveOpponentCount, Is.EqualTo(expected), "One interval creates one additional opponent.");
                AssertEndlessPreparedAnimationGraphs();
                Assert.That(root.RoundFinished || root.EndlessRunFinished, Is.False);
            }
            Assert.That(root.WaitingForNextOpponent, Is.False);
            root.Tick(.025f);
            Assert.That(root.LiveOpponentCount, Is.EqualTo(5), "A filled limit cannot accumulate another arrival.");
            var routes = new List<Vector3>();
            var area = new WarehouseArenaArea(root.WarehousePlan);
            foreach (CombatActor actor in root.Opponents)
            {
                Assert.That(Vector3.ProjectOnPlane(actor.transform.position - root.Hero.transform.position, Vector3.up).magnitude,
                    Is.GreaterThanOrEqualTo(settings.MinimumHeroDistance));
                Assert.That(area.FindRoute(actor.transform.position, root.Hero.transform.position, routes, actor.Body.radius + .04f),
                    Is.True, "Every actual spawned body has a warehouse route to the hero.");
            }
            yield return CaptureEndlessPopulationGameView();

            CombatActor hero = root.Hero, defeated = root.Opponents[0], survivor = root.Opponents[1];
            GameObject defeatedWeapon = defeated.Weapon, heldWeapon = hero.Weapon;
            IFirearmState pistol = hero.CarriedFirearm(CombatWeaponId.Pistol);
            IFirearmState shotgun = hero.CarriedFirearm(CombatWeaponId.Shotgun);
            hero.State.ReceiveProjectileHit(13f);
            hero.State.Advance(.2f);
            Assert.That(hero.BodyDamage.Apply(BodyDamageRegion.LeftForearm, 0, .25f, 1, 1, 0), Is.True);
            root.BodyEffects.SynchronizeActor(hero, hero.LastImpact);
            hero.Body.Move(new Vector3(.4f, 0f, -.3f));
            survivor.State.ReceiveProjectileHit(9f);
            survivor.State.Advance(.2f);
            Assert.That(survivor.BodyDamage.Apply(BodyDamageRegion.LeftForearm, 0, .2f, 1, 1, 0), Is.True);
            root.BodyEffects.SynchronizeActor(survivor, survivor.LastImpact);
            Physics.SyncTransforms();
            float health = hero.State.Health, survivorHealth = survivor.State.Health;
            float trauma = hero.BodyDamage.TissueLoss(BodyDamageRegion.LeftForearm, 0);
            float survivorTrauma = survivor.BodyDamage.TissueLoss(BodyDamageRegion.LeftForearm, 0);
            Vector3 position = hero.transform.position;
            SpendEndlessRound(pistol);
            SpendEndlessRound(shotgun);
            int pistolSequence = pistol.ShotSequence, shotgunSequence = shotgun.ShotSequence;
            var participants = EndlessParticipants();

            // A head projectile creates the real terminal body and contact hold.
            QueueEndlessHeadshot(hero, defeated, 1);
            root.Projectiles.Advance(CombatTestRoot.SimulationStep, participants);
            root.Projectiles.ApplyContacts();
            Assert.That(defeated.State.IsDefeated && defeated.IsRagdollActive, Is.True);
            Assert.That(root.HeadEffects.DetachedSectorCountFor(defeated), Is.GreaterThan(0));
            root.Tick(0f);
            Assert.That(root.EndlessKills, Is.EqualTo(1));
            Assert.That(root.LiveOpponentCount, Is.EqualTo(4));
            Assert.That(root.CorpseCount, Is.EqualTo(1));
            Assert.That(root.WaitingForNextOpponent, Is.True);
            Assert.That(root.RoundFinished || root.EndlessRunFinished, Is.False);
            Assert.That(root.Taunt.CanInteract(root.Player.Interactor), Is.False);
            float beforeHold = root.NextOpponentSeconds;
            root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(root.NextOpponentSeconds, Is.EqualTo(beforeHold).Within(.00001f), "Contact hold cannot advance the spawn clock.");
            Assert.That(pistol.Rounds, Is.EqualTo(pistol.Settings.MagazineCapacity - 1));
            Assert.That(shotgun.Rounds, Is.EqualTo(shotgun.Settings.MagazineCapacity - 1), "Opponent turnover cannot supply ammunition.");
            Assert.That(root.Projectiles.TrySpawn(hero, new Vector3(-4f, 8f, -4f), Vector3.up, 20), Is.True);
            root.Tick(.35f);
            Assert.That(root.LiveOpponentCount, Is.EqualTo(5));
            Assert.That(root.CorpseCount, Is.Zero, "Reusing the shared pool frees the old corpse place.");
            Assert.That(root.EndlessKills, Is.EqualTo(1), "The same defeated participant is credited only once.");
            Assert.That(defeated.State.IsDefeated || defeated.IsRagdollActive || defeated.IsWeaponDropped, Is.False);
            Assert.That(defeated.Weapon, Is.SameAs(defeatedWeapon));
            AssertEndlessPreparedAnimationGraphs();
            Assert.That(root.GetComponentsInChildren<CombatActor>(true).Length, Is.EqualTo(6));
            Assert.That(root.HeadEffects.DetachedSectorCountFor(defeated), Is.Zero);
            Assert.That(root.Projectiles.ActiveCount, Is.GreaterThan(0), "An unrelated slow flight survives the recycled body.");
            Assert.That(survivor.State.Health, Is.EqualTo(survivorHealth).Within(.0001f));
            Assert.That(survivor.BodyDamage.TissueLoss(BodyDamageRegion.LeftForearm, 0), Is.EqualTo(survivorTrauma));
            Assert.That(hero.State.Health, Is.EqualTo(health).Within(.0001f));
            Assert.That(hero.BodyDamage.TissueLoss(BodyDamageRegion.LeftForearm, 0), Is.EqualTo(trauma));
            Assert.That(Vector3.Distance(hero.transform.position, position), Is.LessThan(.03f));
            Assert.That(hero.Weapon, Is.SameAs(heldWeapon));
            Assert.That(pistol.Rounds, Is.EqualTo(pistol.Settings.MagazineCapacity - 1));
            Assert.That(shotgun.Rounds, Is.EqualTo(shotgun.Settings.MagazineCapacity - 1));

            // Defeat in reverse pool order: the oldest death, rather than the
            // lowest actor index, must release its body, detached head and prop.
            CombatActor oldestCorpse = root.Opponents[4];
            GameObject droppedProp = oldestCorpse.Weapon;
            for (int index = 4; index >= 2; index--)
            {
                CombatActor victim = root.Opponents[index];
                QueueEndlessHeadshot(hero, victim, 30 + index);
                root.Projectiles.Advance(CombatTestRoot.SimulationStep, participants);
                root.Projectiles.ApplyContacts();
                Assert.That(victim.IsRagdollActive && victim.IsWeaponDropped, Is.True);
                root.Tick(0f);
                Assert.That(root.CorpseCount, Is.EqualTo(Mathf.Min(5 - index, settings.MaximumCorpses)));
                Assert.That(root.LiveOpponentCount, Is.EqualTo(index));
                Assert.That(root.EndlessKills, Is.EqualTo(6 - index));
            }
            Assert.That(oldestCorpse.gameObject.activeInHierarchy, Is.False);
            Assert.That(oldestCorpse.IsRagdollActive || oldestCorpse.IsWeaponDropped, Is.False);
            Assert.That(droppedProp.activeInHierarchy, Is.False, "A detached dropped prop must retire with its owner.");
            Assert.That(droppedProp.transform.IsChildOf(oldestCorpse.transform), Is.True);
            Assert.That(root.HeadEffects.DetachedSectorCountFor(oldestCorpse), Is.Zero);
            Assert.That(root.Opponents[2].gameObject.activeInHierarchy && root.Opponents[3].gameObject.activeInHierarchy, Is.True,
                "The newest two corpses stay in the world.");
            Assert.That(root.Projectiles.ActiveCount, Is.GreaterThan(0), "Corpse cleanup cannot erase an unrelated flight.");
            Assert.That(survivor.State.Health, Is.EqualTo(survivorHealth).Within(.0001f));
            Assert.That(survivor.BodyDamage.TissueLoss(BodyDamageRegion.LeftForearm, 0), Is.EqualTo(survivorTrauma));
            AssertEndlessPreparedAnimationGraphs();
            root.Tick(0f);
            Assert.That(root.EndlessKills, Is.EqualTo(4), "Retired corpses cannot be credited a second time.");
            yield return CaptureEndlessPopulationGameView("endless-corpses-ru");

            // Clearing every live place retains spent ammunition, the surviving
            // rig and its flights until the next opponent arrives.
            foreach (CombatActor actor in root.Opponents) actor.State.ReceiveProjectileHit(actor.State.Settings.MaxHealth);
            // The preceding real headshots still own their twelve-substep hold;
            // the population updates once active simulation resumes after it.
            root.Tick(13f * CombatTestRoot.SimulationStep);
            Assert.That(root.LiveOpponentCount, Is.Zero);
            Assert.That(root.CorpseCount, Is.EqualTo(settings.MaximumCorpses));
            Assert.That(root.EndlessKills, Is.EqualTo(6));
            AssertEndlessAmmunition(pistol, pistolSequence);
            AssertEndlessAmmunition(shotgun, shotgunSequence);
            Assert.That(hero.State.Health, Is.EqualTo(health).Within(.0001f));
            Assert.That(hero.BodyDamage.TissueLoss(BodyDamageRegion.LeftForearm, 0), Is.EqualTo(trauma));
            Assert.That(root.Projectiles.ActiveCount, Is.GreaterThan(0));
            Assert.That(root.NextOpponentSeconds, Is.GreaterThan(0f));
            hero.State.ReceiveProjectileHit(hero.State.Settings.MaxHealth);
            root.Tick(0f);
            Assert.That(root.EndlessRunFinished, Is.True);
            Assert.That(root.WaitingForNextOpponent, Is.False);
            root.Tick(1f);
            Assert.That(root.LiveOpponentCount, Is.Zero, "Hero defeat cancels the pending opponent arrival.");
            Assert.That(root.EndlessKills, Is.EqualTo(6));

            root.ResetRound();
            AssertEndlessPreparedAnimationGraphs();
            Assert.That(root.EndlessKills, Is.Zero);
            Assert.That(root.LiveOpponentCount, Is.Zero);
            Assert.That(root.CorpseCount, Is.Zero, "Reset releases every retained body.");
            Assert.That(root.WaitingForNextOpponent, Is.True);
            Assert.That(hero.State.Health, Is.EqualTo(hero.State.Settings.MaxHealth));
            Assert.That(hero.BodyDamage.TissueLoss(BodyDamageRegion.LeftForearm, 0), Is.Zero);
            Assert.That(root.ReturnToWeapons(), Is.True);
            yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneIds.MainMenu &&
                !SceneTransitionService.IsTransitioning, "Endless population did not return to preparation.");
            Assert.That(defeated == null && defeatedWeapon == null && heldWeapon == null, Is.True);
            Assert.That(Object.FindAnyObjectByType<CombatActor>(), Is.Null);
            StartMenuRoot menu = Object.FindAnyObjectByType<StartMenuRoot>();
            Assert.That(menu.IsChoosingCombatWeapon, Is.True);
            Assert.That(menu.SelectedCombatSelection, Is.EqualTo(selection), "Preparation retains capacity and pacing with mode and kit.");
            yield return CaptureEndlessGameView("endless-preparation-ru", "ru");
            yield return CaptureEndlessGameView("endless-preparation-en", "en");
            Assert.That(PauseMenuController.IsAnyPaused, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        [PrebuildSetup(typeof(CombatStoredSecondaryMotionAssetsSetup))]
        public IEnumerator Range_ThreeWeaponSecondaryMotionUsesPreparedPlaybackWithoutColdReloads()
        {
            Assert.That(Application.isBatchMode, Is.False, "The focused cloth comparison requires rendered Editor frames.");
            float captureDelta = Time.captureDeltaTime;
            int targetRate = Application.targetFrameRate, vSync = QualitySettings.vSyncCount;
            bool useLegacy = CombatTestRoot.UseLegacyArenaForTests, bakedEnabled = SecondaryMotionBakeRuntime.Enabled;
            int? warehouseSeed = CombatTestRoot.WarehouseSeedForTests;
            string folder = Path.GetFullPath("TestResults/Test combat secondary motion");
            var phases = new List<EndlessPerformancePhase>(3);
            var framePhases = new Dictionary<int, string>();
            var measurements = new List<string>();
            EndlessPerformanceRecorder recorder = null;
            IDisposable gameView = null;
            var input = new InputTestFixture();
            Mouse mouse = null;
            try
            {
                input.Setup(); mouse = InputSystem.AddDevice<Mouse>();
                Directory.CreateDirectory(folder);
                Time.captureDeltaTime = 0f; Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0;
#if UNITY_EDITOR
                Type sizeOwner = typeof(AreaLoadingVisualCapture).GetNestedType("GameViewResolution", BindingFlags.NonPublic);
                gameView = (IDisposable)Activator.CreateInstance(sizeOwner, true);
                sizeOwner.GetMethod("Set").Invoke(gameView, new object[] { 1920, 1080 });
#endif
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                CombatTestRoot.UseLegacyArenaForTests = false; CombatTestRoot.WarehouseSeedForTests = -834214790;
                var schedule = new CombatEncounterSettings(3, 60f, 60f, 60f, 60f);
                Assert.That(CombatTestStartService.TryStart(new CombatTestSelection(
                    new CombatLoadoutSelection(CombatWeaponId.Pistol), CombatTestMode.Endless, schedule)), Is.True);
                yield return AwaitSelectedCombatRange();
                foreach (CombatActor opponent in root.Opponents)
                {
                    Assert.That(opponent.IsZombie, Is.True, "The cache proof retains the production zombie population.");
                    Assert.That(opponent.Weapon, Is.Null);
                }
                // This opt-in diagnostic compares the normal world's live and
                // recorded fabric paths; CombatTest's production default stays off.
                root.GetComponent<PlayerClothPhysicsPolicy>().SetSimulationEnabled(true);
                root.SendMessage("OnApplicationFocus", true); GameInput.HandleApplicationFocus(true);
                foreach (CombatWeaponId id in new[] { CombatWeaponId.Pistol, CombatWeaponId.Crowbar, CombatWeaponId.Shotgun })
                    Assert.That(root.Hero.CarriedWeapon(id), Is.Not.Null);
                root.SetDuelLogging(true, Path.Combine(folder, "Duel")); root.AutomaticSimulation = true;
                recorder = new EndlessPerformanceRecorder(root);
                // The reference retains identical world, animation and real gear.
                SecondaryMotionBakeRuntime.Enabled = false;
                for (int warm = 0; warm < 12; warm++) yield return null;
                yield return MeasureEndlessPerformancePhase(recorder, phases, framePhases, "Test physical idle", 30);
                AssertRealCarrySurfaceSkinFreshness(recorder.Jackets);
                using var allocations = CreateBodyAllocationRecorder();

                // The unrefreshed rest recording still lacks the carried bar.
                // It must keep its strict refusal without reconstructing guard
                // meshes and arrays every time the same layout is sampled.
                SecondaryMotionBakeRuntime.Enabled = true;
                Player3DAssetRegistry rig = ((Player3DCharacterPresentation)root.Player.Visual).Registry;
                PlayerJacketCloth jacket = rig.GetComponent<PlayerJacketCloth>();
                SecondaryMotionBakeRuntime.SetContext(rig, CombatPistolAssetProvider.LoadClip(CombatPistolAssetProvider.RestClip),
                    0d, variant: "stored-shotgun");
                Assert.That(SecondaryMotionBakeRuntime.TrySample(rig, SecondaryMotionBakePart.Jacket,
                    jacket.BakedPoseSignature, jacket.BakedPoseValueCount, false, new WindSample(0f, 0f), out _), Is.False);
                Assert.That(SecondaryMotionBakeRuntime.LastResult(rig, SecondaryMotionBakePart.Jacket),
                    Is.EqualTo(SecondaryMotionBakeResult.GuardLayout));
                long guardBuilds = SecondaryMotionBakeRuntime.Diagnostics.GuardBuildCount;
                long refusals = SecondaryMotionBakeRuntime.ResultCount(SecondaryMotionBakePart.Jacket, SecondaryMotionBakeResult.GuardLayout);
                allocations.Reset(); allocations.Start();
                for (int sample = 0; sample < 32; sample++)
                    SecondaryMotionBakeRuntime.TrySample(rig, SecondaryMotionBakePart.Jacket,
                        jacket.BakedPoseSignature, jacket.BakedPoseValueCount, false, new WindSample(0f, 0f), out _);
                long refusalAllocations = StopBodyAllocationRecorder(allocations);
                Assert.That(SecondaryMotionBakeRuntime.Diagnostics.GuardBuildCount, Is.EqualTo(guardBuilds));
                Assert.That(SecondaryMotionBakeRuntime.ResultCount(SecondaryMotionBakePart.Jacket, SecondaryMotionBakeResult.GuardLayout),
                    Is.EqualTo(refusals + 32));
                Assert.That(refusalAllocations, Is.Zero, "A known obsolete recording must reuse its current rig guard without allocating.");
                measurements.Add("Test obsolete recording: strict GuardLayout refusal; repeated guard builds=0; GC.Alloc events=0 (recorder positive control passed).");
                SecondaryMotionBakeRuntime.Enabled = false;

                SecondaryMotionBakeRuntime.ReloadAssets();
                var before = SecondaryMotionBakeRuntime.Diagnostics;
                allocations.Reset(); allocations.Start();
                long started = Stopwatch.GetTimestamp();
                bool idlePrepared = SecondaryMotionBakeRuntime.Prewarm("stored-shotgun", "Idle");
                bool aimPrepared = SecondaryMotionBakeRuntime.Prewarm("stored-shotgun", "PistolAim");
                long coldTicks = Stopwatch.GetTimestamp() - started;
                long coldAllocations = StopBodyAllocationRecorder(allocations);
                var prepared = SecondaryMotionBakeRuntime.Diagnostics;
                allocations.Reset(); allocations.Start(); started = Stopwatch.GetTimestamp();
                bool idleHit = SecondaryMotionBakeRuntime.Prewarm("stored-shotgun", "Idle");
                bool aimHit = SecondaryMotionBakeRuntime.Prewarm("stored-shotgun", "PistolAim");
                long hitTicks = Stopwatch.GetTimestamp() - started;
                long hitAllocations = StopBodyAllocationRecorder(allocations);
                var cached = SecondaryMotionBakeRuntime.Diagnostics;
                Assert.That(idlePrepared && aimPrepared && idleHit && aimHit, Is.True);
                Assert.That(prepared.DecodeCount - before.DecodeCount, Is.EqualTo(2));
                Assert.That(cached.DecodeCount, Is.EqualTo(prepared.DecodeCount));
                Assert.That(coldAllocations, Is.GreaterThan(0), "The cold scope must observe its decoded arrays through the validated recorder.");
                Assert.That(hitAllocations, Is.Zero, "Prepared lookups must not copy TextAsset bytes or allocate cache keys.");
                Assert.That(cached.ResidentClips, Is.LessThanOrEqualTo(12));
                Assert.That(cached.ResidentFloatValues, Is.LessThanOrEqualTo(16000000));
                measurements.Add(FormattableString.Invariant($"Test selected recordings: cold={EndlessMilliseconds(coldTicks):F5}ms/{coldAllocations} GC.Alloc events; prepared={EndlessMilliseconds(hitTicks):F5}ms/{hitAllocations} GC.Alloc events; decoded={prepared.DecodeCount - before.DecodeCount}; resident={cached.ResidentFloatValues * 4}B; evictions={cached.Evictions - before.Evictions}. Lookup scopes exclude frame rendering; allocation event counts use the current-thread recorder with a positive control, not unsupported Mono byte counters."));

                // Weapon changes introduce more recordings than fit together.
                // Idle and aim are used between those changes and must retain
                // their hot decoded data rather than be evicted by load order.
                long beforeEvictions = cached.Evictions;
                foreach (string name in new[] { "CombatReady", "CrowbarDraw", "CrowbarStow", "PistolDraw", "PistolStow",
                    "ShotgunRest", "ShotgunAim", "PistolFire", "CombatAttack", "CombatBackhand", "Idle" })
                {
                    long beforeHits = SecondaryMotionBakeRuntime.Diagnostics.DecodeCount;
                    Assert.That(SecondaryMotionBakeRuntime.Prewarm("stored-shotgun", "Idle"), Is.True);
                    Assert.That(SecondaryMotionBakeRuntime.Prewarm("stored-shotgun", "PistolAim"), Is.True);
                    Assert.That(SecondaryMotionBakeRuntime.Diagnostics.DecodeCount, Is.EqualTo(beforeHits),
                        "Frequently sampled recordings must survive other weapon clips entering the bounded cache.");
                    Assert.That(SecondaryMotionBakeRuntime.Prewarm("no-gear", name), Is.True);
                    Assert.That(SecondaryMotionBakeRuntime.Diagnostics.ResidentClips, Is.LessThanOrEqualTo(12));
                    Assert.That(SecondaryMotionBakeRuntime.Diagnostics.ResidentFloatValues, Is.LessThanOrEqualTo(16000000));
                }
                long afterChanges = SecondaryMotionBakeRuntime.Diagnostics.DecodeCount;
                Assert.That(SecondaryMotionBakeRuntime.Prewarm("stored-shotgun", "Idle") &&
                    SecondaryMotionBakeRuntime.Prewarm("stored-shotgun", "PistolAim"), Is.True);
                Assert.That(SecondaryMotionBakeRuntime.Diagnostics.DecodeCount, Is.EqualTo(afterChanges));
                Assert.That(SecondaryMotionBakeRuntime.Diagnostics.Evictions, Is.GreaterThan(beforeEvictions),
                    "This regression must exercise real memory/count pressure, not a cache that never fills.");
                measurements.Add("Test weapon recording changes: frequent Idle/aim stay decoded while less recently used recordings leave the bounded cache.");

                SecondaryMotionBakeRuntime.Enabled = true;
                for (int warm = 0; warm < 16; warm++) yield return null;
                yield return MeasureEndlessPerformancePhase(recorder, phases, framePhases, "Test prepared idle", 30);
                TestContext.Out.WriteLine(phases[1].BakeContext);
                Assert.That(phases[1].BakeResults[(int)SecondaryMotionBakePart.Jacket, (int)SecondaryMotionBakeResult.FinalPose], Is.Zero, phases[1].BakeContext);
                Assert.That(phases[1].Cloth.JacketBaked, Is.GreaterThanOrEqualTo(15), phases[1].BakeContext);
                Assert.That(phases[1].Cloth.SlingBaked, Is.GreaterThanOrEqualTo(15), "The real stowed crowbar must retain a certified sling layout.");
                AssertRealCarrySurfaceSkinFreshness(recorder.Jackets);
                // Novice procedural aiming can legitimately require live cloth;
                // preparation never bypasses the authoritative final-pose guard.
                input.Press(mouse.rightButton, queueEventOnly: true);
                for (int warm = 0; warm < 20; warm++) yield return null;
                Assert.That(root.Hero.Firearm.AimRequested && root.Hero.Firearm.IsAiming, Is.True,
                    "The measured aim must consume the real held input rather than stay in Idle.");
                Assert.That(root.Hero.JournalClip, Is.EqualTo("PistolAim"));
                long aimDecodes = SecondaryMotionBakeRuntime.Diagnostics.DecodeCount;
                yield return MeasureEndlessPerformancePhase(recorder, phases, framePhases, "Test prepared novice aim", 30);
                Assert.That(SecondaryMotionBakeRuntime.Diagnostics.DecodeCount, Is.EqualTo(aimDecodes));
                Assert.That(root.LiveOpponentCount, Is.Zero);
                Assert.That(PlayerJacketCloth.MaximumFrameSubsteps, Is.EqualTo(3));
                Assert.That(CombatTestRoot.MaximumFrameSubsteps, Is.EqualTo(4));
                foreach (EndlessPerformancePhase phase in phases) phase.Cloth.AssertPhysicalBounds();
                input.Release(mouse.rightButton, queueEventOnly: true);
                yield return null;
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                try { WriteEndlessPerformanceReport(folder, recorder, phases, measurements, null,
                    "Test rendered three-weapon secondary motion; seed=-834214790; hero=Pistol plus carried Crowbar and Shotgun; capacity=3; live NPCs=0; first/next arrivals=60s; matched physical/prepared Idle and actual held novice aim; captureDeltaTime=0; target=60; vSync=0"); }
                finally
                {
                    try { recorder?.Dispose(); if (root != null) { root.AutomaticSimulation = false; root.SetDuelLogging(false); } }
                    finally
                    {
                        gameView?.Dispose(); SecondaryMotionBakeRuntime.Enabled = bakedEnabled;
                        input.TearDown();
                        Time.captureDeltaTime = captureDelta; Application.targetFrameRate = targetRate; QualitySettings.vSyncCount = vSync;
                        CombatTestRoot.UseLegacyArenaForTests = useLegacy; CombatTestRoot.WarehouseSeedForTests = warehouseSeed;
                    }
                }
            }
        }

        // A headless Tick benchmark cannot reproduce the log's long intervals
        // after render submission. Keep this opt-in test on real Editor frames.
        [UnityTest, Explicit("Rendered warehouse performance evidence with optional CPU Timeline; no machine-dependent FPS gate.")]
        public IEnumerator Range_EndlessWarehouseRetainsRenderedPerformanceEvidence()
        {
            Assert.That(Application.isBatchMode, Is.False, "Run this reproduction in the rendered Editor, without -batchmode/-nographics.");
            float captureDelta = Time.captureDeltaTime;
            int targetRate = Application.targetFrameRate, vSync = QualitySettings.vSyncCount;
            bool useLegacy = CombatTestRoot.UseLegacyArenaForTests;
            int? warehouseSeed = CombatTestRoot.WarehouseSeedForTests;
            string folder = Path.GetFullPath("TestResults/Test combat performance");
            var phases = new List<EndlessPerformancePhase>();
            var timelinePhases = new Dictionary<int, string>();
            var arrivals = new List<string>();
            EndlessPerformanceRecorder recorder = null;
            EndlessCpuTimelineScope timeline = null;
            IDisposable gameView = null;
            try
            {
                Directory.CreateDirectory(folder);
                Time.captureDeltaTime = 0f;
                Application.targetFrameRate = 60;
                QualitySettings.vSyncCount = 0;
#if UNITY_EDITOR
                Type sizeOwner = typeof(AreaLoadingVisualCapture).GetNestedType("GameViewResolution", BindingFlags.NonPublic);
                Assert.That(sizeOwner, Is.Not.Null);
                gameView = (IDisposable)Activator.CreateInstance(sizeOwner, true);
                sizeOwner.GetMethod("Set").Invoke(gameView, new object[] { 1920, 1080 });
#endif
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = -41128863;
                var schedule = new CombatEncounterSettings(5, 60f, 60f, 60f, 60f);
                var selection = new CombatTestSelection(new CombatLoadoutSelection(
                    CombatWeaponId.Shotgun, CombatWeaponId.Pistol), CombatTestMode.Endless, schedule);
                Assert.That(CombatTestStartService.TryStart(selection), Is.True);
                yield return AwaitSelectedCombatRange();
                Assert.That(root.WarehousePlan.Seed, Is.EqualTo(-41128863));
                Assert.That(root.Opponents.Count, Is.EqualTo(5));
                Assert.That(root.Hero.WeaponId, Is.EqualTo(CombatWeaponId.Pistol));
                foreach (CombatActor actor in root.Opponents) Assert.That(actor.WeaponId, Is.EqualTo(CombatWeaponId.Crowbar));
                AssertEndlessPreparedAnimationGraphs();
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                root.SetDuelLogging(true, Path.Combine(folder, "Duel"));
                // Warm rendering/cloth without advancing the population timer.
                for (int warm = 0; warm < 12; warm++) yield return null;
                Assert.That(new Vector2Int(Screen.width, Screen.height), Is.EqualTo(new Vector2Int(1920, 1080)),
                    "Main measurements retain the session's real 1080p framebuffer.");
                recorder = new EndlessPerformanceRecorder(root);
                AssertRealCarrySurfaceSkinFreshness(recorder.Jackets);
                // Independent same-frame skin proofs precede every measured
                // interval, including their transform writes and allocations.
                for (int warm = 0; warm < 4; warm++) yield return null;
                root.AutomaticSimulation = true;
                yield return MeasureEndlessPerformancePhase(recorder, phases, timelinePhases, "Test empty warehouse", 30);
                Assert.That(root.LiveOpponentCount, Is.Zero);
                Assert.That(phases[0].BakeResults[(int)SecondaryMotionBakePart.Jacket, (int)SecondaryMotionBakeResult.FinalPose], Is.Zero,
                    "Production completed Idle with its real stored shotgun must not alternate between recorded cloth and physical contact solving.");
                Assert.That(phases[0].Cloth.JacketBaked, Is.Zero,
                    "Production CombatTest keeps jacket physics and its recorded motion disabled.");
                Assert.That(phases[0].Cloth.SlingBaked, Is.Zero,
                    "Production CombatTest uses the attachment curve without recorded sling physics.");

                PrepareEndlessPerformanceArrival();
                yield return MeasureEndlessPerformancePhase(recorder, phases, timelinePhases, "Test one live opponent", 30);
                Assert.That(root.LiveOpponentCount, Is.EqualTo(1));
                CaptureArrival("Test first arrival");
                // Prepare only the spawn timer between phases. The following
                // bounded production Tick owns activation/reset and graph use.
                for (int expected = 2; expected <= 5; expected++)
                {
                    root.AutomaticSimulation = false;
                    PrepareEndlessPerformanceArrival();
                    root.Tick(CombatTestRoot.SimulationStep);
                    Assert.That(root.LiveOpponentCount, Is.EqualTo(expected));
                    CaptureArrival("Test population arrival");
                }
                root.AutomaticSimulation = true;
                yield return MeasureEndlessPerformancePhase(recorder, phases, timelinePhases, "Test five live opponents", 30);
                Assert.That(root.LiveOpponentCount, Is.EqualTo(5));
                AssertEndlessPreparedAnimationGraphs();

                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-endlessPerformanceCpuTimeline") >= 0)
                    timeline = new EndlessCpuTimelineScope(folder);
                CombatActor victim = root.Opponents[0];
                QueueEndlessHeadshot(root.Hero, victim, 1);
                yield return MeasureEndlessPerformancePhase(recorder, phases, timelinePhases, "Test headshot and damaged population", 30);
                Assert.That(victim.State.IsDefeated && victim.IsRagdollActive, Is.True);
                Assert.That(root.HeadEffects.DetachedSectorCountFor(victim), Is.GreaterThan(0));
                Assert.That(root.EndlessKills, Is.EqualTo(1));
                // Settlement is preparation, outside the timed buckets. PhysX
                // keeps running while the combat/spawn clock stays unchanged.
                root.AutomaticSimulation = false;
                float settleDeadline = Time.realtimeSinceStartup + 8f;
                while (!victim.Ragdoll.IsSettled && Time.realtimeSinceStartup < settleDeadline) yield return null;
                Assert.That(victim.Ragdoll.IsSettled, Is.True, "The damage tail must include an actually settled terminal body.");
                root.AutomaticSimulation = true;
                yield return MeasureEndlessPerformancePhase(recorder, phases, timelinePhases, "Test settled damaged population", 30);
                Assert.That(root.LiveOpponentCount, Is.EqualTo(4));

                GameObject retainedWeapon = victim.Weapon;
                PrepareEndlessPerformanceArrival();
                yield return MeasureEndlessPerformancePhase(recorder, phases, timelinePhases, "Test recycled opponent", 30);
                Assert.That(root.LiveOpponentCount, Is.EqualTo(5));
                Assert.That(victim.State.IsDefeated || victim.IsRagdollActive, Is.False);
                Assert.That(victim.Weapon, Is.SameAs(retainedWeapon));
                Assert.That(root.GetComponentsInChildren<CombatActor>(true).Length, Is.EqualTo(6));
                AssertEndlessPreparedAnimationGraphs();
                CaptureArrival("Test recycled arrival");
                root.AutomaticSimulation = false;
                timeline?.Dispose();
                if (timeline != null) ExportEndlessPerformanceTimeline(folder, timelinePhases);
                var flush = root.JournalForDiagnostics.FlushAsync();
                yield return WaitFor(() => flush.IsCompleted, "Endless performance journal did not flush.");
                Assert.That(flush.IsFaulted, Is.False);
                foreach (EndlessPerformancePhase phase in phases) phase.Cloth.AssertPhysicalBounds();
                LogAssert.NoUnexpectedReceived();

                void CaptureArrival(string name)
                {
                    AssertEndlessPreparedAnimationGraphs();
                    arrivals.Add(FormattableString.Invariant($"{name}: search={EndlessMilliseconds(root.LastSpawnSearchTicks):F5}ms; activation={EndlessMilliseconds(root.LastSpawnActivationTicks):F5}ms; reset={EndlessMilliseconds(root.LastSpawnResetTicks):F5}ms; graphs=1 per prepared body."));
                }
            }
            finally
            {
                try
                {
                    timeline?.Dispose();
                    WriteEndlessPerformanceReport(folder, recorder, phases, arrivals, timeline?.Directory);
                }
                finally
                {
                    try
                    {
                        recorder?.Dispose();
                        if (root != null) { root.AutomaticSimulation = false; root.SetDuelLogging(false); }
                    }
                    finally
                    {
                        try { gameView?.Dispose(); }
                        finally
                        {
                            Time.captureDeltaTime = captureDelta;
                            Application.targetFrameRate = targetRate;
                            QualitySettings.vSyncCount = vSync;
                            CombatTestRoot.UseLegacyArenaForTests = useLegacy;
                            CombatTestRoot.WarehouseSeedForTests = warehouseSeed;
                        }
                    }
                }
            }
        }

        private void AssertEndlessPreparedAnimationGraphs()
        {
            foreach (CombatActor actor in root.GetComponentsInChildren<CombatActor>(true))
                if (!actor.IsHero)
                    Assert.That(actor.NpcAnimationGraphBuilds, Is.EqualTo(1),
                        "Arrival, reset and recycling must reuse each prepared opponent's native animation graph.");
        }

        private void PrepareEndlessPerformanceArrival()
        {
            var state = (EndlessCombatState)typeof(CombatTestRoot).GetField("endless",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(root);
            Assert.That(state.Advance(root.NextOpponentSeconds + CombatTestRoot.SimulationStep, root.LiveOpponentCount), Is.True);
        }

        private static double EndlessMilliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;

        private static IEnumerator MeasureEndlessPerformancePhase(EndlessPerformanceRecorder recorder,
            List<EndlessPerformancePhase> phases, Dictionary<int, string> timelinePhases, string name, int frames)
        {
            var phase = new EndlessPerformancePhase(name);
            phases.Add(phase);
            for (int sample = 0; sample < frames; sample++)
            {
                int unityFrame = Time.frameCount;
                timelinePhases[unityFrame] = name;
                recorder.Begin();
                Profiler.BeginSample(EndlessPerformanceFramePrefix + unityFrame.ToString(CultureInfo.InvariantCulture));
                Profiler.EndSample();
                yield return null;
                recorder.End(phase, unityFrame);
            }
            phase.Renderers = recorder.RendererCounts();
            phase.BakeContext = recorder.BakeContext();
        }

        private const string EndlessPerformanceFramePrefix = "Test endless performance frame ";

        private sealed class EndlessPerformancePhase
        {
            internal readonly RealCarryFrameBucket Cloth;
            internal readonly CarryPerformanceStage Tick = new CarryPerformanceStage(), Pose = new CarryPerformanceStage();
            internal readonly CarryPerformanceStage Capture = new CarryPerformanceStage(), Effects = new CarryPerformanceStage();
            internal readonly CarryPerformanceStage NpcSampling = new CarryPerformanceStage(), NpcComposition = new CarryPerformanceStage();
            internal readonly CarryPerformanceStage NpcSnapshot = new CarryPerformanceStage(), Gpu = new CarryPerformanceStage();
            internal readonly List<string> Frames = new List<string>(30);
            internal string Renderers, BakeContext;
            internal readonly int[,] BakeResults = new int[3, Enum.GetValues(typeof(SecondaryMotionBakeResult)).Length];
            internal readonly int[] SlingDecisions = new int[Enum.GetValues(typeof(CombatShotgunSling.BakedPlaybackDecision)).Length];
            internal float MaximumSlingEntryFit, MaximumSlingEntryElapsed;
            internal EndlessPerformancePhase(string name) => Cloth = new RealCarryFrameBucket(name, true);
        }

        private sealed class EndlessPerformanceRecorder : IDisposable
        {
            private readonly CombatTestRoot root;
            private readonly RealCarryFrameRecorder cloth;
            private readonly CombatShotgunSling sling;
            private readonly FrameTiming[] timings = new FrameTiming[1];
            private readonly bool gpuEnabled = FrameTimingManager.IsFeatureEnabled();
            private readonly SkinnedMeshRenderer[] skins;
            private readonly CombatActor[] skinOwners;
            private readonly bool previousBakeRejectionCapture;
            private long tick, pose, capture, effects, sampling, composition, snapshot;
            private ulong timingStamp;
            private int timingRepeats = -1;
            internal EndlessPerformanceRecorder(CombatTestRoot root)
            {
                this.root = root; cloth = new RealCarryFrameRecorder(root);
                sling = root.Hero.CarriedWeapon(CombatWeaponId.Shotgun).GetComponent<CombatShotgunSling>();
                previousBakeRejectionCapture = SecondaryMotionBakeRuntime.CaptureRejectionDetails;
                SecondaryMotionBakeRuntime.CaptureRejectionDetails = true;
                skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                skinOwners = new CombatActor[skins.Length];
                for (int i = 0; i < skins.Length; i++) skinOwners[i] = skins[i].GetComponentInParent<CombatActor>(true);
            }
            internal PlayerJacketCloth[] Jackets => cloth.Jackets;
            internal void Begin()
            {
                tick = root.TotalSimulationTicks; pose = root.SimulationPoseTicks;
                capture = root.SimulationCaptureTicks; effects = root.SimulationEffectsTicks;
                ReadNpcCounters(out sampling, out composition, out snapshot);
                if (gpuEnabled) FrameTimingManager.CaptureFrameTimings();
                cloth.Begin();
            }
            internal void End(EndlessPerformancePhase phase, int unityFrame)
            {
                cloth.End(phase.Cloth);
                if (root.Player.Visual is Player3DCharacterPresentation hero)
                    foreach (SecondaryMotionBakePart part in Enum.GetValues(typeof(SecondaryMotionBakePart)))
                        phase.BakeResults[(int)part, (int)SecondaryMotionBakeRuntime.LastResult(hero.Registry, part)]++;
                phase.SlingDecisions[(int)sling.LastBakedDecision]++;
                phase.MaximumSlingEntryFit = Mathf.Max(phase.MaximumSlingEntryFit, sling.BakedEntryFitError);
                phase.MaximumSlingEntryElapsed = Mathf.Max(phase.MaximumSlingEntryElapsed, sling.BakedEntryElapsedSeconds);
                ReadNpcCounters(out long sampled, out long composed, out long captured);
                double tickMs = EndlessMilliseconds(root.TotalSimulationTicks - tick), poseMs = EndlessMilliseconds(root.SimulationPoseTicks - pose);
                double captureMs = EndlessMilliseconds(root.SimulationCaptureTicks - capture), effectsMs = EndlessMilliseconds(root.SimulationEffectsTicks - effects);
                phase.Tick.Add(tickMs, 1); phase.Pose.Add(poseMs, 1); phase.Capture.Add(captureMs, 1); phase.Effects.Add(effectsMs, 1);
                phase.NpcSampling.Add(EndlessMilliseconds(sampled - sampling), 1);
                phase.NpcComposition.Add(EndlessMilliseconds(composed - composition), 1);
                phase.NpcSnapshot.Add(EndlessMilliseconds(captured - snapshot), 1);
                double gpu = double.NaN, presentWait = double.NaN;
                if (gpuEnabled && FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].cpuFrameTime > 0d)
                {
                    timingRepeats = timings[0].frameStartTimestamp == timingStamp ? timingRepeats + 1 : 0;
                    timingStamp = timings[0].frameStartTimestamp;
                    if (timings[0].gpuFrameTime > 0d) { gpu = timings[0].gpuFrameTime; phase.Gpu.Add(gpu, 1); }
                    presentWait = timings[0].cpuMainThreadPresentWaitTime;
                }
                RealCarryFrameSample frame = phase.Cloth.Samples[phase.Cloth.Samples.Count - 1];
                phase.Frames.Add(FormattableString.Invariant($"{phase.Cloth.Name},{unityFrame},{Time.frameCount},{frame.Wall:F5},{tickMs:F5},{poseMs:F5},{captureMs:F5},{effectsMs:F5},{frame.Cloth:F5},{frame.Jacket:F5},{frame.Hair:F5},{frame.Scarf:F5},{frame.Sling:F5},{frame.MainThread:F5},{frame.PlayerLoop:F5},{frame.GcBytes},{gpu:F5},{presentWait:F5},{timingRepeats},{root.LiveOpponentCount}"));
            }
            private void ReadNpcCounters(out long sampled, out long composed, out long captured)
            {
                sampled = composed = captured = 0;
                foreach (CombatActor actor in root.Opponents)
                { sampled += actor.NpcAnimationSampleTicks; composed += actor.NpcPoseCompositionTicks; captured += actor.NpcPoseSnapshotTicks; }
            }
            internal string RendererCounts()
            {
                int allEnabled = 0, bodyPrepared = 0, bodyEnabled = 0, heroEnabled = 0, npcEnabled = 0;
                for (int i = 0; i < skins.Length; i++)
                {
                    SkinnedMeshRenderer skin = skins[i];
                    if (skin == null) continue;
                    bool partition = skin.name.StartsWith("Body ", StringComparison.Ordinal);
                    if (partition) bodyPrepared++;
                    if (!skin.enabled || !skin.gameObject.activeInHierarchy) continue;
                    allEnabled++;
                    if (partition) bodyEnabled++;
                    if (skinOwners[i] == root.Hero) heroEnabled++;
                    else if (skinOwners[i] != null) npcEnabled++;
                }
                return FormattableString.Invariant($"Skinned renderers at phase end: prepared all={skins.Length}, prepared Body partitions={bodyPrepared}; enabled and active all={allEnabled}, Body partitions={bodyEnabled}, source/other={allEnabled - bodyEnabled}, hero={heroEnabled}, NPC={npcEnabled}; hero health={root.Hero.State.Health:F3}. Renderer inventory is captured once, not scanned per frame.");
            }
            internal void AppendAvailability(StringBuilder report)
            {
                cloth.AppendAvailability(report); report.AppendLine("FrameTiming feature enabled=" + gpuEnabled + "; unsupported latest timings remain NaN and absent recorder samples use -1; repeated latest timing is not an exact frame age.");
                if (root?.Player.Visual is Player3DCharacterPresentation bakedHero)
                    foreach (SecondaryMotionBakePart part in Enum.GetValues(typeof(SecondaryMotionBakePart)))
                        report.AppendLine("Secondary motion context " + part + ": " + SecondaryMotionBakeRuntime.DescribeContext(bakedHero.Registry, part));
            }
            internal string BakeContext() => root?.Player.Visual is Player3DCharacterPresentation hero
                ? "Phase-end jacket context: " + SecondaryMotionBakeRuntime.DescribeContext(hero.Registry, SecondaryMotionBakePart.Jacket)
                : "No hero secondary-motion context.";
            internal string Rejection(SecondaryMotionBakePart part) => root.Player.Visual is Player3DCharacterPresentation hero
                ? SecondaryMotionBakeRuntime.LastRejection(hero.Registry, part) : null;
            public void Dispose()
            { SecondaryMotionBakeRuntime.CaptureRejectionDetails = previousBakeRejectionCapture; cloth.Dispose(); }
        }

        private sealed class EndlessCpuTimelineScope : IDisposable
        {
            private readonly Type type;
            private bool stopped;
            internal string Directory { get; private set; }
            internal EndlessCpuTimelineScope(string folder)
            {
                foreach (var assembly in UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies())
                    type ??= assembly.GetType("BarPromenade.Editor.CombatCpuTimelineCapture");
                Assert.That(type, Is.Not.Null);
                LogAssert.Expect(LogType.Log, new Regex("^Combat CPU Timeline recording for 15 seconds: " +
                    Regex.Escape(Path.GetFullPath(folder)) + @"[\\/]Manual duel [0-9]{4}-[0-9]{2}-[0-9]{2} [0-9]{2}-[0-9]{2}-[0-9]{2}-[0-9]{3}(?: [1-9][0-9]*)?$"));
                Assert.That(type.GetMethod("StartCapture", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { folder }), Is.True, "The optional Timeline must start without taking another recording's ownership.");
                Directory = (string)type.GetProperty("LastCaptureDirectory", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                LogAssert.Expect(LogType.Log, new Regex(@"^Combat CPU Timeline saved \((?:endless_performance_completed|duration_reached)\): " +
                    Regex.Escape(Directory) + "$"));
            }
            public void Dispose()
            {
                if (stopped) return;
                stopped = true;
                type.GetMethod("StopCapture", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { "endless_performance_completed" });
            }
        }

        private static void WriteEndlessPerformanceReport(string folder, EndlessPerformanceRecorder recorder,
            List<EndlessPerformancePhase> phases, List<string> arrivals, string timelineDirectory, string context = null)
        {
            Directory.CreateDirectory(folder);
            var report = new StringBuilder();
            report.AppendLine((context ?? "Test rendered Endless warehouse; seed=-41128863; hero=Pistol plus carried Crowbar and Shotgun; NPC=Crowbar; capacity=5; controlled first/next opponent schedule=60s; captureDeltaTime=0; target=60; vSync=0") + "; framebuffer=" + Screen.width + "x" + Screen.height + ".");
            report.AppendLine("Machine-specific measurements, without absolute FPS assertions. Coroutine wall covers actual rendered intervals; Tick/pose/contacts/cloth scopes overlap and must not be summed. Latest GPU/present timings can refer to older frames. Duel frame_delivery partitions after-LateUpdate/render submission/next-Update wall time, not GPU completion.");
            report.AppendLine(context != null
                ? "The selected recordings are prepared between reference and playback phases. Both Idle phases retain the same real equipment and empty warehouse; the aim phase changes pose through actual held input. Main/PlayerLoop recorder samples can lag."
                : "Between phases only spawn readiness is prepared; activation/reset still belongs to the production Tick. The settlement preparation pauses combat/spawn advancement while real PhysX settles the damaged body, outside timing buckets. Main/PlayerLoop recorder samples can lag; exact source attribution is in the optional CPU Timeline and matching Unity-frame markers.");
            report.AppendLine("CPU Timeline: " + (timelineDirectory ?? "not requested; use -endlessPerformanceCpuTimeline"));
            recorder?.AppendAvailability(report);
            foreach (string arrival in arrivals) report.AppendLine(arrival);
            var csv = new StringBuilder("phase,marker_unity_frame,resume_unity_frame,wall_ms,tick_ms,population_pose_ms,capture_ms,effects_ms,cloth_ms,jacket_ms,hair_ms,scarf_ms,sling_ms,latest_main_thread_ms,player_loop_ms,gc_bytes,latest_gpu_ms,latest_present_wait_ms,latest_timing_repeat_frames,live_opponents\n");
            foreach (EndlessPerformancePhase phase in phases)
            {
                phase.Cloth.AppendTo(report);
                report.AppendLine(phase.BakeContext);
                report.Append("Sling playback decisions:");
                foreach (CombatShotgunSling.BakedPlaybackDecision decision in Enum.GetValues(typeof(CombatShotgunSling.BakedPlaybackDecision)))
                    if (phase.SlingDecisions[(int)decision] > 0)
                        report.Append(" " + decision + "=" + phase.SlingDecisions[(int)decision]);
                report.AppendLine(FormattableString.Invariant($"; max entry elapsed={phase.MaximumSlingEntryElapsed:F5}s; max entry fit={phase.MaximumSlingEntryFit:F6}m."));
                foreach (SecondaryMotionBakePart part in Enum.GetValues(typeof(SecondaryMotionBakePart)))
                {
                    report.Append("Sample decisions " + part + ":");
                    foreach (SecondaryMotionBakeResult decision in Enum.GetValues(typeof(SecondaryMotionBakeResult)))
                        if (phase.BakeResults[(int)part, (int)decision] > 0)
                            report.Append(" " + decision + "=" + phase.BakeResults[(int)part, (int)decision]);
                    report.AppendLine();
                    if (recorder?.Rejection(part) is string rejection) report.AppendLine("First rejected pose " + part + ": " + rejection);
                }
                report.AppendLine(phase.Renderers ?? "Renderer snapshot unavailable: phase did not finish.");
                phase.Tick.AppendTo(report, "Full production Tick CPU"); phase.Pose.AppendTo(report, "Population Present CPU (nested in Tick)");
                phase.Capture.AppendTo(report, "Population contact capture CPU (nested in Tick)"); phase.Effects.AppendTo(report, "Population effects CPU (nested in Tick)");
                phase.NpcSampling.AppendTo(report, "NPC animation sample CPU (nested in Present)");
                phase.NpcComposition.AppendTo(report, "NPC pose composition CPU (nested in Present)");
                phase.NpcSnapshot.AppendTo(report, "NPC pose snapshot CPU (nested in Present)");
                if (phase.Gpu.Values.Count > 0) phase.Gpu.AppendTo(report, "Latest available GPU timing");
                foreach (string frame in phase.Frames) csv.AppendLine(frame);
            }
            File.WriteAllText(Path.Combine(folder, "Report.txt"), report.ToString());
            File.WriteAllText(Path.Combine(folder, "Frames.csv"), csv.ToString());
            TestContext.Progress.WriteLine("Test combat performance evidence: " + folder + "; CPU Timeline: " + (timelineDirectory ?? "not requested"));
        }

        private static void ExportEndlessPerformanceTimeline(string folder, Dictionary<int, string> phases)
        {
#if UNITY_EDITOR
            var csv = new StringBuilder("profiler_frame,unity_frame,phase,thread,sample,parent,start_ms,duration_ms,name\n");
            int correlated = 0;
            for (int frame = ProfilerDriver.firstFrameIndex; frame <= ProfilerDriver.lastFrameIndex; frame++)
            {
                int unityFrame = -1;
                using (var main = ProfilerDriver.GetRawFrameDataView(frame, 0))
                {
                    if (!main.valid) continue;
                    for (int sample = 0; sample < main.sampleCount; sample++)
                    {
                        string name = main.GetSampleName(sample);
                        if (name.StartsWith(EndlessPerformanceFramePrefix, StringComparison.Ordinal) &&
                            int.TryParse(name.Substring(EndlessPerformanceFramePrefix.Length), out int parsed)) unityFrame = parsed;
                    }
                }
                if (!phases.TryGetValue(unityFrame, out string phase)) continue;
                correlated++;
                for (int thread = 0; thread < 32; thread++)
                {
                    using var data = ProfilerDriver.GetRawFrameDataView(frame, thread);
                    if (!data.valid) break;
                    if (data.threadName != "Main Thread" && data.threadName != "Render Thread") continue;
                    var parents = new Stack<(int sample, int end)>();
                    for (int sample = 0; sample < data.sampleCount; sample++)
                    {
                        while (parents.Count > 0 && sample > parents.Peek().end) parents.Pop();
                        int parent = parents.Count > 0 ? parents.Peek().sample : -1;
                        double duration = data.GetSampleTimeMs(sample);
                        if (duration >= .05d)
                            csv.AppendLine(FormattableString.Invariant($"{frame},{unityFrame},{phase},{data.threadName},{sample},{parent},{data.GetSampleStartTimeMs(sample):F5},{duration:F5},\"{data.GetSampleName(sample).Replace("\"", "\"\"")}\""));
                        int children = data.GetSampleChildrenCountRecursive(sample);
                        if (children > 0) parents.Push((sample, sample + children));
                    }
                }
            }
            File.WriteAllText(Path.Combine(folder, "Timeline.csv"), csv.ToString());
            TestContext.Progress.WriteLine("Test Endless Timeline correlated frames retained in history: " + correlated + "; the complete streamed trace retains older frames too.");
            Assert.That(correlated, Is.GreaterThan(0), "The optional CPU Timeline must retain the measured damaged/recycled tail.");
#endif
        }

        [UnityTest]
        public IEnumerator Range_EndlessProjectilesResolveEveryVictim()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            var selection = new CombatTestSelection(new CombatLoadoutSelection(
                CombatWeaponId.Shotgun, CombatWeaponId.Shotgun), CombatTestMode.Endless,
                new CombatEncounterSettings(5, .05f, .05f, .08f, .08f));
            Assert.That(CombatTestStartService.TryStart(selection), Is.True);
            yield return AwaitSelectedCombatRange();
            root.Tick(.4f);
            Assert.That(root.LiveOpponentCount, Is.EqualTo(5));
            CombatActor selected = root.Opponent;
            CombatActor nearer = root.Opponents[0] == selected ? root.Opponents[1] : root.Opponents[0];
            var participants = EndlessParticipants();
            PlaceEndlessProjectileActors(selected, nearer);
            Assert.That(nearer, Is.Not.SameAs(root.Opponent), "The nearest victim is deliberately outside camera selection.");
            nearer.CaptureContactPose();
            Assert.That(nearer.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head, out Vector3 head, out _, out _), Is.True);
            Assert.That(root.Projectiles.TrySpawn(root.Hero, head - Vector3.forward * .75f,
                Vector3.forward * CombatProjectilePool.MuzzleSpeed, 10), Is.True);
            root.Projectiles.Advance(.025f, participants);
            root.Projectiles.ApplyContacts();
            Assert.That(nearer.State.IsDefeated, Is.True, "A nonselected nearer body stops and receives the real head projectile.");
            Assert.That(selected.State.Health, Is.EqualTo(selected.State.Settings.MaxHealth), "Roster order cannot send a flight through the nearer body.");
            Assert.That(nearer.ReceivedImpactCount, Is.EqualTo(1));

            PlaceEndlessProjectileActors(selected, nearer);
            nearer.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head, out head, out _, out _);
            var wallObject = new GameObject("Test projectile barrier");
            wallObject.transform.SetParent(root.transform, false);
            wallObject.transform.position = head - Vector3.forward * .35f;
            BoxCollider wall = wallObject.AddComponent<BoxCollider>();
            wall.size = new Vector3(3f, 3f, .1f);
            Physics.SyncTransforms();
            int surfaceResponses = root.Projectiles.SurfaceEffects.EmissionCount;
            Assert.That(root.Projectiles.TrySpawn(root.Hero, head - Vector3.forward * .75f,
                Vector3.forward * CombatProjectilePool.MuzzleSpeed, 11), Is.True);
            root.Projectiles.Advance(.025f, participants);
            root.Projectiles.ApplyContacts();
            Assert.That(nearer.State.Health, Is.EqualTo(nearer.State.Settings.MaxHealth));
            Assert.That(selected.State.Health, Is.EqualTo(selected.State.Settings.MaxHealth));
            Assert.That(root.Projectiles.SurfaceEffects.EmissionCount, Is.GreaterThan(surfaceResponses), "The closest wall owns the contact ahead of every body.");
            wall.enabled = false;
            Object.Destroy(wallObject);

            // A twelve-pellet spread reaches two separate bodies on one plane.
            // Contacts on later flight segments must not repeat either response.
            PlaceEndlessProjectileActors(selected, nearer);
            selected.ResetActor(new Vector3(-.45f, PlayerFactory.GroundedRootOffset, 5f), Vector3.back);
            nearer.ResetActor(new Vector3(.45f, PlayerFactory.GroundedRootOffset, 5f), Vector3.back);
            CaptureEndlessProjectilePoses();
            float chestHeight = (selected.Ragdoll.PhysicsController.ChestBody.worldCenterOfMass.y +
                nearer.Ragdoll.PhysicsController.ChestBody.worldCenterOfMass.y) * .5f;
            var firstImpacts = new List<CombatImpact>();
            var secondImpacts = new List<CombatImpact>();
            selected.ImpactReceived += firstImpacts.Add;
            nearer.ImpactReceived += secondImpacts.Add;
            try
            {
                Assert.That(root.Projectiles.TrySpawnVolley(root.Hero, new Vector3(0f, chestHeight, -6.5f),
                    Quaternion.identity, 12, root.Hero.Shotgun.Settings), Is.True);
                for (int tick = 0; tick < 60 && root.Projectiles.ActiveCount > 0; tick++)
                {
                    root.Projectiles.Advance(CombatTestRoot.SimulationStep, participants);
                    root.Projectiles.ApplyContacts();
                }
                Assert.That(root.Projectiles.ActiveCount, Is.Zero);
                Assert.That(firstImpacts, Is.Not.Empty, "The same spread must reach the left body.");
                Assert.That(secondImpacts, Is.Not.Empty, "The same spread must reach the right body.");
                Assert.That(selected.ShotgunVolleyResponseCount, Is.EqualTo(1));
                Assert.That(nearer.ShotgunVolleyResponseCount, Is.EqualTo(1));
                var pelletIds = new HashSet<int>();
                foreach (CombatImpact impact in firstImpacts)
                {
                    Assert.That(impact.Target, Is.SameAs(selected));
                    Assert.That(impact.AttackSequence, Is.EqualTo(12));
                    Assert.That(impact.IsPellet && pelletIds.Add(impact.PelletIndex), Is.True);
                }
                foreach (CombatImpact impact in secondImpacts)
                {
                    Assert.That(impact.Target, Is.SameAs(nearer));
                    Assert.That(impact.AttackSequence, Is.EqualTo(12));
                    Assert.That(impact.IsPellet && pelletIds.Add(impact.PelletIndex), Is.True,
                        "One physical pellet cannot be granted to both victims.");
                }
                int firstResponses = firstImpacts.FindAll(impact => impact.PrimaryResponse).Count;
                int secondResponses = secondImpacts.FindAll(impact => impact.PrimaryResponse).Count;
                Assert.That(firstResponses, Is.EqualTo(1));
                Assert.That(secondResponses, Is.EqualTo(1));
                Assert.That(selected.State.Health, Is.LessThan(selected.State.Settings.MaxHealth));
                Assert.That(nearer.State.Health, Is.LessThan(nearer.State.Settings.MaxHealth));
            }
            finally
            {
                selected.ImpactReceived -= firstImpacts.Add;
                nearer.ImpactReceived -= secondImpacts.Add;
            }

            // Recycling cancels only one body's queued old-generation contact.
            // Another queued victim and a still-flying object retain ownership.
            PlaceEndlessProjectileActors(selected, nearer);
            selected.ResetActor(new Vector3(-2f, PlayerFactory.GroundedRootOffset, 0f), Vector3.back);
            nearer.ResetActor(new Vector3(2f, PlayerFactory.GroundedRootOffset, 0f), Vector3.back);
            CaptureEndlessProjectilePoses();
            QueueEndlessHeadshot(root.Hero, selected, 20);
            QueueEndlessHeadshot(root.Hero, nearer, 21);
            root.Projectiles.Advance(CombatTestRoot.SimulationStep, participants);
            Assert.That(root.Projectiles.TrySpawn(root.Hero, new Vector3(0f, 8f, 0f), Vector3.up, 22), Is.True);
            root.Projectiles.RetireActorContacts(selected);
            selected.ResetActor(selected.transform.position, Vector3.back);
            root.Projectiles.ApplyContacts();
            Assert.That(selected.State.Health, Is.EqualTo(selected.State.Settings.MaxHealth), "An old-generation impact cannot wound the new body.");
            Assert.That(nearer.State.IsDefeated, Is.True, "Retiring another participant cannot discard this contact.");
            Assert.That(root.Projectiles.ActiveCount, Is.EqualTo(1), "Retiring a body cannot erase an unrelated flight.");
            LogAssert.NoUnexpectedReceived();
        }

        private List<CombatActor> EndlessParticipants()
        {
            var participants = new List<CombatActor>(root.Opponents.Count + 1) { root.Hero };
            foreach (CombatActor actor in root.Opponents) participants.Add(actor);
            return participants;
        }

        private void PlaceEndlessProjectileActors(CombatActor selected, CombatActor nearer)
        {
            root.Hero.ResetActor(new Vector3(0f, PlayerFactory.GroundedRootOffset, -5f), Vector3.forward);
            int parked = 0;
            foreach (CombatActor actor in root.Opponents)
            {
                Vector3 position = actor == selected ? new Vector3(0f, PlayerFactory.GroundedRootOffset, 3f) :
                    actor == nearer ? Vector3.up * PlayerFactory.GroundedRootOffset :
                    new Vector3(5f, PlayerFactory.GroundedRootOffset, -4f + parked++ * 3f);
                actor.ResetActor(position, Vector3.back);
            }
            CaptureEndlessProjectilePoses();
        }

        private void CaptureEndlessProjectilePoses()
        {
            Physics.SyncTransforms();
            root.Hero.CaptureContactPose();
            foreach (CombatActor actor in root.Opponents) actor.CaptureContactPose();
        }

        private IEnumerator CaptureEndlessPopulationGameView(string name = "endless-population-ru")
        {
            Camera camera = root.CameraFollow.Camera;
            bool following = root.CameraFollow.enabled;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            try
            {
                // The existing AreaCaptureFixture warehouse room view, with the
                // active population and HUD retained in the real framebuffer.
                root.CameraFollow.enabled = false;
                Vector3 overview = new Vector3(-10.8f, 4.8f, -7.8f);
                camera.transform.SetPositionAndRotation(overview,
                    Quaternion.LookRotation(new Vector3(0f, 2.2f, 2.5f) - overview));
                camera.fieldOfView = 68f;
                yield return CaptureEndlessGameView(name, "ru");
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fov;
                root.CameraFollow.enabled = following;
            }
        }

        private static void SpendEndlessRound(IFirearmState firearm)
        {
            firearm.SetAim(true);
            firearm.Advance(firearm.Settings.RaiseSeconds + firearm.Settings.FireCooldownSeconds);
            Assert.That(firearm.TryFire(), Is.True);
            firearm.CancelAction();
            Assert.That(firearm.Rounds, Is.EqualTo(firearm.Settings.MagazineCapacity - 1));
            Assert.That(firearm.ReserveRounds, Is.EqualTo(firearm.Settings.StartingReserveRounds));
        }

        private void QueueEndlessHeadshot(CombatActor source, CombatActor target, int sequence)
        {
            target.CaptureContactPose();
            Assert.That(target.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head, out Vector3 point,
                out Vector3 forward, out Vector3 up), Is.True);
            Vector3 outward = target.IsHero ? Vector3.Cross(up, forward).normalized : forward;
            Assert.That(root.Projectiles.TrySpawn(source, point + outward * .3f,
                -outward * CombatProjectilePool.MuzzleSpeed, sequence), Is.True);
        }

        private static void AssertEndlessAmmunition(IFirearmState firearm, int sequence)
        {
            Assert.That(firearm.Rounds, Is.EqualTo(firearm.Settings.MagazineCapacity - 1),
                "Defeating every opponent cannot replace the spent round.");
            Assert.That(firearm.ReserveRounds, Is.EqualTo(firearm.Settings.StartingReserveRounds),
                "Opponent turnover leaves the carried reserve unchanged.");
            Assert.That(firearm.ShotSequence, Is.EqualTo(sequence), "Opponent turnover retains the monotonic shot identity.");
            Assert.That(firearm.IsReloading || firearm.ReloadPending, Is.False);
            Assert.That(firearm.AimRequested, Is.False);
            Assert.That(firearm.MagazineAttached, Is.True);
            if (firearm is ShotgunState shotgun)
            {
                Assert.That(shotgun.ChamberLoaded(0), Is.False);
                Assert.That(shotgun.ChamberLoaded(1), Is.True);
                Assert.That(shotgun.ChamberSpent(0), Is.True);
                Assert.That(shotgun.ChamberSpent(1), Is.False);
                Assert.That(shotgun.LastFiredBarrel, Is.EqualTo(0));
                Assert.That(shotgun.ReloadChamberMask | shotgun.ReloadLoadMask, Is.Zero);
            }
        }

        private static IEnumerator CaptureEndlessGameView(string name, string language, bool requireExactSize = false)
        {
            const int width = 640, height = 360;
            int previousWidth = Screen.width, previousHeight = Screen.height;
            FullScreenMode previousMode = Screen.fullScreenMode;
            IDisposable gameView = null;
            using (var localized = new EndlessLanguageScope(language))
            {
                try
                {
#if UNITY_EDITOR
                    // Reuse the existing temporary Game View size owner, including
                    // its restoration, rather than introduce another editor helper.
                    Type sizeOwner = typeof(AreaLoadingVisualCapture).GetNestedType("GameViewResolution", BindingFlags.NonPublic);
                    Assert.That(sizeOwner, Is.Not.Null);
                    gameView = (IDisposable)Activator.CreateInstance(sizeOwner, true);
                    sizeOwner.GetMethod("Set").Invoke(gameView, new object[] { width, height });
#else
                    Screen.SetResolution(width, height, FullScreenMode.Windowed);
#endif
                    float deadline = Time.realtimeSinceStartup + 1f;
                    while ((Screen.width != width || Screen.height != height) && Time.realtimeSinceStartup < deadline)
                        yield return null;
                    // Batch Editor does not execute WaitForEndOfFrame. Attempt its
                    // real framebuffer as well; no camera-only substitute is used.
                    if (Application.isBatchMode) { yield return null; yield return null; }
                    else yield return new WaitForEndOfFrame();
                    Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
                    try
                    {
                        Assert.That(frame, Is.Not.Null, "The endless UI capture requires a rendered framebuffer.");
                        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures", SceneIds.CombatTest, name + ".png"));
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        File.WriteAllBytes(path, frame.EncodeToPNG());
                        TestContext.Progress.WriteLine("Endless UI frame: " + path + "; requested " + width + "x" + height +
                            "; framebuffer " + frame.width + "x" + frame.height + "; batch=" + Application.isBatchMode);
                        if (requireExactSize || !Application.isBatchMode)
                            Assert.That(new Vector2Int(frame.width, frame.height), Is.EqualTo(new Vector2Int(width, height)));
                    }
                    finally { if (frame != null) Object.Destroy(frame); }
                }
                finally
                {
                    gameView?.Dispose();
#if !UNITY_EDITOR
                    Screen.SetResolution(previousWidth, previousHeight, previousMode);
#endif
                }
            }
        }

        [Serializable]
        private sealed class EndlessCatalog { public EndlessCatalogEntry[] entries; }
        [Serializable]
        private sealed class EndlessCatalogEntry { public string key; public string value; }

        private sealed class EndlessLanguageScope : IDisposable
        {
            private readonly Dictionary<string, string> values, previous;
            public EndlessLanguageScope(string language)
            {
                LocalizationService.Get("combat.title");
                values = (Dictionary<string, string>)typeof(LocalizationService).GetField("Values",
                    BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                previous = new Dictionary<string, string>(values);
                TextAsset asset = Resources.Load<TextAsset>("Localization/" + language);
                Assert.That(asset, Is.Not.Null);
                foreach (EndlessCatalogEntry entry in JsonUtility.FromJson<EndlessCatalog>(asset.text).entries)
                    values[entry.key] = entry.value;
            }
            public void Dispose()
            {
                values.Clear();
                foreach (var entry in previous) values.Add(entry.Key, entry.Value);
            }
        }
    }
}
