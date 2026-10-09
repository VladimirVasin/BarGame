using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
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
            PlacePair(6f);
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
