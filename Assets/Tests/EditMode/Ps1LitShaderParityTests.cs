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

        private const string FoliageCloneName =
            "Bar Promenade/PS1 Lit Foliage";
        private const string FoliageClonePath =
            "Assets/Resources/Shaders/Ps1LitFoliage.shader";
        private const string FoliageMaterialPath =
            "Assets/Resources/Materials/MountainFoliageLit.mat";

        /// <summary>
        /// The foliage clone wraps a fourth pass. Wind is an object-space
        /// displacement, identical under every projection, so ShadowCaster
        /// MUST carry it — a crown whose shadow stands still while the crown
        /// sways is the exact bug that pass exists to avoid, and on this road
        /// it is the most visible one there is, because the car's headlights
        /// throw those shadows straight across the asphalt.
        /// </summary>
        private static readonly string[] FoliageWrappedPasses =
        {
            "ForwardLit",
            "ShadowCaster",
            "DepthOnly",
            "DepthNormals"
        };

        /// <summary>
        /// Both clones, so a URP bump fails here for either of them rather
        /// than quietly stranding one on a stale variant set.
        /// </summary>
        private static IEnumerable<TestCaseData> Clones()
        {
            yield return new TestCaseData(
                    CloneName,
                    ClonePath,
                    WrappedPasses)
                .SetName("Ps1Lit");
            yield return new TestCaseData(
                    FoliageCloneName,
                    FoliageClonePath,
                    FoliageWrappedPasses)
                .SetName("Ps1LitFoliage");
        }

        [TestCaseSource(nameof(Clones))]
        public void Clone_CompilesAndKeepsEveryPass(
            string cloneName,
            string clonePath,
            string[] wrappedPasses)
        {
            Shader clone = Shader.Find(cloneName);
            Shader stock = Shader.Find(StockName);
            Assert.That(clone, Is.Not.Null, $"'{cloneName}' is missing.");
            Assert.That(stock, Is.Not.Null, "URP Lit is missing.");

            if (ShaderUtil.ShaderHasError(clone))
            {
                var report = new StringBuilder(
                    $"'{cloneName}' does not compile:");
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
                $"'{cloneName}' is not supported on this platform.");
            Assert.That(
                clone.passCount,
                Is.EqualTo(stock.passCount),
                "A dropped pass means geometry that stops casting " +
                "shadows or stops writing depth.");
        }

        [TestCaseSource(nameof(Clones))]
        public void Clone_CarriesTheSameKeywordSpace(
            string cloneName,
            string clonePath,
            string[] wrappedPasses)
        {
            Shader clone = Shader.Find(cloneName);
            Shader stock = Shader.Find(StockName);

            CollectionAssert.AreEquivalent(
                stock.keywordSpace.keywordNames,
                clone.keywordSpace.keywordNames,
                "The clone's keyword space drifted from URP Lit's. A " +
                "missing keyword compiles a variant that never runs - " +
                "this project already lost the water's lamp glints to " +
                "exactly that.");
        }

        [TestCaseSource(nameof(Clones))]
        public void Clone_CarriesEveryPackagePragmaVerbatim(
            string cloneName,
            string clonePath,
            string[] wrappedPasses)
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
                        Path.Combine(Application.dataPath, "..", clonePath)));

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

        [TestCaseSource(nameof(Clones))]
        public void Clone_WrapsExactlyItsDeclaredPasses(
            string cloneName,
            string clonePath,
            string[] wrappedPasses)
        {
            string source = File.ReadAllText(
                Path.Combine(Application.dataPath, "..", clonePath));
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

                bool wrapped = vertex.Contains("Ps1");
                bool shouldWrap = wrappedPasses.Contains(pass.Key);
                Assert.That(
                    wrapped,
                    Is.EqualTo(shouldWrap),
                    $"Pass '{pass.Key}' of '{cloneName}' is " +
                    (wrapped ? "wrapped" : "unwrapped") +
                    " and should not be. The camera passes must agree " +
                    "to the bit or the depth prepass stops matching the " +
                    "forward pass; Meta must never be touched at all.");
            }

            Assert.That(
                source,
                Does.Contain("#include \"Ps1VertexJitter.hlsl\""),
                "The snap helper must be shared, never inlined per pass.");
        }

        [TestCaseSource(nameof(Clones))]
        public void Clone_KeepsThePropertiesTheWorldDrivesThrough(
            string cloneName,
            string clonePath,
            string[] wrappedPasses)
        {
            Shader clone = Shader.Find(cloneName);

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
                    $"'{cloneName}' no longer exposes '{property}'.");
            }
        }

        /// <summary>
        /// The one contract the parameterized shape above cannot express,
        /// and the reason the foliage clone exists as a separate file at
        /// all: wind and snap are not the same kind of thing.
        ///
        /// The snap works in PROJECTION space, so the shadow map's own
        /// projection can never agree with the camera's and ShadowCaster
        /// must not snap. The wind is a displacement in OBJECT space,
        /// identical under every projection, so ShadowCaster must carry it
        /// or a swaying crown casts a still shadow — straight across the
        /// road the headlights are lighting.
        /// </summary>
        [Test]
        public void FoliageClone_BendsItsShadowButNeverSnapsIt()
        {
            string source = File.ReadAllText(
                Path.Combine(
                    Application.dataPath,
                    "..",
                    FoliageClonePath));

            Assert.That(
                CountOccurrences(source, "Ps1SnapClipPosition("),
                Is.EqualTo(3),
                "Only the three camera passes may snap. A shadow map is a " +
                "different projection at a different resolution, so no " +
                "snap there can agree with the camera's.");
            Assert.That(
                CountOccurrences(source, "MountainWindDisplace("),
                Is.EqualTo(4),
                "Every wrapped pass must bend, ShadowCaster included: a " +
                "crown whose shadow does not sway is the bug.");
            Assert.That(
                source,
                Does.Contain("#include \"MountainWindSway.hlsl\""),
                "The wind helper must be shared so all four passes " +
                "displace by the identical offset.");
        }

        private static int CountOccurrences(string source, string token)
        {
            int count = 0;
            int index = source.IndexOf(token, StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                index = source.IndexOf(
                    token,
                    index + token.Length,
                    StringComparison.Ordinal);
            }

            return count;
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
                    "Assets/Player3D/Materials/Player3DLit.mat",
                    FoliageMaterialPath
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

                // The conifer crowns are the one family that wants a
                // different vertex stage, so they carry the foliage clone.
                // Everything else must be on the plain one.
                string expected =
                    path == FoliageMaterialPath
                        ? FoliageCloneName
                        : CloneName;
                Assert.That(
                    material.shader.name,
                    Is.EqualTo(expected),
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
