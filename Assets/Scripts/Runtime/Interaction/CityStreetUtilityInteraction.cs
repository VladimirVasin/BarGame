using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The placeholder interaction on every phone booth door and
    /// dumpster lid. It stands on the recipe-derived dock, offers the
    /// real prompt and answers with a short feedback line — the same
    /// stub contract the stairwell cat shipped with before feeding
    /// existed. A future pass replaces <see cref="Interact"/> with the
    /// actual call or search without moving the trigger or the dock.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityStreetUtilityInteraction :
        MonoBehaviour,
        IInteractable
    {
        public const string PhoneBoothPromptKey =
            "interaction.use_phone_booth";
        public const string DumpsterPromptKey =
            "interaction.search_dumpster";
        public const string PhoneBoothPlaceholderKey =
            "city.phone_booth.placeholder";
        public const string DumpsterPlaceholderKey =
            "city.dumpster.placeholder";
        public const float ResponseDurationSeconds = 2.5f;

        private CityStreetUtilityDock dock;
        private bool isInitialized;

        public string PromptKey =>
            dock.Kind == CityStreetUtilityKind.PhoneBooth
                ? PhoneBoothPromptKey
                : DumpsterPromptKey;
        public Vector3 InteractionPosition => dock.StandPosition;
        public CityStreetUtilityDock Dock => dock;

        public void Initialize(CityStreetUtilityDock utilityDock)
        {
            if (!utilityDock.IsPresent)
            {
                throw new ArgumentException(
                    "The utility interaction requires a present dock.",
                    nameof(utilityDock));
            }

            dock = utilityDock;
            isInitialized = true;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            return isInitialized &&
                   isActiveAndEnabled &&
                   interactor != null &&
                   interactor.isActiveAndEnabled &&
                   interactor.InputEnabled &&
                   !SceneTransitionService.IsTransitioning;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor))
            {
                return;
            }

            interactor.ShowFeedback(
                dock.Kind == CityStreetUtilityKind.PhoneBooth
                    ? PhoneBoothPlaceholderKey
                    : DumpsterPlaceholderKey,
                ResponseDurationSeconds);
        }
    }
}
