using System;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        private static void AssertPistolConcealedByJacket(CombatActor hero, bool settled = true)
        {
            Assert.That(hero.CarriedWeaponLocation(CombatWeaponId.Pistol), Is.EqualTo(CombatWeaponCarryLocation.Stowed));
            AssertPistolChestFit(hero, measureSettledDrape: settled);
            AssertConcealedPistolDoesNotDriveJacket(hero);
            GameObject pistol = hero.CarriedWeapon(CombatWeaponId.Pistol);
            AssertConcealedRenderers(pistol, true);
            AssertConcealedRenderers(hero.CarryMount(CombatWeaponId.Pistol).parent.gameObject, true);
        }

        private static void AssertConcealedPistolDoesNotDriveJacket(CombatActor hero)
        {
            PlayerJacketCloth cloth = hero.GetComponentInChildren<PlayerJacketCloth>();
            Assert.That(cloth.ClothPhysicsEnabled, Is.False, "The gameplay jacket uses the current rig-only contract.");
            var visual = hero.GetComponentInChildren<Player3DCharacterPresentation>();
            var presented = new System.Collections.Generic.Dictionary<SkinnedMeshRenderer, Vector3[]>();
            var bones = new System.Collections.Generic.Dictionary<Transform, Matrix4x4>();
            var baked = new Mesh { name = "Test concealed gear jacket silhouette" };
            GameObject pistol = hero.CarriedWeapon(CombatWeaponId.Pistol);
            GameObject holster = hero.CarryMount(CombatWeaponId.Pistol).parent.gameObject;
            bool pistolActive = pistol.activeSelf, holsterActive = holster.activeSelf;
            int frame = Time.frameCount;
            long steps = cloth.TotalSimulationSteps;
            try
            {
                // Sample the completed rig once. Reapplying overlays between
                // comparisons would supply a different skinning input even
                // though no animation time or frame had advanced.
                visual.ReapplyLatePresentationPose();
                RefreshCurrentJacket();
                foreach (SkinnedMeshRenderer skin in hero.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!skin.name.StartsWith("CLO_Jacket", StringComparison.Ordinal) ||
                        !skin.enabled || skin.forceRenderingOff || !skin.gameObject.activeInHierarchy) continue;
                    skin.BakeMesh(baked, true);
                    presented.Add(skin, baked.vertices);
                    foreach (Transform bone in skin.bones)
                        if (!bones.ContainsKey(bone)) bones.Add(bone, bone.localToWorldMatrix);
                }
                Assert.That(presented.Count, Is.GreaterThan(0), "Compare the current visible M65, not retired solver output.");
                RefreshCurrentJacket();
                AssertCurrentOutput("Same-gear control");
                // Same bones and authored access state, with only concealed
                // equipment presence changed. Bake the actual rendered skin.
                pistol.SetActive(false); holster.SetActive(false);
                RefreshCurrentJacket();
                AssertCurrentOutput("Concealed gear absent");
                Assert.That(Time.frameCount, Is.EqualTo(frame), "The hidden-gear baseline must use the same moving pose.");
                Assert.That(cloth.TotalSimulationSteps, Is.EqualTo(steps), "The geometry oracle must not enable cloth simulation.");
            }
            finally
            {
                holster.SetActive(holsterActive); pistol.SetActive(pistolActive);
                RefreshCurrentJacket();
                UnityEngine.Object.DestroyImmediate(baked);
            }

            void RefreshCurrentJacket()
            {
                cloth.ApplyAt(0d, paused: false, outside: false, wind: default);
            }

            void AssertCurrentOutput(string phase)
            {
                foreach (var pair in bones)
                    Assert.That(pair.Key.localToWorldMatrix, Is.EqualTo(pair.Value),
                        phase + ": the geometry comparison must retain the exact same bone pose: " + pair.Key.name);
                foreach (var pair in presented)
                {
                    pair.Key.BakeMesh(baked, true);
                    AssertSameOutput(pair.Value, baked.vertices, pair.Key, phase);
                }
            }

            static void AssertSameOutput(Vector3[] withGear, Vector3[] withoutGear, SkinnedMeshRenderer skin, string phase)
            {
                Assert.That(withoutGear.Length, Is.EqualTo(withGear.Length));
                int differentComponents = 0;
                float maximumWorldDelta = 0f;
                for (int vertex = 0; vertex < withGear.Length; vertex++)
                {
                    maximumWorldDelta = Mathf.Max(maximumWorldDelta,
                        skin.transform.TransformVector(withGear[vertex] - withoutGear[vertex]).magnitude);
                    for (int axis = 0; axis < 3; axis++)
                        if (BitConverter.SingleToInt32Bits(withGear[vertex][axis]) !=
                            BitConverter.SingleToInt32Bits(withoutGear[vertex][axis])) differentComponents++;
                }
                Assert.That(differentComponents, Is.Zero,
                    $"{phase}, {skin.name}: concealed pistol and holster must not alter the visible jacket at the identical rig pose; " +
                    $"changed components={differentComponents}, maximum world delta={maximumWorldDelta:R} m.");
            }
        }

        private static void AssertConcealedRenderers(GameObject model, bool concealed)
        {
            int measured = 0;
            foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.name.IndexOf("Flash", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Assert.That(filter != null && filter.sharedMesh != null, Is.True,
                    "Concealment must retain the same authored geometry: " + renderer.name);
                Assert.That(renderer.forceRenderingOff, Is.EqualTo(concealed),
                    "Jacket coverage must hide stowed gear and reveal the same drawn prop: " + renderer.name);
                measured++;
            }
            Assert.That(measured, Is.GreaterThan(0));
        }

        private static void AssertPistolChestFit(CombatActor hero, bool measureSettledDrape = true)
        {
            Vector3 dock = hero.transform.InverseTransformPoint(hero.CarryMount(CombatWeaponId.Pistol).position);
            Assert.That(dock.x, Is.LessThan(-.035f), "The concealed holster belongs on the anatomical left chest.");
            Assert.That(dock.y, Is.InRange(1.20f, 1.49f), "The pistol grip must be at the chest, above the waist.");
            PlayerJacketCloth cloth = hero.GetComponentInChildren<PlayerJacketCloth>();
            Assert.That(cloth, Is.Not.Null);
            if (!measureSettledDrape) return;
            if (!cloth.ClothPhysicsEnabled)
                cloth.ApplyAt(0d, paused: false, outside: false, wind: default);
            float largestBulge = 0f;
            string largestPoint = "";
            int samples = 0;
            foreach (SkinnedMeshRenderer skin in hero.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!skin.name.StartsWith("CLO_Jacket", StringComparison.Ordinal) ||
                    skin.name.Contains("Sleeve") || skin.name.Contains("Forearm") || skin.name.Contains("Cuff")) continue;
                int surface = -1;
                for (int i = 0; i < cloth.SurfaceCount; i++)
                    if (skin.sharedMesh == cloth.DeformedMesh(i) || skin.sharedMesh == cloth.SourceMesh(i))
                    { surface = i; break; }
                if (surface < 0) continue;
                Mesh presented = skin.sharedMesh;
                var actual = new Mesh { name = "Test jacket chest" };
                var reference = new Mesh { name = "Test jacket chest reference" };
                try
                {
                    // Both meshes use these exact bones and this exact pose;
                    // only the runtime cloth displacement differs.
                    skin.BakeMesh(actual, true);
                    skin.sharedMesh = cloth.SourceMesh(surface);
                    skin.BakeMesh(reference, true);
                    Vector3[] visible = actual.vertices, original = reference.vertices;
                    Assert.That(visible.Length, Is.EqualTo(original.Length));
                    for (int i = 0; i < visible.Length; i++)
                    {
                        Vector3 sourceWorld = skin.transform.TransformPoint(original[i]);
                        Vector3 local = hero.transform.InverseTransformPoint(sourceWorld);
                        if (local.x >= -.015f || local.x < -.24f || local.z < .015f ||
                            local.y < 1.12f || local.y > 1.50f || Vector3.Distance(local, dock) > .27f) continue;
                        samples++;
                        Vector3 visibleWorld = skin.transform.TransformPoint(visible[i]);
                        float displacement = Vector3.Distance(sourceWorld, visibleWorld);
                        if (displacement > largestBulge)
                        {
                            largestBulge = displacement;
                            largestPoint = skin.name + " vertex " + i + " " + local.ToString("F6") + " -> " +
                                hero.transform.InverseTransformPoint(visibleWorld).ToString("F6");
                        }
                    }
                }
                finally
                {
                    skin.sharedMesh = presented;
                    UnityEngine.Object.DestroyImmediate(actual);
                    UnityEngine.Object.DestroyImmediate(reference);
                }
            }
            Assert.That(samples, Is.GreaterThan(0), "The chest fit must measure the real visible M65.");
            TestContext.WriteLine("Left chest jacket extra drape " + (largestBulge * 1000f).ToString("F2") + " mm; " + largestPoint);
            Assert.That(largestBulge, Is.LessThanOrEqualTo(.005f),
                "Concealed chest gear must preserve the ordinary jacket silhouette (within 5 mm).");
        }

    }
}
