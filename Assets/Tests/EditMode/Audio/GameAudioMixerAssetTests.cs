using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.EditMode
{
    public sealed class GameAudioMixerAssetTests
    {
        private static readonly GameAudioGroup[] RequiredGroups =
        {
            GameAudioGroup.Master,
            GameAudioGroup.Music,
            GameAudioGroup.AmbienceBeds,
            GameAudioGroup.AmbienceDetails,
            GameAudioGroup.SfxWorld,
            GameAudioGroup.SfxGameplay,
            GameAudioGroup.Ui
        };

        private static readonly string[] RequiredGroupPaths =
        {
            "Master",
            "Master/Music",
            "Master/Ambience/Beds",
            "Master/Ambience/Details",
            "Master/SFX/World",
            "Master/SFX/Gameplay",
            "Master/UI"
        };

        private static readonly string[] RequiredGroupNames =
        {
            "Master",
            "Music",
            "Beds",
            "Details",
            "World",
            "Gameplay",
            "UI"
        };

        private static readonly GameAudioProfile[] RequiredProfiles =
        {
            GameAudioProfile.City,
            GameAudioProfile.Bar,
            GameAudioProfile.Stairwell,
            GameAudioProfile.Home,
            GameAudioProfile.DoorTransition
        };

        private static readonly string[] RequiredSnapshotNames =
        {
            "City",
            "Bar",
            "Stairwell",
            "Home",
            "DoorTransition"
        };

        [TestCase(
            GameAudioProfile.City,
            GameAudioMixer.SceneProfileTransitionSeconds)]
        [TestCase(
            GameAudioProfile.Bar,
            GameAudioMixer.SceneProfileTransitionSeconds)]
        [TestCase(
            GameAudioProfile.Stairwell,
            GameAudioMixer.SceneProfileTransitionSeconds)]
        [TestCase(
            GameAudioProfile.Home,
            GameAudioMixer.SceneProfileTransitionSeconds)]
        [TestCase(
            GameAudioProfile.DoorTransition,
            GameAudioMixer.DoorTransitionProfileTransitionSeconds)]
        [TestCase(GameAudioProfile.None, 0f)]
        [TestCase(GameAudioProfile.Count, 0f)]
        public void GetTransitionSeconds_UsesProfilePolicy(
            GameAudioProfile profile,
            float expectedSeconds)
        {
            Assert.That(
                GameAudioMixer.GetTransitionSeconds(profile),
                Is.EqualTo(expectedSeconds).Within(0.0001f));
        }

        [Test]
        public void MixPolicy_KeepsActionsAheadOfMusicAndBeds()
        {
            Assert.That(
                GameAudioMixer.GetGroupGainDb(GameAudioGroup.Master),
                Is.EqualTo(-6f));
            Assert.That(
                GameAudioMixer.GetGroupGainDb(GameAudioGroup.Music),
                Is.LessThan(
                    GameAudioMixer.GetGroupGainDb(
                        GameAudioGroup.AmbienceBeds)));
            Assert.That(
                GameAudioMixer.GetGroupGainDb(
                    GameAudioGroup.AmbienceDetails),
                Is.GreaterThan(
                    GameAudioMixer.GetGroupGainDb(
                        GameAudioGroup.AmbienceBeds)));
            Assert.That(
                GameAudioMixer.GetGroupGainDb(
                    GameAudioGroup.SfxWorld) -
                GameAudioMixer.GetGroupGainDb(GameAudioGroup.Music),
                Is.GreaterThanOrEqualTo(7.5f));
            Assert.That(
                GameAudioMixer.GetGroupGainDb(
                    GameAudioGroup.SfxGameplay),
                Is.GreaterThan(
                    GameAudioMixer.GetGroupGainDb(
                        GameAudioGroup.SfxWorld)));
        }

        [TestCase(-18.3f, MusicMix.CityOutputVolume, "City")]
        [TestCase(-10.2f, MusicMix.BarOutputVolume, "Bar")]
        [TestCase(-12.5f, MusicMix.HomeOutputVolume, "Home")]
        [TestCase(-14.3f, MusicMix.SmokingOutputVolume, "Smoking")]
        [TestCase(-10.7f, MusicMix.StairwellOutputVolume, "Stairwell")]
        [TestCase(
            -13.3f,
            MusicMix.SupermarketOutputVolume,
            "Supermarket")]
        public void MusicCalibration_CurrentMastersShareBackgroundTarget(
            float rawIntegratedLufs,
            float sourceVolume,
            string track)
        {
            float effectiveLufs =
                rawIntegratedLufs +
                20f * Mathf.Log10(sourceVolume) +
                GameAudioMixer.MusicGainDb +
                GameAudioMixer.MasterHeadroomDb;

            Assert.That(
                effectiveLufs,
                Is.EqualTo(MusicMix.CalibratedIntegratedTargetLufs)
                    .Within(0.15f),
                track + " theme must remain in the shared background " +
                "loudness window.");
            Assert.That(
                MusicMix.ToneCutoffFrequency,
                Is.InRange(10000f, 13000f));
        }

        [Test]
        public void MixerAsset_HasCompleteCanonicalTopology()
        {
            AudioMixer mixer =
                Resources.Load<AudioMixer>(
                    GameAudioMixer.ResourcePath);

            Assert.That(
                GameAudioMixer.ResourcePath,
                Is.EqualTo("Audio/Mixers/BarPromenadeAudio"));
            Assert.That(
                mixer,
                Is.Not.Null,
                "The canonical Resources mixer asset must exist.");
            Assert.That(
                GameAudioMixer.Mixer,
                Is.SameAs(mixer));
            Assert.That(
                GameAudioMixer.HasCompleteConfiguration,
                Is.True);

            for (int index = 0;
                 index < RequiredGroups.Length;
                 index++)
            {
                string path = RequiredGroupPaths[index];
                AudioMixerGroup[] matches =
                    mixer.FindMatchingGroups(path);
                AudioMixerGroup exactMatch = null;
                int exactMatchCount = 0;
                for (int matchIndex = 0;
                     matchIndex < matches.Length;
                     matchIndex++)
                {
                    if (matches[matchIndex].name !=
                        RequiredGroupNames[index])
                    {
                        continue;
                    }

                    exactMatch = matches[matchIndex];
                    exactMatchCount++;
                }

                Assert.That(
                    GameAudioMixer.GetGroupPath(
                        RequiredGroups[index]),
                    Is.EqualTo(path));
                Assert.That(
                    exactMatchCount,
                    Is.EqualTo(1),
                    $"Mixer group path '{path}' must resolve uniquely.");
                if (RequiredGroups[index] !=
                    GameAudioGroup.Master)
                {
                    Assert.That(
                        matches,
                        Has.Length.EqualTo(1),
                        $"Full child path '{path}' must not " +
                        "include descendants or duplicate matches.");
                }

                Assert.That(
                    exactMatch.name,
                    Is.EqualTo(RequiredGroupNames[index]));
                Assert.That(
                    GameAudioMixer.GetGroup(
                        RequiredGroups[index]),
                    Is.SameAs(exactMatch));
            }

            for (int index = 0;
                 index < RequiredProfiles.Length;
                 index++)
            {
                GameAudioProfile profile =
                    RequiredProfiles[index];
                string snapshotName =
                    RequiredSnapshotNames[index];
                AudioMixerSnapshot snapshot =
                    mixer.FindSnapshot(snapshotName);

                Assert.That(
                    GameAudioMixer.GetSnapshotName(profile),
                    Is.EqualTo(snapshotName));
                Assert.That(snapshot, Is.Not.Null);
                Assert.That(
                    snapshot.name,
                    Is.EqualTo(snapshotName));
                Assert.That(
                    GameAudioMixer.GetSnapshot(profile),
                    Is.SameAs(snapshot));
            }
        }

        [Test]
        public void MixerAsset_HasCanonicalDspRoutingAndSceneValues()
        {
            AudioMixer mixer =
                Resources.Load<AudioMixer>(
                    GameAudioMixer.ResourcePath);
            Assert.That(mixer, Is.Not.Null);

            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(
                AssetDatabase.GetAssetPath(mixer));
            Dictionary<string, List<Object>> effects =
                IndexEffects(assets);

            Object compressor =
                RequireEffects(effects, "Compressor", 1)[0];
            Object reverb =
                RequireEffects(effects, "SFX Reverb", 1)[0];
            Object echo =
                RequireEffects(effects, "Echo", 1)[0];
            List<Object> receives =
                RequireEffects(effects, "Receive", 2);
            List<Object> sends =
                RequireEffects(effects, "Send", 5);

            for (int index = 0; index < sends.Count; index++)
            {
                Assert.That(
                    GetSendTarget(sends[index]),
                    Is.Not.Null,
                    $"Send #{index} must target a Receive effect.");
            }

            AudioMixerGroup master =
                FindExactGroup(mixer, "Master", "Master");
            AudioMixerGroup music =
                FindExactGroup(
                    mixer,
                    "Master/Music",
                    "Music");
            AudioMixerGroup details =
                FindExactGroup(
                    mixer,
                    "Master/Ambience/Details",
                    "Details");
            AudioMixerGroup world =
                FindExactGroup(
                    mixer,
                    "Master/SFX/World",
                    "World");
            AudioMixerGroup ui =
                FindExactGroup(mixer, "Master/UI", "UI");
            AudioMixerGroup reverbReturn =
                FindExactGroup(
                    mixer,
                    "Master/Environment Reverb Return",
                    "Environment Reverb Return");
            AudioMixerGroup echoReturn =
                FindExactGroup(
                    mixer,
                    "Master/Echo Return",
                    "Echo Return");

            Assert.That(
                GetSingleGroupEffect(master, "Compressor"),
                Is.SameAs(compressor));
            Assert.That(
                GetSingleGroupEffect(
                    reverbReturn,
                    "SFX Reverb"),
                Is.SameAs(reverb));
            Assert.That(
                GetSingleGroupEffect(echoReturn, "Echo"),
                Is.SameAs(echo));

            Object reverbReceive =
                GetSingleGroupEffect(reverbReturn, "Receive");
            Object echoReceive =
                GetSingleGroupEffect(echoReturn, "Receive");
            CollectionAssert.AreEquivalent(
                receives,
                new[] { reverbReceive, echoReceive });

            AssertSendLayout(music, reverbReceive);
            AssertSendLayout(
                details,
                reverbReceive,
                echoReceive);
            AssertSendLayout(
                world,
                reverbReceive,
                echoReceive);
            Assert.That(
                GetGroupEffects(ui, "Send"),
                Is.Empty,
                "UI must remain dry.");

            for (int index = 0;
                 index < RequiredSnapshotNames.Length;
                 index++)
            {
                string snapshotName =
                    RequiredSnapshotNames[index];
                AudioMixerSnapshot snapshot =
                    mixer.FindSnapshot(snapshotName);
                Assert.That(snapshot, Is.Not.Null);

                AssertValue(
                    GetEffectParameter(
                        echo,
                        mixer,
                        snapshot,
                        "Delay"),
                    230f,
                    $"{snapshotName} Echo Delay");
                AssertValue(
                    GetEffectParameter(
                        echo,
                        mixer,
                        snapshot,
                        "Decay"),
                    0.18f,
                    $"{snapshotName} Echo Decay");
                AssertValue(
                    GetEffectParameter(
                        echo,
                        mixer,
                        snapshot,
                        "Max channels"),
                    2f,
                    $"{snapshotName} Echo Max channels");
                AssertValue(
                    GetEffectParameter(
                        echo,
                        mixer,
                        snapshot,
                        "Drymix"),
                    0f,
                    $"{snapshotName} Echo Drymix");
                AssertValue(
                    GetEffectParameter(
                        echo,
                        mixer,
                        snapshot,
                        "Wetmix"),
                    0.34f,
                    $"{snapshotName} Echo Wetmix");
                for (int groupIndex = 0;
                     groupIndex < RequiredGroups.Length;
                     groupIndex++)
                {
                    AudioMixerGroup mixedGroup = FindExactGroup(
                        mixer,
                        RequiredGroupPaths[groupIndex],
                        RequiredGroupNames[groupIndex]);
                    AssertValue(
                        GetGroupVolume(
                            mixedGroup,
                            mixer,
                            snapshot),
                        GameAudioMixer.GetGroupGainDb(
                            RequiredGroups[groupIndex]),
                        $"{snapshotName} " +
                        $"{RequiredGroupNames[groupIndex]} volume");
                }
            }

            AudioMixerSnapshot door =
                mixer.FindSnapshot("DoorTransition");
            for (int index = 0; index < sends.Count; index++)
            {
                AssertValue(
                    GetEffectMixLevel(
                        sends[index],
                        mixer,
                        door),
                    -80f,
                    $"DoorTransition Send #{index}");
            }

            AssertValue(
                GetGroupVolume(
                    reverbReturn,
                    mixer,
                    door),
                -80f,
                "DoorTransition reverb return");
            AssertValue(
                GetGroupVolume(
                    echoReturn,
                    mixer,
                    door),
                -80f,
                "DoorTransition echo return");

            AudioMixerSnapshot stairwell =
                mixer.FindSnapshot("Stairwell");
            AudioMixerSnapshot home =
                mixer.FindSnapshot("Home");
            AssertSceneContrast(
                mixer,
                details,
                reverbReceive,
                echoReceive,
                stairwell,
                home,
                -9.5f,
                -25.5f,
                -22.5f);
            AssertSceneContrast(
                mixer,
                world,
                reverbReceive,
                echoReceive,
                stairwell,
                home,
                -14f,
                -26f,
                -28f);
            AssertValue(
                GetEffectParameter(
                    reverb,
                    mixer,
                    stairwell,
                    "Decay Time"),
                2.25f,
                "Stairwell reverb decay");
            AssertValue(
                GetEffectParameter(
                    reverb,
                    mixer,
                    home,
                    "Decay Time"),
                0.55f,
                "Home reverb decay");
            Assert.That(
                GetEffectParameter(
                    reverb,
                    mixer,
                    stairwell,
                    "Decay Time"),
                Is.GreaterThan(
                    GetEffectParameter(
                        reverb,
                        mixer,
                        home,
                        "Decay Time")));
        }

        private static Dictionary<string, List<Object>> IndexEffects(
            IEnumerable<Object> assets)
        {
            var result = new Dictionary<string, List<Object>>();
            foreach (Object asset in assets)
            {
                var serialized = new SerializedObject(asset);
                SerializedProperty effectName =
                    serialized.FindProperty("m_EffectName");
                if (effectName == null)
                {
                    continue;
                }

                string name = effectName.stringValue;
                if (!result.TryGetValue(
                        name,
                        out List<Object> matching))
                {
                    matching = new List<Object>();
                    result.Add(name, matching);
                }

                matching.Add(asset);
            }

            return result;
        }

        private static List<Object> RequireEffects(
            IReadOnlyDictionary<string, List<Object>> effects,
            string name,
            int expectedCount)
        {
            effects.TryGetValue(name, out List<Object> matching);
            Assert.That(
                matching,
                Is.Not.Null,
                $"Mixer must contain '{name}'.");
            Assert.That(
                matching,
                Has.Count.EqualTo(expectedCount),
                $"Mixer must contain exactly {expectedCount} '{name}' " +
                "effect(s).");
            return matching;
        }

        private static AudioMixerGroup FindExactGroup(
            AudioMixer mixer,
            string path,
            string expectedName)
        {
            AudioMixerGroup[] matches =
                mixer.FindMatchingGroups(path);
            AudioMixerGroup result = null;
            int count = 0;
            for (int index = 0; index < matches.Length; index++)
            {
                if (matches[index].name != expectedName)
                {
                    continue;
                }

                result = matches[index];
                count++;
            }

            Assert.That(
                count,
                Is.EqualTo(1),
                $"Mixer group path '{path}' must resolve uniquely.");
            return result;
        }

        private static List<Object> GetGroupEffects(
            AudioMixerGroup group,
            string effectName)
        {
            PropertyInfo effectsProperty =
                group.GetType().GetProperty(
                    "effects",
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);
            Assert.That(
                effectsProperty,
                Is.Not.Null,
                "Unity mixer group effects API must be available.");
            var values = (Object[])effectsProperty.GetValue(group);
            var matching = new List<Object>();
            for (int index = 0; index < values.Length; index++)
            {
                var serialized = new SerializedObject(values[index]);
                SerializedProperty name =
                    serialized.FindProperty("m_EffectName");
                if (name != null &&
                    name.stringValue == effectName)
                {
                    matching.Add(values[index]);
                }
            }

            return matching;
        }

        private static Object GetSingleGroupEffect(
            AudioMixerGroup group,
            string effectName)
        {
            List<Object> matching =
                GetGroupEffects(group, effectName);
            Assert.That(
                matching,
                Has.Count.EqualTo(1),
                $"Group '{group.name}' must contain exactly one " +
                $"'{effectName}' effect.");
            return matching[0];
        }

        private static Object GetSendTarget(Object send)
        {
            var serialized = new SerializedObject(send);
            SerializedProperty target =
                serialized.FindProperty("m_SendTarget");
            Assert.That(
                target,
                Is.Not.Null,
                "Unity mixer Send target must be serialized.");
            return target.objectReferenceValue;
        }

        private static void AssertSendLayout(
            AudioMixerGroup group,
            params Object[] expectedTargets)
        {
            List<Object> sends = GetGroupEffects(group, "Send");
            Assert.That(
                sends,
                Has.Count.EqualTo(expectedTargets.Length),
                $"Unexpected Send count on '{group.name}'.");

            var actualTargets = new List<Object>();
            for (int index = 0; index < sends.Count; index++)
            {
                actualTargets.Add(GetSendTarget(sends[index]));
            }

            CollectionAssert.AreEquivalent(
                expectedTargets,
                actualTargets,
                $"Unexpected Send routing on '{group.name}'.");
        }

        private static void AssertSceneContrast(
            AudioMixer mixer,
            AudioMixerGroup group,
            Object reverbReceive,
            Object echoReceive,
            AudioMixerSnapshot stairwell,
            AudioMixerSnapshot home,
            float stairwellReverb,
            float homeReverb,
            float stairwellEcho)
        {
            Object reverbSend =
                FindSendTo(group, reverbReceive);
            Object echoSend =
                FindSendTo(group, echoReceive);

            AssertValue(
                GetEffectMixLevel(
                    reverbSend,
                    mixer,
                    stairwell),
                stairwellReverb,
                $"Stairwell {group.name} reverb send");
            AssertValue(
                GetEffectMixLevel(
                    reverbSend,
                    mixer,
                    home),
                homeReverb,
                $"Home {group.name} reverb send");
            AssertValue(
                GetEffectMixLevel(
                    echoSend,
                    mixer,
                    stairwell),
                stairwellEcho,
                $"Stairwell {group.name} echo send");
            AssertValue(
                GetEffectMixLevel(
                    echoSend,
                    mixer,
                    home),
                -80f,
                $"Home {group.name} echo send");
        }

        private static Object FindSendTo(
            AudioMixerGroup group,
            Object target)
        {
            List<Object> sends = GetGroupEffects(group, "Send");
            Object result = null;
            int count = 0;
            for (int index = 0; index < sends.Count; index++)
            {
                if (GetSendTarget(sends[index]) != target)
                {
                    continue;
                }

                result = sends[index];
                count++;
            }

            Assert.That(
                count,
                Is.EqualTo(1),
                $"Group '{group.name}' must have one Send to " +
                $"'{target.name}'.");
            return result;
        }

        private static float GetEffectParameter(
            Object effect,
            AudioMixer mixer,
            AudioMixerSnapshot snapshot,
            string parameter)
        {
            return InvokeFloat(
                effect,
                "GetValueForParameter",
                mixer,
                snapshot,
                parameter);
        }

        private static float GetEffectMixLevel(
            Object effect,
            AudioMixer mixer,
            AudioMixerSnapshot snapshot)
        {
            return InvokeFloat(
                effect,
                "GetValueForMixLevel",
                mixer,
                snapshot);
        }

        private static float GetGroupVolume(
            AudioMixerGroup group,
            AudioMixer mixer,
            AudioMixerSnapshot snapshot)
        {
            return InvokeFloat(
                group,
                "GetValueForVolume",
                mixer,
                snapshot);
        }

        private static float InvokeFloat(
            Object target,
            string methodName,
            params object[] arguments)
        {
            MethodInfo method = null;
            MethodInfo[] methods =
                target.GetType().GetMethods(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);
            for (int index = 0; index < methods.Length; index++)
            {
                if (methods[index].Name == methodName &&
                    methods[index].GetParameters().Length ==
                    arguments.Length)
                {
                    method = methods[index];
                    break;
                }
            }

            Assert.That(
                method,
                Is.Not.Null,
                $"Unity mixer method '{methodName}' must be available.");
            return (float)method.Invoke(target, arguments);
        }

        private static void AssertValue(
            float actual,
            float expected,
            string context)
        {
            Assert.That(
                actual,
                Is.EqualTo(expected).Within(0.0001f),
                context);
        }
    }
}
