using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class SupermarketProductView : MonoBehaviour
    {
        [SerializeField] private InventoryItemId itemId;
        [SerializeField] private string sourceId = string.Empty;
        [SerializeField] private Transform originalRoot;
        [SerializeField] private Renderer[] productRenderers =
            Array.Empty<Renderer>();
        [SerializeField] private Collider selectionCollider;

        private ReadOnlyCollection<Renderer> renderersView;

        public InventoryItemId ItemId => itemId;
        public string SourceId => sourceId;
        public Transform OriginalRoot => originalRoot;
        public IReadOnlyList<Renderer> Renderers =>
            renderersView ??= Array.AsReadOnly(
                productRenderers ?? Array.Empty<Renderer>());
        public Collider SelectionCollider => selectionCollider;
        public SupermarketShelfView Shelf { get; private set; }

        internal void Initialize(
            InventoryItemId newItemId,
            string newSourceId,
            Transform newOriginalRoot,
            IReadOnlyList<Renderer> newRenderers,
            Collider newSelectionCollider,
            SupermarketShelfView shelf)
        {
            if (newItemId == InventoryItemId.None ||
                !InventoryItemCatalog.TryGet(newItemId, out _))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(newItemId),
                    "A supermarket product requires a catalog item.");
            }

            if (string.IsNullOrWhiteSpace(newSourceId))
            {
                throw new ArgumentException(
                    "A supermarket product requires a stable source ID.",
                    nameof(newSourceId));
            }

            if (newOriginalRoot == null ||
                newOriginalRoot != transform)
            {
                throw new ArgumentException(
                    "The product view must live on its complete model root.",
                    nameof(newOriginalRoot));
            }

            if (newRenderers == null || newRenderers.Count == 0)
            {
                throw new ArgumentException(
                    "A supermarket product requires visible geometry.",
                    nameof(newRenderers));
            }

            if (newSelectionCollider == null ||
                newSelectionCollider.transform != newOriginalRoot ||
                !newSelectionCollider.isTrigger)
            {
                throw new ArgumentException(
                    "The selection trigger must live on the product root.",
                    nameof(newSelectionCollider));
            }

            Shelf = shelf ?? throw new ArgumentNullException(nameof(shelf));
            productRenderers = new Renderer[newRenderers.Count];
            for (int index = 0; index < newRenderers.Count; index++)
            {
                Renderer productRenderer = newRenderers[index];
                if (productRenderer == null ||
                    !productRenderer.transform.IsChildOf(newOriginalRoot))
                {
                    throw new ArgumentException(
                        "Every product renderer must belong to its root.",
                        nameof(newRenderers));
                }

                productRenderers[index] = productRenderer;
            }

            itemId = newItemId;
            sourceId = newSourceId.Trim();
            originalRoot = newOriginalRoot;
            selectionCollider = newSelectionCollider;
            renderersView = Array.AsReadOnly(productRenderers);
        }
    }
}
