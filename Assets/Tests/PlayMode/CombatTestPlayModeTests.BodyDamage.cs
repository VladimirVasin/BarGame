using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class CombatTorsoAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            System.Type.GetType("BarPromenade.Editor.CombatBodyAssetSetup, BarPromenade.Editor", true)
                .GetMethod("ValidateImportedOrThrow", System.Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class CombatTestPlayModeTests
    {
        [UnityTest]
        [PrebuildSetup(typeof(CombatTorsoAssetsSetup))]
        public IEnumerator Range_TorsoErosionKeepsConnectedLaunchCorpsePauseAndReset()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            yield return EnterRange(false, CombatWeaponId.Shotgun);
            foreach (bool hurtHero in new[] { false, true })
            {
                root.ResetRound(); PlacePair(6f);
                CombatActor target = hurtHero ? root.Hero : root.Opponent;
                CombatActor source = hurtHero ? root.Opponent : root.Hero;
                string subject = hurtHero ? "hero" : "opponent";
                var intactTissue = new Dictionary<SkinnedMeshRenderer, Vector3[]>();
                foreach (SkinnedMeshRenderer renderer in target.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (renderer.name.StartsWith("Body FleshRegion")) intactTissue.Add(renderer, renderer.sharedMesh.vertices);
                CaptureBodyDestruction(target, "torso-" + subject + "-intact", target.transform.forward);
                CombatHurtboxes.Hit contact = TorsoContact(target, out Vector3 outward);
                var one = new List<CombatProjectilePool.PelletHit> {
                    new CombatProjectilePool.PelletHit(contact, -outward * CombatProjectilePool.MuzzleSpeed,
                        1f, 0, root.Hero.Shotgun.Settings) };
                target.ReceiveShotgunVolley(source, 1, one, true);
                Assert.That(target.State.IsDefeated, Is.False);
                Assert.That(root.BodyEffects.ActiveFragmentCount, Is.Zero);
                Assert.That(HasDeformedTorsoTissue(target, intactTissue), Is.True,
                    "A partial real contact must remove local exterior and dent finite tissue before exposing bone.");
                target.CaptureContactPose();
                CaptureBodyDestruction(target, "torso-" + subject + "-partial", outward);

                contact = TorsoContact(target, out outward);
                var volley = new List<CombatProjectilePool.PelletHit>();
                for (int pellet = 0; pellet < 12; pellet++) volley.Add(new CombatProjectilePool.PelletHit(contact,
                    -outward * CombatProjectilePool.MuzzleSpeed, 1f, pellet, root.Hero.Shotgun.Settings));
                Vector3 start = target.Ragdoll.PelvisBody.position;
                Assert.That(root.Projectiles.TrySpawnVolley(source,
                    target.Ragdoll.PhysicsController.ChestBody.worldCenterOfMass + outward,
                    Quaternion.LookRotation(-outward), 2, root.Hero.Shotgun.Settings), Is.True);
                for (int step = 0; step < 60 && root.Projectiles.ActiveCount > 0; step++)
                {
                    target.CaptureContactPose();
                    root.Projectiles.Advance(CombatTestRoot.SimulationStep, root.Hero, root.Opponent);
                    root.Projectiles.ApplyContacts();
                }
                Assert.That(root.Projectiles.ActiveCount, Is.Zero);
                Assert.That(target.State.IsDefeated && target.IsRagdollActive, Is.True);
                AssertConnectedTorso(target);
                Assert.That(root.BodyEffects.DepletedTorsoCellCountFor(target), Is.GreaterThan(0));
                Assert.That(root.BodyEffects.ExposedBoneCountFor(target), Is.GreaterThan(0));
                Assert.That(root.BodyEffects.ActiveFragmentCount, Is.Zero,
                    "A torso wound must not emit tissue patches or cascade into limb separation.");
                CaptureBodyDestruction(target, "torso-" + subject + "-exposed", outward);

                // Hitstop retains the immutable impulse until the first physical step.
                root.Tick(.2f);
                float travel = 0f;
                for (int step = 0; step < 45; step++)
                {
                    root.Tick(Time.fixedDeltaTime); yield return new WaitForFixedUpdate();
                    travel = Mathf.Max(travel, Vector3.ProjectOnPlane(target.Ragdoll.PelvisBody.position - start,
                        Vector3.up).magnitude);
                }
                Assert.That(travel, Is.GreaterThan(1.5f), "The connected ragdoll must leave the impact position.");
                AssertConnectedTorso(target);
                CaptureBodyDestruction(target, "torso-" + subject + "-launched", outward);

                float health = target.State.Health;
                int depleted = root.BodyEffects.DepletedTorsoCellCountFor(target);
                contact = TorsoContact(target, out outward); volley.Clear();
                for (int pellet = 0; pellet < 12; pellet++) volley.Add(new CombatProjectilePool.PelletHit(contact,
                    -outward * CombatProjectilePool.MuzzleSpeed, 1f, pellet, root.Hero.Shotgun.Settings));
                target.ReceiveShotgunVolley(source, 3, volley, true);
                Assert.That(target.State.Health, Is.EqualTo(health));
                Assert.That(root.BodyEffects.DepletedTorsoCellCountFor(target), Is.GreaterThanOrEqualTo(depleted));
                Assert.That(root.PauseMenu.Open(), Is.True);
                Vector3 paused = target.Ragdoll.PelvisBody.position;
                root.Tick(.4f); yield return null;
                Assert.That(target.Ragdoll.PelvisBody.position, Is.EqualTo(paused));
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Torso test pause did not release input.");
                root.Tick(.2f); start = target.Ragdoll.PelvisBody.position;
                for (int step = 0; step < 18; step++)
                { root.Tick(Time.fixedDeltaTime); yield return new WaitForFixedUpdate(); }
                Assert.That(Vector3.Distance(target.Ragdoll.PelvisBody.position, start), Is.GreaterThan(.1f),
                    "A new corpse contact must retain its launch across pause.");
                AssertConnectedTorso(target);
                Assert.That(root.BodyEffects.ActiveFragmentCount, Is.Zero);

                // Critical core integrity can end life without retiring any central physics body.
                foreach (BodyDamageRegion region in new[] { BodyDamageRegion.Chest, BodyDamageRegion.Abdomen, BodyDamageRegion.Pelvis })
                    target.BodyDamage.Apply(region, 0, 2f, 99, (int)region, 0);
                root.BodyEffects.SynchronizeActor(target, target.LastImpact);
                Assert.That(target.BodyDamage.IsTerminal, Is.True);
                AssertConnectedTorso(target);
                root.ResetRound();
                Assert.That(root.BodyEffects.DepletedTorsoCellCountFor(target), Is.Zero);
                Assert.That(root.BodyEffects.ExposedBoneCountFor(target), Is.Zero);
                Assert.That(root.BodyEffects.ActiveFragmentCount, Is.Zero);
                Assert.That(target.BodyDamage.IsTerminal, Is.False);
                Assert.That(target.IsRagdollActive, Is.False);
                CaptureBodyDestruction(target, "torso-" + subject + "-reset", target.transform.forward);
            }
            LogAssert.NoUnexpectedReceived();
        }

        private static CombatHurtboxes.Hit TorsoContact(CombatActor target, out Vector3 outward)
        {
            target.CaptureContactPose(); Vector3 centre = target.Ragdoll.PhysicsController.ChestBody.worldCenterOfMass;
            foreach (Vector3 direction in new[] { target.transform.forward, -target.transform.forward,
                target.transform.right, -target.transform.right, Vector3.up, Vector3.down })
                if (target.Hurtboxes.SweepProjectile(centre + direction * .75f, centre - direction * .4f,
                    CombatProjectilePool.Radius, -direction, out CombatHurtboxes.Hit hit) &&
                    CombatBodyDamageState.IsTorso(hit.DamageRegion ?? CombatBodyAnatomy.ToRegion(hit.Part)))
                { outward = direction; return hit; }
            Assert.Fail("The current posed torso must expose a real tissue or bone contact.");
            outward = Vector3.forward; return default;
        }

        private static bool HasDeformedTorsoTissue(CombatActor target, Dictionary<SkinnedMeshRenderer, Vector3[]> intact)
        {
            foreach (SkinnedMeshRenderer renderer in target.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (renderer.enabled && renderer.name.StartsWith("Body FleshRegion") &&
                    renderer.sharedMesh.name.EndsWith("Local Erosion") && renderer.sharedMesh.triangles.Length > 0)
                {
                    Vector3[] changed = renderer.sharedMesh.vertices, before = intact[renderer];
                    for (int i = 0; i < changed.Length; i++)
                        if (renderer.transform.TransformVector(changed[i] - before[i]).magnitude > .001f) return true;
                }
            return false;
        }

        private static void AssertConnectedTorso(CombatActor target)
        {
            for (int r = 0; r < CombatBodyDamageState.RegionCount; r++)
                Assert.That(target.BodyDamage.IsAttached((BodyDamageRegion)r), Is.True,
                    "A torso contact cannot detach the core or its head/limb dependants.");
            foreach (Player3DAnatomicalPart part in new[] { Player3DAnatomicalPart.Torso,
                Player3DAnatomicalPart.LowerTorso, Player3DAnatomicalPart.Pelvis })
            {
                Rigidbody body = target.Ragdoll.PhysicsController.CombatBodyForPart(part);
                Assert.That(body, Is.Not.Null);
                ConfigurableJoint joint = body.GetComponent<ConfigurableJoint>();
                if (joint != null)
                {
                    Assert.That(joint.connectedBody, Is.Not.Null);
                    Assert.That(joint.xMotion, Is.Not.EqualTo(ConfigurableJointMotion.Free));
                }
            }
        }

        [UnityTest]
        public IEnumerator Range_BodyDestructionDefersGeometryAndKeepsFrozenVisibleContacts()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            yield return EnterRange(false, CombatWeaponId.Shotgun);
            Assert.That(root.BloodEffects.PreparedActorCount, Is.EqualTo(2),
                "Wound renderers must prepare before the first shot.");
            Assert.That(root.BloodEffects.WoundCountFor(root.Hero), Is.Zero);
            Assert.That(root.BloodEffects.WoundCountFor(root.Opponent), Is.Zero);
            using var allocations = CreateBodyAllocationRecorder();
            foreach (bool heroVictim in new[] { false, true })
            {
                root.ResetRound(); PlacePair(6f);
                CombatActor target = heroVictim ? root.Hero : root.Opponent;
                CombatActor source = heroVictim ? root.Opponent : root.Hero;
                var originals = new List<SkinnedMeshRenderer>();
                foreach (SkinnedMeshRenderer skin in target.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (skin.enabled && skin.gameObject.activeInHierarchy) originals.Add(skin);
                int synchronizations = root.BodyEffects.ActorSynchronizationCount;
                var damageTimer = System.Diagnostics.Stopwatch.StartNew();
                allocations.Reset(); allocations.Start();
                ApplyPreparedBodyForearmVolley(target, source, 1);
                long damageAllocations = StopBodyAllocationRecorder(allocations);
                damageTimer.Stop();
                Assert.That(root.BodyEffects.ActorSynchronizationCount - synchronizations, Is.EqualTo(1),
                    "All pellet damage is retained, but one received volley rebuilds the body only once.");
                string damageMeasurement = $"Test body volley ({(heroVictim ? "hero" : "opponent")}): {damageTimer.Elapsed.TotalMilliseconds:F3} ms, {damageAllocations} allocations.";
                LogAssert.Expect(LogType.Log, damageMeasurement); Debug.Log(damageMeasurement);
                int intact = 0, replaced = 0;
                foreach (SkinnedMeshRenderer skin in originals)
                {
                    if (CombatBodyDestruction.IsSuppressed(skin)) replaced++;
                    else { Assert.That(skin.enabled, Is.True); intact++; }
                }
                Assert.That(replaced, Is.GreaterThan(0));
                Assert.That(intact, Is.GreaterThan(replaced),
                    "A local forearm hit must keep the untouched production surfaces instead of splitting the whole body into draws.");
                AssertFrozenIntactBodyContact(target);
                AssertFrozenIntactBodyContact(target, sourceSuffix: ".R");
                AssertFrozenGripBodyContact(target);
                AssertSameFrameBodyBoneContact(target);
                if (!heroVictim) AssertRecoverySoleEnvelope(target);
                CaptureBodyDestruction(target, heroVictim ? "body-hero-optimized-cut" : "body-opponent-optimized-cut",
                    target.transform.forward);

                target.Hurtboxes.Capture();
                int builds = target.Hurtboxes.BodySurfaceGeometryBuilds;
                int poseBuilds = target.Hurtboxes.BodySurfacePoseBuilds;
                int poseCaptures = target.Hurtboxes.PoseCaptureCount;
                var timer = System.Diagnostics.Stopwatch.StartNew();
                allocations.Reset(); allocations.Start();
                for (int sample = 0; sample < 32; sample++) target.Hurtboxes.Capture();
                long captureAllocations = StopBodyAllocationRecorder(allocations);
                timer.Stop();
                Assert.That(captureAllocations, Is.Zero, "Repeated damaged-body snapshots must reuse their managed buffers.");
                Assert.That(target.Hurtboxes.PoseCaptureCount, Is.EqualTo(poseCaptures + 32));
                Assert.That(target.Hurtboxes.BodySurfacePoseBuilds, Is.EqualTo(poseBuilds),
                    "Repeated captures of an unchanged pose must retain skin matrices and conservative bounds.");
                Assert.That(target.Hurtboxes.BodySurfaceGeometryBuilds, Is.EqualTo(builds),
                    "Frozen pose capture must not rebuild precise body geometry without a contact query.");
                Vector3 distant = Vector3.one * 500f;
                Assert.That(target.Hurtboxes.SweepProjectile(distant, distant + Vector3.right, 0f,
                    Vector3.right, out _), Is.False);
                Assert.That(target.Hurtboxes.BodySurfaceGeometryBuilds, Is.EqualTo(builds),
                    "A distant sweep must reject body surfaces before skinning vertices or building triangles.");
                string measurement = $"Test damaged body pose capture ({(heroVictim ? "hero" : "opponent")}): {timer.Elapsed.TotalMilliseconds / 32d:F3} ms, {captureAllocations} allocations; exact geometry deferred.";
                LogAssert.Expect(LogType.Log, measurement); Debug.Log(measurement);

                CombatBodyFragment detached = null;
                foreach (CombatBodyFragment fragment in root.BodyEffects.GetComponentsInChildren<CombatBodyFragment>())
                    if (fragment.Owner == target && fragment.name == "Detached body part") { detached = fragment; break; }
                Assert.That(detached, Is.Not.Null);
                Rigidbody rigid = detached.GetComponent<Rigidbody>(); rigid.isKinematic = true;
                detached.transform.position = new Vector3(0f, 5f, 0f);
                var surfaces = new List<HeadContactSurface>();
                foreach (MeshRenderer renderer in detached.GetComponentsInChildren<MeshRenderer>())
                {
                    if (!renderer.enabled) continue;
                    Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    Vector3[] points = mesh.vertices;
                    for (int i = 0; i < points.Length; i++) points[i] = renderer.transform.TransformPoint(points[i]);
                    surfaces.Add(new HeadContactSurface { Name = renderer.name, Vertices = points, Triangles = mesh.triangles });
                }
                Assert.That(surfaces, Is.Not.Empty);
                HeadContactSurface selected = surfaces[0];
                Ray ray = BodyContactProbe(selected);
                Assert.That(IndependentHeadRaycast(surfaces, ray, out Vector3 expected), Is.True);
                target.Hurtboxes.Capture();
                Vector3 move = Vector3.right * 3f;
                detached.transform.position += move;
                Assert.That(target.Hurtboxes.SweepProjectile(ray.origin, ray.GetPoint(.08f), 0f,
                    ray.direction, out CombatHurtboxes.Hit hit), Is.True,
                    "Deferred geometry must use the captured detached pose, even if the live fragment has already moved.");
                Assert.That(hit.IsDetached, Is.True);
                Assert.That(Vector3.Distance(hit.Point, expected), Is.LessThan(.002f),
                    "Contact must match the independently rendered triangles, not the conservative bounds.");
                int firstBuilds = target.Hurtboxes.BodySurfaceGeometryBuilds;
                Assert.That(firstBuilds, Is.GreaterThan(builds));
                Assert.That(target.Hurtboxes.SweepProjectile(ray.origin, ray.GetPoint(.08f), 0f, ray.direction, out _), Is.True);
                Assert.That(target.Hurtboxes.BodySurfaceGeometryBuilds, Is.EqualTo(firstBuilds),
                    "Pellets querying one frozen pose must share its precise geometry.");
                target.Hurtboxes.Capture();
                Assert.That(target.Hurtboxes.SweepProjectile(ray.origin, ray.GetPoint(.08f), 0f, ray.direction, out _), Is.False);
                Assert.That(target.Hurtboxes.SweepProjectile(ray.origin + move, ray.GetPoint(.08f) + move, 0f,
                    ray.direction, out hit), Is.True, "A same-frame recapture must observe the new fragment pose.");
            }
            AssertBodyClothRevisionCache();
            root.ResetRound();
            Assert.That(root.BodyEffects.ActiveFragmentCount, Is.Zero);
            foreach (SkinnedMeshRenderer skin in root.Hero.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                Assert.That(CombatBodyDestruction.IsSuppressed(skin), Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        private static void AssertFrozenIntactBodyContact(CombatActor target, string sourcePrefix = "CLO_Boot", string sourceSuffix = null)
        {
            var surfaces = new List<HeadContactSurface>();
            HeadContactSurface boot = null;
            var scratch = new Mesh { name = "Test visible body surface" };
            try
            {
                foreach (SkinnedMeshRenderer skin in target.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!skin.enabled || !skin.gameObject.activeInHierarchy) continue;
                    skin.BakeMesh(scratch, true);
                    Vector3[] points = scratch.vertices;
                    for (int i = 0; i < points.Length; i++) points[i] = skin.transform.TransformPoint(points[i]);
                    var surface = new HeadContactSurface { Name = skin.name, Vertices = points, Triangles = scratch.triangles };
                    surfaces.Add(surface);
                    if (boot == null && skin.name.StartsWith(sourcePrefix) &&
                        (sourceSuffix == null || skin.name.EndsWith(sourceSuffix))) boot = surface;
                }
            }
            finally { UnityEngine.Object.Destroy(scratch); }
            Assert.That(boot, Is.Not.Null, "The untouched source must still render its original production surface: " + sourcePrefix);
            Ray ray = BodyContactProbe(boot);
            Assert.That(IndependentHeadRaycast(surfaces, ray, out Vector3 expected), Is.True);
            target.Hurtboxes.Capture();
            Vector3 position = target.transform.position;
            try
            {
                target.transform.position += Vector3.right * 3f;
                Assert.That(target.Hurtboxes.SweepProjectile(ray.origin, ray.GetPoint(.08f), 0f,
                    ray.direction, out CombatHurtboxes.Hit hit), Is.True,
                    "Intact originals must query their frozen production triangles.");
                Assert.That(Vector3.Distance(hit.Point, expected), Is.LessThan(.002f),
                    $"Frozen production contact for {boot.Name}: expected {expected}, hit {hit.Point} ({hit.DamageRegion}, patch {hit.DamagePatch}).");
                if (boot.Name.StartsWith("CLO_Boot"))
                {
                    bool left = boot.Name.EndsWith(".L");
                    Assert.That(hit.DamageRegion, Is.EqualTo(left ? BodyDamageRegion.LeftFoot : BodyDamageRegion.RightFoot)
                        .Or.EqualTo(left ? BodyDamageRegion.LeftShin : BodyDamageRegion.RightShin),
                        "The exact original surface must retain its owning leg's damage partition.");
                    Assert.That(hit.DamagePatch, Is.InRange(0, 3));
                }
                int builds = target.Hurtboxes.BodySurfaceGeometryBuilds;
                target.Hurtboxes.SweepProjectile(ray.origin, ray.GetPoint(.08f), 0f, ray.direction, out _);
                Assert.That(target.Hurtboxes.BodySurfaceGeometryBuilds, Is.EqualTo(builds),
                    "Repeated pellets against one frozen intact surface must reuse its exact geometry.");
                int poses = target.Hurtboxes.BodySurfacePoseBuilds;
                target.Hurtboxes.Capture();
                Assert.That(target.Hurtboxes.BodySurfacePoseBuilds, Is.GreaterThan(poses),
                    "A same-frame actor movement must refresh the frozen pose without relying on the render frame counter.");
                Assert.That(target.Hurtboxes.SweepProjectile(ray.origin, ray.GetPoint(.08f), 0f,
                    ray.direction, out _), Is.False);
                Vector3 move = target.transform.position - position;
                Assert.That(target.Hurtboxes.SweepProjectile(ray.origin + move, ray.GetPoint(.08f) + move,
                    0f, ray.direction, out hit), Is.True);
                Assert.That(Vector3.Distance(hit.Point, expected + move), Is.LessThan(.002f),
                    "The refreshed contact must follow the moved production triangles exactly.");
            }
            finally { target.transform.position = position; target.Hurtboxes.Capture(); }
        }

        private static void AssertSameFrameBodyBoneContact(CombatActor target)
        {
            Transform pelvis = FindAnatomicalBone(target, "pelvis");
            Quaternion original = pelvis.localRotation;
            var scratch = new Mesh { name = "Test changed body bone surface" };
            var surfaces = new List<HeadContactSurface>();
            try
            {
                target.Hurtboxes.Capture();
                int poses = target.Hurtboxes.BodySurfacePoseBuilds;
                pelvis.localRotation = original * Quaternion.Euler(0f, 32f, 0f);
                HeadContactSurface boot = null;
                foreach (SkinnedMeshRenderer skin in target.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!skin.enabled || !skin.gameObject.activeInHierarchy) continue;
                    skin.BakeMesh(scratch, true);
                    Vector3[] points = scratch.vertices;
                    for (int i = 0; i < points.Length; i++) points[i] = skin.transform.TransformPoint(points[i]);
                    var surface = new HeadContactSurface { Name = skin.name, Vertices = points, Triangles = scratch.triangles };
                    surfaces.Add(surface);
                    if (boot == null && skin.name.StartsWith("CLO_Boot")) boot = surface;
                }
                Assert.That(boot, Is.Not.Null);
                Ray ray = BodyContactProbe(boot);
                Assert.That(IndependentHeadRaycast(surfaces, ray, out Vector3 expected), Is.True);
                target.Hurtboxes.Capture();
                Assert.That(target.Hurtboxes.BodySurfacePoseBuilds, Is.GreaterThan(poses),
                    "A same-frame bone rotation must invalidate skin matrices even when the actor root stays still.");
                Assert.That(target.Hurtboxes.SweepProjectile(ray.origin, ray.GetPoint(.08f), 0f,
                    ray.direction, out CombatHurtboxes.Hit hit), Is.True);
                Assert.That(Vector3.Distance(hit.Point, expected), Is.LessThan(.002f),
                    "The changed bone contact must match an independent bake of the rendered production mesh.");
                int builds = target.Hurtboxes.BodySurfaceGeometryBuilds;
                int changedPoses = target.Hurtboxes.BodySurfacePoseBuilds;
                target.Hurtboxes.Capture();
                Assert.That(target.Hurtboxes.BodySurfacePoseBuilds, Is.EqualTo(changedPoses));
                Assert.That(target.Hurtboxes.SweepProjectile(ray.origin, ray.GetPoint(.08f), 0f,
                    ray.direction, out hit), Is.True);
                Assert.That(Vector3.Distance(hit.Point, expected), Is.LessThan(.002f));
                Assert.That(target.Hurtboxes.BodySurfaceGeometryBuilds, Is.EqualTo(builds),
                    "An unchanged recapture must retain precise query geometry from the same bone pose.");
            }
            finally
            {
                pelvis.localRotation = original;
                target.Hurtboxes.Capture();
                UnityEngine.Object.Destroy(scratch);
            }
        }

        private static void AssertRecoverySoleEnvelope(CombatActor target)
        {
            var originalLeft = new List<SkinnedMeshRenderer>();
            var originalRight = new List<SkinnedMeshRenderer>();
            var formerLeft = new List<SkinnedMeshRenderer>();
            var formerRight = new List<SkinnedMeshRenderer>();
            foreach (SkinnedMeshRenderer skin in target.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                bool original = skin.name.StartsWith("GEO_", System.StringComparison.Ordinal) ||
                    skin.name.StartsWith("CLO_", System.StringComparison.Ordinal);
                if (skin.name.EndsWith("Sole.L", System.StringComparison.Ordinal))
                { formerLeft.Add(skin); if (original) originalLeft.Add(skin); }
                if (skin.name.EndsWith("Sole.R", System.StringComparison.Ordinal))
                { formerRight.Add(skin); if (original) originalRight.Add(skin); }
            }
            Assert.That(formerLeft.Count + formerRight.Count, Is.GreaterThan(originalLeft.Count + originalRight.Count),
                "The damaged NPC sole oracle must include the disabled derivative skins formerly baked during recovery.");
            using var authored = Player3DFootGroundProbe.Create(originalLeft, originalRight, target.transform);
            using var former = Player3DFootGroundProbe.Create(formerLeft, formerRight, target.transform);
            foreach (FootSide side in new[] { FootSide.Left, FootSide.Right })
            {
                Assert.That(authored.TryGetSoleHeight(side, out float originalHeight), Is.True);
                Assert.That(former.TryGetSoleHeight(side, out float formerHeight), Is.True);
                Assert.That(Mathf.Abs(originalHeight - formerHeight), Is.LessThan(.0005f),
                    "Skipping disabled damage copies must preserve the authored lowest recovery sole vertex: " + side);
            }
        }

        private static void AssertFrozenGripBodyContact(CombatActor target)
        {
            SkinnedMeshRenderer hand = null;
            int shape = -1;
            var hands = target.GetComponentInChildren<NpcHandPose>();
            Assert.That(hands, Is.Not.Null);
            foreach (NpcHandPose.HandBinding binding in hands.Hands)
            {
                if (!binding.IsLeft) continue;
                foreach (SkinnedMeshRenderer skin in binding.Renderers)
                {
                    if (!skin.enabled || !skin.gameObject.activeInHierarchy) continue;
                    for (int i = 0; i < skin.sharedMesh.blendShapeCount; i++)
                    {
                        string name = skin.sharedMesh.GetBlendShapeName(i);
                        if (name != hands.ShapeName && !name.EndsWith("." + hands.ShapeName)) continue;
                        hand = skin; shape = i; break;
                    }
                    if (hand != null) break;
                }
            }
            Assert.That(hand, Is.Not.Null, "The surviving hand needs actual production grip blendshapes.");
            float original = hand.GetBlendShapeWeight(shape);
            try
            {
                foreach (float weight in new[] { 0f, 37.5f, 100f })
                {
                    hand.SetBlendShapeWeight(shape, weight);
                    AssertFrozenIntactBodyContact(target, hand.name);
                }
            }
            finally { hand.SetBlendShapeWeight(shape, original); }
        }

        [UnityTest]
        [PrebuildSetup(typeof(CombatRoomAssetsSetup))]
        public IEnumerator Range_BodyProxiesShareTransformsAndFollowSameFrameChanges()
        {
            CombatTestRoot.UseLegacyArenaForTests = false;
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            Assert.That(CombatTestStartService.TryStart(new CombatTestSelection(
                new CombatLoadoutSelection(CombatWeaponId.Shotgun), CombatTestMode.Laboratory,
                laboratoryEnemy: CombatEnemyKind.Room)), Is.True);
            yield return AwaitSelectedCombatRange();
            root.SendMessage("OnApplicationFocus", true);
            Assert.That(root.Opponent.IsRoom, Is.True);
            AssertBodyProxyCache();
            root.ResetRound();
            LogAssert.NoUnexpectedReceived();
        }

        private void AssertBodyProxyCache()
        {
            CombatActor target = root.Opponent;
            if (target.IsRagdollActive) AssertBodyProxyCollisionIsolation(target, requireDebris: false,
                requireHead: root.HeadEffects.DetachedSectorCountFor(target) > 0 && root.HeadEffects.RetainedSectorCountFor(target) >= 2);
            root.ResetRound(); PlacePair(6f);
            var retainedProxies = new List<BoxCollider>();
            var retainedHeadProxies = new List<BoxCollider>();
            foreach (BoxCollider proxy in target.DamageRigRoot.GetComponentsInChildren<BoxCollider>(true))
                if (proxy.name.StartsWith("Remaining body ", StringComparison.Ordinal)) retainedProxies.Add(proxy);
                else if (proxy.name.StartsWith("Retained Head Sector ", StringComparison.Ordinal)) retainedHeadProxies.Add(proxy);
            Assert.That(retainedProxies, Is.Not.Empty, "Collision proxies must be prepared before the first damaging contact.");
            Assert.That(retainedHeadProxies.Count, Is.GreaterThanOrEqualTo(2), "Head collision sectors must be prepared before the first headshot.");
            foreach (BoxCollider proxy in retainedProxies)
                Assert.That(proxy.enabled, Is.False, "Prepared collision resources must stay disabled while the body is intact.");
            foreach (BoxCollider proxy in retainedHeadProxies)
                Assert.That(proxy.enabled, Is.False, "Prepared head collision sectors must stay disabled while the head is intact.");
            DefeatBodyTargetWithProjectile(target, root.Hero, 19);
            if (!target.State.IsDefeated) DefeatBodyTargetWithProjectile(target, root.Hero, 119);
            Assert.That(target.State.IsDefeated && target.IsRagdollActive, Is.True);
            ApplyKnownBodyContact(target, root.Hero, BodyDamageRegion.RightShin, 0, 2f, 20);
            // Drain the real terminal contact hold before probing live cache work.
            root.Tick(.25f);
            Assert.That((bool)CarrySupportField(root.BodyEffects, "frozen"), Is.False,
                "The proxy cache regression requires live effects after the headshot hit-stop.");
            Assert.That(target.Ragdoll.PhysicsController.FreezeInPlace(), Is.True);
            typeof(CombatRagdoll).GetProperty(nameof(CombatRagdoll.IsSettled)).SetValue(target.Ragdoll, true);
            root.BodyEffects.Tick(CombatTestRoot.SimulationStep);
            AssertBodyProxyCollisionIsolation(target, requireDebris: true, requireHead: true);
            AssertBodyProxyNativeBounds(target, "Current damaged pose");
            int builds = root.BodyEffects.ProxyGeometryBuilds;
            long transformSamples = root.BodyEffects.ProxyTransformSamples;
            int transformCount = root.BodyEffects.ProxyTransformCountFor(target);
            Assert.That(transformCount, Is.GreaterThan(0));
            long unchangedSkips = root.BodyEffects.UnchangedBodyPoseSkips;
            using var allocations = CreateBodyAllocationRecorder();
            var proxyTimer = System.Diagnostics.Stopwatch.StartNew();
            allocations.Reset(); allocations.Start();
            for (int step = 0; step < 32; step++) root.BodyEffects.Tick(CombatTestRoot.SimulationStep);
            long allocated = StopBodyAllocationRecorder(allocations);
            proxyTimer.Stop();
            Assert.That(root.BodyEffects.ProxyGeometryBuilds, Is.EqualTo(builds),
                "Unchanged hand shapes must reuse their collision bounds across simulation substeps.");
            Assert.That(allocated, Is.Zero, "Proxy updates must reuse vertex buffers.");
            Assert.That(root.BodyEffects.UnchangedBodyPoseSkips - unchangedSkips, Is.EqualTo(32),
                "A settled unchanged corpse must skip the full partition/proxy pass while retaining its collision.");
            Assert.That(root.BodyEffects.ProxyTransformSamples - transformSamples,
                Is.InRange(0L, (long)transformCount * 32),
                "One effects pass must sample a shared transform at most once across all body proxies.");
            TestContext.Out.WriteLine($"Test cached body proxies: cpu={proxyTimer.Elapsed.TotalMilliseconds / 32d:F3} ms per tick, " +
                $"transformSamples={root.BodyEffects.ProxyTransformSamples - transformSamples}, uniqueTransforms={transformCount}, allocations={allocated}.");
            target.Hurtboxes.Capture();
            target.Hurtboxes.CaptureProjectileAimBounds();
            Vector3 offRay = target.transform.position + Vector3.up * 1000f;
            int captures = target.Hurtboxes.PoseCaptureCount;
            var resolveAim = (Func<Ray, bool, Vector3>)typeof(CombatTestRoot).GetMethod("ResolveFreePistolAimSurface",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .CreateDelegate(typeof(Func<Ray, bool, Vector3>), root);
            resolveAim(new Ray(offRay, Vector3.right), true);
            Assert.That(target.Hurtboxes.PoseCaptureCount, Is.EqualTo(captures),
                "An off-ray settled corpse must be rejected before detailed anatomy capture.");
            Vector3 headCentre = FindAnatomicalBone(target, "head").position;
            Assert.That(target.Hurtboxes.MayIntersectProjectile(headCentre - Vector3.right * 2f,
                headCentre + Vector3.right * 2f, 0f), Is.True,
                "The broad phase must retain the corpse's current anatomical footprint.");
            var onRay = new Ray(headCentre - Vector3.right * 2f, Vector3.right);
            Vector3 expectedAim = resolveAim(onRay, true);
            captures = target.Hurtboxes.PoseCaptureCount;
            var aimTimer = System.Diagnostics.Stopwatch.StartNew();
            for (int query = 0; query < 32; query++)
                Assert.That(resolveAim(onRay, true), Is.EqualTo(expectedAim));
            aimTimer.Stop();
            Assert.That(target.Hurtboxes.PoseCaptureCount, Is.EqualTo(captures),
                "Repeated on-ray queries must reuse the exactly validated corpse pose.");
            var repeatedCaptureTimer = System.Diagnostics.Stopwatch.StartNew();
            for (int query = 0; query < 32; query++)
            {
                // Reproduce the former caller's full recapture under the same
                // warmed pose and ray; this is a CPU scope comparison, not FPS.
                target.Hurtboxes.Capture();
                target.Hurtboxes.CaptureProjectileAimBounds();
                Assert.That(resolveAim(onRay, true), Is.EqualTo(expectedAim));
            }
            repeatedCaptureTimer.Stop();
            Assert.That(target.Hurtboxes.PoseCaptureCount, Is.EqualTo(captures + 32));
            TestContext.Out.WriteLine($"Test unchanged on-ray Room aim: cached={aimTimer.Elapsed.TotalMilliseconds / 32d:F3} ms/query, former recapture={repeatedCaptureTimer.Elapsed.TotalMilliseconds / 32d:F3} ms/query; identical aim, cached recaptures=0.");
            // A contact capture may replace the shared snapshot between queries,
            // even without moving any transforms. Its aim cache must be reprised.
            target.Hurtboxes.Capture();
            captures = target.Hurtboxes.PoseCaptureCount;
            Assert.That(resolveAim(onRay, true), Is.EqualTo(expectedAim));
            Assert.That(target.Hurtboxes.PoseCaptureCount, Is.EqualTo(captures + 1));
            var hands = target.GetComponentInChildren<NpcHandPose>();
            SkinnedMeshRenderer hand = null;
            int gripShape = -1;
            foreach (NpcHandPose.HandBinding binding in hands.Hands)
            {
                if (!binding.IsLeft) continue;
                foreach (SkinnedMeshRenderer skin in binding.Renderers)
                {
                    int candidate = CharacterJointDeformation.FindShape(skin.sharedMesh, hands.ShapeName);
                    if (candidate < 0) continue;
                    hand = skin; gripShape = candidate; break;
                }
                if (hand != null) break;
            }
            Assert.That(hand, Is.Not.Null);
            BoxCollider handIndexProxy = null;
            CombatBodyDestruction.ProxyBoundsDiagnostic handIndicesBefore = default;
            if (PlayerScarfBodyContacts.NativeClothSupported)
                handIndicesBefore = CaptureBodyProxyIndices(target, BodyDamageRegion.LeftHand, out handIndexProxy);
            float gripWeight = hand.GetBlendShapeWeight(gripShape);
            try
            {
                hand.SetBlendShapeWeight(gripShape, gripWeight == 100f ? 0f : 100f);
                root.BodyEffects.Tick(CombatTestRoot.SimulationStep);
                captures = target.Hurtboxes.PoseCaptureCount;
                resolveAim(onRay, true);
                Assert.That(target.Hurtboxes.PoseCaptureCount, Is.EqualTo(captures + 1),
                    "A changed hand corrective must refresh aim anatomy in the same rendered frame.");
                Assert.That(root.BodyEffects.ProxyGeometryBuilds, Is.GreaterThan(builds),
                    "A changed hand corrective must refresh its proxies immediately in the same rendered frame.");
                AssertBodyProxyNativeBounds(target, "Changed hand corrective");
                if (handIndexProxy != null)
                {
                    Assert.That(root.BodyEffects.TryCaptureProxyBoundsForDiagnostics(target, handIndexProxy, out var handIndicesAfter), Is.True);
                    Assert.That(handIndicesAfter.CachedActiveIndices, Is.SameAs(handIndicesBefore.CachedActiveIndices),
                        "A position-only corrective must retain the original ordered topology array.");
                    Assert.That(handIndicesAfter.IndexBuilds, Is.EqualTo(handIndicesBefore.IndexBuilds),
                        "A position-only corrective must not recopy triangle indices.");
                    Assert.That(handIndicesAfter.CachedActiveIndices, Is.EqualTo(handIndicesAfter.ActiveIndices));
                }
            }
            finally { hand.SetBlendShapeWeight(gripShape, gripWeight); }
            root.BodyEffects.Tick(CombatTestRoot.SimulationStep);
            builds = root.BodyEffects.ProxyGeometryBuilds;
            Transform bone = FindAnatomicalBone(target, "hand.L");
            var proxies = bone.GetComponentsInChildren<BoxCollider>();
            var centres = new Vector3[proxies.Length]; var sizes = new Vector3[proxies.Length];
            Assert.That(proxies, Is.Not.Empty);
            for (int i = 0; i < proxies.Length; i++) { centres[i] = proxies[i].center; sizes[i] = proxies[i].size; }
            Vector3 position = target.transform.position; Quaternion rotation = target.transform.rotation;
            try
            {
                target.transform.SetPositionAndRotation(position + Vector3.right * 3f, Quaternion.Euler(0f, 45f, 0f) * rotation);
                root.BodyEffects.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.BodyEffects.ProxyGeometryBuilds, Is.EqualTo(builds),
                    "Rigid movement of the actor must not rebake hand-local collision geometry. Last invalidation: " +
                    root.BodyEffects.LastProxyCacheInvalidation);
                for (int i = 0; i < proxies.Length; i++)
                { Assert.That(proxies[i].center, Is.EqualTo(centres[i])); Assert.That(proxies[i].size, Is.EqualTo(sizes[i])); }
                AssertBodyProxyNativeBounds(target, "Rigid root movement");
            }
            finally { target.transform.SetPositionAndRotation(position, rotation); }
            Vector3 scale = target.transform.localScale;
            try
            {
                target.transform.localScale = scale * 1.25f;
                root.BodyEffects.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.BodyEffects.ProxyGeometryBuilds, Is.GreaterThan(builds),
                    "A real scale change must invalidate the collision geometry cache.");
            }
            finally { target.transform.localScale = scale; }
            Quaternion handRotation = bone.localRotation;
            try
            {
                target.transform.localScale = Vector3.Scale(scale, new Vector3(1.25f, .8f, 1.1f));
                root.BodyEffects.Tick(CombatTestRoot.SimulationStep);
                builds = root.BodyEffects.ProxyGeometryBuilds;
                bone.localRotation = handRotation * Quaternion.Euler(0f, 0f, 31f);
                root.BodyEffects.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.BodyEffects.ProxyGeometryBuilds, Is.GreaterThan(builds),
                    "Rotation under nonuniform scale must refresh the proxy's physical minimum in the same frame.");
                AssertBodyProxyNativeBounds(target, "Nonuniform scale and hand rotation");
            }
            finally { bone.localRotation = handRotation; target.transform.localScale = scale; }
            root.BodyEffects.Tick(CombatTestRoot.SimulationStep);
            BoxCollider torsoIndexProxy = null;
            int[] originalTorsoIndices = null;
            int damagedIndexBuilds = 0;
            if (PlayerScarfBodyContacts.NativeClothSupported)
                AssertBodyProxyTopologyChange(target, out torsoIndexProxy, out originalTorsoIndices, out damagedIndexBuilds);
            root.ResetRound(); PlacePair(6f);
            foreach (BoxCollider proxy in retainedProxies)
                Assert.That(proxy != null && !proxy.enabled, Is.True, "Reset must retain and disable the same prewarmed proxy.");
            foreach (BoxCollider proxy in retainedHeadProxies)
                Assert.That(proxy != null && !proxy.enabled, Is.True, "Reset must retain and disable the same head collision sector.");
            int retainedCount = 0, retainedHeadCount = 0;
            foreach (BoxCollider proxy in target.DamageRigRoot.GetComponentsInChildren<BoxCollider>(true))
                if (proxy.name.StartsWith("Remaining body ", StringComparison.Ordinal))
                { Assert.That(retainedProxies.Contains(proxy), Is.True); retainedCount++; }
                else if (proxy.name.StartsWith("Retained Head Sector ", StringComparison.Ordinal))
                { Assert.That(retainedHeadProxies.Contains(proxy), Is.True); retainedHeadCount++; }
            Assert.That(retainedCount, Is.EqualTo(retainedProxies.Count), "Reset may not replace or duplicate retained proxies.");
            Assert.That(retainedHeadCount, Is.EqualTo(retainedHeadProxies.Count), "Reset may not replace or duplicate retained head sectors.");
            DefeatBodyTargetWithProjectile(target, root.Hero, 21);
            if (!target.State.IsDefeated) DefeatBodyTargetWithProjectile(target, root.Hero, 121);
            Assert.That(target.State.IsDefeated && target.IsRagdollActive, Is.True);
            ApplyKnownBodyContact(target, root.Hero, BodyDamageRegion.RightShin, 0, 2f, 22);
            // Drain the real terminal contact hold before probing live cache work.
            root.Tick(.25f);
            Assert.That((bool)CarrySupportField(root.BodyEffects, "frozen"), Is.False,
                "The proxy cache regression requires live effects after the headshot hit-stop.");
            root.BodyEffects.Tick(CombatTestRoot.SimulationStep);
            AssertBodyProxyCollisionIsolation(target, requireDebris: true, requireHead: true);
            if (torsoIndexProxy != null)
            {
                Assert.That(root.BodyEffects.TryCaptureProxyBoundsForDiagnostics(target, torsoIndexProxy, out var restoredIndices), Is.True);
                Assert.That(restoredIndices.ActiveIndices, Is.EqualTo(originalTorsoIndices),
                    "Reset must restore the original ordered torso triangles.");
                Assert.That(restoredIndices.CachedActiveIndices, Is.EqualTo(restoredIndices.ActiveIndices));
                Assert.That(restoredIndices.IndexBuilds, Is.GreaterThan(damagedIndexBuilds),
                    "Restored topology must invalidate the damaged index cache.");
                AssertBodyProxyNativeBounds(target, "Restored torso topology");
            }
        }

        private CombatBodyDestruction.ProxyBoundsDiagnostic CaptureBodyProxyIndices(CombatActor target,
            BodyDamageRegion region, out BoxCollider selected)
        {
            foreach (BoxCollider proxy in target.DamageRigRoot.GetComponentsInChildren<BoxCollider>(true))
                if (proxy.enabled && proxy.name.StartsWith("Remaining body " + region + " ", StringComparison.Ordinal) &&
                    root.BodyEffects.TryCaptureProxyBoundsForDiagnostics(target, proxy, out var sample))
                { selected = proxy; return sample; }
            Assert.Fail("The topology comparison requires an active " + region + " proxy.");
            selected = null; return default;
        }

        private void AssertBodyProxyTopologyChange(CombatActor target, out BoxCollider selected,
            out int[] originalIndices, out int damagedIndexBuilds)
        {
            selected = null; originalIndices = null; damagedIndexBuilds = 0;
            var bodies = (System.Collections.IDictionary)CarrySupportField(root.BodyEffects, "bodies");
            var pieces = (List<CombatBodyDestruction.Piece>)CarrySupportField(bodies[target], "Pieces");
            CombatBodyDestruction.Piece largestTorso = null;
            foreach (CombatBodyDestruction.Piece candidate in pieces)
                if (candidate.Region == BodyDamageRegion.Chest && !candidate.Flesh && !candidate.Bone &&
                    candidate.Torso != null && candidate.Proxy != null && candidate.Proxy.enabled &&
                    (largestTorso == null || candidate.Torso.CellCount > largestTorso.Torso.CellCount)) largestTorso = candidate;
            foreach (CombatBodyDestruction.Piece piece in pieces)
            {
                // Use a torso surface with room for a partial wound. The Room
                // neck shares Chest but its small collar can be consumed whole.
                if (piece != largestTorso ||
                    !root.BodyEffects.TryCaptureProxyBoundsForDiagnostics(target, piece.Proxy, out var before)) continue;
                Assert.That(before.ActiveIndices.Length, Is.GreaterThanOrEqualTo(3));
                Vector3 centre = (before.Vertices[before.ActiveIndices[0]] + before.Vertices[before.ActiveIndices[1]] +
                    before.Vertices[before.ActiveIndices[2]]) / 3f;
                Vector3 contact = piece.Proxy.transform.parent.TransformPoint(before.Matrix.MultiplyPoint3x4(centre));
                int topologyBuilds = piece.Torso.TopologyBuilds;
                Assert.That(piece.Torso.Apply(contact, .12f, 2f), Is.True,
                    "The index-cache regression must erode an actual posed torso triangle.");
                root.BodyEffects.Tick(CombatTestRoot.SimulationStep);
                Assert.That(piece.Torso.TopologyBuilds, Is.GreaterThan(topologyBuilds),
                    "The real contact must change owned torso topology.");
                Assert.That(root.BodyEffects.TryCaptureProxyBoundsForDiagnostics(target, piece.Proxy, out var after), Is.True);
                Assert.That(after.CachedActiveIndices, Is.Not.SameAs(before.CachedActiveIndices));
                Assert.That(after.IndexBuilds, Is.GreaterThan(before.IndexBuilds));
                Assert.That(after.SkinBindingBuilds, Is.EqualTo(before.SkinBindingBuilds),
                    "An owned erosion mesh must retain the authored skin binding while its active triangles change.");
                Assert.That(after.ActiveIndices, Is.Not.EqualTo(before.ActiveIndices),
                    "The contact must remove real triangles rather than merely revise vertex positions.");
                Assert.That(after.CachedActiveIndices, Is.EqualTo(after.ActiveIndices),
                    "The rebuilt cache must match the current mesh's independent ordered triangles.");
                selected = piece.Proxy; originalIndices = before.ActiveIndices; damagedIndexBuilds = after.IndexBuilds;
                AssertBodyProxyNativeBounds(target, "Changed torso topology");
                break;
            }
            Assert.That(selected, Is.Not.Null, "The topology regression requires real exterior torso geometry.");
        }

        private void AssertBodyProxyNativeBounds(CombatActor target, string pose)
        {
            if (!PlayerScarfBodyContacts.NativeClothSupported) return;
            int builds = root.BodyEffects.ProxyGeometryBuilds;
            BoxCollider[] proxies = target.DamageRigRoot.GetComponentsInChildren<BoxCollider>(true);
            var bodies = (System.Collections.IDictionary)CarrySupportField(root.BodyEffects, "bodies");
            var pieces = (List<CombatBodyDestruction.Piece>)CarrySupportField(bodies[target], "Pieces");
            foreach (BodyDamageRegion region in new[] { BodyDamageRegion.Chest, BodyDamageRegion.LeftHand })
            {
                CombatBodyDestruction.Piece largest = null;
                foreach (CombatBodyDestruction.Piece piece in pieces)
                    if (piece.Region == region && piece.Proxy != null && piece.Proxy.enabled &&
                        (largest == null || piece.Skin.sharedMesh.vertexCount > largest.Skin.sharedMesh.vertexCount)) largest = piece;
                Assert.That(largest, Is.Not.Null, pose + ": requires active " + region + " geometry.");
                bool compared = false;
                foreach (BoxCollider proxy in proxies)
                {
                    if (proxy != largest.Proxy ||
                        !root.BodyEffects.TryCaptureProxyBoundsForDiagnostics(target, proxy,
                            out CombatBodyDestruction.ProxyBoundsDiagnostic sample)) continue;
                    Assert.That(sample.ExecutedInBurst, Is.True, pose + ": the independent bounds comparison must run the actual Burst fold.");
                    Assert.That(sample.ActiveIndices, Is.Not.Empty);
                    Assert.That(sample.CachedActiveIndices, Is.EqualTo(sample.ActiveIndices),
                        pose + ": cached topology must match independently read current mesh triangles.");
                    Assert.That(sample.UniqueVertexTransforms, Is.LessThanOrEqualTo(sample.CornerCount),
                        pose + ": shared triangle corners must reuse their matrix transform.");
                    Assert.That(sample.UniqueVertexTransforms, Is.LessThanOrEqualTo(new HashSet<int>(sample.ActiveIndices).Count));
                    TestContext.Out.WriteLine($"Test {pose} {region} proxy: transforms={sample.UniqueVertexTransforms}, former corner transforms={sample.CornerCount + 1}.");
                    Bounds reference = new Bounds(sample.Matrix.MultiplyPoint3x4(sample.Vertices[sample.ActiveIndices[0]]), Vector3.zero);
                    // Preserve the original ordered fold, including its first
                    // point and every duplicate triangle vertex index.
                    foreach (int vertex in sample.ActiveIndices)
                        reference.Encapsulate(sample.Matrix.MultiplyPoint3x4(sample.Vertices[vertex]));
                    Vector3 size = Vector3.Max(reference.size, sample.Minimum);
                    for (int axis = 0; axis < 3; axis++)
                    {
                        Assert.That(BitConverter.SingleToInt32Bits(sample.NativeCentre[axis]),
                            Is.EqualTo(BitConverter.SingleToInt32Bits(reference.center[axis])), pose + ": " + region + " proxy centre must match the original fold bits.");
                        Assert.That(BitConverter.SingleToInt32Bits(sample.NativeSize[axis]),
                            Is.EqualTo(BitConverter.SingleToInt32Bits(size[axis])), pose + ": " + region + " proxy size must match the original fold bits.");
                    }
                    compared = true; break;
                }
                Assert.That(compared, Is.True, pose + ": the bound comparison must inspect actual " + region + " geometry.");
            }
            Assert.That(root.BodyEffects.ProxyGeometryBuilds, Is.EqualTo(builds),
                "Independent bound diagnostics must not mutate the production cache or satisfy its later invalidation assertions.");
        }

        private void AssertBodyProxyCollisionIsolation(CombatActor target, bool requireDebris, bool requireHead)
        {
            var proxies = new List<BoxCollider>();
            foreach (BoxCollider proxy in target.DamageRigRoot.GetComponentsInChildren<BoxCollider>(true))
                if (proxy.enabled && proxy.gameObject.activeInHierarchy &&
                    proxy.name.StartsWith("Remaining body ", StringComparison.Ordinal)) proxies.Add(proxy);
            Assert.That(proxies, Is.Not.Empty, "A damaged ragdoll must activate its retained physical body proxies.");
            int debrisPairs = 0;
            for (int i = 0; i < proxies.Count; i++)
            {
                BoxCollider proxy = proxies[i];
                foreach (var original in target.Ragdoll.PhysicsController.AnatomicalColliders)
                    Assert.That(Physics.GetIgnoreCollision(proxy, original.Key), Is.True,
                        "An activated proxy must retain collision exclusions with original anatomy.");
                if (target.Body != null) Assert.That(Physics.GetIgnoreCollision(proxy, target.Body), Is.True,
                    "An activated proxy must retain its actor body collision exclusion.");
                for (int j = i + 1; j < proxies.Count; j++)
                    Assert.That(Physics.GetIgnoreCollision(proxy, proxies[j]), Is.True,
                        "Activated body proxies must not collide with other retained proxies on the same actor.");
                foreach (CombatBodyFragment fragment in root.BodyEffects.GetComponentsInChildren<CombatBodyFragment>(true))
                {
                    if (fragment.Owner != target || !fragment.gameObject.activeInHierarchy) continue;
                    Collider collider = fragment.GetComponent<Collider>();
                    if (collider == null || !collider.enabled) continue;
                    Assert.That(Physics.GetIgnoreCollision(proxy, collider), Is.True,
                        "Newly released body debris must retain exclusions with the prewarmed actor proxies.");
                    debrisPairs++;
                }
            }
            if (requireDebris) Assert.That(debrisPairs, Is.GreaterThan(0), "The lifecycle proof must include real physical body debris.");
            var headProxies = new List<BoxCollider>();
            foreach (BoxCollider proxy in target.DamageRigRoot.GetComponentsInChildren<BoxCollider>(true))
                if (proxy.enabled && proxy.gameObject.activeInHierarchy &&
                    proxy.name.StartsWith("Retained Head Sector ", StringComparison.Ordinal)) headProxies.Add(proxy);
            if (requireHead) Assert.That(headProxies.Count, Is.GreaterThanOrEqualTo(2),
                "The known headshot must leave real retained sector pairs to inspect.");
            for (int i = 0; i < headProxies.Count; i++)
            {
                BoxCollider proxy = headProxies[i];
                foreach (var original in target.Ragdoll.PhysicsController.AnatomicalColliders)
                    Assert.That(Physics.GetIgnoreCollision(proxy, original.Key), Is.True,
                        "An activated head sector must retain exclusions with original anatomy.");
                if (target.Body != null) Assert.That(Physics.GetIgnoreCollision(proxy, target.Body), Is.True,
                    "An activated head sector must retain its actor body collision exclusion.");
                for (int j = i + 1; j < headProxies.Count; j++)
                    Assert.That(Physics.GetIgnoreCollision(proxy, headProxies[j]), Is.True,
                        "Retained head sectors must remain mutually excluded after activation and reactivation.");
            }
        }

        private static ProfilerRecorder CreateBodyAllocationRecorder()
        {
            // Unity Mono can return zero from GC.GetAllocatedBytesForCurrentThread.
            // Prove this current-thread recorder observes a known managed allocation.
            var recorder = new ProfilerRecorder(ProfilerCategory.Memory, "GC.Alloc", 1,
                ProfilerRecorderOptions.WrapAroundWhenCapacityReached | ProfilerRecorderOptions.SumAllSamplesInFrame |
                ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            Assert.That(recorder.Valid, Is.True);
            recorder.Start(); var control = new byte[16384]; recorder.Stop();
            long count = recorder.Count > 0 ? recorder.GetSample(0).Count : 0;
            System.GC.KeepAlive(control);
            Assert.That(count, Is.GreaterThan(0), "The allocation recorder must observe its positive control.");
            recorder.Reset(); return recorder;
        }

        private static long StopBodyAllocationRecorder(ProfilerRecorder recorder)
        { recorder.Stop(); return recorder.Count > 0 ? recorder.GetSample(0).Count : 0; }

        private static Ray BodyContactProbe(HeadContactSurface surface)
        {
            Vector3 centre = default, normal = default;
            float area = 0f;
            for (int i = 0; i < surface.Triangles.Length; i += 3)
            {
                Vector3 a = surface.Vertices[surface.Triangles[i]], b = surface.Vertices[surface.Triangles[i + 1]],
                    c = surface.Vertices[surface.Triangles[i + 2]];
                Vector3 cross = Vector3.Cross(b - a, c - a);
                if (cross.sqrMagnitude <= area) continue;
                area = cross.sqrMagnitude; normal = cross; centre = (a + b + c) / 3f;
            }
            Assert.That(area, Is.GreaterThan(1e-12f), "The visible contact probe needs a nondegenerate triangle.");
            normal /= Mathf.Sqrt(area);
            return new Ray(centre + normal * .04f, -normal);
        }

        private void AssertBodyClothRevisionCache()
        {
            var cloth = root.Hero.DamageRigRoot.GetComponentInParent<PlayerJacketCloth>();
            Assert.That(cloth, Is.Not.Null);
            SkinnedMeshRenderer source = null, template = null;
            foreach (SkinnedMeshRenderer skin in root.Hero.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (skin.name == "CLO_JacketBody") source = skin;
                else if (skin.name.EndsWith("__CLO_JacketBody")) template = skin;
            Assert.That(source, Is.Not.Null); Assert.That(template, Is.Not.Null);
            var host = new GameObject("Test cloth derivative");
            var skinTarget = host.AddComponent<SkinnedMeshRenderer>(); skinTarget.sharedMesh = template.sharedMesh;
            var deformation = new CombatBodySourceDeformation(template.sharedMesh, source);
            try
            {
                cloth.ApplyAt(0d, false, true, new WindSample(4f, 1f));
                deformation.Refresh(skinTarget);
                uint revision = deformation.GeometryVersion;
                Assert.That(revision, Is.GreaterThan(0u));
                deformation.Refresh(skinTarget);
                Assert.That(deformation.GeometryVersion, Is.EqualTo(revision), "An unchanged cloth mesh must reuse its derivative.");
                cloth.ApplyAt(.02d, false, true, new WindSample(4f, 1f));
                deformation.Refresh(skinTarget);
                Assert.That(deformation.GeometryVersion, Is.GreaterThan(revision),
                    "Two source cloth writes in the same frame must remain visible to body contacts.");
            }
            finally { deformation.Dispose(); UnityEngine.Object.Destroy(host); }
        }

        [UnityTest]
        public IEnumerator Range_BodyDestructionFollowsLocalContactsSurvivalCorpsePauseAndReset()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            yield return EnterRange(false, CombatWeaponId.Shotgun);
            Assert.That(root.BodyEffects, Is.Not.Null);
            int heroTissue = root.BodyEffects.TissueRendererCountFor(root.Hero);
            int opponentTissue = root.BodyEffects.TissueRendererCountFor(root.Opponent);

            root.ResetRound();
            PlacePair(6f);
            CaptureBodyDestruction(root.Opponent, "body-intact-before-shot", root.Opponent.transform.forward);
            var close = FireShotgunAtRegion(MeleeBodyRegion.Torso, 1f, 1);
            Assert.That(close, Is.Not.Empty, "The comparison must use actual projectile contacts.");
            float nearLoss = TotalBodyTissueLoss(root.Opponent);
            Assert.That(nearLoss, Is.GreaterThan(0f));
            Assert.That(root.Opponent.BodyDamage.TissueLoss(BodyDamageRegion.LeftShin, 0), Is.Zero);
            Assert.That(root.Opponent.BodyDamage.TissueLoss(BodyDamageRegion.RightForearm, 0), Is.Zero,
                "A torso volley cannot assign missed limb damage.");
            if (root.BodyEffects.ExposedBoneCountFor(root.Opponent) == 0)
            {
                foreach (CombatImpact impact in close)
                    Debug.Log($"Test body impact: part={impact.Part}, region={impact.BodyRegion}, patch={impact.BodyPatch}, wound={impact.WoundDamage:F3}");
                for (int region = 0; region < CombatBodyDamageState.RegionCount; region++)
                {
                    var part = (BodyDamageRegion)region;
                    Debug.Log($"Test body tissue {part}: " +
                        $"{root.Opponent.BodyDamage.TissueLoss(part, 0):F3}, " +
                        $"{root.Opponent.BodyDamage.TissueLoss(part, 1):F3}, " +
                        $"{root.Opponent.BodyDamage.TissueLoss(part, 2):F3}, " +
                        $"{root.Opponent.BodyDamage.TissueLoss(part, 3):F3}");
                }
            }
            Assert.That(root.BodyEffects.ExposedBoneCountFor(root.Opponent), Is.GreaterThan(0),
                "The actual pointblank torso volley must visibly uncover bone.");
            yield return LetBodyFragmentsSeparate();
            CaptureBodyDestruction(root.Opponent, "body-close-shotgun-front", root.Opponent.transform.forward);
            root.ResetRound();
            PlacePair(6f);
            FireShotgunAtRegion(MeleeBodyRegion.Torso, 15f, 2);
            Assert.That(TotalBodyTissueLoss(root.Opponent), Is.LessThan(nearLoss));
            Assert.That(root.HeadEffects.ExposedSkullSectorCountFor(root.Opponent), Is.Zero,
                "The distant torso volley must not prematurely expose head anatomy.");
            yield return LetBodyFragmentsSeparate();
            CaptureBodyDestruction(root.Opponent, "body-distant-shotgun-front", root.Opponent.transform.forward);

            root.ResetRound();
            PlacePair(6f);
            ApplyPreparedShotgunHead(12, false, 10);
            Assert.That(root.HeadEffects.DetachedSectorCountFor(root.Opponent), Is.EqualTo(14),
                "Body tissue loss must preserve the existing fourteen-sector shell cap.");

            foreach (bool heroVictim in new[] { false, true })
            {
                root.ResetRound();
                PlacePair(6f);
                CombatActor target = heroVictim ? root.Hero : root.Opponent;
                CombatActor source = heroVictim ? root.Opponent : root.Hero;
                Transform forearm = FindAnatomicalBone(target, "forearm.R");
                Transform hand = FindAnatomicalBone(target, "hand.R");
                Vector3 oldForearmCenter = Vector3.Lerp(forearm.position, hand.position, .6f);
                CombatImpact limb = ApplyPreparedBodyForearmVolley(target, source, 3);
                Assert.That(target.State.Health, Is.GreaterThan(0f), "A concentrated limb volley must preserve life.");
                Assert.That(target.State.IsDefeated, Is.False, "Limb separation is survivable for both actors.");
                Assert.That(target.BodyDamage.IsAttached(BodyDamageRegion.RightForearm), Is.False);
                Assert.That(target.BodyDamage.IsAttached(BodyDamageRegion.RightHand), Is.False);
                Assert.That(target.BodyDamage.IsAttached(BodyDamageRegion.RightUpperArm), Is.True);
                Assert.That(target.IsWeaponDropped, Is.True);
                Assert.That(target.TryAttack(), Is.False);
                Assert.That(target.RequestPistolShot(), Is.False);
                Assert.That(target.TryReloadPistol(), Is.False);
                Assert.That(target.BodyDamage.CanStand, Is.True);
                Assert.That(root.BodyEffects.DetachedPartCountFor(target), Is.GreaterThan(0));
                target.CaptureContactPose();
                Vector3 rayAxis = target.transform.right;
                if (target.Hurtboxes.SweepProjectile(oldForearmCenter + rayAxis * .2f,
                    oldForearmCenter - rayAxis * .2f, .005f, -rayAxis, out CombatHurtboxes.Hit gap))
                {
                    if (gap.Part == Player3DAnatomicalPart.RightForearm || gap.Part == Player3DAnatomicalPart.RightHand)
                        Assert.That(gap.IsDetached, Is.True,
                            "A severed surface remains hittable at its own pose; attached ghost anatomy must disappear.");
                }
                int detached = root.BodyEffects.DetachedPartCountFor(target);
                root.BodyEffects.SynchronizeActor(target, limb);
                Assert.That(root.BodyEffects.DetachedPartCountFor(target), Is.EqualTo(detached),
                    "Repeated synchronization cannot duplicate finite detached parts.");
                yield return LetBodyFragmentsSeparate();
                CaptureBodyDestruction(target, heroVictim ? "body-hero-forearm-cut" : "body-opponent-forearm-cut",
                    target.transform.forward);

                root.ResetRound();
                PlacePair(6f);
                ApplyKnownBodyContact(target, source, BodyDamageRegion.LeftHand, 0, 2f, 4);
                Assert.That(target.BodyDamage.CanUseLeftHand, Is.False);
                Assert.That(target.BodyDamage.CanUseRightHand, Is.True);
                Assert.That(target.State.IsDefeated, Is.False);
                target.SetBlock(true);
                Assert.That(target.State.IsBlocking, Is.False, "Two-hand guard needs both hands.");
                Assert.That(target.TryReloadPistol(), Is.False);
                if (heroVictim)
                    Assert.That(target.RequestPistolShot(), Is.False, "A shotgun needs its fore-end support.");
                else
                    Assert.That(target.TryAttack(), Is.True, "The surviving right hand can still use a crowbar.");

                root.ResetRound();
                PlacePair(6f);
                ApplyKnownBodyContact(target, source, BodyDamageRegion.RightShin, 0, 2f, 5);
                Assert.That(target.State.IsDefeated, Is.False);
                Assert.That(target.IsKnockedDown || target.IsRagdollActive, Is.True);
                Assert.That(target.BodyDamage.CanStand || target.BodyDamage.CanRise, Is.False);
                Assert.That(target.BodyDamage.CanCrawl, Is.True);
                Assert.That(target.TryStep(Vector2.up), Is.False);
                Assert.That(target.TryKick(), Is.False);
                for (int i = 0; i < 90 && !target.Ragdoll.HasGroundContact; i++)
                {
                    root.Tick(CombatTestRoot.SimulationStep);
                    yield return new WaitForFixedUpdate();
                }
                Assert.That(target.IsBodyGrounded && target.Ragdoll.HasGroundContact, Is.True,
                    "Crawling must start from actual floor support.");
                Vector3 crawlStart = target.BodyWorldPosition;
                target.SetCrawlInput(Vector2.up);
                for (int i = 0; i < 90; i++)
                {
                    root.Tick(CombatTestRoot.SimulationStep);
                    yield return new WaitForFixedUpdate();
                }
                target.ClearCrawlInput();
                Assert.That(Vector3.ProjectOnPlane(target.BodyWorldPosition - crawlStart, Vector3.up).magnitude,
                    Is.GreaterThan(.025f), "The living body must move with strokes of its remaining limbs.");
                Assert.That(target.State.IsDefeated, Is.False);
                Assert.That(target.State.Phase, Is.Not.EqualTo(MeleePhase.Rising),
                    "The ordinary rise must not restore a fighter without leg support.");
                CaptureBodyDestruction(target, heroVictim ? "body-hero-leg-survivor" : "body-opponent-leg-survivor",
                    target.transform.forward);

                root.ResetRound();
                PlacePair(6f);
                // First create an actual corpse, then use explicit regional contacts to
                // inspect every authored layer without relying on random pellet spread.
                if (!heroVictim) FireShotgunAtRegion(MeleeBodyRegion.Torso, 1f, 6);
                else DefeatBodyTargetWithProjectile(target, source, 6);
                Assert.That(target.State.IsDefeated && target.IsRagdollActive, Is.True);
                float corpseHealth = target.State.Health;
                for (int region = 0; region < CombatBodyDamageState.RegionCount; region++)
                    for (int patch = 0; patch < CombatBodyDamageState.PatchCount; patch++)
                    {
                        float remaining = 1f - target.BodyDamage.TissueLoss((BodyDamageRegion)region, patch);
                        if (remaining > .0001f)
                            ApplyKnownBodyContact(target, source, (BodyDamageRegion)region, patch,
                                remaining, 20 + region * CombatBodyDamageState.PatchCount + patch);
                    }
                Assert.That(target.State.Health, Is.EqualTo(corpseHealth));
                Assert.That(root.BodyEffects.ExposedBoneCountFor(target), Is.GreaterThan(8));
                Assert.That(root.HeadEffects.ExposedSkullSectorCountFor(target), Is.GreaterThan(0),
                    "Whole-body tissue loss must expose the authored skull as well as the body bones.");
                Assert.That(root.HeadEffects.RetainedBrainCountFor(target), Is.Zero,
                    "The completed skeleton must not retain soft brain tissue inside its exposed skull.");
                yield return LetBodyFragmentsSeparate(60);
                CaptureBodyDestruction(target, heroVictim ? "body-hero-skeleton-front" : "body-opponent-skeleton-front",
                    target.transform.forward);
                CaptureBodyDestruction(target, heroVictim ? "body-hero-skeleton-rear" : "body-opponent-skeleton-rear",
                    -target.transform.forward);
                CaptureBodyDestruction(target, heroVictim ? "body-hero-skeleton-side" : "body-opponent-skeleton-side",
                    target.transform.right);

                ApplyKnownBodyContact(target, source, BodyDamageRegion.LeftForearm, 0, .4f, 100);
                Assert.That(target.BodyDamage.IsAttached(BodyDamageRegion.LeftForearm), Is.False,
                    "Already exposed corpses still receive structural damage.");
                int fragments = root.BodyEffects.ActiveFragmentCount;
                float debrisAge = root.BodyEffects.DebrisAgeFor(target);
                Assert.That(root.PauseMenu.Open(), Is.True);
                CombatBodyFragment[] pausedFragments = root.BodyEffects.GetComponentsInChildren<CombatBodyFragment>();
                Assert.That(pausedFragments.Length, Is.EqualTo(fragments).And.GreaterThan(0));
                var pausedParts = new Rigidbody[pausedFragments.Length];
                var pausedPositions = new Vector3[pausedFragments.Length];
                for (int part = 0; part < pausedFragments.Length; part++)
                {
                    pausedParts[part] = pausedFragments[part].GetComponent<Rigidbody>();
                    Assert.That(pausedParts[part], Is.Not.Null);
                    pausedPositions[part] = pausedParts[part].position;
                }
                root.Tick(.4f);
                yield return null;
                Assert.That(root.BodyEffects.ActiveFragmentCount, Is.EqualTo(fragments));
                Assert.That(root.BodyEffects.DebrisAgeFor(target), Is.EqualTo(debrisAge));
                for (int part = 0; part < pausedParts.Length; part++)
                    Assert.That(pausedParts[part].position, Is.EqualTo(pausedPositions[part]));
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause did not release body damage input.");
            }

            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            yield return EnterRange(false, CombatWeaponId.Pistol);
            root.ResetRound();
            PlacePairWithReloadReserve(6f);
            Assert.That(root.SetOpponentFocus(false), Is.True);
            CombatActor pistolSurvivor = root.Hero;
            ApplyKnownBodyContact(pistolSurvivor, root.Opponent, BodyDamageRegion.LeftHand, 0, 2f, 101);
            Assert.That(pistolSurvivor.BodyDamage.CanUseRightHand, Is.True);
            Assert.That(pistolSurvivor.IsWeaponDropped, Is.False);
            Vector3 aimPoint = root.Opponent.transform.position + Vector3.up * 1.25f;
            pistolSurvivor.SetPistolAim(true, aimPoint);
            root.Tick(.7f);
            int loaded = pistolSurvivor.Pistol.Rounds;
            Assert.That(pistolSurvivor.RequestPistolShot(), Is.True);
            root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(pistolSurvivor.Pistol.Rounds, Is.EqualTo(loaded - 1),
                "The loaded pistol must remain usable with the surviving right hand.");
            Assert.That(pistolSurvivor.TryReloadPistol(), Is.False);
            AssertSurvivorPistolGrip(pistolSurvivor, "Test standing one-hand shot");
            CaptureBodyDestruction(pistolSurvivor, "body-survivor-one-hand-pistol", pistolSurvivor.transform.forward);
            ApplyKnownBodyContact(pistolSurvivor, root.Opponent, BodyDamageRegion.RightShin, 0, 2f, 102);
            for (int frame = 0; frame < 90 && !pistolSurvivor.Ragdoll.HasGroundContact; frame++)
            {
                root.Tick(Time.fixedDeltaTime);
                yield return new WaitForFixedUpdate();
            }
            Assert.That(pistolSurvivor.CanUseGroundedFirearm, Is.True);
            pistolSurvivor.SetPistolAim(false, aimPoint);
            root.Tick(Time.fixedDeltaTime);
            yield return new WaitForFixedUpdate();
            AssertSurvivorPistolGrip(pistolSurvivor, "Test landed without aim");
            pistolSurvivor.SetCrawlInput(Vector2.up);
            for (int frame = 0; frame < 12; frame++)
            {
                root.Tick(Time.fixedDeltaTime);
                yield return new WaitForFixedUpdate();
                AssertSurvivorPistolGrip(pistolSurvivor, "Test crawl with loaded pistol");
            }
            pistolSurvivor.ClearCrawlInput();
            pistolSurvivor.SetPistolAim(true, aimPoint);
            float minimumAimError = float.PositiveInfinity;
            for (int frame = 0; frame < 180; frame++)
            {
                root.Tick(Time.fixedDeltaTime);
                yield return new WaitForFixedUpdate();
                pistolSurvivor.Present();
                minimumAimError = Mathf.Min(minimumAimError, pistolSurvivor.PistolAimErrorDegrees);
                if (frame > 35 && pistolSurvivor.PistolAimAligned) break;
            }
            float groundedGripError = Vector3.Distance(
                CombatPistolAssetProvider.FindAnchor(pistolSurvivor.Weapon, "Grip").position,
                pistolSurvivor.GetComponentInChildren<NpcHandPose>().CylinderCentre(false));
            string aimState = $"minimum={minimumAimError:F2} degrees, final={pistolSurvivor.PistolAimErrorDegrees:F2} degrees; " +
                $"requested={pistolSurvivor.Firearm.AimRequested}, aiming={pistolSurvivor.Firearm.IsAiming}, " +
                $"allowed={pistolSurvivor.CanUseGroundedFirearm}, simulating={pistolSurvivor.Ragdoll.PhysicsController.IsSimulating}, " +
                $"grip={groundedGripError:F4} m; {pistolSurvivor.Ragdoll.PhysicsController.SurvivorArmAimDiagnostics}";
            if (!pistolSurvivor.PistolAimAligned)
            {
                Debug.Log("Test grounded aim: " + aimState);
                CaptureBodyDestruction(pistolSurvivor, "body-survivor-grounded-unready", pistolSurvivor.transform.forward);
            }
            Assert.That(pistolSurvivor.PistolAimAligned, Is.True,
                "The grounded shot needs the actual visible muzzle aligned by the surviving arm. " + aimState);
            AssertSurvivorPistolGrip(pistolSurvivor, "Test grounded aim");
            loaded = pistolSurvivor.Pistol.Rounds;
            Assert.That(pistolSurvivor.RequestPistolShot(), Is.True);
            root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(pistolSurvivor.Pistol.Rounds, Is.EqualTo(loaded - 1));
            Assert.That(pistolSurvivor.State.IsDefeated, Is.False);
            Assert.That(pistolSurvivor.TryReloadPistol(), Is.False);
            AssertSurvivorPistolGrip(pistolSurvivor, "Test grounded shot");
            CaptureBodyDestruction(pistolSurvivor, "body-survivor-grounded-pistol", pistolSurvivor.transform.forward);
            pistolSurvivor.SetPistolAim(false, aimPoint);
            root.Tick(Time.fixedDeltaTime);
            yield return new WaitForFixedUpdate();
            AssertSurvivorPistolGrip(pistolSurvivor, "Test grounded aim release");

            root.ResetRound();
            Assert.That(root.BodyEffects.ActiveFragmentCount, Is.Zero);
            Assert.That(root.BodyEffects.TissueRendererCountFor(root.Hero), Is.EqualTo(heroTissue));
            Assert.That(root.BodyEffects.TissueRendererCountFor(root.Opponent), Is.EqualTo(opponentTissue));
            foreach (CombatActor actor in new[] { root.Hero, root.Opponent })
            {
                Assert.That(root.BodyEffects.DetachedPartCountFor(actor), Is.Zero);
                Assert.That(root.BodyEffects.ExposedBoneCountFor(actor), Is.Zero);
                Assert.That(actor.BodyDamage.CanUseLeftHand && actor.BodyDamage.CanUseRightHand &&
                    actor.BodyDamage.CanStand && actor.BodyDamage.CanRise, Is.True);
                Assert.That(TotalBodyTissueLoss(actor), Is.Zero);
                Assert.That(actor.IsRagdollActive, Is.False);
            }
            LogAssert.NoUnexpectedReceived();
        }

        private CombatImpact ApplyPreparedBodyForearmVolley(CombatActor target, CombatActor source, int sequence)
        {
            target.CaptureContactPose();
            Transform forearm = FindAnatomicalBone(target, "forearm.R");
            Transform hand = FindAnatomicalBone(target, "hand.R");
            Vector3 center = Vector3.Lerp(forearm.position, hand.position, .5f);
            CombatHurtboxes.Hit contact = default;
            bool found = false;
            foreach (Vector3 approach in new[] { target.transform.right, -target.transform.right,
                target.transform.forward, -target.transform.forward, Vector3.up, Vector3.down })
                if (target.Hurtboxes.SweepProjectile(center + approach * .35f, center - approach * .12f,
                    CombatProjectilePool.Radius, -approach, out CombatHurtboxes.Hit hit) &&
                    hit.Part == Player3DAnatomicalPart.RightForearm)
                { contact = hit; found = true; break; }
            Assert.That(found, Is.True, "The authored forearm must expose an actual projectile contact.");
            var pellets = new List<CombatProjectilePool.PelletHit>();
            for (int pellet = 0; pellet < root.Hero.Shotgun.Settings.PelletCount; pellet++)
                pellets.Add(new CombatProjectilePool.PelletHit(contact, contact.Direction * CombatProjectilePool.MuzzleSpeed,
                    1f, pellet, root.Hero.Shotgun.Settings));
            CombatImpact last = default;
            void Observe(CombatImpact impact) => last = impact;
            target.ImpactReceived += Observe;
            try { target.ReceiveShotgunVolley(source, sequence, pellets, true); }
            finally { target.ImpactReceived -= Observe; }
            Assert.That(last.Target, Is.SameAs(target));
            return last;
        }

        private CombatImpact ApplyKnownBodyContact(CombatActor target, CombatActor source,
            BodyDamageRegion region, int patch, float trauma, int sequence)
        {
            Vector3 point = FindAnatomicalBone(target, CombatBodyAnatomy.BoneName(region)).position;
            var impact = new CombatImpact(source, target, sequence, point, target.transform.forward,
                -target.transform.forward, target.State.Health, target.State.Health, MeleeHitResult.Hit,
                new MeleeHitLocation(CombatBodyAnatomy.CombatRegion(region), MeleeHitSide.Front),
                part: CombatBodyAnatomy.ToPart(region), kind: CombatImpactKind.Projectile,
                bodyRegion: region, bodyPatch: patch);
            Assert.That(target.BodyDamage.Apply(region, patch, trauma, source.GetEntityId().GetHashCode(), sequence, 0), Is.True);
            root.BodyEffects.SynchronizeActor(target, impact);
            target.ApplyBodyCapabilities(impact);
            return impact;
        }

        private static void DefeatBodyTargetWithProjectile(CombatActor target, CombatActor source, int sequence)
        {
            target.CaptureContactPose();
            Assert.That(target.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head, out Vector3 center,
                out Vector3 outward, out _), Is.True);
            Assert.That(target.Hurtboxes.SweepProjectile(center + outward * .5f, center - outward * .5f,
                CombatProjectilePool.Radius, -outward, out CombatHurtboxes.Hit hit), Is.True);
            target.ReceiveProjectile(source, sequence, hit, -outward * CombatProjectilePool.MuzzleSpeed);
        }

        private static float TotalBodyTissueLoss(CombatActor actor)
        {
            float loss = 0f;
            for (int region = 0; region < CombatBodyDamageState.RegionCount; region++)
                for (int patch = 0; patch < CombatBodyDamageState.PatchCount; patch++)
                    loss += actor.BodyDamage.TissueLoss((BodyDamageRegion)region, patch);
            return loss;
        }

        private IEnumerator LetBodyFragmentsSeparate(int steps = 12)
        {
            for (int frame = 0; frame < steps; frame++)
            {
                root.Tick(CombatTestRoot.SimulationStep);
                yield return new WaitForFixedUpdate();
            }
        }

        private static void AssertSurvivorPistolGrip(CombatActor actor, string context)
        {
            actor.Present();
            var hands = actor.GetComponentInChildren<NpcHandPose>();
            Assert.That(hands, Is.Not.Null);
            Transform grip = CombatPistolAssetProvider.FindAnchor(actor.Weapon, "Grip");
            Assert.That(grip, Is.Not.Null);
            Assert.That(Vector3.Distance(grip.position, hands.CylinderCentre(false)), Is.LessThan(.003f),
                context + ": the loaded weapon must remain in its actual physical palm.");
        }

        private void CaptureBodyDestruction(CombatActor target, string name, Vector3 outward)
        {
            Camera camera = root.CameraFollow.Camera;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            try
            {
                Vector3 subject = target.Ragdoll.IsActive
                    ? target.Ragdoll.PelvisBody.position + Vector3.up * .15f
                    : target.transform.position + Vector3.up * .95f;
                outward = outward.sqrMagnitude > .01f ? outward.normalized : Vector3.forward;
                Vector3 side = Vector3.Cross(outward, Vector3.up).normalized;
                Vector3 eye = subject + outward * 2.7f + side * .45f + Vector3.up * .8f;
                Vector3 sight = eye - subject;
                float wallDistance = float.PositiveInfinity;
                foreach (RaycastHit hit in Physics.RaycastAll(subject, sight.normalized, sight.magnitude,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    if (hit.collider.name.StartsWith("Collision_Wall") && hit.distance < wallDistance)
                    {
                        wallDistance = hit.distance;
                        eye = hit.point + hit.normal * .15f;
                    }
                camera.fieldOfView = 45f;
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(subject - eye));
                string path = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest, name + ".png");
                LogAssert.Expect(LogType.Log, "Area capture wrote " + path);
                AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.CombatTest, name);
            }
            finally { camera.transform.SetPositionAndRotation(position, rotation); camera.fieldOfView = fov; }
        }
    }
}
