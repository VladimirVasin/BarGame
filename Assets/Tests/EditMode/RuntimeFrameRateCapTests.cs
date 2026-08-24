using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class RuntimeFrameRateCapTests
    {
        private int previousTargetFrameRate;
        private int previousVSyncCount;

        [SetUp]
        public void SetUp()
        {
            previousTargetFrameRate = Application.targetFrameRate;
            previousVSyncCount = QualitySettings.vSyncCount;
        }

        [TearDown]
        public void TearDown()
        {
            Application.targetFrameRate = previousTargetFrameRate;
            QualitySettings.vSyncCount = previousVSyncCount;
        }

        [Test]
        public void Cap_HoldsSixtyAndClearsVSync()
        {
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 1;

            BarPromenadeRuntimeBootstrap.ApplyFrameRateCap(false);

            Assert.That(
                BarPromenadeRuntimeBootstrap.TargetFrameRate,
                Is.EqualTo(60),
                "The rate is fixed rather than offered: it changes how " +
                "the hero handles, so it is not a player setting.");
            Assert.That(
                Application.targetFrameRate,
                Is.EqualTo(
                    BarPromenadeRuntimeBootstrap.TargetFrameRate));
            Assert.That(
                QualitySettings.vSyncCount,
                Is.Zero,
                "A target frame rate is ignored while vSync is on.");
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
        public void Cap_StaysWithinTheMeasuredRange()
        {
            // The cap is not decoration. Planar speed is read back from
            // the movement the controller delivered, so a graze against
            // tight geometry costs the hero all of it and he
            // re-accelerates; the faster the frames, the less ground he
            // recovers between grazes. A descent sweep over
            // 60/90/120/144/240/500 fps showed that gradient against a
            // one-centimetre overhang, and 60 was the last rate that still
            // carried him through. Raising this means re-running that
            // measurement, not editing the number.
            Assert.That(
                BarPromenadeRuntimeBootstrap.TargetFrameRate,
                Is.LessThanOrEqualTo(60));
            Assert.That(
                BarPromenadeRuntimeBootstrap.TargetFrameRate,
                Is.GreaterThan(0),
                "Zero or negative would mean uncapped.");
        }
    }
}
