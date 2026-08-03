using NUnit.Framework.Interfaces;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.TestRunner;

[assembly: TestRunCallback(
    typeof(BarPromenade.Tests.Infrastructure.AutomaticTestAudioMute))]

namespace BarPromenade.Tests.Infrastructure
{
    [Preserve]
    public sealed class AutomaticTestAudioMute : ITestRunCallback
    {
        private float previousVolume = 1f;
        private bool runActive;

        public void RunStarted(ITest testsToRun)
        {
            if (!runActive)
            {
                previousVolume = AudioListener.volume;
                runActive = true;
            }

            Mute();
        }

        public void RunFinished(ITestResult testResults)
        {
            if (!runActive)
            {
                return;
            }

            AudioListener.volume = previousVolume;
            runActive = false;
        }

        public void TestStarted(ITest test)
        {
            if (runActive)
            {
                Mute();
            }
        }

        public void TestFinished(ITestResult result)
        {
            if (runActive)
            {
                Mute();
            }
        }

        private static void Mute()
        {
            AudioListener.volume = 0f;
        }
    }
}
