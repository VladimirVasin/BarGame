using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Standing in front of the plaque and reading it.
    ///
    /// The board itself carries no letters — the words live on the
    /// panel this raises — so without something to read it with, the
    /// line the player wrote would be visible exactly once, at the
    /// moment of writing, and then be a blank plate forever. That is
    /// the whole reason this exists.
    ///
    /// It outlives the worksite. The dig site is torn down when the
    /// stone goes up; this stays on the finished grave for as long as
    /// the city stands.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CemeteryPlaqueReadInteraction :
        MonoBehaviour,
        IInteractable
    {
        public const string RuntimeRootName = "Grave Plaque Reader";
        public const string PromptKey = "interaction.read_plaque";

        /// <summary>How far in front of the stone's anchor the board
        /// hangs, and therefore where the hero stands to read
        /// it.</summary>
        public const float FaceOffsetMeters = 0.30f;

        public const float TriggerRadius = 1.05f;
        public const float TriggerHeight = 1.8f;

        private Vector3 standPosition;
        private Action onRead;
        private bool isInitialized;

        string IInteractable.PromptKey => PromptKey;
        public Vector3 InteractionPosition => standPosition;

        public static CemeteryPlaqueReadInteraction Create(
            Transform parent,
            CemeteryGravediggingPlan plan,
            Action readAction)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (!plan.IsPresent)
            {
                return null;
            }

            var root = new GameObject(RuntimeRootName);
            root.transform.SetParent(parent, false);
            Vector3 seat =
                CityCemeteryPlaqueWorldBuilder.GetNominalSeat(plan);
            root.transform.position = new Vector3(
                seat.x,
                plan.GroundTopY,
                seat.z);

            var reader =
                root.AddComponent<CemeteryPlaqueReadInteraction>();
            reader.standPosition = root.transform.position;
            reader.onRead = readAction;
            reader.isInitialized = true;

            var trigger = root.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = TriggerRadius;
            trigger.center = new Vector3(
                0f,
                TriggerHeight * 0.5f,
                0f);
            return reader;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            return isInitialized &&
                   isActiveAndEnabled &&
                   onRead != null &&
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

            onRead();
        }
    }
}
