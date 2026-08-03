using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class SupermarketShelfView : MonoBehaviour
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private SupermarketShelfKind kind;
        [SerializeField] private BoxCollider bodyCollider;
        [SerializeField] private BoxCollider interactionTrigger;

        private readonly List<SupermarketProductView> products =
            new List<SupermarketProductView>();
        private ReadOnlyCollection<SupermarketProductView> productsView;
        private Transform interiorRoot;

        public string Id => id;
        public SupermarketShelfKind Kind => kind;
        public SupermarketShelfPlan Plan { get; private set; }
        public BoxCollider BodyCollider => bodyCollider;
        public BoxCollider InteractionTrigger => interactionTrigger;
        public IReadOnlyList<SupermarketProductView> Products =>
            productsView ??= products.AsReadOnly();
        public Vector3 CameraPosition =>
            interiorRoot.TransformPoint(Plan.CameraPosition);
        public Vector3 CameraLookAt =>
            interiorRoot.TransformPoint(Plan.CameraLookAt);
        public float CameraFieldOfView => Plan.CameraFieldOfView;

        internal void Initialize(
            SupermarketShelfPlan shelfPlan,
            Transform roomRoot,
            BoxCollider shelfBodyCollider,
            BoxCollider shelfInteractionTrigger)
        {
            if (Plan != null)
            {
                throw new InvalidOperationException(
                    "The supermarket shelf view is already initialized.");
            }

            Plan = shelfPlan ?? throw new ArgumentNullException(
                nameof(shelfPlan));
            interiorRoot = roomRoot ?? throw new ArgumentNullException(
                nameof(roomRoot));
            bodyCollider = shelfBodyCollider ??
                throw new ArgumentNullException(nameof(shelfBodyCollider));
            interactionTrigger = shelfInteractionTrigger ??
                throw new ArgumentNullException(
                    nameof(shelfInteractionTrigger));
            if (bodyCollider.isTrigger || !interactionTrigger.isTrigger)
            {
                throw new ArgumentException(
                    "Shelf body and interaction colliders have invalid roles.");
            }

            id = Plan.Id;
            kind = Plan.Kind;
            products.Clear();
            productsView = products.AsReadOnly();
        }

        internal void RegisterProduct(SupermarketProductView product)
        {
            if (product == null)
            {
                throw new ArgumentNullException(nameof(product));
            }

            if (!ReferenceEquals(product.Shelf, this))
            {
                throw new ArgumentException(
                    "A product can only register with its owning shelf.",
                    nameof(product));
            }

            for (int index = 0; index < products.Count; index++)
            {
                if (ReferenceEquals(products[index], product) ||
                    string.Equals(
                        products[index].SourceId,
                        product.SourceId,
                        StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "The shelf already contains that product source.",
                        nameof(product));
                }
            }

            products.Add(product);
        }

        public bool RemoveProduct(SupermarketProductView product)
        {
            return product != null && products.Remove(product);
        }

        public bool TryGetProduct(
            string sourceId,
            out SupermarketProductView product)
        {
            for (int index = 0; index < products.Count; index++)
            {
                if (string.Equals(
                        products[index].SourceId,
                        sourceId,
                        StringComparison.Ordinal))
                {
                    product = products[index];
                    return true;
                }
            }

            product = null;
            return false;
        }
    }
}
