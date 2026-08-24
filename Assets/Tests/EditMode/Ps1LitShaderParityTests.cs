using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The PS1 Lit shader is a verbatim clone of URP's Lit with a vertex
    /// snap wrapped around three of its passes. A clone is a snapshot, so
    /// the danger is not that it breaks - it is that the package moves and
    /// the clone quietly does not, leaving the world lit by a stale
    /// variant set. These tests read the live package file off disk, so
    /// the URP upgrade that would cause that fails here first.
    /// </summary>
    public sealed class Ps1LitShaderParityTests
    {
        private const string CloneName = "Bar Promenade/PS1 Lit";
        private const string StockName = "Universal Render Pipeline/Lit";
        private const string ClonePath =
            "Assets/Resources/Shaders/Ps1Lit.shader";
        private const string PackagePath =
            "Packages/com.unity.render-pipelines.universal/Shaders/" +
            "Lit.shader";

        // The three passes that snap, and so are the only ones allowed to
        // differ from the package in their vertex entry point.
        private static readonly string[] WrappedPasses =
        {
            "ForwardLit",
            "DepthOnly",
            "DepthNormals"
        };

        [Test]
        public void Clone_CompilesAndKeepsEveryPass()
        {
            Shader clone = Shader.Find(CloneName);
            Shader stock = Shader.Find(StockName);
            Assert.That(clone, Is.Not.Null, "PS1 Lit is missing.");
            Assert.That(stock, Is.Not.Null, "URP Lit is missing.");

            if (ShaderUtil.ShaderHasError(clone))
            {
                var report = new StringBuilder(
                    "The PS1 Lit shader does not compile:");
                foreach (var message in
                         ShaderUtil.GetShaderMessages(clone))
                {
                    report.Append("\n  ")
                        .Append(message.file)
                        .Append('(')
                        .Append(message.line)
                        .Append("): ")
                        .Append(message.message);
                }

                Assert.Fail(report.ToString());
            }

            Assert.That(
                clone.isSupported,
                Is.True,
                "The PS1 Lit shader is not supported on this platform.");
            Assert.That(
                clone.passCount,
                Is.EqualTo(stock.passCount),
                "A dropped pass means geometry that stops casting " +
                "shadows or stops writing depth.");
        }

        [Test]
        public void Clone_CarriesTheSameKeywordSpace()
        {
            Shader clone = Shader.Find(CloneName);
            Shader stock = Shader.Find(StockName);

            CollectionAssert.AreEquivalent(
                stock.keywordSpace.keywordNames,
                clone.keywordSpace.keywordNames,
                "The clone's keyword space drifted from URP Lit's. A " +
                "missing keyword compiles a variant that never runs - " +
                "this project already lost the water's lamp glints to " +
                "exactly that.");
        }

        [Test]
        public void Clone_CarriesEveryPackagePragmaVerbatim()
        {
            string packageFile = Path.GetFullPath(PackagePath);
            Assert.That(
                File.Exists(packageFile),
                Is.True,
                $"Cannot read the package shader at '{packageFile}'.");

            Dictionary<string, List<string>> stock =
                ReadDirectivesByPass(File.ReadAllText(packageFile));
            Dictionary<string, List<string>> clone =
                ReadDirectivesByPass(
                    File.ReadAllText(
                        Path.Combine(Application.dataPath, "..", ClonePath)));

            CollectionAssert.AreEquivalent(
                stock.Keys,
                clone.Keys,
                "The clone and the package disagree about which passes " +
                "exist.");

            foreach (string pass in stock.Keys)
            {
                CollectionAssert.AreEquivalent(
                    stock[pass],
                    clone[pass],
                    $"Pass '{pass}' drifted from the package shader. " +
                    "Re-copy it from " + PackagePath + " and re-apply " +
                    "only the vertex wrapper.");
            }
        }

        [Test]
        public void Clone_SnapsExactlyTheThreeCameraPasses()
        {
            string source = File.ReadAllText(
                Path.Combine(Application.dataPath, "..", ClonePath));
            Dictionary<string, List<string>> vertexLines =
                ReadDirectivesByPass(source, keepVertex: true);

            foreach (KeyValuePair<string, List<string>> pass in vertexLines)
            {
                string vertex = pass.Value.FirstOrDefault(
                    line => line.StartsWith("#pragma vertex"));
                if (vertex == null)
                {
                    continue;
                }

                bool snapped = vertex.Contains("Ps1");
                bool shouldSnap = WrappedPasses.Contains(pass.Key);
                Assert.That(
                    snapped,
                    Is.EqualTo(shouldSnap),
                    $"Pass '{pass.Key}' is " +
                    (snapped ? "snapped" : "unsnapped") +
                    " and should not be. The camera passes must agree " +
                    "to the bit or the depth prepass stops matching the " +
                    "forward pass; ShadowCaster and Meta must never snap.");
            }

            Assert.That(
                source,
                Does.Contain("#include \"Ps1VertexJitter.hlsl\""),
                "The snap helper must be shared, never inlined per pass.");
        }

        [Test]
        public void Clone_KeepsThePropertiesTheWorldDrivesThrough()
        {
            Shader clone = Shader.Find(CloneName);

            // Every runtime primitive and appearance system writes these
            // by name through property blocks; a rename would break far
            // more than lighting.
            foreach (string property in new[]
                     {
                         "_BaseColor",
                         "_Color",
                         "_BaseMap",
                         "_Smoothness",
                         "_Metallic"
                     })
            {
                Assert.That(
                    clone.FindPropertyIndex(property),
                    Is.GreaterThanOrEqualTo(0),
                    $"PS1 Lit no longer exposes '{property}'.");
            }
        }

        [Test]
        public void EveryMigratedMaterial_UsesTheCloneNotStockLit()
        {
            // The bus and the hero materials are regenerated by editor
            // asset-setup scripts. If one of those scripts is ever pointed
            // back at the package shader, the material silently stops
            // jittering and nothing else notices.
            string[] paths = new[]
                {
                    "Assets/Resources/Materials/RuntimePrimitiveLit.mat",
                    "Assets/Player3D/Materials/Player3DLit.mat"
                }
                .Concat(
                    AssetDatabase
                        .FindAssets("t:Material", new[] { "Assets/Vehicles" })
                        .Select(AssetDatabase.GUIDToAssetPath)
                        .Where(path => path.Contains("CityBus")))
                .ToArray();

            Assert.That(
                paths.Length,
                Is.GreaterThanOrEqualTo(15),
                "Expected the world, hero and bus materials.");

            foreach (string path in paths)
            {
                Material material =
                    AssetDatabase.LoadAssetAtPath<Material>(path);
                Assert.That(material, Is.Not.Null, path);
                if (material.shader.name == "Bar Promenade/City Bus Glass Rain")
                {
                    continue;
                }

                Assert.That(
                    material.shader.name,
                    Is.EqualTo(CloneName),
                    $"'{path}' fell back to {material.shader.name}.");
            }
        }

        /// <summary>
        /// Splits a ShaderLab file into passes and collects the lines that
        /// decide which variants get compiled. The vertex pragma is
        /// dropped by default: it is the one line the clone is allowed to
        /// change.
        /// </summary>
        private static Dictionary<string, List<string>> ReadDirectivesByPass(
            string source,
            bool keepVertex = false)
        {
            var passes = new Dictionary<string, List<string>>(
                StringComparer.Ordinal);
            string current = null;
            foreach (string rawLine in source.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("Name \"", StringComparison.Ordinal))
                {
                    current = line.Substring(6).TrimEnd('"');
                    passes[current] = new List<string>();
                    continue;
                }

                if (current == null)
                {
                    continue;
                }

                bool directive =
                    line.StartsWith("#pragma ", StringComparison.Ordinal) ||
                    line.StartsWith(
                        "#include_with_pragmas",
                        StringComparison.Ordinal) ||
                    line.StartsWith(
                        "#include \"Packages/",
                        StringComparison.Ordinal);
                if (!directive)
                {
                    continue;
                }

                if (!keepVertex &&
                    line.StartsWith("#pragma vertex", StringComparison.Ordinal))
                {
                    continue;
                }

                passes[current].Add(line);
            }

            return passes;
        }
    }
}
