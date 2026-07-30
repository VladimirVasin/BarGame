using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

namespace BarPromenade.Editor
{
    public static class AudioMixerAssetSetup
    {
        public const string MixerPath =
            "Assets/Resources/Audio/Mixers/BarPromenadeAudio.mixer";

        private const float SilentDb = -80f;
        private const float MasterHeadroomDb = -6f;

        private static readonly string[] SnapshotNames =
        {
            "City",
            "Bar",
            "Stairwell",
            "Home",
            "DoorTransition"
        };

        private static readonly SceneMix[] SceneMixes =
        {
            new SceneMix(
                "City",
                -32f,
                -26f,
                -30f,
                SilentDb,
                SilentDb,
                0.72f,
                0.48f,
                -2500f,
                -4300f,
                -1450f),
            new SceneMix(
                "Bar",
                -28f,
                -20f,
                -22f,
                SilentDb,
                SilentDb,
                1.05f,
                0.52f,
                -1750f,
                -3100f,
                -950f),
            new SceneMix(
                "Stairwell",
                -24f,
                -9f,
                -12f,
                -22f,
                -26f,
                2.25f,
                0.38f,
                -750f,
                -2550f,
                -240f),
            new SceneMix(
                "Home",
                -32f,
                -25f,
                -24f,
                SilentDb,
                SilentDb,
                0.55f,
                0.30f,
                -2600f,
                -5200f,
                -1650f),
            new SceneMix(
                "DoorTransition",
                SilentDb,
                SilentDb,
                SilentDb,
                SilentDb,
                SilentDb,
                0.20f,
                0.10f,
                -10000f,
                -10000f,
                -10000f)
        };

        [MenuItem("Bar Promenade/Audio/Create or Update Audio Mixer")]
        public static void Run()
        {
            EnsureFolder("Assets/Resources");
            EnsureFolder("Assets/Resources/Audio");
            EnsureFolder("Assets/Resources/Audio/Mixers");

            var api = new MixerEditorApi();
            api.RefreshEffectDefinitions();

            AudioMixer mixer =
                AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            object controller;
            bool created = mixer == null;
            if (created)
            {
                controller = api.CreateMixer(MixerPath);
                mixer = controller as AudioMixer;
                if (mixer == null)
                {
                    throw new InvalidOperationException(
                        "Unity created an AudioMixer controller with an " +
                        "unexpected managed type.");
                }
            }
            else
            {
                controller = api.FindController(mixer, MixerPath);
            }

            Dictionary<string, object> groups =
                EnsureGroups(api, mixer, controller);
            Dictionary<string, object> snapshots =
                EnsureSnapshots(api, controller);
            ConfigureMixer(
                api,
                mixer,
                controller,
                groups,
                snapshots);

            api.SetTargetSnapshot(controller, snapshots["City"]);
            api.SetStartSnapshot(controller, snapshots["City"]);
            api.NotifySubAssetChanged(controller);

            EditorUtility.SetDirty(mixer);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(
                MixerPath,
                ImportAssetOptions.ForceUpdate);

            Debug.Log(
                $"Bar Promenade audio mixer {(created ? "created" : "updated")} " +
                $"at '{MixerPath}'.");
        }

        private static Dictionary<string, object> EnsureGroups(
            MixerEditorApi api,
            AudioMixer mixer,
            object controller)
        {
            var groups = new Dictionary<string, object>(
                StringComparer.Ordinal);
            object master = api.GetMasterGroup(controller);
            SetObjectName(master, "Master");
            groups.Add("Master", master);

            object music = EnsureGroup(
                api,
                mixer,
                controller,
                master,
                "Music");
            object ambience = EnsureGroup(
                api,
                mixer,
                controller,
                master,
                "Ambience");
            object beds = EnsureGroup(
                api,
                mixer,
                controller,
                ambience,
                "Beds");
            object details = EnsureGroup(
                api,
                mixer,
                controller,
                ambience,
                "Details");
            object sfx = EnsureGroup(
                api,
                mixer,
                controller,
                master,
                "SFX");
            object world = EnsureGroup(
                api,
                mixer,
                controller,
                sfx,
                "World");
            object gameplay = EnsureGroup(
                api,
                mixer,
                controller,
                sfx,
                "Gameplay");
            object ui = EnsureGroup(
                api,
                mixer,
                controller,
                master,
                "UI");
            object reverbReturn = EnsureGroup(
                api,
                mixer,
                controller,
                master,
                "Environment Reverb Return");
            object echoReturn = EnsureGroup(
                api,
                mixer,
                controller,
                master,
                "Echo Return");

