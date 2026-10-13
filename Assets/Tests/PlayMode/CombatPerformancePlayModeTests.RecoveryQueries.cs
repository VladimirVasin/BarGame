using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatPerformancePlayModeTests
    {
        [UnityTest]
        public IEnumerator Range_RecoveryRootUsesActualStairSupportAndRefusesBlockedLanding()
        {
            PlacePair(4f);
            CombatActor actor = root.Opponent;
            Assert.That(actor.IsZombie, Is.True);
            Assert.That(actor.Weapon, Is.Null);
            var scenery = new GameObject("Test recovery staircase");
            GameObject blocker = null;
            try
            {
                WarehouseArenaAssetProvider.Create("Stair", scenery.transform,
                    new Vector3(4f, 0f, .7f), Quaternion.identity);
                Physics.SyncTransforms();
                using (var pose = new CombatRecoveryPose(actor.transform, actor.DamageRigRoot, actor.Body,
                    actor.Ragdoll, actor.GetComponentInChildren<NpcHandPose>(true), true, null))
                {
                    // The recorded landing put the authored root beyond the
                    // right guard while the actual pelvis still had tread support.
                    Vector3 desired = new Vector3(5.24868631f, 1.15749955f, 2.93221736f);
                    Vector3 pelvis = new Vector3(4.91542435f, 1.344202f, 2.603808f);
                    Assert.That(Physics.Raycast(pelvis + Vector3.up * .65f, Vector3.down,
                        out RaycastHit floor, 2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True);
                    Assert.That(floor.collider.name, Does.Contain("Collision_Treads"));
                    Vector3 originalRoot = actor.transform.position;
                    Assert.That(pose.TryRecoveryRoot(desired, Quaternion.identity, pelvis, true,
                        floor.point, out Vector3 accepted), Is.True,
                        "The real tread has a supported root inside the guard; the off-stair ring alone misses it.");
                    Assert.That(Vector3.Distance(actor.transform.position, originalRoot), Is.LessThan(.00001f),
                        "Searching cannot move the root or the visible body.");
                    Assert.That(accepted.x, Is.LessThan(desired.x - .32f),
                        "The recovery uses the actual support neighbourhood rather than expanding the invalid root.");
                    Assert.That(Physics.Raycast(accepted + Vector3.up * .65f, Vector3.down,
                        out RaycastHit acceptedFloor, 2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True);
                    Assert.That(acceptedFloor.collider.name, Does.Contain("Collision_Treads"));
                    Assert.That(accepted.y, Is.EqualTo(acceptedFloor.point.y + .02f).Within(.001f));

                    blocker = WarehouseArenaAssetProvider.Create("Cargo", scenery.transform,
                        new Vector3(floor.point.x, floor.point.y, floor.point.z), Quaternion.identity);
                    blocker.name = "Test blocked stair landing";
                    Physics.SyncTransforms();
                    Assert.That(pose.TryRecoveryRoot(desired, Quaternion.identity, pelvis, true,
                        floor.point, out _), Is.False, "Actual cargo remains an authoritative blocker.");
                    Assert.That(pose.ClearanceRefusalCount, Is.InRange(1, 66));
                    blocker.SetActive(false);
                    Object.Destroy(blocker);
                    blocker = null;
                    Physics.SyncTransforms();
                    Assert.That(pose.TryRecoveryRoot(desired, Quaternion.identity, pelvis, true,
                        floor.point, out Vector3 resumed), Is.True);
                    Assert.That(Vector3.Distance(resumed, accepted), Is.LessThan(.00001f),
                        "Clearing the real blocker restores the same deterministic support.");
                }
                scenery.SetActive(false);
                Object.Destroy(scenery);
                scenery = null;

                // Start with an actual settled fall, then obstruct preparation.
                // A failed pose must keep KnockedDown and the physical landing,
                // rather than announcing Rising with a permanently zero clock.
                Assert.That(actor.TryBeginKnockdown(RecoveryTestImpact(actor, Vector3.down, 1),
                    Vector3.zero, Vector3.zero), Is.True);
                for (int frame = 0; frame < 300 && !actor.Ragdoll.IsSettled; frame++) yield return null;
                Assert.That(actor.Ragdoll.IsSettled, Is.True);
                Vector3 lyingPelvis = actor.Ragdoll.PelvisBody.transform.position;
                float landingHeight = 0f, nearestFloor = float.PositiveInfinity;
                foreach (RaycastHit hit in Physics.RaycastAll(lyingPelvis + Vector3.up * .65f, Vector3.down,
                    2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    if (hit.collider.GetComponentInParent<CombatActor>() == null && hit.collider.attachedRigidbody == null &&
                        hit.normal.y > .65f && hit.distance < nearestFloor)
                    { nearestFloor = hit.distance; landingHeight = hit.point.y; }
                Assert.That(float.IsPositiveInfinity(nearestFloor), Is.False, "The physical landing retains real floor support.");
                blocker = WarehouseArenaAssetProvider.Create("Cargo", root.transform,
                    new Vector3(lyingPelvis.x, landingHeight, lyingPelvis.z), Quaternion.identity);
                blocker.name = "Test blocked recovery landing";
                Physics.SyncTransforms();
                Vector3 heldRoot = actor.transform.position;
                for (int frame = 0; frame < 30; frame++)
                {
                    root.Tick(TickSeconds);
                    Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.KnockedDown));
                    Assert.That(actor.JournalRiseProgress, Is.Zero);
                    Assert.That(Vector3.Distance(actor.transform.position, heldRoot), Is.LessThan(.00001f));
                    Assert.That(Vector3.Distance(actor.Ragdoll.PelvisBody.transform.position, lyingPelvis), Is.LessThan(.002f));
                    yield return null;
                }
                blocker.SetActive(false);
                Object.Destroy(blocker);
                blocker = null;
                Physics.SyncTransforms();
                bool sawRise = false;
                for (int frame = 0; frame < 300 && actor.IsKnockedDown; frame++)
                {
                    root.Tick(TickSeconds);
                    if (!sawRise && actor.State.Phase == MeleePhase.Rising)
                    {
                        sawRise = true;
                        Assert.That(Vector3.Distance(actor.Ragdoll.PelvisBody.transform.position, lyingPelvis), Is.LessThan(.08f),
                            "Accepting the root retains the live lying body before its continuous gathering arc.");
                    }
                    yield return null;
                }
                Assert.That(sawRise && !actor.IsKnockedDown, Is.True, "A cleared landing resumes and completes the authored rise.");
                Assert.That(actor.State.Health, Is.EqualTo(actor.State.Settings.MaxHealth));
            }
            finally
            {
                if (blocker != null) { blocker.SetActive(false); Object.Destroy(blocker); }
                if (scenery != null) { scenery.SetActive(false); Object.Destroy(scenery); }
            }
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Range_RecoveryQueriesGrowPastOwnShapesAndStillRejectRealBlockers()
        {
            PlacePair(4f);
            CombatActor actor = root.Opponent;
            var owned = new GameObject("Test recovery owned colliders");
            owned.transform.SetParent(actor.transform, false);
            using var pose = new CombatRecoveryPose(actor.transform, actor.DamageRigRoot, actor.Body,
                actor.Ragdoll, actor.GetComponentInChildren<NpcHandPose>(true), true, actor.Weapon.transform);
            try
            {
                for (int index = 0; index < 40; index++)
                {
                    SphereCollider shape = owned.AddComponent<SphereCollider>();
                    shape.center = actor.Body.center + Vector3.up * (index * .0001f);
                    shape.radius = .015f;
                }
                Physics.SyncTransforms();
                Assert.That(pose.HasStandingClearance(), Is.True,
                    "Own shapes beyond the original scratch capacity cannot forbid standing.");
                Assert.That(pose.ClearanceQueryCapacity, Is.GreaterThanOrEqualTo(48));
                int capacity = pose.ClearanceQueryCapacity;
                Assert.That(pose.HasStandingClearance(), Is.True);
                Assert.That(pose.ClearanceQueryCapacity, Is.EqualTo(capacity), "Repeated queries retain their scratch.");

                // Exercise the complete-result fallback, not just a larger fixed buffer.
                for (int index = 40; index < 200; index++)
                {
                    SphereCollider shape = owned.AddComponent<SphereCollider>();
                    shape.center = actor.Body.center + Vector3.up * (index * .0001f);
                    shape.radius = .015f;
                }
                Physics.SyncTransforms();
                Assert.That(pose.HasStandingClearance(), Is.True);
                Assert.That(pose.ClearanceQueryCapacity, Is.EqualTo(192), "The retained scratch remains bounded.");

                root.Hero.ResetActor(actor.transform.position - actor.transform.forward * .25f, actor.transform.forward);
                Physics.SyncTransforms();
                Assert.That(pose.HasStandingClearance(), Is.False,
                    "Overflow recovery cannot silently accept a real overlapping character.");
                Assert.That(pose.GetClearanceRefusal(0).Reason, Is.EqualTo("capsule_overlap"));
                Assert.That(pose.GetClearanceRefusal(0).Obstacle, Is.SameAs(root.Hero.Body));
                root.Hero.ResetActor(actor.transform.position - actor.transform.forward * 3f, actor.transform.forward);
                Physics.SyncTransforms();
                Assert.That(pose.HasStandingClearance(), Is.True, "Leaving the obstruction immediately restores clearance.");
            }
            finally
            {
                owned.SetActive(false);
                Object.Destroy(owned);
            }
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }
    }
}
