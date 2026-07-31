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
            new HomeRefrigeratorItemDefinition(
                HomeRefrigeratorItemKind.VodkaBottle,
                "home.refrigerator.item.vodka.name",
                "home.refrigerator.item.vodka.description",
                Quaternion.Euler(0f, -18f, 0f),
                1.35f),
            new HomeRefrigeratorItemDefinition(
                HomeRefrigeratorItemKind.ChickenEgg,
                "home.refrigerator.item.egg.name",
                "home.refrigerator.item.egg.description",
                Quaternion.Euler(10f, -24f, -6f),
                1.85f),
            new HomeRefrigeratorItemDefinition(
                HomeRefrigeratorItemKind.OpenStewCan,
                "home.refrigerator.item.stew_can.name",
                "home.refrigerator.item.stew_can.description",
                Quaternion.Euler(8f, 22f, -4f),
                1.55f)
        };

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
