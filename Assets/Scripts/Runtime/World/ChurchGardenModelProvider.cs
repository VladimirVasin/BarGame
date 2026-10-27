using System;
using UnityEngine;

namespace BarPromenade
{
    public enum ChurchGardenAssetKind
    {
        FountainStone = 0,
        FountainWater = 1,
        FountainStream = 2,
        MaryStatue = 3,
        PotSmall = 4,
        PotMedium = 5,
        PotLarge = 6,
        StonePottingLedge = 7,
        HedgeSegment = 8,
        GardenUplight = 9,
        Bench = 10
    }

    /// <summary>
    /// Fixed-metre passive Blender pieces. Plans own every placement,
    /// collider and interaction; no primitive substitutes hide missing art.
    /// </summary>
    [CreateAssetMenu(fileName = "ChurchGardenModelProvider",
        menuName = "Bar Promenade/Church Garden Model Provider")]
    public sealed class ChurchGardenModelProvider : ScriptableObject
    {
        public const string ResourcePath = "ChurchGarden/ChurchGardenModelProvider";
        public const string DesignId = "church_garden_v1";
        public const float BenchSeatHeight = .49f;
        public const float BenchSeatWidth = 1.60f;
        public const float BenchSeatDepth = .42f;
        public static readonly Vector3 UplightLensLocalPosition =
            new Vector3(0f, .13009021f, .05440439f);
        public static readonly Vector3 UplightLensLocalDirection =
            new Vector3(0f, .57357644f, .81915204f);
        public const int UplightLensMaterialIndex = 1;

        [Serializable]
        private sealed class Piece
        {
            public ChurchGardenAssetKind kind;
            public GameObject prefab;
        }

        [SerializeField] private Piece[] pieces = Array.Empty<Piece>();
        [SerializeField] private string buildSignature = string.Empty;

        public string BuildSignature => buildSignature;

        public static ChurchGardenModelProvider Load()
        {
            return Resources.Load<ChurchGardenModelProvider>(ResourcePath);
        }

        public GameObject GetPrefab(ChurchGardenAssetKind kind)
        {
            for (int i = 0; i < pieces.Length; i++)
            {
                if (pieces[i] != null && pieces[i].kind == kind)
                    return pieces[i].prefab;
            }
            return null;
        }

        public bool IsComplete()
        {
            foreach (ChurchGardenAssetKind kind in Enum.GetValues(typeof(ChurchGardenAssetKind)))
            {
                if (GetPrefab(kind) == null)
                    return false;
            }
            return true;
        }

        public GameObject Instantiate(ChurchGardenAssetKind kind, Transform parent,
            Vector3 localPosition, float yaw = 0f)
        {
            GameObject prefab = GetPrefab(kind);
            if (prefab == null)
                throw new InvalidOperationException($"Church garden art is missing: {kind}.");

            GameObject instance = UnityEngine.Object.Instantiate(prefab, parent, false);
            instance.name = kind.ToString();
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            // Prefabs are measured fixed-metre meshes with unit transforms.
            // Keep this scale rather than separating an ordinary FBX root.
            ConfigureGroundWeather(instance.GetComponent<Renderer>());
            return instance;
        }

        private static void ConfigureGroundWeather(Renderer renderer)
        {
            if (renderer == null) return;
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null || material.shader == null ||
                    material.shader.name != "Bar Promenade/Layered Ground") continue;
                // Ground weather is a per-renderer field in the existing shader.
                // A nonempty MPB keeps these few prefab draws off BRG, whose
                // UnityPerMaterial-only binding cannot supply that field.
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                properties.SetFloat("_GroundCityClimate", 1f);
                renderer.SetPropertyBlock(properties);
                return;
            }
        }
    }
}
