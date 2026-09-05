using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Passive Blender-authored vessels; the world owns all presentation.</summary>
    public static class CityOffshoreBoatAssetProvider
    {
        public const string ResourceFolder = "City/OffshoreBoats/";
        public const int VariantCount = 2;
        private static readonly string[] ModelNames = { "OldTrawler", "OldMotorboat" };
        private static readonly GameObject[] Templates = new GameObject[VariantCount];

        public static string GetModelName(int variant)
        {
            ValidateVariant(variant);
            return ModelNames[variant];
        }

        /// <summary>
        /// Returns a unit-scale wrapper. Keep its imported child transform intact:
        /// importer axis/unit corrections belong to the imported hierarchy.
        /// The wrapper origin is the waterline and its forward is Unity +Z.
        /// </summary>
        public static GameObject Create(int variant, Transform parent)
        {
            ValidateVariant(variant);
            if (Templates[variant] == null)
            {
                Templates[variant] = Resources.Load<GameObject>(ResourceFolder + ModelNames[variant]);
                if (Templates[variant] == null)
                {
                    throw new InvalidOperationException("Missing offshore boat model: " + ModelNames[variant]);
                }
            }

            var wrapper = new GameObject(ModelNames[variant]);
            wrapper.transform.SetParent(parent, false);
            GameObject model = UnityEngine.Object.Instantiate(Templates[variant], wrapper.transform, false);
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }

            return wrapper;
        }

        public static Transform FindPart(GameObject model, string name)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(part.name, name, StringComparison.Ordinal))
                {
                    return part;
                }
            }

            throw new InvalidOperationException($"Offshore boat '{model.name}' has no '{name}' part.");
        }

        private static void ValidateVariant(int variant)
        {
            if (variant < 0 || variant >= VariantCount)
            {
                throw new ArgumentOutOfRangeException(nameof(variant));
            }
        }
    }
}
