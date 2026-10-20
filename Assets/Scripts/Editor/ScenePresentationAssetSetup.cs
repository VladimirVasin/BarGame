using System;
using System.Linq;
using BarPromenade.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BarPromenade.Editor
{
    [InitializeOnLoad]
    public static class ScenePresentationAssetSetup
    {
        private const string RendererPath = "Assets/Settings/PC_Renderer.asset";
        private const string MaterialPath =
            "Assets/Resources/Materials/SceneComposite.mat";

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

        private static void EnsureInstalled()
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
            bool created = feature == null;
            if (created)
            {
                feature =
                    ScriptableObject.CreateInstance<SceneCompositeRendererFeature>();
                feature.name = "Scene Composite";
                feature.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(feature, rendererData);
                rendererData.rendererFeatures.Add(feature);
                feature.SetConfiguration(material);
                EditorUtility.SetDirty(feature);
                EditorUtility.SetDirty(rendererData);
                // Persist the new sub-asset BEFORE the feature map is
                // stamped: an unsaved sub-asset yields localId 0 and
                // poisons the map until the next reload rebuilds it.
                AssetDatabase.SaveAssets();
            }

            bool mapChanged = UpdateFeatureMap(rendererData);
            if (created)
            {
                if (mapChanged)
                {
                    AssetDatabase.SaveAssets();
                }

                return;
            }

            if (!mapChanged &&
                feature.CompositeMaterial == material)
            {
                return;
            }

            feature.SetConfiguration(material);
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();
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
