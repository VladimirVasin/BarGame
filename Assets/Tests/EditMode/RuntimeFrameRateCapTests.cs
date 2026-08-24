using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class RuntimeFrameRateCapTests
    {
        private const string HighFrameRateKey = "graphics.frame_rate_60";

        private int previousTargetFrameRate;
        private int previousVSyncCount;
        private bool savedKeyExists;
        private int savedKeyValue;

        [SetUp]
        public void SetUp()
        {
            previousTargetFrameRate = Application.targetFrameRate;
            previousVSyncCount = QualitySettings.vSyncCount;
            savedKeyExists = PlayerPrefs.HasKey(HighFrameRateKey);
            if (savedKeyExists)
            {
                savedKeyValue = PlayerPrefs.GetInt(HighFrameRateKey);
            }

            PlayerPrefs.DeleteKey(HighFrameRateKey);
            GraphicsEffectsSettings.ResetLoadedStateForTests();
        }

        [TearDown]
        public void TearDown()
        {
            Application.targetFrameRate = previousTargetFrameRate;
            QualitySettings.vSyncCount = previousVSyncCount;
            if (savedKeyExists)
            {
                PlayerPrefs.SetInt(HighFrameRateKey, savedKeyValue);
            }
            else
            {
                PlayerPrefs.DeleteKey(HighFrameRateKey);
            }

            PlayerPrefs.Save();
            GraphicsEffectsSettings.ResetLoadedStateForTests();
        }

        [Test]
        public void Cap_DefaultsToThePeriodRateAndClearsVSync()
        {
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 1;

            BarPromenadeRuntimeBootstrap.ApplyFrameRateCap(false);

            Assert.That(
                BarPromenadeRuntimeBootstrap.PeriodFrameRate,
                Is.EqualTo(30),
                "The game is dressed for the fixed-camera survival " +
                "horror rate.");
            Assert.That(
                Application.targetFrameRate,
                Is.EqualTo(
                    BarPromenadeRuntimeBootstrap.PeriodFrameRate));
            Assert.That(
                QualitySettings.vSyncCount,
                Is.Zero,
                "A target frame rate is ignored while vSync is on.");
        }

        [Test]
        public void Cap_FollowsThePlayersChoiceBothWays()
        {
            GraphicsEffectsSettings.HighFrameRateEnabled = true;
            BarPromenadeRuntimeBootstrap.ApplyFrameRateCap(false);
            Assert.That(
                Application.targetFrameRate,
                Is.EqualTo(
                    BarPromenadeRuntimeBootstrap.SmoothFrameRate));

            GraphicsEffectsSettings.HighFrameRateEnabled = false;
            BarPromenadeRuntimeBootstrap.ApplyFrameRateCap(false);
            Assert.That(
                Application.targetFrameRate,
                Is.EqualTo(
                    BarPromenadeRuntimeBootstrap.PeriodFrameRate));
        }

        [Test]
        public void Cap_LeavesHeadlessRunsAlone()
        {
            Application.targetFrameRate = -1;

            BarPromenadeRuntimeBootstrap.ApplyFrameRateCap(true);

            Assert.That(
                Application.targetFrameRate,
                Is.EqualTo(-1),
                "Capping a batch-mode run would idle the test runner " +
                "between frames.");
        }

        [Test]
        public void Cap_OffersNoRateBeyondTwiceThePeriodOne()
        {
            // The menu is deliberately short. The hero's planar speed is
            // read back from the movement the controller delivered, so a
            // graze against tight geometry costs him all of it and he
            // re-accelerates; the faster the frames, the less ground he
            // recovers between grazes. A descent sweep over
            // 60/90/120/144/240/500 fps showed exactly that gradient
            // against a one-centimetre overhang — worse at every step up.
            // Raising this ceiling means re-measuring that, not editing
            // the number.
            Assert.That(
                BarPromenadeRuntimeBootstrap.SmoothFrameRate,
                Is.LessThanOrEqualTo(
                    BarPromenadeRuntimeBootstrap.PeriodFrameRate * 2),
                "The optional rate may double the period one, no more.");
        }
    }
}
