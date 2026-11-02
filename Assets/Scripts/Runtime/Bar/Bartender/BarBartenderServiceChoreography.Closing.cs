using UnityEngine;

namespace BarPromenade
{
    public sealed partial class BarBartenderServiceChoreography
    {
        private enum ClosingAnnouncementPhase { None, Waiting, Stowing, Turning, Entering, Holding, Exiting, Returning }
        private ClosingAnnouncementPhase closingAnnouncementPhase;
        private bool closingAnnouncementRequested;
        private bool closingReturnNeedsBlend;
        private bool closingExitNeedsBlend;
        private float closingAnnouncementSeconds;
        private Quaternion closingWorkRotation, closingHallRotation;
        private Vector3 closingHallPoint;
        private const float ClosingNeutralBlendSeconds = .35f;

        public bool IsClosingAnnouncementActive => closingAnnouncementPhase != ClosingAnnouncementPhase.None;
        public bool IsReadyForClosingAnnouncement => closingAnnouncementRequested &&
            closingAnnouncementPhase == ClosingAnnouncementPhase.Holding;
        private bool ClosingAnnouncementOwnsPresentation =>
            closingAnnouncementPhase >= ClosingAnnouncementPhase.Stowing;

        /// <summary>Service and carried props finish before this worker addresses the whole hall.</summary>
        public void RequestClosingAnnouncement(Vector3 hallWorldPoint)
        {
            if (!isInitialized || !isActiveAndEnabled) return;
            closingHallPoint = hallWorldPoint;
            closingAnnouncementRequested = true;
            if (closingAnnouncementPhase == ClosingAnnouncementPhase.None)
                SetClosingAnnouncementPhase(ClosingAnnouncementPhase.Waiting);
        }

        public void ReleaseClosingAnnouncement() => closingAnnouncementRequested = false;

