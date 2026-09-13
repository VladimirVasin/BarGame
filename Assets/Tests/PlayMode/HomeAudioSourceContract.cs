using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// What the Home's audio sources owe the scene, asked of each owner
    /// rather than of one total.
    ///
    /// The old total (`3 + soundscape + alarm + weather`) guarded against
    /// a duplicated ambience or music player, but it also broke on every
    /// on-demand voice a fixture gained - the sink tap (c4615756), the
    /// brushing spit (999ffc93), the two urine contacts and the hero's
    /// vomit stream (f822d0c7) - about which a total has no opinion. So:
    /// every declared owner keeps its exact count, and every source nobody
    /// declares is an on-demand voice, which means silent until something
    /// plays it.
    /// </summary>
    internal static class HomeAudioSourceContract
    {
        public static void AssertSourcesAreDeclaredOrOnDemand(
            HomeInteriorRoot home)
        {
            Assert.That(home, Is.Not.Null);
            var declared = new HashSet<AudioSource>();
            Claim(declared, home.Ambience, 1, "base ambience");
            Claim(declared, home.Music, 1, "background music");
            Claim(declared, home.SmokingMusic, 1, "smoking music");
            Claim(
                declared,
                home.Soundscape,
                HomeSoundscape.OwnedSourceCount,
                "soundscape");
            Claim(
                declared,
                home.AlarmClock,
                HomeAlarmClock.OwnedSourceCount,
                "alarm clock");
            Assert.That(
                home.ExteriorAtmosphere,
                Is.Not.Null,
                "The home has no weather behind the window.");
            Claim(
                declared,
                home.ExteriorAtmosphere.RainSound,
                CityRainSoundPlayer.OwnedSourceCount,
                "rain behind the window");
            Claim(
                declared,
                home.ExteriorAtmosphere.ThunderSound,
                CityThunderSoundPlayer.OwnedSourceCount,
                "thunder behind the window");

            AudioSource[] sources =
                home.GetComponentsInChildren<AudioSource>(true);
            for (int index = 0; index < sources.Length; index++)
            {
                AudioSource source = sources[index];
                if (declared.Contains(source))
                {
                    continue;
                }

                string path = PathOf(source.transform);
                Assert.That(
                    source.playOnAwake,
                    Is.False,
                    $"Undeclared source '{path}' must not play on awake.");
                Assert.That(
                    source.isPlaying,
                    Is.False,
                    $"Undeclared source '{path}' is an on-demand voice " +
                    "and must stay silent until something plays it.");
            }
        }

        private static void Claim(
            HashSet<AudioSource> declared,
            Component owner,
            int expected,
            string role)
        {
            Assert.That(owner, Is.Not.Null, $"The home has no {role} owner.");
            AudioSource[] owned =
                owner.GetComponentsInChildren<AudioSource>(true);
            Assert.That(
                owned,
                Has.Length.EqualTo(expected),
                $"The {role} must own exactly {expected} source(s).");
            for (int index = 0; index < owned.Length; index++)
            {
                Assert.That(
                    declared.Add(owned[index]),
                    Is.True,
                    $"Source '{PathOf(owned[index].transform)}' is " +
                    "claimed by two owners.");
            }
        }

        private static string PathOf(Transform transform)
        {
            string path = transform.name;
            for (Transform parent = transform.parent;
                 parent != null;
                 parent = parent.parent)
            {
                path = parent.name + "/" + path;
            }

            return path;
        }
    }
}
