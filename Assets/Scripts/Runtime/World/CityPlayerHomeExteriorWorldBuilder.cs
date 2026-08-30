using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>
    /// Places the complete passive player-home exterior in City. Gameplay
    /// collision, the entrance transaction, street approach and findability
    /// anchors remain plan-owned and are composed beside this model.
    /// </summary>
    public static class CityPlayerHomeExteriorWorldBuilder
    {
        public const string CityObjectName = "Player Home Exterior";
        public const string DoorAnchorRole = "exterior_door";
        public const string DesignId = "player_home_exterior_v1";

        public static PlayerHomeExteriorAssetRegistry BuildCity(
            Transform parent,
            BuildingLot lot)
        {
            Validate(parent, lot);
            return BuildAuthored(
                parent,
                lot.DoorPosition,
                ResolveDirection(lot),
                CityObjectName);
        }

        private static PlayerHomeExteriorAssetRegistry BuildAuthored(
            Transform parent,
            Vector3 doorPosition,
            Vector3 direction,
            string objectName)
        {
            direction.y = 0f;
            direction.Normalize();

            GameObject prefab =
                PlayerHomeExteriorModelResources.LoadPrefab();
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "The player-home exterior model is missing. Run the " +
                    "deterministic PlayerHomeExterior3D Blender generator " +
                    "and build its runtime prefab.");
            }

            GameObject instance = Object.Instantiate(prefab, parent);
            instance.name = objectName;
            instance.transform.localPosition = doorPosition;
            instance.transform.localRotation =
                Quaternion.LookRotation(direction, Vector3.up);
            instance.transform.localScale = Vector3.one;

            PlayerHomeExteriorAssetRegistry registry =
                instance.GetComponent<PlayerHomeExteriorAssetRegistry>();
            if (registry == null)
            {
                throw new InvalidOperationException(
                    "The player-home exterior prefab has no " +
                    "PlayerHomeExteriorAssetRegistry.");
            }

            if (!string.Equals(
                    registry.DesignId,
                    DesignId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The player-home exterior design is " +
                    $"'{registry.DesignId}', expected '{DesignId}'.");
            }

            if (!registry.TryGetAnchor(
                    DoorAnchorRole,
                    out Transform doorAnchor))
            {
                throw new InvalidOperationException(
                    "The player-home exterior has no exterior_door anchor.");
            }

            // Imported FBX roots keep their 100/0.01 unit hierarchy. The
            // anchor's measured world position is therefore the only safe
            // alignment value; localPosition would be off by a factor of 100.
            Vector3 targetDoor = parent.TransformPoint(doorPosition);
            instance.transform.position += targetDoor - doorAnchor.position;

            for (int index = 0; index < registry.Parts.Count; index++)
            {
                PlayerHomeExteriorPartBinding binding =
                    registry.Parts[index];
                Renderer renderer = binding?.Renderer;
                if (renderer == null)
                {
                    continue;
                }

                if (!PlayerHomeExteriorSurfaceAppearance.TryResolveSheet(
                        binding.Sheet,
                        out PlayerHomeExteriorSurfaceKind surface))
                {
                    throw new InvalidOperationException(
                        $"The player-home part '{binding.SourceName}' asks " +
                        $"for unknown sheet '{binding.Sheet}'.");
                }

                PlayerHomeExteriorSurfaceAppearance.Apply(
                    renderer,
                    surface,
                    binding.Emissive);
            }

            return registry;
        }

        private static Vector3 ResolveDirection(BuildingLot lot)
        {
            return new Vector3(
                lot.FrontageDirection.x,
                0f,
                lot.FrontageDirection.y);
        }

        private static void Validate(
            Transform parent,
            BuildingLot lot)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (lot == null)
            {
                throw new ArgumentNullException(nameof(lot));
            }

            if (!lot.IsPlayerHome || !lot.HasRoadFrontage)
            {
                throw new ArgumentException(
                    "A player-home exterior requires the player-home lot " +
                    "with street frontage.",
                    nameof(lot));
            }

            CitySpecialBuildingWorldBuilder.ValidatePlayerHome(lot);
        }
    }
}
