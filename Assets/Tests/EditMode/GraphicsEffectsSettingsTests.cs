using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class GraphicsEffectsSettingsTests
    {
        private static readonly string[] Keys =
        {
            "graphics.clean_aa",
            "graphics.dof",
            "graphics.intoxication_fx",
            "graphics.aspect_4_3",
            "graphics.clean_reflections",
            "graphics.clean_volume",
            "graphics.clean_halation",
            "graphics.clean_directional_highlights",
            "graphics.clean_bounce",
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
                savedValues[i] = PlayerPrefs.GetInt(Keys[i]);
                PlayerPrefs.DeleteKey(Keys[i]);
            }
            GraphicsEffectsSettings.ResetLoadedStateForTests();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < Keys.Length; i++)
            {
                if (savedExists[i]) PlayerPrefs.SetInt(Keys[i], savedValues[i]);
                else PlayerPrefs.DeleteKey(Keys[i]);
            }
            PlayerPrefs.Save();
            GraphicsEffectsSettings.ResetLoadedStateForTests();
        }

        [Test]
        public void Defaults_AllEffectsEnabledWithoutPrefs()
        {
            Assert.That(GraphicsEffectsSettings.CleanAntialiasingEnabled, Is.True);
            Assert.That(GraphicsEffectsSettings.DepthOfFieldEnabled, Is.True);
            Assert.That(GraphicsEffectsSettings.IntoxicationLensFxEnabled, Is.True);
            Assert.That(GraphicsEffectsSettings.AspectRatio43Enabled, Is.False);
            Assert.That(GraphicsEffectsSettings.CleanReflectionsEnabled, Is.True);
            Assert.That(GraphicsEffectsSettings.CleanVolumeEnabled, Is.True);
            Assert.That(GraphicsEffectsSettings.CleanHalationEnabled, Is.True);
            Assert.That(GraphicsEffectsSettings.CleanDirectionalHighlightsEnabled, Is.True);
            Assert.That(GraphicsEffectsSettings.CleanBounceEnabled, Is.True);
            foreach (string key in Keys) Assert.That(PlayerPrefs.HasKey(key), Is.False, key);
            Assert.That(GraphicsEffectsSettings.Version, Is.Zero);
        }

        [Test]
        public void Setter_BumpsVersionOnlyOnActualChange()
        {
            GraphicsEffectsSettings.DepthOfFieldEnabled = true;
            Assert.That(GraphicsEffectsSettings.Version, Is.Zero);
            GraphicsEffectsSettings.DepthOfFieldEnabled = false;
            Assert.That(GraphicsEffectsSettings.Version, Is.EqualTo(1));
            GraphicsEffectsSettings.DepthOfFieldEnabled = false;
            Assert.That(GraphicsEffectsSettings.Version, Is.EqualTo(1));
            SetAll(false);
            Assert.That(GraphicsEffectsSettings.Version, Is.EqualTo(8));
            SetAll(false);
            Assert.That(GraphicsEffectsSettings.Version, Is.EqualTo(8));
            GraphicsEffectsSettings.AspectRatio43Enabled = true;
            Assert.That(GraphicsEffectsSettings.Version, Is.EqualTo(9));
        }

        [Test]
        public void Setter_PersistsAcrossReload()
        {
            SetAll(false);
            GraphicsEffectsSettings.AspectRatio43Enabled = true;
            GraphicsEffectsSettings.ResetLoadedStateForTests();
            Assert.That(GraphicsEffectsSettings.CleanAntialiasingEnabled, Is.False);
            Assert.That(PlayerPrefs.GetInt("graphics.clean_aa", -1), Is.EqualTo(0));
            Assert.That(GraphicsEffectsSettings.DepthOfFieldEnabled, Is.False);
            Assert.That(PlayerPrefs.GetInt("graphics.dof", -1), Is.EqualTo(0));
            Assert.That(GraphicsEffectsSettings.IntoxicationLensFxEnabled, Is.False);
            Assert.That(PlayerPrefs.GetInt("graphics.intoxication_fx", -1), Is.EqualTo(0));
            Assert.That(GraphicsEffectsSettings.AspectRatio43Enabled, Is.True);
            Assert.That(PlayerPrefs.GetInt("graphics.aspect_4_3", -1), Is.EqualTo(1));
            Assert.That(GraphicsEffectsSettings.CleanReflectionsEnabled, Is.False);
            Assert.That(PlayerPrefs.GetInt("graphics.clean_reflections", -1), Is.EqualTo(0));
            Assert.That(GraphicsEffectsSettings.CleanVolumeEnabled, Is.False);
            Assert.That(PlayerPrefs.GetInt("graphics.clean_volume", -1), Is.EqualTo(0));
            Assert.That(GraphicsEffectsSettings.CleanHalationEnabled, Is.False);
            Assert.That(PlayerPrefs.GetInt("graphics.clean_halation", -1), Is.EqualTo(0));
            Assert.That(GraphicsEffectsSettings.CleanDirectionalHighlightsEnabled, Is.False);
            Assert.That(PlayerPrefs.GetInt("graphics.clean_directional_highlights", -1), Is.EqualTo(0));
            Assert.That(GraphicsEffectsSettings.CleanBounceEnabled, Is.False);
            Assert.That(PlayerPrefs.GetInt("graphics.clean_bounce", -1), Is.EqualTo(0));
        }

        private static void SetAll(bool enabled)
        {
            GraphicsEffectsSettings.CleanAntialiasingEnabled = enabled;
            GraphicsEffectsSettings.DepthOfFieldEnabled = enabled;
            GraphicsEffectsSettings.IntoxicationLensFxEnabled = enabled;
            GraphicsEffectsSettings.AspectRatio43Enabled = enabled;
            GraphicsEffectsSettings.CleanReflectionsEnabled = enabled;
            GraphicsEffectsSettings.CleanVolumeEnabled = enabled;
            GraphicsEffectsSettings.CleanHalationEnabled = enabled;
            GraphicsEffectsSettings.CleanDirectionalHighlightsEnabled = enabled;
            GraphicsEffectsSettings.CleanBounceEnabled = enabled;
        }
    }
}
