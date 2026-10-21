using System;
using System.Linq;
using BarPromenade.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BarPromenade.Editor
{
    [InitializeOnLoad]
    public static class ScenePresentationAssetSetup
    {
        private const string RendererPath = "Assets/Settings/PC_Renderer.asset";
        private const string MaterialPath =
            "Assets/Resources/Materials/SceneComposite.mat";
        private const string BaselineProfilePath = "Assets/Settings/PCPresentationBaselineVolumeProfile.asset";

        static ScenePresentationAssetSetup()
        {
            // Same guard as every other auto-setup: headless compiles
            // and test runs must not mutate PC_Renderer.asset or fail
            // error-sensitive batch runs over a missing material.
            if (!Application.isBatchMode)
            {
                EditorApplication.delayCall += EnsureInstalledSilently;
            }
        }

        [MenuItem("Bar Promenade/Rendering/Install Scene Presentation")]
        public static void Run()
        {
            EnsureInstalled();
            Debug.Log(
                "Native scene presentation and PC renderer feature are configured.");
        }

        private static void EnsureInstalledSilently()
        {
            try
            {
                EnsureInstalled();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Could not install Scene presentation: {exception}");
            }
        }

        public static void EnsureInstalled()
        {
            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            UniversalRendererData rendererData =
                AssetDatabase.LoadAssetAtPath<UniversalRendererData>(
                    RendererPath);

            if (material == null || material.shader == null)
            {
                throw new InvalidOperationException(
                    $"Scene composite material is invalid at '{MaterialPath}'.");
            }

            if (rendererData == null)
            {
                throw new InvalidOperationException(
                    $"PC renderer is missing at '{RendererPath}'.");
            }

            SceneCompositeRendererFeature feature = rendererData.rendererFeatures
                .OfType<SceneCompositeRendererFeature>()
                .FirstOrDefault();
            bool changed = false;
            bool created = feature == null;
            if (created)
            {
                feature =
                    ScriptableObject.CreateInstance<SceneCompositeRendererFeature>();
                feature.name = "Scene Composite";
                feature.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(feature, rendererData);
                rendererData.rendererFeatures.Add(feature);
            }
            if (created || feature.CompositeMaterial != material)
            {
                feature.SetConfiguration(material);
                EditorUtility.SetDirty(feature);
                changed = true;
            }

            SceneScreenSpaceReflectionFeature reflection = rendererData.rendererFeatures
                .OfType<SceneScreenSpaceReflectionFeature>().FirstOrDefault();
            if (reflection == null)
            {
                reflection = ScriptableObject.CreateInstance<SceneScreenSpaceReflectionFeature>();
                reflection.name = "Scene Screen Space Reflections";
                reflection.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(reflection, rendererData);
                rendererData.rendererFeatures.Add(reflection);
                created = true;
                changed = true;
            }
            if (reflection.afterOpaque || !reflection.isActive)
            {
                reflection.afterOpaque = false;
                reflection.SetActive(true);
                EditorUtility.SetDirty(reflection);
                changed = true;
            }
            changed |= EnsureReflectionBaseline();

            // Persist new sub-assets before resolving their nonzero local IDs.
            if (created)
            {
                EditorUtility.SetDirty(rendererData);
                AssetDatabase.SaveAssets();
            }
            changed |= UpdateFeatureMap(rendererData);
            if (changed)
            {
                rendererData.SetDirty();
                EditorUtility.SetDirty(rendererData);
                AssetDatabase.SaveAssets();
            }
        }

        private static bool EnsureReflectionBaseline()
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(BaselineProfilePath);
            if (profile == null) throw new InvalidOperationException("PC presentation baseline profile is missing.");
            bool created = !profile.TryGet(out ScreenSpaceReflectionVolumeSettings settings);
            if (created)
            {
                settings = profile.Add<ScreenSpaceReflectionVolumeSettings>(true);
                settings.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
                AssetDatabase.AddObjectToAsset(settings, profile);
            }
            bool changed = created || !settings.active || !settings.mode.overrideState ||
                settings.mode.value != ScreenSpaceReflectionVolumeSettings.ReflectionMode.Disabled;
            if (!changed) return false;
            settings.active = true;
            // Native default is OpaquesOnly: explicitly opt every other scene out.
            settings.mode.Override(ScreenSpaceReflectionVolumeSettings.ReflectionMode.Disabled);
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(profile);
            return true;
        }

        private static bool UpdateFeatureMap(
            UniversalRendererData rendererData)
        {
            SerializedObject serializedRenderer =
                new SerializedObject(rendererData);
            serializedRenderer.Update();
            SerializedProperty features =
                serializedRenderer.FindProperty("m_RendererFeatures");
            SerializedProperty featureMap =
                serializedRenderer.FindProperty("m_RendererFeatureMap");

            bool changed = featureMap.arraySize != features.arraySize;
            featureMap.arraySize = features.arraySize;
            for (int index = 0; index < features.arraySize; index++)
            {
                UnityEngine.Object feature = features
                    .GetArrayElementAtIndex(index)
                    .objectReferenceValue;
                long localId = 0;
                if (feature != null)
                {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        feature,
                        out _,
                        out localId);
                }

                SerializedProperty entry =
                    featureMap.GetArrayElementAtIndex(index);
                if (entry.longValue != localId)
                {
                    entry.longValue = localId;
                    changed = true;
                }
            }

            if (changed)
            {
                serializedRenderer.ApplyModifiedPropertiesWithoutUndo();
            }

            return changed;
        }
    }
}
