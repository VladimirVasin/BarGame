using UnityEngine;

namespace BarPromenade
{
    /// <summary>Owns the quote and the single counter handoff for the active player.</summary>
    [DefaultExecutionOrder(225), DisallowMultipleComponent]
    internal sealed class CityContainerReturnInteraction : MonoBehaviour, IInteractable
    {
        private readonly BarMinigameModalLock modal = new BarMinigameModalLock();
        private CityGlassReturnWorker worker;
        private CityGlassReturnSitePlan site;
        private PlayerInteractor hero;
        private WorldItemFoundScreen screen;
        private Transform batch, glass, can;
        private Transform glassGrip, canGrip;
        private GameSessionState.ReturnableContainers quote;
        private Camera lens;
        private System.IDisposable cameraLoan;
        private Pose initialCamera, counterCamera;
        private float initialFov, elapsed;
        private bool transferring, committed, closing;
        private const float ContactTime = .46f, TransferEnd = 1.34f;

        public Vector3 InteractionPosition => site.CounterWorld;
        public string PromptKey => Available ? "container_return.prompt" : "container_return.closed_prompt";
        private bool Available => worker && worker.isActiveAndEnabled && worker.IsWorking &&
            !worker.IsUsingSwitch && worker.WindowGrilleOpenness > .999f &&
            GameSessionState.GameTimeOfDayMinutes >= CityGlassReturnWorker.ShiftStartMinute &&
            GameSessionState.GameTimeOfDayMinutes < CityGlassReturnWorker.ShiftEndMinute;

        internal void Initialize(CityGlassReturnWorker owner, CityGlassReturnSitePlan plan)
        {
            worker = owner;
            site = plan;
        }

        public bool CanInteract(PlayerInteractor interactor) => site != null && interactor &&
            interactor.InputEnabled && !interactor.InteractKeyClaimed && isActiveAndEnabled &&
            !BarMinigameModalLock.IsAnyLocked && !SceneTransitionService.IsTransitioning &&
            !GameTimeScaleRuntime.IsPaused && AtWindow(interactor);

        private bool AtWindow(PlayerInteractor interactor)
        {
            Vector3 offset = Quaternion.Inverse(site.Rotation) * (interactor.transform.position - site.HeroDock);
            return Mathf.Abs(offset.x) <= .92f && Mathf.Abs(offset.y - PlayerFactory.GroundedRootOffset) <= .38f &&
                offset.z >= -.32f && offset.z <= .84f;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor)) return;
            if (!Available)
            {
                bool closedForDay = GameSessionState.GameTimeOfDayMinutes < CityGlassReturnWorker.ShiftStartMinute ||
                    GameSessionState.GameTimeOfDayMinutes >= CityGlassReturnWorker.ShiftEndMinute;
                interactor.ShowFeedback(closedForDay ? "container_return.closed_until_eight" : "container_return.wait", 1.8f);
                return;
            }

