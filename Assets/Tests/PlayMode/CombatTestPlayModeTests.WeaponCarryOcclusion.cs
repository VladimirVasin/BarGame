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
            var contacts = (PlayerScarfBodyContacts)CarrySupportField(cloth, "contacts");
            var equipment = (PlayerEquipmentContacts)CarrySupportField(contacts, "equipment");
            Assert.That(equipment, Is.Not.Null, "The comparison must retain the real equipment contacts for other carried gear.");
            // The coroutine resumes after Update, before the cloth's LateUpdate.
            // Refresh both baselines from the same current rig pose.
            equipment.UpdatePose(); contacts.UpdatePose();
            var managedWithGear = new Vector3[cloth.SurfaceCount][];
            var nativeWithGear = new Vector3[cloth.SurfaceCount][];
            var managedCounts = new int[cloth.SurfaceCount];
            var nativeCounts = new int[cloth.SurfaceCount];
            int compared = 0;
            for (int surface = 0; surface < cloth.SurfaceCount; surface++)
                if (cloth.TryCompareSurfaceSolverForDiagnostics(surface, out managedWithGear[surface], out nativeWithGear[surface],
                    out managedCounts[surface], out nativeCounts[surface], out _)) compared++;
            Assert.That(compared, Is.GreaterThan(0), "The moving-pose comparison must solve actual pinned jacket panels.");
            GameObject pistol = hero.CarriedWeapon(CombatWeaponId.Pistol);
            GameObject holster = hero.CarryMount(CombatWeaponId.Pistol).parent.gameObject;
            bool pistolActive = pistol.activeSelf, holsterActive = holster.activeSelf;
            int frame = Time.frameCount;
            try
            {
                // The bones, raw panel inputs and secondary-motion history do
                // not advance. Only concealed equipment presence changes.
                pistol.SetActive(false); holster.SetActive(false);
                equipment.UpdatePose(); contacts.UpdatePose();
                for (int surface = 0; surface < cloth.SurfaceCount; surface++)
                {
                    if (nativeWithGear[surface] == null) continue;
                    Assert.That(cloth.TryCompareSurfaceSolverForDiagnostics(surface, out Vector3[] managedWithout,
                        out Vector3[] nativeWithout, out int managedCount, out int nativeCount, out _), Is.True);
                    Assert.That(managedCount, Is.EqualTo(managedCounts[surface]));
                    Assert.That(nativeCount, Is.EqualTo(nativeCounts[surface]));
                    AssertSameOutput(managedWithGear[surface], managedWithout, "Managed jacket panel " + surface);
                    AssertSameOutput(nativeWithGear[surface], nativeWithout, "Native jacket panel " + surface);
                }
                Assert.That(Time.frameCount, Is.EqualTo(frame), "The hidden-gear baseline must use the same moving pose.");
            }
            finally
            {
                holster.SetActive(holsterActive); pistol.SetActive(pistolActive);
                equipment.UpdatePose(); contacts.UpdatePose();
            }

            static void AssertSameOutput(Vector3[] withGear, Vector3[] withoutGear, string label)
            {
                Assert.That(withoutGear.Length, Is.EqualTo(withGear.Length));
                int differentComponents = 0;
                for (int vertex = 0; vertex < withGear.Length; vertex++)
                    for (int axis = 0; axis < 3; axis++)
                        if (BitConverter.SingleToInt32Bits(withGear[vertex][axis]) !=
                            BitConverter.SingleToInt32Bits(withoutGear[vertex][axis])) differentComponents++;
                Assert.That(differentComponents, Is.Zero,
                    label + ": concealed pistol and holster must add no cloth displacement to the identical ordinary-cloth input.");
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
            float largestBulge = 0f;
            string largestPoint = "";
            int samples = 0;
            foreach (SkinnedMeshRenderer skin in hero.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!skin.name.StartsWith("CLO_Jacket", StringComparison.Ordinal) ||
                    skin.name.Contains("Sleeve") || skin.name.Contains("Forearm") || skin.name.Contains("Cuff")) continue;
                int surface = -1;
                for (int i = 0; i < cloth.SurfaceCount; i++)
                    if (skin.sharedMesh == cloth.DeformedMesh(i)) { surface = i; break; }
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
