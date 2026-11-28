using System;
using System.Collections;
using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class HomeMailbox : MonoBehaviour, IInteractable
    {
        internal const string SourceId = "city.home.mailbox.bills";
        private const string MailboxModelPath = "City/HomeMailbox/HomeMailbox";
        private const float OpenAngle = 99f;
        private const float LidTravelSeconds = .32f;

        private readonly BarMinigameModalLock modal = new BarMinigameModalLock();
        private Transform lid;
        private Transform bills;
        private Quaternion closedRotation;
        private Vector3 hingeAxis;
        private WorldItemFoundScreen activeScreen;

        internal static bool HasMail => !GameSessionState.IsWorldItemCollected(SourceId);

        public string PromptKey => "interaction.home_mailbox.check";
        public Vector3 InteractionPosition => transform.TransformPoint(0f, 1.10f, .235f);

        internal static void Create(Transform parent, Vector3 localBase, Vector3 forward)
        {
            Transform root = new GameObject("Home Mailbox").transform;
            root.SetParent(parent, false);
            root.SetLocalPositionAndRotation(localBase, Quaternion.LookRotation(forward));
            Transform model = InstantiateModel(MailboxModelPath, root);
            Transform lid = RequireAnchor(model, "LidHinge");
            Transform paperRest = RequireAnchor(model, "MailRest");
            Transform paper = InventoryItemModelFactory.BuildWorldModel(
                InventoryItemId.HomeBills, root, new Vector3(.22f, .023f, .16f));
            paper.SetPositionAndRotation(paperRest.position, root.rotation);

            BoxCollider target = root.gameObject.AddComponent<BoxCollider>();
            target.isTrigger = true;
            target.center = new Vector3(0f, 1.1f, .23f);
            target.size = new Vector3(.48f, .30f, .16f);

            HomeMailbox mailbox = root.gameObject.AddComponent<HomeMailbox>();
            mailbox.lid = lid;
            mailbox.bills = paper;
            mailbox.closedRotation = lid.localRotation;
            mailbox.hingeAxis = lid.InverseTransformDirection(root.right);
            mailbox.RefreshMail();
        }

        private static Transform InstantiateModel(string path, Transform parent)
        {
            GameObject asset = Resources.Load<GameObject>(path);
            if (asset == null) throw new InvalidOperationException("Missing authored model: " + path);
            return Instantiate(asset, parent, false).transform;
        }

        private static Transform RequireAnchor(Transform model, string name)
        {
            foreach (Transform node in model.GetComponentsInChildren<Transform>(true))
            {
                if (node.name == name) return node;
            }

            throw new InvalidOperationException("Missing authored mailbox anchor: " + name);
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (!isActiveAndEnabled || !HasMail || modal.IsLocked || interactor == null ||
                !interactor.InputEnabled || interactor.InteractKeyClaimed ||
                SceneTransitionService.IsTransitioning || CounterMenuInput.IsBlockedByOtherUi())
                return false;

            Vector3 target = InteractionPosition;
            Vector3 player = interactor.transform.position;
            return (player + Vector3.up * .8f - target).sqrMagnitude <=
                   PlayerInteractor.InteractionRadius * PlayerInteractor.InteractionRadius &&
                   !Physics.Linecast(player + Vector3.up * 1.45f, target,
                       Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor)) return;

            WorldItemFoundScreen screen = WorldItemFoundScreen.For(interactor);
            if (screen == null || !screen.IsInitialized ||
                !modal.TryCaptureAndDisable(interactor, null, FindAnyObjectByType<IntoxicationHudView>()))
                return;

            StartCoroutine(OpenAndInspect(interactor, screen));
        }

        private IEnumerator OpenAndInspect(PlayerInteractor interactor, WorldItemFoundScreen screen)
        {
            try
            {
                yield return MoveLid(0f, OpenAngle);
                if (interactor != null && screen != null && HasMail && !SceneTransitionService.IsTransitioning &&
                    screen.TryPresentWithin(modal, interactor, InventoryItemId.HomeBills, bills,
                        TakeBills, FinishInspection))
                {
                    activeScreen = screen;
                    while (activeScreen != null && !SceneTransitionService.IsTransitioning)
                        yield return null;
                    if (activeScreen != null) activeScreen.Abandon();
                }

                yield return MoveLid(OpenAngle, 0f);
            }
            finally
            {
                ResetPresentation();
            }
        }

        private bool TakeBills() =>
            isActiveAndEnabled && !SceneTransitionService.IsTransitioning &&
            GameSessionState.TryCollectWorldItem(SourceId, InventoryItemId.HomeBills);

        private void FinishInspection(bool taken)
        {
            activeScreen = null;
        }

        private IEnumerator MoveLid(float start, float end)
        {
            for (float elapsed = 0f; elapsed < LidTravelSeconds; elapsed += Time.unscaledDeltaTime)
            {
                SetLidAngle(Mathf.SmoothStep(start, end, elapsed / LidTravelSeconds));
                yield return null;
            }

            SetLidAngle(end);
        }

        private void SetLidAngle(float angle)
        {
            lid.localRotation = closedRotation * Quaternion.AngleAxis(-angle, hingeAxis);
        }

        private void RefreshMail()
        {
            if (bills != null && !modal.IsLocked) bills.gameObject.SetActive(HasMail);
        }

        private void OnEnable()
        {
            GameSessionState.WorldItemsChanged += RefreshMail;
            RefreshMail();
        }

        private void OnDisable()
        {
            GameSessionState.WorldItemsChanged -= RefreshMail;
            StopAllCoroutines();
            ResetPresentation();
        }

        private void ResetPresentation()
        {
            WorldItemFoundScreen screen = activeScreen;
            activeScreen = null;
            if (screen != null) screen.Abandon();
            modal.Restore();
            if (lid != null) SetLidAngle(0f);
            RefreshMail();
        }
    }
}
