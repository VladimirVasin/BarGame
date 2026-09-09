using UnityEngine;

namespace BarPromenade
{
    public sealed partial class AlpineVillageLifeController
    {
        private readonly Transform[] looseLogs = new Transform[VillageHouseholdProgress.LogCount];
        private readonly Vector3[] logSupports = new Vector3[VillageHouseholdProgress.LogCount];
        private readonly bool[] basketReservations = new bool[AlpineVillageLifePlan.BasketCount];
        private readonly bool[] standReservations = new bool[2];
        private int workDeliveryStand;
        private int workBasket, heldLog = -1, loadingLog = -1;
        private bool logContact;
        public int LoadingLogId => heldLog;
        public int WorkingBasket => workBasket;
        public System.Collections.Generic.IReadOnlyList<Transform> FirewoodLogs => looseLogs;

        private void BuildLooseLogs(Transform stack)
        {
            for (int i = 0; i < looseLogs.Length; i++)
            {
                Transform log = VillageLifePropLibrary.Create(VillageLifePropKind.Log, stack).transform;
                log.localPosition = VillageLifePropLibrary.GetAnchor(VillageLifePropKind.LogStack, "Log" + i % 3) +
                    Vector3.up * (i / 3 * .12f);
                looseLogs[i] = log; logSupports[i] = log.position;
            }
        }

        private void InitializeFirewood()
        {
            for (int i = 0; i < baskets.Count; i++) RestoreBasketToSupport(i);
            var count = new int[baskets.Count];
            for (int i = 0; i < looseLogs.Length; i++)
            {
                int basket = GameSessionState.VillageHousehold.GetLogBasket(i);
                if (basket >= 0) PutLogInBasket(i, basket, count[basket]++);
            }
            bool hasWork = SelectNextBasket();
            Woman.transform.position = hasWork ? Plan.Dock(Plan.Pickups[workBasket]) : Plan.Rest;
            SetStage(hasWork ? VillageWomanStage.Waiting : VillageWomanStage.Resting);
        }

        private bool SelectNextBasket()
        {
            for (int i = 0; i < baskets.Count; i++)
                if (!basketReservations[i] && GameSessionState.VillageHousehold.GetBasketDeliveryStand(i) < 0)
                { workBasket = i; return true; }
            return false;
        }

        public bool CanReserveFirewood(int index) => index >= 0 && index < baskets.Count &&
            !basketReservations[index] && GameSessionState.VillageHousehold.GetBasketDeliveryStand(index) < 0 &&
            GameSessionState.VillageHousehold.GetBasketLogCount(index) == 3 &&
            !(index == workBasket && (basketAttached || WomanStage == VillageWomanStage.PickingUp ||
                WomanStage == VillageWomanStage.PuttingDown || WomanStage == VillageWomanStage.PlacingLog));

        public bool ReserveFirewood(int index)
        {
            if (!CanReserveFirewood(index)) return false;
            basketReservations[index] = true;
            if (index == workBasket && route == null && WomanStage == VillageWomanStage.Waiting)
            {
                // Leave the pickup on its houseward side before crossing the
                // yard. Walking directly toward the waiting helper makes both
                // bodies politely block each other at the same narrow dock.
                Vector3 escape = Plan.Ground(Woman.transform.position - Plan.Right * .85f - Plan.Forward * .22f);
                StartWalk(new[] { Woman.transform.position, escape, Plan.Yard(-.8f, 3.5f), Plan.Rest },
                    VillageWomanStage.WalkingToRest, VillageWomanStage.Resting, Plan.Forward);
            }
            return true;
        }

        public void ReleaseFirewood(int index, bool restore)
        {
            if (index < 0 || index >= baskets.Count) return;
            basketReservations[index] = false;
            if (restore) RestoreBasketToSupport(index);
        }

        public bool CanReserveFirewoodStand(int index) => index >= 0 && index < 2 && !standReservations[index] &&
            GameSessionState.VillageHousehold.GetBasketAtStand(index) < 0 &&
            !((basketAttached || WomanStage == VillageWomanStage.PickingUp || WomanStage == VillageWomanStage.PuttingDown) && workDeliveryStand == index);
        public bool ReserveFirewoodStand(int index)
        {
            if (!CanReserveFirewoodStand(index)) return false;
            standReservations[index] = true; return true;
        }
        public void ReleaseFirewoodStand(int index) { if (index >= 0 && index < 2) standReservations[index] = false; }

        public void RestoreBasketToSupport(int index)
        {
            int stand = GameSessionState.VillageHousehold.GetBasketDeliveryStand(index);
            baskets[index].SetPositionAndRotation((stand >= 0 ? Plan.Deliveries[stand] : Plan.Pickups[index]) +
                Vector3.up * AlpineVillageLifePlan.StandHeight, Quaternion.LookRotation(-Plan.Forward));
        }

