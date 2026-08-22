using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace BarPromenade.Editor
{
    public static class NightPresentationAssetSetup
    {
        private const string CityScenePath = "Assets/Scenes/City.unity";
        private const string ProfilePath =
            "Assets/Settings/CityNoirVolumeProfile.asset";
        private const string MaterialFolder = "Assets/Resources/Materials";
        private const string MaterialPath =
            MaterialFolder + "/CityNoirEmission.mat";

        public static void Run()
        {
            EnsureFolder("Assets/Resources");
            EnsureFolder(MaterialFolder);

            Material material = EnsureEmissiveMaterial();
            VolumeProfile profile = EnsureVolumeProfile();
            AssignCityProfile(profile);

            EditorUtility.SetDirty(material);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "City noir Volume Profile and shared emissive material are configured.");
        }

        private static Material EnsureEmissiveMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "URP Unlit shader is unavailable.");
            }

            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "CityNoirEmission",
                    enableInstancing = true
                };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else
            {
                material.shader = shader;
                material.enableInstancing = true;
            }

            material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 0f);
            }

            return material;
        }

        private static VolumeProfile EnsureVolumeProfile()
        {
            VolumeProfile profile =
                AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "CityNoirVolumeProfile";
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            Tonemapping tonemapping = GetOrAdd<Tonemapping>(profile);
            tonemapping.active = true;
            tonemapping.mode.Override(TonemappingMode.ACES);

            Bloom bloom = GetOrAdd<Bloom>(profile);
            bloom.active = true;
            bloom.threshold.Override(0.60f);
            bloom.intensity.Override(0.62f);
            bloom.scatter.Override(0.48f);
            bloom.clamp.Override(10f);
            bloom.highQualityFiltering.Override(false);

            ColorAdjustments color = GetOrAdd<ColorAdjustments>(profile);
            color.active = true;
            color.postExposure.Override(0.62f);
            color.contrast.Override(-10f);
            color.saturation.Override(-24f);
            color.colorFilter.Override(
                new Color(0.94f, 1.00f, 0.97f, 1f));

            Vignette vignette = GetOrAdd<Vignette>(profile);
            vignette.active = true;
            vignette.intensity.Override(0.10f);
            vignette.smoothness.Override(0.48f);
            vignette.rounded.Override(false);

            FilmGrain grain = GetOrAdd<FilmGrain>(profile);
            grain.active = true;
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.015f);
            grain.response.Override(0.80f);

            // Mirrors RuntimeSceneSetup.CreateCityNoirRuntimeProfile:
            // the blur band must sit inside the 48 m far clip, where
            // the exponential-squared fog has not yet flattened depth.
            DepthOfField depthOfField = GetOrAdd<DepthOfField>(profile);
            depthOfField.active = true;
            depthOfField.mode.Override(DepthOfFieldMode.Gaussian);
            depthOfField.gaussianStart.Override(8f);
            depthOfField.gaussianEnd.Override(28f);
            depthOfField.gaussianMaxRadius.Override(1.5f);
            depthOfField.highQualitySampling.Override(false);

            return profile;
        }

        private static T GetOrAdd<T>(VolumeProfile profile)
            where T : VolumeComponent
        {
            if (profile.TryGet(out T component))
            {
                return component;
            }

            component = profile.Add<T>(true);
            component.hideFlags = HideFlags.HideInHierarchy |
                                  HideFlags.HideInInspector;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        private static void AssignCityProfile(VolumeProfile profile)
        {
            Scene scene = EditorSceneManager.OpenScene(
                CityScenePath,
                OpenSceneMode.Single);
            Volume volume = FindGlobalVolume();
            if (volume == null)
            {
                GameObject volumeObject = new GameObject("Global Volume");
                volume = volumeObject.AddComponent<Volume>();
                volume.isGlobal = true;
            }

            volume.sharedProfile = profile;
            volume.priority = 0f;
            volume.weight = 1f;
            EditorUtility.SetDirty(volume);

            Camera camera = Camera.main;
            if (camera == null)
            {
                camera = UnityEngine.Object.FindAnyObjectByType<Camera>();
            }

            if (camera == null)
            {
                throw new InvalidOperationException(
                    "City scene does not contain a camera.");
            }

            UniversalAdditionalCameraData cameraData =
                camera.GetComponent<UniversalAdditionalCameraData>();
            if (cameraData == null)
            {
                cameraData =
                    camera.gameObject.AddComponent<
                        UniversalAdditionalCameraData>();
            }

            cameraData.renderPostProcessing = true;
            EditorUtility.SetDirty(cameraData);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                throw new InvalidOperationException(
                    $"Failed to save '{CityScenePath}'.");
            }
        }

        private static Volume FindGlobalVolume()
        {
            Volume[] volumes =
                UnityEngine.Object.FindObjectsByType<Volume>(
                    FindObjectsInactive.Include);
            for (int index = 0; index < volumes.Length; index++)
            {
                if (volumes[index].isGlobal)
                {
                    return volumes[index];
                }
            }

            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            int separator = path.LastIndexOf('/');
            string parent = path.Substring(0, separator);
            string name = path.Substring(separator + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
