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
    [DefaultExecutionOrder(330)]
    public sealed class BarDrinkMenuPresentation : MonoBehaviour
    {
        // The four descriptive bar rows use a steeper view than the small
        // cafe card: the camera hangs almost over the spread centre and looks
        // nearly along its surface normal, keeping both pages legible without
        // letting the near edge collide with the bottom hint.
        public const float CameraFocusDistanceMeters = 0.45f;
        public const float CameraFocusFieldOfView = 72f;
        public const float CameraFocusSurfaceFacing = 0.998f;
        public const float CameraFocusTargetTowardViewerMeters = 0.07f;

        // The source prop keeps nine compatibility sockets. The visible bar
        // menu normalizes its four chosen sockets to this inset 2 x 2 grid so
        // neither the text boxes nor the selection marker touch the spine,
        // page rails or top/bottom rules.
        public const float LeftPageTextCenterMeters = -0.115f;
        public const float RightPageTextCenterMeters = 0.140f;
        public const float TextRowOffsetMeters = 0.095f;

        private BarServicePropInstance authored;
        private CounterMenuPageView page;
        private CounterMenuPropMotion motion;
        private Pose baseDockPose;
        private bool followsCarrier;

        public bool IsConfigured { get; private set; }
        public bool IsVisible => page != null && page.IsPropVisible;
        public bool IsTextVisible => page != null && page.IsTextVisible;
        public bool IsPlaced { get; private set; }
        public bool IsRestingOnCounter { get; private set; }
        public Transform PropRoot => authored != null
            ? authored.transform
            : null;
        public Transform GripAnchor => motion?.GripAnchor;
        public Transform Carrier => motion?.Carrier;
        public CounterMenuPageView Page => page;
        public Vector3 CameraFocusWorldPosition => page != null
            ? page.FocusWorldPosition
            : transform.position;
        public IReadOnlyList<TMPro.TMP_Text> ItemLines =>
            page?.ItemLines ?? Array.Empty<TMPro.TMP_Text>();
        public TMPro.TMP_Text SelectionMarker => page?.SelectionMarker;

        public void ConfigureDockOffset(Vector3 serviceLocalOffset)
        {
            if (!IsConfigured)
            {
                throw new InvalidOperationException(
                    "The bar menu must be configured before its dock.");
            }

            Transform reference = transform.parent;
            Vector3 worldOffset = reference != null
                ? reference.TransformVector(serviceLocalOffset)
                : serviceLocalOffset;
            motion.SetDockPose(new Pose(
                baseDockPose.position + worldOffset,
                baseDockPose.rotation));
            if (!IsVisible)
            {
                motion.SnapToDock();
            }
        }

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
            IsRestingOnCounter = false;
            followsCarrier = true;
            page.SetRestingVisible(true);
            motion.BeginDelivery();
        }

        public void EvaluateDelivery(float normalized)
        {
            if (!IsConfigured)
            {
                return;
            }

            IsPlaced = false;
            followsCarrier = false;
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
            followsCarrier = false;
            IsPlaced = true;
            IsRestingOnCounter = false;
            page.SetVisible(true, true);
        }

        public bool RestOnCounter()
        {
            if (!IsConfigured || !IsPlaced)
            {
                return false;
            }

            motion.SnapToDock();
            followsCarrier = false;
            IsPlaced = true;
            IsRestingOnCounter = true;
            page.SetRestingVisible(true);
            return true;
        }

        public bool ReopenOnCounter()
        {
            if (!IsConfigured || !IsPlaced || !IsRestingOnCounter)
            {
                return false;
            }

            motion.SnapToDock();
            followsCarrier = false;
            IsRestingOnCounter = false;
            page.SetRestingHighlight(false);
            page.SetVisible(true, true);
            return true;
        }

        public bool IsLookingAtRestingMenu(Camera camera)
        {
            return IsRestingOnCounter &&
                   page != null &&
                   page.IsLookingAtRestingProp(camera);
        }

        public void SetRestingHighlight(bool highlighted)
        {
            page?.SetRestingHighlight(
                highlighted && IsRestingOnCounter);
        }

        public void BeginRetrieval()
        {
            if (!IsConfigured)
            {
                return;
            }

            page.SetRestingHighlight(false);
            IsPlaced = false;
            followsCarrier = false;
            if (IsRestingOnCounter)
            {
                page.SetRestingVisible(true);
            }
            else
            {
                page.SetVisible(true, true);
            }
            motion.BeginRetrieval();
        }

        public void EvaluateRetrieval(float normalized)
        {
            if (!IsConfigured)
            {
                return;
            }

            IsPlaced = false;
            followsCarrier = false;
            if (IsRestingOnCounter)
            {
                page.SetRestingVisible(true);
            }
            else
            {
                page.SetVisible(true, true);
            }
            motion.EvaluateRetrieval(normalized);
        }

        public void CompleteRetrieval(bool keepVisibleOnCarrier = false)
        {
            if (!IsConfigured)
            {
                return;
            }

            motion.AttachToCarrier();
            IsPlaced = false;
            IsRestingOnCounter = false;
            followsCarrier = keepVisibleOnCarrier && motion.Carrier != null;
            if (followsCarrier)
            {
                page.SetRestingVisible(true);
            }
            else
            {
                page.SetVisible(false, false);
            }
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
                CameraFocusDistanceMeters,
                CameraFocusSurfaceFacing,
                CameraFocusTargetTowardViewerMeters);
        }

        public void ResetPresentation()
        {
            if (!IsConfigured)
            {
                return;
            }

            motion.SnapToDock();
            followsCarrier = false;
            IsPlaced = false;
            IsRestingOnCounter = false;
            page.SetRestingHighlight(false);
            page.SetSelection(0, false);
            page.SetVisible(false, false);
        }

        private void LateUpdate()
        {
            if (followsCarrier && motion?.Carrier != null)
            {
                motion.AttachToCarrier();
            }
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
            baseDockPose = rootDock;

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
                PlaceRowOnReadableGrid(row, pageOrigin, index);
                rowAnchors[index] =
                    CounterMenuPageTextAnchor.FromTransform(row);
                string price = string.Format(
                    LocalizationService.Get("drink_shop.price"),
                    offers[index].Price);
                string description = LocalizationService.Get(
                    offers[index].DescriptionKey);
                lines[index] = LocalizationService.Get(
                    offers[index].NameKey) + "\n" + price + "\n" +
                    description;
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

        public static Vector2 ResolveTextBlockPageOffset(int itemIndex)
        {
            if (itemIndex < 0 ||
                itemIndex >= BarServicePropFactory.MenuItemCount)
            {
                throw new ArgumentOutOfRangeException(nameof(itemIndex));
            }

            return new Vector2(
                itemIndex < 2
                    ? LeftPageTextCenterMeters
                    : RightPageTextCenterMeters,
                (itemIndex & 1) == 0
                    ? TextRowOffsetMeters
                    : -TextRowOffsetMeters);
        }

        private static void PlaceRowOnReadableGrid(
            Transform row,
            Transform pageOrigin,
            int itemIndex)
        {
            Vector2 target = ResolveTextBlockPageOffset(itemIndex);
            Vector3 fromOrigin = row.position - pageOrigin.position;
            float currentAcross = Vector3.Dot(fromOrigin, row.right);
            float currentUp = Vector3.Dot(fromOrigin, row.up);
            row.position += row.right * (target.x - currentAcross) +
                            row.up * (target.y - currentUp);
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
