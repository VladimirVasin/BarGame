using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.EditMode
{
    public sealed class PlayerBuildAssetValidationTests
    {
        private static Type Validator => Type.GetType(
            "BarPromenade.Editor.PlayerBuildAssetValidation, BarPromenade.Editor", true);

        [Test]
        public void CurrentProject_PassesReadOnlyGate()
        {
            try
            {
                Validator.GetMethod("ValidateOrThrow").Invoke(null, null);
            }
            catch (TargetInvocationException failure)
            {
                Assert.Fail((failure.InnerException ?? failure).ToString());
            }
        }

        [TestCase("old", "new", true)]
        [TestCase("", "new", true)]
        [TestCase("same", "same", false)]
        [TestCase("", "", true)]
        public void DependencyGate_RejectsMissingOrStaleStamps(string actual, string expected, bool fails)
        {
            MethodInfo method = Validator.GetMethod("ValidateStamp");
            Assert.That(method, Is.Not.Null);
            if (fails)
            {
                TargetInvocationException failure = Assert.Throws<TargetInvocationException>(
                    () => method.Invoke(null, new object[] { "Assets/example.prefab", actual, expected }));
                Assert.That(failure.InnerException, Is.TypeOf<InvalidOperationException>());
                Assert.That(failure.InnerException.Message, Does.Contain("Assets/example.prefab"));
            }
            else
            {
                Assert.DoesNotThrow(() => method.Invoke(null,
                    new object[] { "Assets/example.prefab", actual, expected }));
            }
        }

        [Test]
        public void FailureReport_ContinuesAfterFirstFailureAndNamesEachRepair()
        {
            int visited = 0;
            Action[] checks =
            {
                () => { visited++; throw new InvalidOperationException("missing source"); },
                () => { visited++; },
                () => { visited++; throw new InvalidOperationException("stale prefab"); }
            };
            string[] labels = { "A / RepairA", "B / RepairB", "C / RepairC" };
            var failures = (string[])Validator.GetMethod("CollectFailures").Invoke(
                null, new object[] { labels, checks });
            Assert.That(visited, Is.EqualTo(3));
            Assert.That(failures.Length, Is.EqualTo(2));
            Assert.That(failures[0], Does.Contain("RepairA").And.Contain("missing source"));
            Assert.That(failures[1], Does.Contain("RepairC").And.Contain("stale prefab"));
        }

        [Test]
        public void Gate_IsInstalledForEveryPlayerBuildAndUsesExistingHeroSignature()
        {
            Assert.That(Array.Exists(Validator.GetInterfaces(),
                type => type.FullName == "UnityEditor.Build.IPreprocessBuildWithReport"), Is.True);
            Type setup = Type.GetType("BarPromenade.Editor.Player3DV2AssetSetup, BarPromenade.Editor", true);
            MethodInfo signature = setup.GetMethod("CalculateBuildSignature",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(signature, Is.Not.Null);
            Assert.That(signature.ReturnType, Is.EqualTo(typeof(string)));
            Assert.That(signature.GetParameters(), Is.Empty);
        }

        [Test]
        public void CurrentWeaponCarry_ReadOnlyValidationPreservesPublishedBindings()
        {
            const string path = "Assets/Resources/CombatWeaponCarry/Bindings.asset";
            Type owner = Type.GetType("BarPromenade.Editor.CombatWeaponCarryAssetSetup, BarPromenade.Editor", true);
            CombatWeaponCarryBindings bindings = Resources.Load<CombatWeaponCarryBindings>("CombatWeaponCarry/Bindings");
            Assert.That(bindings, Is.Not.Null);
            byte[] bytes = File.ReadAllBytes(path);
            string serialized = JsonUtility.ToJson(bindings);
            try { owner.GetMethod("ValidateOrThrow").Invoke(null, null); }
            catch (TargetInvocationException failure) { Assert.Fail((failure.InnerException ?? failure).ToString()); }
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
            Assert.That(JsonUtility.ToJson(bindings), Is.EqualTo(serialized));
        }

        [TestCase("current")]
        [TestCase("missing_model")]
        [TestCase("missing_part")]
        [TestCase("translated")]
        [TestCase("winding")]
        [TestCase("duplicate_face")]
        [TestCase("unreadable")]
        [TestCase("unexpected_part")]
        public void StreetProfile_RejectsMissingOrCorruptRequiredTopology(string fault)
        {
            Type owner = Type.GetType("BarPromenade.Editor.CityStreetProfileAssetValidation, BarPromenade.Editor", true);
            MethodInfo validate = owner.GetMethod("ValidateModel");
            GameObject template = Resources.Load<GameObject>("City/StreetProfile/CityStreetProfile3D");
            Assert.That(template, Is.Not.Null);
            GameObject model = null;
            Mesh ownedMesh = null;
            try
            {
                if (fault != "missing_model") model = Object.Instantiate(template);
                if (model != null)
                {
                    MeshFilter patch = Array.Find(model.GetComponentsInChildren<MeshFilter>(true), part => part.name == "SurfacePatch");
                    MeshFilter strip = Array.Find(model.GetComponentsInChildren<MeshFilter>(true), part => part.name == "BoundaryStrip");
                    Assert.That(patch, Is.Not.Null);
                    Assert.That(strip, Is.Not.Null);
                    if (fault == "missing_part") Object.DestroyImmediate(patch.gameObject);
                    else if (fault == "translated") patch.transform.position += Vector3.right * .01f;
                    else if (fault == "unexpected_part") patch.name = "UnknownPatch";
                    else if (fault == "winding" || fault == "unreadable" || fault == "duplicate_face")
                    {
                        MeshFilter target = fault == "duplicate_face" ? strip : patch;
                        ownedMesh = Object.Instantiate(target.sharedMesh);
                        target.sharedMesh = ownedMesh;
                        if (fault == "unreadable") ownedMesh.UploadMeshData(true);
                        else
                        {
                            int[] indices = ownedMesh.triangles;
                            if (fault == "winding") (indices[1], indices[2]) = (indices[2], indices[1]);
                            else { indices[3] = indices[0]; indices[4] = indices[1]; indices[5] = indices[2]; }
                            ownedMesh.triangles = indices;
                        }
                    }
                }
                if (fault == "current") Assert.DoesNotThrow(() => validate.Invoke(null, new object[] { model }));
                else
                {
                    TargetInvocationException failure = Assert.Throws<TargetInvocationException>(() => validate.Invoke(null, new object[] { model }));
                    Assert.That(failure.InnerException, Is.TypeOf<InvalidOperationException>());
                }
                Assert.DoesNotThrow(() => validate.Invoke(null, new object[] { template }),
                    "Negative fixtures must leave the published resource untouched.");
            }
            finally
            {
                if (model != null) Object.DestroyImmediate(model);
                if (ownedMesh != null) Object.DestroyImmediate(ownedMesh);
            }
        }

        [TestCase("current")]
        [TestCase("missing")]
        [TestCase("position")]
        [TestCase("rotation")]
        [TestCase("transfer")]
        [TestCase("duplicate")]
        [TestCase("nonfinite")]
        public void WeaponCarry_ValidatesMeasuredContactsWithoutRepairingStoredBindings(string fault)
        {
            Type owner = Type.GetType("BarPromenade.Editor.CombatWeaponCarryAssetSetup, BarPromenade.Editor", true);
            MethodInfo validate = owner.GetMethod("ValidateBindings");
            CombatWeaponCarryBindings bindings = fault == "missing" ? null : ScriptableObject.CreateInstance<CombatWeaponCarryBindings>();
            try
            {
                if (bindings != null)
                {
                    bindings.Slots = CarrySlots();
                    if (fault == "position") bindings.Slots[1].BoneLocalPosition = Vector3.forward * .01f;
                    else if (fault == "rotation") bindings.Slots[1].BoneLocalRotation = Quaternion.Euler(0f, 1f, 0f);
                    else if (fault == "transfer") bindings.Slots[2].CatchHandLocalPosition = Vector3.up * .01f;
                    else if (fault == "duplicate") bindings.Slots[1].Weapon = bindings.Slots[0].Weapon;
                    else if (fault == "nonfinite") bindings.Slots[1].BoneLocalPosition = new Vector3(float.NaN, 0f, 0f);
                }
                string before = bindings != null ? JsonUtility.ToJson(bindings) : null;
                Action check = () => validate.Invoke(null, new object[] { bindings, CarrySlots() });
                if (fault == "current") Assert.DoesNotThrow(() => check());
                else
                {
                    TargetInvocationException failure = Assert.Throws<TargetInvocationException>(() => check());
                    Assert.That(failure.InnerException, Is.TypeOf<InvalidOperationException>());
                    Assert.That(failure.InnerException.Message, Does.Contain("BuildOrThrow"));
                }
                if (bindings != null) Assert.That(JsonUtility.ToJson(bindings), Is.EqualTo(before));
            }
            finally { if (bindings != null) Object.DestroyImmediate(bindings); }
        }

        private static CombatWeaponCarryBindings.Slot[] CarrySlots() => new[]
        {
            new CombatWeaponCarryBindings.Slot { Weapon = CombatWeaponId.Crowbar, BoneLocalRotation = Quaternion.identity },
            new CombatWeaponCarryBindings.Slot { Weapon = CombatWeaponId.Pistol, BoneLocalRotation = Quaternion.identity },
            new CombatWeaponCarryBindings.Slot { Weapon = CombatWeaponId.Shotgun, BoneLocalRotation = Quaternion.identity }
        };
    }
}
