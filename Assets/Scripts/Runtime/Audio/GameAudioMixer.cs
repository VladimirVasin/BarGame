using System;
using UnityEngine;
using UnityEngine.Audio;

namespace BarPromenade
{
    public enum GameAudioProfile
    {
        None = 0,
        City,
        Bar,
        Stairwell,
        Home,
        DoorTransition,
        Count
    }

    public enum GameAudioGroup
    {
        Master = 0,
        Music,
        AmbienceBeds,
        AmbienceDetails,
        SfxWorld,
        SfxGameplay,
        Ui,
        Count
    }

    public static class GameAudioMixer
    {
        // Canonical whole-game gain staging. These values are authored into
        // every snapshot, so a scene change can alter room character without
        // changing the player's hierarchy of what matters.
        public const float MasterHeadroomDb = -6f;
        public const float MusicGainDb = -5.5f;
        public const float AmbienceBedsGainDb = -4f;
        public const float AmbienceDetailsGainDb = 0.5f;
        public const float SfxWorldGainDb = 2f;
        public const float SfxGameplayGainDb = 2.5f;
        public const float UiGainDb = 1.5f;

        public const string ResourcePath =
            "Audio/Mixers/BarPromenadeAudio";

        public const string MasterGroupPath = "Master";
        public const string PerceptionGroupPath = "Master/Perception";
        public const string MusicGroupPath = "Master/Perception/Music";
        public const string AmbienceBedsGroupPath =
            "Master/Perception/Ambience/Beds";
        public const string AmbienceDetailsGroupPath =
            "Master/Perception/Ambience/Details";
        public const string SfxWorldGroupPath =
            "Master/Perception/SFX/World";
        public const string SfxGameplayGroupPath =
            "Master/Perception/SFX/Gameplay";
        public const string UiGroupPath = "Master/UI";

        public const string CitySnapshotName = "City";
        public const string BarSnapshotName = "Bar";
        public const string StairwellSnapshotName = "Stairwell";
        public const string HomeSnapshotName = "Home";
        public const string DoorTransitionSnapshotName =
            "DoorTransition";

        public const float SceneProfileTransitionSeconds = 0.25f;
        public const float DoorTransitionProfileTransitionSeconds = 0f;

        private static readonly string[] GroupPaths =
        {
            MasterGroupPath,
            MusicGroupPath,
            AmbienceBedsGroupPath,
            AmbienceDetailsGroupPath,
            SfxWorldGroupPath,
            SfxGameplayGroupPath,
            UiGroupPath
        };

        private static readonly string[] SnapshotNames =
        {
            string.Empty,
            CitySnapshotName,
            BarSnapshotName,
            StairwellSnapshotName,
            HomeSnapshotName,
            DoorTransitionSnapshotName
        };

        private static AudioMixer mixer;
        private static AudioMixerGroup[] groups;
        private static AudioMixerSnapshot[] snapshots;
        private static AudioMixerSnapshot currentSnapshot;
        private static bool loadAttempted;
        private static bool missingAssetWarningIssued;

        public static GameAudioProfile CurrentProfile
        {
            get;
            private set;
        }

        public static string CurrentSnapshotName =>
            GetSnapshotName(CurrentProfile);

        public static AudioMixer Mixer
        {
            get
            {
                EnsureLoaded();
                return mixer;
            }
        }

        public static AudioMixerSnapshot CurrentSnapshot =>
            currentSnapshot;

        public static bool IsAvailable => EnsureLoaded();

