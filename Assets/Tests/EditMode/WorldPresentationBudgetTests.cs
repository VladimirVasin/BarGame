using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.EditMode
{
    public sealed class WorldPresentationBudgetTests
    {
        private readonly List<GameObject> roots = new List<GameObject>();

        [TearDown]
        public void Cleanup()
        {
            foreach (GameObject root in roots)
                if (root != null)
                {
                    foreach (VillageResidentPresentation actor in root.GetComponentsInChildren<VillageResidentPresentation>(true))
                        actor.ReleaseAnimation();
                    Object.DestroyImmediate(root);
                }
            roots.Clear();
        }

        [Test]
        public void HairDistanceBudgetPreservesClockPauseAndDirectContactSampling()
        {
            var root = Root("Test cannery worker");
            VillageResidentPresentation actor = CanneryWomanAssetProvider.Create(root.transform);
            CanneryWomanHair hair = actor.GetComponent<CanneryWomanPresentation>().Hair;
            Assert.That(hair.HasAuthoredBindings, Is.True);
            double time = 10d;
            hair.ApplyAt(time, false, false, default);
            int resets = hair.ResetCount;
            Vector3 far = actor.Head.position + Vector3.forward * 32f;
            Vector3 previousTip = hair.WorldPoint(3);
            for (int frame = 0; frame < 36; frame++)
            {
                time += 1d / 60d;
                actor.ApplyIdleVariation((float)time, false, 0f);
                hair.ApplyForObserver(time, false, false, default, far, null);
                Assert.That(hair.LastStepSeconds, Is.EqualTo(1f / 60f).Within(.00001f));
                Assert.That(Vector3.Distance(hair.WorldPoint(3), previousTip), Is.LessThan(.05f),
                    "Changing the refinement budget must not restart or snap the displayed strand.");
                previousTip = hair.WorldPoint(3);
            }
            Assert.That(hair.SurfaceContactIterationLimit, Is.EqualTo(2));
            Assert.That(hair.LastSurfaceContactIterations, Is.InRange(1, 2));
            Assert.That(hair.ResetCount, Is.EqualTo(resets));
            var paused = new Vector3[CanneryWomanHair.PointCount];
            for (int i = 0; i < paused.Length; i++) paused[i] = hair.WorldPoint(i);
            for (int frame = 0; frame < 3; frame++) hair.ApplyForObserver(time, true, false, default, far, null);
            Assert.That(hair.LastStepSeconds, Is.Zero);
            Assert.That(hair.LastSurfaceContactIterations, Is.Zero);
            for (int i = 0; i < paused.Length; i++)
                Assert.That(Vector3.Distance(paused[i], hair.WorldPoint(i)), Is.LessThan(.001f));
            for (int frame = 0; frame < 18; frame++)
            {
                time += 1d / 60d;
                actor.ApplyIdleVariation((float)time, false, 0f);
                hair.ApplyForObserver(time, false, false, default, actor.Head.position + Vector3.forward * 2f, null);
                Assert.That(hair.LastStepSeconds, Is.EqualTo(1f / 60f).Within(.00001f),
                    "Nearby return consumes this frame only, without distant simulation debt.");
            }
            Assert.That(hair.SurfaceContactIterationLimit, Is.EqualTo(8));
            Assert.That(hair.ResetCount, Is.EqualTo(resets));
            hair.ApplyForObserver(time += 1d / 60d, false, false, default, far, null);
            hair.ApplyAt(time += 1d / 60d, false, false, default);
            Assert.That(hair.SurfaceContactIterationLimit, Is.EqualTo(8),
                "Explicit authoring and contact oracles retain full refinement.");

            Assert.That(CanneryWomanHair.ShouldUseFullDetail(22f, 0f, true), Is.True);
            Assert.That(CanneryWomanHair.ShouldUseFullDetail(22f, 0f, false), Is.False);
            Assert.That(CanneryWomanHair.ShouldUseFullDetail(32f, .04f, false), Is.True,
                "A close or zoomed presentation camera keeps detailed hair.");
        }

        [Test]
        public void AutomaticGreetingRetryWaitsForSimulationTimeWhileExplicitEligibilityStaysImmediate()
        {
            var life = Root("Test household greetings").AddComponent<AlpineVillageLifeController>();
            var resident = Root("Test neighbour").AddComponent<VillageResidentPresentation>();
            var automatic = (Action<VillageResidentRole, VillageResidentPresentation>)Delegate.CreateDelegate(
                typeof(Action<VillageResidentRole, VillageResidentPresentation>), life,
                typeof(AlpineVillageLifeController).GetMethod("TryAutomaticGreeting", BindingFlags.Instance | BindingFlags.NonPublic));
            var advance = (Action<float, double, float, Vector3>)Delegate.CreateDelegate(
                typeof(Action<float, double, float, Vector3>), life,
                typeof(AlpineVillageLifeController).GetMethod("AdvanceStep", BindingFlags.Instance | BindingFlags.NonPublic));
            var cooldown = (float[])typeof(AlpineVillageLifeController).GetField("automaticGreetingCooldown",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(life);
            const VillageResidentRole role = VillageResidentRole.RepairNeighbor;
            automatic(role, resident);
            for (int i = 0; i < 50; i++) automatic(role, resident);
            Assert.That(cooldown[(int)role], Is.EqualTo(AlpineVillageLifeController.AutomaticGreetingInterval),
                "Repeated frames cannot advance an automatic retry clock while time is paused.");
            advance(.1f, 600d, 0f, Vector3.zero);
            automatic(role, resident);
            Assert.That(cooldown[(int)role], Is.EqualTo(.1f).Within(.00001f));
            advance(.15f, 600d, 0f, Vector3.zero);
            automatic(role, resident);
            Assert.That(cooldown[(int)role], Is.EqualTo(AlpineVillageLifeController.AutomaticGreetingInterval),
                "An elapsed retry produces one current check, without replaying missed attempts.");
            // The explicit path reads speech/task eligibility, not the automatic
            // timer. A workroom resident can answer E during its retry interval.
            var workroom = Root("Test workroom").AddComponent<VillageWorkroomController>();
            object repair = typeof(VillageWorkroomController).GetField("repair", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(workroom);
            repair.GetType().GetField("Active", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(repair, true);
            repair.GetType().GetField("Action", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(repair, VillageResidentAction.RepairWork);
            typeof(AlpineVillageLifeController).GetField("workroom", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(life, workroom);
            Assert.That(life.CanTalk(role), Is.True);
        }

        [Test]
        public void ResidentVisibilityKeepsOcclusionAfterBufferOverflowAndReusesStorage()
        {
            var owner = Root("Test resident visibility");
            var view = owner.AddComponent<VillageWorkroomController>();
            var viewer = Root("Test viewer");
            var resident = Root("Test resident");
            // More ignored actor colliders than the initial ray buffer. A wall
            // must remain observable even if an unordered first result omits it.
            Vector3 from = new Vector3(4000f, 4000f, 4000f), to = from + Vector3.forward * 10f;
            for (int i = 0; i < 40; i++)
            {
                var child = new GameObject("Test resident contact");
                child.transform.SetParent(resident.transform, false);
                child.transform.position = from + Vector3.forward * (.5f + i * .2f);
                child.AddComponent<BoxCollider>().size = new Vector3(.2f, .2f, .05f);
            }
            var wall = Root("Test wall");
            wall.transform.position = from + Vector3.forward * 9f;
            wall.AddComponent<BoxCollider>().size = new Vector3(1f, 1f, .1f);
            Physics.SyncTransforms();
            Assert.That(view.HasClearResidentView(from, to, viewer.transform, resident.transform), Is.False);
            wall.SetActive(false);
            Physics.SyncTransforms();
            Assert.That(view.HasClearResidentView(from, to, viewer.transform, resident.transform), Is.True);
            // Warm up the native query before measuring the managed contract.
            view.HasClearResidentView(from, to, viewer.transform, resident.transform);
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool clear = true;
            for (int i = 0; i < 30; i++) clear &= view.HasClearResidentView(from, to, viewer.transform, resident.transform);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(clear, Is.True);
            Assert.That(allocated, Is.Zero, "The grown buffer is retained for later automatic or E visibility checks.");
        }

        private GameObject Root(string name)
        {
            var root = new GameObject(name);
            roots.Add(root);
            return root;
        }
    }
}
