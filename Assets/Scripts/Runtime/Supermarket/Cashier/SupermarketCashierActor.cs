using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Samples the hero every frame and feeds look/blink intent into the
    /// presentation. The active fixed-neck asset uses only bounded human
    /// tracking; the retained Watcher can additionally consume extension and
    /// startle state. Runs after the player motor and camera.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(340)]
    public sealed class SupermarketCashierActor : MonoBehaviour
    {
        public const float FocusHeadLift = 0.10f;
        public const float FallbackFocusHeight = 1.58f;

        private SupermarketCashierPresentation presentation;
        private Transform playerBody;
        private Transform playerHead;
        private SupermarketCashierSurveillanceState surveillance;
        private bool isInitialized;

        public SupermarketCashierSurveillanceState Surveillance =>
            surveillance;
        public SupermarketCashierPresentation Presentation =>
            presentation;

        public void Initialize(
            SupermarketCashierPresentation cashierPresentation,
            Transform trackedPlayerBody,
            Transform trackedPlayerHead)
        {
            presentation = cashierPresentation != null
                ? cashierPresentation
                : throw new ArgumentNullException(
                    nameof(cashierPresentation));
            playerBody = trackedPlayerBody;
            playerHead = trackedPlayerHead;
            surveillance = new SupermarketCashierSurveillanceState();
            isInitialized = true;
        }

        private void LateUpdate()
        {
            if (!isInitialized)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            bool hasFocus = playerBody != null;
            float distance = 0f;
            float lookDot = -1f;
            Vector3 focusPoint = transform.position;
            if (hasFocus)
            {
                Vector3 up = transform.up;
                Vector3 toCashier = Vector3.ProjectOnPlane(
                    transform.position - playerBody.position,
                    up);
                distance = toCashier.magnitude;
                if (distance > 0.0001f)
                {
                    Vector3 playerForward = Vector3.ProjectOnPlane(
                        playerBody.forward,
                        up);
                    if (playerForward.sqrMagnitude > 0.0001f)
                    {
                        lookDot = Vector3.Dot(
                            playerForward.normalized,
                            toCashier.normalized);
                    }
                }

                focusPoint = playerHead != null
                    ? playerHead.position + up * FocusHeadLift
                    : playerBody.position + up * FallbackFocusHeight;
            }

            bool usesWatcherBehavior =
                presentation.UsesExtensibleNeck;
            if (usesWatcherBehavior)
            {
                surveillance.Update(distance, lookDot, deltaTime);
            }

            var command = new SupermarketCashierPoseCommand(
                usesWatcherBehavior ? surveillance.Extension : 0f,
                usesWatcherBehavior ? surveillance.StartleWeight : 0f,
                usesWatcherBehavior && surveillance.ScanFrozen,
                usesWatcherBehavior && surveillance.BlinkSuppressed,
                focusPoint,
                hasFocus);
            presentation.Apply(deltaTime, command);
        }
    }
}
