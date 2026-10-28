using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        [Category("CombatMigrationPending")]
        [Ignore("Migration pending: this scenario has not been adapted to current zombie and warehouse contracts.")]
        [UnityTest]
        public IEnumerator Range_ProjectileWoundPreparationReusesTheFirstLayerThroughReset()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
            yield return EnterRange(false, CombatWeaponId.Pistol);
            PlacePair(4f);
            CombatActor target = root.Opponent;
            var layers = PreparedWoundLayers(target);
            Assert.That(layers.Count, Is.EqualTo(1), "Loading prepares one reserve layer, not every possible wound surface.");
            object first = layers[0];
            var layerType = first.GetType();
            var renderer = (SkinnedMeshRenderer)layerType.GetField("Renderer").GetValue(first);
            var holes = (Vector4[])layerType.GetField("Holes").GetValue(first);
            var shapes = (Vector4[])layerType.GetField("Shapes").GetValue(first);
            var states = (Vector4[])layerType.GetField("States").GetValue(first);
            Material material = renderer.sharedMaterial;
            Assert.That(renderer.enabled, Is.False);
            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader.name, Is.EqualTo("Bar Promenade/Combat Wound"));
            Assert.That(renderer.sharedMesh, Is.Not.Null);
            AssertPreparedWoundBlock(renderer, holes.Length, 0);
            Assert.That(root.BloodEffects.ProjectileWoundCountFor(target), Is.Zero);
            Assert.That(root.BloodEffects.ActiveDropCount, Is.Zero);

            double firstMilliseconds = ApplyFirstPreparedWound(target);
            Assert.That(layers.Count, Is.EqualTo(1));
            Assert.That(layers[0], Is.SameAs(first));
            Assert.That(layerType.GetField("Holes").GetValue(first), Is.SameAs(holes));
            Assert.That(layerType.GetField("Shapes").GetValue(first), Is.SameAs(shapes));
            Assert.That(layerType.GetField("States").GetValue(first), Is.SameAs(states));
            Assert.That(renderer.sharedMaterial, Is.SameAs(material));
            Assert.That(renderer.enabled, Is.True, "A real projectile still reveals its original skinned wound overlay.");
            AssertPreparedWoundBlock(renderer, holes.Length, 1);
            yield return CapturePistolWoundNearView(target, "wound-prepared-first-contact");

            root.ResetRound();
            PlacePair(4f);
            Assert.That(PreparedWoundLayers(target)[0], Is.SameAs(first));
            Assert.That(renderer.enabled, Is.False);
            Assert.That(root.BloodEffects.ProjectileWoundCountFor(target), Is.Zero);
            AssertPreparedWoundBlock(renderer, holes.Length, 0);
            foreach (Vector4 hole in holes) Assert.That(hole, Is.EqualTo(Vector4.zero));
            double repeatMilliseconds = ApplyFirstPreparedWound(target);
            Assert.That(layers.Count, Is.EqualTo(1));
            Assert.That(layers[0], Is.SameAs(first));
            Assert.That(renderer.enabled, Is.True);
            AssertPreparedWoundBlock(renderer, holes.Length, 1);
            yield return CapturePistolWoundNearView(target, "wound-prepared-reset-contact");

            // Observations for this machine/run, not a timing threshold or a
            // before/after claim: the first body synchronization still does work.
            Directory.CreateDirectory("TestResults");
            File.WriteAllText(Path.Combine("TestResults", "wound-preparation.json"),
                "{\"first_impact_ms\":" + firstMilliseconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                ",\"reset_impact_ms\":" + repeatMilliseconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                ",\"prepared_layers_per_actor\":1,\"layer_and_buffers_reused\":true}");
            LogAssert.NoUnexpectedReceived();
        }

        private IList PreparedWoundLayers(CombatActor actor)
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var injuries = (IDictionary)typeof(CombatBloodEffects).GetField("injuries", flags).GetValue(root.BloodEffects);
            object injury = injuries[actor];
            Assert.That(injury, Is.Not.Null);
            var marks = (CombatDamageMarks)injury.GetType().GetField("Marks").GetValue(injury);
            return (IList)typeof(CombatDamageMarks).GetField("projectileLayers", flags).GetValue(marks);
        }

        private static void AssertPreparedWoundBlock(SkinnedMeshRenderer renderer, int capacity, int wounds)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            Assert.That(block.isEmpty, Is.False, "Even a reset keeps its zeroed native buffers ready.");
            Assert.That(block.GetVectorArray("_BulletHoles").Length, Is.EqualTo(capacity));
            Assert.That(block.GetVectorArray("_BulletShapes").Length, Is.EqualTo(capacity));
            Assert.That(block.GetVectorArray("_BulletStates").Length, Is.EqualTo(capacity));
            Assert.That(block.GetFloat("_BulletHoleCount"), Is.EqualTo(wounds));
        }

        private double ApplyFirstPreparedWound(CombatActor target)
        {
            target.CaptureContactPose();
            Assert.That(target.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Torso, out Vector3 centre, out _, out _), Is.True);
            Vector3 outward = target.transform.forward;
            int impacts = target.ReceivedImpactCount;
            Assert.That(root.Projectiles.TrySpawn(root.Hero, centre + outward * .75f,
                -outward * CombatProjectilePool.MuzzleSpeed, root.Projectiles.SpawnCount + 1), Is.True);
            root.Projectiles.Advance(CombatTestRoot.SimulationStep, root.Hero, target);
            var timer = Stopwatch.StartNew();
            root.Projectiles.ApplyContacts();
            timer.Stop();
            Assert.That(target.ReceivedImpactCount, Is.EqualTo(impacts + 1));
            Assert.That(target.LastImpact.Kind, Is.EqualTo(CombatImpactKind.Projectile));
            Assert.That(root.BloodEffects.ProjectileWoundCountFor(target), Is.EqualTo(1));
            return timer.Elapsed.TotalMilliseconds;
        }
    }
}
