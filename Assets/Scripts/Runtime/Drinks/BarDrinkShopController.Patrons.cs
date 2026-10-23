using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class BarDrinkShopController
    {
        private readonly BarBartenderServiceQueue serviceQueue = new();
        private readonly BarDrinkServiceTimeline patronServiceTimeline = new();
        private BarCounterPatronRefill activePatronRefill;
        private PatronPresentationBackup patronHeroState;
        private Pose patronVesselDock;
        private bool patronPresentationPrepared;
        private const float PatronCupOfferSeconds = .55f;
        private float patronCupOfferElapsed;
        private Vector3 patronCupOfferOffset;
        private bool patronCupOfferPending;
        private float patronCupReturnElapsed;
        private bool patronCupReturnPending;
        public BarBartenderServiceQueue ServiceQueue => serviceQueue;
        public bool IsPatronServiceActive => activePatronRefill != null;
        public BarCounterPatronRefill ActivePatronRefill => activePatronRefill;
        public bool IsPatronCupOfferActive => IsPatronServiceActive && patronCupOfferPending;
        public float PatronCupOfferProgress => IsPatronServiceActive && patronCupOfferOffset.sqrMagnitude > .000001f
            ? Mathf.Clamp01(patronCupOfferElapsed / PatronCupOfferSeconds) : 0f;
        public Vector3 PatronCupOfferWorldOffset => IsPatronServiceActive ? patronCupOfferOffset : Vector3.zero;
        public bool IsPatronCupReturnActive => IsPatronServiceActive && patronCupReturnPending;
        private BarDrinkServiceTimeline ActiveServiceTimeline => IsPatronServiceActive ? patronServiceTimeline : timeline;
        public BarDrinkVesselView HeroServiceVessel => patronHeroState != null ? patronHeroState.Vessel : serviceView?.ActiveVessel;

        public void ConfigureCounterPatronService(IReadOnlyList<BarPatron> patrons)
        {
            if (patrons == null) throw new ArgumentNullException(nameof(patrons));
            int slot = 0;
            foreach (BarPatron patron in patrons)
            {
                if (patron.Anchor.Role != BarNpcRole.CounterPatron || patron.Drinking == null) continue;
                if (slot >= 2) throw new InvalidOperationException("The shared bar service supports its two authored counter patrons.");
                BarCounterPatronRefill refill = patron.Drinking.gameObject.AddComponent<BarCounterPatronRefill>();
                refill.Configure(patron.Drinking, this, slot++);
            }
        }

        public bool QueuePatronRefill(BarCounterPatronRefill patron)
        {
            if (patron == null || !patron.IsWaitingForRefill) return false;
            bool existing = serviceQueue.Contains(patron);
            bool accepted = serviceQueue.RequestPatron(patron, () => BeginCounterPatronService(patron));
            if (accepted && !existing) bartenderPresentation?.AcknowledgeOrder(patron.transform);
            return accepted;
        }

        private void BeginCounterPatronService(BarCounterPatronRefill patron)
        {
            if (IsPatronServiceActive || !serviceQueue.IsOwner(patron))
                throw new InvalidOperationException("A patron cannot preempt the bartender's current owner.");
            patronHeroState = new PatronPresentationBackup(this);
            activePatronRefill = patron;
            patron.BeginRefill();
            patronVesselDock = new Pose(patron.Vessel.transform.position, patron.Vessel.transform.rotation);
            serviceView.SelectServiceVessel(patron.Vessel);
            currentOrderDrinkId = patron.DrinkId;
            activePresentation = BarDrinkPresentationCatalog.Get(patron.DrinkId);
            DrinkId shelfDrink = patron.DrinkId;
            if (!serviceView.TryGetBottle(shelfDrink, out _))
                shelfDrink = activePresentation.BottleStyle switch
                {
                    BarDrinkBottleStyle.WineBottle => DrinkId.RedWine,
                    BarDrinkBottleStyle.CognacBottle => DrinkId.CognacVs,
                    BarDrinkBottleStyle.VodkaBottle => DrinkId.Vodka,
                    _ => DrinkId.LightBeer
                };
            serviceView.SelectBottle(shelfDrink);
            Vector3 dock = serviceView.transform.InverseTransformPoint(patronVesselDock.position);
            activeServiceLocalOffset = dock - servicePlan.VesselCounterPose.Position;
            // Cup model origins have different heights; they cannot lower the server's floor line.
            activeServiceLocalOffset.y = 0f;
            activeServiceMirrored = false;
            emptyVesselCounterPose = patronVesselDock;
            vesselBaseScale = patron.Vessel.transform.localScale;
            beerPlacementCaptured = bottleGripAttached = bottleReturnCaptured = emptyVesselStowCaptured = false;
            pendingOrderedDrinkPresentation = false;
            patronPresentationPrepared = false;
            ResetCueState();
            patronServiceTimeline.BeginPatronRefill(patron.DrinkId);
            patronCupOfferElapsed = 0f;
            patronCupReturnElapsed = 0f;
            patronCupReturnPending = false;
            patronCupOfferOffset = patronServiceTimeline.IsBeerService
                ? ResolvePatronCupOfferOffset(patron.Vessel) : Vector3.zero;
            patronCupOfferPending = patronCupOfferOffset.sqrMagnitude > .000001f;
            emptyVesselCounterPose.position += patronCupOfferOffset;
            if (patronCupOfferPending) patron.Drinking.SetCounterCupOfferOffset(Vector3.zero);
            bartenderPresentation?.RequestTowelService(true);
        }

        private Vector3 ResolvePatronCupOfferOffset(BarDrinkVesselView vessel)
        {
            Vector3 direction = Vector3.ProjectOnPlane(serviceView.transform.forward, Vector3.up).normalized;
            float distance = ServedVesselServerwardOffset;
            Bounds top = serviceView.CounterSurfaceWorldBounds;
            Bounds shell = vessel.LocalVisualBounds;
            Vector3 scale = vessel.transform.lossyScale;
            // The existing glass slides on the wood; include its full shell rather than just its root.
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = patronVesselDock.position + patronVesselDock.rotation * Vector3.Scale(
                    shell.center + Vector3.Scale(shell.extents, new Vector3(
                        (corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f)), scale);
                if (Mathf.Abs(direction.x) > .0001f)
                    distance = Mathf.Min(distance,
                        ((direction.x > 0f ? top.max.x - .015f : top.min.x + .015f) - point.x) / direction.x);
                if (Mathf.Abs(direction.z) > .0001f)
                    distance = Mathf.Min(distance,
                        ((direction.z > 0f ? top.max.z - .015f : top.min.z + .015f) - point.z) / direction.z);
            }
            return direction * Mathf.Max(0f, distance);
        }

        /// <summary>Called once by the shop's Update; the hero's private clock and transaction stay separate.</summary>
        private bool AdvancePatronService(float deltaTime)
        {
            if (!IsPatronServiceActive) return false;
            if (GameTimeScaleRuntime.IsPaused || bartenderPresentation != null && !bartenderPresentation.IsTowelServiceReady) return true;
            if (patronCupReturnPending)
            {
                if (AdvancePatronCupReturn(deltaTime)) FinishCounterPatronService(true);
                return IsPatronServiceActive;
            }
            BarDrinkServiceTimeline heroTimeline = timeline;
            try
            {
                timeline = patronServiceTimeline;
                if (!patronPresentationPrepared)
                { PrepareOrderedDrinkPresentation(); patronPresentationPrepared = true; }
                if (patronCupOfferPending)
                {
                    patronCupOfferElapsed = Mathf.Min(PatronCupOfferSeconds,
                        patronCupOfferElapsed + Mathf.Max(0f, deltaTime));
                    Vector3 offset = patronCupOfferOffset * Mathf.SmoothStep(0f, 1f, PatronCupOfferProgress);
                    activePatronRefill.Vessel.SetWorldPose(patronVesselDock.position + offset, patronVesselDock.rotation);
                    activePatronRefill.Drinking.SetCounterCupOfferOffset(offset);
                    patronCupOfferPending = patronCupOfferElapsed < PatronCupOfferSeconds;
                    return true;
                }
                timeline.Advance(Mathf.Max(0f, deltaTime));
                BarDrinkServiceFrame frame = timeline.CurrentFrame;
                ApplyBottlePresentation(frame);
                ApplyVesselPresentation(frame);
                PlayCrossedCues();
            }
            finally { timeline = heroTimeline; }
            if (patronServiceTimeline.IsAwaitingDrink)
            {
                if (patronCupOfferOffset.sqrMagnitude > .000001f)
                {
                    patronCupReturnPending = true;
                    AdvancePatronCupReturn(0f);
                }
                else FinishCounterPatronService(true);
            }
            return IsPatronServiceActive;
        }

        private bool AdvancePatronCupReturn(float deltaTime)
        {
            const float reachSeconds = .35f, gripSeconds = .25f;
            float pullBegins = reachSeconds + gripSeconds;
            float duration = pullBegins + PatronCupOfferSeconds;
            patronCupReturnElapsed = Mathf.Min(duration, patronCupReturnElapsed + Mathf.Max(0f, deltaTime));
            float approach = Mathf.SmoothStep(0f, 1f, patronCupReturnElapsed / reachSeconds);
            float remaining = 1f - Mathf.SmoothStep(0f, 1f,
                (patronCupReturnElapsed - pullBegins) / PatronCupOfferSeconds);
            activePatronRefill.Drinking.SetCounterCupOfferOffset(
                patronCupOfferOffset * (approach * remaining), patronCupReturnElapsed >= reachSeconds);
            activePatronRefill.Vessel.SetWorldPose(patronVesselDock.position + patronCupOfferOffset * remaining,
                patronVesselDock.rotation);
            return patronCupReturnElapsed >= duration;
        }

        public Vector3 ResolveHeroRequestPoint()
        {
            BarDrinkVesselView vessel = HeroServiceVessel;
            if (vessel != null && vessel.gameObject.activeInHierarchy) return vessel.transform.position;
            Vector3 offset = patronHeroState != null ? patronHeroState.Offset : activeServiceLocalOffset;
            bool mirrored = patronHeroState != null ? patronHeroState.Mirrored : activeServiceMirrored;
            Vector3 point = servicePlan.VesselCounterPose.Position + offset;
            if (mirrored) point.x = 2f * (servicePlan.SeatPose.Position.x + offset.x) - point.x;
            return serviceView.transform.TransformPoint(point + Vector3.forward * ServedVesselServerwardOffset);
        }

        private bool StoreHeroServiceOffsetIfPatronActive(Vector3 offset, bool mirrored)
        {
            if (patronHeroState == null) return false;
            patronHeroState.Offset = offset;
            patronHeroState.Mirrored = mirrored;
            return true;
        }

        private void ClearPreservedHeroService() => patronHeroState?.Clear();

        public void CancelCounterPatron(BarCounterPatronRefill patron)
        {
            if (ReferenceEquals(activePatronRefill, patron)) FinishCounterPatronService(false);
            else serviceQueue.Cancel(patron);
        }

        private void CancelPatronService()
        {
            // Teardown is not a service completion: never grant another owner on a disabled shop.
            for (int index = 0; index < serviceQueue.QueuedCount; index++)
                if (serviceQueue.GetQueuedOwner(index) is BarCounterPatronRefill queued) queued.AbortRefill();
            serviceQueue.Clear();
            if (IsPatronServiceActive) FinishCounterPatronService(false);
        }

        private void FinishCounterPatronService(bool completed)
        {
            BarCounterPatronRefill patron = activePatronRefill;
            serviceView.HideCarriedBottle();
            serviceView.HidePourStream();
            serviceView.SetBeerTapBartenderContact(false, 0f, 0f);
            serviceView.SetBeerTapHandlePull(0f);
            serviceView.SelectedBottle?.ResetExact();
            if (!completed && patron != null && patron.Vessel != null)
                patron.Vessel.SetWorldPose(patronVesselDock.position, patronVesselDock.rotation);
            activePatronRefill = null;
            patronServiceTimeline.Reset();
            patronPresentationPrepared = false;
            patronCupOfferPending = false;
            patronCupOfferElapsed = 0f;
            patronCupOfferOffset = Vector3.zero;
            patronCupReturnPending = false;
            patronCupReturnElapsed = 0f;
            patronHeroState.Restore(this);
            patronHeroState = null;
            if (patron != null)
            {
                if (completed) patron.CompleteRefill(); else patron.AbortRefill();
            }
            // Grant callbacks run only after every hero field and view binding is restored.
            if (completed) serviceQueue.Release(patron); else serviceQueue.Cancel(patron);
        }

        private sealed class PatronPresentationBackup
        {
            public BarDrinkVesselView Vessel;
            private DrinkId selectedBottle, order;
            private BarDrinkPresentation presentation;
            private readonly Vector3[] points;
            private readonly Quaternion[] rotations;
            private readonly Pose[] poses;
            private readonly bool[] flags;
            public Vector3 Offset;
            public bool Mirrored;
            public PatronPresentationBackup(BarDrinkShopController shop)
            {
                Vessel = shop.serviceView.ActiveVessel;
                selectedBottle = shop.serviceView.SelectedBottle != null ? shop.serviceView.SelectedBottle.DrinkId : DrinkId.None;
                order = shop.currentOrderDrinkId; presentation = shop.activePresentation;
                Offset = shop.activeServiceLocalOffset; Mirrored = shop.activeServiceMirrored;
                points = new[] { shop.bottleStartPosition, shop.bottleShelfReturnPosition, shop.vesselBaseScale,
                    shop.activeBottleHandTarget, shop.activeBottleReachCorrection, shop.activeBottleHandRadial,
                    shop.beerPlacementStartPosition, shop.bottleReturnHandStart };
                rotations = new[] { shop.bottleStartRotation, shop.bottleShelfReturnRotation, shop.activeBottleWorldRotation,
                    shop.beerPlacementStartRotation, shop.emptyVesselRotationInServer };
                poses = new[] { shop.emptyVesselCounterPose, shop.emptyVesselStowStartPose,
                    shop.emptyVesselStorageServerPose, shop.repeatTapStartPose };
                flags = new[] { shop.beerPlacementCaptured, shop.bottleGripAttached, shop.bottleReturnCaptured,
                    shop.emptyVesselStowCaptured, shop.pendingOrderedDrinkPresentation, shop.counterServerAtTarget,
                    shop.clinkPlayed, shop.pourPlayed, shop.gulpPlayed };
            }
            public void Restore(BarDrinkShopController shop)
            {
                shop.serviceView.SelectServiceVessel(Vessel);
                if (selectedBottle != DrinkId.None) shop.serviceView.SelectBottle(selectedBottle); else shop.serviceView.ClearSelection();
                shop.currentOrderDrinkId = order; shop.activePresentation = presentation;
                shop.activeServiceLocalOffset = Offset; shop.activeServiceMirrored = Mirrored;
                shop.bottleStartPosition=points[0]; shop.bottleShelfReturnPosition=points[1]; shop.vesselBaseScale=points[2];
                shop.activeBottleHandTarget=points[3]; shop.activeBottleReachCorrection=points[4]; shop.activeBottleHandRadial=points[5];
                shop.beerPlacementStartPosition=points[6]; shop.bottleReturnHandStart=points[7];
                shop.bottleStartRotation=rotations[0]; shop.bottleShelfReturnRotation=rotations[1]; shop.activeBottleWorldRotation=rotations[2];
                shop.beerPlacementStartRotation=rotations[3]; shop.emptyVesselRotationInServer=rotations[4];
                shop.emptyVesselCounterPose=poses[0]; shop.emptyVesselStowStartPose=poses[1]; shop.emptyVesselStorageServerPose=poses[2]; shop.repeatTapStartPose=poses[3];
                shop.beerPlacementCaptured=flags[0]; shop.bottleGripAttached=flags[1]; shop.bottleReturnCaptured=flags[2];
                shop.emptyVesselStowCaptured=flags[3]; shop.pendingOrderedDrinkPresentation=flags[4]; shop.counterServerAtTarget=flags[5];
                shop.clinkPlayed=flags[6]; shop.pourPlayed=flags[7]; shop.gulpPlayed=flags[8];
            }
            public void Clear()
            {
                if (Vessel != null) Vessel.gameObject.SetActive(false);
                Vessel = null; selectedBottle = order = DrinkId.None; presentation = default;
                Offset = Vector3.zero; Mirrored = false;
                Array.Clear(points, 0, points.Length); points[2] = Vector3.one;
                for (int index = 0; index < rotations.Length; index++) rotations[index] = Quaternion.identity;
                Array.Clear(poses, 0, poses.Length); Array.Clear(flags, 0, flags.Length);
            }
        }
    }
}
