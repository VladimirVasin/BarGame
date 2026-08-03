using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class AutomaticTestAudioMuteTests
    {
        [Test]
        public void RegisteredCallback_MutesCurrentEditModeRun()
        {
            Assert.That(AudioListener.volume, Is.Zero);
        }
    }
}