        public static bool HasCompleteConfiguration
        {
            get
            {
                if (!EnsureLoaded())
                {
                    return false;
                }

                for (int index = 0; index < groups.Length; index++)
                {
                    if (groups[index] == null)
                    {
                        return false;
                    }
                }

                for (int index = 1; index < snapshots.Length; index++)
                {
                    if (snapshots[index] == null)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public static AudioMixerGroup MasterGroup =>
            GetGroup(GameAudioGroup.Master);
        public static AudioMixerGroup MusicGroup =>
            GetGroup(GameAudioGroup.Music);
        public static AudioMixerGroup AmbienceBedsGroup =>
            GetGroup(GameAudioGroup.AmbienceBeds);
        public static AudioMixerGroup AmbienceDetailsGroup =>
            GetGroup(GameAudioGroup.AmbienceDetails);
        public static AudioMixerGroup SfxWorldGroup =>
            GetGroup(GameAudioGroup.SfxWorld);
        public static AudioMixerGroup SfxGameplayGroup =>
            GetGroup(GameAudioGroup.SfxGameplay);
        public static AudioMixerGroup UiGroup =>
            GetGroup(GameAudioGroup.Ui);

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            mixer = null;
            groups = null;
            snapshots = null;
            currentSnapshot = null;
            loadAttempted = false;
            missingAssetWarningIssued = false;
            CurrentProfile = GameAudioProfile.None;
        }

        public static string GetGroupPath(GameAudioGroup group)
        {
            int index = (int)group;
            return index >= 0 && index < GroupPaths.Length
                ? GroupPaths[index]
                : string.Empty;
        }

        public static float GetGroupGainDb(GameAudioGroup group)
        {
            switch (group)
            {
                case GameAudioGroup.Master:
                    return MasterHeadroomDb;
                case GameAudioGroup.Music:
                    return MusicGainDb;
                case GameAudioGroup.AmbienceBeds:
                    return AmbienceBedsGainDb;
                case GameAudioGroup.AmbienceDetails:
                    return AmbienceDetailsGainDb;
                case GameAudioGroup.SfxWorld:
                    return SfxWorldGainDb;
                case GameAudioGroup.SfxGameplay:
                    return SfxGameplayGainDb;
                case GameAudioGroup.Ui:
                    return UiGainDb;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(group),
                        group,
                        "The audio group has no canonical mix gain.");
            }
        }

        public static AudioMixerGroup GetGroup(GameAudioGroup group)
        {
            int index = (int)group;
            if (index < 0 ||
                index >= (int)GameAudioGroup.Count ||
                !EnsureLoaded())
            {
                return null;
            }

            return groups[index];
        }

        public static string GetSnapshotName(GameAudioProfile profile)
        {
            int index = (int)profile;
            return index >= 0 && index < SnapshotNames.Length
                ? SnapshotNames[index]
                : string.Empty;
        }

        public static AudioMixerSnapshot GetSnapshot(
            GameAudioProfile profile)
        {
            int index = (int)profile;
            if (index <= 0 ||
                index >= (int)GameAudioProfile.Count ||
                !EnsureLoaded())
            {
                return null;
            }

            return snapshots[index];
        }

        public static bool Route(
            AudioSource source,
            GameAudioGroup group)
        {
            if (source == null)
            {
                return false;
            }

            AudioMixerGroup output = GetGroup(group);
            if (output == null)
            {
                return false;
            }

            source.outputAudioMixerGroup = output;
            return true;
        }

        public static bool ApplyProfile(GameAudioProfile profile)
        {
            if (profile <= GameAudioProfile.None ||
                profile >= GameAudioProfile.Count)
            {
                return false;
            }

            AudioMixerSnapshot snapshot = GetSnapshot(profile);
            if (snapshot == null)
            {
                return false;
            }

            snapshot.TransitionTo(
                GetTransitionSeconds(profile));
            CurrentProfile = profile;
            currentSnapshot = snapshot;
            return true;
        }

        public static float GetTransitionSeconds(
            GameAudioProfile profile)
        {
            switch (profile)
            {
                case GameAudioProfile.City:
                case GameAudioProfile.Bar:
                case GameAudioProfile.Stairwell:
                case GameAudioProfile.Home:
                    return SceneProfileTransitionSeconds;
                case GameAudioProfile.DoorTransition:
                    return DoorTransitionProfileTransitionSeconds;
                default:
                    return 0f;
            }
        }

        private static bool EnsureLoaded()
        {
            if (loadAttempted)
            {
                return mixer != null;
            }

            loadAttempted = true;
            mixer = Resources.Load<AudioMixer>(ResourcePath);
            if (mixer == null)
            {
                WarnMissingAssetOnce();
                return false;
            }

            groups = new AudioMixerGroup[(int)GameAudioGroup.Count];
            for (int index = 0; index < groups.Length; index++)
            {
                groups[index] =
                    FindExactGroup(GroupPaths[index]);
            }

            snapshots =
                new AudioMixerSnapshot[(int)GameAudioProfile.Count];
            for (int index = 1; index < snapshots.Length; index++)
            {
                snapshots[index] =
                    mixer.FindSnapshot(SnapshotNames[index]);
            }

            return true;
        }

        private static AudioMixerGroup FindExactGroup(string path)
        {
            AudioMixerGroup[] matches =
                mixer.FindMatchingGroups(path);
            if (matches == null)
            {
                return null;
            }

            int separatorIndex = path.LastIndexOf('/');
            string leafName = separatorIndex >= 0
                ? path.Substring(separatorIndex + 1)
                : path;
            AudioMixerGroup exactMatch = null;
            for (int index = 0; index < matches.Length; index++)
            {
                if (matches[index].name != leafName)
                {
                    continue;
                }

                if (exactMatch != null)
                {
                    return null;
                }

                exactMatch = matches[index];
            }

            return exactMatch;
        }

        private static void WarnMissingAssetOnce()
        {
            if (missingAssetWarningIssued)
            {
                return;
            }

            missingAssetWarningIssued = true;
            Debug.LogWarning(
                "[Bar Promenade] Audio mixer is missing at Resources/" +
                ResourcePath +
                ". Audio will continue through Unity's default output.");
        }
    }
}
