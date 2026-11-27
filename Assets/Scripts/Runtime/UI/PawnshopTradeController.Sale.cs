using UnityEngine;

namespace BarPromenade
{
    public sealed partial class PawnshopTradeController
    {
        private const float ContactTime = 1.10f, EndTime = 2.28f;
        private readonly PawnshopFindCatalog.Offer[] sellable = new PawnshopFindCatalog.Offer[PawnshopFindCatalog.Offers.Length];
        private VillageResidentPresentation clerk;
        private Transform counterFrame, trayItem, itemGrip, clerkSpine;
        private NpcHandPose clerkHands;
        private WorldItemFoundScreen saleScreen;
        private PawnshopFindCatalog.Offer quote;
        private int sellableCount, selectedFind;
        private Pose clerkRest, startingShot, counterShot;
        private Camera saleCamera;
        private System.IDisposable cameraLoan;
        private float transferTime, startingFov;
        private bool credited, cleaning;
        internal bool IsHandingOver { get; private set; }

        internal void BindCounter(VillageResidentPresentation resident, Transform frame)
        {
            clerk = resident;
            counterFrame = frame;
            clerkSpine = CityPedestrianHandProps.FindSocket(clerk.ModelRoot, "spine");
            clerkHands = clerk.GetComponent<NpcHandPose>();
            clerkRest = new Pose(clerk.transform.position, clerk.transform.rotation);
        }

        private void RefreshSellable()
        {
            sellableCount = 0;
            foreach (var offer in PawnshopFindCatalog.Offers)
                if (GameSessionState.GetInventoryItemCount(offer.Item) > 0)
                    sellable[sellableCount++] = offer;
            selectedFind = Mathf.Clamp(selectedFind, 0, sellableCount);
        }

        private bool BeginQuote(int index)
        {
            if (index < 0 || index >= sellableCount || saleScreen || IsHandingOver || !clerk) return false;
            quote = sellable[index];
            if (!GameSessionState.CanSellPawnshopFind(quote)) return false;
            saleScreen = WorldItemFoundScreen.For(player.Interactor);
            if (!saleScreen) return false;
            try
            {
                trayItem = InventoryItemModelFactory.BuildWorldModel(quote.Item, counterFrame, Vector3.one);
                trayItem.localPosition = PawnshopInteriorLayoutPlan.SaleTrayEntry;
                itemGrip = PawnshopFindCatalog.FindAnchor(trayItem, "Grip");
                var definition = InventoryItemCatalog.Get(quote.Item);
                string action = string.Format(LocalizationService.Get("pawnshop.trade.sell_price"), quote.Price);
                if (saleScreen.TryPresentActionWithin(modalLock, player.Interactor, quote.Item, trayItem,
                    definition.NameLocalizationKey, action, LocalizationService.Get(definition.DescriptionLocalizationKey),
                    "pawnshop.trade.sale_changed", CanConfirmSale, FinishQuote))
                    return true;
                ReleaseSale();
                return false;
            }
            catch
            {
                Close();
                throw;
            }
        }

        private bool CanConfirmSale() => IsOpen && modalLock.IsLocked && isActiveAndEnabled && clerk &&
            !SceneTransitionService.IsTransitioning && GameSessionState.CanSellPawnshopFind(quote);

        private void FinishQuote(bool accepted)
        {
            if (cleaning) return;
            saleScreen = null;
            if (!accepted || !CanConfirmSale())
            {
                ReleaseSale();
                RefreshSellable();
                inputUnlockFrame = Time.frameCount + 1;
                return;
            }

            trayItem.gameObject.SetActive(true);
            IsHandingOver = true;
            transferTime = 0f;
            BeginCounterShot();
        }

        private void LateUpdate()
        {
            if (!IsHandingOver || GameTimeScaleRuntime.IsPaused) return;
            if (!clerk || !trayItem || !modalLock.IsLocked || SceneTransitionService.IsTransitioning)
            {
                Close();
                return;
            }

            transferTime += Time.deltaTime;
            float back = Ease(1.60f, EndTime);
            float walk = Ease(0f, .83f) * (1f - back);
            clerk.transform.SetPositionAndRotation(clerkRest.position + clerkRest.rotation * Vector3.forward * (.70f * walk),
                clerkRest.rotation);
            trayItem.localPosition = Vector3.Lerp(PawnshopInteriorLayoutPlan.SaleTrayEntry,
                PawnshopInteriorLayoutPlan.SaleTrayPickup, Ease(.12f, .96f)) +
                new Vector3(.10f, .13f, .26f) * Ease(ContactTime, 1.53f);
            float grip = Ease(.73f, ContactTime) * (1f - Ease(1.43f, 1.64f));
            bool walking = transferTime < .80f || transferTime > 1.61f;
            clerk.ApplyLocomotion(walking ? .75f * Mathf.Sin(walk * Mathf.PI) : 0f,
                false, walk * .94f, itemGrip.position);
            if (grip > 0f)
            {
                clerkSpine.rotation = Quaternion.AngleAxis(19f * grip, clerk.transform.right) * clerkSpine.rotation;
                clerk.ApplyHandContacts(itemGrip.position, null, grip);
                clerkHands.SetGrip(false, .72f * grip);
            }
            PresentCounterShot(Ease(0f, .42f) * (1f - Ease(1.54f, EndTime)));
            if (!credited && transferTime >= ContactTime)
            {
                if (!GameSessionState.TrySellPawnshopFind(quote))
                {
                    Close();
                    player.Interactor.ShowFeedback("pawnshop.trade.sale_changed", 1.8f);
                    return;
                }
                credited = true;
                RetroAudio.PlayAt(RetroSfxId.BinFoundMetal, trayItem.position, .70f);
            }
            if (transferTime >= 1.65f && trayItem.gameObject.activeSelf) trayItem.gameObject.SetActive(false);
            if (transferTime < EndTime) return;

            int price = quote.Price;
            Close();
            player.Interactor.ShowFormattedFeedback("pawnshop.trade.sold", 1.8f, price);
        }

