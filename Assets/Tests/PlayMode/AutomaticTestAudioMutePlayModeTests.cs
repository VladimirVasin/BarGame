using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class AutomaticTestAudioMutePlayModeTests
    {
        [Test]
        public void RegisteredCallback_MutesCurrentPlayModeRun()
        {
            Assert.That(AudioListener.volume, Is.Zero);
        }
    }
}
