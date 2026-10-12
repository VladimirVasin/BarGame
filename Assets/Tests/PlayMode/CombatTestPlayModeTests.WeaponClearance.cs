using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatPerformancePlayModeTests
    {
        [UnityTest]
        public IEnumerator Range_WeaponSweepReusesOnlyIdenticalQueriesAndKeepsMovingAnatomy()
        {
            PlacePair(4f);
            root.Tick(TickSeconds);
            yield return null;
            CombatActor actor = root.Hero, other = root.Opponent;
            Transform upper = actor.DamageRigRoot.GetComponentsInChildren<Transform>(true)[0];
            Transform forearm = null;
            foreach (Transform bone in actor.DamageRigRoot.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name == "upper_arm.R") upper = bone;
                if (bone.name == "forearm.R") forearm = bone;
            }
            Assert.That(forearm, Is.Not.Null);
            using (var arm = new CombatArmClearance(actor, upper, forearm))
            {
                Quaternion saved = upper.localRotation;
                float[] angles = { -24f, 0f, 24f };
                var results = new bool[angles.Length];
                int snapshots = arm.CoreSnapshotCount;
                try
                {
                    using (arm.BeginWeaponSolve())
                        for (int i = 0; i < angles.Length; i++)
                        {
                            upper.localRotation = saved * Quaternion.AngleAxis(angles[i], Vector3.up);
                            results[i] = arm.IsClear();
                        }
                    Assert.That(arm.CoreSnapshotCount, Is.EqualTo(snapshots + 1),
                        "A synchronous shoulder search snapshots its fixed core once and reads the rotating arm live.");
                    for (int i = 0; i < angles.Length; i++)
                    {
                        upper.localRotation = saved * Quaternion.AngleAxis(angles[i], Vector3.up);
                        Assert.That(arm.IsClear(), Is.EqualTo(results[i]),
                            "Sharing the fixed core must preserve each independently checked shoulder candidate.");
                    }
                    Assert.That(arm.CoreSnapshotCount, Is.EqualTo(snapshots + 1 + angles.Length),
                        "No core snapshot survives the synchronous solve.");
                }
                finally { upper.localRotation = saved; }
            }

            using var constraint = new CombatWeaponConstraint(actor);
            constraint.SetCombatParticipants(new[] { actor, other });
            long capturedInputs = constraint.OtherAnatomyInputCaptures;
            constraint.Apply();
            Assert.That(constraint.OtherAnatomyInputCaptures - capturedInputs,
                Is.EqualTo(other.Ragdoll.PhysicsController.AnatomicalColliders.Count),
                "A synchronous Apply captures foreign primitive inputs once, regardless of candidate/sweep count.");
            constraint.CommitPresentedPose(null);
            Assert.That(constraint.OtherAnatomyDepthCulled, Is.GreaterThan(0),
                "Remote participant shapes must leave the per-segment depth candidate list.");
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            MethodInfo gather = typeof(CombatWeaponConstraint).GetMethod("GatherObstacles", flags);
            MethodInfo sweep = typeof(CombatWeaponConstraint).GetMethod("SweepClear", flags);
            CapsuleCollider nativeProbe = typeof(CombatWeaponConstraint).GetField("probe", flags)?.GetValue(constraint) as CapsuleCollider;
            Assert.That(gather != null && sweep != null && nativeProbe != null, Is.True);
            Vector3 origin = actor.transform.position + actor.transform.right * 1.2f + Vector3.up * 1.35f;
            Pose from = new Pose(origin - Vector3.forward * .2f, Quaternion.identity);
            Pose to = new Pose(origin + Vector3.forward * .2f, Quaternion.identity);
            bool Sweep(Pose a, Pose b) => (bool)sweep.Invoke(constraint, new object[] { a, b });
            void Gather() { Physics.SyncTransforms(); gather.Invoke(constraint, null); }
            CapsuleCollider moving = null, supporting = null, otherHead = null;
            foreach (var entry in other.Ragdoll.PhysicsController.AnatomicalColliders)
            {
                if (entry.Value == Player3DAnatomicalPart.RightForearm) moving = entry.Key as CapsuleCollider;
                if (entry.Value == Player3DAnatomicalPart.Head) otherHead = entry.Key as CapsuleCollider;
            }
            foreach (var entry in actor.Ragdoll.PhysicsController.AnatomicalColliders)
                if (entry.Value == Player3DAnatomicalPart.LeftForearm) supporting = entry.Key as CapsuleCollider;
            Assert.That(moving != null && supporting != null && otherHead != null, Is.True);

            // The mesh's broad bounds span the whole prop path, but its physical
            // aperture clears every surface. A fresh full swept-envelope query
            // must reject those broad bounds before the per-point native casts.
            var panel = new GameObject("Weapon sweep aperture fixture");
            panel.transform.position = origin;
            var mesh = new Mesh { name = "Weapon sweep aperture" };
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            float[] xs = { -.5f, -.07f, .07f, .5f }, ys = { -.4f, -.2f, .7f, .9f };
            for (int x = 0; x < 3; x++)
                for (int y = 0; y < 3; y++)
                {
                    if (x == 1 && y == 1) continue;
                    int first = vertices.Count;
                    vertices.Add(new Vector3(xs[x], ys[y], 0f));
                    vertices.Add(new Vector3(xs[x + 1], ys[y], 0f));
                    vertices.Add(new Vector3(xs[x + 1], ys[y + 1], 0f));
                    vertices.Add(new Vector3(xs[x], ys[y + 1], 0f));
                    triangles.AddRange(new[] { first, first + 2, first + 1, first, first + 3, first + 2 });
                }
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            MeshCollider aperture = panel.AddComponent<MeshCollider>();
            aperture.sharedMesh = mesh;
            try
            {
                constraint.Apply();
                Gather();
                long emptyQueries = constraint.WorldQueries, emptyCulls = constraint.WorldBoundsCulls;
                Assert.That(Sweep(from, to), Is.True, "The complete prop must pass through the physical aperture.");
                Assert.That(constraint.WorldQueries - emptyQueries, Is.EqualTo(1L),
                    "A complete native envelope miss must replace the aperture's many per-point casts with one query.");
                Assert.That(constraint.WorldBoundsCulls, Is.EqualTo(emptyCulls + 1),
                    "Physical mesh geometry, rather than its whole-panel AABB, determines the empty path.");

                // This corner lies inside the union box but beyond the actual
                // shaft. The envelope must retain the original miss casts,
                // including reuse of identical segment endpoints at one sample.
                BoxCollider corner = panel.AddComponent<BoxCollider>();
                corner.center = new Vector3(.034f, .30f, 0f);
                corner.size = new Vector3(.004f, .05f, .006f);
                constraint.Apply();
                Gather();
                long queries = constraint.WorldQueries, avoided = constraint.RepeatedWorldQueriesAvoided;
                long sampleCount = constraint.SweepSamples;
                long preparedBodies = constraint.OtherAnatomyGeometryBuilds;
                long reusedBodies = constraint.OtherAnatomyGeometryReuses;
                long bodyCandidates = constraint.OtherAnatomySweepCandidates;
                long culledBodies = constraint.OtherAnatomySweepCulled;
                Assert.That(Sweep(from, to), Is.True, "The complete prop must pass through the physical aperture.");
                Assert.That(constraint.OtherAnatomyGeometryBuilds, Is.EqualTo(preparedBodies),
                    "A stable participant reuses its exact primitive/pose preparation across shoulder candidates.");
                Assert.That(constraint.OtherAnatomyGeometryReuses, Is.GreaterThan(reusedBodies));
                Assert.That(constraint.OtherAnatomySweepCandidates, Is.EqualTo(bodyCandidates),
                    "Disjoint foreign anatomy must not enter interpolation or segment penetration loops.");
                Assert.That(constraint.OtherAnatomySweepCulled, Is.GreaterThan(culledBodies));
                long actual = constraint.WorldQueries - queries, reused = constraint.RepeatedWorldQueriesAvoided - avoided;
                long baselineSamples = constraint.SweepSamples - sampleCount;
                Assert.That(baselineSamples, Is.LessThan(48L), "The unchanged short path must leave room to detect the old remote-body 48-sample regression.");
                Assert.That(actual, Is.GreaterThan(0), "This aperture exercises the native world sweeps, not only an AABB fast path.");
                Assert.That(reused, Is.GreaterThan(0), "Shared segment ends and zero-length end envelopes must avoid identical native casts.");
                TestContext.Out.WriteLine($"Weapon aperture sweep: {actual} native casts, {reused} identical casts avoided.");

                long anatomyReuse = constraint.RepeatedAnatomySweepsAvoided;
                queries = constraint.WorldQueries;
                Assert.That(Sweep(from, to), Is.True);
                Assert.That(constraint.RepeatedAnatomySweepsAvoided, Is.EqualTo(anatomyReuse + 1),
                    "Exact anatomical inputs can share their clear sweep only until final commit.");
                Assert.That(constraint.WorldQueries - queries, Is.GreaterThan(0),
                    "Anatomical reuse cannot suppress the live aperture's native world casts.");

                var crowded = new List<BoxCollider>();
                try
                {
                    for (int i = 0; i < 96; i++)
                    {
                        BoxCollider shape = panel.AddComponent<BoxCollider>();
                        shape.center = corner.center; shape.size = corner.size;
                        crowded.Add(shape);
                    }
                    Gather();
                    queries = constraint.WorldQueries; emptyCulls = constraint.WorldBoundsCulls;
                    Assert.That(Sweep(from, to), Is.True);
                    Assert.That(constraint.WorldBoundsCulls, Is.EqualTo(emptyCulls),
                        "A full envelope result cannot prove that an omitted wall is absent.");
                    Assert.That(constraint.WorldQueries - queries, Is.GreaterThan(1L),
                        "Envelope saturation conservatively retains the original per-point casts.");
                }
                finally
                {
                    foreach (BoxCollider shape in crowded) Object.DestroyImmediate(shape);
                    Gather();
                }

                corner.center = Vector3.up * .30f;
                Physics.SyncTransforms();
                Assert.That(Sweep(from, to), Is.False,
                    "A wall moved into the previously clear path must stop it at the same clock, without another gather.");
                corner.center = new Vector3(.034f, .30f, 0f);
                Physics.SyncTransforms();
                Assert.That(Sweep(from, to), Is.True);

                // An unrelated moving body used to force all native world casts
                // to the 48-sample cap, even though its swept bounds miss the prop.
                Vector3 distantPosition = moving.transform.position;
                try
                {
                    moving.transform.position = origin + Vector3.right * 6f;
                    other.InvalidateCombatContacts(); Gather();
                    moving.transform.position += Vector3.right * 5f;
                    Gather();
                    queries = constraint.WorldQueries; sampleCount = constraint.SweepSamples;
                    Assert.That(Sweep(from, to), Is.True);
                    long prunedQueries = constraint.WorldQueries - queries;
                    Assert.That(constraint.SweepSamples - sampleCount, Is.EqualTo(baselineSamples),
                        "A disjoint body cannot increase interpolation density for potentially colliding pairs.");
                    Assert.That(prunedQueries, Is.LessThanOrEqualTo(actual),
                        "Disjoint movement must not multiply native casts along the unchanged aperture path.");
                    TestContext.Out.WriteLine($"Swept pair pruning: {baselineSamples} samples/{prunedQueries} native world casts; " +
                        "unpruned five-metre body travel would require the 48-sample cap.");
                }
                finally { moving.transform.position = distantPosition; other.InvalidateCombatContacts(); }

                constraint.Apply(); Gather();
                Assert.That(Sweep(from, to), Is.True);
                Vector3 supportingPosition = supporting.transform.position;
                float supportingRadius = supporting.radius;
                try
                {
                    anatomyReuse = constraint.RepeatedAnatomySweepsAvoided;
                    supporting.transform.position += Vector3.up * .002f;
                    Assert.That(Sweep(from, to), Is.True);
                    Assert.That(constraint.RepeatedAnatomySweepsAvoided, Is.EqualTo(anatomyReuse),
                        "Even sub-centimetre supporting-arm motion invalidates the exact anatomical result at the same clock.");
                    supporting.radius += .001f;
                    Assert.That(Sweep(from, to), Is.True);
                    Assert.That(constraint.RepeatedAnatomySweepsAvoided, Is.EqualTo(anatomyReuse),
                        "Primitive shape changes invalidate reuse independently of bone pose/clock.");
                }
                finally { supporting.transform.position = supportingPosition; supporting.radius = supportingRadius; }
                Assert.That(Sweep(from, to), Is.True);

                Object.DestroyImmediate(aperture);
                Object.DestroyImmediate(corner);
                BoxCollider wall = panel.AddComponent<BoxCollider>();
                wall.center = Vector3.up * .25f;
                wall.size = new Vector3(.18f, .9f, .006f);
                Gather();
                Assert.That(Sweep(from, to), Is.False,
                    "A new six-millimetre panel must stop the same path immediately; a previous clear sweep cannot be reused.");
                Assert.That(constraint.BlockingShape, Is.EqualTo(panel.name));
            }
            finally { Object.DestroyImmediate(panel); Object.DestroyImmediate(mesh); constraint.CommitPresentedPose(null); }

            Vector3 savedPosition = moving.transform.position;
            bool savedEnabled = moving.enabled, probeEnabled = nativeProbe.enabled;
            float savedRadius = moving.radius, savedHeight = moving.height;
            Vector3 savedScale = moving.transform.localScale;
            Assert.That(moving.enabled, Is.False, "Keep the real animated anatomical source disabled throughout its query proof.");
            var oracleObject = new GameObject("Moving anatomy sweep positive control");
            oracleObject.transform.position = Vector3.up * 10000f;
            SphereCollider oracle = oracleObject.AddComponent<SphereCollider>();
            oracle.radius = .019f; oracle.isTrigger = true;
            var targetOracleObject = new GameObject("Test moving anatomy geometry oracle");
            targetOracleObject.transform.position = Vector3.up * 10000f + Vector3.right;
            CapsuleCollider targetOracle = targetOracleObject.AddComponent<CapsuleCollider>();
            targetOracle.isTrigger = true;
            Vector3 contact = origin + Vector3.up * .25f;
            void MoveCentre(Vector3 point) => moving.transform.position += point - moving.transform.TransformPoint(moving.center);
            bool IntersectsShapeOracle(CapsuleCollider source)
            {
                // Independent enabled native copy with the source's original
                // parameters/scale; do not use the solver's baked query helper.
                targetOracle.center = source.center; targetOracle.radius = source.radius;
                targetOracle.height = source.height; targetOracle.direction = source.direction;
                targetOracle.transform.localScale = source.transform.lossyScale;
                Physics.SyncTransforms();
                return Physics.ComputePenetration(oracle, contact, Quaternion.identity,
                    targetOracle, source.transform.position, source.transform.rotation, out _, out _);
            }
            bool IntersectsOracle() => IntersectsShapeOracle(moving);
            try
            {
                nativeProbe.enabled = true;
                MoveCentre(contact);
                Assert.That(IntersectsOracle(), Is.True, "The independently enabled copy proves the disabled forearm crosses a real shaft point.");
                MoveCentre(contact - Vector3.right * .45f);
                Assert.That(IntersectsOracle(), Is.False);
                other.InvalidateCombatContacts(); Gather(); // A recycled participant starts a new physical history.
                constraint.Apply();
                long anatomyReuse = constraint.RepeatedAnatomySweepsAvoided;
                Assert.That(Sweep(from, to), Is.True);
                MoveCentre(contact + Vector3.right * .45f);
                Assert.That(IntersectsOracle(), Is.False, "Both endpoints clear: only relative swept motion can find the crossing.");
                Gather();
                Assert.That(Sweep(from, to), Is.False,
                    "The same previously clear path must reread the moved opposing arm without a clock advance.");
                Assert.That(Sweep(new Pose(origin, Quaternion.identity), new Pose(origin, Quaternion.identity)), Is.False,
                    "Moving opposing anatomy must stop a stationary weapon, including after an earlier clear world sweep.");
                Assert.That(constraint.RepeatedAnatomySweepsAvoided, Is.EqualTo(anatomyReuse),
                    "A moved opposing collider must not reuse the previously clear anatomical input.");

                bool ownerEnabled = other.enabled;
                try
                {
                    other.enabled = false; Gather();
                    Assert.That(Sweep(from, to), Is.True, "An inactive actor cannot contribute physical anatomy.");
                    anatomyReuse = constraint.RepeatedAnatomySweepsAvoided;
                    other.enabled = true;
                    Assert.That(Sweep(from, to), Is.False,
                        "Reactivation at the same clock must restore the moving body's crossing without another gather.");
                    Assert.That(constraint.RepeatedAnatomySweepsAvoided, Is.EqualTo(anatomyReuse),
                        "Owner enablement must invalidate a clear anatomical result even when the collider remains active.");
                    other.InvalidateCombatContacts(); Gather();
                    Assert.That(Sweep(from, to), Is.True,
                        "Recycling must retire the previous crossing, while the current clear endpoint stays physical.");
                }
                finally { other.enabled = ownerEnabled; }

                moving.height = moving.radius * 2f;
                MoveCentre(contact + Vector3.right * .10f);
                other.InvalidateCombatContacts(); Gather();
                Assert.That(IntersectsOracle(), Is.False);
                Assert.That(Sweep(from, to), Is.True);
                Vector3 scale = moving.transform.lossyScale;
                float perpendicularScale = Mathf.Max(Mathf.Abs(scale[(moving.direction + 1) % 3]),
                    Mathf.Abs(scale[(moving.direction + 2) % 3]));
                moving.radius = .12f / perpendicularScale;
                moving.height = moving.radius * 2f;
                Assert.That(IntersectsOracle(), Is.True);
                Assert.That(Sweep(from, to), Is.False,
                    "Native query geometry must refresh an enlarged disabled primitive at the same pose and clock.");
                moving.radius = savedRadius; moving.height = moving.radius * 2f;
                Assert.That(Sweep(from, to), Is.True);
                moving.transform.localScale = savedScale * 3f;
                MoveCentre(contact + Vector3.right * .10f);
                Assert.That(IntersectsOracle(), Is.True);
                Assert.That(Sweep(from, to), Is.False,
                    "A changed source scale must refresh the baked native dimensions at the same clock.");
                Assert.That(moving.enabled, Is.False);

                var physics = other.Ragdoll.PhysicsController;
                var priorBody = (CombatBodyDamageState)typeof(Player3DRagdollController)
                    .GetField("combatBodyDamage", flags).GetValue(physics);
                bool priorAuthored = (bool)typeof(Player3DRagdollController)
                    .GetField("authoredBodyCollision", flags).GetValue(physics);
                Vector3 savedHead = otherHead.transform.position;
                try
                {
                    moving.radius = savedRadius; moving.height = savedHeight; moving.transform.localScale = savedScale;
                    MoveCentre(contact);
                    other.InvalidateCombatContacts(); Gather();
                    var bodyState = new CombatBodyDamageState();
                    physics.SetCombatBodyDamage(bodyState, true);
                    Assert.That(physics.IsCombatAnatomicalQueryAllowed(moving), Is.True,
                        "Attached standing anatomy retains its posed query envelope even when authored body resources are bound.");
                    Assert.That(Sweep(from, to), Is.False);
                    Assert.That(bodyState.Apply(BodyDamageRegion.RightForearm, 0, 2f, 1, 1, 0), Is.True);
                    physics.SetCombatBodyDamage(bodyState, true);
                    Assert.That(Sweep(from, to), Is.True,
                        "Detaching a source region retires its native query at the same clock without another gather.");
                    bodyState.Reset();
                    physics.SetCombatBodyDamage(bodyState, true);
                    Assert.That(Sweep(from, to), Is.False,
                        "Reset restores attached standing input immediately, including invalidating an earlier clear sweep.");

                    MoveCentre(contact + Vector3.right * 2f);
                    otherHead.transform.position += contact - otherHead.transform.TransformPoint(otherHead.center);
                    other.InvalidateCombatContacts(); Gather();
                    Assert.That(IntersectsShapeOracle(otherHead), Is.True);
                    Assert.That(otherHead.enabled, Is.False);
                    Assert.That(Sweep(from, to), Is.False);
                    physics.SetCombatHeadCollisionEnabled(false);
                    Assert.That(Sweep(from, to), Is.True,
                        "Authored replacement of the head suppresses its old complete primitive at the same clock.");
                    anatomyReuse = constraint.RepeatedAnatomySweepsAvoided;
                    physics.SetCombatHeadCollisionEnabled(true);
                    Assert.That(Sweep(from, to), Is.False,
                        "Resetting head ownership restores its posed primitive without a gather or clock advance.");
                    Assert.That(constraint.RepeatedAnatomySweepsAvoided, Is.EqualTo(anatomyReuse));
                    Assert.That(otherHead.enabled, Is.False);
                }
                finally
                {
                    otherHead.transform.position = savedHead;
                    physics.SetCombatHeadCollisionEnabled(true);
                    physics.SetCombatBodyDamage(null, false);
                    if (priorBody != null) physics.SetCombatBodyDamage(priorBody, priorAuthored);
                }
            }
            finally
            {
                moving.radius = savedRadius; moving.height = savedHeight; moving.transform.localScale = savedScale;
                moving.transform.position = savedPosition;
                moving.enabled = savedEnabled; nativeProbe.enabled = probeEnabled;
                Object.DestroyImmediate(oracleObject);
                Object.DestroyImmediate(targetOracleObject);
                constraint.Restore();
            }
            var queryCache = (CombatPrimitiveQueryCache)typeof(CombatWeaponConstraint)
                .GetField("anatomyQueries", flags).GetValue(constraint);
            Assert.That(queryCache.Count, Is.GreaterThan(0));
            constraint.SetCombatParticipants(new[] { actor, other });
            Assert.That(queryCache.Count, Is.EqualTo(0), "Rebinding participants releases all source-bound query copies.");
            VerifyFinalWeaponCommitRollback(actor, other, upper, supporting);
            LogAssert.NoUnexpectedReceived();
        }

        private static void VerifyFinalWeaponCommitRollback(CombatActor actor, CombatActor other, Transform upper, CapsuleCollider supporting)
        {
            Transform supportingBone = supporting.transform.parent;
            Assert.That(supportingBone.name, Is.EqualTo("forearm.L"),
                "The physical forearm proxy follows the actual supporting animation bone.");
            using var constraint = new CombatWeaponConstraint(actor);
            constraint.SetOpponent(other);
            constraint.Apply(); constraint.CommitPresentedPose(null);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var probe = (CapsuleCollider)typeof(CombatWeaponConstraint).GetField("probe", flags).GetValue(constraint);
            bool enabled = probe.enabled;
            foreach (bool preview in new[] { false, true })
            foreach (bool leftCollision in new[] { false, true })
            {
                Vector3 original = upper.localPosition;
                Vector3 originalSupport = supportingBone.localPosition;
                Vector3 safeSupport = supporting.transform.position;
                bool supportEnabled = supporting.enabled;
                Pose safeWeapon = new Pose(actor.Weapon.transform.position, actor.Weapon.transform.rotation);
                var obstacle = new GameObject("Test weapon commit obstacle");
                BoxCollider wall = obstacle.AddComponent<BoxCollider>();
                wall.size = Vector3.one * .08f;
                wall.enabled = false;
                CombatWeaponConstraint.PreviewScope previewScope = default;
                try
                {
                    if (preview) previewScope = constraint.BeginContactPreview();
                    probe.enabled = true;
                    constraint.Apply();
                    Assert.That(constraint.MotionBlocked, Is.False, "Begin from an admitted safe weapon pose.");
                    if (!leftCollision) upper.position += actor.transform.right * .6f;
                    Vector3 shaft = actor.Weapon.transform.position + actor.Weapon.transform.up * .25f;
                    Collider blocking;
                    if (leftCollision)
                    {
                        // Move the animation bone, as final support presentation
                        // does. Moving its collider child would alter authored
                        // shape geometry, which rig rollback must not overwrite.
                        supportingBone.position += shaft - supporting.transform.TransformPoint(supporting.center);
                        supporting.enabled = true;
                        blocking = supporting;
                    }
                    else
                    {
                        wall.transform.position = shaft;
                        wall.enabled = true;
                        blocking = wall;
                    }
                    Physics.SyncTransforms();
                    Assert.That(Physics.ComputePenetration(probe, shaft, Quaternion.identity, blocking,
                        blocking.transform.position, blocking.transform.rotation, out _, out _), Is.True,
                        "The arriving wall or final supporting arm must physically intersect the shaft before commit.");
                    constraint.CommitPresentedPose(null);
                    Assert.That(constraint.MotionBlocked, Is.True, "The final live-world gate must reject this admitted-then-obstructed pose.");
                    Assert.That(constraint.BlockingShape, Is.EqualTo(blocking.name), "Rollback retains the physical rejection cause.");
                    Assert.That(Vector3.Distance(actor.Weapon.transform.position, safeWeapon.position), Is.LessThan(.001f),
                        "Both contact preview and visible commit restore the last safe full rig.");
                    Assert.That(Quaternion.Angle(actor.Weapon.transform.rotation, safeWeapon.rotation), Is.LessThan(.1f));
                    Assert.That(Vector3.Distance(supporting.transform.position, safeSupport), Is.LessThan(.001f),
                        $"A physically obstructing final supporting arm returns with the last safe rig too: preview={preview}, leftCollision={leftCollision}.");
                    Assert.That(constraint.PenetrationDepth, Is.LessThanOrEqualTo(.0001f), "The rejected final shaft cannot remain displayed inside the wall.");
                }
                finally
                {
                    if (preview) previewScope.Dispose();
                    constraint.Restore(); upper.localPosition = original;
                    supportingBone.localPosition = originalSupport; supporting.enabled = supportEnabled;
                    probe.enabled = enabled;
                    Object.DestroyImmediate(obstacle);
                    constraint.Reset(); constraint.Apply(); constraint.CommitPresentedPose(null);
                }
            }
        }
    }

    public sealed partial class CombatTestPlayModeTests
    {
        private WeaponMeshClearanceProbe weaponMeshClearance;

        [UnityTest]
        public IEnumerator Range_DroppedWeaponCullsDistantBodiesAndKeepsMovingLimbContacts()
        {
            yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(SceneIds.MainMenu);
            yield return EnterRange();
            yield return VerifyDroppedWeaponClearance(root.Opponent, "Test dropped crowbar");
            root.ResetRound();
            LogAssert.NoUnexpectedReceived();
        }

        private IEnumerator VerifyMixedWeaponSupport(CombatActor actor, string subject)
        {
            foreach (var sample in new[]
            {
                (Swing: MeleeSwing.Forehand, Power: .25f),
                (Swing: MeleeSwing.Forehand, Power: 1f),
                (Swing: MeleeSwing.Backhand, Power: .25f),
                (Swing: MeleeSwing.Backhand, Power: .5f),
                (Swing: MeleeSwing.Backhand, Power: 1f)
            })
            {
                MeleeSwing swing = sample.Swing;
                PlacePair(4f);
                yield return null;
                yield return null;
                PresentInertiaPose();
                actor.State.ObserveLateralCue(swing == MeleeSwing.Backhand ? 1 : -1);
                Assert.That(actor.RequestCharge(), Is.True);
                float power = sample.Power;
                string context = subject + "/" + swing + " power=" + power + " mixed grip";
                string captureSubject = subject + "-" + swing + "-power" + Mathf.RoundToInt(power * 100f);
                for (int frame = 0; frame < 120 && actor.State.Charge01 < power; frame++)
                {
                    root.Tick(ImpactFrameSeconds);
                    yield return null;
                    PresentInertiaPose();
                    AssertMixedSupportPose(actor, context + " charge=" + actor.State.Charge01);
                }
                Assert.That(actor.State.Charge01, Is.EqualTo(power).Within(.035f));
                Transform[] releaseJoints = { FindAnatomicalBone(actor, "forearm.R"), FindAnatomicalBone(actor, "hand.R"),
                    FindAnatomicalBone(actor, "forearm.L"), FindAnatomicalBone(actor, "hand.L"), actor.Weapon.transform };
                var beforeRelease = new Pose[releaseJoints.Length];
                for (int i = 0; i < releaseJoints.Length; i++)
                    beforeRelease[i] = new Pose(releaseJoints[i].position, releaseJoints[i].rotation);
                Assert.That(actor.ReleaseCharge(), Is.True, context + ": the actual support must permit release.");
                PresentInertiaPose();
                for (int i = 0; i < releaseJoints.Length; i++)
                {
                    Assert.That(Vector3.Distance(beforeRelease[i].position, releaseJoints[i].position), Is.LessThan(.005f),
                        context + ": charge/release must share the same visible pose: " + releaseJoints[i].name);
                    Assert.That(Quaternion.Angle(beforeRelease[i].rotation, releaseJoints[i].rotation), Is.LessThan(1f),
                        context + ": charge/release must retain the joint frame: " + releaseJoints[i].name);
                }
                bool capturedContact = false, capturedReturn = false;
                for (int frame = 0; frame < 150 && actor.State.IsAttacking; frame++)
                {
                    root.Tick(ImpactFrameSeconds);
                    yield return null;
                    PresentInertiaPose();
                    AssertMixedSupportPose(actor, context + " release=" + actor.State.AttackProgress);
                    float seconds = actor.State.AttackProgress * 1.28f;
                    if (!capturedContact && seconds >= .60f)
                    {
                        CaptureImpactRecoveryFrame(actor, captureSubject, "weapon-contact",
                            actor.transform.position, actor.transform.rotation, true);
                        capturedContact = true;
                    }
                    if (!capturedReturn && seconds >= .89f)
                    {
                        CaptureImpactRecoveryFrame(actor, captureSubject, "weapon-return",
                            actor.transform.position, actor.transform.rotation, true);
                        capturedReturn = true;
                    }
                }
                Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Ready), context);
                Assert.That(capturedContact && capturedReturn, Is.True, context + ": both changed arcs must be inspected.");
            }
        }

        private void AssertMixedSupportPose(CombatActor actor, string context)
        {
            AssertWeaponClearance(actor, context);
            Assert.That(actor.SupportArmState, Is.EqualTo(CombatArmSupportState.SupportingWeapon), context);
            NpcHandPose hands = actor.GetComponentInChildren<NpcHandPose>();
            Assert.That(Vector3.Distance(hands.CylinderCentre(true), actor.SupportGripWorldPosition), Is.LessThan(.025f), context);
            foreach (bool left in new[] { false, true })
            {
                Transform hand = FindAnatomicalBone(actor, left ? "hand.L" : "hand.R");
                Transform forearm = FindAnatomicalBone(actor, left ? "forearm.L" : "forearm.R");
                Vector3 palm = hands.PalmNormal(left);
                Vector3 fingers = Vector3.ProjectOnPlane(hands.CylinderCentre(left) - hand.position, palm).normalized;
                Vector3 lower = (hand.position - forearm.position).normalized;
                float radial = Mathf.Abs(Mathf.Asin(Mathf.Clamp(Vector3.Dot(lower, Vector3.Cross(palm, fingers).normalized), -1f, 1f)) * Mathf.Rad2Deg);
                float flexion = Mathf.Abs(Mathf.Atan2(Vector3.Dot(lower, palm), Vector3.Dot(lower, fingers)) * Mathf.Rad2Deg);
                Assert.That(radial, Is.LessThanOrEqualTo(25.1f), context + (left ? " L" : " R") + " wrist sideways bend");
                Assert.That(flexion, Is.LessThanOrEqualTo(55.1f), context + (left ? " L" : " R") + " wrist flexion");
            }
        }

        // Invoked by the impact/recovery scenario, so the same scene and both
        // authored rigs cover held, interrupted, recovering and dropped props.
        private IEnumerator VerifyWeaponClearanceStates(CombatActor actor, string subject)
        {
            foreach (string pose in new[] { "Ready", "Block", "Attack" })
            {
                PlacePair(4f);
                yield return null;
                yield return null;
                PresentInertiaPose();
                if (pose == "Block") actor.SetBlock(true);
                if (pose == "Attack") Assert.That(actor.TryAttack(), Is.True, subject + ": start the unobstructed swing.");
                for (int frame = 0; frame < 8; frame++)
                {
                    root.Tick(ImpactFrameSeconds);
                    yield return null;
                    PresentInertiaPose();
                }
                if (pose == "Attack")
                {
                    for (int frame = 0; frame < 45 && actor.State.Phase == MeleePhase.Windup; frame++)
                    {
                        root.Tick(ImpactFrameSeconds);
                        yield return null;
                        PresentInertiaPose();
                    }
                    Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Active),
                        subject + ": place the panel in the live arc, after an unobstructed windup.");
                }
                AssertWeaponClearance(actor, subject + "/" + pose + " before wall");

                Transform weapon = actor.Weapon.transform;
                Vector3 gripPosition = weapon.localPosition;
                Quaternion gripRotation = weapon.localRotation;
                Vector3 hook = ClearanceProbe.HookSurface(actor);
                Vector3 outward = Vector3.ProjectOnPlane(hook - actor.transform.position, Vector3.up).normalized;
                if (outward.sqrMagnitude < .5f) outward = actor.transform.forward;
                var panel = new GameObject("Weapon hook overlap fixture");
                panel.transform.SetParent(root.transform, false);
                panel.transform.SetPositionAndRotation(hook, Quaternion.LookRotation(outward));
                BoxCollider wall = panel.AddComponent<BoxCollider>();
                wall.size = new Vector3(.20f, .25f, .035f);
                Physics.SyncTransforms();
                try
                {
                    // Spawn around the already presented hook: a sweep-only
                    // implementation has no previous outside point to rescue it.
                    Assert.That(ClearanceProbe.DepthAgainst(actor, wall), Is.GreaterThan(.012f),
                        subject + "/" + pose + ": the real hook surface must start inside this thin panel.");
                    bool sawRecoil = false;
                    for (int frame = 0; frame < 18; frame++)
                    {
                        root.Tick(ImpactFrameSeconds);
                        yield return null;
                        PresentInertiaPose();
                        sawRecoil |= actor.ActiveClipName != null && actor.ActiveClipName.Contains("Recoil");
                        AssertWeaponClearance(actor, subject + "/" + pose + " wall frame=" + frame);
                        Assert.That(Vector3.Distance(weapon.localPosition, gripPosition), Is.LessThan(.001f),
                            "Clearance must move the arm without sliding the crowbar out of its right-hand grip.");
                        Assert.That(Quaternion.Angle(weapon.localRotation, gripRotation), Is.LessThan(.1f),
                            "Clearance must not rotate the prop independently inside its palm.");
                    }
                    if (pose == "Attack")
                    {
                        Assert.That(actor.State.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Obstacle),
                            subject + ": correcting the rendered crowbar must not erase its wall contact.");
                        Assert.That(sawRecoil, Is.True,
                            subject + ": the live arc must show its wall recoil after the full prop is stopped.");
                    }
                }
                finally { wall.enabled = false; Object.Destroy(panel); }
            }

            yield return VerifyDroppedWeaponClearance(actor, subject);
            PlacePair(4f);
            yield return null;
            PresentInertiaPose();
            AssertWeaponClearance(actor, subject + "/reset after weapon clearance");
        }

        private IEnumerator VerifyDroppedWeaponClearance(CombatActor actor, string subject)
        {
            Assert.That(actor.WeaponId, Is.EqualTo(CombatWeaponId.Crowbar),
                "The dropped shaft regression uses the actual imported crowbar.");
            PlacePair(4f);
            yield return null;
            PresentInertiaPose();
            MethodInfo drop = typeof(CombatActor).GetMethod("DropWeapon", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(drop, Is.Not.Null, "Exercise the actual prop physics handoff without defeating the fixture's actor.");
            drop.Invoke(actor, null);
            Assert.That(actor.IsWeaponDropped, Is.True);
            Rigidbody body = actor.Weapon.GetComponent<Rigidbody>();
            Assert.That(body, Is.Not.Null);
            Assert.That(body.isKinematic, Is.False);
            Assert.That(body.detectCollisions, Is.True);
            for (int frame = 0; frame < 150; frame++)
            {
                root.Tick(ImpactFrameSeconds);
                yield return null;
                AssertWeaponClearance(actor, subject + "/dropped frame=" + frame);
            }
            Assert.That(ClearanceProbe.LowestWorldPoint(actor).y, Is.LessThan(.12f),
                subject + ": the dropped rendered hook and shaft must actually reach the arena floor.");
            Assert.That(body.linearVelocity.magnitude, Is.LessThan(.5f),
                subject + ": the floor contact must settle instead of leaving the prop falling through it.");

            // A standing arm outside the movement capsule has disabled physical
            // anatomy. The freely dropped prop must still stop at its live skin.
            PlacePair(4f);
            CombatActor target = actor == root.Hero ? root.Opponent : root.Hero;
            target.SetBlock(true);
            for (int frame = 0; frame < 24; frame++)
            {
                root.Tick(ImpactFrameSeconds);
                yield return null;
                PresentInertiaPose();
            }
            CapsuleCollider arm = null;
            Vector3 contact = Vector3.zero, outward = Vector3.zero, armPointLocal = Vector3.zero;
            float furthest = float.NegativeInfinity;
            foreach (var entry in target.Ragdoll.PhysicsController.AnatomicalColliders)
            {
                if ((entry.Value != Player3DAnatomicalPart.LeftForearm && entry.Value != Player3DAnatomicalPart.RightForearm) ||
                    !(entry.Key is CapsuleCollider capsule)) continue;
                Vector3 axis = capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward;
                Vector3 signedScale = capsule.transform.lossyScale;
                Vector3 scale = new Vector3(Mathf.Abs(signedScale.x), Mathf.Abs(signedScale.y), Mathf.Abs(signedScale.z));
                float capsuleRadius = capsule.radius * Mathf.Max(scale[(capsule.direction + 1) % 3],
                    scale[(capsule.direction + 2) % 3]);
                float half = Mathf.Max(0f, capsule.height * scale[capsule.direction] * .5f - capsuleRadius);
                foreach (float end in new[] { -1f, 1f })
                {
                    Vector3 point = capsule.transform.TransformPoint(capsule.center) +
                        capsule.transform.rotation * axis * (end * half);
                    Vector3 radial = Vector3.ProjectOnPlane(point - target.transform.position, Vector3.up);
                    if (radial.magnitude <= furthest) continue;
                    furthest = radial.magnitude; arm = capsule; contact = point; outward = radial.normalized;
                    armPointLocal = capsule.transform.InverseTransformPoint(point);
                }
            }
            Assert.That(arm, Is.Not.Null);
            Assert.That(arm.enabled, Is.False, "This regression requires query-only standing anatomy.");
            drop.Invoke(actor, null);
            body = actor.Weapon.GetComponent<Rigidbody>();
            CombatHeldWeaponPhysics droppedPhysics = actor.GetComponent<CombatHeldWeaponPhysics>();
            Assert.That(droppedPhysics, Is.Not.Null);
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.None;
            body.rotation = Quaternion.LookRotation(outward, Vector3.up);
            body.angularVelocity = Vector3.zero;
            actor.Weapon.transform.SetPositionAndRotation(body.position, body.rotation);
            // Align an actual imported shaft surface, rather than assuming its
            // axial sample or the scaled capsule end centre matches this rig.
            Vector3 shaftOffset = ClearanceProbe.ShaftSurfaceOffset(actor, -outward, out Vector3 shaftSampleLocal);
            Vector3 armScale = arm.transform.lossyScale;
            float radius = arm.radius * Mathf.Max(Mathf.Abs(armScale[(arm.direction + 1) % 3]),
                Mathf.Abs(armScale[(arm.direction + 2) % 3]));
            body.position = contact + outward * (radius - .01f) - shaftOffset;
            actor.Weapon.transform.SetPositionAndRotation(body.position, body.rotation);
            droppedPhysics.EnableDropped();
            Physics.SyncTransforms();
            float meshDepth = ClearanceProbe.DepthAgainst(actor, arm);
            Collider queryArm = ClearanceProbe.QueryShape(arm);
            Assert.That(arm.enabled, Is.False, "Query geometry must leave the real animated forearm disabled.");
            Assert.That(queryArm, Is.Not.SameAs(arm));
            Assert.That(queryArm.enabled, Is.True);
            float nativeDepth = 0f;
            foreach (CapsuleCollider capsule in actor.Weapon.GetComponentsInChildren<CapsuleCollider>())
            {
                if (!capsule.enabled || !capsule.name.StartsWith("Dropped Crowbar Collision")) continue;
                if (Physics.ComputePenetration(capsule, capsule.transform.position, capsule.transform.rotation,
                    queryArm, arm.transform.position, arm.transform.rotation, out _, out float depth))
                    nativeDepth = Mathf.Max(nativeDepth, depth);
            }
            Vector3 actualSample = actor.Weapon.transform.position + actor.Weapon.transform.rotation * shaftSampleLocal;
            Vector3 intendedSample = contact + outward * (radius - .01f);
            Assert.That(Vector3.Distance(actualSample, intendedSample), Is.LessThan(.0001f),
                "The controlled contact must stay on the actual imported shaft after native shape preparation.");
            TestContext.WriteLine("Dropped shaft positive control: rendered depth=" + meshDepth.ToString("F4") +
                ", native capsule depth=" + nativeDepth.ToString("F4") + ", real arm enabled=" + arm.enabled);
            Assert.That(nativeDepth, Is.GreaterThan(.005f),
                "An actual dropped capsule must intersect the independently copied real disabled forearm geometry.");
            Assert.That(meshDepth, Is.GreaterThan(.005f),
                "The intended path must cross the rendered shaft and this real disabled forearm.");
            Assert.That(ClearanceProbe.DepthAgainst(actor, target.Body), Is.LessThan(.001f),
                "The same contact pose must be outside the central CharacterController: it cannot prove this contract.");
            Assert.That(ClearanceProbe.RadialClearanceFrom(actor, target.Body), Is.GreaterThan(.001f),
                "Every mesh sample must stay outside even the controller's infinite central cylinder.");
            body.position = contact + outward * (radius + .14f) - shaftOffset;
            actor.Weapon.transform.SetPositionAndRotation(body.position, body.rotation);
            Physics.SyncTransforms();
            droppedPhysics.EnableDropped(); // Begin at the controlled, already detached launch pose.
            body.linearVelocity = -outward * 2f;
            int contactsBefore = droppedPhysics.DroppedAnatomyContactCount;
            for (int frame = 0; frame < 14; frame++)
            {
                root.Tick(ImpactFrameSeconds);
                yield return new WaitForFixedUpdate();
                yield return null;
                PresentInertiaPose();
                // Like the authored pose above, explicitly present the production
                // pass because this manually stepped coroutine resumes before LateUpdate.
                droppedPhysics.ResolveDroppedAnatomy();
                AssertWeaponClearance(actor, subject + "/dropped into animated arm frame=" + frame);
            }
            Assert.That(droppedPhysics.DroppedAnatomyContactCount, Is.GreaterThan(contactsBefore),
                "The animated limb, rather than the central capsule, must receive the dropped-prop contact.");
            Assert.That(arm.enabled, Is.False, "Animated contact must not activate the real physical forearm.");

            // Moving anatomy must also be swept when the weapon itself is still.
            body.linearVelocity = body.angularVelocity = Vector3.zero;
            contact = arm.transform.TransformPoint(armPointLocal);
            outward = Vector3.ProjectOnPlane(contact - target.transform.position, Vector3.up).normalized;
            body.rotation = Quaternion.LookRotation(outward, Vector3.up);
            actor.Weapon.transform.SetPositionAndRotation(body.position, body.rotation);
            shaftOffset = ClearanceProbe.ShaftSurfaceOffset(actor, -outward);
            body.position = contact + outward * (radius + .02f) - shaftOffset;
            actor.Weapon.transform.SetPositionAndRotation(body.position, body.rotation);
            Physics.SyncTransforms();
            droppedPhysics.EnableDropped();
            contactsBefore = droppedPhysics.DroppedAnatomyContactCount;
            Assert.That(ClearanceProbe.DepthAgainst(actor, arm), Is.LessThan(.001f),
                "The stationary prop must begin clear of the moving forearm.");
            Rigidbody forearmBody = arm.GetComponentInParent<Rigidbody>();
            Assert.That(forearmBody, Is.Not.Null, "The disabled anatomical proxy must retain its real rig body's hierarchy.");
            Transform forearmBone = forearmBody.transform;
            Vector3 savedForearmLocal = forearmBone.localPosition;
            Vector3 savedRootPosition = target.transform.position, stationaryWeaponPosition = body.position;
            Vector3 armPositionBefore = arm.transform.position;
            forearmBone.position += outward * .08f;
            Physics.SyncTransforms();
            Assert.That(Vector3.Distance(arm.transform.position - armPositionBefore, outward * .08f), Is.LessThan(.0001f),
                "The real animated forearm, rather than the movement capsule, must cross the shaft.");
            Assert.That(target.transform.position, Is.EqualTo(savedRootPosition));
            Assert.That(body.position, Is.EqualTo(stationaryWeaponPosition));
            Assert.That(ClearanceProbe.DepthAgainst(actor, arm), Is.GreaterThan(.005f),
                "The moved rendered forearm must independently intersect the unchanged imported prop before solving.");
            queryArm = ClearanceProbe.QueryShape(arm);
            nativeDepth = 0f;
            foreach (CapsuleCollider capsule in actor.Weapon.GetComponentsInChildren<CapsuleCollider>())
                if (capsule.enabled && capsule.name.StartsWith("Dropped Crowbar Collision") &&
                    Physics.ComputePenetration(capsule, capsule.transform.position, capsule.transform.rotation,
                        queryArm, arm.transform.position, arm.transform.rotation, out _, out float depth))
                    nativeDepth = Mathf.Max(nativeDepth, depth);
            Assert.That(nativeDepth, Is.GreaterThan(.005f),
                "The same moving-limb crossing must intersect an actual native dropped shaft capsule.");
            droppedPhysics.ResolveDroppedAnatomy();
            AssertWeaponClearance(actor, subject + "/animated arm moves into stationary dropped prop");
            Assert.That(droppedPhysics.DroppedAnatomyContactCount, Is.GreaterThan(contactsBefore));
            Assert.That(arm.enabled, Is.False, "Relative-motion contact must keep standing anatomy query-only.");

            // A cached original envelope cannot survive detachment at the same
            // clock, and restoring the region must immediately restore contact.
            Player3DRagdollController targetPhysics = target.Ragdoll.PhysicsController;
            targetPhysics.SetCombatBodyDamage(target.BodyDamage, true);
            BodyDamageRegion removedArm = target.Ragdoll.PhysicsController.AnatomicalColliders[arm] == Player3DAnatomicalPart.LeftForearm
                ? BodyDamageRegion.LeftUpperArm : BodyDamageRegion.RightUpperArm;
            contact = arm.transform.TransformPoint(armPointLocal);
            shaftOffset = ClearanceProbe.ShaftSurfaceOffset(actor, -outward);
            body.position = contact + outward * (radius - .01f) - shaftOffset;
            actor.Weapon.transform.SetPositionAndRotation(body.position, body.rotation);
            body.linearVelocity = body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            droppedPhysics.EnableDropped();
            contactsBefore = droppedPhysics.DroppedAnatomyContactCount;
            try
            {
                Assert.That(targetPhysics.IsCombatAnatomicalQueryAllowed(arm), Is.True);
                Assert.That(target.BodyDamage.Apply(removedArm, 0, CombatBodyDamageState.SeparationTrauma, 99, 1, 0), Is.True);
                Assert.That(targetPhysics.IsCombatAnatomicalQueryAllowed(arm), Is.False);
                Assert.That(ClearanceProbe.DepthAgainst(actor, arm), Is.GreaterThan(.005f),
                    "The old primitive remains geometrically overlapping; only its actual regional eligibility changes.");
                Assert.That(ClearanceProbe.MaximumDepth(actor, out _), Is.LessThan(.001f),
                    "No other eligible anatomy may explain this controlled detached-region contact.");
                droppedPhysics.ResolveDroppedAnatomy();
                Assert.That(droppedPhysics.DroppedAnatomyContactCount, Is.EqualTo(contactsBefore),
                    "A same-clock detached region must not contribute its cached intact envelope.");
                target.BodyDamage.Reset();
                Assert.That(targetPhysics.IsCombatAnatomicalQueryAllowed(arm), Is.True);
                droppedPhysics.ResolveDroppedAnatomy();
                Assert.That(droppedPhysics.DroppedAnatomyContactCount, Is.GreaterThan(contactsBefore),
                    "A same-clock region reset must restore the still-overlapping live envelope.");
                Assert.That(arm.enabled, Is.False);
            }
            finally
            {
                target.BodyDamage.Reset();
                forearmBone.localPosition = savedForearmLocal;
                Physics.SyncTransforms();
            }

            // Motion far from the entire prop cannot create a correction. Reject
            // that complete sweep before subdividing unrelated limb travel.
            Vector3 savedWeaponPosition = body.position, savedTargetPosition = target.transform.position;
            try
            {
                body.position = savedWeaponPosition + Vector3.up * 100f;
                actor.Weapon.transform.SetPositionAndRotation(body.position, body.rotation);
                Physics.SyncTransforms();
                droppedPhysics.EnableDropped();
                int skipped = droppedPhysics.DroppedAnatomySweepsSkipped;
                long sampled = droppedPhysics.DroppedAnatomyPoseSamples;
                target.Body.Move(Vector3.right * 2f);
                Physics.SyncTransforms();
                droppedPhysics.ResolveDroppedAnatomy();
                Assert.That(droppedPhysics.DroppedAnatomySweepsSkipped, Is.GreaterThan(skipped),
                    "Unrelated moving anatomy must miss the complete swept prop before relative-motion sampling.");
                Assert.That(droppedPhysics.DroppedAnatomyPoseSamples, Is.EqualTo(sampled),
                    "A proven distant miss must not interpolate every primitive through hundreds of time samples.");
            }
            finally
            {
                body.position = savedWeaponPosition;
                actor.Weapon.transform.SetPositionAndRotation(body.position, body.rotation);
                target.transform.position = savedTargetPosition;
                Physics.SyncTransforms();
                droppedPhysics.EnableDropped();
            }
        }

        private WeaponMeshClearanceProbe ClearanceProbe
        {
            get
            {
                if (weaponMeshClearance == null || !weaponMeshClearance.BelongsTo(root.transform))
                    weaponMeshClearance = new WeaponMeshClearanceProbe(root.transform);
                return weaponMeshClearance;
            }
        }

        private void AssertWeaponClearance(CombatActor actor, string context)
        {
            Physics.SyncTransforms();
            float depth = ClearanceProbe.MaximumDepth(actor, out string obstacle);
            if (depth > .01f)
                CaptureImpactRecoveryFrame(actor, actor.IsHero ? "hero-weapon-clearance" : "opponent-weapon-clearance",
                    "penetration-failure", actor.transform.position, actor.transform.rotation, true);
            Assert.That(depth, Is.LessThanOrEqualTo(.01f), context + ": rendered crowbar enters " + obstacle +
                " by " + depth.ToString("F4") + "m; phase=" + actor.State.Phase + ", clip=" + actor.ActiveClipName +
                ", dropped=" + actor.IsWeaponDropped + ", solver=" + actor.WeaponPenetrationDepth.ToString("F4") +
                "/" + actor.WeaponBlockingShape + ". The oracle samples imported mesh surfaces, not solver capsules.");
        }

        private sealed class WeaponMeshClearanceProbe
        {
            private const float SampleRadius = .001f;
            private readonly Transform owner;
            private readonly CombatTestRoot range;
            private readonly SphereCollider probe;
            private readonly Dictionary<GameObject, Vector3[]> samples = new Dictionary<GameObject, Vector3[]>();
            private readonly Dictionary<Collider, Collider> primitiveCopies = new Dictionary<Collider, Collider>();
            private readonly Collider[] nearby = new Collider[128];
            private readonly HashSet<Collider> candidates = new HashSet<Collider>();

            internal WeaponMeshClearanceProbe(Transform parent)
            {
                owner = parent;
                range = parent.GetComponent<CombatTestRoot>();
                Assert.That(range, Is.Not.Null);
                var query = new GameObject("Weapon mesh clearance oracle") { hideFlags = HideFlags.HideAndDontSave };
                query.transform.SetParent(parent, false);
                // Fresh disabled primitives have no native query shape on the
                // pinned backend. Keep the independent sphere as a remote trigger.
                query.transform.position = Vector3.up * 10000f;
                probe = query.AddComponent<SphereCollider>();
                probe.radius = SampleRadius;
                probe.isTrigger = true;
                probe.enabled = true;
                Physics.SyncTransforms();
            }

            internal bool BelongsTo(Transform parent) => owner != null && owner == parent && probe != null;

            internal Vector3 HookSurface(CombatActor actor)
            {
                Vector3 tip = CombatAssetProvider.FindAnchor(actor.Weapon, "StrikeTip").position;
                Vector3 closest = actor.Weapon.transform.position;
                float distance = float.PositiveInfinity;
                foreach (Vector3 local in Samples(actor))
                {
                    Vector3 point = World(actor, local);
                    float candidate = (point - tip).sqrMagnitude;
                    if (candidate < distance) { closest = point; distance = candidate; }
                }
                return closest;
            }

            internal Vector3 LowestWorldPoint(CombatActor actor)
            {
                Vector3 lowest = Vector3.positiveInfinity;
                foreach (Vector3 local in Samples(actor))
                {
                    Vector3 point = World(actor, local);
                    if (point.y < lowest.y) lowest = point;
                }
                return lowest;
            }

            internal Vector3 ShaftSurfaceOffset(CombatActor actor, Vector3 toward)
                => ShaftSurfaceOffset(actor, toward, out _);

            internal Vector3 ShaftSurfaceOffset(CombatActor actor, Vector3 toward, out Vector3 sample)
            {
                Vector3 grip = CombatAssetProvider.FindAnchor(actor.Weapon, "Grip").position;
                Vector3 axis = (CombatAssetProvider.FindAnchor(actor.Weapon, "StrikeBase").position - grip).normalized;
                Vector3 desired = grip + axis * .25f + toward * .019f;
                Vector3 closest = Vector3.zero;
                sample = Vector3.zero;
                float distance = float.PositiveInfinity;
                foreach (Vector3 local in Samples(actor))
                {
                    Vector3 point = World(actor, local);
                    float candidate = (point - desired).sqrMagnitude;
                    if (candidate < distance) { closest = point; distance = candidate; sample = local; }
                }
                return closest - actor.Weapon.transform.position;
            }

            internal float RadialClearanceFrom(CombatActor actor, CharacterController controller)
            {
                Vector3 center = controller.transform.TransformPoint(controller.center);
                float scale = Mathf.Max(Mathf.Abs(controller.transform.lossyScale.x), Mathf.Abs(controller.transform.lossyScale.z));
                float minimum = float.PositiveInfinity;
                foreach (Vector3 local in Samples(actor))
                    minimum = Mathf.Min(minimum, Vector3.ProjectOnPlane(World(actor, local) - center,
                        controller.transform.up).magnitude - controller.radius * scale);
                return minimum;
            }

            internal float MaximumDepth(CombatActor actor, out string obstacle)
            {
                candidates.Clear();
                Bounds bounds = new Bounds(actor.Weapon.transform.position, Vector3.zero);
                foreach (Vector3 local in Samples(actor)) bounds.Encapsulate(World(actor, local));
                int count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents + Vector3.one * SampleRadius,
                    nearby, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                Assert.That(count, Is.LessThan(nearby.Length), "The clearance oracle's broadphase must not silently truncate.");
                for (int i = 0; i < count; i++)
                {
                    Collider collider = nearby[i];
                    if (collider == null || collider == probe || collider is CharacterController ||
                        collider.transform.IsChildOf(actor.Weapon.transform) ||
                        collider.GetComponentInParent<CombatActor>() != null) continue;
                    candidates.Add(collider);
                }
                // Disabled live anatomical shapes still represent the posed body.
                // Only the intentional fingers/palm grip is exempt; forearms remain.
                if (!actor.IsWeaponDropped)
                    foreach (var entry in actor.Ragdoll.PhysicsController.AnatomicalColliders)
                        if (entry.Value != Player3DAnatomicalPart.LeftHand && entry.Value != Player3DAnatomicalPart.RightHand &&
                            actor.Ragdoll.PhysicsController.IsCombatAnatomicalQueryAllowed(entry.Key))
                            candidates.Add(entry.Key);
                // Opposing anatomy is disabled while animated too. A registered
                // hit must stop the visible steel at the struck body surface.
                CombatActor other = actor == range.Hero ? range.Opponent : range.Hero;
                if (other != null && other.Ragdoll != null)
                    foreach (var entry in other.Ragdoll.PhysicsController.AnatomicalColliders)
                        if (other.Ragdoll.PhysicsController.IsCombatAnatomicalQueryAllowed(entry.Key))
                            candidates.Add(entry.Key);

                float maximum = 0f;
                obstacle = "nothing";
                foreach (Collider collider in candidates)
                {
                    float depth = DepthAgainst(actor, collider);
                    if (depth > maximum) { maximum = depth; obstacle = collider.name; }
                }
                // A concave floor mesh has triangle surfaces, not a filled solid.
                // A ray from above also catches a rendered tip that already crossed
                // its surface farther than the tiny sphere's contact thickness.
                Vector3 lowest = LowestWorldPoint(actor);
                foreach (RaycastHit hit in Physics.RaycastAll(lowest + Vector3.up * 2f, Vector3.down, 4f,
                    ~0, QueryTriggerInteraction.Ignore))
                {
                    if (!hit.collider.name.Contains("Collision_Floor") || hit.normal.y < .5f) continue;
                    float depth = hit.point.y - lowest.y;
                    if (depth > maximum) { maximum = depth; obstacle = hit.collider.name; }
                }
                return maximum;
            }

            internal float DepthAgainst(CombatActor actor, Collider collider)
            {
                if (collider == null) return 0f;
                Bounds bounds = ShapeBounds(collider);
                bounds.Expand(SampleRadius * 2f);
                float maximum = 0f;
                Vector3 origin = actor.Weapon.transform.position;
                Quaternion rotation = actor.Weapon.transform.rotation;
                Collider query = null;
                foreach (Vector3 local in Samples(actor))
                {
                    Vector3 point = origin + rotation * local;
                    if (!bounds.Contains(point)) continue;
                    query ??= QueryShape(collider);
                    if (Physics.ComputePenetration(probe, point, Quaternion.identity, query,
                        collider.transform.position, collider.transform.rotation, out _, out float depth))
                        maximum = Mathf.Max(maximum, depth - SampleRadius);
                }
                return maximum;
            }

            internal Collider QueryShape(Collider source)
            {
                if (source.enabled || !(source is CapsuleCollider || source is BoxCollider || source is SphereCollider))
                    return source;
                if (!primitiveCopies.TryGetValue(source, out Collider copy))
                {
                    var control = new GameObject("Test native anatomical clearance query") { hideFlags = HideFlags.HideAndDontSave };
                    control.transform.position = Vector3.up * 10000f + Vector3.right * (primitiveCopies.Count * 2f);
                    control.transform.SetParent(owner, true);
                    copy = source is CapsuleCollider ? control.AddComponent<CapsuleCollider>() :
                        source is BoxCollider ? (Collider)control.AddComponent<BoxCollider>() : control.AddComponent<SphereCollider>();
                    copy.isTrigger = true; copy.enabled = true;
                    primitiveCopies.Add(source, copy);
                }
                Vector3 signedScale = source.transform.lossyScale;
                Vector3 scale = new Vector3(Mathf.Abs(signedScale.x), Mathf.Abs(signedScale.y), Mathf.Abs(signedScale.z));
                if (source is CapsuleCollider capsule && copy is CapsuleCollider copiedCapsule)
                {
                    copiedCapsule.center = Vector3.Scale(capsule.center, signedScale);
                    copiedCapsule.direction = capsule.direction;
                    copiedCapsule.radius = capsule.radius * Mathf.Max(scale[(capsule.direction + 1) % 3], scale[(capsule.direction + 2) % 3]);
                    copiedCapsule.height = Mathf.Max(capsule.height * scale[capsule.direction], copiedCapsule.radius * 2f);
                }
                else if (source is BoxCollider box && copy is BoxCollider copiedBox)
                { copiedBox.center = Vector3.Scale(box.center, signedScale); copiedBox.size = Vector3.Scale(box.size, scale); }
                else if (source is SphereCollider sphere && copy is SphereCollider copiedSphere)
                {
                    copiedSphere.center = Vector3.Scale(sphere.center, signedScale);
                    copiedSphere.radius = sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
                }
                Physics.SyncTransforms();
                return copy;
            }

            private static Bounds ShapeBounds(Collider collider)
            {
                Vector3 center, size;
                if (collider is CapsuleCollider capsule)
                {
                    Vector3 signedScale = capsule.transform.lossyScale;
                    Vector3 scale = new Vector3(Mathf.Abs(signedScale.x), Mathf.Abs(signedScale.y), Mathf.Abs(signedScale.z));
                    float radius = capsule.radius * Mathf.Max(scale[(capsule.direction + 1) % 3],
                        scale[(capsule.direction + 2) % 3]);
                    Vector3 extents = Vector3.one * radius;
                    extents[capsule.direction] = Mathf.Max(capsule.height * scale[capsule.direction] * .5f, radius);
                    Vector3 worldCenter = capsule.transform.TransformPoint(capsule.center);
                    var capsuleBounds = new Bounds(worldCenter, Vector3.zero);
                    for (int x = -1; x <= 1; x += 2)
                        for (int y = -1; y <= 1; y += 2)
                            for (int z = -1; z <= 1; z += 2)
                                capsuleBounds.Encapsulate(worldCenter + capsule.transform.rotation *
                                    Vector3.Scale(extents, new Vector3(x, y, z)));
                    return capsuleBounds;
                }
                else if (collider is BoxCollider box) { center = box.center; size = box.size; }
                else if (collider is SphereCollider sphere) { center = sphere.center; size = Vector3.one * sphere.radius * 2f; }
                else return collider.bounds;
                var result = new Bounds(collider.transform.TransformPoint(center), Vector3.zero);
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                        for (int z = -1; z <= 1; z += 2)
                            result.Encapsulate(collider.transform.TransformPoint(center + Vector3.Scale(size, new Vector3(x, y, z)) * .5f));
                return result;
            }

            private Vector3[] Samples(CombatActor actor)
            {
                if (samples.TryGetValue(actor.Weapon, out Vector3[] points)) return points;
                Transform weapon = actor.Weapon.transform;
                var unique = new Dictionary<Vector3Int, Vector3>();
                foreach (MeshFilter filter in actor.Weapon.GetComponentsInChildren<MeshFilter>(true))
                {
                    Mesh mesh = filter.sharedMesh;
                    Assert.That(mesh, Is.Not.Null);
                    Assert.That(mesh.isReadable, Is.True, "The imported prop is the independent clearance reference.");
                    Vector3[] vertices = mesh.vertices;
                    for (int i = 0; i < vertices.Length; i++)
                        vertices[i] = Quaternion.Inverse(weapon.rotation) * (filter.transform.TransformPoint(vertices[i]) - weapon.position);
                    foreach (Vector3 vertex in vertices) Add(vertex);
                    int[] triangles = mesh.triangles;
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                        Edge(a, b); Edge(b, c); Edge(c, a);
                        Add((a + b + c) / 3f);
                    }
                }
                Assert.That(unique.Count, Is.GreaterThan(40), "Clearance must sample the complete visible crowbar.");
                points = new Vector3[unique.Count];
                unique.Values.CopyTo(points, 0);
                samples.Add(actor.Weapon, points);
                return points;

                void Add(Vector3 point)
                {
                    var key = new Vector3Int(Mathf.RoundToInt(point.x * 1000f), Mathf.RoundToInt(point.y * 1000f),
                        Mathf.RoundToInt(point.z * 1000f));
                    if (!unique.ContainsKey(key)) unique.Add(key, point);
                }
                void Edge(Vector3 a, Vector3 b)
                {
                    int intervals = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / .015f));
                    for (int i = 1; i < intervals; i++) Add(Vector3.Lerp(a, b, i / (float)intervals));
                }
            }

            private static Vector3 World(CombatActor actor, Vector3 local) =>
                actor.Weapon.transform.position + actor.Weapon.transform.rotation * local;
        }
    }
}
