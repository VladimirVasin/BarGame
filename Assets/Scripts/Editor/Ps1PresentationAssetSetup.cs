using System;
using System.Linq;
using BarPromenade.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BarPromenade.Editor
{
    [InitializeOnLoad]
    public static class Ps1PresentationAssetSetup
    {
        private const string RendererPath = "Assets/Settings/PC_Renderer.asset";
        private const string ProfilePath =
            "Assets/Resources/Rendering/Ps1PresentationProfile.asset";
        private const string MaterialPath =
            "Assets/Resources/Materials/Ps1Composite.mat";

        static Ps1PresentationAssetSetup()
        {
            EditorApplication.delayCall += EnsureInstalledSilently;
        }

        [MenuItem("Bar Promenade/Rendering/Install PS1 Presentation")]
        public static void Run()
        {
            EnsureInstalled();
            Debug.Log(
                "PS1 presentation profile and PC renderer feature are configured.");
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
                    $"Could not install PS1 presentation: {exception}");
            }
        }

        private static void EnsureInstalled()
        {
            Ps1PresentationSettings profile =
                AssetDatabase.LoadAssetAtPath<Ps1PresentationSettings>(
                    ProfilePath);
            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            UniversalRendererData rendererData =
                AssetDatabase.LoadAssetAtPath<UniversalRendererData>(
                    RendererPath);

            if (profile == null)
            {
                throw new InvalidOperationException(
                    $"PS1 presentation profile is missing at '{ProfilePath}'.");
            }

            if (material == null || material.shader == null)
            {
                throw new InvalidOperationException(
                    $"PS1 composite material is invalid at '{MaterialPath}'.");
            }

            if (rendererData == null)
            {
                throw new InvalidOperationException(
                    $"PC renderer is missing at '{RendererPath}'.");
            }

            Ps1CompositeRendererFeature feature = rendererData.rendererFeatures
                .OfType<Ps1CompositeRendererFeature>()
                .FirstOrDefault();
            bool created = feature == null;
            if (created)
            {
                feature =
                    ScriptableObject.CreateInstance<Ps1CompositeRendererFeature>();
                feature.name = "PS1 Composite";
                feature.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(feature, rendererData);
                rendererData.rendererFeatures.Add(feature);
            }

            bool mapChanged = UpdateFeatureMap(rendererData);
            if (!created &&
                !mapChanged &&
                feature.PresentationSettings == profile &&
                feature.CompositeMaterial == material)
            {
                return;
            }

            feature.SetConfiguration(profile, material);
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