        private float Ease(float from, float to) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, transferTime));

        private void BeginCounterShot()
        {
            if (!follow || follow.PresentationSuspended) return;
            saleCamera = follow.GetComponent<Camera>();
            if (!saleCamera) return;
            startingShot = new Pose(saleCamera.transform.position, saleCamera.transform.rotation);
            startingFov = saleCamera.fieldOfView;
            Vector3 eye = counterFrame.TransformPoint(new Vector3(.64f, 1.66f, -.48f));
            Vector3 target = counterFrame.TransformPoint(new Vector3(0f, 1.28f, .70f));
            counterShot = new Pose(eye, Quaternion.LookRotation(target - eye));
            cameraLoan = follow.SuspendPresentation();
        }

        private void PresentCounterShot(float weight)
        {
            if (cameraLoan == null || !saleCamera || DebugFreeCamera.IsAnyActive) return;
            saleCamera.transform.SetPositionAndRotation(Vector3.Lerp(startingShot.position, counterShot.position, weight),
                Quaternion.Slerp(startingShot.rotation, counterShot.rotation, weight));
            saleCamera.fieldOfView = Mathf.Lerp(startingFov, 48f, weight);
        }

        private void ReleaseSale()
        {
            cleaning = true;
            if (saleScreen && saleScreen.IsPresenting) saleScreen.Abandon();
            saleScreen = null;
            PresentCounterShot(0f);
            cameraLoan?.Dispose();
            cameraLoan = null;
            saleCamera = null;
            if (clerk && IsHandingOver)
            {
                clerk.transform.SetPositionAndRotation(clerkRest.position, clerkRest.rotation);
                clerkHands?.ResetGrip();
                clerk.ApplyIdleVariation(0f, false, 0f);
            }
            if (trayItem) Destroy(trayItem.gameObject);
            trayItem = itemGrip = null;
            credited = IsHandingOver = cleaning = false;
        }

        private void DrawSellList()
        {
            const int rowsPerPage = 5;
            int first = Mathf.Min(selectedFind, sellableCount - 1) / rowsPerPage * rowsPerPage;
            int end = Mathf.Min(first + rowsPerPage, sellableCount);
            for (int index = first; index < end; index++)
            {
                var offer = sellable[index];
                string name = LocalizationService.Get(InventoryItemCatalog.Get(offer.Item).NameLocalizationKey);
                int count = GameSessionState.GetInventoryItemCount(offer.Item);
                string label = string.Format(LocalizationService.Get("pawnshop.trade.sell_row"), name, count, offer.Price);
                if (DrawRow(label, 120f + (index-first) * 24f, selectedFind == index))
                    BeginQuote(index);
            }
            if (sellableCount <= rowsPerPage)
            {
                if (DrawRow(LocalizationService.Get("pawnshop.trade.close"), 253f, selectedFind == sellableCount)) Close();
                return;
            }
            if (first > 0 && RetroUiInput.Button(new Rect(170,251,32,22), "‹", optionStyle))
                selectedFind = first - rowsPerPage;
            if (end < sellableCount && RetroUiInput.Button(new Rect(438,251,32,22), "›", optionStyle))
                selectedFind = end;
            GUI.Label(new Rect(208,251,53,22), $"{first/rowsPerPage+1}/{(sellableCount-1)/rowsPerPage+1}", textStyle);
            var close = new Rect(266,251,164,22);
            RetroUiTheme.DrawSelection(close, selectedFind == sellableCount);
            if (RetroUiInput.Button(close, LocalizationService.Get("pawnshop.trade.close"),
                selectedFind == sellableCount ? selectedStyle : optionStyle)) Close();
        }

        private bool DrawRow(string text, float y, bool selected)
        {
            var rect = new Rect(170f, y, 300f, 22f);
            RetroUiTheme.DrawSelection(rect, selected);
            return RetroUiInput.Button(rect, text, selected ? selectedStyle : optionStyle);
        }
    }
}
