using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>
    /// The mother's table uses the literal kettle worn by the Kettle Hat
    /// pedestrian. The complete source hierarchy stays intact so the rigid
    /// skinned pieces keep their authored bones, material and detail atlas;
    /// only the ten kettle renderers are allowed to remain visible.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MothersHouseKettleProp : MonoBehaviour
    {
        public const float DefaultUniformScale = 0.60f;

        private const float DockAlignmentTolerance = 0.002f;

        private static readonly string[] RequiredRendererNames =
        {
            "ACC_KettleBody",
            "ACC_KettleHandlePost.L",
            "ACC_KettleHandlePost.R",
            "ACC_KettleHandleTop",
            "ACC_KettleKnob",
            "ACC_KettleLid",
            "ACC_KettleRimBand",
            "ACC_KettleShoulder",
            "ACC_KettleSpout",
            "ACC_KettleSpoutTip"
        };

        [SerializeField] private GameObject sourceInstance;
        [SerializeField] private Renderer[] visibleRenderers =
            Array.Empty<Renderer>();
        [SerializeField] private float uniformScale;

        public GameObject SourceInstance => sourceInstance;
        public IReadOnlyList<Renderer> VisibleRenderers => visibleRenderers;
        public float UniformScale => uniformScale;
        public string SourceDesignId =>
            CityPedestrianResources.KettleHatDesignId;

        public static MothersHouseKettleProp Create(
            Transform parent,
            Transform teapotDock,
            float scale = DefaultUniformScale)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (teapotDock == null)
            {
                throw new ArgumentNullException(nameof(teapotDock));
            }

            if (!IsFinite(scale) || scale <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(scale),
                    "The table-kettle scale must be finite and positive.");
            }

            if (!CityPedestrianResources.TryGetArchetype(
                    CityPedestrianResources.KettleHatDesignId,
                    out CityPedestrianArchetype archetype))
            {
                throw new InvalidOperationException(
                    "The Kettle Hat pedestrian is absent from the city " +
                    "pedestrian catalog.");
            }

            GameObject prefab =
                CityPedestrianResources.LoadPrefab(archetype);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "The Kettle Hat pedestrian prefab could not be loaded " +
                    $"from Resources/{archetype.PrefabResourcePath}.");
            }

            GameObject wrapper = new GameObject("Mother's Table Kettle");
            wrapper.transform.SetParent(parent, false);
            wrapper.transform.SetPositionAndRotation(
                teapotDock.position,
                parent.rotation);
            wrapper.transform.localScale = Vector3.one * scale;

            try
            {
                GameObject instance = Object.Instantiate(
                    prefab,
                    wrapper.transform,
                    false);
                instance.name = "Kettle Hat Source Prefab";

                CityPedestrianAssetRegistry registry =
                    instance.GetComponent<CityPedestrianAssetRegistry>();
                CityKettleHatRigAnchors anchors =
                    instance.GetComponent<CityKettleHatRigAnchors>();
                if (registry == null || anchors == null)
                {
                    throw new InvalidOperationException(
                        "The Kettle Hat source prefab is missing its asset " +
                        "registry or kettle rig anchors.");
                }

                if (registry.Animator != null)
                {
                    registry.Animator.Rebind();
                    registry.Animator.Update(0f);
                    registry.Animator.enabled = false;
                }

                anchors.ResetLid();
                registry.ApplyPaletteVariant(0);

                Renderer[] allRenderers =
                    instance.GetComponentsInChildren<Renderer>(true);
                var required = new HashSet<string>(
                    RequiredRendererNames,
                    StringComparer.Ordinal);
                var visible = new List<Renderer>(
                    RequiredRendererNames.Length);
                for (int index = 0; index < allRenderers.Length; index++)
                {
                    Renderer renderer = allRenderers[index];
                    bool isKettle = required.Remove(
                        renderer.gameObject.name);
                    renderer.enabled = isKettle;
                    if (isKettle)
                    {
                        visible.Add(renderer);
                    }
                }

                if (required.Count != 0 ||
                    visible.Count != RequiredRendererNames.Length)
                {
                    throw new InvalidOperationException(
                        "The table kettle must expose exactly the ten " +
                        "ACC_Kettle source renderers. Missing: " +
                        string.Join(", ", required));
                }

                DisableNonVisualComponents(instance);
                Bounds visibleBounds = AlignRendererBottomToDock(
                    instance.transform,
                    visible,
                    teapotDock.position);
                ValidateRendererDockAlignment(
                    visibleBounds,
                    teapotDock.position);

                MothersHouseKettleProp prop =
                    wrapper.AddComponent<MothersHouseKettleProp>();
                prop.sourceInstance = instance;
                prop.visibleRenderers = visible.ToArray();
                prop.uniformScale = scale;
                return prop;
            }
            catch
            {
                Object.Destroy(wrapper);
                throw;
            }
        }

        private static void DisableNonVisualComponents(
            GameObject instance)
        {
            Collider[] colliders =
                instance.GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < colliders.Length; index++)
            {
                colliders[index].enabled = false;
            }

            Light[] lights =
                instance.GetComponentsInChildren<Light>(true);
            for (int index = 0; index < lights.Length; index++)
            {
                lights[index].enabled = false;
            }

            AudioSource[] audioSources =
                instance.GetComponentsInChildren<AudioSource>(true);
            for (int index = 0; index < audioSources.Length; index++)
            {
                audioSources[index].enabled = false;
            }

            CityKettleHatBoilEffect[] boilEffects =
                instance.GetComponentsInChildren<
                    CityKettleHatBoilEffect>(true);
            for (int index = 0; index < boilEffects.Length; index++)
            {
                boilEffects[index].enabled = false;
            }
        }

        private static Bounds AlignRendererBottomToDock(
            Transform instance,
            IReadOnlyList<Renderer> renderers,
            Vector3 dockPosition)
        {
            Bounds bounds = CalculateRendererBounds(renderers);

            Vector3 sourceDock = new Vector3(
                bounds.center.x,
                bounds.min.y,
                bounds.center.z);
            Vector3 offset = dockPosition - sourceDock;
            instance.position += offset;
            bounds.center += offset;
            return bounds;
        }

        private static Bounds CalculateRendererBounds(
            IReadOnlyList<Renderer> renderers)
        {
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Count; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private static void ValidateRendererDockAlignment(
            Bounds visibleBounds,
            Vector3 dockPosition)
        {
            Vector3 rendererDock = new Vector3(
                visibleBounds.center.x,
                visibleBounds.min.y,
                visibleBounds.center.z);
            if (Vector3.Distance(rendererDock, dockPosition) >
                DockAlignmentTolerance)
            {
                throw new InvalidOperationException(
                    $"The visible source kettle rests at {rendererDock}, " +
                    $"not its table dock {dockPosition}.");
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
