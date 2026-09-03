using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Bar adapter for the shared physical counter-menu page and prop motion.
    /// It resolves only bar-authored semantic roles and localized drink data;
    /// selection, marker, focus and grip-to-dock motion live in generic code.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BarDrinkMenuPresentation : MonoBehaviour
    {
        public const float CameraFocusDistanceMeters = 1.10f;
        public const float CameraFocusFieldOfView = 60f;

        private BarServicePropInstance authored;
        private CounterMenuPageView page;
        private CounterMenuPropMotion motion;

        public bool IsConfigured { get; private set; }
        public bool IsVisible => page != null && page.IsPropVisible;
        public bool IsTextVisible => page != null && page.IsTextVisible;
        public bool IsPlaced { get; private set; }
        public Transform PropRoot => authored != null
            ? authored.transform
            : null;
        public Transform GripAnchor => motion?.GripAnchor;
        public Transform Carrier => motion?.Carrier;
        public CounterMenuPageView Page => page;
        public IReadOnlyList<TMPro.TMP_Text> ItemLines =>
            page?.ItemLines ?? Array.Empty<TMPro.TMP_Text>();
        public TMPro.TMP_Text SelectionMarker => page?.SelectionMarker;

        public static BarDrinkMenuPresentation CreateAndBind(
            Transform parent,
            BarDrinkServicePlan plan,
            Transform authoredDock = null)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            BarServicePropInstance instance =
                BarServicePropFactory.CreateMenu(parent);
            var presentation = instance.gameObject.AddComponent<
                BarDrinkMenuPresentation>();
            presentation.Configure(instance, plan, authoredDock);
            return presentation;
        }

        public void ConfigureCarrier(Transform carrier)
        {
            if (!IsConfigured)
            {
                throw new InvalidOperationException(
                    "The bar menu must be configured before its carrier.");
            }

            motion.SetCarrier(carrier);
        }

        public void BeginDelivery()
        {
            if (!IsConfigured)
            {
                return;
            }

            IsPlaced = false;
            page.SetVisible(true, false);
            motion.BeginDelivery();
        }

        public void EvaluateDelivery(float normalized)
        {
            if (!IsConfigured)
            {
                return;
            }

            IsPlaced = false;
            page.SetVisible(true, false);
            motion.EvaluateDelivery(normalized);
        }

        public void CompleteDelivery()
        {
            if (!IsConfigured)
            {
                return;
            }

            motion.SnapToDock();
            IsPlaced = true;
            page.SetVisible(true, true);
        }

        public void BeginRetrieval()
        {
            if (!IsConfigured)
            {
                return;
            }

            IsPlaced = false;
            page.SetVisible(true, true);
            motion.BeginRetrieval();
        }

        public void EvaluateRetrieval(float normalized)
        {
            if (!IsConfigured)
            {
                return;
            }

            IsPlaced = false;
            page.SetVisible(true, true);
            motion.EvaluateRetrieval(normalized);
        }

        public void CompleteRetrieval()
        {
            if (!IsConfigured)
            {
                return;
            }

            motion.AttachToCarrier();
            IsPlaced = false;
            page.SetVisible(false, false);
        }

        public void SetSelection(int index, bool confirmed)
        {
            page?.SetSelection(index, confirmed);
        }

        public Pose ResolveCameraFocusPose(Vector3 viewerPosition)
        {
            if (!IsConfigured || page == null)
            {
                throw new InvalidOperationException(
                    "The bar menu has no configured physical page.");
            }

            return page.ResolveCameraFocusPose(
                viewerPosition,
                CameraFocusDistanceMeters);
        }

        public void ResetPresentation()
        {
            if (!IsConfigured)
            {
                return;
            }

            motion.SnapToDock();
            IsPlaced = false;
            page.SetSelection(0, false);
            page.SetVisible(false, false);
        }

        private void Configure(
            BarServicePropInstance instance,
            BarDrinkServicePlan plan,
            Transform configuredDock)
        {
            authored = instance ??
                throw new ArgumentNullException(nameof(instance));
            Transform origin = RequireAnchor(
                BarServicePropFactory.MenuOriginRole);
            Transform grip = RequireAnchor(
                BarServicePropFactory.MenuGripRole);
            Pose originDock = configuredDock != null
                ? new Pose(configuredDock.position, configuredDock.rotation)
                : new Pose(
                    transform.parent.TransformPoint(plan.MenuPose.Position),
                    transform.parent.rotation * plan.MenuPose.Rotation);
            AlignAnchorToPose(transform, origin, originDock);
            Pose rootDock = new Pose(transform.position, transform.rotation);

            Transform pageOrigin = RequireAnchor(
                BarServicePropFactory.MenuPageOriginRole);
            Vector3 pageRight = ResolveBasisVector(
                pageOrigin,
                BarServicePropFactory.MenuPageRightRole);
            Vector3 pageUp = ResolveBasisVector(
                pageOrigin,
                BarServicePropFactory.MenuPageUpRole);
            Vector3 pageNormal = ResolveBasisVector(
                pageOrigin,
                BarServicePropFactory.MenuPageNormalRole);
            if (Vector3.Dot(
                    Vector3.Cross(pageUp, pageRight).normalized,
                    pageNormal) < 0.9f)
            {
                throw new InvalidOperationException(
                    "The authored bar menu has an invalid spread basis.");
            }

            IReadOnlyList<BarDrinkOffer> offers = BarDrinkCatalog.Offers;
            if (offers.Count != BarServicePropFactory.MenuItemCount)
            {
                throw new InvalidOperationException(
                    "The authored bar menu and drink catalog row counts " +
                    "do not match.");
            }

            var rowAnchors = new CounterMenuPageTextAnchor[offers.Count];
            var lines = new string[offers.Count];
            for (int index = 0; index < offers.Count; index++)
            {
                Transform row = RequireAnchor(
                    BarServicePropFactory.MenuTextItemRole(index));
                rowAnchors[index] =
                    CounterMenuPageTextAnchor.FromTransform(row);
                string price = string.Format(
                    LocalizationService.Get("drink_shop.price"),
                    offers[index].Price);
                lines[index] = LocalizationService.Get(
                    offers[index].NameKey) + "   " + price;
            }

            Transform selection = RequireAnchor(
                BarServicePropFactory.MenuTextSelectionRole);
            page = gameObject.AddComponent<CounterMenuPageView>();
            page.Initialize(
                transform,
                authored.Renderers,
                rowAnchors,
                selection,
                lines,
                pageOrigin.position,
                pageNormal,
                pageUp,
                CounterMenuPageStyle.Bar);
            motion = new CounterMenuPropMotion(
                transform,
                grip,
                rootDock);
            IsConfigured = true;
            ResetPresentation();
        }

        private Transform RequireAnchor(string role)
        {
            if (authored.TryGetAnchor(role, out Transform anchor) &&
                anchor != null)
            {
                return anchor;
            }

            throw new InvalidOperationException(
                $"The authored bar menu has no anchor role '{role}'.");
        }

        private Vector3 ResolveBasisVector(
            Transform origin,
            string vectorRole)
        {
            Vector3 vector = RequireAnchor(vectorRole).position -
                             origin.position;
            if (vector.sqrMagnitude < 0.000001f)
            {
                throw new InvalidOperationException(
                    $"The authored bar menu basis '{vectorRole}' is empty.");
            }

            return vector.normalized;
        }

        private static void AlignAnchorToPose(
            Transform root,
            Transform anchor,
            Pose target)
        {
            Quaternion correction = target.rotation *
                                    Quaternion.Inverse(anchor.rotation);
            root.rotation = correction * root.rotation;
            root.position += target.position - anchor.position;
        }

        private void OnDisable()
        {
            ResetPresentation();
        }
    }
}