        private bool AdvanceClosingAnnouncement(float deltaTime)
        {
            if (closingAnnouncementPhase == ClosingAnnouncementPhase.None) return false;
            float seconds = Mathf.Max(0f, deltaTime);
            if (closingAnnouncementPhase == ClosingAnnouncementPhase.Waiting)
            {
                if (!closingAnnouncementRequested)
                { SetClosingAnnouncementPhase(ClosingAnnouncementPhase.None); return false; }
                // A waiting served drink belongs to the hero; the worker is
                // free only after bottle/menu/grip transactions and his nod.
                if (shop.WantsBartenderService || shop.IsReturningCounterMenuHome ||
                    shop.IsPatronServiceActive || presentation.IsAcknowledgingOrder ||
                    shop.ServiceView.IsCarriedBottleVisible || !IsAtWipePatch ||
                    presentation.LowReachLowering > .002f) return false;
                closingWorkRotation = transform.rotation;
                Vector3 direction = Vector3.ProjectOnPlane(closingHallPoint - transform.position, Vector3.up);
                closingHallRotation = direction.sqrMagnitude > .0001f
                    ? Quaternion.LookRotation(direction.normalized, Vector3.up) : transform.rotation;
                SetClosingAnnouncementPhase(ClosingAnnouncementPhase.Stowing);
            }

            ReleaseAll();
            presentation.RequestTowelService(true);
            if (!closingAnnouncementRequested)
            {
                if (closingAnnouncementPhase == ClosingAnnouncementPhase.Entering)
                {
                    float entry = ClipLength(BarBartenderClipKind.ClosingAddressEnter);
                    float remaining = 1f - Mathf.Clamp01(closingAnnouncementSeconds / entry);
                    SetClosingAnnouncementPhase(ClosingAnnouncementPhase.Exiting);
                    closingAnnouncementSeconds = remaining * ClipLength(BarBartenderClipKind.ClosingAddressExit);
                }
                else if (closingAnnouncementPhase == ClosingAnnouncementPhase.Holding)
                {
                    // A live loop can be between its authored seam samples.
                    // Blend that exact visible hand pose into the exit instead of snapping to its first key.
                    presentation.BeginClosingAddressPose();
                    closingExitNeedsBlend = true;
                    SetClosingAnnouncementPhase(ClosingAnnouncementPhase.Exiting);
                }
                else if (closingAnnouncementPhase == ClosingAnnouncementPhase.Stowing ||
                         closingAnnouncementPhase == ClosingAnnouncementPhase.Turning)
                {
                    presentation.BeginClosingAddressPose();
                    closingReturnNeedsBlend = true;
                    SetClosingAnnouncementPhase(ClosingAnnouncementPhase.Returning);
                }
            }

            closingAnnouncementSeconds += seconds;
            switch (closingAnnouncementPhase)
            {
                case ClosingAnnouncementPhase.Stowing:
                    presentation.ApplyCounterTurnPose(0f, plantedLowReach: true);
                    if (presentation.IsTowelServiceReady)
                    {
                        presentation.BeginClosingAddressPose();
                        SetClosingAnnouncementPhase(ClosingAnnouncementPhase.Turning);
                    }
                    break;
                case ClosingAnnouncementPhase.Turning:
                    presentation.ApplyClosingAddressPose(BarBartenderClipKind.Notice, 0f,
                        closingAnnouncementSeconds / ClosingNeutralBlendSeconds);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, closingHallRotation,
                        CounterTurnSpeedDegrees * seconds);
                    if (Quaternion.Angle(transform.rotation, closingHallRotation) <= AuthoredFacingToleranceDegrees &&
                        closingAnnouncementSeconds >= ClosingNeutralBlendSeconds)
                        SetClosingAnnouncementPhase(ClosingAnnouncementPhase.Entering);
                    break;
                case ClosingAnnouncementPhase.Entering:
                    presentation.ApplyClosingAddressPose(BarBartenderClipKind.ClosingAddressEnter, closingAnnouncementSeconds);
                    if (closingAnnouncementSeconds >= ClipLength(BarBartenderClipKind.ClosingAddressEnter))
                        SetClosingAnnouncementPhase(ClosingAnnouncementPhase.Holding);
                    break;
                case ClosingAnnouncementPhase.Holding:
                    presentation.ApplyClosingAddressPose(BarBartenderClipKind.ClosingAddressLoop, closingAnnouncementSeconds);
                    break;
                case ClosingAnnouncementPhase.Exiting:
                    presentation.ApplyClosingAddressPose(BarBartenderClipKind.ClosingAddressExit, closingAnnouncementSeconds,
                        closingExitNeedsBlend ? closingAnnouncementSeconds / .2f : 1f);
                    if (closingAnnouncementSeconds >= ClipLength(BarBartenderClipKind.ClosingAddressExit))
                        SetClosingAnnouncementPhase(ClosingAnnouncementPhase.Returning);
                    break;
                case ClosingAnnouncementPhase.Returning:
                    presentation.ApplyClosingAddressPose(BarBartenderClipKind.Notice, 0f,
                        closingReturnNeedsBlend ? closingAnnouncementSeconds / ClosingNeutralBlendSeconds : 1f);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, closingWorkRotation,
                        CounterTurnSpeedDegrees * seconds);
                    if (Quaternion.Angle(transform.rotation, closingWorkRotation) <= AuthoredFacingToleranceDegrees &&
                        (!closingReturnNeedsBlend || closingAnnouncementSeconds >= ClosingNeutralBlendSeconds))
                    {
                        presentation.EndClosingAddressPose();
                        presentation.ResetServicePose();
                        closingReturnNeedsBlend = false;
                        closingExitNeedsBlend = false;
                        SetClosingAnnouncementPhase(ClosingAnnouncementPhase.None);
                    }
                    break;
            }
            return true;
        }

        private float ClipLength(BarBartenderClipKind kind) =>
            presentation.Registry.TryGetClip(kind, out AnimationClip clip, out _) ? Mathf.Max(.001f, clip.length) : .001f;

        private void SetClosingAnnouncementPhase(ClosingAnnouncementPhase phase)
        {
            closingAnnouncementPhase = phase;
            closingAnnouncementSeconds = 0f;
        }
    }
}
