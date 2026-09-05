using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Attach only the dedicated street reaction clips to existing models.</summary>
    [InitializeOnLoad]
    public static class CityPedestrianPersonalSpaceAssetSetup
    {
        public const string AnimationPath =
            "Assets/Pedestrians/Animations/CityPedestrianPersonalSpace.fbx";
        public const string ManifestPath =
            "Assets/Pedestrians/Animations/CityPedestrianPersonalSpace.json";
        private static bool buildQueued;
        private static bool isBuilding;

        static CityPedestrianPersonalSpaceAssetSetup()
        {
            if (!Application.isBatchMode)
            {
                QueueBuild();
            }
        }

        public static bool IsPersonalSpaceClip(string name)
        {
            return !string.IsNullOrEmpty(name) &&
                (name.EndsWith("_PersonalSpaceGuard", StringComparison.Ordinal) ||
                 name.EndsWith("_PersonalSpaceShove", StringComparison.Ordinal));
        }

        public static void QueueBuild()
        {
            if (buildQueued || isBuilding || !File.Exists(AnimationPath) ||
                !File.Exists(ManifestPath))
            {
                return;
            }
            buildQueued = true;
            EditorApplication.delayCall += RunQueuedBuild;
        }

        private static void RunQueuedBuild()
        {
            buildQueued = false;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                NpcHumanV2AssetSetup.IsAnyPipelineBuilding || Player3DV2AssetSetup.IsBuilding)
            {
                QueueBuild();
                return;
            }
            BuildOrThrow();
        }

        [MenuItem("Bar Promenade/City Pedestrian 3D/Build Personal Space Clips")]
        public static void BuildOrThrow()
        {
            if (isBuilding)
            {
                return;
            }
            isBuilding = true;
            try
            {
                AssetDatabase.ImportAsset(AnimationPath, ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceSynchronousImport);
                ValidateBank();
                foreach (CityPedestrianArchetype archetype in CityPedestrianResources.Archetypes)
                {
                    string path = "Assets/Resources/" + archetype.PrefabResourcePath + ".prefab";
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null)
                    {
                        throw new InvalidOperationException("Missing roaming prefab: " + path);
                    }
                    CityPedestrianAssetRegistry source = prefab.GetComponent<CityPedestrianAssetRegistry>();
                    AnimationClip guard = LoadClip(archetype.DesignId, "Guard");
                    AnimationClip shove = LoadClip(archetype.DesignId, "Shove");
                    if (source != null && source.PersonalSpaceGuardClip == guard &&
                        source.PersonalSpaceShoveClip == shove)
                    {
                        continue;
                    }
                    GameObject contents = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        CityPedestrianAssetRegistry registry = contents.GetComponent<CityPedestrianAssetRegistry>();
                        registry.ConfigurePersonalSpaceClips(guard, shove);
                        PrefabUtility.SaveAsPrefabAsset(contents, path);
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(contents);
                    }
                }
                AssetDatabase.SaveAssets();
            }
            finally
            {
                isBuilding = false;
            }
        }

        public static void ConfigureRegistry(CityPedestrianAssetRegistry registry)
        {
            if (!CityPedestrianResources.Roams(registry.DesignId))
            {
                return;
            }
            registry.ConfigurePersonalSpaceClips(
                LoadClip(registry.DesignId, "Guard"), LoadClip(registry.DesignId, "Shove"));
        }

        private static AnimationClip LoadClip(string designId, string suffix)
        {
            string name = designId + "_PersonalSpace" + suffix;
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(AnimationPath)
                .OfType<AnimationClip>().SingleOrDefault(candidate => candidate.name == name);
            if (clip == null || clip.isLooping || clip.events.Length != 0 ||
                Mathf.Abs(clip.length - 1f) > 0.002f)
            {
                throw new InvalidOperationException("Invalid personal-space one-shot: " + name);
            }
            return clip;
        }

        private static void ValidateBank()
        {
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || manifest.bone_count != 31 || manifest.mesh_count != 0 ||
                manifest.root_motion || manifest.fps != 24 || manifest.clips == null ||
                manifest.clip_count != CityPedestrianResources.Archetypes.Count * 2 ||
                manifest.clips.Length != manifest.clip_count)
            {
                throw new InvalidOperationException("Personal-space bank must contain only the six grounded bone-only pairs.");
            }
            foreach (CityPedestrianArchetype archetype in CityPedestrianResources.Archetypes)
            {
                foreach (string suffix in new[] { "Guard", "Shove" })
                {
                    string name = archetype.DesignId + "_PersonalSpace" + suffix;
                    ClipManifest clip = manifest.clips.SingleOrDefault(candidate => candidate.name == name);
                    if (clip == null || clip.archetype != archetype.DesignId || clip.loop ||
                        !clip.one_shot || !clip.in_place || clip.keyed_bone_count != 31 ||
                        clip.contact_frame != 8 || Mathf.Abs(clip.duration_seconds - 1f) > 0.0001f ||
                        Mathf.Abs(clip.contact_seconds - 1f / 3f) > 0.0001f ||
                        clip.contact_forward_palm_dot < 0.995f ||
                        clip.contact_upright_fingers_dot < 0.995f)
                    {
                        throw new InvalidOperationException("Invalid authored personal-space contact: " + name);
                    }
                    LoadClip(archetype.DesignId, suffix);
                }
            }
        }

        [Serializable]
        private sealed class Manifest
        {
            public int bone_count, mesh_count, fps, clip_count;
            public bool root_motion;
            public ClipManifest[] clips;
        }

        [Serializable]
        private sealed class ClipManifest
        {
            public string name, archetype;
            public bool loop, one_shot, in_place;
            public int keyed_bone_count, contact_frame;
            public float duration_seconds, contact_seconds;
            public float contact_forward_palm_dot, contact_upright_fingers_dot;
        }
    }
}