        private void BeginNextFirewoodJob()
        {
            if (basketReservations[workBasket] || GameSessionState.VillageHousehold.GetBasketDeliveryStand(workBasket) >= 0)
            { WalkToRest(); return; }
            if (GameSessionState.VillageHousehold.GetBasketLogCount(workBasket) >= 3)
            {
                workDeliveryStand = -1;
                for (int i = 0; i < 2; i++)
                    if (!standReservations[i] && GameSessionState.VillageHousehold.GetBasketAtStand(i) < 0)
                    { workDeliveryStand = i; break; }
                if (workDeliveryStand < 0) return;
                SetStage(VillageWomanStage.PickingUp); return;
            }
            loadingLog = -1;
            // Remove the upper row before the logs supporting it.
            for (int i = looseLogs.Length - 1; i >= 0; i--)
                if (GameSessionState.VillageHousehold.GetLogBasket(i) < 0) { loadingLog = i; break; }
            if (loadingLog < 0) { WalkToRest(); return; }
            logContact = false;
            Vector3 dock = Plan.Ground(logSupports[loadingLog] + Plan.Forward * .44f);
            StartWalk(new[] { Woman.transform.position, Plan.Yard(3.3f, 3.5f), Plan.Yard(3.3f, 2f),
                Plan.Ground(dock + Plan.Forward * .38f), dock },
                VillageWomanStage.WalkingToStack, VillageWomanStage.LoadingLog, -Plan.Forward);
        }

        private bool AdvanceFirewoodLoading(float dt)
        {
            if (route != null || (WomanStage != VillageWomanStage.LoadingLog && WomanStage != VillageWomanStage.PlacingLog)) return false;
            stageTime += dt;
            bool placing = WomanStage == VillageWomanStage.PlacingLog;
            Woman.Apply(placing ? VillageResidentAction.Place : VillageResidentAction.Reach, stageTime);
            Transform log = looseLogs[loadingLog];
            Quaternion rotation = Woman.transform.rotation;
            Vector3 carried = Woman.transform.position + rotation * new Vector3(0f, .93f, .44f);
            int slot = GameSessionState.VillageHousehold.GetBasketLogCount(workBasket);
            if (placing && logContact) slot--;
            Vector3 supported = placing ? baskets[workBasket].TransformPoint(
                VillageLifePropLibrary.GetAnchor(VillageLifePropKind.Basket, "Content" + slot)) : logSupports[loadingLog];
            float transfer = SmoothFirewood((stageTime - (placing ? 0f : 1.5f)) / 1.5f);
            if (placing) log.position = Vector3.Lerp(carried, supported, transfer);
            else if (stageTime >= 1.5f)
            {
                heldLog = loadingLog; log.SetParent(transform, true);
                log.position = Vector3.Lerp(supported, carried, transfer);
            }
            log.rotation = rotation;
            float weight = placing ? 1f - SmoothFirewood((stageTime - 1.5f) / 1.5f) : SmoothFirewood(stageTime / 1.5f);
            Woman.ApplyHandContacts(log.position + rotation * Vector3.right * .18f,
                log.position - rotation * Vector3.right * .18f, weight);
            if (placing && stageTime >= 1.5f && !logContact)
            {
                logContact = true; heldLog = -1;
                if (GameSessionState.VillageHousehold.TryLoadLog(workBasket, loadingLog))
                { PutLogInBasket(loadingLog, workBasket, slot); sound.PlayWood(log.position, .65f); }
            }
            if (stageTime < 3f) return true;
            if (placing)
            { loadingLog = -1; logContact = false; SetStage(VillageWomanStage.Waiting); }
            else
            {
                StartWalk(new[] { Woman.transform.position, Plan.Yard(3.3f, 2f), Plan.Yard(3.3f, 3.5f),
                    Plan.Yard(1.45f + workBasket * 1.05f, 3.5f), Plan.Dock(Plan.Pickups[workBasket]) },
                    VillageWomanStage.WalkingWithLog, VillageWomanStage.PlacingLog, -Plan.Forward);
            }
            return true;
        }

        private void PutLogInBasket(int log, int basket, int slot)
        {
            looseLogs[log].SetParent(baskets[basket], true);
            looseLogs[log].localPosition = VillageLifePropLibrary.GetAnchor(VillageLifePropKind.Basket, "Content" + slot);
            looseLogs[log].localRotation = Quaternion.Euler(0f, slot == 2 ? 7f : -4f, 0f);
        }

        private Vector3[] ActiveFirewoodRoute() => new[] { Plan.Dock(Plan.Pickups[workBasket]),
            Plan.Yard(1.45f + workBasket * 1.05f, 3.5f), Plan.Yard(-1.5f - workDeliveryStand * 1.05f, 3.5f),
            Plan.Dock(Plan.Deliveries[workDeliveryStand]) };

        private void FollowLoadingLog()
        {
            if (heldLog < 0) return;
            Quaternion rotation = Woman.transform.rotation;
            Transform log = looseLogs[heldLog];
            log.SetPositionAndRotation(Woman.transform.position + rotation * new Vector3(0f, .93f, .44f), rotation);
            Woman.ApplyHandContacts(log.position + rotation * Vector3.right * .18f,
                log.position - rotation * Vector3.right * .18f, 1f);
        }

        private static float SmoothFirewood(float value) { value = Mathf.Clamp01(value); return value * value * (3f - 2f * value); }

        public void Thank(VillageResidentRole role, string key, bool interrupt = true)
        {
            var actor = neighbours[(int)role].Actor;
            if (hero == null || Vector3.Distance(actor.transform.position, hero.position) > 6f) return;
            if (bubbles.Show(actor, LocalizationService.Get(key)) && interrupt)
                speechRemaining[(int)role] = NpcSpeechBubbleView.VisibleSeconds;
        }
    }
}
