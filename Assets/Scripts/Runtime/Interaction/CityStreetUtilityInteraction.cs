using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class CityStreetUtilityInteraction : MonoBehaviour, IInteractable
    {
        public const string PhoneBoothPromptKey = "interaction.enter_phone_booth";
        public const string DumpsterPromptKey = "interaction.search_dumpster";
        public const string PhoneBoothPlaceholderKey = "city.phone_booth.placeholder";
        public const string DumpsterEmptyKey = "city.dumpster.placeholder";
        public const string DumpsterDepletedKey = "city.dumpster.searched";
        private CityGameRoot city;
        internal CityStreetUtilityModel Model { get; private set; }
        public CityStreetUtilityDock Dock { get; private set; }
        public string PromptKey => Dock.Kind == CityStreetUtilityKind.PhoneBooth ? PhoneBoothPromptKey : DumpsterPromptKey;
        public Vector3 InteractionPosition => Dock.StandPosition;

        public void Initialize(CityStreetUtilityDock dock, CityGameRoot owner = null, CityStreetUtilityModel model = null)
        {
            if (!dock.IsPresent) throw new System.ArgumentException("Utility needs an exterior standing point.");
            Dock = dock;
            city = owner;
            Model = model;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (!city || !Model || !isActiveAndEnabled || city.IsDormant || interactor != city.Player.Interactor ||
                !interactor || !interactor.InputEnabled || BarMinigameModalLock.IsAnyLocked ||
                SceneTransitionService.IsTransitioning) return false;
            Vector3 delta = interactor.transform.position - Dock.StandPosition;
            if (Mathf.Abs(delta.y) > .32f || delta.sqrMagnitude > 1.65f*1.65f ||
                Vector3.Dot(delta,Dock.Facing) > .24f) return false;
            Vector3 from = interactor.transform.position + Vector3.up*.84f;
            Vector3 to = Dock.StandPosition + Vector3.up*.84f;
            return !Physics.Linecast(from, to, PlayerInteractor.InteractionLayerMask, QueryTriggerInteraction.Ignore);
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor)) return;
            if (Dock.Kind == CityStreetUtilityKind.Dumpster &&
                GameSessionState.GetDumpsterSearchState(Dock.Id, GameSessionState.GameDayIndex) ==
                    GameSessionState.DumpsterSearchState.Depleted)
            {
                interactor.ShowFeedback(DumpsterDepletedKey, 1.6f);
                return;
            }
            var session = interactor.GetComponent<CityStreetUtilitySession>();
            if (!session) session = interactor.gameObject.AddComponent<CityStreetUtilitySession>();
            session.Begin(city, this);
        }

        private void OnDisable()
        {
            if (city && city.Player.GameObject && city.Player.GameObject.TryGetComponent<CityStreetUtilitySession>(out var session))
                session.Release(this);
        }
    }
}
