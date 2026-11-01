using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class BottleFractureAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
            new CombatLaboratoryAssetsSetup().Setup();
#if UNITY_EDITOR
            Type.GetType("BarPromenade.Editor.CombatZombieBottleFractureAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Real crowbar sweeps fracture the three authored bottle bodies, without flesh effects; reset and disable clean the finite shards.")]
        [PrebuildSetup(typeof(BottleFractureAssetsSetup))]
        public IEnumerator BottleMonstersFractureFromCrowbar()
        {
            float previousDelta = Time.captureDeltaTime;
            bool previousFocus = GameInput.MovementFocused;
            CombatTestRoot root = null;
            Camera camera = null;
            bool followEnabled = false;
            WarehouseRefrigeratorPresentationProbe probe = null;
            try
            {
                Time.captureDeltaTime = 1f / 60f;
                GameInput.HandleApplicationFocus(true);
                GameSessionState.BeginNewGame();
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.AuthoredEnemyKindForTests = null;
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                Assert.That(CombatTestStartService.TryStart(new CombatTestSelection(CombatLoadoutSelection.Default,
                    CombatTestMode.Laboratory, laboratoryEnemy: CombatEnemyKind.Zombie)), Is.True);
                float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
                while (Time.realtimeSinceStartup < deadline)
                {
                    root = Object.FindAnyObjectByType<CombatTestRoot>();
                    if (root != null) root.AutomaticSimulation = false;
                    if (root != null && root.IsInitialized && !SceneTransitionService.IsTransitioning &&
                        GameInput.CanRead(GameInputContext.Gameplay)) break;
                    yield return null;
                }
                Assert.That(root != null && root.IsInitialized && root.IsLaboratory, Is.True);
                typeof(CombatTestRoot).GetMethod("OnApplicationFocus", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(root, new object[] { true });
                root.SetSparring(false);
                root.AutomaticOpponentDecisions = false;
                foreach (CombatActor actor in root.GetComponentsInChildren<CombatActor>(true))
                    if (actor != root.Hero) actor.gameObject.SetActive(false);
                camera = root.CameraFollow.Camera;
                followEnabled = root.CameraFollow.enabled;
                root.CameraFollow.enabled = false;
                probe = camera.gameObject.AddComponent<WarehouseRefrigeratorPresentationProbe>();
                AssertLegacyZombieHasNoBottleGlass(root);
                for (int variant = 0; variant < 3; variant++)
                    yield return VerifyBottleCrowbarFracture(root, camera, probe, variant);
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            }
            finally
            {
                if (probe != null) { probe.CancelPending(); Object.Destroy(probe); }
                if (root != null && camera != null) root.CameraFollow.enabled = followEnabled;
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.AuthoredEnemyKindForTests = null;
                Time.captureDeltaTime = previousDelta;
                GameInput.HandleApplicationFocus(previousFocus);
                GameSessionState.BeginNewGame();
            }
        }

        private static void AssertLegacyZombieHasNoBottleGlass(CombatTestRoot root)
        {
            var host = new GameObject("Legacy ordinary zombie without a bottle");
            host.transform.SetParent(root.transform, false);
            VillageResidentPresentation visual = null;
            try
            {
                var capsule = host.AddComponent<CharacterController>();
                capsule.height = 1.75f; capsule.center = Vector3.up * .875f; capsule.radius = .32f;
                visual = DefaultNpcFactory.CreateForCharacter(host.transform, DefaultNpcPopulation.StairwellEnemyId(0));
                CombatZombieAssetProvider.ApplyAppearance(visual.gameObject, 0);
                var actor = host.AddComponent<CombatActor>();
                actor.InitializeOpponent(visual, capsule, kind: CombatEnemyKind.Zombie);
                Assert.That(actor.IsOrdinaryZombie, Is.True);
                Assert.That(actor.HasBottleGlass, Is.False);
                Assert.That(host.GetComponent<CombatZombieBottleDestruction>(), Is.Null,
                    "A legacy corpse palette cannot claim the authored bottle geometry owner.");
            }
            finally
            {
                if (visual != null) NpcFootstepSources.Unregister(visual.transform);
                Object.DestroyImmediate(host);
            }
        }

        private static IEnumerator VerifyBottleCrowbarFracture(CombatTestRoot root, Camera camera,
            WarehouseRefrigeratorPresentationProbe probe, int variant)
        {
            var host = new GameObject("Authored bottle fracture " + variant);
            host.transform.SetParent(root.transform, false);
            host.transform.position = new Vector3(0f, PlayerFactory.GroundedRootOffset, 1.2f);
            var capsule = host.AddComponent<CharacterController>();
            capsule.height = 1.75f; capsule.center = Vector3.up * .875f; capsule.radius = .32f;
            VillageResidentPresentation visual = DefaultNpcFactory.CreateForCharacter(host.transform,
                DefaultNpcPopulation.StairwellEnemyId(variant == 2 ? 11 : variant));
            CombatZombieAssetProvider.ApplyOrdinaryAppearance(visual.gameObject, variant);
            var victim = host.AddComponent<CombatActor>();
            victim.InitializeOpponent(visual, capsule, kind: CombatEnemyKind.Zombie);
            CombatActor hero = root.Hero;
            var participants = new[] { hero, victim };
            hero.SetCombatParticipants(participants); victim.SetCombatParticipants(participants);
            hero.SetContactTarget(victim);
            // The lab witness keeps its authored idle facing while the hero
            // walks around it. Giving it an AI target would continually turn
            // its embracing forearms back into every approach to the bottle.
            victim.SetContactTarget(null);
            root.HeadEffects.PrepareActor(victim);
            root.BodyEffects.PrepareActor(victim);
            root.BloodEffects.PrepareActor(victim);
            var showImpact = (Action<CombatImpact>)Delegate.CreateDelegate(typeof(Action<CombatImpact>), root,
                typeof(CombatTestRoot).GetMethod("ShowImpact", BindingFlags.Instance | BindingFlags.NonPublic));
            var resetEffects = (Action<CombatActor>)Delegate.CreateDelegate(typeof(Action<CombatActor>), root,
                typeof(CombatTestRoot).GetMethod("ResetActorDamage", BindingFlags.Instance | BindingFlags.NonPublic));
            victim.ImpactReceived += showImpact; victim.DamageReset += resetEffects;
            try
            {
                var glass = host.GetComponent<CombatZombieBottleDestruction>();
                Assert.That(glass, Is.Not.Null, "The story actor owns glass even without a scene-level destruction owner.");
                Assert.That(glass.Variant, Is.EqualTo(variant));
                Assert.That(glass.GlassTransform.GetComponent<MeshRenderer>().sharedMaterials.Length, Is.EqualTo(1),
                    "The single retained submesh cannot retain the imported per-cell material slots.");
                Vector3 ground = host.transform.position;
                Vector3 facing = variant == 2 ? Vector3.forward : Vector3.back;
                bool reachedGlass = false;
                string attempts = string.Empty;
                // Four real approaches, each at two supported bar distances.
                // Start behind the body, where the front-facing long arms do
                // not shield the glass. Every anatomical hurtbox remains live.
                foreach (Vector2 alignment in new[] { new Vector2(180f, .9f), new Vector2(180f, 1.08f),
                    new Vector2(90f, .9f), new Vector2(90f, 1.08f), new Vector2(-90f, .9f),
                    new Vector2(-90f, 1.08f), new Vector2(0f, .9f), new Vector2(0f, 1.08f) })
                {
                    victim.ResetActor(ground, facing);
                    victim.SetOrdinaryZombieDormant(variant);
                    Assert.That(victim.TryAwakenZombie(false), Is.True);
                    victim.Step(CombatZombieAssetProvider.OrdinaryRiseSeconds);
                    Bounds bounds = glass.WorldBounds;
                    Vector3 approach = Quaternion.LookRotation(facing, Vector3.up) *
                        Quaternion.AngleAxis(alignment.x, Vector3.up) * Vector3.forward;
                    Vector3 heroPosition = bounds.center + approach * alignment.y;
                    heroPosition.y = ground.y;
                    hero.ResetActor(heroPosition, -approach);
                    Physics.SyncTransforms();
                    for (int frame = 0; frame < 36; frame++)
                    { hero.Step(1f / 60f); victim.Step(1f / 60f); yield return null; }
                    Assert.That(hero.HasWeaponInHand && !hero.IsSwitchingWeapon, Is.True);
                    Assert.That(Vector3.Dot(victim.transform.forward, facing), Is.GreaterThan(.99f),
                        "The lab witness must retain its idle facing while the hero takes a real side/back approach.");
                    int before = victim.ReceivedImpactCount;
                    int beforeBlood = root.BloodEffects.EmissionCount;
                    if (attempts.Length == 0)
                        yield return CaptureBottleFractureView(camera, probe, glass, variant, "00-intact", false);
                    Assert.That(hero.TryAttack(), Is.True, "A real uncharged production crowbar swing must start.");
                    int sequence = hero.State.AttackSequence;
                    for (int step = 0; step < 120 && victim.ReceivedImpactCount == before; step++)
                    {
                        victim.Step(CombatTestRoot.SimulationStep);
                        hero.Step(CombatTestRoot.SimulationStep);
                        if ((step & 1) != 0) yield return null;
                    }
                    attempts += $" angle={alignment.x},distance={alignment.y}:impact={victim.ReceivedImpactCount - before},glass={glass.GlassImpactCount}," +
                        $"part={victim.LastImpact.Part},point={victim.LastImpact.Point}";
                    if (victim.ReceivedImpactCount == before || !victim.LastImpact.IsGlassContact) continue;
                    reachedGlass = true;
                    Assert.That(victim.ReceivedImpactCount, Is.EqualTo(before + 1));
                    Assert.That(glass.GlassImpactCount, Is.EqualTo(1), "One swing has one material transaction across all metal segments.");
                    Assert.That(glass.BrokenCellCount, Is.GreaterThan(0), "A normal crowbar strike opens physical glass cells immediately.");
                    Assert.That(glass.ActiveShardCount, Is.InRange(1, CombatZombieBottleDestruction.MaximumShardCount));
                    Assert.That(glass.GlassAudioCount, Is.EqualTo(1));
                    Assert.That(root.BloodEffects.EmissionCount, Is.EqualTo(beforeBlood), "Bottle glass cannot emit flesh blood.");
                    Assert.That(root.BodyEffects.TissueRendererCountFor(victim), Is.Zero);
                    Assert.That(root.BodyEffects.ExposedBoneCountFor(victim), Is.Zero);
                    Assert.That(BottleTorsoTrauma(victim), Is.Zero);
                    Assert.That(Vector3.Distance(glass.LastGlassPoint, victim.LastImpact.Point), Is.LessThan(.02f));
                    AssertBottleHoleQueries(victim, glass);
                    yield return CaptureBottleFractureView(camera, probe, glass, variant, "01-impact-hole", true);
                    int broken = glass.BrokenCellCount, receipts = glass.GlassImpactCount;
                    for (int step = 0; step < 36; step++)
                    { hero.Step(CombatTestRoot.SimulationStep); victim.Step(CombatTestRoot.SimulationStep); yield return null; }
                    Assert.That(hero.State.AttackSequence, Is.EqualTo(sequence));
                    Assert.That(glass.GlassImpactCount, Is.EqualTo(receipts), "Recovery/lingering metal cannot shatter the same attack twice.");
                    Assert.That(glass.BrokenCellCount, Is.EqualTo(broken), "A hole persists without spontaneous new breaks.");
                    using (GameTimeScaleRuntime.AcquirePause())
                    {
                        int active = glass.ActiveShardCount;
                        yield return null;
                        yield return null;
                        Assert.That(glass.ActiveShardCount, Is.EqualTo(active), "The shared pause freezes debris lifetime.");
                    }
                    yield return CaptureBottleFractureView(camera, probe, glass, variant, "02-retained-fracture", false);
                    Assert.That(glass.ActiveShardCount, Is.GreaterThan(0), "Inspect cleanup while the physical glass pool is still occupied.");
                    glass.enabled = false;
                    Assert.That(glass.ActiveShardCount, Is.Zero, "Disabling the presentation releases its live physical shards.");
                    glass.enabled = true;
                    victim.ResetActor(ground, facing);
                    Assert.That(glass.BrokenCellCount, Is.Zero);
                    Assert.That(glass.ActiveShardCount, Is.Zero);
                    yield return CaptureBottleFractureView(camera, probe, glass, variant, "03-reset-intact", false);
                    Assert.That(host.GetComponentsInChildren<Camera>(true), Is.Empty);
                    break;
                }
                Assert.That(reachedGlass, Is.True, "The actual metal must reach this authored bottle from a supported angle:" + attempts);
            }
            finally
            {
                victim.ImpactReceived -= showImpact; victim.DamageReset -= resetEffects;
                hero.SetContactTarget(null); hero.SetCombatParticipants(new[] { hero });
                NpcFootstepSources.Unregister(visual.transform);
                Object.DestroyImmediate(host);
            }
        }

        private static float BottleTorsoTrauma(CombatActor actor)
        {
            float result = 0f;
            foreach (BodyDamageRegion region in new[] { BodyDamageRegion.Chest, BodyDamageRegion.Abdomen, BodyDamageRegion.Pelvis })
                for (int patch = 0; patch < CombatBodyDamageState.PatchCount; patch++) result += actor.BodyDamage.TissueLoss(region, patch);
            return result;
        }

        private static void AssertBottleHoleQueries(CombatActor victim, CombatZombieBottleDestruction glass)
        {
            victim.CaptureContactPose();
            bool open = false;
            for (int cell = 0; cell < glass.CellCount && !open; cell++)
            {
                if (!glass.IsCellBroken(cell)) continue;
                int[] indices = glass.CellTriangles[cell];
                for (int i = 0; i < indices.Length && !open; i += 3)
                {
                    Vector3 a = glass.GlassTransform.TransformPoint(glass.GlassVertices[indices[i]]);
                    Vector3 b = glass.GlassTransform.TransformPoint(glass.GlassVertices[indices[i + 1]]);
                    Vector3 c = glass.GlassTransform.TransformPoint(glass.GlassVertices[indices[i + 2]]);
                    Vector3 centre = (a + b + c) / 3f;
                    Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                    if (normal.sqrMagnitude < .5f) continue;
                    // A short ray crosses the vacated wall rather than the far
                    // side of the bottle. An invisible original torso cannot
                    // close every cell which the real glass mesh has lost.
                    open = !victim.Hurtboxes.SweepProjectile(centre + normal * .025f,
                        centre - normal * .025f, .001f, -normal, out _);
                }
            }
            Assert.That(open, Is.True, "A broken glass cell opens a real projectile gap, not only a cosmetic missing renderer.");
        }

        private static IEnumerator CaptureBottleFractureView(Camera camera, WarehouseRefrigeratorPresentationProbe probe,
            CombatZombieBottleDestruction glass, int variant, string phase, bool close)
        {
            yield return AtWarehouseFridgePresentation(probe, () =>
            {
                Vector3 focus = close ? glass.LastGlassPoint : glass.WorldBounds.center;
                Vector3 outward = glass.LastGlassPoint - glass.WorldBounds.center;
                outward.y = 0f;
                if (outward.sqrMagnitude < .001f) outward = Vector3.back;
                outward.Normalize();
                // View the impacted wall from outside it; a fixed frontal
                // camera would hide a valid side/back opening behind the shell.
                Vector3 offset = close ? outward * 1.1f + Vector3.up * .22f +
                    Vector3.Cross(Vector3.up, outward) * .18f : glass.GlassImpactCount > 0 ?
                    outward * 2.8f + Vector3.up * .8f + Vector3.Cross(Vector3.up, outward) * .8f :
                    new Vector3(2.2f, .8f, -2.8f);
                camera.transform.SetPositionAndRotation(focus + offset, Quaternion.LookRotation(-offset, Vector3.up));
                camera.fieldOfView = close ? 42f : 48f;
                CaptureCurrentCamera(camera, "bottle-fracture", "variant-" + variant + "-" + phase);
            });
        }
    }
}
