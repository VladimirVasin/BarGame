using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class GraphicsEffectsSettingsTests
    {
        private static readonly string[] Keys =
        {
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
            Assert.That(
                GraphicsEffectsSettings.DitherEnabled,
                Is.False);

            GraphicsEffectsSettings.DitherEnabled = false;
            Assert.That(
                GraphicsEffectsSettings.Version,
                Is.EqualTo(initial + 1));
        }

        [Test]
        public void Setter_PersistsAcrossReload()
        {
            GraphicsEffectsSettings.ScanlinesEnabled = false;
            GraphicsEffectsSettings.DepthOfFieldEnabled = false;
            GraphicsEffectsSettings.AspectRatio43Enabled = true;

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
        }
    }
}
