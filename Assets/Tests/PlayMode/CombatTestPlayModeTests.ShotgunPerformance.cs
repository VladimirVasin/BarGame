using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        [UnityTest]
        [Order(0)]
        [PrebuildSetup(typeof(CombatTorsoAssetsSetup))]
        public IEnumerator Range_DistantShotgunTorsoVolleyProfilesImpactPhases()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            yield return EnterRange(false, CombatWeaponId.Shotgun);
            using var allocations = CreateBodyAllocationRecorder();
            var timer = new Stopwatch();
            var allSamples = new List<DistantShotgunSample>(384);
            foreach (bool hurtHero in new[] { false, true })
            {
                root.ResetRound(); PlacePair(6f);
                CombatActor target = hurtHero ? root.Hero : root.Opponent;
                CombatActor source = hurtHero ? root.Opponent : root.Hero;
                string subject = hurtHero ? "hero" : "opponent";
                // Read the already prepared surfaces before recording the first shell;
                // this does not capture, skin or warm their deferred contact geometry.
                List<CombatTorsoDamageSurface> surfaces = DistantShotgunTorsoSurfaces(target,
                    out List<CombatBodyDestruction.Piece> pieces);
                var authoredFleshMeshes = new Mesh[pieces.Count];
                for (int i = 0; i < pieces.Count; i++)
                    if (pieces[i].Flesh && pieces[i].Torso != null) authoredFleshMeshes[i] = pieces[i].Skin.sharedMesh;
                target.CaptureContactPose();
                Vector3 point = target.Ragdoll.PhysicsController.ChestBody.worldCenterOfMass;
                Vector3 outward = target.transform.forward;
                var spread = root.Hero.Shotgun.Settings.PelletSpread(0, 1);
                Quaternion direction = Quaternion.LookRotation(-outward) * Quaternion.FromToRotation(
                    new Vector3(spread.x, spread.y, 1f).normalized, Vector3.forward);
                var impacts = new List<CombatImpact>(36);
                var samples = new List<DistantShotgunSample>(192);
                Action capture = target.CaptureContactPose;
                Action advance = () => root.Projectiles.Advance(CombatTestRoot.SimulationStep, root.Hero, root.Opponent);
                Action apply = root.Projectiles.ApplyContacts;
                Action postflight = () => root.Tick(CombatTestRoot.SimulationStep * 2f);
                Action postflightFrame = () => root.TickFrame(CombatTestRoot.SimulationStep * 2f);
                float frozenSeconds = root.HitStopSecondsConsumed;
                bool spawned = false;
                Action spawn = () => spawned = root.Projectiles.TrySpawnVolley(source, point + outward * 15f,
                    direction, 1, root.Hero.Shotgun.Settings);
                target.ImpactReceived += impacts.Add;
                try
                {
                    samples.Add(MeasureDistantShotgunPhase(target, allocations, timer, "spawn", 0, spawn));
                    Assert.That(spawned, Is.True);
                    for (int step = 0; step < 60 && root.Projectiles.ActiveCount > 0; step++)
                    {
                        DistantShotgunSample snapshot = MeasureDistantShotgunPhase(target, allocations, timer, "capture", step, capture);
                        samples.Add(snapshot);
                        Assert.That(snapshot.GeometryBuilds, Is.Zero,
                            "A frozen snapshot must not build precise body triangles before a flight query.");
                        samples.Add(MeasureDistantShotgunPhase(target, allocations, timer, "advance", step, advance));
                        samples.Add(MeasureDistantShotgunPhase(target, allocations, timer, "contacts", step, apply));
                    }
                    Assert.That(root.Projectiles.ActiveCount, Is.Zero, "The bounded distant volley must finish its flight.");
                    Assert.That(impacts.Count, Is.InRange(1, root.Hero.Shotgun.Settings.PelletCount));
                    Assert.That(target.ShotgunVolleyResponseCount, Is.EqualTo(1));
                    Assert.That(target.State.IsDefeated, Is.False,
                        "The distant hit must remain nonterminal to exercise uninterrupted gameplay.");
                    Assert.That(root.Player.Motor.OwnedMovementFrozen, Is.False,
                        "A nonterminal shotgun impact must not freeze the shooter's movement on the contact frame.");
                    float torsoLoss = 0f;
                    for (int region = (int)BodyDamageRegion.Chest; region <= (int)BodyDamageRegion.Pelvis; region++)
                        for (int patch = 0; patch < CombatBodyDamageState.PatchCount; patch++)
                            torsoLoss += target.BodyDamage.TissueLoss((BodyDamageRegion)region, patch);
                    Assert.That(torsoLoss, Is.GreaterThan(0f), "The timed flight must actually wound the torso.");
                    foreach (CombatImpact impact in impacts)
                        Assert.That(impact.IsPellet, Is.True, "The profile must measure real shotgun contacts.");
                    Assert.That(root.BloodEffects.ProjectileWoundCountFor(target), Is.GreaterThan(0),
                        "The real weak torso impact must retain its wound on the body.");
                    Assert.That(root.BloodEffects.TryGetProjectileWound(target, 0, out _, out _), Is.True);

                    Assert.That(surfaces, Is.Not.Empty);
                    CombatTorsoDamageSurface exterior = null;
                    int woundedHiddenFlesh = 0;
                    for (int i = 0; i < pieces.Count; i++)
                    {
                        CombatBodyDestruction.Piece piece = pieces[i];
                        if (piece.Torso == null) continue;
                        if (exterior == null && !piece.Flesh && piece.Torso.HasDamage) exterior = piece.Torso;
                        if (!piece.Flesh || piece.Torso.HasGeometry) continue;
                        Assert.That(piece.Skin.sharedMesh, Is.SameAs(authoredFleshMeshes[i]),
                            "Weak damage must retain the authored mesh while no flesh cells are visible.");
                        Assert.That(piece.Torso.TopologyBuilds, Is.Zero,
                            "Invisible flesh must retain damage without building hidden wound topology.");
                        if (piece.Torso.HasDamage) woundedHiddenFlesh++;
                    }
                    Assert.That(woundedHiddenFlesh, Is.GreaterThan(0),
                        "The weak real torso impact must exercise lazy hidden flesh rather than only untouched surfaces.");
                    Vector3 miss = target.transform.position + Vector3.one * 500f;
                    bool changed = false;
                    Action samePoseMiss = () =>
                    {
                        foreach (CombatTorsoDamageSurface surface in surfaces)
                            changed |= surface.Apply(miss, .075f, .1f);
                    };
                    samePoseMiss(); // Warm the unchanged-pose path without creating a wound.
                    var cellTests = new int[surfaces.Count];
                    var poseCaptures = new int[surfaces.Count];
                    var surfaceRejects = new int[surfaces.Count];
                    for (int i = 0; i < surfaces.Count; i++)
                    {
                        cellTests[i] = surfaces[i].CellTests;
                        poseCaptures[i] = surfaces[i].PoseCaptures;
                        surfaceRejects[i] = surfaces[i].SurfaceRejects;
                    }
                    DistantShotgunSample direct = MeasureDistantShotgunPhase(target, allocations, timer,
                        "torso same-pose miss", 0, samePoseMiss);
                    samples.Add(direct);
                    Assert.That(changed, Is.False, "A broadphase miss must neither consume cells nor deepen a wound.");
                    Assert.That(direct.Allocations, Is.Zero,
                        "A repeated same-pose torso miss must reuse its prepared bones and bounds without allocating arrays.");
                    for (int i = 0; i < surfaces.Count; i++)
                    {
                        Assert.That(surfaces[i].CellTests, Is.EqualTo(cellTests[i]), "A rejected surface must not scan its cells.");
                        Assert.That(surfaces[i].PoseCaptures, Is.EqualTo(poseCaptures[i]), "An unchanged pose must keep its bounds.");
                        Assert.That(surfaces[i].SurfaceRejects, Is.EqualTo(surfaceRejects[i] + 1));
                    }

                    Vector3 originalPosition = target.transform.position;
                    try
                    {
                        target.transform.position += Vector3.right * .05f;
                        DistantShotgunSample moved = MeasureDistantShotgunPhase(target, allocations, timer,
                            "torso moved-pose miss", 0, samePoseMiss);
                        samples.Add(moved);
                        Assert.That(changed, Is.False);
                        Assert.That(moved.Allocations, Is.Zero, "Moving an existing rig must invalidate bounds without allocating bone arrays.");
                        for (int i = 0; i < surfaces.Count; i++)
                        {
                            Assert.That(surfaces[i].CellTests, Is.EqualTo(cellTests[i]));
                            Assert.That(surfaces[i].PoseCaptures, Is.EqualTo(poseCaptures[i] + 1),
                                "The shifted live rig must supply new conservative bounds before rejecting the same miss.");
                            Assert.That(surfaces[i].SurfaceRejects, Is.EqualTo(surfaceRejects[i] + 2));
                        }
                    }
                    finally { target.transform.position = originalPosition; }

                    Assert.That(exterior, Is.Not.Null, "A real torso contact must damage an existing exterior surface.");
                    exterior.PrepareSource(); exterior.Refresh(1); // Warm the source-version-only update.
                    int topology = exterior.TopologyBuilds;
                    uint geometryVersion = exterior.GeometryVersion;
                    Action refresh = () => { exterior.PrepareSource(); exterior.Refresh(2); };
                    DistantShotgunSample refreshed = MeasureDistantShotgunPhase(target, allocations, timer,
                        "torso source refresh", 0, refresh);
                    samples.Add(refreshed);
                    Assert.That(exterior.GeometryVersion, Is.GreaterThan(geometryVersion));
                    Assert.That(exterior.TopologyBuilds, Is.EqualTo(topology),
                        "A changed source pose must refresh vertices without rebuilding unchanged wound topology.");
                    Assert.That(refreshed.Allocations, Is.Zero,
                        "A warmed exterior refresh must reuse its vertex, normal and tangent buffers.");

                    for (int frame = 0; frame < 8; frame++)
                    {
                        samples.Add(MeasureDistantShotgunPhase(target, allocations, timer, "postflight", frame, postflight));
                        Assert.That(root.HitStopSecondsConsumed, Is.EqualTo(frozenSeconds),
                            "Live post-impact frames must not consume a global shotgun hitstop.");
                        yield return null;
                    }
                    int firstShellContacts = impacts.Count;
                    point = target.Ragdoll.PhysicsController.ChestBody.worldCenterOfMass;
                    outward = -target.transform.forward;
                    spread = root.Hero.Shotgun.Settings.PelletSpread(0, 2);
                    direction = Quaternion.LookRotation(-outward) * Quaternion.FromToRotation(
                        new Vector3(spread.x, spread.y, 1f).normalized, Vector3.forward);
                    Action secondSpawn = () => spawned = root.Projectiles.TrySpawnVolley(source, point + outward * 15f,
                        direction, 2, root.Hero.Shotgun.Settings);
                    samples.Add(MeasureDistantShotgunPhase(target, allocations, timer, "spawn", 0, secondSpawn, 2));
                    Assert.That(spawned, Is.True);
                    for (int step = 0; step < 60 && root.Projectiles.ActiveCount > 0; step++)
                    {
                        DistantShotgunSample snapshot = MeasureDistantShotgunPhase(target, allocations, timer, "capture", step, capture, 2);
                        samples.Add(snapshot);
                        Assert.That(snapshot.GeometryBuilds, Is.Zero);
                        samples.Add(MeasureDistantShotgunPhase(target, allocations, timer, "advance", step, advance, 2));
                        samples.Add(MeasureDistantShotgunPhase(target, allocations, timer, "contacts", step, apply, 2));
                    }
                    Assert.That(root.Projectiles.ActiveCount, Is.Zero);
                    Assert.That(impacts.Count - firstShellContacts, Is.InRange(1, root.Hero.Shotgun.Settings.PelletCount),
                        "The second actual shell must contact the same damaged actor without a reset.");
                    Assert.That(target.ShotgunVolleyResponseCount, Is.EqualTo(2));
                    Assert.That(target.State.IsDefeated, Is.False,
                        "The repeated distant hit must still exercise the nonterminal response.");
                    Assert.That(root.Player.Motor.OwnedMovementFrozen, Is.False,
                        "A second nonterminal shell must not reintroduce a global movement freeze.");
                    float secondTorsoLoss = 0f;
                    for (int region = (int)BodyDamageRegion.Chest; region <= (int)BodyDamageRegion.Pelvis; region++)
                        for (int patch = 0; patch < CombatBodyDamageState.PatchCount; patch++)
                            secondTorsoLoss += target.BodyDamage.TissueLoss((BodyDamageRegion)region, patch);
                    Assert.That(secondTorsoLoss, Is.GreaterThan(torsoLoss),
                        "The second measured impact must deepen actual torso damage rather than profile a miss.");
                    samples.Add(MeasureDistantShotgunPhase(target, allocations, timer,
                        "postflight", 0, postflightFrame, 2));
                    Assert.That(root.HitStopSecondsConsumed, Is.EqualTo(frozenSeconds),
                        "The next actual simulation frame must advance without spending a contact freeze.");
                    Assert.That(root.Player.Motor.OwnedMovementFrozen, Is.False);
                    if (!hurtHero)
                    {
                        // Match the live aimed shot path without resetting the injured body.
                        // Camera ownership and reflection setup stay outside the measurement.
                        Assert.That(root.SetOpponentFocus(false), Is.True);
                        var shoulder = (Transform)typeof(CombatTestRoot).GetField("heroChest",
                            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(root);
                        point = target.Ragdoll.PhysicsController.ChestBody.worldCenterOfMass + Vector3.up * .12f;
                        outward = target.transform.forward;
                        Vector3 eye = point + outward * 8f + Vector3.up * .15f;
                        Camera camera = root.CameraFollow.Camera;
                        camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(point - eye));
                        Assert.That(root.CameraFollow.SetFreeAim(root, shoulder, preserveCurrentPose: true), Is.True);
                        root.Hero.SetPistolAim(true, point);
                        Assert.That(root.Hero.IsFreePistolAiming && root.CameraFollow.FreeAimActive, Is.True);
                        spread = root.Hero.Shotgun.Settings.PelletSpread(0, 3);
                        direction = Quaternion.LookRotation(-outward) * Quaternion.FromToRotation(
                            new Vector3(spread.x, spread.y, 1f).normalized, Vector3.forward);
                        Action thirdSpawn = () => spawned = root.Projectiles.TrySpawnVolley(source, point + outward * 8f,
                            direction, 3, root.Hero.Shotgun.Settings);
                        Action aimedFrame = () => root.TickFrame(CombatTestRoot.MaximumFrameSubsteps * CombatTestRoot.SimulationStep);
                        int priorContacts = impacts.Count;
                        samples.Add(MeasureDistantShotgunPhase(target, allocations, timer, "spawn", 0, thirdSpawn, 3));
                        Assert.That(spawned, Is.True);
                        for (int frame = 0; frame < 16 && root.Projectiles.ActiveCount > 0; frame++)
                        {
                            DistantShotgunSample wholeFrame = MeasureDistantShotgunPhase(target, allocations, timer,
                                "frame", frame, aimedFrame, 3);
                            samples.Add(wholeFrame);
                            Assert.That(wholeFrame.FreeAimQueries, Is.GreaterThanOrEqualTo(CombatTestRoot.MaximumFrameSubsteps),
                                "Every live substep must run the real free-aim anatomy query against the injured body.");
                            Assert.That(root.HitStopSecondsConsumed, Is.EqualTo(frozenSeconds));
                            Assert.That(root.Player.Motor.OwnedMovementFrozen, Is.False);
                            yield return null;
                        }
                        Assert.That(root.Projectiles.ActiveCount, Is.Zero);
                        Assert.That(impacts.Count - priorContacts, Is.InRange(3, root.Hero.Shotgun.Settings.PelletCount),
                            "The full-frame regression must include several real spread contacts, not a single distant pellet.");
                        Assert.That(target.ShotgunVolleyResponseCount, Is.EqualTo(3));
                        Assert.That(target.State.IsDefeated, Is.False,
                            "The aimed multi-pellet frame must exercise a surviving injured actor.");
                        float thirdTorsoLoss = 0f;
                        for (int region = (int)BodyDamageRegion.Chest; region <= (int)BodyDamageRegion.Pelvis; region++)
                            for (int patch = 0; patch < CombatBodyDamageState.PatchCount; patch++)
                                thirdTorsoLoss += target.BodyDamage.TissueLoss((BodyDamageRegion)region, patch);
                        Assert.That(thirdTorsoLoss, Is.GreaterThan(secondTorsoLoss));
                        for (int frame = 0; frame < 4; frame++)
                        {
                            DistantShotgunSample wholeFrame = MeasureDistantShotgunPhase(target, allocations, timer,
                                "frame", frame + 16, aimedFrame, 3);
                            samples.Add(wholeFrame);
                            Assert.That(root.Hero.IsFreePistolAiming && root.CameraFollow.FreeAimActive, Is.True);
                            Assert.That(wholeFrame.FreeAimQueries, Is.GreaterThanOrEqualTo(CombatTestRoot.MaximumFrameSubsteps),
                                "A held crosshair must keep querying the newly wounded posed body on subsequent frames.");
                            Assert.That(wholeFrame.GeometryBuilds, Is.GreaterThan(0),
                                "The held crosshair must intersect the wounded body, rather than only query empty space.");
                            Assert.That(root.HitStopSecondsConsumed, Is.EqualTo(frozenSeconds));
                            Assert.That(root.Player.Motor.OwnedMovementFrozen, Is.False);
                            yield return null;
                        }
                    }
                    int synchronizations = 0, contacts = 0;
                    foreach (DistantShotgunSample sample in samples)
                    {
                        synchronizations += sample.Synchronizations;
                        contacts += sample.Contacts;
                    }
                    Assert.That(contacts, Is.EqualTo(impacts.Count));
                    Assert.That(synchronizations, Is.InRange(1, impacts.Count),
                        "Only batches containing actual new wounds may synchronize the actor.");
                }
                finally
                {
                    target.ImpactReceived -= impacts.Add;
                    allSamples.AddRange(samples);
                    foreach (DistantShotgunSample sample in samples)
                        TestContext.Out.WriteLine($"Test distant shotgun impact - {subject}: shell={sample.Shell}, phase={sample.Phase}, " +
                            $"step={sample.Step}, cpu={sample.Milliseconds:F3} ms, allocations={sample.Allocations}, " +
                            $"blood={sample.BloodMilliseconds:F3} ms, body={sample.BodyMilliseconds:F3} ms, " +
                            $"synchronize={sample.SynchronizeMilliseconds:F3} ms, present={sample.PresentationMilliseconds:F3} ms, " +
                            $"freeAim={sample.FreeAimMilliseconds:F3} ms, aimQueries={sample.FreeAimQueries}, " +
                            $"geometry={sample.GeometryBuilds}, sync={sample.Synchronizations}, contacts={sample.Contacts}.{sample.SynchronizationStages}");
                }
            }
            // Collect both rigs before enforcing timing so a slower first actor
            // cannot hide which stage is responsible on the second one.
            foreach (DistantShotgunSample sample in allSamples)
            {
                if (sample.Phase == "frame")
                    Assert.That(sample.Milliseconds, Is.LessThan(40d),
                        "A live aimed frame must include spread contacts and repeated wounded-body queries without the reproduced hitch.");
                if (sample.Shell != 2) continue;
                if (sample.Phase == "contacts")
                    Assert.That(sample.Milliseconds, Is.LessThan(40d),
                        "Repeated distant impacts must avoid the reproduced recurring spike, with Editor timing headroom.");
                else if (sample.Phase == "capture" || sample.Phase == "advance")
                    Assert.That(sample.Milliseconds, Is.LessThan(25d),
                        "Frozen capture and flight sweeps must leave headroom for the impact and rendered frame.");
            }
            root.ResetRound();
            PlacePair(3f);
            float frozenBeforeTerminal = root.HitStopSecondsConsumed;
            FireShotgunAtRegion(MeleeBodyRegion.Torso, 1f, 3);
            Assert.That(root.Opponent.State.IsDefeated, Is.True);
            Assert.That(root.Player.Motor.OwnedMovementFrozen, Is.True,
                "The terminal shotgun contact must retain its brief impact hold.");
            root.Tick(.2f);
            Assert.That(root.HitStopSecondsConsumed, Is.GreaterThan(frozenBeforeTerminal),
                "Removing the surviving-pellet stop must preserve the terminal contact clock.");
            root.ResetRound();
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Range_NpcManualAnimationMatchesAuthoredClipSampling()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            yield return EnterRange(false, CombatWeaponId.Shotgun);
            CombatActor actor = root.Opponent;
            Animator animator = actor.DamageRigRoot.GetComponentInChildren<Animator>(true);
            Assert.That(animator, Is.Not.Null);
            Transform[] transforms = actor.DamageRigRoot.GetComponentsInChildren<Transform>(true);
            var positions = new Vector3[transforms.Length];
            var rotations = new Quaternion[transforms.Length];
            var scales = new Vector3[transforms.Length];
            var names = new List<string>();
            names.AddRange(CombatAssetProvider.ClipNames);
            names.AddRange(CombatAssetProvider.StepClipNames);
            names.AddRange(CombatAssetProvider.LocomotionClipNames);
            names.AddRange(CombatAssetProvider.RecoveryClipNames);
            foreach (string name in names)
            {
                if (CombatAssetProvider.IsKickClip(name)) continue;
                AnimationClip clip = CombatAssetProvider.LoadClip(name, true);
                foreach (float fraction in new[] { 0f, .19f, .53f, 1f, .07f })
                {
                    float time = clip.length * fraction;
                    clip.SampleAnimation(animator.gameObject, time);
                    for (int index = 0; index < transforms.Length; index++)
                    {
                        transforms[index].GetLocalPositionAndRotation(out positions[index], out rotations[index]);
                        scales[index] = transforms[index].localScale;
                    }
                    actor.SampleNpcClip(clip, time);
                    for (int index = 0; index < transforms.Length; index++)
                    {
                        Transform sampled = transforms[index];
                        Assert.That(Vector3.Distance(sampled.localPosition, positions[index]), Is.LessThan(.000001f),
                            name + " authored translation: " + sampled.name);
                        Assert.That(Quaternion.Angle(sampled.localRotation, rotations[index]), Is.LessThan(.04f),
                            name + " authored rotation: " + sampled.name);
                        Assert.That(Vector3.Distance(sampled.localScale, scales[index]), Is.LessThan(.000001f),
                            name + " authored scale: " + sampled.name);
                    }
                }
            }
            root.Opponent.enabled = false;
            root.Opponent.enabled = true;
            AnimationClip ready = CombatAssetProvider.LoadClip(CombatAssetProvider.ReadyClip, true);
            root.Opponent.SampleNpcClip(ready, .13f);
            root.ResetRound();
            LogAssert.NoUnexpectedReceived();
        }

        private DistantShotgunSample MeasureDistantShotgunPhase(CombatActor target, ProfilerRecorder allocations,
            Stopwatch timer, string phase, int step, Action action, int shell = 1)
        {
            int geometry = target.Hurtboxes.BodySurfaceGeometryBuilds;
            int synchronizations = root.BodyEffects.ActorSynchronizationCount;
            int contacts = target.ReceivedImpactCount;
            long bloodTicks = root.BloodImpactTicks;
            long woundTicks = root.BloodEffects.WoundProjectionTicks;
            long burstTicks = root.BloodEffects.BurstEmissionTicks;
            long woundPoseTicks = root.BloodEffects.WoundPoseTicks;
            long woundSkinTicks = root.BloodEffects.WoundSkinTicks;
            long woundTriangleTicks = root.BloodEffects.WoundTriangleTicks;
            long woundClassificationTicks = root.BloodEffects.WoundClassificationTicks;
            long woundCommitTicks = root.BloodEffects.WoundCommitTicks;
            long woundVisibilityTicks = root.BloodEffects.WoundVisibilityTicks;
            long headActivationTicks = root.HeadEffects.TissueActivationTicks;
            long headReleaseTicks = root.HeadEffects.TissueReleaseTicks;
            long headVisibilityTicks = root.HeadEffects.TissueVisibilityTicks;
            long headShapesTicks = root.HeadEffects.TissueShapesTicks;
            long headActivationVisibilityTicks = root.HeadEffects.ActivationVisibilityTicks;
            long headCollisionTicks = root.HeadEffects.ActivationCollisionTicks;
            long bodyTicks = root.BodyImpactTicks;
            long synchronizeTicks = root.BodyEffects.ActorSynchronizationTicks;
            long presentationTicks = root.ImpactPresentationTicks;
            long headSyncTicks = root.BodyEffects.HeadSynchronizationTicks;
            long surfaceRefreshTicks = root.BodyEffects.SurfaceRefreshTicks;
            long surfaceVisibilityTicks = root.BodyEffects.SurfaceVisibilityTicks;
            long physicsSyncTicks = root.BodyEffects.PhysicsSynchronizationTicks;
            long contactCaptureTicks = root.BodyEffects.ContactCaptureTicks;
            int freeAimQueries = root.FreeAimQueryCount;
            long freeAimTicks = root.FreeAimQueryTicks;
            long poseTicks = root.SimulationPoseTicks;
            long captureTicks = root.SimulationCaptureTicks;
            long flightTicks = root.SimulationFlightTicks;
            long effectsTicks = root.SimulationEffectsTicks;
            long npcSampleTicks = root.Opponent.NpcAnimationSampleTicks;
            long npcCompositionTicks = root.Opponent.NpcPoseCompositionTicks;
            long npcSnapshotTicks = root.Opponent.NpcPoseSnapshotTicks;
            allocations.Reset(); allocations.Start(); timer.Restart();
            action();
            timer.Stop(); long allocationCount = StopBodyAllocationRecorder(allocations);
            double tickMilliseconds = 1000d / Stopwatch.Frequency;
            string stages = root.BodyEffects.ActorSynchronizationCount == synchronizations ? string.Empty :
                $" Sync stages: head={(root.BodyEffects.HeadSynchronizationTicks - headSyncTicks) * tickMilliseconds:F3} ms, " +
                $"refresh={(root.BodyEffects.SurfaceRefreshTicks - surfaceRefreshTicks) * tickMilliseconds:F3} ms, " +
                $"visibility={(root.BodyEffects.SurfaceVisibilityTicks - surfaceVisibilityTicks) * tickMilliseconds:F3} ms, " +
                $"physics={(root.BodyEffects.PhysicsSynchronizationTicks - physicsSyncTicks) * tickMilliseconds:F3} ms, " +
                $"capture={(root.BodyEffects.ContactCaptureTicks - contactCaptureTicks) * tickMilliseconds:F3} ms.";
            if (root.BloodEffects.WoundProjectionTicks != woundTicks || root.BloodEffects.BurstEmissionTicks != burstTicks)
                stages += $" Blood stages: wounds={(root.BloodEffects.WoundProjectionTicks - woundTicks) * tickMilliseconds:F3} ms, " +
                    $"burst={(root.BloodEffects.BurstEmissionTicks - burstTicks) * tickMilliseconds:F3} ms, " +
                    $"pose={(root.BloodEffects.WoundPoseTicks - woundPoseTicks) * tickMilliseconds:F3} ms, " +
                    $"skin={(root.BloodEffects.WoundSkinTicks - woundSkinTicks) * tickMilliseconds:F3} ms, " +
                    $"triangles={(root.BloodEffects.WoundTriangleTicks - woundTriangleTicks) * tickMilliseconds:F3} ms, " +
                    $"classification={(root.BloodEffects.WoundClassificationTicks - woundClassificationTicks) * tickMilliseconds:F3} ms, " +
                    $"commit={(root.BloodEffects.WoundCommitTicks - woundCommitTicks) * tickMilliseconds:F3} ms, " +
                    $"visibility={(root.BloodEffects.WoundVisibilityTicks - woundVisibilityTicks) * tickMilliseconds:F3} ms.";
            if (root.HeadEffects.TissueShapesTicks != headShapesTicks)
                stages += $" Head stages: activate={(root.HeadEffects.TissueActivationTicks - headActivationTicks) * tickMilliseconds:F3} ms, " +
                    $"release={(root.HeadEffects.TissueReleaseTicks - headReleaseTicks) * tickMilliseconds:F3} ms, " +
                    $"visibility={(root.HeadEffects.TissueVisibilityTicks - headVisibilityTicks) * tickMilliseconds:F3} ms, " +
                    $"shapes={(root.HeadEffects.TissueShapesTicks - headShapesTicks) * tickMilliseconds:F3} ms, " +
                    $"activationVisibility={(root.HeadEffects.ActivationVisibilityTicks - headActivationVisibilityTicks) * tickMilliseconds:F3} ms, " +
                    $"collision={(root.HeadEffects.ActivationCollisionTicks - headCollisionTicks) * tickMilliseconds:F3} ms.";
            if (phase == "frame")
                stages += $" Frame stages: pose={(root.SimulationPoseTicks - poseTicks) * tickMilliseconds:F3} ms, " +
                    $"capture={(root.SimulationCaptureTicks - captureTicks) * tickMilliseconds:F3} ms, " +
                    $"flight={(root.SimulationFlightTicks - flightTicks) * tickMilliseconds:F3} ms, " +
                    $"effects={(root.SimulationEffectsTicks - effectsTicks) * tickMilliseconds:F3} ms." +
                    $" NPC pose: sample={(root.Opponent.NpcAnimationSampleTicks - npcSampleTicks) * tickMilliseconds:F3} ms, " +
                    $"composition={(root.Opponent.NpcPoseCompositionTicks - npcCompositionTicks) * tickMilliseconds:F3} ms, " +
                    $"snapshot={(root.Opponent.NpcPoseSnapshotTicks - npcSnapshotTicks) * tickMilliseconds:F3} ms.";
            return new DistantShotgunSample(phase, step, shell, timer.Elapsed.TotalMilliseconds, allocationCount,
                target.Hurtboxes.BodySurfaceGeometryBuilds - geometry,
                root.BodyEffects.ActorSynchronizationCount - synchronizations, target.ReceivedImpactCount - contacts,
                (root.BloodImpactTicks - bloodTicks) * tickMilliseconds,
                (root.BodyImpactTicks - bodyTicks) * tickMilliseconds,
                (root.BodyEffects.ActorSynchronizationTicks - synchronizeTicks) * tickMilliseconds,
                (root.ImpactPresentationTicks - presentationTicks) * tickMilliseconds,
                root.FreeAimQueryCount - freeAimQueries, (root.FreeAimQueryTicks - freeAimTicks) * tickMilliseconds, stages);
        }

        private List<CombatTorsoDamageSurface> DistantShotgunTorsoSurfaces(CombatActor target,
            out List<CombatBodyDestruction.Piece> pieces)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var actors = (IDictionary)typeof(CombatBodyDestruction).GetField("bodies", flags).GetValue(root.BodyEffects);
            object body = actors[target];
            pieces = (List<CombatBodyDestruction.Piece>)body.GetType().GetField("Pieces", flags).GetValue(body);
            var result = new List<CombatTorsoDamageSurface>();
            foreach (CombatBodyDestruction.Piece piece in pieces)
            {
                if (piece.Torso == null) continue;
                result.Add(piece.Torso);
            }
            return result;
        }

        private readonly struct DistantShotgunSample
        {
            internal readonly string Phase;
            internal readonly string SynchronizationStages;
            internal readonly int Step, Shell, GeometryBuilds, Synchronizations, Contacts, FreeAimQueries;
            internal readonly double Milliseconds;
            internal readonly double BloodMilliseconds, BodyMilliseconds, SynchronizeMilliseconds, PresentationMilliseconds;
            internal readonly double FreeAimMilliseconds;
            internal readonly long Allocations;
            internal DistantShotgunSample(string phase, int step, int shell, double milliseconds, long allocations,
                int geometryBuilds, int synchronizations, int contacts, double bloodMilliseconds,
                double bodyMilliseconds, double synchronizeMilliseconds, double presentationMilliseconds,
                int freeAimQueries, double freeAimMilliseconds, string synchronizationStages)
            {
                Phase = phase; Step = step; Shell = shell; Milliseconds = milliseconds; Allocations = allocations;
                GeometryBuilds = geometryBuilds; Synchronizations = synchronizations; Contacts = contacts;
                BloodMilliseconds = bloodMilliseconds; BodyMilliseconds = bodyMilliseconds;
                SynchronizeMilliseconds = synchronizeMilliseconds; PresentationMilliseconds = presentationMilliseconds;
                FreeAimQueries = freeAimQueries; FreeAimMilliseconds = freeAimMilliseconds;
                SynchronizationStages = synchronizationStages;
            }
        }
    }
}
