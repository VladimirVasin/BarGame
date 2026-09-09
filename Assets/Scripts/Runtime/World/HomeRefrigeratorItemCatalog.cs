using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Pure presentation metadata for one inspectable refrigerator item.
    /// Localization is deliberately represented by stable keys so this data
    /// has no dependency on the active language or localization service.
    /// </summary>
    public readonly struct HomeRefrigeratorItemDefinition
    {
        internal HomeRefrigeratorItemDefinition(
            HomeRefrigeratorItemKind kind,
            string nameLocalizationKey,
            string descriptionLocalizationKey,
            Quaternion previewLocalRotation,
            float previewScale)
        {
            Kind = kind;
            NameLocalizationKey = nameLocalizationKey;
            DescriptionLocalizationKey = descriptionLocalizationKey;
            PreviewLocalRotation = previewLocalRotation;
            PreviewScale = previewScale;
        }

        public HomeRefrigeratorItemKind Kind { get; }
        public string NameLocalizationKey { get; }
        public string DescriptionLocalizationKey { get; }

        /// <summary>
        /// Starting orientation relative to the camera-facing preview pivot.
        /// </summary>
        public Quaternion PreviewLocalRotation { get; }

        /// <summary>
        /// Multiplier applied to the generated item's original local scale.
        /// </summary>
        public float PreviewScale { get; }
    }

    public static class HomeRefrigeratorItemCatalog
    {
        private static readonly HomeRefrigeratorItemDefinition[] definitions =
        {
            Define(
                HomeRefrigeratorItemKind.VodkaBottle,
                "home.refrigerator.item.vodka.name",
                "home.refrigerator.item.vodka.description"),
            Define(
                HomeRefrigeratorItemKind.ChickenEgg,
                "home.refrigerator.item.egg.name",
                "home.refrigerator.item.egg.description"),
            Define(
                HomeRefrigeratorItemKind.OpenStewCan,
                "home.refrigerator.item.stew_can.name",
                "home.refrigerator.item.stew_can.description")
        };

        /// <summary>
        /// The shelf keeps its own names and descriptions, because a jar in
        /// this refrigerator is written about differently than the same jar
        /// in a pocket. How the object is TURNED when it is held up is not
        /// its own fact, though - that comes from
        /// <see cref="InventoryItemPreviewPoses"/>, the one table every
        /// examination screen reads.
        /// </summary>
        private static HomeRefrigeratorItemDefinition Define(
            HomeRefrigeratorItemKind kind,
            string nameLocalizationKey,
            string descriptionLocalizationKey)
        {
            if (!HomeRefrigeratorInventoryAdapter.TryGetInventoryItem(
                    kind,
                    out InventoryItemId itemId))
            {
                throw new InvalidOperationException(
                    "A stocked refrigerator item must map to an " +
                    "inventory item, which is where its held pose lives.");
            }

            InventoryItemPreviewPose pose =
                InventoryItemPreviewPoses.Get(itemId);
            return new HomeRefrigeratorItemDefinition(
                kind,
                nameLocalizationKey,
                descriptionLocalizationKey,
                pose.BaseRotation,
                pose.InspectionScale);
        }

        private static readonly IReadOnlyList<
            HomeRefrigeratorItemDefinition> definitionsView =
                Array.AsReadOnly(definitions);

        public static IReadOnlyList<HomeRefrigeratorItemDefinition> All =>
            definitionsView;

        public static bool TryGet(
            HomeRefrigeratorItemKind kind,
            out HomeRefrigeratorItemDefinition definition)
        {
            for (int index = 0; index < definitions.Length; index++)
            {
                if (definitions[index].Kind == kind)
                {
                    definition = definitions[index];
                    return true;
                }
            }

            definition = default;
            return false;
        }

        public static HomeRefrigeratorItemDefinition Get(
            HomeRefrigeratorItemKind kind)
        {
            if (TryGet(kind, out HomeRefrigeratorItemDefinition definition))
            {
                return definition;
            }

            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The refrigerator item is not present in the catalog.");
        }
    }
}
