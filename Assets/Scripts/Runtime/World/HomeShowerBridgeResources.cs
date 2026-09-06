using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The three Blender-authored bridge pieces the shower scene shows on
    /// the undressed hero (a shoulder yoke and two deltoid caps), loaded
    /// from Resources the way the toilet's anatomy is: the FBX keeps its
    /// own authoring root under a runtime pivot, and only the pivot is
    /// ever moved. Nothing here is generated at runtime.
    /// </summary>
    public static class HomeShowerBridgeResources
    {
        public const string ResourceFolder = "HomeShowerAction/Models/";
        public const string ShoulderYoke = "ShoulderYoke";
        public const string DeltoidLeft = "DeltoidLeft";
        public const string DeltoidRight = "DeltoidRight";

        public static readonly string[] ModelNames =
        {
            ShoulderYoke,
            DeltoidLeft,
            DeltoidRight
        };

        public static GameObject LoadTemplate(string modelName)
        {
            if (string.IsNullOrEmpty(modelName))
            {
                throw new ArgumentException(
                    "A bridge model name is required.",
                    nameof(modelName));
            }

            return Resources.Load<GameObject>(ResourceFolder + modelName);
        }

        /// <summary>
        /// Instantiates one authored piece under a fresh pivot parented to
        /// <paramref name="parent"/>; false when the FBX has not been
        /// built, so the caller can refuse the scene instead of showing a
        /// hero with holes in him.
        /// </summary>
        public static bool TryCreate(
            string modelName,
            Transform parent,
            out Transform pivot)
        {
            pivot = null;
            GameObject template = LoadTemplate(modelName);
            if (template == null)
            {
                return false;
            }

            pivot = new GameObject("Home Shower " + modelName + " Pivot").transform;
            pivot.SetParent(parent, false);
            // Retain the FBX's authored unit factor on its own root.
            GameObject instance = UnityEngine.Object.Instantiate(template, pivot, false);
            instance.name = "Blender Authored " + modelName;
            Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < colliders.Length; index++)
            {
                colliders[index].enabled = false;
            }

            return true;
        }
    }
}
