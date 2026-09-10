using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        private static void ValidatePortTareClearance(CityPortController port, CityPortCrew crew)
        {
            // The caller advances the real journey densely. This check must
            // never seek its clocks or reconstruct the social state.
            if (port.Snapshot.Stage != CityPortCycleStage.Prepare && port.Snapshot.Stage != CityPortCycleStage.Secure &&
                port.Snapshot.Stage != CityPortCycleStage.Unmoor) return;
            var body = crew.ShoreWorker.GetComponent<CapsuleCollider>();
            Assert.That(body, Is.Not.Null);
            int checkedSolids = 0;
            foreach (var solid in port.Dock.GetComponentsInChildren<MeshCollider>())
                if (solid.name == "DockVisible__Tare" || solid.name.StartsWith("COL_Awning", StringComparison.Ordinal))
                {
                    checkedSolids++;
                    bool overlap = Physics.ComputePenetration(body, body.transform.position, body.transform.rotation,
                        solid, solid.transform.position, solid.transform.rotation, out _, out float depth);
                    Assert.That(overlap && depth > .005f, Is.False,
                        $"Shore worker crosses {solid.name} during {port.Snapshot.Stage} at {port.Snapshot.SecondsInStage:F1}s ({depth:F3}m).");
                }
            Assert.That(checkedSolids, Is.GreaterThanOrEqualTo(2), "Check the actual tare and its supporting awning.");
        }

        private static void ValidatePortPhysicalBodies(CityGameRoot city, CityPortController port, CityPortCrew crew)
        {
            var cannery = city.Cannery;
            Assert.That(cannery, Is.Not.Null, "Stored port fish belongs to the finite supply owner.");
            var hero = city.Player.GameObject.GetComponent<CharacterController>();
            double savedPort = port.ElapsedSeconds, savedLife = crew.LifeElapsedSeconds, savedSupply = cannery.WorkingSeconds;
            bool forcePort = port.ForcePresentation, forceSupply = cannery.ForcePresentation;
            Transform observer = port.PresentationObserver;
            var far = new GameObject("Port collision distance probe");
            try
            {
                port.ForcePresentation = cannery.ForcePresentation = true;
                port.ApplyAt(CityPortCycle.UnloadStartSeconds + CityPortCycle.LandedAtSeconds + .5d, 0f);
                crew.ApplyAt(port.ElapsedSeconds, savedLife + 1000d);
                AssertPortBodyBlocksHero(hero, CityPortAssetProvider.FindPart(port.Dock.gameObject, "DockVisible__Tare").GetComponent<Collider>());
                AssertPortBodyBlocksHero(hero, port.Cargo[0].GetComponent<Collider>());
                var trolley = port.Trolley.GetComponent<Collider>();
                AssertPortBodyBlocksHero(hero, trolley);
                AssertPortBodyBlocksHero(hero, crew.ShoreWorker.GetComponent<Collider>());
                for (int role = 0; role < crew.WorkerCount; role++)
                    Assert.That(crew.GetWorker(role).GetComponent<CapsuleCollider>(), Is.Not.Null);

                cannery.ApplyAt(CanneryTime(cannery, CityFishSupplyStage.LoadFish, 0f));
                var fish = new Collider[CityFishSupplyCycle.HandlingUnits];
                var storedBounds = new Bounds[fish.Length];
                for (int i = 0; i < fish.Length; i++)
                {
                    fish[i] = cannery.transform.Find("Fish handling unit " + i).GetComponent<Collider>();
                    Assert.That(fish[i], Is.Not.Null);
                    Assert.That(fish[i].gameObject.activeInHierarchy, Is.True);
                    storedBounds[i] = fish[i].bounds;
                }
                AssertPortBodyBlocksHero(hero, fish[0]);
                cannery.ApplyAt(TransferTime(cannery, CityFishSupplyStage.LoadFish, 0, .4f));
                Physics.SyncTransforms();
                Assert.That(Vector3.Distance(fish[0].bounds.center, storedBounds[0].center), Is.GreaterThan(.5f),
                    "The same physical fish unit follows the handoff out of storage.");
                AssertPortBodyAbsent(fish[0], storedBounds[0]);
                cannery.ApplyAt(CanneryTime(cannery, CityFishSupplyStage.LoadFinished, 0f));
                for (int i = 0; i < fish.Length; i++)
                {
                    Assert.That(fish[i].gameObject.activeInHierarchy, Is.False, "Consumed fish has no hidden body.");
                    AssertPortBodyAbsent(fish[i], storedBounds[i]);
                }
                foreach (Transform cargo in port.Cargo)
                    Assert.That(cargo.gameObject.activeInHierarchy, Is.False, "Stored cages leave no hidden collision host.");

                Bounds parked = trolley.bounds;
                far.transform.position = port.Plan.Origin + Vector3.one * 1000f;
                port.PresentationObserver = far.transform; port.ForcePresentation = false;
                port.RefreshPresentation(); Physics.SyncTransforms();
                Assert.That(port.ShorePresentationActive, Is.False);
                AssertPortBodyAbsent(trolley, parked);
                port.ForcePresentation = true; port.RefreshPresentation(); Physics.SyncTransforms();
                Assert.That(trolley.enabled, Is.True, "Approach restores the cart at its current logical pose.");
                Debug.Log("PORT COLLISION: real hero blocks on tare/cage/cart/worker/stored fish; finite handoff and distance leave no ghost body.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(far);
                cannery.ForcePresentation = forceSupply; port.PresentationObserver = observer;
                cannery.ApplyAt(savedSupply); port.ForcePresentation = forcePort;
                port.ApplyAt(savedPort, 0f); crew.ApplyAt(savedPort, savedLife); Physics.SyncTransforms();
            }
        }

        private static void AssertPortBodyAbsent(Collider body, Bounds previous)
        {
            Physics.SyncTransforms();
            Assert.That(Array.IndexOf(Physics.OverlapBox(previous.center, previous.extents * .95f,
                Quaternion.identity, ~0, QueryTriggerInteraction.Ignore), body), Is.EqualTo(-1),
                body.name + " must not leave a collider at its former visible position.");
        }

        private static void AssertPortBodyBlocksHero(CharacterController hero, Collider body)
        {
            Assert.That(hero, Is.Not.Null); Assert.That(body, Is.Not.Null);
            Assert.That(body.enabled && body.gameObject.activeInHierarchy && !body.isTrigger, Is.True, body.name);
            Assert.That(Physics.GetIgnoreLayerCollision(hero.gameObject.layer, body.gameObject.layer), Is.False);
            Vector3 saved = hero.transform.position;
            bool enabled = hero.enabled, ignoredTarget = Physics.GetIgnoreCollision(hero, body);
            var ignored = new List<Collider>();
            Physics.SyncTransforms();
            Bounds bounds = body.bounds;
            Vector3 direction = body is MeshCollider ? Vector3.forward : body.transform.right;
            direction.y = 0f; direction.Normalize();
            float travel = 2f * (bounds.extents.magnitude + hero.radius + .5f);
            Vector3 start = bounds.center - direction * travel * .5f;
            start.y = bounds.min.y - (hero.center.y - hero.height * .5f) + .02f;
            try
            {
                // Isolate only neighbours in this short sweep. Repeating it
                // with this exact body ignored proves which object stopped us.
                foreach (Collider other in Physics.OverlapBox(start + direction * travel * .5f + Vector3.up,
                    new Vector3(travel + 1f, 3f, travel + 1f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                    if (other != hero && other != body && !Physics.GetIgnoreCollision(hero, other))
                    { Physics.IgnoreCollision(hero, other, true); ignored.Add(other); }
                hero.enabled = true; hero.transform.position = start;
                Physics.IgnoreCollision(hero, body, false); Physics.SyncTransforms();
                hero.Move(direction * travel);
                Assert.That(Vector3.Dot(hero.transform.position - start, direction), Is.LessThan(travel - .2f), body.name);
                hero.transform.position = start;
                Physics.IgnoreCollision(hero, body, true); Physics.SyncTransforms();
                hero.Move(direction * travel);
                Assert.That(Vector3.Dot(hero.transform.position - start, direction), Is.GreaterThan(travel - .05f),
                    body.name + " must be the blocker, rather than an unrelated neighbouring wall.");
            }
            finally
            {
                hero.transform.position = saved; hero.enabled = enabled;
                Physics.IgnoreCollision(hero, body, ignoredTarget);
                foreach (Collider other in ignored) Physics.IgnoreCollision(hero, other, false);
                Physics.SyncTransforms();
            }
        }
    }
}