            groups.Add("Master/Music", music);
            groups.Add("Master/Ambience", ambience);
            groups.Add("Master/Ambience/Beds", beds);
            groups.Add("Master/Ambience/Details", details);
            groups.Add("Master/SFX", sfx);
            groups.Add("Master/SFX/World", world);
            groups.Add("Master/SFX/Gameplay", gameplay);
            groups.Add("Master/UI", ui);
            groups.Add(
                "Master/Environment Reverb Return",
                reverbReturn);
            groups.Add("Master/Echo Return", echoReturn);
            return groups;
        }

        private static object EnsureGroup(
            MixerEditorApi api,
            AudioMixer mixer,
            object controller,
            object parent,
            string name)
        {
            object group = FindNamedObject(
                api.GetGroupChildren(parent),
                name);
            if (group != null)
            {
                return group;
            }

            object reusable = null;
            int reusableCount = 0;
            foreach (object candidate in api.GetAllGroups(controller))
            {
                if (candidate == null ||
                    ReferenceEquals(candidate, parent) ||
                    !string.Equals(
                        GetObjectName(candidate),
                        name,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                reusable = candidate;
                reusableCount++;
            }

            group = reusableCount == 1
                ? reusable
                : api.CreateGroup(controller, name);
            api.AddChildToParent(controller, group, parent);
            EditorUtility.SetDirty((Object)group);
            EditorUtility.SetDirty(mixer);
            return group;
        }

        private static Dictionary<string, object> EnsureSnapshots(
            MixerEditorApi api,
            object controller)
        {
            var snapshots = IndexNamedObjects(api.GetSnapshots(controller));
            if (!snapshots.TryGetValue("City", out object city))
            {
                if (snapshots.TryGetValue("Snapshot", out object defaultState))
                {
                    SetObjectName(defaultState, "City");
                    city = defaultState;
                }
                else
                {
                    object[] existing = api.GetSnapshots(controller);
                    if (existing.Length == 0)
                    {
                        throw new InvalidOperationException(
                            "The mixer has no source snapshot to clone.");
                    }

                    city = api.CloneSnapshot(
                        controller,
                        existing[0],
                        "City");
                }

                snapshots = IndexNamedObjects(api.GetSnapshots(controller));
            }

            for (int index = 0; index < SnapshotNames.Length; index++)
            {
                string name = SnapshotNames[index];
                if (snapshots.ContainsKey(name))
                {
                    continue;
                }

                object snapshot = api.CloneSnapshot(
                    controller,
                    city,
                    name);
                snapshots.Add(name, snapshot);
            }

            return snapshots;
        }

        private static void ConfigureMixer(
            MixerEditorApi api,
            AudioMixer mixer,
            object controller,
            IReadOnlyDictionary<string, object> groups,
            IReadOnlyDictionary<string, object> snapshots)
        {
            object master = groups["Master"];
            object music = groups["Master/Music"];
            object details = groups["Master/Ambience/Details"];
            object world = groups["Master/SFX/World"];
            object ui = groups["Master/UI"];
            object reverbReturn =
                groups["Master/Environment Reverb Return"];
            object echoReturn = groups["Master/Echo Return"];

            api.RemoveEffectsByName(controller, ui, "Send");

            object compressor = EnsureRequiredEffect(
                api,
                mixer,
                master,
                0,
                "Compressor");
            object reverbReceive = EnsureRequiredEffect(
                api,
                mixer,
                reverbReturn,
                0,
                "Receive");
            object reverb = EnsureRequiredEffect(
                api,
                mixer,
                reverbReturn,
                1,
                "SFX Reverb",
                "Reverb");
            object echoReceive = EnsureRequiredEffect(
                api,
                mixer,
                echoReturn,
                0,
                "Receive");
            object echo = EnsureRequiredEffect(
                api,
                mixer,
                echoReturn,
                1,
                "Echo");

            object musicReverbSend = EnsureRequiredSend(
                api,
                mixer,
                music,
                reverbReceive);
            object detailsReverbSend = EnsureRequiredSend(
                api,
                mixer,
                details,
                reverbReceive);
            object worldReverbSend = EnsureRequiredSend(
                api,
                mixer,
                world,
                reverbReceive);
            object detailsEchoSend = EnsureRequiredSend(
                api,
                mixer,
                details,
                echoReceive);
            object worldEchoSend = EnsureRequiredSend(
                api,
                mixer,
                world,
                echoReceive);

            ConfigureMasterCompressor(
                api,
                controller,
                compressor,
                snapshots);
            ConfigureEcho(
                api,
                controller,
                echo,
                snapshots);

            for (int index = 0; index < SceneMixes.Length; index++)
            {
                SceneMix mix = SceneMixes[index];
                object snapshot = snapshots[mix.SnapshotName];
                api.SetGroupVolume(
                    master,
                    controller,
                    snapshot,
                    MasterHeadroomDb);
                api.SetGroupVolume(
                    reverbReturn,
                    controller,
                    snapshot,
                    mix.SnapshotName == "DoorTransition"
                        ? SilentDb
                        : 0f);
                api.SetGroupVolume(
                    echoReturn,
                    controller,
                    snapshot,
                    mix.SnapshotName == "DoorTransition"
                        ? SilentDb
                        : 0f);

                SetSendLevel(
                    api,
                    musicReverbSend,
                    controller,
                    snapshot,
                    mix.MusicReverbSendDb);
                SetSendLevel(
                    api,
                    detailsReverbSend,
                    controller,
                    snapshot,
                    mix.DetailsReverbSendDb);
                SetSendLevel(
                    api,
                    worldReverbSend,
                    controller,
                    snapshot,
                    mix.WorldReverbSendDb);
                SetSendLevel(
                    api,
                    detailsEchoSend,
                    controller,
                    snapshot,
                    mix.DetailsEchoSendDb);
                SetSendLevel(
                    api,
                    worldEchoSend,
                    controller,
                    snapshot,
                    mix.WorldEchoSendDb);
                ConfigureReverb(
                    api,
                    controller,
                    reverb,
                    snapshot,
                    mix);
            }
        }

        private static void ConfigureMasterCompressor(
            MixerEditorApi api,
            object controller,
            object compressor,
            IReadOnlyDictionary<string, object> snapshots)
        {
            if (compressor == null)
            {
                return;
            }

            foreach (string name in SnapshotNames)
            {
                object snapshot = snapshots[name];
                api.TrySetEffectParameter(
                    compressor,
                    controller,
                    snapshot,
                    -9f,
                    "Threshold");
                api.TrySetEffectParameter(
                    compressor,
                    controller,
                    snapshot,
                    2f,
                    "Ratio");
                api.TrySetEffectParameter(
                    compressor,
                    controller,
                    snapshot,
                    20f,
                    "Attack");
                api.TrySetEffectParameter(
                    compressor,
                    controller,
                    snapshot,
                    120f,
                    "Release");
                api.TrySetEffectParameter(
                    compressor,
                    controller,
                    snapshot,
                    0f,
                    "Make-up Gain",
                    "Makeup Gain",
                    "Make Up Gain");
            }
        }

        private static void ConfigureEcho(
            MixerEditorApi api,
            object controller,
            object echo,
            IReadOnlyDictionary<string, object> snapshots)
        {
            if (echo == null)
            {
                return;
            }

            foreach (string name in SnapshotNames)
            {
                object snapshot = snapshots[name];
                SetRequiredEffectParameter(
                    api,
                    echo,
                    controller,
                    snapshot,
                    230f,
                    "Delay");
                SetRequiredEffectParameter(
                    api,
                    echo,
                    controller,
                    snapshot,
                    0.18f,
                    "Decay Ratio",
                    "Decay");
                SetRequiredEffectParameter(
                    api,
                    echo,
                    controller,
                    snapshot,
                    2f,
                    "Max channels");
                SetRequiredEffectParameter(
                    api,
                    echo,
                    controller,
                    snapshot,
                    0f,
                    "Dry Mix");
                SetRequiredEffectParameter(
                    api,
                    echo,
                    controller,
                    snapshot,
                    0.34f,
                    "Wet Mix");
            }
        }

        private static void SetRequiredEffectParameter(
            MixerEditorApi api,
            object effect,
            object controller,
            object snapshot,
            float value,
            params string[] candidateNames)
        {
            if (api.TrySetEffectParameter(
                    effect,
                    controller,
                    snapshot,
                    value,
                    candidateNames))
            {
                return;
            }

            throw new InvalidOperationException(
                "AudioMixer effect parameter is unavailable: " +
                string.Join(" / ", candidateNames));
        }

        private static void ConfigureReverb(
            MixerEditorApi api,
            object controller,
            object reverb,
            object snapshot,
            SceneMix mix)
        {
            if (reverb == null)
            {
                return;
            }

            api.TrySetEffectParameter(
                reverb,
                controller,
                snapshot,
                -10000f,
                "Dry Level");
            api.TrySetEffectParameter(
                reverb,
                controller,
                snapshot,
                mix.RoomLevel,
                "Room");
            api.TrySetEffectParameter(
                reverb,
                controller,
                snapshot,
                mix.RoomHighFrequencyLevel,
                "Room HF",
                "Room High Frequency");
            api.TrySetEffectParameter(
                reverb,
                controller,
                snapshot,
                mix.DecayTime,
                "Decay Time");
            api.TrySetEffectParameter(
                reverb,
                controller,
                snapshot,
                mix.DecayHighFrequencyRatio,
                "Decay HF Ratio",
                "Decay High Frequency Ratio");
            api.TrySetEffectParameter(
                reverb,
                controller,
                snapshot,
                mix.ReverbLevel,
                "Reverb",
                "Reverb Level");
            api.TrySetEffectParameter(
                reverb,
                controller,
                snapshot,
                mix.SnapshotName == "Stairwell" ? -900f : -2400f,
                "Reflections",
                "Reflections Level");
            api.TrySetEffectParameter(
                reverb,
                controller,
                snapshot,
                mix.SnapshotName == "Stairwell" ? 0.020f : 0.008f,
                "Reflect Delay",
                "Reflections Delay");
            api.TrySetEffectParameter(
                reverb,
                controller,
                snapshot,
                mix.SnapshotName == "Stairwell" ? 0.028f : 0.010f,
                "Reverb Delay");
            api.TrySetEffectParameter(
                reverb,
                controller,
                snapshot,
                mix.SnapshotName == "Stairwell" ? 72f : 58f,
                "Diffusion");
            api.TrySetEffectParameter(
                reverb,
                controller,
                snapshot,
                mix.SnapshotName == "Stairwell" ? 86f : 68f,
                "Density");
        }

        private static object EnsureRequiredEffect(
            MixerEditorApi api,
            AudioMixer mixer,
            object group,
            int insertionIndex,
            params string[] candidateNames)
        {
            for (int index = 0;
                 index < candidateNames.Length;
                 index++)
            {
                string effectName = candidateNames[index];
                if (!api.EffectExists(effectName))
                {
                    continue;
                }

                return api.EnsureEffect(
                    mixer,
                    group,
                    effectName,
                    insertionIndex,
                    null);
            }

            throw new InvalidOperationException(
                "Required AudioMixer effect is unavailable: " +
                string.Join(" / ", candidateNames));
        }

        private static object EnsureRequiredSend(
            MixerEditorApi api,
            AudioMixer mixer,
            object group,
            object receiveEffect)
        {
            if (receiveEffect == null)
            {
                throw new InvalidOperationException(
                    "Required AudioMixer Receive target is unavailable.");
            }

            if (!api.EffectExists("Send"))
            {
                throw new InvalidOperationException(
                    "Required AudioMixer Send effect is unavailable.");
            }

            return api.EnsureEffect(
                mixer,
                group,
                "Send",
                int.MaxValue,
                receiveEffect);
        }

        private static void SetSendLevel(
            MixerEditorApi api,
            object send,
            object controller,
            object snapshot,
            float value)
        {
            if (send != null)
            {
                api.SetEffectMixLevel(
                    send,
                    controller,
                    snapshot,
                    value);
            }
        }

        private static object FindNamedObject(
            IEnumerable<object> values,
            string name)
        {
            foreach (object value in values)
            {
                if (value != null &&
                    string.Equals(
                        GetObjectName(value),
                        name,
                        StringComparison.Ordinal))
                {
                    return value;
                }
            }

            return null;
        }

        private static Dictionary<string, object> IndexNamedObjects(
            IEnumerable<object> values)
        {
            var result = new Dictionary<string, object>(
                StringComparer.Ordinal);
            foreach (object value in values)
            {
                if (value == null)
                {
                    continue;
                }

                string name = GetObjectName(value);
                if (!result.ContainsKey(name))
                {
                    result.Add(name, value);
                }
            }

            return result;
        }

        private static string GetObjectName(object value)
        {
            return ((Object)value).name;
        }

        private static void SetObjectName(object value, string name)
        {
            Object unityObject = (Object)value;
            if (unityObject.name == name)
            {
                return;
            }

            unityObject.name = name;
            EditorUtility.SetDirty(unityObject);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            int separator = path.LastIndexOf('/');
            if (separator <= 0)
            {
                throw new InvalidOperationException(
                    $"Cannot create asset folder '{path}'.");
            }

            string parent = path.Substring(0, separator);
            string name = path.Substring(separator + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private readonly struct SceneMix
        {
            public SceneMix(
                string snapshotName,
                float musicReverbSendDb,
                float detailsReverbSendDb,
                float worldReverbSendDb,
                float detailsEchoSendDb,
                float worldEchoSendDb,
                float decayTime,
                float decayHighFrequencyRatio,
                float roomLevel,
                float roomHighFrequencyLevel,
                float reverbLevel)
            {
                SnapshotName = snapshotName;
                MusicReverbSendDb = musicReverbSendDb;
                DetailsReverbSendDb = detailsReverbSendDb;
                WorldReverbSendDb = worldReverbSendDb;
                DetailsEchoSendDb = detailsEchoSendDb;
                WorldEchoSendDb = worldEchoSendDb;
                DecayTime = decayTime;
                DecayHighFrequencyRatio =
                    decayHighFrequencyRatio;
                RoomLevel = roomLevel;
                RoomHighFrequencyLevel =
                    roomHighFrequencyLevel;
                ReverbLevel = reverbLevel;
            }

            public string SnapshotName { get; }
            public float MusicReverbSendDb { get; }
            public float DetailsReverbSendDb { get; }
            public float WorldReverbSendDb { get; }
            public float DetailsEchoSendDb { get; }
            public float WorldEchoSendDb { get; }
            public float DecayTime { get; }
            public float DecayHighFrequencyRatio { get; }
            public float RoomLevel { get; }
            public float RoomHighFrequencyLevel { get; }
            public float ReverbLevel { get; }
        }

        private sealed class MixerEditorApi
        {
            private const BindingFlags InstanceFlags =
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance;
            private const BindingFlags StaticFlags =
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Static;

            private readonly Type controllerType;
            private readonly Type groupType;
            private readonly Type effectType;
            private readonly Type effectDefinitionsType;

            private readonly MethodInfo createMixer;
            private readonly MethodInfo createGroup;
            private readonly MethodInfo addChildToParent;
            private readonly MethodInfo getAllGroups;
            private readonly MethodInfo cloneSnapshot;
            private readonly MethodInfo notifySubAssetChanged;
            private readonly MethodInfo removeEffect;
            private readonly MethodInfo insertEffect;
            private readonly MethodInfo preallocateEffectGuids;
            private readonly MethodInfo setGroupVolume;
            private readonly MethodInfo setEffectMixLevel;
            private readonly MethodInfo setEffectParameter;
            private readonly MethodInfo effectExists;
            private readonly MethodInfo refreshEffectDefinitions;
            private readonly MethodInfo getEffectParameters;

            private readonly PropertyInfo masterGroup;
            private readonly PropertyInfo groupChildren;
            private readonly PropertyInfo groupEffects;
            private readonly PropertyInfo snapshots;
            private readonly PropertyInfo targetSnapshot;
            private readonly PropertyInfo startSnapshot;
            private readonly PropertyInfo effectName;
            private readonly PropertyInfo sendTarget;

            private readonly ConstructorInfo effectConstructor;

            public MixerEditorApi()
            {
                controllerType = GetRequiredType(
                    "UnityEditor.Audio.AudioMixerController");
                groupType = GetRequiredType(
                    "UnityEditor.Audio.AudioMixerGroupController");
                effectType = GetRequiredType(
                    "UnityEditor.Audio.AudioMixerEffectController");
                effectDefinitionsType = GetRequiredType(
                    "UnityEditor.Audio.MixerEffectDefinitions");

                createMixer = GetRequiredMethod(
                    controllerType,
                    "CreateMixerControllerAtPath",
                    1,
                    StaticFlags);
                createGroup = GetRequiredMethod(
                    controllerType,
                    "CreateNewGroup",
                    2,
                    InstanceFlags);
                addChildToParent = GetRequiredMethod(
                    controllerType,
                    "AddChildToParent",
                    2,
                    InstanceFlags);
                getAllGroups = GetRequiredMethod(
                    controllerType,
                    "GetAllAudioGroupsSlow",
                    0,
                    InstanceFlags);
                cloneSnapshot = GetRequiredMethod(
                    controllerType,
                    "CloneNewSnapshotFromTarget",
                    1,
                    InstanceFlags);
                notifySubAssetChanged = GetRequiredMethod(
                    controllerType,
                    "OnSubAssetChanged",
                    0,
                    InstanceFlags);
                removeEffect = GetRequiredMethod(
                    controllerType,
                    "RemoveEffect",
                    2,
                    InstanceFlags);
                insertEffect = GetRequiredMethod(
                    groupType,
                    "InsertEffect",
                    2,
                    InstanceFlags);
                preallocateEffectGuids = GetRequiredMethod(
                    effectType,
                    "PreallocateGUIDs",
                    0,
                    InstanceFlags);
                setGroupVolume = GetRequiredMethod(
                    groupType,
                    "SetValueForVolume",
                    3,
                    InstanceFlags);
                setEffectMixLevel = GetRequiredMethod(
                    effectType,
                    "SetValueForMixLevel",
                    3,
                    InstanceFlags);
                setEffectParameter = GetRequiredMethod(
                    effectType,
                    "SetValueForParameter",
                    4,
                    InstanceFlags);
                effectExists = GetRequiredMethod(
                    effectDefinitionsType,
                    "EffectExists",
                    1,
                    StaticFlags);
                refreshEffectDefinitions = GetRequiredMethod(
                    effectDefinitionsType,
                    "Refresh",
                    0,
                    StaticFlags);
                getEffectParameters = GetRequiredMethod(
                    effectDefinitionsType,
                    "GetEffectParameters",
                    1,
                    StaticFlags);

                masterGroup = GetRequiredProperty(
                    controllerType,
                    "masterGroup");
                groupChildren = GetRequiredProperty(
                    groupType,
                    "children");
                groupEffects = GetRequiredProperty(
                    groupType,
                    "effects");
                snapshots = GetRequiredProperty(
                    controllerType,
                    "snapshots");
                targetSnapshot = GetRequiredProperty(
                    controllerType,
                    "TargetSnapshot");
                startSnapshot = GetRequiredProperty(
                    controllerType,
                    "startSnapshot");
                effectName = GetRequiredProperty(
                    effectType,
                    "effectName");
                sendTarget = GetRequiredProperty(
                    effectType,
                    "sendTarget");

                effectConstructor = effectType.GetConstructor(
                    InstanceFlags,
                    null,
                    new[] { typeof(string) },
                    null);
                if (effectConstructor == null)
                {
                    throw new MissingMethodException(
                        effectType.FullName,
                        ".ctor(string)");
                }
            }

            public void RefreshEffectDefinitions()
            {
                Invoke(refreshEffectDefinitions, null);
            }

            public object CreateMixer(string path)
            {
                return Invoke(createMixer, null, path);
            }

            public object FindController(
                AudioMixer mixer,
                string path)
            {
                if (controllerType.IsInstanceOfType(mixer))
                {
                    return mixer;
                }

                Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
                for (int index = 0; index < assets.Length; index++)
                {
                    if (controllerType.IsInstanceOfType(assets[index]))
                    {
                        return assets[index];
                    }
                }

                throw new InvalidOperationException(
                    $"'{path}' is not backed by Unity's AudioMixerController.");
            }

            public object GetMasterGroup(object controller)
            {
                return masterGroup.GetValue(controller);
            }

            public object[] GetGroupChildren(object group)
            {
                return ToObjectArray(groupChildren.GetValue(group));
            }

            public object[] GetAllGroups(object controller)
            {
                return ToObjectArray(Invoke(getAllGroups, controller));
            }

            public object CreateGroup(object controller, string name)
            {
                return Invoke(createGroup, controller, name, false);
            }

            public void AddChildToParent(
                object controller,
                object child,
                object parent)
            {
                Invoke(
                    addChildToParent,
                    controller,
                    child,
                    parent);
            }

            public object[] GetSnapshots(object controller)
            {
                return ToObjectArray(snapshots.GetValue(controller));
            }

            public object CloneSnapshot(
                object controller,
                object source,
                string name)
            {
                var before = new HashSet<Object>();
                object[] existing = GetSnapshots(controller);
                for (int index = 0; index < existing.Length; index++)
                {
                    before.Add((Object)existing[index]);
                }

                SetTargetSnapshot(controller, source);
                Invoke(cloneSnapshot, controller, false);
                object[] updated = GetSnapshots(controller);
                for (int index = 0; index < updated.Length; index++)
                {
                    Object candidate = (Object)updated[index];
                    if (before.Contains(candidate))
                    {
                        continue;
                    }

                    SetObjectName(candidate, name);
                    return candidate;
                }

                throw new InvalidOperationException(
                    $"Unity did not create mixer snapshot '{name}'.");
            }

            public void SetTargetSnapshot(
                object controller,
                object snapshot)
            {
                targetSnapshot.SetValue(controller, snapshot);
            }

            public void SetStartSnapshot(
                object controller,
                object snapshot)
            {
                startSnapshot.SetValue(controller, snapshot);
            }

            public bool EffectExists(string name)
            {
                return (bool)Invoke(
                    effectExists,
                    null,
                    name);
            }

            public object EnsureEffect(
                AudioMixer mixer,
                object group,
                string name,
                int insertionIndex,
                object requiredSendTarget)
            {
                object[] effects = GetEffects(group);
                for (int index = 0; index < effects.Length; index++)
                {
                    object effect = effects[index];
                    if (!string.Equals(
                            GetEffectName(effect),
                            name,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (name == "Send" &&
                        !ReferenceEquals(
                            sendTarget.GetValue(effect),
                            requiredSendTarget))
                    {
                        continue;
                    }

                    return effect;
                }

                object created = effectConstructor.Invoke(
                    new object[] { name });
                Invoke(preallocateEffectGuids, created);
                if (requiredSendTarget != null)
                {
                    sendTarget.SetValue(created, requiredSendTarget);
                }

                AssetDatabase.AddObjectToAsset((Object)created, mixer);
                int safeIndex = Mathf.Clamp(
                    insertionIndex,
                    0,
                    effects.Length);
                Invoke(
                    insertEffect,
                    group,
                    created,
                    safeIndex);
                EditorUtility.SetDirty((Object)created);
                EditorUtility.SetDirty((Object)group);
                EditorUtility.SetDirty(mixer);
                return created;
            }

            public void RemoveEffectsByName(
                object controller,
                object group,
                string name)
            {
                object[] effects = GetEffects(group);
                for (int index = effects.Length - 1; index >= 0; index--)
                {
                    if (!string.Equals(
                            GetEffectName(effects[index]),
                            name,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    Invoke(
                        removeEffect,
                        controller,
                        effects[index],
                        group);
                }
            }

            public void SetGroupVolume(
                object group,
                object controller,
                object snapshot,
                float value)
            {
                Invoke(
                    setGroupVolume,
                    group,
                    controller,
                    snapshot,
                    value);
                EditorUtility.SetDirty((Object)group);
                EditorUtility.SetDirty((Object)snapshot);
            }

            public void SetEffectMixLevel(
                object effect,
                object controller,
                object snapshot,
                float value)
            {
                Invoke(
                    setEffectMixLevel,
                    effect,
                    controller,
                    snapshot,
                    value);
                EditorUtility.SetDirty((Object)effect);
                EditorUtility.SetDirty((Object)snapshot);
            }

            public bool TrySetEffectParameter(
                object effect,
                object controller,
                object snapshot,
                float value,
                params string[] candidateNames)
            {
                string actualName = FindEffectParameter(
                    GetEffectName(effect),
                    candidateNames,
                    out float minimum,
                    out float maximum);
                if (string.IsNullOrEmpty(actualName))
                {
                    return false;
                }

                float safeValue = minimum <= maximum
                    ? Mathf.Clamp(value, minimum, maximum)
                    : value;
                Invoke(
                    setEffectParameter,
                    effect,
                    controller,
                    snapshot,
                    actualName,
                    safeValue);
                EditorUtility.SetDirty((Object)effect);
                EditorUtility.SetDirty((Object)snapshot);
                return true;
            }

            public void NotifySubAssetChanged(object controller)
            {
                Invoke(notifySubAssetChanged, controller);
                EditorUtility.SetDirty((Object)controller);
            }

            private object[] GetEffects(object group)
            {
                return ToObjectArray(groupEffects.GetValue(group));
            }

            private string GetEffectName(object effect)
            {
                return (string)effectName.GetValue(effect);
            }

            private string FindEffectParameter(
                string effect,
                IReadOnlyList<string> candidates,
                out float minimum,
                out float maximum)
            {
                minimum = 0f;
                maximum = -1f;
                Array definitions = (Array)Invoke(
                    getEffectParameters,
                    null,
                    effect);
                for (int definitionIndex = 0;
                     definitionIndex < definitions.Length;
                     definitionIndex++)
                {
                    object definition =
                        definitions.GetValue(definitionIndex);
                    Type definitionType = definition.GetType();
                    FieldInfo nameField =
                        definitionType.GetField("name");
                    FieldInfo minimumField =
                        definitionType.GetField("minRange");
                    FieldInfo maximumField =
                        definitionType.GetField("maxRange");
                    if (nameField == null ||
                        minimumField == null ||
                        maximumField == null)
                    {
                        continue;
                    }

                    string parameterName =
                        (string)nameField.GetValue(definition);
                    string normalizedParameter =
                        NormalizeName(parameterName);
                    for (int candidateIndex = 0;
                         candidateIndex < candidates.Count;
                         candidateIndex++)
                    {
                        if (!string.Equals(
                                normalizedParameter,
                                NormalizeName(
                                    candidates[candidateIndex]),
                                StringComparison.Ordinal))
                        {
                            continue;
                        }

                        minimum = Convert.ToSingle(
                            minimumField.GetValue(definition));
                        maximum = Convert.ToSingle(
                            maximumField.GetValue(definition));
                        return parameterName;
                    }
                }

                return null;
            }

            private static string NormalizeName(string value)
            {
                if (string.IsNullOrEmpty(value))
                {
                    return string.Empty;
                }

                var characters = new char[value.Length];
                int count = 0;
                for (int index = 0; index < value.Length; index++)
                {
                    char character = value[index];
                    if (!char.IsLetterOrDigit(character))
                    {
                        continue;
                    }

                    characters[count++] =
                        char.ToUpperInvariant(character);
                }

                return new string(characters, 0, count);
            }

            private static Type GetRequiredType(string name)
            {
                Type type = Type.GetType(
                    name + ", UnityEditor",
                    false);
                if (type == null)
                {
                    throw new TypeLoadException(
                        $"Unity Editor audio API type '{name}' is unavailable.");
                }

                return type;
            }

            private static MethodInfo GetRequiredMethod(
                Type type,
                string name,
                int parameterCount,
                BindingFlags flags)
            {
                MethodInfo[] methods = type.GetMethods(flags);
                for (int index = 0; index < methods.Length; index++)
                {
                    MethodInfo method = methods[index];
                    if (method.Name == name &&
                        method.GetParameters().Length == parameterCount)
                    {
                        return method;
                    }
                }

                throw new MissingMethodException(type.FullName, name);
            }

            private static PropertyInfo GetRequiredProperty(
                Type type,
                string name)
            {
                PropertyInfo property =
                    type.GetProperty(name, InstanceFlags);
                if (property == null)
                {
                    throw new MissingMemberException(
                        type.FullName,
                        name);
                }

                return property;
            }

            private static object Invoke(
                MethodInfo method,
                object target,
                params object[] arguments)
            {
                try
                {
                    return method.Invoke(target, arguments);
                }
                catch (TargetInvocationException exception)
                {
                    throw new InvalidOperationException(
                        $"{method.DeclaringType?.FullName}.{method.Name} failed.",
                        exception.InnerException ?? exception);
                }
            }

            private static object[] ToObjectArray(object values)
            {
                if (values == null)
                {
                    return Array.Empty<object>();
                }

                var result = new List<object>();
                foreach (object value in (IEnumerable)values)
                {
                    result.Add(value);
                }

                return result.ToArray();
            }
        }
    }
}
