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

        [TestCase("current")]
        [TestCase("raw")]
        [TestCase("asset")]
        [TestCase("missing_source")]
        [TestCase("missing_recording")]
        [TestCase("missing_manifest")]
        [TestCase("malformed")]
        [TestCase("shape")]
        [TestCase("unsafe_source")]
        [TestCase("unsafe_recording")]
        [TestCase("bounded")]
        public void SecondaryMotionDiagnostics_NamesActualFailureWithoutReadingUnsafePaths(string fault)
        {
            const string source = "Assets/Example.cs";
            const string asset = "Assets/Example.fbx";
            const string recording = "Assets/Resources/Player/SecondaryMotionBakes/no-gear/Idle.bytes";
            string savedRaw = new string('A', 64), savedAsset = new string('b', 32);
            string sourcePath = fault == "unsafe_source" ? "Assets/../outside.cs" : source;
            string recordingPath = fault == "unsafe_recording" ? "../outside.bytes" : "no-gear/Idle.bytes";
            string sources = $"{{\"path\":\"{sourcePath}\",\"hash\":\"{savedRaw}\",\"raw\":true}}," +
                $"{{\"path\":\"{asset}\",\"hash\":\"{savedAsset}\",\"raw\":false}}";
            if (fault == "bounded")
            {
                var rows = new string[15];
                for (int i = 0; i < rows.Length; i++)
                    rows[i] = $"{{\"path\":\"Assets/Missing{i}.cs\",\"hash\":\"{savedRaw}\",\"raw\":true}}";
                sources = string.Join(",", rows);
            }
            string json = $"{{\"version\":1,\"sources\":[{sources}],\"recordings\":[\"{recordingPath}\"]}}";
            if (fault == "missing_manifest") json = null;
            else if (fault == "malformed") json = "{broken";
            else if (fault == "shape") json = "{\"version\":2}";
            int unsafeReads = 0;
            Func<string, bool> exists = path =>
            {
                if (path.Contains("..")) unsafeReads++;
                return !(fault == "missing_source" && path == source ||
                    fault == "missing_recording" && path == recording || path.StartsWith("Assets/Missing", StringComparison.Ordinal));
            };
            Func<string, string> rawHash = _ => fault == "raw" ? new string('C', 64) : savedRaw;
            Func<string, string> assetHash = _ => fault == "asset" ? new string('d', 32) : savedAsset;
            Type owner = Type.GetType("BarPromenade.Editor.SecondaryMotionBakeDiagnostics, BarPromenade.Editor", true);
            string report = (string)owner.GetMethod("Inspect").Invoke(null, new object[] { json, exists, rawHash, assetHash });
            Assert.That(unsafeReads, Is.Zero, "Diagnostics must reject traversal before calling any reader.");
            if (fault == "current") { Assert.That(report, Is.Empty); return; }
            Assert.That(report, Does.Contain("-executeMethod BarPromenade.Editor.SecondaryMotionBakeAssetSetup.BakeAll"));
            if (fault == "raw")
                Assert.That(report, Does.Contain(source).And.Contain("raw SHA-256").And.Contain(savedRaw).And.Contain(new string('C', 64)));
            else if (fault == "asset")
                Assert.That(report, Does.Contain(asset).And.Contain("AssetDatabase dependency hash").And.Contain(savedAsset).And.Contain(new string('d', 32)));
            else if (fault == "missing_source") Assert.That(report, Does.Contain(source).And.Contain("<missing file>"));
            else if (fault == "missing_recording") Assert.That(report, Does.Contain(recording).And.Contain("Missing recording"));
            else if (fault == "bounded")
            {
                Assert.That(report, Does.Contain("15 issue(s), showing 12").And.Contain("Missing11.cs"));
                Assert.That(report, Does.Not.Contain("Missing12.cs"));
            }
            else if (fault.StartsWith("unsafe_", StringComparison.Ordinal)) Assert.That(report, Does.Contain("Unsafe"));
            else Assert.That(report.ToLowerInvariant(), Does.Contain(fault == "missing_manifest" ? "missing provenance" : "malformed"));
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

        [TestCase("current")]
        [TestCase("importer_unreadable")]
        [TestCase("missing_collision")]
        [TestCase("unreadable_collision")]
        [TestCase("empty_collision")]
        [TestCase("unreadable_decoration")]
        public void StairwellLabyrinth_RequiresPlayerReadableCollisionWithoutRepairingAssets(string fault)
        {
            Type owner = Type.GetType("BarPromenade.Editor.StairwellLabyrinthAssetSetup, BarPromenade.Editor", true);
            MethodInfo validate = owner.GetMethod("ValidateModel");
            Assert.That(validate, Is.Not.Null);
            const string manifest = "{\"parts\":[{\"name\":\"Floor\",\"role\":\"Collision\"}," +
                "{\"name\":\"Shell\",\"role\":\"ShellCollision\"},{\"name\":\"Rail\",\"role\":\"DecorCollision\"}," +
                "{\"name\":\"Decoration\",\"role\":\"Decor\"}]}";
            var model = new GameObject("Read-only labyrinth collision fixture");
            var meshes = new Mesh[4];
            try
            {
                string[] names = { "Floor", "Shell", "Rail", "Decoration" };
                for (int i = 0; i < names.Length; i++)
                {
                    var part = new GameObject(names[i]);
                    part.transform.SetParent(model.transform, false);
                    meshes[i] = new Mesh { name = names[i] };
                    if (fault != "empty_collision" || i != 0)
                    {
                        meshes[i].vertices = new[] { Vector3.zero, Vector3.right, Vector3.forward };
                        meshes[i].triangles = new[] { 0, 1, 2 };
                    }
                    part.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                    if (i == 0 && fault == "missing_collision") Object.DestroyImmediate(part);
                }
                if (fault == "unreadable_collision") meshes[0].UploadMeshData(true);
                if (fault == "unreadable_decoration") meshes[3].UploadMeshData(true);
                bool importerReadable = fault != "importer_unreadable";
                bool accepted = fault == "current" || fault == "unreadable_decoration";
                if (accepted) Assert.DoesNotThrow(() => validate.Invoke(null, new object[] { model, manifest, importerReadable }));
                else
                {
                    TargetInvocationException failure = Assert.Throws<TargetInvocationException>(
                        () => validate.Invoke(null, new object[] { model, manifest, importerReadable }));
                    Assert.That(failure.InnerException, Is.TypeOf<InvalidOperationException>());
                    Assert.That(failure.InnerException.Message, Does.Contain("Stairwell labyrinth"));
                }
                Assert.That(meshes[0].isReadable, Is.EqualTo(fault != "unreadable_collision"), "Validation must not repair unreadable meshes.");
                Assert.That(meshes[3].isReadable, Is.EqualTo(fault != "unreadable_decoration"), "Non-collision meshes remain untouched.");
            }
            finally
            {
                Object.DestroyImmediate(model);
                foreach (Mesh mesh in meshes) if (mesh != null) Object.DestroyImmediate(mesh);
            }
        }

        [TestCase("current")]
        [TestCase("importer_unreadable")]
        [TestCase("missing_importer")]
        [TestCase("missing_meshes")]
        [TestCase("unreadable_mesh")]
        [TestCase("empty_mesh")]
        [TestCase("bounded")]
        public void RuntimeMeshReadability_RejectsPlayerMissingDataWithBoundedReadOnlyDiagnostics(string fault)
        {
            Type owner = Type.GetType("BarPromenade.Editor.RuntimeMeshReadabilityValidation, BarPromenade.Editor", true);
            MethodInfo inspect = owner.GetMethod("InspectImportedModel");
            MethodInfo summarize = owner.GetMethod("SummarizeFailures");
            Assert.That(inspect, Is.Not.Null);
            Assert.That(summarize, Is.Not.Null);
            var mesh = new Mesh { name = "Runtime collision source" };
            try
            {
                if (fault != "empty_mesh")
                {
                    mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.forward };
                    mesh.triangles = new[] { 0, 1, 2 };
                }
                if (fault == "unreadable_mesh") mesh.UploadMeshData(true);
                bool readable = mesh.isReadable;
                int vertices = mesh.vertexCount;
                string report = (string)inspect.Invoke(null, new object[]
                {
                    "Assets/Required.fbx", "Audited runtime consumer", "NamedImporter.Reimport",
                    fault != "missing_importer", fault != "importer_unreadable" && fault != "bounded",
                    fault == "missing_meshes" ? Array.Empty<Mesh>() : new[] { mesh }
                });
                if (fault == "current") Assert.That(report, Is.Empty);
                else
                {
                    Assert.That(report, Does.Contain("Assets/Required.fbx").And.Contain("Audited runtime consumer")
                        .And.Contain("NamedImporter.Reimport"));
                    if (fault == "importer_unreadable" || fault == "bounded")
                    {
                        Assert.That(readable, Is.True, "Editor-accessible mesh must not excuse a disabled importer flag.");
                        Assert.That(report, Does.Contain("ModelImporter Read/Write is disabled"));
                    }
                }
                if (fault == "bounded")
                {
                    var failures = new string[20];
                    for (int i = 0; i < failures.Length; i++) failures[i] = "Source" + i + ": " + report;
                    string bounded = (string)summarize.Invoke(null, new object[] { failures });
                    Assert.That(bounded, Does.Contain("20 source(s) failed, showing 16").And.Contain("Source15:"));
                    Assert.That(bounded, Does.Not.Contain("Source16:").And.Not.Contain("Source19:"));
                }
                else if (fault == "current")
                    Assert.That(summarize.Invoke(null, new object[] { new[] { report } }), Is.EqualTo(string.Empty));
                Assert.That(mesh.isReadable, Is.EqualTo(readable));
                Assert.That(mesh.vertexCount, Is.EqualTo(vertices));
            }
            finally { Object.DestroyImmediate(mesh); }
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
