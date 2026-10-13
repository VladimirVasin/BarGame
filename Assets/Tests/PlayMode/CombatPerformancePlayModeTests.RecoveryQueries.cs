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
