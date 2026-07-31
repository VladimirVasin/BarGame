using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Stable runtime identity and selection geometry for one generated item.
    /// The component lives on OriginalRoot so the complete model can be safely
    /// reparented for inspection and later restored as one object.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HomeRefrigeratorItemView : MonoBehaviour
    {
        [SerializeField] private HomeRefrigeratorItemKind kind;
        [SerializeField] private string slotId;
        [SerializeField] private Transform originalRoot;
        [SerializeField] private Renderer[] itemRenderers =
            Array.Empty<Renderer>();
        [SerializeField] private Collider selectionCollider;

        private ReadOnlyCollection<Renderer> renderersView;

        public HomeRefrigeratorItemKind Kind => kind;
        public string SlotId => slotId;
        public Transform OriginalRoot => originalRoot;
        public IReadOnlyList<Renderer> Renderers =>
            renderersView ??= Array.AsReadOnly(
                itemRenderers ?? Array.Empty<Renderer>());
        public Collider SelectionCollider => selectionCollider;

        internal void Initialize(
            HomeRefrigeratorItemKind newKind,
            string newSlotId,
            Transform newOriginalRoot,
            IReadOnlyList<Renderer> newRenderers,
            Collider newSelectionCollider)
        {
            if (newKind == HomeRefrigeratorItemKind.None)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(newKind),
                    "An item view requires a concrete item kind.");
            }

            if (string.IsNullOrWhiteSpace(newSlotId))
            {
                throw new ArgumentException(
                    "An item view requires a stable slot ID.",
                    nameof(newSlotId));
            }

            if (newOriginalRoot == null)
            {
                throw new ArgumentNullException(nameof(newOriginalRoot));
            }

            if (newOriginalRoot != transform)
            {
                throw new ArgumentException(
                    "The item view must live on its original model root.",
                    nameof(newOriginalRoot));
            }

            if (newRenderers == null || newRenderers.Count == 0)
            {
                throw new ArgumentException(
                    "An item view requires at least one renderer.",
                    nameof(newRenderers));
            }

            itemRenderers = new Renderer[newRenderers.Count];
            for (int index = 0; index < newRenderers.Count; index++)
            {
                Renderer itemRenderer = newRenderers[index];
                if (itemRenderer == null ||
                    !itemRenderer.transform.IsChildOf(newOriginalRoot))
                {
                    throw new ArgumentException(
                        "Every item renderer must belong to its item root.",
                        nameof(newRenderers));
                }

                itemRenderers[index] = itemRenderer;
            }

            if (newSelectionCollider == null)
            {
                throw new ArgumentNullException(
                    nameof(newSelectionCollider));
            }

            if (newSelectionCollider.transform != newOriginalRoot ||
                !newSelectionCollider.isTrigger)
            {
                throw new ArgumentException(
                    "The selection collider must be a trigger on the item root.",
                    nameof(newSelectionCollider));
            }

            kind = newKind;
            slotId = newSlotId.Trim();
            originalRoot = newOriginalRoot;
            selectionCollider = newSelectionCollider;
            renderersView = Array.AsReadOnly(itemRenderers);
        }
    }
}