            quote = GameSessionState.QuoteContainerReturn();
            if (quote.Total == 0)
            {
                interactor.ShowFeedback("container_return.no_empties", 1.8f);
                return;
            }
            screen = WorldItemFoundScreen.For(interactor);
            if (!screen || !modal.TryCaptureAndDisable(interactor, null, null)) return;
            hero = interactor;
            try
            {
                batch = new GameObject("Returnable containers").transform;
                batch.SetParent(transform, false);
                SetBatchPosition(0f);
                if (quote.Bottles > 0)
                    glass = BuildSample(quote.GlassSample, quote.Cans > 0 ? -.105f : 0f, out glassGrip);
                if (quote.Cans > 0)
                    can = BuildSample(InventoryItemId.EmptyBeerCan, quote.Bottles > 0 ? .105f : 0f, out canGrip);
                var item = quote.Bottles > 0 ? quote.GlassSample : InventoryItemId.EmptyBeerCan;
                string detail = string.Format(LocalizationService.Get("container_return.quote"),
                    quote.Bottles, quote.Bottles * GameSessionState.ReturnableContainers.BottlePrice,
                    quote.Cans, quote.Cans * GameSessionState.ReturnableContainers.CanPrice);
                string action = string.Format(LocalizationService.Get("container_return.confirm"), quote.Total);
                if (!screen.TryPresentActionWithin(modal, hero, item, batch, "container_return.title", action, detail,
                    "container_return.changed", CanConfirm, FinishQuote)) Close();
            }
            catch
            {
                Close();
                throw;
            }
        }

        private Transform BuildSample(InventoryItemId item, float x, out Transform grip)
        {
            var sample = InventoryItemModelFactory.BuildWorldModel(item, batch, Vector3.one);
            sample.localPosition = Vector3.right * x;
            grip = sample.GetComponent<VodkaBottleView>().Grip;
            return sample;
        }

        private bool CanConfirm() => hero && !SceneTransitionService.IsTransitioning &&
            Available && AtWindow(hero) && GameSessionState.CanReturnContainers(quote);

        private void FinishQuote(bool accepted)
        {
            if (closing) return;
            screen = null;
            if (!accepted || !CanConfirm())
            {
                Close();
                return;
            }
            batch.gameObject.SetActive(true);
            transferring = true;
            worker.BeginReceiving();
            BeginCounterShot();
        }

        private void SetBatchPosition(float motion)
        {
            float z = Mathf.Lerp(1.69f, 1.30f, motion);
            Vector3 position = site.Origin + site.Rotation * new Vector3(0f, 1.021f, z);
            batch.SetPositionAndRotation(position, site.Rotation);
        }

        private void LateUpdate()
        {
            if (!modal.IsLocked || GameTimeScaleRuntime.IsPaused) return;
            if (!hero || !Available || SceneTransitionService.IsTransitioning || !AtWindow(hero))
            {
                Close();
                return;
            }
            if (!transferring) return;
            elapsed += Time.deltaTime;
            float reach = Ease(.09f, ContactTime) * (1f - Ease(.87f, 1.13f));
            SetBatchPosition(Ease(ContactTime, .87f));
            worker.PoseReceiving(elapsed, glassGrip ? glassGrip.position : null,
                canGrip ? canGrip.position : null, reach);
            PresentCounterShot(Ease(0f, .34f) * (1f - Ease(.88f, TransferEnd)));
            if (!committed && elapsed >= ContactTime)
            {
                if (!GameSessionState.TryReturnContainers(quote))
                {
                    hero.ShowFeedback("container_return.changed", 1.8f);
                    Close();
                    return;
                }
                committed = true;
                if (glass) RetroAudio.PlayAt(RetroSfxId.BinFoundGlass, glass.position);
                if (can) RetroAudio.PlayAt(RetroSfxId.BinFoundMetal, can.position);
            }
            if (elapsed >= 1.13f && batch.gameObject.activeSelf) batch.gameObject.SetActive(false);
            if (elapsed < TransferEnd) return;
            int paid = quote.Total;
            var player = hero;
            Close();
            player.ShowFormattedFeedback("container_return.received", 1.8f, paid);

            float Ease(float from, float to) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, elapsed));
        }

        private void BeginCounterShot()
        {
            lens = Camera.main;
            if (!lens || !lens.TryGetComponent(out PlayerCameraFollow follow) || follow.PresentationSuspended) return;
            initialCamera = new Pose(lens.transform.position, lens.transform.rotation);
            initialFov = lens.fieldOfView;
            float heroX = (Quaternion.Inverse(site.Rotation) * (hero.transform.position - site.HeroDock)).x;
            float side = heroX < 0f ? 1f : -1f;
            Vector3 position = site.Origin + site.Rotation * new Vector3(side * .70f, 1.66f, 2.69f);
            Vector3 aim = site.Origin + site.Rotation * new Vector3(0f, 1.34f, 1.40f);
            counterCamera = new Pose(position, Quaternion.LookRotation(aim - position));
            cameraLoan = follow.SuspendPresentation();
        }

        private void PresentCounterShot(float weight)
        {
            if (cameraLoan == null || !lens || DebugFreeCamera.IsAnyActive) return;
            lens.transform.SetPositionAndRotation(Vector3.Lerp(initialCamera.position, counterCamera.position, weight),
                Quaternion.Slerp(initialCamera.rotation, counterCamera.rotation, weight));
            lens.fieldOfView = Mathf.Lerp(initialFov, 58f, weight);
        }

        private void Close()
        {
            if (closing) return;
            closing = true;
            if (screen && screen.IsPresenting) screen.Abandon();
            screen = null;
            PresentCounterShot(0f);
            cameraLoan?.Dispose();
            cameraLoan = null;
            lens = null;
            if (worker && transferring) worker.EndReceiving();
            if (batch) Destroy(batch.gameObject);
            batch = glass = can = glassGrip = canGrip = null;
            hero = null;
            transferring = committed = false;
            elapsed = 0f;
            modal.Restore();
            closing = false;
        }

        private void OnDisable() => Close();
    }
}
