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
    /// Project Lit retains URP's passes/keywords and the
    /// scene-owned warehouse ambient fade. A clone is a snapshot, so
    /// the danger is not that it breaks - it is that the package moves and
    /// the clone quietly does not, leaving the world lit by a stale
    /// variant set. These tests read the live package file off disk, so
    /// the URP upgrade that would cause that fails here first.
    /// </summary>
    public sealed class LitShaderParityTests
    {
        private const string CloneName = "Bar Promenade/Lit";
        private const string StockName = "Universal Render Pipeline/Lit";
        private const string ClonePath =
            "Assets/Resources/Shaders/Lit.shader";
        private const string PackagePath =
            "Packages/com.unity.render-pipelines.universal/Shaders/" +
            "Lit.shader";

        // Plain Lit uses the package's unchanged native vertex stages.
        private static readonly string[] WrappedPasses = Array.Empty<string>();

        private const string FoliageCloneName =
            "Bar Promenade/Lit Foliage";
        private const string FoliageClonePath =
            "Assets/Resources/Shaders/LitFoliage.shader";
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
                .SetName("Test Lit shader");
            yield return new TestCaseData(
                    FoliageCloneName,
                    FoliageClonePath,
                    FoliageWrappedPasses)
                .SetName("Test foliage Lit shader");
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
                // Plain Lit defines stock PBR before its scene-owned ambient
                // wrapper. This additional guarded include changes no variants.
                if (cloneName == CloneName && pass == "ForwardLit")
                    clone[pass].Remove("#include \"Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl\"");
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

                bool wrapped = vertex.Contains("Foliage");
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

            Assert.That(source, Does.Not.Contain("Ps1"),
                "Geometry must retain native clip positions in every pass.");
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

        /// <summary>Wind is shared by all four camera/shadow object-space wrappers.</summary>
        [Test]
        public void FoliageClone_BendsEveryCameraAndShadowPass()
        {
            string source = File.ReadAllText(
                Path.Combine(Application.dataPath, "..", FoliageClonePath));
            Assert.That(CountOccurrences(source, "MountainWindDisplace("), Is.EqualTo(4),
                "Forward, shadow, depth and normal passes must use the same wind.");
            Assert.That(source, Does.Contain("#include \"MountainWindSway.hlsl\""));
        }

        [Test]
        public void GeometryShaders_KeepNativeClipPositions()
        {
            foreach (string name in new[] { "Lit.shader", "LitFoliage.shader", "GroundSurfaceLit.shader",
                         "WarehouseSurface.shader", "CityMountainPhysical.shader", "CityBuildingWindowSlots.shader",
                         "ChestFlashlightGlow.shader", "CombatSurfaceImpact.shader", "CombatWound.shader", "NpcNameplate.shader" })
            {
                string source = File.ReadAllText(Path.Combine(Application.dataPath, "Resources/Shaders", name));
                Assert.That(source, Does.Not.Contain("Ps1"), name);
            }
            Assert.That(File.Exists(Path.Combine(Application.dataPath,
                "Resources/Shaders/Ps1VertexJitter.hlsl")), Is.False);
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
            // back at the package shader, the material silently loses the
            // scene-owned warehouse lighting hook.
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
