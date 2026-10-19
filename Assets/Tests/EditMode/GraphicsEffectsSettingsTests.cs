using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class GraphicsEffectsSettingsTests
    {
        private static readonly string[] Keys =
        {
            "graphics.style",
            "graphics.clean_aa",
            "graphics.dof",
            "graphics.intoxication_fx",
            "graphics.dither",
            "graphics.scanlines",
            "graphics.aspect_4_3",
            "graphics.vertex_jitter",
            "graphics.begotten"
        };

        private bool[] savedExists;
        private int[] savedValues;

        [SetUp]
        public void SetUp()
        {
            savedExists = new bool[Keys.Length];
            savedValues = new int[Keys.Length];
            for (int i = 0; i < Keys.Length; i++)
            {
                savedExists[i] = PlayerPrefs.HasKey(Keys[i]);
                if (savedExists[i])
                {
                    savedValues[i] = PlayerPrefs.GetInt(Keys[i]);
                }

                PlayerPrefs.DeleteKey(Keys[i]);
            }

            GraphicsEffectsSettings.ResetLoadedStateForTests();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < Keys.Length; i++)
            {
                if (savedExists[i])
                {
                    PlayerPrefs.SetInt(Keys[i], savedValues[i]);
                }
                else
                {
                    PlayerPrefs.DeleteKey(Keys[i]);
                }
            }

            PlayerPrefs.Save();
            GraphicsEffectsSettings.ResetLoadedStateForTests();
        }

        [Test]
        public void Defaults_AllEffectsEnabledWithoutPrefs()
        {
            Assert.That(GraphicsEffectsSettings.Style, Is.EqualTo(GraphicsStyle.Ps1));
            Assert.That(PlayerPrefs.HasKey("graphics.style"), Is.False,
                "Reading a missing style must preserve existing preferences without writing migration data.");
            Assert.That(System.Enum.GetValues(typeof(GraphicsStyle)), Has.Length.EqualTo(2));
            Assert.That((int)GraphicsStyle.CleanLowPoly, Is.EqualTo(2),
                "Existing Clean preferences keep their persisted id.");
            Assert.That(GraphicsEffectsSettings.CleanAntialiasingEnabled, Is.True);
            Assert.That(GraphicsEffectsSettings.IsCleanAntialiasingApplicable, Is.False);
            Assert.That(
                GraphicsEffectsSettings.DepthOfFieldEnabled,
                Is.True);
            Assert.That(
                GraphicsEffectsSettings.IntoxicationLensFxEnabled,
                Is.True);
            Assert.That(
                GraphicsEffectsSettings.DitherEnabled,
                Is.True);
            Assert.That(
                GraphicsEffectsSettings.ScanlinesEnabled,
                Is.True);
            Assert.That(
                GraphicsEffectsSettings.AspectRatio43Enabled,
                Is.False,
                "The 4:3 pillarbox is opt-in: the game is authored " +
                "widescreen.");
            Assert.That(
                GraphicsEffectsSettings.VertexJitterEnabled,
                Is.False,
                "Vertex jitter moves every silhouette in the game, so " +
                "it is offered rather than imposed.");
            Assert.That(
                GraphicsEffectsSettings.BegottenModeEnabled,
                Is.False,
                "The film print replaces the whole picture and its " +
                "cadence, so it is the third opt-in.");
            Assert.That(GraphicsEffectsSettings.Version, Is.Zero);
        }

        [Test]
        public void Setter_BumpsVersionOnlyOnActualChange()
        {
            int initial = GraphicsEffectsSettings.Version;

            GraphicsEffectsSettings.DitherEnabled = true;
            Assert.That(
                GraphicsEffectsSettings.Version,
                Is.EqualTo(initial));

            GraphicsEffectsSettings.DitherEnabled = false;
            Assert.That(
                GraphicsEffectsSettings.Version,
                Is.EqualTo(initial + 1));

            GraphicsEffectsSettings.Style = GraphicsStyle.Ps1;
            Assert.That(GraphicsEffectsSettings.Version, Is.EqualTo(initial + 1));
            GraphicsEffectsSettings.Style = GraphicsStyle.CleanLowPoly;
            Assert.That(GraphicsEffectsSettings.Version, Is.EqualTo(initial + 2));
            GraphicsEffectsSettings.Style = GraphicsStyle.CleanLowPoly;
            Assert.That(GraphicsEffectsSettings.Version, Is.EqualTo(initial + 2));
            Assert.That(
                GraphicsEffectsSettings.DitherEnabled,
                Is.False);

            GraphicsEffectsSettings.DitherEnabled = false;
            Assert.That(
                GraphicsEffectsSettings.Version,
                Is.EqualTo(initial + 2));
            GraphicsEffectsSettings.CleanAntialiasingEnabled = true;
            Assert.That(GraphicsEffectsSettings.Version, Is.EqualTo(initial + 2));
            GraphicsEffectsSettings.CleanAntialiasingEnabled = false;
            Assert.That(GraphicsEffectsSettings.Version, Is.EqualTo(initial + 3));
            GraphicsEffectsSettings.CleanAntialiasingEnabled = false;
            Assert.That(GraphicsEffectsSettings.Version, Is.EqualTo(initial + 3));
        }

        [Test]
        public void Setter_PersistsAcrossReload()
        {
            GraphicsEffectsSettings.ScanlinesEnabled = false;
            GraphicsEffectsSettings.DepthOfFieldEnabled = false;
            GraphicsEffectsSettings.AspectRatio43Enabled = true;
            GraphicsEffectsSettings.Style = GraphicsStyle.CleanLowPoly;
            GraphicsEffectsSettings.CleanAntialiasingEnabled = false;

            GraphicsEffectsSettings.ResetLoadedStateForTests();

            Assert.That(
                GraphicsEffectsSettings.ScanlinesEnabled,
                Is.False);
            Assert.That(
                GraphicsEffectsSettings.DepthOfFieldEnabled,
                Is.False);
            Assert.That(
                GraphicsEffectsSettings.DitherEnabled,
                Is.True);
            Assert.That(
                GraphicsEffectsSettings.AspectRatio43Enabled,
                Is.True);
            Assert.That(GraphicsEffectsSettings.Style, Is.EqualTo(GraphicsStyle.CleanLowPoly));
            Assert.That(GraphicsEffectsSettings.CleanAntialiasingEnabled, Is.False);
        }

        [TestCase(-1)]
        [TestCase(1)]
        [TestCase(3)]
        [TestCase(int.MaxValue)]
        public void UnknownStyle_LoadsPs1WithoutChangingExistingFlags(int storedStyle)
        {
            PlayerPrefs.SetInt("graphics.style", storedStyle);
            PlayerPrefs.SetInt("graphics.dither", 0);
            PlayerPrefs.SetInt("graphics.vertex_jitter", 1);
            PlayerPrefs.SetInt("graphics.clean_aa", 0);
            GraphicsEffectsSettings.ResetLoadedStateForTests();

            Assert.That(GraphicsEffectsSettings.Style, Is.EqualTo(GraphicsStyle.Ps1));
            Assert.That(GraphicsEffectsSettings.DitherEnabled, Is.False);
            Assert.That(GraphicsEffectsSettings.VertexJitterEnabled, Is.True);
            Assert.That(GraphicsEffectsSettings.CleanAntialiasingEnabled, Is.False);
            Assert.That(GraphicsEffectsSettings.Version, Is.Zero);
        }

        [TestCase(GraphicsStyle.Ps1, true, false)]
        [TestCase(GraphicsStyle.CleanLowPoly, false, true)]
        public void Style_ReportsApplicabilityWithoutOverwritingStoredChoices(
            GraphicsStyle selectedStyle, bool ps1EffectsApplicable, bool cleanAntialiasingApplicable)
        {
            GraphicsEffectsSettings.DitherEnabled = true;
            GraphicsEffectsSettings.ScanlinesEnabled = true;
            GraphicsEffectsSettings.VertexJitterEnabled = true;
            GraphicsEffectsSettings.CleanAntialiasingEnabled = true;
            GraphicsEffectsSettings.Style = selectedStyle;

            Assert.That(GraphicsEffectsSettings.IsDitherApplicable, Is.EqualTo(ps1EffectsApplicable));
            Assert.That(GraphicsEffectsSettings.IsScanlinesApplicable, Is.EqualTo(ps1EffectsApplicable));
            Assert.That(GraphicsEffectsSettings.IsVertexJitterApplicable, Is.EqualTo(ps1EffectsApplicable));
            Assert.That(GraphicsEffectsSettings.IsCleanAntialiasingApplicable, Is.EqualTo(cleanAntialiasingApplicable));
            Assert.That(GraphicsEffectsSettings.DitherEnabled, Is.True);
            Assert.That(GraphicsEffectsSettings.ScanlinesEnabled, Is.True);
            Assert.That(GraphicsEffectsSettings.VertexJitterEnabled, Is.True);
            Assert.That(GraphicsEffectsSettings.CleanAntialiasingEnabled, Is.True);
        }
    }
}
