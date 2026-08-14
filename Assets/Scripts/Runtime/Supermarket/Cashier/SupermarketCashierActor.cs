using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Samples the hero every frame and feeds the surveillance and
    /// blink logic into the presentation. Runs after the player motor
    /// and camera so the stare lands on the settled body position.
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

            surveillance.Update(distance, lookDot, deltaTime);
            var command = new SupermarketCashierPoseCommand(
                surveillance.Extension,
                surveillance.StartleWeight,
                surveillance.ScanFrozen,
                surveillance.BlinkSuppressed,
                focusPoint,
                hasFocus);
            presentation.Apply(deltaTime, command);
        }
    }
}
