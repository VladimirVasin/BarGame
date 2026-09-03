using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The normal/bizarre verdicts, held against the models actually on disk.
    ///
    /// The catalog lives in C# rather than in the manifests, which means it
    /// can silently fall behind the roster — a new design would simply have
    /// no verdict and nothing would say so. This is the test that makes that
    /// impossible: the catalog's key set must equal the set of `design_id`s
    /// found in the character manifests, exactly, in both directions.
    /// </summary>
    public sealed class NpcDesignAppearanceTests
    {
        /// <summary>
        /// Where character manifests live. The three folders are swept because
        /// designs are added to them routinely; the five one-offs are named
        /// because their homes are scattered and a sweep wide enough to
        /// catch them would also catch the park chess PIECES, whose manifest
        /// is character-shaped and is not a character.
        /// </summary>
        private static readonly string[] ManifestFolders =
        {
            "Assets/Pedestrians/Models",
            "Assets/Pedestrians/Staged/Models",
            "Assets/Supermarket/Cashier/Models",
        };

        private static readonly string[] StandaloneManifests =
        {
            "Assets/Bar/Bartender/Models/" +
            "BarBartenderOrdinary3D.json",
            "Assets/Bar/Bartender/Models/BarBartender3D.json",
            "Assets/Vehicles/Drivers/Models/CityBusDriver3D.json",
            "Assets/Cemetery/Raven/Models/CemeteryRaven3D.json",
            "Assets/Stairwell/Cat/Models/StairwellCat3D.json",
        };

        [Test]
        [Category("NPC")]
        public void Catalog_CoversExactlyTheCharactersOnDisk()
        {
            Dictionary<string, string> onDisk = ReadDesignIds();

            Assert.That(
                onDisk,
                Is.Not.Empty,
                "No character manifests were found at all — the sweep " +
                "paths are wrong, and this test would pass vacuously.");

            var catalogued = new HashSet<string>(
                NpcDesignAppearanceCatalog.DesignIds,
                StringComparer.Ordinal);
            var present = new HashSet<string>(
                onDisk.Keys,
                StringComparer.Ordinal);

            var missing = new List<string>(present.Except(catalogued));
            missing.Sort(StringComparer.Ordinal);
            Assert.That(
                missing,
                Is.Empty,
                "These designs ship without a normal/bizarre verdict: " +
                string.Join(
                    ", ",
                    missing.Select(id => $"{id} ({onDisk[id]})")) +
                ". Add them to NpcDesignAppearanceCatalog.");

            var stale = new List<string>(catalogued.Except(present));
            stale.Sort(StringComparer.Ordinal);
            Assert.That(
                stale,
                Is.Empty,
                "The catalog carries verdicts for designs that no longer " +
                "exist: " + string.Join(", ", stale));
        }

        [Test]
        [Category("NPC")]
        public void CashierVariants_KeepTheirDistinctVerdicts()
        {
            Dictionary<string, string> onDisk = ReadDesignIds();
            Assert.That(
                onDisk.ContainsKey("supermarket_cashier_v1"),
                Is.True,
                "The ordinary cashier manifest is missing from the " +
                "cashier model folder.");
            Assert.That(
                onDisk.ContainsKey("watcher_cashier_v1"),
                Is.True,
                "The retained Watcher manifest is missing from the " +
                "cashier model folder.");

            Assert.That(
                NpcDesignAppearanceCatalog.TryGet(
                    "supermarket_cashier_v1",
                    out NpcDesignAppearance ordinary),
                Is.True);
            Assert.That(ordinary, Is.EqualTo(NpcDesignAppearance.Normal));

            Assert.That(
                NpcDesignAppearanceCatalog.TryGet(
                    "watcher_cashier_v1",
                    out NpcDesignAppearance watcher),
                Is.True);
            Assert.That(watcher, Is.EqualTo(NpcDesignAppearance.Bizarre));
        }

        [Test]
        [Category("NPC")]
        public void BartenderVariants_KeepTheirDistinctVerdicts()
        {
            Dictionary<string, string> onDisk = ReadDesignIds();
            Assert.That(onDisk.ContainsKey("bar_bartender_v2"), Is.True);
            Assert.That(
                onDisk.ContainsKey("six_armed_bartender_v1"),
                Is.True);
            Assert.That(
                NpcDesignAppearanceCatalog.TryGet(
                    "bar_bartender_v2",
                    out NpcDesignAppearance ordinary),
                Is.True);
            Assert.That(ordinary, Is.EqualTo(NpcDesignAppearance.Normal));
            Assert.That(
                NpcDesignAppearanceCatalog.TryGet(
                    "six_armed_bartender_v1",
                    out NpcDesignAppearance legacy),
                Is.True);
            Assert.That(legacy, Is.EqualTo(NpcDesignAppearance.Bizarre));
        }

        /// <summary>
        /// Every entry must be one of the two values, and both groups must
        /// be inhabited. A catalog that had drifted to all-normal would
        /// otherwise still pass the completeness test above.
        /// </summary>
        [Test]
        [Category("NPC")]
        public void Catalog_UsesBothVerdicts()
        {
            int bizarre = 0;
            int normal = 0;
            foreach (string designId in NpcDesignAppearanceCatalog.DesignIds)
            {
                Assert.That(
                    NpcDesignAppearanceCatalog.TryGet(
                        designId,
                        out NpcDesignAppearance appearance),
                    Is.True,
                    $"'{designId}' is a key that cannot be read back.");
                if (appearance == NpcDesignAppearance.Bizarre)
                {
                    bizarre++;
                }
                else
                {
                    normal++;
                }
            }

            Assert.That(bizarre, Is.GreaterThan(0));
            Assert.That(normal, Is.GreaterThan(0));
            Assert.That(
                bizarre + normal,
                Is.EqualTo(NpcDesignAppearanceCatalog.Count));
        }

        /// <summary>
        /// The designs whose bodies the generators themselves refuse to
        /// normalize. `ordinary_head` at
        /// `tools/build-city-pedestrian-3d-model.py` exempts exactly these
        /// four from the `6.90-7.75` heads-tall and `2.20-2.65` shoulder
        /// rules, which is the build's own statement that their anatomy is
        /// not human. Anything the build calls abnormal cannot be `normal`
        /// here.
        /// </summary>
        [Test]
        [Category("NPC")]
        public void Catalog_AgreesWithTheGeneratorsOwnAnatomyExemptions()
        {
            string[] exemptFromHumanProportions =
            {
                "lampshade_walker_v1",
                "kettle_hat_walker_v1",
                "long_arm_walker_v1",
                "helmet_lamp_hopper_v1",
            };

            foreach (string designId in exemptFromHumanProportions)
            {
                Assert.That(
                    NpcDesignAppearanceCatalog.IsBizarre(designId),
                    Is.True,
                    $"'{designId}' is exempted from the human proportion " +
                    "rules by the generator, so it cannot be normal here.");
            }
        }

        /// <summary>
        /// Unknown ids answer rather than throw — the hero's own models and
        /// the park chess pieces all reach this path.
        /// </summary>
        [Test]
        [Category("NPC")]
        public void Catalog_DeclinesUnknownIdsWithoutThrowing()
        {
            Assert.That(
                NpcDesignAppearanceCatalog.TryGet("city_chess_set_v1", out _),
                Is.False);
            Assert.That(
                NpcDesignAppearanceCatalog.TryGet(null, out _),
                Is.False);
            Assert.That(
                NpcDesignAppearanceCatalog.TryGet(string.Empty, out _),
                Is.False);
            Assert.That(
                NpcDesignAppearanceCatalog.IsBizarre("nothing_at_all"),
                Is.False);
        }

        /// <summary>design id -> display name, for readable failures.</summary>
        private static Dictionary<string, string> ReadDesignIds()
        {
            var found = new Dictionary<string, string>(StringComparer.Ordinal);
            string root = Directory.GetParent(Application.dataPath).FullName;

            var files = new List<string>();
            foreach (string folder in ManifestFolders)
            {
                string absolute = Path.Combine(root, folder);
                if (Directory.Exists(absolute))
                {
                    files.AddRange(
                        Directory.GetFiles(absolute, "*.json"));
                }
            }

            foreach (string relative in StandaloneManifests)
            {
                string absolute = Path.Combine(root, relative);
                Assert.That(
                    File.Exists(absolute),
                    Is.True,
                    $"Named character manifest '{relative}' is missing; " +
                    "the sweep would silently skip it.");
                files.Add(absolute);
            }

            foreach (string file in files)
            {
                CharacterManifest manifest =
                    JsonUtility.FromJson<CharacterManifest>(
                        File.ReadAllText(file));
                if (manifest == null ||
                    string.IsNullOrEmpty(manifest.design_id))
                {
                    continue;
                }

                found[manifest.design_id] = manifest.display_name ??
                                            Path.GetFileName(file);
            }

            return found;
        }

        [Serializable]
        private sealed class CharacterManifest
        {
            public string design_id;
            public string display_name;
        }
    }
}
