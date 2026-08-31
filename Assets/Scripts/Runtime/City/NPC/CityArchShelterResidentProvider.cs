using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Build-safe references to the three staged arch-shelter prefabs. The
    /// prefabs remain outside Resources; only this small provider is loaded.
    /// </summary>
    [CreateAssetMenu(
        fileName = "CityArchShelterResidentProvider",
        menuName = "Bar Promenade/City Arch Shelter Resident Provider")]
    public sealed class CityArchShelterResidentProvider : ScriptableObject
    {
        public const string ResourcePath =
            "City/CityArchShelterResidentProvider";

        [SerializeField] private GameObject standingPrefab;
        [SerializeField] private GameObject seatedPrefab;
        [SerializeField] private GameObject sleeperPrefab;

        public GameObject StandingPrefab => standingPrefab;
        public GameObject SeatedPrefab => seatedPrefab;
        public GameObject SleeperPrefab => sleeperPrefab;
        public bool HasCompleteCast =>
            standingPrefab != null &&
            seatedPrefab != null &&
            sleeperPrefab != null;

        public void Configure(
            GameObject configuredStandingPrefab,
            GameObject configuredSeatedPrefab,
            GameObject configuredSleeperPrefab)
        {
            standingPrefab = configuredStandingPrefab;
            seatedPrefab = configuredSeatedPrefab;
            sleeperPrefab = configuredSleeperPrefab;
        }

        public GameObject GetPrefab(CityArchShelterResidentRole role)
        {
            switch (role)
            {
                case CityArchShelterResidentRole.StandingWarmer:
                    return standingPrefab;
                case CityArchShelterResidentRole.SeatedWarmer:
                    return seatedPrefab;
                case CityArchShelterResidentRole.Sleeper:
                    return sleeperPrefab;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(role),
                        role,
                        "Unsupported arch-shelter resident role.");
            }
        }

        public void ValidateOrThrow()
        {
            if (!HasCompleteCast)
            {
                throw new InvalidOperationException(
                    "The City arch shelter resident provider must bind " +
                    "all three staged prefabs.");
            }

            ValidatePrefab(
                standingPrefab,
                CityArchShelterResidentRole.StandingWarmer);
            ValidatePrefab(
                seatedPrefab,
                CityArchShelterResidentRole.SeatedWarmer);
            ValidatePrefab(
                sleeperPrefab,
                CityArchShelterResidentRole.Sleeper);

            Avatar sharedAvatar = standingPrefab
                .GetComponentInChildren<
                    CityArchShelterResidentAssetRegistry>(true)
                .Animator.avatar;
            if (seatedPrefab.GetComponentInChildren<
                    CityArchShelterResidentAssetRegistry>(true)
                    .Animator.avatar != sharedAvatar ||
                sleeperPrefab.GetComponentInChildren<
                    CityArchShelterResidentAssetRegistry>(true)
                    .Animator.avatar != sharedAvatar)
            {
                throw new InvalidOperationException(
                    "All three arch-shelter residents must use the same " +
                    "Hero V2-compatible Avatar.");
            }
        }

        public static CityArchShelterResidentProvider Load()
        {
            return Resources.Load<CityArchShelterResidentProvider>(
                ResourcePath);
        }

        public static CityArchShelterResidentProvider LoadOrThrow()
        {
            CityArchShelterResidentProvider provider = Load();
            if (provider == null)
            {
                throw new InvalidOperationException(
                    "Missing Resources/" + ResourcePath +
                    " arch-shelter resident provider.");
            }

            provider.ValidateOrThrow();
            return provider;
        }

        private static void ValidatePrefab(
            GameObject prefab,
            CityArchShelterResidentRole expectedRole)
        {
            CityArchShelterResidentAssetRegistry registry = prefab
                .GetComponentInChildren<
                    CityArchShelterResidentAssetRegistry>(true);
            if (registry == null || registry.Role != expectedRole)
            {
                throw new InvalidOperationException(
                    $"The {expectedRole} prefab has the wrong resident " +
                    "registry contract.");
            }

            if (registry.Animator == null ||
                !registry.Animator.enabled ||
                registry.Animator.avatar == null ||
                !registry.Animator.avatar.isValid ||
                registry.Animator.runtimeAnimatorController != null ||
                registry.Animator.applyRootMotion ||
                registry.IdleClip == null ||
                registry.IdleClip.name != ExpectedClipName(expectedRole) ||
                !registry.IdleClip.isLooping ||
                Mathf.Abs(
                    registry.IdleClip.length -
                    ExpectedClipLength(expectedRole)) > 0.002f ||
                registry.ModelRoot == null ||
                registry.Head == null ||
                registry.Pelvis == null ||
                registry.LeftFoot == null ||
                registry.RightFoot == null ||
                registry.DetailAtlas == null ||
                registry.DetailAtlas.width != 256 ||
                registry.DetailAtlas.height != 256 ||
                registry.RendererBindings.Count == 0 ||
                registry.TriangleCount < 1500 ||
                registry.TriangleCount > 2300 ||
                registry.LocalBounds.size.sqrMagnitude <= 0f ||
                string.IsNullOrEmpty(registry.GeneratorVersion) ||
                string.IsNullOrEmpty(registry.DesignId) ||
                string.IsNullOrEmpty(registry.BuildSignature))
            {
                throw new InvalidOperationException(
                    $"The {expectedRole} prefab is incomplete.");
            }

            if (!registry.Head.IsChildOf(registry.ModelRoot) ||
                !registry.Pelvis.IsChildOf(registry.ModelRoot) ||
                !registry.LeftFoot.IsChildOf(registry.ModelRoot) ||
                !registry.RightFoot.IsChildOf(registry.ModelRoot))
            {
                throw new InvalidOperationException(
                    $"The {expectedRole} prefab has invalid rig anchors.");
            }

            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(
                true);
            var boundRenderers = new HashSet<Renderer>();
            int atlasBindingCount = 0;
            foreach (CityArchShelterResidentRendererBinding binding in
                     registry.RendererBindings)
            {
                if (binding == null || binding.Renderer == null ||
                    !binding.Renderer.transform.IsChildOf(prefab.transform) ||
                    !boundRenderers.Add(binding.Renderer))
                {
                    throw new InvalidOperationException(
                        $"The {expectedRole} prefab has an invalid renderer " +
                        "binding.");
                }

                if (binding.UsesDetailAtlas)
                {
                    atlasBindingCount++;
                }
            }

            if (boundRenderers.Count != renderers.Length ||
                atlasBindingCount < 8 ||
                prefab.GetComponentsInChildren<Collider>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Collider2D>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Rigidbody2D>(true).Length != 0 ||
                prefab.GetComponentsInChildren<AudioSource>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Light>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Camera>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    $"The {expectedRole} prefab is not a complete passive " +
                    "resident.");
            }

            MonoBehaviour[] behaviours =
                prefab.GetComponentsInChildren<MonoBehaviour>(true);
            if (behaviours.Length != 1 || behaviours[0] != registry)
            {
                throw new InvalidOperationException(
                    $"The {expectedRole} prefab may carry only its asset " +
                    "registry.");
            }
        }

        private static string ExpectedClipName(
            CityArchShelterResidentRole role)
        {
            switch (role)
            {
                case CityArchShelterResidentRole.StandingWarmer:
                    return "ShelterStandingWarm";
                case CityArchShelterResidentRole.SeatedWarmer:
                    return "ShelterSeatedWarm";
                case CityArchShelterResidentRole.Sleeper:
                    return "ShelterSleeperBreath";
                default:
                    throw new ArgumentOutOfRangeException(nameof(role));
            }
        }

        private static float ExpectedClipLength(
            CityArchShelterResidentRole role)
        {
            switch (role)
            {
                case CityArchShelterResidentRole.StandingWarmer:
                    return 8f;
                case CityArchShelterResidentRole.SeatedWarmer:
                    return 9f;
                case CityArchShelterResidentRole.Sleeper:
                    return 10f;
                default:
                    throw new ArgumentOutOfRangeException(nameof(role));
            }
        }
    }
}
